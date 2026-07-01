using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationTransitionCommandPlannerTests
{
    [Theory]
    [InlineData("freep.transition.none", TransitionKind.None)]
    [InlineData("freep.transition.fade", TransitionKind.Fade)]
    [InlineData("freep.transition.push", TransitionKind.Push)]
    [InlineData("freep.transition.wipe", TransitionKind.Wipe)]
    [InlineData("freep.transition.split", TransitionKind.Split)]
    [InlineData("freep.transition.cut", TransitionKind.Cut)]
    [InlineData("freep.transition.cover", TransitionKind.Cover)]
    [InlineData("freep.transition.uncover", TransitionKind.Uncover)]
    [InlineData("freep.transition.blinds", TransitionKind.Blinds)]
    [InlineData("freep.transition.dissolve", TransitionKind.Dissolve)]
    [InlineData("freep.transition.zoom", TransitionKind.Zoom)]
    [InlineData("freep.transition.wheel", TransitionKind.Wheel)]
    public void TryPlan_MapsGalleryCommandIdsToTransitionKinds(
        string commandId,
        TransitionKind expectedKind)
    {
        PresentationTransitionCommandPlanner.TryPlan(commandId, out var plan).Should().BeTrue();

        plan.CommandId.Should().Be(commandId);
        plan.Intent.Should().Be(PresentationTransitionCommandIntentKind.SetKind);
        plan.Kind.Should().Be(expectedKind);
    }

    [Theory]
    [InlineData("freep.transition.duration", PresentationTransitionCommandIntentKind.SetDuration)]
    [InlineData("freep.transition.advance-on-click", PresentationTransitionCommandIntentKind.ToggleAdvanceOnClick)]
    [InlineData("freep.transition.advance-after", PresentationTransitionCommandIntentKind.SetAdvanceAfter)]
    [InlineData("freep.transition.apply-all", PresentationTransitionCommandIntentKind.ApplyToAllSlides)]
    public void TryPlan_MapsTimingAndApplyAllCommandIdsToTypedIntents(
        string commandId,
        PresentationTransitionCommandIntentKind expectedIntent)
    {
        PresentationTransitionCommandPlanner.TryPlan(commandId, out var plan).Should().BeTrue();

        plan.CommandId.Should().Be(commandId);
        plan.Intent.Should().Be(expectedIntent);
        plan.Kind.Should().BeNull();
    }

    [Fact]
    public void TryPlan_RejectsUnknownCommandId()
    {
        PresentationTransitionCommandPlanner.TryPlan("freep.transition.missing", out var plan)
            .Should().BeFalse();

        plan.Should().BeNull();
    }

    [Fact]
    public void BuildTransitionForKind_NoneClearsTransition()
    {
        var current = new SlideTransition
        {
            Kind = TransitionKind.Push,
            Direction = TransitionDirection.Left,
            DurationMs = 1500,
        };

        PresentationTransitionCommandPlanner.BuildTransitionForKind(current, TransitionKind.None)
            .Should().BeNull();
    }

    [Fact]
    public void BuildTransitionForKind_NewTransitionUsesPowerPointFastDefault()
    {
        var transition = PresentationTransitionCommandPlanner.BuildTransitionForKind(null, TransitionKind.Fade);

        transition.Should().NotBeNull();
        transition!.Kind.Should().Be(TransitionKind.Fade);
        transition.Direction.Should().BeNull();
        transition.DurationMs.Should().Be(PresentationTransitionCommandPlanner.DefaultDurationMs);
        transition.AdvanceOnClick.Should().BeTrue();
        transition.AdvanceAfterMs.Should().BeNull();
    }

    [Fact]
    public void BuildTransitionForKind_PreservesExistingTimingAndDirection()
    {
        var current = new SlideTransition
        {
            Kind = TransitionKind.Wipe,
            Direction = TransitionDirection.Left,
            DurationMs = 1250,
            AdvanceOnClick = false,
            AdvanceAfterMs = 3000,
            RawXml = "<p:transition />",
            MorphOption = "byWord",
        };

        var transition = PresentationTransitionCommandPlanner.BuildTransitionForKind(
            current,
            TransitionKind.Push);

        transition.Should().NotBeSameAs(current);
        transition!.Kind.Should().Be(TransitionKind.Push);
        transition.Direction.Should().Be(TransitionDirection.Left);
        transition.DurationMs.Should().Be(1250);
        transition.AdvanceOnClick.Should().BeFalse();
        transition.AdvanceAfterMs.Should().Be(3000);
        transition.RawXml.Should().BeNull();
        transition.MorphOption.Should().BeNull();
    }

    [Theory]
    [InlineData("0.50s", false, 500)]
    [InlineData("1.25 sec", false, 1250)]
    [InlineData("0s", true, 0)]
    public void TryParseSeconds_MapsRibbonTimingValues(
        string value,
        bool allowZero,
        int expectedMs)
    {
        PresentationTransitionCommandPlanner.TryParseSeconds(value, allowZero, out int ms)
            .Should().BeTrue();

        ms.Should().Be(expectedMs);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("0s", false)]
    [InlineData("-1s", true)]
    [InlineData("fast", false)]
    public void TryParseSeconds_RejectsInvalidValues(string? value, bool allowZero)
    {
        PresentationTransitionCommandPlanner.TryParseSeconds(value, allowZero, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void BuildApplyToAllTransitions_ClonesSourceForEachSlide()
    {
        var source = new SlideTransition
        {
            Kind = TransitionKind.Zoom,
            Direction = TransitionDirection.In,
            DurationMs = 2000,
            AdvanceAfterMs = 5000,
        };

        var transitions = PresentationTransitionCommandPlanner.BuildApplyToAllTransitions(3, source);

        transitions.Should().HaveCount(3);
        foreach (var transition in transitions)
        {
            transition.Should().NotBeNull();
            transition!.Kind.Should().Be(TransitionKind.Zoom);
            transition.DurationMs.Should().Be(2000);
        }

        transitions[0].Should().NotBeSameAs(source);
        transitions[1].Should().NotBeSameAs(transitions[0]);
    }

    [Fact]
    public void BuildApplyToAllTransitions_NullSourcePlansClearForEachSlide()
    {
        PresentationTransitionCommandPlanner.BuildApplyToAllTransitions(2, null)
            .Should()
            .Equal(new SlideTransition?[] { null, null });
    }
}
