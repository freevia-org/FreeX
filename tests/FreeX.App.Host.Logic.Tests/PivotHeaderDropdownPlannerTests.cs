using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class PivotHeaderDropdownPlannerTests
{
    [Fact]
    public void BuildTargets_ReturnsRenderedRowAndColumnHeaderDropdownsWithActiveState()
    {
        var workbook = new Workbook("PivotHeaderDropdownPlannerTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, 1, 1, 5, 3),
            TargetRange = Range(sheet, 2, 5, 8, 9)
        };
        pivot.RowFields.Add(new PivotFieldModel(0, SelectedItems: ["East"]));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.LabelFilters.Add(new PivotLabelFilterModel(1, PivotLabelFilterKind.Contains, "Q"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var targets = PivotHeaderDropdownPlanner.BuildTargets(workbook, sheet);

        targets.Should().HaveCount(2);
        targets.Should().Contain(target =>
            target.PivotTableName == "PivotTable1" &&
            target.FieldCaption == "Region" &&
            target.SourceFieldIndex == 0 &&
            target.Axis == PivotHeaderDropdownAxis.Row &&
            target.HeaderCell == new CellAddress(sheet.Id, 2, 5) &&
            target.IsActive);
        targets.Should().Contain(target =>
            target.PivotTableName == "PivotTable1" &&
            target.FieldCaption == "Quarter" &&
            target.SourceFieldIndex == 1 &&
            target.Axis == PivotHeaderDropdownAxis.Column &&
            target.HeaderCell == new CellAddress(sheet.Id, 2, 6) &&
            target.IsActive);
    }

    [Fact]
    public void BuildTargets_HonorsHiddenFieldHeadersAndPerFieldDropDownFlags()
    {
        var workbook = new Workbook("PivotHeaderDropdownVisibilityPlannerTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, 1, 1, 5, 3),
            TargetRange = Range(sheet, 2, 5, 8, 9),
            ShowFieldHeaders = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        PivotHeaderDropdownPlanner.BuildTargets(workbook, sheet).Should().BeEmpty();

        pivot.ShowFieldHeaders = true;
        pivot.RowFields[0] = pivot.RowFields[0] with { ShowDropDowns = false };
        pivot.ColumnFields[0] = pivot.ColumnFields[0] with { ShowDropDowns = false };
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        PivotHeaderDropdownPlanner.BuildTargets(workbook, sheet).Should().BeEmpty();
    }

    [Fact]
    public void BuildTargets_UsesCompactRowLabelHeaderAndAccountsForPageFieldRows()
    {
        var workbook = new Workbook("PivotHeaderDropdownCompactPlannerTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, 1, 1, 5, 3),
            TargetRange = Range(sheet, 2, 5, 10, 9),
            ReportLayout = PivotReportLayout.Compact
        };
        pivot.PageFields.Add(new PivotFieldModel(1, SelectedItem: "Q1"));
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var targets = PivotHeaderDropdownPlanner.BuildTargets(workbook, sheet);

        targets.Should().Contain(target =>
            target.Axis == PivotHeaderDropdownAxis.Page &&
            target.FieldCaption == "Quarter" &&
            target.HeaderCell == new CellAddress(sheet.Id, 2, 6) &&
            target.IsActive);
        targets.Should().ContainSingle(target =>
            target.Axis == PivotHeaderDropdownAxis.Row &&
            target.FieldCaption == "Region" &&
            target.HeaderCell == new CellAddress(sheet.Id, 4, 5));
    }

    [Fact]
    public void BuildTargets_UsesNativeMatrixHeaderOffsetsForLoadedExcelPivot()
    {
        var workbook = new Workbook("NativeMatrixPivotHeaderDropdownPlannerTest");
        var source = workbook.AddSheet("Source");
        SeedSalesData(source);
        var pivotSheet = workbook.AddSheet("Pivot");
        var pivot = new PivotTableModel
        {
            Name = "NativePivotBasic",
            CacheId = 1,
            SourceRange = Range(source, 1, 1, 5, 3),
            TargetRange = Range(pivotSheet, 3, 1, 9, 5),
            FirstHeaderRow = 1,
            FirstDataRow = 2,
            FirstDataColumn = 1
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivotSheet.PivotTables.Add(pivot);
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 3, 1), new TextValue("Sum of Amount"));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 3, 2), new TextValue("Column Labels"));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 4, 1), new TextValue("Row Labels"));

        var targets = PivotHeaderDropdownPlanner.BuildTargets(workbook, pivotSheet);

        targets.Should().HaveCount(2);
        targets.Should().Contain(target =>
            target.Axis == PivotHeaderDropdownAxis.Column &&
            target.FieldCaption == "Quarter" &&
            target.HeaderCell == new CellAddress(pivotSheet.Id, 3, 2));
        targets.Should().Contain(target =>
            target.Axis == PivotHeaderDropdownAxis.Row &&
            target.FieldCaption == "Region" &&
            target.HeaderCell == new CellAddress(pivotSheet.Id, 4, 1));
    }

    [Fact]
    public void BuildTargets_UsesNativeReportFilterValueCellsAboveTargetRange()
    {
        var workbook = new Workbook("NativeReportFilterPivotHeaderDropdownPlannerTest");
        var source = workbook.AddSheet("Source");
        SeedSalesData(source);
        var pivotSheet = workbook.AddSheet("Pivot");
        var pivot = new PivotTableModel
        {
            Name = "NativePivotReportFilters",
            CacheId = 1,
            SourceRange = Range(source, 1, 1, 5, 3),
            TargetRange = Range(pivotSheet, 4, 1, 8, 4),
            FirstHeaderRow = 1,
            FirstDataRow = 2,
            FirstDataColumn = 1,
            PageOverThenDown = true,
            PageWrap = 2
        };
        pivot.PageFields.Add(new PivotFieldModel(1, SelectedItems: ["Q1", "Q2"]));
        pivot.PageFields.Add(new PivotFieldModel(0, SelectedItem: "North"));
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivotSheet.PivotTables.Add(pivot);
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 2, 1), new TextValue("Quarter"));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 2, 2), new TextValue("(Multiple Items)"));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 2, 4), new TextValue("Region"));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 2, 5), new TextValue("North"));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 4, 1), new TextValue("Sum of Amount"));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 4, 2), new TextValue("Column Labels"));
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 5, 1), new TextValue("Row Labels"));

        var targets = PivotHeaderDropdownPlanner.BuildTargets(workbook, pivotSheet);

        targets.Should().Contain(target =>
            target.Axis == PivotHeaderDropdownAxis.Page &&
            target.FieldCaption == "Quarter" &&
            target.HeaderCell == new CellAddress(pivotSheet.Id, 2, 2) &&
            target.IsActive);
        targets.Should().Contain(target =>
            target.Axis == PivotHeaderDropdownAxis.Page &&
            target.FieldCaption == "Region" &&
            target.HeaderCell == new CellAddress(pivotSheet.Id, 2, 5) &&
            target.IsActive);
        targets.Should().Contain(target =>
            target.Axis == PivotHeaderDropdownAxis.Column &&
            target.HeaderCell == new CellAddress(pivotSheet.Id, 4, 2));
        targets.Should().Contain(target =>
            target.Axis == PivotHeaderDropdownAxis.Row &&
            target.HeaderCell == new CellAddress(pivotSheet.Id, 5, 1));
    }

    private static void SeedSalesData(Sheet sheet)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Quarter"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Amount"));
        SetRow(sheet, 2, "East", "Q1", 10);
        SetRow(sheet, 3, "East", "Q2", 15);
        SetRow(sheet, 4, "West", "Q1", 20);
        SetRow(sheet, 5, "West", "Q2", 25);
    }

    private static void SetRow(Sheet sheet, uint row, string region, string quarter, double amount)
    {
        sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue(region));
        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(quarter));
        sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(amount));
    }

    private static GridRange Range(Sheet sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(
            new CellAddress(sheet.Id, startRow, startCol),
            new CellAddress(sheet.Id, endRow, endCol));
}
