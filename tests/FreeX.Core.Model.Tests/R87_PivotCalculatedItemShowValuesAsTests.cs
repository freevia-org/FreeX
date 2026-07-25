using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

// R87-calc-pivot-aggregation-5-3: a calculated row/column item must have the data field's
// Show Values As setting (e.g. % of Grand Total) applied to its own combined value, the
// same way every ordinary sibling row/column already does - not a raw aggregate that
// silently ignores the setting.
public sealed partial class PivotTableRefreshServiceTests
{
    [Fact]
    public void Refresh_CalculatedRowItem_AppliesShowValuesAsPercentOfGrandTotal()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "% of Grand Total", "sum", ShowValuesAs: PivotShowValuesAs.PercentOfGrandTotal));
        pivot.CalculatedItems.Add(new PivotCalculatedItemModel(0, "East + West", "East+West"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        // East=25, West=45; the grand-total denominator for "% of Grand Total" is the
        // retained source rows' own sum, 70 (see Refresh_CanShowValuesAsPercentOfGrandTotal).
        Text(sheet, "E3").Should().Be("East");
        Number(sheet, "F3").Should().BeApproximately(25d / 70d, 0.0000001);
        Text(sheet, "E4").Should().Be("West");
        Number(sheet, "F4").Should().BeApproximately(45d / 70d, 0.0000001);
        // The calculated item's own cell must show its share of the grand total (70/70 =
        // 100%, since "East + West" spans the whole dataset), not the raw dollar sum (70)
        // sitting unconverted in the same %-formatted column.
        Text(sheet, "E5").Should().Be("East + West");
        Number(sheet, "F5").Should().Be(1);
    }

    // No-regression sibling: with no Show Values As setting (the default), a calculated
    // item must still display its plain raw combined aggregate exactly as before.
    [Fact]
    public void Refresh_CalculatedRowItem_WithoutShowValuesAs_StillShowsRawAggregate()
    {
        var workbook = new Workbook("PivotRefreshTest");
        var sheet = workbook.AddSheet("Data");
        SeedSalesData(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "G8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.CalculatedItems.Add(new PivotCalculatedItemModel(0, "East + West", "East+West"));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        Text(sheet, "E3").Should().Be("East");
        Number(sheet, "F3").Should().Be(25);
        Text(sheet, "E4").Should().Be("West");
        Number(sheet, "F4").Should().Be(45);
        Text(sheet, "E5").Should().Be("East + West");
        Number(sheet, "F5").Should().Be(70);
        Text(sheet, "E6").Should().Be("Grand Total");
        Number(sheet, "F6").Should().Be(140);
    }
}
