using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

// ─────────────────────────────────────────────────────────────────────────────
// SlideShowMediaController
// ─────────────────────────────────────────────────────────────────────────────
//
// Manages the lifecycle of WPF MediaElement overlays for a single slide in the
// fullscreen slideshow.
//
// Design:
//   • For each Media shape on the entering slide we:
//       1. Write the embedded bytes to a unique temp file (or use the http/https
//          LinkUrl directly if link-only).  Temp files are deleted on tear-down.
//       2. Create a MediaElement positioned over the same on-screen rect that
//          SlideCanvas paints the poster/play-button into.
//       3. Add it to the provided WPF Panel overlay.
//   • Click-to-toggle: a MouseLeftButtonDown handler on each MediaElement
//     (and a transparent hit-rect for audio shapes, which have no visual) plays /
//     pauses and marks the event Handled so it does not reach the slideshow advance.
//   • Teardown (called on slide-leave or window close) stops playback, removes the
//     elements from the panel, and deletes temp files.
//
// Headless safety:
//   MediaElement instantiation is deferred into a try/catch; any display-level
//   failure (PlatformNotSupportedException in unit tests) is silently caught and
//   the slot left null.  EnterSlide therefore never throws.
//
// Testability:
//   The rect-computation logic (ComputeMediaRect) is a static method with no WPF
//   dependency so it can be called from pure [Fact] tests.
//   The file-write step is abstracted behind ITempMediaFileWriter so tests can
//   inject a fake implementation.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Abstraction over temp-file creation so tests can inject a fake that does not
/// touch the real file system.
/// </summary>
public interface ITempMediaFileWriter
{
    /// <summary>
    /// Writes <paramref name="bytes"/> to a unique temp file whose extension matches
    /// <paramref name="contentType"/> (e.g. "video/mp4" → ".mp4").
    /// Returns the absolute path to the created file.
    /// </summary>
    string Write(byte[] bytes, string contentType);

    /// <summary>Deletes the file at <paramref name="path"/> (best-effort, ignores errors).</summary>
    void Delete(string path);
}

