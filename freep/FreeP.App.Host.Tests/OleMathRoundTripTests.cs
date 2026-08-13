using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using Free.Shared.Drawing;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Theme 21 round-trip tests:
///   - OLE embedded object: Kind, ProgId, EmbeddedBytes, fallback image, zip entries, rels.
///   - OMML math run: MathRunInfo.RawXml round-trips verbatim; fallback text is preserved.
/// </summary>
public sealed class OleMathRoundTripTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeP.OleMathTests-");
    private string _tempDir => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    // ─────────────────────────────────────────────────────────────────────────────
    // OLE embedded object round-trip
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_Ole_Kind_IsOle()
    {
        var (pres, _) = BuildOlePresentation();
        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var oleShape = reloaded.Slides[0].Shapes.First(s => s.Name == "OleShape");
        oleShape.Kind.Should().Be(SlideShapeKind.Ole,
            "OLE graphicFrame must be loaded with Kind=Ole");
    }

    [Fact]
    public void RoundTrip_Ole_ProgId_Preserved()
    {
        var (pres, progId) = BuildOlePresentation();
        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var oleShape = reloaded.Slides[0].Shapes.First(s => s.Name == "OleShape");
        oleShape.OleObject.Should().NotBeNull();
        oleShape.OleObject!.ProgId.Should().Be(progId,
            "ProgId must survive round-trip verbatim");
    }

    [Fact]
    public void RoundTrip_Ole_EmbeddedBytes_PreservedExact()
    {
        var embeddedBytes = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0xFF };
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 10, Name = "OleShape",
            Kind = SlideShapeKind.Ole,
            OffsetXEmu = 0, OffsetYEmu = 0,
            ExtentCxEmu = 2000000, ExtentCyEmu = 1500000,
            OleObject = new OleObjectInfo
            {
                EmbeddedBytes       = embeddedBytes,
                EmbeddedContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                EmbeddedExtension   = "xlsx",
                ProgId              = "Excel.Sheet.12",
                RelType             = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/package",
                OleObjXml           = "<p:oleObj xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" progId=\"Excel.Sheet.12\" spid=\"_x0000_s1026\" name=\"\" showAsIcon=\"1\"/>",
                WasAlternateContent = false,
            }
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var oleShape = reloaded.Slides[0].Shapes.First(s => s.Name == "OleShape");
        oleShape.OleObject.Should().NotBeNull();
        oleShape.OleObject!.EmbeddedBytes.Should().Equal(embeddedBytes,
            "embedded binary bytes must be byte-identical after round-trip");
    }

    [Fact]
    public void RoundTrip_Ole_EmbeddedBytesWrittenToZip()
    {
        var embeddedBytes = new byte[] { 0xD0, 0xCF, 0x11, 0xE0 }; // OLE2 header
        var (pres, _) = BuildOlePresentation(embeddedBytes);
        var path = WriteToPptx(pres);

        using var zip = ZipFile.OpenRead(path);
        var embEntries = zip.Entries
            .Where(e => e.FullName.StartsWith("ppt/embeddings/", StringComparison.OrdinalIgnoreCase))
            .ToList();
        embEntries.Should().NotBeEmpty(
            "embedded binary must be written as ppt/embeddings/oleObject*.* inside the zip");

        using var ms = new MemoryStream();
        using (var s = embEntries[0].Open()) s.CopyTo(ms);
        ms.ToArray().Should().StartWith(embeddedBytes,
            "embedded binary must match what was stored in the model");
    }

    [Fact]
    public void RoundTrip_Ole_FallbackImage_Preserved()
    {
        var fallbackPng = CreateMinimalPng();
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 20, Name = "OleShape",
            Kind = SlideShapeKind.Ole,
            OffsetXEmu = 0, OffsetYEmu = 0,
            ExtentCxEmu = 2000000, ExtentCyEmu = 1500000,
            OleObject = new OleObjectInfo
            {
                EmbeddedBytes       = new byte[] { 0x01, 0x02 },
                EmbeddedContentType = "application/vnd.ms-excel",
                EmbeddedExtension   = "bin",
                ProgId              = "Excel.Sheet.8",
                RelType             = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/oleObject",
                OleObjXml           = "<p:oleObj xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" progId=\"Excel.Sheet.8\"/>",
                WasAlternateContent = false,
            },
            Picture = new ImagePart { Bytes = fallbackPng, ContentType = "image/png" },
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var oleShape = reloaded.Slides[0].Shapes.First(s => s.Name == "OleShape");
        oleShape.Picture.Should().NotBeNull("fallback image must survive round-trip");
        oleShape.Picture!.Bytes.Should().Equal(fallbackPng,
            "fallback image bytes must be byte-identical");
    }

    [Fact]
    public void RoundTrip_Ole_ZipHasFallbackImageEntry()
    {
        var fallbackPng = CreateMinimalPng();
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 20, Name = "OleShape",
            Kind = SlideShapeKind.Ole,
            OffsetXEmu = 0, OffsetYEmu = 0,
            ExtentCxEmu = 2000000, ExtentCyEmu = 1500000,
            OleObject = new OleObjectInfo
            {
                EmbeddedBytes       = new byte[] { 0x01 },
                EmbeddedContentType = "application/vnd.ms-excel",
                EmbeddedExtension   = "bin",
                ProgId              = "Excel.Sheet.8",
                OleObjXml           = "<p:oleObj xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" progId=\"Excel.Sheet.8\"/>",
            },
            Picture = new ImagePart { Bytes = fallbackPng, ContentType = "image/png" },
        });
        pres.Slides.Add(slide);

        var path = WriteToPptx(pres);
        using var zip = ZipFile.OpenRead(path);

        var mediaEntry = zip.Entries
            .Where(e => e.FullName.StartsWith("ppt/media/oleImg", StringComparison.OrdinalIgnoreCase))
            .ToList();
        mediaEntry.Should().NotBeEmpty("fallback preview image must be written to ppt/media/");
    }

    [Fact]
    public void RoundTrip_Ole_SlideXmlHasGraphicFrame()
    {
        var (pres, _) = BuildOlePresentation();
        var path = WriteToPptx(pres);

        using var zip = ZipFile.OpenRead(path);
        var slide1 = zip.Entries.First(e => e.FullName == "ppt/slides/slide1.xml");
        using var ms = new MemoryStream();
        using (var s = slide1.Open()) s.CopyTo(ms);
        var xml = System.Text.Encoding.UTF8.GetString(ms.ToArray());

        xml.Should().Contain("graphicFrame",
            "the OLE shape must be serialized as a p:graphicFrame in the slide XML");
        xml.Should().Contain("oleObj",
            "the p:oleObj element must appear inside the graphicFrame");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // OMML math run round-trip
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_Math_FallbackText_Preserved()
    {
        var (pres, _) = BuildMathPresentation();
        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var shape = reloaded.Slides[0].Shapes.First(s => s.Name == "MathShape");
        var para = shape.TextBody!.Paragraphs[0];
        var mathRun = para.Runs.FirstOrDefault(r => r.Math is not null);
        mathRun.Should().NotBeNull("math run must survive round-trip");
        mathRun!.Text.Should().Contain("E=mc", "fallback plain text (m:t concat) must be preserved");
    }

    [Fact]
    public void RoundTrip_Math_RawXml_Preserved()
    {
        var (pres, rawXml) = BuildMathPresentation();
        var path = WriteToPptx(pres);
        var reloaded = PptxPackageReader.Read(path);

        var shape = reloaded.Slides[0].Shapes.First(s => s.Name == "MathShape");
        var para = shape.TextBody!.Paragraphs[0];
        var mathRun = para.Runs.FirstOrDefault(r => r.Math is not null);
        mathRun.Should().NotBeNull();
        mathRun!.Math!.RawXml.Should().Contain("oMath",
            "the raw OMML XML must contain the m:oMath element after round-trip");
    }

    [Fact]
    public void RoundTrip_Math_SlideXmlContainsMathElement()
    {
        var (pres, _) = BuildMathPresentation();
        var path = WriteToPptx(pres);

        using var zip = ZipFile.OpenRead(path);
        var slide1 = zip.Entries.First(e => e.FullName == "ppt/slides/slide1.xml");
        using var ms = new MemoryStream();
        using (var s = slide1.Open()) s.CopyTo(ms);
        var xml = System.Text.Encoding.UTF8.GetString(ms.ToArray());

        xml.Should().Contain("oMath",
            "the m:oMath element must be emitted verbatim in the slide XML");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private string WriteToPptx(Presentation pres)
    {
        var path = Path.Combine(_tempDir, Guid.NewGuid().ToString("N") + ".pptx");
        PptxPackageWriter.Write(pres, path);
        return path;
    }

    private static (Presentation pres, string progId) BuildOlePresentation(
        byte[]? embeddedBytes = null)
    {
        const string progId = "Excel.Sheet.12";
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 10, Name = "OleShape",
            Kind = SlideShapeKind.Ole,
            OffsetXEmu = 0, OffsetYEmu = 0,
            ExtentCxEmu = 2000000, ExtentCyEmu = 1500000,
            OleObject = new OleObjectInfo
            {
                EmbeddedBytes       = embeddedBytes ?? new byte[] { 0x01, 0x02, 0x03 },
                EmbeddedContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                EmbeddedExtension   = "xlsx",
                ProgId              = progId,
                RelType             = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/package",
                OleObjXml           = $"<p:oleObj xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" progId=\"{progId}\" showAsIcon=\"1\"/>",
                WasAlternateContent = false,
            }
        });
        pres.Slides.Add(slide);
        return (pres, progId);
    }

    private static (Presentation pres, string rawXml) BuildMathPresentation()
    {
        // Minimal a14:m element wrapping a single m:oMath with fallback text "E=mc2"
        const string rawXml =
            "<a14:m xmlns:a14=\"http://schemas.microsoft.com/office/drawing/2010/main\">" +
            "<m:oMathPara xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\">" +
            "<m:oMath>" +
            "<m:r><m:t>E=mc</m:t></m:r>" +
            "<m:sSup><m:e><m:r><m:t>2</m:t></m:r></m:e><m:sup><m:r><m:t>2</m:t></m:r></m:sup></m:sSup>" +
            "</m:oMath></m:oMathPara></a14:m>";

        var pres = new Presentation();
        var slide = new Slide();

        var mathShape = new SlideShape
        {
            Id = 30, Name = "MathShape",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 0, OffsetYEmu = 0,
            ExtentCxEmu = 3000000, ExtentCyEmu = 1000000,
        };
        var body = new TextBody { Anchor = VerticalAnchor.Top };
        var para = new Paragraph { Align = TextAlign.Left };
        para.Runs.Add(new Run
        {
            Text = "E=mc2", // fallback plain text
            Math = new MathRunInfo
            {
                RawXml            = rawXml,
                IsAlternateContent = false,
            }
        });
        body.Paragraphs.Add(para);
        mathShape.TextBody = body;
        slide.Shapes.Add(mathShape);
        pres.Slides.Add(slide);

        return (pres, rawXml);
    }

    /// <summary>Creates a minimal valid 1x1 white PNG.</summary>
    private static byte[] CreateMinimalPng() =>
        Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
}
