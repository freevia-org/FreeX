using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Sparklines;
using FreeX.Core.Model;

using AvaloniaEllipse = Avalonia.Controls.Shapes.Ellipse;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaLine = Avalonia.Controls.Shapes.Line;
using AvaloniaPoint = Avalonia.Point;
using AvaloniaRectangle = Avalonia.Controls.Shapes.Rectangle;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// A binding-free in-cell layer that paints a sparkline (line / column / win-loss) inside the cell,
/// behind the cell text. The geometry comes from the portable <see cref="SparklineLayoutEngine"/>,
/// resolved against the panel's measured size at arrange time so the sparkline tracks the cell's
/// actual rendered box without data binding — the same arrange-time pattern as
/// <see cref="ConditionalDataBarPanel"/>. Colors mirror the Windows GridView sparkline brushes.
/// </summary>
internal sealed class SparklineCellPanel : Panel
{
    private static readonly IBrush PositiveBrush = new SolidColorBrush(Color.FromRgb(0x21, 0x73, 0x46));
    private static readonly IBrush NegativeBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0x00, 0x00));
    private const double LineThickness = 1.25;
    private const double Inset = SparklineRenderPlanner.CellInset;

    private readonly IReadOnlyList<double> _values;
    private readonly SparklineKind _kind;
    private Size _lastArrange = new(-1, -1);

    public SparklineCellPanel(IReadOnlyList<double> values, SparklineKind kind)
    {
        _values = values;
        _kind = kind;
        IsHitTestVisible = false;
        ClipToBounds = true;
    }

    protected override Size MeasureOverride(Size availableSize) => new(0, 0);

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (finalSize != _lastArrange)
        {
            _lastArrange = finalSize;
            Children.Clear();
            Build(finalSize);
        }

        foreach (var child in Children)
            child.Arrange(new Rect(finalSize));

        return finalSize;
    }

    private void Build(Size size)
    {
        var rect = new LayoutRect(
            Inset,
            Inset,
            Math.Max(1, size.Width - (Inset * 2)),
            Math.Max(1, size.Height - (Inset * 2)));
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        if (_kind == SparklineKind.Line)
            BuildLine(rect);
        else
            BuildColumns(rect);
    }

    private void BuildLine(LayoutRect rect)
    {
        var layout = SparklineLayoutEngine.CalculateLineLayout(_values, rect);
        if (layout.SinglePoint is { } single)
        {
            Children.Add(new AvaloniaEllipse
            {
                Width = 3,
                Height = 3,
                Fill = PositiveBrush,
                Margin = new Thickness(single.X - 1.5, single.Y - 1.5, 0, 0),
                HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
                VerticalAlignment = AvaloniaVerticalAlignment.Top,
                IsHitTestVisible = false,
            });
            return;
        }

        foreach (var segment in layout.Segments)
        {
            Children.Add(new AvaloniaLine
            {
                StartPoint = new AvaloniaPoint(segment.Start.X, segment.Start.Y),
                EndPoint = new AvaloniaPoint(segment.End.X, segment.End.Y),
                Stroke = PositiveBrush,
                StrokeThickness = LineThickness,
                IsHitTestVisible = false,
            });
        }
    }

    private void BuildColumns(LayoutRect rect)
    {
        var layout = SparklineLayoutEngine.CalculateColumnLayout(_values, rect, _kind);
        foreach (var bar in layout.Bars)
        {
            Children.Add(new AvaloniaRectangle
            {
                Width = Math.Max(1, bar.Rect.Width),
                Height = Math.Max(1, bar.Rect.Height),
                Fill = bar.IsNegative ? NegativeBrush : PositiveBrush,
                Margin = new Thickness(bar.Rect.X, bar.Rect.Y, 0, 0),
                HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
                VerticalAlignment = AvaloniaVerticalAlignment.Top,
                IsHitTestVisible = false,
            });
        }
    }
}
