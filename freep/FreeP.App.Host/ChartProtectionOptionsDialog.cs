using System.Windows;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style chart object/data/formatting/selection protection dialog.</summary>
public sealed class ChartProtectionOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ChartProtectionOptionsDialogSession _session;
    private readonly ChartOptionsDialogForm _form;

    public ChartProtectionOptionsDialog(EditingSession editor)
    {
        _session = new ChartProtectionOptionsDialogSession(editor);
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

    internal ChartProtectionOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(ReadInput());

    internal void SetOptionsForTests(bool? chartObject, bool? data, bool? formatting, bool? selection)
    {
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.ProtectedChartObject, _session.FindBooleanIndex(chartObject));
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.ProtectedData, _session.FindBooleanIndex(data));
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.ProtectedFormatting, _session.FindBooleanIndex(formatting));
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.ProtectedSelection, _session.FindBooleanIndex(selection));
    }

    private void OnOk()
    {
        _session.Submit(ReadInput());
        DialogResult = true;
    }

    private ChartProtectionOptionsDialogInput ReadInput() =>
        _session.BuildInput(_form.CaptureValues());
}
