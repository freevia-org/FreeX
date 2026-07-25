using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

// R87-calc-pivot-aggregation-5-2: a numeric-range group's "Ending at" bound
// (PivotFieldModel.GroupEnd, set via the Group Field dialog) must actually clamp which
// bucket a value lands in - values at or past it fall into a distinct overflow group
// instead of silently growing a brand-new interval-sized bucket past the configured end.
public sealed partial class PivotTableRefreshServiceTests
{
    [Fact]
    public void Refresh_NumberRangeGroupedFieldWithEnd_OutOfRangeValueFormsOverflowGroup()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedPriceSalesDataWithOutOfRangeRow(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B7"),
            TargetRange = Range(sheet, "D2", "F11")
        };
        pivot.RowFields.Add(new PivotFieldModel(0, Grouping: PivotFieldGrouping.NumberRange, GroupStart: 0, GroupEnd: 50, GroupInterval: 10));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        // Prices 5,15,25,35,45 fall into their normal 0-49 interval buckets; 95 is past the
        // "Ending at" 50 boundary and must land in a distinct ">50" overflow group instead of
        // a brand-new "90-99" bucket the old (GroupEnd-ignoring) code would have created.
        Text(sheet, "D3").Should().Be("0-9");
        Number(sheet, "E3").Should().Be(10);
        Text(sheet, "D4").Should().Be("10-19");
        Number(sheet, "E4").Should().Be(20);
        Text(sheet, "D5").Should().Be("20-29");
        Number(sheet, "E5").Should().Be(30);
        Text(sheet, "D6").Should().Be("30-39");
        Number(sheet, "E6").Should().Be(40);
        Text(sheet, "D7").Should().Be("40-49");
        Number(sheet, "E7").Should().Be(50);
        Text(sheet, "D8").Should().Be(">50");
        Number(sheet, "E8").Should().Be(60);
        Text(sheet, "D9").Should().Be("Grand Total");
        Number(sheet, "E9").Should().Be(210);
    }

    // No-regression sibling: with GroupEnd left null (as in the pre-existing
    // NumericLabelGrouping tests), buckets keep growing unbounded past any interval - the
    // overflow-group behavior above must only kick in when an explicit "Ending at" is set.
    [Fact]
    public void Refresh_NumberRangeGroupedFieldWithoutEnd_NoOverflowGroupIsCreated()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedPriceSalesDataWithOutOfRangeRow(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B7"),
            TargetRange = Range(sheet, "D2", "F11")
        };
        pivot.RowFields.Add(new PivotFieldModel(0, Grouping: PivotFieldGrouping.NumberRange, GroupStart: 0, GroupInterval: 10));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "D3").Should().Be("0-9");
        Text(sheet, "D4").Should().Be("10-19");
        Text(sheet, "D5").Should().Be("20-29");
        Text(sheet, "D6").Should().Be("30-39");
        Text(sheet, "D7").Should().Be("40-49");
        Text(sheet, "D8").Should().Be("90-99");
        Number(sheet, "E8").Should().Be(60);
        Text(sheet, "D9").Should().Be("Grand Total");
        Number(sheet, "E9").Should().Be(210);
    }

    private static void SeedPriceSalesDataWithOutOfRangeRow(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Price"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new NumberValue(5));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new NumberValue(15));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A4"), new NumberValue(25));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));
        sheet.SetCell(Addr(sheet, "A5"), new NumberValue(35));
        sheet.SetCell(Addr(sheet, "B5"), new NumberValue(40));
        sheet.SetCell(Addr(sheet, "A6"), new NumberValue(45));
        sheet.SetCell(Addr(sheet, "B6"), new NumberValue(50));
        sheet.SetCell(Addr(sheet, "A7"), new NumberValue(95));
        sheet.SetCell(Addr(sheet, "B7"), new NumberValue(60));
    }
}
