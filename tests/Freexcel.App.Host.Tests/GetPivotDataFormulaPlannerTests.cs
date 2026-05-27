using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class GetPivotDataFormulaPlannerTests
{
    [Fact]
    public void Create_RowFieldValueCell_ComposesGetPivotDataFormula()
    {
        var (workbook, sheet, _) = CreateRowPivot();

        GetPivotDataFormulaPlanner.Create(workbook, sheet, sheet, CellAddress.Parse("F4", sheet.Id))
            .Should()
            .Be(new GetPivotDataFormulaPlan(
                "GETPIVOTDATA(\"Sum of Amount\",E2,\"Region\",\"West\")",
                "=GETPIVOTDATA(\"Sum of Amount\",E2,\"Region\",\"West\")"));
    }

    [Fact]
    public void Create_MatrixValueCell_IncludesRowAndColumnItems()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        SetCells(
            sheet,
            ("A1", new TextValue("Region")),
            ("B1", new TextValue("Quarter")),
            ("C1", new TextValue("Amount")),
            ("E2", new TextValue("Region")),
            ("F2", new TextValue("Q1")),
            ("G2", new TextValue("Q2")),
            ("H2", new TextValue("Grand Total")),
            ("E3", new TextValue("East")),
            ("F3", new NumberValue(10)),
            ("G3", new NumberValue(15)),
            ("H3", new NumberValue(25)),
            ("E4", new TextValue("West")),
            ("F4", new NumberValue(20)),
            ("G4", new NumberValue(25)),
            ("H4", new NumberValue(45)),
            ("E5", new TextValue("Grand Total")),
            ("F5", new NumberValue(30)),
            ("G5", new NumberValue(40)),
            ("H5", new NumberValue(70)));
        var pivot = CreatePivot(sheet, "A1:C5", "E2:H5");
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        GetPivotDataFormulaPlanner.Create(workbook, sheet, sheet, CellAddress.Parse("G3", sheet.Id))!
            .Formula.Should()
            .Be("=GETPIVOTDATA(\"Sum of Amount\",E2,\"Region\",\"East\",\"Quarter\",\"Q2\")");
    }

    [Fact]
    public void Create_CrossSheetPivotReference_QuotesSheetName()
    {
        var (workbook, pivotSheet, _) = CreateRowPivot("Pivot Sheet");
        var formulaSheet = workbook.AddSheet("Report");

        GetPivotDataFormulaPlanner.Create(workbook, formulaSheet, pivotSheet, CellAddress.Parse("F4", pivotSheet.Id))!
            .Formula.Should()
            .Be("=GETPIVOTDATA(\"Sum of Amount\",'Pivot Sheet'!E2,\"Region\",\"West\")");
    }

    [Fact]
    public void Create_PageFieldWithSelectedItem_IncludesFilter()
    {
        var (workbook, sheet, pivot) = CreateRowPivot();
        pivot.SourceRange = Range(sheet, "A1:C5");
        pivot.PageFields.Add(new PivotFieldModel(2, SelectedItem: "2026"));
        sheet.SetCell(CellAddress.Parse("C1", sheet.Id), new TextValue("Year"));

        GetPivotDataFormulaPlanner.Create(workbook, sheet, sheet, CellAddress.Parse("F3", sheet.Id))!
            .Formula.Should()
            .Be("=GETPIVOTDATA(\"Sum of Amount\",E2,\"Region\",\"East\",\"Year\",\"2026\")");
    }

    [Fact]
    public void Create_GrandTotalValueCell_OmitsAxisFilters()
    {
        var (workbook, sheet, _) = CreateRowPivot();

        GetPivotDataFormulaPlanner.Create(workbook, sheet, sheet, CellAddress.Parse("F5", sheet.Id))!
            .Formula.Should()
            .Be("=GETPIVOTDATA(\"Sum of Amount\",E2)");
    }

    [Fact]
    public void Create_SubtotalValueCell_UsesSubtotaledOuterItem()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        SetCells(
            sheet,
            ("A1", new TextValue("Region")),
            ("B1", new TextValue("Quarter")),
            ("C1", new TextValue("Amount")),
            ("E2", new TextValue("Region")),
            ("F2", new TextValue("Quarter")),
            ("G2", new TextValue("Sum of Amount")),
            ("E3", new TextValue("East")),
            ("F3", new TextValue("Q1")),
            ("G3", new NumberValue(10)),
            ("E4", new TextValue("East")),
            ("F4", new TextValue("Q2")),
            ("G4", new NumberValue(15)),
            ("E5", new TextValue("East Total")),
            ("G5", new NumberValue(25)));
        var pivot = CreatePivot(sheet, "A1:C5", "E2:G5");
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        GetPivotDataFormulaPlanner.Create(workbook, sheet, sheet, CellAddress.Parse("G5", sheet.Id))!
            .Formula.Should()
            .Be("=GETPIVOTDATA(\"Sum of Amount\",E2,\"Region\",\"East\")");
    }

    [Fact]
    public void Create_LabelCellOrNonPivotCell_ReturnsNull()
    {
        var (workbook, sheet, _) = CreateRowPivot();

        GetPivotDataFormulaPlanner.Create(workbook, sheet, sheet, CellAddress.Parse("E4", sheet.Id)).Should().BeNull();
        GetPivotDataFormulaPlanner.Create(workbook, sheet, sheet, CellAddress.Parse("A1", sheet.Id)).Should().BeNull();
    }

    [Fact]
    public void Create_EscapesQuotesInCaptionsAndItems()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        SetCells(
            sheet,
            ("A1", new TextValue("Region \"Name\"")),
            ("B1", new TextValue("Amount")),
            ("E2", new TextValue("Region \"Name\"")),
            ("F2", new TextValue("Sum of \"Amount\"")),
            ("E3", new TextValue("A \"Quoted\" Item")),
            ("F3", new NumberValue(25)));
        var pivot = CreatePivot(sheet, "A1:B3", "E2:F3");
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of \"Amount\"", "sum"));
        sheet.PivotTables.Add(pivot);

        GetPivotDataFormulaPlanner.Create(workbook, sheet, sheet, CellAddress.Parse("F3", sheet.Id))!
            .Formula.Should()
            .Be("=GETPIVOTDATA(\"Sum of \"\"Amount\"\"\",E2,\"Region \"\"Name\"\"\",\"A \"\"Quoted\"\" Item\")");
    }

    private static (Workbook Workbook, Sheet Sheet, PivotTableModel Pivot) CreateRowPivot(string sheetName = "Sheet1")
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet(sheetName);
        SetCells(
            sheet,
            ("A1", new TextValue("Region")),
            ("B1", new TextValue("Amount")),
            ("E2", new TextValue("Region")),
            ("F2", new TextValue("Sum of Amount")),
            ("E3", new TextValue("East")),
            ("F3", new NumberValue(25)),
            ("E4", new TextValue("West")),
            ("F4", new NumberValue(45)),
            ("E5", new TextValue("Grand Total")),
            ("F5", new NumberValue(70)));
        var pivot = CreatePivot(sheet, "A1:B5", "E2:F5");
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        return (workbook, sheet, pivot);
    }

    private static PivotTableModel CreatePivot(Sheet sheet, string sourceRange, string targetRange) =>
        new()
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, sourceRange),
            TargetRange = Range(sheet, targetRange)
        };

    private static GridRange Range(Sheet sheet, string reference)
    {
        var parts = reference.Split(':');
        return new GridRange(CellAddress.Parse(parts[0], sheet.Id), CellAddress.Parse(parts[^1], sheet.Id));
    }

    private static void SetCells(Sheet sheet, params (string Address, ScalarValue Value)[] cells)
    {
        foreach (var (address, value) in cells)
            sheet.SetCell(CellAddress.Parse(address, sheet.Id), value);
    }
}
