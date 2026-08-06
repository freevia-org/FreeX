using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class ChartOptionsDialogPlanTests
{
    [Fact]
    public void BubblePlanOwnsFieldOrderDefaultsAndAccessibilityMetadata()
    {
        var session = new ChartBubbleOptionsDialogSession(CreateEditor(CreateBubbleChart()));

        var plan = session.BuildDialogPlan();

        plan.CommandId.Should().Be(ChartBubbleOptionsPlanner.CommandId);
        plan.Groups.SelectMany(group => group.Fields).Select(field => field.Id).Should().Equal(
            ChartOptionsDialogFieldId.BubbleScale,
            ChartOptionsDialogFieldId.BubbleSizeRepresents,
            ChartOptionsDialogFieldId.ShowNegativeBubbles);
        plan.Field(ChartOptionsDialogFieldId.BubbleScale).Text.Should().Be("125");
        plan.Field(ChartOptionsDialogFieldId.BubbleSizeRepresents).ChoiceLabels.Should().Equal("Area", "Width");
        plan.Field(ChartOptionsDialogFieldId.ShowNegativeBubbles).IsStandalone.Should().BeTrue();
        plan.Fields.Values.Should().OnlyContain(field =>
            !string.IsNullOrWhiteSpace(field.AccessibleName)
            && field.AutomationId.StartsWith("FreeP.ChartOptions.", StringComparison.Ordinal));
    }

    [Fact]
    public void PortableValuesConstructTheExistingTypedInput()
    {
        var session = new ChartBubbleOptionsDialogSession(CreateEditor(CreateBubbleChart()));
        var values = new ChartOptionsDialogValues(
            new Dictionary<ChartOptionsDialogFieldId, ChartOptionsDialogFieldValue>
            {
                [ChartOptionsDialogFieldId.BubbleScale] = new(Text: "225"),
                [ChartOptionsDialogFieldId.BubbleSizeRepresents] = new(SelectedIndex: 1),
                [ChartOptionsDialogFieldId.ShowNegativeBubbles] = new(IsChecked: true),
            });

        var input = session.BuildInput(values);
        var result = session.BuildCommitPlan(input);

        result.Should().Be(new ChartBubbleOptions(225, BubbleSizeRepresentation.Width, true));
    }

    [Fact]
    public void PlanRejectsDuplicateStableFieldIds()
    {
        var field = new ChartOptionsDialogFieldPlan(
            ChartOptionsDialogFieldId.FontFamily,
            ChartOptionsDialogControlKind.Text,
            "Font family",
            "Font family",
            "FreeP.ChartOptions.FontFamily");
        var groups = new[]
        {
            new ChartOptionsDialogGroupPlan("one", null, "One", [field]),
            new ChartOptionsDialogGroupPlan("two", null, "Two", [field]),
        };

        var action = () => new ChartOptionsDialogPlan(
            "freep.test",
            "Test",
            400,
            300,
            300,
            200,
            false,
            false,
            null,
            "OK",
            "Cancel",
            groups);

        action.Should().Throw<ArgumentException>().WithMessage("*Duplicate chart dialog field*");
    }

    [Fact]
    public void DataTablePlanPreservesInheritedThreeStateTypography()
    {
        var session = new ChartDataTableOptionsDialogSession(CreateEditor(new ChartShape()));

        var plan = session.BuildDialogPlan();

        plan.IsScrollable.Should().BeTrue();
        plan.Field(ChartOptionsDialogFieldId.Bold).IsThreeState.Should().BeTrue();
        plan.Field(ChartOptionsDialogFieldId.Bold).IsChecked.Should().BeNull();
        plan.Field(ChartOptionsDialogFieldId.Italic).IsThreeState.Should().BeTrue();
        plan.Groups.Select(group => group.Id).Should().Equal("table", "appearance", "table-text");
    }

    [Fact]
    public void AreaTargetTransitionRebuildsPortableDefaults()
    {
        var session = new ChartAreaOptionsDialogSession(CreateEditor(new ChartShape()));

        session.BuildDialogPlan().Field(ChartOptionsDialogFieldId.AreaTarget).SelectedIndex.Should().Be(0);
        session.SelectTarget(1);
        var plotAreaPlan = session.BuildDialogPlan();

        plotAreaPlan.Field(ChartOptionsDialogFieldId.AreaTarget).SelectedIndex.Should().Be(1);
        plotAreaPlan.Groups.Select(group => group.Id).Should().Equal("target", "fill", "outline");
    }

    [Theory]
    [InlineData(ChartType.Pie, false)]
    [InlineData(ChartType.OfPie, true)]
    public void PiePlanOwnsConditionalSecondaryPlotFields(ChartType chartType, bool hasSecondaryFields)
    {
        var chart = new ChartShape { ChartType = chartType };
        var session = new ChartPieOptionsDialogSession(CreateEditor(chart));

        var plan = session.BuildDialogPlan();

        plan.Fields.ContainsKey(ChartOptionsDialogFieldId.OfPieType).Should().Be(hasSecondaryFields);
        plan.IsScrollable.Should().Be(hasSecondaryFields);
        plan.Height.Should().Be(hasSecondaryFields ? ChartPieOptionsPlanner.DefaultDialogHeight : 250);
    }

    [Fact]
    public void AxisPlanOwnsSectionsTriStateDefaultsAndAccessibilityMetadata()
    {
        var session = new ChartAxisOptionsDialogSession(CreateEditor(new ChartShape()));

        var plan = session.BuildDialogPlan();

        plan.IsScrollable.Should().BeTrue();
        plan.Groups.Select(group => group.Id).Should().Equal(
            "axis",
            "axis-title",
            "axis-scale",
            "axis-gridlines",
            "axis-labels");
        plan.Field(ChartOptionsDialogFieldId.AxisTitleBold).IsThreeState.Should().BeTrue();
        plan.Field(ChartOptionsDialogFieldId.AxisTitleBold).IsChecked.Should().BeNull();
        plan.Fields.Values.Should().OnlyContain(field =>
            !string.IsNullOrWhiteSpace(field.AccessibleName)
            && field.AutomationId.StartsWith("FreeP.ChartOptions.", StringComparison.Ordinal));
    }

    [Fact]
    public void DisplayPlanOwnsChartSubtypeEnablement()
    {
        var session = new ChartDisplayOptionsDialogSession(CreateEditor(new ChartShape
        {
            ChartType = ChartType.Waterfall,
        }));

        var plan = session.BuildDialogPlan();

        plan.Field(ChartOptionsDialogFieldId.WaterfallConnectorLines).IsEnabled.Should().BeTrue();
        plan.Field(ChartOptionsDialogFieldId.HighLowLines).IsEnabled.Should().BeFalse();
        plan.Field(ChartOptionsDialogFieldId.UpDownBars).IsEnabled.Should().BeFalse();
        plan.Groups.Select(group => group.Id).Should().ContainInOrder(
            "chart-title",
            "chart-display",
            "data-label-content",
            "data-label-style",
            "plot");
    }

    [Fact]
    public void PointSelectionTransitionRebuildsChoicesAndPortableInput()
    {
        var session = new ChartPointOptionsDialogSession(CreateEditor(CreateSelectionChart()));

        session.BuildDialogPlan().Field(ChartOptionsDialogFieldId.Point).ChoiceLabels.Should().HaveCount(1);
        session.SelectSeries(1);
        var plan = session.BuildDialogPlan();
        var input = session.BuildInput(ValuesFromPlan(plan));

        plan.Field(ChartOptionsDialogFieldId.Point).ChoiceLabels.Should().HaveCount(3);
        input.SeriesIndex.Should().Be(1);
        input.PointIndex.Should().Be(0);
        input.LabelBold.Should().BeNull();
    }

    [Fact]
    public void SeriesPlanAndPortableInputPreserveInheritedTriStateValues()
    {
        var chart = CreateSelectionChart();
        chart.Series[1].InvertIfNegative = null;
        var session = new ChartSeriesOptionsDialogSession(CreateEditor(chart), initialSeriesIndex: 1);

        var plan = session.BuildDialogPlan();
        var input = session.BuildInput(ValuesFromPlan(plan));

        plan.Groups.Select(group => group.Id).Should().Equal(
            "series-selection",
            "series-appearance",
            "series-label-content",
            "series-label-style",
            "series-error-bars",
            "series-trendline");
        plan.Field(ChartOptionsDialogFieldId.InvertIfNegative).IsThreeState.Should().BeTrue();
        input.InvertIfNegative.Should().BeNull();
    }

    private static ChartShape CreateBubbleChart() => new()
    {
        ChartType = ChartType.Bubble,
        BubbleScalePercent = 125,
        BubbleSizeRepresents = BubbleSizeRepresentation.Area,
        ShowNegativeBubbles = false,
    };

    private static ChartShape CreateSelectionChart()
    {
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.AddRange(["Q1", "Q2", "Q3"]);
        var revenue = new ChartSeries { Name = "Revenue" };
        revenue.Values.Add(10);
        var margin = new ChartSeries { Name = "Margin" };
        margin.Values.AddRange([1.0, 2.0, 3.0]);
        chart.Series.Add(revenue);
        chart.Series.Add(margin);
        return chart;
    }

    private static ChartOptionsDialogValues ValuesFromPlan(ChartOptionsDialogPlan plan) => new(
        plan.Fields.ToDictionary(
            pair => pair.Key,
            pair => new ChartOptionsDialogFieldValue(
                pair.Value.Text,
                pair.Value.SelectedIndex,
                pair.Value.IsChecked)));

    private static EditingSession CreateEditor(ChartShape chart)
    {
        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 42,
            Name = "Chart",
            Kind = SlideShapeKind.Chart,
            Chart = chart,
        });
        presentation.Slides.Add(slide);
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        editor.Select(42);
        return editor;
    }
}
