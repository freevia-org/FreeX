using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style bubble chart sizing options dialog.</summary>
public sealed partial class ChartBubbleOptionsDialog : ChartOptionsDialogHost<ChartBubbleOptionsDialogSession>
{
    public ChartBubbleOptionsDialog(EditingSession editor)
        : this(new ChartBubbleOptionsDialogSession(editor))
    {
    }

    private ChartBubbleOptionsDialog(ChartBubbleOptionsDialogSession session)
        : base(session, session.BuildDialogPlan(), Submit)
    {
    }

    private static ChartOptionsDialogSubmission Submit(
        ChartBubbleOptionsDialogSession session,
        ChartOptionsDialogValues values)
    {
        var result = session.Submit(session.BuildInput(values));
        return new(result.ShouldClose, result.ValidationMessage);
    }
}
