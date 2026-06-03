using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ChartDialogTests
{
    private static IEnumerable<T> FindLogicalDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T typedChild)
                yield return typedChild;

            foreach (var descendant in FindLogicalDescendants<T>(child))
                yield return descendant;
        }
    }

    private static string ReadChartDialogSource() =>
        string.Join(Environment.NewLine, new[]
        {
            "ChartDialogs.cs",
            "SelectDataSourceDialog.cs",
            "SelectDataSourceDialog.Planning.cs",
            "SelectDataSourceDialog.Controls.cs",
            "SelectDataSourceDialog.Actions.cs"
        }.Select(file => File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", file))));

    private static string ReadChartTypeDialogSource() =>
        string.Join(Environment.NewLine, new[]
        {
            "ChartTypeDialogs.cs",
            "ChartTypeDialogs.Planner.cs",
            "ChartTypeDialogs.PickerUi.cs",
            "ChartTypeDialogs.Change.cs"
        }.Select(file => File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", file))));

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
