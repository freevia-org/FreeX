using FluentAssertions;

using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.ConditionalFormatting;

/// <summary>
/// r564: a conditional-format rule's thresholds are stored as TEXT in the xlsx and parsed with
/// <c>ConditionalFormatEvaluationMath.TryParseInvariant</c>, which uses NumberStyles.Any and so
/// accepts the literal "NaN" and "Infinity". A non-finite threshold does not merely produce a
/// wrong colour -- it inverts the rule:
///
/// <list type="bullet">
/// <item>every ordering comparison against NaN is false, so the rule matches NOTHING;</item>
/// <item><c>NotEqual</c> against NaN is true for every cell, so the rule matches EVERYTHING;</item>
/// <item><c>LessThan Infinity</c> matches every finite cell in the sheet.</item>
/// </list>
///
/// <para>The intended contract is already written down beside this code:
/// <c>MatchesCellValueNumeric_NonNumericThreshold_ReturnsFalse</c> pins that a threshold which is
/// not a number makes the rule not apply. "NaN" is not a number in that sense -- it is a literal
/// .NET happens to parse -- and the same file already carries a <c>SetFinite</c> helper expressing
/// exactly this check, one method below the unguarded parser.</para>
/// </summary>
public sealed class R564_ConditionalFormatThresholdsAreFiniteTests
{
    private static ConditionalFormat CellValueRule(CfOperator op, string v1, string? v2 = null) => new()
    {
        RuleType = CfRuleType.CellValue,
        Operator = op,
        Value1 = v1,
        Value2 = v2,
    };

    // Only the cases where the two behaviours DIFFER. A rule like "Equal NaN" is already false
    // without the fix, because every IEEE comparison against NaN is false -- it would pass either
    // way and prove nothing. Those cases are kept below, in a test named for what they actually
    // show.
    [Theory]
    [InlineData(CfOperator.LessThan, "Infinity")]
    [InlineData(CfOperator.GreaterThan, "-Infinity")]
    [InlineData(CfOperator.LessThan, "1e400")]
    public void A_threshold_that_is_not_a_finite_number_makes_the_rule_not_apply(
        CfOperator op,
        string threshold)
    {
        ConditionalFormatEvaluator
            .MatchesCellValueNumeric(CellValueRule(op, threshold), 5)
            .Should().BeFalse();
    }

    [Fact]
    public void A_not_equal_rule_with_a_nan_threshold_does_not_match_every_cell()
    {
        // The most damaging spelling: NotEqual against NaN is true for EVERY value, so the whole
        // sheet takes the format. Pinned separately because it is the one case whose wrongness is
        // "matches everything" rather than "matches nothing".
        ConditionalFormatEvaluator
            .MatchesCellValueNumeric(CellValueRule(CfOperator.NotEqual, "NaN"), 5)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(CfOperator.NotBetween, "NaN", "10")]
    [InlineData(CfOperator.NotBetween, "1", "NaN")]
    [InlineData(CfOperator.NotBetween, "1", "Infinity")]
    public void A_two_sided_rule_is_not_applied_when_either_bound_is_unusable(
        CfOperator op,
        string v1,
        string v2)
    {
        // Both bounds go through the same parser, and NotBetween is the inverting case again.
        ConditionalFormatEvaluator
            .MatchesCellValueNumeric(CellValueRule(op, v1, v2), 5)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(CfOperator.Equal, "5", null, 5, true)]
    [InlineData(CfOperator.NotEqual, "5", null, 6, true)]
    [InlineData(CfOperator.GreaterThan, "5", null, 6, true)]
    [InlineData(CfOperator.LessThan, "5", null, 4, true)]
    [InlineData(CfOperator.Between, "2", "8", 5, true)]
    [InlineData(CfOperator.NotBetween, "2", "8", 9, true)]
    [InlineData(CfOperator.Between, "2", "8", 9, false)]
    public void Ordinary_thresholds_still_decide_the_rule(
        CfOperator op,
        string v1,
        string? v2,
        double value,
        bool expected)
    {
        // Non-vacuity across every operator the fix touches: a guard that rejected all thresholds
        // would satisfy the assertions above while silently disabling conditional formatting.
        ConditionalFormatEvaluator
            .MatchesCellValueNumeric(CellValueRule(op, v1, v2), value)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(CfOperator.Equal, "NaN")]
    [InlineData(CfOperator.GreaterThan, "NaN")]
    [InlineData(CfOperator.Equal, "Infinity")]
    [InlineData(CfOperator.Between, "NaN", "10")]
    public void These_spellings_were_already_false_for_an_unrelated_reason(
        CfOperator op,
        string v1,
        string? v2 = null)
    {
        // Recorded rather than claimed as evidence: these answer false WITHOUT the fix too, because
        // every IEEE comparison against NaN is false and 5 is neither equal to nor greater than
        // Infinity. They document the intended contract and would catch a future change that made
        // them true, but they do NOT demonstrate this round's fix -- the theory above does.
        ConditionalFormatEvaluator
            .MatchesCellValueNumeric(CellValueRule(op, v1, v2), 5)
            .Should().BeFalse();
    }
}
