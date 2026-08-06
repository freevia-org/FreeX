using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LibVLCSharp.Avalonia;
using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.App.Media;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

/// <summary>
/// Avalonia slideshow adapter for the shared LibVLC playback engine.
/// Poster/click behavior remains deterministic when native LibVLC is unavailable.
/// </summary>
internal sealed class AvaloniaSlideShowMediaController
{
    private sealed class MediaSlot
    {
        public required uint ShapeId { get; init; }
        public required IMediaPlaybackSession Session { get; init; }
        public required MediaInfo Media { get; init; }
        public required bool ShowWhenStopped { get; init; }
        public required LayoutRect AuthoredBounds { get; init; }
        public required bool PlayFullScreen { get; init; }
        public required int BaseVolumePercent { get; set; }
        public required int RemainingSlides { get; set; }
        public VideoView? VideoView { get; init; }
        public PresentationMediaTranscriptTrackDescriptor? CaptionTrack { get; set; }
        public Border? CaptionHost { get; set; }
        public TextBlock? CaptionText { get; set; }
    }

    private readonly Panel _overlay;
    private readonly IMediaPlaybackBackendFactory _backendFactory;
    private readonly List<MediaSlot> _slots = new();
    private readonly DispatcherTimer _captionTimer;
    private IMediaPlaybackBackend? _backend;
    private IMediaPlaybackSession? _transitionSoundSession;
    private IReadOnlyList<SlideShowMediaShapePlan> _active = Array.Empty<SlideShowMediaShapePlan>();
    private Slide? _activeSlide;
    private int? _activeSlideIndex;
    private bool _showMediaControls = true;
    private bool _showNarration = true;
    private double _slideDipW;
    private double _slideDipH;
    private double _canvasW;
    private double _canvasH;

    public AvaloniaSlideShowMediaController(
        Panel overlay,
        IMediaPlaybackBackendFactory? backendFactory = null)
    {
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        _backendFactory = backendFactory ?? new LibVlcMediaPlaybackBackendFactory();
        _captionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        _captionTimer.Tick += (_, _) =>
        {
            EnforceTrimWindows();
            UpdateCaptions();
        };
    }

    public IReadOnlyList<SlideShowMediaShapePlan> Active => _active;
    public MediaPlaybackBackendAvailability? Availability { get; private set; }
    public MediaPlaybackFailure? LastFailure { get; private set; }
    public SlideShowMediaClickPlan LastClick { get; private set; } = SlideShowMediaClickPlan.NotMedia;

    internal string? CaptionTextForTest(uint shapeId) =>
        _slots.FirstOrDefault(slot => slot.ShapeId == shapeId)?.CaptionText?.Text;

    internal void RefreshCaptionsForTest() => UpdateCaptions();

    public void SetCanvasBounds(double canvasW, double canvasH)
    {
        _canvasW = canvasW;
        _canvasH = canvasH;
        _overlay.Width = Math.Max(1, canvasW);
        _overlay.Height = Math.Max(1, canvasH);
    }

    /// <summary>
    /// Repositions active media and caption overlays after the slideshow canvas changes size.
    /// The shared planner owns the letterbox calculation used here and by click hit-testing.
    /// </summary>
    public void UpdateLayout(
        Slide slide,
        double slideDipW,
        double slideDipH,
        double canvasW,
        double canvasH)
    {
        ArgumentNullException.ThrowIfNull(slide);
        if (_activeSlide is not null && !ReferenceEquals(_activeSlide, slide))
        {
            Teardown();
            SetCanvasBounds(canvasW, canvasH);
            return;
        }

        SetCanvasBounds(canvasW, canvasH);
        _slideDipW = slideDipW;
        _slideDipH = slideDipH;

        foreach (var slot in _slots)
        {
            var shape = ShapeTreeLookup.Find(slide, slot.ShapeId);
            if (shape?.Media is null || shape.Kind != SlideShapeKind.Media)
                continue;

            var bounds = SlideShowMediaInteractionPlanner.ComputeMediaBounds(
                shape,
                slideDipW,
                slideDipH,
                canvasW,
                canvasH);
            if (slot.PlayFullScreen && slot.Session.State == MediaPlaybackState.Playing)
                bounds = FullScreenBounds();
            if (slot.VideoView is not null)
            {
                slot.VideoView.Width = Math.Max(1, bounds.Width);
                slot.VideoView.Height = Math.Max(1, bounds.Height);
                Canvas.SetLeft(slot.VideoView, bounds.X);
                Canvas.SetTop(slot.VideoView, bounds.Y);
            }

            if (slot.CaptionHost is not null && slot.CaptionText is not null)
            {
                var cue = PresentationMediaTranscriptPlanner.FindActiveCue(
                    slot.CaptionTrack,
                    slot.Session.Position);
                ApplyCaptionPlacement(slot.CaptionHost, slot.CaptionText, bounds, cue);
            }
        }
    }

