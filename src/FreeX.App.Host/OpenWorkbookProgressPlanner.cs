namespace FreeX.App.Host;

public static class OpenWorkbookProgressPlanner
{
    private const double RunningStageHoldbackPercent = 0.5;

    public static double CalculateStageProgress(
        double stageStartPercent,
        double stageEndPercent,
        TimeSpan elapsed,
        TimeSpan expectedDuration)
        => WorkbookProgressPresentationPlanner.CalculateRunningStagePercent(
            stageStartPercent,
            stageEndPercent,
            elapsed,
            expectedDuration,
            RunningStageHoldbackPercent);

    public static string FormatLoadingFileDetail(string phase, TimeSpan elapsed)
        => FormatLoadingFileDetail(
            WorkbookProgressPresentationPlanner.ParseOpenProgressStep(phase),
            elapsed);

    public static string FormatLoadingFileDetail(WorkbookOpenProgressStep step, TimeSpan elapsed) =>
        UiText.Get(WorkbookProgressPresentationPlanner.SelectOpenDetailResourceKey(step, elapsed));

    public static string ProgressTitle() =>
        UiText.Get(WorkbookProgressPresentationPlanner.OpenTitleResourceKey);
}
