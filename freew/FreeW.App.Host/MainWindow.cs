using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Free.Shared.AppServices;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Wpf;
using Free.Shared.Shell.Wpf;
using Free.Shared.Theme;
using Free.Shared.Theme.Wpf;
using FreeW.App.Host.Backstage;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation;
using FreeW.App.Presentation.Backstage;
using FreeW.App.Presentation.ContextMenus;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Documents;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Options;
using FreeW.App.Presentation.Panes;
using FreeW.App.Presentation.Ribbon;
using FreeW.Ribbon.Definitions;
using FreeW.App.Presentation.Shell;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// FreeW main window. Deliberately code-only and minimal: it exists to prove the shared tier is
/// consumable by a second app. The ribbon is built from the shared <see cref="RibbonDefinition"/>
/// model and rendered by a small local renderer; the status bar shows that the shared storage
/// helpers resolve FreeW's own data folder (because Program.Main set AppProduct = "FreeW").
/// </summary>
public sealed partial class MainWindow : Window
{
    private FileCommands _file = null!;
    private AutosaveCoordinator _autosave = null!;

    /// <summary>Exposed for Program.cs's crash handler to attempt a best-effort emergency snapshot.</summary>
    internal AutosaveCoordinator? AutosaveCoordinatorForCrashHandler => _autosave;
    private DocumentView _editor = null!;
    private DocumentView? _lastFocusedDocumentEditor;
    private Ruler _hRuler = null!;
    private Ruler _vRuler = null!;
    private Button _rulerTabSelector = null!;
    private bool _rulersVisible = true;
    private SisterWpfWindowTitleBinder _titleBinder = null!;
    private TextBlock _pageText = null!;
    private TextBlock _sectionText = null!;
    private TextBlock _countsText = null!;
    private Slider _zoomSlider = null!;
    private TextBlock _zoomLabel = null!;
    private FindReplaceDialog? _findDialog;
    private RibbonStateStore _stateStore = null!;
    private BackstageView _backstage = null!;
    private Border _navPane = null!;
    private ListBox _navList = null!;
    private bool _navPaneVisible;
    private NavigationPaneSession _navigationPaneSession = null!;

    // Reveal Formatting pane (Word's Shift+F1): a read-only side pane, docked on the right (Word's side),
    // that mirrors the effective FONT / PARAGRAPH / SECTION formatting of the current selection. It updates
    // on SelectionChanged from the pure FreeW.Core.Model.RevealFormatting describer, so the pane never
    // touches the model and cannot interfere with editing. Mirrors the navigation pane's dock/toggle shape.
    private Border _revealPane = null!;
    private StackPanel _revealContent = null!;
    private bool _revealPaneVisible;

    // Reviewing Pane (Word's Review > Reviewing Pane): a dockable list of every tracked change in the
    // document — author, date, type, and the affected text — with click-to-navigate (jumps the editor to
    // the change) plus Accept / Reject of the SELECTED single revision and Previous/Next navigation. It is
    // rebuilt from the pure FreeW.Core.Model.RevisionList whenever the document changes or the pane opens,
    // so the surface never owns revision logic. Mirrors the navigation/reveal panes' dock + toggle shape.
    private Border _reviewPane = null!;
    private ListBox _reviewList = null!;
    private TextBlock _reviewStatus = null!;
    private Button _reviewAcceptButton = null!;
    private Button _reviewRejectButton = null!;
    private Button _reviewPreviousButton = null!;
    private Button _reviewNextButton = null!;
    private bool _reviewPaneVisible;
    private ReviewingPaneSession _reviewingPaneSession = null!;

    // Thesaurus Pane (Review > Proofing > Thesaurus, Shift+F7): a docked right pane showing senses +
    // synonyms for the word at the caret, backed by the bundled compact synonym dictionary. Insert replaces
    // the word; Copy puts the synonym on the clipboard. Mirrors the ReviewingPane dock/toggle shape.
    private ThesaurusPane _thesaurusPane = null!;

    // Balloon Overlay (Review > Show Markup > Show Revisions in Balloons): a 200px strip to the right
    // of the editor that renders comments and tracked-change revisions as rounded-rectangle callouts
    // connected to their anchored text by dashed leader lines. Toggled via the Show Markup menu.
    private BalloonOverlay _balloonOverlay = null!;

    // Notes Pane (References > Show Notes): a docked right-side pane that lists every footnote and
    // endnote as a stub (Kind + id + first line). Selecting a stub loads its paragraphs into a sub-editor
    // (a second DocumentView) for rich editing; Apply copies the edited blocks back into the note's
    // Content and re-renders the main editor. Delete removes the note from the model and strips its
    // marker from the body. Mirrors the ReviewingPane dock/toggle shape.
    private Border _notesPane = null!;
    private ListBox _notesList = null!;
    private DocumentView _notesSubEditor = null!;
    private TextBlock _notesSelectedLabel = null!;
    private Button _notesApplyButton = null!;
    private Button _notesDeleteButton = null!;
    private bool _notesPaneVisible;
    private DocumentNotesPaneSession _documentNotesPaneSession = null!;

    // Header/Footer Pane (replaces plain-text HeaderFooterSlotDialog): a docked pane with a slot
    // selector (header/footer/even/first × header/footer) and a DocumentView sub-editor so run
    // formatting (bold/italic/colour/page-number fields) is preserved round-trip. Opening the pane
    // loads the slot's Paragraphs via the wrapper pattern; Close copies them back and re-renders.
    private Border _hfPane = null!;
    private TextBlock _hfSlotLabel = null!;
    private DocumentView _hfSubEditor = null!;
    private HeaderFooterSlotKind? _hfActiveSlot;

    // Navigation-pane search (the box at the top of the pane). Typing finds every occurrence of the term
    // in the document body; the result label shows the count and Next/Prev step through the matches,
    // jumping each into view in the editor. The heading outline below is filtered to entries that either
    // match the term themselves or own a matching block in their subtree. All matching reuses the pure
    // FreeW.Core.Model.TextSearch helper (the same one Find & Replace uses) — no bespoke search here.
    private TextBox _navSearch = null!;
    private TextBlock _navSearchStatus = null!;
    private Button _navSearchPrev = null!;
    private Button _navSearchNext = null!;

    // Identity/palette for the shared window shell.  Colors are resolved from the active theme tokens
    // (FreeWTitleBarBrush / FreeWAccentBrush) registered by WpfThemeApplier at startup, with literal
    // fallbacks so tests that construct MainWindow without a running Application still work.
    // The fallbacks are the canonical FreeW palette for tests that construct a window without an app.
    private static readonly ProductThemeResourceProfile ThemeResources = ProductThemeResourceProfiles.FreeW;

    private static ShellChromeOptions BuildChromeOptions() => new()
    {
        BadgeLetter = Program.ActiveTheme.VisualAssets.ProductGlyph,
        TitleBarColor = WpfThemeResourceResolver.ResolveProjectedOr<SolidColorBrush, Color>(
            ThemeResources.TitleBarBrush,
            brush => brush.Color,
            WpfThemeApplier.ToColor(BrandThemes.FreeW.Colors.TitleBar)),
        TitleBarForegroundColor = WpfThemeResourceResolver.ResolveProjectedOr<SolidColorBrush, Color>(
            ThemeResources.Brush("TitleBarForeground"),
            brush => brush.Color,
            WpfThemeApplier.ToColor(BrandThemes.FreeW.Colors.TitleBarForeground)),
        BadgeColor = WpfThemeResourceResolver.ResolveProjectedOr<SolidColorBrush, Color>(
            ThemeResources.BadgeBrush,
            brush => brush.Color,
            WpfThemeApplier.ToColor(BrandThemes.FreeW.Colors.Accent)),
        CaptionHeight = 34,
        IconUri = Program.ActiveTheme.VisualAssets.GetWpfPackUri("FreeW.App.Host")
    };

    // The grey "desk" the Print-Layout page floats on. Frozen so it can back the editor cheaply.
    private static readonly Brush WorkspaceBrush = CreateWorkspaceBrush();
    private Border _workspace = null!;

