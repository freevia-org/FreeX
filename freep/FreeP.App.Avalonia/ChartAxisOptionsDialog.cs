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
        _session.BuildCommitPlan(_session.BuildInput(_form.CaptureValues()));

    internal void SetOptionsForTests(Func<ChartAxisOptionsDialogSession, ChartOptionsDialogValues> buildValues) =>
        _form.ApplyValues(buildValues(_session));

    private void OnValueChanged(ChartOptionsDialogFieldId fieldId)
    {
        if (_session.TryApplySelectionChange(fieldId, _form.SelectedIndex(fieldId), out var plan))
            _form.ApplyPlan(plan);
    }

    private void OnOk()
    {
        var result = _session.Submit(ReadInput());
        Close(result.ShouldClose);
    }

    private ChartAxisOptionsDialogInput ReadInput() =>
        _session.BuildInput(_form.CaptureValues());
}
