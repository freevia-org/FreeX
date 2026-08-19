using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FreeP.App.Compositor;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// sweep84 F1 + model-not-persisted F1: the writer must derive &lt;p:sldSz type="..."/&gt; from the
/// actual slide dimensions instead of hardcoding "screen16x9", and must not synthesize an
/// explicit zero-extent &lt;a:xfrm&gt; for shapes that never had one (which flips
/// HasExplicitZeroExtentTransform on the next read and hides the shape).
/// </summary>
public sealed class PptxWriterSlideSizeAndPlaceholderXfrmTests : IDisposable
{
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";

    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeP.SlideSizeXfrmTests-");
    private string _tempDir => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    // ── sweep84 F1: <p:sldSz type="..."/> must match cx/cy ────────────────────────────────────

    [Fact]
    public void Write_Standard4x3SlideSize_EmitsScreen4x3Type_NotHardcoded16x9()
    {
        var pres = Presentation.CreateEmpty();
        pres.SlideSizeCxEmu = 9144000;  // 10in  — classic 4:3 width
        pres.SlideSizeCyEmu = 6858000;  // 7.5in — standard height

        var presEl = ReadPresentationXml(WriteToPptx(pres));
        var sldSz = presEl.Element(P + "sldSz")!;

        sldSz.Attribute("cx")!.Value.Should().Be("9144000");
        sldSz.Attribute("cy")!.Value.Should().Be("6858000");
        sldSz.Attribute("type")!.Value.Should().Be("screen4x3",
            "cx/cy declare a 4:3 aspect ratio; the type hint must not contradict them");
    }

    [Fact]
    public void Write_CustomSlideSize_EmitsCustomType_NotHardcoded16x9()
    {
        var pres = Presentation.CreateEmpty();
        pres.SlideSizeCxEmu = 7000000;
        pres.SlideSizeCyEmu = 5000000;

        var presEl = ReadPresentationXml(WriteToPptx(pres));
        var sldSz = presEl.Element(P + "sldSz")!;

        sldSz.Attribute("cx")!.Value.Should().Be("7000000");
        sldSz.Attribute("cy")!.Value.Should().Be("5000000");
        sldSz.Attribute("type")!.Value.Should().Be("custom",
            "an arbitrary non-preset size has no valid named ST_SlideSize value except custom");
    }

    /// <summary>No-regression sibling: the genuine 16:9 default must still say screen16x9.</summary>
    [Fact]
    public void Write_Widescreen16x9SlideSize_StillEmitsScreen16x9Type()
    {
        var pres = Presentation.CreateEmpty();
        pres.SlideSizeCxEmu = 12192000; // 13.33in
        pres.SlideSizeCyEmu = 6858000;  // 7.5in

        var presEl = ReadPresentationXml(WriteToPptx(pres));
        var sldSz = presEl.Element(P + "sldSz")!;

        sldSz.Attribute("cx")!.Value.Should().Be("12192000");
        sldSz.Attribute("cy")!.Value.Should().Be("6858000");
        sldSz.Attribute("type")!.Value.Should().Be("screen16x9");
    }

    // ── model-not-persisted F1: placeholder with no explicit xfrm must round-trip as inheriting ──

