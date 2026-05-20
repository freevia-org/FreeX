using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using Freexcel.Core.Model;
using Freexcel.Core.Commands;
using Freexcel.Core.Calc;
using Freexcel.Core.IO;
using System.Collections.Generic;
using System.Linq;

namespace Freexcel.App.Host;

/// <summary>
/// Main application window — the spreadsheet shell.
/// Coordinates between the engine and the UI components.
/// </summary>
public partial class MainWindow : Window
{
    private const double MaximizedSafeInsetDip = 8.0;
    private const double SheetTabNavScrollAmount = 140.0;
    private const double SheetTabScrollEpsilon = 0.5;

    private readonly ILogger<MainWindow> _logger;
    private readonly IViewportService _viewportService;
    private readonly ICommandBus _commandBus;
    private readonly RecalcEngine _recalcEngine;
    private readonly IEnumerable<IFileAdapter> _fileAdapters;
    private readonly RibbonKeyTipMode _ribbonKeyTipMode = new();
    private readonly KeyboardCommandDispatcher _keyboardCommandDispatcher = new();
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
    private ExcelSelectionMode _selectionMode = ExcelSelectionMode.Normal;
    private bool _endMode;
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
    private double _zoomLevel = 1.0;
    private bool _snapInProgress;
    private bool _suppressZoomSync;
    private bool _formulaBarExpanded;
    private bool _ribbonCompact;
    private bool _normalizingRibbonSurface;
    private CellColor _borderPickerColor = CellColor.Black;
    private BorderStyle _borderPickerStyle = BorderStyle.Thin;
    private static readonly (string Label, string Code)[] NumberFormatOptions =
    [
        ("General", "General"),
        ("Number (0.00)", "0.00"),
        ("Currency ($#,##0.00)", "$#,##0.00"),
        ("Accounting ($#,##0.00)", "_($* #,##0.00_);_($* (#,##0.00);_($* \"-\"??_);_(@_)"),
        ("Percentage (0%)", "0%"),
        ("Fraction (# ?/?)", "# ?/?"),
        ("Scientific (0.00E+00)", "0.00E+00"),
        ("Date (yyyy-MM-dd)", "yyyy-MM-dd"),
        ("Time (HH:mm:ss)", "HH:mm:ss"),
        ("Text (@)", "@")
    ];
    private System.Windows.Controls.TextBox? _inlineEditor;
    private System.Windows.Controls.ComboBox? _validationDropdown;
    private WatchWindowDialog? _watchWindowDialog;
    private bool _suppressValidationDropdownCommit;
    private ColumnResizeSnapshot? _columnResizeSnapshot;
    private RowResizeSnapshot? _rowResizeSnapshot;
    private Action<CommandOutcome>? _repeatPostAction;
    private string? _pivotChartContextFieldCaption;
    private bool _slicerTimelinePaneDismissed;

    private sealed record ColumnResizeSnapshot(SheetId SheetId, uint StartCol, uint EndCol, Dictionary<uint, (bool Had, double Width)> Widths);
    private sealed record RowResizeSnapshot(SheetId SheetId, uint StartRow, uint EndRow, Dictionary<uint, (bool Had, double Height)> Heights);
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
        RegisterKeyboardCommandShortcuts();

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
        this.PreviewKeyDown += MainWindow_PreviewKeyDown;
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

        NumberFormatBox.ItemsSource = NumberFormatOptions.Select(option => option.Label).ToArray();
        NumberFormatBox.SelectedIndex = 0;

        PopulateFormatTableGalleryMenu();
        ApplyOptionsToView();
        NormalizeRibbonSurface(forceCompact: true);
        CreateNewWorkbook();
        UpdateViewport();
        RefreshSheetTabs();
        UpdateTitleBar();
    }

    // ── Header / select-all helpers ───────────────────────────────────────────

    // ── Ribbon cells (insert / delete rows & columns) ────────────────────────

    // ── Print / Export ────────────────────────────────────────────────────────

    // ── Format Painter ───────────────────────────────────────────────────────

    // ── Insert tab ────────────────────────────────────────────────────────────

    // ── Draw tab stubs ────────────────────────────────────────────────────────

    // ── Data tab additions ────────────────────────────────────────────────────

    // ── View tab ─────────────────────────────────────────────────────────────

    // ── QAT / title bar ──────────────────────────────────────────────────────

    // ── Formula bar expand chevron ────────────────────────────────────────────

    // ── Sheet tab nav arrows ──────────────────────────────────────────────────

    // ── Help tab ──────────────────────────────────────────────────────────────



}

