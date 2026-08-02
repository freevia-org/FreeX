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
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;
using Free.Shared.Theme;
using FreeW.App.Avalonia.Backstage;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Pdf;
using FreeW.App.Avalonia.Printing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.App.Presentation;
using FreeW.App.Presentation.Backstage;
using FreeW.App.Presentation.ContextMenus;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Options;
using FreeW.App.Presentation.QuickParts;
using FreeW.App.Presentation.Ribbon;
using FreeW.App.Presentation.Shell;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

public sealed partial class MainWindow : Window
{
    private const string DefaultTitle = "FreeW";
    private static readonly SisterAppFileTextSpec FileText = SisterAppFileTextPlanner.Document;

    private static readonly FilePickerFileType PdfFileType =
        AvaloniaFilePickerTypeAdapter.CreateFileType(
            FreeWFileTextResources.PdfFileTypeName,
            ["*.pdf"],
            ["application/pdf"]);
    private static readonly FilePickerFileType XpsFileType =
        AvaloniaFilePickerTypeAdapter.CreateFileType(
            FreeWFileTextResources.XpsFileTypeName,
            ["*.xps"],
            ["application/oxps", "application/vnd.ms-xpsdocument"]);

    private readonly DocumentPersistenceWorkflow _documentPersistence = new();
    private readonly IPlatformPrintService _printService;
    private readonly Func<Window, PrinterDiscoveryResult, CancellationToken, Task<PrintSelection?>> _showPrintSelectionDialog;
    private readonly Action<IInputElement?> _restorePrintOwnerFocus;
    private readonly Func<IStorageProvider, AvaloniaFilePickerSaveRequest, Task<(bool Canceled, string? LocalPath)>> _pickExportPath;
    private readonly Func<bool, string, Task<string?>>? _askHeaderFooterText;
    private readonly IScreenClipService _screenClipService;
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
    private readonly ApplicationOptionsStore<FreeWOptions> _optionsStore;
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
    private FreeWViewDepthPlan _viewDepthPlan = FreeWViewDepthPlanner.Build(FreeWViewDepthMode.LiveEditor);
    private FreeWViewDepthPagePairNavigationState _sideToSideNavigation =
        FreeWViewDepthPlanner.BuildPagePairNavigation(
            FreeWViewDepthPlanner.Build(FreeWViewDepthMode.LiveEditor),
            requestedFirstVisiblePageNumber: 1,
            totalPages: 1);
    private ScrollViewer? _sideToSidePreviewScrollViewer;
    private Button? _sideToSidePreviousPairButton;
    private Button? _sideToSideNextPairButton;
    private TextBlock? _sideToSidePairStatusText;
    private double _sideToSidePairScrollStrideDip;
    private double _sideToSidePlannedHorizontalOffsetDip;
    private bool _sideToSideUsesLiveEditor;
    private double _zoomScale = 1.0;
    private bool _updatingZoomSlider;
    private bool _readMode;
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
    private bool _navPaneVisibleBeforeReadMode;
    private bool _reviewingPaneVisibleBeforeReadMode;
    private bool _revealPaneVisibleBeforeReadMode;
    private bool _titleBarVisibleBeforeReadMode;
    private bool _ribbonVisibleBeforeReadMode;
    private bool _dataFolderVisibleBeforeReadMode;
    private bool _statusViewSwitchVisibleBeforeReadMode;
    private bool _statusZoomVisibleBeforeReadMode;
    private IBrush _workspaceBackgroundBeforeReadMode = Brushes.Transparent;
    private string _readModeColumnWidth = FreeWReadModePlanner.DefaultColumn;
    private string _readModePageColor = FreeWReadModePlanner.NoColor;
    private bool _suppressEditorDirty;
    private AvaloniaSpeechEngine? _readAloudEngine;
    private ReadAloudController? _readAloudController;
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
            ApplicationOptionsStore<FreeWOptions>.Create(PlatformApplicationDataPathProvider.LocalInstance))
    {
    }

    internal MainWindow(
        IReadOnlyList<string> startupArguments,
        FreeWOptions? options,
        ApplicationOptionsStore<FreeWOptions> optionsStore,
        IScreenClipService? screenClipService = null,
        IPlatformPrintService? printService = null,
        Func<Window, PrinterDiscoveryResult, CancellationToken, Task<PrintSelection?>>? showPrintSelectionDialog = null,
        Action<IInputElement?>? restorePrintOwnerFocus = null,
        Func<IStorageProvider, AvaloniaFilePickerSaveRequest, Task<(bool Canceled, string? LocalPath)>>? pickExportPath = null,
        Func<string, Task<SaveChangesPrompt>>? promptSaveChangesAsync = null,
        Func<string, Exception, Task>? showFileCommandErrorAsync = null,
        Func<bool, string, Task<string?>>? askHeaderFooterText = null)
    {
        _optionsStore = optionsStore;
        _screenClipService = screenClipService ?? new AvaloniaScreenClipService();
        _printService = printService ?? new CupsPrintService();
        _showPrintSelectionDialog = showPrintSelectionDialog ??
            ((owner, discovery, cancellationToken) =>
                CupsPrintDialog.ShowAsync(owner, discovery, cancellationToken: cancellationToken));
        _restorePrintOwnerFocus = restorePrintOwnerFocus ?? RestorePrintOwnerFocus;
        _pickExportPath = pickExportPath ?? PickExportPathAsync;
        _askHeaderFooterText = askHeaderFooterText;
        _options = options ?? _optionsStore.Load();
        _options.Normalize();
        _editor.AutoCorrectEnabled = _options.AutoCorrectEnabled;
        _editor.AutoFormatOptions = _options.AutoFormat ?? AutoFormatOptions.Default;
        _editor.AutoCorrectOptions = _options.AutoCorrect ?? AutoCorrectOptions.Default;

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
        _autosave = new AutosaveAdapter(_editor, _fileWorkflow.Workflow);
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
        _thesaurusPane = new ThesaurusPane(_editor);
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
        var startupDocument = LoadStartupDocument(startupArguments);
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
        Opened += async (_, _) =>
        {
            _autosave.Start();
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
            TitleBarBackground: ResolveThemeBrush(
                "FreeWTitleBarBrush",
                new SolidColorBrush(Color.FromRgb(0x17, 0x32, 0x4D))),
            TitleBarForeground: ResolveThemeBrush("FreeWWhiteBrush", Brushes.White)));
        _titleBar = windowFrame.TitleBar;
        _quickAccessButtons = SisterQuickAccessToolbarBuilder.Render(
            windowFrame.QatHost,
            new SisterQuickAccessToolbarActions(
                Save: () => _ = SaveAsync(),
                Undo: _editor.Undo,
                Redo: _editor.Redo),
            ResolveThemeBrush("FreeWWhiteBrush", Brushes.White));

        Content = windowFrame.Root;
        UpdateStatus();
    }

    private void ApplyWindowIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "FreeW.ico");
        if (!File.Exists(iconPath))
            return;

        try
        {
            using var stream = File.OpenRead(iconPath);
            Icon = new WindowIcon(stream);
        }
        catch
        {
            // Unsupported desktop icon formats must not prevent the document from opening.
        }
    }

    private static IBrush ResolveThemeBrush(string key, IBrush fallback)
    {
        if (Application.Current is { } app &&
            app.TryGetResource(key, global::Avalonia.Styling.ThemeVariant.Default, out var value) &&
            value is IBrush brush)
        {
            return brush;
        }

        return fallback;
    }

    public DocumentView Editor => _editor;

    internal bool IsReadAloudActiveForTest => _readAloudController?.IsActive == true;

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

    internal FreeWViewDepthMode ViewDepthMode => _viewDepthPlan.Mode;
    internal bool IsSplitPreviewActive => _viewDepthPlan.IsSplitActive;
    internal bool IsMultiplePagesPreviewActive => _viewDepthPlan.IsMultiplePagesActive;
    internal bool IsSideToSidePreviewActive => _viewDepthPlan.IsSideToSideActive;
    internal string? ViewDepthLimitation => _viewDepthPlan.Limitation;
    internal FreeWViewDepthPagePairNavigationState SideToSideNavigationForTests => _sideToSideNavigation;
    internal bool HasSideToSidePagePairNavigationForTests =>
        _sideToSidePreviewScrollViewer is not null &&
        _sideToSidePreviousPairButton is not null &&
        _sideToSideNextPairButton is not null &&
        _sideToSidePairStatusText is not null;
    internal Vector SideToSidePreviewOffsetForTests => new(_sideToSidePlannedHorizontalOffsetDip, 0);
    internal Control? WorkspaceContentForTests => _workspace.Child as Control;
    internal bool IsWorkspaceShowingLiveEditor => ReferenceEquals(_workspace.Child, _liveWorkspaceContent);
    internal bool IsSideToSideEditorEditableForTests => _sideToSideUsesLiveEditor;
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
            _status.Text = SisterAppFileTextPlanner.FormatCommandFailed("Insert Icon", ex.Message);
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
        if (result is not null)
            _editor.ApplyFootnoteEndnoteOptions(result);
        _editor.Focus();
    }

    private async Task OpenMultilevelListDialogAsync()
    {
        var result = await MultilevelListDialog.ShowAsync(this, _editor.Document.MultiLevelList.NumberFormats);
        if (result is not null)
            _editor.ApplyMultiLevelListDefinition(result);
        _editor.Focus();
    }

    private async Task OpenTableOfAuthoritiesDialogAsync()
    {
        var options = await TableOfAuthoritiesDialog.ShowAsync(this);
        if (options is not null)
            _editor.InsertTableOfAuthorities(options);
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
            var snapshot = CloneDocument(_editor.Document);
            return new PrintPreviewDialog(
                snapshot,
                _fileWorkflow.DisplayName,
                ExportPdfAsync,
                DirectPrintCapability,
                PrintAsync).ShowDialog(this);
        }
        catch (Exception ex)
        {
            _status.Text = SisterAppFileTextPlanner.FormatCommandFailed("Print Preview", ex.Message);
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
            _status.Text = SisterAppFileTextPlanner.FormatCommandFailed("New window", ex.Message);
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
        ApplyViewDepthPlan(FreeWViewDepthPlanner.Plan(CurrentViewDepthState(), FreeWViewDepthCommand.ToggleSplit));

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
        ApplyViewDepthPlan(FreeWViewDepthPlanner.Plan(CurrentViewDepthState(), FreeWViewDepthCommand.ToggleMultiplePages));

    internal void ToggleSideToSide() =>
        ApplyViewDepthPlan(FreeWViewDepthPlanner.Plan(CurrentViewDepthState(), FreeWViewDepthCommand.ToggleSideToSide));

    internal void NavigateSideToSideNextPairForTests() =>
        NavigateSideToSidePagePair(FreeWViewDepthPagePairNavigationCommand.NextPair);

    internal void NavigateSideToSidePreviousPairForTests() =>
        NavigateSideToSidePagePair(FreeWViewDepthPagePairNavigationCommand.PreviousPair);

    private FreeWViewDepthState CurrentViewDepthState() => new(_viewDepthPlan.Mode);

    private void ApplyViewDepthPlan(FreeWViewDepthPlan plan, bool updateStatus = true)
    {
        if (_outlineMode)
            LeaveOutlineView(restorePriorView: false);

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
                EnterEditableSideToSideView(plan);
                break;
        }

        _viewDepthPlan = plan;
        _editor.ApplyViewDepthLayout(plan.Layout);
        if (updateStatus)
            _status.Text = plan.IsSideToSideActive
                ? _sideToSideNavigation.StatusText
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

    private void EnterEditableSideToSideView(FreeWViewDepthPlan plan)
    {
        RestoreLiveWorkspace();
        if (_scroller is null)
            return;

        _scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _sideToSideUsesLiveEditor = true;
        _sideToSideNavigation = FreeWViewDepthPlanner.BuildPagePairNavigation(
            plan,
            requestedFirstVisiblePageNumber: 1,
            totalPages: Math.Max(1, _editor.PageCount));
        var (pageWidthDip, pageHeightDip) = PageLayout.PageSizeDip(_editor.Document.Page);
        var (viewportWidth, viewportHeight) = GetWorkspaceViewportSize(compact: false);
        var viewport = DocumentViewDepthLayoutPlanner.BuildViewportPlan(
            plan.Layout,
            viewportWidth,
            viewportHeight,
            pageWidthDip,
            pageHeightDip);
        _sideToSidePreviewScrollViewer = _scroller;
        _sideToSidePairScrollStrideDip = viewport.RequiredPageSpanWidthDip * viewport.Scale;
        _workspace.Child = null;
        _workspace.Child = BuildSideToSideNavigationHost(_scroller);
        ApplySideToSideNavigationToScrollViewer(plan);
    }

    private void RefreshSplitPreviewSnapshot()
    {
        if (!_viewDepthPlan.IsSplitActive || _splitPreviewGrid is null || _splitPreviewSnapshot is null)
            return;

        var replacement = BuildReadOnlyPagePreviewSurface(_viewDepthPlan, compact: true);
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
        snapshot.LoadDocument(CloneDocument(_editor.Document));
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
            _sideToSideNavigation = FreeWViewDepthPlanner.BuildPagePairNavigation(
                plan,
                requestedFirstVisiblePageNumber: 1,
                totalPages: snapshot.PageCount);
            _sideToSidePreviewScrollViewer = scroller;
            _sideToSidePairScrollStrideDip = viewport.RequiredPageSpanWidthDip * viewport.Scale;
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
            "Previous pair",
            () => NavigateSideToSidePagePair(FreeWViewDepthPagePairNavigationCommand.PreviousPair));
        _sideToSidePairStatusText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 12, 0)
        };
        _sideToSideNextPairButton = MakeSideToSideNavigationButton(
            "Next pair",
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

    private static Button MakeSideToSideNavigationButton(string text, Action action)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(10, 4),
            MinWidth = 96
        };
        ToolTip.SetTip(button, text);
        button.Click += (_, _) => action();
        return button;
    }

    private void NavigateSideToSidePagePair(FreeWViewDepthPagePairNavigationCommand command)
    {
        if (!_viewDepthPlan.IsSideToSideActive || _sideToSidePreviewScrollViewer is null)
            return;

        _sideToSideNavigation = FreeWViewDepthPlanner.NavigatePagePair(
            _viewDepthPlan,
            _sideToSideNavigation,
            command);
        ApplySideToSideNavigationToScrollViewer(_viewDepthPlan);
        SyncSideToSideNavigationControls();
        _status.Text = _sideToSideNavigation.StatusText;
    }

    private void ApplySideToSideNavigationToScrollViewer(FreeWViewDepthPlan plan)
    {
        if (!plan.IsSideToSideActive || _sideToSidePreviewScrollViewer is null)
            return;

        var pairIndex = (_sideToSideNavigation.FirstVisiblePageNumber - 1) /
            Math.Max(1, _sideToSideNavigation.PagesPerPair);
        var horizontalOffset = Math.Max(0, pairIndex * _sideToSidePairScrollStrideDip);
        _sideToSidePlannedHorizontalOffsetDip = horizontalOffset;
        _sideToSidePreviewScrollViewer.Offset = new Vector(horizontalOffset, 0);
    }

    private void SyncSideToSideNavigationControls()
    {
        if (_sideToSidePreviousPairButton is not null)
            _sideToSidePreviousPairButton.IsEnabled = _sideToSideNavigation.CanGoToPreviousPair;
        if (_sideToSideNextPairButton is not null)
            _sideToSideNextPairButton.IsEnabled = _sideToSideNavigation.CanGoToNextPair;
        if (_sideToSidePairStatusText is not null)
            _sideToSidePairStatusText.Text = _sideToSideNavigation.StatusText;
    }

    private void ResetSideToSideNavigation()
    {
        _sideToSideNavigation = FreeWViewDepthPlanner.BuildPagePairNavigation(
            FreeWViewDepthPlanner.Build(FreeWViewDepthMode.LiveEditor),
            requestedFirstVisiblePageNumber: 1,
            totalPages: 1);
        _sideToSidePreviewScrollViewer = null;
        _sideToSidePreviousPairButton = null;
        _sideToSideNextPairButton = null;
        _sideToSidePairStatusText = null;
        _sideToSidePairScrollStrideDip = 0;
        _sideToSidePlannedHorizontalOffsetDip = 0;
        _sideToSideUsesLiveEditor = false;
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

    private (double PageWidthFactor, double TextWidthFactor, double WholePageFactor) ComputeZoomFitFactors()
    {
        var page = _editor.Document.Page;
        var (pageWidthDip, pageHeightDip) = PageLayout.PageSizeDip(page);
        var (contentWidthDip, _) = PageLayout.ContentAreaDip(page);

        var viewportWidth = 0.0;
        var viewportHeight = 0.0;
        if (_scroller is not null)
        {
            viewportWidth = Math.Max(0, _scroller.Bounds.Width - _scroller.Padding.Left - _scroller.Padding.Right);
            viewportHeight = Math.Max(0, _scroller.Bounds.Height - _scroller.Padding.Top - _scroller.Padding.Bottom);
        }

        return (
            ZoomFit.PageWidth(pageWidthDip, viewportWidth),
            ZoomFit.TextWidth(contentWidthDip, viewportWidth),
            ZoomFit.WholePage(pageWidthDip, pageHeightDip, viewportWidth, viewportHeight));
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

    private DocumentOpenResult? LoadStartupDocument(IReadOnlyList<string> startupArguments)
    {
        var path = startupArguments.FirstOrDefault(a => File.Exists(a) && _documentPersistence.CanOpenPath(a));
        if (path is null)
            return null;
        try
        {
            return _documentPersistence.Open(path);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private Control BuildRibbon()
    {
        var callbacks = new RibbonHostCallbacks(
            Open: () => _ = OpenAsync(),
            Save: () => _ = SaveAsync(),
            ImportPdfText: () => _ = ImportPdfTextAsync(),
            Cut: () => _ = CutAsync(),
            Copy: () => _ = CopyAsync(),
            Paste: () => _ = PasteAsync(),
            PastePlainText: () => _ = PastePlainTextAsync(),
            PasteMergeFormatting: () => _ = PasteMergeFormattingAsync(),
            OpenPasteSpecial: () => _ = OpenPasteSpecialAsync(),
            OpenNewStyleDialog: () => _ = StyleDialog.ShowNewAndApplyAsync(this, _editor),
            OpenManageStylesDialog: () => _ = ManageStylesDialog.ShowAndApplyAsync(this, _editor),
            Backstage: () => _ = ShowBackstageAsync(),
            NewDocument: NewDocument,
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
            OpenSymbolPickerDialog: () => _ = OpenSymbolPickerAsync(),
            CaptureScreenClip: () => _ = InsertScreenClipAsync(),
            OpenTablePropertiesDialog: context => _ = OpenTablePropertiesDialogAsync(context),
            OpenTableFormulaDialog: state => _ = OpenTableFormulaDialogAsync(state),
            OpenWordCountDialog: () => _ = OpenWordCountDialogAsync(),
            OpenCaptionDialog: () => _ = OpenCaptionDialogAsync(),
            OpenCrossReferenceDialog: () => _ = OpenCrossReferenceDialogAsync(),
            OpenCitationDialog: () => _ = OpenCitationDialogAsync(),
            OpenManageSourcesDialog: () => _ = OpenManageSourcesDialogAsync(),
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
            IsSplitActive:   () => _viewDepthPlan.IsSplitActive,
            ZoomOnePage:     ZoomToOnePage,
            ZoomPageWidth:   ZoomToPageWidth,
            ToggleMultiplePages: ToggleMultiplePages,
            IsMultiplePagesActive: () => _viewDepthPlan.IsMultiplePagesActive,
            ToggleSideToSide: ToggleSideToSide,
            IsSideToSideActive: () => _viewDepthPlan.IsSideToSideActive,
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
            InsertTextFromFile:  () => _ = InsertTextFromFileAsync(),
            // AV-MAIL: surface mail-merge info messages in the status bar.
            ShowMailMergeInfo: msg => _status.Text = msg,
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
            OpenHelpOnline: () => _ = OpenExternalHelpLinkAsync(FreeWProductInfo.HelpUrl, "Help Online"),
            OpenFeedback: () => _ = OpenExternalHelpLinkAsync(FreeWProductInfo.FeedbackUrl, "Feedback"),
            CopyDiagnostics: () => _ = CopyDiagnosticsAsync(),
            CheckForUpdates: () => _ = OpenExternalHelpLinkAsync(FreeWProductInfo.LatestReleaseUrl, "Check for Updates"),
            OpenAbout: () => _ = OpenAboutAsync(),
            OpenLegalNotices: () => _ = OpenLegalNoticesAsync(),
            ToggleReadMode: ToggleReadMode,
            IsReadModeActive: () => _readMode,
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
            onFileTabSelected: () => _ = ShowBackstageAsync());
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
        var seed = fields.Count > 0 ? string.Join(",", fields) : string.Empty;
        var csv = await MailMergeDialogs.AskRecipientCsvAsync(this, seed);
        if (string.IsNullOrWhiteSpace(csv))
            return;
        var data = _mailMerge.LoadRecipientsCsv(csv);
        _status.Text = data.Count > 0
            ? $"Loaded {data.Count} recipient(s): {string.Join(", ", data.Header)}"
            : "Recipient list is empty.";
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
        {
            _editor.ApplyPageSettings(page =>
            {
                page.WidthPt = labels.PageWidthPt;
                page.HeightPt = labels.PageHeightPt;
                page.MarginLeftPt = labels.MarginPt;
                page.MarginRightPt = labels.MarginPt;
                page.MarginTopPt = labels.MarginPt;
                page.MarginBottomPt = labels.MarginPt;
                page.Landscape = labels.Landscape;
            });
        }
        _editor.Focus();
    }

    private async Task OpenMatchFieldsAsync()
    {
        if (_mailMerge?.Session.Data is not { } data)
        {
            await FreeWInfoDialog.ShowAsync(
                this,
                "Select recipients first (Mailings > Select Recipients), then match fields.");
            return;
        }
        var mapping = await MailMergeDialogs.AskMatchFieldsAsync(
            this, data.Header, _mailMerge.Session.Mapping ?? new FieldMapping());
        if (mapping is not null)
            _mailMerge.ApplyFieldMapping(mapping);
        _editor.Focus();
    }

    private async Task OpenFilterSortAsync()
    {
        if (_mailMerge?.Session.Data is not { Count: > 0 } data)
        {
            await FreeWInfoDialog.ShowAsync(
                this,
                "Select recipients first (Mailings > Select Recipients), then filter and sort.");
            return;
        }
        var filtered = await MailMergeDialogs.AskFilterSortRecipientsAsync(this, data);
        if (filtered is not null)
        {
            _mailMerge.Session.Data = filtered;
            _mailMerge.Session.Template = null;
            _mailMerge.Session.CurrentIndex = 0;
        }
        _editor.Focus();
    }

    private async Task OpenPreviewNavigationAsync()
    {
        if (_mailMerge is null)
            return;
        if (_mailMerge.Session.Data is not { Count: > 0 })
        {
            await FreeWInfoDialog.ShowAsync(
                this,
                "Select recipients first (Mailings > Select Recipients), then preview a record.");
            return;
        }
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
        if (_mailMerge?.Session.Data is not { Count: > 0 } data)
        {
            await FreeWInfoDialog.ShowAsync(
                this,
                "Select recipients first (Mailings > Select Recipients), then find a recipient.");
            _editor.Focus();
            return;
        }
        var query = await MailMergeDialogs.AskFindRecipientAsync(this);
        if (query is null)
        {
            _editor.Focus();
            return;
        }

        var result = MailMergeFindRecipientPlanner.Find(data, query, _mailMerge.Session.CurrentIndex);
        _mailMerge.Session.CurrentIndex = result.Index;
        await FreeWInfoDialog.ShowAsync(this, result.Message);
        _editor.Focus();
    }

    private async Task OpenCheckForErrorsAsync()
    {
        if (_mailMerge?.Session.Data is not { Count: > 0 })
        {
            await FreeWInfoDialog.ShowAsync(
                this,
                "Select recipients first (Mailings > Select Recipients), then check for errors.");
            return;
        }
        var mode = await MailMergeDialogs.AskCheckForErrorsAsync(this);
        if (mode is not { } selected)
            return;

        var result = _mailMerge.CheckForErrors(selected, completeMerge: false);
        if (result is not null)
        {
            if (result.ShouldPauseForErrors)
            {
                foreach (var issue in result.Issues)
                    await FreeWInfoDialog.ShowAsync(this, issue.Message);
            }
            else if (!result.ShouldOpenReportDocument)
            {
                await FreeWInfoDialog.ShowAsync(this, result.Message);
            }

            if (result.ShouldCompleteMerge)
                _mailMerge.FinishMerge();

            if (result.ShouldOpenReportDocument)
                OpenMailMergeErrorReport(MailMergeCheckForErrorsPlanner.BuildReportDocument(result));
        }
        _editor.Focus();
    }

    private async Task OpenFinishMergeAsync()
    {
        if (_mailMerge is null)
            return;
        if (_mailMerge.Session.Data is not { Count: > 0 } data)
        {
            await FreeWInfoDialog.ShowAsync(
                this,
                "Select recipients first (Mailings > Select Recipients), then Finish & Merge.");
            return;
        }
        var plan = await MailMergeDialogs.AskFinishMergeAsync(
            this, data.Count, _mailMerge.Session.CurrentIndex);
        if (plan is { Success: true, Destination: MailMergeFinishDestination.NewDocument })
            _mailMerge.FinishMerge(plan);
        _editor.Focus();
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

    private async Task PlanEmailMergeAsync()
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
            Array.Empty<int>());
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
        var white = ResolveThemeBrush("FreeWWhiteBrush", Brushes.White);
        _pageStatus = SisterAppStatusBarChrome.CreateInfoText(foreground: white);
        _sectionStatus = SisterAppStatusBarChrome.CreateInfoText(foreground: white);
        _status = SisterAppStatusBarChrome.CreateInfoText(foreground: white);
        _dataFolderStatus = SisterAppStatusBarChrome.CreateInfoText(foreground: white);
        _dataFolderStatus.Text = SisterAppStatusBarTextPlanner.FormatDataFolderStatus(ResolveDataFolderLabel());
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
            Background: ResolveThemeBrush(
                "FreeWStatusSurfaceBrush",
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
            "Read Mode",
            "Toggle distraction-free Read Mode",
            RibbonCommandIconKind.ReadMode,
            foreground,
            ToggleReadMode);
        _printLayoutSwitch = BuildStatusToggle(
            "Print Layout",
            "Print Layout page view",
            RibbonCommandIconKind.PrintLayout,
            foreground,
            () => SetViewMode(DocumentViewMode.PrintLayout));
        _webLayoutSwitch = BuildStatusToggle(
            "Web Layout",
            "Web Layout: continuous, full-width view",
            RibbonCommandIconKind.WebLayout,
            foreground,
            () => SetViewMode(DocumentViewMode.WebLayout));
        _draftSwitch = BuildStatusToggle(
            "Draft",
            "Draft: simplified continuous view for fast editing",
            RibbonCommandIconKind.Draft,
            foreground,
            () => SetViewMode(DocumentViewMode.Draft));
        _pagedEditSwitch = BuildStatusToggle(
            "Page Edit",
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

        if (_viewDepthPlan.Mode != FreeWViewDepthMode.LiveEditor)
            ApplyViewDepthPlan(FreeWViewDepthPlanner.Build(FreeWViewDepthMode.LiveEditor), updateStatus: false);

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
        if (_outlineMode)
            LeaveOutlineView(restorePriorView: false);

        if (_viewDepthPlan.IsMultiplePagesActive || _viewDepthPlan.IsSideToSideActive)
            ApplyViewDepthPlan(FreeWViewDepthPlanner.Build(FreeWViewDepthMode.LiveEditor), updateStatus: false);

        _pagedEditMode = false;
        _editor.ViewMode = mode;
        if (_viewDepthPlan.IsSplitActive)
            RefreshSplitPreviewSnapshot();
        UpdateViewModeButtons();
        RefreshRibbonCommandStates();
        _editor.Focus();
    }

    private void UpdateViewModeButtons()
    {
        var mode = _editor.ViewMode;
        ApplyStatusToggleState(_printLayoutSwitch, !_outlineMode && !_pagedEditMode && mode == DocumentViewMode.PrintLayout);
        ApplyStatusToggleState(_webLayoutSwitch, !_outlineMode && !_pagedEditMode && mode == DocumentViewMode.WebLayout);
        ApplyStatusToggleState(_draftSwitch, !_outlineMode && !_pagedEditMode && mode == DocumentViewMode.Draft);
        ApplyStatusToggleState(_pagedEditSwitch, _pagedEditMode);
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
            if (_viewDepthPlan.Mode != FreeWViewDepthMode.LiveEditor)
                ApplyViewDepthPlan(FreeWViewDepthPlanner.Build(FreeWViewDepthMode.LiveEditor), updateStatus: false);
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
        _readMode = !_readMode;
        if (_readMode)
        {
            _titleBarVisibleBeforeReadMode = _titleBar.IsVisible;
            _ribbonVisibleBeforeReadMode = _ribbonHost?.IsVisible == true;
            _dataFolderVisibleBeforeReadMode = _dataFolderItemControl?.IsVisible == true;
            _statusViewSwitchVisibleBeforeReadMode = _statusViewSwitchControl?.IsVisible == true;
            _statusZoomVisibleBeforeReadMode = _statusZoomControl?.IsVisible == true;
            _navPaneVisibleBeforeReadMode = _navPane.IsVisible;
            _reviewingPaneVisibleBeforeReadMode = _reviewingPane.IsVisible;
            _revealPaneVisibleBeforeReadMode = _revealPane.IsVisible;
            _workspaceBackgroundBeforeReadMode = _workspace.Background ?? Brushes.Transparent;

            _editorMaxWidthBeforeReadMode = _editor.MaxWidth;
            _editorAlignmentBeforeReadMode = _editor.HorizontalAlignment;
            _editorMarginBeforeReadMode = _editor.Margin;
            _titleBar.IsVisible = false;
            if (_ribbonHost is not null)
                _ribbonHost.IsVisible = false;
            if (_dataFolderItemControl is not null)
                _dataFolderItemControl.IsVisible = false;
            if (_statusViewSwitchControl is not null)
                _statusViewSwitchControl.IsVisible = false;
            if (_statusZoomControl is not null)
                _statusZoomControl.IsVisible = false;

            _navPane.IsVisible = false;
            _reviewingPane.IsVisible = false;
            _revealPane.IsVisible = false;

            _editor.MaxWidth = FreeWReadModePlanner.ColumnWidth(_readModeColumnWidth);
            _editor.HorizontalAlignment = HorizontalAlignment.Center;
            _editor.Margin = new Thickness(40);
            var backgroundHex = FreeWReadModePlanner.PageColorHex(_readModePageColor);
            _editor.ViewBackgroundColorHex = backgroundHex;
            _workspace.Background = new SolidColorBrush(ParseColor(backgroundHex));
        }
        else
        {
            _titleBar.IsVisible = _titleBarVisibleBeforeReadMode;
            if (_ribbonHost is not null)
                _ribbonHost.IsVisible = _ribbonVisibleBeforeReadMode;
            if (_dataFolderItemControl is not null)
                _dataFolderItemControl.IsVisible = _dataFolderVisibleBeforeReadMode;
            if (_statusViewSwitchControl is not null)
                _statusViewSwitchControl.IsVisible = _statusViewSwitchVisibleBeforeReadMode;
            if (_statusZoomControl is not null)
                _statusZoomControl.IsVisible = _statusZoomVisibleBeforeReadMode;

            _navPane.IsVisible = _navPaneVisibleBeforeReadMode;
            _reviewingPane.IsVisible = _reviewingPaneVisibleBeforeReadMode;
            _revealPane.IsVisible = _revealPaneVisibleBeforeReadMode;

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

    private void ApplyReadModeColumnWidth(string token)
    {
        _readModeColumnWidth = FreeWReadModePlanner.NormalizeColumnWidth(token);
        if (_readMode)
            _editor.MaxWidth = FreeWReadModePlanner.ColumnWidth(_readModeColumnWidth);
    }

    private void ApplyReadModePageColor(string token)
    {
        _readModePageColor = FreeWReadModePlanner.NormalizePageColor(token);
        if (_readMode)
        {
            var backgroundHex = FreeWReadModePlanner.PageColorHex(_readModePageColor);
            _editor.ViewBackgroundColorHex = backgroundHex;
            _workspace.Background = new SolidColorBrush(ParseColor(backgroundHex));
        }
    }

    private static Color ParseColor(string hex) =>
        Color.Parse(hex);

    internal bool IsReadModeActiveForTests => _readMode;
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
            FreeWKeyboardShortcutCatalog.TryDispatch(
                key,
                ToKeyboardModifiers(e.KeyModifiers),
                ExecuteKeyboardCommand))
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

        var token = ToRibbonKeyTipToken(args.Key);
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

    private static string? ToRibbonKeyTipToken(Key key)
    {
        var name = key.ToString();
        if (name.Length == 1 && char.IsAsciiLetterOrDigit(name[0]))
            return name.ToUpperInvariant();
        if (name.Length == 2 && name[0] == 'D' && char.IsAsciiDigit(name[1]))
            return name[1].ToString();
        return null;
    }

    private void ExecuteKeyboardCommand(FreeWKeyboardCommand command)
    {
        switch (command)
        {
            case FreeWKeyboardCommand.NewDocument: NewDocument(); break;
            case FreeWKeyboardCommand.OpenDocument: _ = OpenAsync(); break;
            case FreeWKeyboardCommand.SaveDocument: _ = SaveAsync(); break;
            case FreeWKeyboardCommand.SaveDocumentAs: _ = SaveAsAsync(); break;
            case FreeWKeyboardCommand.PrintDocument: _ = PrintAsync(); break;
            case FreeWKeyboardCommand.Find: OpenFindReplaceDialog(FindReplaceDialogOpenMode.Find); break;
            case FreeWKeyboardCommand.Replace: OpenFindReplaceDialog(FindReplaceDialogOpenMode.Replace); break;
            case FreeWKeyboardCommand.Cut: _ = CutAsync(); break;
            case FreeWKeyboardCommand.Copy: _ = CopyAsync(); break;
            case FreeWKeyboardCommand.Paste: _ = PasteAsync(); break;
            case FreeWKeyboardCommand.PasteTextOnly: _ = PastePlainTextAsync(); break;
            case FreeWKeyboardCommand.SelectAll: _editor.SelectAll(); break;
            case FreeWKeyboardCommand.Undo: _editor.Undo(); break;
            case FreeWKeyboardCommand.Redo: _editor.Redo(); break;
            case FreeWKeyboardCommand.RevealFormatting: ToggleRevealFormatting(); break;
            case FreeWKeyboardCommand.Thesaurus: ToggleThesaurusPane(); break;
            case FreeWKeyboardCommand.ToggleFieldCodes: _editor.ToggleFieldCodes(); break;
            case FreeWKeyboardCommand.UpdateFields: _editor.UpdateFields(); break;
            default: throw new ArgumentOutOfRangeException(nameof(command), command, null);
        }
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
            _ => default,
        };
        return key is Key.A or Key.C or Key.F or Key.H or Key.N or Key.O or Key.P or Key.S
            or Key.V or Key.X or Key.Y or Key.Z or Key.F1 or Key.F7 or Key.F9;
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
        _zoomLabel.Text = $"{ZoomLevels.ToPercent(_zoomScale)}%";

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
        _scroller.Offset = new Vector(_scroller.Offset.X, target);
    }

    private async Task CopyAsync()
    {
        var text = _editor.SelectedText;
        if (text.Length == 0)
            return;
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }

    private async void OnEditorContextMenuCommandRequested(RibbonCommandId commandId)
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
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            return;
        var text = await clipboard.TryGetTextAsync();
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
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            return null;
        return await clipboard.TryGetTextAsync();
    }

    private async Task<TextDocument?> TryGetClipboardRtfDocumentAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            return null;

        try
        {
            using var data = await clipboard.TryGetDataAsync();
            var rtf = data is null
                ? null
                : await data.TryGetValueAsync(DataFormat.CreateStringPlatformFormat("Rich Text Format"));
            return DocumentView.TryReadRtfClipboardDocument(rtf, out var document) ? document : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return null;
        }
    }

    private async Task OpenAsync()
    {
        await _fileWorkflow.OpenAsync(
            FileText.OpenAction,
            PromptOpenPathAsync,
            OpenPathAsync);
    }

    private async Task<string?> PromptOpenPathAsync()
    {
        using var file = await AvaloniaFilePickerService.PickSingleOpenFileWithLocalPathAsync(
            StorageProvider,
            AvaloniaFilePickerOpenRequest.FromFileTypes(
                FileText.OpenPickerTitle,
                DocumentFilePickerTypes.BuildOpenTypes(_documentPersistence.Adapters)));
        return file?.LocalPath;
    }

    private Task<bool> OpenPathAsync(string path)
    {
        if (!_documentPersistence.CanOpenPath(path))
        {
            _status.Text = SisterAppFileTextPlanner.FormatUnsupportedFileType(
                SisterAppFileTextPlanner.OpenCommand,
                Path.GetExtension(path));
            return Task.FromResult(false);
        }

        try
        {
            ApplyOpenResult(_documentPersistence.Open(path));

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _status.Text = SisterAppFileTextPlanner.FormatCommandFailed(SisterAppFileTextPlanner.OpenCommand, ex.Message);
            return Task.FromResult(false);
        }
    }

    private async Task ImportPdfTextAsync()
    {
        using var file = await AvaloniaFilePickerService.PickSingleOpenFileWithLocalPathAsync(
            StorageProvider,
            AvaloniaFilePickerOpenRequest.FromFileTypes(
                "Import PDF (text only)",
                DocumentFilePickerTypes.BuildPdfImportTypes()));
        var path = file?.LocalPath;
        if (path is null)
            return;

        if (DocumentFileFormatResolver.FindOpenAdapter(
                DocumentFileAdapterCatalog.CreatePdfImportAdapters(),
                Path.GetExtension(path),
                out _) is not { } adapter)
        {
            _status.Text = $"PDF import failed: unsupported file type \"{Path.GetExtension(path)}\".";
            return;
        }

        try
        {
            using var stream = File.OpenRead(path);
            LoadDocumentContent(adapter.Load(stream));
            _fileWorkflow.MarkDirtyWithPath(null);
            _status.Text = $"Imported PDF text from {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            _status.Text = $"PDF import failed: {ex.Message}";
        }
    }

    private Task<bool> SaveAsync() =>
        _fileWorkflow.SaveAsync(SaveToCurrentPathAsync, SaveAsAsync);

    internal Task<bool> SaveForTests() => SaveAsync();

    private Task<bool> SaveToCurrentPathAsync(string path) =>
        _documentPersistence.TryResolveCurrentSaveTarget(path, out var target)
            ? SaveToTargetAsync(target)
            : SaveAsAsync();

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
    {
        if (!_documentPersistence.TryResolveSaveTarget(path, filterIndex, out var target))
        {
            _status.Text = SisterAppFileTextPlanner.FormatUnsupportedFileType(
                SisterAppFileTextPlanner.SaveCommand,
                Path.GetExtension(path));
            return Task.FromResult(false);
        }

        return SaveToTargetAsync(target);
    }

    private async Task<bool> SaveToTargetAsync(DocumentSaveTarget target)
    {
        try
        {
            if (!await ConfirmSaveCompatibilityAsync(target))
            {
                _status.Text = "Save canceled.";
                return false;
            }

            _documentPersistence.Save(_editor.Document, target);
            MarkDocumentSavedWithPath(target.Path);
            _status.Text = SisterAppFileTextPlanner.FormatSaved(Path.GetFileName(target.Path));
            return true;
        }
        catch (Exception ex)
        {
            _status.Text = SisterAppFileTextPlanner.FormatCommandFailed(SisterAppFileTextPlanner.SaveCommand, ex.Message);
            return false;
        }
    }

    private async Task<bool> ConfirmSaveCompatibilityAsync(DocumentSaveTarget target)
    {
        var plan = _documentPersistence.BuildSaveCompatibilityPlan(_editor.Document, target);
        return !plan.RequiresConfirmation || await SaveCompatibilityWarningDialog.ShowAsync(this, plan);
    }

    /// <summary>
    /// File → Export to PDF (Ctrl+Shift+P). Builds the shared app-agnostic PDF model from the editor
    /// layout and writes a real PDF via <see cref="FreeWAvaloniaPdfExport"/> (Skia when available,
    /// dependency-free WinAnsi fallback otherwise). Mirrors the FreeX Avalonia shell's File → Export
    /// to PDF, on the shared PDF tier.
    /// </summary>
    private async Task ExportPdfAsync()
    {
        using var file = await AvaloniaFilePickerService.PickSaveFileWithLocalPathAsync(
            StorageProvider,
            AvaloniaFilePickerSaveRequest.FromFileTypes(
                FreeWFileTextResources.ExportPdfPickerTitle,
                [PdfFileType],
                _fileWorkflow.CurrentFileNameWithoutExtensionOr(FileText.FallbackDisplayName) + ".pdf",
                "pdf"));
        var path = file?.LocalPath;
        if (path is null)
            return;

        try
        {
            var result = FreeWAvaloniaPdfExport.Save(_editor, path);
            _status.Text = FreeWFileTextResources.FormatPdfExported(result.PageCount, result.Backend, Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            _status.Text = SisterAppFileTextPlanner.FormatCommandFailed(FreeWFileTextResources.PdfExportCommand, ex.Message);
        }
    }

    internal async Task PrintAsync()
    {
        var priorFocus = FocusManager?.GetFocusedElement();
        using var cancellation = new CancellationTokenSource();
        _printCancellation = cancellation;
        try
        {
            var discovery = await _printService.DiscoverAsync(cancellation.Token);
            _latestPrinterDiscovery = discovery;
            if (discovery.Status == PrinterDiscoveryStatus.Cancelled)
                throw new OperationCanceledException(cancellation.Token);
            if (!discovery.IsAvailable)
            {
                _status.Text = FormatPrintDiscoveryStatus(discovery);
                return;
            }

            var selection = await _showPrintSelectionDialog(this, discovery, cancellation.Token);
            if (selection is null)
            {
                _status.Text = "Print canceled.";
                return;
            }

            var tempPath = Path.Combine(Path.GetTempPath(), $"FreeW-print-{Guid.NewGuid():N}.pdf");
            try
            {
                FreeWAvaloniaPdfExport.Save(_editor, tempPath);
                var submission = await _printService.SubmitAsync(tempPath, selection, cancellation.Token);
                _status.Text = FormatPrintSubmissionStatus(submission);
            }
            finally
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Print canceled.";
        }
        catch (Exception ex)
        {
            _status.Text = SisterAppFileTextPlanner.FormatCommandFailed("Print", ex.Message);
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
        var selection = await _pickExportPath(
            StorageProvider,
            AvaloniaFilePickerSaveRequest.FromFileTypes(
                FreeWFileTextResources.ExportXpsPickerTitle,
                [XpsFileType],
                _fileWorkflow.CurrentFileNameWithoutExtensionOr(FileText.FallbackDisplayName) + ".xps",
                "xps",
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

        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var temporaryPath = ExportAtomicWriter.CreateTempPath(path);
            try
            {
                using (var stream = File.Create(temporaryPath))
                    FreeWAvaloniaXpsExport.Save(_editor, stream);
                ExportAtomicWriter.ReplaceTarget(temporaryPath, path);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    try { File.Delete(temporaryPath); } catch { }
                }
            }

            _status.Text = FreeWFileTextResources.FormatXpsExported(path);
        }
        catch (Exception ex)
        {
            _status.Text = SisterAppFileTextPlanner.FormatCommandFailed(
                FreeWFileTextResources.XpsExportCommand,
                ex.Message);
        }
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
        try
        {
            _latestPrinterDiscovery = await _printService.DiscoverAsync();
        }
        catch (Exception ex)
        {
            _latestPrinterDiscovery = new PrinterDiscoveryResult(
                PrinterDiscoveryStatus.Failed,
                [],
                null,
                $"Printer discovery failed: {ex.Message}");
        }
    }

    private static string FormatPrintDiscoveryStatus(PrinterDiscoveryResult discovery) =>
        discovery.Status switch
        {
            PrinterDiscoveryStatus.NoPrinters =>
                "No printers are installed or available. Use Print Preview or Create PDF.",
            PrinterDiscoveryStatus.Unavailable =>
                string.IsNullOrWhiteSpace(discovery.Message)
                    ? "Direct printing is unavailable on this host. Use Print Preview or Create PDF."
                    : $"{discovery.Message} Use Print Preview or Create PDF.",
            PrinterDiscoveryStatus.Failed =>
                string.IsNullOrWhiteSpace(discovery.Message)
                    ? "Printer discovery failed. Use Print Preview or Create PDF."
                    : $"{discovery.Message} Use Print Preview or Create PDF.",
            PrinterDiscoveryStatus.Cancelled => "Print canceled.",
            _ => "Direct printing is unavailable. Use Print Preview or Create PDF.",
        };

    private static string FormatPrintSubmissionStatus(PrintSubmissionResult submission) =>
        submission.Status switch
        {
            PrintSubmissionStatus.Submitted => $"Sent to printer {submission.PrinterName}.",
            PrintSubmissionStatus.Cancelled => "Print canceled.",
            PrintSubmissionStatus.NoPrinters =>
                "No printers are installed or available. Use Print Preview or Create PDF.",
            PrintSubmissionStatus.Unavailable =>
                string.IsNullOrWhiteSpace(submission.Message)
                    ? "Direct printing is unavailable on this host. Use Print Preview or Create PDF."
                    : $"{submission.Message} Use Print Preview or Create PDF.",
            _ => submission.Message ?? "Print submission failed. Use Print Preview or Create PDF.",
        };

    private BackstageDirectPrintCapability DirectPrintCapability =>
        _latestPrinterDiscovery?.IsAvailable == true
            ? BackstageDirectPrintCapability.PlatformPrinterAvailable(
                "CUPS printer discovery and foreground submission are available on this Avalonia host; no native system print dialog is used.")
            : BackstageDirectPrintCapability.Deferred(
                DirectPrintDeferredReason());

    private string DirectPrintDeferredReason()
    {
        if (!_printService.IsSupported)
            return "This Avalonia host has no supported native printer service; use Print Preview or Create PDF.";

        return _latestPrinterDiscovery?.Status switch
        {
            PrinterDiscoveryStatus.NoPrinters =>
                "No usable CUPS printer was discovered on this Avalonia host; use Print Preview or Create PDF.",
            PrinterDiscoveryStatus.Unavailable =>
                "The CUPS printer backend is unavailable on this Avalonia host; use Print Preview or Create PDF.",
            PrinterDiscoveryStatus.Failed =>
                "CUPS printer discovery failed on this Avalonia host; use Print Preview or Create PDF.",
            PrinterDiscoveryStatus.Cancelled =>
                "CUPS printer discovery was canceled; use Print Preview or Create PDF.",
            _ => "CUPS printer discovery is still in progress; use Print Preview or Create PDF until a printer is available.",
        };
    }

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

    private static readonly FilePickerFileType ImageFileType =
        AvaloniaFilePickerTypeAdapter.CreateFileType(
            FreeWFileTextResources.PictureFileTypeName,
            ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.tif", "*.tiff"],
            ["image/png", "image/jpeg", "image/gif", "image/bmp", "image/tiff"]);

    /// <summary>
    /// Insert &gt; Picture (AV-INSERT): open a file picker, read the chosen image, and insert it at the
    /// caret as an inline image. The display size is derived from the image's natural pixel dimensions
    /// (96 DPI → points), capped so a large photo does not overflow the page; the bytes are stored verbatim.
    /// </summary>
    private async Task InsertPictureAsync()
    {
        using var file = await AvaloniaFilePickerService.PickSingleOpenFileWithLocalPathAsync(
            StorageProvider,
            AvaloniaFilePickerOpenRequest.FromFileTypes(
                SisterAppFileTextPlanner.InsertPicturePickerTitle,
                [ImageFileType]));
        var path = file?.LocalPath;
        if (path is null)
            return;

        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            var (widthPt, heightPt) = MeasureImagePoints(bytes);
            _editor.InsertInlineImage(bytes, widthPt, heightPt);
            _editor.Focus();
        }
        catch (Exception ex)
        {
            _status.Text = SisterAppFileTextPlanner.FormatCommandFailed(SisterAppFileTextPlanner.InsertPictureCommand, ex.Message);
        }
    }

    /// <summary>
    /// Decode <paramref name="bytes"/> to recover the natural pixel size, convert to points at 96 DPI, and
    /// cap the longest edge so the image fits a typical page body. Falls back to a sensible default size
    /// when the bytes cannot be decoded (e.g. EMF/WMF, which Avalonia's Bitmap cannot read).
    /// </summary>
    private static (double WidthPt, double HeightPt) MeasureImagePoints(byte[] bytes)
    {
        const double maxEdgePt = 360.0; // ~5 inches — fits the body of a Letter/A4 page with 1in margins
        try
        {
            using var ms = new MemoryStream(bytes);
            var bitmap = new Bitmap(ms);
            var widthPt = bitmap.PixelSize.Width * 72.0 / 96.0;
            var heightPt = bitmap.PixelSize.Height * 72.0 / 96.0;
            if (widthPt <= 0 || heightPt <= 0)
                return (200, 150);
            var longest = Math.Max(widthPt, heightPt);
            if (longest > maxEdgePt)
            {
                var scale = maxEdgePt / longest;
                widthPt *= scale;
                heightPt *= scale;
            }
            return (widthPt, heightPt);
        }
        catch
        {
            return (200, 150); // undecodable (metafile) → default box; bytes still round-trip verbatim
        }
    }

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

        var display = ScreenClipPlanner.BuildDisplaySize(
            capture.PixelWidth,
            capture.PixelHeight);
        editor.InsertInlineImage(
            capture.PngBytes,
            display.WidthPt,
            display.HeightPt,
            ImageFormat.Png,
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
        if (action is { Kind: BuildingBlockActionKind.Insert }
            && _quickParts.Get(action.Name) is { } part)
            _editor.InsertQuickPartText(part.Text);
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

    /// <summary>
    /// AV-INSERT2: Insert Text from File — opens a file picker for a .docx/.txt, loads it (reusing the open
    /// adapters for .docx; a plain reader for .txt), and inserts the document's plain text at the caret as a
    /// Quick-Part-style multi-paragraph insert. Wired to <c>freew.text-from-file</c> (Insert → Text).
    /// </summary>
    private async Task InsertTextFromFileAsync()
    {
        using var file = await AvaloniaFilePickerService.PickSingleOpenFileWithLocalPathAsync(
            StorageProvider,
            AvaloniaFilePickerOpenRequest.FromFileTypes(
                InsertDialogTextResources.TextFromFilePickerTitle,
                [TextFromFileType]));
        var path = file?.LocalPath;
        if (path is null)
            return;

        try
        {
            string text;
            var ext = Path.GetExtension(path);
            if (string.Equals(ext, ".txt", StringComparison.OrdinalIgnoreCase))
            {
                text = await File.ReadAllTextAsync(path);
            }
            else
            {
                var adapter = DocumentFileFormatResolver.FindOpenAdapter(_documentPersistence.Adapters, ext, out _);
                if (adapter is null)
                {
                    _status.Text = SisterAppFileTextPlanner.FormatUnsupportedFileType("Insert text", ext);
                    return;
                }
                using var stream = File.OpenRead(path);
                var document = adapter.Load(stream);
                text = document.PlainText;
            }

            _editor.InsertQuickPartText(text);
            _editor.Focus();
        }
        catch (Exception ex)
        {
            _status.Text = SisterAppFileTextPlanner.FormatCommandFailed("Insert text", ex.Message);
        }
    }

    private static readonly FilePickerFileType TextFromFileType =
        AvaloniaFilePickerTypeAdapter.CreateFileType(
            FreeWFileTextResources.TextFromFileTypeName,
            ["*.docx", "*.txt"],
            ["application/vnd.openxmlformats-officedocument.wordprocessingml.document", "text/plain"]);

    private void ApplyOpenResult(DocumentOpenResult result) =>
        LoadDocumentAsSaved(result.Document, result.SavedPath);

    private void LoadDocumentAsSaved(TextDocument document, string? path)
    {
        LoadDocumentContent(document);

        if (path is null)
        {
            _fileWorkflow.MarkSavedWithoutPath();
        }
        else
        {
            MarkDocumentSavedWithPath(path);
        }

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
        ApplyViewDepthPlan(FreeWViewDepthPlanner.Build(FreeWViewDepthMode.LiveEditor), updateStatus: false);
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
        var controller = EnsureReadAloudController();
        if (controller.IsActive)
        {
            controller.Stop();
        }
        else
        {
            controller.Start(_editor.Document, _editor.ReadAloudStartSegmentIndex());
        }

        RefreshRibbonCommandStates();
    }

    private bool IsReadAloudActive() => _readAloudController?.IsActive == true;

    private ReadAloudController EnsureReadAloudController()
    {
        if (_readAloudController is not null)
            return _readAloudController;

        _readAloudEngine = new AvaloniaSpeechEngine();
        _readAloudController = new ReadAloudController(_readAloudEngine);
        _readAloudController.StateChanged += OnReadAloudStateChanged;
        return _readAloudController;
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
            RibbonVisualPalette.FromTheme(App.ActiveTheme));
    }

    private void StopReadAloudAfterDocumentChange()
    {
        if (_readAloudController?.IsActive == true)
            StopReadAloud();
    }

    private void StopReadAloud()
    {
        _readAloudController?.Stop();
        RefreshRibbonCommandStates();
    }

    private void DisposeReadAloud()
    {
        var controller = _readAloudController;
        _readAloudController = null;
        if (controller is not null)
        {
            controller.StateChanged -= OnReadAloudStateChanged;
            controller.Stop();
        }

        _readAloudEngine?.Dispose();
        _readAloudEngine = null;
    }

    private void OnEditorDocumentChanged()
    {
        if (!_suppressEditorDirty)
            _fileWorkflow.MarkDirty();

        RefreshSplitPreviewSnapshot();
        UpdateStatus();
    }

    private void MarkDocumentSavedWithPath(string path)
    {
        _fileWorkflow.MarkSavedWithPath(path, suppressRecentFiles: false);
    }

    private void UpdateStatus()
    {
        var stats = _editor.ComputeStatistics();
        var (currentSection, totalSections) = _editor.SectionInfo();
        var plan = FreeWEditorStatusPlanner.Build(new FreeWEditorStatusSnapshot(
            stats.Words,
            stats.CharactersWithSpaces,
            stats.Paragraphs,
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
            GetDataFolder: ResolveDataFolderLabel,
            GetDocument: () => _editor.Document,
            GetIsDirty: () => _fileWorkflow.IsDirty,

            NewDocument: NewDocument,
            OpenRecent: path =>
            {
                // Run the dirty-gate synchronously through the shared Avalonia workflow.
                if (_fileWorkflow.Open(FileText.OpenAction, () => path, p =>
                    {
                        _ = OpenPathAsync(p);
                        return true;
                    }))
                {
                    // success — OpenPathAsync was already fired
                }
            },
            OpenFolder: OpenFolderInShell,
            Browse: () => _ = OpenAsync(),
            RecoverUnsaved: () => _ = _autosave.OfferRecoveryAsync(this),
            ImportPdfText: () => _ = ImportPdfTextAsync(),
            Save: () => _ = SaveAsync(),
            SaveAs: () => _ = SaveAsAsync(),
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
            Print: DirectPrintCapability.IsAvailable ? () => _ = PrintAsync() : null,
            PrintPreview: () => _ = OpenPrintPreviewAsync());

    private async Task SaveCopyAsync()
    {
        var savePlan = _documentPersistence.BuildSavePickerPlan(
            _fileWorkflow.CurrentPath,
            _fileWorkflow.CurrentFileName,
            FileText.FallbackDisplayName);
        using var file = await AvaloniaFilePickerService.PickSaveFileWithLocalPathAsync(
            StorageProvider,
            AvaloniaFilePickerSaveRequest.FromSavePlan("Save a Copy", savePlan));
        var path = file?.LocalPath;
        if (path is null)
            return;

        await SaveCopyToPathAsync(path);
    }

    internal Task<bool> SaveCopyToPathAsync(string path, int filterIndex = 0)
    {
        if (!_documentPersistence.TryResolveSaveTarget(path, filterIndex, out var target))
        {
            _status.Text = SisterAppFileTextPlanner.FormatUnsupportedFileType(
                SisterAppFileTextPlanner.SaveCommand,
                Path.GetExtension(path));
            return Task.FromResult(false);
        }

        return SaveCopyToTargetAsync(target);
    }

    private async Task<bool> SaveCopyToTargetAsync(DocumentSaveTarget target)
    {
        try
        {
            if (!await ConfirmSaveCompatibilityAsync(target))
            {
                _status.Text = "Save a Copy canceled.";
                return false;
            }

            _documentPersistence.Save(_editor.Document, target);
            _status.Text = SisterAppFileTextPlanner.FormatSaved(Path.GetFileName(target.Path)) + " (copy)";
            return true;
        }
        catch (Exception ex)
        {
            _status.Text = $"Could not save a copy: {ex.Message}";
            return false;
        }
    }

    private async Task OpenPropertiesAsync()
    {
        var dialog = new PropertiesDialog(_editor.Document.Properties);
        await dialog.ShowDialog(this);
        if (!dialog.Accepted)
            return;

        _fileWorkflow.MarkDirty();
        _status.Text = "Document properties updated.";
        _editor.Focus();
    }

    private static TextDocument CloneDocument(TextDocument document)
    {
        using var buffer = new MemoryStream();
        DocxWriter.Write(document, buffer);
        buffer.Position = 0;
        return DocxReader.Read(buffer);
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
            _editor.ApplyInspectorRemovals(choice.Comments, choice.Revisions, choice.Properties, choice.Bookmarks);

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

        ApplyOptions(edited);
        if (!_optionsStore.Save(_options))
            _status.Text = _optionsStore.LastError ?? "FreeW Options could not be saved.";
        else
            _status.Text = "FreeW Options saved.";
    }

    private void ApplyOptions(FreeWOptions edited)
    {
        _options.RecentFilesCap = edited.RecentFilesCap;
        _options.DefaultSaveFormat = edited.DefaultSaveFormat;
        _options.UiLanguage = edited.UiLanguage;
        _options.AutoCorrectEnabled = edited.AutoCorrectEnabled;
        _options.AutoFormat = edited.AutoFormat;
        _options.AutoCorrect = edited.AutoCorrect;
        _options.Normalize();
        _editor.AutoCorrectEnabled = _options.AutoCorrectEnabled;
        _editor.AutoFormatOptions = _options.AutoFormat ?? AutoFormatOptions.Default;
        _editor.AutoCorrectOptions = _options.AutoCorrect ?? AutoCorrectOptions.Default;
    }

    private string ResolveDataFolderLabel()
    {
        try
        {
            return Path.GetDirectoryName(_optionsStore.StorePath) ?? _optionsStore.StorePath;
        }
        catch
        {
            return AppStoragePathPlanner.GetOptionsFilePathLabelOrFallback(PlatformApplicationDataPathProvider.LocalInstance);
        }
    }

    private void OpenFolderInShell(string folder)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true,
                });
        }
        catch (Exception ex)
        {
            _status.Text = $"Could not open folder: {ex.Message}";
        }
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
            _status.Text = SisterAppFileTextPlanner.FormatUnsupportedExtension(extension);
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
                SisterAppFileTextPlanner.FormatSaveAsTitle(format?.FormatName ?? extension),
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
    private static void OpenExternalUri(string url) =>
        ExternalUriLauncher.Open(
            url,
            uri => System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }));
}
