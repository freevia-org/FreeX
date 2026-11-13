using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace FreeX.App.Avalonia;

/// <summary>
/// Keyboard behaviour every owned dialog gets in the shipping app, whatever its builder did or did
/// not configure (WPF gets it from the dialog window and its default Tab navigation):
/// <list type="bullet">
///   <item>Tab and Shift+Tab cycle through every field of the dialog, across the controls of the
///   selected tab page and the button row beneath it, and wrap at both ends;</item>
///   <item>Escape cancels (clicks the Cancel button, or closes a dialog that has none);</item>
///   <item>a dialog that focuses nothing itself gets its first control focused;</item>
///   <item>closing the dialog returns focus to what the owner had focused when it opened.</item>
/// </list>
/// Dialogs that handle a key themselves (a per-dialog tab cycle, a deferred cancel) keep it: this
/// only acts on keys nobody handled. The capture harness installs this same implementation.
/// </summary>
internal static class OwnedDialogKeyboard
{
    private sealed class State(Window owner, IInputElement? ownerFocusBeforeOpen)
    {
        public Window Owner { get; } = owner;
        public IInputElement? OwnerFocusBeforeOpen { get; } = ownerFocusBeforeOpen;
        public int FallbackChecks { get; set; }
    }

    private static readonly ConditionalWeakTable<Window, State> Configured = new();
    private static bool _installed;

    public static void Install()
    {
        if (_installed)
            return;
        _installed = true;
        Window.OwnerProperty.Changed.AddClassHandler<Window>(Configure);
    }

    private static void Configure(Window dialog, AvaloniaPropertyChangedEventArgs _)
    {
        if (dialog is MainWindow || dialog.Owner is not Window owner ||
            !Configured.TryAdd(dialog, new State(owner, owner.FocusManager?.GetFocusedElement())))
        {
            return;
        }

        KeyboardNavigation.SetTabNavigation(dialog, KeyboardNavigationMode.Cycle);
        dialog.Opened += OnOpened;
        dialog.Activated += OnActivated;
        dialog.LayoutUpdated += OnLayoutUpdated;
        dialog.KeyDown += OnEscapeKeyDown;
        dialog.Closed += OnClosed;
    }

    private static void OnOpened(object? sender, EventArgs _)
    {
        if (sender is not Window dialog)
            return;

        // Attached when the dialog opens, after every handler its builder added, so a dialog that
        // handles Tab itself (and marks it handled) is left alone.
        dialog.RemoveHandler(InputElement.KeyDownEvent, OnTunnelKeyDown);
        dialog.AddHandler(InputElement.KeyDownEvent, OnTunnelKeyDown, RoutingStrategies.Tunnel);
        QueueInitialFocus(dialog);
    }

    private static void OnActivated(object? sender, EventArgs _)
    {
        if (sender is Window dialog)
            QueueInitialFocus(dialog);
    }

    private static void OnLayoutUpdated(object? sender, EventArgs _)
    {
        if (sender is Window dialog)
            FocusFirstControl(dialog);
    }

    private static void QueueInitialFocus(Window dialog) =>
        Dispatcher.UIThread.Post(() => FocusFirstControl(dialog), DispatcherPriority.Background);

    internal static bool IsFocusInside(Window dialog, IInputElement? element) =>
        element is Visual visual && ReferenceEquals(TopLevel.GetTopLevel(visual), dialog);

    private static void FocusFirstControl(Window dialog)
    {
        if (!dialog.IsVisible)
            return;

        if (IsFocusInside(dialog, dialog.FocusManager?.GetFocusedElement()))
        {
            dialog.LayoutUpdated -= OnLayoutUpdated;
            return;
        }

        // Last resort, and only after the dialog has had a pass to focus its own target: a dialog
        // that picks a specific initial control can fail its first attempt (the control is not
        // always ready that early) and retries on the next layout. Dialogs that never focus
        // anything still get the fallback, one pass later.
        if (Configured.TryGetValue(dialog, out var state) && ++state.FallbackChecks < 2)
            return;

        var first = GetTabStops(dialog).FirstOrDefault();
        first?.Focus();
        if (IsFocusInside(dialog, dialog.FocusManager?.GetFocusedElement()))
            dialog.LayoutUpdated -= OnLayoutUpdated;
    }

    // Controls whose template parts take focus for them: the control itself is the one tab stop.
    private static bool IsCompositeStop(Visual visual) =>
        visual is ListBox or ComboBox or NumericUpDown or TreeView or AutoCompleteBox or SplitButton
            or ToggleSplitButton or CalendarDatePicker;

    // A list takes focus through its item containers, so the list itself is the stop.
    private static bool HasFocusableItem(Control control) => control switch
    {
        ListBox => control.GetVisualDescendants().OfType<ListBoxItem>()
            .Any(item => item.Focusable && item.IsVisible && item.IsEffectivelyEnabled),
        TreeView => control.GetVisualDescendants().OfType<TreeViewItem>()
            .Any(item => item.Focusable && item.IsVisible && item.IsEffectivelyEnabled),
        _ when IsCompositeStop(control) => control.GetVisualDescendants().OfType<Control>()
            .Any(part => part.Focusable && part.IsVisible && part.IsEffectivelyEnabled),
        _ => false,
    };

