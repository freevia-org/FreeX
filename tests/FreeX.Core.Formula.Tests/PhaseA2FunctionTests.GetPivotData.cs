using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class PhaseA2FunctionTests
{
    // ── GETPIVOTDATA ────────────────────────────────────────────────────────────

    [Fact]
    public void GetPivotData_NoPivotAtReference_ReturnsRef()
    {
        var (wb, sheet) = MakeWb();
        _eval.Evaluate("=GETPIVOTDATA(\"Sum of Amount\",A1)", sheet, wb).Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void GetPivotData_RowFieldItem_ReturnsPivotValue()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new TextValue("Region")),
            (1, 2, new TextValue("Amount")),
            (2, 5, new TextValue("Region")),
            (2, 6, new TextValue("Sum of Amount")),
            (3, 5, new TextValue("East")),
            (3, 6, new NumberValue(25)),
            (4, 5, new TextValue("West")),
            (4, 6, new NumberValue(45)),
            (5, 5, new TextValue("Grand Total")),
            (5, 6, new NumberValue(70)));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 5, 6))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        _eval.Evaluate("=GETPIVOTDATA(\"Sum of Amount\",E2,\"Region\",\"West\")", sheet, wb)
            .Should()
            .Be(new NumberValue(45));
    }

    [Fact]
    public void GetPivotData_RowAndColumnFieldItems_ReturnsMatrixValue()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new TextValue("Region")),
            (1, 2, new TextValue("Quarter")),
            (1, 3, new TextValue("Amount")),
            (2, 5, new TextValue("Region")),
            (2, 6, new TextValue("Q1")),
            (2, 7, new TextValue("Q2")),
            (2, 8, new TextValue("Grand Total")),
            (3, 5, new TextValue("East")),
            (3, 6, new NumberValue(10)),
            (3, 7, new NumberValue(15)),
            (3, 8, new NumberValue(25)),
            (4, 5, new TextValue("West")),
            (4, 6, new NumberValue(20)),
            (4, 7, new NumberValue(25)),
            (4, 8, new NumberValue(45)),
            (5, 5, new TextValue("Grand Total")),
            (5, 6, new NumberValue(30)),
            (5, 7, new NumberValue(40)),
            (5, 8, new NumberValue(70)));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 5, 8))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        _eval.Evaluate("=GETPIVOTDATA(\"Sum of Amount\",E2,\"Region\",\"East\",\"Quarter\",\"Q2\")", sheet, wb)
            .Should()
            .Be(new NumberValue(15));
    }

    [Fact]
    public void GetPivotData_RowFieldOnlyInMatrix_ReturnsRowGrandTotal()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new TextValue("Region")),
            (1, 2, new TextValue("Quarter")),
            (1, 3, new TextValue("Amount")),
            (2, 5, new TextValue("Region")),
            (2, 6, new TextValue("Q1")),
            (2, 7, new TextValue("Q2")),
            (2, 8, new TextValue("Grand Total")),
            (3, 5, new TextValue("East")),
            (3, 6, new NumberValue(10)),
            (3, 7, new NumberValue(15)),
            (3, 8, new NumberValue(25)),
            (4, 5, new TextValue("West")),
            (4, 6, new NumberValue(20)),
            (4, 7, new NumberValue(25)),
            (4, 8, new NumberValue(45)),
            (5, 5, new TextValue("Grand Total")),
            (5, 6, new NumberValue(30)),
            (5, 7, new NumberValue(40)),
            (5, 8, new NumberValue(70)));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 5, 8))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        _eval.Evaluate("=GETPIVOTDATA(\"Sum of Amount\",E2,\"Region\",\"East\")", sheet, wb)
            .Should()
            .Be(new NumberValue(25));
    }

    [Fact]
    public void GetPivotData_OuterRowFieldOnly_ReturnsSubtotal()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new TextValue("Region")),
            (1, 2, new TextValue("Quarter")),
            (1, 3, new TextValue("Amount")),
            (2, 5, new TextValue("Region")),
            (2, 6, new TextValue("Quarter")),
            (2, 7, new TextValue("Sum of Amount")),
            (3, 5, new TextValue("East")),
            (3, 6, new TextValue("Q1")),
            (3, 7, new NumberValue(10)),
            (4, 5, new TextValue("East")),
            (4, 6, new TextValue("Q2")),
            (4, 7, new NumberValue(15)),
            (5, 5, new TextValue("East Total")),
            (5, 7, new NumberValue(25)),
            (6, 5, new TextValue("West")),
            (6, 6, new TextValue("Q1")),
            (6, 7, new NumberValue(20)),
            (7, 5, new TextValue("West")),
            (7, 6, new TextValue("Q2")),
            (7, 7, new NumberValue(25)),
            (8, 5, new TextValue("West Total")),
            (8, 7, new NumberValue(45)),
            (9, 5, new TextValue("Grand Total")),
            (9, 7, new NumberValue(70)));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 9, 7)),
            ShowSubtotals = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        _eval.Evaluate("=GETPIVOTDATA(\"Sum of Amount\",E2,\"Region\",\"East\")", sheet, wb)
            .Should()
            .Be(new NumberValue(25));
    }

    [Fact]
    public void GetPivotData_CrossSheetPivotReference_ReturnsPivotValue()
    {
        var wb = new Workbook();
        var pivotSheet = wb.AddSheet("Pivot");
        var formulaSheet = wb.AddSheet("Report");
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 1, 1), new TextValue("Region"));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 1, 2), new TextValue("Amount"));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 2, 5), new TextValue("Region"));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 2, 6), new TextValue("Sum of Amount"));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 3, 5), new TextValue("East"));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 3, 6), new NumberValue(25));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 4, 5), new TextValue("West"));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 4, 6), new NumberValue(45));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 5, 5), new TextValue("Grand Total"));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 5, 6), new NumberValue(70));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(pivotSheet.Id, 1, 1), new CellAddress(pivotSheet.Id, 5, 2)),
            TargetRange = new GridRange(new CellAddress(pivotSheet.Id, 2, 5), new CellAddress(pivotSheet.Id, 5, 6))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        pivotSheet.PivotTables.Add(pivot);

        _eval.Evaluate("=GETPIVOTDATA(\"Sum of Amount\",Pivot!E2,\"Region\",\"West\")", formulaSheet, wb)
            .Should()
            .Be(new NumberValue(45));
    }

    [Fact]
    public void GetPivotData_SheetQualifiedReferenceIgnoresSameCoordinatesOnFormulaSheet()
    {
        var wb = new Workbook();
        var pivotSheet = wb.AddSheet("Pivot");
        var formulaSheet = wb.AddSheet("Report");

        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 1, 1), new TextValue("Region"));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 1, 2), new TextValue("Amount"));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 2, 5), new TextValue("Region"));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 2, 6), new TextValue("Sum of Amount"));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 3, 5), new TextValue("East"));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 3, 6), new NumberValue(25));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 4, 5), new TextValue("Grand Total"));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 4, 6), new NumberValue(25));

        formulaSheet.SetCell(new CellAddress(formulaSheet.Id, 1, 1), new TextValue("Region"));
        formulaSheet.SetCell(new CellAddress(formulaSheet.Id, 1, 2), new TextValue("Amount"));
        formulaSheet.SetCell(new CellAddress(formulaSheet.Id, 2, 5), new TextValue("Region"));
        formulaSheet.SetCell(new CellAddress(formulaSheet.Id, 2, 6), new TextValue("Sum of Amount"));
        formulaSheet.SetCell(new CellAddress(formulaSheet.Id, 3, 5), new TextValue("East"));
        formulaSheet.SetCell(new CellAddress(formulaSheet.Id, 3, 6), new NumberValue(999));
        formulaSheet.SetCell(new CellAddress(formulaSheet.Id, 4, 5), new TextValue("Grand Total"));
        formulaSheet.SetCell(new CellAddress(formulaSheet.Id, 4, 6), new NumberValue(999));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(pivotSheet.Id, 1, 1), new CellAddress(pivotSheet.Id, 4, 2)),
            TargetRange = new GridRange(new CellAddress(pivotSheet.Id, 2, 5), new CellAddress(pivotSheet.Id, 4, 6))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        pivotSheet.PivotTables.Add(pivot);

        var localPivot = new PivotTableModel
        {
            Name = "PivotTable2",
            CacheId = 2,
            SourceRange = new GridRange(new CellAddress(formulaSheet.Id, 1, 1), new CellAddress(formulaSheet.Id, 4, 2)),
            TargetRange = new GridRange(new CellAddress(formulaSheet.Id, 2, 5), new CellAddress(formulaSheet.Id, 4, 6))
        };
        localPivot.RowFields.Add(new PivotFieldModel(0));
        localPivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        formulaSheet.PivotTables.Add(localPivot);

        _eval.Evaluate("=GETPIVOTDATA(\"Sum of Amount\",Pivot!E2,\"Region\",\"East\")", formulaSheet, wb)
            .Should()
            .Be(new NumberValue(25));
    }

    [Fact]
    public void GetPivotData_PageFieldItem_MustMatchSelectedPageFilter()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new TextValue("Region")),
            (1, 2, new TextValue("Year")),
            (1, 3, new TextValue("Amount")),
            (2, 5, new TextValue("Region")),
            (2, 6, new TextValue("Sum of Amount")),
            (3, 5, new TextValue("East")),
            (3, 6, new NumberValue(25)),
            (4, 5, new TextValue("West")),
            (4, 6, new NumberValue(45)),
            (5, 5, new TextValue("Grand Total")),
            (5, 6, new NumberValue(70)));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 5, 6))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.PageFields.Add(new PivotFieldModel(1, SelectedItem: "2026"));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        _eval.Evaluate("=GETPIVOTDATA(\"Sum of Amount\",E2,\"Region\",\"East\",\"Year\",\"2026\")", sheet, wb)
            .Should()
            .Be(new NumberValue(25));
        _eval.Evaluate("=GETPIVOTDATA(\"Sum of Amount\",E2,\"Region\",\"East\",\"Year\",\"2025\")", sheet, wb)
            .Should()
            .Be(ErrorValue.Ref);
    }


    [Fact]
    public void GetPivotData_UnknownField_ReturnsRef()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new TextValue("Region")),
            (1, 2, new TextValue("Amount")),
            (2, 5, new TextValue("Region")),
            (2, 6, new TextValue("Sum of Amount")),
            (3, 5, new TextValue("East")),
            (3, 6, new NumberValue(25)),
            (4, 5, new TextValue("Grand Total")),
            (4, 6, new NumberValue(25)));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 4, 6))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        _eval.Evaluate("=GETPIVOTDATA(\"Sum of Amount\",E2,\"Bogus\",\"East\")", sheet, wb)
            .Should()
            .Be(ErrorValue.Ref);
    }

    [Fact]
    public void GetPivotData_ConflictingDuplicateField_ReturnsRef()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new TextValue("Region")),
            (1, 2, new TextValue("Amount")),
            (2, 5, new TextValue("Region")),
            (2, 6, new TextValue("Sum of Amount")),
            (3, 5, new TextValue("East")),
            (3, 6, new NumberValue(25)),
            (4, 5, new TextValue("West")),
            (4, 6, new NumberValue(45)),
            (5, 5, new TextValue("Grand Total")),
            (5, 6, new NumberValue(70)));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 5, 6))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        _eval.Evaluate("=GETPIVOTDATA(\"Sum of Amount\",E2,\"Region\",\"East\",\"Region\",\"West\")", sheet, wb)
            .Should()
            .Be(ErrorValue.Ref);
    }

    [Fact]
    public void GetPivotData_CompactNestedRowFields_ReturnsLeafValue()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new TextValue("Region")),
            (1, 2, new TextValue("Quarter")),
            (1, 3, new TextValue("Amount")),
            (2, 5, new TextValue("Row Labels")),
            (2, 6, new TextValue("Sum of Amount")),
            (3, 5, new TextValue("East Q1")),
            (3, 6, new NumberValue(10)),
            (4, 5, new TextValue("East Q2")),
            (4, 6, new NumberValue(15)),
            (5, 5, new TextValue("West Q1")),
            (5, 6, new NumberValue(20)),
            (6, 5, new TextValue("West Q2")),
            (6, 6, new NumberValue(25)),
            (7, 5, new TextValue("Grand Total")),
            (7, 6, new NumberValue(70)));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 7, 6)),
            ReportLayout = PivotReportLayout.Compact
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        _eval.Evaluate("=GETPIVOTDATA(\"Sum of Amount\",E2,\"Region\",\"East\",\"Quarter\",\"Q2\")", sheet, wb)
            .Should()
            .Be(new NumberValue(15));
    }

    [Fact]
    public void GetPivotData_MultipleDataFieldsWithColumnItem_ReturnsRequestedDataField()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new TextValue("Region")),
            (1, 2, new TextValue("Quarter")),
            (1, 3, new TextValue("Amount")),
            (2, 5, new TextValue("Region")),
            (2, 6, new TextValue("Q1")),
            (2, 7, new TextValue("Q1 Count of Amount")),
            (2, 8, new TextValue("Q2")),
            (2, 9, new TextValue("Q2 Count of Amount")),
            (2, 10, new TextValue("Grand Total")),
            (2, 11, new TextValue("Grand Total Count of Amount")),
            (3, 5, new TextValue("East")),
            (3, 6, new NumberValue(10)),
            (3, 7, new NumberValue(1)),
            (3, 8, new NumberValue(15)),
            (3, 9, new NumberValue(1)),
            (3, 10, new NumberValue(25)),
            (3, 11, new NumberValue(2)),
            (4, 5, new TextValue("Grand Total")),
            (4, 6, new NumberValue(10)),
            (4, 7, new NumberValue(1)),
            (4, 8, new NumberValue(15)),
            (4, 9, new NumberValue(1)),
            (4, 10, new NumberValue(25)),
            (4, 11, new NumberValue(2)));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 4, 11))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Count of Amount", "count"));
        sheet.PivotTables.Add(pivot);

        _eval.Evaluate("=GETPIVOTDATA(\"Count of Amount\",E2,\"Region\",\"East\",\"Quarter\",\"Q2\")", sheet, wb)
            .Should()
            .Be(new NumberValue(1));
    }
}
