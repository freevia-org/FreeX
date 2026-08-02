using System.Reflection;
using System.Windows;
using Free.Shared.AppServices;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using SheetGridView = FreeX.App.UI.GridView;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R91-calc-selection-semantics-5-2: Ctrl+Space (whole column) and
/// Shift+Space (whole row) inside a structured Table must scope to the table FIRST, matching
/// Excel's documented escalation -- 1st press selects the table's own column/row, and only a
/// subsequent press on the already-table-scoped selection escalates to the entire worksheet
/// column/row. Drives the real private SelectWholeColumnsFromSelection/SelectWholeRowsFromSelection
/// choke points (MainWindow.Selection.cs) via reflection.
/// </summary>
public sealed class R91_TableScopedSelectWholeColumnsRowsTests
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

    private static void SelectRange(MainWindow window, GridRange range)
    {
        var grid = (SheetGridView)window.FindName("SheetGrid");
        grid.SelectedRanges = null;
        grid.SelectedRange = range;
        PumpDispatcher();
    }

    private static GridRange? SelectedRange(MainWindow window) =>
        ((SheetGridView)window.FindName("SheetGrid")).SelectedRange;

    private static void InvokeSelectWholeColumns(MainWindow window)
    {
        var method = typeof(MainWindow).GetMethod("SelectWholeColumnsFromSelection", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        method!.Invoke(window, null);
        PumpDispatcher();
    }

    private static void InvokeSelectWholeRows(MainWindow window)
    {
        var method = typeof(MainWindow).GetMethod("SelectWholeRowsFromSelection", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        method!.Invoke(window, null);
        PumpDispatcher();
    }

    /// <summary>Builds a 3-column, 10-row table (A1:C1 header, A2:C10 data), with unrelated data
    /// below the table in the same column (B50) to prove the fix doesn't sweep it in prematurely.</summary>
    private static void CreateTable(Workbook workbook, Sheet sheet)
    {
        for (var col = 1u; col <= 3; col++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, col), Cell.FromValue(new TextValue($"Col{col}")));
        for (var row = 2u; row <= 10; row++)
            for (var col = 1u; col <= 3; col++)
                sheet.SetCell(new CellAddress(sheet.Id, row, col), Cell.FromValue(new NumberValue(row * 10 + col)));

        sheet.SetCell(new CellAddress(sheet.Id, 50, 2), Cell.FromValue(new TextValue("unrelated")));

        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 3)),
            HeaderRowCount = 1
        });
    }

    [Fact]
    public void CtrlSpace_InsideTable_FirstPressSelectsTableColumnData_SecondPressAddsHeader_ThirdPressSelectsWholeSheetColumn() =>
        StaTestRunner.Run(() =>
    {
        var (window, workbook, sheet) = CreateAdoptedWindow();
        try
        {
            CreateTable(workbook, sheet);
            var b5 = new CellAddress(sheet.Id, 5, 2);
            SelectRange(window, new GridRange(b5, b5));

            InvokeSelectWholeColumns(window);
            SelectedRange(window).Should().Be(new GridRange(
                new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 10, 2)),
                "1st press must select just the table column's DATA cells (B2:B10), excluding the header row");

            InvokeSelectWholeColumns(window);
            SelectedRange(window).Should().Be(new GridRange(
                new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 10, 2)),
                "2nd press must extend to the whole table column including the header (B1:B10)");

            InvokeSelectWholeColumns(window);
            var wholeSheetColumn = SelectedRange(window)!.Value;
            wholeSheetColumn.Start.Row.Should().Be(1);
            wholeSheetColumn.End.Row.Should().Be(CellAddress.MaxRow);
            wholeSheetColumn.Start.Col.Should().Be(2);
            wholeSheetColumn.End.Col.Should().Be(2);
        }
        finally
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(window);
            PumpDispatcher();
        }
    });

    [Fact]
    public void ShiftSpace_InsideTable_FirstPressSelectsTableRow_SecondPressSelectsWholeSheetRow() =>
        StaTestRunner.Run(() =>
    {
        var (window, workbook, sheet) = CreateAdoptedWindow();
        try
        {
            CreateTable(workbook, sheet);
            var b5 = new CellAddress(sheet.Id, 5, 2);
            SelectRange(window, new GridRange(b5, b5));

            InvokeSelectWholeRows(window);
            SelectedRange(window).Should().Be(new GridRange(
                new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 5, 3)),
                "1st press must select just the table's row span (A5:C5), not the whole sheet row");

            InvokeSelectWholeRows(window);
            var wholeSheetRow = SelectedRange(window)!.Value;
            wholeSheetRow.Start.Col.Should().Be(1);
            wholeSheetRow.End.Col.Should().Be(CellAddress.MaxCol);
            wholeSheetRow.Start.Row.Should().Be(5);
            wholeSheetRow.End.Row.Should().Be(5);
        }
        finally
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(window);
            PumpDispatcher();
        }
    });

    /// <summary>No-regression sibling: outside any table, Ctrl+Space/Shift+Space must still jump
    /// straight to the whole sheet column/row on the very first press, exactly as before this fix.</summary>
    [Fact]
    public void CtrlSpaceAndShiftSpace_OutsideAnyTable_StillSelectWholeSheetColumnOrRowOnFirstPress() =>
        StaTestRunner.Run(() =>
    {
        var (window, workbook, sheet) = CreateAdoptedWindow();
        try
        {
            sheet.SetCell(new CellAddress(sheet.Id, 5, 2), Cell.FromValue(new TextValue("plain")));
            var b5 = new CellAddress(sheet.Id, 5, 2);
            SelectRange(window, new GridRange(b5, b5));

            InvokeSelectWholeColumns(window);
            var wholeColumn = SelectedRange(window)!.Value;
            wholeColumn.Start.Row.Should().Be(1);
            wholeColumn.End.Row.Should().Be(CellAddress.MaxRow);
            wholeColumn.Start.Col.Should().Be(2);
            wholeColumn.End.Col.Should().Be(2);

            SelectRange(window, new GridRange(b5, b5));
            InvokeSelectWholeRows(window);
            var wholeRow = SelectedRange(window)!.Value;
            wholeRow.Start.Col.Should().Be(1);
            wholeRow.End.Col.Should().Be(CellAddress.MaxCol);
            wholeRow.Start.Row.Should().Be(5);
            wholeRow.End.Row.Should().Be(5);
        }
        finally
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(window);
            PumpDispatcher();
        }
    });
}
