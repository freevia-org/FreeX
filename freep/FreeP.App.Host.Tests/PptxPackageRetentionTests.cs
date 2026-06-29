using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Free.Shared.Opc;

namespace FreeP.App.Host.Tests;

public sealed class PptxPackageRetentionTests
{
    private const string ExtendedPropsRelType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties";
    private const string CustomPropsRelType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties";
    private const string CustomXmlRelType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml";
    private const string ExternalReviewRelType =
        "http://example.com/freep/relationships/reviewLink";
    private const string UnknownViewRelType =
        "http://example.com/freep/relationships/viewState";
    private const string UnknownSlideMirrorRelType =
        "http://example.com/freep/relationships/slideMirror";

    [Fact]
    public void CoreProperties_RoundTripThroughPptxPackage()
    {
        var presentation = Presentation.CreateEmpty();
        var created = new DateTimeOffset(2026, 6, 29, 9, 30, 0, TimeSpan.Zero);
        var modified = created.AddMinutes(45);
        typeof(Presentation)
            .GetProperty(nameof(Presentation.Properties))!
            .PropertyType
            .Should()
            .Be(typeof(DocumentProperties));

        presentation.Properties.Title = "FreeP title";
        presentation.Properties.Author = "FreeP author";
        presentation.Properties.Subject = "FreeP subject";
        presentation.Properties.Keywords = "freep,pptx,opc";
        presentation.Properties.Comments = "FreeP comments";
        presentation.Properties.LastModifiedBy = "FreeP editor";
        presentation.Properties.Created = created;
        presentation.Properties.Modified = modified;
        presentation.Properties.Category = "FreeP category";
        presentation.Properties.ContentStatus = "Draft";
        presentation.Properties.Language = "en-US";
        presentation.Properties.Version = "2026.06";

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);

        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            var coreXml = LoadXml(archive, "docProps/core.xml");
            coreXml.Root!.Element(DcNs + "title")!.Value.Should().Be("FreeP title");
            coreXml.Root.Element(DcNs + "creator")!.Value.Should().Be("FreeP author");
            coreXml.Root.Element(DcNs + "subject")!.Value.Should().Be("FreeP subject");
            coreXml.Root.Element(CpNs + "keywords")!.Value.Should().Be("freep,pptx,opc");
            coreXml.Root.Element(DcNs + "description")!.Value.Should().Be("FreeP comments");
            coreXml.Root.Element(CpNs + "lastModifiedBy")!.Value.Should().Be("FreeP editor");
            coreXml.Root.Element(DcTermsNs + "created")!.Value.Should().Be("2026-06-29T09:30:00Z");
            coreXml.Root.Element(DcTermsNs + "modified")!.Value.Should().Be("2026-06-29T10:15:00Z");
            coreXml.Root.Element(CpNs + "category")!.Value.Should().Be("FreeP category");
            coreXml.Root.Element(CpNs + "contentStatus")!.Value.Should().Be("Draft");
            coreXml.Root.Element(DcNs + "language")!.Value.Should().Be("en-US");
            coreXml.Root.Element(CpNs + "version")!.Value.Should().Be("2026.06");
        }

        stream.Position = 0;
        var reloaded = PptxPackageReader.Read(stream);
        reloaded.Properties.Title.Should().Be("FreeP title");
        reloaded.Properties.Author.Should().Be("FreeP author");
        reloaded.Properties.Subject.Should().Be("FreeP subject");
        reloaded.Properties.Keywords.Should().Be("freep,pptx,opc");
        reloaded.Properties.Comments.Should().Be("FreeP comments");
        reloaded.Properties.LastModifiedBy.Should().Be("FreeP editor");
        reloaded.Properties.Created.Should().Be(created);
        reloaded.Properties.Modified.Should().Be(modified);
        reloaded.Properties.Category.Should().Be("FreeP category");
        reloaded.Properties.ContentStatus.Should().Be("Draft");
        reloaded.Properties.Language.Should().Be("en-US");
        reloaded.Properties.Version.Should().Be("2026.06");
    }

    [Fact]
    public void ReadWriteRead_RetainsUnmodeledPackagePartsRelationshipsAndContentTypes()
    {
        using var source = BuildPptxWithUnmodeledPackageData();
        var loaded = PptxPackageReader.Read(source);
        loaded.PackageSnapshot.Should().NotBeNull();
        loaded.Slides.Should().HaveCount(1);

        loaded.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 77,
            Name = "Modeled edit",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 914400,
            OffsetYEmu = 914400,
            ExtentCxEmu = 1828800,
            ExtentCyEmu = 914400,
        });

        using var saved = new MemoryStream();
        PptxPackageWriter.Write(loaded, saved);
        var savedBytes = saved.ToArray();

        using (var archive = new ZipArchive(new MemoryStream(savedBytes), ZipArchiveMode.Read))
        {
            ReadText(archive, "docProps/app.xml").Should().Contain("FreeP retention harness");
            ReadText(archive, "docProps/custom.xml").Should().Contain("RetentionMarker");
            ReadText(archive, "customXml/item1.xml").Should().Contain("retain-me");
            ReadText(archive, "customXml/itemProps1.xml").Should().Contain("itemID");
            ReadText(archive, "customXml/payload.freex").Should().Contain("freex-payload");
            ReadBytes(archive, "ppt/customData/viewState.bin").Should().Equal(new byte[] { 0x46, 0x50, 0x52, 0x01 });

            var rootRels = LoadXml(archive, "_rels/.rels");
            Relationship(rootRels, ExtendedPropsRelType, "docProps/app.xml").Should().NotBeNull();
            Relationship(rootRels, CustomPropsRelType, "docProps/custom.xml").Should().NotBeNull();
            Relationship(rootRels, CustomXmlRelType, "customXml/item1.xml").Should().NotBeNull();
            var externalReviewRel = Relationship(rootRels, ExternalReviewRelType, "https://example.com/freep-review");
            externalReviewRel.Should().NotBeNull();
            externalReviewRel!.Attribute("TargetMode")?.Value.Should().Be("External");
            Relationship(rootRels, UnknownSlideMirrorRelType, "ppt/slides/slide1.xml").Should().BeNull();

            var presRels = LoadXml(archive, "ppt/_rels/presentation.xml.rels");
            Relationship(presRels, UnknownViewRelType, "customData/viewState.bin").Should().NotBeNull();

            var contentTypes = LoadXml(archive, "[Content_Types].xml");
            Override(contentTypes, "/docProps/app.xml",
                "application/vnd.openxmlformats-officedocument.extended-properties+xml").Should().NotBeNull();
            Override(contentTypes, "/docProps/custom.xml",
                "application/vnd.openxmlformats-officedocument.custom-properties+xml").Should().NotBeNull();
            Override(contentTypes, "/customXml/itemProps1.xml",
                "application/vnd.openxmlformats-officedocument.customXmlProperties+xml").Should().NotBeNull();
            Override(contentTypes, "/ppt/customData/viewState.bin",
                "application/vnd.example.freep.viewstate").Should().NotBeNull();
            Default(contentTypes, "freex", "application/vnd.example.freep.payload").Should().NotBeNull();
        }

        using var savedRead = new MemoryStream(savedBytes);
        var reloaded = PptxPackageReader.Read(savedRead);
        reloaded.Slides.Should().HaveCount(1);
        reloaded.Slides[0].Shapes.Should().Contain(s => s.Name == "Modeled edit");
    }

    private static MemoryStream BuildPptxWithUnmodeledPackageData()
    {
        var presentation = Presentation.CreateEmpty();
        using var basePackage = new MemoryStream();
        PptxPackageWriter.Write(presentation, basePackage);

        var package = new MemoryStream();
        package.Write(basePackage.ToArray());
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            WriteText(archive, "docProps/app.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"
                            xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">
                  <Application>FreeP retention harness</Application>
                </Properties>
                """);
            WriteText(archive, "docProps/custom.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/custom-properties"
                            xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">
                  <property fmtid="{D5CDD505-2E9C-101B-9397-08002B2CF9AE}" pid="2" name="RetentionMarker">
                    <vt:lpwstr>retain-me</vt:lpwstr>
                  </property>
                </Properties>
                """);
            WriteText(archive, "customXml/item1.xml", """<bag xmlns="urn:freep:test">retain-me</bag>""");
            WriteText(archive, "customXml/itemProps1.xml",
                """<ds:datastoreItem ds:itemID="{11111111-1111-1111-1111-111111111111}" xmlns:ds="http://schemas.openxmlformats.org/officeDocument/2006/customXml"/>""");
            WriteText(archive, "customXml/payload.freex", "freex-payload");
            WriteBytes(archive, "ppt/customData/viewState.bin", new byte[] { 0x46, 0x50, 0x52, 0x01 });

            var rootRels = LoadXml(archive, "_rels/.rels");
            AddRelationship(rootRels, "rIdAppProps", ExtendedPropsRelType, "docProps/app.xml");
            AddRelationship(rootRels, "rIdCustomProps", CustomPropsRelType, "docProps/custom.xml");
            AddRelationship(rootRels, "rIdCustomXml", CustomXmlRelType, "customXml/item1.xml");
            AddRelationship(rootRels, "rIdExternalReview", ExternalReviewRelType, "https://example.com/freep-review", external: true);
            AddRelationship(rootRels, "rIdSlideMirror", UnknownSlideMirrorRelType, "ppt/slides/slide1.xml");
            WriteXml(archive, "_rels/.rels", rootRels);

            var itemRels = new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(RelsNs + "Relationships",
                    new XElement(RelsNs + "Relationship",
                        new XAttribute("Id", "rId1"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps"),
                        new XAttribute("Target", "itemProps1.xml"))));
            WriteXml(archive, "customXml/_rels/item1.xml.rels", itemRels);

            var presRels = LoadXml(archive, "ppt/_rels/presentation.xml.rels");
            AddRelationship(presRels, "rIdUnknownView", UnknownViewRelType, "customData/viewState.bin");
            WriteXml(archive, "ppt/_rels/presentation.xml.rels", presRels);

            var contentTypes = LoadXml(archive, "[Content_Types].xml");
            AddOverride(contentTypes, "/docProps/app.xml",
                "application/vnd.openxmlformats-officedocument.extended-properties+xml");
            AddOverride(contentTypes, "/docProps/custom.xml",
                "application/vnd.openxmlformats-officedocument.custom-properties+xml");
            AddOverride(contentTypes, "/customXml/itemProps1.xml",
                "application/vnd.openxmlformats-officedocument.customXmlProperties+xml");
            AddOverride(contentTypes, "/ppt/customData/viewState.bin",
                "application/vnd.example.freep.viewstate");
            AddDefault(contentTypes, "freex", "application/vnd.example.freep.payload");
            WriteXml(archive, "[Content_Types].xml", contentTypes);
        }

        package.Position = 0;
        return package;
    }

    private static readonly XNamespace RelsNs =
        "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypesNs =
        "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace DcNs = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace DcTermsNs = "http://purl.org/dc/terms/";
    private static readonly XNamespace CpNs =
        "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";

    private static XElement? Relationship(XDocument doc, string type, string target) =>
        doc.Root?.Elements(RelsNs + "Relationship").FirstOrDefault(r =>
            r.Attribute("Type")?.Value == type &&
            r.Attribute("Target")?.Value == target);

    private static XElement? Override(XDocument doc, string partName, string contentType) =>
        doc.Root?.Elements(ContentTypesNs + "Override").FirstOrDefault(o =>
            o.Attribute("PartName")?.Value == partName &&
            o.Attribute("ContentType")?.Value == contentType);

    private static XElement? Default(XDocument doc, string extension, string contentType) =>
        doc.Root?.Elements(ContentTypesNs + "Default").FirstOrDefault(o =>
            o.Attribute("Extension")?.Value == extension &&
            o.Attribute("ContentType")?.Value == contentType);

    private static void AddRelationship(XDocument doc, string id, string type, string target, bool external = false)
    {
        var relationship = new XElement(RelsNs + "Relationship",
            new XAttribute("Id", id),
            new XAttribute("Type", type),
            new XAttribute("Target", target));
        if (external)
            relationship.Add(new XAttribute("TargetMode", "External"));

        doc.Root!.Add(relationship);
    }

    private static void AddOverride(XDocument doc, string partName, string contentType)
    {
        doc.Root!.Add(new XElement(ContentTypesNs + "Override",
            new XAttribute("PartName", partName),
            new XAttribute("ContentType", contentType)));
    }

    private static void AddDefault(XDocument doc, string extension, string contentType)
    {
        doc.Root!.Add(new XElement(ContentTypesNs + "Default",
            new XAttribute("Extension", extension),
            new XAttribute("ContentType", contentType)));
    }

    private static XDocument LoadXml(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path) ?? throw new FileNotFoundException(path);
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static string ReadText(ZipArchive archive, string path) =>
        Encoding.UTF8.GetString(ReadBytes(archive, path));

    private static byte[] ReadBytes(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path) ?? throw new FileNotFoundException(path);
        using var stream = entry.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static void WriteText(ZipArchive archive, string path, string text) =>
        WriteBytes(archive, path, Encoding.UTF8.GetBytes(text));

    private static void WriteXml(ZipArchive archive, string path, XDocument doc)
    {
        var entry = archive.GetEntry(path);
        entry?.Delete();
        entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        doc.Save(stream);
    }

    private static void WriteBytes(ZipArchive archive, string path, byte[] bytes)
    {
        var entry = archive.GetEntry(path);
        entry?.Delete();
        entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(bytes, 0, bytes.Length);
    }
}
