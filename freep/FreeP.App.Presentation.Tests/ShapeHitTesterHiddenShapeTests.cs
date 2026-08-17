using FreeP.App.Compositor;
using PresentationModel = FreeP.Core.Model.Presentation;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Regression coverage: <see cref="ShapeHitTester"/> must never let a hidden shape
/// (Selection Pane eye-icon toggle, <see cref="SlideShape.IsHidden"/>) intercept a hit-test.
/// <see cref="SlideCompositor"/> never draws a hidden shape (or its subtree), so hit-testing
/// must skip it the same way - otherwise an invisible object silently swallows clicks meant
/// for whatever is actually visible underneath it.
/// </summary>
public sealed class ShapeHitTesterHiddenShapeTests
{
    private static SlideShape MakeRect(uint id, double x, double y, double w, double h, bool hidden = false) => new()
    {
        Id = id,
        Kind = SlideShapeKind.AutoShape,
        OffsetXEmu = (long)(x * 9525),
        OffsetYEmu = (long)(y * 9525),
        ExtentCxEmu = (long)(w * 9525),
        ExtentCyEmu = (long)(h * 9525),
        IsHidden = hidden,
    };

    [Fact]
    public void HitTest_HiddenShapeOnTop_FallsThroughToVisibleShapeBeneath()
    {
        var p = new PresentationModel();
        var slide = new Slide();
        // Same bounds, hidden TextBox stacked above the visible Rectangle.
        var rectangle = MakeRect(1, 0, 0, 100, 100);
        var hiddenTextBox = MakeRect(2, 0, 0, 100, 100, hidden: true);
        slide.Shapes.Add(rectangle);
        slide.Shapes.Add(hiddenTextBox);
        p.Slides.Add(slide);

        var hit = ShapeHitTester.HitTest(slide, p, 50, 50);

        hit.Should().Be(rectangle.Id);
    }

    [Fact]
    public void HitTest_VisibleShapeOnTop_StillWins()
    {
        // Sibling/no-regression: with the topmost shape visible, it must still win - proves the
        // IsHidden check doesn't over-correct into skipping visible shapes too.
        var p = new PresentationModel();
        var slide = new Slide();
        var rectangle = MakeRect(1, 0, 0, 100, 100);
        var topTextBox = MakeRect(2, 0, 0, 100, 100, hidden: false);
        slide.Shapes.Add(rectangle);
        slide.Shapes.Add(topTextBox);
        p.Slides.Add(slide);

        var hit = ShapeHitTester.HitTest(slide, p, 50, 50);

        hit.Should().Be(topTextBox.Id);
    }

    [Fact]
    public void HitTest_HiddenGroupOnTop_FallsThroughToVisibleShapeBeneath_ChildrenNotHitTested()
    {
        // A hidden GROUP must skip its whole subtree, exactly like SlideCompositor.ComposeShape's
        // early-return - a child that isn't itself marked IsHidden must still not be hit while
        // its hidden parent gates the whole subtree from rendering.
        var p = new PresentationModel();
        var slide = new Slide();
        var rectangle = MakeRect(1, 0, 0, 100, 100);
        var hiddenGroup = new SlideShape { Id = 2, Kind = SlideShapeKind.Group, IsHidden = true };
        hiddenGroup.Children.Add(MakeRect(3, 0, 0, 100, 100));
        slide.Shapes.Add(rectangle);
        slide.Shapes.Add(hiddenGroup);
        p.Slides.Add(slide);

        var hit = ShapeHitTester.HitTest(slide, p, 50, 50);

        hit.Should().Be(rectangle.Id);
    }

    [Fact]
    public void HitTest_HiddenChildInsideVisibleGroup_FallsThroughToShapeBeneath()
    {
        var p = new PresentationModel();
        var slide = new Slide();
        var rectangle = MakeRect(1, 0, 0, 100, 100);
        var visibleGroup = new SlideShape { Id = 2, Kind = SlideShapeKind.Group, IsHidden = false };
        visibleGroup.Children.Add(MakeRect(3, 0, 0, 100, 100, hidden: true));
        slide.Shapes.Add(rectangle);
        slide.Shapes.Add(visibleGroup);
        p.Slides.Add(slide);

        var hit = ShapeHitTester.HitTest(slide, p, 50, 50);

        hit.Should().Be(rectangle.Id);
    }

    [Fact]
    public void MarqueeHitTest_ExcludesHiddenShapes()
    {
        var p = new PresentationModel();
        var slide = new Slide();
        var visible = MakeRect(1, 0, 0, 100, 100);
        var hidden = MakeRect(2, 0, 0, 100, 100, hidden: true);
        slide.Shapes.Add(visible);
        slide.Shapes.Add(hidden);
        p.Slides.Add(slide);

        var hits = ShapeHitTester.MarqueeHitTest(slide, p, 0, 0, 100, 100);

        hits.Should().Contain(visible.Id);
        hits.Should().NotContain(hidden.Id);
    }
}
