using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r188-slideshow-second-launch: every launch built a NEW full-screen window. Pressing F5 twice, or
/// clicking Play while a show was already running, stacked a second presentation on top of the
/// first -- each with its own playback position -- and closing the top one revealed the other still
/// running underneath. PowerPoint re-uses the running show. The coordinator now tracks the window it
/// opened and asks the host whether it is still on screen.
/// </summary>
public sealed class R188_SlideShowSecondLaunchTests
{
    [Fact]
    public void TryLaunch_WhileAShowIsAlreadyRunning_ActivatesItInsteadOfOpeningASecond()
    {
        var editor = CreateEditor();
        var customShows = new SlideShowCustomShowSession(() => editor);
        var created = new List<FakeWindow>();
        var shown = new List<FakeWindow>();
        var activated = new List<FakeWindow>();

        var coordinator = new SlideShowWindowLaunchCoordinator<FakeWindow>(
            customShows,
            () => editor.Presentation,
            () => null,
            editor.SetSlideNotesText,
            _ =>
            {
                var window = new FakeWindow { IsLive = true };
                created.Add(window);
                return window;
            },
            (_, _) => { },
            shown.Add,
            window => window.IsLive,
            activated.Add,
            window => window.IsLive = false);

        coordinator.TryLaunch(fromStart: true).Should().BeTrue();
        coordinator.TryLaunch(fromStart: true).Should().BeTrue();

        created.Should().HaveCount(1, "the running show is re-used, not duplicated");
        shown.Should().HaveCount(1);
        activated.Should().ContainSingle().Which.Should().BeSameAs(created[0]);
    }

    [Fact]
    public void TryLaunch_AfterTheShowWasClosed_OpensAFreshOne()
    {
        // The stale reference must not block the next launch: once the host reports the window is
        // gone, starting the show again has to create a new one.
        var editor = CreateEditor();
        var customShows = new SlideShowCustomShowSession(() => editor);
        var created = new List<FakeWindow>();

        var coordinator = new SlideShowWindowLaunchCoordinator<FakeWindow>(
            customShows,
            () => editor.Presentation,
            () => null,
            editor.SetSlideNotesText,
            _ =>
            {
                var window = new FakeWindow { IsLive = true };
                created.Add(window);
                return window;
            },
            (_, _) => { },
            _ => { },
            window => window.IsLive,
            _ => { },
            window => window.IsLive = false);

        coordinator.TryLaunch(fromStart: true).Should().BeTrue();
        created.Should().HaveCount(1);

        created[0].IsLive = false; // the user closed the show

        coordinator.TryLaunch(fromStart: true).Should().BeTrue();
        created.Should().HaveCount(2, "the previous show is gone, so a new one must open");
    }

    [Fact]
    public void TryLaunchReadingView_WhileAFullscreenShowIsRunning_ReplacesItRatherThanReusingIt()
    {
        // r189 CORRECTION. This test previously asserted that Reading View reuses a running
        // fullscreen show, and it was WRONG -- it pinned the defect the r188 reuse check
        // introduced. Reading View is a windowed browse view; a fullscreen show is not that
        // window, so re-focusing it silently drops what the user asked for. The reuse is only
        // correct when the mode matches.
        var editor = CreateEditor();
        var customShows = new SlideShowCustomShowSession(() => editor);
        var created = new List<(FakeWindow Window, bool Browse)>();
        var activated = new List<FakeWindow>();
        var closed = new List<FakeWindow>();

        var coordinator = new SlideShowWindowLaunchCoordinator<FakeWindow>(
            customShows,
            () => editor.Presentation,
            () => null,
            editor.SetSlideNotesText,
            plan =>
            {
                var window = new FakeWindow { IsLive = true };
                created.Add((window, plan.ForceBrowseWindow));
                return window;
            },
            (_, _) => { },
            _ => { },
            window => window.IsLive,
            activated.Add,
            window =>
            {
                window.IsLive = false;
                closed.Add(window);
            });

        coordinator.TryLaunch(fromStart: true).Should().BeTrue();
        coordinator.TryLaunchReadingView().Should().BeTrue();

        created.Should().HaveCount(2);
        created[0].Browse.Should().BeFalse("F5 opens a fullscreen show");
        created[1].Browse.Should().BeTrue("Reading View is the windowed browse view");
        closed.Should().ContainSingle().Which.Should().BeSameAs(created[0].Window);
        activated.Should().BeEmpty("no window of the requested mode existed to re-focus");
    }

