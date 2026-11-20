using System.IO.Compression;
using System.Threading;
using System.Xml.Linq;
using Avalonia.Headless;
using Free.Shared.Pdf;
using Free.Shared.Xps;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Pdf;

namespace FreeW.App.Avalonia.Tests.Printing;

public sealed class PortableXpsWriterTests
{
    private static readonly XNamespace Xps = "http://schemas.microsoft.com/xps/2005/06";
    private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace CoreProperties = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    private static readonly XNamespace DublinCore = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public void WriteToBytes_VectorDocumentProducesRealXpsPackage()
    {
        var document = new PdfContentDocument([
            new PdfContentPage(612, 792, [
                new PdfFillRect(36, 36, 120, 48, new PdfColor(20, 80, 140)),
                new PdfLine(36, 36, 156, 84, PdfColor.Black, 1),
            ])]);

        var bytes = PortableXpsWriter.WriteToBytes(document);

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        archive.GetEntry("[Content_Types].xml").Should().NotBeNull();
        archive.GetEntry("_rels/.rels").Should().NotBeNull();
        archive.GetEntry("FixedDocSeq.fdseq").Should().NotBeNull();
        archive.GetEntry("Documents/1/FixedDocument.fdoc").Should().NotBeNull();
        var page = ReadPart(archive, "Documents/1/Pages/1.fpage");
        page.Name.Should().Be(Xps + "FixedPage");
        page.Attribute("Width")!.Value.Should().Be("816");
        page.Attribute("Height")!.Value.Should().Be("1056");
        page.Attribute(XNamespace.Xml + "lang")!.Value.Should().Be("und");
        var canvas = page.Element(Xps + "Canvas")!;
        canvas.Attribute("RenderTransform")!.Value.Should().Be("1.333333333333,0,0,1.333333333333,0,0");
        canvas.Elements(Xps + "Path").First().Attribute("Fill")!.Value.Should().Be("#14508C");
        archive.GetEntry("docProps/core.xml").Should().BeNull();
        archive.GetEntry("Documents/1/Pages/_rels/1.fpage.rels").Should().BeNull();
    }

    [Fact]
    public void WriteToBytes_PackagesReferencedFontFacesAndDocumentProperties()
    {
        const string fontPart = "/Resources/Fonts/1bb4d991-830c-4651-96e5-d8a25a71e9a3.odttc";
        var fonts = new XpsFontResource(fontPart, [1, 2, 3], 2) { IsRestricted = true };
        var text = new PdfText(30, 40, 12, PdfFontFace.Bold, PdfColor.Black, "{Budget}");
        var secondFontFaceText = text with { X = 45 };
        var options = new XpsWriterOptions
        {
            GlyphOverlays = new Dictionary<PdfText, XpsGlyphRun>(ReferenceEqualityComparer.Instance)
            {
                [text] = new(fonts, "21"),
                [secondFontFaceText] = new(fonts with { FontFaceIndex = 0 }, "21"),
            },
        };
        var document = new PdfContentDocument(
            [
                new PdfContentPage(100, 80,
                    [new PdfRotationGroup(30, 40, 90, [new PdfClipGroup(10, 20, 60, 30, [text])])]),
                new PdfContentPage(80, 100, [text, secondFontFaceText]),
            ],
            new PdfDocumentProperties(Title: " Budget ", Author: "Zoë", Subject: "Planning", Keywords: "future")
            {
                Language = "fr-CA"
            });

        using var archive = new ZipArchive(new MemoryStream(PortableXpsWriter.WriteToBytes(document, options)), ZipArchiveMode.Read);
        var coreRelationship = ReadPart(archive, "_rels/.rels").Elements(Relationships + "Relationship")
            .Should().ContainSingle(relation => (string?)relation.Attribute("Target") == "/docProps/core.xml").Which;
        coreRelationship.Attribute("Type")!.Value.Should().Be(
            "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties");
        var core = ReadPart(archive, "docProps/core.xml");
        core.Name.Should().Be(CoreProperties + "coreProperties");
        core.Element(DublinCore + "title")!.Value.Should().Be("Budget");
        core.Element(DublinCore + "creator")!.Value.Should().Be("Zoë");
        core.Element(DublinCore + "subject")!.Value.Should().Be("Planning");
        core.Element(CoreProperties + "keywords")!.Value.Should().Be("future");
        var types = ReadPart(archive, "[Content_Types].xml").Elements(ContentTypes + "Override").ToArray();
        types.Should().ContainSingle(part => (string?)part.Attribute("PartName") == "/docProps/core.xml").Which
            .Attribute("ContentType")!.Value.Should().Be("application/vnd.openxmlformats-package.core-properties+xml");
        types.Should().ContainSingle(part => (string?)part.Attribute("PartName") == fontPart).Which
            .Attribute("ContentType")!.Value.Should().Be("application/vnd.ms-package.obfuscated-opentype");
        archive.Entries.Should().ContainSingle(entry => entry.FullName == fontPart.TrimStart('/'));

        var first = ReadPart(archive, "Documents/1/Pages/1.fpage");
        first.Attribute(XNamespace.Xml + "lang")!.Value.Should().Be("fr-CA");
        var rotation = first.Element(Xps + "Canvas")!.Element(Xps + "Canvas")!;
        rotation.Attribute("RenderTransform")!.Value.Should().Be("0,1,-1,0,70,10");
        var clip = rotation.Element(Xps + "Canvas")!;
        clip.Attribute("Clip")!.Value.Should().Be("M 10,30 L 70,30 L 70,60 L 10,60 Z");
        var firstGlyph = clip.Element(Xps + "Glyphs")!;
        firstGlyph.Attribute("FontUri")!.Value.Should().Be(fontPart + "#2");
        firstGlyph.Attribute("UnicodeString")!.Value.Should().Be("{}{Budget}");
        firstGlyph.Attribute("Indices")!.Value.Should().Be("21");
        firstGlyph.Attribute("Fill")!.Value.Should().Be("#00000000");
        firstGlyph.Attribute("StyleSimulations").Should().BeNull();
        var second = ReadPart(archive, "Documents/1/Pages/2.fpage");
        second.Descendants(Xps + "Glyphs").Select(run => run.Attribute("FontUri")!.Value)
            .Should().Equal(fontPart + "#2", fontPart);

        foreach (var pageNumber in new[] { 1, 2 })
        {
            AssertResourceRelationship(archive, $"Documents/1/Pages/_rels/{pageNumber}.fpage.rels",
                "required-resource", fontPart);
        }
        AssertResourceRelationship(archive, "Documents/1/_rels/FixedDocument.fdoc.rels", "restricted-font", fontPart);
    }

