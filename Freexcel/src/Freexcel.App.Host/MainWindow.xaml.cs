using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using CellHAlign = Freexcel.Core.Model.HorizontalAlignment;
using CellVAlign = Freexcel.Core.Model.VerticalAlignment;
using Microsoft.Extensions.Logging;
using Freexcel.Core.Model;
using Freexcel.Core.Formula;
using Freexcel.Core.Commands;
using Freexcel.Core.Calc;
using Freexcel.Core.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using System.IO.Packaging;
using System.Threading;
using System.Windows.Threading;

namespace Freexcel.App.Host;

/// <summary>
/// Main application window — the spreadsheet shell.
/// Coordinates between the engine and the UI components.
/// </summary>
public partial class MainWindow : Window
{
    private const double MaximizedSafeInsetDip = 8.0;

    private readonly ILogger<MainWindow> _logger;
    private readonly IViewportService _viewportService;
    private readonly ICommandBus _commandBus;
    private readonly RecalcEngine _recalcEngine;
    private readonly IEnumerable<IFileAdapter> _fileAdapters;
    private readonly RibbonKeyTipMode _ribbonKeyTipMode = new();
    private RibbonKeyTipScope _ribbonKeyTipScope = RibbonKeyTipScope.None;
    private string _ribbonKeyTipSequence = "";
    private ContextMenu? _activeRibbonKeyTipMenu;
    private bool _pendingStandaloneAltKeyTip;
    private readonly WorkbookRef _workbookRef;
    private Workbook _workbook;
    private SheetId _currentSheetId;
    private readonly System.Collections.ObjectModel.ObservableCollection<SheetTabViewModel> _sheetTabs = [];
    private readonly HashSet<SheetId> _groupedSheetIds = [];
    private SheetId? _sheetGroupAnchor;
    private SheetId? _dragSheetTabId;
    private System.Windows.Point _dragSheetTabStart;
    private bool _suppressToolbarSync;
    private bool _suppressViewOptionSync;
    private bool _suppressAppViewOptionSync;
    private bool _isOpeningFile;
    private CellAddress? _selectionAnchor;
    private CellAddress? _selectionCursor;
    private bool _dragSelectActive;
    private Freexcel.App.UI.SplitPaneRegion _activeSplitPaneRegion = Freexcel.App.UI.SplitPaneRegion.BottomRight;
    private readonly Dictionary<SheetId, SplitPaneViewportOffsets> _splitPaneViewportOffsets = [];
    private readonly List<FormulaTraceArrow> _formulaTraceArrows = [];
    private readonly RecentFilesStore _recentFiles;
    private readonly IWorkbookShareService _shareService = new WindowsWorkbookShareService();
    private List<RecentFileViewModel> _allRecentItems = [];
    private FreexcelOptions _options = FreexcelOptions.Load();
    private string? _currentFilePath;
    private XlsxFeatureReport? _currentXlsxFeatureReport;
    private bool _formatPainterActive;
    private StyleId _formatPainterStyleId;
    private double _zoomLevel = 1.0;
    private bool _snapInProgress;
    private bool _suppressZoomSync;
    private bool _formulaBarExpanded;
    private bool _ribbonCompact;
    private System.Windows.Controls.TextBox? _inlineEditor;
    private System.Windows.Controls.ComboBox? _validationDropdown;
    private WatchWindowDialog? _watchWindowDialog;
    private bool _suppressValidationDropdownCommit;
    private ColumnResizeSnapshot? _columnResizeSnapshot;
    private RowResizeSnapshot? _rowResizeSnapshot;
    private Action<CommandOutcome>? _repeatPostAction;
    private string? _pivotChartContextFieldCaption;
    private bool _slicerTimelinePaneDismissed;

    private record InternalClipboard(GridRange SourceRange, List<(CellAddress Source, Cell Cell)> Cells, bool IsCut = false);
    private sealed record PivotFieldListItem(string Caption, bool IsChecked);
    private sealed record SlicerPaneItem(string Name, string FieldName, IReadOnlyList<SlicerTileItem> Tiles);
    private sealed record SlicerTileItem(string SlicerName, string Caption, bool IsSelected);
    private sealed class TimelinePaneItem
    {
        public string Name { get; init; } = "";
        public string FieldName { get; init; } = "";
        public string SelectedStartDate { get; set; } = "";
        public string SelectedEndDate { get; set; } = "";
    }
    private InternalClipboard? _internalClipboard;
    private sealed record ColumnResizeSnapshot(SheetId SheetId, uint StartCol, uint EndCol, Dictionary<uint, (bool Had, double Width)> Widths);
    private sealed record RowResizeSnapshot(SheetId SheetId, uint StartRow, uint EndRow, Dictionary<uint, (bool Had, double Height)> Heights);
    private sealed class FailedWorkbookCommand(string message) : IWorkbookCommand
    {
        public string Label => "Unavailable";
        public CommandOutcome Apply(ICommandContext ctx) => new(false, message);
        public void Revert(ICommandContext ctx) { }
    }

    public MainWindow(
        ILogger<MainWindow> logger,
        IViewportService viewportService,
        ICommandBus commandBus,
        RecalcEngine recalcEngine,
        IEnumerable<IFileAdapter> fileAdapters,
        WorkbookRef workbookRef,
        Workbook workbook)
    {
        _logger = logger;
        _viewportService = viewportService;
        _commandBus = commandBus;
        _recalcEngine = recalcEngine;
        _fileAdapters = fileAdapters;
        _workbookRef = workbookRef;
        _workbook = workbook;
        _recentFiles = RecentFilesStore.Load();

        InitializeComponent();

        _currentSheetId = _workbook.Sheets[0].Id;
        SheetTabsControl.ItemsSource = _sheetTabs;
        
        // Wire up scrollbars
        VerticalScroll.ValueChanged += Scroll_ValueChanged;
        HorizontalScroll.ValueChanged += Scroll_ValueChanged;
        VerticalScroll.Scroll += VerticalScroll_Scroll;
        HorizontalScroll.Scroll += HorizontalScroll_Scroll;
        VerticalScroll.PreviewMouseLeftButtonDown += ScrollBar_PreviewMouseLeftButtonDown;
        HorizontalScroll.PreviewMouseLeftButtonDown += ScrollBar_PreviewMouseLeftButtonDown;
        
        // Wire up grid interactions
        SheetGrid.MouseDown += SheetGrid_MouseDown;
        SheetGrid.ColumnResized  += OnColumnResized;
        SheetGrid.RowResized     += OnRowResized;
        SheetGrid.ColumnResizing += OnColumnResizing;
        SheetGrid.RowResizing    += OnRowResizing;
        SheetGrid.AutofillRequested += OnAutofillRequested;
        SheetGrid.ContextMenuRequested += OnGridContextMenuRequested;
        SheetGrid.PivotChartFieldButtonRequested += OnPivotChartFieldButtonRequested;
        SheetGrid.PageMarginsChanged += OnPageMarginsChanged;
        SheetGrid.SplitDividerMoved += OnSplitDividerMoved;
        SheetGrid.SplitPaneScrollbarScrolled += OnSplitPaneScrollbarScrolled;
        SheetGrid.MouseMove  += SheetGrid_MouseMove;
        SheetGrid.MouseUp    += SheetGrid_MouseUp;
        SheetGrid.MouseWheel += SheetGrid_MouseWheel;
        this.KeyDown += MainWindow_KeyDown;
        this.KeyUp += MainWindow_KeyUp;
        this.Deactivated += MainWindow_Deactivated;
        this.TextInput += MainWindow_TextInput;
        
        Loaded += MainWindow_Loaded;
        SizeChanged += MainWindow_SizeChanged;
        StateChanged += (_, _) =>
        {
            UpdateMaximizedContentInset();
            if (MaxRestoreBtn != null)
                MaxRestoreBtn.Content = WindowState == WindowState.Maximized ? "" : "";
        };

        _logger.LogInformation("MainWindow initialized with Workbook {WorkbookId}", _workbook.Id);
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateMaximizedContentInset();

        // Populate from installed Windows fonts
        var fonts = System.Windows.Media.Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        FontNameBox.ItemsSource = fonts;
        FontNameBox.SelectedItem = fonts.Contains("Calibri") ? "Calibri" : fonts[0];

        var sizes = new[] { "8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "36", "48", "72" };
        FontSizeBox.ItemsSource = sizes;
        FontSizeBox.SelectedItem = "11";

        var formats = new[] { "General", "Number (0.00)", "Currency ($#,##0.00)", "Percentage (0%)", "Date (yyyy-MM-dd)", "Time (HH:mm:ss)", "Text (@)" };
        NumberFormatBox.ItemsSource = formats;
        NumberFormatBox.SelectedIndex = 0;

        ApplyOptionsToView();
        NormalizeRibbonSurface(forceCompact: true);
        CreateNewWorkbook();
        UpdateViewport();
        RefreshSheetTabs();
        UpdateTitleBar();
    }

    private void ApplyOptionsToView()
    {
        SheetGrid.UseR1C1ReferenceStyle = _options.UseR1C1ReferenceStyle;
        _suppressAppViewOptionSync = true;
        try
        {
            if (ViewFormulaBarChk is not null)
                ViewFormulaBarChk.IsChecked = _options.ShowFormulaBar;
            if (FormulaBarBorder is not null)
                FormulaBarBorder.Visibility = _options.ShowFormulaBar ? Visibility.Visible : Visibility.Collapsed;
            _formulaBarExpanded = _options.FormulaBarExpanded;
            ApplyFormulaBarExpansion();
        }
        finally
        {
            _suppressAppViewOptionSync = false;
        }

        if (SheetGrid.SelectedRange is { } range)
        {
            CellAddressBox.Text = FormatRangeReference(range.Start, range.End);
            var sheet = _workbook.GetSheet(_currentSheetId);
            FormulaBar.Text = FormatFormulaBarText(sheet?.GetCell(range.Start), range.Start);
        }
    }

    private void RecalculateWorkbook()
    {
        _recalcEngine.RecalculateAllFormulas(_workbook);
    }

    private void RecalculateIfAutomatic(IReadOnlyList<CellAddress> changedCells)
    {
        if (_workbook.CalculationMode == WorkbookCalculationMode.Automatic)
            _recalcEngine.Recalculate(_workbook, changedCells);
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateRibbonCompactMode();
        UpdateViewport();
    }

    private void UpdateRibbonCompactMode(bool force = false)
    {
        if (RibbonTabs is null)
            return;

        var activePanel = GetActiveRibbonPanel();
        if (activePanel is null)
            return;

        RemoveRibbonCollapsedGroupButtons(activePanel);
        var groups = activePanel.Children
            .OfType<FrameworkElement>()
            .Where(e => e is not System.Windows.Shapes.Rectangle && !IsRibbonCollapsedGroupButton(e))
            .ToList();

        foreach (var group in groups)
        {
            group.Visibility = Visibility.Visible;
            SetRibbonGroupCompact(group, RibbonCompactLevel.Full);
        }

        var collapsedButtons = InsertRibbonCollapsedGroupButtons(activePanel, groups);

        activePanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var ribbonScrollViewer = FindVisualAncestor<ScrollViewer>(activePanel);
        var availableWidth = ribbonScrollViewer?.ActualWidth > 0
            ? ribbonScrollViewer.ActualWidth
            : ribbonScrollViewer?.ViewportWidth;
        if (availableWidth is null or <= 0)
            availableWidth = RibbonTabs.ActualWidth > 0 ? RibbonTabs.ActualWidth : activePanel.ActualWidth;

        var adaptiveGroups = groups.Select((group, index) => MeasureRibbonAdaptiveGroup(group, collapsedButtons[index])).ToList();
        var plannedStates = RibbonAdaptiveLayoutPlanner.Plan(availableWidth.Value, adaptiveGroups);
        var compacted = plannedStates.Any(state => state != RibbonAdaptiveGroupState.Full);

        for (var i = 0; i < groups.Count; i++)
        {
            collapsedButtons[i].Visibility = Visibility.Collapsed;
            groups[i].Visibility = Visibility.Visible;

            switch (plannedStates[i])
            {
                case RibbonAdaptiveGroupState.Full:
                    SetRibbonGroupCompact(groups[i], RibbonCompactLevel.Full);
                    break;
                case RibbonAdaptiveGroupState.SmallWithLabels:
                    SetRibbonGroupCompact(groups[i], RibbonCompactLevel.SmallWithLabels);
                    break;
                case RibbonAdaptiveGroupState.IconOnly:
                    SetRibbonGroupCompact(groups[i], RibbonCompactLevel.IconOnly);
                    break;
                case RibbonAdaptiveGroupState.Collapsed:
                    groups[i].Visibility = Visibility.Collapsed;
                    collapsedButtons[i].Visibility = Visibility.Visible;
                    break;
            }
        }

        _ribbonCompact = compacted;
    }

    private static RibbonAdaptiveGroup MeasureRibbonAdaptiveGroup(FrameworkElement group, Button collapsedButton)
    {
        var name = GetRibbonGroupName(group);
        var fullWidth = MeasureRibbonGroupWidth(group, RibbonCompactLevel.Full);
        var smallWidth = MeasureRibbonGroupWidth(group, RibbonCompactLevel.SmallWithLabels);
        var iconWidth = MeasureRibbonGroupWidth(group, RibbonCompactLevel.IconOnly);
        collapsedButton.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var collapsedWidth = Math.Max(48, collapsedButton.DesiredSize.Width);
        SetRibbonGroupCompact(group, RibbonCompactLevel.Full);

        return new RibbonAdaptiveGroup(name, fullWidth, smallWidth, iconWidth, collapsedWidth);
    }

    private static double MeasureRibbonGroupWidth(FrameworkElement group, RibbonCompactLevel level)
    {
        SetRibbonGroupCompact(group, level);
        group.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Max(0, group.DesiredSize.Width);
    }

    private static List<Button> InsertRibbonCollapsedGroupButtons(StackPanel panel, IReadOnlyList<FrameworkElement> groups)
    {
        var buttons = new List<Button>(groups.Count);
        foreach (var group in groups)
        {
            var button = CreateRibbonCollapsedGroupButton(group);
            var index = panel.Children.IndexOf(group);
            panel.Children.Insert(index + 1, button);
            buttons.Add(button);
        }

        return buttons;
    }

    private static void RemoveRibbonCollapsedGroupButtons(StackPanel panel)
    {
        for (var i = panel.Children.Count - 1; i >= 0; i--)
        {
            if (panel.Children[i] is FrameworkElement element && IsRibbonCollapsedGroupButton(element))
                panel.Children.RemoveAt(i);
        }
    }

    private static bool IsRibbonCollapsedGroupButton(FrameworkElement element) =>
        element.Tag is string tag && string.Equals(tag, "RibbonCollapsedGroupButton", StringComparison.Ordinal);

    private static Button CreateRibbonCollapsedGroupButton(FrameworkElement group)
    {
        var groupName = GetRibbonGroupName(group);
        var icon = RibbonCommandPresentationPlanner.GetGroupIcon(groupName);
        var button = new Button
        {
            Tag = "RibbonCollapsedGroupButton",
            Width = 56,
            Height = 64,
            Margin = new Thickness(1, 0, 3, 0),
            Padding = new Thickness(3, 2, 3, 2),
            Visibility = Visibility.Collapsed,
            ContextMenu = CreateCollapsedRibbonGroupMenu(group),
            Content = new StackPanel
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = icon.Glyph,
                        Tag = "RibbonIcon",
                        FontFamily = icon.FontFamily,
                        FontSize = 22,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        TextAlignment = TextAlignment.Center,
                        Margin = new Thickness(0, 2, 0, 1)
                    },
                    new TextBlock
                    {
                        Text = groupName,
                        Tag = "RibbonLabel",
                        FontSize = 10,
                        TextWrapping = TextWrapping.Wrap,
                        TextAlignment = TextAlignment.Center,
                        MaxWidth = 52,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = "\uE70D",
                        Tag = "RibbonIcon",
                        FontFamily = new FontFamily("Segoe MDL2 Assets"),
                        FontSize = 8,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                    }
                }
            }
        };

        button.SetResourceReference(StyleProperty, "RibbonTallButton");
        RibbonTooltip.SetTitle(button, groupName);
        RibbonTooltip.SetDescription(button, $"Show the {groupName} commands.");
        RibbonTooltip.SetKeyTip(button, CreateGroupKeyTip(groupName));
        button.Click += (_, _) =>
        {
            if (button.ContextMenu is null)
                return;

            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = PlacementMode.Bottom;
            button.ContextMenu.IsOpen = true;
        };
        return button;
    }

    private static ContextMenu CreateCollapsedRibbonGroupMenu(FrameworkElement group)
    {
        var menu = new ContextMenu();
        var added = new HashSet<ButtonBase>();

        foreach (var button in EnumerateVisualDescendants(group).OfType<ButtonBase>())
        {
            if (!added.Add(button) || FindVisualAncestor<ButtonBase>(button) is { } ancestor && !ReferenceEquals(ancestor, button))
                continue;

            if (CreateMenuItemForRibbonButton(button) is { } item)
                menu.Items.Add(item);
        }

        if (menu.Items.Count == 0)
        {
            menu.Items.Add(new MenuItem
            {
                Header = GetRibbonGroupName(group),
                IsEnabled = false
            });
        }

        return menu;
    }

    private static MenuItem? CreateMenuItemForRibbonButton(ButtonBase button)
    {
        var title = RibbonTooltip.GetTitle(button);
        if (string.IsNullOrWhiteSpace(title))
            title = button.Content as string;
        if (string.IsNullOrWhiteSpace(title))
            title = button.Name;
        if (string.IsNullOrWhiteSpace(title))
            return null;

        var item = new MenuItem
        {
            Header = title,
            IsEnabled = button.IsEnabled
        };

        var keyTip = RibbonTooltip.GetKeyTip(button);
        if (!string.IsNullOrWhiteSpace(keyTip))
            RibbonTooltip.SetKeyTip(item, keyTip);

        if (button.ContextMenu is { Items.Count: > 0 } contextMenu)
        {
            foreach (var child in contextMenu.Items)
            {
                if (CloneRibbonMenuItem(child) is { } childItem)
                    item.Items.Add(childItem);
            }
        }
        else
        {
            item.Click += (_, _) => InvokeRibbonButton(button);
        }

        return item;
    }

    private static object? CloneRibbonMenuItem(object source)
    {
        if (source is Separator)
            return new Separator();

        if (source is not MenuItem sourceItem)
            return null;

        var item = new MenuItem
        {
            Header = sourceItem.Header,
            IsEnabled = sourceItem.IsEnabled,
            InputGestureText = sourceItem.InputGestureText
        };

        var keyTip = RibbonTooltip.GetKeyTip(sourceItem);
        if (!string.IsNullOrWhiteSpace(keyTip))
            RibbonTooltip.SetKeyTip(item, keyTip);

        foreach (var child in sourceItem.Items)
        {
            if (CloneRibbonMenuItem(child) is { } childItem)
                item.Items.Add(childItem);
        }

        item.Click += (_, args) =>
        {
            if (args.OriginalSource is MenuItem original && original.Items.Count > 0)
                return;

            sourceItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, sourceItem));
        };

        return item;
    }

    private static void InvokeRibbonButton(ButtonBase button)
    {
        if (button is ToggleButton toggleButton)
            toggleButton.IsChecked = toggleButton.IsChecked != true;

        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
    }

    private static string GetRibbonGroupName(FrameworkElement group)
    {
        var label = EnumerateVisualDescendants(group)
            .OfType<TextBlock>()
            .LastOrDefault(textBlock => FindVisualAncestor<Border>(textBlock) is not null &&
                                        FindVisualAncestor<ButtonBase>(textBlock) is null);

        return string.IsNullOrWhiteSpace(label?.Text) ? "Commands" : label.Text.Trim();
    }

    private static string CreateGroupKeyTip(string groupName)
    {
        var letters = new string(groupName.Where(char.IsLetterOrDigit).Take(2).ToArray());
        return string.IsNullOrWhiteSpace(letters) ? "G" : letters.ToUpperInvariant();
    }

    private StackPanel? GetActiveRibbonPanel()
    {
        if (RibbonTabs.SelectedItem is not TabItem tabItem)
            return null;

        if (string.Equals(tabItem.Header?.ToString(), "Home", StringComparison.Ordinal) &&
            HomeRibbonPanel is not null)
        {
            return HomeRibbonPanel;
        }

        return EnumerateVisualDescendants(tabItem)
            .OfType<StackPanel>()
            .OrderByDescending(panel => panel.Children.OfType<Grid>().Count())
            .FirstOrDefault(panel => panel.Orientation == Orientation.Horizontal &&
                                     panel.Children.OfType<Grid>().Count() >= 2);
    }

    private enum RibbonCompactLevel
    {
        Full,
        SmallWithLabels,
        IconOnly
    }

    private static void SetRibbonGroupCompact(FrameworkElement group, RibbonCompactLevel level)
    {
        foreach (var element in EnumerateVisualDescendants(group).OfType<FrameworkElement>())
        {
            if (element is TextBlock { Tag: string labelTag } label &&
                string.Equals(labelTag, "RibbonLabel", StringComparison.Ordinal))
            {
                label.Visibility = level == RibbonCompactLevel.IconOnly ? Visibility.Collapsed : Visibility.Visible;
                continue;
            }

            if (element is ButtonBase button)
            {
                if (button.Tag is string tag &&
                    RibbonCommandPresentationPlanner.TryParseCompactWidths(tag, out var fullWidth, out var compactWidth))
                {
                    button.Width = level switch
                    {
                        RibbonCompactLevel.Full => fullWidth,
                        RibbonCompactLevel.SmallWithLabels => Math.Max(compactWidth + 28, Math.Ceiling(fullWidth * 0.72)),
                        _ => compactWidth
                    };
                }

                SetRibbonButtonCompact(button, level);
            }
        }
    }

    private static void SetRibbonButtonCompact(ButtonBase button, RibbonCompactLevel level)
    {
        foreach (var textBlock in EnumerateVisualDescendants(button).OfType<TextBlock>())
        {
            if (IsRibbonButtonLabel(textBlock))
                textBlock.Visibility = level == RibbonCompactLevel.IconOnly ? Visibility.Collapsed : Visibility.Visible;
        }

        button.HorizontalContentAlignment = level == RibbonCompactLevel.IconOnly
            ? System.Windows.HorizontalAlignment.Right
            : System.Windows.HorizontalAlignment.Center;

        if (button.Content is FrameworkElement content)
            content.HorizontalAlignment = level == RibbonCompactLevel.IconOnly
                ? System.Windows.HorizontalAlignment.Right
                : System.Windows.HorizontalAlignment.Center;

        foreach (var stack in EnumerateVisualDescendants(button).OfType<StackPanel>())
        {
            if (stack.Orientation == Orientation.Horizontal)
                stack.HorizontalAlignment = level == RibbonCompactLevel.IconOnly
                    ? System.Windows.HorizontalAlignment.Right
                    : System.Windows.HorizontalAlignment.Center;
        }
    }

    private static bool IsRibbonButtonLabel(TextBlock textBlock)
    {
        if (textBlock.Tag is string tag)
        {
            if (string.Equals(tag, "RibbonLabel", StringComparison.Ordinal))
                return true;
            if (string.Equals(tag, "RibbonIcon", StringComparison.Ordinal))
                return false;
        }

        var text = textBlock.Text?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length <= 1)
            return false;

        var fontFamily = textBlock.FontFamily?.Source ?? "";
        if (fontFamily.Contains("MDL2", StringComparison.OrdinalIgnoreCase) ||
            fontFamily.Contains("Symbol", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return FindVisualAncestor<ButtonBase>(textBlock) is not null;
    }

    private static T? FindVisualAncestor<T>(DependencyObject element)
        where T : DependencyObject
    {
        var current = element;
        while (current is not null)
        {
            if (current is T match)
                return match;

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void NormalizeRibbonCommandButtons()
    {
        if (RibbonTabs is null)
            return;

        foreach (var button in EnumerateVisualDescendants(RibbonTabs).OfType<Button>())
        {
            if (button.Content is not string label || string.IsNullOrWhiteSpace(label))
                continue;

            var title = RibbonTooltip.GetTitle(button);
            var commandName = string.IsNullOrWhiteSpace(title) ? label : title;
            var layoutKind = RibbonCommandPresentationPlanner.GetLayoutKind(commandName, label);
            ApplyRibbonCommandSize(button, layoutKind);
            var fullWidth = button.Width is > 0 ? button.Width : Math.Max(button.ActualWidth, 64);
            var compactWidth = layoutKind is RibbonCommandLayoutKind.Large or RibbonCommandLayoutKind.Medium ? 38 : 30;
            button.Tag ??= $"RibbonCompact:{fullWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)}:{compactWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

            button.Content = CreateRibbonCommandContent(commandName, label, layoutKind);
            button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center;
        }

        NormalizeRibbonCommandGroups();
    }

    private void NormalizeRibbonSurface(bool forceCompact = false)
    {
        NormalizeRibbonCommandButtons();
        ConfigureInsertRibbonSurface();
        AlignRibbonIconColumns();
        ApplyToolbarDropdownWhiteBackgrounds();
        UpdateRibbonCompactMode(force: forceCompact);
    }

    private void NormalizeRibbonSurfaceAfterTabSelection()
    {
        Dispatcher.BeginInvoke((Action)(() => NormalizeRibbonSurface(forceCompact: true)));
    }

    private void ConfigureInsertRibbonSurface()
    {
        var insertTab = RibbonTabs?.Items
            .OfType<TabItem>()
            .FirstOrDefault(item => string.Equals(item.Header?.ToString(), "Insert", StringComparison.Ordinal));

        if (insertTab is null)
            return;

        foreach (var button in EnumerateVisualDescendants(insertTab).OfType<Button>())
        {
            var title = RibbonTooltip.GetTitle(button) ?? "";
            if (RibbonCommandPresentationPlanner.ShouldHideFromInsertRibbon(title))
                button.Visibility = Visibility.Collapsed;
        }
    }

    private void AlignRibbonIconColumns()
    {
        if (RibbonTabs is null)
            return;

        foreach (var stack in EnumerateVisualDescendants(RibbonTabs).OfType<StackPanel>())
        {
            if (stack.Orientation != Orientation.Horizontal || stack.Children.Count < 2)
                continue;

            if (stack.Children[0] is not FrameworkElement icon || stack.Children[1] is not TextBlock)
                continue;

            if (FindVisualAncestor<ButtonBase>(stack) is null)
                continue;

            icon.Width = Math.Max(icon.Width is > 0 ? icon.Width : 0, 18);
            icon.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
            icon.Margin = new Thickness(0, icon.Margin.Top, 4, icon.Margin.Bottom);
            stack.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
        }
    }

    private void ApplyToolbarDropdownWhiteBackgrounds()
    {
        if (RibbonTabs is null)
            return;

        foreach (var comboBox in EnumerateVisualDescendants(RibbonTabs).OfType<ComboBox>())
        {
            comboBox.Background = Brushes.White;
            comboBox.Foreground = Brushes.Black;
            comboBox.Resources[SystemColors.WindowBrushKey] = Brushes.White;
            comboBox.Resources[SystemColors.ControlBrushKey] = Brushes.White;
            comboBox.Resources[SystemColors.MenuBrushKey] = Brushes.White;
            comboBox.DropDownOpened -= ToolbarComboBox_DropDownOpened;
            comboBox.DropDownOpened += ToolbarComboBox_DropDownOpened;
        }
    }

    private static void ToolbarComboBox_DropDownOpened(object? sender, EventArgs e)
    {
        if (sender is not ComboBox comboBox)
            return;

        comboBox.Dispatcher.BeginInvoke((Action)(() =>
        {
            comboBox.ApplyTemplate();
            if (comboBox.Template.FindName("PART_Popup", comboBox) is not Popup popup ||
                popup.Child is not DependencyObject popupRoot)
            {
                return;
            }

            ForceDropdownWhite(popupRoot);
        }));
    }

    private static void ForceDropdownWhite(DependencyObject root)
    {
        if (root is Control control)
        {
            control.Background = Brushes.White;
            control.Foreground = Brushes.Black;
        }
        else if (root is Border border)
        {
            border.Background = Brushes.White;
        }
        else if (root is Panel panel)
        {
            panel.Background = Brushes.White;
        }

        if (root is ComboBoxItem item)
        {
            item.Background = Brushes.White;
            item.Foreground = Brushes.Black;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
            ForceDropdownWhite(VisualTreeHelper.GetChild(root, i));
    }

    private static FrameworkElement CreateRibbonCommandContent(string commandName, string label, RibbonCommandLayoutKind layoutKind)
    {
        var tall = layoutKind is RibbonCommandLayoutKind.Large or RibbonCommandLayoutKind.Medium;
        var icon = RibbonCommandPresentationPlanner.GetIcon(commandName);
        var iconBlock = new TextBlock
        {
            Text = icon.Glyph,
            Tag = "RibbonIcon",
            FontFamily = icon.FontFamily,
            FontSize = layoutKind switch
            {
                RibbonCommandLayoutKind.Large => 24,
                RibbonCommandLayoutKind.Medium => 20,
                _ => 12
            },
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = tall ? new Thickness(0, 2, 0, 2) : new Thickness(0, 0, 3, 0)
        };

        var labelBlock = new TextBlock
        {
            Text = label,
            Tag = "RibbonLabel",
            FontSize = 10,
            TextWrapping = tall ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MaxWidth = tall ? 72 : double.PositiveInfinity,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };

        if (tall)
        {
            return new StackPanel
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Children =
                {
                    iconBlock,
                    labelBlock
                }
            };
        }

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Children =
            {
                iconBlock,
                labelBlock
            }
        };
    }

    private static void ApplyRibbonCommandSize(Button button, RibbonCommandLayoutKind layoutKind)
    {
        switch (layoutKind)
        {
            case RibbonCommandLayoutKind.Large:
                button.Width = Math.Max(button.Width is > 0 ? button.Width : 0, 74);
                button.Height = 64;
                button.Padding = new Thickness(3, 2, 3, 2);
                button.VerticalAlignment = System.Windows.VerticalAlignment.Center;
                break;
            case RibbonCommandLayoutKind.Medium:
                button.Width = Math.Max(button.Width is > 0 ? button.Width : 0, 58);
                button.Height = 50;
                button.Padding = new Thickness(3, 2, 3, 2);
                button.VerticalAlignment = System.Windows.VerticalAlignment.Center;
                break;
            default:
                button.Height = button.Height is > 0 ? button.Height : 22;
                button.Padding = new Thickness(3, 1, 3, 1);
                break;
        }
    }

    private void NormalizeRibbonCommandGroups()
    {
        if (RibbonTabs is null)
            return;

        foreach (var panel in EnumerateVisualDescendants(RibbonTabs).OfType<StackPanel>())
        {
            if (panel == HomeRibbonPanel)
                continue;

            var directButtons = panel.Children.OfType<Button>().ToList();
            if (directButtons.Count == 0)
                continue;

            var tallButtonCount = directButtons.Count(b => b.Height >= 46);
            if (tallButtonCount == 0)
                continue;

            panel.Orientation = Orientation.Horizontal;
            panel.VerticalAlignment = System.Windows.VerticalAlignment.Center;
        }

        foreach (var grid in EnumerateVisualDescendants(RibbonTabs).OfType<UniformGrid>())
        {
            var directButtons = grid.Children.OfType<Button>().ToList();
            if (directButtons.Count == 0 || directButtons.All(b => b.Height < 46))
                continue;

            grid.Rows = 1;
            grid.Columns = directButtons.Count;
        }
    }

    private void Scroll_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateViewport();
    }

    private void VerticalScroll_Scroll(object sender, ScrollEventArgs e)
    {
        if (e.ScrollEventType == ScrollEventType.SmallIncrement)
            ExtendScrollRangeFromScrollbarArrow(VerticalScroll, GetScrollableRowLimit(_workbook.GetSheet(_currentSheetId)));
    }

    private void HorizontalScroll_Scroll(object sender, ScrollEventArgs e)
    {
        if (e.ScrollEventType == ScrollEventType.SmallIncrement)
            ExtendScrollRangeFromScrollbarArrow(HorizontalScroll, GetScrollableColumnLimit(_workbook.GetSheet(_currentSheetId)));
    }

    private void ScrollBar_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ScrollBar scrollBar ||
            e.OriginalSource is not DependencyObject source ||
            FindVisualAncestor<RepeatButton>(source) is not { } button)
            return;

        var isForwardLineButton =
            scrollBar.Orientation == Orientation.Vertical && Equals(button.Command, ScrollBar.LineDownCommand) ||
            scrollBar.Orientation == Orientation.Horizontal && Equals(button.Command, ScrollBar.LineRightCommand);
        if (!isForwardLineButton)
            return;

        var sheet = _workbook.GetSheet(_currentSheetId);
        var absoluteLimit = scrollBar.Orientation == Orientation.Vertical
            ? GetScrollableRowLimit(sheet)
            : GetScrollableColumnLimit(sheet);
        if (!TryExtendScrollRangeFromScrollbarArrow(scrollBar, absoluteLimit))
            return;

        e.Handled = true;
    }

    private static void ExtendScrollRangeFromScrollbarArrow(ScrollBar scrollBar, uint absoluteLimit)
    {
        TryExtendScrollRangeFromScrollbarArrow(scrollBar, absoluteLimit);
    }

    private static bool TryExtendScrollRangeFromScrollbarArrow(ScrollBar scrollBar, uint absoluteLimit)
    {
        var (maximum, value) = CalculateScrollbarArrowSmallIncrement(
            scrollBar.Value,
            scrollBar.Maximum,
            scrollBar.SmallChange,
            scrollBar.ViewportSize,
            absoluteLimit);
        if (maximum <= scrollBar.Maximum && value <= scrollBar.Value)
            return false;

        scrollBar.Maximum = maximum;
        scrollBar.Value = value;
        return true;
    }

    private string FormatCellReference(CellAddress address) =>
        SpreadsheetDisplayFormatter.FormatCellReference(address, _options.UseR1C1ReferenceStyle);

    private string FormatColumnReference(uint column) =>
        SpreadsheetDisplayFormatter.FormatColumnReference(column, _options.UseR1C1ReferenceStyle);

    private string FormatRangeReference(CellAddress start, CellAddress end) =>
        SpreadsheetDisplayFormatter.FormatRangeReference(start, end, _options.UseR1C1ReferenceStyle);

    private string FormatFormulaBarText(Cell? cell, CellAddress address) =>
        SpreadsheetDisplayFormatter.FormatFormulaBarText(cell, address, _options.UseR1C1ReferenceStyle);

    // ── Header / select-all helpers ───────────────────────────────────────────

    private void SelectRow(uint row)
    {
        HideValidationDropdown();
        const uint maxCol = 16_384;
        _selectionAnchor = new CellAddress(_currentSheetId, row, 1);
        _selectionCursor = new CellAddress(_currentSheetId, row, maxCol);
        SheetGrid.SelectedRanges = null;
        SheetGrid.SelectedRange = new GridRange(_selectionAnchor.Value, _selectionCursor.Value);
        CellAddressBox.Text = $"{row}:{row}";
        var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(_selectionAnchor.Value);
        FormulaBar.Text = FormatFormulaBarText(cell, _selectionAnchor.Value);
        SheetGrid.Focus();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private void SelectColumn(uint col)
    {
        HideValidationDropdown();
        const uint maxRow = 1_048_576;
        _selectionAnchor = new CellAddress(_currentSheetId, 1, col);
        _selectionCursor = new CellAddress(_currentSheetId, maxRow, col);
        SheetGrid.SelectedRanges = null;
        SheetGrid.SelectedRange = new GridRange(_selectionAnchor.Value, _selectionCursor.Value);
        var colName = FormatColumnReference(col);
        CellAddressBox.Text = $"{colName}:{colName}";
        var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(_selectionAnchor.Value);
        FormulaBar.Text = FormatFormulaBarText(cell, _selectionAnchor.Value);
        SheetGrid.Focus();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private void SelectAll()
    {
        HideValidationDropdown();
        const uint maxRow = 1_048_576;
        const uint maxCol = 16_384;
        _selectionAnchor = new CellAddress(_currentSheetId, 1, 1);
        _selectionCursor = new CellAddress(_currentSheetId, maxRow, maxCol);
        SheetGrid.SelectedRanges = null;
        SheetGrid.SelectedRange = new GridRange(_selectionAnchor.Value, _selectionCursor.Value);
        CellAddressBox.Text = FormatCellReference(_selectionAnchor.Value);
        var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(_selectionAnchor.Value);
        FormulaBar.Text = FormatFormulaBarText(cell, _selectionAnchor.Value);
        SheetGrid.Focus();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private void SheetGrid_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(SheetGrid);
        const double colHeaderH = Freexcel.App.UI.GridView.ColHeaderHeight;
        double rowHeaderW = SheetGrid.ActualRowHeaderWidth;

        var viewport = SheetGrid.Viewport;
        if (viewport == null) return;

        // ── Header area ───────────────────────────────────────────────────────
        if (pos.X < rowHeaderW || pos.Y < colHeaderH)
        {
            // Top-left corner: select all
            if (pos.X < rowHeaderW && pos.Y < colHeaderH)
            {
                SelectAll();
                return;
            }
            // Column header: select entire column
            if (pos.Y < colHeaderH)
            {
                foreach (var cm in viewport.ColMetrics)
                {
                    double left = cm.LeftOffset + rowHeaderW;
                    if (pos.X >= left && pos.X < left + cm.Width)
                    {
                        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && _selectionAnchor.HasValue)
                        {
                            uint anchorCol = _selectionAnchor.Value.Col;
                            _selectionCursor = new CellAddress(_currentSheetId, 1_048_576, cm.Col);
                            SheetGrid.SelectedRange = new GridRange(
                                new CellAddress(_currentSheetId, 1, Math.Min(anchorCol, cm.Col)),
                                new CellAddress(_currentSheetId, 1_048_576, Math.Max(anchorCol, cm.Col)));
                            var c1 = FormatColumnReference(Math.Min(anchorCol, cm.Col));
                            var c2 = FormatColumnReference(Math.Max(anchorCol, cm.Col));
                            CellAddressBox.Text = c1 == c2 ? $"{c1}:{c1}" : $"{c1}:{c2}";
                        }
                        else
                        {
                            SelectColumn(cm.Col);
                        }
                        return;
                    }
                }
                return;
            }
            // Row header: select entire row
            foreach (var rm in viewport.RowMetrics)
            {
                double top = rm.TopOffset + colHeaderH;
                if (pos.Y >= top && pos.Y < top + rm.Height)
                {
                    if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && _selectionAnchor.HasValue)
                    {
                        uint anchorRow = _selectionAnchor.Value.Row;
                        _selectionCursor = new CellAddress(_currentSheetId, rm.Row, 16_384);
                        SheetGrid.SelectedRange = new GridRange(
                            new CellAddress(_currentSheetId, Math.Min(anchorRow, rm.Row), 1),
                            new CellAddress(_currentSheetId, Math.Max(anchorRow, rm.Row), 16_384));
                        var r1 = Math.Min(anchorRow, rm.Row);
                        var r2 = Math.Max(anchorRow, rm.Row);
                        CellAddressBox.Text = r1 == r2 ? $"{r1}:{r1}" : $"{r1}:{r2}";
                    }
                    else
                    {
                        SelectRow(rm.Row);
                    }
                    return;
                }
            }
            return;
        }

        // ── Cell area ─────────────────────────────────────────────────────────

        if (_formulaTraceArrows.Count > 0 &&
            Freexcel.App.UI.GridView.HitTestFormulaTraceMarker(viewport, _formulaTraceArrows, _currentSheetId, pos) is { } traceTarget)
        {
            NavigateToCell(traceTarget);
            RefreshSheetTabs();
            RefreshToolbar();
            RefreshStatusBar();
            e.Handled = true;
            return;
        }

        _activeSplitPaneRegion = Freexcel.App.UI.GridView.HitTestSplitPaneRegion(viewport, pos);
        var hitAddress = Freexcel.App.UI.GridView.HitTestViewportCell(viewport, _currentSheetId, pos);
        if (hitAddress is { } newAddr)
        {
            if (_inlineEditor?.IsVisible == true)
            {
                FormulaBar.Text = _inlineEditor.Text;
                var committed = CommitEdit();
                HideInlineEditor(commit: false);
                if (!committed)
                {
                    e.Handled = true;
                    return;
                }
            }

            if (TryApplyFormatPainter(newAddr))
            {
                e.Handled = true;
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && _selectionAnchor.HasValue)
            {
                ExtendSelection(_selectionAnchor.Value, newAddr);
            }
            else
            {
                SetActiveCell(newAddr);
                if (e.ClickCount == 2)
                {
                    if (!TryShowPivotTableDetails(showMessage: false))
                        EnterEditMode();
                    e.Handled = true;
                }
                else
                {
                    // Start drag-select
                    _dragSelectActive = true;
                    SheetGrid.CaptureMouse();
                }
            }
        }
    }

    private void MainWindow_TextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        // Don't steal input from text boxes or combo boxes (formula bar, toolbar dropdowns)
        if (Keyboard.FocusedElement is TextBox or ComboBox) return;
        if (SheetGrid.SelectedRange == null) return;
        if (string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0) return;

        if (_selectionAnchor.HasValue)
        {
            ShowInlineEditor(_selectionAnchor.Value);
            if (_inlineEditor != null)
            {
                _inlineEditor.Text = e.Text;
                _inlineEditor.CaretIndex = _inlineEditor.Text.Length;
            }
        }
        e.Handled = true;
    }

    private void MainWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is not TextBox and not ComboBox)
        {
            var keyTipKey = GetEffectiveKey(e);
            if (IsStandaloneAltKey(keyTipKey) && _ribbonKeyTipMode.IsActive)
            {
                _pendingStandaloneAltKeyTip = true;
                e.Handled = true;
                return;
            }

            if (_ribbonKeyTipMode.IsActive && Keyboard.Modifiers == ModifierKeys.None)
            {
                HandleActiveRibbonKeyTip(keyTipKey);
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Alt && IsStandaloneAltKey(keyTipKey))
            {
                _pendingStandaloneAltKeyTip = true;
                e.Handled = true;
                return;
            }

            _pendingStandaloneAltKeyTip = false;

            if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
            {
                CancelCopyAndTransientModes();
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Alt &&
                RibbonKeyTipMode.ToKeyTipToken(keyTipKey) is { } keyTip &&
                TryHandleTopLevelRibbonKeyTip(keyTip))
            {
                EnterRibbonKeyTipMode(RibbonKeyTipScope.Commands);
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Alt &&
                RibbonKeyTipMode.ToKeyTipToken(keyTipKey) is { } qatKeyTip &&
                TryInvokeTopLevelQatKeyTip(qatKeyTip))
            {
                ExitRibbonKeyTipMode();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SaveButton_Click(sender, e);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control)
            {
                OpenButton_Click(sender, e);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
            {
                CreateNewWorkbook();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Control)
            {
                PrintButton_Click(sender, e);
                e.Handled = true;
                return;
            }
            if ((e.Key == Key.D1 || e.Key == Key.NumPad1) && Keyboard.Modifiers == ModifierKeys.Control)
            {
                OpenFormatCellsDialog();
                e.Handled = true;
                return;
            }
            if (KeyboardShortcutMatcher.TryGetCommandShortcut(e.Key, e.SystemKey, Keyboard.Modifiers, out var commandShortcut))
            {
                ExecuteCommandShortcut(commandShortcut, sender, e);
                e.Handled = true;
                return;
            }
            if (KeyboardShortcutMatcher.TryGetNumberFormatShortcut(e.Key, Keyboard.Modifiers, out var numberFormatShortcut))
            {
                ApplyNumberFormatShortcut(numberFormatShortcut);
                e.Handled = true;
                return;
            }
            if (KeyboardShortcutMatcher.TryGetBorderShortcut(e.Key, Keyboard.Modifiers, out var borderShortcut))
            {
                if (borderShortcut == BorderKeyboardShortcut.Outline)
                    ApplyOutlineBorderShortcut();
                else
                    ApplyStyleDiff(BorderShortcutService.GetClearBorderDiff());

                e.Handled = true;
                return;
            }
            if (e.Key == Key.Oem3 && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ShowFormulasBtn_Click(sender, e);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.L && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                FilterButton_Click(sender, e);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.PageUp && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ActivateAdjacentVisibleSheet(-1);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.PageDown && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ActivateAdjacentVisibleSheet(1);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.F11 && Keyboard.Modifiers == ModifierKeys.Shift)
            {
                AddSheetButton_Click(sender, e);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control)
            {
                FillDownMenuItem_Click(sender, e);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.R && Keyboard.Modifiers == ModifierKeys.Control)
            {
                FillRightMenuItem_Click(sender, e);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.E && Keyboard.Modifiers == ModifierKeys.Control)
            {
                TryFlashFill();
                e.Handled = true;
                return;
            }
            if ((e.Key == Key.D5 || e.Key == Key.NumPad5) && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ApplyFontToggleShortcut(FontToggleShortcut.Strikethrough, StrikeButton);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
            {
                InsertLinkBtn_Click(sender, e);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.OemSemicolon && Keyboard.Modifiers == ModifierKeys.Control)
            {
                InsertCurrentDateOrTime(insertTime: false);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.OemSemicolon && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                InsertCurrentDateOrTime(insertTime: true);
                e.Handled = true;
                return;
            }
            if ((e.SystemKey == Key.OemPlus || e.SystemKey == Key.Add) && Keyboard.Modifiers == ModifierKeys.Alt)
            {
                InsertAutoSumFormula("SUM");
                e.Handled = true;
                return;
            }
            if (e.SystemKey == Key.Right && Keyboard.Modifiers == (ModifierKeys.Alt | ModifierKeys.Shift))
            {
                GroupRowsBtn_Click(sender, e);
                e.Handled = true;
                return;
            }
            if (e.SystemKey == Key.Left && Keyboard.Modifiers == (ModifierKeys.Alt | ModifierKeys.Shift))
            {
                UngroupRowsBtn_Click(sender, e);
                e.Handled = true;
                return;
            }
            if (KeyboardShortcutMatcher.IsCtrlPlus(e.Key, e.SystemKey, Keyboard.Modifiers))
            {
                ExecuteKeyboardInsert();
                e.Handled = true;
                return;
            }
            if (KeyboardShortcutMatcher.IsCtrlMinus(e.Key, e.SystemKey, Keyboard.Modifiers))
            {
                ExecuteKeyboardDelete();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.F4 && Keyboard.Modifiers == ModifierKeys.None)
            {
                ExecuteRepeatLast();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.W && Keyboard.Modifiers == ModifierKeys.Control)
            {
                Close();
                e.Handled = true;
                return;
            }
            if ((e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None) ||
                (e.Key == Key.G && Keyboard.Modifiers == ModifierKeys.Control))
            {
                FindGoToMenuItem_Click(sender, e);
                e.Handled = true;
                return;
            }
        }

        if (KeyboardShortcutMatcher.TryGetFontToggleShortcut(e.Key, Keyboard.Modifiers, out var fontToggleShortcut))
        {
            var button = fontToggleShortcut switch
            {
                FontToggleShortcut.Bold => BoldButton,
                FontToggleShortcut.Italic => ItalicButton,
                _ => UnderlineButton
            };
            ApplyFontToggleShortcut(fontToggleShortcut, button);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            FindButton_Click(sender, e);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.H && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ReplaceButton_Click(sender, e);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            ExecuteCopy();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.X && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            ExecuteCopy(isCut: true);
            e.Handled = true;
            return;
        }
        if (KeyboardShortcutMatcher.IsPasteSpecialShortcut(e.Key, Keyboard.Modifiers))
        {
            PasteSpecialBtn_Click(sender, e);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ExecutePaste();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            SelectCurrentRegionOrAll();
            e.Handled = true;
            return;
        }
        if (KeyboardShortcutMatcher.TryGetSelectionShortcut(e.Key, Keyboard.Modifiers, out var selectionShortcut))
        {
            if (selectionShortcut == KeyboardSelectionShortcut.SelectAll)
                SelectAll();
            else
                SelectCurrentRegionOnly();

            e.Handled = true;
            return;
        }
        if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SelectWholeColumnsFromSelection();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Shift)
        {
            SelectWholeRowsFromSelection();
            e.Handled = true;
            return;
        }
        if (KeyboardShortcutMatcher.TryGetGridShortcut(e.Key, Keyboard.Modifiers, out var gridShortcut))
        {
            switch (gridShortcut)
            {
                case KeyboardGridShortcut.HideRows:
                    ExecuteRowsHidden(hidden: true);
                    break;
                case KeyboardGridShortcut.UnhideRows:
                    ExecuteRowsHidden(hidden: false);
                    break;
                case KeyboardGridShortcut.HideColumns:
                    ExecuteColumnsHidden(hidden: true);
                    break;
                case KeyboardGridShortcut.UnhideColumns:
                    ExecuteColumnsHidden(hidden: false);
                    break;
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            ExecuteUndo();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Y && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            ExecuteRedo();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && (Keyboard.Modifiers & ModifierKeys.Control) == 0
            && Keyboard.FocusedElement is not TextBox)
        {
            ExecuteClearSelection();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F2)
        {
            EnterEditMode();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Back && Keyboard.FocusedElement is not TextBox)
        {
            ExecuteClearSelection();
            EnterEditMode();
            e.Handled = true;
            return;
        }

        if (SheetGrid.SelectedRange == null) return;

        bool shiftHeld = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        bool ctrlHeld  = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        // When Shift is held the moving end is _selectionCursor; otherwise it's the active cell.
        var current = shiftHeld && _selectionCursor.HasValue
            ? _selectionCursor.Value
            : SheetGrid.SelectedRange.Value.Start;

        var sheet = _workbook.GetSheet(_currentSheetId);
        int pageSize = Math.Max(1, (SheetGrid.Viewport?.RowMetrics.Count ?? 25) - 1);

        CellAddress? target = e.Key switch
        {
            Key.Up    => ctrlHeld ? FindDataBoundaryCol(sheet, current.Row, current.Col, -1)
                                  : new CellAddress(_currentSheetId, current.Row > 1 ? current.Row - 1 : 1u, current.Col),
            Key.Down  => ctrlHeld ? FindDataBoundaryCol(sheet, current.Row, current.Col, +1)
                                  : new CellAddress(_currentSheetId, Math.Min(current.Row + 1, Freexcel.Core.Model.CellAddress.MaxRow), current.Col),
            Key.Left  => ctrlHeld ? FindDataBoundaryRow(sheet, current.Row, current.Col, -1)
                                  : new CellAddress(_currentSheetId, current.Row, current.Col > 1 ? current.Col - 1 : 1u),
            Key.Right => ctrlHeld ? FindDataBoundaryRow(sheet, current.Row, current.Col, +1)
                                  : new CellAddress(_currentSheetId, current.Row, Math.Min(current.Col + 1, Freexcel.Core.Model.CellAddress.MaxCol)),

            Key.Home     => new CellAddress(_currentSheetId, ctrlHeld ? 1u : current.Row, 1u),
            Key.End      => ctrlHeld ? (CellAddress?)CtrlEndCell(sheet) : null,
            Key.PageUp   => new CellAddress(_currentSheetId, (uint)Math.Max(1, (int)current.Row - pageSize), current.Col),
            Key.PageDown => new CellAddress(_currentSheetId, (uint)Math.Min(1_048_576, current.Row + (uint)pageSize), current.Col),

            Key.Enter => shiftHeld
                ? new CellAddress(_currentSheetId, current.Row > 1 ? current.Row - 1 : 1u, current.Col)
                : new CellAddress(_currentSheetId, Math.Min(current.Row + 1, Freexcel.Core.Model.CellAddress.MaxRow), current.Col),
            Key.Tab   => shiftHeld
                ? new CellAddress(_currentSheetId, current.Row, current.Col > 1 ? current.Col - 1 : 1u)
                : new CellAddress(_currentSheetId, current.Row, Math.Min(current.Col + 1, Freexcel.Core.Model.CellAddress.MaxCol)),
            _         => null
        };

        if (target == null) return;

        // Enter and Tab (including Shift variants) move the active cell; they don't extend selection
        bool moveOnly = e.Key is Key.Enter or Key.Tab;
        if (shiftHeld && !moveOnly && _selectionAnchor.HasValue)
            ExtendSelection(_selectionAnchor.Value, target.Value);
        else
            SetActiveCell(target.Value);

        EnsureCellVisible(target.Value);
        e.Handled = true;
    }

    private void ExecuteCommandShortcut(KeyboardCommandShortcut shortcut, object sender, RoutedEventArgs e)
    {
        switch (shortcut)
        {
            case KeyboardCommandShortcut.CreateTable:
                TableBtn_Click(sender, e);
                break;
            case KeyboardCommandShortcut.InsertFunction:
                InsertFunctionBtn_Click(sender, e);
                break;
            case KeyboardCommandShortcut.SpellCheck:
                SpellCheckBtn_Click(sender, e);
                break;
            case KeyboardCommandShortcut.CalculateNow:
                CalcNowBtn_Click(sender, e);
                break;
            case KeyboardCommandShortcut.CalculateSheet:
                CalcSheetBtn_Click(sender, e);
                break;
            case KeyboardCommandShortcut.ToggleFormulaBarExpansion:
                FormulaBarExpandBtn_Click(sender, e);
                break;
            case KeyboardCommandShortcut.QuickAnalysis:
                StatusReadyText.Text = "Quick Analysis is not available in Freexcel.";
                break;
            case KeyboardCommandShortcut.InsertEmbeddedChart:
            case KeyboardCommandShortcut.InsertChartSheet:
                ChartColumnMenuItem_Click(sender, e);
                break;
        }
    }

    private void MainWindow_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_pendingStandaloneAltKeyTip)
            return;

        var keyTipKey = GetEffectiveKey(e);
        if (!IsStandaloneAltKey(keyTipKey))
            return;

        _pendingStandaloneAltKeyTip = false;
        if (Keyboard.FocusedElement is TextBox or ComboBox)
            return;

        if (_ribbonKeyTipMode.IsActive)
            ExitRibbonKeyTipMode();
        else
            EnterRibbonKeyTipMode(RibbonKeyTipScope.TopLevel);

        e.Handled = true;
    }

    private void SetActiveCell(CellAddress addr)
    {
        // If the cell belongs to a merged region, select the whole region
        var sheet = _workbook.GetSheet(_currentSheetId);
        var merge = sheet?.GetMergeRegion(addr);
        if (merge.HasValue)
        {
            _selectionAnchor = merge.Value.Start;
            _selectionCursor = merge.Value.End;
            SheetGrid.SelectedRanges = null;
            SheetGrid.SelectedRange = merge.Value;
            CellAddressBox.Text = FormatCellReference(merge.Value.Start);
            var mergedCell = sheet!.GetCell(merge.Value.Start);
            FormulaBar.Text = FormatFormulaBarText(mergedCell, merge.Value.Start);
            FocusSheetGridIfNeeded();
            RefreshToolbar();
            RefreshStatusBar();
            RefreshValidationDropdown();
            return;
        }

        _selectionAnchor = addr;
        _selectionCursor = addr;
        SheetGrid.SelectedRanges = null;
        SheetGrid.SelectedRange = new GridRange(addr, addr);
        CellAddressBox.Text = FormatCellReference(addr);

        var cell = sheet?.GetCell(addr);
        FormulaBar.Text = FormatFormulaBarText(cell, addr);
        FocusSheetGridIfNeeded();
        RefreshToolbar();
        RefreshStatusBar();
        RefreshValidationDropdown();
    }

    private void SelectCurrentRegionOrAll()
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var activeCell = SheetGrid.SelectedRange?.Start;
        if (sheet is not null &&
            activeCell is { } cell &&
            SelectionRangeService.GetCurrentRegion(sheet, cell) is { } currentRegion &&
            SheetGrid.SelectedRange != currentRegion)
        {
            _selectionAnchor = currentRegion.Start;
            _selectionCursor = currentRegion.End;
            SheetGrid.SelectedRanges = null;
            SheetGrid.SelectedRange = currentRegion;
            CellAddressBox.Text = FormatRangeReference(currentRegion.Start, currentRegion.End);
            var activeCellModel = sheet.GetCell(cell);
            FormulaBar.Text = FormatFormulaBarText(activeCellModel, cell);
            SheetGrid.Focus();
            RefreshToolbar();
            RefreshStatusBar();
            return;
        }

        SelectAll();
    }

    private void SelectCurrentRegionOnly()
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var activeCell = SheetGrid.SelectedRange?.Start;
        if (sheet is not null &&
            activeCell is { } cell &&
            SelectionRangeService.GetCurrentRegion(sheet, cell) is { } currentRegion)
        {
            _selectionAnchor = currentRegion.Start;
            _selectionCursor = currentRegion.End;
            SheetGrid.SelectedRanges = null;
            SheetGrid.SelectedRange = currentRegion;
            CellAddressBox.Text = FormatRangeReference(currentRegion.Start, currentRegion.End);
            FormulaBar.Text = FormatFormulaBarText(sheet.GetCell(cell), cell);
            SheetGrid.Focus();
            RefreshToolbar();
            RefreshStatusBar();
        }
    }

    private void SelectWholeRowsFromSelection()
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        SetSelectionRange(SelectionRangeService.GetWholeRows(range), range.Start);
    }

    private void SelectWholeColumnsFromSelection()
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        SetSelectionRange(SelectionRangeService.GetWholeColumns(range), range.Start);
    }

    private void SetSelectionRange(GridRange range, CellAddress activeCell)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        _selectionAnchor = range.Start;
        _selectionCursor = range.End;
        SheetGrid.SelectedRanges = null;
        SheetGrid.SelectedRange = range;
        CellAddressBox.Text = FormatRangeReference(range.Start, range.End);
        var activeCellModel = sheet?.GetCell(activeCell);
        FormulaBar.Text = FormatFormulaBarText(activeCellModel, activeCell);
        FocusSheetGridIfNeeded();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private void ExtendSelection(CellAddress anchor, CellAddress to)
    {
        _selectionCursor = to;
        SheetGrid.SelectedRanges = null;
        SheetGrid.SelectedRange = new GridRange(
            new CellAddress(_currentSheetId,
                Math.Min(anchor.Row, to.Row), Math.Min(anchor.Col, to.Col)),
            new CellAddress(_currentSheetId,
                Math.Max(anchor.Row, to.Row), Math.Max(anchor.Col, to.Col)));
        CellAddressBox.Text = FormatRangeReference(anchor, to);
        RefreshStatusBar();
    }

    private CellAddress? HitTestCell(System.Windows.Point pos)
    {
        var viewport = SheetGrid.Viewport;
        if (viewport == null) return null;
        return Freexcel.App.UI.GridView.HitTestViewportCell(viewport, _currentSheetId, pos);
    }

    private void SheetGrid_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragSelectActive || e.LeftButton != MouseButtonState.Pressed) return;
        if (_selectionAnchor is not { } anchor) return;
        var hitAddr = HitTestCell(e.GetPosition(SheetGrid));
        if (hitAddr.HasValue)
            ExtendSelection(anchor, hitAddr.Value);
    }

    private void SheetGrid_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_dragSelectActive) return;
        _dragSelectActive = false;
        SheetGrid.ReleaseMouseCapture();
    }

    private void SheetGrid_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        int notches = e.Delta / 120;

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            // Ctrl+Scroll = zoom
            ZoomSlider.Value = Math.Max(ZoomSlider.Minimum,
                Math.Min(ZoomSlider.Maximum, ZoomSlider.Value + notches * 10));
            e.Handled = true;
            return;
        }

        var horizontal = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (SheetGrid.Viewport?.SplitPanes is not null &&
            !Freexcel.App.UI.GridView.CanScrollSplitPaneRegion(_activeSplitPaneRegion, horizontal))
        {
            e.Handled = true;
            return;
        }

        if (TryScrollIndependentSplitPane(horizontal, notches))
        {
            e.Handled = true;
            return;
        }

        if (horizontal)
        {
            var sheet = _workbook.GetSheet(_currentSheetId);
            var (maximum, value) = CalculateWheelScroll(
                HorizontalScroll.Value,
                HorizontalScroll.Maximum,
                notches,
                3,
                HorizontalScroll.ViewportSize,
                GetScrollableColumnLimit(sheet));
            HorizontalScroll.Maximum = maximum;
            HorizontalScroll.Value = value;
        }
        else
        {
            var sheet = _workbook.GetSheet(_currentSheetId);
            var (maximum, value) = CalculateWheelScroll(
                VerticalScroll.Value,
                VerticalScroll.Maximum,
                notches,
                3,
                VerticalScroll.ViewportSize,
                GetScrollableRowLimit(sheet));
            VerticalScroll.Maximum = maximum;
            VerticalScroll.Value = value;
        }
        e.Handled = true;
    }

    private bool TryScrollIndependentSplitPane(bool horizontal, int notches)
    {
        if (SheetGrid.Viewport?.SplitPanes is null)
            return false;

        if (horizontal && _activeSplitPaneRegion == Freexcel.App.UI.SplitPaneRegion.TopRight)
        {
            var chrome = Freexcel.App.UI.GridView.CalculateSplitPaneScrollbarChrome(
                SheetGrid.Viewport,
                SheetGrid.ActualWidth,
                SheetGrid.ActualHeight);
            if (chrome.HorizontalTopRight is null)
                return false;
            var current = _splitPaneViewportOffsets.TryGetValue(_currentSheetId, out var offsets)
                ? offsets.TopRightLeftCol
                : null;
            var target = Freexcel.App.UI.GridView.CalculateSplitPaneScrollbarWheelTarget(
                chrome.HorizontalTopRight,
                current ?? Math.Max(1, (uint)HorizontalScroll.Value),
                notches);
            _splitPaneViewportOffsets[_currentSheetId] = (offsets ?? new SplitPaneViewportOffsets()) with { TopRightLeftCol = target.Index };
            UpdateViewport();
            return true;
        }

        if (!horizontal && _activeSplitPaneRegion == Freexcel.App.UI.SplitPaneRegion.BottomLeft)
        {
            var chrome = Freexcel.App.UI.GridView.CalculateSplitPaneScrollbarChrome(
                SheetGrid.Viewport,
                SheetGrid.ActualWidth,
                SheetGrid.ActualHeight);
            if (chrome.VerticalBottomLeft is null)
                return false;
            var current = _splitPaneViewportOffsets.TryGetValue(_currentSheetId, out var offsets)
                ? offsets.BottomLeftTopRow
                : null;
            var target = Freexcel.App.UI.GridView.CalculateSplitPaneScrollbarWheelTarget(
                chrome.VerticalBottomLeft,
                current ?? Math.Max(1, (uint)VerticalScroll.Value),
                notches);
            _splitPaneViewportOffsets[_currentSheetId] = (offsets ?? new SplitPaneViewportOffsets()) with { BottomLeftTopRow = target.Index };
            UpdateViewport();
            return true;
        }

        return false;
    }

    private void OnSplitPaneScrollbarScrolled(Freexcel.App.UI.SplitPaneScrollbarScrollTarget target)
    {
        if (SheetGrid.Viewport?.SplitPanes is null)
            return;

        _splitPaneViewportOffsets.TryGetValue(_currentSheetId, out var offsets);
        offsets ??= new SplitPaneViewportOffsets();

        if (target is
            {
                Region: Freexcel.App.UI.SplitPaneRegion.TopRight,
                Orientation: Freexcel.App.UI.SplitPaneScrollbarOrientation.Horizontal
            })
        {
            _splitPaneViewportOffsets[_currentSheetId] = offsets with { TopRightLeftCol = target.Index };
            UpdateViewport();
            return;
        }

        if (target is
            {
                Region: Freexcel.App.UI.SplitPaneRegion.BottomLeft,
                Orientation: Freexcel.App.UI.SplitPaneScrollbarOrientation.Vertical
            })
        {
            _splitPaneViewportOffsets[_currentSheetId] = offsets with { BottomLeftTopRow = target.Index };
            UpdateViewport();
        }
    }

    private void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        _pendingStandaloneAltKeyTip = false;
        if (_ribbonKeyTipMode.IsActive)
            ExitRibbonKeyTipMode();
    }

    private void RefreshToolbar()
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;
        var style = _workbook.GetStyle(sheet.GetCell(range.Start)?.StyleId ?? StyleId.Default);

        _suppressToolbarSync = true;
        BoldButton.IsChecked      = style.Bold;
        ItalicButton.IsChecked    = style.Italic;
        UnderlineButton.IsChecked = style.Underline && !style.Strikethrough;
        StrikeButton.IsChecked    = style.Strikethrough;
        AlignTopBtn.IsChecked     = style.VerticalAlignment == CellVAlign.Top;
        AlignMiddleBtn.IsChecked  = style.VerticalAlignment == CellVAlign.Center;
        AlignBottomBtn.IsChecked  = style.VerticalAlignment == CellVAlign.Bottom;
        AlignLeftBtn.IsChecked    = style.HorizontalAlignment == CellHAlign.Left;
        AlignCenterBtn.IsChecked  = style.HorizontalAlignment == CellHAlign.Center;
        AlignRightBtn.IsChecked   = style.HorizontalAlignment == CellHAlign.Right;
        WrapTextBtn.IsChecked     = style.WrapText;
        if (FontNameBox.Items.Contains(style.FontName))
            FontNameBox.SelectedItem = style.FontName;
        var sizeStr = style.FontSize.ToString("0.#");
        if (FontSizeBox.Items.Contains(sizeStr))
            FontSizeBox.SelectedItem = sizeStr;
        _suppressToolbarSync = false;
    }

    private void ApplyStyleDiff(StyleDiff diff)
    {
        if (SheetGrid.SelectedRange is null) return;
        if (!TryExecuteRepeatableApplyStyle(diff, "Apply Style"))
            return;

        UpdateViewport();
        RefreshStatusBar();
    }

    private static Key GetEffectiveKey(System.Windows.Input.KeyEventArgs e) =>
        e.SystemKey == Key.None ? e.Key : e.SystemKey;

    private static bool IsStandaloneAltKey(Key key) =>
        key is Key.LeftAlt or Key.RightAlt or Key.System;

    private void EnterRibbonKeyTipMode(RibbonKeyTipScope scope)
    {
        _ribbonKeyTipMode.Enter();
        _ribbonKeyTipScope = scope;
        _ribbonKeyTipSequence = "";
        _activeRibbonKeyTipMenu = null;
        ShowKeyTipOverlay(scope);
    }

    private void ExitRibbonKeyTipMode()
    {
        if (_activeRibbonKeyTipMenu is not null)
            _activeRibbonKeyTipMenu.IsOpen = false;

        _ribbonKeyTipMode.Cancel();
        _ribbonKeyTipScope = RibbonKeyTipScope.None;
        _ribbonKeyTipSequence = "";
        _activeRibbonKeyTipMenu = null;
        ClearKeyTipOverlay();
    }

    private void HandleActiveRibbonKeyTip(Key key)
    {
        if (key == Key.Escape)
        {
            ExitRibbonKeyTipMode();
            return;
        }

        var token = RibbonKeyTipMode.ToKeyTipToken(key);
        if (token is null)
        {
            ExitRibbonKeyTipMode();
            return;
        }

        _ribbonKeyTipSequence += token;

        if (_ribbonKeyTipScope == RibbonKeyTipScope.TopLevel)
        {
            if (TryHandleTopLevelRibbonKeyTip(_ribbonKeyTipSequence))
                EnterRibbonKeyTipMode(RibbonKeyTipScope.Commands);
            else if (TryInvokeTopLevelQatKeyTip(_ribbonKeyTipSequence))
                ExitRibbonKeyTipMode();
            else
                ExitRibbonKeyTipMode();
            return;
        }

        if (_ribbonKeyTipScope == RibbonKeyTipScope.Menu)
        {
            if (TryInvokeActiveMenuItemKeyTip(_ribbonKeyTipSequence))
                ExitRibbonKeyTipMode();
            else if (RibbonTooltip.TryOpenSubmenuForKeyTip(_activeRibbonKeyTipMenu!, _ribbonKeyTipSequence))
                return;
            else if (!HasActiveMenuItemKeyTipPrefix(_ribbonKeyTipSequence))
                ExitRibbonKeyTipMode();

            return;
        }

        if (TryInvokeVisibleCommandKeyTip(_ribbonKeyTipSequence))
        {
            ExitRibbonKeyTipMode();
            return;
        }

        if (!HasVisibleCommandKeyTipPrefix(_ribbonKeyTipSequence))
            ExitRibbonKeyTipMode();
    }

    private void ShowKeyTipOverlay(RibbonKeyTipScope scope)
    {
        if (KeyTipOverlay == null || RootGrid == null)
            return;

        RootGrid.UpdateLayout();
        KeyTipOverlay.Children.Clear();

        foreach (var element in EnumerateVisualDescendants(RootGrid).OfType<FrameworkElement>())
        {
            if (ReferenceEquals(element, KeyTipOverlay) ||
                !element.IsVisible ||
                element.ActualWidth <= 0 ||
                element.ActualHeight <= 0 ||
                (scope == RibbonKeyTipScope.Commands && IsStartScreenVisible() && !IsInsideStartScreenOverlay(element)) ||
                (scope == RibbonKeyTipScope.Commands && IsInsideUnselectedTabItem(element)))
            {
                continue;
            }

            if (!ShouldShowKeyTipElement(element, scope))
                continue;

            var keyTip = RibbonTooltip.GetKeyTip(element);
            if (string.IsNullOrWhiteSpace(keyTip))
                continue;

            Point origin;
            try
            {
                origin = element.TransformToAncestor(RootGrid).Transform(new Point(0, 0));
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            var badge = CreateKeyTipBadge(keyTip);
            badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var badgeSize = badge.DesiredSize;
            var point = RibbonKeyTipOverlayPlacement.PlaceBadge(
                new Rect(origin, new Size(element.ActualWidth, element.ActualHeight)),
                new Size(RootGrid.ActualWidth, RootGrid.ActualHeight),
                badgeSize);

            Canvas.SetLeft(badge, point.X);
            Canvas.SetTop(badge, point.Y);
            KeyTipOverlay.Children.Add(badge);
        }

        KeyTipOverlay.Visibility = KeyTipOverlay.Children.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private static bool ShouldShowKeyTipElement(FrameworkElement element, RibbonKeyTipScope scope)
    {
        var isQuickAccessButton =
            element is Button button &&
            button.ReadLocalValue(DockPanel.DockProperty) is Dock.Left;
        if (scope == RibbonKeyTipScope.TopLevel)
            return element is TabItem || isQuickAccessButton;

        return element is not TabItem && !isQuickAccessButton;
    }

    private void ClearKeyTipOverlay()
    {
        if (KeyTipOverlay == null)
            return;

        KeyTipOverlay.Children.Clear();
        KeyTipOverlay.Visibility = Visibility.Collapsed;
    }

    private static Border CreateKeyTipBadge(string keyTip) =>
        new()
        {
            Background = System.Windows.Media.Brushes.White,
            BorderBrush = System.Windows.Media.Brushes.Black,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(4, 1, 4, 1),
            Child = new TextBlock
            {
                Text = keyTip,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = System.Windows.Media.Brushes.Black
            }
        };

    private static IEnumerable<DependencyObject> EnumerateVisualDescendants(DependencyObject root)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            yield return child;

            foreach (var descendant in EnumerateVisualDescendants(child))
                yield return descendant;
        }
    }

    private bool TryInvokeVisibleCommandKeyTip(string keyTip)
    {
        var visibleKeyTipElements = GetVisibleKeyTipElements(RibbonKeyTipScope.Commands).ToList();
        var match = RibbonKeyTipRouting.ResolveKeyTipElement(visibleKeyTipElements, keyTip);
        if (match is null)
            return false;

        if (match is ButtonBase button)
        {
            if (TryEnterMenuKeyTipScope(button))
                return false;

            if (button is ToggleButton toggleButton)
                toggleButton.IsChecked = toggleButton.IsChecked != true;

            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
            return true;
        }

        if (match is ComboBox comboBox)
        {
            comboBox.IsDropDownOpen = true;
            return true;
        }

        return false;
    }

    private bool TryEnterMenuKeyTipScope(ButtonBase button)
    {
        if (button.ContextMenu is not { } menu ||
            !GetMenuItems(menu).Any(item => !string.IsNullOrWhiteSpace(RibbonTooltip.GetKeyTip(item))))
        {
            return false;
        }

        _activeRibbonKeyTipMenu = menu;
        _ribbonKeyTipScope = RibbonKeyTipScope.Menu;
        _ribbonKeyTipSequence = "";
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
        ClearKeyTipOverlay();
        return true;
    }

    private bool TryInvokeActiveMenuItemKeyTip(string keyTip)
    {
        if (_activeRibbonKeyTipMenu is null)
            return false;

        var match = RibbonKeyTipRouting.ResolveMenuItem(GetMenuItems(_activeRibbonKeyTipMenu), keyTip);
        if (match is null)
            return false;

        match.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, match));
        return true;
    }

    private bool HasActiveMenuItemKeyTipPrefix(string keyTipPrefix) =>
        _activeRibbonKeyTipMenu is not null &&
        RibbonKeyTipRouting.HasMenuItemKeyTipPrefix(GetMenuItems(_activeRibbonKeyTipMenu), keyTipPrefix);

    private static IEnumerable<MenuItem> GetMenuItems(ItemsControl itemsControl)
    {
        foreach (var item in itemsControl.Items)
        {
            if (item is MenuItem menuItem)
            {
                yield return menuItem;

                foreach (var child in GetMenuItems(menuItem))
                    yield return child;
            }
        }
    }

    private bool TryInvokeTopLevelQatKeyTip(string keyTip)
    {
        var match = GetVisibleKeyTipElements(RibbonKeyTipScope.TopLevel)
            .OfType<ButtonBase>()
            .FirstOrDefault(element =>
                string.Equals(RibbonTooltip.GetKeyTip(element), keyTip, StringComparison.OrdinalIgnoreCase));

        if (match is null)
            return false;

        match.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, match));
        return true;
    }

    private bool HasVisibleCommandKeyTipPrefix(string keyTipPrefix) =>
        RibbonKeyTipRouting.HasKeyTipPrefix(GetVisibleKeyTipElements(RibbonKeyTipScope.Commands), keyTipPrefix);

    private IEnumerable<FrameworkElement> GetVisibleKeyTipElements(RibbonKeyTipScope scope)
    {
        if (RootGrid == null)
            yield break;

        foreach (var element in EnumerateVisualDescendants(RootGrid).OfType<FrameworkElement>())
        {
            if (ReferenceEquals(element, KeyTipOverlay) ||
                !element.IsVisible ||
                element.ActualWidth <= 0 ||
                element.ActualHeight <= 0 ||
                (scope == RibbonKeyTipScope.Commands && IsStartScreenVisible() && !IsInsideStartScreenOverlay(element)) ||
                (scope == RibbonKeyTipScope.Commands && IsInsideUnselectedTabItem(element)) ||
                !ShouldShowKeyTipElement(element, scope) ||
                string.IsNullOrWhiteSpace(RibbonTooltip.GetKeyTip(element)))
            {
                continue;
            }

            yield return element;
        }
    }

    private bool IsStartScreenVisible() =>
        StartScreenOverlay?.Visibility == Visibility.Visible;

    private bool IsInsideStartScreenOverlay(DependencyObject element)
    {
        for (DependencyObject? current = element; current is not null; current = GetTreeParent(current))
        {
            if (ReferenceEquals(current, StartScreenOverlay))
                return true;
        }

        return false;
    }

    private static bool IsInsideUnselectedTabItem(DependencyObject element)
    {
        for (DependencyObject? current = element; current is not null; current = GetTreeParent(current))
        {
            if (current is TabItem tabItem)
                return !tabItem.IsSelected;
        }

        return false;
    }

    private static DependencyObject? GetTreeParent(DependencyObject element)
    {
        try
        {
            if (element is System.Windows.Media.Visual)
                return System.Windows.Media.VisualTreeHelper.GetParent(element);
        }
        catch (InvalidOperationException)
        {
        }

        return LogicalTreeHelper.GetParent(element);
    }

    private void EnterEditMode()
    {
        if (_selectionAnchor.HasValue)
            ShowInlineEditor(_selectionAnchor.Value);
        else
        {
            FormulaBar.Focus();
            FormulaBar.CaretIndex = FormulaBar.Text.Length;
        }
    }

    private void ShowInlineEditor(CellAddress addr)
    {
        HideValidationDropdown();
        var vp = SheetGrid.Viewport;
        if (vp == null) { FormulaBar.Focus(); return; }

        var rowMetric = vp.RowMetrics.FirstOrDefault(r => r.Row == addr.Row);
        var colMetric = vp.ColMetrics.FirstOrDefault(c => c.Col == addr.Col);
        if (rowMetric == null || colMetric == null) { FormulaBar.Focus(); return; }

        var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(addr);
        var text = FormatFormulaBarText(cell, addr);

        if (_inlineEditor == null)
        {
            _inlineEditor = new System.Windows.Controls.TextBox
            {
                BorderThickness = new System.Windows.Thickness(2),
                BorderBrush     = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(33, 115, 70)),
                Padding         = new System.Windows.Thickness(1),
                FontFamily      = new System.Windows.Media.FontFamily("Calibri"),
                FontSize        = 15.0,
                Background      = System.Windows.Media.Brushes.White,
                AcceptsReturn   = false,
            };
            TextOptions.SetTextFormattingMode(_inlineEditor, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(_inlineEditor, TextRenderingMode.ClearType);
            TextOptions.SetTextHintingMode(_inlineEditor, TextHintingMode.Fixed);
            _inlineEditor.PreviewKeyDown += InlineEditor_KeyDown;
            _inlineEditor.LostFocus  += InlineEditor_LostFocus;
            _inlineEditor.TextChanged += (_, _) => FormulaBar.Text = _inlineEditor.Text;
            EditOverlay.Children.Add(_inlineEditor);
        }

        // Cell metrics are in unzoomed coordinates; the EditOverlay is not transformed, so scale.
        double zoom = _zoomLevel;
        double cx = (colMetric.LeftOffset + SheetGrid.ActualRowHeaderWidth) * zoom;
        double cy = (rowMetric.TopOffset  + Freexcel.App.UI.GridView.ColHeaderHeight) * zoom;
        double cellW = colMetric.Width  * zoom;
        double cellH = rowMetric.Height * zoom;

        _inlineEditor.Text = text;
        System.Windows.Controls.Canvas.SetLeft(_inlineEditor, cx - 2);
        System.Windows.Controls.Canvas.SetTop(_inlineEditor,  cy - 2);
        _inlineEditor.Width  = cellW + 4;
        _inlineEditor.Height = Math.Max(cellH + 4, 20);
        _inlineEditor.Visibility  = Visibility.Visible;
        EditOverlay.IsHitTestVisible = true;
        _inlineEditor.Focus();
        _inlineEditor.SelectAll();
    }

    private void HideInlineEditor(bool commit)
    {
        if (_inlineEditor == null) return;
        _inlineEditor.Visibility = Visibility.Collapsed;
        EditOverlay.IsHitTestVisible = false;
        if (commit)
            FormulaBar.Text = _inlineEditor.Text;
    }

    private void RefreshValidationDropdown()
    {
        if (_inlineEditor?.IsVisible == true)
            return;

        if (SheetGrid.SelectedRange is not { } range ||
            _workbook.GetSheet(_currentSheetId) is not { } sheet ||
            TryGetCellOverlayRect(range.Start) is not { } rect)
        {
            HideValidationDropdown();
            return;
        }

        var rule = DataValidationService.GetApplicable(sheet, range.Start)
            .FirstOrDefault(dv => dv.Type == DvType.List && dv.ShowDropdown);
        if (rule is null)
        {
            HideValidationDropdown();
            return;
        }

        var items = DataValidationService.GetListItems(rule, sheet, _workbook);
        if (items.Count == 0)
        {
            HideValidationDropdown();
            return;
        }

        EnsureValidationDropdown();

        _suppressValidationDropdownCommit = true;
        _validationDropdown!.ItemsSource = items;
        var currentText = FormatCellValue(sheet.GetCell(range.Start)?.Value);
        _validationDropdown.SelectedItem = items.FirstOrDefault(item =>
            string.Equals(item, currentText, StringComparison.OrdinalIgnoreCase));
        _suppressValidationDropdownCommit = false;

        var width = Math.Max(18, Math.Min(rect.Width, 160));
        System.Windows.Controls.Canvas.SetLeft(_validationDropdown, rect.Right - width);
        System.Windows.Controls.Canvas.SetTop(_validationDropdown, rect.Top);
        _validationDropdown.Width = width;
        _validationDropdown.Height = Math.Max(18, rect.Height);
        _validationDropdown.Visibility = Visibility.Visible;
        EditOverlay.IsHitTestVisible = true;
    }

    private void EnsureValidationDropdown()
    {
        if (_validationDropdown is not null)
            return;

        _validationDropdown = new System.Windows.Controls.ComboBox
        {
            FontSize = 12,
            Padding = new System.Windows.Thickness(0),
            Background = System.Windows.Media.Brushes.White,
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(33, 115, 70)),
            BorderThickness = new System.Windows.Thickness(1),
            MaxDropDownHeight = 220,
            ToolTip = "Pick from list"
        };
        _validationDropdown.SelectionChanged += ValidationDropdown_SelectionChanged;
        EditOverlay.Children.Add(_validationDropdown);
    }

    private void HideValidationDropdown()
    {
        if (_validationDropdown is not null)
            _validationDropdown.Visibility = Visibility.Collapsed;

        if (_inlineEditor?.IsVisible != true)
            EditOverlay.IsHitTestVisible = false;
    }

    private Rect? TryGetCellOverlayRect(CellAddress addr)
    {
        var vp = SheetGrid.Viewport;
        if (vp is null)
            return null;

        var rowMetric = vp.RowMetrics.FirstOrDefault(r => r.Row == addr.Row);
        var colMetric = vp.ColMetrics.FirstOrDefault(c => c.Col == addr.Col);
        if (rowMetric is null || colMetric is null)
            return null;

        var left = colMetric.LeftOffset + SheetGrid.ActualRowHeaderWidth;
        var top = rowMetric.TopOffset + Freexcel.App.UI.GridView.ColHeaderHeight;
        return new Rect(left, top, colMetric.Width, rowMetric.Height);
    }

    private void ValidationDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressValidationDropdownCommit ||
            _validationDropdown?.SelectedItem is not string selected ||
            SheetGrid.SelectedRange is not { } range)
        {
            return;
        }

        FormulaBar.Text = selected;
        CommitEdit();
        SetActiveCell(range.Start);
    }

    private void InlineEditor_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.F4 && _inlineEditor is not null)
        {
            if (TryCycleFormulaReference(_inlineEditor))
            {
                FormulaBar.Text = _inlineEditor.Text;
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.Escape)
        {
            HideInlineEditor(commit: false);
            // Restore original text in formula bar
            var addr = SheetGrid.SelectedRange?.Start;
            if (addr.HasValue)
            {
                var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(addr.Value);
                FormulaBar.Text = FormatFormulaBarText(cell, addr.Value);
            }
            CancelCopyAndTransientModes();
            FocusSheetGridIfNeeded();
            e.Handled = true;
            return;
        }
        var current = SheetGrid.SelectedRange?.Start;
        if (current is null)
            return;

        var intent = ExcelEditKeyPlanner.GetIntent(
            e.Key,
            Keyboard.Modifiers,
            current.Value,
            pageSize: Math.Max(1, (SheetGrid.Viewport?.RowMetrics.Count ?? 25) - 1),
            allowFormulaBarNavigationKeys: false);

        if (intent.Action == ExcelEditKeyAction.InsertLineBreak)
        {
            InsertLineBreak(_inlineEditor!);
            FormulaBar.Text = _inlineEditor!.Text;
            e.Handled = true;
            return;
        }

        if (intent.Action == ExcelEditKeyAction.CommitSelection)
        {
            FormulaBar.Text = _inlineEditor!.Text;
            if (CommitEditAcrossSelection())
                HideInlineEditor(commit: false);
            e.Handled = true;
            return;
        }

        if (intent.Action == ExcelEditKeyAction.CommitAndMove && intent.Target is { } next)
        {
            var text = _inlineEditor!.Text;
            FormulaBar.Text = text;
            if (CommitEdit())
            {
                HideInlineEditor(commit: false);
                SetActiveCell(next);
                EnsureCellVisible(next);
            }
            e.Handled = true;
        }
    }

    private static void InsertLineBreak(System.Windows.Controls.TextBox editor)
    {
        var insertion = Environment.NewLine;
        var start = editor.SelectionStart;
        var length = editor.SelectionLength;
        editor.Text = editor.Text.Remove(start, length).Insert(start, insertion);
        editor.CaretIndex = start + insertion.Length;
    }

    private void InlineEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_inlineEditor?.IsVisible == true)
        {
            FormulaBar.Text = _inlineEditor.Text;
            HideInlineEditor(commit: true);
            CommitEdit();
        }
    }

    private static string FormatCellValue(ScalarValue? value) =>
        SpreadsheetDisplayFormatter.FormatCellValue(value);

    private void FocusSheetGridIfNeeded()
    {
        if (!ReferenceEquals(Keyboard.FocusedElement, SheetGrid))
            SheetGrid.Focus();
    }

    private void CancelCopyAndTransientModes()
    {
        ClearClipboardVisualState();
        _internalClipboard = null;
        _formatPainterActive = false;
    }

    private void ClearClipboardVisualState()
    {
        SheetGrid.ClipboardRange = null;
        SheetGrid.ClipboardIsCut = false;
    }

    private void EnsureCellVisible(CellAddress addr)
    {
        var vp = SheetGrid.Viewport;
        if (vp == null) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        var frozenRows = sheet?.FrozenRows ?? 0;
        var frozenCols = sheet?.FrozenCols ?? 0;

        var rows = vp.RowMetrics.Where(row => row.Row > frozenRows).ToList();
        if (addr.Row > frozenRows && rows.Count > 0 && !rows.Any(r => r.Row == addr.Row))
        {
            uint firstRow = rows[0].Row;
            uint lastRow  = rows[^1].Row;
            var scrollValue = CalculateScrollValueToRevealCell(
                WorksheetIndexToScrollbarValue(addr.Row, frozenRows),
                WorksheetIndexToScrollbarValue(firstRow, frozenRows),
                WorksheetIndexToScrollbarValue(lastRow, frozenRows),
                GetScrollableRowLimit(sheet),
                (uint)rows.Count);
            VerticalScroll.Maximum = CalculateScrollbarMaximumForKeyboardReveal(
                VerticalScroll.Maximum,
                scrollValue,
                GetScrollableRowLimit(sheet));
            VerticalScroll.Value = scrollValue;
        }

        var cols = vp.ColMetrics.Where(col => col.Col > frozenCols).ToList();
        if (addr.Col > frozenCols && cols.Count > 0 && !cols.Any(c => c.Col == addr.Col))
        {
            uint firstCol = cols[0].Col;
            uint lastCol  = cols[^1].Col;
            var scrollValue = CalculateScrollValueToRevealCell(
                WorksheetIndexToScrollbarValue(addr.Col, frozenCols),
                WorksheetIndexToScrollbarValue(firstCol, frozenCols),
                WorksheetIndexToScrollbarValue(lastCol, frozenCols),
                GetScrollableColumnLimit(sheet),
                (uint)cols.Count);
            HorizontalScroll.Maximum = CalculateScrollbarMaximumForKeyboardReveal(
                HorizontalScroll.Maximum,
                scrollValue,
                GetScrollableColumnLimit(sheet));
            HorizontalScroll.Value = scrollValue;
        }
    }

    // ── Navigation helpers ────────────────────────────────────────────────────

    private bool CellHasData(Sheet? sheet, uint row, uint col)
    {
        if (sheet == null) return false;
        var v = sheet.GetValue(new CellAddress(_currentSheetId, row, col));
        return v != null && v is not BlankValue;
    }

    private CellAddress FindDataBoundaryCol(Sheet? sheet, uint row, uint col, int dir)
    {
        const uint maxRow = 1_048_576;
        bool startFull = CellHasData(sheet, row, col);
        uint r = row;
        while (true)
        {
            long next = (long)r + dir;
            if (next < 1 || next > maxRow) break;
            uint nr = (uint)next;
            bool nextFull = CellHasData(sheet, nr, col);
            if (startFull && !nextFull) break;   // stop before gap
            r = nr;
            if (!startFull && nextFull) break;   // landed on first data cell
        }
        return new CellAddress(_currentSheetId, r, col);
    }

    private CellAddress FindDataBoundaryRow(Sheet? sheet, uint row, uint col, int dir)
    {
        const uint maxCol = 16_384;
        bool startFull = CellHasData(sheet, row, col);
        uint c = col;
        while (true)
        {
            long next = (long)c + dir;
            if (next < 1 || next > maxCol) break;
            uint nc = (uint)next;
            bool nextFull = CellHasData(sheet, row, nc);
            if (startFull && !nextFull) break;
            c = nc;
            if (!startFull && nextFull) break;
        }
        return new CellAddress(_currentSheetId, row, c);
    }

    private CellAddress CtrlEndCell(Sheet? sheet)
    {
        uint maxRow = 1, maxCol = 1;
        if (sheet != null)
            foreach (var (addr, _) in sheet.GetUsedCells())
            {
                if (addr.Row > maxRow) maxRow = addr.Row;
                if (addr.Col > maxCol) maxCol = addr.Col;
            }
        return new CellAddress(_currentSheetId, maxRow, maxCol);
    }

    private void FormulaBar_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.F4)
        {
            if (TryCycleFormulaReference(FormulaBar))
                e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Escape)
        {
            // Restore the original cell value and return focus to grid
            var addr = SheetGrid.SelectedRange?.Start;
            if (addr.HasValue)
            {
                var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(addr.Value);
                FormulaBar.Text = FormatFormulaBarText(cell, addr.Value);
            }
            ClearClipboardVisualState();
            SheetGrid.Focus();
            e.Handled = true;
        }
        else if (SheetGrid.SelectedRange?.Start is { } current)
        {
            int pageSize = Math.Max(1, (SheetGrid.Viewport?.RowMetrics.Count ?? 25) - 1);
            var intent = ExcelEditKeyPlanner.GetIntent(
                e.Key,
                e.KeyboardDevice.Modifiers,
                current,
                pageSize,
                allowFormulaBarNavigationKeys: true);

            if (intent.Action == ExcelEditKeyAction.InsertLineBreak)
            {
                InsertLineBreak(FormulaBar);
                e.Handled = true;
            }
            else if (intent.Action == ExcelEditKeyAction.CommitSelection)
            {
                CommitEditAcrossSelection();
                e.Handled = true;
            }
            else if (intent.Action == ExcelEditKeyAction.CommitAndMove && intent.Target is { } target)
            {
                if (CommitEdit())
                {
                    SetActiveCell(target);
                    EnsureCellVisible(target);
                }

                e.Handled = true;
            }
        }
    }

    private static bool TryCycleFormulaReference(System.Windows.Controls.TextBox editor)
    {
        if (!editor.Text.StartsWith("=", StringComparison.Ordinal))
            return false;

        if (!FormulaReferenceCycler.TryCycleReferenceAtCaret(
                editor.Text,
                editor.SelectionLength > 0 ? editor.SelectionStart : editor.CaretIndex,
                out var cycled,
                out var selectionStart,
                out var selectionLength))
            return false;

        editor.Text = cycled;
        editor.SelectionStart = selectionStart;
        editor.SelectionLength = selectionLength;
        return true;
    }

    private bool CommitEdit()
    {
        if (SheetGrid.SelectedRange == null) return false;
        var addr = SheetGrid.SelectedRange.Value.Start;
        var text = FormulaBar.Text;

        if (!TryCreateCellFromEntryText(addr, text, out var newCell))
            return false;

        return CommitPreparedEdits([(addr, newCell)], text, [addr], "Edit Cell");
    }

    private bool CommitEditAcrossSelection()
    {
        if (SheetGrid.SelectedRange is not { } range) return false;
        var text = FormulaBar.Text;
        var edits = new List<(CellAddress Address, Cell NewCell)>();
        foreach (var address in range.AllCells())
        {
            if (!TryCreateCellFromEntryText(address, text, out var newCell))
                return false;

            edits.Add((address, newCell));
        }

        if (edits.Count == 0)
            return false;

        return CommitPreparedEdits(
            edits,
            text,
            edits.Select(edit => edit.Address).ToList(),
            "Edit Selection");
    }

    private bool TryCreateCellFromEntryText(CellAddress addr, string text, out Cell newCell)
    {
        newCell = default!;
        if (text.StartsWith("="))
        {
            var formula = text.Substring(1);
            if (_options.UseR1C1ReferenceStyle)
                formula = FormulaReferenceStyleService.ToA1(formula, addr);
            newCell = Cell.FromFormula(formula);
        }
        else
        {
            ScalarValue value;
            if (double.TryParse(text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var d) && double.IsFinite(d))
                value = new NumberValue(d);
            else if (text.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
                     text.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
                value = new BoolValue(text.Equals("TRUE", StringComparison.OrdinalIgnoreCase));
            else value = new TextValue(text);

            // Soft validation: check data validation rules and warn but still apply
            var sheet = _workbook.GetSheet(_currentSheetId);
            if (sheet != null)
            {
                var applicableRules = DataValidationService.GetApplicable(sheet, addr);
                DataValidation? violatingRule = null;
                string? violationMsg = null;
                foreach (var dv in applicableRules)
                {
                    var msg = DataValidationService.Validate(dv, value, sheet, addr, _workbook);
                    if (msg != null) { violatingRule = dv; violationMsg = msg; break; }
                }

                if (violationMsg != null && violatingRule != null)
                {
                    var dvRule = violatingRule;
                    var action = DataValidationService.GetInvalidEntryAction(dvRule);
                    if (action == DataValidationInvalidEntryAction.Block)
                    {
                        var icon = dvRule.AlertStyle switch
                        {
                            DvAlertStyle.Information => MessageBoxImage.Information,
                            DvAlertStyle.Warning => MessageBoxImage.Warning,
                            _ => MessageBoxImage.Error
                        };
                        MessageBox.Show(violationMsg, dvRule.ErrorTitle ?? "Validation Error",
                            MessageBoxButton.OK, icon);
                        RefreshValidationDropdown();
                        return false;
                    }

                    if (action == DataValidationInvalidEntryAction.AskToContinue)
                    {
                        var icon = dvRule.AlertStyle switch
                        {
                            DvAlertStyle.Information => MessageBoxImage.Information,
                            DvAlertStyle.Warning => MessageBoxImage.Warning,
                            _ => MessageBoxImage.Error
                        };
                        var buttons = dvRule.AlertStyle == DvAlertStyle.Information
                            ? MessageBoxButton.OKCancel
                            : MessageBoxButton.YesNo;
                        var result = MessageBox.Show(violationMsg, dvRule.ErrorTitle ?? "Validation Error",
                            buttons, icon);
                        if (result is MessageBoxResult.No or MessageBoxResult.Cancel)
                        {
                            RefreshValidationDropdown();
                            return false;
                        }
                    }
                }
            }

            newCell = Cell.FromValue(value);
        }

        return true;
    }

    private bool CommitPreparedEdits(
        IReadOnlyList<(CellAddress Address, Cell NewCell)> edits,
        string text,
        IReadOnlyList<CellAddress> fallbackAffectedCells,
        string title)
    {
        if (!TryExecuteEditCells(edits, title, out var outcome))
            return false;

        var affectedCells = outcome.AffectedCells ?? fallbackAffectedCells;
        if (text.StartsWith("="))
        {
            // For now, we manually register dependencies because we haven't automated this in the command yet.
            try
            {
                foreach (var affected in affectedCells)
                {
                    var formulaA1 = _options.UseR1C1ReferenceStyle
                        ? FormulaReferenceStyleService.ToA1(text.Substring(1), affected)
                        : text.Substring(1);
                    var lexer = new Lexer("=" + formulaA1);
                    var parser = new Parser(lexer.Tokenize());
                    var ast = parser.Parse();
                    _recalcEngine.RegisterFormulaDependencies(affected, ast, affected.Sheet, _workbook);
                }
            }
            catch
            {
                // Formula syntax is invalid; clear stale dependencies so this cell
                // does not incorrectly depend on previously-referenced cells.
                foreach (var affected in affectedCells)
                    _recalcEngine.ClearFormulaDependencies(affected);
            }
        }
        else
        {
            foreach (var affected in affectedCells)
                _recalcEngine.ClearFormulaDependencies(affected);
        }

        RecalculateIfAutomatic(affectedCells);
        UpdateViewport();
        RefreshStatusBar();
        RefreshValidationDropdown();
        return true;
    }

    private void UpdateViewport()
    {
        if (SheetGrid == null || _viewportService == null) return;

        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is not null)
            SyncZoomFromSheet(sheet.ZoomPercent);

        var (topRow, leftCol) = CalculateViewportOrigin(sheet, VerticalScroll.Value, HorizontalScroll.Value);
        topRow = ClampViewportOrigin(
            topRow,
            CellAddress.MaxRow,
            SheetGrid.Viewport is null ? 40 : (uint)CountScrollableRows(SheetGrid.Viewport, sheet));
        leftCol = ClampViewportOrigin(
            leftCol,
            CellAddress.MaxCol,
            SheetGrid.Viewport is null ? 15 : (uint)CountScrollableColumns(SheetGrid.Viewport, sheet));

        var rowHeaderWidth = SheetGrid.ActualRowHeaderWidth;
        var viewport = CreateViewport(sheet, topRow, leftCol, rowHeaderWidth);
        var actualRowHeaderWidth = SheetGrid.ShowHeaders
            ? Freexcel.App.UI.GridView.CalculateRowHeaderWidth(viewport)
            : 0.0;
        if (Math.Abs(actualRowHeaderWidth - rowHeaderWidth) > 0.1)
            viewport = CreateViewport(sheet, topRow, leftCol, actualRowHeaderWidth);

        SheetGrid.Viewport = viewport;
        SheetGrid.FormulaTraceSheetId = _currentSheetId;
        SheetGrid.FormulaTraceArrows = _formulaTraceArrows;
        SheetGrid.Charts = sheet?.Charts;
        SheetGrid.TextBoxes = sheet?.TextBoxes;
        SheetGrid.DrawingShapes = sheet?.DrawingShapes;
        SheetGrid.WorkbookTheme = _workbook.Theme;
        SheetGrid.Pictures = sheet?.Pictures;
        SheetGrid.WorksheetBackground = sheet?.BackgroundImage;
        SheetGrid.Sparklines = sheet?.Sparklines;
        SheetGrid.SparklineValues = sheet is null ? null : BuildSparklineValues(sheet);
        SheetGrid.MergedRegions = sheet?.MergedRegions;
        SheetGrid.WorksheetViewMode = sheet?.ViewMode ?? WorksheetViewMode.Normal;
        SheetGrid.ShowGridLines = sheet?.ShowGridlines ?? true;
        SheetGrid.ShowHeaders = sheet?.ShowHeadings ?? true;
        SheetGrid.ShowRulers = sheet?.ShowRulers ?? true;
        _suppressViewOptionSync = true;
        try
        {
            if (ViewGridlinesChk is not null)
                ViewGridlinesChk.IsChecked = SheetGrid.ShowGridLines;
            if (ViewHeadersChk is not null)
                ViewHeadersChk.IsChecked = SheetGrid.ShowHeaders;
            if (ViewRulerChk is not null)
                ViewRulerChk.IsChecked = SheetGrid.ShowRulers;
        }
        finally
        {
            _suppressViewOptionSync = false;
        }
        SheetGrid.RowPageBreaks = sheet?.RowPageBreaks;
        SheetGrid.ColumnPageBreaks = sheet?.ColumnPageBreaks;
        SheetGrid.PrintArea = sheet?.PrintArea;
        SheetGrid.SplitRow = sheet?.SplitRow;
        SheetGrid.SplitColumn = sheet?.SplitColumn;
        SheetGrid.PageMargins = sheet?.PageMargins ?? WorksheetPageMargins.Narrow;
        SheetGrid.PageOrientation = sheet?.PageOrientation ?? WorksheetPageOrientation.Portrait;
        SheetGrid.PaperSize = sheet?.PaperSize ?? WorksheetPaperSize.A4;

        // Adjust scrollbar range to the used data range + buffer, thumb to visible area
        UpdateScrollbarMaximums(sheet);
        var scrollableRowCount = CountScrollableRows(viewport, sheet);
        var scrollableColumnCount = CountScrollableColumns(viewport, sheet);
        VerticalScroll.ViewportSize   = scrollableRowCount;
        HorizontalScroll.ViewportSize = scrollableColumnCount;
        VerticalScroll.LargeChange    = Math.Max(1, scrollableRowCount);
        HorizontalScroll.LargeChange  = Math.Max(1, scrollableColumnCount);
        RefreshValidationDropdown();
        RefreshPivotFieldListPane();
        RefreshSlicerTimelinePane();
    }

    private ViewportModel CreateViewport(Sheet? sheet, uint topRow, uint leftCol, double rowHeaderWidth)
    {
        var request = new ViewportRequest(
            TopRow: topRow,
            LeftCol: leftCol,
            AvailableHeight: (SheetGrid.ActualHeight - SheetGrid.EffectiveColHeaderHeight) / _zoomLevel,
            AvailableWidth: CalculateViewportAvailableWidth(SheetGrid.ActualWidth, rowHeaderWidth, _zoomLevel),
            SplitPaneOffsets: GetSplitPaneViewportOffsets(sheet, topRow, leftCol));

        return _viewportService.GetViewport(_workbook, _currentSheetId, request);
    }

    private SplitPaneViewportOffsets? GetSplitPaneViewportOffsets(Sheet? sheet, uint topRow, uint leftCol)
    {
        if (sheet is null || (!sheet.SplitRow.HasValue && !sheet.SplitColumn.HasValue))
            return null;

        _splitPaneViewportOffsets.TryGetValue(sheet.Id, out var offsets);
        return new SplitPaneViewportOffsets(
            sheet.SplitColumn.HasValue ? offsets?.TopRightLeftCol ?? leftCol : null,
            sheet.SplitRow.HasValue ? offsets?.BottomLeftTopRow ?? topRow : null);
    }

    private static int CountScrollableRows(ViewportModel viewport, Sheet? sheet)
    {
        var frozenRows = sheet?.FrozenRows ?? 0;
        return Math.Max(1, viewport.RowMetrics.Count(row => row.Row > frozenRows));
    }

    private static int CountScrollableColumns(ViewportModel viewport, Sheet? sheet)
    {
        var frozenCols = sheet?.FrozenCols ?? 0;
        return Math.Max(1, viewport.ColMetrics.Count(column => column.Col > frozenCols));
    }

    public static (uint TopRow, uint LeftCol) CalculateViewportOrigin(
        Sheet? sheet,
        double verticalScrollValue,
        double horizontalScrollValue) =>
        ViewportScrollCalculator.CalculateViewportOrigin(sheet, verticalScrollValue, horizontalScrollValue);

    public static uint ScrollbarValueToWorksheetIndex(
        double scrollbarValue,
        uint frozenCount,
        uint absoluteLimit) =>
        ViewportScrollCalculator.ScrollbarValueToWorksheetIndex(scrollbarValue, frozenCount, absoluteLimit);

    public static uint WorksheetIndexToScrollbarValue(
        uint worksheetIndex,
        uint frozenCount) =>
        ViewportScrollCalculator.WorksheetIndexToScrollbarValue(worksheetIndex, frozenCount);

    public static uint CalculateScrollableLimit(uint absoluteLimit, uint frozenCount)
        => ViewportScrollCalculator.CalculateScrollableLimit(absoluteLimit, frozenCount);

    private static uint GetScrollableRowLimit(Sheet? sheet) =>
        ViewportScrollCalculator.GetScrollableRowLimit(sheet);

    private static uint GetScrollableColumnLimit(Sheet? sheet) =>
        ViewportScrollCalculator.GetScrollableColumnLimit(sheet);

    public static uint ClampViewportOrigin(double rawValue, uint absoluteLimit, uint visibleSpan)
        => ViewportScrollCalculator.ClampViewportOrigin(rawValue, absoluteLimit, visibleSpan);

    public static double CalculateViewportAvailableWidth(
        double gridWidth,
        double rowHeaderWidth,
        double zoomLevel) =>
        ViewportScrollCalculator.CalculateViewportAvailableWidth(gridWidth, rowHeaderWidth, zoomLevel);

    public static uint CalculateOpenedWorksheetScrollValue(
        uint? savedTopLeftIndex,
        uint fallbackIndex,
        uint absoluteLimit,
        uint frozenCount = 0) =>
        ViewportScrollCalculator.CalculateOpenedWorksheetScrollValue(
            savedTopLeftIndex,
            fallbackIndex,
            absoluteLimit,
            frozenCount);

    public static uint CalculateScrollValueToRevealCell(
        uint targetIndex,
        uint firstVisibleIndex,
        uint lastVisibleIndex,
        uint absoluteLimit) =>
        ViewportScrollCalculator.CalculateScrollValueToRevealCell(
            targetIndex,
            firstVisibleIndex,
            lastVisibleIndex,
            absoluteLimit);

    public static uint CalculateScrollValueToRevealCell(
        uint targetIndex,
        uint firstVisibleIndex,
        uint lastVisibleIndex,
        uint absoluteLimit,
        uint visibleSpan) =>
        ViewportScrollCalculator.CalculateScrollValueToRevealCell(
            targetIndex,
            firstVisibleIndex,
            lastVisibleIndex,
            absoluteLimit,
            visibleSpan);

    public static uint CalculateScrollValueToRevealCell(
        uint targetIndex,
        uint firstVisibleIndex,
        uint lastVisibleIndex) =>
        ViewportScrollCalculator.CalculateScrollValueToRevealCell(targetIndex, firstVisibleIndex, lastVisibleIndex);

    public static double CalculateScrollbarMaximumForKeyboardReveal(
        double currentMaximum,
        uint desiredScrollValue,
        uint absoluteLimit) =>
        ViewportScrollCalculator.CalculateScrollbarMaximumForKeyboardReveal(
            currentMaximum,
            desiredScrollValue,
            absoluteLimit);

    public static double CalculateScrollbarMaximumForKeyboardReveal(
        double currentMaximum,
        uint desiredScrollValue) =>
        ViewportScrollCalculator.CalculateScrollbarMaximumForKeyboardReveal(currentMaximum, desiredScrollValue);

    public static (double Maximum, double Value) CalculateScrollbarArrowSmallIncrement(
        double currentValue,
        double currentMaximum,
        double smallChange,
        uint absoluteLimit) =>
        ViewportScrollCalculator.CalculateScrollbarArrowSmallIncrement(
            currentValue,
            currentMaximum,
            smallChange,
            absoluteLimit);

    public static (double Maximum, double Value) CalculateScrollbarArrowSmallIncrement(
        double currentValue,
        double currentMaximum,
        double smallChange,
        double visibleSpan,
        uint absoluteLimit) =>
        ViewportScrollCalculator.CalculateScrollbarArrowSmallIncrement(
            currentValue,
            currentMaximum,
            smallChange,
            visibleSpan,
            absoluteLimit);

    public static (double Maximum, double Value) CalculateWheelScroll(
        double currentValue,
        double currentMaximum,
        int wheelNotches,
        double stepPerNotch,
        double visibleSpan,
        uint absoluteLimit) =>
        ViewportScrollCalculator.CalculateWheelScroll(
            currentValue,
            currentMaximum,
            wheelNotches,
            stepPerNotch,
            visibleSpan,
            absoluteLimit);

    public static uint CalculateMaximumViewportOrigin(uint absoluteLimit, uint visibleSpan)
        => ViewportScrollCalculator.CalculateMaximumViewportOrigin(absoluteLimit, visibleSpan);

    public static uint CalculateScrollbarMaximumForUsedRange(
        uint usedMax,
        uint visibleSpan,
        uint currentScrollValue,
        uint absoluteLimit) =>
        ViewportScrollCalculator.CalculateScrollbarMaximumForUsedRange(
            usedMax,
            visibleSpan,
            currentScrollValue,
            absoluteLimit);

    private void UpdateScrollbarMaximums(Sheet? sheet)
    {
        // Compute the farthest cell with data
        uint usedMaxRow = 1, usedMaxCol = 1;
        if (sheet != null)
            foreach (var (addr, _) in sheet.GetUsedCells())
            {
                if (addr.Row > usedMaxRow) usedMaxRow = addr.Row;
                if (addr.Col > usedMaxCol) usedMaxCol = addr.Col;
            }

        var vp = SheetGrid.Viewport;
        uint visRows = (uint)Math.Max(10, vp is null ? 40 : CountScrollableRows(vp, sheet));
        uint visCols = (uint)Math.Max(5,  vp is null ? 15 : CountScrollableColumns(vp, sheet));

        var frozenRows = sheet?.FrozenRows ?? 0;
        var frozenCols = sheet?.FrozenCols ?? 0;
        uint currentRow = Math.Max(1, (uint)VerticalScroll.Value);
        uint currentCol = Math.Max(1, (uint)HorizontalScroll.Value);
        uint vMaxRow = CalculateScrollbarMaximumForUsedRange(
            WorksheetIndexToScrollbarValue(usedMaxRow, frozenRows),
            visRows,
            currentRow,
            GetScrollableRowLimit(sheet));
        uint vMaxCol = CalculateScrollbarMaximumForUsedRange(
            WorksheetIndexToScrollbarValue(usedMaxCol, frozenCols),
            visCols,
            currentCol,
            GetScrollableColumnLimit(sheet));

        VerticalScroll.Maximum   = Math.Min(vMaxRow, GetScrollableRowLimit(sheet));
        HorizontalScroll.Maximum = Math.Min(vMaxCol, GetScrollableColumnLimit(sheet));
    }

    private void UpdateTitleBar()
    {
        var groupSuffix = IsWorkbookGrouped() ? " [Group]" : "";
        var displayName = $"{_workbook.Name}{groupSuffix} - Freexcel";
        WorkbookNameText.Text = displayName;
        this.Title = displayName;
    }

    private bool IsWorkbookGrouped()
        => SheetTabListPlanner.IsWorkbookGrouped(_workbook, _currentSheetId, _groupedSheetIds);

    // ── Start screen ─────────────────────────────────────────────────────────

    private void ShowStartScreen()
    {
        UpdateSsGreeting();
        SwitchToRecentTab();
        UpdateSsRecentList();
        ShowHomeView();
        StartScreenOverlay.Visibility = Visibility.Visible;
    }

    private void HideStartScreen()
    {
        StartScreenOverlay.Visibility = Visibility.Collapsed;
        SheetGrid.Focus();
    }

    private void ShowHomeView()
    {
        SsHomeView.Visibility = Visibility.Visible;
        SsInfoView.Visibility = Visibility.Collapsed;
        SsHomeNavBtn.Style = (Style)FindResource("SsNavBtnActive");
        SsInfoNavBtn.Style = (Style)FindResource("SsNavBtn");
    }

    private void ShowInfoView()
    {
        SsHomeView.Visibility = Visibility.Collapsed;
        SsInfoView.Visibility = Visibility.Visible;
        SsHomeNavBtn.Style = (Style)FindResource("SsNavBtn");
        SsInfoNavBtn.Style = (Style)FindResource("SsNavBtnActive");
        UpdateInfoView();
    }

    private void UpdateInfoView()
    {
        InfoWorkbookName.Text = _workbook.Name;
        InfoFilePath.Text = _currentFilePath ?? "Not saved yet";
        InfoSheetCount.Text = _workbook.Sheets.Count.ToString();
        InfoFormat.Text = _currentFilePath is not null
            ? System.IO.Path.GetExtension(_currentFilePath).ToLower()
            : ".xlsx";
    }

    private void UpdateSsGreeting()
    {
        var hour = DateTime.Now.Hour;
        SsGreeting.Text = hour switch
        {
            < 12 => "Good morning",
            < 17 => "Good afternoon",
            _    => "Good evening"
        };
    }

    private bool _showingPinnedList;

    private void UpdateSsRecentList(string filter = "")
    {
        var plan = BackstageRecentFileListPlanner.Build(
            _recentFiles.Entries,
            filter,
            System.IO.File.Exists);
        _allRecentItems = plan.AllItems.ToList();
        SsRecentList.ItemsSource = plan.RecentItems;
        SsPinnedList.ItemsSource = plan.PinnedItems;
    }

    private void SsRecentTab_Click(object sender, RoutedEventArgs e)
    {
        if (_showingPinnedList)
            SwitchToRecentTab();
    }

    private void SsPinnedTab_Click(object sender, RoutedEventArgs e)
    {
        if (!_showingPinnedList)
            SwitchToPinnedTab();
    }

    private void SwitchToRecentTab()
    {
        _showingPinnedList = false;
        SsRecentScroll.Visibility  = Visibility.Visible;
        SsPinnedScroll.Visibility  = Visibility.Collapsed;
        SsRecentTab.BorderBrush    = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x21, 0x73, 0x46));
        SsRecentTabText.FontWeight = FontWeights.SemiBold;
        SsRecentTabText.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x21, 0x73, 0x46));
        SsPinnedTab.BorderBrush    = System.Windows.Media.Brushes.Transparent;
        SsPinnedTabText.FontWeight = FontWeights.Normal;
        SsPinnedTabText.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88));
    }

    private void SwitchToPinnedTab()
    {
        _showingPinnedList = true;
        SsRecentScroll.Visibility  = Visibility.Collapsed;
        SsPinnedScroll.Visibility  = Visibility.Visible;
        SsPinnedTab.BorderBrush    = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x21, 0x73, 0x46));
        SsPinnedTabText.FontWeight = FontWeights.SemiBold;
        SsPinnedTabText.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x21, 0x73, 0x46));
        SsRecentTab.BorderBrush    = System.Windows.Media.Brushes.Transparent;
        SsRecentTabText.FontWeight = FontWeights.Normal;
        SsRecentTabText.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88));
    }

    private void CreateNewWorkbook()
    {
        var wb = new Workbook("Book1");
        wb.AddSheet("Sheet1");
        _workbook = wb;
        _workbookRef.Current = wb;
        _currentSheetId = wb.Sheets[0].Id;
        _currentFilePath = null;
        _currentXlsxFeatureReport = null;
        UpdateTitleBar();
        RecalculateWorkbook();
        SheetGrid.SelectedRange = null;
        _selectionAnchor = null;
        _selectionCursor = null;
        CellAddressBox.Text = "A1";
        FormulaBar.Text = "";
        RefreshSheetTabs();
        UpdateViewport();
    }

    private async Task OpenFileAsync(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLower();
        var adapter = _fileAdapters.FirstOrDefault(a => a.Extension == ext);
        if (adapter == null) return;
        if (_isOpeningFile) return;

        try
        {
            _isOpeningFile = true;
            ShowOpenProgress("Opening workbook", "Loading file (preparing)", 1);

            var progress = new Progress<OpenProgressUpdate>(
                update => ShowOpenProgress(update.Title, update.Detail, update.Percent));
            var result = await LoadWorkbookAsync(path, adapter, ext, progress);

            _currentXlsxFeatureReport = result.FeatureReport;
            _workbook = result.Workbook;
            _workbookRef.Current = result.Workbook;
            _workbook.Name = result.DisplayName;
            _currentSheetId = _workbook.Sheets[0].Id;
            _currentFilePath = path;
            UpdateTitleBar();

            _recentFiles.AddOrUpdate(path);
            ShowOpenProgress("Opening workbook", "Loading file (preparing view)", 98);
            ApplyOpenedWorksheetViewState();
            RefreshSheetTabs();
            HideStartScreen();
            ShowOpenProgress("Opening workbook", "Loading file (done)", 100);
            ShowUnsupportedXlsxFeatureOpenWarningIfNeeded();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open file:\n{ex.Message}", "Open Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isOpeningFile = false;
            HideOpenProgress();
        }
    }

    private async Task<OpenWorkbookResult> LoadWorkbookAsync(
        string path,
        IFileAdapter adapter,
        string extension,
        IProgress<OpenProgressUpdate> progress)
    {
        var bytes = await ReadFileBytesWithProgressAsync(path, progress);

        XlsxFeatureReport? featureReport = null;
        if (extension == ".xlsx")
        {
            featureReport = await RunOpenStageAsync(
                progress,
                "inspecting",
                8,
                16,
                TimeSpan.FromSeconds(4),
                () =>
                {
                    using var inspectStream = new MemoryStream(bytes, writable: false);
                    return XlsxFeatureInspector.Inspect(inspectStream);
                });
        }

        var workbook = await RunOpenStageAsync(
            progress,
            "parsing",
            16,
            90,
            TimeSpan.FromSeconds(45),
            () =>
            {
                using var loadStream = new MemoryStream(bytes, writable: false);
                return adapter.Load(loadStream);
            });

        await RunOpenStageAsync(
            progress,
            "calculating",
            90,
            98,
            TimeSpan.FromSeconds(12),
            () =>
            {
                _recalcEngine.RecalculateAllFormulas(workbook);
                return true;
            });

        return new OpenWorkbookResult(
            workbook,
            featureReport,
            System.IO.Path.GetFileNameWithoutExtension(path));
    }

    private static async Task<T> RunOpenStageAsync<T>(
        IProgress<OpenProgressUpdate> progress,
        string detail,
        double startPercent,
        double endPercent,
        TimeSpan expectedDuration,
        Func<T> work)
    {
        progress.Report(new OpenProgressUpdate("Opening workbook", FormatLoadingFileDetail(detail, TimeSpan.Zero), startPercent));
        using var cancellation = new CancellationTokenSource();
        var progressTask = ReportOpenStageProgressAsync(
            progress,
            detail,
            startPercent,
            endPercent,
            expectedDuration,
            cancellation.Token);

        try
        {
            return await Task.Run(work);
        }
        finally
        {
            cancellation.Cancel();
            try { await progressTask; }
            catch (OperationCanceledException) { }
            progress.Report(new OpenProgressUpdate("Opening workbook", FormatLoadingFileDetail(detail, TimeSpan.Zero), endPercent));
        }
    }

    private static async Task ReportOpenStageProgressAsync(
        IProgress<OpenProgressUpdate> progress,
        string detail,
        double startPercent,
        double endPercent,
        TimeSpan expectedDuration,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var percent = CalculateOpenStageProgress(startPercent, endPercent, stopwatch.Elapsed, expectedDuration);
            progress.Report(new OpenProgressUpdate("Opening workbook", FormatLoadingFileDetail(detail, stopwatch.Elapsed), percent));
        }
    }

    public static double CalculateOpenStageProgress(
        double stageStartPercent,
        double stageEndPercent,
        TimeSpan elapsed,
        TimeSpan expectedDuration) =>
        OpenWorkbookProgressPlanner.CalculateStageProgress(
            stageStartPercent,
            stageEndPercent,
            elapsed,
            expectedDuration);

    private static async Task<byte[]> ReadFileBytesWithProgressAsync(
        string path,
        IProgress<OpenProgressUpdate> progress)
    {
        progress.Report(new OpenProgressUpdate("Opening workbook", FormatLoadingFileDetail("reading", TimeSpan.Zero), 1));
        await using var file = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 128,
            useAsync: true);

        var total = file.Length;
        using var memory = new MemoryStream(total > int.MaxValue ? 0 : (int)total);
        var buffer = new byte[1024 * 128];
        long readTotal = 0;
        var startTimestamp = Stopwatch.GetTimestamp();

        while (true)
        {
            var read = await file.ReadAsync(buffer);
            if (read == 0)
                break;

            memory.Write(buffer, 0, read);
            readTotal += read;
            if (total > 0)
            {
                var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
                var percent = 1 + readTotal * 7d / total;
                progress.Report(new OpenProgressUpdate(
                    "Opening workbook",
                    FormatLoadingFileDetail("reading", elapsed),
                    percent));
            }
        }

        return memory.ToArray();
    }

    public static string FormatLoadingFileDetail(string phase, TimeSpan elapsed)
        => OpenWorkbookProgressPlanner.FormatLoadingFileDetail(phase, elapsed);

    private sealed record OpenProgressUpdate(string Title, string Detail, double? Percent);

    private sealed record OpenWorkbookResult(
        Workbook Workbook,
        XlsxFeatureReport? FeatureReport,
        string DisplayName);

    private void ApplyOpenedWorksheetViewState()
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var activeRow = sheet?.ActiveRow ?? 1;
        var activeCol = sheet?.ActiveCol ?? 1;
        SetActiveCell(new CellAddress(
            _currentSheetId,
            Math.Clamp(activeRow, 1u, CellAddress.MaxRow),
            Math.Clamp(activeCol, 1u, CellAddress.MaxCol)));

        VerticalScroll.Value = CalculateOpenedWorksheetScrollValue(
            sheet?.ViewTopRow,
            1,
            CellAddress.MaxRow,
            sheet?.FrozenRows ?? 0);
        HorizontalScroll.Value = CalculateOpenedWorksheetScrollValue(
            sheet?.ViewLeftCol,
            1,
            CellAddress.MaxCol,
            sheet?.FrozenCols ?? 0);
        UpdateViewport();
    }

    private void ShowOpenProgress(string title, string detail, double? percent = null)
    {
        if (OpenProgressOverlay is null)
            return;

        OpenProgressTitle.Text = title;
        OpenProgressDetail.Text = detail;
        if (OpenProgressBar is not null)
        {
            OpenProgressBar.IsIndeterminate = !percent.HasValue;
            if (percent.HasValue)
                OpenProgressBar.Value = Math.Clamp(percent.Value, OpenProgressBar.Minimum, OpenProgressBar.Maximum);
        }
        OpenProgressOverlay.Visibility = Visibility.Visible;
        OpenProgressOverlay.UpdateLayout();
        Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
    }

    private void HideOpenProgress()
    {
        if (OpenProgressOverlay is not null)
            OpenProgressOverlay.Visibility = Visibility.Collapsed;
    }

    // Start screen button handlers
    private void SsBackBtn_Click(object sender, RoutedEventArgs e)       => HideStartScreen();
    private void SsNewBtn_Click(object sender, RoutedEventArgs e)        { CreateNewWorkbook(); HideStartScreen(); }
    private void SsBlankWorkbook_Click(object sender, RoutedEventArgs e) { CreateNewWorkbook(); HideStartScreen(); }
    private void SsOpenBtn_Click(object sender, RoutedEventArgs e)       => OpenButton_Click(sender, e);
    private void SsCloseBtn_Click(object sender, RoutedEventArgs e)      => Application.Current.Shutdown();
    private void SsHomeRibbonBtn_Click(object sender, RoutedEventArgs e) => ShowStartScreen();

    private void RibbonTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RibbonTabs.SelectedItem == FileTab)
        {
            // Switch back to Home immediately so the tab never stays selected
            RibbonTabs.SelectedIndex = 1;
            ShowStartScreen();
            NormalizeRibbonSurfaceAfterTabSelection();
            return;
        }

        NormalizeRibbonSurfaceAfterTabSelection();
    }
    private async void SsShareBtn_Click(object sender, RoutedEventArgs e)
    {
        await ShareWorkbookAsync();
    }

    private void SsAccountBtn_Click(object sender, RoutedEventArgs e)
    {
        var message = DeferredCommandMessages.LocalAccountInfo();
        MessageBox.Show(
            message.Body,
            message.Title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void SsHomeNavBtn_Click(object sender, RoutedEventArgs e)    => ShowHomeView();
    private void SsInfoBtn_Click(object sender, RoutedEventArgs e)       => ShowInfoView();

    private void SsMoreTemplatesBtn_Click(object sender, RoutedEventArgs e)
    {
        var message = DeferredCommandMessages.OnlineTemplatesExcluded();
        MessageBox.Show(
            message.Body,
            message.Title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void SsOptionsBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OptionsDialog(_options, _workbook.DisabledFormulaErrorCodes) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            _options = dlg.Result;
            ApplyFormulaErrorCheckingOptions(dlg.DisabledFormulaErrorCodesResult);
            ApplyOptionsToView();
            UpdateViewport();
        }
    }

    private void ApplyFormulaErrorCheckingOptions(IReadOnlySet<string> disabledErrorCodes)
    {
        foreach (var rule in FormulaErrorCheckingRuleCatalog.SupportedRules)
        {
            var shouldDisable = disabledErrorCodes.Contains(rule.ErrorCode);
            var isDisabled = _workbook.DisabledFormulaErrorCodes.Contains(rule.ErrorCode);
            if (shouldDisable == isDisabled)
                continue;

            if (!TryExecuteCommand(
                    new SetFormulaErrorCheckingRuleCommand(rule.ErrorCode, enabled: !shouldDisable),
                    "Error Checking Options"))
            {
                return;
            }
        }
    }

    private bool TryHandleTopLevelRibbonKeyTip(string keyTip)
    {
        return RibbonTopLevelKeyTipRouter.Resolve(keyTip) switch
        {
            { Kind: RibbonTopLevelKeyTipActionKind.BackstageFile } => OpenFileBackstageFromKeyTip(),
            { Kind: RibbonTopLevelKeyTipActionKind.RibbonTab, RibbonTabHeader: { } header } => SelectRibbonTabByHeader(header),
            _ => false
        };
    }

    private bool OpenFileBackstageFromKeyTip()
    {
        ShowStartScreen();
        if (RibbonTabs != null)
            RibbonTabs.SelectedIndex = 1;
        return true;
    }

    private bool SelectRibbonTabByHeader(string header)
    {
        if (RibbonTabs == null)
            return false;

        foreach (var item in RibbonTabs.Items)
        {
            if (item is TabItem { Header: string tabHeader } &&
                string.Equals(tabHeader, header, StringComparison.OrdinalIgnoreCase))
            {
                RibbonTabs.SelectedItem = item;
                RibbonTabs.UpdateLayout();
                NormalizeRibbonSurface(forceCompact: true);
                return true;
            }
        }

        return false;
    }

    private async void SsRecentItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.FrameworkElement)?.DataContext is RecentFileViewModel vm)
            await OpenFileAsync(vm.Path);
    }

    private void SsPinItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextMenuViewModel(sender) is { } vm)
        {
            _recentFiles.Pin(vm.Path);
            UpdateSsRecentList(SsSearchBox.Text);
        }
        e.Handled = true;
    }

    private void SsUnpinItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextMenuViewModel(sender) is { } vm)
        {
            _recentFiles.Unpin(vm.Path);
            UpdateSsRecentList(SsSearchBox.Text);
        }
        e.Handled = true;
    }

    private void SsRemoveRecentItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextMenuViewModel(sender) is { } vm)
        {
            _recentFiles.Remove(vm.Path);
            _allRecentItems.RemoveAll(x => x.Path == vm.Path);
            UpdateSsRecentList(SsSearchBox.Text);
        }
    }

    private static RecentFileViewModel? GetContextMenuViewModel(object menuItemSender)
    {
        if (menuItemSender is MenuItem mi &&
            mi.Parent is ContextMenu cm &&
            cm.PlacementTarget is FrameworkElement fe)
            return fe.DataContext as RecentFileViewModel;
        if (menuItemSender is FrameworkElement direct)
            return direct.DataContext as RecentFileViewModel;
        return null;
    }

    private void SsSearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        UpdateSsRecentList(SsSearchBox.Text);
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        var filter = string.Join("|", _fileAdapters.Select(a => $"{a.FormatName}|*{a.Extension}"));
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = filter };

        if (dialog.ShowDialog() == true)
            await OpenFileAsync(dialog.FileName);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (FileSavePlanner.TryResolveExistingPath(_currentFilePath, _fileAdapters, out var target))
        {
            SaveWorkbookToTarget(target!);
            return;
        }

        SaveWorkbookWithDialog();
    }

    private bool SaveWorkbookWithDialog()
    {
        var filter = string.Join("|", _fileAdapters.Select(a => $"{a.FormatName}|*{a.Extension}"));
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = filter,
            FileName = _workbook.Name,
            DefaultExt = ".xlsx"
        };

        if (dialog.ShowDialog() == true)
        {
            var ext = System.IO.Path.GetExtension(dialog.FileName).ToLower();
            var adapter = _fileAdapters.FirstOrDefault(a => a.Extension == ext);
            if (adapter == null)
                return false;

            return SaveWorkbookToTarget(new FileSaveTarget(dialog.FileName, adapter));
        }

        return false;
    }

    private bool SaveWorkbookToTarget(FileSaveTarget target)
    {
        var ext = System.IO.Path.GetExtension(target.Path).ToLowerInvariant();
        if (ext == ".xlsx" && !ConfirmUnsupportedXlsxFeatureSave())
            return false;

        try
        {
            using var stream = System.IO.File.Create(target.Path);
            target.Adapter.Save(_workbook, stream);
            _currentFilePath = target.Path;
            _recentFiles.AddOrUpdate(target.Path);
            UpdateTitleBar();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save file:\n{ex.Message}", "Save Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private bool ConfirmUnsupportedXlsxFeatureSave()
    {
        if (_currentXlsxFeatureReport?.HasUnsupportedFeatures != true)
            return true;

        var message = DeferredCommandMessages.UnsupportedXlsxFeatureSaveWarning(_currentXlsxFeatureReport);

        var result = MessageBox.Show(
            message.Body,
            message.Title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        return result == MessageBoxResult.Yes;
    }

    private void ShowUnsupportedXlsxFeatureOpenWarningIfNeeded()
    {
        if (_currentXlsxFeatureReport?.HasUnsupportedFeatures != true)
            return;

        var message = DeferredCommandMessages.UnsupportedXlsxFeatureOpenWarning(_currentXlsxFeatureReport);
        MessageBox.Show(
            message.Body,
            message.Title,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void FindButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new FindReplaceDialog(() => _workbook, _commandBus, NavigateToCell, replaceMode: false)
        {
            Owner = this
        };
        dlg.Show();
    }

    private void ReplaceButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new FindReplaceDialog(() => _workbook, _commandBus, NavigateToCell, replaceMode: true)
        {
            Owner = this
        };
        dlg.Show();
    }

    private void NavigateToCell(CellAddress addr)
    {
        _currentSheetId = addr.Sheet;
        SetActiveCell(addr);
        EnsureCellVisible(addr);
        UpdateViewport();
    }

    private void InsertChartButton_Click(object sender, RoutedEventArgs e)
        => InsertChartOfType(ChartType.Column);

    private void InsertChartOfType(ChartType type)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Insert Chart",
                range,
                currentRange => new AddChartCommand(_currentSheetId, currentRange, type, "Chart")))
            return;

        UpdateViewport();
    }

    private void RefreshSheetTabs()
    {
        var plan = SheetTabListPlanner.Build(_workbook, _currentSheetId, _groupedSheetIds);
        _currentSheetId = plan.CurrentSheetId;
        _sheetTabs.Clear();
        foreach (var tab in plan.Tabs)
            _sheetTabs.Add(tab);
        RefreshSheetProtectionUi();
        RefreshWorkbookProtectionUi();
        UpdateTitleBar();
    }

    private void RefreshSheetProtectionUi()
    {
        if (ProtectSheetButton is null)
            return;

        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return;

        var uiText = SheetProtectionWorkflow.GetUiText(sheet);
        ProtectSheetButton.Content = uiText.ButtonContent;
        RibbonTooltip.SetTitle(ProtectSheetButton, uiText.TooltipTitle);
        RibbonTooltip.SetDescription(ProtectSheetButton, uiText.TooltipDescription);
    }

    private void RefreshWorkbookProtectionUi()
    {
        if (ProtectWorkbookButton is null)
            return;

        var uiText = WorkbookProtectionWorkflow.GetUiText(_workbook);
        ProtectWorkbookButton.Content = uiText.ButtonContent;
        RibbonTooltip.SetTitle(ProtectWorkbookButton, uiText.TooltipTitle);
        RibbonTooltip.SetDescription(ProtectWorkbookButton, uiText.TooltipDescription);
    }

    private string GenerateUniqueSheetName()
        => SheetTabListPlanner.GenerateUniqueSheetName(_workbook);

    private static void ShowCommandError(CommandOutcome outcome, string title)
    {
        if (outcome.Success) return;

        MessageBox.Show(outcome.ErrorMessage ?? "The command could not be completed.",
            title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private bool TryExecuteCommand(IWorkbookCommand command, string title, out CommandOutcome outcome)
    {
        outcome = _commandBus.Execute(_workbook.Id, command);
        if (outcome.Success)
            return true;

        ShowCommandError(outcome, title);
        return false;
    }

    private bool TryExecuteCommand(IWorkbookCommand command, string title) =>
        TryExecuteCommand(command, title, out _);

    private IReadOnlyList<SheetId> CurrentGroupedEditSheetIds()
    {
        var groupedVisibleSheets = _workbook.Sheets
            .Where(sheet => !sheet.IsHidden && _groupedSheetIds.Contains(sheet.Id))
            .Select(sheet => sheet.Id)
            .ToList();

        return groupedVisibleSheets.Count > 1 && groupedVisibleSheets.Contains(_currentSheetId)
            ? groupedVisibleSheets
            : [_currentSheetId];
    }

    private bool TryExecuteEditCells(
        IReadOnlyList<(CellAddress Address, Cell NewCell)> edits,
        string title,
        out CommandOutcome outcome)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        IWorkbookCommand command = targetSheetIds.Count > 1
            ? new GroupedEditCellsCommand(targetSheetIds, _currentSheetId, edits)
            : new EditCellsCommand(_currentSheetId, edits);
        return TryExecuteCommand(command, title, out outcome);
    }

    private bool TryExecuteEditCells(
        IReadOnlyList<(CellAddress Address, Cell NewCell)> edits,
        string title) =>
        TryExecuteEditCells(edits, title, out _);

    private bool TryExecuteApplyStyle(GridRange range, StyleDiff diff, string title)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        IWorkbookCommand command = targetSheetIds.Count > 1
            ? new GroupedApplyStyleCommand(targetSheetIds, range, diff)
            : new ApplyStyleCommand(_currentSheetId, range, diff);
        return TryExecuteCommand(command, title);
    }

    private bool TryExecuteRepeatableApplyStyle(StyleDiff diff, string title)
    {
        IWorkbookCommand CreateCommand()
        {
            var range = SheetGrid.SelectedRange ?? new GridRange(
                new CellAddress(_currentSheetId, 1, 1),
                new CellAddress(_currentSheetId, 1, 1));
            var targetSheetIds = CurrentGroupedEditSheetIds();
            return targetSheetIds.Count > 1
                ? new GroupedApplyStyleCommand(targetSheetIds, range, diff)
                : new ApplyStyleCommand(_currentSheetId, range, diff);
        }

        var outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateCommand);
        if (outcome.Success)
        {
            _repeatPostAction = null;
            return true;
        }

        ShowCommandError(outcome, title);
        return false;
    }

    private bool TryExecuteRepeatableGroupedSheetCommand(
        string title,
        Func<SheetId, IWorkbookCommand> createCommand,
        out CommandOutcome outcome)
    {
        IWorkbookCommand CreateRepeatCommand()
        {
            var targetSheetIds = CurrentGroupedEditSheetIds();
            return targetSheetIds.Count > 1
                ? new CompositeWorkbookCommand(title, targetSheetIds.Select(createCommand).ToList())
                : createCommand(_currentSheetId);
        }

        outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateRepeatCommand);
        if (outcome.Success)
        {
            _repeatPostAction = null;
            return true;
        }

        ShowCommandError(outcome, title);
        return false;
    }

    private bool TryExecuteRepeatableGroupedSheetCommand(
        string title,
        Func<SheetId, IWorkbookCommand> createCommand) =>
        TryExecuteRepeatableGroupedSheetCommand(title, createCommand, out _);

    private bool TryExecuteRepeatableCurrentRangeCommand(
        string title,
        GridRange fallbackRange,
        Func<GridRange, IWorkbookCommand> createCommand,
        out CommandOutcome outcome)
    {
        IWorkbookCommand CreateRepeatCommand()
        {
            var range = SheetGrid.SelectedRange ?? fallbackRange;
            return createCommand(range);
        }

        outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateRepeatCommand);
        if (outcome.Success)
        {
            _repeatPostAction = null;
            return true;
        }

        ShowCommandError(outcome, title);
        return false;
    }

    private bool TryExecuteRepeatableCurrentRangeCommand(
        string title,
        GridRange fallbackRange,
        Func<GridRange, IWorkbookCommand> createCommand) =>
        TryExecuteRepeatableCurrentRangeCommand(title, fallbackRange, createCommand, out _);

    private bool TryExecuteRepeatableChartLayout(
        string caption,
        string missingMessage,
        Func<ChartModel, bool>? canApply,
        string? unsupportedMessage,
        Func<ChartModel, ChartLayoutOptions> optionsFactory)
    {
        IWorkbookCommand CreateCommand()
        {
            var sheet = _workbook.GetSheet(_currentSheetId);
            var chart = sheet?.Charts.FirstOrDefault();
            if (chart is null)
                return new FailedWorkbookCommand(missingMessage);
            if (canApply is not null && !canApply(chart))
                return new FailedWorkbookCommand(unsupportedMessage ?? "This chart command is not supported for the selected chart.");
            return new SetChartLayoutCommand(_currentSheetId, chart.Id, optionsFactory(chart));
        }

        var outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateCommand);
        if (outcome.Success)
        {
            _repeatPostAction = null;
            return true;
        }

        ShowCommandError(outcome, caption);
        return false;
    }

    private bool TryExecuteGroupedSheetCommand(
        string title,
        Func<SheetId, IWorkbookCommand> createCommand,
        out CommandOutcome outcome)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        IWorkbookCommand command = targetSheetIds.Count > 1
            ? new CompositeWorkbookCommand(title, targetSheetIds.Select(createCommand).ToList())
            : createCommand(_currentSheetId);
        return TryExecuteCommand(command, title, out outcome);
    }

    private bool TryExecuteGroupedSheetCommand(
        string title,
        Func<SheetId, IWorkbookCommand> createCommand) =>
        TryExecuteGroupedSheetCommand(title, createCommand, out _);

    private static GridRange RemapRangeToSheet(GridRange range, SheetId sheetId) =>
        new(new CellAddress(sheetId, range.Start.Row, range.Start.Col),
            new CellAddress(sheetId, range.End.Row, range.End.Col));

    private static ConditionalFormat CloneConditionalFormatForSheet(ConditionalFormat source, SheetId sheetId) =>
        new()
        {
            AppliesTo = RemapRangeToSheet(source.AppliesTo, sheetId),
            Priority = source.Priority,
            RuleType = source.RuleType,
            Operator = source.Operator,
            Value1 = source.Value1,
            Value2 = source.Value2,
            FormatIfTrue = source.FormatIfTrue?.Clone(),
            MinColor = source.MinColor,
            MidColor = source.MidColor,
            MaxColor = source.MaxColor,
            UseThreeColorScale = source.UseThreeColorScale,
            DataBarColor = source.DataBarColor,
            AboveAverage = source.AboveAverage,
            FormulaText  = source.FormulaText,
            StopIfTrue   = source.StopIfTrue
        };

    private static DataValidation CloneDataValidationForSheet(DataValidation source, SheetId sheetId) =>
        new()
        {
            AppliesTo = RemapRangeToSheet(source.AppliesTo, sheetId),
            Type = source.Type,
            Operator = source.Operator,
            Formula1 = source.Formula1,
            Formula2 = source.Formula2,
            AllowBlank = source.AllowBlank,
            ShowDropdown = source.ShowDropdown,
            AlertStyle = source.AlertStyle,
            ShowInputMessage = source.ShowInputMessage,
            ShowErrorMessage = source.ShowErrorMessage,
            ErrorTitle = source.ErrorTitle,
            ErrorMessage = source.ErrorMessage,
            PromptTitle = source.PromptTitle,
            PromptMessage = source.PromptMessage
        };

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

    private static string? PromptForInput(string prompt, string defaultValue)
    {
        var win = new Window
        {
            Title = prompt, Width = 300, Height = 120,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize
        };
        var tb = new System.Windows.Controls.TextBox { Text = defaultValue, Margin = new Thickness(10) };
        var btn = new System.Windows.Controls.Button { Content = "OK", Margin = new Thickness(10, 0, 10, 10) };
        var sp = new System.Windows.Controls.StackPanel();
        sp.Children.Add(tb);
        sp.Children.Add(btn);
        win.Content = sp;
        string? result = null;
        btn.Click += (_, _) => { result = tb.Text; win.Close(); };
        win.ShowDialog();
        return result;
    }

    private void SortAscButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Sort",
                range,
                currentRange => new SortCommand(_currentSheetId, currentRange, sortByColOffset: 0, ascending: true)))
            return;
        UpdateViewport();
    }

    private void SortDescButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Sort",
                range,
                currentRange => new SortCommand(_currentSheetId, currentRange, sortByColOffset: 0, ascending: false)))
            return;
        UpdateViewport();
    }

    private void SortCustomButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var input = PromptForInput("Sort keys (for example 1 asc; 2 desc):", "1 asc");
        if (input is null) return;

        if (!SortInputParser.TryParse(input, out var keys, out var error))
        {
            MessageBox.Show(error ?? "Enter sort keys such as 1 asc; 2 desc.",
                "Custom Sort", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Sort",
                range,
                currentRange => new SortCommand(_currentSheetId, currentRange, keys)))
            return;
        UpdateViewport();
    }

    private void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var value = PromptForInput("Filter: values separated by comma/semicolon, top:n, bottom:n, toppercent:n, bottompercent:n, aboveavg, belowavg, blank, nonblank, text=value, text<>value, contains:text, notcontains:text, begins:text, ends:text, between:min:max, date=yyyy-mm-dd, date<>yyyy-mm-dd, date>yyyy-mm-dd, date>=yyyy-mm-dd, date<yyyy-mm-dd, date<=yyyy-mm-dd, datebetween:start:end, >number, >=number, <number, <=number, =number, or <>number", "");
        if (value is null) return;  // user cancelled
        var filterText = value.TrimStart();
        if (filterText.StartsWith("top:", StringComparison.OrdinalIgnoreCase) ||
            filterText.StartsWith("toppercent:", StringComparison.OrdinalIgnoreCase) ||
            filterText.StartsWith("bottompercent:", StringComparison.OrdinalIgnoreCase) ||
            filterText.StartsWith("bottom:", StringComparison.OrdinalIgnoreCase))
        {
            if (!FilterInputParser.TryParseTopBottom(value, out var count, out var top, out var percent, out var error))
            {
                MessageBox.Show(error ?? "Enter top:n, bottom:n, toppercent:n, or bottompercent:n.",
                    "Filter", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TryExecuteRepeatableCurrentRangeCommand(
                    "Filter",
                    range,
                    currentRange => percent
                        ? TopBottomFilterCommand.Percent(_currentSheetId, currentRange, filterColOffset: 0, count, top)
                        : new TopBottomFilterCommand(_currentSheetId, currentRange, filterColOffset: 0, count, top)))
                return;
            UpdateViewport();
            return;
        }

        if (FilterInputParser.TryParseAverage(value, out var aboveAverage))
        {
            if (!TryExecuteRepeatableCurrentRangeCommand(
                    "Filter",
                    range,
                    currentRange => new AverageFilterCommand(_currentSheetId, currentRange, filterColOffset: 0, aboveAverage)))
                return;
            UpdateViewport();
            return;
        }

        if (filterText.Equals("blank", StringComparison.OrdinalIgnoreCase) ||
            filterText.Equals("nonblank", StringComparison.OrdinalIgnoreCase) ||
            filterText.Equals("non-blank", StringComparison.OrdinalIgnoreCase) ||
            filterText.StartsWith("date=", StringComparison.OrdinalIgnoreCase) ||
            filterText.StartsWith("date>", StringComparison.OrdinalIgnoreCase) ||
            filterText.StartsWith("date<", StringComparison.OrdinalIgnoreCase) ||
            filterText.StartsWith("datebetween:", StringComparison.OrdinalIgnoreCase) ||
            filterText.StartsWith("contains:", StringComparison.OrdinalIgnoreCase) ||
            filterText.StartsWith("notcontains:", StringComparison.OrdinalIgnoreCase) ||
            filterText.StartsWith("begins:", StringComparison.OrdinalIgnoreCase) ||
            filterText.StartsWith("ends:", StringComparison.OrdinalIgnoreCase) ||
            filterText.StartsWith("text=", StringComparison.OrdinalIgnoreCase) ||
            filterText.StartsWith("text<>", StringComparison.OrdinalIgnoreCase) ||
            filterText.StartsWith("between:", StringComparison.OrdinalIgnoreCase) ||
            filterText.StartsWith('>') ||
            filterText.StartsWith('<') ||
            filterText.StartsWith('='))
        {
            if (!FilterInputParser.TryParseCriterion(value, out var criterion, out var error) || criterion is null)
            {
                MessageBox.Show(error ?? "Enter a supported filter criterion.",
                    "Filter", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TryExecuteRepeatableCurrentRangeCommand(
                    "Filter",
                    range,
                    currentRange => new FilterConditionCommand(_currentSheetId, currentRange, filterColOffset: 0, criterion)))
                return;
            UpdateViewport();
            return;
        }

        var allowedValues = FilterInputParser.ParseAllowedValues(value);
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Filter",
                range,
                currentRange => new FilterCommand(_currentSheetId, currentRange, filterColOffset: 0, allowedValues: allowedValues)))
            return;
        UpdateViewport();
    }

    private void CfRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range)
        {
            MessageBox.Show("Select a range first.", "CF Rule");
            return;
        }

        var thresholdText = PromptForInput("Conditional Format: highlight cells greater than", "0");
        if (string.IsNullOrWhiteSpace(thresholdText)) return;

        var cf = new ConditionalFormat
        {
            AppliesTo    = range,
            Priority     = 1,
            RuleType     = CfRuleType.CellValue,
            Operator     = CfOperator.GreaterThan,
            Value1       = thresholdText.Trim(),
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) }
        };

        if (!TryExecuteGroupedSheetCommand(
                "Conditional Formatting",
                sheetId => new ApplyConditionalFormatCommand(sheetId, CloneConditionalFormatForSheet(cf, sheetId))))
            return;
        UpdateViewport();
    }

    private void ValidationButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range)
        {
            MessageBox.Show("Select a range first.", "Data Validation");
            return;
        }

        var sheet = _workbook.GetSheet(_currentSheetId);
        var dlg = new DataValidationDialog
        {
            Owner = this,
            SelectionSource = DataValidationService.FormatListSourceRange(range, sheet?.Name, sheet?.Name)
        };
        if (dlg.ShowDialog() != true) return;

        if (dlg.ClearRequested)
        {
            if (!TryExecuteRepeatableGroupedSheetCommand(
                    "Clear Data Validation",
                    sheetId => new ClearDataValidationCommand(sheetId, RemapRangeToSheet(SheetGrid.SelectedRange ?? range, sheetId))))
                return;

            UpdateViewport();
            return;
        }

        if (dlg.Result == null) return;

        var dv = dlg.Result;
        dv.AppliesTo = range;

        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Data Validation",
                sheetId =>
                {
                    var rule = CloneDataValidationForSheet(dv, sheetId);
                    rule.AppliesTo = RemapRangeToSheet(SheetGrid.SelectedRange ?? range, sheetId);
                    return new SetDataValidationCommand(sheetId, rule);
                }))
            return;
        UpdateViewport();
    }

    private void ClearFilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Filter",
                range,
                currentRange => new FilterCommand(_currentSheetId, currentRange, filterColOffset: 0, allowedValues: [])))
            return;
        UpdateViewport();
    }

    // ── Group / Ungroup handlers ─────────────────────────────────────────────

    private void GroupRowsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableCurrentRangeCommand("Group", range, CreateGroupCommand))
            return;
        UpdateViewport();
    }

    private void UngroupRowsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Ungroup",
                range,
                currentRange => OutlineGroupingService.GetGroupingAxis(currentRange) == OutlineGroupingAxis.Columns
                    ? new GroupColumnsCommand(_currentSheetId, currentRange.Start.Col, currentRange.End.Col, 0)
                    : new GroupRowsCommand(_currentSheetId, currentRange.Start.Row, currentRange.End.Row, 0)))
            return;

        UpdateViewport();
    }

    private void CollapseGroupBtn_Click(object sender, RoutedEventArgs e)
    {
        IWorkbookCommand CreateCommand()
        {
            var axis = SheetGrid.SelectedRange is { } range
                ? OutlineGroupingService.GetGroupingAxis(range)
                : OutlineGroupingAxis.Rows;
            return axis == OutlineGroupingAxis.Columns
                ? new CollapseColGroupCommand(_currentSheetId, 1)
                : new CollapseRowGroupCommand(_currentSheetId, 1);
        }

        var outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateCommand);
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Collapse Group");
            return;
        }

        _repeatPostAction = null;
        UpdateViewport();
    }

    private void ExpandGroupBtn_Click(object sender, RoutedEventArgs e)
    {
        IWorkbookCommand CreateCommand()
        {
            var axis = SheetGrid.SelectedRange is { } range
                ? OutlineGroupingService.GetGroupingAxis(range)
                : OutlineGroupingAxis.Rows;
            return axis == OutlineGroupingAxis.Columns
                ? new ExpandColGroupCommand(_currentSheetId, 1)
                : new ExpandRowGroupCommand(_currentSheetId, 1);
        }

        var outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateCommand);
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Expand Group");
            return;
        }

        _repeatPostAction = null;
        UpdateViewport();
    }

    private IWorkbookCommand CreateGroupCommand(GridRange range)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return new GroupRowsCommand(_currentSheetId, range.Start.Row, range.End.Row, 1);

        if (OutlineGroupingService.GetGroupingAxis(range) == OutlineGroupingAxis.Columns)
        {
            int newLevel = GetNextOutlineLevel(range.Start.Col, range.End.Col, sheet.ColOutlineLevels);
            return new GroupColumnsCommand(_currentSheetId, range.Start.Col, range.End.Col, newLevel);
        }

        int rowLevel = GetNextOutlineLevel(range.Start.Row, range.End.Row, sheet.RowOutlineLevels);
        return new GroupRowsCommand(_currentSheetId, range.Start.Row, range.End.Row, rowLevel);
    }

    private static int GetNextOutlineLevel(uint start, uint end, IReadOnlyDictionary<uint, int> outlineLevels)
    {
        int maxExisting = 0;
        for (uint index = start; index <= end; index++)
        {
            if (outlineLevels.TryGetValue(index, out var level) && level > maxExisting)
                maxExisting = level;
        }

        return Math.Min(maxExisting + 1, 8);
    }

    private void NamedRangesButton_Click(object sender, RoutedEventArgs e)
    {
        var initialRange = SheetGrid.SelectedRange;
        var dlg = new NamedRangeDialog(_workbook, _commandBus, initialRange)
        {
            Owner = this
        };
        dlg.ShowDialog();
        UpdateViewport();
    }

    // ── Formatting toolbar handlers ───────────────────────────────────────────

    private void BoldButton_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        ApplyStyleDiff(new StyleDiff(Bold: BoldButton.IsChecked == true));
    }

    private void ItalicButton_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        ApplyStyleDiff(new StyleDiff(Italic: ItalicButton.IsChecked == true));
    }

    private void UnderlineButton_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        var enabled = UnderlineButton.IsChecked == true;
        SetToolbarToggleStates(strike: enabled ? false : null);
        ApplyStyleDiff(new StyleDiff(Underline: enabled, Strikethrough: enabled ? false : null));
    }

    private void StrikeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        var enabled = StrikeButton.IsChecked == true;
        SetToolbarToggleStates(underline: enabled ? false : null);
        ApplyStyleDiff(new StyleDiff(Strikethrough: enabled, Underline: enabled ? false : null, DoubleUnderline: enabled ? false : null));
    }

    private void AlignLeftBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        AlignCenterBtn.IsChecked = false;
        AlignRightBtn.IsChecked  = false;
        ApplyStyleDiff(new StyleDiff(HAlign: CellHAlign.Left));
    }

    private void AlignCenterBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        AlignLeftBtn.IsChecked  = false;
        AlignRightBtn.IsChecked = false;
        ApplyStyleDiff(new StyleDiff(HAlign: CellHAlign.Center));
    }

    private void AlignRightBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        AlignLeftBtn.IsChecked   = false;
        AlignCenterBtn.IsChecked = false;
        ApplyStyleDiff(new StyleDiff(HAlign: CellHAlign.Right));
    }

    private void SetToolbarToggleStates(
        bool? underline = null,
        bool? strike = null,
        bool? top = null,
        bool? middle = null,
        bool? bottom = null)
    {
        _suppressToolbarSync = true;
        try
        {
            if (underline.HasValue) UnderlineButton.IsChecked = underline.Value;
            if (strike.HasValue) StrikeButton.IsChecked = strike.Value;
            if (top.HasValue) AlignTopBtn.IsChecked = top.Value;
            if (middle.HasValue) AlignMiddleBtn.IsChecked = middle.Value;
            if (bottom.HasValue) AlignBottomBtn.IsChecked = bottom.Value;
        }
        finally
        {
            _suppressToolbarSync = false;
        }
    }

    private void WrapTextBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        ApplyStyleDiff(new StyleDiff(WrapText: WrapTextBtn.IsChecked == true));
    }

    private void MergeCenterBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Merge & Center",
                range,
                CreateMergeAndCenterCommand,
                out _))
            return;

        UpdateViewport();
    }

    private IWorkbookCommand CreateMergeAndCenterCommand(GridRange range)
    {
        var targetSheetIds = CurrentGroupedEditSheetIds();
        if (targetSheetIds.Count > 1)
        {
            var commands = targetSheetIds
                .SelectMany(sheetId =>
                {
                    var sheetRange = RemapRangeToSheet(range, sheetId);
                    return new IWorkbookCommand[]
                    {
                        new MergeCellsCommand(sheetId, sheetRange),
                        new ApplyStyleCommand(sheetId, sheetRange, new StyleDiff(HAlign: CellHAlign.Center))
                    };
                })
                .ToList();
            return new CompositeWorkbookCommand("Merge & Center", commands);
        }

        return new CompositeWorkbookCommand(
            "Merge & Center",
            [
                new MergeCellsCommand(_currentSheetId, range),
                new ApplyStyleCommand(_currentSheetId, range, new StyleDiff(HAlign: CellHAlign.Center))
            ]);
    }

    private void FontNameBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        if (FontNameBox.SelectedItem is string name)
            ApplyStyleDiff(new StyleDiff(FontName: name));
    }

    private void FontSizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        var text = FontSizeBox.Text;
        if (double.TryParse(text, out var size) && size > 0)
            ApplyStyleDiff(new StyleDiff(FontSize: size));
    }

    private void FontColorBtn_Click(object sender, RoutedEventArgs e)
    {
        var input = PromptForInput("Font color (R,G,B e.g. 255,0,0):", "0,0,0");
        if (input is null) return;
        var parts = input.Split(',');
        if (parts.Length == 3 && byte.TryParse(parts[0].Trim(), out var r)
            && byte.TryParse(parts[1].Trim(), out var g) && byte.TryParse(parts[2].Trim(), out var b))
            ApplyStyleDiff(new StyleDiff(FontColor: new CellColor(r, g, b)));
    }

    private void FillColorBtn_Click(object sender, RoutedEventArgs e)
    {
        var input = PromptForInput("Fill color (R,G,B e.g. 255,255,0):", "255,255,255");
        if (input is null) return;
        var parts = input.Split(',');
        if (parts.Length == 3 && byte.TryParse(parts[0].Trim(), out var r)
            && byte.TryParse(parts[1].Trim(), out var g) && byte.TryParse(parts[2].Trim(), out var b))
            ApplyStyleDiff(new StyleDiff(FillColor: new CellColor(r, g, b)));
    }

    private void NumberFormatBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        if (NumberFormatBox.SelectedIndex < 0) return;
        var codes = new[] { "General", "0.00", "$#,##0.00", "0%", "yyyy-MM-dd", "HH:mm:ss", "@" };
        if (NumberFormatBox.SelectedIndex < codes.Length)
            ApplyStyleDiff(new StyleDiff(NumberFormat: codes[NumberFormatBox.SelectedIndex]));
    }

    private void ExecuteUndo()
    {
        var outcome = _commandBus.Undo(_workbook.Id);
        if (!outcome.Success) return;
        RecalculateWorkbook();
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private void ExecuteRedo()
    {
        var outcome = _commandBus.Redo(_workbook.Id);
        if (!outcome.Success) return;
        RecalculateWorkbook();
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private void ExecuteRepeatLast()
    {
        var postAction = _repeatPostAction;
        var outcome = _commandBus.RepeatLast(_workbook.Id);
        if (!outcome.Success) return;
        postAction?.Invoke(outcome);
        RecalculateWorkbook();
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
    }

    // ── Ribbon clipboard ─────────────────────────────────────────────────────

    private void CutBtn_Click(object sender, RoutedEventArgs e)   { ExecuteCopy(isCut: true); }
    private void CopyBtn_Click(object sender, RoutedEventArgs e)  { ExecuteCopy(); }
    private void PasteBtn_Click(object sender, RoutedEventArgs e) { ExecutePaste(); }

    // ── Ribbon cells (insert / delete rows & columns) ────────────────────────

    private void InsertRowBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        InsertRows(range.Start.Row);
    }

    private void DeleteRowBtn_Click(object sender, RoutedEventArgs e) => DeleteSelectedRows();

    private void InsertColBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        InsertColumns(range.Start.Col);
    }

    private void DeleteColBtn_Click(object sender, RoutedEventArgs e) => DeleteSelectedColumns();

    // ── Context menu + Insert/Delete ─────────────────────────────────────────

    private void OnGridContextMenuRequested(CellAddress clickedCell, System.Windows.Point screenPos)
    {
        var actualAddr = new CellAddress(_currentSheetId, clickedCell.Row, clickedCell.Col);
        if (SheetGrid.SelectedRange is null)
            SetActiveCell(actualAddr);

        var menu = new ContextMenu();
        void AddItem(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        AddItem("Cut",   () => ExecuteCopy(isCut: true));
        AddItem("Copy",  () => ExecuteCopy());
        AddItem("Paste", () => ExecutePaste());
        menu.Items.Add(new Separator());
        AddItem("Insert Row Above",    () => InsertRows(actualAddr.Row));
        AddItem("Insert Row Below",    () => InsertRows(actualAddr.Row + 1));
        AddItem("Insert Column Left",  () => InsertColumns(actualAddr.Col));
        AddItem("Insert Column Right", () => InsertColumns(actualAddr.Col + 1));
        menu.Items.Add(new Separator());
        AddItem("Delete Row(s)",    DeleteSelectedRows);
        AddItem("Delete Column(s)", DeleteSelectedColumns);
        menu.Items.Add(new Separator());
        AddItem("Format Cells...",  OpenFormatCellsDialog);
        menu.Items.Add(new Separator());
        AddItem("Clear Contents",   ExecuteClearSelection);

        MenuKeyTipAssigner.AssignUniqueKeyTips(menu.Items.OfType<MenuItem>());
        menu.PlacementTarget = SheetGrid;
        menu.IsOpen = true;
    }

    private void InsertRows(uint beforeRow)
    {
        if (!TryExecuteRepeatableGroupedSheetCommand("Insert Row", sheetId => new InsertRowsCommand(sheetId, beforeRow)))
            return;

        RecalculateWorkbook();
        UpdateViewport();
    }

    private void InsertColumns(uint beforeCol)
    {
        if (!TryExecuteRepeatableGroupedSheetCommand("Insert Column", sheetId => new InsertColumnsCommand(sheetId, beforeCol)))
            return;

        RecalculateWorkbook();
        UpdateViewport();
    }

    private void DeleteSelectedRows()
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Delete Row",
                sheetId =>
                {
                    var currentRange = SheetGrid.SelectedRange ?? range;
                    var count = currentRange.End.Row - currentRange.Start.Row + 1;
                    return new DeleteRowsCommand(sheetId, currentRange.Start.Row, count);
                }))
            return;

        RecalculateWorkbook();
        UpdateViewport();
    }

    private void DeleteSelectedColumns()
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Delete Column",
                sheetId =>
                {
                    var currentRange = SheetGrid.SelectedRange ?? range;
                    var count = currentRange.End.Col - currentRange.Start.Col + 1;
                    return new DeleteColumnsCommand(sheetId, currentRange.Start.Col, count);
                }))
            return;

        RecalculateWorkbook();
        UpdateViewport();
    }

    private void ApplyNumberFormatShortcut(NumberFormatShortcut shortcut) =>
        ApplyStyleDiff(new StyleDiff(NumberFormat: NumberFormatShortcutService.GetFormat(shortcut)));

    private void ApplyFontToggleShortcut(FontToggleShortcut shortcut, ToggleButton button)
    {
        var enabled = !(button.IsChecked == true);
        if (shortcut == FontToggleShortcut.Underline)
        {
            SetToolbarToggleStates(underline: enabled, strike: enabled ? false : null);
            ApplyStyleDiff(new StyleDiff(Underline: enabled, Strikethrough: enabled ? false : null));
            return;
        }

        if (shortcut == FontToggleShortcut.Strikethrough)
        {
            SetToolbarToggleStates(strike: enabled, underline: enabled ? false : null);
            ApplyStyleDiff(new StyleDiff(Strikethrough: enabled, Underline: enabled ? false : null, DoubleUnderline: enabled ? false : null));
            return;
        }

        button.IsChecked = enabled;
        ApplyStyleDiff(FontToggleShortcutService.CreateDiff(shortcut, enabled));
    }

    private void ApplyOutlineBorderShortcut()
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        for (uint r = range.Start.Row; r <= range.End.Row; r++)
        {
            for (uint c = range.Start.Col; c <= range.End.Col; c++)
            {
                var address = new CellAddress(_currentSheetId, r, c);
                var diff = BorderShortcutService.GetOutlineBorderDiff(range, address);
                if (!TryExecuteApplyStyle(new GridRange(address, address), diff, "Outline Border"))
                    return;
            }
        }

        UpdateViewport();
    }

    private void ExecuteKeyboardInsert()
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        if (SelectionRangeService.IsWholeRowSelection(range))
        {
            if (!TryExecuteRepeatableGroupedSheetCommand(
                    "Insert Row",
                    sheetId =>
                    {
                        var currentRange = SheetGrid.SelectedRange ?? range;
                        return new InsertRowsCommand(sheetId, currentRange.Start.Row, currentRange.RowCount);
                    }))
                return;
        }
        else if (SelectionRangeService.IsWholeColumnSelection(range))
        {
            if (!TryExecuteRepeatableGroupedSheetCommand(
                    "Insert Column",
                    sheetId =>
                    {
                        var currentRange = SheetGrid.SelectedRange ?? range;
                        return new InsertColumnsCommand(sheetId, currentRange.Start.Col, currentRange.ColCount);
                    }))
                return;
        }
        else if (!TryExecuteRepeatableGroupedSheetCommand(
                     "Insert Cells",
                     sheetId => new InsertCellsCommand(
                         sheetId,
                         RemapRangeToSheet(SheetGrid.SelectedRange ?? range, sheetId),
                         InsertCellsShiftDirection.Down)))
        {
            return;
        }

        RecalculateWorkbook();
        UpdateViewport();
    }

    private void ExecuteKeyboardDelete()
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        if (SelectionRangeService.IsWholeRowSelection(range))
        {
            if (!TryExecuteRepeatableGroupedSheetCommand(
                    "Delete Row",
                    sheetId =>
                    {
                        var currentRange = SheetGrid.SelectedRange ?? range;
                        return new DeleteRowsCommand(sheetId, currentRange.Start.Row, currentRange.RowCount);
                    }))
                return;
        }
        else if (SelectionRangeService.IsWholeColumnSelection(range))
        {
            if (!TryExecuteRepeatableGroupedSheetCommand(
                    "Delete Column",
                    sheetId =>
                    {
                        var currentRange = SheetGrid.SelectedRange ?? range;
                        return new DeleteColumnsCommand(sheetId, currentRange.Start.Col, currentRange.ColCount);
                    }))
                return;
        }
        else if (!TryExecuteRepeatableGroupedSheetCommand(
                     "Delete Cells",
                     sheetId => new DeleteCellsCommand(
                         sheetId,
                         RemapRangeToSheet(SheetGrid.SelectedRange ?? range, sheetId),
                         DeleteCellsShiftDirection.Up)))
        {
            return;
        }

        RecalculateWorkbook();
        UpdateViewport();
    }

    private void ExecuteRowsHidden(bool hidden)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableGroupedSheetCommand(
                hidden ? "Hide Row" : "Unhide Row",
                sheetId =>
                {
                    var currentRange = SheetGrid.SelectedRange ?? range;
                    var (startRow, endRow) = SelectionRangeService.GetRowSpan(currentRange);
                    return new SetRowsHiddenCommand(sheetId, startRow, endRow, hidden);
                }))
            return;

        UpdateViewport();
    }

    private void ExecuteColumnsHidden(bool hidden)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableGroupedSheetCommand(
                hidden ? "Hide Column" : "Unhide Column",
                sheetId =>
                {
                    var currentRange = SheetGrid.SelectedRange ?? range;
                    var (startCol, endCol) = SelectionRangeService.GetColumnSpan(currentRange);
                    return new SetColumnsHiddenCommand(sheetId, startCol, endCol, hidden);
                }))
            return;

        UpdateViewport();
    }

    private void OpenFormatCellsDialog()
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;
        var currentStyle = _workbook.GetStyle(sheet.GetCell(range.Start)?.StyleId ?? StyleId.Default);
        var dlg = new FormatCellsDialog(currentStyle) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.ResultDiff is null) return;
        ApplyStyleDiff(dlg.ResultDiff);
    }

    private void OnAutofillRequested(GridRange sourceRange, GridRange fillRange)
    {
        var cmd = new AutofillCommand(_currentSheetId, sourceRange, fillRange);
        if (!TryExecuteCommand(cmd, "Autofill"))
            return;

        RecalculateIfAutomatic(fillRange.AllCells().ToList());
        UpdateViewport();
        RefreshStatusBar();
    }

    private void RefreshStatusBar()
    {
        if (SheetGrid.SelectedRange is not { } range)
        {
            StatusStatsPanel.Visibility = Visibility.Collapsed;
            StatusReadyText.Visibility  = Visibility.Visible;
            StatusReadyText.Text = "Ready";
            return;
        }

        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        var stats = StatusBarCalculator.Calculate(sheet, range);

        if (stats.Count == 0)
        {
            StatusStatsPanel.Visibility = Visibility.Collapsed;
            StatusReadyText.Visibility  = Visibility.Visible;
            StatusReadyText.Text = StatusBarCalculator.GetReadyStatusText(sheet, range.Start);
            return;
        }

        StatusReadyText.Visibility  = Visibility.Collapsed;
        StatusStatsPanel.Visibility = Visibility.Visible;
        StatusAvgText.Text   = stats.Average.HasValue ? $"Average: {StatusBarCalculator.FormatNumber(stats.Average.Value)}" : "";
        StatusCountText.Text = $"Count: {stats.Count}";
        StatusSumText.Text   = $"Sum: {StatusBarCalculator.FormatNumber(stats.Sum)}";
        StatusMinText.Text   = stats.Min.HasValue ? $"Min: {StatusBarCalculator.FormatNumber(stats.Min.Value)}" : "";
        StatusMaxText.Text   = stats.Max.HasValue ? $"Max: {StatusBarCalculator.FormatNumber(stats.Max.Value)}" : "";
    }

    private (uint start, uint end) GetSelectedColRange(uint col)
    {
        var sel = SheetGrid.SelectedRange;
        if (sel.HasValue && col >= sel.Value.Start.Col && col <= sel.Value.End.Col
            && sel.Value.Start.Col != sel.Value.End.Col)
            return (sel.Value.Start.Col, sel.Value.End.Col);
        return (col, col);
    }

    private (uint start, uint end) GetSelectedRowRange(uint row)
    {
        var sel = SheetGrid.SelectedRange;
        if (sel.HasValue && row >= sel.Value.Start.Row && row <= sel.Value.End.Row
            && sel.Value.Start.Row != sel.Value.End.Row)
            return (sel.Value.Start.Row, sel.Value.End.Row);
        return (row, row);
    }

    private void OnColumnResizing(uint col, double newWidthPx)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet == null) return;
        var (startCol, endCol) = GetSelectedColRange(col);
        CaptureColumnResizeSnapshot(sheet, startCol, endCol);
        for (uint c = startCol; c <= endCol; c++)
            sheet.ColumnWidths[c] = newWidthPx / 8.0;
        UpdateViewport();
    }

    private void OnColumnResized(uint col, double newWidthPx)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet == null) return;
        var (startCol, endCol) = _columnResizeSnapshot is { } snap
            ? (snap.StartCol, snap.EndCol)
            : GetSelectedColRange(col);
        RestoreColumnResizeSnapshot(sheet);
        _columnResizeSnapshot = null;
        if (!TryExecuteGroupedSheetCommand("Column Width", sheetId => new SetColumnWidthCommand(sheetId, startCol, endCol, newWidthPx / 8.0)))
            return;
        UpdateViewport();
    }

    private void OnRowResizing(uint row, double newHeightPx)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet == null) return;
        var (startRow, endRow) = GetSelectedRowRange(row);
        CaptureRowResizeSnapshot(sheet, startRow, endRow);
        for (uint r = startRow; r <= endRow; r++)
            sheet.RowHeights[r] = newHeightPx;
        UpdateViewport();
    }

    private void OnRowResized(uint row, double newHeightPx)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet == null) return;
        var (startRow, endRow) = _rowResizeSnapshot is { } snap
            ? (snap.StartRow, snap.EndRow)
            : GetSelectedRowRange(row);
        RestoreRowResizeSnapshot(sheet);
        _rowResizeSnapshot = null;
        if (!TryExecuteGroupedSheetCommand("Row Height", sheetId => new SetRowHeightCommand(sheetId, startRow, endRow, newHeightPx)))
            return;
        UpdateViewport();
    }

    private void OnPageMarginsChanged(WorksheetPageMargins margins)
    {
        if (!TryExecuteGroupedSheetCommand("Page Margins", sheetId => new SetPageMarginsCommand(sheetId, margins)))
            return;

        UpdateViewport();
        RefreshStatusBar();
    }

    private void CaptureColumnResizeSnapshot(Sheet sheet, uint startCol, uint endCol)
    {
        if (_columnResizeSnapshot is { } existing &&
            existing.SheetId == sheet.Id &&
            existing.StartCol == startCol && existing.EndCol == endCol)
            return;

        var widths = new Dictionary<uint, (bool, double)>();
        for (uint c = startCol; c <= endCol; c++)
        {
            var had = sheet.ColumnWidths.TryGetValue(c, out var w);
            widths[c] = (had, w);
        }
        _columnResizeSnapshot = new ColumnResizeSnapshot(sheet.Id, startCol, endCol, widths);
    }

    private void RestoreColumnResizeSnapshot(Sheet sheet)
    {
        if (_columnResizeSnapshot is not { } snapshot || snapshot.SheetId != sheet.Id) return;
        foreach (var (c, (had, w)) in snapshot.Widths)
        {
            if (had) sheet.ColumnWidths[c] = w;
            else sheet.ColumnWidths.Remove(c);
        }
    }

    private void CaptureRowResizeSnapshot(Sheet sheet, uint startRow, uint endRow)
    {
        if (_rowResizeSnapshot is { } existing &&
            existing.SheetId == sheet.Id &&
            existing.StartRow == startRow && existing.EndRow == endRow)
            return;

        var heights = new Dictionary<uint, (bool, double)>();
        for (uint r = startRow; r <= endRow; r++)
        {
            var had = sheet.RowHeights.TryGetValue(r, out var h);
            heights[r] = (had, h);
        }
        _rowResizeSnapshot = new RowResizeSnapshot(sheet.Id, startRow, endRow, heights);
    }

    private void RestoreRowResizeSnapshot(Sheet sheet)
    {
        if (_rowResizeSnapshot is not { } snapshot || snapshot.SheetId != sheet.Id) return;
        foreach (var (r, (had, h)) in snapshot.Heights)
        {
            if (had) sheet.RowHeights[r] = h;
            else sheet.RowHeights.Remove(r);
        }
    }

    private void ExecuteCopy(bool isCut = false)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var viewport = SheetGrid.Viewport;
        if (viewport == null) return;

        var text = ClipboardSerializer.Serialize(viewport, range);
        try { System.Windows.Clipboard.SetText(text); }
        catch { /* clipboard may be locked */ }

        // Show marching ants around the copied range
        SheetGrid.ClipboardRange = range;
        SheetGrid.ClipboardIsCut = isCut;

        // Capture raw cells (including formulas) for paste formula adjustment
        var sheet = _workbook.GetSheet(_currentSheetId);
        var clipCells = new List<(CellAddress, Cell)>();
        for (uint r = range.Start.Row; r <= range.End.Row; r++)
        {
            for (uint c = range.Start.Col; c <= range.End.Col; c++)
            {
                var addr = new CellAddress(_currentSheetId, r, c);
                var cell = sheet?.GetCell(r, c);
                clipCells.Add((addr, cell?.Clone() ?? Cell.FromValue(BlankValue.Instance)));
            }
        }
        _internalClipboard = new InternalClipboard(range, clipCells, isCut);
    }

    private void ExecutePaste(PasteMode mode = PasteMode.All, PasteSpecialOptions options = default, bool keepColumnWidths = false)
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        // If we have an internal clipboard (copied from within this app), use it with formula adjustment
        if (_internalClipboard is { } clip)
        {
            IWorkbookCommand CreatePasteCommand()
            {
                var currentRange = SheetGrid.SelectedRange ?? range;
                var pasteCommand = PasteCommandFactory.CreateInternalPasteCommand(
                    _workbook,
                    _currentSheetId,
                    clip.SourceRange,
                    clip.Cells,
                    currentRange.Start,
                    ClipboardPastePlanner.ToCorePasteMode(mode),
                    options);
                var command = keepColumnWidths
                    ? new CompositeWorkbookCommand(
                        "Paste Special",
                        [
                            pasteCommand,
                            new PasteColumnWidthsCommand(_currentSheetId, clip.SourceRange, currentRange.Start.Col)
                        ])
                    : pasteCommand;

                if (ClipboardPastePlanner.ShouldClearCutSourceAfterPaste(
                        clip.IsCut,
                        clip.SourceRange,
                        currentRange,
                        mode,
                        options,
                        keepColumnWidths))
                {
                    command = new CompositeWorkbookCommand(
                        "Cut and Paste",
                        [
                            command,
                            new ClearContentsCommand(clip.SourceRange.Start.Sheet, clip.SourceRange)
                        ]);
                }

                return command;
            }

            var title = mode == PasteMode.All && !options.Transpose && options.Operation == PasteSpecialOperation.None
                ? "Paste"
                : "Paste Special";

            var pasteOutcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreatePasteCommand);
            if (!pasteOutcome.Success)
            {
                ShowCommandError(pasteOutcome, title);
                return;
            }

            _repeatPostAction = _ =>
            {
                CompletePasteSelection(clip.SourceRange, options);
                if (clip.IsCut)
                    _internalClipboard = null;
            };
            if (mode != PasteMode.Formats)
                RecalculateIfAutomatic(pasteOutcome.AffectedCells ?? []);

            CompletePasteSelection(clip.SourceRange, options);
            if (clip.IsCut)
                _internalClipboard = null;
            UpdateViewport();
            RefreshToolbar();
            return;
        }

        if (mode == PasteMode.Formats || mode == PasteMode.Formulas)
            return;

        if (mode == PasteMode.All && TryPasteClipboardImage(range.Start))
            return;

        // Fallback: external clipboard (plain text)
        string text;
        try { text = System.Windows.Clipboard.GetText(); }
        catch { return; }
        if (string.IsNullOrEmpty(text)) return;

        var rows = ClipboardSerializer.Deserialize(text);
        if (rows.Length == 0 || rows.All(r => r.Length == 0)) return;
        var capturedRows = rows.Select(row => (IReadOnlyList<string>)row).ToList();

        IWorkbookCommand CreateExternalPasteCommand()
        {
            var currentRange = SheetGrid.SelectedRange ?? range;
            return PasteCommandFactory.CreateExternalTextPasteCommand(
                _currentSheetId,
                currentRange.Start,
                capturedRows);
        }

        var fallbackOutcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateExternalPasteCommand);
        if (!fallbackOutcome.Success)
        {
            ShowCommandError(fallbackOutcome, "Paste");
            return;
        }

        _repeatPostAction = _ => CompleteExternalPasteSelection(capturedRows);
        RecalculateIfAutomatic(fallbackOutcome.AffectedCells ?? []);

        CompleteExternalPasteSelection(capturedRows);
        UpdateViewport();
        RefreshToolbar();
    }

    private void UpdateMaximizedContentInset()
    {
        if (RootGrid is null)
            return;

        RootGrid.Margin = WindowState == WindowState.Maximized
            ? GetMaximizedSafeInset()
            : new Thickness(0);
    }

    private static Thickness GetMaximizedSafeInset()
    {
        var resize = SystemParameters.WindowResizeBorderThickness;
        var inset = Math.Ceiling(Math.Max(
            MaximizedSafeInsetDip,
            Math.Max(
                Math.Max(resize.Left, resize.Right),
                Math.Max(resize.Top, resize.Bottom))));

        return new Thickness(inset);
    }

    private void CompletePasteSelection(GridRange sourceRange, PasteSpecialOptions options)
    {
        if (SheetGrid.SelectedRange is not { } range)
            return;

        var pastedRows = options.Transpose ? sourceRange.ColCount : sourceRange.RowCount;
        var pastedCols = options.Transpose ? sourceRange.RowCount : sourceRange.ColCount;
        var pastedEnd = new CellAddress(
            _currentSheetId,
            range.Start.Row + (uint)pastedRows - 1,
            range.Start.Col + (uint)pastedCols - 1);

        _selectionAnchor = range.Start;
        _selectionCursor = pastedEnd;
        SheetGrid.SelectedRanges = null;
        SheetGrid.SelectedRange = new GridRange(range.Start, pastedEnd);
        ClearClipboardVisualState();
    }

    private void CompleteExternalPasteSelection(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (SheetGrid.SelectedRange is not { } range || rows.Count == 0)
            return;

        var pastedColCount = rows.Count == 0 ? 0 : rows.Max(row => row.Count);
        if (pastedColCount == 0)
            return;

        var pastedEnd = new CellAddress(
            _currentSheetId,
            range.Start.Row + (uint)rows.Count - 1,
            range.Start.Col + (uint)pastedColCount - 1);

        _selectionAnchor = range.Start;
        _selectionCursor = pastedEnd;
        SheetGrid.SelectedRanges = null;
        SheetGrid.SelectedRange = new GridRange(range.Start, pastedEnd);
        ClearClipboardVisualState();
    }

    private bool TryPasteClipboardImage(CellAddress anchor)
    {
        try
        {
            if (!System.Windows.Clipboard.ContainsImage())
                return false;

            var image = System.Windows.Clipboard.GetImage();
            if (image is null)
                return false;

            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
            using var stream = new System.IO.MemoryStream();
            encoder.Save(stream);
            var imageBytes = stream.ToArray();
            var pixelWidth = image.PixelWidth;
            var pixelHeight = image.PixelHeight;

            if (!TryExecuteRepeatableGroupedSheetCommand(
                    "Paste Picture",
                    sheetId =>
                    {
                        var currentAnchor = SheetGrid.SelectedRange?.Start ?? anchor;
                        return ClipboardPictureService.CreateInsertCommand(
                            sheetId,
                            new CellAddress(sheetId, currentAnchor.Row, currentAnchor.Col),
                            imageBytes,
                            pixelWidth,
                            pixelHeight);
                    }))
                return true;

            ClearClipboardVisualState();
            UpdateViewport();
            RefreshToolbar();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void ExecuteClearSelection()
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Clear Contents",
                sheetId => new ClearContentsCommand(sheetId, RemapRangeToSheet(SheetGrid.SelectedRange ?? range, sheetId)),
                out var outcome))
            return;

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        UpdateViewport();
    }

    // ── Print / Export ────────────────────────────────────────────────────────

    private void PrintButton_Click(object sender, RoutedEventArgs e)
    {
        var doc = PrintRenderer.RenderWorksheet(_workbook, _currentSheetId, _viewportService);
        var viewer = new System.Windows.Controls.DocumentViewer { Document = doc };
        var previewWin = new Window
        {
            Title = $"Print Preview — {_workbook.Name}",
            Width = 900, Height = 700,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Content = viewer
        };
        previewWin.ShowDialog();
    }

    private void ExportPdfButton_Click(object sender, RoutedEventArgs e)
    {
        var saveDlg = new Microsoft.Win32.SaveFileDialog
        {
            Title      = "Export as PDF / XPS",
            Filter     = "PDF files (*.pdf)|*.pdf|XPS files (*.xps)|*.xps",
            DefaultExt = ".pdf",
            FileName   = _workbook.Name
        };
        if (saveDlg.ShowDialog() != true) return;

        var ext = System.IO.Path.GetExtension(saveDlg.FileName).ToLowerInvariant();
        if (ext == ".pdf")
            ExportViaPrintToPdf(saveDlg.FileName);
        else
            ExportAsXps(saveDlg.FileName);
    }

    /// <summary>
    /// Tries to export directly to PDF by routing through the "Microsoft Print to PDF"
    /// virtual printer. If the printer is unavailable, falls back to XPS and informs the user.
    /// </summary>
    private void ExportViaPrintToPdf(string pdfPath)
    {
        // Look for a PDF-capable print queue (case-insensitive)
        System.Printing.PrintQueue? pdfQueue = null;
        try
        {
            using var server = new System.Printing.LocalPrintServer();
            pdfQueue = server.GetPrintQueues()
                .FirstOrDefault(q => q.Name.Contains("PDF", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            // Print-spooler unavailable — fall through to XPS fallback
        }

        if (pdfQueue != null)
        {
            // The WPF PrintDialog API can target a specific queue but cannot programmatically
            // set the output file path for the Microsoft Print to PDF virtual printer through
            // the managed API alone. We fall back to XPS (which Windows can open/convert to PDF).
            var xpsPath = System.IO.Path.ChangeExtension(pdfPath, ".xps");
            ExportAsXps(xpsPath);
            MessageBox.Show(
                $"Saved as XPS: {xpsPath}\n\n" +
                "Open the file in XPS Viewer and print to any PDF printer, " +
                "or use File → Print and select 'Microsoft Print to PDF' to save directly as PDF.",
                "Export PDF",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        else
        {
            // No PDF printer found; just save XPS
            var xpsPath = System.IO.Path.ChangeExtension(pdfPath, ".xps");
            ExportAsXps(xpsPath);
            MessageBox.Show(
                $"No PDF printer found on this system.\n\nSaved as XPS: {xpsPath}",
                "Export PDF",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    /// <summary>
    /// Writes the current sheet as an XPS package to <paramref name="xpsPath"/>.
    /// Uses the internal <c>XpsDocumentWriter(XpsDocument)</c> constructor (available in
    /// ReachFramework on .NET 10 / .NET Framework) to write directly to a file without
    /// showing a print dialog.
    /// </summary>
    // ── Format Painter ───────────────────────────────────────────────────────

    private void FormatPainterBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        _formatPainterStyleId = sheet?.GetCell(range.Start)?.StyleId
            ?? sheet?.GetStyleOnly(range.Start.Row, range.Start.Col)
            ?? StyleId.Default;
        _formatPainterActive = true;
    }

    // Call from cell-click path: if painter active, apply stored style
    private bool TryApplyFormatPainter(CellAddress addr)
    {
        if (!_formatPainterActive) return false;
        _formatPainterActive = false;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return true;

        var targetRange = new GridRange(addr, addr);
        var command = FormatPainterCommandFactory.Create(_workbook, _formatPainterStyleId, targetRange);
        if (!TryExecuteCommand(command, "Format Painter"))
            return true;

        UpdateViewport();
        return true;
    }

    // ── Paste Special ────────────────────────────────────────────────────────

    private void PasteSpecialBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_internalClipboard is null)
        {
            string text;
            try { text = System.Windows.Clipboard.GetText(); }
            catch { return; }
            if (string.IsNullOrEmpty(text)) return;
        }

        var dlg = new PasteSpecialDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var options = new PasteSpecialOptions(
            Transpose: dlg.Transpose,
            Operation: dlg.Operation.ToLowerInvariant() switch
            {
                "add" => PasteSpecialOperation.Add,
                "subtract" => PasteSpecialOperation.Subtract,
                "multiply" => PasteSpecialOperation.Multiply,
                "divide" => PasteSpecialOperation.Divide,
                _ => PasteSpecialOperation.None
            });
        var keepColumnWidths = dlg.KeepColumnWidths;
        if (dlg.PastePicture)
        {
            ExecutePasteAsPicture();
            return;
        }

        if (dlg.PasteLink)
        {
            ExecutePasteLink(options.Transpose, keepColumnWidths);
            return;
        }

        if (dlg.PasteValues)
            ExecutePaste(PasteMode.Values, options, keepColumnWidths);
        else if (dlg.PasteFormulas)
            ExecutePaste(PasteMode.Formulas, options, keepColumnWidths);
        else if (dlg.PasteFormats)
            ExecutePaste(PasteMode.Formats, options, keepColumnWidths);
        else
            ExecutePaste(PasteMode.All, options, keepColumnWidths);
    }

    private void ExecutePasteAsPicture()
    {
        if (_internalClipboard is not { } clip || SheetGrid.SelectedRange is not { } range)
            return;

        var sourceCells = clip.Cells
            .Select(c => (c.Item1, FormatPictureCellText(c.Item2.Value)))
            .ToList();
        IWorkbookCommand CreatePastePictureCommand()
        {
            var currentRange = SheetGrid.SelectedRange ?? range;
            return new PasteRangeAsPictureCommand(_currentSheetId, clip.SourceRange, sourceCells, currentRange.Start);
        }

        var outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreatePastePictureCommand);
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Paste Picture");
            return;
        }

        _repeatPostAction = _ => ClearClipboardVisualState();
        ClearClipboardVisualState();
        UpdateViewport();
        RefreshToolbar();
    }

    private static string FormatPictureCellText(ScalarValue value) =>
        value switch
        {
            BlankValue => "",
            NumberValue n => n.Value.ToString(System.Globalization.CultureInfo.CurrentCulture),
            BoolValue b => b.Value ? "TRUE" : "FALSE",
            TextValue t => t.Value,
            ErrorValue e => e.Code,
            _ => value.ToString() ?? ""
        };

    private void ExecutePasteLink(bool transpose, bool keepColumnWidths = false)
    {
        if (_internalClipboard is not { } clip || SheetGrid.SelectedRange is not { } range)
            return;

        var sourceSheet = _workbook.GetSheet(clip.SourceRange.Start.Sheet);
        if (sourceSheet is null)
            return;

        IWorkbookCommand CreatePasteLinkCommand()
        {
            var currentRange = SheetGrid.SelectedRange ?? range;
            var linkedCells = PasteLinkService.CreateLinkedCells(
                clip.SourceRange,
                currentRange.Start,
                sourceSheet.Name,
                transpose);
            var targetSheetIds = CurrentGroupedEditSheetIds();
            IWorkbookCommand linkCommand = targetSheetIds.Count > 1
                ? new GroupedEditCellsCommand(targetSheetIds, _currentSheetId, linkedCells)
                : new EditCellsCommand(_currentSheetId, linkedCells);
            return keepColumnWidths
                ? new CompositeWorkbookCommand(
                    "Paste Link",
                    [
                        linkCommand,
                        new PasteColumnWidthsCommand(_currentSheetId, clip.SourceRange, currentRange.Start.Col)
                    ])
                : linkCommand;
        }

        var outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreatePasteLinkCommand);
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Paste Link");
            return;
        }

        _repeatPostAction = _ => CompletePasteSelection(clip.SourceRange, new PasteSpecialOptions(Transpose: transpose));
        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        CompletePasteSelection(clip.SourceRange, new PasteSpecialOptions(Transpose: transpose));
        UpdateViewport();
        RefreshToolbar();
    }

    private void InsertCurrentDateOrTime(bool insertTime)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var value = insertTime
            ? DateTimeEntryService.CurrentTime(DateTime.Now)
            : DateTimeEntryService.CurrentDate(DateTime.Now);
        if (!TryExecuteRepeatableCurrentRangeCommand(
                insertTime ? "Insert Time" : "Insert Date",
                range,
                currentRange => CreateSingleCellEditCommand(currentRange.Start, Cell.FromValue(value)),
                out var outcome))
            return;

        RecalculateIfAutomatic(outcome.AffectedCells ?? [range.Start]);
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
    }

    // ── Font group additions ─────────────────────────────────────────────────

    private void DoubleUnderlineBtn_Click(object sender, RoutedEventArgs e)
    {
        var isOn = (sender as System.Windows.Controls.Primitives.ToggleButton)?.IsChecked == true;
        if (isOn)
            SetToolbarToggleStates(underline: false, strike: false);
        ApplyStyleDiff(new StyleDiff(DoubleUnderline: isOn, Underline: isOn ? false : null, Strikethrough: isOn ? false : null));
    }

    private void IncreaseFontSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var style = _workbook.GetStyle(sheet?.GetCell(SheetGrid.SelectedRange?.Start ?? default)?.StyleId ?? StyleId.Default);
        double newSize = style.FontSize switch { < 10 => style.FontSize + 1, < 24 => style.FontSize + 2, _ => style.FontSize + 4 };
        ApplyFontSizeAndFitRows(newSize);
    }

    private void DecreaseFontSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var style = _workbook.GetStyle(sheet?.GetCell(SheetGrid.SelectedRange?.Start ?? default)?.StyleId ?? StyleId.Default);
        double newSize = style.FontSize switch { <= 10 => Math.Max(1, style.FontSize - 1), <= 26 => style.FontSize - 2, _ => style.FontSize - 4 };
        ApplyFontSizeAndFitRows(newSize);
    }

    private void ApplyFontSizeAndFitRows(double fontSize)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        ApplyStyleDiff(new StyleDiff(FontSize: fontSize));

        var height = Math.Max(18.0, Math.Ceiling(fontSize * 96.0 / 72.0 + 5.0));
        if (!TryExecuteGroupedSheetCommand("Auto Fit Row Height", sheetId =>
                new SetRowHeightCommand(sheetId, range.Start.Row, range.End.Row, height)))
            return;

        UpdateViewport();
    }

    // ── Border picker ────────────────────────────────────────────────────────

    private void BorderPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }

    private void BorderAllMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var b = new CellBorder(BorderStyle.Thin, CellColor.Black);
        ApplyStyleDiff(new StyleDiff(BorderTop: b, BorderRight: b, BorderBottom: b, BorderLeft: b));
    }
    private void BorderOutsideMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplyOutlineBorderShortcut();
    }
    private void BorderNoneMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ApplyStyleDiff(BorderShortcutService.GetClearBorderDiff());
    }
    private void BorderBottomMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyStyleDiff(new StyleDiff(BorderBottom: new CellBorder(BorderStyle.Thin, CellColor.Black)));
    private void BorderTopMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyStyleDiff(new StyleDiff(BorderTop: new CellBorder(BorderStyle.Thin, CellColor.Black)));
    private void BorderLeftMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyStyleDiff(new StyleDiff(BorderLeft: new CellBorder(BorderStyle.Thin, CellColor.Black)));
    private void BorderRightMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyStyleDiff(new StyleDiff(BorderRight: new CellBorder(BorderStyle.Thin, CellColor.Black)));
    private void BorderThickBottomMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyStyleDiff(new StyleDiff(BorderBottom: new CellBorder(BorderStyle.Thick, CellColor.Black)));

    // ── Alignment group additions ────────────────────────────────────────────

    private void AlignTopBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        SetToolbarToggleStates(top: true, middle: false, bottom: false);
        ApplyStyleDiff(new StyleDiff(VAlign: CellVAlign.Top));
    }

    private void AlignMiddleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        SetToolbarToggleStates(top: false, middle: true, bottom: false);
        ApplyStyleDiff(new StyleDiff(VAlign: CellVAlign.Center));
    }

    private void AlignBottomBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressToolbarSync) return;
        SetToolbarToggleStates(top: false, middle: false, bottom: true);
        ApplyStyleDiff(new StyleDiff(VAlign: CellVAlign.Bottom));
    }

    private void IndentIncBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var style = _workbook.GetStyle(sheet?.GetCell(SheetGrid.SelectedRange?.Start ?? default)?.StyleId ?? StyleId.Default);
        ApplyStyleDiff(new StyleDiff(IndentLevel: Math.Min(15, style.IndentLevel + 1)));
    }
    private void IndentDecBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var style = _workbook.GetStyle(sheet?.GetCell(SheetGrid.SelectedRange?.Start ?? default)?.StyleId ?? StyleId.Default);
        ApplyStyleDiff(new StyleDiff(IndentLevel: Math.Max(0, style.IndentLevel - 1)));
    }

    private void OrientationPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void OrientHorizMenuItem_Click(object sender, RoutedEventArgs e)    => ApplyStyleDiff(new StyleDiff(TextRotation: 0));
    private void OrientAngleCCWMenuItem_Click(object sender, RoutedEventArgs e) => ApplyStyleDiff(new StyleDiff(TextRotation: 45));
    private void OrientAngleCWMenuItem_Click(object sender, RoutedEventArgs e)  => ApplyStyleDiff(new StyleDiff(TextRotation: -45));
    private void OrientVertMenuItem_Click(object sender, RoutedEventArgs e)     => ApplyStyleDiff(new StyleDiff(TextRotation: 90));
    private void OrientRotateUpMenuItem_Click(object sender, RoutedEventArgs e)  => ApplyStyleDiff(new StyleDiff(TextRotation: 90));
    private void OrientRotateDownMenuItem_Click(object sender, RoutedEventArgs e) => ApplyStyleDiff(new StyleDiff(TextRotation: -90));

    // ── Number group additions ───────────────────────────────────────────────

    private void CurrencyBtn_Click(object sender, RoutedEventArgs e)    => ApplyStyleDiff(new StyleDiff(NumberFormat: "$#,##0.00"));
    private void PercentBtn_Click(object sender, RoutedEventArgs e)     => ApplyStyleDiff(new StyleDiff(NumberFormat: "0%"));
    private void CommaStyleBtn_Click(object sender, RoutedEventArgs e)  => ApplyStyleDiff(new StyleDiff(NumberFormat: "#,##0.00"));

    private void IncDecimalBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var style = _workbook.GetStyle(sheet?.GetCell(SheetGrid.SelectedRange?.Start ?? default)?.StyleId ?? StyleId.Default);
        ApplyStyleDiff(new StyleDiff(NumberFormat: NumberFormatDecimalAdjuster.AddDecimalPlace(style.NumberFormat)));
    }
    private void DecDecimalBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var style = _workbook.GetStyle(sheet?.GetCell(SheetGrid.SelectedRange?.Start ?? default)?.StyleId ?? StyleId.Default);
        ApplyStyleDiff(new StyleDiff(NumberFormat: NumberFormatDecimalAdjuster.RemoveDecimalPlace(style.NumberFormat)));
    }

    // ── Styles group ─────────────────────────────────────────────────────────

    private void CfPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void CfGtMenuItem_Click(object sender, RoutedEventArgs e)       => ShowCfDialog("Greater Than");
    private void CfLtMenuItem_Click(object sender, RoutedEventArgs e)       => ShowCfDialog("Less Than");
    private void CfBetweenMenuItem_Click(object sender, RoutedEventArgs e)  => ShowCfDialog("Between");
    private void CfEqMenuItem_Click(object sender, RoutedEventArgs e)       => ShowCfDialog("Equal To");
    private void CfTextMenuItem_Click(object sender, RoutedEventArgs e)     => ShowCfDialog("Text Contains");
    private void CfDateMenuItem_Click(object sender, RoutedEventArgs e)     => ShowCfDialog("Date Occurring");
    private void CfDuplicateMenuItem_Click(object sender, RoutedEventArgs e) => ShowCfDialog("Duplicate Values");
    private void CfTop10MenuItem_Click(object sender, RoutedEventArgs e)    => ShowCfDialog("Top 10 Items");
    private void CfBottom10MenuItem_Click(object sender, RoutedEventArgs e) => ShowCfDialog("Bottom 10 Items");
    private void CfAboveAvgMenuItem_Click(object sender, RoutedEventArgs e) => ShowCfDialog("Above Average");
    private void CfDataBarMenuItem_Click(object sender, RoutedEventArgs e)  => ShowCfDialog("Data Bar");
    private void CfColorScaleMenuItem_Click(object sender, RoutedEventArgs e) => ShowCfDialog("Color Scale");
    private void CfIconSetMenuItem_Click(object sender, RoutedEventArgs e)  => ShowCfDialog("Icon Set");
    private void CfNewRuleMenuItem_Click(object sender, RoutedEventArgs e)  => ShowCfDialog("New Rule");
    private void CfNewFormulaRuleMenuItem_Click(object sender, RoutedEventArgs e) => ShowCfDialog("Formula");
    private void CfClearRulesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteGroupedSheetCommand(
                "Clear Conditional Formatting",
                sheetId => new ClearConditionalFormatsCommand(sheetId, RemapRangeToSheet(range, sheetId))))
            return;
        UpdateViewport();
    }
    private void CfManageRulesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;
        var dlg = new ManageConditionalFormatsDialog(sheet, SheetGrid.SelectedRange) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.ResultRules is null) return;
        var newRules = dlg.ResultRules;
        if (!TryExecuteGroupedSheetCommand(
                "Manage Conditional Formatting Rules",
                sheetId =>
                {
                    var remapped = newRules
                        .Select(r => CloneConditionalFormatForSheet(r, sheetId))
                        .ToList();
                    return new ReplaceAllConditionalFormatsCommand(sheetId, remapped);
                }))
            return;
        UpdateViewport();
    }

    private void ShowCfDialog(string ruleType)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var dlg = new ConditionalFormatDialog(ruleType, range) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.ResultRule is null) return;
        if (!TryExecuteGroupedSheetCommand(
                "Conditional Formatting",
                sheetId => new ApplyConditionalFormatCommand(sheetId, CloneConditionalFormatForSheet(dlg.ResultRule, sheetId))))
            return;
        UpdateViewport();
    }

    private void FormatTableBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void FormatTableLightMenuItem_Click(object sender, RoutedEventArgs e)  => ApplyTableFormat(0);
    private void FormatTableMediumMenuItem_Click(object sender, RoutedEventArgs e) => ApplyTableFormat(1);
    private void FormatTableDarkMenuItem_Click(object sender, RoutedEventArgs e)   => ApplyTableFormat(2);

    private void ApplyTableFormat(int variant)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var (headerFill, oddFill, evenFill) = variant switch
        {
            1 => (new CellColor(31, 78, 121), new CellColor(222, 235, 247), new CellColor(255, 255, 255)),
            2 => (new CellColor(54, 54, 54),  new CellColor(68, 68, 68),    new CellColor(80, 80, 80)),
            _ => (new CellColor(31, 115, 70), new CellColor(226, 239, 218), new CellColor(255, 255, 255))
        };
        for (uint r = range.Start.Row; r <= range.End.Row; r++)
        {
            var fill = r == range.Start.Row ? headerFill : (r % 2 == 0 ? evenFill : oddFill);
            var fontColor = r == range.Start.Row ? CellColor.White : CellColor.Black;
            var bold = r == range.Start.Row;
            if (!TryExecuteApplyStyle(
                    new GridRange(
                        new CellAddress(_currentSheetId, r, range.Start.Col),
                        new CellAddress(_currentSheetId, r, range.End.Col)),
                    new StyleDiff(FillColor: fill, FontColor: fontColor, Bold: bold),
                    "Format as Table"))
                return;
        }
        UpdateViewport();
    }

    private void CellStylesBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void CellStyleGoodMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyStyleDiff(new StyleDiff(FillColor: new CellColor(198, 239, 206), FontColor: new CellColor(0, 97, 0)));
    private void CellStyleBadMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyStyleDiff(new StyleDiff(FillColor: new CellColor(255, 199, 206), FontColor: new CellColor(156, 0, 6)));
    private void CellStyleNeutralMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyStyleDiff(new StyleDiff(FillColor: new CellColor(255, 235, 156), FontColor: new CellColor(156, 101, 0)));
    private void CellStyleH1MenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyStyleDiff(new StyleDiff(Bold: true, FontSize: 16, FillColor: new CellColor(31, 115, 70), FontColor: CellColor.White));
    private void CellStyleH2MenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyStyleDiff(new StyleDiff(Bold: true, FontSize: 14));
    private void CellStyleNoteMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyStyleDiff(new StyleDiff(FillColor: new CellColor(255, 255, 204), BorderBottom: new CellBorder(BorderStyle.Thin, CellColor.Black)));
    private void CellStyleWarningMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyStyleDiff(new StyleDiff(FillColor: new CellColor(255, 192, 0), FontColor: CellColor.Black, Bold: true));
    private void CellStyleTotalMenuItem_Click(object sender, RoutedEventArgs e)
        => ApplyStyleDiff(new StyleDiff(Bold: true, BorderTop: new CellBorder(BorderStyle.Thin, CellColor.Black),
            BorderBottom: new CellBorder(BorderStyle.Double, CellColor.Black)));

    // ── Cells group (pickers) ────────────────────────────────────────────────

    private void InsertPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void DeletePickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void FormatPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }

    private void InsertCellsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var input = PromptForInput("Shift cells (right/down):", "right");
        if (input is null) return;

        var direction = input.Trim().Equals("down", StringComparison.OrdinalIgnoreCase)
            ? InsertCellsShiftDirection.Down
            : InsertCellsShiftDirection.Right;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Insert Cells",
                range,
                currentRange => new InsertCellsCommand(_currentSheetId, currentRange, direction),
                out var outcome))
            return;

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        UpdateViewport();
    }

    private void InsertSheetMenuItem_Click(object sender, RoutedEventArgs e)   { AddSheetButton_Click(sender, e); }
    private void DeleteCellsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var input = PromptForInput("Shift cells (left/up):", "left");
        if (input is null) return;

        var direction = input.Trim().Equals("up", StringComparison.OrdinalIgnoreCase)
            ? DeleteCellsShiftDirection.Up
            : DeleteCellsShiftDirection.Left;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Delete Cells",
                range,
                currentRange => new DeleteCellsCommand(_currentSheetId, currentRange, direction),
                out var outcome))
            return;

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        UpdateViewport();
    }

    private void DeleteSheetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null || _workbook.Sheets.Count <= 1) { MessageBox.Show("Cannot delete the only sheet."); return; }
        if (MessageBox.Show($"Delete '{sheet.Name}'?", "Delete Sheet", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        var outcome = _commandBus.Execute(_workbook.Id, new RemoveSheetCommand(_currentSheetId));
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Delete Sheet");
            return;
        }

        _currentSheetId = _workbook.Sheets[0].Id;
        RecalculateWorkbook();
        RefreshSheetTabs();
        UpdateViewport();
    }

    private void FormatRowHeightMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var input = PromptForInput("Row height (pixels):", "20");
        if (input is null || !double.TryParse(input, out var h) || h <= 0) return;
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteGroupedSheetCommand("Row Height", sheetId => new SetRowHeightCommand(sheetId, range.Start.Row, range.End.Row, h)))
            return;
        UpdateViewport();
    }
    private void FormatAutoRowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteGroupedSheetCommand("Auto Row Height", sheetId => new SetRowHeightCommand(sheetId, range.Start.Row, range.End.Row, height: null)))
            return;
        UpdateViewport();
    }
    private void FormatColWidthMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var input = PromptForInput("Column width (character units):", "8");
        if (input is null || !double.TryParse(input, out var w) || w <= 0) return;
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteGroupedSheetCommand("Column Width", sheetId => new SetColumnWidthCommand(sheetId, range.Start.Col, range.End.Col, w)))
            return;
        UpdateViewport();
    }
    private void FormatAutoColMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteGroupedSheetCommand("Auto Column Width", sheetId => new SetColumnWidthCommand(sheetId, range.Start.Col, range.End.Col, width: null)))
            return;
        UpdateViewport();
    }
    private void FormatDefaultWidthMenuItem_Click(object sender, RoutedEventArgs e) { FormatColWidthMenuItem_Click(sender, e); }
    private void FormatHideRowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ExecuteRowsHidden(hidden: true);
    }

    private void FormatUnhideRowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ExecuteRowsHidden(hidden: false);
    }

    private void FormatHideColMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ExecuteColumnsHidden(hidden: true);
    }

    private void FormatUnhideColMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ExecuteColumnsHidden(hidden: false);
    }
    private void FormatProtectSheetMenuItem_Click(object sender, RoutedEventArgs e) { ProtectSheetBtn_Click(sender, e); }
    private void FormatLockCellMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        var style = _workbook.GetStyle(sheet.GetCell(range.Start)?.StyleId ?? StyleId.Default);
        ApplyStyleDiff(new StyleDiff(Locked: !style.Locked));
    }

    // ── Editing group (pickers) ──────────────────────────────────────────────

    private void AutoSumPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void FormulasAutoSumPickerBtn_Click(object sender, RoutedEventArgs e) { AutoSumPickerBtn_Click(sender, e); }

    private void InsertAutoSumFormula(string func)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "AutoSum",
                range,
                currentRange =>
                {
                    var addr = currentRange.Start;
                    var formula = BuildAutoSumFormula(func, addr);
                    var edits = new List<(CellAddress Address, Cell NewCell)> { (addr, Cell.FromFormula(formula)) };
                    var targetSheetIds = CurrentGroupedEditSheetIds();
                    return targetSheetIds.Count > 1
                        ? new GroupedEditCellsCommand(targetSheetIds, _currentSheetId, edits)
                        : new EditCellsCommand(_currentSheetId, edits);
                },
                out var outcome))
            return;

        RecalculateIfAutomatic(outcome.AffectedCells ?? [range.Start]);
        SetActiveCell(new CellAddress(_currentSheetId, range.Start.Row + 1, range.Start.Col));
        UpdateViewport();
    }

    private string BuildAutoSumFormula(string func, CellAddress addr)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return $"{func}({CellAddress.NumberToColumnName(addr.Col)}{Math.Max(1, addr.Row - 1)}:{CellAddress.NumberToColumnName(addr.Col)}{addr.Row})";

        uint topRow = addr.Row;
        while (topRow > 1 && sheet.GetValue(topRow - 1, addr.Col) is NumberValue) topRow--;
        if (topRow == addr.Row)
        {
            uint leftCol = addr.Col;
            while (leftCol > 1 && sheet.GetValue(addr.Row, leftCol - 1) is NumberValue) leftCol--;
            if (leftCol < addr.Col)
            {
                var leftRangeRef = $"{CellAddress.NumberToColumnName(leftCol)}{addr.Row}:{CellAddress.NumberToColumnName(addr.Col - 1)}{addr.Row}";
                return $"{func}({leftRangeRef})";
            }
        }

        var rangeStr = topRow < addr.Row
            ? $"{CellAddress.NumberToColumnName(addr.Col)}{topRow}:{CellAddress.NumberToColumnName(addr.Col)}{addr.Row - 1}"
            : $"{CellAddress.NumberToColumnName(addr.Col)}{Math.Max(1, addr.Row - 1)}:{CellAddress.NumberToColumnName(addr.Col)}{addr.Row}";
        return $"{func}({rangeStr})";
    }

    private void AutoSumSumMenuItem_Click(object sender, RoutedEventArgs e)   => InsertAutoSumFormula("SUM");
    private void AutoSumAvgMenuItem_Click(object sender, RoutedEventArgs e)   => InsertAutoSumFormula("AVERAGE");
    private void AutoSumCountMenuItem_Click(object sender, RoutedEventArgs e) => InsertAutoSumFormula("COUNT");
    private void AutoSumMaxMenuItem_Click(object sender, RoutedEventArgs e)   => InsertAutoSumFormula("MAX");
    private void AutoSumMinMenuItem_Click(object sender, RoutedEventArgs e)   => InsertAutoSumFormula("MIN");
    private void AutoSumMoreMenuItem_Click(object sender, RoutedEventArgs e)  => InsertFunctionBtn_Click(sender, e);

    private void FillPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void FillDownMenuItem_Click(object sender, RoutedEventArgs e)
        => ExecuteFillCells(FillCellsDirection.Down);

    private void FillRightMenuItem_Click(object sender, RoutedEventArgs e)
        => ExecuteFillCells(FillCellsDirection.Right);

    private void FillUpMenuItem_Click(object sender, RoutedEventArgs e)
        => ExecuteFillCells(FillCellsDirection.Up);

    private void FillLeftMenuItem_Click(object sender, RoutedEventArgs e)
        => ExecuteFillCells(FillCellsDirection.Left);

    private void ExecuteFillCells(FillCellsDirection direction)
    {
        if (SheetGrid.SelectedRange is not { } range || !CanFill(range, direction))
            return;

        var title = direction switch
        {
            FillCellsDirection.Down => "Fill Down",
            FillCellsDirection.Right => "Fill Right",
            FillCellsDirection.Up => "Fill Up",
            FillCellsDirection.Left => "Fill Left",
            _ => "Fill"
        };

        if (!TryExecuteRepeatableGroupedSheetCommand(
                title,
                sheetId => new FillCellsCommand(sheetId, RemapRangeToSheet(SheetGrid.SelectedRange ?? range, sheetId), direction),
                out var outcome))
            return;

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        UpdateViewport();
    }

    private static bool CanFill(GridRange range, FillCellsDirection direction) =>
        direction is FillCellsDirection.Down or FillCellsDirection.Up
            ? range.RowCount >= 2
            : range.ColCount >= 2;

    private void FillSeriesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        // Basic: fill a linear series starting from selected cell
        if (SheetGrid.SelectedRange is not { } range) return;
        var sheet = _workbook.GetSheet(_currentSheetId); if (sheet is null) return;
        var startVal = sheet.GetValue(range.Start.Row, range.Start.Col) as NumberValue;
        if (startVal is null) { MessageBox.Show("Select a cell with a numeric value to start a series."); return; }
        var stepInput = PromptForInput("Step value:", "1");
        if (stepInput is null || !double.TryParse(stepInput, out var step)) return;

        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Fill Series",
                range,
                currentRange =>
                {
                    var edits = BuildFillSeriesEdits(currentRange, step);
                    var targetSheetIds = CurrentGroupedEditSheetIds();
                    return targetSheetIds.Count > 1
                        ? new GroupedEditCellsCommand(targetSheetIds, _currentSheetId, edits)
                        : new EditCellsCommand(_currentSheetId, edits);
                },
                out var outcome))
            return;

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        UpdateViewport();
    }

    private List<(CellAddress Address, Cell NewCell)> BuildFillSeriesEdits(GridRange range, double step)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var startValue = sheet?.GetValue(range.Start.Row, range.Start.Col) as NumberValue;
        if (startValue is null)
            return [];

        var edits = new List<(CellAddress, Cell)>();
        var value = startValue.Value;
        for (uint r = range.Start.Row; r <= range.End.Row; r++)
        {
            for (uint c = range.Start.Col; c <= range.End.Col; c++)
            {
                if (r == range.Start.Row && c == range.Start.Col)
                {
                    value += step;
                    continue;
                }

                edits.Add((new CellAddress(_currentSheetId, r, c), Cell.FromValue(new NumberValue(value))));
                value += step;
            }
        }

        return edits;
    }

    private void FlashFillMenuItem_Click(object sender, RoutedEventArgs e) => TryFlashFill();

    private void TryFlashFill()
    {
        var range = SheetGrid.SelectedRange;
        if (range is null) return;

        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Flash Fill",
                range.Value,
                currentRange => CreateFlashFillCommand(sheet, currentRange),
                out var outcome))
            return;

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        UpdateViewport();
    }

    private FlashFillCommand CreateFlashFillCommand(Sheet sheet, GridRange range)
    {
        uint fillCol = range.Start.Col;
        uint sourceCol = fillCol > 1 ? fillCol - 1 : fillCol + 1;
        uint startRow = range.Start.Row;
        uint endRow = range.End.Row;

        if (startRow == endRow)
        {
            uint maxRow = startRow;
            for (uint r = startRow + 1; r <= CellAddress.MaxRow; r++)
            {
                var fillVal = sheet.GetValue(r, fillCol);
                var srcVal = sheet.GetValue(r, sourceCol);
                if (fillVal is BlankValue && srcVal is BlankValue)
                    break;
                maxRow = r;
            }
            endRow = maxRow;
        }

        return new FlashFillCommand(_currentSheetId, fillCol, sourceCol, startRow, endRow);
    }

    private void SortFilterPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void SortAZMenuItem_Click(object sender, RoutedEventArgs e)    => SortAscButton_Click(sender, e);
    private void SortZAMenuItem_Click(object sender, RoutedEventArgs e)    => SortDescButton_Click(sender, e);
    private void SortCustomMenuItem_Click(object sender, RoutedEventArgs e) => SortCustomButton_Click(sender, e);
    private void FilterToggleMenuItem_Click(object sender, RoutedEventArgs e) => FilterButton_Click(sender, e);
    private void FilterClearMenuItem_Click(object sender, RoutedEventArgs e)  => ClearFilterButton_Click(sender, e);
    private void FilterReapplyMenuItem_Click(object sender, RoutedEventArgs e) => FilterButton_Click(sender, e);

    private void FindSelectPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void FindFindMenuItem_Click(object sender, RoutedEventArgs e)       => FindButton_Click(sender, e);
    private void FindReplaceMenuItem_Click(object sender, RoutedEventArgs e)    => ReplaceButton_Click(sender, e);
    private void FindGoToMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var input = PromptForInput("Go To cell (e.g. B5):", "A1");
        if (input is null) return;
        try
        {
            var addr = CellAddress.Parse(input.Trim(), _currentSheetId);
            SetActiveCell(addr);
            EnsureCellVisible(addr);
        }
        catch { MessageBox.Show("Invalid cell address."); }
    }
    private void FindGoToSpecialMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;
        var range = SheetGrid.SelectedRange ?? sheet.GetUsedRange() ??
            new GridRange(new CellAddress(_currentSheetId, 1, 1), new CellAddress(_currentSheetId, 1, 1));
        var input = PromptForInput("Go To Special (blanks/constants/formulas/comments/validation/visible):", "blanks");
        if (input is null) return;

        var kind = input.Trim().ToLowerInvariant() switch
        {
            "constant" or "constants" => GoToSpecialKind.Constants,
            "formula" or "formulas" => GoToSpecialKind.Formulas,
            "comment" or "comments" => GoToSpecialKind.Comments,
            "validation" or "data validation" => GoToSpecialKind.DataValidation,
            "visible" or "visible cells" => GoToSpecialKind.VisibleCellsOnly,
            _ => GoToSpecialKind.Blanks
        };

        var matches = GoToSpecialService.Find(sheet, range, kind);
        if (matches.Count == 0)
        {
            MessageBox.Show("No cells found.", "Go To Special", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var compressedRanges = SelectionRangeService.CompressAddresses(matches);
        _selectionAnchor = matches[0];
        _selectionCursor = matches[0];
        SheetGrid.SelectedRange = new GridRange(matches[0], matches[0]);
        SheetGrid.SelectedRanges = compressedRanges;
        CellAddressBox.Text = compressedRanges.Count == 1
            ? FormatRangeReference(compressedRanges[0].Start, compressedRanges[0].End)
            : $"{matches.Count} cells";
        EnsureCellVisible(matches[0]);
        UpdateViewport();
        RefreshStatusBar();
    }

    private void ClearPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void ClearAllMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Clear All",
                sheetId =>
                {
                    var currentRange = RemapRangeToSheet(SheetGrid.SelectedRange ?? range, sheetId);
                    return new CompositeWorkbookCommand(
                        "Clear All",
                        [
                            new ClearContentsCommand(sheetId, currentRange),
                            new ApplyStyleCommand(sheetId, currentRange, ClearFormatsDiff())
                        ]);
                },
                out var outcome))
            return;

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        UpdateViewport();
    }
    private void ClearFormatsMenuItem_Click(object sender, RoutedEventArgs e) => ClearFormats();
    private void ClearValuesMenuItem_Click(object sender, RoutedEventArgs e)  => ClearValues();
    private void ClearCommentsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Clear Comments",
                range,
                currentRange => new ClearCommentsCommand(_currentSheetId, currentRange)))
            return;

        UpdateViewport();
    }

    private void ClearHyperlinksMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Clear Hyperlinks",
                range,
                currentRange => new ClearHyperlinksCommand(_currentSheetId, currentRange)))
            return;
        UpdateViewport();
    }

    private void ClearValues()
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Clear Contents",
                sheetId => new ClearContentsCommand(sheetId, RemapRangeToSheet(SheetGrid.SelectedRange ?? range, sheetId)),
                out var outcome))
            return;

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        UpdateViewport();
    }
    private void ClearFormats()
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        ApplyStyleDiff(ClearFormatsDiff());
    }

    private static StyleDiff ClearFormatsDiff() =>
        new(
            Bold: false, Italic: false, Underline: false, DoubleUnderline: false, Strikethrough: false,
            FontName: "Calibri", FontSize: 11, ClearFill: true, NumberFormat: "General",
            HAlign: CellHAlign.General, VAlign: CellVAlign.Bottom, WrapText: false, IndentLevel: 0,
            BorderTop: new CellBorder(BorderStyle.None),
            BorderBottom: new CellBorder(BorderStyle.None),
            BorderLeft: new CellBorder(BorderStyle.None),
            BorderRight: new CellBorder(BorderStyle.None));

    // ── Insert tab ────────────────────────────────────────────────────────────

    private void PivotTableBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null || SheetGrid.SelectedRange is not { } sourceRange)
        {
            MessageBox.Show(
                "Select a source range with a header row before creating a PivotTable.",
                "Insert PivotTable",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (sourceRange.RowCount < 2 || sourceRange.ColCount < 2)
        {
            MessageBox.Show(
                "PivotTable source data must include at least two columns and a header row.",
                "Insert PivotTable",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dataFieldIndex = PivotUiPlanner.ChooseDefaultDataField(sheet, sourceRange);
        var rowFieldIndex = dataFieldIndex == 0 ? 1 : 0;
        var targetRange = PivotUiPlanner.DefaultTargetRange(sheet, sourceRange);
        var name = PivotUiPlanner.GenerateUniquePivotTableName(sheet);

        if (!TryExecuteCommand(
                new AddPivotTableCommand(
                    _currentSheetId,
                    sourceRange,
                    targetRange,
                    name,
                    rowFieldIndexes: [rowFieldIndex],
                    dataFieldIndexes: [dataFieldIndex]),
                "Insert PivotTable"))
            return;

        UpdateViewport();
    }

    private void RefreshPivotTableBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (pivotTable is null)
        {
            MessageBox.Show(
                "Select a cell inside an existing PivotTable, or open a workbook with a PivotTable on the active sheet.",
                "Refresh PivotTable",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!TryExecuteCommand(new RefreshPivotTableCommand(_currentSheetId, pivotTable.Name), "Refresh PivotTable"))
            return;

        UpdateViewport();
    }

    private void PivotTableShowDetailsBtn_Click(object sender, RoutedEventArgs e)
    {
        _ = TryShowPivotTableDetails(showMessage: true);
    }

    private bool TryShowPivotTableDetails(bool showMessage)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var selected = SheetGrid.SelectedRange?.Start;
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (pivotTable is null || selected is null)
        {
            if (showMessage)
            {
                MessageBox.Show(
                    "Select a value cell inside an existing PivotTable before showing detail rows.",
                    "Show PivotTable Details",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            return false;
        }

        if (!TryExecuteCommand(
                new DrillDownPivotTableCommand(_currentSheetId, pivotTable.Name, selected.Value),
                "Show PivotTable Details"))
            return false;

        var detailSheet = _workbook.Sheets.LastOrDefault();
        if (detailSheet is not null)
            _currentSheetId = detailSheet.Id;
        RefreshSheetTabs();
        UpdateViewport();
        return true;
    }

    private void PivotChartBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (pivotTable is null)
        {
            MessageBox.Show(
                "Select a cell inside an existing PivotTable, or open a workbook with a PivotTable on the active sheet.",
                "Insert PivotChart",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!TryExecuteCommand(
                new AddPivotChartCommand(_currentSheetId, pivotTable.Name, ChartType.Column, $"{pivotTable.Name} Chart"),
                "Insert PivotChart"))
            return;

        UpdateViewport();
    }

    private void PivotChartChangeTypeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActivePivotTable(out var sheet, out var pivotTable))
        {
            MessageBox.Show(
                "Select a cell inside an existing PivotTable before changing a PivotChart type.",
                "Change PivotChart Type",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var chart = sheet.Charts.FirstOrDefault(item =>
            item.IsPivotChart &&
            string.Equals(item.PivotTableName, pivotTable.Name, StringComparison.OrdinalIgnoreCase));
        if (chart is null)
        {
            MessageBox.Show(
                "Insert or select a PivotChart connected to this PivotTable before changing its type.",
                "Change PivotChart Type",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var input = PromptForInput("PivotChart type:", chart.Type.ToString());
        if (string.IsNullOrWhiteSpace(input))
            return;

        if (!TryExecuteCommand(new ChangePivotChartTypeCommand(_currentSheetId, chart.Id, ParseChartType(input)), "Change PivotChart Type"))
            return;

        UpdateViewport();
    }

    private void OnPivotChartFieldButtonRequested(ChartModel chart, string fieldButton, System.Windows.Point position)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null || !chart.IsPivotChart || string.IsNullOrWhiteSpace(chart.PivotTableName))
            return;

        var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
            string.Equals(pivot.Name, chart.PivotTableName, StringComparison.OrdinalIgnoreCase));
        if (pivotTable is null)
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        _pivotChartContextFieldCaption = PivotUiPlanner.ResolvePivotChartFieldButtonCaption(pivotTable, headers, fieldButton);
        if (string.IsNullOrWhiteSpace(_pivotChartContextFieldCaption))
            return;

        SetActiveCell(pivotTable.TargetRange.Start);
        RefreshPivotFieldListPane();

        var menu = CreatePivotFieldContextMenu();
        menu.Closed += (_, _) => _pivotChartContextFieldCaption = null;
        menu.PlacementTarget = SheetGrid;
        menu.Placement = PlacementMode.RelativePoint;
        menu.HorizontalOffset = position.X;
        menu.VerticalOffset = position.Y;
        menu.IsOpen = true;
    }

    private ContextMenu CreatePivotFieldContextMenu()
    {
        var menu = new ContextMenu();
        void Add(string header, RoutedEventHandler handler)
        {
            var item = new MenuItem { Header = header };
            item.Click += handler;
            menu.Items.Add(item);
        }

        Add("Sort A to Z", PivotFieldSortAscendingMenuItem_Click);
        Add("Sort Z to A", PivotFieldSortDescendingMenuItem_Click);
        Add("Select Items...", PivotFieldSelectItemsMenuItem_Click);
        Add("Label Filter...", PivotFieldLabelFilterMenuItem_Click);
        Add("Value Filter...", PivotFieldValueFilterMenuItem_Click);
        Add("Clear Filter", PivotFieldClearFilterMenuItem_Click);
        menu.Items.Add(new Separator());
        Add("Value Field Settings...", PivotFieldValueSettingsMenuItem_Click);
        MenuKeyTipAssigner.AssignUniqueKeyTips(menu.Items.OfType<MenuItem>());
        return menu;
    }

    private void RefreshPivotFieldListPane()
    {
        if (PivotFieldListPane is null)
            return;

        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null)
        {
            PivotFieldListPane.Visibility = Visibility.Collapsed;
            SetPivotContextualTabsVisible(false);
            PivotAvailableFieldsList.ItemsSource = null;
            PivotRowsList.ItemsSource = null;
            PivotColumnsList.ItemsSource = null;
            PivotFiltersList.ItemsSource = null;
            PivotValuesList.ItemsSource = null;
            return;
        }

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        PivotAvailableFieldsList.ItemsSource = headers
            .Select((caption, index) => new PivotFieldListItem(
                caption,
                pivotTable.RowFields.Any(field => field.SourceFieldIndex == index) ||
                pivotTable.ColumnFields.Any(field => field.SourceFieldIndex == index) ||
                pivotTable.PageFields.Any(field => field.SourceFieldIndex == index) ||
                pivotTable.DataFields.Any(field => field.SourceFieldIndex == index)))
            .ToList();
        PivotRowsList.ItemsSource = pivotTable.RowFields
            .Select(field => PivotUiPlanner.FieldCaption(headers, field.SourceFieldIndex))
            .ToList();
        PivotColumnsList.ItemsSource = pivotTable.ColumnFields
            .Select(field => PivotUiPlanner.FieldCaption(headers, field.SourceFieldIndex))
            .ToList();
        PivotFiltersList.ItemsSource = pivotTable.PageFields
            .Select(field => PivotUiPlanner.FieldCaption(headers, field.SourceFieldIndex))
            .ToList();
        PivotValuesList.ItemsSource = pivotTable.DataFields
            .Select(field => field.Name)
            .ToList();
        PivotFieldListPane.Visibility = Visibility.Visible;
        SetPivotContextualTabsVisible(true);
    }

    private void RefreshSlicerTimelinePane()
    {
        if (SlicerTimelinePane is null)
            return;

        var slicers = _workbook.Slicers
            .Where(slicer => !string.IsNullOrWhiteSpace(slicer.Name))
            .Select(slicer => new SlicerPaneItem(
                slicer.Name,
                slicer.SourceFieldName ?? slicer.CacheName,
                BuildSlicerTiles(slicer)))
            .ToList();
        var timelines = _workbook.Timelines
            .Where(timeline => !string.IsNullOrWhiteSpace(timeline.Name))
            .Select(timeline => new TimelinePaneItem
            {
                Name = timeline.Name,
                FieldName = timeline.SourceFieldName ?? timeline.CacheName,
                SelectedStartDate = timeline.SelectedStartDate ?? timeline.StartDate ?? "",
                SelectedEndDate = timeline.SelectedEndDate ?? timeline.EndDate ?? ""
            })
            .ToList();

        SlicerItemsControl.ItemsSource = slicers;
        TimelineItemsControl.ItemsSource = timelines;
        if (slicers.Count == 0 && timelines.Count == 0)
        {
            SlicerTimelinePane.Visibility = Visibility.Collapsed;
            _slicerTimelinePaneDismissed = false;
        }
        else if (!_slicerTimelinePaneDismissed)
            SlicerTimelinePane.Visibility = Visibility.Visible;
    }

    private IReadOnlyList<SlicerTileItem> BuildSlicerTiles(SlicerModel slicer)
    {
        var selected = slicer.SelectedItems.ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        var items = ReadSlicerSourceItems(slicer).ToList();
        if (items.Count == 0)
            items.AddRange(slicer.SelectedItems);

        return items
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase)
            .Select(item => new SlicerTileItem(slicer.Name, item, selected.Count == 0 || selected.Contains(item)))
            .ToList();
    }

    private IReadOnlyList<string> ReadSlicerSourceItems(SlicerModel slicer)
    {
        if (string.IsNullOrWhiteSpace(slicer.SourcePivotTableName) ||
            string.IsNullOrWhiteSpace(slicer.SourceFieldName))
        {
            return [];
        }

        foreach (var sheet in _workbook.Sheets)
        {
            var pivotTable = sheet.PivotTables.FirstOrDefault(pivot =>
                string.Equals(pivot.Name, slicer.SourcePivotTableName, StringComparison.OrdinalIgnoreCase));
            if (pivotTable is null)
                continue;

            var headers = ReadPivotSourceHeaders(sheet, pivotTable);
            var sourceIndex = PivotUiPlanner.FindSourceFieldIndex(headers, slicer.SourceFieldName);
            return sourceIndex is null ? [] : ReadPivotFieldItems(sheet, pivotTable, sourceIndex.Value);
        }

        return [];
    }

    private void SlicerTimelinePaneCloseBtn_Click(object sender, RoutedEventArgs e)
    {
        _slicerTimelinePaneDismissed = true;
        SlicerTimelinePane.Visibility = Visibility.Collapsed;
    }

    private void SlicerTileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SlicerTileItem tile })
            return;

        var slicer = _workbook.Slicers.FirstOrDefault(item =>
            string.Equals(item.Name, tile.SlicerName, StringComparison.OrdinalIgnoreCase));
        if (slicer is null)
            return;

        var allItems = ReadSlicerSourceItems(slicer).ToList();
        var selected = slicer.SelectedItems.Count == 0
            ? allItems.ToHashSet(StringComparer.CurrentCultureIgnoreCase)
            : slicer.SelectedItems.ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        if (!selected.Remove(tile.Caption))
            selected.Add(tile.Caption);
        if (selected.Count == allItems.Count)
            selected.Clear();

        if (!TryExecuteCommand(new SetSlicerSelectionCommand(slicer.Name, selected.ToList()), "Slicer"))
            return;

        UpdateViewport();
    }

    private void SlicerClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string slicerName })
            return;

        if (!TryExecuteCommand(new SetSlicerSelectionCommand(slicerName, []), "Slicer"))
            return;

        UpdateViewport();
    }

    private void TimelineApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: TimelinePaneItem item })
            return;

        if (!TryExecuteCommand(
                new SetTimelineRangeCommand(item.Name, EmptyToNull(item.SelectedStartDate), EmptyToNull(item.SelectedEndDate)),
                "Timeline"))
            return;

        UpdateViewport();
    }

    private void TimelineClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: TimelinePaneItem item })
            return;

        if (!TryExecuteCommand(new SetTimelineRangeCommand(item.Name, null, null), "Timeline"))
            return;

        UpdateViewport();
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void SetPivotContextualTabsVisible(bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (PivotTableAnalyzeTab is not null)
            PivotTableAnalyzeTab.Visibility = visibility;
        if (PivotTableDesignTab is not null)
            PivotTableDesignTab.Visibility = visibility;
    }

    private void PivotFieldListBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (pivotTable is null)
        {
            MessageBox.Show(
                "Select a cell inside an existing PivotTable before showing the field list.",
                "PivotTable Fields",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        PivotFieldListPane.Visibility = PivotFieldListPane.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (PivotFieldListPane.Visibility == Visibility.Visible)
            RefreshPivotFieldListPane();
    }

    private void PivotChangeDataSourceBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null)
            return;

        var input = PromptForInput("PivotTable source range:", FormatWorkbookRange(pivotTable.SourceRange));
        if (string.IsNullOrWhiteSpace(input) || !TryParseWorkbookRange(sheet.Id, input, out var sourceRange))
            return;

        if (!TryExecuteCommand(
                new ChangePivotTableSourceCommand(_currentSheetId, pivotTable.Name, sourceRange),
                "Change PivotTable Data Source"))
            return;

        UpdateViewport();
    }

    private void PivotInsertSlicerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActivePivotTable(out var sheet, out var pivotTable))
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var fieldName = GetSelectedPivotFieldListItem();
        if (PivotUiPlanner.FindSourceFieldIndex(headers, fieldName) is null)
            fieldName = headers.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(fieldName))
            return;

        var name = PromptForInput("Slicer name:", $"{fieldName} Slicer");
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (!TryExecuteCommand(new AddSlicerCommand(name.Trim(), pivotTable.Name, fieldName), "Insert Slicer"))
            return;

        _slicerTimelinePaneDismissed = false;
        RefreshSlicerTimelinePane();
        UpdateViewport();
    }

    private void PivotInsertTimelineBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetActivePivotTable(out var sheet, out var pivotTable))
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var fieldName = GetSelectedPivotFieldListItem();
        if (PivotUiPlanner.FindSourceFieldIndex(headers, fieldName) is null)
            fieldName = headers.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(fieldName))
            return;

        var name = PromptForInput("Timeline name:", $"{fieldName} Timeline");
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (!TryExecuteCommand(new AddTimelineCommand(name.Trim(), pivotTable.Name, fieldName), "Insert Timeline"))
            return;

        _slicerTimelinePaneDismissed = false;
        RefreshSlicerTimelinePane();
        UpdateViewport();
    }

    private void PivotGrandTotalsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActivePivotTable(out _, out var pivotTable))
        {
            ApplyPivotOptions(
                pivotTable,
                !pivotTable.ShowRowGrandTotals,
                !pivotTable.ShowColumnGrandTotals,
                pivotTable.ShowSubtotals,
                pivotTable.SubtotalPlacement,
                pivotTable.RepeatItemLabels,
                pivotTable.BlankLineAfterItems,
                pivotTable.StyleName,
                pivotTable.ShowRowHeaders,
                pivotTable.ShowColumnHeaders,
                pivotTable.ShowRowStripes,
                pivotTable.ShowColumnStripes,
                pivotTable.ReportLayout);
        }
    }

    private void PivotSubtotalsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActivePivotTable(out _, out var pivotTable))
        {
            var showSubtotals = pivotTable.ShowSubtotals;
            var subtotalPlacement = pivotTable.SubtotalPlacement;
            if (!pivotTable.ShowSubtotals)
            {
                showSubtotals = true;
                subtotalPlacement = PivotSubtotalPlacement.Bottom;
            }
            else if (pivotTable.SubtotalPlacement == PivotSubtotalPlacement.Bottom)
            {
                subtotalPlacement = PivotSubtotalPlacement.Top;
            }
            else
            {
                showSubtotals = false;
                subtotalPlacement = PivotSubtotalPlacement.Bottom;
            }

            ApplyPivotOptions(
                pivotTable,
                pivotTable.ShowRowGrandTotals,
                pivotTable.ShowColumnGrandTotals,
                showSubtotals,
                subtotalPlacement,
                pivotTable.RepeatItemLabels,
                pivotTable.BlankLineAfterItems,
                pivotTable.StyleName,
                pivotTable.ShowRowHeaders,
                pivotTable.ShowColumnHeaders,
                pivotTable.ShowRowStripes,
                pivotTable.ShowColumnStripes,
                pivotTable.ReportLayout);
        }
    }

    private void PivotReportLayoutBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActivePivotTable(out _, out var pivotTable))
        {
            var reportLayout = pivotTable.ReportLayout switch
            {
                PivotReportLayout.Compact => PivotReportLayout.Outline,
                PivotReportLayout.Outline => PivotReportLayout.Tabular,
                _ => PivotReportLayout.Compact
            };
            ApplyPivotOptions(
                pivotTable,
                pivotTable.ShowRowGrandTotals,
                pivotTable.ShowColumnGrandTotals,
                pivotTable.ShowSubtotals,
                pivotTable.SubtotalPlacement,
                pivotTable.RepeatItemLabels,
                pivotTable.BlankLineAfterItems,
                pivotTable.StyleName,
                pivotTable.ShowRowHeaders,
                pivotTable.ShowColumnHeaders,
                pivotTable.ShowRowStripes,
                pivotTable.ShowColumnStripes,
                reportLayout);
        }
    }

    private void PivotBlankRowsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActivePivotTable(out _, out var pivotTable))
            ApplyPivotOptions(
                pivotTable,
                pivotTable.ShowRowGrandTotals,
                pivotTable.ShowColumnGrandTotals,
                pivotTable.ShowSubtotals,
                pivotTable.SubtotalPlacement,
                pivotTable.RepeatItemLabels,
                !pivotTable.BlankLineAfterItems,
                pivotTable.StyleName,
                pivotTable.ShowRowHeaders,
                pivotTable.ShowColumnHeaders,
                pivotTable.ShowRowStripes,
                pivotTable.ShowColumnStripes,
                pivotTable.ReportLayout);
    }

    private void PivotStyleGalleryBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActivePivotTable(out _, out var pivotTable))
        {
            var styleName = pivotTable.StyleName switch
            {
                "PivotStyleLight16" => "PivotStyleMedium9",
                "PivotStyleMedium9" => "PivotStyleDark4",
                _ => "PivotStyleLight16"
            };
            ApplyPivotOptions(
                pivotTable,
                pivotTable.ShowRowGrandTotals,
                pivotTable.ShowColumnGrandTotals,
                pivotTable.ShowSubtotals,
                pivotTable.SubtotalPlacement,
                pivotTable.RepeatItemLabels,
                pivotTable.BlankLineAfterItems,
                styleName,
                pivotTable.ShowRowHeaders,
                pivotTable.ShowColumnHeaders,
                pivotTable.ShowRowStripes,
                pivotTable.ShowColumnStripes,
                pivotTable.ReportLayout);
        }
    }

    private void PivotRowHeadersBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActivePivotTable(out _, out var pivotTable))
            ApplyPivotOptions(
                pivotTable,
                pivotTable.ShowRowGrandTotals,
                pivotTable.ShowColumnGrandTotals,
                pivotTable.ShowSubtotals,
                pivotTable.SubtotalPlacement,
                pivotTable.RepeatItemLabels,
                pivotTable.BlankLineAfterItems,
                pivotTable.StyleName,
                !pivotTable.ShowRowHeaders,
                pivotTable.ShowColumnHeaders,
                pivotTable.ShowRowStripes,
                pivotTable.ShowColumnStripes,
                pivotTable.ReportLayout);
    }

    private void PivotColumnHeadersBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActivePivotTable(out _, out var pivotTable))
            ApplyPivotOptions(
                pivotTable,
                pivotTable.ShowRowGrandTotals,
                pivotTable.ShowColumnGrandTotals,
                pivotTable.ShowSubtotals,
                pivotTable.SubtotalPlacement,
                pivotTable.RepeatItemLabels,
                pivotTable.BlankLineAfterItems,
                pivotTable.StyleName,
                pivotTable.ShowRowHeaders,
                !pivotTable.ShowColumnHeaders,
                pivotTable.ShowRowStripes,
                pivotTable.ShowColumnStripes,
                pivotTable.ReportLayout);
    }

    private void PivotBandedRowsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActivePivotTable(out _, out var pivotTable))
            ApplyPivotOptions(
                pivotTable,
                pivotTable.ShowRowGrandTotals,
                pivotTable.ShowColumnGrandTotals,
                pivotTable.ShowSubtotals,
                pivotTable.SubtotalPlacement,
                pivotTable.RepeatItemLabels,
                pivotTable.BlankLineAfterItems,
                pivotTable.StyleName,
                pivotTable.ShowRowHeaders,
                pivotTable.ShowColumnHeaders,
                !pivotTable.ShowRowStripes,
                pivotTable.ShowColumnStripes,
                pivotTable.ReportLayout);
    }

    private void PivotBandedColumnsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetActivePivotTable(out _, out var pivotTable))
            ApplyPivotOptions(
                pivotTable,
                pivotTable.ShowRowGrandTotals,
                pivotTable.ShowColumnGrandTotals,
                pivotTable.ShowSubtotals,
                pivotTable.SubtotalPlacement,
                pivotTable.RepeatItemLabels,
                pivotTable.BlankLineAfterItems,
                pivotTable.StyleName,
                pivotTable.ShowRowHeaders,
                pivotTable.ShowColumnHeaders,
                pivotTable.ShowRowStripes,
                !pivotTable.ShowColumnStripes,
                pivotTable.ReportLayout);
    }

    private void ApplyPivotOptions(
        PivotTableModel pivotTable,
        bool showRowGrandTotals,
        bool showColumnGrandTotals,
        bool showSubtotals,
        PivotSubtotalPlacement subtotalPlacement,
        bool repeatItemLabels,
        bool blankLineAfterItems,
        string styleName,
        bool showRowHeaders,
        bool showColumnHeaders,
        bool showRowStripes,
        bool showColumnStripes,
        PivotReportLayout reportLayout)
    {
        if (!TryExecuteCommand(
                new ConfigurePivotTableOptionsCommand(
                    _currentSheetId,
                    pivotTable.Name,
                    showRowGrandTotals,
                    showColumnGrandTotals,
                    showSubtotals,
                    subtotalPlacement,
                    repeatItemLabels,
                    blankLineAfterItems,
                    styleName,
                    showRowHeaders,
                    showColumnHeaders,
                    showRowStripes,
                    showColumnStripes,
                    reportLayout),
                "PivotTable Options"))
            return;

        UpdateViewport();
    }

    private bool TryGetActivePivotTable(out Sheet sheet, out PivotTableModel pivotTable)
    {
        sheet = _workbook.GetSheet(_currentSheetId)!;
        pivotTable = sheet is null ? null! : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange)!;
        return sheet is not null && pivotTable is not null;
    }

    private void PivotFieldListCloseBtn_Click(object sender, RoutedEventArgs e)
    {
        PivotFieldListPane.Visibility = Visibility.Collapsed;
    }

    private void PivotFieldToRowsBtn_Click(object sender, RoutedEventArgs e) =>
        MoveSelectedPivotField(PivotFieldDropZone.Rows);

    private void PivotFieldToColumnsBtn_Click(object sender, RoutedEventArgs e) =>
        MoveSelectedPivotField(PivotFieldDropZone.Columns);

    private void PivotFieldToValuesBtn_Click(object sender, RoutedEventArgs e) =>
        MoveSelectedPivotField(PivotFieldDropZone.Values);

    private void PivotFieldToFiltersBtn_Click(object sender, RoutedEventArgs e) =>
        MoveSelectedPivotField(PivotFieldDropZone.Filters);

    private void PivotFieldList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            sender is not ListBox list ||
            GetPivotListItemCaption(list.SelectedItem) is not { } caption)
        {
            return;
        }

        DragDrop.DoDragDrop(list, caption, DragDropEffects.Move);
    }

    private void PivotFieldList_Drop(object sender, DragEventArgs e)
    {
        if (sender is not ListBox targetList ||
            e.Data.GetData(DataFormats.StringFormat) is not string caption ||
            GetPivotFieldDropZone(targetList) is not { } targetZone)
        {
            return;
        }

        MovePivotFieldToZone(caption, targetZone, targetList.SelectedIndex);
        e.Handled = true;
    }

    private void PivotAvailableFieldCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: PivotFieldListItem item } checkBox)
            return;

        TogglePivotAvailableField(item.Caption, checkBox.IsChecked == true);
    }

    private void TogglePivotAvailableField(string caption, bool isChecked)
    {
        if (isChecked)
        {
            var sheet = _workbook.GetSheet(_currentSheetId);
            var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
            if (sheet is null || pivotTable is null)
                return;

            var headers = ReadPivotSourceHeaders(sheet, pivotTable);
            var sourceIndex = PivotUiPlanner.FindSourceFieldIndex(headers, caption);
            if (sourceIndex is null)
                return;

            var zone = IsNumericPivotSourceField(sheet, pivotTable, sourceIndex.Value)
                ? PivotFieldDropZone.Values
                : PivotFieldDropZone.Rows;
            MovePivotFieldToZone(caption, zone, -1);
            return;
        }

        MovePivotFieldToZone(caption, PivotFieldDropZone.Available, -1);
    }

    private void PivotFieldRemoveBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null)
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var selected = GetSelectedPivotFieldListItem();
        if (string.IsNullOrWhiteSpace(selected))
            return;

        var sourceIndex = PivotUiPlanner.FindSourceFieldIndex(headers, selected);
        var rowFields = sourceIndex is null
            ? pivotTable.RowFields.ToList()
            : pivotTable.RowFields.Where(field => field.SourceFieldIndex != sourceIndex.Value).ToList();
        var columnFields = sourceIndex is null
            ? pivotTable.ColumnFields.ToList()
            : pivotTable.ColumnFields.Where(field => field.SourceFieldIndex != sourceIndex.Value).ToList();
        var pageFields = sourceIndex is null
            ? pivotTable.PageFields.ToList()
            : pivotTable.PageFields.Where(field => field.SourceFieldIndex != sourceIndex.Value).ToList();
        var dataFields = pivotTable.DataFields
            .Where(field => !string.Equals(field.Name, selected, StringComparison.CurrentCultureIgnoreCase) &&
                            (sourceIndex is null || field.SourceFieldIndex != sourceIndex.Value))
            .ToList();

        ApplyPivotFieldListLayout(pivotTable, rowFields, columnFields, pageFields, dataFields);
    }

    private void PivotFieldSortAscendingMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyPivotFieldSort(PivotSortDirection.Ascending);

    private void PivotFieldSortDescendingMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyPivotFieldSort(PivotSortDirection.Descending);

    private void PivotFieldClearFilterMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null)
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var selected = GetSelectedPivotFieldListItem();
        var sourceIndex = PivotUiPlanner.FindSourceFieldIndex(headers, selected);
        var dataFieldIndex = PivotUiPlanner.FindDataFieldIndex(pivotTable, selected);

        var labelFilters = sourceIndex is null
            ? pivotTable.LabelFilters.ToList()
            : pivotTable.LabelFilters.Where(filter => filter.SourceFieldIndex != sourceIndex.Value).ToList();
        var valueFilters = sourceIndex is null
            ? pivotTable.ValueFilters.ToList()
            : pivotTable.ValueFilters.Where(filter => filter.SourceFieldIndex != sourceIndex.Value).ToList();
        var sorts = pivotTable.Sorts
            .Where(sort =>
                (sourceIndex is null || sort.FieldIndex != sourceIndex.Value) &&
                (dataFieldIndex is null || sort.DataFieldIndex != dataFieldIndex.Value))
            .ToList();

        ApplyPivotFieldView(pivotTable, labelFilters, valueFilters, sorts);
    }

    private void PivotFieldSelectItemsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null)
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var sourceIndex = PivotUiPlanner.FindSourceFieldIndex(headers, GetSelectedPivotFieldListItem());
        if (sourceIndex is null)
            return;

        var existingItems = pivotTable.RowFields
            .Concat(pivotTable.ColumnFields)
            .Concat(pivotTable.PageFields)
            .FirstOrDefault(field => field.SourceFieldIndex == sourceIndex.Value)
            ?.SelectedItems;
        var dialog = new PivotFieldFilterDialog(ReadPivotFieldItems(sheet, pivotTable, sourceIndex.Value), existingItems)
        {
            Owner = this,
            Title = $"{PivotUiPlanner.FieldCaption(headers, sourceIndex.Value)} Filter"
        };
        if (dialog.ShowDialog() != true)
            return;

        var allItems = ReadPivotFieldItems(sheet, pivotTable, sourceIndex.Value).ToList();
        var selectedItems = dialog.SelectedItems;
        var items = selectedItems.Count == 0 || selectedItems.Count == allItems.Count ? null : selectedItems;
        var rowFields = PivotUiPlanner.SetFieldSelectedItems(pivotTable.RowFields, sourceIndex.Value, items);
        var columnFields = PivotUiPlanner.SetFieldSelectedItems(pivotTable.ColumnFields, sourceIndex.Value, items);
        var pageFields = PivotUiPlanner.SetFieldSelectedItems(pivotTable.PageFields, sourceIndex.Value, items);

        ApplyPivotFieldListLayout(pivotTable, rowFields, columnFields, pageFields, pivotTable.DataFields.ToList());
    }

    private void PivotFieldLabelFilterMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null)
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var sourceIndex = PivotUiPlanner.FindSourceFieldIndex(headers, GetSelectedPivotFieldListItem());
        if (sourceIndex is null)
            return;

        var dialog = new PivotLabelFilterDialog(sourceIndex.Value) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.ResultFilter is not { } filter)
            return;

        var labelFilters = pivotTable.LabelFilters
            .Where(item => item.SourceFieldIndex != sourceIndex.Value)
            .Append(filter)
            .ToList();
        ApplyPivotFieldView(pivotTable, labelFilters, pivotTable.ValueFilters.ToList(), pivotTable.Sorts.ToList());
    }

    private void PivotFieldValueFilterMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null || pivotTable.DataFields.Count == 0)
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var sourceIndex = PivotUiPlanner.FindSourceFieldIndex(headers, GetSelectedPivotFieldListItem());
        if (sourceIndex is null)
            return;

        var dialog = new PivotValueFilterDialog(sourceIndex.Value) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.ResultFilter is not { } filter)
            return;

        var valueFilters = pivotTable.ValueFilters
            .Where(item => item.SourceFieldIndex != sourceIndex.Value)
            .Append(filter)
            .ToList();
        ApplyPivotFieldView(pivotTable, pivotTable.LabelFilters.ToList(), valueFilters, pivotTable.Sorts.ToList());
    }

    private void PivotFieldValueSettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null)
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var selected = GetSelectedPivotFieldListItem();
        var dataFieldIndex = PivotUiPlanner.FindDataFieldIndex(pivotTable, selected);
        if (dataFieldIndex is null)
        {
            var sourceIndex = PivotUiPlanner.FindSourceFieldIndex(headers, selected);
            if (sourceIndex is null)
                return;
            dataFieldIndex = pivotTable.DataFields.FindIndex(field => field.SourceFieldIndex == sourceIndex.Value);
            if (dataFieldIndex < 0)
                return;
        }

        var current = pivotTable.DataFields[dataFieldIndex.Value];
        var dialog = new PivotValueFieldSettingsDialog(current, headers) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        var dataFields = pivotTable.DataFields.ToList();
        dataFields[dataFieldIndex.Value] = dialog.ResultDataField;

        ApplyPivotFieldListLayout(
            pivotTable,
            pivotTable.RowFields.ToList(),
            pivotTable.ColumnFields.ToList(),
            pivotTable.PageFields.ToList(),
            dataFields);
    }

    private void MoveSelectedPivotField(PivotFieldDropZone zone)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null)
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var selected = GetSelectedPivotFieldListItem();
        var sourceIndex = PivotUiPlanner.FindSourceFieldIndex(headers, selected);
        if (sourceIndex is null)
            return;

        var rowFields = pivotTable.RowFields.Where(field => field.SourceFieldIndex != sourceIndex.Value).ToList();
        var columnFields = pivotTable.ColumnFields.Where(field => field.SourceFieldIndex != sourceIndex.Value).ToList();
        var pageFields = pivotTable.PageFields.Where(field => field.SourceFieldIndex != sourceIndex.Value).ToList();
        var dataFields = pivotTable.DataFields.ToList();
        var field = new PivotFieldModel(sourceIndex.Value);

        switch (zone)
        {
            case PivotFieldDropZone.Rows:
                rowFields.Add(field);
                break;
            case PivotFieldDropZone.Columns:
                columnFields.Add(field);
                break;
            case PivotFieldDropZone.Filters:
                pageFields.Add(field);
                break;
            case PivotFieldDropZone.Values:
                if (dataFields.All(dataField => dataField.SourceFieldIndex != sourceIndex.Value))
                {
                    var caption = PivotUiPlanner.FieldCaption(headers, sourceIndex.Value);
                    var summaryFunction = IsNumericPivotSourceField(sheet, pivotTable, sourceIndex.Value) ? "sum" : "count";
                    var displayName = summaryFunction == "sum" ? $"Sum of {caption}" : $"Count of {caption}";
                    dataFields.Add(new PivotDataFieldModel(sourceIndex.Value, displayName, summaryFunction));
                }
                break;
        }

        ApplyPivotFieldListLayout(pivotTable, rowFields, columnFields, pageFields, dataFields);
    }

    private void MovePivotFieldToZone(string caption, PivotFieldDropZone targetZone, int insertIndex)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null)
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var sourceIndex = PivotUiPlanner.FindFieldSourceIndex(headers, pivotTable, caption);
        var draggedDataField = pivotTable.DataFields.FirstOrDefault(field =>
            string.Equals(field.Name, caption, StringComparison.CurrentCultureIgnoreCase));
        if (sourceIndex is null && draggedDataField is null)
            return;

        var rowFields = pivotTable.RowFields.Where(field => field.SourceFieldIndex != sourceIndex).ToList();
        var columnFields = pivotTable.ColumnFields.Where(field => field.SourceFieldIndex != sourceIndex).ToList();
        var pageFields = pivotTable.PageFields.Where(field => field.SourceFieldIndex != sourceIndex).ToList();
        var dataFields = pivotTable.DataFields
            .Where(field => !string.Equals(field.Name, caption, StringComparison.CurrentCultureIgnoreCase) &&
                            field.SourceFieldIndex != sourceIndex)
            .ToList();

        if (targetZone == PivotFieldDropZone.Available)
        {
            ApplyPivotFieldListLayout(pivotTable, rowFields, columnFields, pageFields, dataFields);
            return;
        }

        if (sourceIndex is null)
            return;

        switch (targetZone)
        {
            case PivotFieldDropZone.Rows:
                InsertAt(rowFields, PivotUiPlanner.FindExistingPivotField(pivotTable, sourceIndex.Value), insertIndex);
                break;
            case PivotFieldDropZone.Columns:
                InsertAt(columnFields, PivotUiPlanner.FindExistingPivotField(pivotTable, sourceIndex.Value), insertIndex);
                break;
            case PivotFieldDropZone.Filters:
                InsertAt(pageFields, PivotUiPlanner.FindExistingPivotField(pivotTable, sourceIndex.Value), insertIndex);
                break;
            case PivotFieldDropZone.Values:
                var valueField = draggedDataField ?? CreateDefaultPivotDataField(sheet, pivotTable, headers, sourceIndex.Value);
                InsertAt(dataFields, valueField, insertIndex);
                break;
        }

        ApplyPivotFieldListLayout(pivotTable, rowFields, columnFields, pageFields, dataFields);
    }

    private void ApplyPivotFieldSort(PivotSortDirection direction)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var pivotTable = sheet is null ? null : PivotUiPlanner.FindPivotTableForSelection(sheet, SheetGrid.SelectedRange);
        if (sheet is null || pivotTable is null)
            return;

        var headers = ReadPivotSourceHeaders(sheet, pivotTable);
        var selected = GetSelectedPivotFieldListItem();
        var sourceIndex = PivotUiPlanner.FindSourceFieldIndex(headers, selected);
        var dataFieldIndex = PivotUiPlanner.FindDataFieldIndex(pivotTable, selected);
        if (sourceIndex is null && dataFieldIndex is null)
            return;

        var sorts = pivotTable.Sorts
            .Where(sort =>
                (sourceIndex is null || sort.FieldIndex != sourceIndex.Value) &&
                (dataFieldIndex is null || sort.DataFieldIndex != dataFieldIndex.Value))
            .ToList();

        if (dataFieldIndex is not null)
        {
            sorts.Add(new PivotSortModel(
                PivotSortTarget.Value,
                direction,
                DataFieldIndex: dataFieldIndex.Value,
                FieldIndex: pivotTable.RowFields.LastOrDefault()?.SourceFieldIndex ??
                            pivotTable.ColumnFields.LastOrDefault()?.SourceFieldIndex ??
                            0));
        }
        else
        {
            sorts.Add(new PivotSortModel(PivotSortTarget.Label, direction, FieldIndex: sourceIndex.GetValueOrDefault()));
        }

        ApplyPivotFieldView(pivotTable, pivotTable.LabelFilters.ToList(), pivotTable.ValueFilters.ToList(), sorts);
    }

    private void ApplyPivotFieldListLayout(
        PivotTableModel pivotTable,
        IReadOnlyList<PivotFieldModel> rowFields,
        IReadOnlyList<PivotFieldModel> columnFields,
        IReadOnlyList<PivotFieldModel> pageFields,
        IReadOnlyList<PivotDataFieldModel> dataFields)
    {
        if (dataFields.Count == 0)
        {
            MessageBox.Show(
                "A PivotTable requires at least one value field.",
                "PivotTable Fields",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!TryExecuteCommand(
                new ConfigurePivotTableLayoutCommand(_currentSheetId, pivotTable.Name, rowFields, columnFields, pageFields, dataFields),
                "PivotTable Fields"))
            return;

        UpdateViewport();
    }

    private void ApplyPivotFieldView(
        PivotTableModel pivotTable,
        IReadOnlyList<PivotLabelFilterModel> labelFilters,
        IReadOnlyList<PivotValueFilterModel> valueFilters,
        IReadOnlyList<PivotSortModel> sorts)
    {
        if (!TryExecuteCommand(
                new ConfigurePivotTableViewCommand(_currentSheetId, pivotTable.Name, labelFilters, valueFilters, sorts),
                "PivotTable Field"))
            return;

        UpdateViewport();
    }

    private string? GetSelectedPivotFieldListItem()
    {
        if (!string.IsNullOrWhiteSpace(_pivotChartContextFieldCaption))
            return _pivotChartContextFieldCaption;

        foreach (var list in new[] { PivotAvailableFieldsList, PivotRowsList, PivotColumnsList, PivotValuesList, PivotFiltersList })
        {
            if (GetPivotListItemCaption(list.SelectedItem) is { } value)
                return value;
        }

        return null;
    }

    private static string? GetPivotListItemCaption(object? item) =>
        item switch
        {
            string value when !string.IsNullOrWhiteSpace(value) => value,
            PivotFieldListItem field when !string.IsNullOrWhiteSpace(field.Caption) => field.Caption,
            _ => null
        };

    private Sheet GetPivotSourceSheet(Sheet fallbackSheet, PivotTableModel pivotTable) =>
        _workbook.GetSheet(pivotTable.SourceRange.Start.Sheet) ?? fallbackSheet;

    private List<string> ReadPivotSourceHeaders(Sheet sheet, PivotTableModel pivotTable)
    {
        var sourceSheet = GetPivotSourceSheet(sheet, pivotTable);
        var headers = new List<string>();
        var start = pivotTable.SourceRange.Start;
        for (var col = start.Col; col <= pivotTable.SourceRange.End.Col; col++)
        {
            var caption = FormatCellValue(sourceSheet.GetValue(start.Row, col)).Trim();
            headers.Add(string.IsNullOrWhiteSpace(caption) ? $"Column {headers.Count + 1}" : caption);
        }

        return headers;
    }

    private IReadOnlyList<string> ReadPivotFieldItems(Sheet sheet, PivotTableModel pivotTable, int sourceFieldIndex)
    {
        var sourceSheet = GetPivotSourceSheet(sheet, pivotTable);
        var sourceColumn = pivotTable.SourceRange.Start.Col + (uint)sourceFieldIndex;
        var values = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        for (var row = pivotTable.SourceRange.Start.Row + 1; row <= pivotTable.SourceRange.End.Row; row++)
        {
            var text = FormatCellValue(sourceSheet.GetValue(row, sourceColumn)).Trim();
            values.Add(string.IsNullOrWhiteSpace(text) ? "(blank)" : text);
        }

        return values.OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private bool TryParseWorkbookRange(SheetId defaultSheetId, string input, out GridRange range)
    {
        range = default;
        var normalized = input.Trim();
        var sheetId = defaultSheetId;
        var bangIndex = normalized.LastIndexOf('!');
        if (bangIndex >= 0)
        {
            var sheetName = UnquoteSheetName(normalized[..bangIndex].Trim());
            var sheet = _workbook.Sheets.FirstOrDefault(item =>
                string.Equals(item.Name, sheetName, StringComparison.CurrentCultureIgnoreCase));
            if (sheet is null)
                return false;

            sheetId = sheet.Id;
            normalized = normalized[(bangIndex + 1)..].Trim();
        }

        var parts = normalized.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 0 or > 2)
            return false;

        try
        {
            var start = CellAddress.Parse(parts[0], sheetId);
            var end = parts.Length == 1 ? start : CellAddress.Parse(parts[1], sheetId);
            range = new GridRange(start, end);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string FormatWorkbookRange(GridRange range)
    {
        var reference = $"{range.Start.ToA1()}:{range.End.ToA1()}";
        var sheet = _workbook.GetSheet(range.Start.Sheet);
        return sheet is null || sheet.Id == _currentSheetId
            ? reference
            : $"{QuoteSheetNameForReference(sheet.Name)}!{reference}";
    }

    private static string UnquoteSheetName(string sheetName)
    {
        if (sheetName.Length >= 2 && sheetName[0] == '\'' && sheetName[^1] == '\'')
            return sheetName[1..^1].Replace("''", "'", StringComparison.Ordinal);

        return sheetName;
    }

    private static string QuoteSheetNameForReference(string sheetName)
    {
        if (sheetName.All(ch => char.IsLetterOrDigit(ch) || ch == '_'))
            return sheetName;

        return $"'{sheetName.Replace("'", "''", StringComparison.Ordinal)}'";
    }

    private static PivotDataFieldModel CreateDefaultPivotDataField(Sheet sheet, PivotTableModel pivotTable, IReadOnlyList<string> headers, int sourceFieldIndex)
    {
        var caption = PivotUiPlanner.FieldCaption(headers, sourceFieldIndex);
        var summaryFunction = IsNumericPivotSourceField(sheet, pivotTable, sourceFieldIndex) ? "sum" : "count";
        var displayName = summaryFunction == "sum" ? $"Sum of {caption}" : $"Count of {caption}";
        return new PivotDataFieldModel(sourceFieldIndex, displayName, summaryFunction);
    }

    private static void InsertAt<T>(List<T> items, T item, int index)
    {
        if (index < 0 || index > items.Count)
            items.Add(item);
        else
            items.Insert(index, item);
    }

    private PivotFieldDropZone? GetPivotFieldDropZone(ListBox list)
    {
        if (ReferenceEquals(list, PivotRowsList))
            return PivotFieldDropZone.Rows;
        if (ReferenceEquals(list, PivotColumnsList))
            return PivotFieldDropZone.Columns;
        if (ReferenceEquals(list, PivotFiltersList))
            return PivotFieldDropZone.Filters;
        if (ReferenceEquals(list, PivotValuesList))
            return PivotFieldDropZone.Values;
        if (ReferenceEquals(list, PivotAvailableFieldsList))
            return PivotFieldDropZone.Available;
        return null;
    }

    private static bool IsNumericPivotSourceField(Sheet sheet, PivotTableModel pivotTable, int sourceFieldIndex)
    {
        var sourceColumn = pivotTable.SourceRange.Start.Col + (uint)sourceFieldIndex;
        for (var row = pivotTable.SourceRange.Start.Row + 1; row <= pivotTable.SourceRange.End.Row; row++)
        {
            if (sheet.GetValue(row, sourceColumn) is NumberValue or DateTimeValue)
                return true;
        }

        return false;
    }

    private static bool TryParsePivotLabelFilter(string input, int sourceFieldIndex, out PivotLabelFilterModel filter)
    {
        filter = new PivotLabelFilterModel(sourceFieldIndex, PivotLabelFilterKind.Contains, "");
        var normalized = input.Trim();
        if (normalized.StartsWith("<>", StringComparison.Ordinal))
        {
            filter = new PivotLabelFilterModel(sourceFieldIndex, PivotLabelFilterKind.DoesNotEqual, normalized[2..].Trim());
            return !string.IsNullOrWhiteSpace(filter.Value);
        }

        var parts = normalized.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[1]))
            return false;

        var kind = parts[0].ToLowerInvariant() switch
        {
            "equals" or "=" => PivotLabelFilterKind.Equals,
            "notequals" or "not" or "<>" => PivotLabelFilterKind.DoesNotEqual,
            "begins" or "beginswith" => PivotLabelFilterKind.BeginsWith,
            "ends" or "endswith" => PivotLabelFilterKind.EndsWith,
            "contains" => PivotLabelFilterKind.Contains,
            "notcontains" => PivotLabelFilterKind.DoesNotContain,
            _ => PivotLabelFilterKind.Contains
        };
        filter = new PivotLabelFilterModel(sourceFieldIndex, kind, parts[1]);
        return true;
    }

    private static bool TryParsePivotValueFilter(string input, int sourceFieldIndex, out PivotValueFilterModel filter)
    {
        filter = new PivotValueFilterModel(0, PivotValueFilterKind.GreaterThan, SourceFieldIndex: sourceFieldIndex);
        var normalized = input.Trim();
        if (TryParseTopBottomPivotValueFilter(normalized, sourceFieldIndex, out filter))
            return true;

        var operators = new[]
        {
            (Text: ">=", Kind: PivotValueFilterKind.GreaterThanOrEqual),
            (Text: "<=", Kind: PivotValueFilterKind.LessThanOrEqual),
            (Text: "<>", Kind: PivotValueFilterKind.DoesNotEqual),
            (Text: ">", Kind: PivotValueFilterKind.GreaterThan),
            (Text: "<", Kind: PivotValueFilterKind.LessThan),
            (Text: "=", Kind: PivotValueFilterKind.Equals)
        };
        foreach (var op in operators)
        {
            if (!normalized.StartsWith(op.Text, StringComparison.Ordinal))
                continue;

            if (!double.TryParse(
                    normalized[op.Text.Length..].Trim(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var value))
            {
                return false;
            }

            filter = new PivotValueFilterModel(0, op.Kind, ComparisonValue: value, SourceFieldIndex: sourceFieldIndex);
            return true;
        }

        return false;
    }

    private static bool TryParseTopBottomPivotValueFilter(string input, int sourceFieldIndex, out PivotValueFilterModel filter)
    {
        filter = new PivotValueFilterModel(0, PivotValueFilterKind.Top, SourceFieldIndex: sourceFieldIndex);
        var parts = input.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !int.TryParse(parts[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var count) ||
            count <= 0)
        {
            return false;
        }

        var kind = parts[0].ToLowerInvariant() switch
        {
            "top" => PivotValueFilterKind.Top,
            "bottom" => PivotValueFilterKind.Bottom,
            _ => (PivotValueFilterKind?)null
        };
        if (kind is null)
            return false;

        filter = new PivotValueFilterModel(0, kind.Value, Count: count, SourceFieldIndex: sourceFieldIndex);
        return true;
    }

    private enum PivotFieldDropZone
    {
        Available,
        Rows,
        Columns,
        Values,
        Filters
    }

    private void TableBtn_Click(object sender, RoutedEventArgs e) => ApplyTableFormat(0);

    private void InsertChartPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void ChartColumnMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Column);
    private void ChartStackedColumnMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.StackedColumn);
    private void ChartPercentStackedColumnMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.PercentStackedColumn);
    private void ChartLineMenuItem_Click(object sender, RoutedEventArgs e)   => InsertChartOfType(ChartType.Line);
    private void ChartPieMenuItem_Click(object sender, RoutedEventArgs e)    => InsertChartOfType(ChartType.Pie);
    private void ChartDoughnutMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Doughnut);
    private void ChartBarMenuItem_Click(object sender, RoutedEventArgs e)    => InsertChartOfType(ChartType.Bar);
    private void ChartStackedBarMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.StackedBar);
    private void ChartPercentStackedBarMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.PercentStackedBar);
    private void ChartAreaMenuItem_Click(object sender, RoutedEventArgs e)   => InsertChartOfType(ChartType.Area);
    private void ChartScatterMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Scatter);
    private void ChartBubbleMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Bubble);
    private void ChartRadarMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Radar);
    private void ChartStockMenuItem_Click(object sender, RoutedEventArgs e) => InsertChartOfType(ChartType.Stock);

    private void ChartFirstSliceAngleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteRepeatableChartLayout(
                "First Slice Angle",
                "Insert or select a pie or doughnut chart before changing first-slice angle.",
                chart => chart.Type is ChartType.Pie or ChartType.Doughnut,
                "First-slice angle only applies to pie and doughnut charts.",
                chart => new ChartLayoutOptions(FirstSliceAngle: chart.FirstSliceAngle >= 270 ? 0 : chart.FirstSliceAngle + 90)))
            return;

        UpdateViewport();
    }

    private void ChartDoughnutHoleSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteRepeatableChartLayout(
                "Doughnut Hole Size",
                "Insert or select a doughnut chart before changing hole size.",
                chart => chart.Type == ChartType.Doughnut,
                "Doughnut hole size only applies to doughnut charts.",
                chart => new ChartLayoutOptions(
                    DoughnutHoleSize: chart.DoughnutHoleSize switch
                    {
                        < 0.45 => 0.55,
                        < 0.7 => 0.75,
                        _ => 0.35
                    })))
            return;

        UpdateViewport();
    }

    private void ChartExplodedSliceBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteRepeatableChartLayout(
                "Explode Slice",
                "Insert or select a pie or doughnut chart before exploding a slice.",
                chart => chart.Type is (ChartType.Pie or ChartType.Doughnut) && ChartTypeSupport.GetDataPointCount(chart) > 0,
                "Exploded slices require a pie or doughnut chart with chart data.",
                chart =>
                {
                    var sliceCount = ChartTypeSupport.GetDataPointCount(chart);
                    var nextIndex = chart.ExplodedSliceIndex < 0
                        ? 0
                        : chart.ExplodedSliceIndex + 1 >= sliceCount ? -1 : chart.ExplodedSliceIndex + 1;
                    var nextDistance = nextIndex < 0
                        ? 0.1
                        : chart.ExplodedSliceDistance >= 0.22 ? 0.1 : chart.ExplodedSliceDistance + 0.06;
                    return new ChartLayoutOptions(ExplodedSliceIndex: nextIndex, ExplodedSliceDistance: nextDistance);
                }))
            return;

        UpdateViewport();
    }

    private void ChartDataLabelsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteRepeatableChartLayout(
                "Data Labels",
                "Insert or select a chart before changing data labels.",
                null,
                null,
                chart => new ChartLayoutOptions(ShowDataLabels: !chart.ShowDataLabels)))
            return;

        UpdateViewport();
    }

    private void ChartDataLabelPositionBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteRepeatableChartLayout(
                "Data Label Position",
                "Insert or select a chart before changing data label positions.",
                null,
                null,
                chart => new ChartLayoutOptions(
                    ShowDataLabels: true,
                    DataLabelPosition: GetNextDataLabelPosition(chart.DataLabelPosition))))
            return;

        UpdateViewport();
    }

    private static ChartDataLabelPosition GetNextDataLabelPosition(ChartDataLabelPosition current) =>
        current switch
        {
            ChartDataLabelPosition.BestFit => ChartDataLabelPosition.OutsideEnd,
            ChartDataLabelPosition.OutsideEnd => ChartDataLabelPosition.InsideEnd,
            ChartDataLabelPosition.InsideEnd => ChartDataLabelPosition.Center,
            _ => ChartDataLabelPosition.BestFit
        };

    private void ChartDataLabelCategoryBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleDataLabelOption(
            "Category Name",
            chart => new ChartLayoutOptions(
                ShowDataLabels: true,
                ShowDataLabelCategoryName: !chart.ShowDataLabelCategoryName));
    }

    private void ChartDataLabelSeriesBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleDataLabelOption(
            "Series Name",
            chart => new ChartLayoutOptions(
                ShowDataLabels: true,
                ShowDataLabelSeriesName: !chart.ShowDataLabelSeriesName));
    }

    private void ChartDataLabelPercentageBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleDataLabelOption(
            "Percentage",
            chart => new ChartLayoutOptions(
                ShowDataLabels: true,
                ShowDataLabelPercentage: !chart.ShowDataLabelPercentage));
    }

    private void ChartDataLabelSeparatorBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleDataLabelOption(
            "Label Separator",
            chart => new ChartLayoutOptions(
                ShowDataLabels: true,
                DataLabelSeparator: chart.DataLabelSeparator switch
                {
                    ChartDataLabelSeparator.Comma => ChartDataLabelSeparator.Semicolon,
                    ChartDataLabelSeparator.Semicolon => ChartDataLabelSeparator.NewLine,
                    ChartDataLabelSeparator.NewLine => ChartDataLabelSeparator.Space,
                    _ => ChartDataLabelSeparator.Comma
                }));
    }

    private void ChartDataLabelNumberFormatBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleDataLabelOption(
            "Label Number Format",
            chart => new ChartLayoutOptions(
                ShowDataLabels: true,
                DataLabelNumberFormat: chart.DataLabelNumberFormat switch
                {
                    ChartDataLabelNumberFormat.General => ChartDataLabelNumberFormat.Number,
                    ChartDataLabelNumberFormat.Number => ChartDataLabelNumberFormat.Currency,
                    ChartDataLabelNumberFormat.Currency => ChartDataLabelNumberFormat.Percent,
                    _ => ChartDataLabelNumberFormat.General
                }));
    }

    private void ChartDataLabelCalloutBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleDataLabelOption(
            "Data Callout",
            chart => new ChartLayoutOptions(
                ShowDataLabels: true,
                ShowDataLabelCallouts: !chart.ShowDataLabelCallouts));
    }

    private void ChartDataLabelFillBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleDataLabelOption(
            "Data Label Fill",
            chart => new ChartLayoutOptions(
                ShowDataLabels: true,
                DataLabelFillColor: GetNextSeriesColor(chart.DataLabelFillColor)));
    }

    private void ChartDataLabelTextBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleDataLabelOption(
            "Data Label Text",
            chart => new ChartLayoutOptions(
                ShowDataLabels: true,
                DataLabelTextColor: GetNextSeriesColor(chart.DataLabelTextColor)));
    }

    private void ChartDataLabelBorderBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleDataLabelOption(
            "Data Label Border",
            chart => new ChartLayoutOptions(
                ShowDataLabels: true,
                DataLabelBorderColor: GetNextSeriesColor(chart.DataLabelBorderColor),
                DataLabelBorderThickness: chart.DataLabelBorderThickness >= 3 ? 0.75 : chart.DataLabelBorderThickness + 0.75));
    }

    private void ChartDataLabelSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleDataLabelOption(
            "Data Label Size",
            chart => new ChartLayoutOptions(
                ShowDataLabels: true,
                DataLabelFontSize: chart.DataLabelFontSize >= 16 ? 9 : chart.DataLabelFontSize + 1));
    }

    private void ChartDataLabelAngleBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleDataLabelOption(
            "Data Label Angle",
            chart => new ChartLayoutOptions(
                ShowDataLabels: true,
                DataLabelAngle: GetNextAxisLabelAngle(chart.DataLabelAngle)));
    }

    private void ChartPointDataLabelBtn_Click(object sender, RoutedEventArgs e)
    {
        const string caption = "Format Data Point Label";
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing point data-label formatting.",
                chart => GetChartSeriesCount(chart) > 0 && ChartTypeSupport.GetDataPointCount(chart) > 0,
                "Add chart data points before changing point data-label formatting.",
                chart =>
                {
                    var formats = chart.PointDataLabelFormats.ToList();
                    var existingIndex = formats.FindIndex(format => format.SeriesIndex == 0 && format.PointIndex == 0);
                    var current = existingIndex >= 0 ? formats[existingIndex] : new ChartPointDataLabelFormat(0, 0);
                    var updated = current with
                    {
                        FillColor = GetNextSeriesColor(current.FillColor),
                        BorderColor = GetNextSeriesColor(current.BorderColor ?? current.FillColor),
                        BorderThickness = current.BorderThickness is null or >= 3 ? 0.75 : current.BorderThickness.Value + 0.75,
                        TextColor = GetNextSeriesColor(current.TextColor),
                        FontSize = current.FontSize is null or >= 16 ? 9 : current.FontSize.Value + 1
                    };
                    if (existingIndex >= 0)
                        formats[existingIndex] = updated;
                    else
                        formats.Add(updated);
                    return new ChartLayoutOptions(
                        ShowDataLabels: true,
                        PointDataLabelFormats: formats);
                }))
            return;

        UpdateViewport();
    }

    private void ChartAreaFillBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Chart Area Fill",
            chart => new ChartLayoutOptions(ChartAreaFillColor: GetNextSeriesColor(chart.ChartAreaFillColor)));
    }

    private void ChartTitleColorBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Chart Title Color",
            chart => new ChartLayoutOptions(ChartTitleTextColor: GetNextSeriesColor(chart.ChartTitleTextColor)));
    }

    private void ChartTitleSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Chart Title Size",
            chart => new ChartLayoutOptions(ChartTitleFontSize: chart.ChartTitleFontSize >= 24 ? 12 : chart.ChartTitleFontSize + 2));
    }

    private void ChartAxisTitleColorBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Axis Title Color",
            chart => new ChartLayoutOptions(AxisTitleTextColor: GetNextSeriesColor(chart.AxisTitleTextColor)));
    }

    private void ChartAxisTitleSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Axis Title Size",
            chart => new ChartLayoutOptions(AxisTitleFontSize: chart.AxisTitleFontSize >= 18 ? 9 : chart.AxisTitleFontSize + 1));
    }

    private void ChartPlotAreaFillBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Plot Area Fill",
            chart => new ChartLayoutOptions(PlotAreaFillColor: GetNextSeriesColor(chart.PlotAreaFillColor)));
    }

    private void ChartPlotAreaBorderBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Plot Area Border",
            chart => new ChartLayoutOptions(
                PlotAreaBorderColor: GetNextSeriesColor(chart.PlotAreaBorderColor),
                PlotAreaBorderThickness: chart.PlotAreaBorderThickness >= 3 ? 1 : chart.PlotAreaBorderThickness + 0.75));
    }

    private void ChartLegendTextBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Legend Text",
            chart => new ChartLayoutOptions(LegendTextColor: GetNextSeriesColor(chart.LegendTextColor)));
    }

    private void ChartLegendFillBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Legend Fill",
            chart => new ChartLayoutOptions(LegendFillColor: GetNextSeriesColor(chart.LegendFillColor)));
    }

    private void ChartLegendBorderBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Legend Border",
            chart => new ChartLayoutOptions(
                LegendBorderColor: GetNextSeriesColor(chart.LegendBorderColor),
                LegendBorderThickness: chart.LegendBorderThickness >= 3 ? 0.75 : chart.LegendBorderThickness + 0.75));
    }

    private void ChartLegendSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Legend Font Size",
            chart => new ChartLayoutOptions(LegendFontSize: chart.LegendFontSize >= 16 ? 9 : chart.LegendFontSize + 1));
    }

    private void ChartLegendOverlayBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAreaOption(
            "Legend Overlay",
            chart => new ChartLayoutOptions(ShowLegend: true, LegendOverlay: !chart.LegendOverlay));
    }

    private static ChartDataLabelNumberFormat GetNextDataLabelNumberFormat(ChartDataLabelNumberFormat current) =>
        current switch
        {
            ChartDataLabelNumberFormat.General => ChartDataLabelNumberFormat.Number,
            ChartDataLabelNumberFormat.Number => ChartDataLabelNumberFormat.Currency,
            ChartDataLabelNumberFormat.Currency => ChartDataLabelNumberFormat.Percent,
            _ => ChartDataLabelNumberFormat.General
        };

    private void ToggleDataLabelOption(string caption, Func<ChartModel, ChartLayoutOptions> optionsFactory)
    {
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing data label options.",
                null,
                null,
                optionsFactory))
            return;

        UpdateViewport();
    }

    private void ToggleChartAreaOption(string caption, Func<ChartModel, ChartLayoutOptions> optionsFactory)
    {
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing chart area formatting.",
                null,
                null,
                optionsFactory))
            return;

        UpdateViewport();
    }

    private void ChartTrendlineBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteRepeatableChartLayout(
                "Trendline",
                "Insert or select a chart before changing trendlines.",
                chart => ChartTypeSupport.SupportsTrendlines(chart.Type),
                "Linear trendlines are currently supported for column, line, bar, scatter, bubble, and area charts.",
                chart => new ChartLayoutOptions(ShowLinearTrendline: !chart.ShowLinearTrendline)))
            return;

        UpdateViewport();
    }

    private void ChartTrendlineTypeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteRepeatableChartLayout(
                "Trendline Type",
                "Insert or select a chart before changing trendline type.",
                chart => ChartTypeSupport.SupportsTrendlines(chart.Type),
                "Trendlines are currently supported for column, line, bar, scatter, bubble, and area charts.",
                chart => new ChartLayoutOptions(
                    ShowLinearTrendline: true,
                    TrendlineType: GetNextTrendlineType(chart.TrendlineType))))
            return;

        UpdateViewport();
    }

    private static ChartTrendlineType GetNextTrendlineType(ChartTrendlineType current) =>
        current switch
        {
            ChartTrendlineType.Linear => ChartTrendlineType.Exponential,
            ChartTrendlineType.Exponential => ChartTrendlineType.Logarithmic,
            ChartTrendlineType.Logarithmic => ChartTrendlineType.Power,
            ChartTrendlineType.Power => ChartTrendlineType.MovingAverage,
            ChartTrendlineType.MovingAverage => ChartTrendlineType.Polynomial,
            _ => ChartTrendlineType.Linear
        };

    private void ChartTrendlinePeriodBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteRepeatableChartLayout(
                "Moving Average Period",
                "Insert or select a chart before changing moving-average period.",
                chart => ChartTypeSupport.SupportsTrendlines(chart.Type),
                "Trendlines are currently supported for column, line, bar, scatter, bubble, and area charts.",
                chart => new ChartLayoutOptions(
                    ShowLinearTrendline: true,
                    TrendlineType: ChartTrendlineType.MovingAverage,
                    TrendlinePeriod: chart.TrendlinePeriod >= 6 ? 2 : chart.TrendlinePeriod + 1)))
            return;

        UpdateViewport();
    }

    private void ChartTrendlineOrderBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteRepeatableChartLayout(
                "Polynomial Order",
                "Insert or select a chart before changing polynomial order.",
                chart => ChartTypeSupport.SupportsTrendlines(chart.Type),
                "Trendlines are currently supported for column, line, bar, scatter, bubble, and area charts.",
                chart => new ChartLayoutOptions(
                    ShowLinearTrendline: true,
                    TrendlineType: ChartTrendlineType.Polynomial,
                    TrendlineOrder: chart.TrendlineOrder >= 6 ? 2 : chart.TrendlineOrder + 1)))
            return;

        UpdateViewport();
    }

    private void ChartTrendlineEquationBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleTrendlineInfo(
            "Trendline Equation",
            chart => new ChartLayoutOptions(
                ShowLinearTrendline: true,
                ShowTrendlineEquation: !chart.ShowTrendlineEquation));
    }

    private void ChartTrendlineRSquaredBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleTrendlineInfo(
            "R-squared",
            chart => new ChartLayoutOptions(
                ShowLinearTrendline: true,
                ShowTrendlineRSquared: !chart.ShowTrendlineRSquared));
    }

    private void ChartTrendlineColorBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleTrendlineInfo(
            "Trendline Color",
            chart => new ChartLayoutOptions(
                ShowLinearTrendline: true,
                TrendlineColor: GetNextTrendlineColor(chart.TrendlineColor)));
    }

    private void ChartTrendlineDashBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleTrendlineInfo(
            "Trendline Dash",
            chart => new ChartLayoutOptions(
                ShowLinearTrendline: true,
                TrendlineDashStyle: chart.TrendlineDashStyle switch
                {
                    ChartLineDashStyle.Dash => ChartLineDashStyle.Dot,
                    ChartLineDashStyle.Dot => ChartLineDashStyle.Solid,
                    _ => ChartLineDashStyle.Dash
                }));
    }

    private void ChartTrendlineWidthBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleTrendlineInfo(
            "Trendline Width",
            chart => new ChartLayoutOptions(
                ShowLinearTrendline: true,
                TrendlineThickness: chart.TrendlineThickness >= 3 ? 1.5 : chart.TrendlineThickness + 0.75));
    }

    private static CellColor GetNextTrendlineColor(CellColor? current)
    {
        if (current is null)
            return new CellColor(217, 83, 25);
        if (current.Value.R == 217 && current.Value.G == 83 && current.Value.B == 25)
            return new CellColor(0, 114, 178);
        if (current.Value.R == 0 && current.Value.G == 114 && current.Value.B == 178)
            return new CellColor(0, 158, 115);
        return new CellColor(128, 128, 128);
    }

    private void ToggleTrendlineInfo(string caption, Func<ChartModel, ChartLayoutOptions> optionsFactory)
    {
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing trendline information.",
                chart => ChartTypeSupport.SupportsTrendlines(chart.Type),
                "Trendline information is currently supported for column, line, bar, scatter, bubble, and area charts.",
                optionsFactory))
            return;

        UpdateViewport();
    }

    private void ChartSecondaryAxisBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteRepeatableChartLayout(
                "Secondary Axis",
                "Insert or select a chart before changing secondary axes.",
                chart => ChartTypeSupport.SupportsSecondaryAxis(chart.Type) &&
                         (chart.ShowSecondaryAxis || GetChartSeriesCount(chart) >= 2),
                "Secondary value axes require a supported chart with at least two data series.",
                chart => new ChartLayoutOptions(
                    ShowSecondaryAxis: !chart.ShowSecondaryAxis,
                    SecondaryAxisSeriesIndexes: [])))
            return;

        UpdateViewport();
    }

    private void ChartXAxisBoundsBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisBounds(useXAxis: true);
    }

    private void ChartYAxisBoundsBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisBounds(useXAxis: false);
    }

    private void ChartXAxisLogBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisLogScale(useXAxis: true);
    }

    private void ChartYAxisLogBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisLogScale(useXAxis: false);
    }

    private void ChartXAxisNumberFormatBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisNumberFormat(useXAxis: true);
    }

    private void ChartYAxisNumberFormatBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisNumberFormat(useXAxis: false);
    }

    private void ChartXAxisGridlinesBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisGridlines(useXAxis: true);
    }

    private void ChartYAxisGridlinesBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisGridlines(useXAxis: false);
    }

    private void ChartXAxisGridlineStyleBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisGridlineStyle(useXAxis: true);
    }

    private void ChartYAxisGridlineStyleBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisGridlineStyle(useXAxis: false);
    }

    private void ChartXAxisTickBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisTicks(useXAxis: true);
    }

    private void ChartYAxisTickBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisTicks(useXAxis: false);
    }

    private void ChartXAxisLabelsBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisLabels(useXAxis: true);
    }

    private void ChartYAxisLabelsBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisLabels(useXAxis: false);
    }

    private void ChartXAxisLabelFontBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisLabelFont(useXAxis: true);
    }

    private void ChartXAxisLabelAngleBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisLabelAngle(useXAxis: true);
    }

    private void ChartYAxisLabelFontBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisLabelFont(useXAxis: false);
    }

    private void ChartYAxisLabelAngleBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisLabelAngle(useXAxis: false);
    }

    private void ChartXAxisLineBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisLine(useXAxis: true);
    }

    private void ChartYAxisLineBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleChartAxisLine(useXAxis: false);
    }

    private void ToggleChartAxisTicks(bool useXAxis)
    {
        var caption = useXAxis ? "X Axis Ticks" : "Y Axis Ticks";
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing axis ticks.",
                null,
                null,
                chart =>
                {
                    var (major, minor) = useXAxis
                        ? GetNextAxisTickState(chart.XAxisMajorTickStyle, chart.XAxisMinorTickStyle)
                        : GetNextAxisTickState(chart.YAxisMajorTickStyle, chart.YAxisMinorTickStyle);
                    return useXAxis
                        ? new ChartLayoutOptions(XAxisMajorTickStyle: major, XAxisMinorTickStyle: minor)
                        : new ChartLayoutOptions(YAxisMajorTickStyle: major, YAxisMinorTickStyle: minor);
                }))
            return;

        UpdateViewport();
    }

    private void ToggleChartAxisLabels(bool useXAxis)
    {
        var caption = useXAxis ? "X Axis Labels" : "Y Axis Labels";
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing axis labels.",
                null,
                null,
                chart => useXAxis
                    ? new ChartLayoutOptions(ShowXAxisLabels: !chart.ShowXAxisLabels)
                    : new ChartLayoutOptions(ShowYAxisLabels: !chart.ShowYAxisLabels)))
            return;

        UpdateViewport();
    }

    private void ToggleChartAxisLabelFont(bool useXAxis)
    {
        var caption = useXAxis ? "X Axis Label Font" : "Y Axis Label Font";
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing axis label formatting.",
                null,
                null,
                chart =>
                {
                    var currentColor = useXAxis ? chart.XAxisLabelTextColor : chart.YAxisLabelTextColor;
                    var currentSize = useXAxis ? chart.XAxisLabelFontSize : chart.YAxisLabelFontSize;
                    var nextColor = GetNextSeriesColor(currentColor);
                    var nextSize = currentSize >= 14 ? 9 : currentSize + 1;
                    return useXAxis
                        ? new ChartLayoutOptions(XAxisLabelTextColor: nextColor, XAxisLabelFontSize: nextSize)
                        : new ChartLayoutOptions(YAxisLabelTextColor: nextColor, YAxisLabelFontSize: nextSize);
                }))
            return;

        UpdateViewport();
    }

    private void ToggleChartAxisLabelAngle(bool useXAxis)
    {
        var caption = useXAxis ? "X Axis Label Angle" : "Y Axis Label Angle";
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing axis label rotation.",
                null,
                null,
                chart =>
                {
                    var currentAngle = useXAxis ? chart.XAxisLabelAngle : chart.YAxisLabelAngle;
                    var nextAngle = GetNextAxisLabelAngle(currentAngle);
                    return useXAxis
                        ? new ChartLayoutOptions(XAxisLabelAngle: nextAngle)
                        : new ChartLayoutOptions(YAxisLabelAngle: nextAngle);
                }))
            return;

        UpdateViewport();
    }

    private void ToggleChartAxisLine(bool useXAxis)
    {
        var caption = useXAxis ? "X Axis Line" : "Y Axis Line";
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing axis line formatting.",
                null,
                null,
                chart =>
                {
                    var currentColor = useXAxis ? chart.XAxisLineColor : chart.YAxisLineColor;
                    var currentThickness = useXAxis ? chart.XAxisLineThickness : chart.YAxisLineThickness;
                    var (nextColor, nextThickness) = GetNextAxisLineState(currentColor, currentThickness);
                    return useXAxis
                        ? new ChartLayoutOptions(XAxisLineColor: nextColor, XAxisLineThickness: nextThickness)
                        : new ChartLayoutOptions(YAxisLineColor: nextColor, YAxisLineThickness: nextThickness);
                }))
            return;

        UpdateViewport();
    }

    private static (ChartAxisTickStyle Major, ChartAxisTickStyle Minor) GetNextAxisTickState(
        ChartAxisTickStyle currentMajor,
        ChartAxisTickStyle currentMinor)
    {
        if (currentMajor == ChartAxisTickStyle.Outside && currentMinor == ChartAxisTickStyle.None)
            return (ChartAxisTickStyle.Inside, ChartAxisTickStyle.None);
        if (currentMajor == ChartAxisTickStyle.Inside && currentMinor == ChartAxisTickStyle.None)
            return (ChartAxisTickStyle.Cross, ChartAxisTickStyle.Inside);
        if (currentMajor == ChartAxisTickStyle.Cross)
            return (ChartAxisTickStyle.None, ChartAxisTickStyle.None);
        return (ChartAxisTickStyle.Outside, ChartAxisTickStyle.None);
    }

    private static double GetNextAxisLabelAngle(double currentAngle)
    {
        if (Math.Abs(currentAngle) < 0.5)
            return -45;
        if (currentAngle <= -44.5)
            return 45;
        if (currentAngle < 89.5)
            return 90;
        return 0;
    }

    private static (CellColor Color, double Thickness) GetNextAxisLineState(CellColor? currentColor, double currentThickness)
    {
        if (currentColor is null || currentThickness < 1.5)
            return (new CellColor(89, 89, 89), 1.5);
        if (currentThickness < 2.5)
            return (new CellColor(0, 114, 178), 2.5);
        if (currentThickness < 3.5)
            return (new CellColor(213, 94, 0), 3.5);
        return (new CellColor(89, 89, 89), 1);
    }

    private void ToggleChartAxisGridlines(bool useXAxis)
    {
        var caption = useXAxis ? "X Axis Gridlines" : "Y Axis Gridlines";
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing axis gridlines.",
                null,
                null,
                chart =>
                {
                    var (showMajor, showMinor) = useXAxis
                        ? GetNextGridlineState(chart.ShowXAxisMajorGridlines, chart.ShowXAxisMinorGridlines)
                        : GetNextGridlineState(chart.ShowYAxisMajorGridlines, chart.ShowYAxisMinorGridlines);
                    return useXAxis
                        ? new ChartLayoutOptions(ShowXAxisMajorGridlines: showMajor, ShowXAxisMinorGridlines: showMinor)
                        : new ChartLayoutOptions(ShowYAxisMajorGridlines: showMajor, ShowYAxisMinorGridlines: showMinor);
                }))
            return;

        UpdateViewport();
    }

    private static (bool ShowMajor, bool ShowMinor) GetNextGridlineState(bool currentMajor, bool currentMinor)
    {
        if (!currentMajor)
            return (true, false);
        if (!currentMinor)
            return (true, true);
        return (false, false);
    }

    private void ToggleChartAxisGridlineStyle(bool useXAxis)
    {
        var caption = useXAxis ? "X Gridline Style" : "Y Gridline Style";
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing gridline formatting.",
                null,
                null,
                chart =>
                {
                    var currentMajorColor = useXAxis ? chart.XAxisMajorGridlineColor : chart.YAxisMajorGridlineColor;
                    var currentMinorColor = useXAxis ? chart.XAxisMinorGridlineColor : chart.YAxisMinorGridlineColor;
                    var currentThickness = useXAxis ? chart.XAxisGridlineThickness : chart.YAxisGridlineThickness;
                    var nextMajorColor = GetNextSeriesColor(currentMajorColor);
                    var nextMinorColor = GetNextSeriesColor(currentMinorColor ?? currentMajorColor);
                    var nextThickness = currentThickness >= 3 ? 1 : currentThickness + 0.5;
                    return useXAxis
                        ? new ChartLayoutOptions(
                            XAxisMajorGridlineColor: nextMajorColor,
                            XAxisMinorGridlineColor: nextMinorColor,
                            XAxisGridlineThickness: nextThickness,
                            ShowXAxisMajorGridlines: true)
                        : new ChartLayoutOptions(
                            YAxisMajorGridlineColor: nextMajorColor,
                            YAxisMinorGridlineColor: nextMinorColor,
                            YAxisGridlineThickness: nextThickness,
                            ShowYAxisMajorGridlines: true);
                }))
            return;

        UpdateViewport();
    }

    private void ToggleChartAxisNumberFormat(bool useXAxis)
    {
        var caption = useXAxis ? "X Axis Number Format" : "Y Axis Number Format";
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing axis number formats.",
                null,
                null,
                chart =>
                {
                    var next = GetNextDataLabelNumberFormat(useXAxis ? chart.XAxisNumberFormat : chart.YAxisNumberFormat);
                    return useXAxis
                        ? new ChartLayoutOptions(XAxisNumberFormat: next)
                        : new ChartLayoutOptions(YAxisNumberFormat: next);
                }))
            return;

        UpdateViewport();
    }

    private void ToggleChartAxisLogScale(bool useXAxis)
    {
        var caption = useXAxis ? "X Log Scale" : "Y Log Scale";
        IWorkbookCommand CreateCommand()
        {
            var sheet = _workbook.GetSheet(_currentSheetId);
            var chart = sheet?.Charts.FirstOrDefault();
            if (sheet is null || chart is null)
                return new FailedWorkbookCommand("Insert or select a chart before changing axis scale.");

            if (useXAxis && !ChartTypeSupport.SupportsXAxisLogScale(chart.Type))
                return new FailedWorkbookCommand("X-axis log scale is currently supported for bar, scatter, and bubble charts with value X axes.");

            if (!useXAxis && !ChartTypeSupport.SupportsYAxisLogScale(chart.Type))
                return new FailedWorkbookCommand("Y-axis log scale is currently supported for column, line, area, scatter, and bubble charts with value Y axes.");

            var enableLog = useXAxis ? !chart.XAxisLogScale : !chart.YAxisLogScale;
            var options = useXAxis
                ? new ChartLayoutOptions(XAxisLogScale: enableLog)
                : new ChartLayoutOptions(YAxisLogScale: enableLog);

            if (enableLog && TryGetChartAxisBounds(sheet, chart, useXAxis, out var minimum, out var maximum))
            {
                var positiveMinimum = minimum > 0 ? minimum : 1;
                var positiveMaximum = maximum > positiveMinimum ? maximum : positiveMinimum * 10;
                options = useXAxis
                    ? options with { XAxisMinimum = positiveMinimum, XAxisMaximum = positiveMaximum }
                    : options with { YAxisMinimum = positiveMinimum, YAxisMaximum = positiveMaximum };
            }

            return new SetChartLayoutCommand(_currentSheetId, chart.Id, options);
        }

        var outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateCommand);
        if (!outcome.Success)
        {
            ShowCommandError(outcome, caption);
            return;
        }

        _repeatPostAction = null;
        UpdateViewport();
    }

    private void ToggleChartAxisBounds(bool useXAxis)
    {
        var caption = useXAxis ? "X Axis Bounds" : "Y Axis Bounds";
        IWorkbookCommand CreateCommand()
        {
            var sheet = _workbook.GetSheet(_currentSheetId);
            var chart = sheet?.Charts.FirstOrDefault();
            if (sheet is null || chart is null)
                return new FailedWorkbookCommand("Insert or select a chart before changing axis bounds.");

            var hasBounds = useXAxis
                ? chart.XAxisMinimum is not null || chart.XAxisMaximum is not null
                : chart.YAxisMinimum is not null || chart.YAxisMaximum is not null;
            if (!hasBounds &&
                (useXAxis
                    ? !ChartTypeSupport.SupportsXAxisBounds(chart.Type)
                    : !ChartTypeSupport.SupportsYAxisBounds(chart.Type)))
                return new FailedWorkbookCommand("Axis bounds are currently supported for chart value axes only.");

            ChartLayoutOptions options;
            if (hasBounds)
            {
                options = useXAxis
                    ? new ChartLayoutOptions(ClearXAxisBounds: true)
                    : new ChartLayoutOptions(ClearYAxisBounds: true);
            }
            else if (TryGetChartAxisBounds(sheet, chart, useXAxis, out var minimum, out var maximum))
            {
                var majorUnit = Math.Max(double.Epsilon, (maximum - minimum) / 5);
                var minorUnit = Math.Max(double.Epsilon, majorUnit / 2);
                options = useXAxis
                    ? new ChartLayoutOptions(XAxisMinimum: minimum, XAxisMaximum: maximum, XAxisMajorUnit: majorUnit, XAxisMinorUnit: minorUnit)
                    : new ChartLayoutOptions(YAxisMinimum: minimum, YAxisMaximum: maximum, YAxisMajorUnit: majorUnit, YAxisMinorUnit: minorUnit);
            }
            else
            {
                return new FailedWorkbookCommand("Add numeric chart data before setting axis bounds.");
            }

            return new SetChartLayoutCommand(_currentSheetId, chart.Id, options);
        }

        var outcome = _commandBus.ExecuteRepeatable(_workbook.Id, CreateCommand);
        if (!outcome.Success)
        {
            ShowCommandError(outcome, caption);
            return;
        }

        _repeatPostAction = null;
        UpdateViewport();
    }

    private static bool TryGetChartAxisBounds(Sheet sheet, ChartModel chart, bool useXAxis, out double minimum, out double maximum)
    {
        minimum = 0;
        maximum = 0;
        var values = new List<double>();
        var startRow = chart.FirstRowIsHeader ? chart.DataRange.Start.Row + 1 : chart.DataRange.Start.Row;
        if (startRow > chart.DataRange.End.Row)
            return false;

        if (useXAxis)
        {
            var xColumns = ChartTypeSupport.GetXAxisValueColumns(chart);
            foreach (var xColumn in xColumns)
            {
                for (var row = startRow; row <= chart.DataRange.End.Row; row++)
                {
                    if (sheet.GetValue(row, xColumn) is NumberValue number)
                        values.Add(number.Value);
                }
            }
        }
        else
        {
            var yColumns = ChartTypeSupport.GetYAxisValueColumns(chart);
            for (var row = startRow; row <= chart.DataRange.End.Row; row++)
            {
                foreach (var col in yColumns)
                {
                    if (sheet.GetValue(row, col) is NumberValue number)
                        values.Add(number.Value);
                }
            }
        }

        if (values.Count == 0)
            return false;

        minimum = values.Min();
        maximum = values.Max();
        if (Math.Abs(maximum - minimum) < double.Epsilon)
        {
            minimum -= 1;
            maximum += 1;
        }

        return true;
    }

    private void ChartSecondaryAxisSeriesBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteRepeatableChartLayout(
                "Secondary Axis Series",
                "Insert or select a chart before changing secondary-axis series.",
                chart => ChartTypeSupport.SupportsSecondaryAxis(chart.Type) && GetChartSeriesCount(chart) >= 2,
                "Secondary value axes require a supported chart with at least two data series.",
                chart =>
                {
                    var next = GetNextSecondaryAxisSeries(chart, GetChartSeriesCount(chart));
                    return new ChartLayoutOptions(
                        ShowSecondaryAxis: next.ShowSecondaryAxis,
                        SecondaryAxisSeriesIndexes: next.SeriesIndexes);
                }))
            return;

        UpdateViewport();
    }

    private static int GetChartSeriesCount(ChartModel chart)
    {
        return ChartTypeSupport.GetDataSeriesCount(chart);
    }

    private static (bool ShowSecondaryAxis, int[] SeriesIndexes) GetNextSecondaryAxisSeries(ChartModel chart, int seriesCount)
    {
        if (!chart.ShowSecondaryAxis)
            return (true, [1]);

        if (chart.SecondaryAxisSeriesIndexes.Count == 0)
            return (false, []);

        var current = chart.SecondaryAxisSeriesIndexes.Min();
        if (current + 1 < seriesCount)
            return (true, [current + 1]);

        return (true, []);
    }

    private static int[] GetNextComboLineSeries(ChartModel chart, int seriesCount)
    {
        if (!chart.UseComboLineForSecondarySeries || chart.ComboLineSeriesIndexes.Count == 0)
            return [1];

        var current = chart.ComboLineSeriesIndexes.Min();
        if (current + 1 < seriesCount)
            return [current + 1];

        return [];
    }

    private void ChartComboBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteRepeatableChartLayout(
                "Combo Chart",
                "Insert or select a chart before changing combo chart options.",
                chart => ChartTypeSupport.SupportsComboLineOverlay(chart.Type) &&
                         (chart.UseComboLineForSecondarySeries || ChartTypeSupport.SupportsComboLineOverlay(chart)),
                "Combo line overlays require a supported chart with at least two data series.",
                chart => new ChartLayoutOptions(
                    UseComboLineForSecondarySeries: !chart.UseComboLineForSecondarySeries,
                    ComboLineSeriesIndexes: !chart.UseComboLineForSecondarySeries ? chart.ComboLineSeriesIndexes : [])))
            return;

        UpdateViewport();
    }

    private void ChartComboSeriesBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteRepeatableChartLayout(
                "Combo Chart Series",
                "Insert or select a chart before changing combo chart series.",
                chart => ChartTypeSupport.SupportsComboLineOverlay(chart.Type) && ChartTypeSupport.SupportsComboLineOverlay(chart),
                "Combo line overlays require a supported chart with at least two data series.",
                chart =>
                {
                    var nextIndexes = GetNextComboLineSeries(chart, GetChartSeriesCount(chart));
                    return new ChartLayoutOptions(
                        UseComboLineForSecondarySeries: nextIndexes.Length > 0,
                        ComboLineSeriesIndexes: nextIndexes);
                }))
            return;

        UpdateViewport();
    }

    private void ChartSeriesColorBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleSeriesFormat(
            "Series Color",
            format => format with
            {
                FillColor = GetNextSeriesColor(format.FillColor),
                StrokeColor = GetNextSeriesColor(format.StrokeColor ?? format.FillColor)
            });
    }

    private void ChartSeriesWidthBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleSeriesFormat(
            "Series Width",
            format => format with
            {
                StrokeThickness = format.StrokeThickness is null or >= 4 ? 1.5 : format.StrokeThickness.Value + 0.75
            });
    }

    private void ChartSeriesDashBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleSeriesFormat(
            "Series Dash",
            format => format with
            {
                DashStyle = format.DashStyle switch
                {
                    null => ChartLineDashStyle.Dash,
                    ChartLineDashStyle.Dash => ChartLineDashStyle.Dot,
                    ChartLineDashStyle.Dot => ChartLineDashStyle.Solid,
                    _ => null
                }
            });
    }

    private void ChartSeriesMarkerBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleSeriesFormat(
            "Series Marker",
            format => format with
            {
                MarkerStyle = format.MarkerStyle switch
                {
                    null => ChartMarkerStyle.Circle,
                    ChartMarkerStyle.Circle => ChartMarkerStyle.Square,
                    ChartMarkerStyle.Square => ChartMarkerStyle.Diamond,
                    ChartMarkerStyle.Diamond => ChartMarkerStyle.Triangle,
                    ChartMarkerStyle.Triangle => ChartMarkerStyle.None,
                    _ => null
                }
            },
            chart => ChartTypeSupport.SupportsSeriesMarkers(chart.Type),
            "Series marker shape and size are currently supported for line and scatter charts.");
    }

    private void ChartSeriesMarkerSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleSeriesFormat(
            "Marker Size",
            format => format with
            {
                MarkerSize = format.MarkerSize is null or >= 12 ? 5 : format.MarkerSize.Value + 2
            },
            chart => ChartTypeSupport.SupportsSeriesMarkers(chart.Type),
            "Series marker shape and size are currently supported for line and scatter charts.");
    }

    private void ToggleSeriesFormat(
        string caption,
        Func<ChartSeriesFormat, ChartSeriesFormat> update,
        Func<ChartModel, bool>? canApply = null,
        string? unsupportedMessage = null)
    {
        if (!TryExecuteRepeatableChartLayout(
                caption,
                "Insert or select a chart before changing series formatting.",
                chart => GetChartSeriesCount(chart) > 0 && (canApply?.Invoke(chart) ?? true),
                unsupportedMessage ?? "Add data series before changing series formatting.",
                chart =>
                {
                    var formats = chart.SeriesFormats.ToList();
                    var existingIndex = formats.FindIndex(format => format.SeriesIndex == 0);
                    var current = existingIndex >= 0 ? formats[existingIndex] : new ChartSeriesFormat(0);
                    var updated = update(current);
                    if (existingIndex >= 0)
                        formats[existingIndex] = updated;
                    else
                        formats.Add(updated);
                    return new ChartLayoutOptions(SeriesFormats: formats);
                }))
            return;

        UpdateViewport();
    }

    private static CellColor GetNextSeriesColor(CellColor? current)
    {
        if (current is null)
            return new CellColor(0, 114, 178);
        if (current.Value.R == 0 && current.Value.G == 114 && current.Value.B == 178)
            return new CellColor(213, 94, 0);
        if (current.Value.R == 213 && current.Value.G == 94 && current.Value.B == 0)
            return new CellColor(0, 158, 115);
        return new CellColor(0, 114, 178);
    }

    private void InsertChartOfType(string type)
    {
        InsertChartOfType(ParseChartType(type));
    }

    private static ChartType ParseChartType(string type)
    {
        var normalized = type.Trim().ToLowerInvariant();
        return normalized switch
        {
            "line" => ChartType.Line,
            "pie" => ChartType.Pie,
            "doughnut" or "donut" => ChartType.Doughnut,
            "bar" => ChartType.Bar,
            "stackedbar" or "stacked bar" => ChartType.StackedBar,
            "percentstackedbar" or "100% stacked bar" or "100%stackedbar" => ChartType.PercentStackedBar,
            "stackedcolumn" or "stacked column" => ChartType.StackedColumn,
            "percentstackedcolumn" or "100% stacked column" or "100%stackedcolumn" => ChartType.PercentStackedColumn,
            "scatter" => ChartType.Scatter,
            "bubble" => ChartType.Bubble,
            "area" => ChartType.Area,
            "radar" => ChartType.Radar,
            "stock" => ChartType.Stock,
            _ => ChartType.Column
        };
    }

    private void SparklineLineBtn_Click(object sender, RoutedEventArgs e)    => InsertSparkline("line");
    private void SparklineColumnBtn_Click(object sender, RoutedEventArgs e)  => InsertSparkline("column");
    private void SparklineWinLossBtn_Click(object sender, RoutedEventArgs e) => InsertSparkline("winloss");

    private void InsertSparkline(string type)
    {
        var selected = SheetGrid.SelectedRange;
        var rangeInput = PromptForInput("Data range (e.g. A1:E1):", selected?.ToString() ?? "");
        if (rangeInput is null) return;
        var targetInput = PromptForInput("Location cell (e.g. F1):", "");
        if (targetInput is null) return;

        GridRange dataRange;
        try
        {
            dataRange = GridRange.Parse(rangeInput, _currentSheetId);
        }
        catch
        {
            MessageBox.Show("Invalid data range.", "Insert Sparkline", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!CellAddress.TryParse(targetInput, _currentSheetId, out var location))
        {
            MessageBox.Show("Invalid location cell.", "Insert Sparkline", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var kind = type switch
        {
            "column" => SparklineKind.Column,
            "winloss" => SparklineKind.WinLoss,
            _ => SparklineKind.Line
        };

        var fallbackLocationRange = new GridRange(location, location);
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Insert Sparkline",
                fallbackLocationRange,
                currentRange => new AddSparklineCommand(_currentSheetId, dataRange, currentRange.Start, kind)))
            return;

        SetActiveCell(location);
        EnsureCellVisible(location);
        UpdateViewport();
    }

    private static IReadOnlyDictionary<Guid, IReadOnlyList<double>> BuildSparklineValues(Sheet sheet)
    {
        var values = new Dictionary<Guid, IReadOnlyList<double>>();
        foreach (var sparkline in sheet.Sparklines)
        {
            var series = new List<double>();
            for (var row = sparkline.DataRange.Start.Row; row <= sparkline.DataRange.End.Row; row++)
            {
                for (var col = sparkline.DataRange.Start.Col; col <= sparkline.DataRange.End.Col; col++)
                {
                    switch (sheet.GetValue(row, col))
                    {
                        case NumberValue number:
                            series.Add(number.Value);
                            break;
                        case DateTimeValue date:
                            series.Add(date.Value);
                            break;
                        case BoolValue boolean:
                            series.Add(boolean.Value ? 1 : 0);
                            break;
                    }
                }
            }

            values[sparkline.Id] = series;
        }

        return values;
    }

    private void InsertLinkBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is null) return;
        var url = PromptForInput("URL:", "https://");
        if (url is null) return;
        var label = PromptForInput("Display text (leave blank to use URL):", "");
        var text = string.IsNullOrWhiteSpace(label) ? url : label;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Insert Link",
                SheetGrid.SelectedRange.Value,
                currentRange => new SetHyperlinkCommand(_currentSheetId, currentRange.Start, url, text)))
            return;
        UpdateViewport();
    }

    private void InsertCommentBtn_Click(object sender, RoutedEventArgs e)    => ReviewNewCommentBtn_Click(sender, e);
    private void TextBoxBtn_Click(object sender, RoutedEventArgs e)
    {
        InsertTextBox();
    }
    private void InsertPictureBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Insert Picture",
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;

        byte[] bytes;
        try
        {
            bytes = System.IO.File.ReadAllBytes(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not read picture file:\n{ex.Message}",
                "Insert Picture", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var contentType = DrawingInputParser.GetImageContentType(dialog.FileName);
        if (!TryExecuteGroupedSheetCommand(
                "Insert Picture",
                sheetId => new InsertPictureCommand(
                    sheetId,
                    new CellAddress(sheetId, range.Start.Row, range.Start.Col),
                    bytes,
                    contentType)))
            return;

        UpdateViewport();
    }

    private void PictureSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        var picture = GetTargetPicture(_currentSheetId);
        if (picture is null)
        {
            MessageBox.Show("No picture found on this sheet.", "Picture Size", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var input = PromptForInput("Picture size (width x height):", $"{(int)picture.Width}x{(int)picture.Height}");
        if (input is null) return;
        var parts = input.Split('x', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 ||
            !double.TryParse(parts[0], out var width) ||
            !double.TryParse(parts[1], out var height))
        {
            MessageBox.Show("Enter size as width x height, for example 320x180.",
                "Picture Size", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Picture Size",
                sheetId => new ResizePictureCommand(sheetId, GetTargetPicture(sheetId)?.Id ?? Guid.Empty, width, height)))
            return;

        UpdateViewport();
    }

    private void PictureRotateBtn_Click(object sender, RoutedEventArgs e)
    {
        var picture = GetTargetPicture(_currentSheetId);
        if (picture is null)
        {
            MessageBox.Show("No picture found on this sheet.", "Rotate Picture", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var input = PromptForInput("Rotation degrees:", ((int)picture.RotationDegrees).ToString());
        if (input is null) return;
        if (!double.TryParse(input, out var rotation))
        {
            MessageBox.Show("Enter a numeric rotation in degrees.",
                "Rotate Picture", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Rotate Picture",
                sheetId => new RotatePictureCommand(sheetId, GetTargetPicture(sheetId)?.Id ?? Guid.Empty, rotation)))
            return;

        UpdateViewport();
    }

    private void PictureCropBtn_Click(object sender, RoutedEventArgs e)
    {
        var picture = GetTargetPicture(_currentSheetId);
        if (picture is null)
        {
            MessageBox.Show("No picture found on this sheet.", "Crop Picture", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (picture.Kind != PictureKind.Image)
        {
            MessageBox.Show("Only inserted image pictures can be cropped.", "Crop Picture", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var defaultText = string.Join(", ",
            DrawingInputParser.FormatCropPercent(picture.CropLeft),
            DrawingInputParser.FormatCropPercent(picture.CropTop),
            DrawingInputParser.FormatCropPercent(picture.CropRight),
            DrawingInputParser.FormatCropPercent(picture.CropBottom));
        var input = PromptForInput("Crop percentages (left, top, right, bottom). Use 0,0,0,0 to reset:", defaultText);
        if (input is null) return;

        var parts = input.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 ||
            !DrawingInputParser.TryParseCropPercent(parts[0], out var left) ||
            !DrawingInputParser.TryParseCropPercent(parts[1], out var top) ||
            !DrawingInputParser.TryParseCropPercent(parts[2], out var right) ||
            !DrawingInputParser.TryParseCropPercent(parts[3], out var bottom) ||
            left + right >= 1 ||
            top + bottom >= 1)
        {
            MessageBox.Show("Enter four percentages between 0 and 99, keeping visible width and height. Example: 10, 0, 10, 0.",
                "Crop Picture", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Crop Picture",
                sheetId => new SetPictureCropCommand(sheetId, GetTargetPicture(sheetId)?.Id ?? Guid.Empty, left, top, right, bottom)))
            return;

        UpdateViewport();
    }

    private PictureModel? GetTargetPicture(SheetId sheetId)
    {
        var sheet = _workbook.GetSheet(sheetId);
        if (sheet is null || sheet.Pictures.Count == 0)
            return null;

        if (SheetGrid.SelectedRange is { } range)
        {
            var anchored = sheet.Pictures.LastOrDefault(p =>
                p.Anchor.Row == range.Start.Row &&
                p.Anchor.Col == range.Start.Col);
            if (anchored is not null)
                return anchored;
        }

        return sheet.Pictures[^1];
    }

    private void HeaderFooterBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        var dialog = new HeaderFooterDialog(sheet) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!TryExecuteGroupedSheetCommand(
                "Header & Footer",
                sheetId => new SetHeaderFooterCommand(
                    sheetId,
                    dialog.Header,
                    dialog.Footer,
                    dialog.FirstPageHeader,
                    dialog.FirstPageFooter,
                    dialog.EvenPageHeader,
                    dialog.EvenPageFooter,
                    dialog.DifferentFirstPage,
                    dialog.DifferentOddEvenPages,
                    dialog.ScaleWithDocument,
                    dialog.AlignWithMargins)))
            return;

        UpdateViewport();
        RefreshStatusBar();
    }
    private void SymbolPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SymbolPickerDialog { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedChar == '\0') return;
        if (SheetGrid.SelectedRange is null) return;
        var selectedChar = dlg.SelectedChar;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Insert Symbol",
                SheetGrid.SelectedRange.Value,
                currentRange =>
                {
                    var currentAddress = currentRange.Start;
                    var currentSheet = _workbook.GetSheet(_currentSheetId);
                    var currentExisting = currentSheet?.GetCell(currentAddress)?.Value as TextValue;
                    var currentText = (currentExisting?.Value ?? "") + selectedChar;
                    return CreateSingleCellEditCommand(currentAddress, Cell.FromValue(new TextValue(currentText)));
                }))
            return;
        UpdateViewport();
    }

    private IWorkbookCommand CreateSingleCellEditCommand(CellAddress address, Cell cell)
    {
        var edits = new List<(CellAddress Address, Cell NewCell)> { (address, cell) };
        var targetSheetIds = CurrentGroupedEditSheetIds();
        return targetSheetIds.Count > 1
            ? new GroupedEditCellsCommand(targetSheetIds, _currentSheetId, edits)
            : new EditCellsCommand(_currentSheetId, edits);
    }

    // ── Draw tab stubs ────────────────────────────────────────────────────────

    private void DrawRectBtn_Click(object sender, RoutedEventArgs e)    => InsertDrawingShape(DrawingShapeKind.Rectangle);
    private void DrawEllipseBtn_Click(object sender, RoutedEventArgs e) => InsertDrawingShape(DrawingShapeKind.Ellipse);
    private void DrawLineBtn_Click(object sender, RoutedEventArgs e)    => InsertDrawingShape(DrawingShapeKind.Line);
    private void DrawTextBtn_Click(object sender, RoutedEventArgs e)    => InsertTextBox();
    private void BringForwardBtn_Click(object sender, RoutedEventArgs e) => ReorderSelectedDrawingShape(forward: true);
    private void SendBackwardBtn_Click(object sender, RoutedEventArgs e) => ReorderSelectedDrawingShape(forward: false);
    private void ObjectSizeBtn_Click(object sender, RoutedEventArgs e) => ResizeSelectedDrawingObject();
    private void ObjectRotateBtn_Click(object sender, RoutedEventArgs e) => RotateSelectedDrawingObject();
    private void ObjectFillBtn_Click(object sender, RoutedEventArgs e) => SetSelectedDrawingObjectColor(isFill: true);
    private void ObjectOutlineBtn_Click(object sender, RoutedEventArgs e) => SetSelectedDrawingObjectColor(isFill: false);
    private void ObjectGradientBtn_Click(object sender, RoutedEventArgs e) => SetSelectedDrawingShapeGradient();
    private void ObjectEffectsBtn_Click(object sender, RoutedEventArgs e) => ToggleSelectedDrawingShapeEffect();

    // ── Page Layout tab ───────────────────────────────────────────────────────

    private void InsertTextBox()
    {
        var anchor = SheetGrid.SelectedRange?.Start ?? new CellAddress(_currentSheetId, 1, 1);
        var text = PromptForInput("Text box text:", "");
        if (text is null) return;

        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Insert Text Box",
                sheetId =>
                {
                    var currentAnchor = SheetGrid.SelectedRange?.Start ?? anchor;
                    return new AddTextBoxCommand(sheetId, new CellAddress(sheetId, currentAnchor.Row, currentAnchor.Col), text);
                }))
            return;

        SetActiveCell(anchor);
        EnsureCellVisible(anchor);
        UpdateViewport();
    }

    private void InsertDrawingShape(DrawingShapeKind kind)
    {
        var anchor = SheetGrid.SelectedRange?.Start ?? new CellAddress(_currentSheetId, 1, 1);
        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Insert Shape",
                sheetId =>
                {
                    var currentAnchor = SheetGrid.SelectedRange?.Start ?? anchor;
                    return new AddDrawingShapeCommand(sheetId, new CellAddress(sheetId, currentAnchor.Row, currentAnchor.Col), kind);
                }))
            return;

        SetActiveCell(anchor);
        EnsureCellVisible(anchor);
        UpdateViewport();
    }

    private void ReorderSelectedDrawingShape(bool forward)
    {
        var currentShape = GetTargetDrawingShape(_currentSheetId);
        if (currentShape is null)
        {
            MessageBox.Show("No drawing shapes are available on this sheet.",
                "Draw", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var title = forward ? "Bring Forward" : "Send Backward";
        if (!TryExecuteRepeatableGroupedSheetCommand(
                title,
                sheetId =>
                {
                    var target = GetTargetDrawingShape(sheetId);
                    return forward
                        ? new BringDrawingShapeForwardCommand(sheetId, target?.Id ?? Guid.Empty)
                        : new SendDrawingShapeBackwardCommand(sheetId, target?.Id ?? Guid.Empty);
                }))
            return;

        SetActiveCell(currentShape.Anchor);
        EnsureCellVisible(currentShape.Anchor);
        UpdateViewport();
    }

    private void ResizeSelectedDrawingObject()
    {
        var target = GetTargetDrawingObject(_currentSheetId);
        if (target is null)
        {
            MessageBox.Show("No drawing object found on this sheet.", "Object Size", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var input = PromptForInput("Object size (width x height):", $"{(int)target.Width}x{(int)target.Height}");
        if (input is null) return;
        var parts = input.Split('x', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 ||
            !double.TryParse(parts[0], out var width) ||
            !double.TryParse(parts[1], out var height))
        {
            MessageBox.Show("Enter size as width x height, for example 160x90.",
                "Object Size", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Object Size",
                sheetId =>
                {
                    var groupedTarget = GetTargetDrawingObject(sheetId, target.Kind);
                    return target.Kind == DrawingObjectTargetKind.Shape
                        ? new ResizeDrawingShapeCommand(sheetId, groupedTarget?.Id ?? Guid.Empty, width, height)
                        : new ResizeTextBoxCommand(sheetId, groupedTarget?.Id ?? Guid.Empty, width, height);
                }))
            return;

        SetActiveCell(target.Anchor);
        EnsureCellVisible(target.Anchor);
        UpdateViewport();
    }

    private void RotateSelectedDrawingObject()
    {
        var target = GetTargetDrawingObject(_currentSheetId);
        if (target is null)
        {
            MessageBox.Show("No drawing object found on this sheet.", "Rotate Object", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var input = PromptForInput("Rotation degrees:", ((int)target.RotationDegrees).ToString());
        if (input is null) return;
        if (!double.TryParse(input, out var rotation))
        {
            MessageBox.Show("Enter a numeric rotation in degrees.",
                "Rotate Object", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Rotate Object",
                sheetId =>
                {
                    var groupedTarget = GetTargetDrawingObject(sheetId, target.Kind);
                    return target.Kind == DrawingObjectTargetKind.Shape
                        ? new RotateDrawingShapeCommand(sheetId, groupedTarget?.Id ?? Guid.Empty, rotation)
                        : new RotateTextBoxCommand(sheetId, groupedTarget?.Id ?? Guid.Empty, rotation);
                }))
            return;

        SetActiveCell(target.Anchor);
        EnsureCellVisible(target.Anchor);
        UpdateViewport();
    }

    private void SetSelectedDrawingObjectColor(bool isFill)
    {
        var target = GetTargetDrawingObject(_currentSheetId);
        if (target is null)
        {
            MessageBox.Show("No drawing object found on this sheet.",
                isFill ? "Object Fill" : "Object Outline",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var input = PromptForInput(
            isFill ? "Object fill color (R,G,B):" : "Object outline color (R,G,B):",
            isFill ? "31,119,180" : "68,68,68");
        if (input is null) return;
        if (!DrawingInputParser.TryParseRgbColor(input, out var color))
        {
            MessageBox.Show("Enter a color as R,G,B, for example 31,119,180.",
                isFill ? "Object Fill" : "Object Outline",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!TryExecuteRepeatableGroupedSheetCommand(
                isFill ? "Object Fill" : "Object Outline",
                sheetId =>
                {
                    var groupedTarget = GetTargetDrawingObject(sheetId, target.Kind);
                    if (target.Kind == DrawingObjectTargetKind.Shape)
                    {
                        return new SetDrawingShapeColorsCommand(
                            sheetId,
                            groupedTarget?.Id ?? Guid.Empty,
                            isFill ? color : groupedTarget?.FillColor,
                            isFill ? groupedTarget?.OutlineColor : color);
                    }

                    return new SetTextBoxColorsCommand(
                        sheetId,
                        groupedTarget?.Id ?? Guid.Empty,
                        isFill ? color : groupedTarget?.FillColor,
                        isFill ? groupedTarget?.OutlineColor : color);
                }))
            return;

        SetActiveCell(target.Anchor);
        EnsureCellVisible(target.Anchor);
        UpdateViewport();
    }

    private void SetSelectedDrawingShapeGradient()
    {
        var shape = GetTargetDrawingShape(_currentSheetId);
        if (shape is null)
        {
            MessageBox.Show("No drawing shape found on this sheet.", "Shape Gradient", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var input = PromptForInput("Gradient colors (start R,G,B; end R,G,B):", "31,119,180; 180,210,240");
        if (input is null) return;
        var parts = input.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 ||
            !DrawingInputParser.TryParseRgbColor(parts[0], out var startColor) ||
            !DrawingInputParser.TryParseRgbColor(parts[1], out var endColor))
        {
            MessageBox.Show("Enter two colors separated by a semicolon, for example 31,119,180; 180,210,240.",
                "Shape Gradient", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Shape Gradient",
                sheetId => new SetDrawingShapeGradientCommand(sheetId, GetTargetDrawingShape(sheetId)?.Id ?? Guid.Empty, startColor, endColor)))
            return;

        SetActiveCell(shape.Anchor);
        EnsureCellVisible(shape.Anchor);
        UpdateViewport();
    }

    private void ToggleSelectedDrawingShapeEffect()
    {
        var shape = GetTargetDrawingShape(_currentSheetId);
        if (shape is null)
        {
            MessageBox.Show("No drawing shape found on this sheet.", "Shape Effects", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var enableShadow = !shape.HasShadowEffect;
        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Shape Effects",
                sheetId => new SetDrawingShapeEffectCommand(sheetId, GetTargetDrawingShape(sheetId)?.Id ?? Guid.Empty, enableShadow)))
            return;

        SetActiveCell(shape.Anchor);
        EnsureCellVisible(shape.Anchor);
        UpdateViewport();
    }

    private DrawingShapeModel? GetTargetDrawingShape(SheetId sheetId)
    {
        var sheet = _workbook.GetSheet(sheetId);
        if (sheet is null || sheet.DrawingShapes.Count == 0)
            return null;

        if (SheetGrid.SelectedRange is { } range)
        {
            var anchored = sheet.DrawingShapes.LastOrDefault(item =>
                item.Anchor.Row == range.Start.Row &&
                item.Anchor.Col == range.Start.Col);
            if (anchored is not null)
                return anchored;
        }

        return sheet.DrawingShapes[^1];
    }

    private DrawingObjectTarget? GetTargetDrawingObject(
        SheetId sheetId,
        DrawingObjectTargetKind? preferredKind = null)
    {
        var sheet = _workbook.GetSheet(sheetId);
        if (sheet is null)
            return null;

        if (preferredKind is null or DrawingObjectTargetKind.Shape && TryGetTargetShape(sheet, out var shape))
            return new DrawingObjectTarget(DrawingObjectTargetKind.Shape, shape.Id, shape.Anchor, shape.Width, shape.Height, shape.RotationDegrees, shape.FillColor, shape.OutlineColor);

        if (preferredKind is null or DrawingObjectTargetKind.TextBox && TryGetTargetTextBox(sheet, out var textBox))
            return new DrawingObjectTarget(DrawingObjectTargetKind.TextBox, textBox.Id, textBox.Anchor, textBox.Width, textBox.Height, textBox.RotationDegrees, textBox.FillColor, textBox.OutlineColor);

        return null;
    }

    private bool TryGetTargetShape(Sheet sheet, out DrawingShapeModel shape)
    {
        if (SheetGrid.SelectedRange is { } range)
        {
            var anchored = sheet.DrawingShapes.LastOrDefault(item =>
                item.Anchor.Row == range.Start.Row &&
                item.Anchor.Col == range.Start.Col);
            if (anchored is not null)
            {
                shape = anchored;
                return true;
            }
        }

        if (sheet.DrawingShapes.Count > 0)
        {
            shape = sheet.DrawingShapes[^1];
            return true;
        }

        shape = null!;
        return false;
    }

    private bool TryGetTargetTextBox(Sheet sheet, out TextBoxModel textBox)
    {
        if (SheetGrid.SelectedRange is { } range)
        {
            var anchored = sheet.TextBoxes.LastOrDefault(item =>
                item.Anchor.Row == range.Start.Row &&
                item.Anchor.Col == range.Start.Col);
            if (anchored is not null)
            {
                textBox = anchored;
                return true;
            }
        }

        if (sheet.TextBoxes.Count > 0)
        {
            textBox = sheet.TextBoxes[^1];
            return true;
        }

        textBox = null!;
        return false;
    }

    private enum DrawingObjectTargetKind
    {
        Shape,
        TextBox
    }

    private sealed record DrawingObjectTarget(
        DrawingObjectTargetKind Kind,
        Guid Id,
        CellAddress Anchor,
        double Width,
        double Height,
        double RotationDegrees,
        CellColor? FillColor,
        CellColor? OutlineColor);

    private enum RibbonKeyTipScope
    {
        None,
        TopLevel,
        Commands,
        Menu
    }

    private void PageLayoutDeferredBtn_Click(object sender, RoutedEventArgs e)
    {
        var commandName = (sender as System.Windows.Controls.Button)?.Content?.ToString() ?? "This command";
        var message = DeferredCommandMessages.WorkbookTheme(commandName);
        MessageBox.Show(
            message.Body,
            message.Title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ThemeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }

    private void ThemeOfficeMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyWorkbookTheme(WorkbookTheme.Office);

    private void ThemeColorfulMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyWorkbookTheme(WorkbookThemeWorkflow.CreateColorfulTheme());

    private void ThemeGrayscaleMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyWorkbookTheme(WorkbookThemeWorkflow.CreateGrayscaleTheme());

    private void ThemeCustomizeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WorkbookThemeDialog(_workbook.Theme) { Owner = this };
        if (dialog.ShowDialog() == true)
            ApplyWorkbookTheme(dialog.ResultTheme);
    }

    private void ThemeColorsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }

    private void ThemeColorsOfficeMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyWorkbookTheme(WorkbookThemeWorkflow.ApplyOfficeColors(_workbook.Theme).WithName(_workbook.Theme.Name));

    private void ThemeColorsColorfulMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyWorkbookTheme(WorkbookThemeWorkflow.ApplyColorfulColors(_workbook.Theme).WithName(_workbook.Theme.Name));

    private void ThemeColorsGrayscaleMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyWorkbookTheme(WorkbookThemeWorkflow.ApplyGrayscaleColors(_workbook.Theme).WithName(_workbook.Theme.Name));

    private void ThemeFontsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }

    private void ThemeFontsOfficeMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyWorkbookTheme(_workbook.Theme.WithFonts(WorkbookTheme.Office.MajorFontName, WorkbookTheme.Office.MinorFontName));

    private void ThemeFontsArialMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyWorkbookTheme(_workbook.Theme.WithFonts("Arial", "Arial"));

    private void ThemeFontsTimesMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyWorkbookTheme(_workbook.Theme.WithFonts("Times New Roman", "Times New Roman"));

    private void ThemeEffectsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }

    private void ThemeEffectsOfficeMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyWorkbookTheme(_workbook.Theme.WithEffects(WorkbookTheme.Office.EffectsName));

    private void ThemeEffectsSubtleMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyWorkbookTheme(_workbook.Theme.WithEffects("Subtle"));

    private void ThemeEffectsRefinedMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyWorkbookTheme(_workbook.Theme.WithEffects("Refined"));

    private void ApplyWorkbookTheme(WorkbookTheme theme)
    {
        if (!TryExecuteCommand(new SetWorkbookThemeCommand(theme), "Themes"))
            return;

        UpdateViewport();
    }

    private void BackgroundBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }

    private void BackgroundChooseMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Sheet Background",
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(dialog.FileName);
        }
        catch (IOException ex)
        {
            MessageBox.Show($"Could not read the selected image: {ex.Message}", "Sheet Background", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show($"Could not read the selected image: {ex.Message}", "Sheet Background", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var background = new WorksheetBackgroundImage(
            bytes,
            DrawingInputParser.GetImageContentType(dialog.FileName),
            Path.GetFileName(dialog.FileName));

        if (!TryExecuteGroupedSheetCommand("Sheet Background", sheetId => new SetWorksheetBackgroundCommand(sheetId, background)))
            return;

        UpdateViewport();
    }

    private void BackgroundClearMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteGroupedSheetCommand("Clear Sheet Background", sheetId => new ClearWorksheetBackgroundCommand(sheetId)))
            return;

        UpdateViewport();
    }

    private void PageMarginsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void MarginNormalMenuItem_Click(object sender, RoutedEventArgs e)
    {
        TryExecuteGroupedSheetCommand("Page Margins", sheetId => new SetPageMarginsCommand(sheetId, WorksheetPageMargins.Normal));
    }

    private void MarginWideMenuItem_Click(object sender, RoutedEventArgs e)
    {
        TryExecuteGroupedSheetCommand("Page Margins", sheetId => new SetPageMarginsCommand(sheetId, WorksheetPageMargins.Wide));
    }

    private void MarginNarrowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        TryExecuteGroupedSheetCommand("Page Margins", sheetId => new SetPageMarginsCommand(sheetId, WorksheetPageMargins.Narrow));
    }

    private void MarginCustomMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        var current = sheet.PageMargins;
        var defaultValue = string.Join(", ",
            current.Left.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
            current.Right.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
            current.Top.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
            current.Bottom.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));

        var input = PromptForInput("Custom margins in inches (left, right, top, bottom):", defaultValue);
        if (input is null) return;

        if (!PageMarginInputParser.TryParse(input, out var margins, out var error))
        {
            MessageBox.Show(error, "Page Margins", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TryExecuteGroupedSheetCommand("Page Margins", sheetId => new SetPageMarginsCommand(sheetId, margins));
    }

    private void PageOrientBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void OrientPortraitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        TryExecuteGroupedSheetCommand(
            "Orientation",
            sheetId => new SetPageOrientationCommand(sheetId, WorksheetPageOrientation.Portrait));
    }

    private void OrientLandscapeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        TryExecuteGroupedSheetCommand(
            "Orientation",
            sheetId => new SetPageOrientationCommand(sheetId, WorksheetPageOrientation.Landscape));
    }

    private void PageSizeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void SizeLetter_Click(object sender, RoutedEventArgs e)
    {
        TryExecuteGroupedSheetCommand("Paper Size", sheetId => new SetPaperSizeCommand(sheetId, WorksheetPaperSize.Letter));
    }

    private void SizeA4_Click(object sender, RoutedEventArgs e)
    {
        TryExecuteGroupedSheetCommand("Paper Size", sheetId => new SetPaperSizeCommand(sheetId, WorksheetPaperSize.A4));
    }

    private void SizeLegal_Click(object sender, RoutedEventArgs e)
    {
        TryExecuteGroupedSheetCommand("Paper Size", sheetId => new SetPaperSizeCommand(sheetId, WorksheetPaperSize.Legal));
    }

    private void PrintAreaBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void PrintAreaSetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        if (!TryExecuteGroupedSheetCommand("Print Area", sheetId => new SetPrintAreaCommand(sheetId, RemapRangeToSheet(range, sheetId))))
            return;
        RefreshStatusBar();
    }

    private void PrintAreaClearMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteGroupedSheetCommand("Print Area", sheetId => new ClearPrintAreaCommand(sheetId)))
            return;
        RefreshStatusBar();
    }

    private void ScaleToFitBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        var current = sheet.ScaleToFit;
        var defaultValue = current.ScalePercent.HasValue
            ? current.ScalePercent.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : $"{current.FitToPagesWide ?? 1}x{current.FitToPagesTall ?? 1}";
        var input = PromptForInput("Scale percent (10-400) or pages wide x tall (for example 1x1):", defaultValue);
        if (input is null) return;

        WorksheetScaleToFit scaleToFit;
        if (input.Contains('x', StringComparison.OrdinalIgnoreCase))
        {
            var parts = input.Split('x', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 ||
                !int.TryParse(parts[0], out var wide) ||
                !int.TryParse(parts[1], out var tall))
            {
                MessageBox.Show("Enter fit-to-pages as width x height, for example 1x1.", "Scale to Fit", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            scaleToFit = new WorksheetScaleToFit(null, wide, tall);
        }
        else if (int.TryParse(input, out var percent))
        {
            scaleToFit = new WorksheetScaleToFit(percent, null, null);
        }
        else
        {
            MessageBox.Show("Enter a scale percent or fit-to-pages value.", "Scale to Fit", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TryExecuteGroupedSheetCommand("Scale to Fit", sheetId => new SetScaleToFitCommand(sheetId, scaleToFit));
    }

    private void PageBreaksBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        var selected = SheetGrid.SelectedRange?.Start;
        var defaultValue = selected is { } address
            ? $"row {Math.Max(2, address.Row)}"
            : "clear";
        var input = PromptForInput("Page break: row N, col N, or clear:", defaultValue);
        if (input is null) return;

        var rowBreaks = sheet.RowPageBreaks.ToList();
        var columnBreaks = sheet.ColumnPageBreaks.ToList();
        var trimmed = input.Trim();
        if (trimmed.Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            rowBreaks.Clear();
            columnBreaks.Clear();
        }
        else if (PageLayoutInputParser.TryParseBreakInput(trimmed, "row", out var rowBreak))
        {
            rowBreaks.Add(rowBreak);
        }
        else if (PageLayoutInputParser.TryParseBreakInput(trimmed, "col", out var columnBreak) ||
                 PageLayoutInputParser.TryParseBreakInput(trimmed, "column", out columnBreak))
        {
            columnBreaks.Add(columnBreak);
        }
        else
        {
            MessageBox.Show("Enter row N, col N, or clear.", "Page Breaks", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TryExecuteGroupedSheetCommand("Page Breaks", sheetId => new SetPageBreaksCommand(sheetId, rowBreaks, columnBreaks));
    }

    private void PrintTitlesBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        var rowsDefault = sheet.PrintTitleRows is { } rows ? $"{rows.Start}:{rows.End}" : "none";
        var colsDefault = sheet.PrintTitleColumns is { } cols
            ? $"{CellAddress.NumberToColumnName(cols.Start)}:{CellAddress.NumberToColumnName(cols.End)}"
            : "none";
        var rowsInput = PromptForInput("Rows to repeat at top (for example 1:2, or none):", rowsDefault);
        if (rowsInput is null) return;
        var colsInput = PromptForInput("Columns to repeat at left (for example A:C, or none):", colsDefault);
        if (colsInput is null) return;

        if (!PageLayoutInputParser.TryParseRepeatRows(rowsInput, out var repeatRows) ||
            !PageLayoutInputParser.TryParseRepeatColumns(colsInput, out var repeatColumns))
        {
            MessageBox.Show("Enter row titles as 1:2 and column titles as A:C, or type none.",
                "Print Titles", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TryExecuteGroupedSheetCommand("Print Titles", sheetId => new SetPrintTitlesCommand(sheetId, repeatRows, repeatColumns));
    }

    private void PageSetupDialogBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        var dialog = new PageSetupDialog(sheet) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (!TryExecuteGroupedSheetCommand(
                "Page Setup",
                sheetId => new SetPageSetupCommand(
                    sheetId,
                    dialog.Orientation,
                    dialog.PaperSize,
                    dialog.Margins,
                    dialog.PrintGridlines,
                    dialog.PrintHeadings,
                    dialog.ScaleToFit,
                    dialog.PrintTitleRows,
                    dialog.PrintTitleColumns,
                    dialog.CenterHorizontally,
                    dialog.CenterVertically,
                    dialog.PageOrder,
                    dialog.FirstPageNumber,
                    dialog.HeaderMargin,
                    dialog.FooterMargin,
                    dialog.PrintBlackAndWhite,
                    dialog.PrintDraftQuality,
                    dialog.PrintQualityDpi,
                    dialog.PrintErrorValue,
                    dialog.PrintComments)))
            return;

        UpdateViewport();
        RefreshStatusBar();
    }

    private void PrintGridlinesChk_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var isChecked = (sender as System.Windows.Controls.CheckBox)?.IsChecked == true;
        TryExecuteCommand(
            new SetPrintOptionsCommand(_currentSheetId, isChecked, sheet?.PrintHeadings ?? false),
            "Print Gridlines");
    }

    private void PrintHeadingsChk_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var isChecked = (sender as System.Windows.Controls.CheckBox)?.IsChecked == true;
        TryExecuteCommand(
            new SetPrintOptionsCommand(_currentSheetId, sheet?.PrintGridlines ?? false, isChecked),
            "Print Headings");
    }

    // ── Formulas tab ──────────────────────────────────────────────────────────

    private void InsertFunctionBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new InsertFunctionDialog { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrEmpty(dlg.SelectedFormula)) return;
        if (SheetGrid.SelectedRange is null) return;
        FormulaBar.Text = "=" + dlg.SelectedFormula;
        EnterEditMode();
    }

    private void DefineNameBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var name = PromptForInput("Define named range (name):", "");
        if (string.IsNullOrWhiteSpace(name)) return;
        var outcome = _commandBus.Execute(_workbook.Id, new DefineNamedRangeCommand(name.Trim(), range));
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Define Name");
            return;
        }

        MessageBox.Show($"Named range '{name}' = {range} defined.", "Define Name", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CreateNamesFromSelectionBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        var dlg = new CreateNamesFromSelectionDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var command = new CreateNamedRangesFromSelectionCommand(
            range,
            dlg.UseTopRow,
            dlg.UseLeftColumn,
            dlg.UseBottomRow,
            dlg.UseRightColumn);
        var outcome = _commandBus.Execute(_workbook.Id, command);
        if (!outcome.Success)
            ShowCommandError(outcome, "Create from Selection");
    }

    private void UseInFormulaBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn) return;
        if (_workbook.NamedRanges.Count == 0)
        {
            MessageBox.Show("No names are defined in this workbook.", "Use in Formula", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var menu = new ContextMenu();
        foreach (var name in _workbook.NamedRanges.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            var item = new MenuItem { Header = name };
            item.Click += (_, _) => InsertDefinedNameIntoFormula(name);
            menu.Items.Add(item);
        }

        MenuKeyTipAssigner.AssignUniqueKeyTips(menu.Items.OfType<MenuItem>());
        menu.PlacementTarget = btn;
        menu.IsOpen = true;
    }

    private void InsertDefinedNameIntoFormula(string name)
    {
        var result = FormulaInsertionService.InsertDefinedName(FormulaBar.Text, FormulaBar.CaretIndex, name);
        FormulaBar.Text = result.Text;
        FormulaBar.CaretIndex = result.CaretIndex;
        FormulaBar.Focus();
        EnterEditMode();
    }

    private void TracePrecedentsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        TracePrecedentsForCell(range.Start, "Trace Precedents");
    }

    private void TracePrecedentsForCell(CellAddress activeCell, string title)
    {
        var precedents = FormulaAuditingService.GetDirectPrecedents(_workbook, activeCell);
        if (precedents.Count == 0)
        {
            MessageBox.Show($"{FormulaAuditFormatter.FormatAddress(_workbook, activeCell)} has no direct precedents.",
                title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _formulaTraceArrows.Clear();
        _formulaTraceArrows.AddRange(FormulaAuditingService.GetPrecedentTraceArrows(_workbook, activeCell));
        UpdateViewport();
        MessageBox.Show(
            $"{FormulaAuditFormatter.FormatAddress(_workbook, activeCell)} directly references {precedents.Count} cell(s):\n{FormulaAuditFormatter.FormatAddresses(_workbook, precedents)}",
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void TraceDependentsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        var activeCell = range.Start;
        var dependents = FormulaAuditingService.GetDirectDependents(_workbook, activeCell);
        if (dependents.Count == 0)
        {
            MessageBox.Show($"{FormulaAuditFormatter.FormatAddress(_workbook, activeCell)} has no direct dependents.",
                "Trace Dependents", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _formulaTraceArrows.Clear();
        _formulaTraceArrows.AddRange(FormulaAuditingService.GetDependentTraceArrows(_workbook, activeCell));
        UpdateViewport();
        MessageBox.Show(
            $"{FormulaAuditFormatter.FormatAddress(_workbook, activeCell)} is directly referenced by {dependents.Count} cell(s):\n{FormulaAuditFormatter.FormatAddresses(_workbook, dependents)}",
            "Trace Dependents",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void RemoveArrowsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_formulaTraceArrows.Count == 0)
        {
            MessageBox.Show("No auditing arrows to remove.", "Remove Arrows", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _formulaTraceArrows.Clear();
        UpdateViewport();
    }

    private void ShowFormulasBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        var showFormulas = !sheet.ShowFormulas;
        if (!TryExecuteGroupedSheetCommand(
                "Show Formulas",
                sheetId => new SetWorksheetShowFormulasCommand(sheetId, showFormulas)))
            return;

        UpdateViewport();
    }

    private void ErrorCheckBtn_Click(object sender, RoutedEventArgs e)
    {
        RecalculateWorkbook();

        var issues = FormulaAuditingService.FindFormulaErrorIssues(_workbook, _currentSheetId);
        if (issues.Count == 0)
        {
            MessageBox.Show("No errors found.", "Error Checking", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new ErrorCheckingDialog(
            issues,
            address =>
            {
                NavigateToCell(address);
                RefreshSheetTabs();
                UpdateViewport();
                RefreshStatusBar();
            },
            issue =>
            {
                if (!TryExecuteCommand(
                        new SetFormulaErrorIgnoredCommand(issue.SheetId, issue.Address, ignored: true),
                        "Ignore Error"))
                    return false;

                UpdateViewport();
                RefreshStatusBar();
                return true;
            },
            issue =>
            {
                NavigateToCell(issue.Address);
                RefreshSheetTabs();
                UpdateViewport();
                RefreshStatusBar();
                TracePrecedentsForCell(issue.Address, "Trace Error");
            })
        {
            Owner = this
        };
        dialog.Show();
    }

    private void EvaluateFormulaBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range)
            return;

        RecalculateWorkbook();
        var summary = FormulaEvaluationSummaryService.GetSummary(_workbook, range.Start);
        if (summary is null)
        {
            MessageBox.Show("Select a cell that contains a formula.", "Evaluate Formula", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new EvaluateFormulaDialog(summary)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void AddWatchBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range)
            return;

        var added = WatchWindowService.AddWatches(_workbook, range);
        _watchWindowDialog?.Refresh();
        MessageBox.Show(
            WatchWindowMessageFormatter.FormatAddResult(added, FormatRangeReference(range.Start, range.End)),
            "Watch Window",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void DeleteWatchBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range)
            return;

        var removed = WatchWindowService.RemoveWatches(_workbook, range);
        _watchWindowDialog?.Refresh();
        MessageBox.Show(
            WatchWindowMessageFormatter.FormatRemoveResult(removed, FormatRangeReference(range.Start, range.End)),
            "Watch Window",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void WatchWindowBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_watchWindowDialog is null)
        {
            _watchWindowDialog = new WatchWindowDialog(
                () =>
                {
                    RecalculateWorkbook();
                    return WatchWindowService.GetEntries(_workbook);
                },
                address =>
                {
                    NavigateToCell(address);
                    RefreshSheetTabs();
                    UpdateViewport();
                    RefreshStatusBar();
                },
                address =>
                {
                    WatchWindowService.RemoveWatch(_workbook, address);
                    UpdateViewport();
                })
            {
                Owner = this
            };
            _watchWindowDialog.Closed += (_, _) => _watchWindowDialog = null;
            _watchWindowDialog.Show();
        }
        else
        {
            _watchWindowDialog.Refresh();
            if (_watchWindowDialog.WindowState == WindowState.Minimized)
                _watchWindowDialog.WindowState = WindowState.Normal;
            _watchWindowDialog.Activate();
        }
    }

    private void CalcNowBtn_Click(object sender, RoutedEventArgs e)
    {
        RecalculateWorkbook();
        UpdateViewport();
    }
    private void CalcSheetBtn_Click(object sender, RoutedEventArgs e)
    {
        _recalcEngine.RecalculateSheetFormulas(_workbook, _currentSheetId);
        UpdateViewport();
    }
    private void CalcOptionsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void CalcAutoMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteCommand(new SetCalculationModeCommand(WorkbookCalculationMode.Automatic), "Calculation Options"))
            return;
        RecalculateWorkbook();
        UpdateViewport();
    }

    private void CalcManualMenuItem_Click(object sender, RoutedEventArgs e)
    {
        TryExecuteCommand(new SetCalculationModeCommand(WorkbookCalculationMode.Manual), "Calculation Options");
    }

    private void FormulaLogicalBtn_Click(object sender, RoutedEventArgs e)
    {
        OpenFormulaFunctionMenu(sender, ["IF", "IFS", "AND", "OR", "NOT", "IFERROR", "IFNA"]);
    }
    private void FormulaTextBtn_Click(object sender, RoutedEventArgs e)    => OpenFormulaFunctionMenu(sender, ["CONCAT", "LEFT", "RIGHT", "MID", "LEN", "TRIM", "TEXT", "UPPER", "LOWER", "PROPER", "SUBSTITUTE", "FIND", "SEARCH", "REPT", "VALUE"]);
    private void FormulaDateBtn_Click(object sender, RoutedEventArgs e)    => OpenFormulaFunctionMenu(sender, ["TODAY", "NOW", "DATE", "YEAR", "MONTH", "DAY", "HOUR", "MINUTE", "SECOND", "WEEKDAY", "EDATE", "DATEDIF"]);
    private void FormulaLookupBtn_Click(object sender, RoutedEventArgs e)  => OpenFormulaFunctionMenu(sender, ["VLOOKUP", "HLOOKUP", "XLOOKUP", "INDEX", "MATCH"]);
    private void FormulaMathBtn_Click(object sender, RoutedEventArgs e)    => OpenFormulaFunctionMenu(sender, ["SUM", "AVERAGE", "COUNT", "MIN", "MAX", "ROUND", "ABS", "SQRT", "MOD", "POWER", "INT", "CEILING", "FLOOR", "SIGN", "LOG", "LN", "EXP", "PI", "FACT", "RANDBETWEEN"]);
    private void FormulaMoreBtn_Click(object sender, RoutedEventArgs e)    => InsertFunctionBtn_Click(sender, e);

    private void OpenFormulaFunctionMenu(object sender, IReadOnlyList<string> functionNames)
    {
        if (sender is not System.Windows.Controls.Button btn) return;
        var menu = new ContextMenu();
        foreach (var functionName in functionNames)
        {
            var item = new MenuItem { Header = functionName };
            item.Click += (_, _) => InsertFormulaFunction(functionName);
            menu.Items.Add(item);
        }

        MenuKeyTipAssigner.AssignUniqueKeyTips(menu.Items.OfType<MenuItem>());
        btn.ContextMenu = menu;
        menu.PlacementTarget = btn;
        menu.IsOpen = true;
    }

    private void InsertFormulaFunction(string funcName)
    {
        if (SheetGrid.SelectedRange is null) return;
        FormulaBar.Text = $"={funcName}(";
        EnterEditMode();
        FormulaBar.CaretIndex = FormulaBar.Text.Length;
    }
    private void Formula_IF_Click(object sender, RoutedEventArgs e)      => InsertFormulaFunction("IF");
    private void Formula_AND_Click(object sender, RoutedEventArgs e)     => InsertFormulaFunction("AND");
    private void Formula_OR_Click(object sender, RoutedEventArgs e)      => InsertFormulaFunction("OR");
    private void Formula_NOT_Click(object sender, RoutedEventArgs e)     => InsertFormulaFunction("NOT");
    private void Formula_IFS_Click(object sender, RoutedEventArgs e)     => InsertFormulaFunction("IFS");
    private void Formula_CONCAT_Click(object sender, RoutedEventArgs e)  => InsertFormulaFunction("CONCAT");
    private void Formula_LEFT_Click(object sender, RoutedEventArgs e)    => InsertFormulaFunction("LEFT");
    private void Formula_RIGHT_Click(object sender, RoutedEventArgs e)   => InsertFormulaFunction("RIGHT");
    private void Formula_MID_Click(object sender, RoutedEventArgs e)     => InsertFormulaFunction("MID");
    private void Formula_LEN_Click(object sender, RoutedEventArgs e)     => InsertFormulaFunction("LEN");
    private void Formula_TRIM_Click(object sender, RoutedEventArgs e)    => InsertFormulaFunction("TRIM");
    private void Formula_TEXT_Click(object sender, RoutedEventArgs e)    => InsertFormulaFunction("TEXT");
    private void Formula_TODAY_Click(object sender, RoutedEventArgs e)   => InsertFormulaFunction("TODAY");
    private void Formula_NOW_Click(object sender, RoutedEventArgs e)     => InsertFormulaFunction("NOW");
    private void Formula_DATE_Click(object sender, RoutedEventArgs e)    => InsertFormulaFunction("DATE");
    private void Formula_YEAR_Click(object sender, RoutedEventArgs e)    => InsertFormulaFunction("YEAR");
    private void Formula_MONTH_Click(object sender, RoutedEventArgs e)   => InsertFormulaFunction("MONTH");
    private void Formula_DAY_Click(object sender, RoutedEventArgs e)     => InsertFormulaFunction("DAY");
    private void Formula_VLOOKUP_Click(object sender, RoutedEventArgs e) => InsertFormulaFunction("VLOOKUP");
    private void Formula_HLOOKUP_Click(object sender, RoutedEventArgs e) => InsertFormulaFunction("HLOOKUP");
    private void Formula_INDEX_Click(object sender, RoutedEventArgs e)   => InsertFormulaFunction("INDEX");
    private void Formula_MATCH_Click(object sender, RoutedEventArgs e)   => InsertFormulaFunction("MATCH");
    private void Formula_XLOOKUP_Click(object sender, RoutedEventArgs e) => InsertFormulaFunction("XLOOKUP");
    private void Formula_SUM_Click(object sender, RoutedEventArgs e)     => InsertFormulaFunction("SUM");
    private void Formula_ROUND_Click(object sender, RoutedEventArgs e)   => InsertFormulaFunction("ROUND");
    private void Formula_ABS_Click(object sender, RoutedEventArgs e)     => InsertFormulaFunction("ABS");
    private void Formula_SQRT_Click(object sender, RoutedEventArgs e)    => InsertFormulaFunction("SQRT");

    // ── Data tab additions ────────────────────────────────────────────────────

    private void GetDataBtn_Click(object sender, RoutedEventArgs e)
    {
        var adapters = _fileAdapters
            .Where(adapter => adapter.Extension is ".csv")
            .ToList();
        if (adapters.Count == 0)
        {
            MessageBox.Show("No import adapters are available.", "Get Data", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var filter = string.Join("|", adapters.Select(a => $"{a.FormatName}|*{a.Extension}"));
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = filter };
        if (dialog.ShowDialog() != true) return;

        var ext = System.IO.Path.GetExtension(dialog.FileName).ToLowerInvariant();
        var adapter = adapters.FirstOrDefault(a => a.Extension == ext);
        if (adapter is null) return;

        try
        {
            using var stream = System.IO.File.OpenRead(dialog.FileName);
            var imported = adapter.Load(stream);
            if (imported.Sheets.Count == 0) return;

            var destination = SheetGrid.SelectedRange?.Start ?? new CellAddress(_currentSheetId, 1, 1);
            if (!TryExecuteCommand(new ImportSheetCommand(_currentSheetId, destination, imported.Sheets[0]), "Get Data", out var outcome))
                return;

            RecalculateIfAutomatic(outcome.AffectedCells ?? []);
            SetActiveCell(destination);
            EnsureCellVisible(destination);
            UpdateViewport();
            RefreshStatusBar();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to import data:\n{ex.Message}", "Get Data", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    private void RefreshAllBtn_Click(object sender, RoutedEventArgs e) => CalcNowBtn_Click(sender, e);

    private void TextToColumnsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var delimInput = PromptForInput("Delimiter (e.g. , or ;):", ",");
        if (delimInput is null || delimInput.Length == 0) return;
        char delim = delimInput[0];
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Text to Columns",
                range,
                currentRange => CreateTextToColumnsCommand(currentRange, delim),
                out var outcome))
            return;

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        UpdateViewport();
    }

    private IWorkbookCommand CreateTextToColumnsCommand(GridRange range, char delimiter)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return new EditCellsCommand(_currentSheetId, []);

        var edits = new List<(CellAddress, Cell)>();
        for (uint r = range.Start.Row; r <= range.End.Row; r++)
        {
            var cellVal = sheet.GetValue(r, range.Start.Col) as TextValue;
            if (cellVal is null) continue;
            var parts = cellVal.Value.Split(delimiter);
            for (int i = 0; i < parts.Length; i++)
            {
                var addr = new CellAddress(_currentSheetId, r, range.Start.Col + (uint)i);
                ScalarValue val = double.TryParse(parts[i].Trim(), out var d) ? new NumberValue(d) : new TextValue(parts[i].Trim());
                edits.Add((addr, Cell.FromValue(val)));
            }
        }

        var targetSheetIds = CurrentGroupedEditSheetIds();
        return targetSheetIds.Count > 1
            ? new GroupedEditCellsCommand(targetSheetIds, _currentSheetId, edits)
            : new EditCellsCommand(_currentSheetId, edits);
    }

    private void RemoveDuplicatesBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        RemoveDuplicateRowsCommand? command = null;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Remove Duplicates",
                range,
                currentRange =>
                {
                    command = new RemoveDuplicateRowsCommand(_currentSheetId, currentRange);
                    return command;
                }))
            return;

        MessageBox.Show($"Removed {command?.RemovedRowCount ?? 0} duplicate rows.", "Remove Duplicates", MessageBoxButton.OK, MessageBoxImage.Information);
        UpdateViewport();
    }

    private void AdvancedFilterBtn_Click(object sender, RoutedEventArgs e)
    {
        var defaultList = SheetGrid.SelectedRange is { } selected
            ? FormatWorkbookRange(selected)
            : "A1:C10";
        var listInput = PromptForInput("List range:", defaultList);
        if (listInput is null) return;
        if (!TryParseWorkbookRange(_currentSheetId, listInput, out var listRange))
        {
            MessageBox.Show("Invalid list range.", "Advanced Filter", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var criteriaInput = PromptForInput("Criteria range:", "E1:F2");
        if (criteriaInput is null) return;
        if (!TryParseWorkbookRange(_currentSheetId, criteriaInput, out var criteriaRange))
        {
            MessageBox.Show("Invalid criteria range.", "Advanced Filter", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        CellAddress? copyTo = null;
        var copyInput = PromptForInput("Copy to cell (leave blank to filter in place):", "");
        if (copyInput is null) return;
        if (!string.IsNullOrWhiteSpace(copyInput))
        {
            if (!CellAddress.TryParse(copyInput.Trim(), _currentSheetId, out var destination))
            {
                MessageBox.Show("Invalid copy destination.", "Advanced Filter", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            copyTo = destination;
        }

        var uniqueInput = PromptForInput("Unique records only? (yes/no):", "no");
        if (uniqueInput is null) return;
        var unique = uniqueInput.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                     uniqueInput.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) ||
                     uniqueInput.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);

        var outcome = _commandBus.Execute(
            _workbook.Id,
            new AdvancedFilterCommand(listRange, criteriaRange, copyTo, unique));
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Advanced Filter");
            return;
        }

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        if (copyTo is { } destinationCell)
            SetActiveCell(destinationCell);
        UpdateViewport();
    }

    private void ConsolidateBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = SheetGrid.SelectedRange;
        var defaultSource = selected?.ToString() ?? "A1:B2";
        var sourceInput = PromptForInput("Source ranges to sum (same size, separated by comma or semicolon):", defaultSource);
        if (sourceInput is null) return;

        var ranges = new List<GridRange>();
        foreach (var part in sourceInput.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                ranges.Add(GridRange.Parse(part, _currentSheetId));
            }
            catch
            {
                MessageBox.Show($"Invalid range: {part}", "Consolidate", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        var defaultDestination = selected?.Start.ToA1() ?? "A1";
        var destinationInput = PromptForInput("Destination cell:", defaultDestination);
        if (destinationInput is null) return;

        if (!CellAddress.TryParse(destinationInput, _currentSheetId, out var destination))
        {
            MessageBox.Show("Invalid destination cell.", "Consolidate", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var outcome = _commandBus.ExecuteRepeatable(
            _workbook.Id,
            () => new ConsolidateCommand(ranges, destination));
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Consolidate");
            return;
        }

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        SetActiveCell(destination);
        EnsureCellVisible(destination);
        UpdateViewport();
    }

    // ── What-If Analysis ─────────────────────────────────────────────────────

    private void SubtotalBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range)
        {
            MessageBox.Show("Select a range with a header row and data rows.", "Subtotal", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var input = PromptForInput("Group column, subtotal columns, function, options (for example 1,2+3,sum,replace pagebreak above):", "1,2,sum,replace");
        if (input is null) return;

        if (!SubtotalInputParser.TryParse(input, out var options, out var error))
        {
            MessageBox.Show(error ?? "Enter group column, subtotal columns, and optional function, for example 1,2+3,sum.",
                "Subtotal", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Subtotal",
                range,
                currentRange =>
                {
                    var subtotalCommand = new SubtotalCommand(
                        _currentSheetId,
                        currentRange,
                        groupByColumnOffset: options.GroupColumnOffset,
                        subtotalColumnOffsets: options.SubtotalColumnOffsets,
                        functionNumber: options.FunctionNumber,
                        pageBreakBetweenGroups: options.PageBreakBetweenGroups,
                        summaryBelowData: options.SummaryBelowData);
                    return options.ReplaceExisting
                        ? new CompositeWorkbookCommand("Subtotal", [new RemoveSubtotalRowsCommand(_currentSheetId, currentRange), subtotalCommand])
                        : subtotalCommand;
                },
                out var outcome))
            return;

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        UpdateViewport();
    }

    private void GoalSeekBtn_Click(object sender, RoutedEventArgs e)
    {
        var selectedCell = _selectionAnchor;
        var dlg = new GoalSeekDialog(_currentSheetId, selectedCell) { Owner = this };

        if (dlg.ShowDialog() != true)
            return;

        var setCell = dlg.SetCell!.Value;
        var changingCell = dlg.ChangingCell!.Value;
        var targetValue = dlg.TargetValue;

        var result = GoalSeekService.Seek(_workbook, _recalcEngine, setCell, targetValue, changingCell);

        if (!result.Converged)
        {
            MessageBox.Show(
                $"Goal Seek could not find a solution.\nClosest value: {result.FoundValue:G10}\nActual result: {result.ActualResult:G10}",
                "Goal Seek", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            $"Goal Seek found a solution.\nChanging cell value: {result.FoundValue:G10}",
            "Goal Seek", MessageBoxButton.OKCancel, MessageBoxImage.Information);

        if (confirm == MessageBoxResult.OK)
        {
            var cmd = new GoalSeekCommand(changingCell, result.FoundValue);
            if (TryExecuteCommand(cmd, "Goal Seek"))
                RecalculateIfAutomatic([changingCell]);
        }
    }

    // ── Review tab ────────────────────────────────────────────────────────────

    private void ScenariosBtn_Click(object sender, RoutedEventArgs e)
    {
        var action = PromptForInput("Scenario action (save/show/list/report):", _workbook.Scenarios.Count == 0 ? "save" : "show");
        if (action is null)
            return;

        switch (action.Trim().ToLowerInvariant())
        {
            case "save":
            case "add":
                SaveScenarioFromSelection();
                break;
            case "show":
            case "apply":
                ShowScenarioByName();
                break;
            case "list":
            case "manager":
                ListScenarios();
                break;
            case "report":
            case "summary":
                CreateScenarioSummaryReport();
                break;
            default:
                MessageBox.Show("Enter save, show, list, or report.", "Scenario Manager", MessageBoxButton.OK, MessageBoxImage.Information);
                break;
        }
    }

    private void SaveScenarioFromSelection()
    {
        if (SheetGrid.SelectedRange is not { } range)
        {
            MessageBox.Show("Select the changing cells for the scenario.", "Scenario Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null)
            return;

        var name = PromptForInput("Scenario name:", _workbook.Scenarios.Count == 0 ? "Scenario 1" : $"Scenario {_workbook.Scenarios.Count + 1}");
        if (name is null)
            return;

        var changes = range.AllCells()
            .Select(address => new ScenarioCellValue(address, sheet.GetValue(address.Row, address.Col)))
            .ToList();
        if (!TryExecuteCommand(new SaveScenarioCommand(name, changes), "Scenario Manager"))
            return;

        MessageBox.Show($"Scenario '{name.Trim()}' saved for {changes.Count} changing cell(s).",
            "Scenario Manager", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ShowScenarioByName()
    {
        if (_workbook.Scenarios.Count == 0)
        {
            MessageBox.Show("No scenarios are saved in this workbook.", "Scenario Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var name = PromptForInput("Scenario name to show:", _workbook.Scenarios[0].Name);
        if (name is null)
            return;

        if (!TryExecuteCommand(new ApplyScenarioCommand(name), "Scenario Manager", out var outcome))
            return;

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        if (outcome.AffectedCells?.FirstOrDefault() is { } first)
        {
            SetActiveCell(first);
            EnsureCellVisible(first);
        }

        UpdateViewport();
        RefreshStatusBar();
    }

    private void ListScenarios()
    {
        if (_workbook.Scenarios.Count == 0)
        {
            MessageBox.Show("No scenarios are saved in this workbook.", "Scenario Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var message = string.Join(Environment.NewLine,
            _workbook.Scenarios.Select(s => $"{s.Name}: {s.ChangingCells.Count} changing cell(s)"));
        MessageBox.Show(message, "Scenario Manager", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CreateScenarioSummaryReport()
    {
        if (!TryExecuteCommand(new ScenarioSummaryReportCommand(), "Scenario Manager"))
            return;

        var report = _workbook.Sheets.LastOrDefault();
        if (report is not null)
        {
            _currentSheetId = report.Id;
            _groupedSheetIds.Clear();
            _groupedSheetIds.Add(_currentSheetId);
            SetActiveCell(new CellAddress(_currentSheetId, 1, 1));
        }

        UpdateViewport();
        RefreshSheetTabs();
        RefreshStatusBar();
    }

    private void ForecastSheetBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range)
        {
            MessageBox.Show("Select a two-column range with headers and at least two data rows.",
                "Forecast Sheet", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var periodsInput = PromptForInput("Forecast periods:", "3");
        if (periodsInput is null)
            return;
        if (!uint.TryParse(periodsInput.Trim(), out var periods) || periods == 0)
        {
            MessageBox.Show("Enter a positive whole number of forecast periods.",
                "Forecast Sheet", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryExecuteCommand(new ForecastSheetCommand(range, periods), "Forecast Sheet"))
            return;

        var forecastSheet = _workbook.Sheets.LastOrDefault();
        if (forecastSheet is not null)
        {
            _currentSheetId = forecastSheet.Id;
            _groupedSheetIds.Clear();
            _groupedSheetIds.Add(_currentSheetId);
            SetActiveCell(new CellAddress(_currentSheetId, 1, 1));
        }

        RecalculateWorkbook();
        UpdateViewport();
        RefreshSheetTabs();
        RefreshStatusBar();
    }

    private void DataTableBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range)
        {
            MessageBox.Show("Select the data table range, including the formula row and input values.",
                "Data Table", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var mode = PromptForInput("Data table type (one/two):", "one");
        if (mode is null)
            return;
        var twoVariable = mode.Trim().Equals("two", StringComparison.OrdinalIgnoreCase) ||
                          mode.Trim().Equals("2", StringComparison.OrdinalIgnoreCase);

        var formulaDefault = new CellAddress(_currentSheetId, range.Start.Row, twoVariable ? range.Start.Col : range.Start.Col + 1).ToA1();
        var formulaInput = PromptForInput("Formula cell:", formulaDefault);
        if (formulaInput is null)
            return;
        if (!CellAddress.TryParse(formulaInput.Trim(), _currentSheetId, out var formulaCell))
        {
            MessageBox.Show("Enter a valid formula cell address.", "Data Table", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Func<GridRange, IWorkbookCommand> createCommand;
        if (twoVariable)
        {
            var rowInputText = PromptForInput("Row input cell:", formulaCell.ToA1());
            if (rowInputText is null)
                return;
            if (!CellAddress.TryParse(rowInputText.Trim(), _currentSheetId, out var rowInputCell))
            {
                MessageBox.Show("Enter a valid row input cell address.", "Data Table", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var columnInputText = PromptForInput("Column input cell:", formulaCell.ToA1());
            if (columnInputText is null)
                return;
            if (!CellAddress.TryParse(columnInputText.Trim(), _currentSheetId, out var columnInputCell))
            {
                MessageBox.Show("Enter a valid column input cell address.", "Data Table", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            createCommand = currentRange => new TwoVariableDataTableCommand(currentRange, formulaCell, rowInputCell, columnInputCell);
        }
        else
        {
            var inputInput = PromptForInput("Column input cell:", formulaCell.ToA1());
            if (inputInput is null)
                return;
            if (!CellAddress.TryParse(inputInput.Trim(), _currentSheetId, out var inputCell))
            {
                MessageBox.Show("Enter a valid input cell address.", "Data Table", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            createCommand = currentRange => new OneVariableDataTableCommand(currentRange, formulaCell, inputCell);
        }

        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Data Table",
                range,
                createCommand,
                out var outcome))
            return;

        RecalculateIfAutomatic(outcome.AffectedCells ?? []);
        UpdateViewport();
        RefreshStatusBar();
    }

    private void SpellCheckBtn_Click(object sender, RoutedEventArgs e)
    {
        var issues = SpellCheckService.FindIssues(_workbook, _currentSheetId);
        if (issues.Count == 0)
        {
            MessageBox.Show("Spelling check is complete.", "Spell Check", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var issue = issues[0];
        SetActiveCell(issue.Address);
        EnsureCellVisible(issue.Address);
        UpdateViewport();

        var replacement = PromptForInput(
            $"Replace '{issue.Word}' in {issue.Address.ToA1()} with:",
            issue.Suggestion);
        if (replacement is null) return;

        var corrected = SpellCheckService.ApplyCorrection(issue, replacement);
        if (!TryExecuteEditCells([(issue.Address, Cell.FromValue(new TextValue(corrected)))], "Spell Check"))
            return;

        UpdateViewport();
        RefreshStatusBar();
    }

    private void WorkbookStatisticsBtn_Click(object sender, RoutedEventArgs e)
    {
        var statistics = WorkbookStatisticsService.GetStatistics(_workbook);
        var message = WorkbookStatisticsFormatter.Format(statistics);
        MessageBox.Show(message, "Workbook Statistics", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void AccessibilityCheckerBtn_Click(object sender, RoutedEventArgs e)
    {
        var issues = AccessibilityCheckerService.FindIssues(_workbook);
        if (issues.Count == 0)
        {
            MessageBox.Show("No accessibility issues found.", "Accessibility", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var message = AccessibilityIssueFormatter.Format(issues);
        MessageBox.Show(message, "Accessibility", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void SetAltTextBtn_Click(object sender, RoutedEventArgs e)
    {
        var target = GetTargetAltTextObject(_currentSheetId);
        if (target is null)
        {
            MessageBox.Show("No picture, shape, or text box is anchored at the selected cell.",
                "Alt Text", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var input = PromptForInput("Alt text:", target.AltText ?? "");
        if (input is null)
            return;

        if (!TryExecuteRepeatableGroupedSheetCommand(
                "Alt Text",
                sheetId =>
                {
                    var groupedTarget = GetTargetAltTextObject(sheetId, target.Kind);
                    return target.Kind switch
                    {
                        AltTextObjectKind.Picture => new SetPictureAltTextCommand(sheetId, groupedTarget?.Id ?? Guid.Empty, input),
                        AltTextObjectKind.Shape => new SetDrawingShapeAltTextCommand(sheetId, groupedTarget?.Id ?? Guid.Empty, input),
                        _ => new SetTextBoxAltTextCommand(sheetId, groupedTarget?.Id ?? Guid.Empty, input)
                    };
                }))
        {
            return;
        }

        SetActiveCell(target.Anchor);
        EnsureCellVisible(target.Anchor);
        UpdateViewport();
        RefreshStatusBar();
    }

    private AltTextObjectTarget? GetTargetAltTextObject(SheetId sheetId, AltTextObjectKind? preferredKind = null)
    {
        var sheet = _workbook.GetSheet(sheetId);
        if (sheet is null)
            return null;

        return AltTextTargetResolver.Resolve(sheet, SheetGrid.SelectedRange?.Start, preferredKind);
    }

    private void ReviewNewCommentBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is null) return;
        var addr = SheetGrid.SelectedRange.Value.Start;
        var text = PromptForInput($"Add comment to {addr.ToA1()}:", "");
        if (text is null) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Comment",
                SheetGrid.SelectedRange.Value,
                currentRange => new SetCommentCommand(_currentSheetId, currentRange.Start, text)))
            return;

        UpdateViewport();
        MessageBox.Show($"Comment added to {addr.ToA1()}.", "Comment", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ReviewDeleteCommentBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is null) return;
        if (!TryExecuteRepeatableCurrentRangeCommand(
                "Comment",
                SheetGrid.SelectedRange.Value,
                currentRange => new DeleteCommentCommand(_currentSheetId, currentRange.Start)))
            return;

        UpdateViewport();
    }

    private void ReviewPrevCommentBtn_Click(object sender, RoutedEventArgs e)
    {
        NavigateComment(previous: true);
    }

    private void ReviewNextCommentBtn_Click(object sender, RoutedEventArgs e)
    {
        NavigateComment(previous: false);
    }

    private void ReviewShowCommentsBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null || sheet.Comments.Count == 0)
        {
            MessageBox.Show("No comments on this sheet.", "Comments", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var text = CommentNavigationPlanner.FormatCommentList(sheet.Comments);
        MessageBox.Show(text, "Comments", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void NavigateComment(bool previous)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null || sheet.Comments.Count == 0)
        {
            MessageBox.Show("No comments on this sheet.", "Comments", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var comments = CommentNavigationPlanner.OrderedCommentAddresses(sheet.Comments);
        var current = SheetGrid.SelectedRange?.Start ?? comments[0];
        var target = CommentNavigationPlanner.FindNext(comments, current, previous);

        SetActiveCell(target);
        EnsureCellVisible(target);
        UpdateViewport();
    }

    private void ProtectSheetBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        string? pwd = null;
        if (!sheet.IsProtected)
        {
            pwd = PromptForInput("Set sheet protection password (leave blank for no password):", "");
            if (pwd is null) return;
        }

        var action = SheetProtectionWorkflow.CreateCommand(sheet, pwd);
        var outcome = _commandBus.Execute(_workbook.Id, action.Command);
        if (!outcome.Success)
        {
            ShowCommandError(outcome, action.Title);
            return;
        }

        MessageBox.Show(action.SuccessMessage, action.Title, MessageBoxButton.OK, MessageBoxImage.Information);
        RefreshSheetProtectionUi();
    }

    private void ProtectWorkbookBtn_Click(object sender, RoutedEventArgs e)
    {
        string? pwd = null;
        if (!_workbook.IsStructureProtected)
        {
            pwd = PromptForInput("Set workbook structure password (leave blank for no password):", "");
            if (pwd is null) return;
        }

        var action = WorkbookProtectionWorkflow.CreateCommand(_workbook, pwd);
        if (!TryExecuteCommand(action.Command, action.Title))
            return;

        MessageBox.Show(action.SuccessMessage, action.Title, MessageBoxButton.OK, MessageBoxImage.Information);
        RefreshWorkbookProtectionUi();
        RefreshSheetTabs();
    }
    private void AllowEditRangesBtn_Click(object sender, RoutedEventArgs e)
    {
        var defaultRange = SheetGrid.SelectedRange?.ToString() ?? "A1:A1";
        var input = PromptForInput("Range to allow editing while protected:", defaultRange);
        if (input is null) return;

        GridRange range;
        try
        {
            range = GridRange.Parse(input, _currentSheetId);
        }
        catch
        {
            MessageBox.Show("Invalid range.", "Allow Edit Ranges", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryExecuteCommand(new AllowEditRangeCommand(_currentSheetId, range), "Allow Edit Ranges"))
            return;

        MessageBox.Show($"{range} can now be edited while this sheet is protected.",
            "Allow Edit Ranges", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private async void ShareWorkbookBtn_Click(object sender, RoutedEventArgs e) => await ShareWorkbookAsync();

    private async Task ShareWorkbookAsync()
    {
        var plan = ShareWorkbookPlanner.CreatePlan(_currentFilePath);
        if (plan.Kind == ShareWorkbookPlanKind.SaveAsBeforeShare)
        {
            if (!SaveWorkbookWithDialog())
                return;
        }
        else if (FileSavePlanner.TryResolveExistingPath(plan.Path, _fileAdapters, out var target))
        {
            if (!SaveWorkbookToTarget(target!))
                return;
        }

        if (string.IsNullOrWhiteSpace(_currentFilePath))
            return;

        try
        {
            await _shareService.ShareFileAsync(this, _currentFilePath, _workbook.Name);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to open Windows Share:\n{ex.Message}",
                "Share Workbook",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // ── View tab ─────────────────────────────────────────────────────────────

    private void ViewGridlinesChk_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressViewOptionSync || SheetGrid is null) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null || sender is not System.Windows.Controls.CheckBox chk) return;

        if (!TryExecuteGroupedSheetCommand(
                "Gridlines",
                sheetId => new SetWorksheetViewOptionsCommand(
                    sheetId,
                    chk.IsChecked == true,
                    _workbook.GetSheet(sheetId)?.ShowHeadings ?? true,
                    _workbook.GetSheet(sheetId)?.ShowRulers ?? true)))
            return;

        UpdateViewport();
    }

    private void ViewHeadersChk_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressViewOptionSync || SheetGrid is null) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null || sender is not System.Windows.Controls.CheckBox chk) return;

        if (!TryExecuteGroupedSheetCommand(
                "Headings",
                sheetId => new SetWorksheetViewOptionsCommand(
                    sheetId,
                    _workbook.GetSheet(sheetId)?.ShowGridlines ?? true,
                    chk.IsChecked == true,
                    _workbook.GetSheet(sheetId)?.ShowRulers ?? true)))
            return;

        UpdateViewport();
    }

    private void ViewRulerChk_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressViewOptionSync || SheetGrid is null) return;
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null || sender is not System.Windows.Controls.CheckBox chk) return;

        if (!TryExecuteGroupedSheetCommand(
                "Ruler",
                sheetId => new SetWorksheetViewOptionsCommand(
                    sheetId,
                    _workbook.GetSheet(sheetId)?.ShowGridlines ?? true,
                    _workbook.GetSheet(sheetId)?.ShowHeadings ?? true,
                    chk.IsChecked == true)))
            return;

        UpdateViewport();
    }

    private void ViewFormulaBarChk_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressAppViewOptionSync) return;
        if (sender is not System.Windows.Controls.CheckBox chk || FormulaBarBorder is null) return;

        _options.ShowFormulaBar = chk.IsChecked == true;
        _options.Save();
        FormulaBarBorder.Visibility = _options.ShowFormulaBar ? Visibility.Visible : Visibility.Collapsed;
    }

    private void NormalViewBtn_Click(object sender, RoutedEventArgs e) =>
        SetWorksheetViewMode(WorksheetViewMode.Normal);

    private void PageBreakPreviewBtn_Click(object sender, RoutedEventArgs e) =>
        SetWorksheetViewMode(WorksheetViewMode.PageBreakPreview);

    private void PageLayoutViewBtn_Click(object sender, RoutedEventArgs e) =>
        SetWorksheetViewMode(WorksheetViewMode.PageLayout);

    private void SetWorksheetViewMode(WorksheetViewMode viewMode)
    {
        if (!TryExecuteGroupedSheetCommand("Workbook View",
                sheetId => new SetWorksheetViewModeCommand(sheetId, viewMode)))
            return;

        UpdateViewport();
    }

    private void CustomViewsBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CustomViewsDialog(_workbook, _commandBus) { Owner = this };
        dialog.ShowDialog();
        if (dialog.ViewApplied)
            UpdateViewport();
    }

    private void ArrangeAllPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }

    private void ArrangeAllMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.Controls.MenuItem)?.Tag is not string tag ||
            !Enum.TryParse<WorkbookWindowArrangement>(tag, out var arrangement))
            return;

        TryExecuteCommand(new SetWorkbookWindowArrangementCommand(arrangement), "Arrange Windows");
    }

    private void ViewWindowDeferredBtn_Click(object sender, RoutedEventArgs e)
    {
        var commandName = (sender as System.Windows.Controls.Button)?.Content?.ToString() ?? "This command";
        var message = DeferredCommandMessages.MultiWindow(commandName);
        MessageBox.Show(
            message.Body,
            message.Title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void FreezePanesPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void FreezeAtSelectionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        SetFreezePanes(
            (uint)Math.Max(0, (int)range.Start.Row - 1),
            (uint)Math.Max(0, (int)range.Start.Col - 1));
    }
    private void FreezeTopRowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SetFreezePanes(1, 0);
    }
    private void FreezeFirstColMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SetFreezePanes(0, 1);
    }
    private void UnfreezeAllMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SetFreezePanes(0, 0);
    }

    private void SetFreezePanes(uint frozenRows, uint frozenCols)
    {
        var outcome = _commandBus.Execute(
            _workbook.Id,
            new SetFreezePanesCommand(_currentSheetId, frozenRows, frozenCols));
        if (!outcome.Success)
        {
            ShowCommandError(outcome, "Freeze Panes");
            return;
        }

        UpdateViewport();
    }

    private void SplitViewBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        uint? splitRow = null;
        uint? splitColumn = null;
        if (sheet.SplitRow is null && sheet.SplitColumn is null &&
            SheetGrid.SelectedRange is { } range)
        {
            splitRow = range.Start.Row > 1 ? range.Start.Row : null;
            splitColumn = range.Start.Col > 1 ? range.Start.Col : null;
        }

        if (!TryExecuteGroupedSheetCommand(
                "Split",
                sheetId => new SetSplitPanesCommand(sheetId, splitRow, splitColumn)))
            return;

        UpdateViewport();
    }

    private void OnSplitDividerMoved(uint? splitRow, uint? splitColumn)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        var nextRow = splitRow ?? sheet.SplitRow;
        var nextColumn = splitColumn ?? sheet.SplitColumn;
        if (nextRow == sheet.SplitRow && nextColumn == sheet.SplitColumn)
            return;

        if (!TryExecuteGroupedSheetCommand(
                "Split",
                sheetId => new SetSplitPanesCommand(sheetId, nextRow, nextColumn)))
            return;

        _splitPaneViewportOffsets.Remove(_currentSheetId);
        UpdateViewport();
    }

    private void MinimizeBtn_Click(object sender, RoutedEventArgs e) =>
        SystemCommands.MinimizeWindow(this);

    private void MaxRestoreBtn_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(this);
        else
            SystemCommands.MaximizeWindow(this);
    }

    private void CloseSysBtn_Click(object sender, RoutedEventArgs e) =>
        SystemCommands.CloseWindow(this);

    private void ZoomInBtn_Click(object sender, RoutedEventArgs e)
    {
        ZoomSlider.Value = Math.Min(ZoomSlider.Maximum, ZoomSlider.Value + 5);
    }
    private void ZoomOutBtn_Click(object sender, RoutedEventArgs e)
    {
        ZoomSlider.Value = Math.Max(ZoomSlider.Minimum, ZoomSlider.Value - 5);
    }
    private void ZoomPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void ZoomPresetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.Controls.MenuItem)?.Tag is not string tag ||
            !Freexcel.App.UI.ZoomLevelMapper.TryParseZoomPercent(tag, out var zoomPercent))
            return;

        ZoomSlider.Value = Freexcel.App.UI.ZoomLevelMapper.ZoomPercentToSlider(zoomPercent);
    }
    private void ZoomCustomMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var current = (int)Math.Round(_zoomLevel * 100);
        var input = PromptForInput("Zoom", current.ToString(System.Globalization.CultureInfo.CurrentCulture));
        if (!Freexcel.App.UI.ZoomLevelMapper.TryParseZoomPercent(input, out var zoomPercent))
        {
            MessageBox.Show(
                "Enter a zoom value from 10 to 400.",
                "Zoom",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        ZoomSlider.Value = Freexcel.App.UI.ZoomLevelMapper.ZoomPercentToSlider(zoomPercent);
    }
    private void Zoom100Btn_Click(object sender, RoutedEventArgs e)
    {
        ZoomSlider.Value = 100;
    }
    private void ZoomSelectionBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        double cols = range.ColCount, rows = range.RowCount;
        double fitPct = Math.Max(Freexcel.App.UI.ZoomLevelMapper.MinZoomPercent, Math.Min(Freexcel.App.UI.ZoomLevelMapper.MaxZoomPercent, Math.Min(
            SheetGrid.ActualWidth  / Math.Max(1, cols * 80) * 100,
            SheetGrid.ActualHeight / Math.Max(1, rows * 20) * 100)));
        ZoomSlider.Value = Freexcel.App.UI.ZoomLevelMapper.ZoomPercentToSlider(fitPct);
    }
    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ZoomSlider == null || SheetGrid == null || StatusZoomText == null) return;
        if (_snapInProgress || _suppressZoomSync) return;
        double sliderVal = e.NewValue;

        // Snap to 100% when near the midpoint
        if (Math.Abs(sliderVal - 100.0) < 3.0)
        {
            _snapInProgress = true;
            ZoomSlider.Value = 100.0;
            _snapInProgress = false;
            sliderVal = 100.0;
        }

        double zoomPct = Freexcel.App.UI.ZoomLevelMapper.SliderToZoomPercent(sliderVal);
        var roundedZoomPct = (int)Math.Round(zoomPct);
        if (!TryExecuteGroupedSheetCommand(
                "Zoom",
                sheetId => new SetWorksheetZoomCommand(sheetId, roundedZoomPct)))
            return;

        SyncZoomFromSheet(roundedZoomPct, updateSlider: false);
        UpdateViewport();
    }

    private void SyncZoomFromSheet(int zoomPercent, bool updateSlider = true)
    {
        zoomPercent = Math.Clamp(zoomPercent, SetWorksheetZoomCommand.MinZoomPercent, SetWorksheetZoomCommand.MaxZoomPercent);
        _zoomLevel = zoomPercent / 100.0;
        if (SheetGrid is not null)
        {
            SheetGrid.ZoomFactor = _zoomLevel;
            SheetGrid.RenderTransform = new System.Windows.Media.ScaleTransform(_zoomLevel, _zoomLevel, 0, 0);
        }
        if (StatusZoomText is not null)
            StatusZoomText.Text = $"{zoomPercent}%";

        if (!updateSlider || ZoomSlider is null)
            return;

        _suppressZoomSync = true;
        try
        {
            ZoomSlider.Value = Freexcel.App.UI.ZoomLevelMapper.ZoomPercentToSlider(zoomPercent);
        }
        finally
        {
            _suppressZoomSync = false;
        }
    }

    // ── QAT / title bar ──────────────────────────────────────────────────────

    private void UndoQatBtn_Click(object sender, RoutedEventArgs e) => ExecuteUndo();
    private void RedoQatBtn_Click(object sender, RoutedEventArgs e) => ExecuteRedo();

    // ── Formula bar expand chevron ────────────────────────────────────────────

    private void FormulaBarExpandBtn_Click(object sender, RoutedEventArgs e)
    {
        _formulaBarExpanded = !_formulaBarExpanded;
        _options.FormulaBarExpanded = _formulaBarExpanded;
        _options.Save();
        ApplyFormulaBarExpansion();
    }

    private void ApplyFormulaBarExpansion()
    {
        if (_formulaBarExpanded)
        {
            FormulaBar.Height       = 72;
            FormulaBar.AcceptsReturn = true;
            FormulaBarExpandBtn.Content = "▲";
        }
        else
        {
            FormulaBar.ClearValue(System.Windows.Controls.TextBox.HeightProperty);
            FormulaBar.AcceptsReturn = false;
            FormulaBarExpandBtn.Content = "▼";
        }
    }

    // ── Ribbon horizontal scroll via mouse wheel ─────────────────────────────

    private void RibbonScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta * 0.5);
        e.Handled = true;
    }

    // ── Sheet tab nav arrows ──────────────────────────────────────────────────

    private void SheetNavLeftBtn_Click(object sender, RoutedEventArgs e)
    {
        SheetTabsScroller.ScrollToHorizontalOffset(
            Math.Max(0, SheetTabsScroller.HorizontalOffset - 80));
    }

    private void SheetNavRightBtn_Click(object sender, RoutedEventArgs e)
    {
        SheetTabsScroller.ScrollToHorizontalOffset(
            SheetTabsScroller.HorizontalOffset + 80);
    }

    // ── Sheet tab context menu ────────────────────────────────────────────────

    private void SheetCtxRename_Click(object sender, RoutedEventArgs e)
    {
        var tab = GetContextMenuTab(sender);
        if (tab == null) return;
        RenameSheetFromTab(tab);
    }

    private void RenameSheetFromTab(SheetTabViewModel tab)
    {
        var name = PromptForInput("Rename Sheet", tab.Name);
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

        var defaultName = hiddenSheets[0].Name;
        var name = PromptForInput("Unhide sheet name:", defaultName);
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
        var defaultValue = sheet?.TabColor is { } color
            ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            : "#217346";
        var input = PromptForInput("Tab color (#RRGGBB or none):", defaultValue);
        if (input is null) return;

        CellColor? tabColor;
        if (input.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            input.Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            tabColor = null;
        }
        else if (!TryParseHexColor(input, out tabColor))
        {
            MessageBox.Show("Enter a color as #RRGGBB, or type none.", "Tab Color",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

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

    private static bool TryParseHexColor(string text, out CellColor? color)
    {
        color = null;
        var normalized = text.Trim();
        if (normalized.StartsWith('#'))
            normalized = normalized[1..];
        if (normalized.Length != 6 ||
            !byte.TryParse(normalized[..2], System.Globalization.NumberStyles.HexNumber, null, out var r) ||
            !byte.TryParse(normalized[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g) ||
            !byte.TryParse(normalized[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            return false;
        }

        color = new CellColor(r, g, b);
        return true;
    }

    // ── Help tab ──────────────────────────────────────────────────────────────

    private void HelpOnlineBtn_Click(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        { FileName = AppInfo.HelpUrl, UseShellExecute = true });
    }

    private void AboutBtn_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            AppInfo.AboutText,
            "About Freexcel", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SendFeedbackBtn_Click(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        { FileName = AppInfo.FeedbackUrl, UseShellExecute = true });
    }

    private void ExportAsXps(string xpsPath)
    {
        try
        {
            var doc = PrintRenderer.RenderWorksheet(_workbook, _currentSheetId, _viewportService);

            // Open the XPS package for write
            var pkg = System.IO.Packaging.Package.Open(
                xpsPath,
                System.IO.FileMode.Create,
                System.IO.FileAccess.ReadWrite);

            using var xpsDoc = new System.Windows.Xps.Packaging.XpsDocument(pkg);

            // XpsDocumentWriter(XpsDocument) is internal in ReachFramework; create it via reflection
            var writerType = typeof(System.Windows.Xps.XpsDocumentWriter);
            var ctor = writerType.GetConstructor(
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null,
                [typeof(System.Windows.Xps.Packaging.XpsDocument)],
                null);

            if (ctor == null)
                throw new InvalidOperationException("XpsDocumentWriter(XpsDocument) constructor not found in ReachFramework.");

            var writer = (System.Windows.Xps.XpsDocumentWriter)ctor.Invoke([xpsDoc]);
            writer.Write(doc.DocumentPaginator);
            // xpsDoc closed by 'using'

            MessageBox.Show(
                $"Saved XPS file:\n{xpsPath}",
                "Export XPS",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to save XPS file:\n{ex.Message}",
                "Export Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
