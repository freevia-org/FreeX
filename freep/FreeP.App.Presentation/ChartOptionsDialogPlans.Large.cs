namespace FreeP.App.Compositor;

public static partial class ChartOptionsDialogPlanCatalog
{
    public static ChartOptionsDialogPlan BuildDialogPlan(
        this ChartAxisOptionsDialogSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var state = session.State;
        var surface = session.Surface;
        return ScrollablePlan(
            surface.CommandId,
            surface.Title,
            ChartAxisOptionsPlanner.DefaultDialogWidth,
            ChartAxisOptionsPlanner.DefaultDialogHeight + 150,
            400,
            360,
            surface.AutoHint,
            surface.OkLabel,
            surface.CancelLabel,
            Group("axis", null, [
                Choice(ChartOptionsDialogFieldId.Axis, surface.AxisLabel, state.AxisIndex, session.AxisOptions),
                Toggle(ChartOptionsDialogFieldId.ShowAxis, surface.ShowAxisLabel, state.ShowAxis),
            ]),
            Group("axis-title", "Axis title", [
                Text(ChartOptionsDialogFieldId.AxisTitle, surface.AxisTitleLabel, state.Title),
                Text(ChartOptionsDialogFieldId.AxisTitleFontFamily, surface.AxisTitleFontFamilyLabel, state.TitleFontFamily),
                Text(ChartOptionsDialogFieldId.AxisTitleFontSize, surface.AxisTitleFontSizeLabel, state.TitleFontSizeText),
                Text(ChartOptionsDialogFieldId.AxisTitleColor, surface.AxisTitleColorLabel, state.TitleColor),
                Toggle(ChartOptionsDialogFieldId.AxisTitleBold, surface.AxisTitleBoldLabel, state.TitleBold, isThreeState: true),
                Toggle(ChartOptionsDialogFieldId.AxisTitleItalic, surface.AxisTitleItalicLabel, state.TitleItalic, isThreeState: true),
            ]),
            Group("axis-scale", "Scale and number", [
                Text(ChartOptionsDialogFieldId.Minimum, surface.MinimumLabel, state.MinimumText),
                Text(ChartOptionsDialogFieldId.Maximum, surface.MaximumLabel, state.MaximumText),
                Text(ChartOptionsDialogFieldId.MajorUnit, surface.MajorUnitLabel, state.MajorUnitText),
                Text(ChartOptionsDialogFieldId.MinorUnit, surface.MinorUnitLabel, state.MinorUnitText),
                Text(ChartOptionsDialogFieldId.NumberFormat, surface.NumberFormatLabel, state.NumberFormatCode),
                Choice(ChartOptionsDialogFieldId.DisplayUnit, surface.DisplayUnitLabel, state.DisplayUnitIndex, session.DisplayUnitOptions),
                Text(ChartOptionsDialogFieldId.CustomDisplayUnit, "Custom divisor", state.CustomDisplayUnitText),
            ]),
            Group("axis-gridlines", "Gridlines", [
                Toggle(ChartOptionsDialogFieldId.MajorGridlines, surface.MajorGridlinesLabel, state.MajorGridlines),
                Toggle(ChartOptionsDialogFieldId.MinorGridlines, surface.MinorGridlinesLabel, state.MinorGridlines),
            ]),
            Group("axis-labels", "Ticks, labels, and crossing", [
                Choice(ChartOptionsDialogFieldId.MajorTickMark, surface.MajorTickMarkLabel, state.MajorTickMarkIndex, session.TickMarkOptions),
                Choice(ChartOptionsDialogFieldId.MinorTickMark, surface.MinorTickMarkLabel, state.MinorTickMarkIndex, session.TickMarkOptions),
                Choice(ChartOptionsDialogFieldId.TickLabelPosition, surface.TickLabelPositionLabel, state.TickLabelPositionIndex, session.TickLabelPositionOptions),
                Choice(ChartOptionsDialogFieldId.Crossing, surface.CrossingLabel, state.CrossingIndex, session.CrossingOptions),
                Text(ChartOptionsDialogFieldId.CrossesAt, surface.CrossesAtLabel, state.CrossesAtText),
                Choice(ChartOptionsDialogFieldId.CrossBetween, surface.CrossBetweenLabel, state.CrossBetweenIndex, session.CrossBetweenOptions),
                Choice(ChartOptionsDialogFieldId.LabelAlignment, surface.LabelAlignmentLabel, state.LabelAlignmentIndex, session.LabelAlignmentOptions),
                Text(ChartOptionsDialogFieldId.LabelOffset, surface.LabelOffsetLabel, state.LabelOffsetText),
                Choice(ChartOptionsDialogFieldId.MultiLevelLabels, surface.MultiLevelLabelsLabel, state.MultiLevelLabelsIndex, session.MultiLevelLabelsOptions),
                Choice(ChartOptionsDialogFieldId.AutoCrossing, surface.AutoCrossingLabel, state.AutoCrossingIndex, session.AutoCrossingOptions),
                Toggle(ChartOptionsDialogFieldId.ReverseOrder, surface.ReverseOrderLabel, state.ReverseOrder),
            ]));
    }

