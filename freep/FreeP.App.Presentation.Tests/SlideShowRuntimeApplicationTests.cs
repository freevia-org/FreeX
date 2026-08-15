using System.Text;
using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowRuntimeApplicationTests
{
    private static readonly DateTimeOffset StartedAtUtc =
        new(2026, 8, 6, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Runtime_ProjectsWindowMediaAndCaptionPolicy()
    {
        var presentation = MakePresentation(1);
        presentation.ShowType = PresentationShowType.BrowsedByIndividual;
        presentation.ShowBrowseScrollbar = true;
        presentation.ShowMediaControls = false;
        presentation.ShowWithNarration = false;
        presentation.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 42,
            Name = "Training video",
            Kind = SlideShapeKind.Media,
            Media = new MediaInfo
            {
                IsVideo = true,
                CaptionTracks =
                {
                    new MediaCaptionTrackInfo
                    {
                        Source = "ppt/media/training.vtt",
                        ContentType = "text/vtt",
                        Language = "en-US",
                        Label = "English",
                        Bytes = Encoding.UTF8.GetBytes(
                            "WEBVTT\n\n00:00:00.000 --> 00:00:01.000\nWelcome")
                    }
                }
            }
        });

        var runtime = CreateRuntime(
            presentation,
            new SlideShowRuntimeCaptionPreference(SlideIndex: 0, ShapeId: 42, TrackIndex: 0));
        runtime.BindRenderer(NoOpRenderer());

        runtime.WindowPlan.Should().Be(new SlideShowRuntimeWindowPlan(
            IsBrowseWindow: true,
            IsBorderless: false,
            IsTopmost: false,
            AllowsResize: true,
            ShowBrowseScrollbars: true));
        runtime.WindowPlan.PlanBrowseWindowSize(1920, 1080)
            .Should().Be(new SlideShowBrowseWindowSizePlan(1024, 768));
        runtime.WindowPlan.PlanBrowseWindowSize(800, 600)
            .Should().Be(new SlideShowBrowseWindowSizePlan(680, 510));
        runtime.WindowPlan.PlanBrowseWindowSize(double.NaN, double.PositiveInfinity)
            .Should().Be(new SlideShowBrowseWindowSizePlan(1024, 768));
        runtime.InitialSlideMetrics.WidthDip.Should().BeGreaterThan(0);
        runtime.InitialSlideMetrics.HeightDip.Should().BeGreaterThan(0);

        var plan = runtime.BuildDisplayPlan(animated: false);

        plan.CaptionSlideIndex.Should().Be(0);
        plan.CaptionTracks.Should().ContainSingle()
            .Which.Should().Match<PresentationMediaTranscriptTrackDescriptor>(track =>
                track.SlideIndex == 0 &&
                track.ShapeId == 42 &&
                track.Cues.Count == 1);
        plan.PreferredCaptionShapeId.Should().Be(42);
        plan.PreferredCaptionTrackIndex.Should().Be(0);
        plan.PreferredCaptionSlideIndex.Should().Be(0);
        plan.ShowMediaControls.Should().BeFalse();
        plan.ShowNarration.Should().BeFalse();
    }

    [Fact]
    public void Runtime_ExecutesNavigationAndInputThroughOneBoundRenderer()
    {
        var presentation = MakePresentation(2);
        var events = new List<string>();
        var runtime = CreateRuntime(presentation);
        runtime.BindRenderer(new SlideShowRuntimeRendererCallbacks(
            () => events.Add("stop"),
            _ => events.Add("close"),
            _ => events.Add("animation"),
            navigation => events.Add($"navigate:{navigation.SlideIndex}"),
            () => events.Add("presenter"),
            () => events.Add("display"),
            plan => events.Add($"screen:{plan.Mode}:{plan.IsBlank}"),
            hyperlink => events.Add($"external:{hyperlink.Url}"),
            () => events.Add("ink"),
            hyperlink => events.Add($"internal:{hyperlink.TargetSlideId}")));

        runtime.ExecuteAdvance(StartedAtUtc.AddSeconds(1), stopAutoAdvance: true)
            .Should().BeOfType<AdvanceResult.NavigateToSlide>();
        runtime.ActivateHyperlink(new Hyperlink { Url = "https://example.com" });
        runtime.HandleKeyboardInput("B").Should().BeTrue();
        runtime.HandleKeyboardInput("P", controlPressed: true).Should().BeTrue();

        events.Should().Equal(
            "stop",
            "navigate:1",
            "external:https://example.com",
            "screen:Black:True",
            "presenter");
        runtime.CurrentPresentationSlideIndex.Should().Be(1);
        runtime.ScreenMode.Should().Be(SlideShowScreenMode.Black);
    }

    [Fact]
    public void Runtime_OwnsPresenterOperationsInkRefreshAndClock()
    {
        var presentation = MakePresentation(2);
        var nowUtc = StartedAtUtc.AddMinutes(3);
        var refreshCount = 0;
        var navigatedTo = -1;
        var notes = new List<(int SlideIndex, string? Text)>();
        var runtime = CreateRuntime(presentation, utcNow: () => nowUtc);
        runtime.BindRenderer(new SlideShowRuntimeRendererCallbacks(
            () => { },
            _ => { },
            _ => { },
            navigation => navigatedTo = navigation.SlideIndex,
            () => { },
            () => { },
            _ => { },
            _ => { },
            () => refreshCount++));

        var operations = runtime.CreatePresenterViewOperations(
            (slideIndex, text) => notes.Add((slideIndex, text)));
        operations.StateProvider().Elapsed.Should().Be(TimeSpan.FromMinutes(3));
        operations.SetNotesText(0, "Updated notes");
        operations.SelectPointerMode(SlideShowPresenterPointerMode.Pen);

        var pointer = new SlideShowCanvasPointer(
            100,
            100,
            960,
            540,
            runtime.InitialSlideMetrics);
        runtime.BeginPointerInk(pointer).IsHandled.Should().BeTrue();
        runtime.AppendPointerInk(pointer with { X = 120, Y = 120 }).IsHandled.Should().BeTrue();
        runtime.EndPointerInk(pointer with { X = 140, Y = 140 }).IsHandled.Should().BeTrue();
        operations.GoNext();

        notes.Should().Equal((0, "Updated notes"));
        refreshCount.Should().Be(4);
        runtime.InkExecutionState.CommittedStrokes.Should().ContainSingle();
        navigatedTo.Should().Be(1);
    }

    [Fact]
    public void Runtime_RevealsHiddenSlidesRestartsKioskAndStopsAfterClose()
    {
        var presentation = MakePresentation(3);
        presentation.ShowType = PresentationShowType.BrowsedAtKiosk;
        presentation.KioskRestartAfterMilliseconds = 12_000;
        presentation.Slides[1].IsHidden = true;
        var events = new List<string>();
        var runtime = CreateRuntime(presentation);
        runtime.BindRenderer(new SlideShowRuntimeRendererCallbacks(
            () => events.Add("stop"),
            _ => events.Add("close"),
            _ => events.Add("animation"),
            navigation => events.Add($"navigate:{navigation.SlideIndex}"),
            () => { },
            () => events.Add("display-hidden"),
            _ => { },
            _ => { },
            () => { }));

        runtime.KioskRestartInterval.Should().Be(TimeSpan.FromSeconds(12));
        runtime.ExecuteHiddenSlideReveal().Should().BeSameAs(presentation.Slides[1]);
        runtime.ExecuteAdvance(stopAutoAdvance: true);
        runtime.RestartKioskShow();
        runtime.Close(StartedAtUtc.AddSeconds(5));
        runtime.RestartKioskShow();

        events.Should().Equal(
            "stop",
            "display-hidden",
            "stop",
            "navigate:1",
            "stop",
            "navigate:0");
        runtime.IsClosed.Should().BeTrue();
    }

    [Fact]
    public void Runtime_RequiresExactlyOneRendererBinding()
    {
        var runtime = CreateRuntime(MakePresentation(1));

        var unboundAction = () => runtime.ExecuteAdvance();
        unboundAction.Should().Throw<InvalidOperationException>()
            .WithMessage("*Bind a slideshow runtime renderer*");

        runtime.BindRenderer(NoOpRenderer());
        var secondBinding = () => runtime.BindRenderer(NoOpRenderer());
        secondBinding.Should().Throw<InvalidOperationException>()
            .WithMessage("*already bound*");
    }

    [Fact]
    public void RuntimeSession_ProjectsTheStableRendererNeutralControlSurface()
    {
        var runtime = CreateRuntime(MakePresentation(2));
        runtime.BindRenderer(NoOpRenderer());
        var session = new SlideShowRuntimeSession(runtime);

        session.Controller.Should().BeSameAs(runtime.Controller);
        session.PresenterToolPlan.Should().BeSameAs(runtime.ToolPlan);
        session.ExecuteAdvance(StartedAtUtc.AddSeconds(1)).Should().NotBeNull();
        session.SetScreenMode(SlideShowScreenMode.Black);

        session.ScreenMode.Should().Be(SlideShowScreenMode.Black);
        session.PresenterStartedAtUtc.Should().Be(StartedAtUtc);
        session.IsPresenterSessionClosed.Should().BeFalse();
    }

    [Fact]
    public void Runtime_OwnsDisplayPresenterAndIdempotentRendererTeardown()
    {
        var events = new List<string>();
        var runtime = CreateRuntime(MakePresentation(1));
        var displayRenderer = new RecordingDisplayRenderer(events);
        runtime.BindRenderer(
            NoOpRenderer() with
            {
                StopTransitionAudio = () => events.Add("stop-audio"),
                TeardownMedia = () => events.Add("teardown-media")
            },
            displayRenderer);

        runtime.DisplayCurrentSlide(animated: false);
        runtime.StartRendererSession();
        runtime.TogglePresenterView();
        runtime.IsPresenterViewOpen.Should().BeTrue();
        events.Clear();

        runtime.CloseRendererSession(StartedAtUtc.AddSeconds(5));
        runtime.CloseRendererSession(StartedAtUtc.AddSeconds(6));

        events.Should().Equal(
            "stop-audio",
            "stop-auto",
            "stop-kiosk",
            "cancel-visuals",
            "close-presenter",
            "teardown-media");
        runtime.IsPresenterViewOpen.Should().BeFalse();
        runtime.IsClosed.Should().BeTrue();
    }

    private static SlideShowRuntimeApplication CreateRuntime(
        Presentation presentation,
        SlideShowRuntimeCaptionPreference? captionPreference = null,
        Func<DateTimeOffset>? utcNow = null) =>
        new(
            presentation,
            SlideShowCustomShowPlanner.BuildFullPresentationRoute(presentation, startIndex: 0),
            StartedAtUtc,
            SlideShowHostCapabilityRecordingCaptureBackend.Deferred("runtime application test"),
            captionPreference,
            utcNow);

    private static SlideShowRuntimeRendererCallbacks NoOpRenderer() => new(
        () => { },
        _ => { },
        _ => { },
        _ => { },
        () => { },
        () => { },
        _ => { },
        _ => { },
        () => { });

    private static Presentation MakePresentation(int slideCount)
    {
        var presentation = Presentation.CreateEmpty();
        while (presentation.Slides.Count < slideCount)
        {
            presentation.Slides.Add(new Slide { Title = $"Slide {presentation.Slides.Count + 1}" });
        }

        return presentation;
    }

    private sealed class RecordingDisplayRenderer(List<string> events) : ISlideShowDisplayRenderer
    {
        public void StopAutoAdvanceTimer() => events.Add("stop-auto");
        public void CancelVisualOperations() => events.Add("cancel-visuals");
        public void ApplyDisplayState(SlideShowRuntimeDisplayPlan plan) => events.Add("apply-display");
        public void RefreshInkOverlay() => events.Add("refresh-ink");
        public void PrepareAnimationOverlay(Slide slide) => events.Add("prepare-overlay");
        public void EnterMediaSlide(SlideShowRuntimeDisplayPlan plan) => events.Add("enter-media");
        public void PlayTransition(Slide slide, SlideTransition transition) => events.Add("transition");
        public void ShowSlideInstant(Slide slide) => events.Add("instant");
        public void StartAutoAdvanceTimer(TimeSpan interval, long displayVersion) => events.Add("start-auto");
        public void RefreshPresenterView() => events.Add("refresh-presenter");
        public void StopKioskRestartTimer() => events.Add("stop-kiosk");
        public void StartKioskRestartTimer(TimeSpan interval) => events.Add("start-kiosk");
        public void RequestAutoAdvance() => events.Add("request-auto");
        public void RequestKioskRestart() => events.Add("request-kiosk");
        public void OpenPresenterView() => events.Add("open-presenter");
        public void ClosePresenterView() => events.Add("close-presenter");
    }
}
