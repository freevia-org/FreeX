namespace FreeX.Core.Model;

[Flags]
public enum UnsupportedChartTrendlineState
{
    Common = 0,
    LabelFormatting = 1,
    ExtendedDefinition = 2
}

public static class ChartTrendlineSupportPolicy
{
    public static void NormalizeUnsupported(
        ChartModel chart,
        UnsupportedChartTrendlineState state = UnsupportedChartTrendlineState.Common)
    {
        ArgumentNullException.ThrowIfNull(chart);
        if (ChartTypeSupport.SupportsTrendlines(chart.Type))
            return;

        chart.ShowLinearTrendline = false;
        chart.TrendlineType = ChartTrendlineType.Linear;
        chart.TrendlinePeriod = 2;
        chart.TrendlineOrder = 2;
        chart.ShowTrendlineEquation = false;
        chart.ShowTrendlineRSquared = false;
        chart.TrendlineColor = null;
        chart.TrendlineThemeColor = null;
        chart.TrendlineThickness = 1.5;
        chart.TrendlineDashStyle = ChartLineDashStyle.Dash;

        if ((state & UnsupportedChartTrendlineState.LabelFormatting) != 0)
            ClearLabelFormatting(chart);

        if ((state & UnsupportedChartTrendlineState.ExtendedDefinition) != 0)
        {
            chart.TrendlineName = null;
            chart.TrendlineForward = null;
            chart.TrendlineBackward = null;
            chart.TrendlineIntercept = null;
        }
    }

    private static void ClearLabelFormatting(ChartModel chart)
    {
        chart.TrendlineLabelNumberFormatCode = null;
        chart.TrendlineLabelNumberFormatSourceLinked = null;
        chart.TrendlineLabelLayout = null;
        chart.TrendlineLabelFillColor = null;
        chart.TrendlineLabelFillThemeColor = null;
        chart.TrendlineLabelBorderColor = null;
        chart.TrendlineLabelBorderThemeColor = null;
        chart.TrendlineLabelBorderThickness = null;
        chart.TrendlineLabelTextColor = null;
        chart.TrendlineLabelTextThemeColor = null;
        chart.TrendlineLabelFontSize = null;
        chart.TrendlineLabelAngle = null;
    }
}
