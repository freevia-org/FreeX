namespace FreeX.App.Host;

internal readonly record struct RibbonAdaptiveDiagnosticsSnapshot(
    int MeasurementInvalidationCount,
    int GroupMeasurementCount,
    int CompactSnapshotCaptureCount,
    int ResizeThresholdRebuildCount,
    int LayoutPlanComputeCount,
    int LayoutPlanCacheHitCount,
    int MeasuredOverflowMeasurementCount,
    int CorrectedStateCacheHitCount,
    int AppliedStateSkipCount,
    int StateApplyCount,
    int StateChangedGroupCount,
    int CollapsedFootprintApplyCount,
    string? MeasurementCacheKey,
    string? ResizeThresholdCacheKey,
    string? CompactSnapshotCacheKey);
