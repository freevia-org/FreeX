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
/// <see cref="R128_FormatCellsDialogActiveCellSeedTests"/> and
/// <see cref="R128_FormatLockCellActiveCellSeedTests"/>): the ScopeAudit for that wave found the
/// identical top-left-corner-vs-active-cell bug pattern surviving untouched in six Home-tab ribbon
/// steppers in MainWindow.HomeFormatting.cs -- IncreaseFontSizeBtn_Click, DecreaseFontSizeBtn_Click,
/// IndentIncBtn_Click, IndentDecBtn_Click, IncDecimalBtn_Click and DecDecimalBtn_Click all read their
/// base value from <c>SheetGrid.SelectedRange?.Start</c> instead of the true active cell, so a
/// backward-extended selection (click C5, Shift+click A1) increments/decrements from A1's value
/// while the ribbon displays C5's. This covers one representative from each affected family
/// (indent and decimal-places, both driven directly through <c>ApplyStyleDiff</c>) plus a
/// no-regression check that a forward selection keeps behaving as before.
/// </summary>
public sealed class R128B_HomeFormattingActiveCellSeedTests
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
    public void IndentIncBtn_BackwardExtendedSelection_IncrementsFromActiveCellNotStart() =>
        StaTestRunner.Run(() =>
    {
        var (window, workbook, sheet) = CreateAdoptedWindow();
        try
        {
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var c5 = new CellAddress(sheet.Id, 5, 3);

            // C5 (active cell) starts at indent 3; A1 (range.Start) starts at indent 0 (default).
            var indentedStyleId = workbook.RegisterStyle(new CellStyle { IndentLevel = 3 });
            sheet.SetCell(c5, new Cell { Value = new TextValue("active"), StyleId = indentedStyleId });
            sheet.SetCell(a1, new Cell { Value = new TextValue("start") });

            // Click C5, then Shift+click A1: active cell stays C5, SelectedRange normalizes to A1:C5.
            window.SheetGrid.ActiveCell = c5;
            var range = new GridRange(c5, a1);
            window.SheetGrid.SelectedRange = range;
            range.Start.Should().Be(a1, "GridRange normalizes its Start to the top-left corner");

            DialogSourceTestSupport.InvokePrivateHandler(window, "IndentIncBtn_Click");

            var resultStyle = workbook.GetStyle(sheet.GetCell(c5)!.StyleId);
            resultStyle.IndentLevel.Should().Be(4,
                "the increment must be applied to the ACTIVE cell's indent (C5 was 3, so it must " +
                "become 4), not range.Start's (A1 was 0, which would produce 1)");
        }
        finally
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(window);
            PumpDispatcher();
        }
    });

    /// <summary>No-regression sibling: a forward (top-left-anchored) selection -- where ActiveCell
    /// and SelectedRange.Start coincide -- must keep incrementing from Start exactly as before.</summary>
    [Fact]
    public void IndentIncBtn_ForwardSelection_IncrementsFromStartAsBefore() =>
        StaTestRunner.Run(() =>
    {
        var (window, workbook, sheet) = CreateAdoptedWindow();
        try
        {
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var c5 = new CellAddress(sheet.Id, 5, 3);

            var indentedStyleId = workbook.RegisterStyle(new CellStyle { IndentLevel = 2 });
            sheet.SetCell(a1, new Cell { Value = new TextValue("active"), StyleId = indentedStyleId });
            sheet.SetCell(c5, new Cell { Value = new TextValue("end") });

            // Click A1, then Shift+click C5: active cell stays A1, which is also SelectedRange.Start.
            window.SheetGrid.ActiveCell = a1;
            var range = new GridRange(a1, c5);
            window.SheetGrid.SelectedRange = range;
            range.Start.Should().Be(a1);

            DialogSourceTestSupport.InvokePrivateHandler(window, "IndentIncBtn_Click");

            var resultStyle = workbook.GetStyle(sheet.GetCell(a1)!.StyleId);
            resultStyle.IndentLevel.Should().Be(3,
                "A1 started at indent 2 and coincides with both the active cell and range.Start here");
        }
        finally
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(window);
            PumpDispatcher();
        }
    });

    [Fact]
    public void IncDecimalBtn_BackwardExtendedSelection_AdjustsFromActiveCellNotStart() =>
        StaTestRunner.Run(() =>
    {
        var (window, workbook, sheet) = CreateAdoptedWindow();
        try
        {
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var c5 = new CellAddress(sheet.Id, 5, 3);

            // C5 (active cell) is formatted "0.00"; A1 (range.Start) is left at General.
            var twoDecimalStyleId = workbook.RegisterStyle(new CellStyle { NumberFormat = "0.00" });
            sheet.SetCell(c5, new Cell { Value = new NumberValue(1.5), StyleId = twoDecimalStyleId });
            sheet.SetCell(a1, new Cell { Value = new NumberValue(2.5) });

            // Click C5, then Shift+click A1: active cell stays C5, SelectedRange normalizes to A1:C5.
            window.SheetGrid.ActiveCell = c5;
            var range = new GridRange(c5, a1);
            window.SheetGrid.SelectedRange = range;
            range.Start.Should().Be(a1, "GridRange normalizes its Start to the top-left corner");

            DialogSourceTestSupport.InvokePrivateHandler(window, "IncDecimalBtn_Click");

            var resultStyle = workbook.GetStyle(sheet.GetCell(c5)!.StyleId);
            resultStyle.NumberFormat.Should().Be("0.000",
                "the decimal-place increase must seed from the ACTIVE cell's number format " +
                "('0.00' -> '0.000'), not range.Start's (General, which would produce a different result)");
        }
        finally
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(window);
            PumpDispatcher();
        }
    });

    /// <summary>No-regression sibling: a forward selection must keep adjusting decimals from Start
    /// exactly as before.</summary>
    [Fact]
    public void IncDecimalBtn_ForwardSelection_AdjustsFromStartAsBefore() =>
        StaTestRunner.Run(() =>
    {
        var (window, workbook, sheet) = CreateAdoptedWindow();
        try
        {
            var a1 = new CellAddress(sheet.Id, 1, 1);
            var c5 = new CellAddress(sheet.Id, 5, 3);

            var oneDecimalStyleId = workbook.RegisterStyle(new CellStyle { NumberFormat = "0.0" });
            sheet.SetCell(a1, new Cell { Value = new NumberValue(1.5), StyleId = oneDecimalStyleId });
            sheet.SetCell(c5, new Cell { Value = new NumberValue(2.5) });

            // Click A1, then Shift+click C5: active cell stays A1, which is also SelectedRange.Start.
            window.SheetGrid.ActiveCell = a1;
            var range = new GridRange(a1, c5);
            window.SheetGrid.SelectedRange = range;
            range.Start.Should().Be(a1);

            DialogSourceTestSupport.InvokePrivateHandler(window, "IncDecimalBtn_Click");

            var resultStyle = workbook.GetStyle(sheet.GetCell(a1)!.StyleId);
            resultStyle.NumberFormat.Should().Be("0.00",
                "A1 started at '0.0' and coincides with both the active cell and range.Start here");
        }
        finally
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(window);
            PumpDispatcher();
        }
    });
}
