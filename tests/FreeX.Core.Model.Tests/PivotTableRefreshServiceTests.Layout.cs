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

    [Fact]
    public void Refresh_HidesGrandTotalWhenDisabled()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G8"),
            ShowGrandTotals = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("East");
        Text(sheet, "E4").Should().Be("West");
        sheet.GetCell(Addr(sheet, "E5")).Should().BeNull();
        sheet.GetCell(Addr(sheet, "F5")).Should().BeNull();
    }

    [Fact]
    public void Refresh_MatrixCanHideRowGrandTotalsWhileKeepingColumnGrandTotals()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I8"),
            ShowRowGrandTotals = false,
            ShowColumnGrandTotals = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "F2").Should().Be("Q1");
        Text(sheet, "G2").Should().Be("Q2");
        sheet.GetCell(Addr(sheet, "H2")).Should().BeNull();
        Text(sheet, "E5").Should().Be("Grand Total");
        Number(sheet, "F5").Should().Be(30);
        Number(sheet, "G5").Should().Be(40);
        sheet.GetCell(Addr(sheet, "H5")).Should().BeNull();
    }

    [Fact]
    public void Refresh_MatrixCanHideColumnGrandTotalsWhileKeepingRowGrandTotals()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I8"),
            ShowRowGrandTotals = true,
            ShowColumnGrandTotals = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "H2").Should().Be("Grand Total");
        Number(sheet, "H3").Should().Be(25);
        Number(sheet, "H4").Should().Be(45);
        sheet.GetCell(Addr(sheet, "E5")).Should().BeNull();
        sheet.GetCell(Addr(sheet, "F5")).Should().BeNull();
    }

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
