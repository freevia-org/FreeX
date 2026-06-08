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

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var reloadedSheet = reloaded.GetSheetAt(0);
        reloadedSheet.GetCell(3, 3)!.Value.Should().Be(new NumberValue(42));
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(reloaded, out var reloadBlockReason)
            .Should()
            .BeTrue(reloadBlockReason);

        reloadedSheet.SetCell(new CellAddress(reloadedSheet.Id, 4, 4), new NumberValue(84));

        using var resaved = new MemoryStream();
        adapter.Save(reloaded, resaved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(resaved).Should().BeEmpty();
        AssertWorksheetNativeMetadataPackage(resaved);
        resaved.Position = 0;
        adapter.Load(resaved).GetSheetAt(0).GetCell(4, 4)!.Value.Should().Be(new NumberValue(84));
    }

    [Fact]
    public void LoadedWorkbookFullSave_SanitizesInvalidWorksheetWebPublishItemsForSchemaValidity()
    {
        using var source = CreateWorksheetNativeMetadataSourcePackage();
        SetWorksheetWebPublishItemsInvalidMetadata(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        AssertWorksheetWebPublishItemsSanitized(saved);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidWorksheetWebPublishItemsForSchemaValidity()
    {
        using var source = CreateWorksheetNativeMetadataSourcePackage();
        SetWorksheetWebPublishItemsInvalidMetadata(source);
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

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        AssertWorksheetWebPublishItemsSanitized(saved);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LoadedWorkbookSave_RepairsWorksheetWebPublishItemsPackageMetadata(bool usePatchSave)
    {
        using var source = CreateWorksheetNativeMetadataSourcePackage();
        RemoveWorksheetWebPublishItemsPackageMetadata(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        if (usePatchSave)
        {
            XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
                .Should()
                .BeTrue(blockReason);
        }

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(
            usePatchSave ? XlsxSavePath.SourcePatch : XlsxSavePath.FullSave,
            adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        AssertWorksheetWebPublishItemsPackageMetadata(saved);
    }

    [Fact]
    public void LoadedWorkbookFullSave_SanitizesInvalidWorksheetOleControlsForSchemaValidity()
    {
        using var source = CreateWorksheetNativeMetadataSourcePackage();
        SetWorksheetOleControlInvalidMetadata(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        AssertWorksheetOleControlsSanitized(saved);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidWorksheetOleControlsForSchemaValidity()
    {
        using var source = CreateWorksheetNativeMetadataSourcePackage();
        SetWorksheetOleControlInvalidMetadata(source);
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

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        AssertWorksheetOleControlsSanitized(saved);
    }

    [Fact]
    public void WorksheetOleControlNormalizer_RebindsControlPropertiesRelationshipIdCollision()
    {
        using var saved = CreateWorksheetNativeMetadataSourcePackage();
        CreateWorksheetControlPropertiesRelationshipIdCollision(saved);

        saved.Position = 0;
        using (var archive = new ZipArchive(saved, ZipArchiveMode.Update, leaveOpen: true))
        {
            XlsxWorksheetOleControlNormalizer.NormalizePackage(archive);
        }

        saved.Position = 0;
        SchemaErrors(saved).Should().BeEmpty();
        AssertWorksheetControlPropertiesRelationshipRebound(saved);
    }

    [Fact]
    public void WorksheetOleControlNormalizer_RebindsOleObjectAndObjectPropertiesRelationshipIdCollision()
    {
        using var saved = CreateWorksheetNativeMetadataSourcePackage();
        CreateWorksheetOleObjectRelationshipIdCollision(saved);

        saved.Position = 0;
        using (var archive = new ZipArchive(saved, ZipArchiveMode.Update, leaveOpen: true))
        {
            XlsxWorksheetOleControlNormalizer.NormalizePackage(archive);
        }

        saved.Position = 0;
        SchemaErrors(saved).Should().BeEmpty();
        AssertWorksheetOleObjectRelationshipRebound(saved);
    }

    [Fact]
    public void WorksheetOleControlNormalizer_RebindsControlPropertiesFromValidControlWhenControlPrIsDangling()
    {
        using var saved = CreateWorksheetNativeMetadataSourcePackage();
        CreateWorksheetControlPropertiesDanglingControlPrRelationship(saved);

        saved.Position = 0;
        using (var archive = new ZipArchive(saved, ZipArchiveMode.Update, leaveOpen: true))
        {
            XlsxWorksheetOleControlNormalizer.NormalizePackage(archive);
        }

        saved.Position = 0;
        SchemaErrors(saved).Should().BeEmpty();
        AssertWorksheetControlPropertiesDanglingControlPrRebound(saved);
    }

    [Fact]
    public void WorksheetOleControlNormalizer_PrunesOleAndControlElementsWhenSidecarPartsAreMissing()
    {
        using var saved = CreateWorksheetNativeMetadataSourcePackage();
        RemoveWorksheetOleControlSidecarParts(saved);

        saved.Position = 0;
        using (var archive = new ZipArchive(saved, ZipArchiveMode.Update, leaveOpen: true))
        {
            XlsxWorksheetOleControlNormalizer.NormalizePackage(archive);
        }

        saved.Position = 0;
        SchemaErrors(saved).Should().BeEmpty();
        AssertWorksheetOleControlSidecarsPruned(saved);
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

        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
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
                new XAttribute(relNs + "id", "rIdFreeXControl"),
                new XElement(
                    worksheetNs + "controlPr",
                    new XAttribute(relNs + "id", "rIdFreeXControl"),
                    new XAttribute("print", "1"),
                    new XAttribute("altText", "Launch report"),
                    CreateControlAnchor(worksheetNs)))));
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

    private static void SetWorksheetWebPublishItemsInvalidMetadata(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var worksheetWebPublishItems = worksheetXml.Root!.Element(worksheetNs + "webPublishItems")!;
        SetInvalidWebPublishItemsPayload(worksheetWebPublishItems, worksheetNs);
        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

        var partXml = LoadPackageXml(archive, "xl/webPublishItems.xml");
        SetInvalidWebPublishItemsPayload(partXml.Root!, worksheetNs);
        ReplacePackageXml(archive, "xl/webPublishItems.xml", partXml);
    }

    private static void RemoveWorksheetWebPublishItemsPackageMetadata(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

        var relationshipsXml = LoadPackageXml(archive, "xl/worksheets/_rels/sheet1.xml.rels");
        relationshipsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(relationship =>
                string.Equals(
                    relationship.Attribute("Type")?.Value,
                    "http://schemas.openxmlformats.org/officeDocument/2006/relationships/webPublishItems",
                    StringComparison.OrdinalIgnoreCase))
            .Remove();
        ReplacePackageXml(archive, "xl/worksheets/_rels/sheet1.xml.rels", relationshipsXml);

        var contentTypesXml = LoadPackageXml(archive, "[Content_Types].xml");
        contentTypesXml.Root!
            .Elements(contentTypeNs + "Override")
            .Where(overrideElement =>
                string.Equals(
                    overrideElement.Attribute("PartName")?.Value,
                    "/xl/webPublishItems.xml",
                    StringComparison.OrdinalIgnoreCase))
            .Remove();
        ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);
    }

    private static void SetWorksheetOleControlInvalidMetadata(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var root = worksheetXml.Root!;

        var oleObjects = root.Element(worksheetNs + "oleObjects")!;
        oleObjects.SetAttributeValue("customOleObjectsFlag", "removed");
        oleObjects.Add(new XElement(worksheetNs + "nativeOleObjectsChild"));
        var oleObject = oleObjects.Element(worksheetNs + "oleObject")!;
        oleObject.SetAttributeValue("progId", " Package ");
        oleObject.SetAttributeValue("shapeId", " 1025 ");
        oleObject.SetAttributeValue("autoLoad", "true");
        oleObject.SetAttributeValue("oleUpdate", "OLEUPDATE_ALWAYS");
        oleObject.SetAttributeValue("customOleObjectFlag", "removed");
        oleObject.Add(new XElement(worksheetNs + "nativeOleObjectChild"));
        oleObjects.Add(new XElement(
            worksheetNs + "oleObject",
            new XAttribute("progId", "RemovedPackage"),
            new XAttribute("shapeId", "not-a-number"),
            new XAttribute(relNs + "id", "rIdFreeXOleObject")));

        var controls = root.Element(worksheetNs + "controls")!;
        controls.SetAttributeValue("customControlsFlag", "removed");
        controls.Add(new XElement(worksheetNs + "nativeControlsChild"));
        var control = controls.Element(worksheetNs + "control")!;
        control.SetAttributeValue("shapeId", " 1026 ");
        control.SetAttributeValue("name", " Button 1 ");
        control.SetAttributeValue("customControlFlag", "removed");
        var controlPr = control.Element(worksheetNs + "controlPr")!;
        controlPr.SetAttributeValue("print", " true ");
        controlPr.SetAttributeValue("altText", " Launch report ");
        controlPr.SetAttributeValue("disabled", "not-a-boolean");
        controlPr.SetAttributeValue("customControlPrFlag", "removed");
        controlPr.Add(new XElement(worksheetNs + "nativeControlPrChild"));
        control.Add(new XElement(
            worksheetNs + "controlPr",
            new XAttribute(relNs + "id", "rIdRemovedControlProperties")));
        control.Add(new XElement(worksheetNs + "nativeControlChild"));
        controls.Add(new XElement(
            worksheetNs + "control",
            new XAttribute("shapeId", "not-a-number"),
            new XAttribute("name", "Removed Control"),
            new XAttribute(relNs + "id", "rIdFreeXControl")));

        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
    }

    private static void CreateWorksheetControlPropertiesRelationshipIdCollision(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var control = worksheetXml.Root!
            .Element(worksheetNs + "controls")!
            .Element(worksheetNs + "control")!;
        control.SetAttributeValue(relNs + "id", "rId1");
        control.Element(worksheetNs + "controlPr")!.SetAttributeValue(relNs + "id", "rId1");
        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

        var relationshipsXml = LoadPackageXml(archive, "xl/worksheets/_rels/sheet1.xml.rels");
        relationshipsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Single(relationship => relationship.Attribute("Target")?.Value == "../ctrlProps/ctrlProp1.xml")
            .SetAttributeValue("Id", "rId2");
        relationshipsXml.Root!.Add(new XElement(
            packageRelNs + "Relationship",
            new XAttribute("Id", "rId1"),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing"),
            new XAttribute("Target", "../drawings/vmlDrawing1.vml")));
        ReplacePackageXml(archive, "xl/worksheets/_rels/sheet1.xml.rels", relationshipsXml);

        WritePackageEntry(archive, "xl/drawings/vmlDrawing1.vml", "<xml/>");
        AddPackageContentTypeOverride(
            archive,
            "/xl/drawings/vmlDrawing1.vml",
            "application/vnd.openxmlformats-officedocument.vmlDrawing");

        var contentTypesXml = LoadPackageXml(archive, "[Content_Types].xml");
        contentTypesXml.Root!
            .Elements(contentTypeNs + "Override")
            .Where(overrideElement => overrideElement.Attribute("PartName")?.Value == "/xl/ctrlProps/ctrlProp1.xml")
            .Remove();
        ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);
    }

    private static void CreateWorksheetOleObjectRelationshipIdCollision(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var oleObject = worksheetXml.Root!
            .Element(worksheetNs + "oleObjects")!
            .Element(worksheetNs + "oleObject")!;
        oleObject.SetAttributeValue(relNs + "id", "rId1");
        oleObject.Add(new XElement(
            worksheetNs + "objectPr",
            new XAttribute(relNs + "id", "rId1"),
            new XAttribute("defaultSize", " true "),
            CreateControlAnchor(worksheetNs)));
        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

        var relationshipsXml = LoadPackageXml(archive, "xl/worksheets/_rels/sheet1.xml.rels");
        relationshipsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Single(relationship => relationship.Attribute("Target")?.Value == "../embeddings/oleObject1.bin")
            .SetAttributeValue("Id", "rId3");
        relationshipsXml.Root!.Add(new XElement(
            packageRelNs + "Relationship",
            new XAttribute("Id", "rId1"),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing"),
            new XAttribute("Target", "../drawings/vmlDrawing1.vml")));
        relationshipsXml.Root!.Add(new XElement(
            packageRelNs + "Relationship",
            new XAttribute("Id", "rId4"),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing"),
            new XAttribute("Target", "../drawings/drawing1.xml")));
        ReplacePackageXml(archive, "xl/worksheets/_rels/sheet1.xml.rels", relationshipsXml);

        WritePackageEntry(archive, "xl/drawings/vmlDrawing1.vml", "<xml/>");
        ReplacePackageXml(archive, "xl/drawings/drawing1.xml", new XDocument(new XElement(
            XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing") + "wsDr")));
        AddPackageContentTypeOverride(
            archive,
            "/xl/drawings/vmlDrawing1.vml",
            "application/vnd.openxmlformats-officedocument.vmlDrawing");
        AddPackageContentTypeOverride(
            archive,
            "/xl/drawings/drawing1.xml",
            "application/vnd.openxmlformats-officedocument.drawing+xml");

        var contentTypesXml = LoadPackageXml(archive, "[Content_Types].xml");
        contentTypesXml.Root!
            .Elements(contentTypeNs + "Override")
            .Where(overrideElement => overrideElement.Attribute("PartName")?.Value == "/xl/embeddings/oleObject1.bin")
            .Remove();
        ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);
    }

    private static void CreateWorksheetControlPropertiesDanglingControlPrRelationship(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        worksheetXml.Root!
            .Element(worksheetNs + "controls")!
            .Element(worksheetNs + "control")!
            .Element(worksheetNs + "controlPr")!
            .SetAttributeValue(relNs + "id", "rIdDanglingControlProperties");
        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

        var relationshipsXml = LoadPackageXml(archive, "xl/worksheets/_rels/sheet1.xml.rels");
        relationshipsXml.Root!.Add(new XElement(
            packageRelNs + "Relationship",
            new XAttribute("Id", "rIdDanglingControlProperties"),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/ctrlProp"),
            new XAttribute("Target", "../ctrlProps/missingCtrlProp.xml")));
        ReplacePackageXml(archive, "xl/worksheets/_rels/sheet1.xml.rels", relationshipsXml);
    }

    private static void RemoveWorksheetOleControlSidecarParts(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);

        archive.GetEntry("xl/embeddings/oleObject1.bin")?.Delete();
        archive.GetEntry("xl/ctrlProps/ctrlProp1.xml")?.Delete();
    }

    private static void SetInvalidWebPublishItemsPayload(XElement webPublishItems, XNamespace worksheetNs)
    {
        webPublishItems.RemoveNodes();
        webPublishItems.SetAttributeValue("count", "not-a-number");
        webPublishItems.SetAttributeValue("customWebPublishItemsFlag", "removed");
        webPublishItems.Add(
            new XElement(worksheetNs + "nativeWebPublishItemsChild"),
            new XElement(
                worksheetNs + "webPublishItem",
                new XAttribute("id", " 1 "),
                new XAttribute("divId", " FreeXWebPublishItems "),
                new XAttribute("sourceType", " sheet "),
                new XAttribute("sourceRef", " A1:B2 "),
                new XAttribute("destinationFile", " https://example.invalid/sheet.htm "),
                new XAttribute("autoRepublish", "true"),
                new XAttribute("customWebPublishItemFlag", "removed"),
                new XElement(worksheetNs + "nativeWebPublishItemChild")),
            new XElement(
                worksheetNs + "webPublishItem",
                new XAttribute("id", "not-a-number"),
                new XAttribute("divId", "RemovedWebPublishItem"),
                new XAttribute("sourceType", "invalid"),
                new XAttribute("destinationFile", "https://example.invalid/removed.htm")));
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
        ReadWorksheetChildElement(stream, "controls")
            .Element(worksheetNs + "control")!
            .Element(worksheetNs + "controlPr")!
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

    private static void AssertWorksheetWebPublishItemsSanitized(Stream stream)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        AssertWebPublishItemsSanitized(ReadWorksheetChildElement(stream, "webPublishItems"), worksheetNs);
        AssertWebPublishItemsSanitized(ReadPackageRootElement(stream, "xl/webPublishItems.xml"), worksheetNs);
    }

    private static void AssertWorksheetWebPublishItemsPackageMetadata(Stream stream)
    {
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

        ReadPackageRootElement(stream, "xl/worksheets/_rels/sheet1.xml.rels")
            .Elements(packageRelNs + "Relationship")
            .Where(relationship =>
                relationship.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/webPublishItems" &&
                relationship.Attribute("Target")?.Value == "../webPublishItems.xml")
            .Should()
            .ContainSingle();

        ReadPackageRootElement(stream, "[Content_Types].xml")
            .Elements(contentTypeNs + "Override")
            .Where(overrideElement =>
                overrideElement.Attribute("PartName")?.Value == "/xl/webPublishItems.xml" &&
                overrideElement.Attribute("ContentType")?.Value == "application/vnd.openxmlformats-officedocument.spreadsheetml.webPublishItems+xml")
            .Should()
            .ContainSingle();
    }

    private static void AssertWebPublishItemsSanitized(XElement webPublishItems, XNamespace worksheetNs)
    {
        webPublishItems.Attribute("count")!.Value.Should().Be("1");
        webPublishItems.Attribute("customWebPublishItemsFlag").Should().BeNull();
        webPublishItems.Element(worksheetNs + "nativeWebPublishItemsChild").Should().BeNull();

        var webPublishItem = webPublishItems.Elements(worksheetNs + "webPublishItem")
            .Should()
            .ContainSingle()
            .Subject;
        webPublishItem.Attribute("id")!.Value.Should().Be("1");
        webPublishItem.Attribute("divId")!.Value.Should().Be("FreeXWebPublishItems");
        webPublishItem.Attribute("sourceType")!.Value.Should().Be("sheet");
        webPublishItem.Attribute("sourceRef")!.Value.Should().Be("A1:B2");
        webPublishItem.Attribute("destinationFile")!.Value.Should().Be("https://example.invalid/sheet.htm");
        webPublishItem.Attribute("autoRepublish")!.Value.Should().Be("true");
        webPublishItem.Attribute("customWebPublishItemFlag").Should().BeNull();
        webPublishItem.Elements().Should().BeEmpty();
    }

    private static void AssertWorksheetOleControlsSanitized(Stream stream)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        var oleObjects = ReadWorksheetChildElement(stream, "oleObjects");
        oleObjects.Attribute("customOleObjectsFlag").Should().BeNull();
        oleObjects.Element(worksheetNs + "nativeOleObjectsChild").Should().BeNull();
        var oleObject = oleObjects.Elements(worksheetNs + "oleObject")
            .Should()
            .ContainSingle()
            .Subject;
        oleObject.Attribute("progId")!.Value.Should().Be("Package");
        oleObject.Attribute("shapeId")!.Value.Should().Be("1025");
        oleObject.Attribute(relNs + "id")!.Value.Should().Be("rIdFreeXOleObject");
        oleObject.Attribute("autoLoad")!.Value.Should().Be("true");
        oleObject.Attribute("oleUpdate")!.Value.Should().Be("OLEUPDATE_ALWAYS");
        oleObject.Attribute("customOleObjectFlag").Should().BeNull();
        oleObject.Elements().Should().BeEmpty();

        var controls = ReadWorksheetChildElement(stream, "controls");
        controls.Attribute("customControlsFlag").Should().BeNull();
        controls.Element(worksheetNs + "nativeControlsChild").Should().BeNull();
        var control = controls.Elements(worksheetNs + "control")
            .Should()
            .ContainSingle()
            .Subject;
        control.Attribute("shapeId")!.Value.Should().Be("1026");
        control.Attribute("name")!.Value.Should().Be("Button 1");
        control.Attribute(relNs + "id")!.Value.Should().Be("rIdFreeXControl");
        control.Attribute("customControlFlag").Should().BeNull();
        var controlPr = control.Elements(worksheetNs + "controlPr")
            .Should()
            .ContainSingle()
            .Subject;
        controlPr.Attribute(relNs + "id")!.Value.Should().Be("rIdFreeXControl");
        controlPr.Attribute("print")!.Value.Should().Be("true");
        controlPr.Attribute("altText")!.Value.Should().Be("Launch report");
        controlPr.Attribute("disabled").Should().BeNull();
        controlPr.Attribute("customControlPrFlag").Should().BeNull();
        controlPr.Elements(worksheetNs + "anchor").Should().ContainSingle();
    }

    private static void AssertWorksheetControlPropertiesRelationshipRebound(Stream stream)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

        var control = ReadWorksheetChildElement(stream, "controls")
            .Element(worksheetNs + "control")!;
        var controlRelationshipId = control.Attribute(relNs + "id")!.Value;
        controlRelationshipId.Should().Be("rId2", "rId1 is claimed by the generated VML sidecar relationship");
        control.Element(worksheetNs + "controlPr")!
            .Attribute(relNs + "id")!
            .Value
            .Should()
            .Be(controlRelationshipId);

        var relationships = ReadPackageRootElement(stream, "xl/worksheets/_rels/sheet1.xml.rels")
            .Elements(packageRelNs + "Relationship")
            .ToList();
        relationships.Where(relationship =>
                relationship.Attribute("Id")?.Value == "rId1" &&
                relationship.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing" &&
                relationship.Attribute("Target")?.Value == "../drawings/vmlDrawing1.vml")
            .Should()
            .ContainSingle();
        relationships.Where(relationship =>
                relationship.Attribute("Id")?.Value == controlRelationshipId &&
                relationship.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/ctrlProp" &&
                relationship.Attribute("Target")?.Value == "../ctrlProps/ctrlProp1.xml")
            .Should()
            .ContainSingle();

        ReadPackageRootElement(stream, "[Content_Types].xml")
            .Elements(contentTypeNs + "Override")
            .Where(overrideElement =>
                overrideElement.Attribute("PartName")?.Value == "/xl/ctrlProps/ctrlProp1.xml" &&
                overrideElement.Attribute("ContentType")?.Value == "application/vnd.ms-excel.controlproperties+xml")
            .Should()
            .ContainSingle();
    }

    private static void AssertWorksheetOleObjectRelationshipRebound(Stream stream)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

        var oleObject = ReadWorksheetChildElement(stream, "oleObjects")
            .Element(worksheetNs + "oleObject")!;
        var oleRelationshipId = oleObject.Attribute(relNs + "id")!.Value;
        oleRelationshipId.Should().Be("rId3", "rId1 is claimed by the generated VML sidecar relationship");
        var objectProperties = oleObject.Element(worksheetNs + "objectPr");
        objectProperties.Should().NotBeNull();
        objectProperties!
            .Attribute(relNs + "id")!
            .Value
            .Should()
            .Be("rId4", "objectPr relationships point at the drawing part, not the embedded OLE object");
        objectProperties
            .Attribute("defaultSize")!
            .Value
            .Should()
            .Be("true");

        var relationships = ReadPackageRootElement(stream, "xl/worksheets/_rels/sheet1.xml.rels")
            .Elements(packageRelNs + "Relationship")
            .ToList();
        relationships.Where(relationship =>
                relationship.Attribute("Id")?.Value == "rId1" &&
                relationship.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing" &&
                relationship.Attribute("Target")?.Value == "../drawings/vmlDrawing1.vml")
            .Should()
            .ContainSingle();
        relationships.Where(relationship =>
                relationship.Attribute("Id")?.Value == oleRelationshipId &&
                relationship.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/oleObject" &&
                relationship.Attribute("Target")?.Value == "../embeddings/oleObject1.bin")
            .Should()
            .ContainSingle();
        relationships.Where(relationship =>
                relationship.Attribute("Id")?.Value == "rId4" &&
                relationship.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing" &&
                relationship.Attribute("Target")?.Value == "../drawings/drawing1.xml")
            .Should()
            .ContainSingle();

        ReadPackageRootElement(stream, "[Content_Types].xml")
            .Elements(contentTypeNs + "Override")
            .Where(overrideElement =>
                overrideElement.Attribute("PartName")?.Value == "/xl/embeddings/oleObject1.bin" &&
                overrideElement.Attribute("ContentType")?.Value == "application/vnd.openxmlformats-officedocument.oleObject")
            .Should()
            .ContainSingle();
    }

    private static void AssertWorksheetControlPropertiesDanglingControlPrRebound(Stream stream)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var control = ReadWorksheetChildElement(stream, "controls")
            .Element(worksheetNs + "control")!;
        control.Attribute(relNs + "id")!.Value.Should().Be("rIdFreeXControl");
        control.Element(worksheetNs + "controlPr")!
            .Attribute(relNs + "id")!
            .Value
            .Should()
            .Be("rIdFreeXControl");

        ReadPackageRootElement(stream, "xl/worksheets/_rels/sheet1.xml.rels")
            .Elements(packageRelNs + "Relationship")
            .Where(relationship => relationship.Attribute("Id")?.Value == "rIdDanglingControlProperties")
            .Should()
            .BeEmpty();
    }

    private static void AssertWorksheetOleControlSidecarsPruned(Stream stream)
    {
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var worksheetRoot = ReadPackageRootElement(stream, "xl/worksheets/sheet1.xml");
        worksheetRoot.Element(worksheetRoot.Name.Namespace + "oleObjects").Should().BeNull();
        worksheetRoot.Element(worksheetRoot.Name.Namespace + "controls").Should().BeNull();

        ReadPackageRootElement(stream, "xl/worksheets/_rels/sheet1.xml.rels")
            .Elements(packageRelNs + "Relationship")
            .Where(relationship =>
                relationship.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/oleObject" ||
                relationship.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/ctrlProp" ||
                relationship.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/control")
            .Should()
            .BeEmpty();

        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        archive.GetEntry("xl/embeddings/oleObject1.bin").Should().BeNull();
        archive.GetEntry("xl/ctrlProps/ctrlProp1.xml").Should().BeNull();
    }

    private static XElement CreateControlAnchor(XNamespace worksheetNs)
    {
        XNamespace drawingSpreadsheetNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

        static XElement Marker(
            XNamespace worksheetNs,
            XNamespace drawingSpreadsheetNs,
            string name,
            int column,
            int row) =>
            new(
                worksheetNs + name,
                new XElement(drawingSpreadsheetNs + "col", column.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new XElement(drawingSpreadsheetNs + "colOff", "0"),
                new XElement(drawingSpreadsheetNs + "row", row.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new XElement(drawingSpreadsheetNs + "rowOff", "0"));

        return new XElement(
            worksheetNs + "anchor",
            new XAttribute("moveWithCells", "1"),
            new XAttribute("sizeWithCells", "1"),
            Marker(worksheetNs, drawingSpreadsheetNs, "from", 0, 1),
            Marker(worksheetNs, drawingSpreadsheetNs, "to", 1, 2));
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
