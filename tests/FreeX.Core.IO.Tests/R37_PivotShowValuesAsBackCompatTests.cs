using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R37-meta-1: the r36 pivot showDataAs rename (see R36_PivotCacheDataFieldAndFilterTests) made
/// ReadPivotDataFields read ONLY the real OOXML "showDataAs" attribute and dropped the back-compat
/// read of the OLD FreeX-only "showValuesAs" attribute -- fixed in XlsxPivotTableReader.DataFields.cs,
/// which now reads showDataAs first (primary, matches real Excel) and falls back to showValuesAs
/// (via the still-existing ReadPivotShowValuesAs converter in XlsxPivotTableReader.Converters.cs) when
/// showDataAs is absent, so a pivot saved by any pre-r36 FreeX build does not lose its Show-Values-As
/// setting on load.
/// </summary>
public sealed class R37_PivotShowValuesAsBackCompatTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void Load_NativeDataField_WithLegacyShowValuesAsAttribute_FallsBackAndMapsCorrectly()
    {
        // A pivot saved by a pre-r36 FreeX build wrote <dataField ... showValuesAs="percentOfGrandTotal"/>
        // and no showDataAs attribute at all. Before this fix, the reader looked only at showDataAs, so
        // this legacy attribute was silently ignored and ShowValuesAs came back as None.
        using var package = CreatePivotWorkbookPackage();
        XlsxPackageTestHelper.PatchPackageXml(package, "xl/pivotTables/pivotTable1.xml", document =>
        {
            var dataField = document.Root!.Element(WorkbookNs + "dataFields")!.Element(WorkbookNs + "dataField")!;
            dataField.SetAttributeValue("showValuesAs", "percentOfGrandTotal");
        });

        var workbook = new XlsxFileAdapter().Load(package);

        var dataField = workbook.GetSheetAt(0).PivotTables.Single().DataFields.Single();
        dataField.ShowValuesAs.Should().Be(PivotShowValuesAs.PercentOfGrandTotal);
    }

    [Fact]
    public void Load_NativeDataField_WithShowDataAsAttribute_StillTakesPriorityOverShowValuesAs()
    {
        // No-regression sibling: showDataAs (the real, current OOXML attribute) must still be read and
        // must take priority when both attributes happen to be present (e.g. a hand-edited or
        // partially-migrated file), never overridden by the legacy showValuesAs fallback.
        using var package = CreatePivotWorkbookPackage();
        XlsxPackageTestHelper.PatchPackageXml(package, "xl/pivotTables/pivotTable1.xml", document =>
        {
            var dataField = document.Root!.Element(WorkbookNs + "dataFields")!.Element(WorkbookNs + "dataField")!;
            dataField.SetAttributeValue("showDataAs", "runTotal");
            dataField.SetAttributeValue("showValuesAs", "percentOfGrandTotal");
        });

        var workbook = new XlsxFileAdapter().Load(package);

        var dataField = workbook.GetSheetAt(0).PivotTables.Single().DataFields.Single();
        dataField.ShowValuesAs.Should().Be(PivotShowValuesAs.RunningTotalIn);
    }

    private static MemoryStream CreatePivotWorkbookPackage() =>
        XlsxPackageTestHelper.SaveWorkbook(CreatePivotWorkbook());

    private static Workbook CreatePivotWorkbook()
    {
        var workbook = new Workbook("PivotShowValuesAsBackCompatTests");
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
