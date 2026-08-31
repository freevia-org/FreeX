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
/// Regression coverage for the shared-view-state F1 finding
/// (src/FreeX.App.Host/MainWindow.Viewport.cs:449): unlike <see cref="WorksheetViewStateStore"/>
/// (zoom/view-mode/freeze/split) and <see cref="WorksheetSelectionStore"/> (active cell), which
/// <c>ReconcileViewStateForSave</c> reconciles for EVERY sheet this window has ever visited, scroll
/// position (<see cref="Sheet.ViewTopRow"/>/<see cref="Sheet.ViewLeftCol"/>) was only reconciled for
/// the CURRENTLY displayed sheet (<c>_currentSheetId</c>). A sheet this window navigated away from
/// kept whatever scroll value a sibling "New Window" last happened to write onto the shared
/// <see cref="Sheet"/>, so Ctrl+S from this window could persist a sibling's scroll position for a
/// background sheet instead of this window's own last-remembered one.
///
/// These tests drive the real production save entry point <c>SaveWorkbookToTargetAsync</c> via
/// reflection, exactly like <see cref="R152_SavePersistsSavingWindowsOwnScrollPositionTests"/>.
/// </summary>
public sealed class R175_SavePersistsSavingWindowsOwnBackgroundSheetScrollTests
{
    /// <summary>
    /// The finding's exact user gesture: window1 scrolls Sheet1 to row 500, then switches to
    /// Sheet2. window2 (a "New Window" sibling) then switches to Sheet1 and scrolls it to row 20,
    /// overwriting the shared Sheet1.ViewTopRow/ViewLeftCol. Ctrl+S from window1 (now showing
    /// Sheet2) must still persist window1's OWN remembered Sheet1 scroll position (row 500), not
    /// window2's later overwrite.
    /// </summary>
    [Fact]
    public void SaveWorkbookToTargetAsync_PersistsSavingWindowsOwnScrollForBackgroundSheet_NotSiblingsLaterOverwrite()
    {
        StaTestRunner.Run(() =>
        {
            using var temp = new TestTemporaryDirectory("FreeX.R175.Save-");
            var savePath = Path.Combine(temp.Path, "Shared.fxjson");

            var (window1, window2, workbook) = CreateSharedWindows();
            try
            {
                var sheet1Id = GetCurrentSheetId(window1);
                var sheet1 = workbook.GetSheet(sheet1Id)!;
                var sheet2 = workbook.AddSheet("Sheet2");

                // Give both sheets a real used range before scrolling, mirroring R152's harness
                // (the WPF host caps each ScrollBar's Maximum to the sheet's used range).
                sheet1.SetCell(new CellAddress(sheet1Id, 900, 60), new NumberValue(1));
                sheet2.SetCell(new CellAddress(sheet2.Id, 900, 60), new NumberValue(1));
                R49MainWindowTestHarness.Invoke(window1, "UpdateViewport");
                R49MainWindowTestHarness.Invoke(window2, "UpdateViewport");

                // window1 scrolls Sheet1 (its own live scrollbars) to row 500/col 40, which runs
                // window1's own UpdateViewport and (correctly, even before the fix) writes that onto
                // the shared Sheet1.ViewTopRow/ViewLeftCol.
                window1.VerticalScroll.Value = 500;
                window1.HorizontalScroll.Value = 40;
                sheet1.ViewTopRow.Should().Be(500u);
                sheet1.ViewLeftCol.Should().Be(40u);

                // window1 switches to Sheet2 -- the finding's "navigated away from Sheet1" step.
                R49MainWindowTestHarness.Invoke(window1, "SelectSingleSheetTab", sheet2.Id);
                R49MainWindowTestHarness.Invoke(window1, "UpdateViewport");
                GetCurrentSheetId(window1).Should().Be(sheet2.Id);

                // window2 (still on Sheet1) now scrolls Sheet1 to a different position, overwriting
                // the shared Sheet1.ViewTopRow/ViewLeftCol via window2's OWN UpdateViewport.
                GetCurrentSheetId(window2).Should().Be(sheet1Id);
                window2.VerticalScroll.Value = 20;
                window2.HorizontalScroll.Value = 3;
                sheet1.ViewTopRow.Should().Be(20u, "window2's own scroll last mutated the shared field");
                sheet1.ViewLeftCol.Should().Be(3u);

                (uint? TopRow, uint? LeftCol)? capturedSheet1 = null;
                var adapter = new TestFileAdapter(
                    save: (savedWorkbook, _) =>
                    {
                        var savedSheet1 = savedWorkbook.GetSheet(sheet1Id)!;
                        capturedSheet1 = (savedSheet1.ViewTopRow, savedSheet1.ViewLeftCol);
                    });

                var saveTask = InvokeSaveWorkbookToTargetAsync(window1, new FileSaveTarget(savePath, adapter));
                WaitForSaveResult(saveTask).Should().BeTrue();

                capturedSheet1.Should().NotBeNull("the writer must have been invoked");
                capturedSheet1!.Value.TopRow.Should().Be(500u,
                    "Ctrl+S from window1 must persist window1's OWN remembered scroll position for " +
                    "Sheet1 (a background sheet it navigated away from), not window2's later overwrite " +
                    "of the shared Sheet fields");
                capturedSheet1.Value.LeftCol.Should().Be(40u);
            }
            finally
            {
                R49MainWindowTestHarness.Close(window1);
                R49MainWindowTestHarness.Close(window2);
            }
        });
    }

