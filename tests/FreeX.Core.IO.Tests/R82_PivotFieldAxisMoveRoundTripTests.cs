using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R82-io-pivot-layout-5-1 regression test for src/FreeX.Core.IO/XlsxFileAdapter.SavePostProcessing.cs:
/// moving a pivot field between the Row/Column/Filter areas (what ConfigurePivotTableLayoutCommand does
/// in memory) was silently discarded on save for any pivot table loaded from an existing .xlsx --
/// XlsxPivotTableWriter.Save (the only code that regenerated &lt;rowFields&gt;/&lt;colFields&gt;/
/// &lt;pageFields&gt; and each &lt;pivotField&gt;'s own axis attribute from the current model) is gated
/// behind <c>!hasSourcePackage</c>, and the preserved-part patches (RewritePivotTableFilterState/
/// RewritePivotTableLayoutState) explicitly left axis reassignment "out of scope." Fixed by the new
/// RewritePivotTableFieldAxes, which patches the preserved part's field-area containers and per-field
/// axis attribute in place from the model, matching how the sibling filter-state/layout-state rewrites
/// already work.
/// </summary>
public sealed class R82_PivotFieldAxisMoveRoundTripTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void SaveThenReload_FieldMovedFromRowToColumnOnLoadedWorkbook_PersistsTheNewArea()
    {
        // Simulates: open an existing .xlsx with 'Region' in Rows, drag it into Columns (exactly what
        // ConfigurePivotTableLayoutCommand.Apply does to the in-memory model), then save the SAME file
        // (source-preserved path) -- exactly the finding's failure scenario.
        using var source = XlsxPackageTestHelper.SaveWorkbook(CreateRegionPivotWorkbook());

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var pivot = loaded.GetSheetAt(0).PivotTables.Single();
        pivot.RowFields.Should().ContainSingle(field => field.SourceFieldIndex == 0);
        pivot.ColumnFields.Should().BeEmpty();

        var regionField = pivot.RowFields.Single();
        pivot.RowFields.Clear();
        pivot.ColumnFields.Add(regionField);

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        var pivotDefinitionRoot = XlsxPackageTestHelper.ReadPackageXml(saved, "xl/pivotTables/pivotTable1.xml").Root!;
        pivotDefinitionRoot.Element(WorkbookNs + "rowFields").Should().BeNull("Region no longer has any field left on the Rows axis");
        var colFieldsElement = pivotDefinitionRoot.Element(WorkbookNs + "colFields");
        colFieldsElement.Should().NotBeNull();
        colFieldsElement!.Elements(WorkbookNs + "field").Select(e => (int)e.Attribute("x")!).Should().Equal(0);

        var pivotFieldElement = pivotDefinitionRoot.Element(WorkbookNs + "pivotFields")!.Elements(WorkbookNs + "pivotField").First();
        pivotFieldElement.Attribute("axis")!.Value.Should().Be("axisCol");

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var reloadedPivot = reloaded.GetSheetAt(0).PivotTables.Single();
        reloadedPivot.RowFields.Should().BeEmpty();
        reloadedPivot.ColumnFields.Should().ContainSingle(field => field.SourceFieldIndex == 0);
    }

    [Fact]
    public void SaveThenReload_FieldMovedFromRowToFilterOnLoadedWorkbook_PersistsTheNewArea()
    {
        // No-regression sibling: a different target area (Filters/pageFields, which needs pivot-cache
        // shared-item resolution XlsxPivotTableWriter.ToPivotPageFieldsXml already handles for a fresh
        // save) must round-trip through the same preserved-part rewrite just as the Row->Column move does.
        using var source = XlsxPackageTestHelper.SaveWorkbook(CreateRegionPivotWorkbook());

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var pivot = loaded.GetSheetAt(0).PivotTables.Single();
        var regionField = pivot.RowFields.Single();
        pivot.RowFields.Clear();
        pivot.PageFields.Add(regionField);

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        var pivotDefinitionRoot = XlsxPackageTestHelper.ReadPackageXml(saved, "xl/pivotTables/pivotTable1.xml").Root!;
        pivotDefinitionRoot.Element(WorkbookNs + "rowFields").Should().BeNull();
        pivotDefinitionRoot.Element(WorkbookNs + "colFields").Should().BeNull();
        var pageFieldsElement = pivotDefinitionRoot.Element(WorkbookNs + "pageFields");
        pageFieldsElement.Should().NotBeNull();
        pageFieldsElement!.Elements(WorkbookNs + "pageField").Select(e => (int)e.Attribute("fld")!).Should().Equal(0);

        var pivotFieldElement = pivotDefinitionRoot.Element(WorkbookNs + "pivotFields")!.Elements(WorkbookNs + "pivotField").First();
        pivotFieldElement.Attribute("axis")!.Value.Should().Be("axisPage");

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var reloadedPivot = reloaded.GetSheetAt(0).PivotTables.Single();
        reloadedPivot.RowFields.Should().BeEmpty();
        reloadedPivot.ColumnFields.Should().BeEmpty();
        reloadedPivot.PageFields.Should().ContainSingle(field => field.SourceFieldIndex == 0);
    }

    [Fact]
    public void SaveThenReload_CombinedAxisFilterAndLayoutChanges_AllPersistInOneRewritePass()
    {
        using var source = XlsxPackageTestHelper.SaveWorkbook(CreateRegionPivotWorkbook());

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var pivot = loaded.GetSheetAt(0).PivotTables.Single();
        var regionField = pivot.RowFields.Single();
        pivot.RowFields.Clear();
        pivot.PageFields.Add(regionField with { SelectedItem = "West" });
        pivot.ShowRowGrandTotals = false;
        pivot.ReportLayout = PivotReportLayout.Outline;
        pivot.DataFields[0] = pivot.DataFields[0] with { SummaryFunction = "count" };

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        saved.Position = 0;
        var reloadedPivot = adapter.Load(saved).GetSheetAt(0).PivotTables.Single();
        reloadedPivot.RowFields.Should().BeEmpty();
        reloadedPivot.PageFields.Should().ContainSingle()
            .Which.SelectedItem.Should().Be("West");
        reloadedPivot.ShowRowGrandTotals.Should().BeFalse();
        reloadedPivot.ReportLayout.Should().Be(PivotReportLayout.Outline);
        reloadedPivot.DataFields.Should().ContainSingle()
            .Which.SummaryFunction.Should().Be("count");
    }

    [Fact]
    public void SaveThenReload_DensePivotFieldAxisMove_PreservesEveryFieldOrder()
    {
        const int fieldCount = 33;
        using var source = XlsxPackageTestHelper.SaveWorkbook(CreateWidePivotWorkbook(fieldCount));

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var pivot = loaded.GetSheetAt(0).PivotTables.Single();
        var rowFields = pivot.RowFields.ToArray();
        rowFields.Should().HaveCount(fieldCount - 1);

        pivot.RowFields.Clear();
        foreach (var field in rowFields)
            pivot.ColumnFields.Add(field);

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);

        var root = XlsxPackageTestHelper.ReadPackageXml(saved, "xl/pivotTables/pivotTable1.xml").Root!;
        root.Element(WorkbookNs + "rowFields").Should().BeNull();
        root.Element(WorkbookNs + "colFields")!
            .Elements(WorkbookNs + "field")
            .Select(field => (int)field.Attribute("x")!)
            .Should().Equal(Enumerable.Range(0, fieldCount - 1),
                "the cached metadata must retain all dense column-field indexes and their model order");
        root.Element(WorkbookNs + "pivotFields")!
            .Elements(WorkbookNs + "pivotField")
            .Take(fieldCount - 1)
            .Should().OnlyContain(field => field.Attribute("axis") != null && field.Attribute("axis")!.Value == "axisCol");
    }

    private static Workbook CreateRegionPivotWorkbook()
    {
        var workbook = new Workbook("R82PivotFieldAxisMoveWorkbook");
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

    private static Workbook CreateWidePivotWorkbook(int fieldCount)
    {
        var workbook = new Workbook("R82WidePivotFieldWorkbook");
        var sheet = workbook.AddSheet("Data");
        for (var column = 0; column < fieldCount; column++)
        {
            var columnIndex = (uint)column + 1;
            sheet.SetCell(new CellAddress(sheet.Id, 1, columnIndex), new TextValue($"Field{column}"));
            sheet.SetCell(new CellAddress(sheet.Id, 2, columnIndex),
                column == fieldCount - 1 ? new NumberValue(1) : new TextValue("A"));
            sheet.SetCell(new CellAddress(sheet.Id, 3, columnIndex),
                column == fieldCount - 1 ? new NumberValue(2) : new TextValue("B"));
        }

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = $"A1:{CellAddress.NumberToColumnName((uint)fieldCount)}3",
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
            RecordCount = 2,
        };
        for (var column = 0; column < fieldCount - 1; column++)
        {
            cache.Fields.Add(new PivotCacheFieldModel(
                $"Field{column}",
                SharedItemCount: 2,
                ContainsString: true,
                SharedItems: ["A", "B"],
                SharedItemKinds: ['s', 's']));
        }
        cache.Fields.Add(new PivotCacheFieldModel($"Field{fieldCount - 1}", ContainsNumber: true));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, (uint)fieldCount)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 8, 2)),
            PackagePart = "xl/pivotTables/pivotTable1.xml",
        };
        for (var column = 0; column < fieldCount - 1; column++)
            pivot.RowFields.Add(new PivotFieldModel(column));
        pivot.DataFields.Add(new PivotDataFieldModel(fieldCount - 1, $"Sum of Field{fieldCount - 1}", "sum"));
        sheet.PivotTables.Add(pivot);

        return workbook;
    }
}
