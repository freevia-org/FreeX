using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class ChartCommandTests
{
    [Fact]
    public void AddChartCommand_PreservesHiddenFilteredSourceRangeAndDefaultsToPlotVisibleCellsOnly()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.HiddenRows.Add(3);
        sheet.FilterHiddenRows.Add(4);
        sheet.HiddenCols.Add(2);
        sheet.GroupHiddenCols.Add(3);
        var ctx = new TestCommandContext(wb);
        var range = CreateChartRange(sheet);

        var outcome = new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);

        outcome.Success.Should().BeTrue();
        var chart = sheet.Charts.Should().ContainSingle().Subject;
        chart.DataRange.Should().Be(range);
        chart.ShowDataInHiddenRowsAndColumns.Should().BeFalse();
        sheet.HiddenRows.Should().Equal(3u);
        sheet.FilterHiddenRows.Should().Equal(4u);
        sheet.HiddenCols.Should().Equal(2u);
        sheet.GroupHiddenCols.Should().Equal(3u);
    }

    [Fact]
    public void ChangeChartSourceCommand_RejectsProtectedSheetWithoutEditObjectsPermission()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var originalRange = CreateChartRange(sheet);
        new AddChartCommand(sheet.Id, originalRange, ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        sheet.IsProtected = true;
        var newRange = Range(sheet, 2, 2, 6, 5);

        var outcome = new ChangeChartSourceCommand(sheet.Id, chart.Id, newRange).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        chart.DataRange.Should().Be(originalRange);
    }

    [Fact]
    public void ChangeChartSourceCommand_AllowsProtectedSheetWithEditObjectsPermission()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var originalRange = CreateChartRange(sheet);
        new AddChartCommand(sheet.Id, originalRange, ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.EditObjects);
        var newRange = Range(sheet, 2, 2, 6, 5);

        var outcome = new ChangeChartSourceCommand(sheet.Id, chart.Id, newRange).Apply(ctx);

        outcome.Success.Should().BeTrue();
        chart.DataRange.Should().Be(newRange);
    }

    [Fact]
    public void ChangeChartSourceCommand_RejectsPivotCharts()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var originalRange = CreateChartRange(sheet);
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = originalRange,
            IsPivotChart = true,
            PivotTableName = "PivotTable1"
        });
        var chart = sheet.Charts[0];
        var newRange = Range(sheet, 2, 2, 6, 5);

        var outcome = new ChangeChartSourceCommand(sheet.Id, chart.Id, newRange).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("PivotChart");
        chart.DataRange.Should().Be(originalRange);
    }

    [Fact]
    public void ChangeChartSourceCommand_AppliesAndRevertsSwitchRowColumn()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var range = CreateChartRange(sheet);
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        chart.SeriesColumnMappings.Add(new ChartSeriesColumnMapping(0, range.Start.Col + 1));
        chart.VerbatimSeriesFormulas = [new ChartSeriesVerbatimFormulas(0, "Sheet1!$B$2:$B$4", null, null)];
        var command = new ChangeChartSourceCommand(sheet.Id, chart.Id, range, seriesInRows: true);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        chart.SeriesInRows.Should().BeTrue();
        // Column-based series mappings and verbatim formulas describe the old orientation
        // and must not survive a switch.
        chart.SeriesColumnMappings.Should().BeEmpty();
        chart.VerbatimSeriesFormulas.Should().BeNull();

        command.Revert(ctx);

        chart.SeriesInRows.Should().BeFalse();
        chart.SeriesColumnMappings.Should().ContainSingle();
        chart.VerbatimSeriesFormulas.Should().ContainSingle();
    }

    [Fact]
    public void R84_ChangeChartSourceCommand_ClearsAndRevertsStaleOrderMarkerAndMultiLevelCategoryOverridesOnSourceChange()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var range = CreateChartRange(sheet);
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        chart.SeriesOrderOverrides.Add(new ChartSeriesOrderOverride(2, 0));
        chart.MultiLevelCategoryXml.Add(new ChartSeriesRawXmlEntry(2, "<c:multiLvlStrRef/>"));
        chart.PointMarkerFormats.Add(new ChartPointMarkerFormat(2, 0, ChartMarkerStyle.Diamond));
        var newRange = Range(sheet, 2, 2, 6, 5);
        var command = new ChangeChartSourceCommand(sheet.Id, chart.Id, newRange);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        // Per-series/per-point overrides are keyed by SeriesIndex and describe the OLD source's
        // series layout; keeping them after a data-range edit would silently mis-apply them to
        // whichever unrelated series now sits at that index post re-index.
        chart.SeriesOrderOverrides.Should().BeEmpty();
        chart.MultiLevelCategoryXml.Should().BeEmpty();
        chart.PointMarkerFormats.Should().BeEmpty();

        command.Revert(ctx);

        chart.SeriesOrderOverrides.Should().ContainSingle();
        chart.MultiLevelCategoryXml.Should().ContainSingle();
        chart.PointMarkerFormats.Should().ContainSingle();
    }

    [Fact]
    public void R84_ChangeChartSourceCommand_KeepsOrderMarkerAndMultiLevelCategoryOverridesWhenSourceUnchanged()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var range = CreateChartRange(sheet);
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        chart.SeriesOrderOverrides.Add(new ChartSeriesOrderOverride(1, 0));
        chart.MultiLevelCategoryXml.Add(new ChartSeriesRawXmlEntry(1, "<c:multiLvlStrRef/>"));
        chart.PointMarkerFormats.Add(new ChartPointMarkerFormat(1, 0, ChartMarkerStyle.Diamond));
        // Same range and orientation as the chart already has: not a source change, so nothing
        // that's keyed by SeriesIndex should be touched.
        var command = new ChangeChartSourceCommand(sheet.Id, chart.Id, range, firstRowIsHeader: chart.FirstRowIsHeader);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        chart.SeriesOrderOverrides.Should().ContainSingle();
        chart.MultiLevelCategoryXml.Should().ContainSingle();
        chart.PointMarkerFormats.Should().ContainSingle();
    }

    [Fact]
    public void R86_ChangeChartSourceCommand_ClearsAndRevertsStaleComboAxisTrendlineAndDataLabelOverridesOnSourceChange()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var range = CreateChartRange(sheet);
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        // Combo chart: series index 1 flagged as a secondary-axis line overlay, plus a few more
        // SeriesIndex-keyed collections that describe the OLD series layout.
        chart.SecondaryAxisSeriesIndexes.Add(1);
        chart.ComboLineSeriesIndexes.Add(1);
        chart.ComboScatterSeriesIndexes.Add(1);
        chart.ExplodedSlices.Add(new ChartPointExplosion(1, 0, 0.2));
        chart.RangeDataLabels.Add(new ChartRangeDataLabel(1, 0, "Label"));
        chart.SeriesRangeDataLabels.Add(new ChartSeriesRangeDataLabels(1, "Sheet1!$D$1:$D$4", 4, []));
        chart.TrendlineSeriesIndex = 1;
        chart.ErrorBarSeriesIndex = 1;
        var newRange = Range(sheet, 2, 2, 6, 5);
        var command = new ChangeChartSourceCommand(sheet.Id, chart.Id, newRange);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        // Widening/relocating the data range re-indexes series, so all of these SeriesIndex-keyed
        // members must be cleared - otherwise index 1 would silently mis-apply to whichever
        // unrelated series now sits there (e.g. a brand-new series wrongly rendered as the combo
        // line overlay / on the secondary axis).
        chart.SecondaryAxisSeriesIndexes.Should().BeEmpty();
        chart.ComboLineSeriesIndexes.Should().BeEmpty();
        chart.ComboScatterSeriesIndexes.Should().BeEmpty();
        chart.ExplodedSlices.Should().BeEmpty();
        chart.RangeDataLabels.Should().BeEmpty();
        chart.SeriesRangeDataLabels.Should().BeEmpty();
        chart.TrendlineSeriesIndex.Should().Be(0);
        chart.ErrorBarSeriesIndex.Should().Be(0);

        command.Revert(ctx);

        chart.SecondaryAxisSeriesIndexes.Should().Equal(1);
        chart.ComboLineSeriesIndexes.Should().Equal(1);
        chart.ComboScatterSeriesIndexes.Should().Equal(1);
        chart.ExplodedSlices.Should().ContainSingle();
        chart.RangeDataLabels.Should().ContainSingle();
        chart.SeriesRangeDataLabels.Should().ContainSingle();
        chart.TrendlineSeriesIndex.Should().Be(1);
        chart.ErrorBarSeriesIndex.Should().Be(1);
    }

    [Fact]
    public void R87_ChangeChartSourceCommand_ClearsShowLinearTrendlineAndShowErrorBarsOnSourceChange()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var range = CreateChartRange(sheet);
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        // A trendline/error-bar drawn on the 3rd series (SeriesIndex 2), as an Excel-authored
        // workbook would have.
        chart.ShowLinearTrendline = true;
        chart.TrendlineSeriesIndex = 2;
        chart.ShowErrorBars = true;
        chart.ErrorBarSeriesIndex = 2;
        var newRange = Range(sheet, 2, 2, 6, 5);
        var command = new ChangeChartSourceCommand(sheet.Id, chart.Id, newRange);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        // Widening/relocating the data range re-indexes series and clears TrendlineSeriesIndex/
        // ErrorBarSeriesIndex to 0 -- but leaving ShowLinearTrendline/ShowErrorBars true would make
        // the trendline/error-bar silently reattach to whichever unrelated series now sits at index
        // 0 instead of disappearing (matching the list-based siblings above, which clear to []).
        chart.TrendlineSeriesIndex.Should().Be(0);
        chart.ErrorBarSeriesIndex.Should().Be(0);
        chart.ShowLinearTrendline.Should().BeFalse();
        chart.ShowErrorBars.Should().BeFalse();

        command.Revert(ctx);

        chart.TrendlineSeriesIndex.Should().Be(2);
        chart.ErrorBarSeriesIndex.Should().Be(2);
        chart.ShowLinearTrendline.Should().BeTrue();
        chart.ShowErrorBars.Should().BeTrue();
    }

    [Fact]
    public void R87_ChangeChartSourceCommand_KeepsShowLinearTrendlineAndShowErrorBarsWhenSourceUnchanged()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var range = CreateChartRange(sheet);
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        chart.ShowLinearTrendline = true;
        chart.TrendlineSeriesIndex = 2;
        chart.ShowErrorBars = true;
        chart.ErrorBarSeriesIndex = 2;
        // Same range and orientation as the chart already has: not a source change, so the
        // trendline/error-bar flags must survive untouched.
        var command = new ChangeChartSourceCommand(sheet.Id, chart.Id, range, firstRowIsHeader: chart.FirstRowIsHeader);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        chart.TrendlineSeriesIndex.Should().Be(2);
        chart.ErrorBarSeriesIndex.Should().Be(2);
        chart.ShowLinearTrendline.Should().BeTrue();
        chart.ShowErrorBars.Should().BeTrue();
    }

    [Fact]
    public void R86_ChangeChartSourceCommand_KeepsComboAxisTrendlineAndDataLabelOverridesWhenSourceUnchanged()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var range = CreateChartRange(sheet);
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        chart.SecondaryAxisSeriesIndexes.Add(1);
        chart.ComboLineSeriesIndexes.Add(1);
        chart.ComboScatterSeriesIndexes.Add(1);
        chart.ExplodedSlices.Add(new ChartPointExplosion(1, 0, 0.2));
        chart.RangeDataLabels.Add(new ChartRangeDataLabel(1, 0, "Label"));
        chart.SeriesRangeDataLabels.Add(new ChartSeriesRangeDataLabels(1, "Sheet1!$D$1:$D$4", 4, []));
        chart.TrendlineSeriesIndex = 1;
        chart.ErrorBarSeriesIndex = 1;
        // Same range and orientation as the chart already has: not a source change, so nothing
        // that's keyed by SeriesIndex should be touched.
        var command = new ChangeChartSourceCommand(sheet.Id, chart.Id, range, firstRowIsHeader: chart.FirstRowIsHeader);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        chart.SecondaryAxisSeriesIndexes.Should().Equal(1);
        chart.ComboLineSeriesIndexes.Should().Equal(1);
        chart.ComboScatterSeriesIndexes.Should().Equal(1);
        chart.ExplodedSlices.Should().ContainSingle();
        chart.RangeDataLabels.Should().ContainSingle();
        chart.SeriesRangeDataLabels.Should().ContainSingle();
        chart.TrendlineSeriesIndex.Should().Be(1);
        chart.ErrorBarSeriesIndex.Should().Be(1);
    }

    [Fact]
    public void ChangeChartSourceCommand_KeepsOrientationAndMappingsWhenSeriesInRowsOmitted()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var range = CreateChartRange(sheet);
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        new ChangeChartSourceCommand(sheet.Id, chart.Id, range, seriesInRows: true).Apply(ctx);
        var newRange = Range(sheet, 2, 2, 6, 5);

        var outcome = new ChangeChartSourceCommand(sheet.Id, chart.Id, newRange).Apply(ctx);

        outcome.Success.Should().BeTrue();
        chart.DataRange.Should().Be(newRange);
        chart.SeriesInRows.Should().BeTrue();
    }

    [Fact]
    public void ChangeChartSourceCommand_SwitchRowColumnValidatesTransposedShape()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        new AddChartCommand(sheet.Id, Range(sheet, 1, 1, 4, 3), ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        // One data row after the header: fine column-major, but transposed it still must
        // yield at least one series and one point (2 columns → 1 series, 1 point each).
        var singleDataRow = Range(sheet, 1, 1, 2, 2);

        var outcome = new ChangeChartSourceCommand(sheet.Id, chart.Id, singleDataRow, seriesInRows: true).Apply(ctx);

        outcome.Success.Should().BeTrue();
        chart.SeriesInRows.Should().BeTrue();
        ChartTypeSupport.GetDataSeriesCount(chart).Should().Be(1);
        ChartTypeSupport.GetDataPointCount(chart).Should().Be(1);
    }

    [Fact]
    public void ChartTypeSupport_TransposesSeriesAndPointCountsWhenSeriesInRows()
    {
        var sheetId = SheetId.New();
        // 4 rows x 3 cols with header row + category column: column-major = 2 series x 3 points.
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 3))
        };

        ChartTypeSupport.GetDataSeriesCount(chart).Should().Be(2);
        ChartTypeSupport.GetDataPointCount(chart).Should().Be(3);

        chart.SeriesInRows = true;

        // Transposed: series names in the first column, categories in the first row → 3 series x 2 points.
        ChartTypeSupport.GetDataSeriesCount(chart).Should().Be(3);
        ChartTypeSupport.GetDataPointCount(chart).Should().Be(2);
    }

    private static GridRange Range(Sheet sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(
            new CellAddress(sheet.Id, startRow, startCol),
            new CellAddress(sheet.Id, endRow, endCol));
}
