using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip tests for the Design-tab depth additions (W23):
/// <list type="bullet">
/// <item>Canonical page-border art <c>w:val</c> token write/read.</item>
/// <item>Image watermark bytes + scale + washout round-trip (base-64 custom properties).</item>
/// <item>Image watermark property names are written when <see cref="WatermarkOptions.IsPicture"/> is true.</item>
/// </list>
/// </summary>
public class DesignDepthRoundTripTests
{
    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static XDocument ReadPartXml(TextDocument document, string partPath)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var entry = zip.GetEntry(partPath)!.Open();
        return XDocument.Load(entry);
    }

    private static XDocument ReadCustomXml(TextDocument document) =>
        ReadPartXml(document, "docProps/custom.xml");

    private static TextDocument ReadWithDocumentXmlMutation(TextDocument document, Action<XDocument> mutate)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var original = zip.GetEntry("word/document.xml")!;
            XDocument xml;
            using (var source = original.Open())
                xml = XDocument.Load(source);
            mutate(xml);
            original.Delete();
            using var destination = zip.CreateEntry("word/document.xml").Open();
            xml.Save(destination);
        }

        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    // ── Page-border art token round-trip ──────────────────────────────────────────────────────────

    [Fact]
    public void PageBorderArtId_RoundTrips_WriteAndRead()
    {
        var doc = new TextDocument();
        doc.Page.PageBorder = new PageBorder("#0000FF", 2.0) { ArtId = 38 };

        var loaded = RoundTrip(doc);

        loaded.Page.PageBorder.Should().NotBeNull();
        loaded.Page.PageBorder!.ArtId.Should().Be(38);
        loaded.Page.PageBorder.ColorHex.Should().Be("#0000FF");
        loaded.Page.PageBorder.WidthPt.Should().BeApproximately(2.0, 0.001);
    }

    [Fact]
    public void PageBorderArtId_Zero_EmitsLineStyleWithoutLegacyArtAttribute()
    {
        var doc = new TextDocument();
        doc.Page.PageBorder = new PageBorder("#000000", 1.0) { ArtId = 0 };

        var sectPr = ReadPartXml(doc, "word/document.xml")
            .Root!.Descendants(XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main") + "pgBorders")
            .FirstOrDefault();

        sectPr.Should().NotBeNull();
        var wNs = XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        sectPr!.Elements().Should().OnlyContain(edge => edge.Attribute(wNs + "val")!.Value == "single");
        sectPr.Elements().Should().NotContain(edge => edge.Attribute(wNs + "art") != null);
    }

    [Fact]
    public void PageBorderArtId_NonZero_EmitsCanonicalWordArtTokenOnAllEdges()
    {
        var doc = new TextDocument();
        doc.Page.PageBorder = new PageBorder("#FF0000", 3.0) { ArtId = 84 };

        var W = XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        var sectPr = ReadPartXml(doc, "word/document.xml")
            .Root!.Descendants(W + "pgBorders")
            .Single();

        var edges = sectPr.Elements().ToList();
        edges.Should().HaveCount(4); // top, left, bottom, right
        foreach (var edge in edges)
        {
            edge.Attribute(W + "val")!.Value.Should().Be("people");
            edge.Attribute(W + "art").Should().BeNull();
            edge.Attribute(W + "sz")!.Value.Should().Be("24");
            edge.Attribute(W + "space")!.Value.Should().Be("24");
        }
    }

    [Fact]
    public void PageBorderArtId_UnsupportedId_FallsBackToValidLineBorderXml()
    {
        var doc = new TextDocument();
        doc.Page.PageBorder = new PageBorder("#000000", 1.0) { ArtId = 999 };

        var W = XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        var xml = ReadPartXml(doc, "word/document.xml");
        var pgBorders = xml.Descendants(W + "pgBorders").Single();

        pgBorders.Elements().Should().OnlyContain(edge =>
            edge.Attribute(W + "val")!.Value == "single");
        pgBorders.DescendantsAndSelf().Attributes(W + "art").Should().BeEmpty();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(38)]
    [InlineData(84)]
    [InlineData(160)]
    public void PageBorderArtId_VariousIds_RoundTrip(int artId)
    {
        var doc = new TextDocument();
        doc.Page.PageBorder = new PageBorder("#000000", 1.0) { ArtId = artId };

        var loaded = RoundTrip(doc);

        loaded.Page.PageBorder!.ArtId.Should().Be(artId);
    }

    [Fact]
    public void PageBorderArtId_ReadsLegacyFreeWArtAttributeAsCompatibilityFallback()
    {
        var doc = new TextDocument();
        doc.Page.PageBorder = new PageBorder("#FF0000", 3.0);
        var W = XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");

        var loaded = ReadWithDocumentXmlMutation(doc, xml =>
        {
            foreach (var edge in xml.Root!.Descendants(W + "pgBorders").Single().Elements())
                edge.SetAttributeValue(W + "art", "84");
        });

        loaded.Page.PageBorder!.ArtId.Should().Be(84);
        loaded.Page.PageBorder.LineStyle.Should().Be(BorderLineStyle.Single);
    }

    [Fact]
    public void PageBorderArtId_NoBorder_NoPageBorderEmitted()
    {
        var doc = new TextDocument();
        doc.Page.PageBorder = null;

        var W = XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        var docXml = ReadPartXml(doc, "word/document.xml");
        docXml.Root!.Descendants(W + "pgBorders").Should().BeEmpty();
    }

    // ── Image watermark round-trip ────────────────────────────────────────────────────────────────

    // A small 1x1 PNG (the shortest valid PNG: 67 bytes).
    private static readonly byte[] TinyPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, // PNG signature
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, // IHDR length + type
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, // width=1, height=1
        0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53, // bitDepth=8, color=RGB
        0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41, // IDAT length + type
        0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00, // compressed pixel
        0x00, 0x00, 0x02, 0x00, 0x01, 0xE2, 0x21, 0xBC,
        0x33, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, // IEND
        0x44, 0xAE, 0x42, 0x60, 0x82
    ];

    [Fact]
    public void ImageWatermark_AllFields_RoundTrip()
    {
        var doc = new TextDocument();
        doc.Page.WatermarkOptions = new WatermarkOptions(string.Empty)
        {
            ImageBytes   = TinyPng,
            ScalePct     = 150,
            Layout       = WatermarkLayout.Horizontal,
            Opacity      = 1.0,
        };

        var loaded = RoundTrip(doc);

        var opts = loaded.Page.WatermarkOptions;
        opts.Should().NotBeNull();
        opts!.IsPicture.Should().BeTrue();
        opts.ImageBytes.Should().Equal(TinyPng);
        opts.ScalePct.Should().Be(150);
        opts.Layout.Should().Be(WatermarkLayout.Horizontal);
        opts.Opacity.Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public void ImageWatermark_AutoScale_RoundTrips()
    {
        var doc = new TextDocument();
        doc.Page.WatermarkOptions = new WatermarkOptions(string.Empty)
        {
            ImageBytes = TinyPng,
            ScalePct   = 0, // Auto
        };

        var loaded = RoundTrip(doc);

        loaded.Page.WatermarkOptions!.ScalePct.Should().Be(0);
        loaded.Page.WatermarkOptions.ImageBytes.Should().Equal(TinyPng);
    }

    [Fact]
    public void ImageWatermark_WritesImageAndScaleProperties()
    {
        var doc = new TextDocument();
        doc.Page.WatermarkOptions = new WatermarkOptions(string.Empty)
        {
            ImageBytes = TinyPng,
            ScalePct   = 75,
        };

        var xml = ReadCustomXml(doc);
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/custom-properties");
        var props = xml.Root!.Elements(ns + "property")
            .ToDictionary(e => e.Attribute("name")!.Value);

        props.Should().ContainKey(Ooxml.WatermarkImagePropertyName);
        props.Should().ContainKey(Ooxml.WatermarkScalePropertyName);
        props[Ooxml.WatermarkScalePropertyName]
            .Descendants()
            .Single()
            .Value
            .Should().Be("75");
        // Image bytes must be valid base-64.
        var base64 = props[Ooxml.WatermarkImagePropertyName].Descendants().Single().Value;
        Convert.FromBase64String(base64).Should().Equal(TinyPng);
    }

    [Fact]
    public void TextWatermark_DoesNotWriteImageProperty()
    {
        var doc = new TextDocument();
        doc.Page.WatermarkOptions = new WatermarkOptions("DRAFT")
        {
            FontFamily = "Arial",
            Opacity    = 0.5
        };

        var xml = ReadCustomXml(doc);
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/custom-properties");
        var names = xml.Root!.Elements(ns + "property")
            .Select(e => e.Attribute("name")!.Value)
            .ToHashSet();

        names.Should().NotContain(Ooxml.WatermarkImagePropertyName);
        names.Should().NotContain(Ooxml.WatermarkScalePropertyName);
    }

    [Fact]
    public void ImageWatermark_IsPicture_ReturnsTrueForNonNullBytes()
    {
        var textOpts = new WatermarkOptions("DRAFT");
        textOpts.IsPicture.Should().BeFalse();

        var picOpts = new WatermarkOptions(string.Empty) { ImageBytes = TinyPng };
        picOpts.IsPicture.Should().BeTrue();
    }
}
