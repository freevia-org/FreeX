using System.Windows;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style chart display options dialog.</summary>
public sealed class ChartDisplayOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ChartDisplayOptionsDialogSession _session;
    private readonly ChartOptionsDialogForm _form;

    public ChartDisplayOptionsDialog(EditingSession editor)
    {
        _session = new ChartDisplayOptionsDialogSession(editor);
        var plan = _session.BuildDialogPlan();
        _form = ChartOptionsDialogChrome.CreateForm(plan, OnOk, Close);

        Title = plan.Title;
        Width = plan.Width;
        Height = plan.Height;
        MinWidth = plan.MinimumWidth;
        MinHeight = plan.MinimumHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
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

    internal void SetLabelTextStyleForTests(
        string? family,
        double? sizePt,
        bool? bold,
        bool? italic,
        string? color)
    {
        _form.SetText(ChartOptionsDialogFieldId.LabelFontFamily, family);
        _form.SetText(ChartOptionsDialogFieldId.LabelFontSize, _session.Format(sizePt));
        _form.SetChecked(ChartOptionsDialogFieldId.LabelBold, bold);
        _form.SetChecked(ChartOptionsDialogFieldId.LabelItalic, italic);
        _form.SetText(ChartOptionsDialogFieldId.LabelColor, color);
    }

    internal void SetBubbleSizeLabelsForTests(bool value) =>
        _form.SetChecked(ChartOptionsDialogFieldId.BubbleSizeLabels, value);

    internal void SetLeaderLinesForTests(bool? value) =>
        _form.SetChecked(ChartOptionsDialogFieldId.LeaderLines, value);

    private void OnOk()
    {
        var result = _session.Submit(ReadInput());
        if (result.ShouldClose)
        {
            DialogResult = true;
            return;
        }

        MessageBox.Show(this, result.ValidationMessage, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private ChartDisplayOptionsDialogInput ReadInput() =>
        _session.BuildInput(_form.CaptureValues());
}
