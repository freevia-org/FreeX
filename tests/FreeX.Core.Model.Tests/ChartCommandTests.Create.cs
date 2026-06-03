using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class ChartCommandTests
{

    [Theory]
    [InlineData(ChartType.Column)]
    [InlineData(ChartType.StackedColumn)]
    [InlineData(ChartType.PercentStackedColumn)]
    [InlineData(ChartType.Line)]
    [InlineData(ChartType.ThreeDLine)]
    [InlineData(ChartType.Pie)]
    [InlineData(ChartType.Doughnut)]
    [InlineData(ChartType.Bar)]
    [InlineData(ChartType.StackedBar)]
    [InlineData(ChartType.PercentStackedBar)]
    [InlineData(ChartType.Scatter)]
    [InlineData(ChartType.Bubble)]
    [InlineData(ChartType.Area)]
    [InlineData(ChartType.ThreeDArea)]
    public void AddChartCommand_AddsRequestedChartTypeAndUndoRemovesIt(ChartType type)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 4));

        var command = new AddChartCommand(sheet.Id, range, type, "Sales");

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts.Should().ContainSingle();
        sheet.Charts[0].Type.Should().Be(type);
        sheet.Charts[0].DataRange.Should().Be(range);
        sheet.Charts[0].Title.Should().Be("Sales");

        command.Revert(ctx);

        sheet.Charts.Should().BeEmpty();
    }

    [Fact]
    public void AddChartCommand_RejectsDataRangeOnDifferentSheet()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet2.Id, 1, 1),
            new CellAddress(sheet2.Id, 3, 2));

        var command = new AddChartCommand(sheet1.Id, range, ChartType.Column);

        command.Apply(ctx).Success.Should().BeFalse();
        sheet1.Charts.Should().BeEmpty();
    }

    [Fact]
    public void AddChartCommand_RejectsProtectedSheetWithoutEditObjectsPermission()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new SimpleCtx(wb);
        var range = CreateChartRange(sheet);

        var outcome = new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.Charts.Should().BeEmpty();
    }

    [Fact]
    public void AddChartCommand_AllowsProtectedSheetWithEditObjectsPermission()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.EditObjects);
        var ctx = new SimpleCtx(wb);
        var range = CreateChartRange(sheet);

        var outcome = new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.Charts.Should().ContainSingle();
    }

    [Fact]
    public void AddChartCommand_ReplacesInvalidChartTypeWithColumn()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));

        var command = new AddChartCommand(sheet.Id, range, (ChartType)99, "Sales");

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts.Should().ContainSingle().Which.Type.Should().Be(ChartType.Column);
        sheet.Charts[0].FirstColIsCategories.Should().BeTrue();
    }

    [Fact]
    public void AddChartCommand_RejectsMapChartType()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 3));

        var outcome = new AddChartCommand(sheet.Id, range, ChartType.Map, "Sales").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("recognized for XLSX preservation");
        sheet.Charts.Should().BeEmpty();
    }

    [Fact]
    public void AddChartSheetCommand_RejectsDeferredAdvancedFamiliesBeforeCreatingSheet()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 3));

        var outcome = new AddChartSheetCommand(sheet.Id, range, ChartType.Map, "Map").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("recognized for XLSX preservation");
        wb.Sheets.Should().ContainSingle().Which.Should().BeSameAs(sheet);
    }

    [Theory]
    [InlineData(ChartType.Treemap)]
    [InlineData(ChartType.Sunburst)]
    [InlineData(ChartType.Histogram)]
    [InlineData(ChartType.Pareto)]
    [InlineData(ChartType.BoxAndWhisker)]
    [InlineData(ChartType.Waterfall)]
    [InlineData(ChartType.Funnel)]
    public void AddChartCommand_AcceptsAdvancedChartFamilies(ChartType type)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 3));

        var outcome = new AddChartCommand(sheet.Id, range, type, "Sales").Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.Charts.Should().ContainSingle().Which.Type.Should().Be(type);
    }

    [Fact]
    public void AddChartCommand_RejectsInvalidInitialSize()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));

        new AddChartCommand(sheet.Id, range, ChartType.Column, width: double.NaN)
            .Apply(ctx).Success.Should().BeFalse();
        new AddChartCommand(sheet.Id, range, ChartType.Column, height: double.PositiveInfinity)
            .Apply(ctx).Success.Should().BeFalse();
        new AddChartCommand(sheet.Id, range, ChartType.Column, width: 0)
            .Apply(ctx).Success.Should().BeFalse();

        sheet.Charts.Should().BeEmpty();
    }

    [Fact]
    public void AddChartCommand_RejectsRangesWithoutDataPoints()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var headerOnlyRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 2));

        var outcome = new AddChartCommand(sheet.Id, headerOnlyRange, ChartType.Column).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.Charts.Should().BeEmpty();
    }

    [Fact]
    public void AddChartCommand_RejectsRangesWithoutDataSeries()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var incompleteBubbleRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));

        var outcome = new AddChartCommand(sheet.Id, incompleteBubbleRange, ChartType.Bubble).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.Charts.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ChartType.Scatter)]
    [InlineData(ChartType.Bubble)]
    public void AddChartCommand_UsesNumericFirstColumnForXyCharts(ChartType type)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 3));

        var command = new AddChartCommand(sheet.Id, range, type);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts.Should().ContainSingle();
        sheet.Charts[0].FirstColIsCategories.Should().BeFalse();
    }

    [Fact]
    public void AddChartSheetCommand_CreatesDefaultChartSheetAndUndoRemovesIt()
    {
        var wb = new Workbook("test");
        var source = wb.AddSheet("Sheet1");
        wb.AddSheet("Chart1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(source.Id, 1, 1),
            new CellAddress(source.Id, 4, 3));

        var command = new AddChartSheetCommand(source.Id, range, ChartType.Column, "Chart");

        command.Apply(ctx).Success.Should().BeTrue();

        command.CreatedSheetId.Should().NotBeNull();
        source.Charts.Should().BeEmpty();
        var chartSheet = wb.Sheets.Single(sheet => sheet.Name == "Chart2");
        chartSheet.Id.Should().Be(command.CreatedSheetId!.Value);
        chartSheet.Charts.Should().ContainSingle();
        chartSheet.Charts[0].Type.Should().Be(ChartType.Column);
        chartSheet.Charts[0].DataRange.Should().Be(range);

        command.Revert(ctx);

        wb.Sheets.Should().NotContain(sheet => sheet.Name == "Chart2");
        source.Charts.Should().BeEmpty();
    }

    [Fact]
    public void ChangeChartTypeCommand_UpdatesNormalChartAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 3));
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];

        var command = new ChangeChartTypeCommand(sheet.Id, chart.Id, ChartType.Scatter);

        command.Apply(ctx).Success.Should().BeTrue();

        chart.Type.Should().Be(ChartType.Scatter);
        chart.FirstColIsCategories.Should().BeFalse();

        command.Revert(ctx);

        chart.Type.Should().Be(ChartType.Column);
        chart.FirstColIsCategories.Should().BeTrue();
    }

    [Fact]
    public void ChangeChartTypeCommand_RejectsDeferredAdvancedFamiliesBeforeMutation()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 3));
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];

        var outcome = new ChangeChartTypeCommand(sheet.Id, chart.Id, ChartType.Map).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("recognized for XLSX preservation");
        chart.Type.Should().Be(ChartType.Column);
    }

    [Fact]
    public void ChangeChartTypeCommand_RejectsPivotCharts()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 3));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = range,
            IsPivotChart = true,
            PivotTableName = "PivotTable1"
        });

        var outcome = new ChangeChartTypeCommand(sheet.Id, sheet.Charts[0].Id, ChartType.Line).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.Charts[0].Type.Should().Be(ChartType.Column);
    }

    [Fact]
    public void ChangeChartSourceCommand_UpdatesNormalChartSourceAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var originalRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 3));
        var newRange = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 6, 5));
        new AddChartCommand(sheet.Id, originalRange, ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];

        var command = new ChangeChartSourceCommand(
            sheet.Id,
            chart.Id,
            newRange,
            firstRowIsHeader: false,
            firstColIsCategories: false);

        command.Apply(ctx).Success.Should().BeTrue();

        chart.DataRange.Should().Be(newRange);
        chart.FirstRowIsHeader.Should().BeFalse();
        chart.FirstColIsCategories.Should().BeFalse();

        command.Revert(ctx);

        chart.DataRange.Should().Be(originalRange);
        chart.FirstRowIsHeader.Should().BeTrue();
        chart.FirstColIsCategories.Should().BeTrue();
    }

    [Fact]
    public void ChangeChartSourceCommand_RejectsRangesOnDifferentSheet()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx = new SimpleCtx(wb);
        var originalRange = new GridRange(
            new CellAddress(sheet1.Id, 1, 1),
            new CellAddress(sheet1.Id, 4, 3));
        var otherSheetRange = new GridRange(
            new CellAddress(sheet2.Id, 1, 1),
            new CellAddress(sheet2.Id, 4, 3));
        new AddChartCommand(sheet1.Id, originalRange, ChartType.Column, "Sales").Apply(ctx);

        var outcome = new ChangeChartSourceCommand(sheet1.Id, sheet1.Charts[0].Id, otherSheetRange).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet1.Charts[0].DataRange.Should().Be(originalRange);
    }

    [Fact]
    public void MoveChartCommand_MovesNormalChartToExistingSheetAndUndoRestores()
    {
        var wb = new Workbook("test");
        var source = wb.AddSheet("Source");
        var target = wb.AddSheet("Dashboard");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(source.Id, 1, 1),
            new CellAddress(source.Id, 4, 3));
        new AddChartCommand(source.Id, range, ChartType.Column, "Sales").Apply(ctx);
        var chart = source.Charts[0];

        var command = new MoveChartCommand(source.Id, chart.Id, target.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        source.Charts.Should().BeEmpty();
        target.Charts.Should().ContainSingle().Which.Id.Should().Be(chart.Id);

        command.Revert(ctx);

        source.Charts.Should().ContainSingle().Which.Id.Should().Be(chart.Id);
        target.Charts.Should().BeEmpty();
    }

    [Fact]
    public void MoveChartCommand_RejectsProtectedSourceWithoutEditObjectsPermission()
    {
        var wb = new Workbook("test");
        var source = wb.AddSheet("Source");
        var target = wb.AddSheet("Dashboard");
        var ctx = new SimpleCtx(wb);
        var range = CreateChartRange(source);
        new AddChartCommand(source.Id, range, ChartType.Column, "Sales").Apply(ctx);
        var chart = source.Charts[0];
        source.IsProtected = true;

        var outcome = new MoveChartCommand(source.Id, chart.Id, target.Id).Apply(ctx);

        outcome.Success.Should().BeFalse();
        source.Charts.Should().Contain(chart);
        target.Charts.Should().BeEmpty();
    }

    [Fact]
    public void MoveChartToNewSheetCommand_CreatesSheetAndUndoRemovesIt()
    {
        var wb = new Workbook("test");
        var source = wb.AddSheet("Source");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(source.Id, 1, 1),
            new CellAddress(source.Id, 4, 3));
        new AddChartCommand(source.Id, range, ChartType.Line, "Sales").Apply(ctx);
        var chart = source.Charts[0];

        var command = new MoveChartToNewSheetCommand(source.Id, chart.Id, "Sales Chart");

        command.Apply(ctx).Success.Should().BeTrue();

        source.Charts.Should().BeEmpty();
        var chartSheet = wb.Sheets.Single(sheet => sheet.Name == "Sales Chart");
        chartSheet.Charts.Should().ContainSingle().Which.Id.Should().Be(chart.Id);

        command.Revert(ctx);

        wb.Sheets.Should().NotContain(sheet => sheet.Name == "Sales Chart");
        source.Charts.Should().ContainSingle().Which.Id.Should().Be(chart.Id);
    }
}
