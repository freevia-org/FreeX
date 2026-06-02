using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxExcelCompatibilityNormalizerTests
{
    private const string DrawingRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing";
    private const string PivotTableRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotTable";
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

    [Fact]
    public void NormalizeSourcePackageSave_RemovesExcelOpenBlockersAndKeepsRefreshablePivots()
    {
        using var package = CreatePackageWithExcelOpenBlockers();

        XlsxExcelCompatibilityNormalizer.NormalizeSourcePackageSave(package);

        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var workbookXml = LoadPackageXml(archive, "xl/workbook.xml");
        workbookXml.Root!.Element(WorkbookNs + "customWorkbookViews").Should().BeNull();
        archive.GetEntry("xl/calcChain.xml").Should().BeNull();

        var contentTypesXml = LoadPackageXml(archive, "[Content_Types].xml");
        contentTypesXml.Root!
            .Elements(ContentTypeNs + "Override")
            .Select(element => element.Attribute("PartName")?.Value)
            .Should()
            .NotContain("/xl/calcChain.xml")
            .And
            .Contain("/xl/pivotCache/pivotCacheRecords1.xml");

        var sheet5Xml = LoadPackageXml(archive, "xl/worksheets/sheet5.xml");
        sheet5Xml.Root!.Element(WorkbookNs + "customSheetViews").Should().BeNull();
        sheet5Xml.Root!.Element(WorkbookNs + "drawing").Should().BeNull();

        var sheet5RelsXml = LoadPackageXml(archive, "xl/worksheets/_rels/sheet5.xml.rels");
        var sheet5RelationshipTypes = sheet5RelsXml.Root!
            .Elements(PackageRelNs + "Relationship")
            .Select(relationship => relationship.Attribute("Type")?.Value)
            .ToList();
        sheet5RelationshipTypes
            .Should()
            .Contain(PivotTableRelationshipType)
            .And
            .NotContain(DrawingRelationshipType);

        var sheet17Xml = LoadPackageXml(archive, "xl/worksheets/sheet17.xml");
        var phoneCell = sheet17Xml.Root!
            .Descendants(WorkbookNs + "c")
            .Single(cell => cell.Attribute("r")?.Value == "N38");
        phoneCell.Element(WorkbookNs + "f").Should().BeNull();
        phoneCell.Attribute("t")!.Value.Should().Be("str");
        phoneCell.Element(WorkbookNs + "v")!.Value.Should().Be("+389 78 609-030");

        var pivotCacheXml = LoadPackageXml(archive, "xl/pivotCache/pivotCacheDefinition1.xml");
        pivotCacheXml.Root!.Attribute(RelNs + "id")!.Value.Should().Be("rId1");
        pivotCacheXml.Root!.Element(WorkbookNs + "cacheFields")!.Attribute("count")!.Value.Should().Be("2");
        pivotCacheXml.Root!.Element(WorkbookNs + "cacheFields")!.Elements(WorkbookNs + "cacheField").Should().HaveCount(2);
        archive.GetEntry("xl/pivotCache/pivotCacheRecords1.xml").Should().NotBeNull();
        archive.GetEntry("xl/pivotCache/_rels/pivotCacheDefinition1.xml.rels").Should().NotBeNull();

        var pivotTableXml = LoadPackageXml(archive, "xl/pivotTables/pivotTable1.xml");
        pivotTableXml.Root!.Attribute("cacheId")!.Value.Should().Be("0");
        pivotTableXml.Root!.Element(WorkbookNs + "pivotFields")!.Attribute("count")!.Value.Should().Be("2");
        pivotTableXml.Root!.Element(WorkbookNs + "pivotTableStyleInfo").Should().NotBeNull();
    }

    private static MemoryStream CreatePackageWithExcelOpenBlockers() =>
        CreatePackage(
            ("[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="xml" ContentType="application/xml" />
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml" />
                  <Override PartName="/xl/calcChain.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.calcChain+xml" />
                  <Override PartName="/xl/pivotCache/pivotCacheDefinition1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.pivotCacheDefinition+xml" />
                  <Override PartName="/xl/pivotTables/pivotTable1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.pivotTable+xml" />
                  <Override PartName="/xl/drawings/missing.xml" ContentType="application/vnd.openxmlformats-officedocument.drawing+xml" />
                </Types>
                """),
            ("xl/workbook.xml", """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Meeting Notes" sheetId="1" r:id="rId1" />
                    <sheet name="Metrics" sheetId="5" r:id="rId5" />
                    <sheet name="2024" sheetId="8" r:id="rId8" />
                    <sheet name="Force DROP" sheetId="17" r:id="rId17" />
                  </sheets>
                  <customWorkbookViews>
                    <customWorkbookView name="Native View" guid="{11111111-1111-1111-1111-111111111111}" />
                  </customWorkbookViews>
                  <pivotCaches>
                    <pivotCache cacheId="0" r:id="rIdPivotCache" />
                  </pivotCaches>
                </workbook>
                """),
            ("xl/_rels/workbook.xml.rels", RelationshipsXml(
                Relationship("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet", "worksheets/sheet1.xml"),
                Relationship("rId5", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet", "worksheets/sheet5.xml"),
                Relationship("rId8", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet", "worksheets/sheet8.xml"),
                Relationship("rId17", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet", "worksheets/sheet17.xml"),
                Relationship("rIdPivotCache", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheDefinition", "pivotCache/pivotCacheDefinition1.xml"),
                Relationship("rIdCalc", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/calcChain", "calcChain.xml"))),
            ("xl/calcChain.xml", "<calcChain xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" />"),
            ("xl/worksheets/sheet1.xml", WorksheetXml("<drawing r:id=\"rIdDrawing\" />")),
            ("xl/worksheets/_rels/sheet1.xml.rels", RelationshipsXml(
                Relationship("rIdDrawing", DrawingRelationshipType, "../drawings/drawing1.xml"))),
            ("xl/worksheets/sheet5.xml", WorksheetXml("""
                <drawing r:id="rIdDrawing" />
                <customSheetViews>
                  <customSheetView guid="{11111111-1111-1111-1111-111111111111}" />
                </customSheetViews>
                """)),
            ("xl/worksheets/_rels/sheet5.xml.rels", RelationshipsXml(
                Relationship("rIdDrawing", DrawingRelationshipType, "../drawings/drawing1.xml"),
                Relationship("rIdPivot", PivotTableRelationshipType, "../pivotTables/pivotTable1.xml"))),
            ("xl/worksheets/sheet8.xml", WorksheetXml("""
                <sheetData>
                  <row r="1"><c r="A1" t="str"><v>Field A</v></c><c r="B1" t="str"><v>Field B</v></c></row>
                </sheetData>
                """)),
            ("xl/worksheets/sheet17.xml", WorksheetXml("""
                <sheetData>
                  <row r="38"><c r="N38"><f>+389 78 609-030</f></c></row>
                </sheetData>
                """)),
            ("xl/pivotCache/pivotCacheDefinition1.xml", """
                <pivotCacheDefinition xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                                      invalid="1" refreshOnLoad="1">
                  <cacheSource type="worksheet"><worksheetSource ref="A1:B2" sheet="2024" /></cacheSource>
                  <cacheFields>
                    <cacheField name="Field A" numFmtId="0"><sharedItems><s v="A" /></sharedItems></cacheField>
                    <cacheField name="Field B" numFmtId="0"><sharedItems><s v="B" /></sharedItems></cacheField>
                    <cacheField name="Stale Field" numFmtId="0"><sharedItems><s v="C" /></sharedItems></cacheField>
                  </cacheFields>
                </pivotCacheDefinition>
                """),
            ("xl/pivotTables/pivotTable1.xml", """
                <pivotTableDefinition xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                                      name="Metrics" cacheId="0" dataCaption="">
                  <location ref="T3:Y76" firstHeaderRow="0" firstDataRow="3" firstDataCol="0" />
                  <pivotFields>
                    <pivotField><items><item x="0" /></items></pivotField>
                    <pivotField><items><item x="0" /></items></pivotField>
                    <pivotField><items><item x="0" /></items></pivotField>
                  </pivotFields>
                </pivotTableDefinition>
                """),
            ("xl/pivotTables/_rels/pivotTable1.xml.rels", RelationshipsXml(
                Relationship("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheDefinition", "../pivotCache/pivotCacheDefinition1.xml"))),
            ("xl/drawings/drawing1.xml", "<xdr:wsDr xmlns:xdr=\"http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing\" />"));

    private static MemoryStream CreatePackage(params (string Path, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static XDocument LoadPackageXml(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName);
        entry.Should().NotBeNull();
        using var stream = entry!.Open();
        return XDocument.Load(stream);
    }

    private static string WorksheetXml(string body) =>
        $$"""
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                   xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          {{body}}
        </worksheet>
        """;

    private static string RelationshipsXml(params string[] relationships) =>
        $$"""
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          {{string.Join(Environment.NewLine, relationships)}}
        </Relationships>
        """;

    private static string Relationship(string id, string type, string target) =>
        $"""<Relationship Id="{id}" Type="{type}" Target="{target}" />""";
}
