namespace Free.Shared.AppServices;

public static class WorkbookProgressPresentationPlanner
{
    private const double ServiceStageHoldRatio = 0.92;
    private const double MinimumExpectedDurationMilliseconds = 1;
    private const double DefaultDetailRotationIntervalSeconds = 3.0;

    public const string OpenTitleResourceKey = "Progress_OpeningWorkbook";
    public const string SaveTitleResourceKey = "Progress_SavingWorkbook";

    public static IReadOnlyList<string> RequiredResourceKeys { get; } =
    [
        OpenTitleResourceKey,
        SaveTitleResourceKey,
        "Progress_LoadingFileReading",
        "Progress_LoadingFileReadingBytes",
        "Progress_LoadingFileCheckingPackage",
        "Progress_LoadingFileInspecting",
        "Progress_LoadingFileCheckingWorkbookParts",
        "Progress_LoadingFileDetectingFeatures",
        "Progress_LoadingFileParsing",
        "Progress_LoadingFilePhaseFormat",
        "Progress_LoadingFileReadingWorksheets",
        "Progress_LoadingFileBuildingWorkbook",
        "Progress_LoadingFileLoadingStyles",
        "Progress_LoadingFileCalculating",
        "Progress_LoadingFileEvaluatingFormulas",
        "Progress_LoadingFileRefreshingValues",
        "Progress_LoadingFilePreparingView",
        "Progress_LoadingFileLayingOutWorksheet",
        "Progress_LoadingFileRestoringSelection",
        "Progress_LoadingFilePreparing",
        "Progress_LoadingFileDone",
        "Progress_LoadingFileWorking",
        "Progress_SavingFileSerializing",
        "Progress_SavingFileBuildingWorkbookParts",
        "Progress_SavingFilePackagingSheets",
        "Progress_SavingFileWriting",
        "Progress_SavingFileWritingBytes",
        "Progress_SavingFileFlushingPackage",
        "Progress_SavingFilePhaseFormat",
        "Progress_SavingFilePreparing",
        "Progress_SavingFileDone",
        "Progress_SavingFileWorking",
        "Progress_ExportingFile",
        "Progress_ExportingFileRendering",
        "Progress_ExportingFileWriting"
    ];

    public static double? CalculateServiceStagePercent(
        double startPercent,
        double endPercent,
        TimeSpan elapsed,
        TimeSpan expectedDuration)
    {
        if (expectedDuration <= TimeSpan.Zero)
            return endPercent;

        var ratio = elapsed.TotalMilliseconds / expectedDuration.TotalMilliseconds;
        if (ratio >= 1)
            return null;

        ratio = Math.Clamp(ratio, 0, ServiceStageHoldRatio);
        return startPercent + ((endPercent - startPercent) * ratio);
    }

    public static double CalculateRunningStagePercent(
        double startPercent,
        double endPercent,
        TimeSpan elapsed,
        TimeSpan expectedDuration,
        double holdbackPercent)
    {
        if (endPercent <= startPercent)
            return startPercent;

        var duration = Math.Max(MinimumExpectedDurationMilliseconds, expectedDuration.TotalMilliseconds);
        var ratio = Math.Clamp(elapsed.TotalMilliseconds / duration, 0, 1);
        var maxWhileRunning = endPercent - Math.Max(0, holdbackPercent);
        return Math.Min(maxWhileRunning, startPercent + ((endPercent - startPercent) * ratio));
    }

    public static WorkbookOpenProgressStep ToOpenProgressStep(WorkbookOpenPhase phase) =>
        phase switch
        {
            WorkbookOpenPhase.Reading => WorkbookOpenProgressStep.Reading,
            WorkbookOpenPhase.Inspecting => WorkbookOpenProgressStep.Inspecting,
            WorkbookOpenPhase.Parsing => WorkbookOpenProgressStep.Parsing,
            WorkbookOpenPhase.Calculating => WorkbookOpenProgressStep.Calculating,
            _ => WorkbookOpenProgressStep.Working
        };

    public static WorkbookSaveProgressStep ToSaveProgressStep(WorkbookSavePhase phase) =>
        phase switch
        {
            WorkbookSavePhase.Preparing => WorkbookSaveProgressStep.Serializing,
            WorkbookSavePhase.Writing => WorkbookSaveProgressStep.Writing,
            WorkbookSavePhase.Completed => WorkbookSaveProgressStep.Done,
            _ => WorkbookSaveProgressStep.Working
        };

    public static WorkbookOpenProgressStep ParseOpenProgressStep(string phase) =>
        Normalize(phase) switch
        {
            "reading" => WorkbookOpenProgressStep.Reading,
            "inspecting" => WorkbookOpenProgressStep.Inspecting,
            "parsing" => WorkbookOpenProgressStep.Parsing,
            "calculating" => WorkbookOpenProgressStep.Calculating,
            "preparing view" => WorkbookOpenProgressStep.PreparingView,
            "preparing" => WorkbookOpenProgressStep.Preparing,
            "done" => WorkbookOpenProgressStep.Done,
            _ => WorkbookOpenProgressStep.Working
        };

