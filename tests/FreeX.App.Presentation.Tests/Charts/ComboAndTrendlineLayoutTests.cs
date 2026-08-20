using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;
using static FreeX.App.Presentation.Tests.Charts.ChartLayoutTestData;

namespace FreeX.App.Presentation.Tests.Charts;

public sealed class ComboAndTrendlineLayoutTests
{
    // ---- Secondary value axis -------------------------------------------------------------

    [Fact]
    public void No_secondary_axis_by_default()
    {
        var chart = Chart(ChartType.Column);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B"],
            [Series(0, "S1", 10, 20), Series(1, "S2", 1, 2)]));

        layout.SecondaryValueAxis.Should().BeNull();
        layout.Series.Should().OnlyContain(s => !s.UsesSecondaryAxis);
    }

    [Fact]
    public void Secondary_axis_is_built_and_assigned_series_are_flagged()
    {
        var chart = Chart(ChartType.Column, c =>
        {
            c.ShowSecondaryAxis = true;
            c.SecondaryAxisSeriesIndexes = [1];
        });
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B"],
            [Series(0, "Primary", 10, 20), Series(1, "Secondary", 1, 2)]));

        layout.SecondaryValueAxis.Should().NotBeNull();
        layout.SecondaryValueAxis!.Side.Should().Be(AxisSide.Right);

        layout.Series[0].UsesSecondaryAxis.Should().BeFalse();
        layout.Series[1].UsesSecondaryAxis.Should().BeTrue();
    }

    [Fact]
    public void Secondary_axis_with_no_explicit_list_sends_all_but_first_series_secondary()
    {
        var chart = Chart(ChartType.Line, c => c.ShowSecondaryAxis = true);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B"],
            [Series(0, "S1", 10, 20), Series(1, "S2", 1, 2), Series(2, "S3", 3, 4)]));

        layout.Series[0].UsesSecondaryAxis.Should().BeFalse();
        layout.Series[1].UsesSecondaryAxis.Should().BeTrue();
        layout.Series[2].UsesSecondaryAxis.Should().BeTrue();
    }

    [Fact]
    public void Secondary_axis_scale_is_driven_by_the_secondary_series_range()
    {
        var chart = Chart(ChartType.Column, c =>
        {
            c.ShowSecondaryAxis = true;
            c.SecondaryAxisSeriesIndexes = [1];
        });
        // Primary range ~ [0, 1000]; secondary range ~ [0, 5]. A point of value 5 on the secondary
        // axis must land near the top of the plot, not near the bottom (which the primary scale gives).
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B"],
            [Series(0, "Primary", 1000, 800), Series(1, "Secondary", 5, 4)],
            new PlotRect(0, 0, 200, 100)));

        var secondaryColumn = layout.Series[1].Bars[0];
        // The bar's top (smaller Y) should be well above the plot mid-line because 5 ≈ axis max.
        secondaryColumn.Rect.Top.Should().BeLessThan(40);
    }

    [Fact]
    public void Secondary_axis_ignored_when_only_one_series()
    {
        var chart = Chart(ChartType.Column, c => c.ShowSecondaryAxis = true);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B"], [Series(0, "S1", 10, 20)]));
        layout.SecondaryValueAxis.Should().BeNull();
    }

    // R135-render-chart-secondary-axis-scale (Avalonia/PDF twin of the WPF fix in
    // ChartRenderer.Axes.cs): ChartModel.SecondaryAxisMinimum/Maximum previously had no effect here --
    // the secondary axis scale was built purely from the plotted secondary-series data range
    // (SecondaryValueRange), with no explicit bounds argument at all, so an authored fixed secondary
    // scale (e.g. Excel's Format Axis > secondary value axis Minimum/Maximum) was silently ignored.
    // Sibling: Secondary_axis_scale_is_driven_by_the_secondary_series_range above proves the
    // auto-fit-to-data behavior is unchanged when no explicit bounds are set.
    [Fact]
    public void Secondary_axis_uses_its_own_explicit_minimum_and_maximum_not_the_auto_fit_data_range()
    {
        var chart = Chart(ChartType.Column, c =>
        {
            c.ShowSecondaryAxis = true;
            c.SecondaryAxisSeriesIndexes = [1];
            c.SecondaryAxisMinimum = 0;
            c.SecondaryAxisMaximum = 1000;
        });
        // Secondary series data only spans [1, 5] -- if the axis auto-fit to the data (the pre-fix
        // behavior) its Maximum would land near 5, not the authored 1000.
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B"],
            [Series(0, "Primary", 10, 20), Series(1, "Secondary", 1, 5)]));

        layout.SecondaryValueAxis.Should().NotBeNull();
        layout.SecondaryValueAxis!.Scale.Minimum.Should().Be(0);
        layout.SecondaryValueAxis!.Scale.Maximum.Should().Be(1000);
    }

    // R135-render-chart-secondary-axis-scale: ChartModel.SecondaryAxisLogScale previously had no
    // effect on the portable layout -- the secondary axis was always built with the plain linear
    // AxisScale.CreateValueAxis, never the logarithmic variant, regardless of this flag.
    [Fact]
    public void Secondary_axis_log_scale_applies_independently_of_primary_axis()
    {
        var chart = Chart(ChartType.Column, c =>
        {
            c.ShowSecondaryAxis = true;
            c.SecondaryAxisSeriesIndexes = [1];
            c.SecondaryAxisLogScale = true;
        });
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B"],
            [Series(0, "Primary", 10, 20), Series(1, "Secondary", 1, 100)]));

        layout.SecondaryValueAxis.Should().NotBeNull();
        layout.SecondaryValueAxis!.Scale.IsLogarithmic.Should().BeTrue();
        layout.ValueAxis!.Scale.IsLogarithmic.Should().BeFalse();
    }

    // Forward half of the R135 number-format fix: ChartModel.SecondaryAxisNumberFormat must drive the
    // secondary axis's own tick labels. Before the fix, the layout always passed the PRIMARY axis's
    // chart.YAxisNumberFormat/Code into BuildValueAxisLayout for the secondary axis too, so a
    // secondary-only Currency request had no effect on its ticks.
    [Fact]
    public void Secondary_axis_number_format_applies_independently_of_primary_axis()
    {
        var chart = Chart(ChartType.Column, c =>
        {
            c.ShowSecondaryAxis = true;
            c.SecondaryAxisSeriesIndexes = [1];
            c.SecondaryAxisNumberFormat = ChartDataLabelNumberFormat.Currency;
        });
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B"],
            [Series(0, "Primary", 10, 20), Series(1, "Secondary", 1, 5)]));

        layout.SecondaryValueAxis.Should().NotBeNull();
        layout.SecondaryValueAxis!.Ticks.Should().Contain(t => t.Label.StartsWith('$'));
    }

    // Reverse/leak half of the R135 number-format fix: a PRIMARY axis Currency format must not also
    // format the secondary axis's ticks -- before the fix, chart.YAxisNumberFormat was passed
    // unconditionally as the secondary axis's number format too, so setting only YAxisNumberFormat
    // silently formatted the secondary axis's ticks as currency even though SecondaryAxisNumberFormat
    // stayed at its General default.
    [Fact]
    public void Primary_axis_number_format_does_not_leak_onto_secondary_axis()
    {
        var chart = Chart(ChartType.Column, c =>
        {
            c.ShowSecondaryAxis = true;
            c.SecondaryAxisSeriesIndexes = [1];
            c.YAxisNumberFormat = ChartDataLabelNumberFormat.Currency;
        });
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B"],
            [Series(0, "Primary", 10, 20), Series(1, "Secondary", 1, 5)]));

        layout.SecondaryValueAxis.Should().NotBeNull();
        layout.SecondaryValueAxis!.Ticks.Should().NotContain(t => t.Label.StartsWith('$'));
        layout.ValueAxis!.Ticks.Should().Contain(t => t.Label.StartsWith('$'));
    }

    // ---- Trendline overlay ----------------------------------------------------------------

    [Fact]
    public void No_trendline_overlay_by_default()
    {
        var chart = Chart(ChartType.Line);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B", "C"], [Series(0, "S1", 1, 2, 3)]));
        layout.Series[0].Trendline.Should().BeNull();
    }

    [Fact]
    public void Linear_trendline_overlay_attaches_to_first_series_in_pixel_space()
    {
        var chart = Chart(ChartType.Line, c =>
        {
            c.ShowLinearTrendline = true;
            c.TrendlineType = ChartTrendlineType.Linear;
        });
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B", "C", "D"],
            [Series(0, "S1", 1, 2, 3, 4)]));

        var trend = layout.Series[0].Trendline;
        trend.Should().NotBeNull();
        trend!.Fit.Should().Be(TrendlineFitKind.Linear);
        trend.Points.Should().HaveCount(2);

        // For a rising series the trendline rises on screen (later x is higher => smaller Y).
        trend.Points[1].X.Should().BeGreaterThan(trend.Points[0].X);
        trend.Points[1].Y.Should().BeLessThan(trend.Points[0].Y);
    }

    [Fact]
    public void Trendline_endpoints_align_with_the_category_scale()
    {
        var chart = Chart(ChartType.Line, c =>
        {
            c.ShowLinearTrendline = true;
            c.TrendlineType = ChartTrendlineType.Linear;
        });
        var plot = new PlotRect(0, 0, 300, 100);
        var request = Request(chart, ["A", "B", "C"], [Series(0, "S1", 2, 4, 6)], plot);
        var layout = ChartLayoutEngine.Layout(request);

        var trend = layout.Series[0].Trendline!;
        var seriesPoints = layout.Series[0].Points;
        // The trendline starts at the first category x and ends at the last category x.
        trend.Points[0].X.Should().BeApproximately(seriesPoints[0].Position.X, 1e-6);
        trend.Points[^1].X.Should().BeApproximately(seriesPoints[^1].Position.X, 1e-6);
    }

    [Fact]
    public void Moving_average_overlay_records_its_fit_kind()
    {
        var chart = Chart(ChartType.Column, c =>
        {
            c.ShowLinearTrendline = true;
            c.TrendlineType = ChartTrendlineType.MovingAverage;
            c.TrendlinePeriod = 2;
        });
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B", "C", "D"],
            [Series(0, "S1", 1, 3, 2, 6)]));

        var trend = layout.Series[0].Trendline;
        trend.Should().NotBeNull();
        trend!.Fit.Should().Be(TrendlineFitKind.MovingAverage);
        trend.Points.Should().HaveCount(3);
    }

    [Fact]
    public void Scatter_trendline_uses_explicit_x_values()
    {
        var chart = Chart(ChartType.Scatter, c =>
        {
            c.ShowLinearTrendline = true;
            c.TrendlineType = ChartTrendlineType.Linear;
        });
        var series = ScatterSeries(0, "S1", [10, 20, 30], 5, 10, 15);
        var layout = ChartLayoutEngine.Layout(Request(chart, [], [series]));

        var trend = layout.Series[0].Trendline;
        trend.Should().NotBeNull();
        trend!.Points.Should().HaveCount(2);
    }

    [Fact]
    public void Trendline_not_attached_for_unsupported_chart_type()
    {
        var chart = Chart(ChartType.Pie, c => c.ShowLinearTrendline = true);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B"], [Series(0, "S1", 10, 20)]));
        layout.Series.Should().OnlyContain(s => s.Trendline == null);
    }

    // ---- F7: Bar (horizontal) trendline overlay ---------------------------------------------

    [Fact]
    public void F7_Bar_chart_with_ShowLinearTrendline_attaches_a_trendline()
    {
        // Regression for F7: before the fix, LayoutBar never called AttachTrendline (unlike
        // LayoutColumnLineArea), so a horizontal Bar chart with ShowLinearTrendline=true produced
        // no Trendline overlay at all, even though WPF renders one (swapTrendlineAxes: true).
        var chart = Chart(ChartType.Bar, c =>
        {
            c.ShowLinearTrendline = true;
            c.TrendlineType = ChartTrendlineType.Linear;
        });
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B", "C"], [Series(0, "S1", 10, 20, 30)]));

        var trend = layout.Series[0].Trendline;
        trend.Should().NotBeNull("Bar charts must honor ShowLinearTrendline just like WPF does");
        trend!.Fit.Should().Be(TrendlineFitKind.Linear);
        trend.Points.Should().HaveCount(2);
    }

    [Fact]
    public void F7_Bar_chart_trendline_is_mapped_into_the_horizontal_bar_geometry()
    {
        // The category axis for Bar is vertical (Left) and the value axis is horizontal (Bottom) —
        // the mirror image of Column/Line. The trendline's pixel points must be mapped through the
        // SAME swapped axes as the bars themselves, not the Column/Line convention.
        var plot = new PlotRect(0, 0, 300, 200);
        var chart = Chart(ChartType.Bar, c =>
        {
            c.ShowLinearTrendline = true;
            c.TrendlineType = ChartTrendlineType.Linear;
        });
        var request = Request(chart, ["A", "B", "C"], [Series(0, "S1", 10, 20, 30)], plot);
        var layout = ChartLayoutEngine.Layout(request);

        var trend = layout.Series[0].Trendline!;
        var categoryScale = layout.CategoryAxis!.Scale;
        var valueScale = layout.ValueAxis!.Scale;

        // Trend point 0 is category index 0 (value 10); trend point 1 is category index 2 (value 30).
        trend.Points[0].Y.Should().BeApproximately(categoryScale.Transform(0), 1e-6);
        trend.Points[0].X.Should().BeApproximately(valueScale.Transform(10), 1e-6);
        trend.Points[^1].Y.Should().BeApproximately(categoryScale.Transform(2), 1e-6);
        trend.Points[^1].X.Should().BeApproximately(valueScale.Transform(30), 1e-6);
    }

    [Fact]
    public void F7_StackedBar_does_not_attach_a_trendline()
    {
        // Mirrors WPF: SupportsTrendlines(ChartType) excludes StackedBar/PercentStackedBar/ThreeDBar —
        // only plain Bar honors ShowLinearTrendline. This guards against over-broadening the F7 fix.
        var chart = Chart(ChartType.StackedBar, c => c.ShowLinearTrendline = true);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B"],
            [Series(0, "S1", 10, 20), Series(1, "S2", 5, 8)]));

        layout.Series.Should().OnlyContain(s => s.Trendline == null);
    }

    // ---- F18: trendline equation / R-squared annotation --------------------------------------

    [Fact]
    public void F18_No_annotation_lines_when_neither_equation_nor_rsquared_requested()
    {
        var chart = Chart(ChartType.Line, c =>
        {
            c.ShowLinearTrendline = true;
            c.TrendlineType = ChartTrendlineType.Linear;
        });
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B", "C"], [Series(0, "S1", 1, 2, 3)]));

        layout.Series[0].Trendline!.AnnotationLines.Should().BeEmpty();
    }

    [Fact]
    public void F18_ShowTrendlineEquation_produces_an_equation_annotation_line()
    {
        // Regression for F18: before the fix, TrendlineLayout carried no annotation text at all, so
        // neither host could draw the equation Excel shows when "Display Equation on chart" is set.
        var chart = Chart(ChartType.Line, c =>
        {
            c.ShowLinearTrendline = true;
            c.TrendlineType = ChartTrendlineType.Linear;
            c.ShowTrendlineEquation = true;
        });
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B", "C", "D"],
            [Series(0, "S1", 1, 2, 3, 4)]));

        var trend = layout.Series[0].Trendline!;
        trend.AnnotationLines.Should().ContainSingle();
        trend.AnnotationLines[0].Should().StartWith("y = ", "linear equation text mirrors the WPF format");
    }

    [Fact]
    public void F18_ShowTrendlineRSquared_produces_an_rsquared_annotation_line()
    {
        var chart = Chart(ChartType.Line, c =>
        {
            c.ShowLinearTrendline = true;
            c.TrendlineType = ChartTrendlineType.Linear;
            c.ShowTrendlineRSquared = true;
        });
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B", "C", "D"],
            [Series(0, "S1", 1, 2, 3, 4)]));

        var trend = layout.Series[0].Trendline!;
        trend.AnnotationLines.Should().ContainSingle();
        trend.AnnotationLines[0].Should().StartWith("R² = ");
    }

    [Fact]
    public void F18_Both_equation_and_rsquared_produce_two_annotation_lines_in_order()
    {
        var chart = Chart(ChartType.Line, c =>
        {
            c.ShowLinearTrendline = true;
            c.TrendlineType = ChartTrendlineType.Linear;
            c.ShowTrendlineEquation = true;
            c.ShowTrendlineRSquared = true;
        });
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B", "C", "D"],
            [Series(0, "S1", 1, 2, 3, 4)]));

        var trend = layout.Series[0].Trendline!;
        trend.AnnotationLines.Should().HaveCount(2);
        trend.AnnotationLines[0].Should().StartWith("y = ");
        trend.AnnotationLines[1].Should().StartWith("R² = ");
    }

    [Fact]
    public void F18_Bar_chart_trendline_annotation_anchor_uses_the_swapped_bar_axes()
    {
        // The annotation anchor for a Bar chart must be mapped through the same swapped axes as the
        // trendline polyline (valueScale → X, categoryScale → Y), matching the source (WPF)
        // renderer's AddTrendlineIfRequested, which swaps each source point to (Y, X) before taking
        // (Min(X), Max(Y)) when swapTrendlineAxes is set for ChartType.Bar -- i.e. the true anchor is
        // (min VALUE, max INDEX), not (min index, max value). See G33 regression fix.
        var plot = new PlotRect(0, 0, 300, 200);
        var chart = Chart(ChartType.Bar, c =>
        {
            c.ShowLinearTrendline = true;
            c.TrendlineType = ChartTrendlineType.Linear;
            c.ShowTrendlineEquation = true;
        });
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B", "C"], [Series(0, "S1", 10, 20, 30)], plot));

        var trend = layout.Series[0].Trendline!;
        trend.AnnotationLines.Should().ContainSingle();

        var categoryScale = layout.CategoryAxis!.Scale;
        var valueScale = layout.ValueAxis!.Scale;
        // Source anchor is (min value = 10, max category index = 2), matching WPF.
        trend.AnnotationAnchor.Y.Should().BeApproximately(categoryScale.Transform(2), 1e-6);
        trend.AnnotationAnchor.X.Should().BeApproximately(valueScale.Transform(10), 1e-6);
    }

    // ---- Combo line/scatter overlay (F6) ---------------------------------------------------

    [Fact]
    public void Combo_line_series_index_lays_out_as_a_line_not_a_column()
    {
        // F6: a real Excel combo chart (bar+line) must draw the designated series as a LINE
        // overlay, not another set of columns. Series 1 is marked as the combo line series.
        var chart = Chart(ChartType.Column, c =>
        {
            c.UseComboLineForSecondarySeries = true;
            c.ComboLineSeriesIndexes = [1];
        });
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B"],
            [Series(0, "Bars", 10, 20), Series(1, "Line", 5, 8)]));

        layout.Series.Should().HaveCount(2);
        layout.Series[0].Kind.Should().Be(SeriesGeometryKind.Columns, "series 0 is not promoted to combo line");
        layout.Series[0].Bars.Should().HaveCount(2);

        layout.Series[1].Kind.Should().Be(SeriesGeometryKind.Line, "series 1 is designated as the combo line overlay");
        layout.Series[1].Points.Should().HaveCount(2);
        layout.Series[1].Bars.Should().BeEmpty("the combo line series must not also produce column bars");
    }

    [Fact]
    public void Combo_line_series_is_excluded_from_the_clustered_column_slot_count()
    {
        // The combo line series must not consume a clustered sub-slot: with 1 bar series + 1 combo
        // line series, the bar series should fill the FULL category slot (as if it were alone),
        // not a half-slot as it would if the line series were still counted as clustered.
        var plot = new PlotRect(0, 0, 300, 200);
        var chart = Chart(ChartType.Column, c =>
        {
            c.UseComboLineForSecondarySeries = true;
            c.ComboLineSeriesIndexes = [1];
        });
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B"],
            [Series(0, "Bars", 10, 20), Series(1, "Line", 5, 8)], plot));

        var catScale = layout.CategoryAxis!.Scale;
        var bar = layout.Series[0].Bars[0].Rect;
        // Full native half-width (gapWidth=219), same as a lone clustered series — not narrowed to a 2-series
        // sub-slot.
        bar.Left.Should().BeApproximately(catScale.Transform(-0.15673981191222572), 1e-6);
        bar.Right.Should().BeApproximately(catScale.Transform(0.15673981191222572), 1e-6);
    }

    [Fact]
    public void Combo_scatter_series_index_lays_out_as_scatter_points()
    {
        var chart = Chart(ChartType.Column, c => c.ComboScatterSeriesIndexes = [1]);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B"],
            [Series(0, "Bars", 10, 20), Series(1, "Scatter", 5, 8)]));

        layout.Series[1].Kind.Should().Be(SeriesGeometryKind.ScatterPoints);
        layout.Series[1].Points.Should().HaveCount(2);
    }

    [Fact]
    public void No_combo_line_series_by_default_all_series_render_as_columns()
    {
        // Regression: an empty ComboLineSeriesIndexes list (the default) must not affect a plain
        // clustered column chart — every series still renders as columns.
        var chart = Chart(ChartType.Column);
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B"],
            [Series(0, "S1", 10, 20), Series(1, "S2", 5, 8)]));

        layout.Series.Should().OnlyContain(s => s.Kind == SeriesGeometryKind.Columns);
    }

    [Fact]
    public void Combo_line_series_in_a_stacked_column_chart_overlays_instead_of_stacking()
    {
        // SupportsComboLineOverlay also covers StackedColumn/PercentStackedColumn: the combo line
        // series must be drawn as a line over the stack, not folded into the running stack totals.
        var chart = Chart(ChartType.StackedColumn, c =>
        {
            c.UseComboLineForSecondarySeries = true;
            c.ComboLineSeriesIndexes = [1];
        });
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B"],
            [Series(0, "Stack", 10, 20), Series(1, "Line", 5, 8)]));

        layout.Series[0].Kind.Should().Be(SeriesGeometryKind.Columns);
        layout.Series[1].Kind.Should().Be(SeriesGeometryKind.Line);
    }
}
