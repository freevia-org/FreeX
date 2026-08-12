using System.Windows;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style Scatter/Radar plot-style dialog.</summary>
public sealed class ChartPlotStyleOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ChartPlotStyleOptionsDialogSession _session;
    private readonly ChartOptionsDialogForm _form;

    public ChartPlotStyleOptionsDialog(EditingSession editor)
    {
        _session = new ChartPlotStyleOptionsDialogSession(editor);
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

    internal ChartPlotStyleOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(_session.BuildInput(_form.CaptureValues()));

    internal void SetOptionsForTests(Func<ChartPlotStyleOptionsDialogSession, ChartOptionsDialogValues> buildValues) =>
        _form.ApplyValues(buildValues(_session));

    private void OnOk()
    {
        _session.Submit(ReadInput());
        DialogResult = true;
    }

    private ChartPlotStyleOptionsDialogInput ReadInput() =>
        _session.BuildInput(_form.CaptureValues());
}