    [Fact]
    public void TryLaunch_WhileReadingViewIsOpen_ReplacesItWithAFullscreenShow()
    {
        // The reverse direction: presenting to an audience must not leave the user in a windowed
        // view just because Reading View happened to be open.
        var editor = CreateEditor();
        var customShows = new SlideShowCustomShowSession(() => editor);
        var created = new List<(FakeWindow Window, bool Browse)>();
        var activated = new List<FakeWindow>();

        var coordinator = new SlideShowWindowLaunchCoordinator<FakeWindow>(
            customShows,
            () => editor.Presentation,
            () => null,
            editor.SetSlideNotesText,
            plan =>
            {
                var window = new FakeWindow { IsLive = true };
                created.Add((window, plan.ForceBrowseWindow));
                return window;
            },
            (_, _) => { },
            _ => { },
            window => window.IsLive,
            activated.Add,
            window => window.IsLive = false);

        coordinator.TryLaunchReadingView().Should().BeTrue();
        coordinator.TryLaunch(fromStart: true).Should().BeTrue();

        created.Should().HaveCount(2);
        created[1].Browse.Should().BeFalse("F5 must give a fullscreen show");
        activated.Should().BeEmpty();
    }

    [Fact]
    public void TryLaunchReadingView_Twice_ReusesTheReadingViewWindow()
    {
        // Reuse is still correct when the mode matches: the r188 duplicate-window fix must not be
        // undone by the mode check.
        var editor = CreateEditor();
        var customShows = new SlideShowCustomShowSession(() => editor);
        var created = new List<FakeWindow>();
        var activated = new List<FakeWindow>();

        var coordinator = new SlideShowWindowLaunchCoordinator<FakeWindow>(
            customShows,
            () => editor.Presentation,
            () => null,
            editor.SetSlideNotesText,
            _ =>
            {
                var window = new FakeWindow { IsLive = true };
                created.Add(window);
                return window;
            },
            (_, _) => { },
            _ => { },
            window => window.IsLive,
            activated.Add,
            window => window.IsLive = false);

        coordinator.TryLaunchReadingView().Should().BeTrue();
        coordinator.TryLaunchReadingView().Should().BeTrue();

        created.Should().HaveCount(1);
        activated.Should().ContainSingle().Which.Should().BeSameAs(created[0]);
    }

    [Fact]
    public void TryLaunch_WithATimingIntent_AppliesItToAReusedWindowToo()
    {
        // r190: Rehearse Timings and Record Timings are ribbon commands with no running-show gate,
        // so a user can invoke either while a show is already up. The reuse branch returned before
        // _setTimingIntent was called, so the button refocused the show and started no recording.
        var editor = CreateEditor();
        var customShows = new SlideShowCustomShowSession(() => editor);
        var created = new List<FakeWindow>();
        var applied = new List<SlideShowTimingIntent>();

        var coordinator = new SlideShowWindowLaunchCoordinator<FakeWindow>(
            customShows,
            () => editor.Presentation,
            () => null,
            editor.SetSlideNotesText,
            _ =>
            {
                var window = new FakeWindow { IsLive = true };
                created.Add(window);
                return window;
            },
            (_, intent) => applied.Add(intent),
            _ => { },
            window => window.IsLive,
            _ => { },
            window => window.IsLive = false);

        coordinator.TryLaunch(fromStart: true).Should().BeTrue();
        applied.Should().BeEmpty("the first launch asked for no timing intent");

        coordinator.TryLaunch(fromStart: true, SlideShowTimingIntent.RecordTimings).Should().BeTrue();

        created.Should().HaveCount(1, "the running show is still reused");
        applied.Should().Equal(SlideShowTimingIntent.RecordTimings);
    }

