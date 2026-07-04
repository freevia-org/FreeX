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
