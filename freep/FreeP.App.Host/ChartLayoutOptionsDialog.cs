using System.Globalization;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style plot-area and legend manual-layout dialog.</summary>
public sealed partial class ChartLayoutOptionsDialog : ChartOptionsDialogHost<ChartLayoutOptionsDialogSession>
{
    public ChartLayoutOptionsDialog(EditingSession editor)
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

    private static ChartOptionsDialogSubmission Submit(
        ChartLayoutOptionsDialogSession session,
        ChartOptionsDialogValues values)
    {
        var result = session.TryCommit(session.BuildInput(values), CultureInfo.CurrentCulture);
        return new(result.Succeeded, result.Error);
    }
}
