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
/// Round-37 fix bucket "slicer-timeline" regression tests.
/// <list type="bullet">
/// <item>R37-io-slicer-timeline-1 — a slicer/timeline added (AddSlicerCommand/AddTimelineCommand) to an
///   already-loaded (source-preserved) workbook was silently dropped on save: the only part-authoring writer
///   (<c>XlsxSlicerTimelineWriter.SaveSlicerTimelines</c>) is gated to the no-source-package path, and the
///   source-preserved path's <see cref="XlsxSlicerTimelineStateRewriter"/> only patched EXISTING parts.
///   <see cref="XlsxSlicerTimelineStateRewriter.Save"/> now also authors parts for any control whose name
///   isn't already represented in the archive, without touching any already-preserved control's parts.</item>
/// <item>R37-io-slicer-timeline-2 — a TABLE slicer's selection change updated only FreeX's private extLst,
///   never the native <c>&lt;i s="1"&gt;</c> flags real Excel reads, because the native-flag rewrite's
///   caption resolver only ever looked at pivot caches (which a table slicer never has). It now also
///   resolves captions from the referenced structured table's column distinct values.</item>
/// </list>
/// </summary>
public sealed class FreeXR37SlicerTimelineTests
{
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace SlicerXmlNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";

    // ── R37-io-slicer-timeline-1 ─────────────────────────────────────────────────────────────────

