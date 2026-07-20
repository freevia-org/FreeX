using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 55 regression test for src/FreeX.Core.IO/XlsxFileAdapter.SavePostProcessing.cs
/// (R55-meta-2): RewritePreservedPivotValueAndLabelFilters must emit &lt;valueFilters&gt; BEFORE
/// &lt;labelFilters&gt; -- matching both its own doc comment ("valueFilters, then labelFilters")
/// and XlsxPivotTableWriter's own emission order for a fresh part (ToPivotValueFiltersXml before
/// ToPivotLabelFiltersXml). Before the fix, the two AddBeforeSelf calls were issued in the wrong
/// order (label first, then value), so -- because AddBeforeSelf places each new element immediately
/// before the anchor -- the LAST call ended up closest to the anchor, producing the reversed
/// [labelFilters, valueFilters] document order.
/// </summary>
public sealed class R55_PivotFilterOrderTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void SaveThenReload_ValueAndLabelFiltersBothEditedOnLoadedWorkbook_EmitsValueFiltersBeforeLabelFilters()
    {
        // Simulates: open an existing .xlsx (no pivot filters yet), apply BOTH a value filter and a
        // label filter, then save the SAME file (source-preserved path) -- exactly the scenario the
        // finding describes.
        using var source = XlsxPackageTestHelper.SaveWorkbook(CreateRegionPivotWorkbook());

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var pivot = loaded.GetSheetAt(0).PivotTables.Single();
        pivot.ValueFilters.Should().BeEmpty();
        pivot.LabelFilters.Should().BeEmpty();

        pivot.ValueFilters.Add(new PivotValueFilterModel(DataFieldIndex: 0, PivotValueFilterKind.Top, Count: 5));
        pivot.LabelFilters.Add(new PivotLabelFilterModel(SourceFieldIndex: 0, PivotLabelFilterKind.BeginsWith, Value: "E"));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        var pivotDefinitionRoot = XlsxPackageTestHelper.ReadPackageXml(saved, "xl/pivotTables/pivotTable1.xml").Root!;
        var elementNames = pivotDefinitionRoot.Elements()
            .Select(element => element.Name.LocalName)
            .Where(name => name is "valueFilters" or "labelFilters")
            .ToList();

        elementNames.Should().Equal(
            ["valueFilters", "labelFilters"],
            "valueFilters must precede labelFilters, matching XlsxPivotTableWriter's own emission order for a fresh part");

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var reloadedPivot = reloaded.GetSheetAt(0).PivotTables.Single();
        reloadedPivot.ValueFilters.Should().ContainSingle();
        reloadedPivot.LabelFilters.Should().ContainSingle();
    }

    [Fact]
    public void SaveThenReload_OnlyValueFilterEditedOnLoadedWorkbook_StillWritesValueFiltersElement()
    {
        // Sibling no-regression case: editing only ONE of the two filter kinds must not be affected
        // by the ordering fix -- valueFilters alone must still be written and reload correctly.
        using var source = XlsxPackageTestHelper.SaveWorkbook(CreateRegionPivotWorkbook());

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var pivot = loaded.GetSheetAt(0).PivotTables.Single();
        pivot.ValueFilters.Add(new PivotValueFilterModel(DataFieldIndex: 0, PivotValueFilterKind.Top, Count: 5));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        var pivotDefinitionRoot = XlsxPackageTestHelper.ReadPackageXml(saved, "xl/pivotTables/pivotTable1.xml").Root!;
        pivotDefinitionRoot.Element(WorkbookNs + "valueFilters").Should().NotBeNull();
        pivotDefinitionRoot.Element(WorkbookNs + "labelFilters").Should().BeNull();

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        reloaded.GetSheetAt(0).PivotTables.Single().ValueFilters.Should().ContainSingle();
    }

    private static Workbook CreateRegionPivotWorkbook()
    {
        var workbook = new Workbook("R55PivotFilterOrderWorkbook");
        var sheet = workbook.AddSheet("Data");
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
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 8, 2)),
            PackagePart = "xl/pivotTables/pivotTable1.xml",
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        return workbook;
    }
}
