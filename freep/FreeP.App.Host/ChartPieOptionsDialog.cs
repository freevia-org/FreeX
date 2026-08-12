using System.Globalization;
using System.Windows;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style first-slice, doughnut-hole, and OfPie options dialog.</summary>
public sealed partial class ChartPieOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ChartPieOptionsDialogSession _session;
    private readonly ChartOptionsDialogForm _form;

    public ChartPieOptionsDialog(EditingSession editor)
    {
        _session = new ChartPieOptionsDialogSession(editor);
        var plan = _session.BuildDialogPlan(CultureInfo.CurrentCulture);
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

    private void OnOk()
    {
        var result = _session.TryCommit(ReadInput(), CultureInfo.CurrentCulture);
        if (result.Succeeded)
        {
            DialogResult = true;
            return;
        }

        DialogMessageHelper.ShowWarning(this, result.Error, Title);
    }

    private ChartPieOptionsDialogInput ReadInput() =>
        _session.BuildInput(_form.CaptureValues());
}
