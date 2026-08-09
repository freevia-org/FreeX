using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R131-commands-pivot-shownodata-grouped: "Show items with no data" injected the pivot cache field's
/// raw, UNGROUPED shared items (e.g. a raw ISO date string like "2026-01-05T00:00:00") straight into
/// the candidate label set alongside the real, correctly-grouped labels (e.g. "2026-01" for a
/// month-grouped date field). Because the raw string never matches the grouped label textually, the
/// no-data injection created a brand-new PHANTOM group for every raw cache value instead of
/// recognizing it belongs to an existing (or otherwise legitimate) group -- so the rendered pivot
/// showed raw dates/numbers as extra row/column labels next to the real month/number-range groups.
///
/// The fix projects each raw shared item through the SAME <c>GroupKeyText</c> grouping transform the
/// real row/column labels go through before it becomes a no-data candidate, so a grouped field's
/// no-data injection contributes group labels, never raw cache values.
/// </summary>
public sealed class R131_PivotShowNoDataGroupedFieldTests
{
    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

    private static string Text(Sheet sheet, string a1) =>
        sheet.GetCell(Addr(sheet, a1))?.Value is TextValue text ? text.Value : "";

    private static double Number(Sheet sheet, string a1) =>
        sheet.GetCell(Addr(sheet, a1))?.Value is NumberValue number ? number.Value : double.NaN;

    /// <summary>Collects every non-empty text value written down a column, from <paramref name="fromRow"/>
    /// through <paramref name="toRow"/> inclusive -- used to see the FULL set of labels a buggy phantom
    /// injection would spill across, regardless of exactly which row index it lands on.</summary>
    private static List<string> CollectColumnTexts(Sheet sheet, char column, uint fromRow, uint toRow)
    {
        var values = new List<string>();
        for (var row = fromRow; row <= toRow; row++)
        {
            var text = Text(sheet, $"{column}{row}");
            if (!string.IsNullOrEmpty(text))
                values.Add(text);
        }

        return values;
    }

