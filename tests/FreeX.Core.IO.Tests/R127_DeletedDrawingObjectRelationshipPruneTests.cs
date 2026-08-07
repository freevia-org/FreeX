using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R127-io-drawing-relationship-orphan-1: deleting a picture/chart/shape/text box that was originally
/// loaded from the source .xlsx correctly drops its ANCHOR from the saved drawing part (R121), but
/// <c>XlsxWorksheetDrawingPartMerger.MergeDrawingRelationships</c> unconditionally copies every source
/// drawing relationship BEFORE that anchor decision is made, leaving a dangling &lt;Relationship&gt;
/// entry in the drawing part's own .rels -- and, for a deleted CHART specifically, the orphaned
/// relationship's target (xl/charts/chartN.xml, plus its own .rels/colors/style sidecars) was also being
/// blindly resurrected wholesale by <c>XlsxPackageMetadataMerger.CopyUnknownPackageParts</c> since no
/// exclusion for it existed.
/// <para>
/// ROUND-TRIP FIXTURE RULE: every fixture here is built by saving a workbook with the REAL
/// <see cref="XlsxFileAdapter"/> writer and reloading it, so the object under test is genuinely
/// <c>IsSourceLoaded == true</c> (or, for a chart, genuinely tombstoned via
/// <see cref="Sheet.DeletedSourceDrawingObjectNames"/>) before it is deleted -- never a hand-authored
/// drawing XML fragment.
/// </para>
/// </summary>
public sealed class R127_DeletedDrawingObjectRelationshipPruneTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

    [Fact]
    public void DeleteSourceLoadedPicture_SaveAndReload_PrunesOrphanedImageRelationship()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("DeletePictureRelationship");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var insert = new InsertPictureCommand(sheet.Id, new CellAddress(sheet.Id, 2, 2), CreatePngBytes(), "image/png");
        insert.Apply(ctx).Success.Should().BeTrue();

        using var initialSave = new MemoryStream();
        adapter.Save(workbook, initialSave);

        initialSave.Position = 0;
        var loaded = adapter.Load(initialSave);
        var loadedSheet = loaded.GetSheet("Sheet1")!;
        var picture = loadedSheet.Pictures.Should().ContainSingle().Which;
        picture.IsSourceLoaded.Should().BeTrue("a plain reloaded picture starts source-loaded");

        var deleteCommand = new DeleteDrawingObjectCommand(loadedSheet.Id, SelectionPaneObjectKind.Picture, picture.Id);
        deleteCommand.Apply(new TestCommandContext(loaded)).Success.Should().BeTrue();

        using var deletedSave = new MemoryStream();
        adapter.Save(loaded, deletedSave);

        deletedSave.Position = 0;
        using var archive = new ZipArchive(deletedSave, ZipArchiveMode.Read, leaveOpen: true);
        var drawingPath = ResolveWorksheetDrawingTarget(archive, "xl/worksheets/sheet1.xml");
        drawingPath.Should().NotBeNullOrEmpty("the sheet must still have a (now-empty) drawing part");

        var drawingRelsEntryName = GetRelationshipPartPath(drawingPath);
        var imageRelationships = GetRelationshipsOrEmpty(archive, drawingRelsEntryName)
            .Where(relationship => string.Equals(
                relationship.Attribute("Type")?.Value,
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image",
                System.StringComparison.OrdinalIgnoreCase))
            .ToList();

        imageRelationships.Should().BeEmpty(
            "the deleted picture's image relationship must not survive the save now that nothing in the drawing part references it");
    }

    [Fact]
    public void DeleteChart_SaveAndReload_PrunesOrphanedChartRelationshipAndPart()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("DeleteChartRelationship");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 0, 0), new TextValue("Cat"));
        sheet.SetCell(new CellAddress(sheet.Id, 0, 1), new TextValue("Series"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 0), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 0), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        var ctx = new TestCommandContext(workbook);
        var dataRange = new GridRange(new CellAddress(sheet.Id, 0, 0), new CellAddress(sheet.Id, 2, 1));
        var insert = new AddChartCommand(sheet.Id, dataRange, ChartType.Column, "Chart 1");
        insert.Apply(ctx).Success.Should().BeTrue();

        using var initialSave = new MemoryStream();
        adapter.Save(workbook, initialSave);

        initialSave.Position = 0;
        var loaded = adapter.Load(initialSave);
        var loadedSheet = loaded.GetSheet("Sheet1")!;
        var chart = loadedSheet.Charts.Should().ContainSingle().Which;

        var deleteCommand = new DeleteDrawingObjectCommand(loadedSheet.Id, SelectionPaneObjectKind.Chart, chart.Id);
        deleteCommand.Apply(new TestCommandContext(loaded)).Success.Should().BeTrue();
        loadedSheet.DeletedSourceDrawingObjectNames.Should().NotBeEmpty("the delete command must tombstone the chart's cNvPr name");

        using var deletedSave = new MemoryStream();
        adapter.Save(loaded, deletedSave);

        deletedSave.Position = 0;
        using var archive = new ZipArchive(deletedSave, ZipArchiveMode.Read, leaveOpen: true);

        // Fix A: the dangling drawing-relationship entry must be pruned.
        var drawingPath = ResolveWorksheetDrawingTarget(archive, "xl/worksheets/sheet1.xml");
        if (!string.IsNullOrEmpty(drawingPath))
        {
            var drawingRelsEntryName = GetRelationshipPartPath(drawingPath);
            var chartRelationships = GetRelationshipsOrEmpty(archive, drawingRelsEntryName)
                .Where(relationship => string.Equals(
                    relationship.Attribute("Type")?.Value,
                    "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart",
                    System.StringComparison.OrdinalIgnoreCase))
                .ToList();
            chartRelationships.Should().BeEmpty(
                "the deleted chart's relationship must not survive the save now that no anchor references it");
        }

        // Fix B: the chart's own orphaned part set must not be resurrected wholesale either.
        archive.Entries
            .Where(entry => entry.FullName.StartsWith("xl/charts/", System.StringComparison.OrdinalIgnoreCase))
            .Should().BeEmpty("a deleted chart's own part set (chartN.xml, its rels, colors/style sidecars) must not be carried forward as a dead, unreferenced part");

        var contentTypesXml = LoadPackageXml(archive, "[Content_Types].xml");
        var chartOverrides = contentTypesXml.Root!.Elements(ContentTypeNs + "Override")
            .Where(element => (element.Attribute("PartName")?.Value ?? "").Contains("/xl/charts/", System.StringComparison.OrdinalIgnoreCase))
            .ToList();
        chartOverrides.Should().BeEmpty("no [Content_Types].xml Override should remain for the deleted chart's part");

        deletedSave.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(deletedSave);
        reloaded.GetSheet("Sheet1")!.Charts.Should().BeEmpty(
            "a deleted chart must not be merged back in from the untouched source package");
    }

    [Fact]
    public void DeleteOnePictureAmongTwo_SaveAndReload_KeepsSurvivingPictureRelationship()
    {
        // No-regression sibling: pruning must only remove relationships nothing references anymore --
        // it must not over-prune a relationship a SURVIVING anchor still legitimately needs.
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("DeleteOneOfTwoPictures");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var insertA = new InsertPictureCommand(sheet.Id, new CellAddress(sheet.Id, 1, 1), CreatePngBytes(), "image/png");
        insertA.Apply(ctx).Success.Should().BeTrue();
        var insertB = new InsertPictureCommand(sheet.Id, new CellAddress(sheet.Id, 6, 6), CreatePngBytes(), "image/png");
        insertB.Apply(ctx).Success.Should().BeTrue();

        using var initialSave = new MemoryStream();
        adapter.Save(workbook, initialSave);

        initialSave.Position = 0;
        var loaded = adapter.Load(initialSave);
        var loadedSheet = loaded.GetSheet("Sheet1")!;
        loadedSheet.Pictures.Should().HaveCount(2);
        var pictureToDelete = loadedSheet.Pictures[0];
        var survivingName = loadedSheet.Pictures[1].Name;

        var deleteCommand = new DeleteDrawingObjectCommand(loadedSheet.Id, SelectionPaneObjectKind.Picture, pictureToDelete.Id);
        deleteCommand.Apply(new TestCommandContext(loaded)).Success.Should().BeTrue();
        loadedSheet.Pictures.Should().ContainSingle();

        using var deletedSave = new MemoryStream();
        adapter.Save(loaded, deletedSave);

        deletedSave.Position = 0;
        using (var archive = new ZipArchive(deletedSave, ZipArchiveMode.Read, leaveOpen: true))
        {
            var drawingPath = ResolveWorksheetDrawingTarget(archive, "xl/worksheets/sheet1.xml");
            var drawingRelsEntryName = GetRelationshipPartPath(drawingPath);
            var imageRelationships = GetRelationshipsOrEmpty(archive, drawingRelsEntryName)
                .Where(relationship => string.Equals(
                    relationship.Attribute("Type")?.Value,
                    "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image",
                    System.StringComparison.OrdinalIgnoreCase))
                .ToList();
            imageRelationships.Should().ContainSingle(
                "the surviving picture's own image relationship must not be pruned away too");
        }

        deletedSave.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(deletedSave);
        var reloadedPictures = reloaded.GetSheet("Sheet1")!.Pictures;
        reloadedPictures.Should().ContainSingle();
        reloadedPictures[0].Name.Should().Be(survivingName, "the surviving picture must round-trip intact, not the deleted one");
    }

    private static string ResolveWorksheetDrawingTarget(ZipArchive archive, string worksheetPath)
    {
        var worksheetXml = LoadPackageXml(archive, worksheetPath);
        var drawingRelId = worksheetXml.Root!
            .Element(WorksheetNs + "drawing")?
            .Attribute(RelNs + "id")?
            .Value;
        if (string.IsNullOrEmpty(drawingRelId))
            return string.Empty;

        var relsPath = GetRelationshipPartPath(worksheetPath);
        var relsEntry = archive.GetEntry(relsPath);
        if (relsEntry is null)
            return string.Empty;

        var relsXml = LoadPackageXml(archive, relsPath);
        var target = relsXml.Root!
            .Elements(PackageRelNs + "Relationship")
            .FirstOrDefault(r => r.Attribute("Id")?.Value == drawingRelId)?
            .Attribute("Target")?
            .Value ?? string.Empty;
        if (string.IsNullOrEmpty(target))
            return string.Empty;

        // Target is relative to xl/worksheets/ (e.g. "../drawings/drawing1.xml") -- resolve to a
        // package-absolute path the way every other reader in this codebase does.
        return XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, target);
    }

    private static string GetRelationshipPartPath(string partPath)
    {
        return string.IsNullOrEmpty(partPath) ? string.Empty : XlsxPackagePath.GetRelationshipPartPath(partPath);
    }

    private static System.Collections.Generic.IReadOnlyList<XElement> GetRelationshipsOrEmpty(ZipArchive archive, string relsEntryName)
    {
        var entry = archive.GetEntry(relsEntryName);
        if (entry is null)
            return System.Array.Empty<XElement>();

        var xml = LoadPackageXml(archive, relsEntryName);
        return xml.Root!.Elements(PackageRelNs + "Relationship").ToList();
    }

    private static XDocument LoadPackageXml(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName);
        entry.Should().NotBeNull($"package entry '{entryName}' must exist");
        using var stream = entry!.Open();
        return XDocument.Load(stream);
    }

    private static byte[] CreatePngBytes()
    {
        // Minimal valid 1x1 transparent PNG.
        return
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41,
            0x54, 0x78, 0x9C, 0x62, 0x00, 0x01, 0x00, 0x00,
            0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
            0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
            0x42, 0x60, 0x82
        ];
    }
}
