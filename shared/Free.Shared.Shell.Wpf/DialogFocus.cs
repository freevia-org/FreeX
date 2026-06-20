using System.Windows.Controls;
using System.Windows.Input;

namespace Free.Shared.Shell;

public static class DialogFocus
{
    public static void FocusAndSelect(TextBox target)
    {
        target.Focus();
        target.SelectAll();
        Keyboard.Focus(target);
    }
}
