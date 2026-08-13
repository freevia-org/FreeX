using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using static FreeX.Core.IO.Tests.XlsxPackageTestFixtures;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxPivotPackageRelationshipTests
{
    private const string PivotCacheRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheDefinition";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    [Fact]
    public void Preserve_RemapsWorkbookPivotCacheRelationshipWhenSourceIdCollidesWithGeneratedRelationship()
    {
        using var sourcePackage = CreateSourcePackage();
        using var targetPackage = CreateTargetPackage();
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);

        var context = XlsxSourcePackagePreservationContext.TryCreate(sourceArchive, targetArchive);
        context.Should().NotBeNull();

        var generatedEntriesBeforeMerge = XlsxPackageMetadataMerger.CopyUnknownPackageParts(sourceArchive, targetArchive);
        XlsxPackageMetadataMerger.MergeRelationshipParts(sourceArchive, targetArchive, generatedEntriesBeforeMerge);
        XlsxPivotXmlReferencePreserver.Preserve(context);

        var workbookXml = LoadPackageXml(targetArchive, "xl/workbook.xml");
        var workbookRelsXml = LoadPackageXml(targetArchive, "xl/_rels/workbook.xml.rels");
        var pivotCacheRelId = workbookXml.Root!
            .Element(WorkbookNs + "pivotCaches")!
            .Element(WorkbookNs + "pivotCache")!
            .Attribute(RelNs + "id")!
            .Value;

        pivotCacheRelId.Should().NotBe("rId31");
        var pivotCacheRelationship = workbookRelsXml.Root!
            .Elements(PackageRelNs + "Relationship")
            .Single(relationship => string.Equals(relationship.Attribute("Id")?.Value, pivotCacheRelId, StringComparison.Ordinal));
        pivotCacheRelationship.Attribute("Type")!.Value.Should().Be(PivotCacheRelationshipType);
        pivotCacheRelationship.Attribute("Target")!.Value.Should().Be("pivotCache/pivotCacheDefinition1.xml");
    }

    private static MemoryStream CreateSourcePackage() =>
        CreatePackage(
            ("xl/workbook.xml", """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Pivot" sheetId="1" r:id="rId2" />
                  </sheets>
                  <pivotCaches>
                    <pivotCache cacheId="0" r:id="rId31" />
                  </pivotCaches>
                </workbook>
                """),
            ("xl/_rels/workbook.xml.rels", RelationshipsXml(
                Relationship("rId2", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet", "worksheets/sheet1.xml"),
                Relationship("rId31", PivotCacheRelationshipType, "pivotCache/pivotCacheDefinition1.xml"))),
            ("xl/worksheets/sheet1.xml", """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                           xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheetData />
                  <pivotTableDefinition r:id="rIdPivot" />
                </worksheet>
                """),
            ("xl/worksheets/_rels/sheet1.xml.rels", RelationshipsXml(
                Relationship("rIdPivot", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotTable", "../pivotTables/pivotTable1.xml"))),
            ("xl/pivotCache/pivotCacheDefinition1.xml", "<pivotCacheDefinition xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" />"),
            ("xl/pivotTables/pivotTable1.xml", "<pivotTableDefinition xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" name=\"Pivot\" cacheId=\"0\" />"));

    private static MemoryStream CreateTargetPackage() =>
        CreatePackage(
            ("xl/workbook.xml", """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Pivot" sheetId="1" r:id="rId2" />
                  </sheets>
                </workbook>
                """),
            ("xl/_rels/workbook.xml.rels", RelationshipsXml(
                Relationship("rId2", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet", "worksheets/sheet1.xml"),
                Relationship("rId31", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles", "styles.xml"))),
            ("xl/worksheets/sheet1.xml", "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData /></worksheet>"),
            ("xl/worksheets/_rels/sheet1.xml.rels", RelationshipsXml()));

}
