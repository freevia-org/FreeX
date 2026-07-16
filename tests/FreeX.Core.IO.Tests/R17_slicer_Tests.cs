using System;
using System.Collections.Generic;
using System.Globalization;
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
/// Round-17 fix bucket regression tests for the slicer/timeline state rewriter and the native
/// .fxl slicer round-trip:
/// <list type="bullet">
/// <item>R17-slicer-timeline-cache-1 — <see cref="XlsxSlicerTimelineStateRewriter"/>'s cache-item
///   caption normalization must key off the per-item <c>SharedItemKinds</c> char, exactly like
///   <c>FreeX.Core.Commands.SlicerItemResolver</c> does, so a MIXED-type pivot field's date/number
///   item still resolves to the same caption both places. Before the fix, keying only on the
///   field-level Contains* flags (which require EXCLUSIVELY one kind) left every item's raw
///   ISO/invariant string un-formatted on a mixed field, silently clearing a selected date/number
///   tile's native <c>s="1"</c> flag on a full save.</item>
/// <item>R17-slicer-timeline-cache-3 — clearing a timeline filter (both selected dates set to null)
///   must remove the preserved native <c>&lt;state&gt;&lt;selection&gt;</c> element entirely rather
///   than leave it with neither of its required startDate/endDate attributes (CT_TimelineRange),
///   which Excel repairs/drops.</item>
/// <item>R17-slicer-timeline-cache-2 — <see cref="NativeJsonAdapter"/>'s slicer DTO must carry
///   <see cref="SlicerModel.CacheItems"/> (+ <see cref="SlicerModel.SelectionCaptured"/>) so a pivot
///   slicer's available tiles survive a native .fxl save/load round trip.</item>
/// </list>
/// </summary>
public sealed class R17_slicer_Tests
{
    private static readonly XNamespace SlicerNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private static readonly XNamespace TimelineNs = "http://schemas.microsoft.com/office/spreadsheetml/2010/11/main";

    // ── R17-slicer-timeline-cache-1 ──────────────────────────────────────────────────────────

    [Fact]
    public void MixedDateTextField_SelectedDateTile_SurvivesSourcePreservedSave()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            var workbook = BuildMixedFieldPivotSlicerWorkbook();
            using var source = SaveWorkbook(workbook);
            // Native selection: index 1 (the date item "2026-01-05T00:00:00") carries s="1", as a real
            // Excel-authored slicer cache would.
            InjectNativeTabularSelection(source, selectedIndex: 1);

            var adapter = new XlsxFileAdapter();
            var loaded = adapter.Load(source);
            var slicer = loaded.Slicers.Should().ContainSingle().Subject;

            // Mirror what FreeX.Core.Commands.SlicerItemResolver would have resolved for a 'd'-kind
            // item with no grouping: DateTime.ToShortDateString() under the current culture.
            var expectedCaption = new DateTime(2026, 1, 5).ToShortDateString();
            slicer.SelectedItems.Add(expectedCaption);
            slicer.SelectionCaptured = true;

            // Force the full-save (source package preserved) path with a trivial cell edit.
            var sheet = loaded.GetSheetAt(0);
            sheet.SetCell(new CellAddress(sheet.Id, 9, 9), new NumberValue(1));

            using var saved = new MemoryStream();
            adapter.Save(loaded, saved);
            adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

