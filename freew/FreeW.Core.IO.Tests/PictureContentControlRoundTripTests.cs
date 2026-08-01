using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace FreeW.Core.IO.Tests;

public sealed class PictureContentControlRoundTripTests
{
    private const string StoreItemId = "{11111111-2222-3333-4444-555555555555}";
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace W15 = "http://schemas.microsoft.com/office/word/2012/wordml";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";

    [Fact]
    public void WordPictureControl_PreservesIdentityMetadataImageAndCanonicalPackageXml()
    {
        using var source = BuildPackage();
        AssertOffice2013Valid(source.ToArray());

        var imported = DocxReader.Read(source);
        AssertModel(imported);

        var saved = Write(imported);
        AssertCanonicalPackage(saved);
        AssertOffice2013Valid(saved);

        var reopened = DocxReader.Read(new MemoryStream(saved));
        AssertModel(reopened);

        var secondSave = Write(reopened);
        AssertCanonicalPackage(secondSave);
        AssertOffice2013Valid(secondSave);
    }

    private static void AssertModel(TextDocument document)
    {
        var run = document.Paragraphs.Should().ContainSingle().Subject.Runs.Should().ContainSingle().Subject;
        run.Text.Should().BeEmpty();
        run.Image.Should().NotBeNull();
        run.Image!.Bytes.Should().Equal(PngBytes);
        run.Image.WidthPt.Should().BeApproximately(96, 0.001);
        run.Image.HeightPt.Should().BeApproximately(48, 0.001);

        run.Control.Should().NotBeNull();
        var control = run.Control!;
        control.Kind.Should().Be(ContentControlKind.Picture);
        control.Tag.Should().Be("HeroPicture");
        control.Alias.Should().Be("Hero picture");
        control.LockMode.Should().Be(ContentControlLockMode.ContentLocked);
        control.WordMetadata.Should().Be(new ContentControlWordMetadata(
            Id: "57",
            DataBinding: new ContentControlDataBinding(
                StoreItemId,
                "/ns0:root/ns0:image",
                "xmlns:ns0='urn:freew:picture'"),
            PlaceholderDocPart: "DefaultPlaceholder_22675703",
            ShowingPlaceholder: true,
            Temporary: true,
            Appearance: "boundingBox"));
    }

    private static void AssertCanonicalPackage(byte[] package)
    {
        using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        var xml = LoadXml(zip, "word/document.xml");
        var sdt = xml.Descendants(W + "sdt").Should().ContainSingle().Subject;
        var properties = sdt.Element(W + "sdtPr")!;

        properties.Element(W + "picture").Should().NotBeNull();
        properties.Element(W + "picture")!.IsEmpty.Should().BeTrue();
        properties.Elements(W + "text").Should().BeEmpty();
        properties.Elements(W + "richText").Should().BeEmpty();
        properties.Element(W + "alias")!.Attribute(W + "val")!.Value.Should().Be("Hero picture");
        properties.Element(W + "lock")!.Attribute(W + "val")!.Value.Should().Be("contentLocked");
        properties.Element(W + "placeholder")!.Element(W + "docPart")!
            .Attribute(W + "val")!.Value.Should().Be("DefaultPlaceholder_22675703");
        properties.Element(W + "showingPlcHdr").Should().NotBeNull();
        properties.Element(W + "temporary").Should().NotBeNull();
        properties.Element(W + "id")!.Attribute(W + "val")!.Value.Should().Be("57");
        properties.Element(W + "tag")!.Attribute(W + "val")!.Value.Should().Be("HeroPicture");
        properties.Element(W15 + "appearance")!.Attribute(W15 + "val")!.Value.Should().Be("boundingBox");

        var binding = properties.Element(W + "dataBinding")!;
        binding.Attribute(W + "storeItemID")!.Value.Should().Be(StoreItemId);
        binding.Attribute(W + "xpath")!.Value.Should().Be("/ns0:root/ns0:image");
        binding.Attribute(W + "prefixMappings")!.Value.Should().Be("xmlns:ns0='urn:freew:picture'");

        sdt.Element(W + "sdtContent")!.Element(W + "r")!
            .Descendants(W + "drawing").Should().ContainSingle();
        var embed = sdt.Descendants(XName.Get("blip", "http://schemas.openxmlformats.org/drawingml/2006/main"))
            .Should().ContainSingle().Subject.Attribute(R + "embed")!.Value;
        var relationship = LoadXml(zip, "word/_rels/document.xml.rels").Root!.Elements(Rel + "Relationship")
            .Single(element => element.Attribute("Id")?.Value == embed);
        relationship.Attribute("Type")!.Value.Should().EndWith("/image");
        var mediaPath = "word/" + relationship.Attribute("Target")!.Value;
        using var media = zip.GetEntry(mediaPath)!.Open();
        using var copy = new MemoryStream();
        media.CopyTo(copy);
        copy.ToArray().Should().Equal(PngBytes);
    }

