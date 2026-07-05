using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum SlideShowRecordingReviewTimingStatus
{
    None,
    PreviewOnly,
    WillPersist,
    AlreadyApplied,
    InvalidSlide
}

public sealed record SlideShowRecordingReviewMediaArtifact(
    SlideShowRecordingMediaArtifactKind Kind,
    bool IsCaptured,
    bool IsDeferred,
    string SuggestedFileName,
    string ContentType,
    string StatusText,
    string PackagePath = "",
    long ContentLengthBytes = 0,
    string ContentSha256 = "",
    byte[]? PayloadBytes = null)
{
    public bool IsPersistable =>
        IsCaptured &&
        !string.IsNullOrWhiteSpace(PackagePath) &&
        ContentLengthBytes > 0 &&
        !string.IsNullOrWhiteSpace(ContentSha256);
}

public enum SlideShowRecordingCaptionArtifactKind
{
    NarrationCaption,
    CameraCaption
}

public sealed record SlideShowRecordingReviewCaptionArtifact(
    SlideShowRecordingCaptionArtifactKind Kind,
    SlideShowRecordingMediaArtifactKind SourceMediaKind,
    bool IsCaptured,
    string SuggestedFileName,
    string ContentType,
    string StatusText,
    string PackagePath,
    long ContentLengthBytes,
    string ContentSha256,
    string Language,
    string Label,
    byte[] PayloadBytes)
{
    public bool IsPersistable =>
        IsCaptured &&
        !string.IsNullOrWhiteSpace(PackagePath) &&
        ContentLengthBytes > 0 &&
        !string.IsNullOrWhiteSpace(ContentSha256) &&
        PayloadBytes.Length > 0;
}

public sealed record SlideShowRecordingReviewRow(
    int SlideIndex,
    string SlideTitle,
    int DurationMs,
    SlideShowRecordingMediaIntent MediaIntent,
    SlideShowRecordingReviewTimingStatus TimingStatus,
    bool TimingWillPersist,
    IReadOnlyList<SlideShowRecordingReviewMediaArtifact> MediaArtifacts,
    IReadOnlyList<SlideShowRecordingReviewCaptionArtifact> CaptionArtifacts,
    IReadOnlyList<string> EvidenceLines);

public sealed record SlideShowRecordingReviewPlan(
    string HostName,
    SlideShowTimingIntent TimingIntent,
    bool IsSessionActive,
    bool CanApplyRecordedTimings,
    int CompletedSegmentCount,
    int TotalRecordedDurationMs,
    int DeferredMediaArtifactCount,
    int CapturedMediaArtifactCount,
    int PersistableMediaArtifactCount,
    int PersistableCaptionArtifactCount,
    IReadOnlyList<SlideShowRecordingReviewRow> Rows,
    IReadOnlyList<SlideShowSlideTimingMutation> TimingMutations,
    IReadOnlyList<string> EvidenceLines);

public sealed record SlideShowRecordingReviewApplyResult(
    int MediaArtifactCount,
    int CaptionArtifactCount)
{
    public int TotalArtifactCount => MediaArtifactCount + CaptionArtifactCount;
}

public static class SlideShowRecordingReviewPlanner
{
    public static SlideShowRecordingReviewPlan BuildPlan(
        Presentation? presentation,
        SlideShowRecordingExecutionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var rows = state.Segments
            .Select(segment => BuildRow(presentation, state, segment))
            .ToArray();
        var mutations = rows
            .Where(row => row.TimingWillPersist)
            .Select(row => new SlideShowSlideTimingMutation(
                row.SlideIndex,
                row.DurationMs,
                ShouldPersist: true,
                state.RecordingPlan.TimingIntent))
            .ToArray();

        return new SlideShowRecordingReviewPlan(
            state.HostCapabilities.HostName,
            state.RecordingPlan.TimingIntent,
            state.IsSessionActive,
            CanApplyRecordedTimings: mutations.Length > 0,
            rows.Length,
            rows.Sum(row => row.DurationMs),
            rows.Sum(row => row.MediaArtifacts.Count(artifact => artifact.IsDeferred)),
            rows.Sum(row => row.MediaArtifacts.Count(artifact => artifact.IsCaptured)),
            rows.Sum(row => row.MediaArtifacts.Count(artifact => artifact.IsPersistable)),
            rows.Sum(row => row.CaptionArtifacts.Count(artifact => artifact.IsPersistable)),
            rows,
            mutations,
            BuildEvidenceLines(state, rows, mutations));
    }

