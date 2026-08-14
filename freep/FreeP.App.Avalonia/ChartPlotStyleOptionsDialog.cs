using Avalonia.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed partial class ChartPlotStyleOptionsDialog : FreePDialogWindow
{
    private readonly ChartPlotStyleOptionsDialogSession _session;
    private readonly ChartOptionsDialogForm _form;

    internal ChartPlotStyleOptionsDialog(EditingSession editor)
    {
        _session = new ChartPlotStyleOptionsDialogSession(editor);
        var plan = _session.BuildDialogPlan();
        _form = ChartOptionsDialogChrome.CreateForm(plan, OnOk, () => Close(false));

        Title = plan.Title;
        Width = plan.Width;
        Height = plan.Height;
        MinWidth = plan.MinimumWidth;
        MinHeight = plan.MinimumHeight;
        CanResize = plan.IsResizable;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = _form.Content;
    }

    private void OnOk()
    {
        _session.Submit(ReadInput());
        Close(true);
    }

    private ChartPlotStyleOptionsDialogInput ReadInput() =>
        _session.BuildInput(_form.CaptureValues());
}
