using FluentAssertions;
using FreeX.App.Host;
using System.Windows;
using Xunit;

namespace FreeX.App.Host.Tests;

public sealed class FormulaInlineEditorLayoutPlannerTests
{
    [Fact]
    public void Create_MatchesEditorChromeToCellAndAllowsTextSurfaceToSpillRight()
    {
        var layout = FormulaInlineEditorLayoutPlanner.Create(
            cellLeft: 100,
            cellTop: 40,
            cellWidth: 64,
            cellHeight: 20);

        layout.EditorRect.Left.Should().Be(100);
        layout.EditorRect.Top.Should().Be(40);
        layout.EditorRect.Width.Should().Be(64);
        layout.EditorRect.Height.Should().Be(20);

        layout.TextOverlayRect.Left.Should().Be(104);
        layout.TextOverlayRect.Top.Should().Be(40);
        layout.TextOverlayRect.Width.Should().BeGreaterThan(layout.EditorRect.Width);
        layout.TextOverlayRect.Bottom.Should().BeLessThanOrEqualTo(layout.EditorRect.Bottom);
    }

    [Fact]
    public void Create_ExpandsTextSurfaceForLongFormulaWithoutExpandingCellChrome()
    {
        var layout = FormulaInlineEditorLayoutPlanner.Create(
            cellLeft: 100,
            cellTop: 40,
            cellWidth: 64,
            cellHeight: 20,
            desiredTextWidth: 340,
            availableRight: 600);

        layout.EditorRect.Should().Be(new FormulaEditorRect(100, 40, 64, 20));
        layout.TextOverlayRect.Left.Should().Be(104);
        layout.TextOverlayRect.Width.Should().BeGreaterThan(340);
        layout.TextOverlayRect.Right.Should().BeLessThanOrEqualTo(600);
    }

    [Fact]
    public void Create_BoundsExpandedTextSurfaceToVisibleRightEdge()
    {
        var layout = FormulaInlineEditorLayoutPlanner.Create(
            cellLeft: 100,
            cellTop: 40,
            cellWidth: 64,
            cellHeight: 20,
            desiredTextWidth: 340,
            availableRight: 300);

        layout.EditorRect.Should().Be(new FormulaEditorRect(100, 40, 64, 20));
        layout.TextOverlayRect.Right.Should().Be(300);
    }

    [Fact]
    public void Create_WithMultipleLines_GrowsEditorHeightByLineCount()
    {
        // R78-render-inplace-editor-5-3: Alt+Enter-inserted line breaks (or a pre-existing
        // multi-line cell value) must grow the editor box downward one cell-row-height per line,
        // instead of staying clipped to a single row.
        var layout = FormulaInlineEditorLayoutPlanner.Create(
            cellLeft: 100,
            cellTop: 40,
            cellWidth: 64,
            cellHeight: 20,
            lineCount: 3);

        layout.EditorRect.Should().Be(new FormulaEditorRect(100, 40, 64, 60));
        layout.TextOverlayRect.Height.Should().Be(60);
    }

    [Fact]
    public void Create_WithDefaultLineCount_KeepsSingleRowHeight()
    {
        // Sibling no-regression: omitting lineCount (single-line entry, the overwhelmingly common
        // case) must keep the pre-existing single-row height untouched.
        var layout = FormulaInlineEditorLayoutPlanner.Create(
            cellLeft: 100,
            cellTop: 40,
            cellWidth: 64,
            cellHeight: 20);

        layout.EditorRect.Height.Should().Be(20);
    }

    [Fact]
    public void GetChromeBorderThickness_RemovesOverflowSideBorders()
    {
        FormulaInlineEditorLayoutPlanner.GetChromeBorderThickness(FormulaInlineEditorOverflow.None)
            .Should().Be(new FormulaEditorThickness(1));

        FormulaInlineEditorLayoutPlanner.GetChromeBorderThickness(new FormulaInlineEditorOverflow(Left: false, Right: true))
            .Should().Be(new FormulaEditorThickness(1, 1, 0, 1));

        FormulaInlineEditorLayoutPlanner.GetChromeBorderThickness(new FormulaInlineEditorOverflow(Left: true, Right: false))
            .Should().Be(new FormulaEditorThickness(0, 1, 1, 1));
    }

    [Fact]
    public void GetChromeRect_ExtendsOnlyUnderHiddenOverflowEdges()
    {
        var editorRect = new FormulaEditorRect(100, 40, 64, 20);

        FormulaInlineEditorLayoutPlanner.GetChromeRect(editorRect, FormulaInlineEditorOverflow.None)
            .Should().Be(editorRect);

        FormulaInlineEditorLayoutPlanner.GetChromeRect(editorRect, new FormulaInlineEditorOverflow(Left: false, Right: true))
            .Should().Be(new FormulaEditorRect(100, 40, 66, 20));

        FormulaInlineEditorLayoutPlanner.GetChromeRect(editorRect, new FormulaInlineEditorOverflow(Left: true, Right: false))
            .Should().Be(new FormulaEditorRect(98, 40, 66, 20));
    }
}
