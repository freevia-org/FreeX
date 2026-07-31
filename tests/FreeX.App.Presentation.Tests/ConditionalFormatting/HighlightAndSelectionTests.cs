using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.ConditionalFormatting;

public sealed class HighlightAndSelectionTests
{
    private static ConditionalFormat CellValueRule(CfOperator op, string v1, string? v2 = null) => new()
    {
        RuleType = CfRuleType.CellValue,
        Operator = op,
        Value1 = v1,
        Value2 = v2,
    };

    [Theory]
    [InlineData(CfOperator.Equal, "5", null, 5, true)]
    [InlineData(CfOperator.Equal, "5", null, 6, false)]
    [InlineData(CfOperator.NotEqual, "5", null, 6, true)]
    [InlineData(CfOperator.GreaterThan, "5", null, 6, true)]
    [InlineData(CfOperator.GreaterThan, "5", null, 5, false)]
    [InlineData(CfOperator.GreaterThanOrEqual, "5", null, 5, true)]
    [InlineData(CfOperator.LessThan, "5", null, 4, true)]
    [InlineData(CfOperator.LessThanOrEqual, "5", null, 5, true)]
    [InlineData(CfOperator.Between, "2", "8", 5, true)]
    [InlineData(CfOperator.Between, "2", "8", 9, false)]
    [InlineData(CfOperator.Between, "2", "8", 2, true)]
    [InlineData(CfOperator.NotBetween, "2", "8", 9, true)]
    [InlineData(CfOperator.NotBetween, "2", "8", 5, false)]
    public void MatchesCellValueNumeric_ComparisonOperators(CfOperator op, string v1, string? v2, double value, bool expected)
    {
        ConditionalFormatEvaluator.MatchesCellValueNumeric(CellValueRule(op, v1, v2), value).Should().Be(expected);
    }

    [Fact]
    public void MatchesCellValueNumeric_NonNumericThreshold_ReturnsFalse()
    {
        ConditionalFormatEvaluator.MatchesCellValueNumeric(CellValueRule(CfOperator.GreaterThan, "abc"), 10).Should().BeFalse();
    }

    [Fact]
    public void AboveAverage_SelectsValuesAboveMean()
    {
        var rule = new ConditionalFormat { RuleType = CfRuleType.AboveAverage, AboveAverage = true };
        var stats = ConditionalFormatStatistics.FromValues([1, 2, 3, 4, 5]); // avg = 3

        ConditionalFormatEvaluator.MatchesAboveBelowAverage(rule, 4, stats).Should().BeTrue();
        ConditionalFormatEvaluator.MatchesAboveBelowAverage(rule, 3, stats).Should().BeFalse(); // strictly above
        ConditionalFormatEvaluator.MatchesAboveBelowAverage(rule, 2, stats).Should().BeFalse();
    }

    [Fact]
    public void BelowAverage_SelectsValuesBelowMean()
    {
        var rule = new ConditionalFormat { RuleType = CfRuleType.AboveAverage, AboveAverage = false };
        var stats = ConditionalFormatStatistics.FromValues([1, 2, 3, 4, 5]); // avg = 3

        ConditionalFormatEvaluator.MatchesAboveBelowAverage(rule, 2, stats).Should().BeTrue();
        ConditionalFormatEvaluator.MatchesAboveBelowAverage(rule, 3, stats).Should().BeFalse();
    }

    [Fact]
    public void AboveAverage_EmptyRange_ReturnsFalse()
    {
        var rule = new ConditionalFormat { RuleType = CfRuleType.AboveAverage, AboveAverage = true };
        var stats = ConditionalFormatStatistics.FromValues([]);

        ConditionalFormatEvaluator.MatchesAboveBelowAverage(rule, 5, stats).Should().BeFalse();
    }

    // R110_: print/PDF's portable evaluator must honor Excel's "Equal or Above/Below Average" and
    // "N standard deviations above/below average" AboveAverage variants exactly like the on-screen
    // grid engine (ViewportConditionalFormatEvaluator.MatchesAboveAverage), not just the plain
    // average comparison.

