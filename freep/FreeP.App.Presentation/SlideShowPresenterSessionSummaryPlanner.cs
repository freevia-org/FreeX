using System.Collections.Generic;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record SlideShowPresenterRecordingSessionSummary(
    bool IsSessionActive,
    int? CurrentSlideIndex,
    int CompletedSegmentCount,
    int TotalRecordedDurationMs,
    int NarrationRequestedSlideCount,
    int NarrationCapturedSlideCount,
    int NarrationDeferredSlideCount,
    int CameraRequestedSlideCount,
    int CameraCapturedSlideCount,
    int CameraDeferredSlideCount,
    int CapturedMediaArtifactCount,
    int DeferredMediaArtifactCount);

public sealed record SlideShowPresenterInkSessionSummary(
    SlideShowInkRetentionDecision RetentionDecision,
    int CommittedStrokeCount,
    int ActiveStrokePointCount,
    int PersistableStrokeCount,
    int GeneratedInkSlideCount,
    int GeneratedInkStrokeCount,
    bool HasTransientLaserOverlay,
    bool WillPersistInkOnExit);

public sealed record SlideShowPresenterSessionSummary(
    string HostName,
    SlideShowPresenterRecordingSessionSummary Recording,
    SlideShowPresenterInkSessionSummary Ink,
    IReadOnlyList<string> EvidenceLines);

public static class SlideShowPresenterSessionSummaryPlanner
{
    public static SlideShowPresenterSessionSummary BuildSummary(
        SlideShowRecordingExecutionState recordingState,
        SlideShowInkExecutionState inkState,
        Presentation? presentation = null,
        Func<int, int>? mapRouteSlideToPresentationSlide = null)
    {
        ArgumentNullException.ThrowIfNull(recordingState);
        ArgumentNullException.ThrowIfNull(inkState);

        var recording = BuildRecordingSummary(recordingState);
        var ink = BuildInkSummary(inkState, presentation, mapRouteSlideToPresentationSlide);

        return new SlideShowPresenterSessionSummary(
            recordingState.HostCapabilities.HostName,
            recording,
            ink,
            BuildEvidenceLines(recordingState.HostCapabilities.HostName, recording, ink));
    }

    private static SlideShowPresenterRecordingSessionSummary BuildRecordingSummary(
        SlideShowRecordingExecutionState state)
    {
        var segments = state.Segments;
        return new SlideShowPresenterRecordingSessionSummary(
            state.IsSessionActive,
            state.CurrentSlideIndex,
            segments.Count,
            segments.Sum(segment => segment.DurationMs),
            segments.Count(segment => segment.NarrationRequested),
            segments.Count(segment => segment.NarrationCaptured),
            segments.Count(segment => segment.NarrationRequested && !segment.NarrationCaptured),
            segments.Count(segment => segment.CameraRequested),
            segments.Count(segment => segment.CameraCaptured),
            segments.Count(segment => segment.CameraRequested && !segment.CameraCaptured),
            segments.Sum(segment => segment.MediaArtifacts.Count(artifact => artifact.IsCaptured)),
            segments.Sum(segment => segment.MediaArtifacts.Count(artifact => artifact.IsDeferred)));
    }

    private static SlideShowPresenterInkSessionSummary BuildInkSummary(
        SlideShowInkExecutionState state,
        Presentation? presentation,
        Func<int, int>? mapRouteSlideToPresentationSlide)
    {
        var retainedState = SlideShowInkExecutionPlanner.ApplyRetentionOnExit(state).State with
        {
            LaserOverlayPoint = null,
        };
        var plan = presentation is null
            ? null
            : SlideShowInkPersistencePlanner.BuildPlan(
                presentation,
                retainedState,
                mapRouteSlideToPresentationSlide);
        var persistableStrokeCount = retainedState.CommittedStrokes.Count(IsPersistableStroke);

        return new SlideShowPresenterInkSessionSummary(
            state.InkRetentionDecision,
            state.CommittedStrokes.Count,
            state.ActiveStroke?.Points.Count ?? 0,
            persistableStrokeCount,
            plan?.Slides.Count ?? 0,
            plan?.Slides.Sum(slide => slide.Strokes.Count) ?? persistableStrokeCount,
            state.LaserOverlayPoint is not null,
            state.InkRetentionDecision == SlideShowInkRetentionDecision.KeepInk &&
                persistableStrokeCount > 0);
    }

    private static IReadOnlyList<string> BuildEvidenceLines(
        string hostName,
        SlideShowPresenterRecordingSessionSummary recording,
        SlideShowPresenterInkSessionSummary ink)
    {
        var lines = new List<string>
        {
            $"{hostName}: {recording.CompletedSegmentCount} recording segment(s), {recording.TotalRecordedDurationMs} ms recorded"
        };

        if (recording.DeferredMediaArtifactCount > 0)
        {
            lines.Add($"{hostName}: {recording.DeferredMediaArtifactCount} recording media artifact(s) deferred");
        }

        if (recording.CapturedMediaArtifactCount > 0)
        {
            lines.Add($"{hostName}: {recording.CapturedMediaArtifactCount} recording media artifact(s) captured");
        }

        lines.Add(
            $"Presenter ink: {ink.PersistableStrokeCount} persistable stroke(s), {ink.GeneratedInkSlideCount} generated slide part(s)");

        if (ink.HasTransientLaserOverlay)
        {
            lines.Add("Presenter ink: transient laser overlay is not retained");
        }

        return lines;
    }

    private static bool IsPersistableStroke(SlideShowInkStroke stroke) =>
        stroke.Points.Count > 0 &&
        stroke.PointerMode is SlideShowPresenterPointerMode.Pen or SlideShowPresenterPointerMode.Highlighter;
}
