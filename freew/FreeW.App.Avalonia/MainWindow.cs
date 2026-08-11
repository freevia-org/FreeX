using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System.Globalization;
using System.Text.Json;
using Free.Shared.AppServices;
using Free.Shared.AppServices.Printing;
using Free.Shared.Drawing;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;
using Free.Shared.Theme;
using Free.Shared.Theme.Avalonia;
using FreeW.App.Avalonia.Backstage;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Pdf;
using FreeW.App.Avalonia.Printing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.App.Presentation;
using FreeW.App.Presentation.Backstage;
using FreeW.App.Presentation.ContextMenus;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.DocumentFragments;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Options;
using FreeW.App.Presentation.QuickParts;
using FreeW.App.Presentation.Ribbon;
using FreeW.App.Presentation.Shell;
using FreeW.App.Presentation.Speech;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

public sealed partial class MainWindow : Window
{
    private static readonly ProductThemeResourceProfile ThemeResources = ProductThemeResourceProfiles.FreeW;

    private const string DefaultTitle = "FreeW";
    private static readonly SisterAppFileTextSpec FileText = FreeWFileTextResources.Document;

    private readonly DocumentPersistenceWorkflow _documentPersistence;
    private readonly FreeWDocumentFileWorkflow _documentFileWorkflow;
    private readonly IPlatformPrintService _printService;
    private readonly FreeWPortablePrintWorkflow _portablePrintWorkflow;
    private readonly Func<Window, PrinterDiscoveryResult, CancellationToken, Task<PrintSelection?>> _showPrintSelectionDialog;
    private readonly Action<IInputElement?> _restorePrintOwnerFocus;
    private readonly Func<DocumentView, Stream, PrintSelection, FreeWAvaloniaPdfExportResult> _savePrintPdf;