            var items = ReadNativeCacheItems(saved);
            // Before the fix: the rewriter's field-level Contains* gate fails on a mixed field (both
            // ContainsString and ContainsDate are true), so the raw ISO string is compared against the
            // resolver-formatted caption and never matches — every item's <i s="1"> flag is cleared.
            // After the fix: keying off the per-item kind ('d' for index 1) formats the same caption
            // the resolver did, so the selection survives.
            items.Should().ContainSingle(item => item.Selected).Which.Index.Should().Be(1,
                "the rewriter must resolve a mixed field's date item the same way the resolver did " +
                "(keying off the per-item kind), not drop the selection because the field-level " +
                "Contains* flags fail the 'exclusively one kind' gate on a mixed field");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    private static Workbook BuildMixedFieldPivotSlicerWorkbook()
    {
        var workbook = new Workbook("MixedFieldPivotSlicerR17");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("2026-01-05T00:00:00"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Data",
            SourceReference = "A1:B3"
        };
        // A genuinely MIXED-type field: one text item, one date item — ContainsString AND ContainsDate
        // both true, which fails the field-level "exclusively one kind" gate the pre-fix rewriter used.
        cache.Fields.Add(new PivotCacheFieldModel(
            "Category",
            ContainsString: true,
            ContainsDate: true,
            ContainsMixedTypes: true,
            SharedItems: ["East", "2026-01-05T00:00:00"],
            SharedItemKinds: ['s', 'd']));
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

        var slicer = new SlicerModel
        {
            Name = "Category Slicer",
            CacheName = "Slicer_Category",
            Caption = "Category",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Category",
            StyleName = "SlicerStyleLight2"
        };
        workbook.Slicers.Add(slicer);

        return workbook;
    }

    /// <summary>
    /// Rewrites the freshly-saved slicerCache1.xml to also carry the NATIVE
    /// &lt;data&gt;&lt;tabular&gt;&lt;items&gt;&lt;i x="N" s="1"/&gt; selection form (what a real
    /// Excel-authored file stores), selecting only <paramref name="selectedIndex"/>.
    /// </summary>
    private static void InjectNativeTabularSelection(MemoryStream package, int selectedIndex)
    {
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/slicerCaches/slicerCache1.xml")!;
            XDocument xml;
            using (var entryStream = entry.Open())
                xml = XDocument.Load(entryStream);

            var root = xml.Root!;
            var data = new XElement(SlicerNs + "data",
                new XElement(SlicerNs + "tabular",
                    new XElement(SlicerNs + "items",
                        new XElement(SlicerNs + "i", new XAttribute("x", 0), selectedIndex == 0 ? new XAttribute("s", "1") : null),
                        new XElement(SlicerNs + "i", new XAttribute("x", 1), selectedIndex == 1 ? new XAttribute("s", "1") : null))));
            // R44-io-pivot-filter-page-3-2: a fresh save of a pivot slicer now ALSO emits its own native
            // <data> element (previously only this injection did), so replace it rather than blindly
            // appending -- a second <data> sibling is schema-invalid (CT_SlicerCacheDefinition allows at
            // most one) and would make every native <i> lookup below see duplicates.
            root.Elements(SlicerNs + "data").Remove();
            root.Add(data);

            entry.Delete();
            var newEntry = archive.CreateEntry("xl/slicerCaches/slicerCache1.xml");
            using var writeStream = newEntry.Open();
            xml.Save(writeStream);
        }

