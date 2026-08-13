using Avalonia.Controls;

namespace FreeP.App.Avalonia;

internal sealed partial class SelectionPane
{
    internal IReadOnlyList<Control> AccessibilityItemsForTests =>
        _items.Children.OfType<Control>().ToArray();
}
