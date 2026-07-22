using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R69-io-slicer-timeline-6-2: on the hasSourcePackage save path,
/// <c>XlsxSlicerTimelineStateRewriter</c> is the ONLY rewriter that touches the preserved
/// slicerCache/timelineCache parts (the fresh-writer's <c>SaveSlicerTimelines</c>, which emits the
/// <c>&lt;pivotTable name="..."/&gt;</c> binding, is gated to <c>!hasSourcePackage</c>). Before the fix, a
/// pivot table rename (<c>RenamePivotTableCommand</c> updating <see cref="SlicerModel.SourcePivotTableName"/>/
/// <see cref="TimelineModel.SourcePivotTableName"/>) was never reflected into the saved cache part's
/// <c>&lt;pivotTable name="..."/&gt;</c> attribute, silently breaking the slicer/timeline-to-pivot connection
/// on reopen.
/// </summary>
public sealed class FreeXR69SlicerTimelinePivotRenameTests
{
    [Fact]
    public void PatchSave_AfterPivotTableRename_RewritesSlicerCachePivotTableBindingToNewName()
    {
        var workbook = BuildWorkbookWithSlicerAndTimeline();
        using var source = SaveWorkbook(workbook);

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);

        var pivot = loaded.GetSheetAt(0).PivotTables.Single(p => p.Name == "PT1");
        pivot.Name = "PT2";
        var slicer = loaded.Slicers.Single();
        slicer.SourcePivotTableName = "PT2";

        // Trivial cell edit to force the hasSourcePackage save (mirrors sibling tests' pattern).
        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 20, 20), new NumberValue(1));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        ReadPivotTableName(saved, "xl/slicerCaches/slicerCache1.xml").Should().Be("PT2",
            "the saved slicerCache pivotTable binding must follow the model's renamed pivot table");
    }

    [Fact]
    public void PatchSave_AfterPivotTableRename_RewritesTimelineCachePivotTableBindingToNewName()
    {
        var workbook = BuildWorkbookWithSlicerAndTimeline();
        using var source = SaveWorkbook(workbook);

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);

        var pivot = loaded.GetSheetAt(0).PivotTables.Single(p => p.Name == "PT1");
        pivot.Name = "PT2";
        var timeline = loaded.Timelines.Single();
        timeline.SourcePivotTableName = "PT2";

        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 20, 20), new NumberValue(1));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        ReadPivotTableName(saved, "xl/timelineCaches/timelineCache1.xml").Should().Be("PT2",
            "the saved timelineCache pivotTable binding must follow the model's renamed pivot table");
    }

    [Fact]
    public void PatchSave_WithoutPivotTableRename_LeavesSlicerAndTimelineCachePivotTableBindingByteUnchanged()
    {
        var workbook = BuildWorkbookWithSlicerAndTimeline();
        using var source = SaveWorkbook(workbook);
        var originalSlicerCacheXml = ReadEntryText(source, "xl/slicerCaches/slicerCache1.xml");
        var originalTimelineCacheXml = ReadEntryText(source, "xl/timelineCaches/timelineCache1.xml");
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);

        // No rename this time -- only an unrelated trivial cell edit to force the hasSourcePackage save.
        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 20, 20), new NumberValue(1));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        ReadPivotTableName(saved, "xl/slicerCaches/slicerCache1.xml").Should().Be("PT1");
        ReadPivotTableName(saved, "xl/timelineCaches/timelineCache1.xml").Should().Be("PT1");
        ReadEntryText(saved, "xl/slicerCaches/slicerCache1.xml").Should().Be(originalSlicerCacheXml,
            "an un-renamed pivot table must leave the preserved slicerCache part byte-unchanged");
        ReadEntryText(saved, "xl/timelineCaches/timelineCache1.xml").Should().Be(originalTimelineCacheXml,
            "an un-renamed pivot table must leave the preserved timelineCache part byte-unchanged");
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static Workbook BuildWorkbookWithSlicerAndTimeline()
    {
        var workbook = new Workbook("PivotRenameSlicerTimelineR69");
        var sheet = workbook.AddSheet("Data1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Date"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Amount"));
        string[] regions = ["East", "West", "North"];
        for (var i = 0; i < regions.Length; i++)
        {
            var row = (uint)(i + 2);
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue(regions[i]));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), DateTimeValue.FromDateTime(new System.DateTime(2026, 1, i + 1)));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue((i + 1) * 10));
        }

        var cache = new PivotCacheModel { CacheId = 1, SourceType = PivotCacheSourceType.WorksheetRange, SourceSheetName = "Data1", SourceReference = "A1:C4" };
        cache.Fields.Add(new PivotCacheFieldModel("Region", ContainsString: true, SharedItems: ["East", "West", "North"]));
        cache.Fields.Add(new PivotCacheFieldModel("Date", ContainsDate: true));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PT1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 6, 1), new CellAddress(sheet.Id, 9, 2))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Region Slicer",
            CacheName = "Slicer_Region",
            Caption = "Region",
            SourcePivotTableName = "PT1",
            SourceFieldName = "Region",
            StyleName = "SlicerStyleLight2"
        });

        workbook.Timelines.Add(new TimelineModel
        {
            Name = "Date Timeline",
            CacheName = "Timeline_Date",
            Caption = "Date",
            SourcePivotTableName = "PT1",
            SourceFieldName = "Date"
        });

        return workbook;
    }

    private static string? ReadPivotTableName(MemoryStream package, string entryPath)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry(entryPath);
        entry.Should().NotBeNull();
        using var entryStream = entry!.Open();
        var xml = XDocument.Load(entryStream);
        var name = xml.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "pivotTable")?
            .Attribute("name")?.Value;
        package.Position = 0;
        return name;
    }

    private static string ReadEntryText(MemoryStream package, string entryPath)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry(entryPath);
        entry.Should().NotBeNull();
        using var entryStream = entry!.Open();
        using var reader = new StreamReader(entryStream);
        var text = reader.ReadToEnd();
        package.Position = 0;
        return text;
    }

    private static MemoryStream SaveWorkbook(Workbook workbook)
    {
        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return stream;
    }
}
