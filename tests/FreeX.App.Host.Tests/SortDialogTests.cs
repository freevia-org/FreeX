using System.Linq;
using FluentAssertions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace FreeX.App.Host.Tests;

public sealed partial class SortDialogTests
{
    private static string ReadSortDialogSource() =>
        DialogSourceTestSupport.ReadHostSources(
            "SortDialog.cs",
            "SortDialog.Types.cs",
            "SortOptionsDialog.cs");

    private static T GetControl<T>(SortDialog dialog, string name)
        where T : class
    {
        var field = typeof(SortDialog).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(dialog).Should().BeOfType<T>().Subject;
    }

    private static T GetControl<T>(SortOptionsDialog dialog, string name)
        where T : class
    {
        var field = typeof(SortOptionsDialog).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(dialog).Should().BeOfType<T>().Subject;
    }

    private static void ClickDefaultButton(DependencyObject root)
    {
        var button = WpfTestTree.FindVisualDescendants<Button>(root).First(candidate => candidate.IsDefault);
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
