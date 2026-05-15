using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

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
        var ctx = new SimpleCtx(wb);
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
        var ctx = new SimpleCtx(wb);
        var dataRange = new GridRange(
            new CellAddress(sheet2.Id, 1, 1),
            new CellAddress(sheet2.Id, 1, 5));
        var location = new CellAddress(sheet1.Id, 1, 6);

        var command = new AddSparklineCommand(sheet1.Id, dataRange, location, SparklineKind.Line);

        command.Apply(ctx).Success.Should().BeFalse();
        sheet1.Sparklines.Should().BeEmpty();
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
