using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-13 fix bucket S14 regression test.
///   - R13-meta-4: a slicer's Clear-Filter (select-all) must round-trip through a full save of a
///     source-preserved workbook. <c>XlsxSlicerTimelineStateRewriter.RewriteNativeCacheItemSelection</c>
///     previously treated an empty <see cref="SlicerModel.SelectedItems"/> as "nothing to rewrite" — which
///     is correct for a freshly-loaded workbook whose selection was never touched, but WRONG once the user
///     has explicitly cleared a partial native selection back to select-all: the preserved native
///     <c>&lt;i s="1"&gt;</c> flags were left untouched, so reopening the file showed the stale partial
///     filter again. <see cref="SlicerModel.SelectionCaptured"/> (set by <c>SetSlicerSelectionCommand</c>,
///     the only path a user selection change reaches the model through) now disambiguates "never touched"
///     from "explicitly cleared", so a real clear-to-select-all strips every native selected flag.
/// </summary>
public sealed class FreeXR13S14Tests
{
    private static readonly XNamespace SlicerNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";

    [Fact]
    public void SlicerClearFilter_ResaveStripsNativeCacheItemSelectedFlags()
    {
        // Build + save a workbook with a pivot slicer, then inject a native <i x="N" s="1"/> PARTIAL
        // selection (as a real Excel-authored file would carry) selecting only "North" (x=2). This
        // simulates loading a real Excel workbook whose slicerCache stores selection ONLY in native form.
        var workbook = BuildPivotSlicerWorkbook();
        using var source = SaveWorkbook(workbook);
        InjectNativeTabularSelection(source, selectedIndex: 2); // "North" selected natively.

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var slicer = loaded.Slicers.Should().ContainSingle().Subject;

        // The freshly-loaded model must not yet consider the selection "captured" — this is what keeps
        // an untouched reopen-then-resave byte-stable (no accidental stripping of a real Excel selection).
        slicer.SelectionCaptured.Should().BeFalse(
            "the Core.IO load path never populates SelectedItems from the native <i s=\"1\"> flags, so a " +
            "freshly-loaded slicer must not be considered explicitly captured yet");

        // Mirror exactly what SetSlicerSelectionCommand.Apply does for a user's Clear-Filter click: an
        // empty selected-items list, with the selection now marked as explicitly captured.
        slicer.SelectedItems.Clear();
        slicer.SelectionCaptured = true;

        // Force the full-save (source package preserved) path with a trivial cell edit.
        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 9, 9), new NumberValue(1));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        var items = ReadNativeCacheItems(saved);
        // Before the fix: RewriteNativeCacheItemSelection returned false as soon as SelectedItems.Count
        // was 0, leaving "North" (x=2) still carrying s="1" from the preserved native part — the user's
        // clear-filter was silently discarded and reopening the file re-applied the stale filter.
        // After the fix: SelectionCaptured disambiguates "user cleared" from "never touched", so every
        // native item is stripped of its s="1" flag.
        items.Should().OnlyContain(item => !item.Selected,
            "the user explicitly cleared the slicer's filter to select-all, so every native <i> item " +
            "must lose its s=\"1\" flag rather than keeping the stale native selection");
    }

    private static Workbook BuildPivotSlicerWorkbook()
    {
        var workbook = new Workbook("PivotSlicerClearFilterR13S14");
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
            SharedItems: ["East", "West", "North"]));
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
    /// Rewrites the freshly-saved slicerCache1.xml to also carry the NATIVE
    /// &lt;data&gt;&lt;tabular&gt;&lt;items&gt;&lt;i x="N" s="1"/&gt; selection form (what a real
    /// Excel-authored file stores), selecting only <paramref name="selectedIndex"/>. The fresh FreeX writer
    /// never emits this native form itself, so this simulates "loaded a real Excel workbook".
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
                        new XElement(SlicerNs + "i", new XAttribute("x", 1), selectedIndex == 1 ? new XAttribute("s", "1") : null),
                        new XElement(SlicerNs + "i", new XAttribute("x", 2), selectedIndex == 2 ? new XAttribute("s", "1") : null))));
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

    private static MemoryStream SaveWorkbook(Workbook workbook)
    {
        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return stream;
    }
}
