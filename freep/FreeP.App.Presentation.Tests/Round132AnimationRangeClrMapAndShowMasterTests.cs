using System.IO.Compression;
using System.Xml.Linq;
using FreeP.App.Compositor;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Round 132: covers three FreeP reader/writer/compositor gaps found in the same review pass —
/// (a) per-paragraph animation targets (p:tgtEl/p:spTgt/p:txEl/p:pRg), (b) layout-level
/// p:clrMapOvr, and (c) per-slide p:sld/@showMasterSp + @showMasterPhAnim.
/// </summary>
public sealed class Round132AnimationRangeClrMapAndShowMasterTests
{
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";

    private static SlideShape MakeThreeParagraphTextShape(uint id) => new()
    {
        Id = id,
        Kind = SlideShapeKind.AutoShape,
        AutoShapeKind = DrawingShapeKind.Rectangle,
        OffsetXEmu = 914400,
        OffsetYEmu = 914400,
        ExtentCxEmu = 4572000,
        ExtentCyEmu = 2743200,
        TextBody = new TextBody
        {
            Paragraphs =
            {
                new Paragraph { Runs = { new Run { Text = "First" } } },
                new Paragraph { Runs = { new Run { Text = "Second" } } },
                new Paragraph { Runs = { new Run { Text = "Third" } } },
            }
        }
    };

    // ── (a) Per-paragraph animation targets ─────────────────────────────────────────────────

