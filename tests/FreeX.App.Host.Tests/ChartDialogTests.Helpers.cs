using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ChartDialogTests
{
    private static string ReadChartDialogSource() =>
        DialogSourceTestSupport.ReadHostSources(
            "ChartDialogs.cs",
            "SelectDataSourceDialog.cs",
            "SelectDataSourceDialog.Planning.cs",
            "SelectDataSourceDialog.Controls.cs",
            "SelectDataSourceDialog.Actions.cs");

    private static string ReadChartTypeDialogSource() =>
        DialogSourceTestSupport.ReadHostSources(
            "ChartTypeDialogs.cs",
            "ChartTypeDialogs.PickerUi.cs",
            "ChartTypeDialogs.Change.cs");

    private static MouseButtonEventArgs CreateMouseDoubleClickEvent() =>
        DialogSourceTestSupport.CreateMouseDoubleClickEvent();
}
