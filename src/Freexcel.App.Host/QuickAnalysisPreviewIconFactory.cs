using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Freexcel.App.Host;

public static class QuickAnalysisPreviewIconFactory
{
    public static FrameworkElement Create(QuickAnalysisPreviewVisual visual)
    {
        var canvas = new Canvas
        {
            Width = 34,
            Height = 22,
            Margin = new Thickness(0, 0, 6, 0)
        };

        switch (visual.Kind)
        {
            case QuickAnalysisPreviewVisualKind.DataBars:
                AddBars(canvas, vertical: false, stacked: false);
                break;
            case QuickAnalysisPreviewVisualKind.ColorScale:
                AddColorScale(canvas);
                break;
            case QuickAnalysisPreviewVisualKind.IconSet:
                AddIconSet(canvas);
                break;
            case QuickAnalysisPreviewVisualKind.Highlight:
                AddGrid(canvas, Brushes.LightGoldenrodYellow, Brushes.Goldenrod);
                break;
            case QuickAnalysisPreviewVisualKind.ClearFormat:
                AddGrid(canvas, Brushes.White, Brushes.LightGray);
                AddLine(canvas, 6, 17, 28, 5, Brushes.Firebrick, 1.5);
                break;
            case QuickAnalysisPreviewVisualKind.ColumnChart:
                AddBars(canvas, vertical: true, stacked: false);
                break;
            case QuickAnalysisPreviewVisualKind.StackedColumnChart:
                AddBars(canvas, vertical: true, stacked: true);
                break;
            case QuickAnalysisPreviewVisualKind.LineChart:
            case QuickAnalysisPreviewVisualKind.LineSparkline:
                AddLineChart(canvas);
                break;
            case QuickAnalysisPreviewVisualKind.PieChart:
                AddPie(canvas);
                break;
            case QuickAnalysisPreviewVisualKind.BarChart:
                AddBars(canvas, vertical: false, stacked: false);
                break;
            case QuickAnalysisPreviewVisualKind.AreaChart:
                AddArea(canvas);
                break;
            case QuickAnalysisPreviewVisualKind.ScatterChart:
                AddScatter(canvas);
                break;
            case QuickAnalysisPreviewVisualKind.TotalFormula:
                AddFormula(canvas);
                break;
            case QuickAnalysisPreviewVisualKind.Table:
                AddGrid(canvas, new SolidColorBrush(Color.FromRgb(229, 244, 239)), new SolidColorBrush(Color.FromRgb(38, 120, 95)));
                break;
            case QuickAnalysisPreviewVisualKind.WinLossSparkline:
                AddWinLoss(canvas);
                break;
            default:
                AddGrid(canvas, Brushes.White, Brushes.LightGray);
                break;
        }

        return canvas;
    }

    private static void AddGrid(Canvas canvas, Brush fill, Brush stroke)
    {
        for (var row = 0; row < 2; row++)
        {
            for (var col = 0; col < 3; col++)
            {
                var rect = new Rectangle
                {
                    Width = 9,
                    Height = 8,
                    Fill = fill,
                    Stroke = stroke,
                    StrokeThickness = 0.6
                };
                Canvas.SetLeft(rect, 3 + col * 9);
                Canvas.SetTop(rect, 3 + row * 8);
                canvas.Children.Add(rect);
            }
        }
    }

    private static void AddBars(Canvas canvas, bool vertical, bool stacked)
    {
        var brush = new SolidColorBrush(Color.FromRgb(70, 130, 180));
        var accent = new SolidColorBrush(Color.FromRgb(132, 185, 95));
        if (vertical)
        {
            var heights = new[] { 8.0, 15.0, 11.0 };
            for (var i = 0; i < heights.Length; i++)
            {
                AddRect(canvas, 7 + i * 8, 18 - heights[i], 5, heights[i], brush);
                if (stacked)
                    AddRect(canvas, 7 + i * 8, 18 - heights[i] - 4, 5, 4, accent);
            }
        }
        else
        {
            var widths = new[] { 14.0, 22.0, 18.0 };
            for (var i = 0; i < widths.Length; i++)
                AddRect(canvas, 5, 5 + i * 5, widths[i], 3, brush);
        }
    }

