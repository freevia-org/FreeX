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
        public required SlideShowMediaPlaybackHandle Playback { get; init; }
        public required LayoutRect AuthoredBounds { get; init; }
        public VideoView? VideoView { get; init; }
        public PresentationMediaTranscriptTrackDescriptor? CaptionTrack { get; set; }
        public Border? CaptionHost { get; set; }
        public TextBlock? CaptionText { get; set; }
    }

    private readonly Panel _overlay;
    private readonly IMediaPlaybackBackendFactory _backendFactory;
    private readonly List<MediaSlot> _slots = new();
    private readonly SlideShowMediaPlaybackSession _playbackSession = new();
    private readonly DispatcherTimer _captionTimer;
    private IMediaPlaybackBackend? _backend;
    private IMediaPlaybackSession? _transitionSoundSession;
    private IReadOnlyList<SlideShowMediaShapePlan> _active = Array.Empty<SlideShowMediaShapePlan>();
    private Slide? _activeSlide;
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
            EnforcePlaybackState();
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
            var shape = SlideShapeTraversal.FindById(slide, slot.ShapeId);
            if (shape?.Media is null || shape.Kind != SlideShapeKind.Media)
                continue;

            var bounds = SlideShowMediaInteractionPlanner.ComputeMediaBounds(
                shape,
                slideDipW,
                slideDipH,
                canvasW,
                canvasH);
            var useFullScreen = _playbackSession.Snapshot(slot.Playback).UseFullScreen;
            var cue = PresentationMediaTranscriptPlanner.FindActiveCue(
                slot.CaptionTrack,
                slot.Session.Position);
            var placement = PresentationMediaTranscriptPlanner.PlanOverlayPlacement(
                new PresentationMediaOverlayPlacementRequest(
                    bounds,
                    canvasW,
                    canvasH,
                    useFullScreen,
                    cue,
                    slot.CaptionTrack?.Regions));
            if (slot.VideoView is not null)
                ApplyVideoViewBounds(slot.VideoView, placement.MediaBounds);

            if (slot.CaptionHost is not null && slot.CaptionText is not null)
                ApplyCaptionPlacement(slot.CaptionHost, slot.CaptionText, placement);
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
        var enterResult = _playbackSession.EnterSlide(presentationSlideIndex);
        ApplyEnterResult(enterResult);
        if (!enterResult.IsContiguous)
            ReleaseBackend();
        _activeSlide = slide;
        _showMediaControls = showMediaControls;
        _showNarration = showNarration;
        var entryPlan = SlideShowMediaInteractionPlanner.PlanSlideEntry(
            slide,
            slideDipW,
            slideDipH,
            canvasW,
            canvasH,
            captionTracks,
            preferredCaptionShapeId,
            preferredCaptionTrackIndex,
            captionSlideIndex,
            preferredCaptionSlideIndex,
            showMediaControls,
            showNarration);
        _active = entryPlan.Active;

        if (!entryPlan.HasPlayableSource || !EnsureBackend())
        {
            RefreshPlaybackTimer();
            return;
        }

        foreach (var entry in entryPlan.Items)
        {
            var shape = entry.Shape;
            var plan = entry.Surface;
            if (!plan.HasSource || !MediaPlaybackSourceFactory.TryCreate(
                    entry.Media.Bytes,
                    entry.Media.LinkUrl,
                    entry.Media.ContentType,
                    entry.Media.IsVideo,
                    out var source,
                    loop: false))
                continue;

            try
            {
                var session = _backend!.CreateSession();
                session.Failed += OnSessionFailed;
                session.Open(source!);
                var port = new AvaloniaMediaPlaybackPort(session);
                SlideShowMediaPlaybackHandle? playback = null;
                VideoView? view = null;
                if (entry.Media.IsVideo && session is LibVlcMediaPlaybackSession)
                {
                    view = CreateVideoView(
                        (LibVlcMediaPlaybackSession)session,
                        plan.Bounds);
                }
                // LibVLC raises EndReached on its own native worker thread, so this handler does not
                // arrive on the UI thread the way the WPF player's events do. Touching the VideoView
                // from here would be a cross-thread visual mutation, and calling back into the
                // player (Pause/Seek) directly from a LibVLC callback is its own hazard. Marshal the
                // whole body onto the UI thread instead.
                session.Ended += (_, _) => Dispatcher.UIThread.Post(() =>
                {
                    if (playback is not null)
                        ApplyPlaybackSnapshot(_playbackSession.HandleEnded(playback));
                });

                Border? captionHost = null;
                TextBlock? captionText = null;
                if (entry.CaptionTrack is not null)
                {
                    (captionHost, captionText) = CreateCaptionView(plan.Bounds);
                }

                playback = _playbackSession.Register(shape.Id, entry.Media, port);
                var slot = new MediaSlot
                {
                    ShapeId = shape.Id,
                    Session = session,
                    Playback = playback,
                    AuthoredBounds = plan.Bounds,
                    VideoView = view,
                    CaptionTrack = entry.CaptionTrack,
                    CaptionHost = captionHost,
                    CaptionText = captionText,
                };
                _slots.Add(slot);
                ApplyPlaybackSnapshot(slot, _playbackSession.Snapshot(playback));
            }
            catch (Exception ex)
            {
                LastFailure = new MediaPlaybackFailure(
                    MediaPlaybackFailureKind.EngineError,
                    "The Avalonia media adapter could not start the media source.",
                    ex);
            }
        }

        RefreshPlaybackTimer();
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

        if (_playbackSession.TryHandleClick(LastClick.Media!.ShapeId, out var snapshot) &&
            snapshot is not null)
            ApplyPlaybackSnapshot(snapshot);
        return true;
    }

    public bool TrySeek(uint shapeId, TimeSpan position)
    {
        var didSeek = _playbackSession.TrySeek(shapeId, position, out var snapshot);
        if (snapshot is not null)
            ApplyPlaybackSnapshot(snapshot);
        return didSeek;
    }

    /// <summary>Seeks an active media session to a named authored bookmark.</summary>
    public bool TrySeekToBookmark(uint shapeId, string bookmarkName)
    {
        var didSeek = _playbackSession.TrySeekToBookmark(shapeId, bookmarkName, out var snapshot);
        if (snapshot is not null)
            ApplyPlaybackSnapshot(snapshot);
        return didSeek;
    }

    public bool TrySetVolume(uint shapeId, int volume)
    {
        var didSet = _playbackSession.TrySetVolume(shapeId, volume, out var snapshot);
        if (snapshot is not null)
            ApplyPlaybackSnapshot(snapshot);
        return didSet;
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
        var host = new Border
        {
            Background = Brushes.Black,
            Opacity = 0.82,
            Child = text,
            IsVisible = false,
            IsHitTestVisible = false,
            ZIndex = 10,
        };
        ApplyCaptionPlacement(
            host,
            text,
            PresentationMediaTranscriptPlanner.PlanOverlayPlacement(
                new PresentationMediaOverlayPlacementRequest(
                    bounds,
                    _canvasW,
                    _canvasH,
                    UseFullScreen: false)));
        _overlay.Children.Add(host);
        return (host, text);
    }

    private static void ApplyCaptionPlacement(
        Border host,
        TextBlock text,
        PresentationMediaOverlayPlacement placement)
    {
        host.Width = placement.CaptionBounds.Width;
        host.Height = placement.CaptionBounds.Height;
        text.Width = placement.CaptionTextWidth ?? double.NaN;
        text.Height = placement.CaptionTextHeight ?? double.NaN;
        text.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        text.RenderTransform = placement.IsCaptionVertical
            ? new RotateTransform(placement.CaptionRotationDegrees)
            : null;
        Canvas.SetLeft(host, placement.CaptionBounds.X);
        Canvas.SetTop(host, placement.CaptionBounds.Y);
    }

    private void UpdateCaptions()
    {
        foreach (var slot in _slots)
        {
            if (slot.CaptionHost is null || slot.CaptionText is null)
                continue;

            var cue = PresentationMediaTranscriptPlanner.FindActiveCue(
                slot.CaptionTrack,
                slot.Playback.Port.Position);
            ApplyCaptionText(slot.CaptionText, cue);
            if (cue is not null
                && _activeSlide is { } activeSlide
                && SlideShapeTraversal.FindById(activeSlide, slot.ShapeId) is { Media: not null } shape)
            {
                var bounds = SlideShowMediaInteractionPlanner.ComputeMediaBounds(
                    shape,
                    _slideDipW,
                    _slideDipH,
                    _canvasW,
                    _canvasH);
                ApplyCaptionPlacement(
                    slot.CaptionHost,
                    slot.CaptionText,
                    PresentationMediaTranscriptPlanner.PlanOverlayPlacement(
                        new PresentationMediaOverlayPlacementRequest(
                            bounds,
                            _canvasW,
                            _canvasH,
                            _playbackSession.Snapshot(slot.Playback).UseFullScreen,
                            cue,
                            slot.CaptionTrack?.Regions)));
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
        _playbackSession.Teardown();
        foreach (var slot in _slots)
            DisposeSlot(slot);
        _slots.Clear();
        _activeSlide = null;
        _captionTimer.Stop();

        ReleaseBackend();
    }

    private void ReleaseBackend()
    {
        _transitionSoundSession?.Dispose();
        _transitionSoundSession = null;
        _backend?.Dispose();
        _backend = null;
    }

    private void ApplyEnterResult(SlideShowMediaEnterResult result)
    {
        for (var i = _slots.Count - 1; i >= 0; i--)
        {
            var slot = _slots[i];
            if (result.Released.Contains(slot.Playback))
            {
                DisposeSlot(slot);
                _slots.RemoveAt(i);
                continue;
            }

            if (slot.CaptionHost is not null)
                _overlay.Children.Remove(slot.CaptionHost);
            slot.CaptionTrack = null;
            slot.CaptionHost = null;
            slot.CaptionText = null;
        }
    }

    private void DisposeSlot(MediaSlot slot)
    {
        slot.Session.Failed -= OnSessionFailed;
        slot.Session.Dispose();
        if (slot.VideoView is not null)
            _overlay.Children.Remove(slot.VideoView);
        if (slot.CaptionHost is not null)
            _overlay.Children.Remove(slot.CaptionHost);
    }

    private void OnSessionFailed(object? sender, MediaPlaybackFailure failure) => LastFailure = failure;

    private void RefreshPlaybackTimer()
    {
        _captionTimer.IsEnabled = SlideShowMediaInteractionPlanner.ShouldRunPeriodicUpdates(
            _slots.Select(slot => new SlideShowMediaActiveSlotMonitorPlan(
                slot.CaptionTrack,
                slot.Playback)),
            _playbackSession);
        UpdateCaptions();
    }

    private void EnforcePlaybackState()
    {
        foreach (var snapshot in _playbackSession.EnforcePlaybackState())
            ApplyPlaybackSnapshot(snapshot);
    }

    private void ApplyPlaybackSnapshot(SlideShowMediaPlaybackSnapshot snapshot)
    {
        var slot = _slots.FirstOrDefault(candidate => candidate.Playback.ShapeId == snapshot.ShapeId);
        if (slot is not null)
            ApplyPlaybackSnapshot(slot, snapshot);
    }

    private void ApplyPlaybackSnapshot(MediaSlot slot, SlideShowMediaPlaybackSnapshot snapshot)
    {
        if (slot.VideoView is null)
            return;

        var placement = PresentationMediaTranscriptPlanner.PlanOverlayPlacement(
            new PresentationMediaOverlayPlacementRequest(
                slot.AuthoredBounds,
                _canvasW,
                _canvasH,
                snapshot.UseFullScreen,
                PresentationMediaTranscriptPlanner.FindActiveCue(
                    slot.CaptionTrack,
                    slot.Playback.Port.Position),
                slot.CaptionTrack?.Regions));
        ApplyVideoViewBounds(
            slot.VideoView,
            placement.MediaBounds);
        if (slot.CaptionHost is not null && slot.CaptionText is not null)
            ApplyCaptionPlacement(slot.CaptionHost, slot.CaptionText, placement);
        slot.VideoView.IsVisible = snapshot.ShowVisual;
    }

    private static void ApplyVideoViewBounds(VideoView view, LayoutRect bounds)
    {
        view.Width = Math.Max(1, bounds.Width);
        view.Height = Math.Max(1, bounds.Height);
        Canvas.SetLeft(view, bounds.X);
        Canvas.SetTop(view, bounds.Y);
    }

    private sealed class AvaloniaMediaPlaybackPort : IMediaPlaybackPort
    {
        private readonly IMediaPlaybackSession _session;

        public AvaloniaMediaPlaybackPort(IMediaPlaybackSession session) => _session = session;

        public bool IsPlaying => _session.State == MediaPlaybackState.Playing;
        public TimeSpan Position => _session.Position;
        public TimeSpan Duration => _session.Duration;
        public int VolumePercent { set => _session.Volume = value; }
        public void Play() => _session.Play();
        public void Pause() => _session.Pause();
        public void Stop() => _session.Stop();
        public bool Seek(TimeSpan position) => _session.Seek(position);
    }
}