    [Fact]
    public void WriterEmitsTxElPRgForParagraphScopedAnimation()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(MakeThreeParagraphTextShape(7));
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = 7,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Fade,
            ParagraphRangeStart = 1,
            ParagraphRangeEnd = 2,
        });

        using var output = new MemoryStream();
        PptxPackageWriter.Write(presentation, output);

        using var archive = new ZipArchive(new MemoryStream(output.ToArray()), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("ppt/slides/slide1.xml")!.Open());
        var slideXml = XDocument.Parse(reader.ReadToEnd());

        var pRg = slideXml.Descendants(P + "spTgt")
            .Where(spTgt => spTgt.Attribute("spid")?.Value == "7")
            .Select(spTgt => spTgt.Element(P + "txEl")?.Element(P + "pRg"))
            .SingleOrDefault(element => element is not null);

        pRg.Should().NotBeNull("a paragraph-scoped animation must emit p:txEl/p:pRg under its p:spTgt");
        pRg!.Attribute("st")!.Value.Should().Be("1");
        pRg.Attribute("end")!.Value.Should().Be("2");
    }

    [Fact]
    public void ReaderAndWriterRoundTripPreservesDistinctPerParagraphAnimationRanges()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(MakeThreeParagraphTextShape(9));
        // Mirrors PowerPoint's "By 1st Level Paragraphs" entrance: one build item per paragraph,
        // each targeting its own paragraph range instead of the whole shape.
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = 9,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Fade,
            Trigger = AnimationTrigger.OnClick,
            ParagraphRangeStart = 0,
            ParagraphRangeEnd = 0,
        });
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = 9,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Fade,
            Trigger = AnimationTrigger.AfterPrevious,
            ParagraphRangeStart = 1,
            ParagraphRangeEnd = 1,
        });
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = 9,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Fade,
            Trigger = AnimationTrigger.AfterPrevious,
            ParagraphRangeStart = 2,
            ParagraphRangeEnd = 2,
        });

        using var output = new MemoryStream();
        PptxPackageWriter.Write(presentation, output);
        var reloaded = PptxPackageReader.Read(new MemoryStream(output.ToArray()));

        var animations = reloaded.Slides[0].Animations
            .Where(a => a.ShapeId == 9)
            .ToList();

        animations.Should().HaveCount(3, "each authored paragraph build item must survive as its own entry, not be collapsed");

        // The bug this guards against: before the fix, every entry read back with
        // ParagraphRangeStart == null, making all three entries indistinguishable
        // whole-shape animations instead of three distinct per-paragraph reveals.
        animations.Select(a => (a.ParagraphRangeStart, a.ParagraphRangeEnd))
            .Should().Equal(new (int?, int?)[] { (0, 0), (1, 1), (2, 2) });

        // Round-trip again to prove the reloaded model still writes back correctly.
        using var second = new MemoryStream();
        PptxPackageWriter.Write(reloaded, second);
        var reloadedAgain = PptxPackageReader.Read(new MemoryStream(second.ToArray()));
        reloadedAgain.Slides[0].Animations
            .Where(a => a.ShapeId == 9)
            .Select(a => (a.ParagraphRangeStart, a.ParagraphRangeEnd))
            .Should().Equal(new (int?, int?)[] { (0, 0), (1, 1), (2, 2) });
    }

    [Fact]
    public void WholeShapeAnimationRoundTripsWithoutParagraphRangeOrPRgElement()
    {
        // Sibling/no-regression: an ordinary whole-shape animation (the overwhelming common
        // case) must not gain a spurious p:pRg, and must reload with a null paragraph range.
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(MakeThreeParagraphTextShape(11));
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = 11,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.FlyIn,
        });

        using var output = new MemoryStream();
        PptxPackageWriter.Write(presentation, output);

        using (var archive = new ZipArchive(new MemoryStream(output.ToArray()), ZipArchiveMode.Read))
        using (var reader = new StreamReader(archive.GetEntry("ppt/slides/slide1.xml")!.Open()))
        {
            var slideXml = XDocument.Parse(reader.ReadToEnd());
            slideXml.Descendants(P + "pRg").Should().BeEmpty("a whole-shape animation must not emit p:txEl/p:pRg");
        }

        var reloaded = PptxPackageReader.Read(new MemoryStream(output.ToArray()));
        var animation = reloaded.Slides[0].Animations.Single(a => a.ShapeId == 11);
        animation.ParagraphRangeStart.Should().BeNull();
        animation.ParagraphRangeEnd.Should().BeNull();
    }

    // ── (b) Layout-level p:clrMapOvr ─────────────────────────────────────────────────────────

    private static Dictionary<string, string> InvertedClrMap() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["bg1"] = "dk1", ["tx1"] = "lt1", ["bg2"] = "dk2", ["tx2"] = "lt2",
        ["accent1"] = "accent1", ["accent2"] = "accent2", ["accent3"] = "accent3",
        ["accent4"] = "accent4", ["accent5"] = "accent5", ["accent6"] = "accent6",
        ["hlink"] = "hlink", ["folHlink"] = "folHlink",
    };

    [Fact]
    public void LayoutColorMapOverrideRoundTripsThroughReaderAndWriter()
    {
        var presentation = Presentation.CreateEmpty();
        var layout = presentation.Layouts.Single();
        layout.ColorMapOverride = InvertedClrMap();

        using var output = new MemoryStream();
        PptxPackageWriter.Write(presentation, output);

        using (var archive = new ZipArchive(new MemoryStream(output.ToArray()), ZipArchiveMode.Read))
        {
            var layoutEntry = archive.Entries.Single(e => e.FullName.StartsWith("ppt/slideLayouts/slideLayout", StringComparison.Ordinal)
                && e.FullName.EndsWith(".xml", StringComparison.Ordinal));
            using var reader = new StreamReader(layoutEntry.Open());
            var layoutXml = XDocument.Parse(reader.ReadToEnd());
            var overrideEl = layoutXml.Root!.Element(P + "clrMapOvr")?.Element(A + "overrideClrMapping");
            overrideEl.Should().NotBeNull("a layout with an explicit ColorMapOverride must emit a:overrideClrMapping, not a:masterClrMapping");
            overrideEl!.Attribute("tx1")!.Value.Should().Be("lt1");
            overrideEl.Attribute("bg1")!.Value.Should().Be("dk1");
        }

        var reloaded = PptxPackageReader.Read(new MemoryStream(output.ToArray()));
        var reloadedLayout = reloaded.Layouts.Single();
        reloadedLayout.ColorMapOverride.Should().NotBeNull();
        reloadedLayout.ColorMapOverride!["tx1"].Should().Be("lt1");
        reloadedLayout.ColorMapOverride["bg1"].Should().Be("dk1");
    }

    [Fact]
    public void LayoutWithoutColorMapOverrideRoundTripsAsMasterClrMapping()
    {
        // Sibling/no-regression: the default (no override) layout must still round-trip to
        // null, i.e. <a:masterClrMapping/>, unaffected by the new ColorMapOverride plumbing.
        var presentation = Presentation.CreateEmpty();
        presentation.Layouts.Single().ColorMapOverride.Should().BeNull();

        using var output = new MemoryStream();
        PptxPackageWriter.Write(presentation, output);

        using (var archive = new ZipArchive(new MemoryStream(output.ToArray()), ZipArchiveMode.Read))
        {
            var layoutEntry = archive.Entries.Single(e => e.FullName.StartsWith("ppt/slideLayouts/slideLayout", StringComparison.Ordinal)
                && e.FullName.EndsWith(".xml", StringComparison.Ordinal));
            using var reader = new StreamReader(layoutEntry.Open());
            var layoutXml = XDocument.Parse(reader.ReadToEnd());
            layoutXml.Root!.Element(P + "clrMapOvr")?.Element(A + "masterClrMapping").Should().NotBeNull();
        }

        var reloaded = PptxPackageReader.Read(new MemoryStream(output.ToArray()));
        reloaded.Layouts.Single().ColorMapOverride.Should().BeNull();
    }

    [Fact]
    public void Compose_LayoutColorMapOverride_OverridesMasterClrMapForShapeFill()
    {
        var p = new Presentation();
        p.Theme = PresentationTheme.CreateDefault(); // Lt1=#FFFFFF, Dk1=#000000

        var master = new SlideMaster { Id = "m1" };
        master.ColorMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["bg1"] = "lt1", ["tx1"] = "dk1", ["bg2"] = "lt2", ["tx2"] = "dk2",
            ["accent1"] = "accent1", ["accent2"] = "accent2", ["accent3"] = "accent3",
            ["accent4"] = "accent4", ["accent5"] = "accent5", ["accent6"] = "accent6",
            ["hlink"] = "hlink", ["folHlink"] = "folHlink",
        };
        p.Masters.Add(master);

        // Shape fill references tx1 (parsed from XML with RoleName set).
        var shapeFill = new ShapeFill.Solid(new ThemeAwareColor(
            SrgbColor.Black,
            new SchemeColorRef { RoleName = "tx1", Slot = ThemeColorSlot.Dk1, LumMod = 1.0 }));

        var layoutPlain = new SlideLayout { Id = "lPlain", MasterId = "m1" };
        p.Layouts.Add(layoutPlain);
        var slidePlain = new Slide { LayoutId = "lPlain" };
        slidePlain.Shapes.Add(new SlideShape
        {
            Id = 1, OffsetXEmu = 0, OffsetYEmu = 0, ExtentCxEmu = 914400, ExtentCyEmu = 457200,
            Fill = shapeFill,
        });
        p.Slides.Add(slidePlain);

        var opsPlain = SlideCompositor.Compose(p, slidePlain);
        ((ResolvedFill.Solid)opsPlain.OfType<DrawOp.Shape>().Single().Fill).Color
            .Should().Be(new SrgbColor(0, 0, 0), "no layout override: tx1 via master map -> Dk1 = black");

        // Layout carries an inverted override; slide itself has none.
        var layoutOvr = new SlideLayout { Id = "lOvr", MasterId = "m1", ColorMapOverride = InvertedClrMap() };
        p.Layouts.Add(layoutOvr);
        var slideOvr = new Slide { LayoutId = "lOvr" };
        slideOvr.Shapes.Add(new SlideShape
        {
            Id = 1, OffsetXEmu = 0, OffsetYEmu = 0, ExtentCxEmu = 914400, ExtentCyEmu = 457200,
            Fill = shapeFill,
        });
        p.Slides.Add(slideOvr);

        var opsOvr = SlideCompositor.Compose(p, slideOvr);
        ((ResolvedFill.Solid)opsOvr.OfType<DrawOp.Shape>().Single().Fill).Color
            .Should().Be(new SrgbColor(0xFF, 0xFF, 0xFF),
            "layout override (inverted: tx1->lt1) must win over the master map -> Lt1 = white");
    }

    [Fact]
    public void Compose_SlideColorMapOverrideTakesPrecedenceOverLayoutColorMapOverride()
    {
        var p = new Presentation();
        p.Theme = PresentationTheme.CreateDefault();

        var master = new SlideMaster { Id = "m1" };
        master.ColorMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["bg1"] = "lt1", ["tx1"] = "dk1", ["bg2"] = "lt2", ["tx2"] = "dk2",
            ["accent1"] = "accent1", ["accent2"] = "accent2", ["accent3"] = "accent3",
            ["accent4"] = "accent4", ["accent5"] = "accent5", ["accent6"] = "accent6",
            ["hlink"] = "hlink", ["folHlink"] = "folHlink",
        };
        p.Masters.Add(master);

        // Layout says tx1 -> lt1 (would resolve white); slide overrides back to tx1 -> dk1 (black).
        var layout = new SlideLayout { Id = "l1", MasterId = "m1", ColorMapOverride = InvertedClrMap() };
        p.Layouts.Add(layout);

        var slide = new Slide
        {
            LayoutId = "l1",
            ColorMapOverride = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["bg1"] = "lt1", ["tx1"] = "dk1", ["bg2"] = "lt2", ["tx2"] = "dk2",
                ["accent1"] = "accent1", ["accent2"] = "accent2", ["accent3"] = "accent3",
                ["accent4"] = "accent4", ["accent5"] = "accent5", ["accent6"] = "accent6",
                ["hlink"] = "hlink", ["folHlink"] = "folHlink",
            },
        };
        slide.Shapes.Add(new SlideShape
        {
            Id = 1, OffsetXEmu = 0, OffsetYEmu = 0, ExtentCxEmu = 914400, ExtentCyEmu = 457200,
            Fill = new ShapeFill.Solid(new ThemeAwareColor(
                SrgbColor.Black,
                new SchemeColorRef { RoleName = "tx1", Slot = ThemeColorSlot.Dk1, LumMod = 1.0 })),
        });
        p.Slides.Add(slide);

        var ops = SlideCompositor.Compose(p, slide);
        ((ResolvedFill.Solid)ops.OfType<DrawOp.Shape>().Single().Fill).Color
            .Should().Be(new SrgbColor(0, 0, 0),
            "slide.ColorMapOverride must win over the layout's override -> Dk1 = black");
    }

    // ── (c) Per-slide showMasterSp / showMasterPhAnim ───────────────────────────────────────

    [Fact]
    public void SlideShowMasterShapesAndPhAnimRoundTripPerSlideIndependently()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].ShowMasterShapes = false;
        presentation.Slides[0].ShowMasterPhAnim = false;
        presentation.Slides.Add(new Slide { ShowMasterShapes = true, ShowMasterPhAnim = true });

        using var output = new MemoryStream();
        PptxPackageWriter.Write(presentation, output);

        using (var archive = new ZipArchive(new MemoryStream(output.ToArray()), ZipArchiveMode.Read))
        {
            using var reader1 = new StreamReader(archive.GetEntry("ppt/slides/slide1.xml")!.Open());
            var slide1Xml = XDocument.Parse(reader1.ReadToEnd());
            slide1Xml.Root!.Attribute("showMasterSp")!.Value.Should().Be("0");
            slide1Xml.Root!.Attribute("showMasterPhAnim")!.Value.Should().Be("0");

            using var reader2 = new StreamReader(archive.GetEntry("ppt/slides/slide2.xml")!.Open());
            var slide2Xml = XDocument.Parse(reader2.ReadToEnd());
            slide2Xml.Root!.Attribute("showMasterSp").Should().BeNull("default (true) must not be written");
            slide2Xml.Root!.Attribute("showMasterPhAnim").Should().BeNull("default (true) must not be written");
        }

        var reloaded = PptxPackageReader.Read(new MemoryStream(output.ToArray()));
        reloaded.Slides[0].ShowMasterShapes.Should().BeFalse();
        reloaded.Slides[0].ShowMasterPhAnim.Should().BeFalse();
        reloaded.Slides[1].ShowMasterShapes.Should().BeTrue("a later slide with the default must not inherit slide 1's override");
        reloaded.Slides[1].ShowMasterPhAnim.Should().BeTrue();
    }

    [Fact]
    public void Compose_HidesMasterDecorationOnlyOnSlideWithShowMasterShapesFalse()
    {
        // Presentation-level ShowMasterShapes (FreeP's Slide Show Settings toggle) stays at its
        // default (true): only the per-slide flag should decide visibility here.
        // Bare Presentation (not CreateEmpty()): CreateEmpty() pre-populates its own empty
        // master, and Compose()'s FirstOrDefault() fallback for a slide with no LayoutId would
        // silently resolve to THAT master instead of the one built below.
        var presentation = new Presentation();
        presentation.ShowMasterShapes.Should().BeTrue();

        var master = new SlideMaster { Id = "m1" };
        master.Placeholders.Add(new SlideShape
        {
            Id = 10,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 100, OffsetYEmu = 200, ExtentCxEmu = 1000, ExtentCyEmu = 600,
            Fill = new ShapeFill.Solid(new ThemeAwareColor(new SrgbColor(0x11, 0x22, 0x33))),
        });
        presentation.Masters.Add(master);

        var slideHidden = new Slide { ShowMasterShapes = false };
        presentation.Slides.Add(slideHidden);
        var slideShown = new Slide { ShowMasterShapes = true };
        presentation.Slides.Add(slideShown);

        SlideCompositor.Compose(presentation, slideHidden)
            .OfType<DrawOp.Shape>()
            .Should().BeEmpty("this slide authored showMasterSp=false: its master decoration must be hidden");

        SlideCompositor.Compose(presentation, slideShown)
            .OfType<DrawOp.Shape>()
            .Should().HaveCount(1, "a sibling slide with the default showMasterSp=true must still show the master decoration");
    }
}