    public void EnterSlide(
        Slide slide,
        double slideDipW,
        double slideDipH,
        double canvasW,
        double canvasH,
        IReadOnlyList<PresentationMediaTranscriptTrackDescriptor>? captionTracks = null,
        uint? preferredCaptionShapeId = null,
        int? preferredCaptionTrackIndex = null,
        int? captionSlideIndex = null,
        int? preferredCaptionSlideIndex = null,
        bool showMediaControls = true,
        bool showNarration = true,
        int? presentationSlideIndex = null)
    {
        ArgumentNullException.ThrowIfNull(slide);
        SetCanvasBounds(canvasW, canvasH);
        _slideDipW = slideDipW;
        _slideDipH = slideDipH;
        var continues = _activeSlideIndex is int previous
            && presentationSlideIndex is int current
            && current == previous + 1;
        if (continues)
            RetainAcrossSlide();
        else
            TeardownPlayback();
        _activeSlide = slide;
        _activeSlideIndex = presentationSlideIndex;
        _showMediaControls = showMediaControls;
        _showNarration = showNarration;
        _active = SlideShowMediaInteractionPlanner.BuildSlidePlan(
            slide, slideDipW, slideDipH, canvasW, canvasH, showMediaControls, showNarration);

        if (!_active.Any(plan => plan.HasSource) || !EnsureBackend())
            return;

        foreach (var shape in ShapeTreeLookup.Enumerate(slide).Where(shape =>
                     shape.Kind == SlideShapeKind.Media
                     && shape.Media is not null
                     && (_showNarration || shape.Media.IsVideo)))
        {
            var plan = _active.First(media => media.ShapeId == shape.Id);
            if (!plan.HasSource || !MediaPlaybackSourceFactory.TryCreate(
                    shape.Media!.Bytes,
                    shape.Media.LinkUrl,
                    shape.Media.ContentType,
                    shape.Media.IsVideo,
                    out var source,
                    loop: shape.Media.Loop))
                continue;

            try
            {
                var session = _backend!.CreateSession();
                var baseVolumePercent = SlideShowMediaInteractionPlanner.NormalizeVolumePercent(shape.Media.VolumePercent);
                session.Failed += OnSessionFailed;
                session.Open(source!);
                SeekToTrimStart(session, shape.Media);
                ApplyFade(session, shape.Media, baseVolumePercent);
                VideoView? view = null;
                if (shape.Media.IsVideo && session is LibVlcMediaPlaybackSession)
                {
                    view = CreateVideoView(
                        (LibVlcMediaPlaybackSession)session,
                        shape.Media.PlayFullScreen &&
                        shape.Media.PlaybackStartMode == MediaPlaybackStartMode.Automatically
                            ? FullScreenBounds()
                            : plan.Bounds);
                }
                session.Ended += (_, _) =>
                {
                    var endAction = SlideShowMediaInteractionPlanner.ResolveEndAction(shape.Media!);
                    if (endAction == SlideShowMediaEndAction.Rewind)
                    {
                        SeekToTrimStart(session, shape.Media!);
                        ApplyFade(session, shape.Media!, baseVolumePercent);
                        session.Pause();
                        if (view is not null)
                        {
                            ApplyVideoViewBounds(view, plan.Bounds);
                            view.IsVisible = shape.Media.ShowWhenStopped;
                        }
                    }
                    else if (endAction == SlideShowMediaEndAction.Stop &&
                             view is not null && !shape.Media!.ShowWhenStopped)
                    {
                        ApplyVideoViewBounds(view, plan.Bounds);
                        view.IsVisible = false;
                    }
                };

                var captionTrack = captionSlideIndex is int currentSlideIndex
                    ? PresentationMediaTranscriptPlanner.SelectPlaybackTrack(
                        captionTracks,
                        currentSlideIndex,
                        shape.Id,
                        preferredCaptionSlideIndex,
                        preferredCaptionShapeId == shape.Id ? preferredCaptionTrackIndex : null)
                    : PresentationMediaTranscriptPlanner.SelectPlaybackTrack(
                        captionTracks,
                        shape.Id,
                        preferredCaptionShapeId == shape.Id ? preferredCaptionTrackIndex : null);
                Border? captionHost = null;
                TextBlock? captionText = null;
                if (captionTrack is not null)
                {
                    (captionHost, captionText) = CreateCaptionView(plan.Bounds);
                }

                _slots.Add(new MediaSlot
                {
                    ShapeId = shape.Id,
                    Session = session,
                    Media = shape.Media,
                    ShowWhenStopped = shape.Media.ShowWhenStopped,
                    BaseVolumePercent = baseVolumePercent,
                    RemainingSlides = Math.Max(1, shape.Media.StopAfterSlides),
                    AuthoredBounds = plan.Bounds,
                    PlayFullScreen = shape.Media.PlayFullScreen,
                    VideoView = view,
                    CaptionTrack = captionTrack,
                    CaptionHost = captionHost,
                    CaptionText = captionText,
                });
                if (shape.Media.PlaybackStartMode == MediaPlaybackStartMode.Automatically)
                {
                    StartPlayback(session, shape.Media, baseVolumePercent);
                    if (view is not null)
                        view.IsVisible = true;
                }
                else if (view is not null && !shape.Media.ShowWhenStopped)
                {
                    view.IsVisible = false;
                }
            }
            catch (Exception ex)
            {
                LastFailure = new MediaPlaybackFailure(
                    MediaPlaybackFailureKind.EngineError,
                    "The Avalonia media adapter could not start the media source.",
                    ex);
            }
        }

        _captionTimer.IsEnabled = _slots.Any(slot =>
            slot.CaptionTrack is not null || HasPlaybackEnvelope(slot.Media));
        UpdateCaptions();
    }

