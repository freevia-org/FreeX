using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FreeX.App.Host;

internal static class StatusDialogKeyboardFocus
{
    public static void FocusDefaultButton(DependencyObject parent)
    {
        if (FindDefaultButton(parent) is not { } button)
            return;

        button.Focus();
        Keyboard.Focus(button);
    }

    private static Button? FindDefaultButton(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent))
        {
            if (child is Button { IsDefault: true, IsEnabled: true } button)
                return button;

            if (child is DependencyObject dependencyObject &&
                FindDefaultButton(dependencyObject) is { } nestedButton)
            {
                return nestedButton;
            }
        }

        return null;
    }
}
