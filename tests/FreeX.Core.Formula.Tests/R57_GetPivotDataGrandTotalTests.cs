using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R57-formula-getpivotdata-5-1/5-2/5-3: three related GETPIVOTDATA grand-total/page-filter bugs.
///
/// 5-1: a pure grand-total request (no field/item pairs) with Show Row/Column Grand Totals
/// turned off used to fall through a vacuously-true fallback loop and silently return the
/// FIRST detail row/column instead of the true aggregate. Now the row/column resolvers signal
/// "unresolved" in that case and GetPivotData recomputes the true aggregate directly from the
/// pivot's source data (or #REF! if that isn't safely resolvable).
///
/// 5-2: the Grand Total row/column detector hardcoded the literal "Grand Total" caption,
/// missing a renamed (PivotTableModel.GrandTotalCaption) caption like "Overall Total".
///
/// 5-3: a field/item pair targeting a Page/Filter field whose CURRENT selection has more than
/// one item checked used to match as long as the requested item was merely one of them,
/// silently returning the combined multi-item total. Excel has no cell that isolates a single
/// item out of a multi-select page filter, so this must now yield #REF!.
/// </summary>
public sealed class R57_GetPivotDataGrandTotalTests
{
    private readonly FormulaEvaluator _eval = new();

    // Region row field: East=100, West=200. Data field "Sum of Sales" = source col 2 (Sales).
    // Target range only ever contains the East/West detail rows -- no Grand Total row -- unless
    // includeGrandTotalRow requests one be appended (with grandTotalCaption/grandTotalValue).
    private static (Workbook wb, Sheet sheet, PivotTableModel pivot) BuildRegionSalesPivot(
        bool showRowGrandTotals,
        bool includeGrandTotalRow,
        string grandTotalCaption = "Grand Total",
        double grandTotalValue = 300)
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");

        // Source data: Region (col1), Sales (col2).
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(200));

        // Materialized pivot output at (row2,col5): header row, then East/West detail rows,
        // optionally a Grand Total row.
        sheet.SetCell(new CellAddress(sheet.Id, 2, 5), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 6), new TextValue("Sum of Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 5), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 6), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 5), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 6), new NumberValue(200));

        uint targetEndRow = 4;
        if (includeGrandTotalRow)
        {
            sheet.SetCell(new CellAddress(sheet.Id, 5, 5), new TextValue(grandTotalCaption));
            sheet.SetCell(new CellAddress(sheet.Id, 5, 6), new NumberValue(grandTotalValue));
            targetEndRow = 5;
        }

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, targetEndRow, 6)),
            ShowRowGrandTotals = showRowGrandTotals,
            GrandTotalCaption = grandTotalCaption == "Grand Total" ? null : grandTotalCaption
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Sales", "sum"));
        sheet.PivotTables.Add(pivot);

        return (wb, sheet, pivot);
    }

    [Fact]
    public void GetPivotData_GrandTotal_ShowRowGrandTotalsOff_ReturnsTrueAggregate_NotFirstRow()
    {
        // Show Row Grand Totals is off -- no Grand Total row is rendered. Before the fix this
        // silently returned East's value (100, the first detail row) instead of the true total.
        var (wb, sheet, _) = BuildRegionSalesPivot(showRowGrandTotals: false, includeGrandTotalRow: false);

        _eval.Evaluate("=GETPIVOTDATA(\"Sum of Sales\",E2)", sheet, wb)
            .Should().Be(new NumberValue(300));
    }

    [Fact]
    public void GetPivotData_GrandTotal_ShowRowGrandTotalsOn_ReadsRenderedCell_Unchanged()
    {
        // Sibling/no-regression control: when the Grand Total row IS rendered, GETPIVOTDATA must
        // still read that cell directly (not recompute) -- proven by giving it a deliberately
        // different cached value (999) than the true recomputed sum (300) and asserting the
        // cached value wins, exactly as before this fix.
        var (wb, sheet, _) = BuildRegionSalesPivot(
            showRowGrandTotals: true, includeGrandTotalRow: true, grandTotalValue: 999);

        _eval.Evaluate("=GETPIVOTDATA(\"Sum of Sales\",E2)", sheet, wb)
            .Should().Be(new NumberValue(999));
    }

    [Fact]
    public void GetPivotData_GrandTotal_RenamedCaption_IsRecognizedAsGrandTotalRow()
    {
        // R57-formula-getpivotdata-5-2: the Grand Total row has been renamed to "Overall Total"
        // (PivotTableModel.GrandTotalCaption). It must still be recognized and read directly.
        var (wb, sheet, _) = BuildRegionSalesPivot(
            showRowGrandTotals: true,
            includeGrandTotalRow: true,
            grandTotalCaption: "Overall Total",
            grandTotalValue: 300);

        _eval.Evaluate("=GETPIVOTDATA(\"Sum of Sales\",E2)", sheet, wb)
            .Should().Be(new NumberValue(300));
    }

    [Fact]
    public void GetPivotData_GrandTotal_DefaultCaption_StillRecognized()
    {
        // Sibling/no-regression control for 5-2: the default "Grand Total" caption (no rename)
        // must still be recognized after switching the detector to consult GrandTotalCaption.
        var (wb, sheet, _) = BuildRegionSalesPivot(
            showRowGrandTotals: true, includeGrandTotalRow: true, grandTotalValue: 300);

        _eval.Evaluate("=GETPIVOTDATA(\"Sum of Sales\",E2)", sheet, wb)
            .Should().Be(new NumberValue(300));
    }

    private static (Workbook wb, Sheet sheet, PivotTableModel pivot) BuildRegionPageFilterPivot(
        IReadOnlyList<string> selectedItems)
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");

        // Source data: Region (col1), Sales (col2). Region is a Filters-area (page) field only --
        // no row/column breakdown exists in the rendered pivot.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(200));

        // Materialized pivot output: just a "Sum of Sales" header + the combined total (300),
        // reflecting whichever region(s) are currently selected in the page filter.
        sheet.SetCell(new CellAddress(sheet.Id, 2, 5), new TextValue("Sum of Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 5), new NumberValue(300));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 3, 5))
        };
        pivot.PageFields.Add(new PivotFieldModel(0, SelectedItems: selectedItems));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Sales", "sum"));
        sheet.PivotTables.Add(pivot);

        return (wb, sheet, pivot);
    }

    [Fact]
    public void GetPivotData_PageFieldPair_MultiSelectFilter_RequestingOneOfSeveral_ReturnsRefError()
    {
        // R57-formula-getpivotdata-5-3: Region page filter currently shows East AND West both
        // checked (multi-select). There is no cell isolating East alone, so requesting it must
        // yield #REF! instead of silently returning the East+West combined total (300).
        var (wb, sheet, _) = BuildRegionPageFilterPivot(["East", "West"]);

        _eval.Evaluate("=GETPIVOTDATA(\"Sum of Sales\",E2,\"Region\",\"East\")", sheet, wb)
            .Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void GetPivotData_PageFieldPair_SingleSelectFilter_StillMatches_NoRegression()
    {
        // Sibling/no-regression control: a page filter narrowed to EXACTLY one selected item
        // still matches that item's field/item pair, unaffected by the multi-select fix.
        var (wb, sheet, _) = BuildRegionPageFilterPivot(["East"]);

        _eval.Evaluate("=GETPIVOTDATA(\"Sum of Sales\",E2,\"Region\",\"East\")", sheet, wb)
            .Should().Be(new NumberValue(300));
    }
}
