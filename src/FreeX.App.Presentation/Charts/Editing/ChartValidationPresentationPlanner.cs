using Free.Shared.Localization;

namespace FreeX.App.Presentation.Charts.Editing;

public static class ChartValidationPresentationPlanner
{
    public static ValidationPresentationDescriptor<ChartBarFormatDialogFieldId> Describe(ChartBarFormatParseIssue issue) =>
        issue == ChartBarFormatParseIssue.Overlap
            ? For("ChartBarFormat_InvalidOverlapMessage", ChartBarFormatDialogFieldId.Overlap)
            : For("ChartBarFormat_InvalidGapWidthMessage", ChartBarFormatDialogFieldId.GapWidth);

    public static ValidationPresentationDescriptor<ChartPieFormatDialogFieldId> Describe(ChartPieFormatParseIssue issue) =>
        issue switch
        {
            ChartPieFormatParseIssue.ExplodedSliceIndex => For("ChartPieFormat_InvalidExplodedSliceIndexMessage", ChartPieFormatDialogFieldId.ExplodedSliceIndex),
            ChartPieFormatParseIssue.ExplodedSliceDistance => For("ChartPieFormat_InvalidExplodedDistanceMessage", ChartPieFormatDialogFieldId.ExplodedSliceDistance),
            ChartPieFormatParseIssue.DoughnutHoleSize => For("ChartPieFormat_InvalidHoleSizeMessage", ChartPieFormatDialogFieldId.DoughnutHoleSize),
            _ => For("ChartPieFormat_InvalidFirstSliceAngleMessage", ChartPieFormatDialogFieldId.FirstSliceAngle)
        };

    public static ValidationPresentationDescriptor<ChartBubbleFormatDialogFieldId> Describe(ChartBubbleFormatParseIssue issue) =>
        For("ChartBubbleFormat_InvalidBubbleScaleMessage", ChartBubbleFormatDialogFieldId.BubbleScale);

    public static ValidationPresentationDescriptor<ChartStockFormatDialogFieldId> Describe(ChartStockFormatParseIssue issue) =>
        issue == ChartStockFormatParseIssue.HighLowLineThickness
            ? For("ChartStockFormat_InvalidLineThicknessMessage", ChartStockFormatDialogFieldId.HighLowLineThickness)
            : For("ChartStockFormat_InvalidGapWidthMessage", ChartStockFormatDialogFieldId.GapWidth);

    public static ValidationPresentationDescriptor<ChartAxisDialogFieldId> Describe(ChartAxisFormatParseIssue issue) =>
        issue switch
        {
            ChartAxisFormatParseIssue.Maximum => For("ChartAxisFormat_InvalidMaximumMessage", ChartAxisDialogFieldId.Maximum),
            ChartAxisFormatParseIssue.MajorUnit => For("ChartAxisFormat_InvalidMajorUnitMessage", ChartAxisDialogFieldId.MajorUnit),
            ChartAxisFormatParseIssue.MinorUnit => For("ChartAxisFormat_InvalidMinorUnitMessage", ChartAxisDialogFieldId.MinorUnit),
            ChartAxisFormatParseIssue.MajorGridlineColor => For("ChartDialog_InvalidOptionalColorMessage", ChartAxisDialogFieldId.MajorGridlineColor),
            ChartAxisFormatParseIssue.MinorGridlineColor => For("ChartDialog_InvalidOptionalColorMessage", ChartAxisDialogFieldId.MinorGridlineColor),
            ChartAxisFormatParseIssue.GridlineThickness => For("ChartAxisFormat_InvalidGridlineWidthMessage", ChartAxisDialogFieldId.GridlineThickness),
            ChartAxisFormatParseIssue.LabelTextColor => For("ChartDialog_InvalidOptionalColorMessage", ChartAxisDialogFieldId.LabelTextColor),
            ChartAxisFormatParseIssue.LabelFontSize => For("ChartAxisFormat_InvalidLabelFontSizeMessage", ChartAxisDialogFieldId.LabelFontSize),
            ChartAxisFormatParseIssue.LabelAngle => For("ChartAxisFormat_InvalidLabelAngleMessage", ChartAxisDialogFieldId.LabelAngle),
            ChartAxisFormatParseIssue.LineColor => For("ChartDialog_InvalidOptionalColorMessage", ChartAxisDialogFieldId.LineColor),
            ChartAxisFormatParseIssue.LineThickness => For("ChartAxisFormat_InvalidAxisLineWidthMessage", ChartAxisDialogFieldId.LineThickness),
            _ => For("ChartAxisFormat_InvalidMinimumMessage", ChartAxisDialogFieldId.Minimum)
        };

