using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression test for the round-111 family gap: AdvancedFilterPlanBuilder.ComputedCriteriaCheck
/// anchored a computed (formula) criterion's relative-reference shift on the criteria cell's own
/// (usually disjoint) row instead of on the list range's first data row. Excel's documented
/// "computed criteria" convention requires the authored formula to be evaluated as if it sat at
/// the list's first data row, then shifted per candidate row -- independent of where the criteria
/// cell itself lives. Using the criteria cell's own row instead offsets every comparison by the
/// (large, arbitrary) distance between the criteria region and the list, silently breaking the
/// computed condition for every row. This mirrors the D-function fix in
/// BuiltInFunctions.Database.TryEvaluateComputedCriterion (src/FreeX.Core.Formula).
/// </summary>
public class R111_AdvancedFilterComputedCriteriaAnchorTests
{
    /// <summary>
    /// The criteria region here is placed far below the list (row 20), unlike the pre-existing
    /// R24 computed-criteria test where the formula's own row (2) happens to coincide with the
    /// list's first data row (2) -- which is exactly why that test could not catch this bug. The
    /// authored formula "=B2>100" references the list's first data row directly, per Excel
    /// convention, and must be evaluated unshifted for row 2, shifted +1 for row 3, etc.
    /// </summary>
    [Fact]
    public void Apply_ComputedCriteriaFarFromListRange_AnchorsShiftOnListFirstDataRow_NotCriteriaCellRow()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        // List range A1:B4 -- Region/Amount headers, three data rows.
        Set(sheet, 1, 1, "Region");
        Set(sheet, 1, 2, "Amount");
        Set(sheet, 2, 1, "West");
        Set(sheet, 2, 2, 50);   // West, Amount<=100 -> excluded by the computed condition
        Set(sheet, 3, 1, "West");
        Set(sheet, 3, 2, 150);  // West, Amount>100 -> matches both conditions
        Set(sheet, 4, 1, "East");
        Set(sheet, 4, 2, 200);  // Not West -> excluded regardless of Amount

        // Criteria range D20:E21, far below the list -- D20="Region" (plain field), E20 blank
        // (computed criterion), D21="West", E21 formula "=B2>100" referencing the list's first
        // data row (row 2) directly, exactly as Excel's help documents authoring a computed
        // criterion.
        Set(sheet, 20, 4, "Region");
        Set(sheet, 21, 4, "West");
        sheet.SetCell(new CellAddress(sheet.Id, 21, 5), Cell.FromFormula("B2>100"));

        var command = new AdvancedFilterCommand(
            ListRange: new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            CriteriaRange: new GridRange(new CellAddress(sheet.Id, 20, 4), new CellAddress(sheet.Id, 21, 5)),
            CopyTo: null,
            UniqueRecordsOnly: false);

        command.Apply(new TestCommandContext(wb)).Success.Should().BeTrue();

        // Row 3 (West, Amount=150) is the only row satisfying Region="West" AND B-row>100.
        // Before the fix, the computed criterion was shifted from row 21 (the criteria cell's own
        // row) instead of row 2 (the list's first data row), so every candidate row's shift landed
        // on invalid/unrelated cells far above the sheet and the computed condition evaluated to
        // false for every row -- hiding row 3 too, and leaving FilterHiddenRows == {2, 3, 4}
        // instead of the correct {2, 4}.
        sheet.FilterHiddenRows.Should().BeEquivalentTo([2u, 4u]);
        sheet.FilterHiddenRows.Should().NotContain(3u);
    }

    /// <summary>
    /// Sibling no-regression check: ordinary (non-computed) value criteria, which don't go
    /// through the anchor-shift logic at all, must keep filtering correctly regardless of where
    /// the criteria region sits relative to the list.
    /// </summary>
    [Fact]
    public void Apply_OrdinaryValueCriteriaFarFromListRange_StillFiltersCorrectly()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        Set(sheet, 1, 1, "Region");
        Set(sheet, 1, 2, "Amount");
        Set(sheet, 2, 1, "West");
        Set(sheet, 2, 2, 50);
        Set(sheet, 3, 1, "West");
        Set(sheet, 3, 2, 150);
        Set(sheet, 4, 1, "East");
        Set(sheet, 4, 2, 200);

        // Criteria range D20:E21, far below the list -- plain field criteria only (no computed
        // column): Region="West" AND Amount>100.
        Set(sheet, 20, 4, "Region");
        Set(sheet, 20, 5, "Amount");
        Set(sheet, 21, 4, "West");
        Set(sheet, 21, 5, ">100");

        var command = new AdvancedFilterCommand(
            ListRange: new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            CriteriaRange: new GridRange(new CellAddress(sheet.Id, 20, 4), new CellAddress(sheet.Id, 21, 5)),
            CopyTo: null,
            UniqueRecordsOnly: false);

        command.Apply(new TestCommandContext(wb)).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().BeEquivalentTo([2u, 4u]);
        sheet.FilterHiddenRows.Should().NotContain(3u);
    }

    private static void Set(Sheet sheet, uint row, uint col, string text) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, col), new TextValue(text));

    private static void Set(Sheet sheet, uint row, uint col, double number) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(number));
}
