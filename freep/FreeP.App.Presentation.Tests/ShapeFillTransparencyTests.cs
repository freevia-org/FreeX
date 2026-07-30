using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.Core.IO;
using FreeP.Core.Model;
using System.IO.Compression;
using System.Text;

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

    [Fact]
    public void OutlineTransparency_PreservesStrokeGeometryThemeAndGradientStops()
    {
        var scheme = new SchemeColorRef { Slot = ThemeColorSlot.Accent2 };
        var outline = new ShapeOutline.Visible(
            new ThemeAwareColor(new SrgbColor(0x44, 0x72, 0xC4), scheme, alpha: 255),
            widthPt: 2.25,
            dash: OutlineDash.Dash,
            beginLineEnd: new ShapeLineEnd(ShapeLineEndKind.Triangle));

        ShapeOutlineTransparency.TryCreate(outline, 128, out var result).Should().BeTrue();
        var visible = result.Should().BeOfType<ShapeOutline.Visible>().Subject;
        visible.Color.Alpha.Should().Be(128);
        visible.Color.SchemeColor.Should().Be(scheme);
        visible.WidthPt.Should().Be(2.25);
        visible.Dash.Should().Be(OutlineDash.Dash);
        visible.BeginLineEnd.Should().Be(outline.BeginLineEnd);

        var gradient = new ShapeOutline.GradientVisible(
            new ShapeFill.Gradient([
                new GradientStop(0, new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00))),
                new GradientStop(1, new ThemeAwareColor(new SrgbColor(0x00, 0x00, 0xFF), alpha: 64)),
            ], GradientKind.Radial, 15),
            widthPt: 1.5);
        ShapeOutlineTransparency.TryCreate(gradient, 96, out var gradientResult).Should().BeTrue();
        var gradientCopy = gradientResult.Should().BeOfType<ShapeOutline.GradientVisible>().Subject;
        gradientCopy.WidthPt.Should().Be(1.5);
        gradientCopy.Gradient.Kind.Should().Be(GradientKind.Radial);
        gradientCopy.Gradient.Stops.Select(stop => stop.Color.Alpha).Should().Equal(96, 96);
    }

    [Fact]
    public void SetShapeOutlineTransparencyCommand_IsUndoable()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide());
        var shape = new SlideShape
        {
            Id = 8,
            Kind = SlideShapeKind.AutoShape,
            Outline = new ShapeOutline.Visible(new SrgbColor(0x00, 0x70, 0xC0)),
        };
        presentation.Slides[0].Shapes.Add(shape);
        var bus = new PresentationCommandBus(presentation);

        bus.Execute(new SetShapeOutlineTransparencyCommand(0, shape.Id, 128));
        ((ShapeOutline.Visible)shape.Outline!).Color.Alpha.Should().Be(128);

        bus.Undo();
        ((ShapeOutline.Visible)shape.Outline!).Color.Alpha.Should().Be(255);
    }

    [Fact]
    public void Presets_ExposePowerPointOutlineTransparencyAlphas()
    {
        ShapeOutlineAuthoringPlanner.OpaqueAlpha.Should().Be(255);
        ShapeOutlineAuthoringPlanner.HalfTransparentAlpha.Should().Be(128);
        ShapeOutlineAuthoringPlanner.TransparentAlpha.Should().Be(0);
    }

    [Fact]
    public void OutlineTransparency_RoundTripsThroughPptx()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 9,
            Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = 914400,
            OffsetYEmu = 914400,
            ExtentCxEmu = 1828800,
            ExtentCyEmu = 914400,
            Fill = new ShapeFill.Solid(new ThemeAwareColor(new SrgbColor(0xF2, 0xF2, 0xF2))),
            Outline = new ShapeOutline.Visible(
                new ThemeAwareColor(new SrgbColor(0x44, 0x72, 0xC4), alpha: 96),
                widthPt: 2.0,
                dash: OutlineDash.Dot),
        });

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        var packageBytes = stream.ToArray();
        using (var archive = new ZipArchive(new MemoryStream(packageBytes), ZipArchiveMode.Read))
        using (var reader = new StreamReader(archive.GetEntry("ppt/slides/slide1.xml")!.Open(), Encoding.UTF8))
        {
            reader.ReadToEnd().Should().Contain("<a:ln");
        }

        var reopenedPresentation = PptxPackageReader.Read(new MemoryStream(packageBytes));
        var reopened = reopenedPresentation.Slides[0].Shapes.Single(shape => shape.Id == 9);
        var outline = reopened.Outline.Should().BeOfType<ShapeOutline.Visible>().Subject;
        outline.Color.Alpha.Should().Be(96);
        outline.WidthPt.Should().Be(2.0);
        outline.Dash.Should().Be(OutlineDash.Dot);
    }
}