    public static ValidationPresentationDescriptor<ChartAreaFormatDialogFieldId> Describe(ChartAreaFormatParseIssue issue) =>
        issue switch
        {
            ChartAreaFormatParseIssue.PlotAreaFillColor => For("ChartDialog_InvalidOptionalColorMessage", ChartAreaFormatDialogFieldId.PlotAreaFillColor),
            ChartAreaFormatParseIssue.PlotAreaBorderColor => For("ChartDialog_InvalidOptionalColorMessage", ChartAreaFormatDialogFieldId.PlotAreaBorderColor),
            ChartAreaFormatParseIssue.PlotAreaBorderThickness => For("ChartAreaLegend_InvalidPlotAreaBorderWidthMessage", ChartAreaFormatDialogFieldId.PlotAreaBorderThickness),
            ChartAreaFormatParseIssue.LegendTextColor => For("ChartDialog_InvalidOptionalColorMessage", ChartAreaFormatDialogFieldId.LegendTextColor),
            ChartAreaFormatParseIssue.LegendFillColor => For("ChartDialog_InvalidOptionalColorMessage", ChartAreaFormatDialogFieldId.LegendFillColor),
            ChartAreaFormatParseIssue.LegendBorderColor => For("ChartDialog_InvalidOptionalColorMessage", ChartAreaFormatDialogFieldId.LegendBorderColor),
            ChartAreaFormatParseIssue.LegendBorderThickness => For("ChartAreaLegend_InvalidLegendBorderWidthMessage", ChartAreaFormatDialogFieldId.LegendBorderThickness),
            ChartAreaFormatParseIssue.LegendFontSize => For("ChartAreaLegend_InvalidLegendFontSizeMessage", ChartAreaFormatDialogFieldId.LegendFontSize),
            _ => For("ChartDialog_InvalidOptionalColorMessage", ChartAreaFormatDialogFieldId.ChartAreaFillColor)
        };

    public static ValidationPresentationDescriptor<ChartSeriesFormatDialogFieldId> Describe(ChartSeriesFormatParseIssue issue) =>
        issue switch
        {
            ChartSeriesFormatParseIssue.StrokeColor => For("ChartDialog_InvalidOptionalColorMessage", ChartSeriesFormatDialogFieldId.StrokeColor),
            ChartSeriesFormatParseIssue.StrokeThickness => For("ChartSeriesFormat_InvalidLineWidthMessage", ChartSeriesFormatDialogFieldId.StrokeThickness),
            ChartSeriesFormatParseIssue.MarkerSize => For("ChartSeriesFormat_InvalidMarkerSizeMessage", ChartSeriesFormatDialogFieldId.MarkerSize),
            _ => For("ChartDialog_InvalidOptionalColorMessage", ChartSeriesFormatDialogFieldId.FillColor)
        };

    public static ValidationPresentationDescriptor<ChartTrendlineDialogFieldId> Describe(ChartTrendlineDialogParseIssue issue) =>
        issue switch
        {
            ChartTrendlineDialogParseIssue.Order => For("ChartTrendline_InvalidOrderMessage", ChartTrendlineDialogFieldId.Order),
            ChartTrendlineDialogParseIssue.Color => For("ChartDialog_InvalidOptionalColorMessage", ChartTrendlineDialogFieldId.LineColor),
            ChartTrendlineDialogParseIssue.Thickness => For("ChartTrendline_InvalidWidthMessage", ChartTrendlineDialogFieldId.LineThickness),
            _ => For("ChartTrendline_InvalidPeriodMessage", ChartTrendlineDialogFieldId.Period)
        };

    public static ValidationPresentationDescriptor<ChartDataLabelsDialogFieldId> Describe(ChartDataLabelsParseIssue issue) =>
        issue switch
        {
            ChartDataLabelsParseIssue.BorderColor => For("ChartDialog_InvalidOptionalColorMessage", ChartDataLabelsDialogFieldId.BorderColor),
            ChartDataLabelsParseIssue.TextColor => For("ChartDialog_InvalidOptionalColorMessage", ChartDataLabelsDialogFieldId.TextColor),
            ChartDataLabelsParseIssue.BorderThickness => For("ChartDataLabels_InvalidBorderThicknessMessage", ChartDataLabelsDialogFieldId.BorderThickness),
            ChartDataLabelsParseIssue.FontSize => For("ChartDataLabels_InvalidFontSizeMessage", ChartDataLabelsDialogFieldId.FontSize),
            ChartDataLabelsParseIssue.Angle => For("ChartDataLabels_InvalidAngleMessage", ChartDataLabelsDialogFieldId.TextAngle),
            _ => For("ChartDialog_InvalidOptionalColorMessage", ChartDataLabelsDialogFieldId.FillColor)
        };

    public static ValidationPresentationDescriptor<ChartErrorBarsDialogFieldId> Describe(ChartErrorBarsParseIssue issue) =>
        For("ChartErrorBars_InvalidValueMessage", ChartErrorBarsDialogFieldId.Value);

    public static LocalizedTextDescriptor DescribeAxisCommandIssue(ChartAxisCommandIssue issue, bool useXAxis) =>
        LocalizedTextDescriptor.Resource(issue switch
        {
            ChartAxisCommandIssue.UnsupportedLogScale => useXAxis
                ? "MainWindowMessage_ChartXAxisLogScaleSupportedTypes"
                : "MainWindowMessage_ChartYAxisLogScaleSupportedTypes",
            ChartAxisCommandIssue.UnsupportedBounds => "MainWindowMessage_ChartAxisBoundsSupportedTypes",
            ChartAxisCommandIssue.NumericBoundsRequired => "MainWindowMessage_ChartAxisBoundsRequiresNumericData",
            _ => "MainWindowMessage_ChartAxisOptionsRequiresChart"
        });

    private static ValidationPresentationDescriptor<TFocusTarget> For<TFocusTarget>(
        string resourceKey,
        TFocusTarget focusTarget)
        where TFocusTarget : struct, Enum =>
        new(LocalizedTextDescriptor.Resource(resourceKey), focusTarget);
}
