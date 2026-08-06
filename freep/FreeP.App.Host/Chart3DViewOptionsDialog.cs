using System.Windows;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style chart camera and Surface3D options dialog.</summary>
public sealed class Chart3DViewOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly Chart3DViewOptionsDialogSession _session;
    private readonly ChartOptionsDialogForm _form;

    public Chart3DViewOptionsDialog(EditingSession editor)
    {
        _session = new Chart3DViewOptionsDialogSession(editor);
        var plan = _session.BuildDialogPlan();
        _form = ChartOptionsDialogChrome.CreateForm(plan, OnOk, Close);

        Title = plan.Title;
        Width = plan.Width;
        Height = plan.Height + 36;
        MinWidth = plan.MinimumWidth;
        MinHeight = plan.MinimumHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Content = _form.Content;
    }

    internal Chart3DViewOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(ReadInput());

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

    private Chart3DViewOptionsDialogInput ReadInput() =>
        _session.BuildInput(_form.CaptureValues());
}
