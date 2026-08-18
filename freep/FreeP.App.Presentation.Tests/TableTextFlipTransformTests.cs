namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r144-remediation finding: flipping a TABLE (reachable via Arrange > Flip on a selected
/// table) must mirror its cell fills/borders but keep cell text upright, exactly like
/// flipping a shape (see <see cref="ShapeTextFlipTransformTests"/>). The static render path
/// (SlideCanvas.RenderTableWithTransform, both shells) still applies the full flip+rotate
/// transform to cell geometry, but pre-mirrors each cell's text box via
/// <see cref="ShapeTransformPlanner.FlipTableCellBounds"/> so the text box lands where the
/// flipped cell now sits, while a rotation-only transform (never flipH/flipV) keeps the
/// glyphs themselves unmirrored.
/// </summary>
public sealed class TableTextFlipTransformTests
{
    [Fact]
    public void FlipTableCellBounds_NoFlip_ReturnsSameBounds()
    {
        var table = new LayoutRect(0, 0, 200, 100);
        var cell = new LayoutRect(0, 0, 100, 100);

        var flipped = ShapeTransformPlanner.FlipTableCellBounds(cell, table, flipH: false, flipV: false);

        flipped.Should().Be(cell);
    }

    [Fact]
    public void FlipTableCellBounds_FlipHorizontal_SwapsLeftAndRightColumns()
    {
        // A 2-column table: left cell occupies x=[0,100), right cell occupies x=[100,200).
        var table = new LayoutRect(0, 0, 200, 100);
        var leftCell = new LayoutRect(0, 0, 100, 100);
        var rightCell = new LayoutRect(100, 0, 100, 100);

        // Flipping the table must move the left cell's text box to where the right cell now
        // visually sits (and vice versa) -- matching the geometry pass, which mirrors the
        // whole cell grid about the table's center.
        ShapeTransformPlanner.FlipTableCellBounds(leftCell, table, flipH: true, flipV: false)
            .Should().Be(rightCell);
        ShapeTransformPlanner.FlipTableCellBounds(rightCell, table, flipH: true, flipV: false)
            .Should().Be(leftCell);
    }

    [Fact]
    public void FlipTableCellBounds_FlipVertical_SwapsTopAndBottomRows()
    {
        var table = new LayoutRect(0, 0, 100, 200);
        var topCell = new LayoutRect(0, 0, 100, 100);
        var bottomCell = new LayoutRect(0, 100, 100, 100);

        ShapeTransformPlanner.FlipTableCellBounds(topCell, table, flipH: false, flipV: true)
            .Should().Be(bottomCell);
        ShapeTransformPlanner.FlipTableCellBounds(bottomCell, table, flipH: false, flipV: true)
            .Should().Be(topCell);
    }

    [Fact]
    public void FlipTableCellBounds_FlipBothAxes_MirrorsDiagonally()
    {
        // A 2x2 grid: flipping both axes swaps each cell with its diagonal opposite.
        var table = new LayoutRect(0, 0, 200, 200);
        var topLeft = new LayoutRect(0, 0, 100, 100);
        var bottomRight = new LayoutRect(100, 100, 100, 100);

        ShapeTransformPlanner.FlipTableCellBounds(topLeft, table, flipH: true, flipV: true)
            .Should().Be(bottomRight);
        ShapeTransformPlanner.FlipTableCellBounds(bottomRight, table, flipH: true, flipV: true)
            .Should().Be(topLeft);
    }

    [Fact]
    public void FlipTableCellBounds_TableNotAtOrigin_StillMirrorsAboutTableCenter()
    {
        // Sibling coverage proving the table's own offset (not just its size) is honored --
        // this must not regress to mirroring about the world origin.
        var table = new LayoutRect(50, 30, 200, 100);
        var leftCell = new LayoutRect(50, 30, 100, 100);
        var rightCell = new LayoutRect(150, 30, 100, 100);

        ShapeTransformPlanner.FlipTableCellBounds(leftCell, table, flipH: true, flipV: false)
            .Should().Be(rightCell);
    }
}
