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

    private static GridRange Range(Sheet sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(
            new CellAddress(sheet.Id, startRow, startCol),
            new CellAddress(sheet.Id, endRow, endCol));
}
