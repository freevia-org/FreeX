using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class ShapeFillTransparencyTests
{
    [Fact]
    public void SolidFillTransparency_PreservesThemeReferenceAndChangesAlpha()
    {
        var scheme = new SchemeColorRef { Slot = ThemeColorSlot.Accent1 };
        var source = new ShapeFill.Solid(new ThemeAwareColor(
            new SrgbColor(0x44, 0x72, 0xC4), scheme, alpha: 255));

        ShapeFillTransparency.TryCreate(source, 128, out var result).Should().BeTrue();
        var solid = result.Should().BeOfType<ShapeFill.Solid>().Subject;
        solid.Color.Alpha.Should().Be(128);
        solid.Color.SchemeColor.Should().Be(scheme);
        solid.Color.Resolved.Should().Be(new SrgbColor(0x44, 0x72, 0xC4));
    }

    [Fact]
    public void GradientAndPatternTransparency_UpdatesEveryColorBearingLayer()
    {
        var gradient = new ShapeFill.Gradient(
        [
            new GradientStop(0, new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00))),
            new GradientStop(1, new ThemeAwareColor(new SrgbColor(0x00, 0x00, 0xFF), alpha: 64)),
        ],
        GradientKind.Radial,
        25);
        var pattern = new ShapeFill.Pattern(
            "diagStripe",
            new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00)),
            new ThemeAwareColor(SrgbColor.White, alpha: 32));

        ShapeFillTransparency.TryCreate(gradient, 96, out var gradientResult).Should().BeTrue();
        var gradientCopy = gradientResult.Should().BeOfType<ShapeFill.Gradient>().Subject;
        gradientCopy.Kind.Should().Be(GradientKind.Radial);
        gradientCopy.AngleDegrees.Should().Be(25);
        gradientCopy.Stops.Select(stop => stop.Color.Alpha).Should().Equal(96, 96);

        ShapeFillTransparency.TryCreate(pattern, 96, out var patternResult).Should().BeTrue();
        var patternCopy = patternResult.Should().BeOfType<ShapeFill.Pattern>().Subject;
        patternCopy.Preset.Should().Be("diagStripe");
        patternCopy.ForegroundColor.Alpha.Should().Be(96);
        patternCopy.BackgroundColor.Alpha.Should().Be(96);
    }

    [Fact]
    public void PictureAndNoneFills_AreNotReinterpretedAsColorTransparency()
    {
        ShapeFillTransparency.TryCreate(ShapeFill.None.Instance, 128, out _).Should().BeFalse();
        ShapeFillTransparency.TryCreate(
            new ShapeFill.Picture([1, 2, 3], "image/png"),
            128,
            out _).Should().BeFalse();
    }

    [Fact]
    public void SetShapeFillTransparencyCommand_IsUndoable()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide());
        var shape = new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
            Fill = new ShapeFill.Solid(new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00))),
        };
        presentation.Slides[0].Shapes.Add(shape);
        var bus = new PresentationCommandBus(presentation);

        bus.Execute(new SetShapeFillTransparencyCommand(0, shape.Id, 128));
        ((ShapeFill.Solid)shape.Fill!).Color.Alpha.Should().Be(128);

        bus.Undo();
        ((ShapeFill.Solid)shape.Fill!).Color.Alpha.Should().Be(255);
    }

    [Fact]
    public void Presets_ExposePowerPointFillTransparencyAlphas()
    {
        ShapeFillAuthoringPlanner.OpaqueAlpha.Should().Be(255);
        ShapeFillAuthoringPlanner.HalfTransparentAlpha.Should().Be(128);
        ShapeFillAuthoringPlanner.TransparentAlpha.Should().Be(0);
    }
}
