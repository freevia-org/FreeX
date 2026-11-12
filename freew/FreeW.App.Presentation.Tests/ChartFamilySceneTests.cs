using System.Globalization;
using Free.Shared.Ribbon;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

/// <summary>
/// Renderer-neutral scenes for stacked / 100% stacked charts and the radar, bubble, stock and
/// combination kinds, plus the chart-type picker and the ribbon grouping commands that select them.
/// </summary>
public sealed class ChartFamilySceneTests
{
    private static Chart TwoSeries(ChartKind kind, ChartGrouping grouping = ChartGrouping.Standard)
    {
        var chart = new Chart { Kind = kind, Grouping = grouping };
        chart.Categories.AddRange(["A", "B", "C"]);
        chart.Series.Add(new ChartSeries("One", [10, 20, 30]));
        chart.Series.Add(new ChartSeries("Two", [30, 20, 10]));
        return chart;
    }

    private static ChartScene Scene(Chart chart) => ChartSmartArtVisualPlanner.BuildChartScene(chart, 360, 216);

    [Fact]
    public void ClusteredColumn_SceneIsUnchangedByTheNewFamilies()
    {
        var scene = Scene(TwoSeries(ChartKind.Column));

        scene.Bars.Should().HaveCount(6);
        scene.ValueAxis.Maximum.Should().Be(30, "clustered series are not summed");
        // Clustered: the two series of a category sit side by side at the same baseline.
        scene.Bars[0].Bounds.Bottom.Should().BeApproximately(scene.Bars[1].Bounds.Bottom, 0.001);
        scene.Bars[0].Bounds.X.Should().BeLessThan(scene.Bars[1].Bounds.X);
        ChartSmartArtVisualPlanner.BuildChartVisualSignature(ChartSmartArtVisualPlanner.BuildChartPlan(TwoSeries(ChartKind.Column)))
            .Should().NotContain("grouping=");
    }

    [Fact]
    public void StackedColumn_StacksEachCategory_AndSizesTheAxisToTheTotals()
    {
        var scene = Scene(TwoSeries(ChartKind.Column, ChartGrouping.Stacked));

        scene.Bars.Should().HaveCount(6);
        scene.ValueAxis.Maximum.Should().Be(40, "every category totals 40");
        var (first, second) = (scene.Bars[0].Bounds, scene.Bars[1].Bounds);
        second.X.Should().BeApproximately(first.X, 0.001, "stacked segments share one column");
        second.Bottom.Should().BeApproximately(first.Y, 0.001, "the second series sits on top of the first");
        (first.Height + second.Height).Should().BeApproximately(scene.PlotBounds.Height, 0.5);
        ChartSmartArtVisualPlanner.BuildChartVisualSignature(
                ChartSmartArtVisualPlanner.BuildChartPlan(TwoSeries(ChartKind.Column, ChartGrouping.Stacked)))
            .Should().EndWith("|grouping=Stacked");
    }

    [Fact]
    public void PercentStackedBar_FillsTheAxisTo100Percent_Horizontally()
    {
        var scene = Scene(TwoSeries(ChartKind.Bar, ChartGrouping.PercentStacked));

        (scene.ValueAxis.Minimum, scene.ValueAxis.Maximum).Should().Be((0d, 100d));
        var (first, second) = (scene.Bars[0].Bounds, scene.Bars[1].Bounds);
        second.Y.Should().BeApproximately(first.Y, 0.001, "stacked segments share one bar");
        second.X.Should().BeApproximately(first.Right, 0.001);
        (first.Width + second.Width).Should().BeApproximately(scene.PlotBounds.Width, 0.5);
        first.Width.Should().BeApproximately(scene.PlotBounds.Width * 0.25, 0.5, "10 of 40 is 25%");
        scene.Texts.Where(text => text.Kind == ChartSceneTextKind.ValueAxis).Select(text => text.Text)
            .Should().Contain("100%");
    }

