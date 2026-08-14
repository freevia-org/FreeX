namespace FreeP.App.Compositor;

/// <summary>Shared public form surface for the WPF and Avalonia chart-option renderers.</summary>
public abstract class ChartOptionsDialogFormAdapter<TControl, TRow>
    where TControl : class
    where TRow : class
{
    private readonly Action<TControl> _focus;

    protected ChartOptionsDialogFormAdapter(
        Func<TControl, PresentationDialogFieldValue> captureValue,
        Action<TControl, PresentationDialogFieldValue> applyValue,
        Action<TControl, ChartOptionsDialogFieldPlan> applyPlan,
        Action<TRow, bool> setVisible,
        Action<TControl> focus)
    {
        FormSession = new(captureValue, applyValue, applyPlan, setVisible);
        _focus = focus ?? throw new ArgumentNullException(nameof(focus));
    }

    protected ChartOptionsDialogFormSession<TControl, TRow> FormSession { get; }

    public ChartOptionsDialogValues CaptureValues() => FormSession.CaptureValues();

    public string Text(ChartOptionsDialogFieldId fieldId) => FormSession.Text(fieldId);

    public int SelectedIndex(ChartOptionsDialogFieldId fieldId) =>
        FormSession.SelectedIndex(fieldId);

    public bool IsChecked(ChartOptionsDialogFieldId fieldId) => FormSession.IsChecked(fieldId);

    public bool? NullableChecked(ChartOptionsDialogFieldId fieldId) =>
        FormSession.NullableChecked(fieldId);

    public void ApplyValues(ChartOptionsDialogValues values) => FormSession.ApplyValues(values);

    public void ApplyPlan(ChartOptionsDialogPlan plan) => FormSession.ApplyPlan(plan);

    public void Focus(ChartOptionsDialogFieldId fieldId)
    {
        if (FormSession.TryGetControl(fieldId, out var control))
            _focus(control);
    }
}
