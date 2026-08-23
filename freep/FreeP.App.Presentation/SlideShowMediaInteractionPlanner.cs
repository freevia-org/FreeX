using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record SlideShowMediaShapePlan(
    uint ShapeId,
    bool IsVideo,
    LayoutRect Bounds,
    bool HasSource,
    string SourceKind,
    string PlaybackCapabilityNote,
    bool ShowMediaControls,
    bool ShowWhenStopped);

public sealed record SlideShowMediaClickPlan(
    bool IsHandled,
    bool ShouldTogglePlayback,
    SlideShowMediaShapePlan? Media)
{
    public static SlideShowMediaClickPlan NotMedia { get; } = new(false, false, null);
}

public sealed record SlideShowMediaEntryItemPlan(
    SlideShape Shape,
    SlideShowMediaShapePlan Surface,
    PresentationMediaTranscriptTrackDescriptor? CaptionTrack)
{
    public uint ShapeId => Shape.Id;

    public MediaInfo Media => Shape.Media!;
}

public sealed record SlideShowMediaSlideEntryPlan(
    IReadOnlyList<SlideShowMediaEntryItemPlan> Items)
{
    public IReadOnlyList<SlideShowMediaShapePlan> Active =>
        Items.Select(item => item.Surface).ToArray();

    public bool HasPlayableSource => Items.Any(item => item.Surface.HasSource);
}

public sealed record SlideShowMediaActiveSlotMonitorPlan(
    PresentationMediaTranscriptTrackDescriptor? CaptionTrack,
    SlideShowMediaPlaybackHandle? Playback);

public sealed record SlideShowMediaPlaybackProjectionPlan(
    PresentationMediaOverlayPlacement Placement,
    bool ShowVisual);

public sealed record SlideShowMediaCaptionProjectionPlan(
    PresentationMediaTranscriptCueDescriptor? Cue,
    PresentationMediaOverlayPlacement? Placement);

public readonly record struct SlideShowMediaTrimWindow(
    TimeSpan Start,
    TimeSpan End)
{
    public bool IsTrimmed => Start > TimeSpan.Zero || End < TimeSpan.MaxValue;
}

public enum SlideShowMediaEndAction
{
    Stop,
    Rewind,
    Loop,
}

/// <summary>
/// Shared slideshow media hit-testing and source policy. WPF and Avalonia keep
/// native playback optional, but they must agree on hit rectangles and consume
/// media clicks before the normal slideshow advance route.
/// </summary>
public static class SlideShowMediaInteractionPlanner
{
    public const string PlaybackBackendCapabilityNote =
        "LibVLC cross-platform audio/video playback is available when the native runtime is present; poster rendering and media click routing remain available as fallback.";

    public static IReadOnlyList<SlideShowMediaShapePlan> BuildSlidePlan(
        Slide slide,
        double slideDipW,
        double slideDipH,
        double canvasW,
        double canvasH,
        bool showMediaControls = true,
        bool showNarration = true)
    {
        ArgumentNullException.ThrowIfNull(slide);

        return EnumerateEligibleShapes(slide, showNarration)
            .Select(shape => BuildShapePlan(shape, slideDipW, slideDipH, canvasW, canvasH, showMediaControls))
            .ToArray();
    }

    public static SlideShowMediaSlideEntryPlan PlanSlideEntry(
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
        bool showNarration = true)
    {
        ArgumentNullException.ThrowIfNull(slide);

        var items = EnumerateEligibleShapes(slide, showNarration)
            .Select(shape => new SlideShowMediaEntryItemPlan(
                shape,
                BuildShapePlan(
                    shape,
                    slideDipW,
                    slideDipH,
                    canvasW,
                    canvasH,
                    showMediaControls),
                SelectCaptionTrack(
                    captionTracks,
                    shape.Id,
                    preferredCaptionShapeId,
                    preferredCaptionTrackIndex,
                    captionSlideIndex,
                    preferredCaptionSlideIndex)))
            .ToArray();
        return new SlideShowMediaSlideEntryPlan(items);
    }

