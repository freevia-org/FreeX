using System.Globalization;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed partial class ChartLayoutOptionsDialog : ChartOptionsDialogHost<ChartLayoutOptionsDialogSession>
{
    internal ChartLayoutOptionsDialog(EditingSession editor)
        : this(new ChartLayoutOptionsDialogSession(editor))
    {
    }

    private ChartLayoutOptionsDialog(ChartLayoutOptionsDialogSession session)
        : base(session, session.BuildDialogPlan(CultureInfo.CurrentCulture), Submit, Replan)
    {
    }

    private static ChartOptionsDialogPlan? Replan(
        ChartLayoutOptionsDialogSession session,
        ChartOptionsDialogFieldId fieldId,
        int selectedIndex) =>
        session.TryApplySelectionChange(fieldId, selectedIndex, out var plan) ? plan : null;

    private static bool Submit(ChartLayoutOptionsDialogSession session, ChartOptionsDialogValues values) =>
        session.TryCommit(session.BuildInput(values), CultureInfo.CurrentCulture).Succeeded;
}
