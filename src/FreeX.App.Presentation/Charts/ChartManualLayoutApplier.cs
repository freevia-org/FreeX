using Free.Shared.Drawing;

namespace FreeX.App.Presentation.Charts;

/// <summary>Applies per-point manual data-label layouts (dragged labels) to a laid-out chart.</summary>
internal static class ChartManualLayoutApplier
{
    public static ChartLayout ApplyDataLabelOffsets(ChartLayoutRequest request, ChartLayout layout)
    {
        var w = request.ChartWidth;
        var h = request.ChartHeight;
        var formats = request.Chart.PointDataLabelFormats;
        if (!(w > 0) || !(h > 0) || layout.DataLabels.Count == 0 || formats.Count == 0)
            return layout;

        List<DataLabelBox>? moved = null;
        for (var i = 0; i < layout.DataLabels.Count; i++)
        {
            var box = layout.DataLabels[i];
            var format = formats.FirstOrDefault(f => f.SeriesIndex == box.SeriesIndex && f.PointIndex == box.PointIndex && f.Layout is not null);
            if (format?.Layout is null ||
                !ChartManualLayoutGeometry.TryResolveOrigin(format.Layout, w, h, new LayoutPoint(0, 0), out var delta))
            {
                continue;
            }

            // Factor layouts are offsets; an edge layout pins the label's top-left corner.
            var target = ChartManualLayoutGeometry.ClampInside(
                string.Equals(format.Layout.XMode, ChartManualLayoutGeometry.EdgeMode, StringComparison.OrdinalIgnoreCase)
                    ? new LayoutRect(delta.X, delta.Y, box.Bounds.Width, box.Bounds.Height)
                    : new LayoutRect(box.Bounds.Left + delta.X, box.Bounds.Top + delta.Y, box.Bounds.Width, box.Bounds.Height),
                w,
                h);
            var dx = target.Left - box.Bounds.Left;
            var dy = target.Top - box.Bounds.Top;
            moved ??= layout.DataLabels.ToList();
            moved[i] = new DataLabelBox(
                box.SeriesIndex,
                box.PointIndex,
                box.Text,
                new LayoutPoint(box.Anchor.X + dx, box.Anchor.Y + dy),
                target);
        }

        return moved is null
            ? layout
            : new ChartLayout
            {
                Type = layout.Type,
                PlotArea = layout.PlotArea,
                CategoryAxis = layout.CategoryAxis,
                ValueAxis = layout.ValueAxis,
                SecondaryValueAxis = layout.SecondaryValueAxis,
                Radar = layout.Radar,
                Series = layout.Series,
                Legend = layout.Legend,
                DataLabels = moved,
            };
    }
}
