using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed partial class Chart3DViewOptionsDialog : ChartOptionsDialogHost<Chart3DViewOptionsDialogSession>
{
    internal Chart3DViewOptionsDialog(EditingSession editor)
        : this(new Chart3DViewOptionsDialogSession(editor))
    {
    }

    private Chart3DViewOptionsDialog(Chart3DViewOptionsDialogSession session)
        : base(session, session.BuildDialogPlan(), Submit, heightAdjustment: 36)
    {
    }

    private static bool Submit(Chart3DViewOptionsDialogSession session, ChartOptionsDialogValues values) =>
        session.Submit(session.BuildInput(values)).ShouldClose;
}
