using System.IO;
using System.Reflection;
using System.Windows.Threading;
using FluentAssertions;
using Free.Shared.AppServices;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for the R138 finding (src/FreeX.App.Services/WorkbookSession.cs:476):
/// unlike the sibling per-window fields <c>MainWindow.ReconcileViewStateForSave</c>
/// (MainWindow.Viewport.cs) already reconciles via <c>_worksheetViewStates</c> (zoom, view mode,
/// gridlines, headings, show-formulas, freeze panes, split -- R120), the active cell/selection was
/// never reconciled onto the shared <see cref="Sheet"/> before save. <c>SetActiveCell</c> and its
/// siblings (MainWindow.Selection.cs) write the active cell straight onto
/// <see cref="Sheet.ActiveRow"/>/<see cref="Sheet.ActiveCol"/> the instant the selection changes --
/// and every "View &gt; New Window" sibling shares the very same <see cref="Sheet"/> object, so
/// whichever sibling's selection changed MOST RECENTLY owned the persisted active cell, regardless
/// of which sibling actually performed the save.
///
/// The fix extends <c>MainWindow.ReconcileViewStateForSave</c> to also push this window's own live
/// active cell (<c>_selectionAnchor</c> for <c>_currentSheetId</c>) and its remembered per-sheet
/// selections (<c>_worksheetSelections</c>, the WPF-host counterpart of
/// <see cref="WorksheetSelectionStore"/>) onto the shared <see cref="Sheet"/> fields immediately
/// before serialization.
///
/// These tests drive the REAL production save entry point <c>SaveWorkbookToTargetAsync</c> via
/// reflection (the same seam <see cref="R120_SavePersistsSavingWindowsOwnViewStateTests"/> uses),
/// with a <see cref="TestFileAdapter"/> that records the <see cref="Sheet"/> state actually handed
/// to <c>IFileAdapter.Save</c> -- the real product boundary this finding is about.
/// </summary>
public sealed class R138_SavePersistsSavingWindowsOwnActiveCellTests
{
    /// <summary>
    /// The primary regression scenario: window1 sets its own active cell, then window2 (a "New
    /// Window" sibling sharing the exact same document) moves the SAME shared Sheet's active cell
    /// to a different one. Before the R138 fix, saving from window1 handed the writer whatever
    /// window2's later selection left in the shared Sheet fields instead of window1's own.
    /// </summary>
    [Fact]
    public void SaveWorkbookToTargetAsync_PersistsSavingWindowsOwnActiveCell_NotSiblingsLaterOverwrite()
    {
        StaTestRunner.Run(() =>
        {
            using var temp = new TestTemporaryDirectory("FreeX.R138.Save-");
            var savePath = Path.Combine(temp.Path, "Shared.fxjson");

            var (window1, window2, workbook) = CreateSharedWindows();
            try
            {
                var sheetId = GetCurrentSheetId(window1);

                InvokeSetActiveCell(window1, new CellAddress(sheetId, 5, 2)); // B5

                var sheet = workbook.GetSheet(sheetId)!;
                sheet.ActiveRow.Should().Be(5u);
                sheet.ActiveCol.Should().Be(2u);

                // Window 2 independently moves ITS OWN active cell on the SAME shared Sheet --
                // exactly what a real second window clicking a different cell after window1's last
                // move does.
                InvokeSetActiveCell(window2, new CellAddress(sheetId, 26, 10)); // J26

                sheet.ActiveRow.Should().Be(26u, "window2's own selection last mutated the shared field");
                sheet.ActiveCol.Should().Be(10u);

                (uint Row, uint Col)? captured = null;
                var adapter = new TestFileAdapter(
                    save: (savedWorkbook, _) =>
                    {
                        var savedSheet = savedWorkbook.GetSheet(sheetId)!;
                        captured = (savedSheet.ActiveRow!.Value, savedSheet.ActiveCol!.Value);
                    });

                // #164 diagnostic: call the reconcile the save path calls, directly, and record
                // what it leaves on the shared Sheet. The save path runs it synchronously right
                // before handing the workbook to the writer (WorkbookSaveExecutionCoordinator.cs:153),
                // so if it produces 5 here but the writer still sees 26, the reconcile is NOT the
                // culprit and something rewrites the shared field between the two -- a different
                // bug from "the reconcile never ran", which is what the previous instrumentation
                // round left open. Running it twice is harmless: it only assigns fields, and the
                // save path will assign the same values again a moment later.
                R49MainWindowTestHarness.Invoke(window1, "ReconcileViewStateForSave");
                var afterReconcile = (sheet.ActiveRow, sheet.ActiveCol);

                var saveTask = InvokeSaveWorkbookToTargetAsync(window1, new FileSaveTarget(savePath, adapter));
                WaitForSaveResult(saveTask).Should().BeTrue();

                captured.Should().NotBeNull("the writer must have been invoked");

                // This assertion has failed intermittently under full-gate load and passes every
                // time in isolation, so the message carries the two pieces of window state that
                // decide it. ReconcileViewStateForSave writes the live anchor only when
                // _currentSheetId resolves to a sheet AND _selectionAnchor is set; if either is
                // off, the shared Sheet keeps whatever window2 wrote last and the row comes back
                // as 26. Printing both here means the next gate failure says which one it was
                // instead of only that the number was wrong.
                var reconcileState =
                    "window1._currentSheetId=" + GetCurrentSheetId(window1)
                    + " (test sheetId=" + sheetId + "), window1._selectionAnchor="
                    + DescribeSelectionAnchor(window1)
                    + ", sheet.Active after a direct ReconcileViewStateForSave=("
                    + afterReconcile.ActiveRow + "," + afterReconcile.ActiveCol + ")";

                captured!.Value.Row.Should().Be(5u,
                    "Ctrl+S from window1 must persist window1's OWN active cell, not window2's " +
                    "later overwrite of the shared Sheet fields [" + reconcileState + "]");
                captured.Value.Col.Should().Be(2u);
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
    /// active cell IT set, and reconciling before save must never disturb cell data.
    /// </summary>
    [Fact]
    public void SaveWorkbookToTargetAsync_SingleWindow_StillPersistsItsOwnActiveCellAndCellData()
    {
        StaTestRunner.Run(() =>
        {
            using var temp = new TestTemporaryDirectory("FreeX.R138.Save-");
            var savePath = Path.Combine(temp.Path, "Solo.fxjson");

            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheetId = GetCurrentSheetId(window);
                InvokeSetActiveCell(window, new CellAddress(sheetId, 9, 4));

                var sheet = workbook.GetSheet(sheetId)!;
                sheet.SetCell(new CellAddress(sheetId, 5, 5), new NumberValue(99));

                (uint Row, uint Col, double CellValue)? captured = null;
                var adapter = new TestFileAdapter(
                    save: (savedWorkbook, _) =>
                    {
                        var savedSheet = savedWorkbook.GetSheet(sheetId)!;
                        var cellValue = ((NumberValue)savedSheet.GetCell(5, 5)!.Value).Value;
                        captured = (savedSheet.ActiveRow!.Value, savedSheet.ActiveCol!.Value, cellValue);
                    });

                var saveTask = InvokeSaveWorkbookToTargetAsync(window, new FileSaveTarget(savePath, adapter));
                WaitForSaveResult(saveTask).Should().BeTrue();

                captured.Should().NotBeNull();
                captured!.Value.Row.Should().Be(9u, "the ordinary single-window save path must be unaffected");
                captured.Value.Col.Should().Be(4u);
                captured.Value.CellValue.Should().Be(99, "reconciliation must never touch cell data");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    private static (MainWindow Primary, MainWindow Secondary, Workbook Workbook) CreateSharedWindows()
    {
        var initialWorkbook = new Workbook("Book1");
        initialWorkbook.AddSheet("Sheet1");
        var workbookRef = new WorkbookRef { Current = initialWorkbook };
        var registry = new WorkbookWindowRegistry();
        var documentState = new WorkbookDocumentState();
        var recalcEngine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        var commandBus = new CommandBus(_ => new TestCommandContext(workbookRef.Current));

        var primary = CreateSharedWindow(
            workbookRef,
            registry,
            documentState,
            recalcEngine,
            commandBus);
        primary.Show();
        R49MainWindowTestHarness.PumpDispatcher();

        var secondary = CreateSharedWindow(
            workbookRef,
            registry,
            documentState,
            recalcEngine,
            commandBus,
            primary.Session.CreateSiblingView(600, 800));
        secondary.Show();
        R49MainWindowTestHarness.PumpDispatcher();

        return (primary, secondary, workbookRef.Current);
    }

    private static MainWindow CreateSharedWindow(
        WorkbookRef workbookRef,
        WorkbookWindowRegistry registry,
        WorkbookDocumentState documentState,
        RecalcEngine recalcEngine,
        ICommandBus commandBus,
        WorkbookSession? session = null) =>
        new(
            NullLogger<MainWindow>.Instance,
            new ViewportService(),
            commandBus,
            recalcEngine,
            [],
            workbookRef,
            workbookRef.Current,
            new R138UserMessageService(),
            documentState,
            windowRegistry: registry,
            workbookSession: session);

    private sealed class R138UserMessageService : IUserMessageService
    {
        public void ShowError(string message, string title = "Error") { }
        public void ShowWarning(string message, string title = "Warning") { }
        public void ShowInfo(string message, string title = "Information") { }
        public bool AskYesNo(string message, string title = "Confirm") => false;

        public UserMessageResult ShowMessage(
            string message,
            string title,
            UserMessageButtons buttons,
            UserMessageIcon icon) => UserMessageResult.Ok;
    }

    private static SheetId GetCurrentSheetId(MainWindow window) =>
        (SheetId)typeof(MainWindow).GetField("_currentSheetId", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window)!;

    /// <summary>Renders window's private _selectionAnchor for a failure message, or "null".</summary>
    private static string DescribeSelectionAnchor(MainWindow window)
    {
        // _selectionAnchor is a PROPERTY (MainWindow.xaml.cs:120) whose setter also mirrors onto
        // SheetGrid.ActiveCell; the storage is _selectionAnchorField. Reading the property name as
        // a field returns null and throws here, which is how this helper failed the first time.
        var field = typeof(MainWindow)
            .GetField("_selectionAnchorField", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field is null)
            return "<no _selectionAnchorField on MainWindow>";

        var value = field.GetValue(window);
        return value is CellAddress cell
            ? "(" + cell.Row + "," + cell.Col + ") on sheet " + cell.Sheet
            : "null";
    }

    private static void InvokeSetActiveCell(MainWindow window, CellAddress address) =>
        R49MainWindowTestHarness.Invoke(window, "SetActiveCell", address);

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
    /// <c>SynchronizationContext</c> (mirrors <see cref="R120_SavePersistsSavingWindowsOwnViewStateTests"/>).
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
