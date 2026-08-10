using System.Windows;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style chart display options dialog.</summary>
public sealed class ChartDisplayOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ChartDisplayOptionsDialogSession _session;
    private readonly ChartOptionsDialogForm _form;

    public ChartDisplayOptionsDialog(EditingSession editor)
    {
        _session = new ChartDisplayOptionsDialogSession(editor);
        var plan = _session.BuildDialogPlan();
        _form = ChartOptionsDialogChrome.CreateForm(plan, OnOk, Close);

        Title = plan.Title;
        Width = plan.Width;
        Height = plan.Height;
        MinWidth = plan.MinimumWidth;
        MinHeight = plan.MinimumHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Content = _form.Content;
    }

    internal ChartDisplayOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlanForTests(_form.CaptureValues());

    internal void SetOptionsForTests(ChartDisplayOptionsDialogTestSettings settings) =>
        _form.ApplyValues(_session.BuildTestValues(settings));

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

    private ChartDisplayOptionsDialogInput ReadInput() =>
        _session.BuildInput(_form.CaptureValues());
}
