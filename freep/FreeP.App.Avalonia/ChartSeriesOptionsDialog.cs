using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed partial class ChartSeriesOptionsDialog : ChartOptionsDialogHost<ChartSeriesOptionsDialogSession>
{
    internal ChartSeriesOptionsDialog(EditingSession editor, int? initialSeriesIndex = null)
        : this(new ChartSeriesOptionsDialogSession(editor, initialSeriesIndex))
    {
    }

    private ChartSeriesOptionsDialog(ChartSeriesOptionsDialogSession session)
        : base(session, session.BuildDialogPlan(), Submit, Replan)
    {
    }

    private static ChartOptionsDialogPlan? Replan(
        ChartSeriesOptionsDialogSession session,
        ChartOptionsDialogFieldId fieldId,
        int selectedIndex) =>
        session.TryApplySelectionChange(fieldId, selectedIndex, out var plan) ? plan : null;

    private static bool Submit(ChartSeriesOptionsDialogSession session, ChartOptionsDialogValues values) =>
        session.TryCommit(session.BuildInput(values)).Succeeded;
}
