using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public partial class FileAdapterSmokeTests
{
    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_RebindsWorkbookExtensionListPackageRelationships()
    {
        var workbook = new Workbook("WorkbookExtPackageRel");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("workbook ext rel"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        var sourcePackageRelationshipId = AddWorkbookExtensionListPackageRelationship(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace x15Ns = "http://schemas.microsoft.com/office/spreadsheetml/2010/11/main";

        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        var extensionRelationshipId = workbookXml.Root!
            .Element(workbookNs + "extLst")!
            .Element(workbookNs + "ext")!
            .Element(x15Ns + "packageRef")!
            .Attribute(relNs + "id")!
            .Value;
        extensionRelationshipId.Should().NotBe(sourcePackageRelationshipId);

        var workbookRelsXml = LoadPackageXml(archive.GetEntry("xl/_rels/workbook.xml.rels")!);
        workbookRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Should()
            .ContainSingle(relationship =>
                (string?)relationship.Attribute("Id") == extensionRelationshipId &&
                (string?)relationship.Attribute("Type") == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/package" &&
                (string?)relationship.Attribute("Target") == "metadata/freexWorkbookExt.xml");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorksheetExtensionListPackageRelationships()
    {
        var workbook = new Workbook("WorksheetExtPackageRel");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("worksheet ext rel"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetExtensionListPackageRelationship(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 2, 1), new TextValue("edited"));
        loadedSheet.Hyperlinks[new CellAddress(loadedSheet.Id, 1, 1)] = "https://example.invalid/generated-link";

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace x15Ns = "http://schemas.microsoft.com/office/spreadsheetml/2010/11/main";

        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var extensionRelationshipId = worksheetXml.Root!
            .Element(worksheetNs + "extLst")!
            .Element(worksheetNs + "ext")!
            .Element(x15Ns + "packageRef")!
            .Attribute(relNs + "id")!
            .Value;
        var worksheetRelsXml = LoadPackageXml(archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels")!);
        worksheetRelsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Should()
            .ContainSingle(relationship =>
                (string?)relationship.Attribute("Id") == extensionRelationshipId &&
                (string?)relationship.Attribute("Type") == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/package" &&
                (string?)relationship.Attribute("Target") == "../metadata/freexWorksheetExt.xml");
    }

    private static string AddWorkbookExtensionListPackageRelationship(MemoryStream packageStream)
    {
        string packageRelationshipId;
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            XNamespace x15Ns = "http://schemas.microsoft.com/office/spreadsheetml/2010/11/main";

            var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
            var workbookRelsXml = LoadPackageXml(archive.GetEntry("xl/_rels/workbook.xml.rels")!);
            var sheetRelationship = workbookRelsXml.Root!
                .Elements(packageRelNs + "Relationship")
                .First(relationship => (string?)relationship.Attribute("Type") == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet");
            var originalSheetRelationshipId = sheetRelationship.Attribute("Id")!.Value;
            packageRelationshipId = originalSheetRelationshipId;
            sheetRelationship.SetAttributeValue("Id", "rIdSheet1");
            workbookXml.Root!
                .Element(workbookNs + "sheets")!
                .Element(workbookNs + "sheet")!
                .SetAttributeValue(relNs + "id", "rIdSheet1");

            workbookRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", packageRelationshipId),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/package"),
                new XAttribute("Target", "metadata/freexWorkbookExt.xml")));
            workbookXml.Root!.Add(new XElement(
                workbookNs + "extLst",
                new XElement(
                    workbookNs + "ext",
                    new XAttribute("uri", "{FREEX-WORKBOOK-PACKAGE-REL}"),
                    new XElement(
                        x15Ns + "packageRef",
                        new XAttribute(XNamespace.Xmlns + "x15", x15Ns),
                        new XAttribute(relNs + "id", packageRelationshipId)))));

            AddTextEntry(archive, "xl/metadata/freexWorkbookExt.xml", "<metadata />");
            AddContentTypeOverride(archive, "/xl/metadata/freexWorkbookExt.xml", "application/xml");
            ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
            ReplacePackageXml(archive, "xl/_rels/workbook.xml.rels", workbookRelsXml);
        }

        packageStream.Position = 0;
        return packageRelationshipId;
    }

    private static void AddWorksheetExtensionListPackageRelationship(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            XNamespace x15Ns = "http://schemas.microsoft.com/office/spreadsheetml/2010/11/main";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            var sheetData = worksheetXml.Root!.Element(worksheetNs + "sheetData")!;
            sheetData.AddAfterSelf(new XElement(
                worksheetNs + "hyperlinks",
                new XElement(
                    worksheetNs + "hyperlink",
                    new XAttribute("ref", "A1"),
                    new XAttribute(relNs + "id", "rIdHyperlink"))));
            worksheetXml.Root!.Add(new XElement(
                worksheetNs + "extLst",
                new XElement(
                    worksheetNs + "ext",
                    new XAttribute("uri", "{FREEX-WORKSHEET-PACKAGE-REL}"),
                    new XElement(
                        x15Ns + "packageRef",
                        new XAttribute(XNamespace.Xmlns + "x15", x15Ns),
                        new XAttribute(relNs + "id", "rId1")))));

            var worksheetRelsXml = new XDocument(new XElement(
                packageRelNs + "Relationships",
                new XElement(
                    packageRelNs + "Relationship",
                    new XAttribute("Id", "rIdHyperlink"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink"),
                    new XAttribute("Target", "https://example.invalid/worksheet-ext-rel"),
                    new XAttribute("TargetMode", "External")),
                new XElement(
                    packageRelNs + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/package"),
                    new XAttribute("Target", "../metadata/freexWorksheetExt.xml"))));

            AddTextEntry(archive, "xl/metadata/freexWorksheetExt.xml", "<metadata />");
            AddContentTypeOverride(archive, "/xl/metadata/freexWorksheetExt.xml", "application/xml");
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
            ReplacePackageXml(archive, "xl/worksheets/_rels/sheet1.xml.rels", worksheetRelsXml);
        }

        packageStream.Position = 0;
    }

    private static void AddTextEntry(ZipArchive archive, string path, string text)
    {
        archive.GetEntry(path)?.Delete();
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(text);
    }

    private static void AddContentTypeOverride(ZipArchive archive, string partName, string contentType)
    {
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
        contentTypesXml.Root!.Add(new XElement(
            contentTypeNs + "Override",
            new XAttribute("PartName", partName),
            new XAttribute("ContentType", contentType)));
        ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);
    }
}
