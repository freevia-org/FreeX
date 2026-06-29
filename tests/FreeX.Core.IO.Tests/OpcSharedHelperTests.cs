using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using FluentAssertions;
using Free.Shared.Opc;

namespace FreeX.Core.IO.Tests;

public sealed class OpcSharedHelperTests
{
    [Theory]
    [InlineData("ppt/slides/slide1.xml", "ppt/slides/_rels/slide1.xml.rels")]
    [InlineData("/word/document.xml", "word/_rels/document.xml.rels")]
    [InlineData("workbook.xml", "_rels/workbook.xml.rels")]
    public void GetRelationshipPartPath_ReturnsSiblingRelsZipEntry(string partPath, string expected)
    {
        OpcPathHelper.GetRelationshipPartPath(partPath).Should().Be(expected);
    }

    [Theory]
    [InlineData("ppt/slides", "../media/image1.png", "ppt/media/image1.png")]
    [InlineData("ppt/slides", "/docProps/core.xml", "docProps/core.xml")]
    [InlineData("xl/worksheets", "../drawings/./drawing1.xml", "xl/drawings/drawing1.xml")]
    public void ResolveRelativeZipPath_CollapsesDotSegments(string baseDirectory, string target, string expected)
    {
        OpcPathHelper.ResolveRelativeZipPath(baseDirectory, target).Should().Be(expected);
    }

    [Theory]
    [InlineData("/word/charts", "../media/image1.png", "/word/media/image1.png")]
    [InlineData("/word/charts", "/docProps/core.xml", "/docProps/core.xml")]
    [InlineData("/", "../escaped.xml", null)]
    public void ResolveAbsolutePartName_PreservesAbsolutePartNameConvention(
        string baseFolder,
        string target,
        string? expected)
    {
        OpcPathHelper.ResolveAbsolutePartName(baseFolder, target).Should().Be(expected);
    }

    [Theory]
    [InlineData("png", "image/png")]
    [InlineData(".mp4", "video/mp4")]
    [InlineData("ogg", "audio/ogg")]
    [InlineData("aac", "audio/aac")]
    [InlineData("tif", "image/tiff")]
    public void TryGetDefaultContentType_CoversSharedMediaDefaults(string extension, string expected)
    {
        OpcMediaTypes.TryGetDefaultContentType(extension, out var contentType).Should().BeTrue();
        contentType.Should().Be(expected);
    }

    [Fact]
    public void RelationshipDocument_AddUnique_PreservesRelationshipAndAvoidsIdCollisions()
    {
        var relationships = new OpcRelationshipDocument();
        relationships.Add("rId1", "type/known", "target.xml");
        relationships.AddUnique("rId1", "type/preserved", "custom.xml");
        relationships.AddUnique("rId2", "type/preserved", "custom.xml");

        var xml = relationships.ToXDocument();
        var entries = xml.Root!.Elements(OpcRelationships.Namespace + "Relationship").ToList();

        entries.Should().HaveCount(2);
        entries[1].Attribute("Id")!.Value.Should().Be("rIdPreserved1");
        entries[1].Attribute("Type")!.Value.Should().Be("type/preserved");
        entries[1].Attribute("Target")!.Value.Should().Be("custom.xml");
    }

    [Fact]
    public void LoadXml_RejectsDtdsThroughSharedHardenedReader()
    {
        using var stream = ToStream("""
            <!DOCTYPE root [ <!ENTITY x "blocked"> ]>
            <root>&x;</root>
            """);

        Action act = () => OpcXml.LoadXml(stream);

        act.Should().Throw<XmlException>();
    }

    [Fact]
    public void LoadXml_WithLoadOptions_PreservesWhitespaceNodes()
    {
        using var stream = ToStream("""
            <root>
              <child />
            </root>
            """);

        var document = OpcXml.LoadXml(stream, LoadOptions.PreserveWhitespace);

        document.Root!.Nodes().OfType<XText>().Should().NotBeEmpty();
    }

