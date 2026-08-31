namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Round-175 F1: a shape or text run whose hyperlink is a built-in action-only click
/// (<see cref="HyperlinkActionKind"/> other than <see cref="HyperlinkActionKind.None"/> --
/// e.g. a PowerPoint "Action Button" authored as
/// <c>&lt;a:hlinkClick action="ppaction://hlink..."/&gt;</c> with no r:id, so both
/// <see cref="Hyperlink.Url"/> and <see cref="Hyperlink.TargetSlideId"/> stay null) used to be
/// completely inert when clicked during the slide show: PlanHyperlinkActivation only branched
/// on IsExternal/TargetSlideId and never read hyperlink.Action, so it fell through to
/// SlideShowSessionInputActionKind.None. These tests exercise every action kind through the
/// same PlanHyperlinkActivation entry point the WPF and Avalonia slide show hosts call.
/// </summary>
public sealed class SlideShowSessionControllerActionHyperlinkTests
{
    [Fact]
    public void PlanHyperlinkActivation_NextSlideAction_AdvancesToNextSlide()
    {
        var session = CreateSession(3, startIndex: 0);

        var plan = session.PlanHyperlinkActivation(new Hyperlink { Action = HyperlinkActionKind.NextSlide });

        plan.ActionKind.Should().Be(SlideShowSessionInputActionKind.ExecuteHostCommand);
        plan.HostCommand.Kind.Should().Be(SlideShowHostCommandKind.NavigateToSlide);
        plan.HostCommand.SlideIndex.Should().Be(1);
    }

    [Fact]
    public void PlanHyperlinkActivation_PreviousSlideAction_GoesBackASlide()
    {
        var session = CreateSession(3, startIndex: 1);

        var plan = session.PlanHyperlinkActivation(new Hyperlink { Action = HyperlinkActionKind.PreviousSlide });

        plan.ActionKind.Should().Be(SlideShowSessionInputActionKind.ExecuteHostCommand);
        plan.HostCommand.Kind.Should().Be(SlideShowHostCommandKind.NavigateToSlide);
        plan.HostCommand.SlideIndex.Should().Be(0);
    }

    [Fact]
    public void PlanHyperlinkActivation_FirstSlideAction_JumpsToSlideZero()
    {
        var session = CreateSession(4, startIndex: 2);

        var plan = session.PlanHyperlinkActivation(new Hyperlink { Action = HyperlinkActionKind.FirstSlide });

        plan.ActionKind.Should().Be(SlideShowSessionInputActionKind.ExecuteHostCommand);
        plan.HostCommand.Kind.Should().Be(SlideShowHostCommandKind.NavigateToSlide);
        plan.HostCommand.SlideIndex.Should().Be(0);
    }

    [Fact]
    public void PlanHyperlinkActivation_LastSlideAction_JumpsToFinalSlide()
    {
        var session = CreateSession(4, startIndex: 0);

        var plan = session.PlanHyperlinkActivation(new Hyperlink { Action = HyperlinkActionKind.LastSlide });

        plan.ActionKind.Should().Be(SlideShowSessionInputActionKind.ExecuteHostCommand);
        plan.HostCommand.Kind.Should().Be(SlideShowHostCommandKind.NavigateToSlide);
        plan.HostCommand.SlideIndex.Should().Be(3);
    }

    [Fact]
    public void PlanHyperlinkActivation_EndShowAction_ProducesCloseCommand()
    {
        var session = CreateSession(2, startIndex: 0);

        var plan = session.PlanHyperlinkActivation(new Hyperlink { Action = HyperlinkActionKind.EndShow });

        plan.ActionKind.Should().Be(SlideShowSessionInputActionKind.ExecuteHostCommand);
        plan.HostCommand.Kind.Should().Be(SlideShowHostCommandKind.Close);
    }

