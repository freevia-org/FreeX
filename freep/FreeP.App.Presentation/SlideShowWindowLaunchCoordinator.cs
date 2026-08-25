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

    public SlideShowWindowLaunchCoordinator(
        SlideShowCustomShowSession customShows,
        Func<Presentation> getPresentation,
        Func<int?> getSelectedCaptionTrackIndex,
        Action<int, string?> setSlideNotesText,
        Func<SlideShowWindowLaunchPlan, TWindow> createWindow,
        Action<TWindow, SlideShowTimingIntent> setTimingIntent,
        Action<TWindow> showWindow)
    {
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

    private void Launch(
        SlideShowPlaybackLaunchPlan playback,
        SlideShowTimingIntent timingIntent,
        bool forceBrowseWindow = false)
    {
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
        _showWindow(window);
    }
}
