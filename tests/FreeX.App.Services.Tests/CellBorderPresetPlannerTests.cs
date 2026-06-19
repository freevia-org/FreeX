using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class CellBorderPresetPlannerTests
{
    private static readonly CellColor Accent = new(33, 115, 70);

    [Theory]
    [InlineData(CellBorderPreset.All)]
    [InlineData(CellBorderPreset.Outside)]
    [InlineData(CellBorderPreset.Inside)]
    [InlineData(CellBorderPreset.Top)]
    [InlineData(CellBorderPreset.Right)]
    [InlineData(CellBorderPreset.Bottom)]
    [InlineData(CellBorderPreset.Left)]
    public void Plan_CreatesBordersWithCallerProvidedStyleAndColor(CellBorderPreset preset)
    {
        var range = Range(2, 3, 4, 5);
        var address = new CellAddress(range.Start.Sheet, 2, 3);

        var diff = CellBorderPresetPlanner.Plan(preset, range, address, BorderStyle.Double, Accent);

        GetBorders(diff)
            .Where(border => border is not null)
            .Should()
            .OnlyContain(border => border!.Value == new CellBorder(BorderStyle.Double, Accent));
    }

    [Fact]
    public void Plan_NoBorderClearsAllBorders()
    {
        var range = Range(2, 3, 4, 5);

        var diff = CellBorderPresetPlanner.Plan(CellBorderPreset.NoBorder, range, range.Start, color: Accent);

        diff.BorderTop.Should().Be(new CellBorder(BorderStyle.None));
        diff.BorderRight.Should().Be(new CellBorder(BorderStyle.None));
        diff.BorderBottom.Should().Be(new CellBorder(BorderStyle.None));
        diff.BorderLeft.Should().Be(new CellBorder(BorderStyle.None));
    }

    [Fact]
    public void Plan_OutsideAppliesOnlyOuterEdgesForEachCellInSelectedRange()
    {
        var range = Range(2, 3, 4, 5);

        var topLeft = CellBorderPresetPlanner.Plan(CellBorderPreset.Outside, range, new CellAddress(range.Start.Sheet, 2, 3), color: Accent);
        var center = CellBorderPresetPlanner.Plan(CellBorderPreset.Outside, range, new CellAddress(range.Start.Sheet, 3, 4), color: Accent);
        var bottomRight = CellBorderPresetPlanner.Plan(CellBorderPreset.Outside, range, new CellAddress(range.Start.Sheet, 4, 5), color: Accent);

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
        var topLeft = CellBorderPresetPlanner.Plan(CellBorderPreset.Inside, range, new CellAddress(range.Start.Sheet, 2, 3), color: Accent);
        var center = CellBorderPresetPlanner.Plan(CellBorderPreset.Inside, range, new CellAddress(range.Start.Sheet, 3, 4), color: Accent);
        var bottomRight = CellBorderPresetPlanner.Plan(CellBorderPreset.Inside, range, new CellAddress(range.Start.Sheet, 4, 5), color: Accent);

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
    [InlineData(CellBorderPreset.Top)]
    [InlineData(CellBorderPreset.Right)]
    [InlineData(CellBorderPreset.Bottom)]
    [InlineData(CellBorderPreset.Left)]
    public void Plan_EdgeChoicesSetOnlyRequestedEdge(CellBorderPreset preset)
    {
        var range = Range(2, 3, 4, 5);

        var diff = CellBorderPresetPlanner.Plan(preset, range, range.Start, color: Accent);

        (diff.BorderTop is not null).Should().Be(preset == CellBorderPreset.Top);
        (diff.BorderRight is not null).Should().Be(preset == CellBorderPreset.Right);
        (diff.BorderBottom is not null).Should().Be(preset == CellBorderPreset.Bottom);
        (diff.BorderLeft is not null).Should().Be(preset == CellBorderPreset.Left);
    }

    [Theory]
    [InlineData(CellBorderPreset.All, "All Borders")]
    [InlineData(CellBorderPreset.Outside, "Outside Borders")]
    [InlineData(CellBorderPreset.Inside, "Inside Borders")]
    [InlineData(CellBorderPreset.NoBorder, "No Border")]
    [InlineData(CellBorderPreset.Top, "Top Border")]
    [InlineData(CellBorderPreset.Right, "Right Border")]
    [InlineData(CellBorderPreset.Bottom, "Bottom Border")]
    [InlineData(CellBorderPreset.Left, "Left Border")]
    [InlineData(CellBorderPreset.ThickBottom, "Thick Bottom Border")]
    [InlineData(CellBorderPreset.DoubleBottom, "Bottom Double Border")]
    [InlineData(CellBorderPreset.ThickOutside, "Thick Outside Borders")]
    [InlineData(CellBorderPreset.TopAndBottom, "Top and Bottom Border")]
    [InlineData(CellBorderPreset.TopAndThickBottom, "Top and Thick Bottom Border")]
    [InlineData(CellBorderPreset.TopAndDoubleBottom, "Top and Double Bottom Border")]
    public void GetDisplayName_ReturnsMenuText(CellBorderPreset preset, string expected)
    {
        CellBorderPresetPlanner.GetDisplayName(preset).Should().Be(expected);
    }

    [Theory]
    [InlineData(CellBorderPreset.All, false)]
    [InlineData(CellBorderPreset.Outside, true)]
    [InlineData(CellBorderPreset.Inside, true)]
    [InlineData(CellBorderPreset.NoBorder, false)]
    [InlineData(CellBorderPreset.Top, false)]
    [InlineData(CellBorderPreset.Right, false)]
    [InlineData(CellBorderPreset.Bottom, false)]
    [InlineData(CellBorderPreset.Left, false)]
    [InlineData(CellBorderPreset.ThickBottom, false)]
    [InlineData(CellBorderPreset.DoubleBottom, false)]
    [InlineData(CellBorderPreset.ThickOutside, true)]
    [InlineData(CellBorderPreset.TopAndBottom, true)]
    [InlineData(CellBorderPreset.TopAndThickBottom, true)]
    [InlineData(CellBorderPreset.TopAndDoubleBottom, true)]
    public void RequiresPerCellPlanning_IdentifiesRangeRelativePresets(CellBorderPreset preset, bool expected)
    {
        CellBorderPresetPlanner.RequiresPerCellPlanning(preset).Should().Be(expected);
    }

    [Theory]
    [InlineData(CellBorderPreset.ThickBottom, BorderStyle.Thick)]
    [InlineData(CellBorderPreset.DoubleBottom, BorderStyle.Double)]
    public void Plan_CompoundBottomPresetsSetOnlyBottomEdgeWithExpectedStyle(CellBorderPreset preset, BorderStyle expectedStyle)
    {
        var range = Range(2, 3, 4, 5);

        var diff = CellBorderPresetPlanner.Plan(preset, range, range.Start, color: Accent);

        diff.BorderBottom.Should().Be(new CellBorder(expectedStyle, Accent));
        diff.BorderTop.Should().BeNull();
        diff.BorderLeft.Should().BeNull();
        diff.BorderRight.Should().BeNull();
    }

    [Fact]
    public void Plan_TopAndThickBottomCombinesThinTopWithThickBottomOnEdgeCells()
    {
        var range = Range(2, 3, 4, 5);

        var topRow = CellBorderPresetPlanner.Plan(
            CellBorderPreset.TopAndThickBottom, range, new CellAddress(range.Start.Sheet, 2, 4), color: Accent);
        var bottomRow = CellBorderPresetPlanner.Plan(
            CellBorderPreset.TopAndThickBottom, range, new CellAddress(range.Start.Sheet, 4, 4), color: Accent);

        topRow.BorderTop.Should().Be(new CellBorder(BorderStyle.Thin, Accent));
        bottomRow.BorderBottom.Should().Be(new CellBorder(BorderStyle.Thick, Accent));
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
