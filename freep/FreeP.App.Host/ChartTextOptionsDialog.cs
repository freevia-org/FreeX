using System.Windows;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style chart-wide default text formatting dialog.</summary>
public sealed class ChartTextOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ChartTextOptionsDialogSession _session;
    private readonly ChartOptionsDialogForm _form;

    public ChartTextOptionsDialog(EditingSession editor, ChartTextTarget target = ChartTextTarget.Chart)
    {
        _session = new ChartTextOptionsDialogSession(editor, target);
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

    internal ChartTextOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlanForTests(_form.CaptureValues());

    internal void SetOptionsForTests(ChartTextOptionsDialogTestSettings settings) =>
        _form.ApplyValues(_session.BuildTestValues(settings));

    private void OnOk()
    {
        var result = _session.Submit(ReadInput());
        if (result.ShouldClose)
        {
            DialogResult = true;
            return;
        }

        MessageBox.Show(this, result.ValidationMessage, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private ChartTextOptionsDialogInput ReadInput() =>
        _session.BuildInput(_form.CaptureValues());
}