    [Fact]
    public void R110_EqualAverage_IncludesValuesEqualToTheMean()
    {
        var rule = new ConditionalFormat { RuleType = CfRuleType.AboveAverage, AboveAverage = true, EqualAverage = true };
        var stats = ConditionalFormatStatistics.FromValues([1, 2, 3, 4, 5]); // avg = 3

        // Plain (non-equal) AboveAverage excludes the mean itself -- EqualAverage must include it.
        ConditionalFormatEvaluator.MatchesAboveBelowAverage(rule, 3, stats).Should().BeTrue();
        ConditionalFormatEvaluator.MatchesAboveBelowAverage(rule, 2, stats).Should().BeFalse();
    }

    [Fact]
    public void R110_EqualAverage_BelowVariant_IncludesValuesEqualToTheMean()
    {
        var rule = new ConditionalFormat { RuleType = CfRuleType.AboveAverage, AboveAverage = false, EqualAverage = true };
        var stats = ConditionalFormatStatistics.FromValues([1, 2, 3, 4, 5]); // avg = 3

        ConditionalFormatEvaluator.MatchesAboveBelowAverage(rule, 3, stats).Should().BeTrue();
        ConditionalFormatEvaluator.MatchesAboveBelowAverage(rule, 4, stats).Should().BeFalse();
    }

    [Fact]
    public void R110_StdDevCount_ShiftsThresholdByNStandardDeviationsAboveMean()
    {
        // [2, 4, 4, 4, 5, 5, 7, 9] has mean 5, sample stdDev 2.138 (STDEV semantics).
        var values = new double[] { 2, 4, 4, 4, 5, 5, 7, 9 };
        var stats = ConditionalFormatStatistics.FromValues(values);
        stats.StdDev.Should().BeApproximately(2.13809, 0.0001);

        var rule = new ConditionalFormat { RuleType = CfRuleType.AboveAverage, AboveAverage = true, StdDevCount = 1 };
        // threshold = 5 + 1*2.13809 = 7.138 -- 9 clears it, 7 does not (plain AboveAverage(>5)
        // would wrongly select 7 too).
        ConditionalFormatEvaluator.MatchesAboveBelowAverage(rule, 9, stats).Should().BeTrue();
        ConditionalFormatEvaluator.MatchesAboveBelowAverage(rule, 7, stats).Should().BeFalse();
    }

    [Fact]
    public void R110_StdDevCount_ShiftsThresholdByNStandardDeviationsBelowMean()
    {
        var values = new double[] { 2, 4, 4, 4, 5, 5, 7, 9 };
        var stats = ConditionalFormatStatistics.FromValues(values);

        var rule = new ConditionalFormat { RuleType = CfRuleType.AboveAverage, AboveAverage = false, StdDevCount = 1 };
        // threshold = 5 - 2.138 = 2.862 -- 2 clears it, 4 does not (plain BelowAverage(<5)
        // would wrongly select 4 too).
        ConditionalFormatEvaluator.MatchesAboveBelowAverage(rule, 2, stats).Should().BeTrue();
        ConditionalFormatEvaluator.MatchesAboveBelowAverage(rule, 4, stats).Should().BeFalse();
    }

    [Fact]
    public void R110_StdDevCount_SinglePointRange_FallsBackToPlainAverageComparison()
    {
        // Fewer than 2 numeric points => no variance => stdDev is 0 => threshold collapses to the
        // plain average, matching the engine's fallback for an unavailable stdDev.
        var stats = ConditionalFormatStatistics.FromValues([5]);
        stats.StdDev.Should().Be(0);

        var rule = new ConditionalFormat { RuleType = CfRuleType.AboveAverage, AboveAverage = true, StdDevCount = 2 };
        ConditionalFormatEvaluator.MatchesAboveBelowAverage(rule, 6, stats).Should().BeTrue();
        ConditionalFormatEvaluator.MatchesAboveBelowAverage(rule, 5, stats).Should().BeFalse();
    }
}
