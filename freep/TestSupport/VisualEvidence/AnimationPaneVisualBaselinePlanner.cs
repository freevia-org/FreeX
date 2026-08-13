using System.Globalization;

namespace FreeP.App.Compositor;

public enum AnimationPaneBaselineCaptureHost
{
    PowerPoint,
    Wpf,
    Avalonia,
}

public enum AnimationPaneBaselineCaptureKind
{
    PaneWorkflow,
    PlaybackCheckpoint,
}

public sealed record AnimationPaneVisualBaselineCaptureRequest(
    string CaptureId,
    AnimationPaneBaselineCaptureHost Host,
    AnimationPaneBaselineCaptureKind Kind,
    int SlideIndex,
    string ScenarioId,
    string SurfaceId,
    string Checkpoint,
    int ElapsedMs,
    bool RequiresPowerPointCom,
    string EvidenceSummary);

public sealed record AnimationPaneVisualBaselineReadinessPlan(
    string ScenarioId,
    int SlideIndex,
    int AnimationRowCount,
    int PlaybackCheckpointCount,
    IReadOnlyList<AnimationPaneVisualBaselineCaptureRequest> CaptureRequests,
    IReadOnlyList<string> EvidenceLines)
{
    public int PowerPointRequestCount => CaptureRequests.Count(request =>
        request.Host == AnimationPaneBaselineCaptureHost.PowerPoint);

    public int SharedHostRequestCount => CaptureRequests.Count(request =>
        request.Host is AnimationPaneBaselineCaptureHost.Wpf or AnimationPaneBaselineCaptureHost.Avalonia);

    public bool IsPowerPointAuthoritativeReady =>
        AnimationRowCount > 0
        && CaptureRequests.Any(request => request.Host == AnimationPaneBaselineCaptureHost.PowerPoint)
        && CaptureRequests.Any(request => request.Host == AnimationPaneBaselineCaptureHost.Wpf)
        && CaptureRequests.Any(request => request.Host == AnimationPaneBaselineCaptureHost.Avalonia);
}

public static class AnimationPaneVisualBaselinePlanner
{
    public static AnimationPaneVisualBaselineReadinessPlan Build(
        AnimationPaneTimelinePlan timelinePlan,
        IReadOnlyList<SlideShowAnimationStepVisualCheckpointPlan> playbackCheckpoints,
        int slideIndex,
        string scenarioId = "animation-pane")
    {
        ArgumentNullException.ThrowIfNull(timelinePlan);
        ArgumentNullException.ThrowIfNull(playbackCheckpoints);

        var safeScenarioId = NormalizeScenarioId(scenarioId);
        var safeSlideIndex = Math.Max(0, slideIndex);
        var requests = new List<AnimationPaneVisualBaselineCaptureRequest>();
        var paneSurfaceId = BuildSurfaceId(safeScenarioId, safeSlideIndex, "pane", "workflow");
        var paneSummary = timelinePlan.HasAnimations
            ? $"Animation pane slide {safeSlideIndex + 1}: {timelinePlan.Items.Count} row(s); selected {FormatSelected(timelinePlan.SelectedIndex)}"
            : $"Animation pane slide {safeSlideIndex + 1}: no animation rows";

        AddHostRequests(
            requests,
            safeScenarioId,
            safeSlideIndex,
            AnimationPaneBaselineCaptureKind.PaneWorkflow,
            paneSurfaceId,
            "pane",
            elapsedMs: 0,
            paneSummary);

        foreach (var checkpoint in playbackCheckpoints)
        {
            var checkpointToken = NormalizeScenarioId(checkpoint.Checkpoint);
            var surfaceId = BuildSurfaceId(
                safeScenarioId,
                safeSlideIndex,
                "playback",
                checkpointToken);
            var summary = $"{checkpoint.EvidenceSummary}; " + string.Join(" | ",
                checkpoint.Frames.Select(frame => frame.EvidenceSummary));

            AddHostRequests(
                requests,
                safeScenarioId,
                safeSlideIndex,
                AnimationPaneBaselineCaptureKind.PlaybackCheckpoint,
                surfaceId,
                checkpoint.Checkpoint,
                checkpoint.ElapsedMs,
                summary);
        }

        var evidenceLines = new List<string>
        {
            $"Scenario {safeScenarioId}: slide {safeSlideIndex + 1}; rows {timelinePlan.Items.Count}; playback checkpoints {playbackCheckpoints.Count}",
            $"Capture requests: {requests.Count}; PowerPoint {requests.Count(request => request.Host == AnimationPaneBaselineCaptureHost.PowerPoint)}; WPF {requests.Count(request => request.Host == AnimationPaneBaselineCaptureHost.Wpf)}; Avalonia {requests.Count(request => request.Host == AnimationPaneBaselineCaptureHost.Avalonia)}",
            "PowerPoint requests are readiness contracts and require desktop PowerPoint COM on the baseline machine",
        };

        return new AnimationPaneVisualBaselineReadinessPlan(
            safeScenarioId,
            safeSlideIndex,
            timelinePlan.Items.Count,
            playbackCheckpoints.Count,
            requests,
            evidenceLines);
    }

    private static void AddHostRequests(
        List<AnimationPaneVisualBaselineCaptureRequest> requests,
        string scenarioId,
        int slideIndex,
        AnimationPaneBaselineCaptureKind kind,
        string surfaceId,
        string checkpoint,
        int elapsedMs,
        string evidenceSummary)
    {
        foreach (var host in new[]
        {
            AnimationPaneBaselineCaptureHost.PowerPoint,
            AnimationPaneBaselineCaptureHost.Wpf,
            AnimationPaneBaselineCaptureHost.Avalonia,
        })
        {
            var hostToken = host.ToString().ToLowerInvariant();
            requests.Add(new AnimationPaneVisualBaselineCaptureRequest(
                $"{surfaceId}.{hostToken}",
                host,
                kind,
                slideIndex,
                scenarioId,
                surfaceId,
                checkpoint,
                Math.Max(0, elapsedMs),
                host == AnimationPaneBaselineCaptureHost.PowerPoint,
                evidenceSummary));
        }
    }

    private static string BuildSurfaceId(
        string scenarioId,
        int slideIndex,
        string surfaceKind,
        string checkpoint) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"freep.{scenarioId}.slide-{slideIndex + 1}.{surfaceKind}.{checkpoint}");

    private static string NormalizeScenarioId(string value)
    {
        var source = string.IsNullOrWhiteSpace(value)
            ? "animation-pane"
            : value.Trim().ToLowerInvariant();
        var normalized = new string(source
            .Select(character => character is >= 'a' and <= 'z' or >= '0' and <= '9'
                ? character
                : '-')
            .ToArray())
            .Trim('-');

        while (normalized.Contains("--", StringComparison.Ordinal))
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);

        return string.IsNullOrWhiteSpace(normalized) ? "animation-pane" : normalized;
    }

    private static string FormatSelected(int selectedIndex) =>
        selectedIndex >= 0
            ? (selectedIndex + 1).ToString(CultureInfo.InvariantCulture)
            : "none";
}
