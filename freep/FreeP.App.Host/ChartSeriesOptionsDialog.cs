using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style per-series chart formatting dialog.</summary>
public sealed partial class ChartSeriesOptionsDialog : ChartOptionsDialogHost<ChartSeriesOptionsDialogSession>
{
    public ChartSeriesOptionsDialog(EditingSession editor, int? initialSeriesIndex = null)
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

    private static ChartOptionsDialogSubmission Submit(
        ChartSeriesOptionsDialogSession session,
        ChartOptionsDialogValues values)
    {
        var result = session.TryCommit(session.BuildInput(values));
        return new(result.Succeeded, result.Error);
    }
}
