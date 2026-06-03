using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PivotTableRefreshServiceTests
{
    [Fact]
    public void Refresh_CompactReportLayoutUsesSingleRowLabelColumnForMatrix()
    {
        var workbook = new Workbook("PivotCompactMatrixLayoutTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "J8"),
            ReportLayout = PivotReportLayout.Compact
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E2").Should().Be("Row Labels");
        Text(sheet, "F2").Should().Be("Q1");
        Text(sheet, "G2").Should().Be("Q2");
        Text(sheet, "H2").Should().Be("Grand Total");
        Text(sheet, "E3").Should().Be("East Q1");
        Number(sheet, "F3").Should().Be(10);
        Number(sheet, "G3").Should().Be(0);
        Number(sheet, "H3").Should().Be(10);
        Text(sheet, "E6").Should().Be("West Q2");
        Number(sheet, "F6").Should().Be(0);
        Number(sheet, "G6").Should().Be(25);
        Number(sheet, "H6").Should().Be(25);
    }

    [Fact]
    public void Refresh_CompactMatrixWritesBottomSubtotalsPerColumn()
    {
        var workbook = new Workbook("PivotCompactMatrixSubtotalTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesChannelData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "D9"),
            TargetRange = Range(sheet, "F2", "J12"),
            ReportLayout = PivotReportLayout.Compact,
            ShowSubtotals = true,
            StyleName = "PivotStyleMedium9"
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.ColumnFields.Add(new PivotFieldModel(2));
        pivot.DataFields.Add(new PivotDataFieldModel(3, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "F5").Should().Be("East Total");
        Number(sheet, "G5").Should().Be(30);
        Number(sheet, "H5").Should().Be(40);
        Number(sheet, "I5").Should().Be(70);
        Text(sheet, "F8").Should().Be("West Total");
        Number(sheet, "G8").Should().Be(70);
        Number(sheet, "H8").Should().Be(80);
        Number(sheet, "I8").Should().Be(150);
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "F5"))!.StyleId).FillColor.Should().Be(new CellColor(221, 235, 247));
    }

    [Fact]
    public void Refresh_CompactMatrixWritesTopSubtotalsPerColumn()
    {
        var workbook = new Workbook("PivotCompactMatrixTopSubtotalTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesChannelData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "D9"),
            TargetRange = Range(sheet, "F2", "J12"),
            ReportLayout = PivotReportLayout.Compact,
            ShowSubtotals = true,
            SubtotalPlacement = PivotSubtotalPlacement.Top,
            StyleName = "PivotStyleMedium9"
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.ColumnFields.Add(new PivotFieldModel(2));
        pivot.DataFields.Add(new PivotDataFieldModel(3, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "F3").Should().Be("East Total");
        Number(sheet, "G3").Should().Be(30);
        Number(sheet, "H3").Should().Be(40);
        Number(sheet, "I3").Should().Be(70);
        Text(sheet, "F4").Should().Be("East Q1");
        Text(sheet, "F6").Should().Be("West Total");
        Number(sheet, "G6").Should().Be(70);
        Number(sheet, "H6").Should().Be(80);
        Number(sheet, "I6").Should().Be(150);
        Text(sheet, "F7").Should().Be("West Q1");
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "F3"))!.StyleId).FillColor.Should().Be(new CellColor(221, 235, 247));
    }

    [Fact]
    public void Refresh_CompactMatrixBlankLineAfterItemsKeepsSpacerAfterTopSubtotalGroup()
    {
        var workbook = new Workbook("PivotCompactMatrixTopSubtotalBlankLineTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesChannelData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "D9"),
            TargetRange = Range(sheet, "F2", "J14"),
            ReportLayout = PivotReportLayout.Compact,
            ShowSubtotals = true,
            SubtotalPlacement = PivotSubtotalPlacement.Top,
            BlankLineAfterItems = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.ColumnFields.Add(new PivotFieldModel(2));
        pivot.DataFields.Add(new PivotDataFieldModel(3, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "F3").Should().Be("East Total");
        Text(sheet, "F4").Should().Be("East Q1");
        Text(sheet, "F5").Should().Be("East Q2");
        sheet.GetCell(Addr(sheet, "F6")).Should().BeNull();
        Text(sheet, "F7").Should().Be("West Total");
        Text(sheet, "F8").Should().Be("West Q1");
        Text(sheet, "F9").Should().Be("West Q2");
        sheet.GetCell(Addr(sheet, "F10")).Should().BeNull();
        Text(sheet, "F11").Should().Be("Grand Total");
    }

    [Fact]
    public void Refresh_CompactMatrixBlankLineAfterItemsKeepsSpacerAfterBottomSubtotal()
    {
        var workbook = new Workbook("PivotCompactMatrixSubtotalBlankLineTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesChannelData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "D9"),
            TargetRange = Range(sheet, "F2", "J14"),
            ReportLayout = PivotReportLayout.Compact,
            ShowSubtotals = true,
            BlankLineAfterItems = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.ColumnFields.Add(new PivotFieldModel(2));
        pivot.DataFields.Add(new PivotDataFieldModel(3, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "F5").Should().Be("East Total");
        sheet.GetCell(Addr(sheet, "F6")).Should().BeNull();
        Text(sheet, "F7").Should().Be("West Q1");
        Text(sheet, "F9").Should().Be("West Total");
        sheet.GetCell(Addr(sheet, "F10")).Should().BeNull();
        Text(sheet, "F11").Should().Be("Grand Total");
    }

}
