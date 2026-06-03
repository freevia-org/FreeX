namespace FreeX.App.Host;

public static class OpenWorkbookProgressPlanner
{
    private const double RunningStageHoldbackPercent = 0.5;
    private const double MinimumExpectedDurationMilliseconds = 1;
    private const double DetailRotationIntervalSeconds = 3.0;

    public static double CalculateStageProgress(
        double stageStartPercent,
        double stageEndPercent,
        TimeSpan elapsed,
        TimeSpan expectedDuration)
    {
        if (stageEndPercent <= stageStartPercent)
            return stageStartPercent;

        var duration = Math.Max(MinimumExpectedDurationMilliseconds, expectedDuration.TotalMilliseconds);
        var ratio = Math.Clamp(elapsed.TotalMilliseconds / duration, 0, 1);
        var maxWhileRunning = stageEndPercent - RunningStageHoldbackPercent;
        return Math.Min(maxWhileRunning, stageStartPercent + (stageEndPercent - stageStartPercent) * ratio);
    }

    public static string FormatLoadingFileDetail(string phase, TimeSpan elapsed)
    {
        var normalizedPhase = NormalizePhase(phase);
        if (string.Equals(normalizedPhase, "reading", StringComparison.OrdinalIgnoreCase))
        {
            return SelectPhaseMessage(
                elapsed,
                "Progress_LoadingFileReading",
                "Progress_LoadingFileReadingBytes",
                "Progress_LoadingFileCheckingPackage");
        }

        if (string.Equals(normalizedPhase, "inspecting", StringComparison.OrdinalIgnoreCase))
        {
            return SelectPhaseMessage(
                elapsed,
                "Progress_LoadingFileInspecting",
                "Progress_LoadingFileCheckingWorkbookParts",
                "Progress_LoadingFileDetectingFeatures");
        }

        if (string.Equals(normalizedPhase, "parsing", StringComparison.OrdinalIgnoreCase))
        {
            return SelectPhaseMessage(
                elapsed,
                "Progress_LoadingFileParsing",
                "Progress_LoadingFileReadingWorksheets",
                "Progress_LoadingFileBuildingWorkbook",
                "Progress_LoadingFileLoadingStyles");
        }

        if (string.Equals(normalizedPhase, "calculating", StringComparison.OrdinalIgnoreCase))
        {
            return SelectPhaseMessage(
                elapsed,
                "Progress_LoadingFileCalculating",
                "Progress_LoadingFileEvaluatingFormulas",
                "Progress_LoadingFileRefreshingValues");
        }

        if (string.Equals(normalizedPhase, "preparing view", StringComparison.OrdinalIgnoreCase))
        {
            return SelectPhaseMessage(
                elapsed,
                "Progress_LoadingFilePreparingView",
                "Progress_LoadingFileLayingOutWorksheet",
                "Progress_LoadingFileRestoringSelection");
        }

        if (string.Equals(normalizedPhase, "preparing", StringComparison.OrdinalIgnoreCase))
            return UiText.Get("Progress_LoadingFilePreparing");

        return string.Equals(normalizedPhase, "done", StringComparison.OrdinalIgnoreCase)
            ? UiText.Get("Progress_LoadingFileDone")
            : UiText.Get("Progress_LoadingFileWorking");
    }

    public static string ProgressTitle() => UiText.Get("Progress_OpeningWorkbook");

    private static string NormalizePhase(string phase) =>
        string.IsNullOrWhiteSpace(phase)
            ? string.Empty
            : phase.Trim();

    private static string SelectPhaseMessage(TimeSpan elapsed, string firstKey, string secondKey, string thirdKey) =>
        UiText.Get(CalculateDetailIndex(elapsed, 3) switch
        {
            0 => firstKey,
            1 => secondKey,
            _ => thirdKey
        });

    private static string SelectPhaseMessage(
        TimeSpan elapsed,
        string firstKey,
        string secondKey,
        string thirdKey,
        string fourthKey) =>
        UiText.Get(CalculateDetailIndex(elapsed, 4) switch
        {
            0 => firstKey,
            1 => secondKey,
            2 => thirdKey,
            _ => fourthKey
        });

    private static int CalculateDetailIndex(TimeSpan elapsed, int messageCount) =>
        (int)Math.Floor(Math.Max(0, elapsed.TotalSeconds) / DetailRotationIntervalSeconds) % messageCount;
}
