using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace FreeX.App.UI.Tests;

/// <summary>
/// WPF/OxyPlot side of the Excel visual leftovers (scatter lines, marker size, pie legend gutter, default axis
/// lines, stacked segment labels). Expected values are what real Excel reports through COM for the same chart.
/// </summary>
public sealed class ChartRendererExcelLeftoverTests
{
    private static readonly CellColor Blue = new(0x15, 0x60, 0x82);

    private static DisplayCell Cell(uint row, uint col, string text) =>
        new(row, col, null, text, null, StyleId.Default, null);

    private static ViewportModel Rows(params (string Category, string A, string? B)[] rows)
    {
        var cells = new List<DisplayCell> { Cell(1, 1, "P"), Cell(1, 2, "A") };
        if (rows.Any(r => r.B is not null))
            cells.Add(Cell(1, 3, "B"));
        for (var i = 0; i < rows.Length; i++)
        {
            var row = (uint)i + 2;
            cells.Add(Cell(row, 1, rows[i].Category));
            cells.Add(Cell(row, 2, rows[i].A));
            if (rows[i].B is { } b)
                cells.Add(Cell(row, 3, b));
        }

        return new ViewportModel(cells, [], []);
    }

    private static ViewportModel ScatterData() => new(
        [
            Cell(1, 1, "X"), Cell(1, 2, "North"),
            Cell(2, 1, "1"), Cell(2, 2, "12"),
            Cell(3, 1, "2.5"), Cell(3, 2, "19"),
            Cell(4, 1, "4"), Cell(4, 2, "14"),
        ],
        [],
        []);

