using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PivotTableRefreshServiceTests
{
    [Fact]
    public void Refresh_CompactReportLayoutUsesSingleRowLabelColumn()
    {
        var workbook = new Workbook("PivotCompactLayoutTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G8"),
            ReportLayout = PivotReportLayout.Compact
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E2").Should().Be("Row Labels");
        Text(sheet, "F2").Should().Be("Sum of Amount");
        Text(sheet, "E3").Should().Be("East Q1");
        Number(sheet, "F3").Should().Be(10);
        Text(sheet, "E4").Should().Be("East Q2");
        Number(sheet, "F4").Should().Be(15);
        Text(sheet, "E7").Should().Be("Grand Total");
        Number(sheet, "F7").Should().Be(70);
        sheet.GetCell(Addr(sheet, "G3")).Should().BeNull();
    }

    [Fact]
    public void Refresh_CompactReportLayoutAppliesConfiguredRowLabelIndent()
    {
        var workbook = new Workbook("PivotCompactIndentRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G8"),
            ReportLayout = PivotReportLayout.Compact,
            CompactRowLabelIndent = 3
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E3"))!.StyleId).IndentLevel.Should().Be(3);
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "E4"))!.StyleId).IndentLevel.Should().Be(3);
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "F3"))!.StyleId).IndentLevel.Should().Be(0);
    }

    [Fact]
    public void Refresh_CompactReportLayoutUsesSubtotaledFieldCaptionForNestedSubtotals()
    {
        var workbook = new Workbook("PivotCompactNestedSubtotalCaptionTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesChannelData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "D9"),
            TargetRange = Range(sheet, "F2", "H20"),
            ReportLayout = PivotReportLayout.Compact,
            ShowSubtotals = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.RowFields.Add(new PivotFieldModel(2));
        pivot.DataFields.Add(new PivotDataFieldModel(3, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "F5").Should().Be("Q1 Total");
        Number(sheet, "G5").Should().Be(25);
        Text(sheet, "F8").Should().Be("Q2 Total");
        Number(sheet, "G8").Should().Be(45);
        Text(sheet, "F11").Should().Be("Q1 Total");
        Number(sheet, "G11").Should().Be(65);
        Text(sheet, "F14").Should().Be("Q2 Total");
        Number(sheet, "G14").Should().Be(85);
    }

}
