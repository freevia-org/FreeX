using System.Globalization;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed partial class ChartPieOptionsDialog : ChartOptionsDialogHost<ChartPieOptionsDialogSession>
{
    internal ChartPieOptionsDialog(EditingSession editor)
        : this(new ChartPieOptionsDialogSession(editor))
    {
    }

    private ChartPieOptionsDialog(ChartPieOptionsDialogSession session)
        : base(session, session.BuildDialogPlan(CultureInfo.CurrentCulture), Submit)
    {
    }

    private static bool Submit(ChartPieOptionsDialogSession session, ChartOptionsDialogValues values) =>
        session.TryCommit(session.BuildInput(values), CultureInfo.CurrentCulture).Succeeded;
}
