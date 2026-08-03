using System.IO;
using System.Reflection;
using System.Windows.Threading;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for the R120 finding (src/FreeX.Core.IO/XlsxWorksheetViewWriter.cs:110):
/// <see cref="FreeX.Core.Commands.WorksheetViewStateStore"/> exists precisely so each Excel
/// "View &gt; New Window" sibling keeps displaying its own remembered Freeze Panes/zoom/view-mode/
/// etc. even after a sibling window mutates the shared <see cref="Sheet"/> fields those per-window
/// snapshots shadow (see <c>GetEffectiveViewState</c>, MainWindow.Viewport.cs) -- but every writer
/// (e.g. <c>XlsxWorksheetViewWriter.UpdateSheetView</c>) only ever reads the shared
/// <see cref="Sheet"/> fields directly, with no path anywhere that reconciled the SAVING window's
/// own per-window snapshot back onto the shared model before serialization. So before this fix,
/// Ctrl+S from a window whose own Freeze Panes had diverged from the shared fields silently
/// persisted whichever sibling window's Freeze Panes last mutated them, not the saving window's own.
///
/// The fix adds <c>MainWindow.ReconcileViewStateForSave</c> (MainWindow.Viewport.cs), called from
/// the real save entry point <c>SaveWorkbookToTargetAsync</c> (MainWindow.Backstage.cs) immediately
/// before handing the workbook to <c>SaveWorkbookWriter</c>/<c>WorkbookSaveService</c>/
/// <c>IFileAdapter.Save</c>.
///
/// These tests drive the REAL production save entry point <c>SaveWorkbookToTargetAsync</c> via
/// reflection (the same seam <see cref="R115_SaveGateSiblingWindowRaceTests"/> uses), with a
/// <see cref="TestFileAdapter"/> that records the <see cref="Sheet"/> state actually handed to
/// <c>IFileAdapter.Save</c> -- the real product boundary this finding is about -- rather than
/// asserting on a hand-built model. The two-sibling-window setup mirrors
/// <see cref="R89_FreezeSplitPerWindowTests"/>.
/// </summary>
public sealed class R120_SavePersistsSavingWindowsOwnViewStateTests
{
    private sealed class SaveTempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());

        public SaveTempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            const int attempts = 60;
            for (var attempt = 1; Directory.Exists(Path); attempt++)
            {
                try
                {
                    Directory.Delete(Path, recursive: true);
                }
                catch (IOException) when (attempt < attempts)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    /// <summary>
    /// The primary regression scenario: window1 sets its own Freeze Panes, then window2 (a "New
    /// Window" sibling sharing the exact same document) changes the SAME shared Sheet's Freeze
    /// Panes to a different value. Window1 still DISPLAYS its own freeze (the R89 fix already
    /// covers that), but before the R120 fix, saving from window1 handed the writer whatever
    /// window2's later command left in the shared Sheet fields instead.
    /// </summary>
    [Fact]
    public void SaveWorkbookToTargetAsync_PersistsSavingWindowsOwnFreezePanes_NotSiblingsLaterOverwrite()
    {
        StaTestRunner.Run(() =>
        {
            using var temp = new SaveTempDirectory();
            var savePath = System.IO.Path.Combine(temp.Path, "Shared.fxjson");

            var (window1, workbook) = R49MainWindowTestHarness.CreateWindow();
            var (window2, _) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheetId = GetCurrentSheetId(window1);
                AdoptSameDocument(window2, workbook, sheetId);

                // Window 2 renders first, seeding its own per-window store from the (still
                // unfrozen) shared FrozenRows/FrozenCols -- mirrors R89_FreezeSplitPerWindowTests.
                R49MainWindowTestHarness.Invoke(window2, "UpdateViewport");

                InvokeSetFreezePanes(window1, frozenRows: 3, frozenCols: 2);

                var sheet = workbook.GetSheet(sheetId)!;
                sheet.FrozenRows.Should().Be(3u);
                sheet.FrozenCols.Should().Be(2u);

                // Simulate a sibling window's command mutating the SAME shared Sheet's Freeze Panes
                // (exactly what SetFreezePanesCommand.Apply does when window1's own SetFreezePanes
                // executes it via its CommandBus above) -- applied directly against the real shared
                // Workbook object every "New Window" sibling shares, standing in for a second
                // MainWindow so the test does not depend on wiring a second window's CommandBus to
                // the shared WorkbookRef, which R49MainWindowTestHarness does not support.
                new FreeX.Core.Commands.SetFreezePanesCommand(sheetId, frozenRows: 7, frozenCols: 6)
                    .Apply(new TestCommandContext(workbook))
                    .Success.Should().BeTrue();

                sheet.FrozenRows.Should().Be(7u, "the sibling's command last mutated the shared field");
                sheet.FrozenCols.Should().Be(6u);

                window1.SheetGrid.Viewport!.FrozenPanes!.Rows.Should().Be(3u,
                    "window1 still displays its OWN freeze, unaffected by the sibling's later change (R89)");
                window1.SheetGrid.Viewport!.FrozenPanes!.Cols.Should().Be(2u);

                // Window 2 never froze panes itself either, so it must keep showing none (R89),
                // confirming the sibling mutation above did not leak into ITS display any more than
                // a real second window's command would.
                R49MainWindowTestHarness.Invoke(window2, "UpdateViewport");
                window2.SheetGrid.Viewport?.FrozenPanes.Should().BeNull();

                (uint FrozenRows, uint FrozenCols)? captured = null;
                var adapter = new TestFileAdapter(
                    save: (savedWorkbook, _) =>
                    {
                        var savedSheet = savedWorkbook.GetSheet(sheetId)!;
                        captured = (savedSheet.FrozenRows, savedSheet.FrozenCols);
                    });

                var saveTask = InvokeSaveWorkbookToTargetAsync(window1, new FileSaveTarget(savePath, adapter));
                WaitForSaveResult(saveTask).Should().BeTrue();

                captured.Should().NotBeNull("the writer must have been invoked");
                captured!.Value.FrozenRows.Should().Be(3u,
                    "Ctrl+S from window1 must persist window1's OWN displayed Freeze Panes, not " +
                    "window2's later overwrite of the shared Sheet fields");
                captured.Value.FrozenCols.Should().Be(2u);
            }
            finally
            {
                R49MainWindowTestHarness.Close(window1);
                R49MainWindowTestHarness.Close(window2);
            }
        });
    }

    /// <summary>
    /// No-regression sibling: a single window (no sibling in play) must still persist whatever
    /// Freeze Panes IT set, and reconciling before save must never disturb cell data -- the fix
    /// must only ever touch the nine view-state fields, never the document's actual content.
    /// </summary>
    [Fact]
    public void SaveWorkbookToTargetAsync_SingleWindow_StillPersistsItsOwnFreezePanesAndCellData()
    {
        StaTestRunner.Run(() =>
        {
            using var temp = new SaveTempDirectory();
            var savePath = System.IO.Path.Combine(temp.Path, "Solo.fxjson");

            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheetId = GetCurrentSheetId(window);
                InvokeSetFreezePanes(window, frozenRows: 4, frozenCols: 1);

                var sheet = workbook.GetSheet(sheetId)!;
                sheet.SetCell(new CellAddress(sheetId, 5, 5), new NumberValue(99));

                (uint FrozenRows, uint FrozenCols, double CellValue)? captured = null;
                var adapter = new TestFileAdapter(
                    save: (savedWorkbook, _) =>
                    {
                        var savedSheet = savedWorkbook.GetSheet(sheetId)!;
                        var cellValue = ((NumberValue)savedSheet.GetCell(5, 5)!.Value).Value;
                        captured = (savedSheet.FrozenRows, savedSheet.FrozenCols, cellValue);
                    });

                var saveTask = InvokeSaveWorkbookToTargetAsync(window, new FileSaveTarget(savePath, adapter));
                WaitForSaveResult(saveTask).Should().BeTrue();

                captured.Should().NotBeNull();
                captured!.Value.FrozenRows.Should().Be(4u, "the ordinary single-window save path must be unaffected");
                captured.Value.FrozenCols.Should().Be(1u);
                captured.Value.CellValue.Should().Be(99, "reconciliation must never touch cell data");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    private static void AdoptSameDocument(MainWindow window, Workbook workbook, SheetId sheetId)
    {
        typeof(MainWindow).GetField("_workbook", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(window, workbook);
        typeof(MainWindow).GetField("_currentSheetId", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(window, sheetId);
    }

    private static SheetId GetCurrentSheetId(MainWindow window) =>
        (SheetId)typeof(MainWindow).GetField("_currentSheetId", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window)!;

    private static void InvokeSetFreezePanes(MainWindow window, uint frozenRows, uint frozenCols) =>
        R49MainWindowTestHarness.Invoke(window, "SetFreezePanes", frozenRows, frozenCols);

    private static Task<bool> InvokeSaveWorkbookToTargetAsync(MainWindow window, FileSaveTarget target)
    {
        var method = typeof(MainWindow).GetMethod(
            "SaveWorkbookToTargetAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull("SaveWorkbookToTargetAsync is the real save entry point this finding concerns");
        return (Task<bool>)method!.Invoke(window, [target])!;
    }

    /// <summary>
    /// Blocks (via <see cref="DispatcherFrame"/> pumping) until <paramref name="task"/> completes,
    /// without deadlocking on a continuation that resumes via the STA dispatcher's
    /// <c>SynchronizationContext</c> (mirrors <see cref="R115_SaveGateSiblingWindowRaceTests"/>).
    /// </summary>
    private static bool WaitForSaveResult(Task<bool> task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            task.ContinueWith(
                _ => frame.Continue = false,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.FromCurrentSynchronizationContext());
            Dispatcher.PushFrame(frame);
        }

        return task.GetAwaiter().GetResult();
    }
}
