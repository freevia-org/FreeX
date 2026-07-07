using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression coverage for round-11 bucket R3 finding R11-formula-functions-1: ODD/EVEN must
/// match Excel for magnitudes above int.MaxValue (Excel supports finite values up to ~9.9e307).
/// Before the fix, OddScalar/EvenScalar narrowed the ceiling-rounded magnitude to an int and
/// rejected anything above int.MaxValue (~2.147e9) with a spurious #NUM! error.
/// </summary>
public class FreeXR11B3Tests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet() => new(SheetId.New(), "S");

    [Fact]
    public void Odd_LargeMagnitudeAboveIntMaxValue_RoundsUpToNextOddInteger()
    {
        // 3,000,000,000 is even and comfortably above int.MaxValue (~2.147e9); Excel's ODD
        // rounds it up (away from zero) to the next odd integer: 3,000,000,001.
        _eval.Evaluate("=ODD(3000000000)", MakeSheet()).Should().Be(new NumberValue(3000000001));
    }

    [Fact]
    public void Even_LargeMagnitudeAboveIntMaxValue_StaysAtSameEvenInteger()
    {
        // 3,000,000,000 is already even, so Excel's EVEN returns it unchanged even though it
        // exceeds int.MaxValue.
        _eval.Evaluate("=EVEN(3000000000)", MakeSheet()).Should().Be(new NumberValue(3000000000));
    }
}
