using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class ShapeTextFillOverrideTests
{
    [Theory]
    [InlineData("<a:solidFill><a:schemeClr val=\"accent1\"><a:lumMod val=\"0\"/></a:schemeClr></a:solidFill>")]
    [InlineData("<a:solidFill><a:sysClr val=\"windowText\" lastClr=\"000000\"/></a:solidFill>")]
    [InlineData("<a:solidFill><a:srgbClr val=\"FF0000\"><a:alpha val=\"50000\"/></a:srgbClr></a:solidFill>")]
    public void UnsupportedSolidColorOrTransformsRemainOpaque(string fillXml)
    {
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace xdr = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        var fill = XElement.Parse($"<root xmlns:a=\"{a}\">{fillXml}</root>").Elements().Single();
        var body = new XElement(xdr + "txBody", new XElement(a + "bodyPr"), new XElement(a + "lstStyle"),
            new XElement(a + "p", new XElement(a + "r", new XElement(a + "rPr", fill), new XElement(a + "t", "x"))));
        var style = XlsxShapeTextDocumentCodec.Read(body)!.Paragraphs.Single().Inlines.Single().Style!;
        style.FillOverride!.Kind.Should().Be(DrawingShapeTextFillKind.Unsupported);
        style.Color.Should().BeNull();
        style.ThemeColor.Should().BeNull();
    }

    [Fact]
    public void SourceNoFillAndUnsupportedFillsSurviveNativeAndXlsx()
    {
        var body = XElement.Parse("""
            <xdr:txBody xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <a:bodyPr/><a:lstStyle><a:defPPr><a:defRPr><a:solidFill><a:srgbClr val="123456"/></a:solidFill></a:defRPr></a:defPPr></a:lstStyle>
              <a:p><a:pPr><a:defRPr><a:noFill/></a:defRPr></a:pPr>
                <a:r><a:rPr><a:gradFill><a:gsLst><a:gs pos="0"><a:srgbClr val="FF0000"/></a:gs><a:gs pos="100000"><a:srgbClr val="0000FF"/></a:gs></a:gsLst><a:lin ang="0"/></a:gradFill></a:rPr><a:t>Gradient</a:t></a:r>
                <a:r><a:rPr><a:solidFill><a:srgbClr val="AABBCC"><a:alpha val="50000"/></a:srgbClr></a:solidFill></a:rPr><a:t>Alpha</a:t></a:r>
              </a:p>
            </xdr:txBody>
            """);
        var rich = XlsxShapeTextDocumentCodec.Read(body)!;
        AssertFills(rich);
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DrawingShapes.Add(new DrawingShapeModel { Name = "Shape", Anchor = new CellAddress(sheet.Id, 1, 1), ShapeTextDocument = rich });
        var native = new NativeJsonAdapter();
        using var nativeStream = new MemoryStream();
        native.Save(workbook, nativeStream);
        nativeStream.Position = 0;
        var loaded = native.Load(nativeStream);
        AssertFills(loaded.Sheets.Single().DrawingShapes.Single().ShapeTextDocument!);
        var xlsx = new XlsxFileAdapter();
        using var xlsxStream = new MemoryStream();
        xlsx.Save(loaded, xlsxStream);
        xlsxStream.Position = 0;
        var result = xlsx.Load(xlsxStream).Sheets.Single().DrawingShapes.Single().ShapeTextDocument!;
        AssertFills(result);
        XNode.DeepEquals(body, XlsxShapeTextDocumentCodec.Write(result, allowSourceRelationships: true)).Should().BeTrue();
    }

    [Fact]
    public void AuthoredNoFillWritesAnExplicitElement_AndConflictingSolidIsRejected()
    {
        var noFill = new DrawingShapeTextRunStyle { FillOverride = new(DrawingShapeTextFillKind.NoFill) };
        var document = new DrawingShapeTextDocument([new([new(DrawingShapeTextInlineKind.Run, "x", style: noFill)])]);
        var xml = XlsxShapeTextDocumentCodec.Write(document, allowSourceRelationships: false);
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        xml.Descendants(a + "rPr").Single().Element(a + "noFill").Should().NotBeNull();
        XlsxShapeTextDocumentCodec.Read(xml)!.Paragraphs.Single().Inlines.Single().Style!.FillOverride!.Kind.Should().Be(DrawingShapeTextFillKind.NoFill);
        var conflicting = noFill with { Color = new CellColor(1, 2, 3) };
        var invalid = new DrawingShapeTextDocument([new([new(DrawingShapeTextInlineKind.Run, "x", style: conflicting)])]);
        Action save = () => XlsxShapeTextDocumentCodec.Write(invalid, allowSourceRelationships: false);
        save.Should().Throw<InvalidOperationException>();
    }

    private static void AssertFills(DrawingShapeTextDocument document)
    {
        document.DefaultParagraph!.RunStyle!.Color.Should().Be(new CellColor(0x12, 0x34, 0x56));
        var paragraph = document.Paragraphs.Single();
        paragraph.DefaultRunStyle!.FillOverride!.Kind.Should().Be(DrawingShapeTextFillKind.NoFill);
        paragraph.Inlines[0].Style!.FillOverride!.Kind.Should().Be(DrawingShapeTextFillKind.Unsupported);
        paragraph.Inlines[0].Style!.FillOverride!.SourceXml.Should().Contain("gradFill");
        paragraph.Inlines[1].Style!.FillOverride!.Kind.Should().Be(DrawingShapeTextFillKind.Unsupported);
        paragraph.Inlines[1].Style!.FillOverride!.SourceXml.Should().Contain("alpha");
        paragraph.Inlines[1].Style!.Color.Should().BeNull();
    }
}