    [Fact]
    public void StackedNegativeValues_StackDownwardsFromZero()
    {
        var series = new[]
        {
            new ChartSeriesVisualPlan("a", [5, -5]),
            new ChartSeriesVisualPlan("b", [3, -2]),
        };

        var stacked = ChartSmartArtVisualPlanner.StackSeries(series, 2, percent: false);

        stacked[0][0].Should().Be((0d, 5d, 5d));
        stacked[1][0].Should().Be((5d, 8d, 3d));
        stacked[0][1].Should().Be((0d, -5d, -5d));
        stacked[1][1].Should().Be((-5d, -7d, -2d));
        var percent = ChartSmartArtVisualPlanner.StackSeries(series, 2, percent: true);
        percent[1][0].To.Should().BeApproximately(100, 1e-9);
        percent[1][1].To.Should().BeApproximately(-100, 1e-9);
    }

    [Theory]
    [InlineData(ChartKind.Line, false)]
    [InlineData(ChartKind.Area, true)]
    public void StackedLineAndArea_PlotCumulativeValues(ChartKind kind, bool fillsArea)
    {
        var scene = Scene(TwoSeries(kind, ChartGrouping.Stacked));

        scene.LineSeries.Should().HaveCount(2);
        scene.LineSeries.Should().OnlyContain(series => series.FillArea == fillsArea);
        // The cumulative top line is flat (every category totals 40) and sits at the plot's top edge.
        var top = scene.LineSeries.Single(series => series.Points.All(point => Math.Abs(point.Y - scene.PlotBounds.Y) < 0.5));
        top.Points.Should().HaveCount(3);
        if (fillsArea)
        {
            scene.LineSeries[0].Should().BeSameAs(top, "the top layer is painted first so lower layers cover it");
            scene.LineSeries.Should().OnlyContain(series => series.AreaOpacity == 1);
        }
    }

    [Fact]
    public void Radar_DrawsOneClosedPolygonPerSeries_OnAWebOfSpokes()
    {
        var scene = Scene(TwoSeries(ChartKind.Radar));

        scene.Bars.Should().BeEmpty();
        scene.LineSeries.Should().HaveCount(2);
        foreach (var polygon in scene.LineSeries)
        {
            polygon.Points.Should().HaveCount(4, "three spokes plus the closing point");
            polygon.Points[^1].Should().Be(polygon.Points[0]);
            polygon.FillArea.Should().BeFalse();
        }

        scene.GridLines.Should().NotBeEmpty();
        scene.Texts.Where(text => text.Kind == ChartSceneTextKind.CategoryAxis).Select(text => text.Text)
            .Should().Equal("A", "B", "C");
    }

    [Fact]
    public void Bubble_ScalesMarkerAreaWithTheBubbleSize()
    {
        var chart = new Chart { Kind = ChartKind.Bubble };
        chart.Categories.AddRange(["1", "2", "3"]);
        var series = new ChartSeries("Markets", [10, 20, 30]);
        series.BubbleSizes.AddRange([1, 4, 16]);
        chart.Series.Add(series);

        var scene = Scene(chart);

        scene.Markers.Should().HaveCount(3);
        scene.Markers.Should().OnlyContain(marker => marker.Kind == ChartSceneMarkerKind.Circle && marker.FillOpacity < 1);
        var radii = scene.Markers.Select(marker => marker.Radius).ToArray();
        radii.Should().BeInAscendingOrder();
        (radii[2] / radii[1]).Should().BeApproximately(2, 0.01, "four times the size is twice the radius");
        scene.Markers.Select(marker => marker.CenterX).Should().BeInAscendingOrder();
    }

