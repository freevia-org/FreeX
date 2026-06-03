using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PivotTableRefreshServiceTests
{
    [Fact]
    public void Refresh_GroupsDateRowFieldByMonth()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedDatedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B5"),
            TargetRange = Range(sheet, "D2", "F8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0, Grouping: PivotFieldGrouping.Month));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "D2").Should().Be("Order Date");
        Text(sheet, "D3").Should().Be("2026-01");
        Number(sheet, "E3").Should().Be(30);
        Text(sheet, "D4").Should().Be("2026-02");
        Number(sheet, "E4").Should().Be(70);
        Text(sheet, "D5").Should().Be("Grand Total");
        Number(sheet, "E5").Should().Be(100);
    }

    [Fact]
    public void Refresh_GroupsNumberRowFieldByInterval()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedPriceSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B5"),
            TargetRange = Range(sheet, "D2", "F8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0, Grouping: PivotFieldGrouping.NumberRange, GroupStart: 0, GroupInterval: 10));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "D3").Should().Be("0-9");
        Number(sheet, "E3").Should().Be(30);
        Text(sheet, "D4").Should().Be("10-19");
        Number(sheet, "E4").Should().Be(70);
        Text(sheet, "D5").Should().Be("Grand Total");
        Number(sheet, "E5").Should().Be(100);
    }

}
