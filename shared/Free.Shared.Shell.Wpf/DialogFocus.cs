using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Free.Shared.Shell;

public static class DialogFocus
{
    public static void Focus(Control target)
    {
        target.Focus();
        Keyboard.Focus(target);
    }

    public static void FocusAndSelect(TextBox target)
    {
        target.Focus();
        target.SelectAll();
        Keyboard.Focus(target);
    }

    public static void ShowWarningAndFocus(Window? owner, string? message, string title, Control target)
    {
        DialogMessageHelper.ShowWarning(owner, message, title);
        if (target is TextBox textBox)
        {
            FocusAndSelect(textBox);
            return;
        }

        Focus(target);
    }
}