    [Fact]
    public void Stock_DrawsAHighLowLineAndACloseTickPerCategory_OnAPriceBandAxis()
    {
        var chart = new Chart { Kind = ChartKind.Stock };
        chart.Categories.AddRange(["Mon", "Tue"]);
        chart.Series.Add(new ChartSeries("High", [120, 125]));
        chart.Series.Add(new ChartSeries("Low", [110, 112]));
        chart.Series.Add(new ChartSeries("Close", [115, 124]));

        var scene = Scene(chart);

        scene.ValueAxis.Minimum.Should().BeGreaterThan(0, "a price band is not anchored at zero");
        var vertical = scene.AxisLines.Where(line => Math.Abs(line.X1 - line.X2) < 0.001 && line.StrokeHex == "#404040").ToList();
        vertical.Should().HaveCount(2);
        vertical.Should().OnlyContain(line => line.Y1 < line.Y2, "high is drawn above low");
        scene.Markers.Should().HaveCount(2, "one close marker per category");
        scene.AxisLines.Should().OnlyContain(line =>
            line.Y1 >= scene.PlotBounds.Y - 0.001 && line.Y1 <= scene.PlotBounds.Bottom + 4.001);
    }

    [Fact]
    public void StockKind_WithoutThreeSeries_StillRendersAsLines()
    {
        var scene = Scene(TwoSeries(ChartKind.Stock));

        scene.LineSeries.Should().HaveCount(2);
    }

    [Fact]
    public void Combo_DrawsColumnsForColumnSeries_AndALineForLineSeries()
    {
        var chart = TwoSeries(ChartKind.Combo);
        chart.Series[1].PlotAsLine = true;

        var scene = Scene(chart);

        scene.Bars.Should().HaveCount(3, "only the first series is columns");
        scene.LineSeries.Should().ContainSingle().Which.Points.Should().HaveCount(3);
        // The line runs through the centre of each category group, over the columns.
        scene.LineSeries[0].Points[0].X.Should().BeApproximately(scene.Bars[0].Bounds.CenterX, 1);
        scene.PlotBounds.Right.Should().BeApproximately(Scene(TwoSeries(ChartKind.Column)).PlotBounds.Right, 0.001);
    }

    [Fact]
    public void Combo_SecondaryAxisSeries_GetsItsOwnScaleAndRightHandLabels()
    {
        var chart = TwoSeries(ChartKind.Combo);
        chart.Series[1] = new ChartSeries("Rate", [1000, 2000, 4000]) { PlotAsLine = true, UseSecondaryAxis = true };

        var scene = Scene(chart);

        scene.ValueAxis.Maximum.Should().Be(30, "the primary axis ignores the secondary series");
        scene.PlotBounds.Right.Should().BeLessThan(Scene(TwoSeries(ChartKind.Column)).PlotBounds.Right, "room for the right-hand axis");
        var rightLabels = scene.Texts.Where(text => text.Kind == ChartSceneTextKind.ValueAxis && text.X > scene.PlotBounds.Right).ToList();
        rightLabels.Select(text => text.Text).Should().Contain("4E+03").And.Contain("0");
        scene.LineSeries.Single().Points[^1].Y.Should().BeApproximately(scene.PlotBounds.Y, 0.5, "4000 tops the secondary scale");
    }

    [Theory]
    [InlineData(ChartKind.Radar)]
    [InlineData(ChartKind.Bubble)]
    [InlineData(ChartKind.Stock)]
    [InlineData(ChartKind.Combo)]
    public void NewKinds_SurviveEmptyAndDegenerateData(ChartKind kind)
    {
        var empty = new Chart { Kind = kind };
        var act = () => Scene(empty);
        act.Should().NotThrow();

        var degenerate = Chart.Create(kind, [], [double.NaN, 0, double.MaxValue]);
        ChartKindNormalizer.Normalize(degenerate);
        var scene = ChartSmartArtVisualPlanner.BuildChartScene(degenerate, 10, 10);
        scene.Markers.Should().OnlyContain(marker => double.IsFinite(marker.CenterX) && double.IsFinite(marker.Radius));
        scene.Bars.Should().OnlyContain(bar => double.IsFinite(bar.Bounds.Height));
    }

