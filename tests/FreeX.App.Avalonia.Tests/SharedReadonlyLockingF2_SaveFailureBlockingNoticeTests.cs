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
/// Regression coverage for shared-readonly-locking F2 (src/FreeX.App.Avalonia/MainWindow.cs,
/// SaveWorkbookToTargetAsync). A save that reaches <c>WorkbookFileOperationOutcome.Failed</c> --
/// e.g. because the destination file is read-only and <c>File.Replace</c> throws
/// <c>UnauthorizedAccessException</c> -- used to be reported only by recoloring the status-bar text
/// via <c>ShowSaveIssue</c>, which a maximized/alt-tabbed user can walk past without noticing that
/// their edits never reached disk. The WPF host stops the user with a must-acknowledge MessageBox
/// for the identical outcome (MainWindow.Backstage.cs). The fix adds
/// <c>ShowBlockingSaveFailure</c>, which the Failed (and ExternalWriteConflict) branches now call
/// instead of <c>ShowSaveIssue</c> directly.
///
/// These tests drive the REAL production entry point directly via the internal test seam
/// <c>SaveWorkbookToTargetAsyncForTest</c> (mirroring R116/R128's convention), and observe the
/// blocking notice through <c>SaveFailureNoticeOverrideForTest</c> -- a headless test cannot safely
/// pump a real owned Avalonia dialog to completion, so this seam mirrors the existing
/// ExternallyModifiedFileOverwriteConfirmOverrideForTest/LossyFormatFeatureLossConfirmOverrideForTest
/// convention instead of asserting on a live window. The fixture file on disk is produced by
/// <see cref="XlsxFileAdapter"/> itself (our own writer), never hand-authored XML, so the round-trip
/// is real (ROUND-TRIP FIXTURE RULE). The read-only failure itself is a REAL OS-level failure (the
/// file's read-only attribute is set for real), not a simulated/injected exception.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class SharedReadonlyLockingF2_SaveFailureBlockingNoticeTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task SaveWorkbookToTargetAsync_ReadOnlyDestination_ShowsBlockingSaveFailureNotice()
    {
        await Session.Dispatch(async () =>
        {
            using var tempDir = new TestTemporaryDirectory("F2-");
            var path = Path.Combine(tempDir.Path, "Book1.xlsx");
            var adapter = new XlsxFileAdapter();
            var format = new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true);

            WriteWorkbook(adapter, path, "before-readonly-save-attempt");
            var originalBytes = File.ReadAllBytes(path);

            var window = new MainWindow([]);
            try
            {
                var target = new WorkbookOpenTarget(path, adapter, ".xlsx", format);
                await window.OpenWorkbookFromTargetAsyncForTest(target);

                // A clean (non-dirty) same-path save short-circuits as a no-op before ever reaching
                // WorkbookSaveService -- mark the session dirty so this Save actually attempts to
                // write, exactly like a real user who edited the workbook after opening it.
                window.Session.MarkDirtyForRecovery();

                // Make the OS genuinely refuse to overwrite the file -- this is the exact class the
                // finding names (File.Replace throws UnauthorizedAccessException).
                File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);

                string? noticeTitle = null;
                string? noticeMessage = null;
                UserMessageIcon? noticeIcon = null;
                window.SaveFailureNoticeOverrideForTest = (title, message, icon) =>
                {
                    noticeTitle = title;
                    noticeMessage = message;
                    noticeIcon = icon;
                };

                var saveTarget = new FileSaveTarget(path, adapter);
                var result = await window.SaveWorkbookToTargetAsyncForTest(saveTarget);

                result.Should().BeFalse(
                    "a save to a read-only destination must fail rather than silently succeed");

                noticeTitle.Should().NotBeNullOrWhiteSpace(
                    "before the fix, a failed save only recolored the status-bar text (ShowSaveIssue) " +
                    "-- it never routed through a must-acknowledge notice at all, so this seam would " +
                    "never have been invoked");
                noticeMessage.Should().NotBeNullOrWhiteSpace();
                noticeIcon.Should().Be(UserMessageIcon.Error,
                    "the WPF host shows this exact outcome with MessageBoxImage.Error " +
                    "(MainWindow.Backstage.cs)");

                File.ReadAllBytes(path).Should().Equal(originalBytes,
                    "a failed save must not have altered the on-disk content");
            }
            finally
            {
                // Clear the read-only attribute so the temp-directory cleanup below can delete it.
                if (File.Exists(path))
                    File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);

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
    public async Task SaveWorkbookToTargetAsync_WritableDestination_SavesNormally_NeverShowsFailureNotice()
    {
        // Sibling no-regression case: an ordinary, successful same-path save must keep working
        // exactly as before this fix -- no failure notice of any kind, and the new content actually
        // lands on disk.
        await Session.Dispatch(async () =>
        {
            using var tempDir = new TestTemporaryDirectory("F2-");
            var path = Path.Combine(tempDir.Path, "Book1.xlsx");
            var adapter = new XlsxFileAdapter();
            var format = new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true);

            WriteWorkbook(adapter, path, "before-save");

            var window = new MainWindow([]);
            try
            {
                var target = new WorkbookOpenTarget(path, adapter, ".xlsx", format);
                await window.OpenWorkbookFromTargetAsyncForTest(target);

                window.Session.MarkDirtyForRecovery();

                var noticeShown = false;
                window.SaveFailureNoticeOverrideForTest = (_, _, _) => noticeShown = true;

                var saveTarget = new FileSaveTarget(path, adapter);
                var result = await window.SaveWorkbookToTargetAsyncForTest(saveTarget);

                result.Should().BeTrue(
                    "a save to a normal writable file must still succeed exactly as before this fix");
                noticeShown.Should().BeFalse(
                    "the blocking failure notice must only fire for a genuinely failed save outcome");
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
}
