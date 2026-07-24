using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R83-meta-1 / R83-order-guard-invented-sweep-1 regression tests for
/// src/FreeX.Core.IO/XlsxFileAdapter.SavePostProcessing.cs: the r82 pivot fixes
/// (RewritePreservedPivotFieldAxes's canonical order array, and
/// RewritePreservedPivotValueAndLabelFilters's filter shape) only matched the real
/// CT_pivotTableDefinition child sequence / native &lt;filters&gt; shape that
/// XlsxPivotTableWriter itself started emitting in r82 -- both were left stale on the
/// PRESERVED-part (hasSourcePackage) save path.
/// </summary>
public sealed class R83_PreservedPivotOrderAndFilterTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // R83-meta-1: a newly-inserted <pageFields> container must be anchored BEFORE
    // <pivotTableStyleInfo> (and before the native <filters> element that already exists on
    // this preserved part from an earlier label filter), not after -- matching the real
    // CT_pivotTableDefinition child sequence XlsxPivotTableWriter.cs establishes.
    [Fact]
    public void SaveThenReload_NewPageFieldOnPreservedPivotWithExistingFilters_IsAnchoredBeforePivotTableStyleInfo()
    {
        var workbook = CreateRegionPivotWorkbookWithExistingLabelFilter();

        using var source = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var pivot = loaded.GetSheetAt(0).PivotTables.Single();

        // Sanity: the source part already has both a native <filters> element (from the
        // pre-existing label filter) and the required <pivotTableStyleInfo>, and no <pageFields>
        // at all -- exactly the finding's failure-scenario preconditions.
        pivot.PageFields.Should().BeEmpty();
        pivot.LabelFilters.Should().ContainSingle();

        // Move 'Amount' onto the Filter (Page) area for the first time.
        pivot.PageFields.Add(new PivotFieldModel(1));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        var root = XlsxPackageTestHelper.ReadPackageXml(saved, "xl/pivotTables/pivotTable1.xml").Root!;
        var pageFieldsElement = root.Element(WorkbookNs + "pageFields");
        pageFieldsElement.Should().NotBeNull();

        var topLevelNames = root.Elements().Select(e => e.Name.LocalName).ToList();
        var pageFieldsIndex = topLevelNames.IndexOf("pageFields");
        var styleInfoIndex = topLevelNames.IndexOf("pivotTableStyleInfo");
        var filtersIndex = topLevelNames.IndexOf("filters");

        pageFieldsIndex.Should().BeLessThan(styleInfoIndex, "pageFields must precede pivotTableStyleInfo per CT_pivotTableDefinition's real child sequence");
        pageFieldsIndex.Should().BeLessThan(filtersIndex, "pageFields must precede filters too");

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var reloadedPivot = reloaded.GetSheetAt(0).PivotTables.Single();
        reloadedPivot.PageFields.Should().ContainSingle(field => field.SourceFieldIndex == 1);
    }

    // No-regression sibling: when the preserved part has NO <filters>/<valueFilters>/<labelFilters>
    // element at all (only the required <pivotTableStyleInfo>), the newly-inserted container was
    // already anchored correctly before the fix (pivotTableStyleInfo was the first canonical-order
    // entry present either way) -- this must keep working unchanged.
    [Fact]
    public void SaveThenReload_NewPageFieldOnPreservedPivotWithoutExistingFilters_IsStillAnchoredBeforePivotTableStyleInfo()
    {
        var workbook = CreateRegionPivotWorkbookWithExistingLabelFilter(addLabelFilter: false);

        using var source = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var pivot = loaded.GetSheetAt(0).PivotTables.Single();
        pivot.LabelFilters.Should().BeEmpty();

        pivot.PageFields.Add(new PivotFieldModel(1));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        var root = XlsxPackageTestHelper.ReadPackageXml(saved, "xl/pivotTables/pivotTable1.xml").Root!;
        root.Element(WorkbookNs + "filters").Should().BeNull("no filter was configured in this scenario");
        var pageFieldsElement = root.Element(WorkbookNs + "pageFields");
        pageFieldsElement.Should().NotBeNull();

        var topLevelNames = root.Elements().Select(e => e.Name.LocalName).ToList();
        topLevelNames.IndexOf("pageFields").Should().BeLessThan(topLevelNames.IndexOf("pivotTableStyleInfo"));

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        reloaded.GetSheetAt(0).PivotTables.Single().PageFields.Should().ContainSingle(field => field.SourceFieldIndex == 1);
    }

    // R83-order-guard-invented-sweep-1: editing a native-representable label filter (BeginsWith) on
    // a PRESERVED pivot part must still round-trip through the real <filters> element, never the
    // invented, non-schema <labelFilters>/<valueFilters> elements.
    [Fact]
    public void SaveThenReload_EditedLabelFilterOnPreservedPivot_WritesNativeFiltersElement_NotInventedShape()
    {
        var workbook = CreateRegionPivotWorkbookWithExistingLabelFilter();

        using var source = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var pivot = loaded.GetSheetAt(0).PivotTables.Single();
        pivot.LabelFilters.Should().ContainSingle().Which.Value.Should().Be("E");

        // Edit the existing label filter (change from BeginsWith "E" to Contains "s") -- the exact
        // failure scenario: an edit to an existing filter on an already-loaded workbook.
        pivot.LabelFilters.Clear();
        pivot.LabelFilters.Add(new PivotLabelFilterModel(SourceFieldIndex: 0, PivotLabelFilterKind.Contains, Value: "s"));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        var root = XlsxPackageTestHelper.ReadPackageXml(saved, "xl/pivotTables/pivotTable1.xml").Root!;
        root.Element(WorkbookNs + "labelFilters").Should().BeNull("Contains has a real ST_PivotFilterType token and must not use the invented shape");
        root.Element(WorkbookNs + "valueFilters").Should().BeNull("no value filter was configured in this scenario");

        var filtersElement = root.Element(WorkbookNs + "filters");
        filtersElement.Should().NotBeNull();
        var filterElement = filtersElement!.Elements(WorkbookNs + "filter").Single();
        filterElement.Attribute("type")!.Value.Should().Be("captionContains");

        var topLevelNames = root.Elements().Select(e => e.Name.LocalName).ToList();
        topLevelNames.IndexOf("pivotTableStyleInfo").Should().BeLessThan(topLevelNames.IndexOf("filters"));

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        reloaded.GetSheetAt(0).PivotTables.Single().LabelFilters.Should().ContainSingle().Which.Should().Be(
            new PivotLabelFilterModel(SourceFieldIndex: 0, PivotLabelFilterKind.Contains, Value: "s"));
    }

    // No-regression sibling: AboveAverage/BelowAverage value filters have no real ST_PivotFilterType
    // token at all, so an edit that produces one of those kinds on a preserved part must keep
    // round-tripping through the pre-existing FreeX-authored <valueFilters> shape.
    [Fact]
    public void SaveThenReload_EditedToAboveAverageValueFilterOnPreservedPivot_StillRoundTripsThroughLegacyShape()
    {
        var workbook = CreateRegionPivotWorkbookWithExistingLabelFilter(addLabelFilter: false);
        var pivot0 = workbook.GetSheetAt(0).PivotTables.Single();
        pivot0.ValueFilters.Add(new PivotValueFilterModel(DataFieldIndex: 0, PivotValueFilterKind.Top, Count: 1, SourceFieldIndex: 0));

        using var source = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var pivot = loaded.GetSheetAt(0).PivotTables.Single();
        pivot.ValueFilters.Should().ContainSingle().Which.Kind.Should().Be(PivotValueFilterKind.Top);

        pivot.ValueFilters.Clear();
        pivot.ValueFilters.Add(new PivotValueFilterModel(DataFieldIndex: 0, PivotValueFilterKind.AboveAverage, SourceFieldIndex: 0));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        var root = XlsxPackageTestHelper.ReadPackageXml(saved, "xl/pivotTables/pivotTable1.xml").Root!;
        root.Element(WorkbookNs + "valueFilters").Should().NotBeNull();
        root.Element(WorkbookNs + "filters").Should().BeNull("AboveAverage was the only filter configured, and it has no native token");

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        reloaded.GetSheetAt(0).PivotTables.Single().ValueFilters.Should().ContainSingle().Which.Should().Be(
            new PivotValueFilterModel(DataFieldIndex: 0, PivotValueFilterKind.AboveAverage, SourceFieldIndex: 0));
    }

    private static Workbook CreateRegionPivotWorkbookWithExistingLabelFilter(bool addLabelFilter = true)
    {
        var workbook = new Workbook("R83PreservedPivotOrderAndFilterWorkbook");
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
        if (addLabelFilter)
            pivot.LabelFilters.Add(new PivotLabelFilterModel(SourceFieldIndex: 0, PivotLabelFilterKind.BeginsWith, Value: "E"));
        sheet.PivotTables.Add(pivot);

        return workbook;
    }
}
