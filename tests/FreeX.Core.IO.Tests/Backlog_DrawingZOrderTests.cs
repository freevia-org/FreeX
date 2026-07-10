using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// drawing-zorder-share-part backlog item: a worksheet's charts and its shapes/pictures/text boxes are
/// each rebuilt into the SAME xl/drawings/drawingN.xml part by two independent writers
/// (<see cref="XlsxWorksheetChartWriter"/> for charts, <c>XlsxWorksheetDrawingObjectWriter</c> for
/// drawing objects). Before the fix, whichever writer ran second (the drawing-object writer, per the
/// fixed SavePostProcessing call order) unconditionally deleted-and-rewrote that part, silently
/// discarding whatever the first writer (the chart writer) had just written — so a sheet with both a
/// chart and a shape lost the chart on every resave.
///
/// This test drives the real save/reload/save pipeline (<see cref="XlsxFileAdapter"/>) to reproduce the
/// exact production scenario: build a sheet with a chart, save (so the sheet genuinely owns a source
/// drawing part), reload, add a NEW shape to that same sheet, and save again. Pre-fix, the second save's
/// drawing part contains only the shape anchor (the chart anchor is lost). Post-fix, it contains BOTH.
/// </summary>
public sealed class Backlog_drawing_zorder_share_part_Tests
{
    private static readonly XNamespace SpreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    [Fact]
    public void Save_SheetWithChartAndNewlyAddedShape_SharesOneDrawingPartWithBothAnchors()
    {
        var adapter = new XlsxFileAdapter();

        // ── Step 1: a sheet with only a chart, saved once so it genuinely owns a source drawing part ──
        var workbook1 = new Workbook("DrawingZOrderRegression");
        var sheet1 = workbook1.AddSheet("Sheet1");
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new TextValue("Category"));
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 2), new TextValue("Value"));
        sheet1.SetCell(new CellAddress(sheet1.Id, 2, 1), new TextValue("A"));
        sheet1.SetCell(new CellAddress(sheet1.Id, 3, 1), new TextValue("B"));
        sheet1.SetCell(new CellAddress(sheet1.Id, 2, 2), new NumberValue(10));
        sheet1.SetCell(new CellAddress(sheet1.Id, 3, 2), new NumberValue(20));
        sheet1.Charts.Add(new ChartModel
        {
            Name = "Regression Chart",
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 3, 2)),
        });

        using var firstSave = new MemoryStream();
        adapter.Save(workbook1, firstSave);

        firstSave.Position = 0;
        using (var archive = new ZipArchive(firstSave, ZipArchiveMode.Read, leaveOpen: true))
        {
            var drawingPath = ResolveWorksheetDrawingTarget(archive, "xl/worksheets/sheet1.xml");
            drawingPath.Should().Contain("drawing1.xml", "the first save must wire Sheet1 to its own drawing part");

            var drawingXml = LoadPackageXml(archive, "xl/drawings/drawing1.xml");
            drawingXml.Root!.Elements()
                .Should().ContainSingle("only the chart anchor should exist after the first save");
        }

        // ── Step 2: reload (Sheet1's drawing part is now source-preserved) ──────────────────────────
        firstSave.Position = 0;
        var workbook2 = adapter.Load(firstSave);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook2, out var blockReason)
            .Should().BeTrue(blockReason);

        // ── Step 3: add a NEW shape to the SAME sheet that owns the chart ───────────────────────────
        var reloadedSheet1 = workbook2.GetSheet("Sheet1")!;
        reloadedSheet1.DrawingShapes.Add(new DrawingShapeModel
        {
            Name = "Regression Shape",
            Kind = DrawingShapeKind.Rectangle,
            Anchor = new CellAddress(reloadedSheet1.Id, 5, 5),
            Width = 100,
            Height = 60,
        });

        // ── Step 4: save again ───────────────────────────────────────────────────────────────────────
        using var secondSave = new MemoryStream();
        adapter.Save(workbook2, secondSave);

        // ── Step 5: assertions ───────────────────────────────────────────────────────────────────────
        secondSave.Position = 0;
        using var resultArchive = new ZipArchive(secondSave, ZipArchiveMode.Read, leaveOpen: true);

        var finalDrawingPath = ResolveWorksheetDrawingTarget(resultArchive, "xl/worksheets/sheet1.xml");
        finalDrawingPath.Should().NotBeNullOrEmpty("Sheet1 must still reference a drawing part");

        var finalDrawingEntryName = "xl/drawings/" + finalDrawingPath[(finalDrawingPath.LastIndexOf('/') + 1)..];
        var finalDrawingXml = LoadPackageXml(resultArchive, finalDrawingEntryName);
        var topLevelAnchors = finalDrawingXml.Root!.Elements().ToList();

        topLevelAnchors.Should().HaveCount(2,
            "the sheet's single drawing part must hold BOTH the chart anchor and the new shape anchor, not just one");

        var chartAnchors = finalDrawingXml.Root!.Descendants(SpreadsheetDrawingNs + "graphicFrame").ToList();
        chartAnchors.Should().ContainSingle(
            "the chart anchor written by XlsxWorksheetChartWriter must survive XlsxWorksheetDrawingObjectWriter's rewrite of the same part");

        var shapeAnchors = finalDrawingXml.Root!.Descendants(SpreadsheetDrawingNs + "sp")
            .Where(sp => sp.Element(SpreadsheetDrawingNs + "nvSpPr")?
                .Element(SpreadsheetDrawingNs + "cNvPr")?
                .Attribute("name")?.Value == "Regression Shape")
            .ToList();
        shapeAnchors.Should().ContainSingle("the newly authored shape anchor must also be present");

        // Every drawing part referenced anywhere in the package must be unique to this sheet: no second,
        // orphaned drawing part left behind holding one of the two anchors.
        resultArchive.Entries
            .Where(entry => entry.FullName.StartsWith("xl/drawings/", System.StringComparison.OrdinalIgnoreCase) &&
                            entry.FullName.EndsWith(".xml", System.StringComparison.OrdinalIgnoreCase) &&
                            !entry.FullName.Contains("/_rels/"))
            .Should().ContainSingle("there must be exactly one real drawing part for this single-sheet workbook");
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

        var relsPath = $"{Path.GetDirectoryName(worksheetPath)!.Replace('\\', '/')}/_rels/{Path.GetFileName(worksheetPath)}.rels";
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
}
