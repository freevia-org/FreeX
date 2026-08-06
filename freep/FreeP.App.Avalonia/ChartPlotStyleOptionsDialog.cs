using Avalonia.Controls;
using Avalonia.Media;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class ChartPlotStyleOptionsDialog : Window
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
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));
        Content = _form.Content;
    }

    internal ChartPlotStyleOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(ReadInput());

    internal void SetOptionsForTests(ScatterStyle scatterStyle, RadarStyle radarStyle)
    {
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.ScatterStyle, _session.FindScatterIndex(scatterStyle));
        _form.SetSelectedIndex(ChartOptionsDialogFieldId.RadarStyle, _session.FindRadarIndex(radarStyle));
    }

    private void OnOk()
    {
        _session.Submit(ReadInput());
        Close(true);
    }

    private ChartPlotStyleOptionsDialogInput ReadInput() =>
        _session.BuildInput(_form.CaptureValues());
}
