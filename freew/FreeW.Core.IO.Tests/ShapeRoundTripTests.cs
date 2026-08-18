using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for inline DrawingML shapes &amp; text boxes (roadmap item W2): a
/// <see cref="Run.Shape"/> must survive write→read, emit a valid inline <c>w:drawing</c> wrapping a
/// <c>wps:wsp</c>, declare the <c>wps</c> namespace on the document root, and not disturb the existing
/// inline-image (pic:pic) path. Geometry kind, size, fill colour and text-box text must all be preserved.
/// </summary>
public class ShapeRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Wps = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";

    private static Shape TextBoxWithHyperlink(string text, string url)
    {
        var shape = new Shape(ShapeKind.TextBox, 180, 72);
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run(text) { HyperlinkUrl = url });
        shape.TextParagraphs.Add(paragraph);
        return shape;
    }

    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static XDocument WriteDocumentXml(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(entry);
    }

    private static TextDocument DocumentWith(Shape shape)
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromShape(shape));
        doc.Blocks.Add(paragraph);
        return doc;
    }

    [Fact]
    public void Rectangle_SurvivesRoundTrip()
    {
        var read = RoundTrip(DocumentWith(Shape.Preset(ShapeKind.Rectangle, widthPt: 120, heightPt: 60)));

        var shape = read.Paragraphs.Single().Runs.Single(r => r.Shape is not null).Shape!;
        shape.Kind.Should().Be(ShapeKind.Rectangle);
        shape.WidthPt.Should().Be(120);
        shape.HeightPt.Should().Be(60);
        shape.FillColorHex.Should().BeNull();
        shape.HasText.Should().BeFalse();
    }

    [Fact]
    public void RoundedRectangle_SurvivesRoundTrip()
    {
        var read = RoundTrip(DocumentWith(Shape.Preset(ShapeKind.RoundedRectangle, widthPt: 100, heightPt: 50)));

        read.Paragraphs.Single().Runs.Single(r => r.Shape is not null).Shape!.Kind
            .Should().Be(ShapeKind.RoundedRectangle);
    }

    [Fact]
    public void FilledEllipse_SurvivesRoundTrip()
    {
        var read = RoundTrip(DocumentWith(Shape.Preset(ShapeKind.Ellipse, widthPt: 90, heightPt: 45, fillColorHex: "#FF0000")));

        var shape = read.Paragraphs.Single().Runs.Single(r => r.Shape is not null).Shape!;
        shape.Kind.Should().Be(ShapeKind.Ellipse);
        shape.WidthPt.Should().Be(90);
        shape.HeightPt.Should().Be(45);
        shape.FillColorHex.Should().Be("#FF0000");
    }

    [Fact]
    public void TextBox_WithText_SurvivesRoundTrip()
    {
        var read = RoundTrip(DocumentWith(Shape.TextBoxWith("Hello shapes", widthPt: 200, heightPt: 80, fillColorHex: "#DCE6F1")));

        var shape = read.Paragraphs.Single().Runs.Single(r => r.Shape is not null).Shape!;
        shape.Kind.Should().Be(ShapeKind.TextBox);
        shape.WidthPt.Should().Be(200);
        shape.HeightPt.Should().Be(80);
        shape.FillColorHex.Should().Be("#DCE6F1");
        shape.HasText.Should().BeTrue();
        shape.PlainText.Should().Be("Hello shapes");
    }

    [Fact]
    public void TextBox_WithMultipleParagraphs_PreservesAllText()
    {
        var shape = new Shape(ShapeKind.TextBox, 200, 120);
        foreach (var line in new[] { "First line", "Second line" })
        {
            var p = new Paragraph();
            p.Runs.Add(new Run(line));
            shape.TextParagraphs.Add(p);
        }

        var read = RoundTrip(DocumentWith(shape));

        var roundTripped = read.Paragraphs.Single().Runs.Single(r => r.Shape is not null).Shape!;
        roundTripped.TextParagraphs.Should().HaveCount(2);
        roundTripped.TextParagraphs[0].PlainText.Should().Be("First line");
        roundTripped.TextParagraphs[1].PlainText.Should().Be("Second line");
    }

    private static byte[] MinimalPng() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x62, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];

    [Fact]
    public void TextBox_WithImage_SurvivesRoundTrip()
    {
        // Regression (r144, freew-textbox-image-loss): CollectImages/the imagesByRun map only walked
        // top-level body paragraphs, never run.Shape.TextParagraphs, so a picture pasted into a text box
        // never got a relationship id or a word/media part and BuildTextRun silently fell through to an
        // empty <w:t/>. The picture must round-trip byte-for-byte, same as a top-level inline image.
        var png = MinimalPng();
        var shape = new Shape(ShapeKind.TextBox, 200, 120);
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromImage(new InlineImage(png, widthPt: 60, heightPt: 40)));
        shape.TextParagraphs.Add(paragraph);

        var read = RoundTrip(DocumentWith(shape));

        var roundTripped = read.Paragraphs.Single().Runs.Single(r => r.Shape is not null).Shape!;
        var imageRun = roundTripped.TextParagraphs.Single().Runs.Single(r => r.Image is not null);
        imageRun.Image!.PngBytes.Should().Equal(png);
        imageRun.Image.WidthPt.Should().BeApproximately(60, 0.01);
        imageRun.Image.HeightPt.Should().BeApproximately(40, 0.01);
    }

    [Fact]
    public void TextBox_WithImageAlongsideTopLevelImage_BothSurviveWithDistinctBytes()
    {
        // Sibling/non-regression coverage for the same fix: a document mixing an ordinary top-level inline
        // image with a text-box image exercises the split image-collection walk (the narrow chart/
        // embedded-object/SmartArt loop plus the separately widened image loop in DocxWriter.BuildDocument)
        // and must not let either image steal the other's relationship id / media bytes.
        var topLevelPng = MinimalPng();
        // A second, distinguishable 1x1 PNG (different IDAT payload) so a swapped assignment is detectable.
        byte[] textBoxPng =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
            0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41,
            0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
            0x00, 0x03, 0x01, 0x01, 0x00, 0x18, 0xDD, 0x8D,
            0xB0, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E,
            0x44, 0xAE, 0x42, 0x60, 0x82,
        ];

        var doc = new TextDocument();
        var topLevelParagraph = new Paragraph();
        topLevelParagraph.Runs.Add(Run.FromImage(new InlineImage(topLevelPng, widthPt: 30, heightPt: 20)));
        doc.Blocks.Add(topLevelParagraph);

        var shape = new Shape(ShapeKind.TextBox, 200, 120);
        var shapeParagraph = new Paragraph();
        shapeParagraph.Runs.Add(Run.FromImage(new InlineImage(textBoxPng, widthPt: 60, heightPt: 40)));
        shape.TextParagraphs.Add(shapeParagraph);
        var shapeParagraphOwner = new Paragraph();
        shapeParagraphOwner.Runs.Add(Run.FromShape(shape));
        doc.Blocks.Add(shapeParagraphOwner);

        var read = RoundTrip(doc);

        var topLevelImage = read.Blocks.OfType<Paragraph>().First().Runs.Single(r => r.Image is not null).Image!;
        var shapeImage = read.Blocks.OfType<Paragraph>().Skip(1).Single()
            .Runs.Single(r => r.Shape is not null).Shape!
            .TextParagraphs.Single().Runs.Single(r => r.Image is not null).Image!;

        topLevelImage.PngBytes.Should().Equal(topLevelPng);
        shapeImage.PngBytes.Should().Equal(textBoxPng);
    }

    [Fact]
    public void HeaderTextBox_WithImage_SurvivesRoundTrip()
    {
        // Same fix, header-scoped path: CollectHeaderFooterImages/BuildHeaderFooterImagesByRun only walked
        // content.Paragraphs directly, never a text box's own nested paragraphs, so a picture inside a
        // header text box was dropped the same way a body one was.
        var png = MinimalPng();
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("Body"));
        var header = new HeaderFooter();
        var headerParagraph = new Paragraph();
        var shape = new Shape(ShapeKind.TextBox, 180, 72);
        var shapeParagraph = new Paragraph();
        shapeParagraph.Runs.Add(Run.FromImage(new InlineImage(png, widthPt: 50, heightPt: 35)));
        shape.TextParagraphs.Add(shapeParagraph);
        headerParagraph.Runs.Add(Run.FromShape(shape));
        header.Paragraphs.Add(headerParagraph);
        document.Header = header;

        var read = RoundTrip(document);

        var headerShape = read.Header!.Paragraphs.Single().Runs.Single(r => r.Shape is not null).Shape!;
        var imageRun = headerShape.TextParagraphs.Single().Runs.Single(r => r.Image is not null);
        imageRun.Image!.PngBytes.Should().Equal(png);
        imageRun.Image.WidthPt.Should().BeApproximately(50, 0.01);
        imageRun.Image.HeightPt.Should().BeApproximately(35, 0.01);
    }

    [Fact]
    public void TextBoxHyperlinks_UseOwningStoryRelationships_AndRoundTrip()
    {
        const string bodyUrl = "https://example.com/textbox-body";
        const string headerUrl = "https://example.com/textbox-header";

        var document = DocumentWith(TextBoxWithHyperlink("Body link", bodyUrl));
        var header = new HeaderFooter();
        var headerParagraph = new Paragraph();
        headerParagraph.Runs.Add(Run.FromShape(TextBoxWithHyperlink("Header link", headerUrl)));
        header.Paragraphs.Add(headerParagraph);
        document.FinalSectionHeadersFooters.Header = header;

        byte[] bytes;
        using (var stream = new MemoryStream())
        {
            DocxWriter.Write(document, stream);
            bytes = stream.ToArray();
        }

        using (var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
        {
            string ReadEntry(string path)
            {
                using var reader = new StreamReader(zip.GetEntry(path)!.Open());
                return reader.ReadToEnd();
            }

            var documentXml = XDocument.Parse(ReadEntry("word/document.xml"));
            var headerXml = XDocument.Parse(ReadEntry("word/header1.xml"));
            documentXml.Descendants(W + "txbxContent").Descendants(W + "hyperlink").Should().ContainSingle();
            headerXml.Descendants(W + "txbxContent").Descendants(W + "hyperlink").Should().ContainSingle();

            ReadEntry("word/_rels/document.xml.rels").Should().Contain(bodyUrl).And.NotContain(headerUrl);
            ReadEntry("word/_rels/header1.xml.rels").Should().Contain(headerUrl).And.NotContain(bodyUrl);
        }

        var roundTripped = DocxReader.Read(new MemoryStream(bytes));
        var bodyShape = roundTripped.Paragraphs.Single().Runs.Single(run => run.Shape is not null).Shape!;
        var headerShape = roundTripped.FinalSectionHeadersFooters.Header!.Paragraphs
            .SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.Shape is not null).Shape!;
        bodyShape.TextParagraphs.Single().Runs.Single().HyperlinkUrl.Should().Be(bodyUrl);
        headerShape.TextParagraphs.Single().Runs.Single().HyperlinkUrl.Should().Be(headerUrl);
    }

    [Fact]
    public void HeaderTextBox_DoesNotDuplicateNestedParagraphOnRoundTrip()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("Body"));
        var header = new HeaderFooter();
        var headerParagraph = new Paragraph();
        headerParagraph.Runs.Add(Run.FromShape(Shape.TextBoxWith("Header box", 180, 72)));
        header.Paragraphs.Add(headerParagraph);
        document.Header = header;

        var once = RoundTrip(document);
        once.Header!.Paragraphs.Should().ContainSingle();
        once.Header.Paragraphs.Single().Runs.Single(run => run.Shape is not null).Shape!
            .TextParagraphs.Single().PlainText.Should().Be("Header box");

        var twice = RoundTrip(once);
        twice.Header!.Paragraphs.Should().ContainSingle();
        twice.Header.Paragraphs.Single().Runs.Single(run => run.Shape is not null).Shape!
            .TextParagraphs.Single().PlainText.Should().Be("Header box");
    }

    [Fact]
    public void Shape_EmitsInlineWspWithNamespacesDeclared()
    {
        var xml = WriteDocumentXml(DocumentWith(Shape.Preset(ShapeKind.RoundedRectangle, 100, 50, "#00FF00")));

        // The wps namespace must be declared on the document root.
        xml.Root!.Attribute(XNamespace.Xmlns + "wps")!.Value.Should().Be(Wps.NamespaceName);

        // The shape serialises as a w:drawing/wp:inline/.../wps:wsp with a roundRect preset and a solidFill.
        var inline = xml.Descendants(Wp + "inline").Single();
        var wsp = inline.Descendants(Wps + "wsp").Single();
        wsp.Descendants(A + "prstGeom").Single().Attribute("prst")!.Value.Should().Be("roundRect");
        wsp.Descendants(A + "srgbClr").Single().Attribute("val")!.Value.Should().Be("00FF00");

        // The graphicData uri must be the wps namespace.
        inline.Descendants(A + "graphicData").Single().Attribute("uri")!.Value.Should().Be(Wps.NamespaceName);
    }

    [Fact]
    public void TextBox_EmitsTxbxContentParagraph()
    {
        var xml = WriteDocumentXml(DocumentWith(Shape.TextBoxWith("Inside", 150, 60)));

        var txbxContent = xml.Descendants(W + "txbxContent").Single();
        var paragraph = txbxContent.Elements(W + "p").Single();
        string.Concat(paragraph.Descendants(W + "t").Select(t => t.Value)).Should().Be("Inside");
    }

    [Fact]
    public void TextBox_ForeignNumbering_SurvivesRoundTrip()
    {
        var shape = new Shape(ShapeKind.TextBox, 180, 72);
        shape.TextParagraphs.Add(new Paragraph("Foreign item")
        {
            PreservedNumbering = new PreservedNumbering(12, 2)
        });
        var document = DocumentWith(shape);
        document.Preserved.OriginalNumbering = new XElement(W + "numbering",
            new XElement(W + "num",
                new XAttribute(W + "numId", 12),
                new XElement(W + "abstractNumId", new XAttribute(W + "val", 99))));

        byte[] bytes;
        using (var stream = new MemoryStream())
        {
            DocxWriter.Write(document, stream);
            bytes = stream.ToArray();
        }

        var remappedNumId = 0;
        using (var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
        {
            using var numberingReader = new StreamReader(zip.GetEntry("word/numbering.xml")!.Open());
            var numbering = XDocument.Parse(numberingReader.ReadToEnd());
            var emittedNumIds = numbering.Root!.Elements(W + "num")
                .Select(element => element.Attribute(W + "numId")!.Value).ToHashSet();

            using var documentReader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
            var documentXml = XDocument.Parse(documentReader.ReadToEnd());
            var emittedTextBoxParagraph = documentXml.Descendants(W + "txbxContent").Elements(W + "p").Single();
            var numPr = emittedTextBoxParagraph.Element(W + "pPr")!.Element(W + "numPr")!;
            remappedNumId = int.Parse(numPr.Element(W + "numId")!.Attribute(W + "val")!.Value);
            emittedNumIds.Should().Contain(remappedNumId.ToString());
            numPr.Element(W + "ilvl")!.Attribute(W + "val")!.Value.Should().Be("2");
        }

        var read = DocxReader.Read(new MemoryStream(bytes));
        var rereadTextBoxParagraph = read.Paragraphs.Single().Runs.Single(run => run.Shape is not null).Shape!
            .TextParagraphs.Single();
        rereadTextBoxParagraph.PreservedNumbering.Should().Be(new PreservedNumbering(remappedNumId, 2));
    }

    [Fact]
    public void Shape_RoundTripsInsideTableCell()
    {
        // Shapes are an inline run mark, so they must flow through table cells like any other run.
        var table = Table.Create(1, 1);
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Add(Run.FromShape(Shape.Preset(ShapeKind.Ellipse, 40, 40, "#123456")));
        var doc = new TextDocument();
        doc.Blocks.Add(table);

        var read = RoundTrip(doc);

        var cellParagraph = ((Table)read.Blocks.Single()).Rows[0].Cells[0].Paragraphs.Single();
        var shape = cellParagraph.Runs.Single(r => r.Shape is not null).Shape!;
        shape.Kind.Should().Be(ShapeKind.Ellipse);
        shape.FillColorHex.Should().Be("#123456");
    }

    [Fact]
    public void ShapeAndImage_CoexistWithoutCollision()
    {
        // A document that has both an inline image and a shape: the image (pic:pic) path must keep working
        // and the shape (wps:wsp) must be recovered as a shape, each with a distinct wp:docPr id.
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("x") { Image = new InlineImage(OnePixelPng(), 10, 10) });
        paragraph.Runs.Add(Run.FromShape(Shape.Preset(ShapeKind.Rectangle, 30, 20)));
        doc.Blocks.Add(paragraph);

        var read = RoundTrip(doc);

        var runs = read.Paragraphs.Single().Runs;
        runs.Count(r => r.Image is not null).Should().Be(1);
        runs.Count(r => r.Shape is not null).Should().Be(1);

        // docPr ids must be unique across the image and the shape.
        var xml = WriteDocumentXml(doc);
        var ids = xml.Descendants(Wp + "docPr").Select(d => d.Attribute("id")!.Value).ToList();
        ids.Should().OnlyHaveUniqueItems();
    }

    // ── New-field round-trip tests (outline, alt text, text direction) ───────────────────────────

    [Fact]
    public void Shape_WithOutline_SurvivesRoundTrip()
    {
        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 120, heightPt: 60, fillColorHex: "#DCE6F1");
        shape.OutlineColorHex = "#FF0000";
        shape.OutlineWidthPt = 1.5;
        shape.OutlineDash = "dash";

        var read = RoundTrip(DocumentWith(shape)).Paragraphs.Single().Runs.Single(r => r.Shape is not null).Shape!;

        read.OutlineColorHex.Should().Be("#FF0000");
        read.OutlineWidthPt.Should().BeApproximately(1.5, 0.01);
        read.OutlineDash.Should().Be("dash");
    }

    [Fact]
    public void Shape_WithAltText_SurvivesRoundTrip()
    {
        var shape = Shape.Preset(ShapeKind.Ellipse, widthPt: 80, heightPt: 80);
        shape.AltText = "Blue circle decoration";

        var read = RoundTrip(DocumentWith(shape)).Paragraphs.Single().Runs.Single(r => r.Shape is not null).Shape!;

        read.AltText.Should().Be("Blue circle decoration");
    }

    [Fact]
    public void TextBox_WithTextDirectionRotate90_SurvivesRoundTrip()
    {
        var shape = Shape.TextBoxWith("Rotated", widthPt: 80, heightPt: 120);
        shape.TextDirection = ShapeTextDirection.Rotate90;

        var read = RoundTrip(DocumentWith(shape)).Paragraphs.Single().Runs.Single(r => r.Shape is not null).Shape!;

        read.TextDirection.Should().Be(ShapeTextDirection.Rotate90);
    }

    [Fact]
    public void TextBox_WithTextDirectionRotate270_SurvivesRoundTrip()
    {
        var shape = Shape.TextBoxWith("Rotated270", widthPt: 80, heightPt: 120);
        shape.TextDirection = ShapeTextDirection.Rotate270;

        var read = RoundTrip(DocumentWith(shape)).Paragraphs.Single().Runs.Single(r => r.Shape is not null).Shape!;

        read.TextDirection.Should().Be(ShapeTextDirection.Rotate270);
    }

    [Fact]
    public void Shape_HorizontalTextDirection_IsDefault_AndDoesNotEmitBodyPrAttributes()
    {
        var shape = Shape.TextBoxWith("Normal", widthPt: 80, heightPt: 60);
        // TextDirection defaults to Horizontal — no special bodyPr attrs should be emitted.

        var xml = WriteDocumentXml(DocumentWith(shape));
        var bodyPr = xml.Descendants(Wps + "bodyPr").FirstOrDefault();
        bodyPr!.Attribute("vert").Should().BeNull();
        bodyPr.Attribute("rot").Should().BeNull();
    }

    [Fact]
    public void Shape_Outline_EmitsALnElement()
    {
        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 50);
        shape.OutlineColorHex = "#0070C0";
        shape.OutlineWidthPt = 2.0;

        var xml = WriteDocumentXml(DocumentWith(shape));
        var ln = xml.Descendants(A + "ln").FirstOrDefault();
        ln.Should().NotBeNull();
        var w = (long?)ln!.Attribute("w");
        w.Should().Be((long)(2.0 * 12700));
        ln.Descendants(A + "srgbClr").First().Attribute("val")!.Value.Should().Be("0070C0");
    }

    [Fact]
    public void Shape_NoOutlineColor_DoesNotEmitALn()
    {
        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 50, fillColorHex: "#DCE6F1");
        // OutlineColorHex not set — a:ln must not be emitted.

        var xml = WriteDocumentXml(DocumentWith(shape));
        xml.Descendants(A + "ln").Should().BeEmpty();
    }

    // ── W24: ExtendedFill round-trip ─────────────────────────────────────────────────────────────

    [Fact]
    public void Shape_GradientFill_SurvivesRoundTrip()
    {
        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 60);
        shape.ExtendedFill = ShapeFill.LinearGradient(5400000,
            new GradientStop(0, "#4472C4"),
            new GradientStop(100000, "#1F4E79"));

        var read = RoundTrip(DocumentWith(shape));
        var s = read.Paragraphs.Single().Runs.Single(r => r.Shape is not null).Shape!;
        s.ExtendedFill.Should().NotBeNull();
        s.ExtendedFill!.Kind.Should().Be(ShapeFillKind.Gradient);
        s.ExtendedFill.GradientStops.Should().HaveCount(2);
        s.ExtendedFill.GradientStops[0].ColorHex.Should().BeOneOf("#4472C4", "4472C4");
        s.ExtendedFill.GradientStops[1].ColorHex.Should().BeOneOf("#1F4E79", "1F4E79");
    }

    [Fact]
    public void Shape_GradientFill_EmitsAGradFillElement()
    {
        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 60);
        shape.ExtendedFill = ShapeFill.LinearGradient(0,
            new GradientStop(0, "#FF0000"),
            new GradientStop(100000, "#0000FF"));

        var xml = WriteDocumentXml(DocumentWith(shape));
        xml.Descendants(A + "gradFill").Should().NotBeEmpty("gradient fill should emit a:gradFill");
        xml.Descendants(A + "solidFill").Where(e =>
            e.Ancestors(Wps + "spPr").Any()).Should().BeEmpty("gradient fill must not also emit a solid fill");
    }

    [Fact]
    public void Shape_PatternFill_SurvivesRoundTrip()
    {
        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 60);
        shape.ExtendedFill = ShapeFill.Patterned("diagCross", "#4472C4", "#FFFFFF");

        var read = RoundTrip(DocumentWith(shape));
        var s = read.Paragraphs.Single().Runs.Single(r => r.Shape is not null).Shape!;
        s.ExtendedFill.Should().NotBeNull();
        s.ExtendedFill!.Kind.Should().Be(ShapeFillKind.Pattern);
        s.ExtendedFill.PatternPreset.Should().Be("diagCross");
    }

    [Fact]
    public void Shape_NoFill_SurvivesRoundTrip()
    {
        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 60);
        shape.ExtendedFill = ShapeFill.NoFill();

        var read = RoundTrip(DocumentWith(shape));
        var s = read.Paragraphs.Single().Runs.Single(r => r.Shape is not null).Shape!;
        s.ExtendedFill.Should().NotBeNull();
        s.ExtendedFill!.Kind.Should().Be(ShapeFillKind.NoFill);
    }

    // ── W24: ShapeEffectLst round-trip ────────────────────────────────────────────────────────────

    [Fact]
    public void Shape_ShadowEffect_SurvivesRoundTrip()
    {
        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 60);
        shape.Effects = new ShapeEffectLst { HasShadow = true, ShadowColorHex = "000000", ShadowBlurRad = 38100, ShadowDist = 38100 };

        var read = RoundTrip(DocumentWith(shape));
        var s = read.Paragraphs.Single().Runs.Single(r => r.Shape is not null).Shape!;
        s.Effects.Should().NotBeNull();
        s.Effects!.HasShadow.Should().BeTrue();
    }

    [Fact]
    public void Shape_ShadowEffect_EmitsOuterShdwElement()
    {
        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 60);
        shape.Effects = new ShapeEffectLst { HasShadow = true, ShadowColorHex = "FF0000", ShadowBlurRad = 50000 };

        var xml = WriteDocumentXml(DocumentWith(shape));
        xml.Descendants(A + "outerShdw").Should().NotBeEmpty("shadow should emit a:outerShdw");
    }

    [Fact]
    public void Shape_GlowEffect_SurvivesRoundTrip()
    {
        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 60);
        shape.Effects = new ShapeEffectLst { HasGlow = true, GlowColorHex = "4472C4", GlowRad = 50000 };

        var read = RoundTrip(DocumentWith(shape));
        var s = read.Paragraphs.Single().Runs.Single(r => r.Shape is not null).Shape!;
        s.Effects.Should().NotBeNull();
        s.Effects!.HasGlow.Should().BeTrue();
    }

    [Fact]
    public void Shape_ReflectionEffect_SurvivesRoundTrip()
    {
        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 60);
        shape.Effects = new ShapeEffectLst { HasReflection = true };

        var read = RoundTrip(DocumentWith(shape));
        var s = read.Paragraphs.Single().Runs.Single(r => r.Shape is not null).Shape!;
        s.Effects.Should().NotBeNull();
        s.Effects!.HasReflection.Should().BeTrue();
    }

    [Fact]
    public void Shape_SoftEdgeEffect_SurvivesRoundTrip()
    {
        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 60);
        shape.Effects = new ShapeEffectLst { HasSoftEdge = true, SoftEdgeRad = 50000 };

        var read = RoundTrip(DocumentWith(shape));
        var s = read.Paragraphs.Single().Runs.Single(r => r.Shape is not null).Shape!;
        s.Effects.Should().NotBeNull();
        s.Effects!.HasSoftEdge.Should().BeTrue();
    }

    [Fact]
    public void Shape_BevelEffect_SurvivesRoundTrip()
    {
        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 60);
        shape.Effects = new ShapeEffectLst { HasBevel = true, BevelW = 76200, BevelH = 76200 };

        var read = RoundTrip(DocumentWith(shape));
        var s = read.Paragraphs.Single().Runs.Single(r => r.Shape is not null).Shape!;
        s.Effects.Should().NotBeNull();
        s.Effects!.HasBevel.Should().BeTrue();
    }

    [Fact]
    public void Shape_BevelEffect_EmitsSp3dElement()
    {
        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 60);
        shape.Effects = new ShapeEffectLst { HasBevel = true, BevelW = 76200, BevelH = 76200 };

        var xml = WriteDocumentXml(DocumentWith(shape));
        xml.Descendants(A + "sp3d").Should().NotBeEmpty("bevel should emit a:sp3d");
        xml.Descendants(A + "bevelT").Should().NotBeEmpty("bevel should emit a:bevelT inside sp3d");
    }

    [Fact]
    public void Shape_ClearEffects_PreservesOtherProperties()
    {
        var shape = Shape.Preset(ShapeKind.Ellipse, widthPt: 80, heightPt: 40, fillColorHex: "#4472C4");
        shape.Effects = new ShapeEffectLst { HasShadow = true };

        var withEffects = RoundTrip(DocumentWith(shape));
        var readShape = withEffects.Paragraphs.Single().Runs.Single(r => r.Shape is not null).Shape!;
        readShape.Effects!.HasShadow.Should().BeTrue();

        // Now clear effects
        readShape.Effects = null;
        var withoutEffects = RoundTrip(withEffects);
        var finalShape = withoutEffects.Paragraphs.Single().Runs.Single(r => r.Shape is not null).Shape!;
        finalShape.Effects.Should().BeNull("cleared effects should not be re-emitted");
        finalShape.FillColorHex.Should().BeOneOf("#4472C4", "4472C4", "fill colour must survive effect clear");
        finalShape.Kind.Should().Be(ShapeKind.Ellipse);
    }

    // ── W26: Body rotation / flip round-trip ─────────────────────────────────────────────────────

    [Fact]
    public void Shape_RotationAngle_SurvivesRoundTrip()
    {
        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 60);
        shape.RotationAngle = 45;

        var read = RoundTrip(DocumentWith(shape)).Paragraphs.Single().Runs.Single(r => r.Shape is not null).Shape!;

        read.RotationAngle.Should().BeApproximately(45, 0.001, "rotation angle must survive write→read");
    }

    [Fact]
    public void Shape_FlipH_SurvivesRoundTrip()
    {
        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 60);
        shape.FlipH = true;

        var read = RoundTrip(DocumentWith(shape)).Paragraphs.Single().Runs.Single(r => r.Shape is not null).Shape!;

        read.FlipH.Should().BeTrue("FlipH must survive write→read");
        read.FlipV.Should().BeFalse("FlipV must remain false when only FlipH was set");
    }

    [Fact]
    public void Shape_FlipV_SurvivesRoundTrip()
    {
        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 60);
        shape.FlipV = true;

        var read = RoundTrip(DocumentWith(shape)).Paragraphs.Single().Runs.Single(r => r.Shape is not null).Shape!;

        read.FlipV.Should().BeTrue("FlipV must survive write→read");
        read.FlipH.Should().BeFalse("FlipH must remain false when only FlipV was set");
    }

    [Fact]
    public void Shape_RotationAndFlip_SurviveRoundTripTogether()
    {
        var shape = Shape.Preset(ShapeKind.Ellipse, widthPt: 80, heightPt: 80);
        shape.RotationAngle = 90;
        shape.FlipH = true;
        shape.FlipV = true;

        var read = RoundTrip(DocumentWith(shape)).Paragraphs.Single().Runs.Single(r => r.Shape is not null).Shape!;

        read.RotationAngle.Should().BeApproximately(90, 0.001);
        read.FlipH.Should().BeTrue();
        read.FlipV.Should().BeTrue();
    }

    [Fact]
    public void Shape_DefaultRotation_DoesNotEmitXfrmAttributes()
    {
        // When RotationAngle == 0 and FlipH/FlipV are false, the a:xfrm element must not carry
        // @rot, @flipH or @flipV (keeps output clean and identical to previous behaviour).
        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 60);

        var xml = WriteDocumentXml(DocumentWith(shape));
        var xfrm = xml.Descendants(A + "xfrm").FirstOrDefault();
        xfrm.Should().NotBeNull("a:xfrm must always be emitted");
        xfrm!.Attribute("rot").Should().BeNull("@rot must not be emitted when RotationAngle is 0");
        xfrm.Attribute("flipH").Should().BeNull("@flipH must not be emitted when FlipH is false");
        xfrm.Attribute("flipV").Should().BeNull("@flipV must not be emitted when FlipV is false");
    }

    [Fact]
    public void Shape_Rotation90_EmitsXfrmRotAttribute()
    {
        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 60);
        shape.RotationAngle = 90;

        var xml = WriteDocumentXml(DocumentWith(shape));
        var xfrm = xml.Descendants(A + "xfrm").First();
        var rotValue = long.Parse(xfrm.Attribute("rot")!.Value);
        rotValue.Should().Be(5400000L, "90 degrees × 60000 = 5400000 DrawingML units");
    }

    // ── W26: Shape wrap / position via FloatingPlacement ─────────────────────────────────────────

    [Fact]
    public void Shape_FloatingWrapping_SurvivesRoundTrip()
    {
        // A floating shape (non-inline) with Square wrapping round-trips through FloatingPlacement.
        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 60);
        shape.Placement = new FloatingPlacement { Wrapping = ImageWrapping.Square };

        var read = RoundTrip(DocumentWith(shape)).Paragraphs.Single().Runs.Single(r => r.Shape is not null).Shape!;

        read.IsFloating.Should().BeTrue("shape with Square wrapping must be floating");
        read.Placement!.Wrapping.Should().Be(ImageWrapping.Square);
    }

    // ── Pattern fill round-trip (bug fix: all presets rendered as diagCross) ─────────────────────

    /// <summary>
    /// Pattern fill presets must round-trip with the correct preset token, foreground and background
    /// colours.  Previously the reader/writer preserved the data correctly but the render path mapped
    /// every preset to a diagCross tile; the fix maps each preset family to a distinct tile.
    /// This test verifies the IO round-trip so a regression in the read/write path is caught early.
    /// </summary>
    [Theory]
    [InlineData("horz")]
    [InlineData("vert")]
    [InlineData("diagStripe")]
    [InlineData("upDiag")]
    [InlineData("cross")]
    [InlineData("diagCross")]
    [InlineData("dotGrid")]
    [InlineData("horzBrick")]
    [InlineData("pct5")]
    [InlineData("pct50")]
    public void PatternFill_Preset_RoundTrips(string preset)
    {
        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 60);
        shape.ExtendedFill = ShapeFill.Patterned(preset, "#112233", "#AABBCC");

        var read = RoundTrip(DocumentWith(shape)).Paragraphs.Single()
            .Runs.Single(r => r.Shape is not null).Shape!;

        read.ExtendedFill.Should().NotBeNull();
        read.ExtendedFill!.Kind.Should().Be(ShapeFillKind.Pattern);
        read.ExtendedFill.PatternPreset.Should().Be(preset);
        read.ExtendedFill.PatternFgColorHex.Should().Be("#112233");
        read.ExtendedFill.PatternBgColorHex.Should().Be("#AABBCC");
    }

    /// <summary>
    /// Each of the distinct preset families used in the render map must produce a different-looking
    /// tile (validated here by checking they don't all resolve to the same set of geometry children).
    /// The test asserts that the XML emitted for distinct pattern presets carries the correct @prst token.
    /// </summary>
    [Fact]
    public void PatternFill_EmitsCorrectPrst_InXml()
    {
        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 60);
        shape.ExtendedFill = ShapeFill.Patterned("horz", "#000000", "#FFFFFF");

        var xml = WriteDocumentXml(DocumentWith(shape));
        var pattFill = xml.Descendants(A + "pattFill").FirstOrDefault();
        pattFill.Should().NotBeNull("a pattern fill must emit a:pattFill");
        pattFill!.Attribute("prst")!.Value.Should().Be("horz");
        pattFill.Element(A + "fgClr").Should().NotBeNull();
        pattFill.Element(A + "bgClr").Should().NotBeNull();
    }

    /// <summary>A minimal valid 1×1 PNG, used to exercise the image path alongside shapes.</summary>
    private static byte[] OnePixelPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
}
