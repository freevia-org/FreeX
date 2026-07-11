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
/// Round-26 regression coverage for R26-io-pivot-deep-2:
/// <see cref="XlsxSlicerTimelineStateRewriter"/>'s native cache-item selection rewrite
/// (<c>RewriteNativeCacheItemSelection</c>) resolved each native <c>&lt;i x="N"/&gt;</c>'s caption via
/// <c>PivotCacheFieldModel.SharedItems[N]</c> -- but <c>XlsxPivotCacheReader</c> drops <c>&lt;m/&gt;</c>
/// (missing-value) shared items entirely when populating that list, so for any field with a blank value the
/// raw native index space (Excel's own, unfiltered) and the model's filtered index space disagree, and the
/// wrong tile's <c>s="1"</c> selected flag gets read/written on save.
/// </summary>
public sealed class FreeXR26SlicerTimelineDeep2Tests
{
    private static readonly XNamespace SlicerNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";

    // ── Bug case: a shared-items list with a missing (<m/>) item shifts the native index space ──────

    [Fact]
    public void PivotSlicerSelectionChange_WithMissingSharedItem_RewritesCorrectNativeTile()
    {
        // Field "Region" as Excel would write it: raw sharedItems = <s v="North"/><m/><s v="South"/><s v="West"/>
        // (raw indices 0..3). XlsxPivotCacheReader drops the <m/> at read time, so
        // PivotCacheFieldModel.SharedItems ends up as the REDUCED ["North","South","West"] (3 items).
        var workbook = BuildPivotSlicerWorkbook(sharedItems: ["North", "South", "West"]);
        using var source = SaveWorkbook(workbook);
        InjectMissingSharedItem(source, cacheFieldName: "Region", missingItemIndex: 1);

        // Native tabular selection exactly as the failure scenario describes: North (x=0) and West (x=3)
        // originally selected, the blank (x=1) and South (x=2) unselected.
        InjectNativeTabularSelection(source, itemCount: 4, selectedIndices: [0, 3]);

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var field = loaded.PivotCaches.Single().Fields.Single(f => f.Name == "Region");
        field.SharedItems.Should().Equal(
            new[] { "North", "South", "West" },
            "the reader drops <m/> items, which is exactly what shifts the native index space");

        var slicer = loaded.Slicers.Should().ContainSingle().Subject;

        // User selects ONLY "South" -- SetSlicerSelectionCommand only ever touches SelectedItems.
        slicer.SelectedItems.Clear();
        slicer.SelectedItems.Add("South");

        // Force the full-save (source package preserved) path with a trivial cell edit.
        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 9, 9), new NumberValue(1));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        var items = ReadNativeCacheItems(saved);
        // Only the item resolving to the RAW index of "South" (x=2, since x=1 is the blank <m/> slot) must
        // carry s="1"; North (x=0) and West (x=3) must be cleared, and the blank (x=1) must stay untouched.
        items.Should().ContainSingle(item => item.Selected).Which.Index.Should().Be(2,
            "raw sharedItems[2] is \"South\" -- indexing PivotCacheFieldModel.SharedItems (which dropped the " +
            "<m/> at raw index 1) instead would wrongly resolve x=2 to \"West\" and leave the blank x=1 " +
            "wrongly marked selected");
        items.Where(item => item.Index != 2).Should().OnlyContain(item => !item.Selected);
    }

    // ── Sibling case: no missing items -- the already-working alignment must not regress ────────────

    [Fact]
    public void PivotSlicerSelectionChange_WithoutMissingSharedItem_StillRewritesCorrectNativeTile()
    {
        // No <m/> gap: raw sharedItems lines up 1:1 with PivotCacheFieldModel.SharedItems.
        var workbook = BuildPivotSlicerWorkbook(sharedItems: ["East", "West", "North"]);
        using var source = SaveWorkbook(workbook);
        InjectNativeTabularSelection(source, itemCount: 3, selectedIndices: [0]); // "East" selected natively.

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var slicer = loaded.Slicers.Should().ContainSingle().Subject;

        slicer.SelectedItems.Clear();
        slicer.SelectedItems.Add("West");

        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 9, 9), new NumberValue(1));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        var items = ReadNativeCacheItems(saved);
        items.Should().ContainSingle(item => item.Selected).Which.Index.Should().Be(1,
            "\"West\" is raw sharedItems[1] when there is no missing-item gap");
        items.Where(item => item.Index != 1).Should().OnlyContain(item => !item.Selected);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static Workbook BuildPivotSlicerWorkbook(IReadOnlyList<string> sharedItems)
    {
        var workbook = new Workbook("PivotSlicerNativeSelectionR26Deep2");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Data",
            SourceReference = "A1:B4"
        };
        cache.Fields.Add(new PivotCacheFieldModel(
            "Region",
            ContainsString: true,
            SharedItems: sharedItems));
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

        return workbook;
    }

    /// <summary>
    /// Inserts a <c>&lt;m/&gt;</c> (missing-value) shared item into the freshly-written
    /// <c>xl/pivotCache/pivotCacheDefinition1.xml</c>'s <c>&lt;sharedItems&gt;</c> at
    /// <paramref name="missingItemIndex"/>, simulating a real Excel-authored cache field whose shared items
    /// include a blank value the fresh FreeX writer never emits on its own (it only ever writes the model's
    /// already-filtered <see cref="PivotCacheFieldModel.SharedItems"/>).
    /// </summary>
    private static void InjectMissingSharedItem(MemoryStream package, string cacheFieldName, int missingItemIndex)
    {
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/pivotCache/pivotCacheDefinition1.xml")!;
            XDocument xml;
            using (var entryStream = entry.Open())
                xml = XDocument.Load(entryStream);

            var workbookNs = xml.Root!.Name.Namespace;
            var cacheField = xml.Root!
                .Element(workbookNs + "cacheFields")!
                .Elements(workbookNs + "cacheField")
                .Single(element => element.Attribute("name")?.Value == cacheFieldName);
            var sharedItemsElement = cacheField.Element(workbookNs + "sharedItems")!;

            var items = sharedItemsElement.Elements().Select(element => new XElement(element)).ToList();
            items.Insert(missingItemIndex, new XElement(workbookNs + "m"));
            sharedItemsElement.RemoveNodes();
            sharedItemsElement.Add(items);
            if (sharedItemsElement.Attribute("count") is not null)
                sharedItemsElement.SetAttributeValue("count", items.Count);

            entry.Delete();
            var newEntry = archive.CreateEntry("xl/pivotCache/pivotCacheDefinition1.xml");
            using var writeStream = newEntry.Open();
            xml.Save(writeStream);
        }

        package.Position = 0;
    }

    /// <summary>
    /// Rewrites the freshly-saved slicerCache1.xml to also carry the NATIVE
    /// &lt;data&gt;&lt;tabular&gt;&lt;items&gt;&lt;i x="N" s="1"/&gt; selection form (what a real
    /// Excel-authored file stores), selecting exactly <paramref name="selectedIndices"/> out of
    /// <paramref name="itemCount"/> raw items. The fresh FreeX writer never emits this native form itself, so
    /// this simulates "loaded a real Excel workbook".
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