    [Fact]
    public void TryLaunch_ReusingAWindowWithNoTimingIntent_DoesNotDisturbIt()
    {
        // None means "no change", exactly as on the creation path -- reuse must not clear a
        // recording the user already started.
        var editor = CreateEditor();
        var customShows = new SlideShowCustomShowSession(() => editor);
        var applied = new List<SlideShowTimingIntent>();

        var coordinator = new SlideShowWindowLaunchCoordinator<FakeWindow>(
            customShows,
            () => editor.Presentation,
            () => null,
            editor.SetSlideNotesText,
            _ => new FakeWindow { IsLive = true },
            (_, intent) => applied.Add(intent),
            _ => { },
            window => window.IsLive,
            _ => { },
            window => window.IsLive = false);

        coordinator.TryLaunch(fromStart: true, SlideShowTimingIntent.RehearseTimings).Should().BeTrue();
        applied.Should().Equal(SlideShowTimingIntent.RehearseTimings);

        coordinator.TryLaunch(fromStart: true).Should().BeTrue();
        // None must not be pushed as a change: the reused window keeps the intent it already has.
        applied.Should().Equal(SlideShowTimingIntent.RehearseTimings);
    }

    [Fact]
    public void TryLaunch_FromADifferentStartSlide_ReplacesTheRunningShowInsteadOfRefocusingIt()
    {
        // r191. Every reuse test written in r188, r189 and r190 drove the coordinator with the SAME
        // input twice and asserted only how many windows were created and which was activated. The
        // route -- which show, which start slide, which animation step -- reaches a window ONLY
        // through the plan handed to createWindow, so the reuse branch discarded it and no
        // assertion in any of those tests could notice. This is the test that fails when it does.
        var editor = CreateEditor();
        var customShows = new SlideShowCustomShowSession(() => editor);
        var created = new List<SlideShowWindowLaunchPlan>();
        var activated = new List<FakeWindow>();

        var coordinator = new SlideShowWindowLaunchCoordinator<FakeWindow>(
            customShows,
            () => editor.Presentation,
            () => null,
            editor.SetSlideNotesText,
            plan =>
            {
                created.Add(plan);
                return new FakeWindow { IsLive = true };
            },
            (_, _) => { },
            _ => { },
            window => window.IsLive,
            activated.Add,
            window => window.IsLive = false);

        // Start on slide 2, then ask to present from the beginning: a different route.
        editor.SelectSlide(1);
        coordinator.TryLaunch(fromStart: false).Should().BeTrue();
        coordinator.TryLaunch(fromStart: true).Should().BeTrue();

        created.Should().HaveCount(2, "the second launch asks for different content");
        created[0].PlaybackRoute.StartIndex.Should().NotBe(created[1].PlaybackRoute.StartIndex);
        activated.Should().BeEmpty("no window showing the requested content existed to refocus");
    }

    [Fact]
    public void TryLaunch_WithTheSameRoute_StillReusesTheRunningShow()
    {
        // The route check must not defeat the r188 duplicate-window fix: an identical request still
        // refocuses rather than restarting the presentation.
        var editor = CreateEditor();
        var customShows = new SlideShowCustomShowSession(() => editor);
        var created = new List<SlideShowWindowLaunchPlan>();
        var activated = new List<FakeWindow>();

        var coordinator = new SlideShowWindowLaunchCoordinator<FakeWindow>(
            customShows,
            () => editor.Presentation,
            () => null,
            editor.SetSlideNotesText,
            plan =>
            {
                created.Add(plan);
                return new FakeWindow { IsLive = true };
            },
            (_, _) => { },
            _ => { },
            window => window.IsLive,
            activated.Add,
            window => window.IsLive = false);

        coordinator.TryLaunch(fromStart: true).Should().BeTrue();
        coordinator.TryLaunch(fromStart: true).Should().BeTrue();

        created.Should().HaveCount(1);
        activated.Should().ContainSingle();
    }

    private static EditingSession CreateEditor()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();
        presentation.Slides.Add(new Slide { Id = "slide-1", Title = "Opening" });
        presentation.Slides.Add(new Slide { Id = "slide-2", Title = "Details" });
        return new EditingSession(presentation, new PresentationCommandBus(presentation));
    }

    private sealed class FakeWindow
    {
        public bool IsLive { get; set; }
    }
}
