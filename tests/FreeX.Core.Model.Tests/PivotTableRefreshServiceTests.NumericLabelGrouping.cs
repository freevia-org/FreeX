using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

// Regression coverage for group E-pivots findings H9 (numeric row/column labels must sort
// numerically, not lexicographically) and H44 (blank/non-numeric source rows in a
// NumberRange-grouped field must form a distinct "(blank)" group instead of the 0 bucket).
public sealed partial class PivotTableRefreshServiceTests
{
    [Fact]
    public void Refresh_UngroupedNumericRowField_SortsNumericallyNotLexicographically()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedNumericQuantitySalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B7"),
            TargetRange = Range(sheet, "D2", "F12")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        // Excel orders numeric row items ascending by value: 1,2,3,10,11,20 - not the
        // lexicographic "1,10,11,2,20,3" a plain string comparer would produce.
        Text(sheet, "D3").Should().Be("1");
        Text(sheet, "D4").Should().Be("2");
        Text(sheet, "D5").Should().Be("3");
        Text(sheet, "D6").Should().Be("10");
        Text(sheet, "D7").Should().Be("11");
        Text(sheet, "D8").Should().Be("20");
        Text(sheet, "D9").Should().Be("Grand Total");
    }

    [Fact]
    public void Refresh_NumberRangeGroupedField_BlankAndTextRowsFormSeparateBlankGroup()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedPriceSalesDataWithBlankAndTextRows(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B5"),
            TargetRange = Range(sheet, "D2", "F10")
        };
        pivot.RowFields.Add(new PivotFieldModel(0, Grouping: PivotFieldGrouping.NumberRange, GroupStart: 0, GroupInterval: 10));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        // The blank cell (row 4) and the text cell ("N/A", row 5) must NOT be folded into
        // the "0-9" bucket alongside the genuinely-numeric price of 2 (row 2); they form
        // their own "(blank)" group instead. Numeric-range buckets now sort as numbers
        // (R17-pivot-cache-deep-2), so they come first in ascending order and the genuinely
        // non-numeric "(blank)" group sorts AFTER them (numbers-before-text, matching
        // PivotKeyComparer's convention), consistent with Excel placing "(blank)" last.
        Text(sheet, "D3").Should().Be("0-9");
        Number(sheet, "E3").Should().Be(10);
        Text(sheet, "D4").Should().Be("10-19");
        Number(sheet, "E4").Should().Be(70);
        Text(sheet, "D5").Should().Be("(blank)");
        Number(sheet, "E5").Should().Be(100);
        Text(sheet, "D6").Should().Be("Grand Total");
        Number(sheet, "E6").Should().Be(180);
    }

    [Fact]
    public void Refresh_NumberRangeGroupedField_ValuesBelowStartFormUnderflowGroup()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedPriceSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B5"),
            TargetRange = Range(sheet, "D2", "F10")
        };
        // "Starting at" 5 with no "Ending at": price 2 is below the start, so it must land in
        // its own "<5" bucket - not extrapolate the 10-wide interval grid backwards into a
        // "-5-4" bucket that doesn't reflect the "Starting at" boundary Excel shows.
        pivot.RowFields.Add(new PivotFieldModel(0, Grouping: PivotFieldGrouping.NumberRange, GroupStart: 5, GroupInterval: 10));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "D3").Should().Be("<5");
        Number(sheet, "E3").Should().Be(10);
        Text(sheet, "D4").Should().Be("5-14");
        // Prices 7 and 12 both fall in 5-14: amounts 20+30
        Number(sheet, "E4").Should().Be(50);
        Text(sheet, "D5").Should().Be("15-24");
        Number(sheet, "E5").Should().Be(40);
        Text(sheet, "D6").Should().Be("Grand Total");
        Number(sheet, "E6").Should().Be(100);
    }

    // Sibling of Refresh_NumberRangeGroupedField_ValuesBelowStartFormUnderflowGroup: a value
    // exactly AT "Starting at" must land in the normal first bucket, not the "<start" bucket -
    // guards the strict "<" in the fix against drifting to "<=" and swallowing the boundary.
    [Fact]
    public void Refresh_NumberRangeGroupedField_ValueAtStartIsNotUnderflow()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Price"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new NumberValue(3));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new NumberValue(5));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A4"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B4"),
            TargetRange = Range(sheet, "D2", "F10")
        };
        pivot.RowFields.Add(new PivotFieldModel(0, Grouping: PivotFieldGrouping.NumberRange, GroupStart: 5, GroupInterval: 10));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "D3").Should().Be("<5");
        Number(sheet, "E3").Should().Be(10);
        Text(sheet, "D4").Should().Be("5-14");
        // Price 5 (== start) and price 10 both belong in 5-14: amounts 20+30
        Number(sheet, "E4").Should().Be(50);
        Text(sheet, "D5").Should().Be("Grand Total");
        Number(sheet, "E5").Should().Be(60);
    }

    private static void SeedNumericQuantitySalesData(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Quantity"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new NumberValue(1));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new NumberValue(2));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A4"), new NumberValue(3));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));
        sheet.SetCell(Addr(sheet, "A5"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "B5"), new NumberValue(40));
        sheet.SetCell(Addr(sheet, "A6"), new NumberValue(11));
        sheet.SetCell(Addr(sheet, "B6"), new NumberValue(50));
        sheet.SetCell(Addr(sheet, "A7"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "B7"), new NumberValue(60));
    }

    private static void SeedPriceSalesDataWithBlankAndTextRows(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Price"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new NumberValue(2));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new NumberValue(12));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(70));
        // Row 4: blank Price cell (no A cell set at all). Row 5: non-numeric text Price cell.
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(60));
        sheet.SetCell(Addr(sheet, "A5"), new TextValue("N/A"));
        sheet.SetCell(Addr(sheet, "B5"), new NumberValue(40));
    }
}
