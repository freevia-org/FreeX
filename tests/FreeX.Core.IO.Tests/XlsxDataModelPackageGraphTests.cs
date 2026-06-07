using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxDataModelPackageGraphTests
{
    private const string PackageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string ContentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelationshipsNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string ConnectionsRelationshipType = RelationshipsNs + "/connections";
    private const string ModelRelationshipType = "http://schemas.microsoft.com/office/2007/relationships/model";
    private const string ModelTableRelationshipType = "http://schemas.microsoft.com/office/2007/relationships/modelTable";
    private const string ModelRelationshipContentType = "application/vnd.ms-excel.modelRelationship+xml";
    private const string ModelContentType = "application/vnd.ms-excel.model+xml";

    [Fact]
    public void MergeRelationshipParts_PreservesDataModelWorkbookGraphToGeneratedConnectionsPart()
    {
        using var sourcePackage = CreateDataModelSourcePackage();
        using var targetPackage = XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/connections.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.connections+xml"/>
                </Types>
                """),
            ("xl/workbook.xml", "<workbook/>"),
            ("xl/connections.xml", "<connections/>"),
            ("xl/_rels/workbook.xml.rels", $"""
                <Relationships xmlns="{PackageRelationshipNs}">
                </Relationships>
                """));
        using var source = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var target = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);

        var generatedEntriesBeforeMerge = XlsxPackageMetadataMerger.CopyUnknownPackageParts(source, target);
        XlsxPackageMetadataMerger.MergeContentTypes(source, target);
        XlsxPackageMetadataMerger.MergeRelationshipParts(source, target, generatedEntriesBeforeMerge);

        AssertDataModelGraph(target);
    }

    [Fact]
    public void XlsxFileAdapter_LoadEditSave_PreservesDataModelPackageGraph()
    {
        var workbook = new Workbook("DataModelGraph");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

        var adapter = new XlsxFileAdapter();
        using var sourcePackage = new MemoryStream();
        adapter.Save(workbook, sourcePackage);
        InjectDataModelPackageGraph(sourcePackage);
        sourcePackage.Position = 0;

        var loaded = adapter.Load(sourcePackage);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 1, 2), new TextValue("edited"));

        using var savedPackage = new MemoryStream();
        adapter.Save(loaded, savedPackage);
        savedPackage.Position = 0;
        using var savedArchive = new ZipArchive(savedPackage, ZipArchiveMode.Read);

        AssertDataModelGraph(savedArchive);
    }

    private static MemoryStream CreateDataModelSourcePackage()
    {
        var stream = XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml", $"""
                <Types xmlns="{ContentTypesNs}">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/connections.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.connections+xml"/>
                  <Override PartName="/xl/model/model.xml" ContentType="{ModelContentType}"/>
                  <Override PartName="/xl/model/tables/table1.xml" ContentType="{ModelRelationshipContentType}"/>
                </Types>
                """),
            ("xl/workbook.xml", "<workbook/>"),
            ("xl/connections.xml", ConnectionsXml),
            ("xl/_rels/workbook.xml.rels", WorkbookRelationshipsXml),
            ("xl/_rels/connections.xml.rels", ConnectionsRelationshipsXml),
            ("xl/model/model.xml", ModelXml),
            ("xl/model/_rels/model.xml.rels", ModelRelationshipsXml),
            ("xl/model/tables/table1.xml", ModelTableXml));
        stream.Position = 0;
        return stream;
    }

    private static void InjectDataModelPackageGraph(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true);

        AddContentTypeOverride(archive, "/xl/connections.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.connections+xml");
        AddContentTypeOverride(archive, "/xl/model/model.xml", ModelContentType);
        AddContentTypeOverride(archive, "/xl/model/tables/table1.xml", ModelRelationshipContentType);
        ReplaceXml(archive, "xl/connections.xml", XDocument.Parse(ConnectionsXml));
        ReplaceXml(archive, "xl/_rels/workbook.xml.rels", MergeRelationships(
            LoadXml(archive, "xl/_rels/workbook.xml.rels"),
            XDocument.Parse(WorkbookRelationshipsXml)));
        ReplaceXml(archive, "xl/_rels/connections.xml.rels", XDocument.Parse(ConnectionsRelationshipsXml));
        ReplaceXml(archive, "xl/model/model.xml", XDocument.Parse(ModelXml));
        ReplaceXml(archive, "xl/model/_rels/model.xml.rels", XDocument.Parse(ModelRelationshipsXml));
        ReplaceXml(archive, "xl/model/tables/table1.xml", XDocument.Parse(ModelTableXml));
    }

    private static void AssertDataModelGraph(ZipArchive archive)
    {
        archive.GetEntry("xl/model/model.xml").Should().NotBeNull();
        archive.GetEntry("xl/model/_rels/model.xml.rels").Should().NotBeNull();
        archive.GetEntry("xl/model/tables/table1.xml").Should().NotBeNull();
        archive.GetEntry("xl/connections.xml").Should().NotBeNull();

        var contentTypes = LoadXml(archive, "[Content_Types].xml");
        HasContentTypeOverride(contentTypes, "/xl/model/model.xml", ModelContentType).Should().BeTrue();
        HasContentTypeOverride(contentTypes, "/xl/model/tables/table1.xml", ModelRelationshipContentType).Should().BeTrue();

        var workbookRels = LoadXml(archive, "xl/_rels/workbook.xml.rels");
        HasRelationship(workbookRels, ConnectionsRelationshipType, "connections.xml").Should().BeTrue();
        HasRelationship(workbookRels, ModelRelationshipType, "model/model.xml").Should().BeTrue();

        var connectionsRels = LoadXml(archive, "xl/_rels/connections.xml.rels");
        HasRelationship(connectionsRels, ModelRelationshipType, "model/model.xml").Should().BeTrue();

        var modelRels = LoadXml(archive, "xl/model/_rels/model.xml.rels");
        HasRelationship(modelRels, ModelTableRelationshipType, "tables/table1.xml").Should().BeTrue();
    }

    private static bool HasContentTypeOverride(XDocument document, string partName, string contentType)
    {
        XNamespace ns = ContentTypesNs;
        return document.Root!
            .Elements(ns + "Override")
            .Any(element =>
                string.Equals(element.Attribute("PartName")?.Value, partName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(element.Attribute("ContentType")?.Value, contentType, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasRelationship(XDocument document, string type, string target)
    {
        XNamespace ns = PackageRelationshipNs;
        return document.Root!
            .Elements(ns + "Relationship")
            .Any(element =>
                string.Equals(element.Attribute("Type")?.Value, type, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(element.Attribute("Target")?.Value, target, StringComparison.OrdinalIgnoreCase));
    }

    private static XDocument MergeRelationships(XDocument target, XDocument source)
    {
        XNamespace ns = PackageRelationshipNs;
        var existing = target.Root!
            .Elements(ns + "Relationship")
            .Select(element => element.Attribute("Id")?.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var relationship in source.Root!.Elements(ns + "Relationship"))
        {
            var copy = new XElement(relationship);
            var id = copy.Attribute("Id")?.Value;
            if (!string.IsNullOrWhiteSpace(id) && existing.Contains(id))
                copy.SetAttributeValue("Id", $"rIdDataModel{existing.Count + 1}");
            target.Root!.Add(copy);
        }

        return target;
    }

    private static void AddContentTypeOverride(ZipArchive archive, string partName, string contentType)
    {
        XNamespace ns = ContentTypesNs;
        var document = LoadXml(archive, "[Content_Types].xml");
        if (!HasContentTypeOverride(document, partName, contentType))
        {
            document.Root!.Add(new XElement(
                ns + "Override",
                new XAttribute("PartName", partName),
                new XAttribute("ContentType", contentType)));
            ReplaceXml(archive, "[Content_Types].xml", document);
        }
    }

    private static XDocument LoadXml(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName);
        entry.Should().NotBeNull(entryName);
        using var stream = entry!.Open();
        return XDocument.Load(stream);
    }

    private static void ReplaceXml(ZipArchive archive, string entryName, XDocument document)
    {
        archive.GetEntry(entryName)?.Delete();
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        document.Save(stream);
    }

    private static string WorkbookRelationshipsXml => $"""
        <Relationships xmlns="{PackageRelationshipNs}">
          <Relationship Id="rIdConnections" Type="{ConnectionsRelationshipType}" Target="connections.xml"/>
          <Relationship Id="rIdModel" Type="{ModelRelationshipType}" Target="model/model.xml"/>
        </Relationships>
        """;

    private static string ConnectionsRelationshipsXml => $"""
        <Relationships xmlns="{PackageRelationshipNs}">
          <Relationship Id="rIdConnectionModel" Type="{ModelRelationshipType}" Target="model/model.xml"/>
        </Relationships>
        """;

    private static string ModelRelationshipsXml => $"""
        <Relationships xmlns="{PackageRelationshipNs}">
          <Relationship Id="rIdModelTable" Type="{ModelTableRelationshipType}" Target="tables/table1.xml"/>
        </Relationships>
        """;

    private static string ConnectionsXml => $"""
        <connections xmlns="{SpreadsheetNs}" count="1">
          <connection id="1" name="ModelConnection" type="5" refreshedVersion="8" background="0">
            <modelTables count="1">
              <modelTable id="1" name="FactSales"/>
            </modelTables>
          </connection>
        </connections>
        """;

    private static string ModelXml => """
        <model xmlns="http://schemas.microsoft.com/office/2007/model">
          <tables>
            <table id="1" name="FactSales"/>
          </tables>
        </model>
        """;

    private static string ModelTableXml => """
        <modelTable xmlns="http://schemas.microsoft.com/office/2007/modelTable" id="1" name="FactSales"/>
        """;
}