    public static ChartOptionsDialogPlan BuildDialogPlan(
        this ChartDisplayOptionsDialogSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var state = session.State;
        var surface = session.Surface;
        return ScrollablePlan(
            surface.CommandId,
            surface.Title,
            ChartDisplayOptionsPlanner.DefaultDialogWidth,
            ChartDisplayOptionsPlanner.DefaultDialogHeight,
            380,
            500,
            surface.PlotHint,
            surface.OkLabel,
            surface.CancelLabel,
            Group("chart-title", "Title", [
                Text(ChartOptionsDialogFieldId.ChartTitle, surface.ChartTitleLabel, state.Title),
                Toggle(ChartOptionsDialogFieldId.TitleOverlay, surface.TitleOverlayLabel, state.TitleOverlay),
                Choice(ChartOptionsDialogFieldId.TitlePosition, surface.TitlePositionLabel, state.TitlePositionIndex, session.TitlePositionOptions, isEnabled: state.SupportsChartExTitleLayout),
                Choice(ChartOptionsDialogFieldId.TitleAlignment, surface.TitleAlignmentLabel, state.TitleAlignmentIndex, session.TitleAlignmentOptions, isEnabled: state.SupportsChartExTitleLayout),
            ]),
            Group("chart-display", "Chart", [
                Toggle(ChartOptionsDialogFieldId.PlotVisibleOnly, surface.PlotVisibleOnlyLabel, state.PlotVisibleOnly),
                Toggle(ChartOptionsDialogFieldId.RoundedCorners, surface.RoundedCornersLabel, state.RoundedCorners),
                Choice(ChartOptionsDialogFieldId.ChartStyle, surface.ChartStyleLabel, state.StyleIndex, session.StyleOptions),
                Choice(ChartOptionsDialogFieldId.Legend, surface.LegendLabel, state.LegendIndex, session.LegendOptions),
                Toggle(ChartOptionsDialogFieldId.VaryColors, surface.VaryColorsLabel, state.VaryColors),
                Toggle(ChartOptionsDialogFieldId.LegendOverlay, surface.LegendOverlayLabel, state.LegendOverlay, isThreeState: true),
            ]),
            Group("data-label-content", "Data label content", [
                Toggle(ChartOptionsDialogFieldId.ValueLabels, surface.ValueLabelsLabel, state.ShowValueLabels),
                Toggle(ChartOptionsDialogFieldId.PercentLabels, surface.PercentLabelsLabel, state.ShowPercentLabels),
                Toggle(ChartOptionsDialogFieldId.CategoryLabels, surface.CategoryLabelsLabel, state.ShowCategoryLabels),
                Toggle(ChartOptionsDialogFieldId.SeriesLabels, surface.SeriesLabelsLabel, state.ShowSeriesLabels),
                Toggle(ChartOptionsDialogFieldId.LegendKeys, surface.LegendKeysLabel, state.ShowLegendKeys),
                Toggle(ChartOptionsDialogFieldId.BubbleSizeLabels, surface.BubbleSizeLabelsLabel, state.ShowBubbleSize),
                Toggle(ChartOptionsDialogFieldId.LeaderLines, surface.LeaderLinesLabel, state.ShowLeaderLines, isEnabled: state.SupportsLeaderLines, isThreeState: true),
            ]),
            Group("data-label-style", "Data label style", [
                Choice(ChartOptionsDialogFieldId.LabelPosition, surface.LabelPositionLabel, state.LabelPositionIndex, session.LabelPositionOptions),
                Text(ChartOptionsDialogFieldId.LabelNumberFormat, surface.NumberFormatLabel, state.LabelNumberFormat),
                Text(ChartOptionsDialogFieldId.LabelSeparator, surface.SeparatorLabel, state.LabelSeparator),
                Text(ChartOptionsDialogFieldId.LabelFontFamily, surface.FontFamilyLabel, state.LabelFontFamily),
                Text(ChartOptionsDialogFieldId.LabelFontSize, surface.FontSizeLabel, state.LabelFontSizeText),
                Toggle(ChartOptionsDialogFieldId.LabelBold, surface.BoldLabel, state.LabelBold, isThreeState: true),
                Toggle(ChartOptionsDialogFieldId.LabelItalic, surface.ItalicLabel, state.LabelItalic, isThreeState: true),
                Text(ChartOptionsDialogFieldId.LabelColor, surface.LabelColorLabel, state.LabelColor),
            ]),
            Group("plot", "Plot", [
                Toggle(ChartOptionsDialogFieldId.CategoryGridlines, surface.CategoryGridlinesLabel, state.CategoryGridlines),
                Toggle(ChartOptionsDialogFieldId.ValueGridlines, surface.ValueGridlinesLabel, state.ValueGridlines),
                Text(ChartOptionsDialogFieldId.BarGapWidth, surface.BarGapWidthLabel, state.BarGapWidthText),
                Text(ChartOptionsDialogFieldId.BarOverlap, surface.BarOverlapLabel, state.BarOverlapText),
                Choice(ChartOptionsDialogFieldId.DisplayBlanks, surface.DisplayBlanksAsLabel, state.DisplayBlanksIndex, session.DisplayBlanksOptions),
                Toggle(ChartOptionsDialogFieldId.ShowDataLabelsOverMaximum, surface.ShowDataLabelsOverMaximumLabel, state.ShowDataLabelsOverMaximum, isThreeState: true),
                Toggle(ChartOptionsDialogFieldId.HighLowLines, surface.HighLowLinesLabel, state.HighLowLines, isEnabled: state.SupportsHighLowLines, isThreeState: true),
                Toggle(ChartOptionsDialogFieldId.WaterfallConnectorLines, surface.WaterfallConnectorLinesLabel, state.WaterfallConnectorLines, isEnabled: state.SupportsWaterfallConnectorLines, isThreeState: true),
                Toggle(ChartOptionsDialogFieldId.DropLines, surface.DropLinesLabel, state.DropLines, isEnabled: state.SupportsDropLines, isThreeState: true),
                Toggle(ChartOptionsDialogFieldId.UpDownBars, surface.UpDownBarsLabel, state.UpDownBars, isEnabled: state.SupportsUpDownBars, isThreeState: true),
                Toggle(ChartOptionsDialogFieldId.SeriesLines, surface.SeriesLinesLabel, state.SeriesLines, isEnabled: state.SupportsSeriesLines, isThreeState: true),
            ]));
    }

