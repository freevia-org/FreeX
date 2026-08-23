using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style Scatter/Radar plot-style dialog.</summary>
public sealed partial class ChartPlotStyleOptionsDialog : ChartOptionsDialogHost<ChartPlotStyleOptionsDialogSession>
{
    public ChartPlotStyleOptionsDialog(EditingSession editor)
        : this(new ChartPlotStyleOptionsDialogSession(editor))
    {
    }

    private ChartPlotStyleOptionsDialog(ChartPlotStyleOptionsDialogSession session)
        : base(session, session.BuildDialogPlan(), Submit)
    {
    }

    private static ChartOptionsDialogSubmission Submit(
        ChartPlotStyleOptionsDialogSession session,
        ChartOptionsDialogValues values)
    {
        session.Submit(session.BuildInput(values));
        return ChartOptionsDialogSubmission.Accepted;
    }
}
