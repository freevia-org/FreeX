namespace FreeX.App.Host;

internal readonly record struct RibbonAdaptiveDiagnosticsSnapshot(
    int MeasurementInvalidationCount,
    int GroupMeasurementCount,
    int CompactSnapshotCaptureCount,
    int ResizeThresholdRebuildCount,
    int MeasuredOverflowMeasurementCount,
    int CorrectedStateCacheHitCount,
    int AppliedStateSkipCount,
    string? MeasurementCacheKey,
    string? ResizeThresholdCacheKey,
    string? CompactSnapshotCacheKey);
