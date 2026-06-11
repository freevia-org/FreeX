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

    private static Rect BoundsRelativeTo(FrameworkElement root, FrameworkElement element) =>
        element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));

    private static void AssertInside(FrameworkElement root, FrameworkElement element)
    {
        var bounds = BoundsRelativeTo(root, element);

        bounds.Left.Should().BeGreaterThanOrEqualTo(-0.5);
        bounds.Top.Should().BeGreaterThanOrEqualTo(-0.5);
        bounds.Right.Should().BeLessThanOrEqualTo(root.ActualWidth + 0.5);
        bounds.Bottom.Should().BeLessThanOrEqualTo(root.ActualHeight + 0.5);
    }
}
