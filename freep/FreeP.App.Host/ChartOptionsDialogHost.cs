using System.Windows;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

public readonly record struct ChartOptionsDialogSubmission(bool ShouldClose, string? ValidationMessage)
{
    public static ChartOptionsDialogSubmission Accepted { get; } = new(true, null);
}

/// <summary>Renderer-local host for the shared chart-options dialog chrome.</summary>
public abstract class ChartOptionsDialogHost<TSession> : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private protected readonly TSession _session;
    private protected readonly ChartOptionsDialogForm _form = null!;
    private readonly Func<TSession, ChartOptionsDialogValues, ChartOptionsDialogSubmission> _submit;

    protected ChartOptionsDialogHost(
        TSession session,
        ChartOptionsDialogPlan plan,
        Func<TSession, ChartOptionsDialogValues, ChartOptionsDialogSubmission> submit,
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
            Close,
            replan is null ? null : Replan);

        Title = plan.Title;
        Width = plan.Width;
        Height = plan.Height + heightAdjustment;
        MinWidth = plan.MinimumWidth;
        MinHeight = plan.MinimumHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
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
        var result = _submit(_session, _form.CaptureValues());
        if (result.ShouldClose)
        {
            DialogResult = true;
            return;
        }

        if (!string.IsNullOrWhiteSpace(result.ValidationMessage))
            DialogMessageHelper.ShowWarning(this, result.ValidationMessage, Title);
    }
}
