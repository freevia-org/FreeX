using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;

using FluentAssertions;

using Free.Shared.AppServices;
using Free.Shared.IO;
using FreeX.App.Services;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression coverage for R116-avalonia-external-modification-detection
/// (src/FreeX.App.Avalonia/MainWindow.cs). <c>WorkbookSaveService.SaveAsync</c> only refuses to
/// overwrite a file that changed on disk since it was opened when the caller passes
/// <c>expectedLastWriteTimeUtc</c> (sourced from <c>WorkbookOpenResult.SourceLastWriteTimeUtc</c>,
/// captured at open). The WPF host threads this through (<c>_currentFileSourceLastWriteTimeUtc</c>
/// in MainWindow.Backstage.cs) so a concurrent second writer (another FreeX/Excel instance, a
/// colleague on a shared drive) is detected and confirmed before Save silently overwrites their
/// changes -- but the Avalonia shell's <c>OpenWorkbookFromTargetAsync</c>/<c>SaveWorkbookToTargetAsync</c>
/// never captured or forwarded this snapshot at all, so a save on Linux/macOS always clobbered a
/// concurrently-modified file with no warning whatsoever. The fix adds the same
/// <c>_currentFileSourceLastWriteTimeUtc</c> field to the Avalonia <c>MainWindow</c>, populates it in
/// <c>OpenWorkbookFromTargetAsync</c>, threads it into <c>WorkbookSaveService.SaveAsync</c>, and
/// proactively confirms an overwrite (mirroring the WPF host's <c>ConfirmExternallyModifiedFileOverwrite</c>)
/// before any save work begins.
///
/// These tests drive the REAL production entry points directly via the internal test seams
/// <c>OpenWorkbookFromTargetAsyncForTest</c>/<c>SaveWorkbookToTargetAsyncForTest</c> (mirroring the
/// <c>ApplyReadOnlyRecommendedPromptIfNeededForTest</c> convention) -- the fixture file on disk is
/// produced by <see cref="XlsxFileAdapter"/> itself (our own writer), never hand-authored XML, so the
/// open/save round-trip is real.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R116_ExternalModificationDetectionTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task SaveWorkbookToTargetAsync_FileModifiedExternallySinceOpen_UserDeclines_LeavesDiskUntouched()
    {
        await Session.Dispatch(async () =>
        {
            using var tempDir = new R116TempDirectory();
            var path = Path.Combine(tempDir.Path, "Book1.xlsx");
            var adapter = new XlsxFileAdapter();
            var format = new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true);

            // Write the initial fixture through the real product writer (round-trip fixture, not
            // hand-authored XML) -- see the ROUND-TRIP FIXTURE RULE.
            WriteWorkbook(adapter, path, "before-external-edit");

            var window = new MainWindow([]);
            try
            {
                var target = new WorkbookOpenTarget(path, adapter, ".xlsx", format);
                await window.OpenWorkbookFromTargetAsyncForTest(target);

                window.CurrentFileSourceLastWriteTimeUtcForTest.Should().NotBeNull(
                    "opening a real file through the real open path must capture its write-time " +
                    "snapshot so a later Save to the same path can detect a concurrent second writer");

                // A clean (non-dirty) same-path save short-circuits as a no-op before ever reaching
                // WorkbookSaveService -- mark the session dirty so this Save actually attempts to
                // write, exactly like a real user who edited the workbook after opening it.
                window.Session.MarkDirtyForRecovery();

                // Simulate a concurrent second writer (another FreeX/Excel instance, or a colleague
                // on a shared drive): change both the file's content and its on-disk write time.
                WriteWorkbook(adapter, path, "written-by-another-program");
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));

                var externalBytes = File.ReadAllBytes(path);
                var externalWriteTimeUtc = File.GetLastWriteTimeUtc(path);

                // The user declines the "overwrite anyway?" confirm prompt.
                window.ExternallyModifiedFileOverwriteConfirmOverrideForTest = _ => UserMessageResult.No;

                var saveTarget = new FileSaveTarget(path, adapter);
                var result = await window.SaveWorkbookToTargetAsyncForTest(saveTarget);

                result.Should().BeFalse(
                    "Save must refuse to proceed once the user declines to overwrite a file that " +
                    "changed on disk since it was opened");
                File.ReadAllBytes(path).Should().Equal(externalBytes,
                    "before the fix, SaveAsync always passed a null expectedLastWriteTimeUtc, so the " +
                    "save proceeded unconditionally and silently clobbered the other writer's content");
                File.GetLastWriteTimeUtc(path).Should().Be(externalWriteTimeUtc,
                    "a declined save must not touch the file on disk at all");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }

            // IMPORTANT: HeadlessUnitTestSession.Dispatch's Func<Task> (non-generic) overload does
            // NOT propagate an exception/assertion failure thrown inside the delegate back to the
            // awaiting xUnit test -- it is silently swallowed and the test reports Passed regardless
            // of what happened inside. Only the Func<Task<T>> overload propagates correctly. This
            // return makes the compiler pick that overload; do not remove it.
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SaveWorkbookToTargetAsync_FileUnchangedSinceOpen_SavesNormally_NoConfirmPrompt()
    {
        // Sibling no-regression case: the ordinary same-path Save (nothing touched the file since
        // open) must keep working exactly as before this fix -- no confirm prompt, and the new
        // content actually lands on disk.
        await Session.Dispatch(async () =>
        {
            using var tempDir = new R116TempDirectory();
            var path = Path.Combine(tempDir.Path, "Book1.xlsx");
            var adapter = new XlsxFileAdapter();
            var format = new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true);

            WriteWorkbook(adapter, path, "before-save");

            var window = new MainWindow([]);
            try
            {
                var target = new WorkbookOpenTarget(path, adapter, ".xlsx", format);
                await window.OpenWorkbookFromTargetAsyncForTest(target);

                var confirmPromptShown = false;
                window.ExternallyModifiedFileOverwriteConfirmOverrideForTest = _ =>
                {
                    confirmPromptShown = true;
                    return UserMessageResult.No;
                };

                // A clean (non-dirty) same-path save short-circuits as a no-op before ever reaching
                // WorkbookSaveService -- mark the session dirty so this Save actually attempts to
                // write, exactly like a real user who edited the workbook after opening it.
                window.Session.MarkDirtyForRecovery();

                var saveTarget = new FileSaveTarget(path, adapter);
                var result = await window.SaveWorkbookToTargetAsyncForTest(saveTarget);

                result.Should().BeTrue(
                    "a Save to a file that has NOT changed on disk since it was opened must still " +
                    "succeed exactly as before this fix");
                confirmPromptShown.Should().BeFalse(
                    "the external-modification confirm prompt must only fire when the on-disk write " +
                    "time actually diverged from the snapshot captured at open");

                window.CurrentFileSourceLastWriteTimeUtcForTest.Should().Be(
                    File.GetLastWriteTimeUtc(path),
                    "a successful save must refresh the write-time snapshot to the file's new " +
                    "on-disk timestamp so the save's own write is never mistaken for an external " +
                    "modification next time");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }

            // See the sibling test above -- the Func<Task<T>> overload is required for Dispatch to
            // actually propagate an assertion failure to xUnit.
            return true;
        }, CancellationToken.None);
    }

    private static void WriteWorkbook(XlsxFileAdapter adapter, string path, string marker)
    {
        var workbook = WorkbookFactory.Create();
        var sheet = workbook.GetSheet("Sheet1")!;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(marker));
        using var stream = File.Create(path);
        adapter.Save(workbook, stream);
    }

    private sealed class R116TempDirectory : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "R116-" + Guid.NewGuid().ToString("N"));

        public R116TempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
