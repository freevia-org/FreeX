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
/// Regression coverage for the reverted SLICER-IO deferrals:
/// <list type="bullet">
/// <item>P7 — slicer/timeline selection/range/level was discarded on a full save of an xlsx-loaded workbook
///   (the preserved native parts replayed the original state). <see cref="XlsxSlicerTimelineStateRewriter"/>
///   now rewrites ONLY those values in place from the model.</item>
/// <item>P11 — a table slicer (SourceTableId set, no pivot binding) now emits the real x15:tableSlicerCache
///   binding on a fresh save instead of a spurious &lt;pivotTables&gt; element.</item>
/// <item>P12 — the slicerCache pivotTable/@tabId now resolves the ACTUAL host-sheet sheetId from
///   workbook.xml instead of a hardcoded "1".</item>
/// </list>
/// Each test fails pre-fix and passes post-fix, and P7 additionally asserts the re-saved package stays
/// schema valid and keeps its slicer/timeline parts — proving it does not reintroduce the round-10
/// part-clobbering regression the reverted change caused.
/// </summary>
public sealed class FreeXDeferSlicerTests
{
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // ── P7: mutate selection/range/level on a loaded workbook, re-save, reload ──────────────────

    [Fact]
    public void LoadedSlicerTimeline_SelectionRangeAndLevelSurviveResave()
    {
        var adapter = new XlsxFileAdapter();
        using var source = SaveWorkbook(CreateSlicerTimelineWorkbook());

        // Load creates a source package; the slicer/timeline/cache parts are preserved verbatim from here.
        var workbook = adapter.Load(source);
        var slicer = workbook.Slicers.Should().ContainSingle().Subject;
        var timeline = workbook.Timelines.Should().ContainSingle().Subject;
        slicer.SelectedItems.Should().Equal("East", "West");
        timeline.SelectedStartDate.Should().Be("2026-03-01");
        timeline.SelectedEndDate.Should().Be("2026-04-30");

        // A cell edit guarantees the full-save (source-package) path — the path the rewriter runs on —
        // exactly as the existing LoadedWorkbookFullSave slicer/timeline test does.
        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 8, 8), new NumberValue(99));

        // Mutate the in-memory model's selection/range/level — the values the reverted fix used to lose.
        slicer.SelectedItems.Clear();
        slicer.SelectedItems.AddRange(["North", "South", "Central"]);
        timeline.SelectedStartDate = "2026-02-15";
        timeline.SelectedEndDate = "2026-05-20";
        timeline.Level = 1;

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        // Full save path (source package present) — the path the rewriter runs on.
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        // Proof the round-10 regression is NOT reintroduced: package stays schema valid and keeps its parts.
        SchemaErrors(saved).Should().BeEmpty();
        PartExists(saved, "xl/slicers/slicer1.xml").Should().BeTrue();
        PartExists(saved, "xl/slicerCaches/slicerCache1.xml").Should().BeTrue();
        PartExists(saved, "xl/timelines/timeline1.xml").Should().BeTrue();
        PartExists(saved, "xl/timelineCaches/timelineCache1.xml").Should().BeTrue();

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var reloadedSlicer = reloaded.Slicers.Should().ContainSingle().Subject;
        var reloadedTimeline = reloaded.Timelines.Should().ContainSingle().Subject;

        reloadedSlicer.SelectedItems.Should().Equal("North", "South", "Central");
        reloadedTimeline.SelectedStartDate.Should().Be("2026-02-15");
        reloadedTimeline.SelectedEndDate.Should().Be("2026-05-20");
        reloadedTimeline.Level.Should().Be(1);
    }

    // ── P11: fresh-save a table slicer emits the x15:tableSlicerCache binding, not <pivotTables> ────

    [Fact]
    public void FreshTableSlicer_EmitsTableSlicerCacheBindingAndRoundTrips()
    {
        var workbook = new Workbook("TableSlicerFresh");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));

        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Category Slicer",
            CacheName = "Slicer_Category",
            Caption = "Category",
            StyleName = "SlicerStyleLight2",
            SourceFieldName = "Category",
            SourceTableId = 3,
            SourceTableColumnId = 5
        });

        using var saved = SaveWorkbook(workbook);

        XNamespace slicerNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
        var cacheRoot = ReadRoot(saved, "xl/slicerCaches/slicerCache1.xml");

        // The table binding is an x15:tableSlicerCache inside ext[uri={2F2917AC-...}] — never <pivotTables>.
        cacheRoot.Elements(slicerNs + "pivotTables").Should().BeEmpty();
        var tableSlicerCache = cacheRoot
            .Descendants()
            .Should()
            .ContainSingle(element => element.Name.LocalName == "tableSlicerCache")
            .Subject;
        tableSlicerCache.Attribute("tableId")!.Value.Should().Be("3");
        tableSlicerCache.Attribute("column")!.Value.Should().Be("5");
        tableSlicerCache.Ancestors()
            .Should()
            .Contain(element =>
                element.Name.LocalName == "ext" &&
                element.Attribute("uri")!.Value == "{2F2917AC-EB37-4324-AD4E-5DD8C200BD13}");

        // Round-trip: the table binding is parsed back into the model.
        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedSlicer = reloaded.Slicers.Should().ContainSingle().Subject;
        reloadedSlicer.SourceTableId.Should().Be(3);
        reloadedSlicer.SourceTableColumnId.Should().Be(5);
        reloadedSlicer.SourcePivotTableName.Should().BeNull();
    }

    // ── P12: fresh-save resolves the slicerCache tabId from the pivot host sheet's sheetId ──────────

    [Fact]
    public void FreshPivotSlicer_ResolvesTabIdFromPivotHostSheetId()
    {
        var workbook = new Workbook("PivotSlicerTabId");
        // First sheet is NOT the pivot host, so a hardcoded "1" would be wrong when the host's sheetId != 1.
        var firstSheet = workbook.AddSheet("Cover");
        firstSheet.SetCell(new CellAddress(firstSheet.Id, 1, 1), new TextValue("cover"));

        var pivotSheet = workbook.AddSheet("Pivots");
        pivotSheet.SetCell(new CellAddress(pivotSheet.Id, 1, 1), new TextValue("data"));
        pivotSheet.PivotTables.Add(new PivotTableModel { Name = "PivotTable1" });

        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Region Slicer",
            CacheName = "Slicer_Region",
            Caption = "Region",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Region"
        });

        using var saved = SaveWorkbook(workbook);

        // Read the real sheetId workbook.xml assigned to the pivot host sheet ("Pivots").
        var workbookRoot = ReadRoot(saved, "xl/workbook.xml");
        var pivotHostSheetId = workbookRoot
            .Element(WorkbookNs + "sheets")!
            .Elements(WorkbookNs + "sheet")
            .Single(element => element.Attribute("name")?.Value == "Pivots")
            .Attribute("sheetId")!
            .Value;

        XNamespace slicerNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
        var tabId = ReadRoot(saved, "xl/slicerCaches/slicerCache1.xml")
            .Descendants(slicerNs + "pivotTable")
            .Single()
            .Attribute("tabId")!
            .Value;

        tabId.Should().Be(pivotHostSheetId, "the slicerCache tabId must be the pivot host sheet's sheetId");
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static Workbook CreateSlicerTimelineWorkbook()
    {
        var workbook = new Workbook("SlicerTimelineState");
        var sheet = workbook.AddSheet("Data");
        for (uint row = 2; row <= 5; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row * 3));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 7));
        }

        var slicer = new SlicerModel
        {
            Name = "Region Slicer",
            CacheName = "Slicer_Region",
            Caption = "Region",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Region",
            StyleName = "SlicerStyleLight2"
        };
        slicer.SelectedItems.AddRange(["East", "West"]);
        workbook.Slicers.Add(slicer);

        workbook.Timelines.Add(new TimelineModel
        {
            Name = "Date Timeline",
            CacheName = "Timeline_Date",
            Caption = "Order Date",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Date",
            StyleName = "TimeSlicerStyleLight1",
            StartDate = "2026-01-01",
            EndDate = "2026-06-30",
            SelectedStartDate = "2026-03-01",
            SelectedEndDate = "2026-04-30"
        });

        return workbook;
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
