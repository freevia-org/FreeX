using System.IO.Compression;
using System.Xml.Linq;
using FreeP.App.Compositor;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class AnimationPresetRoundTripTests
{
    [Theory]
    [InlineData(AnimationPreset.Bold, 15)]
    [InlineData(AnimationPreset.Underline, 18)]
    public void GenericFontEmphasisMappingUsesPowerPointNativePresetIds(
        AnimationPreset preset,
        int expectedPresetId)
    {
        var mapped = PptxAnimationMap.AnimationPresetToOoxml(preset, AnimationKind.Emphasis);

        mapped.presetClass.Should().Be("emph");
        mapped.presetId.Should().Be(expectedPresetId);
    }

    [Fact]
    public void PowerPointTeeterPreset32SurvivesReadAndWriteAsTeeter()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 1371600,
        });
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = 7,
            Kind = AnimationKind.Emphasis,
            Preset = AnimationPreset.Teeter,
        });

        using var first = new MemoryStream();
        PptxPackageWriter.Write(presentation, first);

        using (var archive = new ZipArchive(new MemoryStream(first.ToArray()), ZipArchiveMode.Read))
        using (var reader = new StreamReader(archive.GetEntry("ppt/slides/slide1.xml")!.Open()))
        {
            var slideXml = XDocument.Parse(reader.ReadToEnd());
            XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
            slideXml.Descendants(p + "cTn")
                .Single(element => element.Attribute("nodeType")?.Value == "withEffect")
                .Attribute("presetID")!.Value
                .Should().Be("32");
        }

        var reloaded = PptxPackageReader.Read(new MemoryStream(first.ToArray()));
        var animation = reloaded.Slides[0].Animations.Single();
        animation.Preset.Should().Be(AnimationPreset.Teeter);
        animation.RawPresetClass.Should().BeNull();
        animation.RawPresetId.Should().BeNull();

        using var second = new MemoryStream();
        PptxPackageWriter.Write(reloaded, second);
        using var secondArchive = new ZipArchive(new MemoryStream(second.ToArray()), ZipArchiveMode.Read);
        using var secondReader = new StreamReader(secondArchive.GetEntry("ppt/slides/slide1.xml")!.Open());
        var secondXml = XDocument.Parse(secondReader.ReadToEnd());
        XNamespace p2 = "http://schemas.openxmlformats.org/presentationml/2006/main";
        secondXml.Descendants(p2 + "cTn")
            .Single(element => element.Attribute("nodeType")?.Value == "withEffect")
            .Attribute("presetID")!.Value
            .Should().Be("32");
    }

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
            Acceleration = 25000,
            Deceleration = 35000,
        });

        using var first = new MemoryStream();
        PptxPackageWriter.Write(presentation, first);
        var reloaded = PptxPackageReader.Read(new MemoryStream(first.ToArray()));
        var animation = reloaded.Slides[0].Animations.Single();
        animation.RepeatCount.Should().Be(3);
        animation.RepeatIndefinitely.Should().BeFalse();
        animation.AutoReverse.Should().BeTrue();
        animation.Acceleration.Should().Be(25000);
        animation.Deceleration.Should().Be(35000);

        var clone = SlideCloner.CloneSlide(reloaded.Slides[0]).Animations.Single();
        clone.RepeatCount.Should().Be(3);
        clone.AutoReverse.Should().BeTrue();
        clone.Acceleration.Should().Be(25000);
        clone.Deceleration.Should().Be(35000);

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
        cTn.Attribute("accel")!.Value.Should().Be("25000");
        cTn.Attribute("decel")!.Value.Should().Be("35000");
    }

    [Fact]
    public void IndefiniteRepeatTimingRoundTripsWithoutFiniteCount()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
        });
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

    [Theory]
    [InlineData(AnimationDirection.FromTopLeft, "fromTopLeft")]
    [InlineData(AnimationDirection.FromTopRight, "fromTopRight")]
    [InlineData(AnimationDirection.FromBottomLeft, "fromBottomLeft")]
    [InlineData(AnimationDirection.FromBottomRight, "fromBottomRight")]
    public void FlyInDiagonalDirectionSubtypeSurvivesReadAndWrite(
        AnimationDirection direction,
        string expectedSubtype)
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 457200,
        });
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = 7,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.FlyIn,
            Direction = direction,
        });

        using var first = new MemoryStream();
        PptxPackageWriter.Write(presentation, first);
        var reloaded = PptxPackageReader.Read(new MemoryStream(first.ToArray()));
        reloaded.Slides[0].Animations.Single().Direction.Should().Be(direction);

        using var second = new MemoryStream();
        PptxPackageWriter.Write(reloaded, second);
        using var archive = new ZipArchive(new MemoryStream(second.ToArray()), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("ppt/slides/slide1.xml")!.Open());
        var slideXml = XDocument.Parse(reader.ReadToEnd());
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        var cTn = slideXml.Descendants(p + "cTn")
            .Single(element => element.Attribute("presetID")?.Value == "2");
        cTn.Attribute("presetSubtype")!.Value.Should().Be(expectedSubtype);
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

    [Theory]
    [InlineData(26)] // PowerPoint FlashBulb
    [InlineData(27)] // PowerPoint Flicker
    public void ImportedFlashBulbAndFlickerRetainNativeIdsAndUseBlinkPlayback(int presetId)
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
            RawPresetClass = "emph",
            RawPresetId = presetId,
            RawPresetSubtype = "0",
        });

        using var first = new MemoryStream();
        PptxPackageWriter.Write(presentation, first);
        var reloaded = PptxPackageReader.Read(new MemoryStream(first.ToArray()));
        var animation = reloaded.Slides[0].Animations.Single();

        animation.Preset.Should().Be(AnimationPreset.Blink);
        animation.RawPresetClass.Should().Be("emph");
        animation.RawPresetId.Should().Be(presetId);
        animation.RawPresetSubtype.Should().Be("0");

        SlideShowPlaybackPlanner.PlanShapeAnimation(animation, startDelayMs: 0)
            .EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Blink);

        using var second = new MemoryStream();
        PptxPackageWriter.Write(reloaded, second);
        using var archive = new ZipArchive(new MemoryStream(second.ToArray()), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("ppt/slides/slide1.xml")!.Open());
        var slideXml = XDocument.Parse(reader.ReadToEnd());
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        var cTn = slideXml.Descendants(p + "cTn")
            .Single(element => element.Attribute("presetID")?.Value == presetId.ToString());
        cTn.Attribute("presetClass")!.Value.Should().Be("emph");
        cTn.Attribute("presetSubtype")!.Value.Should().Be("0");
    }

    [Fact]
    public void ImportedColorWaveRetainsNativeIdAndUsesColorPulsePlayback()
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
            RawPresetClass = "emph",
            RawPresetId = 20,
            RawPresetSubtype = "0",
        });

        using var first = new MemoryStream();
        PptxPackageWriter.Write(presentation, first);
        var reloaded = PptxPackageReader.Read(new MemoryStream(first.ToArray()));
        var animation = reloaded.Slides[0].Animations.Single();

        animation.Preset.Should().Be(AnimationPreset.ColorPulse);
        animation.RawPresetClass.Should().Be("emph");
        animation.RawPresetId.Should().Be(20);
        animation.RawPresetSubtype.Should().Be("0");
        SlideShowPlaybackPlanner.PlanShapeAnimation(animation, startDelayMs: 0)
            .EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.ColorPulse);

        using var second = new MemoryStream();
        PptxPackageWriter.Write(reloaded, second);
        using var archive = new ZipArchive(new MemoryStream(second.ToArray()), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("ppt/slides/slide1.xml")!.Open());
        var slideXml = XDocument.Parse(reader.ReadToEnd());
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        var cTn = slideXml.Descendants(p + "cTn")
            .Single(element => element.Attribute("presetID")?.Value == "20");
        cTn.Attribute("presetClass")!.Value.Should().Be("emph");
        cTn.Attribute("presetSubtype")!.Value.Should().Be("0");
    }

    [Fact]
    public void ImportedChangeFontColorRetainsNativeIdAndUsesColorPlayback()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape { Id = 7, Kind = SlideShapeKind.AutoShape });
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = 7,
            Kind = AnimationKind.Emphasis,
            Preset = AnimationPreset.ChangeColor,
            RawPresetClass = "emph",
            RawPresetId = 3,
            RawPresetSubtype = "0",
            PreservedColorBehaviorXml = """
                <p:animClr xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" clrSpc="rgb">
                  <p:cBhvr><p:cTn id="77" dur="500" fill="hold"/><p:tgtEl><p:spTgt spid="7"/></p:tgtEl></p:cBhvr>
                  <p:clrFrom xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"><a:srgbClr val="000000"/></p:clrFrom>
                  <p:clrTo xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"><a:srgbClr val="FF0000"/></p:clrTo>
                </p:animClr>
                """,
        });

        using var first = new MemoryStream();
        PptxPackageWriter.Write(presentation, first);
        var reloaded = PptxPackageReader.Read(new MemoryStream(first.ToArray()));
        var animation = reloaded.Slides[0].Animations.Single();

        animation.Preset.Should().Be(AnimationPreset.ChangeColor);
        animation.RawPresetClass.Should().Be("emph");
        animation.RawPresetId.Should().Be(3);
        animation.PreservedColorBehaviorXml.Should().Contain("FF0000");
        SlideShowPlaybackPlanner.PlanShapeAnimation(animation, startDelayMs: 0)
            .EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.ChangeColor);

        using var second = new MemoryStream();
        PptxPackageWriter.Write(reloaded, second);
        using var archive = new ZipArchive(new MemoryStream(second.ToArray()), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("ppt/slides/slide1.xml")!.Open());
        var slideXml = XDocument.Parse(reader.ReadToEnd());
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        var cTn = slideXml.Descendants(p + "cTn")
            .Single(element => element.Attribute("presetClass")?.Value == "emph"
                && element.Attribute("presetID")?.Value == "3");
        cTn.Attribute("presetSubtype")!.Value.Should().Be("0");
        slideXml.Descendants(p + "animClr").Should().ContainSingle();
    }

    [Fact]
    public void ImportedChangeFontSizeRetainsNativeBehaviorAndUsesGrowPlayback()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape { Id = 7, Kind = SlideShapeKind.AutoShape });
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = 7,
            Kind = AnimationKind.Emphasis,
            Preset = AnimationPreset.Grow,
            RawPresetClass = "emph",
            RawPresetId = 4,
            RawPresetSubtype = "2",
            ScaleBehavior = AnimationScaleBehavior.FromTo(1.5),
            PreservedNumericBehaviorXml = """
                <p:anim xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" to="1.5" calcmode="lin" valueType="num">
                  <p:cBhvr override="childStyle">
                    <p:cTn id="77" dur="2000" fill="hold"/>
                    <p:tgtEl><p:spTgt spid="7"/></p:tgtEl>
                    <p:attrNameLst><p:attrName>style.fontSize</p:attrName></p:attrNameLst>
                  </p:cBhvr>
                </p:anim>
                """,
        });

        using var first = new MemoryStream();
        PptxPackageWriter.Write(presentation, first);
        var reloaded = PptxPackageReader.Read(new MemoryStream(first.ToArray()));
        var animation = reloaded.Slides[0].Animations.Single();

        animation.Preset.Should().Be(AnimationPreset.Grow);
        animation.RawPresetClass.Should().Be("emph");
        animation.RawPresetId.Should().Be(4);
        animation.RawPresetSubtype.Should().Be("2");
        animation.ScaleBehavior!.ToX.Should().Be(AnimationScaleBehavior.Format(1.5));
        animation.PreservedNumericBehaviorXml.Should().Contain("style.fontSize");
        AnimationAmountSemantics.ResolveScale(animation.Preset, animation.ScaleBehavior).Should().Be(1.5);
        SlideShowPlaybackPlanner.PlanShapeAnimation(animation, startDelayMs: 0)
            .EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.GrowShrink);

        var clonedAnimation = SlideCloner.CloneSlide(reloaded.Slides[0]).Animations.Single();
        clonedAnimation.PreservedNumericBehaviorXml.Should().Be(animation.PreservedNumericBehaviorXml);

        using var second = new MemoryStream();
        PptxPackageWriter.Write(reloaded, second);
        using var archive = new ZipArchive(new MemoryStream(second.ToArray()), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("ppt/slides/slide1.xml")!.Open());
        var slideXml = XDocument.Parse(reader.ReadToEnd());
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        var cTn = slideXml.Descendants(p + "cTn")
            .Single(element => element.Attribute("presetClass")?.Value == "emph"
                && element.Attribute("presetID")?.Value == "4");
        cTn.Attribute("presetSubtype")!.Value.Should().Be("2");
        cTn.Descendants(p + "anim").Should().ContainSingle()
            .Which.Descendants(p + "attrName").Single().Value.Should().Be("style.fontSize");
        cTn.Descendants(p + "animScale").Should().BeEmpty();
    }

    [Fact]
    public void AuthoredChangeFontSizeWritesNativePowerPointBehavior()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape { Id = 7, Kind = SlideShapeKind.AutoShape });
        presentation.Slides[0].Animations.Add(
            PresentationAnimationCommandPlanner.BuildFontSizeAnimation(7));

        using var first = new MemoryStream();
        PptxPackageWriter.Write(presentation, first);
        var reloaded = PptxPackageReader.Read(new MemoryStream(first.ToArray()));
        var animation = reloaded.Slides[0].Animations.Single();

        animation.Preset.Should().Be(AnimationPreset.Grow);
        animation.RawPresetClass.Should().Be("emph");
        animation.RawPresetId.Should().Be(4);
        animation.RawPresetSubtype.Should().Be("2");
        animation.PreservedNumericBehaviorXml.Should().Contain("style.fontSize");
        animation.PreservedNumericBehaviorXml.Should().Contain("to=\"1.5\"");

        using var archive = new ZipArchive(new MemoryStream(first.ToArray()), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("ppt/slides/slide1.xml")!.Open());
        var slideXml = XDocument.Parse(reader.ReadToEnd());
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        var cTn = slideXml.Descendants(p + "cTn")
            .Single(element => element.Attribute("presetClass")?.Value == "emph"
                && element.Attribute("presetID")?.Value == "4");
        cTn.Attribute("presetSubtype")!.Value.Should().Be("2");
        cTn.Descendants(p + "anim").Should().ContainSingle()
            .Which.Descendants(p + "attrName").Single().Value.Should().Be("style.fontSize");
        cTn.Descendants(p + "anim").Single().Attribute("to")!.Value.Should().Be("1.5");
        cTn.Descendants(p + "animScale").Should().BeEmpty();
    }

    [Fact]
    public void AuthoredChangeLineColorWritesNativeStrokeBehaviors()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape { Id = 7, Kind = SlideShapeKind.AutoShape });
        presentation.Slides[0].Animations.Add(
            PresentationAnimationCommandPlanner.BuildLineColorAnimation(7));

        using var first = new MemoryStream();
        PptxPackageWriter.Write(presentation, first);
        var reloaded = PptxPackageReader.Read(new MemoryStream(first.ToArray()));
        var animation = reloaded.Slides[0].Animations.Single();

        animation.Preset.Should().Be(AnimationPreset.ChangeLineColor);
        animation.RawPresetClass.Should().BeNull();
        animation.RawPresetId.Should().BeNull();
        animation.RawPresetSubtype.Should().BeNull();
        animation.EffectSubtype.Should().Be("2");
        animation.PreservedLineBehaviorXml.Should().Contain("stroke.color");
        animation.PreservedLineBehaviorXml.Should().Contain("stroke.on");
        animation.PreservedLineBehaviorXml.Should().Contain("accent2");
        animation.PreservedLineBehaviorXml.Should().Contain("spid=\"7\"");
        SlideShowPlaybackPlanner.PlanShapeAnimation(animation, startDelayMs: 0)
            .EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.ChangeColor);

        var clonedAnimation = SlideCloner.CloneSlide(reloaded.Slides[0]).Animations.Single();
        clonedAnimation.PreservedLineBehaviorXml.Should().Be(animation.PreservedLineBehaviorXml);

        using var archive = new ZipArchive(new MemoryStream(first.ToArray()), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("ppt/slides/slide1.xml")!.Open());
        var slideXml = XDocument.Parse(reader.ReadToEnd());
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        var cTn = slideXml.Descendants(p + "cTn")
            .Single(element => element.Attribute("presetClass")?.Value == "emph"
                && element.Attribute("presetID")?.Value == "7");
        cTn.Attribute("presetSubtype")!.Value.Should().Be("2");
        cTn.Descendants(p + "animClr").Should().ContainSingle()
            .Which.Descendants(p + "attrName").Single().Value.Should().Be("stroke.color");
        cTn.Descendants(p + "animClr").Single()
            .Descendants(p + "spTgt").Single().Attribute("spid")!.Value.Should().Be("7");
        var lineOnSetter = cTn.Descendants(p + "set")
            .Single(element => element.Descendants(p + "attrName")
                .Any(attribute => attribute.Value == "stroke.on"));
        lineOnSetter.Descendants(p + "attrName").Single().Value.Should().Be("stroke.on");
        lineOnSetter
            .Descendants(p + "spTgt").Single().Attribute("spid")!.Value.Should().Be("7");
        cTn.Descendants(a + "schemeClr").Single().Attribute("val")!.Value.Should().Be("accent2");
    }

    [Fact]
    public void AuthoredChangeFontStyleWritesNativeStyleBehaviors()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape { Id = 7, Kind = SlideShapeKind.AutoShape });
        presentation.Slides[0].Animations.Add(
            PresentationAnimationCommandPlanner.BuildFontStyleAnimation(7));

        using var first = new MemoryStream();
        PptxPackageWriter.Write(presentation, first);
        var reloaded = PptxPackageReader.Read(new MemoryStream(first.ToArray()));
        var animation = reloaded.Slides[0].Animations.Single();

        animation.Preset.Should().Be(AnimationPreset.ChangeFontStyle);
        animation.RawPresetClass.Should().BeNull();
        animation.RawPresetId.Should().BeNull();
        animation.RawPresetSubtype.Should().BeNull();
        animation.EffectSubtype.Should().Be("1");
        animation.PreservedFontStyleBehaviorXml.Should().Contain("style.fontStyle");
        animation.PreservedFontStyleBehaviorXml.Should().Contain("style.fontWeight");
        animation.PreservedFontStyleBehaviorXml.Should().Contain("style.textDecorationUnderline");
        animation.PreservedFontStyleBehaviorXml.Should().Contain("normal");
        animation.PreservedFontStyleBehaviorXml.Should().Contain("bold");
        animation.PreservedFontStyleBehaviorXml.Should().Contain("false");
        SlideShowPlaybackPlanner.PlanShapeAnimation(animation, startDelayMs: 0)
            .EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.ChangeFontStyle);

        var clonedAnimation = SlideCloner.CloneSlide(reloaded.Slides[0]).Animations.Single();
        clonedAnimation.PreservedFontStyleBehaviorXml.Should().Be(animation.PreservedFontStyleBehaviorXml);

        using var archive = new ZipArchive(new MemoryStream(first.ToArray()), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("ppt/slides/slide1.xml")!.Open());
        var slideXml = XDocument.Parse(reader.ReadToEnd());
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        var cTn = slideXml.Descendants(p + "cTn")
            .Single(element => element.Attribute("presetClass")?.Value == "emph"
                && element.Attribute("presetID")?.Value == "5");
        cTn.Attribute("presetSubtype")!.Value.Should().Be("1");
        var styleSetters = cTn.Descendants(p + "set")
            .Where(element => element.Descendants(p + "attrName")
                .Any(attribute => attribute.Value.StartsWith("style.", StringComparison.Ordinal)))
            .ToList();
        styleSetters.Should().HaveCount(3);
        styleSetters.Single(element => element.Descendants(p + "attrName")
                .Any(attribute => attribute.Value == "style.fontStyle"))
            .Descendants(p + "strVal").Single().Attribute("val")!.Value.Should().Be("normal");
        styleSetters.Single(element => element.Descendants(p + "attrName")
                .Any(attribute => attribute.Value == "style.fontWeight"))
            .Descendants(p + "strVal").Single().Attribute("val")!.Value.Should().Be("bold");
        styleSetters.Single(element => element.Descendants(p + "attrName")
                .Any(attribute => attribute.Value == "style.textDecorationUnderline"))
            .Descendants(p + "strVal").Single().Attribute("val")!.Value.Should().Be("false");
    }

    [Fact]
    public void AuthoredBoldWritesNativeFontWeightBehavior()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape { Id = 7, Kind = SlideShapeKind.AutoShape });
        presentation.Slides[0].Animations.Add(
            PresentationAnimationCommandPlanner.BuildBoldAnimation(7));

        using var first = new MemoryStream();
        PptxPackageWriter.Write(presentation, first);
        var reloaded = PptxPackageReader.Read(new MemoryStream(first.ToArray()));
        var animation = reloaded.Slides[0].Animations.Single();

        animation.Preset.Should().Be(AnimationPreset.Bold);
        animation.RawPresetClass.Should().Be("emph");
        animation.RawPresetId.Should().Be(15);
        animation.RawPresetSubtype.Should().Be("0");
        animation.PreservedFontStyleBehaviorXml.Should().Contain("style.fontWeight");
        animation.PreservedFontStyleBehaviorXml.Should().Contain("bold");
        SlideShowPlaybackPlanner.PlanShapeAnimation(animation, startDelayMs: 0)
            .EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Bold);

        using var archive = new ZipArchive(new MemoryStream(first.ToArray()), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("ppt/slides/slide1.xml")!.Open());
        var slideXml = XDocument.Parse(reader.ReadToEnd());
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        var cTn = slideXml.Descendants(p + "cTn")
            .Single(element => element.Attribute("presetClass")?.Value == "emph"
                && element.Attribute("presetID")?.Value == "15");
        cTn.Descendants(p + "set")
            .Where(element => element.Descendants(p + "attrName")
                .Any(attribute => attribute.Value == "style.fontWeight"))
            .Should().ContainSingle()
            .Which.Descendants(p + "attrName").Single().Value.Should().Be("style.fontWeight");
        cTn.Descendants(p + "strVal").Single().Attribute("val")!.Value.Should().Be("bold");
    }

    [Fact]
    public void AuthoredUnderlineWritesNativeIteratorAndStyleBehavior()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape { Id = 7, Kind = SlideShapeKind.AutoShape });
        presentation.Slides[0].Animations.Add(
            PresentationAnimationCommandPlanner.BuildUnderlineAnimation(7));

        using var first = new MemoryStream();
        PptxPackageWriter.Write(presentation, first);
        var reloaded = PptxPackageReader.Read(new MemoryStream(first.ToArray()));
        var animation = reloaded.Slides[0].Animations.Single();

        animation.Preset.Should().Be(AnimationPreset.Underline);
        animation.RawPresetClass.Should().Be("emph");
        animation.RawPresetId.Should().Be(18);
        animation.RawPresetSubtype.Should().Be("0");
        animation.PreservedFontStyleBehaviorXml.Should().Contain("style.textDecorationUnderline");
        animation.PreservedFontStyleBehaviorXml.Should().Contain("true");
        animation.PreservedIterationXml.Should().Contain("type=\"lt\"");
        animation.PreservedIterationXml.Should().Contain("val=\"4000\"");
        SlideShowPlaybackPlanner.PlanShapeAnimation(animation, startDelayMs: 0)
            .EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.Underline);

        using var archive = new ZipArchive(new MemoryStream(first.ToArray()), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("ppt/slides/slide1.xml")!.Open());
        var slideXml = XDocument.Parse(reader.ReadToEnd());
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        var cTn = slideXml.Descendants(p + "cTn")
            .Single(element => element.Attribute("presetClass")?.Value == "emph"
                && element.Attribute("presetID")?.Value == "18");
        cTn.Descendants(p + "iterate").Should().ContainSingle()
            .Which.Attribute("type")!.Value.Should().Be("lt");
        cTn.Descendants(p + "tmPct").Single().Attribute("val")!.Value.Should().Be("4000");
        cTn.Descendants(p + "set")
            .Where(element => element.Descendants(p + "attrName")
                .Any(attribute => attribute.Value == "style.textDecorationUnderline"))
            .Should().ContainSingle()
            .Which.Descendants(p + "attrName").Single().Value.Should().Be("style.textDecorationUnderline");
    }

    [Fact]
    public void ImportedChangeFillColorRetainsFillTargetAndNativeSetters()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
            Fill = new ShapeFill.Solid(SrgbColor.FromRgb(0x4472C4)),
        });
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = 7,
            Kind = AnimationKind.Emphasis,
            Preset = AnimationPreset.ChangeFillColor,
            RawPresetClass = "emph",
            RawPresetId = 1,
            RawPresetSubtype = "2",
            PreservedFillBehaviorXml = """
                <p:childTnLst xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main">
                  <p:animClr clrSpc="rgb" dir="cw">
                    <p:cBhvr>
                      <p:cTn id="6" dur="2000" fill="hold"/>
                      <p:tgtEl><p:spTgt spid="7"/></p:tgtEl>
                      <p:attrNameLst><p:attrName>fillcolor</p:attrName></p:attrNameLst>
                    </p:cBhvr>
                    <p:to><a:schemeClr xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" val="accent2"/></p:to>
                  </p:animClr>
                  <p:set>
                    <p:cBhvr><p:cTn id="7" dur="2000" fill="hold"/><p:tgtEl><p:spTgt spid="7"/></p:tgtEl><p:attrNameLst><p:attrName>fill.type</p:attrName></p:attrNameLst></p:cBhvr>
                    <p:to><p:strVal val="solid"/></p:to>
                  </p:set>
                  <p:set>
                    <p:cBhvr><p:cTn id="8" dur="2000" fill="hold"/><p:tgtEl><p:spTgt spid="7"/></p:tgtEl><p:attrNameLst><p:attrName>fill.on</p:attrName></p:attrNameLst></p:cBhvr>
                    <p:to><p:strVal val="true"/></p:to>
                  </p:set>
                </p:childTnLst>
                """,
        });

        using var first = new MemoryStream();
        PptxPackageWriter.Write(presentation, first);
        var reloaded = PptxPackageReader.Read(new MemoryStream(first.ToArray()));
        var animation = reloaded.Slides[0].Animations.Single();

        animation.Preset.Should().Be(AnimationPreset.ChangeFillColor);
        animation.RawPresetClass.Should().Be("emph");
        animation.RawPresetId.Should().Be(1);
        animation.PreservedFillBehaviorXml.Should().Contain("fillcolor");
        animation.PreservedFillBehaviorXml.Should().Contain("fill.type");
        animation.PreservedFillBehaviorXml.Should().Contain("fill.on");
        var plan = SlideShowPlaybackPlanner.PlanShapeAnimation(
            animation,
            startDelayMs: 0,
            presentation: reloaded);
        plan.EffectKind.Should().Be(SlideShowShapeAnimationEffectKind.ChangeFillColor);
        plan.ColorFromHex.Should().Be("4472C4");
        plan.ColorToHex.Should().Be("ED7D31");

        var clonedAnimation = SlideCloner.CloneSlide(reloaded.Slides[0]).Animations.Single();
        clonedAnimation.PreservedFillBehaviorXml.Should().Be(animation.PreservedFillBehaviorXml);

        using var second = new MemoryStream();
        PptxPackageWriter.Write(reloaded, second);
        using var archive = new ZipArchive(new MemoryStream(second.ToArray()), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("ppt/slides/slide1.xml")!.Open());
        var slideXml = XDocument.Parse(reader.ReadToEnd());
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        var cTn = slideXml.Descendants(p + "cTn")
            .Single(element => element.Attribute("presetClass")?.Value == "emph"
                && element.Attribute("presetID")?.Value == "1");
        cTn.Descendants(p + "animClr").Single()
            .Descendants(p + "attrName").Single().Value.Should().Be("fillcolor");
        cTn.Descendants(p + "strVal")
            .Select(element => element.Attribute("val")?.Value)
            .Should().Contain(new[] { "solid", "true" });
    }

    [Fact]
    public void AuthoredChangeFillColorWritesNativePowerPointBehavior()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape { Id = 7, Kind = SlideShapeKind.AutoShape });
        presentation.Slides[0].Animations.Add(
            PresentationAnimationCommandPlanner.BuildAnimation(
                AnimationKind.Emphasis,
                AnimationPreset.ChangeFillColor,
                shapeId: 7));

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);

        var reloaded = PptxPackageReader.Read(new MemoryStream(stream.ToArray()));
        var animation = reloaded.Slides[0].Animations.Single();
        animation.Preset.Should().Be(AnimationPreset.ChangeFillColor);
        animation.RawPresetClass.Should().Be("emph");
        animation.RawPresetId.Should().Be(1);
        animation.RawPresetSubtype.Should().Be("2");
        animation.PreservedFillBehaviorXml.Should().Contain("fillcolor");
        animation.PreservedFillBehaviorXml.Should().Contain("accent2");
        animation.PreservedFillBehaviorXml.Should().Contain("spid=\"7\"");

        using var archive = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("ppt/slides/slide1.xml")!.Open());
        var slideXml = XDocument.Parse(reader.ReadToEnd());
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        slideXml.Descendants(p + "animClr")
            .Single()
            .Descendants(p + "attrName")
            .Single()
            .Value.Should().Be("fillcolor");
    }

    [Fact]
    public void AuthoredChangeFontColorWritesNativePowerPointBehavior()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape { Id = 7, Kind = SlideShapeKind.AutoShape });
        presentation.Slides[0].Animations.Add(
            PresentationAnimationCommandPlanner.BuildFontColorAnimation(shapeId: 7));

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);

        var reloaded = PptxPackageReader.Read(new MemoryStream(stream.ToArray()));
        var animation = reloaded.Slides[0].Animations.Single();
        animation.Preset.Should().Be(AnimationPreset.ChangeColor);
        animation.RawPresetClass.Should().Be("emph");
        animation.RawPresetId.Should().Be(3);
        animation.RawPresetSubtype.Should().Be("0");
        animation.PreservedColorBehaviorXml.Should().Contain("style.color");
        animation.PreservedColorBehaviorXml.Should().Contain("accent2");
        animation.PreservedColorBehaviorXml.Should().Contain("spid=\"7\"");

        using var archive = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("ppt/slides/slide1.xml")!.Open());
        var slideXml = XDocument.Parse(reader.ReadToEnd());
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        var cTn = slideXml.Descendants(p + "cTn")
            .Single(element => element.Attribute("presetClass")?.Value == "emph"
                && element.Attribute("presetID")?.Value == "3");
        cTn.Descendants(p + "animClr")
            .Single()
            .Descendants(p + "attrName")
            .Single()
            .Value.Should().Be("style.color");
    }

    [Fact]
    public void SpinEffectSubtypeSurvivesReadCloneAndWrite()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape { Id = 7, Kind = SlideShapeKind.AutoShape });
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = 7,
            Kind = AnimationKind.Emphasis,
            Preset = AnimationPreset.Spin,
            EffectSubtype = "twoSpins",
        });

        using var first = new MemoryStream();
        PptxPackageWriter.Write(presentation, first);
        var reloaded = PptxPackageReader.Read(new MemoryStream(first.ToArray()));
        var animation = reloaded.Slides[0].Animations.Single();
        animation.Preset.Should().Be(AnimationPreset.Spin);
        animation.EffectSubtype.Should().Be("twoSpins");
        animation.Direction.Should().BeNull();

        var clonedAnimation = SlideCloner.CloneSlide(reloaded.Slides[0]).Animations.Single();
        clonedAnimation.EffectSubtype.Should().Be("twoSpins");

        using var second = new MemoryStream();
        PptxPackageWriter.Write(reloaded, second);
        using var archive = new ZipArchive(new MemoryStream(second.ToArray()), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("ppt/slides/slide1.xml")!.Open());
        var slideXml = XDocument.Parse(reader.ReadToEnd());
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        var cTn = slideXml.Descendants(p + "cTn")
            .Single(element => element.Attribute("presetID")?.Value == "8");
        cTn.Attribute("presetClass")!.Value.Should().Be("emph");
        cTn.Attribute("presetSubtype")!.Value.Should().Be("twoSpins");
    }

    [Fact]
    public void KnownNonDirectionalEffectSubtypeSurvivesReadCloneAndWrite()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape { Id = 7, Kind = SlideShapeKind.AutoShape });
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = 7,
            Kind = AnimationKind.Emphasis,
            Preset = AnimationPreset.Pulse,
            EffectSubtype = "authoredPulseVariant",
        });

        using var first = new MemoryStream();
        PptxPackageWriter.Write(presentation, first);
        var reloaded = PptxPackageReader.Read(new MemoryStream(first.ToArray()));
        var animation = reloaded.Slides[0].Animations.Single();
        animation.EffectSubtype.Should().Be("authoredPulseVariant");

        var clonedSlide = SlideCloner.CloneSlide(reloaded.Slides[0]);
        clonedSlide.Animations.Single().EffectSubtype.Should().Be("authoredPulseVariant");

        using var second = new MemoryStream();
        reloaded.Slides[0] = clonedSlide;
        PptxPackageWriter.Write(reloaded, second);
        using var archive = new ZipArchive(new MemoryStream(second.ToArray()), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("ppt/slides/slide1.xml")!.Open());
        var slideXml = XDocument.Parse(reader.ReadToEnd());
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        var cTn = slideXml.Descendants(p + "cTn")
            .Single(element => element.Attribute("presetClass")?.Value == "emph"
                && element.Attribute("presetID")?.Value == "14");
        cTn.Attribute("presetSubtype")!.Value.Should().Be("authoredPulseVariant");
    }

    [Theory]
    [InlineData(AnimationPreset.ChangeColor, 7)]
    [InlineData(AnimationPreset.GrowWithColor, 12)]
    [InlineData(AnimationPreset.Shimmer, 36)]
    public void ColorEffectBehaviorSurvivesReadCloneAndWrite(AnimationPreset preset, int expectedPresetId)
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape { Id = 7, Kind = SlideShapeKind.AutoShape });
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = 7,
            Kind = AnimationKind.Emphasis,
            Preset = preset,
            PreservedColorBehaviorXml = """
                <p:animClr xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" clrSpc="rgb">
                  <p:cBhvr><p:cTn id="77" dur="500" fill="hold"/><p:tgtEl><p:spTgt spid="7"/></p:tgtEl></p:cBhvr>
                  <p:clrFrom><a:srgbClr xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" val="FF0000"/></p:clrFrom>
                  <p:clrTo><a:srgbClr xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" val="0000FF"/></p:clrTo>
                </p:animClr>
                """,
        });

        using var first = new MemoryStream();
        PptxPackageWriter.Write(presentation, first);
        var reloaded = PptxPackageReader.Read(new MemoryStream(first.ToArray()));
        var animation = reloaded.Slides[0].Animations.Single();
        animation.Preset.Should().Be(preset);
        animation.PreservedColorBehaviorXml.Should().Contain("clrFrom");
        animation.PreservedColorBehaviorXml.Should().Contain("FF0000");

        var clonedAnimation = SlideCloner.CloneSlide(reloaded.Slides[0]).Animations.Single();
        clonedAnimation.PreservedColorBehaviorXml.Should().Be(animation.PreservedColorBehaviorXml);

        using var second = new MemoryStream();
        PptxPackageWriter.Write(reloaded, second);
        using var archive = new ZipArchive(new MemoryStream(second.ToArray()), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("ppt/slides/slide1.xml")!.Open());
        var slideXml = XDocument.Parse(reader.ReadToEnd());
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        var colorBehavior = slideXml.Descendants(p + "animClr").Single();
        slideXml.Descendants(p + "cTn").Single(element => element.Attribute("presetClass")?.Value == "emph")
            .Attribute("presetID")!.Value.Should().Be(expectedPresetId.ToString());
        colorBehavior.Element(p + "clrFrom")!.Element(XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main") + "srgbClr")!
            .Attribute("val")!.Value.Should().Be("FF0000");
        colorBehavior.Descendants(p + "cTn").Single().Attribute("id")!.Value.Should().NotBe("77");
    }

    [Theory]
    [InlineData(AnimationPreset.Shrink, 0.25)]
    [InlineData(AnimationPreset.Shrink, 0.5)]
    [InlineData(AnimationPreset.Grow, 1.5)]
    [InlineData(AnimationPreset.Grow, 4.0)]
    public void GrowShrinkAnimScaleSurvivesReadCloneAndWrite(
        AnimationPreset expectedPreset,
        double scale)
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape { Id = 7, Kind = SlideShapeKind.AutoShape });
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = 7,
            Kind = AnimationKind.Emphasis,
            Preset = expectedPreset,
            EffectSubtype = "legacy-subtype-is-not-amount",
            ScaleBehavior = AnimationScaleBehavior.FromTo(scale),
        });

        using var first = new MemoryStream();
        PptxPackageWriter.Write(presentation, first);
        var reloaded = PptxPackageReader.Read(new MemoryStream(first.ToArray()));
        var animation = reloaded.Slides[0].Animations.Single();
        animation.Preset.Should().Be(expectedPreset);
        animation.EffectSubtype.Should().BeNull();
        animation.ScaleBehavior!.FromX.Should().Be("100000");
        animation.ScaleBehavior.ToX.Should().Be(AnimationScaleBehavior.Format(scale));
        SlideCloner.CloneSlide(reloaded.Slides[0]).Animations.Single().ScaleBehavior!.ToX
            .Should().Be(AnimationScaleBehavior.Format(scale));

        using var second = new MemoryStream();
        PptxPackageWriter.Write(reloaded, second);
        using var archive = new ZipArchive(new MemoryStream(second.ToArray()), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("ppt/slides/slide1.xml")!.Open());
        var slideXml = XDocument.Parse(reader.ReadToEnd());
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        var cTn = slideXml.Descendants(p + "cTn")
            .Single(element => element.Attribute("presetClass")?.Value == "emph"
                && element.Attribute("presetID")?.Value == "5");
        cTn.Attribute("presetSubtype")!.Value.Should().Be("0");
        var animScale = cTn.Descendants(p + "animScale").Single();
        animScale.Descendants(p + "attrName").Select(element => element.Attribute("val")!.Value)
            .Should().Equal("ScaleX", "ScaleY");
        animScale.Element(p + "from")!.Attribute("x")!.Value.Should().Be("100000");
        animScale.Element(p + "to")!.Attribute("x")!.Value.Should().Be(AnimationScaleBehavior.Format(scale));
    }

    [Fact]
    public void GrowShrinkByOnlyCustomScaleSurvivesReadWriteWithoutSubtypeInference()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape { Id = 7, Kind = SlideShapeKind.AutoShape });
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = 7,
            Kind = AnimationKind.Emphasis,
            Preset = AnimationPreset.Grow,
            EffectSubtype = "150",
            ScaleBehavior = new AnimationScaleBehavior { ByX = "35000", ByY = "35000" },
        });

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        var reloaded = PptxPackageReader.Read(new MemoryStream(stream.ToArray()));
        var animation = reloaded.Slides[0].Animations.Single();
        animation.Preset.Should().Be(AnimationPreset.Grow);
        animation.EffectSubtype.Should().BeNull();
        animation.ScaleBehavior!.ByX.Should().Be("35000");
        AnimationAmountSemantics.ResolveScale(animation.Preset, animation.ScaleBehavior).Should().Be(1.35);
    }

    [Fact]
    public void GrowWithColorScaleSurvivesReadWriteAsAnAuthoredAmount()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape { Id = 7, Kind = SlideShapeKind.AutoShape });
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = 7,
            Kind = AnimationKind.Emphasis,
            Preset = AnimationPreset.GrowWithColor,
            ScaleBehavior = AnimationScaleBehavior.FromTo(1.5),
        });

        using var first = new MemoryStream();
        PptxPackageWriter.Write(presentation, first);
        var reloaded = PptxPackageReader.Read(new MemoryStream(first.ToArray()));
        var animation = reloaded.Slides[0].Animations.Single();

        animation.Preset.Should().Be(AnimationPreset.GrowWithColor);
        animation.Direction.Should().BeNull();
        animation.EffectSubtype.Should().BeNull();
        animation.ScaleBehavior!.ToX.Should().Be(AnimationScaleBehavior.Format(1.5));
        AnimationAmountSemantics.ResolveScale(animation.Preset, animation.ScaleBehavior).Should().Be(1.5);

        using var archive = new ZipArchive(new MemoryStream(first.ToArray()), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("ppt/slides/slide1.xml")!.Open());
        var slideXml = XDocument.Parse(reader.ReadToEnd());
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        var cTn = slideXml.Descendants(p + "cTn")
            .Single(element => element.Attribute("presetClass")?.Value == "emph"
                && element.Attribute("presetID")?.Value == "12");
        cTn.Attribute("presetSubtype")!.Value.Should().Be("0");
        cTn.Descendants(p + "animScale").Single()
            .Element(p + "to")!.Attribute("x")!.Value
            .Should().Be(AnimationScaleBehavior.Format(1.5));
    }

    [Fact]
    public void PulseScaleSurvivesReadWriteAsAnAuthoredAmount()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape { Id = 8, Kind = SlideShapeKind.AutoShape });
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = 8,
            Kind = AnimationKind.Emphasis,
            Preset = AnimationPreset.Pulse,
            ScaleBehavior = AnimationScaleBehavior.FromTo(1.5),
        });

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        var reloaded = PptxPackageReader.Read(new MemoryStream(stream.ToArray()));
        var animation = reloaded.Slides[0].Animations.Single();

        animation.Preset.Should().Be(AnimationPreset.Pulse);
        animation.Direction.Should().BeNull();
        animation.EffectSubtype.Should().BeNull();
        animation.ScaleBehavior!.ToX.Should().Be(AnimationScaleBehavior.Format(1.5));
        AnimationAmountSemantics.ResolveScale(animation.Preset, animation.ScaleBehavior).Should().Be(1.5);
    }

    [Theory]
    [InlineData("100000", "150000", null, 1.5)] // from_to
    [InlineData("80000", null, "30000", 1.1)]   // from_by
    [InlineData(null, "50000", null, 0.5)]      // to_only
    [InlineData(null, null, "35000", 1.35)]     // by_only
    public void GrowShrinkScaleResolution_CoversOfficeValidValueCombinations(
        string? from,
        string? to,
        string? by,
        double expected)
    {
        var behavior = new AnimationScaleBehavior
        {
            FromX = from,
            FromY = from,
            ToX = to,
            ToY = to,
            ByX = by,
            ByY = by,
        };

        AnimationAmountSemantics.ResolveScaleAxes(AnimationPreset.Grow, behavior)
            .Should().Be((expected, expected));
    }

    [Fact]
    public void GrowShrinkUnknownScaleTokensAreRetainedAsOpaqueAnimScaleValues()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape { Id = 7, Kind = SlideShapeKind.AutoShape });
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = 7,
            Kind = AnimationKind.Emphasis,
            Preset = AnimationPreset.Grow,
            ScaleBehavior = new AnimationScaleBehavior
            {
                FromX = "100000",
                FromY = "100000",
                ToX = "office-custom",
                ToY = "office-custom",
            },
        });

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        var reloaded = PptxPackageReader.Read(new MemoryStream(stream.ToArray()));
        var behavior = reloaded.Slides[0].Animations.Single().ScaleBehavior!;
        behavior.ToX.Should().Be("office-custom");
        behavior.ToY.Should().Be("office-custom");
    }

    [Fact]
    public void AsymmetricGrowShrinkAnimScaleSurvivesReadWriteWithBothAxes()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape { Id = 7, Kind = SlideShapeKind.AutoShape });
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = 7,
            Kind = AnimationKind.Emphasis,
            Preset = AnimationPreset.Grow,
            ScaleBehavior = new AnimationScaleBehavior
            {
                FromX = "100000",
                FromY = "100000",
                ToX = "150000",
                ToY = "75000",
                ZoomContents = true,
            },
        });

        using var first = new MemoryStream();
        PptxPackageWriter.Write(presentation, first);
        var reloaded = PptxPackageReader.Read(new MemoryStream(first.ToArray()));
        var behavior = reloaded.Slides[0].Animations.Single().ScaleBehavior!;
        behavior.FromX.Should().Be("100000");
        behavior.FromY.Should().Be("100000");
        behavior.ToX.Should().Be("150000");
        behavior.ToY.Should().Be("75000");
        behavior.ZoomContents.Should().BeTrue();

        using var second = new MemoryStream();
        PptxPackageWriter.Write(reloaded, second);
        using var archive = new ZipArchive(new MemoryStream(second.ToArray()), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("ppt/slides/slide1.xml")!.Open());
        var slideXml = XDocument.Parse(reader.ReadToEnd());
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        var animScale = slideXml.Descendants(p + "animScale").Single();
        animScale.Attribute("zoomContents")!.Value.Should().Be("1");
        animScale.Element(p + "from")!.Attribute("x")!.Value.Should().Be("100000");
        animScale.Element(p + "from")!.Attribute("y")!.Value.Should().Be("100000");
        animScale.Element(p + "to")!.Attribute("x")!.Value.Should().Be("150000");
        animScale.Element(p + "to")!.Attribute("y")!.Value.Should().Be("75000");
    }

    [Theory]
    [InlineData(AnimationDirection.HorizontalOut, "0")]
    [InlineData(AnimationDirection.HorizontalIn, "1")]
    [InlineData(AnimationDirection.VerticalOut, "2")]
    [InlineData(AnimationDirection.VerticalIn, "3")]
    [InlineData(AnimationDirection.Horizontal, "horizontal")]
    [InlineData(AnimationDirection.Vertical, "vertical")]
    public void SplitDirectionSurvivesPptxReadWriteRoundTrip(
        AnimationDirection direction,
        string expectedSubtype)
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 914400,
        });
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = 7,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Split,
            Direction = direction,
        });

        using var first = new MemoryStream();
        PptxPackageWriter.Write(presentation, first);
        var reloaded = PptxPackageReader.Read(new MemoryStream(first.ToArray()));
        reloaded.Slides[0].Animations.Single().Direction.Should().Be(direction);

        using var second = new MemoryStream();
        PptxPackageWriter.Write(reloaded, second);
        using var archive = new ZipArchive(new MemoryStream(second.ToArray()), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("ppt/slides/slide1.xml")!.Open());
        var slideXml = XDocument.Parse(reader.ReadToEnd());
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        var cTn = slideXml.Descendants(p + "cTn")
            .Single(element => element.Attribute("presetClass")?.Value == "entr"
                && element.Attribute("presetID")?.Value == "3");
        cTn.Attribute("presetSubtype")!.Value.Should().Be(expectedSubtype);
    }

    [Theory]
    [InlineData(AnimationDirection.Horizontal, "horizontal")]
    [InlineData(AnimationDirection.Vertical, "vertical")]
    public void WaveDirectionSurvivesPptxReadWriteRoundTrip(
        AnimationDirection direction,
        string expectedSubtype)
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 914400,
        });
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = 7,
            Kind = AnimationKind.Emphasis,
            Preset = AnimationPreset.Wave,
            Direction = direction,
        });

        using var first = new MemoryStream();
        PptxPackageWriter.Write(presentation, first);
        var reloaded = PptxPackageReader.Read(new MemoryStream(first.ToArray()));
        reloaded.Slides[0].Animations.Single().Direction.Should().Be(direction);

        using var second = new MemoryStream();
        PptxPackageWriter.Write(reloaded, second);
        using var archive = new ZipArchive(new MemoryStream(second.ToArray()), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("ppt/slides/slide1.xml")!.Open());
        var slideXml = XDocument.Parse(reader.ReadToEnd());
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        var cTn = slideXml.Descendants(p + "cTn")
            .Single(element => element.Attribute("presetClass")?.Value == "emph"
                && element.Attribute("presetID")?.Value == "34");
        cTn.Attribute("presetSubtype")!.Value.Should().Be(expectedSubtype);
    }
}