    private static Brush CreateWorkspaceBrush()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6));
        brush.Freeze();
        return brush;
    }

    // Read mode (distraction-free view) chrome we hide/restore, plus the saved presentation we restore.
    private Border _titleBar = null!;
    private UIElement _ribbon = null!;
    private TabControl _ribbonTabs = null!;

    // Manages the Word-style contextual "Tools" tabs (Picture Format / Table Design / Table Layout):
    // the shared controller shows them only while their selection context is active.
    private RibbonContextualTabController _contextualTabs = null!;

    // File ribbon tab (Word-style Backstage entry): selecting it opens Backstage and the shared router
    // restores the previously-active content tab.
    private TabItem _fileTab = null!;
    private RibbonFileTabRouter? _fileTabRouter;
    private Border _status = null!;
    private Border _markedAsFinalBanner = null!;
    private TextBlock _dataFolderText = null!;
    private FrameworkElement _dataFolderItem = null!;
    private FrameworkElement _viewSwitchItem = null!;
    private FrameworkElement _zoomItem = null!;
    private IDisposable? _readAloudCommandLifetime;
    private readonly FreeWEditorInteractionSession _editorInteraction = new();
    private FreeWApplicationCommandRouter _applicationCommands = null!;

    // Status-bar view-switch toggle buttons for the three mutually-exclusive print-family view modes
    // (Print Layout / Web Layout / Draft). They mirror the same state as the View ribbon's Views group;
    // RefreshViewModeChecks keeps exactly one of them checked to match _editor.ViewMode.
    private ToggleButton _printLayoutSwitch = null!;
    private ToggleButton _webLayoutSwitch = null!;
    private ToggleButton _draftSwitch = null!;

    // Multiple Pages / Side to Side / Split share the same host-neutral view-depth policy as Avalonia.
    // Both multi-page modes use the editable page-box surface; only their page arrangement and
    // navigation chrome differ.
    private readonly FreeWViewSession _viewSession = new(FreeWViewDepthCapabilities.FullDesktop);
    private FlowDocumentPageViewer? _paginatedViewer; // the overlay page viewer (non-null while active)
    private PaginatedEditorPanel? _editablePaginatedPanel;
    private Button? _sideToSidePreviousPairButton;
    private Button? _sideToSideNextPairButton;
    private TextBlock? _sideToSidePairStatusText;
    private UIElement? _workspaceGridChild;        // saved workspace child so restore is reversible

    // Split Window: a GridSplitter divides the workspace into a top live-editor pane and a bottom
    // read-only FlowDocumentScrollViewer snapshot. Toggling off restores the single editor.
    private Grid? _splitGrid;                      // the split host grid (non-null while active)
    private System.Windows.Threading.DispatcherTimer? _splitDebounceTimer; // ~300 ms refresh gate

    // PagedEdit: editable paginated surface. When active the workspace child is swapped
    // from the live workspaceGrid to a PaginatedEditorPanel. Exiting commits all page boxes back to
    // the model and reloads the continuous editor. Opt-in via View ▸ Views ▸ Page Edit.
    private bool _pagedEditMode;
    private PaginatedEditorPanel? _pagedEditPanel;  // non-null while PagedEdit is active
    private ToggleButton _pagedEditSwitch = null!;  // status-bar shortcut toggle

    // Outline view (View > Outline). The outline surface overlays the normal editing surface; entering the
    // view hides the workspace (and its rulers) and shows the outline, exiting restores them verbatim —
    // the same save/restore shape as Read Mode. The model is never mutated by switching views.
    private OutlineView _outlineView = null!;
    private bool _outlineMode;
    private Thickness _editorMarginBeforeReadMode;
    private double _editorMaxWidthBeforeReadMode = double.PositiveInfinity;
    private HorizontalAlignment _editorAlignmentBeforeReadMode = HorizontalAlignment.Stretch;
    // Print-Layout sizing the editor applies (page-width Width + drop shadow) which read mode neutralizes
    // for its reading column and restores on exit, so the two view toggles don't fight over the surface.
    private double _editorWidthBeforeReadMode = double.NaN;
    private Effect? _editorEffectBeforeReadMode;
    private System.Windows.Media.Brush? _editorBackgroundBeforeReadMode;

    // FreeW's persisted settings (shared JsonSettingsStore). Defaults are used when none are supplied,
    // so the window stays constructible in isolation; Program.Main passes the loaded options + the store
    // that persists edits made from the backstage Options dialog. The options instance is mutated in place
    // so settings read live by FileCommands (e.g. the recent-files cap) take effect without a restart.
    private readonly FreeWOptions _options;
    private readonly FreeWOptionsRuntimeSession _optionsRuntime;
    private readonly IApplicationOptionsStore<FreeWOptions> _optionsStore;
    private readonly IUserMessageService? _messageService;
    private readonly IPlatformClipboard _platformClipboard;
    private readonly FreeWDocumentWindowPlanner _documentWindowPlanner;
    private readonly int _documentWindowNumber;

    private FreeWRibbonHostExecutionPorts CreateRibbonHostExecutionPorts() =>
        FreeWRibbonHostExecutionPorts.Empty with
        {
            Open = () => _applicationCommands.Execute(FreeWKeyboardCommand.OpenDocument),
            Save = () => _applicationCommands.Execute(FreeWKeyboardCommand.SaveDocument),
            Cut = () => _applicationCommands.Execute(FreeWKeyboardCommand.Cut),
            Copy = () => _applicationCommands.Execute(FreeWKeyboardCommand.Copy),
            Paste = () => _applicationCommands.Execute(FreeWKeyboardCommand.Paste),
            Backstage = ShowBackstage,
            NewDocument = () => _applicationCommands.Execute(FreeWKeyboardCommand.NewDocument),
            ToggleNavigationPane = ToggleNavPane,
            IsNavigationPaneVisible = () => _navPaneVisible,
            ToggleReviewingPane = ToggleReviewPane,
            IsReviewingPaneVisible = () => _reviewPaneVisible,
            ToggleRevealFormatting = ToggleRevealFormatting,
            IsRevealFormattingVisible = () => _revealPaneVisible,
            OpenFindReplaceDialog = () => OpenFindReplace(),
            SetPrintLayout = () => SetViewMode(DocumentViewMode.PrintLayout),
            SetWebLayout = () => SetViewMode(DocumentViewMode.WebLayout),
            SetDraftView = () => SetViewMode(DocumentViewMode.Draft),
            GetDocumentViewChecks = CurrentDocumentViewChecks,
            SetOutlineView = ToggleOutlineView,
            IsOutlineViewActive = () => _outlineMode,
            OpenZoomDialog = OpenZoomDialog,
            ApplyZoom = (absolute, delta) =>
                _editor.ZoomLevel = absolute ?? ZoomLevels.Clamp(_editor.ZoomLevel + delta),
            ZoomOnePage = ZoomToOnePage,
            ZoomPageWidth = ZoomToPageWidth,
            OpenPrintPreview = OpenPrintPreview,
            ToggleReadMode = ToggleReadMode,
            IsReadModeActive = () => _editorInteraction.IsReadModeActive,
            ApplyReadModeColumnWidth = ApplyReadModeColumnWidth,
            ApplyReadModePageColor = ApplyReadModePageColor,
            ToggleRuler = ToggleRulers,
            IsRulerVisible = () => _rulersVisible,
            ToggleMultiplePages = ToggleMultiplePages,
            IsMultiplePagesActive = () => _viewSession.CurrentDepth.IsMultiplePagesActive,
            ToggleSideToSide = ToggleSideToSide,
            IsSideToSideActive = () => _viewSession.CurrentDepth.IsSideToSideActive,
            ToggleSplit = ToggleSplitWindow,
            IsSplitActive = () => _viewSession.CurrentDepth.IsSplitActive,
            TogglePagedEditView = TogglePagedEditView,
            ToggleNotesPane = ToggleNotesPane,
            IsNotesPaneVisible = () => _notesPaneVisible,
            OpenHeaderFooterPane = OpenHeaderFooterPane,
            CloseHeaderFooterPane = CloseHeaderFooterPane,
            AcceptThisChange = AcceptSelectedRevision,
            RejectThisChange = RejectSelectedRevision,
            PreviousChange = () => StepRevision(-1),
            NextChange = () => StepRevision(+1),
            NewWindow = OpenNewWindow,
            ArrangeAll = ArrangeAllWindows,
            OpenThesaurus = ToggleThesaurusPane,
            ToggleReviewBalloons = ToggleBalloons,
            IsReviewBalloonsActive = () => _balloonOverlay?.BalloonsEnabled ?? false,
            OpenHelpOnline = () => OpenExternalHelpLink(
                FreeWProductInfo.HelpUrl,
                FreeWApplicationFrameTextCatalog.HelpOnlineCommandName),
            OpenFeedback = () => OpenExternalHelpLink(
                FreeWAppInfo.FeedbackUrl,
                FreeWApplicationFrameTextCatalog.FeedbackCommandName),
            CopyDiagnostics = CopyDiagnostics,
            CheckForUpdates = () => OpenExternalHelpLink(
                FreeWProductInfo.LatestReleaseUrl,
                FreeWApplicationFrameTextCatalog.CheckForUpdatesCommandName),
            OpenAbout = ShowAboutDialog,
            OpenLegalNotices = ShowLegalNoticesDialog,
            OpenMailMergeErrorReport = OpenMailMergeErrorReport,
            PrintMailMergeDocument = PrintMailMergeDocument,
        };

    public MainWindow() : this(new FreeWOptions())
    {
    }

    public MainWindow(
        FreeWOptions options,
        IApplicationOptionsStore<FreeWOptions>? optionsStore = null,
        IUserMessageService? messageService = null,
        IPlatformClipboard? platformClipboard = null,
        IReadOnlyList<string>? startupFilePaths = null,
        bool suppressStartupRecoveryOffer = false,
        FreeWDocumentWindowPlanner? documentWindowPlanner = null,
        int documentWindowNumber = 1)
    {
        if (documentWindowNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(documentWindowNumber));

        _options = options ?? new FreeWOptions();
        _optionsRuntime = new FreeWOptionsRuntimeSession(_options);
        _messageService = messageService;
        _platformClipboard = platformClipboard ?? new WpfPlatformClipboard(Dispatcher);
        _documentWindowPlanner = documentWindowPlanner ?? new FreeWDocumentWindowPlanner();
        _documentWindowNumber = documentWindowNumber;
        _optionsStore = optionsStore ?? new InMemoryApplicationOptionsStore<FreeWOptions>(_options);
        Title = FreeWApplicationFrameDescriptor.Title.ApplicationName;
        Width = 1280;
        Height = 760;
        // Open maximized like FreeX, so the ribbon shows its groups in full rather than collapsing the
        // dense tabs to overflow dropdowns at a small default size.
        WindowState = WindowState.Maximized;
        Background = WpfThemeResourceResolver.Find<Brush>(ThemeResources.SheetSurfaceBrush)
            ?? new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        // Build the borderless WindowChrome shell — custom integrated title bar with embedded window
        // buttons, Win11 rounded corners, the maximized inset, and the shared chrome styles — from the
        // shared tier, so FreeW assembles its window from shared parts instead of re-coding the chrome.
        // App-specific ribbon brushes/styles still come from FreeWRibbonResources (merged at the ribbon).
        var chromeOptions = BuildChromeOptions();
        ShellChrome.ConfigureWindow(this, chromeOptions);

        var chromeStack = new StackPanel { Orientation = Orientation.Vertical };

        var body = new DockPanel { LastChildFill = true };

        var editor = new DocumentView(_platformClipboard) { Margin = new Thickness(40, 24, 40, 24) };
        _editor = editor;
        _lastFocusedDocumentEditor = editor;
        AddHandler(
            Keyboard.GotKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler(OnWindowGotKeyboardFocus),
            handledEventsToo: true);
        // Push the persisted AutoCorrect / AutoFormat-As-You-Type settings so the editor's as-you-type
        // rules honour the user's toggles from the first keystroke (re-applied when Options is saved).
        ApplyEditorTypingOptions(_optionsRuntime.EditorTypingOptions);
        editor.LoadModel(FreeWSampleDocumentFactory.Create(FreeWSampleDocumentProfile.ClassicEditor));
        _navigationPaneSession = new NavigationPaneSession(
            CurrentPaneDocument,
            new NavigationPaneMutationActions(
                editor.MoveHeading,
                editor.PromoteHeading,
                editor.DemoteHeading,
                editor.CollapseHeading,
                editor.ExpandHeading,
                editor.IsHeadingCollapsed),
            NavigationPaneTextCatalog.Resolve(UiText.Get));
        _reviewingPaneSession = new ReviewingPaneSession(
            editor.ListRevisions,
            new ReviewingPaneMutationActions(
                editor.AcceptRevision,
                editor.RejectRevision,
                () =>
                {
                    if (!editor.HasRevisions())
                        return false;
                    editor.AcceptAllRevisions();
                    return true;
                },
                () =>
                {
                    if (!editor.HasRevisions())
                        return false;
                    editor.RejectAllRevisions();
                    return true;
                }));
        _documentNotesPaneSession = new DocumentNotesPaneSession(
            CurrentPaneDocument,
            new DocumentNotesPaneMutationActions(
                (id, footnote, paragraphs) =>
                {
                    var exists = footnote
                        ? editor.Model.Footnotes.ContainsKey(id)
                        : editor.Model.Endnotes.ContainsKey(id);
                    if (!exists)
                        return false;
                    editor.ReplaceNoteContent(id, footnote, paragraphs);
                    return true;
                },
                (id, footnote) =>
                {
                    var exists = footnote
                        ? editor.Model.Footnotes.ContainsKey(id)
                        : editor.Model.Endnotes.ContainsKey(id);
                    if (!exists)
                        return false;
                    if (footnote)
                        editor.DeleteFootnote(id);
                    else
                        editor.DeleteEndnote(id);
                    return true;
                }));
        var stateStore = new RibbonStateStore();
        _stateStore = stateStore;
        var commands = FreeWRibbonCommands.Build(
            editor,
            stateStore,
            CreateRibbonHostExecutionPorts(),
            new FreeWWpfRibbonNativeExecutionPorts(
                ResolveFieldEditor: ResolveFieldCommandEditor));
        if (commands.TryGet("freew.read-aloud", out var readAloudCommand))
            _readAloudCommandLifetime = readAloudCommand as IDisposable;
        _file = new FileCommands(this, editor, UpdateTitle, _options, messageService: _messageService);
        _applicationCommands = new FreeWApplicationCommandRouter(new FreeWApplicationCommandActions(
            NewDocument: () => _file.New(),
            OpenDocument: () => _file.Open(),
            SaveDocument: () => _file.Save(),
            SaveDocumentAs: () => _file.SaveAs(),
            PrintDocument: Print,
            Find: () => OpenFindReplace(FindReplaceOpenMode.Find),
            Replace: () => OpenFindReplace(FindReplaceOpenMode.Replace),
            Cut: () => ExecuteEditingCommand(ApplicationCommands.Cut),
            Copy: () => ExecuteEditingCommand(ApplicationCommands.Copy),
            Paste: () => ExecuteEditingCommand(ApplicationCommands.Paste),
            PasteTextOnly: _editor.PastePlainText,
            SelectAll: () => ExecuteEditingCommand(ApplicationCommands.SelectAll),
            Undo,
            Redo,
            RevealFormatting: ToggleRevealFormatting,
            Thesaurus: ToggleThesaurusPane,
            LockCurrentField: () => ExecuteCurrentFieldCommand(
                FreeWKeyboardCommand.LockCurrentField,
                ResolveFieldCommandEditor()),
            UnlockCurrentField: () => ExecuteCurrentFieldCommand(
                FreeWKeyboardCommand.UnlockCurrentField,
                ResolveFieldCommandEditor()),
            UnlinkCurrentField: () => ExecuteCurrentFieldCommand(
                FreeWKeyboardCommand.UnlinkCurrentField,
                ResolveFieldCommandEditor()),
            ToggleCurrentFieldCode: () => ExecuteCurrentFieldCommand(
                FreeWKeyboardCommand.ToggleCurrentFieldCode,
                ResolveFieldCommandEditor()),
            ToggleFieldCodes: _editor.ToggleFieldCodes,
            UpdateCurrentField: () => ExecuteCurrentFieldCommand(
                FreeWKeyboardCommand.UpdateCurrentField,
                ResolveFieldCommandEditor())));
        editor.TextChanged += (_, _) =>
        {
            _file.MarkDirty();
            UpdateCounts();
            RefreshOutline();
            RefreshContextualTabs();
            RefreshReviewPane();
            RefreshNotesPane();
            // Balloon overlay: rebuild whenever the document changes (no-op when disabled).
            _balloonOverlay?.Rebuild();
            // Debounced refresh of the split-window snapshot pane (~300 ms), so rapid keystrokes
            // don't re-paginate on every character. No-op when the split pane is not open.
            ScheduleSplitPaneRefresh();
        };
        // Live selection stats: when the caret/selection moves, refresh the status-bar counts so a
        // non-empty selection shows its own word/character totals (and reverts when nothing is selected).
        // Also re-evaluate which contextual "Tools" tabs apply to the new selection.
        editor.SelectionChanged += (_, _) => { UpdateCounts(); RefreshContextualTabs(); };
        _autosave = new AutosaveCoordinator(editor, _file, recoverInNewWindow: OpenNewWindowWithRecoveredSnapshot);
        Loaded += (_, _) =>
        {
            // R133-remediation: OfferRecovery now enumerates and offers EVERY pending snapshot, not
            // just the latest. The extra windows it opens (via OpenNewWindowWithRecoveredSnapshot)
            // pass suppressStartupRecoveryOffer:true so their own Loaded handler does not re-run
            // OfferRecovery and re-prompt for the very candidates this window's call is already
            // working through.
            if (!suppressStartupRecoveryOffer)
                _autosave.OfferRecovery(this);
            _autosave.Start();
        };
        Closing += (_, e) =>
        {
            // Save-before-close gate (shared FileLifecyclePlanner). Cancel the close if the user
            // backs out; only stop autosave (which deletes the recovery snapshot) once we commit to
            // closing. Previously FreeW closed without prompting and silently lost unsaved work.
            if (!_file.ConfirmCloseAllowed())
            {
                e.Cancel = true;
                return;
            }
            DisposeReadAloud();
            _autosave.Stop();
        };

        // The title bar comes from the shared shell; the host fills its QAT slot and keeps the title text.
        // It is composed into the OUTER grid (below), in its own top row ABOVE the Backstage overlay, so
        // opening the File screen never hides the caption / QAT / window buttons. Only the ribbon goes into
        // the chrome stack here.
        var titleBar = ShellChrome.BuildTitleBar(this, chromeOptions);
        _titleBar = titleBar.Root;
        _titleBinder = new SisterWpfWindowTitleBinder(this, titleBar.TitleText);
        AddQuickAccessButtons(titleBar.QatHost);

        var (ribbon, ribbonTabs) = BuildRibbon(
            FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeWRibbonCapabilities.Wpf),
            commands,
            stateStore);
        _ribbon = ribbon;
        _ribbonTabs = ribbonTabs;
        chromeStack.Children.Add(ribbon);

        var status = BuildStatusBar();
        var clientFrame = SisterAppClientFrameBuilder.Build(
            new SisterAppClientFrameSpec(
                Chrome: chromeStack,
                WorkArea: body,
                StatusBar: status));
        var root = clientFrame.Root;

        var navPane = BuildNavPane();
        DockPanel.SetDock(navPane, Dock.Left);
        body.Children.Add(navPane);

        // Reveal Formatting pane docks on the RIGHT (Word's side for the Shift+F1 pane), opposite the
        // left navigation pane. Added before the fill child so the DockPanel reserves its edge first.
        var revealPane = BuildRevealPane();
        DockPanel.SetDock(revealPane, Dock.Right);
        body.Children.Add(revealPane);

        // Reviewing Pane also docks on the RIGHT (Word's side for the revisions list). Added before the
        // fill child so the DockPanel reserves its edge; only one of reveal/review is typically open.
        var reviewPane = BuildReviewPane();
        DockPanel.SetDock(reviewPane, Dock.Right);
        body.Children.Add(reviewPane);

        // Thesaurus Pane docks on the RIGHT (Review > Proofing > Thesaurus, Shift+F7). Collapsed by
        // default; ToggleThesaurusPane shows/hides it and triggers a lookup from the bundled dataset.
        _thesaurusPane = new ThesaurusPane(editor, _platformClipboard);
        var thesaurusPane = _thesaurusPane.Build();
        DockPanel.SetDock(thesaurusPane, Dock.Right);
        body.Children.Add(thesaurusPane);

        // Notes Pane docks on the BOTTOM (Word positions the notes pane below the body). Collapsed by
        // default; ToggleNotesPane shows/hides it and RefreshNotesPane rebuilds the stub list.
        var notesPane = BuildNotesPane();
        DockPanel.SetDock(notesPane, Dock.Bottom);
        body.Children.Add(notesPane);

        // Header/Footer Pane docks on the BOTTOM (Word's in-document header region analogue). Collapsed
        // by default; OpenHeaderFooterPane loads the slot into the sub-editor.
        var hfPane = BuildHeaderFooterPane();
        DockPanel.SetDock(hfPane, Dock.Bottom);
        body.Children.Add(hfPane);

        // Grey "workspace" behind the editor so the Print-Layout page reads as a white sheet floating on a
        // desk. The editor sizes/centres itself to the page width in Print-Layout mode (see
        // DocumentView.ApplyPageChrome); the grey shows on either side. In plain/continuous mode the editor
        // stretches to fill, so the grey is fully covered and the look is unchanged. Purely host chrome.
        // Word-style rulers (Print-Layout only): a horizontal tick scale above the page, a thinner
        // vertical scale down its left edge, and the tab-stop selector where they meet. The editor sits
        // in the bottom-right cell so the page floats on the grey workspace exactly as before.
        _hRuler = new Ruler(editor, Ruler.Orientation.Horizontal);
        _vRuler = new Ruler(editor, Ruler.Orientation.Vertical);
        _rulerTabSelector = BuildRulerTabSelector();

        // Workspace grid: col 0 = vertical ruler, col 1 = editor + floating canvas, col 2 = balloon strip.
        var workspaceGrid = new Grid();
        workspaceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                         // col 0: v-ruler
        workspaceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });    // col 1: editor
        workspaceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                         // col 2: balloon strip
        workspaceGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        workspaceGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Grid.SetRow(_rulerTabSelector, 0);
        Grid.SetColumn(_rulerTabSelector, 0);
        workspaceGrid.Children.Add(_rulerTabSelector);

        Grid.SetRow(_hRuler, 0);
        Grid.SetColumn(_hRuler, 1);
        workspaceGrid.Children.Add(_hRuler);

        Grid.SetRow(_vRuler, 1);
        Grid.SetColumn(_vRuler, 0);
        workspaceGrid.Children.Add(_vRuler);
        ApplyRulerVisibility();

        // Phase 1: floating-image overlay canvas. Host the editor and a transparent sibling Canvas in
        // the same Grid cell (row 1, col 1) so the canvas sits on top of the editor at the same size
        // and position. The canvas is transparent and IsHitTestVisible=true only on its image children.
        // This is the MINIMAL layout change: we wrap both into a single Grid that lives in the cell,
        // leaving the surrounding workspaceGrid / workspace Border structure completely untouched.
        var editorOverlayHost = new Grid();
        var floatingCanvas = new Canvas
        {
            IsHitTestVisible = true,
            Background = System.Windows.Media.Brushes.Transparent
        };
        editorOverlayHost.Children.Add(editor);
        editorOverlayHost.Children.Add(floatingCanvas);
        editor.SetFloatingCanvas(floatingCanvas);

        Grid.SetRow(editorOverlayHost, 1);
        Grid.SetColumn(editorOverlayHost, 1);
        workspaceGrid.Children.Add(editorOverlayHost);

        // Balloon strip (col 2, rows 0+1): hosts the BalloonOverlay canvas. Width=0 when disabled;
        // opens to 200px when Show Markup > Show Revisions in Balloons is toggled on.
        _balloonOverlay = new BalloonOverlay(editor);
        var balloonVisual = _balloonOverlay.Visual;
        Grid.SetRow(balloonVisual, 0);
        Grid.SetRowSpan(balloonVisual, 2);
        Grid.SetColumn(balloonVisual, 2);
        workspaceGrid.Children.Add(balloonVisual);

        _workspace = new Border
        {
            Background = WorkspaceBrush,
            Child = workspaceGrid
        };

        // Outline view (View > Outline): an indented heading/body outline with the Outlining mini-toolbar.
        // It overlays the normal editing surface and is collapsed until the view is switched on; both share
        // one host grid so toggling between Print Layout and Outline just flips which child is visible —
        // the editor model is never disturbed (mirrors the Read-Mode enter/exit pattern).
        _outlineView = new OutlineView(_editor) { Visibility = Visibility.Collapsed };
        var contentHost = new Grid();
        contentHost.Children.Add(_workspace);
        contentHost.Children.Add(_outlineView);

        // "Marked as Final" banner (Word's advisory read-only bar): a subtle amber strip docked above the
        // editing surface, with an "Edit Anyway" button that clears the flag. Collapsed until the document
        // is marked final; kept in sync via the editor's ProtectionStateChanged event.
        _markedAsFinalBanner = BuildMarkedAsFinalBanner();
        DockPanel.SetDock(_markedAsFinalBanner, Dock.Top);
        body.Children.Add(_markedAsFinalBanner);

        body.Children.Add(contentHost);

        // Keep the banner and the Protect-group ribbon toggles in sync with the editor's protection /
        // Mark-as-Final state, however it changes (ribbon command, load, or "Edit Anyway").
        editor.ProtectionStateChanged += (_, _) =>
        {
            RefreshMarkedAsFinalBanner();
            var protectionStatePlan = ReviewProtectionStatePlanner.Build(_editor.Model.Protection, _editor.IsMarkedAsFinal);
            foreach (var commandState in protectionStatePlan.Commands)
                _stateStore.SetChecked(commandState.CommandId, commandState.IsChecked);
        };

        // Keep the indent/tab markers on the horizontal ruler following the caret/selection.
        editor.SelectionChanged += (_, _) => _hRuler.Refresh();

        // Keep the Reveal Formatting pane (when shown) reflecting the caret's current formatting.
        editor.SelectionChanged += (_, _) => RefreshRevealFormatting();

        CommandBindings.Add(new CommandBinding(
            ApplicationCommands.New,
            (_, _) => _applicationCommands.Execute(FreeWKeyboardCommand.NewDocument)));
        CommandBindings.Add(new CommandBinding(
            ApplicationCommands.Open,
            (_, _) => _applicationCommands.Execute(FreeWKeyboardCommand.OpenDocument)));
        CommandBindings.Add(new CommandBinding(
            ApplicationCommands.Save,
            (_, _) => _applicationCommands.Execute(FreeWKeyboardCommand.SaveDocument)));
        CommandBindings.Add(new CommandBinding(
            ApplicationCommands.SaveAs,
            (_, _) => _applicationCommands.Execute(FreeWKeyboardCommand.SaveDocumentAs)));

        CommandBindings.Add(new CommandBinding(
            ApplicationCommands.Print,
            (_, _) => _applicationCommands.Execute(FreeWKeyboardCommand.PrintDocument)));
        InstallSharedKeyboardShortcuts();

        UpdateTitle();
        UpdateCounts();
        RefreshOutline();

        // Print Layout is the default view (the Word default), so seed the View > Views toggles (Print
        // Layout / Web Layout / Draft) to reflect the editor's initial view mode — exactly one is checked.
        RefreshViewModeChecks();
        _stateStore.SetChecked("freew.ruler", _rulersVisible);

        // The Word-style Backstage (File screen) is a full-window overlay above the document. It is
        // hidden by default; the File button (title bar) shows it, a back arrow / Esc hides it. It reuses
        // the host's existing File commands — no file IO is reimplemented in the backstage.
        _backstage = new BackstageView(new BackstageCallbacks(
            DisplayName: _file.DisplayName,
            CurrentPath: _file.CurrentPath,
            GetRecentEntries: () => _file.RecentEntries,
            GetFileFormats: () => _file.SaveFormats,
            GetPageSettings: () => CurrentBackstageDocument().Page,
            GetCurrentOptions: () => _options,
            GetDataFolder: FreeWApplicationFrameDescriptor.ResolveDataFolderLabel,
            GetDocument: CurrentBackstageDocument,
            GetIsDirty: () => _file.IsDirty,
            NewDocument: () => _applicationCommands.Execute(FreeWKeyboardCommand.NewDocument),
            OpenRecent: path => _file.OpenRecentPath(path),
            OpenFolder: folder => _file.OpenFromFolder(folder),
            Browse: () => _applicationCommands.Execute(FreeWKeyboardCommand.OpenDocument),
            RecoverUnsaved: () => _autosave.RecoverUnsavedDocuments(this),
            ImportPdfText: () => _file.ImportPdfText(),
            Save: () => _applicationCommands.Execute(FreeWKeyboardCommand.SaveDocument),
            SaveAs: () => _applicationCommands.Execute(FreeWKeyboardCommand.SaveDocumentAs),
            SaveAsFormat: (extension, _) => _file.SaveAs(extension),
            SaveCopy: () => _file.SaveCopy(),
            OpenContainingFolder: OpenContainingFolder,
            ExportPdf: ExportToPdf,
            ExportXps: ExportToXps,
            EditProperties: OpenProperties,
            MarkAsFinal: ToggleMarkAsFinal,
            RestrictEditing: OpenRestrictEditing,
            InspectDocument: InspectDocument,
            CheckAccessibility: CheckAccessibility,
            OpenOptions: OpenOptions,
            CloseDocument: CloseDocument,
            Print: () => _applicationCommands.Execute(FreeWKeyboardCommand.PrintDocument),
            PrintPreview: OpenPrintPreview,
            SaveAsSuggested: (fileName, extension) => _file.SaveAsSuggested(fileName, extension),
            OnClosed: () =>
            {
                SetEditorAdornersVisible(true);
                _editor.Focus();
            },
            GetDisplayName: () => _file.DisplayName,
            GetCurrentPath: () => _file.CurrentPath));

        // Compose the window. The title bar occupies its own top row of the OUTER grid, always above the
        // Backstage; `belowTitle` stacks the Backstage overlay over the 3-row body (ribbon + document +
        // status), so the File screen covers those but leaves the title bar visible (Word behaviour).
        var frame = SisterAppWindowFrameBuilder.Build(new SisterAppWindowFrameSpec(_titleBar, root, _backstage));
        Content = frame.Root;

        // V5 KeyTips: pressing Alt overlays Word-style letter badges over the ribbon tabs, then over the
        // active tab's controls, so the ribbon is fully keyboard-navigable. The overlay walks the rendered
        // ribbon and draws its badges on the outer grid (which spans the whole client area).
        KeyTipsOverlay.Install(this, _ribbonTabs, frame.Root);

        // R133-wpf-startup-file-args: file-association double-clicks, dragged-file launches, and plain
        // command-line invocations used to be silently ignored -- FreeW.App.Host.Program.Main never
        // even read them, so the window always fell back to CreateSampleDocument() above. Open the
        // first resolvable argument into THIS window (replacing the sample doc) synchronously, so it
        // is showing by the time the window is first painted. Must run at the very END of the
        // constructor: OpenPath's success path calls _editor.LoadModel, which synchronously raises
        // RichTextBox's Document-changed event straight into the editor.TextChanged handler wired
        // above, which fans out into UpdateCounts/RefreshOutline/RefreshContextualTabs/RefreshReviewPane/
        // RefreshNotesPane -- all of which read status-bar/pane fields that this constructor builds
        // throughout its body, so opening the file any earlier throws a NullReferenceException that
        // OpenPath's own broad catch quietly turns into a misleading "Could not open the document" for
        // what would otherwise be a perfectly good file. OpenPath does not dirty-gate (the sample doc it
        // may replace is never dirty) and already reports a missing file or an unrecognized extension
        // through the normal ShowError dialog instead of throwing, so a bad startup argument degrades to
        // the sample document rather than crashing the app before it is usable. Any FURTHER file
        // arguments (e.g. multiple files dragged onto the taskbar icon in one launch, which the OS
        // delivers as multiple path arguments to a single process) each open in their own new window --
        // deferred to Loaded so the dispatcher is guaranteed to be pumping before OpenAdditionalStartupFiles
        // calls Show() on the sibling windows it creates, mirroring OpenNewWindow()'s own
        // (already-proven-safe) window-creation pattern below.
        if (startupFilePaths is { Count: > 0 })
        {
            _file.OpenPath(startupFilePaths[0]);
            if (startupFilePaths.Count > 1)
            {
                var additionalStartupFilePaths = startupFilePaths.Skip(1).ToArray();
                Loaded += (_, _) => OpenAdditionalStartupFiles(additionalStartupFilePaths);
            }
        }
    }

    private void InstallSharedKeyboardShortcuts()
    {
        var commands = _applicationCommands.Shortcuts
            .Select(shortcut => shortcut.Command)
            .Distinct()
            .ToDictionary(
                command => command,
                command => new RoutedUICommand(command.ToString(), $"FreeW{command}", typeof(MainWindow)));

        foreach (var (command, routedCommand) in commands)
        {
            CommandBindings.Add(new CommandBinding(
                routedCommand,
                (_, _) => _applicationCommands.Execute(command)));
        }

        foreach (var shortcut in _applicationCommands.Shortcuts)
        {
            InputBindings.Add(new KeyBinding(
                commands[shortcut.Command],
                new KeyGesture(ToWpfKey(shortcut.Key), ToWpfModifiers(shortcut.Modifiers))));
        }
    }

    internal static void ExecuteCurrentFieldCommand(
        FreeWKeyboardCommand command,
        DocumentView editor)
    {
        switch (command)
        {
            case FreeWKeyboardCommand.LockCurrentField: editor.SetFieldLockAtCaret(true); break;
            case FreeWKeyboardCommand.UnlockCurrentField: editor.SetFieldLockAtCaret(false); break;
            case FreeWKeyboardCommand.UnlinkCurrentField: editor.UnlinkFieldAtCaret(); break;
            case FreeWKeyboardCommand.ToggleCurrentFieldCode: editor.ToggleFieldCodeAtCaret(); break;
            case FreeWKeyboardCommand.UpdateCurrentField: editor.UpdateFieldAtCaret(); break;
            default: throw new ArgumentOutOfRangeException(nameof(command), command, null);
        }
    }

    private void OnWindowGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (FindDocumentEditor(e.NewFocus) is { } editor)
            _lastFocusedDocumentEditor = editor;
    }

    private DocumentView ResolveFieldCommandEditor()
    {
        var lastFocusedAvailable = _lastFocusedDocumentEditor is { IsVisible: true, IsEnabled: true };
        var target = ResolveFieldCommandEditor(
            Keyboard.FocusedElement,
            _editor,
            _lastFocusedDocumentEditor,
            lastFocusedAvailable);
        ConfigureFieldEvaluationContext(target);
        return target;
    }

    private void ConfigureFieldEvaluationContext(DocumentView target)
    {
        var isBodyEditor = ReferenceEquals(target, _editor);
        target.FieldEvaluationDocument = isBodyEditor ? null : _editor.Model;
        target.FieldEvaluationFileName = isBodyEditor ? null : _editor.CurrentFileName;
    }

    internal static DocumentView ResolveFieldCommandEditor(
        IInputElement? focusedElement,
        DocumentView fallback) =>
        ResolveFieldCommandEditor(focusedElement, fallback, lastFocused: null, lastFocusedAvailable: false);

    internal static DocumentView ResolveFieldCommandEditor(
        IInputElement? focusedElement,
        DocumentView fallback,
        DocumentView? lastFocused,
        bool lastFocusedAvailable)
    {
        return FindDocumentEditor(focusedElement)
            ?? (lastFocusedAvailable && lastFocused is not null ? lastFocused : fallback);
    }

    private static DocumentView? FindDocumentEditor(IInputElement? focusedElement)
    {
        for (var current = focusedElement as DependencyObject;
             current is not null;
             current = GetParent(current))
        {
            if (current is DocumentView editor)
                return editor;
        }

        return null;
    }

    private static DependencyObject? GetParent(DependencyObject current)
    {
        if (current is FrameworkContentElement contentElement)
            return contentElement.Parent;

        var logicalParent = LogicalTreeHelper.GetParent(current);
        if (logicalParent is not null)
            return logicalParent;

        try
        {
            return VisualTreeHelper.GetParent(current);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private void ExecuteEditingCommand(RoutedCommand command)
    {
        var target = Keyboard.FocusedElement ?? _editor;
        if (command.CanExecute(null, target))
            command.Execute(null, target);
    }

    private static Key ToWpfKey(FreeWKeyboardKey key) => key switch
    {
        FreeWKeyboardKey.A => Key.A,
        FreeWKeyboardKey.C => Key.C,
        FreeWKeyboardKey.F => Key.F,
        FreeWKeyboardKey.H => Key.H,
        FreeWKeyboardKey.N => Key.N,
        FreeWKeyboardKey.O => Key.O,
        FreeWKeyboardKey.P => Key.P,
        FreeWKeyboardKey.S => Key.S,
        FreeWKeyboardKey.V => Key.V,
        FreeWKeyboardKey.X => Key.X,
        FreeWKeyboardKey.Y => Key.Y,
        FreeWKeyboardKey.Z => Key.Z,
        FreeWKeyboardKey.F1 => Key.F1,
        FreeWKeyboardKey.F7 => Key.F7,
        FreeWKeyboardKey.F9 => Key.F9,
        FreeWKeyboardKey.F11 => Key.F11,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    };

    private static ModifierKeys ToWpfModifiers(FreeWKeyboardModifiers modifiers)
    {
        var result = ModifierKeys.None;
        if ((modifiers & FreeWKeyboardModifiers.Control) != 0)
            result |= ModifierKeys.Control;
        if ((modifiers & FreeWKeyboardModifiers.Shift) != 0)
            result |= ModifierKeys.Shift;
        if ((modifiers & FreeWKeyboardModifiers.Alt) != 0)
            result |= ModifierKeys.Alt;
        return result;
    }

    // Show the Word-style Backstage (File screen) over the document.
    private void ShowBackstage()
    {
        // The backstage is an opaque overlay, but the editor's page-break markers live in the window
        // AdornerLayer, which draws ABOVE sibling content — so they bleed through the File screen unless the
        // layer is hidden. Collapse it here and restore it in the backstage OnClosed callback.
        SetEditorAdornersVisible(false);
        _backstage.Show();
    }

    private TextDocument CurrentBackstageDocument()
    {
        _editor.CommitToModel();
        return _editor.Model;
    }

    private void CloseDocument()
    {
        Close();
    }

    private static void OpenContainingFolder(string documentPath)
    {
        _ = DesktopPathLauncher.RevealFile(documentPath);
    }

    private void OpenExternalHelpLink(string url, string title)
    {
        var result = DesktopExternalUriLauncher.Open(url);
        if (FreeWSupportCommandFeedbackPlanner.PlanExternalUriLaunch(result, title, url) is not { } feedback)
            return;

        PresentCommandFeedback(feedback);
    }

    private void CopyDiagnostics()
    {
        var diagnosticsDirectory = AppStoragePathPlanner.GetDiagnosticsDirectory(PlatformAppDiagnosticsPathProvider.Instance);
        var optionsPath = AppStoragePathPlanner.GetOptionsFilePathLabelOrFallback(PlatformApplicationDataPathProvider.LocalInstance);
        var diagnosticsText = FreeWAppInfo.CreateDiagnosticsText(diagnosticsDirectory, optionsPath);

        try
        {
            var result = _platformClipboard.WriteAsync(new PlatformClipboardContent(Text: diagnosticsText))
                .AsTask()
                .GetAwaiter()
                .GetResult();
            PresentCommandFeedback(FreeWSupportCommandFeedbackPlanner.PlanDiagnosticsCopy(result));
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or System.Threading.ThreadStateException)
        {
            PresentCommandFeedback(FreeWSupportCommandFeedbackPlanner.PlanDiagnosticsCopy(
                PlatformClipboardWriteResult.Failed(ex.Message)));
        }
    }

    private void PresentCommandFeedback(FreeWCommandFeedbackPlan feedback)
    {
        if (feedback.Tone == FreeWCommandFeedbackTone.Information)
            DialogMessageHelper.ShowInfo(this, feedback.Message, feedback.Title);
        else
            DialogMessageHelper.ShowWarning(this, feedback.Message, feedback.Title);
    }

    private void ShowAboutDialog()
    {
        var dialog = new AboutDialog { Owner = this };
        dialog.ShowDialog();
    }

    private void ShowLegalNoticesDialog()
    {
        var dialog = new LegalNoticesDialog { Owner = this };
        dialog.ShowDialog();
    }

    // Toggle the editor's AdornerLayer (page-break markers, etc.) so they don't draw over the backstage.
    private void SetEditorAdornersVisible(bool visible)
    {
        if (System.Windows.Documents.AdornerLayer.GetAdornerLayer(_editor) is { } layer)
            layer.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    // Recompute which contextual "Tools" tabs apply to the current selection and let the shared controller
    // show/hide them: Picture Format when an image is selected, table tabs when the caret is in a table.
    // The activation keys ("picture"/"table") match the RibbonTabContext keys declared in FreeWRibbon.
    private void RefreshContextualTabs()
    {
        if (_contextualTabs is null)
            return;

        var state = RibbonContextState.None;
        if (_editor.SelectedImage() is not null)
            state = state.With("picture");
        if (_editor.IsCaretInTable())
            state = state.With("table");
        if (_editor.SelectedChart() is not null)
            state = state.With("chart");
        if (_editor.SelectedShape() is not null || _editor.SelectedWordArt() is not null)
            state = state.With("drawing");
        if (_editor.SelectedSmartArt() is not null)
            state = state.With("smartart");

        _contextualTabs.Apply(state);
    }

    // Fill the shared title bar's Quick Access Toolbar slot with Save / Undo / Redo via the shared QAT
    // renderer (Free.Shared.Ribbon.Wpf): neutral descriptors (command id + tooltip + ribbon glyph) rendered
    // as small flat icon buttons (white vector glyphs on the navy caption, shared ChromeFlatButtonStyle,
    // hit-test-visible so clicks land while WindowChrome owns the caption). The click callback routes by
    // command id: Save → the file command; Undo/Redo → the editor's built-in (RichTextBox) history, the same
    // inline history the keyboard drives.
    private void AddQuickAccessButtons(StackPanel host) =>
        SisterQuickAccessToolbarBuilder.Render(
            host,
            this,
            new SisterQuickAccessToolbarActions(
                Save: () => _applicationCommands.Execute(FreeWKeyboardCommand.SaveDocument),
                Undo: () => _applicationCommands.Execute(FreeWKeyboardCommand.Undo),
                Redo: () => _applicationCommands.Execute(FreeWKeyboardCommand.Redo)),
            new QuickAccessToolbarRenderOptions
            {
                ForegroundResourceKey = ThemeResources.Brush("TitleBarForeground").PrimaryKey
            });

    private void DisposeReadAloud()
    {
        var lifetime = _readAloudCommandLifetime;
        _readAloudCommandLifetime = null;
        lifetime?.Dispose();
    }

    private void UpdateTitle()
    {
        _titleBinder.Update(new SisterWpfWindowTitleSpec(
            DisplayName: _file.DisplayName,
            ApplicationName: "FreeW",
            IsDirty: _file.IsDirty,
            DirtyMarker: " *",
            Separator: " \u2014 ",
            WindowSuffix: FreeWDocumentWindowPlanner.FormatWindowSuffix(_documentWindowNumber)));
    }

    // Recompute the live status-bar counts. When there is a non-empty selection, show that selection's
    // word + character totals (via the pure WordCount helpers over Selection.Text); otherwise fall back
    // to the whole-document word/character/paragraph counts. Cheap enough to run on every edit
    // (TextChanged), on selection change, and on document load.
    private void UpdateCounts()
    {
        var selectionText = _editor.Selection.Text;
        var (current, total) = _editor.PageInfo();
        var (section, sections) = _editor.SectionInfo();
        ApplyStatusPlan(_editorInteraction.BuildStatus(new FreeWEditorStatusContext(
            _editor.Model,
            CurrentPage: current,
            TotalPages: total,
            CurrentSection: section,
            TotalSections: sections,
            SelectionText: selectionText)));
    }

    // Refresh the Word-style "Page X of Y", section, and count status. Page position is an approximate
    // continuous-flow location from DocumentView.PageInfo; section is best-effort from SectionInfo.
    private void ApplyStatusPlan(FreeWEditorStatusPlan plan)
    {
        _pageText.Text = plan.PageStatus;
        _sectionText.Text = plan.SectionStatus;
        _countsText.Text = plan.CountsStatus;
    }

    // Build the footer (status bar) as a single full-width row that sits BELOW the ruler + document region
    // (#3): it is docked to the bottom of the outer DockPanel, so the workspace (which contains the vertical
    // ruler and the page) fills only the space above it and can never draw over it.
    //
    // The bar is responsive (#4): a 3-column grid whose middle column ("*") holds the left info group, which
    // is allowed to condense/ellipsize as the window narrows, while the right-side view + zoom controls are
    // pinned in Auto columns so they stay fully visible. The whole strip uses the same #2B579A surface as the
    // title bar with the shared clean (flat, hover-only) footer button styles.
    // Word's "Marked as Final" advisory bar: a subtle amber strip above the editing surface with the
    // information text and an "Edit Anyway" button that clears the flag (re-enabling editing). Collapsed
    // until the document is marked final; see RefreshMarkedAsFinalBanner.
    private Border BuildMarkedAsFinalBanner()
    {
        var safetyText = BackstageInfoSafetyPanePlanner.ResolveText(UiText.Get);
        var text = new TextBlock
        {
            Text = safetyText.MarkedAsFinalBanner,
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x4D, 0x00)),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 12, 0)
        };

        var editAnyway = new Button
        {
            Content = safetyText.EditAnywayLabel,
            MinWidth = 96,
            Padding = new Thickness(8, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center
        };
        editAnyway.Click += (_, _) => _editor.SetMarkedAsFinal(false);

        var row = new DockPanel { LastChildFill = true, Margin = new Thickness(12, 4, 12, 4) };
        DockPanel.SetDock(editAnyway, Dock.Right);
        row.Children.Add(editAnyway);
        row.Children.Add(text);

        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xFD, 0xF3, 0xD0)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE3, 0xC8, 0x70)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Visibility = Visibility.Collapsed,
            Child = row
        };
    }

    // Show the "Marked as Final" banner only while the document carries Word's advisory read-only flag.
    private void RefreshMarkedAsFinalBanner() =>
        _markedAsFinalBanner.Visibility = _editor.IsMarkedAsFinal ? Visibility.Visible : Visibility.Collapsed;

    private Border BuildStatusBar()
    {
        // ── Left info group: Page / Section / Words-Chars-Paragraphs / Data folder. Hosted in a clipping
        //    StackPanel so when space runs short the rightmost item (data folder) is clipped/ellipsized
        //    first while the pinned right-side controls keep their full width. ──
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };

        _pageText = SisterAppStatusBarChrome.CreateInfoText();
        left.Children.Add(_pageText);
        left.Children.Add(SisterAppStatusBarChrome.CreateSeparator());
        _sectionText = SisterAppStatusBarChrome.CreateInfoText();
        left.Children.Add(_sectionText);
        left.Children.Add(SisterAppStatusBarChrome.CreateSeparator());
        _countsText = SisterAppStatusBarChrome.CreateInfoText();
        left.Children.Add(_countsText);

        // The data folder is the lowest-priority item; wrap it so it ellipsizes and is the first to be
        // clipped when the window narrows (its separator + text live in one panel that can shrink away).
        var dataFolderPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        dataFolderPanel.Children.Add(SisterAppStatusBarChrome.CreateSeparator());
        _dataFolderText = SisterAppStatusBarChrome.CreateInfoText();
        _dataFolderText.Text = SisterAppStatusBarTextPlanner.FormatDataFolderStatus(
            FreeWApplicationFrameDescriptor.ResolveDataFolderLabel());
        _dataFolderText.ToolTip = _dataFolderText.Text;
        dataFolderPanel.Children.Add(_dataFolderText);
        _dataFolderItem = dataFolderPanel;
        left.Children.Add(dataFolderPanel);

        // ── Pinned right-side controls. ──
        _viewSwitchItem = (FrameworkElement)BuildViewSwitchControl();

        _zoomItem = (FrameworkElement)BuildZoomControl();
        _zoomItem.Margin = new Thickness(6, 0, 10, 0);

        _status = SisterAppStatusBarChrome.Build(new SisterAppStatusBarSpec(
            // Status bar surface routed through the shared FreeW theme.
            WpfThemeResourceResolver.Find<Brush>(ThemeResources.StatusSurfaceBrush)
                ?? new SolidColorBrush(WpfThemeApplier.ToColor(BrandThemes.FreeW.Colors.StatusSurface)),
            left,
            [_viewSwitchItem, _zoomItem])).Root;
        return _status;
    }

    // The Word-style view-switch cluster on the right of the status bar: compact icon shortcuts for Read
    // Mode plus the three mutually-exclusive print-family views (Print Layout / Web Layout / Draft). They reuse the same
    // MainWindow state the View ribbon drives (ToggleReadMode / SetViewMode), so the ribbon Views group and
    // these buttons stay in lock-step. The print-family buttons are ChromeStatusToggleButtons so the active
    // view reads as pressed; RefreshViewModeChecks keeps exactly one checked.
    private UIElement BuildViewSwitchControl()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        Button ViewButton(string label, string tip, RibbonCommandIconKind icon, Action onClick)
        {
            var button = new Button
            {
                Content = StatusViewIcon(icon),
                Style = (Style)FindResource("ChromeStatusButtonStyle"),
                Width = 24,
                Height = 22,
                Padding = new Thickness(4, 2, 4, 2),
                Margin = new Thickness(1, 2, 1, 2),
                ToolTip = tip
            };
            AutomationProperties.SetName(button, label);
            AutomationProperties.SetHelpText(button, tip);
            button.Click += (_, _) => onClick();
            return button;
        }

        ToggleButton ViewToggle(string label, string tip, RibbonCommandIconKind icon, DocumentViewMode mode)
        {
            var toggle = new ToggleButton
            {
                Content = StatusViewIcon(icon),
                Style = (Style)FindResource("ChromeStatusToggleButtonStyle"),
                Width = 24,
                Height = 22,
                Padding = new Thickness(4, 2, 4, 2),
                Margin = new Thickness(1, 2, 1, 2),
                ToolTip = tip
            };
            AutomationProperties.SetName(toggle, label);
            AutomationProperties.SetHelpText(toggle, tip);
            // Clicking always lands on this mode (re-checking the active one is a no-op); never let the
            // toggle uncheck itself, since exactly one print-family view is always active.
            toggle.Click += (_, _) => SetViewMode(mode);
            return toggle;
        }

        _printLayoutSwitch = ViewToggle(
            FreeWApplicationFrameTextCatalog.PrintLayout.Label,
            FreeWApplicationFrameTextCatalog.PrintLayout.HelpText,
            RibbonCommandIconKind.PrintLayout,
            DocumentViewMode.PrintLayout);
        _webLayoutSwitch = ViewToggle(
            FreeWApplicationFrameTextCatalog.WebLayoutLabel,
            UiText.Get("View_WebLayout_WpfHelpText"),
            RibbonCommandIconKind.WebLayout,
            DocumentViewMode.WebLayout);
        _draftSwitch = ViewToggle(
            FreeWApplicationFrameTextCatalog.Draft.Label,
            FreeWApplicationFrameTextCatalog.Draft.HelpText,
            RibbonCommandIconKind.Draft,
            DocumentViewMode.Draft);
        // PagedEdit has its own toggle (enter/exit), not routed through SetViewMode(mode), because
        // the paged surface is a separate workspace child swap — not a DocumentView mode change.
        _pagedEditSwitch = new ToggleButton
        {
            Content = StatusViewIcon(RibbonCommandIconKind.PrintLayout),
            Style = (Style)FindResource("ChromeStatusToggleButtonStyle"),
            Width = 24,
            Height = 22,
            Padding = new Thickness(4, 2, 4, 2),
            Margin = new Thickness(1, 2, 1, 2),
            ToolTip = UiText.Get("View_PageEdit_WpfHelpText")
        };
        AutomationProperties.SetName(_pagedEditSwitch, FreeWApplicationFrameTextCatalog.PageEditLabel);
        AutomationProperties.SetHelpText(_pagedEditSwitch, UiText.Get("View_PageEdit_WpfHelpText"));
        _pagedEditSwitch.Click += (_, _) => TogglePagedEditView();

        panel.Children.Add(ViewButton(
            FreeWApplicationFrameTextCatalog.ReadMode.Label,
            FreeWApplicationFrameTextCatalog.ReadMode.HelpText,
            RibbonCommandIconKind.ReadMode,
            ToggleReadMode));
        panel.Children.Add(_printLayoutSwitch);
        panel.Children.Add(_webLayoutSwitch);
        panel.Children.Add(_draftSwitch);
        panel.Children.Add(_pagedEditSwitch);
        return panel;
    }

    private static FrameworkElement StatusViewIcon(RibbonCommandIconKind icon) =>
        Free.Shared.Ribbon.Wpf.RibbonIconFactory.CreateIcon(new RibbonCommandIcon(icon), 13, Brushes.White);

    // The left navigation pane: a header plus a ListBox of heading outline entries (indented by level).
    // Collapsed by default; ToggleNavPane shows/hides it. Selecting an entry scrolls that heading into
    // view in the editor and moves the caret there (see RefreshOutline / OnOutlineSelected).
    private UIElement BuildNavPane()
    {
        _navList = new ListBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent
        };
        _navList.SelectionChanged += OnOutlineSelected;

        // Right-click an outline entry to restructure it: Promote/Demote change the heading's style
        // (reversible, like the styles dropdown) and Collapse/Expand hide/show its body in the editor
        // view only. Each item maps the selected OutlineEntry's model block index onto the DocumentView.
        _navList.ContextMenu = BuildOutlineContextMenu();

        var header = new TextBlock
        {
            Text = NavigationPaneTextCatalog.Resolve(UiText.Get).Title,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(10, 8, 10, 6)
        };

        var searchArea = BuildNavSearch();

        var layout = new DockPanel { Width = 240 };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(searchArea, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(searchArea);
        layout.Children.Add(_navList);

        _navPane = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Visibility = Visibility.Collapsed,
            Child = layout
        };
        return _navPane;
    }

    // The "Search document" area at the top of the navigation pane: a text box plus a Prev/Next/count
    // row. Typing recomputes the document matches (TextSearch over every body paragraph) and jumps to
    // the first; Prev/Next step through them. Built once and reused; wired to the live editor model.
    private UIElement BuildNavSearch()
    {
        _navSearch = new TextBox
        {
            Margin = new Thickness(10, 0, 10, 4),
            Padding = new Thickness(2, 1, 2, 1),
            ToolTip = NavigationPaneTextCatalog.Resolve(UiText.Get).SearchDocument
        };
        _navSearch.TextChanged += (_, _) => RunNavSearch();
        _navSearch.KeyDown += (_, e) =>
        {
            // Enter advances to the next match (Shift+Enter to the previous), mirroring Word's nav search.
            if (e.Key == Key.Enter)
            {
                StepNavSearch(forward: (Keyboard.Modifiers & ModifierKeys.Shift) == 0);
                e.Handled = true;
            }
        };

        var text = NavigationPaneTextCatalog.Resolve(UiText.Get);
        _navSearchPrev = new Button { Content = "‹", Width = 22, Padding = new Thickness(0), ToolTip = text.PreviousMatch };
        _navSearchNext = new Button { Content = "›", Width = 22, Padding = new Thickness(0), ToolTip = text.NextMatch };
        _navSearchPrev.Click += (_, _) => StepNavSearch(forward: false);
        _navSearchNext.Click += (_, _) => StepNavSearch(forward: true);

        _navSearchStatus = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60))
        };

        var controls = new DockPanel { Margin = new Thickness(10, 0, 10, 6) };
        DockPanel.SetDock(_navSearchPrev, Dock.Left);
        DockPanel.SetDock(_navSearchNext, Dock.Left);
        controls.Children.Add(_navSearchPrev);
        controls.Children.Add(_navSearchNext);
        controls.Children.Add(_navSearchStatus);

        var area = new StackPanel();
        area.Children.Add(_navSearch);
        area.Children.Add(controls);
        UpdateNavSearchStatus();
        return area;
    }

    // Recompute the set of body blocks that contain the search term (reusing the pure TextSearch helper),
    // jump to the first hit, filter the outline to relevant headings, and refresh the Prev/Next/count row.
    // An empty term clears the search and shows the full outline again.
    private void RunNavSearch()
    {
        RenderNavigationOutcome(_navigationPaneSession.SetQuery(_navSearch?.Text));
    }

    // Move to the next/previous document match (wrapping at the ends) and bring it into view. No-op when
    // there are no matches.
    private void StepNavSearch(bool forward)
    {
        RenderNavigationOutcome(_navigationPaneSession.StepSearch(forward ? 1 : -1));
    }

    // Update the "n of m" / "No matches" status label and enable the Prev/Next buttons accordingly.
    private void UpdateNavSearchStatus()
    {
        if (_navSearchStatus is null)
            return;

        var state = _navigationPaneSession.State;
        _navSearchStatus.Text = state.SearchStatusText;
        _navSearchPrev.IsEnabled = state.CanStepSearch;
        _navSearchNext.IsEnabled = state.CanStepSearch;
    }

    // WPF's model is committed before a portable pane session projects it.
    private TextDocument CurrentPaneDocument()
    {
        _editor.CommitToModel();
        return _editor.Model;
    }

    // Show/hide the navigation pane and push the new checked-state into the ribbon state store so the
    // View > Navigation Pane toggle button stays in sync. Refreshes the outline when the pane appears.
    private void ToggleNavPane()
    {
        _navPaneVisible = !_navPaneVisible;
        _navPane.Visibility = _navPaneVisible ? Visibility.Visible : Visibility.Collapsed;
        _stateStore.SetChecked("freew.nav-pane", _navPaneVisible);
        if (_navPaneVisible)
            RefreshOutline();
    }

    // View > Show > Ruler: Word exposes this as a simple visibility toggle. FreeW's ruler is currently
    // backed by passive page/indent/tab-stop chrome, so this only shows or hides that existing surface.
    private void ToggleRulers()
    {
        _rulersVisible = !_rulersVisible;
        ApplyRulerVisibility();
        _stateStore.SetChecked("freew.ruler", _rulersVisible);
    }

    private void ApplyRulerVisibility()
    {
        if (_hRuler is null || _vRuler is null)
            return;

        var visibility = _rulersVisible && !_outlineMode ? Visibility.Visible : Visibility.Collapsed;
        _hRuler.Visibility = visibility;
        _vRuler.Visibility = visibility;
        _rulerTabSelector.Visibility = visibility;
    }

    private Button BuildRulerTabSelector()
    {
        var button = new Button
        {
            Width = 16,
            Height = 16,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            BorderThickness = new Thickness(0, 0, 1, 1),
            Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 9,
            Focusable = true
        };
        AutomationProperties.SetName(button, UiText.Get("Ruler_TabStopSelector_AutomationName"));

        void Refresh()
        {
            button.Content = _hRuler.SelectedTabStopAlignment switch
            {
                TabStopAlignment.Center => "C",
                TabStopAlignment.Right => "R",
                TabStopAlignment.Decimal => ".",
                _ => "L"
            };
            button.ToolTip = _hRuler.SelectedTabStopAlignment switch
            {
                TabStopAlignment.Center => UiText.Get("Ruler_CenterTab_ToolTip"),
                TabStopAlignment.Right => UiText.Get("Ruler_RightTab_ToolTip"),
                TabStopAlignment.Decimal => UiText.Get("Ruler_DecimalTab_ToolTip"),
                _ => UiText.Get("Ruler_LeftTab_ToolTip")
            };
        }

        button.Click += (_, _) =>
        {
            _hRuler.SelectedTabStopAlignment = _hRuler.SelectedTabStopAlignment switch
            {
                TabStopAlignment.Left => TabStopAlignment.Center,
                TabStopAlignment.Center => TabStopAlignment.Right,
                TabStopAlignment.Right => TabStopAlignment.Decimal,
                _ => TabStopAlignment.Left
            };
            Refresh();
        };

        Refresh();
        return button;
    }

    // The Reveal Formatting pane: a header plus a scrollable, read-only list of the FONT / PARAGRAPH /
    // SECTION formatting in effect at the caret. Collapsed by default; ToggleRevealFormatting shows/hides
    // it. The content is rebuilt on every selection change from the pure RevealFormatting describer (see
    // RefreshRevealFormatting), so the pane never touches the model. Mirrors BuildNavPane's dock/chrome.
    private UIElement BuildRevealPane()
    {
        var header = new TextBlock
        {
            Text = UiText.Get("Pane_RevealFormatting_Heading"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(10, 8, 10, 6)
        };

        _revealContent = new StackPanel { Margin = new Thickness(10, 0, 10, 8) };
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _revealContent
        };

        var layout = new DockPanel { Width = 240 };
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(scroll);

        _revealPane = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Visibility = Visibility.Collapsed,
            Child = layout
        };
        return _revealPane;
    }

    // Show/hide the Reveal Formatting pane and keep the View > Reveal Formatting toggle button in sync.
    // Rebuilds the pane's content when it appears so it shows the current selection immediately.
    private void ToggleRevealFormatting()
    {
        _revealPaneVisible = !_revealPaneVisible;
        _revealPane.Visibility = _revealPaneVisible ? Visibility.Visible : Visibility.Collapsed;
        _stateStore.SetChecked("freew.reveal-formatting", _revealPaneVisible);
        if (_revealPaneVisible)
            RefreshRevealFormatting();
    }

    // Rebuild the Reveal Formatting pane from the effective formatting at the caret (run + paragraph +
    // section), via the pure RevealFormatting describer. No-op when the pane is hidden, so selection
    // churn while the pane is closed costs nothing. Read-only: never commits or mutates the model.
    private void RefreshRevealFormatting()
    {
        if (_revealContent is null || !_revealPaneVisible)
            return;

        var sections = RevealFormatting.Describe(
            _editor.CurrentRunFormatting, _editor.CurrentParagraphFormatting, _editor.Model.Page);

        _revealContent.Children.Clear();
        foreach (var section in sections)
        {
            _revealContent.Children.Add(new TextBlock
            {
                Text = section.Heading,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(WpfThemeApplier.ToColor(Program.ActiveTheme.Colors.AccentDark)),
                Margin = new Thickness(0, 10, 0, 4)
            });

            foreach (var item in section.Items)
            {
                _revealContent.Children.Add(new TextBlock
                {
                    Text = item.Label,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60)),
                    Margin = new Thickness(0, 4, 0, 0)
                });
                _revealContent.Children.Add(new TextBlock
                {
                    Text = item.Value,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(8, 0, 0, 2)
                });
            }
        }
    }

    // The Reviewing Pane: a header, an Accept/Reject/Prev/Next toolbar, a sort control, a status line
    // ("N changes"), and a scrollable list of every tracked change (author • type, plus the affected
    // text). Collapsed by default; ToggleReviewPane shows/hides it. Selecting an entry jumps the editor
    // to that change (click-to-navigate) and the toolbar acts on the SELECTED single revision. Content
    // is rebuilt from the pure RevisionList (see RefreshReviewPane), so the pane never owns revision
    // logic. Mirrors BuildRevealPane's dock/chrome.
    private static readonly ReviewingPanePresentationDescriptor WpfReviewingPanePresentation =
        ReviewingPanePresentationPlanner.For(ReviewingPanePresentationProfile.CompactWpf);

    private UIElement BuildReviewPane()
    {
        var header = new TextBlock
        {
            Text = WpfReviewingPanePresentation.PaneTitle,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(10, 8, 10, 6)
        };

        Button MakeButton(string text, string tip, System.Action onClick)
        {
            var button = new Button
            {
                Content = text,
                ToolTip = tip,
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 0, 4, 0),
                MinWidth = 28
            };
            button.Click += (_, _) => onClick();
            return button;
        }

        var toolbar = new WrapPanel { Margin = new Thickness(10, 0, 10, 6) };
        _reviewAcceptButton = MakeButton(
            WpfReviewingPanePresentation.Actions.AcceptSelected.Label,
            WpfReviewingPanePresentation.Actions.AcceptSelected.ToolTip,
            AcceptSelectedRevision);
        _reviewRejectButton = MakeButton(
            WpfReviewingPanePresentation.Actions.RejectSelected.Label,
            WpfReviewingPanePresentation.Actions.RejectSelected.ToolTip,
            RejectSelectedRevision);
        _reviewPreviousButton = MakeButton(
            WpfReviewingPanePresentation.Actions.Previous.Label,
            WpfReviewingPanePresentation.Actions.Previous.ToolTip,
            () => StepRevision(-1));
        _reviewNextButton = MakeButton(
            WpfReviewingPanePresentation.Actions.Next.Label,
            WpfReviewingPanePresentation.Actions.Next.ToolTip,
            () => StepRevision(+1));
        toolbar.Children.Add(_reviewAcceptButton);
        toolbar.Children.Add(_reviewRejectButton);
        toolbar.Children.Add(_reviewPreviousButton);
        toolbar.Children.Add(_reviewNextButton);

        // Sort control: reorders the Reviewing Pane without touching the document model.
        var sortRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(10, 0, 10, 4)
        };
        sortRow.Children.Add(new TextBlock
        {
            Text = WpfReviewingPanePresentation.SortLabel,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        });
        var sortCombo = new ComboBox { MinWidth = 130 };
        foreach (var option in WpfReviewingPanePresentation.SortOptions)
            sortCombo.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Order });
        sortCombo.SelectedIndex = 0;
        sortCombo.SelectionChanged += (_, _) =>
        {
            if (sortCombo.SelectedItem is ComboBoxItem { Tag: ReviewRevisionSortOrder order })
                RenderReviewOutcome(_reviewingPaneSession.SetSortOrder(order));
        };
        sortRow.Children.Add(sortCombo);

        _reviewStatus = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60)),
            Margin = new Thickness(10, 0, 10, 6)
        };

        _reviewList = new ListBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Margin = new Thickness(4, 0, 4, 8)
        };
        // Selecting an entry navigates the editor to that change (click-to-navigate).
        _reviewList.SelectionChanged += OnReviewSelectionChanged;

        var layout = new DockPanel { Width = 270 };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(sortRow, Dock.Top);
        DockPanel.SetDock(_reviewStatus, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(toolbar);
        layout.Children.Add(sortRow);
        layout.Children.Add(_reviewStatus);
        layout.Children.Add(_reviewList);

        _reviewPane = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Visibility = Visibility.Collapsed,
            Child = layout
        };
        return _reviewPane;
    }

    // Show/hide the Reviewing Pane and keep the Review > Reviewing Pane toggle button in sync. Rebuilds the
    // list when it appears so it reflects the current document immediately.
    private void ToggleReviewPane()
    {
        _reviewPaneVisible = !_reviewPaneVisible;
        _reviewPane.Visibility = _reviewPaneVisible ? Visibility.Visible : Visibility.Collapsed;
        _stateStore.SetChecked("freew.reviewing-pane", _reviewPaneVisible);
        if (_reviewPaneVisible)
            RefreshReviewPane();
    }

    // Rebuild the Reviewing Pane's list from the document's tracked changes (the pure RevisionList via
    // DocumentView.ListRevisions). Applies the active sort order before building list items. No-op when
    // the pane is hidden, so editing churn while it is closed costs nothing. Tries to preserve the
    // selected position so Accept/Reject keeps focus on the next change.
    private void RefreshReviewPane()
    {
        if (_reviewList is null || !_reviewPaneVisible)
            return;

        RenderReviewOutcome(_reviewingPaneSession.Refresh());
    }

    private void RenderReviewOutcome(ReviewingPaneOutcome outcome)
    {
        var state = outcome.State;
        _reviewList.SelectionChanged -= OnReviewSelectionChanged;
        _reviewList.Items.Clear();
        foreach (var entry in state.Entries)
            _reviewList.Items.Add(BuildRevisionItem(entry));

        _reviewStatus.Text = ReviewingPanePresentationPlanner.BuildCountText(
            state.Entries.Count,
            ReviewingPanePresentationProfile.CompactWpf);
        _reviewAcceptButton.IsEnabled = state.CanResolveSelected;
        _reviewRejectButton.IsEnabled = state.CanResolveSelected;
        _reviewPreviousButton.IsEnabled = state.HasRevisions;
        _reviewNextButton.IsEnabled = state.HasRevisions;
        _reviewList.SelectedIndex = state.SelectedIndex;
        _reviewList.SelectionChanged += OnReviewSelectionChanged;

        if (outcome.NavigateToRevision is { } target)
        {
            _editor.NavigateToRevision(target);
            _reviewList.ScrollIntoView(_reviewList.SelectedItem);
        }
    }

    private void OnReviewSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var outcome = _reviewingPaneSession.SelectIndex(_reviewList.SelectedIndex);
        if (outcome.NavigateToRevision is { } target)
            _editor.NavigateToRevision(target);
    }

    // One reviewing-pane row: a bold "Author • Type" caption over the affected text (wrapped, dimmed).
    private static UIElement BuildRevisionItem(RevisionEntry entry)
    {
        var presentation = ReviewingPanePresentationPlanner.BuildRevision(
            entry,
            ReviewingPanePresentationProfile.CompactWpf);

        var panel = new StackPanel { Margin = new Thickness(6, 4, 6, 4) };
        panel.Children.Add(new TextBlock
        {
            Text = presentation.CaptionText,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(WpfThemeApplier.ToColor(Program.ActiveTheme.Colors.AccentDark))
        });
        if (presentation.SnippetText.Length > 0)
            panel.Children.Add(new TextBlock
            {
                Text = presentation.SnippetText,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50))
            });
        return panel;
    }

    // Accept the single revision selected in the Reviewing Pane, then rebuild the list (which slides the
    // selection onto the next pending change). No-op when nothing is selected.
    private void AcceptSelectedRevision()
    {
        var outcome = _reviewingPaneSession.AcceptSelected();
        if (outcome.MutationApplied)
        {
            _file.MarkDirty();
            UpdateCounts();
        }
        RenderReviewOutcome(outcome);
    }

    // Reject the single revision selected in the Reviewing Pane, then rebuild the list.
    private void RejectSelectedRevision()
    {
        var outcome = _reviewingPaneSession.RejectSelected();
        if (outcome.MutationApplied)
        {
            _file.MarkDirty();
            UpdateCounts();
        }
        RenderReviewOutcome(outcome);
    }

    // Previous/Next change: step the selection through the list (and so navigate the editor, via the list's
    // SelectionChanged handler). Opens the pane first if it is closed. Wraps at the ends.
    private void StepRevision(int direction)
    {
        if (!_reviewPaneVisible)
            ToggleReviewPane();
        RenderReviewOutcome(_reviewingPaneSession.Step(direction));
    }

    // ── Thesaurus Pane ──────────────────────────────────────────────────────────────────────────────

    // Toggle the Thesaurus pane (Review > Proofing > Thesaurus, Shift+F7) and trigger a word lookup.
    // Opens the docked right pane (built by ThesaurusPane.Build()) if it was closed, then looks up the
    // word at the caret in the bundled synonym dictionary. If already open, closes it.
    private void ToggleThesaurusPane()
    {
        _thesaurusPane.Toggle();
    }

    // ── Balloon Overlay ──────────────────────────────────────────────────────────────────────────────

    // Toggle Review > Show Markup > Show Revisions in Balloons. Enables or disables the right-margin
    // balloon strip (BalloonOverlay) and rebuilds it immediately when enabling.
    private void ToggleBalloons()
    {
        _balloonOverlay.Toggle();
        // Print/Print Preview/PDF/XPS export (PrintLayout.BuildPaginator) read this flag so the
        // printed page reserves and draws the same balloon strip shown on screen (Word's behaviour).
        _editor.ShowMarkupBalloons = _balloonOverlay.BalloonsEnabled;
        _stateStore.SetChecked("freew.show-markup-balloons", _balloonOverlay.BalloonsEnabled);
    }

    // ── Notes Pane (Feature 1A-1C) ──────────────────────────────────────────────────────────────────

    // Build the Notes pane: header, a ListBox of note stubs (Kind + id + first line), a small sub-editor
    // for rich editing of the selected note, Apply and Delete buttons. Docked at the bottom; collapsed
    // by default. Mirrors BuildReviewPane's pattern exactly: the pane never owns note logic; it reads
    // and writes Footnotes/Endnotes via the model.
    private UIElement BuildNotesPane()
    {
        var header = new TextBlock
        {
            Text = FreeWUiTextCatalog.NotesHeading,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(10, 8, 10, 4)
        };

        _notesList = new ListBox
        {
            MinHeight = 60,
            MaxHeight = 100,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(8, 0, 8, 4)
        };

        _notesSelectedLabel = new TextBlock
        {
            Text = string.Empty,
            FontStyle = FontStyles.Italic,
            Foreground = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50)),
            Margin = new Thickness(10, 0, 10, 2),
            Visibility = Visibility.Collapsed
        };

        // Sub-editor: a second DocumentView with its own undo stack — editing here never pollutes the
        // main editor's undo. It is sized small so it fits in the bottom pane without crowding.
        _notesSubEditor = new DocumentView
        {
            MinHeight = 80,
            MaxHeight = 160,
            Margin = new Thickness(8, 0, 8, 4),
            Visibility = Visibility.Collapsed
        };

        Button MakeButton(string text, string tip, System.Action onClick, bool isPrimary = false)
        {
            var btn = new Button
            {
                Content = text,
                ToolTip = tip,
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 0, 6, 0),
                MinWidth = 60
            };
            if (isPrimary)
                btn.FontWeight = FontWeights.SemiBold;
            btn.Click += (_, _) => onClick();
            return btn;
        }

        _notesApplyButton = MakeButton(
            FreeWUiTextCatalog.NotesApply,
            FreeWUiTextCatalog.NotesApplyToolTip,
            ApplySelectedNote,
            isPrimary: true);
        _notesDeleteButton = MakeButton(
            FreeWUiTextCatalog.NotesDelete,
            FreeWUiTextCatalog.NotesDeleteToolTip,
            DeleteSelectedNote);
        _notesApplyButton.Visibility  = Visibility.Collapsed;
        _notesDeleteButton.Visibility = Visibility.Collapsed;

        var toolbar = new WrapPanel { Margin = new Thickness(10, 0, 10, 6) };
        toolbar.Children.Add(_notesApplyButton);
        toolbar.Children.Add(_notesDeleteButton);

        // Selecting a stub loads the note's content into the sub-editor.
        _notesList.SelectionChanged += OnNoteSelectionChanged;

        var layout = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(_notesList, Dock.Top);
        DockPanel.SetDock(_notesSelectedLabel, Dock.Top);
        DockPanel.SetDock(toolbar, Dock.Bottom);
        layout.Children.Add(header);
        layout.Children.Add(_notesList);
        layout.Children.Add(_notesSelectedLabel);
        layout.Children.Add(toolbar);
        layout.Children.Add(_notesSubEditor);  // fill

        _notesPane = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xFD, 0xFD, 0xFD)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Visibility = Visibility.Collapsed,
            Child = layout
        };
        return _notesPane;
    }

    // Toggle the Notes pane, keeping freew.show-notes ribbon state in sync.
    private void ToggleNotesPane()
    {
        _notesPaneVisible = !_notesPaneVisible;
        _notesPane.Visibility = _notesPaneVisible ? Visibility.Visible : Visibility.Collapsed;
        _stateStore.SetChecked("freew.show-notes", _notesPaneVisible);
        if (_notesPaneVisible)
            RefreshNotesPane();
    }

    // Rebuild the stub list from the model's Footnotes + Endnotes dicts.
    // No-op when the pane is hidden. Tries to preserve the selected position.
    private void RefreshNotesPane()
    {
        if (_notesList is null || !_notesPaneVisible)
            return;

        RenderNotesOutcome(_documentNotesPaneSession.Refresh());
    }

    // Load the note selected in the stub list into the sub-editor.
    private void OnNoteSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RenderNotesOutcome(_documentNotesPaneSession.SelectIndex(_notesList.SelectedIndex));
    }

    private void RenderNotesOutcome(DocumentNotesPaneOutcome outcome)
    {
        var state = outcome.State;
        _notesList.SelectionChanged -= OnNoteSelectionChanged;
        _notesList.ItemsSource = state.Items;
        _notesList.SelectedIndex = state.SelectedIndex;
        _notesList.SelectionChanged += OnNoteSelectionChanged;

        _notesSelectedLabel.Text = state.SelectedNote?.Label ?? string.Empty;
        _notesSelectedLabel.Visibility = state.HasSelection ? Visibility.Visible : Visibility.Collapsed;
        _notesSubEditor.Visibility = state.HasSelection ? Visibility.Visible : Visibility.Collapsed;
        _notesApplyButton.Visibility = state.CanApply ? Visibility.Visible : Visibility.Collapsed;
        _notesDeleteButton.Visibility = state.CanDelete ? Visibility.Visible : Visibility.Collapsed;
        if (state.EditorDocument is { } editorDocument)
        {
            ConfigureFieldEvaluationContext(_notesSubEditor);
            _notesSubEditor.LoadModel(editorDocument);
        }
    }

    // Apply edits from the sub-editor back to the selected note's Content, then re-render the main editor
    // so marker tooltips (which show the note's plain text) refresh.
    private void ApplySelectedNote()
    {
        _notesSubEditor.CommitToModel();
        var outcome = _documentNotesPaneSession.Apply(_notesSubEditor.Model.Blocks);
        if (outcome.MutationApplied)
            _file.MarkDirty();
        RenderNotesOutcome(outcome);
    }

    // Delete the selected note from the model and strip its marker run from the body, then refresh.
    private void DeleteSelectedNote()
    {
        var outcome = _documentNotesPaneSession.DeleteSelected();
        if (outcome.MutationApplied)
            _file.MarkDirty();
        RenderNotesOutcome(outcome);
    }

    // Lightweight stub for the Notes pane's list. Carries enough for display + selection → load.
    // ── Header/Footer Pane (Feature 2A) ─────────────────────────────────────────────────────────────

    // Build the Header/Footer pane: a slot-label, a DocumentView sub-editor for rich editing
    // (preserving bold/italic/colour/page-number fields), and a "Close Header and Footer" button.
    // Docked at the bottom; collapsed by default.
    private UIElement BuildHeaderFooterPane()
    {
        _hfSlotLabel = new TextBlock
        {
            Text = string.Empty,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(10, 6, 10, 4)
        };

        _hfSubEditor = new DocumentView
        {
            MinHeight = 80,
            MaxHeight = 200,
            Margin = new Thickness(8, 0, 8, 4)
        };

        var closeBtn = new Button
        {
            Content = UiText.Get("HeaderFooter_Close_Label"),
            Padding = new Thickness(10, 3, 10, 3),
            Margin = new Thickness(10, 4, 10, 6),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        closeBtn.Click += (_, _) => CloseHeaderFooterPane();

        var layout = new DockPanel();
        DockPanel.SetDock(_hfSlotLabel, Dock.Top);
        DockPanel.SetDock(closeBtn,     Dock.Bottom);
        layout.Children.Add(_hfSlotLabel);
        layout.Children.Add(closeBtn);
        layout.Children.Add(_hfSubEditor);   // fill

        _hfPane = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF8, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xA0, 0xB8, 0xD8)),
            BorderThickness = new Thickness(0, 2, 0, 0),
            Visibility = Visibility.Collapsed,
            Child = layout
        };
        return _hfPane;
    }

    // Open the Header/Footer pane for the named slot, loading its Paragraphs into the sub-editor.
    // Preserves run formatting (bold/italic/colour/field runs) that the old plain-text dialog lost.
    //
    // Phase 4 (DEBUG): when PagedEdit is active, route to the in-page header/footer region instead
    // of opening the docked pane, so the in-page sub-editor gets focus. The docked pane remains
    // for the non-paged modes.
    private void OpenHeaderFooterPane(string slotName) =>
        OpenHeaderFooterPane(HeaderFooterDialogPlanner.ParseSlot(slotName));

    private void OpenHeaderFooterPane(HeaderFooterSlotKind slot)
    {
        if (_pagedEditMode && _pagedEditPanel is not null)
        {
            // Route to the in-page region; if the slot is not visible (e.g. "even-header" when
            // DifferentOddEvenPages is off) FocusInPageHfRegion returns false and we fall through
            // to the docked pane as a fallback.
            if (_pagedEditPanel.FocusInPageHfRegion(slot))
                return;
        }
        _hfActiveSlot = slot;

        var label = HeaderFooterDialogPlanner.LabelFor(slot);
        _hfSlotLabel.Text = UiText.Format("HeaderFooter_Editing_Status_Format", label);

        var hf = _editor.Model.FinalSectionHeadersFooters;
        var current = HeaderFooterDialogPlanner.GetSlot(hf, slot);

        // Wrapper document — seeded with the main doc's DefaultRun so fonts match.
        var wrapper = TextDocument.CreateEmpty();
        wrapper.DefaultRun       = _editor.Model.DefaultRun;
        wrapper.DefaultParagraph = _editor.Model.DefaultParagraph;
        wrapper.Blocks.Clear();

        if (current is not null)
        {
            foreach (var para in current.Paragraphs)
                wrapper.Blocks.Add(para);
        }
        if (wrapper.Blocks.Count == 0)
            wrapper.Blocks.Add(new Paragraph());

        ConfigureFieldEvaluationContext(_hfSubEditor);
        _hfSubEditor.LoadModel(wrapper);

        _hfPane.Visibility = Visibility.Visible;
    }

    // Close (commit) the Header/Footer pane: commit the sub-editor's edits back to the slot in the
    // model, hide the pane, and re-render the main editor. Mirrors the "Close Header and Footer" button
    // and the freew.hf-close command.
    private void CloseHeaderFooterPane()
    {
        if (_hfActiveSlot is null)
        {
            _hfPane.Visibility = Visibility.Collapsed;
            return;
        }

        _hfSubEditor.CommitToModel();

        // Build a new HeaderFooter from the sub-editor's blocks.
        var hfOut = new HeaderFooter();
        foreach (var block in _hfSubEditor.Model.Blocks.OfType<Paragraph>())
            hfOut.Paragraphs.Add(block);

        // Write back to the correct slot.
        var hf = _editor.Model.FinalSectionHeadersFooters;
        HeaderFooterDialogPlanner.SetSlot(hf, _hfActiveSlot.Value, hfOut);

        _hfActiveSlot  = null;
        _hfPane.Visibility = Visibility.Collapsed;

        // Re-render the main editor and return focus (no-op page-settings commit triggers
        // CommitToModel + Render inside ApplyPageSettings without changing any setting).
        _editor.ApplyPageSettings(_ => { });
        _file.MarkDirty();
        _editor.Focus();
    }

    // Read mode (distraction-free view): hide the ribbon, title bar, navigation pane, and the status
    // bar's non-essential extras (data folder + zoom), then constrain the editor to a centered, roomy
    // reading column. Toggling off restores every hidden element to exactly what it was before (including
    // whether the nav pane had been open) and returns the editor to its original full-width presentation.
    // The toggle state is mirrored into the shared RibbonStateStore so the View > Read Mode button stays
    // in sync, exactly like the navigation-pane toggle.
    private void ToggleReadMode()
    {
        var plan = _editorInteraction.ToggleReadMode(new FreeWEditorChromeVisibility(
            TitleBar: ToChromeVisibility(_titleBar.Visibility),
            Ribbon: ToChromeVisibility(_ribbon.Visibility),
            DataFolder: ToChromeVisibility(_dataFolderItem.Visibility),
            ViewSwitch: ToChromeVisibility(_viewSwitchItem.Visibility),
            Zoom: ToChromeVisibility(_zoomItem.Visibility),
            NavigationPane: ToChromeVisibility(_navPaneVisible),
            RevealPane: ToChromeVisibility(_revealPaneVisible),
            ReviewingPane: ToChromeVisibility(_reviewPaneVisible)));

        if (plan.IsActive)
        {
            _editorMarginBeforeReadMode = _editor.Margin;
            _editorMaxWidthBeforeReadMode = _editor.MaxWidth;
            _editorAlignmentBeforeReadMode = _editor.HorizontalAlignment;
            _editorWidthBeforeReadMode = _editor.Width;
            _editorEffectBeforeReadMode = _editor.Effect;
            _editorBackgroundBeforeReadMode = _editor.Background;
        }

        _titleBar.Visibility = ToVisibility(plan.Chrome.TitleBar);
        _ribbon.Visibility = ToVisibility(plan.Chrome.Ribbon);
        _dataFolderItem.Visibility = ToVisibility(plan.Chrome.DataFolder);
        _viewSwitchItem.Visibility = ToVisibility(plan.Chrome.ViewSwitch);
        _zoomItem.Visibility = ToVisibility(plan.Chrome.Zoom);
        _navPane.Visibility = ToVisibility(plan.Chrome.NavigationPane);
        _revealPane.Visibility = ToVisibility(plan.Chrome.RevealPane);
        _reviewPane.Visibility = ToVisibility(plan.Chrome.ReviewingPane);

        if (plan.IsActive)
        {
            _editor.HorizontalAlignment = HorizontalAlignment.Center;
            _editor.Width = double.NaN;
            _editor.Effect = null;
            _editor.MaxWidth = plan.ColumnWidth;
            _editor.Margin = new Thickness(40, 40, 40, 40);
            _editor.Background = ReadModeBrush(plan.PageColorHex);
        }
        else
        {
            _editor.HorizontalAlignment = _editorAlignmentBeforeReadMode;
            _editor.MaxWidth = _editorMaxWidthBeforeReadMode;
            _editor.Margin = _editorMarginBeforeReadMode;
            _editor.Width = _editorWidthBeforeReadMode;
            _editor.Effect = _editorEffectBeforeReadMode;
            _editor.Background = _editorBackgroundBeforeReadMode;
        }

        _stateStore.SetChecked("freew.read-mode", plan.IsActive);
    }

    private static FreeWChromeVisibility ToChromeVisibility(bool isVisible) =>
        isVisible ? FreeWChromeVisibility.Visible : FreeWChromeVisibility.Collapsed;

    private static FreeWChromeVisibility ToChromeVisibility(Visibility visibility) => visibility switch
    {
        Visibility.Visible => FreeWChromeVisibility.Visible,
        Visibility.Hidden => FreeWChromeVisibility.Hidden,
        _ => FreeWChromeVisibility.Collapsed,
    };

    private static Visibility ToVisibility(FreeWChromeVisibility visibility) => visibility switch
    {
        FreeWChromeVisibility.Visible => Visibility.Visible,
        FreeWChromeVisibility.Hidden => Visibility.Hidden,
        _ => Visibility.Collapsed,
    };

    // Feature 4 — Read Mode column width: Narrow (560 px) / Default (760 px) / Wide (1024 px).
    // Stores the token and, if read mode is currently active, applies the new max-width immediately.
    private void ApplyReadModeColumnWidth(string token)
    {
        var plan = _editorInteraction.UpdateReadModeColumnWidth(token);
        if (plan.ApplyImmediately)
            _editor.MaxWidth = plan.ColumnWidth;
    }

    // Feature 4 — Read Mode page color: None (white), Sepia (#F0E0C0), or Inverse (dark #1E1E1E).
    // Stores the token and, if read mode is currently active, tints the editor background immediately.
    private void ApplyReadModePageColor(string token)
    {
        var plan = _editorInteraction.UpdateReadModePageColor(token);
        if (plan.ApplyImmediately)
            _editor.Background = ReadModeBrush(plan.PageColorHex);
    }

    private static System.Windows.Media.Brush ReadModeBrush(string colorHex) =>
        new System.Windows.Media.SolidColorBrush(
            WpfRgbColorAdapter.ParseColorToken(colorHex));

    // Feature 5 — New Window: shared code snapshots document/file state and assigns the view number;
    // WPF only creates the native window and loads the resulting plan.
    private void OpenNewWindow()
    {
        _editor.CommitToModel();
        var plan = _documentWindowPlanner.CreateNext(_editor.Model, _file.CurrentPath, _file.IsDirty);
        var newWindow = new MainWindow(
            _options,
            _optionsStore,
            _messageService,
            documentWindowPlanner: _documentWindowPlanner,
            documentWindowNumber: plan.WindowNumber);
        newWindow._file.LoadDocumentWindow(plan);
        newWindow.Show();
    }

    // R133-remediation: AutosaveCoordinator.OfferRecovery/RecoverUnsavedDocuments call this for
    // every accepted recovery candidate beyond the first (which restores directly into the window
    // that is already open) -- mirrors OpenNewWindow()'s window-creation pattern above so each
    // additional pending snapshot from a multi-window crash gets its own window instead of being
    // silently left on disk. suppressStartupRecoveryOffer:true stops the new window's own Loaded
    // handler from re-running OfferRecovery and re-prompting for the very candidates the caller's
    // loop is already working through.
    private bool OpenNewWindowWithRecoveredSnapshot(AutosaveRecoveryCandidate candidate)
    {
        var newWindow = new MainWindow(_options, messageService: _messageService, suppressStartupRecoveryOffer: true);
        var loaded = newWindow._file.OpenSnapshot(candidate.SnapshotPath, candidate.Sidecar.OriginalFilePath);
        newWindow.Show();
        newWindow.Activate();
        return loaded;
    }

    // R133-wpf-startup-file-args: opens every startup file argument beyond the first (already opened
    // synchronously into this window's constructor), each in its own new window -- mirrors
    // OpenNewWindow()'s window-creation pattern above so multiple files dragged onto the taskbar icon
    // in one launch (delivered as multiple path arguments to a single process) all open, instead of
    // every argument after the first being silently dropped.
    private void OpenAdditionalStartupFiles(IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
        {
            var newWindow = new MainWindow(_options, messageService: _messageService);
            newWindow.Show();
            newWindow._file.OpenPath(path);
        }
    }

    private void OpenMailMergeErrorReport(TextDocument report)
    {
        var reportWindow = new MainWindow(_options, messageService: _messageService);
        reportWindow._editor.LoadModel(report);
        reportWindow.Title = FreeWUiTextCatalog.MailMergeErrorReportWindowTitle;
        reportWindow.Show();
        reportWindow._editor.Focus();
    }

    // Feature 5 — Arrange All: tile all open FreeW windows across the work area.
    // Uses SystemParameters.WorkArea so the taskbar is not covered.
    private static void ArrangeAllWindows()
    {
        var freeWWindows = System.Windows.Application.Current.Windows
            .OfType<MainWindow>()
            .Where(w => w.IsVisible)
            .ToList();

        if (freeWWindows.Count == 0) return;

        var area = System.Windows.SystemParameters.WorkArea;
        var count = freeWWindows.Count;

        var bounds = ArrangeAllLayoutPlanner.ArrangeRowFirst(
            area.Width,
            area.Height,
            count,
            maxColumns: 3);

        for (var i = 0; i < bounds.Count; i++)
        {
            var w = freeWWindows[i];
            var bound = bounds[i];
            w.WindowState = System.Windows.WindowState.Normal;
            w.Left   = area.Left + bound.X;
            w.Top    = area.Top  + bound.Y;
            w.Width  = bound.Width;
            w.Height = bound.Height;
        }
    }

    // View > Views: switch the editing surface to one of the three mutually-exclusive print-family view
    // modes — Print Layout (the Word page sheet), Web Layout (a continuous, full-width view with no page
    // chrome, text wrapping to the window like a web page) or Draft (a simplified continuous view for fast
    // editing). DocumentView owns the page presentation; here we drive that, leave any Outline overlay
    // (which replaces the surface) so the chosen view is actually visible, and refresh the ribbon + status
    // bar so exactly one mode reads as active. Switching never mutates the model.
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
            ApplyViewDepthTransition(_viewSession.RestoreLiveEditor());

        if (plan.ExitPagedEditMode)
            ExitPagedEdit();

        _editor.SetViewMode(plan.TargetMode);
        RefreshViewModeChecks();
    }

    // Push the active view mode into the shared RibbonStateStore (so the View ribbon's Print Layout /
    // Web Layout / Draft / Page Edit toggle buttons reflect it) and the status-bar toggle buttons.
    // Exactly one is checked at a time — PagedEdit has its own surface and is mutually exclusive with
    // the continuous print-family modes. Outline mode clears the print-family checks. Mirrors how the
    // read-mode / nav-pane toggles keep their buttons in sync.
    private void RefreshViewModeChecks()
    {
        var plan = CurrentDocumentViewChecks();

        _stateStore.SetChecked("freew.print-layout", plan.PrintLayout);
        _stateStore.SetChecked("freew.web-layout", plan.WebLayout);
        _stateStore.SetChecked("freew.draft-view", plan.Draft);
        _stateStore.SetChecked("freew.paged-edit-view", plan.PagedEdit);

        if (_printLayoutSwitch is not null) _printLayoutSwitch.IsChecked = plan.PrintLayout;
        if (_webLayoutSwitch is not null) _webLayoutSwitch.IsChecked = plan.WebLayout;
        if (_draftSwitch is not null) _draftSwitch.IsChecked = plan.Draft;
        if (_pagedEditSwitch is not null) _pagedEditSwitch.IsChecked = plan.PagedEdit;
    }

    private FreeWDocumentViewCheckPlan CurrentDocumentViewChecks() =>
        _viewSession.BuildDocumentViewChecks(
            _editor.ViewMode,
            _outlineMode,
            _pagedEditMode);

    // View > Outline: swap the normal editing surface for the heading-structured outline view (and its
    // Outlining mini-toolbar), or back again. Entering hides the workspace + rulers and shows the outline,
    // populated from the live model; exiting restores everything verbatim — the same save/restore shape as
    // Read Mode. Switching views never mutates the model, so toggling back lands on an untouched document.
    // The checked-state is mirrored into the shared RibbonStateStore so the View > Outline button stays in
    // sync, exactly like the Print Layout / Read Mode toggles.
    private void ToggleOutlineView()
    {
        if (_outlineMode)
            LeaveOutlineView();
        else
            EnterOutlineView();
    }

    private void EnterOutlineView()
    {
        var plan = _viewSession.EnterOutline(_pagedEditMode);
        if (plan.ExitPageSurface)
            ApplyViewDepthTransition(_viewSession.RestoreLiveEditor());
        if (plan.ExitPagedEditSurface)
            ExitPagedEdit();

        _outlineMode = plan.IsOutlineMode;
        _workspace.Visibility = Visibility.Collapsed;
        _hRuler.Visibility = Visibility.Collapsed;
        _vRuler.Visibility = Visibility.Collapsed;
        _outlineView.Visibility = Visibility.Visible;
        _outlineView.Refresh();

        RefreshOutlineViewState();
    }

    private void LeaveOutlineView(bool restorePriorView = true)
    {
        if (!_outlineMode)
            return;

        var plan = _viewSession.LeaveOutline(restorePriorView);
        _outlineMode = plan.IsOutlineMode;
        _outlineView.Visibility = Visibility.Collapsed;
        _workspace.Visibility = Visibility.Visible;
        ApplyRulerVisibility();

        if (plan.EnterPagedEditSurface)
            EnterPagedEdit();

        RefreshOutlineViewState();
    }

    private void RefreshOutlineViewState()
    {
        _stateStore.SetChecked("freew.outline-view", _outlineMode);

        // Outline and the print-family views are mutually exclusive: entering Outline clears the Print
        // Layout / Web Layout / Draft checks, and leaving it re-checks whichever the editor is still in.
        RefreshViewModeChecks();
    }

    // ── Multiple Pages / Side to Side ────────────────────────────────────────────────────────────
    // Both modes build a read-only FlowDocumentPageViewer fed by PrintLayout.BuildPaginatedDocument and swap
    // the workspace child from the live workspaceGrid to that viewer. Re-entering any print-family
    // view mode (Print Layout / Web Layout / Draft) restores the live editor via ExitPaginatedView.
    // The two modes are mutually exclusive with each other and with any live-editor overlay mode.

    /// <summary>
    /// Enters (or exits) the Multiple Pages paginated overlay. Commits the editor to the model first so
    /// the viewer reflects the latest content, then swaps the workspace child from the workspaceGrid to
    /// a full-window <see cref="FlowDocumentPageViewer"/> backed by <see cref="PrintLayout.BuildPaginatedDocument"/>.
    /// </summary>
    private void ToggleMultiplePages() =>
        ApplyViewDepthTransition(_viewSession.Execute(FreeWViewDepthCommand.ToggleMultiplePages));

    /// <summary>
    /// Enters (or exits) the Side to Side paginated overlay — same as Multiple Pages but the viewer
    /// is zoomed to fit two pages and exposes shared pair-wise page navigation.
    /// </summary>
    private void ToggleSideToSide() =>
        ApplyViewDepthTransition(_viewSession.Execute(FreeWViewDepthCommand.ToggleSideToSide));

    private void ApplyViewDepthTransition(FreeWViewDepthTransition transition)
    {
        if (transition.ExitSplitSurface)
            ExitSplitView();
        if (transition.ExitPageSurface)
            ExitPaginatedView();

        var plan = transition.Current;
        _editor.ApplyViewDepthLayout(plan.Layout);

        switch (plan.SurfaceKind)
        {
            case FreeWViewDepthSurfaceKind.LiveEditor:
                break;
            case FreeWViewDepthSurfaceKind.SplitEditorWithReadOnlyPreview:
                EnterSplitView();
                break;
            case FreeWViewDepthSurfaceKind.ReadOnlyPagePreview:
                EnterPaginatedView(plan);
                break;
            case FreeWViewDepthSurfaceKind.EditablePageView:
                EnterEditablePaginatedView(plan);
                break;
        }

        SyncViewDepthRibbonState();
    }

    /// <summary>
    /// Enters an editable paginated surface. The existing paginated editor owns page sharding,
    /// cross-page caret routing, and model commit. Multiple Pages uses a fixed two-column grid;
    /// Side to Side uses a horizontal strip plus pair-navigation chrome.
    /// </summary>
    private void EnterEditablePaginatedView(FreeWViewDepthPlan plan)
    {
        _editor.CommitToModel();
        var layoutMode = plan.IsMultiplePagesActive
            ? PaginatedPageLayoutMode.TwoColumnGrid
            : PaginatedPageLayoutMode.Horizontal;
        _editablePaginatedPanel = PaginatedEditorPanel.Build(_editor, layoutMode);

        _workspaceGridChild = _workspace.Child;
        if (plan.IsSideToSideActive)
        {
            _viewSession.StartPagePairNavigation(
                totalPages: _editablePaginatedPanel.PageBoxes.Count);
            _workspace.Child = BuildSideToSideNavigationHost(_editablePaginatedPanel);
            ApplySideToSideNavigationToViewer();
        }
        else
        {
            ResetSideToSideNavigation();
            _workspace.Child = _editablePaginatedPanel;
        }
    }

    /// <summary>
    /// Builds a <see cref="FlowDocumentPageViewer"/> backed by <see cref="PrintLayout.BuildPaginatedDocument"/>
    /// and swaps it in as the workspace child, hiding the live workspaceGrid. The editor is committed to
    /// the model first so the view reflects the latest content. Side-to-Side applies the shared plan's
    /// two-page fit intent; other preview modes leave the page viewer at its default page flow.
    /// </summary>
    private void EnterPaginatedView(FreeWViewDepthPlan plan)
    {
        // Commit so the paginated view reflects the latest edits.
        _editor.CommitToModel();

        var document = PrintLayout.BuildPaginatedDocument(_editor);
        var paginator = ((System.Windows.Documents.IDocumentPaginatorSource)document).DocumentPaginator;
        paginator.ComputePageCount();

        var viewer = new FlowDocumentPageViewer
        {
            Document = document
        };

        var pagesAcross = plan.Layout.PagesAcross > 1 ? plan.Layout.PagesAcross : 0;

        // For two-page-fit preview modes: apply a zoom factor that fits 2 pages side-by-side in the current viewport.
        // FlowDocumentPageViewer exposes no explicit "pages across" property, so we approximate it by halving the
        // page-width zoom factor so both pages are simultaneously visible in the viewport.
        if (pagesAcross == 2)
        {
            var (pageWidthFactor, _, _) = ComputeZoomFitFactors();
            viewer.Zoom = DocumentViewDepthLayoutPlanner.BuildDocumentViewerZoomPercent(
                plan.Layout,
                pageWidthFactor);
        }

        UIElement preview = viewer;
        if (plan.IsSideToSideActive)
        {
            _viewSession.StartPagePairNavigation(totalPages: paginator.PageCount);
            preview = BuildSideToSideNavigationHost(viewer);
        }
        else
        {
            ResetSideToSideNavigation();
        }

        // Save the current workspace child so ExitPaginatedView can restore it.
        _workspaceGridChild = _workspace.Child;
        _workspace.Child = preview;
        _paginatedViewer = viewer;
        ApplySideToSideNavigationToViewer();
    }

    private UIElement BuildSideToSideNavigationHost(UIElement content)
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
        host.Children.Add(content);
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
            Padding = new Thickness(10, 4, 10, 4),
            MinWidth = 96,
            ToolTip = semantic.AutomationName
        };
        button.Click += (_, _) => action();
        AutomationProperties.SetAutomationId(button, semantic.AutomationId);
        AutomationProperties.SetName(button, semantic.AutomationName);
        return button;
    }

    private void NavigateSideToSidePagePair(FreeWViewDepthPagePairNavigationCommand command)
    {
        if (!_viewSession.CurrentDepth.IsSideToSideActive ||
            (_paginatedViewer is null && _editablePaginatedPanel is null))
            return;

        _viewSession.NavigatePagePair(command);
        ApplySideToSideNavigationToViewer();
        SyncSideToSideNavigationControls();
    }

    private void ApplySideToSideNavigationToViewer()
    {
        if (!_viewSession.CurrentDepth.IsSideToSideActive)
            return;

        var firstPage = _viewSession.PagePairNavigation.FirstVisiblePageNumber;
        if (_paginatedViewer is not null && _paginatedViewer.CanGoToPage(firstPage))
            _paginatedViewer.GoToPage(firstPage);
        else
            _editablePaginatedPanel?.ScrollToPage(firstPage);
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
        _sideToSidePreviousPairButton = null;
        _sideToSideNextPairButton = null;
        _sideToSidePairStatusText = null;
    }

    /// <summary>
    /// Restores the live workspaceGrid as the workspace child, dismissing the paginated overlay and
    /// clearing both the Multiple Pages and Side to Side flags.
    /// </summary>
    private void ExitPaginatedView()
    {
        if (_workspaceGridChild is not null)
            _workspace.Child = _workspaceGridChild;

        _paginatedViewer = null;
        if (_editablePaginatedPanel is not null)
        {
            PaginatedCommitCoordinator.Commit(_editablePaginatedPanel, _editor);
            _editor.LoadModel(_editor.Model);
        }
        _editablePaginatedPanel = null;
        _workspaceGridChild = null;
        ResetSideToSideNavigation();
        SyncViewDepthRibbonState();
    }

    // ── PagedEdit ────────────────────────────────────────────────────────────────────────────────
    // Opt-in editable-pagination surface.  Swaps the workspace child from the live workspaceGrid
    // to a PaginatedEditorPanel; exiting commits all page boxes back into the model and reloads the
    // continuous editor unchanged.  Entered via View ▸ Views ▸ Page Edit (freew.paged-edit-view).

    /// <summary>
    /// Toggles PagedEdit mode on or off. Wired to the ribbon button and status-bar shortcut.
    /// Entering commits the continuous editor first and swaps in the <see cref="PaginatedEditorPanel"/>;
    /// exiting commits all page boxes back to the model and restores the continuous editor.
    /// Mutually exclusive with Print Layout / Web Layout / Draft (the continuous editor stays the default).
    /// </summary>
    private void TogglePagedEditView()
    {
        if (_pagedEditMode)
            ExitPagedEdit();
        else
            EnterPagedEdit();
    }

    /// <summary>
    /// Enters PagedEdit mode: commits the live editor, builds the <see cref="PaginatedEditorPanel"/>,
    /// and swaps it in as the workspace child.  The default continuous editor is untouched.
    /// </summary>
    internal void EnterPagedEdit()
    {
        if (_pagedEditMode)
            return;

        // Leave any overlay modes that also swap the workspace child.
        if (_viewSession.CurrentDepth.Mode != FreeWViewDepthMode.LiveEditor)
            ApplyViewDepthTransition(_viewSession.RestoreLiveEditor());
        if (_outlineMode)
            LeaveOutlineView(restorePriorView: false);

        // Commit so the panel reflects the latest edits.
        _editor.CommitToModel();

        _pagedEditPanel = PaginatedEditorPanel.Build(_editor);
        _pagedEditMode = true;

        // Swap workspace child exactly like EnterPaginatedView / ToggleSplitWindow.
        _workspaceGridChild = _workspace.Child;
        _workspace.Child = _pagedEditPanel;

        // Sync ribbon toggles: PagedEdit on, print-family modes off.
        RefreshViewModeChecks();
    }

    /// <summary>
    /// Exits PagedEdit mode: commits all page boxes back to the model via
    /// <see cref="PaginatedCommitCoordinator"/>, restores the workspace child, and reloads the
    /// continuous editor from the updated model so PrintLayout/Web/Draft work normally again.
    /// </summary>
    internal void ExitPagedEdit()
    {
        if (!_pagedEditMode || _pagedEditPanel is null)
            return;

        // Commit all page boxes into the model.
        PaginatedCommitCoordinator.Commit(_pagedEditPanel, _editor);

        // Restore workspace.
        if (_workspaceGridChild is not null)
            _workspace.Child = _workspaceGridChild;
        _workspaceGridChild = null;
        _pagedEditPanel = null;
        _pagedEditMode = false;

        // Reload the continuous editor from the just-committed model so the view is current.
        _editor.LoadModel(_editor.Model);

        // Sync ribbon toggles: PagedEdit off, print-family mode back on.
        RefreshViewModeChecks();
    }

    // ── Split Window ─────────────────────────────────────────────────────────────────────────────
    // Split divides the workspace border into a top pane (the live workspaceGrid + editor) and a
    // bottom read-only FlowDocumentScrollViewer snapshot built from PrintLayout.BuildPaginatedDocument.
    // The snapshot is refreshed on TextChanged with a ~300 ms debounce so rapid keystrokes don't
    // re-paginate on every character. Toggling off removes the splitter and restores the single editor.

    /// <summary>
    /// Toggles the split-window view. When entering, replaces the workspace child with a Grid that
    /// contains the original workspaceGrid (top), a <see cref="GridSplitter"/> (middle), and a read-only
    /// <see cref="FlowDocumentScrollViewer"/> snapshot (bottom). When exiting, restores the original child.
    /// </summary>
    private void ToggleSplitWindow() =>
        ApplyViewDepthTransition(_viewSession.Execute(FreeWViewDepthCommand.ToggleSplit));

    private void EnterSplitView()
    {
        // Commit so the initial snapshot reflects the latest edits.
        _editor.CommitToModel();

        // Save the original child (the workspaceGrid) so ExitSplitView can restore it.
        var originalChild = _workspace.Child;

        var splitGrid = new Grid();
        splitGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        splitGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) });           // splitter
        splitGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Top pane: the live workspaceGrid (editor + rulers), detached from _workspace first.
        _workspace.Child = null;
        Grid.SetRow(originalChild, 0);
        splitGrid.Children.Add(originalChild);

        // Splitter: horizontal, resizes top and bottom rows.
        var splitter = new GridSplitter
        {
            Height = 4,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            ResizeDirection = GridResizeDirection.Rows,
            ShowsPreview = false
        };
        Grid.SetRow(splitter, 1);
        splitGrid.Children.Add(splitter);

        // Bottom pane: a read-only snapshot built from the paginator.
        var snapshotViewer = BuildSplitSnapshot();
        Grid.SetRow(snapshotViewer, 2);
        splitGrid.Children.Add(snapshotViewer);

        _splitGrid = splitGrid;
        _workspace.Child = splitGrid;

        SyncViewDepthRibbonState();
    }

    /// <summary>
    /// Builds the initial read-only snapshot <see cref="FlowDocumentScrollViewer"/> for the split-window
    /// bottom pane, fed by <see cref="PrintLayout.BuildPaginatedDocument"/>.
    /// </summary>
    private FlowDocumentScrollViewer BuildSplitSnapshot()
    {
        var doc = PrintLayout.BuildPaginatedDocument(_editor);
        var viewer = new FlowDocumentScrollViewer
        {
            Document = doc,
            IsSelectionEnabled = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        return viewer;
    }

    /// <summary>
    /// Exits the split-window view, restoring the original workspace child (the workspaceGrid + editor).
    /// </summary>
    private void ExitSplitView()
    {
        if (_splitGrid is null)
        {
            SyncViewDepthRibbonState();
            return;
        }

        // The original workspaceGrid is the first child (row 0) of _splitGrid.
        var originalChild = _splitGrid.Children[0] as UIElement;
        _splitGrid.Children.Clear();
        _workspace.Child = originalChild;

        _splitGrid = null;

        // Stop the debounce timer if it is still running.
        _splitDebounceTimer?.Stop();
        _splitDebounceTimer = null;

        SyncViewDepthRibbonState();
    }

    private void SyncViewDepthRibbonState()
    {
        _stateStore.SetChecked("freew.zoom-multiple-pages", _viewSession.CurrentDepth.IsMultiplePagesActive);
        _stateStore.SetChecked("freew.zoom-side-to-side", _viewSession.CurrentDepth.IsSideToSideActive);
        _stateStore.SetChecked("freew.split-window", _viewSession.CurrentDepth.IsSplitActive);
    }

    /// <summary>
    /// Arms a one-shot ~300 ms timer to refresh the split-window snapshot. Resets the timer on every
    /// call so rapid keystrokes collapse into a single rebuild at the end of the burst. No-op when the
    /// split pane is not active.
    /// </summary>
    private void ScheduleSplitPaneRefresh()
    {
        if (!_viewSession.CurrentDepth.IsSplitActive || _splitGrid is null)
            return;

        // Restart the debounce timer on every TextChanged.
        if (_splitDebounceTimer is null)
        {
            _splitDebounceTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = System.TimeSpan.FromMilliseconds(300)
            };
            _splitDebounceTimer.Tick += (_, _) =>
            {
                _splitDebounceTimer.Stop();

                // Repaginating the live document from a timer tick is fatal if it throws: the
                // dispatcher handler records the fault but does not mark it handled, and the timer
                // restarts on every keystroke, so a document that trips it would crash again on the
                // next character. The sibling repaginate timer in PaginatedEditorPanel is guarded
                // for exactly this reason; the split pane simply drops its stale snapshot instead.
                try
                {
                    RefreshSplitSnapshot();
                }
                catch (Exception)
                {
                    // Leave the previous snapshot in place; the next edit schedules another pass.
                }
            };
        }
        else
        {
            _splitDebounceTimer.Stop();
        }

        _splitDebounceTimer.Start();
    }

    /// <summary>
    /// Rebuilds the split-window snapshot pane from the latest committed content. Called after the
    /// debounce delay so the snapshot reflects the most recent edits without lagging the editor.
    /// </summary>
    private void RefreshSplitSnapshot()
    {
        if (!_viewSession.CurrentDepth.IsSplitActive || _splitGrid is null || _splitGrid.Children.Count < 3)
            return;

        _editor.CommitToModel();
        var newDoc = PrintLayout.BuildPaginatedDocument(_editor);

        if (_splitGrid.Children[2] is FlowDocumentScrollViewer viewer)
            viewer.Document = newDoc;
    }

    // Recompute the heading outline from the editor's committed model and repopulate the nav list.
    // Cheap, and skipped entirely while the pane is hidden. Each list item carries its OutlineEntry so
    // a selection can map straight back to the model block index.
    private void RefreshOutline()
    {
        if (_navList is null || !_navPaneVisible)
            return;

        RenderNavigationOutcome(_navigationPaneSession.Refresh());
    }

    private void RenderNavigationOutcome(NavigationPaneOutcome outcome)
    {
        var state = outcome.State;
        _navList.SelectionChanged -= OnOutlineSelected;
        _navList.Items.Clear();
        foreach (var heading in state.Headings)
            _navList.Items.Add(new OutlineItem(heading));
        _navList.SelectedIndex = state.SelectedHeadingBlockIndex is { } blockIndex
            ? state.Headings.ToList().FindIndex(heading => heading.BlockIndex == blockIndex)
            : -1;
        _navList.SelectionChanged += OnOutlineSelected;
        UpdateNavSearchStatus();

        if (outcome.NavigateToBlockIndex is { } target)
            _editor.BringBlockIntoView(target);
    }

    // Clicking an outline entry scrolls the matching heading into view and moves the caret there by
    // mapping the entry's model block index onto the editor's FlowDocument (DocumentView.BringBlockIntoView).
    private void OnOutlineSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_navList.SelectedItem is OutlineItem item)
            RenderNavigationOutcome(_navigationPaneSession.SelectHeading(item.Entry.BlockIndex));
    }

    // The outline-entry context menu (Promote / Demote / Collapse / Expand). Each item acts on the
    // currently selected OutlineEntry, mapping its model block index onto the editor's heading commands,
    // then refreshes the outline so promoted/demoted levels and collapse markers update.
    private ContextMenu BuildOutlineContextMenu()
    {
        var menu = new ContextMenu();
        menu.Opened += (_, _) => PopulateOutlineContextMenu(menu);
        return menu;
    }

    private void PopulateOutlineContextMenu(ContextMenu menu)
    {
        var plan = _navigationPaneSession.BuildOutlineMenu();

        menu.Items.Clear();
        foreach (var planned in plan.Items)
        {
            if (planned.Kind == RibbonMenuItemKind.Separator)
            {
                menu.Items.Add(new Separator());
                continue;
            }

            var item = new MenuItem { Header = planned.Header, IsEnabled = planned.IsEnabled };
            if (planned.CommandId is { } commandId)
                item.Click += (_, _) => ExecuteOutlineContextCommand(commandId);
            menu.Items.Add(item);
        }
    }

    private void ExecuteOutlineContextCommand(RibbonCommandId commandId)
    {
        RenderNavigationOutcome(_navigationPaneSession.ExecuteOutlineCommand(commandId));
    }

    // A nav-list row: indents the heading text by its outline level and remembers the source entry
    // (so a click can map back to the model block index). ToString drives the default ListBox display.
    private sealed class OutlineItem(NavigationHeadingProjection entry)
    {
        public NavigationHeadingProjection Entry { get; } = entry;

        public override string ToString()
        {
            var text = Entry.Text.Length > 0 ? Entry.Text : "(untitled)";
            return new string(' ', Entry.Level * 4) + text;
        }
    }

    // A status-bar zoom control: a [-] button, a 50%..200% slider, a [+] button, and a live percentage
    // label. All three drive DocumentView.ZoomLevel; ZoomChanged feeds the slider/label back so the
    // control stays in sync with other zoom sources (e.g. Ctrl+MouseWheel in the editor).
    private UIElement BuildZoomControl()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        Button ZoomButton(string label, Action onClick)
        {
            var button = new Button
            {
                Content = label,
                Style = (Style)FindResource("ChromeStatusButtonStyle"),
                Width = 24,
                Padding = new Thickness(0),
                Margin = new Thickness(2, 3, 2, 3)
            };
            button.Click += (_, _) => onClick();
            return button;
        }

        _zoomSlider = new Slider
        {
            Minimum = ZoomLevels.Min,
            Maximum = ZoomLevels.Max,
            Value = _editor.ZoomLevel,
            Width = 120,
            VerticalAlignment = VerticalAlignment.Center,
            TickFrequency = ZoomLevels.Step,
            SmallChange = ZoomLevels.Step,
            LargeChange = ZoomLevels.Step,
            ToolTip = FreeWUiTextCatalog.Zoom
        };
        _zoomSlider.ValueChanged += (_, e) => _editor.ZoomLevel = e.NewValue;

        _zoomLabel = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 2, 0),
            MinWidth = 38,
            TextAlignment = System.Windows.TextAlignment.Right,
            Text = ZoomLevels.FormatPercent(_editor.ZoomLevel)
        };

        // Keep the slider + label in sync no matter how zoom changes (buttons, wheel, or the slider itself).
        _editor.ZoomChanged += (_, factor) =>
        {
            _zoomSlider.Value = factor;
            _zoomLabel.Text = ZoomLevels.FormatPercent(factor);
        };

        panel.Children.Add(ZoomButton("−", () => _editor.ZoomLevel = ZoomLevels.StepDown(_editor.ZoomLevel)));
        panel.Children.Add(_zoomSlider);
        panel.Children.Add(ZoomButton("+", () => _editor.ZoomLevel = ZoomLevels.StepUp(_editor.ZoomLevel)));
        // The percentage is clickable (Word does this): clicking it opens the Zoom dialog.
        var zoomButton = new Button
        {
            Content = _zoomLabel,
            Style = (Style)FindResource("ChromeStatusButtonStyle"),
            Padding = new Thickness(2, 0, 2, 0),
            ToolTip = FreeWUiTextCatalog.Zoom
        };
        zoomButton.Click += (_, _) => OpenZoomDialog();
        panel.Children.Add(zoomButton);
        return panel;
    }

    // View > Zoom (and the clickable status-bar percentage): open Word's Zoom dialog. The page-relative fit
    // factors (Page width / Text width / Whole page) are computed from the live workspace viewport and the
    // model page geometry via the pure ZoomFit helper, so "Page width"/"Whole page" honour the real page
    // size + margins. The chosen factor drives DocumentView.ZoomLevel (clamped, shared with the slider).
    private void OpenZoomDialog()
    {
        var chosen = ZoomDialog.Prompt(this, _editor.ZoomLevel, ComputeZoomFitFactors());
        if (chosen is { } factor)
            _editor.ZoomLevel = factor;
    }

    private void ZoomToOnePage()
    {
        var (_, _, wholePageFactor) = ComputeZoomFitFactors();
        _editor.ZoomLevel = wholePageFactor;
    }

    private void ZoomToPageWidth()
    {
        var (pageWidthFactor, _, _) = ComputeZoomFitFactors();
        _editor.ZoomLevel = pageWidthFactor;
    }

    private ZoomDialogFitFactors ComputeZoomFitFactors()
    {
        _editor.CommitToModel();
        var page = _editor.Model.Page;

        // The viewport the page floats in: the grey workspace, minus the editor's own breathing-room margin.
        var margin = _editor.Margin;
        var viewportWidth = Math.Max(0, _workspace.ActualWidth - margin.Left - margin.Right);
        var viewportHeight = Math.Max(0, _workspace.ActualHeight - margin.Top - margin.Bottom);

        return ZoomDialogPlanner.BuildFitFactors(page, viewportWidth, viewportHeight);
    }

    // QAT Undo / Redo: focus the editing surface and run its built-in (RichTextBox) undo/redo, which is
    // the same inline history Ctrl+Z / Ctrl+Y drive. Guarded by CanUndo/CanRedo so a no-op stays a no-op.
    private void Undo()
    {
        _editor.Focus();
        if (_editor.CanUndo)
            _editor.Undo();
    }

    private void Redo()
    {
        _editor.Focus();
        if (_editor.CanRedo)
            _editor.Redo();
    }

    private void Print() => PrintDocument(_editor, "FreeW Document");

    private void PrintMailMergeDocument(TextDocument document)
    {
        var printEditor = new DocumentView();
        printEditor.LoadModel(document);
        PrintDocument(printEditor, "FreeW Mail Merge");
    }

    private void PrintDocument(DocumentView editor, string description)
    {
        // Compose the same physical sequence used by Print Preview before opening the dialog. This
        // gives the native range control exact bounds that include section parity blanks and note
        // continuation pages.
        var paginator = PrintLayout.BuildPaginator(editor);
        paginator.ComputePageCount();
        var plan = FreeWPrintRequestPlanner.Create(
            description,
            editor.Model.Page,
            paginator.PageCount);
        var result = WpfPaginatorPrintWorkflow.Execute(new WpfPaginatorPrintRequest(
            plan.Description,
            _ => paginator,
            new WpfPaginatorPrintTicketOptions(
                PageWidthDip: plan.PageWidthDip,
                PageHeightDip: plan.PageHeightDip),
            new WpfPaginatorPageRangeOptions(
                plan.TotalPages,
                (from, to) =>
                {
                    var selectedRange = FreeWPrintRequestPlanner.FromOneBasedRange(
                        from,
                        to,
                        plan.TotalPages);
                    var (firstPage, lastPage) = FreeWPrintRequestPlanner.ResolvePageRange(
                        selectedRange,
                        plan.TotalPages);
                    return new WpfPaginatorPageRange(firstPage, lastPage);
                }),
            this));

        if (result is { Outcome: WpfPaginatorPrintOutcome.Failed, Error: not null })
        {
            DialogMessageHelper.ShowError(
                this,
                UiText.Format("Print_Failed_Message_Format", result.Error.Message),
                UiText.Get("Print_Title"));
        }
    }

    private void OpenPrintPreview()
    {
        var preview = new PrintPreviewWindow(_editor, _file.DisplayName) { Owner = this };
        preview.Show();
    }

    /// <summary>
    /// File &gt; Export: writes the document to a real PDF. Reuses the print pipeline
    /// (<see cref="PrintLayout.BuildPaginator"/>) so the exported pages match Print / Print Preview
    /// exactly (page geometry, header/footer, watermark, border, footnotes), renders them to PDF via
    /// <see cref="PdfExport"/>, and flushes atomically through the shared
    /// <see cref="Free.Shared.AppServices.AtomicFileWriter"/>.
    /// </summary>
    private void ExportToPdf()
    {
        var plan = FreeWExportWorkflow.CreatePlan(FreeWExportFormat.Pdf, _file.DisplayName);
        var saveResult = WpfFileDialogService.ShowSaveDialog(
            this,
            plan.Filter,
            plan.SuggestedFileName,
            plan.DefaultExtensionWithDot,
            1,
            plan.PickerTitle);
        if (!saveResult.Chosen)
            return;

        var path = saveResult.FileName!;
        var execution = FreeWExportWorkflow.ExecuteAsync(
            plan,
            path,
            (stream, _) =>
            {
                var paginator = PrintLayout.BuildPaginator(_editor);
                paginator.ComputePageCount();
                var imageDiagnostics = new List<string>(_editor.ImageDecodeDiagnostics);
                var writerDiagnostics = new List<string>();
                var bytes = PdfExport.RenderToBytes(
                    paginator,
                    _file.DisplayName,
                    writerDiagnostics);
                imageDiagnostics.AddRange(writerDiagnostics);
                stream.Write(bytes);
                return ValueTask.FromResult(new FreeWExportArtifact(
                    paginator.PageCount,
                    "WPF",
                    imageDiagnostics.Count));
            }).GetAwaiter().GetResult();
        if (execution.Succeeded)
        {
            DialogMessageHelper.ShowInfo(
                this,
                execution.Message,
                plan.PickerTitle);
        }
        else
        {
            DialogMessageHelper.ShowError(
                this,
                execution.Message,
                plan.PickerTitle);
        }
    }

    /// <summary>
    /// File &gt; Export: writes the document to a real XPS package. Reuses the same print pipeline
    /// (<see cref="PrintLayout.BuildPaginator"/>) as Print / Export to PDF so the exported pages match
    /// exactly (page geometry, header/footer, watermark, border, footnotes), serialises them as vector
    /// glyph runs via <see cref="XpsExport"/>, and flushes atomically through the shared
    /// <see cref="Free.Shared.AppServices.AtomicFileWriter"/>.
    /// </summary>
    private void ExportToXps()
    {
        var plan = FreeWExportWorkflow.CreatePlan(FreeWExportFormat.Xps, _file.DisplayName);
        var saveResult = WpfFileDialogService.ShowSaveDialog(
            this,
            plan.Filter,
            plan.SuggestedFileName,
            plan.DefaultExtensionWithDot,
            1,
            plan.PickerTitle);
        if (!saveResult.Chosen)
            return;

        var path = saveResult.FileName!;
        var execution = FreeWExportWorkflow.ExecuteAsync(
            plan,
            path,
            (stream, _) =>
            {
                var paginator = PrintLayout.BuildPaginator(_editor);
                var bytes = XpsExport.RenderToBytes(paginator);
                stream.Write(bytes);
                return ValueTask.FromResult(new FreeWExportArtifact());
            }).GetAwaiter().GetResult();
        if (execution.Succeeded)
        {
            DialogMessageHelper.ShowInfo(
                this,
                execution.Message,
                plan.PickerTitle);
        }
        else
        {
            DialogMessageHelper.ShowError(
                this,
                execution.Message,
                plan.PickerTitle);
        }
    }

    private void OpenFindReplace() => OpenFindReplace(FindReplaceOpenMode.Find);

    private void OpenFindReplace(FindReplaceOpenMode openMode)
    {
        if (_findDialog is null)
        {
            _findDialog = new FindReplaceDialog(this, _editor, openMode);
            _findDialog.Closed += (_, _) => _findDialog = null;
        }
        _findDialog.Show();
        _findDialog.Activate();
        _findDialog.ActivateFor(openMode);
    }

    private void OpenProperties()
    {
        var dialog = new PropertiesDialog(this, _editor.Model.Properties);
        if (dialog.ShowDialog() == true && dialog.Result is { } result)
        {
            _editor.ApplyDocumentProperties(result);
            _file.MarkDirty();
        }
    }

    private void ToggleMarkAsFinal()
    {
        _editor.Focus();
        _editor.SetMarkedAsFinal(!_editor.IsMarkedAsFinal);
    }

    private void OpenRestrictEditing()
    {
        _editor.Focus();
        var chosen = RestrictEditingDialog.Prompt(this, _editor.Model.Protection);
        if (chosen is { } settings)
            _editor.SetProtection(settings);
    }

    private void InspectDocument()
    {
        _editor.CommitToModel();
        var result = DocumentInspector.Inspect(_editor.Model);
        var choice = DocumentInspectorDialog.Show(this, result);
        if (choice is null)
            return;

        _editor.ApplyInspectorRemovals(choice);
    }

    private void CheckAccessibility()
    {
        _editor.CommitToModel();
        var report = AccessibilityChecker.Check(_editor.Model);
        var dialog = new AccessibilityReportDialog(this, report);
        dialog.ShowDialog();
    }

    // Opens the modal FreeW Options editor. On OK it applies the edited settings live (by mutating the
    // shared _options instance FileCommands reads) and persists them through the shared JsonSettingsStore
    // so they survive a restart. Save is best-effort — a failure surfaces a message but never throws.
    private void OpenOptions()
    {
        var dialog = new OptionsDialog(this, _options);
        if (dialog.ShowDialog() != true)
            return;

        var edited = dialog.Result;
        var outcome = _optionsRuntime.ApplyAndPersist(
            edited,
            options => _optionsStore.Save(options),
            () => _optionsStore.Load());
        ApplyEditorTypingOptions(outcome.EditorTypingOptions);

        if (!outcome.Persisted)
            DialogMessageHelper.ShowError(
                this,
                _optionsStore.LastError,
                UiText.Get("Options_Dialog_Title"));
    }

    // Push the persisted AutoCorrect master switch + per-rule AutoFormat toggles onto the live editor so the
    // as-you-type rules honour the user's settings immediately (called at construction and after Options OK).
    private void ApplyEditorTypingOptions(FreeWEditorTypingOptionsPlan plan)
    {
        _editor.AutoCorrectEnabled = plan.AutoCorrectEnabled;
        _editor.AutoFormatOptions = plan.AutoFormat;
        _editor.AutoCorrectOptions = plan.AutoCorrect;
    }

    // --- Real Word-style ribbon, rendered by the shared WPF renderer ---
    //
    // BuildRibbon builds a flat tab strip (Home/Insert/Layout/Design/View/Mailings/Review) over the
    // shared RibbonDefinition model. Each selected tab's body is produced by the shared
    // Free.Shared.Ribbon.Wpf.RibbonWpfRenderer — the same renderer FreeX uses — so FreeW gets Word's
    // visual vocabulary (Large hero buttons, Medium icon+label, Small icon-only, group panels, dividers,
    // group-label borders and vector glyphs). Command behavior and live toggle state flow through the
    // FreeW command registry + IRibbonStateStore exactly as before.

    private (UIElement Ribbon, TabControl Tabs) BuildRibbon(RibbonDefinition definition, IRibbonCommandRegistry registry, IRibbonStateStore stateStore)
    {
        // Install FreeW's command-id → glyph mapping so the shared renderer draws meaningful icons for
        // freew.* ids (otherwise every button would fall back to the generic glyph).
        FreeWRibbonIcons.Install();

        // Flat Word/FreeX ribbon tabs come from the shared library so any app on the shared ribbon gets
        // the look automatically (no border, transparent headers, hover wash, selected tab filled white
        // with a colored accent underline). See RibbonTabControlFactory.
        var result = RibbonShellBuilder.Build(new RibbonShellBuildSpec(
            definition,
            registry,
            stateStore,
            FileTabHeader: "File",
            FileTabAccent: WpfThemeApplier.ToColor(Program.ActiveTheme.Colors.Accent),
            FileTabHover: WpfThemeApplier.ToColor(Program.ActiveTheme.Colors.AccentDark),
            ShowBackstage)
        {
            EnableContextualTabs = true,
            ResourceDictionaries =
            [
                new ResourceDictionary
                {
                    Source = new Uri("/FreeW.App.Host;component/Ribbon/FreeWRibbonResources.xaml", UriKind.Relative)
                }
            ],
            CustomizeTabContent = (tab, content) =>
            {

        // The renderer resolves its button/group styles and surface brushes via TryFindResource on the
        // supplied resource host. Merge FreeW's ribbon styles into the TabControl so those lookups
        // resolve (the renderer falls back gracefully for any key it can't find).
        // ── File tab (Word-style): the FIRST ribbon tab, rendered as an accent-coloured pill. Selecting it
        //    opens the Backstage overlay rather than swapping the ribbon body to an empty tab. Like FreeX,
        //    the File tab never *stays* selected: the SelectionChanged handler shows the Backstage and
        //    immediately reverts the selection to the previously-active content tab (index 1 = Home).
        // Contextual "Tools" tabs (Picture Format / table tabs) are declared in the ribbon model and
        // managed by the shared controller — hidden until their selection context is active. Default revert
        // tab is Home (index 1; index 0 is the File pill).
            // V5 galleries: inject the live-preview Word-style galleries into the rendered group content.
            // The shared renderer stamps each group's grid with its catalog id (RibbonMetadata.CatalogId),
            // so we find the target group and prepend a custom gallery control into its content lane. This
            // keeps the galleries entirely app-side (custom WPF content) without a shared RibbonGallery type.
            if (tab.Id == "home")
                // The gallery owns visible quick-style selection; Clear/New/Manage stay reachable from
                // its More popup, so duplicate width-heavy style buttons need not evict core Font/Paragraph.
                InjectGallery(content, "styles", StylesGallery.Build(_editor, registry), removeKind: RemoveKind.All);
            if (tab.Id == "design")
                // Replace the rendered Document Formatting controls with the live-preview Word-style
                // gallery/menu strip so backed commands do not appear twice beside their custom previews.
                InjectGallery(content, "themes", ThemeGallery.BuildDocumentFormatting(_editor), removeKind: RemoveKind.All);
            if (tab.Id == "table-design")
                // Table Styles gallery: inject a live-preview style picker into the Table Style group,
                // replacing the Shading button placeholder so the gallery owns that lane.
                InjectGallery(content, "table-style", TableStylesGallery.Build(_editor, registry), removeKind: RemoveKind.All);

            if (tab.Id == "chart-design")
            {
                // Inject the three Chart Design galleries (Quick Layout, Chart Styles, Change Colors)
                // into the corresponding groups on the chart contextual tab. Each gallery replaces the
                // group's placeholder ribbon commands with live-preview swatches.
                InjectGallery(content, "chart-quick-layout", ChartDesignGallery.BuildQuickLayouts(registry), removeKind: RemoveKind.All);
                InjectGallery(content, "chart-style", ChartDesignGallery.BuildChartStyles(registry), removeKind: RemoveKind.All);
                InjectGallery(content, "chart-colors", ChartDesignGallery.BuildChangeColors(registry), removeKind: RemoveKind.All);
            }

            if (tab.Id == "smartart-design")
            {
                // Inject the three SmartArt gallery strips: Layouts, Change Colors, Styles.
                InjectGallery(content, "smartart-layouts", SmartArtGallery.BuildLayouts(registry), removeKind: RemoveKind.All);
                InjectGallery(content, "smartart-colors", SmartArtGallery.BuildColors(registry), removeKind: RemoveKind.All, extra: SmartArtGallery.BuildStyles(registry));
            }

            }
        });

        // Start on Home (index 1; index 0 is the File tab). Remember it as the last "real" tab so File
        // selection can revert to it.
        _fileTab = result.FileTab;
        _fileTabRouter = result.FileTabRouter;
        _contextualTabs = result.ContextualTabs!;
        return (result.Root, result.Tabs);
    }

    // The accent-coloured File tab style (Word's blue File button look): a solid accent fill with white
    // text, a darker hover/press, comfortable padding. Distinct from the flat content-tab headers so it
    // reads as the Backstage entry point. Authored in code to keep parity with the code-only shell.
    // What of a group's original rendered controls to drop before injecting a gallery.
    private enum RemoveKind { None, Combos, All }

    // Find the group grid carrying CatalogId == groupId in the freshly built tab content and prepend the
    // gallery into its content lane (row 0). `removeKind` controls which of the group's original
    // placeholder controls are removed first: All clears the lane (the gallery fully owns the group);
    // Combos drops only ComboBox columns (so a placeholder combo the gallery supersedes goes away while
    // command buttons like New Style / Manage Styles remain). An optional `extra` gallery is appended
    // after the first (e.g. the Design Colors strip).
    private static void InjectGallery(DependencyObject content, string groupId, FrameworkElement gallery, RemoveKind removeKind, FrameworkElement? extra = null)
    {
        var grid = FindGroupGrid(content, groupId);
        if (grid is null)
            return;

        // Row 0 of the group grid holds the content lane (a horizontal StackPanel of columns/controls).
        var lane = grid.Children.OfType<FrameworkElement>().FirstOrDefault(c => Grid.GetRow(c) == 0) as Panel;
        if (lane is null)
            return;

        if (removeKind == RemoveKind.All)
        {
            lane.Children.Clear();
        }
        else if (removeKind == RemoveKind.Combos)
        {
            // Each lane column is its own StackPanel; the renderer packs combos into combo-only columns,
            // so a column whose children are all ComboBoxes is a placeholder-combo column to drop.
            var toRemove = lane.Children.OfType<Panel>()
                .Where(col => col.Children.Count > 0 && col.Children.OfType<UIElement>().All(c => c is ComboBox))
                .ToList();
            foreach (var col in toRemove)
                lane.Children.Remove(col);
        }

        var host = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(2, 2, 2, 0) };
        host.Children.Add(gallery);
        if (extra is not null)
            host.Children.Add(extra);
        lane.Children.Insert(0, host);
    }

    // Find the group content grid stamped with the given catalog id, walking the renderer's known
    // structure: the tab content is a Border whose child is a RibbonAdaptivePanel whose children are
    // RibbonGroupHosts. Each host's Content is the group grid (which carries the catalog id). This walks
    // the logical structure the renderer built eagerly, so it works before the visual tree is realized
    // (unlike VisualTreeHelper, which would see nothing until the ribbon is measured/rendered).
    private static Grid? FindGroupGrid(DependencyObject root, string groupId)
    {
        var panel = (root as Border)?.Child as Panel;
        if (panel is null)
            return null;

        foreach (var child in panel.Children)
        {
            if (child is RibbonGroupHost host && host.Content is Grid grid
                && RibbonMetadata.GetCatalogId(grid) == groupId)
                return grid;
        }
        return null;
    }
}
