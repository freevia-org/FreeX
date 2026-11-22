using FluentAssertions;
using FreeX.App.Presentation.Charts.Selection;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Charts;

public sealed class ChartFormatPanePlannerTests
{
    private static ChartModel Column() => new() { Type = ChartType.Column, Title = "Sales", XAxisTitle = "Quarter", YAxisTitle = "Revenue" };

    [Fact]
    public void Title_pane_offers_text_font_size_and_reset_position()
    {
        var pane = ChartFormatPanePlanner.Build(Column(), new ChartElementRef(ChartElementKind.ChartTitle));

        pane.Rows.Select(r => r.Id).Should().Equal(
            ChartFormatPanePlanner.RowText, ChartFormatPanePlanner.RowFontSize, ChartFormatPanePlanner.RowResetPosition);
        pane.Rows[0].Value.Should().Be("Sales");
        pane.MoreOptionsTarget.Should().Be(ChartElementFormatTarget.ChartTitles);
    }

    [Fact]
    public void Editing_title_text_plans_a_title_change()
    {
        var options = ChartFormatPanePlanner.Plan(Column(), new ChartElementRef(ChartElementKind.ChartTitle), ChartFormatPanePlanner.RowText, "Q4");

        options!.Title.Should().Be("Q4");
    }

    [Fact]
    public void Reset_position_clears_only_that_elements_manual_layout()
    {
        var chart = Column();

        ChartFormatPanePlanner.Plan(chart, new ChartElementRef(ChartElementKind.Legend), ChartFormatPanePlanner.RowResetPosition, "")!
            .ClearManualLayouts.Should().Be(ChartManualLayoutTargets.Legend);
        ChartFormatPanePlanner.Plan(chart, new ChartElementRef(ChartElementKind.CategoryAxisTitle), ChartFormatPanePlanner.RowResetPosition, "")!
            .ClearManualLayouts.Should().Be(ChartManualLayoutTargets.XAxisTitle);
        ChartFormatPanePlanner.Plan(chart, new ChartElementRef(ChartElementKind.ValueAxisTitle), ChartFormatPanePlanner.RowResetPosition, "")!
            .ClearManualLayouts.Should().Be(ChartManualLayoutTargets.YAxisTitle);
    }

    [Fact]
    public void Legend_position_choice_applies_and_none_is_rejected()
    {
        var legend = new ChartElementRef(ChartElementKind.Legend);

        ChartFormatPanePlanner.Plan(Column(), legend, ChartFormatPanePlanner.RowLegendPosition, "Bottom")!
            .LegendPosition.Should().Be(ChartLegendPosition.Bottom);
        ChartFormatPanePlanner.Plan(Column(), legend, ChartFormatPanePlanner.RowLegendPosition, "None").Should().BeNull();
    }

    [Fact]
    public void Axis_pane_hides_bounds_for_category_axes_and_routes_x_y_by_orientation()
    {
        var column = Column();
        var category = ChartFormatPanePlanner.Build(column, new ChartElementRef(ChartElementKind.CategoryAxis));
        var value = ChartFormatPanePlanner.Build(column, new ChartElementRef(ChartElementKind.ValueAxis));

        category.Rows.Select(r => r.Id).Should().NotContain(ChartFormatPanePlanner.RowMinimum);
        value.Rows.Select(r => r.Id).Should().Contain(ChartFormatPanePlanner.RowMinimum);

        ChartFormatPanePlanner.Plan(column, new ChartElementRef(ChartElementKind.ValueAxis), ChartFormatPanePlanner.RowMinimum, "5")!
            .YAxisMinimum.Should().Be(5);
        ChartFormatPanePlanner.Plan(column, new ChartElementRef(ChartElementKind.CategoryAxis), ChartFormatPanePlanner.RowShowLabels, "false")!
            .ShowXAxisLabels.Should().BeFalse();
    }

    [Fact]
    public void Invalid_numbers_are_rejected()
    {
        var value = new ChartElementRef(ChartElementKind.ValueAxis);

        ChartFormatPanePlanner.Plan(Column(), value, ChartFormatPanePlanner.RowMinimum, "abc").Should().BeNull();
        ChartFormatPanePlanner.Plan(Column(), new ChartElementRef(ChartElementKind.ChartTitle), ChartFormatPanePlanner.RowFontSize, "500").Should().BeNull();
    }

    [Fact]
    public void Data_label_pane_offers_percentage_only_for_pie()
    {
        var column = ChartFormatPanePlanner.Build(Column(), ChartElementRef.ForDataLabels(0));
        var pie = ChartFormatPanePlanner.Build(new ChartModel { Type = ChartType.Pie }, ChartElementRef.ForDataLabels(0));

        column.Rows.Select(r => r.Id).Should().NotContain(ChartFormatPanePlanner.RowShowPercentage);
        pie.Rows.Select(r => r.Id).Should().Contain(ChartFormatPanePlanner.RowShowPercentage);
    }

    [Fact]
    public void Every_element_builds_a_pane_and_row_labels_have_resource_keys()
    {
        foreach (var kind in Enum.GetValues<ChartElementKind>())
        {
            var pane = ChartFormatPanePlanner.Build(Column(), new ChartElementRef(kind, 0));
            pane.ElementName.Should().NotBeNull();
            pane.Rows.Should().OnlyContain(r => r.LabelKey.StartsWith("ChartFormatPane_"));
            foreach (var row in pane.Rows.Where(r => r.Kind != ChartFormatRowKind.Button))
            {
                // Blank number rows (auto bounds) have nothing to apply; everything else must plan a change.
                if (row.Kind == ChartFormatRowKind.Number && row.Value.Length == 0)
                    continue;
                ChartFormatPanePlanner.Plan(Column(), new ChartElementRef(kind, 0), row.Id, row.Value)
                    .Should().NotBeNull($"{kind}/{row.Id}");
            }
        }
    }
}
