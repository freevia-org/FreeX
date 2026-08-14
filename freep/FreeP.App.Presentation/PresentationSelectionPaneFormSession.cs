namespace FreeP.App.Compositor;

/// <summary>Applies Selection Pane plans and transitions to renderer-owned rows.</summary>
public sealed class PresentationSelectionPaneFormSession<TRow>
    where TRow : class
{
    private readonly PresentationSelectionPaneSession _session;
    private readonly Action<string> _setStatus;
    private readonly Action _clearRows;
    private readonly Func<PresentationSelectionPaneItemPlan, int, PresentationSelectionPaneItemSession, TRow> _buildRow;
    private readonly Action<TRow> _addRow;
    private readonly Action<PresentationSelectionPanePlan> _applyAccessibility;
    private readonly Action? _onAccessibilityChanged;

    public PresentationSelectionPaneFormSession(
        PresentationSelectionPaneSession session,
        Action<string> setStatus,
        Action clearRows,
        Func<PresentationSelectionPaneItemPlan, int, PresentationSelectionPaneItemSession, TRow> buildRow,
        Action<TRow> addRow,
        Action<PresentationSelectionPanePlan> applyAccessibility,
        Action? onAccessibilityChanged = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _setStatus = setStatus ?? throw new ArgumentNullException(nameof(setStatus));
        _clearRows = clearRows ?? throw new ArgumentNullException(nameof(clearRows));
        _buildRow = buildRow ?? throw new ArgumentNullException(nameof(buildRow));
        _addRow = addRow ?? throw new ArgumentNullException(nameof(addRow));
        _applyAccessibility = applyAccessibility ?? throw new ArgumentNullException(nameof(applyAccessibility));
        _onAccessibilityChanged = onAccessibilityChanged;
    }

    public PresentationSelectionPanePlan CurrentPlan => _session.CurrentPlan;

    public PresentationSelectionPanePlan SetEditor(EditingSession editor) =>
        Render(_session.SetEditor(editor));

    public PresentationSelectionPanePlan Refresh() => Render(_session.Refresh());

    public PresentationSelectionPanePlan Render(PresentationSelectionPanePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _setStatus(plan.StatusText);
        _clearRows();
        for (var index = 0; index < plan.Items.Count; index++)
        {
            var item = plan.Items[index];
            _addRow(_buildRow(item, index, _session.CreateItemSession(item.ShapeId)));
        }

        _applyAccessibility(plan);
        _onAccessibilityChanged?.Invoke();
        return plan;
    }

    public void ApplyTransition(
        PresentationSelectionPaneTransitionPlan transition,
        Action<string>? restoreName = null)
    {
        ArgumentNullException.ThrowIfNull(transition);
        if (transition.RestoreNameText is { } name)
            restoreName?.Invoke(name);
        if (transition.ShouldRefreshPane)
            Render(transition.PanePlan);
    }
}
