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
    string ContentSha256 = "")
{
    public bool IsPersistable =>
        IsCaptured &&
        !string.IsNullOrWhiteSpace(PackagePath) &&
        ContentLengthBytes > 0 &&
        !string.IsNullOrWhiteSpace(ContentSha256);
}

public sealed record SlideShowRecordingReviewRow(
    int SlideIndex,
    string SlideTitle,
    int DurationMs,
    SlideShowRecordingMediaIntent MediaIntent,
    SlideShowRecordingReviewTimingStatus TimingStatus,
    bool TimingWillPersist,
    IReadOnlyList<SlideShowRecordingReviewMediaArtifact> MediaArtifacts,
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
    IReadOnlyList<SlideShowRecordingReviewRow> Rows,
    IReadOnlyList<SlideShowSlideTimingMutation> TimingMutations,
    IReadOnlyList<string> EvidenceLines);

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
                artifact.ContentSha256))
            .ToArray();

        return new SlideShowRecordingReviewRow(
            segment.SlideIndex,
            title,
            segment.DurationMs,
            segment.MediaIntent,
            timingStatus,
            timingStatus is SlideShowRecordingReviewTimingStatus.WillPersist,
            artifacts,
            BuildRowEvidenceLines(state.HostCapabilities.HostName, title, segment, timingStatus, artifacts));
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
        IReadOnlyList<SlideShowRecordingReviewMediaArtifact> artifacts)
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

        return lines;
    }

    private static string ResolveSlideTitle(Slide slide, int slideIndex) =>
        string.IsNullOrWhiteSpace(slide.Title)
            ? $"Slide {slideIndex + 1}"
            : slide.Title.Trim();
}
