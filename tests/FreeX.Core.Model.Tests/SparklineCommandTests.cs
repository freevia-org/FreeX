using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class SparklineCommandTests
{
    [Theory]
    [InlineData(SparklineKind.Line)]
    [InlineData(SparklineKind.Column)]
    [InlineData(SparklineKind.WinLoss)]
    public void AddSparklineCommand_AddsSparklineAndUndoRemovesIt(SparklineKind kind)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var dataRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 5));
        var location = new CellAddress(sheet.Id, 1, 6);

        var command = new AddSparklineCommand(sheet.Id, dataRange, location, kind);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Sparklines.Should().ContainSingle();
        sheet.Sparklines[0].DataRange.Should().Be(dataRange);
        sheet.Sparklines[0].Location.Should().Be(location);
        sheet.Sparklines[0].Kind.Should().Be(kind);

        command.Revert(ctx);

        sheet.Sparklines.Should().BeEmpty();
    }

    [Fact]
    public void AddSparklineCommand_RejectsRangesOnDifferentSheet()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx = new TestCommandContext(wb);
        var dataRange = new GridRange(
            new CellAddress(sheet2.Id, 1, 1),
            new CellAddress(sheet2.Id, 1, 5));
        var location = new CellAddress(sheet1.Id, 1, 6);

        var command = new AddSparklineCommand(sheet1.Id, dataRange, location, SparklineKind.Line);

        command.Apply(ctx).Success.Should().BeFalse();
        sheet1.Sparklines.Should().BeEmpty();
    }

    [Fact]
    public void AddSparklineCommand_RejectsInvalidSparklineKind()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var dataRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 5));

        var command = new AddSparklineCommand(
            sheet.Id,
            dataRange,
            new CellAddress(sheet.Id, 1, 6),
            (SparklineKind)99);

        command.Apply(ctx).Success.Should().BeFalse();
        sheet.Sparklines.Should().BeEmpty();
    }

    [Fact]
    public void AddSparklineCommand_RejectsOversizedDataRange()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var dataRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, (uint)(SparklineRangeLimits.MaxDataCellCount + 1), 1));

        var command = new AddSparklineCommand(
            sheet.Id,
            dataRange,
            new CellAddress(sheet.Id, 1, 6),
            SparklineKind.Line);

        command.Apply(ctx).Success.Should().BeFalse();
        sheet.Sparklines.Should().BeEmpty();
    }

    private static SparklineModel AddSparkline(Sheet sheet, ICommandContext ctx, SparklineKind kind = SparklineKind.Line)
    {
        var dataRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 5));
        var location = new CellAddress(sheet.Id, 1, 6);
        new AddSparklineCommand(sheet.Id, dataRange, location, kind).Apply(ctx).Success.Should().BeTrue();
        return sheet.Sparklines[0];
    }

    [Fact]
    public void ConfigureSparklineCommand_AppliesSettingsAndUndoRestoresThem()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var sparkline = AddSparkline(sheet, ctx, SparklineKind.Line);

        var settings = new SparklineSettings(
            SparklineKind.Column,
            ShowMarkers: true,
            ShowHighPoint: true,
            ShowLowPoint: false,
            ShowFirstPoint: true,
            ShowLastPoint: false,
            ShowNegativePoints: true,
            SeriesColor: new CellColor(10, 20, 30));
        var command = new ConfigureSparklineCommand(sheet.Id, sparkline.Id, settings);

        command.Apply(ctx).Success.Should().BeTrue();
        sparkline.Kind.Should().Be(SparklineKind.Column);
        sparkline.ShowMarkers.Should().BeTrue();
        sparkline.ShowHighPoint.Should().BeTrue();
        sparkline.ShowNegativePoints.Should().BeTrue();
        sparkline.SeriesColor.Should().Be(new CellColor(10, 20, 30));

        command.Revert(ctx);
        sparkline.Kind.Should().Be(SparklineKind.Line);
        sparkline.ShowMarkers.Should().BeFalse();
        sparkline.ShowHighPoint.Should().BeFalse();
        sparkline.ShowNegativePoints.Should().BeFalse();
        sparkline.SeriesColor.Should().BeNull();
    }

    [Fact]
    public void ConfigureSparklineCommand_RejectsInvalidKindAndMissingSparkline()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var sparkline = AddSparkline(sheet, ctx);

        new ConfigureSparklineCommand(
            sheet.Id,
            sparkline.Id,
            SparklineSettings.Capture(sparkline) with { Kind = (SparklineKind)99 })
            .Apply(ctx).Success.Should().BeFalse();

        new ConfigureSparklineCommand(
            sheet.Id,
            Guid.NewGuid(),
            SparklineSettings.Capture(sparkline))
            .Apply(ctx).Success.Should().BeFalse();
    }

    [Fact]
    public void ClearSparklineCommand_RemovesSparklineAndUndoReinsertsAtSamePosition()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var first = AddSparkline(sheet, ctx);
        var second = new SparklineModel
        {
            DataRange = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 5)),
            Location = new CellAddress(sheet.Id, 2, 6),
            Kind = SparklineKind.Column,
        };
        sheet.Sparklines.Add(second);

        var command = new ClearSparklineCommand(sheet.Id, first.Id);
        command.Apply(ctx).Success.Should().BeTrue();
        sheet.Sparklines.Should().ContainSingle().Which.Should().BeSameAs(second);

        command.Revert(ctx);
        sheet.Sparklines.Should().HaveCount(2);
        sheet.Sparklines[0].Should().BeSameAs(first);
        sheet.Sparklines[1].Should().BeSameAs(second);
    }

    [Fact]
    public void ClearSparklineCommand_RejectsMissingSparkline()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        AddSparkline(sheet, ctx);

        new ClearSparklineCommand(sheet.Id, Guid.NewGuid()).Apply(ctx).Success.Should().BeFalse();
        sheet.Sparklines.Should().ContainSingle();
    }
}
