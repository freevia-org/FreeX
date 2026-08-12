using System.Windows.Controls;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class AutoFilterDialogTests
{
    private static string ReadAutoFilterDialogSources()
    {
        var hostSources = DialogSourceTestSupport.ReadHostSources(
            "AutoFilterDialog.cs",
            "AutoFilterDialog.Controls.cs",
            "AutoFilterDialog.State.cs");

        // The pure criteria planner and the dialog/menu model types moved to the portable
        // FreeX.App.Presentation layer; include them so the combined source still covers the
        // full AutoFilter dialog behavior.
        var portableSources = string.Join(
            Environment.NewLine,
            WorkspaceFileLocator.ReadAllText(
                "src", "FreeX.App.Presentation", "Filtering", "AutoFilterDialogCriteriaPlanner.cs"),
            WorkspaceFileLocator.ReadAllText(
                "src", "FreeX.App.Presentation", "Filtering", "AutoFilterDialogModel.cs"),
            WorkspaceFileLocator.ReadAllText(
                "src", "FreeX.App.Presentation", "Filtering", "AutoFilterMenuPlanner.cs"));

        return string.Join(Environment.NewLine, hostSources, portableSources);
    }

}
