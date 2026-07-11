using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression coverage for R25-cell-alignment-render-deep-3: Excel's Format Cells > Alignment > Indent
/// spinner is enabled for Right (as well as Left) horizontal alignment and shifts the text away from the
/// cell's right edge by the indent amount, mirroring Left's "away from the left edge" behavior.
/// </summary>
public sealed class CellTextOrientationLayoutPlannerIndentTests
{
    [Fact]
    public void CalculateLayout_RightAlignedText_AppliesIndent_ShiftsAwayFromRightEdge()
    {
        // Cell spans x:[10,110] (width 100). Text is 30 wide. With no indent the right-aligned text's
        // right edge sits at 110 - 2 = 108 (left edge 78). With a 2-level indent (16px, matching the
        // 8px/level convention used at both GridView.Rendering.cs call sites) the text must shift a
        // further 16px away from the right edge -- i.e. right edge 92, left edge 62 -- exactly like
        // Excel, and matching the Left-alignment indent behavior mirrored.
        var unindented = CellTextOrientationLayoutPlanner.CalculateLayout(
            new CellTextLayoutRect(10, 20, 100, 40),
            textWidth: 30,
            textHeight: 10,
            HorizontalAlignment.Right,
            VerticalAlignment.Bottom,
            isNumeric: false,
            indentPixels: 0,
            textRotation: 0);

        var indented = CellTextOrientationLayoutPlanner.CalculateLayout(
            new CellTextLayoutRect(10, 20, 100, 40),
            textWidth: 30,
            textHeight: 10,
            HorizontalAlignment.Right,
            VerticalAlignment.Bottom,
            isNumeric: false,
            indentPixels: 16,
            textRotation: 0);

        unindented.Bounds.Right.Should().BeApproximately(108, 0.001);
        unindented.Bounds.Left.Should().BeApproximately(78, 0.001);

        indented.Bounds.Right.Should().BeApproximately(92, 0.001, "indent must pull right-aligned text away from the right edge, like Excel");
        indented.Bounds.Left.Should().BeApproximately(62, 0.001);
        indented.TextPoint.X.Should().Be(62);

        // Vertical placement is untouched by the horizontal indent fix.
        indented.TextPoint.Y.Should().Be(unindented.TextPoint.Y);
    }

    [Fact]
    public void CalculateLayout_LeftAlignedText_StillAppliesIndent_NoRegression()
    {
        // Sibling case the fix must not disturb: Left (the fallback branch at the bottom of the same
        // switch) already applied indentPixels before this fix and must continue to do so unchanged.
        var layout = CellTextOrientationLayoutPlanner.CalculateLayout(
            new CellTextLayoutRect(10, 20, 100, 40),
            textWidth: 30,
            textHeight: 10,
            HorizontalAlignment.Left,
            VerticalAlignment.Bottom,
            isNumeric: false,
            indentPixels: 16,
            textRotation: 0);

        layout.TextPoint.X.Should().Be(10 + 2 + 16);
        layout.Bounds.Left.Should().BeApproximately(28, 0.001);
    }

    [Fact]
    public void CalculateLayout_RightAlignedTextWiderThanCell_WithIndent_StillOverflowsLeftNotRight()
    {
        // Regression guard combined with the fix: a too-wide right-aligned string with an indent must
        // still overflow leftward (never clamp to spill rightward into the next column), just shifted an
        // extra `indentPixels` left of where it would land with no indent.
        var layout = CellTextOrientationLayoutPlanner.CalculateLayout(
            new CellTextLayoutRect(100, 0, 40, 20),
            textWidth: 100,
            textHeight: 10,
            HorizontalAlignment.Right,
            VerticalAlignment.Center,
            isNumeric: false,
            indentPixels: 8,
            textRotation: 0);

        layout.Bounds.Right.Should().BeApproximately(130, 0.001);
        layout.Bounds.Left.Should().BeApproximately(30, 0.001);
        layout.Bounds.Right.Should().BeLessThan(140, "the text must not spill past the cell's right edge into the next column");
    }
}
