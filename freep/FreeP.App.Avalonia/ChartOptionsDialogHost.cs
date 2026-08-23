using Avalonia.Controls;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

/// <summary>Renderer-local host for the shared chart-options dialog chrome.</summary>
internal abstract class ChartOptionsDialogHost<TSession> : FreePDialogWindow
{
    private protected readonly TSession _session;
    private protected readonly ChartOptionsDialogForm _form = null!;
    private readonly Func<TSession, ChartOptionsDialogValues, bool> _submit;

    protected ChartOptionsDialogHost(
        TSession session,
        ChartOptionsDialogPlan plan,
        Func<TSession, ChartOptionsDialogValues, bool> submit,
        Func<TSession, ChartOptionsDialogFieldId, int, ChartOptionsDialogPlan?>? replan = null,
        double heightAdjustment = 0)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(submit);

        _session = session;
        _submit = submit;
        _form = ChartOptionsDialogChrome.CreateForm(
            plan,
            Accept,
            () => Close(false),
            replan is null ? null : Replan);

        Title = plan.Title;
        Width = plan.Width;
        Height = plan.Height + heightAdjustment;
        MinWidth = plan.MinimumWidth;
        MinHeight = plan.MinimumHeight;
        CanResize = plan.IsResizable;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = _form.Content;

        void Replan(ChartOptionsDialogFieldId fieldId)
        {
            var updated = replan!.Invoke(_session, fieldId, _form.SelectedIndex(fieldId));
            if (updated is not null)
                _form.ApplyPlan(updated);
        }
    }

    protected ChartOptionsDialogValues CaptureValues() => _form.CaptureValues();

    protected int SelectedIndex(ChartOptionsDialogFieldId fieldId) => _form.SelectedIndex(fieldId);

    protected void ApplyValues(ChartOptionsDialogValues values) => _form.ApplyValues(values);

    private void Accept()
    {
        if (_submit(_session, _form.CaptureValues()))
            Close(true);
    }
}
