using System.Windows;
using System.Windows.Media;

using FreeX.App.Presentation.QuickAnalysis;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public partial class GridView
{
    private DrawingGroup? _preSelectionLayerCache;
    private PreSelectionLayerCacheKey _preSelectionLayerCacheKey;
    private PreSelectionLayerCacheKey _lastPreSelectionLayerRenderKey;
    private bool _hasLastPreSelectionLayerRenderKey;
    private bool _selectionVisualOnlyChangePending;

    internal readonly record struct PreSelectionLayerCacheKey(
        IReadOnlyList<DisplayCell> Cells,
        IReadOnlyList<RowMetric> RowMetrics,
        IReadOnlyList<ColMetric> ColMetrics,
        FrozenPaneState? FrozenPanes,
        IReadOnlyList<OverlayPrimitive>? Overlays,
        SplitPaneState? SplitPanes,
        IReadOnlyList<ChartDataCell>? ChartDataCells,
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
        GridRange? PagePreviewRange,
        bool ShowRulers,
        WorksheetPageMargins PageMargins,
        WorksheetPageOrientation PageOrientation,
        WorksheetPaperSize PaperSize,
        WorksheetPageOrder PageOrder,
        WorksheetScaleToFit ScaleToFit,
        WorksheetRepeatRange? PrintTitleRows,
        WorksheetRepeatRange? PrintTitleColumns,
        IReadOnlyList<SparklineModel>? Sparklines,
        int SparklineCount,
        IReadOnlyDictionary<Guid, IReadOnlyList<double>>? SparklineValues,
        int SparklineValueCount,
        GridRange? QuickAnalysisPreviewRange,
        QuickAnalysisPreviewVisualKind QuickAnalysisPreviewVisual,
        // R175: this cache holds the fully rendered cell layer, so anything the cell painters
        // resolve against the workbook theme -- font colors, fills (CellStyle.ResolveFontColor/
        // ResolveFillColor) and, since R175, border colors (CellBorder.ResolveColor) -- is baked
        // into the cached DrawingGroup. Without the theme in the key a Theme Colors swap that left
        // the viewport otherwise untouched replayed the stale drawing forever. WorkbookTheme is a
        // record whose Colors dictionary compares by reference, so a genuinely new theme never
        // matches while the SAME instance being re-assigned (what MainWindow.Viewport.cs does on
        // every viewport refresh) still does -- the cache survives the common no-op case.
        WorkbookTheme WorkbookTheme);

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

    internal PreSelectionLayerCacheKey CreatePreSelectionLayerCacheKey(bool skipHeavyLayers)
    {
        var mergedRegions = MergedRegions;
        var rowPageBreaks = RowPageBreaks;
        var columnPageBreaks = ColumnPageBreaks;
        var sparklines = Sparklines;
        var sparklineValues = SparklineValues;
        var viewport = Viewport!;

        return new PreSelectionLayerCacheKey(
            viewport.Cells,
            viewport.RowMetrics,
            viewport.ColMetrics,
            viewport.FrozenPanes,
            viewport.Overlays,
            viewport.SplitPanes,
            viewport.ChartDataCells,
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
            PagePreviewRange,
            ShowRulers,
            PageMargins,
            PageOrientation,
            PaperSize,
            PageOrder,
            ScaleToFit,
            PrintTitleRows,
            PrintTitleColumns,
            sparklines,
            sparklines?.Count ?? 0,
            sparklineValues,
            sparklineValues?.Count ?? 0,
            QuickAnalysisPreviewRange,
            QuickAnalysisPreviewVisual,
            WorkbookTheme);
    }

    private void MarkSelectionVisualOnlyChange() => _selectionVisualOnlyChangePending = true;

    private void ClearPreSelectionLayerCache()
    {
        _preSelectionLayerCache = null;
        _hasLastPreSelectionLayerRenderKey = false;
    }
}
