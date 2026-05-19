using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class FillSeriesPlannerTests
{
    [Theory]
    [InlineData("1", true, 1)]
    [InlineData(" -2.5 ", true, -2.5)]
    [InlineData("0", true, 0)]
    [InlineData("", false, 0)]
    [InlineData("NaN", false, 0)]
    [InlineData("Infinity", false, 0)]
    [InlineData("step", false, 0)]
    public void TryParseStep_ParsesFiniteNumericStep(string input, bool expected, double expectedStep)
    {
        var result = FillSeriesPlanner.TryParseStep(input, out var step);

        result.Should().Be(expected);
        step.Should().Be(expectedStep);
    }

    [Theory]
    [InlineData(FillCellsDirection.Down, 2, 1, true)]
    [InlineData(FillCellsDirection.Up, 2, 1, true)]
    [InlineData(FillCellsDirection.Right, 1, 2, true)]
    [InlineData(FillCellsDirection.Left, 1, 2, true)]
    [InlineData(FillCellsDirection.Down, 1, 2, false)]
    [InlineData(FillCellsDirection.Right, 2, 1, false)]
    public void CanFill_RequiresMultipleCellsInFillDirection(FillCellsDirection direction, uint rows, uint columns, bool expected)
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, rows, columns));

        FillSeriesPlanner.CanFill(range, direction).Should().Be(expected);
    }

    [Fact]
    public void BuildLinearSeriesEdits_FillsRowMajorCellsAfterStartingCell()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 3, 3));
        sheet.SetCell(range.Start, new NumberValue(10));

        var edits = FillSeriesPlanner.BuildLinearSeriesEdits(sheet, range, step: 2);

        edits.Should().HaveCount(3);
        edits.Select(edit => edit.Address).Should().Equal(
            new CellAddress(sheet.Id, 2, 3),
            new CellAddress(sheet.Id, 3, 2),
            new CellAddress(sheet.Id, 3, 3));
        edits.Select(edit => ((NumberValue)edit.NewCell.Value).Value).Should().Equal(12, 14, 16);
    }

    [Fact]
    public void BuildLinearSeriesEdits_ReturnsNoEditsWhenStartingCellIsNotNumeric()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 3));
        sheet.SetCell(range.Start, new TextValue("Start"));

        FillSeriesPlanner.BuildLinearSeriesEdits(sheet, range, step: 1).Should().BeEmpty();
    }
}