    [Fact]
    public void NewSlicerAddedToLoadedWorkbook_ResaveWritesSlicerAndSlicerCacheParts()
    {
        using var source = SaveWorkbook(BuildPivotWorkbookWithoutControls());

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        loaded.Slicers.Should().BeEmpty("no slicer exists yet in the source package");

        // Mimics AddSlicerCommand.Apply: a brand-new SlicerModel with no PackagePart, added directly to
        // the in-memory model of an already-loaded (source-preserved) workbook.
        loaded.Slicers.Add(new SlicerModel
        {
            Name = "Region Slicer",
            CacheName = "Slicer_Region",
            Caption = "Region",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Region"
        });

        // Force the full-save (source package preserved) path with a trivial cell edit.
        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 9, 9), new NumberValue(1));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        SchemaErrors(saved).Should().BeEmpty();
        PartExists(saved, "xl/slicers/slicer1.xml").Should().BeTrue(
            "the newly-added slicer must be written even though it never existed in the source package");
        PartExists(saved, "xl/slicerCaches/slicerCache1.xml").Should().BeTrue();

        var workbookRelsRoot = ReadRoot(saved, "xl/_rels/workbook.xml.rels");
        var hasSlicerCacheRelationship = workbookRelsRoot.Elements(PackageRelNs + "Relationship")
            .Any(rel => (rel.Attribute("Type")?.Value ?? "").Contains("slicerCache"));
        hasSlicerCacheRelationship.Should().BeTrue("the workbook must carry a relationship to the new slicerCache part");

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var reloadedSlicer = reloaded.Slicers.Should().ContainSingle().Subject;
        reloadedSlicer.Name.Should().Be("Region Slicer");
        reloadedSlicer.SourcePivotTableName.Should().Be("PivotTable1");
        reloadedSlicer.SourceFieldName.Should().Be("Region");
    }

    [Fact]
    public void NewTimelineAddedToLoadedWorkbook_ResaveWritesTimelineAndTimelineCacheParts()
    {
        using var source = SaveWorkbook(BuildPivotWorkbookWithoutControls());

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        loaded.Timelines.Should().BeEmpty("no timeline exists yet in the source package");

        // Mimics AddTimelineCommand.Apply.
        loaded.Timelines.Add(new TimelineModel
        {
            Name = "Date Timeline",
            CacheName = "Timeline_Date",
            Caption = "Order Date",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Date",
            StartDate = "2026-01-01",
            EndDate = "2026-06-30"
        });

        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 9, 9), new NumberValue(1));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        SchemaErrors(saved).Should().BeEmpty();
        PartExists(saved, "xl/timelines/timeline1.xml").Should().BeTrue(
            "the newly-added timeline must be written even though it never existed in the source package");
        PartExists(saved, "xl/timelineCaches/timelineCache1.xml").Should().BeTrue();

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var reloadedTimeline = reloaded.Timelines.Should().ContainSingle().Subject;
        reloadedTimeline.Name.Should().Be("Date Timeline");
        reloadedTimeline.SourcePivotTableName.Should().Be("PivotTable1");
        reloadedTimeline.StartDate.Should().Be("2026-01-01");
        reloadedTimeline.EndDate.Should().Be("2026-06-30");
    }

    // ── R37-io-slicer-timeline-1 sibling: adding a new control must not disturb an already-preserved one ──

    [Fact]
    public void ExistingPreservedSlicer_PlusNewlyAddedSlicer_KeepsExistingSlicerCacheUntouched()
    {
        using var source = SaveWorkbook(BuildPivotWorkbookWithRegionSlicer());

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        loaded.Slicers.Should().ContainSingle(slicer => slicer.Name == "Region Slicer");

        var originalCacheXml = ReadRoot(source, "xl/slicerCaches/slicerCache1.xml");

        // Add a SECOND, brand-new slicer bound to a different field on the same pivot table.
        loaded.Slicers.Add(new SlicerModel
        {
            Name = "Status Slicer",
            CacheName = "Slicer_Status",
            Caption = "Status",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Status"
        });

        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 9, 9), new NumberValue(1));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        var resavedOriginalCacheXml = ReadRoot(saved, "xl/slicerCaches/slicerCache1.xml");
        XNode.DeepEquals(originalCacheXml, resavedOriginalCacheXml).Should().BeTrue(
            "an already-preserved slicer's cache part must stay byte-for-byte untouched just because a " +
            "DIFFERENT new slicer was added in the same save");

        PartExists(saved, "xl/slicers/slicer2.xml").Should().BeTrue();
        PartExists(saved, "xl/slicerCaches/slicerCache2.xml").Should().BeTrue();

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        reloaded.Slicers.Should().HaveCount(2);
        reloaded.Slicers.Should().Contain(slicer => slicer.Name == "Region Slicer");
        reloaded.Slicers.Should().Contain(slicer => slicer.Name == "Status Slicer" && slicer.SourceFieldName == "Status");
    }

    // ── R84-io-slicer-append-tabular ─────────────────────────────────────────────────────────────
    //
    // A pivot slicer ADDED to an already-loaded (source-preserved) workbook used to be authored by
    // AppendNewControls WITHOUT the native <data><tabular><items> item list -- only the FreeX-private
    // fx: selectedItems extLst -- so on reload it rendered with zero item buttons (the same symptom
    // R44-io-pivot-filter-page-3-2 fixed for the fresh save path). AppendNewSlicers now emits the same
    // native list via the shared XlsxPivotSlicerCacheData builder, including the required pivotCacheId
    // (R83-io-slicer-tabular-pivotcacheid).

    [Fact]
    public void NewPivotSlicerAddedToLoadedWorkbook_WritesNativeTabularItemsWithRequiredPivotCacheId()
    {
        // Seed: a workbook that already has a pivot cache field carrying SharedItems + a pivot table + a
        // slicer, saved once to a real source package, then LOADED (so the whole thing goes through the
        // source-preserved / FullSave path on the second save).
        using var source = SaveWorkbook(BuildPivotWorkbookWithRegionSlicer());

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        loaded.Slicers.Should().ContainSingle(slicer => slicer.Name == "Region Slicer");

        // Add a brand-new pivot slicer (mimics AddSlicerCommand) bound to a DIFFERENT field on the same
        // pivot table, whose shared items ("Open"/"Closed") resolve on the loaded model.
        loaded.Slicers.Add(new SlicerModel
        {
            Name = "Status Slicer",
            CacheName = "Slicer_Status",
            Caption = "Status",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Status"
        });

        // Force the source-preserved (FullSave) path with a trivial cell edit.
        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 9, 9), new NumberValue(1));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        // (a) The whole package must validate clean under Microsoft365 -- proving the newly-authored
        // <tabular> carries the required pivotCacheId (missing it trips "The required attribute
        // 'pivotCacheId' is missing" on /x14:slicerCacheDefinition/x14:data/x14:tabular).
        SchemaErrors(saved).Should().BeEmpty(
            "a pivot slicer added to a loaded workbook must author a schema-clean native tabular cache");

        var newCachePath = ResolveSlicerCachePath(saved, "Slicer_Status");
        var tabular = ReadRoot(saved, newCachePath).Descendants(SlicerXmlNs + "tabular").Should().ContainSingle().Subject;
        tabular.Attribute("pivotCacheId")!.Value.Should().Be("1",
            "the native tabular slicer cache's pivotCacheId must be the bound pivot cache's CacheId (1)");

        // (b) The native <items> list must carry one <i> per shared item, all selected (no explicit
        // selection recorded == the unfiltered '(All)' state), and round-trip into SlicerModel.CacheItems.
        var items = ReadNativeCacheItems(saved, newCachePath);
        items.Should().HaveCount(2, "the bound field carries two shared items (Open, Closed)");
        items.Should().OnlyContain(item => item.Selected,
            "a newly-added slicer with no explicit selection starts with every tile selected");

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var reloadedSlicer = reloaded.Slicers.Single(slicer => slicer.Name == "Status Slicer");
        reloadedSlicer.CacheItems.Should().HaveCount(2,
            "SlicerItemResolver gates on CacheItems.Count > 0 -- an empty list means zero item buttons");
    }

    [Fact]
    public void NewPivotSlicerAddedToLoadedWorkbook_WithExplicitSelection_ValidatesCleanAndSelectionRoundTrips()
    {
        using var source = SaveWorkbook(BuildPivotWorkbookWithRegionSlicer());

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);

        var newSlicer = new SlicerModel
        {
            Name = "Status Slicer",
            CacheName = "Slicer_Status",
            Caption = "Status",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Status"
        };
        newSlicer.SelectedItems.Add("Open"); // shared-item index 0
        loaded.Slicers.Add(newSlicer);

        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 9, 9), new NumberValue(1));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        SchemaErrors(saved).Should().BeEmpty(
            "authoring the native tabular with the required pivotCacheId must keep the package schema-clean");

        var newCachePath = ResolveSlicerCachePath(saved, "Slicer_Status");
        var items = ReadNativeCacheItems(saved, newCachePath);
        items.Should().ContainSingle(item => item.Selected).Which.Index.Should().Be(0,
            "only 'Open' (shared-item index 0) was explicitly selected");
        items.Where(item => item.Index != 0).Should().OnlyContain(item => !item.Selected);

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var reloadedSlicer = reloaded.Slicers.Single(slicer => slicer.Name == "Status Slicer");
        reloadedSlicer.CacheItems.Should().HaveCount(2,
            "both shared items must round-trip as cache items so the reloaded slicer renders its tiles");
        reloadedSlicer.CacheItems.Single(item => item.IsSelected).Index.Should().Be(0,
            "the reloaded slicer must still report only 'Open' (index 0) as the selected tile");
    }

    // ── R37-io-slicer-timeline-2 ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TableSlicerSelectionChange_ResaveRewritesNativeCacheItemSelectedFlags()
    {
        // Build + save a workbook with a structured table ("Category" distinct values, in first-occurrence
        // order: Widget, Gadget, Gizmo) and a TABLE slicer (SourceTableId/SourceTableColumnId set, no pivot
        // binding), then inject a native <data><tabular><items> selection (as a real Excel-authored file
        // would carry) selecting "Widget" (x=0, s="1").
        using var source = SaveWorkbook(BuildTableSlicerWorkbook());
        InjectNativeTabularSelection(source, "xl/slicerCaches/slicerCache1.xml", selectedIndex: 0);

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var slicer = loaded.Slicers.Should().ContainSingle().Subject;
        slicer.SourceTableId.Should().Be(9);
        slicer.SourceTableColumnId.Should().Be(11);

        // Change the selection in FreeX to "Gadget" (index 1) — this only touches SelectedItems/
        // SelectionCaptured, exactly like SetSlicerSelectionCommand.ApplyTableSlicer does; the native cache
        // items are never mutated by the command layer.
        slicer.SelectedItems.Clear();
        slicer.SelectedItems.Add("Gadget");
        slicer.SelectionCaptured = true;

        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 9, 9), new NumberValue(1));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        var items = ReadNativeCacheItems(saved, "xl/slicerCaches/slicerCache1.xml");
        items.Should().ContainSingle(item => item.Selected).Which.Index.Should().Be(1,
            "Excel reads a table slicer's selection from the native <i s=\"1\"> flags too, so a FreeX " +
            "selection change must be reflected there, not just in the private extLst");
        items.Where(item => item.Index != 1).Should().OnlyContain(item => !item.Selected);
    }

    [Fact]
    public void HeaderlessTableSlicerSelectionChange_ResaveKeepsFirstDataRowInNativeIndexSpace()
    {
        using var source = SaveWorkbook(BuildTableSlicerWorkbook(headerRowCount: 0));
        InjectNativeTabularSelection(
            source,
            "xl/slicerCaches/slicerCache1.xml",
            selectedIndex: 0,
            itemCount: 4);

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var slicer = loaded.Slicers.Should().ContainSingle().Subject;
        slicer.SelectedItems.Clear();
        slicer.SelectedItems.Add("Widget");
        slicer.SelectionCaptured = true;

        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 9, 9), new NumberValue(1));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        var items = ReadNativeCacheItems(saved, "xl/slicerCaches/slicerCache1.xml");
        items.Should().HaveCount(4);
        items.Should().ContainSingle(item => item.Selected).Which.Index.Should().Be(1,
            "a headerless table's first range row is data at index 0, so Widget remains index 1");
    }

    [Fact]
    public void TableSlicerUnchangedSelection_ResaveLeavesNativeCacheItemFlagsUntouched()
    {
        // Sibling/no-regression case: when the model never captured a selection change
        // (SelectionCaptured stays false, mirroring "loaded but the user never touched the slicer"), the
        // native flags must be left exactly as preserved -- proving the new table-caption resolution path
        // doesn't turn every re-save of a table slicer into an (incorrect) rewrite.
        using var source = SaveWorkbook(BuildTableSlicerWorkbook());
        InjectNativeTabularSelection(source, "xl/slicerCaches/slicerCache1.xml", selectedIndex: 0);

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        loaded.Slicers.Should().ContainSingle().Subject.SelectionCaptured.Should().BeFalse();

        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 9, 9), new NumberValue(1));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        var items = ReadNativeCacheItems(saved, "xl/slicerCaches/slicerCache1.xml");
        items.Should().ContainSingle(item => item.Selected).Which.Index.Should().Be(0,
            "an untouched selection must leave the preserved native flags exactly as loaded");
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static Workbook BuildPivotWorkbookWithoutControls()
    {
        var workbook = new Workbook("PivotNoControlsR37");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Status"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Open"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Closed"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(20));

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Data",
            SourceReference = "A1:C3"
        };
        cache.Fields.Add(new PivotCacheFieldModel("Region", ContainsString: true, SharedItems: ["East", "West"]));
        cache.Fields.Add(new PivotCacheFieldModel("Status", ContainsString: true, SharedItems: ["Open", "Closed"]));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
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

    private static Workbook BuildPivotWorkbookWithRegionSlicer()
    {
        var workbook = BuildPivotWorkbookWithoutControls();
        var slicer = new SlicerModel
        {
            Name = "Region Slicer",
            CacheName = "Slicer_Region",
            Caption = "Region",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Region"
        };
        workbook.Slicers.Add(slicer);
        return workbook;
    }

    private static Workbook BuildTableSlicerWorkbook(int? headerRowCount = null)
    {
        var workbook = new Workbook("TableSlicerNativeSelectionR37");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Widget"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Gadget"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Gizmo"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));

        var table = new StructuredTableModel
        {
            Id = 9,
            Name = "CategoryTable",
            DisplayName = "CategoryTable",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            HasAutoFilter = true,
            HeaderRowCount = headerRowCount
        };
        table.Columns.Add(new StructuredTableColumnModel(11, "Category"));
        table.Columns.Add(new StructuredTableColumnModel(12, "Amount"));
        sheet.StructuredTables.Add(table);

        var slicer = new SlicerModel
        {
            Name = "Category Slicer",
            CacheName = "Slicer_Category",
            Caption = "Category",
            SourceFieldName = "Category",
            SourceTableId = 9,
            SourceTableColumnId = 11
        };
        workbook.Slicers.Add(slicer);

        return workbook;
    }

    /// <summary>
    /// Rewrites the freshly-saved slicerCache part to also carry the NATIVE
    /// &lt;data&gt;&lt;tabular&gt;&lt;items&gt;&lt;i x="N" s="1"/&gt; selection form (what a real Excel-authored
    /// file stores), selecting only <paramref name="selectedIndex"/> among <paramref name="itemCount"/>
    /// items. The fresh FreeX writer
    /// never emits this native form itself, so this simulates "loaded a real Excel workbook".
    /// </summary>
    private static void InjectNativeTabularSelection(
        MemoryStream package,
        string cacheEntryName,
        int selectedIndex,
        int itemCount = 3)
    {
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry(cacheEntryName)!;
            XDocument xml;
            using (var entryStream = entry.Open())
                xml = XDocument.Load(entryStream);

            var root = xml.Root!;
            var data = new XElement(SlicerXmlNs + "data",
                new XElement(SlicerXmlNs + "tabular",
                    new XElement(SlicerXmlNs + "items",
                        Enumerable.Range(0, itemCount).Select(index =>
                            new XElement(
                                SlicerXmlNs + "i",
                                new XAttribute("x", index),
                                selectedIndex == index ? new XAttribute("s", "1") : null)))));
            root.Add(data);

            entry.Delete();
            var newEntry = archive.CreateEntry(cacheEntryName);
            using var writeStream = newEntry.Open();
            xml.Save(writeStream);
        }

        package.Position = 0;
    }

    /// <summary>
    /// Locates the <c>xl/slicerCaches/*.xml</c> part whose root <c>slicerCacheDefinition/@name</c> equals
    /// <paramref name="cacheName"/> -- used to find the brand-new appended slicer's cache without hard-coding
    /// the allocated <c>slicerCacheN.xml</c> index.
    /// </summary>
    private static string ResolveSlicerCachePath(MemoryStream package, string cacheName)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        foreach (var entry in archive.Entries.Where(e =>
                     e.FullName.StartsWith("xl/slicerCaches/", StringComparison.OrdinalIgnoreCase) &&
                     e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            using var entryStream = entry.Open();
            var name = XDocument.Load(entryStream).Root?.Attribute("name")?.Value;
            if (string.Equals(name, cacheName, StringComparison.OrdinalIgnoreCase))
                return entry.FullName;
        }

        throw new InvalidOperationException($"No slicerCache part named '{cacheName}' was found in the package.");
    }

    private static (int Index, bool Selected)[] ReadNativeCacheItems(MemoryStream package, string cacheEntryName)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry(cacheEntryName);
        entry.Should().NotBeNull();
        using var entryStream = entry!.Open();
        var xml = XDocument.Load(entryStream);
        return xml.Descendants()
            .Where(element => element.Name.LocalName == "i")
            .Select(element => (
                Index: int.Parse(element.Attribute("x")!.Value),
                Selected: element.Attribute("s")?.Value == "1"))
            .ToArray();
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
