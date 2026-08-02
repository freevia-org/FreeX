using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class ShapeTransparencyPlannerTests
{
    [Fact]
    public void SolidFillTransparencyPreservesThemeReferenceAndMapsPercentToAlpha()
    {
        var scheme = new SchemeColorRef { Slot = ThemeColorSlot.Accent1, RoleName = "accent1" };
        var fill = new ShapeFill.Solid(new ThemeAwareColor(SrgbColor.FromRgb(0x4472C4), scheme, 255));

        var changed = ShapeTransparencyPlanner.ApplyFill(fill, 40).Should().BeOfType<ShapeFill.Solid>().Subject;

        changed.Color.Resolved.Should().Be(SrgbColor.FromRgb(0x4472C4));
        changed.Color.Alpha.Should().Be(153);
        changed.Color.SchemeColor.Should().BeSameAs(scheme);
    }

    [Fact]
    public void GradientAndPatternTransparencyPreserveTheirStructure()
    {
        var gradient = new ShapeFill.Gradient(
        [
            new GradientStop(0, new ThemeAwareColor(SrgbColor.FromRgb(0xFF0000), alpha: 200)),
            new GradientStop(1, new ThemeAwareColor(SrgbColor.FromRgb(0x0000FF), alpha: 100)),
        ], GradientKind.Radial, 33);
        var pattern = new ShapeFill.Pattern(
            "pct50",
            new ThemeAwareColor(SrgbColor.Black),
            new ThemeAwareColor(SrgbColor.White));

        var changedGradient = ShapeTransparencyPlanner.ApplyFill(gradient, 75).Should().BeOfType<ShapeFill.Gradient>().Subject;
        var changedPattern = ShapeTransparencyPlanner.ApplyFill(pattern, 25).Should().BeOfType<ShapeFill.Pattern>().Subject;

        changedGradient.Kind.Should().Be(GradientKind.Radial);
        changedGradient.AngleDegrees.Should().Be(33);
        changedGradient.Stops.Select(x => x.Color.Alpha).Should().AllBeEquivalentTo((byte)64);
        changedPattern.Preset.Should().Be("pct50");
        changedPattern.ForegroundColor.Alpha.Should().Be(191);
        changedPattern.BackgroundColor.Alpha.Should().Be(191);
    }

    [Fact]
    public void OutlineTransparencyPreservesGeometryAndGradientStops()
    {
        var outline = new ShapeOutline.GradientVisible(
            new ShapeFill.Gradient(
            [
                new GradientStop(0, new ThemeAwareColor(SrgbColor.Black)),
                new GradientStop(1, new ThemeAwareColor(SrgbColor.White)),
            ]),
            widthPt: 2.25,
            dash: OutlineDash.Dash,
            beginLineEnd: new ShapeLineEnd(ShapeLineEndKind.Triangle));

        var changed = ShapeTransparencyPlanner.ApplyOutline(outline, 50)
            .Should().BeOfType<ShapeOutline.GradientVisible>().Subject;

        changed.WidthPt.Should().Be(2.25);
        changed.Dash.Should().Be(OutlineDash.Dash);
        changed.BeginLineEnd!.Kind.Should().Be(ShapeLineEndKind.Triangle);
        changed.Gradient.Stops.Select(x => x.Color.Alpha).Should().AllBeEquivalentTo((byte)128);
    }

    [Fact]
    public void NoneAndPictureFillsAreNotRewritten()
    {
        var picture = new ShapeFill.Picture([1, 2, 3], "image/png");

        ShapeTransparencyPlanner.ApplyFill(ShapeFill.None.Instance, 50).Should().BeSameAs(ShapeFill.None.Instance);
        ShapeTransparencyPlanner.ApplyFill(picture, 50).Should().BeSameAs(picture);
        ShapeTransparencyPlanner.ToAlpha(0).Should().Be(255);
        ShapeTransparencyPlanner.ToAlpha(100).Should().Be(0);
    }

    [Fact]
    public void EditingSessionAppliesTransparencyThroughUndoBus()
    {
        var presentation = new Presentation();
        var slide = new Slide();
        var shape = new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
            Fill = new ShapeFill.Solid(new ThemeAwareColor(SrgbColor.FromRgb(0x4472C4))),
            Outline = new ShapeOutline.Visible(ThemeAwareColor.Black),
        };
        slide.Shapes.Add(shape);
        presentation.Slides.Add(slide);

        var bus = new PresentationCommandBus(presentation);
        var editor = new EditingSession(presentation, bus);
        editor.Select(shape.Id);
        editor.SetSelectedFillTransparency(50);
        editor.SetSelectedOutlineTransparency(25);

        ((ShapeFill.Solid)shape.Fill!).Color.Alpha.Should().Be(128);
        ((ShapeOutline.Visible)shape.Outline!).Color.Alpha.Should().Be(191);

        bus.Undo();
        ((ShapeOutline.Visible)shape.Outline!).Color.Alpha.Should().Be(255);
        bus.Undo();
        ((ShapeFill.Solid)shape.Fill!).Color.Alpha.Should().Be(255);
    }
}
