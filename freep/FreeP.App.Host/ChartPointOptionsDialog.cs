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
        _session.BuildCommitPlanForTests(_form.CaptureValues());

    internal void SetOptionsForTests(ChartPointOptionsDialogTestSettings settings) =>
        _form.ApplyValues(_session.BuildTestValues(settings));

    private void OnValueChanged(ChartOptionsDialogFieldId fieldId)
    {
        if (_session.TryApplySelectionChange(fieldId, _form.SelectedIndex(fieldId), out var plan))
            _form.ApplyPlan(plan);
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

    private ChartPointOptionsDialogInput ReadInput() =>
        _session.BuildInput(_form.CaptureValues());
}
