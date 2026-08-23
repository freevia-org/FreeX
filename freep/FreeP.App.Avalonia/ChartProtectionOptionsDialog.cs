using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed partial class ChartProtectionOptionsDialog : ChartOptionsDialogHost<ChartProtectionOptionsDialogSession>
{
    internal ChartProtectionOptionsDialog(EditingSession editor)
        : this(new ChartProtectionOptionsDialogSession(editor))
    {
    }

    private ChartProtectionOptionsDialog(ChartProtectionOptionsDialogSession session)
        : base(session, session.BuildDialogPlan(), Submit)
    {
    }

    private static bool Submit(ChartProtectionOptionsDialogSession session, ChartOptionsDialogValues values)
    {
        session.Submit(session.BuildInput(values));
        return true;
    }
}
