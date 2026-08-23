using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style chart display options dialog.</summary>
public sealed partial class ChartDisplayOptionsDialog : ChartOptionsDialogHost<ChartDisplayOptionsDialogSession>
{
    public ChartDisplayOptionsDialog(EditingSession editor)
        : this(new ChartDisplayOptionsDialogSession(editor))
    {
    }

    private ChartDisplayOptionsDialog(ChartDisplayOptionsDialogSession session)
        : base(session, session.BuildDialogPlan(), Submit)
    {
    }

    private static ChartOptionsDialogSubmission Submit(
        ChartDisplayOptionsDialogSession session,
        ChartOptionsDialogValues values)
    {
        var result = session.Submit(session.BuildInput(values));
        return new(result.ShouldClose, result.ValidationMessage);
    }
}