    public static ChartOptionsDialogPlan BuildDialogPlan(
        this ChartPointOptionsDialogSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var state = session.State;
        var surface = session.Surface;
        return ScrollablePlan(
            surface.CommandId,
            surface.Title,
            ChartPointOptionsPlanner.DefaultDialogWidth,
            ChartPointOptionsPlanner.DefaultDialogHeight,
            420,
            500,
            surface.AutoHint,
            surface.OkLabel,
            surface.CancelLabel,
            Group("point-selection", null, [
                Choice(ChartOptionsDialogFieldId.Series, surface.SeriesLabel, state.SeriesIndex, Labels(state.SeriesOptions, option => option.Label)),
                Choice(ChartOptionsDialogFieldId.Point, surface.PointLabel, state.PointIndex, Labels(state.PointOptions, option => option.Label)),
            ]),
            Group("point-appearance", "Point appearance", [
                Text(ChartOptionsDialogFieldId.FillColor, surface.FillColorLabel, state.FillColorText),
                Text(ChartOptionsDialogFieldId.StrokeColor, surface.StrokeColorLabel, state.StrokeColorText),
                Text(ChartOptionsDialogFieldId.StrokeWidth, surface.StrokeWidthLabel, state.StrokeWidthText),
                Choice(ChartOptionsDialogFieldId.Marker, surface.MarkerLabel, state.MarkerIndex, Labels(session.MarkerOptions, option => option.Label)),
                Text(ChartOptionsDialogFieldId.MarkerSize, surface.MarkerSizeLabel, state.MarkerSizeText),
                Text(ChartOptionsDialogFieldId.Explosion, surface.ExplosionLabel, state.ExplosionText),
            ]),
            Group("point-label-content", "Point data labels", [
                Toggle(ChartOptionsDialogFieldId.UsePointDataLabels, surface.PointDataLabelsLabel, state.UsePointDataLabels),
                Toggle(ChartOptionsDialogFieldId.ValueLabels, surface.ValueLabelsLabel, state.ShowValueLabels),
                Toggle(ChartOptionsDialogFieldId.PercentLabels, surface.PercentLabelsLabel, state.ShowPercentLabels),
                Toggle(ChartOptionsDialogFieldId.CategoryLabels, surface.CategoryLabelsLabel, state.ShowCategoryLabels),
                Toggle(ChartOptionsDialogFieldId.SeriesLabels, surface.SeriesLabelsLabel, state.ShowSeriesLabels),
                Toggle(ChartOptionsDialogFieldId.LegendKeys, surface.LegendKeysLabel, state.ShowLegendKeys),
                Toggle(ChartOptionsDialogFieldId.BubbleSizeLabels, surface.BubbleSizeLabelsLabel, state.ShowBubbleSize),
                Toggle(ChartOptionsDialogFieldId.LeaderLines, ChartSeriesOptionsPlanner.LeaderLinesLabel, state.ShowLeaderLines, isThreeState: true),
            ]),
            Group("point-label-style", "Data label style", [
                Choice(ChartOptionsDialogFieldId.LabelPosition, surface.LabelPositionLabel, state.LabelPositionIndex, Labels(session.LabelPositionOptions, option => option.Label)),
                Text(ChartOptionsDialogFieldId.LabelNumberFormat, surface.NumberFormatLabel, state.LabelNumberFormat),
                Text(ChartOptionsDialogFieldId.LabelSeparator, surface.SeparatorLabel, state.LabelSeparator),
                Text(ChartOptionsDialogFieldId.LabelFontFamily, surface.FontFamilyLabel, state.LabelFontFamily),
                Text(ChartOptionsDialogFieldId.LabelFontSize, surface.FontSizeLabel, state.LabelFontSizeText),
                Toggle(ChartOptionsDialogFieldId.LabelBold, surface.BoldLabel, state.LabelBold, isThreeState: true),
                Toggle(ChartOptionsDialogFieldId.LabelItalic, surface.ItalicLabel, state.LabelItalic, isThreeState: true),
                Text(ChartOptionsDialogFieldId.LabelColor, surface.LabelColorLabel, state.LabelColorText),
            ]));
    }

