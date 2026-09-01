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
            activated.Add);

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
            _ => { });

        coordinator.TryLaunch(fromStart: true).Should().BeTrue();
        created.Should().HaveCount(1);

        created[0].IsLive = false; // the user closed the show

        coordinator.TryLaunch(fromStart: true).Should().BeTrue();
        created.Should().HaveCount(2, "the previous show is gone, so a new one must open");
    }

    [Fact]
    public void TryLaunchReadingView_WhileAShowIsRunning_AlsoReusesIt()
    {
        // Reading view goes through the same Launch path, so it must not open a second window
        // either -- the duplicate-window bug was in Launch, not in any one entry point.
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
            activated.Add);

        coordinator.TryLaunch(fromStart: true).Should().BeTrue();
        coordinator.TryLaunchReadingView().Should().BeTrue();

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
