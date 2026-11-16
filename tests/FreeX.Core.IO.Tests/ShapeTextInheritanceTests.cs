using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class ShapeTextInheritanceTests
{
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Xdr = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

    [Fact]
    public void SourceDefaults_AreTypedAndPreserveUnknownXml()
    {
        var body = XElement.Parse("""
            <xdr:txBody xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <a:bodyPr/><a:lstStyle><a:defPPr algn="ctr"><a:defRPr b="1" i="0" sz="1300"><a:latin typeface="+mn-lt"/></a:defRPr><a:buNone/></a:defPPr>
              <a:lvl2pPr algn="r"><a:defRPr b="0"><a:solidFill><a:srgbClr val="123456"/></a:solidFill></a:defRPr></a:lvl2pPr></a:lstStyle>
              <a:p><a:pPr lvl="1" algn="l"><a:defRPr i="1" b="0"/></a:pPr><a:r><a:rPr b="0"/><a:t>One</a:t></a:r><a:br/><a:fld id="{00000000-0000-0000-0000-000000000001}" type="slidenum"><a:t>2</a:t></a:fld></a:p>
            </xdr:txBody>
            """);
        var document = XlsxShapeTextDocumentCodec.Read(body)!;
        document.DefaultParagraph!.Alignment.Should().Be(DrawingShapeTextHAlign.Center);
        document.DefaultParagraph.RunStyle!.Bold.Should().BeTrue();
        document.DefaultParagraph.RunStyle.Italic.Should().BeFalse();
        document.DefaultParagraph.RunStyle.ThemeFont.Should().Be("+mn-lt");
        document.ListLevels.Should().ContainSingle();
        document.ListLevels[0].Level.Should().Be(1);
        document.ListLevels[0].Defaults.RunStyle!.Bold.Should().BeFalse();
        document.ListLevels[0].Defaults.RunStyle!.Color.Should().Be(new CellColor(0x12, 0x34, 0x56));
        var paragraph = document.Paragraphs.Single();
        paragraph.Level.Should().Be(1);
        paragraph.DefaultRunStyle!.Bold.Should().BeFalse();
        paragraph.DefaultRunStyle.Italic.Should().BeTrue();
        paragraph.Inlines[0].Style!.Bold.Should().BeFalse();
        document.PlainText.Should().Be("One\n2");
        XNode.DeepEquals(body, XlsxShapeTextDocumentCodec.Write(document, allowSourceRelationships: true)).Should().BeTrue();
    }

    [Fact]
    public void AuthoredDefaults_SurviveNativeAndXlsxRoundTrips()
    {
        var link = new DrawingObjectHyperlink("https://example.org/default", "External", "help");
        var document = new DrawingShapeTextDocument(
            [new DrawingShapeTextParagraph([new(DrawingShapeTextInlineKind.Run, "Text")],
                DrawingShapeTextHAlign.Right, new DrawingShapeTextRunStyle(Bold: false, Italic: true, Hyperlink: link), 2)],
            new DrawingShapeTextParagraphDefaults(DrawingShapeTextHAlign.Center, new DrawingShapeTextRunStyle(Bold: true, Hyperlink: link)),
            [new DrawingShapeTextListLevel(2, new DrawingShapeTextParagraphDefaults(RunStyle: new DrawingShapeTextRunStyle(Italic: false, ThemeFont: "+mj-lt", Hyperlink: link)))]);
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DrawingShapes.Add(new DrawingShapeModel { Name = "Shape", Anchor = new CellAddress(sheet.Id, 1, 1), ShapeTextDocument = document });
        new DuplicateSheetCommand(sheet.Id).Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        workbook.Sheets[1].DrawingShapes.Single().ShapeTextDocument.Should().BeSameAs(document);
        var native = new NativeJsonAdapter();
        using var buffer = new MemoryStream();
        native.Save(workbook, buffer);
        buffer.Position = 0;
        var reloaded = native.Load(buffer);
        var xlsx = new XlsxFileAdapter();
        using var saved = new MemoryStream();
        xlsx.Save(reloaded, saved);
        saved.Position = 0;
        var actual = xlsx.Load(saved).Sheets.First().DrawingShapes.Single().ShapeTextDocument!;
        actual.DefaultParagraph.Should().Be(document.DefaultParagraph);
        actual.ListLevels.Should().Equal(document.ListLevels);
        actual.Paragraphs.Single().Level.Should().Be(2);
        actual.Paragraphs.Single().Alignment.Should().Be(DrawingShapeTextHAlign.Right);
        actual.Paragraphs.Single().DefaultRunStyle.Should().Be(document.Paragraphs.Single().DefaultRunStyle);
        var xml = XlsxShapeTextDocumentCodec.Write(actual, allowSourceRelationships: true);
        xml.Element(A + "lstStyle")!.Element(A + "lvl3pPr")!.Element(A + "defRPr")!.Attribute("i")!.Value.Should().Be("0");
        xml.Element(A + "p")!.Element(A + "pPr")!.Element(A + "defRPr")!.Attribute("b")!.Value.Should().Be("0");
        xml.Name.Should().Be(Xdr + "txBody");
        saved.Position = 0;
        using var package = SpreadsheetDocument.Open(saved, false);
        new OpenXmlValidator(FileFormatVersions.Microsoft365).Validate(package).Should().BeEmpty();
    }
}
