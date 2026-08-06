using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowDisplayCoordinatorTests
{
    [Fact]
    public void CoordinatorSource_RemainsFrameworkNeutralAndExcludesMediaPlaybackSession()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Presentation",
            "SlideShowDisplayCoordinator.cs"));

        source.Should().Contain("public sealed class SlideShowDisplayCoordinator");
        source.Should().Contain("public interface ISlideShowDisplayRenderer");
        source.Should().Contain("SlideShowDisplayRendererOperationKind.CancelVisualOperations");
        source.Should().Contain("SlideShowDisplayRendererOperationKind.EnterMediaSlide");
        source.Should().NotContain("System.Windows");
        source.Should().NotContain("Avalonia");
        source.Should().NotContain("DispatcherTimer");
        source.Should().NotContain("Storyboard");
        source.Should().NotContain("SlideShowMediaPlaybackSession");
        source.Should().NotContain("SlideShowMediaController");
        source.Should().NotContain("Geometry");
    }

    [Fact]
    public void Display_ExecutesCancellationOverlayTransitionTimerAndPresenterInOrder()
    {
        var coordinator = new SlideShowDisplayCoordinator();
        var renderer = new RecordingRenderer();
        coordinator.TogglePresenterView(renderer);
        renderer.Events.Clear();

        var display = MakeDisplay(
            transition: new SlideTransition { Kind = TransitionKind.Fade },
            autoAdvanceAfterMs: 2_500);
        var plan = coordinator.Display(display, renderer);

        plan.DisplayVersion.Should().Be(1);
        renderer.Events.Should().Equal(
            "stop-auto",
            "cancel-visuals",
            "apply-display",
            "refresh-ink",
            "prepare-overlay",
            "enter-media",
            "play-transition:Fade",
            "start-auto:00:00:02.5000000:1",
            "refresh-presenter");
    }

    [Fact]
    public void Display_WithoutSlide_StillCancelsPriorWorkButDoesNotArmPlayback()
    {
        var coordinator = new SlideShowDisplayCoordinator();
        var renderer = new RecordingRenderer();
        var display = MakeDisplay(includeSlide: false, autoAdvanceAfterMs: 1_000);

        coordinator.Display(display, renderer);

        renderer.Events.Should().Equal(
            "stop-auto",
            "cancel-visuals",
            "apply-display",
            "refresh-ink");
    }

    [Fact]
    public void AutoAdvance_IsOneShotAndRejectsStaleDisplayVersions()
    {
        var coordinator = new SlideShowDisplayCoordinator();
        var renderer = new RecordingRenderer();

        var first = coordinator.Display(MakeDisplay(autoAdvanceAfterMs: 500), renderer);
        var second = coordinator.Display(MakeDisplay(autoAdvanceAfterMs: 750), renderer);
        renderer.Events.Clear();

        coordinator.HandleAutoAdvanceElapsed(first.DisplayVersion, renderer)
            .Operations.Should().BeEmpty();
        coordinator.HandleAutoAdvanceElapsed(second.DisplayVersion, renderer)
            .Operations.Select(operation => operation.Kind).Should().Equal(
                SlideShowDisplayRendererOperationKind.StopAutoAdvanceTimer,
                SlideShowDisplayRendererOperationKind.RequestAutoAdvance);
        coordinator.HandleAutoAdvanceElapsed(second.DisplayVersion, renderer)
            .Operations.Should().BeEmpty();
        renderer.Events.Should().Equal("stop-auto", "request-auto");
    }

    [Fact]
    public void KioskTimer_IsActiveOnlyDuringAStartedOpenSession()
    {
        var coordinator = new SlideShowDisplayCoordinator();
        var renderer = new RecordingRenderer();

        coordinator.HandleKioskRestartElapsed(renderer).Operations.Should().BeEmpty();
        coordinator.StartSession(TimeSpan.FromSeconds(12), renderer);
        coordinator.HandleKioskRestartElapsed(renderer);
        coordinator.CloseSession(renderer);
        coordinator.HandleKioskRestartElapsed(renderer).Operations.Should().BeEmpty();

        renderer.Events.Should().Equal(
            "stop-kiosk",
            "start-kiosk:00:00:12",
            "request-kiosk",
            "stop-auto",
            "stop-kiosk",
            "cancel-visuals");
    }

    [Fact]
    public void PresenterToggle_OwnsStateAndNativeCloseNotification()
    {
        var coordinator = new SlideShowDisplayCoordinator();
        var renderer = new RecordingRenderer();

        coordinator.TogglePresenterView(renderer);
        coordinator.IsPresenterViewOpen.Should().BeTrue();
        coordinator.NotifyPresenterViewClosed();
        coordinator.IsPresenterViewOpen.Should().BeFalse();
        coordinator.TogglePresenterView(renderer);
        coordinator.TogglePresenterView(renderer);

        renderer.Events.Should().Equal(
            "open-presenter",
            "open-presenter",
            "close-presenter");
    }

    [Fact]
    public void CloseSession_InvalidatesDisplayAndClosesPresenterOnce()
    {
        var coordinator = new SlideShowDisplayCoordinator();
        var renderer = new RecordingRenderer();
        coordinator.TogglePresenterView(renderer);
        var display = coordinator.Display(MakeDisplay(autoAdvanceAfterMs: 300), renderer);
        renderer.Events.Clear();

        coordinator.CloseSession(renderer);
        coordinator.CloseSession(renderer).Operations.Should().BeEmpty();
        coordinator.HandleAutoAdvanceElapsed(display.DisplayVersion, renderer)
            .Operations.Should().BeEmpty();
        var displayAction = () => coordinator.PlanDisplay(MakeDisplay());

        displayAction.Should().Throw<InvalidOperationException>()
            .WithMessage("*display session is closed*");
        renderer.Events.Should().Equal(
            "stop-auto",
            "stop-kiosk",
            "cancel-visuals",
            "close-presenter");
    }

    private static SlideShowRuntimeDisplayPlan MakeDisplay(
        bool includeSlide = true,
        SlideTransition? transition = null,
        int? autoAdvanceAfterMs = null)
    {
        var slide = includeSlide ? new Slide { Title = "Display slide" } : null;
        return new SlideShowRuntimeDisplayPlan(
            new SlideShowHostDisplayPlan(
                slide,
                SlideShowSlideMetrics.Default,
                transition,
                autoAdvanceAfterMs),
            CaptionSlideIndex: 0,
            CaptionTracks: Array.Empty<PresentationMediaTranscriptTrackDescriptor>(),
            PreferredCaptionShapeId: null,
            PreferredCaptionTrackIndex: null,
            PreferredCaptionSlideIndex: null,
            ShowMediaControls: true,
            ShowNarration: true);
    }

    private sealed class RecordingRenderer : ISlideShowDisplayRenderer
    {
        public List<string> Events { get; } = new();

        public void StopAutoAdvanceTimer() => Events.Add("stop-auto");
        public void CancelVisualOperations() => Events.Add("cancel-visuals");
        public void ApplyDisplayState(SlideShowRuntimeDisplayPlan plan) => Events.Add("apply-display");
        public void RefreshInkOverlay() => Events.Add("refresh-ink");
        public void PrepareAnimationOverlay(Slide slide) => Events.Add("prepare-overlay");
        public void EnterMediaSlide(SlideShowRuntimeDisplayPlan plan) => Events.Add("enter-media");
        public void PlayTransition(Slide slide, SlideTransition transition) =>
            Events.Add($"play-transition:{transition.Kind}");
        public void ShowSlideInstant(Slide slide) => Events.Add("show-instant");
        public void StartAutoAdvanceTimer(TimeSpan interval, long displayVersion) =>
            Events.Add($"start-auto:{interval}:{displayVersion}");
        public void RefreshPresenterView() => Events.Add("refresh-presenter");
        public void StopKioskRestartTimer() => Events.Add("stop-kiosk");
        public void StartKioskRestartTimer(TimeSpan interval) => Events.Add($"start-kiosk:{interval}");
        public void RequestAutoAdvance() => Events.Add("request-auto");
        public void RequestKioskRestart() => Events.Add("request-kiosk");
        public void OpenPresenterView() => Events.Add("open-presenter");
        public void ClosePresenterView() => Events.Add("close-presenter");
    }
}
