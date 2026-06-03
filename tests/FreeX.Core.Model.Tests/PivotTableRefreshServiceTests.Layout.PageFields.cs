using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PivotTableRefreshServiceTests
{
    [Fact]
    public void Refresh_MaterializesPageFieldsUsingReportFilterLayout()
    {
        var workbook = new Workbook("PivotRefreshPageFieldLayoutTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "J8"),
            PageOverThenDown = true,
            PageWrap = 2
        };
        pivot.PageFields.Add(new PivotFieldModel(0, SelectedItems: ["East", "West"]));
        pivot.PageFields.Add(new PivotFieldModel(1, SelectedItem: "Q1"));
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E2").Should().Be("Region");
        Text(sheet, "F2").Should().Be("(Multiple Items)");
        Text(sheet, "G2").Should().Be("Quarter");
        Text(sheet, "H2").Should().Be("Q1");
        Text(sheet, "E4").Should().Be("Region");
        Number(sheet, "F5").Should().Be(10);
    }

}
