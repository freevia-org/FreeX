using System.Collections.Generic;
using System.Linq;
using System.Windows;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Coverage for chart rendering (<c>DocumentView.BuildChartRun</c>): it renders every series and honours
/// the <see cref="ChartKind"/> (line vs column), rather than sketching only the first series as columns.
/// Runs on STA because it builds the real WPF editing surface.
/// </summary>
public sealed class ChartRenderingTests
{
    private static DocumentView ViewWithChart(ChartKind kind)
    {
        var view = new DocumentView();
        view.LoadModel(TextDocument.CreateEmpty());
        var chart = new Chart { Kind = kind, Title = "t" };
        chart.Categories.AddRange(new[] { "A", "B", "C" });
        chart.Series.Add(new ChartSeries("S1", new double[] { 1, 2, 3 }));
        chart.Series.Add(new ChartSeries("S2", new double[] { 3, 2, 1 }));
        view.InsertChart(chart);
        return view;
    }

    private static List<T> LogicalDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        var result = new List<T>();
        foreach (var child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject d)
            {
                if (d is T t)
                    result.Add(t);
                result.AddRange(LogicalDescendants<T>(d));
            }
        return result;
    }

    [StaFact]
    public void LineChart_RendersOnePolylinePerSeries()
    {
        var view = ViewWithChart(ChartKind.Line);
        var polylines = LogicalDescendants<System.Windows.Shapes.Polyline>(view.Document);
        Assert.Equal(2, polylines.Count); // one per series; legend swatches are Rectangles, not Polylines
    }

    [StaFact]
    public void ColumnChart_RendersBarsAndNoPolylines()
    {
        var view = ViewWithChart(ChartKind.Column);
        Assert.Empty(LogicalDescendants<System.Windows.Shapes.Polyline>(view.Document));
        // 2 series x 3 categories = 6 bars, plus 2 legend swatches = 8 rectangles (>= 6 either way).
        var rects = LogicalDescendants<System.Windows.Shapes.Rectangle>(view.Document);
        Assert.True(rects.Count >= 6, $"expected >= 6 bar rectangles, got {rects.Count}");
    }

    [StaFact]
    public void ColumnChart_DrawsValueGridlines()
    {
        var view = ViewWithChart(ChartKind.Column);
        // 4 horizontal gridlines + 1 baseline axis line; without gridlines there would be just the axis.
        var lines = LogicalDescendants<System.Windows.Shapes.Line>(view.Document);
        Assert.True(lines.Count >= 5, $"expected gridlines + axis (>= 5 lines), got {lines.Count}");
    }
}
