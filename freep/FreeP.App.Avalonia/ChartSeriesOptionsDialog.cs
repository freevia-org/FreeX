using Avalonia.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed partial class ChartSeriesOptionsDialog : FreePDialogWindow
{
    private readonly ChartSeriesOptionsDialogSession _session;
    private readonly ChartOptionsDialogForm _form;

    internal ChartSeriesOptionsDialog(EditingSession editor, int? initialSeriesIndex = null)
    {
        _session = new ChartSeriesOptionsDialogSession(editor, initialSeriesIndex);
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

    private void OnValueChanged(ChartOptionsDialogFieldId fieldId)
    {
        if (_session.TryApplySelectionChange(fieldId, _form.SelectedIndex(fieldId), out var plan))
            _form.ApplyPlan(plan);
    }

    private void OnOk()
    {
        var result = _session.TryCommit(ReadInput());
        if (result.Succeeded)
            Close(true);
    }

    private ChartSeriesOptionsDialogInput ReadInput() =>
        _session.BuildInput(_form.CaptureValues());
}
