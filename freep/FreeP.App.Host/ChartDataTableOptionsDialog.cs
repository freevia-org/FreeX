using System.Globalization;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style chart data-table options dialog.</summary>
public sealed partial class ChartDataTableOptionsDialog : ChartOptionsDialogHost<ChartDataTableOptionsDialogSession>
{
    public ChartDataTableOptionsDialog(EditingSession editor)
        : this(new ChartDataTableOptionsDialogSession(editor))
    {
    }

    private ChartDataTableOptionsDialog(ChartDataTableOptionsDialogSession session)
        : base(session, session.BuildDialogPlan(CultureInfo.CurrentCulture), Submit)
    {
    }

    private static ChartOptionsDialogSubmission Submit(
        ChartDataTableOptionsDialogSession session,
        ChartOptionsDialogValues values)
    {
        var result = session.TryCommit(session.BuildInput(values), CultureInfo.CurrentCulture);
        return new(result.Succeeded, result.Error);
    }
}
