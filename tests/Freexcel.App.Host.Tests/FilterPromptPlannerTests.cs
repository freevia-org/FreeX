using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class FilterPromptPlannerTests
{
    private static readonly SheetId SheetId = SheetId.New();
    private static readonly GridRange Range = new(new CellAddress(SheetId, 1, 1), new CellAddress(SheetId, 5, 3));

    [Theory]
    [InlineData("top:10", FilterPromptPlanKind.TopBottom, true, false)]
    [InlineData("bottompercent:25", FilterPromptPlanKind.TopBottom, false, true)]
    public void TryPlan_ClassifiesTopBottomFilters(string input, FilterPromptPlanKind expectedKind, bool expectedTop, bool expectedPercent)
    {
        FilterPromptPlanner.TryPlan(input, out var plan, out var error).Should().BeTrue();

        error.Should().BeNull();
        plan.Should().NotBeNull();
        plan!.Kind.Should().Be(expectedKind);
        plan.Top.Should().Be(expectedTop);
        plan.Percent.Should().Be(expectedPercent);
        plan.CreateCommand(SheetId, Range, 1).Should().BeAssignableTo<TopBottomFilterCommand>();
    }

    [Theory]
    [InlineData("aboveavg", true)]
    [InlineData("belowaverage", false)]
    public void TryPlan_ClassifiesAverageFilters(string input, bool expectedAbove)
    {
        FilterPromptPlanner.TryPlan(input, out var plan, out var error).Should().BeTrue();

        error.Should().BeNull();
        plan.Should().NotBeNull();
        plan!.Kind.Should().Be(FilterPromptPlanKind.Average);
        plan.AboveAverage.Should().Be(expectedAbove);
        plan.CreateCommand(SheetId, Range, 0).Should().BeOfType<AverageFilterCommand>();
    }

    [Theory]
    [InlineData("blank", typeof(BlankFilterCriterion))]
    [InlineData("contains:East", typeof(TextContainsFilterCriterion))]
    [InlineData("equals:East", typeof(TextEqualsFilterCriterion))]
    [InlineData(">=10", typeof(NumberGreaterThanOrEqualFilterCriterion))]
    public void TryPlan_ClassifiesConditionFilters(string input, Type criterionType)
    {
        FilterPromptPlanner.TryPlan(input, out var plan, out var error).Should().BeTrue();

        error.Should().BeNull();
        plan.Should().NotBeNull();
        plan!.Kind.Should().Be(FilterPromptPlanKind.Condition);
        plan.Criterion.Should().BeOfType(criterionType);
        plan.CreateCommand(SheetId, Range, 0).Should().BeOfType<FilterConditionCommand>();
    }

    [Fact]
    public void TryPlan_ClassifiesCommaSeparatedValuesAsAllowedValuesFilter()
    {
        FilterPromptPlanner.TryPlan("East, West;East", out var plan, out var error).Should().BeTrue();

        error.Should().BeNull();
        plan.Should().NotBeNull();
        plan!.Kind.Should().Be(FilterPromptPlanKind.AllowedValues);
        plan.AllowedValues.Should().Equal("East", "West");
        plan.CreateCommand(SheetId, Range, 0).Should().BeOfType<FilterCommand>();
    }

    [Theory]
    [InlineData("top:0", "Enter a positive item count.")]
    [InlineData("contains:", "Enter text to match.")]
    public void TryPlan_ReturnsParserErrorsForMalformedStructuredFilters(string input, string expectedError)
    {
        FilterPromptPlanner.TryPlan(input, out var plan, out var error).Should().BeFalse();

        plan.Should().BeNull();
        error.Should().Be(expectedError);
    }
}
