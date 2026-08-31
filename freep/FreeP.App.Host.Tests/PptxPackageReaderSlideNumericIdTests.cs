using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeP.App.Host.Tests;

public sealed class PptxPackageReaderSlideNumericIdTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace OfficeRelationships =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationships =
        "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string SlideRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide";

    [Fact]
    public void Read_DenseSlideCatalog_PreservesPresentationOrderAndNumericIds()
    {
        const int slideCount = 512;
        var presentationOrder = Enumerable.Range(1, slideCount).Reverse().ToArray();
        var slideIds = presentationOrder.Select(index =>
            new XElement(Presentation + "sldId",
                new XAttribute("id", 10_000 + index),
                new XAttribute(OfficeRelationships + "id", $"rId{index}")));
        var relationships = Enumerable.Range(1, slideCount).Select(index =>
            CreateRelationship($"rId{index}", SlideRelationshipType, $"slides/slide{index}.xml"));

        using var package = CreatePackage(slideIds, relationships);
        var result = PptxPackageReader.Read(package);

        result.Slides.Select(slide => slide.Id)
            .Should().Equal(presentationOrder.Select(index => $"rId{index}"));
        result.Slides.Select(slide => slide.NumericId)
            .Should().Equal(presentationOrder.Select(index => (uint?)(10_000 + index)));
    }

    [Fact]
    public void Read_DuplicateRelationshipIds_UseFirstCaseInsensitiveNumericIdIncludingNullShadowing()
    {
        var slideIds = new[]
        {
            new XElement(Presentation + "sldId",
                new XAttribute(OfficeRelationships + "id", "rId1")),
            CreateSlideId("RID1", "901"),
            CreateSlideId("rId2", "invalid"),
            CreateSlideId("RID2", "902"),
            CreateSlideId("rId3", "303"),
            CreateSlideId("RID3", "903"),
            CreateSlideId("rIdMissing", "404"),
            CreateSlideId("rIdNonSlide", "405"),
            new XElement(Presentation + "sldId", new XAttribute("id", 406)),
        };
        var relationships = new[]
        {
            CreateRelationship("rId1", SlideRelationshipType, "slides/slide1.xml"),
            CreateRelationship("rId2", SlideRelationshipType, "slides/slide2.xml"),
            CreateRelationship("rId3", SlideRelationshipType, "slides/slide3.xml"),
            CreateRelationship(
                "rIdNonSlide",
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme",
                "theme/theme1.xml"),
        };

        using var package = CreatePackage(slideIds, relationships);
        var result = PptxPackageReader.Read(package);

        result.Slides.Select(slide => slide.Id)
            .Should().Equal("rId1", "RID1", "rId2", "RID2", "rId3", "RID3");
        result.Slides.Select(slide => slide.NumericId)
            .Should().Equal(new uint?[] { null, null, null, null, 303u, 303u });
    }

    private static XElement CreateSlideId(string relationshipId, string numericId) =>
        new(Presentation + "sldId",
            new XAttribute("id", numericId),
            new XAttribute(OfficeRelationships + "id", relationshipId));

    private static XElement CreateRelationship(string id, string type, string target) =>
        new(PackageRelationships + "Relationship",
            new XAttribute("Id", id),
            new XAttribute("Type", type),
            new XAttribute("Target", target));

    private static MemoryStream CreatePackage(
        IEnumerable<XElement> slideIds,
        IEnumerable<XElement> relationships)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteDocument(
                archive,
                "_rels/.rels",
                new XDocument(
                    new XElement(PackageRelationships + "Relationships",
                        CreateRelationship(
                            "rIdOfficeDocument",
                            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument",
                            "ppt/presentation.xml"))));
            WriteDocument(
                archive,
                "ppt/_rels/presentation.xml.rels",
                new XDocument(new XElement(PackageRelationships + "Relationships", relationships)));
            WriteDocument(
                archive,
                "ppt/presentation.xml",
                new XDocument(
                    new XElement(Presentation + "presentation",
                        new XAttribute(XNamespace.Xmlns + "r", OfficeRelationships),
                        new XElement(Presentation + "sldSz",
                            new XAttribute("cx", 9_144_000),
                            new XAttribute("cy", 6_858_000)),
                        new XElement(Presentation + "sldIdLst", slideIds))));
        }

        stream.Position = 0;
        return stream;
    }

    private static void WriteDocument(ZipArchive archive, string path, XDocument document)
    {
        using var entry = archive.CreateEntry(path).Open();
        document.Save(entry);
    }
}
