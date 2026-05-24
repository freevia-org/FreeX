using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Freexcel.App.Host;

public partial class MainWindow
{
    private bool TryHandleShellFocusCyclePreview(System.Windows.Input.KeyEventArgs e)
    {
        if (!KeyboardShortcutMatcher.TryGetCommandShortcut(
                e.Key,
                e.SystemKey,
                Keyboard.Modifiers,
                out var commandShortcut) ||
            commandShortcut != KeyboardCommandShortcut.CycleShellFocus)
        {
            return false;
        }

        if (IsStartScreenVisible() && TryHandleBackstageShellFocusCycle(Keyboard.Modifiers == ModifierKeys.Shift))
        {
            e.Handled = true;
            return true;
        }

        ExecuteCommandShortcut(commandShortcut, this, e);
        e.Handled = true;
        return true;
    }

    private bool TryHandleFocusedRibbonKeyboardNavigation(System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is not DependencyObject focusedElement ||
            !IsInsideRibbonSurface(focusedElement) ||
            Keyboard.Modifiers is not ModifierKeys.None and not ModifierKeys.Shift)
        {
            return false;
        }

        if (e.Key == Key.Escape)
        {
            FocusSheetGridIfNeeded();
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.Tab)
        {
            MoveFocusedRibbonElement(focusedElement, Keyboard.Modifiers == ModifierKeys.Shift
                ? FocusNavigationDirection.Previous
                : FocusNavigationDirection.Next);
            e.Handled = true;
            return true;
        }

        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            var direction = e.Key switch
            {
                Key.Left => FocusNavigationDirection.Left,
                Key.Right => FocusNavigationDirection.Right,
                Key.Up => FocusNavigationDirection.Up,
                Key.Down => FocusNavigationDirection.Down,
                _ => FocusNavigationDirection.Next
            };
            MoveFocusedRibbonElement(focusedElement, direction);
            e.Handled = true;
            return true;
        }

        if (e.Key is Key.Home or Key.End)
        {
            var direction = e.Key switch
            {
                Key.Home => FocusNavigationDirection.First,
                Key.End => FocusNavigationDirection.Last,
                _ => FocusNavigationDirection.Next
            };
            MoveFocusedRibbonElement(focusedElement, direction);
            e.Handled = true;
            return true;
        }

        return false;
    }

    private static bool MoveFocusedRibbonElement(DependencyObject focusedElement, FocusNavigationDirection direction)
    {
        return focusedElement is UIElement focusedUiElement &&
               focusedUiElement.MoveFocus(new TraversalRequest(direction));
    }

    private bool TryHandleFocusedStatusBarKeyboardNavigation(System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is not UIElement focusedElement ||
            !IsDescendantOf(focusedElement, StatusBarGrid) ||
            Keyboard.Modifiers is not ModifierKeys.None and not ModifierKeys.Shift)
        {
            return false;
        }

        if (e.Key != Key.Tab)
            return false;

        var request = new TraversalRequest(Keyboard.Modifiers == ModifierKeys.Shift
            ? FocusNavigationDirection.Previous
            : FocusNavigationDirection.Next);
        focusedElement.MoveFocus(request);
        e.Handled = true;
        return true;
    }

    private bool IsInsideRibbonSurface(DependencyObject element)
    {
        for (DependencyObject? current = element; current is not null; current = GetTreeParentForKeyboardFocus(current))
        {
            if (ReferenceEquals(current, RibbonTabs))
                return true;
        }

        return false;
    }

    private static DependencyObject? GetTreeParentForKeyboardFocus(DependencyObject element)
    {
        if (element is Visual)
        {
            var visualParent = VisualTreeHelper.GetParent(element);
            if (visualParent is not null)
                return visualParent;
        }

        return LogicalTreeHelper.GetParent(element);
    }

    private void CycleShellFocus(bool reverse)
    {
        var current = GetCurrentShellFocusTarget();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            current = ShellFocusCyclePlanner.GetNext(current, reverse);
            if (FocusShellRegion(current))
                return;
        }
    }

    private ShellFocusTarget GetCurrentShellFocusTarget()
    {
        if (Keyboard.FocusedElement is DependencyObject focusedElement)
        {
            if (IsInsideRibbonSurface(focusedElement))
                return ShellFocusTarget.Ribbon;

            if (ReferenceEquals(focusedElement, FormulaBar) ||
                ReferenceEquals(focusedElement, CellAddressBox) ||
                ReferenceEquals(focusedElement, FormulaBarExpandBtn) ||
                IsDescendantOf(focusedElement, FormulaBarBorder))
            {
                return ShellFocusTarget.FormulaBar;
            }

            if (ReferenceEquals(focusedElement, SheetNavLeftBtn) ||
                ReferenceEquals(focusedElement, SheetNavRightBtn) ||
                ReferenceEquals(focusedElement, AddSheetButton) ||
                ReferenceEquals(focusedElement, HorizontalScroll) ||
                IsDescendantOf(focusedElement, SheetTabsScroller))
            {
                return ShellFocusTarget.SheetTabs;
            }

            if (IsDescendantOf(focusedElement, StatusBarGrid))
                return ShellFocusTarget.StatusBar;
        }

        return ShellFocusTarget.Worksheet;
    }

    private bool FocusShellRegion(ShellFocusTarget target)
    {
        switch (target)
        {
            case ShellFocusTarget.Ribbon:
                if (RibbonTabs?.SelectedItem is TabItem selectedTab && selectedTab.Focus())
                    return true;
                return RibbonTabs?.Focus() == true;

            case ShellFocusTarget.FormulaBar:
                if (FormulaBarBorder?.Visibility != Visibility.Visible)
                    return false;
                return FormulaBar.Focus();

            case ShellFocusTarget.SheetTabs:
                return TryFocusCurrentSheetTab() || AddSheetButton.Focus();

            case ShellFocusTarget.StatusBar:
                return FocusStatusBar();

            default:
                FocusSheetGridIfNeeded();
                return true;
        }
    }

    private static bool IsDescendantOf(DependencyObject element, DependencyObject? ancestor)
    {
        if (ancestor is null)
            return false;

        for (DependencyObject? current = element; current is not null; current = GetTreeParentForKeyboardFocus(current))
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }

        return false;
    }

    private bool FocusStatusBar()
    {
        return StatusZoomOutButton.Focus() || ZoomSlider.Focus();
    }

    private void ExecuteCommandShortcut(KeyboardCommandShortcut shortcut, object sender, RoutedEventArgs e)
    {
        _keyboardCommandDispatcher.TryExecute(shortcut, sender, e);
    }

    private void MainWindow_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var keyTipKey = GetEffectiveKey(e);
        if (!_standaloneAltKeyTipTracker.ShouldToggleOnKeyUp(keyTipKey))
            return;

        if (Keyboard.FocusedElement is TextBox or ComboBox)
            return;

        if (_ribbonKeyTipMode.IsActive)
            ExitRibbonKeyTipMode();
        else
            EnterRibbonKeyTipMode(RibbonKeyTipScope.TopLevel);

        e.Handled = true;
    }

    private void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        _standaloneAltKeyTipTracker.CancelStandaloneAltCandidate();
        if (_ribbonKeyTipMode.IsActive)
            ExitRibbonKeyTipMode();
    }
}
