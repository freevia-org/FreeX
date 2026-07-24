using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R82-io-pivot-layout-5-2 regression test for src/FreeX.Core.IO/XlsxPivotTableWriter.cs: a fresh (no
/// loaded source .xlsx) pivot table save emitted pivot value filters, label filters, and item sort order
/// as non-native, schema-invalid top-level elements (&lt;valueFilters&gt;/&lt;labelFilters&gt;/
/// &lt;pivotSorts&gt;) that don't exist in ECMA-376's CT_pivotTableDefinition content model, even though
/// FreeX's own reader (XlsxPivotTableReader.FiltersAndSorts.cs) already parses the REAL shape -- a single
/// &lt;filters&gt;/&lt;filter&gt; element plus each &lt;pivotField&gt;'s own sortType attribute and
/// &lt;autoSortScope&gt; child. Fixed by writing that real shape instead (verified schema-valid via
/// OpenXmlValidator against FileFormatVersions.Microsoft365).
/// </summary>
public sealed class R82_PivotNativeFilterAndSortRoundTripTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void FreshSave_TopValueFilterLabelFilterAndValueSort_WritesNativeFiltersElement_AndRoundTrips()
    {
        var workbook = CreateFreshRegionPivotWorkbook();
        var pivot = workbook.GetSheetAt(0).PivotTables.Single();

        pivot.ValueFilters.Add(new PivotValueFilterModel(DataFieldIndex: 0, PivotValueFilterKind.Top, Count: 1, SourceFieldIndex: 0));
        pivot.LabelFilters.Add(new PivotLabelFilterModel(SourceFieldIndex: 0, PivotLabelFilterKind.BeginsWith, Value: "E"));
        pivot.Sorts.Add(new PivotSortModel(PivotSortTarget.Value, PivotSortDirection.Descending, DataFieldIndex: 0, FieldIndex: 0));

        using var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved); // no prior Load() => hasSourcePackage is false => the fresh-rebuild writer path runs.

        var pivotDefinitionRoot = XlsxPackageTestHelper.ReadPackageXml(saved, "xl/pivotTables/pivotTable1.xml").Root!;

        // The invented, non-schema elements must be gone.
        pivotDefinitionRoot.Element(WorkbookNs + "labelFilters").Should().BeNull();
        pivotDefinitionRoot.Element(WorkbookNs + "pivotSorts").Should().BeNull();

        // The real <filters> collection must carry both filters.
        var filtersElement = pivotDefinitionRoot.Element(WorkbookNs + "filters");
        filtersElement.Should().NotBeNull();
        var filterElements = filtersElement!.Elements(WorkbookNs + "filter").ToList();
        filterElements.Should().HaveCount(2);
        filterElements[0].Attribute("type")!.Value.Should().Be("count", "real ST_PivotFilterType has no separate top/bottom token");
        filterElements[0].Element(WorkbookNs + "autoFilter").Should().NotBeNull("CT_PivotFilter declares autoFilter as a required child");
        filterElements[1].Attribute("type")!.Value.Should().Be("captionBeginsWith");

        // pivotTableStyleInfo (a required element) must precede filters, matching CT_pivotTableDefinition's
        // real child sequence.
        var topLevelNames = pivotDefinitionRoot.Elements().Select(e => e.Name.LocalName).ToList();
        topLevelNames.IndexOf("pivotTableStyleInfo").Should().BeLessThan(topLevelNames.IndexOf("filters"));

        // The sort lives on the pivotField itself, not a separate root element.
        var regionField = pivotDefinitionRoot.Element(WorkbookNs + "pivotFields")!.Elements(WorkbookNs + "pivotField").First();
        regionField.Attribute("sortType")!.Value.Should().Be("descending");
        regionField.Element(WorkbookNs + "autoSortScope").Should().NotBeNull();

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var reloadedPivot = reloaded.GetSheetAt(0).PivotTables.Single();
        reloadedPivot.ValueFilters.Should().ContainSingle().Which.Should().Be(
            new PivotValueFilterModel(DataFieldIndex: 0, PivotValueFilterKind.Top, Count: 1, SourceFieldIndex: 0));
        reloadedPivot.LabelFilters.Should().ContainSingle().Which.Should().Be(
            new PivotLabelFilterModel(SourceFieldIndex: 0, PivotLabelFilterKind.BeginsWith, Value: "E"));
        reloadedPivot.Sorts.Should().ContainSingle().Which.Should().Be(
            new PivotSortModel(PivotSortTarget.Value, PivotSortDirection.Descending, DataFieldIndex: 0, FieldIndex: 0));
    }

    [Fact]
    public void FreshSave_AboveAverageValueFilter_StillRoundTripsThroughTheLegacyShape()
    {
        // No-regression sibling: AboveAverage/BelowAverage have no representation in the real
        // ST_PivotFilterType at all (confirmed against the OpenXml SDK's own enumeration), so they must
        // keep round-tripping through the pre-existing FreeX-authored <valueFilters> shape rather than
        // being silently dropped now that every other kind moved to the native <filters> element.
        var workbook = CreateFreshRegionPivotWorkbook();
        var pivot = workbook.GetSheetAt(0).PivotTables.Single();
        pivot.ValueFilters.Add(new PivotValueFilterModel(DataFieldIndex: 0, PivotValueFilterKind.AboveAverage, SourceFieldIndex: 0));

        using var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);

        var pivotDefinitionRoot = XlsxPackageTestHelper.ReadPackageXml(saved, "xl/pivotTables/pivotTable1.xml").Root!;
        pivotDefinitionRoot.Element(WorkbookNs + "valueFilters").Should().NotBeNull();
        pivotDefinitionRoot.Element(WorkbookNs + "filters").Should().BeNull("no other filter/sort was added in this scenario");

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        reloaded.GetSheetAt(0).PivotTables.Single().ValueFilters.Should().ContainSingle().Which.Should().Be(
            new PivotValueFilterModel(DataFieldIndex: 0, PivotValueFilterKind.AboveAverage, SourceFieldIndex: 0));
    }

    private static Workbook CreateFreshRegionPivotWorkbook()
    {
        var workbook = new Workbook("R82PivotNativeFilterWorkbook");
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
            SourceSheetName = sheet.Name,
            SourceReference = "A1:B3",
        });
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Region"));
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Amount"));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 8, 2)),
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        return workbook;
    }
}
