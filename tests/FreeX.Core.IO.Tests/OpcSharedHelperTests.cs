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

    private static MemoryStream ToStream(string xml) =>
        new(Encoding.UTF8.GetBytes(xml), writable: false);

    private static void WriteText(ZipArchive archive, string path, string text)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(text);
    }
}
