using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;

namespace Free.Shared.Ribbon.Avalonia;

internal static class AvaloniaMenuFocusRestorer
{
    public static void RestoreToParentAfterSubmenuClose(
        MenuItem parent,
        Func<bool> isRootPopupOpen)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(isRootPopupOpen);

        parent.Focusable = true;
        parent.IsSelected = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (!isRootPopupOpen() || parent.IsSubMenuOpen)
                    return;

                TopLevel.GetTopLevel(parent)?.FocusManager?.Focus(parent, NavigationMethod.Tab);
            },
            DispatcherPriority.ContextIdle);
    }
}
