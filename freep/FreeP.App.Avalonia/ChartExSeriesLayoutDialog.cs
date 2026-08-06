using Avalonia.Controls;
using Avalonia.Media;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

internal sealed class ChartExSeriesLayoutDialog : Window
{
    private readonly ChartExSeriesLayoutDialogSession _session;
    private readonly ChartOptionsDialogForm _form;

    internal ChartExSeriesLayoutDialog(EditingSession editor)
    {
        _session = new ChartExSeriesLayoutDialogSession(editor);
        var plan = _session.BuildDialogPlan();
        _form = ChartOptionsDialogChrome.CreateForm(plan, OnOk, () => Close(false), OnValueChanged);

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

    private int SelectedLayoutIndex =>
        _form.SelectedIndex(ChartOptionsDialogFieldId.ChartExLayout);

    private void OnValueChanged(ChartOptionsDialogFieldId fieldId)
    {
        if (fieldId != ChartOptionsDialogFieldId.ChartExSeries)
            return;

        _session.SelectSeries(_form.SelectedIndex(fieldId));
        _form.ApplyPlan(_session.BuildDialogPlan());
    }

    private void OnOk()
    {
        if (_session.TryApply(SelectedLayoutIndex, out _))
            Close(true);
    }
}
