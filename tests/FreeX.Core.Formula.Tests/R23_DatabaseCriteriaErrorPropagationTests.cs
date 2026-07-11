using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-23 review fix (R23-error-propagation-3): database functions (DSUM/DGET/...) must
/// propagate an error-valued criteria-table cell instead of silently treating it as "never
/// matches", mirroring how SUMIF/SUMIFS/MAXIFS explicitly check `criteria is ErrorValue` and
/// return it before ever compiling the criteria matcher.
/// </summary>
public sealed class R23_DatabaseCriteriaErrorPropagationTests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void DSum_CriteriaCellIsError_PropagatesErrorInsteadOfTreatingAsNoMatch()
    {
        // Region/Sales database; criteria header "Region" with a criteria value cell that is
        // itself an error (as if computed by a formula that failed, e.g. =#REF!).
        var sheet = new Sheet(SheetId.New(), "S");
        Set(sheet, 1, 1, new TextValue("Region"));
        Set(sheet, 1, 2, new TextValue("Sales"));
        Set(sheet, 2, 1, new TextValue("East"));
        Set(sheet, 2, 2, new NumberValue(100));
        Set(sheet, 3, 1, new TextValue("West"));
        Set(sheet, 3, 2, new NumberValue(200));
        Set(sheet, 1, 4, new TextValue("Region"));
        Set(sheet, 2, 4, ErrorValue.Ref);

        _eval.Evaluate("=DSUM(A1:B3,\"Sales\",D1:D2)", sheet)
            .Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void DGet_CriteriaCellIsError_PropagatesErrorInsteadOfReturningValueError()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        Set(sheet, 1, 1, new TextValue("Region"));
        Set(sheet, 1, 2, new TextValue("Sales"));
        Set(sheet, 2, 1, new TextValue("East"));
        Set(sheet, 2, 2, new NumberValue(100));
        Set(sheet, 3, 1, new TextValue("West"));
        Set(sheet, 3, 2, new NumberValue(200));
        Set(sheet, 1, 4, new TextValue("Region"));
        Set(sheet, 2, 4, ErrorValue.NA);

        // Before the fix this silently matched zero rows and returned #VALUE! (the generic
        // "no records found" result) rather than propagating the criteria cell's own error.
        _eval.Evaluate("=DGET(A1:B3,\"Sales\",D1:D2)", sheet)
            .Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void DSum_NormalCriteria_StillMatchesAndSumsCorrectly()
    {
        // Regression guard: ordinary (non-error) criteria must be unaffected by the fix.
        var sheet = new Sheet(SheetId.New(), "S");
        Set(sheet, 1, 1, new TextValue("Region"));
        Set(sheet, 1, 2, new TextValue("Sales"));
        Set(sheet, 2, 1, new TextValue("East"));
        Set(sheet, 2, 2, new NumberValue(100));
        Set(sheet, 3, 1, new TextValue("West"));
        Set(sheet, 3, 2, new NumberValue(200));
        Set(sheet, 1, 4, new TextValue("Region"));
        Set(sheet, 2, 4, new TextValue("East"));

        _eval.Evaluate("=DSUM(A1:B3,\"Sales\",D1:D2)", sheet)
            .Should().Be(new NumberValue(100));
    }

    private static void Set(Sheet sheet, uint row, uint col, ScalarValue value)
        => sheet.SetCell(new CellAddress(sheet.Id, row, col), value);
}
