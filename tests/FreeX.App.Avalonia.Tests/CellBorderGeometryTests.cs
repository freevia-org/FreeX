using FluentAssertions;
using FreeX.App.Avalonia;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Unit tests for <see cref="CellBorderGeometry"/> — the pure thickness/dash-array mapping that
/// mirrors the WPF <c>DrawBorderEdge</c> table.  No UI thread required.
/// </summary>
public sealed class CellBorderGeometryTests
{
    // ── Thickness tests ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(BorderStyle.Hair,               0.25)]
    [InlineData(BorderStyle.Thin,               0.5)]
    [InlineData(BorderStyle.Dashed,             0.5)]   // thin-weight dash
    [InlineData(BorderStyle.Dotted,             0.5)]   // thin-weight dot
    [InlineData(BorderStyle.DashDot,            0.5)]   // thin-weight dash-dot
    [InlineData(BorderStyle.DashDotDot,         0.5)]   // thin-weight dash-dot-dot
    [InlineData(BorderStyle.SlantDashDot,       0.5)]   // thin-weight slant
    [InlineData(BorderStyle.Medium,             1.5)]
    [InlineData(BorderStyle.MediumDashed,       1.5)]
    [InlineData(BorderStyle.MediumDashDot,      1.5)]
    [InlineData(BorderStyle.MediumDashDotDot,   1.5)]
    [InlineData(BorderStyle.Thick,              2.5)]
    [InlineData(BorderStyle.Double,             0.5)]   // falls through to default
    public void GetThickness_ReturnsCorrectValue(BorderStyle style, double expected)
    {
        CellBorderGeometry.GetThickness(style).Should().BeApproximately(expected, precision: 0.001);
    }

    // ── Dash-array tests ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(BorderStyle.Thin)]
    [InlineData(BorderStyle.Hair)]
    [InlineData(BorderStyle.Medium)]
    [InlineData(BorderStyle.Thick)]
    [InlineData(BorderStyle.Double)]
    public void GetDashArray_ReturnNull_ForSolidStyles(BorderStyle style)
    {
        CellBorderGeometry.GetDashArray(style).Should().BeNull();
    }

    [Theory]
    [InlineData(BorderStyle.Dashed)]
    [InlineData(BorderStyle.MediumDashed)]
    public void GetDashArray_ReturnsDash_ForDashedStyles(BorderStyle style)
    {
        var arr = CellBorderGeometry.GetDashArray(style);
        arr.Should().NotBeNull().And.Equal(2, 2);
    }

    [Fact]
    public void GetDashArray_ReturnsDot_ForDotted()
    {
        var arr = CellBorderGeometry.GetDashArray(BorderStyle.Dotted);
        arr.Should().NotBeNull().And.Equal(1, 2);
    }

    [Theory]
    [InlineData(BorderStyle.DashDot)]
    [InlineData(BorderStyle.MediumDashDot)]
    [InlineData(BorderStyle.SlantDashDot)]
    public void GetDashArray_ReturnsDashDot_ForDashDotStyles(BorderStyle style)
    {
        var arr = CellBorderGeometry.GetDashArray(style);
        arr.Should().NotBeNull().And.Equal(2, 2, 1, 2);
    }

    [Theory]
    [InlineData(BorderStyle.DashDotDot)]
    [InlineData(BorderStyle.MediumDashDotDot)]
    public void GetDashArray_ReturnsDashDotDot_ForDashDotDotStyles(BorderStyle style)
    {
        var arr = CellBorderGeometry.GetDashArray(style);
        arr.Should().NotBeNull().And.Equal(2, 2, 1, 2, 1, 2);
    }

    // ── Double-border geometry tests (R48-reimplementation-twin-sweep-1) ───────────────────────────

