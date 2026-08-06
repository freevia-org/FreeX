using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class ChartAreaOptionsDialog : Window
{
    private readonly ChartAreaOptionsDialogSession _session;
    private readonly ChartOptionsDialogForm _form;

    internal ChartAreaOptionsDialog(
        EditingSession editor,
        ChartAreaFormattingTarget? initialTarget = null)
    {
        _session = new ChartAreaOptionsDialogSession(editor, initialTarget);
        var plan = _session.BuildDialogPlan(CultureInfo.CurrentCulture);
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

    internal ChartAreaOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(ReadInput(), CultureInfo.CurrentCulture);

    internal void SetOptionsForTests(
        ChartAreaFormattingTarget target,
        string? fill,
        string? outline,
        double? width,
        bool noFill = false,
        bool noOutline = false,
        double? fillTransparency = null)
    {
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.AreaTarget, target == ChartAreaFormattingTarget.PlotArea ? 1 : 0);
        _form.SetText(ChartOptionsDialogFieldId.FillColor, fill);
        _form.SetText(ChartOptionsDialogFieldId.FillTransparency, Format(fillTransparency));
        _form.SetChecked(ChartOptionsDialogFieldId.NoFill, noFill);
        _form.SetText(ChartOptionsDialogFieldId.OutlineColor, outline);
        _form.SetText(ChartOptionsDialogFieldId.OutlineWidth, Format(width));
        _form.SetChecked(ChartOptionsDialogFieldId.NoOutline, noOutline);
    }

    private void OnValueChanged(ChartOptionsDialogFieldId fieldId)
    {
        if (fieldId != ChartOptionsDialogFieldId.AreaTarget)
            return;

        _session.SelectTarget(_form.SelectedIndex(fieldId));
        _form.ApplyPlan(_session.BuildDialogPlan(CultureInfo.CurrentCulture));
    }

    private void OnOk()
    {
        var result = _session.TryCommit(ReadInput(), CultureInfo.CurrentCulture);
        if (result.Succeeded)
            Close(true);
    }

    private ChartAreaOptionsDialogInput ReadInput() =>
        _session.BuildInput(_form.CaptureValues());

    private static string Format(double? value) =>
        ChartDialogOptionProjection.Format(value, CultureInfo.CurrentCulture);
}
