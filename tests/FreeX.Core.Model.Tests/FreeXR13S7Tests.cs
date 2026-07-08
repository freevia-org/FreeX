using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round-13 fix bucket S7 — R13-meta-3: the value-filter recompute (FilterCommand's
/// RecomputeHiddenRows / ClearOwnedRows) must consult Sheet.ColumnFilterOwnedRows before
/// un-hiding a row, exactly like the condition/color/Top-Bottom side (ApplyColumnOwnedVisibility)
/// already does — otherwise loosening or clearing a value filter on one column can un-hide a row
/// a still-active condition/color/Top-Bottom filter on ANOTHER column is responsible for hiding,
/// breaking Excel's AND-across-columns AutoFilter semantics.
/// </summary>
public sealed class FreeXR13S7Tests
{
    // R13-meta-3 (primary defect site: FilterCommand.RecomputeHiddenRows's per-row loop, originally
    // FilterCommand.cs:156): AutoFilter on A1:B5. Column B gets a "greater than 100" condition filter
    // that hides row 5 (B5=50). Column A then gets a value filter that ALSO excludes row 5 (A5="Foo"),
    // so both mechanisms now own row 5. Loosening column A's value filter so row 5's A value passes
    // must NOT un-hide row 5, because column B's condition filter is still active and still fails it —
    // Excel ANDs AutoFilter criteria across columns, so row 5 must stay hidden.
    [Fact]
    public void RecomputeHiddenRows_LoosenedValueFilterColumn_KeepsRowHiddenByOtherActiveConditionFilter()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("ColA"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("ColB"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Keep"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(200));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Keep"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(200));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Keep"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(200));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("Foo"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(50));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 2));
        var ctx = new TestCommandContext(wb);

        // Column B (offset 1): condition filter "> 100" hides row 5 (B5=50) and registers ownership.
        new FilterConditionCommand(sheet.Id, range, 1, new NumberGreaterThanFilterCriterion(100))
            .Apply(ctx).Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().Contain(5u);
        sheet.ColumnFilterOwnedRows[2].Should().Contain(5u);

        // Column A (offset 0): value filter ["Keep"] ALSO excludes row 5 (A5="Foo") — both
        // mechanisms now own row 5.
        new FilterCommand(sheet.Id, range, 0, ["Keep"]).Apply(ctx).Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().Contain(5u);
        sheet.ValueFilterHiddenRows.Should().Contain(5u);

        // Loosen column A's value filter so row 5's A value ("Foo") now passes too.
        new FilterCommand(sheet.Id, range, 0, ["Keep", "Foo"]).Apply(ctx).Success.Should().BeTrue();

        // Row 5 must STILL be hidden: column B's condition filter is still active and B5=50 still
        // fails "> 100". Excel ANDs across columns — a row hidden by ANY active column's filter must
        // stay hidden regardless of what other columns' filters decide.
        sheet.FilterHiddenRows.Should().Contain(5u,
            "column B's still-active condition filter excludes row 5 even though column A's value filter now allows it");
        sheet.ValueFilterHiddenRows.Should().NotContain(5u,
            "the value-filter mechanism itself no longer owns row 5 once column A's filter allows it");
    }

    // R13-meta-3 (same defect via FilterCommand.ClearOwnedRows, originally FilterCommand.cs:502-506):
    // same AND-across-columns setup, but this time column A's value filter is CLEARED entirely
    // (its last active value-filter column removed) rather than loosened — exercising the
    // "sheet.ActiveValueFilterColumns.Count == 0" / ClearOwnedRows code path instead of the per-row
    // recompute loop. Row 5 must still stay hidden because column B's condition filter still owns it.
    [Fact]
    public void ClearOwnedRows_ClearingLastValueFilterColumn_KeepsRowHiddenByOtherActiveConditionFilter()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("ColA"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("ColB"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Keep"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(200));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Foo"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(50));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        var ctx = new TestCommandContext(wb);

        // Column B (offset 1): condition filter "> 100" hides row 3 (B3=50).
        new FilterConditionCommand(sheet.Id, range, 1, new NumberGreaterThanFilterCriterion(100))
            .Apply(ctx).Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().Contain(3u);

        // Column A (offset 0): value filter ["Keep"] ALSO excludes row 3 (A3="Foo").
        new FilterCommand(sheet.Id, range, 0, ["Keep"]).Apply(ctx).Success.Should().BeTrue();
        sheet.ValueFilterHiddenRows.Should().Contain(3u);

        // Clear column A's value filter entirely (its last active value-filter column is removed),
        // driving sheet.ActiveValueFilterColumns.Count to 0 and taking the ClearOwnedRows path.
        new FilterCommand(sheet.Id, range, 0, []).Apply(ctx).Success.Should().BeTrue();

        sheet.ActiveValueFilterColumns.Should().BeEmpty();
        sheet.FilterHiddenRows.Should().Contain(3u,
            "column B's still-active condition filter excludes row 3 even after column A's value filter is cleared");
    }
}
