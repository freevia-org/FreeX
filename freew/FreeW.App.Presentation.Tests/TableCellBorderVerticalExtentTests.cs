using FluentAssertions;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Presentation.Tests;

/// <summary>
/// Word lays an "at least" row's own cell borders outside the authored row height; the renderer adds
/// this extent to the row minimum so bordered header/total rows stand as tall as Word's.
/// </summary>
public sealed class TableCellBorderVerticalExtentTests
{
    private const double PxPerPoint = 96.0 / 72.0;

    [Fact]
    public void DoubleTopAndThickBottom_CountTheirStrokesAndGap()
    {
        var plan = TableCellBorderVisualPlanner.Build(new CellBorders
        {
            Top = new CellBorderEdge(BorderLineStyle.Double, "#1F4E79", 1.25),
            Bottom = new CellBorderEdge(BorderLineStyle.Thick, "#1F4E79", 1.25),
            Left = new CellBorderEdge(BorderLineStyle.Single, "#1F4E79", 0.75),
            Right = new CellBorderEdge(BorderLineStyle.Single, "#1F4E79", 0.75),
        }, PxPerPoint);

        // 3 x 1.25pt for the double rule plus 2 x 1.25pt for the thick one; side edges do not count.
        plan.VerticalBorderExtentDip.Should().BeApproximately((3 * 1.25 + 2 * 1.25) * PxPerPoint, 1e-9);
    }

    [Fact]
    public void SingleEdgesCountTheirWidth_AndAbsentEdgesNothing()
    {
        TableCellBorderVisualPlanner.Build(new CellBorders
        {
            Top = new CellBorderEdge(BorderLineStyle.Single, "#000000", 1.5),
        }, PxPerPoint).VerticalBorderExtentDip.Should().BeApproximately(1.5 * PxPerPoint, 1e-9);

        TableCellBorderVisualPlanner.Build(null, PxPerPoint).VerticalBorderExtentDip.Should().Be(0);
    }
}
