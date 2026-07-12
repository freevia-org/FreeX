using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R31-meta-2: the R30-io-pivot-cache-deep-2 guard in XlsxPivotTableReader.Fields.cs
/// (ReadNativePivotFieldSelections) declined to resolve a row/column/page field's hidden-item selection
/// whenever the field's declared sharedItems @count (<see cref="PivotCacheFieldModel.SharedItemCount"/>)
/// was larger than the materialized <see cref="PivotCacheFieldModel.SharedItems"/> list -- which is the
/// COMMON case (any field with a single blank source cell triggers it, since XlsxPivotCacheReader drops the
/// resulting &lt;m/&gt; entry from SharedItems while still counting it in SharedItemCount). That blanket
/// decline silently disabled hidden-item filtering entirely for any such field, not just the narrow
/// genuinely-ambiguous case the original fix targeted.
///
/// The fix reconstructs the raw OOXML shared-item index -> materialized SharedItems index mapping from the
/// pivotField's own &lt;items&gt; list: each &lt;item&gt; carries its own "m" (missing) flag independent of
/// its "x" shared-item index, so when every raw index is accounted for exactly once we can determine, for
/// each real (non-missing) raw index, precisely how many missing items precede it -- with no need to see
/// the pivot cache's raw sharedItems XML at all. These tests pin: (1) a field with a leading blank shared
/// item still correctly hides the item the user actually hid (not a shifted neighbor, and not "give up and
/// show everything"), and (2) the ordinary sibling case -- no blank/missing item at all -- still filters
/// exactly as before.
/// </summary>
public sealed class R31Meta2_PivotHiddenItemMissingSharedItemMappingTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void Load_NativeRowFieldHiddenItem_WithLeadingMissingSharedItem_StillHidesCorrectItem()
    {
        // Raw OOXML sharedItems: <m/><s v="North"/><s v="South"/><s v="East"/> (raw indices 0..3, count="4").
        // The reader drops the leading <m/>, materializing SharedItems as ["North","South","East"] (3
        // entries, every real item shifted down by one relative to its raw index). The native pivotField's
        // <items> list fully enumerates all four raw indices (mirroring what real Excel writes): raw index
        // 0 is flagged m="1" (the blank bucket itself, not hidden), and raw index 2 ("South") is hidden.
        //
        // Before this fix: the blanket declaredCount>count guard aborted resolving this field entirely,
        // so SelectedItems came back null and "South" kept showing despite being unchecked in the source
        // file. Before THAT (pre-R30): the raw index would have been used directly against the shifted
        // list (materialized[2] = "East"), wrongly hiding "East" instead of "South". The fix must hide
        // exactly "South" -- the item Excel actually recorded as hidden -- and nothing else.
        using var package = CreatePivotWorkbookPackage(withLeadingMissingSharedItem: true, hiddenRawIndex: 2);

        var workbook = new XlsxFileAdapter().Load(package);

        var rowField = workbook.GetSheetAt(0).PivotTables.Should().ContainSingle().Subject
            .RowFields.Should().ContainSingle().Subject;
        rowField.SourceFieldIndex.Should().Be(0);
        rowField.SelectedItems.Should().Equal("North", "East");
    }

    [Fact]
    public void Load_NativeRowFieldHiddenItem_WithNoMissingSharedItems_StillResolvesVisibleSelection()
    {
        // Sibling case: no <m/> was ever dropped, so SharedItemCount matches the materialized list and the
        // raw item @x index lines up with it exactly like before the fix -- must keep resolving correctly.
        using var package = CreatePivotWorkbookPackage(withLeadingMissingSharedItem: false, hiddenRawIndex: 1);

        var workbook = new XlsxFileAdapter().Load(package);

        var rowField = workbook.GetSheetAt(0).PivotTables.Should().ContainSingle().Subject
            .RowFields.Should().ContainSingle().Subject;
        rowField.SourceFieldIndex.Should().Be(0);
        // Raw index 1 ("South") is hidden; the remaining visible items are "North" and "East".
        rowField.SelectedItems.Should().Equal("North", "East");
    }

    private static MemoryStream CreatePivotWorkbookPackage(bool withLeadingMissingSharedItem, int hiddenRawIndex)
    {
        var package = XlsxPackageTestHelper.SaveWorkbook(CreatePivotWorkbook());

        XlsxPackageTestHelper.PatchPackageXml(package, "xl/pivotCache/pivotCacheDefinition1.xml", document =>
        {
            var sharedItems = document.Root!
                .Element(WorkbookNs + "cacheFields")!
                .Elements(WorkbookNs + "cacheField")
                .First(field => string.Equals(field.Attribute("name")?.Value, "Region", StringComparison.Ordinal))
                .Element(WorkbookNs + "sharedItems")!;
            sharedItems.RemoveNodes();
            if (withLeadingMissingSharedItem)
            {
                sharedItems.SetAttributeValue("count", "4");
                sharedItems.Add(
                    new XElement(WorkbookNs + "m"),
                    new XElement(WorkbookNs + "s", new XAttribute("v", "North")),
                    new XElement(WorkbookNs + "s", new XAttribute("v", "South")),
                    new XElement(WorkbookNs + "s", new XAttribute("v", "East")));
            }
            else
            {
                sharedItems.SetAttributeValue("count", "3");
                sharedItems.Add(
                    new XElement(WorkbookNs + "s", new XAttribute("v", "North")),
                    new XElement(WorkbookNs + "s", new XAttribute("v", "South")),
                    new XElement(WorkbookNs + "s", new XAttribute("v", "East")));
            }
        });

        XlsxPackageTestHelper.PatchPackageXml(package, "xl/pivotTables/pivotTable1.xml", document =>
        {
            var pivotField = document.Root!
                .Element(WorkbookNs + "pivotFields")!
                .Elements(WorkbookNs + "pivotField")
                .First();
            var items = pivotField.Element(WorkbookNs + "items")!;
            items.RemoveNodes();

            if (withLeadingMissingSharedItem)
            {
                // A complete enumeration of all four raw indices, exactly as real Excel writes when a
                // pivot field's source column contains a blank cell: one <item> per raw shared-item slot,
                // with the blank bucket flagged m="1" and the user's hidden selection flagged hidden="1".
                items.SetAttributeValue("count", "4");
                items.Add(
                    new XElement(WorkbookNs + "item", new XAttribute("x", "0"), new XAttribute("m", "1")),
                    new XElement(WorkbookNs + "item", new XAttribute("x", "1")),
                    new XElement(
                        WorkbookNs + "item",
                        new XAttribute("x", hiddenRawIndex.ToString()),
                        new XAttribute("hidden", "1")),
                    new XElement(WorkbookNs + "item", new XAttribute("x", "3")));
            }
            else
            {
                items.SetAttributeValue("count", "1");
                items.Add(new XElement(
                    WorkbookNs + "item",
                    new XAttribute("x", hiddenRawIndex.ToString()),
                    new XAttribute("hidden", "1")));
            }
        });

        return package;
    }

    private static Workbook CreatePivotWorkbook()
    {
        var workbook = new Workbook("PivotRowFieldMissingSharedItemMapping");
        var sheet = workbook.AddSheet("PivotData");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = "A1:B3",
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
            RecordCount = 2,
        };
        cache.Fields.Add(new PivotCacheFieldModel(
            "Region",
            SharedItemCount: 3,
            ContainsString: true,
            SharedItems: ["North", "South", "East"],
            SharedItemKinds: ['s', 's', 's']));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 2)),
            TargetRange = new GridRange(
                new CellAddress(sheet.Id, 5, 1),
                new CellAddress(sheet.Id, 8, 2)),
            PackagePart = "xl/pivotTables/pivotTable1.xml",
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        return workbook;
    }
}
