using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-58 regression coverage for R58-io-slicer-timeline-6-1: a pivot slicer's field is resolved by
/// NAME across ALL pivot caches in the workbook (<c>XlsxSlicerTimelineStateRewriter.ResolveRawSharedItemCaptions</c>
/// and <c>XlsxSlicerTimelineWriter.ResolveSlicerSharedItemsField</c>), not the specific cache the slicer's own
/// <c>SourcePivotTableName</c> is bound to. When two independent pivot caches both carry a field with the same
/// name (e.g. "Region") but different shared-item lists, the wrong cache's caption list gets used, silently
/// corrupting/dropping the user's slicer selection.
/// </summary>
public sealed class FreeXR58SlicerTimelineCacheScopeTests
{
    private static readonly XNamespace SlicerNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";

    // ── Bug case: two pivot caches share a field name; the slicer is bound to the SECOND cache ────────

    [Fact]
    public void PivotSlicerSelectionChange_WithTwoCachesSharingFieldName_RewritesUsingSlicerOwnBoundCache()
    {
        // Cache1/PivotTable1's "Region" field: ["East","West","North"]. Cache2/PivotTable2's "Region" field:
        // ["North","South"] -- a completely different shared-items space under the same field name. The
        // slicer is bound to PivotTable2 (SourcePivotTableName), so its native <i x="N"> items are indexed
        // against Cache2's space (x=0 -> "North", x=1 -> "South").
        var workbook = BuildTwoCacheWorkbook();
        using var source = SaveWorkbook(workbook);

        // Native tabular selection: "North" (x=0) originally selected, "South" (x=1) not.
        InjectNativeTabularSelection(source, itemCount: 2, selectedIndices: [0]);

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var slicer = loaded.Slicers.Should().ContainSingle().Subject;
        slicer.SourcePivotTableName.Should().Be("PivotTable2");

        // User selects ONLY "South" via SetSlicerSelectionCommand.
        slicer.SelectedItems.Clear();
        slicer.SelectedItems.Add("South");

        // Force the full-save (source package preserved) path with a trivial cell edit.
        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 20, 20), new NumberValue(1));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        var items = ReadNativeCacheItems(saved);
        // The slicer's own bound cache (Cache2) must be used: x=1 ("South") selected, x=0 ("North") cleared.
        // Resolving via a name-only scan would instead find Cache1 first (added first, also has a non-empty
        // "Region" field) and index its ["East","West","North"] list by x=0/x=1 -> "East"/"West", neither of
        // which matches "South", silently clearing BOTH native items instead.
        items.Should().ContainSingle(item => item.Selected).Which.Index.Should().Be(1,
            "Cache2's sharedItems[1] is \"South\" -- the slicer is bound to PivotTable2/Cache2, not Cache1");
        items.Where(item => item.Index != 1).Should().OnlyContain(item => !item.Selected);
    }

    // ── Sibling case: a single-cache workbook (no cross-cache collision) must keep working ────────────

    [Fact]
    public void PivotSlicerSelectionChange_WithSingleCache_StillRewritesCorrectNativeTile()
    {
        var workbook = new Workbook("PivotSlicerSingleCacheR58");
        var sheet = workbook.AddSheet("Data1");
        PopulateRegionSheet(sheet, "East", "West", "North");

        var cache = new PivotCacheModel { CacheId = 1, SourceType = PivotCacheSourceType.WorksheetRange, SourceSheetName = "Data1", SourceReference = "A1:B4" };
        cache.Fields.Add(new PivotCacheFieldModel("Region", ContainsString: true, SharedItems: ["East", "West", "North"]));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 6, 1), new CellAddress(sheet.Id, 9, 2))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        var slicer = new SlicerModel
        {
            Name = "Region Slicer",
            CacheName = "Slicer_Region",
            Caption = "Region",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Region",
            StyleName = "SlicerStyleLight2"
        };
        workbook.Slicers.Add(slicer);

        using var source = SaveWorkbook(workbook);
        InjectNativeTabularSelection(source, itemCount: 3, selectedIndices: [0]); // "East" selected natively.

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var loadedSlicer = loaded.Slicers.Should().ContainSingle().Subject;
        loadedSlicer.SelectedItems.Clear();
        loadedSlicer.SelectedItems.Add("West");

        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 20, 20), new NumberValue(1));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        var items = ReadNativeCacheItems(saved);
        items.Should().ContainSingle(item => item.Selected).Which.Index.Should().Be(1,
            "\"West\" is sharedItems[1] and there is only one cache in this workbook");
        items.Where(item => item.Index != 1).Should().OnlyContain(item => !item.Selected);
    }

    // ── Fresh-insert path: XlsxSlicerTimelineWriter.ResolveSlicerSharedItemsField must scope by cache too ──

    [Fact]
    public void FreshSave_PivotSlicerWithTwoCachesSharingFieldName_AuthorsItemsFromSlicerOwnBoundCache()
    {
        var workbook = BuildTwoCacheWorkbook();
        var slicer = workbook.Slicers.Single();
        slicer.SelectedItems.Add("South");

        using var saved = SaveWorkbook(workbook);

        var items = ReadNativeCacheItems(saved);
        // Fresh authoring must build the <data><tabular><items> list from Cache2's ["North","South"] (2
        // items, "South" selected at x=1), not Cache1's ["East","West","North"] (3 items, none selected
        // since none of them equal "South").
        items.Should().HaveCount(2, "the slicer is bound to PivotTable2/Cache2, whose Region field has 2 items");
        items.Should().ContainSingle(item => item.Selected).Which.Index.Should().Be(1,
            "Cache2's sharedItems[1] is \"South\"");
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static Workbook BuildTwoCacheWorkbook()
    {
        var workbook = new Workbook("PivotSlicerTwoCacheR58");

        var sheet1 = workbook.AddSheet("Data1");
        PopulateRegionSheet(sheet1, "East", "West", "North");
        var cache1 = new PivotCacheModel { CacheId = 1, SourceType = PivotCacheSourceType.WorksheetRange, SourceSheetName = "Data1", SourceReference = "A1:B4" };
        cache1.Fields.Add(new PivotCacheFieldModel("Region", ContainsString: true, SharedItems: ["East", "West", "North"]));
        cache1.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
        workbook.PivotCaches.Add(cache1);

        var pivot1 = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 4, 2)),
            TargetRange = new GridRange(new CellAddress(sheet1.Id, 6, 1), new CellAddress(sheet1.Id, 9, 2))
        };
        pivot1.RowFields.Add(new PivotFieldModel(0));
        pivot1.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet1.PivotTables.Add(pivot1);

        var sheet2 = workbook.AddSheet("Data2");
        PopulateRegionSheet(sheet2, "North", "South");
        var cache2 = new PivotCacheModel { CacheId = 2, SourceType = PivotCacheSourceType.WorksheetRange, SourceSheetName = "Data2", SourceReference = "A1:B3" };
        cache2.Fields.Add(new PivotCacheFieldModel("Region", ContainsString: true, SharedItems: ["North", "South"]));
        cache2.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
        workbook.PivotCaches.Add(cache2);

        var pivot2 = new PivotTableModel
        {
            Name = "PivotTable2",
            CacheId = 2,
            SourceRange = new GridRange(new CellAddress(sheet2.Id, 1, 1), new CellAddress(sheet2.Id, 3, 2)),
            TargetRange = new GridRange(new CellAddress(sheet2.Id, 6, 1), new CellAddress(sheet2.Id, 8, 2))
        };
        pivot2.RowFields.Add(new PivotFieldModel(0));
        pivot2.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet2.PivotTables.Add(pivot2);

        var slicer = new SlicerModel
        {
            Name = "Region Slicer",
            CacheName = "Slicer_Region",
            Caption = "Region",
            SourcePivotTableName = "PivotTable2",
            SourceFieldName = "Region",
            StyleName = "SlicerStyleLight2"
        };
        workbook.Slicers.Add(slicer);

        return workbook;
    }

    private static void PopulateRegionSheet(Sheet sheet, params string[] regions)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        for (var i = 0; i < regions.Length; i++)
        {
            var row = (uint)(i + 2);
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue(regions[i]));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue((i + 1) * 10));
        }
    }

    /// <summary>
    /// Rewrites the freshly-saved slicerCache1.xml to also carry the NATIVE
    /// &lt;data&gt;&lt;tabular&gt;&lt;items&gt;&lt;i x="N" s="1"/&gt; selection form (what a real
    /// Excel-authored file stores), selecting exactly <paramref name="selectedIndices"/> out of
    /// <paramref name="itemCount"/> raw items. Mirrors FreeXR26SlicerTimelineDeep2Tests's helper.
    /// </summary>
    private static void InjectNativeTabularSelection(MemoryStream package, int itemCount, params int[] selectedIndices)
    {
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/slicerCaches/slicerCache1.xml")!;
            XDocument xml;
            using (var entryStream = entry.Open())
                xml = XDocument.Load(entryStream);

            var root = xml.Root!;
            var selected = new HashSet<int>(selectedIndices);
            var itemElements = Enumerable.Range(0, itemCount)
                .Select(index => new XElement(
                    SlicerNs + "i",
                    new XAttribute("x", index),
                    selected.Contains(index) ? new XAttribute("s", "1") : null));
            var data = new XElement(SlicerNs + "data",
                new XElement(SlicerNs + "tabular",
                    new XElement(SlicerNs + "items", itemElements)));
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

    private static MemoryStream SaveWorkbook(Workbook workbook)
    {
        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return stream;
    }
}
