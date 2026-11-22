using Free.Shared.Drawing;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Legends;

namespace FreeX.App.UI;

/// <summary>
/// Honors manual (dragged) chart layouts in the OxyPlot-backed WPF renderer: the plot area is pinned by
/// fixed plot margins, the legend by a <see cref="PlacedLegend"/>, and the chart / axis titles are drawn
/// by <see cref="ScreenTextAnnotation"/> at their dragged position. The fractions come from
/// <see cref="ChartModel"/>'s manual layouts via the shared <see cref="ChartManualLayoutGeometry"/> so
/// this renderer, the Avalonia renderer and the file round-trip agree.
/// </summary>
public static partial class ChartRenderer
{
    /// <summary>Applies the chart's manual layouts to <paramref name="model"/> rendered at <paramref name="scale"/> pixels per chart pixel.</summary>
    internal static void ApplyManualLayouts(PlotModel model, ChartModel chart, double scale)
    {
        var width = Math.Max(1, chart.Width);
        var height = Math.Max(1, chart.Height);

        if (chart.PlotAreaLayout is { } plotLayout &&
            string.Equals(plotLayout.LayoutTarget, ChartManualLayoutGeometry.InnerTarget, StringComparison.OrdinalIgnoreCase) &&
            ChartManualLayoutGeometry.TryResolveRect(plotLayout, width, height, new LayoutRect(0, 0, width, height), out var plot) &&
            plot.Width > 1 && plot.Height > 1)
        {
            model.Padding = new OxyThickness(0);
            model.PlotMargins = new OxyThickness(
                plot.Left * scale,
                plot.Top * scale,
                Math.Max(0, (width - plot.Right) * scale),
                Math.Max(0, (height - plot.Bottom) * scale));
        }

        if (model.Legends.OfType<Legend>().FirstOrDefault() is { } legend &&
            ChartManualLayoutGeometry.HasPosition(chart.LegendLayout))
        {
            var placed = PlacedLegend.From(legend, chart.LegendLayout!, width, height, scale);
            var index = model.Legends.IndexOf(legend);
            model.Legends[index] = placed;
        }

        if (ChartManualLayoutGeometry.HasPosition(chart.TitleLayout) && !string.IsNullOrWhiteSpace(model.Title))
        {
            model.Annotations.Add(new ScreenTextAnnotation(
                model.Title!, model.TitleFont, model.TitleFontSize * scale, model.TitleFontWeight, model.TitleColor,
                chart.TitleLayout!, width, height, scale, rotated: false));
            model.Title = null;
        }

        ApplyAxisTitleLayout(model, chart.XAxisTitleLayout, AxisPosition.Bottom, width, height, scale);
        ApplyAxisTitleLayout(model, chart.YAxisTitleLayout, AxisPosition.Left, width, height, scale);
    }

    private static void ApplyAxisTitleLayout(
        PlotModel model,
        ChartManualLayoutModel? layout,
        AxisPosition position,
        double width,
        double height,
        double scale)
    {
        if (!ChartManualLayoutGeometry.HasPosition(layout))
            return;

        var axis = model.Axes.FirstOrDefault(a => a.Position == position && string.IsNullOrEmpty(a.Key) && !string.IsNullOrWhiteSpace(a.Title));
        if (axis is null)
            return;

        model.Annotations.Add(new ScreenTextAnnotation(
            axis.Title!, axis.TitleFont, axis.TitleFontSize * scale, axis.TitleFontWeight, axis.TitleColor,
            layout!, width, height, scale, rotated: position is AxisPosition.Left or AxisPosition.Right));
        axis.Title = null;
    }

    /// <summary>An OxyPlot legend pinned to a manual layout position (OxyPlot only offers corner placement).</summary>
    internal sealed class PlacedLegend : Legend
    {
        private ChartManualLayoutModel _layout = new();
        private double _width;
        private double _height;
        private double _scale = 1;

        public static PlacedLegend From(Legend source, ChartManualLayoutModel layout, double width, double height, double scale) =>
            new()
            {
                _layout = layout,
                _width = width,
                _height = height,
                _scale = scale,
                // Inside placement so the plot area does not reserve a gutter for a legend the user moved.
                LegendPlacement = LegendPlacement.Inside,
                LegendTextColor = source.LegendTextColor,
                LegendFontSize = source.LegendFontSize,
                LegendBackground = source.LegendBackground,
                LegendBorder = source.LegendBorder,
                LegendBorderThickness = source.LegendBorderThickness,
                LegendPosition = LegendPosition.TopLeft,
            };

        public override OxyRect GetLegendRectangle(OxySize legendSize)
        {
            ChartManualLayoutGeometry.TryResolveOrigin(_layout, _width, _height, new LayoutPoint(0, 0), out var origin);
            var x = Math.Clamp(origin.X * _scale, 0, Math.Max(0, (_width * _scale) - legendSize.Width));
            var y = Math.Clamp(origin.Y * _scale, 0, Math.Max(0, (_height * _scale) - legendSize.Height));
            return new OxyRect(x, y, legendSize.Width, legendSize.Height);
        }
    }

    /// <summary>Text drawn at a fixed position of the chart (a dragged title), in render pixels.</summary>
    internal sealed class ScreenTextAnnotation : Annotation
    {
        private readonly string _text;
        private readonly string? _font;
        private readonly double _fontSize;
        private readonly double _fontWeight;
        private readonly OxyColor _color;
        private readonly ChartManualLayoutModel _layout;
        private readonly double _width;
        private readonly double _height;
        private readonly double _scale;
        private readonly bool _rotated;

        public ScreenTextAnnotation(
            string text,
            string? font,
            double fontSize,
            double fontWeight,
            OxyColor color,
            ChartManualLayoutModel layout,
            double width,
            double height,
            double scale,
            bool rotated)
        {
            _text = text;
            _font = font;
            _fontSize = fontSize > 0 ? fontSize : 12 * scale;
            _fontWeight = fontWeight;
            _color = color.IsUndefined() || color.IsAutomatic() ? OxyColors.Black : color;
            _layout = layout;
            _width = width;
            _height = height;
            _scale = scale;
            _rotated = rotated;
            Layer = AnnotationLayer.AboveSeries;
        }

        public override void Render(IRenderContext rc)
        {
            ChartManualLayoutGeometry.TryResolveOrigin(_layout, _width, _height, new LayoutPoint(0, 0), out var origin);
            var size = rc.MeasureText(_text, _font, _fontSize, _fontWeight);
            var x = origin.X * _scale;
            var y = origin.Y * _scale;
            if (_rotated)
            {
                // Rotated -90 degrees the text runs upward from its anchor, so anchor at the bottom of its box.
                rc.DrawText(new ScreenPoint(x, y + size.Width), _text, _color, _font, _fontSize, _fontWeight, -90,
                    OxyPlot.HorizontalAlignment.Left, OxyPlot.VerticalAlignment.Top);
                return;
            }

            rc.DrawText(new ScreenPoint(x, y), _text, _color, _font, _fontSize, _fontWeight, 0,
                OxyPlot.HorizontalAlignment.Left, OxyPlot.VerticalAlignment.Top);
        }
    }
}
