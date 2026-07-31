using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R105: settles the debt left by the R102 sweep (see R102_RenameSheetPreservedPartsSweepTests), which
/// applied the same rename-tolerant-lookup guard DEFENSIVELY to six more preservers
/// (XlsxWorksheetVmlReferencePreserver, XlsxPivotXmlReferencePreserver, XlsxStructuredTableReferencePreserver,
/// XlsxWorksheetPrinterSettingsReferencePreserver, XlsxWorksheetMetadataPreserver, and
/// XlsxUnsupportedSheetReferencePreserver.CreateWorksheetPathRebindings) without a fixture proving any of
/// them actually fires. Each fixture below either proves the guard MATTERS (fails when the R102 change is
/// reverted, passes when restored) or documents why it is INERT (passes both ways because the content
/// survives via a different, already-fixed path).
/// </summary>
public sealed class R105_RenameSweepDefensiveFixPreserverTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    private static byte[] MinimalPngBytes() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];

    private static string GetWorksheetPathForSheetName(ZipArchive archive, string sheetName)
    {
        var workbookXml = XlsxPackageXmlEditor.LoadXml(archive.GetEntry("xl/workbook.xml")!);
        var workbookRels = XlsxRelationshipReader.LoadTargets(archive, "xl/_rels/workbook.xml.rels", "xl/workbook.xml", PackageRelNs);
        return XlsxWorkbookSheetPathReader
            .GetWorkbookSheetPaths(workbookXml, workbookRels, WorksheetNs, RelNs)
            .Single(pair => pair.SheetName == sheetName)
            .WorksheetPath;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // 1) XlsxWorksheetVmlReferencePreserver -- INERT (verdict reached empirically, no test needed here;
    //    see the R105 verdict report). Reverting the R102 fix in BOTH of its branches and re-running
    //    R102_RenameSheetPreservedPartsSweepTests' comment fixtures (legacyDrawing) left all 20 tests
    //    green: XlsxLegacyCommentPreserver (a separate, later-running preserver with its OWN independent,
    //    already-correct rename handling via PreserveReconciledVmlDrawing) fully re-establishes the
    //    legacyDrawing marker regardless of this preserver. A from-scratch header/footer-picture fixture
    //    (legacyDrawingHF, a non-comment VML marker) was also tried and ALSO passed reverted: renaming a
    //    sheet makes XlsxHeaderFooterPicturePackageWriter.FindSheetsWithUnchangedSourcePictures fail its
    //    own (separately buggy, out-of-scope) name-keyed "unchanged" lookup against the SOURCE package's
    //    load-time name, so the sheet is always treated as "changed" and the writer regenerates the
    //    marker+VML fresh from the model BEFORE this preserver ever runs. Both branches are therefore
    //    dead on arrival for every case actually reachable through RenameSheetCommand.
    // ─────────────────────────────────────────────────────────────────────────────────────────
    // 2) XlsxWorksheetPrinterSettingsReferencePreserver -- INERT, confirmed with a STRONGER assertion
    //    than the R102 sweep's own fixture. That fixture (RenameSheet_KeepsPrinterSettings_SingleSheetBook)
    //    only asserts the pageSetup r:id attribute is non-null, which is inertness-blind (a dangling,
    //    unresolvable r:id copied verbatim would also satisfy it). The rigorous version below additionally
    //    resolves that r:id against the renamed sheet's own .rels part and confirms the printerSettings
    //    part it points at exists -- and it ALSO passes with the R102 fix fully reverted. Root cause: a
    //    plain rename never changes a sheet's own worksheetN.xml part PATH (an established invariant used
    //    throughout this file), and XlsxPackageMetadataMerger.MergeRelationshipParts merges every
    //    worksheet's .rels file keyed by that PATH (not by sheet name) BEFORE this preserver ever runs --
    //    so the printerSettings relationship (and the pageSetup r:id string naming it, separately carried
    //    forward verbatim by XlsxWorksheetMetadataPreserver, which excludes "r:id" from
    //    ModeledPageSetupAttributes) already arrives intact and mutually consistent through a mechanism
    //    that was never vulnerable to the name-keyed rename bug in the first place.
    // ─────────────────────────────────────────────────────────────────────────────────────────

    private static void AddPrinterSettings(MemoryStream packageStream, string worksheetPath)
    {
        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);

        var worksheetXml = XlsxPackageXmlEditor.LoadXml(archive.GetEntry(worksheetPath)!);
        var root = worksheetXml.Root!;
        root.SetAttributeValue(XNamespace.Xmlns + "r", RelNs.NamespaceName);
        var pageSetup = root.Element(WorksheetNs + "pageSetup");
        if (pageSetup is null)
        {
            pageSetup = new XElement(WorksheetNs + "pageSetup");
            root.Add(pageSetup);
        }

        pageSetup.SetAttributeValue(RelNs + "id", "rIdFreeXPrinterSettings");
        XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);

        var worksheetRelsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
        var worksheetRelsEntry = archive.GetEntry(worksheetRelsPath);
        var worksheetRelsXml = worksheetRelsEntry is not null
            ? XlsxPackageXmlEditor.LoadXml(worksheetRelsEntry)
            : new XDocument(new XElement(PackageRelNs + "Relationships"));
        worksheetRelsXml.Root!.Add(new XElement(
            PackageRelNs + "Relationship",
            new XAttribute("Id", "rIdFreeXPrinterSettings"),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/printerSettings"),
            new XAttribute("Target", "../printerSettings/printerSettings1.bin")));
        XlsxPackageXmlEditor.ReplaceXml(archive, worksheetRelsPath, worksheetRelsXml);

        var printerSettingsEntry = archive.CreateEntry("xl/printerSettings/printerSettings1.bin", CompressionLevel.Optimal);
        using (var writer = new BinaryWriter(printerSettingsEntry.Open()))
            writer.Write(new byte[] { 0x46, 0x58, 0x50, 0x52, 0x4E });

        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var contentTypesXml = XlsxPackageXmlEditor.LoadXml(archive.GetEntry("[Content_Types].xml")!);
        contentTypesXml.Root!.Add(new XElement(
            contentTypeNs + "Override",
            new XAttribute("PartName", "/xl/printerSettings/printerSettings1.bin"),
            new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.printerSettings")));
        XlsxPackageXmlEditor.ReplaceXml(archive, "[Content_Types].xml", contentTypesXml);

        packageStream.Position = 0;
    }

    [Fact]
    public void RenameSheet_KeepsPrinterSettings_RelationshipActuallyResolves()
    {
        var workbook = new Workbook("RenamePrinterSettingsResolved");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("data"));

        using var source = XlsxPackageTestHelper.SaveWorkbook(workbook);
        string worksheetPath;
        source.Position = 0;
        using (var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true))
            worksheetPath = GetWorksheetPathForSheetName(archive, "Data");

        AddPrinterSettings(source, worksheetPath);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheet("Data")!;
        var ctx = new TestCommandContext(loaded);

        new RenameSheetCommand(loadedSheet.Id, "DataRenamed").Apply(ctx).Success.Should().BeTrue();

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var renamedPath = GetWorksheetPathForSheetName(savedArchive, "DataRenamed");
        var worksheetXml = XlsxPackageXmlEditor.LoadXml(savedArchive.GetEntry(renamedPath)!);
        var pageSetup = worksheetXml.Root!.Element(WorksheetNs + "pageSetup");
        pageSetup.Should().NotBeNull();
        var relId = pageSetup!.Attribute(RelNs + "id")?.Value;
        relId.Should().NotBeNullOrWhiteSpace("the renamed sheet's pageSetup->printerSettings relationship id must survive a plain rename");

        var relsPath = XlsxPackagePath.GetRelationshipPartPath(renamedPath);
        var relsEntry = savedArchive.GetEntry(relsPath);
        relsEntry.Should().NotBeNull("the renamed sheet must carry its own relationships part");
        var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry!);
        var relationship = relsXml.Root!.Elements(PackageRelNs + "Relationship")
            .SingleOrDefault(r => (string?)r.Attribute("Id") == relId);
        relationship.Should().NotBeNull(
            "the pageSetup r:id must resolve to a REAL relationship in the renamed sheet's own .rels part " +
            "(not merely be a non-null attribute copied verbatim from source, which is dangling if so)");
        var printerSettingsTarget = XlsxPackagePath.ResolveRelationshipTarget(renamedPath, relationship!.Attribute("Target")!.Value);
        savedArchive.GetEntry(printerSettingsTarget).Should().NotBeNull("the printerSettings part itself must survive");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // 3) XlsxWorksheetMetadataPreserver main preserve loop -- MATTERS: <webPublishItems>, a block that
    //    is in GetRetainedWorksheetChildNames' generic raw-carry-forward list and has NO FreeX model
    //    representation at all (FreeX has no web-publish feature), so it can ONLY survive via this
    //    preserver's generic fallback path (CreateReboundRetainedWorksheetBlock), never via a native
    //    writer regenerating it from the model.
    // ─────────────────────────────────────────────────────────────────────────────────────────

    private static void AddWebPublishItems(MemoryStream packageStream, string worksheetPath)
    {
        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);

        var worksheetXml = XlsxPackageXmlEditor.LoadXml(archive.GetEntry(worksheetPath)!);
        var root = worksheetXml.Root!;
        root.Add(XElement.Parse(
            """
            <webPublishItems xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="1">
              <webPublishItem id="1" divId="Data_9999" sourceType="sheet" destinationFile="page.htm" title="Data" autoUpdate="1"/>
            </webPublishItems>
            """));
        XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        packageStream.Position = 0;
    }

    [Fact]
    public void RenameSheet_KeepsWebPublishItems_SingleSheetBook()
    {
        var workbook = new Workbook("RenameWebPublishItems");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("data"));

        using var source = XlsxPackageTestHelper.SaveWorkbook(workbook);
        string worksheetPath;
        source.Position = 0;
        using (var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true))
            worksheetPath = GetWorksheetPathForSheetName(archive, "Data");

        AddWebPublishItems(source, worksheetPath);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheet("Data")!;
        var ctx = new TestCommandContext(loaded);

        new RenameSheetCommand(loadedSheet.Id, "DataRenamed").Apply(ctx).Success.Should().BeTrue();

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var renamedPath = GetWorksheetPathForSheetName(savedArchive, "DataRenamed");
        var worksheetXml = XlsxPackageXmlEditor.LoadXml(savedArchive.GetEntry(renamedPath)!);
        worksheetXml.Root!.Element(WorksheetNs + "webPublishItems").Should().NotBeNull(
            "the renamed sheet's <webPublishItems> (a block FreeX never models) must survive a plain rename");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // 4) XlsxWorksheetMetadataPreserver's RebindWorksheetCustomPropertyRelationships (context overload)
    //    -- MATTERS: a worksheet <customProperties>/<customPr> relationship pointing at an
    //    xl/customProperty/customPropertyN.bin part. FreeX has no model concept of worksheet custom
    //    properties at all, so both the <customPr> element AND its relationship id must come from
    //    preservation; this exercises the rel-rebind specifically (not the element-merge, which is a
    //    separate call already covered by the generic MergeWorksheetCustomProperties path).
    // ─────────────────────────────────────────────────────────────────────────────────────────

    private static void AddWorksheetCustomProperty(MemoryStream packageStream, string worksheetPath)
    {
        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);

        var worksheetXml = XlsxPackageXmlEditor.LoadXml(archive.GetEntry(worksheetPath)!);
        var root = worksheetXml.Root!;
        root.SetAttributeValue(XNamespace.Xmlns + "r", RelNs.NamespaceName);
        root.Add(XElement.Parse(
            """
            <customProperties xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                               xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <customPr name="FreeXCustomProp" r:id="rIdCustomProp"/>
            </customProperties>
            """));
        XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);

        var worksheetRelsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
        var worksheetRelsEntry = archive.GetEntry(worksheetRelsPath);
        var worksheetRelsXml = worksheetRelsEntry is not null
            ? XlsxPackageXmlEditor.LoadXml(worksheetRelsEntry)
            : new XDocument(new XElement(PackageRelNs + "Relationships"));
        worksheetRelsXml.Root!.Add(new XElement(
            PackageRelNs + "Relationship",
            new XAttribute("Id", "rIdCustomProp"),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customProperty"),
            new XAttribute("Target", "../customProperty/customProperty1.bin")));
        XlsxPackageXmlEditor.ReplaceXml(archive, worksheetRelsPath, worksheetRelsXml);

        var customPropEntry = archive.CreateEntry("xl/customProperty/customProperty1.bin", CompressionLevel.Optimal);
        using (var writer = new BinaryWriter(customPropEntry.Open()))
            writer.Write(new byte[] { 0x01, 0x02, 0x03, 0x04 });

        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var contentTypesXml = XlsxPackageXmlEditor.LoadXml(archive.GetEntry("[Content_Types].xml")!);
        contentTypesXml.Root!.Add(new XElement(
            contentTypeNs + "Override",
            new XAttribute("PartName", "/xl/customProperty/customProperty1.bin"),
            new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.customProperty")));
        XlsxPackageXmlEditor.ReplaceXml(archive, "[Content_Types].xml", contentTypesXml);

        packageStream.Position = 0;
    }

    [Fact]
    public void RenameSheet_KeepsWorksheetCustomPropertyRelationship_SingleSheetBook()
    {
        var workbook = new Workbook("RenameWorksheetCustomProperty");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("data"));

        using var source = XlsxPackageTestHelper.SaveWorkbook(workbook);
        string worksheetPath;
        source.Position = 0;
        using (var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true))
            worksheetPath = GetWorksheetPathForSheetName(archive, "Data");

        AddWorksheetCustomProperty(source, worksheetPath);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheet("Data")!;
        var ctx = new TestCommandContext(loaded);

        new RenameSheetCommand(loadedSheet.Id, "DataRenamed").Apply(ctx).Success.Should().BeTrue();

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var renamedPath = GetWorksheetPathForSheetName(savedArchive, "DataRenamed");
        var worksheetXml = XlsxPackageXmlEditor.LoadXml(savedArchive.GetEntry(renamedPath)!);
        var customPr = worksheetXml.Root!.Element(WorksheetNs + "customProperties")?.Element(WorksheetNs + "customPr");
        customPr.Should().NotBeNull("the renamed sheet's <customPr> element must survive a plain rename");
        var relId = customPr!.Attribute(RelNs + "id")?.Value;
        relId.Should().NotBeNullOrWhiteSpace();

        var relsPath = XlsxPackagePath.GetRelationshipPartPath(renamedPath);
        var relsXml = XlsxPackageXmlEditor.LoadXml(savedArchive.GetEntry(relsPath)!);
        var relationship = relsXml.Root!.Elements(PackageRelNs + "Relationship")
            .SingleOrDefault(r => (string?)r.Attribute("Id") == relId);
        relationship.Should().NotBeNull("the custom property's r:id must resolve to a real relationship on the renamed sheet");
        var target = XlsxPackagePath.ResolveRelationshipTarget(renamedPath, relationship!.Attribute("Target")!.Value);
        savedArchive.GetEntry(target).Should().NotBeNull("the xl/customProperty/*.bin part itself must survive");
    }
}
