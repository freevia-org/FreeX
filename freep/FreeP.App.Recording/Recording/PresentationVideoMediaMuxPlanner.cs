using System.Globalization;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Recording;

public sealed record PresentationVideoMediaMuxTrack(
    PresentationRecordingMediaArtifactKind Kind,
    string Path,
    TimeSpan StartTime,
    TimeSpan Duration);

public sealed record PresentationVideoMediaCaptionTrack(
    PresentationRecordingMediaArtifactKind Kind,
    string Path,
    TimeSpan StartTime,
    TimeSpan Duration);

public sealed record PresentationVideoMediaMuxPlan(
    IReadOnlyList<PresentationVideoMediaMuxTrack> NarrationTracks,
    IReadOnlyList<PresentationVideoMediaMuxTrack> CameraTracks,
    IReadOnlyList<PresentationVideoMediaCaptionTrack> CaptionTracks)
{
    public int MuxedNarrationTrackCount => NarrationTracks.Count;

    public int MuxedCameraTrackCount => CameraTracks.Count;

    public int MuxedCaptionTrackCount => CaptionTracks.Count;

    public bool HasVideoOverlay => CameraTracks.Count > 0;
}

/// <summary>
/// Materializes captured recording artifacts for the host encoder and builds the common
/// ffmpeg input/filter graph used by WPF and Avalonia. Camera clips are rendered as a
/// deterministic bottom-right picture-in-picture overlay; slide timing remains authoritative.
/// </summary>
public static class PresentationVideoMediaMuxPlanner
{
    public static PresentationVideoMediaMuxPlan Prepare(
        PresentationVideoFramePackage package,
        IReadOnlyList<PresentationRecordingMediaArtifact>? mediaArtifacts,
        string directory)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (mediaArtifacts is null || mediaArtifacts.Count == 0)
            return new([], [], []);

        var slideStartTimes = package.Frames
            .GroupBy(frame => frame.SlideIndex)
            .ToDictionary(group => group.Key, group => group.Min(frame => frame.StartTime));
        var narrationTracks = new List<PresentationVideoMediaMuxTrack>();
        var cameraTracks = new List<PresentationVideoMediaMuxTrack>();
        var captionTracks = new List<PresentationVideoMediaCaptionTrack>();

        foreach (var artifact in mediaArtifacts)
        {
            if (!artifact.HasPayload || !slideStartTimes.TryGetValue(artifact.SlideIndex, out var startTime))
                continue;

            if (artifact.Kind == PresentationRecordingMediaArtifactKind.NarrationAudio &&
                !package.Plan.ExportPlan.IncludeNarration)
            {
                continue;
            }

            var isCaption = artifact.Kind is
                PresentationRecordingMediaArtifactKind.NarrationCaption or
                PresentationRecordingMediaArtifactKind.CameraCaption;
            if (isCaption &&
                artifact.Kind == PresentationRecordingMediaArtifactKind.NarrationCaption &&
                !package.Plan.ExportPlan.IncludeNarration)
            {
                continue;
            }

            if (artifact.Kind is not PresentationRecordingMediaArtifactKind.NarrationAudio and
                not PresentationRecordingMediaArtifactKind.CameraVideo and
                not PresentationRecordingMediaArtifactKind.NarrationCaption and
                not PresentationRecordingMediaArtifactKind.CameraCaption)
            {
                continue;
            }

            var extension = Path.GetExtension(artifact.SuggestedFileName);
            if (string.IsNullOrWhiteSpace(extension) || extension.Length > 8)
                extension = artifact.Kind is PresentationRecordingMediaArtifactKind.CameraVideo
                    ? ".mp4"
                    : artifact.Kind is PresentationRecordingMediaArtifactKind.NarrationCaption or
                        PresentationRecordingMediaArtifactKind.CameraCaption
                        ? ".vtt"
                        : ".audio";

            if (isCaption)
            {
                var captionIndex = captionTracks.Count;
                var captionPath = Path.Combine(directory, $"caption-{captionIndex:D4}{extension}");
                File.WriteAllBytes(captionPath, artifact.PayloadBytes!);
                captionTracks.Add(new PresentationVideoMediaCaptionTrack(
                    artifact.Kind,
                    captionPath,
                    startTime,
                    TimeSpan.FromMilliseconds(Math.Max(0, artifact.DurationMs))));
                continue;
            }

            var trackIndex = artifact.Kind == PresentationRecordingMediaArtifactKind.CameraVideo
                ? cameraTracks.Count
                : narrationTracks.Count;
            var stem = artifact.Kind == PresentationRecordingMediaArtifactKind.CameraVideo
                ? "camera"
                : "narration";
            var path = Path.Combine(directory, $"{stem}-{trackIndex:D4}{extension}");
            File.WriteAllBytes(path, artifact.PayloadBytes!);

            var duration = TimeSpan.FromMilliseconds(Math.Max(0, artifact.DurationMs));
            var track = new PresentationVideoMediaMuxTrack(artifact.Kind, path, startTime, duration);
            if (artifact.Kind == PresentationRecordingMediaArtifactKind.CameraVideo)
                cameraTracks.Add(track);
            else
                narrationTracks.Add(track);
        }

