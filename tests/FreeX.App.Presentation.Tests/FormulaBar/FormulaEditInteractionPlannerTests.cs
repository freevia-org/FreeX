using FluentAssertions;
using Free.Shared.AppServices;

namespace FreeX.App.Presentation.Tests.FormulaBar;

public sealed class FormulaEditInteractionPlannerTests
{
    [Theory]
    [InlineData(FormulaEditStatusBarMode.Enter, StatusBarTextResourceKeys.EnterMode)]
    [InlineData(FormulaEditStatusBarMode.Edit, StatusBarTextResourceKeys.EditMode)]
    [InlineData(FormulaEditStatusBarMode.Point, StatusBarTextResourceKeys.PointMode)]
    public void BuildStatusBarPlan_MapsFormulaEditModesToSharedStatusText(
        FormulaEditStatusBarMode mode,
        string expectedResourceKey)
    {
        var plan = FormulaEditInteractionPlanner.BuildStatusBarPlan(mode);

        plan.Mode.Should().Be(mode);
        plan.ResourceKey.Should().Be(expectedResourceKey);
    }

    [Theory]
    [InlineData(false, FormulaEditStatusBarMode.Edit, StatusBarTextResourceKeys.EditMode)]
    [InlineData(true, FormulaEditStatusBarMode.Point, StatusBarTextResourceKeys.PointMode)]
    public void BuildEditStatusBarPlan_UsesPointModeWhenRangeEntryIsActive(
        bool pointMode,
        FormulaEditStatusBarMode expectedMode,
        string expectedResourceKey)
    {
        var plan = FormulaEditInteractionPlanner.BuildEditStatusBarPlan(pointMode);

        plan.Mode.Should().Be(expectedMode);
        plan.ResourceKey.Should().Be(expectedResourceKey);
    }

    [Fact]
    public void BuildEnterStatusBarPlan_UsesSharedEnterModeText()
    {
        var plan = FormulaEditInteractionPlanner.BuildEnterStatusBarPlan();

        plan.Mode.Should().Be(FormulaEditStatusBarMode.Enter);
        plan.ResourceKey.Should().Be(StatusBarTextResourceKeys.EnterMode);
    }

    [Theory]
    [InlineData("=", true)]
    [InlineData("=SUM(", false)]
    [InlineData("abc", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void BuildTextChangePlan_OnlyRaisesEnterStatusWhenFormulaEntryStarts(
        string? text,
        bool expectedStartsPointMode)
    {
        var plan = FormulaEditInteractionPlanner.BuildTextChangePlan(text);

        plan.StartsPointMode.Should().Be(expectedStartsPointMode);
        if (expectedStartsPointMode)
        {
            plan.StatusBarPlan.Should().NotBeNull();
            plan.StatusBarPlan!.Value.Mode.Should().Be(FormulaEditStatusBarMode.Enter);
            plan.StatusBarPlan.Value.ResourceKey.Should().Be(StatusBarTextResourceKeys.EnterMode);
        }
        else
        {
            plan.StatusBarPlan.Should().BeNull();
        }
    }

    [Theory]
    [InlineData("=", true)]
    [InlineData("abc", false)]
    [InlineData("", false)]
    public void BuildTypedEntryPlan_AlwaysUsesEnterStatusButOnlyEqualsStartsPointMode(
        string text,
        bool expectedPointMode)
    {
        var plan = FormulaEditInteractionPlanner.BuildTypedEntryPlan(text);

        plan.PointMode.Should().Be(expectedPointMode);
        plan.StatusBarPlan.Mode.Should().Be(FormulaEditStatusBarMode.Enter);
        plan.StatusBarPlan.ResourceKey.Should().Be(StatusBarTextResourceKeys.EnterMode);
    }

    [Theory]
    [InlineData("=A1", false, true, false, true, FormulaEditStatusBarMode.Point, StatusBarTextResourceKeys.PointMode)]
    [InlineData("=A1", true, false, true, true, FormulaEditStatusBarMode.Edit, StatusBarTextResourceKeys.EditMode)]
    [InlineData("abc", false, false, true, false, FormulaEditStatusBarMode.Edit, StatusBarTextResourceKeys.EditMode)]
    public void BuildPointModeTogglePlan_PlansModeStateAndStatusText(
        string text,
        bool currentPointMode,
        bool expectedPointMode,
        bool expectedClearReferenceSpan,
        bool expectedHandled,
        FormulaEditStatusBarMode expectedMode,
        string expectedResourceKey)
    {
        var plan = FormulaEditInteractionPlanner.BuildPointModeTogglePlan(text, currentPointMode);

        plan.PointMode.Should().Be(expectedPointMode);
        plan.ClearReferenceSpan.Should().Be(expectedClearReferenceSpan);
        plan.Handled.Should().Be(expectedHandled);
        plan.StatusBarPlan.Mode.Should().Be(expectedMode);
        plan.StatusBarPlan.ResourceKey.Should().Be(expectedResourceKey);
    }
}
