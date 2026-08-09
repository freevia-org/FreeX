using FreeX.App.Presentation.GridInteraction;
using FreeX.App.Presentation.Rendering;
using FreeX.Core.Model;
using System.Collections;
using System.Windows;

namespace FreeX.App.UI;

public interface ISplitPaneCellLayoutConsumer
{
    void AcceptLayout(SplitPaneCellLayout layout);
}

/// <summary>Thin WPF geometry adapter over <see cref="ViewportGeometryPlanner"/>.</summary>
public static class SplitPaneCellLayoutPlanner
{
    public static IReadOnlyList<SplitPaneCellLayout> CalculateLayouts(
        ViewportModel viewport,
        IReadOnlyList<GridRange>? mergedRegions = null,
        CellAddress? editingCell = null)
    {
        var layouts = ViewportGeometryPlanner.CalculateSplitPaneLayouts(
            viewport,
            CreateSettings(viewport),
            mergedRegions,
            editingCell);
        return layouts.Count == 0 ? [] : new WpfViewportCellLayoutList(layouts);
    }

    public static void VisitLayouts<TConsumer>(
        ViewportModel viewport,
        IReadOnlyList<GridRange>? mergedRegions,
        CellAddress? editingCell,
        ref TConsumer consumer)
        where TConsumer : struct, ISplitPaneCellLayoutConsumer
    {
        var adapter = new WpfViewportCellLayoutConsumer<TConsumer>(consumer);
        ViewportGeometryPlanner.VisitSplitPaneLayouts(
            viewport,
            CreateSettings(viewport),
            mergedRegions,
            editingCell,
            ref adapter);
        consumer = adapter.Consumer;
    }

    private static ViewportGeometrySettings CreateSettings(ViewportModel viewport) =>
        new(
            GridView.CalculateRowHeaderWidth(viewport),
            GridView.ColHeaderHeight,
            MetricPlacement: ViewportMetricPlacement.MetricOffsets,
            HitTestEdges: ViewportHitTestEdgeBehavior.ExclusiveEnd);

    private static SplitPaneCellLayout ToWpf(ViewportCellLayout layout) =>
        new(
            layout.Cell,
            ToWpf(layout.Bounds),
            ToWpf(layout.TextClipBounds),
            layout.Region switch
            {
                SplitPanePointerRegion.TopLeft => SplitPaneRegion.TopLeft,
                SplitPanePointerRegion.TopRight => SplitPaneRegion.TopRight,
                SplitPanePointerRegion.BottomLeft => SplitPaneRegion.BottomLeft,
                _ => SplitPaneRegion.BottomRight,
            });

    private static Rect ToWpf(LayoutRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private struct WpfViewportCellLayoutConsumer<TConsumer>(TConsumer consumer) : IViewportCellLayoutConsumer
        where TConsumer : struct, ISplitPaneCellLayoutConsumer
    {
        private TConsumer _consumer = consumer;

        public readonly TConsumer Consumer => _consumer;

        public void AcceptLayout(ViewportCellLayout layout) => _consumer.AcceptLayout(ToWpf(layout));
    }

    private sealed class WpfViewportCellLayoutList(IReadOnlyList<ViewportCellLayout> layouts)
        : IReadOnlyList<SplitPaneCellLayout>
    {
        public int Count => layouts.Count;

        public SplitPaneCellLayout this[int index] => ToWpf(layouts[index]);

        public IEnumerator<SplitPaneCellLayout> GetEnumerator()
        {
            for (var index = 0; index < Count; index++)
                yield return this[index];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
