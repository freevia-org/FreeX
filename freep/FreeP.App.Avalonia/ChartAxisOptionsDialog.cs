using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed partial class ChartAxisOptionsDialog : ChartOptionsDialogHost<ChartAxisOptionsDialogSession>
{
    internal ChartAxisOptionsDialog(EditingSession editor, ChartAxisKind? initialAxis = null)
        : this(new ChartAxisOptionsDialogSession(editor, initialAxis))
    {
    }

    private ChartAxisOptionsDialog(ChartAxisOptionsDialogSession session)
        : base(session, session.BuildDialogPlan(), Submit, Replan)
    {
    }

    private static ChartOptionsDialogPlan? Replan(
        ChartAxisOptionsDialogSession session,
        ChartOptionsDialogFieldId fieldId,
        int selectedIndex) =>
        session.TryApplySelectionChange(fieldId, selectedIndex, out var plan) ? plan : null;

    private static bool Submit(ChartAxisOptionsDialogSession session, ChartOptionsDialogValues values) =>
        session.Submit(session.BuildInput(values)).ShouldClose;
}
