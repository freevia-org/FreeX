using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R114-io-1: ResyncPivotCacheFieldTypeMetadata (XlsxPivotTableWriter.Cache.cs) is documented to
/// "widen (never narrow)" a cacheField's observed type flags/min-max from the current live source
/// data immediately before every save. It correctly does this for numeric MinValue/MaxValue, but
/// previously never widened MinDate/MaxDate for a DateTimeValue column -- so a cacheField loaded (or
/// created) with a narrow date range stayed frozen at that range forever, even though the sibling
/// pivotCacheRecords are freshly regenerated from the live (now wider) date column on every save.
/// That produced a self-contradictory saved pivotCacheDefinition: the declared sharedItems
/// minDate/maxDate summary disagreed with the actual cached records.
/// </summary>
public sealed class R114_PivotCacheDateBoundsResyncTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void Save_DateFieldWithStaleMinMaxDate_WidensToLiveSourceRange()
    {
        // Source sheet's live date column spans Jan through Dec 2026 (six months wider than what the
        // cacheField below claims), mirroring "add rows extending the date range, then Refresh + Save".
        var workbook = new Workbook("PivotDateResync");
        var sheet = workbook.AddSheet("Sales");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("OrderDate"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new DateTimeValue(new DateTime(2026, 1, 15).ToOADate()));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new DateTimeValue(new DateTime(2026, 12, 20).ToOADate()));
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
        // Stale metadata as if the cache was created/loaded back when the source only spanned Jan-Jun.
        cache.Fields.Add(new PivotCacheFieldModel(
            "OrderDate",
            ContainsDate: true,
            MinDate: "2026-01-15T00:00:00",
            MaxDate: "2026-06-30T00:00:00"));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true, MinValue: 10, MaxValue: 10));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 8, 2)),
            PackagePart = "xl/pivotTables/pivotTable1.xml",
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var cacheDefinition = XlsxPackageTestHelper.ReadPackageXml(package, "xl/pivotCache/pivotCacheDefinition1.xml");

        var dateField = cacheDefinition.Root!
            .Element(WorkbookNs + "cacheFields")!
            .Elements(WorkbookNs + "cacheField")
            .First(f => f.Attribute("name")!.Value == "OrderDate");
        var sharedItems = dateField.Element(WorkbookNs + "sharedItems")!;

        // The defect: without the fix these stay frozen at the stale "2026-06-30T00:00:00".
        sharedItems.Attribute("minDate")!.Value.Should().Be("2026-01-15T00:00:00");
        sharedItems.Attribute("maxDate")!.Value.Should().Be("2026-12-20T00:00:00");

        // Also reload through the real reader to confirm the widened bounds round-trip, not merely
        // the raw XML attribute values written by the fragment under test.
        package.Position = 0;
        var loadedField = new XlsxFileAdapter().Load(package).PivotCaches.Single().Fields
            .Single(f => f.Name == "OrderDate");
        loadedField.MinDate.Should().Be("2026-01-15T00:00:00");
        loadedField.MaxDate.Should().Be("2026-12-20T00:00:00");
    }

    /// <summary>
    /// No-regression sibling: the pre-existing numeric MinValue/MaxValue widening (the behaviour this
    /// fix must not disturb) still widens correctly on the very same save pass that now also widens
    /// MinDate/MaxDate for the neighbouring date field.
    /// </summary>
    [Fact]
    public void Save_NumericFieldWithStaleMinMaxValue_StillWidensToLiveSourceRange()
    {
        var workbook = new Workbook("PivotNumericResync");
        var sheet = workbook.AddSheet("Sales");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("OrderDate"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new DateTimeValue(new DateTime(2026, 1, 15).ToOADate()));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new DateTimeValue(new DateTime(2026, 6, 30).ToOADate()));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(500));

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = "A1:B3",
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
            RecordCount = 2,
        };
        cache.Fields.Add(new PivotCacheFieldModel("OrderDate", ContainsDate: true));
        // Stale numeric bounds narrower than the live 5..500 range.
        cache.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true, MinValue: 5, MaxValue: 5));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 8, 2)),
            PackagePart = "xl/pivotTables/pivotTable1.xml",
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var cacheDefinition = XlsxPackageTestHelper.ReadPackageXml(package, "xl/pivotCache/pivotCacheDefinition1.xml");

        var amountField = cacheDefinition.Root!
            .Element(WorkbookNs + "cacheFields")!
            .Elements(WorkbookNs + "cacheField")
            .First(f => f.Attribute("name")!.Value == "Amount");
        var sharedItems = amountField.Element(WorkbookNs + "sharedItems")!;

        sharedItems.Attribute("minValue")!.Value.Should().Be("5");
        sharedItems.Attribute("maxValue")!.Value.Should().Be("500");
    }
}
