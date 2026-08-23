using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed partial class ChartPointOptionsDialog : ChartOptionsDialogHost<ChartPointOptionsDialogSession>
{
    internal ChartPointOptionsDialog(
        EditingSession editor,
        int? initialSeriesIndex = null,
        int? initialPointIndex = null)
        : this(new ChartPointOptionsDialogSession(editor, initialSeriesIndex, initialPointIndex))
    {
    }

    private ChartPointOptionsDialog(ChartPointOptionsDialogSession session)
        : base(session, session.BuildDialogPlan(), Submit, Replan)
    {
    }

    private static ChartOptionsDialogPlan? Replan(
        ChartPointOptionsDialogSession session,
        ChartOptionsDialogFieldId fieldId,
        int selectedIndex) =>
        session.TryApplySelectionChange(fieldId, selectedIndex, out var plan) ? plan : null;

    private static bool Submit(ChartPointOptionsDialogSession session, ChartOptionsDialogValues values) =>
        session.TryCommit(session.BuildInput(values)).Succeeded;
}
