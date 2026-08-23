namespace FreeP.App.Compositor;

/// <summary>Owns item-action and accessibility projection for a native Selection Pane row.</summary>
public sealed class PresentationSelectionPaneItemFormSession
{
    private readonly PresentationSelectionPaneItemSession _item;
    private readonly Action<PresentationSelectionPaneTransitionPlan, Action<string>?> _apply;

    public PresentationSelectionPaneItemFormSession(
        PresentationSelectionPaneItemSession item,
        PresentationSelectionPaneItemPlan plan,
        int index,
        Action<PresentationSelectionPaneTransitionPlan, Action<string>?> apply)
    {
        _item = item ?? throw new ArgumentNullException(nameof(item));
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        AccessibilityPlan = PresentationPaneAccessibilityPlanner.PlanItem(
            PresentationPaneAccessibilityPlanner.SelectionPaneId,
            index,
            plan.ShapeName,
            plan.IsSelected,
            PresentationPaneAccessibilityPlanner.BuildShapeKey(plan.ShapeId));
    }

    public PresentationSelectionPaneItemPlan Plan { get; }

    public PresentationPaneAccessibilityItemPlan AccessibilityPlan { get; }

    public void Select() => Apply(_item.Select());

    public void CommitRename(string? name, Action<string> restoreName)
    {
        if (string.Equals(name, Plan.ShapeName, StringComparison.Ordinal))
            return;

        _apply(_item.CommitRename(name), restoreName);
    }

    public void CancelRename() => Apply(_item.CancelRename());

    public void ToggleVisibility() => Apply(_item.ToggleVisibility());

    public void MoveTowardFront() => Apply(_item.MoveTowardFront());

    public void MoveTowardBack() => Apply(_item.MoveTowardBack());

    private void Apply(PresentationSelectionPaneTransitionPlan transition) =>
        _apply(transition, null);
}
