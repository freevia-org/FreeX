namespace FreeP.App.Compositor;

public sealed record PresentationSmartArtTextPaneRowProjection(
    SmartArtNodeOutlineItem Item,
    PresentationPaneAccessibilityItemPlan Accessibility);

public sealed record PresentationSmartArtTextPaneHostProjection(
    string Heading,
    string Message,
    bool CanApply,
    bool CanToggleAssistant,
    bool CanEditSelectedRow,
    IReadOnlyList<PresentationSmartArtTextPaneRowProjection> Rows);

public static class PresentationMainWindowPaneRenderProjection
{
    public static PresentationSmartArtTextPaneHostProjection ProjectSmartArtTextPane(
        PresentationSmartArtTextPanePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var rows = plan.Rows.Select((item, index) =>
            new PresentationSmartArtTextPaneRowProjection(
                item,
                PresentationPaneAccessibilityPlanner.PlanItem(
                    PresentationPaneAccessibilityPlanner.SmartArtTextPaneId,
                    index,
                    item.Text,
                    StringComparer.Ordinal.Equals(item.ModelId, plan.SelectedModelId),
                    item.ModelId))).ToArray();

        return new(
            plan.Heading,
            plan.Message,
            plan.CanApply,
            plan.CanToggleAssistant,
            plan.CanEditSelectedRow,
            rows);
    }
}

public sealed record PresentationSmartArtTextPaneNativeViewBindings<TNativeRow>(
    Action<bool> SetUpdating,
    Action ClearRows,
    Action<string> SetHeading,
    Action<string> SetMessage,
    Action<bool> SetApplyEnabled,
    Action<bool> SetAssistantEnabled,
    Action<bool> SetEditActionsEnabled,
    Func<SmartArtNodeOutlineItem, TNativeRow> BuildRow,
    Action<TNativeRow, PresentationPaneAccessibilityItemPlan> ApplyAccessibility,
    Action<TNativeRow> AddRow);

public sealed class PresentationSmartArtTextPaneNativeViewAdapter<TNativeRow>
{
    private readonly PresentationSmartArtTextPaneNativeViewBindings<TNativeRow> _bindings;

    public PresentationSmartArtTextPaneNativeViewAdapter(
        PresentationSmartArtTextPaneNativeViewBindings<TNativeRow> bindings)
    {
        _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
    }

    public void Render(PresentationSmartArtTextPanePlan plan)
    {
        var projection = PresentationMainWindowPaneRenderProjection.ProjectSmartArtTextPane(plan);
        _bindings.SetUpdating(true);
        try
        {
            _bindings.ClearRows();
            _bindings.SetHeading(projection.Heading);
            _bindings.SetMessage(projection.Message);
            _bindings.SetApplyEnabled(projection.CanApply);
            _bindings.SetAssistantEnabled(projection.CanToggleAssistant);
            _bindings.SetEditActionsEnabled(projection.CanEditSelectedRow);
            foreach (var rowPlan in projection.Rows)
            {
                var row = _bindings.BuildRow(rowPlan.Item);
                _bindings.ApplyAccessibility(row, rowPlan.Accessibility);
                _bindings.AddRow(row);
            }
        }
        finally
        {
            _bindings.SetUpdating(false);
        }
    }
}

public sealed record PresentationProofingPaneNativeViewBindings<TNativeRow>(
    Action<string> SetHeading,
    Action<string> SetMessage,
    Action ClearRows,
    Action<string> AddEmptyState,
    Func<PresentationProofingIssueRowPlan, TNativeRow> BuildRow,
    Action<TNativeRow> AddRow);

public sealed class PresentationProofingPaneNativeViewAdapter<TNativeRow>
{
    private readonly PresentationProofingPaneNativeViewBindings<TNativeRow> _bindings;

    public PresentationProofingPaneNativeViewAdapter(
        PresentationProofingPaneNativeViewBindings<TNativeRow> bindings)
    {
        _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
    }

    public void Render(PresentationProofingPanePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _bindings.SetHeading(plan.Heading);
        _bindings.SetMessage(plan.DisplayMessage);
        _bindings.ClearRows();
        if (plan.ShouldShowEmptyState)
        {
            _bindings.AddEmptyState(plan.Message);
            return;
        }

        foreach (var row in plan.Rows)
            _bindings.AddRow(_bindings.BuildRow(row));
    }
}
