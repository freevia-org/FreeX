using FluentAssertions;
using Freexcel.App.UI;
using Freexcel.Core.Model;

namespace Freexcel.App.UI.Tests;

public sealed class GridViewAutofillTests
{
    [Fact]
    public void ConstrainAutofillTarget_PrefersVerticalAxisWhenDragExtendsFartherDown()
    {
        var sheet = SheetId.New();
        var source = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 3, 3));

        var target = GridView.ConstrainAutofillTarget(
            source,
            new CellAddress(sheet, 8, 6));

        target.Should().Be(new CellAddress(sheet, 8, 3));
    }

    [Fact]
    public void ConstrainAutofillTarget_PrefersHorizontalAxisWhenDragExtendsFartherRight()
    {
        var sheet = SheetId.New();
        var source = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 3, 3));

        var target = GridView.ConstrainAutofillTarget(
            source,
            new CellAddress(sheet, 5, 9));

        target.Should().Be(new CellAddress(sheet, 3, 9));
    }

    [Fact]
    public void CalculateAutofillEdgeScrollIntent_RequestsHorizontalScrollNearRightEdge()
    {
        GridView.CalculateAutofillEdgeScrollIntent(
                pointerX: 795,
                pointerY: 120,
                width: 800,
                height: 600,
                rowHeaderWidth: 48,
                columnHeaderHeight: 24)
            .Should()
            .Be(new GridAutoScrollRequest(1, 0));
    }

    [Fact]
    public void CalculateAutofillEdgeScrollIntent_IgnoresPointerAwayFromEdges()
    {
        GridView.CalculateAutofillEdgeScrollIntent(
                pointerX: 400,
                pointerY: 300,
                width: 800,
                height: 600,
                rowHeaderWidth: 48,
                columnHeaderHeight: 24)
            .Should()
            .Be(new GridAutoScrollRequest(0, 0));
    }
}