    [Theory]
    [InlineData(ChartKind.Column)]
    [InlineData(ChartKind.Line)]
    [InlineData(ChartKind.Scatter)]
    [InlineData(ChartKind.Radar)]
    [InlineData(ChartKind.Bubble)]
    [InlineData(ChartKind.Stock)]
    [InlineData(ChartKind.Combo)]
    public void HugeOrNearlyEqualValues_NeverStallTheAxisWalk(ChartKind kind)
    {
        // Values near double.MaxValue used to round the axis up to Infinity (an endless `value += step`),
        // and a tight band at a huge level makes the step too small to advance at all.
        foreach (var values in new[]
                 {
                     new[] { 1.7e308, 1e308, 1.5e308 },
                     new[] { 1e17, 1e17 + 16, 1e17 + 32 },
                     new[] { double.PositiveInfinity, double.NegativeInfinity, double.NaN },
                 })
        {
            var chart = new Chart { Kind = kind };
            chart.Categories.AddRange(["1e308", "1.0000000000000001e17", "1e17"]);
            chart.Series.Add(new ChartSeries("High", values));
            chart.Series.Add(new ChartSeries("Low", values.Select(value => value * 0.999999999).ToArray()));
            chart.Series.Add(new ChartSeries("Close", values));
            ChartKindNormalizer.Normalize(chart);

            var scene = Scene(chart);

            scene.Texts.Count.Should().BeLessThan(200);
            scene.GridLines.Count.Should().BeLessThan(400);
            scene.ValueAxis.Maximum.Should().BeGreaterThan(scene.ValueAxis.Minimum);
            double.IsFinite(scene.ValueAxis.Maximum).Should().BeTrue();
        }
    }
    // ── Chart-type picker (Insert Chart / Edit Data) ─────────────────────────────────────────────

    [Fact]
    public void ChartTypePicker_ListsEveryKindFirst_ThenTheStackedVariants()
    {
        var choices = InsertChartDialogPlanner.BuildChartTypeChoices();

        choices.Take(Enum.GetValues<ChartKind>().Length).Select(choice => choice.Label)
            .Should().Equal(Enum.GetNames<ChartKind>(), "the existing entries keep their labels and positions");
        choices.Select(choice => choice.Label).Should().Contain(
            ["Radar", "Bubble", "Stock", "Combo", "Stacked Column", "100% Stacked Column", "Stacked Bar",
             "100% Stacked Bar", "Stacked Line", "100% Stacked Line", "Stacked Area", "100% Stacked Area"]);
        choices.Select(choice => choice.Id).Should().OnlyHaveUniqueItems();
        choices.Select(choice => (choice.Kind, choice.Grouping)).Should().OnlyHaveUniqueItems();
        InsertChartDialogPlanner.RequiredResourceKeys.Should().Contain(
            choices.Select(choice => "InsertChart_Type_" + choice.Id));
    }

    [Fact]
    public void ChartTypePicker_LabelsAreLocalized()
    {
        var choices = InsertChartDialogPlanner.BuildChartTypeChoices(key => "localized:" + key);

        choices.Should().OnlyContain(choice => choice.Label == "localized:InsertChart_Type_" + choice.Id);
    }

    [Theory]
    [InlineData(ChartKind.Column, ChartGrouping.Standard, "Column")]
    [InlineData(ChartKind.Column, ChartGrouping.PercentStacked, "100% Stacked Column")]
    [InlineData(ChartKind.Area, ChartGrouping.Stacked, "Stacked Area")]
    [InlineData(ChartKind.Pie, ChartGrouping.Stacked, "Pie")]
    [InlineData(ChartKind.Combo, ChartGrouping.Standard, "Combo")]
    public void ChartTypePicker_SelectsTheEntryForAChart(ChartKind kind, ChartGrouping grouping, string label)
    {
        var index = InsertChartDialogPlanner.FindChartTypeChoiceIndex(kind, grouping);

        InsertChartDialogPlanner.ChartTypeChoiceAt(index).Label.Should().Be(label);
        InsertChartDialogPlanner.ChartTypeChoiceAt(-1).Label.Should().Be("Column");
        InsertChartDialogPlanner.ChartTypeChoiceAt(999).Label.Should().Be("Column");
    }

