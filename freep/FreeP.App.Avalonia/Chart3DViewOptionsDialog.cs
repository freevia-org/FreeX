using Avalonia.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed partial class Chart3DViewOptionsDialog : FreePDialogWindow
{
    private readonly Chart3DViewOptionsDialogSession _session;
    private readonly ChartOptionsDialogForm _form;

    internal Chart3DViewOptionsDialog(EditingSession editor)
    {
        _session = new Chart3DViewOptionsDialogSession(editor);
        var plan = _session.BuildDialogPlan();
        _form = ChartOptionsDialogChrome.CreateForm(plan, OnOk, () => Close(false));

        Title = plan.Title;
        Width = plan.Width;
        Height = plan.Height + 36;
        MinWidth = plan.MinimumWidth;
        MinHeight = plan.MinimumHeight;
        CanResize = plan.IsResizable;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = _form.Content;
    }

    private void OnOk()
    {
        var result = _session.Submit(ReadInput());
        Close(result.ShouldClose);
    }

    private Chart3DViewOptionsDialogInput ReadInput() =>
        _session.BuildInput(_form.CaptureValues());
}
