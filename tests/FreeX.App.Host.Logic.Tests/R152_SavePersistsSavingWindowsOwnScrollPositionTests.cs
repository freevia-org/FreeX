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
/// Regression coverage for the freex-freeze-split F2 finding
/// (src/FreeX.App.Host/MainWindow.Viewport.cs:437): unlike every other field
/// <c>MainWindow.ReconcileViewStateForSave</c> already reconciles onto the shared
/// <see cref="Sheet"/> before save (ViewMode/ZoomPercent/display toggles/Freeze-Split -- R120,
/// ActiveRow/ActiveCol -- R138), <see cref="Sheet.ViewTopRow"/>/<see cref="Sheet.ViewLeftCol"/> were
/// never reconciled. They are instead written unconditionally by <c>UpdateViewport</c> for
/// whichever "View &gt; New Window" sibling's viewport last refreshed -- so switching focus back to
/// a window that never re-ran its own <c>UpdateViewport</c> (e.g. Alt+Tab, without touching that
/// window's grid) and pressing Ctrl+S there persisted the OTHER window's scroll position instead of
/// the saving window's own.
///
/// These tests drive the real production save entry point <c>SaveWorkbookToTargetAsync</c> via
/// reflection, exactly like <see cref="R138_SavePersistsSavingWindowsOwnActiveCellTests"/> and
/// <see cref="R120_SavePersistsSavingWindowsOwnViewStateTests"/>, with a
/// <see cref="TestFileAdapter"/> that records the <see cref="Sheet"/> state actually handed to
/// <c>IFileAdapter.Save</c>.
/// </summary>
public sealed class R152_SavePersistsSavingWindowsOwnScrollPositionTests
{
    /// <summary>
    /// The primary regression scenario, matching the finding's user gesture exactly: window2 (a
    /// "New Window" sibling) scrolls, which runs window2's OWN <c>UpdateViewport</c> and
    /// unconditionally overwrites the shared <see cref="Sheet.ViewTopRow"/>/<see cref="Sheet.ViewLeftCol"/>.
    /// window1 never touches its own grid (no re-run of its own <c>UpdateViewport</c>) before
    /// Ctrl+S. Before the fix, saving from window1 persisted window2's scroll position; after the
    /// fix it must persist window1's own (still at row 1 / col 1).
    /// </summary>
    [Fact]
    public void SaveWorkbookToTargetAsync_PersistsSavingWindowsOwnScrollPosition_NotSiblingsLaterOverwrite()
    {
        StaTestRunner.Run(() =>
        {
            using var temp = new TestTemporaryDirectory("FreeX.R152.Save-");
            var savePath = Path.Combine(temp.Path, "Shared.fxjson");

            var (window1, window2, workbook) = CreateSharedWindows();
            try
            {
                var sheetId = GetCurrentSheetId(window1);
                var sheet = workbook.GetSheet(sheetId)!;

                // The WPF host caps each ScrollBar's Maximum to the sheet's used range + a small
                // buffer (UpdateScrollbarMaximums, MainWindow.Viewport.cs:1228-1253), exactly like
                // Excel does on a mostly-empty sheet -- so give the sheet a real used range before
                // scrolling, then refresh both windows' scrollbar Maximums against it (mirrors
                // R41_FreezePaneScrollPreservationTests' dataRowCount population).
                sheet.SetCell(new CellAddress(sheetId, 400, 60), new NumberValue(1));
                R49MainWindowTestHarness.Invoke(window1, "UpdateViewport");
                R49MainWindowTestHarness.Invoke(window2, "UpdateViewport");

                // Both windows render unscrolled at row 1 / col 1 immediately after creation.
                window1.VerticalScroll.Value.Should().Be(1);
                window1.HorizontalScroll.Value.Should().Be(1);
                sheet.ViewTopRow.Should().Be(1u);
                sheet.ViewLeftCol.Should().Be(1u);

                // Window 2 independently scrolls its OWN grid -- exactly what a real second
                // window's user scrolling to a different area does. This runs window2's own
                // UpdateViewport (wired via ScrollBar.ValueChanged, MainWindow.xaml.cs:433-434)
                // and unconditionally overwrites the shared Sheet fields.
                window2.VerticalScroll.Value = 300;
                window2.HorizontalScroll.Value = 50;

                sheet.ViewTopRow.Should().Be(300u, "window2's own scroll last mutated the shared field");
                sheet.ViewLeftCol.Should().Be(50u);

                // window1 never scrolled its own grid, so its own scrollbars are still at 1/1 --
                // the finding's "switch focus back without touching the grid" gesture.
                window1.VerticalScroll.Value.Should().Be(1);
                window1.HorizontalScroll.Value.Should().Be(1);

                (uint? TopRow, uint? LeftCol)? captured = null;
                var adapter = new TestFileAdapter(
                    save: (savedWorkbook, _) =>
                    {
                        var savedSheet = savedWorkbook.GetSheet(sheetId)!;
                        captured = (savedSheet.ViewTopRow, savedSheet.ViewLeftCol);
                    });

                var saveTask = InvokeSaveWorkbookToTargetAsync(window1, new FileSaveTarget(savePath, adapter));
                WaitForSaveResult(saveTask).Should().BeTrue();

                captured.Should().NotBeNull("the writer must have been invoked");
                captured!.Value.TopRow.Should().Be(1u,
                    "Ctrl+S from window1 must persist window1's OWN scroll position, not window2's " +
                    "later overwrite of the shared Sheet fields");
                captured.Value.LeftCol.Should().Be(1u);
            }
            finally
            {
                R49MainWindowTestHarness.Close(window1);
                R49MainWindowTestHarness.Close(window2);
            }
        });
    }

