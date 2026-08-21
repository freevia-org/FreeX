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
using FreeX.App.Presentation.FormulaBar;
using FreeX.App.Presentation.Dialogs;
using FreeX.App.Presentation.Editing;
using FreeX.App.Presentation.Ribbon;
using FreeX.App.Presentation.PivotUI;
using FreeX.App.Presentation.Sparklines;
using FreeX.App.Services;
using FreeX.App.UI;
using Free.Shared.Theme.Wpf;
using Free.Shared.Shell.Wpf;

namespace FreeX.App.Host;

/// <summary>
/// Main application window — the spreadsheet shell.
/// Coordinates between the engine and the UI components.
/// </summary>
public partial class MainWindow : Window, IWorkbookWindow, IFormulaPointModeWorkbookWindow
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
    private WorkbookDocumentContext _documentContext;
    private readonly IUserMessageService _messageService;
    private readonly RecalcEngine _recalcEngine;
    private readonly IEnumerable<IFileAdapter> _fileAdapters;
    private readonly IAppDiagnostics? _diagnostics;
    private readonly AppDiagnosticsMetadata _diagnosticsMetadata;
    private readonly AppDiagnosticsOptions _diagnosticsOptions;
    private readonly FreeXRibbonKeyTipInputSession _ribbonKeyTipSession = new();
    private readonly KeyboardCommandDispatcher _keyboardCommandDispatcher = new();
    private readonly WorkbookSessionFactory _sessionFactory =
        new(FindReplaceDialogSchema.ResolvePolicyText(UiText.Get));
    private WorkbookSession _session;
    private bool _workbookSessionDisposed;
    private readonly StandaloneAltKeyTipTracker _standaloneAltKeyTipTracker = new();
    private ContextMenu? _activeRibbonKeyTipMenu;
    private ItemsControl? _activeRibbonKeyTipItemsControl;
    // Preserve the established partial-class name while keeping WorkbookSession authoritative.
    private Workbook _workbook => _session.Workbook;
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
    private readonly WorkbookSelectionStatsCache _statusBarStatsCache = new();
    private readonly StatusBarViewModelCache _statusBarDisplayStateCache =
        new(new ResourceKeyStatusBarTextProvider(UiText.Get));
    private Free.Shared.AppServices.StatusBarViewModel? _lastStatusBarDisplayState;
    private Free.Shared.AppServices.StatusBarAutomationSnapshot? _lastStatusBarAutomationSnapshot;
    private readonly SparklineValueCache _sparklineValueCache = new();
    private ulong _navigationCacheRevision;
    private bool _suppressViewOptionSync;
    private bool _suppressAppViewOptionSync;
    private bool _isOpeningFile;
    private bool _isSavingFile;
    private bool _isExportingFile;
    // File name shown in the footer operation-progress message during an open/save (null when idle).
    private string? _operationProgressFileName;
    private readonly FileOperationCancellationSession _fileOperationCancellationSession = new();
    private readonly WorkbookReadOnlySession _workbookReadOnlySession = new();
    private Dictionary<UIElement, bool>? _fileOperationInputEnabledSnapshot;
    // Reentrant hold count backing the save-input gate (see AdjustSaveGate in
    // MainWindow.Backstage.cs): incremented both when THIS window starts its own save and when a
    // "New Window" sibling's save broadcasts the gate into this window, so the input surface is
    // only re-enabled once every hold on it has released (R115-app-host-save-race).
    private int _saveGateHoldCount;
    // Dirty/save state is owned by WorkbookSession. These private properties preserve the names
    // used across the WPF partial-class surface while that renderer is migrated incrementally.
    // They preserve the same names used across the 50-file partial-class surface so
    // all callers continue to compile without mass edits.
    //
    // Mutations go through MainWindow.WorkbookLifecycle.cs and delegate to the session.
    private bool _workbookDirty => _session.IsDirty;
    private bool _suppressClosePrompt
    {
        get => _session.SuppressClosePrompt;
        set => _session.SuppressClosePrompt = value;
    }
    private string? _currentFilePath
    {
        get => _session.CurrentFilePath;
        set => _session.SetCurrentFilePathFromHost(value);
    }
    private int _workbookDirtyGeneration => _session.DirtyGeneration;
    private bool _closeAfterSaveInProgress;
    private CellAddress? _selectionAnchorField;
    // The true active/anchor cell of the current selection (e.g. where a Shift+arrow
    // extension started; F2/typing edits this cell — see MainWindow.Editing.cs). Kept mirrored
    // onto SheetGrid.ActiveCell so the grid's UI Automation peer can announce the real active
    // cell instead of SelectedRange's normalized top-left Start corner, which is a different
    // cell whenever the selection was extended upward or leftward (R14-accessibility-automation-2).
    private CellAddress? _selectionAnchor
    {
        get => _selectionAnchorField;
        set
        {
            _selectionAnchorField = value;
            SheetGrid.ActiveCell = value;
        }
    }
    private CellAddress? _selectionCursor;
    private ExcelSelectionMode _selectionMode = ExcelSelectionMode.Normal;
    // Remembers each sheet's selection within this window so switching sheets restores it (Excel parity).
    private readonly FreeX.Core.Commands.WorksheetSelectionStore _worksheetSelections = new();
    // Remembers each sheet's view mode/zoom within THIS window, independent of any other window
    // viewing the same document (Excel "New Window" gives every window its own view --
    // R83-app-view-modes-5-1).
    private readonly FreeX.Core.Commands.WorksheetViewStateStore _worksheetViewStates = new();
    private bool _endMode;
    // Captured from GridView.AutofillModifiersResolved immediately before the paired
    // AutofillRequested call, so OnAutofillRequested can pass Excel's Ctrl-flip state
    // (copy<->series) into AutofillCommand.
    private bool _autofillCtrlHeld;
    // Captured from GridView.SelectionMoveModifiersResolved immediately before the paired
    // SelectionMoveRequested call, so OnSelectionMoveRequested can tell Excel's Ctrl-drag-to-copy
    // gesture apart from an ordinary (destructive) move.
    private bool _selectionMoveCtrlHeld;
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
    // File.Exists on a recent entry can block for the SMB/TCP connect timeout when the path is an
    // unreachable UNC/network share; UpdateSsRecentList (MainWindow.Backstage.cs) re-runs the
    // existence check for every recent entry on every Recent-files search-box keystroke, so a raw
    // File.Exists there would freeze the UI per character typed. This cache probes off the UI
    // thread and reuses the cached result across keystrokes. See MainWindow.Backstage.cs.
    private readonly RecentFilePathExistenceCache _recentFilePathExistenceCache;
    private readonly WorkbookFileWorkflow _fileWorkflow;
    private readonly IWorkbookShareService _shareService = new WindowsWorkbookShareService();
    private List<RecentFileViewModel> _allRecentItems = [];
    private readonly FreeXOptionsRuntimeSession _optionsRuntimeSession;
    private AppOptions _options;
    // _currentFilePath is declared as a delegating property in the dirty/save-state cluster above.
    private XlsxFeatureReport? _currentXlsxFeatureReport;
    // Snapshot of _currentFilePath's on-disk write time taken at open (OpenWorkbookResult.
    // SourceLastWriteTimeUtc), threaded into WorkbookSaveService.SaveAsync's expectedLastWriteTimeUtc
    // so a save detects the file having been changed externally since open and warns instead of
    // silently overwriting (WorkbookExternallyModifiedException). Null disables the guard (new/
    // recovery-opened workbooks that have no meaningful "loaded from disk at time T" to compare).
    private DateTime? _currentFileSourceLastWriteTimeUtc;
    private double _zoomLevel = 1.0;
    private bool _snapInProgress;
    private bool _suppressZoomSync;
    private bool _formulaBarExpanded;
    private bool _normalizingRibbonSurface;
    private readonly HashSet<TabItem> _normalizedRibbonStaticTabs = [];
    private bool _suppressRibbonSelectionChangedNormalization;
    private bool _resizeViewportRefreshPending;
    private bool _isInWindowResizeMoveLoop;
    private int _resizeViewportRefreshGeneration;
    private System.Windows.Threading.DispatcherTimer? _resizeViewportRefreshTimer;
    private readonly BorderPickerSession _borderPickerSession = new();
    private RibbonBorderPreset _selectedBorderPreset = RibbonBorderPreset.All;
    private static readonly CellColor RibbonDefaultFillColor = new(255, 255, 0);
    private static readonly CellColor RibbonDefaultFontColor = new(255, 0, 0);
    private CellColor? _selectedFillColor = RibbonDefaultFillColor;
    private CellColor _selectedFontColor = RibbonDefaultFontColor;
    // R142-services-theme-colors-1: the theme slot/tint of the last color picked from the ribbon's
    // Font/Fill Color gallery, when it came from a Theme Colors swatch (see
    // MainWindow.HomeFormatting.cs FontColorPickerBtn_Click/FillColorPickerBtn_Click/
    // ApplySelectedFontColor/ApplySelectedFillColor). Null whenever the swatch/color/default in use
    // is not theme-linked.
    private WorkbookThemeColorReference? _selectedFontThemeColor;
    private WorkbookThemeColorReference? _selectedFillThemeColor;
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
    // R78-render-inplace-editor-5-3: the single-row (one line of text) height the inline editor
    // was opened at, kept separate from _inlineEditorChromeBaseRect.Height (which grows on every
    // keystroke to fit the current line count) so each recompute always multiplies from the same
    // fixed per-line unit instead of compounding off the previous pass's already-grown height.
    private double _inlineEditorSingleLineHeight;
    private System.Windows.Controls.TextBlock? _inlineFormulaReferenceOverlay;
    private System.Windows.Controls.TextBox? _textBoxInlineEditor;
    private System.Windows.Controls.Border? _textBoxInlineEditorChrome;
    private bool _syncingFormulaEditorText;
    private bool _isApplyingFormulaEditorText;
    private System.Windows.Controls.ComboBox? _validationDropdown;
    private System.Windows.Controls.Border? _dvInputMessageBorder;
    // The modeless AutoFilter dropdown flyout (a separate window) and the sheet it was opened on,
    // so a sheet switch can dismiss it instead of leaving it floating over the new sheet.
    private AutoFilterDialog? _autoFilterDropdown;
    private SheetId _autoFilterDropdownSheetId;
    private CellAddress? _formulaEditCell;
    private readonly FormulaRangeEditingSession _formulaRangeEditingSession = new();
    // R78-render-inplace-editor-5-1: whether the current inline-edit session was opened via F2 /
    // double-click (Excel's "Edit" mode -- caret lands in existing content, arrows reposition it)
    // vs. by typing a fresh character over the selection (Excel's "Enter" mode -- arrows commit the
    // freshly-typed non-formula text and move the active cell). Set true in ShowInlineEditor
    // (F2/double-click's common path) and overridden false by MainWindow_TextInput's typed-entry.
    private bool _formulaEditEnteredViaEditKey;
    // Border pool: Borders are allocated once and reused across keystrokes (show/hide + reposition)
    // instead of being created and removed on every refresh. _formulaReferenceGridOverlayActiveCount
    // tracks how many pool entries are currently visible.
    private readonly List<System.Windows.Controls.Border> _formulaReferenceGridOverlayPool = [];
    private int _formulaReferenceGridOverlayActiveCount;
    // R91-formula-editing-assist-5-3: parallel pool for each highlight overlay's drag-resize grip,
    // plus the FormulaReferenceHighlight each active pool slot currently represents -- a grip's own
    // MouseDown handler looks this up (by the grip's index in the pool) to know which reference
    // (text span + GridRange) its drag should resize. Grown/hidden in lockstep with
    // _formulaReferenceGridOverlayPool in RefreshFormulaReferenceGridOverlays.
    private readonly List<System.Windows.Shapes.Rectangle> _formulaReferenceGridOverlayGripPool = [];
    private readonly List<FormulaReferenceHighlight?> _formulaReferenceGridOverlayHighlights = [];
    private System.Windows.Controls.TextBox? _formulaReferenceDragEditor;
    private WatchWindowDialog? _watchWindowDialog;
    private bool _suppressValidationDropdownCommit;
    private GridResizePreviewSnapshot? _columnResizeSnapshot;
    private GridResizePreviewSnapshot? _rowResizeSnapshot;
    private Action<CommandOutcome>? _repeatPostAction;
    private string? _pivotFieldMenuContextCaption;
    private PivotFieldBucket? _pivotFieldMenuContextZone;
    private PivotFieldBucket? _pivotFieldDragSourceZone;
    private bool _pivotFieldDragRemoveCueActive;
    private IReadOnlyDictionary<(uint Row, uint Col), PivotHeaderDropdownTarget> _pivotHeaderDropdownTargets =
        new Dictionary<(uint Row, uint Col), PivotHeaderDropdownTarget>();
    private bool _slicerTimelinePaneDismissed;
    private readonly WorkbookWindowRegistry? _windowRegistry;
    private string _windowTitleSuffix = string.Empty;
    private bool _adoptSharedWorkbookOnLoad;
    private bool _suppressScrollBroadcast;
    private readonly IPlatformClipboard _platformClipboard;

    // ── Per-document save/dirty state service (shared by the views of one document) ──
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
        AppOptions? options = null,
        WorkbookWindowRegistry? windowRegistry = null,
        NewWorkbookNameSequence? newWorkbookNameSequence = null,
        WorkbookSession? workbookSession = null,
        IPlatformClipboard? platformClipboard = null,
        FreeXOptionsRuntimeSession? optionsRuntimeSession = null,
        WorkbookClipboardSession? workbookClipboardSession = null)
        : this(
            logger,
            viewportService,
            recalcEngine,
            fileAdapters,
            WorkbookDocumentContext.Attach(workbookRef, commandBus, workbook),
            messageService,
            documentState,
            diagnostics,
            diagnosticsMetadata,
            diagnosticsOptions,
            options,
            windowRegistry,
            newWorkbookNameSequence,
            workbookSession,
            platformClipboard,
            optionsRuntimeSession,
            workbookClipboardSession)
    {
    }

    public MainWindow(
        ILogger<MainWindow> logger,
        IViewportService viewportService,
        RecalcEngine recalcEngine,
        IEnumerable<IFileAdapter> fileAdapters,
        WorkbookDocumentContext documentContext,
        IUserMessageService messageService,
        WorkbookDocumentState? documentState = null,
        IAppDiagnostics? diagnostics = null,
        AppDiagnosticsMetadata? diagnosticsMetadata = null,
        AppDiagnosticsOptions? diagnosticsOptions = null,
        AppOptions? options = null,
        WorkbookWindowRegistry? windowRegistry = null,
        NewWorkbookNameSequence? newWorkbookNameSequence = null,
        WorkbookSession? workbookSession = null,
        IPlatformClipboard? platformClipboard = null,
        FreeXOptionsRuntimeSession? optionsRuntimeSession = null,
        WorkbookClipboardSession? workbookClipboardSession = null)
    {
        // The MainWindow DI factory supplies a fresh per-document WorkbookDocumentState. Sibling
        // windows receive a WorkbookSession that already shares the originating document state.
        _newWorkbookNameSequence = newWorkbookNameSequence ?? new NewWorkbookNameSequence();
        // clip-2 (R143): the internal formula-preserving clipboard must be process-wide (matching
        // real Excel: copying in one open workbook and pasting into another open in the SAME
        // instance keeps the formula), not one fresh WorkbookClipboardSession per window. The DI
        // factory (App.xaml.cs ConfigureServices) registers WorkbookClipboardSession as a singleton
        // and ActivatorUtilities.CreateInstance<MainWindow> resolves this parameter from it, so
        // every window opened through Services.GetRequiredService<MainWindow>() shares the same
        // session. Callers that construct a MainWindow directly (every existing test) leave this
        // null and get an isolated per-instance session exactly as before -- no test behavior change.
        _workbookClipboardSession = workbookClipboardSession ?? new WorkbookClipboardSession();
        _logger = logger;
        _viewportService = viewportService;
        _documentContext = documentContext ?? throw new ArgumentNullException(nameof(documentContext));
        _messageService = messageService;
        _platformClipboard = platformClipboard ?? new WpfPlatformClipboard(
            Dispatcher,
            new WpfPlatformClipboardOptions(
                MaxWriteAttempts: 20,
                WriteRetryDelay: TimeSpan.FromMilliseconds(50),
                FlushAfterWrite: true,
                VerifyTextAfterWrite: true,
                VerifyImageAfterWrite: true,
                FallBackToText: true));
        _recalcEngine = recalcEngine;
        _fileAdapters = fileAdapters;
        _diagnostics = diagnostics;
        _diagnosticsMetadata = diagnosticsMetadata ?? AppDiagnosticsMetadata.Create(AppInfo.VersionText);
        _diagnosticsOptions = diagnosticsOptions ?? AppDiagnosticsOptions.CreateDefault();
        _session = workbookSession ?? _documentContext.CreateHostOwnedSession(
            _sessionFactory,
            new StartupWorkbookLoadResult(
                _documentContext.CurrentWorkbook,
                _documentContext.CurrentWorkbook.Name,
                "Initialized workbook.",
                IsFallback: false,
                SourcePath: documentState?.CurrentFilePath),
            recalcEngine,
            viewportService,
            fileAdapters,
            documentState ?? new WorkbookDocumentState(),
            viewportHeight: 1,
            viewportWidth: 1,
            includeObjects: true);
        if (!ReferenceEquals(_session.Workbook, _documentContext.CurrentWorkbook))
        {
            throw new ArgumentException(
                "The supplied workbook session must own the document context's workbook.",
                nameof(workbookSession));
        }
        _currentSheetId = _session.ActiveSheet.Id;
        ConfigureWorkbookSessionRendererAdapters();
        _optionsRuntimeSession = optionsRuntimeSession ?? new FreeXOptionsRuntimeSession(options);
        _options = _optionsRuntimeSession.LiveOptions;
        _windowRegistry = windowRegistry;
        // A window handed a workbook that a registered window already views is a secondary view
        // of that document (View > New Window passed the originating window's context); it must
        // adopt the shared workbook on load instead of creating a fresh one. A window built with
        // its own fresh context (app startup, startup recovery, command-line file arguments)
        // never matches a registered window's document and initializes its own workbook (H39).
        _adoptSharedWorkbookOnLoad = windowRegistry?.HasWindowForDocument(
            _documentContext.CurrentWorkbook.Id) == true;
        _recentFiles = RecentFilesStore.Load();
        // Refresh the Recent-files search box once a background probe resolves a path this window
        // hasn't checked yet, so a UNC/network entry that was optimistically shown as existing gets
        // dropped from view once we actually learn it's unreachable, without ever blocking the
        // keystroke that triggered the probe.
        _recentFilePathExistenceCache = new RecentFilePathExistenceCache(onProbed: _ =>
            Dispatcher.BeginInvoke(() =>
            {
                if (SsSearchBox is not null)
                    UpdateSsRecentList(SsSearchBox.Text);
            }));
        _fileWorkflow = CreateWorkbookFileWorkflow();

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
        _documentContext.CommandStackChanged += CommandStackChangeNotifier_StackChanged;

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
        SheetGrid.AutofillHandleDoubleClicked += OnAutofillHandleDoubleClicked;
        SheetGrid.SelectionMoveModifiersResolved += ctrlHeld => _selectionMoveCtrlHeld = ctrlHeld;
        SheetGrid.SelectionMoveRequested += OnSelectionMoveRequested;
        SheetGrid.ContextMenuRequested += OnGridContextMenuRequested;
        SheetGrid.HeaderContextMenuRequested += OnGridHeaderContextMenuRequested;
        SheetGrid.AutoFilterDropdownRequested += OnAutoFilterDropdownRequested;
        SheetGrid.PivotHeaderDropdownRequested += OnPivotHeaderDropdownRequested;
        SheetGrid.OutlineGroupToggleRequested += OnOutlineGroupToggleRequested;
        SheetGrid.OutlineLevelButtonRequested += OnOutlineLevelButtonRequested;
        SheetGrid.PivotChartFieldButtonRequested += OnPivotChartFieldButtonRequested;
        SheetGrid.WaterfallChartPointContextMenuRequested += OnWaterfallChartPointContextMenuRequested;
        SheetGrid.PageMarginsChanged += OnPageMarginsChanged;
        SheetGrid.PageBreakLineMoved += OnPageBreakLineMoved;
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
        FormulaBar.SelectionChanged += (_, _) =>
        {
            if (_isApplyingFormulaEditorText)
                return;

            ClearFormulaReferenceEntrySpanIfCaretLeftReference(FormulaBar);

            // R160-formula-editing-F1: SelectionChanged also fires on a caret-only move (arrow
            // keys, clicking to reposition) with no TextChanged alongside it, but the live
            // signature-help tooltip is caret-position-sensitive -- it re-bolds whichever argument
            // the caret currently sits inside and hides once the caret leaves the call entirely.
            // Refresh it here too, gated the same way as the TextChanged-driven refresh just below
            // (skip while the inline editor is the live editing surface; it owns the tooltip then).
            if (_inlineEditor?.IsVisible != true)
                RefreshFormulaSignatureHelp(FormulaBar);
        };
        FormulaBar.TextChanged += (_, _) =>
        {
            if (_isApplyingFormulaEditorText)
                return;

            SyncInlineEditorTextFromFormulaBar();
            UpdateFormulaRangeEntryStateAfterTextChanged(FormulaBar);

            var formulaBarHasFocus = ReferenceEquals(System.Windows.Input.Keyboard.FocusedElement, FormulaBar);
            if (!formulaBarHasFocus && _inlineEditor?.IsVisible != true)
            {
                ClearFormulaReferenceHighlights();
                return;
            }

            RefreshFormulaReferenceHighlights();

            // R88-app-autocomplete-picklist-5-3: Cell-value AutoComplete was only ever wired to the
            // inline in-cell editor's own TextChanged handler. Typing straight into the Formula Bar
            // (its own edit surface whenever EditActiveCellInFormulaBar begins an edit without also
            // showing the inline editor) never offered a suggestion. Only run this here when the
            // inline editor isn't the live editing surface -- it already applies (and syncs back to
            // the Formula Bar) its own suggestion, so re-running against the Formula Bar mid-sync
            // would fight over the selected suggestion tail.
            if (_inlineEditor?.IsVisible != true)
            {
                if (!_formulaRangeEditingSession.ConsumeCellValueAutocompleteSuppression())
                    ApplyCellValueAutoCompleteSuggestion(FormulaBar);

                // R91-formula-editing-assist-5-1/5-2: same function-name AutoComplete popup and
                // live signature tooltip as the inline in-cell editor, for the "clicked straight
                // into the Formula Bar" edit path where the inline editor never shows (see
                // EditActiveCellInFormulaBar in MainWindow.Editing.cs).
                RefreshFormulaFunctionAutocomplete(FormulaBar);
                RefreshFormulaSignatureHelp(FormulaBar);
            }
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

    internal WorkbookSession Session => _session;

    // R160-formula-editing-F1: test-only readback of the live signature-help tooltip's state, so a
    // headless test can assert which argument is bolded after a caret-only move (no TextChanged)
    // without reaching into the private popup/textblock fields declared in MainWindow.Editing.cs.
    internal bool FormulaSignatureHelpIsOpenForTest => _signatureHelpPopup?.IsOpen == true;

    internal string? FormulaSignatureHelpBoldArgumentForTest
    {
        get
        {
            if (_signatureHelpPopup?.IsOpen != true || _signatureHelpTextBlock is null)
                return null;

            foreach (var inline in _signatureHelpTextBlock.Inlines)
            {
                if (inline is System.Windows.Documents.Run { FontWeight: var weight } run
                    && weight == FontWeights.Bold)
                {
                    return run.Text;
                }
            }

            return null;
        }
    }

    private void RecordDiagnosticEvent(string eventName, IReadOnlyDictionary<string, string?>? properties = null) =>
        _diagnostics?.RecordEvent(eventName, properties);

    /// <summary>
    /// Handles a fill-handle double-click: fill straight down to match the populated extent of the
    /// nearest non-blank adjacent column (checked to the left first, then the right, matching
    /// Excel), stopping at the first blank row below the source. GridView has no cell data access,
    /// so this host resolves the adjacent-column extent and hands it to
    /// <see cref="GridAutofillPlanner.CalculateDoubleClickFillRange"/> to compute the fill range,
    /// then executes it the same way as a dragged <see cref="OnAutofillRequested"/> fill.
    /// </summary>
    private void OnAutofillHandleDoubleClicked(GridRange source)
    {
        if (_workbook.GetSheet(_currentSheetId) is not { } sheet)
            return;

        var adjacentLastRow = GridAutofillPlanner.ResolveAdjacentColumnLastPopulatedRow(sheet, source);
        var fillRange = GridAutofillPlanner.CalculateDoubleClickFillRange(source, adjacentLastRow);
        if (fillRange is null)
            return;

        // Double-click never raises AutofillModifiersResolved (that pairing only happens at
        // drag-release), so _autofillCtrlHeld may hold a stale value from an earlier drag.
        // Excel's double-click fill always behaves like a plain (non-Ctrl) drag, so pass false
        // explicitly rather than reading the possibly-stale field.
        ExecuteAutofill(source, fillRange.Value, ctrlHeld: false);
    }

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
        _documentContext.CommandStackChanged -= CommandStackChangeNotifier_StackChanged;
        _fileOperationCancellationSession.Dispose();
        _workbookSessionDisposed = true;
        _session.Dispose();
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

