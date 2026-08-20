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
/// Regression coverage for shared-progress-reporting F2 (src/FreeX.App.Avalonia/MainWindow.cs): the
/// Avalonia shell's Open/Save progress callbacks discarded the numeric <c>Percent</c> field the
/// shared open/save pipeline already computes (the same value that drives the WPF host's
/// StatusSaveProgressBar via BackstageProgressOverlayBinder) -- only the short cycling phase text
/// (<c>WorkbookProgressTextFormatter...Detail</c>) was ever applied to a visual. There was no
/// ProgressBar (or any percent-driven control) anywhere in the file, so a large Open/Save on
/// Linux/macOS showed strictly less information than the identical operation on Windows.
///
/// These tests drive the REAL production entry points directly via the internal test seams
/// <c>OpenWorkbookFromTargetAsyncForTest</c>/<c>SaveWorkbookToTargetAsyncForTest</c> (mirroring
/// R119's convention) against a real round-tripped fixture (produced by <see cref="XlsxFileAdapter"/>
/// itself, never hand-authored XML), and inspect the new status-bar progress bar through test-only
/// seams that read the real control -- not a duplicate calculation.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class SharedProgressReportingF2StatusBarProgressBarTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task SaveWorkbookToTargetAsync_ReportsNumericProgress_OnTheStatusBarProgressBar()
    {
        await Session.Dispatch(async () =>
        {
            using var tempDir = new F2TempDirectory();
            var path = Path.Combine(tempDir.Path, "Book1.xlsx");
            var adapter = new XlsxFileAdapter();

            WriteWorkbook(adapter, path, "before-save");

            var window = new MainWindow([]);
            try
            {
                var target = new WorkbookOpenTarget(
                    path, adapter, ".xlsx", new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true));
                await window.OpenWorkbookFromTargetAsyncForTest(target);
                window.Session.MarkDirtyForRecovery();

                // NOTE: no "hidden while idle" check here -- OpenWorkbookFromTargetAsyncForTest calls
                // straight into the inner OpenWorkbookFromTargetAsync, which (by design, see its own
                // finally-block comment) leaves the Cancel button/progress bar's last-applied visible
                // state stale until the OUTER OpenWorkbookAsync/OpenWorkbookPathAsync's own
                // UpdateSaveButton call -- which this direct test seam deliberately bypasses, exactly
                // as R119_FileOperationCancelTests' own save test does after the same kind of direct
                // Open call.

                var saveTarget = new FileSaveTarget(path, adapter);
                // Do not await yet, mirroring R119: the shared cancellation session (and this fix's
                // ExecutionStarting -> ApplyFileOperationProgress(null) call) both run synchronously
                // before the first genuine await, so the bar is already visible and indeterminate by
                // the time this call returns a Task.
                var saveTask = window.SaveWorkbookToTargetAsyncForTest(saveTarget);

                window.FileOperationProgressBarVisibleForTest.Should().BeTrue(
                    "the status-bar progress bar must be visible for the whole duration of an " +
                    "in-flight save, same gating as the Cancel button");
                window.FileOperationProgressBarIsIndeterminateForTest.Should().BeTrue(
                    "the initial 'preparing' phase has no known percent yet, so the bar must start " +
                    "indeterminate rather than pinned at a stale 0%");

                var result = await saveTask;
                result.Should().BeTrue("an uncancelled save must still succeed exactly as before this fix");

                // Core proof of F2: WorkbookSaveService's own final progress report before returning
                // (WorkbookSavePhase.Completed, Percent: 100 -- see WorkbookSaveService.cs) is a real,
                // non-null Percent computed by the identical shared pipeline the WPF host already
                // shows on its StatusSaveProgressBar. Before this fix, nothing in the Avalonia
                // callback ever read update.Percent, so the bar (which did not even exist) could
                // never have reached a determinate state -- this assertion is the one that fails
                // against the pre-fix code.
                window.FileOperationProgressBarIsIndeterminateForTest.Should().BeFalse(
                    "a save that completed must have applied at least one real numeric Percent, " +
                    "ending determinate rather than indeterminate");
                window.FileOperationProgressBarValueForTest.Should().BeGreaterThan(0,
                    "the applied Percent must be a genuine value from the shared pipeline, not the " +
                    "bar's untouched default of 0");

                window.FileOperationProgressBarVisibleForTest.Should().BeFalse(
                    "once the save completes, the progress bar must hide again, same lifetime as the " +
                    "Cancel button");
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
    public async Task OpenWorkbookFromTargetAsync_ReportsNumericProgress_OnTheStatusBarProgressBar()
    {
        await Session.Dispatch(async () =>
        {
            using var tempDir = new F2TempDirectory();
            var path = Path.Combine(tempDir.Path, "Book1.xlsx");
            var adapter = new XlsxFileAdapter();

            WriteWorkbook(adapter, path, "before-open");

            var window = new MainWindow([]);
            try
            {
                var target = new WorkbookOpenTarget(
                    path, adapter, ".xlsx", new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true));

                await window.OpenWorkbookFromTargetAsyncForTest(target);

                // Core proof of F2 for the Open path: WorkbookOpenService's final progress report
                // before returning (WorkbookOpenPhase.Calculating, Percent: 98 -- see
                // WorkbookOpenService.cs) is real and non-null. Before this fix the Avalonia Open
                // progress callback never read it.
                // NOTE: no "hides again" check here -- see the comment in the Save test above; a bare
                // OpenWorkbookFromTargetAsyncForTest call does not re-run UpdateSaveButton, so the
                // bar's IsVisible is left stale by design and is not what this fix is about.
                window.FileOperationProgressBarIsIndeterminateForTest.Should().BeFalse(
                    "an open that completed must have applied at least one real numeric Percent");
                window.FileOperationProgressBarValueForTest.Should().BeGreaterThan(0,
                    "the applied Percent must be a genuine value from the shared pipeline, not the " +
                    "bar's untouched default of 0");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    // ── Sibling/no-regression: the existing status text must keep working unchanged ────────────

    [Fact]
    public async Task SaveWorkbookToTargetAsync_StatusTextStillCyclesPhaseLabels_AlongsideTheNewBar()
    {
        await Session.Dispatch(async () =>
        {
            using var tempDir = new F2TempDirectory();
            var path = Path.Combine(tempDir.Path, "Book1.xlsx");
            var adapter = new XlsxFileAdapter();

            WriteWorkbook(adapter, path, "before-save");

            var window = new MainWindow([]);
            try
            {
                var target = new WorkbookOpenTarget(
                    path, adapter, ".xlsx", new FileFormatDescriptor(".xlsx", "XLSX Workbook", CanOpen: true, CanSave: true));
                await window.OpenWorkbookFromTargetAsyncForTest(target);
                window.Session.MarkDirtyForRecovery();

                var saveTarget = new FileSaveTarget(path, adapter);
                // Checked synchronously before awaiting (same reasoning as the indeterminate check
                // above): ExecutionStarting sets both the status text and the progress bar
                // synchronously, before the first real await, so this is deterministic.
                var saveTask = window.SaveWorkbookToTargetAsyncForTest(saveTarget);

                window.StatusTextForTest.Text.Should().NotBeNullOrEmpty(
                    "adding the progress bar must not remove the existing 'preparing' status text " +
                    "this fix leaves untouched");

                var result = await saveTask;
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

    private sealed class F2TempDirectory : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "F2-" + Guid.NewGuid().ToString("N"));

        public F2TempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (!Directory.Exists(Path))
                return;

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
