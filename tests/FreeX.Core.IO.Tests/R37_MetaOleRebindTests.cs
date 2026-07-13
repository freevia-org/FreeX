using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Covers R37-meta-2 (HIGH, r36-INCOMPLETE-FIX): the round-36 fix for linked (non-embedded)
/// <c>&lt;oleObject&gt;</c> elements only patched the isolated <c>NormalizeWorksheetRoot</c> pass
/// (<c>ShouldRemoveRelationshipBackedElement</c>). The FULL pipeline
/// (<see cref="XlsxWorksheetOleControlNormalizer.NormalizePackage"/>) also runs
/// <c>RebindOleObjectRelationships</c>, which had no link-attribute guard: for a pure-link
/// <c>&lt;oleObject link="..."&gt;</c> with no <c>r:id</c>, it finds no relationship, falls through
/// its "assign the next unused/newly-created relationship" logic, and either steals an unrelated
/// embedded object's relationship or — when no other relationship exists at all — deletes the
/// linked object outright, silently undoing the round-36 fix once the package round-trips through
/// <c>NormalizePackage</c>.
/// </summary>
public sealed class R37_MetaOleRebindTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    [Fact]
    public void NormalizePackage_PureLinkOleObjectAlone_SurvivesRebindPass()
    {
        // No embedded objects and no relationships file at all: the old code's fallback chain
        // (FindUnusedValidPackageRelationship -> FindNextUnusedPackageRelationship -> create-from-
        // oleObjectParts) bottoms out at null, and the old code unconditionally removed the
        // oleObject in that case. The fix must leave the linked object completely untouched.
        var worksheetXml = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="{WorksheetNs.NamespaceName}" xmlns:r="{RelNs.NamespaceName}">
              <oleObjects>
                <oleObject progId="Excel.Sheet.12" link="C:\data\source.xlsx" shapeId="1030" />
              </oleObjects>
            </worksheet>
            """;

        using var package = XlsxPackageTestFixtures.CreatePackage(
            ("xl/worksheets/sheet1.xml", worksheetXml));

        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            XlsxWorksheetOleControlNormalizer.NormalizePackage(archive);
        }

        var oleObject = ReadSoleOleObject(package);
        oleObject.Should().NotBeNull("a linked OLE object has no embed relationship by design and must survive the full normalize pipeline");
        oleObject!.Attribute("link")!.Value.Should().Be(@"C:\data\source.xlsx");
        oleObject.Attribute(RelNs + "id").Should().BeNull();
    }

    [Fact]
    public void NormalizePackage_PureLinkOleObjectAlongsideEmbeddedObject_DoesNotStealTheEmbeddedObjectsRelationship()
    {
        // A linked oleObject with no r:id sits ahead of a properly-embedded oleObject in document
        // order. An extra, still-unused embeddings part is present so the old code's "assign next
        // unused relationship then fall back to creating one from oleObjectParts" logic has
        // something to hand the linked object — which it must not do, and which must not disturb
        // the embedded object's own correct binding.
        var worksheetXml = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="{WorksheetNs.NamespaceName}" xmlns:r="{RelNs.NamespaceName}">
              <oleObjects>
                <oleObject progId="Excel.Sheet.12" link="C:\data\source.xlsx" shapeId="1030" />
                <oleObject r:id="rIdOle1" progId="Package" shapeId="1031" />
              </oleObjects>
            </worksheet>
            """;

        var relationshipsXml = XlsxPackageTestFixtures.RelationshipsXml(
            XlsxPackageTestFixtures.Relationship(
                "rIdOle1",
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/oleObject",
                "../embeddings/oleObject1.bin"));

        using var package = XlsxPackageTestFixtures.CreatePackage(
            ("xl/worksheets/sheet1.xml", worksheetXml),
            ("xl/worksheets/_rels/sheet1.xml.rels", relationshipsXml),
            ("xl/embeddings/oleObject1.bin", "OLE1"),
            ("xl/embeddings/oleObject2.bin", "OLE2"));

        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            XlsxWorksheetOleControlNormalizer.NormalizePackage(archive);
        }

        var oleObjects = ReadOleObjects(package);
        oleObjects.Should().HaveCount(2, "neither the linked nor the embedded object should be dropped");

        var linkedObject = oleObjects.Single(o => o.Attribute("link") is not null);
        linkedObject.Attribute("link")!.Value.Should().Be(@"C:\data\source.xlsx");
        linkedObject.Attribute(RelNs + "id").Should().BeNull("a linked object must not be given a stolen or fabricated embed relationship");

        var embeddedObject = oleObjects.Single(o => o.Attribute("link") is null);
        embeddedObject.Attribute(RelNs + "id")!.Value.Should().Be(
            "rIdOle1",
            "the embedded object's own relationship id must not be reassigned just because a linked sibling was processed first");

        ResolveOleObjectRelationshipTarget(package, "rIdOle1").Should().Be(
            "../embeddings/oleObject1.bin",
            "the embedded object must keep pointing at its own correct package part");
    }

    [Fact]
    public void NormalizePackage_EmbeddedOleObjectWithDanglingRelationshipId_IsStillRebound()
    {
        // Sibling no-regression case: with no linked object in the picture at all, an embedded
        // oleObject whose r:id no longer resolves to a valid relationship must still be rebound to
        // the available embeddings part exactly as before this fix.
        var worksheetXml = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="{WorksheetNs.NamespaceName}" xmlns:r="{RelNs.NamespaceName}">
              <oleObjects>
                <oleObject r:id="rIdStale" progId="Package" shapeId="1032" />
              </oleObjects>
            </worksheet>
            """;

        using var package = XlsxPackageTestFixtures.CreatePackage(
            ("xl/worksheets/sheet1.xml", worksheetXml),
            ("xl/embeddings/oleObject1.bin", "OLE1"));

        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            XlsxWorksheetOleControlNormalizer.NormalizePackage(archive);
        }

        var oleObject = ReadSoleOleObject(package);
        oleObject.Should().NotBeNull();
        var reboundId = oleObject!.Attribute(RelNs + "id")?.Value;
        reboundId.Should().NotBeNullOrWhiteSpace();
        ResolveOleObjectRelationshipTarget(package, reboundId!).Should().Contain("embeddings/oleObject1.bin");
    }

    private static XElement? ReadSoleOleObject(MemoryStream package)
    {
        var oleObjects = ReadOleObjects(package);
        return oleObjects.Count == 1 ? oleObjects[0] : null;
    }

    private static List<XElement> ReadOleObjects(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        return worksheetXml.Root!
            .Element(WorksheetNs + "oleObjects")?
            .Elements(WorksheetNs + "oleObject")
            .ToList()
            ?? [];
    }

    private static string? ResolveOleObjectRelationshipTarget(MemoryStream package, string relationshipId)
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
