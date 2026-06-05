using System.Windows;

namespace FreeX.App.Host.Tests;

internal static class WpfTestTree
{
    internal static IEnumerable<T> FindLogicalDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match)
                yield return match;

            foreach (var descendant in FindLogicalDescendants<T>(child))
                yield return descendant;
        }
    }
}
