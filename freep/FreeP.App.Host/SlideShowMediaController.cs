using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Free.Shared.Drawing;
using Free.Shared.Opc;
using FreeP.App.Compositor;
using FreeP.App.Media;
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

/// <summary>Default implementation backed by shared temporary-file leases.</summary>
internal sealed class TempMediaFileWriter : ITempMediaFileWriter
{
    private readonly ConcurrentDictionary<string, TemporaryFileLease> _leases =
        new(StringComparer.OrdinalIgnoreCase);

    public string Write(byte[] bytes, string contentType)
    {
        string ext = ContentTypeToExtension(contentType);
        var lease = TemporaryFileLease.Create("freep_media_", ext);
        try
        {
            lease.WriteAllBytes(bytes);
            if (!_leases.TryAdd(lease.Path, lease))
                throw new IOException($"Temporary media path is already owned: '{lease.Path}'.");
            return lease.Path;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    public void Delete(string path)
    {
        if (_leases.TryRemove(path, out var lease))
        {
            lease.Dispose();
            return;
        }

        using var externallyOwnedFile = TemporaryFileLease.Own(path);
    }

    internal static string ContentTypeToExtension(string contentType) =>
        OpcMediaTypes.GetMediaFileExtension(
            contentType,
            OpcMediaExtensionProfile.EmbeddedPlayback,
            includeDot: true);
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
public sealed partial class SlideShowMediaController
{
    // ── injected / constructed ────────────────────────────────────────────────

    private readonly Panel               _overlay;      // the canvas/panel to add elements into
    private readonly ITempMediaFileWriter _fileWriter;

    // Slide DIP dimensions — used to compute on-screen rect.
    private readonly SlideShowMediaNativeInteractionSession _interaction = new();
    private readonly SlideShowMediaPlaybackCommandCoordinator _playback;
    private SlideShowMediaPlaybackSession PlaybackSession => _playback.Session;

    // ── per-slide state ───────────────────────────────────────────────────────

    // For each media shape: the MediaElement (null if creation failed) + optional temp path.
    private sealed record MediaSlot(
        uint ShapeId,
        MediaElement? Element,
        IMediaPlaybackSession? Session,
        LayoutRect AuthoredBounds,
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
        _playback = new SlideShowMediaPlaybackCommandCoordinator(ApplyPlaybackSnapshot);
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

    partial void ObserveMediaClick(SlideShowMediaClickPlan click);

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
        => EnterSlide(
            new(slide, slideDipW, slideDipH, canvasW, canvasH, captionTracks,
                preferredCaptionShapeId, preferredCaptionTrackIndex, captionSlideIndex,
                preferredCaptionSlideIndex, showMediaControls, showNarration),
            presentationSlideIndex);

    public void EnterSlide(
        SlideShowMediaNativeEntryRequest request,
        int? presentationSlideIndex = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ApplyEnterResult(PlaybackSession.EnterSlide(presentationSlideIndex));

        var entryPlan = _interaction.Enter(request);

        foreach (var entry in entryPlan.Items)
        {
            var bounds = entry.Surface.Bounds;
            var slot = CreateSlot(entry.ShapeId, entry.Media, bounds);
            if (entry.CaptionTrack is not null)
            {
                var caption = CreateCaptionView(bounds);
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
            PlaybackSession);
        UpdateCaptions();
    }

    /// <summary>
    /// Update all existing MediaElement positions/sizes when the canvas is resized.
    /// Call from a SlideCanvas SizeChanged handler if desired (optional).
    /// </summary>
    public void UpdateLayout(Slide slide, double canvasW, double canvasH)
    {
        if (!_interaction.UpdateLayout(slide, canvasW, canvasH))
        {
            Teardown();
            return;
        }

        foreach (var slot in _slots)
        {
            var useFullScreen = slot.Playback is { } playback
                && PlaybackSession.Snapshot(playback).UseFullScreen;
            var projection = SlideShowMediaInteractionPlanner.PlanCaptionProjection(
                slide,
                slot.ShapeId,
                slot.CaptionTrack,
                slot.Element is null ? null : slot.Playback?.Port.Position ?? slot.Element.Position,
                useFullScreen,
                _interaction.SlideWidthDip,
                _interaction.SlideHeightDip,
                canvasW,
                canvasH);
            if (projection.Placement is not { } placement)
                continue;

            if (slot.Element is not null)
                ApplyRect(slot.Element, placement.MediaBounds);
            if (slot.CaptionHost is not null && slot.CaptionText is not null)
                ApplyCaptionPlacement(slot.CaptionHost, slot.CaptionText, placement);
        }
    }

    /// <summary>
    /// Stops all players, removes all MediaElement children from the overlay, and
    /// deletes all temp files.  Safe to call multiple times.
    /// </summary>
    public void Teardown()
    {
        PlaybackSession.Teardown();
        foreach (var slot in _slots)
            DisposeSlot(slot);
        _slots.Clear();
        _interaction.Clear();
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
        slot.Session?.Dispose();

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
    }

    /// <summary>
    /// Hit-tests a point (in canvas DIP coords) against any active media slot.
    /// Returns true and toggles play/pause if a slot was hit; the caller must
    /// set e.Handled = true to consume the click.
    /// </summary>
    public bool TryHandleClick(double canvasX, double canvasY, Slide slide,
                               double canvasW, double canvasH)
    {
        var click = _interaction.PlanClick(
            slide,
            _interaction.SlideWidthDip,
            _interaction.SlideHeightDip,
            canvasW,
            canvasH,
            canvasX,
            canvasY);
        ObserveMediaClick(click);
        if (!click.IsHandled)
            return false;

        if (PlaybackSession.TryHandleClick(click.Media!.ShapeId, out var snapshot) && snapshot is not null)
            ApplyPlaybackSnapshot(snapshot);
        return true;
    }

    // ── internal helpers ──────────────────────────────────────────────────────

    private (Border Host, TextBlock Text) CreateCaptionView(LayoutRect bounds)
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
        var host = new Border
        {
            Background = Brushes.Black,
            Opacity = 0.82,
            Child = text,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        ApplyCaptionPlacement(
            host,
            text,
            PresentationMediaTranscriptPlanner.PlanOverlayPlacement(
                new PresentationMediaOverlayPlacementRequest(
                    bounds,
                    _interaction.CanvasWidth,
                    _interaction.CanvasHeight,
                    UseFullScreen: false)));
        Panel.SetZIndex(host, 10);
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
        text.RenderTransformOrigin = new Point(0.5, 0.5);
        text.RenderTransform = placement.IsCaptionVertical
            ? new RotateTransform(placement.CaptionRotationDegrees)
            : Transform.Identity;
        Canvas.SetLeft(host, placement.CaptionBounds.X);
        Canvas.SetTop(host, placement.CaptionBounds.Y);
    }

    private void UpdateCaptions(TimeSpan? testPlaybackPosition = null)
    {
        foreach (var slot in _slots)
        {
            if (slot.CaptionHost is null || slot.CaptionText is null)
                continue;

            var useFullScreen = slot.Playback is { } playback
                && PlaybackSession.Snapshot(playback).UseFullScreen;
            var projection = SlideShowMediaInteractionPlanner.PlanCaptionProjection(
                _interaction.ActiveSlide,
                slot.ShapeId,
                slot.CaptionTrack,
                testPlaybackPosition ?? slot.Playback?.Port.Position,
                useFullScreen,
                _interaction.SlideWidthDip,
                _interaction.SlideHeightDip,
                _interaction.CanvasWidth,
                _interaction.CanvasHeight);
            ApplyCaptionText(slot.CaptionText, projection.Cue);
            if (projection.Placement is { } placement)
            {
                ApplyCaptionPlacement(slot.CaptionHost, slot.CaptionText, placement);
            }
            slot.CaptionHost.Visibility = projection.Cue is null
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
                Foreground = CaptionBrush(span.ForegroundColorHex, span.Opacity, fallbackToWhite: true),
                Background = CaptionBrush(span.BackgroundColorHex, span.Opacity, fallbackToWhite: false)
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

    private static Brush? CaptionBrush(string? colorHex, double? opacity, bool fallbackToWhite)
    {
        if (string.IsNullOrWhiteSpace(colorHex))
        {
            if (opacity is null || !fallbackToWhite)
                return null;

            return new SolidColorBrush(Color.FromArgb(CaptionAlpha(opacity), 0xFF, 0xFF, 0xFF));
        }

        if (!RgbColorTextCodec.TryParse(
                colorHex,
                RgbColorTextProfile.CaptionPayload,
                out var color))
            return null;

        return new SolidColorBrush(Color.FromArgb(
            CaptionAlpha(opacity),
            color.R,
            color.G,
            color.B));
    }

    private static byte CaptionAlpha(double? opacity) => opacity is { } value
        ? (byte)Math.Round(Math.Clamp(value, 0, 1) * byte.MaxValue)
        : byte.MaxValue;

    private MediaSlot CreateSlot(uint shapeId, MediaInfo media, LayoutRect bounds)
    {
        if (!MediaPlaybackSourceFactory.TryCreate(
                media.Bytes,
                media.LinkUrl,
                media.ContentType,
                media.IsVideo,
                out var source,
                loop: false))
            return new MediaSlot(shapeId, null, null, bounds);

        MediaElement? element = null;
        WpfMediaPlaybackSession? session = null;
        try
        {
            element = new MediaElement
            {
                LoadedBehavior   = MediaState.Manual,
                UnloadedBehavior = MediaState.Stop,
                // PowerPoint lets a bookmark seek land while media is paused. WPF
                // otherwise ignores Position assignments made before playback starts.
                ScrubbingEnabled = true,
                Visibility       = Visibility.Collapsed,
                IsHitTestVisible = false,   // we do our own hit-testing
            };

            ApplyRect(element, bounds);
            session = new WpfMediaPlaybackSession(
                element,
                new WpfMediaPlaybackSourceStore(_fileWriter));
            var port = new WpfMediaPlaybackPort(session);
            SlideShowMediaPlaybackHandle? playback = null;

            // Handle media failure gracefully — just hide the element.
            session.Failed += (_, _) =>
            {
                element.Visibility = Visibility.Collapsed;
            };

            element.MediaOpened += (_, _) =>
            {
                session.HandleMediaOpened();
                if (playback is not null &&
                    PlaybackSession.Synchronize(playback, out var snapshot) &&
                    snapshot is not null)
                    ApplyPlaybackSnapshot(snapshot);
            };

            element.MediaEnded += (_, _) => session.HandleMediaEnded();
            element.MediaFailed += (_, args) => session.HandleMediaFailed(args.ErrorException);
            session.Ended += (_, _) =>
            {
                if (playback is not null)
                    ApplyPlaybackSnapshot(PlaybackSession.HandleEnded(playback));
            };

            session.Open(source!);
            if (session.State == MediaPlaybackState.Failed)
            {
                session.Dispose();
                return new MediaSlot(shapeId, null, null, bounds);
            }

            _overlay.Children.Add(element);
            playback = PlaybackSession.Register(shapeId, media, port);
            var slot = new MediaSlot(shapeId, element, session, bounds, playback);
            ApplyPlaybackSnapshot(slot, PlaybackSession.Snapshot(playback));
            return slot;
        }
        catch
        {
            session?.Dispose();
            if (element is not null)
                _overlay.Children.Remove(element);
            element = null;
        }

        return new MediaSlot(shapeId, element, null, bounds);
    }

    /// <summary>Seeks an active media element, matching the Avalonia playback controller.</summary>
    public bool TrySeek(uint shapeId, TimeSpan position) =>
        _playback.TrySeek(shapeId, position);

    /// <summary>Seeks an active media element to a named authored bookmark.</summary>
    public bool TrySeekToBookmark(uint shapeId, string bookmarkName) =>
        _playback.TrySeekToBookmark(shapeId, bookmarkName);

    /// <summary>Sets the active media volume using the shared 0-100 volume convention.</summary>
    public bool TrySetVolume(uint shapeId, int volume) =>
        _playback.TrySetVolume(shapeId, volume);

    private static void ApplyRect(MediaElement el, LayoutRect bounds)
    {
        el.Width = Math.Max(1, bounds.Width);
        el.Height = Math.Max(1, bounds.Height);
        Canvas.SetLeft(el, bounds.X);
        Canvas.SetTop(el, bounds.Y);
    }

    private void EnforcePlaybackState() => _playback.EnforcePlaybackState();

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
        var projection = SlideShowMediaInteractionPlanner.PlanPlaybackProjection(
            slot.AuthoredBounds,
            slot.CaptionTrack,
            slot.Playback?.Port.Position ?? element.Position,
            _interaction.CanvasWidth,
            _interaction.CanvasHeight,
            snapshot);
        ApplyRect(element, projection.Placement.MediaBounds);
        if (slot.CaptionHost is not null && slot.CaptionText is not null)
            ApplyCaptionPlacement(slot.CaptionHost, slot.CaptionText, projection.Placement);
        element.Visibility = projection.ShowVisual ? Visibility.Visible : Visibility.Collapsed;
    }

    private sealed class WpfMediaPlaybackPort : IMediaPlaybackPort
    {
        private readonly IMediaPlaybackSession _session;

        public WpfMediaPlaybackPort(IMediaPlaybackSession session) => _session = session;

        public bool IsPlaying => _session.State == MediaPlaybackState.Playing;
        public TimeSpan Position => _session.Position;
        public TimeSpan Duration => _session.Duration;
        public int VolumePercent
        {
            set => _session.Volume = value;
        }

        public void Play() => _session.Play();
        public void Pause() => _session.Pause();
        public void Stop() => _session.Stop();
        public bool Seek(TimeSpan position) => _session.Seek(position);
    }
}
