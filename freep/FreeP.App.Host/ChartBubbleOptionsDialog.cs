using System.Windows;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style bubble chart sizing options dialog.</summary>
public sealed class ChartBubbleOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ChartBubbleOptionsDialogSession _session;
    private readonly ChartOptionsDialogForm _form;

    public ChartBubbleOptionsDialog(EditingSession editor)
    {
        _session = new ChartBubbleOptionsDialogSession(editor);
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

    internal ChartBubbleOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlanForTests(_form.CaptureValues());

    internal void SetOptionsForTests(ChartBubbleOptionsDialogTestSettings settings) =>
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

    private ChartBubbleOptionsDialogInput ReadInput() =>
        _session.BuildInput(_form.CaptureValues());
}
