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
}
