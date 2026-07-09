using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

// R15 round-15 fixes:
//   R15-pivot-tables-deep-1: RunningTotal must order/accumulate base-field items using the
//     same numeric-aware comparer used for row/column display (PivotKeyComparer), not plain
//     lexicographic OrdinalIgnoreCase, or numeric base fields (1,2,3,10,...) accumulate wrong.
//   R15-pivot-tables-deep-2: in a matrix pivot (row + column fields), "Running Total In" must
//     accumulate over the CURRENT COLUMN's own rows, not the whole-grid GrandTotalRows, or
//     every column shows the same grid-wide running total.
public sealed partial class PivotTableRefreshServiceTests
{
    private static void SeedNumericItemAmounts(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Item"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new NumberValue(1));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(5));
        sheet.SetCell(Addr(sheet, "A3"), new NumberValue(2));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(7));
        sheet.SetCell(Addr(sheet, "A4"), new NumberValue(3));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(11));
        sheet.SetCell(Addr(sheet, "A5"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "B5"), new NumberValue(13));
    }

    private static void SeedRegionQuarterAmounts(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Quarter"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B3"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C3"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B4"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C4"), new NumberValue(100));
        sheet.SetCell(Addr(sheet, "A5"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B5"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C5"), new NumberValue(200));
    }

    [Fact]
    public void Refresh_RunningTotalIn_NumericBaseField_AccumulatesInNumericDisplayOrder()
    {
        // R15-pivot-tables-deep-1: base items 1,2,3,10 must accumulate in numeric display
        // order (1,2,3,10), not lexicographic order (1,10,2,3). At the "10" row the running
        // total must include all four items, not just {1,10}.
        var workbook = new Workbook("PivotNumericRunningTotalTest");
        var sheet = workbook.AddSheet("Data");
        SeedNumericItemAmounts(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B5"),
            TargetRange = Range(sheet, "E2", "G8")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(
            1,
            "Running Total",
            "sum",
            ShowValuesAs: PivotShowValuesAs.RunningTotalIn,
            BaseFieldIndex: 0));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        // Rows are written in numeric display order: 1 (row3), 2 (row4), 3 (row5), 10 (row6).
        Number(sheet, "F3").Should().Be(5);       // item 1: running total = 5
        Number(sheet, "F4").Should().Be(12);      // item 2: 5 + 7
        Number(sheet, "F5").Should().Be(23);      // item 3: 5 + 7 + 11
        Number(sheet, "F6").Should().Be(36);      // item 10: 5 + 7 + 11 + 13 (all four items)
    }

    [Fact]
    public void Refresh_MatrixRunningTotalIn_AccumulatesPerColumnNotGridWide()
    {
        // R15-pivot-tables-deep-2: in a matrix pivot, each column's running total must
        // accumulate over that column's own rows only. Before the fix, both columns shared
        // the identical grid-wide running total (330) at Q2 instead of their own per-column
        // cumulative sums (30 for East, 300 for West).
        var workbook = new Workbook("PivotMatrixRunningTotalTest");
        var sheet = workbook.AddSheet("Data");
        SeedRegionQuarterAmounts(sheet);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C5"),
            TargetRange = Range(sheet, "E2", "I7")
        };
        pivot.RowFields.Add(new PivotFieldModel(1));    // Quarter: Q1, Q2
        pivot.ColumnFields.Add(new PivotFieldModel(0)); // Region: East, West
        pivot.DataFields.Add(new PivotDataFieldModel(
            2,
            "Running Total",
            "sum",
            ShowValuesAs: PivotShowValuesAs.RunningTotalIn,
            BaseFieldIndex: 1));

        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        // Row3 = Q1 (first item): running total = the value itself for each column.
        Number(sheet, "F3").Should().Be(10);  // East/Q1
        Number(sheet, "G3").Should().Be(100); // West/Q1

        // Row4 = Q2 (second item): per-column cumulative sums, NOT the grid-wide 330.
        Number(sheet, "F4").Should().Be(30);   // East: 10 + 20
        Number(sheet, "G4").Should().Be(300);  // West: 100 + 200
        Number(sheet, "F4").Should().NotBe(Number(sheet, "G4"));
    }
}
