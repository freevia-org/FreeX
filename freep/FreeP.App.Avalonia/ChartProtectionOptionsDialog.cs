using Avalonia.Controls;
using Avalonia.Media;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class ChartProtectionOptionsDialog : Window
{
    private readonly ChartProtectionOptionsDialogSession _session;
    private readonly ChartOptionsDialogForm _form;

    internal ChartProtectionOptionsDialog(EditingSession editor)
    {
        _session = new ChartProtectionOptionsDialogSession(editor);
        var plan = _session.BuildDialogPlan();
        _form = ChartOptionsDialogChrome.CreateForm(plan, OnOk, () => Close(false));

        Title = plan.Title;
        Width = plan.Width;
        Height = plan.Height;
        MinWidth = plan.MinimumWidth;
        MinHeight = plan.MinimumHeight;
        CanResize = plan.IsResizable;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));
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
        Close(true);
    }

    private ChartProtectionOptionsDialogInput ReadInput() =>
        _session.BuildInput(_form.CaptureValues());
}
