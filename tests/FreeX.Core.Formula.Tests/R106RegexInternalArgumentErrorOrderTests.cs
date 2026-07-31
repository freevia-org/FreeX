using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-106 finding: the R104 fix made RegexReplace/RegexExtract check args[0] (text) for an
/// error before any other argument, but left the resolution order AMONG the remaining optional
/// arguments unchanged. Both functions resolved case_sensitivity -- the LAST parameter -- together
/// with the pattern (position 2), before ever inspecting earlier-positioned optional arguments
/// (RegexReplace's replacement/occurrence at positions 3/4; RegexExtract's return_mode at position
/// 3). So when an earlier-positioned argument AND case_sensitivity are simultaneously errors, the
/// wrong (non-leftmost) one used to win. These tests pin the fix: strict ascending-index (0,1,2,
/// 3,4) argument-error precedence, matching TextBeforeAfter's convention in BuiltInFunctions.TextSplit.cs.
/// </summary>
public sealed class R106RegexInternalArgumentErrorOrderTests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void R106_RegexReplace_ReplacementAndCaseSensitivityBothErrors_ReturnsLeftmostReplacementError()
    {
        // replacement (position 3) = 1/0 -> #DIV/0!, case_sensitivity (position 5, the LAST
        // parameter) = NA() -> #N/A. Leftmost (replacement) must win.
        var result = _eval.Evaluate("=REGEXREPLACE(\"hello\",\"l\",1/0,1,NA())", Sheet());

        result.Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void R106_RegexReplace_OccurrenceAndCaseSensitivityBothErrors_ReturnsLeftmostOccurrenceError()
    {
        // occurrence (position 4) = 1/0 -> #DIV/0!, case_sensitivity (position 5) = NA() -> #N/A.
        var result = _eval.Evaluate("=REGEXREPLACE(\"hello\",\"l\",\"x\",1/0,NA())", Sheet());

        result.Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void R106_RegexExtract_ReturnModeAndCaseSensitivityBothErrors_ReturnsLeftmostReturnModeError()
    {
        // return_mode (position 3) = 1/0 -> #DIV/0!, case_sensitivity (position 4, the LAST
        // parameter) = NA() -> #N/A. Leftmost (return_mode) must win.
        var result = _eval.Evaluate("=REGEXEXTRACT(\"abc123\",\"[0-9]+\",1/0,NA())", Sheet());

        result.Should().Be(ErrorValue.DivByZero);
    }

    // --- No-regression siblings: a single error in any one of the previously-mishandled
    // positions must still surface correctly, and ordinary (no-error) evaluation is unaffected. ---

    [Fact]
    public void R106_RegexReplace_OnlyCaseSensitivityIsError_StillReturnsCaseSensitivityError()
    {
        var result = _eval.Evaluate("=REGEXREPLACE(\"hello\",\"l\",\"x\",1,NA())", Sheet());

        result.Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void R106_RegexReplace_OnlyOccurrenceIsError_StillReturnsOccurrenceError()
    {
        var result = _eval.Evaluate("=REGEXREPLACE(\"hello\",\"l\",\"x\",NA())", Sheet());

        result.Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void R106_RegexExtract_OnlyCaseSensitivityIsError_StillReturnsCaseSensitivityError()
    {
        var result = _eval.Evaluate("=REGEXEXTRACT(\"abc123\",\"[0-9]+\",1,NA())", Sheet());

        result.Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void R106_RegexExtract_OnlyReturnModeIsError_StillReturnsReturnModeError()
    {
        var result = _eval.Evaluate("=REGEXEXTRACT(\"abc123\",\"[0-9]+\",NA())", Sheet());

        result.Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void R106_RegexReplace_NoErrors_StillReplacesNormallyWithAllOptionalArgs()
    {
        // Sanity: full 5-argument, no-error REGEXREPLACE behavior is unaffected by the reorder.
        // case_sensitivity=1 -> IgnoreCase per this codebase's convention, so "a" matches all
        // four letters; occurrence=2 replaces the second match (the "A" at index 1).
        var result = _eval.Evaluate("=REGEXREPLACE(\"aAaA\",\"a\",\"X\",2,1)", Sheet());

        result.Should().Be(new TextValue("aXaA"));
    }

    [Fact]
    public void R106_RegexExtract_NoErrors_StillExtractsNormallyWithAllOptionalArgs()
    {
        // Sanity: full 4-argument, no-error REGEXEXTRACT behavior is unaffected by the reorder.
        var result = _eval.Evaluate("=REGEXEXTRACT(\"ABC123\",\"[a-z]+\",0,1)", Sheet());

        result.Should().Be(new TextValue("ABC"));
    }

    private static Sheet Sheet(params (int Row, int Col, ScalarValue Value)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (row, col, value) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)row, (uint)col), value);
        return sheet;
    }
}