    public static WorkbookSaveProgressStep ParseSaveProgressStep(string phase) =>
        Normalize(phase) switch
        {
            "serializing" => WorkbookSaveProgressStep.Serializing,
            "writing" => WorkbookSaveProgressStep.Writing,
            "preparing" => WorkbookSaveProgressStep.Preparing,
            "done" => WorkbookSaveProgressStep.Done,
            _ => WorkbookSaveProgressStep.Working
        };

    public static WorkbookProgressTextPlan BuildOpenTextPlan(WorkbookOpenProgressStep step, TimeSpan elapsed) =>
        new(OpenTitleResourceKey, SelectOpenDetailResourceKey(step, elapsed));

    public static WorkbookProgressTextPlan BuildSaveTextPlan(WorkbookSaveProgressStep step, TimeSpan elapsed) =>
        new(SaveTitleResourceKey, SelectSaveDetailResourceKey(step, elapsed));

    public static string SelectOpenDetailResourceKey(WorkbookOpenProgressStep step, TimeSpan elapsed) =>
        step switch
        {
            WorkbookOpenProgressStep.Reading => SelectDetailResourceKey(
                elapsed,
                "Progress_LoadingFileReading",
                "Progress_LoadingFileReadingBytes",
                "Progress_LoadingFileCheckingPackage"),
            WorkbookOpenProgressStep.Inspecting => SelectDetailResourceKey(
                elapsed,
                "Progress_LoadingFileInspecting",
                "Progress_LoadingFileCheckingWorkbookParts",
                "Progress_LoadingFileDetectingFeatures"),
            WorkbookOpenProgressStep.Parsing => SelectDetailResourceKey(
                elapsed,
                "Progress_LoadingFileParsing",
                "Progress_LoadingFileReadingWorksheets",
                "Progress_LoadingFileBuildingWorkbook",
                "Progress_LoadingFileLoadingStyles"),
            WorkbookOpenProgressStep.Calculating => SelectDetailResourceKey(
                elapsed,
                "Progress_LoadingFileCalculating",
                "Progress_LoadingFileEvaluatingFormulas",
                "Progress_LoadingFileRefreshingValues"),
            WorkbookOpenProgressStep.PreparingView => SelectDetailResourceKey(
                elapsed,
                "Progress_LoadingFilePreparingView",
                "Progress_LoadingFileLayingOutWorksheet",
                "Progress_LoadingFileRestoringSelection"),
            WorkbookOpenProgressStep.Preparing => "Progress_LoadingFilePreparing",
            WorkbookOpenProgressStep.Done => "Progress_LoadingFileDone",
            _ => "Progress_LoadingFileWorking"
        };

    public static string SelectSaveDetailResourceKey(WorkbookSaveProgressStep step, TimeSpan elapsed) =>
        step switch
        {
            WorkbookSaveProgressStep.Serializing => SelectDetailResourceKey(
                elapsed,
                "Progress_SavingFileSerializing",
                "Progress_SavingFileBuildingWorkbookParts",
                "Progress_SavingFilePackagingSheets"),
            WorkbookSaveProgressStep.Writing => SelectDetailResourceKey(
                elapsed,
                "Progress_SavingFileWriting",
                "Progress_SavingFileWritingBytes",
                "Progress_SavingFileFlushingPackage"),
            WorkbookSaveProgressStep.Preparing => "Progress_SavingFilePreparing",
            WorkbookSaveProgressStep.Done => "Progress_SavingFileDone",
            _ => "Progress_SavingFileWorking"
        };

    private static string SelectDetailResourceKey(
        TimeSpan elapsed,
        string firstKey,
        string secondKey,
        string thirdKey) =>
        CalculateDetailIndex(elapsed, 3) switch
        {
            0 => firstKey,
            1 => secondKey,
            _ => thirdKey
        };

    private static string SelectDetailResourceKey(
        TimeSpan elapsed,
        string firstKey,
        string secondKey,
        string thirdKey,
        string fourthKey) =>
        CalculateDetailIndex(elapsed, 4) switch
        {
            0 => firstKey,
            1 => secondKey,
            2 => thirdKey,
            _ => fourthKey
        };

    private static int CalculateDetailIndex(TimeSpan elapsed, int messageCount) =>
        (int)Math.Floor(Math.Max(0, elapsed.TotalSeconds) / DefaultDetailRotationIntervalSeconds) % messageCount;

    private static string Normalize(string phase) =>
        string.IsNullOrWhiteSpace(phase)
            ? string.Empty
            : phase.Trim().ToLowerInvariant();
}

public enum WorkbookOpenProgressStep
{
    Preparing,
    Reading,
    Inspecting,
    Parsing,
    Calculating,
    PreparingView,
    Done,
    Working
}

public enum WorkbookSaveProgressStep
{
    Preparing,
    Serializing,
    Writing,
    Done,
    Working
}

public sealed record WorkbookProgressTextPlan(string TitleResourceKey, string DetailResourceKey);
