using System.Windows;
using System.Windows.Media;

using FreeX.Core.Model;

namespace FreeX.App.UI;

public partial class GridView
{
    private DrawingGroup? _preSelectionLayerCache;
    private PreSelectionLayerCacheKey _preSelectionLayerCacheKey;
    private PreSelectionLayerCacheKey _lastPreSelectionLayerRenderKey;
    private bool _hasLastPreSelectionLayerRenderKey;
    private bool _selectionVisualOnlyChangePending;

    private readonly record struct PreSelectionLayerCacheKey(
        ViewportModel Viewport,
        bool SkipHeavyLayers,
        double ActualWidth,
        double ActualHeight,
        double RowHeaderWidth,
        double ColumnHeaderHeight,
        bool ShowGridLines,
        WorksheetBackgroundImage? WorksheetBackground,
        CellAddress? EditingCell,
        IReadOnlyList<GridRange>? MergedRegions,
        int MergedRegionCount,
        long MergedRegionSignature,
        WorksheetViewMode WorksheetViewMode,
        IReadOnlyCollection<uint>? RowPageBreaks,
        int RowPageBreakCount,
        ulong RowPageBreakFingerprint,
        IReadOnlyCollection<uint>? ColumnPageBreaks,
        int ColumnPageBreakCount,
        ulong ColumnPageBreakFingerprint,
        GridRange? PrintArea,
        bool ShowRulers,
        WorksheetPageMargins PageMargins,
        WorksheetPageOrientation PageOrientation,
        WorksheetPaperSize PaperSize,
        IReadOnlyList<SparklineModel>? Sparklines,
        int SparklineCount,
        IReadOnlyDictionary<Guid, IReadOnlyList<double>>? SparklineValues,
        int SparklineValueCount,
        GridRange? QuickAnalysisPreviewRange,
        GridQuickAnalysisPreviewVisualKind QuickAnalysisPreviewVisual);

    private void RenderPreSelectionLayersWithCache(
        DrawingContext dc,
        bool skipHeavyLayers,
        bool isLiveResizing)
    {
        if (!CanCachePreSelectionLayers(skipHeavyLayers, isLiveResizing))
        {
            ClearPreSelectionLayerCache();
            RenderPreSelectionLayers(dc, skipHeavyLayers, isLiveResizing);
            return;
        }

        var key = CreatePreSelectionLayerCacheKey(skipHeavyLayers);
        if (_preSelectionLayerCache is { } cached &&
            _preSelectionLayerCacheKey == key)
        {
            dc.DrawDrawing(cached);
            return;
        }

        if (_preSelectionLayerCache is not null)
            ClearPreSelectionLayerCache();

        if (ShouldBuildPreSelectionLayerCache(key))
        {
            var rebuilt = BuildPreSelectionLayerCache(skipHeavyLayers, isLiveResizing);
            _preSelectionLayerCache = rebuilt;
            _preSelectionLayerCacheKey = key;
            RememberPreSelectionLayerRenderKey(key);
            dc.DrawDrawing(rebuilt);
            return;
        }

        RenderPreSelectionLayers(dc, skipHeavyLayers, isLiveResizing);
        RememberPreSelectionLayerRenderKey(key);
    }

    private static bool CanCachePreSelectionLayers(bool skipHeavyLayers, bool isLiveResizing) =>
        !isLiveResizing;

    private bool ShouldBuildPreSelectionLayerCache(PreSelectionLayerCacheKey key) =>
        _selectionVisualOnlyChangePending ||
        (_hasLastPreSelectionLayerRenderKey && _lastPreSelectionLayerRenderKey == key);

    private void RememberPreSelectionLayerRenderKey(PreSelectionLayerCacheKey key)
    {
        _lastPreSelectionLayerRenderKey = key;
        _hasLastPreSelectionLayerRenderKey = true;
    }

    private DrawingGroup BuildPreSelectionLayerCache(bool skipHeavyLayers, bool isLiveResizing)
    {
        var group = new DrawingGroup();
        using (var groupContext = group.Open())
            RenderPreSelectionLayers(groupContext, skipHeavyLayers, isLiveResizing);

        if (group.CanFreeze)
            group.Freeze();

        return group;
    }

    private PreSelectionLayerCacheKey CreatePreSelectionLayerCacheKey(bool skipHeavyLayers)
    {
        var mergedRegions = MergedRegions;
        var rowPageBreaks = RowPageBreaks;
        var columnPageBreaks = ColumnPageBreaks;
        var sparklines = Sparklines;
        var sparklineValues = SparklineValues;

        return new PreSelectionLayerCacheKey(
            Viewport!,
            skipHeavyLayers,
            ActualWidth,
            ActualHeight,
            ActualRowHeaderWidth,
            EffectiveColHeaderHeight,
            ShowGridLines,
            WorksheetBackground,
            EditingCell,
            mergedRegions,
            mergedRegions?.Count ?? 0,
            mergedRegions is { Count: > 0 } ? CalculateMergedRegionSignature(mergedRegions) : 0,
            WorksheetViewMode,
            rowPageBreaks,
            rowPageBreaks?.Count ?? 0,
            rowPageBreaks is { Count: > 0 } ? CalculatePageBreakFingerprint(rowPageBreaks) : 0,
            columnPageBreaks,
            columnPageBreaks?.Count ?? 0,
            columnPageBreaks is { Count: > 0 } ? CalculatePageBreakFingerprint(columnPageBreaks) : 0,
            PrintArea,
            ShowRulers,
            PageMargins,
            PageOrientation,
            PaperSize,
            sparklines,
            sparklines?.Count ?? 0,
            sparklineValues,
            sparklineValues?.Count ?? 0,
            QuickAnalysisPreviewRange,
            QuickAnalysisPreviewVisual);
    }

    private void MarkSelectionVisualOnlyChange() => _selectionVisualOnlyChangePending = true;

    private void ClearPreSelectionLayerCache()
    {
        _preSelectionLayerCache = null;
        _hasLastPreSelectionLayerRenderKey = false;
    }
}
