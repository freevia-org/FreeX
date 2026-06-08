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
    public void WorkbookNativeMetadata_ProducesSchemaValidWorkbook()
    {
        using var source = CreateWorkbookNativeMetadataSourcePackage();

        SchemaErrors(source).Should().BeEmpty();
        AssertWorkbookNativeMetadataPackage(source);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithWorkbookNativeMetadata_ProducesSchemaValidWorkbook()
    {
        using var source = CreateWorkbookNativeMetadataSourcePackage();
        var sourceOleSize = ReadWorkbookChildElement(source, "oleSize");
        var sourceWebPublishing = ReadWorkbookChildElement(source, "webPublishing");
        var sourceFileRecoveryProperties = ReadWorkbookChildElement(source, "fileRecoveryPr");
        var sourceWebPublishObjects = ReadWorkbookChildElement(source, "webPublishObjects");
        var sourceExtensionList = ReadWorkbookChildElement(source, "extLst");
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.FileRecoveryProperties.Should().ContainSingle(properties =>
            properties.AutoRecover == true &&
            properties.CrashSave == true &&
            properties.RepairLoad == false);
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
        AssertWorkbookNativeMetadataPackage(saved);
        ReadWorkbookChildElement(saved, "oleSize")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceOleSize.ToString(SaveOptions.DisableFormatting));
        ReadWorkbookChildElement(saved, "webPublishing")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceWebPublishing.ToString(SaveOptions.DisableFormatting));
        ReadWorkbookChildElement(saved, "fileRecoveryPr")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceFileRecoveryProperties.ToString(SaveOptions.DisableFormatting));
        ReadWorkbookChildElement(saved, "webPublishObjects")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceWebPublishObjects.ToString(SaveOptions.DisableFormatting));
        ReadWorkbookChildElement(saved, "extLst")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceExtensionList.ToString(SaveOptions.DisableFormatting));

        saved.Position = 0;
        adapter.Load(saved).FileRecoveryProperties.Should().ContainSingle(properties =>
            properties.AutoRecover == true &&
            properties.CrashSave == true &&
            properties.RepairLoad == false);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidWorkbookOleSizeForSchemaValidity()
    {
        using var source = CreateWorkbookNativeMetadataSourcePackage();
        SetWorkbookOleSizeInvalidAttributes(source);
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
        var oleSize = ReadWorkbookChildElement(saved, "oleSize");
        oleSize.Attribute("ref")!.Value.Should().Be("A1:D12");
        oleSize.Attribute("customOleSizeFlag").Should().BeNull();
        oleSize.Element(oleSize.Name.Namespace + "nativeOleSizeChild").Should().BeNull();
        AssertWorkbookNativeMetadataModelReload(adapter, saved);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidWorkbookWebPublishingForSchemaValidity()
    {
        using var source = CreateWorkbookNativeMetadataSourcePackage();
        SetWorkbookWebPublishingInvalidAttributes(source);
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
        var webPublishing = ReadWorkbookChildElement(saved, "webPublishing");
        webPublishing.Attribute("css")!.Value.Should().Be("1");
        webPublishing.Attribute("targetScreenSize")!.Value.Should().Be("800x600");
        webPublishing.Attribute("dpi")!.Value.Should().Be("96");
        webPublishing.Attribute("codePage")!.Value.Should().Be("65001");
        webPublishing.Attribute("customWebPublishingFlag").Should().BeNull();
        webPublishing.Element(webPublishing.Name.Namespace + "nativeWebPublishingChild").Should().BeNull();
        AssertWorkbookNativeMetadataModelReload(adapter, saved);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidWorkbookWebPublishObjectsForSchemaValidity()
    {
        using var source = CreateWorkbookNativeMetadataSourcePackage();
        SetWorkbookWebPublishObjectsInvalidAttributes(source);
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
        var webPublishObjects = ReadWorkbookChildElement(saved, "webPublishObjects");
        webPublishObjects.Attribute("count")!.Value.Should().Be("1");
        webPublishObjects.Attribute("customWebPublishObjectsFlag").Should().BeNull();
        webPublishObjects.Element(webPublishObjects.Name.Namespace + "nativeWebPublishObjectsChild").Should().BeNull();
        var webPublishObject = webPublishObjects.Elements(webPublishObjects.Name.Namespace + "webPublishObject").Single();
        webPublishObject.Attribute("id")!.Value.Should().Be("1");
        webPublishObject.Attribute("divId")!.Value.Should().Be("FreeXWebPublish");
        webPublishObject.Attribute("destinationFile")!.Value.Should().Be("https://example.invalid/report.htm");
        webPublishObject.Attribute("customWebPublishObjectFlag").Should().BeNull();
        webPublishObject.Element(webPublishObject.Name.Namespace + "nativeWebPublishObjectChild").Should().BeNull();
        AssertWorkbookNativeMetadataModelReload(adapter, saved);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidWorkbookExtensionListForSchemaValidity()
    {
        using var source = CreateWorkbookNativeMetadataSourcePackage();
        SetWorkbookExtensionListInvalidAttributes(source);
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
        var extensionList = ReadWorkbookChildElement(saved, "extLst");
        extensionList.Attribute("customWorkbookExtLstFlag").Should().BeNull();
        extensionList.Element(extensionList.Name.Namespace + "nativeWorkbookExtLstChild").Should().BeNull();
        var extension = extensionList.Elements(extensionList.Name.Namespace + "ext").Single();
        extension.Attribute("uri")!.Value.Should().Be("{00112233-4455-6677-8899-AABBCCDDEEFF}");
        extension.Attribute("customWorkbookExtFlag").Should().BeNull();
        extension.ToString(SaveOptions.DisableFormatting).Should().Contain("FreeXWorkbookNativeMetadata");
        AssertWorkbookNativeMetadataModelReload(adapter, saved);
    }

    private static MemoryStream CreateWorkbookNativeMetadataSourcePackage()
    {
        var workbook = new Workbook("WorkbookNativeMetadataPatchSave");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("native workbook metadata"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(24));

        var stream = Save(workbook);
        AddWorkbookNativeMetadata(stream);
        stream.Position = 0;
        return stream;
    }

    private static void AddWorkbookNativeMetadata(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace x15Ns = "http://schemas.microsoft.com/office/spreadsheetml/2010/11/main";

        var workbookXml = LoadPackageXml(archive, "xl/workbook.xml");
        var root = workbookXml.Root!;
        ReplaceWorkbookChildInOrder(root, new XElement(
            workbookNs + "oleSize",
            new XAttribute("ref", "A1:D12")));
        ReplaceWorkbookChildInOrder(root, new XElement(
            workbookNs + "webPublishing",
            new XAttribute("css", "1"),
            new XAttribute("thicket", "0"),
            new XAttribute("longFileNames", "1"),
            new XAttribute("vml", "1"),
            new XAttribute("allowPng", "1"),
            new XAttribute("targetScreenSize", "800x600"),
            new XAttribute("dpi", "96")));
        ReplaceWorkbookChildInOrder(root, new XElement(
            workbookNs + "fileRecoveryPr",
            new XAttribute("autoRecover", "1"),
            new XAttribute("crashSave", "1"),
            new XAttribute("repairLoad", "0")));
        ReplaceWorkbookChildInOrder(root, new XElement(
            workbookNs + "webPublishObjects",
            new XAttribute("count", "1"),
            new XElement(
                workbookNs + "webPublishObject",
                new XAttribute("id", "1"),
                new XAttribute("divId", "FreeXWebPublish"),
                new XAttribute("sourceObject", "Data"),
                new XAttribute("destinationFile", "https://example.invalid/report.htm"),
                new XAttribute("title", "Report"),
                new XAttribute("autoRepublish", "0"))));
        ReplaceWorkbookChildInOrder(root, new XElement(
            workbookNs + "extLst",
            new XElement(
                workbookNs + "ext",
                new XAttribute("uri", "{00112233-4455-6677-8899-AABBCCDDEEFF}"),
                new XElement(
                    x15Ns + "futureMetadata",
                    new XAttribute(XNamespace.Xmlns + "x15", x15Ns),
                    new XAttribute("name", "FreeXWorkbookNativeMetadata")))));

        ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
    }

    private static void SetWorkbookOleSizeInvalidAttributes(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var workbookXml = LoadPackageXml(archive, "xl/workbook.xml");
        var oleSize = workbookXml.Root!.Element(workbookNs + "oleSize")!;
        oleSize.SetAttributeValue("ref", " a1:d12 ");
        oleSize.SetAttributeValue("customOleSizeFlag", "removed");
        oleSize.Add(new XElement(workbookNs + "nativeOleSizeChild"));
        ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
    }

    private static void SetWorkbookWebPublishingInvalidAttributes(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var workbookXml = LoadPackageXml(archive, "xl/workbook.xml");
        var webPublishing = workbookXml.Root!.Element(workbookNs + "webPublishing")!;
        webPublishing.SetAttributeValue("dpi", " 96 ");
        webPublishing.SetAttributeValue("codePage", " 65001 ");
        webPublishing.SetAttributeValue("customWebPublishingFlag", "removed");
        webPublishing.Add(new XElement(workbookNs + "nativeWebPublishingChild"));
        ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
    }

    private static void SetWorkbookWebPublishObjectsInvalidAttributes(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        var webPublishObjects = workbookXml.Root!.Element(workbookNs + "webPublishObjects")!;
        webPublishObjects.SetAttributeValue("count", " 1 ");
        webPublishObjects.SetAttributeValue("customWebPublishObjectsFlag", "removed");
        webPublishObjects.Add(new XElement(workbookNs + "nativeWebPublishObjectsChild"));
        var webPublishObject = webPublishObjects.Element(workbookNs + "webPublishObject")!;
        webPublishObject.SetAttributeValue("id", " 1 ");
        webPublishObject.SetAttributeValue("divId", " FreeXWebPublish ");
        webPublishObject.SetAttributeValue("customWebPublishObjectFlag", "removed");
        webPublishObject.Add(new XElement(workbookNs + "nativeWebPublishObjectChild"));
        ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
    }

    private static void SetWorkbookExtensionListInvalidAttributes(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        var extensionList = workbookXml.Root!.Element(workbookNs + "extLst")!;
        extensionList.SetAttributeValue("customWorkbookExtLstFlag", "removed");
        extensionList.Add(new XElement(workbookNs + "nativeWorkbookExtLstChild"));
        var extension = extensionList.Element(workbookNs + "ext")!;
        extension.SetAttributeValue("uri", " {00112233-4455-6677-8899-AABBCCDDEEFF} ");
        extension.SetAttributeValue("customWorkbookExtFlag", "removed");
        extensionList.Add(new XElement(workbookNs + "ext", new XAttribute("uri", " ")));
        ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
    }

    private static void AssertWorkbookNativeMetadataPackage(Stream stream)
    {
        AssertWorkbookNativeMetadataOrder(ReadPackageRootElement(stream, "xl/workbook.xml"));
        ReadWorkbookChildElement(stream, "oleSize")
            .Attribute("ref")!
            .Value
            .Should()
            .Be("A1:D12");
        ReadWorkbookChildElement(stream, "webPublishing")
            .Attribute("targetScreenSize")!
            .Value
            .Should()
            .Be("800x600");
        ReadWorkbookChildElement(stream, "fileRecoveryPr")
            .Attribute("autoRecover")!
            .Value
            .Should()
            .Be("1");

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var webPublishObject = ReadWorkbookChildElement(stream, "webPublishObjects")
            .Elements(workbookNs + "webPublishObject")
            .Single();
        webPublishObject.Attribute("divId")!.Value.Should().Be("FreeXWebPublish");
        webPublishObject.Attribute("destinationFile")!.Value.Should().Be("https://example.invalid/report.htm");
        ReadWorkbookChildElement(stream, "extLst")
            .Element(workbookNs + "ext")!
            .Attribute("uri")!
            .Value
            .Should()
            .Be("{00112233-4455-6677-8899-AABBCCDDEEFF}");
    }

    private static void AssertWorkbookNativeMetadataModelReload(XlsxFileAdapter adapter, Stream stream)
    {
        stream.Position = 0;
        var reloaded = adapter.Load(stream);
        reloaded.FileRecoveryProperties.Should().ContainSingle(properties =>
            properties.AutoRecover == true &&
            properties.CrashSave == true &&
            properties.RepairLoad == false);

        var sheet = reloaded.GetSheetAt(0);
        sheet.Name.Should().Be("Data");
        sheet.GetValue(1, 1).Should().Be(new TextValue("native workbook metadata"));
        sheet.GetValue(2, 2).Should().Be(new NumberValue(24));
        sheet.GetValue(3, 3).Should().Be(new NumberValue(42));
    }

    private static void AssertWorkbookNativeMetadataOrder(XElement workbookRoot)
    {
        var childNames = workbookRoot.Elements().Select(element => element.Name.LocalName).ToList();
        AssertWorkbookChildPrecedes(childNames, "calcPr", "oleSize");
        AssertWorkbookChildPrecedes(childNames, "oleSize", "webPublishing");
        AssertWorkbookChildPrecedes(childNames, "webPublishing", "fileRecoveryPr");
        AssertWorkbookChildPrecedes(childNames, "fileRecoveryPr", "webPublishObjects");
        AssertWorkbookChildPrecedes(childNames, "webPublishObjects", "extLst");
    }

    private static void AssertWorkbookChildPrecedes(
        List<string> childNames,
        string firstName,
        string secondName)
    {
        var firstIndex = childNames.IndexOf(firstName);
        var secondIndex = childNames.IndexOf(secondName);
        if (firstIndex >= 0 && secondIndex >= 0)
            firstIndex.Should().BeLessThan(secondIndex);
    }

    private static void ReplaceWorkbookChildInOrder(XElement root, XElement child)
    {
        root.Elements(child.Name).Remove();
        var insertBefore = root.Elements()
            .FirstOrDefault(element => WorkbookChildSchemaOrder(element) > WorkbookChildSchemaOrder(child));
        if (insertBefore is null)
            root.Add(child);
        else
            insertBefore.AddBeforeSelf(child);
    }

    private static int WorkbookChildSchemaOrder(XElement element) =>
        element.Name.LocalName switch
        {
            "revisionPtr" => 0,
            "fileVersion" => 1,
            "fileSharing" => 2,
            "workbookPr" => 3,
            "workbookProtection" => 4,
            "bookViews" => 5,
            "sheets" => 6,
            "functionGroups" => 7,
            "externalReferences" => 8,
            "definedNames" => 9,
            "calcPr" => 10,
            "oleSize" => 11,
            "customWorkbookViews" => 12,
            "pivotCaches" => 13,
            "smartTagPr" => 14,
            "smartTagTypes" => 15,
            "webPublishing" => 16,
            "fileRecoveryPr" => 17,
            "webPublishObjects" => 18,
            "extLst" => 100,
            _ => 90
        };
}