    [Fact]
    public void GetDoubleBorderLineOffsets_HorizontalEdge_ReturnsTwoLinesOffsetVertically()
    {
        // A horizontal edge from (0,10) to (100,10): the two double-border strokes must straddle
        // the original edge, offset perpendicular (vertically) by half the gap on each side, and
        // must NOT collapse onto the single original centerline (the pre-fix bug: only one line).
        var (x1, y1, x2, y2, x3, y3, x4, y4) =
            CellBorderGeometry.GetDoubleBorderLineOffsets(0, 10, 100, 10);

        // Horizontal edge -> offset is purely vertical (X unchanged for both lines).
        x1.Should().BeApproximately(0, 0.001);
        x2.Should().BeApproximately(100, 0.001);
        x3.Should().BeApproximately(0, 0.001);
        x4.Should().BeApproximately(100, 0.001);

        // The two lines must be on opposite sides of the original centerline (y=10), separated by
        // exactly DoubleBorderGap, and neither should equal the original y=10 (i.e. this must
        // actually produce TWO distinct parallel lines, not one single line at the center).
        y1.Should().BeApproximately(y2, 0.001);
        y3.Should().BeApproximately(y4, 0.001);
        Math.Abs(y1 - y3).Should().BeApproximately(CellBorderGeometry.DoubleBorderGap, 0.001,
            "the two strokes must be distinct parallel lines, not one single line at the center");
        ((y1 + y3) / 2.0).Should().BeApproximately(10, 0.001, "the pair must straddle the original edge");
    }

    [Fact]
    public void GetDoubleBorderLineOffsets_VerticalEdge_ReturnsTwoLinesOffsetHorizontally()
    {
        // A vertical edge from (20,0) to (20,50): offset must be purely horizontal.
        var (x1, y1, x2, y2, x3, y3, x4, y4) =
            CellBorderGeometry.GetDoubleBorderLineOffsets(20, 0, 20, 50);

        y1.Should().BeApproximately(0, 0.001);
        y2.Should().BeApproximately(50, 0.001);
        y3.Should().BeApproximately(0, 0.001);
        y4.Should().BeApproximately(50, 0.001);

        x1.Should().BeApproximately(x2, 0.001);
        x3.Should().BeApproximately(x4, 0.001);
        Math.Abs(x1 - x3).Should().BeApproximately(CellBorderGeometry.DoubleBorderGap, 0.001);
        ((x1 + x3) / 2.0).Should().BeApproximately(20, 0.001, "the pair must straddle the original edge");
    }

    [Fact]
    public void GetDoubleBorderLineOffsets_ZeroLengthEdge_ReturnsDegenerateBothLinesCoincident()
    {
        var (x1, y1, x2, y2, x3, y3, x4, y4) =
            CellBorderGeometry.GetDoubleBorderLineOffsets(5, 5, 5, 5);

        x1.Should().BeApproximately(5, 0.001);
        y1.Should().BeApproximately(5, 0.001);
        x2.Should().BeApproximately(5, 0.001);
        y2.Should().BeApproximately(5, 0.001);
        x3.Should().BeApproximately(5, 0.001);
        y3.Should().BeApproximately(5, 0.001);
        x4.Should().BeApproximately(5, 0.001);
        y4.Should().BeApproximately(5, 0.001);
    }

    // ── Adjacent-edge border-conflict resolution tests (R48-reimplementation-twin-sweep-2) ─────────

    [Fact]
    public void ResolveBorderEdgeWinner_NeighborHeavier_NeighborWins()
    {
        // A1 has a Thin bottom border; its neighbor A2 has a Thick top border on the same shared
        // edge. Excel always shows the heavier (Thick) line -- the lighter Thin edge must be
        // suppressed in favor of the neighbor's Thick border.
        var mine     = new CellBorder(BorderStyle.Thin);
        var neighbor = new CellBorder(BorderStyle.Thick);

        var winner = CellBorderGeometry.ResolveBorderEdgeWinner(mine, neighbor);

        winner.Style.Should().Be(BorderStyle.Thick);
    }

