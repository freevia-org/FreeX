using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxLoadedWorkbookPatchSaveTests
{
    public static TheoryData<ScalarValue, string?, string?> FormulaCachedValueCases => new()
    {
        { new NumberValue(99.5), null, "99.5" },
        { new TextValue("cached text"), "str", "cached text" },
        { new BoolValue(true), "b", "1" },
        { new ErrorValue("#N/A"), "e", "#N/A" },
        { BlankValue.Instance, null, null }
    };

    public static TheoryData<string> AttributedFormulaElements => new()
    {
        """<f t="shared" si="0">1+1</f>""",
        """<f t="array" ref="A1:A1" ca="1">1+1</f>"""
    };

    private static void PrepareLoadedWorkbookForEdit(Workbook workbook)
    {
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);
    }

    [Fact]
    public void Save_LoadedWorkbookWithExistingLiteralCellEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreateSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("  patched value  "));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/styles.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/styles.xml"));
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("  patched value  ");
        ReadCellTextSpaceMode(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("preserve");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        adapter.Load(reloadStream)
            .GetSheetAt(0)
            .GetCell(1, 1)!
            .Value
            .Should()
            .Be(new TextValue("  patched value  "));
    }

    [Fact]
    public void Save_LoadedWorkbookWithWorkbookMetadataPart_PreservesMetadataPackageGraphAndCellIndexes()
    {
        var sourceBytes = AddWorkbookMetadataPackageParts(CreateSourcePackage());
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("patched metadata-backed value"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        ReadPackageEntry(savedBytes, "xl/metadata.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/metadata.xml"));
        ReadContentTypeOverrides(savedBytes)
            .Should()
            .Contain("/xl/metadata.xml");
        WorkbookRelationshipsContain(
                savedBytes,
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/sheetMetadata",
                "metadata.xml")
            .Should()
            .BeTrue();
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("patched metadata-backed value");
        ReadCellAttribute(savedBytes, "xl/worksheets/sheet1.xml", "A1", "cm")
            .Should()
            .Be("1");
        ReadCellAttribute(savedBytes, "xl/worksheets/sheet1.xml", "A1", "vm")
            .Should()
            .Be("1");
    }

    [Fact]
    public void Save_LoadedWorkbookWithOfficeAddIn_PreservesWebExtensionPackageGraph()
    {
        var sourceBytes = AddOfficeWebExtensionPackageGraph(CreateSourcePackage());
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("patched add-in workbook value"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        ReadPackageEntry(savedBytes, "xl/webextensions/taskpanes.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/webextensions/taskpanes.xml"));
        ReadPackageEntry(savedBytes, "xl/webextensions/_rels/taskpanes.xml.rels")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/webextensions/_rels/taskpanes.xml.rels"));
        ReadPackageEntry(savedBytes, "xl/webextensions/webextension1.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/webextensions/webextension1.xml"));
        ReadContentTypeOverrides(savedBytes)
            .Should()
            .Contain(new[]
            {
                "/xl/webextensions/taskpanes.xml",
                "/xl/webextensions/webextension1.xml"
            });
        WorkbookRelationshipsContain(
                savedBytes,
                "http://schemas.microsoft.com/office/2011/relationships/webextensiontaskpanes",
                "webextensions/taskpanes.xml")
            .Should()
            .BeTrue();
        PackageRelationshipsContain(
                savedBytes,
                "xl/webextensions/_rels/taskpanes.xml.rels",
                "http://schemas.microsoft.com/office/2011/relationships/webextension",
                "webextension1.xml")
            .Should()
            .BeTrue();
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("patched add-in workbook value");
    }

    [Fact]
    public void Save_LoadedWorkbookFullSaveWithOfficeAddIn_PreservesWebExtensionPackageGraph()
    {
        var sourceBytes = AddOfficeWebExtensionPackageGraph(CreateSourcePackage());
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        var styleId = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            FillColor = new CellColor(221, 235, 247)
        });
        sheet.GetCell(1, 1)!.StyleId = styleId;

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);

        using var archive = new ZipArchive(new MemoryStream(savedBytes), ZipArchiveMode.Read);
        XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var workbookRelationshipsXml = LoadPackageXml(archive, "xl/_rels/workbook.xml.rels");
        var workbookRelationshipIds = workbookRelationshipsXml.Root!
            .Elements(relationshipNs + "Relationship")
            .Select(element => element.Attribute("Id")?.Value)
            .OfType<string>()
            .ToList();
        workbookRelationshipIds.Should().OnlyHaveUniqueItems();
        workbookRelationshipsXml.Root!
            .Elements(relationshipNs + "Relationship")
            .Should()
            .ContainSingle(element =>
                (string?)element.Attribute("Type") == "http://schemas.microsoft.com/office/2011/relationships/webextensiontaskpanes" &&
                (string?)element.Attribute("Target") == "webextensions/taskpanes.xml");

        AssertWebExtensionTaskpaneReferenceIsBound(savedBytes);
        ReadContentTypeOverrides(savedBytes)
            .Should()
            .Contain(new[]
            {
                "/xl/webextensions/taskpanes.xml",
                "/xl/webextensions/webextension1.xml"
            });
    }

    [Fact]
    public void Save_LoadedWorkbookWithXmlMaps_PreservesMapInfoPackageGraph()
    {
        var sourceBytes = AddWorkbookXmlMapsPackageGraph(CreateSourcePackage());
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("patched xml map workbook value"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        ReadPackageEntry(savedBytes, "xl/xmlMaps.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/xmlMaps.xml"));
        ReadContentTypeOverrides(savedBytes)
            .Should()
            .Contain("/xl/xmlMaps.xml");
        WorkbookRelationshipsContain(
                savedBytes,
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/xmlMaps",
                "xmlMaps.xml")
            .Should()
            .BeTrue();
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("patched xml map workbook value");
    }

    [Fact]
    public void Save_LoadedWorkbookWithRichDataGraph_PreservesRichDataPartsRelationshipsAndContentTypes()
    {
        var sourceBytes = AddRichDataPackageParts(CreateSourcePackage());
        using (var inspectionSource = new MemoryStream(sourceBytes, writable: false))
        {
            XlsxFeatureInspector.Inspect(inspectionSource).Features
                .Select(feature => feature.Kind)
                .Should()
                .NotContain(
                    XlsxUnsupportedFeatureKind.LinkedDataTypes,
                    "the preserved rich-data graph describes Excel local images, not service-linked data types");
        }
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("patched rich data workbook"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        foreach (var path in new[]
                 {
                     "xl/metadata.xml",
                     "xl/richData/rdrichvalue.xml",
                     "xl/richData/rdrichvaluestructure.xml",
                     "xl/richData/rdRichValueTypes.xml",
                     "xl/richData/richValueRel.xml",
                     "xl/richData/_rels/richValueRel.xml.rels",
                     "xl/media/image1.png"
                 })
        {
            ReadPackageEntry(savedBytes, path)
                .Should()
                .Equal(ReadPackageEntry(sourceBytes, path));
        }

        ReadContentTypeOverrides(savedBytes)
            .Should()
            .Contain(new[]
            {
                "/xl/metadata.xml",
                "/xl/richData/rdrichvalue.xml",
                "/xl/richData/rdrichvaluestructure.xml",
                "/xl/richData/rdRichValueTypes.xml",
                "/xl/richData/richValueRel.xml"
            });
        WorkbookRelationshipsContain(
                savedBytes,
                "http://schemas.microsoft.com/office/2017/06/relationships/rdRichValue",
                "richData/rdrichvalue.xml")
            .Should()
            .BeTrue();
        WorkbookRelationshipsContain(
                savedBytes,
                "http://schemas.microsoft.com/office/2017/06/relationships/rdRichValueStructure",
                "richData/rdrichvaluestructure.xml")
            .Should()
            .BeTrue();
        WorkbookRelationshipsContain(
                savedBytes,
                "http://schemas.microsoft.com/office/2017/06/relationships/rdRichValueTypes",
                "richData/rdRichValueTypes.xml")
            .Should()
            .BeTrue();
        WorkbookRelationshipsContain(
                savedBytes,
                "http://schemas.microsoft.com/office/2022/10/relationships/richValueRel",
                "richData/richValueRel.xml")
            .Should()
            .BeTrue();
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("patched rich data workbook");
    }

    [Fact]
    public void Save_LoadedWorkbookWithUnpreparedDirectEdit_FallsBackOnPatchValidationDelta()
    {
        var sourceBytes = CreateSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("direct edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        adapter.LastSaveDiagnostics.Reason.Should().Be("change_unsupported_model_delta");
    }

    [Fact]
    public void Save_LoadedUnchangedWorkbook_ReportsSourceCopyDiagnostics()
    {
        var sourceBytes = CreateSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourceCopy);
        adapter.LastSaveDiagnostics.PathLabel.Should().Be("source_copy");
        adapter.LastSaveDiagnostics.Reason.Should().Be("model_unchanged");
    }

    [Fact]
    public void Save_LoadedUnchangedWorkbookWithDigitalSignatures_CopiesSourcePackage()
    {
        var sourceBytes = AddDigitalSignaturePackageGraph(CreateSourcePackage());
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeFalse();
        blockReason.Should().Be("package_guard_digital_signatures");

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourceCopy);
        adapter.LastSaveDiagnostics.Reason.Should().Be("model_unchanged");
        saved.ToArray().Should().Equal(sourceBytes);
    }

    [Fact]
    public void Save_LoadedEditedWorkbookWithDigitalSignatures_RemovesInvalidatedSignaturePackageGraph()
    {
        var sourceBytes = AddDigitalSignaturePackageGraph(CreateSourcePackage());
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeFalse();
        blockReason.Should().Be("package_guard_digital_signatures");

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("edited signed workbook"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        adapter.LastSaveDiagnostics.Reason.Should().Be("package_guard_digital_signatures");
        PackageHasEntry(savedBytes, "_xmlsignatures/origin.sigs").Should().BeFalse();
        PackageHasEntry(savedBytes, "_xmlsignatures/sig1.xml").Should().BeFalse();
        PackageHasEntry(savedBytes, "_xmlsignatures/_rels/origin.sigs.rels").Should().BeFalse();
        ReadContentTypeOverrides(savedBytes)
            .Should()
            .NotContain(new[]
            {
                "/_xmlsignatures/origin.sigs",
                "/_xmlsignatures/sig1.xml"
            });
        RootRelationshipsContain(
                savedBytes,
                "http://schemas.openxmlformats.org/package/2006/relationships/digital-signature/origin",
                "_xmlsignatures/origin.sigs")
            .Should()
            .BeFalse();
        using var reload = new MemoryStream(savedBytes, writable: false);
        adapter.Load(reload)
            .GetSheetAt(0)
            .GetCell(1, 1)!
            .Value
            .Should()
            .Be(new TextValue("edited signed workbook"));
    }

    [Fact]
    public void Save_LoadedUnchangedWorkbookAboveLegacyFingerprintLimit_CopiesSourcePackage()
    {
        var sourceBytes = CreateDenseSourcePackage(rowCount: 100, columnCount: 251);
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);

        workbook.Sheets.Sum(sheet => sheet.CellCount).Should().BeGreaterThan(25_000);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.PathLabel.Should().Be("source_copy");
        adapter.LastSaveDiagnostics.Reason.Should().Be("model_unchanged");
        saved.ToArray().Should().Equal(sourceBytes);
    }

    [Fact]
    public void Save_NewWorkbook_ReportsNoSourcePackageFullSaveDiagnostics()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("new"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        adapter.LastSaveDiagnostics.PathLabel.Should().Be("full_save");
        adapter.LastSaveDiagnostics.Reason.Should().Be("no_source_package");
    }

    [Fact]
    public void Save_LoadedWorkbookWithNewLiteralCellEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreateSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new TextValue("new value"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.PathLabel.Should().Be("source_patch");
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        adapter.LastSaveDiagnostics.CellChangeCount.Should().Be(1);
        adapter.LastSaveDiagnostics.TotalPatchChangeCount.Should().Be(1);
        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/styles.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/styles.xml"));
        ReadWorksheetDimension(savedBytes, "xl/worksheets/sheet1.xml")
            .Should()
            .Be("A1:D4");
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "D4")
            .Should()
            .Be("new value");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        adapter.Load(reloadStream)
            .GetSheetAt(0)
            .GetCell(4, 4)!
            .Value
            .Should()
            .Be(new TextValue("new value"));
    }

    [Fact]
    public void Save_LoadedWorkbookWithExistingCellStyleEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreateStyledSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        var sourceStyleCell = sheet.GetCell(1, 2);
        sourceStyleCell.Should().NotBeNull();
        sourceStyleCell!.StyleId.Should().NotBe(StyleId.Default);
        var patchedCell = sheet.GetCell(1, 1);
        patchedCell.Should().NotBeNull();
        patchedCell!.StyleId = sourceStyleCell.StyleId;

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/styles.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/styles.xml"));
        ReadCellStyleIndex(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be(ReadCellStyleIndex(sourceBytes, "xl/worksheets/sheet1.xml", "B1"));

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloaded = adapter.Load(reloadStream);
        var reloadedSheet = reloaded.GetSheetAt(0);
        reloadedSheet.GetCell(1, 1)!.Value
            .Should()
            .Be(new TextValue("plain"));
        reloaded.GetStyle(reloadedSheet.GetCell(1, 1)!.StyleId)
            .Should()
            .Be(reloaded.GetStyle(reloadedSheet.GetCell(1, 2)!.StyleId));
    }

    [Fact]
    public void Save_LoadedWorkbookWithExistingStyleOnlyStyleEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreateStyledSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        var styleOnlyStyleId = sheet.GetStyleOnly(1, 4);
        styleOnlyStyleId.Should().NotBeNull();
        var patchedCell = sheet.GetCell(1, 1);
        patchedCell.Should().NotBeNull();
        patchedCell!.StyleId = styleOnlyStyleId!.Value;

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/styles.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/styles.xml"));
        ReadCellStyleIndex(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be(ReadCellStyleIndex(sourceBytes, "xl/worksheets/sheet1.xml", "D1"));

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloaded = adapter.Load(reloadStream);
        var reloadedSheet = reloaded.GetSheetAt(0);
        var reloadedStyleOnlyStyleId = reloadedSheet.GetStyleOnly(1, 4);
        reloadedStyleOnlyStyleId.Should().NotBeNull();
        reloaded.GetStyle(reloadedSheet.GetCell(1, 1)!.StyleId)
            .Should()
            .Be(reloaded.GetStyle(reloadedStyleOnlyStyleId!.Value));
    }

    [Fact]
    public void Save_LoadedWorkbookWithNewLiteralInSourceStyleOnlyCell_PatchesSourcePackage()
    {
        var sourceBytes = CreateStyledSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        var styleOnlyStyleId = sheet.GetStyleOnly(1, 4);
        styleOnlyStyleId.Should().NotBeNull();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new Cell
        {
            Value = new TextValue("new styled value"),
            StyleId = styleOnlyStyleId!.Value
        });

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.PathLabel.Should().Be("source_patch");
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        adapter.LastSaveDiagnostics.CellChangeCount.Should().Be(1);
        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/styles.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/styles.xml"));
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "D1")
            .Should()
            .Be("new styled value");
        ReadCellStyleIndex(savedBytes, "xl/worksheets/sheet1.xml", "D1")
            .Should()
            .Be(ReadCellStyleIndex(sourceBytes, "xl/worksheets/sheet1.xml", "D1"));

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloaded = adapter.Load(reloadStream);
        var reloadedSheet = reloaded.GetSheetAt(0);
        reloadedSheet.GetStyleOnly(1, 4).Should().BeNull();
        reloadedSheet.GetCell(1, 4)!.Value.Should().Be(new TextValue("new styled value"));
        reloaded.GetStyle(reloadedSheet.GetCell(1, 4)!.StyleId)
            .Should()
            .Be(workbook.GetStyle(styleOnlyStyleId.Value));
    }

    [Fact]
    public void Save_LoadedWorkbookWithNewLiteralInSourceStyleOnlyCellAndChangedStyle_FallsBackToFullSave()
    {
        var sourceBytes = CreateStyledSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.GetStyleOnly(1, 4).Should().NotBeNull();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("default styled value"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        adapter.LastSaveDiagnostics.PathLabel.Should().Be("full_save");
        adapter.LastSaveDiagnostics.Reason.Should().Be("change_inserted_style_only_cell");
    }

    [Fact]
    public void Save_LoadedWorkbookWithNewCellStyleEdit_FallsBackToFullSave()
    {
        var sourceBytes = CreateSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        var styleId = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            FillColor = new CellColor(221, 235, 247)
        });
        sheet.GetCell(1, 1)!.StyleId = styleId;

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        adapter.LastSaveDiagnostics.PathLabel.Should().Be("full_save");
        adapter.LastSaveDiagnostics.Reason.Should().Be("change_new_style");
        ReadPackageEntry(savedBytes, "xl/styles.xml")
            .Should()
            .NotEqual(ReadPackageEntry(sourceBytes, "xl/styles.xml"));

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloaded = adapter.Load(reloadStream);
        reloaded.GetStyle(reloaded.GetSheetAt(0).GetCell(1, 1)!.StyleId)
            .Bold
            .Should()
            .BeTrue();
    }

    [Fact]
    public void Save_LoadedWorkbookWithUnusedStyleTableEdit_FallsBackToFullSave()
    {
        var sourceBytes = CreateSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("patched value"));
        workbook.RegisterStyle(new CellStyle
        {
            Italic = true,
            FillColor = new CellColor(255, 199, 206)
        });

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        adapter.LastSaveDiagnostics.PathLabel.Should().Be("full_save");
        adapter.LastSaveDiagnostics.Reason.Should().Be("change_unsupported_model_delta");

        saved.Position = 0;
        adapter.Load(saved)
            .GetSheetAt(0)
            .GetCell(1, 1)!
            .Value
            .Should()
            .Be(new TextValue("patched value"));
    }

    [Fact]
    public void Save_LoadedWorkbookWithRowColumnDimensionEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreateSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.RowHeights[2] = 32;
        sheet.HiddenRows.Add(4);
        sheet.ColumnWidths[2] = 18.5;
        sheet.HiddenCols.Add(3);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/styles.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/styles.xml"));
        ReadRowAttribute(savedBytes, "xl/worksheets/sheet1.xml", 2, "ht")
            .Should()
            .Be("24");
        ReadRowAttribute(savedBytes, "xl/worksheets/sheet1.xml", 2, "customHeight")
            .Should()
            .Be("1");
        ReadRowAttribute(savedBytes, "xl/worksheets/sheet1.xml", 4, "hidden")
            .Should()
            .Be("1");
        ReadColumnAttribute(savedBytes, "xl/worksheets/sheet1.xml", 2, "width")
            .Should()
            .Be("18.5");
        ReadColumnAttribute(savedBytes, "xl/worksheets/sheet1.xml", 2, "customWidth")
            .Should()
            .Be("1");
        ReadColumnAttribute(savedBytes, "xl/worksheets/sheet1.xml", 3, "hidden")
            .Should()
            .Be("1");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloaded = adapter.Load(reloadStream).GetSheetAt(0);
        reloaded.RowHeights[2].Should().BeApproximately(32, 0.0001);
        reloaded.HiddenRows.Should().Contain(4u);
        reloaded.ColumnWidths[2].Should().BeApproximately(18.5, 0.0001);
        reloaded.HiddenCols.Should().Contain(3u);
    }

    [Fact]
    public void Save_LoadedWorkbookWithMergedRegionEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreateMergedRegionSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.MergedRegions.Should().HaveCount(2);
        sheet.RemoveMergedRegion(sheet.MergedRegions[0]).Should().BeTrue();
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 4, 2)));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/styles.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/styles.xml"));
        ReadMergeCellsAttribute(savedBytes, "xl/worksheets/sheet1.xml", "nativeMergeContainerAttr")
            .Should()
            .BeNull();
        ReadMergeCellsAttribute(savedBytes, "xl/worksheets/sheet1.xml", "count")
            .Should()
            .Be("2");
        ReadMergeCellReferences(savedBytes, "xl/worksheets/sheet1.xml")
            .Should()
            .Equal("C1:D1", "A3:B4");
        ReadMergeCellAttribute(savedBytes, "xl/worksheets/sheet1.xml", "C1:D1", "nativeMergeCellAttr")
            .Should()
            .BeNull();
        ReadMergeCellAttribute(savedBytes, "xl/worksheets/sheet1.xml", "A3:B4", "nativeMergeCellAttr")
            .Should()
            .BeNull();

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloaded = adapter.Load(reloadStream).GetSheetAt(0);
        reloaded.MergedRegions
            .Select(region => region.ToString())
            .Should()
            .Equal("C1:D1", "A3:B4");
    }

    // R60-io-sheet-dimension-usedrange-6-2: a patch-save whose ONLY change is a new merge over
    // previously blank/default cells never extended <dimension>, because UpdateDimension (the only
    // place that recomputes it) only runs when the patch has an actual cell-value change --
    // ApplyMergeRegionChanges never touched <dimension> at all. CreateSourcePackage's dimension is
    // "A1:C3" (A1/B2/C3 are the only populated cells); merging E5:F6 -- blank cells entirely outside
    // that range, with no other edit -- must extend the saved <dimension> to "A1:F6" so Ctrl+End /
    // the used-range extent stays consistent with the newly written <mergeCells>.
    [Fact]
    public void Save_LoadedWorkbookWithNewMergeOverBlankCellsBeyondDimension_ExtendsWorksheetDimension()
    {
        var sourceBytes = CreateSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        ReadWorksheetDimension(sourceBytes, "xl/worksheets/sheet1.xml").Should().Be("A1:C3");

        var sheet = workbook.GetSheetAt(0);
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 5, 5),
            new CellAddress(sheet.Id, 6, 6)));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        ReadMergeCellReferences(savedBytes, "xl/worksheets/sheet1.xml")
            .Should()
            .Equal("E5:F6");
        ReadWorksheetDimension(savedBytes, "xl/worksheets/sheet1.xml")
            .Should()
            .Be("A1:F6", "the new merge extends beyond the sheet's prior A1:C3 used range");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloaded = adapter.Load(reloadStream).GetSheetAt(0);
        reloaded.MergedRegions
            .Select(region => region.ToString())
            .Should()
            .Equal("E5:F6");
    }

    // Sibling no-regression test: a merge that stays WITHIN the sheet's existing used range must
    // leave <dimension> untouched -- the fix only ever grows the ref, it must never shrink or
    // rewrite it when the merge doesn't actually extend past the current bounds.
    [Fact]
    public void Save_LoadedWorkbookWithNewMergeWithinDimension_LeavesWorksheetDimensionUnchanged()
    {
        var sourceBytes = CreateSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        ReadWorksheetDimension(sourceBytes, "xl/worksheets/sheet1.xml").Should().Be("A1:C3");

        var sheet = workbook.GetSheetAt(0);
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 2)));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        ReadMergeCellReferences(savedBytes, "xl/worksheets/sheet1.xml")
            .Should()
            .Equal("A1:B2");
        ReadWorksheetDimension(savedBytes, "xl/worksheets/sheet1.xml")
            .Should()
            .Be("A1:C3", "a merge fully inside the existing used range must not alter <dimension>");
    }

    [Fact]
    public void Save_LoadedWorkbookWithInternalHyperlinkEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreateInternalHyperlinkSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.Hyperlinks[address].Should().Be("Data!B2");
        sheet.HyperlinkMetadata[address].Should().Be(new HyperlinkMetadata(
            HyperlinkTargetKind.PlaceInThisDocument,
            "Jump original",
            "Data!B2"));
        sheet.Hyperlinks[address] = "Data!C3";
        sheet.HyperlinkMetadata[address] = new HyperlinkMetadata(
            HyperlinkTargetKind.PlaceInThisDocument,
            "Jump patched",
            "Data!C3");

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/styles.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/styles.xml"));
        ReadHyperlinksAttribute(savedBytes, "xl/worksheets/sheet1.xml", "nativeHyperlinksAttr")
            .Should()
            .BeNull();
        ReadHyperlinkAttribute(savedBytes, "xl/worksheets/sheet1.xml", "A1", "location")
            .Should()
            .Be("Data!C3");
        ReadHyperlinkAttribute(savedBytes, "xl/worksheets/sheet1.xml", "A1", "tooltip")
            .Should()
            .Be("Jump patched");
        ReadHyperlinkAttribute(savedBytes, "xl/worksheets/sheet1.xml", "A1", "display")
            .Should()
            .Be("Jump display");
        ReadHyperlinkAttribute(savedBytes, "xl/worksheets/sheet1.xml", "A1", "customAttr")
            .Should()
            .BeNull();

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloadedSheet = adapter.Load(reloadStream).GetSheetAt(0);
        var reloadedAddress = new CellAddress(reloadedSheet.Id, 1, 1);
        reloadedSheet.Hyperlinks[reloadedAddress].Should().Be("Data!C3");
        reloadedSheet.HyperlinkMetadata[reloadedAddress].Should().Be(new HyperlinkMetadata(
            HyperlinkTargetKind.PlaceInThisDocument,
            "Jump patched",
            "Data!C3"));
    }

    [Fact]
    public void Save_LoadedWorkbookWithLegacyCommentTextEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreateLegacyCommentSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        var address = new CellAddress(sheet.Id, 2, 3);
        sheet.Comments[address].Should().Be("Original note");
        sheet.Comments[address] = "Patched note";

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/worksheets/sheet1.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/worksheets/sheet1.xml"));
        ReadPackageEntry(savedBytes, "xl/worksheets/_rels/sheet1.xml.rels")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/worksheets/_rels/sheet1.xml.rels"));
        ReadPackageEntry(savedBytes, "xl/drawings/vmlDrawing1.vml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/drawings/vmlDrawing1.vml"));
        ReadCommentText(savedBytes, "xl/comments1.xml", "C2")
            .Should()
            .Be("Patched note");
        ReadCommentAttribute(savedBytes, "xl/comments1.xml", "C2", "nativeCommentAttr")
            .Should()
            .Be("kept-comment");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloadedSheet = adapter.Load(reloadStream).GetSheetAt(0);
        var reloadedAddress = new CellAddress(reloadedSheet.Id, 2, 3);
        reloadedSheet.Comments[reloadedAddress].Should().Be("Patched note");
    }

    [Fact]
    public void Save_LoadedWorkbookWithLegacyCommentAndCellEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreateLegacyCommentSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("cell patched"));
        sheet.Comments[new CellAddress(sheet.Id, 2, 3)] = "Comment patched";

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("cell patched");
        ReadCommentText(savedBytes, "xl/comments1.xml", "C2")
            .Should()
            .Be("Comment patched");
        ReadPackageEntry(savedBytes, "xl/drawings/vmlDrawing1.vml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/drawings/vmlDrawing1.vml"));
    }

    [Fact]
    public void Save_LoadedWorkbookWithLegacyCommentAndPictureVmlDrawing_PatchesSourcePackage()
    {
        var sourceBytes = AddPictureShapeToLegacyCommentVml(CreateLegacyCommentSourcePackage());
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("cell patched"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("cell patched");
        ReadPackageEntry(savedBytes, "xl/drawings/vmlDrawing1.vml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/drawings/vmlDrawing1.vml"));
        ReadPackageEntry(savedBytes, "xl/drawings/_rels/vmlDrawing1.vml.rels")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/drawings/_rels/vmlDrawing1.vml.rels"));
        ReadPackageEntry(savedBytes, "xl/media/image1.png")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/media/image1.png"));
    }

    [Fact]
    public void Save_LoadedWorkbookWithPictureOnlyLegacyDrawing_PatchesSourcePackage()
    {
        var sourceBytes = CreatePictureOnlyLegacyDrawingSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("cell patched"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        ReadPackageEntry(savedBytes, "xl/worksheets/_rels/sheet1.xml.rels")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/worksheets/_rels/sheet1.xml.rels"));
        ReadPackageEntry(savedBytes, "xl/drawings/vmlDrawing1.vml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/drawings/vmlDrawing1.vml"));
        ReadPackageEntry(savedBytes, "xl/drawings/_rels/vmlDrawing1.vml.rels")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/drawings/_rels/vmlDrawing1.vml.rels"));
        ReadPackageEntry(savedBytes, "xl/media/image1.png")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/media/image1.png"));
    }

    [Fact]
    public void Save_LoadedWorkbookWithAddedLegacyComment_FallsBackToFullSave()
    {
        var sourceBytes = CreateLegacyCommentSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.Comments[new CellAddress(sheet.Id, 1, 1)] = "New note";

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .NotEqual(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
    }

    [Fact]
    public void Save_LoadedWorkbookWithNonNoteVmlLegacyCommentEdit_FallsBackToFullSave()
    {
        var sourceBytes = CreateLegacyCommentSourcePackage(vmlObjectType: "Button");
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeFalse();
        blockReason.Should().Be("package_guard_legacy_drawing_vml");

        var sheet = workbook.GetSheetAt(0);
        sheet.Comments[new CellAddress(sheet.Id, 2, 3)] = "Patched note";

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .NotEqual(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
    }

    [Fact]
    public void Save_LoadedWorkbookWithStructuredTableDataBodyEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreateStructuredTableSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.StructuredTables.Should().ContainSingle();
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(99));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/worksheets/_rels/sheet1.xml.rels")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/worksheets/_rels/sheet1.xml.rels"));
        ReadPackageEntry(savedBytes, "xl/tables/table1.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/tables/table1.xml"));
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "B2")
            .Should()
            .Be("99");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloadedSheet = adapter.Load(reloadStream).GetSheetAt(0);
        reloadedSheet.GetCell(2, 2)!.Value.Should().Be(new NumberValue(99));
        reloadedSheet.StructuredTables.Should().ContainSingle()
            .Which.Range.ToString().Should().Be("A1:B3");
    }

    [Fact]
    public void Save_LoadedWorkbookWithStructuredTableOutsideTableEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreateStructuredTableSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.StructuredTables.Should().ContainSingle();
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new TextValue("outside patched"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/worksheets/_rels/sheet1.xml.rels")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/worksheets/_rels/sheet1.xml.rels"));
        ReadPackageEntry(savedBytes, "xl/tables/table1.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/tables/table1.xml"));
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "C4")
            .Should()
            .Be("outside patched");
    }

    [Fact]
    public void Save_LoadedWorkbookWithStructuredTableNewCellOutsideTable_PatchesSourcePackage()
    {
        var sourceBytes = CreateStructuredTableSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.StructuredTables.Should().ContainSingle();
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new TextValue("new outside"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        ReadPackageEntry(savedBytes, "xl/tables/table1.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/tables/table1.xml"));
        ReadWorksheetDimension(savedBytes, "xl/worksheets/sheet1.xml")
            .Should()
            .Be("A1:D4");
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "D4")
            .Should()
            .Be("new outside");
    }

    [Fact]
    public void Save_LoadedWorkbookWithStructuredTableHeaderEdit_FallsBackToFullSave()
    {
        var sourceBytes = CreateStructuredTableSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Changed"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .NotEqual(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
    }

    [Fact]
    public void Save_LoadedWorkbookWithFilteredStructuredTableDataBodyEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreateStructuredTableSourcePackage(includeFilter: true);
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.StructuredTables.Should().ContainSingle()
            .Which.FilterColumns.Should().ContainSingle();
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(99));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        adapter.LastSaveDiagnostics.CellChangeCount.Should().Be(1);
        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/worksheets/_rels/sheet1.xml.rels")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/worksheets/_rels/sheet1.xml.rels"));
        ReadPackageEntry(savedBytes, "xl/tables/table1.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/tables/table1.xml"));
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "B2")
            .Should()
            .Be("99");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloadedSheet = adapter.Load(reloadStream).GetSheetAt(0);
        reloadedSheet.GetCell(2, 2)!.Value.Should().Be(new NumberValue(99));
        reloadedSheet.StructuredTables.Should().ContainSingle()
            .Which.FilterColumns.Should().ContainSingle()
            .Which.Values.Should().ContainSingle().Which.Should().Be("East");
    }

    [Fact]
    public void Save_LoadedWorkbookWithFilteredStructuredTableFilterColumnEdit_FallsBackToFullSave()
    {
        var sourceBytes = CreateStructuredTableSourcePackage(includeFilter: true);
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.StructuredTables.Should().ContainSingle()
            .Which.FilterColumns.Should().ContainSingle()
            .Which.ColumnId.Should().Be(0);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("West"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        adapter.LastSaveDiagnostics.Reason.Should().Be("change_table_cell");
    }

    [Fact]
    public void Save_LoadedWorkbookWithSparklineAndUnrelatedCellEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreateSparklineSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.Sparklines.Should().ContainSingle();
        sheet.SetCell(new CellAddress(sheet.Id, 2, 5), new NumberValue(99));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        adapter.LastSaveDiagnostics.CellChangeCount.Should().Be(1);
        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        Encoding.UTF8.GetString(ReadPackageEntry(savedBytes, "xl/worksheets/sheet1.xml"))
            .Should()
            .Contain("sparklineGroups");
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "E2")
            .Should()
            .Be("99");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloadedSheet = adapter.Load(reloadStream).GetSheetAt(0);
        reloadedSheet.GetCell(2, 5)!.Value.Should().Be(new NumberValue(99));
        var sparkline = reloadedSheet.Sparklines.Should().ContainSingle().Subject;
        sparkline.Kind.Should().Be(SparklineKind.Column);
        sparkline.DataRange.ToString().Should().Be("A1:C1");
        sparkline.Location.ToA1().Should().Be("D1");
    }

    [Fact]
    public void Save_LoadedWorkbookWithChartAndOutsideCellEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreateChartSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.Charts.Should().ContainSingle();
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new TextValue("outside patched"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/drawings/drawing1.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/drawings/drawing1.xml"));
        ReadPackageEntry(savedBytes, "xl/drawings/_rels/drawing1.xml.rels")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/drawings/_rels/drawing1.xml.rels"));
        ReadPackageEntry(savedBytes, "xl/charts/chart1.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/charts/chart1.xml"));
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "D4")
            .Should()
            .Be("outside patched");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloadedSheet = adapter.Load(reloadStream).GetSheetAt(0);
        reloadedSheet.GetCell(4, 4)!.Value.Should().Be(new TextValue("outside patched"));
        reloadedSheet.Charts.Should().ContainSingle();
    }

    [Fact]
    public void Save_LoadedWorkbookWithChartThemeOverrideAndOutsideCellEdit_PatchesSourcePackage()
    {
        var sourceBytes = AddChartThemeOverridePackageGraph(CreateChartSourcePackage());
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.Charts.Should().ContainSingle();
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new TextValue("theme override patched"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        adapter.LastSaveDiagnostics.CellChangeCount.Should().Be(1);
        ReadPackageEntry(savedBytes, "xl/charts/chart1.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/charts/chart1.xml"));
        ReadPackageEntry(savedBytes, "xl/charts/_rels/chart1.xml.rels")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/charts/_rels/chart1.xml.rels"));
        ReadPackageEntry(savedBytes, "xl/theme/themeOverride1.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/theme/themeOverride1.xml"));
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "D4")
            .Should()
            .Be("theme override patched");
    }

    [Fact]
    public void Save_LoadedWorkbookWithChartSourceCellEdit_FallsBackToFullSave()
    {
        var sourceBytes = CreateChartSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.Charts.Should().ContainSingle();
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(99));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        saved.Length.Should().BeGreaterThan(0);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        adapter.LastSaveDiagnostics.Reason.Should().Be("change_chart_source_cell");
    }

    [Fact]
    public void Save_LoadedWorkbookWithSmartArtDiagramAndOutsideCellEdit_PreservesDiagramPackageGraph()
    {
        var sourceBytes = CreateSmartArtDiagramSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new TextValue("smartart outside patched"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        adapter.LastSaveDiagnostics.CellChangeCount.Should().Be(1);
        ReadPackageEntry(savedBytes, "xl/drawings/drawing1.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/drawings/drawing1.xml"));
        ReadPackageEntry(savedBytes, "xl/drawings/_rels/drawing1.xml.rels")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/drawings/_rels/drawing1.xml.rels"));
        ReadPackageEntry(savedBytes, "xl/diagrams/data1.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/diagrams/data1.xml"));
        ReadPackageEntry(savedBytes, "xl/diagrams/layout1.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/diagrams/layout1.xml"));
        ReadPackageEntry(savedBytes, "xl/diagrams/quickStyle1.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/diagrams/quickStyle1.xml"));
        ReadPackageEntry(savedBytes, "xl/diagrams/colors1.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/diagrams/colors1.xml"));
        ReadContentTypeOverrides(savedBytes).Should().Contain([
            "/xl/diagrams/data1.xml",
            "/xl/diagrams/layout1.xml",
            "/xl/diagrams/quickStyle1.xml",
            "/xl/diagrams/colors1.xml"]);
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "D4")
            .Should()
            .Be("smartart outside patched");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        adapter.Load(reloadStream)
            .GetSheetAt(0)
            .GetCell(4, 4)!
            .Value
            .Should()
            .Be(new TextValue("smartart outside patched"));
    }

    [Fact]
    public void Save_LoadedWorkbookWithChartExAndOutsideCellEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreateChartExSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.Charts.Should().ContainSingle().Which.Type.Should().Be(ChartType.Histogram);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new TextValue("chartEx outside patched"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        adapter.LastSaveDiagnostics.CellChangeCount.Should().Be(1);
        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/drawings/drawing1.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/drawings/drawing1.xml"));
        ReadPackageEntry(savedBytes, "xl/drawings/_rels/drawing1.xml.rels")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/drawings/_rels/drawing1.xml.rels"));
        ReadPackageEntry(savedBytes, "xl/charts/chart1.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/charts/chart1.xml"));
        ReadPackageEntry(savedBytes, "xl/charts/_rels/chart1.xml.rels")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/charts/_rels/chart1.xml.rels"));
        ReadPackageEntry(savedBytes, "xl/charts/style1.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/charts/style1.xml"));
        ReadPackageEntry(savedBytes, "xl/charts/colors1.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/charts/colors1.xml"));
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "D4")
            .Should()
            .Be("chartEx outside patched");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloadedSheet = adapter.Load(reloadStream).GetSheetAt(0);
        reloadedSheet.GetCell(4, 4)!.Value.Should().Be(new TextValue("chartEx outside patched"));
        reloadedSheet.Charts.Should().ContainSingle().Which.Type.Should().Be(ChartType.Histogram);
    }

    [Fact]
    public void Save_LoadedWorkbookWithChartExSourceCellEdit_FallsBackToFullSave()
    {
        var sourceBytes = CreateChartExSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.Charts.Should().ContainSingle().Which.Type.Should().Be(ChartType.Histogram);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(99));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        saved.Length.Should().BeGreaterThan(0);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        adapter.LastSaveDiagnostics.Reason.Should().Be("change_chart_source_cell");
    }

    [Fact]
    public void Save_LoadedWorkbookWithDrawingShapesAndUnrelatedCellEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreateDrawingShapeSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.TextBoxes.Should().ContainSingle();
        sheet.DrawingShapes.Should().ContainSingle();
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new TextValue("shape-adjacent patched"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        adapter.LastSaveDiagnostics.CellChangeCount.Should().Be(1);
        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/drawings/drawing1.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/drawings/drawing1.xml"));
        ReadPackageEntry(savedBytes, "xl/drawings/_rels/drawing1.xml.rels")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/drawings/_rels/drawing1.xml.rels"));
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "D4")
            .Should()
            .Be("shape-adjacent patched");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloadedSheet = adapter.Load(reloadStream).GetSheetAt(0);
        reloadedSheet.GetCell(4, 4)!.Value.Should().Be(new TextValue("shape-adjacent patched"));
        reloadedSheet.TextBoxes.Should().ContainSingle().Which.Text.Should().Be("Review note");
        reloadedSheet.DrawingShapes.Should().ContainSingle().Which.Kind.Should().Be(DrawingShapeKind.Ellipse);
    }

    [Fact]
    public void Save_LoadedWorkbookWithResizedDrawingShape_DoesNotDiscardNewGeometry()
    {
        // F15 regression: resizing a source-loaded shape (or moving it via anchor sub-cell offset) must not
        // be silently dropped by the cell-patch fast-save path. Before the fix, the patch-safe comparison only
        // checked the shape's anchor *cell*, so a pure geometry change looked like "no drawing change" and the
        // stale source drawing XML was kept — the resize/move was lost even though the save "succeeded".
        var sourceBytes = CreateDrawingShapeSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        var shape = sheet.DrawingShapes.Should().ContainSingle().Which;
        var originalWidth = shape.Width;
        var originalHeight = shape.Height;
        var originalOffsetX = shape.AnchorOffsetX;
        var originalOffsetY = shape.AnchorOffsetY;

        var newWidth = originalWidth + 250;
        var newHeight = originalHeight + 120;
        var newOffsetX = originalOffsetX + 30;
        var newOffsetY = originalOffsetY + 15;
        shape.Width = newWidth;
        shape.Height = newHeight;
        shape.AnchorOffsetX = newOffsetX;
        shape.AnchorOffsetY = newOffsetY;
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new TextValue("shape-resized"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        // The geometry change makes the source drawing part unsafe to keep as-is, so the whole package must
        // fall back to a full save (never a source patch that silently retains the old drawing XML).
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloadedSheet = adapter.Load(reloadStream).GetSheetAt(0);
        reloadedSheet.GetCell(4, 4)!.Value.Should().Be(new TextValue("shape-resized"));
        var reloadedShape = reloadedSheet.DrawingShapes.Should().ContainSingle().Which;
        reloadedShape.Width.Should().BeApproximately(newWidth, 1.0);
        reloadedShape.Height.Should().BeApproximately(newHeight, 1.0);
        reloadedShape.AnchorOffsetX.Should().BeApproximately(newOffsetX, 1.0);
        reloadedShape.AnchorOffsetY.Should().BeApproximately(newOffsetY, 1.0);
    }

    [Fact]
    public void Save_LoadedWorkbookWithOpaqueDrawingShapeAndUnrelatedCellEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreateOpaqueDrawingShapeSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.TextBoxes.Should().ContainSingle();
        sheet.DrawingShapes.Should().BeEmpty();
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new TextValue("opaque shape patched"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        adapter.LastSaveDiagnostics.CellChangeCount.Should().Be(1);
        ReadPackageEntry(savedBytes, "xl/drawings/drawing1.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/drawings/drawing1.xml"));
        ReadPackageEntry(savedBytes, "xl/drawings/_rels/drawing1.xml.rels")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/drawings/_rels/drawing1.xml.rels"));
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "D4")
            .Should()
            .Be("opaque shape patched");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloadedSheet = adapter.Load(reloadStream).GetSheetAt(0);
        reloadedSheet.GetCell(4, 4)!.Value.Should().Be(new TextValue("opaque shape patched"));
        reloadedSheet.TextBoxes.Should().ContainSingle().Which.Text.Should().Be("Review note");
        reloadedSheet.DrawingShapes.Should().BeEmpty();
    }

    [Fact]
    public void Save_LoadedWorkbookWithPivotAndOutsideCellEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreatePivotSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.PivotTables.Should().ContainSingle();
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new TextValue("outside patched"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        ReadPackageEntry(savedBytes, "xl/pivotCache/pivotCacheDefinition1.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/pivotCache/pivotCacheDefinition1.xml"));
        ReadPackageEntry(savedBytes, "xl/pivotTables/pivotTable1.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/pivotTables/pivotTable1.xml"));
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "D4")
            .Should()
            .Be("outside patched");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloadedSheet = adapter.Load(reloadStream).GetSheetAt(0);
        reloadedSheet.GetCell(4, 4)!.Value.Should().Be(new TextValue("outside patched"));
        reloadedSheet.PivotTables.Should().ContainSingle();
    }

    [Fact]
    public void Save_LoadedWorkbookWithPivotSourceCellEdit_FallsBackToFullSave()
    {
        var sourceBytes = CreatePivotSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.PivotTables.Should().ContainSingle();
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(99));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        saved.Length.Should().BeGreaterThan(0);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        adapter.LastSaveDiagnostics.Reason.Should().Be("change_pivot_source_cell");
    }

    [Fact]
    public void Save_LoadedWorkbookWithPivotChartAndOutsideCellEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreatePivotChartSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.PivotTables.Should().ContainSingle();
        sheet.Charts.Should().ContainSingle().Which.IsPivotChart.Should().BeTrue();
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new TextValue("pivot chart outside patched"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        adapter.LastSaveDiagnostics.CellChangeCount.Should().Be(1);
        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/drawings/drawing1.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/drawings/drawing1.xml"));
        ReadPackageEntry(savedBytes, "xl/drawings/_rels/drawing1.xml.rels")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/drawings/_rels/drawing1.xml.rels"));
        ReadPackageEntry(savedBytes, "xl/charts/chart1.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/charts/chart1.xml"));
        ReadPackageEntry(savedBytes, "xl/pivotCache/pivotCacheDefinition1.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/pivotCache/pivotCacheDefinition1.xml"));
        ReadPackageEntry(savedBytes, "xl/pivotTables/pivotTable1.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/pivotTables/pivotTable1.xml"));
        ReadPackageEntry(savedBytes, "xl/pivotTables/_rels/pivotTable1.xml.rels")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/pivotTables/_rels/pivotTable1.xml.rels"));
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "D4")
            .Should()
            .Be("pivot chart outside patched");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloadedSheet = adapter.Load(reloadStream).GetSheetAt(0);
        reloadedSheet.GetCell(4, 4)!.Value.Should().Be(new TextValue("pivot chart outside patched"));
        reloadedSheet.PivotTables.Should().ContainSingle();
        reloadedSheet.Charts.Should().ContainSingle().Which.IsPivotChart.Should().BeTrue();
    }

    [Fact]
    public void Save_LoadedWorkbookWithPivotChartSourceCellEdit_FallsBackToFullSave()
    {
        var sourceBytes = CreatePivotChartSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.PivotTables.Should().ContainSingle();
        sheet.Charts.Should().ContainSingle().Which.IsPivotChart.Should().BeTrue();
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(99));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        saved.Length.Should().BeGreaterThan(0);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        adapter.LastSaveDiagnostics.Reason.Should().Be("change_pivot_source_cell");
    }

    [Fact]
    public void Save_LoadedWorkbookWithClearedLiteralCell_PatchesSourcePackage()
    {
        var sourceBytes = CreateSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.ClearCell(2, 2);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/styles.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/styles.xml"));
        TryReadCellElement(savedBytes, "xl/worksheets/sheet1.xml", "B2")
            .Should()
            .BeNull();

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        adapter.Load(reloadStream)
            .GetSheetAt(0)
            .GetCell(2, 2)
            .Should()
            .BeNull();
    }

    [Theory]
    [MemberData(nameof(FormulaCachedValueCases))]
    public void Save_LoadedWorkbookWithFormulaCachedValueEdit_PatchesFormulaCache(
        ScalarValue cachedValue,
        string? expectedCellType,
        string? expectedRawValue)
    {
        var sourceBytes = CreateFormulaSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var cell = workbook.GetSheetAt(0).GetCell(1, 1)!;
        cell.FormulaText.Should().Be("1+1");
        cell.Value = cachedValue;

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/calcChain.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/calcChain.xml"));
        ReadCellFormula(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("1+1");
        ReadCellType(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be(expectedCellType);
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be(expectedRawValue);
    }

    [Fact]
    public void RebaseLoadedPackageSnapshot_TreatsOpenRecalculationAsBaselineForPatchSave()
    {
        var sourceBytes = CreateFormulaSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.GetCell(1, 1)!.Value = new NumberValue(3);
        adapter.RebaseLoadedPackageSnapshot(workbook).Should().BeTrue();
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new TextValue("user edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        adapter.LastSaveDiagnostics.CellChangeCount.Should().Be(1);
        ReadCellFormula(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("1+1");
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("2");
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "D4")
            .Should()
            .Be("user edit");
    }

    [Fact]
    public void Save_LoadedWorkbookWithPlainFormulaTextEdit_FallsBackToFullSaveAndDropsCalcChain()
    {
        var sourceBytes = CreateFormulaSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var cell = workbook.GetSheetAt(0).GetCell(1, 1)!;
        cell.FormulaText = "1+2";
        cell.Value = new NumberValue(3);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        adapter.LastSaveDiagnostics.Reason.Should().Be("change_formula_array_mode");
        adapter.LastSaveDiagnostics.InvalidatesCalcChain.Should().BeTrue();
        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .NotEqual(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        PackageHasEntry(savedBytes, "xl/calcChain.xml").Should().BeFalse();
        ReadContentTypeOverrides(savedBytes).Should().NotContain("/xl/calcChain.xml");
        ReadWorkbookRelationshipTypes(savedBytes)
            .Should()
            .NotContain("http://schemas.openxmlformats.org/officeDocument/2006/relationships/calcChain");
        ReadCellFormula(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("1+2");
    }

    [Fact]
    public void Save_LoadedWorkbookWithFormulaArrayModeEdit_FallsBackToFullSave()
    {
        var sourceBytes = CreateFormulaSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var cell = workbook.GetSheetAt(0).GetCell(1, 1)!;
        cell.ArrayMode.Should().Be(FormulaArrayMode.Implicit);
        cell.FormulaText = cell.FormulaText;
        cell.ArrayMode.Should().Be(FormulaArrayMode.Dynamic);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        adapter.LastSaveDiagnostics.Reason.Should().Be("change_formula_array_mode");
        adapter.LastSaveDiagnostics.InvalidatesCalcChain.Should().BeTrue();
    }

    [Fact]
    public void Save_LoadedWorkbookWithClearedFormulaCell_PatchesSourcePackageAndDropsCalcChain()
    {
        var sourceBytes = CreateFormulaSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.ClearCell(1, 1);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        PackageHasEntry(savedBytes, "xl/calcChain.xml").Should().BeFalse();
        ReadContentTypeOverrides(savedBytes).Should().NotContain("/xl/calcChain.xml");
        TryReadCellElement(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .BeNull();
    }

    [Theory]
    [MemberData(nameof(AttributedFormulaElements))]
    public void Save_LoadedWorkbookWithAttributedFormulaTextEdit_FallsBackToFullSave(string formulaElement)
    {
        var sourceBytes = CreateFormulaSourcePackage(formulaElement);
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var cell = workbook.GetSheetAt(0).GetCell(1, 1)!;
        cell.FormulaText = "1+2";
        cell.Value = new NumberValue(3);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        adapter.LastSaveDiagnostics.InvalidatesCalcChain.Should().BeTrue();
        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .NotEqual(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        PackageHasEntry(savedBytes, "xl/calcChain.xml").Should().BeFalse();
        ReadContentTypeOverrides(savedBytes).Should().NotContain("/xl/calcChain.xml");
        ReadWorkbookRelationshipTypes(savedBytes)
            .Should()
            .NotContain("http://schemas.openxmlformats.org/officeDocument/2006/relationships/calcChain");
        ReadCellFormula(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("1+2");
    }

    [Fact]
    public void Save_LoadedWorkbookWithAttributedArrayFormulaCachedValueEdit_PatchesCacheAndPreservesFormulaMetadata()
    {
        const string arrayFormula = """<f t="array" ref="A1:A1" ca="1">1+1</f>""";
        var sourceBytes = CreateFormulaSourcePackage(arrayFormula);
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var cell = workbook.GetSheetAt(0).GetCell(1, 1)!;
        cell.FormulaText.Should().Be("1+1");
        cell.Value = new NumberValue(4);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        adapter.LastSaveDiagnostics.CellChangeCount.Should().Be(1);
        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/calcChain.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/calcChain.xml"));
        ReadContentTypeOverrides(savedBytes).Should().Contain("/xl/calcChain.xml");
        ReadWorkbookRelationshipTypes(savedBytes)
            .Should()
            .Contain("http://schemas.openxmlformats.org/officeDocument/2006/relationships/calcChain");
        ReadCellFormula(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("1+1");
        ReadCellFormulaAttribute(savedBytes, "xl/worksheets/sheet1.xml", "A1", "t")
            .Should()
            .Be("array");
        ReadCellFormulaAttribute(savedBytes, "xl/worksheets/sheet1.xml", "A1", "ref")
            .Should()
            .Be("A1:A1");
        ReadCellFormulaAttribute(savedBytes, "xl/worksheets/sheet1.xml", "A1", "ca")
            .Should()
            .Be("1");
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("4");

        using var reload = new MemoryStream(savedBytes, writable: false);
        var reloadedCell = adapter.Load(reload).GetSheetAt(0).GetCell(1, 1)!;
        reloadedCell.FormulaText.Should().Be("1+1");
        reloadedCell.Value.Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Save_LoadedWorkbookWithAttributedSharedFormulaCachedValueEdit_PatchesCacheAndPreservesFormulaMetadata()
    {
        const string sharedFormula = """<f t="shared" ref="A1:A1" si="0">1+1</f>""";
        var sourceBytes = CreateFormulaSourcePackage(sharedFormula);
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var cell = workbook.GetSheetAt(0).GetCell(1, 1)!;
        cell.FormulaText.Should().Be("1+1");
        cell.Value = new NumberValue(5);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        adapter.LastSaveDiagnostics.CellChangeCount.Should().Be(1);
        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/calcChain.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/calcChain.xml"));
        ReadCellFormula(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("1+1");
        ReadCellFormulaAttribute(savedBytes, "xl/worksheets/sheet1.xml", "A1", "t")
            .Should()
            .Be("shared");
        ReadCellFormulaAttribute(savedBytes, "xl/worksheets/sheet1.xml", "A1", "ref")
            .Should()
            .Be("A1:A1");
        ReadCellFormulaAttribute(savedBytes, "xl/worksheets/sheet1.xml", "A1", "si")
            .Should()
            .Be("0");
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("5");

        using var reload = new MemoryStream(savedBytes, writable: false);
        var reloadedCell = adapter.Load(reload).GetSheetAt(0).GetCell(1, 1)!;
        reloadedCell.FormulaText.Should().Be("1+1");
        reloadedCell.Value.Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Save_LoadedWorkbookWithWorksheetViewMetadataEdit_PatchesSourcePackage()
    {
        var sourceBytes = CreateSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.ShowGridlines = false;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("patched value"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        adapter.LastSaveDiagnostics.CellChangeCount.Should().Be(1);
        adapter.LastSaveDiagnostics.WorksheetViewChangeCount.Should().Be(1);
        adapter.LastSaveDiagnostics.TotalPatchChangeCount.Should().Be(2);
        ReadPackageEntry(savedBytes, "xl/workbook.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/workbook.xml"));
        ReadPackageEntry(savedBytes, "xl/styles.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/styles.xml"));
        ReadPrimarySheetViewAttribute(savedBytes, "xl/worksheets/sheet1.xml", "showGridLines")
            .Should()
            .Be("0");
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("patched value");

        using var reload = new MemoryStream(savedBytes, writable: false);
        var reloaded = adapter.Load(reload).GetSheetAt(0);
        reloaded.ShowGridlines.Should().BeFalse();
        reloaded.GetCell(1, 1)!.Value.Should().Be(new TextValue("patched value"));
    }

    [Fact]
    public void Save_LoadedWorkbookWithCustomWorkbookViewsAndCellEdit_PatchesSourcePackageAndRemovesCustomViews()
    {
        var sourceBytes = AddCustomWorkbookViews(CreateSourcePackage());
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("patched value"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        ReadWorkbookCustomWorkbookViews(savedBytes).Should().BeNull();
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("patched value");
        ReadPackageEntry(savedBytes, "xl/styles.xml")
            .Should()
            .Equal(ReadPackageEntry(sourceBytes, "xl/styles.xml"));
    }

    [Fact]
    public void Save_LoadedWorkbookWithNativeOnlyCustomSheetViewsAndCellEdit_PatchesSourcePackageAndRemovesCustomViews()
    {
        var sourceBytes = AddMinimalCustomSheetViews(CreateSourcePackage());
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        workbook.CustomViews.Should().BeEmpty();
        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("patched value"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        ReadWorksheetCustomSheetViews(savedBytes, "xl/worksheets/sheet1.xml").Should().BeNull();
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("patched value");
    }

    [Fact]
    public void Save_LoadedWorkbookWithModeledCustomViewsAndCellEdit_PatchesSourcePackageAndPreservesCustomViews()
    {
        var sourceBytes = AddMatchedCustomViews(CreateSourcePackage());
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        workbook.CustomViews.Should().ContainSingle();
        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("patched value"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        ReadWorkbookCustomWorkbookViews(savedBytes).Should().NotBeNull();
        ReadWorksheetCustomSheetViews(savedBytes, "xl/worksheets/sheet1.xml").Should().NotBeNull();
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("patched value");

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        adapter.Load(reloadStream).CustomViews.Should().ContainSingle();
    }

    [Fact]
    public void Save_LoadedWorkbookWithRichSharedStringFontAndCellEdit_PatchesSourcePackageAndSanitizesFont()
    {
        var sourceBytes = AddCssFontFamilyRichSharedString(CreateSourcePackage());
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(456));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        ReadRichSharedStringFontValue(savedBytes, "original value")
            .Should()
            .Be("Google Sans");
    }

    [Fact]
    public void Save_LoadedWorkbookWithOfficeRevisionUidAttributes_PatchesSourcePackageAndDropsRevisionAttributes()
    {
        var sourceBytes = AddOfficeRevisionUidAttributes(CreateSourcePackage());
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("patched revision uid workbook"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "A1")
            .Should()
            .Be("patched revision uid workbook");
        PackageXmlHasOfficeRevisionAttributes(savedBytes, "xl/workbook.xml")
            .Should()
            .BeFalse();
        PackageXmlHasOfficeRevisionAttributes(savedBytes, "xl/worksheets/sheet1.xml")
            .Should()
            .BeFalse();
        Encoding.UTF8.GetString(ReadPackageEntry(savedBytes, "xl/workbook.xml"))
            .Should()
            .NotContain("/revision");
        Encoding.UTF8.GetString(ReadPackageEntry(savedBytes, "xl/worksheets/sheet1.xml"))
            .Should()
            .NotContain("/revision");
        ReadMarkupCompatibilityIgnorable(savedBytes, "xl/workbook.xml")
            .Should()
            .Be("x14 foo");
        ReadMarkupCompatibilityIgnorable(savedBytes, "xl/worksheets/sheet1.xml")
            .Should()
            .Be("x14ac");
    }

    [Fact]
    public void Save_LoadedWorkbookWithOnlyOfficeRevisionLastSaveAttribute_PatchesSourcePackageAndDropsRevisionAttribute()
    {
        var sourceBytes = AddWorkbookOnlyOfficeRevisionLastSaveAttribute(CreateSourcePackage());
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(456));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        ReadCellText(savedBytes, "xl/worksheets/sheet1.xml", "B2")
            .Should()
            .Be("456");
        PackageXmlHasOfficeRevisionAttributes(savedBytes, "xl/workbook.xml")
            .Should()
            .BeFalse();
        Encoding.UTF8.GetString(ReadPackageEntry(savedBytes, "xl/workbook.xml"))
            .Should()
            .NotContain("/revision");
    }

    [Fact]
    public void Save_LoadedWorkbookWithOfficeRevisionPointerElement_PatchesSourcePackageAndPreservesNeededIgnorablePrefix()
    {
        var sourceBytes = AddWorkbookOfficeRevisionPointerElement(CreateSourcePackage());
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(654));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        var workbookXml = Encoding.UTF8.GetString(ReadPackageEntry(savedBytes, "xl/workbook.xml"));
        workbookXml.Should().Contain("revisionPtr");
        workbookXml.Should().NotContain("uidLastSave");
        workbookXml.Should().NotContain("revision10");
        ReadMarkupCompatibilityIgnorable(savedBytes, "xl/workbook.xml")
            .Should()
            .Be("xr");
    }

    [Fact]
    public void Save_LoadedWorkbookWithShadowedRevisionPrefix_KeepsNonRevisionIgnorablePrefix()
    {
        var sourceBytes = AddWorksheetRevisionPrefixShadow(CreateSourcePackage());
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(sourceBytes, writable: false))
            workbook = adapter.Load(source);
        PrepareLoadedWorkbookForEdit(workbook);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(789));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        var savedBytes = saved.ToArray();

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        ReadMarkupCompatibilityIgnorable(savedBytes, "xl/worksheets/sheet1.xml")
            .Should()
            .Be("x14ac");
        ReadRowMarkupCompatibilityIgnorable(savedBytes, "xl/worksheets/sheet1.xml", 1)
            .Should()
            .Be("xr");
        Encoding.UTF8.GetString(ReadPackageEntry(savedBytes, "xl/worksheets/sheet1.xml"))
            .Should()
            .Contain("urn:freex-nonrevision");
    }

    private static byte[] CreateSourcePackage()
    {
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Data");
            sheet.Cell("A1").Value = "original value";
            sheet.Cell("B2").Value = 123.45;
            sheet.Cell("C3").Value = true;
            workbook.SaveAs(stream);
        }

        return RemoveEmptyWorkbookDefinedNames(stream.ToArray());
    }

    private static byte[] AddWorkbookMetadataPackageParts(byte[] sourceBytes)
    {
        using var stream = new MemoryStream();
        stream.Write(sourceBytes, 0, sourceBytes.Length);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
            XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            XNamespace xdaNs = "http://schemas.microsoft.com/office/spreadsheetml/2017/dynamicarray";

            var contentTypesXml = LoadPackageXml(archive, "[Content_Types].xml");
            contentTypesXml.Root!
                .Elements(contentTypeNs + "Override")
                .Where(element => string.Equals((string?)element.Attribute("PartName"), "/xl/metadata.xml", StringComparison.OrdinalIgnoreCase))
                .Remove();
            contentTypesXml.Root.Add(new XElement(
                contentTypeNs + "Override",
                new XAttribute("PartName", "/xl/metadata.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheetMetadata+xml")));
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);

            var workbookRelationshipsXml = LoadPackageXml(archive, "xl/_rels/workbook.xml.rels");
            workbookRelationshipsXml.Root!.Add(new XElement(
                relationshipNs + "Relationship",
                new XAttribute("Id", "rIdFreeXMetadata"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/sheetMetadata"),
                new XAttribute("Target", "metadata.xml")));
            ReplacePackageXml(archive, "xl/_rels/workbook.xml.rels", workbookRelationshipsXml);

            var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
            var metadataCell = worksheetXml
                .Descendants(workbookNs + "c")
                .Single(element => element.Attribute("r")?.Value == "A1");
            metadataCell.SetAttributeValue("cm", "1");
            metadataCell.SetAttributeValue("vm", "1");
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

            var metadataXml = new XDocument(new XElement(
                workbookNs + "metadata",
                new XAttribute(XNamespace.Xmlns + "xda", xdaNs.NamespaceName),
                new XElement(
                    workbookNs + "metadataTypes",
                    new XAttribute("count", "1"),
                    new XElement(
                        workbookNs + "metadataType",
                        new XAttribute("name", "XLDAPR"),
                        new XAttribute("minSupportedVersion", "120000"),
                        new XAttribute("copy", "1"),
                        new XAttribute("pasteAll", "1"),
                        new XAttribute("pasteValues", "1"),
                        new XAttribute("merge", "1"),
                        new XAttribute("splitFirst", "1"),
                        new XAttribute("rowColShift", "1"),
                        new XAttribute("clearFormats", "1"),
                        new XAttribute("clearComments", "1"),
                        new XAttribute("assign", "1"),
                        new XAttribute("coerce", "1"),
                        new XAttribute("cellMeta", "1"))),
                new XElement(
                    workbookNs + "futureMetadata",
                    new XAttribute("name", "XLDAPR"),
                    new XAttribute("count", "1"),
                    new XElement(
                        workbookNs + "bk",
                        new XElement(
                            workbookNs + "extLst",
                            new XElement(
                                workbookNs + "ext",
                                new XAttribute("uri", "{bdbb8cdc-fa1e-496e-a857-3c3f30c029c3}"),
                                new XElement(
                                    xdaNs + "dynamicArrayProperties",
                                    new XAttribute("fDynamic", "1"),
                                    new XAttribute("fCollapsed", "0")))))),
                new XElement(
                    workbookNs + "cellMetadata",
                    new XAttribute("count", "1"),
                    new XElement(
                        workbookNs + "bk",
                        new XElement(
                            workbookNs + "rc",
                            new XAttribute("t", "1"),
                            new XAttribute("v", "0")))),
                new XElement(
                    workbookNs + "valueMetadata",
                    new XAttribute("count", "1"),
                    new XElement(
                        workbookNs + "bk",
                        new XElement(
                            workbookNs + "rc",
                            new XAttribute("t", "1"),
                            new XAttribute("v", "0"))))));
            ReplacePackageXml(archive, "xl/metadata.xml", metadataXml);
        }

        return stream.ToArray();
    }

    private static byte[] AddDigitalSignaturePackageGraph(byte[] sourceBytes)
    {
        using var stream = new MemoryStream();
        stream.Write(sourceBytes, 0, sourceBytes.Length);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
            XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";

            var contentTypesXml = LoadPackageXml(archive, "[Content_Types].xml");
            contentTypesXml.Root!
                .Elements(contentTypeNs + "Override")
                .Where(element =>
                    string.Equals((string?)element.Attribute("PartName"), "/_xmlsignatures/origin.sigs", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals((string?)element.Attribute("PartName"), "/_xmlsignatures/sig1.xml", StringComparison.OrdinalIgnoreCase))
                .Remove();
            contentTypesXml.Root.Add(
                new XElement(
                    contentTypeNs + "Override",
                    new XAttribute("PartName", "/_xmlsignatures/origin.sigs"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-package.digital-signature-origin")),
                new XElement(
                    contentTypeNs + "Override",
                    new XAttribute("PartName", "/_xmlsignatures/sig1.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-package.digital-signature-xmlsignature+xml")));
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);

            var rootRelationshipsXml = LoadPackageXml(archive, "_rels/.rels");
            rootRelationshipsXml.Root!.Add(new XElement(
                relationshipNs + "Relationship",
                new XAttribute("Id", "rIdSignatureOrigin"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/package/2006/relationships/digital-signature/origin"),
                new XAttribute("Target", "_xmlsignatures/origin.sigs")));
            ReplacePackageXml(archive, "_rels/.rels", rootRelationshipsXml);

            ReplacePackageXml(
                archive,
                "_xmlsignatures/origin.sigs",
                new XDocument(new XElement("SignatureOrigin")));
            ReplacePackageXml(
                archive,
                "_xmlsignatures/sig1.xml",
                new XDocument(new XElement("Signature")));
            ReplacePackageXml(
                archive,
                "_xmlsignatures/_rels/origin.sigs.rels",
                new XDocument(new XElement(
                    relationshipNs + "Relationships",
                    new XElement(
                        relationshipNs + "Relationship",
                        new XAttribute("Id", "rIdSignature1"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/package/2006/relationships/digital-signature/signature"),
                        new XAttribute("Target", "sig1.xml")))));
        }

        return stream.ToArray();
    }

    private static byte[] AddOfficeWebExtensionPackageGraph(
        byte[] sourceBytes,
        string workbookRelationshipId = "rIdFreeXOfficeAddinTaskpanes",
        string webExtensionRelationshipId = "rIdWebExtension1")
    {
        using var stream = new MemoryStream();
        stream.Write(sourceBytes, 0, sourceBytes.Length);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
            XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";

            var contentTypesXml = LoadPackageXml(archive, "[Content_Types].xml");
            contentTypesXml.Root!
                .Elements(contentTypeNs + "Override")
                .Where(element =>
                    string.Equals((string?)element.Attribute("PartName"), "/xl/webextensions/taskpanes.xml", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals((string?)element.Attribute("PartName"), "/xl/webextensions/webextension1.xml", StringComparison.OrdinalIgnoreCase))
                .Remove();
            contentTypesXml.Root.Add(
                new XElement(
                    contentTypeNs + "Override",
                    new XAttribute("PartName", "/xl/webextensions/taskpanes.xml"),
                    new XAttribute("ContentType", "application/vnd.ms-office.webextensiontaskpanes+xml")),
                new XElement(
                    contentTypeNs + "Override",
                    new XAttribute("PartName", "/xl/webextensions/webextension1.xml"),
                    new XAttribute("ContentType", "application/vnd.ms-office.webextension+xml")));
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);

            var workbookRelationshipsXml = LoadPackageXml(archive, "xl/_rels/workbook.xml.rels");
            workbookRelationshipsXml.Root!.Add(new XElement(
                relationshipNs + "Relationship",
                new XAttribute("Id", workbookRelationshipId),
                new XAttribute("Type", "http://schemas.microsoft.com/office/2011/relationships/webextensiontaskpanes"),
                new XAttribute("Target", "webextensions/taskpanes.xml")));
            ReplacePackageXml(archive, "xl/_rels/workbook.xml.rels", workbookRelationshipsXml);

            ReplacePackageXml(
                archive,
                "xl/webextensions/taskpanes.xml",
                new XDocument(new XElement(
                    XNamespace.Get("http://schemas.microsoft.com/office/webextensions/taskpanes/2010/11") + "taskpanes",
                    new XElement(
                        XNamespace.Get("http://schemas.microsoft.com/office/webextensions/taskpanes/2010/11") + "taskpane",
                        new XAttribute("dockstate", "right"),
                        new XAttribute("visibility", "0"),
                        new XAttribute("width", "350"),
                        new XAttribute("row", "4"),
                        new XElement(
                            XNamespace.Get("http://schemas.microsoft.com/office/webextensions/taskpanes/2010/11") + "webextensionref",
                            new XAttribute(XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships") + "id", webExtensionRelationshipId))))));
            ReplacePackageXml(
                archive,
                "xl/webextensions/_rels/taskpanes.xml.rels",
                new XDocument(new XElement(
                    relationshipNs + "Relationships",
                    new XElement(
                        relationshipNs + "Relationship",
                        new XAttribute("Id", webExtensionRelationshipId),
                        new XAttribute("Type", "http://schemas.microsoft.com/office/2011/relationships/webextension"),
                        new XAttribute("Target", "webextension1.xml")))));
            ReplacePackageXml(
                archive,
                "xl/webextensions/webextension1.xml",
                new XDocument(new XElement(
                    XNamespace.Get("http://schemas.microsoft.com/office/webextensions/webextension/2010/11") + "webextension",
                    new XElement(
                        XNamespace.Get("http://schemas.microsoft.com/office/webextensions/webextension/2010/11") + "reference",
                        new XAttribute("id", "wa104379955"),
                        new XAttribute("version", "1.0.0.0"),
                        new XAttribute("store", "en-US"),
                        new XAttribute("storeType", "OMEX")),
                    new XElement(XNamespace.Get("http://schemas.microsoft.com/office/webextensions/webextension/2010/11") + "alternateReferences"),
                    new XElement(XNamespace.Get("http://schemas.microsoft.com/office/webextensions/webextension/2010/11") + "properties"),
                    new XElement(XNamespace.Get("http://schemas.microsoft.com/office/webextensions/webextension/2010/11") + "bindings"),
                    new XElement(XNamespace.Get("http://schemas.microsoft.com/office/webextensions/webextension/2010/11") + "snapshot", "AAAA"))));
        }

        return stream.ToArray();
    }

    private static byte[] AddWorkbookXmlMapsPackageGraph(byte[] sourceBytes)
    {
        using var stream = new MemoryStream();
        stream.Write(sourceBytes, 0, sourceBytes.Length);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
            XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";

            var contentTypesXml = LoadPackageXml(archive, "[Content_Types].xml");
            contentTypesXml.Root!
                .Elements(contentTypeNs + "Override")
                .Where(element => string.Equals(
                    (string?)element.Attribute("PartName"),
                    "/xl/xmlMaps.xml",
                    StringComparison.OrdinalIgnoreCase))
                .Remove();
            contentTypesXml.Root.Add(new XElement(
                contentTypeNs + "Override",
                new XAttribute("PartName", "/xl/xmlMaps.xml"),
                new XAttribute("ContentType", "application/xml")));
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);

            var workbookRelationshipsXml = LoadPackageXml(archive, "xl/_rels/workbook.xml.rels");
            workbookRelationshipsXml.Root!.Add(new XElement(
                relationshipNs + "Relationship",
                new XAttribute("Id", "rIdFreeXXmlMaps"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/xmlMaps"),
                new XAttribute("Target", "xmlMaps.xml")));
            ReplacePackageXml(archive, "xl/_rels/workbook.xml.rels", workbookRelationshipsXml);

            ReplacePackageXml(
                archive,
                "xl/xmlMaps.xml",
                new XDocument(new XElement(
                    workbookNs + "MapInfo",
                    new XAttribute("SelectionNamespaces", "xmlns:fx='urn:freex:xml-map'"),
                    new XElement(
                        workbookNs + "Schema",
                        new XAttribute("ID", "schema1"),
                        new XAttribute("SchemaRef", "customXml/item1.xml")),
                    new XElement(
                        workbookNs + "Map",
                        new XAttribute("ID", "1"),
                        new XAttribute("Name", "FreeXXmlMap"),
                        new XAttribute("RootElement", "root"),
                        new XAttribute("SchemaID", "schema1"),
                        new XAttribute("ShowImportExportValidationErrors", "1")))));
        }

        return stream.ToArray();
    }

    private static byte[] AddRichDataPackageParts(byte[] sourceBytes)
    {
        using var stream = new MemoryStream();
        stream.Write(sourceBytes, 0, sourceBytes.Length);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
            XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            XNamespace richDataNs = "http://schemas.microsoft.com/office/spreadsheetml/2017/richdata";
            XNamespace richData2Ns = "http://schemas.microsoft.com/office/spreadsheetml/2017/richdata2";

            var contentTypesXml = LoadPackageXml(archive, "[Content_Types].xml");
            AddContentTypeOverride(contentTypesXml, contentTypeNs, "/xl/metadata.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheetMetadata+xml");
            AddContentTypeOverride(contentTypesXml, contentTypeNs, "/xl/richData/rdrichvalue.xml", "application/vnd.ms-excel.rdrichvalue+xml");
            AddContentTypeOverride(contentTypesXml, contentTypeNs, "/xl/richData/rdrichvaluestructure.xml", "application/vnd.ms-excel.rdrichvaluestructure+xml");
            AddContentTypeOverride(contentTypesXml, contentTypeNs, "/xl/richData/rdRichValueTypes.xml", "application/vnd.ms-excel.rdrichvaluetypes+xml");
            AddContentTypeOverride(contentTypesXml, contentTypeNs, "/xl/richData/richValueRel.xml", "application/vnd.ms-excel.richvaluerel+xml");
            AddContentTypeDefault(contentTypesXml, contentTypeNs, "png", "image/png");
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);

            var workbookRelationshipsXml = LoadPackageXml(archive, "xl/_rels/workbook.xml.rels");
            workbookRelationshipsXml.Root!.Add(
                new XElement(
                    relationshipNs + "Relationship",
                    new XAttribute("Id", "rIdFreeXRichMetadata"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/sheetMetadata"),
                    new XAttribute("Target", "metadata.xml")),
                new XElement(
                    relationshipNs + "Relationship",
                    new XAttribute("Id", "rIdFreeXRichValue"),
                    new XAttribute("Type", "http://schemas.microsoft.com/office/2017/06/relationships/rdRichValue"),
                    new XAttribute("Target", "richData/rdrichvalue.xml")),
                new XElement(
                    relationshipNs + "Relationship",
                    new XAttribute("Id", "rIdFreeXRichValueStructure"),
                    new XAttribute("Type", "http://schemas.microsoft.com/office/2017/06/relationships/rdRichValueStructure"),
                    new XAttribute("Target", "richData/rdrichvaluestructure.xml")),
                new XElement(
                    relationshipNs + "Relationship",
                    new XAttribute("Id", "rIdFreeXRichValueTypes"),
                    new XAttribute("Type", "http://schemas.microsoft.com/office/2017/06/relationships/rdRichValueTypes"),
                    new XAttribute("Target", "richData/rdRichValueTypes.xml")),
                new XElement(
                    relationshipNs + "Relationship",
                    new XAttribute("Id", "rIdFreeXRichValueRel"),
                    new XAttribute("Type", "http://schemas.microsoft.com/office/2022/10/relationships/richValueRel"),
                    new XAttribute("Target", "richData/richValueRel.xml")));
            ReplacePackageXml(archive, "xl/_rels/workbook.xml.rels", workbookRelationshipsXml);

            ReplacePackageXml(
                archive,
                "xl/metadata.xml",
                new XDocument(
                    new XElement(
                        workbookNs + "metadata",
                        new XAttribute(XNamespace.Xmlns + "xlrd", richDataNs.NamespaceName),
                        new XElement(
                            workbookNs + "metadataTypes",
                            new XAttribute("count", "1"),
                            new XElement(
                                workbookNs + "metadataType",
                                new XAttribute("name", "XLRICHVALUE"),
                                new XAttribute("minSupportedVersion", "120000"),
                                new XAttribute("copy", "1"),
                                new XAttribute("pasteAll", "1"),
                                new XAttribute("pasteValues", "1"),
                                new XAttribute("merge", "1"),
                                new XAttribute("splitFirst", "1"),
                                new XAttribute("rowColShift", "1"),
                                new XAttribute("clearFormats", "1"),
                                new XAttribute("clearComments", "1"),
                                new XAttribute("assign", "1"),
                                new XAttribute("coerce", "1"))),
                        new XElement(workbookNs + "futureMetadata", new XAttribute("name", "XLRICHVALUE"), new XAttribute("count", "0")),
                        new XElement(workbookNs + "valueMetadata", new XAttribute("count", "0")))));

            ReplacePackageXml(
                archive,
                "xl/richData/rdrichvalue.xml",
                new XDocument(
                    new XElement(
                        richDataNs + "rvData",
                        new XAttribute("count", "1"),
                        new XElement(
                            richDataNs + "rv",
                            new XAttribute("s", "0"),
                            new XElement(richDataNs + "v", "0"),
                            new XElement(richDataNs + "v", "5")))));
            ReplacePackageXml(
                archive,
                "xl/richData/rdrichvaluestructure.xml",
                new XDocument(
                    new XElement(
                        richDataNs + "rvStructures",
                        new XAttribute("count", "1"),
                        new XElement(
                            richDataNs + "s",
                            new XAttribute("t", "_localImage"),
                            new XElement(richDataNs + "k", new XAttribute("n", "_rvRel:LocalImageIdentifier"), new XAttribute("t", "i")),
                            new XElement(richDataNs + "k", new XAttribute("n", "CalcOrigin"), new XAttribute("t", "i"))))));
            ReplacePackageXml(
                archive,
                "xl/richData/rdRichValueTypes.xml",
                new XDocument(
                    new XElement(
                        richData2Ns + "rvTypesInfo",
                        new XElement(
                            richData2Ns + "global",
                            new XElement(richData2Ns + "keyFlags")))));
            ReplacePackageXml(
                archive,
                "xl/richData/richValueRel.xml",
                new XDocument(new XElement(richDataNs + "richValueRels", new XAttribute("count", "1"))));
            ReplacePackageXml(
                archive,
                "xl/richData/_rels/richValueRel.xml.rels",
                new XDocument(
                    new XElement(
                        relationshipNs + "Relationships",
                        new XElement(
                            relationshipNs + "Relationship",
                            new XAttribute("Id", "rIdFreeXRichImage"),
                            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
                            new XAttribute("Target", "../media/image1.png")))));
            WritePackageBytes(archive, "xl/media/image1.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        }

        return stream.ToArray();
    }

    private static byte[] CreateDenseSourcePackage(int rowCount, int columnCount)
    {
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Data");
            for (var row = 1; row <= rowCount; row++)
            {
                for (var column = 1; column <= columnCount; column++)
                    sheet.Cell(row, column).Value = (row * 1000) + column;
            }

            workbook.SaveAs(stream);
        }

        return RemoveEmptyWorkbookDefinedNames(stream.ToArray());
    }

    private static byte[] AddOfficeRevisionUidAttributes(byte[] sourceBytes)
    {
        using var stream = new MemoryStream();
        stream.Write(sourceBytes, 0, sourceBytes.Length);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace markupCompatNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";
            XNamespace x14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
            XNamespace fooNs = "urn:freex-nonrevision";
            XNamespace revisionNs = "http://schemas.microsoft.com/office/spreadsheetml/2014/revision";
            XNamespace revision2Ns = "http://schemas.microsoft.com/office/spreadsheetml/2015/revision2";
            XNamespace revision10Ns = "http://schemas.microsoft.com/office/spreadsheetml/2016/revision10";

            var workbookXml = LoadPackageXml(archive, "xl/workbook.xml");
            workbookXml.Root!.SetAttributeValue(XNamespace.Xmlns + "mc", markupCompatNs.NamespaceName);
            workbookXml.Root.SetAttributeValue(XNamespace.Xmlns + "x14", x14Ns.NamespaceName);
            workbookXml.Root.SetAttributeValue(XNamespace.Xmlns + "foo", fooNs.NamespaceName);
            workbookXml.Root.SetAttributeValue(XNamespace.Xmlns + "xr2", revision2Ns.NamespaceName);
            workbookXml.Root.SetAttributeValue(XNamespace.Xmlns + "xr10", revision10Ns.NamespaceName);
            workbookXml.Root.SetAttributeValue(
                markupCompatNs + "Ignorable",
                AppendIgnorablePrefix(workbookXml.Root.Attribute(markupCompatNs + "Ignorable")?.Value, "x14", "xr2", "xr10", "foo"));
            workbookXml.Root.SetAttributeValue(revision10Ns + "uidLastSave", "{00000000-0000-0000-0000-000000000000}");
            workbookXml.Root
                .Element(workbookNs + "bookViews")!
                .Element(workbookNs + "workbookView")!
                .SetAttributeValue(revision2Ns + "uid", "{48973FB0-6DDF-407F-BFF1-05D2BBB0F9CF}");
            ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);

            var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
            worksheetXml.Root!.SetAttributeValue(XNamespace.Xmlns + "mc", markupCompatNs.NamespaceName);
            worksheetXml.Root.SetAttributeValue(XNamespace.Xmlns + "xr", revisionNs.NamespaceName);
            worksheetXml.Root.SetAttributeValue(
                markupCompatNs + "Ignorable",
                AppendIgnorablePrefix(worksheetXml.Root.Attribute(markupCompatNs + "Ignorable")?.Value, "xr"));
            worksheetXml
                .Descendants(workbookNs + "c")
                .Single(element => element.Attribute("r")?.Value == "A1")
                .SetAttributeValue(revisionNs + "uid", "{EB1F693D-8528-450A-BC10-895DEFE5B6D9}");
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        return stream.ToArray();
    }

    private static byte[] AddWorkbookOnlyOfficeRevisionLastSaveAttribute(byte[] sourceBytes)
    {
        using var stream = new MemoryStream();
        stream.Write(sourceBytes, 0, sourceBytes.Length);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace markupCompatNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";
            XNamespace revision10Ns = "http://schemas.microsoft.com/office/spreadsheetml/2016/revision10";
            var workbookXml = LoadPackageXml(archive, "xl/workbook.xml");
            workbookXml.Root!.SetAttributeValue(XNamespace.Xmlns + "mc", markupCompatNs.NamespaceName);
            workbookXml.Root.SetAttributeValue(XNamespace.Xmlns + "xr10", revision10Ns.NamespaceName);
            workbookXml.Root.SetAttributeValue(
                markupCompatNs + "Ignorable",
                AppendIgnorablePrefix(workbookXml.Root.Attribute(markupCompatNs + "Ignorable")?.Value, "xr10"));
            workbookXml.Root.SetAttributeValue(revision10Ns + "uidLastSave", "{00000000-0000-0000-0000-000000000000}");
            ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
        }

        return stream.ToArray();
    }

    private static byte[] AddWorkbookOfficeRevisionPointerElement(byte[] sourceBytes)
    {
        using var stream = new MemoryStream();
        stream.Write(sourceBytes, 0, sourceBytes.Length);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace markupCompatNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";
            XNamespace revisionNs = "http://schemas.microsoft.com/office/spreadsheetml/2014/revision";
            XNamespace revision10Ns = "http://schemas.microsoft.com/office/spreadsheetml/2016/revision10";
            var workbookXml = LoadPackageXml(archive, "xl/workbook.xml");
            workbookXml.Root!.SetAttributeValue(XNamespace.Xmlns + "mc", markupCompatNs.NamespaceName);
            workbookXml.Root.SetAttributeValue(XNamespace.Xmlns + "xr", revisionNs.NamespaceName);
            workbookXml.Root.SetAttributeValue(XNamespace.Xmlns + "xr10", revision10Ns.NamespaceName);
            workbookXml.Root.SetAttributeValue(
                markupCompatNs + "Ignorable",
                AppendIgnorablePrefix(workbookXml.Root.Attribute(markupCompatNs + "Ignorable")?.Value, "xr", "xr10"));
            workbookXml.Root.AddFirst(new XElement(
                revisionNs + "revisionPtr",
                new XAttribute("revIDLastSave", "0"),
                new XAttribute(revision10Ns + "uidLastSave", "{00000000-0000-0000-0000-000000000000}")));
            ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
        }

        return stream.ToArray();
    }

    private static byte[] AddWorksheetRevisionPrefixShadow(byte[] sourceBytes)
    {
        using var stream = new MemoryStream();
        stream.Write(sourceBytes, 0, sourceBytes.Length);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace markupCompatNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";
            XNamespace revisionNs = "http://schemas.microsoft.com/office/spreadsheetml/2014/revision";
            XNamespace nonRevisionNs = "urn:freex-nonrevision";

            var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
            worksheetXml.Root!.SetAttributeValue(XNamespace.Xmlns + "mc", markupCompatNs.NamespaceName);
            worksheetXml.Root.SetAttributeValue(XNamespace.Xmlns + "xr", revisionNs.NamespaceName);
            worksheetXml.Root.SetAttributeValue(
                markupCompatNs + "Ignorable",
                AppendIgnorablePrefix(worksheetXml.Root.Attribute(markupCompatNs + "Ignorable")?.Value, "xr"));
            worksheetXml
                .Descendants(workbookNs + "c")
                .Single(element => element.Attribute("r")?.Value == "A1")
                .SetAttributeValue(revisionNs + "uid", "{EB1F693D-8528-450A-BC10-895DEFE5B6D9}");

            var row = worksheetXml
                .Descendants(workbookNs + "row")
                .Single(element => element.Attribute("r")?.Value == "1");
            row.SetAttributeValue(XNamespace.Xmlns + "xr", nonRevisionNs.NamespaceName);
            row.SetAttributeValue(markupCompatNs + "Ignorable", "xr");
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        return stream.ToArray();
    }

    private static byte[] AddCssFontFamilyRichSharedString(byte[] sourceBytes)
    {
        using var stream = new MemoryStream();
        stream.Write(sourceBytes, 0, sourceBytes.Length);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var sharedStringsXml = LoadPackageXml(archive, "xl/sharedStrings.xml");

            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var sharedString = sharedStringsXml.Root!
                .Elements(workbookNs + "si")
                .Single(element => element.Element(workbookNs + "t")?.Value == "original value");
            sharedString.ReplaceNodes(new XElement(
                workbookNs + "r",
                new XElement(
                    workbookNs + "rPr",
                    new XElement(workbookNs + "rFont", new XAttribute("val", "\"Google Sans\", Roboto, sans-serif")),
                    new XElement(workbookNs + "sz", new XAttribute("val", "11"))),
                new XElement(workbookNs + "t", "original value")));

            ReplacePackageXml(archive, "xl/sharedStrings.xml", sharedStringsXml);
        }

        return stream.ToArray();
    }

    private static byte[] AddMinimalCustomSheetViews(byte[] sourceBytes)
    {
        return UpdateWorksheetXml(sourceBytes, worksheetXml =>
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            worksheetXml.Root!.AddFirst(new XElement(
                worksheetNs + "customSheetViews",
                new XElement(
                    worksheetNs + "customSheetView",
                    new XAttribute("guid", "{11111111-1111-1111-1111-111111111111}"),
                    new XAttribute("view", "normal"),
                    new XAttribute("scale", "120"),
                    new XAttribute("state", "visible"))));
        });
    }

    private static byte[] AddMatchedCustomViews(byte[] sourceBytes)
    {
        var withWorkbookViews = AddCustomWorkbookViews(sourceBytes);
        return UpdateWorksheetXml(withWorkbookViews, worksheetXml =>
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            worksheetXml.Root!.AddFirst(new XElement(
                worksheetNs + "customSheetViews",
                new XElement(
                    worksheetNs + "customSheetView",
                    new XAttribute("guid", "{22222222-2222-2222-2222-222222222222}"),
                    new XAttribute("view", "normal"),
                    new XAttribute("scale", "120"),
                    new XAttribute("showGridLines", "0"),
                    new XAttribute("state", "visible"),
                    new XElement(
                        worksheetNs + "pane",
                        new XAttribute("xSplit", "1"),
                        new XAttribute("ySplit", "1"),
                        new XAttribute("state", "split")))));
        });
    }

    private static byte[] UpdateWorksheetXml(byte[] sourceBytes, Action<XDocument> update)
    {
        using var stream = new MemoryStream();
        stream.Write(sourceBytes, 0, sourceBytes.Length);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");

            update(worksheetXml);

            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        return stream.ToArray();
    }

    private static byte[] AddCustomWorkbookViews(byte[] sourceBytes)
    {
        using var stream = new MemoryStream();
        stream.Write(sourceBytes, 0, sourceBytes.Length);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var workbookXml = LoadPackageXml(archive, "xl/workbook.xml");

            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            workbookXml.Root!.Add(new XElement(
                workbookNs + "customWorkbookViews",
                new XElement(
                    workbookNs + "customWorkbookView",
                    new XAttribute("name", "FreeXView"),
                    new XAttribute("guid", "{22222222-2222-2222-2222-222222222222}"),
                    new XAttribute("autoUpdate", "0"),
                    new XAttribute("mergeInterval", "0"),
                    new XAttribute("personalView", "0"),
                    new XAttribute("includePrintSettings", "1"),
                    new XAttribute("includeHiddenRowCol", "1"))));

            ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
        }

        return stream.ToArray();
    }

    private static XElement? ReadWorkbookCustomWorkbookViews(byte[] packageBytes)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = LoadPackageXml(archive, "xl/workbook.xml");
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return document.Root!.Element(workbookNs + "customWorkbookViews");
    }

    private static XElement? ReadWorksheetCustomSheetViews(byte[] packageBytes, string worksheetPath)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = LoadPackageXml(archive, worksheetPath);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return document.Root!.Element(worksheetNs + "customSheetViews");
    }

    private static string? ReadRichSharedStringFontValue(byte[] packageBytes, string plainText)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = LoadPackageXml(archive, "xl/sharedStrings.xml");
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return document.Root!
            .Elements(workbookNs + "si")
            .Single(element => string.Concat(element.Descendants(workbookNs + "t").Select(text => text.Value)) == plainText)
            .Element(workbookNs + "r")?
            .Element(workbookNs + "rPr")?
            .Element(workbookNs + "rFont")?
            .Attribute("val")
            ?.Value;
    }

    private static byte[] CreateStyledSourcePackage()
    {
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Data");
            sheet.Cell("A1").Value = "plain";
            sheet.Cell("B1").Value = "styled";
            sheet.Cell("B1").Style.Font.Bold = true;
            sheet.Cell("B1").Style.Fill.BackgroundColor = XLColor.FromArgb(221, 235, 247);
            sheet.Cell("D1").Style.Font.Italic = true;
            workbook.SaveAs(stream);
        }

        return RemoveEmptyWorkbookDefinedNames(stream.ToArray());
    }

    private static byte[] RemoveEmptyWorkbookDefinedNames(byte[] sourceBytes)
    {
        using var stream = new MemoryStream();
        stream.Write(sourceBytes, 0, sourceBytes.Length);
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var workbookXml = LoadPackageXml(archive, "xl/workbook.xml");
            var changed = false;
            foreach (var definedNames in workbookXml.Root!.Elements(workbookNs + "definedNames").ToList())
            {
                if (definedNames.HasElements || definedNames.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration))
                    continue;

                definedNames.Remove();
                changed = true;
            }

            if (changed)
                ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
        }

        return stream.ToArray();
    }

    private static byte[] CreateMergedRegionSourcePackage()
    {
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Data");
            sheet.Cell("A1").Value = "merged 1";
            sheet.Cell("C1").Value = "merged 2";
            sheet.Range("A1:B1").Merge();
            sheet.Range("C1:D1").Merge();
            workbook.SaveAs(stream);
        }

        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");

            var worksheetNs = worksheetXml.Root!.Name.Namespace;
            var mergeCells = worksheetXml.Root.Element(worksheetNs + "mergeCells");
            mergeCells.Should().NotBeNull();
            mergeCells!.SetAttributeValue("nativeMergeContainerAttr", "kept");
            foreach (var mergeCell in mergeCells.Elements(worksheetNs + "mergeCell"))
            {
                var reference = mergeCell.Attribute("ref")?.Value;
                if (reference == "A1:B1")
                    mergeCell.SetAttributeValue("nativeMergeCellAttr", "kept-A1-B1");
                else if (reference == "C1:D1")
                    mergeCell.SetAttributeValue("nativeMergeCellAttr", "kept-C1-D1");
            }

            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        return RemoveEmptyWorkbookDefinedNames(stream.ToArray());
    }

    private static byte[] CreateInternalHyperlinkSourcePackage()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(
            (
                "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """),
            (
                "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """),
            (
                "xl/workbook.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Data" sheetId="1" r:id="rId1"/>
                  </sheets>
                </workbook>
                """),
            (
                "xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """),
            (
                "xl/styles.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <fonts count="1"><font><sz val="11"/><color theme="1"/><name val="Calibri"/><family val="2"/><scheme val="minor"/></font></fonts>
                  <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
                  <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                  <cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
                  <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
                  <dxfs count="0"/>
                  <tableStyles count="0" defaultTableStyle="TableStyleMedium2" defaultPivotStyle="PivotStyleLight16"/>
                </styleSheet>
                """),
            (
                "xl/worksheets/sheet1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <dimension ref="A1:C3"/>
                  <sheetData>
                    <row r="1"><c r="A1" t="inlineStr"><is><t>Jump</t></is></c></row>
                    <row r="2"><c r="B2"><v>1</v></c></row>
                    <row r="3"><c r="C3"><v>2</v></c></row>
                  </sheetData>
                  <hyperlinks nativeHyperlinksAttr="kept">
                    <hyperlink ref="A1" location="Data!B2" tooltip="Jump original" display="Jump display" customAttr="kept-link"/>
                  </hyperlinks>
                </worksheet>
                """));

        return package.ToArray();
    }

    private static byte[] CreateLegacyCommentSourcePackage(string vmlObjectType = "Note")
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(
            (
                "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="vml" ContentType="application/vnd.openxmlformats-officedocument.vmlDrawing"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                  <Override PartName="/xl/comments1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.comments+xml"/>
                </Types>
                """),
            (
                "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """),
            (
                "xl/workbook.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Data" sheetId="1" r:id="rId1"/>
                  </sheets>
                </workbook>
                """),
            (
                "xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """),
            (
                "xl/styles.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <fonts count="1"><font><sz val="11"/><color theme="1"/><name val="Calibri"/><family val="2"/><scheme val="minor"/></font></fonts>
                  <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
                  <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                  <cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
                  <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
                  <dxfs count="0"/>
                  <tableStyles count="0" defaultTableStyle="TableStyleMedium2" defaultPivotStyle="TableStyleLight16"/>
                </styleSheet>
                """),
            (
                "xl/worksheets/sheet1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <dimension ref="A1:C2"/>
                  <sheetData>
                    <row r="1"><c r="A1" t="inlineStr"><is><t>source</t></is></c></row>
                    <row r="2"><c r="C2" t="inlineStr"><is><t>review</t></is></c></row>
                  </sheetData>
                  <legacyDrawing r:id="rId2"/>
                </worksheet>
                """),
            (
                "xl/worksheets/_rels/sheet1.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments" Target="../comments1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing" Target="../drawings/vmlDrawing1.vml"/>
                </Relationships>
                """),
            (
                "xl/comments1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <comments xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <authors>
                    <author>Excel Reviewer</author>
                  </authors>
                  <commentList nativeCommentListAttr="kept-list">
                    <comment ref="C2" authorId="0" nativeCommentAttr="kept-comment">
                      <text><r><t>Original note</t></r></text>
                    </comment>
                  </commentList>
                </comments>
                """),
            (
                "xl/drawings/vmlDrawing1.vml",
                $$"""
                <?xml version="1.0" encoding="UTF-8"?>
                <xml xmlns:v="urn:schemas-microsoft-com:vml" xmlns:o="urn:schemas-microsoft-com:office:office" xmlns:x="urn:schemas-microsoft-com:office:excel">
                  <v:shape id="_x0000_s1025" type="#_x0000_t202" style="position:absolute;margin-left:80pt;margin-top:6pt;width:108pt;height:59.25pt;z-index:1;visibility:hidden" fillcolor="#ffffe1" o:insetmode="auto">
                    <v:fill color2="#ffffe1"/>
                    <v:shadow color="black" obscured="t"/>
                    <v:path o:connecttype="none"/>
                    <v:textbox style="mso-direction-alt:auto"><div style="text-align:left"/></v:textbox>
                    <x:ClientData ObjectType="{{vmlObjectType}}">
                      <x:MoveWithCells/>
                      <x:SizeWithCells/>
                      <x:Anchor>2, 15, 1, 2, 4, 15, 5, 3</x:Anchor>
                      <x:AutoFill>False</x:AutoFill>
                      <x:Row>1</x:Row>
                      <x:Column>2</x:Column>
                    </x:ClientData>
                  </v:shape>
                </xml>
                """));

        return package.ToArray();
    }

    private static byte[] AddPictureShapeToLegacyCommentVml(byte[] sourceBytes)
    {
        using var stream = new MemoryStream();
        stream.Write(sourceBytes, 0, sourceBytes.Length);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            AddLegacyDrawingPictureParts(archive, addPictureShape: true);
        }

        return stream.ToArray();
    }

    private static byte[] CreatePictureOnlyLegacyDrawingSourcePackage()
    {
        using var stream = new MemoryStream();
        var sourceBytes = CreateLegacyCommentSourcePackage(vmlObjectType: "Pict");
        stream.Write(sourceBytes, 0, sourceBytes.Length);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
            XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";

            var contentTypesXml = LoadPackageXml(archive, "[Content_Types].xml");
            contentTypesXml.Root!
                .Elements(contentTypeNs + "Override")
                .Where(element => string.Equals((string?)element.Attribute("PartName"), "/xl/comments1.xml", StringComparison.OrdinalIgnoreCase))
                .Remove();
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);

            archive.GetEntry("xl/comments1.xml")?.Delete();
            var worksheetRelationshipsXml = LoadPackageXml(archive, "xl/worksheets/_rels/sheet1.xml.rels");
            worksheetRelationshipsXml.Root!
                .Elements(relationshipNs + "Relationship")
                .Where(element => string.Equals(
                    (string?)element.Attribute("Type"),
                    "http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments",
                    StringComparison.OrdinalIgnoreCase))
                .Remove();
            ReplacePackageXml(archive, "xl/worksheets/_rels/sheet1.xml.rels", worksheetRelationshipsXml);

            AddLegacyDrawingPictureParts(archive, addPictureShape: false);
        }

        return stream.ToArray();
    }

    private static void AddLegacyDrawingPictureParts(ZipArchive archive, bool addPictureShape)
    {
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        XNamespace packageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace vmlNs = "urn:schemas-microsoft-com:vml";
        XNamespace officeNs = "urn:schemas-microsoft-com:office:office";
        XNamespace excelNs = "urn:schemas-microsoft-com:office:excel";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        var contentTypesXml = LoadPackageXml(archive, "[Content_Types].xml");
        AddContentTypeDefault(contentTypesXml, contentTypeNs, "png", "image/png");
        ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);

        var vmlXml = LoadPackageXml(archive, "xl/drawings/vmlDrawing1.vml");
        var pictureShape = addPictureShape
            ? new XElement(
                vmlNs + "shape",
                new XAttribute("id", "_x0000_s1026"),
                new XAttribute("type", "#_x0000_t75"),
                new XAttribute("style", "position:absolute;margin-left:12pt;margin-top:12pt;width:48pt;height:36pt;z-index:2"),
                new XElement(vmlNs + "imagedata", new XAttribute(relNs + "id", "rIdImage1"), new XAttribute(officeNs + "title", "COIN-style preserved picture")),
                new XElement(
                    excelNs + "ClientData",
                    new XAttribute("ObjectType", "Pict"),
                    new XElement(excelNs + "MoveWithCells"),
                    new XElement(excelNs + "SizeWithCells"),
                    new XElement(excelNs + "Anchor", "1, 15, 1, 2, 2, 15, 3, 3")))
            : vmlXml.Descendants(vmlNs + "shape").Single();

        if (!addPictureShape)
        {
            pictureShape.AddFirst(new XElement(
                vmlNs + "imagedata",
                new XAttribute(relNs + "id", "rIdImage1"),
                new XAttribute(officeNs + "title", "COIN-style preserved picture")));
        }
        else
        {
            vmlXml.Root!.Add(pictureShape);
        }

        ReplacePackageXml(archive, "xl/drawings/vmlDrawing1.vml", vmlXml);
        ReplacePackageXml(
            archive,
            "xl/drawings/_rels/vmlDrawing1.vml.rels",
            new XDocument(new XElement(
                packageRelationshipNs + "Relationships",
                new XElement(
                    packageRelationshipNs + "Relationship",
                    new XAttribute("Id", "rIdImage1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
                    new XAttribute("Target", "../media/image1.png")))));
        WritePackageBytes(archive, "xl/media/image1.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
    }

    private static byte[] CreateStructuredTableSourcePackage(bool includeFilter = false)
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(
            (
                "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                  <Override PartName="/xl/tables/table1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.table+xml"/>
                </Types>
                """),
            (
                "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """),
            (
                "xl/workbook.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Data" sheetId="1" r:id="rId1"/>
                  </sheets>
                </workbook>
                """),
            (
                "xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """),
            (
                "xl/styles.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <fonts count="1"><font><sz val="11"/><color theme="1"/><name val="Calibri"/><family val="2"/><scheme val="minor"/></font></fonts>
                  <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
                  <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                  <cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
                  <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
                  <dxfs count="0"/>
                  <tableStyles count="0" defaultTableStyle="TableStyleMedium2" defaultPivotStyle="TableStyleLight16"/>
                </styleSheet>
                """),
            (
                "xl/worksheets/sheet1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <dimension ref="A1:C4"/>
                  <sheetData>
                    <row r="1"><c r="A1" t="inlineStr"><is><t>Category</t></is></c><c r="B1" t="inlineStr"><is><t>Amount</t></is></c></row>
                    <row r="2"><c r="A2" t="inlineStr"><is><t>East</t></is></c><c r="B2"><v>10</v></c></row>
                    <row r="3"><c r="A3" t="inlineStr"><is><t>West</t></is></c><c r="B3"><v>20</v></c></row>
                    <row r="4"><c r="C4" t="inlineStr"><is><t>outside</t></is></c></row>
                  </sheetData>
                  <tableParts count="1"><tablePart r:id="rId1"/></tableParts>
                </worksheet>
                """),
            (
                "xl/worksheets/_rels/sheet1.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/table" Target="../tables/table1.xml"/>
                </Relationships>
                """),
            (
                "xl/tables/table1.xml",
                CreateStructuredTableXml(includeFilter)));

        return package.ToArray();
    }

    private static byte[] CreateSparklineSourcePackage()
    {
        var workbook = new Workbook("SparklinePatch");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 5), new NumberValue(10));
        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 3)),
            Location = new CellAddress(sheet.Id, 1, 4),
            Kind = SparklineKind.Column
        });

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        return stream.ToArray();
    }

    private static byte[] CreateChartSourcePackage()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(
            (
                "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                  <Override PartName="/xl/drawings/drawing1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawing+xml"/>
                  <Override PartName="/xl/charts/chart1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawingml.chart+xml"/>
                </Types>
                """),
            (
                "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """),
            (
                "xl/workbook.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Data" sheetId="1" r:id="rId1"/>
                  </sheets>
                </workbook>
                """),
            (
                "xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """),
            (
                "xl/styles.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <fonts count="1"><font><sz val="11"/><color theme="1"/><name val="Calibri"/><family val="2"/><scheme val="minor"/></font></fonts>
                  <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
                  <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                  <cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
                  <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
                  <dxfs count="0"/>
                  <tableStyles count="0" defaultTableStyle="TableStyleMedium2" defaultPivotStyle="PivotStyleLight16"/>
                </styleSheet>
                """),
            (
                "xl/worksheets/sheet1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <dimension ref="A1:D4"/>
                  <sheetData>
                    <row r="1"><c r="A1" t="inlineStr"><is><t>Region</t></is></c><c r="B1" t="inlineStr"><is><t>Sales</t></is></c></row>
                    <row r="2"><c r="A2" t="inlineStr"><is><t>East</t></is></c><c r="B2"><v>10</v></c></row>
                    <row r="3"><c r="A3" t="inlineStr"><is><t>West</t></is></c><c r="B3"><v>20</v></c></row>
                    <row r="4"><c r="D4" t="inlineStr"><is><t>outside</t></is></c></row>
                  </sheetData>
                  <drawing r:id="rId1"/>
                </worksheet>
                """),
            (
                "xl/worksheets/_rels/sheet1.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing" Target="../drawings/drawing1.xml"/>
                </Relationships>
                """),
            (
                "xl/drawings/drawing1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                          xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <xdr:twoCellAnchor>
                    <xdr:from><xdr:col>3</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>0</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
                    <xdr:to><xdr:col>8</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>10</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
                    <xdr:graphicFrame macro="">
                      <xdr:nvGraphicFramePr>
                        <xdr:cNvPr id="2" name="Sales Chart"/>
                        <xdr:cNvGraphicFramePr/>
                      </xdr:nvGraphicFramePr>
                      <xdr:xfrm>
                        <a:off x="0" y="0"/>
                        <a:ext cx="0" cy="0"/>
                      </xdr:xfrm>
                      <a:graphic>
                        <a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/chart">
                          <c:chart r:id="rId1"/>
                        </a:graphicData>
                      </a:graphic>
                    </xdr:graphicFrame>
                    <xdr:clientData/>
                  </xdr:twoCellAnchor>
                </xdr:wsDr>
                """),
            (
                "xl/drawings/_rels/drawing1.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart" Target="../charts/chart1.xml"/>
                </Relationships>
                """),
            (
                "xl/charts/chart1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                              xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                  <c:chart>
                    <c:title><c:tx><c:rich><a:p><a:r><a:t>Sales</a:t></a:r></a:p></c:rich></c:tx></c:title>
                    <c:plotArea>
                      <c:barChart>
                        <c:barDir val="col"/>
                        <c:ser>
                          <c:idx val="0"/>
                          <c:order val="0"/>
                          <c:tx><c:strRef><c:f>Data!$B$1</c:f></c:strRef></c:tx>
                          <c:cat><c:strRef><c:f>Data!$A$2:$A$3</c:f></c:strRef></c:cat>
                          <c:val><c:numRef><c:f>Data!$B$2:$B$3</c:f></c:numRef></c:val>
                        </c:ser>
                      </c:barChart>
                    </c:plotArea>
                  </c:chart>
                </c:chartSpace>
                """));

        return package.ToArray();
    }

    private static byte[] AddChartThemeOverridePackageGraph(byte[] sourceBytes)
    {
        using var stream = new MemoryStream();
        stream.Write(sourceBytes, 0, sourceBytes.Length);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
            XNamespace packageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

            var contentTypesXml = LoadPackageXml(archive, "[Content_Types].xml");
            AddContentTypeOverride(
                contentTypesXml,
                contentTypeNs,
                "/xl/theme/themeOverride1.xml",
                "application/vnd.openxmlformats-officedocument.themeOverride+xml");
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);
            ReplacePackageXml(
                archive,
                "xl/charts/_rels/chart1.xml.rels",
                new XDocument(new XElement(
                    packageRelationshipNs + "Relationships",
                    new XElement(
                        packageRelationshipNs + "Relationship",
                        new XAttribute("Id", "rIdTheme1"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/themeOverride"),
                        new XAttribute("Target", "../theme/themeOverride1.xml")))));
            ReplacePackageXml(
                archive,
                "xl/theme/themeOverride1.xml",
                new XDocument(new XElement(
                    drawingNs + "themeOverride",
                    new XElement(drawingNs + "clrScheme", new XAttribute("name", "Office")))));
        }

        return stream.ToArray();
    }

    private static byte[] CreateSmartArtDiagramSourcePackage()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(
            (
                "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                  <Override PartName="/xl/drawings/drawing1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawing+xml"/>
                  <Override PartName="/xl/diagrams/data1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml"/>
                  <Override PartName="/xl/diagrams/layout1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawingml.diagramLayout+xml"/>
                  <Override PartName="/xl/diagrams/quickStyle1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawingml.diagramStyle+xml"/>
                  <Override PartName="/xl/diagrams/colors1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawingml.diagramColors+xml"/>
                </Types>
                """),
            (
                "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """),
            (
                "xl/workbook.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Data" sheetId="1" r:id="rId1"/>
                  </sheets>
                </workbook>
                """),
            (
                "xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """),
            (
                "xl/styles.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <fonts count="1"><font><sz val="11"/><color theme="1"/><name val="Calibri"/><family val="2"/><scheme val="minor"/></font></fonts>
                  <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
                  <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                  <cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
                  <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
                  <dxfs count="0"/>
                  <tableStyles count="0" defaultTableStyle="TableStyleMedium2" defaultPivotStyle="PivotStyleLight16"/>
                </styleSheet>
                """),
            (
                "xl/worksheets/sheet1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <dimension ref="A1:D4"/>
                  <sheetData>
                    <row r="1"><c r="A1" t="inlineStr"><is><t>original value</t></is></c></row>
                    <row r="4"><c r="D4" t="inlineStr"><is><t>outside</t></is></c></row>
                  </sheetData>
                  <drawing r:id="rId1"/>
                </worksheet>
                """),
            (
                "xl/worksheets/_rels/sheet1.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing" Target="../drawings/drawing1.xml"/>
                </Relationships>
                """),
            (
                "xl/drawings/drawing1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                          xmlns:dgm="http://schemas.openxmlformats.org/drawingml/2006/diagram"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <xdr:absoluteAnchor>
                    <xdr:pos x="0" y="0"/>
                    <xdr:ext cx="1828800" cy="914400"/>
                    <xdr:graphicFrame macro="">
                      <xdr:nvGraphicFramePr>
                        <xdr:cNvPr id="2" name="FreeX SmartArt"/>
                        <xdr:cNvGraphicFramePr/>
                      </xdr:nvGraphicFramePr>
                      <xdr:xfrm>
                        <a:off x="0" y="0"/>
                        <a:ext cx="1828800" cy="914400"/>
                      </xdr:xfrm>
                      <a:graphic>
                        <a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/diagram">
                          <dgm:relIds r:dm="rIdDiagramData1" r:lo="rIdDiagramLayout1" r:qs="rIdDiagramQuickStyle1" r:cs="rIdDiagramColors1"/>
                        </a:graphicData>
                      </a:graphic>
                    </xdr:graphicFrame>
                    <xdr:clientData/>
                  </xdr:absoluteAnchor>
                </xdr:wsDr>
                """),
            (
                "xl/drawings/_rels/drawing1.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdDiagramData1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramData" Target="../diagrams/data1.xml"/>
                  <Relationship Id="rIdDiagramLayout1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramLayout" Target="../diagrams/layout1.xml"/>
                  <Relationship Id="rIdDiagramQuickStyle1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramQuickStyle" Target="../diagrams/quickStyle1.xml"/>
                  <Relationship Id="rIdDiagramColors1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramColors" Target="../diagrams/colors1.xml"/>
                </Relationships>
                """),
            ("xl/diagrams/data1.xml", """<dgm:dataModel xmlns:dgm="http://schemas.openxmlformats.org/drawingml/2006/diagram"/>"""),
            ("xl/diagrams/layout1.xml", """<dgm:layoutDef xmlns:dgm="http://schemas.openxmlformats.org/drawingml/2006/diagram"/>"""),
            ("xl/diagrams/quickStyle1.xml", """<dgm:styleDef xmlns:dgm="http://schemas.openxmlformats.org/drawingml/2006/diagram"/>"""),
            ("xl/diagrams/colors1.xml", """<dgm:colorsDef xmlns:dgm="http://schemas.openxmlformats.org/drawingml/2006/diagram"/>"""));

        return package.ToArray();
    }

    private static byte[] CreateChartExSourcePackage()
    {
        var workbook = new Workbook("ChartExPatch");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new TextValue("outside"));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Histogram,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2)),
            Title = "Sales Histogram"
        });

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        return stream.ToArray();
    }

    private static byte[] CreateDrawingShapeSourcePackage()
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(
            (
                "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                  <Override PartName="/xl/drawings/drawing1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawing+xml"/>
                </Types>
                """),
            (
                "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """),
            (
                "xl/workbook.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Data" sheetId="1" r:id="rId1"/>
                  </sheets>
                </workbook>
                """),
            (
                "xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """),
            (
                "xl/styles.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <fonts count="1"><font><sz val="11"/><color theme="1"/><name val="Calibri"/><family val="2"/><scheme val="minor"/></font></fonts>
                  <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
                  <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                  <cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
                  <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
                  <dxfs count="0"/>
                  <tableStyles count="0" defaultTableStyle="TableStyleMedium2" defaultPivotStyle="PivotStyleLight16"/>
                </styleSheet>
                """),
            (
                "xl/worksheets/sheet1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <dimension ref="A1:D4"/>
                  <sheetData>
                    <row r="1"><c r="A1" t="inlineStr"><is><t>Header</t></is></c></row>
                    <row r="4"><c r="D4" t="inlineStr"><is><t>outside</t></is></c></row>
                  </sheetData>
                  <drawing r:id="rId1"/>
                </worksheet>
                """),
            (
                "xl/worksheets/_rels/sheet1.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing" Target="../drawings/drawing1.xml"/>
                </Relationships>
                """),
            (
                "xl/drawings/drawing1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                  <xdr:twoCellAnchor>
                    <xdr:from><xdr:col>0</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>0</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
                    <xdr:to><xdr:col>2</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>3</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
                    <xdr:sp>
                      <xdr:nvSpPr>
                        <xdr:cNvPr id="2" name="Review Callout" title="Review note title" descr="Review note alt"/>
                        <xdr:cNvSpPr txBox="1"/>
                      </xdr:nvSpPr>
                      <xdr:spPr><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></xdr:spPr>
                      <xdr:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:t>Review note</a:t></a:r></a:p></xdr:txBody>
                    </xdr:sp>
                    <xdr:clientData/>
                  </xdr:twoCellAnchor>
                  <xdr:twoCellAnchor>
                    <xdr:from><xdr:col>2</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>0</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
                    <xdr:to><xdr:col>4</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>3</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
                    <xdr:sp>
                      <xdr:nvSpPr>
                        <xdr:cNvPr id="3" name="Approval Shape" title="Approval marker title" descr="Approval marker alt"/>
                        <xdr:cNvSpPr/>
                      </xdr:nvSpPr>
                      <xdr:spPr><a:prstGeom prst="ellipse"><a:avLst/></a:prstGeom></xdr:spPr>
                    </xdr:sp>
                    <xdr:clientData/>
                  </xdr:twoCellAnchor>
                </xdr:wsDr>
                """),
            (
                "xl/drawings/_rels/drawing1.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"/>
                """));

        return package.ToArray();
    }

    private static byte[] CreateOpaqueDrawingShapeSourcePackage()
    {
        using var stream = new MemoryStream();
        var sourceBytes = CreateDrawingShapeSourcePackage();
        stream.Write(sourceBytes, 0, sourceBytes.Length);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
            XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

            var drawingXml = LoadPackageXml(archive, "xl/drawings/drawing1.xml");
            var opaqueShape = drawingXml
                .Descendants(spreadsheetDrawingNs + "sp")
                .Single(element => string.Equals(
                    (string?)element
                        .Element(spreadsheetDrawingNs + "nvSpPr")
                        ?.Element(spreadsheetDrawingNs + "cNvPr")
                        ?.Attribute("name"),
                    "Approval Shape",
                    StringComparison.Ordinal));
            opaqueShape
                .Element(spreadsheetDrawingNs + "spPr")!
                .Element(drawingNs + "prstGeom")!
                .SetAttributeValue("prst", "gear6");
            ReplacePackageXml(archive, "xl/drawings/drawing1.xml", drawingXml);
        }

        return stream.ToArray();
    }

    private static byte[] CreatePivotSourcePackage()
    {
        var workbook = new Workbook("PivotPatch");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new TextValue("outside"));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = sourceRange.ToString(),
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
            RecordCount = 2,
            CreatedVersion = 8,
            MinRefreshableVersion = 4
        };
        cache.Fields.Add(new PivotCacheFieldModel("Category"));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", 4));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = sourceRange,
            TargetRange = new GridRange(
                new CellAddress(sheet.Id, 6, 4),
                new CellAddress(sheet.Id, 9, 5)),
            PackagePart = "xl/pivotTables/pivotTable1.xml"
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum", 4));
        sheet.PivotTables.Add(pivot);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        return stream.ToArray();
    }

    private static byte[] CreatePivotChartSourcePackage()
    {
        var workbook = new Workbook("PivotChartPatch");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new TextValue("outside"));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = sourceRange.ToString(),
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
            RecordCount = 2,
            CreatedVersion = 8,
            MinRefreshableVersion = 4
        };
        cache.Fields.Add(new PivotCacheFieldModel("Category"));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", 4));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = sourceRange,
            TargetRange = new GridRange(
                new CellAddress(sheet.Id, 6, 4),
                new CellAddress(sheet.Id, 9, 5)),
            PackagePart = "xl/pivotTables/pivotTable1.xml"
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum", 4));
        sheet.PivotTables.Add(pivot);

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = pivot.TargetRange,
            IsPivotChart = true,
            PivotTableName = pivot.Name,
            PivotCacheId = pivot.CacheId,
            Title = "Pivot Chart",
            Left = 20,
            Top = 20,
            Width = 420,
            Height = 280
        });

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        return stream.ToArray();
    }

    private static string CreateStructuredTableXml(bool includeFilter) =>
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <table xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" id="1" name="Table1" displayName="Table1" ref="A1:B3" totalsRowShown="0">
          AUTOFILTER
          <tableColumns count="2">
            <tableColumn id="1" name="Category"/>
            <tableColumn id="2" name="Amount"/>
          </tableColumns>
          <tableStyleInfo name="TableStyleMedium2" showFirstColumn="0" showLastColumn="0" showRowStripes="1" showColumnStripes="0"/>
        </table>
        """.Replace(
            "AUTOFILTER",
            includeFilter
                ? """
                  <autoFilter ref="A1:B3"><filterColumn colId="0"><filters><filter val="East"/></filters></filterColumn></autoFilter>
                  """
                : """<autoFilter ref="A1:B3"/>""",
            StringComparison.Ordinal);

    private static byte[] CreateFormulaSourcePackage(string formulaElement = "<f>1+1</f>")
    {
        using var package = XlsxPackageTestFixtures.CreatePackage(
            (
                "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                  <Override PartName="/xl/calcChain.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.calcChain+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """),
            (
                "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """),
            (
                "xl/workbook.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Data" sheetId="1" r:id="rId1"/>
                  </sheets>
                  <calcPr calcId="191029"/>
                </workbook>
                """),
            (
                "xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                  <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/calcChain" Target="calcChain.xml"/>
                </Relationships>
                """),
            (
                "xl/styles.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <fonts count="1"><font><sz val="11"/><color theme="1"/><name val="Calibri"/><family val="2"/><scheme val="minor"/></font></fonts>
                  <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
                  <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                  <cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs>
                  <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
                  <dxfs count="0"/>
                  <tableStyles count="0" defaultTableStyle="TableStyleMedium2" defaultPivotStyle="PivotStyleLight16"/>
                </styleSheet>
                """),
            (
                "xl/calcChain.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <calcChain xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <c r="A1" i="1"/>
                </calcChain>
                """),
            (
                "xl/worksheets/sheet1.xml",
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <dimension ref="A1:A1"/>
                  <sheetData>
                    <row r="1"><c r="A1">FORMULA_ELEMENT<v>2</v></c></row>
                  </sheetData>
                </worksheet>
                """.Replace("FORMULA_ELEMENT", formulaElement, StringComparison.Ordinal)));

        return package.ToArray();
    }

    private static byte[] ReadPackageEntry(byte[] packageBytes, string path)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry(path);
        entry.Should().NotBeNull();
        using var entryStream = entry!.Open();
        using var bytes = new MemoryStream();
        entryStream.CopyTo(bytes);
        return bytes.ToArray();
    }

    private static XDocument LoadPackageXml(ZipArchive archive, string path) =>
        XlsxPackageTestFixtures.LoadPackageXml(archive, path);

    private static void ReplacePackageXml(ZipArchive archive, string path, XDocument document)
    {
        archive.GetEntry(path)?.Delete();
        var replacement = archive.CreateEntry(path);
        using var replacementStream = replacement.Open();
        document.Save(replacementStream, System.Xml.Linq.SaveOptions.DisableFormatting);
    }

    private static void AddContentTypeOverride(
        XDocument contentTypesXml,
        XNamespace contentTypeNs,
        string partName,
        string contentType)
    {
        contentTypesXml.Root!
            .Elements(contentTypeNs + "Override")
            .Where(element => string.Equals((string?)element.Attribute("PartName"), partName, StringComparison.OrdinalIgnoreCase))
            .Remove();
        contentTypesXml.Root.Add(new XElement(
            contentTypeNs + "Override",
            new XAttribute("PartName", partName),
            new XAttribute("ContentType", contentType)));
    }

    private static void AddContentTypeDefault(
        XDocument contentTypesXml,
        XNamespace contentTypeNs,
        string extension,
        string contentType)
    {
        if (contentTypesXml.Root!
            .Elements(contentTypeNs + "Default")
            .Any(element => string.Equals((string?)element.Attribute("Extension"), extension, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        contentTypesXml.Root.Add(new XElement(
            contentTypeNs + "Default",
            new XAttribute("Extension", extension),
            new XAttribute("ContentType", contentType)));
    }

    private static void WritePackageBytes(ZipArchive archive, string path, byte[] bytes)
    {
        archive.GetEntry(path)?.Delete();
        var replacement = archive.CreateEntry(path);
        using var replacementStream = replacement.Open();
        replacementStream.Write(bytes, 0, bytes.Length);
    }

    private static string? ReadMarkupCompatibilityIgnorable(byte[] packageBytes, string path)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = LoadPackageXml(archive, path);
        XNamespace markupCompatNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";
        return document.Root?.Attribute(markupCompatNs + "Ignorable")?.Value;
    }

    private static string? ReadRowMarkupCompatibilityIgnorable(byte[] packageBytes, string worksheetPath, uint row)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = LoadPackageXml(archive, worksheetPath);
        XNamespace markupCompatNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";
        var ns = document.Root!.Name.Namespace;
        return document
            .Descendants(ns + "row")
            .Single(element => element.Attribute("r")?.Value == row.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Attribute(markupCompatNs + "Ignorable")
            ?.Value;
    }

    private static string AppendIgnorablePrefix(string? currentValue, params string[] prefixes)
    {
        var values = (currentValue ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        foreach (var prefix in prefixes)
        {
            if (!values.Contains(prefix, StringComparer.Ordinal))
                values.Add(prefix);
        }

        return string.Join(" ", values);
    }

    private static bool PackageXmlHasOfficeRevisionAttributes(byte[] packageBytes, string path)
    {
        using var stream = new MemoryStream(ReadPackageEntry(packageBytes, path), writable: false);
        var document = XDocument.Load(stream);
        return document.Root!
            .DescendantsAndSelf()
            .SelectMany(element => element.Attributes())
            .Any(attribute =>
                !attribute.IsNamespaceDeclaration &&
                attribute.Name.NamespaceName.StartsWith(
                    "http://schemas.microsoft.com/office/spreadsheetml/",
                    StringComparison.Ordinal) &&
                attribute.Name.NamespaceName.Contains("/revision", StringComparison.Ordinal));
    }

    private static bool PackageHasEntry(byte[] packageBytes, string path)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        return archive.GetEntry(path) is not null;
    }

    private static IReadOnlyList<string> ReadContentTypeOverrides(byte[] packageBytes)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = XlsxPackageTestFixtures.LoadPackageXml(archive, "[Content_Types].xml");
        var ns = document.Root!.Name.Namespace;
        return document.Root!
            .Elements(ns + "Override")
            .Select(element => element.Attribute("PartName")?.Value ?? "")
            .ToList();
    }

    private static IReadOnlyList<string> ReadWorkbookRelationshipTypes(byte[] packageBytes)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/_rels/workbook.xml.rels");
        var ns = document.Root!.Name.Namespace;
        return document.Root!
            .Elements(ns + "Relationship")
            .Select(element => element.Attribute("Type")?.Value ?? "")
            .ToList();
    }

    private static bool WorkbookRelationshipsContain(byte[] packageBytes, string type, string target)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/_rels/workbook.xml.rels");
        var ns = document.Root!.Name.Namespace;
        return document.Root!
            .Elements(ns + "Relationship")
            .Any(element =>
                string.Equals(element.Attribute("Type")?.Value, type, StringComparison.Ordinal) &&
                string.Equals(element.Attribute("Target")?.Value, target, StringComparison.Ordinal));
    }

    private static bool RootRelationshipsContain(byte[] packageBytes, string type, string target)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = XlsxPackageTestFixtures.LoadPackageXml(archive, "_rels/.rels");
        var ns = document.Root!.Name.Namespace;
        return document.Root!
            .Elements(ns + "Relationship")
            .Any(element =>
                string.Equals(element.Attribute("Type")?.Value, type, StringComparison.Ordinal) &&
                string.Equals(element.Attribute("Target")?.Value, target, StringComparison.Ordinal));
    }

    private static bool PackageRelationshipsContain(byte[] packageBytes, string relationshipsPath, string type, string target)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = XlsxPackageTestFixtures.LoadPackageXml(archive, relationshipsPath);
        var ns = document.Root!.Name.Namespace;
        return document.Root!
            .Elements(ns + "Relationship")
            .Any(element =>
                string.Equals(element.Attribute("Type")?.Value, type, StringComparison.Ordinal) &&
                string.Equals(element.Attribute("Target")?.Value, target, StringComparison.Ordinal));
    }

    private static void AssertWebExtensionTaskpaneReferenceIsBound(byte[] packageBytes)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var relationshipsXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/webextensions/_rels/taskpanes.xml.rels");
        var webExtensionRelationshipIds = relationshipsXml.Root!
            .Elements(relationshipNs + "Relationship")
            .Where(element =>
                (string?)element.Attribute("Type") == "http://schemas.microsoft.com/office/2011/relationships/webextension" &&
                (string?)element.Attribute("Target") == "webextension1.xml")
            .Select(element => element.Attribute("Id")?.Value)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        webExtensionRelationshipIds.Should().ContainSingle();

        XNamespace taskpanesNs = "http://schemas.microsoft.com/office/webextensions/taskpanes/2010/11";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var taskpanesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/webextensions/taskpanes.xml");
        var taskpaneReferenceIds = taskpanesXml.Root!
            .Descendants(taskpanesNs + "webextensionref")
            .Select(element => element.Attribute(relNs + "id")?.Value ?? "")
            .ToList();
        taskpaneReferenceIds
            .Should()
            .BeSubsetOf(webExtensionRelationshipIds);
    }

    private static string? ReadCellText(byte[] packageBytes, string worksheetPath, string reference)
    {
        var cell = ReadCellElement(packageBytes, worksheetPath, reference);
        var ns = cell.Name.Namespace;
        if (string.Equals(cell.Attribute("t")?.Value, "inlineStr", StringComparison.Ordinal))
            return cell.Element(ns + "is")?.Element(ns + "t")?.Value;

        return cell.Element(ns + "v")?.Value;
    }

    private static string? ReadCellFormula(byte[] packageBytes, string worksheetPath, string reference)
    {
        var cell = ReadCellElement(packageBytes, worksheetPath, reference);
        var ns = cell.Name.Namespace;
        return cell.Element(ns + "f")?.Value;
    }

    private static string? ReadCellFormulaAttribute(
        byte[] packageBytes,
        string worksheetPath,
        string reference,
        string attributeName)
    {
        var cell = ReadCellElement(packageBytes, worksheetPath, reference);
        var ns = cell.Name.Namespace;
        return cell.Element(ns + "f")?.Attribute(attributeName)?.Value;
    }

    private static string? ReadCellType(byte[] packageBytes, string worksheetPath, string reference) =>
        ReadCellElement(packageBytes, worksheetPath, reference).Attribute("t")?.Value;

    private static string? ReadCellStyleIndex(byte[] packageBytes, string worksheetPath, string reference) =>
        ReadCellElement(packageBytes, worksheetPath, reference).Attribute("s")?.Value;

    private static string? ReadCellAttribute(byte[] packageBytes, string worksheetPath, string reference, string attributeName) =>
        ReadCellElement(packageBytes, worksheetPath, reference).Attribute(attributeName)?.Value;

    private static string? ReadPrimarySheetViewAttribute(byte[] packageBytes, string worksheetPath, string attributeName)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = LoadPackageXml(archive, worksheetPath);
        var ns = document.Root!.Name.Namespace;
        return document.Root!
            .Element(ns + "sheetViews")
            ?.Elements(ns + "sheetView")
            .FirstOrDefault(view => string.Equals(view.Attribute("workbookViewId")?.Value ?? "0", "0", StringComparison.Ordinal))
            ?.Attribute(attributeName)
            ?.Value;
    }

    private static string? ReadRowAttribute(byte[] packageBytes, string worksheetPath, uint row, string attributeName)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = LoadPackageXml(archive, worksheetPath);
        var ns = document.Root!.Name.Namespace;
        return document
            .Descendants(ns + "row")
            .SingleOrDefault(element => element.Attribute("r")?.Value == row.ToString(System.Globalization.CultureInfo.InvariantCulture))
            ?.Attribute(attributeName)
            ?.Value;
    }

    private static string? ReadColumnAttribute(byte[] packageBytes, string worksheetPath, uint column, string attributeName)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = LoadPackageXml(archive, worksheetPath);
        var ns = document.Root!.Name.Namespace;
        foreach (var element in document.Descendants(ns + "col"))
        {
            if (!uint.TryParse(element.Attribute("min")?.Value, out var min) ||
                !uint.TryParse(element.Attribute("max")?.Value, out var max) ||
                column < min ||
                column > max)
            {
                continue;
            }

            return element.Attribute(attributeName)?.Value;
        }

        return null;
    }

    private static string? ReadMergeCellsAttribute(byte[] packageBytes, string worksheetPath, string attributeName)
    {
        var mergeCells = ReadMergeCellsElement(packageBytes, worksheetPath);
        return mergeCells?.Attribute(attributeName)?.Value;
    }

    private static IReadOnlyList<string> ReadMergeCellReferences(byte[] packageBytes, string worksheetPath)
    {
        var mergeCells = ReadMergeCellsElement(packageBytes, worksheetPath);
        if (mergeCells is null)
            return [];

        var ns = mergeCells.Name.Namespace;
        return mergeCells
            .Elements(ns + "mergeCell")
            .Select(element => element.Attribute("ref")?.Value ?? "")
            .ToList();
    }

    private static string? ReadMergeCellAttribute(
        byte[] packageBytes,
        string worksheetPath,
        string reference,
        string attributeName)
    {
        var mergeCells = ReadMergeCellsElement(packageBytes, worksheetPath);
        if (mergeCells is null)
            return null;

        var ns = mergeCells.Name.Namespace;
        return mergeCells
            .Elements(ns + "mergeCell")
            .SingleOrDefault(element => string.Equals(element.Attribute("ref")?.Value, reference, StringComparison.OrdinalIgnoreCase))
            ?.Attribute(attributeName)
            ?.Value;
    }

    private static XElement? ReadMergeCellsElement(byte[] packageBytes, string worksheetPath)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = LoadPackageXml(archive, worksheetPath);
        var ns = document.Root!.Name.Namespace;
        return document.Root.Element(ns + "mergeCells");
    }

    private static string? ReadHyperlinksAttribute(byte[] packageBytes, string worksheetPath, string attributeName)
    {
        var hyperlinks = ReadHyperlinksElement(packageBytes, worksheetPath);
        return hyperlinks?.Attribute(attributeName)?.Value;
    }

    private static string? ReadHyperlinkAttribute(
        byte[] packageBytes,
        string worksheetPath,
        string reference,
        string attributeName)
    {
        var hyperlinks = ReadHyperlinksElement(packageBytes, worksheetPath);
        if (hyperlinks is null)
            return null;

        var ns = hyperlinks.Name.Namespace;
        return hyperlinks
            .Elements(ns + "hyperlink")
            .SingleOrDefault(element => string.Equals(element.Attribute("ref")?.Value, reference, StringComparison.OrdinalIgnoreCase))
            ?.Attribute(attributeName)
            ?.Value;
    }

    private static XElement? ReadHyperlinksElement(byte[] packageBytes, string worksheetPath)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = LoadPackageXml(archive, worksheetPath);
        var ns = document.Root!.Name.Namespace;
        return document.Root.Element(ns + "hyperlinks");
    }

    private static string? ReadCommentText(byte[] packageBytes, string commentsPath, string reference)
    {
        var comment = ReadCommentElement(packageBytes, commentsPath, reference);
        var ns = comment.Name.Namespace;
        return string.Concat(comment
            .Element(ns + "text")?
            .Descendants(ns + "t")
            .Select(element => element.Value) ?? []);
    }

    private static string? ReadCommentAttribute(
        byte[] packageBytes,
        string commentsPath,
        string reference,
        string attributeName) =>
        ReadCommentElement(packageBytes, commentsPath, reference)
            .Attribute(attributeName)
            ?.Value;

    private static XElement ReadCommentElement(byte[] packageBytes, string commentsPath, string reference)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = LoadPackageXml(archive, commentsPath);
        var ns = document.Root!.Name.Namespace;
        var comment = document
            .Descendants(ns + "comment")
            .SingleOrDefault(element => string.Equals(element.Attribute("ref")?.Value, reference, StringComparison.OrdinalIgnoreCase));
        comment.Should().NotBeNull();
        return comment!;
    }

    private static string? ReadCellTextSpaceMode(byte[] packageBytes, string worksheetPath, string reference)
    {
        var cell = ReadCellElement(packageBytes, worksheetPath, reference);
        var ns = cell.Name.Namespace;
        return cell.Element(ns + "is")?.Element(ns + "t")?.Attribute(XNamespace.Xml + "space")?.Value;
    }

    private static string? ReadWorksheetDimension(byte[] packageBytes, string worksheetPath)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = LoadPackageXml(archive, worksheetPath);
        var ns = document.Root!.Name.Namespace;
        return document.Root.Element(ns + "dimension")?.Attribute("ref")?.Value;
    }

    private static XElement ReadCellElement(byte[] packageBytes, string worksheetPath, string reference)
    {
        var cell = TryReadCellElement(packageBytes, worksheetPath, reference);
        cell.Should().NotBeNull();
        return cell!;
    }

    private static XElement? TryReadCellElement(byte[] packageBytes, string worksheetPath, string reference)
    {
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var document = LoadPackageXml(archive, worksheetPath);
        var ns = document.Root!.Name.Namespace;
        var cell = document
            .Descendants(ns + "c")
            .SingleOrDefault(element => string.Equals(element.Attribute("r")?.Value, reference, StringComparison.Ordinal));

        return cell;
    }
}
