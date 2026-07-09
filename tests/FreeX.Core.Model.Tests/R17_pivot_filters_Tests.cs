using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

// R17 round-17 fixes (PivotTableRefreshService.Filters.cs):
//   R17-pivot-cache-deep-2: NumberRangeKeyText formats grouped-number buckets as
//     "{start}-{end}" (an embedded hyphen), so PivotKeyComparer.CompareKeyText failed
//     double.TryParse on the whole label and fell back to lexicographic string ordering,
//     e.g. "100-109" sorted before "20-29". Row/column display order (and RunningTotalIn
//     base-field ordering, which reuses the same comparer) must instead be ascending by the
//     bucket's numeric start.
//   R17-pivot-cache-deep-3: NumberRangeKeyText computed bucketEnd = bucketStart + interval -
//     1 unconditionally, which is only correct for an integer interval. For a fractional
//     interval (e.g. 0.5) this understates the range or even puts the end before the start
//     (e.g. "0--0.5" instead of "0-0.5"). Excel labels a numeric group with a fractional
//     interval using the half-open upper bound start..(start+interval).
public sealed partial class PivotTableRefreshServiceTests
{
    private static void SeedNumberRangeOrderingData(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Value"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));

        // Deliberately out of order so grouping/sorting is actually exercised rather than
        // happening to already be in ascending order.
        double[] values = [115, 25, 95, 15, 105, 35, 85, 45, 75, 55, 65, 5];
        for (var index = 0; index < values.Length; index++)
        {
            var row = (uint)index + 2;
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(values[index]));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(1));
        }
    }

    private static void SeedFractionalIntervalOrderingData(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Value"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new NumberValue(0.7));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(1));
        sheet.SetCell(Addr(sheet, "A3"), new NumberValue(0.2));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(1));
    }

    [Fact]
    public void Refresh_GroupsNumberRowFieldByTen_OrdersBucketsAscendingNumericNotLexicographic()
    {
        // R17-pivot-cache-deep-2: with values spanning 0..120 grouped by 10, the display
        // order of the resulting buckets must be 0-9,10-19,...,90-99,100-109,110-119 -
        // ascending by numeric start, not "0-9","10-19","100-109","110-119","20-29",... which
        // is what plain lexicographic string sorting of the labels would produce.
        var workbook = new Workbook("PivotNumberRangeOrderTest");
        var sheet = workbook.AddSheet("Data");
        SeedNumberRangeOrderingData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B13"),
            TargetRange = Range(sheet, "D2", "F15")
        };
        pivot.RowFields.Add(new PivotFieldModel(0, Grouping: PivotFieldGrouping.NumberRange, GroupStart: 0, GroupInterval: 10));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        string[] expectedOrder =
        [
            "0-9", "10-19", "20-29", "30-39", "40-49", "50-59",
            "60-69", "70-79", "80-89", "90-99", "100-109", "110-119"
        ];
        for (var index = 0; index < expectedOrder.Length; index++)
        {
            var row = (uint)(3 + index);
            Text(sheet, $"D{row}").Should().Be(expectedOrder[index], $"row {row} should hold bucket #{index}");
        }

        Text(sheet, "D15").Should().Be("Grand Total");
    }

    [Fact]
    public void Refresh_GroupsNumberRowFieldByHalf_LabelsUpperBoundGreaterThanOrEqualToLowerBound()
    {
        // R17-pivot-cache-deep-3: grouping by a fractional interval (0.5) over 0..1 must
        // label buckets "0-0.5" and "0.5-1" (the half-open upper bound), not the inclusive
        // "-1" integer form which yields a nonsensical "0--0.5" (end before start).
        var workbook = new Workbook("PivotFractionalIntervalTest");
        var sheet = workbook.AddSheet("Data");
        SeedFractionalIntervalOrderingData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D2", "F6")
        };
        pivot.RowFields.Add(new PivotFieldModel(0, Grouping: PivotFieldGrouping.NumberRange, GroupStart: 0, GroupInterval: 0.5));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "D3").Should().Be("0-0.5");
        Text(sheet, "D4").Should().Be("0.5-1");
        Text(sheet, "D5").Should().Be("Grand Total");
    }
}
