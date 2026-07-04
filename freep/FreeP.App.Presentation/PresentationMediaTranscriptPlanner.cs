using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
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

public sealed record PresentationMediaTranscriptCueDescriptor(
    TimeSpan StartTime,
    TimeSpan EndTime,
    string Text)
{
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
    IReadOnlyList<PresentationMediaTranscriptCueDescriptor>? Cues = null);

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

public static class PresentationMediaTranscriptPlanner
{
    public const string MissingMediaMessage = "Media object is required.";
    public const string MissingCaptionTrackMessage = "Caption track was not found.";
    public const string ExternalCaptionTrackMessage = "External caption tracks must remain link metadata; create a new internal track instead.";
    public const string MissingCaptionDescriptorMessage = "Caption authoring descriptor is required.";
    public const string MissingCaptionContentMessage = "Caption authoring requires typed cues or transcript text.";
    public const string AmbiguousCaptionContentMessage = "Caption authoring accepts either typed cues or transcript text, not both.";
    public const string EmptyCaptionContentMessage = "Caption authoring requires at least one valid cue.";
    public const string InvalidCaptionCueTimingMessage = "Caption cues must have non-negative, increasing, non-overlapping time ranges.";
    public const string InvalidCaptionSourceMessage = "Internal caption track source must be a relative .vtt package path or file name.";

    private enum CaptionTrackFormat
    {
        Unsupported,
        WebVtt,
        Srt
    }

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

        if (existingTrack.IsExternal)
        {
            return PresentationMediaCaptionTrackMutationResult.Failure(ExternalCaptionTrackMessage);
        }

        if (!TryBuildInternalCaptionTrack(trackIndex, descriptor, existingTrack, out var replacement, out var errorMessage))
        {
            return PresentationMediaCaptionTrackMutationResult.Failure(errorMessage);
        }

        media.CaptionTracks[trackIndex] = replacement;
        return PresentationMediaCaptionTrackMutationResult.Success(trackIndex, replacement);
    }

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

        if (track.IsExternal)
        {
            return PresentationMediaCaptionTrackMutationResult.Failure(ExternalCaptionTrackMessage);
        }

        media.CaptionTracks.RemoveAt(trackIndex);
        return PresentationMediaCaptionTrackMutationResult.Deleted(trackIndex, track);
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

        var source = NormalizeCaptionSource(
            descriptor.Source,
            existingTrack?.Source,
            trackIndex);
        if (source is null)
        {
            errorMessage = InvalidCaptionSourceMessage;
            return false;
        }

        track = new MediaCaptionTrackInfo
        {
            Source = source,
            Bytes = BuildWebVttBytes(cues),
            ContentType = "text/vtt",
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

            normalized.Add(new PresentationMediaTranscriptCueDescriptor(cue.StartTime, cue.EndTime, text));
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
                    : "captions.srt"
            },
            normalizedText);

        return format switch
        {
            CaptionTrackFormat.WebVtt => ParseWebVtt(normalizedText),
            CaptionTrackFormat.Srt => ParseSrt(normalizedText),
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

    private static byte[] BuildWebVttBytes(IReadOnlyList<PresentationMediaTranscriptCueDescriptor> cues)
    {
        var builder = new StringBuilder("WEBVTT\r\n\r\n");
        foreach (var cue in cues)
        {
            builder
                .Append(FormatWebVttTimestamp(cue.StartTime))
                .Append(" --> ")
                .Append(FormatWebVttTimestamp(cue.EndTime))
                .Append("\r\n")
                .Append(EscapeWebVttText(cue.Text))
                .Append("\r\n\r\n");
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

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
        int trackIndex)
    {
        if (NormalizeText(requestedSource) is { } requested)
        {
            return NormalizeInternalWebVttSource(requested);
        }

        if (NormalizeText(existingSource) is { } existing
            && NormalizeInternalWebVttSource(existing) is { } reusable)
        {
            return reusable;
        }

        var source = $"ppt/media/authored-captions{trackIndex + 1}.vtt";
        return NormalizeInternalWebVttSource(source);
    }

    private static string? NormalizeInternalWebVttSource(string source)
    {
        source = source.Replace('\\', '/');

        if (Uri.TryCreate(source, UriKind.Absolute, out _)
            || source.StartsWith("/", StringComparison.Ordinal)
            || source.Split('/').Any(part => part is ".." or ".")
            || !source.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return source;
    }

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
        var cues = format switch
        {
            CaptionTrackFormat.WebVtt => ParseWebVtt(text),
            CaptionTrackFormat.Srt => ParseSrt(text),
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
                cues);
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

    private static bool LooksLikeSrt(string text)
        => EnumerateBlocks(text)
            .SelectMany(block => block)
            .Take(4)
            .Any(line => line.Contains("-->", StringComparison.Ordinal)
                && line.Contains(',', StringComparison.Ordinal));

    private static IReadOnlyList<PresentationMediaTranscriptCueDescriptor> ParseWebVtt(string text)
    {
        var cues = new List<PresentationMediaTranscriptCueDescriptor>();

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

    private static bool TryParseTimingLine(string line, out TimeSpan start, out TimeSpan end)
    {
        start = default;
        end = default;

        var parts = line.Split(["-->"], 2, StringSplitOptions.None);
        if (parts.Length != 2)
        {
            return false;
        }

        var startToken = parts[0].Trim();
        var endToken = parts[1]
            .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return TryParseCaptionTime(startToken, out start)
            && TryParseCaptionTime(endToken, out end)
            && end >= start;
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
