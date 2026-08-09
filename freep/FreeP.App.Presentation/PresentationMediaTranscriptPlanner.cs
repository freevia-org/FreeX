using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum PresentationMediaTranscriptTrackStatus
{
    Available,
    UnsupportedFormat,
    External,
    NoBytes
}

public enum PresentationMediaTranscriptCueAlignment
{
    Start,
    Center,
    End,
    Left,
    Right
}

public enum PresentationMediaTranscriptCueWritingMode
{
    Horizontal,
    VerticalRightToLeft,
    VerticalLeftToRight
}

public sealed record PresentationMediaCaptionPlacement(
    double X,
    double Y,
    double Width,
    double Height,
    double RotationDegrees = 0);

public sealed record PresentationMediaTranscriptRegionDescriptor(
    string Id,
    double WidthPercent = 100,
    int Lines = 3,
    double RegionAnchorXPercent = 0,
    double RegionAnchorYPercent = 100,
    double ViewportAnchorXPercent = 0,
    double ViewportAnchorYPercent = 100,
    string? Scroll = null);

public sealed record PresentationMediaTranscriptCueSpan(
    string Text,
    bool Bold = false,
    bool Italic = false,
    bool Underline = false)
{
    public string? Voice { get; init; }

    public string? Language { get; init; }

    public IReadOnlyList<string> Classes { get; init; } = [];

    public string? ForegroundColorHex { get; init; }

    public string? BackgroundColorHex { get; init; }

    public string? FontFamily { get; init; }

    /// <summary>Absolute CSS caption size normalized to device-independent pixels.</summary>
    public double? FontSizePx { get; init; }
}

public sealed record PresentationMediaTranscriptCueDescriptor(
    TimeSpan StartTime,
    TimeSpan EndTime,
    string Text)
{
    public string? RegionId { get; init; }

    public IReadOnlyList<PresentationMediaTranscriptCueSpan> Spans { get; init; } = [];

    public string? WebVttMarkup { get; init; }

    public double? PositionPercent { get; init; }

    public double? LinePercent { get; init; }

    /// <summary>
    /// WebVTT snap-to-lines position.  Zero is the first line from the leading edge;
    /// negative values count backward from the trailing edge.  A non-null value is
    /// mutually exclusive with <see cref="LinePercent"/>.
    /// </summary>
    public int? LineNumber { get; init; }

    public double? SizePercent { get; init; }

    public PresentationMediaTranscriptCueAlignment Alignment { get; init; } =
        PresentationMediaTranscriptCueAlignment.Center;

    public PresentationMediaTranscriptCueWritingMode WritingMode { get; init; } =
        PresentationMediaTranscriptCueWritingMode.Horizontal;

    public string StartTimeText => FormatTime(StartTime);

    public string EndTimeText => FormatTime(EndTime);

    public string TimeRangeText => $"{StartTimeText} - {EndTimeText}";

    private static string FormatTime(TimeSpan value)
        => value.Hours > 0
            ? value.ToString(@"h\:mm\:ss\.fff", CultureInfo.InvariantCulture)
            : value.ToString(@"m\:ss\.fff", CultureInfo.InvariantCulture);
}

public sealed record PresentationMediaTranscriptTrackDescriptor(
    int SlideIndex,
    uint ShapeId,
    string ShapeName,
    int TrackIndex,
    string Label,
    string Language,
    string Source,
    string ContentType,
    PresentationMediaTranscriptTrackStatus Status,
    string StatusMessage,
    IReadOnlyList<PresentationMediaTranscriptCueDescriptor> Cues)
{
    public string? WebVttStyleSheet { get; init; }

    public IReadOnlyList<PresentationMediaTranscriptRegionDescriptor> Regions { get; init; } = [];

    public int CueCount => Cues.Count;

    public bool HasTranscript => Status == PresentationMediaTranscriptTrackStatus.Available && Cues.Count > 0;
}

public sealed record PresentationMediaTranscriptPlan(
    int SlideCount,
    int MediaShapeCount,
    int TrackCount,
    int CueCount,
    IReadOnlyList<PresentationMediaTranscriptTrackDescriptor> Tracks);

public sealed record PresentationMediaCaptionTrackAuthoringDescriptor(
    string? Label,
    string? Language,
    string? Source,
    string? TranscriptText,
    IReadOnlyList<PresentationMediaTranscriptCueDescriptor>? Cues = null,
    string? WebVttStyleSheet = null,
    IReadOnlyList<PresentationMediaTranscriptRegionDescriptor>? Regions = null);

public sealed record PresentationMediaCaptionTrackMutationResult(
    bool Succeeded,
    string? ErrorMessage,
    int TrackIndex,
    MediaCaptionTrackInfo? Track)
{
    public static PresentationMediaCaptionTrackMutationResult Success(int trackIndex, MediaCaptionTrackInfo track) =>
        new(true, null, trackIndex, track);

    public static PresentationMediaCaptionTrackMutationResult Deleted(int trackIndex, MediaCaptionTrackInfo track) =>
        new(true, null, trackIndex, track);

    public static PresentationMediaCaptionTrackMutationResult Failure(string errorMessage) =>
        new(false, errorMessage, -1, null);
}

public enum PresentationMediaCaptionAuthoringIntentKind
{
    Create,
    Replace,
    Delete,
    Close
}

public sealed record PresentationMediaCaptionAuthoringFieldPlan(
    string Label,
    string Value,
    string Placeholder,
    bool IsEnabled,
    string? ValidationMessage);

public sealed record PresentationMediaCaptionAuthoringTrackPlan(
    int TrackIndex,
    string Label,
    string Language,
    string Source,
    PresentationMediaTranscriptTrackStatus Status,
    bool IsExternal,
    bool CanReplace,
    bool CanDelete,
    bool IsSelected)
{
    public bool IsAvailable => !IsExternal;

    public string AvailabilityLabel => IsAvailable ? "available" : "unavailable";

    public string DisplayText => $"{TrackIndex + 1}. {Label} ({AvailabilityLabel})";
}

public sealed record PresentationMediaCaptionAuthoringActionPlan(
    string CommandId,
    string Label,
    PresentationMediaCaptionAuthoringIntentKind Intent,
    bool IsEnabled,
    string? DisabledReason);

public sealed record PresentationMediaCaptionAuthoringPanePlan(
    int SlideIndex,
    uint? ShapeId,
    string ShapeName,
    int SelectedTrackIndex,
    int SelectedTrackListIndex,
    string Message,
    PresentationMediaCaptionAuthoringFieldPlan Label,
    PresentationMediaCaptionAuthoringFieldPlan Language,
    PresentationMediaCaptionAuthoringFieldPlan Source,
    PresentationMediaCaptionAuthoringFieldPlan TranscriptText,
    IReadOnlyList<PresentationMediaCaptionAuthoringTrackPlan> Tracks,
    IReadOnlyList<PresentationMediaCaptionAuthoringActionPlan> Actions)
{
    public bool HasSelectedMedia => ShapeId.HasValue;

    public bool HasSelectedTrack => SelectedTrackIndex >= 0;

    public PresentationMediaCaptionAuthoringTrackPlan? SelectedTrack =>
        Tracks.FirstOrDefault(track => track.TrackIndex == SelectedTrackIndex);

    public string Heading => PresentationPaneTextResources.BuildMediaCaptionsHeading(ShapeName);
}

public sealed record PresentationMediaCaptionAuthoringMutationPlan(
    bool ShouldApply,
    PresentationMediaCaptionAuthoringIntentKind Intent,
    int TrackIndex,
    PresentationMediaCaptionTrackAuthoringDescriptor? Descriptor,
    string? ErrorMessage);

public static class PresentationMediaTranscriptPlanner
{
    public const string CaptionAuthoringPaneOpenCommandId = "freep.media-captions.open";
    public const string CaptionAuthoringPaneCreateCommandId = "freep.media-captions.create";
    public const string CaptionAuthoringPaneReplaceCommandId = "freep.media-captions.replace";
    public const string CaptionAuthoringPaneDeleteCommandId = "freep.media-captions.delete";
    public const string CaptionAuthoringPaneCloseCommandId = "freep.media-captions.close";

    public const string MissingMediaMessage = "Media object is required.";
    public const string MissingSelectedMediaMessage = "Select one media shape to author captions.";
    public const string MissingCaptionTrackMessage = "Caption track was not found.";
    public const string ExternalCaptionTrackMessage = "External caption links are replaced with an internal caption track when authored.";
    public const string MissingCaptionDescriptorMessage = "Caption authoring descriptor is required.";
    public const string MissingCaptionContentMessage = "Caption authoring requires typed cues or transcript text.";
    public const string AmbiguousCaptionContentMessage = "Caption authoring accepts either typed cues or transcript text, not both.";
    public const string EmptyCaptionContentMessage = "Caption authoring requires at least one valid cue.";
    public const string InvalidCaptionCueTimingMessage = "Caption cues must have non-negative, increasing, non-overlapping time ranges.";
    public const string InvalidCaptionSourceMessage = "Internal caption track source must be a relative .vtt, .srt, .ttml, or .dfxp package path or file name.";
    public const string CaptionAuthoringReadyMessage = "Author internal WebVTT, SRT, TTML, or DFXP caption tracks for the selected media.";
    public const string CaptionAuthoringExternalTrackMessage = "External caption links can be inspected, replaced with authored captions, or deleted.";

    private enum CaptionTrackFormat
    {
        Unsupported,
        WebVtt,
        Srt,
        Ttml
    }

    private sealed record WebVttCueStyle(
        string? ForegroundColorHex,
        string? BackgroundColorHex,
        bool Bold,
        bool Italic,
        bool Underline,
        string? FontFamily,
        double? FontSizePx);

    private sealed record TtmlSpanStyle(
        bool Bold = false,
        bool Italic = false,
        bool Underline = false,
        string? ForegroundColorHex = null,
        string? BackgroundColorHex = null,
        string? FontFamily = null,
        double? FontSizePx = null,
        string? Voice = null,
        string? Language = null,
        PresentationMediaTranscriptCueAlignment Alignment = PresentationMediaTranscriptCueAlignment.Center,
        PresentationMediaTranscriptCueWritingMode WritingMode = PresentationMediaTranscriptCueWritingMode.Horizontal,
        double? PositionPercent = null,
        double? LinePercent = null,
        double? SizePercent = null,
        bool HasExplicitStyle = false,
        bool HasLayoutStyle = false);

