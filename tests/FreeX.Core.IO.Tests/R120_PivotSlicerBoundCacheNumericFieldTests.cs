using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-120 regression coverage: <c>XlsxPivotSlicerCacheData.ResolveSlicerSharedItemsField</c> must NOT
/// widen its search to an unrelated pivot cache once the slicer's own bound cache (via
/// <c>SourcePivotTableName</c> -&gt; <c>PivotTableModel.CacheId</c>) has resolved successfully -- even when
/// that cache's own field carries no enumerated <c>SharedItems</c> (the standard OOXML shape for a purely
/// numeric pivot field, where Excel writes only containsNumber/minValue/maxValue on &lt;sharedItems&gt; and
/// omits per-value &lt;n&gt; children entirely). Before the fix, the resolver fell through to a name-only
/// scan across every OTHER cache in the workbook and would happily return a different cache's same-named
/// field, causing the saved slicerCacheDefinition to bind &lt;pivotTables&gt; to one pivot table while its
/// &lt;data&gt;&lt;tabular pivotCacheId&gt; and item list came from a completely unrelated cache.
/// </summary>
public sealed class R120_PivotSlicerBoundCacheNumericFieldTests
{
    // ── Bug case: slicer's OWN bound cache has a numeric field with no enumerated shared items; a
    // completely different cache happens to have a same-named field that DOES have shared items ──────

    [Fact]
    public void ResolveSlicerSharedItemsField_BoundCacheFieldHasNoSharedItems_ReturnsNullNotUnrelatedCache()
    {
        var workbook = BuildNumericBoundCacheWithUnrelatedNamedFieldElsewhere();
        var slicer = workbook.Slicers.Single();

        // The slicer is bound to PivotTable1/Cache1, whose "Year" field is purely numeric (no
        // SharedItems). Cache2's unrelated "Year" field DOES have SharedItems ["2020","2021"] -- before
        // the fix, the resolver would incorrectly return Cache2's field here.
        var resolved = XlsxPivotSlicerCacheData.ResolveSlicerSharedItemsField(workbook, slicer);

        resolved.Should().BeNull(
            "the slicer's own bound cache (Cache1) resolved successfully, so its numeric field's absent " +
            "SharedItems must NOT trigger a fallback scan into Cache2's unrelated same-named field");
    }

    [Fact]
    public void FreshSave_PivotSlicerBoundToNumericFieldCache_AuthorsNoDataElementFromUnrelatedCache()
    {
        var workbook = BuildNumericBoundCacheWithUnrelatedNamedFieldElsewhere();

        using var saved = SaveWorkbook(workbook);
        var dataElements = ReadSlicerCacheDataElements(saved);

        // No native <data><tabular> element at all -- NOT one derived from Cache2 (whose CacheId is 2).
        // Before the fix this would contain a <tabular pivotCacheId="2"> with Cache2's ["2020","2021"]
        // item list, even though <pivotTables><pivotTable name="PivotTable1"/> (Cache1) is what the same
        // slicerCacheDefinition names as its binding.
        dataElements.Should().BeEmpty(
            "PivotTable1's own cache (Cache1) has no enumerated shared items for \"Year\" -- the writer " +
            "must not author a self-contradictory <data> element sourced from the unrelated Cache2");
    }

    // ── Sibling/no-regression case: when the bound pivot table name is genuinely UNRESOLVABLE (stale/
    // absent), the legacy name-only fallback scan across every cache must still work ──────────────────

