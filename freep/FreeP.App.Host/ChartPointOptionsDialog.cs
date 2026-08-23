using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style per-point chart formatting dialog.</summary>
public sealed partial class ChartPointOptionsDialog : ChartOptionsDialogHost<ChartPointOptionsDialogSession>
{
    public ChartPointOptionsDialog(
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

    private static ChartOptionsDialogSubmission Submit(
        ChartPointOptionsDialogSession session,
        ChartOptionsDialogValues values)
    {
        var result = session.TryCommit(session.BuildInput(values));
        return new(result.Succeeded, result.Error);
    }
}
