namespace FreeX.App.Host;

internal readonly record struct RibbonFallbackDiagnosticsSnapshot(
    int RequestCount,
    int PostedCount,
    int ExecutedCount,
    int ForcedNormalizeCount,
    int ForcedCompactCount,
    int SkippedCompactLayoutCount,
    int FirstFrameLayoutUpdateCount,
    string LastRequestedWork,
    string LastMergedWork,
    string LastExecutedWork,
    bool IsPending,
    bool ResizeCompactionPendingOnExit);
