using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed partial class ChartDisplayOptionsDialog : ChartOptionsDialogHost<ChartDisplayOptionsDialogSession>
{
    internal ChartDisplayOptionsDialog(EditingSession editor)
        : this(new ChartDisplayOptionsDialogSession(editor))
    {
    }

    private ChartDisplayOptionsDialog(ChartDisplayOptionsDialogSession session)
        : base(session, session.BuildDialogPlan(), Submit)
    {
    }

    private static bool Submit(ChartDisplayOptionsDialogSession session, ChartOptionsDialogValues values) =>
        session.Submit(session.BuildInput(values)).ShouldClose;
}
