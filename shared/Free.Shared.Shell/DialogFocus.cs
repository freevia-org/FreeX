using System.Windows.Controls;
using System.Windows.Input;

namespace Free.Shared.Shell;

internal static class DialogFocus
{
    public static void FocusAndSelect(TextBox target)
    {
        target.Focus();
        target.SelectAll();
        Keyboard.Focus(target);
    }
}
