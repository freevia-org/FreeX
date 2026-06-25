using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Layout tests for HorizontalAlignment.Fill — verifies the text origin is at Left+2 so the
/// GridView renderer can tile copies rightward from that position.
/// </summary>
public sealed class FillAlignmentLayoutTests
{
    [Fact]
    public void Fill_TextOrigin_IsLeftPlusPad()
    {
        // Arrange: a 100×20 cell, text is 30 px wide — narrower than cell
        var cellRect = new CellTextLayoutRect(10, 5, 100, 20);

        // Act
        var layout = CellTextOrientationLayoutPlanner.CalculateLayout(
            cellRect,
            textWidth: 30,
            textHeight: 12,
            horizontalAlignment: HorizontalAlignment.Fill,
            verticalAlignment: null,
            isNumeric: false,
            indentPixels: 0,
            textRotation: 0);

        // Assert: for Fill the X origin is cell.Left + 2 (same as Left), not centered
        layout.TextPoint.X.Should().Be(12, "Fill alignment starts at Left + 2px pad");
    }

    [Fact]
    public void Fill_DoesNotCenter_EvenWhenTextIsNarrow()
    {
        var cellRect = new CellTextLayoutRect(0, 0, 200, 20);

        var fillLayout = CellTextOrientationLayoutPlanner.CalculateLayout(
            cellRect, textWidth: 10, textHeight: 12,
            HorizontalAlignment.Fill, null, false, 0, 0);

        var centerLayout = CellTextOrientationLayoutPlanner.CalculateLayout(
            cellRect, textWidth: 10, textHeight: 12,
            HorizontalAlignment.Center, null, false, 0, 0);

        fillLayout.TextPoint.X.Should().NotBe(centerLayout.TextPoint.X,
            "Fill must start from left, not center");
    }
}
