using System.Windows;
using Free.Shared.AppServices;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Sibling-pickup regression for R128-cellscmds-formatcells-activecell-1 (see
/// <see cref="R128_FormatCellsDialogActiveCellSeedTests"/>): Format &gt; Lock Cell
/// (<c>FormatLockCellMenuItem_Click</c>) read the Locked state to flip from
/// <c>SelectedRange.Start</c> instead of the true active cell -- the identical bug pattern, in the
/// same file, one caller down. A backward-extended selection (click C5, Shift+click A1) must
/// toggle Locked based on C5 (the active cell), not A1 (range.Start).
/// </summary>
public sealed class R128_FormatLockCellActiveCellSeedTests
{
    private sealed class DocumentPlaceholderWindow(WorkbookId documentId) : IWorkbookWindow
    {
        public WorkbookId DocumentId { get; } = documentId;
        public void ApplyWindowTitleSuffix(string suffix) { }
        public void RefreshFromSharedWorkbook() { }
        public void RefreshTitleBar() { }
        public void ActivateWindow() { }
        public void SetWindowVisible(bool visible) { }
        public WorkbookScrollOffset GetScrollOffset() => default;
        public void SetScrollOffset(WorkbookScrollOffset offset) { }
        public void TileToWorkArea(Rect bounds) { }
        public void ApplyFormulaBarVisibility(bool visible) { }
        public void ApplySaveInProgress(bool inProgress) { }
    }

    private static (MainWindow Window, Workbook Workbook, Sheet Sheet) CreateAdoptedWindow()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var workbookRef = new WorkbookRef { Current = workbook };
        var registry = new WorkbookWindowRegistry();
        registry.Register(new DocumentPlaceholderWindow(workbook.Id));

        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var commandBus = new CommandBus(_ => new TestCommandContext(workbookRef.Current));
        var window = new MainWindow(
            NullLogger<MainWindow>.Instance,
            new ViewportService(),
            commandBus,
            new RecalcEngine(graph, evaluator),
            [],
            workbookRef,
            workbookRef.Current,
            NullUserMessageService.Instance,
            new WorkbookDocumentState(),
            windowRegistry: registry)
        {
            WindowState = WindowState.Normal,
            Width = 1280,
            Height = 720
        };

        window.Show();
        window.Activate();
        PumpDispatcher();

        return (window, workbook, sheet);
    }

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    [Fact]
    public void BackwardExtendedSelection_TogglesFromActiveCellNotStart() =>
        StaTestRunner.Run(() =>
    {
        var (window, workbook, sheet) = CreateAdoptedWindow();
        try
        {
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var c5 = new CellAddress(sheet.Id, 5, 3);

            // C5 (active cell) is explicitly UNLOCKED; A1 (range.Start) is left at the sheet
            // default, which is LOCKED (CellStyle.Default.Locked == true).
            var unlockedStyleId = workbook.RegisterStyle(new CellStyle { Locked = false });
            sheet.SetCell(c5, new Cell { Value = new TextValue("active"), StyleId = unlockedStyleId });
            sheet.SetCell(a1, new Cell { Value = new TextValue("start") });

            // Click C5, then Shift+click A1: active cell stays C5, SelectedRange normalizes to A1:C5.
            window.SheetGrid.ActiveCell = c5;
            var range = new GridRange(c5, a1);
            window.SheetGrid.SelectedRange = range;
            range.Start.Should().Be(a1, "GridRange normalizes its Start to the top-left corner");

            DialogSourceTestSupport.InvokePrivateHandler(window, "FormatLockCellMenuItem_Click");

            var resultStyle = workbook.GetStyle(sheet.GetCell(c5)!.StyleId);
            resultStyle.Locked.Should().BeTrue(
                "toggling must flip the ACTIVE cell's Locked state (C5 was unlocked, so it must become " +
                "locked), not range.Start's (A1 was locked, which would toggle everything to unlocked)");
        }
        finally
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(window);
            PumpDispatcher();
        }
    });

    /// <summary>No-regression sibling: a forward (top-left-anchored) selection -- where ActiveCell
    /// and SelectedRange.Start coincide -- must keep toggling from Start exactly as before.</summary>
    [Fact]
    public void ForwardSelection_TogglesFromStartAsBefore() =>
        StaTestRunner.Run(() =>
    {
        var (window, workbook, sheet) = CreateAdoptedWindow();
        try
        {
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var c5 = new CellAddress(sheet.Id, 5, 3);

            var unlockedStyleId = workbook.RegisterStyle(new CellStyle { Locked = false });
            sheet.SetCell(a1, new Cell { Value = new TextValue("active"), StyleId = unlockedStyleId });
            sheet.SetCell(c5, new Cell { Value = new TextValue("end") });

            // Click A1, then Shift+click C5: active cell stays A1, which is also SelectedRange.Start.
            window.SheetGrid.ActiveCell = a1;
            var range = new GridRange(a1, c5);
            window.SheetGrid.SelectedRange = range;
            range.Start.Should().Be(a1);

            DialogSourceTestSupport.InvokePrivateHandler(window, "FormatLockCellMenuItem_Click");

            var resultStyle = workbook.GetStyle(sheet.GetCell(a1)!.StyleId);
            resultStyle.Locked.Should().BeTrue("A1 was unlocked and coincides with both the active cell and range.Start here");
        }
        finally
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(window);
            PumpDispatcher();
        }
    });
}
