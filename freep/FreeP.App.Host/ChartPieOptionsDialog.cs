using System.Globalization;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style first-slice, doughnut-hole, and OfPie options dialog.</summary>
public sealed partial class ChartPieOptionsDialog : ChartOptionsDialogHost<ChartPieOptionsDialogSession>
{
    public ChartPieOptionsDialog(EditingSession editor)
        : this(new ChartPieOptionsDialogSession(editor))
    {
    }

    private ChartPieOptionsDialog(ChartPieOptionsDialogSession session)
        : base(session, session.BuildDialogPlan(CultureInfo.CurrentCulture), Submit)
    {
    }

    private static ChartOptionsDialogSubmission Submit(
        ChartPieOptionsDialogSession session,
        ChartOptionsDialogValues values)
    {
        var result = session.TryCommit(session.BuildInput(values), CultureInfo.CurrentCulture);
        return new(result.Succeeded, result.Error);
    }
}
