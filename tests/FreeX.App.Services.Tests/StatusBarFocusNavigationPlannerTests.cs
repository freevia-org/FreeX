using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Services.Tests;

public sealed class StatusBarFocusNavigationPlannerTests
{
    [Fact]
    public void FocusOrder_MatchesExcelStatusBarKeyboardSequence()
    {
        StatusBarFocusNavigationPlanner.FocusOrder.Should().Equal(
            StatusBarFocusTarget.ZoomOutButton,
            StatusBarFocusTarget.ZoomSlider,
            StatusBarFocusTarget.ZoomInButton,
            StatusBarFocusTarget.ZoomText,
            StatusBarFocusTarget.NormalViewButton,
            StatusBarFocusTarget.PageLayoutViewButton,
            StatusBarFocusTarget.PageBreakPreviewButton);
    }

    [Fact]
    public void BuildInitialFocusOrder_StartsAtZoomOutAndSkipsUnavailableControls()
    {
        var candidates = Candidates(
            available: [
                StatusBarFocusTarget.ZoomSlider,
                StatusBarFocusTarget.ZoomInButton,
                StatusBarFocusTarget.NormalViewButton
            ]);

        var order = StatusBarFocusNavigationPlanner.BuildInitialFocusOrder(candidates);

        order.Should().Equal(
            StatusBarFocusTarget.ZoomSlider,
            StatusBarFocusTarget.ZoomInButton,
            StatusBarFocusTarget.NormalViewButton);
    }

    [Theory]
    [InlineData(StatusBarFocusTarget.ZoomOutButton, false, StatusBarFocusTarget.ZoomSlider)]
    [InlineData(StatusBarFocusTarget.ZoomSlider, false, StatusBarFocusTarget.ZoomInButton)]
    [InlineData(StatusBarFocusTarget.PageBreakPreviewButton, false, StatusBarFocusTarget.ZoomOutButton)]
    [InlineData(StatusBarFocusTarget.ZoomOutButton, true, StatusBarFocusTarget.PageBreakPreviewButton)]
    [InlineData(StatusBarFocusTarget.ZoomText, true, StatusBarFocusTarget.ZoomInButton)]
    public void BuildKeyboardNavigationPlan_TabMovesThroughAvailableStatusTargets(
        StatusBarFocusTarget current,
        bool reverse,
        StatusBarFocusTarget expected)
    {
        var plan = StatusBarFocusNavigationPlanner.BuildKeyboardNavigationPlan(
            StatusBarKeyboardNavigationKey.Tab,
            reverse,
            current,
            AllCandidates());

        plan.Action.Should().Be(StatusBarKeyboardNavigationAction.MoveFocus);
        plan.Target.Should().Be(expected);
    }

    [Fact]
    public void BuildKeyboardNavigationPlan_TabSkipsUnavailableControls()
    {
        var plan = StatusBarFocusNavigationPlanner.BuildKeyboardNavigationPlan(
            StatusBarKeyboardNavigationKey.Tab,
            reverse: false,
            StatusBarFocusTarget.ZoomOutButton,
            Candidates(available: [
                StatusBarFocusTarget.ZoomOutButton,
                StatusBarFocusTarget.ZoomText,
                StatusBarFocusTarget.PageBreakPreviewButton
            ]));

        plan.Should().Be(new StatusBarKeyboardNavigationPlan(
            StatusBarKeyboardNavigationAction.MoveFocus,
            StatusBarFocusTarget.ZoomText));
    }

    [Fact]
    public void BuildKeyboardNavigationPlan_TabFromUnknownCurrentStartsAtFirstAvailableTarget()
    {
        var plan = StatusBarFocusNavigationPlanner.BuildKeyboardNavigationPlan(
            StatusBarKeyboardNavigationKey.Tab,
            reverse: true,
            currentTarget: null,
            Candidates(available: [
                StatusBarFocusTarget.ZoomSlider,
                StatusBarFocusTarget.PageLayoutViewButton
            ]));

        plan.Should().Be(new StatusBarKeyboardNavigationPlan(
            StatusBarKeyboardNavigationAction.MoveFocus,
            StatusBarFocusTarget.ZoomSlider));
    }

    [Fact]
    public void BuildKeyboardNavigationPlan_EscapeReturnsWorksheetFocusAction()
    {
        var plan = StatusBarFocusNavigationPlanner.BuildKeyboardNavigationPlan(
            StatusBarKeyboardNavigationKey.Escape,
            reverse: false,
            StatusBarFocusTarget.ZoomSlider,
            AllCandidates());

        plan.Should().Be(new StatusBarKeyboardNavigationPlan(
            StatusBarKeyboardNavigationAction.ReturnToWorksheet,
            Target: null));
    }

    [Fact]
    public void BuildKeyboardNavigationPlan_IgnoresOtherKeysAndEmptyTargetSets()
    {
        var otherKey = StatusBarFocusNavigationPlanner.BuildKeyboardNavigationPlan(
            StatusBarKeyboardNavigationKey.Other,
            reverse: false,
            StatusBarFocusTarget.ZoomSlider,
            AllCandidates());
        var emptyTargets = StatusBarFocusNavigationPlanner.BuildKeyboardNavigationPlan(
            StatusBarKeyboardNavigationKey.Tab,
            reverse: false,
            StatusBarFocusTarget.ZoomSlider,
            Candidates(available: []));

        otherKey.Action.Should().Be(StatusBarKeyboardNavigationAction.Ignore);
        emptyTargets.Action.Should().Be(StatusBarKeyboardNavigationAction.Ignore);
    }

    [Fact]
    public void Planner_DoesNotReferencePlatformUiAssemblies()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find(
            "src",
            "FreeX.App.Services",
            "StatusBarFocusNavigationPlanner.cs"));

        source.Should().NotContain("System.Windows");
        source.Should().NotContain("Avalonia");
        source.Should().NotContain("FreeX.App.Host");
    }

    private static IReadOnlyCollection<StatusBarFocusCandidate> AllCandidates() =>
        Candidates(StatusBarFocusNavigationPlanner.FocusOrder);

    private static IReadOnlyCollection<StatusBarFocusCandidate> Candidates(
        params StatusBarFocusTarget[] available) =>
        Candidates((IReadOnlyCollection<StatusBarFocusTarget>)available);

    private static IReadOnlyCollection<StatusBarFocusCandidate> Candidates(
        IReadOnlyCollection<StatusBarFocusTarget> available)
    {
        var availableSet = available.ToHashSet();
        return StatusBarFocusNavigationPlanner.FocusOrder
            .Select(target => new StatusBarFocusCandidate(target, availableSet.Contains(target)))
            .ToArray();
    }
}
