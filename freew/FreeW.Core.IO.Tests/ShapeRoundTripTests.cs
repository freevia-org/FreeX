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

    /// <summary>A minimal valid 1×1 PNG, used to exercise the image path alongside shapes.</summary>
    private static byte[] OnePixelPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
}
