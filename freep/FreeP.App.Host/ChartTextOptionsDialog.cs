using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style chart-wide default text formatting dialog.</summary>
public sealed partial class ChartTextOptionsDialog : ChartOptionsDialogHost<ChartTextOptionsDialogSession>
{
    public ChartTextOptionsDialog(EditingSession editor, ChartTextTarget target = ChartTextTarget.Chart)
        : this(new ChartTextOptionsDialogSession(editor, target))
    {
    }

    private ChartTextOptionsDialog(ChartTextOptionsDialogSession session)
        : base(session, session.BuildDialogPlan(), Submit)
    {
    }

    private static ChartOptionsDialogSubmission Submit(
        ChartTextOptionsDialogSession session,
        ChartOptionsDialogValues values)
    {
        var result = session.Submit(session.BuildInput(values));
        return new(result.ShouldClose, result.ValidationMessage);
    }
}
