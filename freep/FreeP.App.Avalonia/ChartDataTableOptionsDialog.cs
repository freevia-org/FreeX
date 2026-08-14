using System.Globalization;
using Avalonia.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed partial class ChartDataTableOptionsDialog : FreePDialogWindow
{
    private readonly ChartDataTableOptionsDialogSession _session;
    private readonly ChartOptionsDialogForm _form;

    internal ChartDataTableOptionsDialog(EditingSession editor)
    {
        _session = new ChartDataTableOptionsDialogSession(editor);
        var plan = _session.BuildDialogPlan(CultureInfo.CurrentCulture);
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
        var result = _session.TryCommit(ReadInput(), CultureInfo.CurrentCulture);
        if (result.Succeeded)
            Close(true);
    }

    private ChartDataTableOptionsDialogInput ReadInput() =>
        _session.BuildInput(_form.CaptureValues());
}
