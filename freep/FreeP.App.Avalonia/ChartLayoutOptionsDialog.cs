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
        _session.BuildCommitPlanForTests(_form.CaptureValues(), CultureInfo.CurrentCulture);

    internal void SetOptionsForTests(ChartLayoutOptionsDialogTestSettings settings) =>
        _form.ApplyValues(_session.BuildTestValues(settings, CultureInfo.CurrentCulture));

    private void OnValueChanged(ChartOptionsDialogFieldId fieldId)
    {
        if (_session.TryApplySelectionChange(fieldId, _form.SelectedIndex(fieldId), out var plan))
            _form.ApplyPlan(plan);
    }

    private void OnOk()
    {
        var result = _session.TryCommit(ReadInput(), CultureInfo.CurrentCulture);
        if (result.Succeeded)
            Close(true);
    }

    private ChartLayoutOptionsDialogInput ReadInput() =>
        _session.BuildInput(_form.CaptureValues());
}
