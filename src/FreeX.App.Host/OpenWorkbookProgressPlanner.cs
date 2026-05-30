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
        var messages = GetPhaseMessages(normalizedPhase);
        return messages[CalculateDetailIndex(elapsed, messages.Length)];
    }

    public static string ProgressTitle() => UiText.Get("Progress_OpeningWorkbook");

    private static string NormalizePhase(string phase) =>
        string.IsNullOrWhiteSpace(phase)
            ? string.Empty
            : phase.Trim();

    private static string[] GetPhaseMessages(string normalizedPhase) =>
        normalizedPhase.ToLowerInvariant() switch
        {
            "reading" =>
            [
                UiText.Get("Progress_LoadingFileReading"),
                UiText.Get("Progress_LoadingFileReadingBytes"),
                UiText.Get("Progress_LoadingFileCheckingPackage")
            ],
            "inspecting" =>
            [
                UiText.Get("Progress_LoadingFileInspecting"),
                UiText.Get("Progress_LoadingFileCheckingWorkbookParts"),
                UiText.Get("Progress_LoadingFileDetectingFeatures")
            ],
            "parsing" =>
            [
                UiText.Get("Progress_LoadingFileParsing"),
                UiText.Get("Progress_LoadingFileReadingWorksheets"),
                UiText.Get("Progress_LoadingFileBuildingWorkbook"),
                UiText.Get("Progress_LoadingFileLoadingStyles")
            ],
            "calculating" =>
            [
                UiText.Get("Progress_LoadingFileCalculating"),
                UiText.Get("Progress_LoadingFileEvaluatingFormulas"),
                UiText.Get("Progress_LoadingFileRefreshingValues")
            ],
            "preparing view" =>
            [
                UiText.Get("Progress_LoadingFilePreparingView"),
                UiText.Get("Progress_LoadingFileLayingOutWorksheet"),
                UiText.Get("Progress_LoadingFileRestoringSelection")
            ],
            "preparing" => [UiText.Get("Progress_LoadingFilePreparing")],
            "done" => [UiText.Get("Progress_LoadingFileDone")],
            _ => [UiText.Get("Progress_LoadingFileWorking")]
        };

    private static int CalculateDetailIndex(TimeSpan elapsed, int messageCount) =>
        (int)Math.Floor(Math.Max(0, elapsed.TotalSeconds) / DetailRotationIntervalSeconds) % messageCount;
}