    public bool TryHandleClick(
        Slide slide,
        double slideDipW,
        double slideDipH,
        double canvasW,
        double canvasH,
        double canvasX,
        double canvasY)
    {
        LastClick = SlideShowMediaInteractionPlanner.PlanClick(
            slide,
            slideDipW,
            slideDipH,
            canvasW,
            canvasH,
            canvasX,
            canvasY,
            _showMediaControls,
            _showNarration);
        if (!LastClick.IsHandled)
            return false;

        var slot = _slots.FirstOrDefault(candidate =>
            candidate.ShapeId == LastClick.Media!.ShapeId);
        if (slot is null)
            return true;

        if (slot.Session.State == MediaPlaybackState.Playing)
        {
            slot.Session.Pause();
            if (slot.VideoView is not null)
                ApplyVideoViewBounds(slot.VideoView, slot.AuthoredBounds);
            if (slot.VideoView is not null && !slot.ShowWhenStopped)
                slot.VideoView.IsVisible = false;
        }
        else
        {
            if (slot.PlayFullScreen && slot.VideoView is not null)
                ApplyVideoViewBounds(slot.VideoView, FullScreenBounds());
            StartPlayback(slot.Session, slot.Media, slot.BaseVolumePercent);
            if (slot.VideoView is not null)
                slot.VideoView.IsVisible = true;
        }
        return true;
    }

