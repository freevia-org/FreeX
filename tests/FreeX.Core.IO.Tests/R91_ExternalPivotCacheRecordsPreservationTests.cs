using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R91-io-external-data-model-5-1: an External/Consolidation/Scenario-sourced pivot cache has no live
/// worksheet range the no-source-package writer (<see cref="XlsxPivotTableWriter"/>) can re-derive
/// &lt;pivotCacheRecords&gt; rows from -- previously that unconditionally produced an empty
/// &lt;pivotCacheRecords count="0"/&gt;, silently destroying an offline-cached query/consolidation result
/// on every save that takes this path (native .fxl round-trip reload, legacy .xls export). The fix
/// threads a verbatim copy of the original &lt;pivotCacheRecords&gt; XML (captured at load time by
/// <see cref="XlsxPivotCacheReader"/>, carried through <see cref="PivotCacheModel.RawRecordsXml"/> and
/// the NativeJsonAdapter DTOs) back out as passthrough instead. A sibling defect in the same writer --
/// silently reclassifying a Consolidation/Scenario cacheSource as @type="worksheet" with a schema-invalid
/// attribute-less &lt;worksheetSource/&gt; child -- is covered too.
/// </summary>
public sealed class R91_ExternalPivotCacheRecordsPreservationTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private const string SampleRawRecordsXml =
        "<pivotCacheRecords xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" count=\"2\">" +
        "<r><s v=\"East\"/><n v=\"10\"/></r>" +
        "<r><s v=\"West\"/><n v=\"20\"/></r>" +
        "</pivotCacheRecords>";

    [Fact]
    public void Save_ExternalConnectionBackedCacheWithPreservedRecords_WritesRecordsInsteadOfEmpty()
    {
        var workbook = new Workbook("ExternalPivotCache");
        var sheet = workbook.AddSheet("Pivot");

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.External,
            ConnectionId = 5,
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
            RecordCount = 2,
            RawRecordsXml = SampleRawRecordsXml,
        };
        cache.Fields.Add(new PivotCacheFieldModel("Region", ContainsString: true));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 8, 2)),
            PackagePart = "xl/pivotTables/pivotTable1.xml",
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        // No XlsxSourcePackage is attached to this workbook, so saving takes the no-source-package
        // full-rewrite branch that unconditionally calls XlsxPivotTableWriter.Save -- exactly the state
        // a workbook rehydrated from a .fxl (NativeJsonAdapter) file, or bound for legacy .xls export,
        // is in on its next .xlsx save.
        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);

        var recordsXml = XlsxPackageTestHelper.ReadPackageXml(package, "xl/pivotCache/pivotCacheRecords1.xml");
        recordsXml.Root!.Attribute("count")!.Value.Should().Be("2");
        var recordElements = recordsXml.Root!.Elements(WorkbookNs + "r").ToList();
        recordElements.Should().HaveCount(2);
        recordElements[0].Element(WorkbookNs + "s")!.Attribute("v")!.Value.Should().Be("East");
        recordElements[1].Element(WorkbookNs + "s")!.Attribute("v")!.Value.Should().Be("West");

        var definitionXml = XlsxPackageTestHelper.ReadPackageXml(package, "xl/pivotCache/pivotCacheDefinition1.xml");
        definitionXml.Root!.Attribute("recordCount")!.Value.Should().Be("2");
        var cacheSourceXml = definitionXml.Root!.Element(WorkbookNs + "cacheSource")!;
        cacheSourceXml.Attribute("type")!.Value.Should().Be("external");
        cacheSourceXml.Attribute("connectionId")!.Value.Should().Be("5");
    }

    [Fact]
    public void Save_WorksheetRangeSourcedCacheWithStaleRawRecordsXml_StillRegeneratesRecordsFromLiveSheet()
    {
        // No-regression sibling: an ordinary WorksheetRange cache always has a live range to regenerate
        // records from, so even if RawRecordsXml were somehow populated (defensive -- the reader never
        // sets it for this source type) it must never shadow the live data.
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
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
            RecordCount = 2,
            RawRecordsXml = "<pivotCacheRecords xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" count=\"1\"><r><s v=\"Stale\"/></r></pivotCacheRecords>",
        };
        cache.Fields.Add(new PivotCacheFieldModel("Region", ContainsString: true, SharedItems: ["East", "West"], SharedItemKinds: ['s', 's']));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
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
        var recordsXml = XlsxPackageTestHelper.ReadPackageXml(package, "xl/pivotCache/pivotCacheRecords1.xml");

        recordsXml.Root!.Attribute("count")!.Value.Should().Be("2");
        var recordElements = recordsXml.Root!.Elements(WorkbookNs + "r").ToList();
        recordElements.Should().HaveCount(2);
        recordElements[0].Element(WorkbookNs + "s")!.Attribute("v")!.Value.Should().Be("East");
        recordElements[1].Element(WorkbookNs + "s")!.Attribute("v")!.Value.Should().Be("West");
    }

    [Theory]
    [InlineData(PivotCacheSourceType.Consolidation, "consolidation")]
    [InlineData(PivotCacheSourceType.Scenario, "scenario")]
    public void Save_ConsolidationOrScenarioSourcedCache_PreservesOwnTypeAttributeWithoutInvalidWorksheetSourceChild(
        PivotCacheSourceType sourceType,
        string expectedTypeAttribute)
    {
        var workbook = new Workbook("NonWorksheetPivotCache");
        var sheet = workbook.AddSheet("Pivot");

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = sourceType,
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
        };
        cache.Fields.Add(new PivotCacheFieldModel("Region", ContainsString: true));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 8, 2)),
            PackagePart = "xl/pivotTables/pivotTable1.xml",
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        sheet.PivotTables.Add(pivot);

        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var definitionXml = XlsxPackageTestHelper.ReadPackageXml(package, "xl/pivotCache/pivotCacheDefinition1.xml");
        var cacheSourceXml = definitionXml.Root!.Element(WorkbookNs + "cacheSource")!;

        cacheSourceXml.Attribute("type")!.Value.Should().Be(expectedTypeAttribute);
        // No worksheetSource child: CT_CacheSource only allows one for @type="worksheet", and an
        // attribute-less <worksheetSource/> (the pre-fix output) is schema-invalid.
        cacheSourceXml.Element(WorkbookNs + "worksheetSource").Should().BeNull();
    }

    [Fact]
    public void Load_ExternalCacheDefinitionWithRecordsPart_CapturesRawRecordsXmlVerbatim()
    {
        var workbook = new Workbook("ExternalPivotCacheLoad");
        var sheet = workbook.AddSheet("Pivot");

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.External,
            ConnectionId = 9,
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
        };
        cache.Fields.Add(new PivotCacheFieldModel("Region", ContainsString: true));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 8, 2)),
            PackagePart = "xl/pivotTables/pivotTable1.xml",
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        sheet.PivotTables.Add(pivot);

        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);

        // Simulate real Excel's offline-cached query rows landing in the records part (FreeX itself has
        // no way to author these, since the connection is external -- this is exactly the shape a real
        // xlsx built by Excel's Data > Get Data would have).
        XlsxPackageTestHelper.PatchPackageXml(package, "xl/pivotCache/pivotCacheRecords1.xml", document =>
        {
            document.Root!.SetAttributeValue("count", "2");
            document.Root!.Add(
                new XElement(WorkbookNs + "r", new XElement(WorkbookNs + "s", new XAttribute("v", "East"))),
                new XElement(WorkbookNs + "r", new XElement(WorkbookNs + "s", new XAttribute("v", "West"))));
        });

        var loadedCache = new XlsxFileAdapter().Load(package).PivotCaches.Should().ContainSingle().Subject;

        loadedCache.RawRecordsXml.Should().NotBeNullOrWhiteSpace();
        var preserved = XDocument.Parse(loadedCache.RawRecordsXml!);
        var preservedRecords = preserved.Root!.Elements(WorkbookNs + "r").ToList();
        preservedRecords.Should().HaveCount(2);
        preservedRecords[0].Element(WorkbookNs + "s")!.Attribute("v")!.Value.Should().Be("East");
        preservedRecords[1].Element(WorkbookNs + "s")!.Attribute("v")!.Value.Should().Be("West");
    }

    [Fact]
    public void Load_WorksheetRangeSourcedCache_DoesNotCaptureRawRecordsXml()
    {
        // No-regression sibling: an ordinary worksheet/table cache always has a live range the writer
        // can regenerate records from, so the reader must not spend memory capturing a raw-records
        // passthrough copy for it.
        var workbook = new Workbook("PivotRangeSourceRecordsLoad");
        var sheet = workbook.AddSheet("SalesData");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = "A1:A2",
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
        };
        cache.Fields.Add(new PivotCacheFieldModel("Region", ContainsString: true, SharedItems: ["East"], SharedItemKinds: ['s']));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 8, 2)),
            PackagePart = "xl/pivotTables/pivotTable1.xml",
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        sheet.PivotTables.Add(pivot);

        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var loadedCache = new XlsxFileAdapter().Load(package).PivotCaches.Should().ContainSingle().Subject;

        loadedCache.RawRecordsXml.Should().BeNull();
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_PreservesExternalCacheRawRecordsXml()
    {
        // Exercises the exact scenario in the finding: Save As FreeX-native (.fxl) then reopen -- the
        // RawRecordsXml passthrough must survive the NativeJsonAdapter DTO round-trip so a subsequent
        // .xlsx export (via XlsxPivotTableWriter, tested above) still has something to preserve.
        var workbook = new Workbook("ExternalPivotCacheNativeJson");
        var sheet = workbook.AddSheet("Pivot");

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.External,
            ConnectionId = 3,
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
            RawRecordsXml = SampleRawRecordsXml,
        };
        cache.Fields.Add(new PivotCacheFieldModel("Region", ContainsString: true));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 8, 2)),
            PackagePart = "xl/pivotTables/pivotTable1.xml",
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        var adapter = new NativeJsonAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream);
        var loadedCache = loaded.PivotCaches.Should().ContainSingle().Subject;
        loadedCache.RawRecordsXml.Should().Be(SampleRawRecordsXml);
    }
}
