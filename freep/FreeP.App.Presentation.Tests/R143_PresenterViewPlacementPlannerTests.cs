using FreeP.App.Compositor;
using Xunit;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Round 143 / finding F3 (freep-slideshow-presenter lens, freep/FreeP.App.Host/SlideShowWindow.cs):
/// Presenter View previously had no multi-monitor placement logic at all -- it always centered
/// itself over the audience-facing slideshow window (CenterOwner), even on a dual-monitor rig
/// where a second display exists for it. These tests exercise the real production decision
/// (<see cref="PresenterViewPlacementPlanner.SelectPresenterScreen"/>) that now backs the
/// WPF host's and the Avalonia shell's Presenter View placement.
/// </summary>
public sealed class R143_PresenterViewPlacementPlannerTests
{
    private static readonly SlideShowScreenBounds Primary = new(0, 0, 1920, 1080, IsPrimary: true);
    private static readonly SlideShowScreenBounds Secondary = new(1920, 0, 1920, 1080, IsPrimary: false);
    private static readonly SlideShowScreenBounds Tertiary = new(3840, 0, 1920, 1080, IsPrimary: false);

    [Fact]
    public void SingleMonitor_ReturnsNull_SoCallerKeepsCenterOwnerFallback()
    {
        // Before the fix, this is effectively the ONLY case that ever existed: with a single
        // display there is nowhere else to put Presenter View, so the planner defers to the
        // caller's existing single-monitor fallback (CenterOwner) by returning null.
        var result = PresenterViewPlacementPlanner.SelectPresenterScreen(Primary, [Primary]);

        Assert.Null(result);
    }

    [Fact]
    public void SlideshowOnSecondary_TwoMonitors_PicksPrimaryForPresenterView()
    {
        // This is the exact bug scenario from the finding: full-screen playback (e.g. speaker
        // mode maximizing wherever the process happened to start, often the secondary/projector
        // display) with Presenter View toggled on. The presenter's own screen (primary, the
        // laptop panel) must be chosen -- not the screen the slideshow occupies.
        var result = PresenterViewPlacementPlanner.SelectPresenterScreen(Secondary, [Primary, Secondary]);

        Assert.Equal(Primary, result);
    }

    [Fact]
    public void SlideshowOnPrimary_TwoMonitors_PicksTheOtherScreen()
    {
        // Mirror image: if the slideshow itself is on the primary display, Presenter View must
        // move to the secondary display rather than also landing on the primary.
        var result = PresenterViewPlacementPlanner.SelectPresenterScreen(Primary, [Primary, Secondary]);

        Assert.Equal(Secondary, result);
    }

    [Fact]
    public void ThreeMonitors_SlideshowOnPrimary_PrefersFirstNonMatchingNonPrimaryScreen()
    {
        var result = PresenterViewPlacementPlanner.SelectPresenterScreen(Primary, [Primary, Secondary, Tertiary]);

        Assert.Equal(Secondary, result);
    }

    [Fact]
    public void ThreeMonitors_SlideshowOnNonPrimary_StillPrefersPrimaryOverAnotherNonPrimary()
    {
        // Even when there are two candidate "other" screens, the primary display wins over a
        // third non-primary one, since it is the display most likely to be in front of the
        // presenter (their own laptop) rather than a second projector/extended display.
        var result = PresenterViewPlacementPlanner.SelectPresenterScreen(Tertiary, [Primary, Secondary, Tertiary]);

        Assert.Equal(Primary, result);
    }

    [Fact]
    public void NullAllScreens_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => PresenterViewPlacementPlanner.SelectPresenterScreen(Primary, null!));
    }

    [Fact]
    public void SlideshowScreenNotInList_TwoOtherScreensExist_StillPicksAnOtherScreen()
    {
        // Defensive case: the slideshow's reported screen doesn't exactly match any entry in
        // allScreens (e.g. a stale/rounded reading). The planner should not throw or return the
        // slideshow's own (unmatched) bounds -- it should still pick a real, distinct display.
        var unknown = new SlideShowScreenBounds(9999, 9999, 1920, 1080, IsPrimary: false);

        var result = PresenterViewPlacementPlanner.SelectPresenterScreen(unknown, [Primary, Secondary]);

        Assert.Equal(Primary, result);
    }
}
