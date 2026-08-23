using FreeP.App.Compositor;

namespace FreeP.App.Host;

public sealed partial class ChartExSeriesLayoutDialog : ChartOptionsDialogHost<ChartExSeriesLayoutDialogSession>
{
    public ChartExSeriesLayoutDialog(EditingSession editor)
        : this(new ChartExSeriesLayoutDialogSession(editor))
    {
    }

    private ChartExSeriesLayoutDialog(ChartExSeriesLayoutDialogSession session)
        : base(session, session.BuildDialogPlan(), Submit, Replan)
    {
    }

    private int SelectedLayoutIndex => SelectedIndex(ChartOptionsDialogFieldId.ChartExLayout);

    private static ChartOptionsDialogPlan? Replan(
        ChartExSeriesLayoutDialogSession session,
        ChartOptionsDialogFieldId fieldId,
        int selectedIndex) =>
        session.TryApplySelectionChange(fieldId, selectedIndex, out var plan) ? plan : null;

    private static ChartOptionsDialogSubmission Submit(
        ChartExSeriesLayoutDialogSession session,
        ChartOptionsDialogValues values) =>
        session.TryApply(values.SelectedIndex(ChartOptionsDialogFieldId.ChartExLayout), out var error)
            ? ChartOptionsDialogSubmission.Accepted
            : new(false, error);
}