    private static void SeedDatedSalesData(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Order Date"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), DateTimeValue.FromDateTime(new DateTime(2026, 1, 5)));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), DateTimeValue.FromDateTime(new DateTime(2026, 1, 20)));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A4"), DateTimeValue.FromDateTime(new DateTime(2026, 2, 2)));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));
        sheet.SetCell(Addr(sheet, "A5"), DateTimeValue.FromDateTime(new DateTime(2026, 2, 28)));
        sheet.SetCell(Addr(sheet, "B5"), new NumberValue(40));
    }

    private static void SeedPriceSalesData(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Price"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new NumberValue(2));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new NumberValue(7));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A4"), new NumberValue(12));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));
        sheet.SetCell(Addr(sheet, "A5"), new NumberValue(17));
        sheet.SetCell(Addr(sheet, "B5"), new NumberValue(40));
    }

    /// <summary>
    /// THE anchor test (row-key path, date grouping): the cache field's raw ISO shared items
    /// ("2026-01-05T00:00:00" etc.) must NOT show up verbatim as row labels once the field is grouped
    /// by Month -- only the grouped labels ("2026-01", "2026-02", "2026-03") may appear. The extra
    /// "2026-03-10T00:00:00" shared item (present in the cache but with no matching row) proves the
    /// no-data injection still legitimately surfaces a group with zero rows -- just correctly grouped,
    /// not as its own raw phantom label.
    /// </summary>
    [Fact]
    public void Refresh_RowFieldGroupedByMonth_ShowItemsWithNoData_InjectsGroupedLabelsNotRawCacheValues()
    {
        var workbook = new Workbook("R131RowGroupedNoData");
        var sheet = workbook.AddSheet("Data");
        SeedDatedSalesData(sheet);
        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            Fields =
            {
                new PivotCacheFieldModel(
                    "Order Date",
                    ContainsDate: true,
                    SharedItems:
                    [
                        "2026-01-05T00:00:00",
                        "2026-01-20T00:00:00",
                        "2026-02-02T00:00:00",
                        "2026-02-28T00:00:00",
                        // Present in the cache but not in any live row -- a genuine no-data group.
                        "2026-03-10T00:00:00"
                    ],
                    SharedItemKinds: ['d', 'd', 'd', 'd', 'd']),
                new PivotCacheFieldModel("Amount", ContainsNumber: true)
            }
        });
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B5"),
            TargetRange = Range(sheet, "D2", "F20"),
            EmptyValueText = "N/A",
            ShowItemsWithNoDataOnRows = true,
            ReportLayout = PivotReportLayout.Tabular
        };
        pivot.RowFields.Add(new PivotFieldModel(0, Grouping: PivotFieldGrouping.Month));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var labels = CollectColumnTexts(sheet, 'D', 3, 20);
        // Exactly the 3 grouped labels plus the grand total -- no raw ISO date string anywhere.
        labels.Should().BeEquivalentTo(["2026-01", "2026-02", "2026-03", "Grand Total"]);
        labels.Should().NotContain(label => label.Contains("T00:00:00"));

        Text(sheet, "D3").Should().Be("2026-01");
        Number(sheet, "E3").Should().Be(30);
        Text(sheet, "D4").Should().Be("2026-02");
        Number(sheet, "E4").Should().Be(70);
        Text(sheet, "D5").Should().Be("2026-03");
        Text(sheet, "E5").Should().Be("N/A");
        Text(sheet, "D6").Should().Be("Grand Total");
        Number(sheet, "E6").Should().Be(100);
    }

    /// <summary>
    /// FAMILY (column-key path): the same phantom-raw-value defect applies to a grouped COLUMN field's
    /// no-data injection (<c>BuildColumnKeys</c>), not just the row path.
    /// </summary>
    [Fact]
    public void Refresh_ColumnFieldGroupedByMonth_ShowItemsWithNoData_InjectsGroupedLabelsNotRawCacheValues()
    {
        var workbook = new Workbook("R131ColumnGroupedNoData");
        var sheet = workbook.AddSheet("Data");
        SeedDatedSalesData(sheet);
        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            Fields =
            {
                new PivotCacheFieldModel(
                    "Order Date",
                    ContainsDate: true,
                    SharedItems:
                    [
                        "2026-01-05T00:00:00",
                        "2026-01-20T00:00:00",
                        "2026-02-02T00:00:00",
                        "2026-02-28T00:00:00",
                        "2026-03-10T00:00:00"
                    ],
                    SharedItemKinds: ['d', 'd', 'd', 'd', 'd']),
                new PivotCacheFieldModel("Amount", ContainsNumber: true)
            }
        });
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B5"),
            TargetRange = Range(sheet, "D2", "M6"),
            EmptyValueText = "N/A",
            ShowItemsWithNoDataOnColumns = true
        };
        pivot.ColumnFields.Add(new PivotFieldModel(0, Grouping: PivotFieldGrouping.Month));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var labels = "DEFGHIJKLM"
            .Select(col => Text(sheet, $"{col}2"))
            .Where(text => !string.IsNullOrEmpty(text))
            .ToList();
        labels.Should().BeEquivalentTo(["2026-01", "2026-02", "2026-03", "Grand Total"]);
        labels.Should().NotContain(label => label.Contains("T00:00:00"));
    }

    /// <summary>
    /// FAMILY (numeric grouping): same defect for a NumberRange-grouped field -- the raw unbucketed
    /// numeric cache values ("2", "7", "12", "17", "42") must not appear as their own labels once the
    /// field is grouped into 10-wide buckets; only the bucket labels may appear.
    /// </summary>
    [Fact]
    public void Refresh_RowFieldGroupedByNumberRange_ShowItemsWithNoData_InjectsBucketLabelsNotRawCacheValues()
    {
        var workbook = new Workbook("R131NumberRangeGroupedNoData");
        var sheet = workbook.AddSheet("Data");
        SeedPriceSalesData(sheet);
        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            Fields =
            {
                new PivotCacheFieldModel(
                    "Price",
                    ContainsNumber: true,
                    // "42" has no matching row -- a genuine no-data bucket (40-49).
                    SharedItems: ["2", "7", "12", "17", "42"],
                    SharedItemKinds: ['n', 'n', 'n', 'n', 'n']),
                new PivotCacheFieldModel("Amount", ContainsNumber: true)
            }
        });
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B5"),
            TargetRange = Range(sheet, "D2", "F20"),
            EmptyValueText = "N/A",
            ShowItemsWithNoDataOnRows = true
        };
        pivot.RowFields.Add(new PivotFieldModel(
            0, Grouping: PivotFieldGrouping.NumberRange, GroupStart: 0, GroupInterval: 10));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        var labels = CollectColumnTexts(sheet, 'D', 3, 20);
        labels.Should().BeEquivalentTo(["0-9", "10-19", "40-49", "Grand Total"]);
        // None of the raw unbucketed cache values ("2", "7", "12", "17", "42") leaked through verbatim.
        labels.Should().NotContain(new[] { "2", "7", "12", "17", "42" });
    }

    /// <summary>
    /// SIBLING no-regression: an UNGROUPED field's "show items with no data" must still work exactly as
    /// before -- the cache's raw shared items ARE the real labels for an ungrouped field (no transform
    /// applies), so they must still be injected verbatim, proving the fix didn't over-correct by
    /// suppressing legitimate no-data labels for the common (ungrouped) case.
    /// </summary>
    [Fact]
    public void Refresh_RowFieldUngrouped_ShowItemsWithNoData_StillInjectsCacheSharedItemsVerbatim()
    {
        var workbook = new Workbook("R131UngroupedNoDataRegression");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(25));
        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            Fields =
            {
                new PivotCacheFieldModel("Region", ContainsString: true, SharedItems: ["East", "North", "West"], SharedItemKinds: ['s', 's', 's']),
                new PivotCacheFieldModel("Amount", ContainsNumber: true)
            }
        });
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D2", "F7"),
            EmptyValueText = "N/A",
            ShowItemsWithNoDataOnRows = true,
            ReportLayout = PivotReportLayout.Tabular
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "D3").Should().Be("East");
        Number(sheet, "E3").Should().Be(10);
        Text(sheet, "D4").Should().Be("North");
        Text(sheet, "E4").Should().Be("N/A");
        Text(sheet, "D5").Should().Be("West");
        Number(sheet, "E5").Should().Be(25);
        Text(sheet, "D6").Should().Be("Grand Total");
        Number(sheet, "E6").Should().Be(35);
    }
}