    public static ChartOptionsDialogPlan BuildDialogPlan(
        this ChartSeriesOptionsDialogSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var state = session.State;
        var surface = session.Surface;
        return ScrollablePlan(
            surface.CommandId,
            surface.Title,
            ChartSeriesOptionsPlanner.DefaultDialogWidth,
            ChartSeriesOptionsPlanner.DefaultDialogHeight,
            420,
            500,
            surface.AutoHint,
            surface.OkLabel,
            surface.CancelLabel,
            Group("series-selection", null, [
                Choice(ChartOptionsDialogFieldId.Series, surface.SeriesLabel, state.SeriesIndex, Labels(state.SeriesOptions, option => option.Label), 160),
                Choice(ChartOptionsDialogFieldId.SeriesChartType, surface.SeriesChartTypeLabel, state.SeriesChartTypeIndex, Labels(state.SeriesChartTypeOptions, option => option.Label), 160),
                Toggle(ChartOptionsDialogFieldId.SecondaryAxis, surface.SecondaryAxisLabel, state.OnSecondaryAxis),
            ]),
            Group("series-appearance", "Series appearance", [
                Toggle(ChartOptionsDialogFieldId.SmoothLine, surface.SmoothLineLabel, state.SmoothLine),
                Toggle(ChartOptionsDialogFieldId.InvertIfNegative, surface.InvertIfNegativeLabel, state.InvertIfNegative, isThreeState: true),
                Text(ChartOptionsDialogFieldId.LineWidth, surface.LineWidthLabel, state.LineWidthText, 160),
                Text(ChartOptionsDialogFieldId.LineColor, surface.LineColorLabel, state.LineColorText, 160),
                Choice(ChartOptionsDialogFieldId.LineDash, surface.LineDashLabel, state.LineDashIndex, Labels(session.DashOptions, option => option.Label), 160),
                Toggle(ChartOptionsDialogFieldId.NoLine, surface.NoLineLabel, state.NoLine),
                Text(ChartOptionsDialogFieldId.FillColor, surface.FillColorLabel, state.FillColorText, 160),
                Choice(ChartOptionsDialogFieldId.Marker, surface.MarkerLabel, state.MarkerIndex, Labels(session.MarkerOptions, option => option.Label), 160),
                Text(ChartOptionsDialogFieldId.MarkerSize, surface.MarkerSizeLabel, state.MarkerSizeText, 160),
            ]),
            Group("series-label-content", "Series data labels", [
                Toggle(ChartOptionsDialogFieldId.UseSeriesDataLabels, surface.SeriesDataLabelsLabel, state.UseSeriesDataLabels),
                Toggle(ChartOptionsDialogFieldId.ValueLabels, surface.ValueLabelsLabel, state.ShowValueLabels),
                Toggle(ChartOptionsDialogFieldId.PercentLabels, surface.PercentLabelsLabel, state.ShowPercentLabels),
                Toggle(ChartOptionsDialogFieldId.CategoryLabels, surface.CategoryLabelsLabel, state.ShowCategoryLabels),
                Toggle(ChartOptionsDialogFieldId.SeriesLabels, surface.SeriesLabelsLabel, state.ShowSeriesLabels),
                Toggle(ChartOptionsDialogFieldId.LegendKeys, surface.LegendKeysLabel, state.ShowLegendKeys),
                Toggle(ChartOptionsDialogFieldId.BubbleSizeLabels, surface.BubbleSizeLabelsLabel, state.ShowBubbleSize),
                Toggle(ChartOptionsDialogFieldId.LeaderLines, session.LeaderLinesLabel, state.ShowLeaderLines, isThreeState: true),
            ]),
            Group("series-label-style", "Data label style", [
                Choice(ChartOptionsDialogFieldId.LabelPosition, surface.LabelPositionLabel, state.LabelPositionIndex, Labels(session.LabelPositionOptions, option => option.Label), 160),
                Text(ChartOptionsDialogFieldId.LabelNumberFormat, surface.NumberFormatLabel, state.LabelNumberFormat, 160),
                Text(ChartOptionsDialogFieldId.LabelSeparator, surface.SeparatorLabel, state.LabelSeparator, 160),
                Text(ChartOptionsDialogFieldId.LabelFontFamily, surface.FontFamilyLabel, state.LabelFontFamily, 160),
                Text(ChartOptionsDialogFieldId.LabelFontSize, surface.FontSizeLabel, state.LabelFontSizeText, 160),
                Toggle(ChartOptionsDialogFieldId.LabelBold, surface.BoldLabel, state.LabelBold, isThreeState: true),
                Toggle(ChartOptionsDialogFieldId.LabelItalic, surface.ItalicLabel, state.LabelItalic, isThreeState: true),
                Text(ChartOptionsDialogFieldId.LabelColor, surface.LabelColorLabel, state.LabelColorText, 160),
            ]),
            Group("series-error-bars", "Error bars", [
                Toggle(ChartOptionsDialogFieldId.ErrorBars, session.ErrorBarsLabel, state.ErrorBarsEnabled),
                Choice(ChartOptionsDialogFieldId.ErrorDirection, session.ErrorDirectionLabel, state.ErrorDirectionIndex, Labels(session.ErrorDirectionOptions, option => option.Label), 160),
                Choice(ChartOptionsDialogFieldId.ErrorBarType, session.ErrorBarTypeLabel, state.ErrorBarTypeIndex, Labels(session.ErrorBarTypeOptions, option => option.Label), 160),
                Choice(ChartOptionsDialogFieldId.ErrorValueType, session.ErrorValueTypeLabel, state.ErrorValueTypeIndex, Labels(session.ErrorValueTypeOptions, option => option.Label), 160),
                Text(ChartOptionsDialogFieldId.ErrorValue, session.ErrorValueLabel, state.ErrorValueText, 160),
                Toggle(ChartOptionsDialogFieldId.ErrorNoEndCap, session.ErrorNoEndCapLabel, state.ErrorNoEndCap),
            ]),
            Group("series-trendline", "Trendline", [
                Toggle(ChartOptionsDialogFieldId.Trendline, session.TrendlineLabel, state.TrendlineEnabled),
                Choice(ChartOptionsDialogFieldId.TrendlineType, session.TrendlineTypeLabel, state.TrendlineTypeIndex, Labels(session.TrendlineTypeOptions, option => option.Label), 160),
                Text(ChartOptionsDialogFieldId.TrendlineOrder, session.TrendlineOrderLabel, state.TrendlineOrderText, 160),
                Text(ChartOptionsDialogFieldId.TrendlinePeriod, session.TrendlinePeriodLabel, state.TrendlinePeriodText, 160),
                Text(ChartOptionsDialogFieldId.TrendlineForward, session.TrendlineForwardLabel, state.TrendlineForwardText, 160),
                Text(ChartOptionsDialogFieldId.TrendlineBackward, session.TrendlineBackwardLabel, state.TrendlineBackwardText, 160),
                Toggle(ChartOptionsDialogFieldId.TrendlineEquation, session.TrendlineEquationLabel, state.TrendlineEquation),
                Toggle(ChartOptionsDialogFieldId.TrendlineRSquared, session.TrendlineRSquaredLabel, state.TrendlineRSquared),
            ]));
    }

