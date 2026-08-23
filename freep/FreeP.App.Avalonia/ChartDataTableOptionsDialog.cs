using System.Globalization;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed partial class ChartDataTableOptionsDialog : ChartOptionsDialogHost<ChartDataTableOptionsDialogSession>
{
    internal ChartDataTableOptionsDialog(EditingSession editor)
        : this(new ChartDataTableOptionsDialogSession(editor))
    {
    }

    private ChartDataTableOptionsDialog(ChartDataTableOptionsDialogSession session)
        : base(session, session.BuildDialogPlan(CultureInfo.CurrentCulture), Submit)
    {
    }

    private static bool Submit(ChartDataTableOptionsDialogSession session, ChartOptionsDialogValues values) =>
        session.TryCommit(session.BuildInput(values), CultureInfo.CurrentCulture).Succeeded;
}
