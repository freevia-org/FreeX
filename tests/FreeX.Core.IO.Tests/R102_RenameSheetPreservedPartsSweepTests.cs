using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R102-io-rename-worksheet-exclusion-sweep-1: sweep of the SAME bug class fixed under
/// R102-io-rename-worksheet-exclusion-1 (see R102_RenameSheetPreservedPartsTests) across every other
/// per-sheet source-package preserver that matched a source sheet against
/// XlsxSourcePackagePreservationContext.TargetSheets by raw (load-time) NAME: a plain rename makes that
/// lookup fail exactly like a genuine delete, silently dropping whatever the sheet's own preserver
/// carries forward. Each fixture below builds a feature via the normal model API (or, where FreeX has NO
/// in-model representation at all, a raw post-save package patch mirroring the existing convention in
/// XlsxFormControlLoadRoundTripTests / XlsxBroaderRetentionChecksTests), loads it (so the feature becomes
/// "source-loaded"), renames a sheet, forces the full-rebuild save path, and asserts the feature survives
/// on the renamed sheet.
/// </summary>
public sealed class R102_RenameSheetPreservedPartsSweepTests
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
    // 1) XlsxWorksheetDrawingPartMerger / XlsxWorksheetDrawingReferencePreserver -- a worksheet picture.
    // ─────────────────────────────────────────────────────────────────────────────────────────

    private static Workbook BuildPictureWorkbook(out Sheet sheet)
    {
        var workbook = new Workbook("RenameDrawing");
        sheet = workbook.AddSheet("Picture");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("data"));
        sheet.Pictures.Add(new PictureModel
        {
            Name = "Pic1",
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = PictureKind.Image,
            ImageBytes = MinimalPngBytes(),
            ContentType = "image/png",
            Width = 96,
            Height = 64
        });
        return workbook;
    }

    [Fact]
    public void RenameSheet_KeepsPicture_SingleSheetBook()
    {
        var workbook = BuildPictureWorkbook(out var sheet);
        using var source = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheet("Picture")!;
        var ctx = new TestCommandContext(loaded);

        new RenameSheetCommand(loadedSheet.Id, "PictureRenamed").Apply(ctx).Success.Should().BeTrue();

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var renamedPath = GetWorksheetPathForSheetName(savedArchive, "PictureRenamed");
        var worksheetXml = XlsxPackageXmlEditor.LoadXml(savedArchive.GetEntry(renamedPath)!);
        worksheetXml.Root!.Element(WorksheetNs + "drawing").Should().NotBeNull(
            "the renamed sheet's <drawing> element must survive a plain rename, not be dropped as if the sheet had been deleted");
        savedArchive.Entries.Should().Contain(e => e.FullName.StartsWith("xl/drawings/drawing", StringComparison.OrdinalIgnoreCase),
            "the drawing part itself must survive");
    }

    [Fact]
    public void RenameSheet_KeepsPicture_AmongMultipleSheets()
    {
        var workbook = BuildPictureWorkbook(out var pictureSheet);
        var report = workbook.AddSheet("Report");
        report.SetCell(new CellAddress(report.Id, 1, 1), new TextValue("report"));

        using var source = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var loadedPictureSheet = loaded.GetSheet("Picture")!;
        var ctx = new TestCommandContext(loaded);

        new RenameSheetCommand(loaded.GetSheet("Report")!.Id, "ReportRenamed").Apply(ctx).Success.Should().BeTrue();
        new RenameSheetCommand(loadedPictureSheet.Id, "PictureRenamed").Apply(ctx).Success.Should().BeTrue();

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var renamedPath = GetWorksheetPathForSheetName(savedArchive, "PictureRenamed");
        var worksheetXml = XlsxPackageXmlEditor.LoadXml(savedArchive.GetEntry(renamedPath)!);
        worksheetXml.Root!.Element(WorksheetNs + "drawing").Should().NotBeNull(
            "the picture sheet's drawing must survive when renamed alongside another renamed sheet");
    }

    [Fact]
    public void RenameSheet_ThenRenameBack_KeepsPicture()
    {
        var workbook = BuildPictureWorkbook(out var sheet);
        using var source = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheet("Picture")!;
        var ctx = new TestCommandContext(loaded);

        new RenameSheetCommand(loadedSheet.Id, "Temp").Apply(ctx).Success.Should().BeTrue();
        new RenameSheetCommand(loadedSheet.Id, "Picture").Apply(ctx).Success.Should().BeTrue();

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var finalPath = GetWorksheetPathForSheetName(savedArchive, "Picture");
        var worksheetXml = XlsxPackageXmlEditor.LoadXml(savedArchive.GetEntry(finalPath)!);
        worksheetXml.Root!.Element(WorksheetNs + "drawing").Should().NotBeNull(
            "renaming away and back to the original name must not drop the drawing");
    }

    // R102-io-rename-worksheet-exclusion-sweep-1-falsepositive regression: deleting a sheet BETWEEN two
    // others that carry no drawing shifts the trailing sheet's worksheet part down to the deleted
    // sheet's old path (ClosedXML renumbers sequentially). A naive path-only rename-fallback would
    // misread that coincidence as "the deleted sheet's drawing was renamed onto the trailing sheet" and
    // resurrect it there. Caught for real by
    // FileAdapterSmokeTests.XlsxAdapter_LoadedWorkbookSave_DoesNotResurrectDeletedSheetUnsupportedWorksheetArtifacts;
    // this is the same collision reproduced against the drawing preserver specifically.
    [Fact]
    public void DeleteMiddleSheet_DoesNotResurrectItsPictureOntoRenumberedTrailingSheet()
    {
        var workbook = new Workbook("DeleteMiddleSheetNoResurrection");
        var first = workbook.AddSheet("First");
        first.SetCell(new CellAddress(first.Id, 1, 1), new TextValue("first"));
        var middle = workbook.AddSheet("Middle");
        middle.SetCell(new CellAddress(middle.Id, 1, 1), new TextValue("middle"));
        middle.Pictures.Add(new PictureModel
        {
            Name = "MiddlePic",
            Anchor = new CellAddress(middle.Id, 2, 2),
            Kind = PictureKind.Image,
            ImageBytes = MinimalPngBytes(),
            ContentType = "image/png",
            Width = 96,
            Height = 64
        });
        var trailing = workbook.AddSheet("Trailing");
        trailing.SetCell(new CellAddress(trailing.Id, 1, 1), new TextValue("trailing"));

        using var source = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var middleId = loaded.GetSheet("Middle")!.Id;
        loaded.RemoveSheet(middleId);

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var trailingPath = GetWorksheetPathForSheetName(savedArchive, "Trailing");
        var worksheetXml = XlsxPackageXmlEditor.LoadXml(savedArchive.GetEntry(trailingPath)!);
        worksheetXml.Root!.Element(WorksheetNs + "drawing").Should().BeNull(
            "the deleted Middle sheet's picture must not be resurrected onto Trailing just because " +
            "renumbering happened to shift Trailing onto Middle's old worksheet path");
    }

    [Fact]
    public void NoRename_KeepsPicture_OnFullRebuildSave()
    {
        var workbook = BuildPictureWorkbook(out var sheet);
        using var source = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);

        var tempSheet = loaded.AddSheet("__TempForFullSave__");
        loaded.RemoveSheet(tempSheet.Id);
        var loadedSheet = loaded.GetSheet("Picture")!;
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 5, 5), new TextValue("edited"));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var path = GetWorksheetPathForSheetName(savedArchive, "Picture");
        var worksheetXml = XlsxPackageXmlEditor.LoadXml(savedArchive.GetEntry(path)!);
        worksheetXml.Root!.Element(WorksheetNs + "drawing").Should().NotBeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // 2) XlsxWorksheetVmlReferencePreserver -- a cell comment's legacyDrawing marker + VML shape.
    // ─────────────────────────────────────────────────────────────────────────────────────────

    private static Workbook BuildCommentWorkbook(out Sheet sheet)
    {
        var workbook = new Workbook("RenameComment");
        sheet = workbook.AddSheet("Commented");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("data"));
        sheet.Comments[new CellAddress(sheet.Id, 1, 1)] = "A comment";
        return workbook;
    }

    [Fact]
    public void RenameSheet_KeepsCommentLegacyDrawing_SingleSheetBook()
    {
        var workbook = BuildCommentWorkbook(out var sheet);
        using var source = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheet("Commented")!;
        var ctx = new TestCommandContext(loaded);

        new RenameSheetCommand(loadedSheet.Id, "CommentedRenamed").Apply(ctx).Success.Should().BeTrue();

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var renamedPath = GetWorksheetPathForSheetName(savedArchive, "CommentedRenamed");
        var worksheetXml = XlsxPackageXmlEditor.LoadXml(savedArchive.GetEntry(renamedPath)!);
        worksheetXml.Root!.Element(WorksheetNs + "legacyDrawing").Should().NotBeNull(
            "the renamed sheet's <legacyDrawing> marker (the comment's VML shape binding) must survive a plain rename");
        savedArchive.Entries.Should().Contain(e => e.FullName.StartsWith("xl/drawings/vmlDrawing", StringComparison.OrdinalIgnoreCase),
            "the VML shape part itself must survive");
    }

    [Fact]
    public void RenameSheet_KeepsCommentLegacyDrawing_AmongMultipleSheets()
    {
        var workbook = BuildCommentWorkbook(out var commentSheet);
        var trailer = workbook.AddSheet("Trailer");
        trailer.SetCell(new CellAddress(trailer.Id, 1, 1), new TextValue("trailer"));

        using var source = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var loadedCommentSheet = loaded.GetSheet("Commented")!;
        var ctx = new TestCommandContext(loaded);

        new RenameSheetCommand(loaded.GetSheet("Trailer")!.Id, "TrailerRenamed").Apply(ctx).Success.Should().BeTrue();
        new RenameSheetCommand(loadedCommentSheet.Id, "CommentedRenamed").Apply(ctx).Success.Should().BeTrue();

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var renamedPath = GetWorksheetPathForSheetName(savedArchive, "CommentedRenamed");
        var worksheetXml = XlsxPackageXmlEditor.LoadXml(savedArchive.GetEntry(renamedPath)!);
        worksheetXml.Root!.Element(WorksheetNs + "legacyDrawing").Should().NotBeNull();
    }

    // R103-io-rename-name-reuse-identity-gap regression: an UNRENAMED sheet whose worksheet part gets
    // renumbered purely because an unrelated EARLIER sheet was deleted (no rename involved for Trailing
    // itself) must still resolve its OWN preserved unmodeled parts onto its own (renumbered) worksheet.
    // A naive "trust the direct name match only when its resolved path is byte-identical to the sheet's
    // OWN load-time path" fix would incorrectly reject this case too (Trailing's path legitimately moved
    // from worksheet3.xml to worksheet2.xml), silently dropping Trailing's own control -- this guards
    // against that alternate, over-broad fix regressing this legitimate case.
    [Fact]
    public void UnrenamedTrailingSheet_KeepsOwnFormControl_AfterUnrelatedMiddleSheetDeleted()
    {
        var workbook = new Workbook("RenumberOwnControlNoRename");
        var first = workbook.AddSheet("First");
        first.SetCell(new CellAddress(first.Id, 1, 1), new TextValue("first"));
        var middle = workbook.AddSheet("Middle");
        middle.SetCell(new CellAddress(middle.Id, 1, 1), new TextValue("middle"));
        var trailing = workbook.AddSheet("Trailing");
        trailing.SetCell(new CellAddress(trailing.Id, 4, 9), new BoolValue(true));

        using var source = XlsxPackageTestHelper.SaveWorkbook(workbook);
        string trailingSourcePath;
        source.Position = 0;
        using (var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true))
            trailingSourcePath = GetWorksheetPathForSheetName(archive, "Trailing");

        AddCheckBoxControl(source, trailingSourcePath);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        loaded.GetSheet("Trailing")!.FormControls.Should().ContainSingle("sanity: control must load");
        var middleId = loaded.GetSheet("Middle")!.Id;
        loaded.RemoveSheet(middleId);

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var trailingPath = GetWorksheetPathForSheetName(savedArchive, "Trailing");
        trailingPath.Should().Be("xl/worksheets/sheet2.xml",
            "sanity: Trailing must actually have been renumbered down from its original slot for this test to be meaningful");
        var worksheetXml = XlsxPackageXmlEditor.LoadXml(savedArchive.GetEntry(trailingPath)!);
        worksheetXml.Descendants(WorksheetNs + "control").Should().NotBeEmpty(
            "Trailing's OWN unmodeled control must survive when it keeps its name but gets renumbered " +
            "due to Middle's unrelated deletion elsewhere in the workbook");
    }

    [Fact]
    public void NoRename_KeepsCommentLegacyDrawing_OnFullRebuildSave()
    {
        var workbook = BuildCommentWorkbook(out var sheet);
        using var source = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var tempSheet = loaded.AddSheet("__TempForFullSave__");
        loaded.RemoveSheet(tempSheet.Id);
        var loadedSheet = loaded.GetSheet("Commented")!;
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 5, 5), new TextValue("edited"));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var path = GetWorksheetPathForSheetName(savedArchive, "Commented");
        var worksheetXml = XlsxPackageXmlEditor.LoadXml(savedArchive.GetEntry(path)!);
        worksheetXml.Root!.Element(WorksheetNs + "legacyDrawing").Should().NotBeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // 3) XlsxPivotXmlReferencePreserver -- a pivotTableDefinition on the renamed sheet.
    // ─────────────────────────────────────────────────────────────────────────────────────────

    private static Workbook BuildPivotWorkbook(out Sheet sheet)
    {
        var workbook = new Workbook("RenamePivot");
        sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Data",
            SourceReference = "A1:B3"
        };
        cache.Fields.Add(new PivotCacheFieldModel("Region", ContainsString: true, SharedItems: ["East", "West"]));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 8, 2))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        return workbook;
    }

    [Fact]
    public void RenameSheet_KeepsPivotTableDefinition_SingleSheetBook()
    {
        var workbook = BuildPivotWorkbook(out var sheet);
        using var source = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, source);
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
        AssertPivotTableSurvivesOnWorksheet(savedArchive, renamedPath);
    }

    [Fact]
    public void RenameSheet_KeepsPivotTableDefinition_AmongMultipleSheets()
    {
        var workbook = BuildPivotWorkbook(out var dataSheet);
        var trailer = workbook.AddSheet("Trailer");
        trailer.SetCell(new CellAddress(trailer.Id, 1, 1), new TextValue("trailer"));

        using var source = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var loadedDataSheet = loaded.GetSheet("Data")!;
        var ctx = new TestCommandContext(loaded);

        new RenameSheetCommand(loaded.GetSheet("Trailer")!.Id, "TrailerRenamed").Apply(ctx).Success.Should().BeTrue();
        new RenameSheetCommand(loadedDataSheet.Id, "DataRenamed").Apply(ctx).Success.Should().BeTrue();

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var renamedPath = GetWorksheetPathForSheetName(savedArchive, "DataRenamed");
        AssertPivotTableSurvivesOnWorksheet(savedArchive, renamedPath);
    }

    [Fact]
    public void NoRename_KeepsPivotTableDefinition_OnFullRebuildSave()
    {
        var workbook = BuildPivotWorkbook(out var sheet);
        using var source = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var tempSheet = loaded.AddSheet("__TempForFullSave__");
        loaded.RemoveSheet(tempSheet.Id);
        var loadedSheet = loaded.GetSheet("Data")!;
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 10, 10), new TextValue("edited"));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var path = GetWorksheetPathForSheetName(savedArchive, "Data");
        AssertPivotTableSurvivesOnWorksheet(savedArchive, path);
    }

    // A pivot table is linked to its worksheet purely via a worksheet-rels relationship of type
    // ".../pivotTable" pointing at its own xl/pivotTables/pivotTableN.xml part (see
    // XlsxPivotTableWriter.cs:233-236 -- CT_Worksheet has no element referencing a
    // pivotTableDefinition at all). FreeX's native pivot writer regenerates this relationship AND the
    // part fresh from the model on every full save, so a MODELED pivot table's rename-survival is
    // actually carried by the already-fixed GetExcludedWorksheetPackagePartPaths (worksheet-rels is
    // part of the worksheet's own preserved-or-excluded part set) rather than by
    // XlsxPivotXmlReferencePreserver.PreserveWorksheetPivotTableDefinitions (whose own gate -- a raw
    // <pivotTableDefinition> embedded as a worksheet CHILD element -- is never produced by the current
    // writer for any pivot table FreeX can fully model; see the R102 sweep report for detail).
    private static void AssertPivotTableSurvivesOnWorksheet(ZipArchive archive, string worksheetPath)
    {
        var relsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
        var relsEntry = archive.GetEntry(relsPath);
        relsEntry.Should().NotBeNull("the renamed sheet must still carry its own relationships part");
        var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry!);
        var pivotRelationship = relsXml.Root!
            .Elements(PackageRelNs + "Relationship")
            .SingleOrDefault(r => (string?)r.Attribute("Type") ==
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotTable");
        pivotRelationship.Should().NotBeNull(
            "the renamed sheet's worksheet -> pivotTable relationship must survive a plain rename");
        var pivotTarget = XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, pivotRelationship!.Attribute("Target")!.Value);
        archive.GetEntry(pivotTarget).Should().NotBeNull("the pivotTableDefinition part itself must survive");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // 4) XlsxStructuredTableReferencePreserver -- a tablePart on the renamed sheet.
    // ─────────────────────────────────────────────────────────────────────────────────────────

    private static Workbook BuildStructuredTableWorkbook(out Sheet sheet)
    {
        var workbook = new Workbook("RenameTable");
        sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "DataTable",
            DisplayName = "DataTable",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)),
            HasAutoFilter = true,
            StyleName = "TableStyleMedium2",
            PackagePart = "xl/tables/table1.xml"
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Category"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Amount"));
        sheet.StructuredTables.Add(table);

        return workbook;
    }

    [Fact]
    public void RenameSheet_KeepsStructuredTable_SingleSheetBook()
    {
        var workbook = BuildStructuredTableWorkbook(out var sheet);
        using var source = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, source);
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
        worksheetXml.Root!.Element(WorksheetNs + "tableParts").Should().NotBeNull(
            "the renamed sheet's <tableParts> must survive a plain rename");
        savedArchive.GetEntry("xl/tables/table1.xml").Should().NotBeNull();
    }

    [Fact]
    public void NoRename_KeepsStructuredTable_OnFullRebuildSave()
    {
        var workbook = BuildStructuredTableWorkbook(out var sheet);
        using var source = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var tempSheet = loaded.AddSheet("__TempForFullSave__");
        loaded.RemoveSheet(tempSheet.Id);
        var loadedSheet = loaded.GetSheet("Data")!;
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 10, 10), new TextValue("edited"));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var path = GetWorksheetPathForSheetName(savedArchive, "Data");
        var worksheetXml = XlsxPackageXmlEditor.LoadXml(savedArchive.GetEntry(path)!);
        worksheetXml.Root!.Element(WorksheetNs + "tableParts").Should().NotBeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // 5) XlsxWorksheetPrinterSettingsReferencePreserver -- a printerSettings binding (unmodeled part).
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
    public void RenameSheet_KeepsPrinterSettings_SingleSheetBook()
    {
        var workbook = new Workbook("RenamePrinterSettings");
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
        pageSetup!.Attribute(RelNs + "id").Should().NotBeNull(
            "the renamed sheet's pageSetup->printerSettings relationship id must survive a plain rename");
        savedArchive.GetEntry("xl/printerSettings/printerSettings1.bin").Should().NotBeNull();
    }

    [Fact]
    public void NoRename_KeepsPrinterSettings_OnFullRebuildSave()
    {
        var workbook = new Workbook("NoRenamePrinterSettings");
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
        var tempSheet = loaded.AddSheet("__TempForFullSave__");
        loaded.RemoveSheet(tempSheet.Id);
        loaded.GetSheet("Data")!.SetCell(new CellAddress(loaded.GetSheet("Data")!.Id, 2, 1), new TextValue("edited"));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        savedArchive.GetEntry("xl/printerSettings/printerSettings1.bin").Should().NotBeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // 6) XlsxWorksheetFormControlPreserver -- a legacy CheckBox form control (unmodeled controls block).
    // ─────────────────────────────────────────────────────────────────────────────────────────

    private static void ReplaceEntry(ZipArchive archive, string path, XDocument xml)
    {
        archive.GetEntry(path)?.Delete();
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        xml.Save(stream, SaveOptions.DisableFormatting);
    }

    private static void ReplaceRawEntry(ZipArchive archive, string path, string content)
    {
        archive.GetEntry(path)?.Delete();
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static void AddCheckBoxControl(MemoryStream packageStream, string worksheetPath)
    {
        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);

        var worksheetXml = XlsxPackageXmlEditor.LoadXml(archive.GetEntry(worksheetPath)!);
        var root = worksheetXml.Root!;
        root.SetAttributeValue(XNamespace.Xmlns + "r", RelNs.NamespaceName);
        root.Add(XElement.Parse(
            """
            <legacyDrawing xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                           xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                           r:id="rIdVml"/>
            """));
        root.Add(XElement.Parse(
            """
            <mc:AlternateContent xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                                 xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                                 xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                                 xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing">
              <mc:Choice Requires="x14">
                <controls>
                  <mc:AlternateContent xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006">
                    <mc:Choice Requires="x14">
                      <control shapeId="1025" r:id="rIdCtrl" name="Check Box 1">
                        <controlPr defaultSize="0" autoFill="0" autoLine="0" autoPict="0">
                          <anchor>
                            <from><xdr:col>1</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff></from>
                            <to><xdr:col>3</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>3</xdr:row><xdr:rowOff>0</xdr:rowOff></to>
                          </anchor>
                        </controlPr>
                      </control>
                    </mc:Choice>
                  </mc:AlternateContent>
                </controls>
              </mc:Choice>
            </mc:AlternateContent>
            """));
        ReplaceEntry(archive, worksheetPath, worksheetXml);

        XNamespace fcNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
        var ctrlPropXml = new XDocument(new XElement(fcNs + "formControlPr",
            new XAttribute("objectType", "CheckBox"),
            new XAttribute("checked", "Checked"),
            new XAttribute("lockText", "1"),
            new XAttribute("noThreeD", "1")));
        ReplaceEntry(archive, "xl/ctrlProps/ctrlProp1.xml", ctrlPropXml);

        var vml =
            "<xml xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:x=\"urn:schemas-microsoft-com:office:excel\">" +
            "<v:shape id=\"CheckBox1\" type=\"#_x0000_t201\"><x:ClientData ObjectType=\"Checkbox\">" +
            "<x:Anchor>1,0,1,0,3,0,3,0</x:Anchor><x:Checked>1</x:Checked>" +
            "</x:ClientData></v:shape></xml>";
        ReplaceRawEntry(archive, "xl/drawings/vmlDrawing1.vml", vml);

        var relsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
        var relsXml = archive.GetEntry(relsPath) is { } relsEntry
            ? XlsxPackageXmlEditor.LoadXml(relsEntry)
            : new XDocument(new XElement(PackageRelNs + "Relationships"));
        relsXml.Root!.Add(
            new XElement(PackageRelNs + "Relationship",
                new XAttribute("Id", "rIdVml"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing"),
                new XAttribute("Target", "../drawings/vmlDrawing1.vml")),
            new XElement(PackageRelNs + "Relationship",
                new XAttribute("Id", "rIdCtrl"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/ctrlProp"),
                new XAttribute("Target", "../ctrlProps/ctrlProp1.xml")));
        ReplaceEntry(archive, relsPath, relsXml);

        XNamespace ctNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var contentTypesXml = XlsxPackageXmlEditor.LoadXml(archive.GetEntry("[Content_Types].xml")!);
        var ctRoot = contentTypesXml.Root!;
        if (!ctRoot.Elements(ctNs + "Default").Any(d => (string?)d.Attribute("Extension") == "vml"))
        {
            ctRoot.Add(new XElement(ctNs + "Default",
                new XAttribute("Extension", "vml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.vmlDrawing")));
        }

        ctRoot.Add(new XElement(ctNs + "Override",
            new XAttribute("PartName", "/xl/ctrlProps/ctrlProp1.xml"),
            new XAttribute("ContentType", "application/vnd.ms-excel.controlproperties+xml")));
        ReplaceEntry(archive, "[Content_Types].xml", contentTypesXml);

        packageStream.Position = 0;
    }

    [Fact]
    public void RenameSheet_KeepsFormControl_SingleSheetBook()
    {
        var workbook = new Workbook("RenameFormControl");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 4, 9), new BoolValue(true));

        using var source = XlsxPackageTestHelper.SaveWorkbook(workbook);
        string worksheetPath;
        source.Position = 0;
        using (var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true))
            worksheetPath = GetWorksheetPathForSheetName(archive, "Data");

        AddCheckBoxControl(source, worksheetPath);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        loaded.GetSheet("Data")!.FormControls.Should().ContainSingle("sanity: the control must have loaded into the model");
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
        worksheetXml.Descendants(WorksheetNs + "control").Should().NotBeEmpty(
            "the renamed sheet's <control> reference must survive a plain rename");
        savedArchive.GetEntry("xl/ctrlProps/ctrlProp1.xml").Should().NotBeNull();
    }

    [Fact]
    public void NoRename_KeepsFormControl_OnFullRebuildSave()
    {
        var workbook = new Workbook("NoRenameFormControl");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 4, 9), new BoolValue(true));

        using var source = XlsxPackageTestHelper.SaveWorkbook(workbook);
        string worksheetPath;
        source.Position = 0;
        using (var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true))
            worksheetPath = GetWorksheetPathForSheetName(archive, "Data");

        AddCheckBoxControl(source, worksheetPath);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var tempSheet = loaded.AddSheet("__TempForFullSave__");
        loaded.RemoveSheet(tempSheet.Id);
        loaded.GetSheet("Data")!.SetCell(new CellAddress(loaded.GetSheet("Data")!.Id, 2, 1), new TextValue("edited"));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        savedArchive.GetEntry("xl/ctrlProps/ctrlProp1.xml").Should().NotBeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // 7) R103-io-rename-name-reuse-identity-gap: name-reuse-after-delete and name-swap repros.
    // Both defeat the plain name-based direct match (and the path-based fallback's own guard)
    // because a load-time name gets freed up and taken over by a genuinely different physical
    // sheet in the same edit -- see XlsxRenamedSourceSheetResolver's R103 header comment.
    // ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DeleteSheetThenRenameAnotherIntoItsFreedName_DoesNotResurrectDeletedSheetFormControlOntoRenamedSheet()
    {
        var workbook = new Workbook("R103DeleteThenReuseName");
        var data = workbook.AddSheet("Data");
        data.SetCell(new CellAddress(data.Id, 4, 9), new BoolValue(true));
        var sheet2 = workbook.AddSheet("Sheet2");
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new TextValue("plain"));

        using var source = XlsxPackageTestHelper.SaveWorkbook(workbook);
        string dataSourcePath;
        source.Position = 0;
        using (var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true))
            dataSourcePath = GetWorksheetPathForSheetName(archive, "Data");

        AddCheckBoxControl(source, dataSourcePath);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        loaded.GetSheet("Data")!.FormControls.Should().ContainSingle("sanity: the control must have loaded into the model");
        var ctx = new TestCommandContext(loaded);

        loaded.RemoveSheet(loaded.GetSheet("Data")!.Id).Should().BeTrue();
        new RenameSheetCommand(loaded.GetSheet("Sheet2")!.Id, "Data").Apply(ctx).Success.Should().BeTrue();

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        savedArchive.Entries.Should().ContainSingle(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase),
            "only the surviving (renamed) sheet's worksheet part should remain");
        var dataPath = GetWorksheetPathForSheetName(savedArchive, "Data");
        var worksheetXml = XlsxPackageXmlEditor.LoadXml(savedArchive.GetEntry(dataPath)!);
        worksheetXml.Descendants(WorksheetNs + "control").Should().BeEmpty(
            "the DELETED Data sheet's form control must not be resurrected onto Sheet2 just because Sheet2 " +
            "was renamed to reuse the freed 'Data' name string");

        // NOTE: xl/ctrlProps/ctrlProp1.xml (the ORPHANED part the deleted sheet's control used to
        // reference) is a separate, pre-existing sibling gap in
        // XlsxFileAdapter.SourcePackage.cs's GetExcludedWorksheetPackagePartPaths -- its own
        // removedWorksheetPaths computation checks `!context.TargetSheets.ContainsKey(currentName)`
        // without the same Sheet.Id identity verification this resolver now applies, so it doesn't
        // realize "Data" is gone (TargetSheets still has a "Data" key -- Sheet2's) and never computes
        // Data's now-orphaned dependency parts for exclusion. Out of scope for this defect (the
        // worksheet-content misattribution this test targets is fully fixed above); left as a named
        // follow-up rather than fixed here.
    }

    [Fact]
    public void SwapTwoSheetNames_KeepsEachSheetsOwnFormControlOnItsOwnPhysicalPart()
    {
        var workbook = new Workbook("R103SwapNames");
        var alpha = workbook.AddSheet("Alpha");
        alpha.SetCell(new CellAddress(alpha.Id, 4, 9), new BoolValue(true));
        var beta = workbook.AddSheet("Beta");
        beta.SetCell(new CellAddress(beta.Id, 1, 1), new TextValue("plain"));

        using var source = XlsxPackageTestHelper.SaveWorkbook(workbook);
        string alphaSourcePath, betaSourcePath;
        source.Position = 0;
        using (var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true))
        {
            alphaSourcePath = GetWorksheetPathForSheetName(archive, "Alpha");
            betaSourcePath = GetWorksheetPathForSheetName(archive, "Beta");
        }

        AddCheckBoxControl(source, alphaSourcePath);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        loaded.GetSheet("Alpha")!.FormControls.Should().ContainSingle("sanity: the control must have loaded into the model");
        var ctx = new TestCommandContext(loaded);

        var alphaId = loaded.GetSheet("Alpha")!.Id;
        var betaId = loaded.GetSheet("Beta")!.Id;
        new RenameSheetCommand(alphaId, "__Temp103__").Apply(ctx).Success.Should().BeTrue();
        new RenameSheetCommand(betaId, "Alpha").Apply(ctx).Success.Should().BeTrue();
        new RenameSheetCommand(alphaId, "Beta").Apply(ctx).Success.Should().BeTrue();

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);

        // A plain rename never moves a sheet's own worksheetN.xml part (established invariant used
        // throughout this file), so Alpha's control-carrying content must still live at its OWN
        // original path -- regardless of what name currently labels that path.
        var formerAlphaWorksheetXml = XlsxPackageXmlEditor.LoadXml(savedArchive.GetEntry(alphaSourcePath)!);
        formerAlphaWorksheetXml.Descendants(WorksheetNs + "control").Should().NotBeEmpty(
            "the sheet that was originally 'Alpha' (now named 'Beta' after the swap) must keep its OWN " +
            "form control on its OWN physical worksheet part");

        var formerBetaWorksheetXml = XlsxPackageXmlEditor.LoadXml(savedArchive.GetEntry(betaSourcePath)!);
        formerBetaWorksheetXml.Descendants(WorksheetNs + "control").Should().BeEmpty(
            "the sheet that was originally 'Beta' (now named 'Alpha' after the swap) must NOT inherit " +
            "the other sheet's form control just because the names crossed over");

        // Sanity: the swap actually took effect and both physical parts survived under their new names.
        GetWorksheetPathForSheetName(savedArchive, "Beta").Should().Be(
            XlsxPackagePath.NormalizePackagePath(alphaSourcePath));
        GetWorksheetPathForSheetName(savedArchive, "Alpha").Should().Be(
            XlsxPackagePath.NormalizePackagePath(betaSourcePath));
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // R124: XlsxWorksheetMetadataPreserver.PreserveWorksheetMetadata -- the raw-XML carry-forward
    // pass for everything FreeX doesn't model (protectedRanges, sortState, scenarios, rowBreaks/
    // colBreaks, oleObjects, controls, customSheetViews, sheetProtection, page setup, ...) used its
    // own inline direct-name/path-fallback resolution instead of the shared, Sheet.Id-identity-
    // verified XlsxRenamedSourceSheetResolver that XlsxWorksheetFormControlPreserver and
    // XlsxWorksheetDrawingReferencePreserver already delegate to (see
    // SwapTwoSheetNames_KeepsEachSheetsOwnFormControlOnItsOwnPhysicalPart above, which proves the
    // resolver-based preservers already handle this). A two-sheet NAME SWAP reuses a load-time name
    // string for a genuinely different physical sheet in the same save, so the naive direct-match
    // branch wrongly resolved to the OTHER sheet's target part, misattributing all of this
    // preserver's raw unmodeled metadata onto the wrong physical worksheet.
    // ─────────────────────────────────────────────────────────────────────────────────────────

    private static void AddOleObjectsMarker(MemoryStream packageStream, string worksheetPath)
    {
        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);

        var worksheetXml = XlsxPackageXmlEditor.LoadXml(archive.GetEntry(worksheetPath)!);
        var root = worksheetXml.Root!;
        // R124: a LINKED oleObject (no r:id embed relationship, only a `link` target) is valid per
        // CT_OleObject -- XlsxWorksheetOleControlNormalizer.ShouldRemoveRelationshipBackedElement
        // legitimately strips any <oleObject> that has neither an r:id embed NOR a `link` (it treats
        // that as orphaned/invalid), so the fixture must carry `link` to survive normalization without
        // needing a real xl/embeddings/*.bin part + relationship.
        root.Add(XElement.Parse(
            """
            <oleObjects xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <oleObject progId="R124.Marker" shapeId="9999" link="[Book1.xlsx]Sheet1!R1C1"/>
            </oleObjects>
            """));
        ReplaceEntry(archive, worksheetPath, worksheetXml);
        packageStream.Position = 0;
    }

    [Fact]
    public void SwapTwoSheetNames_KeepsEachSheetsOwnUnmodeledMetadataOnItsOwnPhysicalPart()
    {
        var workbook = new Workbook("R124SwapMetadata");
        var alpha = workbook.AddSheet("Alpha");
        alpha.SetCell(new CellAddress(alpha.Id, 1, 1), new TextValue("alpha-data"));
        var beta = workbook.AddSheet("Beta");
        beta.SetCell(new CellAddress(beta.Id, 1, 1), new TextValue("beta-data"));

        using var source = XlsxPackageTestHelper.SaveWorkbook(workbook);
        string alphaSourcePath, betaSourcePath;
        source.Position = 0;
        using (var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true))
        {
            alphaSourcePath = GetWorksheetPathForSheetName(archive, "Alpha");
            betaSourcePath = GetWorksheetPathForSheetName(archive, "Beta");
        }

        // Only Alpha's source worksheet carries the unmodeled <oleObjects> block.
        AddOleObjectsMarker(source, alphaSourcePath);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var ctx = new TestCommandContext(loaded);

        var alphaId = loaded.GetSheet("Alpha")!.Id;
        var betaId = loaded.GetSheet("Beta")!.Id;
        new RenameSheetCommand(alphaId, "__Temp124__").Apply(ctx).Success.Should().BeTrue();
        new RenameSheetCommand(betaId, "Alpha").Apply(ctx).Success.Should().BeTrue();
        new RenameSheetCommand(alphaId, "Beta").Apply(ctx).Success.Should().BeTrue();

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);

        // A plain rename never moves a sheet's own worksheetN.xml part, so Alpha's own unmodeled
        // metadata must still live at its OWN original physical path -- regardless of what name
        // currently labels that path after the swap.
        var formerAlphaWorksheetXml = XlsxPackageXmlEditor.LoadXml(savedArchive.GetEntry(alphaSourcePath)!);
        formerAlphaWorksheetXml.Root!.Element(WorksheetNs + "oleObjects").Should().NotBeNull(
            "the sheet that was originally 'Alpha' (now named 'Beta' after the swap) must keep its OWN " +
            "unmodeled metadata (protectedRanges/sheetProtection/scenarios/breaks/oleObjects/controls/...) " +
            "on its OWN physical worksheet part");

        var formerBetaWorksheetXml = XlsxPackageXmlEditor.LoadXml(savedArchive.GetEntry(betaSourcePath)!);
        formerBetaWorksheetXml.Root!.Element(WorksheetNs + "oleObjects").Should().BeNull(
            "the sheet that was originally 'Beta' (now named 'Alpha' after the swap) must NOT inherit " +
            "the other sheet's unmodeled metadata just because the names crossed over");

        // Sanity: the swap actually took effect and both physical parts survived under their new names.
        GetWorksheetPathForSheetName(savedArchive, "Beta").Should().Be(
            XlsxPackagePath.NormalizePackagePath(alphaSourcePath));
        GetWorksheetPathForSheetName(savedArchive, "Alpha").Should().Be(
            XlsxPackagePath.NormalizePackagePath(betaSourcePath));
    }

    // No-regression sibling: a PLAIN rename (no name reuse/swap) must still carry the unmodeled
    // metadata forward onto the renamed sheet's own physical part, exercising the ordinary
    // (non-identity) branch of the same resolution this fix touched.
    [Fact]
    public void RenameSheet_KeepsUnmodeledMetadata_AmongMultipleSheets()
    {
        var workbook = new Workbook("R124RenameMetadata");
        var data = workbook.AddSheet("Data");
        data.SetCell(new CellAddress(data.Id, 1, 1), new TextValue("data"));
        var report = workbook.AddSheet("Report");
        report.SetCell(new CellAddress(report.Id, 1, 1), new TextValue("report"));

        using var source = XlsxPackageTestHelper.SaveWorkbook(workbook);
        string dataSourcePath;
        source.Position = 0;
        using (var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true))
            dataSourcePath = GetWorksheetPathForSheetName(archive, "Data");

        AddOleObjectsMarker(source, dataSourcePath);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var loadedDataSheet = loaded.GetSheet("Data")!;
        var ctx = new TestCommandContext(loaded);

        new RenameSheetCommand(loadedDataSheet.Id, "DataRenamed").Apply(ctx).Success.Should().BeTrue();

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var renamedPath = GetWorksheetPathForSheetName(savedArchive, "DataRenamed");
        var worksheetXml = XlsxPackageXmlEditor.LoadXml(savedArchive.GetEntry(renamedPath)!);
        worksheetXml.Root!.Element(WorksheetNs + "oleObjects").Should().NotBeNull(
            "a plain rename (no name reuse) must still carry the sheet's own unmodeled metadata forward " +
            "onto its own (renamed) physical worksheet part");

        var reportPath = GetWorksheetPathForSheetName(savedArchive, "Report");
        var reportWorksheetXml = XlsxPackageXmlEditor.LoadXml(savedArchive.GetEntry(reportPath)!);
        reportWorksheetXml.Root!.Element(WorksheetNs + "oleObjects").Should().BeNull(
            "the untouched sibling sheet must not pick up metadata that never belonged to it");
    }
}
