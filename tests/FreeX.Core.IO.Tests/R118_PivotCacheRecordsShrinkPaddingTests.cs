using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R118-io-pivotcache-records-shrink: a structural edit (e.g. deleting an interior source column via
/// RowColumnShiftHelpers.ShiftPivotCaches) narrows cache.SourceReference -- and therefore the live
/// source range's column count -- WITHOUT ever touching cache.Fields, because the only code that
/// narrows cache.Fields to match (PivotTableRefreshService.ReconcileCacheFields) runs solely on an
/// explicit pivot Refresh, not on Save. Before this fix, ToPivotCacheRecordsXml clamped each &lt;r&gt;
/// record's value count to Math.Min(cache.Fields.Count, sourceRange.ColCount), so a save that follows
/// such a shrink (with no intervening refresh) wrote fewer &lt;r&gt; values per record than
/// &lt;cacheFields count="..."&gt; declares. Per CT_Record (ECMA-376 18.10.1.11), a record's unlisted
/// trailing fields are read by Excel as index 0 of that field's own (never-narrowed) sharedItems list
/// -- not as "no data" -- so every row would silently render one fixed stale value for the deleted
/// field's column instead of surfacing its removal. The fix always emits cache.Fields.Count values per
/// record, padding any index beyond the (possibly shrunk) live source range with an explicit
/// &lt;m/&gt; (missing) marker.
/// </summary>
public sealed class R118_PivotCacheRecordsShrinkPaddingTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void Save_CacheFieldsWiderThanShrunkSourceRange_PadsTrailingValuesWithMissingMarker()
    {
        // Simulates the post-column-delete, pre-refresh state: the cache was originally built from a
        // 3-column source (Region, Amount, Note), but a column delete has since narrowed
        // cache.SourceReference to only 2 columns while cache.Fields still lists all 3 -- exactly what
        // RowColumnShiftHelpers.ShiftPivotCaches produces (it rewrites SourceReference only, never
        // cache.Fields).
        var workbook = new Workbook("PivotShrinkPadding");
        var sheet = workbook.AddSheet("SalesData");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            // Shrunk to 2 columns (A:B) by a column delete that never touched cache.Fields below.
            SourceReference = "A1:B3",
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
            RecordCount = 2,
        };
        cache.Fields.Add(new PivotCacheFieldModel("Region", ContainsString: true, SharedItems: ["East", "West"], SharedItemKinds: ['s', 's']));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
        // The deleted column's stale field survives untouched -- still 3 fields declared even though
        // the live source range is now only 2 columns wide.
        cache.Fields.Add(new PivotCacheFieldModel("Note", ContainsString: true, SharedItems: ["stale-old-value"], SharedItemKinds: ['s']));
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

        // No XlsxSourcePackage is attached, so saving takes the no-source-package full-rewrite branch
        // that unconditionally calls XlsxPivotTableWriter.Save (the modeled/!hasSourcePackage path).
        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var definitionXml = XlsxPackageTestHelper.ReadPackageXml(package, "xl/pivotCache/pivotCacheDefinition1.xml");
        var cacheFieldsCount = int.Parse(definitionXml.Root!.Element(WorkbookNs + "cacheFields")!.Attribute("count")!.Value);
        cacheFieldsCount.Should().Be(3);

        var recordsXml = XlsxPackageTestHelper.ReadPackageXml(package, "xl/pivotCache/pivotCacheRecords1.xml");
        var records = recordsXml.Root!.Elements(WorkbookNs + "r").ToList();
        records.Should().HaveCount(2);

        foreach (var record in records)
        {
            // Every record must carry exactly as many value children as <cacheFields count> declares --
            // matching cacheFields.Count (schema-consistent), not the shrunk live source's column count.
            var children = record.Elements().ToList();
            children.Should().HaveCount(cacheFieldsCount);

            // The trailing field (whose source column no longer exists) must be an explicit <m/>
            // (missing) marker, not silently omitted -- an omitted trailing value is read by Excel as
            // index 0 of that field's sharedItems (a stale constant), which is exactly the corruption
            // this fix prevents.
            children[2].Name.LocalName.Should().Be("m");
        }
    }

    [Fact]
    public void Save_CacheFieldsMatchingLiveSourceRange_EmitsRealValuesWithNoPadding()
    {
        // No-regression sibling: the ordinary case (cache.Fields.Count == live source column count, no
        // shrink) must keep emitting real per-column values with no <m/> padding, exactly as before
        // this fix.
        var workbook = new Workbook("PivotNoShrinkPadding");
        var sheet = workbook.AddSheet("SalesData");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
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
        cache.Fields.Add(new PivotCacheFieldModel("Region", ContainsString: true, SharedItems: ["East", "West"], SharedItemKinds: ['s', 's']));
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

        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var recordsXml = XlsxPackageTestHelper.ReadPackageXml(package, "xl/pivotCache/pivotCacheRecords1.xml");
        var records = recordsXml.Root!.Elements(WorkbookNs + "r").ToList();
        records.Should().HaveCount(2);

        records[0].Elements().Should().HaveCount(2);
        records[0].Elements().Should().NotContain(e => e.Name.LocalName == "m");
        records[0].Elements().ElementAt(0).Attribute("v")!.Value.Should().Be("East");
        records[0].Elements().ElementAt(1).Attribute("v")!.Value.Should().Be("10");

        records[1].Elements().ElementAt(0).Attribute("v")!.Value.Should().Be("West");
        records[1].Elements().ElementAt(1).Attribute("v")!.Value.Should().Be("20");
    }
}
