using System.Globalization;
using System.Windows;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style plot-area and legend manual-layout dialog.</summary>
public sealed class ChartLayoutOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ChartLayoutOptionsDialogSession _session;
    private readonly ChartOptionsDialogForm _form;

    public ChartLayoutOptionsDialog(EditingSession editor)
    {
        _session = new ChartLayoutOptionsDialogSession(editor);
        var plan = _session.BuildDialogPlan(CultureInfo.CurrentCulture);
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
        {
            DialogResult = true;
            return;
        }

        MessageBox.Show(this, result.Error, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
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
