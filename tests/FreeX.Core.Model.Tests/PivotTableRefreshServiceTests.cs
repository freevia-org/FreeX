using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PivotTableRefreshServiceTests
{
    [Fact]
    public void Refresh_MaterializesRowFieldSumAndGrandTotal()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G6")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E2").Should().Be("Region");
        Text(sheet, "F2").Should().Be("Sum of Amount");
        Text(sheet, "E3").Should().Be("East");
        Number(sheet, "F3").Should().Be(25);
        Text(sheet, "E4").Should().Be("West");
        Number(sheet, "F4").Should().Be(45);
        Text(sheet, "E5").Should().Be("Grand Total");
        Number(sheet, "F5").Should().Be(70);
    }

    [Fact]
    public void Refresh_UsesCustomGrandTotalCaptionForRowPivotStyleAndDetails()
    {
        var workbook = new Workbook("PivotCustomGrandTotalCaptionTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G6"),
            GrandTotalCaption = "Overall Total",
            StyleName = "PivotStyleMedium2"
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E5").Should().Be("Overall Total");
        Number(sheet, "F5").Should().Be(70);
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E5"))!.StyleId).FillColor
            .Should().Be(new CellColor(189, 215, 238));
        var detail = PivotTableRefreshService.ExtractDetailRows(workbook, sheet, pivot, Addr(sheet, "F5"));
        detail.Rows.Should().HaveCount(4);
    }

}
