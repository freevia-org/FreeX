using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-trip data-loss regression tests for <see cref="XlsxWorksheetDrawingObjectWriter"/>.
/// Verifies that when a workbook is saved, reloaded, and saved again with new authored drawing
/// objects on a second sheet, drawing parts are allocated without colliding with the source-preserved
/// drawing that belongs to the first sheet.
/// </summary>
public sealed class DrawingObjectWriterRoundTripTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace SpreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

    // Regression test for the drawing-index collision bug:
    //   1. Build a workbook with a picture on Sheet1; save it → drawing1.xml is genuinely wired to Sheet1.
    //   2. Load it back (Sheet1's drawing is now source-loaded/preserved).
    //   3. Add a NEW picture on Sheet2.
    //   4. Save again.
    //   5. Assert: Sheet1 still has its picture (drawing part intact), Sheet2's picture is on a SEPARATE
    //      drawing part, and the two sheets reference different, non-colliding drawing paths.
    //
    // Without the fix, the object writer's naive drawingIndex++ counter claims drawing1.xml for Sheet2
    // (the Delete at that path is a no-op because source-preservation hasn't run yet), then
    // CopyUnknownPackageParts skips the source drawing1.xml (name already exists), and the worksheet-rel
    // preserver wires Sheet1 to drawing1.xml which now holds Sheet2's picture — losing Sheet1's picture.
    [Fact]
    public void DrawingObjectWriter_AfterReload_DoesNotCollideWithSourcePreservedDrawingPart()
    {
        var adapter = new XlsxFileAdapter();

        // ── Step 1: build Sheet1 with a picture and save ────────────────────────────────
        var workbook1 = new Workbook("DrawingCollisionRegression");
        var sheet1 = workbook1.AddSheet("Sheet1");
        sheet1.Pictures.Add(new PictureModel
        {
            Name = "Sheet1Picture",
            Anchor = new CellAddress(sheet1.Id, 2, 2),
            Kind = PictureKind.Image,
            ImageBytes = MinimalPngBytes(),
            ContentType = "image/png",
            Width = 96,
            Height = 64,
            AltText = "Sheet1 picture"
        });
        workbook1.AddSheet("Sheet2");

        using var firstSave = new MemoryStream();
        adapter.Save(workbook1, firstSave);

        // Verify: after the first save drawing1.xml exists and Sheet1 references it.
        firstSave.Position = 0;
        using (var archive = new ZipArchive(firstSave, ZipArchiveMode.Read, leaveOpen: true))
        {
            archive.GetEntry("xl/drawings/drawing1.xml")
                .Should().NotBeNull("drawing1.xml must be created on the first save");

            var sheet1DrawingPath = ResolveWorksheetDrawingTarget(archive, "xl/worksheets/sheet1.xml");
            sheet1DrawingPath.Should().Contain("drawing1.xml",
                "Sheet1's worksheet rel must point at drawing1.xml after the first save");
        }

        // ── Step 2: reload (Sheet1's drawing is now source-preserved) ────────────────────
        firstSave.Position = 0;
        var workbook2 = adapter.Load(firstSave);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook2, out var blockReason)
            .Should().BeTrue(blockReason);

        // ── Step 3: add a NEW picture on Sheet2 ─────────────────────────────────────────
        var sheet2 = workbook2.GetSheet("Sheet2")!;
        sheet2.Pictures.Add(new PictureModel
        {
            Name = "Sheet2Picture",
            Anchor = new CellAddress(sheet2.Id, 3, 3),
            Kind = PictureKind.Image,
            ImageBytes = MinimalPngBytes(),
            ContentType = "image/png",
            Width = 120,
            Height = 80,
            AltText = "Sheet2 picture"
        });

        // ── Step 4: save again ───────────────────────────────────────────────────────────
        using var secondSave = new MemoryStream();
        adapter.Save(workbook2, secondSave);

        // ── Step 5: assertions ───────────────────────────────────────────────────────────
        secondSave.Position = 0;
        using var resultArchive = new ZipArchive(secondSave, ZipArchiveMode.Read, leaveOpen: true);

        // Both drawing parts must exist.
        resultArchive.GetEntry("xl/drawings/drawing1.xml")
            .Should().NotBeNull("drawing1.xml (Sheet1's source drawing) must be preserved");
        resultArchive.GetEntry("xl/drawings/drawing2.xml")
            .Should().NotBeNull("drawing2.xml must be created for Sheet2's new picture");

        // Sheet1 must still reference drawing1.xml (its original source drawing).
        var sheet1DrawingPathAfter = ResolveWorksheetDrawingTarget(resultArchive, "xl/worksheets/sheet1.xml");
        sheet1DrawingPathAfter.Should().Contain("drawing1.xml",
            "Sheet1's worksheet rel must still point at drawing1.xml after the second save");

        // Sheet2 must reference drawing2.xml (a fresh, non-colliding allocation).
        var sheet2DrawingPathAfter = ResolveWorksheetDrawingTarget(resultArchive, "xl/worksheets/sheet2.xml");
        sheet2DrawingPathAfter.Should().Contain("drawing2.xml",
            "Sheet2's worksheet rel must point at drawing2.xml, not Sheet1's drawing1.xml");

        // The two sheets must reference DIFFERENT drawing parts.
        sheet1DrawingPathAfter.Should().NotBe(sheet2DrawingPathAfter,
            "Sheet1 and Sheet2 must not share the same drawing part");

        // drawing1.xml must still contain the Sheet1 picture anchor (not Sheet2's picture).
        var drawing1 = LoadPackageXml(resultArchive, "xl/drawings/drawing1.xml");
        drawing1.Root!
            .Descendants(SpreadsheetDrawingNs + "oneCellAnchor")
            .Should().ContainSingle("drawing1.xml must contain exactly one anchor for Sheet1's picture");

        // drawing2.xml must contain Sheet2's picture anchor.
        var drawing2 = LoadPackageXml(resultArchive, "xl/drawings/drawing2.xml");
        drawing2.Root!
            .Descendants(SpreadsheetDrawingNs + "oneCellAnchor")
            .Should().ContainSingle("drawing2.xml must contain exactly one anchor for Sheet2's picture");

        // Reload the second save and confirm both pictures are still round-trippable.
        resultArchive.Dispose();
        secondSave.Position = 0;
        var reloaded = adapter.Load(secondSave);

        var reloadedSheet1 = reloaded.GetSheet("Sheet1")!;
        var reloadedSheet1Picture = reloadedSheet1.Pictures
            .Should().ContainSingle("Sheet1 must still have its picture after the second round-trip").Subject;
        reloadedSheet1Picture.Name.Should().Be("Sheet1Picture");
        reloadedSheet1Picture.ImageBytes.Should().Equal(MinimalPngBytes());

        var reloadedSheet2 = reloaded.GetSheet("Sheet2")!;
        var reloadedSheet2Picture = reloadedSheet2.Pictures
            .Should().ContainSingle("Sheet2 must have its newly added picture after the round-trip").Subject;
        reloadedSheet2Picture.Name.Should().Be("Sheet2Picture");
        reloadedSheet2Picture.ImageBytes.Should().Equal(MinimalPngBytes());
    }

    /// <summary>
    /// Follows the worksheet drawing relationship to resolve the drawing part target path.
    /// Returns the raw Target attribute value from the worksheet .rels file.
    /// </summary>
    private static string ResolveWorksheetDrawingTarget(ZipArchive archive, string worksheetPath)
    {
        var worksheetXml = LoadPackageXml(archive, worksheetPath);
        var drawingRelId = worksheetXml.Root!
            .Element(WorksheetNs + "drawing")?
            .Attribute(RelNs + "id")?
            .Value;
        if (string.IsNullOrEmpty(drawingRelId))
            return string.Empty;

        var relsPath = $"{System.IO.Path.GetDirectoryName(worksheetPath)!.Replace('\\', '/')}/_rels/{System.IO.Path.GetFileName(worksheetPath)}.rels";
        var relsXml = LoadPackageXml(archive, relsPath);
        return relsXml.Root!
            .Elements(PackageRelNs + "Relationship")
            .FirstOrDefault(r => r.Attribute("Id")?.Value == drawingRelId)?
            .Attribute("Target")?
            .Value ?? string.Empty;
    }

    private static XDocument LoadPackageXml(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName);
        entry.Should().NotBeNull($"package entry '{entryName}' must exist");
        using var stream = entry!.Open();
        return XDocument.Load(stream);
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
}
