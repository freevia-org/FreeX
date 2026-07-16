using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R47-formula-database-dfunctions-3-1 / R47-formula-database-dfunctions-3-2: Excel's documented
/// "computed criteria" convention for D-functions -- a criteria column whose header is either
/// blank, or a label that isn't any of the database's own column names -- holds a formula that is
/// re-evaluated per candidate database row (its relative references shifted down to that row),
/// not a plain value comparison. Before the fix, DbRowMatchesCriteriaRow either silently skipped
/// the whole column (blank header -> "continue", over-matching every row) or rejected the whole
/// criteria row outright (non-column-name header -> "return false", excluding every row), instead
/// of evaluating the computed formula.
/// </summary>
public sealed class R47_DatabaseComputedCriteriaTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeNameSalaryDatabase()
    {
        // Database A1:B4 (Name/Salary): Davolio=100, David=200, Smith=300.
        var sheet = new Sheet(SheetId.New(), "S");
        Set(sheet, 1, 1, new TextValue("Name"));
        Set(sheet, 1, 2, new TextValue("Salary"));
        Set(sheet, 2, 1, new TextValue("Davolio"));
        Set(sheet, 2, 2, new NumberValue(100));
        Set(sheet, 3, 1, new TextValue("David"));
        Set(sheet, 3, 2, new NumberValue(200));
        Set(sheet, 4, 1, new TextValue("Smith"));
        Set(sheet, 4, 2, new NumberValue(300));
        return sheet;
    }

    [Fact]
    public void DSum_BlankHeaderComputedCriterion_EvaluatesFormulaPerRow_NotIgnored()
    {
        // Criteria D1:D2: D1 blank (Excel's "computed criteria" header convention), D2 holds a
        // formula referencing the first data row's Salary cell (B2), re-evaluated per candidate
        // row -- only Smith's row (Salary=300 > 200) should match. Pre-fix: the blank header made
        // FreeX skip the whole column entirely, over-matching every row -> DSUM = 600 (all rows).
        var sheet = MakeNameSalaryDatabase();
        SetFormula(sheet, 2, 4, "=B2>200");

        _eval.Evaluate("=DSUM(A1:B4,\"Salary\",D1:D2)", sheet)
            .Should().Be(new NumberValue(300));
    }

    [Fact]
    public void DSum_NonColumnHeaderComputedCriterion_EvaluatesFormulaPerRow_NotAlwaysExcluded()
    {
        // Criteria E1:E2: E1 = "AvgCheck" (not a database column name -- also a valid computed-
        // criteria header per Excel's documentation), E2 holds a formula referencing B2. Matches
        // David (200) and Smith (300) -> DSUM = 500. Pre-fix: the unresolvable header made FreeX
        // reject the whole criteria row outright for every database row -> DSUM = 0.
        var sheet = MakeNameSalaryDatabase();
        Set(sheet, 1, 5, new TextValue("AvgCheck"));
        SetFormula(sheet, 2, 5, "=B2>150");

        _eval.Evaluate("=DSUM(A1:B4,\"Salary\",E1:E2)", sheet)
            .Should().Be(new NumberValue(500));
    }

    [Fact]
    public void DSum_MappedColumnCriterion_StillWorksNormally_NotRegressedByComputedCriteriaFix()
    {
        // Sibling regression guard: a normal, mapped-column criteria header ("Salary" matching a
        // real database column) must keep comparing values directly -- not be misrouted into the
        // computed-criteria path just because it happens to be a text header.
        var sheet = MakeNameSalaryDatabase();
        Set(sheet, 1, 6, new TextValue("Salary"));
        Set(sheet, 2, 6, new NumberValue(200));

        _eval.Evaluate("=DSUM(A1:B4,\"Salary\",F1:F2)", sheet)
            .Should().Be(new NumberValue(200));
    }

    [Fact]
    public void DSum_BlankHeaderWithNoFormulaInCriteriaCell_StillIgnoredAsNoCondition()
    {
        // Sibling regression guard: a blank header whose criteria cell holds no formula (blank,
        // or a plain literal) has no computed condition to apply and must still be ignored,
        // matching Advanced Filter's own computed-criteria convention -- it must NOT start
        // rejecting every row just because it's not a mapped column.
        var sheet = MakeNameSalaryDatabase();
        // G1 left blank (no header); G2 left blank too (no formula, no value).

        _eval.Evaluate("=DSUM(A1:B4,\"Salary\",G1:G2)", sheet)
            .Should().Be(new NumberValue(600));
    }

    private static void Set(Sheet sheet, uint row, uint col, ScalarValue value)
        => sheet.SetCell(new CellAddress(sheet.Id, row, col), value);

    private static void SetFormula(Sheet sheet, uint row, uint col, string formulaText)
        => sheet.SetCell(new CellAddress(sheet.Id, row, col), Cell.FromFormula(formulaText));
}
