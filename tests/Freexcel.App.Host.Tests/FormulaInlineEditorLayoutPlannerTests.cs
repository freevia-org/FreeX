using FluentAssertions;
using Freexcel.App.Host;
using System.Windows;
using Xunit;

namespace Freexcel.App.Host.Tests;

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
    public void GetChromeBorderThickness_RemovesOverflowSideBorders()
    {
        FormulaInlineEditorLayoutPlanner.GetChromeBorderThickness(FormulaInlineEditorOverflow.None)
            .Should().Be(new Thickness(1));

        FormulaInlineEditorLayoutPlanner.GetChromeBorderThickness(new FormulaInlineEditorOverflow(Left: false, Right: true))
            .Should().Be(new Thickness(1, 1, 0, 1));

        FormulaInlineEditorLayoutPlanner.GetChromeBorderThickness(new FormulaInlineEditorOverflow(Left: true, Right: false))
            .Should().Be(new Thickness(0, 1, 1, 1));
    }

    [Fact]
    public void GetChromeRect_ExtendsOnlyUnderHiddenOverflowEdges()
    {
        var editorRect = new Rect(100, 40, 64, 20);

        FormulaInlineEditorLayoutPlanner.GetChromeRect(editorRect, FormulaInlineEditorOverflow.None)
            .Should().Be(editorRect);

        FormulaInlineEditorLayoutPlanner.GetChromeRect(editorRect, new FormulaInlineEditorOverflow(Left: false, Right: true))
            .Should().Be(new Rect(100, 40, 66, 20));

        FormulaInlineEditorLayoutPlanner.GetChromeRect(editorRect, new FormulaInlineEditorOverflow(Left: true, Right: false))
            .Should().Be(new Rect(98, 40, 66, 20));
    }
}
