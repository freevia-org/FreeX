using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxPivotTableNativeIoSemanticTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void Load_NativePageFieldItemIndex_MapsSelectedItemFromCacheSharedItems()
    {
        using var package = CreatePivotWorkbookPackage(PivotCacheSourceType.WorksheetRange);
        XlsxPackageTestHelper.PatchPackageXml(package, "xl/pivotTables/pivotTable1.xml", document =>
        {
            var pageField = document.Root!
                .Element(WorkbookNs + "pageFields")!
                .Element(WorkbookNs + "pageField")!;
            pageField.Attribute("name")?.Remove();
            pageField.SetAttributeValue("item", "1");
        });

        var workbook = new XlsxFileAdapter().Load(package);

        var pageField = workbook.GetSheetAt(0).PivotTables.Should().ContainSingle().Subject
            .PageFields.Should().ContainSingle().Subject;
        pageField.SourceFieldIndex.Should().Be(0);
        pageField.SelectedItem.Should().Be("West");
    }

    [Fact]
    public void Save_SelectedPageFieldItem_WritesNativePageFieldItemIndex()
    {
        var workbook = CreatePivotWorkbook(PivotCacheSourceType.WorksheetRange);
        var pageField = workbook.GetSheetAt(0).PivotTables.Single().PageFields.Single();
        workbook.GetSheetAt(0).PivotTables.Single().PageFields[0] = pageField with { SelectedItem = "West" };

        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var pivotTable = XlsxPackageTestHelper.ReadPackageXml(package, "xl/pivotTables/pivotTable1.xml");

        var nativePageField = pivotTable.Root!
            .Element(WorkbookNs + "pageFields")!
            .Element(WorkbookNs + "pageField")!;
        nativePageField.Attribute("item")!.Value.Should().Be("1");
        nativePageField.Attribute("name").Should().BeNull();
    }

    [Fact]
    public void Save_TablePivotCacheSource_WritesTrueWorksheetSourceNameWithoutRange()
    {
        var workbook = CreatePivotWorkbook(PivotCacheSourceType.Table);

        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var cacheDefinition = XlsxPackageTestHelper.ReadPackageXml(package, "xl/pivotCache/pivotCacheDefinition1.xml");

        var worksheetSource = cacheDefinition.Root!
            .Element(WorkbookNs + "cacheSource")!
            .Element(WorkbookNs + "worksheetSource")!;
        worksheetSource.Attribute("name")!.Value.Should().Be("SalesTable");
        worksheetSource.Attribute("ref").Should().BeNull();
        worksheetSource.Attribute("sheet").Should().BeNull();

        package.Position = 0;
        var loadedCache = new XlsxFileAdapter().Load(package).PivotCaches.Should().ContainSingle().Subject;
        loadedCache.SourceType.Should().Be(PivotCacheSourceType.Table);
        loadedCache.SourceTableName.Should().Be("SalesTable");
        loadedCache.SourceReference.Should().BeNull();
    }

    [Fact]
    public void Load_CacheLevelDateFieldGroup_MapsGroupingToPivotField()
    {
        using var package = CreatePivotWorkbookPackage(PivotCacheSourceType.WorksheetRange);
        XlsxPackageTestHelper.PatchPackageXml(package, "xl/pivotTables/pivotTable1.xml", document =>
        {
            document.Root!.Element(WorkbookNs + "pageFields")?.Remove();
            document.Root.Add(new XElement(
                WorkbookNs + "rowFields",
                new XAttribute("count", "1"),
                new XElement(WorkbookNs + "field", new XAttribute("x", "0"))));
        });
        XlsxPackageTestHelper.PatchPackageXml(package, "xl/pivotCache/pivotCacheDefinition1.xml", document =>
        {
            var cacheField = document.Root!
                .Element(WorkbookNs + "cacheFields")!
                .Elements(WorkbookNs + "cacheField")
                .First();
            cacheField.SetAttributeValue("name", "OrderDate");

            var sharedItems = cacheField.Element(WorkbookNs + "sharedItems")!;
            sharedItems.RemoveNodes();
            sharedItems.SetAttributeValue("containsString", null);
            sharedItems.SetAttributeValue("containsDate", "1");
            sharedItems.SetAttributeValue("minDate", "2025-01-01T00:00:00");
            sharedItems.SetAttributeValue("maxDate", "2025-03-31T00:00:00");
            sharedItems.Add(
                new XElement(WorkbookNs + "d", new XAttribute("v", "2025-01-15T00:00:00")),
                new XElement(WorkbookNs + "d", new XAttribute("v", "2025-02-15T00:00:00")));

            cacheField.Add(new XElement(
                WorkbookNs + "fieldGroup",
                new XAttribute("base", "0"),
                new XElement(
                    WorkbookNs + "rangePr",
                    new XAttribute("autoStart", "0"),
                    new XAttribute("autoEnd", "0"),
                    new XAttribute("groupBy", "months"),
                    new XAttribute("startDate", "2025-01-01T00:00:00"),
                    new XAttribute("endDate", "2025-03-31T00:00:00"),
                    new XAttribute("groupInterval", "1"))));
        });

        var workbook = new XlsxFileAdapter().Load(package);

        var rowField = workbook.GetSheetAt(0).PivotTables.Should().ContainSingle().Subject.RowFields
            .Should().ContainSingle().Subject;
        rowField.SourceFieldIndex.Should().Be(0);
        rowField.Grouping.Should().Be(PivotFieldGrouping.Month);
        rowField.GroupInterval.Should().Be(1);
    }

    private static MemoryStream CreatePivotWorkbookPackage(PivotCacheSourceType sourceType) =>
        XlsxPackageTestHelper.SaveWorkbook(CreatePivotWorkbook(sourceType));

    private static Workbook CreatePivotWorkbook(PivotCacheSourceType sourceType)
    {
        var workbook = new Workbook("PivotNativeIoSemantics");
        var sheet = workbook.AddSheet("PivotData");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = sourceType,
            SourceSheetName = sheet.Name,
            SourceReference = "A1:B3",
            SourceTableName = sourceType == PivotCacheSourceType.Table ? "SalesTable" : null,
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
            RecordCount = 2,
            CreatedVersion = 8,
            MinRefreshableVersion = 4,
        };
        cache.Fields.Add(new PivotCacheFieldModel(
            "Region",
            SharedItemCount: 3,
            ContainsString: true,
            SharedItems: ["East", "West", "North"],
            SharedItemKinds: ['s', 's', 's']));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", 4, ContainsNumber: true));
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
        pivot.PageFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum", 4));
        sheet.PivotTables.Add(pivot);

        return workbook;
    }
}
