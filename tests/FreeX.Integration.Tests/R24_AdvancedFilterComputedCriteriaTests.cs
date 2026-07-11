using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression test for R24-autofilter-advanced-3: AdvancedFilterPlanBuilder.BuildCriteriaRows
/// skipped any criteria column whose header cell was blank -- the standard Excel convention for a
/// computed/formula criterion. That silently dropped the formula condition entirely instead of
/// evaluating it (with its relative references shifted) against each candidate list row, so the
/// remaining plain criteria matched a wider row set than real Excel would.
/// </summary>
public class R24_AdvancedFilterComputedCriteriaTests
{
    [Fact]
    public void Apply_ComputedCriteriaColumnWithBlankHeader_AndsFormulaConditionWithPlainCriteria()
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

        // Criteria range D1:E2 -- D1="Region" (plain field), E1 blank (computed criterion),
        // D2="West", E2 formula "=B2>100" referencing the list's first data row.
        Set(sheet, 1, 4, "Region");
        // E1 intentionally left blank (no SetCell call) -- this is the Excel convention that
        // marks column E as a computed/formula criterion instead of a plain field match.
        Set(sheet, 2, 4, "West");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 5), Cell.FromFormula("B2>100"));

        var command = new AdvancedFilterCommand(
            ListRange: new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            CriteriaRange: new GridRange(new CellAddress(sheet.Id, 1, 4), new CellAddress(sheet.Id, 2, 5)),
            CopyTo: null,
            UniqueRecordsOnly: false);

        command.Apply(new TestCommandContext(wb)).Success.Should().BeTrue();

        // Row 3 (West, Amount=150) is the only row satisfying Region="West" AND B-row>100.
        // Row 2 (West, Amount=50) must be hidden by the computed condition, and row 4 (East)
        // must be hidden by the plain Region condition.
        sheet.FilterHiddenRows.Should().BeEquivalentTo([2u, 4u]);
        sheet.FilterHiddenRows.Should().NotContain(3u);
    }

    private static void Set(Sheet sheet, uint row, uint col, string text) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, col), new TextValue(text));

    private static void Set(Sheet sheet, uint row, uint col, double number) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(number));
}
