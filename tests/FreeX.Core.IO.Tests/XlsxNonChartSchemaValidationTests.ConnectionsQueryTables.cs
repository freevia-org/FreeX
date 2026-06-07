using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxNonChartSchemaValidationTests
{
    [Fact]
    public void LoadedWorkbookSave_SanitizesInvalidConnectionAndQueryTableSidecarAttributesForSchemaValidity()
    {
        var workbook = new Workbook("ConnectionQueryTableSidecars");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(42));

        using var source = Save(workbook);
        AddInvalidConnectionQueryTablePackage(source);
        SchemaErrors(source).Should().NotBeEmpty();
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 3, 1), new NumberValue(84));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        SchemaErrors(saved)
            .Should()
            .OnlyContain(error => error.Contains("queryTableParts", StringComparison.Ordinal));

        var connections = ReadPackageRootElement(saved, "xl/connections.xml");
        var connection = connections.Element(connections.Name.Namespace + "connection")!;
        connections.Attribute("count").Should().BeNull();
        connection.Attribute("id")!.Value.Should().Be("1");
        connection.Attribute("refreshedVersion")!.Value.Should().Be("0");
        connection.Attribute("deleted").Should().BeNull();
        connection.Attribute("interval").Should().BeNull();

        var queryTable = ReadPackageRootElement(saved, "xl/queryTables/queryTable1.xml");
        queryTable.Attribute("connectionId")!.Value.Should().Be("1");
        queryTable.Attribute("autoFormatId").Should().BeNull();
        queryTable.Attribute("applyNumberFormats").Should().BeNull();

        var worksheet = ReadPackageRootElement(saved, "xl/worksheets/sheet1.xml");
        var queryTableParts = worksheet.Element(worksheet.Name.Namespace + "queryTableParts")!;
        queryTableParts.Attribute("count")!.Value.Should().Be("1");
    }

    private static void AddInvalidConnectionQueryTablePackage(MemoryStream packageStream)
    {
        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);

        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

        var contentTypesXml = LoadPackageXml(archive, "[Content_Types].xml");
        AddContentTypeOverride(
            contentTypesXml,
            contentTypeNs,
            "/xl/connections.xml",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.connections+xml");
        AddContentTypeOverride(
            contentTypesXml,
            contentTypeNs,
            "/xl/queryTables/queryTable1.xml",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.queryTable+xml");
        ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);

        var workbookRelationships = LoadPackageXml(archive, "xl/_rels/workbook.xml.rels");
        workbookRelationships.Root!.Add(new XElement(
            packageRelNs + "Relationship",
            new XAttribute("Id", "rIdFreeXConnections"),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/connections"),
            new XAttribute("Target", "connections.xml")));
        ReplacePackageXml(archive, "xl/_rels/workbook.xml.rels", workbookRelationships);

        ReplacePackageXml(archive, "xl/connections.xml", new XDocument(
            new XElement(
                worksheetNs + "connections",
                new XAttribute("count", "not-a-number"),
                new XElement(
                    worksheetNs + "connection",
                    new XAttribute("id", "not-a-number"),
                    new XAttribute("name", "FreeXConnection"),
                    new XAttribute("deleted", "maybe"),
                    new XAttribute("interval", "not-a-number")))));

        var worksheetRelationshipsPath = "xl/worksheets/_rels/sheet1.xml.rels";
        var worksheetRelationships = archive.GetEntry(worksheetRelationshipsPath) is null
            ? new XDocument(new XElement(packageRelNs + "Relationships"))
            : LoadPackageXml(archive, worksheetRelationshipsPath);
        worksheetRelationships.Root!.Add(new XElement(
            packageRelNs + "Relationship",
            new XAttribute("Id", "rIdFreeXQueryTable"),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/queryTable"),
            new XAttribute("Target", "../queryTables/queryTable1.xml")));
        ReplacePackageXml(archive, worksheetRelationshipsPath, worksheetRelationships);

        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        worksheetXml.Root!.Add(new XElement(
            worksheetNs + "queryTableParts",
            new XAttribute("count", "not-a-number"),
            new XElement(
                worksheetNs + "queryTablePart",
                new XAttribute(relNs + "id", "rIdFreeXQueryTable"))));
        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

        ReplacePackageXml(archive, "xl/queryTables/queryTable1.xml", new XDocument(
            new XElement(
                worksheetNs + "queryTable",
                new XAttribute("name", "FreeXQueryTable"),
                new XAttribute("connectionId", "not-a-number"),
                new XAttribute("autoFormatId", "not-a-number"),
                new XAttribute("applyNumberFormats", "maybe"),
                new XAttribute("applyBorderFormats", "0"),
                new XAttribute("applyFontFormats", "0"),
                new XAttribute("applyPatternFormats", "0"),
                new XAttribute("applyAlignmentFormats", "0"),
                new XAttribute("applyWidthHeightFormats", "0"))));

        packageStream.Position = 0;
    }

    private static void AddContentTypeOverride(
        XDocument contentTypesXml,
        XNamespace contentTypeNs,
        string partName,
        string contentType)
    {
        contentTypesXml.Root!
            .Elements(contentTypeNs + "Override")
            .Where(element => string.Equals(element.Attribute("PartName")?.Value, partName, StringComparison.OrdinalIgnoreCase))
            .Remove();
        contentTypesXml.Root.Add(new XElement(
            contentTypeNs + "Override",
            new XAttribute("PartName", partName),
            new XAttribute("ContentType", contentType)));
    }

}
