using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class ChartCommandTests
{
    [Fact]
    public void SetChartBoundsCommand_UpdatesEmbeddedChartBoundsAndUndoRestoresThem()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var range = CreateChartRange(sheet);
        new AddChartCommand(
            sheet.Id,
            range,
            ChartType.Column,
            "Sales",
            left: 20,
            top: 30,
            width: 400,
            height: 300).Apply(ctx);
        var chart = sheet.Charts.Should().ContainSingle().Subject;

        var command = new SetChartBoundsCommand(sheet.Id, chart.Id, 72, 96, 480, 270);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.AffectedCells.Should().ContainSingle().Which.Should().Be(range.Start);
        chart.Left.Should().Be(72);
        chart.Top.Should().Be(96);
        chart.Width.Should().Be(480);
        chart.Height.Should().Be(270);

        command.Revert(ctx);

        chart.Left.Should().Be(20);
        chart.Top.Should().Be(30);
        chart.Width.Should().Be(400);
        chart.Height.Should().Be(300);
    }

    [Theory]
    [InlineData(double.NaN, 20, 320, 180, "finite")]
    [InlineData(20, double.PositiveInfinity, 320, 180, "finite")]
    [InlineData(20, 20, 0, 180, "positive")]
    [InlineData(20, 20, 320, -1, "positive")]
    public void SetChartBoundsCommand_RejectsInvalidPositionOrSize(
        double left,
        double top,
        double width,
        double height,
        string message)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        new AddChartCommand(sheet.Id, CreateChartRange(sheet), ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts.Should().ContainSingle().Subject;

        var outcome = new SetChartBoundsCommand(sheet.Id, chart.Id, left, top, width, height).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain(message);
        chart.Left.Should().Be(20);
        chart.Top.Should().Be(20);
        chart.Width.Should().Be(400);
        chart.Height.Should().Be(300);
    }

    [Fact]
    public void SetChartBoundsCommand_RejectsProtectedSheetWithoutEditObjectsPermission()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        new AddChartCommand(sheet.Id, CreateChartRange(sheet), ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts.Should().ContainSingle().Subject;
        sheet.IsProtected = true;

        var outcome = new SetChartBoundsCommand(sheet.Id, chart.Id, 64, 48, 320, 180).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        chart.Left.Should().Be(20);
        chart.Top.Should().Be(20);
        chart.Width.Should().Be(400);
        chart.Height.Should().Be(300);
    }
}
