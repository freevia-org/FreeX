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

        // East group: 2 Q1 items, Q1 Total, 2 Q2 items, Q2 Total, East Total
        Text(sheet, "F3").Should().Be("East Q1 Retail");
        Number(sheet, "G3").Should().Be(10);
        Text(sheet, "F4").Should().Be("East Q1 Wholesale");
        Number(sheet, "G4").Should().Be(15);
        Text(sheet, "F5").Should().Be("Q1 Total");
        Number(sheet, "G5").Should().Be(25);
        Text(sheet, "F6").Should().Be("East Q2 Retail");
        Number(sheet, "G6").Should().Be(20);
        Text(sheet, "F7").Should().Be("East Q2 Wholesale");
        Number(sheet, "G7").Should().Be(25);
        Text(sheet, "F8").Should().Be("Q2 Total");
        Number(sheet, "G8").Should().Be(45);
        Text(sheet, "F9").Should().Be("East Total");
        Number(sheet, "G9").Should().Be(70);
        // West group: 2 Q1 items, Q1 Total, 2 Q2 items, Q2 Total, West Total
        Text(sheet, "F10").Should().Be("West Q1 Retail");
        Number(sheet, "G10").Should().Be(30);
        Text(sheet, "F11").Should().Be("West Q1 Wholesale");
        Number(sheet, "G11").Should().Be(35);
        Text(sheet, "F12").Should().Be("Q1 Total");
        Number(sheet, "G12").Should().Be(65);
        Text(sheet, "F13").Should().Be("West Q2 Retail");
        Number(sheet, "G13").Should().Be(40);
        Text(sheet, "F14").Should().Be("West Q2 Wholesale");
        Number(sheet, "G14").Should().Be(45);
        Text(sheet, "F15").Should().Be("Q2 Total");
        Number(sheet, "G15").Should().Be(85);
        Text(sheet, "F16").Should().Be("West Total");
        Number(sheet, "G16").Should().Be(150);
        Text(sheet, "F17").Should().Be("Grand Total");
        Number(sheet, "G17").Should().Be(220);
    }

}
