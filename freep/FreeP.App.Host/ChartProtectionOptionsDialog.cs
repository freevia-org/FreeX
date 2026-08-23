using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style chart object/data/formatting/selection protection dialog.</summary>
public sealed partial class ChartProtectionOptionsDialog : ChartOptionsDialogHost<ChartProtectionOptionsDialogSession>
{
    public ChartProtectionOptionsDialog(EditingSession editor)
        : this(new ChartProtectionOptionsDialogSession(editor))
    {
    }

    private ChartProtectionOptionsDialog(ChartProtectionOptionsDialogSession session)
        : base(session, session.BuildDialogPlan(), Submit)
    {
    }

    private static ChartOptionsDialogSubmission Submit(
        ChartProtectionOptionsDialogSession session,
        ChartOptionsDialogValues values)
    {
        session.Submit(session.BuildInput(values));
        return ChartOptionsDialogSubmission.Accepted;
    }
}
