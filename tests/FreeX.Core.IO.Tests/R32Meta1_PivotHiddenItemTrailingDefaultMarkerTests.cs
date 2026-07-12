using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R32-meta-1: R31's raw-index reconstruction in XlsxPivotTableReader.Fields.cs
/// (TryResolveHiddenIndexesAcrossMissingSharedItems) bailed out (returned null, disabling hidden-item
/// filtering for the whole field) the moment it hit an &lt;item&gt; with no "x" attribute. But real Excel --
/// and FreeX's own writer, XlsxPivotTableWriter.cs:443-445 -- ALWAYS appends a trailing
/// &lt;item t="default"/&gt; (no "x") after enumerating a field's real per-value items. That near-universal
/// trailing marker re-triggered the exact "give up and show everything" regression R31 claimed to fix,
/// silently re-disabling hidden-item filtering for the standard Excel item-list shape.
///
/// The fix skips (rather than aborts on) an &lt;item&gt; that has no "x" attribute -- it is the default/
/// subtotal marker, not a raw shared-item entry -- and only declines the reconstruction for a genuine
/// inconsistency (a real "x" that is out of range or a duplicate). These tests pin: (1) a field with a
/// leading blank shared item AND a trailing default marker still hides exactly the item the user hid, and
/// (2) the ordinary sibling case -- no blank/missing shared item, but still carrying the standard trailing
/// default marker -- still filters exactly as before.
/// </summary>
public sealed class R32Meta1_PivotHiddenItemTrailingDefaultMarkerTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void Load_NativeRowFieldHiddenItem_WithLeadingMissingSharedItemAndTrailingDefaultMarker_StillHidesCorrectItem()
    {
        // Raw OOXML sharedItems: <m/><s v="North"/><s v="South"/><s v="East"/> (raw indices 0..3, count="4").
        // The pivotField's <items> list mirrors exactly what real Excel writes for this shape: one <item>
        // per raw shared-item slot (blank bucket flagged m="1", "South" flagged hidden="1"), PLUS a trailing
        // <item t="default"/> with no "x" attribute at all.
        //
        // Before this fix: the trailing no-"x" item made ReadIntAttribute return null, and the very first
        // guard in the loop (`rawIndex is not { } index ... return null`) discarded the whole reconstruction
        // regardless of how many real items had already been resolved -- so SelectedItems came back null and
        // "South" kept showing despite being unchecked in the source file. The fix must hide exactly
        // "South" and nothing else.
        using var package = CreatePivotWorkbookPackage(withLeadingMissingSharedItem: true, hiddenRawIndex: 2);

        var workbook = new XlsxFileAdapter().Load(package);

        var rowField = workbook.GetSheetAt(0).PivotTables.Should().ContainSingle().Subject
            .RowFields.Should().ContainSingle().Subject;
        rowField.SourceFieldIndex.Should().Be(0);
        rowField.SelectedItems.Should().Equal("North", "East");
    }

    [Fact]
    public void Load_NativeRowFieldHiddenItem_WithNoMissingSharedItemsButTrailingDefaultMarker_StillResolvesVisibleSelection()
    {
        // Sibling case: no <m/> was ever dropped (SharedItemCount matches the materialized list, so this
        // field doesn't even go through the raw-index reconstruction path), but the pivotField's <items>
        // list still carries the standard trailing <item t="default"/> marker every real pivot table has.
        // Must keep resolving correctly and not be disrupted by the new no-"x" handling either.
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
                // pivot field's source column contains a blank cell, PLUS the trailing default/subtotal
                // marker Excel (and XlsxPivotTableWriter.cs:443-445) always appends.
                items.SetAttributeValue("count", "5");
                items.Add(
                    new XElement(WorkbookNs + "item", new XAttribute("x", "0"), new XAttribute("m", "1")),
                    new XElement(WorkbookNs + "item", new XAttribute("x", "1")),
                    new XElement(
                        WorkbookNs + "item",
                        new XAttribute("x", hiddenRawIndex.ToString()),
                        new XAttribute("hidden", "1")),
                    new XElement(WorkbookNs + "item", new XAttribute("x", "3")),
                    new XElement(WorkbookNs + "item", new XAttribute("t", "default")));
            }
            else
            {
                items.SetAttributeValue("count", "2");
                items.Add(
                    new XElement(
                        WorkbookNs + "item",
                        new XAttribute("x", hiddenRawIndex.ToString()),
                        new XAttribute("hidden", "1")),
                    new XElement(WorkbookNs + "item", new XAttribute("t", "default")));
            }
        });

        return package;
    }

    private static Workbook CreatePivotWorkbook()
    {
        var workbook = new Workbook("PivotRowFieldTrailingDefaultMarker");
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