    public static bool ShouldRunPeriodicUpdates(
        IEnumerable<SlideShowMediaActiveSlotMonitorPlan> slots,
        SlideShowMediaPlaybackSession playbackSession)
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(playbackSession);

        return slots.Any(slot =>
            slot.CaptionTrack is not null
            || slot.Playback is { } playback
            && playbackSession.RequiresPeriodicUpdate(playback));
    }

    public static SlideShowMediaPlaybackProjectionPlan PlanPlaybackProjection(
        LayoutRect authoredBounds,
        PresentationMediaTranscriptTrackDescriptor? captionTrack,
        TimeSpan playbackPosition,
        double canvasWidth,
        double canvasHeight,
        SlideShowMediaPlaybackSnapshot playback)
    {
        ArgumentNullException.ThrowIfNull(playback);
        return new(
            PresentationMediaTranscriptPlanner.PlanOverlayPlacement(
                new PresentationMediaOverlayPlacementRequest(
                    authoredBounds,
                    canvasWidth,
                    canvasHeight,
                    playback.UseFullScreen,
                    PresentationMediaTranscriptPlanner.FindActiveCue(captionTrack, playbackPosition),
                    captionTrack?.Regions)),
            playback.ShowVisual);
    }

    public static SlideShowMediaCaptionProjectionPlan PlanCaptionProjection(
        Slide? activeSlide,
        uint shapeId,
        PresentationMediaTranscriptTrackDescriptor? captionTrack,
        TimeSpan? playbackPosition,
        bool useFullScreen,
        double slideWidthDip,
        double slideHeightDip,
        double canvasWidth,
        double canvasHeight)
    {
        var cue = playbackPosition is { } position
            ? PresentationMediaTranscriptPlanner.FindActiveCue(captionTrack, position)
            : null;
        if (activeSlide is null
            || SlideShapeTraversal.FindById(activeSlide, shapeId) is not { Media: not null } shape)
        {
            return new(cue, null);
        }

        var bounds = ComputeMediaBounds(
            shape,
            slideWidthDip,
            slideHeightDip,
            canvasWidth,
            canvasHeight);
        return new(
            cue,
            PresentationMediaTranscriptPlanner.PlanOverlayPlacement(
                new PresentationMediaOverlayPlacementRequest(
                    bounds,
                    canvasWidth,
                    canvasHeight,
                    useFullScreen,
                    cue,
                    captionTrack?.Regions)));
    }

    public static SlideShowMediaClickPlan PlanClick(
        Slide slide,
        double slideDipW,
        double slideDipH,
        double canvasW,
        double canvasH,
        double canvasX,
        double canvasY,
        bool showMediaControls = true,
        bool showNarration = true)
    {
        foreach (var media in BuildSlidePlan(
            slide, slideDipW, slideDipH, canvasW, canvasH, showMediaControls, showNarration).Reverse())
        {
            if (canvasX >= media.Bounds.Left && canvasX <= media.Bounds.Right &&
                canvasY >= media.Bounds.Top && canvasY <= media.Bounds.Bottom)
                return new SlideShowMediaClickPlan(true, true, media);
        }

        return SlideShowMediaClickPlan.NotMedia;
    }

    public static LayoutRect ComputeMediaBounds(
        SlideShape shape,
        double slideDipW,
        double slideDipH,
        double canvasW,
        double canvasH)
    {
        ArgumentNullException.ThrowIfNull(shape);

        var scale = canvasW > 0 && canvasH > 0 && slideDipW > 0 && slideDipH > 0
            ? Math.Min(canvasW / slideDipW, canvasH / slideDipH)
            : 1.0;
        var offsetX = (canvasW - slideDipW * scale) / 2;
        var offsetY = (canvasH - slideDipH * scale) / 2;

        var shapeX = shape.OffsetXEmu / 9525.0;
        var shapeY = shape.OffsetYEmu / 9525.0;
        var shapeW = shape.ExtentCxEmu / 9525.0;
        var shapeH = shape.ExtentCyEmu / 9525.0;

        return new LayoutRect(
            offsetX + shapeX * scale,
            offsetY + shapeY * scale,
            shapeW * scale,
            shapeH * scale);
    }

    /// <summary>
    /// Keeps the host-independent media volume contract in the shared 0-100 range.
    /// Native WPF and LibVLC adapters consume different representations of this value.
    /// </summary>
    public static int NormalizeVolumePercent(int volume) => Math.Clamp(volume, 0, 100);

    /// <summary>
    /// Resolves PowerPoint's trim-from-start/trim-from-end values against the
    /// duration reported by the active playback engine. Unknown durations keep
    /// the start trim (which can be applied before playback) and leave the end
    /// open until the engine reports a duration.
    /// </summary>
    public static SlideShowMediaTrimWindow ResolveTrimWindow(
        MediaInfo media,
        TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(media);

        var start = PositiveMilliseconds(media.TrimStartMilliseconds);
        if (duration <= TimeSpan.Zero)
            return new SlideShowMediaTrimWindow(start, TimeSpan.MaxValue);

        var endTrim = PositiveMilliseconds(media.TrimEndMilliseconds);
        var end = duration - endTrim;
        if (end < start)
            end = start;

        return new SlideShowMediaTrimWindow(start, end);
    }

    public static bool IsAtOrPastTrimEnd(
        MediaInfo media,
        TimeSpan position,
        TimeSpan duration)
    {
        var window = ResolveTrimWindow(media, duration);
        return window.End != TimeSpan.MaxValue && position >= window.End;
    }

    public static TimeSpan ClampToTrimStart(MediaInfo media, TimeSpan position)
    {
        ArgumentNullException.ThrowIfNull(media);
        var start = PositiveMilliseconds(media.TrimStartMilliseconds);
        return position < start ? start : position;
    }

    public static SlideShowMediaEndAction ResolveEndAction(MediaInfo media)
    {
        ArgumentNullException.ThrowIfNull(media);
        return media.Loop
            ? SlideShowMediaEndAction.Loop
            : media.RewindAfterPlaying
                ? SlideShowMediaEndAction.Rewind
                : SlideShowMediaEndAction.Stop;
    }

    /// <summary>
    /// Resolves a named media bookmark to a seek position while respecting the
    /// active trim window. Bookmark names are user-facing labels, so lookup is
    /// trimmed and case-insensitive; duplicate names resolve to the first entry.
    /// </summary>
    public static bool TryResolveMediaBookmarkPosition(
        MediaInfo media,
        string bookmarkName,
        TimeSpan duration,
        out TimeSpan position)
    {
        ArgumentNullException.ThrowIfNull(media);
        position = TimeSpan.Zero;
        var normalizedName = bookmarkName?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
            return false;

        var bookmark = media.Bookmarks.FirstOrDefault(candidate =>
            string.Equals(candidate.Name?.Trim(), normalizedName, StringComparison.OrdinalIgnoreCase));
        if (bookmark is null || !double.IsFinite(bookmark.TimeMilliseconds) || bookmark.TimeMilliseconds < 0)
            return false;

        position = TimeSpan.FromMilliseconds(
            Math.Min(bookmark.TimeMilliseconds, TimeSpan.MaxValue.TotalMilliseconds));
        var window = ResolveTrimWindow(media, duration);
        if (position < window.Start)
            position = window.Start;
        else if (window.End != TimeSpan.MaxValue && position > window.End)
            position = window.End;
        return true;
    }

    /// <summary>
    /// Computes the current playback volume after applying authored fade-in and
    /// fade-out durations. The returned value remains in the shared 0-100 range.
    /// </summary>
    public static int ComputeEffectiveVolumePercent(
        MediaInfo media,
        int baseVolumePercent,
        TimeSpan position,
        TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(media);

        var baseVolume = NormalizeVolumePercent(baseVolumePercent);
        var fadeIn = PositiveMilliseconds(media.FadeInMilliseconds);
        var fadeOut = PositiveMilliseconds(media.FadeOutMilliseconds);
        if (fadeIn == TimeSpan.Zero && fadeOut == TimeSpan.Zero)
            return baseVolume;

        var window = ResolveTrimWindow(media, duration);
        var factor = 1d;
        if (fadeIn > TimeSpan.Zero)
        {
            var elapsed = position - window.Start;
            factor = Math.Min(factor, elapsed <= TimeSpan.Zero
                ? 0d
                : Math.Clamp(elapsed.TotalMilliseconds / fadeIn.TotalMilliseconds, 0d, 1d));
        }

        if (fadeOut > TimeSpan.Zero && window.End != TimeSpan.MaxValue)
        {
            var remaining = window.End - position;
            factor = Math.Min(factor, remaining <= TimeSpan.Zero
                ? 0d
                : Math.Clamp(remaining.TotalMilliseconds / fadeOut.TotalMilliseconds, 0d, 1d));
        }

        return (int)Math.Round(baseVolume * factor, MidpointRounding.AwayFromZero);
    }

    private static TimeSpan PositiveMilliseconds(double value) =>
        value > 0 && double.IsFinite(value)
            ? TimeSpan.FromMilliseconds(Math.Min(value, TimeSpan.MaxValue.TotalMilliseconds))
            : TimeSpan.Zero;

    private static SlideShowMediaShapePlan BuildShapePlan(
        SlideShape shape,
        double slideDipW,
        double slideDipH,
        double canvasW,
        double canvasH,
        bool showMediaControls)
    {
        var media = shape.Media!;
        var hasEmbeddedSource = media.Bytes is { Length: > 0 };
        var hasLinkedSource = Uri.TryCreate(media.LinkUrl, UriKind.Absolute, out var link) &&
            link.Scheme is "http" or "https";

        return new SlideShowMediaShapePlan(
            shape.Id,
            media.IsVideo,
            ComputeMediaBounds(shape, slideDipW, slideDipH, canvasW, canvasH),
            hasEmbeddedSource || hasLinkedSource,
            hasEmbeddedSource ? "embedded" : hasLinkedSource ? "http-link" : "missing",
            PlaybackBackendCapabilityNote,
            showMediaControls,
            media.ShowWhenStopped);
    }

    private static PresentationMediaTranscriptTrackDescriptor? SelectCaptionTrack(
        IReadOnlyList<PresentationMediaTranscriptTrackDescriptor>? captionTracks,
        uint shapeId,
        uint? preferredCaptionShapeId,
        int? preferredCaptionTrackIndex,
        int? captionSlideIndex,
        int? preferredCaptionSlideIndex) =>
        captionSlideIndex is int currentSlideIndex
            ? PresentationMediaTranscriptPlanner.SelectPlaybackTrack(
                captionTracks,
                currentSlideIndex,
                shapeId,
                preferredCaptionSlideIndex,
                preferredCaptionShapeId == shapeId ? preferredCaptionTrackIndex : null)
            : PresentationMediaTranscriptPlanner.SelectPlaybackTrack(
                captionTracks,
                shapeId,
                preferredCaptionShapeId == shapeId ? preferredCaptionTrackIndex : null);

    private static IEnumerable<SlideShape> EnumerateEligibleShapes(
        Slide slide,
        bool showNarration) =>
        SlideShapeTraversal.EnumerateDepthFirst(slide).Where(shape =>
            shape.Kind == SlideShapeKind.Media
            && shape.Media is not null
            && (showNarration || shape.Media.IsVideo));
}
