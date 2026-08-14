using System.Globalization;
using Avalonia.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed partial class ChartAreaOptionsDialog : FreePDialogWindow
{
    private readonly ChartAreaOptionsDialogSession _session;
    private readonly ChartOptionsDialogForm _form;

    internal ChartAreaOptionsDialog(
        EditingSession editor,
        ChartAreaFormattingTarget? initialTarget = null)
    {
        _session = new ChartAreaOptionsDialogSession(editor, initialTarget);
        var plan = _session.BuildDialogPlan(CultureInfo.CurrentCulture);
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
        var result = _session.TryCommit(ReadInput(), CultureInfo.CurrentCulture);
        if (result.Succeeded)
            Close(true);
    }

    private ChartAreaOptionsDialogInput ReadInput() =>
        _session.BuildInput(_form.CaptureValues());
}
