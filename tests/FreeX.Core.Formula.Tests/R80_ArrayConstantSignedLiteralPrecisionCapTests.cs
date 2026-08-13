using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R80-formula-array-cse-5-3: a SIGNED numeric literal inside an array constant (e.g.
/// {-123456789012345678,1} or {+123456789012345678,1}) bypassed Excel's 15-significant-digit
/// literal storage cap. ParseArrayConstantElement's unsigned TokenType.Number branch correctly
/// wraps the parsed value in ExcelNumericPrecision.CapSignificantDigits (see R75_NumericLiteralPrecisionCapTests
/// .SixteenDigitArrayConstantElement_IsAlsoTruncated), but the TokenType.Plus/Minus branch
/// delegated to ParseSignedArrayConstantNumber, which did a bare double.Parse + negation with no
/// capping at all. Fixed by capping the unsigned magnitude via ExcelNumericPrecision before
/// negating, matching the unsigned array-constant path and the ordinary-literal path (Parser.cs's
/// plain TokenType.Number ParsePrimary case, which gets its negative form via a UnaryOpNode
/// wrapping an already-capped NumberNode).
/// </summary>
public sealed class R80_ArrayConstantSignedLiteralPrecisionCapTests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void NegativeEighteenDigitArrayConstantElement_IsTruncatedToFifteenSignificantDigits()
    {
        var (workbook, sheet) = MakeSheet();

        _eval.Evaluate("=SUM({-123456789012345678,1})", sheet, workbook)
            .Should().Be(new NumberValue(-123456789012345000d + 1));
    }

    [Fact]
    public void NegativeEighteenDigitArrayConstantElement_MatchesUnsignedMagnitudeCap()
    {
        var (workbook, sheet) = MakeSheet();

        // The negative array-constant element must be capped to exactly the negation of what the
        // unsigned magnitude caps to (i.e. the same 15-sig-digit truncation, just sign-flipped) --
        // not left at full IEEE-754 double precision.
        _eval.Evaluate("={-123456789012345678,1}", sheet, workbook)
            .Should().BeOfType<RangeValue>()
            .Which.Cells[0, 0].Should().Be(new NumberValue(-123456789012345000d));
    }

    [Fact]
    public void ExplicitlyPositiveArrayConstantElement_IsAlsoTruncated()
    {
        var (workbook, sheet) = MakeSheet();

        // Covers the TokenType.Plus branch of ParseSignedArrayConstantNumber, not just Minus.
        _eval.Evaluate("={+123456789012345678,1}", sheet, workbook)
            .Should().BeOfType<RangeValue>()
            .Which.Cells[0, 0].Should().Be(new NumberValue(123456789012345000d));
    }

    [Fact]
    public void SignedSmallIntegerArrayConstantElement_IsUnaffectedByTheCap()
    {
        var (workbook, sheet) = MakeSheet();

        // No-regression sibling: a short signed literal inside an array constant (well under the
        // 15-significant-digit cap) must still parse to its exact negated value.
        _eval.Evaluate("={-4,3}", sheet, workbook)
            .Should().BeOfType<RangeValue>()
            .Which.Cells[0, 0].Should().Be(new NumberValue(-4));
    }

    [Fact]
    public void MultiRowArrayConstantWithMixedSignedElements_ParsesToCorrectTwoByTwoArray()
    {
        var (workbook, sheet) = MakeSheet();

        // Matches the fix guidance's own example: {-1,2;3,-4} must parse to the correct 2x2 array.
        var result = _eval.Evaluate("={-1,2;3,-4}", sheet, workbook)
            .Should().BeOfType<RangeValue>().Subject;

        result.Cells[0, 0].Should().Be(new NumberValue(-1));
        result.Cells[0, 1].Should().Be(new NumberValue(2));
        result.Cells[1, 0].Should().Be(new NumberValue(3));
        result.Cells[1, 1].Should().Be(new NumberValue(-4));
    }

    private static (Workbook workbook, Sheet sheet) MakeSheet()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet);
    }
}
