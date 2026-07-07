using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-12 fix bucket Q6 regression test.
///   - R12-meta-1: a pivot slicer whose selection is stored ONLY in the native slicerCache
///     &lt;data&gt;&lt;tabular&gt;&lt;items&gt;&lt;i x="N" s="1"/&gt; flags (the normal Excel form) must survive a
///     pure round-trip save untouched. <see cref="SlicerModel.SelectedItems"/> is populated only by the
///     host UI's SlicerItemResolver (never by the Core.IO load path, and never for the all-selected case),
///     so it is empty at save time for a headless/programmatic load-then-save with no selection edit. The
///     native-cache rewriter must treat that as "nothing to apply" and leave the preserved s="1" flags
///     alone, not read it as "select nothing" and strip every flag.
/// </summary>
public sealed class FreeXR12Q6Tests
{
    private static readonly XNamespace SlicerNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";

    [Fact]
    public void PureRoundTrip_WithEmptySelectedItems_PreservesNativeCacheItemSelectedFlags()
    {
        // Build + save a workbook with a pivot cache field carrying shared items ("East"/"West"/"North"),
        // a pivot slicer bound to that field, and a native <data><tabular><items> selection (as a real
        // Excel-authored file would carry) selecting "West" (x=1, s="1"). This simulates loading a real
        // Excel workbook whose slicerCache stores selection ONLY in the native <i s="1"> form.
        var workbook = BuildPivotSlicerWorkbook();
        using var source = SaveWorkbook(workbook);
        InjectNativeTabularSelection(source, selectedIndex: 1); // "West" selected natively.

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var slicer = loaded.Slicers.Should().ContainSingle().Subject;

        // Pure round-trip: nothing touches SelectedItems (the host UI's SlicerItemResolver, which is the
        // ONLY code that back-fills it from the native flags, never runs in a headless load/save). This
        // mirrors both a headless re-save and an all-items-selected slicer (SlicerItemResolver
        // deliberately skips projecting a selection in that case too).
        slicer.SelectedItems.Should().BeEmpty(
            "a pure round-trip never populates SelectedItems from the native cache flags outside the host UI");

        // Force the full-save (source package preserved) path with a trivial unrelated cell edit.
        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 9, 9), new NumberValue(1));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);

        var items = ReadNativeCacheItems(saved);
        // Before the fix: RewriteNativeCacheItemSelection ran unconditionally, saw an empty SelectedItems,
        // and stripped every native s="1" flag (silent selection data loss on a round-trip).
        // After the fix: the preserved native selection ("West", x=1) survives untouched.
        items.Should().ContainSingle(item => item.Selected).Which.Index.Should().Be(1,
            "an empty SelectedItems at save time means the model never captured/changed the selection, " +
            "so the preserved native <i s=\"1\"> flag must be left exactly as Excel wrote it");
    }

    private static Workbook BuildPivotSlicerWorkbook()
    {
        var workbook = new Workbook("PivotSlicerNativeSelectionR12Q6");
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