    public bool TrySeek(uint shapeId, TimeSpan position)
    {
        if (position < TimeSpan.Zero)
            return false;

        var slot = _slots.FirstOrDefault(candidate => candidate.ShapeId == shapeId);
        if (slot is null)
            return false;

        var window = SlideShowMediaInteractionPlanner.ResolveTrimWindow(
            slot.Media,
            slot.Session.Duration);
        var bounded = window.End != TimeSpan.MaxValue && position > window.End
            ? window.End
            : SlideShowMediaInteractionPlanner.ClampToTrimStart(slot.Media, position);
        var didSeek = slot.Session.Seek(bounded);
        if (didSeek)
            ApplyFade(slot.Session, slot.Media, slot.BaseVolumePercent);
        return didSeek;
    }

    /// <summary>Seeks an active media session to a named authored bookmark.</summary>
    public bool TrySeekToBookmark(uint shapeId, string bookmarkName)
    {
        var slot = _slots.FirstOrDefault(candidate => candidate.ShapeId == shapeId);
        if (slot is null || !SlideShowMediaInteractionPlanner.TryResolveMediaBookmarkPosition(
                slot.Media, bookmarkName, slot.Session.Duration, out var position))
            return false;

        var didSeek = slot.Session.Seek(position);
        if (didSeek)
            ApplyFade(slot.Session, slot.Media, slot.BaseVolumePercent);
        return didSeek;
    }

    public bool TrySetVolume(uint shapeId, int volume)
    {
        var slot = _slots.FirstOrDefault(candidate => candidate.ShapeId == shapeId);
        if (slot is null) return false;
        slot.BaseVolumePercent = SlideShowMediaInteractionPlanner.NormalizeVolumePercent(volume);
        ApplyFade(slot.Session, slot.Media, slot.BaseVolumePercent);
        return true;
    }

    public bool PlayTransitionSound(TransitionSound sound)
    {
        ArgumentNullException.ThrowIfNull(sound);
        if (!MediaPlaybackSourceFactory.TryCreate(
                sound.AudioBytes,
                null,
                sound.ContentType,
                isVideo: false,
                out var source,
                loop: sound.Loop) || !EnsureBackend())
            return false;

        _transitionSoundSession?.Dispose();
        _transitionSoundSession = _backend!.CreateSession();
        _transitionSoundSession.Failed += OnSessionFailed;
        _transitionSoundSession.Open(source!);
        _transitionSoundSession.Play();
        return true;
    }

    public void Teardown()
    {
        TeardownPlayback();
        _active = Array.Empty<SlideShowMediaShapePlan>();
        LastClick = SlideShowMediaClickPlan.NotMedia;
        Availability = null;
        LastFailure = null;
    }

    private bool EnsureBackend()
    {
        if (_backend is not null)
            return true;

        if (!_backendFactory.TryCreate(out _backend, out var failure))
        {
            LastFailure = failure;
            Availability = new MediaPlaybackBackendAvailability(
                false,
                new MediaPlaybackCapabilities(
                    false, false, false, false, false, "LibVLC", failure?.Message),
                failure?.Message);
            return false;
        }

        Availability = new MediaPlaybackBackendAvailability(true, _backend!.Capabilities);
        return true;
    }

    private VideoView CreateVideoView(
        LibVlcMediaPlaybackSession session,
        Free.Shared.Drawing.LayoutRect bounds)
    {
        var view = new VideoView
        {
            MediaPlayer = session.NativePlayer,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Width = Math.Max(1, bounds.Width),
            Height = Math.Max(1, bounds.Height),
        };
        Canvas.SetLeft(view, bounds.X);
        Canvas.SetTop(view, bounds.Y);
        _overlay.Children.Add(view);
        return view;
    }

