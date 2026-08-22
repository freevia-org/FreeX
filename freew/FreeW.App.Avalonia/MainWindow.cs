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
using Free.Shared.AppServices;
using Free.Shared.AppServices.Printing;
using Free.Shared.AppServices.Windows;
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
using FreeW.App.Presentation.Editing;
using FreeW.App.Presentation.Options;
using FreeW.App.Presentation.QuickParts;
using FreeW.App.Presentation.Ribbon;
using FreeW.Ribbon.Definitions;
using FreeW.App.Presentation.Shell;
using FreeW.App.Presentation.Speech;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

public sealed partial class MainWindow : Window
{
    private static readonly ProductThemeResourceProfile ThemeResources = ProductThemeResourceProfiles.FreeW;

    private static readonly SisterAppFileTextSpec FileText = FreeWFileTextResources.Document;

    private readonly DocumentPersistenceWorkflow _documentPersistence;
    private readonly FreeWDocumentFileWorkflow _documentFileWorkflow;
    private readonly FreeWDocumentFileCommandSession _fileCommands;
    private readonly IPlatformPrintService _printService;
    private readonly PortablePrintSubmissionWorkflow _portablePrintWorkflow;
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
    private readonly ScreenClipWorkflowCoordinator _screenClipWorkflow = new();
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
    private readonly FindReplaceDialogSession _inlineFindReplaceSession;
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
    // r148-startup-fileopen: set once in the constructor when a startup argument was supplied but
    // could not be opened (missing, locked, corrupt, or unsupported), so Opened can surface it --
    // see ShowStartupOpenFailureIfAnyAsync.
    private readonly bool _startupOpenFailed;
    private readonly string? _startupOpenFailurePath;
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
    private readonly FreeWDocumentWindowPlanner _documentWindowPlanner;
    private readonly int _documentWindowNumber;
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
    private double _zoomScale = 1.0;
    private bool _updatingZoomSlider;
    private readonly FreeWEditorInteractionSession _editorInteraction = new();
    private readonly FreeWApplicationCommandRouter _applicationCommands;
    private bool _pagedEditMode;
    // Avalonia's PrintLayout is already the live, multi-page editing surface used by Page Edit.
    // Keep the prior continuous view so entering the alias does not change the user's view when it
    // is exited again (WPF restores the live editor that was underneath its page panel).
    private DocumentViewMode _viewModeBeforePagedEdit = DocumentViewMode.PrintLayout;
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
        bool suppressStartupRecoveryOffer = false,
        FreeWDocumentWindowPlanner? documentWindowPlanner = null,
        int documentWindowNumber = 1,
        // r137-remediation2: lets headless tests observe the shell's own message boxes (notably the
        // externally-modified-file overwrite prompt) instead of a real Avalonia dialog nothing can
        // answer. Matches the WPF host, which already takes an IUserMessageService. Null keeps the
        // production AvaloniaUserMessageService owned by this window.
        IUserMessageService? messageService = null)
    {
        if (documentWindowNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(documentWindowNumber));

        _optionsStore = optionsStore;
        _documentWindowPlanner = documentWindowPlanner ?? new FreeWDocumentWindowPlanner();
        _documentWindowNumber = documentWindowNumber;
        _documentPersistence = documentPersistence ?? new DocumentPersistenceWorkflow();
        _screenClipService = screenClipService ?? new AvaloniaScreenClipService();
        _platformClipboard = platformClipboard ?? new AvaloniaPlatformClipboard(
            () => TopLevel.GetTopLevel(this)?.Clipboard);
        _editor.CanPasteProvider = () => _platformClipboard.IsAvailable;
        _printService = printService ?? PlatformPrintServiceSelector.Select(
            windowsFactory: static () => new WindowsPrintService(),
            cupsFactory: static () => new CupsPrintService());
        _portablePrintWorkflow = new PortablePrintSubmissionWorkflow(_printService);
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

        Title = FreeWApplicationFrameDescriptor.Title.ApplicationName;
        Width = 1040;
        Height = 720;
        MinWidth = 720;
        MinHeight = 480;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));
        ApplyWindowIcon();
        _fileWorkflow = new SisterAvaloniaFileCommandWorkflow(
            owner: this,
            titleSpec: new SisterAvaloniaFileTitleSpec(
                ApplicationName: FreeWApplicationFrameDescriptor.Title.ApplicationName,
                Separator: FreeWApplicationFrameDescriptor.Title.Separator,
                DirtyMarker: FreeWApplicationFrameDescriptor.Title.DirtyMarker,
                ApplicationPlacement: FreeWApplicationFrameDescriptor.Title.ApplicationPlacement,
                UntitledDisplayName: FreeWApplicationFrameDescriptor.Title.DefaultDocumentDisplayName,
                CollapseCleanUntitledTitle: FreeWApplicationFrameDescriptor.Title.CollapseCleanDefaultDocumentTitle),
            maxRecentEntries: () => _options.RecentFilesCap,
            onChanged: OnFileWorkflowChanged,
            saveAsync: SaveAsync,
            promptSaveChangesAsync: promptSaveChangesAsync,
            showFileCommandErrorAsync: showFileCommandErrorAsync,
            restoreOwnerFocus: RestoreOwnerFocus,
            messageService: messageService);
        RefreshDocumentWindowTitle();
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
                ConfirmExternallyModifiedOverwriteAsync: (path, cancellationToken) =>
                    _fileWorkflow.ConfirmExternallyModifiedOverwriteAsync(path, cancellationToken),
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
        _fileCommands = new FreeWDocumentFileCommandSession(
            _documentFileWorkflow,
            new FreeWFileCommandLifecyclePorts(
                CurrentPath: () => _fileWorkflow.CurrentPath,
                CurrentFileName: () => _fileWorkflow.CurrentFileName,
                NewAsync: (action, loadAsync) => _fileWorkflow.NewAsync(action, loadAsync),
                OpenAsync: _fileWorkflow.OpenAsync,
                SaveAsync: _fileWorkflow.SaveAsync),
            new FreeWDocumentFileCommandPorts(
                LoadNewDocumentAsync: () =>
                {
                    LoadDocumentContent(TextDocument.CreateEmpty());
                    return Task.CompletedTask;
                },
                PickOpenPathAsync: PromptOpenPathAsync,
                PickPdfImportPathAsync: _pickPdfImportPathAsync,
                PickSaveTargetAsync: PromptSavePathAsync,
                PresentFeedback: feedback => ApplyFileFeedback(feedback)),
            FileText);
        _inlineFindReplaceSession = new FindReplaceDialogSession(
            new AvaloniaFindReplaceCommandHost(_editor),
            policyText: FindReplaceDialogPlanner.ResolvePolicyText(UiText.Get));
        _applicationCommands = new FreeWApplicationCommandRouter(new FreeWApplicationCommandActions(
            NewDocument: NewDocument,
            OpenDocument: () => _ = OpenAsync(),
            SaveDocument: () => _ = SaveAsync(),
            SaveDocumentAs: () => _ = SaveAsAsync(),
            PrintDocument: () => _ = PrintAsync(),
            Find: () => OpenFindReplaceDialog(FindReplaceOpenMode.Find),
            Replace: () => OpenFindReplaceDialog(FindReplaceOpenMode.Replace),
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
            recoverInNewWindowAsync: OpenNewWindowWithRecoveredSnapshotAsync,
            confirmDiscardOrSaveAsync: () => _fileWorkflow.ConfirmCloseAllowedAsync("recovering an unsaved document"));
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
        _editor.CaretMoved += RefreshRibbonCommandStates;
        _editor.DocumentChanged += RefreshRibbonCommandStates;
        _editor.CaretMoved += () => { if (_thesaurusPane.IsVisible) _thesaurusPane.Refresh(); };
        _editor.ViewModeChanged += UpdateStatus;
        _editor.ViewModeChanged += UpdateViewModeButtons;
        _editor.HyperlinkActivated += OpenExternalUri;
        _editor.ContextMenuCommandRequested += OnEditorContextMenuCommandRequested;

        UpdateViewModeButtons();
        var startupDocument = FreeWApplicationStartup.TryOpenStartupDocument(
            startupArguments,
            _documentPersistence);
        // r148-startup-fileopen: TryOpenStartupDocument returns null both when nothing was asked for
        // (no startup arguments -- silently show the blank sample document, unchanged) and when a
        // requested file could not be opened (missing/locked/corrupt/unsupported -- WPF's equivalent
        // OpenPath already pops ShowError for this; Avalonia previously showed nothing at all). Only
        // the second case should alert, so gate on there having been an actual argument to try.
        _startupOpenFailed = startupDocument is null && startupArguments.Count > 0;
        _startupOpenFailurePath = startupArguments.Count > 0 ? startupArguments[0] : null;
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
            // r148-startup-fileopen: deferred to Opened (not shown synchronously in the constructor)
            // for the same reason the recovery offer below is -- AvaloniaUserMessageDialog.ShowDialog
            // needs an owner window that is already shown.
            await ShowStartupOpenFailureIfAnyAsync();
            if (!suppressStartupRecoveryOffer)
                await _autosave.OfferRecoveryAsync(this);
            await RefreshPrinterDiscoveryAsync();
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
                new SolidColorBrush(AvaloniaThemeApplier.ToColor(BrandThemes.FreeW.Colors.TitleBar))),
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
        AvaloniaWindowIconLoader.TryApply(this, App.ActiveTheme);

    public DocumentView Editor => _editor;

    public bool HasToolbar { get; private set; }

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
        FindReplaceOpenMode openMode = FindReplaceOpenMode.Find)
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

    private async Task OpenChangeCaseDialogAsync()
    {
        _editor.Focus();
        if (_editor.SelectedText.Length == 0)
        {
            await FreeWInfoDialog.ShowAsync(this, UiText.Get("ChangeCase_SelectText_Message"));
            return;
        }

        if (await ChangeCaseDialog.ShowAsync(this) is { } kind)
        {
            _editor.Focus();
            _editor.ChangeSelectionCase(kind);
        }
    }

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

    private Task OpenCellBordersDialogAsync() =>
        CellBordersDialog.ShowAndApplyAsync(this, _editor);

    private Task OpenSortDialogAsync() =>
        SortDialog.ShowAndApplyAsync(this, _editor);

    private ValueTask<ImageCropDialogResult?> ShowImageCropDialogAsync(InlineImage image) =>
        new(ImageCropDialog.ShowAsync(
            this,
            image.CropLeft,
            image.CropRight,
            image.CropTop,
            image.CropBottom));

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

    private ValueTask<ChartTitleDialogResult?> ShowChartTitleDialogAsync(Chart chart) =>
        new(ChartTitleDialog.ShowAsync(this, chart.Title));

    private ValueTask<ChartAxisTitlesDialogResult?> ShowChartAxisTitlesDialogAsync(Chart chart) =>
        new(ChartAxisTitlesDialog.ShowAsync(this, chart.CategoryAxisTitle, chart.ValueAxisTitle));

    private ValueTask<ChartSizeDialogResult?> ShowChartSizeDialogAsync(Chart chart) =>
        new(ChartSizeDialog.ShowAsync(this, chart.WidthPt, chart.HeightPt));

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
            _editor.InsertInlineImage(AvaloniaIconInsertionAdapter.Rasterize(selection));
        }
        catch (Exception ex)
        {
            await AvaloniaUserMessageDialog.ShowErrorAsync(
                this,
                IconPickerDialogPlanner.RasterizationErrorMessage(ex.Message),
                IconPickerDialogPlanner.Surface.Title);
        }
        finally
        {
            _editor.Focus();
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

    private ValueTask<char?> ShowTableToTextDialogAsync() =>
        new(TableTextConversionDialog.ShowAsync(
            this,
            TableTextConversionDialogPlanner.ResolveText(UiText.Get).TableToTextTitle));

    private async Task OpenTextToTableDialogAsync()
    {
        var delimiter = await TableTextConversionDialog.ShowAsync(
            this,
            TableTextConversionDialogPlanner.ResolveText(UiText.Get).TextToTableTitle);
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

    private ValueTask<SmartArt?> ShowSmartArtEditDialogAsync(SmartArt smartArt) =>
        new(SmartArtEditDialog.ShowAsync(this, smartArt));

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
            ObjectFormatCommandPlanner.ShapePositionDialogTitle(position.IsGroupLocal),
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
        var selectedWordArt = _editor.SelectedWordArt();
        if (selectedShape is null && selectedWordArt is null)
            return;
        var seed = selectedShape?.AltText ?? selectedWordArt?.AltText;
        var result = await ImageAltTextDialog.ShowAsync(this, seed ?? string.Empty);
        if (result is not null)
            _editor.SetSelectedFloatingAltText(result);
        _editor.Focus();
    }

    private ValueTask<Chart?> ShowChartDataDialogAsync(Chart chart) =>
        new(InsertChartDialog.ShowAsync(this, chart));

    private async Task OpenCaptionDialogAsync(CaptionLabel? requestedLabel = null)
    {
        var defaultLabel = requestedLabel
            ?? (_editor.IsCaretInTable() ? CaptionLabel.Table : CaptionLabel.Figure);
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
        MasterSourceStore.Save(masterStore, CreateMasterSourceStore(result.MasterSources));
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
            MasterSourceStore.Save(masterStore, CreateMasterSourceStore(result.MasterSources));
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
        _status.Text = enabled
            ? UiText.Get("Proofing_SpellCheckOn_Status")
            : UiText.Get("Proofing_SpellCheckOff_Status");
    }

    private void AddCurrentWordToDictionary()
    {
        var word = _editor.CurrentProofingWord;
        if (word is null)
        {
            _status.Text = UiText.Get("Proofing_SelectWord_Status");
            _editor.Focus();
            return;
        }

        _status.Text = _editor.AddCurrentWordToDictionary()
            ? UiText.Format("Proofing_AddedToDictionary_Status_Format", word)
            : UiText.Format("Proofing_AlreadyInDictionary_Status_Format", word);
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
            ? UiText.Get("Proofing_LanguageCleared_Status")
            : UiText.Format("Proofing_LanguageSet_Status_Format", normalized);
        _editor.Focus();
    }

    private async Task CompareDocumentsAsync()
    {
        var originalPath = await PromptReviewDocumentPathAsync(
            UiText.Get("Review_Compare_OriginalPickerTitle"));
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
            var original = OpenReviewDocument(
                picked.OriginalFilePath,
                UiText.Get("Review_CompareDocuments_Action"));
            var compared = ReviewCompareCombineWorkflow.ExecuteCompare(
                new CompareDocumentsExecutionInput(
                    original,
                    _editor.Document,
                    picked.Author,
                    ReviewCompareCombineWorkflow.CreateRevisionDateXml(DateTimeOffset.UtcNow),
                    picked.Settings));
            LoadReviewResult(
                compared,
                UiText.Format("Review_ComparedWith_Status_Format", Path.GetFileName(picked.OriginalFilePath)));
        }
        catch (Exception ex)
        {
            _status.Text = UiText.Format("Review_CompareFailed_Status_Format", ex.Message);
        }

        _editor.Focus();
    }

    private async Task CombineDocumentsAsync()
    {
        var originalPath = await PromptReviewDocumentPathAsync(
            UiText.Get("Review_Combine_OriginalPickerTitle"));
        if (originalPath is null)
            return;

        var reviewerBPath = await PromptReviewDocumentPathAsync(
            UiText.Get("Review_Combine_ReviewerBPickerTitle"));
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
            var combineAction = UiText.Get("Review_CombineDocuments_Action");
            var original = OpenReviewDocument(picked.OriginalFilePath, combineAction);
            var reviewerB = OpenReviewDocument(picked.ReviewerBFilePath, combineAction);
            var combined = ReviewCompareCombineWorkflow.ExecuteCombine(
                new CombineDocumentsExecutionInput(
                    original,
                    _editor.Document,
                    picked.AuthorA,
                    reviewerB,
                    picked.AuthorB,
                    ReviewCompareCombineWorkflow.CreateRevisionDateXml(DateTimeOffset.UtcNow)));
            LoadReviewResult(
                combined,
                UiText.Format("Review_CombinedWith_Status_Format", Path.GetFileName(picked.ReviewerBFilePath)));
        }
        catch (Exception ex)
        {
            _status.Text = UiText.Format("Review_CombineFailed_Status_Format", ex.Message);
        }

        _editor.Focus();
    }

    private async Task<string?> PromptReviewDocumentPathAsync(string title)
    {
        using var file = await AvaloniaFilePickerService.PickSingleOpenFileWithLocalPathAsync(
            StorageProvider,
            AvaloniaFilePickerOpenRequest.FromFileTypes(
                title,
                AvaloniaFilePickerTypeAdapter.ToFileTypes(
                    DocumentFileDialogRequestPlanner
                        .BuildOpenPickerPlan(_documentPersistence.Adapters)
                        .FileTypes)));
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
            _status.Text = CommentDialogPresentationPlanner.Text.MissingReplyTargetMessage;
            _editor.Focus();
            return;
        }

        var text = await CommentReplyDialog.AskAsync(this, CommentTextEntryKind.Reply);
        if (!string.IsNullOrWhiteSpace(text) && !_editor.ReplyToCommentAtCaret(text))
            _status.Text = CommentDialogPresentationPlanner.Text.MissingReplyTargetMessage;
        _editor.Focus();
    }

    private async Task NewCommentAsync()
    {
        var text = await CommentReplyDialog.AskAsync(this, CommentTextEntryKind.NewComment);
        if (!string.IsNullOrWhiteSpace(text))
            _editor.NewComment(text);
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
                PrintAsync,
                _editor.CurrentReviewDisplayState).ShowDialog(this);
        }
        catch (Exception ex)
        {
            _status.Text = SisterAppFileTextPlanner.FormatCommandFailed(
                FileText,
                UiText.Get("Operation_PrintPreview"),
                ex.Message);
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
        var dialog = new ZoomDialog(_zoomScale, ComputeZoomFitFactors());
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
            var plan = _documentWindowPlanner.CreateNext(
                _editor.Document,
                _fileWorkflow.CurrentPath,
                _fileWorkflow.IsDirty);
            var second = new MainWindow(
                Array.Empty<string>(),
                _options,
                _optionsStore,
                documentWindowPlanner: _documentWindowPlanner,
                documentWindowNumber: plan.WindowNumber);
            second.LoadDocumentContent(plan.Document);
            second._fileWorkflow.Workflow.ApplyDocumentState(plan.CurrentPath, plan.IsDirty);
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
            Title = FreeWUiTextCatalog.MailMergeErrorReportWindowTitle
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


    /// <summary>
    /// Detaches <paramref name="control"/> from whatever currently owns it. Avalonia throws
    /// "The Control already has a parent" on a re-parent, and the live workspace is handed back and
    /// forth between the workspace border, a split-preview grid and the page-preview surfaces — so any
    /// path that left it attached to a surface being torn down crashed the window on the way back.
    /// </summary>
    private static void DetachFromParent(Control control)
    {
        switch (control.Parent)
        {
            case Panel panel:
                panel.Children.Remove(control);
                break;
            case Decorator decorator when ReferenceEquals(decorator.Child, control):
                decorator.Child = null;
                break;
            case global::Avalonia.Controls.ContentControl content when ReferenceEquals(content.Content, control):
                content.Content = null;
                break;
        }
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
        {
            DetachFromParent(_liveWorkspaceContent);
            _workspace.Child = _liveWorkspaceContent;
        }
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
        DetachFromParent(_liveWorkspaceContent);
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
        if (plan.IsMultiplePagesActive)
        {
            _editor.Focus();
            return;
        }

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
        var callbacks = new FreeWRibbonHostExecutionPorts(
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
            GetDocumentViewChecks: CurrentDocumentViewChecks,
            IsNavigationPaneVisible: () => _navPane.IsVisible,
            IsRevealFormattingVisible: () => _revealPane.IsVisible,
            IsReviewingPaneVisible: () => _reviewingPane.IsVisible,
            SetOutlineView: ToggleOutlineView,
            IsOutlineViewActive: () => _outlineMode,
            OpenFontDialog:      () => _ = OpenFontDialogAsync(),
            OpenChangeCaseDialog: () => _ = OpenChangeCaseDialogAsync(),
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
            ShowImageCropDialogAsync: ShowImageCropDialogAsync,
            OpenImageSizeDialog: () => _ = OpenImageSizeDialogAsync(),
            OpenImageAltTextDialog: () => _ = OpenImageAltTextDialogAsync(),
            OpenImageBorderDialog: () => _ = OpenImageBorderDialogAsync(),
            OpenImageAdjustDialog: () => _ = OpenImageAdjustDialogAsync(),
            OpenImagePositionDialog: () => _ = OpenImagePositionDialogAsync(),
            OpenShapePositionDialog: () => _ = OpenShapePositionDialogAsync(),
            OpenShapeSizeDialog: () => _ = OpenShapeSizeDialogAsync(),
            OpenShapeAltTextDialog: () => _ = OpenShapeAltTextDialogAsync(),
            OpenInsertChartDialog: () => _ = OpenInsertChartDialogAsync(),
            ShowChartDataDialogAsync: ShowChartDataDialogAsync,
            ShowChartTitleDialogAsync: ShowChartTitleDialogAsync,
            ShowChartAxisTitlesDialogAsync: ShowChartAxisTitlesDialogAsync,
            ShowChartSizeDialogAsync: ShowChartSizeDialogAsync,
            OpenInsertSmartArtDialog: () => _ = OpenInsertSmartArtDialogAsync(),
            OpenIconPickerDialog: () => _ = OpenIconPickerDialogAsync(),
            OpenTextToTableDialog: () => _ = OpenTextToTableDialogAsync(),
            ShowTableToTextDialogAsync: ShowTableToTextDialogAsync,
            ShowSmartArtEditDialogAsync: ShowSmartArtEditDialogAsync,
            OpenDateTimeDialog: () => _ = OpenDateTimeDialogAsync(),
            OpenMultilevelListDialog: () => _ = OpenMultilevelListDialogAsync(),
            ToggleOrientation:   ToggleOrientation,
            ApplyMarginPreset:   ApplyMarginPreset,
            ApplyPaperSize:      ApplyPaperSize,
            InsertPicture:       () => _ = InsertPictureAsync(),
            InsertObject:        () => _ = InsertEmbeddedObjectAsync(),
            OpenSymbolPickerDialog: () => _ = OpenSymbolPickerAsync(),
            CaptureScreenClip: () => _ = InsertScreenClipAsync(),
            ShowTablePropertiesDialogAsync: ShowTablePropertiesDialogAsync,
            ShowTableFormulaDialogAsync: ShowTableFormulaDialogAsync,
            OpenWordCountDialog: () => _ = OpenWordCountDialogAsync(),
            OpenCaptionDialog: () => _ = OpenCaptionDialogAsync(),
            OpenCaptionDialogForLabel: label => _ = OpenCaptionDialogAsync(label),
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
            OpenCellBordersDialog: () => _ = OpenCellBordersDialogAsync(),
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
            NewComment: () => _ = NewCommentAsync(),
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
        var registry = FreeWAvaloniaRibbonCommands.Build(_editor, callbacks, out var mailMerge);
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
            RuleCommand(MailMergeRuleKind.IfThenElse));
        registry.Register(new RibbonCommandId("freew.merge-rule-skip-record-if"),
            RuleCommand(MailMergeRuleKind.SkipRecordIf));
        registry.Register(new RibbonCommandId("freew.merge-rule-next-record-if"),
            RuleCommand(MailMergeRuleKind.NextRecordIf));
        registry.Register(new RibbonCommandId("freew.merge-rule-fill-in"),
            RuleCommand(MailMergeRuleKind.FillIn));
        registry.Register(new RibbonCommandId("freew.merge-rule-ask"),
            RuleCommand(MailMergeRuleKind.Ask));
        registry.Register(new RibbonCommandId("freew.merge-rule-set"),
            RuleCommand(MailMergeRuleKind.Set));
        registry.Register(new RibbonCommandId("freew.merge-rule-ref"),
            RuleCommand(MailMergeRuleKind.Ref));

        IRibbonCommand RuleCommand(MailMergeRuleKind kind) =>
            new ActionRibbonCommand(() => _ = InsertMergeRuleAsync(kind));
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
        var canonicalDefinition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeWRibbonCapabilities.Avalonia);
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
                var title = MailMergeRuleDialogPlanner.ResolveInteractivePromptTitle(prompt.Kind, UiText.Get);
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
            var title = MailMergeRuleDialogPlanner.ResolveInteractivePromptTitle(prompt.Kind, UiText.Get);
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

    private async Task InsertMergeRuleAsync(MailMergeRuleKind kind)
    {
        if (_mailMerge is null)
            return;

        await _mailMerge.AuthorRuleAsync(
            kind,
            (request, _) => MailMergeDialogs.AskMergeRuleAsync(this, request));
        _editor.Focus();
    }

    // OS clipboard via Avalonia's data-transfer API (same pattern as the FreeX shell):
    // TopLevel.Clipboard with SetTextAsync / TryGetTextAsync.
    private Control BuildFindBar()
    {
        var next = new Button { Content = UiText.Get("Find_Inline_FindNext_Label"), Padding = new Thickness(10, 4), Margin = new Thickness(6, 0, 0, 0) };
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

        var replace = new Button { Content = UiText.Get("Find_Inline_Replace_Label"), Padding = new Thickness(10, 4), Margin = new Thickness(6, 0, 0, 0) };
        replace.Click += (_, _) => DoReplace();
        var replaceAll = new Button { Content = UiText.Get("Find_Inline_ReplaceAll_Label"), Padding = new Thickness(6, 4), Margin = new Thickness(4, 0, 0, 0) };
        replaceAll.Click += (_, _) => DoReplaceAll();

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8, 4),
            Children =
            {
                new TextBlock { Text = UiText.Get("Find_Inline_Find_Label"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) },
                _findBox,
                next,
                new TextBlock { Text = UiText.Get("Find_Inline_Replace_FieldLabel"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 6, 0) },
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
                new SolidColorBrush(AvaloniaThemeApplier.ToColor(BrandThemes.FreeW.Colors.StatusSurface))),
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
            UiText.Get("View_WebLayout_HelpText"),
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
            UiText.Get("View_PageEdit_HelpText"),
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
        AutomationProperties.SetName(_zoomSlider, FreeWUiTextCatalog.Zoom);
        ToolTip.SetTip(_zoomSlider, FreeWUiTextCatalog.Zoom);
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

        panel.Children.Add(BuildZoomButton("\u2212", FreeWUiTextCatalog.ZoomOut, foreground, () => ApplyZoom(ZoomLevels.StepDown(_zoomScale))));
        panel.Children.Add(_zoomSlider);
        panel.Children.Add(BuildZoomButton("+", FreeWUiTextCatalog.ZoomIn, foreground, () => ApplyZoom(ZoomLevels.StepUp(_zoomScale))));

        var percentage = new Button
        {
            Content = _zoomLabel,
            Padding = new Thickness(2, 0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
        };
        AutomationProperties.SetName(percentage, FreeWUiTextCatalog.Zoom);
        ToolTip.SetTip(percentage, FreeWUiTextCatalog.Zoom);
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

        var plan = _viewSession.EnterOutline(_pagedEditMode);
        if (plan.ExitPageSurface)
            ApplyViewDepthTransition(_viewSession.RestoreLiveEditor(), updateStatus: false);

        _pagedEditMode = plan.IsPagedEditMode;
        _outlineMode = plan.IsOutlineMode;
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

        var plan = _viewSession.LeaveOutline(restorePriorView);
        _outlineMode = plan.IsOutlineMode;
        _pagedEditMode = plan.IsPagedEditMode;
        if (_liveWorkspaceContent is not null)
        {
            DetachFromParent(_liveWorkspaceContent);
            _workspace.Child = _liveWorkspaceContent;
        }
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
        var plan = CurrentDocumentViewChecks();
        ApplyStatusToggleState(_printLayoutSwitch, plan.PrintLayout);
        ApplyStatusToggleState(_webLayoutSwitch, plan.WebLayout);
        ApplyStatusToggleState(_draftSwitch, plan.Draft);
        ApplyStatusToggleState(_pagedEditSwitch, plan.PagedEdit);
    }

    private FreeWDocumentViewCheckPlan CurrentDocumentViewChecks() =>
        _viewSession.BuildDocumentViewChecks(
            _editor.ViewMode,
            _outlineMode,
            _pagedEditMode);

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
        var transition = AvaloniaRibbonKeyTipInputPlanner.ResolveModeTransition(
            args.Key,
            args.KeyModifiers,
            _ribbonKeyTipsVisible);
        if (transition.ModeVisible is { } modeVisible)
            SetRibbonKeyTipsVisible(modeVisible);
        if (!transition.ShouldRouteToken)
        {
            if (transition.Handled)
                args.Handled = true;
            return transition.Handled;
        }

        if (_ribbonControl is null)
            return false;

        if (!AvaloniaRibbonRenderer.TryActivateTopLevelKeyTip(_ribbonControl, transition.Token!))
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

    private Task<bool> NewDocumentAsync() => _fileCommands.NewAsync();

    private void ToggleFindBar(bool show)
    {
        if (_findBar is null)
            return;
        _findBar.IsVisible = show;
        if (show)
            _findBox.Focus();
    }

    private void DoFind()
        => ExecuteInlineFindReplace(FindReplaceDialogActionKind.FindNext);

    private void DoReplace()
        => ExecuteInlineFindReplace(FindReplaceDialogActionKind.Replace);

    private void DoReplaceAll()
        => ExecuteInlineFindReplace(FindReplaceDialogActionKind.ReplaceAll);

    private void ExecuteInlineFindReplace(FindReplaceDialogActionKind action) =>
        _status.Text = _inlineFindReplaceSession.Execute(
            action,
            new FindReplaceDialogInput(
                _findBox.Text,
                _replaceBox.Text,
                MatchCase: false,
                WholeWord: false,
                UseWildcards: false)).StatusText;

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

    private async Task<FreeWClipboardTransferResult> CopyAsync()
    {
        // shell-clipboard F2: unlike the WPF shell's native RichTextBox Copy/Cut (which places RTF
        // and an HTML/Xaml payload on the clipboard automatically), this editor is a custom control
        // with no such native behaviour, so it must build the rich payload itself -- otherwise every
        // Copy+Paste round trip silently drops all character formatting, even within this document.
        var (document, ranges) = _editor.GetSelectionRichSnapshot();
        var richDocument = FreeWClipboardApplicationWorkflow.BuildSelectionRichDocument(document, ranges);
        // ...and alongside it FreeW's own flavour, which keeps what RTF/HTML cannot express — a content
        // control, a tracked change's author, a comment anchor — for a paste back into FreeW.
        var nativeDocument = FreeWClipboardApplicationWorkflow.BuildSelectionNativeDocument(document, ranges);
        var result = await FreeWClipboardApplicationWorkflow.WriteSelectionAsync(
            _platformClipboard,
            _editor.SelectedText,
            richDocument,
            nativeDocument);
        if (result.Status is FreeWClipboardTransferStatus.Unsupported or FreeWClipboardTransferStatus.Failed)
            ApplyClipboardFeedback(result);
        return result;
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
            _status.Text = SisterAppFileTextPlanner.FormatCommandFailed(
                FileText,
                UiText.Get("Operation_Editor"),
                ex.Message);
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
        var copy = await CopyAsync();
        if (copy.CanCommitCut)
            _editor.TryDeleteSelection();
    }

    /// <summary>
    /// clipboard-interop F1 (Avalonia twin of the WPF fix in DocumentView.OnPasteExecuted): ordinary
    /// Paste (Ctrl+V, the plain ribbon Paste button -- see callbacks.Paste above -- and the editor
    /// context menu's Paste item) used to call <see cref="FreeWClipboardApplicationWorkflow.ReadTextAsync"/>
    /// unconditionally, which reads with includeRichDocument:false and so can never recover formatting --
    /// even though the IDENTICAL clipboard content pastes with formatting intact via Paste Special > Keep
    /// Source Formatting (<see cref="OpenPasteSpecialAsync"/>, which already reads RTF and both HTML
    /// clipboard flavors through <see cref="FreeWClipboardApplicationWorkflow.ReadPasteSpecialAsync"/>).
    /// This routes ordinary Paste through that SAME already-working plan (skipping only the option-picker
    /// dialog, since ordinary Paste always wants Keep Source Formatting), so Ctrl+V and the plain ribbon
    /// button recover formatting exactly like Paste Special's "Keep Source Formatting" choice does. A
    /// plain-text-only clipboard (no RTF/HTML) degrades gracefully to the same plain-text insertion the
    /// old ReadTextAsync path would have produced.
    /// </summary>
    private async Task PasteAsync()
    {
        var transfer = await FreeWClipboardApplicationWorkflow.ReadPasteSpecialAsync(_platformClipboard);
        if (!transfer.IsSuccess || transfer.Payload is null)
        {
            ApplyClipboardFeedback(transfer);
            return;
        }

        var plan = FreeWClipboardApplicationWorkflow.PlanPaste(transfer.Payload, PasteSpecialOption.KeepSourceFormatting);
        if (!ApplyClipboardPastePlan(plan))
            _status.Text = FreeWClipboardApplicationWorkflow.EmptyClipboardMessage;
    }

    /// <summary>
    /// clipboard-interop F1 twin's shared apply step (PasteAsync and OpenPasteSpecialAsync both route
    /// through here so they cannot disagree). A RichDocument insert is tried first; if it fails, Text is
    /// the fallback.
    ///
    /// freew-clip-image-text (R159): a synthesized image RichDocument (freew-paste-formats F1) does not
    /// fold the clipboard's independent Text into itself the way an HTML/RTF RichDocument does -- an
    /// HTML/RTF paste already contains its Text, so pasting Text too would duplicate it, but a
    /// synthesized image paste does not, so a clipboard carrying both a bitmap and unrelated plain text
    /// (a screenshot tool that also copies the saved file path, say) must still get the text inserted
    /// after the image rather than silently dropped.
    /// </summary>
    private bool ApplyClipboardPastePlan(FreeWClipboardPastePlan plan)
    {
        if (plan.RichDocument is not { } richDocument)
        {
            return plan.TextKind == DocumentPasteTextKind.TextOnly
                ? _editor.PastePlainText(plan.Text)
                : _editor.PasteMergeFormatting(plan.Text);
        }

        if (!_editor.PasteKeepSourceFormatting(richDocument))
            return _editor.PasteMergeFormatting(plan.Text);

        if (plan.RichDocumentIsSynthesizedImage)
            _editor.PasteMergeFormatting(plan.Text);
        return true;
    }

    private async Task PastePlainTextAsync()
    {
        var transfer = await FreeWClipboardApplicationWorkflow.ReadTextAsync(_platformClipboard);
        ApplyClipboardText(transfer, DocumentPasteTextKind.TextOnly);
    }

    private async Task PasteMergeFormattingAsync()
    {
        var transfer = await FreeWClipboardApplicationWorkflow.ReadTextAsync(_platformClipboard);
        ApplyClipboardText(transfer, DocumentPasteTextKind.MergeFormatting);
    }

    private async Task OpenPasteSpecialAsync()
    {
        var transfer = await FreeWClipboardApplicationWorkflow.ReadPasteSpecialAsync(_platformClipboard);
        if (!transfer.IsSuccess || transfer.Payload is null)
        {
            ApplyClipboardFeedback(transfer);
            return;
        }

        var option = await PasteSpecialDialog.ShowAsync(this);
        if (option is null)
            return;

        var plan = FreeWClipboardApplicationWorkflow.PlanPaste(transfer.Payload, option.Value);
        if (!ApplyClipboardPastePlan(plan))
            _status.Text = FreeWClipboardApplicationWorkflow.EmptyClipboardMessage;
    }

    private bool ApplyClipboardText(
        FreeWClipboardTransferResult transfer,
        DocumentPasteTextKind kind)
    {
        if (!transfer.IsSuccess || transfer.Payload is null)
        {
            ApplyClipboardFeedback(transfer);
            return false;
        }

        var pasted = kind == DocumentPasteTextKind.TextOnly
            ? _editor.PastePlainText(transfer.Payload.Text)
            : _editor.PasteMergeFormatting(transfer.Payload.Text);
        if (!pasted)
            _status.Text = FreeWClipboardApplicationWorkflow.EmptyClipboardMessage;
        return pasted;
    }

    private void ApplyClipboardFeedback(FreeWClipboardTransferResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.FeedbackMessage))
            _status.Text = result.FeedbackMessage;
    }

    private async Task OpenAsync() => await _fileCommands.OpenAsync();

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
        _fileCommands.OpenSelectedPathAsync(path);

    private async Task<string?> PromptOpenPathAsync(FreeWDocumentOpenPickerRequest request)
    {
        using var file = await AvaloniaFilePickerService.PickSingleOpenFileWithLocalPathAsync(
            StorageProvider,
            AvaloniaFilePickerOpenRequest.FromFileTypes(
                FileText.OpenPickerTitle,
                AvaloniaFilePickerTypeAdapter.ToFileTypes(
                    DocumentFileDialogRequestPlanner
                        .BuildOpenPickerPlan(_documentPersistence.Adapters)
                        .FileTypes)));
        return file?.LocalPath;
    }

    private Task<bool> ImportPdfTextAsync() =>
        _fileCommands.ImportPdfTextAsync();

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

    private Task<bool> SaveAsync() => _fileCommands.SaveAsync();

    private Task<bool> SaveAsAsync() => _fileCommands.SaveAsAsync();

    private async Task<FreeWDocumentSavePickerResult?> PromptSavePathAsync(
        FreeWDocumentSavePickerRequest request)
    {
        var savePlan = _documentPersistence.BuildSavePickerPlan(
            request.CurrentPath,
            request.SuggestedFileName ?? request.CurrentFileName,
            FileText.FallbackDisplayName,
            request.PreferredExtension);
        using var file = await AvaloniaFilePickerService.PickSaveFileWithLocalPathAsync(
            StorageProvider,
            AvaloniaFilePickerSaveRequest.FromSavePlan(request.Title, savePlan));
        var path = file?.LocalPath;
        return path is null ? null : new FreeWDocumentSavePickerResult(path);
    }

    private bool ApplyFileFeedback(FreeWDocumentFileFeedback feedback)
    {
        _status.Text = feedback.Message;
        // r148: a real Save/Open/Import failure (disk full, locked file, permission denied, ...)
        // must reach a modal alert -- the WPF host already routes ShouldShowError to ShowError; this
        // status-bar-only line was the Avalonia gap (a user mid-typing or looking away would never
        // see it). PresentFeedback is a synchronous Action port shared with the WPF host's signature,
        // so this is necessarily fire-and-forget; ShowFileCommandErrorAsync still serializes behind
        // its own owner-window dialog machinery like every other Avalonia alert in this shell.
        if (feedback.ShouldShowError)
            _ = _fileWorkflow.ShowFileCommandErrorAsync(feedback.ErrorSummary!, feedback.Exception!);
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
                (intent, token) => _showPrintSelectionDialog(this, intent.Discovery, token),
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
                requestedSelection: null,
                cancellationToken: cancellation.Token);
            _latestPrinterDiscovery = execution.Discovery ?? _latestPrinterDiscovery;
            var message = FreeWPrintMessagePlanner.FormatExecution(execution);
            _status.Text = printPdfResult is { ImageDiagnostics.Count: > 0 }
                ? UiText.Format(
                    "Print_ImageWarnings_Status_Format",
                    message,
                    printPdfResult.ImageDiagnostics.Count)
                : message;
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

    private async Task RefreshPrinterDiscoveryAsync()
    {
        try
        {
            _latestPrinterDiscovery = _printService.IsSupported
                ? await _printService.DiscoverAsync()
                : new PrinterDiscoveryResult(PrinterDiscoveryStatus.Unavailable, [], null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _latestPrinterDiscovery = new PrinterDiscoveryResult(
                PrinterDiscoveryStatus.Failed,
                [],
                null,
                ex.Message);
        }
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
        FreeWDocumentFragmentImportPlanner.CreateEmbeddedObjectRequest());

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

    private async ValueTask<TableFormulaField?> ShowTableFormulaDialogAsync(
        TableFormulaDialogInitialState initialState)
    {
        var dialog = new TableFormulaDialog(initialState);
        await dialog.ShowDialog(this);
        return dialog.Result;
    }

    internal static void ApplyTableFormulaResult(DocumentView editor, TableFormulaField? formula)
    {
        ArgumentNullException.ThrowIfNull(editor);
        if (formula is not null)
            editor.InsertTableFormula(formula);
    }

    private async ValueTask<TablePropertiesValues?> ShowTablePropertiesDialogAsync(ModelTableContext context)
    {
        var dialog = new TablePropertiesDialog(context);
        await dialog.ShowDialog(this);
        return dialog.Result;
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
            var result = await _screenClipWorkflow.ExecuteAsync(
                cancellationToken => _screenClipService.CaptureAsync(this, cancellationToken),
                image => ApplyScreenClipImage(_editor, image));
            if (result.Outcome == ScreenClipWorkflowOutcome.Inserted)
            {
                _status.Text = UiText.Format(
                    "ScreenClip_Inserted_Status_Format",
                    result.PixelWidth,
                    result.PixelHeight);
            }
            else if (result.Outcome == ScreenClipWorkflowOutcome.Failed)
            {
                _status.Text = UiText.Format(
                    "ScreenClip_Failed_Status_Format",
                    result.FailureMessage ?? string.Empty);
            }
        }
        finally
        {
            _editor.Focus();
        }
    }

    internal static void ApplyScreenClipImage(DocumentView editor, InlineImage image)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(image);
        editor.InsertInlineImage(
            image.Bytes,
            image.WidthPt,
            image.HeightPt,
            image.Format,
            image.OriginalPixelWidth,
            image.OriginalPixelHeight);
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

        var dialog = new HyperlinkDialog(
            initialDisplay: _editor.HyperlinkDisplayTextAtCaret(),
            initialAddress: _editor.HyperlinkTargetAtCaret(),
            mode: HyperlinkDialogMode.Edit);
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
        {
            if (_editor.InsertBookmark(add) == BookmarkInsertOutcome.DuplicateName)
            {
                await FreeWInfoDialog.ShowAsync(
                    this,
                    UiText.Format("Bookmark_DuplicateName_Message_Format", add),
                    UiText.Get("Bookmark_Title"));
            }
        }
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
        var presentation = LinkBookmarkDialogPlanner.Build(_editor.BookmarkNames());
        if (presentation.IsEmpty)
        {
            await FreeWInfoDialog.ShowAsync(this, presentation.EmptyMessage, presentation.EmptyTitle);
            _editor.Focus();
            return;
        }

        var dialog = new LinkBookmarkDialog(presentation);
        await dialog.ShowDialog(this);
        if (dialog.BookmarkName is { } bookmark)
            _editor.ApplyInternalLink(bookmark);
        _editor.Focus();
    }

    /// <summary>
    /// Opens the shared saved Quick Part picker and inserts the selected library entry at the caret.
    /// </summary>
    private async Task OpenQuickPartDialogAsync()
    {
        var session = new QuickPartInsertSession(_quickParts);
        if (session.Current.IsEmpty)
        {
            await FreeWInfoDialog.ShowAsync(
                this,
                QuickPartCommandPlanner.ResolveText(UiText.Get).EmptyLibraryMessage);
            _editor.Focus();
            return;
        }

        var dialog = new QuickPartDialog(session);
        await dialog.ShowDialog(this);
        if (dialog.Action is { } action)
            _editor.InsertQuickPartText(action.Text);
        _editor.Focus();
    }

    private async Task SaveQuickPartSelectionAsync()
    {
        var selectedText = _editor.SelectedText;
        if (string.IsNullOrEmpty(selectedText))
        {
            await FreeWInfoDialog.ShowAsync(
                this,
                QuickPartCommandPlanner.ResolveText(UiText.Get).EmptySelectionMessage);
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
        FreeWDocumentFragmentImportPlanner.CreateTextFromFileRequest());

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

    /// <summary>
    /// r148-startup-fileopen: a command-line/file-association startup path that could not be opened
    /// used to fall back to the blank sample document with no feedback anywhere -- the WPF host's
    /// equivalent (<c>FileCommands.OpenPath</c>, see the R133-wpf-startup-file-args constructor
    /// comment) already shows a modal ShowError for the identical gesture.
    /// <see cref="FreeWApplicationStartup.TryOpenStartupDocument"/> returns null uniformly for "no
    /// startup arguments" and "the argument couldn't be opened", so <see cref="_startupOpenFailed"/>
    /// (computed in the constructor, before it is known which case this is) is what distinguishes
    /// them -- a plain launch with no file must stay silent.
    /// </summary>
    private Task ShowStartupOpenFailureIfAnyAsync() =>
        _startupOpenFailed
            ? _fileWorkflow.ShowFileCommandErrorAsync(
                "Could not open the document",
                new InvalidOperationException(
                    $"'{Path.GetFileName(_startupOpenFailurePath)}' could not be opened. " +
                    "It may be missing, in use by another program, or in an unsupported format."))
            : Task.CompletedTask;

    private void LoadDocumentAsSaved(TextDocument document, string? path)
    {
        LoadDocumentContent(document);

        if (path is null)
            _fileWorkflow.MarkSavedWithoutPath();
        else
            MarkDocumentSavedWithPath(path);

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

    private void MarkDocumentSavedWithPath(string path) =>
        _fileWorkflow.MarkSavedWithPath(path, suppressRecentFiles: false);

    private void OnFileWorkflowChanged()
    {
        RefreshDocumentWindowTitle();
        _editor.CurrentFileName = _fileWorkflow.CurrentFileName;
        UpdateStatus();
    }

    private void RefreshDocumentWindowTitle()
    {
        Title = ApplicationWindowTitlePolicy.Compose(
            FreeWApplicationFrameDescriptor.Title,
            _fileWorkflow.CurrentFileName ?? FreeWApplicationFrameDescriptor.Title.DefaultDocumentDisplayName,
            _fileWorkflow.IsDirty,
            FreeWDocumentWindowPlanner.FormatWindowSuffix(_documentWindowNumber),
            isDefaultDocument: _fileWorkflow.CurrentPath is null);
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
            RecoverUnsaved: () => _ = _autosave.RecoverUnsavedDocumentsAsync(this),
            ImportPdfText: () => _ = ImportPdfTextAsync(),
            Save: () => _applicationCommands.Execute(FreeWKeyboardCommand.SaveDocument),
            SaveAs: () => _applicationCommands.Execute(FreeWKeyboardCommand.SaveDocumentAs),
            SaveAsFormat: (extension, filterIndex) =>
            {
                _ = filterIndex;
                _ = _fileCommands.SaveAsFormatAsync(extension);
            },
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

    private async Task SaveCopyAsync() => await _fileCommands.SaveCopyAsync();

    internal Task<bool> SaveCopyToPathAsync(string path, int filterIndex = 0)
        => _fileCommands.SavePathAsync(path, filterIndex, DocumentSaveExecutionKind.SaveCopy);

    private async Task OpenPropertiesAsync()
    {
        var dialog = new PropertiesDialog(_editor.Document.Properties);
        await dialog.ShowDialog(this);
        if (!dialog.Accepted || dialog.Result is not { } result)
            return;

        _editor.ApplyDocumentProperties(result);
        _status.Text = UiText.Get("DocumentProperties_Updated_Status");
        _editor.Focus();
    }

    private void ToggleMarkAsFinal()
    {
        var text = BackstageInfoSafetyPanePlanner.ResolveText(UiText.Get);
        _editor.SetMarkedAsFinal(!_editor.IsMarkedAsFinal);
        _status.Text = _editor.IsMarkedAsFinal
            ? text.MarkedAsFinalStatus
            : text.NotMarkedAsFinalStatus;
        _editor.Focus();
    }

    private async Task OpenRestrictEditingAsync()
    {
        var dialog = new RestrictEditingDialog(_editor.Document.Protection);
        await dialog.ShowDialog(this);
        if (dialog.Result is not { } settings)
            return;

        _editor.SetProtection(settings);
        var text = BackstageInfoSafetyPanePlanner.ResolveText(UiText.Get);
        _status.Text = settings.Mode == ProtectionMode.None
            ? text.RestrictionsRemovedStatus
            : string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                text.RestrictionsAppliedFormat,
                settings.Mode);
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

        var text = BackstageInfoSafetyPanePlanner.ResolveText(UiText.Get);
        _status.Text = choice.Any
            ? text.SelectedDataRemovedStatus
            : text.InspectorCompletedStatus;
        _editor.Focus();
    }

    private async Task CheckAccessibilityAsync()
    {
        var report = AccessibilityChecker.Check(_editor.Document);
        var dialog = new AccessibilityReportDialog(report);
        await dialog.ShowDialog(this);
        var text = BackstageInfoSafetyPanePlanner.ResolveText(UiText.Get);
        _status.Text = report.IsClean
            ? text.NoAccessibilityIssuesStatus
            : string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                text.AccessibilityIssueCountStatusFormat,
                report.Issues.Count);
        _editor.Focus();
    }

    private async Task OpenOptionsAsync()
    {
        var dialog = new OptionsDialog(_options);
        await dialog.ShowDialog(this);
        if (dialog.Result is not { } edited)
            return;

        var outcome = _optionsRuntime.ApplyAndPersist(
            edited,
            options => _optionsStore.Save(options),
            () => _optionsStore.Load());
        ApplyEditorTypingOptions(outcome.EditorTypingOptions);
        if (!outcome.Persisted)
            _status.Text = _optionsStore.LastError ?? UiText.Get("Options_SaveFailed_Status");
        else
            _status.Text = UiText.Get("Options_Saved_Status");
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
            _status.Text = UiText.Format("Shell_OpenFolderFailed_Status_Format", result.Error.Message);
    }

    // Opens an external URL raised by DocumentView.HyperlinkActivated through the shared scheme allowlist.
    // Mirrors the WPF host's OnHyperlinkRequestNavigate: blocked schemes and launch failures are silently
    // dropped so a bad URL never crashes the editor.
    private static void OpenExternalUri(string url) => _ = TryOpenExternalUri(url);

    private static ExternalUriLaunchResult TryOpenExternalUri(string url) =>
        DesktopExternalUriLauncher.Open(url);
}
