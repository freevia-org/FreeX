using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R78-io-pivotcache-5-1: a Table(ListObject)-sourced pivot cache with no SourceSheetName/SourceReference
/// (the shape a real xlsx Table cache -- or a workbook round-tripped through the FreeX-native format --
/// carries per CT_WorksheetSource, ECMA-376 18.10.2.42) must still resolve its data range via
/// SourceTableName so pivotCacheRecords isn't silently wiped to zero rows on the no-source-package
/// full-rewrite save path.
///
/// R78-io-pivotcache-5-2: a date/number-range-grouped cache field's native CT_GroupItems label list
/// (ECMA-376 18.10.1.36) must be modeled, read, and re-emitted under &lt;fieldGroup&gt; so a
/// pivotTable's pivotField/items index positions still resolve to real labels on reopen.
/// </summary>
public sealed class R78_PivotCacheTableSourceRecordsAndGroupItemsTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void Save_TableSourcedCacheWithNoSourceSheetOrReference_ResolvesRecordsFromStructuredTable()
    {
        // Simulates a cache reloaded from a real xlsx (or round-tripped through the FreeX-native format,
        // which copies SourceSheetName/SourceReference/SourceTableName through unchanged): the reader
        // never populates SourceSheetName/SourceReference for a Table source, only SourceTableName.
        var workbook = new Workbook("PivotTableSourceRecords");
        var sheet = workbook.AddSheet("SalesData");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "SalesTable",
            DisplayName = "SalesTable",
            Range = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 2)),
            HasAutoFilter = true,
            HeaderRowCount = 1,
        });

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.Table,
            SourceSheetName = null,
            SourceReference = null,
            SourceTableName = "SalesTable",
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

        // No XlsxSourcePackage is attached to this workbook, so saving takes the no-source-package
        // full-rewrite branch that unconditionally calls XlsxPivotTableWriter.Save.
        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var recordsXml = XlsxPackageTestHelper.ReadPackageXml(package, "xl/pivotCache/pivotCacheRecords1.xml");

        recordsXml.Root!.Attribute("count")!.Value.Should().Be("2");
        recordsXml.Root!.Elements(WorkbookNs + "r").Should().HaveCount(2);

        var definitionXml = XlsxPackageTestHelper.ReadPackageXml(package, "xl/pivotCache/pivotCacheDefinition1.xml");
        definitionXml.Root!.Attribute("recordCount")!.Value.Should().Be("2");
    }

    [Fact]
    public void Save_WorksheetRangeSourcedCache_StillResolvesRecordsWithoutStructuredTable()
    {
        // No-regression sibling: a plain WorksheetRange-sourced cache (the common case, no
        // SourceTableName at all) must keep resolving via SourceSheetName/SourceReference exactly as
        // before -- the new table-name fallback must not be consulted or required for this path.
        var workbook = new Workbook("PivotRangeSourceRecords");
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
            SourceTableName = null,
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

        recordsXml.Root!.Attribute("count")!.Value.Should().Be("2");
        recordsXml.Root!.Elements(WorkbookNs + "r").Should().HaveCount(2);
    }

    [Fact]
    public void SaveThenLoad_MonthGroupedCacheField_RoundTripsGroupItemsLabelList()
    {
        var workbook = new Workbook("PivotGroupItemsRoundTrip");
        var sheet = workbook.AddSheet("SalesData");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Date"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), DateTimeValue.FromDateTime(new DateTime(2024, 1, 15)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), DateTimeValue.FromDateTime(new DateTime(2024, 2, 15)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        var monthLabels = new[] { "Jan", "Feb" };
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
            "Date",
            ContainsDate: true,
            Grouping: PivotFieldGrouping.Month,
            GroupStartDate: "2024-01-01T00:00:00",
            GroupEndDate: "2024-12-31T00:00:00",
            GroupItems: monthLabels));
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
        var definitionXml = XlsxPackageTestHelper.ReadPackageXml(package, "xl/pivotCache/pivotCacheDefinition1.xml");
        var dateField = definitionXml.Root!
            .Element(WorkbookNs + "cacheFields")!
            .Elements(WorkbookNs + "cacheField")
            .First(f => f.Attribute("name")!.Value == "Date");

        var groupItemsXml = dateField
            .Element(WorkbookNs + "fieldGroup")!
            .Element(WorkbookNs + "groupItems");
        groupItemsXml.Should().NotBeNull();
        groupItemsXml!.Attribute("count")!.Value.Should().Be("2");
        groupItemsXml.Elements(WorkbookNs + "s").Select(e => e.Attribute("v")!.Value)
            .Should().Equal("Jan", "Feb");

        package.Position = 0;
        var loadedCache = new XlsxFileAdapter().Load(package).PivotCaches.Should().ContainSingle().Subject;
        var loadedDateField = loadedCache.Fields.Should().Contain(f => f.Name == "Date").Subject;
        loadedDateField.GroupItems.Should().Equal("Jan", "Feb");
    }

    [Fact]
    public void SaveThenLoad_UngroupedCacheField_HasNoGroupItems()
    {
        // No-regression sibling: a plain (non-grouped) cache field must not gain a spurious
        // fieldGroup/groupItems element, and GroupItems must round-trip as null.
        var workbook = new Workbook("PivotNoGroupItems");
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
        var definitionXml = XlsxPackageTestHelper.ReadPackageXml(package, "xl/pivotCache/pivotCacheDefinition1.xml");
        var regionField = definitionXml.Root!
            .Element(WorkbookNs + "cacheFields")!
            .Elements(WorkbookNs + "cacheField")
            .First(f => f.Attribute("name")!.Value == "Region");

        regionField.Element(WorkbookNs + "fieldGroup").Should().BeNull();

        package.Position = 0;
        var loadedCache = new XlsxFileAdapter().Load(package).PivotCaches.Should().ContainSingle().Subject;
        var loadedRegionField = loadedCache.Fields.Should().Contain(f => f.Name == "Region").Subject;
        loadedRegionField.GroupItems.Should().BeNull();
    }
}
