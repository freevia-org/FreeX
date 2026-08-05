using System.IO;
using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowPresenterSessionDedupTests
{
    [Fact]
    public void Session_ExecutesStopThenTimingMoveThenNativeNavigation()
    {
        var presentation = MakePresentation(2);
        var route = SlideShowCustomShowPlanner.BuildFullPresentationRoute(presentation, startIndex: 0);
        var started = new DateTimeOffset(2026, 8, 5, 10, 0, 0, TimeSpan.Zero);
        var session = new SlideShowSessionController(
            presentation,
            route,
            started,
            new SlideShowDeterministicRecordingCaptureBackend("portable presenter test"));
        session.SetTimingIntent(SlideShowTimingIntent.RecordTimings, started);
        var events = new List<string>();

        var command = session.PlanAdvance(stopAutoAdvance: true);
        session.CurrentPresentationSlideIndex.Should().Be(0,
            "presenter timing follows the displayed slide until command execution");

        session.ExecuteHostCommand(
            command,
            started.AddMilliseconds(1500),
            new SlideShowHostExecutionCallbacks(
                () => events.Add("stop-auto-advance"),
                _ => events.Add("close"),
                _ => events.Add("play-step"),
                navigation =>
                {
                    events.Add($"navigate:{session.CurrentPresentationSlideIndex}");
                    navigation.SlideIndex.Should().Be(1);
                }));

        events.Should().Equal("stop-auto-advance", "navigate:1");
        session.TimingRecorderState.RecordedTimings.Should().ContainSingle()
            .Which.AdvanceAfterMs.Should().Be(1500);
        presentation.Slides[0].Transition!.AdvanceAfterMs.Should().Be(1500);
    }

    [Fact]
    public void Session_OwnsNumericJumpBufferAndBlackoutTransitions()
    {
        var presentation = MakePresentation(3);
        var route = SlideShowCustomShowPlanner.BuildFullPresentationRoute(presentation, startIndex: 0);
        var started = new DateTimeOffset(2026, 8, 5, 11, 0, 0, TimeSpan.Zero);
        var session = new SlideShowSessionController(
            presentation,
            route,
            started,
            SlideShowHostCapabilityRecordingCaptureBackend.Deferred("portable presenter test"));

        session.PlanKeyboardInput("D2").ShouldExecuteHostCommand.Should().BeFalse();
        session.SlideNumberBuffer.Should().Be("2");
        var jump = session.PlanKeyboardInput("Enter");
        jump.IsHandled.Should().BeTrue();
        jump.ShouldExecuteHostCommand.Should().BeTrue();
        session.SlideNumberBuffer.Should().BeEmpty();

        session.ExecuteHostCommand(
            jump.HostCommand,
            started.AddSeconds(1),
            NoOpCallbacks());
        session.CurrentPresentationSlideIndex.Should().Be(1);

        var black = session.PlanKeyboardInput("B");
        black.ScreenMode.Should().Be(SlideShowScreenMode.Black);
        session.ScreenMode.Should().Be(SlideShowScreenMode.Black);
        session.IsScreenBlank.Should().BeTrue();

        session.PlanKeyboardInput("B").ScreenMode.Should().Be(SlideShowScreenMode.Normal);
        session.IsScreenBlank.Should().BeFalse();

        session.PlanKeyboardInput("D3");
        session.PlanKeyboardInput("Escape").IsHandled.Should().BeTrue();
        session.SlideNumberBuffer.Should().BeEmpty();
    }

    [Fact]
    public void PresenterViewSession_CommitsNotesBeforeJumpAndOwnsToolToggles()
    {
        var presentation = MakePresentation(2);
        var route = SlideShowCustomShowPlanner.BuildFullPresentationRoute(presentation, startIndex: 0);
        var started = new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);
        var slideshow = new SlideShowSessionController(
            presentation,
            route,
            started,
            SlideShowHostCapabilityRecordingCaptureBackend.Deferred("portable presenter test"));
        var events = new List<string>();
        SlideShowTimingIntent? timingIntent = null;
        SlideShowRecordingMediaIntent? mediaIntent = null;

        var presenter = new SlideShowPresenterViewSession(
            () => slideshow.CreatePresenterState(started.AddSeconds(65)),
            goBack: () => events.Add("back"),
            goNext: () => events.Add("next"),
            setTimingIntent: intent => timingIntent = intent,
            setMediaIntent: intent => mediaIntent = intent,
            goToSlide: slideNumber => events.Add($"jump:{slideNumber}"),
            setNotesText: (slideIndex, text) => events.Add($"notes:{slideIndex}:{text}"));

        presenter.GoToSlide("2", notesDirty: true, notesText: "Updated notes")
            .Should().Be(new SlideShowPresenterViewActionResult(true, true));
        events.Should().Equal("notes:0:Updated notes", "jump:2");

        presenter.ToggleTimingIntent(SlideShowTimingIntent.RecordTimings);
        presenter.ToggleMediaIntent(SlideShowRecordingMediaIntent.NarrationAndMedia);
        timingIntent.Should().Be(SlideShowTimingIntent.RecordTimings);
        mediaIntent.Should().Be(SlideShowRecordingMediaIntent.NarrationAndMedia);

        var plan = presenter.BuildViewPlan();
        plan.ElapsedText.Should().Be("01:05");
        plan.CurrentSlideNumber.Should().Be(1);
        plan.CanGoBack.Should().BeFalse();
        plan.CanAdvance.Should().BeTrue();
        plan.CanSetTimingIntent.Should().BeTrue();
        plan.CanSetMediaIntent.Should().BeTrue();
    }

    [Fact]
    public void NativeHosts_KeepOnlyRendererSpecificPresenterResponsibilities()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var presenterFiles = new[]
        {
            Read(root, "freep", "FreeP.App.Host", "PresenterViewWindow.cs"),
            Read(root, "freep", "FreeP.App.Avalonia", "PresenterViewWindow.cs"),
        };
        var slideShowFiles = new[]
        {
            Read(root, "freep", "FreeP.App.Host", "SlideShowWindow.cs"),
            Read(root, "freep", "FreeP.App.Avalonia", "SlideShowWindow.cs"),
        };

        foreach (var source in presenterFiles)
        {
            source.Should().Contain("SlideShowPresenterViewSession");
            source.Should().Contain("_session.BuildViewPlan()");
            source.Should().Contain("DispatcherTimer");
            source.Should().Contain("SlideCanvas");
            source.Should().NotContain("SlideShowSlideNumberPlanner");
            source.Should().NotContain("BuildRecordingSummary");
            source.Should().NotContain("private readonly Func<SlideShowPresenterState> _stateProvider");
        }

        foreach (var source in slideShowFiles)
        {
            source.Should().Contain("_session.PlanKeyboardInput(");
            source.Should().Contain("_session.ExecuteHostCommand(");
            source.Should().Contain("_session.BuildDisplayPlan(");
            source.Should().Contain("_session.DisplaySlide");
            source.Should().Contain("DispatcherTimer");
            source.Should().Contain("_screenModeOverlay");
            source.Should().NotContain("private readonly SlideShowController _controller");
            source.Should().NotContain("private string _slideNumberBuffer");
            source.Should().NotContain("private SlideShowScreenMode _screenMode");
            source.Should().NotContain("SlideShowScreenModePlanner.TryPlanKey(");
            source.Should().NotContain("SlideShowHostPlanner.PlanSlideNumberJump(");
        }
    }

    private static SlideShowHostExecutionCallbacks NoOpCallbacks() => new(
        () => { },
        _ => { },
        _ => { },
        _ => { });

    private static Presentation MakePresentation(int slideCount)
    {
        var presentation = Presentation.CreateEmpty();
        while (presentation.Slides.Count < slideCount)
        {
            presentation.Slides.Add(new Slide { Title = $"Slide {presentation.Slides.Count + 1}" });
        }

        return presentation;
    }

    private static string Read(string root, params string[] relativeParts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(relativeParts).ToArray()));
}
