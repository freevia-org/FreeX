using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxNonChartSchemaValidationTests
{
    [Fact]
    public void WorksheetNativeMetadataPackage_ProducesSchemaValidWorkbook()
    {
        using var source = CreateWorksheetNativeMetadataSourcePackage();

        SchemaErrors(source).Should().BeEmpty();
        AssertWorksheetNativeMetadataPackage(source);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithWorksheetNativeMetadata_ProducesSchemaValidWorkbook()
    {
        using var source = CreateWorksheetNativeMetadataSourcePackage();
        var sourceOleObjects = ReadWorksheetChildElement(source, "oleObjects");
        var sourceControls = ReadWorksheetChildElement(source, "controls");
        var sourceWebPublishItems = ReadWorksheetChildElement(source, "webPublishItems");
        var sourceWorksheetRelationships = ReadPackageRootElement(source, "xl/worksheets/_rels/sheet1.xml.rels");
        var sourceContentTypes = ReadPackageRootElement(source, "[Content_Types].xml");
        var sourceWebPublishItemsPart = ReadPackageRootElement(source, "xl/webPublishItems.xml");
        var sourceControlProperties = ReadPackageRootElement(source, "xl/ctrlProps/ctrlProp1.xml");
        var sourceOleObjectText = ReadPackageEntryText(source, "xl/embeddings/oleObject1.bin");
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        AssertWorksheetNativeMetadataPackage(saved);
        ReadWorksheetChildElement(saved, "oleObjects")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceOleObjects.ToString(SaveOptions.DisableFormatting));
        ReadWorksheetChildElement(saved, "controls")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceControls.ToString(SaveOptions.DisableFormatting));
        ReadWorksheetChildElement(saved, "webPublishItems")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceWebPublishItems.ToString(SaveOptions.DisableFormatting));
        ReadPackageRootElement(saved, "xl/worksheets/_rels/sheet1.xml.rels")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceWorksheetRelationships.ToString(SaveOptions.DisableFormatting));
        ReadPackageRootElement(saved, "[Content_Types].xml")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceContentTypes.ToString(SaveOptions.DisableFormatting));
        ReadPackageRootElement(saved, "xl/webPublishItems.xml")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceWebPublishItemsPart.ToString(SaveOptions.DisableFormatting));
        ReadPackageRootElement(saved, "xl/ctrlProps/ctrlProp1.xml")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceControlProperties.ToString(SaveOptions.DisableFormatting));
        ReadPackageEntryText(saved, "xl/embeddings/oleObject1.bin").Should().Be(sourceOleObjectText);
    }

    private static MemoryStream CreateWorksheetNativeMetadataSourcePackage()
    {
        var workbook = new Workbook("WorksheetNativeMetadataPatchSave");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Name"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Kept"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(24));

        var stream = Save(workbook);
        AddWorksheetNativeMetadataPackage(stream);
        stream.Position = 0;
        return stream;
    }

    private static void AddWorksheetNativeMetadataPackage(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace controlNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        AddPackageContentTypeOverride(
            archive,
            "/xl/webPublishItems.xml",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.webPublishItems+xml");
        AddPackageContentTypeOverride(
            archive,
            "/xl/embeddings/oleObject1.bin",
            "application/vnd.openxmlformats-officedocument.oleObject");
        AddPackageContentTypeOverride(
            archive,
            "/xl/ctrlProps/ctrlProp1.xml",
            "application/vnd.ms-excel.controlproperties+xml");

        var worksheetRelationshipsPath = "xl/worksheets/_rels/sheet1.xml.rels";
        var worksheetRelationshipsXml = archive.GetEntry(worksheetRelationshipsPath) is { } worksheetRelationshipsEntry
            ? LoadPackageXml(worksheetRelationshipsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        AddWorksheetNativeRelationship(
            worksheetRelationshipsXml,
            packageRelNs,
            "rIdFreeXOleObject",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/oleObject",
            "../embeddings/oleObject1.bin");
        AddWorksheetNativeRelationship(
            worksheetRelationshipsXml,
            packageRelNs,
            "rIdFreeXControl",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/ctrlProp",
            "../ctrlProps/ctrlProp1.xml");
        AddWorksheetNativeRelationship(
            worksheetRelationshipsXml,
            packageRelNs,
            "rIdFreeXWebPublishItems",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/webPublishItems",
            "../webPublishItems.xml");
        ReplacePackageXml(archive, worksheetRelationshipsPath, worksheetRelationshipsXml);

        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var root = worksheetXml.Root!;
        ReplaceWorksheetChildInOrder(root, new XElement(
            worksheetNs + "oleObjects",
            new XElement(
                worksheetNs + "oleObject",
                new XAttribute("progId", "Package"),
                new XAttribute("shapeId", "1025"),
                new XAttribute(relNs + "id", "rIdFreeXOleObject"))));
        ReplaceWorksheetChildInOrder(root, new XElement(
            worksheetNs + "controls",
            new XElement(
                worksheetNs + "control",
                new XAttribute("shapeId", "1026"),
                new XAttribute("name", "Button 1"),
                new XAttribute(relNs + "id", "rIdFreeXControl"))));
        ReplaceWorksheetChildInOrder(root, new XElement(
            worksheetNs + "webPublishItems",
            new XAttribute("count", "1"),
            new XElement(
                worksheetNs + "webPublishItem",
                new XAttribute("id", "1"),
                new XAttribute("divId", "FreeXWebPublishItems"),
                new XAttribute("sourceType", "sheet"),
                new XAttribute("sourceRef", "A1:B2"),
                new XAttribute("destinationFile", "https://example.invalid/sheet.htm"))));
        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

        ReplacePackageXml(archive, "xl/webPublishItems.xml", new XDocument(
            new XElement(
                worksheetNs + "webPublishItems",
                new XAttribute("count", "1"),
                new XElement(
                    worksheetNs + "webPublishItem",
                    new XAttribute("id", "1"),
                    new XAttribute("divId", "FreeXWebPublishItems"),
                    new XAttribute("sourceType", "sheet"),
                    new XAttribute("destinationFile", "https://example.invalid/sheet.htm")))));
        ReplacePackageXml(archive, "xl/ctrlProps/ctrlProp1.xml", new XDocument(
            new XElement(
                controlNs + "formControlPr",
                new XAttribute("objectType", "Button"),
                new XAttribute("checked", "Unchecked"))));
        WritePackageEntry(archive, "xl/embeddings/oleObject1.bin", "FreeX generated OLE placeholder");
    }

    private static void AddWorksheetNativeRelationship(
        XDocument relationshipsXml,
        XNamespace packageRelNs,
        string id,
        string type,
        string target)
    {
        relationshipsXml.Root!.Elements(packageRelNs + "Relationship")
            .Where(relationship =>
                relationship.Attribute("Id")?.Value == id ||
                relationship.Attribute("Type")?.Value == type ||
                relationship.Attribute("Target")?.Value == target)
            .Remove();
        relationshipsXml.Root!.Add(new XElement(
            packageRelNs + "Relationship",
            new XAttribute("Id", id),
            new XAttribute("Type", type),
            new XAttribute("Target", target)));
    }

    private static void AssertWorksheetNativeMetadataPackage(Stream stream)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var worksheetRoot = ReadPackageRootElement(stream, "xl/worksheets/sheet1.xml");
        AssertWorksheetNativeMetadataOrder(worksheetRoot);
        ReadWorksheetChildElement(stream, "oleObjects")
            .Element(worksheetNs + "oleObject")!
            .Attribute(relNs + "id")!
            .Value
            .Should()
            .Be("rIdFreeXOleObject");
        ReadWorksheetChildElement(stream, "controls")
            .Element(worksheetNs + "control")!
            .Attribute(relNs + "id")!
            .Value
            .Should()
            .Be("rIdFreeXControl");
        ReadWorksheetChildElement(stream, "webPublishItems")
            .Element(worksheetNs + "webPublishItem")!
            .Attribute("id")!
            .Value
            .Should()
            .Be("1");

        var relationships = ReadPackageRootElement(stream, "xl/worksheets/_rels/sheet1.xml.rels")
            .Elements(packageRelNs + "Relationship")
            .ToList();
        relationships.Where(relationship =>
                relationship.Attribute("Id")?.Value == "rIdFreeXOleObject" &&
                relationship.Attribute("Target")?.Value == "../embeddings/oleObject1.bin")
            .Should()
            .ContainSingle();
        relationships.Where(relationship =>
                relationship.Attribute("Id")?.Value == "rIdFreeXControl" &&
                relationship.Attribute("Target")?.Value == "../ctrlProps/ctrlProp1.xml")
            .Should()
            .ContainSingle();
        relationships.Where(relationship =>
                relationship.Attribute("Id")?.Value == "rIdFreeXWebPublishItems" &&
                relationship.Attribute("Target")?.Value == "../webPublishItems.xml")
            .Should()
            .ContainSingle();

        ReadPackageRootElement(stream, "xl/webPublishItems.xml")
            .Element(worksheetNs + "webPublishItem")!
            .Attribute("divId")!
            .Value
            .Should()
            .Be("FreeXWebPublishItems");
        ReadPackageRootElement(stream, "xl/ctrlProps/ctrlProp1.xml")
            .Attribute("objectType")!
            .Value
            .Should()
            .Be("Button");
        ReadPackageEntryText(stream, "xl/embeddings/oleObject1.bin")
            .Should()
            .Be("FreeX generated OLE placeholder");
    }

    private static void AssertWorksheetNativeMetadataOrder(XElement worksheetRoot)
    {
        var childNames = worksheetRoot.Elements().Select(element => element.Name.LocalName).ToList();
        AssertWorksheetChildPrecedes(childNames, "picture", "oleObjects");
        AssertWorksheetChildPrecedes(childNames, "oleObjects", "controls");
        AssertWorksheetChildPrecedes(childNames, "controls", "webPublishItems");
        AssertWorksheetChildPrecedes(childNames, "webPublishItems", "tableParts");
        AssertWorksheetChildPrecedes(childNames, "webPublishItems", "extLst");
    }

    private static void AssertWorksheetChildPrecedes(
        List<string> childNames,
        string firstName,
        string secondName)
    {
        var firstIndex = childNames.IndexOf(firstName);
        var secondIndex = childNames.IndexOf(secondName);
        if (firstIndex >= 0 && secondIndex >= 0)
            firstIndex.Should().BeLessThan(secondIndex);
    }

    private static void ReplaceWorksheetChildInOrder(XElement root, XElement child)
    {
        root.Elements(child.Name).Remove();
        var insertBefore = root.Elements()
            .FirstOrDefault(element => WorksheetChildSchemaOrder(element) > WorksheetChildSchemaOrder(child));
        if (insertBefore is null)
            root.Add(child);
        else
            insertBefore.AddBeforeSelf(child);
    }

    private static int WorksheetChildSchemaOrder(XElement element) =>
        element.Name.LocalName switch
        {
            "sheetPr" => 0,
            "dimension" => 1,
            "sheetViews" => 2,
            "sheetFormatPr" => 3,
            "cols" => 4,
            "sheetData" => 5,
            "sheetCalcPr" => 6,
            "sheetProtection" => 7,
            "protectedRanges" => 8,
            "scenarios" => 9,
            "autoFilter" => 10,
            "sortState" => 11,
            "dataConsolidate" => 12,
            "customSheetViews" => 13,
            "mergeCells" => 14,
            "phoneticPr" => 15,
            "conditionalFormatting" => 16,
            "dataValidations" => 17,
            "hyperlinks" => 18,
            "printOptions" => 19,
            "pageMargins" => 20,
            "pageSetup" => 21,
            "headerFooter" => 22,
            "rowBreaks" => 23,
            "colBreaks" => 24,
            "customProperties" => 25,
            "cellWatches" => 26,
            "ignoredErrors" => 27,
            "singleXmlCells" => 28,
            "smartTags" => 29,
            "drawing" => 30,
            "legacyDrawing" => 31,
            "legacyDrawingHF" => 32,
            "drawingHF" => 33,
            "picture" => 34,
            "oleObjects" => 35,
            "controls" => 36,
            "webPublishItems" => 37,
            "tableParts" => 38,
            "extLst" => 100,
            _ => 90
        };
}
