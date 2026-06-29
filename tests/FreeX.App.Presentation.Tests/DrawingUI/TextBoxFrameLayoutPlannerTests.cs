using FluentAssertions;
using FreeX.App.Presentation.DrawingUI;

namespace FreeX.App.Presentation.Tests.DrawingUI;

public sealed class TextBoxFrameLayoutPlannerTests
{
    [Fact]
    public void CreateNormalized_AppliesMinimumFrameAndInsetTextBounds()
    {
        var layout = TextBoxFrameLayoutPlanner.CreateNormalized(new LayoutRect(10, 20, 5, 6));

        layout.Bounds.Should().Be(new LayoutRect(10, 20, TextBoxFrameLayoutPlanner.MinimumWidth, TextBoxFrameLayoutPlanner.MinimumHeight));
        layout.TextBounds.Should().Be(new LayoutRect(14, 24, 16, 10));
    }

    [Fact]
    public void CreateScaled_ScalesFrameBeforeApplyingRendererInset()
    {
        var layout = TextBoxFrameLayoutPlanner.CreateScaled(
            new LayoutRect(20, 40, TextBoxFrameLayoutPlanner.MinimumWidth, TextBoxFrameLayoutPlanner.MinimumHeight),
            scale: 0.5);

        layout.Bounds.Should().Be(new LayoutRect(10, 20, 12, 9));
        layout.TextBounds.Should().Be(new LayoutRect(14, 24, 4, 1));
    }
}
