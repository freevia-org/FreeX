using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed partial class ChartTextOptionsDialog : ChartOptionsDialogHost<ChartTextOptionsDialogSession>
{
    internal ChartTextOptionsDialog(EditingSession editor, ChartTextTarget target = ChartTextTarget.Chart)
        : this(new ChartTextOptionsDialogSession(editor, target))
    {
    }

    private ChartTextOptionsDialog(ChartTextOptionsDialogSession session)
        : base(session, session.BuildDialogPlan(), Submit)
    {
    }

    private static bool Submit(ChartTextOptionsDialogSession session, ChartOptionsDialogValues values) =>
        session.Submit(session.BuildInput(values)).ShouldClose;
}
