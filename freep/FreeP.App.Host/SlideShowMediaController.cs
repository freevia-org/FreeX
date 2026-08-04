using System.IO;
using System.Windows;
using System.Windows.Controls;
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

    // ── per-slide state ───────────────────────────────────────────────────────

    // For each media shape: the MediaElement (null if creation failed) + optional temp path.
    private sealed record MediaSlot(
        uint ShapeId,
        MediaElement? Element,
        string? TempPath,
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
        _captionTimer.Tick += (_, _) => UpdateCaptions();
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
                           IReadOnlyList<PresentationMediaTranscriptTrackDescriptor>? captionTracks = null)
    {
        // Teardown any previous slide's media first (guard against double-call).
        Teardown();

        _slideDipW = slideDipW;
        _slideDipH = slideDipH;
        _canvasW = canvasW;
        _canvasH = canvasH;
        _activeSlide = slide;

        foreach (var shape in ShapeTreeLookup.Enumerate(slide))
        {
            if (shape.Kind != SlideShapeKind.Media || shape.Media is null)
                continue;

            var rect = ComputeMediaRect(shape, slideDipW, slideDipH, canvasW, canvasH);
            var slot = CreateSlot(shape.Id, shape.Media, rect, shape.Media.IsVideo);
            var captionTrack = captionTracks?.FirstOrDefault(track =>
                track.ShapeId == shape.Id && track.HasTranscript);
            if (captionTrack is not null)
            {
                var caption = CreateCaptionView(rect);
                slot = slot with
                {
                    CaptionTrack = captionTrack,
                    CaptionHost = caption.Host,
                    CaptionText = caption.Text,
                };
            }
            _slots.Add(slot);
        }

        _captionTimer.IsEnabled = _slots.Any(slot => slot.CaptionTrack is not null);
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
            var shape = ShapeTreeLookup.Find(slide, slot.ShapeId);
            if (shape?.Media is null || shape.Kind != SlideShapeKind.Media)
                continue;

            var r = ComputeMediaRect(shape, _slideDipW, _slideDipH, canvasW, canvasH);
            if (slot.Element is not null)
                ApplyRect(slot.Element, r);

            if (slot.CaptionHost is not null)
                ApplyCaptionPlacement(slot.CaptionHost, r, cue: null);
        }
    }

    /// <summary>
    /// Stops all players, removes all MediaElement children from the overlay, and
    /// deletes all temp files.  Safe to call multiple times.
    /// </summary>
    public void Teardown()
    {
        foreach (var slot in _slots)
        {
            if (slot.Element is not null)
            {
                try
                {
                    slot.Element.Stop();
                    _overlay.Children.Remove(slot.Element);
                }
                catch { /* ignore */ }
            }

            if (slot.CaptionHost is not null)
                _overlay.Children.Remove(slot.CaptionHost);

            if (slot.TempPath is not null)
                _fileWriter.Delete(slot.TempPath);
        }
        _slots.Clear();
        _activeSlide = null;
        _captionTimer.Stop();
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
            canvasY);
        LastMediaClickShapeIdForTest = click.Media?.ShapeId;
        if (!click.IsHandled)
            return false;

        var slot = _slots.FirstOrDefault(candidate =>
            candidate.ShapeId == click.Media!.ShapeId);
        if (slot is null)
            return true;

        TogglePlayPause(slot.Element);
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
        ApplyCaptionPlacement(host, bounds, cue: null);
        Panel.SetZIndex(host, 10);
        _overlay.Children.Add(host);
        return (host, text);
    }

    private static void ApplyCaptionPlacement(
        Border host,
        MediaShapeRect bounds,
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
        Canvas.SetLeft(host, bounds.X + placement.X);
        Canvas.SetTop(host, bounds.Y + placement.Y);
    }

    private void UpdateCaptions(TimeSpan? testPlaybackPosition = null)
    {
        foreach (var slot in _slots)
        {
            if (slot.CaptionHost is null || slot.CaptionText is null)
                continue;

            if (slot.Element is null && testPlaybackPosition is null)
            {
                slot.CaptionText.Text = string.Empty;
                slot.CaptionHost.Visibility = Visibility.Collapsed;
                continue;
            }

            var cue = PresentationMediaTranscriptPlanner.FindActiveCue(
                slot.CaptionTrack,
                testPlaybackPosition ?? slot.Element!.Position);
            slot.CaptionText.Text = cue?.Text ?? string.Empty;
            if (cue is not null
                && _activeSlide is { } activeSlide
                && ShapeTreeLookup.Find(activeSlide, slot.ShapeId) is { Media: not null } shape)
            {
                var bounds = ComputeMediaRect(shape, _slideDipW, _slideDipH, _canvasW, _canvasH);
                ApplyCaptionPlacement(slot.CaptionHost, bounds, cue);
            }
            slot.CaptionHost.Visibility = cue is null
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }

    private MediaSlot CreateSlot(uint shapeId, MediaInfo media, MediaShapeRect rect, bool isVideo)
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
            return new MediaSlot(shapeId, null, tempPath);
        }

        MediaElement? element = null;
        try
        {
            element = new MediaElement
            {
                LoadedBehavior   = MediaState.Manual,
                UnloadedBehavior = MediaState.Stop,
                Source           = source,
                // For audio: collapse the visual (no video frame to show).
                Visibility       = isVideo ? Visibility.Visible : Visibility.Collapsed,
                IsHitTestVisible = false,   // we do our own hit-testing
            };

            ApplyRect(element, rect);

            // Handle media failure gracefully — just hide the element.
            element.MediaFailed += (_, _) =>
            {
                element.Visibility = Visibility.Collapsed;
            };

            element.MediaEnded += (_, _) =>
            {
                if (!media.Loop)
                    return;

                try
                {
                    element.Position = TimeSpan.Zero;
                    element.Play();
                    element.Tag = true;
                }
                catch (InvalidOperationException)
                {
                    element.Tag = false;
                }
            };

            _overlay.Children.Add(element);
            if (media.PlaybackStartMode == MediaPlaybackStartMode.Automatically)
            {
                element.Play();
                element.Tag = true;
            }
        }
        catch
        {
            // Headless / no-display: swallow — tempPath is still set so cleanup works.
            element = null;
        }

        return new MediaSlot(shapeId, element, tempPath);
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
        if (position < TimeSpan.Zero)
            return false;

        var element = _slots.FirstOrDefault(slot => slot.ShapeId == shapeId)?.Element;
        if (element is null)
            return false;

        try
        {
            element.Position = position;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Sets the active media volume using the shared 0-100 volume convention.</summary>
    public bool TrySetVolume(uint shapeId, int volume)
    {
        var element = _slots.FirstOrDefault(slot => slot.ShapeId == shapeId)?.Element;
        if (element is null)
            return false;

        try
        {
            element.Volume = Math.Clamp(volume, 0, 100) / 100d;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void ApplyRect(MediaElement el, MediaShapeRect r)
    {
        el.Width  = Math.Max(1, r.Width);
        el.Height = Math.Max(1, r.Height);
        Canvas.SetLeft(el, r.X);
        Canvas.SetTop(el, r.Y);
    }

    private static void TogglePlayPause(MediaElement? el)
    {
        if (el is null) return;
        try
        {
            // Check CanPause: if playing → pause; else → play.
            // We track state by observing the SpeedRatio + Position heuristic.
            // The simplest safe approach: try Pause first; if it fails, Play.
            // MediaElement does not expose a clean "IsPlaying" property,
            // so we use a tag flag.
            if (el.Tag is true)
            {
                el.Pause();
                el.Tag = false;
            }
            else
            {
                el.Play();
                el.Tag = true;
            }
        }
        catch { /* ignore */ }
    }
}
