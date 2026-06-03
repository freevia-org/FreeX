using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace FreeX.App.Host.Tests;

public sealed partial class SortDialogTests
{
    private static string ReadSortDialogSource() =>
        string.Join(Environment.NewLine, new[]
        {
            "SortDialog.cs",
            "SortDialog.Types.cs",
            "SortOptionsDialog.cs"
        }.Select(file => File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", file))));

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
        var button = FindVisualChildren<Button>(root).First(candidate => candidate.IsDefault);
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T typedChild)
                yield return typedChild;
            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }
}
