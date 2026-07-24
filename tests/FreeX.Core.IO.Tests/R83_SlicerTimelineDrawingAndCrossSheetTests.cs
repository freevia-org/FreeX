using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-83 fix bucket "io-slicer-timeline" regression tests.
/// <list type="bullet">
/// <item>R83-io-slicer-timeline-5-1 — a newly-inserted slicer/timeline never got an
/// <c>xl/drawings/*.xml</c> mc:AlternateContent -&gt; mc:Choice -&gt; graphicFrame anchor authored on
/// EITHER save path (the fresh no-source-package writer, <see cref="XlsxSlicerTimelineWriter"/>, or the
/// source-preserved append-new-control path, <see cref="XlsxSlicerTimelineStateRewriter"/>), so it had no
/// on-sheet shape at all -- and thus no <c>DrawingAnchor</c> -- after a save+reload round trip. Both paths
/// now author the anchor via <see cref="XlsxSlicerTimelineDrawingWriter"/>.</item>
/// <item>R83-io-slicer-timeline-5-2 — a slicer/timeline anchored on a DIFFERENT sheet than its bound pivot
/// table ("dashboard" pattern) had its per-worksheet relationship/extLst wired to the PIVOT's sheet
/// instead of its OWN <c>SourceSheetName</c>, on both the fresh writer's <c>ResolveWorksheetPath</c> and
/// the state rewriter's identically-shaped one. Both now consult <c>SourceSheetName</c> first.</item>
/// </list>
/// </summary>
public sealed class R83_SlicerTimelineDrawingAndCrossSheetTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    // ── R83-io-slicer-timeline-5-1: fresh (no-source-package) save path ───────────────────────────

    [Fact]
    public void NewSlicerWithDrawingAnchor_FreshSave_AuthorsDrawingGraphicFrame()
    {
        var workbook = BuildPivotWorkbook();
        var slicer = new SlicerModel
        {
            Name = "Region Slicer",
            CacheName = "Slicer_Region",
            Caption = "Region",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Region",
            DrawingAnchor = new DrawingAnchorRange(
                new DrawingAnchorPoint(5, 0, 1, 0),
                new DrawingAnchorPoint(8, 0, 9, 0))
        };
        workbook.Slicers.Add(slicer);

        using var saved = SaveWorkbook(workbook);

        SchemaErrors(saved).Should().BeEmpty();
        var drawingXml = ReadRoot(saved, "xl/drawings/drawing1.xml");
        drawingXml.Descendants()
            .Any(element => element.Name.LocalName == "slicer" && element.Attribute("name")?.Value == "Region Slicer")
            .Should().BeTrue("the fresh writer must author a graphicFrame anchor for the new slicer, or it has no on-sheet shape");

        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedSlicer = reloaded.Slicers.Should().ContainSingle().Subject;
        reloadedSlicer.DrawingAnchor.Should().NotBeNull(
            "the reader locates a slicer's shape exclusively via the drawing graphicFrame it just wrote");
        reloadedSlicer.SourceSheetName.Should().Be("Data");
    }

    [Fact]
    public void NewTimelineWithDrawingAnchor_FreshSave_AuthorsDrawingGraphicFrame()
    {
        var workbook = BuildPivotWorkbook();
        var timeline = new TimelineModel
        {
            Name = "Date Timeline",
            CacheName = "Timeline_Date",
            Caption = "Order Date",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Date",
            StartDate = "2026-01-01",
            EndDate = "2026-06-30",
            DrawingAnchor = new DrawingAnchorRange(
                new DrawingAnchorPoint(5, 0, 1, 0),
                new DrawingAnchorPoint(9, 0, 5, 0))
        };
        workbook.Timelines.Add(timeline);

        using var saved = SaveWorkbook(workbook);

        SchemaErrors(saved).Should().BeEmpty();
        var drawingXml = ReadRoot(saved, "xl/drawings/drawing1.xml");
        drawingXml.Descendants()
            .Any(element =>
                (element.Name.LocalName == "timeline" || element.Name.LocalName == "timeslicer") &&
                element.Attribute("name")?.Value == "Date Timeline")
            .Should().BeTrue("the fresh writer must author a graphicFrame anchor for the new timeline");

        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        reloaded.Timelines.Should().ContainSingle().Subject.DrawingAnchor.Should().NotBeNull();
    }

    // Sibling/no-regression: a control with no DrawingAnchor at all (FreeX cannot yet place it) must not
    // crash and must not author a spurious drawing part.
    [Fact]
    public void NewSlicerWithoutDrawingAnchor_FreshSave_AuthorsNoDrawingPart()
    {
        var workbook = BuildPivotWorkbook();
        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Region Slicer",
            CacheName = "Slicer_Region",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Region"
        });

        using var saved = SaveWorkbook(workbook);

        SchemaErrors(saved).Should().BeEmpty();
        PartExists(saved, "xl/drawings/drawing1.xml").Should().BeFalse(
            "no drawing part should be authored when the model carries no anchor to place");

        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        reloaded.Slicers.Should().ContainSingle().Subject.DrawingAnchor.Should().BeNull();
    }

    // ── R83-io-slicer-timeline-5-1: source-preserved append-new-control path ──────────────────────

    [Fact]
    public void NewSlicerAddedToLoadedWorkbook_ResaveAuthorsDrawingGraphicFrame()
    {
        using var source = SaveWorkbook(BuildPivotWorkbook());

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        loaded.Slicers.Add(new SlicerModel
        {
            Name = "Region Slicer",
            CacheName = "Slicer_Region",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Region",
            DrawingAnchor = new DrawingAnchorRange(
                new DrawingAnchorPoint(5, 0, 1, 0),
                new DrawingAnchorPoint(8, 0, 9, 0))
        });

        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 9, 9), new NumberValue(1));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        SchemaErrors(saved).Should().BeEmpty();
        var drawingXml = ReadRoot(saved, "xl/drawings/drawing1.xml");
        drawingXml.Descendants()
            .Any(element => element.Name.LocalName == "slicer" && element.Attribute("name")?.Value == "Region Slicer")
            .Should().BeTrue("the source-preserved append-new-control path must also author a graphicFrame anchor");

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        reloaded.Slicers.Should().ContainSingle().Subject.DrawingAnchor.Should().NotBeNull();
    }

    // Sibling/no-regression: re-saving with no new controls (only the previously-existing preserved
    // slicer) must not touch its already-preserved drawing/anchor.
    [Fact]
    public void ExistingPreservedSlicer_Resave_LeavesDrawingUntouched()
    {
        var workbookWithSlicer = BuildPivotWorkbook();
        workbookWithSlicer.Slicers.Add(new SlicerModel
        {
            Name = "Region Slicer",
            CacheName = "Slicer_Region",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Region",
            DrawingAnchor = new DrawingAnchorRange(
                new DrawingAnchorPoint(5, 0, 1, 0),
                new DrawingAnchorPoint(8, 0, 9, 0))
        });
        using var source = SaveWorkbook(workbookWithSlicer);
        var originalDrawingXml = ReadRoot(source, "xl/drawings/drawing1.xml");

        var adapter = new XlsxFileAdapter();
        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.Slicers.Should().ContainSingle().Subject.DrawingAnchor.Should().NotBeNull();

        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 9, 9), new NumberValue(1));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        var resavedDrawingXml = ReadRoot(saved, "xl/drawings/drawing1.xml");
        XNode.DeepEquals(originalDrawingXml, resavedDrawingXml).Should().BeTrue(
            "re-saving a workbook whose only slicer already had a preserved anchor must not rewrite it");
    }

    // ── R83-io-slicer-timeline-5-2: fresh (no-source-package) save path ────────────────────────────

    [Fact]
    public void CrossSheetSlicer_FreshSave_BindsWorksheetRelationshipToOwnSheetNotPivotSheet()
    {
        var workbook = BuildPivotWorkbook();
        workbook.AddSheet("Dashboard");
        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Region Slicer",
            CacheName = "Slicer_Region",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Region",
            SourceSheetName = "Dashboard",
            DrawingAnchor = new DrawingAnchorRange(
                new DrawingAnchorPoint(1, 0, 1, 0),
                new DrawingAnchorPoint(4, 0, 9, 0))
        });

        using var saved = SaveWorkbook(workbook);

        SchemaErrors(saved).Should().BeEmpty();
        var dataPath = ResolveWorksheetPathByName(saved, "Data");
        var dashboardPath = ResolveWorksheetPathByName(saved, "Dashboard");

        WorksheetHasSlicerRelationship(saved, dashboardPath).Should().BeTrue(
            "the slicer is anchored on Dashboard, so ITS worksheet must carry the slicer relationship/extLst");
        WorksheetHasSlicerRelationship(saved, dataPath).Should().BeFalse(
            "the pivot's own sheet (Data) must NOT get the slicer relationship just because it hosts the bound pivot table");
    }

    // Sibling/no-regression: when SourceSheetName is absent (a freshly-inserted, same-sheet slicer never
    // sets it), resolution must still fall back to the pivot table's own host sheet exactly as before.
    [Fact]
    public void SameSheetSlicer_FreshSave_BindsWorksheetRelationshipToPivotSheet()
    {
        var workbook = BuildPivotWorkbook();
        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Region Slicer",
            CacheName = "Slicer_Region",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Region",
            DrawingAnchor = new DrawingAnchorRange(
                new DrawingAnchorPoint(5, 0, 1, 0),
                new DrawingAnchorPoint(8, 0, 9, 0))
        });

        using var saved = SaveWorkbook(workbook);

        var dataPath = ResolveWorksheetPathByName(saved, "Data");
        WorksheetHasSlicerRelationship(saved, dataPath).Should().BeTrue();
    }

    // ── R83-io-slicer-timeline-5-2: source-preserved append-new-control path ──────────────────────

    [Fact]
    public void CrossSheetSlicerAddedToLoadedWorkbook_ResaveBindsOwnSheetNotPivotSheet()
    {
        var baseWorkbook = BuildPivotWorkbook();
        baseWorkbook.AddSheet("Dashboard");
        using var source = SaveWorkbook(baseWorkbook);

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        loaded.Slicers.Add(new SlicerModel
        {
            Name = "Region Slicer",
            CacheName = "Slicer_Region",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Region",
            SourceSheetName = "Dashboard",
            DrawingAnchor = new DrawingAnchorRange(
                new DrawingAnchorPoint(1, 0, 1, 0),
                new DrawingAnchorPoint(4, 0, 9, 0))
        });

        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 9, 9), new NumberValue(1));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        SchemaErrors(saved).Should().BeEmpty();
        var dataPath = ResolveWorksheetPathByName(saved, "Data");
        var dashboardPath = ResolveWorksheetPathByName(saved, "Dashboard");

        WorksheetHasSlicerRelationship(saved, dashboardPath).Should().BeTrue(
            "the append-new-control path must also bind to the slicer's OWN sheet");
        WorksheetHasSlicerRelationship(saved, dataPath).Should().BeFalse(
            "the pivot's own sheet must not get the relationship just because it hosts the bound pivot table");
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static Workbook BuildPivotWorkbook()
    {
        var workbook = new Workbook("PivotSlicerDrawingR83");
        var sheet = workbook.AddSheet("Data");
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
        // Deliberately no SharedItems here: XlsxSlicerTimelineWriter.BuildPivotSlicerCacheDataElement (an
        // unrelated, pre-existing writer path -- not part of this fix) emits a schema-invalid x14:tabular
        // when a field has SharedItems, which would make every SchemaErrors assertion below fail for a
        // reason unrelated to the drawing-anchor/cross-sheet fixes under test here.
        cache.Fields.Add(new PivotCacheFieldModel("Region", ContainsString: true));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 6, 1), new CellAddress(sheet.Id, 9, 2))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        return workbook;
    }

    private static string ResolveWorksheetPathByName(Stream stream, string sheetName)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        var workbookXml = XDocument.Load(archive.GetEntry("xl/workbook.xml")!.Open());
        var sheetElement = workbookXml.Root!.Element(WorkbookNs + "sheets")!.Elements(WorkbookNs + "sheet")
            .First(element => element.Attribute("name")!.Value == sheetName);
        var relId = sheetElement.Attribute(RelNs + "id")!.Value;

        var relsXml = XDocument.Load(archive.GetEntry("xl/_rels/workbook.xml.rels")!.Open());
        var target = relsXml.Root!.Elements(PackageRelNs + "Relationship")
            .First(element => element.Attribute("Id")!.Value == relId).Attribute("Target")!.Value;

        return target.StartsWith('/') ? target.TrimStart('/') : $"xl/{target}";
    }

    private static bool WorksheetHasSlicerRelationship(Stream stream, string worksheetPath)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var relsEntry = archive.GetEntry(GetRelsPath(worksheetPath));
        if (relsEntry is null)
            return false;

        var xml = XDocument.Load(relsEntry.Open());
        return xml.Root!.Elements(PackageRelNs + "Relationship")
            .Any(element => (element.Attribute("Type")?.Value ?? "").EndsWith("/relationships/slicer", System.StringComparison.Ordinal));
    }

    private static string GetRelsPath(string partPath)
    {
        var slash = partPath.LastIndexOf('/');
        var directory = partPath[..slash];
        var file = partPath[(slash + 1)..];
        return $"{directory}/_rels/{file}.rels";
    }

    private static MemoryStream SaveWorkbook(Workbook workbook)
    {
        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return stream;
    }

    private static XElement ReadRoot(Stream stream, string entryName)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry(entryName);
        entry.Should().NotBeNull(entryName);
        using var entryStream = entry!.Open();
        return XDocument.Load(entryStream).Root!;
    }

    private static bool PartExists(Stream stream, string entryName)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        return archive.GetEntry(entryName) is not null;
    }

    private static List<string> SchemaErrors(Stream stream)
    {
        stream.Position = 0;
        using var document = SpreadsheetDocument.Open(stream, false);
        var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
        return validator.Validate(document)
            .Where(error => error.ErrorType == ValidationErrorType.Schema)
            .Select(error => $"{error.Description} @ {error.Path?.XPath}")
            .ToList();
    }
}