    private (Border Host, TextBlock Text) CreateCaptionView(LayoutRect bounds)
    {
        var text = new TextBlock
        {
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 4),
        };
        var height = Math.Clamp(bounds.Height * 0.2, 36, 86);
        var host = new Border
        {
            Background = Brushes.Black,
            Opacity = 0.82,
            Width = Math.Max(1, bounds.Width),
            Height = height,
            Child = text,
            IsVisible = false,
            IsHitTestVisible = false,
            ZIndex = 10,
        };
        ApplyCaptionPlacement(host, text, bounds, cue: null);
        _overlay.Children.Add(host);
        return (host, text);
    }

    private static void ApplyCaptionPlacement(
        Border host,
        TextBlock text,
        LayoutRect bounds,
        PresentationMediaTranscriptCueDescriptor? cue)
    {
        var defaultHeight = Math.Clamp(bounds.Height * 0.2, 36, 86);
        var placement = PresentationMediaTranscriptPlanner.ComputeCaptionPlacement(
            cue,
            bounds.Width,
            bounds.Height,
            defaultHeight);
        host.Width = placement.Width;
        host.Height = placement.Height;
        var isVertical = placement.RotationDegrees != 0;
        text.Width = isVertical ? placement.Height : double.NaN;
        text.Height = isVertical ? placement.Width : double.NaN;
        text.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        text.RenderTransform = isVertical
            ? new RotateTransform(placement.RotationDegrees)
            : null;
        Canvas.SetLeft(host, bounds.X + placement.X);
        Canvas.SetTop(host, bounds.Y + placement.Y);
    }

    private void UpdateCaptions()
    {
        foreach (var slot in _slots)
        {
            if (slot.CaptionHost is null || slot.CaptionText is null)
                continue;

            var cue = PresentationMediaTranscriptPlanner.FindActiveCue(
                slot.CaptionTrack,
                slot.Session.Position);
            ApplyCaptionText(slot.CaptionText, cue);
            if (cue is not null
                && _activeSlide is { } activeSlide
                && ShapeTreeLookup.Find(activeSlide, slot.ShapeId) is { Media: not null } shape)
            {
                var bounds = SlideShowMediaInteractionPlanner.ComputeMediaBounds(
                    shape,
                    _slideDipW,
                    _slideDipH,
                    _canvasW,
                    _canvasH);
                if (slot.PlayFullScreen && slot.Session.State == MediaPlaybackState.Playing)
                    bounds = FullScreenBounds();
                ApplyCaptionPlacement(slot.CaptionHost, slot.CaptionText, bounds, cue);
            }
            slot.CaptionHost.IsVisible = cue is not null;
        }
    }

    private static void ApplyCaptionText(
        TextBlock text,
        PresentationMediaTranscriptCueDescriptor? cue)
    {
        text.Inlines?.Clear();
        if (cue is null)
        {
            text.Text = string.Empty;
            return;
        }

        if (cue.Spans.Count == 0)
        {
            text.Text = cue.Text;
            return;
        }

        text.Text = null;
        foreach (var span in cue.Spans)
        {
            var run = new global::Avalonia.Controls.Documents.Run
            {
                Text = span.Text,
                FontWeight = span.Bold ? FontWeight.Bold : FontWeight.Normal,
                FontStyle = span.Italic ? FontStyle.Italic : FontStyle.Normal,
                TextDecorations = span.Underline ? TextDecorations.Underline : null,
                Foreground = CaptionBrush(span.ForegroundColorHex),
                Background = CaptionBrush(span.BackgroundColorHex)
            };
            if (!string.IsNullOrWhiteSpace(span.FontFamily))
            {
                run.FontFamily = span.FontFamily;
            }
            if (span.FontSizePx is { } fontSizePx)
            {
                run.FontSize = fontSizePx;
            }
            text.Inlines?.Add(run);
        }
    }

    private static IBrush? CaptionBrush(string? colorHex)
    {
        if (string.IsNullOrWhiteSpace(colorHex))
        {
            return null;
        }

        try
        {
            return new SolidColorBrush(Color.Parse("#" + colorHex));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private void TeardownPlayback()
    {
        foreach (var slot in _slots)
            DisposeSlot(slot);
        _slots.Clear();
        _activeSlide = null;
        _activeSlideIndex = null;
        _captionTimer.Stop();

        _transitionSoundSession?.Dispose();
        _transitionSoundSession = null;
        _backend?.Dispose();
        _backend = null;
    }

    private void RetainAcrossSlide()
    {
        for (var i = _slots.Count - 1; i >= 0; i--)
        {
            var slot = _slots[i];
            if (slot.Media.IsVideo || slot.RemainingSlides <= 1)
            {
                DisposeSlot(slot);
                _slots.RemoveAt(i);
                continue;
            }

            if (slot.CaptionHost is not null)
                _overlay.Children.Remove(slot.CaptionHost);
            slot.RemainingSlides--;
            slot.CaptionTrack = null;
            slot.CaptionHost = null;
            slot.CaptionText = null;
        }
    }

    private void DisposeSlot(MediaSlot slot)
    {
        try { slot.Session.Stop(); } catch { }
        slot.Session.Failed -= OnSessionFailed;
        slot.Session.Dispose();
        if (slot.VideoView is not null)
            _overlay.Children.Remove(slot.VideoView);
        if (slot.CaptionHost is not null)
            _overlay.Children.Remove(slot.CaptionHost);
    }

    private void OnSessionFailed(object? sender, MediaPlaybackFailure failure) => LastFailure = failure;

    private void EnforceTrimWindows()
    {
        foreach (var slot in _slots)
        {
            if (slot.Session.State != MediaPlaybackState.Playing)
                continue;

            ApplyFade(slot.Session, slot.Media, slot.BaseVolumePercent);
            if (!SlideShowMediaInteractionPlanner.IsAtOrPastTrimEnd(
                    slot.Media, slot.Session.Position, slot.Session.Duration))
                continue;

            switch (SlideShowMediaInteractionPlanner.ResolveEndAction(slot.Media))
            {
                case SlideShowMediaEndAction.Loop:
                    StartPlayback(slot.Session, slot.Media, slot.BaseVolumePercent);
                    break;
                case SlideShowMediaEndAction.Rewind:
                    SeekToTrimStart(slot.Session, slot.Media);
                    ApplyFade(slot.Session, slot.Media, slot.BaseVolumePercent);
                    slot.Session.Pause();
                    if (slot.VideoView is not null)
                    {
                        ApplyVideoViewBounds(slot.VideoView, slot.AuthoredBounds);
                        slot.VideoView.IsVisible = slot.ShowWhenStopped;
                    }
                    break;
                default:
                    slot.Session.Pause();
                    if (slot.VideoView is not null)
                        ApplyVideoViewBounds(slot.VideoView, slot.AuthoredBounds);
                    if (slot.VideoView is not null && !slot.ShowWhenStopped)
                        slot.VideoView.IsVisible = false;
                    break;
            }
        }
    }

    private static bool HasPlaybackEnvelope(MediaInfo media) =>
        HasPositiveTiming(media.TrimStartMilliseconds) ||
        HasPositiveTiming(media.TrimEndMilliseconds) ||
        HasPositiveTiming(media.FadeInMilliseconds) ||
        HasPositiveTiming(media.FadeOutMilliseconds);

    private static bool HasPositiveTiming(double value) => value > 0 && double.IsFinite(value);

    private LayoutRect FullScreenBounds() =>
        new(0, 0, Math.Max(1, _canvasW), Math.Max(1, _canvasH));

    private static void ApplyVideoViewBounds(VideoView view, LayoutRect bounds)
    {
        view.Width = Math.Max(1, bounds.Width);
        view.Height = Math.Max(1, bounds.Height);
        Canvas.SetLeft(view, bounds.X);
        Canvas.SetTop(view, bounds.Y);
    }

    private static void StartPlayback(IMediaPlaybackSession session, MediaInfo media, int baseVolumePercent)
    {
        SeekToTrimStart(session, media);
        ApplyFade(session, media, baseVolumePercent);
        session.Play();
    }

    private static void ApplyFade(IMediaPlaybackSession session, MediaInfo media, int baseVolumePercent)
    {
        session.Volume = SlideShowMediaInteractionPlanner.ComputeEffectiveVolumePercent(
            media,
            baseVolumePercent,
            session.Position,
            session.Duration);
    }

    private static void SeekToTrimStart(IMediaPlaybackSession session, MediaInfo media)
    {
        var position = session.Position;
        var window = SlideShowMediaInteractionPlanner.ResolveTrimWindow(
            media, session.Duration);
        if (window.End != TimeSpan.MaxValue && position >= window.End)
            session.Seek(window.Start);
        else if (position < window.Start)
            session.Seek(window.Start);
    }
}
