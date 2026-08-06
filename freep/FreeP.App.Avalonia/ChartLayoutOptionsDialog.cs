using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class ChartLayoutOptionsDialog : Window
{
    private readonly ChartLayoutOptionsDialogSession _session;
    private readonly ChartOptionsDialogForm _form;

    internal ChartLayoutOptionsDialog(EditingSession editor)
    {
        _session = new ChartLayoutOptionsDialogSession(editor);
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

    internal ChartLayoutOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(ReadInput(), CultureInfo.CurrentCulture);

    internal void SetOptionsForTests(
        ChartLayoutTarget target,
        string? layoutTarget,
        ChartManualLayoutMode xMode,
        ChartManualLayoutMode yMode,
        ChartManualLayoutMode widthMode,
        ChartManualLayoutMode heightMode,
        double? x,
        double? y,
        double? width,
        double? height)
    {
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.LayoutTargetObject, FindTargetIndex(target));
        var layoutTargets = ChartLayoutOptionsPlanner.LayoutTargetOptionsFor(layoutTarget);
        _form.SetChoices(
            ChartOptionsDialogFieldId.LayoutTarget,
            layoutTargets.Select(option => option.Label).ToArray(),
            ChartDialogOptionProjection.FindIndex(layoutTargets, layoutTarget, option => option.Value, comparer: StringComparer.OrdinalIgnoreCase));
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.XMode, FindModeIndex(xMode));
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.YMode, FindModeIndex(yMode));
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.WidthMode, FindModeIndex(widthMode));
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.HeightMode, FindModeIndex(heightMode));
        _form.SetText(ChartOptionsDialogFieldId.X, Format(x));
        _form.SetText(ChartOptionsDialogFieldId.Y, Format(y));
        _form.SetText(ChartOptionsDialogFieldId.Width, Format(width));
        _form.SetText(ChartOptionsDialogFieldId.Height, Format(height));
    }

    private void OnValueChanged(ChartOptionsDialogFieldId fieldId)
    {
        if (fieldId != ChartOptionsDialogFieldId.LayoutTargetObject)
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

    private ChartLayoutOptionsDialogInput ReadInput() =>
        _session.BuildInput(_form.CaptureValues());

    private static string Format(double? value) =>
        ChartDialogOptionProjection.Format(value, CultureInfo.CurrentCulture);

    private static int FindTargetIndex(ChartLayoutTarget value) =>
        ChartDialogOptionProjection.FindIndex(ChartLayoutOptionsPlanner.TargetOptions, value, option => option.Value);

    private static int FindModeIndex(ChartManualLayoutMode value) =>
        ChartDialogOptionProjection.FindIndex(ChartLayoutOptionsPlanner.ModeOptions, value, option => option.Value);
}
