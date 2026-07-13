using FluentAssertions;
using FreeX.App.UI;
using System.Windows;

namespace FreeX.App.UI.Tests;

// Excel never tints the active cell within a selection: the fill covers the whole selected
// range except the active cell, which stays unfilled (only the heavy selection border outlines
// it). Regression coverage for R42-render-selection-activecell-3-1.
public sealed class GridViewSelectionActiveCellFillTests
{
    [Fact]
    public void BuildSelectionFillGeometry_ExcludesActiveCellFromMultiCellSelectionFill()
    {
        var rect = new Rect(0, 0, 100, 60);
        var activeCellHole = new Rect(40, 20, 20, 20); // interior cell within the range

        var geometry = GridView.BuildSelectionFillGeometry(rect, activeCellHole);

        geometry.Should().NotBeNull();
        // Inside the active cell: must stay unfilled ("white hole").
        geometry!.FillContains(new Point(50, 30)).Should().BeFalse();
        // Elsewhere in the selected range (outside the active cell): must be tinted.
        geometry.FillContains(new Point(5, 5)).Should().BeTrue();
        geometry.FillContains(new Point(90, 55)).Should().BeTrue();
    }

    [Fact]
    public void BuildSelectionFillGeometry_SingleCellSelectionHasNoTintAtAll()
    {
        // A single-cell selection's "range" and "active cell" are the same rect, so the whole
        // fill area is excluded - matching Excel's plain-border look for a lone selected cell.
        var rect = new Rect(10, 10, 20, 20);

        var geometry = GridView.BuildSelectionFillGeometry(rect, rect);

        geometry.Should().BeNull();
    }

    [Fact]
    public void BuildSelectionFillGeometry_NoRegression_FullyTintsRangeWhenActiveCellIsElsewhere()
    {
        // Sibling no-regression case: in a multi-range selection, ranges that do not contain the
        // active cell must keep their full (unpunched) tint fill.
        var rect = new Rect(0, 0, 100, 60);

        var geometry = GridView.BuildSelectionFillGeometry(rect, hole: null);

        geometry.Should().NotBeNull();
        geometry!.FillContains(new Point(50, 30)).Should().BeTrue();
        geometry.FillContains(new Point(1, 1)).Should().BeTrue();
        geometry.FillContains(new Point(99, 59)).Should().BeTrue();
    }

    [Fact]
    public void BuildSelectionFillGeometry_NoRegression_FullyTintsRangeWhenHoleIsOutsideRect()
    {
        var rect = new Rect(0, 0, 100, 60);
        var hole = new Rect(200, 200, 20, 20); // does not intersect rect at all

        var geometry = GridView.BuildSelectionFillGeometry(rect, hole);

        geometry.Should().NotBeNull();
        geometry!.FillContains(new Point(50, 30)).Should().BeTrue();
    }

    [Fact]
    public void BuildSelectionFillGeometry_ReturnsNullForDegenerateRect()
    {
        var rect = new Rect(0, 0, 0, 60);

        GridView.BuildSelectionFillGeometry(rect, hole: null).Should().BeNull();
    }
}