/// <summary>Default implementation that writes to <see cref="Path.GetTempPath"/>.</summary>
internal sealed class TempMediaFileWriter : ITempMediaFileWriter
{
    public string Write(byte[] bytes, string contentType)
    {
        string ext = ContentTypeToExtension(contentType);
        string path = Path.Combine(Path.GetTempPath(), $"freep_media_{Guid.NewGuid():N}{ext}");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public void Delete(string path)
    {
        try { File.Delete(path); } catch { /* best-effort */ }
    }

    internal static string ContentTypeToExtension(string contentType) =>
        contentType.ToLowerInvariant() switch
        {
            "video/mp4"             => ".mp4",
            "video/mpeg"            => ".mpg",
            "video/avi"             => ".avi",
            "video/x-msvideo"       => ".avi",
            "video/quicktime"       => ".mov",
            "video/x-ms-wmv"        => ".wmv",
            "video/x-ms-asf"        => ".asf",
            "video/webm"            => ".webm",
            "audio/mpeg"            => ".mp3",
            "audio/mp3"             => ".mp3",
            "audio/wav"             => ".wav",
            "audio/x-wav"           => ".wav",
            "audio/ogg"             => ".ogg",
            "audio/x-ms-wma"        => ".wma",
            "audio/aac"             => ".aac",
            "audio/flac"            => ".flac",
            _                       => ".bin",
        };
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Compute result returned by <see cref="SlideShowMediaController.ComputeMediaRect"/>.
/// </summary>
public sealed class MediaShapeRect
{
    /// <summary>On-screen canvas DIP X (top-left).</summary>
    public double X      { get; }
    /// <summary>On-screen canvas DIP Y (top-left).</summary>
    public double Y      { get; }
    /// <summary>Width in DIP.</summary>
    public double Width  { get; }
    /// <summary>Height in DIP.</summary>
    public double Height { get; }

    public MediaShapeRect(double x, double y, double w, double h)
    {
        X = x; Y = y; Width = w; Height = h;
    }
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Manages MediaElement overlays for media shapes on a single slide visit.
/// </summary>
public sealed class SlideShowMediaController
{
    // ── injected / constructed ────────────────────────────────────────────────

    private readonly Panel               _overlay;      // the canvas/panel to add elements into
    private readonly ITempMediaFileWriter _fileWriter;

    // Slide DIP dimensions — used to compute on-screen rect.
    private double _slideDipW;
    private double _slideDipH;
    private double _canvasW;
    private double _canvasH;
    private Slide? _activeSlide;
    private bool _showMediaControls = true;
    private bool _showNarration = true;
    private readonly SlideShowMediaPlaybackSession _playbackSession = new();

    // ── per-slide state ───────────────────────────────────────────────────────

    // For each media shape: the MediaElement (null if creation failed) + optional temp path.
    private sealed record MediaSlot(
        uint ShapeId,
        MediaElement? Element,
        string? TempPath,
        MediaShapeRect AuthoredRect,
        SlideShowMediaPlaybackHandle? Playback = null,
        PresentationMediaTranscriptTrackDescriptor? CaptionTrack = null,
        Border? CaptionHost = null,
        TextBlock? CaptionText = null);
    private readonly List<MediaSlot> _slots = new();
    private readonly DispatcherTimer _captionTimer;

    // ── construction ──────────────────────────────────────────────────────────

    /// <param name="overlay">Panel (Canvas/Grid) to add MediaElement children to.</param>
    /// <param name="fileWriter">Override for tests; pass null to use the real file system.</param>
    public SlideShowMediaController(Panel overlay, ITempMediaFileWriter? fileWriter = null)
    {
        _overlay    = overlay ?? throw new ArgumentNullException(nameof(overlay));
        _fileWriter = fileWriter ?? new TempMediaFileWriter();
        _captionTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        _captionTimer.Tick += (_, _) =>
        {
            EnforcePlaybackState();
            UpdateCaptions();
        };
    }

    internal string? CaptionTextForTest(uint shapeId) =>
        _slots.FirstOrDefault(slot => slot.CaptionTrack?.ShapeId == shapeId)?.CaptionText?.Text;

    internal void RefreshCaptionsForTest(TimeSpan? playbackPosition = null) =>
        UpdateCaptions(playbackPosition);

    internal uint? LastMediaClickShapeIdForTest { get; private set; }

    // ── public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Compute the on-screen DIP rect for a media shape given the canvas size and slide DIP size.
    /// This is the same coordinate-space conversion used by hit-testing in SlideShowWindow.
    /// </summary>
    public static MediaShapeRect ComputeMediaRect(
        SlideShape shape,
        double slideDipW, double slideDipH,
        double canvasW,   double canvasH)
    {
        var bounds = SlideShowMediaInteractionPlanner.ComputeMediaBounds(
            shape, slideDipW, slideDipH, canvasW, canvasH);
        return new MediaShapeRect(
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height);
    }

    /// <summary>
    /// Called when entering a slide. Collects all Media shapes and creates a
    /// (possibly hidden) MediaElement for each.
    /// </summary>
    /// <param name="slide">The slide just entered.</param>
    /// <param name="slideDipW">Slide width in DIP (presentation.SlideSizeCxEmu / 9525.0).</param>
    /// <param name="slideDipH">Slide height in DIP.</param>
    /// <param name="canvasW">Actual pixel width of the slide canvas at the moment of entry.</param>
    /// <param name="canvasH">Actual pixel height of the slide canvas.</param>
    public void EnterSlide(Slide slide, double slideDipW, double slideDipH,
                           double canvasW, double canvasH,
                           IReadOnlyList<PresentationMediaTranscriptTrackDescriptor>? captionTracks = null,
                           uint? preferredCaptionShapeId = null,
                           int? preferredCaptionTrackIndex = null,
                           int? captionSlideIndex = null,
                           int? preferredCaptionSlideIndex = null,
                           bool showMediaControls = true,
                           bool showNarration = true,
                           int? presentationSlideIndex = null)
    {
        ApplyEnterResult(_playbackSession.EnterSlide(presentationSlideIndex));

        _slideDipW = slideDipW;
        _slideDipH = slideDipH;
        _canvasW = canvasW;
        _canvasH = canvasH;
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

        foreach (var entry in entryPlan.Items)
        {
            var bounds = entry.Surface.Bounds;
            var rect = new MediaShapeRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            var slot = CreateSlot(entry.ShapeId, entry.Media, rect);
            if (entry.CaptionTrack is not null)
            {
                var caption = CreateCaptionView(rect);
                slot = slot with
                {
                    CaptionTrack = entry.CaptionTrack,
                    CaptionHost = caption.Host,
                    CaptionText = caption.Text,
                };
            }
            _slots.Add(slot);
        }

        _captionTimer.IsEnabled = SlideShowMediaInteractionPlanner.ShouldRunPeriodicUpdates(
            _slots.Select(slot => new SlideShowMediaActiveSlotMonitorPlan(
                slot.CaptionTrack,
                slot.Playback)),
            _playbackSession);
        UpdateCaptions();
    }

    /// <summary>
    /// Update all existing MediaElement positions/sizes when the canvas is resized.
    /// Call from a SlideCanvas SizeChanged handler if desired (optional).
    /// </summary>
    public void UpdateLayout(Slide slide, double canvasW, double canvasH)
    {
        if (_activeSlide is not null && !ReferenceEquals(_activeSlide, slide))
        {
            Teardown();
            return;
        }

        _canvasW = canvasW;
        _canvasH = canvasH;

        foreach (var slot in _slots)
        {
            var shape = SlideShapeTraversal.FindById(slide, slot.ShapeId);
            if (shape?.Media is null || shape.Kind != SlideShapeKind.Media)
                continue;

            var r = ComputeMediaRect(shape, _slideDipW, _slideDipH, canvasW, canvasH);
            if (slot.Element is not null)
                ApplyRect(slot.Element, slot.Playback is { } playback &&
                    _playbackSession.Snapshot(playback).UseFullScreen
                    ? FullScreenRect()
                    : r);

            if (slot.CaptionHost is not null && slot.CaptionText is not null)
            {
                var cue = slot.Element is null
                    ? null
                    : PresentationMediaTranscriptPlanner.FindActiveCue(
                        slot.CaptionTrack,
                        slot.Playback?.Port.Position ?? slot.Element.Position);
                ApplyCaptionPlacement(slot.CaptionHost, slot.CaptionText,
                    slot.Playback is { } captionPlayback &&
                    _playbackSession.Snapshot(captionPlayback).UseFullScreen
                        ? FullScreenRect()
                        : r,
                    cue,
                    slot.CaptionTrack?.Regions);
            }
        }
    }

    /// <summary>
    /// Stops all players, removes all MediaElement children from the overlay, and
    /// deletes all temp files.  Safe to call multiple times.
    /// </summary>
    public void Teardown()
    {
        _playbackSession.Teardown();
        foreach (var slot in _slots)
            DisposeSlot(slot);
        _slots.Clear();
        _activeSlide = null;
        _captionTimer.Stop();
    }

    private void ApplyEnterResult(SlideShowMediaEnterResult result)
    {
        for (var i = _slots.Count - 1; i >= 0; i--)
        {
            var slot = _slots[i];
            if (slot.Playback is null || result.Released.Contains(slot.Playback))
            {
                DisposeSlot(slot);
                _slots.RemoveAt(i);
                continue;
            }

            if (slot.CaptionHost is not null)
                _overlay.Children.Remove(slot.CaptionHost);
            _slots[i] = slot with
            {
                CaptionTrack = null,
                CaptionHost = null,
                CaptionText = null,
            };
        }
    }

    private void DisposeSlot(MediaSlot slot)
    {
        if (slot.Element is not null)
        {
            try
            {
                _overlay.Children.Remove(slot.Element);
            }
            catch { /* ignore */ }
        }

        if (slot.CaptionHost is not null)
            _overlay.Children.Remove(slot.CaptionHost);

        if (slot.TempPath is not null)
            _fileWriter.Delete(slot.TempPath);
    }

    /// <summary>
    /// Hit-tests a point (in canvas DIP coords) against any active media slot.
    /// Returns true and toggles play/pause if a slot was hit; the caller must
    /// set e.Handled = true to consume the click.
    /// </summary>
    public bool TryHandleClick(double canvasX, double canvasY, Slide slide,
                               double canvasW, double canvasH)
    {
        var click = SlideShowMediaInteractionPlanner.PlanClick(
            slide,
            _slideDipW,
            _slideDipH,
            canvasW,
            canvasH,
            canvasX,
            canvasY,
            _showMediaControls,
            _showNarration);
        LastMediaClickShapeIdForTest = click.Media?.ShapeId;
        if (!click.IsHandled)
            return false;

        if (_playbackSession.TryHandleClick(click.Media!.ShapeId, out var snapshot) && snapshot is not null)
            ApplyPlaybackSnapshot(snapshot);
        return true;
    }

    // ── internal helpers ──────────────────────────────────────────────────────

    private (Border Host, TextBlock Text) CreateCaptionView(MediaShapeRect bounds)
    {
        var text = new TextBlock
        {
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(10, 4, 10, 4),
        };
        var height = Math.Clamp(bounds.Height * 0.2, 36, 86);
        var host = new Border
        {
            Background = Brushes.Black,
            Opacity = 0.82,
            Width = Math.Max(1, bounds.Width),
            Height = height,
            Child = text,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        ApplyCaptionPlacement(host, text, bounds, cue: null);
        Panel.SetZIndex(host, 10);
        _overlay.Children.Add(host);
        return (host, text);
    }

    private static void ApplyCaptionPlacement(
        Border host,
        TextBlock text,
        MediaShapeRect bounds,
        PresentationMediaTranscriptCueDescriptor? cue,
        IReadOnlyList<PresentationMediaTranscriptRegionDescriptor>? regions = null)
    {
        var defaultHeight = Math.Clamp(bounds.Height * 0.2, 36, 86);
        var placement = PresentationMediaTranscriptPlanner.ComputeCaptionPlacement(
            cue,
            bounds.Width,
            bounds.Height,
            defaultHeight,
            regions);
        host.Width = placement.Width;
        host.Height = placement.Height;
        var isVertical = placement.RotationDegrees != 0;
        text.Width = isVertical ? placement.Height : double.NaN;
        text.Height = isVertical ? placement.Width : double.NaN;
        text.RenderTransformOrigin = new Point(0.5, 0.5);
        text.RenderTransform = isVertical
            ? new RotateTransform(placement.RotationDegrees)
            : Transform.Identity;
        Canvas.SetLeft(host, bounds.X + placement.X);
        Canvas.SetTop(host, bounds.Y + placement.Y);
    }

    private void UpdateCaptions(TimeSpan? testPlaybackPosition = null)
    {
        foreach (var slot in _slots)
        {
            if (slot.CaptionHost is null || slot.CaptionText is null)
                continue;

            if (slot.Playback is null && testPlaybackPosition is null)
            {
                slot.CaptionText.Text = string.Empty;
                slot.CaptionHost.Visibility = Visibility.Collapsed;
                continue;
            }

            var cue = PresentationMediaTranscriptPlanner.FindActiveCue(
                slot.CaptionTrack,
                testPlaybackPosition ?? slot.Playback!.Port.Position);
            ApplyCaptionText(slot.CaptionText, cue);
            if (cue is not null
                && _activeSlide is { } activeSlide
                && SlideShapeTraversal.FindById(activeSlide, slot.ShapeId) is { Media: not null } shape)
            {
                var bounds = ComputeMediaRect(shape, _slideDipW, _slideDipH, _canvasW, _canvasH);
                if (slot.Playback is { } playback &&
                    _playbackSession.Snapshot(playback).UseFullScreen)
                    bounds = FullScreenRect();
                ApplyCaptionPlacement(slot.CaptionHost, slot.CaptionText, bounds, cue, slot.CaptionTrack?.Regions);
            }
            slot.CaptionHost.Visibility = cue is null
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }

    private static void ApplyCaptionText(
        TextBlock text,
        PresentationMediaTranscriptCueDescriptor? cue)
    {
        text.Inlines.Clear();
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

        foreach (var span in cue.Spans)
        {
            var run = new System.Windows.Documents.Run(span.Text)
            {
                FontWeight = span.Bold ? FontWeights.Bold : FontWeights.Normal,
                FontStyle = span.Italic ? FontStyles.Italic : FontStyles.Normal,
                TextDecorations = span.Underline ? TextDecorations.Underline : null,
                Foreground = CaptionBrush(span.ForegroundColorHex),
                Background = CaptionBrush(span.BackgroundColorHex)
            };
            if (!string.IsNullOrWhiteSpace(span.FontFamily))
            {
                try { run.FontFamily = new System.Windows.Media.FontFamily(span.FontFamily); }
                catch (ArgumentException) { /* fall back to the inherited caption font */ }
            }
            if (span.FontSizePx is { } fontSizePx)
            {
                run.FontSize = fontSizePx;
            }
            text.Inlines.Add(run);
        }
    }

    private static Brush? CaptionBrush(string? colorHex)
    {
        if (string.IsNullOrWhiteSpace(colorHex))
        {
            return null;
        }

        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#" + colorHex)!);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private MediaSlot CreateSlot(uint shapeId, MediaInfo media, MediaShapeRect rect)
    {
        // Resolve source first (writes temp file if needed).
        // This is done OUTSIDE the element-creation try/catch so the temp path
        // is always recorded and cleaned up on Teardown, even if MediaElement
        // construction fails (e.g. headless / no display in unit tests).
        // NOTE: tempPath is set even when source is null (e.g. invalid URI from a fake writer),
        // so we always have it for cleanup.
        Uri? source = ResolveSource(media, out string? tempPath);
        if (source is null)
        {
            // Still record the tempPath for cleanup (written but URI was unparseable).
            return new MediaSlot(shapeId, null, tempPath, rect);
        }

        MediaElement? element = null;
        try
        {
            element = new MediaElement
            {
                LoadedBehavior   = MediaState.Manual,
                UnloadedBehavior = MediaState.Stop,
                // PowerPoint lets a bookmark seek land while media is paused. WPF
                // otherwise ignores Position assignments made before playback starts.
                ScrubbingEnabled = true,
                Source           = source,
                Visibility       = Visibility.Collapsed,
                IsHitTestVisible = false,   // we do our own hit-testing
            };

            ApplyRect(element, rect);
            var port = new WpfMediaPlaybackPort(element);
            SlideShowMediaPlaybackHandle? playback = null;

            // Handle media failure gracefully — just hide the element.
            element.MediaFailed += (_, _) =>
            {
                element.Visibility = Visibility.Collapsed;
            };

            element.MediaOpened += (_, _) =>
            {
                port.OnMediaOpened();
                if (playback is not null &&
                    _playbackSession.Synchronize(playback, out var snapshot) &&
                    snapshot is not null)
                    ApplyPlaybackSnapshot(snapshot);
            };

            element.MediaEnded += (_, _) =>
            {
                port.OnMediaEnded();
                if (playback is not null)
                    ApplyPlaybackSnapshot(_playbackSession.HandleEnded(playback));
            };

            _overlay.Children.Add(element);
            playback = _playbackSession.Register(shapeId, media, port);
            var slot = new MediaSlot(shapeId, element, tempPath, rect, playback);
            ApplyPlaybackSnapshot(slot, _playbackSession.Snapshot(playback));
            return slot;
        }
        catch
        {
            // Headless / no-display: swallow — tempPath is still set so cleanup works.
            if (element is not null)
                _overlay.Children.Remove(element);
            element = null;
        }

        return new MediaSlot(shapeId, element, tempPath, rect);
    }

    private Uri? ResolveSource(MediaInfo media, out string? tempPath)
    {
        tempPath = null;

        // 1. Embedded bytes: write to a temp file.
        if (media.Bytes is { Length: > 0 })
        {
            tempPath = _fileWriter.Write(media.Bytes, media.ContentType);
            // Build a file:// URI; tolerate fake/relative paths from tests by falling back
            // gracefully — the MediaElement will simply never receive a source.
            if (!Uri.TryCreate(tempPath, UriKind.Absolute, out var fileUri))
                return null;
            return fileUri;
        }

        // 2. Link-only: only allow http/https (same safety guard as OpenExternalUrl).
        if (!string.IsNullOrEmpty(media.LinkUrl))
        {
            if (Uri.TryCreate(media.LinkUrl, UriKind.Absolute, out var uri) &&
                uri.Scheme is "http" or "https")
            {
                return uri;
            }
            // Non-safe scheme (e.g. file:// or blank) — skip.
            return null;
        }

        return null; // nothing to play
    }

    /// <summary>Seeks an active media element, matching the Avalonia playback controller.</summary>
    public bool TrySeek(uint shapeId, TimeSpan position)
    {
        var didSeek = _playbackSession.TrySeek(shapeId, position, out var snapshot);
        if (snapshot is not null)
            ApplyPlaybackSnapshot(snapshot);
        return didSeek;
    }

    /// <summary>Seeks an active media element to a named authored bookmark.</summary>
    public bool TrySeekToBookmark(uint shapeId, string bookmarkName)
    {
        var didSeek = _playbackSession.TrySeekToBookmark(shapeId, bookmarkName, out var snapshot);
        if (snapshot is not null)
            ApplyPlaybackSnapshot(snapshot);
        return didSeek;
    }

    /// <summary>Sets the active media volume using the shared 0-100 volume convention.</summary>
    public bool TrySetVolume(uint shapeId, int volume)
    {
        var didSet = _playbackSession.TrySetVolume(shapeId, volume, out var snapshot);
        if (snapshot is not null)
            ApplyPlaybackSnapshot(snapshot);
        return didSet;
    }

    private static void ApplyRect(MediaElement el, MediaShapeRect r)
    {
        el.Width  = Math.Max(1, r.Width);
        el.Height = Math.Max(1, r.Height);
        Canvas.SetLeft(el, r.X);
        Canvas.SetTop(el, r.Y);
    }

    private void EnforcePlaybackState()
    {
        foreach (var snapshot in _playbackSession.EnforcePlaybackState())
            ApplyPlaybackSnapshot(snapshot);
    }

    private void ApplyPlaybackSnapshot(SlideShowMediaPlaybackSnapshot snapshot)
    {
        var slot = _slots.FirstOrDefault(candidate => candidate.Playback is { } playback &&
            playback.ShapeId == snapshot.ShapeId);
        if (slot is not null)
            ApplyPlaybackSnapshot(slot, snapshot);
    }

    private void ApplyPlaybackSnapshot(MediaSlot slot, SlideShowMediaPlaybackSnapshot snapshot)
    {
        if (slot.Element is not { } element)
            return;

        element.Tag = snapshot.IsPlaying;
        ApplyRect(element, snapshot.UseFullScreen ? FullScreenRect() : slot.AuthoredRect);
        element.Visibility = snapshot.ShowVisual ? Visibility.Visible : Visibility.Collapsed;
    }

    private MediaShapeRect FullScreenRect() =>
        new(0, 0, Math.Max(1, _canvasW), Math.Max(1, _canvasH));

    private sealed class WpfMediaPlaybackPort : IMediaPlaybackPort
    {
        private readonly MediaElement _element;
        private TimeSpan? _pendingPosition;

        public WpfMediaPlaybackPort(MediaElement element) => _element = element;

        public bool IsPlaying { get; private set; }
        public TimeSpan Position => _pendingPosition ?? ReadPosition();
        public TimeSpan Duration => _element.NaturalDuration.HasTimeSpan
            ? _element.NaturalDuration.TimeSpan
            : TimeSpan.Zero;
        public int VolumePercent
        {
            set => _element.Volume = value / 100d;
        }

        public void Play()
        {
            _element.Play();
            IsPlaying = true;
        }

        public void Pause()
        {
            _element.Pause();
            IsPlaying = false;
        }

        public void Stop()
        {
            _element.Stop();
            IsPlaying = false;
        }

        public bool Seek(TimeSpan position)
        {
            try
            {
                _element.Position = position;
                _pendingPosition = _element.NaturalDuration.HasTimeSpan ? null : position;
                return true;
            }
            catch (InvalidOperationException)
            {
                _pendingPosition = position;
                return false;
            }
        }

        public void OnMediaOpened()
        {
            if (_pendingPosition is not { } position)
                return;

            _element.Position = position;
            _pendingPosition = null;
        }

        public void OnMediaEnded() => IsPlaying = false;

        private TimeSpan ReadPosition()
        {
            try
            {
                return _element.Position;
            }
            catch (InvalidOperationException)
            {
                return TimeSpan.Zero;
            }
        }
    }
}
