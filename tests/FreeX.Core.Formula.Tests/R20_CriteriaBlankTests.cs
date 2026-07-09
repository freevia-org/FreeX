using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression tests for R20-math-trig-functions-3.
///
/// Bare comparison-operator criteria "=" and "&lt;&gt;" (used by COUNTIF/SUMIF/etc.)
/// must special-case blank cells exactly like the plain "" / "&lt;&gt;" text criteria
/// and like the engine's own equality operator: a blank cell coerces to "" for text
/// equality, so "=" must match blanks and "&lt;&gt;" must NOT match blanks.
///
/// Root cause: MatchesTextComparison (BuiltInFunctions.Criteria.cs) treated any
/// non-TextValue cell (including BlankValue) as a non-match for Equal and an
/// unconditional match for NotEqual, without ever checking whether the rhs was
/// the empty string — so COUNTIF(range,"=") returned 0 instead of counting blanks,
/// and COUNTIF(range,"&lt;&gt;") counted blanks too, instead of excluding them.
/// </summary>
public class R20_criteria_blank_Tests
{
    private readonly FormulaEvaluator _eval = new();

    // A1 = blank, A2 = "foo", A3 = 5.
    private static Sheet MakeSheetWithBlank()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        // A1 intentionally left unset (blank cell).
        sheet.SetCell(new CellAddress(sheet.Id, 2u, 1u), new TextValue("foo"));
        sheet.SetCell(new CellAddress(sheet.Id, 3u, 1u), new NumberValue(5));
        return sheet;
    }

    [Fact]
    public void Countif_BareEquals_MatchesBlankCell_LikeEmptyStringCriteria()
    {
        var sheet = MakeSheetWithBlank();

        // Bare "=" must match the single blank cell (A1) — same as plain "".
        _eval.Evaluate("=COUNTIF(A1:A3,\"=\")", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Countif_BareEquals_EqualsCountif_WithEmptyStringCriteria()
    {
        var sheet = MakeSheetWithBlank();

        var withEquals = _eval.Evaluate("=COUNTIF(A1:A3,\"=\")", sheet);
        var withEmptyString = _eval.Evaluate("=COUNTIF(A1:A3,\"\")", sheet);

        withEquals.Should().Be(withEmptyString);
        withEquals.Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Countif_BareNotEqual_ExcludesBlankCell()
    {
        var sheet = MakeSheetWithBlank();

        // Bare "<>" must count only the 2 non-blank cells (A2="foo", A3=5).
        _eval.Evaluate("=COUNTIF(A1:A3,\"<>\")", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Sumif_BareEquals_SumsOnlyBlankCellsRange()
    {
        // sumRange = B1:B3 = {1,2,3}; criteriaRange = A1:A3 = {blank,"foo",5}
        var sheet = MakeSheetWithBlank();
        sheet.SetCell(new CellAddress(sheet.Id, 1u, 2u), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2u, 2u), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3u, 2u), new NumberValue(3));

        // Only A1 (blank) matches "=", so the corresponding B1 (=1) is summed.
        _eval.Evaluate("=SUMIF(A1:A3,\"=\",B1:B3)", sheet).Should().Be(new NumberValue(1));
    }
}
