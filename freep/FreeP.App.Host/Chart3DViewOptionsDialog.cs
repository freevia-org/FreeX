using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style chart camera and Surface3D options dialog.</summary>
public sealed partial class Chart3DViewOptionsDialog : ChartOptionsDialogHost<Chart3DViewOptionsDialogSession>
{
    public Chart3DViewOptionsDialog(EditingSession editor)
        : this(new Chart3DViewOptionsDialogSession(editor))
    {
    }

    private Chart3DViewOptionsDialog(Chart3DViewOptionsDialogSession session)
        : base(session, session.BuildDialogPlan(), Submit, heightAdjustment: 36)
    {
    }

    private static ChartOptionsDialogSubmission Submit(
        Chart3DViewOptionsDialogSession session,
        ChartOptionsDialogValues values)
    {
        var result = session.Submit(session.BuildInput(values));
        return new(result.ShouldClose, result.ValidationMessage);
    }
}
