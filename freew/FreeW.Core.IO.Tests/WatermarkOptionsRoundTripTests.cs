using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip tests for <see cref="WatermarkOptions"/> — the full custom watermark options (text,
/// font, colour, layout, opacity) persisted as custom document properties (docProps/custom.xml).
/// </summary>
public class WatermarkOptionsRoundTripTests
{
    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static XDocument ReadCustomXml(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = zip.GetEntry("docProps/custom.xml")!;
        using var reader = entry.Open();
        return XDocument.Load(reader);
    }

    private static XDocument ReadHeaderXml(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = zip.GetEntry("word/header1.xml")!;
        using var reader = entry.Open();
        return XDocument.Load(reader);
    }

    private static string ReadHeaderRelsXml(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = zip.GetEntry("word/_rels/header1.xml.rels")!;
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static TextDocument ReadWithoutWatermarkCustomProperties(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
            zip.GetEntry("docProps/custom.xml")!.Delete();
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static TextDocument ReadWithMutatedVmlText(TextDocument document, string replacementText)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry("word/header1.xml")!;
            XDocument xml;
            using (var reader = entry.Open())
                xml = XDocument.Load(reader);
            var vml = XNamespace.Get("urn:schemas-microsoft-com:vml");
            xml.Descendants(vml + "textpath").Last().SetAttributeValue("string", replacementText);
            entry.Delete();
            var replacement = zip.CreateEntry("word/header1.xml", CompressionLevel.Optimal);
            using var writer = new StreamWriter(replacement.Open());
            xml.Save(writer);
        }
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static TextDocument ReadWithVmlPictureImageData(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            zip.GetEntry("docProps/custom.xml")!.Delete();

            var entry = zip.GetEntry("word/header1.xml")!;
            XDocument xml;
            using (var reader = entry.Open())
                xml = XDocument.Load(reader);

            var vml = XNamespace.Get("urn:schemas-microsoft-com:vml");
            var rel = XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships");
            var shape = xml.Descendants(vml + "shape")
                .Single(shape => shape.Attribute("id")?.Value == "PowerPlusPictureWaterMarkObject");
            shape.Element(vml + "fill")!.SetAttributeValue(rel + "id", null);
            shape.Add(new XElement(vml + "imagedata", new XAttribute(rel + "id", "rIdWatermarkImage")));

            entry.Delete();
            var replacement = zip.CreateEntry("word/header1.xml", CompressionLevel.Optimal);
            using var writer = new StreamWriter(replacement.Open());
            xml.Save(writer);
        }
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    // ── WatermarkOptions full round-trip ──────────────────────────────────

    [Fact]
    public void WatermarkOptions_AllFields_RoundTrip()
    {
        var doc = new TextDocument();
        doc.Page.WatermarkOptions = new WatermarkOptions("CONFIDENTIAL")
        {
            FontFamily = "Arial",
            FontColorHex = "#FF0000",
            Layout = WatermarkLayout.Horizontal,
            Opacity = 0.5
        };

        var loaded = RoundTrip(doc);

        var opts = loaded.Page.WatermarkOptions;
        opts.Should().NotBeNull();
        opts!.Text.Should().Be("CONFIDENTIAL");
        opts.FontFamily.Should().Be("Arial");
        opts.FontColorHex.Should().Be("#FF0000");
        opts.Layout.Should().Be(WatermarkLayout.Horizontal);
        opts.Opacity.Should().BeApproximately(0.5, 0.001);
    }

    [Fact]
    public void WatermarkOptions_Diagonal_RoundTrip()
    {
        var doc = new TextDocument();
        doc.Page.WatermarkOptions = new WatermarkOptions("DRAFT")
        {
            Layout = WatermarkLayout.Diagonal,
            Opacity = 1.0
        };

        var loaded = RoundTrip(doc);

        loaded.Page.WatermarkOptions.Should().NotBeNull();
        loaded.Page.WatermarkOptions!.Layout.Should().Be(WatermarkLayout.Diagonal);
        loaded.Page.WatermarkOptions.Opacity.Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public void WatermarkOptions_DefaultFields_RoundTrip()
    {
        var doc = new TextDocument();
        // Default options (only text set, everything else is default)
        doc.Page.WatermarkOptions = new WatermarkOptions("SAMPLE");

        var loaded = RoundTrip(doc);

        var opts = loaded.Page.WatermarkOptions;
        opts.Should().NotBeNull();
        opts!.Text.Should().Be("SAMPLE");
        opts.FontFamily.Should().Be("Calibri");
        opts.FontColorHex.Should().Be("#808080");
        opts.Layout.Should().Be(WatermarkLayout.Diagonal);
        opts.Opacity.Should().BeApproximately(0.3, 0.001);
    }

    [Fact]
    public void WatermarkOptions_Remove_RoundTrips_NoWatermark()
    {
        var doc = new TextDocument();
        doc.Page.WatermarkOptions = new WatermarkOptions("DRAFT");

        // Clear it
        doc.Page.WatermarkOptions = null;

        var loaded = RoundTrip(doc);

        loaded.Page.WatermarkOptions.Should().BeNull();
        loaded.Page.Watermark.Should().BeNullOrEmpty();
        loaded.Page.EffectiveWatermark.Should().BeNull();
    }

    [Fact]
    public void WatermarkOptions_WritesAllFiveCustomProperties()
    {
        var doc = new TextDocument();
        doc.Page.WatermarkOptions = new WatermarkOptions("SECRET")
        {
            FontFamily = "Times New Roman",
            FontColorHex = "#000080",
            Layout = WatermarkLayout.Horizontal,
            Opacity = 0.8
        };

        var xml = ReadCustomXml(doc);
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/custom-properties");
        var props = xml.Root!.Elements(ns + "property")
            .ToDictionary(e => e.Attribute("name")!.Value);

        props.Should().ContainKey(Ooxml.WatermarkPropertyName);
        props.Should().ContainKey(Ooxml.WatermarkFontFamilyPropertyName);
        props.Should().ContainKey(Ooxml.WatermarkColorPropertyName);
        props.Should().ContainKey(Ooxml.WatermarkLayoutPropertyName);
        props.Should().ContainKey(Ooxml.WatermarkOpacityPropertyName);
    }

    [Fact]
    public void WatermarkOptions_EmitsWordCompatibleVmlInDefaultHeader()
    {
        var doc = new TextDocument();
        doc.Page.WatermarkOptions = new WatermarkOptions("CONFIDENTIAL")
        {
            FontFamily = "Arial",
            FontColorHex = "#123456",
            Layout = WatermarkLayout.Horizontal,
            Opacity = 0.5
        };

        var xml = ReadHeaderXml(doc);
        var vml = XNamespace.Get("urn:schemas-microsoft-com:vml");
        var shape = xml.Descendants(vml + "shape").Single();
        var textPath = shape.Element(vml + "textpath");

        shape.Attribute("style")!.Value.Should().Contain("rotation:0");
        shape.Attribute("fillcolor")!.Value.Should().Be("123456");
        shape.Element(vml + "fill")!.Attribute("opacity")!.Value.Should().Be("0.5");
        textPath!.Attribute("string")!.Value.Should().Be("CONFIDENTIAL");
        textPath.Attribute("on")!.Value.Should().Be("t");
        textPath.Attribute("fitshape")!.Value.Should().Be("t");
        textPath.Attribute("style")!.Value.Should().Contain("font-family:Arial");
    }

    [Fact]
    public void NativeVmlTextWatermark_ImportsWhenFreeWCustomPropertiesAreAbsent()
    {
        var doc = new TextDocument();
        doc.Page.WatermarkOptions = new WatermarkOptions("NATIVE WORD")
        {
            FontFamily = "Arial",
            FontColorHex = "#123456",
            Layout = WatermarkLayout.Horizontal,
            Opacity = 0.5
        };

        var loaded = ReadWithoutWatermarkCustomProperties(doc);

        var watermark = loaded.Page.WatermarkOptions;
        watermark.Should().NotBeNull();
        watermark!.Text.Should().Be("NATIVE WORD");
        watermark.FontFamily.Should().Be("Arial");
        watermark.FontColorHex.Should().Be("#123456");
        watermark.Layout.Should().Be(WatermarkLayout.Horizontal);
        watermark.Opacity.Should().BeApproximately(0.5, 0.001);
    }

    [Fact]
    public void NativeVmlTextWatermark_DoesNotOverrideFreeWCustomProperties()
    {
        var doc = new TextDocument();
        doc.Page.WatermarkOptions = new WatermarkOptions("AUTHORITATIVE");

        var loaded = ReadWithMutatedVmlText(doc, "STALE VML");

        loaded.Page.WatermarkOptions!.Text.Should().Be("AUTHORITATIVE");
    }

    [Fact]
    public void NativeVmlPictureWatermark_ImportsHeaderLocalImageWhenFreeWCustomPropertiesAreAbsent()
    {
        var image = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
        var doc = new TextDocument();
        doc.Page.WatermarkOptions = new WatermarkOptions(string.Empty)
        {
            ImageBytes = image,
            Layout = WatermarkLayout.Horizontal,
            Opacity = 0.65
        };

        var loaded = ReadWithoutWatermarkCustomProperties(doc);

        var watermark = loaded.Page.WatermarkOptions;
        watermark.Should().NotBeNull();
        watermark!.IsPicture.Should().BeTrue();
        watermark.ImageBytes.Should().Equal(image);
        watermark.NativeVmlPictureWidthPt.Should().BeApproximately(468, 0.001);
        watermark.NativeVmlPictureHeightPt.Should().BeApproximately(281, 0.001);
        watermark.Layout.Should().Be(WatermarkLayout.Horizontal);
        watermark.Opacity.Should().BeApproximately(0.65, 0.001);
    }

    [Fact]
    public void NativeVmlPictureWatermark_ImportsImageDataRelationshipWhenFillDoesNotOwnIt()
    {
        var image = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
        var doc = new TextDocument();
        doc.Page.WatermarkOptions = new WatermarkOptions(string.Empty)
        {
            ImageBytes = image,
            Layout = WatermarkLayout.Diagonal,
            Opacity = 0.4
        };

        var loaded = ReadWithVmlPictureImageData(doc);

        var watermark = loaded.Page.WatermarkOptions;
        watermark.Should().NotBeNull();
        watermark!.ImageBytes.Should().Equal(image);
        watermark.Layout.Should().Be(WatermarkLayout.Diagonal);
        watermark.Opacity.Should().BeApproximately(0.4, 0.001);
    }

    [Fact]
    public void WatermarkOptions_DoesNotConsumeHeaderParagraph()
    {
        var doc = new TextDocument
        {
            Header = new HeaderFooter("Visible header")
        };
        doc.Page.WatermarkOptions = new WatermarkOptions("CONFIDENTIAL");

        var loaded = RoundTrip(doc);

        loaded.Header.Should().NotBeNull();
        loaded.Header!.Paragraphs.Should().ContainSingle();
        loaded.Header.Paragraphs[0].PlainText.Should().Be("Visible header");
    }

    [Fact]
    public void WatermarkOptions_RoundTrip_PreservesHeaderTextWhenWatermarkSharesTheParagraph()
    {
        var doc = new TextDocument();
        doc.FinalSectionHeadersFooters.Header = new HeaderFooter("Visible header");
        doc.Page.WatermarkOptions = new WatermarkOptions("CONFIDENTIAL");

        var loaded = RoundTrip(doc);

        loaded.FinalSectionHeadersFooters.Header!.Paragraphs.Should().ContainSingle();
        loaded.FinalSectionHeadersFooters.Header.Paragraphs[0].PlainText.Should().Be("Visible header");
    }

    [Fact]
    public void PictureWatermark_EmitsHeaderRelationshipAndMedia()
    {
        var doc = new TextDocument();
        doc.Page.WatermarkOptions = new WatermarkOptions(string.Empty)
        {
            ImageBytes = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==")
        };

        var rels = ReadHeaderRelsXml(doc);
        rels.Should().Contain("Id=\"rIdWatermarkImage\"");

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        zip.GetEntry("word/media/header1_watermark1.png").Should().NotBeNull();
    }

    [Fact]
    public void PictureWatermark_NativeVmlSize_RoundTripsThroughTheHeaderPayload()
    {
        var image = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
        var doc = new TextDocument();
        doc.Page.WatermarkOptions = new WatermarkOptions(string.Empty)
        {
            ImageBytes = image,
            NativeVmlPictureWidthPt = 512.5,
            NativeVmlPictureHeightPt = 240.25
        };

        var xml = ReadHeaderXml(doc);
        var vml = XNamespace.Get("urn:schemas-microsoft-com:vml");
        var shape = xml.Descendants(vml + "shape")
            .Single(shape => shape.Attribute("id")?.Value == "PowerPlusPictureWaterMarkObject");

        shape.Attribute("style")!.Value.Should().Contain("width:512.5pt;height:240.25pt");

        var loaded = RoundTrip(doc).Page.WatermarkOptions;
        loaded.Should().NotBeNull();
        loaded!.ImageBytes.Should().Equal(image);
        loaded.NativeVmlPictureWidthPt.Should().BeApproximately(512.5, 0.001);
        loaded.NativeVmlPictureHeightPt.Should().BeApproximately(240.25, 0.001);
    }

    [Fact]
    public void PictureWatermark_RoundTrip_DoesNotBecomeHeaderContent()
    {
        var image = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
        var doc = new TextDocument();
        doc.Page.WatermarkOptions = new WatermarkOptions(string.Empty)
        {
            ImageBytes = image,
            ScalePct = 48,
            Opacity = 0.38
        };

        var loaded = RoundTrip(doc);

        loaded.Page.EffectiveWatermark!.ImageBytes.Should().Equal(image);
        loaded.FinalSectionHeadersFooters.Header.Should().NotBeNull();
        loaded.FinalSectionHeadersFooters.Header!.IsEmpty.Should().BeTrue();
    }

    // ── Legacy Watermark migration ────────────────────────────────────────

    [Fact]
    public void LegacyWatermark_MigratesTo_EffectiveWatermark()
    {
        // Simulate a document written with the old single-string custom property only.
        var doc = new TextDocument();
        doc.Page.Watermark = "INTERNAL";           // legacy field, no WatermarkOptions
        doc.Page.WatermarkOptions.Should().BeNull(); // not set

        // EffectiveWatermark migrates the legacy text with default options.
        var effective = doc.Page.EffectiveWatermark;
        effective.Should().NotBeNull();
        effective!.Text.Should().Be("INTERNAL");
        effective.FontFamily.Should().Be("Calibri");
        effective.Layout.Should().Be(WatermarkLayout.Diagonal);
    }

    [Fact]
    public void LegacyWatermark_WritesOnlyTextProperty()
    {
        // When only Watermark (legacy) is set and WatermarkOptions is null, the writer emits
        // only the FreeWWatermark property (backward-compatible).
        var doc = new TextDocument();
        doc.Page.Watermark = "LEGACY";

        var xml = ReadCustomXml(doc);
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/custom-properties");
        var names = xml.Root!.Elements(ns + "property")
            .Select(e => e.Attribute("name")!.Value)
            .ToHashSet();

        names.Should().Contain(Ooxml.WatermarkPropertyName);
        names.Should().NotContain(Ooxml.WatermarkFontFamilyPropertyName);
    }

    [Fact]
    public void LegacyWatermark_RoundTrips_AsLegacy()
    {
        // A document with only the legacy Watermark property loads back with Watermark set,
        // WatermarkOptions null, but EffectiveWatermark non-null (migration on the fly).
        var doc = new TextDocument();
        doc.Page.Watermark = "OLD STYLE";

        var loaded = RoundTrip(doc);

        loaded.Page.Watermark.Should().Be("OLD STYLE");
        loaded.Page.WatermarkOptions.Should().BeNull();
        loaded.Page.EffectiveWatermark.Should().NotBeNull();
        loaded.Page.EffectiveWatermark!.Text.Should().Be("OLD STYLE");
    }

    [Fact]
    public void WatermarkOptions_TakesPrecedence_OverLegacyWatermark()
    {
        // If both are set in the model (shouldn't happen in normal use, but just in case),
        // WatermarkOptions wins for EffectiveWatermark.
        var doc = new TextDocument();
        doc.Page.Watermark = "OLD";
        doc.Page.WatermarkOptions = new WatermarkOptions("NEW");

        doc.Page.EffectiveWatermark!.Text.Should().Be("NEW");
    }
}
