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
        _session.BuildCommitPlanForTests(_form.CaptureValues());

    internal void SetOptionsForTests(ChartSeriesOptionsDialogTestSettings settings) =>
        _form.ApplyValues(_session.BuildTestValues(settings));

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
