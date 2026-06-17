using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;
using static FreeX.App.Presentation.Tests.Charts.ChartLayoutTestData;

namespace FreeX.App.Presentation.Tests.Charts;

public sealed class LegendAndDataLabelLayoutTests
{
    [Fact]
    public void Hidden_legend_leaves_the_plot_rectangle_untouched()
    {
        var request = Request(Chart(ChartType.Column, c => c.ShowLegend = false),
            ["A"], [Series(0, "S1", 10)]);
        var layout = ChartLayoutEngine.Layout(request);

        layout.Legend.Position.Should().Be(ChartLegendPosition.None);
        layout.PlotArea.Should().Be(StandardPlot.ToRect());
    }

    [Fact]
    public void Right_legend_reserves_a_gutter_on_the_right()
    {
        var request = Request(Chart(ChartType.Column, c =>
        {
            c.ShowLegend = true;
            c.LegendPosition = ChartLegendPosition.Right;
        }), ["A", "B"], [Series(0, "Series One", 10, 20), Series(1, "Series Two", 30, 40)]);
        var layout = ChartLayoutEngine.Layout(request);

        layout.Legend.Position.Should().Be(ChartLegendPosition.Right);
        layout.Legend.Entries.Should().HaveCount(2);
        // Plot narrows; legend sits to the right of the (narrowed) plot area.
        layout.PlotArea.Width.Should().BeLessThan(StandardPlot.Width);
        layout.Legend.Bounds.Left.Should().BeApproximately(layout.PlotArea.Right, 1e-6);
    }

    [Fact]
    public void Bottom_legend_reserves_a_gutter_at_the_bottom()
    {
        var request = Request(Chart(ChartType.Column, c =>
        {
            c.ShowLegend = true;
            c.LegendPosition = ChartLegendPosition.Bottom;
        }), ["A"], [Series(0, "S1", 10), Series(1, "S2", 20)]);
        var layout = ChartLayoutEngine.Layout(request);

        layout.PlotArea.Height.Should().BeLessThan(StandardPlot.Height);
        layout.Legend.Bounds.Top.Should().BeApproximately(layout.PlotArea.Bottom, 1e-6);
    }

    [Fact]
    public void Overlay_legend_does_not_shrink_the_plot()
    {
        var request = Request(Chart(ChartType.Column, c =>
        {
            c.ShowLegend = true;
            c.LegendPosition = ChartLegendPosition.Right;
            c.LegendOverlay = true;
        }), ["A"], [Series(0, "S1", 10)]);
        var layout = ChartLayoutEngine.Layout(request);

        layout.PlotArea.Width.Should().Be(StandardPlot.Width);
        layout.Legend.Entries.Should().HaveCount(1);
    }

    [Fact]
    public void Legend_entry_swatch_sits_left_of_its_label()
    {
        var request = Request(Chart(ChartType.Column, c =>
        {
            c.ShowLegend = true;
            c.LegendPosition = ChartLegendPosition.Right;
        }), ["A"], [Series(0, "Apples", 10)]);
        var layout = ChartLayoutEngine.Layout(request);

        var entry = layout.Legend.Entries[0];
        entry.Label.Should().Be("Apples");
        entry.SwatchRect.Right.Should().BeLessThanOrEqualTo(entry.LabelRect.Left);
    }

    [Fact]
    public void Wider_labels_produce_a_wider_vertical_legend()
    {
        ChartLayoutRequest Build(string name) => Request(Chart(ChartType.Column, c =>
        {
            c.ShowLegend = true;
            c.LegendPosition = ChartLegendPosition.Right;
        }), ["A"], [Series(0, name, 10)]);

        var narrow = ChartLayoutEngine.Layout(Build("X")).Legend.Bounds.Width;
        var wide = ChartLayoutEngine.Layout(Build("A very long series name")).Legend.Bounds.Width;
        wide.Should().BeGreaterThan(narrow, "the measurer reports a wider label");
    }

    [Fact]
    public void Pie_legend_lists_categories_not_series()
    {
        var request = Request(Chart(ChartType.Pie, c =>
        {
            c.ShowLegend = true;
            c.LegendPosition = ChartLegendPosition.Right;
        }), ["North", "South", "East"], [Series(0, "Region", 1, 2, 3)]);
        var layout = ChartLayoutEngine.Layout(request);

        layout.Legend.Entries.Select(e => e.Label).Should().Equal("North", "South", "East");
    }

    [Fact]
    public void Data_labels_off_by_default()
    {
        var request = Request(Chart(ChartType.Column), ["A", "B"], [Series(0, "S1", 10, 20)]);
        ChartLayoutEngine.Layout(request).DataLabels.Should().BeEmpty();
    }

    [Fact]
    public void Value_data_labels_are_emitted_per_point_and_anchored_at_the_value()
    {
        var request = Request(Chart(ChartType.Column, c =>
        {
            c.ShowDataLabels = true;
            c.ShowDataLabelValue = true;
        }), ["A", "B"], [Series(0, "S1", 10, 20)]);
        var layout = ChartLayoutEngine.Layout(request);

        layout.DataLabels.Should().HaveCount(2);
        layout.DataLabels[0].Text.Should().Be("10");
        // Anchor Y is the value position; label box is centered on the anchor.
        var valueY = layout.ValueAxis!.Scale.Transform(10);
        layout.DataLabels[0].Anchor.Y.Should().BeApproximately(valueY, 1e-6);
        layout.DataLabels[0].Bounds.Center.Y.Should().BeApproximately(layout.DataLabels[0].Anchor.Y, 1e-6);
    }

    [Fact]
    public void Category_name_data_labels_use_the_category_text()
    {
        var request = Request(Chart(ChartType.Column, c =>
        {
            c.ShowDataLabels = true;
            c.ShowDataLabelValue = false;
            c.ShowDataLabelCategoryName = true;
        }), ["Alpha", "Beta"], [Series(0, "S1", 10, 20)]);
        var layout = ChartLayoutEngine.Layout(request);

        layout.DataLabels.Select(d => d.Text).Should().Equal("Alpha", "Beta");
    }

    [Fact]
    public void Pie_percentage_data_labels_format_as_percent()
    {
        var request = Request(Chart(ChartType.Pie, c =>
        {
            c.ShowDataLabels = true;
            c.ShowDataLabelValue = false;
            c.ShowDataLabelPercentage = true;
        }), ["A", "B"], [Series(0, "S1", 25, 75)]);
        var layout = ChartLayoutEngine.Layout(request);

        layout.DataLabels.Select(d => d.Text).Should().Equal("25%", "75%");
    }
}
