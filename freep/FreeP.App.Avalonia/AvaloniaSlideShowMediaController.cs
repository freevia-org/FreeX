using Avalonia.Controls;
using Avalonia.Layout;
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
        public VideoView? VideoView { get; init; }
    }

    private readonly Panel _overlay;
    private readonly IMediaPlaybackBackendFactory _backendFactory;
    private readonly List<MediaSlot> _slots = new();
    private IMediaPlaybackBackend? _backend;
    private IMediaPlaybackSession? _transitionSoundSession;
    private IReadOnlyList<SlideShowMediaShapePlan> _active = Array.Empty<SlideShowMediaShapePlan>();

    public AvaloniaSlideShowMediaController(
        Panel overlay,
        IMediaPlaybackBackendFactory? backendFactory = null)
    {
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        _backendFactory = backendFactory ?? new LibVlcMediaPlaybackBackendFactory();
    }

    public IReadOnlyList<SlideShowMediaShapePlan> Active => _active;
    public MediaPlaybackBackendAvailability? Availability { get; private set; }
    public MediaPlaybackFailure? LastFailure { get; private set; }
    public SlideShowMediaClickPlan LastClick { get; private set; } = SlideShowMediaClickPlan.NotMedia;

    public void SetCanvasBounds(double canvasW, double canvasH)
    {
        _overlay.Width = Math.Max(1, canvasW);
        _overlay.Height = Math.Max(1, canvasH);
    }

    public void EnterSlide(
        Slide slide,
        double slideDipW,
        double slideDipH,
        double canvasW,
        double canvasH)
    {
        ArgumentNullException.ThrowIfNull(slide);
        SetCanvasBounds(canvasW, canvasH);
        TeardownPlayback();
        _active = SlideShowMediaInteractionPlanner.BuildSlidePlan(
            slide, slideDipW, slideDipH, canvasW, canvasH);

        if (!_active.Any(plan => plan.HasSource) || !EnsureBackend())
            return;

        foreach (var shape in slide.Shapes.Where(shape =>
                     shape.Kind == SlideShapeKind.Media && shape.Media is not null))
        {
            var plan = _active.First(media => media.ShapeId == shape.Id);
            if (!plan.HasSource || !MediaPlaybackSourceFactory.TryCreate(
                    shape.Media!.Bytes,
                    shape.Media.LinkUrl,
                    shape.Media.ContentType,
                    shape.Media.IsVideo,
                    out var source))
                continue;

            try
            {
                var session = _backend!.CreateSession();
                session.Failed += OnSessionFailed;
                session.Open(source!);
                VideoView? view = null;
                if (shape.Media.IsVideo && session is LibVlcMediaPlaybackSession)
                {
                    view = CreateVideoView(
                        (LibVlcMediaPlaybackSession)session,
                        plan.Bounds);
                }

                _slots.Add(new MediaSlot
                {
                    ShapeId = shape.Id,
                    Session = session,
                    VideoView = view,
                });
                session.Play();
            }
            catch (Exception ex)
            {
                LastFailure = new MediaPlaybackFailure(
                    MediaPlaybackFailureKind.EngineError,
                    "The Avalonia media adapter could not start the media source.",
                    ex);
            }
        }
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
            canvasY);
        if (!LastClick.IsHandled)
            return false;

        var slot = _slots.FirstOrDefault(candidate =>
            candidate.ShapeId == LastClick.Media!.ShapeId);
        if (slot is null)
            return true;

        if (slot.Session.State == MediaPlaybackState.Playing)
            slot.Session.Pause();
        else
            slot.Session.Play();
        return true;
    }

    public bool TrySeek(uint shapeId, TimeSpan position) =>
        _slots.FirstOrDefault(slot => slot.ShapeId == shapeId)?.Session.Seek(position) == true;

    public bool TrySetVolume(uint shapeId, int volume)
    {
        var slot = _slots.FirstOrDefault(candidate => candidate.ShapeId == shapeId);
        if (slot is null) return false;
        slot.Session.Volume = volume;
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
                out var source) || !EnsureBackend())
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

    private void TeardownPlayback()
    {
        foreach (var slot in _slots)
        {
            try { slot.Session.Stop(); } catch { }
            slot.Session.Failed -= OnSessionFailed;
            slot.Session.Dispose();
            if (slot.VideoView is not null)
                _overlay.Children.Remove(slot.VideoView);
        }
        _slots.Clear();

        _transitionSoundSession?.Dispose();
        _transitionSoundSession = null;
        _backend?.Dispose();
        _backend = null;
    }

    private void OnSessionFailed(object? sender, MediaPlaybackFailure failure) => LastFailure = failure;
}
