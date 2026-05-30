using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

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
        SelectSingleSheetTab(tab.Id);
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

    private void RenameSheetFromTab(SheetTabViewModel tab)
    {
        SelectSingleSheetTab(tab.Id);
        UpdateViewport();
        RefreshSheetTabs();
        RenameSheet(tab.Id, tab.Name);
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

    private void SheetTabsRowGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateSheetTabNavigation();
    }

    private void UpdateSheetTabNavigation()
    {
        UpdateSheetTabViewportWidth();
        UpdateSheetTabsScrollerClip();
        UpdateSheetTabsChromeLayer();
        var canScroll = SheetTabsScroller.ScrollableWidth > SheetTabScrollEpsilon;
        var canScrollLeft = canScroll && SheetTabsScroller.HorizontalOffset > SheetTabScrollEpsilon;
        var canScrollRight = canScroll &&
                             SheetTabsScroller.HorizontalOffset < SheetTabsScroller.ScrollableWidth - SheetTabScrollEpsilon;

        SheetNavLeftBtn.Visibility = canScroll ? Visibility.Visible : Visibility.Hidden;
        SheetNavRightBtn.Visibility = canScroll ? Visibility.Visible : Visibility.Hidden;
        var activeNavigationBrush = (Brush)FindResource("FreeXAccentDarkBrush");
        var inactiveNavigationBrush = (Brush)FindResource("FreeXBorderStrongBrush");
        SheetNavLeftBtn.Foreground = canScrollLeft ? activeNavigationBrush : inactiveNavigationBrush;
        SheetNavRightBtn.Foreground = canScrollRight ? activeNavigationBrush : inactiveNavigationBrush;
        SheetNavLeftBtn.IsHitTestVisible = canScrollLeft;
        SheetNavRightBtn.IsHitTestVisible = canScrollRight;
    }

    private void UpdateSheetTabViewportWidth()
    {
        if (SheetTabsRowGrid.ActualWidth <= 0)
            return;

        var rowHeaderWidth = SheetGrid.ActualRowHeaderWidth;
        SheetTabsLeadingSpacer.Width = rowHeaderWidth;

        SheetTabsControl.Measure(new Size(double.PositiveInfinity, SheetTabsRowGrid.ActualHeight));
        AddSheetButton.Measure(new Size(double.PositiveInfinity, SheetTabsRowGrid.ActualHeight));
        var tabContentWidth = MeasureSheetTabContentWidth();
        if (tabContentWidth <= 0)
            return;

        var rowWidth = SheetTabsRowGrid.ActualWidth;
        if (WindowState == WindowState.Normal && !double.IsNaN(Width) && Width > 0)
            rowWidth = Math.Min(rowWidth, Width);
        else if (RootGrid.ActualWidth > 0)
            rowWidth = Math.Min(rowWidth, RootGrid.ActualWidth);

        var fixedWidth = rowHeaderWidth;
        var available = Math.Max(
            SheetTabMinimumViewportWidth + SheetTabMinimumHorizontalScrollbarWidth,
            rowWidth - fixedWidth);
        var preferredScrollbarWidth = Math.Clamp(
            available * SheetTabPreferredHorizontalScrollbarRatio,
            SheetTabMinimumHorizontalScrollbarWidth,
            SheetTabPreferredHorizontalScrollbarMaxWidth);
        var tabsBeforeScrollbarShrinks = Math.Max(
            SheetTabMinimumViewportWidth,
            available - preferredScrollbarWidth);
        var tabsAtMinimumScrollbar = Math.Max(
            SheetTabMinimumViewportWidth,
            available - SheetTabMinimumHorizontalScrollbarWidth);
        var targetWidth = Math.Min(tabContentWidth, available);
        var targetScrollbarWidth = tabContentWidth <= tabsBeforeScrollbarShrinks
            ? preferredScrollbarWidth
            : Math.Max(
                SheetTabMinimumHorizontalScrollbarWidth,
                available - Math.Min(tabContentWidth, tabsAtMinimumScrollbar));

        targetWidth = Math.Min(targetWidth, tabsAtMinimumScrollbar);

        var tabsWidthUnchanged = Math.Abs(SheetTabsScroller.Width - targetWidth) <= 0.5;
        var scrollbarWidthUnchanged = Math.Abs(HorizontalScroll.Width - targetScrollbarWidth) <= 0.5;
        if (tabsWidthUnchanged && scrollbarWidthUnchanged)
            return;

        SheetTabsScroller.Width = targetWidth;
        HorizontalScroll.Width = targetScrollbarWidth;
        Dispatcher.BeginInvoke(() =>
        {
            UpdateSheetTabNavigation();
        }, DispatcherPriority.Loaded);
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

        if (measuredBounds.Count > 0)
        {
            var left = measuredBounds.Min(bounds => bounds.Left);
            var right = measuredBounds.Max(bounds => bounds.Right);
            var measuredWidth = Math.Max(0, right - left);
            if (AddSheetButton.ActualWidth <= 0 || AddSheetButton.ActualHeight <= 0)
                measuredWidth += ResolveLayoutWidth(AddSheetButton);

            return measuredWidth;
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
        return measuredWidth;
    }

    private static double EstimateSheetTabWidth(SheetTabViewModel tab)
        => Math.Max(86, 54 + (tab.Name?.Length ?? 0) * 7.5);

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

        SheetTabsChromeLayer.Children.Clear();
        SheetTabsOverlayLayer.Children.Clear();
        var chromeWidth = SheetTabsChromeLayer.ActualWidth;
        var accentBrush = (Brush)FindResource("FreeXAccentBrush");
        var inactiveStrokeBrush = (Brush)FindResource("FreeXBorderStrongBrush");
        var inactiveFillBrush = (Brush)FindResource("FreeXSheetSurfaceBrush");
        var groupedFillBrush = (Brush)FindResource("FreeXAccentSoftBrush");
        var tentativeFillBrush = AddSheetButton.IsPressed
            ? (Brush)FindResource("FreeXAccentPressedBrush")
            : AddSheetButton.IsMouseOver
                ? (Brush)FindResource("FreeXAccentSoftBrush")
                : (Brush)FindResource("FreeXChromeSurfaceBrush");
        var tentativeStrokeBrush = accentBrush;

        Rect? addRect = null;
        if (AddSheetButton.ActualWidth > 0)
        {
            addRect = SheetTabChromeBounds(AddSheetButton, SheetTabOverlapWidth);
        }

        var tabClipGeometry = CreateVisibleSheetTabClipGeometry(addRect);
        var scrollableClipGeometry = CreateScrollableSheetTabClipGeometry();
        if (addRect is { } add && add.Right > -16 && add.Left < SheetTabsChromeLayer.ActualWidth + 16)
        {
            SheetTabsChromeLayer.Children.Add(CreateSheetTabPath(
                CreateSheetTabFillGeometry(add, drawLeft: false),
                tentativeFillBrush,
                null,
                0,
                scrollableClipGeometry,
                1));

            SheetTabsChromeLayer.Children.Add(CreateSheetTabPath(
                CreateSheetTabOutlineGeometry(add, drawLeft: false, drawRight: true),
                null,
                tentativeStrokeBrush,
                1.25,
                scrollableClipGeometry,
                AddSheetButton.IsMouseOver ? 0.95 : 0.82));
        }

        var visibleTabs = _sheetTabs.ToList();
        var activeTabIndex = visibleTabs.FindIndex(tab => tab.Id == _currentSheetId);
        var activeTab = activeTabIndex >= 0 ? visibleTabs[activeTabIndex] : null;
        Rect? activeRect = null;
        if (activeTabIndex >= 0)
            activeRect = ClipSheetTabChromeBoundsToVisibleTabs(
                TryGetSheetTabChromeBounds(visibleTabs[activeTabIndex], chromeWidth),
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
                ClipSheetTabChromeBoundsToVisibleTabs(TryGetSheetTabChromeBounds(tab, chromeWidth), addRect) is not { } tabRect)
                return;

            var isGrouped = tab.IsGrouped;
            var tabFill = tab.TabColor is { } tabColor
                ? CreatePastelTabBrush(tabColor)
                : isGrouped
                ? groupedFillBrush
                : inactiveFillBrush;
            var tabStroke = isGrouped ? accentBrush : inactiveStrokeBrush;
            var tabStrokeOpacity = isGrouped ? 0.78 : 0.92;
            var isRightOfActive = activeTabIndex >= 0 && tabIndex > activeTabIndex;
            var rightNeighborIsInactiveTab = tabIndex < visibleTabs.Count - 1 &&
                                             visibleTabs[tabIndex + 1].Id != _currentSheetId;
            var drawLeft = isRightOfActive ? false : !tab.IsLeftSideCoveredByActive;
            var drawRight = isRightOfActive || (!tab.IsRightSideCoveredByActive && !rightNeighborIsInactiveTab);
            SheetTabsChromeLayer.Children.Add(CreateSheetTabPath(
                CreateSheetTabFillGeometry(tabRect, drawLeft: !isRightOfActive),
                tabFill,
                null,
                0,
                tabClipGeometry,
                1));

            SheetTabsChromeLayer.Children.Add(CreateSheetTabPath(
                CreateSheetTabOutlineGeometry(
                    tabRect,
                    drawLeft: drawLeft,
                    drawRight: drawRight),
                null,
                tabStroke,
                isGrouped ? 1.25 : 1.15,
                tabClipGeometry,
                tabStrokeOpacity));
        }

        if (activeRect is { } active)
        {
            var activeFillBrush = activeTab?.TabColor is { } activeTabColor
                ? CreatePastelTabBrush(activeTabColor)
                : (Brush)FindResource("FreeXRibbonSurfaceBrush");
            SheetTabsChromeLayer.Children.Add(CreateSheetTabPath(
                CreateSheetTabFillGeometry(active, top: -1.0),
                activeFillBrush,
                null,
                0,
                tabClipGeometry,
                1));
            RenderSheetTabsOverlay(
                addRect,
                activeRect,
                accentBrush);
            return;
        }

        RenderSheetTabsOverlay(
            addRect,
            activeRect,
            accentBrush);
    }

    private void RenderSheetTabsOverlay(
        Rect? addRect,
        Rect? activeRect,
        Brush gridRuleBrush)
    {
        var chromeWidth = SheetTabsChromeLayer.ActualWidth;
        var gridRuleGeometry = activeRect is { } active
            ? CreateActiveSheetTabGridRuleGeometry(chromeWidth, active)
            : new LineGeometry(new Point(0, SheetTabGridRuleTop), new Point(chromeWidth, SheetTabGridRuleTop));
        SheetTabsOverlayLayer.Children.Add(CreateSheetTabPath(
            gridRuleGeometry,
            null,
            gridRuleBrush,
            SheetTabGridRuleStrokeThickness,
            null,
            1));
    }

    private Rect SheetTabChromeBounds(FrameworkElement element, double leftOverlap)
    {
        var elementBounds = element.TransformToAncestor(SheetTabsRowGrid)
            .TransformBounds(new Rect(new Point(0, 0), element.RenderSize));
        var layerBounds = SheetTabsChromeLayer.TransformToAncestor(SheetTabsRowGrid)
            .TransformBounds(new Rect(new Point(0, 0), SheetTabsChromeLayer.RenderSize));
        return new Rect(elementBounds.Left - layerBounds.Left - leftOverlap, 0, elementBounds.Width + leftOverlap, 26);
    }

    private Rect? TryGetSheetTabChromeBounds(SheetTabViewModel tab, double chromeWidth)
    {
        if (SheetTabsControl.ItemContainerGenerator.ContainerFromItem(tab) is not FrameworkElement container ||
            container.ActualWidth <= 0)
            return null;

        var tabRect = SheetTabChromeBounds(container, SheetTabOverlapWidth);
        return tabRect.Right < -16 || tabRect.Left > chromeWidth + 16
            ? null
            : tabRect;
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

        var clippedWidth = visibleRight - rect.Left;
        return clippedWidth >= 24
            ? new Rect(rect.Left, rect.Top, clippedWidth, rect.Height)
            : null;
    }

    private Geometry CreateVisibleSheetTabClipGeometry(Rect? addRect)
    {
        var scrollerBounds = SheetTabChromeBounds(SheetTabsScroller, 0);
        var left = Math.Clamp(scrollerBounds.Left, 0, SheetTabsChromeLayer.ActualWidth);
        var right = Math.Clamp(GetVisibleSheetTabsRight(addRect), 0, SheetTabsChromeLayer.ActualWidth);

        var geometry = new RectangleGeometry(new Rect(left, -2, Math.Max(0, right - left), 32));
        geometry.Freeze();
        return geometry;
    }

    private double GetVisibleSheetTabsRight(Rect? addRect)
    {
        var scrollerBounds = SheetTabChromeBounds(SheetTabsScroller, 0);
        var right = scrollerBounds.Right;
        if (addRect is { } add)
            right = Math.Min(right, add.Left + SheetTabOverlapWidth);

        return right;
    }

    private Geometry CreateScrollableSheetTabClipGeometry()
    {
        var scrollerBounds = SheetTabChromeBounds(SheetTabsScroller, 0);
        var left = Math.Clamp(scrollerBounds.Left, 0, SheetTabsChromeLayer.ActualWidth);
        var right = Math.Clamp(scrollerBounds.Right, 0, SheetTabsChromeLayer.ActualWidth);
        var geometry = new RectangleGeometry(new Rect(left, -2, Math.Max(0, right - left), 32));
        geometry.Freeze();
        return geometry;
    }

    private static SolidColorBrush CreatePastelTabBrush(CellColor color)
    {
        const byte baseComponent = 243;
        return new SolidColorBrush(Color.FromRgb(
            BlendColorComponent(baseComponent, color.R, 0.2),
            BlendColorComponent(baseComponent, color.G, 0.2),
            BlendColorComponent(baseComponent, color.B, 0.2)));
    }

    private static byte BlendColorComponent(byte background, byte foreground, double foregroundWeight)
        => (byte)Math.Round(background + (foreground - background) * foregroundWeight);

    private static Geometry CreateActiveSheetTabGridRuleGeometry(double width, Rect tab)
    {
        const double top = SheetTabGridRuleTop;
        const double sideInset = 8.0;
        const double sideBottom = 22.0;
        const double bottomInset = 12.0;
        const double bottom = 26.0;
        var left = Math.Clamp(tab.Left, 0, width);
        var right = Math.Clamp(tab.Right, 0, width);

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(0, top), false, false);
            context.LineTo(new Point(left, top), true, true);
            context.BezierTo(
                new Point(left + sideInset, top),
                new Point(left + sideInset, 8),
                new Point(left + sideInset, 8),
                true,
                true);
            context.LineTo(new Point(left + sideInset, sideBottom), true, true);
            context.BezierTo(
                new Point(left + sideInset, 24),
                new Point(left + bottomInset - 2, bottom),
                new Point(left + bottomInset, bottom),
                true,
                true);
            context.LineTo(new Point(right - bottomInset, bottom), true, true);
            context.BezierTo(
                new Point(right - bottomInset + 2, bottom),
                new Point(right - sideInset, 24),
                new Point(right - sideInset, sideBottom),
                true,
                true);
            context.LineTo(new Point(right - sideInset, 8), true, true);
            context.BezierTo(
                new Point(right - sideInset, 8),
                new Point(right - sideInset, top),
                new Point(right, top),
                true,
                true);
            context.LineTo(new Point(width, top), true, true);
        }

        geometry.Freeze();
        return geometry;
    }

    private static Geometry CreateSheetTabFillGeometry(Rect tab, double top = 0.5, bool drawLeft = true)
    {
        const double sideInset = 8.0;
        const double sideBottom = 22.0;
        const double bottomInset = 12.0;
        const double bottom = 26.0;
        var left = tab.Left;
        var right = tab.Right;

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(left, top), true, true);
            if (drawLeft)
            {
                context.BezierTo(new Point(left + sideInset, top), new Point(left + sideInset, 8), new Point(left + sideInset, 8), true, true);
                context.LineTo(new Point(left + sideInset, sideBottom), true, true);
                context.BezierTo(new Point(left + sideInset, 24), new Point(left + bottomInset - 2, bottom), new Point(left + bottomInset, bottom), true, true);
            }
            else
            {
                context.LineTo(new Point(left, bottom), true, true);
            }

            context.LineTo(new Point(right - bottomInset, bottom), true, true);
            context.BezierTo(new Point(right - bottomInset + 2, bottom), new Point(right - sideInset, 24), new Point(right - sideInset, sideBottom), true, true);
            context.LineTo(new Point(right - sideInset, 8), true, true);
            context.BezierTo(new Point(right - sideInset, 8), new Point(right - sideInset, top), new Point(right, top), true, true);
            context.LineTo(new Point(left, top), true, true);
        }

        geometry.Freeze();
        return geometry;
    }

    private static Geometry CreateSheetTabOutlineGeometry(Rect tab, bool drawLeft, bool drawRight)
    {
        const double top = 0.5;
        const double sideInset = 8.0;
        const double sideBottom = 22.0;
        const double bottomInset = 12.0;
        const double bottom = 26.0;
        var left = tab.Left;
        var right = tab.Right;
        var bottomStart = drawLeft ? left + bottomInset : left;
        var bottomEnd = drawRight ? right - bottomInset : right;

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            if (drawLeft)
            {
                context.BeginFigure(new Point(left, top), false, false);
                context.BezierTo(new Point(left + sideInset, top), new Point(left + sideInset, 8), new Point(left + sideInset, 8), true, true);
                context.LineTo(new Point(left + sideInset, sideBottom), true, true);
                context.BezierTo(new Point(left + sideInset, 24), new Point(left + bottomInset - 2, bottom), new Point(bottomStart, bottom), true, true);
            }
            else
            {
                context.BeginFigure(new Point(bottomStart, bottom), false, false);
            }

            context.LineTo(new Point(bottomEnd, bottom), true, true);

            if (drawRight)
            {
                context.BezierTo(new Point(right - bottomInset + 2, bottom), new Point(right - sideInset, 24), new Point(right - sideInset, sideBottom), true, true);
                context.LineTo(new Point(right - sideInset, 8), true, true);
                context.BezierTo(new Point(right - sideInset, 8), new Point(right - sideInset, top), new Point(right, top), true, true);
            }
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
        var activeIndex = visibleTabs.FindIndex(tab => tab.Id == _currentSheetId);
        if (activeIndex < 0)
            return;

        var activeTab = visibleTabs[activeIndex];
        if (activeTab is null ||
            SheetTabsControl.ItemContainerGenerator.ContainerFromItem(activeTab) is not FrameworkElement container)
            return;

        var bounds = container.TransformToAncestor(SheetTabsScroller)
            .TransformBounds(new Rect(new Point(0, 0), container.RenderSize));
        var visibleViewportRight = Math.Max(0, SheetTabsScroller.ViewportWidth);

        var currentOffset = SheetTabsScroller.HorizontalOffset;
        var activeContentLeft = currentOffset + bounds.Left;
        var activeContentRight = currentOffset + bounds.Right;
        var contextTabsBeforeActive = activeTab.Name.Length >= 7 ? 1 : 2;
        var anchorIndex = Math.Max(0, activeIndex - contextTabsBeforeActive);
        var targetOffset = activeContentLeft;

        if (SheetTabsControl.ItemContainerGenerator.ContainerFromItem(visibleTabs[anchorIndex]) is FrameworkElement anchor)
        {
            var anchorBounds = anchor.TransformToAncestor(SheetTabsScroller)
                .TransformBounds(new Rect(new Point(0, 0), anchor.RenderSize));
            targetOffset = currentOffset + anchorBounds.Left;
        }

        if (activeContentRight - targetOffset > visibleViewportRight)
            targetOffset = activeContentRight - visibleViewportRight;
        if (activeContentLeft - targetOffset < 0)
            targetOffset = activeContentLeft;

        targetOffset = Math.Clamp(targetOffset, 0, SheetTabsScroller.ScrollableWidth);
        if (Math.Abs(targetOffset - currentOffset) > SheetTabScrollEpsilon)
            SheetTabsScroller.ScrollToHorizontalOffset(targetOffset);
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
            var tabId = tab.Id;
            SelectSheetTabForKeyboardContextMenu(tabId);
            var refreshedTab = _sheetTabs.FirstOrDefault(item => item.Id == tabId);
            target = refreshedTab is null ? target : FindSheetTabContextMenuTarget(refreshedTab) ?? target;
            contextMenu = target.ContextMenu;
            if (contextMenu is null)
                return false;
        }

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

        var firstEnabledItem = contextMenu.Items.OfType<MenuItem>().FirstOrDefault(item => item.IsEnabled);
        if (firstEnabledItem is null)
            return;

        firstEnabledItem.Focus();
        Keyboard.Focus(firstEnabledItem);
    }

    private bool TryHandleFocusedSheetTabKeyboardNavigation(System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None ||
            Keyboard.FocusedElement is not DependencyObject focusedElement ||
            (!ReferenceEquals(focusedElement, SheetTabsScroller) && !IsDescendantOf(focusedElement, SheetTabsScroller)))
        {
            return false;
        }

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
        var visibleTabs = _sheetTabs.ToList();
        var nextSheetId = SheetTabFocusPlanner.AdjacentTab(visibleTabs, _currentSheetId, direction);
        if (nextSheetId is null)
            return false;

        FocusSheetTab(nextSheetId.Value);
        return true;
    }

    private bool FocusEdgeVisibleSheetTab(bool first)
    {
        var sheetId = SheetTabFocusPlanner.EdgeTab(_sheetTabs.ToList(), first);
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
            var outcome = _commandBus.Execute(_workbook.Id, new RenameSheetCommand(sheetId, name));
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
            _messageService.ShowWarning(
                UiText.Get("MainWindowMessage_DeleteOnlyVisibleSheet"),
                UiText.Get("MainWindowMessage_DeleteSheetTitle"));
            return;
        }
        var tab = GetContextMenuTab(sender);
        if (tab == null) return;
        if (!_messageService.AskYesNo(
                UiText.Format("MainWindowMessage_DeleteSheetPrompt", tab.Name),
                UiText.Get("MainWindowMessage_DeleteSheetTitle"))) return;
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
        HideSheet(tab.Id);
    }

    private void SheetCtxUnhide_Click(object sender, RoutedEventArgs e)
    {
        UnhideSheet();
    }

    private void HideCurrentSheet()
    {
        HideSheet(_currentSheetId);
    }

    private void HideSheet(SheetId sheetId)
    {
        if (!TryExecuteCommand(new SetSheetHiddenCommand(sheetId, hidden: true), "Hide Sheet"))
            return;

        if (_currentSheetId == sheetId)
            _currentSheetId = _workbook.Sheets.First(s => !s.IsHidden).Id;
        _groupedSheetIds.Remove(sheetId);
        if (_groupedSheetIds.Count == 0)
            _groupedSheetIds.Add(_currentSheetId);
        _sheetGroupAnchor = _currentSheetId;
        UpdateViewport();
        RefreshSheetTabs();
    }

    private void UnhideSheet()
    {
        var hiddenSheets = _workbook.Sheets.Where(s => s.IsHidden).ToList();
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

        var sheet = hiddenSheets.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
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
        ColorSheetTab(tab.Id);
    }

    private void ColorCurrentSheetTab()
    {
        ColorSheetTab(_currentSheetId);
    }

    private void ColorSheetTab(SheetId sheetId)
    {
        var sheet = _workbook.GetSheet(sheetId);
        if (!TryShowColorPicker("Tab Color", sheet?.TabColor ?? new CellColor(15, 109, 140), allowNoColor: true, out var tabColor))
            return;

        if (!TryExecuteCommand(new SetSheetTabColorCommand(sheetId, tabColor), "Tab Color"))
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
