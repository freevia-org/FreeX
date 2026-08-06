using System.Windows;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style per-point chart formatting dialog.</summary>
public sealed class ChartPointOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ChartPointOptionsDialogSession _session;
    private readonly ChartOptionsDialogForm _form;

    public ChartPointOptionsDialog(
        EditingSession editor,
        int? initialSeriesIndex = null,
        int? initialPointIndex = null)
    {
        _session = new ChartPointOptionsDialogSession(editor, initialSeriesIndex, initialPointIndex);
        var plan = _session.BuildDialogPlan();
        _form = ChartOptionsDialogChrome.CreateForm(plan, OnOk, Close, OnValueChanged);

        Title = plan.Title;
        Width = plan.Width;
        Height = plan.Height;
        MinWidth = plan.MinimumWidth;
        MinHeight = plan.MinimumHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Content = _form.Content;
    }

    internal ChartPointOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(ReadInput());

    internal void SetOptionsForTests(
        int seriesIndex,
        int pointIndex,
        string? fillColor,
        string? strokeColor,
        double? strokeWidthPt,
        ChartMarkerSymbol? markerSymbol,
        double? markerSizePt,
        bool usePointDataLabels = false,
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
        int? explosionPercent = null,
        bool? showLeaderLines = null)
    {
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.Series, seriesIndex);
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.Point, pointIndex);
        _form.SetText(ChartOptionsDialogFieldId.FillColor, fillColor);
        _form.SetText(ChartOptionsDialogFieldId.StrokeColor, strokeColor);
        _form.SetText(ChartOptionsDialogFieldId.StrokeWidth, _session.Format(strokeWidthPt));
        _form.SetChecked(ChartOptionsDialogFieldId.UsePointDataLabels, usePointDataLabels);
        _form.SetChecked(ChartOptionsDialogFieldId.ValueLabels, showValueLabels);
        _form.SetChecked(ChartOptionsDialogFieldId.PercentLabels, showPercentLabels);
        _form.SetChecked(ChartOptionsDialogFieldId.CategoryLabels, showCategoryLabels);
        _form.SetChecked(ChartOptionsDialogFieldId.SeriesLabels, showSeriesLabels);
        _form.SetChecked(ChartOptionsDialogFieldId.LegendKeys, showLegendKeys);
        _form.SetChecked(ChartOptionsDialogFieldId.BubbleSizeLabels, showBubbleSize);
        _form.SetChecked(ChartOptionsDialogFieldId.LeaderLines, showLeaderLines);
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.LabelPosition, _session.FindLabelPositionIndex(labelPosition));
        _form.SetText(ChartOptionsDialogFieldId.LabelNumberFormat, labelNumberFormat);
        _form.SetText(ChartOptionsDialogFieldId.LabelSeparator, labelSeparator);
        _form.SetText(ChartOptionsDialogFieldId.LabelFontFamily, labelFontFamily);
        _form.SetText(ChartOptionsDialogFieldId.LabelFontSize, _session.Format(labelFontSizePt));
        _form.SetChecked(ChartOptionsDialogFieldId.LabelBold, labelBold);
        _form.SetChecked(ChartOptionsDialogFieldId.LabelItalic, labelItalic);
        _form.SetText(ChartOptionsDialogFieldId.LabelColor, labelColor);
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.Marker, _session.FindMarkerIndex(markerSymbol));
        _form.SetText(ChartOptionsDialogFieldId.MarkerSize, _session.Format(markerSizePt));
        _form.SetText(ChartOptionsDialogFieldId.Explosion, _session.Format(explosionPercent));
    }

    private void OnValueChanged(ChartOptionsDialogFieldId fieldId)
    {
        switch (fieldId)
        {
            case ChartOptionsDialogFieldId.Series:
                _session.SelectSeries(_form.SelectedIndex(fieldId));
                _form.ApplyPlan(_session.BuildDialogPlan());
                break;
            case ChartOptionsDialogFieldId.Point:
                _session.SelectPoint(_form.SelectedIndex(fieldId));
                _form.ApplyPlan(_session.BuildDialogPlan());
                break;
        }
    }

    private void OnOk()
    {
        var result = _session.TryCommit(ReadInput());
        if (result.Succeeded)
        {
            DialogResult = true;
            return;
        }

        MessageBox.Show(this, result.Error, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private ChartPointOptionsDialogInput ReadInput() =>
        _session.BuildInput(_form.CaptureValues());
}