    [Fact]
    public void ReplaceXmlEntry_DeletesDuplicateZipEntriesBeforeWritingReplacement()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteText(archive, "xl/workbook.xml", "<old />");
            WriteText(archive, "xl/workbook.xml", "<stale />");
        }

        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            OpcXml.ReplaceXmlEntry(
                archive,
                "xl/workbook.xml",
                new XDocument(new XElement("replacement")));
        }

        stream.Position = 0;
        using var readArchive = new ZipArchive(stream, ZipArchiveMode.Read);
        readArchive.Entries.Where(e => e.FullName == "xl/workbook.xml").Should().ContainSingle();
        using var entryStream = readArchive.GetEntry("xl/workbook.xml")!.Open();
        OpcXml.LoadXml(entryStream).Root!.Name.LocalName.Should().Be("replacement");
    }

    [Fact]
    public void CoreDocumentProperties_BuildAndRead_RoundTripsSharedOpcFields()
    {
        var created = new DateTimeOffset(2026, 6, 28, 9, 10, 11, TimeSpan.Zero);
        var modified = new DateTimeOffset(2026, 6, 28, 10, 11, 12, TimeSpan.Zero);
        var properties = new CoreDocumentProperties(
            Title: "Quarterly Plan",
            Author: "FreeX",
            Subject: "Shared doc properties",
            Keywords: "opc,dedup",
            Comments: "Round-trip through shared helper",
            LastModifiedBy: "Codex",
            Created: created,
            Modified: modified,
            Category: "Planning",
            ContentStatus: "Draft",
            Language: "en-US",
            Version: "2026.06");

        var document = OpcDocumentProperties.BuildCorePropertiesDocument(
            properties,
            includeDcmiTypeNamespace: true,
            includeXmlDeclaration: true);

        document.Declaration.Should().NotBeNull();
        document.Root!.Attribute(XNamespace.Xmlns + "dcmitype")!.Value
            .Should()
            .Be(OpcDocumentProperties.DublinCoreTypeNamespace.NamespaceName);
        document.Root.Element(OpcDocumentProperties.DublinCoreTermsNamespace + "created")!
            .Attribute(OpcDocumentProperties.XmlSchemaInstanceNamespace + "type")!
            .Value
            .Should()
            .Be("dcterms:W3CDTF");
        OpcDocumentProperties.ReadCoreProperties(document).Should().Be(properties);
    }

    [Fact]
    public void ExtendedDocumentProperties_BuildAndRead_RoundTripsSharedOpcFields()
    {
        var properties = new ExtendedDocumentProperties(
            Application: "Microsoft Excel",
            Company: "FreeX Test Lab",
            Manager: "Fidelity",
            PresentationFormat: "Workbook",
            Template: "SchemaTemplate.xltx");

        var document = OpcDocumentProperties.BuildExtendedPropertiesDocument(
            properties,
            includeXmlDeclaration: true);

        document.Declaration.Should().NotBeNull();
        OpcDocumentProperties.ReadExtendedProperties(document).Should().Be(properties);
    }

    [Fact]
    public void PreservePropertyElements_CopiesOnlyRequestedOpcPropertyElements()
    {
        var source = new XElement(
            OpcDocumentProperties.CorePropertiesNamespace + "coreProperties",
            new XElement(OpcDocumentProperties.DublinCoreNamespace + "title", "source title"),
            new XElement(OpcDocumentProperties.DublinCoreNamespace + "subject", "source subject"),
            new XElement(OpcDocumentProperties.CorePropertiesNamespace + "category", "source category"));
        var target = new XElement(
            OpcDocumentProperties.CorePropertiesNamespace + "coreProperties",
            new XElement(OpcDocumentProperties.DublinCoreNamespace + "title", "target title"));

        var changed = OpcDocumentProperties.PreservePropertyElements(
            source,
            target,
            OpcDocumentProperties.WorkbookStableCorePropertyElementNames);

        changed.Should().BeTrue();
        target.Element(OpcDocumentProperties.DublinCoreNamespace + "title")!.Value.Should().Be("target title");
        target.Element(OpcDocumentProperties.DublinCoreNamespace + "subject")!.Value.Should().Be("source subject");
        target.Element(OpcDocumentProperties.CorePropertiesNamespace + "category")!.Value.Should().Be("source category");
    }

    private static MemoryStream ToStream(string xml) =>
        new(Encoding.UTF8.GetBytes(xml), writable: false);

    private static void WriteText(ZipArchive archive, string path, string text)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(text);
    }
}
