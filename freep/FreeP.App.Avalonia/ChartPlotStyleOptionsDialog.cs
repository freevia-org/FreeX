using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed partial class ChartPlotStyleOptionsDialog : ChartOptionsDialogHost<ChartPlotStyleOptionsDialogSession>
{
    internal ChartPlotStyleOptionsDialog(EditingSession editor)
        : this(new ChartPlotStyleOptionsDialogSession(editor))
    {
    }

    private ChartPlotStyleOptionsDialog(ChartPlotStyleOptionsDialogSession session)
        : base(session, session.BuildDialogPlan(), Submit)
    {
    }

    private static bool Submit(ChartPlotStyleOptionsDialogSession session, ChartOptionsDialogValues values)
    {
        session.Submit(session.BuildInput(values));
        return true;
    }
}