    [Fact]
    public void Analyze_TextWithoutFontProvesExactMissingDependency()
    {
        var document = new PdfContentDocument([
            new PdfContentPage(612, 792, [new PdfText(36, 756, 12, PdfFontFace.Regular, PdfColor.Black, "text")])]);

        var report = PortableXpsWriter.Analyze(document);

        report.IsExportable.Should().BeFalse();
        report.TextOperationCount.Should().Be(1);
        report.Requirements.Should().Contain(message =>
            message.Contains("embedded XPS font resource", StringComparison.Ordinal));
        var action = () => PortableXpsWriter.WriteToBytes(document);
        action.Should().Throw<XpsUnsupportedContentException>();
    }

    [Fact]
    public async Task AvaloniaExport_TextDocumentUsesRasterPageFallbackInsideXpsPackage()
    {
        await Session.Dispatch(() =>
        {
            var view = new DocumentView();
            view.InsertText("Cyrillic XPS fallback");

            using var stream = new MemoryStream();
            FreeWAvaloniaXpsExport.Save(view, stream);

            using var archive = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
            archive.GetEntry("FixedDocSeq.fdseq").Should().NotBeNull();
            var png = archive.Entries.Should().ContainSingle(entry =>
                entry.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).Which;
            var page = ReadPart(archive, "Documents/1/Pages/1.fpage");
            var brush = page.Descendants(Xps + "ImageBrush").Should().ContainSingle().Which;
            brush.Attribute("ViewboxUnits")!.Value.Should().Be("Absolute");
            brush.Attribute("ImageSource")!.Value.Should().Be("/" + png.FullName);
            AssertResourceRelationship(archive, "Documents/1/Pages/_rels/1.fpage.rels", "required-resource", "/" + png.FullName);
        }, CancellationToken.None);
    }

    private static XElement ReadPart(ZipArchive archive, string path)
    {
        var part = archive.GetEntry(path);
        part.Should().NotBeNull();
        using var stream = part!.Open();
        return XElement.Load(stream);
    }

    private static void AssertResourceRelationship(ZipArchive archive, string path, string type, string target)
    {
        var relation = ReadPart(archive, path).Elements(Relationships + "Relationship").Should().ContainSingle().Which;
        relation.Attribute("Type")!.Value.Should().Be(Xps.NamespaceName + "/" + type);
        relation.Attribute("Target")!.Value.Should().Be(target);
    }
}