    private static void AddColorScale(Canvas canvas)
    {
        AddRect(canvas, 4, 5, 8, 12, new SolidColorBrush(Color.FromRgb(248, 105, 107)));
        AddRect(canvas, 13, 5, 8, 12, new SolidColorBrush(Color.FromRgb(255, 235, 132)));
        AddRect(canvas, 22, 5, 8, 12, new SolidColorBrush(Color.FromRgb(99, 190, 123)));
    }

    private static void AddIconSet(Canvas canvas)
    {
        AddEllipse(canvas, 6, 7, Brushes.Firebrick);
        AddEllipse(canvas, 15, 7, Brushes.Goldenrod);
        AddEllipse(canvas, 24, 7, Brushes.SeaGreen);
    }

    private static void AddLineChart(Canvas canvas)
    {
        AddLine(canvas, 5, 16, 13, 10, Brushes.SteelBlue, 1.4);
        AddLine(canvas, 13, 10, 21, 13, Brushes.SteelBlue, 1.4);
        AddLine(canvas, 21, 13, 29, 6, Brushes.SteelBlue, 1.4);
    }

    private static void AddPie(Canvas canvas)
    {
        AddEllipse(canvas, 8, 4, Brushes.SteelBlue, 14);
        AddRect(canvas, 21, 7, 7, 4, Brushes.Goldenrod);
        AddRect(canvas, 21, 13, 7, 4, Brushes.SeaGreen);
    }

    private static void AddArea(Canvas canvas)
    {
        var polygon = new Polygon
        {
            Points = [new Point(5, 17), new Point(11, 9), new Point(18, 12), new Point(27, 5), new Point(29, 17)],
            Fill = new SolidColorBrush(Color.FromArgb(150, 70, 130, 180))
        };
        canvas.Children.Add(polygon);
    }

    private static void AddScatter(Canvas canvas)
    {
        AddEllipse(canvas, 6, 13, Brushes.SteelBlue, 4);
        AddEllipse(canvas, 14, 8, Brushes.SeaGreen, 4);
        AddEllipse(canvas, 24, 5, Brushes.Goldenrod, 4);
    }

    private static void AddFormula(Canvas canvas)
    {
        var text = new TextBlock
        {
            Text = "fx",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.SteelBlue
        };
        Canvas.SetLeft(text, 10);
        Canvas.SetTop(text, 1);
        canvas.Children.Add(text);
    }

    private static void AddWinLoss(Canvas canvas)
    {
        AddRect(canvas, 7, 5, 4, 6, Brushes.SeaGreen);
        AddRect(canvas, 15, 11, 4, 6, Brushes.Firebrick);
        AddRect(canvas, 23, 5, 4, 6, Brushes.SeaGreen);
    }

    private static void AddRect(Canvas canvas, double left, double top, double width, double height, Brush fill)
    {
        var rect = new Rectangle
        {
            Width = width,
            Height = height,
            Fill = fill
        };
        Canvas.SetLeft(rect, left);
        Canvas.SetTop(rect, top);
        canvas.Children.Add(rect);
    }

    private static void AddEllipse(Canvas canvas, double left, double top, Brush fill, double size = 5)
    {
        var ellipse = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = fill
        };
        Canvas.SetLeft(ellipse, left);
        Canvas.SetTop(ellipse, top);
        canvas.Children.Add(ellipse);
    }

    private static void AddLine(Canvas canvas, double x1, double y1, double x2, double y2, Brush stroke, double thickness)
    {
        canvas.Children.Add(new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = stroke,
            StrokeThickness = thickness
        });
    }
}
