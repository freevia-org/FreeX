using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using SheetGridView = FreeX.App.UI.GridView;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowRibbonKeyTipTests
{
    private sealed class MainWindowHarness : IDisposable
    {
        private static SharedMainWindowSession? SharedSession;

        private readonly MainWindow _window;
        private readonly Workbook _workbook;
        private readonly MethodInfo _enterKeyTipMode;
        private readonly MethodInfo _handleActiveRibbonKeyTip;
        private readonly MethodInfo _tryHandleDirectRibbonKeyTip;
        private readonly MethodInfo _tryHandleFocusedRibbonKeyboardNavigation;
        private readonly MethodInfo _isInsideRibbonSurface;
        private readonly MethodInfo _getVisibleKeyTipElements;
        private readonly MethodInfo _updateRibbonCompactMode;
        private readonly MethodInfo _updateViewport;
        private readonly MethodInfo _updateSsRecentList;
        private readonly MethodInfo _refreshSheetProtectionUi;
        private readonly MethodInfo _hideStartScreen;
        private readonly MethodInfo _rebuildQuickAccessToolbar;
        private readonly MethodInfo _zoomCustomMenuItemClick;
        private readonly Type _scopeType;
        private readonly FieldInfo _scopeField;
        private readonly FieldInfo _activeMenuField;
        private readonly FieldInfo _recentFilesField;
        private readonly FieldInfo _optionsField;
        private readonly RecordingUserMessageService _messageService;

        private MainWindowHarness(
            MainWindow window,
            Workbook workbook,
            RecordingUserMessageService messageService)
        {
            _window = window;
            _workbook = workbook;
            _messageService = messageService;
            _enterKeyTipMode = typeof(MainWindow).GetMethod("EnterRibbonKeyTipMode", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "EnterRibbonKeyTipMode");
            _handleActiveRibbonKeyTip = typeof(MainWindow).GetMethod("HandleActiveRibbonKeyTip", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "HandleActiveRibbonKeyTip");
            _tryHandleDirectRibbonKeyTip = typeof(MainWindow).GetMethod("TryHandleDirectRibbonKeyTip", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "TryHandleDirectRibbonKeyTip");
            _tryHandleFocusedRibbonKeyboardNavigation = typeof(MainWindow).GetMethod("TryHandleFocusedRibbonKeyboardNavigation", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "TryHandleFocusedRibbonKeyboardNavigation");
            _isInsideRibbonSurface = typeof(MainWindow).GetMethod("IsInsideRibbonSurface", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "IsInsideRibbonSurface");
            _getVisibleKeyTipElements = typeof(MainWindow).GetMethod("GetVisibleKeyTipElements", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "GetVisibleKeyTipElements");
            _updateRibbonCompactMode = typeof(MainWindow).GetMethod("UpdateRibbonCompactMode", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "UpdateRibbonCompactMode");
            _updateViewport = typeof(MainWindow).GetMethod("UpdateViewport", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "UpdateViewport");
            _updateSsRecentList = typeof(MainWindow).GetMethod("UpdateSsRecentList", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "UpdateSsRecentList");
            _refreshSheetProtectionUi = typeof(MainWindow).GetMethod("RefreshSheetProtectionUi", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "RefreshSheetProtectionUi");
            _hideStartScreen = typeof(MainWindow).GetMethod("HideStartScreen", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "HideStartScreen");
            _rebuildQuickAccessToolbar = typeof(MainWindow).GetMethod("RebuildQuickAccessToolbar", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "RebuildQuickAccessToolbar");
            _zoomCustomMenuItemClick = typeof(MainWindow).GetMethod("ZoomCustomMenuItem_Click", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "ZoomCustomMenuItem_Click");
            _scopeType = typeof(MainWindow).GetNestedType("RibbonKeyTipScope", BindingFlags.NonPublic)
                ?? throw new MissingMemberException(nameof(MainWindow), "RibbonKeyTipScope");
            _scopeField = typeof(MainWindow).GetField("_ribbonKeyTipScope", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_ribbonKeyTipScope");
            _activeMenuField = typeof(MainWindow).GetField("_activeRibbonKeyTipMenu", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_activeRibbonKeyTipMenu");
            _recentFilesField = typeof(MainWindow).GetField("_recentFiles", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_recentFiles");
            _optionsField = typeof(MainWindow).GetField("_options", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_options");
        }

        public string? SelectedRibbonTabHeader =>
            (_window.FindName("RibbonTabs") as TabControl)?.SelectedItem is TabItem tab
                ? tab.Header?.ToString()
                : null;

        public string KeyTipScope => _scopeField.GetValue(_window)?.ToString() ?? "";

        public bool? IsToggleChecked(string name) =>
            (_window.FindName(name) as System.Windows.Controls.Primitives.ToggleButton)?.IsChecked;

        public IReadOnlyList<string> OverlayBadgeTexts =>
            (_window.FindName("KeyTipOverlay") as Canvas)?.Children
                .OfType<Border>()
                .Select(border => (border.Child as TextBlock)?.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Cast<string>()
                .ToList() ?? [];

        public Rect OverlayBadgeBounds(string text)
        {
            var overlay = (_window.FindName("KeyTipOverlay") as Canvas)
                ?? throw new InvalidOperationException("KeyTipOverlay was not found.");
            var badges = overlay.Children
                .OfType<Border>()
                .Where(border => string.Equals((border.Child as TextBlock)?.Text, text, StringComparison.Ordinal))
                .ToList();
            badges.Should().ContainSingle($"the overlay should contain one {text} badge");

            var badge = badges[0];
            var width = badge.ActualWidth > 0 ? badge.ActualWidth : badge.DesiredSize.Width;
            var height = badge.ActualHeight > 0 ? badge.ActualHeight : badge.DesiredSize.Height;
            return new Rect(Canvas.GetLeft(badge), Canvas.GetTop(badge), width, height);
        }

        public Rect ElementBounds(string name)
        {
            var root = (_window.FindName("RootGrid") as FrameworkElement)
                ?? throw new InvalidOperationException("RootGrid was not found.");
            var element = (_window.FindName(name) as FrameworkElement)
                ?? throw new InvalidOperationException($"{name} was not found.");
            return element.TransformToAncestor(root)
                .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        }

        public Rect RibbonButtonBoundsByTitle(string title)
        {
            var root = (_window.FindName("RootGrid") as FrameworkElement)
                ?? throw new InvalidOperationException("RootGrid was not found.");
            var matches = SelectedRibbonCommandButtons()
                .Where(button => string.Equals(RibbonTooltip.GetTitle(button), title, StringComparison.Ordinal))
                .ToList();
            matches.Should().ContainSingle($"the selected ribbon tab should expose one visible {title} button");

            var button = matches[0];
            return button.TransformToAncestor(root)
                .TransformBounds(new Rect(0, 0, button.ActualWidth, button.ActualHeight));
        }

        public bool ActiveMenuIsOpen => ActiveMenu?.IsOpen == true;

        public (string Title, string Message)? LastInfoMessage => _messageService.LastInfo;

        public bool FocusedElementIsInsideRibbon =>
            Keyboard.FocusedElement is DependencyObject focusedElement &&
            (bool)_isInsideRibbonSurface.Invoke(_window, [focusedElement])!;

        public bool FocusedElementIsWorksheet =>
            ReferenceEquals(Keyboard.FocusedElement, _window.FindName("SheetGrid"));

        public bool StartScreenIsVisible =>
            (_window.FindName("StartScreenOverlay") as FrameworkElement)?.Visibility == Visibility.Visible;

        public bool NumberFormatDropDownIsOpen =>
            (_window.FindName("NumberFormatBox") as ComboBox)?.IsDropDownOpen == true;

        public bool NumberFormatBoxHasKeyboardFocus =>
            (_window.FindName("NumberFormatBox") as ComboBox)?.IsKeyboardFocusWithin == true ||
            ReferenceEquals(Keyboard.FocusedElement, _window.FindName("NumberFormatBox"));

        public bool UndoQatIsEnabled =>
            (_window.FindName("UndoQatBtn") as Button)?.IsEnabled == true;

        public bool RedoQatIsEnabled =>
            (_window.FindName("RedoQatBtn") as Button)?.IsEnabled == true;

        public bool TitleBarQatIsVisible =>
            (_window.FindName("TitleBarQatPanel") as FrameworkElement)?.Visibility == Visibility.Visible;

        public bool BelowRibbonQatIsVisible =>
            (_window.FindName("BelowRibbonQatRoot") as FrameworkElement)?.Visibility == Visibility.Visible;

        public bool? NamedButtonIsEnabled(string name) =>
            (_window.FindName(name) as Button)?.IsEnabled;

        public string? StatusZoomText =>
            (_window.FindName("StatusZoomText") as TextBlock)?.Text;

        public int ExpectedZoomSelectionPercent
        {
            get
            {
                if (_window.FindName("SheetGrid") is not SheetGridView { SelectedRange: { } range } sheetGrid)
                    return 100;

                return (int)Math.Round(ZoomSelectionPlanner.CalculateFitPercent(
                    sheetGrid.ActualWidth,
                    sheetGrid.ActualHeight,
                    range.ColCount,
                    range.RowCount));
            }
        }

        public (bool ShowGridlines, bool ShowHeadings, bool ShowRulers) ActiveSheetViewOptions
        {
            get
            {
                var sheet = _workbook.Sheets[0];
                return (sheet.ShowGridlines, sheet.ShowHeadings, sheet.ShowRulers);
            }
        }

        public bool FormulaBarIsVisible =>
            (_window.FindName("FormulaBarBorder") as FrameworkElement)?.Visibility == Visibility.Visible;

        public WorksheetViewMode ActiveSheetViewMode => _workbook.Sheets[0].ViewMode;

        public IReadOnlyList<uint> ActiveSheetRowPageBreaks => _workbook.Sheets[0].RowPageBreaks.ToList();

        public IReadOnlyList<uint> ActiveSheetColumnPageBreaks => _workbook.Sheets[0].ColumnPageBreaks.ToList();

        public WorksheetPageMargins ActiveSheetPageMargins => _workbook.Sheets[0].PageMargins;

        public WorksheetPageOrientation ActiveSheetPageOrientation => _workbook.Sheets[0].PageOrientation;

        public WorksheetPaperSize ActiveSheetPaperSize => _workbook.Sheets[0].PaperSize;

        public (bool PrintGridlines, bool PrintHeadings) ActiveSheetPrintOptions
        {
            get
            {
                var sheet = _workbook.Sheets[0];
                return (sheet.PrintGridlines, sheet.PrintHeadings);
            }
        }

        public (uint StartRow, uint StartCol, uint EndRow, uint EndCol)? ActiveSheetPrintArea
        {
            get
            {
                var range = _workbook.Sheets[0].PrintArea;
                return range is { } value
                    ? (value.Start.Row, value.Start.Col, value.End.Row, value.End.Col)
                    : null;
            }
        }

        public WorkbookCalculationMode WorkbookCalculationMode => _workbook.CalculationMode;

        public (uint FrozenRows, uint FrozenCols) ActiveSheetFrozenPanes
        {
            get
            {
                var sheet = _workbook.Sheets[0];
                return (sheet.FrozenRows, sheet.FrozenCols);
            }
        }

        public (uint? SplitRow, uint? SplitColumn) ActiveSheetSplitPanes
        {
            get
            {
                var sheet = _workbook.Sheets[0];
                return (sheet.SplitRow, sheet.SplitColumn);
            }
        }

        public (uint Row, uint Col)? SelectedCellAddress
        {
            get
            {
                if (_window.FindName("SheetGrid") is not SheetGridView { SelectedRange: { } range })
                    return null;

                return (range.Start.Row, range.Start.Col);
            }
        }

        public bool ActiveCellBold
        {
            get
            {
                var sheet = _workbook.Sheets[0];
                var address = new CellAddress(sheet.Id, 1, 1);
                var styleId = sheet.GetCell(address)?.StyleId
                    ?? sheet.GetStyleOnly(address.Row, address.Col)
                    ?? StyleId.Default;
                return _workbook.GetStyle(styleId).Bold;
            }
        }

        public void SetNumber(uint row, uint col, double value)
        {
            var sheet = _workbook.Sheets[0];
            sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(value));
            PumpDispatcher();
        }

        public string? CellFormulaText(uint row, uint col)
        {
            var sheet = _workbook.Sheets[0];
            return sheet.GetCell(new CellAddress(sheet.Id, row, col))?.FormulaText;
        }

        public void SelectActiveCell()
        {
            var sheet = _workbook.Sheets[0];
            var address = new CellAddress(sheet.Id, 1, 1);
            if (_window.FindName("SheetGrid") is SheetGridView sheetGrid)
                sheetGrid.SelectedRange = new GridRange(address, address);
            PumpDispatcher();
        }

        public void SelectRange(uint startRow, uint startCol, uint endRow, uint endCol)
        {
            var sheet = _workbook.Sheets[0];
            if (_window.FindName("SheetGrid") is SheetGridView sheetGrid)
            {
                sheetGrid.SelectedRanges = null;
                sheetGrid.SelectedRange = new GridRange(
                    new CellAddress(sheet.Id, startRow, startCol),
                    new CellAddress(sheet.Id, endRow, endCol));
            }

            PumpDispatcher();
        }

        public string? SetButtonKeyTip(string name, string keyTip)
        {
            var button = (_window.FindName(name) as ButtonBase)
                ?? throw new InvalidOperationException($"Button {name} was not found.");
            var originalKeyTip = RibbonTooltip.GetKeyTip(button);
            RibbonTooltip.SetKeyTip(button, keyTip);
            PumpDispatcher();
            return originalKeyTip;
        }

        public bool ButtonIsInBelowRibbonQat(string name) =>
            _window.FindName(name) is Button button &&
            ReferenceEquals(button.Parent, _window.FindName("BelowRibbonQatPanel"));

        public void ConfigureQuickAccessToolbar(IReadOnlyList<string> commandIds, bool belowRibbon)
        {
            var options = (FreeXOptions)_optionsField.GetValue(_window)!;
            options.QuickAccessToolbarCommands = commandIds.ToList();
            options.QuickAccessToolbarBelowRibbon = belowRibbon;
            _rebuildQuickAccessToolbar.Invoke(_window, null);
            _window.UpdateLayout();
            PumpDispatcher();
        }

        public IDisposable AddHomeRibbonCommandButton(string keyTip, string title)
        {
            var panel = (_window.FindName("HomeRibbonPanel") as Panel)
                ?? throw new InvalidOperationException("HomeRibbonPanel was not found.");
            var button = new Button
            {
                Content = title,
                Width = 96,
                Height = 28,
                IsEnabled = true
            };
            RibbonTooltip.SetTitle(button, title);
            RibbonTooltip.SetKeyTip(button, keyTip);

            panel.Children.Add(button);
            _window.UpdateLayout();
            PumpDispatcher();

            return new DisposableAction(() =>
            {
                panel.Children.Remove(button);
                _window.UpdateLayout();
                PumpDispatcher();
            });
        }

        public void AddNote(uint row, uint col, string text)
        {
            var sheet = _workbook.Sheets[0];
            sheet.Comments[new CellAddress(sheet.Id, row, col)] = text;
            PumpDispatcher();
        }

        public void AddThreadedComment(uint row, uint col, string text)
        {
            var sheet = _workbook.Sheets[0];
            sheet.ThreadedComments[new CellAddress(sheet.Id, row, col)] = new ThreadedComment(text);
            PumpDispatcher();
        }

        public int RowOutlineLevel(uint row)
        {
            var sheet = _workbook.Sheets[0];
            return sheet.RowOutlineLevels.TryGetValue(row, out var level) ? level : 0;
        }

        public int DrawingShapeCount => _workbook.Sheets[0].DrawingShapes.Count;

        public DrawingShapeKind? LastDrawingShapeKind => _workbook.Sheets[0].DrawingShapes.LastOrDefault()?.Kind;

        public (uint Row, uint Col)? LastDrawingShapeAnchor
        {
            get
            {
                var anchor = _workbook.Sheets[0].DrawingShapes.LastOrDefault()?.Anchor;
                return anchor is { } value ? (value.Row, value.Col) : null;
            }
        }

        public int ChartCount => _workbook.Sheets[0].Charts.Count;

        public ChartType? LastChartType => _workbook.Sheets[0].Charts.LastOrDefault()?.Type;

        public string? ActiveMenuItemGestureText(string header) =>
            FindActiveMenuItem(header)?.InputGestureText;

        public bool? ActiveMenuItemIsChecked(string header) =>
            FindActiveMenuItem(header)?.IsChecked;

        public bool ActiveMenuItemSubmenuIsOpen(string header) =>
            FindActiveMenuItem(header)?.IsSubmenuOpen == true;

        public WorkbookWindowArrangement WorkbookArrangement =>
            _workbook.WindowArrangement;

        public IReadOnlyList<string> VisibleCommandKeyTips(string keyTip)
        {
            var scope = Enum.Parse(_scopeType, "Commands");
            var elements = ((System.Collections.IEnumerable)_getVisibleKeyTipElements.Invoke(_window, [scope])!)
                .OfType<FrameworkElement>()
                .Where(element => string.Equals(RibbonTooltip.GetKeyTip(element), keyTip, StringComparison.OrdinalIgnoreCase))
                .Select(element => RibbonTooltip.GetTitle(element) ?? element.Name ?? element.GetType().Name)
                .ToList();
            return elements;
        }

        public bool? CommandButtonIsEnabled(string name) =>
            (_window.FindName(name) as ButtonBase)?.IsEnabled;

        public string? CommandButtonHelpText(string name) =>
            _window.FindName(name) is DependencyObject element
                ? AutomationProperties.GetHelpText(element)
                : null;

        public void ShowPivotContextualTabs()
        {
            if (_window.FindName("PivotTableAnalyzeTab") is TabItem analyzeTab)
                analyzeTab.Visibility = Visibility.Visible;
            if (_window.FindName("PivotTableDesignTab") is TabItem designTab)
                designTab.Visibility = Visibility.Visible;

            _window.UpdateLayout();
            PumpDispatcher();
        }

        public void RefreshViewport()
        {
            _updateViewport.Invoke(_window, null);
            _window.UpdateLayout();
            PumpDispatcher();
        }

        public bool ContextualTabIsVisible(string name) =>
            (_window.FindName(name) as TabItem)?.Visibility == Visibility.Visible;

        public bool PivotFieldListPaneIsVisible =>
            (_window.FindName("PivotFieldListPane") as FrameworkElement)?.Visibility == Visibility.Visible;

        private ContextMenu? ActiveMenu => _activeMenuField.GetValue(_window) as ContextMenu;

        private IEnumerable<ButtonBase> SelectedRibbonCommandButtons()
        {
            if (_window.FindName("RibbonTabs") is not TabControl { SelectedItem: TabItem selectedTab })
                return [];

            var root = selectedTab.Content as DependencyObject ?? selectedTab;
            return EnumerateSelfAndVisualDescendants(root)
                .Concat(EnumerateLogicalDescendants(root))
                .OfType<ButtonBase>()
                .Distinct()
                .Where(button => button.IsVisible);
        }

        private MenuItem? FindActiveMenuItem(string header) =>
            ActiveMenu is { } menu
                ? EnumerateMenuItems(menu).FirstOrDefault(item => string.Equals(item.Header?.ToString(), header, StringComparison.Ordinal))
                : null;

        public static MainWindowHarness Create(Action<Workbook>? configureWorkbook = null)
        {
            var session = SharedSession ??= CreateSharedSession();
            var window = session.Window;
            if (!window.IsVisible)
                window.Show();
            window.Activate();

            window.WindowState = WindowState.Normal;
            window.Width = 2400;
            window.Height = 720;
            if (window.FindName("RibbonTabs") is TabControl ribbonTabs)
                ribbonTabs.Width = 2400;
            window.UpdateLayout();
            PumpDispatcher();

            var createNewWorkbook = typeof(MainWindow).GetMethod("CreateNewWorkbook", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "CreateNewWorkbook");
            createNewWorkbook.Invoke(window, null);
            configureWorkbook?.Invoke(session.WorkbookRef.Current);

            var harness = new MainWindowHarness(window, session.WorkbookRef.Current, session.MessageService);
            harness.ResetUiState();
            return harness;
        }

        private static SharedMainWindowSession CreateSharedSession()
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = workbook };
            var graph = new DependencyGraph();
            var evaluator = new FormulaEvaluator();
            var messageService = new RecordingUserMessageService();
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(graph, evaluator),
                [],
                workbookRef,
                workbook,
                messageService);

            window.WindowState = WindowState.Normal;
            window.Width = 2400;
            window.Height = 720;
            window.Show();
            window.Activate();
            if (window.FindName("RibbonTabs") is TabControl ribbonTabs)
                ribbonTabs.Width = 2400;
            window.UpdateLayout();
            PumpDispatcher();
            return new SharedMainWindowSession(window, workbookRef, messageService);
        }

        public void SetRibbonWidth(double width)
        {
            if (_window.FindName("RibbonTabs") is TabControl ribbonTabs)
            {
                ribbonTabs.Width = width;
                ribbonTabs.SelectedIndex = 1;
            }

            _window.WindowState = WindowState.Normal;
            _window.Width = width;
            _window.UpdateLayout();
            _updateRibbonCompactMode.Invoke(_window, [true]);
            PumpDispatcher();
        }

        public void SelectRibbonTab(string header, double width)
        {
            if (_window.FindName("RibbonTabs") is TabControl ribbonTabs)
            {
                ribbonTabs.Width = width;
                ribbonTabs.SelectedItem = ribbonTabs.Items
                    .OfType<TabItem>()
                    .First(item => string.Equals(item.Header?.ToString(), header, StringComparison.Ordinal));
            }

            _window.WindowState = WindowState.Normal;
            _window.Width = width;
            _window.UpdateLayout();
            PumpDispatcher();
            PumpDispatcher();
            _updateRibbonCompactMode.Invoke(_window, [true]);
            PumpDispatcher();
        }

        public void SetRecentFiles(IEnumerable<string> recentPaths, IEnumerable<string> pinnedPaths)
        {
            var store = (RecentFilesStore)_recentFilesField.GetValue(_window)!;
            store.Entries.Clear();
            store.Entries.AddRange(recentPaths.Select(path => new RecentFileEntry
            {
                Path = path,
                LastOpened = DateTimeOffset.UtcNow,
                IsPinned = false
            }));
            store.Entries.AddRange(pinnedPaths.Select(path => new RecentFileEntry
            {
                Path = path,
                LastOpened = DateTimeOffset.UtcNow,
                IsPinned = true
            }));
            _updateSsRecentList.Invoke(_window, [""]);
            PumpDispatcher();
        }

        public void RefreshSheetProtectionUi()
        {
            _refreshSheetProtectionUi.Invoke(_window, null);
            PumpDispatcher();
        }

        public void EnterKeyTipScope(string scope)
        {
            var value = Enum.Parse(_scopeType, scope);
            _enterKeyTipMode.Invoke(_window, [value]);
            PumpDispatcher();
        }

        public void HandleKeyTip(Key key)
        {
            _handleActiveRibbonKeyTip.Invoke(_window, [key]);
            PumpDispatcher();
        }

        public bool HandleDirectTopLevelKeyTip(Key key)
        {
            var handled = (bool)_tryHandleDirectRibbonKeyTip.Invoke(_window, [key])!;
            PumpDispatcher();
            return handled;
        }

        public bool FocusSelectedRibbonTab()
        {
            if (_window.FindName("RibbonTabs") is not TabControl { SelectedItem: TabItem tab })
                return false;

            var focused = tab.Focus();
            Keyboard.Focus(tab);
            PumpDispatcher();
            return focused || ReferenceEquals(Keyboard.FocusedElement, tab);
        }

        public bool HandleFocusedRibbonKey(Key key)
        {
            var source = PresentationSource.FromVisual(_window);
            source.Should().NotBeNull("the shared test window must be visible before routing focused-ribbon keyboard input");
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source!, Environment.TickCount, key)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent
            };
            var handled = (bool)_tryHandleFocusedRibbonKeyboardNavigation.Invoke(_window, [args])!;
            PumpDispatcher();
            return handled;
        }

        public void OpenRibbonMenu(Key tabKeyTip, params Key[] commandKeyTips)
        {
            EnterKeyTipScope("TopLevel");
            HandleKeyTip(tabKeyTip);
            foreach (var keyTip in commandKeyTips)
                HandleKeyTip(keyTip);

            ActiveMenuIsOpen.Should().BeTrue(
                "the ribbon keytip sequence {0},{1} should open a menu",
                tabKeyTip,
                string.Join(",", commandKeyTips));
        }

        public void OpenCustomZoomDialogAndCancel()
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() =>
                {
                    var zoomDialog = _window.OwnedWindows.OfType<ZoomDialog>().Single();
                    zoomDialog.Close();
                }));

            _zoomCustomMenuItemClick.Invoke(_window, [_window, new RoutedEventArgs()]);
            PumpDispatcher();
        }

        private void ResetUiState()
        {
            _window.Activate();
            _messageService.Clear();
            _hideStartScreen.Invoke(_window, null);
            ConfigureQuickAccessToolbar(QuickAccessToolbarCatalog.DefaultCommandIds, belowRibbon: false);
            if (ActiveMenu is { } activeMenu)
                activeMenu.IsOpen = false;
            _scopeField.SetValue(_window, Enum.Parse(_scopeType, "None"));
            _activeMenuField.SetValue(_window, null);
            if (_window.FindName("KeyTipOverlay") is Canvas overlay)
            {
                overlay.Children.Clear();
                overlay.Visibility = Visibility.Collapsed;
            }
            if (_window.FindName("RibbonTabs") is TabControl ribbonTabs)
            {
                ribbonTabs.Width = 2400;
                ribbonTabs.SelectedIndex = 1;
            }
            if (_window.FindName("NumberFormatBox") is ComboBox numberFormatBox)
                numberFormatBox.IsDropDownOpen = false;
            SelectActiveCell();
            _window.UpdateLayout();
            PumpDispatcher();
        }

        public void Dispose()
        {
            ConfigureQuickAccessToolbar(QuickAccessToolbarCatalog.DefaultCommandIds, belowRibbon: false);
            if (ActiveMenu is { } activeMenu)
                activeMenu.IsOpen = false;
            if (_window.FindName("NumberFormatBox") is ComboBox numberFormatBox)
                numberFormatBox.IsDropDownOpen = false;
            _window.UpdateLayout();
            PumpDispatcher();
        }

        private sealed record SharedMainWindowSession(
            MainWindow Window,
            WorkbookRef WorkbookRef,
            RecordingUserMessageService MessageService);

        private sealed class RecordingUserMessageService : FreeX.App.UI.IUserMessageService
        {
            private readonly List<(string Kind, string Title, string Message)> _messages = [];

            public (string Title, string Message)? LastInfo
            {
                get
                {
                    for (var index = _messages.Count - 1; index >= 0; index--)
                    {
                        var message = _messages[index];
                        if (message.Kind == "Info")
                            return (message.Title, message.Message);
                    }

                    return null;
                }
            }

            public void Clear() => _messages.Clear();

            public void ShowError(string message, string title = "Error") =>
                _messages.Add(("Error", title, message));

            public void ShowWarning(string message, string title = "Warning") =>
                _messages.Add(("Warning", title, message));

            public void ShowInfo(string message, string title = "Information") =>
                _messages.Add(("Info", title, message));

            public bool AskYesNo(string message, string title = "Confirm")
            {
                _messages.Add(("Question", title, message));
                return false;
            }
        }

        private sealed class DisposableAction(Action dispose) : IDisposable
        {
            private Action? _dispose = dispose;

            public void Dispose()
            {
                var disposeAction = _dispose;
                if (disposeAction is null)
                    return;

                _dispose = null;
                disposeAction();
            }
        }

        private static IEnumerable<MenuItem> EnumerateMenuItems(ItemsControl control)
        {
            foreach (var item in control.Items)
            {
                if (item is not MenuItem menuItem)
                    continue;

                yield return menuItem;

                foreach (var child in EnumerateMenuItems(menuItem))
                    yield return child;
            }
        }

        private static IEnumerable<DependencyObject> EnumerateSelfAndVisualDescendants(DependencyObject root)
        {
            yield return root;

            var childCount = 0;
            try
            {
                childCount = VisualTreeHelper.GetChildrenCount(root);
            }
            catch (InvalidOperationException)
            {
            }

            for (var index = 0; index < childCount; index++)
            {
                var child = VisualTreeHelper.GetChild(root, index);
                foreach (var descendant in EnumerateSelfAndVisualDescendants(child))
                    yield return descendant;
            }
        }

        private static IEnumerable<DependencyObject> EnumerateLogicalDescendants(DependencyObject root)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is not DependencyObject dependencyObject)
                    continue;

                yield return dependencyObject;

                foreach (var descendant in EnumerateLogicalDescendants(dependencyObject))
                    yield return descendant;
            }
        }
    }

    private static void ConfigureWorkbookWithPivotTable(Workbook workbook)
    {
        var sheet = workbook.Sheets[0];
        var sheetId = sheet.Id;
        sheet.SetCell(new CellAddress(sheetId, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheetId, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheetId, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheetId, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheetId, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheetId, 3, 2), new NumberValue(20));

        var sourceRange = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 3, 2));
        var targetRange = new GridRange(
            new CellAddress(sheetId, 6, 5),
            new CellAddress(sheetId, 9, 6));

        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = sourceRange.ToString()
        });
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Region"));
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = sourceRange,
            TargetRange = targetRange
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
    }

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new InvalidOperationException($"Sheet {sheetId} not found");
    }

    private sealed class TempRecentFiles : IDisposable
    {
        private readonly string _directory;

        private TempRecentFiles(string directory, IReadOnlyList<string> paths)
        {
            _directory = directory;
            Paths = paths;
        }

        public IReadOnlyList<string> Paths { get; }

        public static TempRecentFiles Create(int count)
        {
            var directory = Path.Combine(Path.GetTempPath(), "FreeXRecentKeyTips", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var paths = Enumerable.Range(1, count)
                .Select(index =>
                {
                    var path = Path.Combine(directory, $"Book{index}.xlsx");
                    File.WriteAllText(path, "");
                    return path;
                })
                .ToList();
            return new TempRecentFiles(directory, paths);
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }

    private static void RunSta(Action action)
    {
        StaTestRunner.Run(action);
    }
}
