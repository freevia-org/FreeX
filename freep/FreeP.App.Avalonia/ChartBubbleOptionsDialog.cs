using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed partial class ChartBubbleOptionsDialog : ChartOptionsDialogHost<ChartBubbleOptionsDialogSession>
{
    internal ChartBubbleOptionsDialog(EditingSession editor)
        : this(new ChartBubbleOptionsDialogSession(editor))
    {
    }

    private ChartBubbleOptionsDialog(ChartBubbleOptionsDialogSession session)
        : base(session, session.BuildDialogPlan(), Submit)
    {
    }

    private static bool Submit(ChartBubbleOptionsDialogSession session, ChartOptionsDialogValues values) =>
        session.Submit(session.BuildInput(values)).ShouldClose;
}
