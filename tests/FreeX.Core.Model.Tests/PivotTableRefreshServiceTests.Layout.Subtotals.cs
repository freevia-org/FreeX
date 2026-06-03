using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PivotTableRefreshServiceTests
{
    [Fact]
    public void Refresh_WritesOuterRowFieldSubtotalsWhenEnabled()
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
            ShowSubtotals = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("East");
        Text(sheet, "F3").Should().Be("Q1");
        Number(sheet, "G3").Should().Be(10);
        Text(sheet, "E5").Should().Be("East Total");
        Number(sheet, "G5").Should().Be(25);
        Text(sheet, "E8").Should().Be("West Total");
        Number(sheet, "G8").Should().Be(45);
        Text(sheet, "E9").Should().Be("Grand Total");
        Number(sheet, "G9").Should().Be(70);
    }

    [Fact]
    public void Refresh_CanPlaceOuterRowFieldSubtotalsAtTopOfGroup()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "H10"),
            ShowSubtotals = true,
            SubtotalPlacement = PivotSubtotalPlacement.Top
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("East Total");
        Number(sheet, "G3").Should().Be(25);
        Text(sheet, "E4").Should().Be("East");
        Text(sheet, "F4").Should().Be("Q1");
        Text(sheet, "E6").Should().Be("West Total");
        Number(sheet, "G6").Should().Be(45);
        Text(sheet, "E7").Should().Be("West");
        Text(sheet, "F7").Should().Be("Q1");
        Text(sheet, "E9").Should().Be("Grand Total");
        Number(sheet, "G9").Should().Be(70);
    }

}