    /// <summary>
    /// No-regression sibling: a window that DID scroll itself must still persist its own scrolled
    /// position (the fix must not regress the ordinary matching case back to row 1 / col 1), and
    /// reconciling before save must never disturb cell data.
    /// </summary>
    [Fact]
    public void SaveWorkbookToTargetAsync_WindowThatScrolledItself_StillPersistsItsOwnScrollAndCellData()
    {
        StaTestRunner.Run(() =>
        {
            using var temp = new TestTemporaryDirectory("FreeX.R152.Save-");
            var savePath = Path.Combine(temp.Path, "Solo.fxjson");

            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheetId = GetCurrentSheetId(window);
                var sheet = workbook.GetSheet(sheetId)!;

                // See the sibling test above: give the sheet a real used range and refresh the
                // scrollbar Maximum against it before scrolling past the used-range-based cap.
                sheet.SetCell(new CellAddress(sheetId, 100, 20), new NumberValue(1));
                R49MainWindowTestHarness.Invoke(window, "UpdateViewport");

                window.VerticalScroll.Value = 42;
                window.HorizontalScroll.Value = 7;

                sheet.ViewTopRow.Should().Be(42u, "the ordinary single-window scroll path must be unaffected");
                sheet.ViewLeftCol.Should().Be(7u);
                sheet.SetCell(new CellAddress(sheetId, 5, 5), new NumberValue(99));

                (uint? TopRow, uint? LeftCol, double CellValue)? captured = null;
                var adapter = new TestFileAdapter(
                    save: (savedWorkbook, _) =>
                    {
                        var savedSheet = savedWorkbook.GetSheet(sheetId)!;
                        var cellValue = ((NumberValue)savedSheet.GetCell(5, 5)!.Value).Value;
                        captured = (savedSheet.ViewTopRow, savedSheet.ViewLeftCol, cellValue);
                    });

                var saveTask = InvokeSaveWorkbookToTargetAsync(window, new FileSaveTarget(savePath, adapter));
                WaitForSaveResult(saveTask).Should().BeTrue();

                captured.Should().NotBeNull();
                captured!.Value.TopRow.Should().Be(42u, "saving must persist this window's own scrolled position");
                captured.Value.LeftCol.Should().Be(7u);
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
            new R152UserMessageService(),
            documentState,
            windowRegistry: registry,
            workbookSession: session);

    private sealed class R152UserMessageService : IUserMessageService
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
