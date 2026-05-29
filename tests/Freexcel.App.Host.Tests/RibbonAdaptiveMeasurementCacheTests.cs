using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class RibbonAdaptiveMeasurementCacheTests
{
    [Fact]
    public void StaticRibbonNormalization_InvalidatesWidthSensitiveAdaptiveCaches()
    {
        var ribbonSource = System.IO.File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Ribbon.cs"));
        var adaptiveSource = System.IO.File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.RibbonAdaptive.cs"));

        var staticNormalizer = ribbonSource.Substring(
            ribbonSource.IndexOf("private void NormalizeStaticRibbonSurfaceForSelectedTabOnce", StringComparison.Ordinal),
            ribbonSource.IndexOf("private void NormalizeRibbonGroupMetadata", StringComparison.Ordinal) -
            ribbonSource.IndexOf("private void NormalizeStaticRibbonSurfaceForSelectedTabOnce", StringComparison.Ordinal));
        var invalidator = adaptiveSource.Substring(
            adaptiveSource.IndexOf("private void InvalidateRibbonAdaptiveMeasurementCaches", StringComparison.Ordinal),
            adaptiveSource.IndexOf("private double GetRibbonAvailableWidth", StringComparison.Ordinal) -
            adaptiveSource.IndexOf("private void InvalidateRibbonAdaptiveMeasurementCaches", StringComparison.Ordinal));

        staticNormalizer.Should().Contain("InvalidateRibbonAdaptiveMeasurementCaches();");
        staticNormalizer.Should().NotContain("_ribbonAdaptiveStateDiffInvalidated = true;");
        invalidator.Should().Contain("_ribbonAdaptiveMeasurementCacheKey = null;");
        invalidator.Should().Contain("_ribbonAdaptiveGroupCache = null;");
        invalidator.Should().Contain("_ribbonResizeThresholdCacheKey = null;");
        invalidator.Should().Contain("_ribbonResizeThresholds = [];");
        invalidator.Should().Contain("_ribbonCompactSnapshotCacheKey = null;");
        invalidator.Should().Contain("_ribbonCompactGroupSnapshotCache = null;");
        invalidator.Should().Contain("_lastRibbonAdaptiveAppliedStateKey = null;");
        invalidator.Should().Contain("_ribbonCorrectedStateCache.Clear();");
        invalidator.Should().Contain("_ribbonMeasuredOverflowCache.Clear();");
        invalidator.Should().Contain("_ribbonAdaptiveStateDiffInvalidated = true;");
    }
}