    [Fact]
    public void InitialState_ReportsTheSeedGrouping()
    {
        var state = InsertChartDialogPlanner.BuildInitialState(
            TwoSeries(ChartKind.Bar, ChartGrouping.Stacked), CultureInfo.InvariantCulture);

        (state.Kind, state.Grouping).Should().Be((ChartKind.Bar, ChartGrouping.Stacked));
    }

    private static Chart BuildResult(ChartKind kind, ChartGrouping grouping, Chart? seed, params string[][] rows)
    {
        var seriesCount = rows[0].Length - 1;
        InsertChartDialogPlanner.TryBuildResult(
                kind,
                grouping,
                "Title",
                Enumerable.Range(1, seriesCount).Select(index => "S" + index).ToArray(),
                rows.Select(row => new InsertChartDialogRow(row[0], row[1..])),
                CultureInfo.InvariantCulture,
                getText: null,
                seed,
                out var result,
                out _)
            .Should().BeTrue();
        return result!;
    }

    [Fact]
    public void DialogResult_CarriesThePickedGrouping()
    {
        var chart = BuildResult(ChartKind.Column, ChartGrouping.PercentStacked, seed: null, ["A", "1", "2"], ["B", "3", "4"]);

        (chart.Kind, chart.Grouping).Should().Be((ChartKind.Column, ChartGrouping.PercentStacked));
        BuildResult(ChartKind.Pie, ChartGrouping.Stacked, seed: null, ["A", "1"]).Grouping
            .Should().Be(ChartGrouping.Standard, "a pie cannot stack");
    }

    [Fact]
    public void DialogResult_ForStockAndCombo_IsAlwaysAWorkingChartOfThatKind()
    {
        var stock = BuildResult(ChartKind.Stock, ChartGrouping.Standard, seed: null, ["Mon", "10"], ["Tue", "20"]);
        ChartKindNormalizer.IsStockShaped(stock).Should().BeTrue();

        var combo = BuildResult(ChartKind.Combo, ChartGrouping.Standard, seed: null, ["A", "1", "2"], ["B", "3", "4"]);
        combo.Series.Select(series => series.PlotAsLine).Should().Equal(false, true);
    }

    [Fact]
    public void EditData_KeepsWhatTheGridCannotShow_FromTheChartBeingEdited()
    {
        var seed = TwoSeries(ChartKind.Combo);
        seed.Series[0].PlotAsLine = true;
        seed.Series[0].UseSecondaryAxis = true;

        var edited = BuildResult(ChartKind.Combo, ChartGrouping.Standard, seed, ["A", "5", "6"], ["B", "7", "8"]);

        edited.Series.Select(series => (series.PlotAsLine, series.UseSecondaryAxis)).Should().Equal((true, true), (false, false));
        edited.Series[0].Values.Should().Equal(5, 7);

        var bubbleSeed = Chart.Create(ChartKind.Bubble, ["1", "2"], [1d, 2d]);
        bubbleSeed.Series[0].BubbleSizes.AddRange([9, 4]);
        BuildResult(ChartKind.Bubble, ChartGrouping.Standard, bubbleSeed, ["1", "3"], ["2", "5"])
            .Series[0].BubbleSizes.Should().Equal(9, 4);

        // Changing kind in the dialog does not drag the old kind's settings along.
        BuildResult(ChartKind.Column, ChartGrouping.Standard, seed, ["A", "5", "6"])
            .Series.Should().OnlyContain(series => !series.PlotAsLine && !series.UseSecondaryAxis);
    }

    // ── Ribbon: Change Chart Type ────────────────────────────────────────────────────────────────

