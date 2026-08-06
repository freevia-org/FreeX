using Avalonia.Controls;
using Avalonia.Media;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class ChartAxisOptionsDialog : Window
{
    private readonly ChartAxisOptionsDialogSession _session;
    private readonly ChartOptionsDialogForm _form;

    internal ChartAxisOptionsDialog(EditingSession editor, ChartAxisKind? initialAxis = null)
    {
        _session = new ChartAxisOptionsDialogSession(editor, initialAxis);
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

    internal ChartAxisOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(ReadInput());

    internal void SetOptionsForTests(
        ChartAxisKind axis,
        string title,
        double? minimum,
        double? maximum,
        double? majorUnit,
        double? minorUnit,
        string numberFormatCode,
        bool majorGridlines,
        ChartTickMark? majorTickMark = null,
        ChartTickMark? minorTickMark = null,
        ChartTickLabelPosition? tickLabelPosition = null,
        ChartAxisCrossing? crosses = null,
        double? crossesAt = null,
        bool showAxis = true,
        ChartCrossBetween? crossBetween = null,
        ChartLabelAlignment? labelAlignment = null,
        int? labelOffsetPercent = null,
        bool? noMultiLevelLabels = null,
        bool? autoCrossing = null,
        bool reverseOrder = false,
        bool minorGridlines = false)
    {
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.Axis, (int)axis);
        _form.SetText(ChartOptionsDialogFieldId.AxisTitle, title);
        _form.SetChecked(ChartOptionsDialogFieldId.ShowAxis, showAxis);
        _form.SetText(ChartOptionsDialogFieldId.Minimum, _session.Format(minimum));
        _form.SetText(ChartOptionsDialogFieldId.Maximum, _session.Format(maximum));
        _form.SetText(ChartOptionsDialogFieldId.MajorUnit, _session.Format(majorUnit));
        _form.SetText(ChartOptionsDialogFieldId.MinorUnit, _session.Format(minorUnit));
        _form.SetText(ChartOptionsDialogFieldId.NumberFormat, numberFormatCode);
        _form.SetChecked(ChartOptionsDialogFieldId.MajorGridlines, majorGridlines);
        _form.SetChecked(ChartOptionsDialogFieldId.MinorGridlines, minorGridlines);
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.MajorTickMark, _session.FindTickMarkIndex(majorTickMark));
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.MinorTickMark, _session.FindTickMarkIndex(minorTickMark));
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.TickLabelPosition, _session.FindTickLabelPositionIndex(tickLabelPosition));
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.Crossing, _session.FindCrossingIndex(crosses));
        _form.SetText(ChartOptionsDialogFieldId.CrossesAt, _session.Format(crossesAt));
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.CrossBetween, _session.FindCrossBetweenIndex(crossBetween));
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.LabelAlignment, _session.FindLabelAlignmentIndex(labelAlignment));
        _form.SetText(ChartOptionsDialogFieldId.LabelOffset, _session.Format(labelOffsetPercent));
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.MultiLevelLabels, _session.FindMultiLevelLabelsIndex(noMultiLevelLabels));
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.AutoCrossing, _session.FindAutoCrossingIndex(autoCrossing));
        _form.SetChecked(ChartOptionsDialogFieldId.ReverseOrder, reverseOrder);
    }

    private void OnValueChanged(ChartOptionsDialogFieldId fieldId)
    {
        if (fieldId != ChartOptionsDialogFieldId.Axis)
            return;

        _session.SelectAxis(_form.SelectedIndex(fieldId));
        _form.ApplyPlan(_session.BuildDialogPlan());
    }

    private void OnOk()
    {
        var result = _session.Submit(ReadInput());
        Close(result.ShouldClose);
    }

    private ChartAxisOptionsDialogInput ReadInput() =>
        _session.BuildInput(_form.CaptureValues());
}
