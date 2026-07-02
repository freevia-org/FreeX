using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FreeP.App.Compositor;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Wave 25A: Round-trip tests for slide zoom, ink contentPart, 3D model, and unknown
/// graphicFrame preservation (no silent loss guarantee).
/// </summary>
public sealed class ModernObjectsRoundTripTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "FreeP.ModernObjectsTests", Guid.NewGuid().ToString("N"));

    public ModernObjectsRoundTripTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    // ── Minimal 1×1 white PNG ─────────────────────────────────────────────────
    private static readonly byte[] MinPng =
        Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    // ── Zoom graphicFrame round-trip ──────────────────────────────────────────

    [Fact]
    public void ZoomGraphicFrame_RoundTrips_VerbatimXmlAndPreservedKind()
    {
        // Build a PPTX with a synthetic zoom graphicFrame injected into slide1
        const string zoomUri = "http://schemas.microsoft.com/office/powerpoint/2010/main";
        const string zoomXml = """
            <p:graphicFrame xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                            xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <p:nvGraphicFramePr>
                <p:cNvPr id="10" name="Zoom 10"/>
                <p:cNvGraphicFramePr/>
                <p:nvPr/>
              </p:nvGraphicFramePr>
              <p:xfrm>
                <a:off x="457200" y="274638"/>
                <a:ext cx="2743200" cy="1828800"/>
              </p:xfrm>
              <a:graphic>
                <a:graphicData uri="http://schemas.microsoft.com/office/powerpoint/2010/main">
                  <p14:zoom xmlns:p14="http://schemas.microsoft.com/office/powerpoint/2010/main" type="slide"/>
                </a:graphicData>
              </a:graphic>
            </p:graphicFrame>
            """;

        var ms1 = BuildPptxWithShapeXml(zoomXml);

        // Read
        var pres1 = PptxPackageReader.Read(ms1);
        var slide1 = pres1.Slides[0];
        var zoom = slide1.Shapes.FirstOrDefault(s => s.Kind == SlideShapeKind.Zoom);

        zoom.Should().NotBeNull("zoom graphicFrame should not be silently dropped");
        zoom!.PreservedObject.Should().NotBeNull();
        zoom.PreservedObject!.ObjectKind.Should().Be(PreservedObjectKind.Zoom);
        zoom.PreservedObject.RawXml.Should().Contain(zoomUri);

        // Write → re-read (round-trip)
        var ms2 = WritePptxToMemory(pres1);
        var pres2 = PptxPackageReader.Read(ms2);
        var zoom2 = pres2.Slides[0].Shapes.FirstOrDefault(s => s.Kind == SlideShapeKind.Zoom);

        zoom2.Should().NotBeNull("zoom must survive write/re-read round-trip");
        zoom2!.PreservedObject!.ObjectKind.Should().Be(PreservedObjectKind.Zoom);
        zoom2.PreservedObject.RawXml.Should().Contain(zoomUri);
    }

    // ── Ink contentPart round-trip ────────────────────────────────────────────

    [Fact]
    public void InkContentPart_RoundTrips_VerbatimXmlAndCapturesBytes()
    {
        const string inkXml = """
            <p:contentPart xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                           xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                           r:id="rIdInk1">
              <p:nvContentPartPr>
                <p:cNvPr id="20" name="Ink 20"/>
              </p:nvContentPartPr>
              <p:xfrm xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                <a:off x="914400" y="457200"/>
                <a:ext cx="1828800" cy="914400"/>
              </p:xfrm>
            </p:contentPart>
            """;
        var inkBytes = Encoding.UTF8.GetBytes("<inkml><trace>0 0 1 1</trace></inkml>");
        const string inkRelType = "http://schemas.microsoft.com/office/2016/05/19/relationships/ink";

        var ms1 = BuildPptxWithContentPart(inkXml, inkBytes, inkRelType,
            inkPartPath: "ppt/ink/ink1.xml", inkRelId: "rIdInk1");

        var pres1 = PptxPackageReader.Read(ms1);
        var inkShape = pres1.Slides[0].Shapes.FirstOrDefault(s => s.Kind == SlideShapeKind.Ink);

        inkShape.Should().NotBeNull("ink contentPart should not be silently dropped");
        inkShape!.PreservedObject.Should().NotBeNull();
        inkShape.PreservedObject!.ObjectKind.Should().Be(PreservedObjectKind.Ink);
        inkShape.PreservedObject.Parts.Values.Should().Contain(b => b.Length > 0,
            "the ink part bytes should have been captured");

        // Round-trip
        var ms2 = WritePptxToMemory(pres1);
        var pres2 = PptxPackageReader.Read(ms2);
        var ink2 = pres2.Slides[0].Shapes.FirstOrDefault(s => s.Kind == SlideShapeKind.Ink);

        ink2.Should().NotBeNull("ink must survive write/re-read round-trip");
        ink2!.PreservedObject!.ObjectKind.Should().Be(PreservedObjectKind.Ink);
        ink2.PreservedObject.Parts.Values.Should().Contain(b => b.Length > 0,
            "ink part bytes should survive round-trip");
    }

    // ── 3D model graphicFrame round-trip ──────────────────────────────────────

    [Fact]
    public void Model3dGraphicFrame_RoundTrips_VerbatimXmlAndGlbBytes()
    {
        const string model3dUri = "http://schemas.microsoft.com/office/drawing/2017/model3d";
        const string model3dRelType = "http://schemas.microsoft.com/office/2017/06/relationships/model3d";
        var glbBytes = new byte[] { 0x67, 0x6C, 0x54, 0x46, 0x02, 0x00 }; // glTF magic

        const string model3dXml = """
            <p:graphicFrame xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                            xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                            xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <p:nvGraphicFramePr>
                <p:cNvPr id="30" name="3D Model 30"/>
                <p:cNvGraphicFramePr/>
                <p:nvPr/>
              </p:nvGraphicFramePr>
              <p:xfrm>
                <a:off x="914400" y="457200"/>
                <a:ext cx="2743200" cy="2743200"/>
              </p:xfrm>
              <a:graphic>
                <a:graphicData uri="http://schemas.microsoft.com/office/drawing/2017/model3d">
                  <am3d:model3d xmlns:am3d="http://schemas.microsoft.com/office/drawing/2017/model3d"
                                r:id="rIdGlb1"/>
                </a:graphicData>
              </a:graphic>
            </p:graphicFrame>
            """;

        var ms1 = BuildPptxWithShapeXml(model3dXml,
            extraParts: new() { ["ppt/media/model1.glb"] = (glbBytes, "model/gltf-binary") },
            extraRels: new() { ["rIdGlb1"] = (model3dRelType, "../media/model1.glb") });

        var pres1 = PptxPackageReader.Read(ms1);
        var m3d = pres1.Slides[0].Shapes.FirstOrDefault(s => s.Kind == SlideShapeKind.Model3d);

        m3d.Should().NotBeNull("3D model graphicFrame should not be silently dropped");
        m3d!.PreservedObject!.ObjectKind.Should().Be(PreservedObjectKind.Model3d);
        m3d.PreservedObject.RawXml.Should().Contain(model3dUri);
        m3d.PreservedObject.Parts.Values.Should().Contain(b => b.SequenceEqual(glbBytes),
            "the GLB bytes should have been captured");

        // Round-trip
        var ms2 = WritePptxToMemory(pres1);
        var pres2 = PptxPackageReader.Read(ms2);
        var m3d2 = pres2.Slides[0].Shapes.FirstOrDefault(s => s.Kind == SlideShapeKind.Model3d);

        m3d2.Should().NotBeNull("3D model must survive write/re-read round-trip");
        m3d2!.PreservedObject!.Parts.Values.Should().Contain(b => b.SequenceEqual(glbBytes),
            "GLB bytes must survive round-trip");
    }

    // ── Unknown graphicFrame — no silent loss ─────────────────────────────────

    [Fact]
    public void UnknownGraphicFrameUri_IsPreserved_VerbatimAndNotDropped()
    {
        const string unknownXml = """
            <p:graphicFrame xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                            xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <p:nvGraphicFramePr>
                <p:cNvPr id="40" name="Unknown 40"/>
                <p:cNvGraphicFramePr/>
                <p:nvPr/>
              </p:nvGraphicFramePr>
              <p:xfrm>
                <a:off x="0" y="0"/>
                <a:ext cx="1828800" cy="914400"/>
              </p:xfrm>
              <a:graphic>
                <a:graphicData uri="http://example.com/some/future/extension">
                  <ex:data xmlns:ex="http://example.com/some/future/extension" value="test-payload"/>
                </a:graphicData>
              </a:graphic>
            </p:graphicFrame>
            """;

        var ms1 = BuildPptxWithShapeXml(unknownXml);

        var pres1 = PptxPackageReader.Read(ms1);
        var unknown = pres1.Slides[0].Shapes
            .FirstOrDefault(s => s.Kind == SlideShapeKind.PreservedObject);

        unknown.Should().NotBeNull("unknown graphicFrame should not be silently dropped");
        unknown!.PreservedObject!.ObjectKind.Should().Be(PreservedObjectKind.Unknown);
        unknown.PreservedObject.RawXml.Should().Contain("test-payload",
            "the payload XML must be captured verbatim");

        // Round-trip
        var ms2 = WritePptxToMemory(pres1);
        var pres2 = PptxPackageReader.Read(ms2);
        var u2 = pres2.Slides[0].Shapes
            .FirstOrDefault(s => s.Kind == SlideShapeKind.PreservedObject);

        u2.Should().NotBeNull("unknown graphicFrame must survive round-trip");
        u2!.PreservedObject!.RawXml.Should().Contain("test-payload",
            "payload must survive write/re-read round-trip");
    }

    // ── EA1: preserved fallback image gets a content-type Default entry ──────────

    [Fact]
    public void EA1_PreservedFallbackImage_GetsContentTypeDefault()
    {
        // Bug EA1: the fallback image bytes written for a preserved object never had its file
        // extension registered in mediaExtensions → no Default entry in [Content_Types].xml → repair.
        const string unknownXml = """
            <p:graphicFrame xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                            xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <p:nvGraphicFramePr>
                <p:cNvPr id="50" name="EA1Test"/>
                <p:cNvGraphicFramePr/><p:nvPr/>
              </p:nvGraphicFramePr>
              <p:xfrm><a:off x="0" y="0"/><a:ext cx="1" cy="1"/></p:xfrm>
              <a:graphic>
                <a:graphicData uri="http://example.com/test">
                  <ex:data xmlns:ex="http://example.com/test" value="ea1"/>
                </a:graphicData>
              </a:graphic>
            </p:graphicFrame>
            """;

        // Read a PPTX with this shape; the fallback image comes from the shape tree injection.
        var pres1 = PptxPackageReader.Read(BuildPptxWithShapeXml(unknownXml));

        // Manually set a PNG fallback on the shape to test EA1 (simulates what ExtractPreservedFallbackImage would set)
        var shape = pres1.Slides[0].Shapes.FirstOrDefault(s => s.Kind == SlideShapeKind.PreservedObject);
        Assert.NotNull(shape);
        shape!.Picture = new ImagePart { Bytes = MinPng, ContentType = "image/png" };

        // Write and verify [Content_Types].xml has a Default for "png"
        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres1, ms);
        ms.Position = 0;

        using var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);
        var ctEntry = zip.GetEntry("[Content_Types].xml");
        Assert.NotNull(ctEntry);
        string ctXml;
        using (var sr = new StreamReader(ctEntry!.Open())) ctXml = sr.ReadToEnd();

        // EA1 fix: "png" Default must be present because fallback image extension is registered
        Assert.Contains("Extension=\"png\"", ctXml);
        Assert.Contains("image/png", ctXml);
    }

    // ── FA1 (was EA2): reindexed preserved part gets correct Override ────────────

    /// <summary>
    /// Builds a slide with one shape whose PreservedObject references
    /// "ppt/media/3dModel.glb" (a Model3d preserved object).
    /// </summary>
    private static Slide BuildPreservedModel3dSlide(uint shapeId, byte[] glbBytes)
    {
        var slide = new Slide();
        var shape = new SlideShape
        {
            Id   = shapeId,
            Kind = SlideShapeKind.Model3d,
            ExtentCxEmu = 914400, ExtentCyEmu = 914400,
            PreservedObject = new PreservedObjectInfo
            {
                ObjectKind          = PreservedObjectKind.Model3d,
                RawXml              = "<p:graphicFrame xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\"/>",
                WasAlternateContent = false,
            },
        };
        shape.PreservedObject.Parts["ppt/media/3dModel.glb"]            = glbBytes;
        shape.PreservedObject.PartContentTypes["ppt/media/3dModel.glb"] = "model/gltf-binary";
        shape.PreservedObject.SlideRels["rId1"] =
            ("http://schemas.microsoft.com/office/2017/06/relationships/model3d", "ppt/media/3dModel.glb");
        slide.Shapes.Add(shape);
        return slide;
    }

    /// <summary>
    /// Strengthened FA1 regression test. The ORIGINAL EA2 test only asserted that the
    /// content-types XML contained the substring "preserved_2_" and "model/gltf-binary" — a
    /// weak check that passes even if the Override is emitted at a path the writer never
    /// actually wrote to the zip (which is exactly the FA1 bug: the pre-scan's reindex
    /// numbering can disagree with WriteSlidePreservedObjects' real per-slide-reset numbering).
    ///
    /// This test instead parses [Content_Types].xml properly and asserts, for EVERY Override
    /// whose PartName looks like a preserved-object media part, that a zip entry ACTUALLY EXISTS
    /// at that exact path — i.e. the Override always describes a real, written part.
    /// It also asserts the converse: every preserved-object part written to the zip has a
    /// matching Override.
    /// </summary>
    [Fact]
    public void FA1_ReindexedPreservedPart_OverrideMatchesActualWrittenZipEntry()
    {
        var pres = new Presentation();
        var glbBytes = new byte[] { 0x67, 0x6C, 0x54, 0x46 }; // glTF magic

        // Two slides, each with a shape whose preserved part resolves to the SAME OPC path.
        pres.Slides.Add(BuildPreservedModel3dSlide(1, glbBytes));
        pres.Slides.Add(BuildPreservedModel3dSlide(2, glbBytes));

        AssertContentTypesOverridesMatchWrittenPreservedParts(pres);
    }

    /// <summary>
    /// FA1: same as above but with a THIRD slide added. The old buggy pre-scan used a
    /// "remap once, then skip forever" global guard keyed only on the original path, so once the
    /// SECOND slide's occurrence claimed the sole remap dictionary entry, a THIRD (or later)
    /// slide reusing the same original path was silently ignored by the pre-scan — even though
    /// the real per-slide writer (with its fresh per-slide writtenPaths/partCounter) would still
    /// reindex or write that third occurrence. A 2-slide case can pass by coincidence; 3+ slides
    /// exposes the bug for real.
    /// </summary>
    [Fact]
    public void FA1_ThreeSlidesShareOnePath_EveryWrittenPartHasMatchingOverride()
    {
        var pres = new Presentation();
        var glbBytes = new byte[] { 0x67, 0x6C, 0x54, 0x46 };

        pres.Slides.Add(BuildPreservedModel3dSlide(1, glbBytes));
        pres.Slides.Add(BuildPreservedModel3dSlide(2, glbBytes));
        pres.Slides.Add(BuildPreservedModel3dSlide(3, glbBytes));

        AssertContentTypesOverridesMatchWrittenPreservedParts(pres);
    }

    /// <summary>
    /// FA1: two shapes on the SAME slide both reference the same original path, followed by a
    /// second slide that reuses the same original path again. Exercises the per-shape AND
    /// per-slide reset boundaries together.
    /// </summary>
    [Fact]
    public void FA1_TwoShapesOneSlideThenAnotherSlide_EveryWrittenPartHasMatchingOverride()
    {
        var pres = new Presentation();
        var glbBytes = new byte[] { 0x67, 0x6C, 0x54, 0x46 };

        var slide1 = new Slide();
        slide1.Shapes.Add(BuildPreservedModel3dSlide(1, glbBytes).Shapes[0]);
        var shape1b = BuildPreservedModel3dSlide(101, glbBytes).Shapes[0];
        slide1.Shapes.Add(shape1b);
        pres.Slides.Add(slide1);
        pres.Slides.Add(BuildPreservedModel3dSlide(2, glbBytes));

        AssertContentTypesOverridesMatchWrittenPreservedParts(pres);
    }

    /// <summary>
    /// Writes <paramref name="pres"/> and asserts: (1) every [Content_Types].xml Override whose
    /// PartName is a preserved-media path (ppt/media/preserved_*.* or ppt/media/3dModel.glb, the
    /// paths used in these tests) corresponds to an actual zip entry, and (2) every actual
    /// preserved-media zip entry has a corresponding Override with the expected content type.
    /// </summary>
    private static void AssertContentTypesOverridesMatchWrittenPreservedParts(Presentation pres)
    {
        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;

        using var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);
        var ctEntry = zip.GetEntry("[Content_Types].xml");
        Assert.NotNull(ctEntry);
        XDocument ctDoc;
        using (var s = ctEntry!.Open()) ctDoc = XDocument.Load(s);

        XNamespace ct = "http://schemas.openxmlformats.org/package/2006/content-types";
        bool LooksLikePreservedMediaPath(string partName) =>
            partName.Contains("/media/preserved_", StringComparison.OrdinalIgnoreCase) ||
            partName.Contains("/media/3dModel.glb", StringComparison.OrdinalIgnoreCase);

        var overridesForPreservedParts = ctDoc.Root!
            .Elements(ct + "Override")
            .Select(e => (PartName: e.Attribute("PartName")!.Value, ContentType: e.Attribute("ContentType")!.Value))
            .Where(o => LooksLikePreservedMediaPath(o.PartName))
            .ToList();

        overridesForPreservedParts.Should().NotBeEmpty(
            "at least one preserved-object media Override should have been emitted");

        // (1) Every Override for a preserved-media path must point at a REAL zip entry.
        foreach (var (partName, contentType) in overridesForPreservedParts)
        {
            var zipPath = partName.TrimStart('/');
            var entry = zip.GetEntry(zipPath);
            entry.Should().NotBeNull(
                $"[Content_Types].xml declares an Override at '{partName}' (content type " +
                $"'{contentType}') but the writer never actually wrote a zip entry there — " +
                "this is the FA1 bug: PowerPoint would show a repair prompt for the part that " +
                "WAS written (with no matching Override) while this phantom Override describes " +
                "nothing real.");
        }

        // (2) Every actually-written preserved-media zip entry must have a matching Override.
        var writtenPreservedEntries = zip.Entries
            .Where(e => LooksLikePreservedMediaPath(e.FullName))
            .Select(e => e.FullName)
            .ToList();

        writtenPreservedEntries.Should().NotBeEmpty("the writer should have written at least one preserved media part");

        var overriddenPaths = new HashSet<string>(
            overridesForPreservedParts.Select(o => o.PartName.TrimStart('/')),
            StringComparer.OrdinalIgnoreCase);

        foreach (var writtenPath in writtenPreservedEntries)
        {
            overriddenPaths.Should().Contain(writtenPath,
                $"the writer wrote a preserved-object part at '{writtenPath}' but " +
                "[Content_Types].xml has no Override for it — PowerPoint would prompt to repair.");
        }
    }

    // ── EA3: mc:AlternateContent Requires token round-trips verbatim ──────────────

    [Fact]
    public void EA3_McRequiresToken_RoundTrips_Verbatim()
    {
        // Bug EA3: the preserved-object mc:AlternateContent re-wrap hardcoded Requires="p14",
        // even when the original used "p15", "p159", or another prefix. This test verifies the
        // token is captured on read and re-emitted verbatim on write.
        const string shapeXml = """
            <mc:AlternateContent
                xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                xmlns:p159="http://schemas.microsoft.com/office/powerpoint/2015/09/main">
              <mc:Choice Requires="p159" xmlns:p159="http://schemas.microsoft.com/office/powerpoint/2015/09/main">
                <p:graphicFrame xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                                xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                  <p:nvGraphicFramePr>
                    <p:cNvPr id="60" name="EA3Test"/><p:cNvGraphicFramePr/><p:nvPr/>
                  </p:nvGraphicFramePr>
                  <p:xfrm><a:off x="0" y="0"/><a:ext cx="1" cy="1"/></p:xfrm>
                  <a:graphic>
                    <a:graphicData uri="http://example.com/test">
                      <ex:data xmlns:ex="http://example.com/test" value="ea3"/>
                    </a:graphicData>
                  </a:graphic>
                </p:graphicFrame>
              </mc:Choice>
              <mc:Fallback>
                <p:graphicFrame xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                                xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                  <p:nvGraphicFramePr>
                    <p:cNvPr id="60" name="EA3Test"/><p:cNvGraphicFramePr/><p:nvPr/>
                  </p:nvGraphicFramePr>
                  <p:xfrm><a:off x="0" y="0"/><a:ext cx="1" cy="1"/></p:xfrm>
                  <a:graphic>
                    <a:graphicData uri="http://example.com/test"/>
                  </a:graphic>
                </p:graphicFrame>
              </mc:Fallback>
            </mc:AlternateContent>
            """;

        var ms1 = BuildPptxWithShapeXml(shapeXml);
        var pres1 = PptxPackageReader.Read(ms1);

        var shape = pres1.Slides[0].Shapes.FirstOrDefault(s => s.Kind == SlideShapeKind.PreservedObject);
        Assert.NotNull(shape);

        // EA3: the token must be captured on read
        Assert.True(shape!.PreservedObject!.WasAlternateContent, "WasAlternateContent must be set");
        Assert.Equal("p159", shape.PreservedObject.McRequiresToken); // EA3 fix: captured

        // Write and re-read
        var ms2 = WritePptxToMemory(pres1);
        ms2.Position = 0;

        // Verify the re-emitted XML has Requires="p159", not "p14"
        using var zip = new ZipArchive(ms2, ZipArchiveMode.Read, leaveOpen: true);
        var slideEntry = zip.Entries.FirstOrDefault(e =>
            e.FullName.StartsWith("ppt/slides/slide") && e.FullName.EndsWith(".xml"));
        Assert.NotNull(slideEntry);
        string slideXml;
        using (var sr = new StreamReader(slideEntry!.Open())) slideXml = sr.ReadToEnd();

        // EA3 fix: must contain the ORIGINAL Requires token "p159", not the hardcoded "p14"
        Assert.Contains("Requires=\"p159\"", slideXml);
        // EA3 fix: the prefix "p14" must NOT appear as the Requires token
        Assert.DoesNotContain("Requires=\"p14\"", slideXml);
    }

    // ── FA2: multi-token mc:Choice Requires must not throw / must produce valid xmlns ──────

    /// <summary>
    /// Bug FA2: mc:AlternateContent permits Requires to be a SPACE-SEPARATED list of prefixes
    /// (e.g. Requires="p14 p15"). The old writer did
    /// `new XAttribute(XNamespace.Xmlns + requiresToken, requiresNsUri)` using the RAW (possibly
    /// multi-token) string as the xmlns LOCAL NAME — an xmlns local-name containing a space is
    /// not a legal XML name, so XName/XAttribute construction throws XmlException, and the
    /// ENTIRE save fails for any preserved object whose original wrapper used a multi-token
    /// Requires. This test asserts the save no longer throws, and that Requires plus BOTH
    /// per-token xmlns declarations are preserved on round-trip.
    /// </summary>
    [Fact]
    public void FA2_MultiTokenRequires_WritesWithoutThrowing_AndRoundTripsBothXmlns()
    {
        const string p14Uri  = "http://schemas.microsoft.com/office/powerpoint/2010/main";
        const string p15Uri  = "http://schemas.microsoft.com/office/powerpoint/2012/main";
        const string shapeXml = """
            <mc:AlternateContent
                xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                xmlns:p14="http://schemas.microsoft.com/office/powerpoint/2010/main"
                xmlns:p15="http://schemas.microsoft.com/office/powerpoint/2012/main">
              <mc:Choice Requires="p14 p15"
                         xmlns:p14="http://schemas.microsoft.com/office/powerpoint/2010/main"
                         xmlns:p15="http://schemas.microsoft.com/office/powerpoint/2012/main">
                <p:graphicFrame xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                                xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                  <p:nvGraphicFramePr>
                    <p:cNvPr id="70" name="FA2Test"/><p:cNvGraphicFramePr/><p:nvPr/>
                  </p:nvGraphicFramePr>
                  <p:xfrm><a:off x="0" y="0"/><a:ext cx="1" cy="1"/></p:xfrm>
                  <a:graphic>
                    <a:graphicData uri="http://example.com/test">
                      <ex:data xmlns:ex="http://example.com/test" value="fa2"/>
                    </a:graphicData>
                  </a:graphic>
                </p:graphicFrame>
              </mc:Choice>
              <mc:Fallback>
                <p:graphicFrame xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                                xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                  <p:nvGraphicFramePr>
                    <p:cNvPr id="70" name="FA2Test"/><p:cNvGraphicFramePr/><p:nvPr/>
                  </p:nvGraphicFramePr>
                  <p:xfrm><a:off x="0" y="0"/><a:ext cx="1" cy="1"/></p:xfrm>
                  <a:graphic>
                    <a:graphicData uri="http://example.com/test"/>
                  </a:graphic>
                </p:graphicFrame>
              </mc:Fallback>
            </mc:AlternateContent>
            """;

        var ms1 = BuildPptxWithShapeXml(shapeXml);
        var pres1 = PptxPackageReader.Read(ms1);

        var shape = pres1.Slides[0].Shapes.FirstOrDefault(s => s.Kind == SlideShapeKind.PreservedObject);
        shape.Should().NotBeNull();
        shape!.PreservedObject!.WasAlternateContent.Should().BeTrue();
        shape.PreservedObject.McRequiresToken.Should().Be("p14 p15",
            "the raw multi-token Requires value must be captured verbatim");

        // FA2: both tokens' namespace URIs must have been resolved individually on read.
        shape.PreservedObject.McRequiresNsUris.Should().ContainKey("p14")
            .WhoseValue.Should().Be(p14Uri);
        shape.PreservedObject.McRequiresNsUris.Should().ContainKey("p15")
            .WhoseValue.Should().Be(p15Uri);

        // FA2 core assertion: writing must NOT throw (the old code threw XmlException here
        // because it tried to declare an xmlns named "p14 p15", which is not a legal XML name).
        MemoryStream ms2 = null!;
        var writeAction = () => ms2 = WritePptxToMemory(pres1);
        writeAction.Should().NotThrow("a multi-token Requires must not crash the save");

        ms2.Position = 0;
        using var zip = new ZipArchive(ms2, ZipArchiveMode.Read, leaveOpen: true);
        var slideEntry = zip.Entries.FirstOrDefault(e =>
            e.FullName.StartsWith("ppt/slides/slide") && e.FullName.EndsWith(".xml"));
        slideEntry.Should().NotBeNull();
        string slideXml;
        using (var sr = new StreamReader(slideEntry!.Open())) slideXml = sr.ReadToEnd();

        // The XML must be well-formed (XDocument.Parse throws on malformed XML / illegal names).
        XDocument slideDoc = null!;
        var parseAction = () => slideDoc = XDocument.Parse(slideXml);
        parseAction.Should().NotThrow("the re-emitted slide XML must be well-formed");

        // Requires attribute preserved verbatim, and BOTH xmlns declarations present.
        slideXml.Should().Contain("Requires=\"p14 p15\"");
        slideXml.Should().Contain(p14Uri);
        slideXml.Should().Contain(p15Uri);

        // Both xmlns prefixes must actually be declared as xmlns attributes (not just appear as
        // a substring somewhere) — find the mc:Choice element and check its in-scope namespaces.
        XNamespace mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";
        var choiceEl = slideDoc!.Descendants(mc + "Choice").FirstOrDefault();
        choiceEl.Should().NotBeNull("mc:Choice must be re-emitted");
        choiceEl!.GetNamespaceOfPrefix("p14")?.NamespaceName.Should().Be(p14Uri);
        choiceEl.GetNamespaceOfPrefix("p15")?.NamespaceName.Should().Be(p15Uri);
    }

    /// <summary>
    /// FA2: when a Requires token's namespace URI is UNKNOWN (not resolvable from the source
    /// document and not one of the well-known MS prefixes), the writer must NOT force the p14
    /// URI onto it — that would be a wrong/misleading binding. This test uses a fabricated
    /// unknown prefix ("zzUnknown") with no xmlns declared anywhere in scope, so
    /// McRequiresNsUris will be empty; the writer should fall back to preserving the element
    /// verbatim (un-wrapped) rather than emit a broken/incorrect AlternateContent.
    /// </summary>
    [Fact]
    public void FA2_UnknownRequiresToken_DoesNotForcePrefixToP14Uri()
    {
        const string shapeXml = """
            <mc:AlternateContent
                xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006">
              <mc:Choice Requires="zzUnknown">
                <p:graphicFrame xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                                xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                  <p:nvGraphicFramePr>
                    <p:cNvPr id="71" name="FA2UnknownTest"/><p:cNvGraphicFramePr/><p:nvPr/>
                  </p:nvGraphicFramePr>
                  <p:xfrm><a:off x="0" y="0"/><a:ext cx="1" cy="1"/></p:xfrm>
                  <a:graphic>
                    <a:graphicData uri="http://example.com/test">
                      <ex:data xmlns:ex="http://example.com/test" value="fa2unknown"/>
                    </a:graphicData>
                  </a:graphic>
                </p:graphicFrame>
              </mc:Choice>
              <mc:Fallback>
                <p:graphicFrame xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                                xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                  <p:nvGraphicFramePr>
                    <p:cNvPr id="71" name="FA2UnknownTest"/><p:cNvGraphicFramePr/><p:nvPr/>
                  </p:nvGraphicFramePr>
                  <p:xfrm><a:off x="0" y="0"/><a:ext cx="1" cy="1"/></p:xfrm>
                  <a:graphic>
                    <a:graphicData uri="http://example.com/test"/>
                  </a:graphic>
                </p:graphicFrame>
              </mc:Fallback>
            </mc:AlternateContent>
            """;

        var ms1 = BuildPptxWithShapeXml(shapeXml);
        var pres1 = PptxPackageReader.Read(ms1);

        var shape = pres1.Slides[0].Shapes.FirstOrDefault(s => s.Kind == SlideShapeKind.PreservedObject);
        shape.Should().NotBeNull();
        shape!.PreservedObject!.McRequiresToken.Should().Be("zzUnknown");
        shape.PreservedObject.McRequiresNsUris.Should().NotContainKey("zzUnknown",
            "an unresolvable prefix must not get a guessed URI on read");

        Action writeAction = () =>
        {
            using var ms2 = WritePptxToMemory(pres1);
            ms2.Position = 0;
            using var zip = new ZipArchive(ms2, ZipArchiveMode.Read, leaveOpen: true);
            var slideEntry = zip.Entries.First(e =>
                e.FullName.StartsWith("ppt/slides/slide") && e.FullName.EndsWith(".xml"));
            string slideXml;
            using (var sr = new StreamReader(slideEntry.Open())) slideXml = sr.ReadToEnd();

            // Must not crash and must not fabricate the p14 URI for the "zzUnknown" prefix.
            var p14Uri = "http://schemas.microsoft.com/office/powerpoint/2010/main";
            if (slideXml.Contains("zzUnknown"))
                slideXml.Should().NotContain($"xmlns:zzUnknown=\"{p14Uri}\"");
        };
        writeAction.Should().NotThrow("an unknown Requires token must not crash the save");
    }

    // ── SlideCloner preserves modern object ───────────────────────────────────

    [Fact]
    public void SlideCloner_ClonesPreservedObject_CorrectlySharedBytes()
    {
        var slide = new Slide();
        var shape = new SlideShape
        {
            Id  = 7,
            Kind = SlideShapeKind.Zoom,
            ExtentCxEmu = 1000000,
            ExtentCyEmu = 500000,
            PreservedObject = new PreservedObjectInfo
            {
                ObjectKind          = PreservedObjectKind.Zoom,
                RawXml              = "<p:graphicFrame/>",
                WasAlternateContent = true,
            },
            Picture = new ImagePart { Bytes = MinPng, ContentType = "image/png" },
        };
        shape.PreservedObject.Parts["ppt/media/img.png"]            = MinPng;
        shape.PreservedObject.PartContentTypes["ppt/media/img.png"] = "image/png";
        shape.PreservedObject.SlideRels["rId1"] = ("reltype", "ppt/media/img.png");
        slide.Shapes.Add(shape);

        var clone  = SlideCloner.CloneSlide(slide);
        var cs     = clone.Shapes[0];

        cs.Kind.Should().Be(SlideShapeKind.Zoom);
        cs.PreservedObject.Should().NotBeNull();
        cs.PreservedObject!.ObjectKind.Should().Be(PreservedObjectKind.Zoom);
        cs.PreservedObject.WasAlternateContent.Should().BeTrue();
        cs.PreservedObject.Parts.Should().ContainKey("ppt/media/img.png");
        cs.PreservedObject.SlideRels["rId1"].TargetPath.Should().Be("ppt/media/img.png");
        cs.Picture.Should().NotBeNull();
    }

    // ── Compositor renders fallback picture for modern objects ────────────────

    [Fact]
    public void Compositor_PreservedObject_WithPreviewImage_ProducesPictureOp()
    {
        var pres  = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id          = 8,
            Kind        = SlideShapeKind.Model3d,
            OffsetXEmu  = 457200,
            OffsetYEmu  = 457200,
            ExtentCxEmu = 2743200,
            ExtentCyEmu = 1828800,
            PreservedObject = new PreservedObjectInfo { ObjectKind = PreservedObjectKind.Model3d },
            Picture     = new ImagePart { Bytes = MinPng, ContentType = "image/png" },
        });
        pres.Slides.Add(slide);

        var ops = SlideCompositor.Compose(pres, slide);

        ops.OfType<DrawOp.Picture>().Should().HaveCount(1,
            "a preserved object with a preview image should emit one DrawOp.Picture");
        ops.OfType<DrawOp.Picture>().First().Bytes.Should().BeEquivalentTo(MinPng);
    }

    [Fact]
    public void Compositor_PreservedObject_WithoutPreviewImage_ProducesShapeOp()
    {
        var pres  = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id          = 9,
            Kind        = SlideShapeKind.Zoom,
            OffsetXEmu  = 457200,
            OffsetYEmu  = 457200,
            ExtentCxEmu = 2743200,
            ExtentCyEmu = 1828800,
            PreservedObject = new PreservedObjectInfo { ObjectKind = PreservedObjectKind.Zoom },
            // No Picture — should emit grey placeholder rectangle
        });
        pres.Slides.Add(slide);

        var ops = SlideCompositor.Compose(pres, slide);

        ops.OfType<DrawOp.Shape>().Should().HaveCount(1,
            "a preserved object without a preview image should emit one DrawOp.Shape placeholder");
    }

    // ── Fixture builders ──────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal PPTX stream with a single slide containing <paramref name="shapeXml"/>
    /// injected into the spTree. Optionally adds extra OPC parts and slide rels.
    /// </summary>
    private static MemoryStream BuildPptxWithShapeXml(
        string shapeXml,
        Dictionary<string, (byte[] bytes, string contentType)>? extraParts = null,
        Dictionary<string, (string relType, string target)>? extraRels = null)
    {
        // Create a base PPTX via the writer
        var basePres = new Presentation();
        basePres.Slides.Add(new Slide());
        var ms = new MemoryStream();
        PptxPackageWriter.Write(basePres, ms);
        ms.Position = 0;

        // Open for update and inject content
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true))
        {
            // Add extra parts
            if (extraParts is not null)
            {
                foreach (var kv in extraParts)
                {
                    var entry = zip.CreateEntry(kv.Key, CompressionLevel.Optimal);
                    using var s = entry.Open();
                    s.Write(kv.Value.bytes);
                }
            }

            // Patch slide rels to add extra rels
            if (extraRels is not null)
            {
                const string relsPath = "ppt/slides/_rels/slide1.xml.rels";
                var relsEntry = zip.GetEntry(relsPath);
                string relsXml;
                using (var sr = new StreamReader(relsEntry!.Open())) relsXml = sr.ReadToEnd();
                var relsDoc = XDocument.Parse(relsXml);
                var pkgRelsNs = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships");
                foreach (var kv in extraRels)
                {
                    relsDoc.Root!.Add(new XElement(pkgRelsNs + "Relationship",
                        new XAttribute("Id", kv.Key),
                        new XAttribute("Type", kv.Value.relType),
                        new XAttribute("Target", kv.Value.target)));
                }
                relsEntry.Delete();
                var newRels = zip.CreateEntry(relsPath, CompressionLevel.Optimal);
                using (var sw = new StreamWriter(newRels.Open())) relsDoc.Save(sw);
            }

            // Inject shape into spTree in slide1.xml
            const string slidePath = "ppt/slides/slide1.xml";
            var slideEntry = zip.GetEntry(slidePath)!;
            string slideXml;
            using (var sr = new StreamReader(slideEntry.Open())) slideXml = sr.ReadToEnd();
            var slideDoc = XDocument.Parse(slideXml);
            var spTree = slideDoc.Descendants().First(e => e.Name.LocalName == "spTree");
            spTree.Add(XElement.Parse(shapeXml));
            slideEntry.Delete();
            var newSlide = zip.CreateEntry(slidePath, CompressionLevel.Optimal);
            using (var sw = new StreamWriter(newSlide.Open())) slideDoc.Save(sw);
        }

        ms.Position = 0;
        return ms;
    }

    /// <summary>
    /// Builds a minimal PPTX with a contentPart element and a matching OPC ink part.
    /// </summary>
    private static MemoryStream BuildPptxWithContentPart(
        string contentPartXml,
        byte[] inkPartBytes,
        string inkRelType,
        string inkPartPath,
        string inkRelId)
    {
        var basePres = new Presentation();
        basePres.Slides.Add(new Slide());
        var ms = new MemoryStream();
        PptxPackageWriter.Write(basePres, ms);
        ms.Position = 0;

        using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true))
        {
            // Write the ink part
            var inkEntry = zip.CreateEntry(inkPartPath, CompressionLevel.Optimal);
            using (var s = inkEntry.Open()) s.Write(inkPartBytes);

            // Add rel entry (relative target from ppt/slides/ → ../../ppt/ink/)
            const string relsPath = "ppt/slides/_rels/slide1.xml.rels";
            var relsEntry = zip.GetEntry(relsPath)!;
            string relsXml;
            using (var sr = new StreamReader(relsEntry.Open())) relsXml = sr.ReadToEnd();
            var relsDoc = XDocument.Parse(relsXml);
            var pkgRelsNs = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships");
            // Relative path from ppt/slides/ to ppt/ink/
            var relTarget = "../" + string.Join("/", inkPartPath.Split('/')[1..]);
            relsDoc.Root!.Add(new XElement(pkgRelsNs + "Relationship",
                new XAttribute("Id", inkRelId),
                new XAttribute("Type", inkRelType),
                new XAttribute("Target", relTarget)));
            relsEntry.Delete();
            var newRels = zip.CreateEntry(relsPath, CompressionLevel.Optimal);
            using (var sw = new StreamWriter(newRels.Open())) relsDoc.Save(sw);

            // Inject contentPart into spTree
            const string slidePath = "ppt/slides/slide1.xml";
            var slideEntry = zip.GetEntry(slidePath)!;
            string slideXml;
            using (var sr = new StreamReader(slideEntry.Open())) slideXml = sr.ReadToEnd();
            var slideDoc = XDocument.Parse(slideXml);
            var spTree = slideDoc.Descendants().First(e => e.Name.LocalName == "spTree");
            spTree.Add(XElement.Parse(contentPartXml));
            slideEntry.Delete();
            var newSlide = zip.CreateEntry(slidePath, CompressionLevel.Optimal);
            using (var sw = new StreamWriter(newSlide.Open())) slideDoc.Save(sw);
        }

        ms.Position = 0;
        return ms;
    }

    private static MemoryStream WritePptxToMemory(Presentation pres)
    {
        var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        return ms;
    }
}
