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
    public void GetChromeBorderThickness_RemovesRightBorderOnlyWhenTextSpillsRight()
    {
        FormulaInlineEditorLayoutPlanner.GetChromeBorderThickness(textSpillsRight: false)
            .Should().Be(new Thickness(1));

        FormulaInlineEditorLayoutPlanner.GetChromeBorderThickness(textSpillsRight: true)
            .Should().Be(new Thickness(1, 1, 0, 1));
    }
}
