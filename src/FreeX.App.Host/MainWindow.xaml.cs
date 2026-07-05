using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using FreeX.Core.Model;
using FreeX.Core.Commands;
using FreeX.Core.Calc;
using FreeX.Core.IO;
using System.Collections.Generic;
using System.ComponentModel;
using FreeX.App.Presentation.GridInteraction;
using FreeX.App.Services;
using FreeX.App.UI;
using Free.Shared.Theme.Wpf;

namespace FreeX.App.Host;

/// <summary>
/// Main application window — the spreadsheet shell.
/// Coordinates between the engine and the UI components.
/// </summary>
public partial class MainWindow : Window, IWorkbookWindow
{
    private const double MaximizedSafeInsetDip = 8.0;
    private const double SheetTabNavScrollAmount = 140.0;
    private const double SheetTabScrollEpsilon = 0.5;
    private const double SheetTabOverlapWidth = 16.0;
    private const double SheetTabRightNavigationReserveWidth = 28.0;
    private const double SheetTabChromeHeight = 28.0;
    private const double SheetTabGridRuleTop = 0.5;
    private const double SheetTabGridRuleStrokeThickness = 1.0;
    private const int ResizeViewportRefreshDelayMilliseconds = 140;

    private readonly ILogger<MainWindow> _logger;
    private readonly IViewportService _viewportService;
    // ── Per-window document context: _workbookRef + _commandBus + _documentState ──
    // Each window owns its document's ref/bus/state. Windows opened via View > New Window
    // share the originating window's instances (several views of one document); File > Open
    // or File > New in such a view swaps in fresh instances (DetachFromSharedDocumentContext)
    // so the new document is independent of the siblings (H39). Not readonly for that reason.
    private ICommandBus _commandBus;
    private ICommandStackChangeNotifier? _commandStackChangeNotifier;
    private readonly IUserMessageService _messageService;
    private readonly RecalcEngine _recalcEngine;
    private readonly IEnumerable<IFileAdapter> _fileAdapters;
    private readonly IAppDiagnostics? _diagnostics;
    private readonly AppDiagnosticsMetadata _diagnosticsMetadata;
    private readonly AppDiagnosticsOptions _diagnosticsOptions;
    private readonly RibbonKeyTipMode _ribbonKeyTipMode = new();
    private readonly KeyboardCommandDispatcher _keyboardCommandDispatcher = new();
    private readonly StandaloneAltKeyTipTracker _standaloneAltKeyTipTracker = new();
    private RibbonKeyTipScope _ribbonKeyTipScope = RibbonKeyTipScope.None;
    private string _ribbonKeyTipSequence = "";
    private bool _legacyDataKeyTipSequence;
    private bool _legacyEditKeyTipSequence;
    private ContextMenu? _activeRibbonKeyTipMenu;
    private ItemsControl? _activeRibbonKeyTipItemsControl;
    private WorkbookRef _workbookRef;
    private Workbook _workbook;
    private SheetId _currentSheetId;
    private readonly System.Collections.ObjectModel.ObservableCollection<SheetTabViewModel> _sheetTabs = [];
    private readonly HashSet<SheetId> _groupedSheetIds = [];
    private SheetId? _sheetGroupAnchor;
    private SheetId? _dragSheetTabId;
    private System.Windows.Point _dragSheetTabStart;
    private int? _dragSheetTabPendingToIndex;
    private bool _activateSheetDialogOpenOrPending;
    private bool _suppressToolbarSync;
    private readonly ToolbarVisualStateCache _toolbarVisualStateCache = new();
    private ToolbarVisualState? _lastToolbarVisualState;
    private QuickAccessCommandState? _lastQuickAccessCommandState;
    private WorkbookId? _lastQuickAccessCommandStateWorkbookId;
    private readonly StatusBarStatsCache _statusBarStatsCache = new();
    private readonly StatusBarViewModelCache _statusBarDisplayStateCache =
        new(new ResourceKeyStatusBarTextProvider(UiText.Get));
    private Free.Shared.AppServices.StatusBarViewModel? _lastStatusBarDisplayState;
    // Cache for UpdateStatusStatsPanelAutomation: avoid the string[]/LINQ allocation when the inputs are unchanged.
    private Free.Shared.AppServices.StatusBarViewModel? _lastStatusBarAutomationState;
    private string? _lastStatusBarAutomationName;
    private readonly SparklineValueCache _sparklineValueCache = new();
    private ulong _navigationCacheRevision;
    private bool _suppressViewOptionSync;
    private bool _suppressAppViewOptionSync;
    private bool _isOpeningFile;
    private bool _isSavingFile;
    private bool _isExportingFile;
    // File name shown in the footer operation-progress message during an open/save (null when idle).
    private string? _operationProgressFileName;
    private CancellationTokenSource? _fileOperationCancellation;
    private Dictionary<UIElement, bool>? _fileOperationInputEnabledSnapshot;
    // ── Dirty / save-state cluster: canonical state lives in _documentState ──
    // These private properties delegate to the injected WorkbookDocumentState service.
    // They preserve the same names used across the 50-file partial-class surface so
    // all callers continue to compile without mass edits.
    //
    // Mutations go through the MarkWorkbookDirty / MarkWorkbookSaved methods in
    // MainWindow.WorkbookLifecycle.cs, which call _documentState directly.  The
    // read-only getters here allow all the partial files to read the same state.
    private bool _workbookDirty => _documentState.IsDirty;
    private bool _suppressClosePrompt
    {
        get => _documentState.SuppressClosePrompt;
        set => _documentState.SuppressClosePrompt = value;
    }
    private string? _currentFilePath
    {
        get => _documentState.CurrentFilePath;
        set => _documentState.SetCurrentFilePath(value);
    }
    private int _workbookDirtyGeneration => _documentState.DirtyGeneration;
    private bool _closeAfterSaveInProgress;
    private CellAddress? _selectionAnchor;
    private CellAddress? _selectionCursor;
    private ExcelSelectionMode _selectionMode = ExcelSelectionMode.Normal;
    // Remembers each sheet's selection within this window so switching sheets restores it (Excel parity).
    private readonly FreeX.Core.Commands.WorksheetSelectionStore _worksheetSelections = new();
    private bool _endMode;
    // Captured from GridView.AutofillModifiersResolved immediately before the paired
    // AutofillRequested call, so OnAutofillRequested can pass Excel's Ctrl-flip state
    // (copy<->series) into AutofillCommand.
    private bool _autofillCtrlHeld;
    private bool _dragSelectActive;
    private bool _dragSelectAddsAdditionalRange;
    private bool _dragSelectionTransientOverlaysCleared;
    private GridHeaderContextMenuTarget? _dragHeaderSelectionTarget;
    private uint _dragHeaderSelectionAnchor;
    private bool _dragSelectStatusRefreshPending;
    private bool _dragSelectToolbarRefreshPending;
    private FreeX.App.UI.SplitPaneRegion _activeSplitPaneRegion = FreeX.App.UI.SplitPaneRegion.BottomRight;
    private readonly Dictionary<SheetId, SplitPaneViewportOffsets> _splitPaneViewportOffsets = [];
    private readonly List<FormulaTraceArrow> _formulaTraceArrows = [];
    private readonly RecentFilesStore _recentFiles;
    private readonly IWorkbookShareService _shareService = new WindowsWorkbookShareService();
    private List<RecentFileViewModel> _allRecentItems = [];
    private FreeXOptions _options;
    // _currentFilePath is declared as a delegating property in the dirty/save-state cluster above.
    private XlsxFeatureReport? _currentXlsxFeatureReport;
    private double _zoomLevel = 1.0;
    private bool _snapInProgress;
    private bool _suppressZoomSync;
    private bool _formulaBarExpanded;
    private bool _ribbonCompact;
    private bool _normalizingRibbonSurface;
    private readonly HashSet<TabItem> _normalizedRibbonStaticTabs = [];
    private string? _ribbonAdaptiveMeasurementCacheKey;
    private IReadOnlyList<RibbonAdaptiveGroup>? _ribbonAdaptiveGroupCache;
    private IReadOnlyList<string>? _ribbonAdaptiveGroupProfileKeyCache;
    private double _ribbonAdaptiveFixedChromeWidthCache;
    private string? _ribbonResizeThresholdCacheKey;
    private IReadOnlyList<double> _ribbonResizeThresholds = [];
    private double _lastRibbonResizeWidth = double.NaN;
    private bool _ribbonResizeNormalizationRequired = true;
    private RibbonAppliedStateKey? _lastRibbonAdaptiveAppliedStateKey;
    private string? _ribbonAdaptiveControlCacheKey;
    private readonly Dictionary<TabItem, RibbonActivePanelCacheEntry> _ribbonAdaptiveActivePanelCacheByTab = [];
    private StackPanel? _ribbonAdaptiveControlCachePanel;
    private TabItem? _ribbonAdaptiveControlCacheTab;
    private ScrollViewer? _ribbonAdaptiveScrollViewerCache;
    private IReadOnlyList<FrameworkElement>? _ribbonAdaptiveGroupControlCache;
    private IReadOnlyList<Button>? _ribbonAdaptiveCollapsedButtonCache;
    private string? _ribbonCompactSnapshotCacheKey;
    private IReadOnlyList<RibbonCompactGroupSnapshot>? _ribbonCompactGroupSnapshotCache;
    private IReadOnlyList<RibbonAdaptiveGroupState>? _lastRibbonAdaptiveAppliedStates;
    private RibbonCollapsedGroupFootprintMode? _lastRibbonCollapsedFootprintMode;
    private string? _ribbonAdaptiveLayoutPlanCacheKey;
    private readonly Dictionary<RibbonAdaptiveLayoutPlanCacheEntryKey, RibbonAdaptiveLayoutResult> _ribbonAdaptiveLayoutPlanCache = [];
    private readonly Dictionary<RibbonCorrectionCacheKey, IReadOnlyList<RibbonAdaptiveGroupState>> _ribbonCorrectedStateCache = [];
    private readonly Dictionary<RibbonMeasuredOverflowCacheKey, bool> _ribbonMeasuredOverflowCache = [];
    private bool _ribbonAdaptiveStateDiffInvalidated;
    private int _ribbonAdaptiveMeasurementInvalidationCount;
    private int _ribbonAdaptiveGroupMeasurementCount;
    private int _ribbonCompactSnapshotCaptureCount;
    private int _ribbonResizeThresholdRebuildCount;
    private int _ribbonAdaptiveLayoutPlanComputeCount;
    private int _ribbonAdaptiveLayoutPlanCacheHitCount;
    private int _ribbonMeasuredOverflowMeasurementCount;
    private int _ribbonCorrectedStateCacheHitCount;
    private int _ribbonAppliedStateSkipCount;
    private int _ribbonAdaptiveStateApplyCount;
    private int _ribbonAdaptiveStateChangedGroupCount;
    private int _ribbonCollapsedFootprintApplyCount;
    private bool _ribbonFallbackPending;
    private RibbonFallbackWork _ribbonFallbackWork;
    private int _ribbonFallbackRequestCount;
    private int _ribbonFallbackPostedCount;
    private int _ribbonFallbackExecutedCount;
    private int _ribbonFallbackForcedNormalizeCount;
    private int _ribbonFallbackForcedCompactCount;
    private int _ribbonFallbackSkippedCompactLayoutCount;
    private int _ribbonFirstFrameLayoutUpdateCount;
    private RibbonFallbackWork _lastRibbonFallbackRequestedWork;
    private RibbonFallbackWork _lastRibbonFallbackMergedWork;
    private RibbonFallbackWork _lastRibbonFallbackExecutedWork;
    private RibbonAppliedStateKey? _queuedRibbonCompactFallbackStateKey;
    private bool _suppressRibbonSelectionChangedNormalization;
    private bool _ribbonResizeCompactionPendingOnExit;
    private bool _resizeViewportRefreshPending;
    private bool _isInWindowResizeMoveLoop;
    private int _resizeViewportRefreshGeneration;
    private System.Windows.Threading.DispatcherTimer? _resizeViewportRefreshTimer;
    private CellColor _borderPickerColor = CellColor.Black;
    private BorderStyle _borderPickerStyle = BorderStyle.Thin;
    private RibbonBorderPreset _selectedBorderPreset = RibbonBorderPreset.All;
    private BorderDrawMode _borderDrawMode;
    private static readonly CellColor RibbonDefaultFillColor = new(255, 255, 0);
    private static readonly CellColor RibbonDefaultFontColor = new(255, 0, 0);
    private CellColor? _selectedFillColor = RibbonDefaultFillColor;
    private CellColor _selectedFontColor = RibbonDefaultFontColor;
    private bool _currentShapeHasFill = true;
    private CellColor? _currentShapeFillColor;
    private CellColor? _currentShapeOutlineColor;
    private readonly IReadOnlyList<System.Windows.Media.Brush> _formulaReferenceBrushes =
    [
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(32, 112, 214)),
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(192, 80, 77)),
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(112, 48, 160)),
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 128, 64)),
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(237, 125, 49)),
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 153, 153))
    ];
    private System.Windows.Controls.TextBox? _inlineEditor;
    private System.Windows.Controls.Border? _inlineEditorChrome;
    private FormulaEditorRect? _inlineEditorChromeBaseRect;
    private System.Windows.Controls.TextBlock? _inlineFormulaReferenceOverlay;
    private System.Windows.Controls.TextBox? _textBoxInlineEditor;
    private System.Windows.Controls.Border? _textBoxInlineEditorChrome;
    private Guid? _textBoxInlineEditingId;
    private string? _textBoxInlineOriginalText;
    private bool _syncingFormulaEditorText;
    private System.Windows.Controls.ComboBox? _validationDropdown;
    private System.Windows.Controls.Border? _dvInputMessageBorder;
    // The modeless AutoFilter dropdown flyout (a separate window) and the sheet it was opened on,
    // so a sheet switch can dismiss it instead of leaving it floating over the new sheet.
    private AutoFilterDialog? _autoFilterDropdown;
    private SheetId _autoFilterDropdownSheetId;
    private CellAddress? _formulaEditCell;
    private CellAddress? _formulaRangeSelectionAnchor;
    private int? _formulaReferenceStart;
    private int? _formulaReferenceLength;
    private bool _formulaRangeEntryMode;
    // Border pool: Borders are allocated once and reused across keystrokes (show/hide + reposition)
    // instead of being created and removed on every refresh. _formulaReferenceGridOverlayActiveCount
    // tracks how many pool entries are currently visible.
    private readonly List<System.Windows.Controls.Border> _formulaReferenceGridOverlayPool = [];
    private int _formulaReferenceGridOverlayActiveCount;
    private WatchWindowDialog? _watchWindowDialog;
    private bool _suppressValidationDropdownCommit;
    private GridResizePreviewSnapshot? _columnResizeSnapshot;
    private GridResizePreviewSnapshot? _rowResizeSnapshot;
    private Action<CommandOutcome>? _repeatPostAction;
    private string? _pivotFieldMenuContextCaption;
    private PivotFieldDropZone? _pivotFieldMenuContextZone;
    private PivotFieldDropZone? _pivotFieldDragSourceZone;
    private bool _pivotFieldDragRemoveCueActive;
    private IReadOnlyDictionary<(uint Row, uint Col), PivotHeaderDropdownTarget> _pivotHeaderDropdownTargets =
        new Dictionary<(uint Row, uint Col), PivotHeaderDropdownTarget>();
    private bool _slicerTimelinePaneDismissed;
    private readonly WorkbookWindowRegistry? _windowRegistry;
    private string _windowTitleSuffix = string.Empty;
    private bool _adoptSharedWorkbookOnLoad;
    private bool _suppressScrollBroadcast;

    // ── Per-document save/dirty state service (shared by the views of one document) ──
    private WorkbookDocumentState _documentState;
    private readonly NewWorkbookNameSequence _newWorkbookNameSequence;

    public MainWindow(
        ILogger<MainWindow> logger,
        IViewportService viewportService,
        ICommandBus commandBus,
        RecalcEngine recalcEngine,
        IEnumerable<IFileAdapter> fileAdapters,
        WorkbookRef workbookRef,
        Workbook workbook,
        IUserMessageService messageService,
        WorkbookDocumentState? documentState = null,
        IAppDiagnostics? diagnostics = null,
        AppDiagnosticsMetadata? diagnosticsMetadata = null,
        AppDiagnosticsOptions? diagnosticsOptions = null,
        FreeXOptions? options = null,
        WorkbookWindowRegistry? windowRegistry = null,
        NewWorkbookNameSequence? newWorkbookNameSequence = null)
    {
        // The MainWindow DI factory supplies a fresh per-document WorkbookDocumentState (View >
        // New Window passes the originating window's instead); tests that omit it get a default.
        _documentState = documentState ?? new WorkbookDocumentState();
        _newWorkbookNameSequence = newWorkbookNameSequence ?? new NewWorkbookNameSequence();
        _logger = logger;
        _viewportService = viewportService;
        _commandBus = commandBus;
        _commandStackChangeNotifier = commandBus as ICommandStackChangeNotifier;
        _messageService = messageService;
        _recalcEngine = recalcEngine;
        _fileAdapters = fileAdapters;
        _diagnostics = diagnostics;
        _diagnosticsMetadata = diagnosticsMetadata ?? AppDiagnosticsMetadata.Create(AppInfo.VersionText);
        _diagnosticsOptions = diagnosticsOptions ?? AppDiagnosticsOptions.CreateDefault();
        _workbookRef = workbookRef;
        _workbook = workbook;
        _currentSheetId = _workbook.Sheets[0].Id;
        _options = options ?? FreeXOptions.Load();
        _windowRegistry = windowRegistry;
        // A window handed a workbook that a registered window already views is a secondary view
        // of that document (View > New Window passed the originating window's context); it must
        // adopt the shared workbook on load instead of creating a fresh one. A window built with
        // its own fresh context (app startup, startup recovery, command-line file arguments)
        // never matches a registered window's document and initializes its own workbook (H39).
        _adoptSharedWorkbookOnLoad = windowRegistry?.HasWindowForDocument(workbook.Id) == true;
        _recentFiles = RecentFilesStore.Load();

        InitializeComponent();
        ApplySisterAppClientFrameContractRows();
        ConfigureStatusZoomSlider();
        // Merge the active brand theme into this window's own resources (as the last entry so it
        // overrides same-keyed brushes from ThemeResources.xaml merged earlier in this dict).
        // DynamicResource references in the title-bar chrome then resolve to these token brushes,
        // making the title bar runtime-swappable.  For the default theme the values are
        // byte-identical to ThemeResources.xaml, so the visual result is unchanged.
        if (App.TryGetServices(out _))
            WpfThemeApplier.Apply(Resources, App.ActiveTheme, "FreeX");
        InitializeInsertShapeGalleryContextMenu();
        if (_commandStackChangeNotifier is not null)
            _commandStackChangeNotifier.StackChanged += CommandStackChangeNotifier_StackChanged;

        RibbonMenuIconSeeder.Register();
        RebuildQuickAccessToolbar();
        InitializeQuickAccessToolbarCustomizationContextMenus();
        ConfigureBackstageHomePaneDescriptors();
        ConfigureBackstageInfoActionButtons();
        InitializeBackstageFrame();
        RegisterKeyboardCommandShortcuts();

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
        SheetGrid.ColumnAutoFitRequested += OnColumnAutoFitRequested;
        SheetGrid.RowAutoFitRequested += OnRowAutoFitRequested;
        SheetGrid.ColumnResizing += OnColumnResizing;
        SheetGrid.RowResizing    += OnRowResizing;
        SheetGrid.ResizeCanceled += OnResizeCanceled;
        SheetGrid.AutofillModifiersResolved += ctrlHeld => _autofillCtrlHeld = ctrlHeld;
        SheetGrid.AutofillRequested += OnAutofillRequested;
        SheetGrid.AutofillEdgeScrollRequested += OnAutofillEdgeScrollRequested;
        SheetGrid.SelectionMoveRequested += OnSelectionMoveRequested;
        SheetGrid.ContextMenuRequested += OnGridContextMenuRequested;
        SheetGrid.HeaderContextMenuRequested += OnGridHeaderContextMenuRequested;
        SheetGrid.AutoFilterDropdownRequested += OnAutoFilterDropdownRequested;
        SheetGrid.PivotHeaderDropdownRequested += OnPivotHeaderDropdownRequested;
        SheetGrid.OutlineGroupToggleRequested += OnOutlineGroupToggleRequested;
        SheetGrid.PivotChartFieldButtonRequested += OnPivotChartFieldButtonRequested;
        SheetGrid.WaterfallChartPointContextMenuRequested += OnWaterfallChartPointContextMenuRequested;
        SheetGrid.PageMarginsChanged += OnPageMarginsChanged;
        SheetGrid.SplitDividerMoved += OnSplitDividerMoved;
        SheetGrid.SplitPaneScrollbarScrolled += OnSplitPaneScrollbarScrolled;
        SheetGrid.ObjectMoved   += OnObjectMoved;
        SheetGrid.ChartBoundsChanged += OnChartBoundsChanged;
        SheetGrid.ObjectResized += OnObjectResized;
        SheetGrid.ObjectResizedWithAnchor += OnObjectResizedWithAnchor;
        SheetGrid.ObjectRotated += OnObjectRotated;
        SheetGrid.PictureCropped += OnPictureCropped;
        SheetGrid.NoteInlineEditSubmitted += SheetGrid_NoteInlineEditSubmitted;
        SheetGrid.ThreadedCommentInlineEditSubmitted += SheetGrid_ThreadedCommentInlineEditSubmitted;
        SheetGrid.TextBoxEditRequested += OnTextBoxEditRequested;
        SheetGrid.NativeSlicerClearFilterRequested += OnNativeSlicerClearFilterRequested;
        SheetGrid.NativeSlicerTileToggleRequested += OnNativeSlicerTileToggleRequested;
        SheetGrid.NativeTimelineClearFilterRequested += OnNativeTimelineClearFilterRequested;
        SheetGrid.NativeTimelineGranularityToggleRequested += OnNativeTimelineGranularityToggleRequested;
        SheetGrid.NativeTimelineRangeRequested += OnNativeTimelineRangeRequested;
        WireFormControlEvents();
        DependencyPropertyDescriptor.FromProperty(
            FreeX.App.UI.GridView.SelectedObjectIdProperty,
            typeof(FreeX.App.UI.GridView))?.AddValueChanged(SheetGrid, OnSelectedObjectContextChanged);
        DependencyPropertyDescriptor.FromProperty(
            FreeX.App.UI.GridView.SelectedObjectKindProperty,
            typeof(FreeX.App.UI.GridView))?.AddValueChanged(SheetGrid, OnSelectedObjectContextChanged);
        SheetGrid.MouseMove  += SheetGrid_MouseMove;
        SheetGrid.MouseUp    += SheetGrid_MouseUp;
        SheetGrid.LostMouseCapture += SheetGrid_LostMouseCapture;
        SheetGrid.MouseWheel += SheetGrid_MouseWheel;
        this.PreviewKeyDown += MainWindow_PreviewKeyDown;
        this.KeyDown += MainWindow_KeyDown;
        this.KeyUp += MainWindow_KeyUp;
        this.Deactivated += MainWindow_Deactivated;
        this.TextInput += MainWindow_TextInput;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
        FormulaBar.GotKeyboardFocus += (_, _) => CaptureFormulaEditCell();
        FormulaBar.TextChanged += (_, _) =>
        {
            SyncInlineEditorTextFromFormulaBar();
            UpdateFormulaRangeEntryStateAfterTextChanged(FormulaBar);

            var formulaBarHasFocus = ReferenceEquals(System.Windows.Input.Keyboard.FocusedElement, FormulaBar);
            if (!formulaBarHasFocus && _inlineEditor?.IsVisible != true)
            {
                ClearFormulaReferenceHighlights();
                return;
            }

            RefreshFormulaReferenceHighlights();
        };
        
        Loaded += MainWindow_Loaded;
        Loaded += (_, _) => UpdateMaxRestoreButtonState();
        SizeChanged += MainWindow_SizeChanged;
        StateChanged += (_, _) =>
        {
            UpdateMaximizedContentInset();
            UpdateMaxRestoreButtonState();
        };

        _logger.LogInformation("MainWindow initialized with Workbook {WorkbookId}", _workbook.Id);
    }

    private void RecordDiagnosticEvent(string eventName, IReadOnlyDictionary<string, string?>? properties = null) =>
        _diagnostics?.RecordEvent(eventName, properties);

    private void CommandStackChangeNotifier_StackChanged(object? sender, CommandStackChangedEventArgs e)
    {
        if (e.WorkbookId != _workbook.Id)
            return;

        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => CommandStackChangeNotifier_StackChanged(sender, e));
            return;
        }

        RefreshQuickAccessToolbarCommandStates();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        if (_commandStackChangeNotifier is not null)
            _commandStackChangeNotifier.StackChanged -= CommandStackChangeNotifier_StackChanged;
    }

    private void UpdateMaxRestoreButtonState()
    {
        if (MaxRestoreIcon is null || MaxRestoreBtn is null)
            return;

        var isMaximized = WindowState == WindowState.Maximized;
        MaxRestoreIcon.Kind = isMaximized
            ? RibbonCommandIconKind.WindowRestore
            : RibbonCommandIconKind.WindowMaximize;
        if (!ReferenceEquals(MaxRestoreBtn.Content, MaxRestoreIcon))
            MaxRestoreBtn.Content = MaxRestoreIcon;
        System.Windows.Automation.AutomationProperties.SetName(
            MaxRestoreBtn,
            UiText.Get(isMaximized
                ? "MainWindow_AutomationName_RestoreDown"
                : "MainWindow_AutomationName_Maximize"));
        System.Windows.Automation.AutomationProperties.SetHelpText(
            MaxRestoreBtn,
            UiText.Get(isMaximized
                ? "MainWindow_AutomationName_RestoreDown"
                : "MainWindow_AutomationName_Maximize"));
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

