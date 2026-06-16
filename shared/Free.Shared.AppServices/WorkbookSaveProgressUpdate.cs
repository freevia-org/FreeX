namespace Free.Shared.AppServices;

public enum WorkbookSavePhase
{
    Preparing,
    Writing,
    Completed
}

public sealed record WorkbookSaveProgressUpdate(
    WorkbookSavePhase Phase,
    TimeSpan Elapsed,
    double? Percent);