    [Fact]
    public void PlanHyperlinkActivation_LastSlideViewedAction_ReturnsToPriorSlide()
    {
        var session = CreateSession(3, startIndex: 0);

        // Move 0 -> 2 via an ordinary internal-jump hyperlink so MoveToSlide records slide 0
        // as "last viewed" before departing it.
        var jumpPlan = session.PlanHyperlinkActivation(
            new Hyperlink { TargetSlideId = session.PlaybackRoute.Slides[2].Id });
        session.ExecuteInputPlan(jumpPlan, MakeCallbacks(session));
        session.CurrentPresentationSlideIndex.Should().Be(2);

        var lastViewedPlan = session.PlanHyperlinkActivation(
            new Hyperlink { Action = HyperlinkActionKind.LastSlideViewed });

        lastViewedPlan.ActionKind.Should().Be(SlideShowSessionInputActionKind.ExecuteHostCommand);
        lastViewedPlan.HostCommand.Kind.Should().Be(SlideShowHostCommandKind.NavigateToSlide);
        lastViewedPlan.HostCommand.SlideIndex.Should().Be(0);
    }

    [Fact]
    public void PlanHyperlinkActivation_LastSlideViewedAction_NoOpsWhenNothingWasViewedYet()
    {
        var session = CreateSession(3, startIndex: 0);

        var plan = session.PlanHyperlinkActivation(new Hyperlink { Action = HyperlinkActionKind.LastSlideViewed });

        plan.ActionKind.Should().Be(SlideShowSessionInputActionKind.ExecuteHostCommand);
        plan.HostCommand.Kind.Should().Be(SlideShowHostCommandKind.None);
        plan.HostCommand.IsHandled.Should().BeTrue();
    }

    /// <summary>
    /// Sibling no-regression case: an internal slide-jump hyperlink (Hyperlink.TargetSlideId
    /// set, Action left at its default None) must keep behaving exactly as before -- this is
    /// the adjacent case PlanHyperlinkActivation already handled correctly and round-175 F1
    /// must not disturb.
    /// </summary>
    [Fact]
    public void PlanHyperlinkActivation_OrdinarySlideJumpHyperlink_StillNavigatesByTargetSlideId()
    {
        var session = CreateSession(3, startIndex: 0);
        var targetId = session.PlaybackRoute.Slides[2].Id;

        var plan = session.PlanHyperlinkActivation(new Hyperlink { TargetSlideId = targetId });

        plan.ActionKind.Should().Be(SlideShowSessionInputActionKind.ExecuteHostCommand);
        plan.HostCommand.Kind.Should().Be(SlideShowHostCommandKind.NavigateToSlide);
        plan.HostCommand.SlideIndex.Should().Be(2);
    }

    /// <summary>
    /// Sibling no-regression case: an external URL hyperlink (Hyperlink.Url set) must still
    /// resolve to OpenExternalHyperlink regardless of Action -- IsExternal is checked before
    /// Action in PlanHyperlinkActivation and that ordering must not change.
    /// </summary>
    [Fact]
    public void PlanHyperlinkActivation_ExternalUrlHyperlink_StillOpensExternally()
    {
        var session = CreateSession(2, startIndex: 0);

        var plan = session.PlanHyperlinkActivation(new Hyperlink { Url = "https://example.com" });

        plan.ActionKind.Should().Be(SlideShowSessionInputActionKind.OpenExternalHyperlink);
    }

    private static SlideShowSessionController CreateSession(int slideCount, int startIndex)
    {
        var presentation = MakePresentation(slideCount);
        var route = SlideShowCustomShowPlanner.BuildFullPresentationRoute(presentation, startIndex);
        return new SlideShowSessionController(
            presentation,
            route,
            DateTimeOffset.UtcNow,
            SlideShowHostCapabilityRecordingCaptureBackend.Deferred("round-175 action hyperlink test"));
    }

    private static SlideShowSessionInputExecutionCallbacks MakeCallbacks(SlideShowSessionController session) =>
        new(
            TogglePresenterView: () => { },
            RevealHiddenSlide: targetSlideId => session.RevealHiddenSlide(targetSlideId),
            SetScreenMode: _ => { },
            ExecuteHostCommand: command => session.ExecuteHostCommand(
                command,
                DateTimeOffset.UtcNow,
                new SlideShowHostExecutionCallbacks(
                    StopAutoAdvance: () => { },
                    Close: _ => { },
                    PlayAnimationStep: _ => { },
                    NavigateToSlide: _ => { })),
            OpenExternalHyperlink: _ => { });

    private static Presentation MakePresentation(int slideCount)
    {
        var presentation = Presentation.CreateEmpty();
        while (presentation.Slides.Count < slideCount)
        {
            presentation.Slides.Add(new Slide { Title = $"Slide {presentation.Slides.Count + 1}" });
        }

        return presentation;
    }
}
