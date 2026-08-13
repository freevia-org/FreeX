using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R75-formula-precision-4-2: a numeric literal parsed by Parser.cs's NumberNode construction
/// sites was never capped to Excel's 15-significant-digit storage precision, so a literal over 15
/// significant digits (e.g. 1234567890123456, 16 digits) stayed as the raw 16-digit double.Parse
/// result instead of Excel's truncated-to-15-sig-digit storage value (1234567890123450). Fixed by
/// capping every NumberNode literal via <see cref="ExcelNumericPrecision"/> (Excel truncates --
/// zeroes -- excess low-order integer digits unconditionally, it does not round them).
/// </summary>
public sealed class R75_NumericLiteralPrecisionCapTests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void SixteenDigitLiteral_IsTruncatedToFifteenSignificantDigits()
    {
        var (workbook, sheet) = MakeSheet();

        _eval.Evaluate("=1234567890123456", sheet, workbook)
            .Should().Be(new NumberValue(1234567890123450d));
    }

    [Fact]
    public void SixteenDigitLiteralMinusItsTruncatedForm_IsExactlyZero()
    {
        var (workbook, sheet) = MakeSheet();

        _eval.Evaluate("=1234567890123456-1234567890123450", sheet, workbook)
            .Should().Be(new NumberValue(0));
    }

    [Fact]
    public void FifteenDigitLiteral_IsUnaffectedByTheCap()
    {
        var (workbook, sheet) = MakeSheet();

        _eval.Evaluate("=123456789012345", sheet, workbook)
            .Should().Be(new NumberValue(123456789012345d));
    }

    [Fact]
    public void DecimalLiteral_IsUnaffectedByTheCap()
    {
        var (workbook, sheet) = MakeSheet();

        _eval.Evaluate("=0.1", sheet, workbook).Should().Be(new NumberValue(0.1));
    }

    [Fact]
    public void TinyLiteral_RemainsFiniteAndNonzero()
    {
        var (workbook, sheet) = MakeSheet();

        _eval.Evaluate("=5E-200", sheet, workbook).Should().Be(new NumberValue(5e-200));
    }

    [Fact]
    public void SmallIntegerLiteral_IsUnaffectedByTheCap()
    {
        var (workbook, sheet) = MakeSheet();

        _eval.Evaluate("=2", sheet, workbook).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void SixteenDigitArrayConstantElement_IsAlsoTruncated()
    {
        var (workbook, sheet) = MakeSheet();

        // Covers the second NumberNode construction site (ParseArrayConstantElement), which is
        // reached for a positive literal inside an array constant.
        _eval.Evaluate("={1234567890123456,2}", sheet, workbook)
            .Should().BeOfType<RangeValue>()
            .Which.Cells[0, 0].Should().Be(new NumberValue(1234567890123450d));
    }

    private static (Workbook workbook, Sheet sheet) MakeSheet()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet);
    }
}
