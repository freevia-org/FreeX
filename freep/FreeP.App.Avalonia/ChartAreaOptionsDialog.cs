using System.Globalization;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed partial class ChartAreaOptionsDialog : ChartOptionsDialogHost<ChartAreaOptionsDialogSession>
{
    internal ChartAreaOptionsDialog(EditingSession editor, ChartAreaFormattingTarget? initialTarget = null)
        : this(new ChartAreaOptionsDialogSession(editor, initialTarget))
    {
    }

    private ChartAreaOptionsDialog(ChartAreaOptionsDialogSession session)
        : base(session, session.BuildDialogPlan(CultureInfo.CurrentCulture), Submit, Replan)
    {
    }

    private static ChartOptionsDialogPlan? Replan(
        ChartAreaOptionsDialogSession session,
        ChartOptionsDialogFieldId fieldId,
        int selectedIndex) =>
        session.TryApplySelectionChange(fieldId, selectedIndex, out var plan) ? plan : null;

    private static bool Submit(ChartAreaOptionsDialogSession session, ChartOptionsDialogValues values) =>
        session.TryCommit(session.BuildInput(values), CultureInfo.CurrentCulture).Succeeded;
}
