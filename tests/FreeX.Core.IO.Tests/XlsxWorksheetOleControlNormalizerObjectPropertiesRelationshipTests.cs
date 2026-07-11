using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Covers <c>RebindObjectPropertiesRelationships</c> in <see cref="XlsxWorksheetOleControlNormalizer"/>:
/// when an OLE object's &lt;objectPr&gt; relationship id no longer resolves to a valid drawing
/// relationship, the fallback assignment must not hand out the same drawing relationship to two
/// different OLE objects (R26-io-comments-drawings-deep-3).
/// </summary>
public sealed class XlsxWorksheetOleControlNormalizerObjectPropertiesRelationshipTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    [Fact]
    public void RebindObjectPropertiesRelationships_TwoDanglingObjectPr_BindToDistinctDrawingRelationships()
    {
        using var package = CreatePackage(
            objectPr1RelationshipId: "rIdMissingDrawing1",
            objectPr2RelationshipId: "rIdMissingDrawing2");

        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            XlsxWorksheetOleControlNormalizer.NormalizePackage(archive);
        }

        var (firstObjectPrId, secondObjectPrId) = ReadObjectPropertiesRelationshipIds(package);

        firstObjectPrId.Should().NotBeNullOrWhiteSpace();
        secondObjectPrId.Should().NotBeNullOrWhiteSpace();
        secondObjectPrId.Should().NotBe(
            firstObjectPrId,
            "each OLE object's preview picture must be bound to its own drawing relationship, not the same one");

        var firstTarget = ResolveDrawingRelationshipTarget(package, firstObjectPrId!);
        var secondTarget = ResolveDrawingRelationshipTarget(package, secondObjectPrId!);
        firstTarget.Should().NotBe(secondTarget, "the two OLE objects must not share the same preview drawing part");
        new[] { firstTarget, secondTarget }.Should().BeEquivalentTo(["../drawings/drawing1.xml", "../drawings/drawing2.xml"]);
    }

    [Fact]
    public void RebindObjectPropertiesRelationships_AlreadyValidDistinctIds_AreLeftUnchanged()
    {
        using var package = CreatePackage(
            objectPr1RelationshipId: "rIdDrawingA",
            objectPr2RelationshipId: "rIdDrawingB");

        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            XlsxWorksheetOleControlNormalizer.NormalizePackage(archive);
        }

        var (firstObjectPrId, secondObjectPrId) = ReadObjectPropertiesRelationshipIds(package);

        firstObjectPrId.Should().Be("rIdDrawingA");
        secondObjectPrId.Should().Be("rIdDrawingB");
        ResolveDrawingRelationshipTarget(package, firstObjectPrId!).Should().Be("../drawings/drawing1.xml");
        ResolveDrawingRelationshipTarget(package, secondObjectPrId!).Should().Be("../drawings/drawing2.xml");
    }

    private static MemoryStream CreatePackage(string objectPr1RelationshipId, string objectPr2RelationshipId)
    {
        var worksheetXml = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <oleObjects>
                <oleObject r:id="rIdOle1" shapeId="1025">
                  <objectPr r:id="{objectPr1RelationshipId}" />
                </oleObject>
                <oleObject r:id="rIdOle2" shapeId="1026">
                  <objectPr r:id="{objectPr2RelationshipId}" />
                </oleObject>
              </oleObjects>
            </worksheet>
            """;

        var worksheetRelationshipsXml = XlsxPackageTestFixtures.RelationshipsXml(
            XlsxPackageTestFixtures.Relationship(
                "rIdOle1",
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/oleObject",
                "../embeddings/oleObject1.bin"),
            XlsxPackageTestFixtures.Relationship(
                "rIdOle2",
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/oleObject",
                "../embeddings/oleObject2.bin"),
            XlsxPackageTestFixtures.Relationship(
                "rIdDrawingA",
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing",
                "../drawings/drawing1.xml"),
            XlsxPackageTestFixtures.Relationship(
                "rIdDrawingB",
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing",
                "../drawings/drawing2.xml"));

        const string drawingXml = """<xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing" />""";

        return XlsxPackageTestFixtures.CreatePackage(
            ("xl/worksheets/sheet1.xml", worksheetXml),
            ("xl/worksheets/_rels/sheet1.xml.rels", worksheetRelationshipsXml),
            ("xl/embeddings/oleObject1.bin", "OLE1"),
            ("xl/embeddings/oleObject2.bin", "OLE2"),
            ("xl/drawings/drawing1.xml", drawingXml),
            ("xl/drawings/drawing2.xml", drawingXml));
    }

    private static (string? First, string? Second) ReadObjectPropertiesRelationshipIds(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var oleObjects = worksheetXml.Root!.Element(WorksheetNs + "oleObjects")!.Elements(WorksheetNs + "oleObject").ToList();
        oleObjects.Should().HaveCount(2);

        var first = oleObjects[0].Element(WorksheetNs + "objectPr")?.Attribute(RelNs + "id")?.Value;
        var second = oleObjects[1].Element(WorksheetNs + "objectPr")?.Attribute(RelNs + "id")?.Value;
        return (first, second);
    }

    private static string? ResolveDrawingRelationshipTarget(MemoryStream package, string relationshipId)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var relationshipsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/_rels/sheet1.xml.rels");
        return relationshipsXml.Root!
            .Elements(PackageRelNs + "Relationship")
            .Where(relationship => relationship.Attribute("Id")?.Value == relationshipId)
            .Select(relationship => relationship.Attribute("Target")?.Value)
            .SingleOrDefault();
    }
}
