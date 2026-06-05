using System.Windows.Controls;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class AutoFilterDialogTests
{
    private static string ReadAutoFilterDialogSources()
    {
        return DialogSourceTestSupport.ReadHostSources(
            "AutoFilterDialog.cs",
            "AutoFilterDialog.Controls.cs",
            "AutoFilterDialog.Criteria.cs",
            "AutoFilterDialogCriteriaPlanner.cs",
            "AutoFilterDialog.State.cs",
            "AutoFilterDialogModel.cs");
    }

}
