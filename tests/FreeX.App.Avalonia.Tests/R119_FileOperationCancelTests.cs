using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;

using FluentAssertions;

using Free.Shared.IO;
using FreeX.App.Services;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression coverage for R119-avalonia-file-op-cancel (src/FreeX.App.Avalonia/MainWindow.cs).
///
/// Before this fix, <c>SaveWorkbookToTargetAsync</c> passed <c>CancellationToken.None</c> literally
/// to <c>WorkbookSaveService.SaveAsync</c>, and <c>OpenWorkbookFromTargetAsync</c> passed no token at
/// all to <c>WorkbookOpenService.LoadAsync</c> (defaulting to <c>CancellationToken.None</c>) -- even
/// though both service methods genuinely observe a real token at multiple points. There was also no
/// Cancel affordance anywhere in the Avalonia shell. The WPF host had a live cancellation source
/// and a status-bar Cancel button wired to it, so a user on Linux/macOS had strictly less capability
/// than on Windows for the identical action.
///
/// The host now acquires the token from the shared <see cref="FileOperationCancellationSession"/>,
/// wires the status-bar Cancel button to that session, and threads the lease token into both
/// <c>OpenWorkbookFromTargetAsync</c> and <c>SaveWorkbookToTargetAsync</c>.
///
/// These tests drive the REAL production entry points directly via the internal test seams
/// <c>OpenWorkbookFromTargetAsyncForTest</c>/<c>SaveWorkbookToTargetAsyncForTest</c> (mirroring
/// R116's convention), and request cancellation via <c>RaiseFileOperationCancelButtonClickForTest</c>,
/// which drives the exact same <c>FileOperationCancelButton_Click</c> handler a real pointer click on
/// the status-bar Cancel button would. The fixture file on disk is produced by
/// <see cref="XlsxFileAdapter"/> itself (our own writer), never hand-authored XML, so the round-trip
/// is real (ROUND-TRIP FIXTURE RULE).
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R119_FileOperationCancelTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task SaveWorkbookToTargetAsync_CanceledBeforeWrite_AbortsAndLeavesDiskUntouched()
    {
        await Session.Dispatch(async () =>
        {
            using var tempDir = new R119TempDirectory();
            var path = Path.Combine(tempDir.Path, "Book1.xlsx");
            var adapter = new XlsxFileAdapter();
            var format = new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true);

            WriteWorkbook(adapter, path, "before-save");
            var originalBytes = File.ReadAllBytes(path);

            var window = new MainWindow([]);
            try
            {
                var target = new WorkbookOpenTarget(path, adapter, ".xlsx", format);
                await window.OpenWorkbookFromTargetAsyncForTest(target);
                window.Session.MarkDirtyForRecovery();

                var saveTarget = new FileSaveTarget(path, adapter);
                // Do not await yet: the shared session lease begins synchronously before the very
                // first await inside SaveWorkbookToTargetAsync, so by the time this call returns a
                // Task the cancellation session is already active -- mirroring how
                // fast the real Cancel button can be clicked relative to a save that just started.
                var saveTask = window.SaveWorkbookToTargetAsyncForTest(saveTarget);

                window.FileOperationCancelButtonVisibleForTest.Should().BeTrue(
                    "the status-bar Cancel affordance must be visible for the whole duration of an " +
                    "in-flight save -- before this fix there was no such affordance at all");
                window.FileOperationCancellationActiveForTest.Should().BeTrue(
                    "the shared cancellation session must own the in-flight save");

                window.RaiseFileOperationCancelButtonClickForTest();

                var result = await saveTask;

                result.Should().BeFalse(
                    "a canceled save must report failure, not silently succeed");
                File.ReadAllBytes(path).Should().Equal(originalBytes,
                    "before the fix, SaveAsync was always called with CancellationToken.None, so " +
                    "cancellation had zero effect and the save proceeded to completion regardless -- " +
                    "after the fix, the token is genuinely observed and the write is aborted before " +
                    "the temp file ever replaces the original");
                window.FileOperationCancelButtonEnabledForTest.Should().BeFalse(
                    "clicking Cancel must immediately disable the button so a second click can't " +
                    "fault on an already-canceled source");
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
    public async Task OpenWorkbookFromTargetAsync_CanceledBeforeLoad_AbortsWithoutReplacingSession()
    {
        await Session.Dispatch(async () =>
        {
            using var tempDir = new R119TempDirectory();
            var path = Path.Combine(tempDir.Path, "Book1.xlsx");
            var adapter = new XlsxFileAdapter();
            var format = new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true);

            WriteWorkbook(adapter, path, "cancel-target-marker");

            var window = new MainWindow([]);
            try
            {
                var target = new WorkbookOpenTarget(path, adapter, ".xlsx", format);
                var originalWorkbookId = window.Session.Workbook.Id;

                // Do not await yet, for the same reason as the save test above -- Begin runs before
                // the first await.
                var openTask = window.OpenWorkbookFromTargetAsyncForTest(target);

                window.FileOperationCancelButtonVisibleForTest.Should().BeTrue(
                    "the status-bar Cancel affordance must be visible for the whole duration of an " +
                    "in-flight open");
                window.FileOperationCancellationActiveForTest.Should().BeTrue();

                window.RaiseFileOperationCancelButtonClickForTest();
                await openTask;

                window.Session.Workbook.Id.Should().Be(originalWorkbookId,
                    "before the fix, LoadAsync was always called with a defaulted " +
                    "CancellationToken.None (no token argument at all), so cancellation had zero " +
                    "effect and the open proceeded to completion, replacing the session regardless -- " +
                    "after the fix, the token is genuinely observed and the open aborts before " +
                    "ReplaceSession ever runs");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SaveWorkbookToTargetAsync_NotCanceled_StillSavesNormally_CancelButtonHidesAfterward()
    {
        // Sibling no-regression case: an ordinary save that nobody cancels must keep working exactly
        // as before this fix, and the new Cancel affordance must not leak into the idle state.
        await Session.Dispatch(async () =>
        {
            using var tempDir = new R119TempDirectory();
            var path = Path.Combine(tempDir.Path, "Book1.xlsx");
            var adapter = new XlsxFileAdapter();
            var format = new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true);

            WriteWorkbook(adapter, path, "before-save");

            var window = new MainWindow([]);
            try
            {
                window.FileOperationCancelButtonVisibleForTest.Should().BeFalse(
                    "the Cancel button must stay hidden while idle (no open/save in flight)");

                var target = new WorkbookOpenTarget(path, adapter, ".xlsx", format);
                await window.OpenWorkbookFromTargetAsyncForTest(target);
                window.Session.MarkDirtyForRecovery();

                var saveTarget = new FileSaveTarget(path, adapter);
                var result = await window.SaveWorkbookToTargetAsyncForTest(saveTarget);

                result.Should().BeTrue(
                    "an uncanceled save must still succeed exactly as before this fix");
                window.FileOperationCancelButtonVisibleForTest.Should().BeFalse(
                    "once the save completes, the Cancel button must hide again");
                window.FileOperationCancellationActiveForTest.Should().BeFalse(
                    "the shared session must retire the save lease once it has finished");
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

    private sealed class R119TempDirectory : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "R119-" + Guid.NewGuid().ToString("N"));

        public R119TempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (!Directory.Exists(Path))
                return;

            // R119-appservices-open-cancel-eager (WorkbookOpenService.LoadAsync) deliberately
            // abandons -- rather than waits for -- its Inspecting/Parsing stage's background
            // thread-pool work once cancellation is observed, so the file this directory holds can
            // still be briefly open on another thread for a few milliseconds after
            // OpenWorkbookFromTargetAsync's own await has already returned canceled. That is by
            // design (an unresponsive/disconnected network path must not block Cancel from ever
            // taking effect) and is not a defect in this fix -- retry the cleanup rather than
            // asserting on it.
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    Directory.Delete(Path, recursive: true);
                    return;
                }
                catch (IOException) when (attempt < 20)
                {
                    Thread.Sleep(25);
                }
            }
        }
    }
}
