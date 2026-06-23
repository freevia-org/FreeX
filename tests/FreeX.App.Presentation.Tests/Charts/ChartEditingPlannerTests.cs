using FluentAssertions;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Charts;

public sealed class ChartEditingPlannerTests
{
    // ---- ChartTypeChangePlanner ----------------------------------------------------------------------

    [Fact]
    public void TypeChange_SupportedChoices_AreAllAuthorable_AndCoverCommonFamilies()
    {
        var choices = ChartTypeChangePlanner.GetSupportedChoices();

        choices.Should().NotBeEmpty();
        choices.Should().OnlyContain(choice => ChartTypeSupport.IsAuthorable(choice.Type));
        choices.Select(c => c.Type).Should().Contain(new[]
        {
            ChartType.Column, ChartType.Bar, ChartType.Line, ChartType.Area,
            ChartType.Scatter, ChartType.Pie, ChartType.Doughnut, ChartType.Bubble,
            ChartType.Radar, ChartType.Stock
        });
        choices.Should().OnlyContain(choice => !string.IsNullOrWhiteSpace(choice.DisplayName));
    }

    [Fact]
    public void TypeChange_Plan_ReturnsRequestedType_WhenDifferentAndAuthorable()
    {
        var plan = ChartTypeChangePlanner.Plan(ChartType.Column, ChartType.Line);

        plan.HasChange.Should().BeTrue();
        plan.AppliedType.Should().Be(ChartType.Line);
        plan.Message.Should().BeNull();
    }

