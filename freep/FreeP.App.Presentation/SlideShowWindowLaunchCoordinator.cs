using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Coordinates portable slideshow launch policy while leaving native window creation,
/// ownership, focus, and presentation to the renderer host.
/// </summary>
public sealed class SlideShowWindowLaunchCoordinator<TWindow>
    where TWindow : class
{
    private readonly SlideShowCustomShowSession _customShows;
    private readonly Func<Presentation> _getPresentation;
    private readonly Func<int?> _getSelectedCaptionTrackIndex;
    private readonly Action<int, string?> _setSlideNotesText;
    private readonly Func<SlideShowWindowLaunchPlan, TWindow> _createWindow;
    private readonly Action<TWindow, SlideShowTimingIntent> _setTimingIntent;
    private readonly Action<TWindow> _showWindow;
    private readonly Func<TWindow, bool> _isWindowLive;
    private readonly Action<TWindow> _activateWindow;
    private readonly Action<TWindow> _closeWindow;

    // r188: the show this coordinator most recently opened, or null once it has closed. Without it
    // every launch built a NEW full-screen window: pressing F5 twice, or clicking Play while a show
    // was already running, left two presentations stacked on top of each other, each with its own
    // playback position, and closing the top one revealed the second still running. PowerPoint
    // re-uses the running show. The liveness probe and activation are supplied by the host rather
    // than inferred here because only the renderer knows whether its window is still on screen --
    // and they are REQUIRED, not optional, so a shell cannot quietly opt back into the old
    // behaviour by omitting them.
    private TWindow? _liveWindow;

    // r189: whether _liveWindow was opened as the windowed browse/Reading View rather than a
    // fullscreen show. Reuse requires this to match what the caller is asking for.
    private bool _liveWindowIsBrowseWindow;

    // r191: the route the live window was opened with, so a launch asking for different content
    // replaces it rather than silently refocusing the old one.
    private SlideShowPlaybackRoute? _liveWindowRoute;

    public SlideShowWindowLaunchCoordinator(
        SlideShowCustomShowSession customShows,
        Func<Presentation> getPresentation,
        Func<int?> getSelectedCaptionTrackIndex,
        Action<int, string?> setSlideNotesText,
        Func<SlideShowWindowLaunchPlan, TWindow> createWindow,
        Action<TWindow, SlideShowTimingIntent> setTimingIntent,
        Action<TWindow> showWindow,
        Func<TWindow, bool> isWindowLive,
        Action<TWindow> activateWindow,
        Action<TWindow> closeWindow)
    {
        _isWindowLive = isWindowLive ?? throw new ArgumentNullException(nameof(isWindowLive));
        _activateWindow = activateWindow ?? throw new ArgumentNullException(nameof(activateWindow));
        _closeWindow = closeWindow ?? throw new ArgumentNullException(nameof(closeWindow));
        _customShows = customShows ?? throw new ArgumentNullException(nameof(customShows));
        _getPresentation = getPresentation ?? throw new ArgumentNullException(nameof(getPresentation));
        _getSelectedCaptionTrackIndex = getSelectedCaptionTrackIndex
            ?? throw new ArgumentNullException(nameof(getSelectedCaptionTrackIndex));
        _setSlideNotesText = setSlideNotesText ?? throw new ArgumentNullException(nameof(setSlideNotesText));
        _createWindow = createWindow ?? throw new ArgumentNullException(nameof(createWindow));
        _setTimingIntent = setTimingIntent ?? throw new ArgumentNullException(nameof(setTimingIntent));
        _showWindow = showWindow ?? throw new ArgumentNullException(nameof(showWindow));
    }

    public bool TryLaunch(
        bool fromStart,
        SlideShowTimingIntent timingIntent = SlideShowTimingIntent.None,
        int? animationStartIndex = null)
    {
        if (!_customShows.TryBuildPlaybackLaunch(
                fromStart,
                animationStartIndex,
                _getSelectedCaptionTrackIndex(),
                out var playback))
        {
            return false;
        }

        Launch(playback, timingIntent);
        return true;
    }

    public bool TryLaunchNamed(string? customShowName, int startIndex = 0)
    {
        if (!_customShows.TryBuildNamedPlaybackLaunch(
                customShowName,
                startIndex,
                _getSelectedCaptionTrackIndex(),
                out var playback))
        {
            return false;
        }

        Launch(playback, SlideShowTimingIntent.None);
        return true;
    }

    public bool TryLaunchReadingView()
    {
        if (!_customShows.TryBuildPlaybackLaunch(
                fromStart: false,
                animationStartIndex: null,
                _getSelectedCaptionTrackIndex(),
                out var playback))
        {
            return false;
        }

        Launch(playback, SlideShowTimingIntent.None, forceBrowseWindow: true);
        return true;
    }

    /// <summary>
    /// Whether two launch routes address the same content. <see cref="SlideShowPlaybackRoute"/> is a
    /// class with reference equality, so the identifying fields are compared: which custom show
    /// (or none), which slides of the deck it resolved to, and where playback starts. A null
    /// <paramref name="live"/> means nothing is on screen, which never matches.
    /// </summary>
    private static bool RouteMatches(SlideShowPlaybackRoute? live, SlideShowPlaybackRoute requested)
    {
        if (live is null)
            return false;

        return string.Equals(live.CustomShowName, requested.CustomShowName, StringComparison.Ordinal)
            && live.StartIndex == requested.StartIndex
            && live.AnimationStartIndex == requested.AnimationStartIndex
            && live.SourceSlideIndices.SequenceEqual(requested.SourceSlideIndices);
    }

    private void Launch(
        SlideShowPlaybackLaunchPlan playback,
        SlideShowTimingIntent timingIntent,
        bool forceBrowseWindow = false)
    {
        // A show already on screen is brought forward instead of being duplicated. The stale
        // reference is dropped first so a window the user closed does not block the next launch.
        //
        // r189: the reuse is conditional on the MODE matching. The r188 version of this check
        // looked only at liveness, so requesting Reading View while a fullscreen show was running
        // re-focused the fullscreen window and dropped the request -- and pressing F5 during
        // Reading View kept the user in a windowed view instead of going fullscreen for an
        // audience. A window in the wrong mode is not the window the user asked for, so it is
        // closed and replaced rather than activated.
        if (_liveWindow is { } running)
        {
            if (_isWindowLive(running))
            {
                // r191: the ROUTE has to match as well as the mode. The route (which custom show,
                // which start slide, which animation step) only ever reaches a window through the
                // launch plan handed to _createWindow, so a reuse that skipped straight to
                // _activateWindow discarded it: picking a different custom show while one was
                // already presenting refocused the running show and left the audience on the old
                // deck, while the presenter believed they had switched. A window showing different
                // content is not the window the user asked for, so it is replaced -- the same
                // treatment a mode mismatch already gets.
                if (_liveWindowIsBrowseWindow == forceBrowseWindow
                    && RouteMatches(_liveWindowRoute, playback.Route))
                {
                    // r190: the reused window still has to receive the timing intent. Rehearse
                    // Timings and Record Timings are ordinary ribbon commands with no
                    // running-show gate, so invoking either while a show is up took this branch
                    // and returned before _setTimingIntent was ever called -- the button
                    // refocused the show and started no recording, silently. Applying it here is
                    // what the creation path below does; reuse must not be the cheaper path that
                    // quietly does less.
                    if (timingIntent != SlideShowTimingIntent.None)
                        _setTimingIntent(running, timingIntent);

                    _activateWindow(running);
                    return;
                }

                _closeWindow(running);
            }

            _liveWindow = null;
            _liveWindowRoute = null;
        }

        var caption = playback.CaptionSelection;
        var launchPlan = new SlideShowWindowLaunchPlan(
            _getPresentation(),
            playback.Route,
            SetSlideNotesText: _setSlideNotesText,
            PreferredCaptionSlideIndex: caption?.SlideIndex,
            PreferredCaptionShapeId: caption?.ShapeId,
            PreferredCaptionTrackIndex: caption?.TrackIndex,
            ForceBrowseWindow: forceBrowseWindow);
        var window = _createWindow(launchPlan);
        if (timingIntent != SlideShowTimingIntent.None)
            _setTimingIntent(window, timingIntent);
        _liveWindow = window;
        _liveWindowIsBrowseWindow = forceBrowseWindow;
        _liveWindowRoute = playback.Route;
        _showWindow(window);
    }
}