    private static MemoryStream BuildPackage()
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="png" ContentType="image/png"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                </Types>
                """);
            Add(zip, "_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);
            Add(zip, "word/document.xml", $$"""
                <w:document xmlns:w="{{W}}" xmlns:w15="{{W15}}"
                            xmlns:r="{{R}}"
                            xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
                            xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                            xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture"
                            xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                            mc:Ignorable="w15">
                  <w:body><w:p><w:sdt><w:sdtPr>
                    <w:alias w:val="Hero picture"/>
                    <w:lock w:val="contentLocked"/>
                    <w:placeholder><w:docPart w:val="DefaultPlaceholder_22675703"/></w:placeholder>
                    <w:showingPlcHdr/>
                    <w:dataBinding w:prefixMappings="xmlns:ns0='urn:freew:picture'" w:xpath="/ns0:root/ns0:image" w:storeItemID="{{StoreItemId}}"/>
                    <w:temporary/>
                    <w:id w:val="57"/>
                    <w15:appearance w15:val="boundingBox"/>
                    <w:tag w:val="HeroPicture"/>
                    <w:picture/>
                  </w:sdtPr><w:sdtContent><w:r><w:drawing><wp:inline>
                    <wp:extent cx="1219200" cy="609600"/>
                    <wp:docPr id="1" name="Hero picture"/>
                    <a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:pic>
                      <pic:nvPicPr><pic:cNvPr id="0" name="Hero picture"/><pic:cNvPicPr/></pic:nvPicPr>
                      <pic:blipFill><a:blip r:embed="rIdImage1"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>
                      <pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="1219200" cy="609600"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr>
                    </pic:pic></a:graphicData></a:graphic>
                  </wp:inline></w:drawing></w:r></w:sdtContent></w:sdt></w:p><w:sectPr/></w:body>
                </w:document>
                """);
            Add(zip, "word/_rels/document.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdImage1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/image1.png"/>
                </Relationships>
                """);
            Add(zip, "word/media/image1.png", PngBytes);
        }
        stream.Position = 0;
        return stream;
    }

    private static byte[] Write(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static XDocument LoadXml(ZipArchive zip, string path)
    {
        using var stream = zip.GetEntry(path)!.Open();
        return XDocument.Load(stream);
    }

    private static void AssertOffice2013Valid(byte[] package)
    {
        using var stream = new MemoryStream(package);
        using var document = WordprocessingDocument.Open(stream, false);
        var errors = new OpenXmlValidator(FileFormatVersions.Office2013).Validate(document)
            .Select(error => $"{error.Id}: {error.Description}; node={error.Node?.OuterXml}")
            .ToList();
        errors.Should().BeEmpty();
    }

    private static void Add(ZipArchive zip, string path, string text) =>
        Add(zip, path, Encoding.UTF8.GetBytes(text));

    private static void Add(ZipArchive zip, string path, byte[] bytes)
    {
        var entry = zip.CreateEntry(path);
        using var stream = entry.Open();
        stream.Write(bytes);
    }

    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
}
