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

public static class PresentationMediaTranscriptPlanner
{
    private enum CaptionTrackFormat
    {
        Unsupported,
        WebVtt,
        Srt
    }

    private static readonly Regex TagPattern = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.Compiled);

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
