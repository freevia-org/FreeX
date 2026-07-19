using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-49 fix bucket "io-b" regression tests for Excel's "linked slicers"/"linked timelines" -- two
/// widgets sharing one slicerCache/timelineCache part (e.g. the same widget copied to another sheet via
/// Filter Connections). <see cref="XlsxSlicerTimelineStateRewriter"/> resolved, per shared cache, exactly
/// ONE bound SlicerModel/TimelineModel to patch the cache from -- whichever widget name happened to be
/// enumerated first while scanning the package -- regardless of which widget the user actually just
/// edited. When the edited widget wasn't the first one found, its selection change was silently discarded
/// on save and the shared cache reverted to (or stayed at) the untouched sibling's stale state.
/// <list type="bullet">
/// <item>R49-io-slicer-timeline-3-2 -- two linked SLICER widgets sharing one slicerCache.</item>
/// <item>R49-io-slicer-timeline-3-3 -- two linked TIMELINE widgets sharing one timelineCache.</item>
/// </list>
/// </summary>
public sealed class R49_LinkedSlicerTimelineSharedCacheTests
{
    // ── R49-io-slicer-timeline-3-2 (linked slicers) ─────────────────────────────────────────────

    [Fact]
    public void LinkedSlicers_EditingSecondEnumeratedWidget_IsNotRevertedBySharedCache()
    {
        using var source = SaveWorkbook(BuildPivotWorkbookWithLinkedRegionSlicers(firstSelectedItem: "East"));

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        loaded.Slicers.Should().HaveCount(2);
        loaded.Slicers.Should().OnlyContain(slicer => slicer.CacheName == "Slicer_Region");
        loaded.Slicers.Should().OnlyContain(slicer => slicer.SelectedItems.SequenceEqual(new[] { "East" }),
            "both linked widgets must load the shared cache's current selection identically");

        // The user only touches the SECOND-enumerated widget ("Region Slicer B") -- exactly like
        // SetSlicerSelectionCommand.Apply, which mutates only the named SlicerModel it looked up.
        var editedSlicer = loaded.Slicers.Single(slicer => slicer.Name == "Region Slicer B");
        editedSlicer.SelectedItems.Clear();
        editedSlicer.SelectedItems.Add("West");
        editedSlicer.SelectionCaptured = true;
        // "Region Slicer A" is left exactly as loaded (untouched, stale "East").

        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 9, 9), new NumberValue(1));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        ReadSlicerCacheSelectedItems(saved, "xl/slicerCaches/slicerCache1.xml").Should().Equal(["West"],
            "the user's live edit on the second-enumerated linked slicer must win over the untouched " +
            "first-enumerated sibling's stale selection, not be silently reverted");

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        reloaded.Slicers.Should().OnlyContain(slicer => slicer.SelectedItems.SequenceEqual(new[] { "West" }),
            "both linked widgets share the cache, so the edit must be visible on reload from either name");
    }

    [Fact]
    public void LinkedSlicers_EditingFirstEnumeratedWidget_StillSavesCorrectly()
    {
        // Sibling/no-regression case: editing the FIRST-enumerated linked widget was already the
        // (accidentally) correct path pre-fix -- must remain correct after preferring a captured model.
        using var source = SaveWorkbook(BuildPivotWorkbookWithLinkedRegionSlicers(firstSelectedItem: "East"));

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);

        var editedSlicer = loaded.Slicers.Single(slicer => slicer.Name == "Region Slicer A");
        editedSlicer.SelectedItems.Clear();
        editedSlicer.SelectedItems.Add("West");
        editedSlicer.SelectionCaptured = true;
        // "Region Slicer B" is left exactly as loaded (untouched, stale "East").

        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 9, 9), new NumberValue(1));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        ReadSlicerCacheSelectedItems(saved, "xl/slicerCaches/slicerCache1.xml").Should().Equal(["West"],
            "editing the first-enumerated linked widget must still save its selection correctly");
    }

    // ── R49-io-slicer-timeline-3-3 (linked timelines) ───────────────────────────────────────────

    [Fact]
    public void LinkedTimelines_EditingSecondEnumeratedWidget_IsNotRevertedBySharedCache()
    {
        using var source = SaveWorkbook(BuildPivotWorkbookWithLinkedDateTimelines(
            firstSelectedStart: "2026-01-01", firstSelectedEnd: "2026-03-31"));

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        loaded.Timelines.Should().HaveCount(2);
        loaded.Timelines.Should().OnlyContain(timeline => timeline.CacheName == "Timeline_Date");
        loaded.Timelines.Should().OnlyContain(timeline =>
            timeline.SelectedStartDate == "2026-01-01" && timeline.SelectedEndDate == "2026-03-31",
            "both linked widgets must load the shared cache's current selected range identically");

        // The user only touches the SECOND-enumerated widget -- exactly like SetTimelineRangeCommand.Apply,
        // which mutates only the named TimelineModel it looked up.
        var editedTimeline = loaded.Timelines.Single(timeline => timeline.Name == "Date Timeline B");
        editedTimeline.SelectedStartDate = "2026-04-01";
        editedTimeline.SelectedEndDate = "2026-06-30";
        // "Date Timeline A" is left exactly as loaded (untouched, stale range).

        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 9, 9), new NumberValue(1));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        var (start, end) = ReadTimelineCacheSelectedRange(saved, "xl/timelineCaches/timelineCache1.xml");
        start.Should().Be("2026-04-01", "the user's live date-range edit on the second-enumerated linked " +
            "timeline must win over the untouched first-enumerated sibling's stale range, not be silently reverted");
        end.Should().Be("2026-06-30");

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        reloaded.Timelines.Should().OnlyContain(timeline =>
            timeline.SelectedStartDate == "2026-04-01" && timeline.SelectedEndDate == "2026-06-30",
            "both linked widgets share the cache, so the edit must be visible on reload from either name");
    }

    [Fact]
    public void LinkedTimelines_EditingFirstEnumeratedWidget_StillSavesCorrectly()
    {
        // Sibling/no-regression case: editing the FIRST-enumerated linked widget was already the
        // (accidentally) correct path pre-fix -- must remain correct after preferring the differing model.
        using var source = SaveWorkbook(BuildPivotWorkbookWithLinkedDateTimelines(
            firstSelectedStart: "2026-01-01", firstSelectedEnd: "2026-03-31"));

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);

        var editedTimeline = loaded.Timelines.Single(timeline => timeline.Name == "Date Timeline A");
        editedTimeline.SelectedStartDate = "2026-04-01";
        editedTimeline.SelectedEndDate = "2026-06-30";
        // "Date Timeline B" is left exactly as loaded (untouched, stale range).

        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 9, 9), new NumberValue(1));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        var (start, end) = ReadTimelineCacheSelectedRange(saved, "xl/timelineCaches/timelineCache1.xml");
        start.Should().Be("2026-04-01", "editing the first-enumerated linked widget must still save its range correctly");
        end.Should().Be("2026-06-30");
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static Workbook BuildPivotWorkbookBase(out PivotTableModel pivot)
    {
        var workbook = new Workbook("LinkedSlicerTimelineR49");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Date"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("2026-01-15"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("2026-05-15"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(20));

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Data",
            SourceReference = "A1:C3"
        };
        cache.Fields.Add(new PivotCacheFieldModel("Region", ContainsString: true, SharedItems: ["East", "West"]));
        cache.Fields.Add(new PivotCacheFieldModel("Date", ContainsDate: true));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
        workbook.PivotCaches.Add(cache);

        pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 6, 1), new CellAddress(sheet.Id, 9, 2))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        return workbook;
    }

    /// <summary>
    /// Two SlicerModels ("Region Slicer A" then "Region Slicer B") that share one CacheName --
    /// XlsxSlicerTimelineWriter's "reuse an already-written cache part for slicers that share the same
    /// CacheName" path writes a SINGLE slicerCache part for both, exactly mirroring Excel's linked-slicer
    /// package shape (Filter Connections / copying a slicer to another sheet). Only the FIRST slicer
    /// processed authors the cache's initial selection.
    /// </summary>
    private static Workbook BuildPivotWorkbookWithLinkedRegionSlicers(string firstSelectedItem)
    {
        var workbook = BuildPivotWorkbookBase(out _);
        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Region Slicer A",
            CacheName = "Slicer_Region",
            Caption = "Region",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Region",
            SelectedItems = { firstSelectedItem }
        });
        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Region Slicer B",
            CacheName = "Slicer_Region",
            Caption = "Region",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Region"
        });
        return workbook;
    }

    /// <summary>
    /// Two TimelineModels ("Date Timeline A" then "Date Timeline B") that share one CacheName --
    /// XlsxSlicerTimelineWriter's equivalent "reuse an already-written cache part for timelines that share
    /// the same CacheName" path writes a SINGLE timelineCache part for both, mirroring Excel's
    /// linked-timeline package shape. Only the FIRST timeline processed authors the cache's initial
    /// selected range.
    /// </summary>
    private static Workbook BuildPivotWorkbookWithLinkedDateTimelines(string firstSelectedStart, string firstSelectedEnd)
    {
        var workbook = BuildPivotWorkbookBase(out _);
        workbook.Timelines.Add(new TimelineModel
        {
            Name = "Date Timeline A",
            CacheName = "Timeline_Date",
            Caption = "Date",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Date",
            StartDate = "2026-01-01",
            EndDate = "2026-12-31",
            SelectedStartDate = firstSelectedStart,
            SelectedEndDate = firstSelectedEnd
        });
        workbook.Timelines.Add(new TimelineModel
        {
            Name = "Date Timeline B",
            CacheName = "Timeline_Date",
            Caption = "Date",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Date",
            StartDate = "2026-01-01",
            EndDate = "2026-12-31"
        });
        return workbook;
    }

    private static string[] ReadSlicerCacheSelectedItems(Stream package, string cacheEntryName)
    {
        var root = ReadRoot(package, cacheEntryName);
        return root.Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "selectedItem", System.StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Attribute("value")?.Value ?? "")
            .ToArray();
    }

    private static (string? Start, string? End) ReadTimelineCacheSelectedRange(Stream package, string cacheEntryName)
    {
        var root = ReadRoot(package, cacheEntryName);
        return (root.Attribute("selectedStartDate")?.Value, root.Attribute("selectedEndDate")?.Value);
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
}
