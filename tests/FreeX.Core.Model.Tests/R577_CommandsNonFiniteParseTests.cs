using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression tests for round 577's three findings in FreeX.Core.Commands, all of the same class:
/// <c>double.TryParse</c> returns <c>true</c> with +/-Infinity on magnitude overflow (it has not
/// thrown since .NET Core) and accepts the literal "NaN"/"Infinity" spellings under
/// <c>NumberStyles.Float</c>.
/// </summary>
public sealed class R577_CommandsNonFiniteParseTests
{
    // ---- CalculationOptionsInputParser.TryParseMaxChange -------------------------------------
    // The guard was the REJECT form "parsed < 0", which rejects NEITHER Infinity nor NaN (every
    // comparison with NaN is false) -- r551's shape. This is the iterative-calculation convergence
    // threshold: Infinity declares every iteration converged, NaN declares none converged.

    [Theory]
    [InlineData("Infinity")]
    [InlineData("NaN")]
    [InlineData("1e400")]
    public void MaxChange_NonFiniteText_IsRejected(string text)
        => CalculationOptionsInputParser.TryParseMaxChange(text, out var value)
            .Should().BeFalse($"\"{text}\" parsed to {value}");

    [Theory]
    [InlineData("0.001", 0.001)]
    [InlineData("0", 0)]
    [InlineData("1000", 1000)]
    public void MaxChange_FiniteText_IsStillAccepted(string text, double expected)
    {
        // Non-vacuity: the finite test must not have closed the box to ordinary thresholds.
        CalculationOptionsInputParser.TryParseMaxChange(text, out var value).Should().BeTrue();
        value.Should().Be(expected);
    }

    [Fact]
    public void MaxChange_NegativeText_IsStillRejected()
        => CalculationOptionsInputParser.TryParseMaxChange("-1", out _).Should().BeFalse();

    // ---- PivotCalculatedExpressionEvaluator.ReadNumber ----------------------------------------
    // Its scanner admits only digits and '.', so no exponent or "NaN" spelling reaches the parse --
    // but a long enough run of digits overflows double all the same. A calculated field's result
    // becomes a cell value, and a cell value may not be non-finite (r562, r563, r569, r576).

    [Fact]
    public void CalculatedField_OverflowingLiteral_DoesNotEvaluateToInfinity()
    {
        var result = PivotCalculatedExpressionEvaluator.Evaluate(
            new string('9', 400), _ => 0);

        double.IsFinite(result).Should().BeTrue($"the literal evaluated to {result}");
    }

    [Fact]
    public void CalculatedField_OrdinaryLiteral_StillEvaluates()
    {
        // Non-vacuity: the guard must not have flattened every literal to the 0 fallback.
        PivotCalculatedExpressionEvaluator.Evaluate("2.5+1", _ => 0).Should().Be(3.5);
    }

    // ---- PivotTableRefreshService.ParseSharedItemScalarValue -----------------------------------
    // raw is a pivot-cache shared item read straight out of the file, so this line could mint a
    // NumberValue holding a value that is not a number.

    private static readonly PivotCacheFieldModel NumericField =
        new("F", ContainsNumber: true);

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e400")]
    public void SharedItem_NonFiniteRaw_StaysText(string raw)
    {
        var value = PivotTableRefreshService.ParseSharedItemScalarValue(raw, 'n', NumericField);

        value.Should().BeOfType<TextValue>(
            "an item that is not a number must not become a NumberValue");
        if (value is NumberValue number)
            double.IsFinite(number.Value).Should().BeTrue($"\"{raw}\" produced {number.Value}");
    }

    [Fact]
    public void SharedItem_FiniteRaw_StillBecomesANumber()
    {
        // Non-vacuity: the guard must not have turned every numeric shared item into text.
        PivotTableRefreshService.ParseSharedItemScalarValue("1234.5", 'n', NumericField)
            .Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(1234.5);
    }

    // ---- FilterCriterionInputParser.TryParseThreshold -----------------------------------------
    // A non-finite threshold makes every ordering comparison false, so the filter would hide every
    // row while reporting no error at all.

    [Theory]
    [InlineData("> NaN")]
    [InlineData("> 1e400")]
    [InlineData(">= Infinity")]
    public void FilterCriterion_NonFiniteThreshold_IsAnError(string input)
    {
        FilterCriterionInputParser.TryParseCriterion(input, out var criterion, out var error)
            .Should().BeFalse($"\"{input}\" is not a usable numeric threshold");
        criterion.Should().BeNull();
        error.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("> 5")]
    [InlineData(">= 0")]
    [InlineData("< -2.5")]
    public void FilterCriterion_FiniteThreshold_StillParses(string input)
    {
        // Non-vacuity: the finite test must not have rejected ordinary comparison criteria.
        FilterCriterionInputParser.TryParseCriterion(input, out var criterion, out var error)
            .Should().BeTrue(error);
        criterion.Should().NotBeNull();
    }

    // ---- PersistedCustomFilterCriterion.Matches -----------------------------------------------
    // Value is a persisted customFilters/@val read out of the FILE. A NaN threshold makes every
    // ordering comparison false (hiding every row) and makes notEqual true for every row --
    // r564's conditional-format threshold defect, in the autofilter.

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void PersistedCustomFilter_NonFiniteValue_FallsBackToTextComparison(string value)
    {
        // With the criterion treated as text, a numeric cell simply does not match it; before the
        // fix "notEqual NaN" matched EVERY cell instead.
        new PersistedCustomFilterCriterion("notEqual", value)
            .Matches(new NumberValue(42))
            .Should().BeTrue("42 is genuinely not the text \"" + value + "\"");

        new PersistedCustomFilterCriterion("greaterThan", value)
            .Matches(new NumberValue(42))
            .Should().BeFalse();
    }

    [Fact]
    public void PersistedCustomFilter_FiniteValue_StillComparesNumerically()
    {
        // Non-vacuity: the guard must not have stopped numeric criteria comparing as numbers.
        new PersistedCustomFilterCriterion("greaterThan", "10")
            .Matches(new NumberValue(42)).Should().BeTrue();
        new PersistedCustomFilterCriterion("greaterThan", "100")
            .Matches(new NumberValue(42)).Should().BeFalse();
    }
}
