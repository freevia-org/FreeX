using Avalonia.Controls;
using Avalonia.Media;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class ChartSeriesOptionsDialog : Window
{
    private readonly ChartSeriesOptionsDialogSession _session;
    private readonly ChartOptionsDialogForm _form;

    internal ChartSeriesOptionsDialog(EditingSession editor, int? initialSeriesIndex = null)
    {
        _session = new ChartSeriesOptionsDialogSession(editor, initialSeriesIndex);
        var plan = _session.BuildDialogPlan();
        _form = ChartOptionsDialogChrome.CreateForm(plan, OnOk, () => Close(false), OnValueChanged);

        Title = plan.Title;
        Width = plan.Width;
        Height = plan.Height;
        MinWidth = plan.MinimumWidth;
        MinHeight = plan.MinimumHeight;
        CanResize = plan.IsResizable;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));
        Content = _form.Content;
    }

    internal ChartSeriesOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(ReadInput());

    internal void SetOptionsForTests(
        int seriesIndex,
        bool smoothLine,
        bool onSecondaryAxis,
        double? lineWidthPt,
        ChartMarkerSymbol markerSymbol,
        double? markerSizePt,
        string? fillColor = null,
        string? lineColor = null,
        OutlineDash lineDash = OutlineDash.Solid,
        bool noLine = false,
        bool useSeriesDataLabels = false,
        bool showValueLabels = false,
        bool showPercentLabels = false,
        bool showCategoryLabels = false,
        bool showSeriesLabels = false,
        bool showLegendKeys = false,
        DataLabelPosition labelPosition = DataLabelPosition.OutsideEnd,
        string? labelNumberFormat = null,
        string? labelSeparator = null,
        string? labelFontFamily = null,
        double? labelFontSizePt = null,
        bool? labelBold = null,
        bool? labelItalic = null,
        string? labelColor = null,
        bool showBubbleSize = false,
        bool? showLeaderLines = null,
        bool errorBars = false,
        bool trendline = false,
        ChartTrendlineType trendlineType = ChartTrendlineType.Linear,
        int? trendlineOrder = null,
        int? trendlinePeriod = null,
        double? trendlineForward = null,
        double? trendlineBackward = null,
        bool trendlineEquation = false,
        bool trendlineRSquared = false,
        ChartType? overrideChartType = null,
        bool? invertIfNegative = null)
    {
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.Series, seriesIndex);
        _form.SetChecked(ChartOptionsDialogFieldId.SmoothLine, smoothLine);
        _form.SetChecked(ChartOptionsDialogFieldId.SecondaryAxis, onSecondaryAxis);
        if (invertIfNegative.HasValue)
            _form.SetChecked(ChartOptionsDialogFieldId.InvertIfNegative, invertIfNegative);
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.SeriesChartType, _session.FindSeriesChartTypeIndex(overrideChartType));
        _form.SetText(ChartOptionsDialogFieldId.LineWidth, _session.Format(lineWidthPt));
        _form.SetText(ChartOptionsDialogFieldId.LineColor, lineColor);
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.LineDash, _session.FindDashIndex(lineDash));
        _form.SetChecked(ChartOptionsDialogFieldId.NoLine, noLine);
        _form.SetText(ChartOptionsDialogFieldId.FillColor, fillColor);
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.Marker, _session.FindMarkerIndex(markerSymbol));
        _form.SetText(ChartOptionsDialogFieldId.MarkerSize, _session.Format(markerSizePt));
        _form.SetChecked(ChartOptionsDialogFieldId.UseSeriesDataLabels, useSeriesDataLabels);
        _form.SetChecked(ChartOptionsDialogFieldId.ValueLabels, showValueLabels);
        _form.SetChecked(ChartOptionsDialogFieldId.PercentLabels, showPercentLabels);
        _form.SetChecked(ChartOptionsDialogFieldId.CategoryLabels, showCategoryLabels);
        _form.SetChecked(ChartOptionsDialogFieldId.SeriesLabels, showSeriesLabels);
        _form.SetChecked(ChartOptionsDialogFieldId.LegendKeys, showLegendKeys);
        _form.SetChecked(ChartOptionsDialogFieldId.BubbleSizeLabels, showBubbleSize);
        _form.SetChecked(ChartOptionsDialogFieldId.LeaderLines, showLeaderLines);
        _form.SetChecked(ChartOptionsDialogFieldId.ErrorBars, errorBars);
        _form.SetChecked(ChartOptionsDialogFieldId.Trendline, trendline);
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.TrendlineType, _session.FindTrendlineTypeIndex(trendlineType));
        _form.SetText(ChartOptionsDialogFieldId.TrendlineOrder, _session.Format(trendlineOrder));
        _form.SetText(ChartOptionsDialogFieldId.TrendlinePeriod, _session.Format(trendlinePeriod));
        _form.SetText(ChartOptionsDialogFieldId.TrendlineForward, _session.Format(trendlineForward));
        _form.SetText(ChartOptionsDialogFieldId.TrendlineBackward, _session.Format(trendlineBackward));
        _form.SetChecked(ChartOptionsDialogFieldId.TrendlineEquation, trendlineEquation);
        _form.SetChecked(ChartOptionsDialogFieldId.TrendlineRSquared, trendlineRSquared);
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.LabelPosition, _session.FindLabelPositionIndex(labelPosition));
        _form.SetText(ChartOptionsDialogFieldId.LabelNumberFormat, labelNumberFormat);
        _form.SetText(ChartOptionsDialogFieldId.LabelSeparator, labelSeparator);
        _form.SetText(ChartOptionsDialogFieldId.LabelFontFamily, labelFontFamily);
        _form.SetText(ChartOptionsDialogFieldId.LabelFontSize, _session.Format(labelFontSizePt));
        _form.SetChecked(ChartOptionsDialogFieldId.LabelBold, labelBold);
        _form.SetChecked(ChartOptionsDialogFieldId.LabelItalic, labelItalic);
        _form.SetText(ChartOptionsDialogFieldId.LabelColor, labelColor);
    }

    private void OnValueChanged(ChartOptionsDialogFieldId fieldId)
    {
        if (fieldId != ChartOptionsDialogFieldId.Series)
            return;

        _session.SelectSeries(_form.SelectedIndex(fieldId));
        _form.ApplyPlan(_session.BuildDialogPlan());
    }

    private void OnOk()
    {
        var result = _session.TryCommit(ReadInput());
        if (result.Succeeded)
            Close(true);
    }

    private ChartSeriesOptionsDialogInput ReadInput() =>
        _session.BuildInput(_form.CaptureValues());
}
