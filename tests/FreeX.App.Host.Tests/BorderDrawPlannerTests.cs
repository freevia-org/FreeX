using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class BorderDrawPlannerTests
{
    [Fact]
    public void CreateDiff_DrawGridUsesCurrentLineStyleAndColor()
    {
        var accent = new CellColor(33, 115, 70);

        var diff = BorderDrawPlanner.CreateDiff(BorderDrawMode.DrawGrid, BorderStyle.Double, accent);

        var expected = new CellBorder(BorderStyle.Double, accent);
        diff.BorderTop.Should().Be(expected);
        diff.BorderRight.Should().Be(expected);
        diff.BorderBottom.Should().Be(expected);
        diff.BorderLeft.Should().Be(expected);
    }

    [Fact]
    public void CreateDiff_EraseClearsAllBorders()
    {
        var diff = BorderDrawPlanner.CreateDiff(BorderDrawMode.Erase, BorderStyle.Thick, new CellColor(1, 2, 3));

        diff.BorderTop.Should().Be(new CellBorder(BorderStyle.None));
        diff.BorderRight.Should().Be(new CellBorder(BorderStyle.None));
        diff.BorderBottom.Should().Be(new CellBorder(BorderStyle.None));
        diff.BorderLeft.Should().Be(new CellBorder(BorderStyle.None));
    }
}
