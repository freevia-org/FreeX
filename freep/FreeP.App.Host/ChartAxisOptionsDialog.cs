using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style chart axis scale and display dialog.</summary>
public sealed partial class ChartAxisOptionsDialog : ChartOptionsDialogHost<ChartAxisOptionsDialogSession>
{
    public ChartAxisOptionsDialog(EditingSession editor, ChartAxisKind? initialAxis = null)
        : this(new ChartAxisOptionsDialogSession(editor, initialAxis))
    {
    }

    private ChartAxisOptionsDialog(ChartAxisOptionsDialogSession session)
        : base(session, session.BuildDialogPlan(), Submit, Replan)
    {
    }

    private static ChartOptionsDialogPlan? Replan(
        ChartAxisOptionsDialogSession session,
        ChartOptionsDialogFieldId fieldId,
        int selectedIndex) =>
        session.TryApplySelectionChange(fieldId, selectedIndex, out var plan) ? plan : null;

    private static ChartOptionsDialogSubmission Submit(
        ChartAxisOptionsDialogSession session,
        ChartOptionsDialogValues values)
    {
        var result = session.Submit(session.BuildInput(values));
        return new(result.ShouldClose, result.ValidationMessage);
    }
}
