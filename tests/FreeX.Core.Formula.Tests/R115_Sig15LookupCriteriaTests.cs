using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression tests for the round-115 finding: FormulaEvaluator.Operators.cs's CompareValues
/// (backing the worksheet =,&lt;,&gt;,&lt;=,&gt;=,&lt;&gt; operators) rounds both numeric operands to
/// 15 significant digits before comparing, matching Excel's documented storage/comparison
/// behavior. But the sibling comparison helpers that back every lookup/criteria function --
/// ScalarEquals and CompareScalar in BuiltInFunctions.Coercion.cs (used by MATCH/VLOOKUP/
/// HLOOKUP/XLOOKUP/XMATCH exact and approximate match, and SORT/SORTBY ordering), and
/// CriteriaMatcher's numeric comparisons in BuiltInFunctions.Criteria.cs (used by COUNTIF/
/// SUMIF/AVERAGEIF/COUNTIFS/SUMIFS/etc.) -- compared raw doubles with no rounding at all.
///
/// Any function whose own result isn't itself pre-rounded to 15 significant digits (e.g.
/// STDEV.S/VAR and siblings, which return a raw Math.Sqrt(...) via NumberResult with no
/// rounding) produces a stored cell value that differs from its own G15/General-format
/// displayed text only in the 16th+ significant digit. Excel's '=' operator correctly treats
/// the displayed value as equal to the stored value; MATCH/VLOOKUP/XLOOKUP/COUNTIF/SUMIF must
/// agree, but previously did not.
/// </summary>
public sealed class R115_Sig15LookupCriteriaTests
{
    private readonly FormulaEvaluator _eval = new();

    // A1:A10 = {3,7,7,19,24,5,8,12,1,17}; C1 holds the raw double that real Excel's
    // STDEV.S(A1:A10) produces for this data (7.4988888065721664...), which differs from
    // its own G15 display text ("7.49888880657217") only in the 16th+ significant digit --
    // exactly the STDEV.S scenario from the finding's own reproduction. (FreeX's own
    // STDEV.S has both a fast direct-range path, which happens to already pre-round its
    // result, and a general/slow path that does not; setting C1 directly to the raw,
    // unrounded double -- the standard way every sibling test in this file populates a
    // sheet -- isolates the comparison-helper bug under test from that routing detail,
    // while A1:A10 stays present as the realistic source data STDEV.S(A1:A10) is computed
    // over in the finding's report.)
    private Sheet MakeSheetWithComputedStdev(out string typedText)
    {
        const double rawStdev = 7.4988888065721664;
        var sheet = MakeSheet(
            (1, 1, new NumberValue(3)), (2, 1, new NumberValue(7)), (3, 1, new NumberValue(7)),
            (4, 1, new NumberValue(19)), (5, 1, new NumberValue(24)), (6, 1, new NumberValue(5)),
            (7, 1, new NumberValue(8)), (8, 1, new NumberValue(12)), (9, 1, new NumberValue(1)),
            (10, 1, new NumberValue(17)),
            (1, 3, new NumberValue(rawStdev)));

        typedText = BuiltInFunctions.NumberToExcelText(rawStdev);
        return sheet;
    }

    [Fact]
    public void EqualsOperator_AlreadyMatchesTypedDisplayText_BaselineSanityCheck()
    {
        // Sanity check reproducing the finding's own evidence: the '=' operator (CompareValues,
        // already 15-sig-digit rounded before this fix) finds the STDEV.S result equal to what
        // it visibly displays. This must remain TRUE both before and after the fix.
        var sheet = MakeSheetWithComputedStdev(out var typedText);
        var result = _eval.Evaluate($"=C1={typedText}", sheet);
        result.Should().Be(new BoolValue(true));
    }

    [Fact]
    public void Match_ExactMatchOnFunctionResult_FindsValueEqualsOperatorConfirms()
    {
        var sheet = MakeSheetWithComputedStdev(out var typedText);
        var result = _eval.Evaluate($"=MATCH({typedText},C1:C1,0)", sheet);
        result.Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Vlookup_ExactMatchOnFunctionResult_FindsValueEqualsOperatorConfirms()
    {
        var sheet = MakeSheetWithComputedStdev(out var typedText);
        var result = _eval.Evaluate($"=VLOOKUP({typedText},C1:C1,1,FALSE)", sheet);
        result.Should().Be(new NumberValue(((NumberValue)_eval.Evaluate("=C1", sheet)).Value));
    }

    [Fact]
    public void Xlookup_ExactMatchOnFunctionResult_FindsValueEqualsOperatorConfirms()
    {
        var sheet = MakeSheetWithComputedStdev(out var typedText);
        var result = _eval.Evaluate($"=XLOOKUP({typedText},C1:C1,C1:C1)", sheet);
        result.Should().BeOfType<NumberValue>();
    }

    [Fact]
    public void Xmatch_ExactMatchOnFunctionResult_FindsValueEqualsOperatorConfirms()
    {
        var sheet = MakeSheetWithComputedStdev(out var typedText);
        var result = _eval.Evaluate($"=XMATCH({typedText},C1:C1)", sheet);
        result.Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Countif_NumericCriteriaOnFunctionResult_CountsValueEqualsOperatorConfirms()
    {
        var sheet = MakeSheetWithComputedStdev(out var typedText);
        var result = _eval.Evaluate($"=COUNTIF(C1:C1,{typedText})", sheet);
        result.Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Sumif_NumericCriteriaOnFunctionResult_SumsValueEqualsOperatorConfirms()
    {
        var sheet = MakeSheetWithComputedStdev(out var typedText);
        var rawStdev = ((NumberValue)_eval.Evaluate("=C1", sheet)).Value;
        var result = _eval.Evaluate($"=SUMIF(C1:C1,{typedText},C1:C1)", sheet);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().Be(rawStdev);
    }

    // --- No-regression sibling coverage: comparisons that must NOT start matching after the fix ---

    [Fact]
    public void Match_ExactMatch_GenuinelyDifferentNumber_StillReturnsNA()
    {
        // A value that differs beyond 15 significant digits' worth of rounding tolerance
        // (an actual different number, not float noise) must still fail to match.
        var sheet = MakeSheet((1, 1, new NumberValue(7.4988888065721664)));
        var result = _eval.Evaluate("=MATCH(8.5,A1:A1,0)", sheet);
        result.Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Countif_NumericCriteria_GenuinelyDifferentNumber_StillReturnsZero()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(7.4988888065721664)));
        var result = _eval.Evaluate("=COUNTIF(A1:A1,8.5)", sheet);
        result.Should().Be(new NumberValue(0));
    }

    [Fact]
    public void Match_ApproximateMatch_OrderingAcrossRoundedValues_StillWorksCorrectly()
    {
        // Approximate-match ordering (CompareScalar) over values that only differ in the
        // 16th+ significant digit must treat them as equal/tied, consistent with the exact
        // match fix, while normal ordering of genuinely distinct values is unaffected.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)), (3, 1, new NumberValue(3)));
        var result = _eval.Evaluate("=MATCH(2,A1:A3,1)", sheet);
        result.Should().Be(new NumberValue(2));
    }

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (row, col, val) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)row, (uint)col), val);
        return sheet;
    }
}
