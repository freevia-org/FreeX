using FluentAssertions;
using Freexcel.App.Host;
using Xunit;

namespace Freexcel.App.Host.Tests;

public sealed class FormulaEditInteractionPlannerTests
{
    [Fact]
    public void IsRangeEntryActive_RequiresFormulaTextAndPointMode()
    {
        FormulaEditInteractionPlanner.IsRangeEntryActive("=SUM(A1:A2)", pointMode: false)
            .Should().BeFalse();

        FormulaEditInteractionPlanner.IsRangeEntryActive("=SUM(", pointMode: true)
            .Should().BeTrue();
    }

    [Fact]
    public void ShouldCommitInlineArrows_CommitsOnlyNonFormulaText()
    {
        FormulaEditInteractionPlanner.ShouldCommitInlineArrows("abc", pointMode: false)
            .Should().BeTrue();

        FormulaEditInteractionPlanner.ShouldCommitInlineArrows("=SUM(A1:A2)", pointMode: false)
            .Should().BeFalse();

        FormulaEditInteractionPlanner.ShouldCommitInlineArrows("=SUM(", pointMode: true)
            .Should().BeFalse();
    }

    [Fact]
    public void TogglePointMode_TogglesOnlyFormulaText()
    {
        FormulaEditInteractionPlanner.TogglePointMode("=A1", pointMode: false).Should().BeTrue();
        FormulaEditInteractionPlanner.TogglePointMode("=A1", pointMode: true).Should().BeFalse();
        FormulaEditInteractionPlanner.TogglePointMode("abc", pointMode: false).Should().BeFalse();
    }

    [Fact]
    public void ShouldStartPointModeFromTypedText_StartsOnlyForNewFormulaEntry()
    {
        FormulaEditInteractionPlanner.ShouldStartPointModeFromTypedText("=").Should().BeTrue();
        FormulaEditInteractionPlanner.ShouldStartPointModeFromTypedText("=SUM(").Should().BeFalse();
        FormulaEditInteractionPlanner.ShouldStartPointModeFromTypedText("text").Should().BeFalse();
    }
}
