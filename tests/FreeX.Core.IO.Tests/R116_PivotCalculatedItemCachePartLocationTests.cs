using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R116-io-pivot-calcitem-part: CT_pivotTableDefinition has NO calculatedItems child at all (confirmed
/// via reflection against DocumentFormat.OpenXml.Spreadsheet.PivotTableDefinition, which exposes no
/// CalculatedItems property). CT_PivotCacheDefinition (ECMA-376 18.10.1.3) is the real home for
/// calculatedItems (PivotCacheDefinition.CalculatedItems exists, positioned after TupleCache/before
/// CalculatedMembers). XlsxPivotTableWriter previously wrote &lt;calculatedItems&gt; as a direct child of
/// pivotTableDefinition (xl/pivotTables/pivotTableN.xml) -- schema-invalid OOXML that real Excel's
/// repair flow silently drops on open, along with an invalid `name` attribute CT_CalculatedItem does not
/// declare (only field/formula attributes plus a required pivotArea child).
/// </summary>
public sealed class R116_PivotCalculatedItemCachePartLocationTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static XDocument LoadPackageXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static (Workbook Workbook, Sheet Sheet, PivotTableModel Pivot) BuildWorkbookWithPivot()
    {
        var workbook = new Workbook("PivotCalcItemLocation");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Data",
            SourceReference = "A1:B3"
        });
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Region"));
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Amount"));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 9, 2))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum", 1));
        sheet.PivotTables.Add(pivot);

        return (workbook, sheet, pivot);
    }

    // FAIL BEFORE / PASS AFTER: proves the element moved to the real schema location and the invalid
    // "name" attribute is gone, while the Name still round-trips through FreeX's own reader.
    [Fact]
    public void Save_CalculatedItem_IsWrittenToPivotCacheDefinitionNotPivotTableDefinition()
    {
        var (workbook, _, pivot) = BuildWorkbookWithPivot();
        pivot.CalculatedItems.Add(new PivotCalculatedItemModel(0, "East + West", "East+West"));

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var pivotXml = LoadPackageXml(archive.GetEntry("xl/pivotTables/pivotTable1.xml")!);
            var cacheXml = LoadPackageXml(archive.GetEntry("xl/pivotCache/pivotCacheDefinition1.xml")!);

            // The bug: pivotTableDefinition must NOT carry a calculatedItems child (CT_pivotTableDefinition
            // has no such element in the real schema).
            pivotXml.Root!.Element(WorkbookNs + "calculatedItems").Should().BeNull(
                "CT_pivotTableDefinition has no calculatedItems child; real Excel repairs/drops it there");

            // The fix: pivotCacheDefinition carries it instead (CT_PivotCacheDefinition.calculatedItems).
            var calculatedItemsElement = cacheXml.Root!.Element(WorkbookNs + "calculatedItems");
            calculatedItemsElement.Should().NotBeNull("CT_PivotCacheDefinition is the real schema home for calculatedItems");

            var calculatedItem = calculatedItemsElement!.Elements(WorkbookNs + "calculatedItem").Should().ContainSingle().Subject;
            calculatedItem.Attribute("field")!.Value.Should().Be("0");
            calculatedItem.Attribute("formula")!.Value.Should().Be("East+West");

            // CT_CalculatedItem declares only field/formula attributes -- no "name" (confirmed via
            // reflection against DocumentFormat.OpenXml.Spreadsheet.CalculatedItem).
            calculatedItem.Attribute("name").Should().BeNull("CT_CalculatedItem has no name attribute");

            // pivotArea remains the required child that targets the field.
            var pivotArea = calculatedItem.Element(WorkbookNs + "pivotArea");
            pivotArea.Should().NotBeNull();
        }

        // FreeX's own round trip must still recover the Name (preserved via the item's own extLst, since
        // the real schema has no attribute home for it).
        saved.Position = 0;
        var loaded = new XlsxFileAdapter().Load(saved);
        var loadedPivot = loaded.GetSheetAt(0).PivotTables.Should().ContainSingle().Subject;
        loadedPivot.CalculatedItems.Should().ContainSingle()
            .Which.Should().Be(new PivotCalculatedItemModel(0, "East + West", "East+West"));
    }

    // NO-REGRESSION SIBLING: a pivot table with no calculated items must not gain a spurious
    // calculatedItems element in EITHER part as a side effect of the relocation.
    [Fact]
    public void Save_NoCalculatedItems_OmitsCalculatedItemsElementFromBothParts()
    {
        var (workbook, _, _) = BuildWorkbookWithPivot();

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var pivotXml = LoadPackageXml(archive.GetEntry("xl/pivotTables/pivotTable1.xml")!);
        var cacheXml = LoadPackageXml(archive.GetEntry("xl/pivotCache/pivotCacheDefinition1.xml")!);

        pivotXml.Root!.Element(WorkbookNs + "calculatedItems").Should().BeNull();
        cacheXml.Root!.Element(WorkbookNs + "calculatedItems").Should().BeNull();
    }

    // SIBLING behavior: since calculatedItems is now cache-level, a SECOND pivot table sharing the same
    // cache (but never itself given the calculated item) must still see it after a save/load round trip --
    // matching real Excel, where a calculated item defined via one pivot's Analyze > Fields, Items & Sets
    // dialog is visible from every pivot table built on that same cache.
    [Fact]
    public void Save_TwoPivotTablesSharingCache_BothSeeTheCalculatedItemAfterRoundTrip()
    {
        var (workbook, sheet, pivot1) = BuildWorkbookWithPivot();
        pivot1.CalculatedItems.Add(new PivotCalculatedItemModel(0, "East + West", "East+West"));

        var pivot2 = new PivotTableModel
        {
            Name = "PivotTable2",
            CacheId = 1,
            SourceRange = pivot1.SourceRange,
            TargetRange = new GridRange(new CellAddress(sheet.Id, 12, 1), new CellAddress(sheet.Id, 16, 2))
        };
        pivot2.RowFields.Add(new PivotFieldModel(0));
        pivot2.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum", 1));
        sheet.PivotTables.Add(pivot2);

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        var loaded = new XlsxFileAdapter().Load(saved);
        var loadedPivots = loaded.GetSheetAt(0).PivotTables;
        loadedPivots.Should().HaveCount(2);
        foreach (var loadedPivot in loadedPivots)
        {
            loadedPivot.CalculatedItems.Should().ContainSingle()
                .Which.Should().Be(new PivotCalculatedItemModel(0, "East + West", "East+West"));
        }
    }
}
