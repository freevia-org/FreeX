using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;

namespace Free.Shared.Ribbon.Avalonia;

internal static class AvaloniaMenuFocusRestorer
{
    public static void RestoreToParentAfterSubmenuClose(
        MenuItem parent,
        IInputElement closingSubmenuFocusOwner,
        Func<bool> isRootPopupOpen)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(closingSubmenuFocusOwner);
        ArgumentNullException.ThrowIfNull(isRootPopupOpen);

        parent.Focusable = true;
        parent.IsSelected = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (!isRootPopupOpen() || parent.IsSubMenuOpen)
                    return;

                var focusManager = TopLevel.GetTopLevel(parent)?.FocusManager;
                var focused = focusManager?.GetFocusedElement();
                if (focusManager is null ||
                    focused is not null &&
                    !ReferenceEquals(focused, closingSubmenuFocusOwner) &&
                    !ReferenceEquals(focused, parent))
                {
                    return;
                }

                focusManager.Focus(parent, NavigationMethod.Tab);
            },
            DispatcherPriority.ContextIdle);
    }
}
