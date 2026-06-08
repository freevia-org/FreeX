namespace FreeX.App.Services;

public enum WorkbookOpenPhase
{
    Reading,
    Inspecting,
    Parsing,
    Calculating,
}

public sealed record WorkbookOpenProgressUpdate(
    WorkbookOpenPhase Phase,
    TimeSpan Elapsed,
    double? Percent);
