namespace FreeP.App.Compositor;

internal static class ChartOptionsDialogSelectionTransition
{
    public static bool TryApply<TSelection>(
        ChartOptionsDialogFieldId fieldId,
        ChartOptionsDialogFieldId selectionFieldId,
        int selectedIndex,
        Func<int, TSelection> select,
        Func<ChartOptionsDialogPlan> buildPlan,
        out ChartOptionsDialogPlan plan)
    {
        ArgumentNullException.ThrowIfNull(select);
        ArgumentNullException.ThrowIfNull(buildPlan);

        if (fieldId != selectionFieldId)
        {
            plan = null!;
            return false;
        }

        _ = select(selectedIndex);
        plan = buildPlan();
        return true;
    }
}
