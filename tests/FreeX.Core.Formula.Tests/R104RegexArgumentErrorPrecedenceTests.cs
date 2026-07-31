using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-104 finding: REGEXTEST/REGEXEXTRACT/REGEXREPLACE resolved the pattern (and, for
/// REGEXREPLACE, replacement/occurrence/case-sensitivity) arguments before ever checking args[0]
/// (the text argument) for an error, so when multiple arguments are simultaneously errors the
/// wrong (non-leftmost) argument's error won -- breaking the left-to-right argument-error
/// precedence Excel applies and that every other multi-argument function in this codebase
/// (AGGREGATE, IF/IFERROR, EXACT, TEXTBEFORE/TEXTAFTER, ...) already follows.
/// </summary>
public sealed class R104RegexArgumentErrorPrecedenceTests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void R104_RegexTest_TextAndPatternBothErrors_ReturnsLeftmostTextError()
    {
        // args[0] = 1/0 -> #DIV/0!, args[1] = NA() -> #N/A. Leftmost (text) argument's error
        // must win, matching Excel's left-to-right precedence.
        var result = _eval.Evaluate("=REGEXTEST(1/0, NA())", Sheet());

        result.Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void R104_RegexExtract_TextAndPatternBothErrors_ReturnsLeftmostTextError()
    {
        var result = _eval.Evaluate("=REGEXEXTRACT(1/0, NA())", Sheet());

        result.Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void R104_RegexReplace_TextAndReplacementBothErrors_ReturnsLeftmostTextError()
    {
        // args[0] = 1/0 -> #DIV/0!, args[2] (replacement) = NA() -> #N/A. Previously RegexReplace
        // resolved the pattern (args[1]), then case-sensitivity (args[4]), then replacement
        // (args[2]) before ever reaching the args[0] check inside RegexReplaceScalar, so the
        // replacement's #N/A leaked out instead of the text argument's #DIV/0!.
        var result = _eval.Evaluate("=REGEXREPLACE(1/0, \"a\", NA())", Sheet());

        result.Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void R104_RegexReplace_TextAndOccurrenceBothErrors_ReturnsLeftmostTextError()
    {
        // args[0] = 1/0 -> #DIV/0!, args[3] (occurrence) = NA() -> #N/A.
        var result = _eval.Evaluate("=REGEXREPLACE(1/0, \"a\", \"b\", NA())", Sheet());

        result.Should().Be(ErrorValue.DivByZero);
    }

    // --- No-regression siblings: when only ONE argument is an error, that error must still
    // surface exactly as before (this is the behavior the pre-existing test suite already
    // encodes for pattern-only errors; these confirm text-only-error and pattern-only-error
    // cases both still work correctly after reordering the checks). ---

    [Fact]
    public void R104_RegexTest_OnlyPatternIsError_StillReturnsPatternError()
    {
        var result = _eval.Evaluate("=REGEXTEST(\"abc\", NA())", Sheet());

        result.Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void R104_RegexTest_OnlyTextIsError_StillReturnsTextError()
    {
        var result = _eval.Evaluate("=REGEXTEST(1/0, \"abc\")", Sheet());

        result.Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void R104_RegexReplace_OnlyPatternIsError_StillReturnsPatternError()
    {
        var result = _eval.Evaluate("=REGEXREPLACE(\"abc\", NA(), \"x\")", Sheet());

        result.Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void R104_RegexReplace_NoErrors_StillReplacesNormally()
    {
        // Plain sanity check that ordinary (non-error) REGEXREPLACE behavior is untouched.
        var result = _eval.Evaluate("=REGEXREPLACE(\"abc123\", \"[0-9]+\", \"X\")", Sheet());

        result.Should().Be(new TextValue("abcX"));
    }

    private static Sheet Sheet(params (int Row, int Col, ScalarValue Value)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (row, col, value) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)row, (uint)col), value);
        return sheet;
    }
}
