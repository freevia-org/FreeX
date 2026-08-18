using System.IO;
using Free.Shared.Drawing;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Round 144 regression tests (freep-run-ea-cs-typeface-lost / F1 freep-hyperlinks-actions):
///  - a:rPr/a:ea and a:rPr/a:cs typefaces on a run and a field-run must round-trip.
///  - Action-only a:hlinkClick (Next/Previous/First/Last Slide, End Show — no r:id) must
///    round-trip instead of being silently discarded on read.
/// </summary>
public sealed class Round144RunEaCsAndActionHyperlinkTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeP.Round144Tests-");
    private string _tempDir => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    private string WriteToPptx(Presentation p)
    {
        var path = Path.Combine(_tempDir, Guid.NewGuid().ToString("N") + ".pptx");
        PptxPackageWriter.Write(p, path);
        return path;
    }

    private static SlideShape MakeShape(uint id = 1, string name = "Shape1") => new()
    {
        Id            = id,
        Name          = name,
        Kind          = SlideShapeKind.AutoShape,
        AutoShapeKind = DrawingShapeKind.Rectangle,
        OffsetXEmu    = 914400,
        OffsetYEmu    = 914400,
        ExtentCxEmu   = 2000000,
        ExtentCyEmu   = 1000000,
    };

    // ─────────────────────────────────────────────────────────────────────────────
    // a:ea / a:cs run typefaces
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_RunEastAsianAndComplexScriptTypefaces_ArePreserved()
    {
        var p = new Presentation();
        var slide = new Slide();
        var shape = MakeShape();
        shape.TextBody = new TextBody();
        var para = new Paragraph();
        var run = new Run
        {
            Text = "CJK text",
            FontFamily = "Calibri",
            EastAsiaFontFamily = "MS Gothic",
            ComplexScriptFontFamily = "Arial",
        };
        para.Runs.Add(run);
        shape.TextBody.Paragraphs.Add(para);
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);

        var path = WriteToPptx(p);
        var reloaded = PptxPackageReader.Read(path);

        var rtRun = reloaded.Slides[0].Shapes.First(s => s.Name == "Shape1")
            .TextBody!.Paragraphs[0].Runs[0];

        // Sibling assertion: the pre-existing latin typeface must still round-trip.
        rtRun.FontFamily.Should().Be("Calibri", "the latin typeface must still round-trip as before");
        rtRun.EastAsiaFontFamily.Should().Be("MS Gothic",
            "the a:ea typeface must survive a FreeP save instead of falling back to the default East-Asian font");
        rtRun.ComplexScriptFontFamily.Should().Be("Arial",
            "the a:cs typeface must survive a FreeP save");
    }

    [Fact]
    public void RoundTrip_RunWithoutEastAsianOrComplexScriptTypeface_LeavesThemNull()
    {
        // Sibling test: a run that never had ea/cs typefaces must not gain them.
        var p = new Presentation();
        var slide = new Slide();
        var shape = MakeShape();
        shape.TextBody = new TextBody();
        var para = new Paragraph();
        var run = new Run { Text = "Latin only", FontFamily = "Calibri" };
        para.Runs.Add(run);
        shape.TextBody.Paragraphs.Add(para);
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);

        var path = WriteToPptx(p);
        var reloaded = PptxPackageReader.Read(path);

        var rtRun = reloaded.Slides[0].Shapes.First(s => s.Name == "Shape1")
            .TextBody!.Paragraphs[0].Runs[0];

        rtRun.FontFamily.Should().Be("Calibri");
        rtRun.EastAsiaFontFamily.Should().BeNull("no a:ea was authored, so none should be invented");
        rtRun.ComplexScriptFontFamily.Should().BeNull("no a:cs was authored, so none should be invented");
    }

    [Fact]
    public void RoundTrip_FieldRunEastAsianAndComplexScriptTypefaces_ArePreserved()
    {
        var p = new Presentation();
        var slide = new Slide();
        var shape = MakeShape();
        shape.TextBody = new TextBody();
        var para = new Paragraph();
        var field = new FieldRun
        {
            FieldType = "slidenum",
            CachedText = "1",
            FontFamily = "Calibri",
            EastAsiaFontFamily = "MS Gothic",
            ComplexScriptFontFamily = "Arial",
        };
        para.Runs.Add(new Run { Text = "1", Field = field });
        shape.TextBody.Paragraphs.Add(para);
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);

        var path = WriteToPptx(p);
        var reloaded = PptxPackageReader.Read(path);

        var rtField = reloaded.Slides[0].Shapes.First(s => s.Name == "Shape1")
            .TextBody!.Paragraphs[0].Runs[0].Field;

        rtField.Should().NotBeNull();
        rtField!.FontFamily.Should().Be("Calibri");
        rtField.EastAsiaFontFamily.Should().Be("MS Gothic",
            "field-run a:ea typeface must round-trip the same way as a plain run's");
        rtField.ComplexScriptFontFamily.Should().Be("Arial");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Action-only hlinkClick (Next/Previous/First/Last Slide, End Show)
    // ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(HyperlinkActionKind.NextSlide, "ppaction://hlinknextslide")]
    [InlineData(HyperlinkActionKind.PreviousSlide, "ppaction://hlinkprevslide")]
    [InlineData(HyperlinkActionKind.FirstSlide, "ppaction://hlinkfirstslide")]
    [InlineData(HyperlinkActionKind.LastSlide, "ppaction://hlinklastslide")]
    [InlineData(HyperlinkActionKind.LastSlideViewed, "ppaction://hlinklastslideviewed")]
    [InlineData(HyperlinkActionKind.EndShow, "ppaction://hlinkendshow")]
    public void RoundTrip_ShapeActionOnlyHyperlink_PreservesAction(HyperlinkActionKind kind, string expectedUri)
    {
        var p = new Presentation();
        var slide = new Slide();
        var shape = MakeShape();
        shape.Hyperlink = new Hyperlink { Action = kind, Tooltip = "Go" };
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);

        var path = WriteToPptx(p);
        var reloaded = PptxPackageReader.Read(path);

        var rtShape = reloaded.Slides[0].Shapes.First(s => s.Name == "Shape1");
        rtShape.Hyperlink.Should().NotBeNull(
            $"the action-only click action ({expectedUri}) must not be silently discarded on load");
        rtShape.Hyperlink!.Action.Should().Be(kind);
        rtShape.Hyperlink.Url.Should().BeNull();
        rtShape.Hyperlink.TargetSlideId.Should().BeNull();
        rtShape.Hyperlink.Tooltip.Should().Be("Go");
        rtShape.Hyperlink.IsExternal.Should().BeFalse();
    }

    [Fact]
    public void RoundTrip_ShapeActionOnlyHyperlink_EmitsNoRelationship()
    {
        // The written package must not allocate a rels entry for an action-only click — the
        // whole action lives in the action="" attribute, per the OOXML spec.
        var p = new Presentation();
        var slide = new Slide();
        var shape = MakeShape();
        shape.Hyperlink = new Hyperlink { Action = HyperlinkActionKind.NextSlide };
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);

        var path = WriteToPptx(p);

        using var archive = System.IO.Compression.ZipFile.OpenRead(path);
        var slideXml = archive.GetEntry("ppt/slides/slide1.xml");
        slideXml.Should().NotBeNull();
        using var reader = new StreamReader(slideXml!.Open());
        var xml = reader.ReadToEnd();
        xml.Should().Contain("ppaction://hlinknextslide");
        xml.Should().NotContain("r:id", "an action-only hlinkClick needs no relationship");
    }

    [Fact]
    public void RoundTrip_RunActionOnlyHyperlink_PreservesAction()
    {
        // Sibling test: the same action-only handling applies to run-level hyperlinks
        // (text hyperlinks), not just shape-level ones.
        var p = new Presentation();
        var slide = new Slide();
        var shape = MakeShape();
        shape.TextBody = new TextBody();
        var para = new Paragraph();
        var run = new Run { Text = "Next" };
        run.Hyperlink = new Hyperlink { Action = HyperlinkActionKind.EndShow };
        para.Runs.Add(run);
        shape.TextBody.Paragraphs.Add(para);
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);

        var path = WriteToPptx(p);
        var reloaded = PptxPackageReader.Read(path);

        var rtRun = reloaded.Slides[0].Shapes.First(s => s.Name == "Shape1")
            .TextBody!.Paragraphs[0].Runs[0];
        rtRun.Hyperlink.Should().NotBeNull();
        rtRun.Hyperlink!.Action.Should().Be(HyperlinkActionKind.EndShow);
    }

    [Fact]
    public void RoundTrip_ShapeExternalHyperlink_StillWorks_AlongsideActionSupport()
    {
        // Sibling/neighbour test: adding action-only support must not disturb ordinary
        // external hyperlink round-tripping (same code path in BuildHlinkClickEl/ResolveHlinkClick).
        var p = new Presentation();
        var slide = new Slide();
        var shape = MakeShape();
        shape.Hyperlink = new Hyperlink { Url = "https://example.com", Tooltip = "Go here" };
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);

        var path = WriteToPptx(p);
        var reloaded = PptxPackageReader.Read(path);

        var rtShape = reloaded.Slides[0].Shapes.First(s => s.Name == "Shape1");
        rtShape.Hyperlink.Should().NotBeNull();
        rtShape.Hyperlink!.Url.Should().Be("https://example.com");
        rtShape.Hyperlink.Action.Should().Be(HyperlinkActionKind.None);
        rtShape.Hyperlink.IsExternal.Should().BeTrue();
    }

    [Fact]
    public void RoundTrip_ShapeSlideJumpHyperlink_StillWorks_AlongsideActionSupport()
    {
        // Sibling/neighbour test: internal slide-jump hyperlinks (a different a:hlinkClick
        // shape entirely — has an r:id and action="ppaction://hlinksldjump") must be unaffected.
        var p = new Presentation();
        var s1 = new Slide { Id = "rId2" };
        var s2 = new Slide { Id = "rId3" };
        var shape = MakeShape();
        shape.Hyperlink = new Hyperlink { TargetSlideId = "rId3" };
        s1.Shapes.Add(shape);
        p.Slides.Add(s1);
        p.Slides.Add(s2);

        var path = WriteToPptx(p);
        var reloaded = PptxPackageReader.Read(path);

        var rtShape = reloaded.Slides[0].Shapes.First(s => s.Name == "Shape1");
        rtShape.Hyperlink.Should().NotBeNull();
        rtShape.Hyperlink!.TargetSlideId.Should().Be(reloaded.Slides[1].Id);
        rtShape.Hyperlink.Action.Should().Be(HyperlinkActionKind.None);
    }

    [Fact]
    public void ResolveHlinkClick_UnknownActionOnlyWithoutRId_StillDropsRatherThanCorrupting()
    {
        // Not-a-regression test: an hlinkClick with action-only text we don't recognize and no
        // rId (e.g. a custom/vendor action) must still be dropped rather than crashing or
        // fabricating a bogus hyperlink — only the standard PowerPoint action verbs are modeled.
        var p = new Presentation();
        var slide = new Slide();
        var shape = MakeShape();
        slide.Shapes.Add(shape);
        p.Slides.Add(slide);
        var path = WriteToPptx(p);

        // Patch the written slide XML in place: inject an hlinkClick with an unrecognized
        // action and no r:id, so the fallback path (not the six standard verbs) is exercised.
        using (var archive = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Update))
        {
            var entry = archive.GetEntry("ppt/slides/slide1.xml")!;
            string xml;
            using (var sr = new StreamReader(entry.Open())) xml = sr.ReadToEnd();
            xml = xml.Replace("<p:cNvPr id=\"1\" name=\"Shape1\">",
                "<p:cNvPr id=\"1\" name=\"Shape1\"><a:hlinkClick action=\"ppaction://hlinkcustomvendoraction\"/>");
            entry.Delete();
            var newEntry = archive.CreateEntry("ppt/slides/slide1.xml");
            using var sw = new StreamWriter(newEntry.Open());
            sw.Write(xml);
        }

        var reloaded = PptxPackageReader.Read(path);
        var rtShape = reloaded.Slides[0].Shapes.First(s => s.Name == "Shape1");
        rtShape.Hyperlink.Should().BeNull(
            "an unrecognized action-only hlinkClick with no rId must be dropped, not fabricated");
    }
}
