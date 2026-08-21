using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using FreeX.App.Presentation.SheetUI;
using FreeX.App.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

public sealed class MainWindowSheetTabKeyboardTests
{
    [Fact]
    public void MenuKeyOnFocusedSheetTab_OpensSheetTabContextMenuWithFocusAndAccessKeys()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.FocusCurrentSheetTab().Should().BeTrue();
            harness.FocusedSheetTabName.Should().Be("Sheet1");

            harness.OpenFocusedSheetTabContextMenu();

            harness.SheetTabContextMenuIsOpen.Should().BeTrue(harness.DebugSheetTabs);
            harness.SheetTabContextMenuPlacementTargetIsFocusedTab.Should().BeTrue();
            harness.SheetTabMenuItemGestureText(UiText.Get("MainWindow_Header_InsertSheet")).Should().Be("I");
            harness.SheetTabMenuItemGestureText(UiText.Get("MainWindow_Header_DeleteSheet")).Should().Be("E");
            harness.SheetTabMenuItemGestureText(UiText.Get("MainWindow_Header_Rename")).Should().Be("R");
            harness.SheetTabMenuItemGestureText(UiText.Get("MainWindow_Header_MoveOrCopy")).Should().Be("M");
            harness.SheetTabMenuItemGestureText(UiText.Get("MainWindow_Header_ViewCode")).Should().Be("V");
            harness.SheetTabMenuItemGestureText(UiText.Get("MainWindow_Header_ProtectSheet")).Should().Be("P");
            harness.SheetTabMenuItemGestureText(UiText.Get("MainWindow_Header_TabColor")).Should().Be("T");
        });
    }

    [Fact]
    public void SheetTabTabColorMenu_ExposesImmediateThemeAndStandardSwatches()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.FocusCurrentSheetTab().Should().BeTrue();
            harness.OpenFocusedSheetTabContextMenu();

            var palette = harness.SheetTabColorPalette;
            palette.HasNoColor.Should().BeTrue();
            palette.ThemeSwatchCount.Should().Be(
                CellColorPalettePlanner.BuildThemePalette().Sum(column => column.Shades.Count));
            palette.StandardSwatchCount.Should().Be(CellColorPalettePlanner.BuildStandardSwatches().Count);
            palette.HasMoreColors.Should().BeTrue();

            harness.ApplyFirstStandardSheetTabColor();
            harness.ActiveSheetTabColor.Should().Be(CellColorPalettePlanner.BuildStandardSwatches()[0].Color);
        });
    }

    [Fact]
    public void MenuKeyOnInactiveFocusedSheetTab_SelectsTabBeforeWorksheetFallback()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.InsertNewSheet();
            harness.ActiveSheetTabName.Should().Be("Sheet2");

            harness.FocusSheetTab("Sheet1").Should().BeTrue();
            harness.FocusedSheetTabName.Should().Be("Sheet1");

            harness.RouteFocusedSheetTabContextMenu().Should().BeTrue();

            harness.ActiveSheetTabName.Should().Be("Sheet1");
            harness.GroupedSheetTabNames.Should().Equal("Sheet1");
        });
    }

    [Fact]
    public void AddSheetButton_LivesInScrollableTabStripAndExposesKeyboardAutomation()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            var window = harness.Window;

            window.UpdateLayout();
            PumpDispatcher();
            window.UpdateLayout();

            var scroller = (FrameworkElement)window.FindName("SheetTabsScroller");
            var scrollableContent = (FrameworkElement)window.FindName("SheetTabsScrollableContent");
            var addSheet = (FrameworkElement)window.FindName("AddSheetButton");

            var scrollerBounds = BoundsRelativeToWindow(scroller, window);
            var addBounds = BoundsRelativeToWindow(addSheet, window);

            addSheet.Parent.Should().BeSameAs(scrollableContent, "the add-sheet button should scroll and clip with the sheet tabs instead of reserving fixed space");
            addSheet.Focusable.Should().BeTrue();
            AutomationProperties.GetName(addSheet).Should().Be(UiText.Get("MainWindow_AutomationName_InsertSheet"));
            AutomationProperties.GetHelpText(addSheet).Should().Be(UiText.Get("MainWindow_AutomationHelpText_AddANewSheetToTheWorkbook"));
            addBounds.Left.Should().BeGreaterThanOrEqualTo(scrollerBounds.Left);
            addBounds.Right.Should().BeLessThanOrEqualTo(scrollerBounds.Right + 0.5, "the add-sheet button should be inside the visible tab viewport when the single-sheet strip fits");
            addSheet.ActualWidth.Should().BeGreaterThan(34);
            addSheet.ActualHeight.Should().BeGreaterThan(20);
        });
    }

    [Fact]
    public void AddSheetButton_StaysVisibleBeforeRightNavigationAfterTrailingInsert()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create(sheetCount: 20, width: 760);
            var window = harness.Window;

            harness.UpdateSheetTabNavigation(iterations: 4);
            window.UpdateLayout();
            PumpDispatcher();
            harness.UpdateSheetTabNavigation(iterations: 4);

            var addSheet = (FrameworkElement)window.FindName("AddSheetButton");
            var rightNav = (FrameworkElement)window.FindName("SheetNavRightBtn");
            var scroller = (FrameworkElement)window.FindName("SheetTabsScroller");

            var addBounds = BoundsRelativeToWindow(addSheet, window);
            var rightNavBounds = BoundsRelativeToWindow(rightNav, window);
            var scrollerBounds = BoundsRelativeToWindow(scroller, window);

            rightNav.Visibility.Should().Be(Visibility.Visible);
            addBounds.Left.Should().BeGreaterThanOrEqualTo(scrollerBounds.Left - 0.5);
            addBounds.Right.Should().BeLessThanOrEqualTo(rightNavBounds.Left + 0.5, "the new-sheet button should scroll into the visible lane before the right navigation arrow");
            addSheet.IsHitTestVisible.Should().BeTrue("the visible new-sheet button must remain clickable");
            addSheet.Focusable.Should().BeTrue("the visible new-sheet button must remain keyboard reachable");
        });
    }

    [Fact]
    public void AddSheetButton_DisablesInputWhenScrolledOutOfVisibleTabViewport()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create(sheetCount: 20, width: 760);
            var window = harness.Window;

            var scroller = (ScrollViewer)window.FindName("SheetTabsScroller");
            scroller.ScrollToHorizontalOffset(0);
            window.UpdateLayout();
            PumpDispatcher();
            harness.UpdateSheetTabNavigation(iterations: 4);

            var addSheet = (FrameworkElement)window.FindName("AddSheetButton");

            addSheet.IsHitTestVisible.Should().BeFalse("the hidden new-sheet button should not receive pointer clicks outside the visible tab viewport");
            addSheet.Focusable.Should().BeFalse("the hidden new-sheet button should not receive keyboard focus");
        });
    }

    [Fact]
    public void SheetTabViewport_KeepsRuleFlushWithGridEdge()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create(sheetCount: 6);
            var window = harness.Window;

            window.UpdateLayout();
            PumpDispatcher();
            window.UpdateLayout();

            var row = (FrameworkElement)window.FindName("SheetTabsRowGrid");
            var scroller = (FrameworkElement)window.FindName("SheetTabsScroller");
            var chromeLayer = (FrameworkElement)window.FindName("SheetTabsChromeLayer");
            var overlayLayer = (FrameworkElement)window.FindName("SheetTabsOverlayLayer");

            harness.FocusCurrentSheetTab().Should().BeTrue();
            window.UpdateLayout();
            PumpDispatcher();
            window.UpdateLayout();

            var focusedTab = Keyboard.FocusedElement.Should().BeAssignableTo<FrameworkElement>().Subject;
            var rowBounds = BoundsRelativeToWindow(row, window);
            var scrollerBounds = BoundsRelativeToWindow(scroller, window);
            var chromeBounds = BoundsRelativeToWindow(chromeLayer, window);
            var overlayBounds = BoundsRelativeToWindow(overlayLayer, window);
            var focusedTabBounds = BoundsRelativeToWindow(focusedTab, window);

            scrollerBounds.Top.Should().BeLessThan(rowBounds.Top + 3.0, "the sheet-tab viewport should leave only the layout rounding needed for the tab chrome");
            chromeBounds.Top.Should().BeApproximately(rowBounds.Top, 0.5, "the drawn tab chrome layer should start at the grid edge so the blue rule is visually flush");
            overlayBounds.Top.Should().BeApproximately(chromeBounds.Top, 0.25);
            scroller.ActualHeight.Should().BeApproximately(28, 0.5);
            rowBounds.Height.Should().BeApproximately(scroller.ActualHeight, 0.5, "the sheet-tab row should not leave a shelf below the tab chrome");
            focusedTabBounds.Top.Should().BeGreaterThanOrEqualTo(scrollerBounds.Top - 0.5);
            focusedTabBounds.Bottom.Should().BeLessThanOrEqualTo(scrollerBounds.Bottom + 0.5, "the focused sheet tab chrome must fit inside the clipped viewport");
            chromeBounds.Bottom.Should().BeLessThanOrEqualTo(rowBounds.Bottom + 0.5);
            overlayBounds.Bottom.Should().BeLessThanOrEqualTo(rowBounds.Bottom + 0.5);
        });
    }

    [Fact]
    public void SheetTabNavigationButtons_StayBelowGridRule()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create(sheetCount: 20, width: 520);
            var window = harness.Window;

            harness.UpdateSheetTabNavigation();
            window.UpdateLayout();
            PumpDispatcher();
            window.UpdateLayout();
            PumpDispatcher();
            harness.UpdateSheetTabNavigation();
            window.UpdateLayout();

            var row = (FrameworkElement)window.FindName("SheetTabsRowGrid");
            var leadingSpacer = (FrameworkElement)window.FindName("SheetTabsLeadingSpacer");
            var scroller = (ScrollViewer)window.FindName("SheetTabsScroller");
            var overlayLayer = (FrameworkElement)window.FindName("SheetTabsOverlayLayer");
            var horizontalScroll = (ScrollBar)window.FindName("HorizontalScroll");
            var visibleNavButtons = new[]
                {
                    (FrameworkElement)window.FindName("SheetNavLeftBtn"),
                    (FrameworkElement)window.FindName("SheetNavRightBtn")
                }
                .Where(button => button.Visibility == Visibility.Visible)
                .ToList();

            var rowBounds = BoundsRelativeToWindow(row, window);
            var leadingSpacerBounds = BoundsRelativeToWindow(leadingSpacer, window);
            var overlayBounds = BoundsRelativeToWindow(overlayLayer, window);
            var leftNavBounds = BoundsRelativeToWindow((FrameworkElement)window.FindName("SheetNavLeftBtn"), window);
            var rightNavBounds = BoundsRelativeToWindow((FrameworkElement)window.FindName("SheetNavRightBtn"), window);
            var horizontalScrollArrow = WpfTestTree.FindVisualDescendants<RepeatButton>(horizontalScroll)
                .Where(button => button.ActualWidth > 0 && button.ActualHeight > 0)
                .OrderBy(button => BoundsRelativeToWindow(button, window).Left)
                .FirstOrDefault();
            overlayBounds.Top.Should().BeApproximately(rowBounds.Top, 0.5, "the blue sheet-tab rule layer should start at the grid edge without extra vertical space");
            horizontalScrollArrow.Should().NotBeNull("the worksheet horizontal scrollbar should expose a left arrow for alignment");
            visibleNavButtons.Should().HaveCount(
                2,
                "the narrow many-sheet viewport must expose both navigation arrows so their rule clearance is covered; rowWidth={0:F1}, scrollerWidth={1:F1}, viewportWidth={2:F1}, extentWidth={3:F1}, scrollableWidth={4:F1}",
                rowBounds.Width,
                scroller.ActualWidth,
                scroller.ViewportWidth,
                scroller.ExtentWidth,
                scroller.ScrollableWidth);

            foreach (var button in visibleNavButtons)
            {
                var buttonBounds = BoundsRelativeToWindow(button, window);
                var buttonTopOffset = buttonBounds.Top - overlayBounds.Top;
                buttonTopOffset.Should().BeInRange(
                    0.75,
                    2.25,
                    "sheet-tab nav buttons should start below the internal blue rule while allowing layout rounding in the compact strip");
                buttonBounds.Bottom.Should().BeLessThanOrEqualTo(rowBounds.Bottom + 0.5, "sheet-tab nav buttons should stay inside the sheet-tab row");
            }

            HorizontalCenter(leftNavBounds)
                .Should()
                .BeApproximately(HorizontalCenter(leadingSpacerBounds), 0.75, "the sheet-tab left arrow should be centered under the row headers");

            var horizontalScrollArrowBounds = BoundsRelativeToWindow(horizontalScrollArrow!, window);
            VerticalCenter(rightNavBounds)
                .Should()
                .BeApproximately(VerticalCenter(horizontalScrollArrowBounds), 0.75, "the sheet-tab right arrow should align with the worksheet scrollbar left arrow");

            CaptureSheetTabLowerBandIfRequested(window, row);
        });
    }

    [Fact]
    public void SheetTabActiveAndAddTab_StayBelowGridRule()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create(sheetCount: 1);
            var window = harness.Window;

            harness.FocusCurrentSheetTab().Should().BeTrue();
            window.UpdateLayout();
            PumpDispatcher();
            window.UpdateLayout();

            var row = (FrameworkElement)window.FindName("SheetTabsRowGrid");
            var scroller = (FrameworkElement)window.FindName("SheetTabsScroller");
            var overlayLayer = (FrameworkElement)window.FindName("SheetTabsOverlayLayer");
            var addSheet = (FrameworkElement)window.FindName("AddSheetButton");
            var leftNav = (FrameworkElement)window.FindName("SheetNavLeftBtn");
            var rightNav = (FrameworkElement)window.FindName("SheetNavRightBtn");
            var focusedTab = Keyboard.FocusedElement.Should().BeAssignableTo<FrameworkElement>().Subject;

            var rowBounds = BoundsRelativeToWindow(row, window);
            var scrollerBounds = BoundsRelativeToWindow(scroller, window);
            var overlayBounds = BoundsRelativeToWindow(overlayLayer, window);
            var addBounds = BoundsRelativeToWindow(addSheet, window);
            var focusedTabBounds = BoundsRelativeToWindow(focusedTab, window);

            overlayBounds.Top.Should().BeApproximately(rowBounds.Top, 0.5, "the sheet-tab rule layer should be flush with the grid edge");
            focusedTabBounds.Top.Should().BeGreaterThanOrEqualTo(scrollerBounds.Top - 0.5);
            focusedTabBounds.Bottom.Should().BeLessThanOrEqualTo(scrollerBounds.Bottom + 0.5);
            addBounds.Top.Should().BeGreaterThanOrEqualTo(scrollerBounds.Top - 0.5);
            addBounds.Bottom.Should().BeLessThanOrEqualTo(scrollerBounds.Bottom + 0.5, "the add-sheet tab must fit inside the unclipped tab viewport");
            addBounds.Left.Should().BeGreaterThanOrEqualTo(scrollerBounds.Left - 0.5);
            addBounds.Right.Should().BeLessThanOrEqualTo(scrollerBounds.Right + 0.5, "the add-sheet tab plus sign must be visible in the single-sheet viewport");
            leftNav.Visibility.Should().Be(Visibility.Hidden, "the disabled sheet-tab left arrow should not be shown when a single sheet plus the add tab fits");
            rightNav.Visibility.Should().Be(Visibility.Hidden, "the disabled sheet-tab right arrow should not be shown when a single sheet plus the add tab fits");

            CaptureSheetTabLowerBandIfRequested(window, row, "FREEX_SHEET_TAB_SINGLE_CAPTURE");
        });
    }

    [Fact]
    public void ArrowKeyOnAddSheetButton_DoesNotRouteAsFocusedSheetTabNavigation()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.FocusAddSheetButton().Should().BeTrue();
            harness.ActiveSheetTabName.Should().Be("Sheet1");

            harness.HandleFocusedSheetTabKeyboardNavigation(Key.Right).Should().BeFalse();

            harness.ActiveSheetTabName.Should().Be("Sheet1");
            harness.AddSheetButtonHasKeyboardFocus.Should().BeTrue();
        });
    }

    [Fact]
    public void ArrowKeyOnFocusedSheetTab_RoutesAsSheetTabNavigation()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.FocusCurrentSheetTab().Should().BeTrue();
            harness.FocusedSheetTabName.Should().Be("Sheet1");

            harness.HandleFocusedSheetTabKeyboardNavigation(Key.Left).Should().BeTrue();

            harness.ActiveSheetTabName.Should().Be("Sheet1");
            harness.FocusedSheetTabName.Should().Be("Sheet1");
        });
    }

    [Fact]
    public void HomeEndKeysOnFocusedSheetTab_RouteToEdgeSheetTabs()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.InsertNewSheet();
            harness.InsertNewSheet();
            harness.ActiveSheetTabName.Should().Be("Sheet3");

            harness.FocusCurrentSheetTab().Should().BeTrue();
            harness.FocusedSheetTabName.Should().Be("Sheet3");

            harness.HandleFocusedSheetTabKeyboardNavigation(Key.Home).Should().BeTrue();

            harness.ActiveSheetTabName.Should().Be("Sheet1");
            harness.FocusedSheetTabName.Should().Be("Sheet1");

            harness.HandleFocusedSheetTabKeyboardNavigation(Key.End).Should().BeTrue();

            harness.ActiveSheetTabName.Should().Be("Sheet3");
            harness.FocusedSheetTabName.Should().Be("Sheet3");
        });
    }

    [Theory]
    [InlineData(Key.Enter)]
    [InlineData(Key.Escape)]
    [InlineData(Key.Tab)]
    public void NonNavigationKeyOnFocusedSheetTab_DoesNotRouteAsSheetTabNavigation(Key key)
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.FocusCurrentSheetTab().Should().BeTrue();
            harness.FocusedSheetTabName.Should().Be("Sheet1");

            harness.HandleFocusedSheetTabKeyboardNavigation(key).Should().BeFalse();

            harness.ActiveSheetTabName.Should().Be("Sheet1");
            harness.FocusedSheetTabName.Should().Be("Sheet1");
        });
    }

    [Fact]
    public void RightClickSheetTab_ClearsPreviousGroupedHighlight()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.InsertNewSheet();
            harness.ActiveSheetTabName.Should().Be("Sheet2");
            harness.GroupedSheetTabNames.Should().Equal("Sheet2");

            harness.RightClickSheetTab("Sheet1");

            harness.ActiveSheetTabName.Should().Be("Sheet1");
            harness.GroupedSheetTabNames.Should().Equal("Sheet1");
        });
    }

    [Fact]
    public void ProtectedSheetTab_ShowsPadlockAndUpdatesAutomationName()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SheetTabLockIsVisible("Sheet1").Should().BeFalse();
            harness.SheetTabAutomationName("Sheet1").Should().Be("Sheet1");

            harness.SetSheetProtected("Sheet1", isProtected: true);

            harness.SheetTabLockIsVisible("Sheet1").Should().BeTrue();
            harness.SheetTabAutomationName("Sheet1").Should().Be("Sheet1 (protected sheet)");

            harness.SetSheetProtected("Sheet1", isProtected: false);

            harness.SheetTabLockIsVisible("Sheet1").Should().BeFalse();
            harness.SheetTabAutomationName("Sheet1").Should().Be("Sheet1");
        });
    }

    [Fact]
    public void RightClickGroupedSheetTab_PreservesGroupForUngroupMenuCommand()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create(sheetCount: 3);

            harness.SelectAllSheetsFromContextMenu();
            harness.GroupedSheetTabNames.Should().BeEquivalentTo("Sheet1", "Sheet2", "Sheet3");

            harness.RightClickSheetTab("Sheet2");

            harness.ActiveSheetTabName.Should().Be("Sheet2");
            harness.GroupedSheetTabNames.Should().BeEquivalentTo("Sheet1", "Sheet2", "Sheet3");
        });
    }

    [Fact]
    public void SheetTabMouseMove_CancelsStaleDragWhenLeftButtonIsReleased()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.SheetTabs.cs");
        var mouseMove = source[
            source.IndexOf("private void SheetTab_MouseMove", StringComparison.Ordinal)..
            source.IndexOf("private void SheetTab_MouseLeftButtonUp", StringComparison.Ordinal)];

        mouseMove.Should().Contain("if (_dragSheetTabId is not { } draggedId)");
        mouseMove.Should().Contain("if (e.LeftButton != MouseButtonState.Pressed)");
        mouseMove.Should().Contain("ClearSheetTabDragState();");
        mouseMove.IndexOf("ClearSheetTabDragState();", StringComparison.Ordinal)
            .Should()
            .BeLessThan(mouseMove.IndexOf("var current = e.GetPosition(SheetTabsControl);", StringComparison.Ordinal));
    }

    [Fact]
    public void SheetTabDrag_CapturesMouseAndClearsStateOnReleaseOrLostCapture()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.SheetTabs.cs");
        var xaml = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml");

        var mouseDown = source[
            source.IndexOf("private void SheetTab_MouseLeftButtonDown", StringComparison.Ordinal)..
            source.IndexOf("private void SheetTab_MouseMove", StringComparison.Ordinal)];
        var captureHelper = source[
            source.IndexOf("private void CaptureSheetTabMouseForDrag", StringComparison.Ordinal)..
            source.IndexOf("private SheetTabDragTarget? FindSheetTabDragTarget", StringComparison.Ordinal)];
        var mouseMove = source[
            source.IndexOf("private void SheetTab_MouseMove", StringComparison.Ordinal)..
            source.IndexOf("private void SheetTab_MouseLeftButtonUp", StringComparison.Ordinal)];
        var mouseUpAndLostCapture = source[
            source.IndexOf("private void SheetTab_MouseLeftButtonUp", StringComparison.Ordinal)..
            source.IndexOf("private void SheetTab_MouseRightButtonDown", StringComparison.Ordinal)];

        xaml.Should().Contain("LostMouseCapture=\"SheetTab_LostMouseCapture\"");
        mouseDown.Should().Contain("CaptureSheetTabMouseForDrag(tab.Id, sender);");
        mouseDown.IndexOf("RefreshSheetTabs();", StringComparison.Ordinal)
            .Should()
            .BeLessThan(mouseDown.IndexOf("_dragSheetTabId = tab.Id;", StringComparison.Ordinal));
        mouseDown.IndexOf("_dragSheetTabStart = dragStart;", StringComparison.Ordinal)
            .Should()
            .BeLessThan(mouseDown.IndexOf("CaptureSheetTabMouseForDrag(tab.Id, sender);", StringComparison.Ordinal));
        captureHelper.Should().Contain("FindSheetTabContextMenuTarget(refreshedTab)");
        captureHelper.Should().Contain("refreshedElement.CaptureMouse();");
        mouseMove.Should().Contain("element.ReleaseMouseCapture();");
        mouseMove.IndexOf("element.ReleaseMouseCapture();", StringComparison.Ordinal)
            .Should()
            .BeLessThan(mouseMove.IndexOf("var current = e.GetPosition(SheetTabsControl);", StringComparison.Ordinal));
        mouseUpAndLostCapture.Should().Contain("element.ReleaseMouseCapture();");
        mouseUpAndLostCapture.Should().Contain("private void SheetTab_LostMouseCapture");
        mouseUpAndLostCapture.Should().Contain("_dragSheetTabId = null;");
    }

    [Fact]
    public void SheetTabLabelDoubleClick_RenamesOnlyForLeftButton()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.SheetTabs.cs");
        var labelMouseDown = source[
            source.IndexOf("private void SheetTab_LabelMouseDown", StringComparison.Ordinal)..
            source.IndexOf("private void RenameSheetFromTab", StringComparison.Ordinal)];

        labelMouseDown.Should().Contain("e.ChangedButton != MouseButton.Left");
        labelMouseDown.Should().Contain("e.ClickCount != 2");
        labelMouseDown.Should().Contain("RenameSheetFromTab(tab);");
        labelMouseDown.Should().Contain("e.Handled = true;");
        labelMouseDown.IndexOf("e.ChangedButton != MouseButton.Left", StringComparison.Ordinal)
            .Should()
            .BeLessThan(labelMouseDown.IndexOf("RenameSheetFromTab(tab);", StringComparison.Ordinal));
        labelMouseDown.IndexOf("RenameSheetFromTab(tab);", StringComparison.Ordinal)
            .Should()
            .BeLessThan(labelMouseDown.IndexOf("e.Handled = true;", StringComparison.Ordinal));
    }

    [Fact]
    public void SheetTabPointerMechanics_WireRenameDragGroupingOverflowAndContextMenuRoutes()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.SheetTabs.cs");
        var xaml = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml");

        var mouseDown = Slice(source, "private void SheetTab_MouseLeftButtonDown", "private void SheetTab_MouseMove");
        var mouseMove = Slice(source, "private void SheetTab_MouseMove", "private void SheetTab_MouseLeftButtonUp");
        var groupClick = Slice(source, "private void UpdateGroupedSheetsForClick", "private void SheetNavLeftBtn_Click");
        var leftNav = Slice(source, "private void SheetNavLeftBtn_Click", "private void SheetNavRightBtn_Click");
        var rightNav = Slice(source, "private void SheetNavRightBtn_Click", "private void SheetNavButton_MouseRightButtonDown");
        var navRightClick = Slice(source, "private void SheetNavButton_MouseRightButtonDown", "    //");

        xaml.Should().Contain("MouseLeftButtonDown=\"SheetTab_MouseLeftButtonDown\"");
        xaml.Should().Contain("MouseMove=\"SheetTab_MouseMove\"");
        xaml.Should().Contain("MouseLeftButtonUp=\"SheetTab_MouseLeftButtonUp\"");
        xaml.Should().Contain("LostMouseCapture=\"SheetTab_LostMouseCapture\"");
        xaml.Should().Contain("MouseRightButtonDown=\"SheetTab_MouseRightButtonDown\"");
        xaml.Should().Contain("MouseDown=\"SheetTab_LabelMouseDown\"");
        xaml.Should().Contain("Click=\"SheetNavLeftBtn_Click\"");
        xaml.Should().Contain("Click=\"SheetNavRightBtn_Click\"");
        xaml.Should().Contain("Loaded=\"SheetNavButton_Loaded\"");
        xaml.Should().Contain("PreviewMouseDown=\"SheetNavButton_MouseRightButtonDown\"");
        xaml.Should().Contain("PreviewMouseRightButtonDown=\"SheetNavButton_MouseRightButtonDown\"");
        xaml.Should().Contain("MouseRightButtonDown=\"SheetNavButton_MouseRightButtonDown\"");
        xaml.Should().Contain("PreviewMouseRightButtonUp=\"SheetNavButton_MouseRightButtonUp\"");
        xaml.Should().Contain("MouseRightButtonUp=\"SheetNavButton_MouseRightButtonUp\"");
        xaml.Should().Contain("ContextMenuOpening=\"SheetNavButton_ContextMenuOpening\"");

        mouseDown.Should().Contain("_dragSheetTabId = tab.Id;");
        mouseDown.Should().Contain("var dragStart = e.GetPosition(SheetTabsControl);");
        mouseDown.Should().Contain("_dragSheetTabStart = dragStart;");
        mouseDown.Should().Contain("_dragSheetTabPendingToIndex = null;");
        mouseDown.Should().Contain("CaptureSheetTabMouseForDrag(tab.Id, sender);");
        mouseDown.Should().Contain("UpdateGroupedSheetsForClick(tab.Id);");

        mouseMove.Should().Contain("SystemParameters.MinimumHorizontalDragDistance");
        mouseMove.Should().Contain("FindSheetTabDragTarget(current, draggedId, e.OriginalSource as System.Windows.DependencyObject)");
        mouseMove.Should().Contain("SheetTabPointerPlanner.CalculateDropIndex(fromIndex, targetIndex, insertAfterTarget)");
        source.Should().NotContain("CalculateSheetTabDragToIndex");
        mouseMove.Should().Contain("_dragSheetTabPendingToIndex = toIndex;");
        mouseMove.Should().NotContain("new MoveSheetCommand(fromIndex, toIndex)");
        source.Should().Contain("SheetTabsControl.InputHitTest(position)");
        source.Should().Contain("FindSheetTabDragTargetByBounds(position, draggedId)");
        source.Should().Contain("private void CommitPendingSheetTabDragDrop()");
        source.Should().Contain("_session.SelectSheetFromTab(draggedId, selectRange: false, toggle: false)");
        source.Should().Contain("_session.MoveActiveSheetTo(toIndex)");
        source.Should().NotContain("new MoveSheetCommand(fromIndex, toIndex)");
        source.Should().Contain("private void ClearSheetTabDragState()");
        source.Should().Contain("_currentSheetId = _session.ActiveSheet.Id;");

        groupClick.Should().Contain("=> UpdateGroupedSheetsForClick(clickedSheetId, Keyboard.Modifiers);");
        groupClick.Should().Contain("UpdateGroupedSheetsForClick(SheetId clickedSheetId, ModifierKeys modifiers)");
        groupClick.Should().Contain("(modifiers & ModifierKeys.Shift) != 0");
        groupClick.Should().Contain("SheetGroupSelectionService.SelectRange");
        groupClick.Should().Contain("(modifiers & ModifierKeys.Control) != 0");
        groupClick.Should().Contain("SheetGroupSelectionService.Toggle");
        groupClick.Should().Contain("SheetGroupSelectionService.SelectSingle");

        leftNav.Should().Contain("SheetTabsScroller.ScrollToHorizontalOffset");
        leftNav.Should().Contain("SheetTabsScroller.HorizontalOffset - SheetTabNavScrollAmount");
        rightNav.Should().Contain("SheetTabsScroller.ScrollToHorizontalOffset");
        rightNav.Should().Contain("SheetTabsScroller.HorizontalOffset + SheetTabNavScrollAmount");
        navRightClick.Should().Contain("e.Handled = true;");
        navRightClick.Should().Contain("e.ChangedButton != MouseButton.Right");
        navRightClick.Should().Contain("BeginShowActivateSheetDialogFromSheetNav();");
        source.Should().Contain("private void SheetNavButton_Loaded");
        source.Should().Contain("handledEventsToo");
        source.Should().Contain("private void SheetNavButton_MouseRightButtonUp");
        source.Should().Contain("private void SheetNavButton_ContextMenuOpening");
        source.Should().Contain("private void BeginShowActivateSheetDialogFromSheetNav()");
        source.Should().Contain("_activateSheetDialogOpenOrPending");
        source.Should().Contain("Dispatcher.BeginInvoke(() =>");
        source.Should().Contain("private void ShowActivateSheetDialogFromSheetNav()");
        source.Should().Contain("new ActivateSheetDialog(_workbook, _currentSheetId)");
        navRightClick.Should().Contain("e.Handled = true;");

        // The sheet-tab context menu is now built at runtime from the neutral SheetTabContextMenuPlanner
        // (single-sourced with the Avalonia port) instead of a hand-authored XAML ContextMenu. The tab
        // chrome attaches the menu on load and dispatches each planner action to the existing handlers.
        xaml.Should().NotContain("<ContextMenu Opened=\"SheetTabContextMenu_XamlOpened\">");
        xaml.Should().Contain("Loaded=\"SheetTabChrome_Loaded\"");
        source.Should().Contain("private void SheetTabChrome_Loaded(object sender, RoutedEventArgs e)");
        source.Should().Contain("element.ContextMenuOpening += SheetTabChrome_ContextMenuOpening;");
        source.Should().Contain("element.ContextMenu = BuildSheetTabContextMenu(element.DataContext as SheetTabViewModel);");
        source.Should().Contain("private void SheetTabChrome_ContextMenuOpening(object sender, ContextMenuEventArgs e)");
        source.Should().Contain("private void RebuildSheetTabContextMenu(ContextMenu menu, SheetTabViewModel? tab)");
        source.Should().Contain("SheetTabContextMenuPlanner.BuildSheetTabCommands(state)");
        source.Should().Contain("HideSheetTabContextMenuInputGestures(menu);");
        source.Should().Contain("item.InputGestureText = string.Empty;");
        source.Should().Contain("private SheetTabContextMenuState BuildSheetTabContextMenuState(SheetTabViewModel? tab)");
        source.Should().Contain("CanUnhideSheet: hiddenSheetCount > 0");
        source.Should().Contain("CanUngroupSheets: _groupedSheetIds.Count > 1");
        source.Should().Contain("RibbonTooltip.SetKeyTip(menuItem, command.KeyTip)");
        source.Should().Contain("RibbonMetadata.SetCommandName(menuItem, command.CommandName)");
        source.Should().Contain("menu.Opened += SheetTabContextMenu_Opened;");
        source.Should().Contain("SheetTabContextMenuAction.Rename => SheetCtxRename_Click,");
        source.Should().Contain("SheetTabContextMenuAction.MoveOrCopy => SheetCtxMoveOrCopy_Click,");
        source.Should().Contain("SheetTabContextMenuAction.Hide => SheetCtxHide_Click,");
        source.Should().Contain("SheetTabContextMenuAction.Unhide => SheetCtxUnhide_Click,");
        source.Should().Contain("SheetTabContextMenuAction.SelectAllSheets => SheetCtxSelectAllSheets_Click,");
        source.Should().Contain("SheetTabContextMenuAction.UngroupSheets => SheetCtxUngroupSheets_Click,");

        static string Slice(string text, string startMarker, string endMarker)
        {
            var start = text.IndexOf(startMarker, StringComparison.Ordinal);
            var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
            start.Should().BeGreaterThanOrEqualTo(0, $"expected to find {startMarker}");
            end.Should().BeGreaterThan(start, $"expected to find {endMarker} after {startMarker}");
            return text[start..end];
        }
    }

    [Theory]
    [InlineData(3, 2, true, 3)]
    [InlineData(3, 2, false, 2)]
    [InlineData(3, 1, false, 1)]
    [InlineData(0, 2, false, 1)]
    [InlineData(0, 2, true, 2)]
    public void SheetTabDragIndexPlanner_UsesTargetHalfForBeforeAfterInsertion(
        int fromIndex,
        int targetIndex,
        bool insertAfterTarget,
        int expectedToIndex)
    {
        var toIndex = SheetTabPointerPlanner.CalculateDropIndex(fromIndex, targetIndex, insertAfterTarget);

        toIndex.Should().Be(expectedToIndex);
    }

    [Fact]
    public void SheetTabChrome_ReusesRenderedPathsAcrossRepeatedManyTabNavigationUpdates()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create(sheetCount: 140);

            harness.UpdateSheetTabNavigation();
            var chromeChildren = harness.SheetTabChromeChildren;
            var overlayChildren = harness.SheetTabOverlayChildren;

            chromeChildren.Should().NotBeEmpty("many visible tabs should render chrome paths before repeated scroll or resize callbacks");
            overlayChildren.Should().NotBeEmpty("the sheet-tab grid rule should be rendered before repeated callbacks");

            harness.UpdateSheetTabNavigation(iterations: 60);

            harness.SheetTabChromeChildren.Should().Equal(chromeChildren, "unchanged many-tab navigation callbacks should skip chrome clear/rebuild churn");
            harness.SheetTabOverlayChildren.Should().Equal(overlayChildren, "unchanged many-tab navigation callbacks should keep the overlay path intact");
        });
    }

    private sealed class MainWindowHarness : IDisposable
    {
        private readonly MainWindow _window;
        private FrameworkElement? _routedSheetTabTarget;

        private MainWindowHarness(MainWindow window)
        {
            _window = window;
        }

        public string? FocusedSheetTabName =>
            (FocusedSheetTabTarget?.DataContext as SheetTabViewModel)?.Name;

        public MainWindow Window => _window;

        public string? ActiveSheetTabName =>
            SheetTabViewModels
                .FirstOrDefault(viewModel => viewModel.IsActive)
                is { } active
                    ? active.Name
                    : null;

        public bool AddSheetButtonHasKeyboardFocus =>
            _window.FindName("AddSheetButton") is FrameworkElement addSheet &&
            addSheet.IsKeyboardFocusWithin;

        public string? SheetTabAutomationName(string name) =>
            AutomationProperties.GetName(SheetTabNameText(name) ?? SheetTabTarget(name));

        public bool SheetTabLockIsVisible(string name) =>
            SheetTabLockIcon(name)?.Visibility == Visibility.Visible;

        public IReadOnlyList<string> GroupedSheetTabNames =>
            SheetTabViewModels
                .Where(viewModel => viewModel.IsGrouped)
                .Select(viewModel => viewModel.Name)
                .ToList();

        public bool SheetTabContextMenuIsOpen => RoutedOrActiveSheetTabTarget?.ContextMenu?.IsOpen == true;

        public bool SheetTabContextMenuPlacementTargetIsFocusedTab =>
            RoutedOrActiveSheetTabTarget is { ContextMenu.PlacementTarget: { } target } &&
            ReferenceEquals(target, RoutedOrActiveSheetTabTarget);

        public string? SheetTabMenuItemGestureText(string header) =>
            RoutedOrActiveSheetTabTarget?.ContextMenu?.Items
                .OfType<MenuItem>()
                .FirstOrDefault(item => string.Equals(item.Header?.ToString(), header, StringComparison.Ordinal))
                ?.InputGestureText;

        public (bool HasNoColor, int ThemeSwatchCount, int StandardSwatchCount, bool HasMoreColors) SheetTabColorPalette
        {
            get
            {
                var tabColor = RoutedOrActiveSheetTabTarget?.ContextMenu?.Items
                    .OfType<MenuItem>()
                    .Single(item => string.Equals(item.Header?.ToString(), UiText.Get("MainWindow_Header_TabColor"), StringComparison.Ordinal));
                var menuItems = tabColor?.Items.OfType<MenuItem>().ToList() ?? [];
                var gallery = menuItems.Single(item => item.Header is StackPanel);
                var palettes = ((StackPanel)gallery.Header).Children.OfType<UniformGrid>().ToList();

                return (
                    menuItems.Any(item => string.Equals(item.Header?.ToString(), UiText.Get("RibbonWire_TabColorNone"), StringComparison.Ordinal)),
                    palettes[0].Children.Count,
                    palettes[1].Children.Count,
                    menuItems.Any(item => string.Equals(item.Header?.ToString(), UiText.Get("ColorPicker_MoreColorsEllipsis"), StringComparison.Ordinal)));
            }
        }

        public CellColor? ActiveSheetTabColor => _window.Session.ActiveSheet.TabColor;

        public void ApplyFirstStandardSheetTabColor()
        {
            var tabColor = RoutedOrActiveSheetTabTarget?.ContextMenu?.Items
                .OfType<MenuItem>()
                .Single(item => string.Equals(item.Header?.ToString(), UiText.Get("MainWindow_Header_TabColor"), StringComparison.Ordinal));
            var gallery = tabColor?.Items.OfType<MenuItem>().Single(item => item.Header is StackPanel);
            var palettes = ((StackPanel)gallery!.Header).Children.OfType<UniformGrid>().ToList();
            ((Button)palettes[1].Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            _window.UpdateLayout();
            PumpDispatcher();
        }

        public IReadOnlyList<UIElement> SheetTabChromeChildren =>
            ((Panel)_window.FindName("SheetTabsChromeLayer")).Children.Cast<UIElement>().ToList();

        public IReadOnlyList<UIElement> SheetTabOverlayChildren =>
            ((Panel)_window.FindName("SheetTabsOverlayLayer")).Children.Cast<UIElement>().ToList();

        public string DebugSheetTabs =>
            string.Join("; ", SheetTabTargets.Select(element =>
            {
                var dataContext = element.DataContext as SheetTabViewModel;
                var name = dataContext?.Name;
                var active = dataContext?.IsActive;
                return $"{name}:active={active}:focus={element.IsKeyboardFocusWithin}:menu={element.ContextMenu?.IsOpen}:placement={ReferenceEquals(element.ContextMenu?.PlacementTarget, element)}";
            })) + $" routed={_routedSheetTabTarget?.ContextMenu?.IsOpen}:{ReferenceEquals(_routedSheetTabTarget?.ContextMenu?.PlacementTarget, _routedSheetTabTarget)} focused={Keyboard.FocusedElement?.GetType().Name}";

        public bool FocusCurrentSheetTab()
        {
            var focused = _window.TryFocusCurrentSheetTabForTest();
            PumpDispatcher();
            return focused;
        }

        public void SetSheetProtected(string name, bool isProtected)
        {
            var sheet = CurrentWorkbook.Sheets.Single(sheet => string.Equals(sheet.Name, name, StringComparison.Ordinal));
            sheet.IsProtected = isProtected;
            _window.RefreshSheetTabsForTest();
            _window.UpdateLayout();
            PumpDispatcher();
            _window.UpdateLayout();
        }

        public bool FocusSheetTab(string name)
        {
            var target = SheetTabTarget(name);
            var focused = target.Focus();
            Keyboard.Focus(target);
            PumpDispatcher();
            return focused;
        }

        public bool FocusAddSheetButton()
        {
            if (_window.FindName("AddSheetButton") is not FrameworkElement addSheet)
                return false;

            var focused = addSheet.Focus();
            Keyboard.Focus(addSheet);
            PumpDispatcher();
            return focused;
        }

        public bool HandleFocusedSheetTabKeyboardNavigation(Key key)
        {
            var source = PresentationSource.FromVisual(_window);
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
            {
                RoutedEvent = Keyboard.KeyDownEvent
            };

            var handled = _window.TryHandleFocusedSheetTabKeyboardNavigationForTest(args);
            _window.UpdateLayout();
            PumpDispatcher();
            _window.UpdateLayout();
            return handled;
        }

        public void InsertNewSheet()
        {
            _window.InsertNewSheetForTest();
            _window.UpdateLayout();
            PumpDispatcher();
        }

        public void UpdateSheetTabNavigation(int iterations = 1)
        {
            for (var i = 0; i < iterations; i++)
                _window.UpdateSheetTabNavigationForTest();

            _window.UpdateLayout();
            PumpDispatcher();
        }

        public void RightClickSheetTab(string name)
        {
            var target = SheetTabTarget(name);
            var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right)
            {
                RoutedEvent = UIElement.MouseRightButtonDownEvent,
                Source = target
            };

            _window.RaiseSheetTabRightClickForTest(target, args);
            _window.UpdateLayout();
            PumpDispatcher();
        }

        public void SelectAllSheetsFromContextMenu()
        {
            _window.SelectAllSheetsFromContextMenuForTest();
            _window.UpdateLayout();
            PumpDispatcher();
        }

        public void OpenFocusedSheetTabContextMenu()
        {
            _routedSheetTabTarget = FocusedSheetTabTarget;
            _routedSheetTabTarget.Should().NotBeNull("the active sheet tab should have keyboard focus before the Menu key is routed");
            _routedSheetTabTarget!.Focus();
            Keyboard.Focus(_routedSheetTabTarget);

            var opened = _window.TryOpenFocusedSheetTabContextMenuForTest();
            opened.Should().BeTrue("the focused sheet tab route should open the sheet-tab context menu before worksheet fallback");
            if (_routedSheetTabTarget.ContextMenu is { } menu)
                _window.RaiseSheetTabContextMenuOpenedForTest(menu);
        }

        public bool RouteFocusedSheetTabContextMenu()
        {
            var routed = _window.TryOpenFocusedSheetTabContextMenuForTest();
            _window.UpdateLayout();
            PumpDispatcher();
            _window.UpdateLayout();
            return routed;
        }

        public static MainWindowHarness Create(int sheetCount = 1, double width = 1280)
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = workbook };
            var graph = new DependencyGraph();
            var evaluator = new FormulaEvaluator();
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(graph, evaluator),
                [],
                workbookRef,
                workbook,
                NullUserMessageService.Instance)
            {
                WindowState = WindowState.Normal,
                Width = width,
                Height = 720
            };

            window.Show();
            window.UpdateLayout();
            PumpDispatcher();
            var harness = new MainWindowHarness(window);
            for (var i = 1; i < sheetCount; i++)
                harness.InsertNewSheet();

            return harness;
        }

        public void Dispose()
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
            PumpDispatcher();
        }

        private FrameworkElement? FocusedSheetTabTarget
        {
            get
            {
                return SheetTabTargets.FirstOrDefault(element => element.IsKeyboardFocusWithin);
            }
        }

        private FrameworkElement? ActiveSheetTabTarget =>
            SheetTabTargets.FirstOrDefault(element =>
                element.DataContext is SheetTabViewModel { IsActive: true });

        private FrameworkElement? RoutedOrActiveSheetTabTarget => _routedSheetTabTarget ?? ActiveSheetTabTarget;

        private IReadOnlyList<SheetTabViewModel> SheetTabViewModels =>
            SheetTabTargets
                .Select(element => element.DataContext)
                .OfType<SheetTabViewModel>()
                .Distinct()
                .ToList();

        private FrameworkElement SheetTabTarget(string name) =>
            SheetTabTargets.Single(element =>
                element.DataContext is SheetTabViewModel viewModel &&
                string.Equals(viewModel.Name, name, StringComparison.Ordinal));

        private FrameworkElement? SheetTabLockIcon(string name) =>
            WpfTestTree.FindVisualDescendants<FrameworkElement>(SheetTabTarget(name))
                .Concat(WpfTestTree.FindLogicalDescendants<FrameworkElement>(SheetTabTarget(name)))
                .FirstOrDefault(element => string.Equals(element.Name, "ProtectedSheetLockIcon", StringComparison.Ordinal));

        private FrameworkElement? SheetTabNameText(string name) =>
            WpfTestTree.FindVisualDescendants<FrameworkElement>(SheetTabTarget(name))
                .Concat(WpfTestTree.FindLogicalDescendants<FrameworkElement>(SheetTabTarget(name)))
                .FirstOrDefault(element => string.Equals(element.Name, "SheetTabNameText", StringComparison.Ordinal));

        private Workbook CurrentWorkbook => _window.Session.Workbook;

        private IReadOnlyList<FrameworkElement> SheetTabTargets
        {
            get
            {
                if (_window.FindName("SheetTabsControl") is not ItemsControl tabs)
                    return [];

                return WpfTestTree.FindVisualDescendants<DependencyObject>(tabs)
                    .Concat(WpfTestTree.FindLogicalDescendants<DependencyObject>(tabs))
                    .OfType<FrameworkElement>()
                    .Distinct()
                    .Where(element =>
                        element.ContextMenu is not null &&
                        element.DataContext?.GetType().Name == "SheetTabViewModel")
                    .ToList();
            }
        }

    }

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    private static Rect BoundsRelativeToWindow(FrameworkElement element, Window window) =>
        element.TransformToAncestor(window).TransformBounds(new Rect(new Size(element.ActualWidth, element.ActualHeight)));

    private static double HorizontalCenter(Rect rect) => rect.Left + rect.Width / 2.0;

    private static double VerticalCenter(Rect rect) => rect.Top + rect.Height / 2.0;

    private static void CaptureSheetTabLowerBandIfRequested(
        Window window,
        FrameworkElement row,
        string environmentVariableName = "FREEX_SHEET_TAB_LOWER_BAND_CAPTURE")
    {
        var outputPath = Environment.GetEnvironmentVariable(environmentVariableName);
        if (string.IsNullOrWhiteSpace(outputPath))
            return;

        if (window.FindName("RootGrid") is not FrameworkElement root)
            return;

        root.UpdateLayout();
        var source = PresentationSource.FromVisual(root);
        var dpiX = source?.CompositionTarget.TransformToDevice.M11 ?? 1.0;
        var dpiY = source?.CompositionTarget.TransformToDevice.M22 ?? 1.0;
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(root.ActualWidth * dpiX));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(root.ActualHeight * dpiY));

        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96 * dpiX, 96 * dpiY, PixelFormats.Pbgra32);
        bitmap.Render(root);

        var rowTop = row.TransformToAncestor(root).Transform(new Point(0, 0)).Y;
        var cropTop = Math.Max(0, (int)Math.Floor((rowTop - 28) * dpiY));
        var cropHeight = Math.Min(pixelHeight - cropTop, Math.Max(1, (int)Math.Ceiling(120 * dpiY)));
        var cropped = new CroppedBitmap(bitmap, new Int32Rect(0, cropTop, pixelWidth, cropHeight));

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(cropped));
        using var stream = File.Create(outputPath);
        encoder.Save(stream);
    }
}