        return new(narrationTracks, cameraTracks, captionTracks);
    }

    public static IReadOnlyList<string> BuildFfmpegArguments(
        string concatPath,
        string outputPath,
        string encoderName,
        PresentationVideoMediaMuxPlan mediaPlan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(concatPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(encoderName);
        ArgumentNullException.ThrowIfNull(mediaPlan);

        var arguments = new List<string>
        {
            "-hide_banner",
            "-loglevel", "error",
            "-y",
            "-f", "concat",
            "-safe", "0",
            "-i", concatPath,
        };

        foreach (var track in mediaPlan.NarrationTracks)
        {
            arguments.Add("-i");
            arguments.Add(track.Path);
        }

        foreach (var track in mediaPlan.CameraTracks)
        {
            arguments.Add("-i");
            arguments.Add(track.Path);
        }

        foreach (var track in mediaPlan.CaptionTracks)
        {
            arguments.Add("-itsoffset");
            arguments.Add(Seconds(track.StartTime));
            arguments.Add("-i");
            arguments.Add(track.Path);
        }

        var filters = new List<string>();
        var videoMap = "0:v:0";
        if (mediaPlan.CameraTracks.Count > 0)
        {
            var currentVideo = "[0:v:0]";
            for (var index = 0; index < mediaPlan.CameraTracks.Count; index++)
            {
                var track = mediaPlan.CameraTracks[index];
                var inputIndex = 1 + mediaPlan.NarrationTracks.Count + index;
                var cameraLabel = $"[camera{index}]";
                var outputLabel = index == mediaPlan.CameraTracks.Count - 1
                    ? "[vout]"
                    : $"[video{index}]";
                var startSeconds = track.StartTime.TotalSeconds.ToString("0.######", CultureInfo.InvariantCulture);
                var durationFilter = track.Duration > TimeSpan.Zero
                    ? $",trim=duration={track.Duration.TotalSeconds.ToString("0.######", CultureInfo.InvariantCulture)}"
                    : string.Empty;

                filters.Add(
                    $"[{inputIndex}:v]setpts=PTS-STARTPTS{durationFilter},setpts=PTS+{startSeconds}/TB,scale=trunc(iw*0.25/2)*2:-2{cameraLabel}");
                filters.Add(
                    $"{currentVideo}{cameraLabel}overlay=x=main_w-overlay_w-32:y=main_h-overlay_h-32:eof_action=pass:shortest=0{outputLabel}");
                currentVideo = outputLabel;
            }

            videoMap = "[vout]";
        }

        if (mediaPlan.NarrationTracks.Count == 0)
        {
            arguments.Add("-an");
        }
        else
        {
            var audioFilters = mediaPlan.NarrationTracks
                .Select((track, index) =>
                    $"[{index + 1}:a]adelay={StartDelayMilliseconds(track.StartTime)}:all=1[a{index}]")
                .ToList();
            var mixedInputs = string.Concat(mediaPlan.NarrationTracks.Select((_, index) => $"[a{index}]"));
            audioFilters.Add(mediaPlan.NarrationTracks.Count == 1
                ? "[a0]aresample=async=1[aout]"
                : $"{mixedInputs}amix=inputs={mediaPlan.NarrationTracks.Count}:duration=longest:dropout_transition=0,aresample=async=1[aout]");
            filters.AddRange(audioFilters);
        }

        if (filters.Count > 0)
        {
            arguments.Add("-filter_complex");
            arguments.Add(string.Join(';', filters));
            arguments.Add("-map");
            arguments.Add(videoMap);
            if (mediaPlan.NarrationTracks.Count > 0)
            {
                arguments.Add("-map");
                arguments.Add("[aout]");
                arguments.Add("-shortest");
                arguments.Add("-c:a");
                arguments.Add("aac");
                arguments.Add("-b:a");
                arguments.Add("192k");
            }
        }
        else
        {
            arguments.Add("-map");
            arguments.Add("0:v:0");
            if (mediaPlan.NarrationTracks.Count == 0)
                arguments.Add("-an");
        }

        var captionInputBase = 1 + mediaPlan.NarrationTracks.Count + mediaPlan.CameraTracks.Count;
        for (var index = 0; index < mediaPlan.CaptionTracks.Count; index++)
        {
            arguments.Add("-map");
            arguments.Add($"{captionInputBase + index}:0");
        }
        if (mediaPlan.CaptionTracks.Count > 0)
        {
            arguments.Add("-c:s");
            arguments.Add("mov_text");
        }

        arguments.Add("-c:v");
        arguments.Add(encoderName);
        arguments.Add("-pix_fmt");
        arguments.Add("yuv420p");
        arguments.Add("-movflags");
        arguments.Add("+faststart");
        arguments.Add(outputPath);
        return arguments;
    }

    private static long StartDelayMilliseconds(TimeSpan startTime) =>
        Math.Max(0, (long)Math.Round(startTime.TotalMilliseconds, MidpointRounding.AwayFromZero));

    private static string Seconds(TimeSpan value) =>
        Math.Max(0, value.TotalSeconds).ToString("0.######", CultureInfo.InvariantCulture);
}