    [Fact]
    public void Write_TitlePlaceholderWithNoExplicitTransform_OmitsXfrmAndSurvivesRoundTrip()
    {
        // Exactly Presentation.CreateEmpty()'s repro: a title placeholder that never had an
        // <a:xfrm> (Offset/Extent all default to 0, HasExplicitZeroExtentTransform is false).
        var pres = Presentation.CreateEmpty();
        var titleShape = pres.Slides[0].Shapes.Single(s => s.Placeholder?.Type == PlaceholderType.Title);
        titleShape.OffsetXEmu.Should().Be(0);
        titleShape.ExtentCxEmu.Should().Be(0);
        titleShape.HasExplicitZeroExtentTransform.Should().BeFalse();

        var opsBeforeSave = SlideCompositor.Compose(pres, pres.Slides[0]);
        opsBeforeSave.OfType<DrawOp.Shape>().Should().ContainSingle(
            "the title placeholder must render before it is ever saved");

        var path = WriteToPptx(pres);

        // The raw XML must not carry a synthesized zero-extent <a:xfrm> for the title shape.
        var slideXml = ReadSlideXml(path, 1);
        var titleSp = slideXml.Descendants(P + "sp")
            .Single(sp => sp.Element(P + "nvSpPr")?
                .Element(P + "nvPr")?
                .Element(P + "ph")?
                .Attribute("type")?.Value == "title");
        var spPr = titleSp.Element(P + "spPr")!;
        spPr.Element(A + "xfrm").Should().BeNull(
            "a shape that never had explicit geometry must not gain a synthesized <a:xfrm> on save");

        var reloaded = PptxPackageReader.Read(path);
        var reloadedTitle = reloaded.Slides[0].Shapes.Single(s => s.Placeholder?.Type == PlaceholderType.Title);
        reloadedTitle.HasExplicitZeroExtentTransform.Should().BeFalse(
            "round-tripping must not flip the shape into the 'deliberately hidden' state");

        var opsAfterReload = SlideCompositor.Compose(reloaded, reloaded.Slides[0]);
        opsAfterReload.OfType<DrawOp.Shape>().Should().ContainSingle(
            "the title placeholder must still render after save + reload — it must not vanish");
    }

    /// <summary>
    /// No-regression sibling: a shape whose source spPr genuinely declared an explicit
    /// zero-extent xfrm (PowerPoint's "intentionally hidden placeholder" signal) must still be
    /// written with that explicit xfrm and must still read back as hidden.
    /// </summary>
    [Fact]
    public void Write_ExplicitlyHiddenZeroExtentPlaceholder_StillWritesXfrmAndStaysHidden()
    {
        var pres = Presentation.CreateEmpty();
        var titleShape = pres.Slides[0].Shapes.Single(s => s.Placeholder?.Type == PlaceholderType.Title);
        titleShape.HasExplicitZeroExtentTransform = true; // simulate a source file that had <a:xfrm><a:off .../><a:ext cx="0" cy="0"/></a:xfrm>

        var path = WriteToPptx(pres);

        var slideXml = ReadSlideXml(path, 1);
        var titleSp = slideXml.Descendants(P + "sp")
            .Single(sp => sp.Element(P + "nvSpPr")?
                .Element(P + "nvPr")?
                .Element(P + "ph")?
                .Attribute("type")?.Value == "title");
        var xfrm = titleSp.Element(P + "spPr")!.Element(A + "xfrm");
        xfrm.Should().NotBeNull("an explicitly-hidden zero-extent shape must still round-trip its xfrm");
        xfrm!.Element(A + "ext")!.Attribute("cx")!.Value.Should().Be("0");
        xfrm.Element(A + "ext")!.Attribute("cy")!.Value.Should().Be("0");

        var reloaded = PptxPackageReader.Read(path);
        var reloadedTitle = reloaded.Slides[0].Shapes.Single(s => s.Placeholder?.Type == PlaceholderType.Title);
        reloadedTitle.HasExplicitZeroExtentTransform.Should().BeTrue(
            "a genuinely explicit zero-extent transform must still be recognized as the hidden signal");
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────

    private string WriteToPptx(Presentation pres)
    {
        var path = Path.Combine(_tempDir, Guid.NewGuid().ToString("N") + ".pptx");
        PptxPackageWriter.Write(pres, path);
        return path;
    }

    private static XElement ReadPresentationXml(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        using var stream = zip.GetEntry("ppt/presentation.xml")!.Open();
        return XDocument.Load(stream).Root!;
    }

    private static XElement ReadSlideXml(string path, int slideNumber)
    {
        using var zip = ZipFile.OpenRead(path);
        using var stream = zip.GetEntry($"ppt/slides/slide{slideNumber}.xml")!.Open();
        return XDocument.Load(stream).Root!;
    }
}