    public static ChartAxisOptionsDialogInput BuildInput(
        this ChartAxisOptionsDialogSession session,
        ChartOptionsDialogValues values) => new(
            values.SelectedIndex(ChartOptionsDialogFieldId.Axis),
            values.Text(ChartOptionsDialogFieldId.AxisTitle),
            values.Text(ChartOptionsDialogFieldId.AxisTitleFontFamily),
            values.Text(ChartOptionsDialogFieldId.AxisTitleFontSize),
            values.Text(ChartOptionsDialogFieldId.AxisTitleColor),
            values.NullableChecked(ChartOptionsDialogFieldId.AxisTitleBold),
            values.NullableChecked(ChartOptionsDialogFieldId.AxisTitleItalic),
            values.IsChecked(ChartOptionsDialogFieldId.ShowAxis),
            values.Text(ChartOptionsDialogFieldId.Minimum),
            values.Text(ChartOptionsDialogFieldId.Maximum),
            values.Text(ChartOptionsDialogFieldId.MajorUnit),
            values.Text(ChartOptionsDialogFieldId.MinorUnit),
            values.Text(ChartOptionsDialogFieldId.NumberFormat),
            values.SelectedIndex(ChartOptionsDialogFieldId.DisplayUnit),
            values.Text(ChartOptionsDialogFieldId.CustomDisplayUnit),
            values.IsChecked(ChartOptionsDialogFieldId.MajorGridlines),
            values.IsChecked(ChartOptionsDialogFieldId.MinorGridlines),
            values.SelectedIndex(ChartOptionsDialogFieldId.MajorTickMark),
            values.SelectedIndex(ChartOptionsDialogFieldId.MinorTickMark),
            values.SelectedIndex(ChartOptionsDialogFieldId.TickLabelPosition),
            values.SelectedIndex(ChartOptionsDialogFieldId.Crossing),
            values.Text(ChartOptionsDialogFieldId.CrossesAt),
            values.SelectedIndex(ChartOptionsDialogFieldId.CrossBetween),
            values.SelectedIndex(ChartOptionsDialogFieldId.LabelAlignment),
            values.Text(ChartOptionsDialogFieldId.LabelOffset),
            values.SelectedIndex(ChartOptionsDialogFieldId.MultiLevelLabels),
            values.SelectedIndex(ChartOptionsDialogFieldId.AutoCrossing),
            values.IsChecked(ChartOptionsDialogFieldId.ReverseOrder));

