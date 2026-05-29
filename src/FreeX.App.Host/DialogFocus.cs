using System.Windows.Controls;
using System.Windows.Input;

namespace FreeX.App.Host;

internal static class DialogFocus
{
    public static void FocusAndSelect(TextBox target)
    {
        target.Focus();
        target.SelectAll();
        Keyboard.Focus(target);
    }
}
