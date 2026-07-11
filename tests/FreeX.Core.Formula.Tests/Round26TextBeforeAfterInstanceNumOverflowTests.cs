using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R26-text-functions-modern-deep-3: TEXTBEFORE/TEXTAFTER returned #NUM! instead of #VALUE!
/// for instance_num == -2147483648 (Int32.MinValue). TryGetOptionalInteger's range check
/// (`number > int.MaxValue || number < int.MinValue`) does not reject number == int.MinValue
/// exactly, so options.InstanceNum could become Int32.MinValue. TextBeforeAfterScalar then
/// called Math.Abs(options.InstanceNum), and .NET's Math.Abs(int) unconditionally throws
/// OverflowException for Int32.MinValue (its positive counterpart isn't representable as an
/// int). The generic function-dispatch catch in FormulaEvaluator.Functions.cs converts any
/// OverflowException to #NUM!, so the formula silently returned the wrong error code -- every
/// other instance_num domain violation in this function (out-of-range magnitude, zero, etc.)
/// returns #VALUE!, matching real Excel. Fixed in BuiltInFunctions.TextSplit.cs by widening the
/// Math.Abs comparison to long arithmetic so it can never overflow, regardless of how
/// InstanceNum reached Int32.MinValue.
/// </summary>
public sealed class Round26TextBeforeAfterInstanceNumOverflowTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet Sheet() => new(SheetId.New(), "S");

    [Theory]
    [InlineData("=TEXTBEFORE(\"abc\",\"b\",-2147483648)")]
    [InlineData("=TEXTAFTER(\"abc\",\"b\",-2147483648)")]
    public void TextBeforeAfter_InstanceNumIntMinValue_ReturnsValueErrorNotNum(string formula)
    {
        // Bug case: instance_num = Int32.MinValue used to throw OverflowException from
        // Math.Abs(int), which the generic catch turned into #NUM! instead of #VALUE!.
        _eval.Evaluate(formula, Sheet()).Should().Be(ErrorValue.Value);
    }

    [Theory]
    [InlineData("=TEXTBEFORE(\"alpha\",\"a\",6)")]
    [InlineData("=TEXTAFTER(\"alpha\",\"a\",6)")]
    [InlineData("=TEXTBEFORE(\"alpha\",\"a\",0)")]
    [InlineData("=TEXTAFTER(\"alpha\",\"a\",0)")]
    public void TextBeforeAfter_OtherInstanceNumDomainErrors_StillReturnValue_NoRegression(string formula)
    {
        // Sibling already-working domain-error cases (out-of-range magnitude, zero) must be
        // unaffected by widening the overflow-safe bounds check to long arithmetic.
        _eval.Evaluate(formula, Sheet()).Should().Be(ErrorValue.Value);
    }

    [Theory]
    [InlineData("=TEXTBEFORE(\"Little red Riding Hood's red hood\",\"red\",-2)", "Little ")]
    [InlineData("=TEXTAFTER(\"Little red Riding Hood's red hood\",\"red\",2)", " hood")]
    public void TextBeforeAfter_OrdinaryInstanceNum_StillWorks_NoRegression(string formula, string expected)
    {
        // Ordinary (small, in-range) instance_num values must keep working exactly as before.
        _eval.Evaluate(formula, Sheet()).Should().Be(new TextValue(expected));
    }
}
