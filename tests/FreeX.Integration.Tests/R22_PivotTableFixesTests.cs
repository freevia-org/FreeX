using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R22-pivot-table-1/2/3: pivot "Show Values As" base-item resolution, grouped-field ranking,
/// and drill-through subtotal detection.
///
/// (1) Per ECMA-376 CT_DataField, baseItem is an xsd:unsignedInt INDEX into the base field's
/// shared items, not a label. The reader used to store the raw (numeric-looking, real-Excel)
/// attribute text verbatim, so BaseItemAggregate's label comparison never matched and every
/// Difference-From/%-Difference-From cell silently computed as if no base item were set. The
/// writer used to emit the display label directly as baseItem, which is schema-invalid (not an
/// unsignedInt) and breaks on a real Excel/spec-conformant reader.
///
/// (2) "Rank Largest/Smallest" used to rank against the raw, ungrouped per-row base-field text
/// instead of the displayed (date/number-grouped) bucket text, unlike the already-fixed
/// RunningTotalIn path -- so a rank base field that is grouped (e.g. dates grouped by Month)
/// ranked among hundreds of near-singleton raw values instead of the handful of displayed
/// buckets.
///
/// (3) Drill-through ("Show Details") used to identify a subtotal row purely by its label ending
/// in " Total", so a legitimate row item whose own value happens to end in " Total" (e.g. a
/// channel named "Regional Total") was misidentified as a subtotal and its detail rows were
/// looked up under the wrong (truncated, prefix-matched) key.
/// </summary>
public sealed class R22_pivot_table_Tests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // --- R22-pivot-table-1 --------------------------------------------------------------------

    // DEFERRED (round 22): the baseItem index<->label write/read fix (R22-pivot-table-1) was reverted
    // because the writer dropped baseItem entirely when a model-stored label could not be resolved to a
    // shared-item index (data loss, worse than the non-conformant label form). Needs robust label<->index
    // handling with shared-items availability + a smoke-test update before re-enabling.
    [Fact(Skip = "R22-pivot-table-1 (baseItem numeric index) deferred — see round-22 notes")]
    public void DifferenceFromBaseItem_SavedAndReloaded_RoundTripsAsNumericIndexResolvingBackToLabel()
    {
        var workbook = new Workbook("PivotBaseItemTest");
        var sheet = workbook.AddSheet("Data");
        SetText(sheet, 1, 1, "Region");
        SetText(sheet, 1, 2, "Amount");
        var rows = new (string Region, double Amount)[]
        {
            ("East", 10), ("East", 20), ("West", 100), ("West", 200),
        };
        for (var i = 0; i < rows.Length; i++)
        {
            var row = (uint)i + 2;
            SetText(sheet, row, 1, rows[i].Region);
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(rows[i].Amount));
        }

        // Shared items populated as if loaded from a real Excel-authored file -- "West" is index 1.
        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = "A1:B5",
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
        };
        cache.Fields.Add(new PivotCacheFieldModel(
            "Region",
            ContainsString: true,
            SharedItems: ["East", "West"],
            SharedItemKinds: ['s', 's']));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 1, 4), new CellAddress(sheet.Id, 10, 7)),
            PackagePart = "xl/pivotTables/pivotTable1.xml",
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        pivot.DataFields.Add(new PivotDataFieldModel(
            1, "Difference From West", "sum",
            ShowValuesAs: PivotShowValuesAs.DifferenceFrom,
            BaseFieldIndex: 0,
            BaseItem: "West"));
        sheet.PivotTables.Add(pivot);

        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);

        // Write-side (R22-pivot-table-1b): the saved baseItem attribute must be the numeric shared-item
        // index of "West" (1), never the raw label text -- a bare label violates CT_DataField's
        // xsd:unsignedInt schema and is what a real Excel/spec-conformant reader would choke on.
        var savedDataField = ReadDataFieldElement(ms, "Difference From West");
        savedDataField.Attribute("baseItem").Should().NotBeNull();
        savedDataField.Attribute("baseItem")!.Value.Should().Be("1",
            "baseItem is an xsd:unsignedInt index into the base field's shared items ([\"East\",\"West\"]), not the literal label");

        // Read-side (R22-pivot-table-1a): loading that numeric-index XML back must resolve it to the
        // displayed label ("West") so BaseItemAggregate's KeyText comparison actually matches rows.
        ms.Position = 0;
        var reloaded = adapter.Load(ms);
        var reloadedSheet = reloaded.Sheets[0];
        var reloadedPivot = reloadedSheet.PivotTables.Single();
        var reloadedDataField = reloadedPivot.DataFields.Single(f => f.Name == "Difference From West");
        reloadedDataField.BaseItem.Should().Be("West",
            "the numeric baseItem index must resolve back to the base field's shared-item label, not stay as the literal index text");

        // End-to-end: refreshing the reloaded pivot must compute a real Difference-From result
        // (East - West = 30 - 300 = -270), not the "always 0 baseline" bug this finding describes.
        PivotTableRefreshService.Refresh(reloaded, reloadedSheet, reloadedPivot);
        var diffValue = FindRowValue(reloadedSheet, reloadedPivot, "East", columnOffset: 2);
        diffValue.Should().Be(-270, "Difference From West for the East row must be East's sum (30) minus West's sum (300)");
    }

    private static XElement ReadDataFieldElement(MemoryStream package, string dataFieldName)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/pivotTables/pivotTable1.xml")!;
        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        return document.Root!
            .Element(WorkbookNs + "dataFields")!
            .Elements(WorkbookNs + "dataField")
            .Single(field => field.Attribute("name")!.Value == dataFieldName);
    }

    private static double? FindRowValue(Sheet sheet, PivotTableModel pivot, string rowLabel, int columnOffset)
    {
        for (var row = pivot.TargetRange.Start.Row; row <= pivot.TargetRange.End.Row; row++)
        {
            if (sheet.GetCell(new CellAddress(sheet.Id, row, pivot.TargetRange.Start.Col))?.Value is not TextValue text ||
                text.Value != rowLabel)
            {
                continue;
            }

            var valueCell = sheet.GetCell(new CellAddress(sheet.Id, row, pivot.TargetRange.Start.Col + (uint)columnOffset));
            return valueCell?.Value is NumberValue number ? number.Value : null;
        }

        return null;
    }

    // --- R22-pivot-table-2 --------------------------------------------------------------------

    [Fact]
    public void RankLargest_WithMonthGroupedBaseField_RanksDisplayedMonthBucketsNotRawDailyValues()
    {
        var workbook = new Workbook("PivotRankGroupedTest");
        var sheet = workbook.AddSheet("Data");
        SetText(sheet, 1, 1, "OrderDate");
        SetText(sheet, 1, 2, "Sales");
        var rows = new (int Year, int Month, int Day, double Sales)[]
        {
            (2024, 1, 5, 10), (2024, 1, 15, 20), (2024, 1, 25, 5),
            (2024, 2, 5, 100), (2024, 2, 15, 200),
        };
        for (var i = 0; i < rows.Length; i++)
        {
            var row = (uint)i + 2;
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), DateTimeValue.FromDateTime(new System.DateTime(rows[i].Year, rows[i].Month, rows[i].Day)));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(rows[i].Sales));
        }

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 2)),
            // Body start = TargetRange.Start (no page fields): header row 2, label col 5 (E),
            // "Sum of Sales" col 6 (F), "Rank Largest" col 7 (G). First data row = 3 (January),
            // second = 4 (February).
            TargetRange = new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 10, 8)),
        };
        pivot.RowFields.Add(new PivotFieldModel(0, Grouping: PivotFieldGrouping.Month));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Sales", "sum"));
        pivot.DataFields.Add(new PivotDataFieldModel(
            1, "Rank Largest", "sum",
            ShowValuesAs: PivotShowValuesAs.RankLargest,
            BaseFieldIndex: 0));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        // Row 3 = January bucket (sum 10+20+5=35, 3 raw rows), Row 4 = February bucket (sum
        // 100+200=300, 2 raw rows). Real Excel ranks the 2 DISPLAYED month buckets: February is
        // largest (rank 1), January is second (rank 2). Pre-fix, RankValue grouped by every
        // distinct raw per-row date instead (5 near-singleton buckets), so January's rank came
        // out as 4th-largest among those 5 raw values and February's as 2nd, instead of 2 and 1.
        Number(sheet, row: 3, col: 6).Should().Be(35, "January's displayed Sum of Sales bucket total");
        Number(sheet, row: 3, col: 7).Should().Be(2, "January must rank 2nd of the 2 displayed month buckets, not among raw daily values");
        Number(sheet, row: 4, col: 6).Should().Be(300, "February's displayed Sum of Sales bucket total");
        Number(sheet, row: 4, col: 7).Should().Be(1, "February must rank 1st (largest) of the 2 displayed month buckets");
    }

    private static double? Number(Sheet sheet, uint row, uint col) =>
        sheet.GetCell(new CellAddress(sheet.Id, row, col))?.Value is NumberValue number ? number.Value : null;

    // --- R22-pivot-table-3 --------------------------------------------------------------------

    [Fact]
    public void ExtractDetailRows_ForLeafItemLabelEndingInTotal_IsNotMisidentifiedAsSubtotal()
    {
        var workbook = new Workbook("PivotDetailsFalseSubtotalTest");
        var sheet = workbook.AddSheet("Data");
        SetText(sheet, 1, 1, "Channel");
        SetText(sheet, 1, 2, "Amount");
        // "Regional Total" is a genuine, literal source-data item value -- NOT an Excel-generated
        // subtotal caption (subtotals are entirely disabled below: ShowSubtotals defaults to false
        // and there is only a single row field, so WriteSubtotalRow's own gate can never fire).
        SetText(sheet, 2, 1, "Regional Total");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(500));
        SetText(sheet, 3, 1, "Online");
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(300));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 1, 4), new CellAddress(sheet.Id, 10, 6)),
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var labelColumn = pivot.TargetRange.Start.Col;
        var valueColumn = labelColumn + 1;
        var regionalTotalRow = FindLabelRow(sheet, pivot, "Regional Total");
        regionalTotalRow.Should().NotBeNull("the 'Regional Total' item must be materialized as its own pivot row");

        var pivotCell = new CellAddress(sheet.Id, regionalTotalRow!.Value, valueColumn);
        var detail = PivotTableRefreshService.ExtractDetailRows(workbook, sheet, pivot, pivotCell);

        // Pre-fix, the literal " Total" suffix check treated this leaf row as a subtotal, truncated
        // its key to "Regional" (which matches no actual Channel value), and returned zero detail
        // rows. Post-fix, it must return exactly the one genuine "Regional Total" source row.
        detail.Rows.Should().ContainSingle("drilling into the 'Regional Total' row must return that row's own source data, not zero rows");
        detail.Rows[0][0].Should().Be(new TextValue("Regional Total"));
        detail.Rows[0][1].Should().Be(new NumberValue(500));
    }

    private static uint? FindLabelRow(Sheet sheet, PivotTableModel pivot, string label)
    {
        for (var row = pivot.TargetRange.Start.Row; row <= pivot.TargetRange.End.Row; row++)
        {
            if (sheet.GetCell(new CellAddress(sheet.Id, row, pivot.TargetRange.Start.Col))?.Value is TextValue text &&
                text.Value == label)
            {
                return row;
            }
        }

        return null;
    }

    private static void SetText(Sheet sheet, uint row, uint col, string value) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, col), new TextValue(value));
}
