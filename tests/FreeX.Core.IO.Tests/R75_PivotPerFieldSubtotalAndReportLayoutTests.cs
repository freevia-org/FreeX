using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R75-io-pivottable-layout-4-2: per-field subtotal on/off + Top/Bottom placement was collapsed into ONE
/// table-wide PivotTableModel.ShowSubtotals/SubtotalPlacement (read only off the FIRST axis field), so two
/// fields with different subtotal settings got the same value on save. Fixed by adding per-field
/// ShowSubtotals/SubtotalPlacement to PivotFieldModel, read/written per <c>&lt;pivotField&gt;</c>.
///
/// R75-io-pivottable-layout-4-3: per-field compact/outline report form was likewise collapsed table-wide.
/// Fixed by adding a per-field ReportLayout to PivotFieldModel, read/written per <c>&lt;pivotField&gt;</c>.
/// </summary>
public sealed class R75_PivotPerFieldSubtotalAndReportLayoutTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // ── R75-io-pivottable-layout-4-2 ────────────────────────────────────────────────────────────

    [Fact]
    public void SaveThenReload_TwoRowFieldsWithDifferentSubtotalSettings_EachFieldKeepsItsOwnSetting()
    {
        var workbook = CreateTwoRowFieldPivotWorkbook();
        var pivot = workbook.GetSheetAt(0).PivotTables.Single();
        pivot.ShowSubtotals = true; // table-wide default: subtotals ON.
        pivot.SubtotalPlacement = PivotSubtotalPlacement.Top;
        // Region (field 0) uses the table default (no override). Product (field 1) explicitly turns
        // subtotals OFF and would place them at the Bottom if they were ever re-enabled.
        pivot.RowFields[1] = pivot.RowFields[1] with
        {
            ShowSubtotals = false,
            SubtotalPlacement = PivotSubtotalPlacement.Bottom,
        };

        using var saved = SaveWorkbook(workbook);
        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedPivot = reloaded.GetSheetAt(0).PivotTables.Single();

        var region = reloadedPivot.RowFields.Single(f => f.SourceFieldIndex == 0);
        var product = reloadedPivot.RowFields.Single(f => f.SourceFieldIndex == 1);
        region.ShowSubtotals.Should().BeTrue("Region never overrode the table-wide subtotals-on setting");
        product.ShowSubtotals.Should().BeFalse(
            "Product explicitly turned subtotals off and must keep that setting independent of Region's");
        product.SubtotalPlacement.Should().Be(PivotSubtotalPlacement.Bottom);
    }

    [Fact]
    public void SaveThenReload_SingleRowFieldSubtotal_StillRoundTrips()
    {
        // Sibling no-regression: a single-field pivot (the ordinary, far more common case) must still
        // round-trip its subtotal on/off + placement correctly now that per-field storage exists.
        var workbook = CreateTwoRowFieldPivotWorkbook();
        workbook.GetSheetAt(0).PivotTables.Single().RowFields.RemoveAt(1);
        var pivot = workbook.GetSheetAt(0).PivotTables.Single();
        pivot.ShowSubtotals = true;
        pivot.SubtotalPlacement = PivotSubtotalPlacement.Top;

        using var saved = SaveWorkbook(workbook);
        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedPivot = reloaded.GetSheetAt(0).PivotTables.Single();

        reloadedPivot.ShowSubtotals.Should().BeTrue();
        reloadedPivot.SubtotalPlacement.Should().Be(PivotSubtotalPlacement.Top);
        reloadedPivot.RowFields.Single().ShowSubtotals.Should().BeTrue();
    }

    // ── R75-io-pivottable-layout-4-3 ────────────────────────────────────────────────────────────

    [Fact]
    public void SaveThenReload_TwoRowFieldsWithDifferentReportLayouts_EachFieldKeepsItsOwnForm()
    {
        var workbook = CreateTwoRowFieldPivotWorkbook();
        var pivot = workbook.GetSheetAt(0).PivotTables.Single();
        pivot.ReportLayout = PivotReportLayout.Tabular; // table-wide default.
        pivot.RowFields[0] = pivot.RowFields[0] with { ReportLayout = PivotReportLayout.Outline };
        pivot.RowFields[1] = pivot.RowFields[1] with { ReportLayout = PivotReportLayout.Compact };

        using var saved = SaveWorkbook(workbook);

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var pivotXml = LoadPivotXml(archive);
            var pivotFields = pivotXml.Root!.Element(WorkbookNs + "pivotFields")!.Elements(WorkbookNs + "pivotField").ToList();
            pivotFields[0].Attribute("compact")!.Value.Should().Be("0");
            pivotFields[0].Attribute("outline")!.Value.Should().Be("1");
            pivotFields[1].Attribute("compact")!.Value.Should().Be("1");
            pivotFields[1].Attribute("outline")!.Value.Should().Be("1");
        }

        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedPivot = reloaded.GetSheetAt(0).PivotTables.Single();
        reloadedPivot.RowFields.Single(f => f.SourceFieldIndex == 0).ReportLayout.Should().Be(PivotReportLayout.Outline);
        reloadedPivot.RowFields.Single(f => f.SourceFieldIndex == 1).ReportLayout.Should().Be(PivotReportLayout.Compact);
    }

    [Fact]
    public void SaveThenReload_UniformReportLayout_NoRegressionInEffectiveForm()
    {
        // Sibling no-regression: a pivot where neither field overrides the report layout must still
        // resolve to the table-wide form for BOTH fields, exactly as before per-field storage existed.
        var workbook = CreateTwoRowFieldPivotWorkbook();
        var pivot = workbook.GetSheetAt(0).PivotTables.Single();
        pivot.ReportLayout = PivotReportLayout.Compact;

        using var saved = SaveWorkbook(workbook);
        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedPivot = reloaded.GetSheetAt(0).PivotTables.Single();

        reloadedPivot.ReportLayout.Should().Be(PivotReportLayout.Compact);
        foreach (var field in reloadedPivot.RowFields)
        {
            (field.ReportLayout ?? reloadedPivot.ReportLayout).Should().Be(PivotReportLayout.Compact);
        }
    }

    private static Workbook CreateTwoRowFieldPivotWorkbook()
    {
        var workbook = new Workbook("PivotPerFieldLayoutWorkbook");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Product"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Widget"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Gadget"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(20));

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = "A1:C3",
            RecordCount = 2,
        };
        cache.Fields.Add(new PivotCacheFieldModel("Region", SharedItemCount: 2, ContainsString: true, SharedItems: ["East", "West"], SharedItemKinds: ['s', 's']));
        cache.Fields.Add(new PivotCacheFieldModel("Product", SharedItemCount: 2, ContainsString: true, SharedItems: ["Widget", "Gadget"], SharedItemKinds: ['s', 's']));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 9, 3)),
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        return workbook;
    }

    private static XDocument LoadPivotXml(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/pivotTables/pivotTable1.xml")!;
        using var entryStream = entry.Open();
        return XDocument.Load(entryStream);
    }

    private static MemoryStream SaveWorkbook(Workbook workbook)
    {
        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return stream;
    }
}
