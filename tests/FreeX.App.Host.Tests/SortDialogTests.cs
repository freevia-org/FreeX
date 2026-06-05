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
        => DialogSourceTestSupport.GetPrivateField<T>(dialog, name);

    private static T GetControl<T>(SortOptionsDialog dialog, string name)
        where T : class
        => DialogSourceTestSupport.GetPrivateField<T>(dialog, name);

    private static void ClickDefaultButton(DependencyObject root)
    {
        var button = WpfTestTree.FindVisualDescendants<Button>(root).First(candidate => candidate.IsDefault);
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
