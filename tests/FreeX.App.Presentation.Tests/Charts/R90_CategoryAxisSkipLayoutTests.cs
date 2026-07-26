using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;
using static FreeX.App.Presentation.Tests.Charts.ChartLayoutTestData;

namespace FreeX.App.Presentation.Tests.Charts;

/// <summary>
/// R90-render-chart-axis-titles-5-2: Excel's Format Axis &gt; Labels "Interval between labels"
/// (<c>&lt;c:tickLblSkip&gt;</c>) and "Interval between tick marks" (<c>&lt;c:tickMarkSkip&gt;</c>) were
/// round-tripped correctly through <see cref="ChartModel.XAxisLabelSkip"/>/
/// <see cref="ChartModel.XAxisTickMarkSkip"/> (XlsxChartAxisReader reads them, XlsxChartXmlWriter.Axes
/// writes them) but no renderer consulted them, so a chart authored with "every 3rd label" drew all of
/// them. The portable layout engine -- which feeds both the cross-platform shell renderer and the PDF
/// exporter -- now thins category labels and tick marks accordingly.
/// </summary>
public sealed class R90_CategoryAxisSkipLayoutTests
{
    private static readonly string[] SixCategories = ["C0", "C1", "C2", "C3", "C4", "C5"];

    private static ChartLayout LayoutWithSkips(ChartType type, int labelSkip, int tickMarkSkip) =>
        ChartLayoutEngine.Layout(Request(
            Chart(type, c =>
            {
                c.XAxisLabelSkip = labelSkip;
                c.XAxisTickMarkSkip = tickMarkSkip;
            }),
            SixCategories,
            [Series(0, "S1", 10, 20, 30, 40, 50, 60)]));

    [Fact]
    public void Category_axis_keeps_only_every_nth_label_when_label_skip_is_set()
    {
        var layout = LayoutWithSkips(ChartType.Column, labelSkip: 3, tickMarkSkip: 0);

        // Every category still produces a tick (so gridlines and the axis extent are unchanged) --
        // only the labels are thinned, anchored on the first category the way Excel anchors them.
        layout.CategoryAxis!.Ticks.Should().HaveCount(6);
        layout.CategoryAxis.Ticks.Select(t => t.Label).Should().Equal("C0", "", "", "C3", "", "");
    }

    [Fact]
    public void Category_axis_draws_only_every_nth_tick_mark_when_tick_mark_skip_is_set()
    {
        var layout = LayoutWithSkips(ChartType.Column, labelSkip: 0, tickMarkSkip: 2);

        layout.CategoryAxis!.Ticks.Select(t => t.DrawTickMark)
            .Should().Equal(true, false, true, false, true, false);
        // Tick-mark thinning must not thin the labels.
        layout.CategoryAxis.Ticks.Select(t => t.Label).Should().Equal(SixCategories);
    }

    [Fact]
    public void Label_and_tick_mark_skips_are_applied_independently()
    {
        var layout = LayoutWithSkips(ChartType.Column, labelSkip: 3, tickMarkSkip: 2);

        layout.CategoryAxis!.Ticks.Select(t => t.Label).Should().Equal("C0", "", "", "C3", "", "");
        layout.CategoryAxis.Ticks.Select(t => t.DrawTickMark)
            .Should().Equal(true, false, true, false, true, false);
    }

    [Fact]
    public void Bar_family_category_axis_on_the_left_honors_the_same_x_model_fields()
    {
        // XlsxChartAxisReader always stores the category axis's skips on the X* fields, even when the
        // category axis is the vertical one, so the bar family must read them from there too.
        var layout = LayoutWithSkips(ChartType.Bar, labelSkip: 2, tickMarkSkip: 0);

        layout.CategoryAxis!.Side.Should().Be(AxisSide.Left);
        layout.CategoryAxis.Ticks.Select(t => t.Label).Should().Equal("C0", "", "C2", "", "C4", "");
    }

    // ---- No-regression siblings: the default and the CT_Skip "1" spelling both draw everything ----

    [Fact]
    public void Category_axis_draws_every_label_and_tick_mark_by_default()
    {
        var layout = LayoutWithSkips(ChartType.Column, labelSkip: 0, tickMarkSkip: 0);

        layout.CategoryAxis!.Ticks.Select(t => t.Label).Should().Equal(SixCategories);
        layout.CategoryAxis.Ticks.Should().OnlyContain(t => t.DrawTickMark);
    }

    [Fact]
    public void Skip_of_one_is_excels_default_interval_and_thins_nothing()
    {
        // ECMA-376 CT_Skip defaults to 1, and Excel writes val="1" for "show every label", so 1 must
        // behave exactly like the unspecified 0 the model stores when the element is absent.
        var layout = LayoutWithSkips(ChartType.Column, labelSkip: 1, tickMarkSkip: 1);

        layout.CategoryAxis!.Ticks.Select(t => t.Label).Should().Equal(SixCategories);
        layout.CategoryAxis.Ticks.Should().OnlyContain(t => t.DrawTickMark);
    }
}
