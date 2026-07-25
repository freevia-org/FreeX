using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PivotTableRefreshServiceTests
{
    [Fact]
    public void ExtractDetailRows_ReturnsSourceRowsBehindPivotOutputRow()
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
            // R90-render-pivot-layout-5-1/5-3: pin the (former) Tabular/no-subtotal defaults this
            // drill-down cell-address test was written against.
            ReportLayout = PivotReportLayout.Tabular,
            ShowSubtotals = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var detail = PivotTableRefreshService.ExtractDetailRows(workbook, sheet, pivot, Addr(sheet, "G3"));

        detail.Headers.Should().Equal("Region", "Quarter", "Amount");
        detail.Rows.Should().ContainSingle();
        detail.Rows[0].Select(PivotValueText).Should().Equal("East", "Q1", "10");
    }

    [Fact]
    public void ExtractDetailRows_ForRowLabelCell_ReturnsNoRows()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "H8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var detail = PivotTableRefreshService.ExtractDetailRows(workbook, sheet, pivot, Addr(sheet, "E3"));

        detail.Headers.Should().Equal("Region", "Quarter", "Amount");
        detail.Rows.Should().BeEmpty();
    }

    [Fact]
    public void ExtractDetailRows_ForMatrixValueCell_FiltersByColumnField()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var detail = PivotTableRefreshService.ExtractDetailRows(workbook, sheet, pivot, Addr(sheet, "F3"));

        detail.Headers.Should().Equal("Region", "Quarter", "Amount");
        detail.Rows.Should().ContainSingle();
        detail.Rows[0].Select(PivotValueText).Should().Equal("East", "Q1", "10");
    }

    [Fact]
    public void ExtractDetailRows_ForGrandTotal_ReturnsAllFilteredSourceRows()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "H8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var detail = PivotTableRefreshService.ExtractDetailRows(workbook, sheet, pivot, Addr(sheet, "F5"));

        detail.Headers.Should().Equal("Region", "Quarter", "Amount");
        detail.Rows.Should().HaveCount(4);
        var rowTexts = detail.Rows.Select(row => string.Join("|", row.Select(PivotValueText))).ToList();
        rowTexts.Should().Contain("East|Q1|10");
        rowTexts.Should().Contain("West|Q2|25");
    }

    [Fact]
    public void ExtractDetailRows_ForSubtotal_ReturnsSourceRowsInSubtotalGroup()
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
            // R90-render-pivot-layout-5-1/5-3: pin the (former) Bottom/Tabular defaults this
            // drill-down cell-address test was written against.
            SubtotalPlacement = PivotSubtotalPlacement.Bottom,
            ReportLayout = PivotReportLayout.Tabular
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var detail = PivotTableRefreshService.ExtractDetailRows(workbook, sheet, pivot, Addr(sheet, "G5"));

        detail.Headers.Should().Equal("Region", "Quarter", "Amount");
        detail.Rows.Should().HaveCount(2);
        var rowTexts = detail.Rows.Select(row => string.Join("|", row.Select(PivotValueText))).ToList();
        rowTexts.Should().Contain("East|Q1|10");
        rowTexts.Should().Contain("East|Q2|15");
    }

    [Fact]
    public void ExtractDetailRows_WhenRepeatLabelsAreOff_UsesNearestVisibleOuterLabel()
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
            RepeatItemLabels = false,
            // R90-render-pivot-layout-5-1/5-3: pin the (former) Tabular/no-subtotal defaults this
            // drill-down cell-address test was written against.
            ReportLayout = PivotReportLayout.Tabular,
            ShowSubtotals = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var detail = PivotTableRefreshService.ExtractDetailRows(workbook, sheet, pivot, Addr(sheet, "G4"));

        detail.Headers.Should().Equal("Region", "Quarter", "Amount");
        detail.Rows.Should().ContainSingle();
        detail.Rows[0].Select(PivotValueText).Should().Equal("East", "Q2", "15");
    }

    [Fact]
    public void ExtractDetailRows_ForCompactLayoutValueCell_UsesRenderedValueColumnAndCollapsedRowLabels()
    {
        var workbook = new Workbook("PivotCompactDrillDownTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "H10"),
            ReportLayout = PivotReportLayout.Compact,
            // R90-render-pivot-layout-5-1: pin the (former) no-subtotal default -- this test is about
            // the compact collapsed-row-label drill-down mapping, not subtotals.
            ShowSubtotals = false
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var detail = PivotTableRefreshService.ExtractDetailRows(workbook, sheet, pivot, Addr(sheet, "F4"));

        detail.Headers.Should().Equal("Region", "Quarter", "Amount");
        detail.Rows.Should().ContainSingle();
        detail.Rows[0].Select(PivotValueText).Should().Equal("East", "Q1", "10");
    }

    [Fact]
    public void ExtractDetailRows_ForColumnOnlyPivot_FiltersByColumnItem()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I5")
        };
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var detail = PivotTableRefreshService.ExtractDetailRows(workbook, sheet, pivot, Addr(sheet, "F3"));

        detail.Headers.Should().Equal("Region", "Quarter", "Amount");
        detail.Rows.Should().HaveCount(2);
        var rowTexts = detail.Rows.Select(row => string.Join("|", row.Select(PivotValueText))).ToList();
        rowTexts.Should().Contain("East|Q2|15");
        rowTexts.Should().Contain("West|Q2|25");
    }

}
