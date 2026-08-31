using System.Xml.Linq;
using FreeP.App.Compositor;
using PresentationModel = FreeP.Core.Model.Presentation;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Paragraph spacing authored in a master or layout <c>a:lstStyle</c> (<c>a:spcBef</c>,
/// <c>a:spcAft</c>, <c>a:lnSpc</c> on <c>a:lvl1pPr</c>..<c>a:lvl9pPr</c>) must be read,
/// inherited and written back. TextStyleLevel carried no spacing fields at all, so a deck that
/// authors spacing only in its master — which is what PowerPoint's stock body placeholder does —
/// rendered with its paragraphs jammed together.
/// </summary>
public sealed class ListStyleParagraphSpacingTests
{
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";

    private static Paragraph MakeParagraph(string text, double fontSizePt, Action<Paragraph>? configure = null)
    {
        var para = new Paragraph();
        para.Runs.Add(new Run { Text = text, FontSizePt = fontSizePt });
        configure?.Invoke(para);
        return para;
    }

    /// <summary>
    /// Builds a presentation whose master body style authors spacing at level 1 and whose slide
    /// has a Body placeholder with a single paragraph that authors none of its own.
    /// </summary>
    private static ResolvedParagraph ResolveWithMasterStyle(
        Action<TextStyleLevel> configureMasterLevel,
        Action<Paragraph>? configureParagraph = null,
        Action<TextStyleLevel>? configureLayoutLevel = null)
    {
        var p = new PresentationModel { Theme = PresentationTheme.CreateDefault() };

        var masterLevel = new TextStyleLevel();
        configureMasterLevel(masterLevel);
        var master = new SlideMaster { Id = "m1", TextStyles = new MasterTextStyles() };
        master.TextStyles.BodyStyle[0] = masterLevel;
        p.Masters.Add(master);

        var layout = new SlideLayout { Id = "l1", MasterId = "m1" };
        if (configureLayoutLevel is not null)
        {
            var layoutLevel = new TextStyleLevel();
            configureLayoutLevel(layoutLevel);
            var layoutBody = new TextBody { LstStyle = new TextStyleLevels() };
            layoutBody.LstStyle![0] = layoutLevel;
            layout.Placeholders.Add(new SlideShape
            {
                Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
                TextBody = layoutBody
            });
        }
        p.Layouts.Add(layout);

        var body = new TextBody();
        body.Paragraphs.Add(MakeParagraph("Body text", 20, configureParagraph));
        var slide = new Slide { LayoutId = "l1" };
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            Placeholder = new Placeholder { Type = PlaceholderType.Body, Idx = 1 },
            OffsetXEmu = 457200,
            OffsetYEmu = 274320,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 1371600,
            TextBody = body
        });
        p.Slides.Add(slide);

        return SlideCompositor.Compose(p, slide)
            .OfType<DrawOp.Shape>().Single().Text!.Paragraphs[0];
    }

    // ─── Reader ───────────────────────────────────────────────────────────────

    [Fact]
    public void Read_MasterBodyStyleSpacing_PopulatesTextStyleLevel()
    {
        // A master body style shaped like PowerPoint's own: percent space-before, exact
        // space-after, percent line spacing.
        var p = PresentationModel.CreateEmpty();
        var masterLevel = new TextStyleLevel
        {
            SpaceBeforePercent = 20,
            SpaceAfterPt = 6,
            LineSpacingPercent = 90
        };
        p.Masters[0].TextStyles ??= new MasterTextStyles();
        p.Masters[0].TextStyles!.BodyStyle[0] = masterLevel;

        using var stream = new MemoryStream();
        FreeP.Core.IO.PptxPackageWriter.Write(p, stream);
        stream.Position = 0;
        var reloaded = FreeP.Core.IO.PptxPackageReader.Read(stream);

        var level = reloaded.Masters[0].TextStyles!.BodyStyle[0];
        level.Should().NotBeNull();
        level!.SpaceBeforePercent.Should().Be(20);
        level.SpaceBeforePt.Should().BeNull("spcPct and spcPts are mutually exclusive");
        level.SpaceAfterPt.Should().Be(6);
        level.SpaceAfterPercent.Should().BeNull();
        level.LineSpacingPercent.Should().Be(90);
        level.LineSpacingPointsExact.Should().BeNull();
    }

    [Fact]
    public void Write_LevelSpacing_EmitsSchemaChildOrderBeforeBullets()
    {
        // CT_TextParagraphProperties order: lnSpc → spcBef → spcAft → bullet group → defRPr.
        var p = PresentationModel.CreateEmpty();
        p.Masters[0].TextStyles ??= new MasterTextStyles();
        p.Masters[0].TextStyles!.BodyStyle[0] = new TextStyleLevel
        {
            LineSpacingPercent = 90,
            SpaceBeforePercent = 20,
            SpaceAfterPt = 6,
            BulletKind = BulletKind.Char,
            BulletChar = "•",
            FontSizePt = 18
        };

        using var stream = new MemoryStream();
        FreeP.Core.IO.PptxPackageWriter.Write(p, stream);

        using var zip = new System.IO.Compression.ZipArchive(
            new MemoryStream(stream.ToArray()), System.IO.Compression.ZipArchiveMode.Read);
        var masterEntry = zip.Entries.Single(entry =>
            entry.FullName.StartsWith("ppt/slideMasters/slideMaster", StringComparison.OrdinalIgnoreCase) &&
            entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        using var entryStream = masterEntry.Open();
        var lvl1 = XDocument.Load(entryStream).Descendants(A + "lvl1pPr").First();

        var childNames = lvl1.Elements().Select(element => element.Name.LocalName).ToList();
        childNames.Should().ContainInOrder("lnSpc", "spcBef", "spcAft", "buChar", "defRPr");
        lvl1.Element(A + "spcBef")!.Element(A + "spcPct")!.Attribute("val")!.Value.Should().Be("20000");
        lvl1.Element(A + "spcAft")!.Element(A + "spcPts")!.Attribute("val")!.Value.Should().Be("600");
        lvl1.Element(A + "lnSpc")!.Element(A + "spcPct")!.Attribute("val")!.Value.Should().Be("90000");
    }

    // ─── Compositor inheritance ───────────────────────────────────────────────

    [Fact]
    public void Compose_MasterLevelSpacing_InheritedByParagraphWithNone()
    {
        var resolved = ResolveWithMasterStyle(level =>
        {
            level.SpaceBeforePercent = 20;
            level.SpaceAfterPt = 6;
            level.LineSpacingPercent = 90;
        });

        resolved.SpaceBeforePercent.Should().Be(20,
            "a paragraph that authors no spcBef inherits the master body style's");
        resolved.SpaceAfterPt.Should().Be(6);
        resolved.LineSpacingPercent.Should().Be(90);
        TextLayoutPlanner.ResolveSpaceBeforePoints(resolved).Should().BeApproximately(
            0.20 * 20 * ParagraphSpacingMetrics.LineHeightFactor, 1e-9);
    }

    [Fact]
    public void Compose_ParagraphSpacing_OverridesInheritedLevel()
    {
        var resolved = ResolveWithMasterStyle(
            level => level.SpaceBeforePercent = 20,
            para => para.SpaceBeforePt = 14);

        resolved.SpaceBeforePt.Should().Be(14, "an explicit paragraph spcBef wins over the master");
        resolved.SpaceBeforePercent.Should().BeNull(
            "spcPts and spcPct are mutually exclusive children of one a:spcBef — a paragraph " +
            "points value must not be combined with an inherited percent");
        TextLayoutPlanner.ResolveSpaceBeforePoints(resolved).Should().Be(14);
    }

    [Fact]
    public void Compose_LayoutLevelSpacing_WinsOverMaster()
    {
        var resolved = ResolveWithMasterStyle(
            level => level.SpaceBeforePercent = 20,
            configureParagraph: null,
            configureLayoutLevel: level => level.SpaceBeforePercent = 200);

        resolved.SpaceBeforePercent.Should().Be(200,
            "the layout lstStyle is more specific than the master txStyles");
    }

    [Fact]
    public void Compose_ParagraphWithoutSpacing_StaysZeroWhenNoLevelAuthorsIt()
    {
        var resolved = ResolveWithMasterStyle(level => level.FontSizePt = 20);

        resolved.SpaceBeforePt.Should().Be(0);
        resolved.SpaceBeforePercent.Should().BeNull();
        resolved.SpaceAfterPt.Should().Be(0);
        resolved.SpaceAfterPercent.Should().BeNull();
        TextLayoutPlanner.ResolveSpaceBeforePoints(resolved).Should().Be(0);
    }

    // ─── Layer merge ──────────────────────────────────────────────────────────

    [Fact]
    public void MergeTextStyleLevels_ResolvesEachSpacingElementFromOneLayer()
    {
        // The layout authors spcBef as points; the master authors it as a percent. Taking the
        // points from one layer and the percent from the other would apply both.
        var merged = SlideCompositor.MergeTextStyleLevels(
            shape: null,
            layout: new TextStyleLevel { SpaceBeforePt = 10 },
            master: new TextStyleLevel { SpaceBeforePercent = 20, SpaceAfterPercent = 35 });

        merged.SpaceBeforePt.Should().Be(10);
        merged.SpaceBeforePercent.Should().BeNull(
            "a:spcBef resolves as a unit from the most specific layer that authored it");
        merged.SpaceAfterPercent.Should().Be(35,
            "a:spcAft is a separate element and still falls through to the master");
    }

    [Fact]
    public void MergeTextStyleLevels_LineSpacingResolvesAsAUnit()
    {
        var merged = SlideCompositor.MergeTextStyleLevels(
            shape: new TextStyleLevel { LineSpacingPointsExact = 24 },
            layout: new TextStyleLevel { LineSpacingPercent = 150 },
            master: null);

        merged.LineSpacingPointsExact.Should().Be(24);
        merged.LineSpacingPercent.Should().BeNull();
    }
}
