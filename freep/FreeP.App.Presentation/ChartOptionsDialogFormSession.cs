namespace FreeP.App.Compositor;

/// <summary>
/// Owns renderer-neutral chart-options form state while native hosts retain control creation,
/// value conversion, visibility realization, and focus.
/// </summary>
public sealed class ChartOptionsDialogFormSession<TControl, TRow>
    where TControl : class
    where TRow : class
{
    private readonly Dictionary<ChartOptionsDialogFieldId, TControl> _controls = [];
    private readonly Dictionary<ChartOptionsDialogFieldId, TRow> _rows = [];
    private readonly Func<TControl, ChartOptionsDialogFieldValue> _captureValue;
    private readonly Action<TControl, ChartOptionsDialogFieldValue> _applyValue;
    private readonly Action<TControl, ChartOptionsDialogFieldPlan> _applyPlan;
    private readonly Action<TRow, bool> _setRowVisibility;

    public ChartOptionsDialogFormSession(
        Func<TControl, ChartOptionsDialogFieldValue> captureValue,
        Action<TControl, ChartOptionsDialogFieldValue> applyValue,
        Action<TControl, ChartOptionsDialogFieldPlan> applyPlan,
        Action<TRow, bool> setRowVisibility)
    {
        _captureValue = captureValue ?? throw new ArgumentNullException(nameof(captureValue));
        _applyValue = applyValue ?? throw new ArgumentNullException(nameof(applyValue));
        _applyPlan = applyPlan ?? throw new ArgumentNullException(nameof(applyPlan));
        _setRowVisibility = setRowVisibility ?? throw new ArgumentNullException(nameof(setRowVisibility));
    }

    public bool IsApplyingPlan { get; private set; } = true;

    public void Register(ChartOptionsDialogFieldId fieldId, TControl control, TRow row)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(row);
        _controls.Add(fieldId, control);
        _rows.Add(fieldId, row);
    }

    public void CompleteInitialRender() => IsApplyingPlan = false;

    public ChartOptionsDialogValues CaptureValues() => new(
        _controls.ToDictionary(
            pair => pair.Key,
            pair => Normalize(_captureValue(pair.Value))));

    public ChartOptionsDialogFieldValue Value(ChartOptionsDialogFieldId fieldId) =>
        _controls.TryGetValue(fieldId, out var control)
            ? Normalize(_captureValue(control))
            : throw new KeyNotFoundException($"The chart options form does not define {fieldId}.");

    public string Text(ChartOptionsDialogFieldId fieldId) => Value(fieldId).Text;

    public int SelectedIndex(ChartOptionsDialogFieldId fieldId) => Value(fieldId).SelectedIndex;

    public bool IsChecked(ChartOptionsDialogFieldId fieldId) => Value(fieldId).IsChecked == true;

    public bool? NullableChecked(ChartOptionsDialogFieldId fieldId) => Value(fieldId).IsChecked;

    public void ApplyValues(ChartOptionsDialogValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        foreach (var (fieldId, value) in values.Fields)
        {
            if (_controls.TryGetValue(fieldId, out var control))
                _applyValue(control, value);
        }
    }

    public void ApplyPlan(ChartOptionsDialogPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        IsApplyingPlan = true;
        try
        {
            foreach (var field in plan.Fields.Values)
            {
                if (!_controls.TryGetValue(field.Id, out var control))
                    continue;

                _applyPlan(control, field);
                _setRowVisibility(_rows[field.Id], field.IsVisible);
            }
        }
        finally
        {
            IsApplyingPlan = false;
        }
    }

    public bool TryGetControl(ChartOptionsDialogFieldId fieldId, out TControl control) =>
        _controls.TryGetValue(fieldId, out control!);

    private static ChartOptionsDialogFieldValue Normalize(ChartOptionsDialogFieldValue value) =>
        value.Text is null
            ? value with { Text = string.Empty }
            : value;
}