    [Fact]
    public void ResolveSlicerSharedItemsField_UnresolvableBoundPivotTable_StillFallsBackToNameOnlyScan()
    {
        var workbook = BuildNumericBoundCacheWithUnrelatedNamedFieldElsewhere();
        var slicer = workbook.Slicers.Single();
        slicer.SourcePivotTableName = "NoSuchPivotTable";

        var resolved = XlsxPivotSlicerCacheData.ResolveSlicerSharedItemsField(workbook, slicer);

        resolved.Should().NotBeNull(
            "when SourcePivotTableName cannot be resolved to any pivot table at all, the name-only " +
            "fallback scan across every cache must still find Cache2's \"Year\" field");
        resolved!.Value.Cache.CacheId.Should().Be(2);
        resolved.Value.Field.SharedItems.Should().Equal("2020", "2021");
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static Workbook BuildNumericBoundCacheWithUnrelatedNamedFieldElsewhere()
    {
        var workbook = new Workbook("PivotSlicerNumericBoundCacheR120");

        var sheet1 = workbook.AddSheet("Sales");
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new TextValue("Year"));
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 2), new TextValue("Amount"));
        sheet1.SetCell(new CellAddress(sheet1.Id, 2, 1), new NumberValue(2020));
        sheet1.SetCell(new CellAddress(sheet1.Id, 2, 2), new NumberValue(10));
        sheet1.SetCell(new CellAddress(sheet1.Id, 3, 1), new NumberValue(2021));
        sheet1.SetCell(new CellAddress(sheet1.Id, 3, 2), new NumberValue(20));

        // Cache1: "Year" field is purely numeric -- Excel writes containsNumber/min/maxValue only, no
        // enumerated per-value shared items. This is the slicer's ACTUAL bound cache.
        var cache1 = new PivotCacheModel { CacheId = 1, SourceType = PivotCacheSourceType.WorksheetRange, SourceSheetName = "Sales", SourceReference = "A1:B3" };
        cache1.Fields.Add(new PivotCacheFieldModel("Year", ContainsNumber: true, MinValue: 2020, MaxValue: 2021));
        cache1.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
        workbook.PivotCaches.Add(cache1);

        var pivot1 = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 3, 2)),
            TargetRange = new GridRange(new CellAddress(sheet1.Id, 6, 1), new CellAddress(sheet1.Id, 9, 2))
        };
        pivot1.RowFields.Add(new PivotFieldModel(0));
        pivot1.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet1.PivotTables.Add(pivot1);

        // Cache2: a completely unrelated pivot table also happens to have a "Year" field, but this one
        // was grouped/text-categorized so it DOES carry enumerated shared items.
        var sheet2 = workbook.AddSheet("Inventory");
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new TextValue("Year"));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 2), new TextValue("Stock"));
        sheet2.SetCell(new CellAddress(sheet2.Id, 2, 1), new TextValue("2020"));
        sheet2.SetCell(new CellAddress(sheet2.Id, 2, 2), new NumberValue(5));
        sheet2.SetCell(new CellAddress(sheet2.Id, 3, 1), new TextValue("2021"));
        sheet2.SetCell(new CellAddress(sheet2.Id, 3, 2), new NumberValue(7));

        var cache2 = new PivotCacheModel { CacheId = 2, SourceType = PivotCacheSourceType.WorksheetRange, SourceSheetName = "Inventory", SourceReference = "A1:B3" };
        cache2.Fields.Add(new PivotCacheFieldModel("Year", ContainsString: true, SharedItems: ["2020", "2021"]));
        cache2.Fields.Add(new PivotCacheFieldModel("Stock", ContainsNumber: true));
        workbook.PivotCaches.Add(cache2);

        var pivot2 = new PivotTableModel
        {
            Name = "PivotTable2",
            CacheId = 2,
            SourceRange = new GridRange(new CellAddress(sheet2.Id, 1, 1), new CellAddress(sheet2.Id, 3, 2)),
            TargetRange = new GridRange(new CellAddress(sheet2.Id, 6, 1), new CellAddress(sheet2.Id, 9, 2))
        };
        pivot2.RowFields.Add(new PivotFieldModel(0));
        pivot2.DataFields.Add(new PivotDataFieldModel(1, "Sum of Stock", "sum"));
        sheet2.PivotTables.Add(pivot2);

        var slicer = new SlicerModel
        {
            Name = "Year Slicer",
            CacheName = "Slicer_Year",
            Caption = "Year",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Year",
            StyleName = "SlicerStyleLight2"
        };
        workbook.Slicers.Add(slicer);

        return workbook;
    }

    private static XElement[] ReadSlicerCacheDataElements(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/slicerCaches/slicerCache1.xml");
        entry.Should().NotBeNull();
        using var entryStream = entry!.Open();
        var xml = XDocument.Load(entryStream);
        return xml.Descendants().Where(element => element.Name.LocalName == "data").ToArray();
    }

    private static MemoryStream SaveWorkbook(Workbook workbook)
    {
        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return stream;
    }
}
