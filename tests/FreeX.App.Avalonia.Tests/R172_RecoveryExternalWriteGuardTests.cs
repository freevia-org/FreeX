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
/// Round-172 finding shared-autosave-recovery/F2: <c>MainWindow.LoadRecoverySnapshotAsync</c>
/// (src/FreeX.App.Avalonia/MainWindow.cs) calls <c>ReplaceSession</c>, which unconditionally resets
/// <c>_currentFileSourceLastWriteTimeUtc</c> to null (a fresh document identity), but recovery is
/// itself a form of "opening" an existing document -- unlike File &gt; New, the recovered session's
/// <c>CurrentFilePath</c> is the REAL original file, not untitled. Before this fix,
/// <c>LoadRecoverySnapshotAsync</c> never re-populated the guard afterward, so the external-
/// modification write-time guard stayed disarmed (null) after every recovery, and the first save
/// after recovering a crashed workbook could silently overwrite a copy of the original file that
/// changed on disk while the app was gone. Mirrors the WPF host's
/// <c>MainWindow.SetCurrentFilePathForRecovery</c> fix and FreeP's
/// <c>PresentationFileCommandSession.RestoreAutosaveSnapshot</c> fix for the identical class of bug.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R172_RecoveryExternalWriteGuardTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task LoadRecoverySnapshotAsync_ThenSave_PromptsAndRefusesWhenOriginalChangedOnDisk()
    {
        await Session.Dispatch(async () =>
        {
            using var tempDir = new TestTemporaryDirectory("R172-");
            var originalPath = Path.Combine(tempDir.Path, "Book1.xlsx");
            var adapter = new XlsxFileAdapter();

            WriteWorkbook(adapter, originalPath, "original-content");
            // Back-date so the external write below is guaranteed to observe a strictly later
            // on-disk timestamp even on filesystems with coarse write-time resolution.
            File.SetLastWriteTimeUtc(originalPath, DateTime.UtcNow.AddMinutes(-5));

            var snapshotPath = Path.Combine(tempDir.Path, "recovery-snapshot.fxl");
            WriteRecoverySnapshot(snapshotPath, "recovered-content");

            var window = new MainWindow([]);
            try
            {
                var loaded = await window.LoadRecoverySnapshotAsync(snapshotPath, originalPath);
                loaded.Should().BeTrue("a well-formed snapshot must load successfully");

                window.CurrentFileSourceLastWriteTimeUtcForTest.Should().NotBeNull(
                    "recovery must re-arm the external-modification guard from the ORIGINAL file's " +
                    "current on-disk write time -- before the fix, ReplaceSession's null reset was " +
                    "never re-populated for the recovery path, unlike a normal file open");
                window.CurrentFileSourceLastWriteTimeUtcForTest.Should().Be(
                    File.GetLastWriteTimeUtc(originalPath));

                // Simulate "another program changed the original file while FreeX was crashed/relaunching".
                await Task.Delay(50);
                WriteWorkbook(adapter, originalPath, "written-by-another-program");
                File.SetLastWriteTimeUtc(originalPath, DateTime.UtcNow.AddMinutes(5));

                var externalBytes = File.ReadAllBytes(originalPath);
                var externalWriteTimeUtc = File.GetLastWriteTimeUtc(originalPath);

                var promptCount = 0;
                window.ExternallyModifiedFileOverwriteConfirmOverrideForTest = _ =>
                {
                    promptCount++;
                    return UserMessageResult.No; // decline the overwrite
                };

                var saveTarget = new FileSaveTarget(originalPath, adapter);
                var result = await window.SaveWorkbookToTargetAsyncForTest(saveTarget);

                promptCount.Should().Be(
                    1,
                    "the guard must ask before overwriting a file that changed on disk since " +
                    "recovery -- before the fix the null baseline made the save think nothing had " +
                    "changed, so no prompt was ever raised");
                result.Should().BeFalse(
                    "declining the overwrite prompt must refuse the save, not silently proceed");
                File.ReadAllBytes(originalPath).Should().Equal(externalBytes,
                    "a declined overwrite must leave the externally-written content on disk untouched");
                File.GetLastWriteTimeUtc(originalPath).Should().Be(externalWriteTimeUtc);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            // See R116's identical note: HeadlessUnitTestSession.Dispatch's Func<Task> overload
            // swallows assertion failures thrown inside the delegate. This return forces the
            // compiler to pick the Func<Task<T>> overload, which propagates correctly.
            return true;
        }, CancellationToken.None);
    }

    /// <summary>
    /// Sibling no-regression: when the original file has NOT changed on disk since recovery, the
    /// guard must stay quiet and the save must proceed without any prompt.
    /// </summary>
    [Fact]
    public async Task LoadRecoverySnapshotAsync_ThenSave_SavesWithoutPromptWhenOriginalUnchanged()
    {
        await Session.Dispatch(async () =>
        {
            using var tempDir = new TestTemporaryDirectory("R172-");
            var originalPath = Path.Combine(tempDir.Path, "Book1.xlsx");
            var adapter = new XlsxFileAdapter();

            WriteWorkbook(adapter, originalPath, "original-content");

            var snapshotPath = Path.Combine(tempDir.Path, "recovery-snapshot.fxl");
            WriteRecoverySnapshot(snapshotPath, "recovered-content");

            var window = new MainWindow([]);
            try
            {
                var loaded = await window.LoadRecoverySnapshotAsync(snapshotPath, originalPath);
                loaded.Should().BeTrue();

                var promptCount = 0;
                window.ExternallyModifiedFileOverwriteConfirmOverrideForTest = _ =>
                {
                    promptCount++;
                    return UserMessageResult.Yes;
                };

                var saveTarget = new FileSaveTarget(originalPath, adapter);
                var result = await window.SaveWorkbookToTargetAsyncForTest(saveTarget);

                promptCount.Should().Be(0, "no external change occurred, so the guard must not prompt");
                result.Should().BeTrue();
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

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

    private static void WriteRecoverySnapshot(string snapshotPath, string marker)
    {
        var workbook = WorkbookFactory.Create();
        var sheet = workbook.GetSheet("Sheet1")!;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(marker));
        using var stream = File.Create(snapshotPath);
        new NativeJsonAdapter().Save(workbook, stream);
    }
}