    // Test-injected save-PDF seams (savePrintPdf/saveSelectedPrintPdf) are void Actions that write a
    // synthetic file and don't go through the shared PDF writers, so they cannot produce real image
    // diagnostics; this stands in for "none" so the result shape matches the production Save() path.
    private static readonly FreeWAvaloniaPdfExportResult NoImageDiagnosticsPrintResult =
        new(0, Free.Shared.Pdf.Skia.PdfExportBackend.PortableWinAnsi, []);
    private readonly Func<IStorageProvider, AvaloniaFilePickerSaveRequest, Task<(bool Canceled, string? LocalPath)>> _pickExportPath;
    private readonly Func<Task<string?>> _pickPdfImportPathAsync;
    private readonly Func<bool, string, Task<string?>>? _askHeaderFooterText;
    private readonly IScreenClipService _screenClipService;
    private readonly IPlatformClipboard _platformClipboard;
    private readonly DocumentView _editor = new();
    private readonly QuickPartLibrary _quickParts = QuickPartLibrary.Load();
    private TextBlock _pageStatus = null!;
    private TextBlock _sectionStatus = null!;
    private TextBlock _status = null!;
    private TextBlock _dataFolderStatus = null!;
    // AV-MAIL: the Mailings engine (recipients / merge fields / preview / finish-merge) shared with the ribbon.
    private MailMergeEngine? _mailMerge;
    private readonly TextBox _findBox = new() { Width = 200, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _replaceBox = new() { Width = 200, VerticalAlignment = VerticalAlignment.Center };
    private TextBlock _zoomLabel = null!;
    private Slider _zoomSlider = null!;
    private readonly ScaleTransform _zoom = new(1, 1);
    private Button _readModeSwitch = null!;
    private ToggleButton _printLayoutSwitch = null!;
    private ToggleButton _webLayoutSwitch = null!;
    private ToggleButton _draftSwitch = null!;
    private ToggleButton _pagedEditSwitch = null!;
    private readonly SisterAvaloniaFileCommandWorkflow _fileWorkflow;
    private readonly SisterAvaloniaAsyncWindowCloseCoordinator _closeCoordinator;
    private readonly Border _titleBar;
    private Border? _ribbonHost;
    private Border? _statusBar;
    private Control? _dataFolderItemControl;
    private Control? _statusViewSwitchControl;
    private Control? _statusZoomControl;
    private IReadOnlyList<Button> _quickAccessButtons = [];
    private readonly FreeWOptions _options;
    private readonly FreeWOptionsRuntimeSession _optionsRuntime;
    private readonly IApplicationOptionsStore<FreeWOptions> _optionsStore;
    private readonly AutosaveAdapter _autosave;
    private readonly NavigationPane _navPane;
    private readonly ReviewingPane _reviewingPane;
    private readonly ReviewBalloonsPane _reviewBalloonsPane;
    private readonly RevealFormattingPane _revealPane;
    private readonly NotesPane _notesPane;
    private readonly ThesaurusPane _thesaurusPane;
    private readonly OutlineView _outlineView;
    private Control? _ribbonControl;
    private IRibbonCommandRegistry? _ribbonRegistry;
    private readonly RibbonStateStore _ribbonStateStore = new();
    private int _ribbonStateRefreshCount;
    private bool _ribbonKeyTipsVisible;
    private Border? _findBar;
    private FindReplaceDialog? _findReplaceDialog;
    private ScrollViewer? _scroller;
    private readonly Border _workspace = new()
    {
        Background = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)),
    };
    private Control? _liveWorkspaceContent;
    private Grid? _splitPreviewGrid;
    private Control? _splitPreviewSnapshot;
    private readonly FreeWViewSession _viewSession = new(FreeWViewDepthCapabilities.FullDesktop);
    private ScrollViewer? _sideToSidePreviewScrollViewer;
    private Button? _sideToSidePreviousPairButton;
    private Button? _sideToSideNextPairButton;
    private TextBlock? _sideToSidePairStatusText;
    private double _sideToSidePairScrollStrideDip;
    private double _sideToSidePlannedHorizontalOffsetDip;
    private bool _sideToSideUsesLiveEditor;
    private bool _multiplePagesUsesLiveEditor;
    private double _zoomScale = 1.0;
    private bool _updatingZoomSlider;
    private readonly FreeWEditorInteractionSession _editorInteraction = new();
    private readonly FreeWApplicationCommandRouter _applicationCommands;
    private bool _pagedEditMode;
    // Avalonia's PrintLayout is already the live, multi-page editing surface used by Page Edit.
    // Keep the prior continuous view so entering the alias does not change the user's view when it
    // is exited again (WPF restores the live editor that was underneath its page panel).
    private DocumentViewMode _viewModeBeforePagedEdit = DocumentViewMode.PrintLayout;
    private bool _pagedEditModeBeforeOutline;
    private bool _outlineMode;
    private double _editorMaxWidthBeforeReadMode = double.PositiveInfinity;
    private HorizontalAlignment _editorAlignmentBeforeReadMode = HorizontalAlignment.Stretch;
    private Thickness _editorMarginBeforeReadMode;
    private IBrush _workspaceBackgroundBeforeReadMode = Brushes.Transparent;
    private bool _suppressEditorDirty;
    private ReadAloudSession? _readAloudSession;
    private CancellationTokenSource? _printCancellation;
    private PrinterDiscoveryResult? _latestPrinterDiscovery;

    public MainWindow()
        : this(Array.Empty<string>())
    {
    }

    public MainWindow(IReadOnlyList<string> startupArguments)
        : this(
            startupArguments,
            null,
            InMemoryApplicationOptionsStore<FreeWOptions>.ForProductFile(
                PlatformApplicationDataPathProvider.LocalInstance))
    {
    }

    internal MainWindow(
        IReadOnlyList<string> startupArguments,
        FreeWOptions? options,
        IApplicationOptionsStore<FreeWOptions> optionsStore,
        IScreenClipService? screenClipService = null,
        IPlatformPrintService? printService = null,
        Func<Window, PrinterDiscoveryResult, CancellationToken, Task<PrintSelection?>>? showPrintSelectionDialog = null,
        Action<IInputElement?>? restorePrintOwnerFocus = null,
        Func<IStorageProvider, AvaloniaFilePickerSaveRequest, Task<(bool Canceled, string? LocalPath)>>? pickExportPath = null,
        Func<string, Task<SaveChangesPrompt>>? promptSaveChangesAsync = null,
        Func<string, Exception, Task>? showFileCommandErrorAsync = null,
        Func<bool, string, Task<string?>>? askHeaderFooterText = null,
        Action<DocumentView, Stream>? savePrintPdf = null,
        DocumentPersistenceWorkflow? documentPersistence = null,
        Func<Task<string?>>? pickPdfImportPathAsync = null,
        Action<DocumentView, Stream, PrintSelection>? saveSelectedPrintPdf = null,
        IPlatformClipboard? platformClipboard = null,
        bool suppressStartupRecoveryOffer = false)
    {
        _optionsStore = optionsStore;
        _documentPersistence = documentPersistence ?? new DocumentPersistenceWorkflow();
        _screenClipService = screenClipService ?? new AvaloniaScreenClipService();
        _platformClipboard = platformClipboard ?? new AvaloniaPlatformClipboard(
            () => TopLevel.GetTopLevel(this)?.Clipboard);
        _editor.CanPasteProvider = () => _platformClipboard.IsAvailable;
        _printService = printService ?? PlatformPrintServiceFactory.Create();
        _portablePrintWorkflow = new FreeWPortablePrintWorkflow(_printService);
        _showPrintSelectionDialog = showPrintSelectionDialog ??
            ((owner, discovery, cancellationToken) =>
                CupsPrintDialog.ShowAsync(owner, discovery, cancellationToken: cancellationToken));
        _restorePrintOwnerFocus = restorePrintOwnerFocus ?? RestorePrintOwnerFocus;
        _savePrintPdf = saveSelectedPrintPdf is not null
            ? (view, stream, selection) =>
            {
                saveSelectedPrintPdf(view, stream, selection);
                return NoImageDiagnosticsPrintResult;
            }
            : savePrintPdf is not null
                ? (view, stream, _) =>
                {
                    savePrintPdf(view, stream);
                    return NoImageDiagnosticsPrintResult;
                }
                : (view, stream, selection) => FreeWAvaloniaPdfExport.Save(view, stream, selection);
        _pickExportPath = pickExportPath ?? PickExportPathAsync;
        _pickPdfImportPathAsync = pickPdfImportPathAsync ?? PromptPdfImportPathAsync;
        _askHeaderFooterText = askHeaderFooterText;
        _options = options ?? _optionsStore.Load();
        _optionsRuntime = new FreeWOptionsRuntimeSession(_options);
        ApplyEditorTypingOptions(_optionsRuntime.EditorTypingOptions);

        Title = DefaultTitle;
        Width = 1040;
        Height = 720;
        MinWidth = 720;
        MinHeight = 480;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));
        ApplyWindowIcon();
        _fileWorkflow = new SisterAvaloniaFileCommandWorkflow(
            owner: this,
            titleSpec: new SisterAvaloniaFileTitleSpec(
                ApplicationName: DefaultTitle,
                Separator: " \u2014 ",
                ApplicationPlacement: WindowTitleApplicationPlacement.DocumentThenApplication),
            maxRecentEntries: () => _options.RecentFilesCap,
            onChanged: UpdateStatus,
            saveAsync: SaveAsync,
            promptSaveChangesAsync: promptSaveChangesAsync,
            showFileCommandErrorAsync: showFileCommandErrorAsync,
            restoreOwnerFocus: RestoreOwnerFocus);
        _documentFileWorkflow = new FreeWDocumentFileWorkflow(
            _fileWorkflow.Workflow,
            _documentPersistence,
            new FreeWDocumentFilePorts(
                GetDocument: () => _editor.Document,
                LoadDocumentAsync: (document, _) =>
                {
                    LoadDocumentContent(document);
                    return ValueTask.CompletedTask;
                },
                ConfirmSaveCompatibilityAsync: (plan, _) =>
                    new ValueTask<bool>(SaveCompatibilityWarningDialog.ShowAsync(this, plan)),
                UpdateFieldsAsync: _ =>
                {
                    _suppressEditorDirty = true;
                    try
                    {
                        _editor.UpdateFields();
                    }
                    finally
                    {
                        _suppressEditorDirty = false;
                    }
                    return ValueTask.CompletedTask;
                }));
        _applicationCommands = new FreeWApplicationCommandRouter(new FreeWApplicationCommandActions(
            NewDocument: NewDocument,
            OpenDocument: () => _ = OpenAsync(),
            SaveDocument: () => _ = SaveAsync(),
            SaveDocumentAs: () => _ = SaveAsAsync(),
            PrintDocument: () => _ = PrintAsync(),
            Find: () => OpenFindReplaceDialog(FindReplaceDialogOpenMode.Find),
            Replace: () => OpenFindReplaceDialog(FindReplaceDialogOpenMode.Replace),
            Cut: () => _ = CutAsync(),
            Copy: () => _ = CopyAsync(),
            Paste: () => _ = PasteAsync(),
            PasteTextOnly: () => _ = PastePlainTextAsync(),
            SelectAll: _editor.SelectAll,
            Undo: _editor.Undo,
            Redo: _editor.Redo,
            RevealFormatting: ToggleRevealFormatting,
            Thesaurus: ToggleThesaurusPane,
            LockCurrentField: () => _editor.SetFieldLockAtCaret(true),
            UnlockCurrentField: () => _editor.SetFieldLockAtCaret(false),
            UnlinkCurrentField: _editor.UnlinkFieldAtCaret,
            ToggleCurrentFieldCode: _editor.ToggleFieldCodeAtCaret,
            ToggleFieldCodes: _editor.ToggleFieldCodes,
            UpdateCurrentField: _editor.UpdateFieldAtCaret));
        _autosave = new AutosaveAdapter(
            _editor,
            _fileWorkflow.Workflow,
            recoverInNewWindowAsync: OpenNewWindowWithRecoveredSnapshotAsync);
        _closeCoordinator = new SisterAvaloniaAsyncWindowCloseCoordinator(
            confirmCloseAllowedAsync: ConfirmCloseAllowedAndStopAutosaveAsync,
            requestClose: () =>
            {
                DisposeReadAloud();
                Close();
            },
            restoreOwnerFocus: RestoreOwnerFocus);
        _navPane = new NavigationPane(_editor);
        _reviewingPane = new ReviewingPane(_editor);
        _reviewBalloonsPane = new ReviewBalloonsPane(_editor);
        _revealPane = new RevealFormattingPane(_editor);
        _notesPane = new NotesPane(_editor);
        _thesaurusPane = new ThesaurusPane(
            _editor,
            async text => (await _platformClipboard.WriteAsync(
                new PlatformClipboardContent(Text: text))).IsSuccess);
        _outlineView = new OutlineView(_editor);

        var ribbon = BuildRibbon();
        var statusBar = BuildStatusBar();
        var findBar = BuildFindBar();

        _scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(48, 24),
            Content = new LayoutTransformControl { LayoutTransform = _zoom, Child = _editor },
        };
        _navPane.ScrollerRef = _scroller;

        var workArea = new DockPanel { LastChildFill = true };

        // Nav pane docked left; reviewing pane docked right; workspace fills the remainder.
        DockPanel.SetDock(_navPane, Dock.Left);
        workArea.Children.Add(_navPane);

        DockPanel.SetDock(_reviewingPane, Dock.Right);
        workArea.Children.Add(_reviewingPane);

        DockPanel.SetDock(_reviewBalloonsPane, Dock.Right);
        workArea.Children.Add(_reviewBalloonsPane);

        DockPanel.SetDock(_revealPane, Dock.Right);
        workArea.Children.Add(_revealPane);

        DockPanel.SetDock(_thesaurusPane, Dock.Right);
        workArea.Children.Add(_thesaurusPane);

        DockPanel.SetDock(_notesPane, Dock.Bottom);
        workArea.Children.Add(_notesPane);

        _liveWorkspaceContent = _scroller;
        _workspace.Child = _scroller;
        workArea.Children.Add(_workspace);

        _editor.DocumentChanged += OnEditorDocumentChanged;
        _editor.DocumentChanged += StopReadAloudAfterDocumentChange;
        _editor.DocumentChanged += () => { if (_navPane.IsVisible) _navPane.Refresh(); };
        _editor.DocumentChanged += () => { if (_reviewingPane.IsVisible) _reviewingPane.Refresh(); };
        _editor.DocumentChanged += () => { if (_reviewBalloonsPane.IsVisible) _reviewBalloonsPane.Refresh(); };
        _editor.DocumentChanged += () => { if (_revealPane.IsVisible) _revealPane.Refresh(); };
        _editor.DocumentChanged += () => { if (_notesPane.IsVisible) _notesPane.Refresh(); };
        _editor.DocumentChanged += () => { if (_thesaurusPane.IsVisible) _thesaurusPane.Refresh(); };
        _editor.DocumentChanged += () => { if (_outlineMode) _outlineView.Refresh(); };
        _editor.ScrollToCaretRequested += ScrollCaretIntoView;
        _editor.CaretMoved += UpdateStatus;
        _editor.CaretMoved += () => { if (_thesaurusPane.IsVisible) _thesaurusPane.Refresh(); };
        _editor.ViewModeChanged += UpdateStatus;
        _editor.ViewModeChanged += UpdateViewModeButtons;
        _editor.HyperlinkActivated += OpenExternalUri;
        _editor.ContextMenuCommandRequested += OnEditorContextMenuCommandRequested;

        UpdateViewModeButtons();
        _editor.CellEditRequested += async req =>
        {
            var result = await new CellEditDialog(req.Text).ShowDialog<string?>(this);
            if (result is not null)
                _editor.SetCellText(req.Block, req.Row, req.Col, result);
        };
        var startupDocument = FreeWApplicationStartup.TryOpenStartupDocument(
            startupArguments,
            _documentPersistence);
        if (startupDocument is null)
            LoadDocumentAsSaved(SampleDocument.Create(), path: null);
        else
            ApplyOpenResult(startupDocument);
        KeyDown += MainWindow_KeyDown;
        AddHandler(
            InputElement.PointerPressedEvent,
            (_, _) => SetRibbonKeyTipsVisible(false),
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        Deactivated += (_, _) => SetRibbonKeyTipsVisible(false);

        // Start autosave once the window is shown; offer recovery on first open.
        // R133-remediation: OfferRecoveryAsync now enumerates and offers EVERY pending snapshot,
        // not just the latest. The extra windows it opens (via
        // OpenNewWindowWithRecoveredSnapshotAsync) pass suppressStartupRecoveryOffer:true so their
        // own Opened handler does not re-run OfferRecoveryAsync and re-prompt for the very
        // candidates this window's call is already working through.
        Opened += async (_, _) =>
        {
            _autosave.Start();
            if (!suppressStartupRecoveryOffer)
                await _autosave.OfferRecoveryAsync(this);
            await RefreshPrinterDiscoveryAsync();
            await RunTablePropertiesX11ValidationSeedAsync();
        };

        // Dirty-gate on close: cancel the synchronous event and let the shared async
        // coordinator resume the close only after the dirty decision settles.
        Closing += OnWindowClosing;

        var frame = SisterAppClientFrameBuilder.Build(SisterAppClientFrameSpec.ForWorkArea(
            chrome: ribbon,
            workArea: workArea,
            statusBar: statusBar,
            bottomPanelsAboveStatus: [findBar]));

        var windowFrame = SisterAppWindowFrameBuilder.Build(new SisterAppWindowFrameSpec(
            Window: this,
            Body: frame.Root,
            TitleBarBackground: AvaloniaThemeResourceResolver.ResolveOr<IBrush>(
                ThemeResources.TitleBarBrush,
                new SolidColorBrush(Color.FromRgb(0x17, 0x32, 0x4D))),
            TitleBarForeground: AvaloniaThemeResourceResolver.ResolveOr<IBrush>(ThemeResources.WhiteBrush, Brushes.White)));
        _titleBar = windowFrame.TitleBar;
        _quickAccessButtons = SisterQuickAccessToolbarBuilder.Render(
            windowFrame.QatHost,
            new SisterQuickAccessToolbarActions(
                Save: () => _applicationCommands.Execute(FreeWKeyboardCommand.SaveDocument),
                Undo: () => _applicationCommands.Execute(FreeWKeyboardCommand.Undo),
                Redo: () => _applicationCommands.Execute(FreeWKeyboardCommand.Redo)),
            AvaloniaThemeResourceResolver.ResolveOr<IBrush>(ThemeResources.WhiteBrush, Brushes.White));

        Content = windowFrame.Root;
        UpdateStatus();
    }

    private void ApplyWindowIcon() =>
        AvaloniaWindowIconLoader.TryApply(this, "FreeW.ico");

    public DocumentView Editor => _editor;

    internal bool IsReadAloudActiveForTest => _readAloudSession?.IsActive == true;

    internal void ToggleReadAloudForTest() => ToggleReadAloud();
    public bool HasToolbar { get; private set; }

    /// <summary>
    /// Exposes the navigation pane for tests that need to inspect its state headlessly.
    /// </summary>
    internal NavigationPane NavPane => _navPane;

    /// <summary>
    /// Exposes the reviewing pane for tests that need to inspect its state headlessly.
    /// </summary>
    internal ReviewingPane ReviewingPane => _reviewingPane;

    internal bool StepRevision(int direction)
    {
        if (!_reviewingPane.IsVisible)
            ToggleReviewingPane();
        else
            _reviewingPane.Refresh();
        return _reviewingPane.StepRevision(direction, refresh: false);
    }

    // Review > Changes uses the selected Reviewing Pane row in WPF. Keep the Avalonia ribbon on
    // that same selected-entry route; an empty or hidden pane is a deliberate no-op.
    private void AcceptSelectedRevision()
    {
        if (_reviewingPane.SelectedRevision is { } entry)
            _reviewingPane.AcceptEntry(entry);
    }

    private void RejectSelectedRevision()
    {
        if (_reviewingPane.SelectedRevision is { } entry)
            _reviewingPane.RejectEntry(entry);
    }

    internal ReviewBalloonsPane ReviewBalloonsPane => _reviewBalloonsPane;
    internal bool RibbonKeyTipsVisibleForTest => _ribbonKeyTipsVisible;
    internal Control? RibbonControlForTest => _ribbonControl;
    internal IRibbonCommandRegistry? RibbonRegistryForTests => _ribbonRegistry;
    internal bool HasWindowIconForTests => Icon is not null;
    internal Border TitleBarForTests => _titleBar;
    internal IReadOnlyList<Button> QuickAccessButtonsForTests => _quickAccessButtons;
    internal IReadOnlyList<Control> StatusViewControlsForTests =>
        [_readModeSwitch, _printLayoutSwitch, _webLayoutSwitch, _draftSwitch, _pagedEditSwitch];
    internal string PageStatusForTests => _pageStatus.Text ?? string.Empty;
    internal string SectionStatusForTests => _sectionStatus.Text ?? string.Empty;
    internal string CountsStatusForTests => _status.Text ?? string.Empty;
    internal string PrintStatusForTests => _status.Text ?? string.Empty;
    internal MailMergeEngine MailMergeForTests => _mailMerge!;
    internal Task ExecuteFinishMergePlanForTests(MailMergeFinishPlan plan) => ExecuteFinishMergePlanAsync(plan);
    internal string DataFolderStatusForTests => _dataFolderStatus.Text ?? string.Empty;
    internal Slider ZoomSliderForTests => _zoomSlider;
    internal string ZoomLabelForTests => _zoomLabel.Text ?? string.Empty;
    internal void ApplyZoomForTests(double scale) => ApplyZoom(scale);
    internal void RaiseKeyDownForTest(KeyEventArgs args) => MainWindow_KeyDown(this, args);
    internal bool IsCloseDecisionPendingForTests => _closeCoordinator.IsClosePending;

    /// <summary>
    /// Exposes the reveal-formatting pane for tests that need to inspect its state headlessly.
    /// </summary>
    internal RevealFormattingPane RevealPane => _revealPane;
    internal NotesPane NotesPaneForTest => _notesPane;
    internal ThesaurusPane ThesaurusPaneForTest => _thesaurusPane;

    internal FreeWViewDepthMode ViewDepthMode => _viewSession.CurrentDepth.Mode;
    internal bool IsSplitPreviewActive => _viewSession.CurrentDepth.IsSplitActive;
    internal bool IsMultiplePagesPreviewActive => _viewSession.CurrentDepth.IsMultiplePagesActive;
    internal bool IsSideToSidePreviewActive => _viewSession.CurrentDepth.IsSideToSideActive;
    internal string? ViewDepthLimitation => _viewSession.CurrentDepth.Limitation;
    internal FreeWViewDepthPagePairNavigationState SideToSideNavigationForTests =>
        _viewSession.PagePairNavigation;
    internal bool HasSideToSidePagePairNavigationForTests =>
        _sideToSidePreviewScrollViewer is not null &&
        _sideToSidePreviousPairButton is not null &&
        _sideToSideNextPairButton is not null &&
        _sideToSidePairStatusText is not null;
    internal Vector SideToSidePreviewOffsetForTests => new(_sideToSidePlannedHorizontalOffsetDip, 0);
    internal Control? WorkspaceContentForTests => _workspace.Child as Control;
    internal bool IsWorkspaceShowingLiveEditor => ReferenceEquals(_workspace.Child, _liveWorkspaceContent);
    internal bool IsSideToSideEditorEditableForTests => _sideToSideUsesLiveEditor;
    internal bool IsMultiplePagesEditorEditableForTests => _multiplePagesUsesLiveEditor;
    internal bool IsOutlineModeActiveForTests => _outlineMode;
    internal bool IsPagedEditModeActiveForTests => _pagedEditMode;
    internal void TogglePagedEditViewForTests() => TogglePagedEditView();
    internal bool IsWorkspaceShowingOutline => ReferenceEquals(_workspace.Child, _outlineView);
    internal OutlineView OutlineViewForTests => _outlineView;
    internal void ToggleOutlineViewForTests() => ToggleOutlineView();

    /// <summary>
    /// Show or hide the navigation pane and refresh its heading list when making it visible.
    /// Wired to <c>freew.navigationpane</c> ribbon toggle.
    /// </summary>
    internal void ToggleNavigationPane()
    {
        _navPane.IsVisible = !_navPane.IsVisible;
        if (_navPane.IsVisible)
            _navPane.Refresh();
        RefreshRibbonCommandStates();
    }

    /// <summary>
    /// Show or hide the reviewing pane and refresh its tracked-changes list when making it visible.
    /// Wired to <c>freew.reviewingpane</c> ribbon toggle.
    /// </summary>
    internal void ToggleReviewingPane()
    {
        _reviewingPane.IsVisible = !_reviewingPane.IsVisible;
        if (_reviewingPane.IsVisible)
            _reviewingPane.Refresh();
        RefreshRibbonCommandStates();
    }

    /// <summary>
    /// Show or hide the compact review balloon strip backed by comments and revisions.
    /// Wired to <c>freew.show-markup-balloons</c>.
    /// </summary>
    internal void ToggleReviewBalloons()
    {
        var show = !_reviewBalloonsPane.IsVisible;
        _reviewBalloonsPane.IsVisible = show;
        _editor.ApplyShowMarkupBalloons(show);
        if (show)
            _reviewBalloonsPane.Refresh();
    }

    /// <summary>
    /// Show or hide the Reveal Formatting pane and refresh its content when making it visible.
    /// Wired to <c>freew.reveal-formatting</c> ribbon toggle (View → Show group) and Shift+F1.
    /// </summary>
    internal void ToggleRevealFormatting()
    {
        _revealPane.IsVisible = !_revealPane.IsVisible;
        if (_revealPane.IsVisible)
            _revealPane.Refresh();
        RefreshRibbonCommandStates();
    }

    /// <summary>
    /// Opens the Find &amp; Replace dialog (modeless). If an instance is already open it is
    /// brought to the front. Wired to <c>freew.find-replace-dialog</c> ribbon command and Ctrl+H.
    /// </summary>
    internal void OpenFindReplaceDialog(
        FindReplaceDialogOpenMode openMode = FindReplaceDialogOpenMode.Find)
    {
        if (_findReplaceDialog is not null)
        {
            _findReplaceDialog.Activate();
            _findReplaceDialog.ActivateFor(openMode);
            return;
        }

        _findReplaceDialog = new FindReplaceDialog(_editor, openMode)
        {
            ScrollerRef = _scroller,
        };
        _findReplaceDialog.Closed += (_, _) => _findReplaceDialog = null;
        _findReplaceDialog.Show(this);
        _findReplaceDialog.Activate();
        _findReplaceDialog.ActivateFor(openMode);
    }

    /// <summary>
    /// Opens the Font dialog (modal). Pre-populates from the caret formatting; on OK applies the
    /// changes to the selection via <see cref="DocumentView"/> formatting methods.
    /// Wired to <c>freew.font-dialog</c> ribbon command (Home → Font group).
    /// </summary>
    private Task OpenFontDialogAsync() =>
        FontDialog.ShowAndApplyAsync(this, _editor);

    /// <summary>
    /// Opens the Paragraph dialog (modal). Pre-populates from the current paragraph's formatting;
    /// on OK applies the changes via <see cref="DocumentView"/> paragraph methods.
    /// Wired to <c>freew.paragraph-dialog</c> ribbon command (Home → Paragraph group).
    /// </summary>
    private Task OpenParagraphDialogAsync() =>
        ParagraphDialog.ShowAndApplyAsync(this, _editor);

    private Task OpenTabsDialogAsync() =>
        TabsDialog.ShowAndApplyAsync(this, _editor);

    private Task OpenBordersAndShadingDialogAsync() =>
        BordersAndShadingDialog.ShowAndApplyAsync(this, _editor);

    private Task OpenCharacterBorderDialogAsync()
    {
        _editor.Focus();
        return CharacterFormattingPickerDialog.ShowAndApplyBorderAsync(this, _editor);
    }

    private Task OpenCharacterShadingDialogAsync()
    {
        _editor.Focus();
        return CharacterFormattingPickerDialog.ShowAndApplyShadingAsync(this, _editor);
    }

    private Task OpenCellShadingDialogAsync() =>
        CellShadingDialog.ShowAndApplyAsync(this, _editor);

    private Task OpenSortDialogAsync() =>
        SortDialog.ShowAndApplyAsync(this, _editor);

    private async Task OpenImageCropDialogAsync()
    {
        if (_editor.SelectedFloatingImage() is not { } image)
            return;

        var result = await ImageCropDialog.ShowAsync(
            this,
            image.CropLeft,
            image.CropRight,
            image.CropTop,
            image.CropBottom);
        if (result is not null)
            _editor.SetSelectedImageCrop(result.Left, result.Right, result.Top, result.Bottom);
        _editor.Focus();
    }

    private async Task OpenImageSizeDialogAsync()
    {
        if (_editor.SelectedFloatingImage() is not { } image)
            return;

        var result = await ImageSizeDialog.ShowAsync(this, image.WidthPt, image.HeightPt);
        if (result is not null)
            _editor.SetSelectedImageSize(result.Width, result.Height);
        _editor.Focus();
    }

    private async Task OpenImageAltTextDialogAsync()
    {
        if (_editor.SelectedFloatingImage() is not { } image)
            return;

        var result = await ImageAltTextDialog.ShowAsync(this, image.AltText ?? string.Empty);
        if (result is not null)
            _editor.SetSelectedFloatingAltText(result);
        _editor.Focus();
    }

    private async Task OpenImageBorderDialogAsync()
    {
        if (_editor.SelectedFloatingImage() is not { } image)
            return;

        var result = await ImageBorderDialog.ShowAsync(
            this,
            image.BorderColorHex,
            image.BorderWidthPt,
            image.BorderDash);
        if (result is not null)
            _editor.SetSelectedImageBorder(result.Color, result.Width, result.Dash);
        _editor.Focus();
    }

    private async Task OpenImageAdjustDialogAsync()
    {
        if (_editor.SelectedFloatingImage() is not { } image)
            return;
        var result = await ImageAdjustDialog.ShowAsync(
            this, image.BrightnessPct, image.ContrastPct, image.SaturationPct, image.TransparencyPct);
        if (result is not null)
            _editor.SetSelectedImageAdjust(result.Brightness, result.Contrast, result.Saturation, result.Transparency);
        _editor.Focus();
    }

    private async Task OpenImagePositionDialogAsync()
    {
        if (_editor.SelectedFloatingImage() is not { } image)
            return;
        var result = await ImagePositionDialog.ShowAsync(
            this,
            image.HorizontalOffsetPt,
            image.VerticalOffsetPt,
            image.HorizontalAnchor,
            image.VerticalAnchor);
        if (result is not null)
            _editor.SetFloatingPosition(result.HorizontalOffset, result.VerticalOffset, result.HorizontalAnchor, result.VerticalAnchor);
        _editor.Focus();
    }

    private async Task OpenInsertChartDialogAsync()
    {
        var chart = await InsertChartDialog.ShowAsync(this);
        if (chart is not null)
            _editor.InsertChart(chart);
        _editor.Focus();
    }

    private async Task OpenChartTitleDialogAsync()
    {
        if (_editor.SelectedFloatingChart() is not { } chart)
            return;
        var result = await ChartTitleDialog.ShowAsync(this, chart.Title);
        if (result is not null)
            _editor.SetChartTitle(result.NewTitle);
        _editor.Focus();
    }

    private async Task OpenChartAxisTitlesDialogAsync()
    {
        if (_editor.SelectedFloatingChart() is not { } chart)
            return;
        var result = await ChartAxisTitlesDialog.ShowAsync(this, chart.CategoryAxisTitle, chart.ValueAxisTitle);
        if (result is not null)
            _editor.SetChartAxisTitles(result.CategoryTitle, result.ValueTitle);
        _editor.Focus();
    }

    private async Task OpenChartSizeDialogAsync()
    {
        if (_editor.SelectedFloatingChart() is not { } chart)
            return;
        var result = await ChartSizeDialog.ShowAsync(this, chart.WidthPt, chart.HeightPt);
        if (result is not null)
            _editor.SetSelectedChartSize(result.WidthPt, result.HeightPt);
        _editor.Focus();
    }

    private async Task OpenInsertSmartArtDialogAsync()
    {
        var smartArt = await InsertSmartArtDialog.ShowAsync(this);
        if (smartArt is not null)
            _editor.InsertSmartArt(smartArt);
        _editor.Focus();
    }

    private async Task OpenIconPickerDialogAsync()
    {
        try
        {
            var selection = await IconPickerDialog.ShowAsync(this);
            if (selection is null)
                return;
            var bytes = SvgIconRasterizer.RasterizeFileToPng(selection.Path);
            _editor.InsertInlineImage(bytes, 72, 72, ImageFormat.Png);
            _editor.Focus();
        }
        catch (Exception ex)
        {
            _status.Text = SisterAppFileTextPlanner.FormatCommandFailed(FileText, "Insert Icon", ex.Message);
        }
    }

    private async Task OpenCustomizeThemeColorsDialogAsync()
    {
        var dialog = new CustomizeThemeColorsDialog(_editor.Document.Theme);
        await dialog.ShowDialog(this);
        if (dialog.Result is { } result)
            _editor.ApplyThemeColors(result);
        _editor.Focus();
    }

    private async Task OpenCustomizeThemeFontsDialogAsync()
    {
        var theme = _editor.Document.Theme;
        var dialog = new CustomizeThemeFontsDialog(
            new DocumentFontSet(theme.Name, theme.HeadingFont, theme.BodyFont));
        await dialog.ShowDialog(this);
        if (dialog.Result is { } result)
            _editor.ApplyDocumentFontSet(result);
        _editor.Focus();
    }

    private async Task OpenPageColorDialogAsync()
    {
        var dialog = new PageColorDialog(_editor.Document.Page.BackgroundColorHex);
        await dialog.ShowDialog(this);
        if (dialog.Accepted)
            _editor.SetPageColor(dialog.Result);
        _editor.Focus();
    }

    private async Task OpenTableToTextDialogAsync()
    {
        if (!_editor.CanConvertTableToText)
            return;

        var delimiter = await TableTextConversionDialog.ShowAsync(this, "Convert Table to Text");
        if (delimiter is { } value)
            _editor.ConvertTableToText(value);
        _editor.Focus();
    }

    private async Task OpenTextToTableDialogAsync()
    {
        var delimiter = await TableTextConversionDialog.ShowAsync(this, "Convert Text to Table");
        if (delimiter is { } value)
            _editor.ConvertSelectedParagraphsToTable(value);
        _editor.Focus();
    }

    private async Task OpenDateTimeDialogAsync()
    {
        var moment = DateTime.Now;
        var culture = CultureInfo.CurrentCulture;
        var result = await DateTimeDialog.ShowAsync(this, moment, culture);
        if (result is null)
            return;
        if (result.IsField && result.FieldInstruction is { } instruction)
            _editor.InsertComplexField(instruction, result.Text);
        else
            _editor.InsertText(result.Text);
        _editor.Focus();
    }

    private async Task OpenNoteDialogAsync(bool footnote)
    {
        var text = await NoteTextDialog.ShowAsync(this, footnote);
        if (string.IsNullOrWhiteSpace(text))
            return;
        if (footnote)
            _editor.InsertFootnote(text);
        else
            _editor.InsertEndnote(text);
        var id = footnote ? _editor.Document.Footnotes.Keys.Max() : _editor.Document.Endnotes.Keys.Max();
        _notesPane.ShowAndSelect(footnote, id);
        _editor.Focus();
    }

    private async Task OpenFootnoteEndnoteOptionsDialogAsync()
    {
        var result = await FootnoteEndnoteOptionsDialog.ShowAsync(
            this,
            _editor.Document.FootnoteNumbering,
            _editor.Document.EndnoteNumbering);
        var commit = FootnoteEndnoteOptionsDialogPlanner.PlanCommit(result);
        if (commit.ShouldApply)
            _editor.ApplyFootnoteEndnoteOptions(commit.Result!);
        _editor.Focus();
    }

    private async Task OpenMultilevelListDialogAsync()
    {
        var result = await MultilevelListDialog.ShowAsync(this, _editor.Document.MultiLevelList.NumberFormats);
        var commit = MultilevelListDialogPlanner.PlanCommit(result);
        if (commit.ShouldApply)
            _editor.ApplyMultiLevelListDefinition(commit.Definition!);
        _editor.Focus();
    }

    private async Task OpenTableOfAuthoritiesDialogAsync()
    {
        var options = await TableOfAuthoritiesDialog.ShowAsync(this);
        var commit = TableOfAuthoritiesDialogPlanner.PlanCommit(options);
        if (commit.ShouldInsert)
            _editor.InsertTableOfAuthorities(commit.Options!);
        _editor.Focus();
    }

    private async Task OpenSmartArtEditDialogAsync()
    {
        if (_editor.SelectedFloatingSmartArt() is not { } smartArt)
            return;

        var replacement = await SmartArtEditDialog.ShowAsync(this, smartArt);
        if (replacement is not null)
            _editor.ReplaceSelectedSmartArt(replacement);
        _editor.Focus();
    }

    /// <summary>
    /// Opens the Page Setup dialog (modal). Pre-populates from the document's current page
    /// geometry; on OK applies the changes as a single undoable step.
    /// Wired to <c>freew.page-setup-dialog</c> ribbon command (Layout → Page Setup group).
    /// </summary>
    private Task OpenPageSetupDialogAsync(PageSetupDialogTab initialTab = PageSetupDialogTab.Margins) =>
        PageSetupDialog.ShowAndApplyAsync(
            this,
            _editor,
            initialTab,
            openLineNumbers: CycleLineNumbersFromPageSetupAsync,
            openBorders: OpenBordersAndShadingDialogAsync);

    private Task CycleLineNumbersFromPageSetupAsync()
    {
        _editor.ApplyPageSettings(PageLayoutCommandPlanner.CycleLineNumberMode);
        return Task.CompletedTask;
    }

    private Task OpenColumnsDialogAsync() => ColumnsDialog.ShowAndApplyAsync(this, _editor);

    private Task OpenCustomParagraphSpacingDialogAsync() =>
        CustomParagraphSpacingDialog.ShowAndApplyAsync(this, _editor);

    private Task OpenDropCapOptionsDialogAsync() => DropCapOptionsDialog.ShowAndApplyAsync(this, _editor);

    private Task OpenHyphenationOptionsDialogAsync() =>
        HyphenationOptionsDialog.ShowAndApplyAsync(this, _editor);

    private Task OpenManualHyphenationDialogAsync() =>
        ManualHyphenationDialog.ShowAndApplyAsync(this, _editor, message => _status.Text = message);

    private Task OpenLineNumberOptionsDialogAsync() =>
        LineNumberOptionsDialog.ShowAndApplyAsync(this, _editor);

    private Task OpenPageNumberFormatDialogAsync() =>
        PageNumberFormatDialog.ShowAndApplyAsync(this, _editor);

    /// <summary>
    /// AV-DESIGN: Opens the Page Borders dialog (modal); on OK applies the chosen border via
    /// <see cref="DocumentView.SetPageBorder"/> (undoable), or removes it on "None". Wired to
    /// <c>freew.page-borders</c> (Design → Page Background group).
    /// </summary>
    private async Task OpenPageBordersDialogAsync()
    {
        var dialog = new PageBordersDialog(_editor.Document.Page.PageBorder);
        await dialog.ShowDialog(this);
        if (dialog.RemoveRequested)
            _editor.SetPageBorder(null);
        else if (dialog.Result is { } border)
            _editor.SetPageBorder(border);
    }

    /// <summary>
    /// AV-DESIGN: Opens the Custom Watermark dialog (modal); on OK applies the chosen text watermark via
    /// <see cref="DocumentView.SetWatermark"/> (undoable), or removes it on "No Watermark". Wired to
    /// <c>freew.watermark.custom</c> (Design → Page Background group).
    /// </summary>
    private async Task OpenWatermarkDialogAsync()
    {
        var dialog = new WatermarkDialog(_editor.Document.Page.EffectiveWatermark);
        await dialog.ShowDialog(this);
        if (dialog.RemoveRequested)
            _editor.SetWatermark(null);
        else if (dialog.Result is { } options)
            _editor.SetWatermark(options);
    }

    /// <summary>
    /// AV-REVIEW: Opens the Word Count dialog (modal), showing words/characters/paragraphs/lines computed
    /// from the document model. Wired to <c>freew.word-count</c> ribbon command (Review → Proofing group).
    /// </summary>
    private Task OpenWordCountDialogAsync() =>
        new WordCountDialog(_editor.ComputeStatistics()).ShowDialog(this);

    private async Task OpenCrossReferenceDialogAsync()
    {
        var dialog = new CrossReferenceDialog(_editor.Document);
        await dialog.ShowDialog(this);
        if (dialog.Result is { } result)
            _editor.InsertCrossReference(result.Type, result.Target, result.InsertAs, result.Hyperlink);
        _editor.Focus();
    }

    private async Task OpenShapePositionDialogAsync()
    {
        if (_editor.GetSelectedShapePosition() is not { } position)
            return;
        var result = await ImagePositionDialog.ShowAsync(
            this,
            position.HorizontalOffsetPt,
            position.VerticalOffsetPt,
            position.HorizontalAnchor,
            position.VerticalAnchor,
            position.IsGroupLocal ? "Shape Position in Group" : "Shape Position",
            position.IsGroupLocal);
        if (result is not null)
            _editor.SetSelectedShapePosition(
                result.HorizontalOffset,
                result.VerticalOffset,
                result.HorizontalAnchor,
                result.VerticalAnchor);
        _editor.Focus();
    }

    private async Task OpenShapeSizeDialogAsync()
    {
        if (_editor.SelectedFloatingShape() is not { } shape)
            return;
        var result = await ImageSizeDialog.ShowAsync(this, shape.WidthPt, shape.HeightPt, "Shape Size");
        if (result is not null)
            _editor.SetSelectedShapeSize(result.Width, result.Height);
        _editor.Focus();
    }

    private async Task OpenShapeAltTextDialogAsync()
    {
        var selectedShape = _editor.SelectedFloatingShape();
        var selectedWordArt = _editor.SelectedFloatingWordArt();
        if (selectedShape is null && selectedWordArt is null)
            return;
        var seed = selectedShape?.AltText ?? selectedWordArt?.AltText;
        var result = await ImageAltTextDialog.ShowAsync(this, seed ?? string.Empty);
        if (result is not null)
            _editor.SetSelectedFloatingAltText(result);
        _editor.Focus();
    }

    private async Task OpenChartEditDataDialogAsync()
    {
        if (_editor.SelectedFloatingChart() is not { } chart)
            return;
        var replacement = await InsertChartDialog.ShowAsync(this, chart);
        if (replacement is not null)
            _editor.ReplaceSelectedChartData(replacement);
        _editor.Focus();
    }

    private async Task OpenCaptionDialogAsync()
    {
        var defaultLabel = _editor.IsCaretInTable() ? CaptionLabel.Table : CaptionLabel.Figure;
        var result = await CaptionDialog.ShowAsync(this, defaultLabel);
        if (result is not null)
            _editor.InsertCaption(result.Label, result.Text);
        _editor.Focus();
    }

    private async Task OpenCitationDialogAsync()
    {
        var source = await PickCitationSourceAsync();
        if (source is null)
            return;

        _editor.InsertCitation(source);
        _editor.Focus();
    }

    private async Task<Source?> PickCitationSourceAsync()
    {
        var sources = _editor.Document.Sources;
        if (sources.Count > 0)
        {
            var picker = new CitationSourcePickerDialog(sources);
            await picker.ShowDialog(this);
            if (picker.Pick is null)
                return null;
            if (!picker.Pick.AddNew)
                return picker.Pick.Source;
        }

        var entryDialog = new SourceEntryDialog();
        await entryDialog.ShowDialog(this);
        if (entryDialog.Entry is not { } entry)
            return null;

        var masterStore = MasterSourceStore.Load();
        var state = SourceManagementDialogPlanner.BuildInitialState(sources, masterStore.ToSources());
        var plan = SourceManagementDialogPlanner.AddCitationSource(state, entry);
        if (plan.Validation is { } validation)
        {
            _status.Text = validation.Message;
            return null;
        }
        if (plan.Source is null)
            return null;

        var result = SourceManagementDialogPlanner.BuildResult(plan.State);
        _editor.ReplaceSources(result.CurrentSources);
        MasterSourceStore.Save(CreateMasterSourceStore(result.MasterSources));
        return plan.Source;
    }

    private async Task OpenManageSourcesDialogAsync()
    {
        var masterStore = MasterSourceStore.Load();
        var dialog = new ManageSourcesDialog(_editor.Document.Sources, masterStore.ToSources());
        await dialog.ShowDialog(this);
        if (dialog.Result is { } result)
        {
            _editor.ReplaceSources(result.CurrentSources);
            MasterSourceStore.Save(CreateMasterSourceStore(result.MasterSources));
        }
        _editor.Focus();
    }

    private async Task OpenMarkCitationDialogAsync()
    {
        var seed = _editor.SelectedText.Trim();
        var dialog = new MarkCitationDialog(seed);
        await dialog.ShowDialog(this);
        if (dialog.Citation is { } citation)
            _editor.MarkCitation(citation);
        _editor.Focus();
    }

    private async Task OpenMarkIndexEntryDialogAsync()
    {
        var seed = _editor.SelectedText.Trim();
        var dialog = new MarkIndexEntryDialog(seed, _editor.BookmarkNames());
        await dialog.ShowDialog(this);
        if (dialog.Mark is { } mark)
        {
            if (dialog.MarkAll)
                _editor.MarkAllIndexEntries(seed, mark);
            else
                _editor.MarkIndexEntry(mark);
        }
        _editor.Focus();
    }

    private async Task OpenInsertIndexDialogAsync()
    {
        var result = await InsertIndexDialog.ShowAsync(this);
        if (result is not null)
            _editor.InsertIndex(result.Identifier);
        _editor.Focus();
    }

    private async Task OpenUpdateIndexDialogAsync()
    {
        var result = await InsertIndexDialog.ShowUpdateAsync(this);
        if (result is not null)
            _editor.RefreshIndex(result.Identifier);
        _editor.Focus();
    }

    private static MasterSourceStore CreateMasterSourceStore(IReadOnlyList<Source> sources) =>
        new()
        {
            Sources = sources.Select(SourceRecord.FromSource).ToList()
        };

    private void ToggleSpellCheck()
    {
        var enabled = _editor.ToggleSpellCheck();
        _status.Text = enabled ? "Spelling proofing is on." : "Spelling proofing is off.";
    }

    private void AddCurrentWordToDictionary()
    {
        var word = _editor.CurrentProofingWord;
        if (word is null)
        {
            _status.Text = "Select a word, or place the caret inside one, then choose Add to Dictionary.";
            _editor.Focus();
            return;
        }

        _status.Text = _editor.AddCurrentWordToDictionary()
            ? $"Added '{word}' to the custom dictionary."
            : $"'{word}' is already in the custom dictionary.";
        _editor.Focus();
    }

    private void ToggleThesaurusPane() => _thesaurusPane.Toggle();

    private async Task OpenProofingLanguageDialogAsync()
    {
        var current = _editor.GetCaretFormatting().Run.LanguageTag;
        var chosen = await ProofingLanguageDialog.ChooseAsync(this, current);
        if (chosen is null)
            return;

        _editor.SetProofingLanguage(chosen);
        var normalized = ProofingLanguageCatalog.NormalizeTag(chosen);
        _status.Text = normalized is null
            ? "Proofing language cleared."
            : $"Proofing language set to {normalized}.";
        _editor.Focus();
    }

    private async Task CompareDocumentsAsync()
    {
        var originalPath = await PromptReviewDocumentPathAsync("Compare: pick the ORIGINAL document");
        if (originalPath is null)
            return;

        var prompt = ReviewCompareCombineWorkflow.BuildComparePrompt(
            _editor.Document,
            _fileWorkflow.CurrentFileName,
            Environment.UserName);
        var picked = await CompareDocumentsDialog.ShowAsync(this, originalPath, prompt);
        if (picked is null)
            return;

        try
        {
            var original = OpenReviewDocument(picked.OriginalFilePath, "Compare documents");
            var compared = ReviewCompareCombineWorkflow.ExecuteCompare(
                new CompareDocumentsExecutionInput(
                    original,
                    _editor.Document,
                    picked.Author,
                    ReviewCompareCombineWorkflow.CreateRevisionDateXml(DateTimeOffset.UtcNow),
                    picked.Settings));
            LoadReviewResult(compared, $"Compared with {Path.GetFileName(picked.OriginalFilePath)}.");
        }
        catch (Exception ex)
        {
            _status.Text = $"Could not compare the documents: {ex.Message}";
        }

        _editor.Focus();
    }

    private async Task CombineDocumentsAsync()
    {
        var originalPath = await PromptReviewDocumentPathAsync("Combine: pick the ORIGINAL document");
        if (originalPath is null)
            return;

        var reviewerBPath = await PromptReviewDocumentPathAsync("Combine: pick Reviewer B's revised document");
        if (reviewerBPath is null)
            return;

        var prompt = ReviewCompareCombineWorkflow.BuildCombinePrompt(
            _editor.Document,
            _fileWorkflow.CurrentFileName,
            Environment.UserName,
            ReviewCompareCombineWorkflow.DefaultReviewerB);
        var picked = await CombineDocumentsDialog.ShowAsync(this, originalPath, reviewerBPath, prompt);
        if (picked is null)
            return;

        try
        {
            var original = OpenReviewDocument(picked.OriginalFilePath, "Combine documents");
            var reviewerB = OpenReviewDocument(picked.ReviewerBFilePath, "Combine documents");
            var combined = ReviewCompareCombineWorkflow.ExecuteCombine(
                new CombineDocumentsExecutionInput(
                    original,
                    _editor.Document,
                    picked.AuthorA,
                    reviewerB,
                    picked.AuthorB,
                    ReviewCompareCombineWorkflow.CreateRevisionDateXml(DateTimeOffset.UtcNow)));
            LoadReviewResult(combined, $"Combined with {Path.GetFileName(picked.ReviewerBFilePath)}.");
        }
        catch (Exception ex)
        {
            _status.Text = $"Could not combine the documents: {ex.Message}";
        }

        _editor.Focus();
    }

    private async Task<string?> PromptReviewDocumentPathAsync(string title)
    {
        using var file = await AvaloniaFilePickerService.PickSingleOpenFileWithLocalPathAsync(
            StorageProvider,
            AvaloniaFilePickerOpenRequest.FromFileTypes(
                title,
                DocumentFilePickerTypes.BuildOpenTypes(_documentPersistence.Adapters)));
        return file?.LocalPath;
    }

    private TextDocument OpenReviewDocument(string path, string commandName)
    {
        if (!_documentPersistence.CanOpenPath(path))
        {
            throw new InvalidOperationException(SisterAppFileTextPlanner.FormatUnsupportedFileType(
                FileText,
                commandName,
                Path.GetExtension(path)));
        }

        return _documentPersistence.Open(path).Document;
    }

    private void LoadReviewResult(TextDocument document, string statusText)
    {
        LoadDocumentContent(document);
        _fileWorkflow.MarkDirtyWithPath(null);
        _status.Text = statusText;
    }

    private async Task ReplyToCommentAsync()
    {
        if (_editor.CommentsAtCaret.Count == 0)
        {
            _status.Text = "Place the caret in a comment to reply.";
            _editor.Focus();
            return;
        }

        var text = await CommentReplyDialog.AskAsync(this);
        if (!string.IsNullOrWhiteSpace(text) && !_editor.ReplyToCommentAtCaret(text))
            _status.Text = "Place the caret in a comment to reply.";
        _editor.Focus();
    }

    private async Task ShowCommentsAsync(IReadOnlyList<CommentListItem> items)
    {
        await CommentListDialog.ShowAsync(this, items);
        _editor.Focus();
    }

    /// <summary>
    /// Opens the Avalonia print-preview surface over a snapshot of the current document. Native print
    /// selection remains deferred, but the preview uses the same paginated renderer as the live editor.
    /// </summary>
    private Task OpenPrintPreviewAsync()
    {
        try
        {
            var snapshot = FreeWDocumentSnapshot.Clone(_editor.Document);
            return new PrintPreviewDialog(
                snapshot,
                _fileWorkflow.DisplayName,
                ExportPdfAsync,
                DirectPrintCapability,
                PrintAsync).ShowDialog(this);
        }
        catch (Exception ex)
        {
            _status.Text = SisterAppFileTextPlanner.FormatCommandFailed(FileText, "Print Preview", ex.Message);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// AV-VIEW: Opens the Zoom dialog (modal). Pre-selects the preset matching the current zoom (or the
    /// custom box), and on OK applies the chosen scale through the same <see cref="ApplyZoom(double)"/>
    /// path as the quick zoom commands. Wired to <c>freew.zoom-dialog</c> (View → Zoom group).
    /// </summary>
    private async Task OpenZoomDialogAsync()
    {
        var dialog = new ZoomDialog(_zoomScale);
        await dialog.ShowDialog(this);
        if (dialog.Result is { } scale)
        {
            ApplyZoom(scale);
            _editor.Focus();
        }
    }

    /// <summary>
    /// AV-VIEW: Window → New Window. Opens a second top-level window showing the same document content.
    /// The document is round-tripped through the in-memory docx serializer so the second window edits an
    /// independent copy (TextDocument has no deep-clone), matching the spirit of Word's "new window on the
    /// same document". Wired to <c>freew.new-window</c>.
    /// </summary>
    private void OpenNewWindow()
    {
        try
        {
            using var buffer = new MemoryStream();
            DocxWriter.Write(_editor.Document, buffer);
            buffer.Position = 0;
            var copy = DocxReader.Read(buffer);

            var second = new MainWindow();
            second.LoadDocumentContent(copy);
            second.Title = Title + " : 2";
            second.Show();
        }
        catch (Exception ex)
        {
            _status.Text = SisterAppFileTextPlanner.FormatCommandFailed(
                FileText,
                FreeWFileTextResources.NewWindowCommand,
                ex.Message);
        }
    }

    // R133-remediation: AutosaveAdapter.OfferRecoveryAsync calls this for every accepted recovery
    // candidate beyond the first (which restores directly into the window that is already open) --
    // mirrors OpenNewWindow()'s window-creation pattern above so each additional pending snapshot
    // from a multi-window crash gets its own window instead of being silently left on disk.
    // suppressStartupRecoveryOffer:true stops the new window's own Opened handler from re-running
    // OfferRecoveryAsync and re-prompting for the very candidates the caller's loop is already
    // working through.
    private async Task<bool> OpenNewWindowWithRecoveredSnapshotAsync(AutosaveRecoveryCandidate candidate)
    {
        try
        {
            var doc = DocxReader.Read(candidate.SnapshotPath);
            var newWindow = new MainWindow(
                Array.Empty<string>(),
                null,
                ApplicationOptionsStore<FreeWOptions>.Create(PlatformApplicationDataPathProvider.LocalInstance),
                suppressStartupRecoveryOffer: true);
            newWindow.LoadDocumentContent(doc);
            newWindow._fileWorkflow.MarkDirtyWithPath(candidate.Sidecar.OriginalFilePath);
            newWindow.Show();
            newWindow.Activate();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void OpenMailMergeErrorReport(TextDocument report)
    {
        var reportWindow = new MainWindow
        {
            Title = "FreeW - Mail Merge Error Report"
        };
        reportWindow.LoadDocumentContent(report);
        reportWindow.Show();
        reportWindow._editor.Focus();
    }

    /// <summary>
    /// AV-VIEW: Window > Arrange All. Tiles every visible FreeW top-level window on the screen that
    /// owns this window, using the screen working area so desktop panels/taskbars remain unobscured.
    /// Avalonia reports working-area coordinates in physical pixels while Window dimensions are DIPs;
    /// the planner converts only the latter before applying each tile.
    /// </summary>
    private void ArrangeAllWindows()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        var windows = desktop.Windows
            .OfType<MainWindow>()
            .Where(window => window.IsVisible)
            .ToList();
        if (windows.Count == 0)
            return;

        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
            return;

        var scaling = screen.Scaling > 0 ? screen.Scaling : 1;
        var bounds = ArrangeAllLayoutPlanner.ArrangeRowFirst(
            screen.WorkingArea.Width / scaling,
            screen.WorkingArea.Height / scaling,
            windows.Count,
            maxColumns: 3);
        var tiles = FreeWAvaloniaWindowBoundsTranslator.Translate(screen.WorkingArea, scaling, bounds);
        for (var index = 0; index < windows.Count && index < tiles.Count; index++)
        {
            var window = windows[index];
            var tile = tiles[index];
            window.WindowState = WindowState.Normal;
            window.Position = tile.Position;
            window.Width = tile.Width;
            window.Height = tile.Height;
        }
    }

    /// <summary>
    /// AV-VIEW: Window → Split. A true split-pane (two scroll regions over one document) is a larger
    /// surface than this slice. The top pane remains the live editor; the bottom pane is a
    /// read-only paginated snapshot, so the command is backed without pretending to offer dual live editing.
    /// </summary>
    internal void ToggleSplit() =>
        ApplyViewDepthTransition(_viewSession.Execute(FreeWViewDepthCommand.ToggleSplit));

    private void ZoomToOnePage()
    {
        var (_, _, wholePageFactor) = ComputeZoomFitFactors();
        ApplyZoom(wholePageFactor);
        _editor.Focus();
    }

    private void ZoomToPageWidth()
    {
        var (pageWidthFactor, _, _) = ComputeZoomFitFactors();
        ApplyZoom(pageWidthFactor);
        _editor.Focus();
    }

    internal void ToggleMultiplePages() =>
        ApplyViewDepthTransition(_viewSession.Execute(FreeWViewDepthCommand.ToggleMultiplePages));

    internal void ToggleSideToSide() =>
        ApplyViewDepthTransition(_viewSession.Execute(FreeWViewDepthCommand.ToggleSideToSide));

    internal void NavigateSideToSideNextPairForTests() =>
        NavigateSideToSidePagePair(FreeWViewDepthPagePairNavigationCommand.NextPair);

    internal void NavigateSideToSidePreviousPairForTests() =>
        NavigateSideToSidePagePair(FreeWViewDepthPagePairNavigationCommand.PreviousPair);

    private void ApplyViewDepthTransition(FreeWViewDepthTransition transition, bool updateStatus = true)
    {
        if (_outlineMode)
            LeaveOutlineView(restorePriorView: false);

        var plan = transition.Current;

        switch (plan.SurfaceKind)
        {
            case FreeWViewDepthSurfaceKind.LiveEditor:
                RestoreLiveWorkspace();
                break;
            case FreeWViewDepthSurfaceKind.SplitEditorWithReadOnlyPreview:
                EnterSplitPreview(plan);
                break;
            case FreeWViewDepthSurfaceKind.ReadOnlyPagePreview:
                EnterReadOnlyPagePreview(plan);
                break;
            case FreeWViewDepthSurfaceKind.EditablePageView:
                EnterEditablePageView(plan);
                break;
        }

        _editor.ApplyViewDepthLayout(plan.Layout);
        if (updateStatus)
            _status.Text = plan.IsSideToSideActive
                ? _viewSession.PagePairNavigation.StatusText
                : plan.StatusText;
    }

    private void RestoreLiveWorkspace()
    {
        if (_splitPreviewGrid is not null && _liveWorkspaceContent is not null)
        {
            _splitPreviewGrid.Children.Remove(_liveWorkspaceContent);
            _splitPreviewGrid.Children.Clear();
        }

        _splitPreviewGrid = null;
        _splitPreviewSnapshot = null;
        ResetSideToSideNavigation();

        if (_liveWorkspaceContent is not null && !ReferenceEquals(_workspace.Child, _liveWorkspaceContent))
            _workspace.Child = _liveWorkspaceContent;
        if (_scroller is not null)
            _scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
    }

    private void EnterSplitPreview(FreeWViewDepthPlan plan)
    {
        RestoreLiveWorkspace();
        if (_liveWorkspaceContent is null)
            return;

        var splitGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = new GridLength(5) },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
            },
        };

        _workspace.Child = null;
        Grid.SetRow(_liveWorkspaceContent, 0);
        splitGrid.Children.Add(_liveWorkspaceContent);

        var splitter = new GridSplitter
        {
            Height = 5,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
            ResizeDirection = GridResizeDirection.Rows,
        };
        Grid.SetRow(splitter, 1);
        splitGrid.Children.Add(splitter);

        _splitPreviewSnapshot = BuildReadOnlyPagePreviewSurface(plan, compact: true);
        Grid.SetRow(_splitPreviewSnapshot, 2);
        splitGrid.Children.Add(_splitPreviewSnapshot);

        _splitPreviewGrid = splitGrid;
        _workspace.Child = splitGrid;
        _editor.Focus();
    }

    private void EnterReadOnlyPagePreview(FreeWViewDepthPlan plan)
    {
        RestoreLiveWorkspace();
        _workspace.Child = BuildReadOnlyPagePreviewSurface(plan, compact: false);
        ApplySideToSideNavigationToScrollViewer(plan);
    }

    private void EnterEditablePageView(FreeWViewDepthPlan plan)
    {
        RestoreLiveWorkspace();
        if (_scroller is null)
            return;

        _scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _multiplePagesUsesLiveEditor = plan.IsMultiplePagesActive;
        if (plan.IsMultiplePagesActive)
        {
            _editor.Focus();
            return;
        }

        _sideToSideUsesLiveEditor = true;
        _viewSession.StartPagePairNavigation(totalPages: Math.Max(1, _editor.PageCount));
        var (pageWidthDip, pageHeightDip) = PageLayout.PageSizeDip(_editor.Document.Page);
        var (viewportWidth, viewportHeight) = GetWorkspaceViewportSize(compact: false);
        var viewport = DocumentViewDepthLayoutPlanner.BuildViewportPlan(
            plan.Layout,
            viewportWidth,
            viewportHeight,
            pageWidthDip,
            pageHeightDip);
        _sideToSidePreviewScrollViewer = _scroller;
        UpdateSideToSidePairScrollStride(plan);
        _workspace.Child = null;
        _workspace.Child = BuildSideToSideNavigationHost(_scroller);
        ApplySideToSideNavigationToScrollViewer(plan);
    }

    private void UpdateSideToSidePairScrollStride(FreeWViewDepthPlan plan)
    {
        if (!plan.IsSideToSideActive)
            return;

        var (pageWidthDip, _) = PageLayout.PageSizeDip(_editor.Document.Page);
        // The live DocumentView owns one page gap per page boundary. Advancing a pair therefore
        // crosses two full page strides, and the LayoutTransform applies the current zoom to that
        // logical distance. Recompute it whenever zoom changes so navigation stays page-aligned.
        _sideToSidePairScrollStrideDip =
            2 * (pageWidthDip + plan.Layout.InterPageGapDip) * _zoomScale;
    }

    private void RefreshSplitPreviewSnapshot()
    {
        if (!_viewSession.CurrentDepth.IsSplitActive || _splitPreviewGrid is null || _splitPreviewSnapshot is null)
            return;

        var replacement = BuildReadOnlyPagePreviewSurface(_viewSession.CurrentDepth, compact: true);
        var index = _splitPreviewGrid.Children.IndexOf(_splitPreviewSnapshot);
        if (index < 0)
            return;

        Grid.SetRow(replacement, 2);
        _splitPreviewGrid.Children.RemoveAt(index);
        _splitPreviewGrid.Children.Insert(index, replacement);
        _splitPreviewSnapshot = replacement;
    }

    private Control BuildReadOnlyPagePreviewSurface(FreeWViewDepthPlan plan, bool compact)
    {
        var snapshot = new DocumentView
        {
            Focusable = false,
            IsHitTestVisible = false,
            ViewMode = DocumentViewMode.PrintLayout,
            ShowGridlines = _editor.ShowGridlines,
            ViewTableGridlines = _editor.ViewTableGridlines,
            ShowRuler = _editor.ShowRuler && !compact,
        };
        snapshot.LoadDocument(FreeWDocumentSnapshot.Clone(_editor.Document));
        snapshot.ApplyViewDepthLayout(plan.Layout);

        var (pageWidthDip, pageHeightDip) = PageLayout.PageSizeDip(_editor.Document.Page);
        var (viewportWidth, viewportHeight) = GetWorkspaceViewportSize(compact);
        var viewport = DocumentViewDepthLayoutPlanner.BuildViewportPlan(
            plan.Layout,
            viewportWidth,
            viewportHeight,
            pageWidthDip,
            pageHeightDip);

        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = compact ? new Thickness(24, 12) : new Thickness(48, 24),
            Content = new LayoutTransformControl
            {
                LayoutTransform = new ScaleTransform(viewport.Scale, viewport.Scale),
                Child = snapshot,
            },
        };

        if (!compact && plan.IsSideToSideActive)
        {
            _viewSession.StartPagePairNavigation(totalPages: snapshot.PageCount);
            _sideToSidePreviewScrollViewer = scroller;
            _sideToSidePairScrollStrideDip = 2 * (pageWidthDip + plan.Layout.InterPageGapDip) * viewport.Scale;
            return BuildSideToSideNavigationHost(scroller);
        }

        ResetSideToSideNavigation();
        return scroller;
    }

    private Control BuildSideToSideNavigationHost(ScrollViewer scroller)
    {
        var host = new DockPanel { LastChildFill = true };
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 4)
        };

        _sideToSidePreviousPairButton = MakeSideToSideNavigationButton(
            FreeWApplicationFrameTextCatalog.PreviousPagePairLabel,
            FreeWApplicationFrameTextCatalog.PreviousPagePairSemantic,
            () => NavigateSideToSidePagePair(FreeWViewDepthPagePairNavigationCommand.PreviousPair));
        _sideToSidePairStatusText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 12, 0)
        };
        AutomationProperties.SetAutomationId(
            _sideToSidePairStatusText,
            FreeWApplicationFrameTextCatalog.PagePairStatusAutomationId);
        _sideToSideNextPairButton = MakeSideToSideNavigationButton(
            FreeWApplicationFrameTextCatalog.NextPagePairLabel,
            FreeWApplicationFrameTextCatalog.NextPagePairSemantic,
            () => NavigateSideToSidePagePair(FreeWViewDepthPagePairNavigationCommand.NextPair));

        toolbar.Children.Add(_sideToSidePreviousPairButton);
        toolbar.Children.Add(_sideToSidePairStatusText);
        toolbar.Children.Add(_sideToSideNextPairButton);

        DockPanel.SetDock(toolbar, Dock.Top);
        host.Children.Add(toolbar);
        host.Children.Add(scroller);
        SyncSideToSideNavigationControls();
        return host;
    }

    private static Button MakeSideToSideNavigationButton(
        string text,
        FreeWSemanticIdentity semantic,
        Action action)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(10, 4),
            MinWidth = 96
        };
        ToolTip.SetTip(button, text);
        AutomationProperties.SetAutomationId(button, semantic.AutomationId);
        AutomationProperties.SetName(button, semantic.AutomationName);
        button.Click += (_, _) => action();
        return button;
    }

    private void NavigateSideToSidePagePair(FreeWViewDepthPagePairNavigationCommand command)
    {
        if (!_viewSession.CurrentDepth.IsSideToSideActive || _sideToSidePreviewScrollViewer is null)
            return;

        _viewSession.NavigatePagePair(command);
        ApplySideToSideNavigationToScrollViewer(_viewSession.CurrentDepth);
        SyncSideToSideNavigationControls();
        _status.Text = _viewSession.PagePairNavigation.StatusText;
    }

    private void ApplySideToSideNavigationToScrollViewer(FreeWViewDepthPlan plan)
    {
        if (!plan.IsSideToSideActive || _sideToSidePreviewScrollViewer is null)
            return;

        var pairIndex = (_viewSession.PagePairNavigation.FirstVisiblePageNumber - 1) /
            Math.Max(1, _viewSession.PagePairNavigation.PagesPerPair);
        var horizontalOffset = Math.Max(0, pairIndex * _sideToSidePairScrollStrideDip);
        _sideToSidePlannedHorizontalOffsetDip = horizontalOffset;
        _sideToSidePreviewScrollViewer.Offset = new Vector(horizontalOffset, 0);
    }

    private void SyncSideToSideNavigationControls()
    {
        if (_sideToSidePreviousPairButton is not null)
            _sideToSidePreviousPairButton.IsEnabled = _viewSession.PagePairNavigation.CanGoToPreviousPair;
        if (_sideToSideNextPairButton is not null)
            _sideToSideNextPairButton.IsEnabled = _viewSession.PagePairNavigation.CanGoToNextPair;
        if (_sideToSidePairStatusText is not null)
            _sideToSidePairStatusText.Text = _viewSession.PagePairNavigation.StatusText;
    }

    private void ResetSideToSideNavigation()
    {
        _viewSession.ResetPagePairNavigation();
        _sideToSidePreviewScrollViewer = null;
        _sideToSidePreviousPairButton = null;
        _sideToSideNextPairButton = null;
        _sideToSidePairStatusText = null;
        _sideToSidePairScrollStrideDip = 0;
        _sideToSidePlannedHorizontalOffsetDip = 0;
        _sideToSideUsesLiveEditor = false;
        _multiplePagesUsesLiveEditor = false;
    }

    private (double Width, double Height) GetWorkspaceViewportSize(bool compact)
    {
        var bounds = compact && _scroller is not null ? _scroller.Bounds : _workspace.Bounds;
        var width = bounds.Width > 0 ? bounds.Width : Width;
        var height = bounds.Height > 0 ? bounds.Height : Height;
        if (compact)
            height /= 2;

        return (Math.Max(1, width), Math.Max(1, height));
    }

    private ZoomDialogFitFactors ComputeZoomFitFactors()
    {
        var page = _editor.Document.Page;

        var viewportWidth = 0.0;
        var viewportHeight = 0.0;
        if (_scroller is not null)
        {
            viewportWidth = Math.Max(0, _scroller.Bounds.Width - _scroller.Padding.Left - _scroller.Padding.Right);
            viewportHeight = Math.Max(0, _scroller.Bounds.Height - _scroller.Padding.Top - _scroller.Padding.Bottom);
        }

        return ZoomDialogPlanner.BuildFitFactors(page, viewportWidth, viewportHeight);
    }

    /// <summary>
    /// Toggle the document orientation between Portrait and Landscape (AV-PAGE).
    /// Wired to <c>freew.page-orientation</c>.
    /// </summary>
    private void ToggleOrientation()
    {
        _editor.ApplyPageSettings(PageLayoutCommandPlanner.ToggleOrientation);
    }

    /// <summary>
    /// Apply a named margin preset (AV-PAGE).  Recognised names: "normal" (72pt / 1in all
    /// sides), "narrow" (36pt / 0.5in all sides), "wide" (108pt / 1.5in left+right, 72pt top+bottom).
    /// Wired to <c>freew.page-margins-*</c> ribbon commands.
    /// </summary>
    private void ApplyMarginPreset(string preset)
    {
        if (PageLayoutCommandPlanner.TryParseMarginPreset(preset, out var parsed))
            _editor.ApplyPageSettings(page => PageLayoutCommandPlanner.ApplyMarginPreset(page, parsed));
    }

    /// <summary>
    /// Apply a quick paper size (AV-PAGE).  Recognised names: "letter" (612 × 792 pt),
    /// "a4" (595.3 × 841.9 pt). Preserves the current orientation.
    /// Wired to <c>freew.page-size-*</c> ribbon commands.
    /// </summary>
    private void ApplyPaperSize(string name)
    {
        if (PageLayoutCommandPlanner.TryParsePaperSize(name, out var parsed))
            _editor.ApplyPageSettings(page => PageLayoutCommandPlanner.ApplyPaperSize(page, parsed));
    }

    private Control BuildRibbon()
    {
        var callbacks = new RibbonHostCallbacks(
            Open: () => _applicationCommands.Execute(FreeWKeyboardCommand.OpenDocument),
            Save: () => _applicationCommands.Execute(FreeWKeyboardCommand.SaveDocument),
            ImportPdfText: () => _ = ImportPdfTextAsync(),
            Cut: () => _applicationCommands.Execute(FreeWKeyboardCommand.Cut),
            Copy: () => _applicationCommands.Execute(FreeWKeyboardCommand.Copy),
            Paste: () => _applicationCommands.Execute(FreeWKeyboardCommand.Paste),
            PastePlainText: () => _applicationCommands.Execute(FreeWKeyboardCommand.PasteTextOnly),
            PasteMergeFormatting: () => _ = PasteMergeFormattingAsync(),
            OpenPasteSpecial: () => _ = OpenPasteSpecialAsync(),
            OpenNewStyleDialog: () => _ = StyleDialog.ShowNewAndApplyAsync(this, _editor),
            OpenManageStylesDialog: () => _ = ManageStylesDialog.ShowAndApplyAsync(this, _editor),
            Backstage: () => _ = ShowBackstageAsync(),
            NewDocument: () => _applicationCommands.Execute(FreeWKeyboardCommand.NewDocument),
            ToggleNavigationPane: ToggleNavigationPane,
            ToggleReviewingPane: ToggleReviewingPane,
            AcceptThisChange: AcceptSelectedRevision,
            RejectThisChange: RejectSelectedRevision,
            PreviousChange: () => StepRevision(-1),
            NextChange: () => StepRevision(1),
            ToggleRevealFormatting: ToggleRevealFormatting,
            OpenFindReplaceDialog: () => OpenFindReplaceDialog(),
            SetPrintLayout: () => SetViewMode(DocumentViewMode.PrintLayout),
            SetWebLayout:   () => SetViewMode(DocumentViewMode.WebLayout),
            SetDraftView:   () => SetViewMode(DocumentViewMode.Draft),
            IsPrintLayoutActive: () => !_outlineMode && !_pagedEditMode &&
                _editor.ViewMode == DocumentViewMode.PrintLayout,
            IsWebLayoutActive: () => !_outlineMode && !_pagedEditMode &&
                _editor.ViewMode == DocumentViewMode.WebLayout,
            IsDraftViewActive: () => !_outlineMode && !_pagedEditMode &&
                _editor.ViewMode == DocumentViewMode.Draft,
            IsNavigationPaneVisible: () => _navPane.IsVisible,
            IsRevealFormattingVisible: () => _revealPane.IsVisible,
            IsReviewingPaneVisible: () => _reviewingPane.IsVisible,
            SetOutlineView: ToggleOutlineView,
            IsOutlineViewActive: () => _outlineMode,
            OpenFontDialog:      () => _ = OpenFontDialogAsync(),
            OpenParagraphDialog: () => _ = OpenParagraphDialogAsync(),
            OpenPageSetupDialog: () => _ = OpenPageSetupDialogAsync(),
            OpenCustomMarginsDialog: () => _ = OpenPageSetupDialogAsync(PageSetupDialogTab.Margins),
            OpenMorePaperSizesDialog: () => _ = OpenPageSetupDialogAsync(PageSetupDialogTab.Paper),
            OpenColumnsDialog: () => _ = OpenColumnsDialogAsync(),
            OpenCustomParagraphSpacingDialog: () => _ = OpenCustomParagraphSpacingDialogAsync(),
            OpenDropCapOptionsDialog: () => _ = OpenDropCapOptionsDialogAsync(),
            OpenHyphenationOptionsDialog: () => _ = OpenHyphenationOptionsDialogAsync(),
            OpenManualHyphenationDialog: () => _ = OpenManualHyphenationDialogAsync(),
            OpenLineNumberOptionsDialog: () => _ = OpenLineNumberOptionsDialogAsync(),
            OpenPageNumberFormatDialog: () => _ = OpenPageNumberFormatDialogAsync(),
            AskHeaderFooterText: _askHeaderFooterText ??
                ((footer, initial) => HeaderFooterTextDialog.ShowAsync(this, footer, initial)),
            OpenImageCropDialog: () => _ = OpenImageCropDialogAsync(),
            OpenImageSizeDialog: () => _ = OpenImageSizeDialogAsync(),
            OpenImageAltTextDialog: () => _ = OpenImageAltTextDialogAsync(),
            OpenImageBorderDialog: () => _ = OpenImageBorderDialogAsync(),
            OpenImageAdjustDialog: () => _ = OpenImageAdjustDialogAsync(),
            OpenImagePositionDialog: () => _ = OpenImagePositionDialogAsync(),
            OpenShapePositionDialog: () => _ = OpenShapePositionDialogAsync(),
            OpenShapeSizeDialog: () => _ = OpenShapeSizeDialogAsync(),
            OpenShapeAltTextDialog: () => _ = OpenShapeAltTextDialogAsync(),
            OpenInsertChartDialog: () => _ = OpenInsertChartDialogAsync(),
            OpenChartEditDataDialog: () => _ = OpenChartEditDataDialogAsync(),
            OpenChartTitleDialog: () => _ = OpenChartTitleDialogAsync(),
            OpenChartAxisTitlesDialog: () => _ = OpenChartAxisTitlesDialogAsync(),
            OpenChartSizeDialog: () => _ = OpenChartSizeDialogAsync(),
            OpenInsertSmartArtDialog: () => _ = OpenInsertSmartArtDialogAsync(),
            OpenIconPickerDialog: () => _ = OpenIconPickerDialogAsync(),
            OpenTextToTableDialog: () => _ = OpenTextToTableDialogAsync(),
            OpenTableToTextDialog: () => _ = OpenTableToTextDialogAsync(),
            OpenSmartArtEditDialog: () => _ = OpenSmartArtEditDialogAsync(),
            OpenDateTimeDialog: () => _ = OpenDateTimeDialogAsync(),
            OpenMultilevelListDialog: () => _ = OpenMultilevelListDialogAsync(),
            ToggleOrientation:   ToggleOrientation,
            ApplyMarginPreset:   ApplyMarginPreset,
            ApplyPaperSize:      ApplyPaperSize,
            InsertPicture:       () => _ = InsertPictureAsync(),
            InsertObject:        () => _ = InsertEmbeddedObjectAsync(),
            OpenSymbolPickerDialog: () => _ = OpenSymbolPickerAsync(),
            CaptureScreenClip: () => _ = InsertScreenClipAsync(),
            OpenTablePropertiesDialog: context => _ = OpenTablePropertiesDialogAsync(context),
            OpenTableFormulaDialog: state => _ = OpenTableFormulaDialogAsync(state),
            OpenWordCountDialog: () => _ = OpenWordCountDialogAsync(),
            OpenCaptionDialog: () => _ = OpenCaptionDialogAsync(),
            OpenCrossReferenceDialog: () => _ = OpenCrossReferenceDialogAsync(),
            OpenCitationDialog: () => _ = OpenCitationDialogAsync(),
            OpenManageSourcesDialog: () => _ = OpenManageSourcesDialogAsync(),
            OpenMarkIndexEntryDialog: () => _ = OpenMarkIndexEntryDialogAsync(),
            OpenInsertIndexDialog: () => _ = OpenInsertIndexDialogAsync(),
            OpenUpdateIndexDialog: () => _ = OpenUpdateIndexDialogAsync(),
            OpenMarkCitationDialog: () => _ = OpenMarkCitationDialogAsync(),
            OpenFootnoteDialog: () => _ = OpenNoteDialogAsync(footnote: true),
            OpenEndnoteDialog: () => _ = OpenNoteDialogAsync(footnote: false),
            ToggleNotesPane: _notesPane.Toggle,
            IsNotesPaneVisible: () => _notesPane.IsVisible,
            OpenFootnoteEndnoteOptionsDialog: () => _ = OpenFootnoteEndnoteOptionsDialogAsync(),
            ShowTableOfAuthoritiesDialog: () => _ = OpenTableOfAuthoritiesDialogAsync(),
            ApplyZoom: (absolute, delta) =>
            {
                var newScale = absolute.HasValue ? absolute.Value : _zoomScale + delta;
                ApplyZoom(newScale);
            },
            OpenTabsDialog: () => _ = OpenTabsDialogAsync(),
            OpenBordersAndShadingDialog: () => _ = OpenBordersAndShadingDialogAsync(),
            OpenCharacterBorderDialog: () => _ = OpenCharacterBorderDialogAsync(),
            OpenCharacterShadingDialog: () => _ = OpenCharacterShadingDialogAsync(),
            OpenCellShadingDialog: () => _ = OpenCellShadingDialogAsync(),
            OpenSortDialog: () => _ = OpenSortDialogAsync(),
            OpenZoomDialog: () => _ = OpenZoomDialogAsync(),
            OpenPrintPreview: () => _ = OpenPrintPreviewAsync(),
            NewWindow:       OpenNewWindow,
            ArrangeAll:      ArrangeAllWindows,
            ToggleSplit:     ToggleSplit,
            IsSplitActive:   () => _viewSession.CurrentDepth.IsSplitActive,
            ZoomOnePage:     ZoomToOnePage,
            ZoomPageWidth:   ZoomToPageWidth,
            ToggleMultiplePages: ToggleMultiplePages,
            IsMultiplePagesActive: () => _viewSession.CurrentDepth.IsMultiplePagesActive,
            ToggleSideToSide: ToggleSideToSide,
            IsSideToSideActive: () => _viewSession.CurrentDepth.IsSideToSideActive,
            TogglePagedEditView: TogglePagedEditView,
            IsPagedEditViewActive: () => _pagedEditMode,
            // AV-INSERT2: Insert depth 2 dialog launchers (optional callbacks).
            OpenHyperlinkDialog: () => _ = OpenHyperlinkDialogAsync(),
            OpenEditHyperlinkDialog: () => _ = OpenEditHyperlinkDialogAsync(),
            OpenHyperlinkTooltipDialog: () => _ = OpenHyperlinkTooltipDialogAsync(),
            OpenBookmarkDialog:  () => _ = OpenBookmarkDialogAsync(),
            OpenBookmarkManagerDialog: () => _ = OpenBookmarkManagerDialogAsync(),
            OpenLinkBookmarkDialog: () => _ = OpenLinkBookmarkDialogAsync(),
            OpenQuickPartDialog: () => _ = OpenQuickPartDialogAsync(),
            SaveQuickPartSelection: () => _ = SaveQuickPartSelectionAsync(),
            OpenBuildingBlocksOrganizer: () => _ = OpenBuildingBlocksOrganizerAsync(),
            OpenFieldDialog: () => _ = OpenFieldDialogAsync(),
            OpenDrawTableDialog: () => _ = OpenDrawTableDialogAsync(),
            OpenSplitCellDialog: () => _ = OpenSplitCellDialogAsync(),
            InsertTextFromFile:  () => _ = InsertTextFromFileAsync(),
            // AV-MAIL: surface mail-merge info messages in the status bar.
            ShowMailMergeInfo: msg => _status.Text = msg,
            OpenMailDraft: target => TryOpenExternalUri(target) == ExternalUriLaunchResult.Launched,
            // AV-DESIGN: Page Borders + Custom Watermark dialog launchers (optional callbacks).
            OpenPageBordersDialog: () => _ = OpenPageBordersDialogAsync(),
            OpenWatermarkDialog:   () => _ = OpenWatermarkDialogAsync(),
            OpenCustomizeThemeColorsDialog: () => _ = OpenCustomizeThemeColorsDialogAsync(),
            OpenCustomizeThemeFontsDialog: () => _ = OpenCustomizeThemeFontsDialogAsync(),
            OpenPageColorDialog: () => _ = OpenPageColorDialogAsync(),
            // AV-REVIEW: route ribbon safety/protect commands through the same Backstage flows.
            MarkAsFinal: ToggleMarkAsFinal,
            RestrictEditing: () => _ = OpenRestrictEditingAsync(),
            InspectDocument: () => _ = InspectDocumentAsync(),
            CheckAccessibility: () => _ = CheckAccessibilityAsync(),
            ReplyComment: () => _ = ReplyToCommentAsync(),
            ShowComments: rows => _ = ShowCommentsAsync(rows),
            ToggleSpellcheck: ToggleSpellCheck,
            IsSpellcheckActive: () => _editor.SpellCheckEnabled,
            AddToDictionary: AddCurrentWordToDictionary,
            OpenThesaurus: ToggleThesaurusPane,
            SetProofingLanguage: () => _ = OpenProofingLanguageDialogAsync(),
            ToggleReadAloud: ToggleReadAloud,
            IsReadAloudActive: IsReadAloudActive,
            CompareDocuments: () => _ = CompareDocumentsAsync(),
            CombineDocuments: () => _ = CombineDocumentsAsync(),
            OpenHelpOnline: () => _ = OpenExternalHelpLinkAsync(
                FreeWProductInfo.HelpUrl,
                FreeWApplicationFrameTextCatalog.HelpOnlineCommandName),
            OpenFeedback: () => _ = OpenExternalHelpLinkAsync(
                FreeWProductInfo.FeedbackUrl,
                FreeWApplicationFrameTextCatalog.FeedbackCommandName),
            CopyDiagnostics: () => _ = CopyDiagnosticsAsync(),
            CheckForUpdates: () => _ = OpenExternalHelpLinkAsync(
                FreeWProductInfo.LatestReleaseUrl,
                FreeWApplicationFrameTextCatalog.CheckForUpdatesCommandName),
            OpenAbout: () => _ = OpenAboutAsync(),
            OpenLegalNotices: () => _ = OpenLegalNoticesAsync(),
            ToggleReadMode: ToggleReadMode,
            IsReadModeActive: () => _editorInteraction.IsReadModeActive,
            ApplyReadModeColumnWidth: ApplyReadModeColumnWidth,
            ApplyReadModePageColor: ApplyReadModePageColor,
            ToggleReviewBalloons: ToggleReviewBalloons,
            IsReviewBalloonsActive: () => _reviewBalloonsPane.IsVisible);

        // AV-MAIL: capture the Mailings engine so the shell can drive dialog-bound commands with async
        // Avalonia dialogs over the same session the ribbon commands share.
        var registry = FreeWRibbon.BuildRegistry(_editor, callbacks, out var mailMerge);
        _ribbonRegistry = registry;
        _mailMerge = mailMerge;
        registry.Register(new RibbonCommandId("freew.start-mail-merge"),
            new ActionRibbonCommand(() => _ = OpenStartMailMergeAsync()));
        registry.Register(new RibbonCommandId("freew.merge-envelopes"),
            new ActionRibbonCommand(() => _ = OpenEnvelopeAsync()));
        registry.Register(new RibbonCommandId("freew.merge-labels"),
            new ActionRibbonCommand(() => _ = OpenLabelsAsync()));
        registry.Register(new RibbonCommandId("freew.merge-data"),
            new ActionRibbonCommand(() => _ = SelectRecipientsAsync()));
        registry.Register(new RibbonCommandId("freew.merge-edit-recipients"),
            new ActionRibbonCommand(() => _ = SelectRecipientsAsync()));
        registry.Register(new RibbonCommandId("freew.select-recipients"),
            new ActionRibbonCommand(() => _ = SelectRecipientsAsync()));
        registry.Register(new RibbonCommandId("freew.merge-field"),
            new ActionRibbonCommand(() => _ = InsertMergeFieldAsync()));
        registry.Register(new RibbonCommandId("freew.merge-match-fields"),
            new ActionRibbonCommand(() => _ = OpenMatchFieldsAsync()));
        registry.Register(new RibbonCommandId("freew.merge-filter-sort"),
            new ActionRibbonCommand(() => _ = OpenFilterSortAsync()));
        registry.Register(new RibbonCommandId("freew.merge-preview"),
            new ActionRibbonCommand(() => _ = OpenPreviewNavigationAsync()));
        registry.Register(new RibbonCommandId("freew.merge-finish"),
            new ActionRibbonCommand(() => _ = OpenFinishMergeAsync()));
        registry.Register(new RibbonCommandId("freew.finish-merge"),
            new ActionRibbonCommand(() => _ = OpenFinishMergeAsync()));
        registry.Register(new RibbonCommandId("freew.merge-find-recipient"),
            new ActionRibbonCommand(() => _ = OpenFindRecipientAsync()));
        registry.Register(new RibbonCommandId("freew.merge-check-errors"),
            new ActionRibbonCommand(() => _ = OpenCheckForErrorsAsync()));
        registry.Register(new RibbonCommandId("freew.merge-email"),
            new ActionRibbonCommand(() => _ = PlanEmailMergeAsync()));
        registry.Register(new RibbonCommandId("freew.merge-rule-if"),
            new ActionRibbonCommand(() => _ = InsertMergeRuleIfAsync()));
        registry.Register(new RibbonCommandId("freew.merge-rule-skip-record-if"),
            new ActionRibbonCommand(() => _ = InsertMergeRuleConditionAsync(skipRecord: true)));
        registry.Register(new RibbonCommandId("freew.merge-rule-next-record-if"),
            new ActionRibbonCommand(() => _ = InsertMergeRuleConditionAsync(skipRecord: false)));
        registry.Register(new RibbonCommandId("freew.merge-rule-fill-in"),
            new ActionRibbonCommand(() => _ = InsertMergeRulePromptAsync("Fill-in", "Enter the prompt text for this Fill-in field:",
                prompt => _mailMerge?.InsertFillInRule(prompt))));
        registry.Register(new RibbonCommandId("freew.merge-rule-ask"),
            new ActionRibbonCommand(() => _ = InsertMergeRuleNameValueAsync("Ask", "Prompt text:",
                result => _mailMerge?.InsertAskRule(result.Name, result.Value))));
        registry.Register(new RibbonCommandId("freew.merge-rule-set"),
            new ActionRibbonCommand(() => _ = InsertMergeRuleNameValueAsync("Set Bookmark", "Value:",
                result => _mailMerge?.InsertSetRule(result.Name, result.Value))));
        registry.Register(new RibbonCommandId("freew.merge-rule-ref"),
            new ActionRibbonCommand(() => _ = InsertMergeRulePromptAsync("Ref Bookmark", "Enter the bookmark name to reference:",
                prompt => _mailMerge?.InsertRefRule(prompt))));
        // AV-PICTAB: merge the Table (caret-in-cell) and Floating (picture/drawing selected)
        // contextual triggers so both sets of contextual tabs can surface from one source.
        var contextSource = new CompositeRibbonContextSource(
            new TableRibbonContextSource(_editor),
            new HeaderFooterRibbonContextSource(_editor),
            new FloatingRibbonContextSource(_editor));
        // The shared Avalonia renderer owns the shell File tab so its F key tip can route
        // through the same Backstage callback as FreeP. FreeW's canonical definition keeps
        // its Avalonia-only File command group for the portable catalog, but it must not be
        // rendered a second time beside the shell tab.
        var canonicalDefinition = FreeWRibbon.BuildDefinition();
        var definition = canonicalDefinition with
        {
            Tabs = canonicalDefinition.Tabs
                .Where(tab => !string.Equals(tab.Id, "file", StringComparison.Ordinal))
                .ToArray(),
        };
        _ribbonControl = AvaloniaRibbonRenderer.BuildRibbon(
            definition,
            registry,
            contextSource: contextSource,
            afterExecute: () =>
            {
                RefreshRibbonCommandStates();
                _editor.Focus();
            },
            palette: RibbonVisualPalette.FromTheme(App.ActiveTheme),
            onFileTabSelected: () => _ = ShowBackstageAsync(),
            stateStore: _ribbonStateStore);
        HasToolbar = true;
        _ribbonHost = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = _ribbonControl,
        };
        return _ribbonHost;
    }

    // AV-MAIL: Mailings > Select Recipients. Prompt for a CSV recipient list (seeded with the document's
    // existing merge-field names as the header hint), then load it into the shared merge session.
    private async Task SelectRecipientsAsync()
    {
        if (_mailMerge is null)
            return;
        var fields = FreeW.Core.Model.MailMerge.FieldNames(_editor.Document);
        var dialogPlan = MailMergeRecipientDialogPlanner.CreatePlan(
            fields,
            _mailMerge.Session.Data);
        var csv = await MailMergeDialogs.AskRecipientCsvAsync(
            this,
            dialogPlan.SeedHeader,
            dialogPlan.InitialCsv);
        if (string.IsNullOrWhiteSpace(csv))
            return;
        var transition = _mailMerge.LoadRecipientsCsvWithTransition(csv);
        _status.Text = transition.Message;
        _editor.Focus();
    }

    private async Task OpenStartMailMergeAsync()
    {
        if (_mailMerge is null)
            return;
        var type = await MailMergeDialogs.AskStartMailMergeAsync(this);
        switch (type)
        {
            case MailMergeStartType.Letters: _mailMerge.StartMailMergeLetters(); break;
            case MailMergeStartType.Directory: _mailMerge.StartMailMergeDirectory(); break;
            case MailMergeStartType.NormalDocument: _mailMerge.ClearMergeSession(); break;
        }
        _editor.Focus();
    }

    private async Task OpenEnvelopeAsync()
    {
        var result = await MailMergeDialogs.AskEnvelopeAsync(this);
        if (result is { } envelope)
        {
            _editor.ApplyPageSettings(page =>
            {
                page.WidthPt = envelope.WidthPt;
                page.HeightPt = envelope.HeightPt;
                page.MarginLeftPt = envelope.MarginPt;
                page.MarginRightPt = envelope.MarginPt;
                page.MarginTopPt = envelope.MarginPt;
                page.MarginBottomPt = envelope.MarginPt;
                page.Landscape = envelope.Landscape;
            });
        }
        _editor.Focus();
    }

    private async Task OpenLabelsAsync()
    {
        var result = await MailMergeDialogs.AskLabelsAsync(this);
        if (result is { } labels)
            _mailMerge?.ApplyLabels(labels);
        _editor.Focus();
    }

    private async Task<bool> ValidateMailMergeOperationAsync(MailMergeOperation operation)
    {
        if (_mailMerge is null)
            return false;

        var validation = _mailMerge.ValidateOperation(operation);
        if (validation.IsValid)
            return true;

        await FreeWInfoDialog.ShowAsync(this, validation.Message);
        return false;
    }

    private async Task OpenMatchFieldsAsync()
    {
        if (!await ValidateMailMergeOperationAsync(MailMergeOperation.MatchFields))
            return;

        var data = _mailMerge!.Session.Data!;
        var mapping = await MailMergeDialogs.AskMatchFieldsAsync(
            this, data.Header, _mailMerge.Session.Mapping ?? new FieldMapping());
        if (mapping is not null)
            _mailMerge.ApplyFieldMapping(mapping);
        _editor.Focus();
    }

    private async Task OpenFilterSortAsync()
    {
        if (!await ValidateMailMergeOperationAsync(MailMergeOperation.FilterSortRecipients))
            return;

        var data = _mailMerge!.Session.Data!;
        var filtered = await MailMergeDialogs.AskFilterSortRecipientsAsync(this, data);
        if (filtered is not null)
            _mailMerge.ApplyRecipientFilter(filtered);
        _editor.Focus();
    }

    private async Task OpenPreviewNavigationAsync()
    {
        if (_mailMerge is null)
            return;
        if (!await ValidateMailMergeOperationAsync(MailMergeOperation.PreviewRecord))
            return;
        if (!_mailMerge.EnsurePreviewingForNavigation())
            return;
        var data = _mailMerge.Session.Data!;
        var action = await MailMergeDialogs.AskPreviewNavigationAsync(
            this, _mailMerge.Session.CurrentIndex, data.Count);
        switch (action)
        {
            case MailMergePreviewDialogAction.MovePrevious: _mailMerge.PreviousRecord(); break;
            case MailMergePreviewDialogAction.MoveNext: _mailMerge.NextRecord(); break;
            case MailMergePreviewDialogAction.Done: _mailMerge.TogglePreview(); break;
        }
        _editor.Focus();
    }

    private async Task OpenFindRecipientAsync()
    {
        if (!await ValidateMailMergeOperationAsync(MailMergeOperation.FindRecipient))
        {
            _editor.Focus();
            return;
        }
        var query = await MailMergeDialogs.AskFindRecipientAsync(this);
        if (query is null)
        {
            _editor.Focus();
            return;
        }

        var result = _mailMerge!.FindRecipient(query);
        await FreeWInfoDialog.ShowAsync(this, result.Message);
        _editor.Focus();
    }

    private async Task OpenCheckForErrorsAsync()
    {
        if (!await ValidateMailMergeOperationAsync(MailMergeOperation.CheckForErrors))
            return;
        var mode = await MailMergeDialogs.AskCheckForErrorsAsync(this);
        if (mode is not { } selected)
            return;

        var execution = _mailMerge!.CheckForErrorsPlan(selected);
        if (execution.Success && execution.Result is { } result)
        {
            foreach (var message in execution.Messages)
                await FreeWInfoDialog.ShowAsync(this, message);

            if (result.ShouldCompleteMerge)
            {
                var plan = MailMergeFinishPlanner.PlanNewDocumentAllRecords(
                    _mailMerge.Session.Data!.Count);
                await ExecuteFinishMergePlanAsync(plan);
            }

            if (execution.ReportDocument is { } report)
                OpenMailMergeErrorReport(report);
        }
        _editor.Focus();
    }

    private async Task OpenFinishMergeAsync()
    {
        if (_mailMerge is null)
            return;
        if (!await ValidateMailMergeOperationAsync(MailMergeOperation.FinishMerge))
            return;

        var data = _mailMerge.Session.Data!;
        var plan = await MailMergeDialogs.AskFinishMergeAsync(
            this, data.Count, _mailMerge.Session.CurrentIndex);
        if (plan is not null)
            await ExecuteFinishMergePlanAsync(plan);
        _editor.Focus();
    }

    private async Task ExecuteFinishMergePlanAsync(MailMergeFinishPlan plan)
    {
        if (_mailMerge is null || !plan.Success)
            return;

        var route = _mailMerge.RouteFinish(
            plan,
            printingAvailable: true,
            emailAvailable: true);
        if (!route.Success)
            return;
        if (route.Route == MailMergeFinishRoute.Email)
        {
            await PlanEmailMergeAsync(route.EmailRecordIndexes);
            return;
        }

        var mergeState = await CollectInteractiveMergeAnswersAsync();
        if (mergeState is null)
            return;

        mergeState.RecordPromptResolver = ResolvePerRecordMergePrompt;

        // Snapshot the template on the UI thread before backgrounding the merge. Outside preview the
        // merge would otherwise iterate the live document's Blocks and Styles once per record while
        // the editor stays fully typeable — a keystroke that splits a paragraph mid-merge throws
        // "collection was modified" on the background thread. Running the merge inline is not an
        // option: its per-record prompts post to the UI thread and wait, so that would deadlock.
        var templateSnapshot = _mailMerge.Session.IsPreviewing
            ? null
            : FreeWDocumentSnapshot.Clone(_editor.Document);
        var result = await Task.Run(() => _mailMerge.BuildFinishedMerge(plan, mergeState, templateSnapshot));
        if (result is null)
            return;

        if (route.Route == MailMergeFinishRoute.NewDocument)
        {
            _mailMerge.ApplyFinishedMerge(result);
            return;
        }

        if (route.Route == MailMergeFinishRoute.Printer)
            await PrintAsync(result.Document);
    }

    private string? ResolvePerRecordMergePrompt(
        MailMergeInteractivePrompt prompt,
        int _)
    {
        var completion = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var title = prompt.Kind == MailMergeInteractivePromptKind.FillIn ? "Fill-in" : "Ask";
                completion.SetResult(await MailMergeDialogs.AskMergeRulePromptAsync(
                    this,
                    title,
                    prompt.Prompt,
                    prompt.DefaultAnswer));
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });
        return completion.Task.GetAwaiter().GetResult();
    }

    private async Task<MergeState?> CollectInteractiveMergeAnswersAsync()
    {
        if (_mailMerge is null)
            return null;

        var state = new MergeState();
        foreach (var prompt in _mailMerge.GetInteractiveFinishPrompts())
        {
            var title = prompt.Kind == MailMergeInteractivePromptKind.FillIn ? "Fill-in" : "Ask";
            var answer = await MailMergeDialogs.AskMergeRulePromptAsync(
                this, title, prompt.Prompt, prompt.DefaultAnswer);
            if (answer is null)
                return null;

            MailMergeInteractivePromptPlanner.ApplyResponse(state, prompt, answer);
        }

        return state;
    }

    // AV-MAIL: Mailings > Insert Merge Field. Pick / type a field name (seeded with the loaded recipient
    // list's columns), then insert the «Field» placeholder at the caret through the undoable edit path.
    private async Task InsertMergeFieldAsync()
    {
        if (_mailMerge is null)
            return;
        var name = await MailMergeDialogs.AskMergeFieldNameAsync(this, _mailMerge.AvailableFieldNames);
        if (string.IsNullOrWhiteSpace(name))
            return;
        _mailMerge.InsertMergeFieldNamed(name);
        _editor.Focus();
    }

    private async Task PlanEmailMergeAsync(IReadOnlyList<int>? selectedRecordIndexes = null)
    {
        if (_mailMerge is null)
            return;
        if (_mailMerge.Session.Data is not { Count: > 0 } data)
        {
            _mailMerge.PlanEmailMerge();
            return;
        }

        var intent = await MailMergeDialogs.AskEmailMergeDeliveryAsync(
            this,
            data,
            _mailMerge.Session.CurrentIndex,
            selectedRecordIndexes ?? Array.Empty<int>());
        if (intent is null)
            return;

        _mailMerge.PlanEmailMerge(intent);
        _editor.Focus();
    }

    private async Task InsertMergeRuleIfAsync()
    {
        if (_mailMerge is null)
            return;

        var result = await MailMergeDialogs.AskMergeRuleIfAsync(this, _mailMerge.AvailableFieldNames);
        if (result is null)
            return;

        _mailMerge.InsertIfRule(result);
        _editor.Focus();
    }

    private async Task InsertMergeRuleConditionAsync(bool skipRecord)
    {
        if (_mailMerge is null)
            return;

        var title = skipRecord ? "Skip Record If" : "Next Record If";
        var result = await MailMergeDialogs.AskMergeRuleConditionAsync(this, _mailMerge.AvailableFieldNames, title);
        if (result is null)
            return;

        if (skipRecord)
            _mailMerge.InsertSkipRecordIfRule(result);
        else
            _mailMerge.InsertNextRecordIfRule(result);
        _editor.Focus();
    }

    private async Task InsertMergeRulePromptAsync(string title, string prompt, Action<string> apply)
    {
        var result = await MailMergeDialogs.AskMergeRulePromptAsync(this, title, prompt);
        if (result is null)
            return;

        apply(result);
        _editor.Focus();
    }

    private async Task InsertMergeRuleNameValueAsync(
        string title,
        string valueLabel,
        Action<MailMergeRuleNameValueDialogResult> apply)
    {
        var result = await MailMergeDialogs.AskMergeRuleNameValueAsync(this, title, valueLabel);
        if (result is null)
            return;

        apply(result.Value);
        _editor.Focus();
    }

    // OS clipboard via Avalonia's data-transfer API (same pattern as the FreeX shell):
    // TopLevel.Clipboard with SetTextAsync / TryGetTextAsync.
    private Control BuildFindBar()
    {
        var next = new Button { Content = "Find Next", Padding = new Thickness(10, 4), Margin = new Thickness(6, 0, 0, 0) };
        next.Click += (_, _) => DoFind();
        _findBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                DoFind();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                ToggleFindBar(show: false);
                e.Handled = true;
            }
        };

        var replace = new Button { Content = "Replace", Padding = new Thickness(10, 4), Margin = new Thickness(6, 0, 0, 0) };
        replace.Click += (_, _) => DoReplace();
        var replaceAll = new Button { Content = "Replace All", Padding = new Thickness(6, 4), Margin = new Thickness(4, 0, 0, 0) };
        replaceAll.Click += (_, _) => DoReplaceAll();

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8, 4),
            Children =
            {
                new TextBlock { Text = "Find:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) },
                _findBox,
                next,
                new TextBlock { Text = "Replace:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 6, 0) },
                _replaceBox,
                replace,
                replaceAll,
            },
        };
        _findBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF7)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            IsVisible = false,
            Child = row,
        };
        return _findBar;
    }

    private Border BuildStatusBar()
    {
        var white = AvaloniaThemeResourceResolver.ResolveOr<IBrush>(ThemeResources.WhiteBrush, Brushes.White);
        _pageStatus = SisterAppStatusBarChrome.CreateInfoText(foreground: white);
        _sectionStatus = SisterAppStatusBarChrome.CreateInfoText(foreground: white);
        _status = SisterAppStatusBarChrome.CreateInfoText(foreground: white);
        _dataFolderStatus = SisterAppStatusBarChrome.CreateInfoText(foreground: white);
        _dataFolderStatus.Text = SisterAppStatusBarTextPlanner.FormatDataFolderStatus(
            FreeWApplicationFrameDescriptor.ResolveDataFolderLabel(_optionsStore.StorePath));
        ToolTip.SetTip(_dataFolderStatus, _dataFolderStatus.Text);

        _dataFolderItemControl = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                SisterAppStatusBarChrome.CreateSeparator(),
                _dataFolderStatus,
            },
        };

        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            ClipToBounds = true,
            Children =
            {
                _pageStatus,
                SisterAppStatusBarChrome.CreateSeparator(),
                _sectionStatus,
                SisterAppStatusBarChrome.CreateSeparator(),
                _status,
                _dataFolderItemControl,
            },
        };

        var viewSwitch = BuildViewSwitchControl(white);
        var zoom = BuildZoomControl(white);
        _statusViewSwitchControl = viewSwitch;
        _statusZoomControl = zoom;
        _statusBar = SisterAppStatusBarChrome.Build(new SisterAppStatusBarSpec(
            Background: AvaloniaThemeResourceResolver.ResolveOr<IBrush>(
                ThemeResources.StatusSurfaceBrush,
                new SolidColorBrush(Color.FromRgb(0x17, 0x32, 0x4D))),
            LeftContent: left,
            RightItems: [viewSwitch, zoom])).Root;
        return _statusBar;
    }

    private Control BuildViewSwitchControl(IBrush foreground)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0),
        };

        _readModeSwitch = BuildStatusButton(
            FreeWApplicationFrameTextCatalog.ReadMode.Label,
            FreeWApplicationFrameTextCatalog.ReadMode.HelpText,
            RibbonCommandIconKind.ReadMode,
            foreground,
            ToggleReadMode);
        _printLayoutSwitch = BuildStatusToggle(
            FreeWApplicationFrameTextCatalog.PrintLayout.Label,
            FreeWApplicationFrameTextCatalog.PrintLayout.HelpText,
            RibbonCommandIconKind.PrintLayout,
            foreground,
            () => SetViewMode(DocumentViewMode.PrintLayout));
        _webLayoutSwitch = BuildStatusToggle(
            FreeWApplicationFrameTextCatalog.WebLayoutLabel,
            "Web Layout: continuous, full-width view",
            RibbonCommandIconKind.WebLayout,
            foreground,
            () => SetViewMode(DocumentViewMode.WebLayout));
        _draftSwitch = BuildStatusToggle(
            FreeWApplicationFrameTextCatalog.Draft.Label,
            FreeWApplicationFrameTextCatalog.Draft.HelpText,
            RibbonCommandIconKind.Draft,
            foreground,
            () => SetViewMode(DocumentViewMode.Draft));
        _pagedEditSwitch = BuildStatusToggle(
            FreeWApplicationFrameTextCatalog.PageEditLabel,
            "Page Edit: editable paginated page boxes",
            RibbonCommandIconKind.PrintLayout,
            foreground,
            TogglePagedEditView);

        panel.Children.Add(_readModeSwitch);
        panel.Children.Add(_printLayoutSwitch);
        panel.Children.Add(_webLayoutSwitch);
        panel.Children.Add(_draftSwitch);
        panel.Children.Add(_pagedEditSwitch);
        return panel;
    }

    private static Button BuildStatusButton(
        string name,
        string helpText,
        RibbonCommandIconKind icon,
        IBrush foreground,
        Action onClick)
    {
        var button = new Button
        {
            Content = AvaloniaRibbonIcons.BuildMonochrome(icon, 13, name, foreground),
            Width = 24,
            Height = 22,
            Padding = new Thickness(4, 2),
            Margin = new Thickness(1, 2),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
        };
        AutomationProperties.SetName(button, name);
        AutomationProperties.SetHelpText(button, helpText);
        ToolTip.SetTip(button, helpText);
        button.Click += (_, _) => onClick();
        return button;
    }

    private static ToggleButton BuildStatusToggle(
        string name,
        string helpText,
        RibbonCommandIconKind icon,
        IBrush foreground,
        Action onClick)
    {
        var button = new ToggleButton
        {
            Content = AvaloniaRibbonIcons.BuildMonochrome(icon, 13, name, foreground),
            Width = 24,
            Height = 22,
            Padding = new Thickness(4, 2),
            Margin = new Thickness(1, 2),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
        };
        AutomationProperties.SetName(button, name);
        AutomationProperties.SetHelpText(button, helpText);
        ToolTip.SetTip(button, helpText);
        button.Click += (_, _) => onClick();
        return button;
    }

    private Control BuildZoomControl(IBrush foreground)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 10, 0),
        };

        _zoomSlider = new Slider
        {
            Minimum = ZoomLevels.Min,
            Maximum = ZoomLevels.Max,
            Value = ZoomLevels.Default,
            Width = 120,
            TickFrequency = ZoomLevels.Step,
            IsSnapToTickEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(_zoomSlider, "Zoom");
        ToolTip.SetTip(_zoomSlider, "Zoom");
        _zoomSlider.PropertyChanged += (_, e) =>
        {
            if (!_updatingZoomSlider && e.Property == RangeBase.ValueProperty)
                ApplyZoom(_zoomSlider.Value);
        };

        _zoomLabel = SisterAppStatusBarChrome.CreateInfoText(
            "100%",
            foreground,
            new Thickness(6, 0, 2, 0));
        _zoomLabel.MinWidth = 38;
        _zoomLabel.TextAlignment = global::Avalonia.Media.TextAlignment.Right;

        panel.Children.Add(BuildZoomButton("\u2212", "Zoom out", foreground, () => ApplyZoom(ZoomLevels.StepDown(_zoomScale))));
        panel.Children.Add(_zoomSlider);
        panel.Children.Add(BuildZoomButton("+", "Zoom in", foreground, () => ApplyZoom(ZoomLevels.StepUp(_zoomScale))));

        var percentage = new Button
        {
            Content = _zoomLabel,
            Padding = new Thickness(2, 0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
        };
        AutomationProperties.SetName(percentage, "Zoom");
        ToolTip.SetTip(percentage, "Zoom");
        percentage.Click += (_, _) => _ = OpenZoomDialogAsync();
        panel.Children.Add(percentage);
        return panel;
    }

    private static Button BuildZoomButton(string glyph, string name, IBrush foreground, Action onClick)
    {
        var button = new Button
        {
            Content = glyph,
            Foreground = foreground,
            Width = 24,
            Height = 22,
            Padding = new Thickness(0),
            Margin = new Thickness(2, 3),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
        };
        AutomationProperties.SetName(button, name);
        ToolTip.SetTip(button, name);
        button.Click += (_, _) => onClick();
        return button;
    }

    // View > Outline swaps the live editor surface for the dedicated outline control. The underlying
    // DocumentView remains alive so leaving Outline restores the exact prior editor/view state.
    private void ToggleOutlineView()
    {
        if (_outlineMode)
        {
            LeaveOutlineView();
            return;
        }

        if (_viewSession.CurrentDepth.Mode != FreeWViewDepthMode.LiveEditor)
            ApplyViewDepthTransition(_viewSession.RestoreLiveEditor(), updateStatus: false);

        _pagedEditModeBeforeOutline = _pagedEditMode;
        _pagedEditMode = false;
        _outlineMode = true;
        _workspace.Child = _outlineView;
        _outlineView.Refresh();
        UpdateViewModeButtons();
        UpdateStatus();
        RefreshRibbonCommandStates();
    }

    private void LeaveOutlineView(bool restorePriorView = true)
    {
        if (!_outlineMode)
            return;

        _outlineMode = false;
        _pagedEditMode = restorePriorView && _pagedEditModeBeforeOutline;
        if (_liveWorkspaceContent is not null)
            _workspace.Child = _liveWorkspaceContent;
        UpdateViewModeButtons();
        UpdateStatus();
        RefreshRibbonCommandStates();
        _editor.Focus();
    }

    private void SetViewMode(DocumentViewMode mode)
    {
        var plan = _viewSession.PlanDocumentViewChange(
            _editor.ViewMode,
            _outlineMode,
            _pagedEditMode,
            mode);

        if (plan.ExitOutlineMode)
            LeaveOutlineView(restorePriorView: false);

        if (plan.ExitPaginatedView)
            ApplyViewDepthTransition(_viewSession.RestoreLiveEditor(), updateStatus: false);

        if (plan.ExitPagedEditMode)
            _pagedEditMode = false;
        _editor.ViewMode = plan.TargetMode;
        if (_viewSession.CurrentDepth.IsSplitActive)
            RefreshSplitPreviewSnapshot();
        UpdateViewModeButtons();
        RefreshRibbonCommandStates();
        _editor.Focus();
    }

    private void UpdateViewModeButtons()
    {
        var plan = _viewSession.BuildDocumentViewChecks(
            _editor.ViewMode,
            _outlineMode,
            _pagedEditMode);
        ApplyStatusToggleState(_printLayoutSwitch, plan.PrintLayout);
        ApplyStatusToggleState(_webLayoutSwitch, plan.WebLayout);
        ApplyStatusToggleState(_draftSwitch, plan.Draft);
        ApplyStatusToggleState(_pagedEditSwitch, plan.PagedEdit);
    }

    private static void ApplyStatusToggleState(ToggleButton toggle, bool isChecked)
    {
        toggle.IsChecked = isChecked;
        toggle.Background = isChecked
            ? new SolidColorBrush(Color.FromArgb(0x45, 0x10, 0x25, 0x3A))
            : Brushes.Transparent;
        toggle.BorderBrush = isChecked
            ? new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF))
            : Brushes.Transparent;
    }

    private void TogglePagedEditView()
    {
        if (_outlineMode)
            LeaveOutlineView(restorePriorView: false);

        if (_pagedEditMode)
        {
            _pagedEditMode = false;
            _editor.ViewMode = _viewModeBeforePagedEdit;
        }
        else
        {
            if (_viewSession.CurrentDepth.Mode != FreeWViewDepthMode.LiveEditor)
                ApplyViewDepthTransition(_viewSession.RestoreLiveEditor(), updateStatus: false);
            _viewModeBeforePagedEdit = _editor.ViewMode;
            _editor.ViewMode = DocumentViewMode.PrintLayout;
            _pagedEditMode = true;
        }

        UpdateViewModeButtons();
        UpdateStatus();
        RefreshRibbonCommandStates();
        _editor.Focus();
    }

    private void ToggleReadMode()
    {
        var plan = _editorInteraction.ToggleReadMode(new FreeWEditorChromeVisibility(
            TitleBar: ToChromeVisibility(_titleBar.IsVisible),
            Ribbon: ToChromeVisibility(_ribbonHost?.IsVisible == true),
            DataFolder: ToChromeVisibility(_dataFolderItemControl?.IsVisible == true),
            ViewSwitch: ToChromeVisibility(_statusViewSwitchControl?.IsVisible == true),
            Zoom: ToChromeVisibility(_statusZoomControl?.IsVisible == true),
            NavigationPane: ToChromeVisibility(_navPane.IsVisible),
            RevealPane: ToChromeVisibility(_revealPane.IsVisible),
            ReviewingPane: ToChromeVisibility(_reviewingPane.IsVisible)));

        if (plan.IsActive)
        {
            _workspaceBackgroundBeforeReadMode = _workspace.Background ?? Brushes.Transparent;
            _editorMaxWidthBeforeReadMode = _editor.MaxWidth;
            _editorAlignmentBeforeReadMode = _editor.HorizontalAlignment;
            _editorMarginBeforeReadMode = _editor.Margin;
        }

        _titleBar.IsVisible = IsChromeVisible(plan.Chrome.TitleBar);
        if (_ribbonHost is not null)
            _ribbonHost.IsVisible = IsChromeVisible(plan.Chrome.Ribbon);
        if (_dataFolderItemControl is not null)
            _dataFolderItemControl.IsVisible = IsChromeVisible(plan.Chrome.DataFolder);
        if (_statusViewSwitchControl is not null)
            _statusViewSwitchControl.IsVisible = IsChromeVisible(plan.Chrome.ViewSwitch);
        if (_statusZoomControl is not null)
            _statusZoomControl.IsVisible = IsChromeVisible(plan.Chrome.Zoom);
        _navPane.IsVisible = IsChromeVisible(plan.Chrome.NavigationPane);
        _revealPane.IsVisible = IsChromeVisible(plan.Chrome.RevealPane);
        _reviewingPane.IsVisible = IsChromeVisible(plan.Chrome.ReviewingPane);

        if (plan.IsActive)
        {
            _editor.MaxWidth = plan.ColumnWidth;
            _editor.HorizontalAlignment = HorizontalAlignment.Center;
            _editor.Margin = new Thickness(40);
            _editor.ViewBackgroundColorHex = plan.PageColorHex;
            _workspace.Background = new SolidColorBrush(ParseColor(plan.PageColorHex));
        }
        else
        {
            _editor.MaxWidth = _editorMaxWidthBeforeReadMode;
            _editor.HorizontalAlignment = _editorAlignmentBeforeReadMode;
            _editor.Margin = _editorMarginBeforeReadMode;
            _editor.ViewBackgroundColorHex = null;
            _workspace.Background = _workspaceBackgroundBeforeReadMode;
        }

        UpdateViewModeButtons();
        UpdateStatus();
        RefreshRibbonCommandStates();
        _editor.Focus();
    }

    private static FreeWChromeVisibility ToChromeVisibility(bool isVisible) =>
        isVisible ? FreeWChromeVisibility.Visible : FreeWChromeVisibility.Collapsed;

    private static bool IsChromeVisible(FreeWChromeVisibility visibility) =>
        visibility == FreeWChromeVisibility.Visible;

    private void ApplyReadModeColumnWidth(string token)
    {
        var plan = _editorInteraction.UpdateReadModeColumnWidth(token);
        if (plan.ApplyImmediately)
            _editor.MaxWidth = plan.ColumnWidth;
    }

    private void ApplyReadModePageColor(string token)
    {
        var plan = _editorInteraction.UpdateReadModePageColor(token);
        if (plan.ApplyImmediately)
        {
            _editor.ViewBackgroundColorHex = plan.PageColorHex;
            _workspace.Background = new SolidColorBrush(ParseColor(plan.PageColorHex));
        }
    }

    private static Color ParseColor(string hex) =>
        DrawingMlRgbColor.TryParseHexRgb(hex, out var color)
            ? Color.FromRgb(color.R, color.G, color.B)
            : Colors.Black;

    internal bool IsReadModeActiveForTests => _editorInteraction.IsReadModeActive;
    internal double ReadModeMaxWidthForTests => _editor.MaxWidth;
    internal string? ReadModeBackgroundForTests => _editor.ViewBackgroundColorHex;
    internal bool IsRibbonVisibleForTests => _ribbonHost?.IsVisible == true;
    internal bool IsTitleBarVisibleForTests => _titleBar.IsVisible;
    internal bool IsNavigationPaneVisibleForTests => _navPane.IsVisible;
    internal bool IsRevealPaneVisibleForTests => _revealPane.IsVisible;
    internal bool IsReviewingPaneVisibleForTests => _reviewingPane.IsVisible;
    internal int RibbonStateRefreshCountForTests => _ribbonStateRefreshCount;
    internal void SetReadModePaneVisibilityForTests(bool navigation, bool reveal, bool reviewing)
    {
        _navPane.IsVisible = navigation;
        _revealPane.IsVisible = reveal;
        _reviewingPane.IsVisible = reviewing;
    }
    internal void ToggleReadModeForTests() => ToggleReadMode();
    internal void ApplyReadModeColumnWidthForTests(string token) => ApplyReadModeColumnWidth(token);
    internal void ApplyReadModePageColorForTests(string token) => ApplyReadModePageColor(token);

    private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (TryHandleRibbonKeyTips(e))
            return;

        if (TryMapKeyboardKey(e.Key, out var key) &&
            _applicationCommands.TryExecute(
                key,
                ToKeyboardModifiers(e.KeyModifiers)))
        {
            e.Handled = true;
            return;
        }

        var ctrl = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        if (!ctrl)
            return;

        switch (e.Key)
        {
            case Key.P when (e.KeyModifiers & KeyModifiers.Shift) != 0: _ = ExportPdfAsync(); e.Handled = true; break;
            case Key.OemPlus or Key.Add: ApplyZoom(_zoomScale + 0.1); e.Handled = true; break;
            case Key.OemMinus or Key.Subtract: ApplyZoom(_zoomScale - 0.1); e.Handled = true; break;
            case Key.D0 or Key.NumPad0: ApplyZoom(1.0); e.Handled = true; break;
        }
    }

    private bool TryHandleRibbonKeyTips(KeyEventArgs args)
    {
        if (args.Key is Key.LeftAlt or Key.RightAlt)
        {
            SetRibbonKeyTipsVisible(!_ribbonKeyTipsVisible);
            args.Handled = true;
            return true;
        }

        if (args.Key == Key.F10 && args.KeyModifiers == KeyModifiers.None)
        {
            SetRibbonKeyTipsVisible(!_ribbonKeyTipsVisible);
            args.Handled = true;
            return true;
        }

        if (!_ribbonKeyTipsVisible)
            return false;

        if (args.Key == Key.Escape)
        {
            SetRibbonKeyTipsVisible(false);
            args.Handled = true;
            return true;
        }

        var token = AvaloniaKeyTipTokenFormatter.Format(args.Key);
        if (token is null || _ribbonControl is null)
            return false;

        if (!AvaloniaRibbonRenderer.TryActivateTopLevelKeyTip(_ribbonControl, token))
            return false;

        SetRibbonKeyTipsVisible(false);
        args.Handled = true;
        return true;
    }

    private void SetRibbonKeyTipsVisible(bool visible)
    {
        _ribbonKeyTipsVisible = visible;
        if (_ribbonControl is not null)
            AvaloniaRibbonRenderer.SetTopLevelKeyTipsVisible(_ribbonControl, visible);
    }

    private static FreeWKeyboardModifiers ToKeyboardModifiers(KeyModifiers modifiers)
    {
        var result = FreeWKeyboardModifiers.None;
        if ((modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
            result |= FreeWKeyboardModifiers.Control;
        if ((modifiers & KeyModifiers.Shift) != 0)
            result |= FreeWKeyboardModifiers.Shift;
        if ((modifiers & KeyModifiers.Alt) != 0)
            result |= FreeWKeyboardModifiers.Alt;
        return result;
    }

    private static bool TryMapKeyboardKey(Key key, out FreeWKeyboardKey mapped)
    {
        mapped = key switch
        {
            Key.A => FreeWKeyboardKey.A,
            Key.C => FreeWKeyboardKey.C,
            Key.F => FreeWKeyboardKey.F,
            Key.H => FreeWKeyboardKey.H,
            Key.N => FreeWKeyboardKey.N,
            Key.O => FreeWKeyboardKey.O,
            Key.P => FreeWKeyboardKey.P,
            Key.S => FreeWKeyboardKey.S,
            Key.V => FreeWKeyboardKey.V,
            Key.X => FreeWKeyboardKey.X,
            Key.Y => FreeWKeyboardKey.Y,
            Key.Z => FreeWKeyboardKey.Z,
            Key.F1 => FreeWKeyboardKey.F1,
            Key.F7 => FreeWKeyboardKey.F7,
            Key.F9 => FreeWKeyboardKey.F9,
            Key.F11 => FreeWKeyboardKey.F11,
            _ => default,
        };
        return key is Key.A or Key.C or Key.F or Key.H or Key.N or Key.O or Key.P or Key.S
            or Key.V or Key.X or Key.Y or Key.Z or Key.F1 or Key.F7 or Key.F9 or Key.F11;
    }

    // ── Closing gate ─────────────────────────────────────────────────────────

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        _printCancellation?.Cancel();
        StopReadAloud();

        e.Cancel = _closeCoordinator.ShouldCancelClosing();
    }

    private async Task<bool> ConfirmCloseAllowedAndStopAutosaveCoreAsync()
    {
        if (!await _fileWorkflow.ConfirmCloseAllowedAsync("closing"))
            return false;

        await _autosave.StopAsync();
        return true;
    }

    private Task<bool> ConfirmCloseAllowedAndStopAutosaveAsync() =>
        ConfirmCloseAllowedAndStopAutosaveCoreAsync();

    private void RestoreOwnerFocus()
    {
        Activate();
        Focus();
    }

    private void ApplyZoom(double scale)
    {
        _zoomScale = ZoomLevels.Clamp(Math.Round(scale, 2));
        _zoom.ScaleX = _zoomScale;
        _zoom.ScaleY = _zoomScale;
        _zoomLabel.Text = ZoomLevels.FormatPercent(_zoomScale);

        if (Math.Abs(_zoomSlider.Value - _zoomScale) > 0.0001)
        {
            _updatingZoomSlider = true;
            try
            {
                _zoomSlider.Value = _zoomScale;
            }
            finally
            {
                _updatingZoomSlider = false;
            }
        }

        var viewDepthPlan = _viewSession.CurrentDepth;
        if (viewDepthPlan.IsSideToSideActive && _sideToSidePreviewScrollViewer is not null)
        {
            UpdateSideToSidePairScrollStride(viewDepthPlan);
            ApplySideToSideNavigationToScrollViewer(viewDepthPlan);
        }
    }

    private void NewDocument() => _ = NewDocumentAsync();

    internal Task<bool> NewDocumentAsyncForTests() => NewDocumentAsync();

    private Task<bool> NewDocumentAsync() =>
        _fileWorkflow.NewAsync(
            FileText.NewAction,
            () =>
            {
                LoadDocumentContent(TextDocument.CreateEmpty());
                return Task.CompletedTask;
            });

    private void ToggleFindBar(bool show)
    {
        if (_findBar is null)
            return;
        _findBar.IsVisible = show;
        if (show)
            _findBox.Focus();
    }

    private void DoFind()
    {
        var query = _findBox.Text;
        if (string.IsNullOrEmpty(query))
            return;
        if (!_editor.FindNext(query))
            _status.Text = $"No match for \"{query}\".";
    }

    private void DoReplace()
    {
        var query = _findBox.Text;
        if (string.IsNullOrEmpty(query))
            return;
        if (!_editor.ReplaceNext(query, _replaceBox.Text ?? string.Empty))
            _status.Text = $"No match for \"{query}\".";
    }

    private void DoReplaceAll()
    {
        var query = _findBox.Text;
        if (string.IsNullOrEmpty(query))
            return;
        var n = _editor.ReplaceAll(query, _replaceBox.Text ?? string.Empty);
        _status.Text = $"Replaced {n} occurrence{(n == 1 ? "" : "s")} of \"{query}\".";
        UpdateStatus();
    }

    private void ScrollCaretIntoView()
    {
        if (_scroller is null)
            return;
        var target = Math.Max(0, _editor.CaretTop - 40);
        var horizontal = _viewSession.CurrentDepth.IsSideToSideActive
            ? Math.Max(0, _editor.CaretLeft - 40)
            : _scroller.Offset.X;
        _scroller.Offset = new Vector(horizontal, target);
    }

    private async Task CopyAsync()
    {
        var text = _editor.SelectedText;
        if (text.Length == 0)
            return;
        var result = await _platformClipboard.WriteAsync(new PlatformClipboardContent(Text: text));
        ThrowClipboardWriteFailure(result);
    }

    // Guarded: this is an `async void` handler wired to the editor's right-click menu, and its
    // Cut/Copy/Paste arms await the platform clipboard directly with no protection of their own.
    // Clipboard access is a shared OS resource that fails routinely (Wayland portal unavailable, an
    // X11 clipboard manager missing, unsupported content), and such a failure escaping here would
    // terminate the process on an everyday right-click.
    private void OnEditorContextMenuCommandRequested(RibbonCommandId commandId) =>
        RunEditorContextMenuCommandGuarded(commandId);

    private async void RunEditorContextMenuCommandGuarded(RibbonCommandId commandId)
    {
        try
        {
            await ApplyEditorContextMenuCommandAsync(commandId);
        }
        catch (Exception ex)
        {
            _status.Text = SisterAppFileTextPlanner.FormatCommandFailed(FileText, "Editor", ex.Message);
        }
    }

    private async Task ApplyEditorContextMenuCommandAsync(RibbonCommandId commandId)
    {
        switch (commandId.Value)
        {
            case FreeWContextMenuPlanner.EditorUndo:
                _editor.Undo();
                break;
            case FreeWContextMenuPlanner.EditorRedo:
                _editor.Redo();
                break;
            case FreeWContextMenuPlanner.EditorCut:
                await CutAsync();
                break;
            case FreeWContextMenuPlanner.EditorCopy:
                await CopyAsync();
                break;
            case FreeWContextMenuPlanner.EditorPaste:
                await PasteAsync();
                break;
            case FreeWContextMenuPlanner.EditorDelete:
                _editor.TryDeleteSelection();
                break;
            case FreeWContextMenuPlanner.EditorSelectAll:
                _editor.SelectAllText();
                break;
        }
    }

    private async Task CutAsync()
    {
        await CopyAsync();
        _editor.TryDeleteSelection();
    }

    private async Task PasteAsync()
    {
        var text = ReadClipboardText(await _platformClipboard.ReadTextAsync());
        if (!_editor.PastePlainText(text))
            _status.Text = "Clipboard does not contain text.";
    }

    private async Task PastePlainTextAsync()
    {
        var text = await TryGetClipboardTextAsync();
        if (!_editor.PastePlainText(text))
            _status.Text = "Clipboard does not contain text.";
    }

    private async Task PasteMergeFormattingAsync()
    {
        var text = await TryGetClipboardTextAsync();
        if (!_editor.PasteMergeFormatting(text))
            _status.Text = "Clipboard does not contain text.";
    }

    private async Task OpenPasteSpecialAsync()
    {
        var text = await TryGetClipboardTextAsync();
        var source = await TryGetClipboardRtfDocumentAsync();
        if (PasteText.Normalize(text).Length == 0 && source is null)
        {
            _status.Text = "Clipboard does not contain text.";
            return;
        }

        var option = await PasteSpecialDialog.ShowAsync(this);
        if (option is null)
            return;

        var pasted = option.Value switch
        {
            PasteSpecialOption.KeepTextOnly => _editor.PastePlainText(text),
            PasteSpecialOption.KeepSourceFormatting when source is not null =>
                _editor.PasteKeepSourceFormatting(source) || _editor.PasteMergeFormatting(text),
            _ => _editor.PasteMergeFormatting(text),
        };
        if (!pasted)
            _status.Text = "Clipboard does not contain text.";
    }

    private async Task<string?> TryGetClipboardTextAsync()
    {
        return ReadClipboardText(await _platformClipboard.ReadTextAsync());
    }

    private async Task<TextDocument?> TryGetClipboardRtfDocumentAsync()
    {
        var format = new PlatformClipboardFormat(
            "Rich Text Format",
            PlatformClipboardDataKind.Text);
        var result = await _platformClipboard.ReadCustomAsync(format);
        var rtf = result.Status == PlatformClipboardReadStatus.Success
            ? result.Value?.Text
            : null;
        return RtfClipboardDocumentParser.TryParse(rtf, out var document) ? document : null;
    }

    private static string? ReadClipboardText(PlatformClipboardReadResult<string> result) =>
        result.Status switch
        {
            PlatformClipboardReadStatus.Success => result.Value,
            PlatformClipboardReadStatus.Unavailable or PlatformClipboardReadStatus.Empty => null,
            PlatformClipboardReadStatus.Unsupported => throw new NotSupportedException(result.ErrorMessage),
            PlatformClipboardReadStatus.Failed => throw new InvalidOperationException(result.ErrorMessage),
            _ => null,
        };

    private static void ThrowClipboardWriteFailure(PlatformClipboardWriteResult result)
    {
        if (result.Status is PlatformClipboardWriteStatus.Success or PlatformClipboardWriteStatus.Unavailable)
            return;
        if (result.Status == PlatformClipboardWriteStatus.Unsupported)
            throw new NotSupportedException(result.ErrorMessage);
        throw new InvalidOperationException(result.ErrorMessage);
    }

    private async Task OpenAsync()
    {
        await _fileWorkflow.OpenAsync(
            FileText.OpenAction,
            PromptOpenPathAsync,
            OpenPathAsync);
    }

    private Task<bool> OpenRecentPathAsync(string path) =>
/*
    /// <summary>
    /// Backstage "Open Recent". Must run the dirty-gate through the async workflow: the shared
    /// workflow's synchronous Open overload prompts for unsaved changes via a helper that blocks
    /// the UI thread waiting on the async save-changes dialog's result, which can never be pumped
    /// while that same UI thread is blocked — a guaranteed deadlock when the current document is
    /// dirty.
    /// </summary>
    private Task<bool> OpenRecentAsync(string path) =>
*/
        _fileWorkflow.OpenAsync(
            FileText.OpenAction,
            () => Task.FromResult<string?>(path),
            OpenPathAsync);

    private async Task<string?> PromptOpenPathAsync()
    {
        using var file = await AvaloniaFilePickerService.PickSingleOpenFileWithLocalPathAsync(
            StorageProvider,
            AvaloniaFilePickerOpenRequest.FromFileTypes(
                FileText.OpenPickerTitle,
                DocumentFilePickerTypes.BuildOpenTypes(_documentPersistence.Adapters)));
        return file?.LocalPath;
    }

    private async Task<bool> OpenPathAsync(string path)
    {
        var execution = await _documentFileWorkflow.OpenPathAsync(path);
        return ApplyFileFeedback(FreeWDocumentFileFeedbackPlanner.PlanOpen(execution, path));
    }

    internal Task<bool> ImportPdfTextAsyncForTests() => ImportPdfTextAsync();

    private Task<bool> ImportPdfTextAsync() =>
        _fileWorkflow.OpenAsync(
            FreeWDocumentFileFeedbackPlanner.ImportPdfAction,
            _pickPdfImportPathAsync,
            ImportPdfTextPathAsync);

    private async Task<string?> PromptPdfImportPathAsync()
    {
        using var file = await AvaloniaFilePickerService.PickSingleOpenFileWithLocalPathAsync(
            StorageProvider,
            AvaloniaFilePickerOpenRequest.FromFileTypes(
                FreeWDocumentFileFeedbackPlanner.ImportPdfPickerTitle,
                AvaloniaFilePickerTypeAdapter.ToFileTypes(
                    _documentPersistence.BuildPdfImportPickerPlan().FileTypes)));
        return file?.LocalPath;
    }

    private async Task<bool> ImportPdfTextPathAsync(string path)
    {
        var result = await _documentFileWorkflow.ImportPdfTextPathAsync(path);
        return ApplyFileFeedback(FreeWDocumentFileFeedbackPlanner.PlanImport(result, path));
    }

    private Task<bool> SaveAsync() =>
        _fileWorkflow.SaveAsync(SaveToCurrentPathAsync, SaveAsAsync);

    internal Task<bool> SaveForTests() => SaveAsync();

    private Task<bool> SaveToCurrentPathAsync(string path) =>
        SaveToCurrentPathCoreAsync(path);

    private async Task<bool> SaveToCurrentPathCoreAsync(string path)
    {
        var result = await _documentFileWorkflow.SaveCurrentPathAsync(path);
        var feedback = FreeWDocumentFileFeedbackPlanner.PlanSave(
            result,
            DocumentSaveExecutionKind.Save,
            path);
        return feedback.RequiresSaveAs ? await SaveAsAsync() : ApplyFileFeedback(feedback);
    }

    private async Task<bool> SaveAsAsync()
    {
        var savePlan = _documentPersistence.BuildSavePickerPlan(
            _fileWorkflow.CurrentPath,
            _fileWorkflow.CurrentFileName,
            FileText.FallbackDisplayName);
        using var file = await AvaloniaFilePickerService.PickSaveFileWithLocalPathAsync(
            StorageProvider,
            AvaloniaFilePickerSaveRequest.FromSavePlan(FileText.SavePickerTitle, savePlan));
        var path = file?.LocalPath;
        return path is not null && await SaveToPathAsync(path);
    }

    private Task<bool> SaveToPathAsync(string path) =>
        SaveToPathAsync(path, filterIndex: 0);

    private Task<bool> SaveToPathAsync(string path, int filterIndex)
        => SavePathCoreAsync(path, filterIndex, DocumentSaveExecutionKind.Save);

    private async Task<bool> SavePathCoreAsync(
        string path,
        int filterIndex,
        DocumentSaveExecutionKind kind)
    {
        var execution = await _documentFileWorkflow.SavePathAsync(path, filterIndex, kind);
        return ApplyFileFeedback(FreeWDocumentFileFeedbackPlanner.PlanSave(execution, kind, path));
    }

    private bool ApplyFileFeedback(FreeWDocumentFileFeedback feedback)
    {
        _status.Text = feedback.Message;
        return feedback.Succeeded;
    }

    /// <summary>
    /// File → Export to PDF (Ctrl+Shift+P). Builds the shared app-agnostic PDF model from the editor
    /// layout and writes a real PDF via <see cref="FreeWAvaloniaPdfExport"/> (Skia when available,
    /// dependency-free WinAnsi fallback otherwise). Mirrors the FreeX Avalonia shell's File → Export
    /// to PDF, on the shared PDF tier.
    /// </summary>
    private async Task ExportPdfAsync()
    {
        var plan = FreeWExportWorkflow.CreatePlan(
            FreeWExportFormat.Pdf,
            _fileWorkflow.CurrentFileNameWithoutExtensionOr(FileText.FallbackDisplayName));
        using var file = await AvaloniaFilePickerService.PickSaveFileWithLocalPathAsync(
            StorageProvider,
            AvaloniaFilePickerSaveRequest.FromFileTypes(
                plan.PickerTitle,
                [AvaloniaFilePickerTypeAdapter.ToFileType(plan.FileType)],
                plan.SuggestedFileName,
                plan.DefaultExtensionWithoutDot));
        var path = file?.LocalPath;
        if (path is null)
            return;

        var execution = await FreeWExportWorkflow.ExecuteAsync(
            plan,
            path,
            (stream, _) =>
            {
                var result = FreeWAvaloniaPdfExport.Save(_editor, stream);
                return ValueTask.FromResult(new FreeWExportArtifact(
                    result.PageCount,
                    result.Backend.ToString(),
                    result.ImageDiagnostics.Count));
            });
        _status.Text = execution.Message;
    }

    internal Task PrintAsync() => PrintAsync(document: null);

    internal async Task PrintAsync(TextDocument? document)
    {
        var priorFocus = FocusManager?.GetFocusedElement();
        using var cancellation = new CancellationTokenSource();
        _printCancellation = cancellation;
        try
        {
            FreeWAvaloniaPdfExportResult? printPdfResult = null;
            var execution = await _portablePrintWorkflow.ExecuteAsync(
                (discovery, token) => _showPrintSelectionDialog(this, discovery, token),
                (stream, selection, _) =>
                {
                    var printView = _editor;
                    if (document is not null)
                    {
                        printView = new DocumentView();
                        printView.LoadDocument(document);
                    }

                    printPdfResult = _savePrintPdf(printView, stream, selection);
                    return ValueTask.CompletedTask;
                },
                cancellation.Token);
            _latestPrinterDiscovery = execution.Discovery ?? _latestPrinterDiscovery;
            _status.Text = printPdfResult is { ImageDiagnostics.Count: > 0 }
                ? $"{execution.Message} ({printPdfResult.ImageDiagnostics.Count} image warning(s))"
                : execution.Message;
        }
        finally
        {
            if (ReferenceEquals(_printCancellation, cancellation))
                _printCancellation = null;
            _restorePrintOwnerFocus(priorFocus);
        }
    }

    /// <summary>
    /// File - Export to XPS. Uses the portable Avalonia XPS writer and the same atomic replacement
    /// contract as the WPF export path. The picker result is intentionally reduced to a local path;
    /// virtual/non-local storage cannot be passed to the file-based writer and is reported honestly.
    /// </summary>
    private async Task ExportXpsAsync()
    {
        var plan = FreeWExportWorkflow.CreatePlan(
            FreeWExportFormat.Xps,
            _fileWorkflow.CurrentFileNameWithoutExtensionOr(FileText.FallbackDisplayName));
        var selection = await _pickExportPath(
            StorageProvider,
            AvaloniaFilePickerSaveRequest.FromFileTypes(
                plan.PickerTitle,
                [AvaloniaFilePickerTypeAdapter.ToFileType(plan.FileType)],
                plan.SuggestedFileName,
                plan.DefaultExtensionWithoutDot,
                showOverwritePrompt: true,
                suggestFirstFileType: true));
        if (selection.Canceled)
            return;

        var path = selection.LocalPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            _status.Text = SisterAppFileTextPlanner.FormatSelectedFileNotLocalPath(
                FileText,
                FreeWFileTextResources.XpsExportCommand);
            return;
        }

        var execution = await FreeWExportWorkflow.ExecuteAsync(
            plan,
            path,
            (stream, _) =>
            {
                FreeWAvaloniaXpsExport.Save(_editor, stream);
                return ValueTask.FromResult(new FreeWExportArtifact());
            });
        _status.Text = execution.Message;
    }

    private static async Task<(bool Canceled, string? LocalPath)> PickExportPathAsync(
        IStorageProvider storageProvider,
        AvaloniaFilePickerSaveRequest request)
    {
        using var file = await AvaloniaFilePickerService.PickSaveFileWithLocalPathAsync(storageProvider, request);
        return file is null ? (true, null) : (false, file.LocalPath);
    }

    internal Task ExportXpsForTests() => ExportXpsAsync();

    private async Task RefreshPrinterDiscoveryAsync()
    {
        _latestPrinterDiscovery = await _portablePrintWorkflow.DiscoverAsync();
    }

    private BackstageDirectPrintCapability DirectPrintCapability =>
        FreeWPrintMessagePlanner.PlanCapability(
            _printService.IsSupported,
            _latestPrinterDiscovery);

    private void RestorePrintOwnerFocus(IInputElement? priorFocus)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Activate();
            if (priorFocus is InputElement input && input.Focusable && input.IsEffectivelyEnabled)
                input.Focus();
            else
                _editor.Focus();
        }, DispatcherPriority.Input);
    }

    /// <summary>
    /// Insert &gt; Picture (AV-INSERT): realize the portable import workflow through Avalonia-native ports.
    /// </summary>
    private async Task InsertPictureAsync()
    {
        var workflow = new FreeWPictureImportWorkflow(
            new AvaloniaPictureImportPickerPort(StorageProvider),
            new AvaloniaPictureImportSourceReaderPort(),
            new AvaloniaPictureDecoderPort(),
            new AvaloniaPictureRasterizerPort(),
            new AvaloniaPictureInsertionPort(_editor));
        var result = await workflow.ImportAsync();
        var presentation = FreeWPictureImportOutcomePlanner.Plan(
            result,
            FileText,
            FreeWPictureImportFailureSurface.Status);
        if (presentation.StatusText is { } statusText)
            _status.Text = statusText;
    }

    /// <summary>Pick a file and insert it as a Word-compatible generic OLE Package.</summary>
    private Task InsertEmbeddedObjectAsync() => ExecuteDocumentFragmentImportAsync(
        FreeWDocumentFragmentImportPlanner.CreateEmbeddedObjectRequest(
            FreeWDocumentFragmentHostProfile.Avalonia));

    private async Task OpenSymbolPickerAsync()
    {
        var dialog = new SymbolPickerDialog();
        await dialog.ShowDialog(this);
        ApplySymbolPickerResult(_editor, dialog.Result);
        _editor.Focus();
    }

    internal static void ApplySymbolPickerResult(DocumentView editor, string? symbol)
    {
        ArgumentNullException.ThrowIfNull(editor);
        if (!string.IsNullOrEmpty(symbol))
            editor.InsertSymbol(symbol);
    }

    private async Task OpenLegalNoticesAsync()
    {
        var dialog = new LegalNoticesDialog();
        await dialog.ShowDialog(this);
        _editor.Focus();
    }

    private async Task OpenAboutAsync()
    {
        var dialog = new AboutDialog();
        await dialog.ShowDialog(this);
        _editor.Focus();
    }

    private async Task OpenTableFormulaDialogAsync(TableFormulaDialogInitialState initialState)
    {
        var dialog = new TableFormulaDialog(initialState);
        await dialog.ShowDialog(this);
        ApplyTableFormulaResult(_editor, dialog.Result);
        _editor.Focus();
    }

    internal static void ApplyTableFormulaResult(DocumentView editor, TableFormulaField? formula)
    {
        ArgumentNullException.ThrowIfNull(editor);
        if (formula is not null)
            editor.InsertTableFormula(formula);
    }

    private async Task OpenTablePropertiesDialogAsync(ModelTableContext context)
    {
        var dialog = new TablePropertiesDialog(context);
        await dialog.ShowDialog(this);
        ApplyTablePropertiesResult(_editor, dialog.Result);
        WriteTablePropertiesX11ValidationResult(context, dialog);
        _editor.Focus();
    }

    private async Task RunTablePropertiesX11ValidationSeedAsync()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("FREEW_TABLE_PROPERTIES_X11_SEED"), "1", StringComparison.Ordinal))
            return;

        _editor.InsertTable(2, 2);
        var tableBlock = -1;
        for (var index = 0; index < _editor.Document.Blocks.Count; index++)
        {
            if (_editor.Document.Blocks[index] is Table table
                && table.Rows.Count == 2
                && table.Rows.All(row => row.Cells.Count == 2))
                tableBlock = index;
        }

        if (tableBlock < 0)
            throw new InvalidOperationException("Table Properties X11 validation seed did not create a table.");

        _editor.PlaceCaretInCell(tableBlock, 0, 0, 0, 0);
        var context = _editor.CaretTableContext()
            ?? throw new InvalidOperationException("Table Properties X11 validation seed did not select cell A1.");
        await OpenTablePropertiesDialogAsync(context);
    }

    private static void WriteTablePropertiesX11ValidationResult(
        ModelTableContext context,
        TablePropertiesDialog dialog)
    {
        var path = Environment.GetEnvironmentVariable("FREEW_TABLE_PROPERTIES_X11_RESULT");
        if (string.IsNullOrWhiteSpace(path))
            return;

        var result = new
        {
            schema = "freew.table-properties.x11-result.v1",
            status = dialog.Result is null ? "cancelled" : "applied",
            tableRows = context.Table.Rows.Count,
            tableColumns = context.Table.Rows.Count == 0 ? 0 : context.Table.Rows[0].Cells.Count,
            values = dialog.Result,
            focusTrace = dialog.FocusTraceForValidation,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }

    internal static void ApplyTablePropertiesResult(DocumentView editor, TablePropertiesValues? values)
    {
        ArgumentNullException.ThrowIfNull(editor);
        if (values is not null)
            editor.ApplyTableProperties(values);
    }

    private async Task InsertScreenClipAsync()
    {
        try
        {
            var capture = await _screenClipService.CaptureAsync(this);
            if (capture is null)
                return;

            ApplyScreenClipCapture(_editor, capture);
            _status.Text = $"Inserted screen clipping ({capture.PixelWidth} x {capture.PixelHeight}).";
        }
        catch (Exception ex)
        {
            _status.Text = $"Could not capture the screen clip: {ex.Message}";
        }
        finally
        {
            _editor.Focus();
        }
    }

    internal Task InsertScreenClipForTestAsync() => InsertScreenClipAsync();

    internal static void ApplyScreenClipCapture(DocumentView editor, ScreenClipCapture capture)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(capture);
        if (capture.PngBytes.Length == 0)
            throw new ArgumentException("Screenshot bytes are empty.", nameof(capture));

        var display = ScreenClipPlanner.BuildImageInsertionPlan(
            capture.PixelWidth,
            capture.PixelHeight);
        editor.InsertInlineImage(
            capture.PngBytes,
            display.WidthPt,
            display.HeightPt,
            display.Format,
            display.OriginalPixelWidth,
            display.OriginalPixelHeight);
    }

    // ── AV-INSERT2: Insert depth 2 dialog launchers ─────────────────────────────

    /// <summary>
    /// AV-INSERT2: Opens the Insert Hyperlink dialog. Pre-fills the display field with the current selection
    /// text (Word's behaviour), and on OK inserts/converts the hyperlink via
    /// <see cref="DocumentView.InsertHyperlink"/>. Wired to <c>freew.insert-hyperlink</c> (Insert → Links).
    /// </summary>
    private async Task OpenHyperlinkDialogAsync()
    {
        var dialog = new HyperlinkDialog(initialDisplay: _editor.SelectedText);
        await dialog.ShowDialog(this);
        if (dialog.Address is { } address)
        {
            _editor.InsertHyperlink(dialog.DisplayText ?? string.Empty, address);
            _editor.Focus();
        }
    }

    /// <summary>
    /// AV-LINKS: Opens Edit Hyperlink for the link under the caret and retargets it on OK.
    /// </summary>
    private async Task OpenEditHyperlinkDialogAsync()
    {
        if (!_editor.IsCaretOnHyperlink())
            return;

        var links = _editor.HyperlinksAtCaret();
        var target = links.Count > 0
            ? links[0].Url ?? (links[0].Anchor is { Length: > 0 } anchor ? "#" + anchor : string.Empty)
            : string.Empty;
        var dialog = new HyperlinkDialog(
            initialDisplay: _editor.SelectedText,
            initialAddress: target,
            title: InsertDialogTextResources.Hyperlink.EditTitle);
        await dialog.ShowDialog(this);
        if (dialog.Address is { } address)
            _editor.EditHyperlink(address, dialog.DisplayText);
        _editor.Focus();
    }

    /// <summary>
    /// AV-LINKS: Opens ScreenTip for the link under the caret and sets or clears it on OK.
    /// </summary>
    private async Task OpenHyperlinkTooltipDialogAsync()
    {
        if (!_editor.IsCaretOnHyperlink())
            return;

        var dialog = new ScreenTipDialog(_editor.HyperlinkTooltipAtCaret());
        await dialog.ShowDialog(this);
        if (dialog.ScreenTip is { } tip)
            _editor.SetHyperlinkTooltip(tip);
        _editor.Focus();
    }

    /// <summary>
    /// AV-INSERT2: Opens the Bookmark dialog (add at caret / Go To existing). Lists the document's current
    /// bookmark names. Wired to <c>freew.insert-bookmark</c> (Insert → Links).
    /// </summary>
    private async Task OpenBookmarkDialogAsync()
    {
        var names = Bookmarks.List(_editor.Document)
            .Select(b => b.Name)
            .Distinct()
            .ToList();
        var dialog = new BookmarkDialog(names);
        await dialog.ShowDialog(this);
        if (dialog.BookmarkName is { } add)
            _editor.InsertBookmark(add);
        else if (dialog.GoToName is { } go)
            _editor.GoToBookmark(go);
        _editor.Focus();
    }

    private async Task OpenBookmarkManagerDialogAsync()
    {
        await BookmarkManagerDialog.ShowAsync(this, _editor);
        _editor.Focus();
    }

    /// <summary>
    /// AV-LINKS: Opens a bookmark picker and links the current selection to the chosen internal target.
    /// </summary>
    private async Task OpenLinkBookmarkDialogAsync()
    {
        var names = _editor.BookmarkNames();
        if (names.Count == 0)
            return;

        var dialog = new LinkBookmarkDialog(names);
        await dialog.ShowDialog(this);
        if (dialog.BookmarkName is { } bookmark)
            _editor.ApplyInternalLink(bookmark);
        _editor.Focus();
    }

    /// <summary>
    /// AV-INSERT2: Opens the Insert Quick Part dialog (a free-text snippet) and inserts the entered text at
    /// the caret. Wired to <c>freew.quick-parts.snippet</c> (Insert → Text → Quick Parts).
    /// </summary>
    private async Task OpenQuickPartDialogAsync()
    {
        var dialog = new QuickPartDialog();
        await dialog.ShowDialog(this);
        if (dialog.SnippetText is { } text)
        {
            _editor.InsertQuickPartText(text);
            _editor.Focus();
        }
    }

    private async Task SaveQuickPartSelectionAsync()
    {
        var selectedText = _editor.SelectedText;
        if (string.IsNullOrEmpty(selectedText))
        {
            await FreeWInfoDialog.ShowAsync(this, QuickPartCommandPlanner.EmptySelectionMessage);
            _editor.Focus();
            return;
        }

        var name = await QuickPartNameDialog.AskAsync(this);
        var part = QuickPartCommandPlanner.CreateSelection(selectedText, name);
        if (part is not null)
            _quickParts.Save(part);
        _editor.Focus();
    }

    private async Task OpenBuildingBlocksOrganizerAsync()
    {
        var action = await BuildingBlocksOrganizerDialog.ShowAsync(this, _quickParts);
        if (action is { Kind: BuildingBlocksOrganizerActionKind.Insert })
            _editor.InsertQuickPartText(action.Text);
        _editor.Focus();
    }

    private async Task OpenFieldDialogAsync()
    {
        var instruction = await FieldPickerDialog.AskAsync(this);
        if (instruction is not null)
            _editor.InsertComplexField(instruction);
        _editor.Focus();
    }

    private async Task OpenDrawTableDialogAsync()
    {
        var dimensions = await DrawTableDimensionDialog.AskAsync(this);
        if (dimensions is { } value)
            _editor.InsertTable(value.Rows, value.Columns);
        _editor.Focus();
    }

    private async Task OpenSplitCellDialogAsync()
    {
        var dimensions = await DrawTableDimensionDialog.AskSplitCellAsync(this);
        if (dimensions is { } value)
            _editor.SplitCurrentCell(value.Rows, value.Columns);
        _editor.Focus();
    }

    /// <summary>
    /// AV-INSERT2: Insert Text from File realizes the portable DOCX/TXT policy through Avalonia-native ports.
    /// Wired to <c>freew.text-from-file</c> (Insert -> Text).
    /// </summary>
    private Task InsertTextFromFileAsync() => ExecuteDocumentFragmentImportAsync(
        FreeWDocumentFragmentImportPlanner.CreateTextFromFileRequest(
            FreeWDocumentFragmentHostProfile.Avalonia));

    private async Task ExecuteDocumentFragmentImportAsync(FreeWDocumentFragmentImportRequest request)
    {
        var workflow = new FreeWDocumentFragmentImportWorkflow(
            _documentPersistence.Adapters,
            new AvaloniaDocumentFragmentPickerPort(StorageProvider),
            new AvaloniaDocumentFragmentSourceReaderPort(),
            new AvaloniaDocumentFragmentInsertionPort(_editor));
        var result = await workflow.ImportAsync(request);
        var presentation = FreeWDocumentFragmentImportOutcomePlanner.Plan(
            result,
            FileText,
            FreeWDocumentFragmentImportFailureSurface.AvaloniaStatus);
        if (presentation.StatusText is { } statusText)
            _status.Text = statusText;
    }

    private void ApplyOpenResult(DocumentOpenResult result)
    {
        var execution = _documentFileWorkflow.ApplyOpenResultAsync(result).GetAwaiter().GetResult();
        if (!execution.Succeeded)
            throw execution.Exception ?? new InvalidOperationException("The startup document could not be opened.");
    }

    private void LoadDocumentAsSaved(TextDocument document, string? path)
    {
        LoadDocumentContent(document);

        if (path is null)
            _fileWorkflow.MarkSavedWithoutPath();
        else
            _fileWorkflow.MarkSavedWithPath(path, suppressRecentFiles: false);

        if (document.UpdateFieldsOnOpen)
        {
            _suppressEditorDirty = true;
            try
            {
                _editor.UpdateFields();
            }
            finally
            {
                _suppressEditorDirty = false;
            }
        }
    }

    private void LoadDocumentContent(TextDocument document)
    {
        StopReadAloud();
        ApplyViewDepthTransition(_viewSession.RestoreLiveEditor(), updateStatus: false);
        _suppressEditorDirty = true;
        try
        {
            _editor.LoadDocument(document);
        }
        finally
        {
            _suppressEditorDirty = false;
        }
    }

    private void ToggleReadAloud()
    {
        EnsureReadAloudSession().ToggleStartStop();
        RefreshRibbonCommandStates();
    }

    private bool IsReadAloudActive() => _readAloudSession?.IsActive == true;

    private ReadAloudSession EnsureReadAloudSession()
    {
        if (_readAloudSession is not null)
            return _readAloudSession;

        _readAloudSession = new ReadAloudSession(new ReadAloudSessionPorts(
            GetDocument: () => _editor.Document,
            GetStartSegmentIndex: _editor.ReadAloudStartSegmentIndex,
            CreateEngine: _ => new AvaloniaSpeechEngine()));
        _readAloudSession.StateChanged += OnReadAloudStateChanged;
        return _readAloudSession;
    }

    private void OnReadAloudStateChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RefreshRibbonCommandStates();
            return;
        }

        try
        {
            Dispatcher.UIThread.Post(RefreshRibbonCommandStates);
        }
        catch (Exception)
        {
            // The window may be closing; no UI state update is needed once the dispatcher is gone.
        }
    }

    private void RefreshRibbonCommandStates()
    {
        if (_ribbonControl is null || _ribbonRegistry is null)
            return;

        _ribbonStateRefreshCount++;
        AvaloniaRibbonRenderer.SyncToggleStates(
            _ribbonControl,
            _ribbonRegistry,
            RibbonVisualPalette.FromTheme(App.ActiveTheme),
            _ribbonStateStore);
    }

    private void StopReadAloudAfterDocumentChange()
    {
        if (_readAloudSession?.HandleDocumentChanged() == true)
            RefreshRibbonCommandStates();
    }

    private void StopReadAloud()
    {
        _readAloudSession?.Stop();
        RefreshRibbonCommandStates();
    }

    private void DisposeReadAloud()
    {
        var session = _readAloudSession;
        _readAloudSession = null;
        if (session is null)
            return;

        session.StateChanged -= OnReadAloudStateChanged;
        session.Dispose();
    }

    private void OnEditorDocumentChanged()
    {
        if (!_suppressEditorDirty)
            _fileWorkflow.MarkDirty();

        RefreshSplitPreviewSnapshot();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var (currentSection, totalSections) = _editor.SectionInfo();
        var plan = _editorInteraction.BuildStatus(new FreeWEditorStatusContext(
            _editor.Document,
            CurrentPage: _editor.CaretPageIndex + 1,
            TotalPages: _editor.PageCount,
            CurrentSection: currentSection,
            TotalSections: totalSections,
            SelectionText: _editor.SelectedText,
            IncludePageStatus: _editor.ViewMode == DocumentViewMode.PrintLayout,
            IncludeSectionStatus: true,
            IsEdited: _editor.CanUndo));
        _pageStatus.Text = plan.PageStatus;
        _sectionStatus.Text = plan.SectionStatus;
        _status.Text = plan.CountsStatus;
    }

    // ── Backstage (File screen) ───────────────────────────────────────────────

    /// <summary>
    /// Opens the FreeW backstage (File screen) as a modal full-window overlay.
    /// The backstage renders its panes from the portable Presentation-tier planners and
    /// dispatches user actions back through this shell's file workflow and open/save paths.
    /// </summary>
    private Task ShowBackstageAsync()
    {
        var callbacks = BuildBackstageCallbacks();
        return BackstageView.ShowAsync(this, callbacks);
    }

    internal BackstageCallbacks BuildBackstageCallbacks() =>
        new BackstageCallbacks(
            DisplayName: _fileWorkflow.DisplayName,
            CurrentPath: _fileWorkflow.CurrentPath,
            GetRecentEntries: () => _fileWorkflow.RecentEntries,
            GetFileFormats: () => _documentPersistence.Adapters.SelectMany(a => a.Formats),
            GetPageSettings: () => _editor.Document.Page,
            GetCurrentOptions: () => _options,
            GetDataFolder: () => FreeWApplicationFrameDescriptor.ResolveDataFolderLabel(_optionsStore.StorePath),
            GetDocument: () => _editor.Document,
            GetIsDirty: () => _fileWorkflow.IsDirty,

            NewDocument: () => _applicationCommands.Execute(FreeWKeyboardCommand.NewDocument),
            OpenRecent: path => _ = OpenRecentPathAsync(path),
            OpenFolder: OpenFolderInShell,
            Browse: () => _applicationCommands.Execute(FreeWKeyboardCommand.OpenDocument),
            RecoverUnsaved: () => _ = _autosave.OfferRecoveryAsync(this),
            ImportPdfText: () => _ = ImportPdfTextAsync(),
            Save: () => _applicationCommands.Execute(FreeWKeyboardCommand.SaveDocument),
            SaveAs: () => _applicationCommands.Execute(FreeWKeyboardCommand.SaveDocumentAs),
            SaveAsFormat: (ext, filterIndex) => _ = SaveAsWithFormatAsync(ext, filterIndex),
            SaveCopy: () => _ = SaveCopyAsync(),
            OpenContainingFolder: path =>
            {
                var folder = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(folder))
                    OpenFolderInShell(folder);
            },
            ExportPdf: () => _ = ExportPdfAsync(),
            ExportXps: () => _ = ExportXpsAsync(),
            EditProperties: () => _ = OpenPropertiesAsync(),
            MarkAsFinal: ToggleMarkAsFinal,
            RestrictEditing: () => _ = OpenRestrictEditingAsync(),
            InspectDocument: () => _ = InspectDocumentAsync(),
            CheckAccessibility: () => _ = CheckAccessibilityAsync(),
            OpenOptions: () => _ = OpenOptionsAsync(),
            CloseDocument: Close,
            DirectPrintCapability: DirectPrintCapability,
            Print: DirectPrintCapability.IsAvailable
                ? () => _applicationCommands.Execute(FreeWKeyboardCommand.PrintDocument)
                : null,
            PrintPreview: () => _ = OpenPrintPreviewAsync());

    private async Task SaveCopyAsync()
    {
        var savePlan = _documentPersistence.BuildSavePickerPlan(
            _fileWorkflow.CurrentPath,
            _fileWorkflow.CurrentFileName,
            FileText.FallbackDisplayName);
        using var file = await AvaloniaFilePickerService.PickSaveFileWithLocalPathAsync(
            StorageProvider,
            AvaloniaFilePickerSaveRequest.FromSavePlan(
                FreeWDocumentFileFeedbackPlanner.SaveCopyCommand,
                savePlan));
        var path = file?.LocalPath;
        if (path is null)
            return;

        await SaveCopyToPathAsync(path);
    }

    internal Task<bool> SaveCopyToPathAsync(string path, int filterIndex = 0)
        => SavePathCoreAsync(path, filterIndex, DocumentSaveExecutionKind.SaveCopy);

    private async Task OpenPropertiesAsync()
    {
        var dialog = new PropertiesDialog(_editor.Document.Properties);
        await dialog.ShowDialog(this);
        if (!dialog.Accepted || dialog.Result is not { } result)
            return;

        _editor.ApplyDocumentProperties(result);
        _status.Text = "Document properties updated.";
        _editor.Focus();
    }

    private void ToggleMarkAsFinal()
    {
        _editor.SetMarkedAsFinal(!_editor.IsMarkedAsFinal);
        _status.Text = _editor.IsMarkedAsFinal
            ? "Document marked as final."
            : "Document is no longer marked as final.";
        _editor.Focus();
    }

    private async Task OpenRestrictEditingAsync()
    {
        var dialog = new RestrictEditingDialog(_editor.Document.Protection);
        await dialog.ShowDialog(this);
        if (dialog.Result is not { } settings)
            return;

        _editor.SetProtection(settings);
        _status.Text = settings.Mode == ProtectionMode.None
            ? "Editing restrictions removed."
            : $"Editing restricted: {settings.Mode}.";
        _editor.Focus();
    }

    private async Task InspectDocumentAsync()
    {
        var result = DocumentInspector.Inspect(_editor.Document);
        var dialog = new DocumentInspectorDialog(result);
        await dialog.ShowDialog(this);
        if (dialog.Choice is not { } choice)
            return;

        if (choice.Any)
            _editor.ApplyInspectorRemovals(choice);

        _status.Text = choice.Any
            ? "Selected document data removed."
            : "Document Inspector completed.";
        _editor.Focus();
    }

    private async Task CheckAccessibilityAsync()
    {
        var report = AccessibilityChecker.Check(_editor.Document);
        var dialog = new AccessibilityReportDialog(report);
        await dialog.ShowDialog(this);
        _status.Text = report.IsClean
            ? "No accessibility issues found."
            : $"{report.Issues.Count} accessibility issue(s) found.";
        _editor.Focus();
    }

    private async Task OpenOptionsAsync()
    {
        var dialog = new OptionsDialog(_options);
        await dialog.ShowDialog(this);
        if (dialog.Result is not { } edited)
            return;

        ApplyEditorTypingOptions(_optionsRuntime.Apply(edited));
        if (!_optionsStore.Save(_options))
            _status.Text = _optionsStore.LastError ?? "FreeW Options could not be saved.";
        else
            _status.Text = "FreeW Options saved.";
    }

    private void ApplyEditorTypingOptions(FreeWEditorTypingOptionsPlan plan)
    {
        _editor.AutoCorrectEnabled = plan.AutoCorrectEnabled;
        _editor.AutoFormatOptions = plan.AutoFormat;
        _editor.AutoCorrectOptions = plan.AutoCorrect;
    }

    private void OpenFolderInShell(string folder)
    {
        var result = DesktopPathLauncher.OpenDirectory(folder);
        if (result.Error is not null)
            _status.Text = $"Could not open folder: {result.Error.Message}";
    }

    /// <summary>
    /// Save As targeting a specific file format chosen from the backstage planner.
    /// Builds a save-picker pre-filtered to the requested format and lets the user
    /// confirm the filename before saving.
    /// </summary>
    private async Task SaveAsWithFormatAsync(string extension, int filterIndex)
    {
        var normalizedExt = DocumentFileFormatResolver.NormalizeExtension(extension);
        if (!_documentPersistence.TryGetSaveFormat(filterIndex, out var format) &&
            !_documentPersistence.TryGetSaveFormat(normalizedExt, out format))
        {
            _status.Text = SisterAppFileTextPlanner.FormatUnsupportedExtension(FileText, extension);
            return;
        }

        var savePlan = _documentPersistence.BuildSavePickerPlan(
            _fileWorkflow.CurrentPath,
            _fileWorkflow.CurrentFileName,
            FileText.FallbackDisplayName,
            normalizedExt);

        using var file = await AvaloniaFilePickerService.PickSaveFileWithLocalPathAsync(
            StorageProvider,
            AvaloniaFilePickerSaveRequest.FromFileTypes(
                SisterAppFileTextPlanner.FormatSaveAsTitle(FileText, format?.FormatName ?? extension),
                [
                    AvaloniaFilePickerTypeAdapter.CreateFileType(
                        format?.FormatName ?? extension,
                        [$"*{normalizedExt}"])
                ],
                savePlan.SuggestedFileName,
                savePlan.DefaultExtensionWithoutDot));
        var path = file?.LocalPath;
        if (path is not null)
            await SaveToPathAsync(path, filterIndex);
    }

    // Opens an external URL raised by DocumentView.HyperlinkActivated through the shared scheme allowlist.
    // Mirrors the WPF host's OnHyperlinkRequestNavigate: blocked schemes and launch failures are silently
    // dropped so a bad URL never crashes the editor.
    private static void OpenExternalUri(string url) => _ = TryOpenExternalUri(url);

    private static ExternalUriLaunchResult TryOpenExternalUri(string url) =>
        DesktopExternalUriLauncher.Open(url);
}