        package.Position = 0;
    }

    private static (int Index, bool Selected)[] ReadNativeCacheItems(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/slicerCaches/slicerCache1.xml");
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

    // ── R17-slicer-timeline-cache-3 ──────────────────────────────────────────────────────────

    [Fact]
    public void ClearedTimelineFilter_RemovesNativeSelectionElement_KeepsValidSchema()
    {
        var workbook = CreateTimelineWorkbook();
        using var source = SaveWorkbook(workbook);
        // Simulate a genuine Excel-authored native <state><bounds/><selection startDate.. endDate../>,
        // stripping the fresh writer's root-attribute form first so load is forced onto the native path.
        InjectNativeTimelineSelectionState(source, "2026-03-01T00:00:00", "2026-04-30T00:00:00");

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var timeline = loaded.Timelines.Should().ContainSingle().Subject;
        timeline.SelectedStartDate.Should().Be("2026-03-01");
        timeline.SelectedEndDate.Should().Be("2026-04-30");

        // User clears the timeline filter (mirrors what a Clear-Filter command does to the model).
        timeline.SelectedStartDate = null;
        timeline.SelectedEndDate = null;

        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 8, 8), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        var cacheRoot = ReadRoot(saved, "xl/timelineCaches/timelineCache1.xml");
        var selection = cacheRoot.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "selection");
        selection.Should().BeNull(
            "a cleared filter must remove the native <selection> element rather than leave a " +
            "CT_TimelineRange stub with neither of its required startDate/endDate attributes");

        // <bounds> (the untouched available-range) must survive the clear.
        var bounds = cacheRoot.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "bounds");
        bounds.Should().NotBeNull("the available-range bounds must be left untouched by a selection clear");

        SchemaErrors(saved).Should().BeEmpty("the cleared-filter package must stay schema valid");
    }

    private static Workbook CreateTimelineWorkbook()
    {
        var workbook = new Workbook("TimelineClearR17");
        var sheet = workbook.AddSheet("Data");
        for (uint row = 2; row <= 5; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row * 3));

        workbook.Timelines.Add(new TimelineModel
        {
            Name = "Date Timeline",
            CacheName = "Timeline_Date",
            Caption = "Order Date",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Date",
            StyleName = "TimeSlicerStyleLight1",
            StartDate = "2026-01-01",
            EndDate = "2026-06-30"
        });

        return workbook;
    }

    private static void InjectNativeTimelineSelectionState(MemoryStream package, string startDate, string endDate)
    {
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/timelineCaches/timelineCache1.xml")!;
            XDocument xml;
            using (var entryStream = entry.Open())
                xml = XDocument.Load(entryStream);

            var root = xml.Root!;
            // Strip the fresh writer's root-attribute selected-range form so the reader/rewriter are
            // forced onto the native <state><selection> path, matching a genuine Excel-authored file.
            root.Attribute("selectedStartDate")?.Remove();
            root.Attribute("selectedEndDate")?.Remove();

            var state = new XElement(TimelineNs + "state",
                new XElement(TimelineNs + "bounds",
                    new XAttribute("startDate", root.Attribute("startDate")?.Value ?? "2026-01-01T00:00:00"),
                    new XAttribute("endDate", root.Attribute("endDate")?.Value ?? "2026-06-30T00:00:00")),
                new XElement(TimelineNs + "selection",
                    new XAttribute("startDate", startDate),
                    new XAttribute("endDate", endDate)));
            root.Add(state);

            entry.Delete();
            var newEntry = archive.CreateEntry("xl/timelineCaches/timelineCache1.xml");
            using var writeStream = newEntry.Open();
            xml.Save(writeStream);
        }

        package.Position = 0;
    }

    // ── R17-slicer-timeline-cache-2 ──────────────────────────────────────────────────────────

    [Fact]
    public void NativeJsonAdapter_RoundTrips_PivotSlicer_CacheItems()
    {
        var workbook = new Workbook("PivotSlicerCacheItemsWorkbook");
        var slicer = new SlicerModel
        {
            Name = "Region Slicer",
            Caption = "Region",
            CacheName = "Slicer_Region",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Region",
            StyleName = "SlicerStyleLight2",
            CacheItems = new List<SlicerCacheItem>
            {
                new(0, true),
                new(1, false),
                new(2, false)
            }
        };
        slicer.SelectedItems.Add("East");
        slicer.SelectionCaptured = true;
        workbook.Slicers.Add(slicer);

        var loaded = RoundTrip(workbook);

        var loadedSlicer = loaded.Slicers.Should().ContainSingle().Subject;
        // Before the fix: CacheItems is never persisted by the SlicerDto, so a reloaded pivot slicer's
        // available (unselected) tiles vanish — the pivot-item resolver gates entirely on
        // CacheItems.Count > 0, so an empty list here means every unselected tile is gone.
        loadedSlicer.CacheItems.Should().HaveCount(3);
        loadedSlicer.CacheItems.Should().Contain(item => item.Index == 0 && item.IsSelected);
        loadedSlicer.CacheItems.Should().Contain(item => item.Index == 1 && !item.IsSelected);
        loadedSlicer.CacheItems.Should().Contain(item => item.Index == 2 && !item.IsSelected);
        loadedSlicer.SelectionCaptured.Should().BeTrue();
    }

    private static Workbook RoundTrip(Workbook source)
    {
        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(source, stream);
        stream.Position = 0;
        return adapter.Load(stream);
    }

    // ── shared helpers ────────────────────────────────────────────────────────────────────────

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
