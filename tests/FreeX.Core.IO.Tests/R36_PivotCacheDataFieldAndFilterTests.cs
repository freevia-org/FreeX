using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R36-io-pivot-cache-2-1: CT_DataField's real OOXML attribute is showDataAs (ST_ShowDataAs), not the
/// FreeX-invented showValuesAs -- fixed in XlsxPivotTableReader.DataFields.cs / XlsxPivotTableWriter.cs /
/// XlsxPivotTableWriter.Converters.cs.
///
/// R36-io-pivot-cache-2-2: a date-type CT_RangePr (groupBy=years/quarters/months/days) serializes its
/// bounds as startDate/endDate (not startNum/endNum) -- fixed in XlsxPivotCacheReader.cs /
/// XlsxPivotTableWriter.Cache.cs, with new PivotCacheFieldModel.GroupStartDate/GroupEndDate.
///
/// R36-io-pivot-cache-2-3: native pivot Date Filters (dateBetween, thisQuarter, yearToDate, ...) were not
/// recognized by ReadNativePivotLabelFilterKind and silently dropped -- fixed in
/// XlsxPivotTableReader.FiltersAndSorts.cs, with new PivotLabelFilterKind date members.
/// </summary>
public sealed class R36_PivotCacheDataFieldAndFilterTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // ---------- R36-io-pivot-cache-2-1: showDataAs ----------

    [Fact]
    public void Load_NativeDataField_WithShowDataAsAttribute_MapsToShowValuesAs()
    {
        // Real Excel writes <dataField ... showDataAs="percentOfTotal"/> -- the earlier reader only ever
        // looked at a nonexistent "showValuesAs" attribute, so this was always read back as None.
        using var package = CreatePivotWorkbookPackage();
        XlsxPackageTestHelper.PatchPackageXml(package, "xl/pivotTables/pivotTable1.xml", document =>
        {
            var dataField = document.Root!.Element(WorkbookNs + "dataFields")!.Element(WorkbookNs + "dataField")!;
            dataField.SetAttributeValue("showDataAs", "percentOfTotal");
        });

        var workbook = new XlsxFileAdapter().Load(package);

        var dataField = workbook.GetSheetAt(0).PivotTables.Single().DataFields.Single();
        dataField.ShowValuesAs.Should().Be(PivotShowValuesAs.PercentOfGrandTotal);
    }

    [Fact]
    public void Save_ShowValuesAs_WritesNativeShowDataAsAttributeWithCorrectToken()
    {
        // Sibling: a different ShowValuesAs mode round-trips through the correct showDataAs attribute
        // name AND the correct ST_ShowDataAs token (not the earlier FreeX-invented ones).
        var workbook = CreatePivotWorkbook();
        var pivot = workbook.GetSheetAt(0).PivotTables.Single();
        pivot.DataFields[0] = pivot.DataFields[0] with { ShowValuesAs = PivotShowValuesAs.RunningTotalIn, BaseFieldIndex = 0 };

        using var saved = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var pivotXml = XlsxPackageTestHelper.ReadPackageXml(saved, "xl/pivotTables/pivotTable1.xml");
        var dataFieldXml = pivotXml.Root!.Element(WorkbookNs + "dataFields")!.Element(WorkbookNs + "dataField")!;

        dataFieldXml.Attribute("showDataAs")!.Value.Should().Be("runTotal");
        dataFieldXml.Attribute("showValuesAs").Should().BeNull();

        saved.Position = 0;
        var loaded = new XlsxFileAdapter().Load(saved);
        loaded.GetSheetAt(0).PivotTables.Single().DataFields.Single().ShowValuesAs.Should().Be(PivotShowValuesAs.RunningTotalIn);
    }

    // ---------- R36-io-pivot-cache-2-2: date-group rangePr startDate/endDate ----------

    [Fact]
    public void Load_NativeMonthGroup_WithDateBounds_ReadsGroupStartEndDateNotNumeric()
    {
        // Real Excel serializes a date-type group's bounds as startDate/endDate; startNum/endNum are
        // never present. The old reader only ever looked for startNum/endNum, so GroupStart/GroupEnd
        // came back null and the custom range was lost.
        using var package = CreatePivotWorkbookPackage();
        XlsxPackageTestHelper.PatchPackageXml(package, "xl/pivotCache/pivotCacheDefinition1.xml", document =>
        {
            var cacheField = document.Root!
                .Element(WorkbookNs + "cacheFields")!
                .Elements(WorkbookNs + "cacheField")
                .First(field => string.Equals(field.Attribute("name")?.Value, "Region", StringComparison.Ordinal));
            cacheField.Add(new XElement(
                WorkbookNs + "fieldGroup",
                new XElement(
                    WorkbookNs + "rangePr",
                    new XAttribute("groupBy", "months"),
                    new XAttribute("startDate", "2024-03-01T00:00:00"),
                    new XAttribute("endDate", "2025-02-28T00:00:00"))));
        });

        var workbook = new XlsxFileAdapter().Load(package);

        var regionField = workbook.PivotCaches.Single().Fields.Single(field => field.Name == "Region");
        regionField.Grouping.Should().Be(PivotFieldGrouping.Month);
        regionField.GroupStartDate.Should().Be("2024-03-01T00:00:00");
        regionField.GroupEndDate.Should().Be("2025-02-28T00:00:00");
        regionField.GroupStart.Should().BeNull();
        regionField.GroupEnd.Should().BeNull();
    }

    [Fact]
    public void SaveThenLoad_PivotCacheFieldWithMonthGrouping_AndDateBounds_RoundTripsNativeStartEndDate()
    {
        // Sibling: the writer must emit startDate/endDate (not startNum/endNum) for a date-type grouping
        // when the model carries date bounds, and the reader must read them straight back.
        var workbook = CreatePivotWorkbook();
        workbook.PivotCaches.Single().Fields[0] = workbook.PivotCaches.Single().Fields[0] with
        {
            Grouping = PivotFieldGrouping.Month,
            GroupStartDate = "2024-03-01T00:00:00",
            GroupEndDate = "2025-02-28T00:00:00",
        };

        using var saved = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var cacheXml = XlsxPackageTestHelper.ReadPackageXml(saved, "xl/pivotCache/pivotCacheDefinition1.xml");
        var rangePr = cacheXml.Root!
            .Element(WorkbookNs + "cacheFields")!
            .Elements(WorkbookNs + "cacheField")
            .First(field => string.Equals(field.Attribute("name")?.Value, "Region", StringComparison.Ordinal))
            .Element(WorkbookNs + "fieldGroup")!
            .Element(WorkbookNs + "rangePr")!;
        rangePr.Attribute("startDate")!.Value.Should().Be("2024-03-01T00:00:00");
        rangePr.Attribute("endDate")!.Value.Should().Be("2025-02-28T00:00:00");
        rangePr.Attribute("startNum").Should().BeNull();
        rangePr.Attribute("endNum").Should().BeNull();

        saved.Position = 0;
        var loaded = new XlsxFileAdapter().Load(saved);
        var loadedField = loaded.PivotCaches.Single().Fields.Single(field => field.Name == "Region");
        loadedField.Grouping.Should().Be(PivotFieldGrouping.Month);
        loadedField.GroupStartDate.Should().Be("2024-03-01T00:00:00");
        loadedField.GroupEndDate.Should().Be("2025-02-28T00:00:00");
    }

    [Fact]
    public void SaveThenLoad_PivotCacheFieldWithNumberRangeGrouping_StillUsesNumericBounds()
    {
        // No-regression sibling: NumberRange (non-date) grouping must keep using startNum/endNum, never
        // startDate/endDate, and must not be affected by the date-bounds branch added above.
        var workbook = CreatePivotWorkbook();
        workbook.PivotCaches.Single().Fields[0] = workbook.PivotCaches.Single().Fields[0] with
        {
            Grouping = PivotFieldGrouping.NumberRange,
            GroupStart = 0,
            GroupEnd = 100,
            GroupInterval = 10,
        };

        using var saved = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var cacheXml = XlsxPackageTestHelper.ReadPackageXml(saved, "xl/pivotCache/pivotCacheDefinition1.xml");
        var rangePr = cacheXml.Root!
            .Element(WorkbookNs + "cacheFields")!
            .Elements(WorkbookNs + "cacheField")
            .First(field => string.Equals(field.Attribute("name")?.Value, "Region", StringComparison.Ordinal))
            .Element(WorkbookNs + "fieldGroup")!
            .Element(WorkbookNs + "rangePr")!;
        rangePr.Attribute("startNum")!.Value.Should().Be("0");
        rangePr.Attribute("endNum")!.Value.Should().Be("100");
        rangePr.Attribute("startDate").Should().BeNull();
        rangePr.Attribute("endDate").Should().BeNull();

        saved.Position = 0;
        var loaded = new XlsxFileAdapter().Load(saved);
        var loadedField = loaded.PivotCaches.Single().Fields.Single(field => field.Name == "Region");
        loadedField.GroupStart.Should().Be(0);
        loadedField.GroupEnd.Should().Be(100);
        loadedField.GroupStartDate.Should().BeNull();
        loadedField.GroupEndDate.Should().BeNull();
    }

    // ---------- R36-io-pivot-cache-2-3: native Date Filters ----------

    [Fact]
    public void Load_NativeDateBetweenFilter_IsCapturedNotDropped()
    {
        // Real Excel's Date Filters > Between writes <filter fld="0" type="dateBetween" value1="..."
        // value2="..."/>. The old converter fell through to null for any "date*" token, so both
        // ReadNativePivotValueFilters and ReadNativePivotLabelFilters discarded the whole filter.
        using var package = CreatePivotWorkbookPackage();
        XlsxPackageTestHelper.PatchPackageXml(package, "xl/pivotTables/pivotTable1.xml", document =>
        {
            document.Root!.Add(new XElement(
                WorkbookNs + "filters",
                new XAttribute("count", "1"),
                new XElement(
                    WorkbookNs + "filter",
                    new XAttribute("fld", "0"),
                    new XAttribute("type", "dateBetween"),
                    new XAttribute("value1", "2024-01-01T00:00:00"),
                    new XAttribute("value2", "2024-03-31T00:00:00"))));
        });

        var workbook = new XlsxFileAdapter().Load(package);

        var pivot = workbook.GetSheetAt(0).PivotTables.Single();
        pivot.LabelFilters.Should().ContainSingle().Which.Should().Be(
            new PivotLabelFilterModel(0, PivotLabelFilterKind.DateBetween, "2024-01-01T00:00:00", "2024-03-31T00:00:00"));
        pivot.ValueFilters.Should().BeEmpty();
    }

    [Fact]
    public void Load_NativeThisQuarterFilter_WithNoValueAttribute_IsCapturedNotDropped()
    {
        // Sibling: a "relative period" Date Filter (This Quarter/Year to Date/etc.) carries no value
        // attribute at all -- the period is implied by the type token alone. The old
        // string.IsNullOrEmpty(value) guard would have dropped it even once the token was recognized.
        using var package = CreatePivotWorkbookPackage();
        XlsxPackageTestHelper.PatchPackageXml(package, "xl/pivotTables/pivotTable1.xml", document =>
        {
            document.Root!.Add(new XElement(
                WorkbookNs + "filters",
                new XAttribute("count", "1"),
                new XElement(
                    WorkbookNs + "filter",
                    new XAttribute("fld", "0"),
                    new XAttribute("type", "thisQuarter"))));
        });

        var workbook = new XlsxFileAdapter().Load(package);

        var pivot = workbook.GetSheetAt(0).PivotTables.Single();
        pivot.LabelFilters.Should().ContainSingle().Which.Should().Be(
            new PivotLabelFilterModel(0, PivotLabelFilterKind.ThisQuarter, ""));
    }

    private static MemoryStream CreatePivotWorkbookPackage() =>
        XlsxPackageTestHelper.SaveWorkbook(CreatePivotWorkbook());

    private static Workbook CreatePivotWorkbook()
    {
        var workbook = new Workbook("PivotDataFieldAndFilterTests");
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
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = "A1:B3",
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
            RecordCount = 2,
        };
        cache.Fields.Add(new PivotCacheFieldModel(
            "Region",
            SharedItemCount: 2,
            ContainsString: true,
            SharedItems: ["East", "West"],
            SharedItemKinds: ['s', 's']));
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