    public static ChartDisplayOptionsDialogInput BuildInput(
        this ChartDisplayOptionsDialogSession session,
        ChartOptionsDialogValues values) => new(
            values.Text(ChartOptionsDialogFieldId.ChartTitle),
            values.IsChecked(ChartOptionsDialogFieldId.TitleOverlay),
            values.SelectedIndex(ChartOptionsDialogFieldId.TitlePosition),
            values.SelectedIndex(ChartOptionsDialogFieldId.TitleAlignment),
            values.IsChecked(ChartOptionsDialogFieldId.PlotVisibleOnly),
            values.IsChecked(ChartOptionsDialogFieldId.RoundedCorners),
            values.SelectedIndex(ChartOptionsDialogFieldId.ChartStyle),
            values.SelectedIndex(ChartOptionsDialogFieldId.Legend),
            values.IsChecked(ChartOptionsDialogFieldId.ValueLabels),
            values.IsChecked(ChartOptionsDialogFieldId.PercentLabels),
            values.IsChecked(ChartOptionsDialogFieldId.CategoryLabels),
            values.IsChecked(ChartOptionsDialogFieldId.SeriesLabels),
            values.IsChecked(ChartOptionsDialogFieldId.LegendKeys),
            values.IsChecked(ChartOptionsDialogFieldId.BubbleSizeLabels),
            values.NullableChecked(ChartOptionsDialogFieldId.LeaderLines),
            values.Text(ChartOptionsDialogFieldId.LabelNumberFormat),
            values.Text(ChartOptionsDialogFieldId.LabelSeparator),
            values.Text(ChartOptionsDialogFieldId.LabelFontFamily),
            values.Text(ChartOptionsDialogFieldId.LabelFontSize),
            values.NullableChecked(ChartOptionsDialogFieldId.LabelBold),
            values.NullableChecked(ChartOptionsDialogFieldId.LabelItalic),
            values.Text(ChartOptionsDialogFieldId.LabelColor),
            values.SelectedIndex(ChartOptionsDialogFieldId.LabelPosition),
            values.IsChecked(ChartOptionsDialogFieldId.CategoryGridlines),
            values.IsChecked(ChartOptionsDialogFieldId.ValueGridlines),
            values.Text(ChartOptionsDialogFieldId.BarGapWidth),
            values.Text(ChartOptionsDialogFieldId.BarOverlap),
            values.SelectedIndex(ChartOptionsDialogFieldId.DisplayBlanks),
            values.NullableChecked(ChartOptionsDialogFieldId.ShowDataLabelsOverMaximum),
            values.IsChecked(ChartOptionsDialogFieldId.VaryColors),
            values.NullableChecked(ChartOptionsDialogFieldId.LegendOverlay),
            values.NullableChecked(ChartOptionsDialogFieldId.HighLowLines),
            values.NullableChecked(ChartOptionsDialogFieldId.WaterfallConnectorLines),
            values.NullableChecked(ChartOptionsDialogFieldId.DropLines),
            values.NullableChecked(ChartOptionsDialogFieldId.UpDownBars),
            values.NullableChecked(ChartOptionsDialogFieldId.SeriesLines));