    public static void ApplyRecordedTimings(
        Presentation presentation,
        SlideShowRecordingReviewPlan plan)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(plan);

        SlideShowTimingRecorderPlanner.ApplyTimings(presentation, plan.TimingMutations);
    }

    public static SlideShowRecordingReviewApplyResult ApplyPersistableArtifacts(
        Presentation presentation,
        SlideShowRecordingReviewPlan plan)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(plan);

        var mediaCount = ApplyPersistableMediaArtifacts(presentation, plan);
        var captionCount = ApplyPersistableCaptionArtifacts(presentation, plan);
        return new SlideShowRecordingReviewApplyResult(mediaCount, captionCount);
    }

    public static int ApplyPersistableMediaArtifacts(
        Presentation presentation,
        SlideShowRecordingReviewPlan plan)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(plan);

        var artifacts = plan.Rows
            .SelectMany(row => row.MediaArtifacts
                .Where(artifact => artifact.IsPersistable)
                .Select(artifact => new PresentationRecordingMediaArtifact(
                    MapArtifactKind(artifact.Kind),
                    row.SlideIndex,
                    artifact.SuggestedFileName,
                    artifact.ContentType,
                    artifact.PackagePath,
                    artifact.ContentLengthBytes,
                    artifact.ContentSha256,
                    row.DurationMs,
                    plan.HostName,
                    artifact.StatusText,
                    artifact.PayloadBytes)))
            .ToArray();

        if (artifacts.Length == 0)
        {
            return 0;
        }

        var replacementKeys = artifacts
            .Select(ArtifactKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        presentation.RecordingMediaArtifacts.RemoveAll(existing =>
            replacementKeys.Contains(ArtifactKey(existing)));
        presentation.RecordingMediaArtifacts.AddRange(artifacts);
        return artifacts.Length;
    }

    public static int ApplyPersistableCaptionArtifacts(
        Presentation presentation,
        SlideShowRecordingReviewPlan plan)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(plan);

        var artifacts = plan.Rows
            .SelectMany(row => row.CaptionArtifacts
                .Where(artifact => artifact.IsPersistable)
                .Select(artifact => new PresentationRecordingMediaArtifact(
                    MapCaptionArtifactKind(artifact.Kind),
                    row.SlideIndex,
                    artifact.SuggestedFileName,
                    artifact.ContentType,
                    artifact.PackagePath,
                    artifact.ContentLengthBytes,
                    artifact.ContentSha256,
                    row.DurationMs,
                    plan.HostName,
                    artifact.StatusText,
                    artifact.PayloadBytes)))
            .ToArray();

        if (artifacts.Length == 0)
        {
            return 0;
        }

        var replacementKeys = artifacts
            .Select(ArtifactKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        presentation.RecordingMediaArtifacts.RemoveAll(existing =>
            replacementKeys.Contains(ArtifactKey(existing)));
        presentation.RecordingMediaArtifacts.AddRange(artifacts);
        return artifacts.Length;
    }

    private static SlideShowRecordingReviewRow BuildRow(
        Presentation? presentation,
        SlideShowRecordingExecutionState state,
        SlideShowRecordingSlideSegment segment)
    {
        var slideExists = presentation is not null &&
            segment.SlideIndex >= 0 &&
            segment.SlideIndex < presentation.Slides.Count;
        var title = slideExists
            ? ResolveSlideTitle(presentation!.Slides[segment.SlideIndex], segment.SlideIndex)
            : $"Slide {segment.SlideIndex + 1}";
        var timingStatus = ResolveTimingStatus(presentation, state, segment, slideExists);
        var artifacts = segment.MediaArtifacts
            .Select(artifact => new SlideShowRecordingReviewMediaArtifact(
                artifact.Kind,
                artifact.IsCaptured,
                artifact.IsDeferred,
                artifact.SuggestedFileName,
                artifact.ContentType,
                artifact.StatusText,
                artifact.PackagePath,
                artifact.ContentLengthBytes,
                artifact.ContentSha256,
                artifact.PayloadBytes))
            .ToArray();
        var captions = BuildCaptionArtifacts(state.HostCapabilities.HostName, title, segment, artifacts);

        return new SlideShowRecordingReviewRow(
            segment.SlideIndex,
            title,
            segment.DurationMs,
            segment.MediaIntent,
            timingStatus,
            timingStatus is SlideShowRecordingReviewTimingStatus.WillPersist,
            artifacts,
            captions,
            BuildRowEvidenceLines(state.HostCapabilities.HostName, title, segment, timingStatus, artifacts, captions));
    }

    private static SlideShowRecordingReviewTimingStatus ResolveTimingStatus(
        Presentation? presentation,
        SlideShowRecordingExecutionState state,
        SlideShowRecordingSlideSegment segment,
        bool slideExists)
    {
        if (!state.RecordingPlan.ShouldTrackPerSlideTimings)
        {
            return SlideShowRecordingReviewTimingStatus.None;
        }

        if (!slideExists)
        {
            return SlideShowRecordingReviewTimingStatus.InvalidSlide;
        }

        if (!state.RecordingPlan.ShouldPersistTimings)
        {
            return SlideShowRecordingReviewTimingStatus.PreviewOnly;
        }

        var existingAdvanceMs = presentation!.Slides[segment.SlideIndex].Transition?.AdvanceAfterMs;
        return existingAdvanceMs == segment.DurationMs
            ? SlideShowRecordingReviewTimingStatus.AlreadyApplied
            : SlideShowRecordingReviewTimingStatus.WillPersist;
    }

    private static IReadOnlyList<string> BuildRowEvidenceLines(
        string hostName,
        string slideTitle,
        SlideShowRecordingSlideSegment segment,
        SlideShowRecordingReviewTimingStatus timingStatus,
        IReadOnlyList<SlideShowRecordingReviewMediaArtifact> artifacts,
        IReadOnlyList<SlideShowRecordingReviewCaptionArtifact> captions)
    {
        var lines = new List<string>
        {
            $"{hostName}: {slideTitle} recorded for {segment.DurationMs} ms"
        };

        lines.Add(timingStatus switch
        {
            SlideShowRecordingReviewTimingStatus.WillPersist =>
                $"{hostName}: {slideTitle} timing will persist as advance-after",
            SlideShowRecordingReviewTimingStatus.AlreadyApplied =>
                $"{hostName}: {slideTitle} timing is already applied",
            SlideShowRecordingReviewTimingStatus.PreviewOnly =>
                $"{hostName}: {slideTitle} timing is preview-only",
            SlideShowRecordingReviewTimingStatus.InvalidSlide =>
                $"{hostName}: {slideTitle} timing cannot apply because the slide is missing",
            _ => $"{hostName}: {slideTitle} has no timing mutation"
        });

        if (artifacts.Count > 0)
        {
            var captured = artifacts.Count(artifact => artifact.IsCaptured);
            var deferred = artifacts.Count(artifact => artifact.IsDeferred);
            lines.Add($"{hostName}: {slideTitle} media artifacts captured {captured}, deferred {deferred}");

            foreach (var artifact in artifacts.Where(artifact => artifact.IsPersistable))
            {
                var shortHash = artifact.ContentSha256.Length > 12
                    ? artifact.ContentSha256[..12]
                    : artifact.ContentSha256;
                lines.Add(
                    $"{hostName}: {slideTitle} {artifact.Kind} ready for PPTX media persistence at {artifact.PackagePath} ({artifact.ContentLengthBytes} bytes; sha256 {shortHash})");
            }
        }

        if (captions.Count > 0)
        {
            foreach (var caption in captions.Where(caption => caption.IsPersistable))
            {
                var shortHash = caption.ContentSha256.Length > 12
                    ? caption.ContentSha256[..12]
                    : caption.ContentSha256;
                lines.Add(
                    $"{hostName}: {slideTitle} {caption.Kind} ready for PPTX caption persistence at {caption.PackagePath} ({caption.ContentLengthBytes} bytes; sha256 {shortHash})");
            }
        }

        return lines;
    }

    private static IReadOnlyList<string> BuildEvidenceLines(
        SlideShowRecordingExecutionState state,
        IReadOnlyList<SlideShowRecordingReviewRow> rows,
        IReadOnlyList<SlideShowSlideTimingMutation> mutations)
    {
        var lines = new List<string>
        {
            $"{state.HostCapabilities.HostName}: {rows.Count} completed recording review row(s)",
            $"{state.HostCapabilities.HostName}: {rows.Sum(row => row.DurationMs)} ms total recorded"
        };

        if (mutations.Count > 0)
        {
            lines.Add($"{state.HostCapabilities.HostName}: {mutations.Count} recorded timing mutation(s) ready to apply");
        }

        var deferredArtifacts = rows.Sum(row => row.MediaArtifacts.Count(artifact => artifact.IsDeferred));
        if (deferredArtifacts > 0)
        {
            lines.Add($"{state.HostCapabilities.HostName}: {deferredArtifacts} recording media artifact(s) deferred");
        }

        var persistableArtifacts = rows.Sum(row => row.MediaArtifacts.Count(artifact => artifact.IsPersistable));
        if (persistableArtifacts > 0)
        {
            lines.Add($"{state.HostCapabilities.HostName}: {persistableArtifacts} recording media artifact(s) ready for PPTX media persistence");
        }

        var persistableCaptionArtifacts = rows.Sum(row => row.CaptionArtifacts.Count(artifact => artifact.IsPersistable));
        if (persistableCaptionArtifacts > 0)
        {
            lines.Add($"{state.HostCapabilities.HostName}: {persistableCaptionArtifacts} recording caption artifact(s) ready for PPTX caption persistence");
        }

        return lines;
    }

    private static IReadOnlyList<SlideShowRecordingReviewCaptionArtifact> BuildCaptionArtifacts(
        string hostName,
        string slideTitle,
        SlideShowRecordingSlideSegment segment,
        IReadOnlyList<SlideShowRecordingReviewMediaArtifact> mediaArtifacts)
    {
        var captions = new List<SlideShowRecordingReviewCaptionArtifact>();
        foreach (var artifact in mediaArtifacts.Where(artifact => artifact.IsPersistable))
        {
            captions.Add(BuildCaptionArtifact(hostName, slideTitle, segment, artifact));
        }

        return captions;
    }

    private static SlideShowRecordingReviewCaptionArtifact BuildCaptionArtifact(
        string hostName,
        string slideTitle,
        SlideShowRecordingSlideSegment segment,
        SlideShowRecordingReviewMediaArtifact sourceArtifact)
    {
        var isNarration = sourceArtifact.Kind == SlideShowRecordingMediaArtifactKind.NarrationAudio;
        var fileStem = isNarration ? "narration-captions" : "camera-captions";
        var suggestedFileName = $"slide-{segment.SlideIndex + 1:000}-{fileStem}.vtt";
        var packagePath = $"ppt/media/recording-captions/{suggestedFileName}";
        var label = isNarration ? "Narration captions" : "Camera subtitles";
        var cueText = isNarration
            ? $"{slideTitle}: narration captured by {hostName} for {segment.DurationMs} ms."
            : $"{slideTitle}: camera video captured by {hostName} for {segment.DurationMs} ms.";

        var media = new MediaInfo();
        var result = PresentationMediaTranscriptPlanner.CreateInternalCaptionTrack(
            media,
            new PresentationMediaCaptionTrackAuthoringDescriptor(
                label,
                "en-US",
                packagePath,
                TranscriptText: null,
                Cues:
                [
                    new PresentationMediaTranscriptCueDescriptor(
                        TimeSpan.Zero,
                        TimeSpan.FromMilliseconds(Math.Max(1, segment.DurationMs)),
                        cueText)
                ]));

        var track = result.Track ?? throw new InvalidOperationException(result.ErrorMessage);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(track.Bytes)).ToLowerInvariant();
        var kind = isNarration
            ? SlideShowRecordingCaptionArtifactKind.NarrationCaption
            : SlideShowRecordingCaptionArtifactKind.CameraCaption;

        return new SlideShowRecordingReviewCaptionArtifact(
            kind,
            sourceArtifact.Kind,
            IsCaptured: true,
            suggestedFileName,
            "text/vtt",
            $"{hostName}: {label} authored for {sourceArtifact.SuggestedFileName}",
            track.Source,
            track.Bytes.Length,
            hash,
            track.Language,
            track.Label,
            track.Bytes);
    }

    private static string ResolveSlideTitle(Slide slide, int slideIndex) =>
        string.IsNullOrWhiteSpace(slide.Title)
            ? $"Slide {slideIndex + 1}"
            : slide.Title.Trim();

    private static PresentationRecordingMediaArtifactKind MapArtifactKind(
        SlideShowRecordingMediaArtifactKind kind) =>
        kind == SlideShowRecordingMediaArtifactKind.NarrationAudio
            ? PresentationRecordingMediaArtifactKind.NarrationAudio
            : PresentationRecordingMediaArtifactKind.CameraVideo;

    private static PresentationRecordingMediaArtifactKind MapCaptionArtifactKind(
        SlideShowRecordingCaptionArtifactKind kind) =>
        kind == SlideShowRecordingCaptionArtifactKind.NarrationCaption
            ? PresentationRecordingMediaArtifactKind.NarrationCaption
            : PresentationRecordingMediaArtifactKind.CameraCaption;

    private static string ArtifactKey(PresentationRecordingMediaArtifact artifact) =>
        $"{artifact.SlideIndex}|{artifact.Kind}|{artifact.PackagePath}";
}
