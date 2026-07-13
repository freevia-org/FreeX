using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R40-io-pivot-layout-3-1: Classic PivotTable Layout (PivotTableModel.ShowClassicLayout) must round-trip
/// through the real OOXML gridDropZones attribute on CT_pivotTableDefinition (Excel's "enables dragging of
/// fields in the grid" checkbox), NOT the unrelated showDropZones attribute. Before the fix, the writer
/// wrote ShowClassicLayout into showDropZones and hardcoded gridDropZones purely from ReportLayout
/// (Compact/Outline => "0", Tabular => "1"), so a Compact/Outline pivot with Classic Layout enabled lost
/// that setting on save, and reading a genuine Excel file with gridDropZones="1" never set
/// ShowClassicLayout at all.
///
/// R40-io-pivot-layout-3-2: fieldListSortAscending (the PivotTable Field List panel's sort order) was not
/// modeled anywhere in PivotTableModel and was silently dropped on every save.
/// </summary>
public sealed class R40_PivotLayoutGridDropZonesAndFieldListSortTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void Save_ClassicLayoutWithCompactReportLayout_WritesGridDropZonesOne_NotHardcodedZero()
    {
        // Before the fix, PivotReportLayoutAttributes hardcoded gridDropZones="0" for Compact/Outline
        // report layouts regardless of ShowClassicLayout, silently discarding the user's Classic Layout
        // choice whenever the report layout was anything but Tabular.
        var workbook = CreatePivotWorkbook(reportLayout: PivotReportLayout.Compact, showClassicLayout: true);

        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var pivotXml = XlsxPackageTestHelper.ReadPackageXml(package, "xl/pivotTables/pivotTable1.xml");

        pivotXml.Root!.Attribute("gridDropZones")!.Value.Should().Be("1");
        // The report-layout form attributes are unaffected by Classic Layout.
        pivotXml.Root!.Attribute("compact")!.Value.Should().Be("1");
        pivotXml.Root!.Attribute("outline")!.Value.Should().Be("1");
    }

    [Fact]
    public void Save_ClassicLayoutDisabledWithTabularReportLayout_WritesGridDropZonesZero_NotHardcodedOne()
    {
        // Before the fix, Tabular report layout unconditionally forced gridDropZones="1" even when the
        // user never enabled Classic Layout, causing real Excel to render classic in-grid drag-and-drop
        // chrome the user never asked for.
        var workbook = CreatePivotWorkbook(reportLayout: PivotReportLayout.Tabular, showClassicLayout: false);

        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var pivotXml = XlsxPackageTestHelper.ReadPackageXml(package, "xl/pivotTables/pivotTable1.xml");

        pivotXml.Root!.Attribute("gridDropZones")!.Value.Should().Be("0");
        pivotXml.Root!.Attribute("compact")!.Value.Should().Be("0");
        pivotXml.Root!.Attribute("outline")!.Value.Should().Be("0");
    }

    [Fact]
    public void SaveLoad_RoundTripsShowClassicLayout_ViaGridDropZones()
    {
        var workbook = CreatePivotWorkbook(reportLayout: PivotReportLayout.Outline, showClassicLayout: true);

        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var loaded = new XlsxFileAdapter().Load(package);

        loaded.GetSheetAt(0).PivotTables.Single().ShowClassicLayout.Should().BeTrue();
    }

    [Fact]
    public void Load_NativeFileWithGridDropZonesSetAndShowDropZonesUnset_SetsShowClassicLayout()
    {
        // A genuine Excel-authored file: gridDropZones="1" (Classic Layout truly on), no showDropZones
        // attribute at all. Before the fix, the reader only ever inspected showDropZones, so
        // ShowClassicLayout was never set from a real Excel file's gridDropZones value.
        using var package = XlsxPackageTestHelper.SaveWorkbook(
            CreatePivotWorkbook(reportLayout: PivotReportLayout.Tabular, showClassicLayout: false));
        XlsxPackageTestHelper.PatchPackageXml(package, "xl/pivotTables/pivotTable1.xml", document =>
        {
            document.Root!.SetAttributeValue("gridDropZones", "1");
            document.Root!.Attribute("showDropZones")?.Remove();
        });

        var loaded = new XlsxFileAdapter().Load(package);

        loaded.GetSheetAt(0).PivotTables.Single().ShowClassicLayout.Should().BeTrue();
    }

    [Fact]
    public void Load_NativeFileWithShowDropZonesFalseAndGridDropZonesUnset_DoesNotSetShowClassicLayout()
    {
        // No-regression sibling: an unrelated showDropZones="0" attribute (its own, unmodeled flag) must
        // NOT be conflated with ShowClassicLayout when gridDropZones is absent (defaults to false).
        using var package = XlsxPackageTestHelper.SaveWorkbook(
            CreatePivotWorkbook(reportLayout: PivotReportLayout.Tabular, showClassicLayout: false));
        XlsxPackageTestHelper.PatchPackageXml(package, "xl/pivotTables/pivotTable1.xml", document =>
        {
            document.Root!.SetAttributeValue("showDropZones", "0");
            document.Root!.Attribute("gridDropZones")?.Remove();
        });

        var loaded = new XlsxFileAdapter().Load(package);

        loaded.GetSheetAt(0).PivotTables.Single().ShowClassicLayout.Should().BeFalse();
    }

    [Fact]
    public void Save_FieldListSortAscendingTrue_WritesAttribute_NotDropped()
    {
        var workbook = CreatePivotWorkbook(
            reportLayout: PivotReportLayout.Tabular,
            showClassicLayout: false,
            fieldListSortAscending: true);

        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var pivotXml = XlsxPackageTestHelper.ReadPackageXml(package, "xl/pivotTables/pivotTable1.xml");

        pivotXml.Root!.Attribute("fieldListSortAscending")!.Value.Should().Be("1");
    }

    [Fact]
    public void Save_FieldListSortAscendingFalse_WritesAttributeAsZero()
    {
        // No-regression sibling: the default (false / data-source order) still round-trips explicitly
        // rather than being silently omitted or left ambiguous.
        var workbook = CreatePivotWorkbook(
            reportLayout: PivotReportLayout.Tabular,
            showClassicLayout: false,
            fieldListSortAscending: false);

        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var pivotXml = XlsxPackageTestHelper.ReadPackageXml(package, "xl/pivotTables/pivotTable1.xml");

        pivotXml.Root!.Attribute("fieldListSortAscending")!.Value.Should().Be("0");
    }

    [Fact]
    public void SaveLoad_RoundTripsFieldListSortAscending()
    {
        var workbook = CreatePivotWorkbook(
            reportLayout: PivotReportLayout.Tabular,
            showClassicLayout: false,
            fieldListSortAscending: true);

        using var package = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var loaded = new XlsxFileAdapter().Load(package);

        loaded.GetSheetAt(0).PivotTables.Single().FieldListSortAscending.Should().BeTrue();
    }

    [Fact]
    public void Load_NativeFileWithFieldListSortAscendingAttribute_IsPreservedOnResave()
    {
        // A real Excel-authored workbook has fieldListSortAscending="1"; even a re-save with no other
        // change must not revert it to Excel's default.
        using var package = XlsxPackageTestHelper.SaveWorkbook(
            CreatePivotWorkbook(reportLayout: PivotReportLayout.Tabular, showClassicLayout: false));
        XlsxPackageTestHelper.PatchPackageXml(package, "xl/pivotTables/pivotTable1.xml", document =>
        {
            document.Root!.SetAttributeValue("fieldListSortAscending", "1");
        });

        var loaded = new XlsxFileAdapter().Load(package);
        loaded.GetSheetAt(0).PivotTables.Single().FieldListSortAscending.Should().BeTrue();

        using var resaved = XlsxPackageTestHelper.SaveWorkbook(loaded);
        var resavedPivotXml = XlsxPackageTestHelper.ReadPackageXml(resaved, "xl/pivotTables/pivotTable1.xml");
        resavedPivotXml.Root!.Attribute("fieldListSortAscending")!.Value.Should().Be("1");
    }

    private static Workbook CreatePivotWorkbook(
        PivotReportLayout reportLayout,
        bool showClassicLayout,
        bool fieldListSortAscending = false)
    {
        var workbook = new Workbook("R40PivotLayoutTests");
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
            ReportLayout = reportLayout,
            ShowClassicLayout = showClassicLayout,
            FieldListSortAscending = fieldListSortAscending,
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        return workbook;
    }
}
