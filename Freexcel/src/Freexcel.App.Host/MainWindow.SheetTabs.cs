using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class MainWindow
{
    private void RefreshSheetTabs()
    {
        var plan = SheetTabListPlanner.Build(_workbook, _currentSheetId, _groupedSheetIds);
        _currentSheetId = plan.CurrentSheetId;
        _sheetTabs.Clear();
        foreach (var tab in plan.Tabs)
            _sheetTabs.Add(tab);
        UpdateSheetTabNavigation();
        Dispatcher.BeginInvoke(() =>
        {
            BringCurrentSheetTabIntoView();
            UpdateSheetTabNavigation();
        }, DispatcherPriority.Loaded);
        RefreshSheetProtectionUi();
        RefreshWorkbookProtectionUi();
        UpdateTitleBar();
    }

    private string GenerateUniqueSheetName()
        => SheetTabListPlanner.GenerateUniqueSheetName(_workbook);

    private void SheetTab_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as System.Windows.FrameworkElement)?.DataContext is not SheetTabViewModel tab) return;
        _dragSheetTabId = tab.Id;
        _dragSheetTabStart = e.GetPosition(SheetTabsControl);
        _currentSheetId = tab.Id;
        UpdateGroupedSheetsForClick(tab.Id);
        UpdateViewport();
        RefreshSheetTabs();
    }

    private void SheetTab_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragSheetTabId is not { } draggedId || e.LeftButton != MouseButtonState.Pressed)
            return;

        var current = e.GetPosition(SheetTabsControl);
        if (Math.Abs(current.X - _dragSheetTabStart.X) < SystemParameters.MinimumHorizontalDragDistance)
            return;

        var target = FindSheetTabViewModel(e.OriginalSource as System.Windows.DependencyObject);
        if (target is null || target.Id == draggedId)
            return;

        var sheets = _workbook.Sheets.ToList();
        var fromIndex = sheets.FindIndex(s => s.Id == draggedId);
        var toIndex = sheets.FindIndex(s => s.Id == target.Id);
        if (fromIndex < 0 || toIndex < 0 || fromIndex == toIndex)
            return;

        if (!TryExecuteCommand(new MoveSheetCommand(fromIndex, toIndex), "Move Sheet"))
            return;

        _currentSheetId = draggedId;
        _dragSheetTabStart = current;
        RefreshSheetTabs();
    }

    private void SheetTab_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _dragSheetTabId = null;
    }

    private void SheetTab_MouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as System.Windows.FrameworkElement)?.DataContext is not SheetTabViewModel tab) return;
        _currentSheetId = tab.Id;
        if (_groupedSheetIds.Count == 0)
            _groupedSheetIds.Add(tab.Id);
        _sheetGroupAnchor ??= tab.Id;
        UpdateViewport();
        RefreshSheetTabs();
    }

    private void SheetTab_LabelMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        var tab = (sender as System.Windows.FrameworkElement)?.DataContext as SheetTabViewModel;
        if (tab is null) return;
        RenameSheetFromTab(tab);
    }

    private void AddSheetButton_Click(object sender, RoutedEventArgs e)
    {
        InsertNewSheet();
    }

    private void InsertNewSheet()
    {
        var outcome = _commandBus.ExecuteRepeatable(
            _workbook.Id,
            () => new AddSheetCommand(GenerateUniqueSheetName()));
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Insert Sheet");
            return;
        }

        _currentSheetId = _workbook.Sheets[^1].Id;
        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(_currentSheetId);
        _sheetGroupAnchor = _currentSheetId;
        UpdateViewport();
        RefreshSheetTabs();
    }

    private void UpdateGroupedSheetsForClick(SheetId clickedSheetId)
    {
        var visibleSheetIds = _workbook.Sheets.Where(s => !s.IsHidden).Select(s => s.Id).ToList();
        var modifiers = Keyboard.Modifiers;
        IReadOnlyList<SheetId> selected;
        if ((modifiers & ModifierKeys.Shift) != 0 && _sheetGroupAnchor.HasValue)
        {
            selected = SheetGroupSelectionService.SelectRange(visibleSheetIds, _sheetGroupAnchor.Value, clickedSheetId);
        }
        else if ((modifiers & ModifierKeys.Control) != 0)
        {
            selected = SheetGroupSelectionService.Toggle(clickedSheetId, _groupedSheetIds);
            _sheetGroupAnchor = clickedSheetId;
        }
        else
        {
            selected = SheetGroupSelectionService.SelectSingle(clickedSheetId);
            _sheetGroupAnchor = clickedSheetId;
        }

        _groupedSheetIds.Clear();
        foreach (var id in selected)
            _groupedSheetIds.Add(id);
        if (_groupedSheetIds.Count == 0)
            _groupedSheetIds.Add(clickedSheetId);
    }

    private void SheetNavLeftBtn_Click(object sender, RoutedEventArgs e)
    {
        SheetTabsScroller.ScrollToHorizontalOffset(
            Math.Max(0, SheetTabsScroller.HorizontalOffset - SheetTabNavScrollAmount));
    }

    private void SheetNavRightBtn_Click(object sender, RoutedEventArgs e)
    {
        SheetTabsScroller.ScrollToHorizontalOffset(
            Math.Min(SheetTabsScroller.ScrollableWidth, SheetTabsScroller.HorizontalOffset + SheetTabNavScrollAmount));
    }

    // ── Sheet tab context menu ────────────────────────────────────────────────

    private void SheetTabsScroller_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateSheetTabNavigation();
    }

    private void SheetTabsScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        UpdateSheetTabNavigation();
    }

    private void SheetTabsScroller_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateSheetTabNavigation();
    }

    private void UpdateSheetTabNavigation()
    {
        var canScroll = SheetTabsScroller.ScrollableWidth > SheetTabScrollEpsilon;
        SheetNavLeftBtn.Visibility = canScroll && SheetTabsScroller.HorizontalOffset > SheetTabScrollEpsilon
            ? Visibility.Visible
            : Visibility.Hidden;
        SheetNavRightBtn.Visibility = canScroll &&
                                      SheetTabsScroller.HorizontalOffset < SheetTabsScroller.ScrollableWidth - SheetTabScrollEpsilon
            ? Visibility.Visible
            : Visibility.Hidden;
    }

    private void BringCurrentSheetTabIntoView()
    {
        var activeTab = _sheetTabs.FirstOrDefault(tab => tab.Id == _currentSheetId);
        if (activeTab is null ||
            SheetTabsControl.ItemContainerGenerator.ContainerFromItem(activeTab) is not FrameworkElement container)
            return;

        var bounds = container.TransformToAncestor(SheetTabsScroller)
            .TransformBounds(new Rect(new Point(0, 0), container.RenderSize));
        if (bounds.Left < 0)
        {
            SheetTabsScroller.ScrollToHorizontalOffset(
                Math.Max(0, SheetTabsScroller.HorizontalOffset + bounds.Left));
        }
        else if (bounds.Right > SheetTabsScroller.ViewportWidth)
        {
            SheetTabsScroller.ScrollToHorizontalOffset(
                Math.Min(
                    SheetTabsScroller.ScrollableWidth,
                    SheetTabsScroller.HorizontalOffset + bounds.Right - SheetTabsScroller.ViewportWidth));
        }
    }

    private bool TryFocusCurrentSheetTab()
    {
        BringCurrentSheetTabIntoView();
        var activeTab = _sheetTabs.FirstOrDefault(tab => tab.Id == _currentSheetId);
        if (activeTab is null)
            return false;

        return FindSheetTabContextMenuTarget(activeTab)?.Focus() == true;
    }

    private bool TryOpenFocusedSheetTabContextMenu()
    {
        if (Keyboard.FocusedElement is not DependencyObject focusedElement ||
            (!ReferenceEquals(focusedElement, SheetTabsScroller) && !IsDescendantOf(focusedElement, SheetTabsScroller)))
        {
            return false;
        }

        var target = FindSheetTabContextMenuTarget(focusedElement);
        if (target?.ContextMenu is not { } contextMenu)
            return false;

        if (target.DataContext is SheetTabViewModel tab)
        {
            SelectSheetTabForKeyboardContextMenu(tab);
            target = FindSheetTabContextMenuTarget(tab) ?? target;
            contextMenu = target.ContextMenu;
            if (contextMenu is null)
                return false;
        }

        MenuKeyTipAssigner.AssignUniqueKeyTips(contextMenu.Items.OfType<MenuItem>());
        contextMenu.PlacementTarget = target;
        contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        contextMenu.IsOpen = true;
        return true;
    }

    private FrameworkElement? FindSheetTabContextMenuTarget(SheetTabViewModel tab)
    {
        if (SheetTabsControl.ItemContainerGenerator.ContainerFromItem(tab) is not DependencyObject container)
            return null;

        return FindVisualDescendant<FrameworkElement>(
            container,
            element => ReferenceEquals(element.DataContext, tab) && element.ContextMenu is not null);
    }

    private static FrameworkElement? FindSheetTabContextMenuTarget(DependencyObject source)
    {
        for (DependencyObject? current = source; current is not null; current = GetTreeParentForKeyboardFocus(current))
        {
            if (current is FrameworkElement { DataContext: SheetTabViewModel, ContextMenu: not null } element)
                return element;
        }

        return null;
    }

    private void SelectSheetTabForKeyboardContextMenu(SheetTabViewModel tab)
    {
        _currentSheetId = tab.Id;
        if (_groupedSheetIds.Count == 0)
            _groupedSheetIds.Add(tab.Id);
        _sheetGroupAnchor ??= tab.Id;
        UpdateViewport();
        RefreshSheetTabs();
    }

    private static T? FindVisualDescendant<T>(DependencyObject source, Func<T, bool> predicate)
        where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(source);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(source, i);
            if (child is T match && predicate(match))
                return match;

            var descendant = FindVisualDescendant(child, predicate);
            if (descendant is not null)
                return descendant;
        }

        return null;
    }

    private void SheetCtxRename_Click(object sender, RoutedEventArgs e)
    {
        var tab = GetContextMenuTab(sender);
        if (tab == null) return;
        RenameSheetFromTab(tab);
    }

    private void RenameSheetFromTab(SheetTabViewModel tab)
    {
        var dialog = new SheetNameDialog(tab.Name) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        var name = dialog.Result.SheetName;
        if (!string.IsNullOrWhiteSpace(name) && name != tab.Name)
        {
            var outcome = _commandBus.Execute(_workbook.Id, new RenameSheetCommand(tab.Id, name));
            if (!outcome.Success)
            {
                ShowCommandError(outcome, "Rename Sheet");
                return;
            }

            RecalculateWorkbook();
            RefreshSheetTabs();
        }
    }

    private void SheetCtxInsert_Click(object sender, RoutedEventArgs e)
    {
        InsertNewSheet();
    }

    private void SheetCtxDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_workbook.Sheets.Count(s => !s.IsHidden) <= 1)
        {
            MessageBox.Show("Cannot delete the only visible sheet.", "Delete Sheet",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var tab = GetContextMenuTab(sender);
        if (tab == null) return;
        if (MessageBox.Show($"Delete sheet \"{tab.Name}\"?", "Delete Sheet",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var outcome = _commandBus.Execute(_workbook.Id, new RemoveSheetCommand(tab.Id));
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Delete Sheet");
            return;
        }

        _currentSheetId = _workbook.Sheets[0].Id;
        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(_currentSheetId);
        _sheetGroupAnchor = _currentSheetId;
        RecalculateWorkbook();
        UpdateViewport();
        RefreshSheetTabs();
    }

    private void ActivateAdjacentVisibleSheet(int direction)
    {
        var nextSheetId = SheetTabListPlanner.AdjacentVisibleSheet(_workbook, _currentSheetId, direction);
        if (nextSheetId is null)
            return;

        _currentSheetId = nextSheetId.Value;
        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(_currentSheetId);
        _sheetGroupAnchor = _currentSheetId;
        UpdateViewport();
        RefreshSheetTabs();
    }

    private void SelectAdjacentVisibleSheetGroup(int direction)
    {
        var plan = SheetTabListPlanner.SelectAdjacentVisibleSheetGroup(
            _workbook,
            _currentSheetId,
            _sheetGroupAnchor,
            direction);
        if (plan is null)
            return;

        _currentSheetId = plan.CurrentSheetId;
        _sheetGroupAnchor = plan.AnchorSheetId;
        _groupedSheetIds.Clear();
        foreach (var id in plan.GroupedSheetIds)
            _groupedSheetIds.Add(id);
        if (_groupedSheetIds.Count == 0)
            _groupedSheetIds.Add(_currentSheetId);

        UpdateViewport();
        RefreshSheetTabs();
    }

    private void SheetCtxDuplicate_Click(object sender, RoutedEventArgs e)
    {
        var tab = GetContextMenuTab(sender);
        if (tab == null) return;
        if (!TryExecuteCommand(new DuplicateSheetCommand(tab.Id), "Duplicate Sheet"))
            return;

        var sourceIndex = _workbook.Sheets.ToList().FindIndex(s => s.Id == tab.Id);
        _currentSheetId = _workbook.Sheets[Math.Min(sourceIndex + 1, _workbook.Sheets.Count - 1)].Id;
        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(_currentSheetId);
        _sheetGroupAnchor = _currentSheetId;
        RecalculateWorkbook();
        UpdateViewport();
        RefreshSheetTabs();
    }

    private void SheetCtxHide_Click(object sender, RoutedEventArgs e)
    {
        var tab = GetContextMenuTab(sender);
        if (tab == null) return;
        if (!TryExecuteCommand(new SetSheetHiddenCommand(tab.Id, hidden: true), "Hide Sheet"))
            return;

        if (_currentSheetId == tab.Id)
            _currentSheetId = _workbook.Sheets.First(s => !s.IsHidden).Id;
        _groupedSheetIds.Remove(tab.Id);
        if (_groupedSheetIds.Count == 0)
            _groupedSheetIds.Add(_currentSheetId);
        _sheetGroupAnchor = _currentSheetId;
        UpdateViewport();
        RefreshSheetTabs();
    }

    private void SheetCtxUnhide_Click(object sender, RoutedEventArgs e)
    {
        var hiddenSheets = _workbook.Sheets.Where(s => s.IsHidden).ToList();
        if (hiddenSheets.Count == 0)
        {
            MessageBox.Show("No hidden sheets.", "Unhide Sheet", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new UnhideSheetDialog(hiddenSheets.Select(sheet => sheet.Name)) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        var name = dialog.Result.SheetName;
        if (string.IsNullOrWhiteSpace(name)) return;

        var sheet = hiddenSheets.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (sheet is null)
        {
            MessageBox.Show("Hidden sheet not found.", "Unhide Sheet", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryExecuteCommand(new SetSheetHiddenCommand(sheet.Id, hidden: false), "Unhide Sheet"))
            return;

        _currentSheetId = sheet.Id;
        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(_currentSheetId);
        _sheetGroupAnchor = _currentSheetId;
        UpdateViewport();
        RefreshSheetTabs();
    }

    private void SheetCtxTabColor_Click(object sender, RoutedEventArgs e)
    {
        var tab = GetContextMenuTab(sender);
        if (tab == null) return;
        var sheet = _workbook.GetSheet(tab.Id);
        if (!TryShowColorPicker("Tab Color", sheet?.TabColor ?? new CellColor(33, 115, 70), allowNoColor: true, out var tabColor))
            return;

        if (!TryExecuteCommand(new SetSheetTabColorCommand(tab.Id, tabColor), "Tab Color"))
            return;
        RefreshSheetTabs();
    }

    private void SheetCtxSelectAllSheets_Click(object sender, RoutedEventArgs e)
    {
        var visibleSheetIds = _workbook.Sheets.Where(s => !s.IsHidden).Select(s => s.Id).ToList();
        _groupedSheetIds.Clear();
        foreach (var id in SheetGroupSelectionService.SelectAll(visibleSheetIds))
            _groupedSheetIds.Add(id);
        _sheetGroupAnchor = _currentSheetId;
        RefreshSheetTabs();
    }

    private void SheetCtxUngroupSheets_Click(object sender, RoutedEventArgs e)
    {
        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(_currentSheetId);
        _sheetGroupAnchor = _currentSheetId;
        RefreshSheetTabs();
    }

    private void SheetCtxMoveLeft_Click(object sender, RoutedEventArgs e)
    {
        MoveSheetTab(sender, -1);
    }

    private void SheetCtxMoveRight_Click(object sender, RoutedEventArgs e)
    {
        MoveSheetTab(sender, 1);
    }

    private void MoveSheetTab(object sender, int direction)
    {
        var tab = GetContextMenuTab(sender);
        if (tab == null) return;

        var fromIndex = _workbook.Sheets.ToList().FindIndex(s => s.Id == tab.Id);
        var toIndex = fromIndex + direction;
        var outcome = _commandBus.Execute(_workbook.Id, new MoveSheetCommand(fromIndex, toIndex));
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Move Sheet");
            return;
        }

        _currentSheetId = tab.Id;
        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(_currentSheetId);
        _sheetGroupAnchor = _currentSheetId;
        RefreshSheetTabs();
    }

    private static SheetTabViewModel? GetContextMenuTab(object sender)
    {
        if (sender is System.Windows.Controls.MenuItem mi &&
            FindParentContextMenu(mi) is { PlacementTarget: System.Windows.FrameworkElement fe })
        {
            return fe.DataContext as SheetTabViewModel
                ?? (fe.Parent as System.Windows.FrameworkElement)?.DataContext as SheetTabViewModel;
        }
        return null;
    }

    private static SheetTabViewModel? FindSheetTabViewModel(System.Windows.DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is System.Windows.FrameworkElement { DataContext: SheetTabViewModel tab })
                return tab;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static System.Windows.Controls.ContextMenu? FindParentContextMenu(System.Windows.DependencyObject item)
    {
        var current = item;
        while (current is not null)
        {
            if (current is System.Windows.Controls.ContextMenu contextMenu)
                return contextMenu;
            current = System.Windows.LogicalTreeHelper.GetParent(current);
        }

        return null;
    }
}
