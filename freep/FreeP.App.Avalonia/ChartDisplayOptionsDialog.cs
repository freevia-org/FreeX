using Avalonia.Controls;
using Avalonia.Media;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class ChartDisplayOptionsDialog : Window
{
    private readonly ChartDisplayOptionsDialogSession _session;
    private readonly ChartOptionsDialogForm _form;

    internal ChartDisplayOptionsDialog(EditingSession editor)
    {
        _session = new ChartDisplayOptionsDialogSession(editor);
        var plan = _session.BuildDialogPlan();
        _form = ChartOptionsDialogChrome.CreateForm(plan, OnOk, () => Close(false));

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

    internal ChartDisplayOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(ReadInput());

    internal void SetVaryColorsForTests(bool value) =>
        _form.SetChecked(ChartOptionsDialogFieldId.VaryColors, value);

    internal void SetTitleOverlayForTests(bool value) =>
        _form.SetChecked(ChartOptionsDialogFieldId.TitleOverlay, value);

    internal void SetTitlePositionForTests(ChartExTitlePosition value) =>
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.TitlePosition, _session.FindTitlePositionIndex(value));

    internal void SetTitleAlignmentForTests(ChartExTitleAlignment value) =>
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.TitleAlignment, _session.FindTitleAlignmentIndex(value));

    internal void SetPlotVisibleOnlyForTests(bool value) =>
        _form.SetChecked(ChartOptionsDialogFieldId.PlotVisibleOnly, value);

    internal void SetRoundedCornersForTests(bool value) =>
        _form.SetChecked(ChartOptionsDialogFieldId.RoundedCorners, value);

    internal void SetStyleIdForTests(int? styleId) =>
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.ChartStyle, _session.FindStyleIndex(styleId));

    internal void SetLegendOverlayForTests(bool? value) =>
        _form.SetChecked(ChartOptionsDialogFieldId.LegendOverlay, value);

    internal void SetHighLowLinesForTests(bool? value) =>
        _form.SetChecked(ChartOptionsDialogFieldId.HighLowLines, value);

    internal void SetWaterfallConnectorLinesForTests(bool? value) =>
        _form.SetChecked(ChartOptionsDialogFieldId.WaterfallConnectorLines, value);

    internal void SetDropLinesForTests(bool? value) =>
        _form.SetChecked(ChartOptionsDialogFieldId.DropLines, value);

    internal void SetUpDownBarsForTests(bool? value) =>
        _form.SetChecked(ChartOptionsDialogFieldId.UpDownBars, value);

    internal void SetSeriesLinesForTests(bool? value) =>
        _form.SetChecked(ChartOptionsDialogFieldId.SeriesLines, value);

    internal void SetLeaderLinesForTests(bool? value) =>
        _form.SetChecked(ChartOptionsDialogFieldId.LeaderLines, value);

    internal void SetOptionsForTests(
        string title,
        LegendPosition? legend,
        bool showValueLabels,
        DataLabelPosition labelPosition,
        bool categoryGridlines,
        bool valueGridlines,
        bool showPercentLabels = false,
        bool showCategoryLabels = false,
        bool showSeriesLabels = false,
        bool showLegendKeys = false,
        string? numberFormat = null,
        string? separator = null,
        int? barGapWidthPercent = null,
        int? barOverlapPercent = null,
        ChartDisplayBlanksAs? displayBlanksAs = null,
        bool? showDataLabelsOverMaximum = null,
        string? labelFontFamily = null,
        double? labelFontSizePt = null,
        bool? labelBold = null,
        bool? labelItalic = null,
        string? labelColor = null,
        bool showBubbleSize = false)
    {
        _form.SetText(ChartOptionsDialogFieldId.ChartTitle, title);
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.Legend, _session.FindLegendIndex(legend));
        _form.SetChecked(ChartOptionsDialogFieldId.ValueLabels, showValueLabels);
        _form.SetChecked(ChartOptionsDialogFieldId.PercentLabels, showPercentLabels);
        _form.SetChecked(ChartOptionsDialogFieldId.CategoryLabels, showCategoryLabels);
        _form.SetChecked(ChartOptionsDialogFieldId.SeriesLabels, showSeriesLabels);
        _form.SetChecked(ChartOptionsDialogFieldId.LegendKeys, showLegendKeys);
        _form.SetChecked(ChartOptionsDialogFieldId.BubbleSizeLabels, showBubbleSize);
        _form.SetChecked(ChartOptionsDialogFieldId.LeaderLines, null);
        _form.SetText(ChartOptionsDialogFieldId.LabelNumberFormat, numberFormat);
        _form.SetText(ChartOptionsDialogFieldId.LabelSeparator, separator);
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.LabelPosition, _session.FindLabelPositionIndex(labelPosition));
        _form.SetChecked(ChartOptionsDialogFieldId.CategoryGridlines, categoryGridlines);
        _form.SetChecked(ChartOptionsDialogFieldId.ValueGridlines, valueGridlines);
        _form.SetText(ChartOptionsDialogFieldId.BarGapWidth, _session.Format(barGapWidthPercent));
        _form.SetText(ChartOptionsDialogFieldId.BarOverlap, _session.Format(barOverlapPercent));
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.DisplayBlanks, _session.FindDisplayBlanksIndex(displayBlanksAs));
        _form.SetChecked(ChartOptionsDialogFieldId.ShowDataLabelsOverMaximum, showDataLabelsOverMaximum);
        _form.SetText(ChartOptionsDialogFieldId.LabelFontFamily, labelFontFamily);
        _form.SetText(ChartOptionsDialogFieldId.LabelFontSize, _session.Format(labelFontSizePt));
        _form.SetChecked(ChartOptionsDialogFieldId.LabelBold, labelBold);
        _form.SetChecked(ChartOptionsDialogFieldId.LabelItalic, labelItalic);
        _form.SetText(ChartOptionsDialogFieldId.LabelColor, labelColor);
    }

    private void OnOk()
    {
        var result = _session.Submit(ReadInput());
        if (result.ShouldClose)
            Close(true);
    }

    private ChartDisplayOptionsDialogInput ReadInput() =>
        _session.BuildInput(_form.CaptureValues());
}