    private static readonly Regex TagPattern = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.Compiled);

    public static PresentationMediaCaptionTrackMutationResult CreateInternalCaptionTrack(
        MediaInfo? media,
        PresentationMediaCaptionTrackAuthoringDescriptor? descriptor)
    {
        if (media is null)
        {
            return PresentationMediaCaptionTrackMutationResult.Failure(MissingMediaMessage);
        }

        if (!TryBuildInternalCaptionTrack(media.CaptionTracks.Count, descriptor, existingTrack: null, out var track, out var errorMessage))
        {
            return PresentationMediaCaptionTrackMutationResult.Failure(errorMessage);
        }

        media.CaptionTracks.Add(track);
        return PresentationMediaCaptionTrackMutationResult.Success(media.CaptionTracks.Count - 1, track);
    }

    public static PresentationMediaCaptionTrackMutationResult ReplaceInternalCaptionTrack(
        MediaInfo? media,
        int trackIndex,
        PresentationMediaCaptionTrackAuthoringDescriptor? descriptor)
    {
        if (media is null)
        {
            return PresentationMediaCaptionTrackMutationResult.Failure(MissingMediaMessage);
        }

        if (!TryGetCaptionTrack(media, trackIndex, out var existingTrack))
        {
            return PresentationMediaCaptionTrackMutationResult.Failure(MissingCaptionTrackMessage);
        }

        if (!TryBuildInternalCaptionTrack(trackIndex, descriptor, existingTrack, out var replacement, out var errorMessage))
        {
            return PresentationMediaCaptionTrackMutationResult.Failure(errorMessage);
        }

        media.CaptionTracks[trackIndex] = replacement;
        return PresentationMediaCaptionTrackMutationResult.Success(trackIndex, replacement);
    }

    /// <summary>
    /// Removes an internal or external caption relationship. External deletion does not
    /// touch the linked resource; it only removes the track from the media object.
    /// </summary>
    public static PresentationMediaCaptionTrackMutationResult DeleteInternalCaptionTrack(
        MediaInfo? media,
        int trackIndex)
    {
        if (media is null)
        {
            return PresentationMediaCaptionTrackMutationResult.Failure(MissingMediaMessage);
        }

        if (!TryGetCaptionTrack(media, trackIndex, out var track))
        {
            return PresentationMediaCaptionTrackMutationResult.Failure(MissingCaptionTrackMessage);
        }

        media.CaptionTracks.RemoveAt(trackIndex);
        return PresentationMediaCaptionTrackMutationResult.Deleted(trackIndex, track);
    }

    public static SlideShape? FindSelectedMediaShape(
        Slide? slide,
        IReadOnlyList<uint>? selectedShapeIds)
    {
        if (slide is null || selectedShapeIds is not { Count: 1 })
        {
            return null;
        }

        var selectedShapeId = selectedShapeIds[0];
        return EnumerateShapes(slide.Shapes).FirstOrDefault(shape =>
            shape.Id == selectedShapeId
            && shape.Kind == SlideShapeKind.Media
            && shape.Media is not null);
    }

    public static PresentationMediaTranscriptTrackDescriptor? SelectPlaybackTrack(
        IReadOnlyList<PresentationMediaTranscriptTrackDescriptor>? tracks,
        uint shapeId,
        int? preferredTrackIndex = null)
        => SelectPlaybackTrackCore(
            tracks,
            slideIndex: null,
            shapeId,
            preferredSlideIndex: null,
            preferredTrackIndex);

    public static PresentationMediaTranscriptTrackDescriptor? SelectPlaybackTrack(
        IReadOnlyList<PresentationMediaTranscriptTrackDescriptor>? tracks,
        int slideIndex,
        uint shapeId,
        int? preferredSlideIndex,
        int? preferredTrackIndex)
        => SelectPlaybackTrackCore(
            tracks,
            slideIndex,
            shapeId,
            preferredSlideIndex,
            preferredTrackIndex);

    private static PresentationMediaTranscriptTrackDescriptor? SelectPlaybackTrackCore(
        IReadOnlyList<PresentationMediaTranscriptTrackDescriptor>? tracks,
        int? slideIndex,
        uint shapeId,
        int? preferredSlideIndex,
        int? preferredTrackIndex)
    {
        if (tracks is null)
        {
            return null;
        }

        var shapeTracks = tracks.Where(track =>
            (!slideIndex.HasValue || track.SlideIndex == slideIndex)
            && track.ShapeId == shapeId
            && track.HasTranscript).ToArray();
        var selectedTrackIndex = preferredSlideIndex == slideIndex
            ? preferredTrackIndex
            : null;
        return selectedTrackIndex is int preferred
            ? shapeTracks.FirstOrDefault(track => track.TrackIndex == preferred)
                ?? shapeTracks.FirstOrDefault()
            : shapeTracks.FirstOrDefault();
    }

    public static PresentationMediaCaptionAuthoringPanePlan BuildCaptionAuthoringPanePlan(
        Slide? slide,
        int slideIndex,
        IReadOnlyList<uint>? selectedShapeIds,
        int? selectedTrackIndex,
        string? proposedLabel,
        string? proposedLanguage,
        string? proposedSource,
        string? proposedTranscriptText)
    {
        var mediaShape = FindSelectedMediaShape(slide, selectedShapeIds);
        if (mediaShape?.Media is not { } media)
        {
            return EmptyCaptionAuthoringPanePlan(slideIndex);
        }

        var normalizedTrackIndex = NormalizeSelectedTrackIndex(media, selectedTrackIndex);
        var tracks = new List<PresentationMediaCaptionAuthoringTrackPlan>();
        for (var index = 0; index < media.CaptionTracks.Count; index++)
        {
            var descriptor = BuildTrack(slideIndex, mediaShape, index, media.CaptionTracks[index]);
            tracks.Add(new PresentationMediaCaptionAuthoringTrackPlan(
                index,
                descriptor.Label,
                descriptor.Language,
                descriptor.Source,
                descriptor.Status,
                media.CaptionTracks[index].IsExternal,
                true,
                !media.CaptionTracks[index].IsExternal,
                index == normalizedTrackIndex));
        }

        var selectedTrack = normalizedTrackIndex >= 0 ? media.CaptionTracks[normalizedTrackIndex] : null;
        var enabled = true;
        var labelValue = proposedLabel ?? NormalizeText(selectedTrack?.Label) ?? string.Empty;
        var languageValue = proposedLanguage ?? NormalizeText(selectedTrack?.Language) ?? string.Empty;
        var sourceValue = proposedSource ?? NormalizeText(selectedTrack?.Source) ?? string.Empty;
        var transcriptValue = proposedTranscriptText ?? DecodeCaptionAuthoringText(selectedTrack);

        var descriptorForValidation = new PresentationMediaCaptionTrackAuthoringDescriptor(
            labelValue,
            languageValue,
            sourceValue,
            transcriptValue);
        var createError = ValidateCaptionAuthoringMutation(
            media,
            PresentationMediaCaptionAuthoringIntentKind.Create,
            media.CaptionTracks.Count,
            descriptorForValidation);
        var replaceError = ValidateCaptionAuthoringMutation(
            media,
            PresentationMediaCaptionAuthoringIntentKind.Replace,
            normalizedTrackIndex,
            descriptorForValidation);
        var deleteError = ValidateCaptionAuthoringMutation(
            media,
            PresentationMediaCaptionAuthoringIntentKind.Delete,
            normalizedTrackIndex,
            descriptor: null);
        var selectedIsExternal = selectedTrack?.IsExternal == true;
        var message = selectedIsExternal
            ? CaptionAuthoringExternalTrackMessage
            : CaptionAuthoringReadyMessage;

        return new PresentationMediaCaptionAuthoringPanePlan(
            slideIndex,
            mediaShape.Id,
            DescribeShape(mediaShape),
            normalizedTrackIndex,
            tracks.FindIndex(track => track.IsSelected),
            message,
            new PresentationMediaCaptionAuthoringFieldPlan("Label", labelValue, "English captions", enabled, null),
            new PresentationMediaCaptionAuthoringFieldPlan("Language", languageValue, "en-US", enabled, null),
            new PresentationMediaCaptionAuthoringFieldPlan("Package path", sourceValue, "ppt/media/authored-captions.vtt", enabled, createError == InvalidCaptionSourceMessage || replaceError == InvalidCaptionSourceMessage ? InvalidCaptionSourceMessage : null),
            new PresentationMediaCaptionAuthoringFieldPlan("Transcript", transcriptValue, "WEBVTT caption text or SRT transcript text", enabled, FirstContentError(createError, replaceError)),
            tracks,
            [
                new PresentationMediaCaptionAuthoringActionPlan(CaptionAuthoringPaneCreateCommandId, "Create", PresentationMediaCaptionAuthoringIntentKind.Create, createError is null, createError),
                new PresentationMediaCaptionAuthoringActionPlan(CaptionAuthoringPaneReplaceCommandId, "Replace", PresentationMediaCaptionAuthoringIntentKind.Replace, replaceError is null, replaceError),
                new PresentationMediaCaptionAuthoringActionPlan(CaptionAuthoringPaneDeleteCommandId, "Delete", PresentationMediaCaptionAuthoringIntentKind.Delete, deleteError is null, deleteError),
                new PresentationMediaCaptionAuthoringActionPlan(CaptionAuthoringPaneCloseCommandId, "Close", PresentationMediaCaptionAuthoringIntentKind.Close, true, null)
            ]);
    }

    public static PresentationMediaCaptionAuthoringMutationPlan BuildCaptionAuthoringMutationPlan(
        MediaInfo? media,
        PresentationMediaCaptionAuthoringIntentKind intent,
        int trackIndex,
        PresentationMediaCaptionTrackAuthoringDescriptor? descriptor)
    {
        if (intent == PresentationMediaCaptionAuthoringIntentKind.Close)
        {
            return new(false, intent, trackIndex, descriptor, null);
        }

        var errorMessage = ValidateCaptionAuthoringMutation(media, intent, trackIndex, descriptor);
        return new PresentationMediaCaptionAuthoringMutationPlan(
            errorMessage is null,
            intent,
            trackIndex,
            descriptor,
            errorMessage);
    }

    public static PresentationMediaCaptionTrackMutationResult ApplyCaptionAuthoringMutation(
        MediaInfo? media,
        PresentationMediaCaptionAuthoringMutationPlan plan)
    {
        if (!plan.ShouldApply)
        {
            return PresentationMediaCaptionTrackMutationResult.Failure(
                plan.ErrorMessage ?? MissingCaptionDescriptorMessage);
        }

        return plan.Intent switch
        {
            PresentationMediaCaptionAuthoringIntentKind.Create =>
                CreateInternalCaptionTrack(media, plan.Descriptor),
            PresentationMediaCaptionAuthoringIntentKind.Replace =>
                ReplaceInternalCaptionTrack(media, plan.TrackIndex, plan.Descriptor),
            PresentationMediaCaptionAuthoringIntentKind.Delete =>
                DeleteInternalCaptionTrack(media, plan.TrackIndex),
            _ => PresentationMediaCaptionTrackMutationResult.Failure(MissingCaptionDescriptorMessage)
        };
    }

    public static PresentationMediaTranscriptPlan BuildTranscriptPlan(Presentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var tracks = new List<PresentationMediaTranscriptTrackDescriptor>();
        var mediaShapeCount = 0;

        for (var slideIndex = 0; slideIndex < presentation.Slides.Count; slideIndex++)
        {
            var slide = presentation.Slides[slideIndex];
            foreach (var shape in EnumerateShapes(slide.Shapes))
            {
                if (shape.Kind != SlideShapeKind.Media || shape.Media is not { } media)
                {
                    continue;
                }

                mediaShapeCount++;
                for (var trackIndex = 0; trackIndex < media.CaptionTracks.Count; trackIndex++)
                {
                    tracks.Add(BuildTrack(slideIndex, shape, trackIndex, media.CaptionTracks[trackIndex]));
                }
            }
        }

        return new PresentationMediaTranscriptPlan(
            presentation.Slides.Count,
            mediaShapeCount,
            tracks.Count,
            tracks.Sum(track => track.Cues.Count),
            tracks);
    }

    /// <summary>
    /// Resolves the caption visible at a media playback position.  Cues are already normalized
    /// and validated by <see cref="BuildTranscriptPlan"/>, so the playback hosts can share the
    /// same half-open interval semantics without parsing caption formats themselves.
    /// </summary>
    public static PresentationMediaTranscriptCueDescriptor? FindActiveCue(
        PresentationMediaTranscriptTrackDescriptor? track,
        TimeSpan position)
    {
        if (track is null || !track.HasTranscript || position < TimeSpan.Zero)
            return null;

        return track.Cues.FirstOrDefault(cue =>
            position >= cue.StartTime && position < cue.EndTime);
    }

    public static PresentationMediaCaptionPlacement ComputeCaptionPlacement(
        PresentationMediaTranscriptCueDescriptor? cue,
        double mediaWidth,
        double mediaHeight,
        double defaultHeight,
        IReadOnlyList<PresentationMediaTranscriptRegionDescriptor>? regions = null)
    {
        if (cue?.RegionId is { Length: > 0 } regionId
            && regions?.FirstOrDefault(region =>
                string.Equals(region.Id, regionId, StringComparison.OrdinalIgnoreCase)) is { } region)
        {
            return ComputeRegionCaptionPlacement(region, mediaWidth, mediaHeight, defaultHeight, cue.WritingMode);
        }

        var writingMode = cue?.WritingMode ?? PresentationMediaTranscriptCueWritingMode.Horizontal;
        if (writingMode is not PresentationMediaTranscriptCueWritingMode.Horizontal)
            return ComputeVerticalCaptionPlacement(cue, mediaWidth, mediaHeight, defaultHeight, writingMode);

        var widthPercent = Math.Clamp(cue?.SizePercent ?? 100, 1, 100);
        var width = Math.Max(1, mediaWidth * widthPercent / 100);
        var height = Math.Max(1, defaultHeight);
        var positionPercent = Math.Clamp(cue?.PositionPercent ?? 50, 0, 100);
        var anchorX = mediaWidth * positionPercent / 100;
        var alignment = cue?.Alignment ?? PresentationMediaTranscriptCueAlignment.Center;
        var x = alignment is PresentationMediaTranscriptCueAlignment.Start
            or PresentationMediaTranscriptCueAlignment.Left
            ? anchorX
            : alignment is PresentationMediaTranscriptCueAlignment.End
                or PresentationMediaTranscriptCueAlignment.Right
                    ? anchorX - width
                    : anchorX - width / 2;
        x = Math.Clamp(x, 0, Math.Max(0, mediaWidth - width));

        var y = cue?.LineNumber is { } lineNumber
            ? ResolveHorizontalLineNumber(lineNumber, mediaHeight, height)
            : cue?.LinePercent is { } linePercent
                ? mediaHeight * Math.Clamp(linePercent, 0, 100) / 100
                : mediaHeight - height;
        y = Math.Clamp(y, 0, Math.Max(0, mediaHeight - height));

        return new PresentationMediaCaptionPlacement(x, y, width, height);
    }

    private static PresentationMediaCaptionPlacement ComputeRegionCaptionPlacement(
        PresentationMediaTranscriptRegionDescriptor region,
        double mediaWidth,
        double mediaHeight,
        double defaultHeight,
        PresentationMediaTranscriptCueWritingMode writingMode)
    {
        var width = Math.Max(1, mediaWidth * Math.Clamp(region.WidthPercent, 1, 100) / 100);
        var height = Math.Max(1, defaultHeight * Math.Max(1, region.Lines));
        var viewportX = mediaWidth * Math.Clamp(region.ViewportAnchorXPercent, 0, 100) / 100;
        var viewportY = mediaHeight * Math.Clamp(region.ViewportAnchorYPercent, 0, 100) / 100;
        var regionX = width * Math.Clamp(region.RegionAnchorXPercent, 0, 100) / 100;
        var regionY = height * Math.Clamp(region.RegionAnchorYPercent, 0, 100) / 100;
        var x = Math.Clamp(viewportX - regionX, 0, Math.Max(0, mediaWidth - width));
        var y = Math.Clamp(viewportY - regionY, 0, Math.Max(0, mediaHeight - height));
        var rotation = writingMode == PresentationMediaTranscriptCueWritingMode.VerticalRightToLeft
            ? 90
            : writingMode == PresentationMediaTranscriptCueWritingMode.VerticalLeftToRight
                ? -90
                : 0;
        return new PresentationMediaCaptionPlacement(x, y, width, height, rotation);
    }

    private static PresentationMediaCaptionPlacement ComputeVerticalCaptionPlacement(
        PresentationMediaTranscriptCueDescriptor? cue,
        double mediaWidth,
        double mediaHeight,
        double defaultHeight,
        PresentationMediaTranscriptCueWritingMode writingMode)
    {
        var height = Math.Max(1, mediaHeight * Math.Clamp(cue?.SizePercent ?? 100, 1, 100) / 100);
        var width = Math.Max(1, defaultHeight);
        var positionPercent = Math.Clamp(cue?.PositionPercent ?? 50, 0, 100);
        var anchorY = mediaHeight * positionPercent / 100;
        var alignment = cue?.Alignment ?? PresentationMediaTranscriptCueAlignment.Center;
        var y = alignment is PresentationMediaTranscriptCueAlignment.Start
            or PresentationMediaTranscriptCueAlignment.Left
            ? anchorY
            : alignment is PresentationMediaTranscriptCueAlignment.End
                or PresentationMediaTranscriptCueAlignment.Right
                    ? anchorY - height
                    : anchorY - height / 2;
        y = Math.Clamp(y, 0, Math.Max(0, mediaHeight - height));

        var defaultLinePercent = writingMode == PresentationMediaTranscriptCueWritingMode.VerticalRightToLeft
            ? 100
            : 0;
        var x = cue?.LineNumber is { } lineNumber
            ? ResolveVerticalLineNumber(lineNumber, mediaWidth, width, writingMode)
            : ResolveVerticalPercentLine(cue?.LinePercent, defaultLinePercent, mediaWidth, width, writingMode);
        x = Math.Clamp(x, 0, Math.Max(0, mediaWidth - width));

        var rotation = writingMode == PresentationMediaTranscriptCueWritingMode.VerticalRightToLeft
            ? 90
            : -90;
        return new PresentationMediaCaptionPlacement(x, y, width, height, rotation);
    }

    private static double ResolveHorizontalLineNumber(int lineNumber, double mediaHeight, double lineHeight)
        => lineNumber >= 0
            ? lineNumber * lineHeight
            : mediaHeight - Math.Abs((double)lineNumber) * lineHeight;

    private static double ResolveVerticalLineNumber(
        int lineNumber,
        double mediaWidth,
        double lineWidth,
        PresentationMediaTranscriptCueWritingMode writingMode)
    {
        if (writingMode == PresentationMediaTranscriptCueWritingMode.VerticalRightToLeft)
        {
            return lineNumber >= 0
                ? mediaWidth - (lineNumber + 1) * lineWidth
                : (Math.Abs((double)lineNumber) - 1) * lineWidth;
        }

        return lineNumber >= 0
            ? lineNumber * lineWidth
            : mediaWidth - Math.Abs((double)lineNumber) * lineWidth;
    }

    private static double ResolveVerticalPercentLine(
        double? linePercent,
        double defaultLinePercent,
        double mediaWidth,
        double lineWidth,
        PresentationMediaTranscriptCueWritingMode writingMode)
    {
        var percent = Math.Clamp(linePercent ?? defaultLinePercent, 0, 100);
        var lineAnchorX = mediaWidth * percent / 100;
        return writingMode == PresentationMediaTranscriptCueWritingMode.VerticalRightToLeft
            ? lineAnchorX - lineWidth
            : lineAnchorX;
    }

    private static PresentationMediaCaptionAuthoringPanePlan EmptyCaptionAuthoringPanePlan(int slideIndex)
        => new(
            slideIndex,
            null,
            string.Empty,
            -1,
            -1,
            MissingSelectedMediaMessage,
            new PresentationMediaCaptionAuthoringFieldPlan("Label", string.Empty, "English captions", false, null),
            new PresentationMediaCaptionAuthoringFieldPlan("Language", string.Empty, "en-US", false, null),
            new PresentationMediaCaptionAuthoringFieldPlan("Package path", string.Empty, "ppt/media/authored-captions.vtt", false, null),
            new PresentationMediaCaptionAuthoringFieldPlan("Transcript", string.Empty, "WEBVTT caption text or SRT transcript text", false, null),
            [],
            [
                new PresentationMediaCaptionAuthoringActionPlan(CaptionAuthoringPaneCreateCommandId, "Create", PresentationMediaCaptionAuthoringIntentKind.Create, false, MissingSelectedMediaMessage),
                new PresentationMediaCaptionAuthoringActionPlan(CaptionAuthoringPaneReplaceCommandId, "Replace", PresentationMediaCaptionAuthoringIntentKind.Replace, false, MissingSelectedMediaMessage),
                new PresentationMediaCaptionAuthoringActionPlan(CaptionAuthoringPaneDeleteCommandId, "Delete", PresentationMediaCaptionAuthoringIntentKind.Delete, false, MissingSelectedMediaMessage),
                new PresentationMediaCaptionAuthoringActionPlan(CaptionAuthoringPaneCloseCommandId, "Close", PresentationMediaCaptionAuthoringIntentKind.Close, true, null)
            ]);

    private static int NormalizeSelectedTrackIndex(MediaInfo media, int? selectedTrackIndex)
    {
        if (selectedTrackIndex is { } requested
            && requested >= 0
            && requested < media.CaptionTracks.Count)
        {
            return requested;
        }

        for (var index = 0; index < media.CaptionTracks.Count; index++)
        {
            if (!media.CaptionTracks[index].IsExternal)
            {
                return index;
            }
        }

        return media.CaptionTracks.Count > 0 ? 0 : -1;
    }

    private static string? ValidateCaptionAuthoringMutation(
        MediaInfo? media,
        PresentationMediaCaptionAuthoringIntentKind intent,
        int trackIndex,
        PresentationMediaCaptionTrackAuthoringDescriptor? descriptor)
    {
        if (media is null)
        {
            return MissingMediaMessage;
        }

        return intent switch
        {
            PresentationMediaCaptionAuthoringIntentKind.Create =>
                TryBuildInternalCaptionTrack(media.CaptionTracks.Count, descriptor, existingTrack: null, out _, out var createError)
                    ? null
                    : createError,
            PresentationMediaCaptionAuthoringIntentKind.Replace =>
                ValidateReplaceCaptionAuthoringMutation(media, trackIndex, descriptor),
            PresentationMediaCaptionAuthoringIntentKind.Delete =>
                ValidateDeleteCaptionAuthoringMutation(media, trackIndex),
            _ => MissingCaptionDescriptorMessage
        };
    }

    private static string? ValidateReplaceCaptionAuthoringMutation(
        MediaInfo media,
        int trackIndex,
        PresentationMediaCaptionTrackAuthoringDescriptor? descriptor)
    {
        if (!TryGetCaptionTrack(media, trackIndex, out var existingTrack))
        {
            return MissingCaptionTrackMessage;
        }

        return TryBuildInternalCaptionTrack(trackIndex, descriptor, existingTrack, out _, out var errorMessage)
            ? null
            : errorMessage;
    }

    private static string? ValidateDeleteCaptionAuthoringMutation(MediaInfo media, int trackIndex)
    {
        if (!TryGetCaptionTrack(media, trackIndex, out var existingTrack))
        {
            return MissingCaptionTrackMessage;
        }

        return null;
    }

    private static string? FirstContentError(params string?[] errorMessages)
        => errorMessages.FirstOrDefault(message => message is MissingCaptionContentMessage
            or AmbiguousCaptionContentMessage
            or EmptyCaptionContentMessage
            or InvalidCaptionCueTimingMessage);

    private static string DecodeCaptionAuthoringText(MediaCaptionTrackInfo? track)
        => track is { IsExternal: false, Bytes: { Length: > 0 } }
            ? DecodeUtf8(track.Bytes)
            : string.Empty;

    private static bool TryBuildInternalCaptionTrack(
        int trackIndex,
        PresentationMediaCaptionTrackAuthoringDescriptor? descriptor,
        MediaCaptionTrackInfo? existingTrack,
        out MediaCaptionTrackInfo track,
        out string errorMessage)
    {
        track = new MediaCaptionTrackInfo();
        errorMessage = string.Empty;

        if (descriptor is null)
        {
            errorMessage = MissingCaptionDescriptorMessage;
            return false;
        }

        if (!TryBuildAuthoredCues(descriptor, out var cues, out errorMessage))
        {
            return false;
        }

        var requestedSource = descriptor.Source;
        if (existingTrack?.IsExternal == true && IsExternalCaptionSource(requestedSource))
        {
            // The selected field contains the external URI. Replacing it means creating
            // an embedded package part, so keep that display value out of the new target.
            requestedSource = null;
        }

        var format = ResolveAuthoringFormat(requestedSource, existingTrack);
        var source = NormalizeCaptionSource(
            requestedSource,
            existingTrack?.IsExternal == true ? null : existingTrack?.Source,
            trackIndex,
            format);
        if (source is null)
        {
            errorMessage = InvalidCaptionSourceMessage;
            return false;
        }

        var existingText = existingTrack is { IsExternal: false }
            ? DecodeUtf8(existingTrack.Bytes)
            : string.Empty;
        var regions = descriptor.Regions
            ?? (format == CaptionTrackFormat.WebVtt
                ? ParseWebVttRegions(!string.IsNullOrWhiteSpace(descriptor.TranscriptText)
                    ? descriptor.TranscriptText
                    : existingText)
                : []);

        track = new MediaCaptionTrackInfo
        {
            RelationshipId = existingTrack?.RelationshipId ?? string.Empty,
            Source = source,
            Bytes = BuildCaptionBytes(
                cues,
                format,
                descriptor.WebVttStyleSheet
                    ?? ExtractWebVttStyleSheet(existingTrack is { IsExternal: false }
                        ? DecodeUtf8(existingTrack.Bytes)
                        : string.Empty),
                regions),
            ContentType = GetCaptionContentType(source, format),
            Language = NormalizeText(descriptor.Language) ?? NormalizeText(existingTrack?.Language) ?? string.Empty,
            Label = NormalizeText(descriptor.Label) ?? NormalizeText(existingTrack?.Label) ?? InferTrackLabel(source, trackIndex),
            IsExternal = false
        };

        return true;
    }

    private static bool TryBuildAuthoredCues(
        PresentationMediaCaptionTrackAuthoringDescriptor descriptor,
        out IReadOnlyList<PresentationMediaTranscriptCueDescriptor> cues,
        out string errorMessage)
    {
        var hasTypedCues = descriptor.Cues is { Count: > 0 };
        var hasTranscript = !string.IsNullOrWhiteSpace(descriptor.TranscriptText);

        cues = [];
        errorMessage = string.Empty;

        if (!hasTypedCues && !hasTranscript)
        {
            errorMessage = MissingCaptionContentMessage;
            return false;
        }

        if (hasTypedCues && hasTranscript)
        {
            errorMessage = AmbiguousCaptionContentMessage;
            return false;
        }

        cues = hasTypedCues
            ? NormalizeAuthoredCues(descriptor.Cues!)
            : ParseCaptionTranscriptText(descriptor.TranscriptText!);

        if (cues.Count == 0)
        {
            errorMessage = EmptyCaptionContentMessage;
            return false;
        }

        if (!ValidateCaptionCueTiming(cues))
        {
            errorMessage = InvalidCaptionCueTimingMessage;
            return false;
        }

        return true;
    }

    private static IReadOnlyList<PresentationMediaTranscriptCueDescriptor> NormalizeAuthoredCues(
        IEnumerable<PresentationMediaTranscriptCueDescriptor> cues)
    {
        var normalized = new List<PresentationMediaTranscriptCueDescriptor>();
        foreach (var cue in cues)
        {
            var text = CollapseWhitespace(cue.Text);
            if (text.Length == 0)
            {
                continue;
            }

            normalized.Add(cue with { Text = text });
        }

        return normalized;
    }

    private static IReadOnlyList<PresentationMediaTranscriptCueDescriptor> ParseCaptionTranscriptText(string text)
    {
        var normalizedText = DecodeUtf8(Encoding.UTF8.GetBytes(text));
        var format = DetectFormat(
            new MediaCaptionTrackInfo
            {
                Source = normalizedText.TrimStart().StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase)
                    ? "captions.vtt"
                    : normalizedText.IndexOf("<tt", StringComparison.OrdinalIgnoreCase) >= 0
                        ? "captions.ttml"
                        : "captions.srt"
            },
            normalizedText);

        return format switch
        {
            CaptionTrackFormat.WebVtt => ParseWebVtt(normalizedText),
            CaptionTrackFormat.Srt => ParseSrt(normalizedText),
            CaptionTrackFormat.Ttml => ParseTtml(normalizedText),
            _ => []
        };
    }

    private static bool ValidateCaptionCueTiming(IReadOnlyList<PresentationMediaTranscriptCueDescriptor> cues)
    {
        var previousEnd = TimeSpan.Zero;
        for (var index = 0; index < cues.Count; index++)
        {
            var cue = cues[index];
            if (cue.StartTime < TimeSpan.Zero
                || cue.EndTime <= cue.StartTime
                || (index > 0 && cue.StartTime < previousEnd))
            {
                return false;
            }

            previousEnd = cue.EndTime;
        }

        return true;
    }

    private static byte[] BuildWebVttBytes(
        IReadOnlyList<PresentationMediaTranscriptCueDescriptor> cues,
        string? styleSheet = null,
        IReadOnlyList<PresentationMediaTranscriptRegionDescriptor>? regions = null)
    {
        var builder = new StringBuilder("WEBVTT\r\n\r\n");
        if (!string.IsNullOrWhiteSpace(styleSheet))
        {
            builder.Append(styleSheet.Trim()).Append("\r\n\r\n");
        }

        foreach (var region in regions ?? [])
        {
            builder
                .Append("REGION\r\n")
                .Append("id:").Append(region.Id).Append("\r\n")
                .Append("width:").Append(region.WidthPercent.ToString("0.###", CultureInfo.InvariantCulture)).Append("%\r\n")
                .Append("lines:").Append(region.Lines.ToString(CultureInfo.InvariantCulture)).Append("\r\n")
                .Append("regionanchor:")
                .Append(region.RegionAnchorXPercent.ToString("0.###", CultureInfo.InvariantCulture)).Append('%')
                .Append(',')
                .Append(region.RegionAnchorYPercent.ToString("0.###", CultureInfo.InvariantCulture)).Append("%\r\n")
                .Append("viewportanchor:")
                .Append(region.ViewportAnchorXPercent.ToString("0.###", CultureInfo.InvariantCulture)).Append('%')
                .Append(',')
                .Append(region.ViewportAnchorYPercent.ToString("0.###", CultureInfo.InvariantCulture)).Append("%\r\n");
            if (!string.IsNullOrWhiteSpace(region.Scroll))
                builder.Append("scroll:").Append(region.Scroll).Append("\r\n");
            builder.Append("\r\n");
        }

        foreach (var cue in cues)
        {
            builder
                .Append(FormatWebVttTimestamp(cue.StartTime))
                .Append(" --> ")
                .Append(FormatWebVttTimestamp(cue.EndTime));
            AppendWebVttSettings(builder, cue);
            builder
                .Append("\r\n")
                .Append(cue.WebVttMarkup ?? EscapeWebVttText(cue.Text))
                .Append("\r\n\r\n");
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static void AppendWebVttSettings(
        StringBuilder builder,
        PresentationMediaTranscriptCueDescriptor cue)
    {
        if (cue.Alignment != PresentationMediaTranscriptCueAlignment.Center)
        {
            builder.Append(" align:").Append(cue.Alignment switch
            {
                PresentationMediaTranscriptCueAlignment.Start => "start",
                PresentationMediaTranscriptCueAlignment.End => "end",
                PresentationMediaTranscriptCueAlignment.Left => "left",
                PresentationMediaTranscriptCueAlignment.Right => "right",
                _ => "center"
            });
        }

        if (cue.PositionPercent is { } position)
            builder.Append(" position:").Append(position.ToString("0.###", CultureInfo.InvariantCulture)).Append('%');
        if (cue.LinePercent is { } line)
            builder.Append(" line:").Append(line.ToString("0.###", CultureInfo.InvariantCulture)).Append('%');
        else if (cue.LineNumber is { } lineNumber)
            builder.Append(" line:").Append(lineNumber.ToString(CultureInfo.InvariantCulture));
        if (cue.SizePercent is { } size)
            builder.Append(" size:").Append(size.ToString("0.###", CultureInfo.InvariantCulture)).Append('%');
        if (cue.WritingMode != PresentationMediaTranscriptCueWritingMode.Horizontal)
            builder.Append(" vertical:").Append(cue.WritingMode == PresentationMediaTranscriptCueWritingMode.VerticalRightToLeft ? "rl" : "lr");
        if (!string.IsNullOrWhiteSpace(cue.RegionId))
            builder.Append(" region:").Append(cue.RegionId);
    }

    private static byte[] BuildCaptionBytes(
        IReadOnlyList<PresentationMediaTranscriptCueDescriptor> cues,
        CaptionTrackFormat format,
        string? webVttStyleSheet = null,
        IReadOnlyList<PresentationMediaTranscriptRegionDescriptor>? regions = null)
        => format switch
        {
            CaptionTrackFormat.WebVtt => BuildWebVttBytes(cues, webVttStyleSheet, regions),
            CaptionTrackFormat.Srt => BuildSrtBytes(cues),
            CaptionTrackFormat.Ttml => BuildTtmlBytes(cues),
            _ => BuildWebVttBytes(cues)
        };

    private static byte[] BuildSrtBytes(IReadOnlyList<PresentationMediaTranscriptCueDescriptor> cues)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < cues.Count; index++)
        {
            var cue = cues[index];
            builder
                .Append(index + 1)
                .Append("\r\n")
                .Append(FormatSrtTimestamp(cue.StartTime))
                .Append(" --> ")
                .Append(FormatSrtTimestamp(cue.EndTime))
                .Append("\r\n")
                .Append(cue.Text)
                .Append("\r\n\r\n");
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static string FormatSrtTimestamp(TimeSpan value)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{(long)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00},{value.Milliseconds:000}");

    private static byte[] BuildTtmlBytes(IReadOnlyList<PresentationMediaTranscriptCueDescriptor> cues)
    {
        XNamespace ttml = "http://www.w3.org/ns/ttml";
        XNamespace tts = "http://www.w3.org/ns/ttml#styling";
        XNamespace ttm = "http://www.w3.org/ns/ttml#metadata";
        XNamespace xml = "http://www.w3.org/XML/1998/namespace";
        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(
                ttml + "tt",
                new XElement(
                    ttml + "body",
                    new XElement(
                        ttml + "div",
                        cues.Select(cue => BuildTtmlParagraph(cue, ttml, tts, ttm, xml))))));

        return Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting));
    }

    private static XElement BuildTtmlParagraph(
        PresentationMediaTranscriptCueDescriptor cue,
        XNamespace ttml,
        XNamespace tts,
        XNamespace ttm,
        XNamespace xml)
    {
        var content = new List<object>
        {
            new XAttribute("begin", FormatTtmlTimestamp(cue.StartTime)),
            new XAttribute("end", FormatTtmlTimestamp(cue.EndTime))
        };
        content.AddRange(BuildTtmlLayoutAttributes(cue, tts).Cast<object>());
        content.AddRange(BuildTtmlCueContent(cue, ttml, tts, ttm, xml));
        return new XElement(ttml + "p", content);
    }

    private static IEnumerable<XAttribute> BuildTtmlLayoutAttributes(
        PresentationMediaTranscriptCueDescriptor cue,
        XNamespace tts)
    {
        if (cue.Alignment != PresentationMediaTranscriptCueAlignment.Center)
        {
            yield return new XAttribute(
                tts + "textAlign",
                cue.Alignment switch
                {
                    PresentationMediaTranscriptCueAlignment.Start => "start",
                    PresentationMediaTranscriptCueAlignment.End => "end",
                    PresentationMediaTranscriptCueAlignment.Left => "left",
                    PresentationMediaTranscriptCueAlignment.Right => "right",
                    _ => "center"
                });
        }

        if (cue.WritingMode != PresentationMediaTranscriptCueWritingMode.Horizontal)
        {
            yield return new XAttribute(
                tts + "writingMode",
                cue.WritingMode == PresentationMediaTranscriptCueWritingMode.VerticalRightToLeft
                    ? "tbrl"
                    : "tblr");
        }

        if (cue.PositionPercent is { } position
            && cue.LinePercent is { } line)
        {
            var originX = cue.WritingMode == PresentationMediaTranscriptCueWritingMode.Horizontal
                ? position
                : line;
            var originY = cue.WritingMode == PresentationMediaTranscriptCueWritingMode.Horizontal
                ? line
                : position;
            yield return new XAttribute(
                tts + "origin",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{originX:0.###}% {originY:0.###}%"));
        }

        if (cue.SizePercent is { } size)
        {
            var extent = cue.WritingMode == PresentationMediaTranscriptCueWritingMode.Horizontal
                ? $"{size:0.###}% auto"
                : $"auto {size:0.###}%";
            yield return new XAttribute(
                tts + "extent",
                extent);
        }
    }

    private static object[] BuildTtmlCueContent(
        PresentationMediaTranscriptCueDescriptor cue,
        XNamespace ttml,
        XNamespace tts,
        XNamespace ttm,
        XNamespace xml)
    {
        if (cue.Spans.Count == 0)
        {
            return [cue.Text];
        }

        return cue.Spans
            .Select(span => (object)new XElement(
                ttml + "span",
                BuildTtmlSpanContent(span, tts, ttm, xml)))
            .ToArray();
    }

    private static object[] BuildTtmlSpanContent(
        PresentationMediaTranscriptCueSpan span,
        XNamespace tts,
        XNamespace ttm,
        XNamespace xml)
        => BuildTtmlStyleAttributes(span, tts, ttm, xml)
            .Cast<object>()
            .Append(span.Text)
            .ToArray();

    private static IEnumerable<XAttribute> BuildTtmlStyleAttributes(
        PresentationMediaTranscriptCueSpan span,
        XNamespace tts,
        XNamespace ttm,
        XNamespace xml)
    {
        if (span.Bold)
        {
            yield return new XAttribute(tts + "fontWeight", "bold");
        }

        if (span.Italic)
        {
            yield return new XAttribute(tts + "fontStyle", "italic");
        }

        if (span.Underline)
        {
            yield return new XAttribute(tts + "textDecoration", "underline");
        }

        if (span.ForegroundColorHex is { Length: > 0 } foreground)
        {
            yield return new XAttribute(tts + "color", $"#{foreground}");
        }

        if (span.BackgroundColorHex is { Length: > 0 } background)
        {
            yield return new XAttribute(tts + "backgroundColor", $"#{background}");
        }

        if (span.FontFamily is { Length: > 0 } family)
        {
            yield return new XAttribute(tts + "fontFamily", family);
        }

        if (span.FontSizePx is { } fontSize)
        {
            yield return new XAttribute(
                tts + "fontSize",
                string.Create(CultureInfo.InvariantCulture, $"{fontSize:0.###}px"));
        }

        if (span.Voice is { Length: > 0 } voice)
        {
            yield return new XAttribute(ttm + "agent", voice);
        }

        if (span.Language is { Length: > 0 } language)
        {
            yield return new XAttribute(xml + "lang", language);
        }
    }

    private static string FormatTtmlTimestamp(TimeSpan value)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{(long)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds:000}");

    private static string FormatWebVttTimestamp(TimeSpan value)
    {
        var hours = (long)value.TotalHours;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{hours:00}:{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds:000}");
    }

    private static string EscapeWebVttText(string text)
        => text
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    private static string? NormalizeCaptionSource(
        string? requestedSource,
        string? existingSource,
        int trackIndex,
        CaptionTrackFormat format)
    {
        if (NormalizeText(requestedSource) is { } requested)
        {
            return NormalizeInternalCaptionSource(requested);
        }

        if (NormalizeText(existingSource) is { } existing
            && NormalizeInternalCaptionSource(existing) is { } reusable)
        {
            return reusable;
        }

        var extension = format switch
        {
            CaptionTrackFormat.Srt => "srt",
            CaptionTrackFormat.Ttml => "ttml",
            _ => "vtt"
        };
        var source = $"ppt/media/authored-captions{trackIndex + 1}.{extension}";
        return NormalizeInternalCaptionSource(source);
    }

    private static string? NormalizeInternalCaptionSource(string source)
    {
        source = source.Replace('\\', '/');

        if (Uri.TryCreate(source, UriKind.Absolute, out _)
            || source.StartsWith("/", StringComparison.Ordinal)
            || source.Split('/').Any(part => part is ".." or ".")
            || !HasExtension(source, ".vtt")
                && !HasExtension(source, ".srt")
                && !HasExtension(source, ".ttml")
                && !HasExtension(source, ".dfxp"))
        {
            return null;
        }

        return source;
    }

    private static CaptionTrackFormat ResolveAuthoringFormat(
        string? requestedSource,
        MediaCaptionTrackInfo? existingTrack)
    {
        if (NormalizeText(requestedSource) is { } requested)
        {
            return GetCaptionFormatFromSource(requested) ?? CaptionTrackFormat.Unsupported;
        }

        if (existingTrack is { IsExternal: false, Bytes.Length: > 0 })
        {
            var existingFormat = DetectFormat(existingTrack, DecodeUtf8(existingTrack.Bytes));
            if (existingFormat != CaptionTrackFormat.Unsupported)
            {
                return existingFormat;
            }
        }

        return GetCaptionFormatFromSource(existingTrack?.Source) ?? CaptionTrackFormat.WebVtt;
    }

    private static bool IsExternalCaptionSource(string? source)
    {
        var normalized = NormalizeText(source);
        return normalized is not null
            && (Uri.TryCreate(normalized, UriKind.Absolute, out _)
                || normalized.StartsWith("//", StringComparison.Ordinal));
    }

    private static CaptionTrackFormat? GetCaptionFormatFromSource(string? source)
    {
        if (HasExtension(source, ".vtt"))
        {
            return CaptionTrackFormat.WebVtt;
        }

        if (HasExtension(source, ".srt"))
        {
            return CaptionTrackFormat.Srt;
        }

        if (HasExtension(source, ".ttml") || HasExtension(source, ".dfxp"))
        {
            return CaptionTrackFormat.Ttml;
        }

        return null;
    }

    private static string GetCaptionContentType(string source, CaptionTrackFormat format)
        => format switch
        {
            CaptionTrackFormat.Srt => "application/x-subrip",
            CaptionTrackFormat.Ttml when HasExtension(source, ".dfxp") => "application/ttaf+xml",
            CaptionTrackFormat.Ttml => "application/ttml+xml",
            _ => "text/vtt"
        };

    private static bool TryGetCaptionTrack(MediaInfo media, int trackIndex, out MediaCaptionTrackInfo track)
    {
        if (trackIndex >= 0 && trackIndex < media.CaptionTracks.Count)
        {
            track = media.CaptionTracks[trackIndex];
            return true;
        }

        track = new MediaCaptionTrackInfo();
        return false;
    }

    private static PresentationMediaTranscriptTrackDescriptor BuildTrack(
        int slideIndex,
        SlideShape shape,
        int trackIndex,
        MediaCaptionTrackInfo track)
    {
        var label = NormalizeText(track.Label)
            ?? InferTrackLabel(track.Source, trackIndex);
        var language = NormalizeText(track.Language) ?? string.Empty;
        var source = NormalizeText(track.Source) ?? string.Empty;
        var contentType = NormalizeText(track.ContentType) ?? string.Empty;
        string? webVttStyleSheet = null;
        IReadOnlyList<PresentationMediaTranscriptRegionDescriptor> regions = [];

        if (track.IsExternal)
        {
            return Descriptor(
                PresentationMediaTranscriptTrackStatus.External,
                "External caption track is not used for transcript planning.",
                []);
        }

        if (track.Bytes.Length == 0)
        {
            return Descriptor(
                PresentationMediaTranscriptTrackStatus.NoBytes,
                "Caption track has no authored bytes.",
                []);
        }

        var text = DecodeUtf8(track.Bytes);
        var format = DetectFormat(track, text);
        webVttStyleSheet = format == CaptionTrackFormat.WebVtt
            ? ExtractWebVttStyleSheet(text)
            : null;
        regions = format == CaptionTrackFormat.WebVtt
            ? ParseWebVttRegions(text)
            : [];
        var cues = format switch
        {
            CaptionTrackFormat.WebVtt => ParseWebVtt(text),
            CaptionTrackFormat.Srt => ParseSrt(text),
            CaptionTrackFormat.Ttml => ParseTtml(text),
            _ => []
        };

        if (format == CaptionTrackFormat.Unsupported)
        {
            return Descriptor(
                PresentationMediaTranscriptTrackStatus.UnsupportedFormat,
                "Caption track format is not supported for transcript planning.",
                []);
        }

        return Descriptor(
            PresentationMediaTranscriptTrackStatus.Available,
            "Transcript generated from authored caption bytes.",
            cues);

        PresentationMediaTranscriptTrackDescriptor Descriptor(
            PresentationMediaTranscriptTrackStatus status,
            string statusMessage,
            IReadOnlyList<PresentationMediaTranscriptCueDescriptor> cues)
            => new(
                slideIndex,
                shape.Id,
                DescribeShape(shape),
                trackIndex,
                label,
                language,
                source,
                contentType,
                status,
                statusMessage,
                cues)
            {
                WebVttStyleSheet = webVttStyleSheet,
                Regions = regions
            };
    }

    private static CaptionTrackFormat DetectFormat(MediaCaptionTrackInfo track, string text)
    {
        if (IsWebVtt(track.ContentType, track.Source, text))
        {
            return CaptionTrackFormat.WebVtt;
        }

        if (IsSrt(track.ContentType, track.Source, text))
        {
            return CaptionTrackFormat.Srt;
        }

        if (IsTtml(track.ContentType, track.Source, text))
        {
            return CaptionTrackFormat.Ttml;
        }

        return CaptionTrackFormat.Unsupported;
    }

    private static bool IsWebVtt(string? contentType, string? source, string text)
        => ContainsIgnoreCase(contentType, "text/vtt")
            || HasExtension(source, ".vtt")
            || text.TrimStart().StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase);

    private static bool IsSrt(string? contentType, string? source, string text)
        => ContainsIgnoreCase(contentType, "application/x-subrip")
            || ContainsIgnoreCase(contentType, "text/srt")
            || HasExtension(source, ".srt")
            || LooksLikeSrt(text);

    private static bool IsTtml(string? contentType, string? source, string text)
        => ContainsIgnoreCase(contentType, "ttml")
            || ContainsIgnoreCase(contentType, "ttaf")
            || HasExtension(source, ".ttml")
            || HasExtension(source, ".dfxp")
            || text.TrimStart().StartsWith("<tt", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeSrt(string text)
        => EnumerateBlocks(text)
            .SelectMany(block => block)
            .Take(4)
            .Any(line => line.Contains("-->", StringComparison.Ordinal)
                && line.Contains(',', StringComparison.Ordinal));

    private static IReadOnlyList<PresentationMediaTranscriptCueDescriptor> ParseWebVtt(string text)
    {
        var cues = new List<PresentationMediaTranscriptCueDescriptor>();
        var styles = ParseWebVttStyles(text);

        foreach (var block in EnumerateBlocks(text))
        {
            if (block.Count == 0)
            {
                continue;
            }

            var first = block[0].Trim();
            if (first.StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase)
                || first.StartsWith("NOTE", StringComparison.OrdinalIgnoreCase)
                || first.StartsWith("STYLE", StringComparison.OrdinalIgnoreCase)
                || first.StartsWith("REGION", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var timingIndex = block.FindIndex(line => line.Contains("-->", StringComparison.Ordinal));
            if (timingIndex < 0
                || !TryParseTimingLine(
                    block[timingIndex],
                    out var start,
                    out var end,
                    out var positionPercent,
                    out var linePercent,
                    out var lineNumber,
                    out var sizePercent,
                    out var alignment,
                    out var writingMode,
                    out var regionId))
            {
                continue;
            }

            var cueLines = block.Skip(timingIndex + 1).ToArray();
            var cueText = BuildCueText(cueLines);
            if (cueText.Length == 0)
            {
                continue;
            }

            cues.Add(new PresentationMediaTranscriptCueDescriptor(start, end, cueText)
            {
                Spans = ParseWebVttSpans(cueLines, styles),
                WebVttMarkup = string.Join(" ", cueLines),
                PositionPercent = positionPercent,
                LinePercent = linePercent,
                LineNumber = lineNumber,
                SizePercent = sizePercent,
                Alignment = alignment,
                WritingMode = writingMode,
                RegionId = regionId
            });
        }

        return cues;
    }

    private static IReadOnlyList<PresentationMediaTranscriptRegionDescriptor> ParseWebVttRegions(string text)
    {
        var regions = new List<PresentationMediaTranscriptRegionDescriptor>();
        foreach (var block in EnumerateBlocks(text))
        {
            if (block.Count == 0
                || !block[0].Trim().Equals("REGION", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var values = block.Skip(1)
                .Select(line => line.Trim())
                .Select(line =>
                {
                    var separator = line.IndexOf(':');
                    return separator > 0
                        ? new KeyValuePair<string, string>(line[..separator].Trim().ToLowerInvariant(), line[(separator + 1)..].Trim())
                        : default;
                })
                .Where(pair => pair.Key.Length > 0)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

            if (!values.TryGetValue("id", out var id) || string.IsNullOrWhiteSpace(id))
                continue;

            var width = values.TryGetValue("width", out var widthText)
                ? ParseWebVttPercent(widthText) ?? 100
                : 100;
            var lines = values.TryGetValue("lines", out var linesText)
                && int.TryParse(linesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLines)
                ? Math.Max(1, parsedLines)
                : 3;
            var (regionAnchorX, regionAnchorY) = ParseWebVttAnchor(values, "regionanchor", 0, 100);
            var (viewportAnchorX, viewportAnchorY) = ParseWebVttAnchor(values, "viewportanchor", 0, 100);
            values.TryGetValue("scroll", out var scroll);
            regions.Add(new PresentationMediaTranscriptRegionDescriptor(
                id,
                width,
                lines,
                regionAnchorX,
                regionAnchorY,
                viewportAnchorX,
                viewportAnchorY,
                string.IsNullOrWhiteSpace(scroll) ? null : scroll));
        }

        return regions;
    }

    private static (double X, double Y) ParseWebVttAnchor(
        IReadOnlyDictionary<string, string> values,
        string key,
        double defaultX,
        double defaultY)
    {
        if (!values.TryGetValue(key, out var text))
            return (defaultX, defaultY);

        var parts = text.Split(',', 2, StringSplitOptions.TrimEntries);
        return parts.Length == 2
            ? (ParseWebVttPercent(parts[0]) ?? defaultX, ParseWebVttPercent(parts[1]) ?? defaultY)
            : (defaultX, defaultY);
    }

    private static string? ExtractWebVttStyleSheet(string text)
    {
        var styleBlocks = EnumerateBlocks(text)
            .Where(block => block.Count > 0
                && block[0].Trim().StartsWith("STYLE", StringComparison.OrdinalIgnoreCase))
            .Select(block => string.Join("\r\n", block))
            .ToArray();
        return styleBlocks.Length == 0 ? null : string.Join("\r\n\r\n", styleBlocks);
    }

    private static IReadOnlyDictionary<string, WebVttCueStyle> ParseWebVttStyles(string text)
    {
        var styles = new Dictionary<string, WebVttCueStyle>(StringComparer.OrdinalIgnoreCase);
        var styleSheet = ExtractWebVttStyleSheet(text);
        if (string.IsNullOrWhiteSpace(styleSheet))
        {
            return styles;
        }

        foreach (Match rule in Regex.Matches(
                     styleSheet,
                     @"::cue\(\.(?<class>[A-Za-z0-9_-]+)\)\s*\{(?<body>[^}]*)\}",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var style = new WebVttCueStyle(null, null, false, false, false, null, null);
            foreach (var declaration in rule.Groups["body"].Value.Split(';'))
            {
                var separator = declaration.IndexOf(':');
                if (separator <= 0)
                {
                    continue;
                }

                var property = declaration[..separator].Trim().ToLowerInvariant();
                var value = declaration[(separator + 1)..].Trim();
                style = property switch
                {
                    "color" => style with { ForegroundColorHex = NormalizeWebVttColor(value) ?? style.ForegroundColorHex },
                    "background-color" => style with { BackgroundColorHex = NormalizeWebVttColor(value) ?? style.BackgroundColorHex },
                    "font-family" => style with { FontFamily = NormalizeWebVttFontFamily(value) ?? style.FontFamily },
                    "font-size" => ParseWebVttFontSizePx(value) is { } size
                        ? style with { FontSizePx = size }
                        : style,
                    "font-weight" when value.Equals("bold", StringComparison.OrdinalIgnoreCase)
                        || int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var weight) && weight >= 600
                        => style with { Bold = true },
                    "font-style" when value.Equals("italic", StringComparison.OrdinalIgnoreCase)
                        => style with { Italic = true },
                    "text-decoration" when value.Contains("underline", StringComparison.OrdinalIgnoreCase)
                        => style with { Underline = true },
                    _ => style
                };
            }

            styles[rule.Groups["class"].Value] = style;
        }

        return styles;
    }

    private static string? NormalizeWebVttColor(string value)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith('#'))
        {
            var hex = normalized[1..];
            if (hex.Length == 3)
            {
                hex = string.Concat(hex.Select(character => $"{character}{character}"));
            }

            return hex.Length == 6
                && hex.All(character => Uri.IsHexDigit(character))
                ? hex.ToUpperInvariant()
                : null;
        }

        var rgb = Regex.Match(
            normalized,
            @"^rgb\(\s*(?<r>\d{1,3})\s*,\s*(?<g>\d{1,3})\s*,\s*(?<b>\d{1,3})\s*\)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!rgb.Success
            || !int.TryParse(rgb.Groups["r"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var red)
            || !int.TryParse(rgb.Groups["g"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var green)
            || !int.TryParse(rgb.Groups["b"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var blue)
            || red is < 0 or > 255
            || green is < 0 or > 255
            || blue is < 0 or > 255)
        {
            return null;
        }

        return $"{red:X2}{green:X2}{blue:X2}";
    }

    private static string? NormalizeWebVttFontFamily(string value)
    {
        var family = value
            .Split(',', 2, StringSplitOptions.TrimEntries)[0]
            .Trim()
            .Trim('"', '\'');
        return family.Length == 0 ? null : family;
    }

    private static double? ParseWebVttFontSizePx(string value)
    {
        var match = Regex.Match(
            value.Trim(),
            @"^(?<size>[0-9]+(?:\.[0-9]+)?)(?<unit>px|pt)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success
            || !double.TryParse(match.Groups["size"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var size)
            || size <= 0)
        {
            return null;
        }

        return match.Groups["unit"].Value.Equals("pt", StringComparison.OrdinalIgnoreCase)
            ? size * 96d / 72d
            : size;
    }

    private static IReadOnlyList<PresentationMediaTranscriptCueDescriptor> ParseSrt(string text)
    {
        var cues = new List<PresentationMediaTranscriptCueDescriptor>();

        foreach (var block in EnumerateBlocks(text))
        {
            var timingIndex = block.FindIndex(line => line.Contains("-->", StringComparison.Ordinal));
            if (timingIndex < 0
                || !TryParseTimingLine(block[timingIndex], out var start, out var end))
            {
                continue;
            }

            var cueText = BuildCueText(block.Skip(timingIndex + 1));
            if (cueText.Length == 0)
            {
                continue;
            }

            cues.Add(new PresentationMediaTranscriptCueDescriptor(start, end, cueText));
        }

        return cues;
    }

    private static IReadOnlyList<PresentationMediaTranscriptCueDescriptor> ParseTtml(string text)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return Array.Empty<PresentationMediaTranscriptCueDescriptor>();
        }

        var root = document.Root;
        var frameRate = ReadTtmlRate(root, "frameRate") ?? 30.0;
        var frameRateMultiplier = GetTtmlAttribute(root, "frameRateMultiplier");
        if (frameRateMultiplier is not null)
        {
            var multiplier = frameRateMultiplier
                .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (multiplier.Length == 2
                && double.TryParse(multiplier[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator)
                && double.TryParse(multiplier[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator)
                && numerator > 0
                && denominator > 0)
            {
                frameRate *= numerator / denominator;
            }
        }

        var tickRate = ReadTtmlRate(root, "tickRate") ?? 1.0;
        var regions = ReadTtmlRegions(root);

        var cues = new List<PresentationMediaTranscriptCueDescriptor>();
        if (root is not null)
        {
            CollectTtmlTimedElements(
                root,
                scheduledStart: TimeSpan.Zero,
                parentBegin: TimeSpan.Zero,
                inheritedEnd: null,
                frameRate,
                tickRate,
                inheritedStyle: new TtmlSpanStyle(),
                regions,
                cues);
        }
        return cues;
    }

    private static TimeSpan? CollectTtmlTimedElements(
        XElement element,
        TimeSpan scheduledStart,
        TimeSpan parentBegin,
        TimeSpan? inheritedEnd,
        double frameRate,
        double tickRate,
        TtmlSpanStyle inheritedStyle,
        IReadOnlyDictionary<string, XElement> regions,
        List<PresentationMediaTranscriptCueDescriptor> cues)
    {
        var regionStyle = GetTtmlAttribute(element, "region") is { } regionId
            && regions.TryGetValue(regionId, out var resolvedRegion)
                ? ApplyTtmlStyle(inheritedStyle, resolvedRegion)
                : inheritedStyle;
        var effectiveStyle = ApplyTtmlStyle(regionStyle, element);
        var elementEnd = inheritedEnd;
        if (TryParseTtmlTime(
                GetTtmlAttribute(element, "end"),
                frameRate,
                tickRate,
                out var explicitEnd))
        {
            var absoluteEnd = parentBegin + explicitEnd;
            elementEnd = elementEnd is null || absoluteEnd < elementEnd.Value
                ? absoluteEnd
                : elementEnd;
        }

        if (TryParseTtmlTime(
                GetTtmlAttribute(element, "dur"),
                frameRate,
                tickRate,
                out var duration))
        {
            var absoluteEnd = scheduledStart + duration;
            elementEnd = elementEnd is null || absoluteEnd < elementEnd.Value
                ? absoluteEnd
                : elementEnd;
        }

        if (string.Equals(element.Name.LocalName, "p", StringComparison.OrdinalIgnoreCase))
        {
            var beginToken = GetTtmlAttribute(element, "begin");
            if (beginToken is not null
                && !TryParseTtmlTime(beginToken, frameRate, tickRate, out _))
            {
                return elementEnd;
            }

            if (elementEnd is TimeSpan end
                && end > scheduledStart)
            {
                var cueText = CollapseWhitespace(element.Value);
                if (cueText.Length > 0)
                {
                    var cue = new PresentationMediaTranscriptCueDescriptor(
                        scheduledStart,
                        end,
                        cueText)
                    {
                        Spans = ParseTtmlSpans(element, effectiveStyle),
                        PositionPercent = effectiveStyle.PositionPercent,
                        LinePercent = effectiveStyle.LinePercent,
                        SizePercent = effectiveStyle.SizePercent,
                        Alignment = effectiveStyle.Alignment,
                        WritingMode = effectiveStyle.WritingMode
                    };
                    cues.Add(cue);
                }
            }

            return elementEnd;
        }

        var isSequential = string.Equals(
            GetTtmlAttribute(element, "timeContainer"),
            "seq",
            StringComparison.OrdinalIgnoreCase);
        var cursor = scheduledStart;
        TimeSpan? latestChildEnd = null;

        foreach (var child in element.Elements())
        {
            var childBegin = TimeSpan.Zero;
            var beginToken = GetTtmlAttribute(child, "begin");
            if (beginToken is not null
                && !TryParseTtmlTime(beginToken, frameRate, tickRate, out childBegin))
            {
                continue;
            }

            var childStart = (isSequential ? cursor : scheduledStart) + childBegin;
            var childEnd = CollectTtmlTimedElements(
                child,
                childStart,
                scheduledStart,
                elementEnd,
                frameRate,
                tickRate,
                effectiveStyle,
                regions,
                cues);
            if (childEnd is TimeSpan resolvedEnd)
            {
                latestChildEnd = latestChildEnd is null || resolvedEnd > latestChildEnd.Value
                    ? resolvedEnd
                    : latestChildEnd;
                if (isSequential)
                {
                    cursor = resolvedEnd;
                }
            }
        }

        return elementEnd ?? latestChildEnd;
    }

    private static IReadOnlyDictionary<string, XElement> ReadTtmlRegions(XElement? root)
    {
        var regions = new Dictionary<string, XElement>(StringComparer.Ordinal);
        if (root is null)
        {
            return regions;
        }

        foreach (var region in root.Descendants().Where(element =>
                     string.Equals(element.Name.LocalName, "region", StringComparison.OrdinalIgnoreCase)))
        {
            var id = GetTtmlAttribute(region, "id")?.Trim();
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            regions[id] = region;
        }

        return regions;
    }

    private static TtmlSpanStyle ApplyTtmlStyle(TtmlSpanStyle inherited, XElement element)
    {
        var result = inherited;
        var hasExplicitStyle = inherited.HasExplicitStyle;

        if (GetTtmlAttribute(element, "fontWeight") is { } fontWeight)
        {
            result = result with
            {
                Bold = fontWeight.Equals("bold", StringComparison.OrdinalIgnoreCase)
                    || (int.TryParse(fontWeight, NumberStyles.Integer, CultureInfo.InvariantCulture, out var weight)
                        && weight >= 600),
                HasExplicitStyle = true
            };
            hasExplicitStyle = true;
        }

        if (GetTtmlAttribute(element, "fontStyle") is { } fontStyle)
        {
            result = result with
            {
                Italic = fontStyle.Equals("italic", StringComparison.OrdinalIgnoreCase)
                    || fontStyle.Equals("oblique", StringComparison.OrdinalIgnoreCase),
                HasExplicitStyle = true
            };
            hasExplicitStyle = true;
        }

        if (GetTtmlAttribute(element, "textDecoration") is { } textDecoration)
        {
            result = result with
            {
                Underline = textDecoration.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
                    .Any(value => value.Equals("underline", StringComparison.OrdinalIgnoreCase)),
                HasExplicitStyle = true
            };
            hasExplicitStyle = true;
        }

        if (GetTtmlAttribute(element, "color") is { } foreground
            && NormalizeTtmlColor(foreground) is { } foregroundHex)
        {
            result = result with { ForegroundColorHex = foregroundHex, HasExplicitStyle = true };
            hasExplicitStyle = true;
        }

        if (GetTtmlAttribute(element, "backgroundColor") is { } background
            && NormalizeTtmlColor(background) is { } backgroundHex)
        {
            result = result with { BackgroundColorHex = backgroundHex, HasExplicitStyle = true };
            hasExplicitStyle = true;
        }

        if (GetTtmlAttribute(element, "fontFamily") is { } fontFamily
            && NormalizeWebVttFontFamily(fontFamily) is { } family)
        {
            result = result with { FontFamily = family, HasExplicitStyle = true };
            hasExplicitStyle = true;
        }

        if (GetTtmlAttribute(element, "fontSize") is { } fontSize
            && ParseWebVttFontSizePx(fontSize) is { } size)
        {
            result = result with { FontSizePx = size, HasExplicitStyle = true };
            hasExplicitStyle = true;
        }

        var voiceToken = GetTtmlAttribute(element, "agent")
            ?? GetTtmlAttribute(element, "voice");
        if (voiceToken is not null)
        {
            result = result with
            {
                Voice = voiceToken.Trim(),
                HasExplicitStyle = true
            };
            hasExplicitStyle = true;
        }

        if (GetTtmlAttribute(element, "lang") is { } language)
        {
            result = result with { Language = language.Trim(), HasExplicitStyle = true };
            hasExplicitStyle = true;
        }

        if (GetTtmlAttribute(element, "textAlign") is { } textAlign
            && ParseTtmlAlignment(textAlign) is { } alignment)
        {
            result = result with { Alignment = alignment, HasLayoutStyle = true };
        }

        if (GetTtmlAttribute(element, "displayAlign") is { } displayAlign
            && ParseTtmlDisplayAlignment(displayAlign) is { } displayLine)
        {
            result = result with { LinePercent = displayLine, HasLayoutStyle = true };
        }

        if (GetTtmlAttribute(element, "writingMode") is { } writingMode
            && ParseTtmlWritingMode(writingMode) is { } parsedWritingMode)
        {
            result = result with { WritingMode = parsedWritingMode, HasLayoutStyle = true };
        }

        if (GetTtmlAttribute(element, "origin") is { } origin
            && TryParseTtmlPairPercent(origin, out var originX, out var originY))
        {
            result = result with
            {
                PositionPercent = result.WritingMode == PresentationMediaTranscriptCueWritingMode.Horizontal
                    ? originX
                    : originY,
                LinePercent = result.WritingMode == PresentationMediaTranscriptCueWritingMode.Horizontal
                    ? originY
                    : originX,
                HasLayoutStyle = true
            };
        }

        if (GetTtmlAttribute(element, "extent") is { } extent
            && TryParseTtmlPairPercent(extent, allowAuto: true, out var extentWidth, out var extentHeight))
        {
            result = result with
            {
                SizePercent = result.WritingMode == PresentationMediaTranscriptCueWritingMode.Horizontal
                    ? extentWidth
                    : extentHeight,
                HasLayoutStyle = true
            };
        }

        return result with { HasExplicitStyle = hasExplicitStyle };
    }

    private static PresentationMediaTranscriptCueAlignment? ParseTtmlAlignment(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "start" => PresentationMediaTranscriptCueAlignment.Start,
            "end" => PresentationMediaTranscriptCueAlignment.End,
            "left" => PresentationMediaTranscriptCueAlignment.Left,
            "right" => PresentationMediaTranscriptCueAlignment.Right,
            "center" => PresentationMediaTranscriptCueAlignment.Center,
            _ => null
        };

    private static double? ParseTtmlDisplayAlignment(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "before" => 0,
            "center" => 50,
            "after" => 100,
            _ => null
        };

    private static PresentationMediaTranscriptCueWritingMode? ParseTtmlWritingMode(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "tbrl" => PresentationMediaTranscriptCueWritingMode.VerticalRightToLeft,
            "tblr" => PresentationMediaTranscriptCueWritingMode.VerticalLeftToRight,
            "lrtb" or "rltb" => PresentationMediaTranscriptCueWritingMode.Horizontal,
            _ => null
        };

    private static bool TryParseTtmlPairPercent(
        string value,
        out double first,
        out double second)
        => TryParseTtmlPairPercent(value, allowAuto: false, out first, out second);

    private static bool TryParseTtmlPairPercent(
        string value,
        bool allowAuto,
        out double first,
        out double second)
    {
        first = default;
        second = default;
        var parts = value.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            && TryParseTtmlPercentOrAuto(parts[0], allowAuto, out first)
            && TryParseTtmlPercentOrAuto(parts[1], allowAuto, out second);
    }

    private static bool TryParseTtmlPercentOrAuto(string value, bool allowAuto, out double result)
    {
        if (allowAuto && value.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            result = 100;
            return true;
        }

        return TryParseTtmlPercent(value, out result);
    }

    private static bool TryParseTtmlPercent(string value, out double result)
    {
        result = default;
        var normalized = value.Trim();
        if (!normalized.EndsWith('%')
            || !double.TryParse(
                normalized[..^1],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out result))
        {
            return false;
        }

        return result is >= 0 and <= 100;
    }

    private static IReadOnlyList<PresentationMediaTranscriptCueSpan> ParseTtmlSpans(
        XElement paragraph,
        TtmlSpanStyle inheritedStyle)
    {
        var spans = new List<PresentationMediaTranscriptCueSpan>();
        var hasExplicitStyle = inheritedStyle.HasExplicitStyle;
        var pendingWhitespace = false;
        foreach (var node in paragraph.Nodes())
        {
            CollectTtmlTextSpans(
                node,
                inheritedStyle,
                spans,
                ref hasExplicitStyle,
                ref pendingWhitespace);
        }

        if (!hasExplicitStyle || spans.Count == 0)
        {
            return [];
        }

        spans[0] = spans[0] with { Text = spans[0].Text.TrimStart() };
        spans[^1] = spans[^1] with { Text = spans[^1].Text.TrimEnd() };
        return spans.Where(span => span.Text.Length > 0).ToArray();
    }

    private static void CollectTtmlTextSpans(
        XNode node,
        TtmlSpanStyle inheritedStyle,
        List<PresentationMediaTranscriptCueSpan> spans,
        ref bool hasExplicitStyle,
        ref bool pendingWhitespace)
    {
        if (node is XText text)
        {
            var value = WhitespacePattern.Replace(WebUtility.HtmlDecode(text.Value), " ");
            if (value.Length == 0)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                pendingWhitespace |= spans.Count > 0;
                return;
            }

            if (pendingWhitespace
                && spans.Count > 0
                && !char.IsWhiteSpace(spans[^1].Text[^1])
                && !char.IsWhiteSpace(value[0]))
            {
                value = " " + value;
            }

            pendingWhitespace = false;

            spans.Add(new PresentationMediaTranscriptCueSpan(
                value,
                inheritedStyle.Bold,
                inheritedStyle.Italic,
                inheritedStyle.Underline)
            {
                Voice = inheritedStyle.Voice,
                Language = inheritedStyle.Language,
                ForegroundColorHex = inheritedStyle.ForegroundColorHex,
                BackgroundColorHex = inheritedStyle.BackgroundColorHex,
                FontFamily = inheritedStyle.FontFamily,
                FontSizePx = inheritedStyle.FontSizePx
            });
            return;
        }

        if (node is not XElement element)
        {
            return;
        }

        var effectiveStyle = ApplyTtmlStyle(inheritedStyle, element);
        hasExplicitStyle |= effectiveStyle.HasExplicitStyle;
        foreach (var child in element.Nodes())
        {
            CollectTtmlTextSpans(
                child,
                effectiveStyle,
                spans,
                ref hasExplicitStyle,
                ref pendingWhitespace);
        }
    }

    private static string? NormalizeTtmlColor(string value)
    {
        var normalized = NormalizeWebVttColor(value);
        if (normalized is not null)
        {
            return normalized;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "black" => "000000",
            "white" => "FFFFFF",
            "red" => "FF0000",
            "green" => "008000",
            "blue" => "0000FF",
            "yellow" => "FFFF00",
            "cyan" or "aqua" => "00FFFF",
            "magenta" or "fuchsia" => "FF00FF",
            _ => null
        };
    }

    private static string? GetTtmlAttribute(XElement? element, string localName) =>
        element?.Attributes()
            .FirstOrDefault(attribute =>
                string.Equals(attribute.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))
            ?.Value;

    private static double? ReadTtmlRate(XElement? root, string localName)
    {
        var token = GetTtmlAttribute(root, localName);
        return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && value > 0
            ? value
            : null;
    }

    private static bool TryParseTtmlTime(string? token, out TimeSpan value)
        => TryParseTtmlTime(token, 30.0, 1.0, out value);

    private static bool TryParseTtmlTime(
        string? token,
        double frameRate,
        double tickRate,
        out TimeSpan value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var normalized = token.Trim();
        foreach (var (suffix, multiplier) in new[]
        {
            ("ms", TimeSpan.TicksPerMillisecond / 1.0),
            ("h", TimeSpan.TicksPerHour / 1.0),
            ("m", TimeSpan.TicksPerMinute / 1.0),
            ("s", TimeSpan.TicksPerSecond / 1.0)
        })
        {
            if (!normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var number = normalized[..^suffix.Length];
            if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount)
                || amount < 0)
            {
                return false;
            }

            value = new TimeSpan(checked((long)Math.Round(amount * multiplier)));
            return true;
        }

        if (normalized.EndsWith('f')
            && double.TryParse(
                normalized[..^1],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var frames)
            && frames >= 0
            && frameRate > 0)
        {
            value = TimeSpan.FromSeconds(frames / frameRate);
            return true;
        }

        if (normalized.EndsWith('t')
            && double.TryParse(
                normalized[..^1],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var ticks)
            && ticks >= 0
            && tickRate > 0)
        {
            value = TimeSpan.FromSeconds(ticks / tickRate);
            return true;
        }

        var clockParts = normalized.Replace(';', ':').Split(':');
        if (clockParts.Length == 4
            && int.TryParse(clockParts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var clockHours)
            && int.TryParse(clockParts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var clockMinutes)
            && int.TryParse(clockParts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var clockSeconds)
            && double.TryParse(clockParts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var clockFrames)
            && clockHours >= 0
            && clockMinutes >= 0
            && clockSeconds >= 0
            && clockFrames >= 0
            && frameRate > 0)
        {
            value = TimeSpan.FromHours(clockHours)
                + TimeSpan.FromMinutes(clockMinutes)
                + TimeSpan.FromSeconds(clockSeconds + clockFrames / frameRate);
            return true;
        }

        return TryParseCaptionTime(normalized, out value);
    }

    private static bool TryParseTimingLine(string line, out TimeSpan start, out TimeSpan end)
        => TryParseTimingLine(
            line,
            out start,
            out end,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _);

    private static bool TryParseTimingLine(
        string line,
        out TimeSpan start,
        out TimeSpan end,
        out double? positionPercent,
        out double? linePercent,
        out int? lineNumber,
        out double? sizePercent,
        out PresentationMediaTranscriptCueAlignment alignment,
        out PresentationMediaTranscriptCueWritingMode writingMode,
        out string? regionId)
    {
        start = default;
        end = default;
        positionPercent = null;
        linePercent = null;
        lineNumber = null;
        sizePercent = null;
        alignment = PresentationMediaTranscriptCueAlignment.Center;
        writingMode = PresentationMediaTranscriptCueWritingMode.Horizontal;
        regionId = null;

        var parts = line.Split(["-->"], 2, StringSplitOptions.None);
        if (parts.Length != 2)
        {
            return false;
        }

        var startToken = parts[0].Trim();
        var endAndSettings = parts[1]
            .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var endToken = endAndSettings.FirstOrDefault();
        if (!TryParseCaptionTime(startToken, out start)
            || !TryParseCaptionTime(endToken, out end)
            || end < start)
        {
            return false;
        }

        foreach (var setting in endAndSettings.Skip(1))
        {
            var separator = setting.IndexOf(':');
            if (separator <= 0 || separator == setting.Length - 1)
                continue;

            var key = setting[..separator].ToLowerInvariant();
            var value = setting[(separator + 1)..];
            switch (key)
            {
                case "align":
                    alignment = value.ToLowerInvariant() switch
                    {
                        "start" => PresentationMediaTranscriptCueAlignment.Start,
                        "end" => PresentationMediaTranscriptCueAlignment.End,
                        "left" => PresentationMediaTranscriptCueAlignment.Left,
                        "right" => PresentationMediaTranscriptCueAlignment.Right,
                        _ => PresentationMediaTranscriptCueAlignment.Center
                    };
                    break;
                case "position":
                    positionPercent = ParseWebVttPercent(value);
                    break;
                case "line":
                    if (value.EndsWith('%')
                        && ParseWebVttPercent(value) is { } parsedPercent)
                    {
                        linePercent = parsedPercent;
                    }
                    else if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLine))
                    {
                        lineNumber = parsedLine;
                    }
                    break;
                case "size":
                    sizePercent = ParseWebVttPercent(value);
                    break;
                case "vertical":
                    writingMode = value.ToLowerInvariant() switch
                    {
                        "rl" => PresentationMediaTranscriptCueWritingMode.VerticalRightToLeft,
                        "lr" => PresentationMediaTranscriptCueWritingMode.VerticalLeftToRight,
                        _ => PresentationMediaTranscriptCueWritingMode.Horizontal
                    };
                    break;
                case "region":
                    regionId = value;
                    break;
            }
        }

        return true;
    }

    private static double? ParseWebVttPercent(string value)
    {
        if (!value.EndsWith('%')
            || !double.TryParse(value[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent)
            || percent is < 0 or > 100)
        {
            return null;
        }

        return percent;
    }

    private static bool TryParseCaptionTime(string? token, out TimeSpan value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var normalized = token.Trim().Replace(',', '.');
        var parts = normalized.Split(':');
        if (parts.Length is not (2 or 3))
        {
            return false;
        }

        var hour = 0;
        var minuteIndex = 0;
        if (parts.Length == 3)
        {
            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out hour))
            {
                return false;
            }

            minuteIndex = 1;
        }

        if (!int.TryParse(parts[minuteIndex], NumberStyles.None, CultureInfo.InvariantCulture, out var minute)
            || !double.TryParse(parts[minuteIndex + 1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var second))
        {
            return false;
        }

        value = TimeSpan.FromHours(hour) + TimeSpan.FromMinutes(minute) + TimeSpan.FromSeconds(second);
        return true;
    }

    private static string BuildCueText(IEnumerable<string> lines)
    {
        var text = string.Join(" ", lines.Select(StripCueMarkup).Where(line => line.Length > 0));
        return CollapseWhitespace(text);
    }

    private static IReadOnlyList<PresentationMediaTranscriptCueSpan> ParseWebVttSpans(
        IEnumerable<string> lines,
        IReadOnlyDictionary<string, WebVttCueStyle>? styles = null)
    {
        var markup = string.Join(" ", lines);
        var spans = new List<PresentationMediaTranscriptCueSpan>();
        var bold = 0;
        var italic = 0;
        var underline = 0;
        var voices = new List<string>();
        var languages = new List<string>();
        var classScopes = new List<IReadOnlyList<string>>();

        foreach (Match match in Regex.Matches(
                     markup,
                     "(?<tag><(?<close>/)?(?<name>b|i|u|c(?:\\.[^ >]+)*|v(?:\\.[^ >]+)*|lang(?:\\.[^ >]+)*)(?<args>[^>]*)>)|(?<text>[^<]+)",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            if (match.Groups["tag"].Success)
            {
                var rawName = match.Groups["name"].Value;
                var name = rawName.Split('.')[0].ToLowerInvariant();
                var dottedValue = rawName.Contains('.', StringComparison.Ordinal)
                    ? NormalizeTagValue(string.Join('.', rawName.Split('.').Skip(1)))
                    : null;
                var tagValue = NormalizeTagValue(match.Groups["args"].Value) ?? dottedValue;
                var delta = match.Groups["close"].Success ? -1 : 1;
                switch (name)
                {
                    case "b": bold = Math.Max(0, bold + delta); break;
                    case "i": italic = Math.Max(0, italic + delta); break;
                    case "u": underline = Math.Max(0, underline + delta); break;
                    case "v":
                        if (delta < 0)
                        {
                            PopScope(voices);
                        }
                        else if (tagValue is { } voice)
                        {
                            voices.Add(voice);
                        }
                        break;
                    case "lang":
                        if (delta < 0)
                        {
                            PopScope(languages);
                        }
                        else if (tagValue is { } language)
                        {
                            languages.Add(language);
                        }
                        break;
                    case "c":
                        if (delta < 0)
                        {
                            PopScope(classScopes);
                        }
                        else
                        {
                            var classes = rawName
                                .Split('.')
                                .Skip(1)
                                .Select(NormalizeTagValue)
                                .Where(value => value is not null)
                                .Cast<string>()
                                .ToArray();
                            classScopes.Add(classes);
                        }
                        break;
                }

                continue;
            }

            var text = WhitespacePattern.Replace(WebUtility.HtmlDecode(match.Value), " ");
            if (text.Length > 0)
            {
                var style = ResolveWebVttCueStyle(
                    classScopes.SelectMany(scope => scope),
                    styles);
                spans.Add(new PresentationMediaTranscriptCueSpan(
                    text,
                    bold > 0 || style.Bold,
                    italic > 0 || style.Italic,
                    underline > 0 || style.Underline)
                {
                    Voice = voices.LastOrDefault(),
                    Language = languages.LastOrDefault(),
                    Classes = classScopes.SelectMany(scope => scope).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    ForegroundColorHex = style.ForegroundColorHex,
                    BackgroundColorHex = style.BackgroundColorHex,
                    FontFamily = style.FontFamily,
                    FontSizePx = style.FontSizePx
                });
            }
        }

        if (spans.Count == 0)
        {
            return [];
        }

        spans[0] = spans[0] with { Text = spans[0].Text.TrimStart() };
        spans[^1] = spans[^1] with { Text = spans[^1].Text.TrimEnd() };
        return spans.Where(span => span.Text.Length > 0).ToArray();

        static void PopScope<T>(List<T> scopes)
        {
            if (scopes.Count > 0)
                scopes.RemoveAt(scopes.Count - 1);
        }

        static string? NormalizeTagValue(string value)
        {
            var normalized = value.Trim();
            return normalized.Length == 0 ? null : normalized;
        }
    }

    private static WebVttCueStyle ResolveWebVttCueStyle(
        IEnumerable<string> classes,
        IReadOnlyDictionary<string, WebVttCueStyle>? styles)
    {
        var result = new WebVttCueStyle(null, null, false, false, false, null, null);
        if (styles is null)
        {
            return result;
        }

        foreach (var className in classes)
        {
            if (!styles.TryGetValue(className, out var style))
            {
                continue;
            }

            result = result with
            {
                ForegroundColorHex = style.ForegroundColorHex ?? result.ForegroundColorHex,
                BackgroundColorHex = style.BackgroundColorHex ?? result.BackgroundColorHex,
                FontFamily = style.FontFamily ?? result.FontFamily,
                FontSizePx = style.FontSizePx ?? result.FontSizePx,
                Bold = result.Bold || style.Bold,
                Italic = result.Italic || style.Italic,
                Underline = result.Underline || style.Underline
            };
        }

        return result;
    }

    private static string StripCueMarkup(string line)
    {
        var withoutTags = TagPattern.Replace(line, string.Empty);
        var decoded = WebUtility.HtmlDecode(withoutTags);
        return CollapseWhitespace(decoded);
    }

    private static IEnumerable<List<string>> EnumerateBlocks(string text)
    {
        var normalized = DecodeLineEndings(text);
        var block = new List<string>();
        foreach (var rawLine in normalized.Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.Length == 0)
            {
                if (block.Count > 0)
                {
                    yield return block;
                    block = [];
                }

                continue;
            }

            block.Add(line);
        }

        if (block.Count > 0)
        {
            yield return block;
        }
    }

    private static string DecodeUtf8(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        return text.Length > 0 && text[0] == '\uFEFF'
            ? text[1..]
            : text;
    }

    private static string DecodeLineEndings(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

    private static bool ContainsIgnoreCase(string? value, string needle)
        => value?.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool HasExtension(string? source, string extension)
        => NormalizeText(source)?.EndsWith(extension, StringComparison.OrdinalIgnoreCase) == true;

    private static string InferTrackLabel(string? source, int trackIndex)
    {
        var normalized = NormalizeText(source);
        if (normalized is null)
        {
            return $"Track {trackIndex + 1}";
        }

        var slashIndex = normalized.LastIndexOfAny(['/', '\\']);
        return slashIndex >= 0 && slashIndex < normalized.Length - 1
            ? normalized[(slashIndex + 1)..]
            : normalized;
    }

    private static string DescribeShape(SlideShape shape)
        => string.IsNullOrWhiteSpace(shape.Name)
            ? $"{shape.Kind} {shape.Id}"
            : shape.Name;

    private static string? NormalizeText(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string CollapseWhitespace(string? value)
        => NormalizeText(value) is { } normalized
            ? WhitespacePattern.Replace(normalized, " ")
            : string.Empty;

    private static IEnumerable<SlideShape> EnumerateShapes(IEnumerable<SlideShape> shapes)
    {
        foreach (var shape in shapes)
        {
            yield return shape;
            foreach (var child in EnumerateShapes(shape.Children))
            {
                yield return child;
            }
        }
    }
}
