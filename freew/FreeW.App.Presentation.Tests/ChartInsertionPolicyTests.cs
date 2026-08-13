using System.Globalization;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Editing;

namespace FreeW.App.Presentation.Tests;

public sealed class ChartInsertionPolicyTests
{
    [Fact]
    public void SuppliedChartRemainsTheInsertionModel()
    {
        var supplied = Chart.Create(
            ChartKind.Line,
            ["Jan", "Feb"],
            [3d, 5d],
            "Revenue",
            "Monthly revenue");

        DocumentObjectEditingCoordinator.PlanChartInsertion(supplied)
            .Should().BeSameAs(supplied);
    }

    [Fact]
    public void EmptyPlaceholderChartIsNotReplacedByTheDefaultPreset()
    {
        var placeholder = new Chart
        {
            WidthPt = 240d,
            HeightPt = 144d,
        };

        var planned = DocumentObjectEditingCoordinator.PlanChartInsertion(placeholder);

        planned.Should().BeSameAs(placeholder);
        planned.Categories.Should().BeEmpty();
        planned.Series.Should().BeEmpty();
        planned.WidthPt.Should().Be(240d);
        planned.HeightPt.Should().Be(144d);
    }

    [Fact]
    public void DefaultInsertionUsesThePortableDialogPresetAndModelSizing()
    {
        var chart = DocumentObjectEditingCoordinator.PlanChartInsertion();

        chart.Kind.Should().Be(ChartKind.Column);
        chart.Title.Should().Be(InsertChartDialogPlanner.DefaultTitle);
        chart.Categories.Should().Equal("Q1", "Q2", "Q3", "Q4");
        chart.Series.Should().ContainSingle();
        chart.Series[0].Name.Should().Be(InsertChartDialogPlanner.DefaultSeriesName);
        chart.Series[0].Values.Should().Equal(8d, 5d, 11d, 7d);
        chart.WidthPt.Should().Be(360d);
        chart.HeightPt.Should().Be(216d);
        chart.StyleId.Should().Be(0);
        chart.ColorSchemeId.Should().BeNull();
        chart.QuickLayoutId.Should().Be(0);
    }

    [Fact]
    public void DefaultInsertionMaterializesIndependentMutableCharts()
    {
        var first = DocumentObjectEditingCoordinator.PlanChartInsertion();
        var second = DocumentObjectEditingCoordinator.PlanChartInsertion();

        first.Should().NotBeSameAs(second);
        first.Series[0].Should().NotBeSameAs(second.Series[0]);

        first.Categories[0] = "Changed";
        first.Series[0].Values[0] = 99d;

        second.Categories[0].Should().Be("Q1");
        second.Series[0].Values[0].Should().Be(8d);
    }

    [Fact]
    public void DefaultInsertionMatchesTheSharedDialogState()
    {
        var state = InsertChartDialogPlanner.BuildInitialState(
            null,
            CultureInfo.InvariantCulture);
        var chart = DocumentObjectEditingCoordinator.PlanChartInsertion();

        chart.Kind.Should().Be(state.Kind);
        chart.Title.Should().Be(state.Title);
        chart.Categories.Should().Equal(state.Rows.Select(row => row.Category));
        chart.Series.Select(series => series.Name)
            .Should().Equal(state.SeriesNames);
        chart.Series[0].Values.Select(value => value.ToString("G", CultureInfo.InvariantCulture))
            .Should().Equal(state.Rows.Select(row => row.SeriesValues[0]));
    }
}
