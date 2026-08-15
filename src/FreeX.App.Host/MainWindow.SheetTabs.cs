using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FreeX.App.Presentation.FormulaBar;
using FreeX.App.Presentation.SheetUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private static readonly Brush SheetTabSeparatorBrush = CreateFrozenSheetTabBrush(0xB8, 0xBE, 0xC4);

    private SheetTabViewportMeasureKey? _lastSheetTabViewportMeasureKey;
    private double _lastSheetTabViewportContentWidth;
    private bool _sheetTabViewportRefreshQueued;
    private SheetTabsChromeRenderKey? _lastSheetTabsChromeRenderKey;

    private void RefreshSheetTabs()
    {
        var plan = SheetTabListPlanner.Build(_workbook, _currentSheetId, _groupedSheetIds);
        _currentSheetId = plan.CurrentSheetId;
        _sheetTabs.Clear();
        foreach (var tab in plan.Tabs)
            _sheetTabs.Add(MapSheetTabListEntry(tab));
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

    private static SheetTabViewModel MapSheetTabListEntry(SheetTabListEntry entry) =>
        new(entry.Id, entry.Name, entry.TabColor, entry.IsProtected)
        {
            IsActive = entry.IsActive,
            IsGrouped = entry.IsGrouped,
            IsLeftSideCoveredByActive = entry.IsLeftSideCoveredByActive,
            IsRightSideCoveredByActive = entry.IsRightSideCoveredByActive
        };

    private int FindWorkbookSheetIndex(SheetId sheetId)
    {
        var sheets = _workbook.Sheets;
        for (var index = 0; index < sheets.Count; index++)
        {
            if (sheets[index].Id == sheetId)
                return index;
        }

        return -1;
    }

    private void SyncWorkbookActiveSheetIndex()
    {
        var index = FindWorkbookSheetIndex(_currentSheetId);
        if (index >= 0)
            _workbook.ActiveSheetIndex = index;
    }

    private bool TrySelectWorkbookActiveSheet()
    {
        if (_workbook.ActiveSheetIndex is not { } index ||
            index < 0 ||
            index >= _workbook.Sheets.Count)
        {
            return false;
        }

        var sheet = _workbook.Sheets[index];
        if (sheet.IsHidden)
            return false;

        SelectSingleSheetTab(sheet.Id);
        return true;
    }

    private IReadOnlyList<SheetId> GetVisibleSheetIds()
        => _workbook.Sheets.Where(sheet => !sheet.IsHidden).Select(sheet => sheet.Id).ToList();

    private static int FindSheetTabIndex(IReadOnlyList<SheetTabViewModel> tabs, SheetId sheetId)
    {
        for (var index = 0; index < tabs.Count; index++)
        {
            if (tabs[index].Id == sheetId)
                return index;
        }

        return -1;
    }

    private int FindCurrentSheetTabIndex(IReadOnlyList<SheetTabViewModel> tabs)
        => FindSheetTabIndex(tabs, _currentSheetId);

    private SheetTabViewModel? FindSheetTab(SheetId sheetId)
    {
        foreach (var tab in _sheetTabs)
            if (tab.Id == sheetId)
                return tab;

        return null;
    }

    private SheetTabViewModel? FindCurrentSheetTab()
        => FindSheetTab(_currentSheetId);

    private static MenuItem? FindFirstEnabledMenuItem(ContextMenu contextMenu)
    {
        foreach (var item in contextMenu.Items)
            if (item is MenuItem { IsEnabled: true } menuItem)
                return menuItem;

        return null;
    }

    private static Sheet? FindHiddenSheetByName(IReadOnlyList<Sheet> hiddenSheets, string sheetName)
    {
        foreach (var sheet in hiddenSheets)
            if (sheet.Name.Equals(sheetName, StringComparison.OrdinalIgnoreCase))
                return sheet;

        return null;
    }

    private void SelectSingleSheetTab(SheetId sheetId)
    {
        _currentSheetId = sheetId;
        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(sheetId);
        _sheetGroupAnchor = sheetId;
    }

    private void SheetTab_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as System.Windows.FrameworkElement)?.DataContext is not SheetTabViewModel tab) return;
        if (TryHandleFormulaSheetTabClick(tab.Id, Keyboard.Modifiers))
        {
            e.Handled = true;
            return;
        }

        var dragStart = e.GetPosition(SheetTabsControl);
        _currentSheetId = tab.Id;
        UpdateGroupedSheetsForClick(tab.Id);
        UpdateViewport();
        RefreshSheetTabs();
        _dragSheetTabId = tab.Id;
        _dragSheetTabStart = dragStart;
        _dragSheetTabPendingToIndex = null;
        CaptureSheetTabMouseForDrag(tab.Id, sender);
    }

    private void SheetTab_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragSheetTabId is not { } draggedId)
            return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            CommitPendingSheetTabDragDrop();
            ClearSheetTabDragState();
            if (sender is System.Windows.UIElement element && element.IsMouseCaptured)
                element.ReleaseMouseCapture();
            return;
        }

        var current = e.GetPosition(SheetTabsControl);
        if (Math.Abs(current.X - _dragSheetTabStart.X) < SystemParameters.MinimumHorizontalDragDistance)
            return;

        var dragTarget = FindSheetTabDragTarget(current, draggedId, e.OriginalSource as System.Windows.DependencyObject);
        if (dragTarget is null || dragTarget.Tab.Id == draggedId)
            return;

        var fromIndex = FindWorkbookSheetIndex(draggedId);
        var targetIndex = FindWorkbookSheetIndex(dragTarget.Tab.Id);
        var insertAfterTarget = current.X >= dragTarget.Bounds.Left + dragTarget.Bounds.Width / 2.0;
        var toIndex = SheetTabPointerPlanner.CalculateDropIndex(fromIndex, targetIndex, insertAfterTarget);
        if (fromIndex < 0 || toIndex < 0 || fromIndex == toIndex)
            return;

        _dragSheetTabPendingToIndex = toIndex;
    }

    private void SheetTab_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        CommitPendingSheetTabDragDrop();
        ClearSheetTabDragState();
        if (sender is System.Windows.UIElement element && element.IsMouseCaptured)
            element.ReleaseMouseCapture();
    }

    private void CommitPendingSheetTabDragDrop()
    {
        if (_dragSheetTabId is not { } draggedId || _dragSheetTabPendingToIndex is not { } toIndex)
            return;

        var fromIndex = FindWorkbookSheetIndex(draggedId);
        if (fromIndex < 0 || toIndex < 0 || fromIndex == toIndex)
            return;

        if (!TryExecuteCommand(new MoveSheetCommand(fromIndex, toIndex), "Move Sheet"))
            return;

        _currentSheetId = draggedId;
        // Moving a sheet can change which sheets fall inside a 3-D span reference
        // (e.g. =SUM(Sheet1:Sheet3!A1)), so recalculate just like the other structural
        // sheet operations (rename/delete/duplicate) do.
        RecalculateWorkbook();
        RefreshSheetTabs();
    }

    private void ClearSheetTabDragState()
    {
        _dragSheetTabId = null;
        _dragSheetTabPendingToIndex = null;
    }

    private void CaptureSheetTabMouseForDrag(SheetId sheetId, object sender)
    {
        SheetTabsControl.UpdateLayout();
        if (FindSheetTab(sheetId) is { } refreshedTab &&
            FindSheetTabContextMenuTarget(refreshedTab) is UIElement refreshedElement)
        {
            refreshedElement.CaptureMouse();
            return;
        }

        if (sender is UIElement fallbackElement)
            fallbackElement.CaptureMouse();
    }

    private SheetTabDragTarget? FindSheetTabDragTarget(Point position, SheetId draggedId, DependencyObject? fallbackHit)
    {
        var hit = SheetTabsControl.InputHitTest(position) as DependencyObject;
        return FindSheetTabDragTargetFromHit(hit, draggedId)
            ?? FindSheetTabDragTargetByBounds(position, draggedId)
            ?? FindSheetTabDragTargetFromHit(fallbackHit, draggedId);
    }

    private SheetTabDragTarget? FindSheetTabDragTargetFromHit(DependencyObject? hit, SheetId draggedId)
    {
        var hitTab = FindSheetTabViewModel(hit);
        if (hitTab is null || hitTab.Id == draggedId)
            return null;

        return CreateSheetTabDragTarget(hitTab);
    }

    private SheetTabDragTarget? CreateSheetTabDragTarget(SheetTabViewModel tab)
    {
        var element = FindSheetTabContextMenuTarget(tab);
        if (element is null || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            return null;

        var bounds = element.TransformToAncestor(SheetTabsControl)
            .TransformBounds(new Rect(new Point(0, 0), element.RenderSize));
        return new SheetTabDragTarget(tab, bounds);
    }

    private SheetTabDragTarget? FindSheetTabDragTargetByBounds(Point position, SheetId draggedId)
    {
        return _sheetTabs
            .Where(tab => tab.Id != draggedId)
            .Select(tab => new { Tab = tab, Element = FindSheetTabContextMenuTarget(tab) })
            .Where(candidate => candidate.Element is not null &&
                                candidate.Element.ActualWidth > 0 &&
                                candidate.Element.ActualHeight > 0)
            .Select(candidate => new
            {
                candidate.Tab,
                Element = candidate.Element!,
                Bounds = candidate.Element!.TransformToAncestor(SheetTabsControl)
                    .TransformBounds(new Rect(new Point(0, 0), candidate.Element.RenderSize))
            })
            .Where(candidate => candidate.Bounds.Contains(position))
            .OrderByDescending(candidate => Panel.GetZIndex(candidate.Element))
            .ThenBy(candidate => Math.Abs(position.X - (candidate.Bounds.Left + candidate.Bounds.Width / 2.0)))
            .FirstOrDefault()
            is { } match
                ? new SheetTabDragTarget(match.Tab, match.Bounds)
                : null;
    }

    private void SheetTab_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (Mouse.LeftButton != MouseButtonState.Pressed)
            CommitPendingSheetTabDragDrop();
        ClearSheetTabDragState();
    }

    private void SheetTab_MouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as System.Windows.FrameworkElement)?.DataContext is not SheetTabViewModel tab) return;
        if (_groupedSheetIds.Count > 1 && _groupedSheetIds.Contains(tab.Id))
        {
            _currentSheetId = tab.Id;
            _sheetGroupAnchor = tab.Id;
        }
        else
        {
            SelectSingleSheetTab(tab.Id);
        }

        UpdateViewport();
        RefreshSheetTabs();
        UpdateTitleBar();
    }

    private void SheetTab_LabelMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ClickCount != 2) return;
        var tab = (sender as System.Windows.FrameworkElement)?.DataContext as SheetTabViewModel;
        if (tab is null) return;
        RenameSheetFromTab(tab);
        e.Handled = true;
    }

    private void RenameSheetFromTab(SheetTabViewModel tab)
    {
        SelectSingleSheetTab(tab.Id);
        UpdateViewport();
        RefreshSheetTabs();
        RenameSheet(tab.Id, tab.Name);
    }

    private void AddSheetButton_Click(object sender, RoutedEventArgs e)
    {
        // The tab-strip '+' button always adds after the last sheet, matching real Excel's own
        // New Sheet button -- pass no target so InsertNewSheet appends (see its doc comment).
        InsertNewSheet();
    }

    /// <summary>
    /// Inserts a new sheet, optionally BEFORE <paramref name="insertBeforeSheetId"/> (the tab the
    /// user right-clicked to invoke Insert) instead of always appending. R84-calc-crosssheet-3d-5-3:
    /// Excel inserts a sheet acted on via the tab context menu immediately before that tab, so it
    /// lands inside any 3-D span reference the acted-on sheet already sits within (e.g.
    /// =SUM(Sheet1:Sheet3!A1)) -- appending unconditionally (the pre-fix behavior) always placed
    /// the new sheet outside such a span.
    /// </summary>
    private void InsertNewSheet(SheetId? insertBeforeSheetId = null)
    {
        SynchronizeWorkbookSessionSelection();
        if (!CompleteWorksheetSessionCommand(
                _session.AddSheet(insertBeforeSheetId),
                "Insert Sheet"))
        {
            return;
        }

        UpdateViewport();
        RefreshSheetTabs();
    }

    private void UpdateGroupedSheetsForClick(SheetId clickedSheetId)
        => UpdateGroupedSheetsForClick(clickedSheetId, Keyboard.Modifiers);

    private void UpdateGroupedSheetsForClick(SheetId clickedSheetId, ModifierKeys modifiers)
    {
        var visibleSheetIds = GetVisibleSheetIds();
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

    private bool TryHandleFormulaSheetTabClick(SheetId sheetId, ModifierKeys modifiers)
    {
        // Existing formulas are opened in Edit mode, not Point mode. Keep that formula edit
        // alive while the user switches to a qualified reference's sheet so its grid overlay and
        // resize grip can move with the active worksheet (matching the WPF formula editor's
        // cross-sheet reference workflow).
        var formulaEditor = GetFormulaReferenceHighlightEditor();
        if (formulaEditor is null ||
            _workbook.GetSheet(sheetId) is not { } clickedSheet ||
            _workbook.GetSheet(_currentSheetId) is not { } activeSheet)
        {
            return false;
        }

        var formulaText = formulaEditor.Text;
        var selectionStart = formulaEditor.SelectionStart;
        var selectionLength = formulaEditor.SelectionLength;

        _formulaRangeEditingSession.ApplySheetTabSelection(
            activeSheet.Name,
            clickedSheet.Name,
            (modifiers & ModifierKeys.Shift) != 0);
        _currentSheetId = sheetId;
        UpdateGroupedSheetsForClick(sheetId, modifiers);
        UpdateViewport();
        RefreshSheetTabs();
        RestoreFormulaRangeEntryEditor(formulaEditor, formulaText, selectionStart, selectionLength);
        RefreshFormulaReferenceHighlights();
        UpdateTitleBar();
        return true;
    }

    private void RestoreFormulaRangeEntryEditor(
        System.Windows.Controls.TextBox editor,
        string formulaText,
        int selectionStart,
        int selectionLength)
    {
        if (editor.Text != formulaText)
            editor.Text = formulaText;

        var safeSelectionStart = Math.Clamp(selectionStart, 0, editor.Text.Length);
        var safeSelectionLength = Math.Clamp(selectionLength, 0, editor.Text.Length - safeSelectionStart);
        editor.Select(safeSelectionStart, safeSelectionLength);
        editor.Focus();
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

    private void SheetNavButton_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not UIElement element)
            return;

        var downHandler = new MouseButtonEventHandler(SheetNavButton_MouseRightButtonDown);
        var upHandler = new MouseButtonEventHandler(SheetNavButton_MouseRightButtonUp);
        element.RemoveHandler(UIElement.PreviewMouseDownEvent, downHandler);
        element.RemoveHandler(UIElement.PreviewMouseRightButtonDownEvent, downHandler);
        element.RemoveHandler(UIElement.MouseRightButtonDownEvent, downHandler);
        element.RemoveHandler(UIElement.PreviewMouseRightButtonUpEvent, upHandler);
        element.RemoveHandler(UIElement.MouseRightButtonUpEvent, upHandler);
        element.AddHandler(UIElement.PreviewMouseDownEvent, downHandler, handledEventsToo: true);
        element.AddHandler(UIElement.PreviewMouseRightButtonDownEvent, downHandler, handledEventsToo: true);
        element.AddHandler(UIElement.MouseRightButtonDownEvent, downHandler, handledEventsToo: true);
        element.AddHandler(UIElement.PreviewMouseRightButtonUpEvent, upHandler, handledEventsToo: true);
        element.AddHandler(UIElement.MouseRightButtonUpEvent, upHandler, handledEventsToo: true);
    }

    private void SheetNavButton_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Right)
            return;

        e.Handled = true;
        BeginShowActivateSheetDialogFromSheetNav();
    }

    private void SheetNavButton_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Right)
            return;

        e.Handled = true;
        BeginShowActivateSheetDialogFromSheetNav();
    }

    private void SheetNavButton_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        e.Handled = true;
        BeginShowActivateSheetDialogFromSheetNav();
    }

    private void BeginShowActivateSheetDialogFromSheetNav()
    {
        if (_activateSheetDialogOpenOrPending)
            return;

        _activateSheetDialogOpenOrPending = true;
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                ShowActivateSheetDialogFromSheetNav();
            }
            finally
            {
                _activateSheetDialogOpenOrPending = false;
            }
        }, DispatcherPriority.Input);
    }

    private void ShowActivateSheetDialogFromSheetNav()
    {
        var dialog = new ActivateSheetDialog(_workbook, _currentSheetId) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        _currentSheetId = dialog.Result.SheetId;
        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(_currentSheetId);
        _sheetGroupAnchor = _currentSheetId;
        UpdateViewport();
        RefreshSheetTabs();
        FocusSheetGridIfNeeded();
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

    private void SheetTabsRowGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateSheetTabNavigation();
    }

    private void UpdateSheetTabNavigation()
    {
        UpdateSheetTabViewportWidth();
        UpdateSheetTabsScrollerClip();
        var canScroll = SheetTabsScroller.ScrollableWidth > SheetTabScrollEpsilon;
        var canScrollLeft = canScroll && SheetTabsScroller.HorizontalOffset > SheetTabScrollEpsilon;
        var canScrollRight = canScroll &&
                             SheetTabsScroller.HorizontalOffset < SheetTabsScroller.ScrollableWidth - SheetTabScrollEpsilon;

        SheetNavLeftBtn.Visibility = canScroll ? Visibility.Visible : Visibility.Hidden;
        SheetNavRightBtn.Visibility = canScroll ? Visibility.Visible : Visibility.Hidden;
        var activeNavigationBrush = TryFindResource("FreeXAccentDarkBrush") as Brush
            ?? new SolidColorBrush(Color.FromRgb(0x17, 0x32, 0x4D));
        var inactiveNavigationBrush = TryFindResource("FreeXBorderStrongBrush") as Brush
            ?? new SolidColorBrush(Color.FromRgb(0xC8, 0xCC, 0xD0));
        SheetNavLeftBtn.Foreground = canScrollLeft ? activeNavigationBrush : inactiveNavigationBrush;
        SheetNavRightBtn.Foreground = canScrollRight ? activeNavigationBrush : inactiveNavigationBrush;
        SheetNavLeftBtn.IsHitTestVisible = canScroll;
        SheetNavRightBtn.IsHitTestVisible = canScroll;
        UpdateAddSheetButtonInteractivity();
        UpdateSheetTabsChromeLayer();
    }

    private double GetSheetTabsVisibleViewportRight()
    {
        var viewportRight = Math.Max(0, SheetTabsScroller.ActualWidth);
        if (SheetNavRightBtn.Visibility != Visibility.Visible)
            return viewportRight;

        var reserveWidth = SheetNavRightBtn.ActualWidth > 0
            ? SheetNavRightBtn.ActualWidth
            : SheetTabRightNavigationReserveWidth;
        return Math.Max(0, viewportRight - reserveWidth);
    }

    private Rect? TryGetAddSheetButtonViewportBounds()
    {
        if (AddSheetButton.ActualWidth <= 0 || AddSheetButton.ActualHeight <= 0)
            return null;

        return AddSheetButton.TransformToAncestor(SheetTabsScroller)
            .TransformBounds(new Rect(new Point(0, 0), AddSheetButton.RenderSize));
    }

    private void UpdateAddSheetButtonInteractivity()
    {
        var isFullyVisible = TryGetAddSheetButtonViewportBounds() is { } bounds &&
                             bounds.Left >= -SheetTabScrollEpsilon &&
                             bounds.Right <= GetSheetTabsVisibleViewportRight() + SheetTabScrollEpsilon;

        AddSheetButton.IsHitTestVisible = isFullyVisible;
        AddSheetButton.Focusable = isFullyVisible;
    }

    private void UpdateSheetTabViewportWidth()
    {
        if (SheetTabsRowGrid.ActualWidth <= 0)
            return;

        var rowHeaderWidth = SheetGrid.ActualRowHeaderWidth;
        SheetTabsLeadingSpacer.Width = rowHeaderWidth;
        var rowWidth = GetSheetTabsAvailableRowWidth();
        var viewportKey = CreateSheetTabViewportMeasureKey(rowHeaderWidth, rowWidth);
        if (_lastSheetTabViewportMeasureKey == viewportKey && _lastSheetTabViewportContentWidth > 0)
        {
            ApplySheetTabViewportWidths(_lastSheetTabViewportContentWidth, rowHeaderWidth, rowWidth);
            return;
        }

        SheetTabsControl.Measure(new Size(double.PositiveInfinity, SheetTabsRowGrid.ActualHeight));
        AddSheetButton.Measure(new Size(double.PositiveInfinity, SheetTabsRowGrid.ActualHeight));
        SheetTabsTrailingViewportReserve.Measure(new Size(double.PositiveInfinity, SheetTabsRowGrid.ActualHeight));
        var tabContentWidth = MeasureSheetTabContentWidth();
        if (tabContentWidth <= 0)
            return;

        _lastSheetTabViewportMeasureKey = viewportKey;
        _lastSheetTabViewportContentWidth = tabContentWidth;
        ApplySheetTabViewportWidths(tabContentWidth, rowHeaderWidth, rowWidth);
    }

    private double GetSheetTabsAvailableRowWidth()
    {
        var rowWidth = SheetTabsRowGrid.ActualWidth;
        if (WindowState == WindowState.Normal && !double.IsNaN(Width) && Width > 0)
            rowWidth = Math.Min(rowWidth, Width);
        else if (RootGrid.ActualWidth > 0)
            rowWidth = Math.Min(rowWidth, RootGrid.ActualWidth);

        return rowWidth;
    }

    private void ApplySheetTabViewportWidths(double tabContentWidth, double rowHeaderWidth, double rowWidth)
    {
        var layout = SheetTabScrollbarLayoutPlanner.Plan(tabContentWidth, rowHeaderWidth, rowWidth);
        var targetWidth = layout.SheetTabsViewportWidth;
        var targetScrollbarWidth = layout.HorizontalScrollbarWidth;

        var tabsWidthUnchanged = Math.Abs(SheetTabsScroller.Width - targetWidth) <= 0.5;
        var scrollbarWidthUnchanged = Math.Abs(HorizontalScroll.Width - targetScrollbarWidth) <= 0.5;
        if (tabsWidthUnchanged && scrollbarWidthUnchanged)
            return;

        SheetTabsScroller.Width = targetWidth;
        HorizontalScroll.Width = targetScrollbarWidth;
        if (_sheetTabViewportRefreshQueued)
            return;

        _sheetTabViewportRefreshQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _sheetTabViewportRefreshQueued = false;
            UpdateSheetTabNavigation();
        }, DispatcherPriority.Loaded);
    }

    private SheetTabViewportMeasureKey CreateSheetTabViewportMeasureKey(double rowHeaderWidth, double rowWidth)
    {
        var tabHash = new HashCode();
        foreach (var tab in _sheetTabs)
        {
            tabHash.Add(tab.Id);
            tabHash.Add(tab.Name, StringComparer.Ordinal);
            tabHash.Add(tab.IsProtected);
            if (tab.TabColor is { } color)
            {
                tabHash.Add(color.R);
                tabHash.Add(color.G);
                tabHash.Add(color.B);
            }
            else
            {
                tabHash.Add(0);
            }
        }

        return new SheetTabViewportMeasureKey(
            QuantizeSheetTabLayoutValue(rowHeaderWidth),
            QuantizeSheetTabLayoutValue(rowWidth),
            QuantizeSheetTabLayoutValue(SheetTabsRowGrid.ActualHeight),
            QuantizeSheetTabLayoutValue(AddSheetButton.ActualWidth),
            QuantizeSheetTabLayoutValue(AddSheetButton.ActualHeight),
            QuantizeSheetTabLayoutValue(AddSheetButton.MinWidth),
            _sheetTabs.Count,
            tabHash.ToHashCode());
    }

    private void UpdateSheetTabsScrollerClip()
    {
        if (SheetTabsScroller.ActualWidth <= 0 || SheetTabsScroller.ActualHeight <= 0)
        {
            SheetTabsScroller.Clip = null;
            return;
        }

        var geometry = new RectangleGeometry(new Rect(0, 0, SheetTabsScroller.ActualWidth, SheetTabsScroller.ActualHeight));
        geometry.Freeze();
        SheetTabsScroller.Clip = geometry;
    }

    private double MeasureSheetTabContentWidth()
    {
        var measuredBounds = _sheetTabs
            .Select(tab => SheetTabsControl.ItemContainerGenerator.ContainerFromItem(tab) as FrameworkElement)
            .Where(container => container is not null && container.ActualWidth > 0 && container.ActualHeight > 0)
            .Select(container => SheetTabChromeBounds(container!, SheetTabOverlapWidth))
            .ToList();
        if (AddSheetButton.ActualWidth > 0 && AddSheetButton.ActualHeight > 0)
            measuredBounds.Add(SheetTabChromeBounds(AddSheetButton, SheetTabOverlapWidth));
        if (SheetTabsTrailingViewportReserve.ActualWidth > 0 && SheetTabsTrailingViewportReserve.ActualHeight > 0)
            measuredBounds.Add(SheetTabChromeBounds(SheetTabsTrailingViewportReserve, 0));
        if (measuredBounds.Count > 0)
        {
            var left = measuredBounds.Min(bounds => bounds.Left);
            var right = measuredBounds.Max(bounds => bounds.Right);
            var measuredWidth = Math.Max(0, right - left);
            if (AddSheetButton.ActualWidth <= 0 || AddSheetButton.ActualHeight <= 0)
                measuredWidth += ResolveLayoutWidth(AddSheetButton);
            if (SheetTabsTrailingViewportReserve.ActualWidth <= 0 || SheetTabsTrailingViewportReserve.ActualHeight <= 0)
                measuredWidth += ResolveLayoutWidth(SheetTabsTrailingViewportReserve);

            return Math.Max(measuredWidth, EstimateSheetTabContentWidth());
        }

        return EstimateSheetTabContentWidth();
    }

    private double EstimateSheetTabContentWidth()
    {
        if (_sheetTabs.Count == 0)
            return 0;

        var measuredWidth = SheetTabOverlapWidth;
        foreach (var tab in _sheetTabs)
            measuredWidth += Math.Max(0, EstimateSheetTabWidth(tab) - SheetTabOverlapWidth);

        measuredWidth += ResolveLayoutWidth(AddSheetButton);
        measuredWidth += ResolveLayoutWidth(SheetTabsTrailingViewportReserve);
        return measuredWidth;
    }

    private static double EstimateSheetTabWidth(SheetTabViewModel tab) =>
        SheetTabWidthEstimator.Estimate(tab.Name, tab.IsProtected, SheetTabWidthEstimator.Wpf);

    private static double ResolveLayoutWidth(FrameworkElement element)
    {
        if (element.ActualWidth > 0)
            return element.ActualWidth;
        if (!double.IsNaN(element.Width) && element.Width > 0)
            return element.Width;
        return Math.Max(0, element.MinWidth);
    }

    private void UpdateSheetTabsChromeLayer()
    {
        if (SheetTabsChromeLayer.ActualWidth <= 0 || SheetTabsRowGrid.ActualWidth <= 0)
            return;

        var chromeWidth = SheetTabsChromeLayer.ActualWidth;
        var accentBrush = TryFindResource("FreeXAccentBrush") as Brush
            ?? new SolidColorBrush(Color.FromRgb(0x0F, 0x6D, 0x8C));
        var separatorBrush = SheetTabSeparatorBrush;

        Rect? addRect = null;
        Rect? addContentRect = null;
        if (AddSheetButton.ActualWidth > 0)
        {
            addRect = SheetTabChromeBounds(AddSheetButton, SheetTabOverlapWidth);
            addContentRect = TryGetAddSheetButtonContentBounds();
        }

        var visibleTabs = _sheetTabs.ToList();
        var tabRects = visibleTabs
            .Select(tab => TryGetSheetTabChromeBounds(tab, chromeWidth))
            .ToArray();
        var tabLabelRects = visibleTabs
            .Select(TryGetSheetTabLabelBounds)
            .ToArray();
        var renderKey = CreateSheetTabsChromeRenderKey(
            chromeWidth,
            addRect,
            addContentRect,
            visibleTabs,
            tabRects,
            tabLabelRects);
        if (_lastSheetTabsChromeRenderKey == renderKey)
            return;

        _lastSheetTabsChromeRenderKey = renderKey;
        SheetTabsChromeLayer.Children.Clear();
        SheetTabsOverlayLayer.Children.Clear();
        var tabClipGeometry = CreateVisibleSheetTabClipGeometry(addRect);

        var activeTabIndex = FindCurrentSheetTabIndex(visibleTabs);
        var activeTab = activeTabIndex >= 0 ? visibleTabs[activeTabIndex] : null;
        Rect? activeRect = null;
        if (activeTabIndex >= 0)
            activeRect = ClipSheetTabChromeBoundsToVisibleTabs(
                tabRects[activeTabIndex],
                addRect);

        if (activeTabIndex < 0)
        {
            for (var tabIndex = 0; tabIndex < visibleTabs.Count; tabIndex++)
                RenderInactiveSheetTab(tabIndex);
        }
        else
        {
            for (var tabIndex = 0; tabIndex < activeTabIndex; tabIndex++)
                RenderInactiveSheetTab(tabIndex);

            for (var tabIndex = visibleTabs.Count - 1; tabIndex > activeTabIndex; tabIndex--)
                RenderInactiveSheetTab(tabIndex);
        }

        void RenderInactiveSheetTab(int tabIndex)
        {
            var tab = visibleTabs[tabIndex];
            if (tab.Id == _currentSheetId ||
                ClipSheetTabChromeBoundsToVisibleTabs(tabRects[tabIndex], addRect) is not { } tabRect)
                return;

            var nextTabIsActive = tabIndex < visibleTabs.Count - 1 &&
                                  visibleTabs[tabIndex + 1].Id == _currentSheetId;

            if (tab.TabColor is not null || tab.IsGrouped)
            {
                var fillBrush = tab.TabColor is { } tabColor
                    ? CreatePastelTabBrush(tabColor)
                    : (TryFindResource("FreeXAccentSoftBrush") as Brush
                        ?? new SolidColorBrush(Color.FromRgb(0xE6, 0xF6, 0xFA)));
                SheetTabsChromeLayer.Children.Add(CreateSheetTabPath(
                    CreateInactiveSheetTabFillGeometry(tabRect),
                    fillBrush,
                    null,
                    0,
                    tabClipGeometry,
                    tab.IsGrouped ? 0.9 : 0.82));
            }

            if (nextTabIsActive)
                return;

            SheetTabsChromeLayer.Children.Add(CreateSheetTabPath(
                CreateSheetTabSeparatorGeometry(ResolveSheetTabSeparatorX(tabIndex, tabRect)),
                null,
                separatorBrush,
                SheetTabGridRuleStrokeThickness,
                tabClipGeometry,
                0.68));
        }

        double ResolveSheetTabSeparatorX(int tabIndex, Rect tabRect)
        {
            var nextLabelBounds = tabIndex < visibleTabs.Count - 1
                ? tabLabelRects[tabIndex + 1]
                : addContentRect;
            if (tabLabelRects[tabIndex] is { } currentLabelBounds &&
                nextLabelBounds is { } nextLabel &&
                currentLabelBounds.Right < nextLabel.Left - SheetTabScrollEpsilon)
                return (currentLabelBounds.Right + nextLabel.Left) / 2.0;

            return tabRect.Right;
        }

        if (activeRect is { } active)
        {
            var activeFillBrush = activeTab?.TabColor is { } activeTabColor
                ? CreatePastelTabBrush(activeTabColor)
                : (TryFindResource("FreeXRibbonSurfaceBrush") as Brush ?? Brushes.White);
            SheetTabsChromeLayer.Children.Add(CreateSheetTabPath(
                CreateActiveSheetTabFillGeometry(active),
                activeFillBrush,
                null,
                0,
                tabClipGeometry,
                1));
            RenderSheetTabsOverlay(activeRect, addRect, tabClipGeometry, accentBrush);
            return;
        }

        RenderSheetTabsOverlay(activeRect, addRect, tabClipGeometry, accentBrush);
    }

    private SheetTabsChromeRenderKey CreateSheetTabsChromeRenderKey(
        double chromeWidth,
        Rect? addRect,
        Rect? addContentRect,
        IReadOnlyList<SheetTabViewModel> visibleTabs,
        IReadOnlyList<Rect?> tabRects,
        IReadOnlyList<Rect?> tabLabelRects)
    {
        var tabHash = new HashCode();
        for (var i = 0; i < visibleTabs.Count; i++)
        {
            var tab = visibleTabs[i];
            tabHash.Add(tab.Id);
            tabHash.Add(tab.IsGrouped);
            tabHash.Add(tab.IsProtected);
            tabHash.Add(tab.IsLeftSideCoveredByActive);
            tabHash.Add(tab.IsRightSideCoveredByActive);
            if (tab.TabColor is { } color)
            {
                tabHash.Add(color.R);
                tabHash.Add(color.G);
                tabHash.Add(color.B);
            }
            else
            {
                tabHash.Add(0);
            }

            AddRectToHash(ref tabHash, tabRects[i]);
            AddRectToHash(ref tabHash, tabLabelRects[i]);
        }

        AddRectToHash(ref tabHash, addContentRect);
        return new SheetTabsChromeRenderKey(
            QuantizeSheetTabLayoutValue(chromeWidth),
            QuantizeSheetTabLayoutValue(SheetTabsRowGrid.ActualWidth),
            QuantizeSheetTabLayoutValue(SheetTabsScroller.HorizontalOffset),
            QuantizeSheetTabLayoutValue(SheetTabsScroller.ActualWidth),
            QuantizeSheetTabLayoutValue(SheetTabsScroller.ActualHeight),
            QuantizeSheetTabLayoutValue(GetSheetTabsVisibleViewportRight()),
            QuantizeSheetTabLayoutValue(AddSheetButton.ActualWidth),
            QuantizeSheetTabLayoutValue(AddSheetButton.ActualHeight),
            QuantizeSheetTabLayoutValue(addRect?.Left ?? double.NaN),
            QuantizeSheetTabLayoutValue(addRect?.Right ?? double.NaN),
            _currentSheetId,
            visibleTabs.Count,
            tabHash.ToHashCode());
    }

    private static void AddRectToHash(ref HashCode hash, Rect? rect)
    {
        if (rect is not { } value)
        {
            hash.Add(0);
            return;
        }

        hash.Add(1);
        hash.Add(QuantizeSheetTabLayoutValue(value.Left));
        hash.Add(QuantizeSheetTabLayoutValue(value.Top));
        hash.Add(QuantizeSheetTabLayoutValue(value.Width));
        hash.Add(QuantizeSheetTabLayoutValue(value.Height));
    }

    private static long QuantizeSheetTabLayoutValue(double value)
        => double.IsFinite(value) ? (long)Math.Round(value * 2, MidpointRounding.AwayFromZero) : long.MinValue;

    private void RenderSheetTabsOverlay(
        Rect? activeRect,
        Rect? addRect,
        Geometry tabClipGeometry,
        Brush gridRuleBrush)
    {
        var chromeWidth = SheetTabsChromeLayer.ActualWidth;
        var visibleTabsLeft = Math.Clamp(SheetTabChromeBounds(SheetTabsScroller, 0).Left, 0, chromeWidth);
        var visibleTabsRight = Math.Clamp(GetVisibleSheetTabsRight(addRect), 0, chromeWidth);
        var gridRuleGeometry = CreateSheetTabTopRuleGeometry(
            chromeWidth,
            activeRect,
            visibleTabsLeft,
            visibleTabsRight);
        SheetTabsOverlayLayer.Children.Add(CreateSheetTabPath(
            gridRuleGeometry,
            null,
            gridRuleBrush,
            SheetTabGridRuleStrokeThickness,
            null,
            1));

        if (activeRect is not { } active)
            return;

        SheetTabsOverlayLayer.Children.Add(CreateSheetTabPath(
            CreateActiveSheetTabContourGeometry(active),
            null,
            gridRuleBrush,
            SheetTabGridRuleStrokeThickness,
            tabClipGeometry,
            1));
    }

    private Rect SheetTabChromeBounds(FrameworkElement element, double leftOverlap)
    {
        var elementBounds = SheetTabsLayerBounds(element);
        return new Rect(elementBounds.Left - leftOverlap, 0, elementBounds.Width + leftOverlap, SheetTabChromeHeight);
    }

    private Rect SheetTabsLayerBounds(FrameworkElement element)
    {
        var elementBounds = element.TransformToAncestor(SheetTabsRowGrid)
            .TransformBounds(new Rect(new Point(0, 0), element.RenderSize));
        var layerBounds = SheetTabsChromeLayer.TransformToAncestor(SheetTabsRowGrid)
            .TransformBounds(new Rect(new Point(0, 0), SheetTabsChromeLayer.RenderSize));
        return new Rect(
            elementBounds.Left - layerBounds.Left,
            elementBounds.Top - layerBounds.Top,
            elementBounds.Width,
            elementBounds.Height);
    }

    private Rect? TryGetSheetTabChromeBounds(SheetTabViewModel tab, double chromeWidth)
    {
        if (SheetTabsControl.ItemContainerGenerator.ContainerFromItem(tab) is not FrameworkElement container ||
            container.ActualWidth <= 0)
            return null;

        var chromeTarget = FindSheetTabContextMenuTarget(tab) ?? container;
        var tabRect = SheetTabChromeBounds(chromeTarget, 0);
        return tabRect.Right < -16 || tabRect.Left > chromeWidth + 16
            ? null
            : tabRect;
    }

    private Rect? TryGetSheetTabLabelBounds(SheetTabViewModel tab)
    {
        if (SheetTabsControl.ItemContainerGenerator.ContainerFromItem(tab) is not DependencyObject container)
            return null;

        var label = FindVisualDescendant<FrameworkElement>(
            container,
            element => ReferenceEquals(element.DataContext, tab) &&
                       string.Equals(element.Name, "SheetTabLabelContent", StringComparison.Ordinal));
        return label is { ActualWidth: > 0, ActualHeight: > 0 }
            ? SheetTabsLayerBounds(label)
            : null;
    }

    private Rect? TryGetAddSheetButtonContentBounds()
    {
        AddSheetButton.ApplyTemplate();
        var label = FindVisualDescendant<TextBlock>(
            AddSheetButton,
            textBlock => string.Equals(textBlock.Text, "+", StringComparison.Ordinal));
        return label is { ActualWidth: > 0, ActualHeight: > 0 }
            ? SheetTabsLayerBounds(label)
            : null;
    }

    private Rect? ClipSheetTabChromeBoundsToVisibleTabs(Rect? tabRect, Rect? addRect)
    {
        if (tabRect is not { } rect)
            return null;

        var visibleRight = GetVisibleSheetTabsRight(addRect);
        if (rect.Left >= visibleRight - SheetTabScrollEpsilon)
            return null;

        if (rect.Right <= visibleRight)
            return rect;

        var visibleWidth = visibleRight - rect.Left;
        return visibleWidth >= 24
            ? rect
            : null;
    }

    private Geometry CreateVisibleSheetTabClipGeometry(Rect? addRect)
    {
        var scrollerBounds = SheetTabChromeBounds(SheetTabsScroller, 0);
        var left = Math.Clamp(scrollerBounds.Left, 0, SheetTabsChromeLayer.ActualWidth);
        var right = Math.Clamp(GetVisibleSheetTabsRight(addRect), 0, SheetTabsChromeLayer.ActualWidth);

        var geometry = new RectangleGeometry(new Rect(left, -3, Math.Max(0, right - left), SheetTabChromeHeight + 6));
        geometry.Freeze();
        return geometry;
    }

    private double GetVisibleSheetTabsRight(Rect? addRect)
    {
        var scrollerBounds = SheetTabChromeBounds(SheetTabsScroller, 0);
        var right = Math.Min(
            scrollerBounds.Right,
            scrollerBounds.Left + GetSheetTabsVisibleViewportRight());
        if (addRect is { } add)
            right = Math.Min(right, add.Left + SheetTabOverlapWidth);

        return right;
    }

    private static SolidColorBrush CreatePastelTabBrush(CellColor color)
    {
        const byte baseComponent = 243;
        return new SolidColorBrush(Color.FromRgb(
            BlendColorComponent(baseComponent, color.R, 0.2),
            BlendColorComponent(baseComponent, color.G, 0.2),
            BlendColorComponent(baseComponent, color.B, 0.2)));
    }

    private static SolidColorBrush CreateFrozenSheetTabBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private static byte BlendColorComponent(byte background, byte foreground, double foregroundWeight)
        => (byte)Math.Round(background + (foreground - background) * foregroundWeight);

    private static Geometry CreateSheetTabTopRuleGeometry(
        double width,
        Rect? activeTab,
        double visibleTabsLeft,
        double visibleTabsRight)
    {
        const double top = SheetTabGridRuleTop;
        if (activeTab is not { } tab)
            return CreateFrozenLineGeometry(new Point(0, top), new Point(width, top));

        var gapLeft = Math.Clamp(Math.Max(tab.Left, visibleTabsLeft), 0, width);
        var gapRight = Math.Clamp(Math.Min(tab.Right, visibleTabsRight), 0, width);
        if (gapRight <= gapLeft + SheetTabScrollEpsilon)
            return CreateFrozenLineGeometry(new Point(0, top), new Point(width, top));

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            if (gapLeft > SheetTabScrollEpsilon)
            {
                context.BeginFigure(new Point(0, top), false, false);
                context.LineTo(new Point(gapLeft, top), true, true);
            }

            if (gapRight < width - SheetTabScrollEpsilon)
            {
                context.BeginFigure(new Point(gapRight, top), false, false);
                context.LineTo(new Point(width, top), true, true);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static LineGeometry CreateFrozenLineGeometry(Point start, Point end)
    {
        var geometry = new LineGeometry(start, end);
        geometry.Freeze();
        return geometry;
    }

    private static Geometry CreateActiveSheetTabContourGeometry(Rect tab)
    {
        const double top = SheetTabGridRuleTop;
        const double sideTop = top + 6.5;
        const double sideInset = 8.0;
        const double sideBottom = top + 23.5;
        const double sideBottomControl = top + 25.5;
        const double bottomInset = 12.0;
        const double bottom = top + 27.0;
        var left = tab.Left;
        var right = tab.Right;

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(left, top), false, false);
            context.BezierTo(
                new Point(left + sideInset, top),
                new Point(left + sideInset, sideTop),
                new Point(left + sideInset, sideTop),
                true,
                true);
            context.LineTo(new Point(left + sideInset, sideBottom), true, true);
            context.BezierTo(
                new Point(left + sideInset, sideBottomControl),
                new Point(left + bottomInset - 2, bottom),
                new Point(left + bottomInset, bottom),
                true,
                true);
            context.LineTo(new Point(right - bottomInset, bottom), true, true);
            context.BezierTo(
                new Point(right - bottomInset + 2, bottom),
                new Point(right - sideInset, sideBottomControl),
                new Point(right - sideInset, sideBottom),
                true,
                true);
            context.LineTo(new Point(right - sideInset, sideTop), true, true);
            context.BezierTo(
                new Point(right - sideInset, sideTop),
                new Point(right - sideInset, top),
                new Point(right, top),
                true,
                true);
        }

        geometry.Freeze();
        return geometry;
    }

    private static Geometry CreateSheetTabSeparatorGeometry(double x)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(x, SheetTabGridRuleTop + 7.0), false, false);
            context.LineTo(new Point(x, SheetTabGridRuleTop + 21.0), true, true);
        }

        geometry.Freeze();
        return geometry;
    }

    private static Geometry CreateInactiveSheetTabFillGeometry(Rect tab)
    {
        const double top = SheetTabGridRuleTop + 1.5;
        const double bottom = SheetTabGridRuleTop + 26.5;
        const double horizontalInset = 8.0;
        const double radius = 4.0;
        var left = tab.Left + horizontalInset;
        var right = tab.Right - horizontalInset;
        if (right <= left + radius * 2)
            return Geometry.Empty;

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(left, top), true, true);
            context.LineTo(new Point(right, top), true, true);
            context.LineTo(new Point(right, bottom - radius), true, true);
            context.QuadraticBezierTo(new Point(right, bottom), new Point(right - radius, bottom), true, true);
            context.LineTo(new Point(left + radius, bottom), true, true);
            context.QuadraticBezierTo(new Point(left, bottom), new Point(left, bottom - radius), true, true);
            context.LineTo(new Point(left, top), true, true);
        }

        geometry.Freeze();
        return geometry;
    }

    private static Geometry CreateActiveSheetTabFillGeometry(Rect tab)
    {
        const double top = SheetTabGridRuleTop;
        const double sideTop = top + 6.5;
        const double sideInset = 8.0;
        const double sideBottom = top + 23.5;
        const double sideBottomControl = top + 25.5;
        const double bottomInset = 12.0;
        const double bottom = top + 27.0;
        var left = tab.Left;
        var right = tab.Right;

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(left, top), true, true);
            context.BezierTo(new Point(left + sideInset, top), new Point(left + sideInset, sideTop), new Point(left + sideInset, sideTop), true, true);
            context.LineTo(new Point(left + sideInset, sideBottom), true, true);
            context.BezierTo(new Point(left + sideInset, sideBottomControl), new Point(left + bottomInset - 2, bottom), new Point(left + bottomInset, bottom), true, true);
            context.LineTo(new Point(right - bottomInset, bottom), true, true);
            context.BezierTo(new Point(right - bottomInset + 2, bottom), new Point(right - sideInset, sideBottomControl), new Point(right - sideInset, sideBottom), true, true);
            context.LineTo(new Point(right - sideInset, sideTop), true, true);
            context.BezierTo(new Point(right - sideInset, top), new Point(right - sideInset, top), new Point(right, top), true, true);
            context.LineTo(new Point(left, top), true, true);
        }

        geometry.Freeze();
        return geometry;
    }

    private static System.Windows.Shapes.Path CreateSheetTabPath(
        Geometry data,
        Brush? fill,
        Brush? stroke,
        double strokeThickness,
        Geometry? clip,
        double opacity)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = data,
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = strokeThickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Opacity = opacity,
            SnapsToDevicePixels = true,
            IsHitTestVisible = false,
            Clip = clip
        };

        return path;
    }

    private void BringCurrentSheetTabIntoView()
    {
        var visibleTabs = _sheetTabs.ToList();
        var activeIndex = FindCurrentSheetTabIndex(visibleTabs);
        if (activeIndex < 0)
            return;

        var activeTab = visibleTabs[activeIndex];
        if (activeTab is null)
            return;

        var visibleViewportRight = GetSheetTabsVisibleViewportRight();
        if (visibleViewportRight <= SheetTabScrollEpsilon)
            return;

        var currentOffset = SheetTabsScroller.HorizontalOffset;
        var tabViewportBounds = visibleTabs
            .Select(TryGetSheetTabViewportBounds)
            .ToArray();
        if (tabViewportBounds[activeIndex] is not { } activeBounds)
            return;

        var targetLeft = activeBounds.Left;
        var targetRight = activeBounds.Right;
        if (activeIndex == visibleTabs.Count - 1 &&
            TryGetAddSheetButtonViewportBounds() is { } addSheetBounds)
        {
            targetLeft = Math.Min(targetLeft, addSheetBounds.Left);
            targetRight = Math.Max(targetRight, addSheetBounds.Right);
        }

        var targetOffset = SheetTabViewportScrollPlanner.CalculateOffsetForSelectedTab(
            currentOffset,
            targetLeft,
            targetRight,
            visibleViewportRight,
            SheetTabsScroller.ScrollableWidth,
            SheetTabScrollEpsilon);
        if (Math.Abs(targetOffset - currentOffset) > SheetTabScrollEpsilon)
            SheetTabsScroller.ScrollToHorizontalOffset(targetOffset);
    }

    private Rect? TryGetSheetTabViewportBounds(SheetTabViewModel tab)
    {
        if (SheetTabsControl.ItemContainerGenerator.ContainerFromItem(tab) is not FrameworkElement container ||
            container.ActualWidth <= 0 ||
            container.ActualHeight <= 0)
            return null;

        return container.TransformToAncestor(SheetTabsScroller)
            .TransformBounds(new Rect(new Point(0, 0), container.RenderSize));
    }

    private bool TryFocusCurrentSheetTab()
    {
        BringCurrentSheetTabIntoView();
        var activeTab = FindCurrentSheetTab();
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
            var tabId = tab.Id;
            SelectSheetTabForKeyboardContextMenu(tabId);
            var refreshedTab = FindSheetTab(tabId);
            target = refreshedTab is null ? target : FindSheetTabContextMenuTarget(refreshedTab) ?? target;
            contextMenu = target.ContextMenu;
            if (contextMenu is null)
                return false;
        }

        RebuildSheetTabContextMenu(contextMenu, target.DataContext as SheetTabViewModel);
        MenuKeyTipAssigner.AssignUniqueKeyTips(contextMenu.Items.OfType<MenuItem>());
        contextMenu.Opened -= SheetTabContextMenu_Opened;
        contextMenu.Opened += SheetTabContextMenu_Opened;
        contextMenu.PlacementTarget = target;
        contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        contextMenu.IsOpen = true;
        return true;
    }

    private static void SheetTabContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu contextMenu)
            return;

        var firstEnabledItem = FindFirstEnabledMenuItem(contextMenu);
        if (firstEnabledItem is null)
            return;

        firstEnabledItem.Focus();
        Keyboard.Focus(firstEnabledItem);
    }

    // Builds the per-tab context menu from the neutral SheetTabContextMenuPlanner so the
    // sheet-tab menu's labels, order, keytips, and enablement are single-sourced with the Avalonia port
    // instead of hand-authored in XAML. The menu is rebuilt before opening so Excel-like disabled rows
    // such as Unhide and Ungroup Sheets reflect the current workbook state.
    private void SheetTabChrome_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
            return;

        element.ContextMenuOpening -= SheetTabChrome_ContextMenuOpening;
        element.ContextMenuOpening += SheetTabChrome_ContextMenuOpening;
        element.ContextMenu = BuildSheetTabContextMenu(element.DataContext as SheetTabViewModel);
    }

    private void SheetTabChrome_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            element.ContextMenu is not { } contextMenu)
        {
            return;
        }

        RebuildSheetTabContextMenu(contextMenu, element.DataContext as SheetTabViewModel);
    }

    private ContextMenu BuildSheetTabContextMenu(SheetTabViewModel? tab)
    {
        var menu = new ContextMenu();
        menu.Opened += SheetTabContextMenu_Opened;
        RebuildSheetTabContextMenu(menu, tab);

        return menu;
    }

    private void RebuildSheetTabContextMenu(ContextMenu menu, SheetTabViewModel? tab)
    {
        menu.Items.Clear();
        var state = BuildSheetTabContextMenuState(tab);
        foreach (var command in SheetTabContextMenuPlanner.BuildSheetTabCommands(state))
            AddSheetTabContextMenuItem(menu.Items, command);
    }

    private SheetTabContextMenuState BuildSheetTabContextMenuState(SheetTabViewModel? tab)
    {
        var visibleSheetCount = _workbook.Sheets.Count(sheet => !sheet.IsHidden);
        var hiddenSheetCount = _workbook.Sheets.Count(sheet => sheet.IsHidden && !sheet.IsVeryHidden);
        var selectedSheetIsVisible = tab is not null &&
                                     _workbook.Sheets.Any(sheet => sheet.Id == tab.Id && !sheet.IsHidden);

        return new SheetTabContextMenuState(
            CanDeleteSheet: selectedSheetIsVisible && visibleSheetCount > 1,
            CanHideSheet: selectedSheetIsVisible && visibleSheetCount > 1,
            CanUnhideSheet: hiddenSheetCount > 0,
            CanSelectAllSheets: visibleSheetCount > 1,
            CanUngroupSheets: _groupedSheetIds.Count > 1);
    }

    private void AddSheetTabContextMenuItem(
        ItemCollection target,
        SheetTabContextMenuCommand command)
    {
        if (command.IsSeparator)
        {
            target.Add(new Separator());
            return;
        }

        var menuItem = new MenuItem
        {
            Header = UiText.Get(command.ResourceKey),
            IsEnabled = command.IsEnabled
        };

        if (!string.IsNullOrEmpty(command.KeyTip))
            RibbonTooltip.SetKeyTip(menuItem, command.KeyTip);
        if (!string.IsNullOrEmpty(command.CommandName))
            RibbonMetadata.SetCommandName(menuItem, command.CommandName);

        if (ResolveSheetTabContextMenuHandler(command.Action) is { } handler)
            menuItem.Click += (clickSender, clickArgs) => handler(clickSender, clickArgs);

        target.Add(menuItem);
    }

    // Maps neutral planner actions to the existing sheet-tab Click handlers. "View Code" intentionally has
    // no handler (it was always disabled in the XAML); every other action routes to the same handler the
    // hand-authored ContextMenu wired, so dispatch through GetContextMenuTab(sender) resolves the tab.
    private RoutedEventHandler? ResolveSheetTabContextMenuHandler(SheetTabContextMenuAction action) =>
        action switch
        {
            SheetTabContextMenuAction.InsertSheet => SheetCtxInsert_Click,
            SheetTabContextMenuAction.DeleteSheet => SheetCtxDelete_Click,
            SheetTabContextMenuAction.Rename => SheetCtxRename_Click,
            SheetTabContextMenuAction.MoveOrCopy => SheetCtxMoveOrCopy_Click,
            SheetTabContextMenuAction.ProtectSheet => SheetCtxProtectSheet_Click,
            SheetTabContextMenuAction.TabColor => SheetCtxTabColor_Click,
            SheetTabContextMenuAction.Hide => SheetCtxHide_Click,
            SheetTabContextMenuAction.Unhide => SheetCtxUnhide_Click,
            SheetTabContextMenuAction.SelectAllSheets => SheetCtxSelectAllSheets_Click,
            SheetTabContextMenuAction.UngroupSheets => SheetCtxUngroupSheets_Click,
            _ => null
        };

    private bool TryHandleFocusedSheetTabKeyboardNavigation(System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None ||
            Keyboard.FocusedElement is not DependencyObject focusedElement ||
            (!ReferenceEquals(focusedElement, SheetTabsScroller) && !IsDescendantOf(focusedElement, SheetTabsScroller)))
        {
            return false;
        }

        if (FindSheetTabContextMenuTarget(focusedElement) is null)
            return false;

        var handled = e.Key switch
        {
            Key.Left => FocusAdjacentVisibleSheetTab(-1),
            Key.Right => FocusAdjacentVisibleSheetTab(1),
            Key.Home => FocusEdgeVisibleSheetTab(first: true),
            Key.End => FocusEdgeVisibleSheetTab(first: false),
            _ => false
        };

        e.Handled = handled;
        return handled;
    }

    private bool FocusAdjacentVisibleSheetTab(int direction)
    {
        var nextSheetId = SheetTabFocusPlanner.AdjacentTab(_sheetTabs, _currentSheetId, direction, static tab => tab.Id);
        if (nextSheetId is null)
            return false;

        FocusSheetTab(nextSheetId.Value);
        return true;
    }

    private bool FocusEdgeVisibleSheetTab(bool first)
    {
        var sheetId = SheetTabFocusPlanner.EdgeTab(_sheetTabs, first, static tab => tab.Id);
        if (sheetId is null)
            return false;

        FocusSheetTab(sheetId.Value);
        return true;
    }

    private void FocusSheetTab(SheetId sheetId)
    {
        _currentSheetId = sheetId;
        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(sheetId);
        _sheetGroupAnchor = sheetId;
        UpdateViewport();
        RefreshSheetTabs();
        Dispatcher.BeginInvoke(() => TryFocusCurrentSheetTab(), DispatcherPriority.Loaded);
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

    private void SelectSheetTabForKeyboardContextMenu(SheetId tabId)
    {
        SelectSingleSheetTab(tabId);
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
        RenameSheet(tab.Id, tab.Name);
    }

    private void RenameCurrentSheet()
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return;

        RenameSheet(_currentSheetId, sheet.Name);
    }

    private void RenameSheet(SheetId sheetId, string currentName)
    {
        var dialog = new SheetNameDialog(currentName) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        var name = dialog.Result.SheetName;
        if (!string.IsNullOrWhiteSpace(name) && name != currentName)
        {
            if (!TryExecuteCommand(new RenameSheetCommand(sheetId, name), "Rename Sheet"))
                return;

            RecalculateWorkbook();
            RefreshSheetTabs();
        }
    }

    private void SheetCtxInsert_Click(object sender, RoutedEventArgs e)
    {
        // R84-calc-crosssheet-3d-5-3: insert BEFORE the right-clicked tab (matching Excel's own
        // tab-context-menu Insert), not always at the end -- see InsertNewSheet's doc comment.
        var tab = GetContextMenuTab(sender);
        if (tab is null) return;
        InsertNewSheet(tab.Id);
    }

    private void SheetCtxDelete_Click(object sender, RoutedEventArgs e)
    {
        var tab = GetContextMenuTab(sender);
        if (tab == null) return;

        SynchronizeWorkbookSessionSelection();
        var selectedSheetIds = _session.GetCurrentGroupedStructureSheetIds();

        var visibleSheetCount = _workbook.Sheets.Count(s => !s.IsHidden);
        var visibleSelectedCount = _workbook.Sheets.Count(s => !s.IsHidden && selectedSheetIds.Contains(s.Id));
        if (visibleSheetCount - visibleSelectedCount < 1)
        {
            _messageService.ShowWarning(
                UiText.Get("MainWindowMessage_DeleteOnlyVisibleSheet"),
                UiText.Get("MainWindowMessage_DeleteSheetTitle"));
            return;
        }

        var prompt = selectedSheetIds.Count > 1
            ? UiText.Format("MainWindowMessage_DeleteSheetsPrompt", selectedSheetIds.Count)
            : UiText.Format("MainWindowMessage_DeleteSheetPrompt", tab.Name);
        if (!_messageService.AskYesNo(prompt, UiText.Get("MainWindowMessage_DeleteSheetTitle"))) return;

        var result = _session.DeleteSelectedSheets();
        if (!result.Success)
        {
            _messageService.ShowWarning(
                result.ErrorMessage ?? UiText.Get("MainWindowMessage_DeleteOnlyVisibleSheet"),
                UiText.Get("MainWindowMessage_DeleteSheetTitle"));
            return;
        }

        foreach (var sheetId in selectedSheetIds)
        {
            _worksheetSelections.Remove(sheetId);
            // R126-viewstate-delete-purge-1: drop this window's own remembered view state/split
            // offsets for the deleted sheet id(s) too -- otherwise WorksheetViewStateStore and
            // _splitPaneViewportOffsets each keep one stale entry per deleted sheet for the rest
            // of this window's lifetime (only a full New/Open Clear() ever drops them).
            _worksheetViewStates.Remove(sheetId);
            _splitPaneViewportOffsets.Remove(sheetId);
        }

        _currentSheetId = _session.ActiveSheet.Id;
        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(_currentSheetId);
        _sheetGroupAnchor = _currentSheetId;
        ApplyWorkbookSessionSelectionToRenderer();
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

        SynchronizeWorkbookSessionSelection();
        var result = _session.DuplicateSelectedSheets(tab.Id);
        if (!result.Success)
        {
            _messageService.ShowWarning(
                result.ErrorMessage ?? "The selected sheets could not be duplicated.",
                "Duplicate Sheet");
            return;
        }

        _currentSheetId = _session.ActiveSheet.Id;
        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(_currentSheetId);
        _sheetGroupAnchor = _currentSheetId;
        ApplyWorkbookSessionSelectionToRenderer();
        UpdateViewport();
        RefreshSheetTabs();
    }

    private void SheetCtxMoveOrCopy_Click(object sender, RoutedEventArgs e)
    {
        var tab = GetContextMenuTab(sender);
        if (tab == null) return;

        var dialog = new MoveOrCopySheetDialog(_workbook, tab.Id) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        SynchronizeWorkbookSessionSelection();
        var result = _session.MoveOrCopySelectedSheets(
            tab.Id,
            dialog.Result.InsertBeforeIndex,
            dialog.Result.CreateCopy);
        if (!result.Success)
        {
            _messageService.ShowWarning(
                result.ErrorMessage ?? "The selected sheets could not be moved or copied.",
                "Move or Copy Sheet");
            return;
        }

        _currentSheetId = _session.ActiveSheet.Id;
        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(_currentSheetId);
        _sheetGroupAnchor = _currentSheetId;
        ApplyWorkbookSessionSelectionToRenderer();
        UpdateViewport();
        RefreshSheetTabs();
    }

    private void SheetCtxHide_Click(object sender, RoutedEventArgs e)
    {
        var tab = GetContextMenuTab(sender);
        if (tab == null) return;

        var selectedSheetIds = _groupedSheetIds.Contains(tab.Id)
            ? _workbook.Sheets.Select(sheet => sheet.Id).Where(_groupedSheetIds.Contains).ToList()
            : [tab.Id];
        HideSheets(selectedSheetIds);
    }

    private void SheetCtxUnhide_Click(object sender, RoutedEventArgs e)
    {
        UnhideSheet();
    }

    private void HideCurrentSheet()
    {
        var selectedSheetIds = _groupedSheetIds.Contains(_currentSheetId)
            ? _workbook.Sheets.Select(sheet => sheet.Id).Where(_groupedSheetIds.Contains).ToList()
            : [_currentSheetId];
        HideSheets(selectedSheetIds);
    }

    private void HideSheets(IReadOnlyCollection<SheetId> sheetIds)
    {
        SynchronizeWorkbookSessionSelection();
        var result = _session.HideSelectedSheets();
        if (!result.Success)
        {
            _messageService.ShowWarning(
                result.ErrorMessage ?? UiText.Get("MainWindowMessage_DeleteOnlyVisibleSheet"),
                "Hide Sheet");
            return;
        }

        _currentSheetId = _session.ActiveSheet.Id;
        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(_currentSheetId);
        _sheetGroupAnchor = _currentSheetId;
        ApplyWorkbookSessionSelectionToRenderer();
        UpdateViewport();
        RefreshSheetTabs();
    }

    private void UnhideSheet()
    {
        var hiddenSheets = _workbook.Sheets.Where(s => s.IsHidden && !s.IsVeryHidden).ToList();
        if (hiddenSheets.Count == 0)
        {
            _messageService.ShowInfo(
                UiText.Get("MainWindowMessage_NoHiddenSheets"),
                UiText.Get("MainWindowMessage_UnhideSheetTitle"));
            return;
        }

        var dialog = new UnhideSheetDialog(hiddenSheets.Select(sheet => sheet.Name)) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        var name = dialog.Result.SheetName;
        if (string.IsNullOrWhiteSpace(name)) return;

        var sheet = FindHiddenSheetByName(hiddenSheets, name);
        if (sheet is null)
        {
            _messageService.ShowWarning(
                UiText.Get("MainWindowMessage_HiddenSheetNotFound"),
                UiText.Get("MainWindowMessage_UnhideSheetTitle"));
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

        var selectedSheetIds = _groupedSheetIds.Contains(tab.Id)
            ? _workbook.Sheets.Select(sheet => sheet.Id).Where(_groupedSheetIds.Contains).ToList()
            : [tab.Id];
        ColorSheetTabs(tab.Id, selectedSheetIds);
    }

    private void SheetCtxProtectSheet_Click(object sender, RoutedEventArgs e) =>
        ProtectSheetBtn_Click(sender, e);

    private void ColorCurrentSheetTab()
    {
        var selectedSheetIds = _groupedSheetIds.Contains(_currentSheetId)
            ? _workbook.Sheets.Select(sheet => sheet.Id).Where(_groupedSheetIds.Contains).ToList()
            : [_currentSheetId];
        ColorSheetTabs(_currentSheetId, selectedSheetIds);
    }

    private void ColorSheetTabs(SheetId sheetId, IReadOnlyCollection<SheetId> sheetIds)
    {
        var sheet = _workbook.GetSheet(sheetId);
        if (!TryShowColorPicker("Tab Color", sheet?.TabColor ?? new CellColor(15, 109, 140), allowNoColor: true, out var tabColor))
            return;

        SynchronizeWorkbookSessionSelection();
        var result = _session.SetSelectedSheetTabColor(tabColor);
        if (!result.Success)
        {
            _messageService.ShowWarning(result.ErrorMessage ?? "Tab color could not be changed.", "Tab Color");
            return;
        }
        RefreshSheetTabs();
    }

    private void SheetCtxSelectAllSheets_Click(object sender, RoutedEventArgs e)
    {
        var visibleSheetIds = GetVisibleSheetIds();
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

        var fromIndex = FindWorkbookSheetIndex(tab.Id);
        var toIndex = fromIndex + direction;
        if (!TryExecuteCommand(new MoveSheetCommand(fromIndex, toIndex), "Move Sheet"))
            return;

        _currentSheetId = tab.Id;
        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(_currentSheetId);
        _sheetGroupAnchor = _currentSheetId;
        // Moving a sheet can change which sheets fall inside a 3-D span reference
        // (e.g. =SUM(Sheet1:Sheet3!A1)), so recalculate just like the other structural
        // sheet operations (rename/delete/duplicate) do.
        RecalculateWorkbook();
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

    private readonly record struct SheetTabViewportMeasureKey(
        long RowHeaderWidth,
        long RowWidth,
        long RowHeight,
        long AddButtonActualWidth,
        long AddButtonActualHeight,
        long AddButtonMinWidth,
        int TabCount,
        int TabHash);

    private readonly record struct SheetTabsChromeRenderKey(
        long ChromeWidth,
        long RowWidth,
        long HorizontalOffset,
        long ScrollerWidth,
        long ScrollerHeight,
        long VisibleViewportRight,
        long AddButtonWidth,
        long AddButtonHeight,
        long AddButtonLeft,
        long AddButtonRight,
        SheetId CurrentSheetId,
        int TabCount,
        int TabHash);

    private sealed record SheetTabDragTarget(SheetTabViewModel Tab, Rect Bounds);
}