    private static FreeWRibbonCommandBindingPorts RegisterChartCommands(Func<Chart?> selected, Action<Chart> applyData)
    {
        var bindings = new FreeWRibbonCommandBindingPorts();
        FreeWRibbonEditorExecutionProfile.RegisterChartSmartArt(
            bindings,
            new FreeWRibbonChartSmartArtExecutionPorts(
                PrepareExecution: () => { },
                CompleteExecution: () => { },
                SelectedChart: selected,
                SetChartKind: _ => { },
                ApplyChartStyle: _ => { },
                ApplyChartColorScheme: _ => { },
                ApplyChartQuickLayout: _ => { },
                ToggleChartLegend: () => { },
                ShowChartTitleDialogAsync: _ => ValueTask.FromResult<ChartTitleDialogResult?>(null),
                ApplyChartTitleOutcome: _ => { },
                ToggleChartTitleFallback: null,
                ShowChartAxisTitlesDialogAsync: _ => ValueTask.FromResult<ChartAxisTitlesDialogResult?>(null),
                ApplyChartAxisTitlesOutcome: _ => { },
                ToggleChartAxisTitlesFallback: null,
                ShowChartDataDialogAsync: _ => ValueTask.FromResult<Chart?>(null),
                ApplyChartDataOutcome: applyData,
                ShowChartSizeDialogAsync: _ => ValueTask.FromResult<ChartSizeDialogResult?>(null),
                ApplyChartSizeOutcome: _ => { },
                SelectedSmartArt: () => null,
                MutateSmartArt: _ => { },
                ApplySmartArtLayout: _ => { },
                ApplySmartArtColorScheme: _ => { },
                ApplySmartArtStyle: _ => { },
                ShowSmartArtEditDialogAsync: _ => ValueTask.FromResult<SmartArt?>(null),
                ApplySmartArtEditOutcome: _ => { }));
        return bindings;
    }

    [Fact]
    public void EveryChartKind_HasAChangeChartTypeCommand()
    {
        var bindings = RegisterChartCommands(() => null, _ => { });

        foreach (var kind in Enum.GetValues<ChartKind>())
            bindings.TryGet($"freew.chart-type-{kind.ToString().ToLowerInvariant()}", out _).Should().BeTrue();
        foreach (var grouping in Enum.GetValues<ChartGrouping>())
            bindings.TryGet(FreeWRibbonEditorExecutionProfile.ChartGroupingCommandId(grouping), out _).Should().BeTrue();
    }

    [Fact]
    public void GroupingCommand_ReplacesTheSelectedChartWithAStackedCopy_ThroughTheUndoableDataPath()
    {
        Chart? selected = TwoSeries(ChartKind.Column);
        Chart? applied = null;
        var bindings = RegisterChartCommands(() => selected, chart => applied = chart);
        bindings.TryGet("freew.chart-grouping-stacked", out var stacked).Should().BeTrue();

        ((IRibbonStatefulCommand)stacked!).GetState().IsEnabled.Should().BeTrue();
        stacked.Execute(RibbonCommandContext.Empty);

        applied.Should().NotBeNull().And.NotBeSameAs(selected);
        applied!.Grouping.Should().Be(ChartGrouping.Stacked);
        applied.Series.Select(series => series.Values.ToArray()).Should().BeEquivalentTo(
            selected!.Series.Select(series => series.Values.ToArray()));
        selected.Grouping.Should().Be(ChartGrouping.Standard, "the host's undoable command applies the change");
    }

    [Fact]
    public void GroupingCommand_IsDisabledForKindsThatCannotStack_AndIgnoresANoOp()
    {
        Chart? selected = TwoSeries(ChartKind.Pie);
        Chart? applied = null;
        var bindings = RegisterChartCommands(() => selected, chart => applied = chart);
        bindings.TryGet("freew.chart-grouping-percent", out var percent).Should().BeTrue();
        bindings.TryGet("freew.chart-grouping-standard", out var standard).Should().BeTrue();

        ((IRibbonStatefulCommand)percent!).GetState().IsEnabled.Should().BeFalse();
        percent.Execute(RibbonCommandContext.Empty);
        applied.Should().BeNull();

        selected = TwoSeries(ChartKind.Line);
        standard!.Execute(RibbonCommandContext.Empty);
        applied.Should().BeNull("the chart is already unstacked");

        selected = null;
        ((IRibbonStatefulCommand)percent).GetState().IsEnabled.Should().BeFalse();
    }
}
