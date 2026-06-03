using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PivotTableRefreshServiceTests
{
    [Fact]
    public void Refresh_MergeAndCenterLabelsMergesRepeatedOuterRowLabels()
    {
        var workbook = new Workbook("PivotMergeLabelsRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I10"),
            ShowSubtotals = false,
            MergeAndCenterLabels = true,
            ShowRowStripes = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        sheet.MergedRegions.Should().Contain(new GridRange(Addr(sheet, "E3"), Addr(sheet, "E4")));
        sheet.MergedRegions.Should().Contain(new GridRange(Addr(sheet, "E5"), Addr(sheet, "E6")));
        Text(sheet, "E3").Should().Be("East");
        var eastStyle = workbook.GetStyle(sheet.GetCell(Addr(sheet, "E3"))!.StyleId);
        eastStyle.HorizontalAlignment.Should().Be(HorizontalAlignment.Center);
        eastStyle.VerticalAlignment.Should().Be(VerticalAlignment.Center);
        eastStyle.FillColor.Should().NotBeNull();
        sheet.GetCell(Addr(sheet, "E4")).Should().BeNull();
        Text(sheet, "E5").Should().Be("West");
        var westStyle = workbook.GetStyle(sheet.GetCell(Addr(sheet, "E5"))!.StyleId);
        westStyle.HorizontalAlignment.Should().Be(HorizontalAlignment.Center);
        westStyle.VerticalAlignment.Should().Be(VerticalAlignment.Center);
        westStyle.FillColor.Should().NotBeNull();
        sheet.GetCell(Addr(sheet, "E6")).Should().BeNull();
    }

    [Fact]
    public void Refresh_MergeAndCenterLabelsMergesSuppressedRepeatedOuterRowLabels()
    {
        var workbook = new Workbook("PivotMergeSuppressedLabelsRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I10"),
            ShowSubtotals = false,
            RepeatItemLabels = false,
            MergeAndCenterLabels = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        sheet.MergedRegions.Should().Contain(new GridRange(Addr(sheet, "E3"), Addr(sheet, "E4")));
        sheet.MergedRegions.Should().Contain(new GridRange(Addr(sheet, "E5"), Addr(sheet, "E6")));
        Text(sheet, "E3").Should().Be("East");
        sheet.GetCell(Addr(sheet, "E4")).Should().BeNull();
        Text(sheet, "E5").Should().Be("West");
        sheet.GetCell(Addr(sheet, "E6")).Should().BeNull();
    }

    [Fact]
    public void Refresh_MergeAndCenterLabelsMergesSubtotalCaptionsAcrossRowLabelColumns()
    {
        var workbook = new Workbook("PivotMergeSubtotalLabelsRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I12"),
            ShowSubtotals = true,
            MergeAndCenterLabels = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        sheet.MergedRegions.Should().Contain(new GridRange(Addr(sheet, "E5"), Addr(sheet, "F5")));
        sheet.MergedRegions.Should().Contain(new GridRange(Addr(sheet, "E8"), Addr(sheet, "F8")));
        Text(sheet, "E5").Should().Be("East Total");
        Text(sheet, "E8").Should().Be("West Total");
        var subtotalStyle = workbook.GetStyle(sheet.GetCell(Addr(sheet, "E5"))!.StyleId);
        subtotalStyle.HorizontalAlignment.Should().Be(HorizontalAlignment.Center);
        subtotalStyle.VerticalAlignment.Should().Be(VerticalAlignment.Center);
    }

    [Fact]
    public void Refresh_MergeAndCenterLabelsRemovesStalePivotMergesWhenDisabled()
    {
        var workbook = new Workbook("PivotMergeLabelsRefreshDisableTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        sheet.AddMergedRegion(new GridRange(Addr(sheet, "E3"), Addr(sheet, "E4")));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I10"),
            ShowSubtotals = false,
            MergeAndCenterLabels = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        sheet.MergedRegions.Should().BeEmpty();
        Text(sheet, "E3").Should().Be("East");
        Text(sheet, "E4").Should().Be("East");
    }

    [Fact]
    public void Refresh_CompactMergeAndCenterLabelsMergesRowLabelHeaderAcrossColumnHeaderRows()
    {
        var workbook = new Workbook("PivotCompactMatrixMergeHeaderTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesProductChannelData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "E5"),
            TargetRange = Range(sheet, "G2", "M10"),
            ReportLayout = PivotReportLayout.Compact,
            MergeAndCenterLabels = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.ColumnFields.Add(new PivotFieldModel(2));
        pivot.ColumnFields.Add(new PivotFieldModel(3));
        pivot.DataFields.Add(new PivotDataFieldModel(4, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        sheet.MergedRegions.Should().Contain(new GridRange(Addr(sheet, "G2"), Addr(sheet, "G3")));
        Text(sheet, "G2").Should().Be("Row Labels");
        sheet.GetCell(Addr(sheet, "G3")).Should().BeNull();
        Text(sheet, "G4").Should().Be("East Widget");
        var headerStyle = workbook.GetStyle(sheet.GetCell(Addr(sheet, "G2"))!.StyleId);
        headerStyle.HorizontalAlignment.Should().Be(HorizontalAlignment.Center);
        headerStyle.VerticalAlignment.Should().Be(VerticalAlignment.Center);
    }

}
