using System.Windows;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style chart axis scale and display dialog.</summary>
public sealed class ChartAxisOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ChartAxisOptionsDialogSession _session;
    private readonly ChartOptionsDialogForm _form;

    public ChartAxisOptionsDialog(EditingSession editor, ChartAxisKind? initialAxis = null)
    {
        _session = new ChartAxisOptionsDialogSession(editor, initialAxis);
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

    internal ChartAxisOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlanForTests(_form.CaptureValues());

    internal void SetOptionsForTests(ChartAxisOptionsDialogTestSettings settings) =>
        _form.ApplyValues(_session.BuildTestValues(settings));

    private void OnValueChanged(ChartOptionsDialogFieldId fieldId)
    {
        if (_session.TryApplySelectionChange(fieldId, _form.SelectedIndex(fieldId), out var plan))
            _form.ApplyPlan(plan);
    }

    private void OnOk()
    {
        var result = _session.Submit(ReadInput());
        if (result.ShouldClose)
        {
            DialogResult = true;
            return;
        }

        DialogMessageHelper.ShowWarning(this, result.ValidationMessage, Title);
    }

    private ChartAxisOptionsDialogInput ReadInput() =>
        _session.BuildInput(_form.CaptureValues());
}
