using Avalonia.Controls;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

internal sealed class ChartExSeriesLayoutDialog : FreePDialogWindow
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
        Content = _form.Content;
    }

    private int SelectedLayoutIndex =>
        _form.SelectedIndex(ChartOptionsDialogFieldId.ChartExLayout);

    private void OnValueChanged(ChartOptionsDialogFieldId fieldId)
    {
        if (_session.TryApplySelectionChange(fieldId, _form.SelectedIndex(fieldId), out var plan))
            _form.ApplyPlan(plan);
    }

    private void OnOk()
    {
        if (_session.TryApply(SelectedLayoutIndex, out _))
            Close(true);
    }
}
