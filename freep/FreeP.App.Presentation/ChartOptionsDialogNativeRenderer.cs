namespace FreeP.App.Compositor;

/// <summary>Owns portable group, field-kind, and hint ordering for native chart-option forms.</summary>
public sealed class ChartOptionsDialogNativeRenderer<TControl, TRow>
    where TControl : class
    where TRow : class
{
    private readonly Func<ChartOptionsDialogFieldPlan, TControl> _createText;
    private readonly Func<ChartOptionsDialogFieldPlan, TControl> _createChoice;
    private readonly Func<ChartOptionsDialogFieldPlan, TControl> _createToggle;
    private readonly Func<ChartOptionsDialogFieldPlan, TControl, TRow> _createRow;
    private readonly Action<string, bool> _addHeader;
    private readonly Action<TRow> _addRow;
    private readonly Action<string> _addHint;

    public ChartOptionsDialogNativeRenderer(
        Func<ChartOptionsDialogFieldPlan, TControl> createText,
        Func<ChartOptionsDialogFieldPlan, TControl> createChoice,
        Func<ChartOptionsDialogFieldPlan, TControl> createToggle,
        Func<ChartOptionsDialogFieldPlan, TControl, TRow> createRow,
        Action<string, bool> addHeader,
        Action<TRow> addRow,
        Action<string> addHint)
    {
        _createText = createText ?? throw new ArgumentNullException(nameof(createText));
        _createChoice = createChoice ?? throw new ArgumentNullException(nameof(createChoice));
        _createToggle = createToggle ?? throw new ArgumentNullException(nameof(createToggle));
        _createRow = createRow ?? throw new ArgumentNullException(nameof(createRow));
        _addHeader = addHeader ?? throw new ArgumentNullException(nameof(addHeader));
        _addRow = addRow ?? throw new ArgumentNullException(nameof(addRow));
        _addHint = addHint ?? throw new ArgumentNullException(nameof(addHint));
    }

    public void Render(ChartOptionsDialogPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var hasContent = false;
        foreach (var group in plan.Groups)
        {
            if (!string.IsNullOrWhiteSpace(group.Header))
            {
                _addHeader(group.Header, hasContent);
                hasContent = true;
            }

            foreach (var field in group.Fields)
            {
                var control = field.ControlKind switch
                {
                    ChartOptionsDialogControlKind.Text => _createText(field),
                    ChartOptionsDialogControlKind.Choice => _createChoice(field),
                    ChartOptionsDialogControlKind.Toggle => _createToggle(field),
                    _ => throw new ArgumentOutOfRangeException(nameof(field.ControlKind)),
                };
                _addRow(_createRow(field, control));
                hasContent = true;
            }
        }

        if (!string.IsNullOrWhiteSpace(plan.Hint))
            _addHint(plan.Hint);
    }
}
