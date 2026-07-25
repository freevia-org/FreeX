using System.IO.Compression;
using System.Xml.Linq;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class AnimationPresetRoundTripTests
{
    [Fact]
    public void RepeatAndAutoReverseTimingSurviveReadCloneAndWrite()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 914400,
            OffsetYEmu = 457200,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 1371600,
        });
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = 7,
            Kind = AnimationKind.Emphasis,
            Preset = AnimationPreset.Pulse,
            RepeatCount = 3,
            AutoReverse = true,
        });

        using var first = new MemoryStream();
        PptxPackageWriter.Write(presentation, first);
        var reloaded = PptxPackageReader.Read(new MemoryStream(first.ToArray()));
        var animation = reloaded.Slides[0].Animations.Single();
        animation.RepeatCount.Should().Be(3);
        animation.RepeatIndefinitely.Should().BeFalse();
        animation.AutoReverse.Should().BeTrue();

        var clone = SlideCloner.CloneSlide(reloaded.Slides[0]).Animations.Single();
        clone.RepeatCount.Should().Be(3);
        clone.AutoReverse.Should().BeTrue();

        using var second = new MemoryStream();
        PptxPackageWriter.Write(reloaded, second);
        using var archive = new ZipArchive(new MemoryStream(second.ToArray()), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("ppt/slides/slide1.xml")!.Open());
        var slideXml = XDocument.Parse(reader.ReadToEnd());
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        var cTn = slideXml.Descendants(p + "cTn")
            .Single(element => element.Attribute("presetID")?.Value is not null
                && element.Attribute("nodeType")?.Value == "withEffect");
        cTn.Attribute("repeatCount")!.Value.Should().Be("3");
        cTn.Attribute("autoRev")!.Value.Should().Be("1");
    }

    [Fact]
    public void IndefiniteRepeatTimingRoundTripsWithoutFiniteCount()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape { Id = 7, Kind = SlideShapeKind.AutoShape });
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = 7,
            RepeatIndefinitely = true,
        });

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        var reloaded = PptxPackageReader.Read(new MemoryStream(stream.ToArray()));
        var animation = reloaded.Slides[0].Animations.Single();
        animation.RepeatIndefinitely.Should().BeTrue();
        animation.RepeatCount.Should().BeNull();
    }

    [Fact]
    public void UnknownPresetTokensSurviveReadCloneAndWrite()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 914400,
            OffsetYEmu = 457200,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 1371600,
        });
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = 7,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Appear,
            RawPresetClass = "entr",
            RawPresetId = 42,
            RawPresetSubtype = "fromBottomRight",
        });

        using var first = new MemoryStream();
        PptxPackageWriter.Write(presentation, first);
        var firstBytes = first.ToArray();

        var reloaded = PptxPackageReader.Read(new MemoryStream(firstBytes));
        var animation = reloaded.Slides[0].Animations.Single();
        animation.Preset.Should().Be(AnimationPreset.Appear,
            "unknown effects retain deterministic fallback playback semantics");
        animation.RawPresetClass.Should().Be("entr");
        animation.RawPresetId.Should().Be(42);
        animation.RawPresetSubtype.Should().Be("fromBottomRight");

        var clonedSlide = SlideCloner.CloneSlide(reloaded.Slides[0]);
        var clonedAnimation = clonedSlide.Animations.Single();
        clonedAnimation.RawPresetClass.Should().Be("entr");
        clonedAnimation.RawPresetId.Should().Be(42);
        clonedAnimation.RawPresetSubtype.Should().Be("fromBottomRight");

        using var second = new MemoryStream();
        reloaded.Slides[0] = clonedSlide;
        PptxPackageWriter.Write(reloaded, second);
        using var archive = new ZipArchive(new MemoryStream(second.ToArray()), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("ppt/slides/slide1.xml")!.Open());
        var slideXml = XDocument.Parse(reader.ReadToEnd());
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        var cTn = slideXml.Descendants(p + "cTn")
            .Single(element => element.Attribute("presetID")?.Value == "42");
        cTn.Attribute("presetClass")!.Value.Should().Be("entr");
        cTn.Attribute("presetSubtype")!.Value.Should().Be("fromBottomRight");
    }
}
