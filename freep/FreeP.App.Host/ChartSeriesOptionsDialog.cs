using System.Windows;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style per-series chart formatting dialog.</summary>
public sealed class ChartSeriesOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ChartSeriesOptionsDialogSession _session;
    private readonly ChartOptionsDialogForm _form;

    public ChartSeriesOptionsDialog(EditingSession editor, int? initialSeriesIndex = null)
    {
        _session = new ChartSeriesOptionsDialogSession(editor, initialSeriesIndex);
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
        {
            DialogResult = true;
            return;
        }

        DialogMessageHelper.ShowWarning(this, result.Error, Title);
    }

    private ChartSeriesOptionsDialogInput ReadInput() =>
        _session.BuildInput(_form.CaptureValues());
}