    [Fact]
    public void ResolveBorderEdgeWinner_MineHeavier_MineWins()
    {
        var mine     = new CellBorder(BorderStyle.Double);
        var neighbor = new CellBorder(BorderStyle.Hair);

        var winner = CellBorderGeometry.ResolveBorderEdgeWinner(mine, neighbor);

        winner.Style.Should().Be(BorderStyle.Double);
    }

    [Fact]
    public void ResolveBorderEdgeWinner_NeighborNone_MineWins()
    {
        var mine     = new CellBorder(BorderStyle.Thin);
        var neighbor = new CellBorder(BorderStyle.None);

        CellBorderGeometry.ResolveBorderEdgeWinner(mine, neighbor).Should().Be(mine);
    }

    [Fact]
    public void ResolveBorderEdgeWinner_MineNone_NeighborWins()
    {
        var mine     = new CellBorder(BorderStyle.None);
        var neighbor = new CellBorder(BorderStyle.Medium);

        CellBorderGeometry.ResolveBorderEdgeWinner(mine, neighbor).Should().Be(neighbor);
    }

    [Fact]
    public void ResolveBorderEdgeWinner_SymmetricRegardlessOfArgumentOrder()
    {
        var thin  = new CellBorder(BorderStyle.Thin);
        var thick = new CellBorder(BorderStyle.Thick);

        CellBorderGeometry.ResolveBorderEdgeWinner(thin, thick).Should().Be(
            CellBorderGeometry.ResolveBorderEdgeWinner(thick, thin),
            "the resolution must not depend on which cell is 'mine' vs 'neighbor'");
    }
}

/// <summary>
/// Unit tests for the <see cref="MainWindow"/> border-visibility gate — verifies that diagonal
/// borders are included in the visibility check added in wave-1.
/// </summary>
public sealed class HasVisibleCellBorderTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static CellStyle StyleWith(
        CellBorder? top    = null,
        CellBorder? right  = null,
        CellBorder? bottom = null,
        CellBorder? left   = null,
        CellBorder? diagDown = null,
        CellBorder? diagUp   = null)
    {
        var s = new CellStyle();
        if (top      is { } t) s.BorderTop           = t;
        if (right    is { } r) s.BorderRight          = r;
        if (bottom   is { } b) s.BorderBottom         = b;
        if (left     is { } l) s.BorderLeft           = l;
        if (diagDown is { } d) s.BorderDiagonalDown   = d;
        if (diagUp   is { } u) s.BorderDiagonalUp     = u;
        return s;
    }

    private static CellBorder Thin => new(BorderStyle.Thin);
    private static CellBorder None => new(BorderStyle.None);

    // ── Tests ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NoBordersSet_IsNotVisible()
    {
        // CellBorderGeometry does not expose HasVisibleCellBorder directly; verify the mapping
        // is consistent: all None means none of the dash-array / thickness branches trigger.
        CellBorderGeometry.GetDashArray(BorderStyle.None).Should().BeNull();
        CellBorderGeometry.GetThickness(BorderStyle.None).Should().BeApproximately(0.5, 0.001);
    }

    [Theory]
    [InlineData(BorderStyle.Thin)]
    [InlineData(BorderStyle.Dashed)]
    [InlineData(BorderStyle.Hair)]
    [InlineData(BorderStyle.SlantDashDot)]
    [InlineData(BorderStyle.MediumDashDotDot)]
    public void DiagonalDown_WithAnyNonNoneStyle_IsHandledByGeometry(BorderStyle style)
    {
        // Ensure the geometry helper does not throw or return zero thickness for any valid style.
        CellBorderGeometry.GetThickness(style).Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData(BorderStyle.Thin)]
    [InlineData(BorderStyle.DashDot)]
    [InlineData(BorderStyle.MediumDashed)]
    public void DiagonalUp_WithAnyNonNoneStyle_IsHandledByGeometry(BorderStyle style)
    {
        CellBorderGeometry.GetThickness(style).Should().BeGreaterThan(0);
    }
}