    /// <summary>
    /// No-regression sibling: a SINGLE window (no sibling ever mutates the shared fields) that
    /// visits two sheets with distinct scroll positions must still persist BOTH sheets' correct
    /// scroll positions -- the currently displayed sheet's (computed live from its own scrollbars,
    /// exactly as before the fix) and the background sheet's (now reconciled via the new per-window
    /// scroll-origin store). This also guards against the new background-sheet loop ever
    /// overwriting the current sheet with a stale dictionary entry.
    /// </summary>
    [Fact]
    public void SaveWorkbookToTargetAsync_SingleWindowTwoSheets_PersistsBothSheetsOwnScrollAndCellData()
    {
        StaTestRunner.Run(() =>
        {
            using var temp = new TestTemporaryDirectory("FreeX.R175.Save-");
            var savePath = Path.Combine(temp.Path, "Solo.fxjson");

            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet1Id = GetCurrentSheetId(window);
                var sheet1 = workbook.GetSheet(sheet1Id)!;
                var sheet2 = workbook.AddSheet("Sheet2");

                sheet1.SetCell(new CellAddress(sheet1Id, 100, 20), new NumberValue(1));
                sheet2.SetCell(new CellAddress(sheet2.Id, 300, 40), new NumberValue(1));
                R49MainWindowTestHarness.Invoke(window, "UpdateViewport");

                // Scroll Sheet1, then navigate away to Sheet2 and scroll it too.
                window.VerticalScroll.Value = 42;
                window.HorizontalScroll.Value = 7;
                sheet1.SetCell(new CellAddress(sheet1Id, 5, 5), new NumberValue(99));

                R49MainWindowTestHarness.Invoke(window, "SelectSingleSheetTab", sheet2.Id);
                R49MainWindowTestHarness.Invoke(window, "UpdateViewport");
                window.VerticalScroll.Value = 150;
                window.HorizontalScroll.Value = 25;
                GetCurrentSheetId(window).Should().Be(sheet2.Id);

                (uint? TopRow, uint? LeftCol, double CellValue)? capturedSheet1 = null;
                (uint? TopRow, uint? LeftCol)? capturedSheet2 = null;
                var adapter = new TestFileAdapter(
                    save: (savedWorkbook, _) =>
                    {
                        var savedSheet1 = savedWorkbook.GetSheet(sheet1Id)!;
                        var savedSheet2 = savedWorkbook.GetSheet(sheet2.Id)!;
                        var cellValue = ((NumberValue)savedSheet1.GetCell(5, 5)!.Value).Value;
                        capturedSheet1 = (savedSheet1.ViewTopRow, savedSheet1.ViewLeftCol, cellValue);
                        capturedSheet2 = (savedSheet2.ViewTopRow, savedSheet2.ViewLeftCol);
                    });

                var saveTask = InvokeSaveWorkbookToTargetAsync(window, new FileSaveTarget(savePath, adapter));
                WaitForSaveResult(saveTask).Should().BeTrue();

                capturedSheet1.Should().NotBeNull();
                capturedSheet1!.Value.TopRow.Should().Be(42u,
                    "the background sheet this window navigated away from must keep this window's own " +
                    "remembered scroll position");
                capturedSheet1.Value.LeftCol.Should().Be(7u);
                capturedSheet1.Value.CellValue.Should().Be(99, "reconciliation must never touch cell data");

                capturedSheet2.Should().NotBeNull();
                capturedSheet2!.Value.TopRow.Should().Be(150u,
                    "the currently displayed sheet must still persist its own live scroll position");
                capturedSheet2.Value.LeftCol.Should().Be(25u);
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
            new R175UserMessageService(),
            documentState,
            windowRegistry: registry,
            workbookSession: session);

    private sealed class R175UserMessageService : IUserMessageService
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
