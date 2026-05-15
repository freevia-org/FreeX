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
}
