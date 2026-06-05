using System.Reflection;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FluentAssertions;
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
            "ChartTypeDialogs.Planner.cs",
            "ChartTypeDialogs.PickerUi.cs",
            "ChartTypeDialogs.Change.cs");

    private static T GetPrivateField<T>(object instance, string name)
        where T : class
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(instance).Should().BeOfType<T>().Subject;
    }

    private static MouseButtonEventArgs CreateMouseDoubleClickEvent() =>
        new(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = Control.MouseDoubleClickEvent
        };
}