    /// <summary>The dialog's tab stops in visual order: tab headers and list/combo internals are not stops.</summary>
    internal static IReadOnlyList<Control> GetTabStops(Window dialog)
    {
        var stops = new List<Control>();
        Visit(dialog);
        return stops;

        void Visit(Visual parent)
        {
            foreach (var child in parent.GetVisualChildren())
            {
                if (child is Control control && control.IsVisible && control.IsEffectivelyEnabled)
                {
                    // An editable combo box is not a tab stop itself (its text box is), but it
                    // still stands for that one stop.
                    if (control is not TabItem and not TabControl &&
                        ((control.Focusable && KeyboardNavigation.GetIsTabStop(control)) ||
                         HasFocusableItem(control)))
                    {
                        stops.Add(control);
                    }

                    if (IsCompositeStop(control))
                        continue;
                }

                if (child is not Control { IsVisible: false })
                    Visit(child);
            }
        }
    }

    private static int IndexOfStop(IReadOnlyList<Control> stops, IInputElement? focused)
    {
        for (var node = focused as Visual; node is not null; node = node.GetVisualParent())
        {
            for (var index = 0; index < stops.Count; index++)
            {
                if (ReferenceEquals(stops[index], node))
                    return index;
            }
        }

        return -1;
    }

    private static bool FocusStop(Control target)
    {
        if (target.Focus(NavigationMethod.Tab))
            return true;
        if (target is ListBox listBox)
        {
            var item = listBox.GetVisualDescendants().OfType<ListBoxItem>()
                .FirstOrDefault(candidate => Equals(candidate.Content, listBox.SelectedItem))
                ?? listBox.GetVisualDescendants().OfType<ListBoxItem>().FirstOrDefault(candidate => candidate.Focusable);
            if (item?.Focus(NavigationMethod.Tab) == true)
                return true;
        }
        else if (target is TreeView tree)
        {
            var item = tree.GetVisualDescendants().OfType<TreeViewItem>()
                .FirstOrDefault(candidate => candidate.Focusable && candidate.IsSelected)
                ?? tree.GetVisualDescendants().OfType<TreeViewItem>().FirstOrDefault(candidate => candidate.Focusable);
            if (item?.Focus(NavigationMethod.Tab) == true)
                return true;
        }

        return target.GetVisualDescendants().OfType<Control>()
            .FirstOrDefault(part => part.Focusable && part.IsVisible && part.IsEffectivelyEnabled)
            ?.Focus(NavigationMethod.Tab) == true;
    }

    // Tunnelled so it sees Tab before the focus manager does; it acts only at the ends of the
    // cycle, where Avalonia's own wrap lands on the wrong control (or nowhere), and on Ctrl+Tab.
    private static void OnTunnelKeyDown(object? sender, KeyEventArgs args)
    {
        if (sender is not Window dialog || args.Handled || args.Key != Key.Tab)
            return;

        if (args.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Shift))
            return;

        var stops = GetTabStops(dialog);
        if (stops.Count < 2)
            return;
        var index = IndexOfStop(stops, dialog.FocusManager?.GetFocusedElement());
        if (index < 0)
            return;

        var reverse = args.KeyModifiers == KeyModifiers.Shift;
        if (index != (reverse ? 0 : stops.Count - 1))
            return;

        // Whatever handles this Tab (Avalonia's own navigation, or a dialog's own handler) runs
        // after this one, so the wrap is checked once the key has been processed: if focus did
        // not land on the opposite end of the cycle it is moved there.
        var target = stops[reverse ? stops.Count - 1 : 0];
        Dispatcher.UIThread.Post(
            () =>
            {
                if (!dialog.IsVisible)
                    return;
                var current = GetTabStops(dialog);
                var landed = IndexOfStop(current, dialog.FocusManager?.GetFocusedElement());
                if (landed >= 0 && ReferenceEquals(current[landed], target))
                    return;
                FocusStop(target);
            },
            DispatcherPriority.Input);
    }

    private static void OnEscapeKeyDown(object? sender, KeyEventArgs args)
    {
        if (sender is not Window dialog || args.Handled || args.Key != Key.Escape ||
            args.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        var cancel = dialog.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(button => button.IsCancel && button.IsVisible && button.IsEffectivelyEnabled);
        cancel?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, cancel));
        if (dialog.IsVisible)
            dialog.Close();
        args.Handled = true;
    }

    private static void OnClosed(object? sender, EventArgs _)
    {
        if (sender is not Window dialog || !Configured.TryGetValue(dialog, out var state))
            return;

        dialog.Opened -= OnOpened;
        dialog.Activated -= OnActivated;
        dialog.LayoutUpdated -= OnLayoutUpdated;
        dialog.RemoveHandler(InputElement.KeyDownEvent, OnTunnelKeyDown);
        dialog.KeyDown -= OnEscapeKeyDown;
        dialog.Closed -= OnClosed;

        Dispatcher.UIThread.Post(() => RestoreOwnerFocus(state), DispatcherPriority.Input);
    }

    private static void RestoreOwnerFocus(State state)
    {
        var owner = state.Owner;
        if (!owner.IsVisible)
            return;

        owner.Activate();
        if (state.OwnerFocusBeforeOpen is InputElement prior &&
            prior.Focusable && prior.IsEffectivelyEnabled && IsFocusInside(owner, prior))
        {
            prior.Focus();
            return;
        }

        (owner as MainWindow)?.FocusSheetGridAfterDialog();
    }
}

public sealed partial class MainWindow
{
    internal void FocusSheetGridAfterDialog() => _sheetGridHost.Focus();
}