    public static ChartPointOptionsDialogInput BuildInput(
        this ChartPointOptionsDialogSession session,
        ChartOptionsDialogValues values) => new(
            values.SelectedIndex(ChartOptionsDialogFieldId.Series),
            values.SelectedIndex(ChartOptionsDialogFieldId.Point),
            values.Text(ChartOptionsDialogFieldId.FillColor),
            values.Text(ChartOptionsDialogFieldId.StrokeColor),
            values.Text(ChartOptionsDialogFieldId.StrokeWidth),
            values.IsChecked(ChartOptionsDialogFieldId.UsePointDataLabels),
            values.IsChecked(ChartOptionsDialogFieldId.ValueLabels),
            values.IsChecked(ChartOptionsDialogFieldId.PercentLabels),
            values.IsChecked(ChartOptionsDialogFieldId.CategoryLabels),
            values.IsChecked(ChartOptionsDialogFieldId.SeriesLabels),
            values.IsChecked(ChartOptionsDialogFieldId.LegendKeys),
            values.IsChecked(ChartOptionsDialogFieldId.BubbleSizeLabels),
            values.NullableChecked(ChartOptionsDialogFieldId.LeaderLines),
            values.SelectedIndex(ChartOptionsDialogFieldId.LabelPosition),
            values.Text(ChartOptionsDialogFieldId.LabelNumberFormat),
            values.Text(ChartOptionsDialogFieldId.LabelSeparator),
            values.Text(ChartOptionsDialogFieldId.LabelFontFamily),
            values.Text(ChartOptionsDialogFieldId.LabelFontSize),
            values.NullableChecked(ChartOptionsDialogFieldId.LabelBold),
            values.NullableChecked(ChartOptionsDialogFieldId.LabelItalic),
            values.Text(ChartOptionsDialogFieldId.LabelColor),
            values.SelectedIndex(ChartOptionsDialogFieldId.Marker),
            values.Text(ChartOptionsDialogFieldId.MarkerSize),
            values.Text(ChartOptionsDialogFieldId.Explosion));

