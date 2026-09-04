using FluentAssertions;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// r371: chained exponentiation follows Excel, which is LEFT-associative.
///
/// <para>Excel evaluates operators of equal precedence left to right, and <c>^</c> is no exception:
/// <c>=2^3^2</c> is <c>(2^3)^2 = 64</c>. The parser was right-associative -- the mathematical
/// convention, and what most programming languages do -- so it answered 512. Nothing caught it
/// because nothing tested a chained exponent; the parser comment asserted the behaviour and no test
/// verified it.</para>
///
/// <para>This is the failure mode that matters least often and costs most when it happens: the
/// formula does not error, it returns a plausible number that differs from the one Excel shows for
/// the same file.</para>
/// </summary>
public sealed class R371_ExponentiationIsLeftAssociativeTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet() => new Workbook("Assoc").AddSheet("Sheet1");

    [Theory]
    [InlineData("=2^3^2", 64)]      // (2^3)^2, not 2^(3^2) = 512
    [InlineData("=3^2^3", 729)]     // (3^2)^3, not 3^(2^3) = 6561
    [InlineData("=2^2^2^2", 256)]   // (((2^2)^2)^2), not 2^65536
    public void AChainedExponentGroupsLeftToRight(string formula, double expected)
    {
        _eval.Evaluate(formula, MakeSheet())
            .Should().Be(new NumberValue(expected), "Excel groups equal-precedence operators left to right");
    }

    [Theory]
    [InlineData("=-2^2", 4)]        // unary minus binds tighter than ^ in Excel
    [InlineData("=2^-1", 0.5)]      // a unary sign on the right operand still parses
    [InlineData("=2^3", 8)]
    [InlineData("=4^0.5", 2)]
    public void TheSurroundingPrecedenceRulesAreUnchanged(string formula, double expected)
    {
        // The associativity fix must not disturb how ^ binds against unary signs, which Excel treats
        // differently from most languages and which this parser already had right.
        _eval.Evaluate(formula, MakeSheet())
            .Should().Be(new NumberValue(expected));
    }

    [Fact]
    public void ExponentiationStillBindsTighterThanMultiplication()
    {
        _eval.Evaluate("=2*3^2", MakeSheet())
            .Should().Be(new NumberValue(18), "3^2 is evaluated before the multiplication");
    }
}
