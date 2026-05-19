using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class BorderPickerPlannerTests
{
    private static readonly CellColor Accent = new(33, 115, 70);

    [Theory]
    [InlineData(BorderPickerChoice.All)]
    [InlineData(BorderPickerChoice.Outline)]
    [InlineData(BorderPickerChoice.Inside)]
    [InlineData(BorderPickerChoice.Top)]
    [InlineData(BorderPickerChoice.Right)]
    [InlineData(BorderPickerChoice.Bottom)]
    [InlineData(BorderPickerChoice.Left)]
    public void Plan_CreatesBordersWithCallerProvidedColor(BorderPickerChoice choice)
    {
        var range = Range(2, 3, 4, 5);
        var address = new CellAddress(range.Start.Sheet, 2, 3);

        var diff = BorderPickerPlanner.Plan(choice, range, address, Accent);

        GetBorders(diff)
            .Where(border => border is not null)
            .Should()
            .OnlyContain(border => border!.Value.Color == Accent);
    }

    [Fact]
    public void Plan_NoneClearsAllBorders()
    {
        var range = Range(2, 3, 4, 5);

        var diff = BorderPickerPlanner.Plan(BorderPickerChoice.None, range, range.Start, Accent);

        diff.BorderTop.Should().Be(new CellBorder(BorderStyle.None, Accent));
        diff.BorderRight.Should().Be(new CellBorder(BorderStyle.None, Accent));
        diff.BorderBottom.Should().Be(new CellBorder(BorderStyle.None, Accent));
        diff.BorderLeft.Should().Be(new CellBorder(BorderStyle.None, Accent));
    }

    [Fact]
    public void Plan_OutlineAppliesOnlyOuterEdgesForEachCellInSelectedRange()
    {
        var range = Range(2, 3, 4, 5);

        var topLeft = BorderPickerPlanner.Plan(BorderPickerChoice.Outline, range, new CellAddress(range.Start.Sheet, 2, 3), Accent);
        var center = BorderPickerPlanner.Plan(BorderPickerChoice.Outline, range, new CellAddress(range.Start.Sheet, 3, 4), Accent);
        var bottomRight = BorderPickerPlanner.Plan(BorderPickerChoice.Outline, range, new CellAddress(range.Start.Sheet, 4, 5), Accent);

        topLeft.BorderTop.Should().Be(new CellBorder(BorderStyle.Thin, Accent));
        topLeft.BorderLeft.Should().Be(new CellBorder(BorderStyle.Thin, Accent));
        topLeft.BorderRight.Should().BeNull();
        topLeft.BorderBottom.Should().BeNull();

        center.BorderTop.Should().BeNull();
        center.BorderRight.Should().BeNull();
        center.BorderBottom.Should().BeNull();
        center.BorderLeft.Should().BeNull();

        bottomRight.BorderRight.Should().Be(new CellBorder(BorderStyle.Thin, Accent));
        bottomRight.BorderBottom.Should().Be(new CellBorder(BorderStyle.Thin, Accent));
        bottomRight.BorderTop.Should().BeNull();
        bottomRight.BorderLeft.Should().BeNull();
    }

    [Fact]
    public void Plan_InsideAppliesInteriorEdgesOnly()
    {
        var range = Range(2, 3, 4, 5);
        var topLeft = BorderPickerPlanner.Plan(BorderPickerChoice.Inside, range, new CellAddress(range.Start.Sheet, 2, 3), Accent);
        var center = BorderPickerPlanner.Plan(BorderPickerChoice.Inside, range, new CellAddress(range.Start.Sheet, 3, 4), Accent);
        var bottomRight = BorderPickerPlanner.Plan(BorderPickerChoice.Inside, range, new CellAddress(range.Start.Sheet, 4, 5), Accent);

        topLeft.BorderTop.Should().BeNull();
        topLeft.BorderLeft.Should().BeNull();
        topLeft.BorderRight.Should().Be(new CellBorder(BorderStyle.Thin, Accent));
        topLeft.BorderBottom.Should().Be(new CellBorder(BorderStyle.Thin, Accent));

        center.BorderTop.Should().Be(new CellBorder(BorderStyle.Thin, Accent));
        center.BorderRight.Should().Be(new CellBorder(BorderStyle.Thin, Accent));
        center.BorderBottom.Should().Be(new CellBorder(BorderStyle.Thin, Accent));
        center.BorderLeft.Should().Be(new CellBorder(BorderStyle.Thin, Accent));

        bottomRight.BorderTop.Should().Be(new CellBorder(BorderStyle.Thin, Accent));
        bottomRight.BorderLeft.Should().Be(new CellBorder(BorderStyle.Thin, Accent));
        bottomRight.BorderRight.Should().BeNull();
        bottomRight.BorderBottom.Should().BeNull();
    }

    [Theory]
    [InlineData(BorderPickerChoice.Top)]
    [InlineData(BorderPickerChoice.Right)]
    [InlineData(BorderPickerChoice.Bottom)]
    [InlineData(BorderPickerChoice.Left)]
    public void Plan_EdgeChoicesSetOnlyRequestedEdge(BorderPickerChoice choice)
    {
        var range = Range(2, 3, 4, 5);

        var diff = BorderPickerPlanner.Plan(choice, range, range.Start, Accent);

        (diff.BorderTop is not null).Should().Be(choice == BorderPickerChoice.Top);
        (diff.BorderRight is not null).Should().Be(choice == BorderPickerChoice.Right);
        (diff.BorderBottom is not null).Should().Be(choice == BorderPickerChoice.Bottom);
        (diff.BorderLeft is not null).Should().Be(choice == BorderPickerChoice.Left);
    }

    private static GridRange Range(uint startRow, uint startCol, uint endRow, uint endCol)
    {
        var sheetId = SheetId.New();
        return new GridRange(
            new CellAddress(sheetId, startRow, startCol),
            new CellAddress(sheetId, endRow, endCol));
    }

    private static IEnumerable<CellBorder?> GetBorders(StyleDiff diff)
    {
        yield return diff.BorderTop;
        yield return diff.BorderRight;
        yield return diff.BorderBottom;
        yield return diff.BorderLeft;
    }
}