    public static ChartSeriesOptionsDialogInput BuildInput(
        this ChartSeriesOptionsDialogSession session,
        ChartOptionsDialogValues values) => new(
            values.SelectedIndex(ChartOptionsDialogFieldId.Series),
            values.SelectedIndex(ChartOptionsDialogFieldId.SeriesChartType),
            values.IsChecked(ChartOptionsDialogFieldId.SmoothLine),
            values.IsChecked(ChartOptionsDialogFieldId.SecondaryAxis),
            values.NullableChecked(ChartOptionsDialogFieldId.InvertIfNegative),
            values.Text(ChartOptionsDialogFieldId.LineWidth),
            values.Text(ChartOptionsDialogFieldId.LineColor),
            values.SelectedIndex(ChartOptionsDialogFieldId.LineDash),
            values.IsChecked(ChartOptionsDialogFieldId.NoLine),
            values.Text(ChartOptionsDialogFieldId.FillColor),
            values.IsChecked(ChartOptionsDialogFieldId.UseSeriesDataLabels),
            values.IsChecked(ChartOptionsDialogFieldId.ValueLabels),
            values.IsChecked(ChartOptionsDialogFieldId.PercentLabels),
            values.IsChecked(ChartOptionsDialogFieldId.CategoryLabels),
            values.IsChecked(ChartOptionsDialogFieldId.SeriesLabels),
            values.IsChecked(ChartOptionsDialogFieldId.LegendKeys),
            values.IsChecked(ChartOptionsDialogFieldId.BubbleSizeLabels),
            values.NullableChecked(ChartOptionsDialogFieldId.LeaderLines),
            values.IsChecked(ChartOptionsDialogFieldId.ErrorBars),
            values.SelectedIndex(ChartOptionsDialogFieldId.ErrorDirection),
            values.SelectedIndex(ChartOptionsDialogFieldId.ErrorBarType),
            values.SelectedIndex(ChartOptionsDialogFieldId.ErrorValueType),
            values.Text(ChartOptionsDialogFieldId.ErrorValue),
            values.IsChecked(ChartOptionsDialogFieldId.ErrorNoEndCap),
            values.IsChecked(ChartOptionsDialogFieldId.Trendline),
            values.SelectedIndex(ChartOptionsDialogFieldId.TrendlineType),
            values.Text(ChartOptionsDialogFieldId.TrendlineOrder),
            values.Text(ChartOptionsDialogFieldId.TrendlinePeriod),
            values.Text(ChartOptionsDialogFieldId.TrendlineForward),
            values.Text(ChartOptionsDialogFieldId.TrendlineBackward),
            values.IsChecked(ChartOptionsDialogFieldId.TrendlineEquation),
            values.IsChecked(ChartOptionsDialogFieldId.TrendlineRSquared),
            values.SelectedIndex(ChartOptionsDialogFieldId.LabelPosition),
            values.Text(ChartOptionsDialogFieldId.LabelNumberFormat),
            values.Text(ChartOptionsDialogFieldId.LabelSeparator),
            values.Text(ChartOptionsDialogFieldId.LabelFontFamily),
            values.Text(ChartOptionsDialogFieldId.LabelFontSize),
            values.NullableChecked(ChartOptionsDialogFieldId.LabelBold),
            values.NullableChecked(ChartOptionsDialogFieldId.LabelItalic),
            values.Text(ChartOptionsDialogFieldId.LabelColor),
            values.SelectedIndex(ChartOptionsDialogFieldId.Marker),
            values.Text(ChartOptionsDialogFieldId.MarkerSize));
}