    private static ChartModel ScatterChart(ChartSeriesFormat format)
    {
        var sheet = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Scatter,
            FirstColIsCategories = false,
            DataRange = new GridRange(new CellAddress(sheet, 1, 1), new CellAddress(sheet, 4, 2)),
        };
        chart.SeriesFormats.Add(format);
        return chart;
    }

    private static ChartModel Chart(ChartType type, uint lastRow, uint lastCol)
    {
        var sheet = SheetId.New();
        return new ChartModel
        {
            Type = type,
            DataRange = new GridRange(new CellAddress(sheet, 1, 1), new CellAddress(sheet, lastRow, lastCol)),
        };
    }

    private static PlotModel Build(ChartModel chart, ViewportModel viewport) =>
        ChartRenderer.BuildPlotModel(chart, viewport).Should().BeOfType<PlotModel>().Subject;

    [Fact]
    public void Excel_default_marker_size_5_is_a_6_67_pixel_diameter()
    {
        // <c:marker><c:size val="5"/> is the diameter in points (Excel default); OxyPlot sizes by radius in DIP.
        ChartRenderer.ToOxyMarkerSize(5).Should().BeApproximately(5 * 2.0 / 3.0, 1e-9);
        ChartRenderer.ToOxyMarkerSize(10).Should().BeApproximately(10 * 2.0 / 3.0, 1e-9);
        ChartRenderer.ToOxyMarkerSize(null).Should().BeApproximately(5 * 2.0 / 3.0, 1e-9);
    }

    [Fact]
    public void Scatter_series_marker_uses_the_excel_size()
    {
        var chart = ScatterChart(new ChartSeriesFormat(0, MarkerStyle: ChartMarkerStyle.Circle, MarkerSize: 10, StrokeColor: Blue, NoLine: true));

        var series = Build(chart, ScatterData()).Series.OfType<ScatterSeries>().Single();

        series.MarkerSize.Should().BeApproximately(10 * 2.0 / 3.0, 1e-9);
    }

    [Fact]
    public void Scatter_with_lines_strokes_the_points_beneath_the_markers()
    {
        var chart = ScatterChart(new ChartSeriesFormat(0, StrokeColor: Blue, StrokeThickness: 1.5, Smooth: true));

        var model = Build(chart, ScatterData());

        var line = model.Annotations.OfType<PolylineAnnotation>().Should().ContainSingle().Subject;
        line.Points.Select(p => (p.X, p.Y)).Should().Equal((1.0, 12.0), (2.5, 19.0), (4.0, 14.0));
        line.Layer.Should().Be(AnnotationLayer.BelowSeries);
        line.InterpolationAlgorithm.Should().NotBeNull("xlXYScatterSmooth interpolates the line");
        model.Series.OfType<ScatterSeries>().Should().ContainSingle("the markers stay on the scatter series");
    }

    [Fact]
    public void Scatter_whose_line_is_no_fill_draws_markers_only()
    {
        var chart = ScatterChart(new ChartSeriesFormat(0, StrokeColor: Blue, StrokeThickness: 3, NoLine: true));

        Build(chart, ScatterData()).Annotations.OfType<PolylineAnnotation>().Should().BeEmpty();
    }

    [Fact]
    public void Line_series_whose_line_is_no_fill_draws_no_line()
    {
        var chart = Chart(ChartType.Line, 3, 2);
        chart.SeriesFormats.Add(new ChartSeriesFormat(0, MarkerStyle: ChartMarkerStyle.Circle, NoLine: true));

        var model = Build(chart, Rows(("Q1", "3", null), ("Q2", "5", null)));

        model.Series.OfType<LineSeries>().Single().LineStyle.Should().Be(LineStyle.None);
    }

    [Fact]
    public void Default_axis_lines_follow_excel_light_grey_category_axis_and_no_value_axis_line()
    {
        var model = Build(Chart(ChartType.Column, 3, 2), Rows(("Q1", "3", null), ("Q2", "5", null)));

        var category = model.Axes.Single(a => a.Position == AxisPosition.Bottom);
        var value = model.Axes.Single(a => a.Position == AxisPosition.Left);
        category.AxislineStyle.Should().Be(LineStyle.Solid);
        category.AxislineColor.Should().Be(OxyColor.FromRgb(0xD9, 0xD9, 0xD9), "COM Axis.Format.Line.ForeColor of the category axis");
        value.AxislineStyle.Should().Be(LineStyle.None, "Excel's default value axis has no line");
    }

    [Fact]
    public void An_authored_value_axis_line_is_still_drawn()
    {
        var chart = Chart(ChartType.Column, 3, 2);
        chart.YAxisLineColor = new CellColor(255, 0, 0);

        var model = Build(chart, Rows(("Q1", "3", null), ("Q2", "5", null)));

        model.Axes.Single(a => a.Position == AxisPosition.Left).AxislineStyle.Should().Be(LineStyle.Solid);
    }

    [Fact]
    public void Pie_legend_is_the_real_legend_in_a_gutter_outside_the_pie()
    {
        var chart = Chart(ChartType.Pie, 4, 2);
        chart.ShowLegend = true;
        chart.LegendPosition = ChartLegendPosition.Bottom;

        var model = Build(chart, Rows(("Q1", "3", null), ("Q2", "5", null), ("Q3", "2", null)));

        model.Legends.Should().ContainSingle("the built-in legend lays out below the pie instead of overprinting it");
        model.Series.OfType<ScatterSeries>().Select(s => s.Title).Should().Equal("Q1", "Q2", "Q3");
        model.Annotations.OfType<RectangleAnnotation>().Should().BeEmpty("no hand-placed swatches over the plot area");
    }

    [Fact]
    public void Stacked_column_segments_get_centred_label_annotations()
    {
        var chart = Chart(ChartType.StackedColumn, 3, 3);
        chart.ShowDataLabels = true;

        var model = Build(chart, Rows(("Q1", "12", "8"), ("Q2", "19", "15")));

        var labels = model.Annotations.OfType<TextAnnotation>().ToList();
        labels.Select(l => l.Text).Should().Equal("12", "19", "8", "15");
        labels.Select(l => l.TextPosition.Y).Should().Equal(6.0, 9.5, 16.0, 26.5);
        labels.Should().OnlyContain(l => l.TextVerticalAlignment == OxyPlot.VerticalAlignment.Middle);
    }
}