    [Fact]
    public void TypeChange_Plan_IsNoOp_WhenTypeUnchanged()
    {
        var plan = ChartTypeChangePlanner.Plan(ChartType.Pie, ChartType.Pie);

        plan.HasChange.Should().BeFalse();
        plan.AppliedType.Should().BeNull();
        plan.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TypeChange_Plan_Rejects_DeferredAuthoringFamily()
    {
        // Map is renderable-but-not-authorable: the planner must reject converting to it.
        var plan = ChartTypeChangePlanner.Plan(ChartType.Column, ChartType.Map);

        plan.HasChange.Should().BeFalse();
        plan.Message.Should().Be(ChartAuthoringPlanner.DeferredAuthoringMessage);
    }

    // ---- ChartTitlesPlanner --------------------------------------------------------------------------

    [Fact]
    public void Titles_Plan_TrimsAndCollapsesWhitespace()
    {
        var input = new ChartTitlesInput("  Sales  ", "  Quarter ", "   ");
        var options = ChartTitlesPlanner.Plan(ChartType.Column, input);

        options.Title.Should().Be("Sales");
        options.XAxisTitle.Should().Be("Quarter");
        options.YAxisTitle.Should().BeEmpty();
    }

    [Fact]
    public void Titles_Plan_DropsAxisTitles_ForAxislessChartTypes()
    {
        var input = new ChartTitlesInput("Revenue", "Category", "Value");
        var options = ChartTitlesPlanner.Plan(ChartType.Pie, input);

        options.Title.Should().Be("Revenue");
        options.XAxisTitle.Should().BeEmpty();
        options.YAxisTitle.Should().BeEmpty();
    }

    [Fact]
    public void Titles_Read_ProjectsModelTitles()
    {
        var chart = new ChartModel { Title = "T", XAxisTitle = "X", YAxisTitle = "Y" };
        var input = ChartTitlesPlanner.Read(chart);

        input.ChartTitle.Should().Be("T");
        input.XAxisTitle.Should().Be("X");
        input.YAxisTitle.Should().Be("Y");
    }

    [Fact]
    public void Titles_Plan_RoundTripsThroughSetChartLayoutCommand()
    {
        // The planner output applied via the Core command must land on the model.
        var chart = new ChartModel { Type = ChartType.Column, Title = "old" };
        var options = ChartTitlesPlanner.Plan(chart.Type, new ChartTitlesInput("New Title", "Months", "Units"));

        ApplyLayout(chart, options);

        chart.Title.Should().Be("New Title");
        chart.XAxisTitle.Should().Be("Months");
        chart.YAxisTitle.Should().Be("Units");
    }

    // ---- ChartLegendPlanner --------------------------------------------------------------------------

    [Fact]
    public void Legend_PositionChoices_AreTheFourPlacements()
    {
        var positions = ChartLegendPlanner.GetPositionChoices().Select(c => c.Position).ToList();

        positions.Should().BeEquivalentTo(new[]
        {
            ChartLegendPosition.Right, ChartLegendPosition.Top,
            ChartLegendPosition.Left, ChartLegendPosition.Bottom
        });
        positions.Should().NotContain(ChartLegendPosition.None);
    }

    [Fact]
    public void Legend_Read_SurfacesNoneAsRight()
    {
        var chart = new ChartModel { ShowLegend = false, LegendPosition = ChartLegendPosition.None };
        var input = ChartLegendPlanner.Read(chart);

        input.ShowLegend.Should().BeFalse();
        input.Position.Should().Be(ChartLegendPosition.Right);
    }

    [Fact]
    public void Legend_Plan_SetsShowAndPosition()
    {
        var options = ChartLegendPlanner.Plan(new ChartLegendInput(ShowLegend: true, ChartLegendPosition.Bottom));

        options.ShowLegend.Should().BeTrue();
        options.LegendPosition.Should().Be(ChartLegendPosition.Bottom);
    }

    [Fact]
    public void Legend_Plan_KeepsPosition_EvenWhenHidden()
    {
        var options = ChartLegendPlanner.Plan(new ChartLegendInput(ShowLegend: false, ChartLegendPosition.Left));

        options.ShowLegend.Should().BeFalse();
        options.LegendPosition.Should().Be(ChartLegendPosition.Left);
    }

    [Fact]
    public void Legend_Plan_FallsBackToRight_ForInvalidPosition()
    {
        var options = ChartLegendPlanner.Plan(new ChartLegendInput(ShowLegend: true, ChartLegendPosition.None));

        options.LegendPosition.Should().Be(ChartLegendPosition.Right);
    }

    [Fact]
    public void Legend_Plan_RoundTripsThroughSetChartLayoutCommand()
    {
        var chart = new ChartModel { Type = ChartType.Column, ShowLegend = true, LegendPosition = ChartLegendPosition.Right };
        var options = ChartLegendPlanner.Plan(new ChartLegendInput(ShowLegend: true, ChartLegendPosition.Top));

        ApplyLayout(chart, options);

        chart.ShowLegend.Should().BeTrue();
        chart.LegendPosition.Should().Be(ChartLegendPosition.Top);
    }

    // ---- ChartDataLabelsPlanner ----------------------------------------------------------------------

    [Fact]
    public void DataLabels_PositionChoices_CoverTheFourPlacements()
    {
        var positions = ChartDataLabelsPlanner.GetPositionChoices().Select(c => c.Position).ToList();

        positions.Should().BeEquivalentTo(new[]
        {
            ChartDataLabelPosition.BestFit, ChartDataLabelPosition.OutsideEnd,
            ChartDataLabelPosition.InsideEnd, ChartDataLabelPosition.Center
        });
        ChartDataLabelsPlanner.GetPositionChoices().Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.DisplayName));
    }

    [Fact]
    public void DataLabels_Read_ProjectsModelState()
    {
        var chart = new ChartModel
        {
            ShowDataLabels = true,
            DataLabelPosition = ChartDataLabelPosition.OutsideEnd,
            ShowDataLabelValue = false,
            ShowDataLabelCategoryName = true,
            ShowDataLabelPercentage = true,
        };
        var input = ChartDataLabelsPlanner.Read(chart);

        input.ShowDataLabels.Should().BeTrue();
        input.Position.Should().Be(ChartDataLabelPosition.OutsideEnd);
        input.ShowValue.Should().BeFalse();
        input.ShowCategoryName.Should().BeTrue();
        input.ShowPercentage.Should().BeTrue();
    }

    [Fact]
    public void DataLabels_Plan_ForcesValueWhenShownWithoutAnyToggle()
    {
        var input = new ChartDataLabelsInput(
            ShowDataLabels: true, ChartDataLabelPosition.Center,
            ShowValue: false, ShowCategoryName: false, ShowSeriesName: false,
            ShowPercentage: false, ShowLegendKey: false);
        var options = ChartDataLabelsPlanner.Plan(input);

        options.ShowDataLabels.Should().BeTrue();
        options.ShowDataLabelValue.Should().BeTrue();
        options.DataLabelPosition.Should().Be(ChartDataLabelPosition.Center);
    }

    [Fact]
    public void DataLabels_Plan_KeepsConfiguration_EvenWhenHidden()
    {
        var input = new ChartDataLabelsInput(
            ShowDataLabels: false, ChartDataLabelPosition.InsideEnd,
            ShowValue: false, ShowCategoryName: true, ShowSeriesName: false,
            ShowPercentage: false, ShowLegendKey: false);
        var options = ChartDataLabelsPlanner.Plan(input);

        options.ShowDataLabels.Should().BeFalse();
        options.DataLabelPosition.Should().Be(ChartDataLabelPosition.InsideEnd);
        options.ShowDataLabelCategoryName.Should().BeTrue();
        // Hidden labels must not force a value toggle on.
        options.ShowDataLabelValue.Should().BeFalse();
    }

    [Fact]
    public void DataLabels_Plan_RoundTripsThroughSetChartLayoutCommand()
    {
        var chart = new ChartModel { Type = ChartType.Column };
        var options = ChartDataLabelsPlanner.Plan(new ChartDataLabelsInput(
            ShowDataLabels: true, ChartDataLabelPosition.OutsideEnd,
            ShowValue: true, ShowCategoryName: true, ShowSeriesName: false,
            ShowPercentage: false, ShowLegendKey: false));

        ApplyLayout(chart, options);

        chart.ShowDataLabels.Should().BeTrue();
        chart.DataLabelPosition.Should().Be(ChartDataLabelPosition.OutsideEnd);
        chart.ShowDataLabelValue.Should().BeTrue();
        chart.ShowDataLabelCategoryName.Should().BeTrue();
    }

    // ---- ChartAxisPlanner ----------------------------------------------------------------------------

    [Fact]
    public void Axis_Read_ProjectsChosenAxisState()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            YAxisMinimum = 0,
            YAxisMaximum = 100,
            YAxisMajorUnit = 25,
            YAxisNumberFormat = ChartDataLabelNumberFormat.Currency,
            ShowYAxisMajorGridlines = true,
        };
        var input = ChartAxisPlanner.Read(chart, useXAxis: false);

        input.UseXAxis.Should().BeFalse();
        input.Minimum.Should().Be(0);
        input.Maximum.Should().Be(100);
        input.MajorUnit.Should().Be(25);
        input.NumberFormat.Should().Be(ChartDataLabelNumberFormat.Currency);
        input.ShowMajorGridlines.Should().BeTrue();
    }

    [Fact]
    public void Axis_Validate_RejectsMinimumNotBelowMaximum()
    {
        var input = new ChartAxisInput(true, Minimum: 10, Maximum: 5, MajorUnit: null,
            LogScale: false, ChartDataLabelNumberFormat.General, ShowMajorGridlines: false, ShowMinorGridlines: false);

        ChartAxisPlanner.Validate(input).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Axis_Validate_RejectsNonPositiveMajorUnit()
    {
        var input = new ChartAxisInput(true, Minimum: null, Maximum: null, MajorUnit: 0,
            LogScale: false, ChartDataLabelNumberFormat.General, ShowMajorGridlines: false, ShowMinorGridlines: false);

        ChartAxisPlanner.Validate(input).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Axis_Validate_AllowsAutoBounds()
    {
        var input = new ChartAxisInput(true, Minimum: null, Maximum: null, MajorUnit: null,
            LogScale: false, ChartDataLabelNumberFormat.General, ShowMajorGridlines: true, ShowMinorGridlines: false);

        ChartAxisPlanner.Validate(input).Should().BeNull();
    }

    [Fact]
    public void Axis_Plan_SetsClearBoundsFlag_WhenBothBoundsAuto()
    {
        var input = new ChartAxisInput(true, Minimum: null, Maximum: null, MajorUnit: null,
            LogScale: false, ChartDataLabelNumberFormat.General, ShowMajorGridlines: false, ShowMinorGridlines: false);

        var xOptions = ChartAxisPlanner.Plan(input);
        xOptions.ClearXAxisBounds.Should().BeTrue();

        var yOptions = ChartAxisPlanner.Plan(input with { UseXAxis = false });
        yOptions.ClearYAxisBounds.Should().BeTrue();
    }

    [Fact]
    public void Axis_Plan_RoundTripsThroughSetChartLayoutCommand()
    {
        var chart = new ChartModel { Type = ChartType.Column };
        var input = new ChartAxisInput(false, Minimum: 0, Maximum: 50, MajorUnit: 10,
            LogScale: false, ChartDataLabelNumberFormat.Number, ShowMajorGridlines: true, ShowMinorGridlines: true);

        ApplyLayout(chart, ChartAxisPlanner.Plan(input));

        chart.YAxisMinimum.Should().Be(0);
        chart.YAxisMaximum.Should().Be(50);
        chart.YAxisMajorUnit.Should().Be(10);
        chart.YAxisNumberFormat.Should().Be(ChartDataLabelNumberFormat.Number);
        chart.ShowYAxisMajorGridlines.Should().BeTrue();
        chart.ShowYAxisMinorGridlines.Should().BeTrue();
    }

    // ---- ChartSeriesFormatPlanner --------------------------------------------------------------------

    [Fact]
    public void SeriesFormat_Read_ReturnsStoredFormat_ForChosenSeries()
    {
        // A 3-column range (category + two value columns) yields >= 2 data series so index 1 is valid.
        var chart = MultiSeriesChart();
        chart.SeriesFormats.Add(new ChartSeriesFormat(1, FillColor: new CellColor(10, 20, 30), StrokeThickness: 2.5));
        var input = ChartSeriesFormatPlanner.Read(chart, 1);

        input.SeriesIndex.Should().Be(1);
        input.FillColor.Should().Be(new CellColor(10, 20, 30));
        input.StrokeThickness.Should().Be(2.5);
    }

    [Fact]
    public void SeriesFormat_Read_ClampsRequestedIndexIntoSeriesRange()
    {
        // An empty data range has at most one series, so an out-of-range request clamps to 0.
        var chart = new ChartModel { Type = ChartType.Line };
        ChartSeriesFormatPlanner.Read(chart, 5).SeriesIndex.Should().Be(0);
    }

    private static ChartModel MultiSeriesChart()
    {
        var sheet = new SheetId(Guid.NewGuid());
        return new ChartModel
        {
            Type = ChartType.Line,
            FirstColIsCategories = true,
            DataRange = new GridRange(
                new CellAddress(sheet, 1, 1),
                new CellAddress(sheet, 4, 3)),
        };
    }

    [Fact]
    public void SeriesFormat_Validate_RejectsNonPositiveLineWidthAndMarkerSize()
    {
        ChartSeriesFormatPlanner.Validate(new ChartSeriesFormatInput(0, null, null, StrokeThickness: 0, null, null))
            .Should().NotBeNullOrWhiteSpace();
        ChartSeriesFormatPlanner.Validate(new ChartSeriesFormatInput(0, null, null, null, null, MarkerSize: -1))
            .Should().NotBeNullOrWhiteSpace();
        ChartSeriesFormatPlanner.Validate(new ChartSeriesFormatInput(0, null, null, null, null, null))
            .Should().BeNull();
    }

    [Fact]
    public void SeriesFormat_Plan_ReplacesMatchingSeries_AndPreservesOthers()
    {
        var chart = new ChartModel { Type = ChartType.Line };
        chart.SeriesFormats.Add(new ChartSeriesFormat(0, FillColor: new CellColor(1, 1, 1)));
        chart.SeriesFormats.Add(new ChartSeriesFormat(1, FillColor: new CellColor(2, 2, 2)));

        var options = ChartSeriesFormatPlanner.Plan(chart, new ChartSeriesFormatInput(
            1, FillColor: new CellColor(9, 9, 9), StrokeColor: null, StrokeThickness: null,
            MarkerStyle: ChartMarkerStyle.Circle, MarkerSize: 6));

        options.SeriesFormats.Should().HaveCount(2);
        options.SeriesFormats!.Single(f => f.SeriesIndex == 0).FillColor.Should().Be(new CellColor(1, 1, 1));
        var updated = options.SeriesFormats!.Single(f => f.SeriesIndex == 1);
        updated.FillColor.Should().Be(new CellColor(9, 9, 9));
        updated.MarkerStyle.Should().Be(ChartMarkerStyle.Circle);
        updated.MarkerSize.Should().Be(6);
    }

    [Fact]
    public void SeriesFormat_Plan_AppendsWhenSeriesHasNoExistingFormat()
    {
        var chart = new ChartModel { Type = ChartType.Line };
        var options = ChartSeriesFormatPlanner.Plan(chart, new ChartSeriesFormatInput(
            2, FillColor: new CellColor(5, 5, 5), null, null, null, null));

        options.SeriesFormats.Should().ContainSingle(f => f.SeriesIndex == 2 && f.FillColor == new CellColor(5, 5, 5));
    }

    [Fact]
    public void SeriesFormat_Plan_RoundTripsThroughSetChartLayoutCommand()
    {
        var chart = new ChartModel { Type = ChartType.Line };
        var options = ChartSeriesFormatPlanner.Plan(chart, new ChartSeriesFormatInput(
            0, FillColor: new CellColor(7, 8, 9), StrokeColor: new CellColor(1, 2, 3),
            StrokeThickness: 3, MarkerStyle: ChartMarkerStyle.Square, MarkerSize: 8));

        ApplyLayout(chart, options);

        var format = chart.SeriesFormats.Single(f => f.SeriesIndex == 0);
        format.FillColor.Should().Be(new CellColor(7, 8, 9));
        format.StrokeColor.Should().Be(new CellColor(1, 2, 3));
        format.StrokeThickness.Should().Be(3);
        format.MarkerStyle.Should().Be(ChartMarkerStyle.Square);
        format.MarkerSize.Should().Be(8);
    }

    // ---- ChartTrendlinePlanner -----------------------------------------------------------------------

    [Fact]
    public void Trendline_TypeChoices_CoverAllSixTypes()
    {
        var types = ChartTrendlinePlanner.GetTypeChoices().Select(c => c.Type).ToList();

        types.Should().BeEquivalentTo(new[]
        {
            ChartTrendlineType.Linear, ChartTrendlineType.Exponential, ChartTrendlineType.Logarithmic,
            ChartTrendlineType.Power, ChartTrendlineType.MovingAverage, ChartTrendlineType.Polynomial
        });
    }

    [Fact]
    public void Trendline_Supports_OnlyTrendlineCapableTypes()
    {
        ChartTrendlinePlanner.SupportsTrendlines(ChartType.Line).Should().BeTrue();
        ChartTrendlinePlanner.SupportsTrendlines(ChartType.Scatter).Should().BeTrue();
        ChartTrendlinePlanner.SupportsTrendlines(ChartType.Pie).Should().BeFalse();
    }

    [Fact]
    public void Trendline_Plan_ClampsPeriodAndOrder()
    {
        var options = ChartTrendlinePlanner.Plan(new ChartTrendlineInput(
            ShowTrendline: true, ChartTrendlineType.MovingAverage,
            Period: 1000, Order: 99, ShowEquation: true, ShowRSquared: true));

        options.ShowLinearTrendline.Should().BeTrue();
        options.TrendlineType.Should().Be(ChartTrendlineType.MovingAverage);
        options.TrendlinePeriod.Should().Be(ChartTrendlinePlanner.MaxPeriod);
        options.TrendlineOrder.Should().Be(ChartTrendlinePlanner.MaxOrder);
        options.ShowTrendlineEquation.Should().BeTrue();
        options.ShowTrendlineRSquared.Should().BeTrue();
    }

    [Fact]
    public void Trendline_Plan_RoundTripsThroughSetChartLayoutCommand()
    {
        var chart = new ChartModel { Type = ChartType.Scatter };
        var options = ChartTrendlinePlanner.Plan(new ChartTrendlineInput(
            ShowTrendline: true, ChartTrendlineType.Exponential,
            Period: 3, Order: 2, ShowEquation: true, ShowRSquared: false));

        ApplyLayout(chart, options);

        chart.ShowLinearTrendline.Should().BeTrue();
        chart.TrendlineType.Should().Be(ChartTrendlineType.Exponential);
        chart.ShowTrendlineEquation.Should().BeTrue();
        chart.ShowTrendlineRSquared.Should().BeFalse();
    }

    // ---- ChartErrorBarsPlanner -----------------------------------------------------------------------

    [Fact]
    public void ErrorBars_KindChoices_CoverAllFourKinds()
    {
        var kinds = ChartErrorBarsPlanner.GetKindChoices().Select(c => c.Kind).ToList();

        kinds.Should().BeEquivalentTo(new[]
        {
            ChartErrorBarKind.StandardError, ChartErrorBarKind.Percentage,
            ChartErrorBarKind.FixedValue, ChartErrorBarKind.Custom
        });
    }

    [Fact]
    public void ErrorBars_DirectionChoices_CoverAllThreeDirections()
    {
        var directions = ChartErrorBarsPlanner.GetDirectionChoices().Select(c => c.Direction).ToList();

        directions.Should().BeEquivalentTo(new[]
        {
            ChartErrorBarDirection.Both, ChartErrorBarDirection.Plus, ChartErrorBarDirection.Minus
        });
    }

    [Fact]
    public void ErrorBars_Supports_OnlyErrorBarCapableTypes()
    {
        ChartErrorBarsPlanner.SupportsErrorBars(ChartType.Column).Should().BeTrue();
        ChartErrorBarsPlanner.SupportsErrorBars(ChartType.Scatter).Should().BeTrue();
        ChartErrorBarsPlanner.SupportsErrorBars(ChartType.Pie).Should().BeFalse();
    }

    [Fact]
    public void ErrorBars_Plan_ClampsValue()
    {
        var options = ChartErrorBarsPlanner.Plan(new ChartErrorBarsInput(
            ShowErrorBars: true, ChartErrorBarKind.FixedValue,
            ChartErrorBarDirection.Plus, Value: 99999, EndCaps: true));

        options.ShowErrorBars.Should().BeTrue();
        options.ErrorBarKind.Should().Be(ChartErrorBarKind.FixedValue);
        options.ErrorBarDirection.Should().Be(ChartErrorBarDirection.Plus);
        options.ErrorBarValue.Should().Be(ChartErrorBarsPlanner.MaxValue);
        options.ErrorBarEndCaps.Should().BeTrue();
    }

    [Fact]
    public void ErrorBars_Read_FallsBackForNonFiniteValue()
    {
        var chart = new ChartModel { Type = ChartType.Line, ErrorBarValue = double.NaN };

        var input = ChartErrorBarsPlanner.Read(chart);

        double.IsFinite(input.Value).Should().BeTrue();
        input.Value.Should().BeInRange(ChartErrorBarsPlanner.MinValue, ChartErrorBarsPlanner.MaxValue);
    }

    [Fact]
    public void ErrorBars_Plan_RoundTripsThroughSetChartLayoutCommand()
    {
        var chart = new ChartModel { Type = ChartType.Column };
        var options = ChartErrorBarsPlanner.Plan(new ChartErrorBarsInput(
            ShowErrorBars: true, ChartErrorBarKind.Percentage,
            ChartErrorBarDirection.Minus, Value: 12.5, EndCaps: false));

        ApplyLayout(chart, options);

        chart.ShowErrorBars.Should().BeTrue();
        chart.ErrorBarKind.Should().Be(ChartErrorBarKind.Percentage);
        chart.ErrorBarDirection.Should().Be(ChartErrorBarDirection.Minus);
        chart.ErrorBarValue.Should().Be(12.5);
        chart.ErrorBarEndCaps.Should().BeFalse();
    }

    // ---- ChartComboPlanner ---------------------------------------------------------------------------

    [Fact]
    public void Combo_SupportsCombo_RequiresColumnOrAreaFamilyWithTwoSeries()
    {
        ChartComboPlanner.SupportsCombo(MakeChartWithSeries(ChartType.Column, columns: 3)).Should().BeTrue();
        ChartComboPlanner.SupportsCombo(MakeChartWithSeries(ChartType.Column, columns: 2)).Should().BeFalse();
        ChartComboPlanner.SupportsCombo(MakeChartWithSeries(ChartType.Pie, columns: 3)).Should().BeFalse();
    }

    [Fact]
    public void Combo_Read_AnchorsBaseSeries_AndReflectsStoredTreatment()
    {
        var chart = MakeChartWithSeries(ChartType.Column, columns: 4); // 3 series: 0,1,2
        chart.ComboLineSeriesIndexes = [2];
        chart.SecondaryAxisSeriesIndexes = [1];

        var input = ChartComboPlanner.Read(chart);

        input.Series.Should().HaveCount(3);
        input.Series[0].Should().Be(new ChartComboSeriesInput(0, AsLine: false, OnSecondaryAxis: false));
        input.Series[1].AsLine.Should().BeFalse();
        input.Series[1].OnSecondaryAxis.Should().BeTrue();
        input.Series[2].AsLine.Should().BeTrue();
        input.Series[2].OnSecondaryAxis.Should().BeFalse();
    }

    [Fact]
    public void Combo_Plan_ProjectsLineAndSecondarySets_DroppingBaseSeries()
    {
        var input = new ChartComboInput(new[]
        {
            new ChartComboSeriesInput(0, AsLine: true, OnSecondaryAxis: true), // base must be ignored
            new ChartComboSeriesInput(1, AsLine: true, OnSecondaryAxis: false),
            new ChartComboSeriesInput(2, AsLine: false, OnSecondaryAxis: true),
        });

        var options = ChartComboPlanner.Plan(input);

        options.ComboLineSeriesIndexes.Should().Equal(1);
        options.UseComboLineForSecondarySeries.Should().BeTrue();
        options.SecondaryAxisSeriesIndexes.Should().Equal(2);
        options.ShowSecondaryAxis.Should().BeTrue();
    }

    [Fact]
    public void Combo_Plan_ClearsOverlayFlags_WhenNothingSelected()
    {
        var input = new ChartComboInput(new[]
        {
            new ChartComboSeriesInput(0, false, false),
            new ChartComboSeriesInput(1, false, false),
        });

        var options = ChartComboPlanner.Plan(input);

        options.ComboLineSeriesIndexes.Should().BeEmpty();
        options.UseComboLineForSecondarySeries.Should().BeFalse();
        options.SecondaryAxisSeriesIndexes.Should().BeEmpty();
        options.ShowSecondaryAxis.Should().BeFalse();
    }

    [Fact]
    public void Combo_Plan_AppliesToChart_ViaSetChartLayoutCommand()
    {
        var chart = new ChartModel { Type = ChartType.Column };
        var input = new ChartComboInput(new[]
        {
            new ChartComboSeriesInput(0, false, false),
            new ChartComboSeriesInput(1, AsLine: true, OnSecondaryAxis: false),
        });

        ApplyLayout(chart, ChartComboPlanner.Plan(input));

        chart.ComboLineSeriesIndexes.Should().Contain(1);
        chart.UseComboLineForSecondarySeries.Should().BeTrue();
    }

    // ---- ChartMovePlanner ----------------------------------------------------------------------------

    [Fact]
    public void Move_Plan_TrimsName_AndAllowsNewSheetWithoutResolving()
    {
        var plan = ChartMovePlanner.Plan(
            new ChartMoveInput(ChartMoveTargetKind.NewSheet, "  Chart1  "),
            _ => false);

        plan.IsValid.Should().BeTrue();
        plan.TargetName.Should().Be("Chart1");
        plan.TargetKind.Should().Be(ChartMoveTargetKind.NewSheet);
    }

    [Fact]
    public void Move_Plan_RejectsBlankName()
    {
        var plan = ChartMovePlanner.Plan(
            new ChartMoveInput(ChartMoveTargetKind.ObjectInSheet, "   "),
            _ => true);

        plan.IsValid.Should().BeFalse();
        plan.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Move_Plan_RejectsExistingSheetTarget_WhenNameDoesNotResolve()
    {
        var plan = ChartMovePlanner.Plan(
            new ChartMoveInput(ChartMoveTargetKind.ObjectInSheet, "Ghost"),
            name => name == "Sheet1");

        plan.IsValid.Should().BeFalse();
        plan.Error.Should().Contain("Ghost");
    }

    [Fact]
    public void Move_Plan_AcceptsExistingSheetTarget_WhenNameResolves()
    {
        var plan = ChartMovePlanner.Plan(
            new ChartMoveInput(ChartMoveTargetKind.ObjectInSheet, "Sheet1"),
            name => name == "Sheet1");

        plan.IsValid.Should().BeTrue();
        plan.TargetName.Should().Be("Sheet1");
    }

    // ---- ChartAreaFormatPlanner ----------------------------------------------------------------------

    [Fact]
    public void ChartArea_Read_CapturesFillAndBorderState()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            ChartAreaFillColor = new CellColor(10, 20, 30),
            PlotAreaFillColor = new CellColor(40, 50, 60),
            PlotAreaBorderColor = new CellColor(70, 80, 90),
            PlotAreaBorderThickness = 2.5,
        };

        var input = ChartAreaFormatPlanner.Read(chart);

        input.ChartAreaFillColor.Should().Be(new CellColor(10, 20, 30));
        input.PlotAreaFillColor.Should().Be(new CellColor(40, 50, 60));
        input.PlotAreaBorderColor.Should().Be(new CellColor(70, 80, 90));
        input.PlotAreaBorderThickness.Should().Be(2.5);
    }

    [Fact]
    public void ChartArea_Validate_RejectsOutOfRangeOrNonFiniteWidth()
    {
        ChartAreaFormatPlanner.Validate(new ChartAreaFormatInput(null, null, null, -1)).Should().NotBeNull();
        ChartAreaFormatPlanner.Validate(new ChartAreaFormatInput(null, null, null, 99)).Should().NotBeNull();
        ChartAreaFormatPlanner.Validate(new ChartAreaFormatInput(null, null, null, double.NaN)).Should().NotBeNull();
        ChartAreaFormatPlanner.Validate(new ChartAreaFormatInput(null, null, null, 1.5)).Should().BeNull();
    }

    [Fact]
    public void ChartArea_Plan_AppliesFillAndBorder_ViaSetChartLayoutCommand()
    {
        var chart = new ChartModel { Type = ChartType.Column };
        var options = ChartAreaFormatPlanner.Plan(new ChartAreaFormatInput(
            new CellColor(1, 2, 3), new CellColor(4, 5, 6), new CellColor(7, 8, 9), 3));

        ApplyLayout(chart, options);

        chart.ChartAreaFillColor.Should().Be(new CellColor(1, 2, 3));
        chart.PlotAreaFillColor.Should().Be(new CellColor(4, 5, 6));
        chart.PlotAreaBorderColor.Should().Be(new CellColor(7, 8, 9));
        chart.PlotAreaBorderThickness.Should().Be(3);
    }

    // ---- ChartBarFormatPlanner -----------------------------------------------------------------------

    [Fact]
    public void BarFormat_Supports_OnlyBarColumnFamilies()
    {
        ChartBarFormatPlanner.Supports(new ChartModel { Type = ChartType.Column }).Should().BeTrue();
        ChartBarFormatPlanner.Supports(new ChartModel { Type = ChartType.Bar }).Should().BeTrue();
        ChartBarFormatPlanner.Supports(new ChartModel { Type = ChartType.Line }).Should().BeFalse();
    }

    [Fact]
    public void BarFormat_Read_FallsBackToExcelDefaults()
    {
        var read = ChartBarFormatPlanner.Read(new ChartModel { Type = ChartType.Column });
        read.Should().Be(new ChartBarFormatInput(150, 0));
    }

    [Fact]
    public void BarFormat_Plan_ClampsAndApplies()
    {
        var chart = new ChartModel { Type = ChartType.Column };
        var options = ChartBarFormatPlanner.Plan(new ChartBarFormatInput(600, -200));
        options.BarGapWidth.Should().Be(ChartBarFormatPlanner.MaxGapWidth);
        options.BarOverlap.Should().Be(ChartBarFormatPlanner.MinOverlap);
        ApplyLayout(chart, options);
        chart.BarGapWidth.Should().Be(ChartBarFormatPlanner.MaxGapWidth);
        chart.BarOverlap.Should().Be(ChartBarFormatPlanner.MinOverlap);
    }

    [Fact]
    public void BarFormat_Validate_RejectsOutOfRange()
    {
        ChartBarFormatPlanner.Validate(new ChartBarFormatInput(100, 0)).Should().BeNull();
        ChartBarFormatPlanner.Validate(new ChartBarFormatInput(600, 0)).Should().NotBeNull();
        ChartBarFormatPlanner.Validate(new ChartBarFormatInput(100, 200)).Should().NotBeNull();
    }

    // ---- ChartPieFormatPlanner -----------------------------------------------------------------------

    [Fact]
    public void PieFormat_Supports_AndHoleSizeGating()
    {
        ChartPieFormatPlanner.Supports(new ChartModel { Type = ChartType.Pie }).Should().BeTrue();
        ChartPieFormatPlanner.Supports(new ChartModel { Type = ChartType.Doughnut }).Should().BeTrue();
        ChartPieFormatPlanner.Supports(new ChartModel { Type = ChartType.Column }).Should().BeFalse();
        ChartPieFormatPlanner.SupportsHoleSize(new ChartModel { Type = ChartType.Doughnut }).Should().BeTrue();
        ChartPieFormatPlanner.SupportsHoleSize(new ChartModel { Type = ChartType.Pie }).Should().BeFalse();
    }

    [Fact]
    public void PieFormat_Plan_ClampsAndApplies()
    {
        var chart = new ChartModel { Type = ChartType.Doughnut };
        var options = ChartPieFormatPlanner.Plan(new ChartPieFormatInput(400, 1, 0.9, 0.05));
        ApplyLayout(chart, options);
        chart.FirstSliceAngle.Should().Be(ChartPieFormatPlanner.MaxFirstSliceAngle);
        chart.ExplodedSliceIndex.Should().Be(1);
        chart.ExplodedSliceDistance.Should().Be(ChartPieFormatPlanner.MaxExplodedDistance);
        chart.DoughnutHoleSize.Should().Be(ChartPieFormatPlanner.MinHoleSize);
    }

    [Fact]
    public void PieFormat_Validate_RejectsOutOfRange()
    {
        ChartPieFormatPlanner.Validate(new ChartPieFormatInput(90, 0, 0.2, 0.5)).Should().BeNull();
        ChartPieFormatPlanner.Validate(new ChartPieFormatInput(400, 0, 0.2, 0.5)).Should().NotBeNull();
        ChartPieFormatPlanner.Validate(new ChartPieFormatInput(90, 0, 0.9, 0.5)).Should().NotBeNull();
        ChartPieFormatPlanner.Validate(new ChartPieFormatInput(90, 0, 0.2, 0.95)).Should().NotBeNull();
    }

    // ---- ChartBubbleFormatPlanner --------------------------------------------------------------------

    [Fact]
    public void BubbleFormat_Supports_OnlyBubble()
    {
        ChartBubbleFormatPlanner.Supports(new ChartModel { Type = ChartType.Bubble }).Should().BeTrue();
        ChartBubbleFormatPlanner.Supports(new ChartModel { Type = ChartType.Scatter }).Should().BeFalse();
    }

    [Fact]
    public void BubbleFormat_Plan_ClampsAndApplies()
    {
        var chart = new ChartModel { Type = ChartType.Bubble };
        var options = ChartBubbleFormatPlanner.Plan(new ChartBubbleFormatInput(500, true, ChartBubbleSizeRepresents.Width));
        options.BubbleScale.Should().Be(ChartBubbleFormatPlanner.MaxBubbleScale);
        ApplyLayout(chart, options);
        chart.BubbleScale.Should().Be(ChartBubbleFormatPlanner.MaxBubbleScale);
        chart.ShowNegativeBubbles.Should().BeTrue();
        chart.BubbleSizeRepresents.Should().Be(ChartBubbleSizeRepresents.Width);
    }

    // ---- ChartStockFormatPlanner ---------------------------------------------------------------------

    [Fact]
    public void StockFormat_Supports_OnlyStock()
    {
        ChartStockFormatPlanner.Supports(new ChartModel { Type = ChartType.Stock }).Should().BeTrue();
        ChartStockFormatPlanner.Supports(new ChartModel { Type = ChartType.Line }).Should().BeFalse();
    }

    [Fact]
    public void StockFormat_Plan_ClampsAndApplies()
    {
        var chart = new ChartModel { Type = ChartType.Stock };
        var up = new CellColor(10, 20, 30);
        var options = ChartStockFormatPlanner.Plan(new ChartStockFormatInput(600, up, null, null, null, null, 50));
        options.UpDownBarGapWidth.Should().Be(ChartStockFormatPlanner.MaxGapWidth);
        ApplyLayout(chart, options);
        chart.UpDownBarGapWidth.Should().Be(ChartStockFormatPlanner.MaxGapWidth);
        chart.UpBarFillColor.Should().Be(up);
        chart.HighLowLineThickness.Should().Be(ChartStockFormatPlanner.MaxLineThickness);
    }

    [Fact]
    public void StockFormat_Validate_RejectsOutOfRange()
    {
        ChartStockFormatPlanner.Validate(new ChartStockFormatInput(100, null, null, null, null, null, 1)).Should().BeNull();
        ChartStockFormatPlanner.Validate(new ChartStockFormatInput(600, null, null, null, null, null, 1)).Should().NotBeNull();
        ChartStockFormatPlanner.Validate(new ChartStockFormatInput(100, null, null, null, null, null, 50)).Should().NotBeNull();
    }

    // ---- ChartQuickFormatCycler ----------------------------------------------------------------------

    [Fact]
    public void QuickCycler_SeriesColor_WalksPalette()
    {
        ChartQuickFormatCycler.DefaultSeriesColor.Should().Be(new CellColor(0, 114, 178));
        var first = ChartQuickFormatCycler.NextSeriesColor(null);
        first.Should().Be(ChartQuickFormatCycler.DefaultSeriesColor);
        ChartQuickFormatCycler.NextSeriesColor(first).Should().Be(new CellColor(213, 94, 0));
    }

    [Theory]
    [InlineData(ChartDataLabelPosition.BestFit, ChartDataLabelPosition.OutsideEnd)]
    [InlineData(ChartDataLabelPosition.OutsideEnd, ChartDataLabelPosition.InsideEnd)]
    [InlineData(ChartDataLabelPosition.InsideEnd, ChartDataLabelPosition.Center)]
    [InlineData(ChartDataLabelPosition.Center, ChartDataLabelPosition.BestFit)]
    public void QuickCycler_DataLabelPosition_CyclesExcelLikePositions(
        ChartDataLabelPosition current,
        ChartDataLabelPosition expected)
    {
        ChartQuickFormatCycler.NextDataLabelPosition(current).Should().Be(expected);
    }

    [Fact]
    public void QuickCycler_FontSizes_StepAndWrap()
    {
        ChartQuickFormatCycler.NextChartTitleFontSize(16).Should().Be(18);
        ChartQuickFormatCycler.NextChartTitleFontSize(24).Should().Be(12);
        ChartQuickFormatCycler.NextAxisTitleFontSize(18).Should().Be(9);
        ChartQuickFormatCycler.NextLegendFontSize(16).Should().Be(9);
        ChartQuickFormatCycler.NextDataLabelBorderThickness(0.75).Should().Be(1.5);
        ChartQuickFormatCycler.NextDataLabelBorderThickness(3).Should().Be(0.75);
    }

    [Fact]
    public void QuickCycler_GridlineState_CyclesOffMajorMajorMinor()
    {
        ChartQuickFormatCycler.NextGridlineState(false, false).Should().Be((true, false));
        ChartQuickFormatCycler.NextGridlineState(true, false).Should().Be((true, true));
        ChartQuickFormatCycler.NextGridlineState(true, true).Should().Be((false, false));
    }

    [Fact]
    public void QuickCycler_SeriesDash_Cycles()
    {
        ChartQuickFormatCycler.NextSeriesDash(null).Should().Be(ChartLineDashStyle.Dash);
        ChartQuickFormatCycler.NextSeriesDash(ChartLineDashStyle.Dash).Should().Be(ChartLineDashStyle.Dot);
        ChartQuickFormatCycler.NextSeriesDash(ChartLineDashStyle.Dot).Should().Be(ChartLineDashStyle.Solid);
        ChartQuickFormatCycler.NextSeriesDash(ChartLineDashStyle.Solid).Should().BeNull();
    }

    [Fact]
    public void QuickCycler_MarkerSize_StepAndWrap()
    {
        ChartQuickFormatCycler.NextMarkerSize(null).Should().Be(5);
        ChartQuickFormatCycler.NextMarkerSize(5).Should().Be(7);
        ChartQuickFormatCycler.NextMarkerSize(12).Should().Be(5);
    }

    [Fact]
    public void QuickCycler_MergeFirstSeriesFormat_ReplacesOrAppends_AndPreservesOthers()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Line,
            SeriesFormats = { new ChartSeriesFormat(0), new ChartSeriesFormat(1) { MarkerSize = 3 } },
        };
        var updated = ChartQuickFormatCycler.ReadFirstSeriesFormat(chart) with { DashStyle = ChartLineDashStyle.Dot };
        var merged = ChartQuickFormatCycler.MergeFirstSeriesFormat(chart, updated);
        merged.Should().HaveCount(2);
        merged.Single(f => f.SeriesIndex == 0).DashStyle.Should().Be(ChartLineDashStyle.Dot);
        merged.Single(f => f.SeriesIndex == 1).MarkerSize.Should().Be(3);
    }

    [Fact]
    public void QuickCycler_ComboLineSeries_StepsAndClears()
    {
        var chart = MakeChartWithSeries(ChartType.Column, columns: 4); // 3 series: 0,1,2
        ChartQuickFormatCycler.NextComboLineSeries(chart).Should().Equal(1);
        chart.UseComboLineForSecondarySeries = true;
        chart.ComboLineSeriesIndexes.Add(1);
        ChartQuickFormatCycler.NextComboLineSeries(chart).Should().Equal(2);
        chart.ComboLineSeriesIndexes.Clear();
        chart.ComboLineSeriesIndexes.Add(2);
        ChartQuickFormatCycler.NextComboLineSeries(chart).Should().BeEmpty();
    }

    private static ChartModel MakeChartWithSeries(ChartType type, int columns)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Series");
        return new ChartModel
        {
            Type = type,
            FirstColIsCategories = true,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 6, (uint)columns)),
        };
    }

    private static void ApplyLayout(ChartModel chart, ChartLayoutOptions options)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        if (chart.DataRange.Start.Sheet != sheet.Id)
        {
            chart.DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 3));
        }

        sheet.Charts.Add(chart);
        var ctx = new InMemoryCommandContext(workbook);
        var outcome = new SetChartLayoutCommand(sheet.Id, chart.Id, options).Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
    }

    private sealed class InMemoryCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }
}
