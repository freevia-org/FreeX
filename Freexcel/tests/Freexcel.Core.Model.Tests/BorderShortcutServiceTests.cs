using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Model.Tests;

public sealed class BorderShortcutServiceTests
{
    [Fact]
    public void GetOutlineBorderDiff_AppliesOnlyPerimeterEdgesForRange()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 2, 3),
            new CellAddress(sheetId, 4, 5));

        var topLeft = BorderShortcutService.GetOutlineBorderDiff(range, new CellAddress(sheetId, 2, 3));
        var center = BorderShortcutService.GetOutlineBorderDiff(range, new CellAddress(sheetId, 3, 4));
        var bottomRight = BorderShortcutService.GetOutlineBorderDiff(range, new CellAddress(sheetId, 4, 5));

        topLeft.BorderTop.Should().Be(new CellBorder(BorderStyle.Thin, CellColor.Black));
        topLeft.BorderLeft.Should().Be(new CellBorder(BorderStyle.Thin, CellColor.Black));
        topLeft.BorderBottom.Should().BeNull();
        topLeft.BorderRight.Should().BeNull();

        center.BorderTop.Should().BeNull();
        center.BorderRight.Should().BeNull();
        center.BorderBottom.Should().BeNull();
        center.BorderLeft.Should().BeNull();

        bottomRight.BorderRight.Should().Be(new CellBorder(BorderStyle.Thin, CellColor.Black));
        bottomRight.BorderBottom.Should().Be(new CellBorder(BorderStyle.Thin, CellColor.Black));
        bottomRight.BorderTop.Should().BeNull();
        bottomRight.BorderLeft.Should().BeNull();
    }

    [Fact]
    public void GetClearBorderDiff_RemovesAllCellBorders()
    {
        var diff = BorderShortcutService.GetClearBorderDiff();

        diff.BorderTop.Should().Be(new CellBorder(BorderStyle.None));
        diff.BorderRight.Should().Be(new CellBorder(BorderStyle.None));
        diff.BorderBottom.Should().Be(new CellBorder(BorderStyle.None));
        diff.BorderLeft.Should().Be(new CellBorder(BorderStyle.None));
    }

    [Fact]
    public void GetSingleBorderDiff_CreatesRequestedEdgeAndLineStyle()
    {
        var diff = BorderShortcutService.GetSingleBorderDiff(BorderEdge.Bottom, BorderStyle.Double);

        diff.BorderTop.Should().BeNull();
        diff.BorderRight.Should().BeNull();
        diff.BorderBottom.Should().Be(new CellBorder(BorderStyle.Double, CellColor.Black));
        diff.BorderLeft.Should().BeNull();
    }

    [Fact]
    public void GetSingleBorderDiff_UsesRequestedLineColor()
    {
        var color = new CellColor(12, 34, 56);

        var diff = BorderShortcutService.GetSingleBorderDiff(BorderEdge.Right, BorderStyle.Dashed, color);

        diff.BorderRight.Should().Be(new CellBorder(BorderStyle.Dashed, color));
        diff.BorderTop.Should().BeNull();
        diff.BorderBottom.Should().BeNull();
        diff.BorderLeft.Should().BeNull();
    }

    [Fact]
    public void GetOutlineBorderDiff_UsesRequestedLineStyleOnlyOnRangePerimeter()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 2, 3),
            new CellAddress(sheetId, 4, 5));

        var topLeft = BorderShortcutService.GetOutlineBorderDiff(range, new CellAddress(sheetId, 2, 3), BorderStyle.Thick);
        var center = BorderShortcutService.GetOutlineBorderDiff(range, new CellAddress(sheetId, 3, 4), BorderStyle.Thick);
        var bottomRight = BorderShortcutService.GetOutlineBorderDiff(range, new CellAddress(sheetId, 4, 5), BorderStyle.Thick);

        topLeft.BorderTop.Should().Be(new CellBorder(BorderStyle.Thick, CellColor.Black));
        topLeft.BorderLeft.Should().Be(new CellBorder(BorderStyle.Thick, CellColor.Black));
        topLeft.BorderBottom.Should().BeNull();
        topLeft.BorderRight.Should().BeNull();

        center.BorderTop.Should().BeNull();
        center.BorderRight.Should().BeNull();
        center.BorderBottom.Should().BeNull();
        center.BorderLeft.Should().BeNull();

        bottomRight.BorderRight.Should().Be(new CellBorder(BorderStyle.Thick, CellColor.Black));
        bottomRight.BorderBottom.Should().Be(new CellBorder(BorderStyle.Thick, CellColor.Black));
        bottomRight.BorderTop.Should().BeNull();
        bottomRight.BorderLeft.Should().BeNull();
    }

    [Fact]
    public void GetOutlineBorderDiff_UsesRequestedLineColorOnRangePerimeter()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 2, 3),
            new CellAddress(sheetId, 4, 5));
        var color = new CellColor(80, 20, 140);

        var topLeft = BorderShortcutService.GetOutlineBorderDiff(range, new CellAddress(sheetId, 2, 3), BorderStyle.Dotted, color);
        var center = BorderShortcutService.GetOutlineBorderDiff(range, new CellAddress(sheetId, 3, 4), BorderStyle.Dotted, color);
        var bottomRight = BorderShortcutService.GetOutlineBorderDiff(range, new CellAddress(sheetId, 4, 5), BorderStyle.Dotted, color);

        topLeft.BorderTop.Should().Be(new CellBorder(BorderStyle.Dotted, color));
        topLeft.BorderLeft.Should().Be(new CellBorder(BorderStyle.Dotted, color));
        center.BorderTop.Should().BeNull();
        center.BorderRight.Should().BeNull();
        center.BorderBottom.Should().BeNull();
        center.BorderLeft.Should().BeNull();
        bottomRight.BorderRight.Should().Be(new CellBorder(BorderStyle.Dotted, color));
        bottomRight.BorderBottom.Should().Be(new CellBorder(BorderStyle.Dotted, color));
    }

    [Fact]
    public void GetTopAndBottomBorderDiff_AppliesOnlyTopAndBottomPerimeterEdges()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 2, 3),
            new CellAddress(sheetId, 4, 5));

        var topLeft = BorderShortcutService.GetTopAndBottomBorderDiff(range, new CellAddress(sheetId, 2, 3), BorderStyle.Double);
        var center = BorderShortcutService.GetTopAndBottomBorderDiff(range, new CellAddress(sheetId, 3, 4), BorderStyle.Double);
        var bottomRight = BorderShortcutService.GetTopAndBottomBorderDiff(range, new CellAddress(sheetId, 4, 5), BorderStyle.Double);

        topLeft.BorderTop.Should().Be(new CellBorder(BorderStyle.Thin, CellColor.Black));
        topLeft.BorderBottom.Should().BeNull();
        topLeft.BorderLeft.Should().BeNull();
        topLeft.BorderRight.Should().BeNull();

        center.BorderTop.Should().BeNull();
        center.BorderRight.Should().BeNull();
        center.BorderBottom.Should().BeNull();
        center.BorderLeft.Should().BeNull();

        bottomRight.BorderBottom.Should().Be(new CellBorder(BorderStyle.Double, CellColor.Black));
        bottomRight.BorderTop.Should().BeNull();
        bottomRight.BorderLeft.Should().BeNull();
        bottomRight.BorderRight.Should().BeNull();
    }

    [Fact]
    public void GetInsideBorderDiff_AppliesOnlyInteriorEdges()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 2, 3),
            new CellAddress(sheetId, 4, 5));
        var color = new CellColor(33, 115, 70);

        var topLeft = BorderShortcutService.GetInsideBorderDiff(range, new CellAddress(sheetId, 2, 3), BorderStyle.Dashed, color);
        var center = BorderShortcutService.GetInsideBorderDiff(range, new CellAddress(sheetId, 3, 4), BorderStyle.Dashed, color);
        var bottomRight = BorderShortcutService.GetInsideBorderDiff(range, new CellAddress(sheetId, 4, 5), BorderStyle.Dashed, color);

        topLeft.BorderTop.Should().BeNull();
        topLeft.BorderLeft.Should().BeNull();
        topLeft.BorderRight.Should().Be(new CellBorder(BorderStyle.Dashed, color));
        topLeft.BorderBottom.Should().Be(new CellBorder(BorderStyle.Dashed, color));

        center.BorderTop.Should().Be(new CellBorder(BorderStyle.Dashed, color));
        center.BorderRight.Should().Be(new CellBorder(BorderStyle.Dashed, color));
        center.BorderBottom.Should().Be(new CellBorder(BorderStyle.Dashed, color));
        center.BorderLeft.Should().Be(new CellBorder(BorderStyle.Dashed, color));

        bottomRight.BorderTop.Should().Be(new CellBorder(BorderStyle.Dashed, color));
        bottomRight.BorderLeft.Should().Be(new CellBorder(BorderStyle.Dashed, color));
        bottomRight.BorderRight.Should().BeNull();
        bottomRight.BorderBottom.Should().BeNull();
    }
}
