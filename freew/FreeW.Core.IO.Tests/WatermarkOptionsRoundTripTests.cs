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

    private static bool HasDefaultHeader(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        return zip.GetEntry("word/header1.xml") is not null;
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

    private static TextDocument ReadWithoutWordVisibleWatermarkPayload(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
            zip.GetEntry("word/header1.xml")!.Delete();
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

    private static TextDocument ReadWithWordNativeWatermarkShapeId(TextDocument document)
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
            xml.Descendants(vml + "shape")
                .Single(shape => shape.Attribute("id")?.Value == "PowerPlusWaterMarkObject")
                .SetAttributeValue("id", "PowerPlusWaterMarkObject357476642");
            entry.Delete();
            var replacement = zip.CreateEntry("word/header1.xml", CompressionLevel.Optimal);
            using var writer = new StreamWriter(replacement.Open());
            xml.Save(writer);
            zip.GetEntry("docProps/custom.xml")!.Delete();
        }
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static TextDocument ReadWithWordNativeWatermarkColor(TextDocument document, string color)
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
            var shape = xml.Descendants(vml + "shape")
                .Single(candidate => candidate.Attribute("id")?.Value == "PowerPlusWaterMarkObject");
            shape.SetAttributeValue("fillcolor", color);
            shape.Element(vml + "fill")!.SetAttributeValue("color", color);
            entry.Delete();
            var replacement = zip.CreateEntry("word/header1.xml", CompressionLevel.Optimal);
            using var writer = new StreamWriter(replacement.Open());
            xml.Save(writer);
            zip.GetEntry("docProps/custom.xml")!.Delete();
        }
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static TextDocument ReadWithMutatedVmlTextSize(TextDocument document, double widthPt, double heightPt)
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
            var shape = xml.Descendants(vml + "shape")
                .Single(shape => shape.Attribute("id")?.Value == "PowerPlusWaterMarkObject");
            var style = shape.Attribute("style")!.Value;
            shape.SetAttributeValue("style", style.Replace(
                "width:468pt;height:117pt",
                $"width:{widthPt:0.##}pt;height:{heightPt:0.##}pt",
                StringComparison.Ordinal));
            entry.Delete();
            var replacement = zip.CreateEntry("word/header1.xml", CompressionLevel.Optimal);
            using var writer = new StreamWriter(replacement.Open());
            xml.Save(writer);
        }
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static TextDocument ReadWithMutatedVmlTextFitShape(TextDocument document, string fitShape)
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
            xml.Descendants(vml + "textpath").Last().SetAttributeValue("fitshape", fitShape);
            entry.Delete();
            var replacement = zip.CreateEntry("word/header1.xml", CompressionLevel.Optimal);
            using var writer = new StreamWriter(replacement.Open());
            xml.Save(writer);
        }
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static TextDocument ReadWithMutatedVmlTextRotation(TextDocument document, double rotationDegrees)
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
            var shape = xml.Descendants(vml + "shape")
                .Single(shape => shape.Attribute("id")?.Value == "PowerPlusWaterMarkObject");
            var style = shape.Attribute("style")!.Value;
            shape.SetAttributeValue("style", style.Replace(
                "rotation:315",
                $"rotation:{rotationDegrees.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                StringComparison.Ordinal));
            entry.Delete();
            var replacement = zip.CreateEntry("word/header1.xml", CompressionLevel.Optimal);
            using var writer = new StreamWriter(replacement.Open());
            xml.Save(writer);
        }
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static TextDocument ReadWithMutatedVmlTextShapeType(TextDocument document)
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
            var shapeType = xml.Descendants(vml + "shapetype").Single();
            shapeType.SetAttributeValue("id", "FreeWCustomWatermarkPath");
            shapeType.SetAttributeValue("path", "m0,0l21600,0e");
            xml.Descendants(vml + "shape")
                .Single(shape => shape.Attribute("id")?.Value == "PowerPlusWaterMarkObject")
                .SetAttributeValue("type", "#FreeWCustomWatermarkPath");
            entry.Delete();
            var replacement = zip.CreateEntry("word/header1.xml", CompressionLevel.Optimal);
            using var writer = new StreamWriter(replacement.Open());
            xml.Save(writer);
        }
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static TextDocument ReadWithMutatedVmlTextPathControls(TextDocument document)
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
            var textPath = xml.Descendants(vml + "textpath").Last();
            textPath.SetAttributeValue("fitpath", "t");
            textPath.SetAttributeValue("trim", "t");
            textPath.SetAttributeValue("xscale", "f");
            textPath.SetAttributeValue("style", "font:italic 72pt Arial;font-family:Arial;font-size:72pt;v-text-kern:t");
            entry.Delete();
            var replacement = zip.CreateEntry("word/header1.xml", CompressionLevel.Optimal);
            using var writer = new StreamWriter(replacement.Open());
            xml.Save(writer);
        }
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static TextDocument ReadWithMutatedVmlTextPathEnabled(TextDocument document, bool enabled)
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
            xml.Descendants(vml + "textpath").Last().SetAttributeValue("on", enabled ? "t" : "f");
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
        var w = XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        var vml = XNamespace.Get("urn:schemas-microsoft-com:vml");
        var shape = xml.Descendants(vml + "shape").Single();
        var shapeType = xml.Descendants(vml + "shapetype").Single();
        var textPath = shape.Element(vml + "textpath");

        xml.Descendants(w + "docPartGallery")
            .Single()
            .Attribute(w + "val")!.Value.Should().Be("Watermarks");
        xml.Descendants(w + "sdtContent").Single().Descendants(w + "noProof").Should().ContainSingle();
        shape.Attribute("style")!.Value.Should().Contain("rotation:0");
        shape.Attribute("fillcolor")!.Value.Should().Be("123456");
        shape.Element(vml + "fill")!.Attribute("opacity")!.Value.Should().Be("0.5");
        textPath!.Attribute("string")!.Value.Should().Be("CONFIDENTIAL");
        textPath.Attribute("on")!.Value.Should().Be("t");
        textPath.Attribute("fitshape")!.Value.Should().Be("t");
        textPath.Attribute("style")!.Value.Should().Contain("font-family:Arial");
        shapeType.Attribute("path")!.Value.Should().Be("m@7,l@8,m@5,21600l@6,21600e");
        shapeType.Element(vml + "formulas")!.Elements(vml + "f")
            .Select(formula => formula.Attribute("eqn")!.Value)
            .Should().Equal(
                "sum #0 0 10800",
                "prod #0 2 1",
                "sum 21600 0 @1",
                "sum 0 0 @2",
                "sum 21600 0 @3",
                "if @0 @3 0",
                "if @0 21600 @1",
                "if @0 0 @2",
                "if @0 @3 21600",
                "mid @5 @6",
                "mid @8 @5",
                "mid @7 @8",
                "mid @6 @7",
                "sum @6 0 @5");
        shapeType.Descendants(vml + "h").Single().Attribute("position")!.Value.Should().Be("#0,bottomRight");
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
    public void NativeVmlTextWatermark_ImportsWordGeneratedSuffixedShapeId()
    {
        var doc = new TextDocument();
        doc.Page.WatermarkOptions = new WatermarkOptions("NATIVE WORD");

        var loaded = ReadWithWordNativeWatermarkShapeId(doc);

        loaded.Page.WatermarkOptions!.Text.Should().Be("NATIVE WORD");
    }

    [Fact]
    public void NativeVmlTextWatermark_ImportsWordNamedSilverColor()
    {
        var doc = new TextDocument();
        doc.Page.WatermarkOptions = new WatermarkOptions("NATIVE WORD");

        var loaded = ReadWithWordNativeWatermarkColor(doc, "silver");

        loaded.Page.WatermarkOptions!.FontColorHex.Should().Be("#C0C0C0");
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
    public void CustomTextWatermarkWithoutWordVisibleVmlPayload_RemainsEditableButIsNotRendered()
    {
        var loaded = ReadWithoutWordVisibleWatermarkPayload(new TextDocument
        {
            Page =
            {
                WatermarkOptions = new WatermarkOptions("CONFIDENTIAL")
                {
                    FontFamily = "Arial",
                    FontColorHex = "#123456",
                    Layout = WatermarkLayout.Diagonal,
                    Opacity = 0.4
                }
            }
        });

        var watermark = loaded.Page.WatermarkOptions;
        watermark.Should().NotBeNull();
        watermark!.Text.Should().Be("CONFIDENTIAL");
        watermark.FontFamily.Should().Be("Arial");
        watermark.NativeVmlTextPathEnabled.Should().BeFalse();
        HasDefaultHeader(loaded).Should().BeFalse(
            "custom metadata without a source VML watermark must remain absent after save");
    }

    [Fact]
    public void NativeVmlTextWatermark_SupplementsFreeWMetadataWithVisibleShapeGeometry()
    {
        var document = new TextDocument();
        document.Page.WatermarkOptions = new WatermarkOptions("AUTHORITATIVE");
        var loaded = ReadWithMutatedVmlTextSize(document, widthPt: 512.5, heightPt: 240.25);

        var watermark = loaded.Page.WatermarkOptions;
        watermark.Should().NotBeNull();
        watermark!.Text.Should().Be("AUTHORITATIVE");
        watermark.NativeVmlTextWidthPt.Should().BeApproximately(512.5, 0.001);
        watermark.NativeVmlTextHeightPt.Should().BeApproximately(240.25, 0.001);

        var rewritten = ReadHeaderXml(loaded);
        var vml = XNamespace.Get("urn:schemas-microsoft-com:vml");
        rewritten.Descendants(vml + "shape")
            .Single(shape => shape.Attribute("id")?.Value == "PowerPlusWaterMarkObject")
            .Attribute("style")!.Value.Should().Contain("width:512.5pt;height:240.25pt");
    }

    [Theory]
    [InlineData("t", true)]
    [InlineData("false", false)]
    public void NativeVmlTextWatermark_PreservesTextPathFitShape(string token, bool expected)
    {
        var loaded = ReadWithMutatedVmlTextFitShape(new TextDocument
        {
            Page = { WatermarkOptions = new WatermarkOptions("AUTHORITATIVE") }
        }, token);

        loaded.Page.WatermarkOptions!.NativeVmlTextFitShape.Should().Be(expected);
        ReadHeaderXml(loaded).Descendants(XNamespace.Get("urn:schemas-microsoft-com:vml") + "textpath")
            .Last().Attribute("fitshape")!.Value.Should().Be(expected ? "t" : "f");
    }

    [Fact]
    public void NativeVmlTextWatermark_PreservesReferencedShapeTypePayload()
    {
        var loaded = ReadWithMutatedVmlTextShapeType(new TextDocument
        {
            Page = { WatermarkOptions = new WatermarkOptions("AUTHORITATIVE") }
        });

        var vml = XNamespace.Get("urn:schemas-microsoft-com:vml");
        var payload = loaded.Page.WatermarkOptions!.NativeVmlTextShapeTypeXml;
        payload.Should().NotBeNull();
        var parsedPayload = XElement.Parse(payload!);
        parsedPayload.Attribute("id")!.Value.Should().Be("FreeWCustomWatermarkPath");
        parsedPayload.Attribute("path")!.Value.Should().Be("m0,0l21600,0e");

        var rewritten = ReadHeaderXml(loaded);
        rewritten.Descendants(vml + "shape")
            .Single(shape => shape.Attribute("id")?.Value == "PowerPlusWaterMarkObject")
            .Attribute("type")!.Value.Should().Be("#FreeWCustomWatermarkPath");
        rewritten.Descendants(vml + "shapetype")
            .Single(shapeType => shapeType.Attribute("id")?.Value == "FreeWCustomWatermarkPath")
            .Attribute("path")!.Value.Should().Be("m0,0l21600,0e");

        var reopened = RoundTrip(loaded);
        XElement.Parse(reopened.Page.WatermarkOptions!.NativeVmlTextShapeTypeXml!)
            .Attribute("path")!.Value.Should().Be("m0,0l21600,0e");
    }

    [Fact]
    public void NativeVmlTextWatermark_PreservesUnmodeledTextPathControls()
    {
        var loaded = ReadWithMutatedVmlTextPathControls(new TextDocument
        {
            Page = { WatermarkOptions = new WatermarkOptions("AUTHORITATIVE") }
        });

        var vml = XNamespace.Get("urn:schemas-microsoft-com:vml");
        var payload = XElement.Parse(loaded.Page.WatermarkOptions!.NativeVmlTextPathXml!);
        payload.Attribute("fitpath")!.Value.Should().Be("t");
        payload.Attribute("trim")!.Value.Should().Be("t");
        payload.Attribute("xscale")!.Value.Should().Be("f");
        payload.Attribute("style")!.Value.Should().Contain("v-text-kern:t");

        var rewritten = ReadHeaderXml(loaded).Descendants(vml + "textpath").Last();
        rewritten.Attribute("fitpath")!.Value.Should().Be("t");
        rewritten.Attribute("trim")!.Value.Should().Be("t");
        rewritten.Attribute("xscale")!.Value.Should().Be("f");
        rewritten.Attribute("style")!.Value.Should().Contain("v-text-kern:t");
        rewritten.Attribute("style")!.Value.Should().Contain("font-family:Calibri");
        rewritten.Attribute("style")!.Value.Should().Contain("font-size:1pt");
        rewritten.Attribute("style")!.Value.Should().NotContain("font:italic");
    }

    [Fact]
    public void NativeVmlTextWatermark_PreservesDisabledTextPathState()
    {
        var loaded = ReadWithMutatedVmlTextPathEnabled(new TextDocument
        {
            Page = { WatermarkOptions = new WatermarkOptions("AUTHORITATIVE") }
        }, enabled: false);

        loaded.Page.WatermarkOptions!.NativeVmlTextPathEnabled.Should().BeFalse();
        ReadHeaderXml(loaded).Descendants(XNamespace.Get("urn:schemas-microsoft-com:vml") + "textpath")
            .Last().Attribute("on")!.Value.Should().Be("f");
    }

    [Fact]
    public void NativeVmlTextWatermark_PreservesExplicitShapeRotation()
    {
        var loaded = ReadWithMutatedVmlTextRotation(new TextDocument
        {
            Page = { WatermarkOptions = new WatermarkOptions("AUTHORITATIVE") }
        }, 300.5);

        loaded.Page.WatermarkOptions!.NativeVmlTextRotationDegrees.Should().BeApproximately(300.5, 0.001);
        ReadHeaderXml(loaded).Descendants(XNamespace.Get("urn:schemas-microsoft-com:vml") + "shape")
            .Single(shape => shape.Attribute("id")?.Value == "PowerPlusWaterMarkObject")
            .Attribute("style")!.Value.Should().Contain("rotation:300.5");
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
        loaded.NativeVmlPictureRecolor.Should().BeTrue();
    }

    [Fact]
    public void PictureWatermark_NativeVmlRecolorFalse_RoundTripsThroughTheHeaderPayload()
    {
        var image = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
        var doc = new TextDocument();
        doc.Page.WatermarkOptions = new WatermarkOptions(string.Empty)
        {
            ImageBytes = image,
            NativeVmlPictureRecolor = false
        };

        var xml = ReadHeaderXml(doc);
        var vml = XNamespace.Get("urn:schemas-microsoft-com:vml");
        var fill = xml.Descendants(vml + "shape")
            .Single(shape => shape.Attribute("id")?.Value == "PowerPlusPictureWaterMarkObject")
            .Element(vml + "fill");

        fill.Should().NotBeNull();
        fill!.Attribute("recolor")!.Value.Should().Be("f");
        RoundTrip(doc).Page.WatermarkOptions!.NativeVmlPictureRecolor.Should().BeFalse();
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
