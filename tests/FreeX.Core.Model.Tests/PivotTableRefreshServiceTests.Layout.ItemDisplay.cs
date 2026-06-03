using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PivotTableRefreshServiceTests
{
    [Fact]
    public void Refresh_SuppressesRepeatedOuterLabelsWhenDisabled()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I10"),
            RepeatItemLabels = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("East");
        Text(sheet, "F3").Should().Be("Q1");
        Text(sheet, "E4").Should().Be("");
        Text(sheet, "F4").Should().Be("Q2");
        Text(sheet, "E5").Should().Be("West");
        Text(sheet, "E6").Should().Be("");
    }

    [Fact]
    public void Refresh_MatrixSuppressesRepeatedOuterLabelsWhenDisabled()
    {
        var workbook = new Workbook("PivotMatrixRepeatLabelsTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "J10"),
            RepeatItemLabels = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("East");
        Text(sheet, "F3").Should().Be("Q1");
        Text(sheet, "E4").Should().Be("");
        Text(sheet, "F4").Should().Be("Q2");
        Text(sheet, "E5").Should().Be("West");
        Text(sheet, "E6").Should().Be("");
    }

    [Fact]
    public void Refresh_WritesBlankLineAfterOuterItemsWhenEnabled()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I12"),
            BlankLineAfterItems = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("East");
        Text(sheet, "F4").Should().Be("Q2");
        sheet.GetCell(Addr(sheet, "E5")).Should().BeNull();
        sheet.GetCell(Addr(sheet, "G5")).Should().BeNull();
        Text(sheet, "E6").Should().Be("West");
    }

    [Fact]
    public void Refresh_MatrixWritesBlankLineAfterOuterItemsWhenEnabled()
    {
        var workbook = new Workbook("PivotMatrixBlankLineTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "J12"),
            BlankLineAfterItems = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("East");
        Text(sheet, "F4").Should().Be("Q2");
        sheet.GetCell(Addr(sheet, "E5")).Should().BeNull();
        sheet.GetCell(Addr(sheet, "G5")).Should().BeNull();
        Text(sheet, "E6").Should().Be("West");
    }

}
