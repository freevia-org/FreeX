using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;
using static FreeX.App.Presentation.Tests.Charts.ChartLayoutTestData;

namespace FreeX.App.Presentation.Tests.Charts;

/// <summary>
/// R88-render-chart-labels-legend-5-4: a deleted &lt;c:legendEntry&gt; on a pie/doughnut chart is keyed
/// to the POINT index (there is only one plotted series), unlike a series chart's legend where the
/// idx is a series-plot-order index. LegendLayoutBuilder.CollectLabels' pie branch previously ignored
/// chart.LegendEntries entirely, so a deleted slice's legend row always reappeared.
/// </summary>
public sealed class R88_LegendLayoutBuilderPieDeleteTests
{
    [Fact]
    public void Pie_legend_hides_the_deleted_point_entry()
    {
        var request = Request(Chart(ChartType.Pie, c =>
        {
            c.ShowLegend = true;
            c.LegendPosition = ChartLegendPosition.Right;
            c.LegendEntries.Add(new ChartLegendEntryModel(Index: 1, IsDeleted: true));
        }), ["North", "South", "East"], [Series(0, "Region", 1, 2, 3)]);

        var layout = ChartLayoutEngine.Layout(request);

        layout.Legend.Entries.Select(e => e.Label).Should().Equal("North", "East");
    }

    [Fact]
    public void Doughnut_legend_hides_the_deleted_point_entry()
    {
        var request = Request(Chart(ChartType.Doughnut, c =>
        {
            c.ShowLegend = true;
            c.LegendPosition = ChartLegendPosition.Right;
            c.LegendEntries.Add(new ChartLegendEntryModel(Index: 0, IsDeleted: true));
        }), ["North", "South", "East"], [Series(0, "Region", 1, 2, 3)]);

        var layout = ChartLayoutEngine.Layout(request);

        layout.Legend.Entries.Select(e => e.Label).Should().Equal("South", "East");
    }

    // No-regression sibling: a legend entry that carries ONLY text formatting (no <c:delete>) must
    // NOT hide the point, matching the R45-io-chart-datatable-legend-3-1 round-trip contract that
    // ChartLegendEntryModel's own doc comment already documents for the series-legend path.
    [Fact]
    public void Pie_legend_keeps_entries_with_only_text_formatting()
    {
        var request = Request(Chart(ChartType.Pie, c =>
        {
            c.ShowLegend = true;
            c.LegendPosition = ChartLegendPosition.Right;
            c.LegendEntries.Add(new ChartLegendEntryModel(Index: 1, IsDeleted: null, TextBold: true));
        }), ["North", "South", "East"], [Series(0, "Region", 1, 2, 3)]);

        var layout = ChartLayoutEngine.Layout(request);

        layout.Legend.Entries.Select(e => e.Label).Should().Equal("North", "South", "East");
    }

    // No-regression sibling: with no LegendEntries at all, every category still lists (the pre-existing
    // behavior LegendAndDataLabelLayoutTests.Pie_legend_lists_categories_not_series already covers).
    [Fact]
    public void Pie_legend_lists_every_category_when_nothing_is_deleted()
    {
        var request = Request(Chart(ChartType.Pie, c =>
        {
            c.ShowLegend = true;
            c.LegendPosition = ChartLegendPosition.Right;
        }), ["North", "South", "East"], [Series(0, "Region", 1, 2, 3)]);

        var layout = ChartLayoutEngine.Layout(request);

        layout.Legend.Entries.Select(e => e.Label).Should().Equal("North", "South", "East");
    }
}
