using System.Globalization;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style chart-area and plot-area formatting dialog.</summary>
public sealed partial class ChartAreaOptionsDialog : ChartOptionsDialogHost<ChartAreaOptionsDialogSession>
{
    public ChartAreaOptionsDialog(EditingSession editor, ChartAreaFormattingTarget? initialTarget = null)
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

    private static ChartOptionsDialogSubmission Submit(
        ChartAreaOptionsDialogSession session,
        ChartOptionsDialogValues values)
    {
        var result = session.TryCommit(session.BuildInput(values), CultureInfo.CurrentCulture);
        return new(result.Succeeded, result.Error);
    }
}
