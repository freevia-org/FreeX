using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Free.Shared.Drawing;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Wpf;
using Free.Shared.Shell;
using Free.Shared.Shell.Wpf;
using Free.Shared.Theme;
using Free.Shared.Theme.Wpf;
using FreeP.App.Compositor;
using FreeP.App.Host.Backstage;
using FreeP.App.Rendering.Wpf;
using FreeP.Core.Model;
using ModelHyperlink = FreeP.Core.Model.Hyperlink;

namespace FreeP.App.Host;

/// <summary>
/// FreeP main window. Deliberately code-only and minimal: it exists to prove the shared tier is consumable by
/// a third sister app. The window is composed entirely from shared chrome — the <see cref="ShellChrome"/>
/// title bar, a shared <see cref="RibbonDefinition"/> ribbon rendered by the shared WPF renderer, the shared
/// <c>BackstageFrame</c> File screen, and a simple status bar — around a real slide canvas (SlideCanvas).
/// Mirrors FreeW.MainWindow's composition, swapping the Word document for the presentation stub.
///
/// Wave 3A layout:
///   ┌──────────────────────────────────────────┐
///   │  Title bar (shared ShellChrome)          │
///   ├──────────────────────────────────────────┤
///   │  Ribbon tabs                             │
///   ├────────────┬─────────────────────────────┤
///   │ Slide pane │  Stage (SlideCanvas)         │
///   │ host (3B)  │                             │
///   │ ~180px wide│                             │
///   ├────────────┴─────────────────────────────┤
///   │  Status bar                              │
///   └──────────────────────────────────────────┘
/// </summary>
public sealed partial class MainWindow : Window,
    IPresentationAltTextPaneHostView,
    IPresentationReadingOrderPaneHostView
{
    // Identity/palette for the shared window shell (Office-neutral title bar; berry "P" badge).
    private static readonly ProductThemeResourceProfile ThemeResources = ProductThemeResourceProfiles.FreeP;

    private static ShellChromeOptions BuildChromeOptions() => new()
    {
        BadgeLetter = Program.ActiveTheme.VisualAssets.ProductGlyph,
        TitleBarColor = FreePBrushes.TitleBarColor,
        TitleBarForegroundColor = FreePBrushes.TitleBarForegroundColor,
        BadgeColor = FreePBrushes.AccentDarkColor,
        CaptionHeight = FreePShellVisualMetrics.TitleBarHeight,
        IconUri = Program.ActiveTheme.VisualAssets.GetWpfPackUri("FreeP.App.Host")
    };

    private readonly FreePOptions _options;
    private readonly FreePOptionsRuntimeSession _optionsRuntime;
    private readonly IApplicationOptionsStore<FreePOptions> _optionsStore;
    private readonly IUserMessageService? _messageService;

    // ── Wave 10B: OS-clipboard service ────────────────────────────────────────────
    // Created once; the renderer is injected so tests can replace it without real Clipboard.
    private readonly OsClipboardService _osClipboard =
        new OsClipboardService(new WpfPlatformClipboard(), new WpfShapeRenderer());

    // ── Model ─────────────────────────────────────────────────────────────────────

    private readonly PresentationWorkareaSession _workareaSession;
    private Presentation _presentation => _workareaSession.Presentation;

    // ── Editing session (Wave 3A) ─────────────────────────────────────────────────

    /// <summary>
    /// The active editing session. 3B (thumbnail pane) and 3C (canvas interaction) consume this.
    /// Rebuilt on every file new/open — subscribers re-attach after LoadModel.
    /// </summary>
    internal EditingSession Editor => _workareaSession.Editor;

    // ── Shell chrome ──────────────────────────────────────────────────────────────

    private PresentationFileCommandSession _fileSession = null!;
    private readonly PresentationDocumentWindowPlanner _documentWindowPlanner;
    private readonly int _documentWindowNumber;

    // ── Autosave / crash recovery ─────────────────────────────────────────────────

    private AutosaveCoordinator _autosave = null!;

    /// <summary>
    /// Read by <see cref="EmergencySnapshotCrashHandler"/> only. Nullable because the crash handler
    /// can run before the constructor finished building this window.
    /// </summary>
    internal AutosaveCoordinator? AutosaveCoordinatorForCrashHandler => _autosave;

    private BackstageView _backstage = null!;
    private Border _titleBar = null!;
    private SisterWpfWindowTitleBinder _titleBinder = null!;
    private TabControl _ribbonTabs = null!;
    private TabItem _fileTab = null!;
    private RibbonFileTabRouter? _fileTabRouter;
    private RibbonContextualTabController? _contextualTabs;
    private readonly RibbonStateStore _ribbonStateStore = new();
    private FreePRibbonBindingSession? _ribbonBindingSession;

    // ── Body layout ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Left pane host seam — Wave 3B fills this with the slide-thumbnail pane.
    /// Named exactly "_slidePaneHost" so 3B can attach without restructuring.
    /// Currently rendered as an empty 180px-wide border region.
    /// <!-- 3B SEAM: add your thumbnail pane as the Child of this Border. -->
    /// </summary>
    internal Border SlidePaneHost { get; private set; } = null!;
    private SlidePane _slidePane = null!;
    private PresentationOutlinePane _outlinePane = null!;

    /// <summary>
    /// The centre-stage canvas. 3C attaches interaction (mouse/keyboard) to this.
    /// The canvas is bound to Editor.CurrentSlide.
    /// <!-- 3C SEAM: attach mouse handlers + adorner layer to _slideCanvas. -->
    /// </summary>
    internal SlideCanvas SlideCanvas { get; private set; } = null!;

    private Border _canvasHost = null!;
    private Grid _bodySplitter = null!;
    private Canvas _textOverlay = null!;
    private Canvas _oleOverlay = null!;
    private WpfOleInPlaceHost? _activeOleHost;
    private TextBlock _slideCountText = null!;
    private PresentationViewShowState _viewShowState = PresentationViewShowState.Default;
    private PresentationViewZoomState _viewZoomState = PresentationViewZoomState.FitToWindow;
    private PresentationViewModeState _viewModeState = PresentationViewModeState.Normal;
    private PresentationViewColorModeState _viewColorModeState = PresentationViewColorModeState.Color;

    // Notes pane (Wave 7B)
    private TextBox _notesBox = null!;
    private bool _notesRefreshing;   // guard against re-entrant TextChanged → SetCurrentSlideNotesText

    // Comment indicator overlay + list pane (Wave 11B)
    private Canvas  _commentOverlay = null!;  // hosts speech-bubble dots over the slide canvas
    private StackPanel _commentListPanel = null!; // shows comment text list below canvas
    private Border  _commentListHost = null!; // collapsible container for _commentListPanel
    private readonly PresentationReviewWorkflowSession _reviewWorkflowSession;
    private readonly PresentationMainWindowReviewPaneCoordinator _reviewPaneHostCoordinator = null!;
    private readonly PresentationProofingPaneNativeViewAdapter<UIElement> _proofingPaneNativeView;
    private StackPanel _layoutPickerPanel = null!;
    private Border _layoutPickerHost = null!;
    private UniformGrid _tablePickerGrid = null!;
    private Border _tablePickerHost = null!;
    private Border _altTextPaneHost = null!;
    private TextBlock _altTextPaneHeading = null!;
    private TextBlock _altTextPaneMessage = null!;
    private TextBlock _altTextTitleLabel = null!;
    private TextBox _altTextTitleBox = null!;
    private TextBlock _altTextDescriptionLabel = null!;
    private TextBox _altTextDescriptionBox = null!;
    private CheckBox _altTextDecorativeCheck = null!;
    private Button _altTextApplyButton = null!;
    private Button _altTextCloseButton = null!;
    private readonly PresentationAltTextPaneHostCoordinator _altTextPaneHostCoordinator;
    private Border _accessibilityCheckerPaneHost = null!;
    private TextBlock _accessibilityCheckerPaneHeading = null!;
    private TextBlock _accessibilityCheckerPaneMessage = null!;
    private StackPanel _accessibilityCheckerReviewDetailsPanel = null!;
    private StackPanel _accessibilityCheckerRowsPanel = null!;
    private readonly List<string> _accessibilityCheckerTableStructureReviewRenderedLines = new();
    private Border _readingOrderPaneHost = null!;
    private SelectionPane _selectionPane = null!;
    private TextBlock _readingOrderPaneHeading = null!;
    private TextBlock _readingOrderPaneMessage = null!;
    private StackPanel _readingOrderPaneItemsPanel = null!;
    private Button _readingOrderMoveEarlierButton = null!;
    private Button _readingOrderMoveLaterButton = null!;
    private readonly PresentationReadingOrderPaneHostCoordinator _readingOrderPaneHostCoordinator;
    private Border _proofingPaneHost = null!;
    private TextBlock _proofingPaneHeading = null!;
    private TextBlock _proofingPaneMessage = null!;
    private StackPanel _proofingPaneRowsPanel = null!;
    private Border _mediaCaptionPaneHost = null!;
    private TextBlock _mediaCaptionPaneHeading = null!;
    private TextBlock _mediaCaptionPaneMessage = null!;
    private ComboBox _mediaCaptionTrackBox = null!;
    private TextBlock _mediaCaptionLabelText = null!;
    private TextBox _mediaCaptionLabelBox = null!;
    private TextBlock _mediaCaptionLanguageText = null!;
    private TextBox _mediaCaptionLanguageBox = null!;
    private TextBlock _mediaCaptionSourceText = null!;
    private TextBox _mediaCaptionSourceBox = null!;
    private TextBlock _mediaCaptionTranscriptText = null!;
    private TextBox _mediaCaptionTranscriptBox = null!;
    private TextBlock _mediaVolumeText = null!;
    private Slider _mediaVolumeSlider = null!;
    private Button _mediaVolumeApplyButton = null!;
    private TextBlock _mediaStartModeText = null!;
    private ComboBox _mediaStartModeBox = null!;
    private CheckBox _mediaLoopCheckBox = null!;
    private CheckBox _mediaShowWhenStoppedCheckBox = null!;
    private CheckBox _mediaRewindAfterPlayingCheckBox = null!;
    private CheckBox _mediaPlayFullScreenCheckBox = null!;
    private TextBlock _mediaStopAfterSlidesText = null!;
    private TextBox _mediaStopAfterSlidesBox = null!;
    private Button _mediaPlaybackApplyButton = null!;
    private TextBlock _mediaTrimStartText = null!;
    private TextBox _mediaTrimStartBox = null!;
    private TextBlock _mediaTrimEndText = null!;
    private TextBox _mediaTrimEndBox = null!;
    private TextBlock _mediaFadeInText = null!;
    private TextBox _mediaFadeInBox = null!;
    private TextBlock _mediaFadeOutText = null!;
    private TextBox _mediaFadeOutBox = null!;
    private Button _mediaTimingApplyButton = null!;
    private TextBlock _mediaBookmarkText = null!;
    private ComboBox _mediaBookmarkBox = null!;
    private TextBlock _mediaBookmarkNameText = null!;
    private TextBox _mediaBookmarkNameBox = null!;
    private TextBlock _mediaBookmarkTimeText = null!;
    private TextBox _mediaBookmarkTimeBox = null!;
    private Button _mediaBookmarkCreateButton = null!;
    private Button _mediaBookmarkReplaceButton = null!;
    private Button _mediaBookmarkDeleteButton = null!;
    private Button _mediaCaptionCreateButton = null!;
    private Button _mediaCaptionReplaceButton = null!;
    private Button _mediaCaptionDeleteButton = null!;
    private Button _mediaCaptionCloseButton = null!;
    private PresentationMediaPaneCaptionNativeControls<TextBlock, TextBox> _mediaCaptionControls = null!;
    private PresentationMediaPaneNativeButtons<Button> _mediaPaneButtons = null!;
    private readonly PresentationMediaPaneHostViewAdapter _wpfMediaPaneHostView;
    private readonly PresentationMediaPaneHostCoordinator _mediaPaneHostCoordinator;
    private readonly PresentationSmartArtTextPaneSession _smartArtTextPaneSession;
    private readonly PresentationSmartArtTextPaneNativeViewAdapter<TextBox> _smartArtTextPaneNativeView;
    private readonly PresentationZoomAuthoringSession _zoomAuthoringSession;
    private readonly PresentationDomainContextMenuSession _domainContextMenuSession;
    private readonly PresentationNotesPaneSession _notesPaneSession;
    private readonly PresentationHyperlinkWorkflowSession _hyperlinkWorkflowSession;
    private readonly SlideShowCustomShowSession _customShowSession;
    private Border _smartArtTextPaneHost = null!;
    private TextBlock _smartArtTextPaneHeading = null!;
    private TextBlock _smartArtTextPaneMessage = null!;
    private StackPanel _smartArtTextPaneRowsPanel = null!;
    private Button _smartArtTextPaneAssistantButton = null!;
    private Button _smartArtTextPanePictureButton = null!;
    private Button _smartArtTextPaneClearPictureButton = null!;
    private WrapPanel _smartArtTextPaneOutlineActions = null!;
    private readonly List<Button> _smartArtTextPaneActionButtons = new();
    private Button _smartArtTextPaneApplyButton = null!;
    private Button _smartArtTextPaneCloseButton = null!;
    private bool _smartArtTextPaneRefreshing;

    internal PresentationCommentPanePlan? LastCommentPanePlan => _reviewWorkflowSession.LastCommentPanePlan;
    internal PresentationAccessibilityCheckerPanePlan? LastAccessibilityCheckerPanePlan =>
        _reviewWorkflowSession.LastAccessibilityCheckerPanePlan;
    internal PresentationTableStructureReviewDisplayPlan? LastTableStructureReviewDisplayPlan =>
        _reviewWorkflowSession.LastTableStructureReviewDisplayPlan;
    internal PresentationReadingOrderPlan? LastReadingOrderPlan => _reviewWorkflowSession.LastReadingOrderPlan;
    internal PresentationProofingPanePlan? LastProofingPanePlan => _reviewWorkflowSession.LastProofingPanePlan;
    internal PresentationMediaPaneHostCoordinator MediaPaneHost => _mediaPaneHostCoordinator;
    internal PresentationMediaCaptionAuthoringPanePlan? LastMediaCaptionAuthoringPanePlan =>
        _mediaPaneHostCoordinator.LastCaptionAuthoringPanePlan;
    internal PresentationDesignCommandPlan? LastLayoutRequestPlan { get; private set; }
    internal PresentationNotesPagePreviewPlan? LastNotesPagePreviewPlan { get; private set; }
    internal PresentationNotesPagePdfRenderPlan? LastNotesPagePdfRenderPlan { get; private set; }
    internal PresentationPrintOutputPackage? LastPrintOutputPackage { get; private set; }
    internal PresentationPrintBackstagePlan? LastPrintBackstagePlan { get; private set; }
    internal PresentationVideoExportPlan? LastVideoExportPlan { get; private set; }
    internal PresentationVideoFramePackage? LastVideoFramePackage { get; private set; }
    internal PresentationVideoExportHandoffPlan? LastVideoExportHandoffPlan { get; private set; }
    internal PresentationLayoutPickerPlan? LastLayoutPickerPlan { get; private set; }
    internal PresentationLayoutChoice? LastAppliedLayoutChoice { get; private set; }
    internal TableInsertionPickerPlan? LastTablePickerPlan { get; private set; }
    internal bool IsSmartArtTextPaneVisible =>
        _workareaSession.Panes.IsVisible(PresentationWorkareaPane.SmartArtText);
    internal bool IsAccessibilityCheckerPaneVisible =>
        _workareaSession.Panes.IsVisible(PresentationWorkareaPane.AccessibilityChecker);
    internal bool IsDirty => _fileSession.IsDirty;
    internal bool IsProofingPaneVisible =>
        _workareaSession.Panes.IsVisible(PresentationWorkareaPane.Proofing);
    internal bool IsMediaCaptionPaneVisible =>
        _workareaSession.Panes.IsVisible(PresentationWorkareaPane.MediaCaption);

    // ── Wave 16B: Animation pane (right-side collapsible panel) ──────────────────
    // 16B SEAM START — do not restructure this region (16A/16C may conflict nearby).
    private readonly AnimationPaneSession _animationPaneSession;
    private AnimationPane? _animPane;
    private Border         _animPaneHost = null!;  // collapsible right-side dock (~240px)
    private readonly PresentationPaneAccessibilityAdapter _paneAccessibility = new();

    internal bool IsAnimationPaneVisible =>
        _animPaneHost?.Visibility == Visibility.Visible;

    /// <summary>
    /// Test-seam: exposes the animation pane host border so tests can inspect visibility
    /// without launching the actual UI.  Internal; only visible to FreeP.App.Host.Tests.
    /// </summary>
    // 16B SEAM END

    // ── Constructors ──────────────────────────────────────────────────────────────

    public MainWindow() : this(new FreePOptions()) { }

    public MainWindow(
        FreePOptions options,
        IApplicationOptionsStore<FreePOptions>? optionsStore = null,
        IUserMessageService? messageService = null,
        PresentationNativePrintHandoffHostCapabilities? nativePrintCapability = null,
        IReadOnlyList<string>? startupFilePaths = null,
        bool suppressStartupRecoveryOffer = false,
        PresentationCustomDictionaryStore? proofingDictionaryStore = null,
        PresentationDocumentWindowPlanner? documentWindowPlanner = null,
        int documentWindowNumber = 1)
    {
        if (documentWindowNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(documentWindowNumber));

        _documentWindowPlanner = documentWindowPlanner ?? new PresentationDocumentWindowPlanner();
        _documentWindowNumber = documentWindowNumber;
        _options = options ?? new FreePOptions();
        _optionsRuntime = new FreePOptionsRuntimeSession(_options);
        _messageService = messageService;
        _optionsStore = optionsStore ?? new InMemoryApplicationOptionsStore<FreePOptions>(_options);

        Title = FreePApplicationFrameDescriptor.Title.ApplicationName;
        Width = 1280;
        Height = 760;
        WindowState = WindowState.Maximized;
        Background = FreePBrushes.SheetSurface;

        var chromeOptions = BuildChromeOptions();
        ShellChrome.ConfigureWindow(this, chromeOptions);

        _animationPaneSession = new(() => Editor);
        _workareaSession = new PresentationWorkareaSession(CreateWorkareaEndpoint());
        _proofingPaneNativeView = new(
            new PresentationProofingPaneNativeViewBindings<UIElement>(
                SetHeading: value => _proofingPaneHeading.Text = value,
                SetMessage: value => _proofingPaneMessage.Text = value,
                ClearRows: () => _proofingPaneRowsPanel.Children.Clear(),
                AddEmptyState: message => _proofingPaneRowsPanel.Children.Add(BuildProofingEmptyState(message)),
                BuildRow: BuildProofingIssueRowCard,
                AddRow: row => _proofingPaneRowsPanel.Children.Add(row)));
        _smartArtTextPaneNativeView = new(
            new PresentationSmartArtTextPaneNativeViewBindings<TextBox>(
                SetUpdating: value => _smartArtTextPaneRefreshing = value,
                ClearRows: () => _smartArtTextPaneRowsPanel.Children.Clear(),
                SetHeading: value => _smartArtTextPaneHeading.Text = value,
                SetMessage: value => _smartArtTextPaneMessage.Text = value,
                SetApplyEnabled: value => _smartArtTextPaneApplyButton.IsEnabled = value,
                SetAssistantEnabled: value => _smartArtTextPaneAssistantButton.IsEnabled = value,
                SetEditActionsEnabled: value =>
                {
                    foreach (var button in _smartArtTextPaneActionButtons)
                        button.IsEnabled = value;
                },
                BuildRow: BuildSmartArtTextPaneRow,
                ApplyAccessibility: PresentationPaneAccessibilityAdapter.ApplyItem,
                AddRow: row => _smartArtTextPaneRowsPanel.Children.Add(row)));
        _readingOrderPaneHostCoordinator = new(_workareaSession.Panes, this);
        _reviewWorkflowSession = new(
            () => Editor,
            new PresentationReviewWorkflowSessionCallbacks(
                MarkDirty: () => _fileSession.MarkDirty(),
                RefreshCanvas: RefreshCanvas,
                RefreshNotesPane: RefreshNotesPane,
                RenderAccessibilityCheckerPaneIfVisible: plan =>
                    _reviewPaneHostCoordinator.RenderAccessibilityPaneIfVisible(plan),
                PresentAccessibilityCheckerPane: plan =>
                    _reviewPaneHostCoordinator.PresentAccessibilityPane(plan),
                OpenAltTextPane: () => ShowAltTextPane(),
                OpenHyperlinkDialog: () => OpenHyperlinkDialog(),
                OpenMediaCaptionPane: () => MediaPaneHost.Show(),
                RenderCommentPane: RenderCommentPane,
                RenderAltTextPaneIfVisible: RenderAltTextPaneIfVisible,
                RenderReadingOrderPaneIfVisible: plan => _readingOrderPaneHostCoordinator.RenderIfVisible(plan),
                PresentReadingOrderPane: plan => _readingOrderPaneHostCoordinator.Present(plan),
                RenderProofingPaneIfVisible: plan =>
                    _reviewPaneHostCoordinator.RenderProofingPaneIfVisible(plan),
                PresentProofingPane: plan => _reviewPaneHostCoordinator.PresentProofingPane(plan),
                UpdateAfterCommentMutation: UpdateTitle,
                UpdateAfterCommentNavigation: UpdateSlideCount,
                UpdateAfterProofingCorrection: UpdateTitle),
            proofingDictionaryStore ?? PresentationCustomDictionaryStore.Load());
        _reviewPaneHostCoordinator = new(
            _reviewWorkflowSession,
            _workareaSession.Panes,
            new DelegatingPresentationMainWindowReviewPaneView(
                new PresentationMainWindowReviewPaneViewBindings(
                    IsAccessibilityPaneVisible: () => IsAccessibilityCheckerPaneVisible,
                    IsProofingPaneVisible: () => IsProofingPaneVisible,
                    SetAccessibilityPaneVisible: visible =>
                        _accessibilityCheckerPaneHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed,
                    SetProofingPaneVisible: visible =>
                        _proofingPaneHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed,
                    RenderAccessibilityPane: RenderAccessibilityCheckerPane,
                    RenderProofingPane: RenderProofingPane,
                    RefreshPaneAccessibilityMetadata: RefreshPaneAccessibilityMetadata)));
        _altTextPaneHostCoordinator = new(
            _reviewWorkflowSession,
            _workareaSession.Panes,
            this);
        _smartArtTextPaneSession = new(
            () => Editor,
            new PresentationSmartArtTextPaneSessionCallbacks(
                MarkDirty: () => _fileSession.MarkDirty(),
                RefreshCanvas: RefreshCanvas,
                UpdateHost: UpdateTitle,
                RenderPane: RenderSmartArtTextPane));
        _zoomAuthoringSession = new(
            () => Editor,
            new PresentationZoomAuthoringSessionCallbacks(
                MarkDirty: () => _fileSession.MarkDirty(),
                RefreshCanvas: RefreshCanvas,
                UpdateHost: UpdateTitle,
                RenderSlidePreview: (presentation, slideIndex, widthPx, heightPx) =>
                    WpfPresentationSlideImageRenderer.RenderSlideToPng(
                        presentation,
                        slideIndex,
                        widthPx,
                        heightPx)));
        _domainContextMenuSession = new(
            () => Editor,
            new PresentationDomainContextMenuSessionCallbacks(
                OpenChartPointOptions: (seriesIndex, pointIndex) =>
                    OpenChartPointOptionsDialog(seriesIndex, pointIndex),
                OpenChartSeriesOptions: seriesIndex => OpenChartSeriesOptionsDialog(seriesIndex),
                OpenChartAxisOptions: axisKind => OpenChartAxisOptionsDialog(axisKind),
                OpenChartTextOptions: textTarget => OpenChartTextOptionsDialog(textTarget),
                OpenChartAreaOptions: areaTarget => OpenChartAreaOptionsDialog(areaTarget),
                OpenChartOptions: OpenChartDisplayOptionsDialog));
        _notesPaneSession = new(() => Editor);
        _hyperlinkWorkflowSession = new(() => Editor);
        _customShowSession = new(() => Editor);

        // File commands.
        _fileSession = WpfPresentationFileCommandSessionFactory.Create(
            this,
            () => _presentation,
            LoadModel,
            UpdateTitle,
            _options,
            messageService: _messageService,
            getImageExportRange: () => PresentationExportPlanner.BuildCurrentSlideRangeRequest(Editor.CurrentSlideIndex),
            getPrintCurrentSlideNumber: () => Editor.CurrentSlideIndex + 1,
            nativePrintCapability: nativePrintCapability);

        // Autosave / crash recovery. Before this, a FreeP crash lost every edit back to the last
        // manual save: there was no periodic snapshot, no emergency snapshot, and no startup
        // recovery offer at all. Mirrors FreeW's AutosaveCoordinator wiring.
        _autosave = new AutosaveCoordinator(
            () => _presentation,
            _fileSession,
            recoverInNewWindow: OpenNewWindowWithRecoveredSnapshot);

        // Title bar.
        var titleBar = ShellChrome.BuildTitleBar(this, chromeOptions);
        _titleBar = titleBar.Root;
        _titleBinder = new SisterWpfWindowTitleBinder(this, titleBar.TitleText);
        AddQuickAccessButtons(titleBar.QatHost);

        // Ribbon. Wave 4C passes the slideshow launch Actions into the command registry;
        // StartSlideShow (Wave 4B) opens the fullscreen SlideShowWindow.
        _ribbonBindingSession = new FreePRibbonBindingSession(
            Editor,
            _ribbonStateStore,
            CreateRibbonHostProfile);
        var ribbon = BuildRibbon(
            FreeP.Ribbon.Definitions.FreePRibbon.Build(FreeP.Ribbon.Definitions.FreePRibbonCapabilities.Wpf),
            _ribbonBindingSession.Registry,
            _ribbonBindingSession.StateStore);
        RefreshContextualTabs();

        // Body: slide pane + stage.
        var body = BuildBody();
        _wpfMediaPaneHostView = BuildWpfMediaPaneHostView();
        _mediaPaneHostCoordinator = new(
            new PresentationMediaPaneSession(
                () => Editor,
                new PresentationMediaPaneSessionCallbacks(
                    MarkDirty: () => _fileSession.MarkDirty(),
                    RefreshReviewWorkflowPlans: RefreshReviewWorkflowPlans,
                    UpdateHost: UpdateTitle)),
            _workareaSession.Panes,
            _wpfMediaPaneHostView);
        RefreshPaneAccessibilityMetadata();
        BindMediaPaneEvents();

        // Status bar.
        var status = BuildStatusBar();
        var clientFrame = SisterAppClientFrameBuilder.Build(new SisterAppClientFrameSpec(
            Chrome: ribbon,
            WorkArea: body,
            StatusBar: status));
        var root = clientFrame.Root;

        InstallSharedKeyboardShortcuts();

        Closing += (_, e) =>
        {
            if (!_fileSession.ConfirmCloseAllowedAsync().GetAwaiter().GetResult())
            {
                e.Cancel = true;
                return;
            }

            // Only stop autosave (which deletes this window's recovery snapshot) once we have
            // committed to closing -- a cancelled close must keep its snapshot.
            _autosave.Stop();
        };

        Loaded += (_, _) =>
        {
            // Windows opened BY the recovery loop pass suppressStartupRecoveryOffer:true so they do
            // not re-prompt for the very candidates the opening window is already working through.
            if (!suppressStartupRecoveryOffer)
                _autosave.OfferRecovery(this);
            _autosave.Start();
        };

        // Backstage. OnClosed restores keyboard focus to the slide canvas -- without it, Esc/back-arrow
        // (or any Backstage action that dismisses the overlay) leaves focus on nothing, so arrow keys,
        // Delete, and typed text stop reaching the slide until the user clicks a slide/shape manually.
        _backstage = new BackstageView(BuildBackstageEndpoints(), onClosed: () => SlideCanvas.Focus());

        var frame = SisterAppWindowFrameBuilder.Build(new SisterAppWindowFrameSpec(_titleBar, root, _backstage));
        Content = frame.Root;

        Closed += (_, _) => _workareaSession.Dispose();
        _workareaSession.Initialize();
        var startupOpenSession = new PresentationStartupOpenSession(_fileSession);
        var startupOpenPlan = startupOpenSession.Plan(startupFilePaths ?? []);
        var primaryStartupEntry = startupOpenPlan.Entries.FirstOrDefault(entry => !entry.OpenInNewWindow);
        if (primaryStartupEntry is not null)
            RunFileCommand(startupOpenSession.OpenAsync(primaryStartupEntry));
        else
            RunOptionalFileCommand(startupOpenSession.ReportFirstUnopenableAsync(startupOpenPlan));

        var additionalStartupEntries = startupOpenPlan.Entries
            .Where(entry => entry.OpenInNewWindow)
            .ToArray();
        if (additionalStartupEntries.Length > 0)
            Loaded += (_, _) => OpenAdditionalStartupPresentations(additionalStartupEntries);
    }

    // ── Editor construction ───────────────────────────────────────────────────────

    // ── 3C SEAM: canvas editing attachment ───────────────────────────────────────

    /// <summary>
    /// Wires the gesture handler and in-canvas text editor to the current Editor.
    /// Called once from BuildBody and again when the workarea session replaces its editor.
    ///
    /// 3C SEAM LINE: this single call to <see cref="SlideCanvas.AttachEditing"/> is the
    /// only change to MainWindow needed for Wave 3C.
    /// </summary>
    private void AttachCanvasEditing()
    {
        // _textOverlay may be null during the very first call from BuildBody before
        // the field is assigned; BuildBody itself calls this after assigning it.
        if (_textOverlay is null) return;
        SlideCanvas.MouseRightButtonUp -= OnSlideCanvasMouseRightButtonUp;
        SlideCanvas.MouseRightButtonUp += OnSlideCanvasMouseRightButtonUp;
        SlideCanvas.AttachEditing(
            Editor,
            _textOverlay,
            TryOpenOleInPlace,
            OnChartPointDoubleClick,
            ReportClipboardWriteFailure,
            // Inline embedded objects live inside a shape's rich text and are hosted by the
            // converter, not by TryOpenOleInPlace above; without this the native commit of an
            // inline object left the document clean.
            onInlineOlePayloadUpdated: _ => MarkDirtyFromAnyThread(),
            // Slide-level external activation: without this a double-clicked embedded object
            // edited in its own application saved into the model and left the document clean,
            // because the coordinator's default route reports nothing.
            tryActivateOleExternally: TryActivateOleExternally);
        SlideCanvas.ApplyViewShowState(_viewShowState);
    }

    /// <summary>
    /// Opens a slide-level embedded object in its associated application, the route a double-click
    /// takes when in-place activation is unavailable. Reporting the application's save is what
    /// separates this from the activation coordinator's default route, which silently drops it.
    /// </summary>
    private bool TryActivateOleExternally(OleObjectInfo? oleObject) =>
        OleActivationService.TryActivate(
            oleObject,
            onPayloadUpdated: _ => MarkDirtyFromAnyThread());

    /// <summary>
    /// Marks the document dirty from whichever thread an OLE payload commit arrives on. In-place
    /// commits arrive on the dispatcher, but an external application's save is reported from the
    /// activation session's continuation -- and the shell's change notification updates window
    /// chrome, so an unmarshalled call throws where the session swallows it, leaving a clean title
    /// over a dirty document.
    /// </summary>
    private void MarkDirtyFromAnyThread()
    {
        if (Dispatcher.CheckAccess())
            _fileSession.MarkDirty();
        else
            Dispatcher.InvokeAsync(() => _fileSession.MarkDirty());
    }

    private bool TryOpenOleInPlace(SlideShape shape)
    {
        if (_oleOverlay is null)
            return false;

        var transform = SlideCanvas.CurrentTransform;
        var margin = SlideCanvas.Margin;
        var plan = OleActivationCoordinator.PlanInPlaceActivation(
            shape,
            new SlideTransformCore(
                transform.Scale,
                transform.OffsetX,
                transform.OffsetY,
                transform.SlideWidthDip,
                transform.SlideHeightDip),
            margin.Left,
            margin.Top);
        if (plan is null)
            return false;

        CloseActiveOleHost();
        var bounds = new Rect(
            plan.Bounds.Left,
            plan.Bounds.Top,
            plan.Bounds.Width,
            plan.Bounds.Height);

        return WpfOleInPlaceHost.TryShow(
            _oleOverlay,
            plan.OleObject,
            bounds,
            out _activeOleHost,
            onPayloadUpdated: _ => MarkDirtyFromAnyThread());
    }

    private void CloseActiveOleHost()
    {
        if (_activeOleHost is null)
            return;

        _activeOleHost.Dispose();
        _oleOverlay.Children.Remove(_activeOleHost);
        _activeOleHost = null;
    }

    private void OnSlideCanvasMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var screenPoint = e.GetPosition(SlideCanvas);
        var slidePoint = SlideCanvas.CurrentTransform.ScreenToSlide(screenPoint.X, screenPoint.Y);
        var plan = _domainContextMenuSession.BuildAtSlidePoint(slidePoint.X, slidePoint.Y);
        if (plan is null)
            return;

        var menu = BuildDomainContextMenu(plan);
        menu.PlacementTarget = SlideCanvas;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private ContextMenu BuildDomainContextMenu(PresentationDomainContextMenuPlan plan)
    {
        var menu = new ContextMenu
        {
            Placement = PlacementMode.MousePoint,
        };
        PresentationDomainContextMenuNativeAdapter.Populate(
            plan,
            menu,
            new PresentationDomainContextMenuNativeBindings<ContextMenu, MenuItem>(
                CreateItem: entry => new MenuItem
                {
                    Header = entry.Text,
                    IsEnabled = entry.IsEnabled,
                },
                AddRootSeparator: target => target.Items.Add(new Separator()),
                AddRootItem: (target, item) => target.Items.Add(item),
                AddChildSeparator: parent => parent.Items.Add(new Separator()),
                AddChildItem: (parent, item) => parent.Items.Add(item),
                BindExecute: (item, execute) => item.Click += (_, _) => execute()),
            action => _domainContextMenuSession.Execute(action, TryExecuteInlineTableAction));
        return menu;
    }

    private bool TryExecuteInlineTableAction(PresentationDomainContextAction action) =>
        SlideCanvas?.TableCellEditor?.TryExecuteActiveTableStructureAction(action.Kind) == true;



    // ── File load ─────────────────────────────────────────────────────────────────

    private void ApplyPresentationViewShowState(PresentationViewShowState state)
    {
        _viewShowState = state;
        if (_notesBox is not null)
            _notesBox.Visibility = _viewModeState.Mode == PresentationViewMode.SlideSorter || !state.ShowNotesPane
                ? Visibility.Collapsed
                : Visibility.Visible;
        if (SlideCanvas is not null)
            SlideCanvas.ApplyViewShowState(state);
    }

    private void ApplyPresentationViewZoomState(PresentationViewZoomState state)
    {
        _viewZoomState = state;
        if (SlideCanvas is not null)
            SlideCanvas.ApplyViewZoomState(state);
    }

    private void ApplyPresentationViewModeState(PresentationViewModeState state)
    {
        _viewModeState = state;
        var isOutline = state.Mode == PresentationViewMode.Outline;
        var isSlideSorter = state.Mode == PresentationViewMode.SlideSorter;
        var isNotesPage = state.Mode == PresentationViewMode.NotesPage;
        if (SlidePaneHost is null || _canvasHost is null || _bodySplitter is null)
            return;

        SlidePaneHost.Width = isSlideSorter || isNotesPage ? double.NaN : FreePShellVisualMetrics.SlidePaneWidth;
        _bodySplitter.ColumnDefinitions[0].Width = isSlideSorter
            ? new GridLength(1, GridUnitType.Star)
            : isNotesPage ? new GridLength(0) : GridLength.Auto;
        _bodySplitter.ColumnDefinitions[1].Width = isSlideSorter
            ? new GridLength(0)
            : new GridLength(1, GridUnitType.Star);
        _canvasHost.Visibility = isSlideSorter ? Visibility.Collapsed : Visibility.Visible;
        _notesBox.Visibility = !isSlideSorter && _viewShowState.ShowNotesPane
            ? Visibility.Visible
            : Visibility.Collapsed;
        _canvasHost.Background = isNotesPage ? FreePBrushes.White : FreePBrushes.PlaceholderSurface;
        _notesBox.MaxHeight = isNotesPage ? 300 : 120;
        _notesBox.Background = isNotesPage ? FreePBrushes.White : FreePBrushes.NotesHintSurface;
        SlidePaneHost.Child = isOutline ? _outlinePane : _slidePane;
        _slidePane.SetSlideSorterMode(isSlideSorter);
        if (isOutline)
            _outlinePane.RefreshProjection();
        SyncRibbonCommandStates();
    }

    private void ApplyPresentationViewColorModeState(PresentationViewColorModeState state)
    {
        _viewColorModeState = state;
        SlideCanvas?.ApplyViewColorModeState(state);
    }

    private void LoadModel(Presentation presentation)
    {
        _workareaSession.ReplacePresentation(presentation);
    }

    private void OpenNewPresentationWindow()
    {
        var plan = _documentWindowPlanner.CreateNext(
            _presentation,
            _fileSession.CurrentPath,
            _fileSession.IsDirty);
        var window = new MainWindow(
            _options,
            _optionsStore,
            _messageService,
            documentWindowPlanner: _documentWindowPlanner,
            documentWindowNumber: plan.WindowNumber);
        window.LoadModel(plan.Presentation);
        window._fileSession.ApplyWindowState(plan.CurrentPath, plan.IsDirty);
        window.Show();
    }

    private static void ArrangeAllPresentationWindows()
    {
        var windows = Application.Current.Windows.OfType<MainWindow>()
            .Where(window => window.IsVisible)
            .ToList();
        if (windows.Count == 0)
            return;

        var area = SystemParameters.WorkArea;
        var bounds = ArrangeAllLayoutPlanner.ArrangeRowFirst(
            area.Width, area.Height, windows.Count, maxColumns: 3);
        for (var index = 0; index < bounds.Count; index++)
        {
            var window = windows[index];
            var tile = bounds[index];
            window.WindowState = WindowState.Normal;
            window.Left = area.Left + tile.X;
            window.Top = area.Top + tile.Y;
            window.Width = tile.Width;
            window.Height = tile.Height;
        }
    }

    private static void CascadePresentationWindows()
    {
        var windows = Application.Current.Windows.OfType<MainWindow>()
            .Where(window => window.IsVisible)
            .ToList();
        if (windows.Count == 0)
            return;

        var area = SystemParameters.WorkArea;
        var bounds = ArrangeAllLayoutPlanner.Arrange(
            ShellWindowArrangement.Cascade, area.Width, area.Height, windows.Count);
        for (var index = 0; index < bounds.Count; index++)
        {
            var window = windows[index];
            var tile = bounds[index];
            window.WindowState = WindowState.Normal;
            window.Left = area.Left + tile.X;
            window.Top = area.Top + tile.Y;
            window.Width = tile.Width;
            window.Height = tile.Height;
        }
    }

    /// <summary>
    /// Shows the View &gt; Window live window list. This is deliberately a native menu rather than a
    /// static ribbon dropdown: its entries must track independently-created FreeP document windows.
    /// </summary>
    private void ShowPresentationWindowPicker()
    {
        var windows = Application.Current.Windows.OfType<MainWindow>()
            .Where(window => window.IsVisible)
            .ToList();
        if (windows.Count == 0)
            return;

        var menu = new ContextMenu
        {
            PlacementTarget = this,
            Placement = PlacementMode.Bottom,
        };
        foreach (var window in windows)
        {
            var target = window;
            var item = new MenuItem
            {
                Header = target.Title,
                IsCheckable = true,
                IsChecked = ReferenceEquals(target, this),
            };
            item.Click += (_, _) => ActivatePresentationWindow(target);
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private static void ActivatePresentationWindow(MainWindow window)
    {
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;

        window.Activate();
        window.Focus();
    }

    // ── Body layout ───────────────────────────────────────────────────────────────

    private UIElement BuildBody()
    {
        // LEFT pane host — Wave 3B fills this container with the thumbnail/sorter pane.
        // <!-- 3B SEAM: set SlidePaneHost.Child = your thumbnail panel here. -->
        SlidePaneHost = new Border
        {
            Width      = FreePShellVisualMetrics.SlidePaneWidth,
            Background = FreePBrushes.CardBorder,
        };
        // 3B SEAM: attach the slide-thumbnail pane.
        _slidePane = new SlidePane(_workareaSession);
        _outlinePane = new PresentationOutlinePane(_workareaSession);
        SlidePaneHost.Child = _slidePane;

        // CENTRE stage — the canvas proper.
        SlideCanvas = new SlideCanvas
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Stretch,
            Margin              = new Thickness(FreePShellVisualMetrics.CanvasMargin)
        };

        // 3C SEAM: text-edit overlay Canvas (sits on top of the canvas, same coordinate space).
        _textOverlay = new Canvas
        {
            IsHitTestVisible    = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Stretch
        };

        _oleOverlay = new Canvas
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        // Wave 11B: comment indicator overlay (speech-bubble dots, non-interactive).
        _commentOverlay = new Canvas
        {
            IsHitTestVisible    = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Stretch
        };
        _commentOverlay.SizeChanged += (_, _) =>
            DrawCommentDots(LastCommentPanePlan?.Comments ?? []);

        // Wrap canvas + overlays in a Grid so the overlays occupy the same bounds.
        var stageGrid = new Grid();
        stageGrid.Children.Add(SlideCanvas);
        stageGrid.Children.Add(_textOverlay);
        stageGrid.Children.Add(_commentOverlay);
        stageGrid.Children.Add(_oleOverlay);

        // AdornerDecorator ensures the adorner layer sits directly above SlideCanvas,
        // so SelectionAdorner handles are positioned correctly regardless of zoom.
        var adornerDecorator = new AdornerDecorator { Child = stageGrid };

        _canvasHost = new Border
        {
            Background = FreePBrushes.PlaceholderSurface,
            ClipToBounds = true,
            Child      = adornerDecorator
        };

        // 3C SEAM: attach gesture handler and text editor.
        // Called here after canvas construction and by the workarea endpoint after New/Open.
        AttachCanvasEditing();

        // Notes pane — a slim strip below the slide canvas.
        // Edits go through EditingSession so they are undoable and mark the file dirty.
        _notesBox = new TextBox
        {
            AcceptsReturn       = true,
            TextWrapping        = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight           = FreePShellVisualMetrics.NotesPaneHeight,
            MaxHeight           = 120,
            Padding             = new Thickness(8, 4, 8, 4),
            FontSize            = 12,
            Background          = FreePBrushes.NotesHintSurface,
            BorderThickness     = new Thickness(0, 1, 0, 0),
            BorderBrush         = FreePBrushes.PaneBorder,
            VerticalAlignment   = VerticalAlignment.Stretch,
        };
        _notesBox.TextChanged += (_, _) =>
        {
            if (_notesRefreshing) return;
            var result = _notesPaneSession.ApplyText(_notesBox.Text);
            LastNotesPagePreviewPlan = result.Plan.Preview;
        };
        _notesBox.KeyDown += OnNotesKeyDown;

        // Wave 11B: comment list pane — a collapsible strip above the notes pane.
        // It is hidden when the current slide has no comments.
        _commentListPanel = new StackPanel { Orientation = Orientation.Vertical };
        _commentListHost = new Border
        {
            BorderBrush     = FreePBrushes.PaneBorder,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Background      = FreePBrushes.NotesSurface,
            MaxHeight       = 100,
            Child           = new ScrollViewer
            {
                // Avalonia uses overlay scrolling for this compact strip. Keep
                // WPF scrollable without reserving a different right-side gutter.
                VerticalScrollBarVisibility   = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content                       = _commentListPanel,
            },
            Visibility      = Visibility.Collapsed,
        };

        _layoutPickerPanel = new StackPanel { Orientation = Orientation.Vertical };
        _layoutPickerHost = new Border
        {
            BorderBrush     = FreePBrushes.PaneBorder,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Background      = FreePBrushes.White,
            MaxHeight       = 220,
            Visibility      = Visibility.Collapsed,
            Child           = new ScrollViewer
            {
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content                       = _layoutPickerPanel,
            },
        };
        _tablePickerGrid = new UniformGrid
        {
            Rows = TableInsertionPickerPlanner.DefaultMaxRows,
            Columns = TableInsertionPickerPlanner.DefaultMaxColumns,
            Margin = new Thickness(8),
        };
        _tablePickerHost = new Border
        {
            BorderBrush     = FreePBrushes.PaneBorder,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Background      = FreePBrushes.White,
            Visibility      = Visibility.Collapsed,
            Child           = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Children =
                {
                    new TextBlock
                    {
                        Text = TableInsertionPickerPlanner.PickerHeading,
                        Margin = new Thickness(10, 8, 10, 2),
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = FreePBrushes.PaneText,
                    },
                    _tablePickerGrid,
                },
            },
        };

        _altTextPaneHost = BuildAltTextPaneHost();
        _accessibilityCheckerPaneHost = BuildAccessibilityCheckerPaneHost();

        // Right-side panel: canvas on top, picker/comment strips, notes strip below.
        var rightPanel = new Grid();
        rightPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rightPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rightPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rightPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rightPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_canvasHost,       0);
        Grid.SetRow(_layoutPickerHost, 1);
        Grid.SetRow(_tablePickerHost,  2);
        Grid.SetRow(_commentListHost,  3);
        Grid.SetRow(_notesBox,         4);
        rightPanel.Children.Add(_canvasHost);
        rightPanel.Children.Add(_layoutPickerHost);
        rightPanel.Children.Add(_tablePickerHost);
        rightPanel.Children.Add(_commentListHost);
        rightPanel.Children.Add(_notesBox);

        // ── Wave 16B: Animation pane host (right-side, hidden by default) ────────
        // 16B SEAM: _animPaneHost is inserted as column 2. It starts Collapsed so the
        // layout is unchanged until the ribbon toggle is pressed. Width=240 is a visual
        // guideline — the Border has no explicit Width so it sizes to content when shown.
        _animPaneHost = new Border
        {
            Width      = 240,
            Visibility = Visibility.Collapsed,
            Background = FreePBrushes.DisabledSurface,
        };
        // AnimationPane itself is created lazily on first show (ToggleAnimationPane).
        // END 16B SEAM

        _readingOrderPaneHost = BuildReadingOrderPaneHost();
        _selectionPane = new SelectionPane(Editor, RefreshPaneAccessibilityMetadata);
        _selectionPane.Refresh();
        _proofingPaneHost = BuildProofingPaneHost();
        _mediaCaptionPaneHost = BuildMediaCaptionPaneHost();
        _smartArtTextPaneHost = BuildSmartArtTextPaneHost();

        var splitter = _bodySplitter = new Grid();
        splitter.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        splitter.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        splitter.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        splitter.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        splitter.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        splitter.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        splitter.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        splitter.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        splitter.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // selection pane
        splitter.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 16B: anim pane
        Grid.SetColumn(SlidePaneHost,  0);
        Grid.SetColumn(rightPanel,     1);
        Grid.SetColumn(_accessibilityCheckerPaneHost, 2);
        Grid.SetColumn(_altTextPaneHost, 3);
        Grid.SetColumn(_readingOrderPaneHost, 4);
        Grid.SetColumn(_proofingPaneHost, 5);
        Grid.SetColumn(_mediaCaptionPaneHost, 6);
        Grid.SetColumn(_smartArtTextPaneHost, 7);
        Grid.SetColumn(_selectionPane, 8);
        Grid.SetColumn(_animPaneHost,  9); // 16B
        splitter.Children.Add(SlidePaneHost);
        splitter.Children.Add(rightPanel);
        splitter.Children.Add(_accessibilityCheckerPaneHost);
        splitter.Children.Add(_altTextPaneHost);
        splitter.Children.Add(_readingOrderPaneHost);
        splitter.Children.Add(_proofingPaneHost);
        splitter.Children.Add(_mediaCaptionPaneHost);
        splitter.Children.Add(_smartArtTextPaneHost);
        splitter.Children.Add(_selectionPane);
        splitter.Children.Add(_animPaneHost); // 16B

        RefreshPaneAccessibilityMetadata();

        return splitter;
    }

    private void RefreshPaneAccessibilityMetadata()
    {
        if (SlidePaneHost is null || _notesBox is null || _commentListHost is null
            || _accessibilityCheckerPaneHost is null || _altTextPaneHost is null
            || _readingOrderPaneHost is null || _proofingPaneHost is null
            || _mediaCaptionPaneHost is null || _smartArtTextPaneHost is null
            || _selectionPane is null || _animPaneHost is null
            || _mediaPaneHostCoordinator is null)
            return;

        var smartArtItemCount = _smartArtTextPaneRowsPanel?.Children.Count ?? 0;
        var selectionPlan = _selectionPane.CurrentPlan;
        var animationPlan = _animPane?.CurrentTimelinePlan;
        var selectedSmartArtIndex = _smartArtTextPaneRowsPanel?.Children.IndexOf(
            _smartArtTextPaneRowsPanel.Children.OfType<TextBox>().FirstOrDefault(box =>
                box.Tag is SmartArtNodeOutlineItem item &&
                StringComparer.Ordinal.Equals(item.ModelId, _smartArtTextPaneSession.SelectedModelId))) ?? -1;
        var states = PresentationMainWindowPaneAccessibilityPlan.Build(
            _reviewWorkflowSession,
            _mediaPaneHostCoordinator,
            _workareaSession.Panes,
            _presentation.Slides.Count,
            Editor.CurrentSlideIndex,
            new(
                _accessibilityCheckerRowsPanel?.Children.Count ?? 0,
                _readingOrderPaneItemsPanel?.Children.Count ?? 0,
                _proofingPaneRowsPanel?.Children.Count ?? 0,
                _mediaCaptionTrackBox?.Items.Count ?? 0,
                _mediaCaptionTrackBox?.SelectedIndex ?? -1,
                smartArtItemCount,
                selectedSmartArtIndex,
                selectionPlan.Items.Count,
                selectionPlan.SelectedItemIndex,
                _animPaneHost.Visibility == Visibility.Visible,
                animationPlan?.Items.Count ?? 0,
                animationPlan?.SelectedIndex ?? -1));
        FrameworkElement[] controls =
        [
            SlidePaneHost, _notesBox, _commentListHost, _accessibilityCheckerPaneHost,
            _altTextPaneHost, _readingOrderPaneHost, _proofingPaneHost, _mediaCaptionPaneHost,
            _smartArtTextPaneHost, _selectionPane, _animPaneHost,
        ];

        for (var index = 0; index < states.Count; index++)
        {
            var state = states[index];
            _paneAccessibility.ApplyPane(
                controls[index], state.PaneId, state.IsVisible, state.ItemCount, state.SelectedIndex);
        }
    }

    private Border BuildMediaCaptionPaneHost()
    {
        _mediaCaptionPaneHeading = new TextBlock
        {
            Text = PresentationPaneTextResources.MediaCaptionsHeading,
            FontSize = PresentationMediaPaneVisualMetrics.HeadingFontSize,
            FontWeight = FontWeights.SemiBold,
            Margin = MediaPaneMargin(
                PresentationMediaPaneVisualMetrics.HeadingTopMargin,
                PresentationMediaPaneVisualMetrics.HeadingBottomMargin),
        };
        _mediaCaptionPaneMessage = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = FreePBrushes.PaneSecondaryText,
            Margin = MediaPaneMargin(0, PresentationMediaPaneVisualMetrics.MessageBottomMargin),
        };
        _mediaCaptionTrackBox = new ComboBox
        {
            Margin = MediaPaneMargin(0, PresentationMediaPaneVisualMetrics.TrackBottomMargin),
            MinHeight = PresentationMediaPaneVisualMetrics.CompactControlHeight,
        };

        _mediaCaptionLabelText = BuildMediaCaptionPaneLabel();
        _mediaCaptionLabelBox = BuildMediaCaptionPaneTextBox(singleLine: true);
        _mediaCaptionLanguageText = BuildMediaCaptionPaneLabel();
        _mediaCaptionLanguageBox = BuildMediaCaptionPaneTextBox(singleLine: true);
        _mediaCaptionSourceText = BuildMediaCaptionPaneLabel();
        _mediaCaptionSourceBox = BuildMediaCaptionPaneTextBox(singleLine: true);
        _mediaCaptionTranscriptText = BuildMediaCaptionPaneLabel();
        _mediaCaptionTranscriptBox = BuildMediaCaptionPaneTextBox(singleLine: false);
        _mediaVolumeText = BuildMediaCaptionPaneLabel();
        _mediaVolumeText.Text = PresentationPaneTextResources.PlaybackVolume;
        _mediaVolumeSlider = new Slider
        {
            Minimum = PresentationMediaPaneSession.MinimumVolumePercent,
            Maximum = PresentationMediaPaneSession.MaximumVolumePercent,
            TickFrequency = PresentationMediaPaneSession.VolumeTickFrequency,
            IsSnapToTickEnabled = PresentationMediaPaneSession.SnapVolumeToTicks,
            Margin = MediaPaneMargin(0, PresentationMediaPaneVisualMetrics.FieldBottomMargin),
        };
        _mediaVolumeApplyButton = BuildMediaCaptionPaneButton();
        _mediaVolumeApplyButton.Content = PresentationPaneTextResources.ApplyVolume;
        _mediaStartModeText = BuildMediaCaptionPaneLabel();
        _mediaStartModeText.Text = PresentationPaneTextResources.PlaybackStart;
        _mediaStartModeBox = new ComboBox
        {
            Margin = MediaPaneMargin(0, PresentationMediaPaneVisualMetrics.FieldBottomMargin),
            MinHeight = PresentationMediaPaneVisualMetrics.CompactControlHeight,
            Items =
            {
                new ComboBoxItem
                {
                    Content = PresentationPaneTextResources.MediaPlaybackStartOptions[0].Label,
                    Tag = PresentationPaneTextResources.MediaPlaybackStartOptions[0].Mode,
                },
                new ComboBoxItem
                {
                    Content = PresentationPaneTextResources.MediaPlaybackStartOptions[1].Label,
                    Tag = PresentationPaneTextResources.MediaPlaybackStartOptions[1].Mode,
                },
            },
        };
        _mediaLoopCheckBox = BuildMediaPaneToggle(PresentationMediaPaneControlCatalog.Loop);
        _mediaShowWhenStoppedCheckBox = BuildMediaPaneToggle(PresentationMediaPaneControlCatalog.ShowWhenStopped);
        _mediaRewindAfterPlayingCheckBox = BuildMediaPaneToggle(PresentationMediaPaneControlCatalog.RewindAfterPlaying);
        _mediaPlayFullScreenCheckBox = BuildMediaPaneToggle(PresentationMediaPaneControlCatalog.PlayFullScreen);
        (_mediaStopAfterSlidesText, _mediaStopAfterSlidesBox) =
            BuildMediaPaneTextControl(PresentationMediaPaneControlCatalog.StopAfterSlides);
        _mediaPlaybackApplyButton = BuildMediaCaptionPaneButton();
        _mediaPlaybackApplyButton.Content = PresentationPaneTextResources.ApplyPlayback;
        (_mediaTrimStartText, _mediaTrimStartBox) =
            BuildMediaPaneTextControl(PresentationMediaPaneControlCatalog.TrimStart);
        (_mediaTrimEndText, _mediaTrimEndBox) =
            BuildMediaPaneTextControl(PresentationMediaPaneControlCatalog.TrimEnd);
        (_mediaFadeInText, _mediaFadeInBox) =
            BuildMediaPaneTextControl(PresentationMediaPaneControlCatalog.FadeIn);
        (_mediaFadeOutText, _mediaFadeOutBox) =
            BuildMediaPaneTextControl(PresentationMediaPaneControlCatalog.FadeOut);
        _mediaTimingApplyButton = BuildMediaCaptionPaneButton();
        _mediaTimingApplyButton.Content = PresentationPaneTextResources.ApplyTiming;
        _mediaBookmarkText = BuildMediaCaptionPaneLabel();
        _mediaBookmarkText.Text = PresentationPaneTextResources.MediaBookmarks;
        _mediaBookmarkBox = new ComboBox
        {
            Margin = MediaPaneMargin(0, PresentationMediaPaneVisualMetrics.FieldBottomMargin),
            MinHeight = PresentationMediaPaneVisualMetrics.CompactControlHeight,
        };
        (_mediaBookmarkNameText, _mediaBookmarkNameBox) =
            BuildMediaPaneTextControl(PresentationMediaPaneControlCatalog.BookmarkName);
        (_mediaBookmarkTimeText, _mediaBookmarkTimeBox) =
            BuildMediaPaneTextControl(PresentationMediaPaneControlCatalog.BookmarkTime);
        _mediaBookmarkCreateButton = BuildMediaCaptionPaneButton();
        _mediaBookmarkCreateButton.Content = PresentationPaneTextResources.AddBookmark;
        _mediaBookmarkReplaceButton = BuildMediaCaptionPaneButton();
        _mediaBookmarkReplaceButton.Content = PresentationPaneTextResources.ReplaceBookmark;
        _mediaBookmarkDeleteButton = BuildMediaCaptionPaneButton();
        _mediaBookmarkDeleteButton.Content = PresentationPaneTextResources.DeleteBookmark;
        _mediaCaptionCreateButton = BuildMediaCaptionPaneButton();
        _mediaCaptionReplaceButton = BuildMediaCaptionPaneButton();
        _mediaCaptionDeleteButton = BuildMediaCaptionPaneButton();
        _mediaCaptionCloseButton = BuildMediaCaptionPaneButton();
        _mediaCaptionControls = new(
            _mediaCaptionLabelText, _mediaCaptionLabelBox,
            _mediaCaptionLanguageText, _mediaCaptionLanguageBox,
            _mediaCaptionSourceText, _mediaCaptionSourceBox,
            _mediaCaptionTranscriptText, _mediaCaptionTranscriptBox);
        _mediaPaneButtons = new(
            _mediaVolumeApplyButton, _mediaPlaybackApplyButton, _mediaTimingApplyButton,
            _mediaBookmarkCreateButton, _mediaBookmarkReplaceButton, _mediaBookmarkDeleteButton,
            _mediaCaptionCreateButton, _mediaCaptionReplaceButton, _mediaCaptionDeleteButton,
            _mediaCaptionCloseButton);

        var buttons = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = MediaPaneMargin(
                PresentationMediaPaneVisualMetrics.ActionRowTopMargin,
                PresentationMediaPaneVisualMetrics.ActionRowBottomMargin),
        };
        foreach (var button in _mediaPaneButtons.InVisualOrder)
            buttons.Children.Add(button);

        var panel = new StackPanel { Orientation = Orientation.Vertical };
        panel.Children.Add(_mediaCaptionPaneHeading);
        panel.Children.Add(_mediaCaptionPaneMessage);
        panel.Children.Add(_mediaCaptionTrackBox);
        panel.Children.Add(_mediaCaptionLabelText);
        panel.Children.Add(_mediaCaptionLabelBox);
        panel.Children.Add(_mediaCaptionLanguageText);
        panel.Children.Add(_mediaCaptionLanguageBox);
        panel.Children.Add(_mediaCaptionSourceText);
        panel.Children.Add(_mediaCaptionSourceBox);
        panel.Children.Add(_mediaCaptionTranscriptText);
        panel.Children.Add(_mediaCaptionTranscriptBox);
        panel.Children.Add(_mediaStartModeText);
        panel.Children.Add(_mediaStartModeBox);
        panel.Children.Add(_mediaLoopCheckBox);
        panel.Children.Add(_mediaShowWhenStoppedCheckBox);
        panel.Children.Add(_mediaRewindAfterPlayingCheckBox);
        panel.Children.Add(_mediaPlayFullScreenCheckBox);
        panel.Children.Add(_mediaStopAfterSlidesText);
        panel.Children.Add(_mediaStopAfterSlidesBox);
        panel.Children.Add(_mediaVolumeText);
        panel.Children.Add(_mediaVolumeSlider);
        panel.Children.Add(_mediaTrimStartText);
        panel.Children.Add(_mediaTrimStartBox);
        panel.Children.Add(_mediaTrimEndText);
        panel.Children.Add(_mediaTrimEndBox);
        panel.Children.Add(_mediaFadeInText);
        panel.Children.Add(_mediaFadeInBox);
        panel.Children.Add(_mediaFadeOutText);
        panel.Children.Add(_mediaFadeOutBox);
        panel.Children.Add(_mediaBookmarkText);
        panel.Children.Add(_mediaBookmarkBox);
        panel.Children.Add(_mediaBookmarkNameText);
        panel.Children.Add(_mediaBookmarkNameBox);
        panel.Children.Add(_mediaBookmarkTimeText);
        panel.Children.Add(_mediaBookmarkTimeBox);
        panel.Children.Add(buttons);

        return new Border
        {
            Width = PresentationMediaPaneVisualMetrics.PaneWidth,
            Visibility = Visibility.Collapsed,
            Background = FreePBrushes.White,
            BorderBrush = FreePBrushes.PaneBorder,
            BorderThickness = new Thickness(PresentationMediaPaneVisualMetrics.PaneBorderThickness, 0, 0, 0),
            Child = panel,
        };
    }

    private static TextBlock BuildMediaCaptionPaneLabel()
        => new()
        {
            FontSize = PresentationMediaPaneVisualMetrics.BodyFontSize,
            FontWeight = FontWeights.SemiBold,
            Margin = MediaPaneMargin(
                PresentationMediaPaneVisualMetrics.LabelTopMargin,
                PresentationMediaPaneVisualMetrics.LabelBottomMargin),
        };

    private static TextBox BuildMediaCaptionPaneTextBox(bool singleLine)
        => new()
        {
            AcceptsReturn = !singleLine,
            TextWrapping = singleLine ? TextWrapping.NoWrap : TextWrapping.Wrap,
            MinHeight = singleLine
                ? PresentationMediaPaneVisualMetrics.CompactControlHeight
                : PresentationMediaPaneVisualMetrics.TranscriptMinimumHeight,
            MaxHeight = singleLine
                ? PresentationMediaPaneVisualMetrics.CompactControlHeight
                : PresentationMediaPaneVisualMetrics.TranscriptMaximumHeight,
            Margin = MediaPaneMargin(0, PresentationMediaPaneVisualMetrics.FieldBottomMargin),
            Padding = new Thickness(
                PresentationMediaPaneVisualMetrics.FieldHorizontalPadding,
                PresentationMediaPaneVisualMetrics.FieldVerticalPadding,
                PresentationMediaPaneVisualMetrics.FieldHorizontalPadding,
                PresentationMediaPaneVisualMetrics.FieldVerticalPadding),
            VerticalScrollBarVisibility = singleLine ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto,
        };

    private static CheckBox BuildMediaPaneToggle(PresentationMediaPaneToggleControlPlan plan) =>
        new()
        {
            Content = plan.Label,
            IsChecked = plan.IsCheckedByDefault,
            Margin = MediaPaneMargin(
                PresentationMediaPaneVisualMetrics.CheckBoxTopMargin,
                PresentationMediaPaneVisualMetrics.FieldBottomMargin),
        };

    private static (TextBlock Label, TextBox Input) BuildMediaPaneTextControl(
        PresentationMediaPaneTextControlPlan plan)
    {
        var label = BuildMediaCaptionPaneLabel();
        label.Text = plan.Label;
        var input = BuildMediaCaptionPaneTextBox(singleLine: true);
        input.Text = plan.InitialValue;
        return (label, input);
    }

    private static Button BuildMediaCaptionPaneButton()
        => new()
        {
            MinWidth = PresentationMediaPaneVisualMetrics.ActionButtonMinimumWidth,
            Padding = new Thickness(
                PresentationMediaPaneVisualMetrics.ActionButtonHorizontalPadding,
                PresentationMediaPaneVisualMetrics.ActionButtonVerticalPadding,
                PresentationMediaPaneVisualMetrics.ActionButtonHorizontalPadding,
                PresentationMediaPaneVisualMetrics.ActionButtonVerticalPadding),
            Margin = new Thickness(
                0,
                0,
                PresentationMediaPaneVisualMetrics.ActionButtonRightMargin,
                PresentationMediaPaneVisualMetrics.ActionButtonBottomMargin),
        };

    private static Thickness MediaPaneMargin(double top, double bottom) =>
        new(
            PresentationMediaPaneVisualMetrics.ContentSideMargin,
            top,
            PresentationMediaPaneVisualMetrics.ContentSideMargin,
            bottom);

    private void BindMediaPaneEvents() => PresentationMediaPaneFormEventBinder.Bind(
        _mediaCaptionTrackBox,
        _mediaBookmarkBox,
        _mediaCaptionControls,
        _mediaPaneButtons,
        (input, action) => input.TextChanged += (_, _) => action(),
        (button, action) => button.Click += (_, _) => action(),
        (comboBox, action) => comboBox.SelectionChanged += (_, _) => action(),
        comboBox => comboBox.SelectedItem is ComboBoxItem { Tag: int trackIndex } ? trackIndex : null,
        comboBox => comboBox.SelectedItem is ComboBoxItem { Tag: int bookmarkIndex } ? bookmarkIndex : null,
        new PresentationMediaPaneFormEventRouter(_mediaPaneHostCoordinator));

    private Border BuildSmartArtTextPaneHost()
    {
        var chrome = PresentationPaneTextResources.BuildSmartArtTextPaneChrome();
        _smartArtTextPaneHeading = new TextBlock
        {
            Text = chrome.Heading,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(12, 12, 12, 4),
        };
        _smartArtTextPaneMessage = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = FreePBrushes.PaneSecondaryText,
            Margin = new Thickness(12, 0, 12, 8),
        };
        _smartArtTextPaneRowsPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
        };
        _smartArtTextPaneAssistantButton = new Button
        {
            Content = chrome.ToggleAssistant,
            MinWidth = 120,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 8, 0),
        };
        _smartArtTextPanePictureButton = new Button
        {
            Content = chrome.ReplacePicture,
            MinWidth = 120,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 8, 0),
        };
        _smartArtTextPaneClearPictureButton = new Button
        {
            Content = chrome.RemovePicture,
            MinWidth = 120,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 8, 0),
        };
        _smartArtTextPaneApplyButton = new Button
        {
            Content = chrome.Apply,
            MinWidth = 72,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 8, 0),
        };
        _smartArtTextPaneCloseButton = new Button
        {
            Content = chrome.Close,
            MinWidth = 72,
            Padding = new Thickness(10, 4, 10, 4),
        };
        _smartArtTextPaneAssistantButton.Click += (_, _) => ToggleSmartArtTextPaneAssistant();
        _smartArtTextPanePictureButton.Click += async (_, _) => await ReplaceSmartArtTextPanePictureFromFileAsync();
        _smartArtTextPaneClearPictureButton.Click += (_, _) => ClearSmartArtTextPanePicture();
        _smartArtTextPaneApplyButton.Click += (_, _) => ApplySmartArtTextPane();
        _smartArtTextPaneCloseButton.Click += (_, _) => HideSmartArtTextPane();

        _smartArtTextPaneOutlineActions = new WrapPanel
        {
            Margin = new Thickness(12, 0, 12, 4),
        };
        foreach (var action in chrome.OutlineActions)
            AddSmartArtTextPaneActionButton(action.Label, action.ToolTip, action.Kind);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12, 8, 12, 12),
        };
        buttons.Children.Add(_smartArtTextPaneAssistantButton);
        buttons.Children.Add(_smartArtTextPanePictureButton);
        buttons.Children.Add(_smartArtTextPaneClearPictureButton);
        buttons.Children.Add(_smartArtTextPaneApplyButton);
        buttons.Children.Add(_smartArtTextPaneCloseButton);

        var panel = new DockPanel();
        var header = new StackPanel { Orientation = Orientation.Vertical };
        header.Children.Add(_smartArtTextPaneHeading);
        header.Children.Add(_smartArtTextPaneMessage);
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(_smartArtTextPaneOutlineActions, Dock.Bottom);
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(header);
        panel.Children.Add(_smartArtTextPaneOutlineActions);
        panel.Children.Add(buttons);
        panel.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _smartArtTextPaneRowsPanel,
        });

        return new Border
        {
            Width = 320,
            Visibility = Visibility.Collapsed,
            Background = FreePBrushes.White,
            BorderBrush = FreePBrushes.PaneBorder,
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = panel,
        };
    }

    private void AddSmartArtTextPaneActionButton(
        string label,
        string toolTip,
        SmartArtNodeEditKind kind)
    {
        var button = new Button
        {
            Content = label,
            ToolTip = toolTip,
            MinWidth = 82,
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 0, 6, 6),
            IsEnabled = false,
        };
        button.Click += (_, _) => ApplySmartArtTextPaneAction(kind);
        _smartArtTextPaneActionButtons.Add(button);
        _smartArtTextPaneOutlineActions.Children.Add(button);
    }

    private Border BuildAltTextPaneHost()
    {
        _altTextPaneHeading = new TextBlock
        {
            Text = PresentationPaneTextResources.AltTextHeading,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(12, 12, 12, 4),
        };
        _altTextPaneMessage = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = FreePBrushes.PaneSecondaryText,
            Margin = new Thickness(12, 0, 12, 8),
        };
        _altTextTitleLabel = BuildAltTextPaneLabel();
        _altTextTitleBox = BuildAltTextPaneTextBox(singleLine: true);
        _altTextDescriptionLabel = BuildAltTextPaneLabel();
        _altTextDescriptionBox = BuildAltTextPaneTextBox(singleLine: false);
        _altTextDecorativeCheck = new CheckBox
        {
            Margin = new Thickness(12, 8, 12, 6),
        };
        _altTextApplyButton = new Button
        {
            MinWidth = 72,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 8, 0),
        };
        _altTextCloseButton = new Button
        {
            MinWidth = 72,
            Padding = new Thickness(10, 4, 10, 4),
        };

        _altTextTitleBox.TextChanged += (_, _) => RefreshVisibleAltTextPaneFromFields();
        _altTextDescriptionBox.TextChanged += (_, _) => RefreshVisibleAltTextPaneFromFields();
        _altTextDecorativeCheck.Checked += (_, _) => RefreshVisibleAltTextPaneFromFields();
        _altTextDecorativeCheck.Unchecked += (_, _) => RefreshVisibleAltTextPaneFromFields();
        _altTextApplyButton.Click += (_, _) => ApplyAltTextPane();
        _altTextCloseButton.Click += (_, _) => HideAltTextPane();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12, 8, 12, 12),
        };
        buttons.Children.Add(_altTextApplyButton);
        buttons.Children.Add(_altTextCloseButton);

        var panel = new StackPanel { Orientation = Orientation.Vertical };
        panel.Children.Add(_altTextPaneHeading);
        panel.Children.Add(_altTextPaneMessage);
        panel.Children.Add(_altTextTitleLabel);
        panel.Children.Add(_altTextTitleBox);
        panel.Children.Add(_altTextDescriptionLabel);
        panel.Children.Add(_altTextDescriptionBox);
        panel.Children.Add(_altTextDecorativeCheck);
        panel.Children.Add(buttons);

        var host = new Border
        {
            Width = 292,
            Visibility = Visibility.Collapsed,
            Background = FreePBrushes.White,
            BorderBrush = FreePBrushes.PaneBorder,
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = panel,
        };
        _altTextCloseButton.Content = PresentationPaneTextResources.CloseCommand;
        return host;
    }

    private static TextBlock BuildAltTextPaneLabel()
        => new()
        {
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(12, 6, 12, 2),
        };

    private static TextBox BuildAltTextPaneTextBox(bool singleLine)
        => new()
        {
            AcceptsReturn = !singleLine,
            TextWrapping = singleLine ? TextWrapping.NoWrap : TextWrapping.Wrap,
            MinHeight = singleLine ? 28 : 84,
            MaxHeight = singleLine ? 28 : 120,
            Margin = new Thickness(12, 0, 12, 4),
            Padding = new Thickness(6, 4, 6, 4),
            VerticalScrollBarVisibility = singleLine ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto,
        };

    private Border BuildAccessibilityCheckerPaneHost()
    {
        _accessibilityCheckerPaneHeading = new TextBlock
        {
            Text = PresentationPaneTextResources.AccessibilityHeading,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(12, 12, 12, 4),
        };
        _accessibilityCheckerPaneMessage = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = FreePBrushes.PaneSecondaryText,
            Margin = new Thickness(12, 0, 12, 8),
        };
        _accessibilityCheckerRowsPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
        };
        _accessibilityCheckerReviewDetailsPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(12, 0, 12, 8),
        };

        var panel = new DockPanel();
        var header = new StackPanel { Orientation = Orientation.Vertical };
        header.Children.Add(_accessibilityCheckerPaneHeading);
        header.Children.Add(_accessibilityCheckerPaneMessage);
        header.Children.Add(_accessibilityCheckerReviewDetailsPanel);
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        panel.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _accessibilityCheckerRowsPanel,
        });

        return new Border
        {
            Width = 320,
            Visibility = Visibility.Collapsed,
            Background = FreePBrushes.White,
            BorderBrush = FreePBrushes.PaneBorder,
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = panel,
        };
    }

    private Border BuildReadingOrderPaneHost()
    {
        _readingOrderPaneHeading = new TextBlock
        {
            Text = PresentationPaneTextResources.ReadingOrderHeading,
            FontSize = PresentationReadingOrderPaneVisualMetrics.HeadingFontSize,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(
                PresentationReadingOrderPaneVisualMetrics.ContentSideMargin,
                PresentationReadingOrderPaneVisualMetrics.HeadingTopMargin,
                PresentationReadingOrderPaneVisualMetrics.ContentSideMargin,
                PresentationReadingOrderPaneVisualMetrics.HeadingBottomMargin),
        };
        _readingOrderPaneMessage = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = FreePBrushes.PaneSecondaryText,
            Margin = new Thickness(
                PresentationReadingOrderPaneVisualMetrics.ContentSideMargin,
                0,
                PresentationReadingOrderPaneVisualMetrics.ContentSideMargin,
                PresentationReadingOrderPaneVisualMetrics.MessageBottomMargin),
        };
        _readingOrderMoveEarlierButton = new Button
        {
            MinWidth = PresentationReadingOrderPaneVisualMetrics.MoveEarlierButtonWidth,
            Padding = new Thickness(
                PresentationReadingOrderPaneVisualMetrics.ActionButtonHorizontalPadding,
                PresentationReadingOrderPaneVisualMetrics.ActionButtonVerticalPadding,
                PresentationReadingOrderPaneVisualMetrics.ActionButtonHorizontalPadding,
                PresentationReadingOrderPaneVisualMetrics.ActionButtonVerticalPadding),
            Margin = new Thickness(0, 0, PresentationReadingOrderPaneVisualMetrics.ActionButtonGap, 0),
        };
        _readingOrderMoveLaterButton = new Button
        {
            MinWidth = PresentationReadingOrderPaneVisualMetrics.MoveLaterButtonWidth,
            Padding = new Thickness(
                PresentationReadingOrderPaneVisualMetrics.ActionButtonHorizontalPadding,
                PresentationReadingOrderPaneVisualMetrics.ActionButtonVerticalPadding,
                PresentationReadingOrderPaneVisualMetrics.ActionButtonHorizontalPadding,
                PresentationReadingOrderPaneVisualMetrics.ActionButtonVerticalPadding),
        };
        _readingOrderMoveEarlierButton.Click += (_, _) => ApplyReadingOrderMoveEarlier();
        _readingOrderMoveLaterButton.Click += (_, _) => ApplyReadingOrderMoveLater();
        _readingOrderPaneItemsPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
        };

        var actionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(
                PresentationReadingOrderPaneVisualMetrics.ContentSideMargin,
                0,
                PresentationReadingOrderPaneVisualMetrics.ContentSideMargin,
                PresentationReadingOrderPaneVisualMetrics.MessageBottomMargin),
        };
        actionPanel.Children.Add(_readingOrderMoveEarlierButton);
        actionPanel.Children.Add(_readingOrderMoveLaterButton);

        var panel = new DockPanel();
        var header = new StackPanel { Orientation = Orientation.Vertical };
        header.Children.Add(_readingOrderPaneHeading);
        header.Children.Add(_readingOrderPaneMessage);
        header.Children.Add(actionPanel);
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        panel.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _readingOrderPaneItemsPanel,
        });

        return new Border
        {
            Width = PresentationReadingOrderPaneVisualMetrics.PaneWidth,
            Visibility = Visibility.Collapsed,
            Background = FreePBrushes.White,
            BorderBrush = FreePBrushes.PaneBorder,
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = panel,
        };
    }

    // ── Canvas refresh ────────────────────────────────────────────────────────────

    private Border BuildProofingPaneHost()
    {
        _proofingPaneHeading = new TextBlock
        {
            Text = PresentationPaneTextResources.ProofingHeading,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(12, 12, 12, 4),
        };
        _proofingPaneMessage = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = FreePBrushes.PaneSecondaryText,
            Margin = new Thickness(12, 0, 12, 8),
        };
        _proofingPaneRowsPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
        };

        var panel = new DockPanel();
        var header = new StackPanel { Orientation = Orientation.Vertical };
        header.Children.Add(_proofingPaneHeading);
        header.Children.Add(_proofingPaneMessage);
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        panel.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _proofingPaneRowsPanel,
        });

        return new Border
        {
            Width = 320,
            Visibility = Visibility.Collapsed,
            Background = FreePBrushes.White,
            BorderBrush = FreePBrushes.PaneBorder,
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = panel,
        };
    }

    private void RefreshCanvas()
    {
        CloseActiveOleHost();
        SlideCanvas.Presentation = _presentation;
        SlideCanvas.Slide        = Editor.CurrentSlide;
        SlideCanvas.Refresh();
    }

    // ── Notes pane refresh (Wave 7B) ──────────────────────────────────────────────

    private void OnNotesKeyDown(object sender, KeyEventArgs e)
    {
        if (_notesRefreshing || Keyboard.Modifiers != ModifierKeys.Control)
            return;

        var kind = e.Key switch
        {
            Key.B => TableCellTextFormatKind.Bold,
            Key.I => TableCellTextFormatKind.Italic,
            Key.U => TableCellTextFormatKind.Underline,
            Key.D5 => TableCellTextFormatKind.Strikethrough,
            _ => (TableCellTextFormatKind?)null,
        };
        if (kind is { } formatKind && TryApplyCurrentSlideNotesTextFormat(formatKind))
            e.Handled = true;
    }

    private bool TryApplyCurrentSlideNotesTextFormat(TableCellTextFormatKind kind)
    {
        if (_notesRefreshing || !_notesBox.IsVisible || _notesBox.SelectionLength == 0)
            return false;

        return Editor.TryApplyCurrentSlideNotesTextFormat(
            kind,
            (_notesBox.SelectionStart, _notesBox.SelectionStart + _notesBox.SelectionLength),
            _notesBox.Text);
    }

    private bool TryApplyCurrentSlideNotesValueFormat(
        TableCellTextValueFormatKind kind,
        object? value)
    {
        if (_notesRefreshing || !_notesBox.IsVisible || _notesBox.SelectionLength == 0)
            return false;

        return Editor.TryApplyCurrentSlideNotesValueFormat(
            kind,
            value,
            (_notesBox.SelectionStart, _notesBox.SelectionStart + _notesBox.SelectionLength),
            _notesBox.Text);
    }

    private bool TryApplyCurrentSlideNotesParagraphFormat(
        TableCellParagraphFormatKind kind,
        object? value)
    {
        if (_notesRefreshing || !_notesBox.IsVisible || _notesBox.SelectionLength == 0)
            return false;

        return Editor.TryApplyCurrentSlideNotesParagraphFormat(
            kind,
            value,
            (_notesBox.SelectionStart, _notesBox.SelectionStart + _notesBox.SelectionLength),
            _notesBox.Text);
    }

    /// <summary>
    /// Populates the notes TextBox from the current slide's Notes body.
    /// The _notesRefreshing guard prevents the TextChanged handler from routing the
    /// programmatic set back through EditingSession (which would create a spurious undo entry).
    /// </summary>
    private void RefreshNotesPane()
    {
        if (_notesBox is null) return;
        _notesRefreshing = true;
        try
        {
            var plan = _notesPaneSession.BuildProjection();
            LastNotesPagePreviewPlan = plan.Preview;
            _notesBox.Text = plan.Text;
        }
        finally
        {
            _notesRefreshing = false;
        }
        RefreshPaneAccessibilityMetadata();
    }

    // ── Comment pane + overlay refresh (Wave 11B) ────────────────────────────────

    /// <summary>
    /// Refreshes the comment indicator overlay dots (on the stage canvas) and the
    /// comment list strip below the canvas for the current slide.
    /// Guards null fields so it is safe to call before BuildBody completes.
    /// </summary>
    private void RefreshCommentPane()
    {
        if (_reviewWorkflowSession is null)
            return;

        RenderCommentPane(_reviewWorkflowSession.BuildCommentPanePlan());
    }

    private void RenderCommentPane(PresentationCommentPanePlan plan)
    {
        if (_commentOverlay is null || _commentListHost is null || _commentListPanel is null) return;
        var comments = plan.Comments;

        // ── Overlay: rebuild speech-bubble markers ──────────────────────────────
        _commentOverlay.Children.Clear();
        if (plan.HasComments)
        {
            // We'll draw the dots when the overlay has been laid out.  Register a one-shot handler.
            _commentOverlay.Loaded -= OnCommentOverlayLoaded;
            _commentOverlay.Loaded += OnCommentOverlayLoaded;

            // If already loaded, draw immediately.
            if (_commentOverlay.IsLoaded)
                DrawCommentDots(comments);
        }

        // ── List pane ──────────────────────────────────────────────────────────
        _commentListPanel.Children.Clear();
        AddCommentPaneSummary(_commentListPanel, plan);
        AddCommentInput(_commentListPanel);
        AddReviewCommentActions(_commentListPanel, plan.ToolbarActions);
        if (plan.HasComments)
        {
            foreach (var (cm, itemIndex) in comments.Select((comment, index) => (comment, index)))
            {
                // Header: initials badge + author name + timestamp
                var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 4, 6, 0) };
                var badge = new Border
                {
                    Background      = FreePBrushes.Accent,
                    CornerRadius    = new CornerRadius(3),
                    Padding         = new Thickness(4, 1, 4, 1),
                    Margin          = new Thickness(0, 0, 6, 0),
                    Child           = new TextBlock
                    {
                        Text       = cm.InitialsBadgeText,
                        FontSize   = PresentationCommentPaneVisualMetrics.StatusFontSize,
                        Foreground = FreePBrushes.White,
                    }
                };
                var authorText = new TextBlock
                {
                    Text       = cm.AuthorDisplayName,
                    FontSize   = PresentationCommentPaneVisualMetrics.AuthorFontSize,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = FreePBrushes.PaneHeadingText,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                headerPanel.Children.Add(badge);
                headerPanel.Children.Add(authorText);
                headerPanel.Children.Add(new TextBlock
                {
                    Text       = cm.ThreadStatusLabel,
                    FontSize   = PresentationCommentPaneVisualMetrics.StatusFontSize,
                    Foreground = FreePBrushes.PaneMutedText,
                    Margin     = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                });

                // Comment body text
                var bodyText = new TextBlock
                {
                    Text         = cm.TextPreview,
                    FontSize     = PresentationCommentPaneVisualMetrics.BodyFontSize,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground   = FreePBrushes.PaneText,
                    Margin       = new Thickness(16, 2, 6, 6),
                };

                var card = new StackPanel();
                card.Children.Add(headerPanel);
                card.Children.Add(bodyText);
                if (cm.ShouldShowMentionDetail)
                    AddMentionDetail(card, cm.MentionDetailSummary, new Thickness(16, 0, 6, 6));
                AddEditCommentInput(card, cm, plan.SaveEditAction);
                AddReplyRows(card, cm);
                AddReplyInput(card, cm);

                var cardHost = new Border
                {
                    Background      = cm.IsSelected ? FreePBrushes.SelectedCommentSurface : FreePBrushes.PaneSurface,
                    BorderBrush     = cm.IsSelected ? FreePBrushes.Accent : FreePBrushes.CardBorder,
                    BorderThickness = new Thickness(cm.IsSelected ? 2 : 1),
                    CornerRadius    = new CornerRadius(4),
                    Margin          = new Thickness(0, 0, 0, PresentationCommentPaneVisualMetrics.CardBottomMargin),
                    Cursor          = Cursors.Hand,
                    Child           = card,
                };
                PresentationPaneAccessibilityAdapter.ApplyItem(
                    cardHost,
                    PresentationPaneAccessibilityPlanner.PlanItem(
                        PresentationPaneAccessibilityPlanner.CommentsPaneId,
                        itemIndex,
                        cm.TextPreview,
                        cm.IsSelected,
                        cm.AccessibilityKey));
                cardHost.MouseLeftButtonDown += (_, _) => SelectReviewComment(cm.CommentIndex);
                _commentListPanel.Children.Add(cardHost);
            }
        }

        var commentPaneState = _workareaSession.Panes.ResolveVisibility(
            PresentationWorkareaPane.ReviewComments,
            comments.Count > 0,
            PresentationWorkareaPaneVisibilityPolicy.RequestedOrContent).Current;
        _commentListHost.Visibility = commentPaneState.IsVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        PresentationPaneAccessibilityAdapter.ApplyPaneMetadata(
            _commentListHost,
            PresentationPaneAccessibilityPlanner.CommentsPaneId,
            commentPaneState.IsVisible,
            comments.Count,
            plan.SelectedCommentIndex);
        RefreshPaneAccessibilityMetadata();
    }

    private void AddCommentPaneSummary(Panel host, PresentationCommentPanePlan plan)
    {
        var summaryRow = new DockPanel();
        var close = new Button
        {
            Content = plan.CloseAction.Label,
            IsEnabled = plan.CloseAction.IsEnabled,
            MinWidth = PresentationCommentPaneVisualMetrics.CloseMinimumWidth,
            MinHeight = 0,
            Height = PresentationCommentPaneVisualMetrics.CompactControlHeight,
            FontSize = PresentationCommentPaneVisualMetrics.CompactControlFontSize,
            Padding = new Thickness(8, 0, 8, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 6),
            Tag = PresentationSemanticIdentityCatalog.CommentsPaneCloseTag,
        };
        close.Click += (_, _) => HideReviewCommentsPane();
        DockPanel.SetDock(close, Dock.Right);
        summaryRow.Children.Add(close);
        summaryRow.Children.Add(new TextBlock
        {
            Text = plan.HeaderSummaryText,
            FontSize = PresentationCommentPaneVisualMetrics.SummaryFontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = FreePBrushes.PaneText,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6),
        });
        host.Children.Add(summaryRow);
        host.Children.Add(new TextBlock
        {
            Text = plan.FilterOptionsSummaryText,
            FontSize = PresentationCommentPaneVisualMetrics.FilterFontSize,
            Foreground = FreePBrushes.PaneMutedText,
            Margin = new Thickness(0, 0, 0, 6),
        });
    }

    private void AddCommentInput(Panel host)
    {
        var input = new TextBox
        {
            MinWidth = PresentationCommentPaneVisualMetrics.AddCommentInputMinimumWidth,
            MinHeight = 0,
            Height = PresentationCommentPaneVisualMetrics.CompactControlHeight,
            FontSize = PresentationCommentPaneVisualMetrics.CompactControlFontSize,
            Padding = new Thickness(4, 0, 4, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        var button = new Button
        {
            Content = PresentationPaneTextResources.NewCommentCommand,
            Width = PresentationCommentPaneVisualMetrics.AddCommentButtonWidth,
            MinHeight = 0,
            Height = PresentationCommentPaneVisualMetrics.CompactControlHeight,
            FontSize = PresentationCommentPaneVisualMetrics.CompactControlFontSize,
            Padding = new Thickness(8, 0, 8, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        button.Click += (_, _) => AddComment(input.Text, author: ResolveCommentAuthor());

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };
        row.Children.Add(input);
        row.Children.Add(button);
        host.Children.Add(row);
    }

    private void AddReviewCommentActions(
        Panel host,
        IReadOnlyList<PresentationReviewWorkflowActionPlan> actions)
    {
        var panel = new WrapPanel
        {
            Margin = new Thickness(12, 0, 12, 2),
        };

        foreach (var action in actions)
        {
            if (action.CommandId == PresentationReviewWorkflowPlanner.ReplyCommentCommandId)
                continue;

            var button = new Button
            {
                Content = action.Label,
                IsEnabled = action.IsEnabled,
                Tag = action.CommandId,
                Width = PresentationCommentPaneVisualMetrics.ToolbarActionWidth(action.CommandId),
                MinHeight = 0,
                Height = PresentationCommentPaneVisualMetrics.CompactControlHeight,
                FontSize = PresentationCommentPaneVisualMetrics.CompactControlFontSize,
                Padding = new Thickness(8, 0, 8, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 6),
            };
            AutomationProperties.SetAutomationId(button, action.CommandId);
            AutomationProperties.SetName(button, action.Label);
            button.Click += (_, _) => _reviewPaneHostCoordinator.ExecuteCommentCommand(action.CommandId);
            panel.Children.Add(button);
        }

        host.Children.Add(panel);
    }

    private void AddEditCommentInput(
        StackPanel card,
        PresentationCommentDescriptor cm,
        PresentationReviewSurfaceActionPlan editAction)
    {
        if (!cm.IsSelected || !cm.CanEdit)
            return;

        var editText = GetCommentText(cm.CommentIndex) ?? cm.TextPreview;
        var input = new TextBox
        {
            Text = editText,
            CaretIndex = editText.Length,
            MinWidth = PresentationCommentPaneVisualMetrics.AddCommentInputMinimumWidth,
            MinHeight = 0,
            Height = PresentationCommentPaneVisualMetrics.CompactControlHeight,
            FontSize = PresentationCommentPaneVisualMetrics.CompactControlFontSize,
            Padding = new Thickness(4, 0, 4, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 6, 6)
        };
        var mentionButton = BuildCommentMentionButton(
            PresentationSemanticIdentityCatalog.CommentMentionEditTag,
            () => input.Text,
            () => input.CaretIndex,
            PresentationReviewWorkflowIntentKind.EditComment);
        var button = new Button
        {
            Content = editAction.Label,
            IsEnabled = editAction.IsEnabled,
            MinWidth = 72,
            MinHeight = 0,
            Height = PresentationCommentPaneVisualMetrics.CompactControlHeight,
            FontSize = PresentationCommentPaneVisualMetrics.CompactControlFontSize,
            Padding = new Thickness(8, 0, 8, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 6)
        };
        button.Click += (_, _) => EditSelectedComment(input.Text);

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };
        row.Children.Add(input);
        row.Children.Add(mentionButton);
        row.Children.Add(button);
        card.Children.Add(row);
    }

    private void AddReplyRows(StackPanel card, PresentationCommentDescriptor cm)
    {
        foreach (var reply in cm.Replies)
        {
            var row = new TextBlock
            {
                Text = reply.DisplayText,
                FontSize = PresentationCommentPaneVisualMetrics.ReplyFontSize,
                TextWrapping = TextWrapping.Wrap,
                Foreground = FreePBrushes.PaneSecondaryText,
                Margin = new Thickness(26, 0, 6, 4),
            };
            card.Children.Add(row);
            if (reply.ShouldShowMentionDetail)
                AddMentionDetail(card, reply.MentionDetailSummary, new Thickness(26, 0, 6, 4));
        }
    }

    private void AddReplyInput(StackPanel card, PresentationCommentDescriptor cm)
    {
        if (!cm.IsSelected || !cm.CanReply)
            return;

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(16, 0, 6, 6),
        };
        var input = new System.Windows.Controls.TextBox
        {
            MinWidth = PresentationCommentPaneVisualMetrics.AddCommentInputMinimumWidth,
            MinHeight = 0,
            Height = PresentationCommentPaneVisualMetrics.CompactControlHeight,
            FontSize = PresentationCommentPaneVisualMetrics.CompactControlFontSize,
            Padding = new Thickness(4, 0, 4, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        var mentionButton = BuildCommentMentionButton(
            PresentationSemanticIdentityCatalog.CommentMentionReplyTag,
            () => input.Text,
            () => input.CaretIndex,
            PresentationReviewWorkflowIntentKind.ReplyComment);
        var button = new System.Windows.Controls.Button
        {
            Content = PresentationPaneTextResources.ReplyCommand,
            MinWidth = 58,
            MinHeight = 0,
            Height = PresentationCommentPaneVisualMetrics.CompactControlHeight,
            FontSize = PresentationCommentPaneVisualMetrics.CompactControlFontSize,
            Padding = new Thickness(8, 0, 8, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        button.Click += (_, _) => ReplyToSelectedComment(input.Text, author: ResolveCommentAuthor());
        row.Children.Add(input);
        row.Children.Add(mentionButton);
        row.Children.Add(button);
        card.Children.Add(row);
    }

    private static void AddMentionDetail(StackPanel card, string mentionDetailSummary, Thickness margin)
    {
        card.Children.Add(new TextBlock
        {
            Text = mentionDetailSummary,
            FontSize = PresentationCommentPaneVisualMetrics.MentionFontSize,
            TextWrapping = TextWrapping.Wrap,
            Foreground = FreePBrushes.PaneMutedText,
            Margin = margin,
        });
    }

    private Button BuildCommentMentionButton(
        string tag,
        Func<string?> getText,
        Func<int> getCaretIndex,
        PresentationReviewWorkflowIntentKind intent)
    {
        var mentionPicker = _reviewWorkflowSession.BuildCommentMentionPickerPlanForInput(
            getText(),
            getCaretIndex());
        var button = new Button
        {
            Content = mentionPicker.TriggerLabel,
            MinWidth = 72,
            Margin = new Thickness(0, 0, 6, 6),
            IsEnabled = mentionPicker.HasCandidates,
            Tag = tag,
        };
        button.Click += (_, _) =>
        {
            var dispatch = _reviewWorkflowSession.DispatchCommentMentionPicker(
                intent,
                getText(),
                getCaretIndex(),
                currentAuthor: intent == PresentationReviewWorkflowIntentKind.ReplyComment
                    ? ResolveCommentAuthor()
                    : null);
            if (dispatch.ApplicationResult is not null)
                return;

            if (dispatch.ShouldShowPicker)
            {
                var menu = BuildCommentMentionMenu(
                    tag,
                    getText,
                    getCaretIndex,
                    intent,
                    dispatch.PickerPlan);
                button.ContextMenu = menu;
                menu.IsOpen = true;
            }
        };
        return button;
    }

    private ContextMenu BuildCommentMentionMenu(string tag, Func<string?> getText, Func<int> getCaretIndex, PresentationReviewWorkflowIntentKind intent, PresentationCommentMentionPickerPlan picker) =>
        _reviewPaneHostCoordinator.BuildCommentMentionMenu(
            tag, getText, getCaretIndex, intent, picker,
            new PresentationCommentMentionMenuNativeBindings<ContextMenu, MenuItem>(
                CreateMenu: static () => new ContextMenu(),
                CreateItem: plan => new MenuItem { Header = plan.Label, Tag = plan.SemanticTag },
                BindClick: (item, execute) => item.Click += (_, _) => execute(),
                AddItem: (menu, item) => menu.Items.Add(item)));

    private static IEnumerable<string> EnumerateCommentPaneText(DependencyObject? control)
    {
        if (control is null)
            yield break;

        if (control is TextBlock textBlock)
        {
            yield return textBlock.Text;
        }

        if (control is Panel panel)
        {
            foreach (UIElement child in panel.Children)
            {
                foreach (var text in EnumerateCommentPaneText(child))
                {
                    yield return text;
                }
            }
        }
        else if (control is ContentControl { Content: DependencyObject content })
        {
            foreach (var text in EnumerateCommentPaneText(content))
            {
                yield return text;
            }
        }
        else if (control is Decorator { Child: DependencyObject child })
        {
            foreach (var text in EnumerateCommentPaneText(child))
            {
                yield return text;
            }
        }
    }

    private static IEnumerable<Button> EnumerateCommentPaneButtons(DependencyObject? control)
    {
        if (control is null)
            yield break;

        if (control is Button button)
        {
            yield return button;
        }

        if (control is Panel panel)
        {
            foreach (UIElement child in panel.Children)
            {
                foreach (var descendant in EnumerateCommentPaneButtons(child))
                {
                    yield return descendant;
                }
            }
        }
        else if (control is ContentControl { Content: DependencyObject content })
        {
            foreach (var descendant in EnumerateCommentPaneButtons(content))
            {
                yield return descendant;
            }
        }
        else if (control is Decorator { Child: DependencyObject child })
        {
            foreach (var descendant in EnumerateCommentPaneButtons(child))
            {
                yield return descendant;
            }
        }
    }

    private void OnCommentOverlayLoaded(object sender, RoutedEventArgs e)
    {
        _commentOverlay.Loaded -= OnCommentOverlayLoaded;
        var comments = LastCommentPanePlan?.Comments ?? [];
        DrawCommentDots(comments);
    }

    /// <summary>
    /// Paints speech-bubble dot markers on <see cref="_commentOverlay"/> for each comment.
    /// Shared Presentation code owns all marker geometry and semantics; this method only
    /// materializes WPF controls.
    /// </summary>
    private void DrawCommentDots(IReadOnlyList<PresentationCommentDescriptor> comments)
    {
        _commentOverlay.Children.Clear();
        var markers = PresentationCommentMarkerLayoutPlanner.Build(
            comments,
            _commentOverlay.ActualWidth,
            _commentOverlay.ActualHeight,
            _presentation.SlideSizeCxEmu,
            _presentation.SlideSizeCyEmu);

        foreach (var marker in markers)
        {
            // Speech-bubble: a small orange circle with a tooltip showing author+text.
            var dot = new Border
            {
                Width           = marker.Bounds.Width,
                Height          = marker.Bounds.Height,
                CornerRadius    = new CornerRadius(marker.Bounds.Width / 2),
                Background      = marker.IsSelected ? FreePBrushes.AccentDark : FreePBrushes.Accent,
                BorderBrush     = FreePBrushes.White,
                BorderThickness = new Thickness(marker.BorderThickness),
                ToolTip         = marker.ToolTip,
            };
            AutomationProperties.SetAutomationId(dot, marker.AutomationId);
            AutomationProperties.SetName(dot, marker.ToolTip);

            Canvas.SetLeft(dot, marker.Bounds.X);
            Canvas.SetTop(dot, marker.Bounds.Y);
            _commentOverlay.Children.Add(dot);
        }
    }

    internal PresentationVideoExportPlan RefreshVideoExportPlan(PresentationVideoExportRequest? request = null)
    {
        LastVideoExportPlan = _fileSession.BuildVideoExportPlan(request);
        return LastVideoExportPlan;
    }

    internal PresentationVideoFramePackage RefreshVideoFramePackage(PresentationVideoExportRequest? request = null)
    {
        LastVideoFramePackage = _fileSession.BuildVideoFramePackage(request);
        LastVideoExportPlan = _fileSession.BuildVideoExportPlan(request);
        LastVideoExportHandoffPlan = _fileSession.LastVideoExportHandoffPlan;
        return LastVideoFramePackage;
    }

    internal PresentationNotesPagePdfRenderPlan RefreshNotesPagePdfRenderPlan(PresentationSlideRangeRequest? range = null)
    {
        LastNotesPagePdfRenderPlan = _fileSession.BuildNotesPagePdfRenderPlan(range);
        return LastNotesPagePdfRenderPlan;
    }

    internal PresentationPrintOutputPackage RefreshPrintOutputPackage(PresentationPrintRequest? request = null)
    {
        LastPrintOutputPackage = _fileSession.BuildPrintOutputPackage(request);
        return LastPrintOutputPackage;
    }

    internal PresentationPrintBackstagePlan RefreshPrintBackstagePlan(PresentationPrintRequest? request = null)
    {
        LastPrintBackstagePlan = _fileSession.BuildPrintBackstagePlan(request);
        return LastPrintBackstagePlan;
    }

    internal void RefreshReviewWorkflowPlans()
    {
        _reviewWorkflowSession.RefreshReviewWorkflowPlans();
        RefreshPaneAccessibilityMetadata();
    }

    internal PresentationCommentPanePlan ShowReviewCommentsPane()
    {
        _workareaSession.Panes.Show(PresentationWorkareaPane.ReviewComments);
        return _reviewWorkflowSession.ShowReviewCommentsPane();
    }

    internal void HideReviewCommentsPane()
    {
        _workareaSession.Panes.Hide(PresentationWorkareaPane.ReviewComments);
        if (_commentListHost is not null)
            _commentListHost.Visibility = Visibility.Collapsed;
        RefreshPaneAccessibilityMetadata();
    }


    private void SelectReviewComment(int commentIndex) => _reviewWorkflowSession.SelectReviewComment(commentIndex);
    internal PresentationCommentNavigationPlan NavigateReviewComment(PresentationReviewWorkflowIntentKind intent) => _reviewWorkflowSession.NavigateReviewComment(intent);
    internal PresentationCommentMutationPlan DeleteSelectedComment() => _reviewWorkflowSession.DeleteSelectedComment();
    internal PresentationCommentMutationPlan AddComment(string? text, DateTime? timestamp = null, string? author = null, string? initials = null, long xemu = 0, long yemu = 0) => _reviewWorkflowSession.AddComment(text, timestamp, author, initials, xemu, yemu);
    internal PresentationCommentMutationPlan EditSelectedComment(string? text, string? author = null, string? initials = null) => _reviewWorkflowSession.EditSelectedComment(text, author, initials);
    internal PresentationCommentMutationPlan ResolveSelectedComment(DateTime? resolvedAt = null, string? resolvedBy = null) => _reviewWorkflowSession.ResolveSelectedComment(resolvedAt, resolvedBy);
    internal PresentationCommentMutationPlan ReopenSelectedComment() => _reviewWorkflowSession.ReopenSelectedComment();
    internal PresentationCommentMutationPlan ReplyToSelectedComment(string? text, DateTime? timestamp = null, string? author = null, string? initials = null) => _reviewWorkflowSession.ReplyToSelectedComment(text, timestamp, author, initials);

    /// <summary>
    /// Resolves the real author identity to stamp on a new comment, reply, or resolution.
    /// Delegates to the shared session so WPF and Avalonia resolve identity through the one
    /// place instead of each host carrying its own copy of the resolution chain (document
    /// author, then OS account name, mirroring FreeW's ReviewAuthorIdentityPlanner.ResolveAuthor
    /// minus the revision-author override FreeW has and FreeP does not).
    /// </summary>
    internal string? ResolveCommentAuthor() => _reviewWorkflowSession.ResolveCommentAuthor();

    private string? GetSelectedCommentText() => _reviewWorkflowSession.GetSelectedCommentText();

    private string? GetCommentText(int commentIndex) => _reviewWorkflowSession.GetCommentText(commentIndex);

    internal PresentationAccessibilityCheckerPanePlan ShowAccessibilityCheckerPane() { var plan = _reviewWorkflowSession.ShowAccessibilityCheckerPane(); RefreshPaneAccessibilityMetadata(); return plan; }

    internal PresentationAccessibilityCheckerPanePlan SelectAccessibilityCheckerRow(int rowIndex)
        => _reviewWorkflowSession.SelectAccessibilityCheckerRow(rowIndex);

    internal PresentationAccessibilityCheckerPanePlan ApplyAccessibilityCheckerRowAction(int rowIndex)
        => _reviewWorkflowSession.ApplyAccessibilityCheckerRowAction(rowIndex);

    private void RenderAccessibilityCheckerPane(PresentationAccessibilityCheckerPanePlan plan)
    {
        _accessibilityCheckerPaneHeading.Text = plan.Heading;
        _accessibilityCheckerPaneMessage.Text = plan.Message;
        RenderTableStructureReviewDetails(LastTableStructureReviewDisplayPlan);

        _accessibilityCheckerRowsPanel.Children.Clear();
        if (plan.ShouldShowEmptyState)
        {
            _accessibilityCheckerRowsPanel.Children.Add(new TextBlock
            {
                Text = plan.EmptyStateMessage,
                Foreground = FreePBrushes.PaneMutedText,
                Margin = new Thickness(12, 0, 12, 10),
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        foreach (var row in plan.Rows)
            _accessibilityCheckerRowsPanel.Children.Add(BuildAccessibilityCheckerRowCard(row));
    }

    private UIElement BuildAccessibilityCheckerRowCard(PresentationAccessibilityCheckerRowPlan row)
    {
        var title = new TextBlock
        {
            Text = row.DisplayTitle,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        var metadata = new TextBlock
        {
            Text = row.DisplayMetadata,
            Foreground = FreePBrushes.PaneSecondaryText,
            TextWrapping = TextWrapping.Wrap,
        };
        var detail = new TextBlock
        {
            Text = row.Detail,
            Foreground = FreePBrushes.PaneText,
            TextWrapping = TextWrapping.Wrap,
        };
        var action = new Button
        {
            Content = row.ActionLabel,
            Tag = row.RowIndex,
            MinWidth = 96,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
            ToolTip = row.CommandHint,
        };
        action.Click += (_, _) => ApplyAccessibilityCheckerRowAction(row.RowIndex);

        var panel = new StackPanel { Orientation = Orientation.Vertical };
        panel.Children.Add(title);
        if (row.ShouldShowSelectionIndicator)
        {
            panel.Children.Add(new TextBlock
            {
                Text = PresentationPaneTextResources.ProofingSelectedIssue,
                Foreground = FreePBrushes.Accent,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }
        panel.Children.Add(metadata);
        panel.Children.Add(detail);
        panel.Children.Add(action);

        var card = new Border
        {
            Background = row.IsSelected
                ? FreePBrushes.SelectedCardSurface
                : FreePBrushes.PaneSurface,
            BorderBrush = row.IsSelected
                ? FreePBrushes.Accent
                : FreePBrushes.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10),
            Margin = new Thickness(12, 0, 12, 10),
            Child = panel,
        };
        card.MouseLeftButtonUp += (_, _) => SelectAccessibilityCheckerRow(row.RowIndex);
        PresentationPaneAccessibilityAdapter.ApplyItem(
            card,
            PresentationPaneAccessibilityPlanner.PlanItem(
                PresentationPaneAccessibilityPlanner.AccessibilityPaneId,
                row.RowIndex,
                row.Title,
                row.IsSelected,
                row.AccessibilityKey));
        return card;
    }

    private void RenderTableStructureReviewDetails(PresentationTableStructureReviewDisplayPlan? display)
    {
        _accessibilityCheckerReviewDetailsPanel.Children.Clear();
        _accessibilityCheckerTableStructureReviewRenderedLines.Clear();
        if (display is null)
            return;

        _accessibilityCheckerTableStructureReviewRenderedLines.Add(display.Heading);
        _accessibilityCheckerTableStructureReviewRenderedLines.Add(display.Summary);
        _accessibilityCheckerTableStructureReviewRenderedLines.Add(display.Guidance);

        _accessibilityCheckerReviewDetailsPanel.Children.Add(new TextBlock
        {
            Text = display.Heading,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        _accessibilityCheckerReviewDetailsPanel.Children.Add(new TextBlock
        {
            Text = display.Summary,
            Foreground = FreePBrushes.PaneText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 4),
        });
        _accessibilityCheckerReviewDetailsPanel.Children.Add(new TextBlock
        {
            Text = display.Guidance,
            Foreground = FreePBrushes.PaneSecondaryText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        });

        foreach (var detail in display.Details)
        {
            _accessibilityCheckerTableStructureReviewRenderedLines.Add(detail.RenderedLine);
            _accessibilityCheckerReviewDetailsPanel.Children.Add(BuildTableStructureReviewDetail(detail));
        }
    }

    private static UIElement BuildTableStructureReviewDetail(PresentationTableStructureReviewDetailRowPlan detail)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical };
        panel.Children.Add(new TextBlock
        {
            Text = detail.Category,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = detail.Summary,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = detail.Detail,
            Foreground = FreePBrushes.PaneSecondaryText,
            TextWrapping = TextWrapping.Wrap,
        });

        return new Border
        {
            Background = FreePBrushes.SubtlePaneSurface,
            BorderBrush = FreePBrushes.SubtlePaneBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 6),
            Child = panel,
        };
    }

    private void RefreshAltTextRequestPlan()
        => _altTextPaneHostCoordinator.RefreshSelection();

    internal IReadOnlyList<SmartArtNodeOutlineItem> ShowSmartArtTextPane() =>
        SmartArtTextPaneHostCoordinator.Show();

    internal void HideSmartArtTextPane() => SmartArtTextPaneHostCoordinator.Hide();

    private PresentationWorkareaPaneHostCoordinator<IReadOnlyList<SmartArtNodeOutlineItem>>?
        _smartArtTextPaneHostCoordinator;

    private PresentationWorkareaPaneHostCoordinator<IReadOnlyList<SmartArtNodeOutlineItem>>
        SmartArtTextPaneHostCoordinator =>
        _smartArtTextPaneHostCoordinator ??= new(
            _workareaSession.Panes,
            PresentationWorkareaPane.SmartArtText,
            RefreshSmartArtTextPane,
            visible => _smartArtTextPaneHost.Visibility = visible
                ? Visibility.Visible
                : Visibility.Collapsed,
            RefreshPaneAccessibilityMetadata);

    internal void SetSmartArtTextPaneRowText(int rowIndex, string text)
    {
        if (!IsSmartArtTextPaneVisible)
            ShowSmartArtTextPane();

        var row = _smartArtTextPaneRowsPanel.Children.OfType<TextBox>().ElementAt(rowIndex);
        SetTextIfChanged(row, text);
    }

    internal SmartArtTextPaneApplyResult ApplySmartArtTextPane()
    {
        var rows = _smartArtTextPaneRowsPanel.Children
            .OfType<TextBox>()
            .Select(box => box.Tag is SmartArtNodeOutlineItem item
                ? new SmartArtTextPaneOutlineRow(box.Text, item.Level, item.IsAssistant, item.ModelId)
                : new SmartArtTextPaneOutlineRow(box.Text, 0))
            .ToArray();
        return _smartArtTextPaneSession.ApplyOutline(rows);
    }


    private async Task ReplaceSmartArtTextPanePictureFromFileAsync()
    {
        var result = await ImportPresentationAssetAsync(PresentationAssetImportKind.SmartArtPicture);
        await MaterializePresentationAssetImportResultAsync(
            result,
            PresentationAssetImportOutcomePolicy.SmartArtPane,
            statusText => _smartArtTextPaneMessage.Text = statusText);
    }

    private SmartArtNodeEditResult? ApplySmartArtTextPanePicture(
        byte[] imageBytes,
        string contentType) =>
        _smartArtTextPaneSession.ApplyPicture(imageBytes, contentType);

    private void ClearSmartArtTextPanePicture()
        => _smartArtTextPaneSession.ClearPicture();


    private SmartArtNodeEditResult? ToggleSmartArtTextPaneAssistant() => _smartArtTextPaneSession.ToggleAssistant();


    private SmartArtNodeEditResult? ApplySmartArtTextPaneAction(SmartArtNodeEditKind kind) => _smartArtTextPaneSession.ApplyAction(kind);




    private SmartArtLayoutApplyResult ApplySmartArtLayoutPreset(SmartArtLayoutPreset preset) => _smartArtTextPaneSession.ApplyLayoutPreset(preset);
    private SmartArtQuickStyleApplyResult ApplySmartArtQuickStylePreset(SmartArtQuickStylePreset preset) => _smartArtTextPaneSession.ApplyQuickStylePreset(preset);
    private SmartArtColorApplyResult ApplySmartArtColorPreset(SmartArtColorPreset preset) => _smartArtTextPaneSession.ApplyColorPreset(preset);


    private IReadOnlyList<SmartArtNodeOutlineItem> RefreshSmartArtTextPane() => _smartArtTextPaneSession.Refresh().Rows;
    private void RenderSmartArtTextPane(PresentationSmartArtTextPanePlan plan) => _smartArtTextPaneNativeView.Render(plan);

    private TextBox BuildSmartArtTextPaneRow(SmartArtNodeOutlineItem item)
    {
        var selected = StringComparer.Ordinal.Equals(
            item.ModelId,
            _smartArtTextPaneSession.SelectedModelId);
        var box = new TextBox
        {
            Text = item.Text,
            Tag = item,
            MinHeight = 26,
            Margin = new Thickness(12 + (item.Level * 18), 0, 12, 6),
            Padding = new Thickness(6, 3, 6, 3),
            BorderBrush = selected
                ? FreePBrushes.Accent
                : FreePBrushes.DisabledBorder,
            BorderThickness = new Thickness(selected ? 2 : 1),
            ToolTip = item.RoleDisplayText,
        };
        box.GotKeyboardFocus += (_, _) => _smartArtTextPaneSession.SelectModel(item.ModelId);
        box.KeyDown += (_, e) =>
        {
            if (_smartArtTextPaneRefreshing)
                return;

            if (!TryMapSmartArtTextPaneKey(e.Key, Keyboard.Modifiers, out var key, out var modifiers))
                return;

            _smartArtTextPaneSession.SelectModel(item.ModelId);
            var result = ApplySmartArtTextPaneKeyboardRoute(key, modifiers);
            if (result is not null)
                e.Handled = true;
        };
        return box;
    }

    private SmartArtNodeEditResult? ApplySmartArtTextPaneKeyboardRoute(
        SmartArtTextPaneShortcutKey key,
        SmartArtTextPaneShortcutModifiers modifiers) =>
        _smartArtTextPaneSession.ApplyKeyboardRoute(key, modifiers);

    private void ConvertSelectedSmartArtToShapes()
        => _smartArtTextPaneSession.ConvertSelectedToShapes();

    private static bool TryMapSmartArtTextPaneKey(
        Key key,
        ModifierKeys keyboardModifiers,
        out SmartArtTextPaneShortcutKey shortcutKey,
        out SmartArtTextPaneShortcutModifiers modifiers)
    {
        shortcutKey = key switch
        {
            Key.Enter or Key.Return => SmartArtTextPaneShortcutKey.Enter,
            Key.Tab => SmartArtTextPaneShortcutKey.Tab,
            Key.Up => SmartArtTextPaneShortcutKey.Up,
            Key.Down => SmartArtTextPaneShortcutKey.Down,
            Key.Delete => SmartArtTextPaneShortcutKey.Delete,
            _ => default
        };
        if (key is not (Key.Enter or Key.Return or Key.Tab or Key.Up or Key.Down or Key.Delete))
        {
            modifiers = SmartArtTextPaneShortcutModifiers.None;
            return false;
        }

        modifiers = SmartArtTextPaneShortcutModifiers.None;
        if (keyboardModifiers.HasFlag(ModifierKeys.Shift))
            modifiers |= SmartArtTextPaneShortcutModifiers.Shift;
        if (keyboardModifiers.HasFlag(ModifierKeys.Control))
            modifiers |= SmartArtTextPaneShortcutModifiers.Control;
        if (keyboardModifiers.HasFlag(ModifierKeys.Alt))
            modifiers |= SmartArtTextPaneShortcutModifiers.Alt;
        return true;
    }

    internal void ShowAltTextPane() => _altTextPaneHostCoordinator.Show();

    internal void HideAltTextPane() => _altTextPaneHostCoordinator.Hide();

    private PresentationMediaCaptionHostSnapshot CaptureMediaCaptionHostSnapshot() =>
        _wpfMediaPaneHostView.CaptureCaption();
    private PresentationMediaVolumeHostSnapshot CaptureMediaVolumeHostSnapshot() =>
        _wpfMediaPaneHostView.CaptureVolume();
    private PresentationMediaPlaybackHostSnapshot CaptureMediaPlaybackHostSnapshot() =>
        _wpfMediaPaneHostView.CapturePlayback();
    private PresentationMediaTimingHostSnapshot CaptureMediaTimingHostSnapshot() =>
        _wpfMediaPaneHostView.CaptureTiming();
    private PresentationMediaBookmarkHostSnapshot CaptureMediaBookmarkHostSnapshot() =>
        _wpfMediaPaneHostView.CaptureBookmark();

    private PresentationMediaPaneHostViewAdapter BuildWpfMediaPaneHostView() =>
        PresentationMediaPaneNativeComposition.Compose(
            new PresentationMediaPaneNativeControls<FrameworkElement>(
                _mediaCaptionPaneHost,
                _mediaCaptionLabelBox,
                _mediaCaptionLanguageBox,
                _mediaCaptionSourceBox,
                _mediaCaptionTranscriptBox,
                _mediaVolumeSlider,
                _mediaStartModeBox,
                _mediaLoopCheckBox,
                _mediaShowWhenStoppedCheckBox,
                _mediaRewindAfterPlayingCheckBox,
                _mediaPlayFullScreenCheckBox,
                _mediaStopAfterSlidesBox,
                _mediaTrimStartBox,
                _mediaTrimEndBox,
                _mediaFadeInBox,
                _mediaFadeOutBox,
                _mediaBookmarkNameBox,
                _mediaBookmarkTimeBox,
                _mediaCaptionPaneHeading,
                _mediaCaptionPaneMessage,
                _mediaPlaybackApplyButton,
                _mediaVolumeApplyButton,
                _mediaTimingApplyButton),
            new PresentationMediaPaneNativeAccessors<FrameworkElement>(
                control => control?.Visibility == Visibility.Visible,
                (control, value) => control.Visibility = value ? Visibility.Visible : Visibility.Collapsed,
                control => (control as TextBox)?.Text,
                WriteWpfMediaText,
                control => (control as Slider)?.Value,
                (control, value) => ((Slider)control).Value =
                    value ?? PresentationMediaPaneSession.DefaultVolumePercent,
                control => (control as ComboBox)?.SelectedIndex,
                (control, value) => ((ComboBox)control).SelectedIndex = value ?? -1,
                control => (control as CheckBox)?.IsChecked,
                (control, value) => ((CheckBox)control).IsChecked = value,
                (control, value) => control.IsEnabled = value),
            RenderMediaCaptionTrackOptions,
            RenderWpfMediaCaptionField,
            RenderWpfMediaCaptionAction,
            RenderWpfMediaBookmarkOptions,
            RefreshPaneAccessibilityMetadata);

    private static void WriteWpfMediaText(FrameworkElement control, string? value)
    {
        switch (control)
        {
            case TextBox textBox:
                textBox.Text = value ?? string.Empty;
                break;
            case TextBlock textBlock:
                textBlock.Text = value ?? string.Empty;
                break;
            default:
                throw new ArgumentException("The media control does not expose text.", nameof(control));
        }
    }

    private void RenderWpfMediaCaptionField(
        PresentationMediaPaneCaptionField field,
        PresentationMediaCaptionAuthoringFieldPlan plan)
    {
        var controls = _mediaCaptionControls.Get(field);
        RenderMediaCaptionField(controls.Label, controls.Input, plan);
    }

    private void RenderWpfMediaCaptionAction(
        PresentationMediaPaneCaptionAction action,
        PresentationMediaCaptionAuthoringActionPlan plan)
    {
        ApplyMediaCaptionButtonPlan(_mediaPaneButtons.Get(action), plan);
    }

    private void RenderWpfMediaBookmarkOptions(PresentationMediaPaneProjection plan)
    {
        PresentationMediaBookmarkNativeAdapter.Render(
            plan,
            new PresentationMediaBookmarkNativeBindings<ComboBoxItem>(
                Clear: () => _mediaBookmarkBox.Items.Clear(),
                CreateItem: bookmark => new() { Content = bookmark.DisplayText, Tag = bookmark.Index },
                AddItem: item => _mediaBookmarkBox.Items.Add(item),
                SetSelectedIndex: value => _mediaBookmarkBox.SelectedIndex = value,
                SetName: value => _mediaBookmarkNameBox.Text = value,
                SetTime: value => _mediaBookmarkTimeBox.Text = value,
                SetListEnabled: value => _mediaBookmarkBox.IsEnabled = value,
                SetNameEnabled: value => _mediaBookmarkNameBox.IsEnabled = value,
                SetTimeEnabled: value => _mediaBookmarkTimeBox.IsEnabled = value,
                SetCreateEnabled: value => _mediaBookmarkCreateButton.IsEnabled = value,
                SetReplaceEnabled: value => _mediaBookmarkReplaceButton.IsEnabled = value,
                SetDeleteEnabled: value => _mediaBookmarkDeleteButton.IsEnabled = value));
    }

    private void RenderMediaCaptionTrackOptions(PresentationMediaCaptionAuthoringPanePlan plan)
    {
        PresentationMediaCaptionTrackNativeAdapter.Render(
            plan,
            new PresentationMediaCaptionTrackNativeBindings<ComboBoxItem>(
                Clear: () => _mediaCaptionTrackBox.Items.Clear(),
                CreateItem: track => new() { Content = track.DisplayText, Tag = track.TrackIndex },
                ApplyAccessibility: PresentationPaneAccessibilityAdapter.ApplyItem,
                AddItem: item => _mediaCaptionTrackBox.Items.Add(item),
                SetEnabled: value => _mediaCaptionTrackBox.IsEnabled = value,
                SetSelectedIndex: value => _mediaCaptionTrackBox.SelectedIndex = value));
    }

    private void RefreshVisibleMediaCaptionPaneFromFields() => _mediaPaneHostCoordinator.Refresh();

    private static void RenderMediaCaptionField(
        TextBlock label,
        TextBox textBox,
        PresentationMediaCaptionAuthoringFieldPlan field)
    {
        label.Text = field.DisplayLabel;
        textBox.ToolTip = field.ToolTip;
        textBox.IsEnabled = field.IsEnabled;
        SetTextIfChanged(textBox, field.Value);
    }

    private static void ApplyMediaCaptionButtonPlan(
        Button button,
        PresentationMediaCaptionAuthoringActionPlan action)
    {
        button.Content = action.Label;
        button.IsEnabled = action.IsEnabled;
        button.ToolTip = action.DisabledReason;
    }

    internal PresentationReadingOrderPlan ShowReadingOrderPane()
        => _reviewWorkflowSession.ShowReadingOrderPane();

    internal PresentationSelectionPanePlan ShowSelectionPane() => SelectionPaneHostCoordinator.Show();

    private PresentationWorkareaPaneHostCoordinator<PresentationSelectionPanePlan>?
        _selectionPaneHostCoordinator;

    private PresentationWorkareaPaneHostCoordinator<PresentationSelectionPanePlan>
        SelectionPaneHostCoordinator =>
        _selectionPaneHostCoordinator ??= new(
            _workareaSession.Panes,
            PresentationWorkareaPane.Selection,
            _selectionPane.Refresh,
            visible => _selectionPane.Visibility = visible
                ? Visibility.Visible
                : Visibility.Collapsed,
            RefreshPaneAccessibilityMetadata);

    internal PresentationReadingOrderMutationPlan ApplyReadingOrderMoveEarlier()
        => ApplyReadingOrderMove(PresentationReviewWorkflowIntentKind.MoveReadingOrderEarlier);

    internal PresentationReadingOrderMutationPlan ApplyReadingOrderMoveLater()
        => ApplyReadingOrderMove(PresentationReviewWorkflowIntentKind.MoveReadingOrderLater);

    internal PresentationReadingOrderSelectionPlan ApplyReadingOrderSelectItem(uint shapeId)
        => _reviewWorkflowSession.SelectReadingOrderItem(shapeId);

    private PresentationReadingOrderMutationPlan ApplyReadingOrderMove(
        PresentationReviewWorkflowIntentKind intent)
        => _reviewWorkflowSession.ApplyReadingOrderMove(intent);

    internal void SetAltTextPaneInput(string title, string description, bool isDecorative)
        => _altTextPaneHostCoordinator.SetInput(new(title, description, isDecorative));

    internal PresentationAltTextMutationPlan ApplyAltTextPane() =>
        _altTextPaneHostCoordinator.Apply();

    private void RefreshVisibleAltTextPaneFromFields() => _altTextPaneHostCoordinator.Refresh();

    private void RenderAltTextPaneIfVisible(PresentationAltTextPanePlan plan) =>
        _altTextPaneHostCoordinator.RenderIfVisible(plan);

    bool IPresentationAltTextPaneHostView.IsPaneVisible =>
        _altTextPaneHost?.Visibility == Visibility.Visible;

    PresentationAltTextPaneHostSnapshot IPresentationAltTextPaneHostView.CaptureInput() =>
        new(_altTextTitleBox.Text, _altTextDescriptionBox.Text, _altTextDecorativeCheck.IsChecked == true);

    void IPresentationAltTextPaneHostView.SetPaneVisible(bool visible) =>
        _altTextPaneHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    void IPresentationAltTextPaneHostView.SetInput(PresentationAltTextPaneHostSnapshot input)
    {
        _altTextTitleBox.Text = input.Title ?? string.Empty;
        _altTextDescriptionBox.Text = input.Description ?? string.Empty;
        _altTextDecorativeCheck.IsChecked = input.IsDecorative;
    }

    void IPresentationAltTextPaneHostView.Render(PresentationAltTextPaneHostRenderPlan plan)
    {
        _altTextPaneHeading.Text = plan.Heading;
        _altTextPaneMessage.Text = plan.Message;
        _altTextTitleLabel.Text = plan.Title.Label;
        _altTextDescriptionLabel.Text = plan.Description.Label;
        SetTextIfChanged(_altTextTitleBox, plan.Title.Value);
        SetTextIfChanged(_altTextDescriptionBox, plan.Description.Value);
        _altTextTitleBox.ToolTip = plan.Title.Placeholder;
        _altTextDescriptionBox.ToolTip = plan.Description.ValidationMessage ?? plan.Description.Placeholder;
        _altTextTitleBox.IsEnabled = plan.Title.IsEnabled;
        _altTextDescriptionBox.IsEnabled = plan.Description.IsEnabled;
        _altTextDecorativeCheck.Content = plan.DecorativeAction.Label;
        _altTextDecorativeCheck.IsEnabled = plan.DecorativeAction.IsEnabled;
        _altTextDecorativeCheck.IsChecked = plan.IsDecorative;
        _altTextApplyButton.Content = plan.ApplyAction.Label;
        _altTextApplyButton.IsEnabled = plan.ApplyAction.IsEnabled;
        _altTextApplyButton.ToolTip = plan.ApplyAction.DisabledReason;
        _altTextCloseButton.Content = plan.CloseAction.Label;
        _altTextCloseButton.IsEnabled = plan.CloseAction.IsEnabled;
    }

    void IPresentationAltTextPaneHostView.RefreshAccessibilityMetadata() =>
        RefreshPaneAccessibilityMetadata();

    void IPresentationReadingOrderPaneHostView.SetPaneVisible(bool visible) =>
        _readingOrderPaneHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    void IPresentationReadingOrderPaneHostView.Render(PresentationReadingOrderPaneHostRenderPlan plan)
    {
        _readingOrderPaneHeading.Text = plan.Heading;
        _readingOrderPaneMessage.Text = plan.Message;
        ApplyReadingOrderButtonPlan(_readingOrderMoveEarlierButton, plan.MoveEarlierAction);
        ApplyReadingOrderButtonPlan(_readingOrderMoveLaterButton, plan.MoveLaterAction);

        _readingOrderPaneItemsPanel.Children.Clear();
        if (plan.ShouldShowEmptyState)
        {
            _readingOrderPaneItemsPanel.Children.Add(new TextBlock
            {
                Text = plan.EmptyStateMessage,
                Foreground = FreePBrushes.PaneMutedText,
                Margin = new Thickness(12, 0, 12, 10),
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        foreach (var item in plan.Items)
            _readingOrderPaneItemsPanel.Children.Add(BuildReadingOrderItemCard(item));
    }

    void IPresentationReadingOrderPaneHostView.RefreshAccessibilityMetadata() =>
        RefreshPaneAccessibilityMetadata();

    private static void ApplyReadingOrderButtonPlan(
        Button button,
        PresentationReadingOrderPaneActionRenderPlan action)
    {
        button.Content = action.Label;
        button.IsEnabled = action.IsEnabled;
        button.ToolTip = action.DisabledReason;
        button.Tag = action.CommandId;
    }

    private UIElement BuildReadingOrderItemCard(PresentationReadingOrderItemPlan item)
    {
        var title = new TextBlock
        {
            Text = item.DisplayTitle,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        var metadata = new TextBlock
        {
            Text = item.Metadata,
            Foreground = FreePBrushes.PaneSecondaryText,
            TextWrapping = TextWrapping.Wrap,
        };
        var accessibility = new TextBlock
        {
            Text = item.AccessibilitySummary,
            Foreground = FreePBrushes.PaneText,
            TextWrapping = TextWrapping.Wrap,
        };
        var altText = new TextBlock
        {
            Text = item.AltTextDisplayText,
            Foreground = FreePBrushes.PaneMutedText,
            TextWrapping = TextWrapping.Wrap,
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
        };
        panel.Children.Add(title);
        panel.Children.Add(metadata);
        panel.Children.Add(accessibility);
        panel.Children.Add(altText);

        if (item.IsSelected)
        {
            panel.Children.Insert(1, new TextBlock
            {
                Text = item.SelectedLabel,
                Foreground = FreePBrushes.Accent,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, PresentationReadingOrderPaneVisualMetrics.SelectedItemTopInset, 0, 0),
            });
        }

        var card = new Border
        {
            Background = item.IsSelected
                ? FreePBrushes.SelectedCardSurface
                : FreePBrushes.PaneSurface,
            BorderBrush = item.IsSelected
                ? FreePBrushes.Accent
                : FreePBrushes.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(PresentationReadingOrderPaneVisualMetrics.CardCornerRadius),
            Padding = new Thickness(PresentationReadingOrderPaneVisualMetrics.CardPadding),
            Margin = new Thickness(
                PresentationReadingOrderPaneVisualMetrics.ContentSideMargin,
                0,
                PresentationReadingOrderPaneVisualMetrics.ContentSideMargin,
                PresentationReadingOrderPaneVisualMetrics.CardBottomMargin),
            Child = panel,
        };

        var button = new Button
        {
            Content = card,
            Tag = PresentationReviewWorkflowPlanner.ReadingOrderSelectItemCommandId,
            ToolTip = item.SelectionToolTip,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        button.Click += (_, _) => ApplyReadingOrderSelectItem(item.ShapeId);
        PresentationPaneAccessibilityAdapter.ApplyItem(
            button,
            PresentationPaneAccessibilityPlanner.PlanItem(
                PresentationPaneAccessibilityPlanner.ReadingOrderPaneId,
                item.ReadingOrderIndex,
                item.ShapeName,
                item.IsSelected,
                PresentationPaneAccessibilityPlanner.BuildShapeKey(item.ShapeId)));
        return button;
    }

    private static void SetTextIfChanged(TextBox textBox, string value)
    {
        if (textBox.Text != value)
            textBox.Text = value;
    }

    internal PresentationAltTextMutationPlan ApplySelectedShapeAlternativeText(string? description, string? title = null, bool isDecorative = false) => _reviewWorkflowSession.ApplySelectedShapeAlternativeText(description, title, isDecorative);
    internal PresentationProofingCorrectionMutationPlan ApplyProofingCorrection(PresentationProofingScopeDescriptor scope, int start, int length, string? replacement) => _reviewWorkflowSession.ApplyProofingCorrection(scope, start, length, replacement);
    private void RefreshProofingRequestPlan() => _reviewWorkflowSession.RefreshProofingRequestPlan();
    internal PresentationProofingPanePlan ShowProofingPane() => _reviewPaneHostCoordinator.ShowProofingPane();
    internal PresentationProofingPanePlan SelectProofingIssueRow(int rowIndex) => _reviewWorkflowSession.SelectProofingIssueRow(rowIndex);
    internal PresentationProofingCorrectionMutationPlan ApplySelectedProofingCorrection() => _reviewWorkflowSession.ApplySelectedProofingCorrection();
    internal PresentationProofingPanePlan IgnoreSelectedProofingIssue() => _reviewWorkflowSession.IgnoreSelectedProofingIssue();
    internal PresentationProofingPanePlan IgnoreAllSelectedProofingIssues() => _reviewWorkflowSession.IgnoreAllSelectedProofingIssues();
    internal PresentationProofingPanePlan AddSelectedProofingWordToDictionary() => _reviewWorkflowSession.AddSelectedProofingWordToDictionary();

    private void RenderProofingPane(PresentationProofingPanePlan plan)
        => _proofingPaneNativeView.Render(plan);

    private static UIElement BuildProofingEmptyState(string message) =>
        new TextBlock
        {
            Text = message,
            Foreground = FreePBrushes.PaneMutedText,
            Margin = new Thickness(12, 0, 12, 10),
            TextWrapping = TextWrapping.Wrap,
        };

    private UIElement BuildProofingIssueRowCard(PresentationProofingIssueRowPlan row)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var action in PresentationMainWindowReviewPaneCoordinator.BuildProofingRowActions(row))
        {
            var button = new Button
            {
                Content = action.Label,
                Tag = row.RowIndex,
                MinWidth = action.MinimumWidth,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(action.HasLeadingSpacing ? 8 : 0, 8, 0, 0),
                IsEnabled = action.IsEnabled,
                ToolTip = action.DisabledReason,
            };
            button.Click += (_, _) =>
                _reviewPaneHostCoordinator.ExecuteProofingRowAction(row.RowIndex, action.Kind);
            buttons.Children.Add(button);
        }

        var panel = new StackPanel { Orientation = Orientation.Vertical };
        panel.Children.Add(new TextBlock
        {
            Text = row.DisplayTitle,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = row.ReplacementDisplayText,
            Foreground = FreePBrushes.PaneSecondaryText,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = row.Message,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(buttons);

        var card = new Border
        {
            Background = row.IsSelected ? FreePBrushes.SelectedRowSurface : Brushes.Transparent,
            BorderBrush = FreePBrushes.GridBorder,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 8, 12, 8),
            Child = panel,
        };
        PresentationPaneAccessibilityAdapter.ApplyItem(
            card,
            PresentationPaneAccessibilityPlanner.PlanItem(
                PresentationPaneAccessibilityPlanner.ProofingPaneId,
                row.RowIndex,
                row.Text,
                row.IsSelected,
                row.AccessibilityKey));
        return card;
    }

    // ── Wave 16B: Animation pane show/hide ───────────────────────────────────────
    //
    // ToggleAnimationPane is called by the ribbon host profile when the freep.anim.pane
    // toggle button is pressed.  It lazily constructs the AnimationPane on first show
    // and toggles _animPaneHost visibility.
    //
    // RebuildAnimationPaneIfVisible is called from LoadModel (file new/open) so the
    // pane reflects the new editor if it is currently visible.
    //
    // 16B SEAM: keep these two methods contiguous; do not add code between them.

    /// <summary>
    /// Shows or hides the animation pane.  The first call creates the pane; subsequent
    /// calls toggle visibility.  Called by the freep.anim.pane ribbon toggle.
    /// </summary>
    internal void ToggleAnimationPane()
    {
        if (_animPaneHost.Visibility == Visibility.Visible)
        {
            _animPaneHost.Visibility = Visibility.Collapsed;
            RefreshPaneAccessibilityMetadata();
        }
        else
        {
            // Lazy construction: create the pane against the current Editor.
            if (_animPane is null || _animPaneHost.Child is null)
            {
                _animPane = new AnimationPane(
                    _animationPaneSession,
                    onPreview: StartAnimationPanePreview,
                    onAccessibilityChanged: RefreshPaneAccessibilityMetadata,
                    onEditMotionPath: OpenMotionPathEditor);
                _animPaneHost.Child = _animPane;
            }
            else
            {
                _animPane.Rebuild();
            }
            _animPaneHost.Visibility = Visibility.Visible;
            RefreshPaneAccessibilityMetadata();
        }
    }

    /// <summary>
    /// Rebuilds the visible native projection from the shared animation-pane session.
    /// The session resolves the current editor lazily, so replacing the presentation does not
    /// require replacing the WPF control or creating a second lifecycle owner.
    /// </summary>
    private void RebuildAnimationPaneIfVisible()
    {
        if (_animPaneHost is null || _animPaneHost.Visibility != Visibility.Visible) return;
        _animPane?.Rebuild();
        RefreshPaneAccessibilityMetadata();
    }

    private void OpenMotionPathEditor(int animationIndex)
    {
        var dialog = new MotionPathEditorDialog(Editor, animationIndex)
        {
            Owner = this,
        };
        dialog.ShowDialog();
    }

    private void StartAnimationPanePreview(AnimationPanePlaybackSessionPlan session)
    {
        var animationStartIndex = session.CommandKind == AnimationPanePlaybackControlKind.PlayFromSelected
            ? session.StartAnimationIndex
            : null;
        StartSlideShow(
            fromStart: false,
            timingIntent: FreeP.App.Compositor.SlideShowTimingIntent.None,
            animationStartIndex: animationStartIndex);
    }

    // END 16B SEAM

    // ── Status bar ────────────────────────────────────────────────────────────────

    private Border BuildStatusBar()
    {
        _slideCountText = SisterAppStatusBarChrome.CreateInfoText();
        return SisterAppStatusBarChrome.Build(new SisterAppStatusBarSpec(
            WpfThemeResourceResolver.Find<Brush>(ThemeResources.StatusSurfaceBrush)
                ?? FreePBrushes.Accent,
            _slideCountText,
            LeftMargin: new Thickness(12, 0, 0, 0))).Root;
    }

    private void UpdateSlideCount() =>
        _slideCountText.Text = _workareaSession
            .BuildStatusPlan(FreePApplicationFrameDescriptor.ResolveDataFolderLabel())
            .Text;

    /// <summary>
    /// Copy/Cut used to swallow OS-clipboard write failures entirely (<see
    /// cref="OsClipboardService.LastWriteFailureMessage"/> went unread), leaving the user believing
    /// content was copied when it was not. Surface it in the status bar, mirroring how the Avalonia
    /// shell reports command failures.
    /// </summary>
    private void ReportClipboardWriteFailure(string command, string message) =>
        _slideCountText.Text = SisterAppFileTextPlanner.FormatCommandFailed(
            PresentationFileTextResources.Presentation,
            command,
            message);

    // ── Quick-access + title ──────────────────────────────────────────────────────

    private void AddQuickAccessButtons(StackPanel host) =>
        SisterQuickAccessToolbarBuilder.Render(
            host,
            this,
            new SisterQuickAccessToolbarActions(
                Save: () => _workareaSession.ExecuteCommand(FreePKeyboardCommand.SavePresentation),
                Undo: () => _workareaSession.ExecuteCommand(FreePKeyboardCommand.Undo),
                Redo: () => _workareaSession.ExecuteCommand(FreePKeyboardCommand.Redo)),
            new QuickAccessToolbarRenderOptions
            {
                ForegroundResourceKey = ThemeResources.Brush("TitleBarForeground").PrimaryKey
            });

    private void UpdateTitle()
    {
        var title = FreePApplicationFrameDescriptor.Title;
        _titleBinder.Update(new SisterWpfWindowTitleSpec(
            DisplayName: _fileSession.DisplayName,
            ApplicationName: title.ApplicationName,
            IsDirty: _fileSession.IsDirty,
            DirtyMarker: title.DirtyMarker,
            Separator: title.Separator,
            ApplicationPlacement: title.ApplicationPlacement,
            WindowSuffix: PresentationDocumentWindowPlanner.FormatWindowSuffix(_documentWindowNumber)));
    }

    // ── Keyboard bindings ─────────────────────────────────────────────────────────

    private void InstallSharedKeyboardShortcuts()
    {
        var commands = Enum.GetValues<FreePKeyboardCommand>()
            .ToDictionary(
                command => command,
                command => new RoutedUICommand(command.ToString(), $"FreeP{command}", typeof(MainWindow)));

        foreach (var (command, routedCommand) in commands)
        {
            CommandBindings.Add(new CommandBinding(
                routedCommand,
                (_, _) => _workareaSession.ExecuteCommand(command)));
        }

        foreach (var shortcut in FreePKeyboardShortcutCatalog.All)
        {
            InputBindings.Add(new KeyBinding(
                commands[shortcut.Command],
                new KeyGesture(ToWpfKey(shortcut.Key), ToWpfModifiers(shortcut.Modifiers))));
        }
    }

    private static Key ToWpfKey(FreePKeyboardKey key) =>
        Enum.Parse<Key>(key.ToString());

    private static ModifierKeys ToWpfModifiers(FreePKeyboardModifiers modifiers)
    {
        var result = ModifierKeys.None;
        if ((modifiers & FreePKeyboardModifiers.Control) != 0)
            result |= ModifierKeys.Control;
        if ((modifiers & FreePKeyboardModifiers.Shift) != 0)
            result |= ModifierKeys.Shift;
        if ((modifiers & FreePKeyboardModifiers.Alt) != 0)
            result |= ModifierKeys.Alt;
        return result;
    }

    // ── Slide show (Wave 4B) ──────────────────────────────────────────────────────

    /// <summary>
    /// Launches the fullscreen slide show playback.
    /// Called by F5 (fromStart=true) and Shift+F5 (fromStart=false).
    /// Wave 4C adds ribbon buttons that call this method; keep internal/public + discoverable.
    /// </summary>
    internal void StartSlideShow(bool fromStart)
        => StartSlideShow(fromStart, FreeP.App.Compositor.SlideShowTimingIntent.None);

    /// <summary>
    /// Opens the current slide in a resizable, non-editing Reading View window without
    /// changing the presentation's persisted slide-show settings.
    /// </summary>
    internal void StartReadingView() => SlideShowWindowLauncher.TryLaunchReadingView();

    private void StartSlideShowWithTiming(FreeP.App.Compositor.SlideShowTimingIntent timingIntent)
        => StartSlideShow(fromStart: true, timingIntent: timingIntent);

    private void StartSlideShow(
        bool fromStart,
        FreeP.App.Compositor.SlideShowTimingIntent timingIntent,
        int? animationStartIndex = null)
        => SlideShowWindowLauncher.TryLaunch(fromStart, timingIntent, animationStartIndex);

    // ── Chart data editing (Wave 9B) ──────────────────────────────────────────────

    /// <summary>
    /// Builds the playback route for a stored custom show without opening a window.
    /// </summary>
    internal bool TryBuildCustomSlideShowRoute(
        string? customShowName,
        int startIndex,
        out SlideShowPlaybackRoute route) =>
        _customShowSession.TryBuildNamedRoute(customShowName, startIndex, out route);

    internal SlideShowLaunchPlan BuildSlideShowLaunchPlan() =>
        _customShowSession.BuildLaunchPlan();

    internal bool TryStartCustomSlideShow(string? customShowName, int startIndex = 0)
        => SlideShowWindowLauncher.TryLaunchNamed(customShowName, startIndex);

    private SlideShowWindowLaunchCoordinator<SlideShowWindow>? _slideShowWindowLauncher;

    private SlideShowWindowLaunchCoordinator<SlideShowWindow> SlideShowWindowLauncher =>
        _slideShowWindowLauncher ??= new(
            _customShowSession,
            () => _presentation,
            () => _mediaPaneHostCoordinator.SelectedCaptionTrackIndex,
            Editor.SetSlideNotesText,
            static plan => new SlideShowWindow(plan),
            static (window, intent) => window.SetPresenterTimingIntent(intent),
            ShowSlideShowWindow);

    private void ShowSlideShowWindow(SlideShowWindow window)
    {
        // Owner can only be set when the main window is already shown (not during unit tests).
        if (IsVisible)
            window.Owner = this;
        window.Show();
    }

    internal void OpenCustomShowDialog()
    {
        ShowDomainDialog(new CustomShowDialog(
            _customShowSession,
            name => TryStartCustomSlideShow(name)));
    }

    private void ShowDomainDialog(Window dialog)
    {
        if (IsVisible)
            dialog.Owner = this;
        dialog.ShowDialog();
    }

    internal void OpenRotationOptionsDialog()
    {
        if (!_workareaSession.CanOpenDomainDialog(PresentationDomainDialogKind.RotationOptions))
            return;

        var dialog = new RotationOptionsDialog(Editor) { Owner = this };
        dialog.ShowDialog();
    }

    // ── Slide size dialog (Wave 10B) ──────────────────────────────────────────────

    /// <summary>
    /// Opens the <see cref="SlideSizeDialog"/> for the current presentation.
    /// On OK the session's <see cref="EditingSession.SetSlideSize"/> is called (undoable).
    /// </summary>
    internal void OpenSlideSizeDialog()
    {
        ShowDomainDialog(new SlideSizeDialog(Editor));
    }

    internal void OpenHeaderFooterDialog(HeaderFooterCommandFocus focus)
    {
        ShowDomainDialog(new HeaderFooterDialog(Editor, focus));
    }

    internal void OpenSlideShowSettingsDialog()
    {
        ShowDomainDialog(new SlideShowSettingsDialog(Editor));
    }

    internal void OpenLayoutPicker()
    {
        LastLayoutRequestPlan = PresentationDesignCommandPlanner.LayoutPlan;
        LastLayoutPickerPlan = PresentationDesignCommandPlanner.BuildLayoutPickerPlan(
            _presentation,
            Editor.CurrentSlideIndex);
        ShowLayoutPicker(LastLayoutPickerPlan);
    }

    internal bool ApplyLayoutChoice(string layoutId)
    {
        var applied = PresentationDesignCommandPlanner.TryApplyLayoutChoice(
            Editor,
            layoutId,
            out var choice);
        if (applied)
        {
            LastAppliedLayoutChoice = choice;
            RefreshCanvas();
            UpdateSlideCount();
            HideLayoutPicker();
        }

        return applied;
    }

    internal void OpenTablePicker()
    {
        LastTablePickerPlan = TableInsertionPickerPlanner.BuildPlan();
        ShowTablePicker(LastTablePickerPlan);
    }

    internal bool ApplyTablePickerChoice(int rows, int columns)
    {
        var applied = TableInsertionPickerPlanner.TryApplyChoice(Editor, rows, columns);
        if (applied)
        {
            RefreshCanvas();
            UpdateSlideCount();
            HideTablePicker();
        }

        return applied;
    }

    private void PickTransitionSound()
    {
        _ = ImportPresentationAssetAsync(PresentationAssetImportKind.TransitionSound);
    }

    private void InsertEmbeddedObjectFromFile()
    {
        _ = ImportPresentationAssetAsync(PresentationAssetImportKind.EmbeddedObject);
    }

    private void ShowTablePicker(TableInsertionPickerPlan plan)
    {
        if (_tablePickerHost is null || _tablePickerGrid is null)
            return;

        _tablePickerGrid.Rows = plan.MaxRows;
        _tablePickerGrid.Columns = plan.MaxColumns;
        _tablePickerGrid.Children.Clear();
        foreach (var choice in plan.Choices)
        {
            var button = new Button
            {
                Tag = choice,
                Content = choice.DisplayLabel,
                Margin = new Thickness(2),
                Padding = new Thickness(6, 4, 6, 4),
                MinWidth = 74,
                BorderBrush = choice.IsDefault
                    ? FreePBrushes.Accent
                    : FreePBrushes.DisabledBorder,
                Background = choice.IsDefault
                    ? FreePBrushes.SelectedSwatchSurface
                    : FreePBrushes.White,
            };
            AutomationProperties.SetAutomationId(button, choice.AutomationId);
            button.Click += (_, _) =>
            {
                if (button.Tag is TableInsertionPickerChoice tableChoice)
                    ApplyTablePickerChoice(tableChoice.Rows, tableChoice.Columns);
            };
            _tablePickerGrid.Children.Add(button);
        }

        HideLayoutPicker();
        _tablePickerHost.Visibility = Visibility.Visible;
    }

    private void HideTablePicker()
    {
        if (_tablePickerHost is not null)
            _tablePickerHost.Visibility = Visibility.Collapsed;
    }

    private void ShowLayoutPicker(PresentationLayoutPickerPlan plan)
    {
        if (_layoutPickerHost is null || _layoutPickerPanel is null)
            return;

        PresentationLayoutPickerNativeAdapter.Populate(
            plan,
            _layoutPickerPanel,
            new PresentationLayoutPickerNativeBindings<StackPanel, TextBlock, WrapPanel, Button>(
                Clear: root => root.Children.Clear(),
                CreateHeading: group => new TextBlock
                {
                    Text = group.Heading,
                    Margin = new Thickness(10, 8, 10, 2),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = FreePBrushes.PaneText,
                },
                CreateGroup: _ => new WrapPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(4, 0, 4, 4),
                },
                CreateChoice: choice =>
                {
                    var button = new Button
                    {
                        Content = BuildLayoutChoiceTile(choice),
                        Margin = new Thickness(4),
                        Padding = new Thickness(0),
                        HorizontalContentAlignment = HorizontalAlignment.Stretch,
                        IsEnabled = choice.Chrome.IsEnabled,
                    };
                    AutomationProperties.SetName(button, choice.DisplayLabel);
                    AutomationProperties.SetAutomationId(button, choice.AutomationId);
                    return button;
                },
                BindChoice: (choice, execute) => choice.Click += (_, _) => execute(),
                AddChoice: (group, choice) => group.Children.Add(choice),
                AddHeading: (root, heading) => root.Children.Add(heading),
                AddGroup: (root, group) => root.Children.Add(group)),
            layoutId => ApplyLayoutChoice(layoutId));

        HideTablePicker();
        _layoutPickerHost.Visibility = Visibility.Visible;
    }

    private void HideLayoutPicker()
    {
        if (_layoutPickerHost is not null)
            _layoutPickerHost.Visibility = Visibility.Collapsed;
    }

    private static UIElement BuildLayoutChoiceTile(PresentationLayoutChoice choice)
    {
        var label = new TextBlock
        {
            Text = choice.DisplayLabel,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 11,
            Margin = new Thickness(0, 5, 0, 0),
            Width = PresentationDesignCommandPlanner.LayoutThumbnailWidthDip,
        };

        var stack = new StackPanel
        {
            Width = PresentationDesignCommandPlanner.LayoutThumbnailWidthDip + 18,
        };
        stack.Children.Add(BuildLayoutThumbnail(choice));
        stack.Children.Add(label);

        if (!string.IsNullOrWhiteSpace(choice.Chrome.BadgeText))
        {
            stack.Children.Add(new TextBlock
            {
                Text = choice.Chrome.BadgeText,
                FontSize = 10,
                Foreground = BrushFromHex(
                    PresentationDesignCommandPlanner.LayoutPickerVisuals.BadgeForegroundBrushHex),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 3, 0, 0),
            });
        }

        return new Border
        {
            BorderBrush = BrushFromHex(choice.Chrome.BorderBrushHex),
            Background = BrushFromHex(choice.Chrome.BackgroundBrushHex),
            BorderThickness = new Thickness(choice.Chrome.BorderThicknessDip),
            Padding = new Thickness(8),
            Child = stack,
        };
    }

    private static UIElement BuildLayoutThumbnail(PresentationLayoutChoice choice)
    {
        var canvas = new Canvas
        {
            Width = PresentationDesignCommandPlanner.LayoutThumbnailWidthDip,
            Height = PresentationDesignCommandPlanner.LayoutThumbnailHeightDip,
            Background = BrushFromHex(
                PresentationDesignCommandPlanner.LayoutPickerVisuals.ThumbnailBackgroundBrushHex),
        };

        foreach (var placeholder in choice.ThumbnailPlaceholders)
        {
            var visual = placeholder.Visual;
            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = placeholder.Bounds.Width,
                Height = placeholder.Bounds.Height,
                Fill = BrushFromHex(visual.FillBrushHex),
                Stroke = BrushFromHex(visual.StrokeBrushHex),
                StrokeThickness = visual.StrokeThicknessDip,
                RadiusX = visual.CornerRadiusDip,
                RadiusY = visual.CornerRadiusDip,
            };
            Canvas.SetLeft(rect, placeholder.Bounds.X);
            Canvas.SetTop(rect, placeholder.Bounds.Y);
            canvas.Children.Add(rect);
        }

        return new Border
        {
            BorderBrush = BrushFromHex(
                PresentationDesignCommandPlanner.LayoutPickerVisuals.ThumbnailBorderBrushHex),
            BorderThickness = new Thickness(
                PresentationDesignCommandPlanner.LayoutPickerVisuals.ThumbnailBorderThicknessDip),
            Child = canvas,
        };
    }

    private static Brush BrushFromHex(string hex) =>
        new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);

    /// <summary>
    /// Opens the <see cref="HyperlinkDialog"/> for the currently selected shape(s).
    /// Wave 11A: pre-fills the dialog with the existing hyperlink if exactly one shape is selected.
    /// On OK, calls <see cref="EditingSession.SetShapeHyperlink"/> (undoable).
    /// </summary>
    internal void OpenHyperlinkDialog()
    {
        var textEditor = SlideCanvas.TextEditor;
        ModelHyperlink? selectedRunHyperlink = null;
        var editsSelectedRun = textEditor is not null
            && textEditor.TryGetSelectedShapeRunHyperlink(out selectedRunHyperlink);
        var request = _hyperlinkWorkflowSession.BuildRequest(
            editsSelectedRun,
            selectedRunHyperlink);
        var dialog = new HyperlinkDialog(request.DialogRequest);
        if (IsVisible) dialog.Owner = this;
        _hyperlinkWorkflowSession.Apply(
            request,
            dialog.ShowDialog() == true ? dialog.Result : null,
            hyperlink => textEditor?.TryApplySelectedShapeRunHyperlink(hyperlink) == true);
    }

    internal void OpenSlideZoomDialog()
    {
        var request = _zoomAuthoringSession.BuildSlideInsertionRequest();
        if (request is null)
            return;

        var dialog = new SingleTargetZoomDialog(
            ZoomTargetDialogKind.Slide,
            request.Options,
            request.Title,
            request.SelectedTargetId);
        if (IsVisible)
            dialog.Owner = this;
        if (dialog.ShowDialog() == true)
            _zoomAuthoringSession.ApplySlideInsertion(dialog.SelectedTargetId);
    }

    internal void OpenSectionZoomDialog()
    {
        var request = _zoomAuthoringSession.BuildSectionInsertionRequest();
        if (request is null)
            return;

        var dialog = new SingleTargetZoomDialog(
            ZoomTargetDialogKind.Section,
            request.Options,
            request.Title,
            request.SelectedTargetId);
        if (IsVisible)
            dialog.Owner = this;
        if (dialog.ShowDialog() == true)
            _zoomAuthoringSession.ApplySectionInsertion(dialog.SelectedTargetId);
    }

    internal void OpenSummaryZoomDialog()
    {
        var request = _zoomAuthoringSession.BuildSummaryInsertionRequest();
        if (request is null)
            return;

        var dialog = new SummaryZoomDialog(
            request.Options,
            request.Title,
            request.SelectedTargetIds);
        if (IsVisible)
            dialog.Owner = this;
        if (dialog.ShowDialog() == true)
            _zoomAuthoringSession.ApplySummaryInsertion(dialog.SelectedTargetSectionIds);
    }

    internal void OpenZoomTargetDialog()
    {
        var request = _zoomAuthoringSession.BuildSelectedTargetRequest();
        if (request is null)
            return;

        if (request.Kind == PresentationZoomTargetKind.Slide)
        {
            var dialog = new SingleTargetZoomDialog(
                ZoomTargetDialogKind.Slide,
                request.Options,
                request.Title,
                request.SelectedTargetId);
            if (IsVisible)
                dialog.Owner = this;
            if (dialog.ShowDialog() == true)
                _zoomAuthoringSession.ApplySelectedTarget(request, dialog.SelectedTargetId);
            return;
        }

        var sectionDialog = new SingleTargetZoomDialog(
            ZoomTargetDialogKind.Section,
            request.Options,
            request.Title,
            request.SelectedTargetId);
        if (IsVisible)
            sectionDialog.Owner = this;
        if (sectionDialog.ShowDialog() == true)
            _zoomAuthoringSession.ApplySelectedTarget(request, sectionDialog.SelectedTargetId);
    }

    internal void OpenSummaryZoomTargetsDialog()
    {
        var request = _zoomAuthoringSession.BuildSelectedSummaryTargetsRequest();
        if (request is null)
            return;

        var dialog = new SummaryZoomDialog(
            request.Options,
            request.Title,
            request.SelectedTargetIds);
        if (IsVisible) dialog.Owner = this;
        if (dialog.ShowDialog() == true)
            _zoomAuthoringSession.ApplySelectedSummaryTargets(
                request,
                dialog.SelectedTargetSectionIds);
    }

    internal void OpenZoomObjectPropertiesDialog()
    {
        var request = _zoomAuthoringSession.BuildSelectedPropertiesRequest();
        if (request is null)
            return;

        var dialog = new ZoomObjectPropertiesDialog(
            request.Properties,
            request.SummaryTargets,
            request.SummaryTileProperties);
        if (IsVisible)
            dialog.Owner = this;
        if (dialog.ShowDialog() == true)
        {
            _zoomAuthoringSession.ApplySelectedProperties(
                request,
                new PresentationZoomPropertiesApplyRequest(
                    dialog.Properties,
                    dialog.ApplySummaryPropertiesToAllTiles,
                    dialog.SummaryTileProperties,
                    dialog.SummaryTileLayout));
        }
    }

    internal async void OpenZoomCoverImagePicker()
    {
        var request = _zoomAuthoringSession.BuildSelectedCoverTargetRequest();
        if (request is null)
            return;

        string? summarySectionId = null;
        if (request.RequiresSummaryTarget)
        {
            var targetDialog = new SummaryZoomCoverImageTargetDialog(request.SummaryTargetOptions);
            if (IsVisible)
                targetDialog.Owner = this;
            if (targetDialog.ShowDialog() != true)
                return;
            summarySectionId = targetDialog.SelectedTargetSectionId;
        }

        var result = await ImportPresentationAssetAsync(
            PresentationAssetImportKind.ZoomCoverImage,
            (bytes, contentType) => _zoomAuthoringSession.ApplySelectedCoverImage(
                request,
                summarySectionId,
                bytes,
                contentType));
        if (result.Status == PresentationAssetImportStatus.Failed)
        {
            await MaterializePresentationAssetImportResultAsync(
                result,
                PresentationAssetImportOutcomePolicy.ModalError);
        }
    }

    internal void RestoreZoomPreview()
    {
        var request = _zoomAuthoringSession.BuildSelectedCoverTargetRequest();
        if (request is null)
            return;

        string? summarySectionId = null;
        if (request.RequiresSummaryTarget)
        {
            var targetDialog = new SummaryZoomCoverImageTargetDialog(request.SummaryTargetOptions);
            if (IsVisible)
                targetDialog.Owner = this;
            if (targetDialog.ShowDialog() != true)
                return;
            summarySectionId = targetDialog.SelectedTargetSectionId;
        }

        _zoomAuthoringSession.RestoreSelectedPreview(request, summarySectionId);
    }

    // ── Find & Replace dialog (Wave 12B) ──────────────────────────────────────────

    /// <summary>The live Find/Replace dialog instance (modeless).  Null when closed.</summary>
    private FindReplaceDialog? _findReplaceDialog;
    internal FindReplaceDialog? ActiveFindReplaceDialog => _findReplaceDialog;

    /// <summary>
    /// Opens (or focuses) the Find dialog in Find-only mode (Ctrl+F).
    /// </summary>
    internal void OpenFindDialog()
    {
        if (_findReplaceDialog is null || !_findReplaceDialog.IsVisible)
        {
            _findReplaceDialog = new FindReplaceDialog(Editor, showReplace: false, RefreshCanvas);
            if (IsVisible) _findReplaceDialog.Owner = this;
            _findReplaceDialog.Closed += (_, _) => _findReplaceDialog = null;
            _findReplaceDialog.Show();
        }
        else
        {
            _findReplaceDialog.ShowReplaceMode(false);
            _findReplaceDialog.Activate();
        }
    }

    /// <summary>
    /// Opens (or focuses) the Find and Replace dialog in Replace mode (Ctrl+H).
    /// </summary>
    internal void OpenFindReplaceDialog()
    {
        if (_findReplaceDialog is null || !_findReplaceDialog.IsVisible)
        {
            _findReplaceDialog = new FindReplaceDialog(Editor, showReplace: true, RefreshCanvas);
            if (IsVisible) _findReplaceDialog.Owner = this;
            _findReplaceDialog.Closed += (_, _) => _findReplaceDialog = null;
            _findReplaceDialog.Show();
        }
        else
        {
            _findReplaceDialog.ShowReplaceMode(true);
            _findReplaceDialog.Activate();
        }
    }

    // ── Backstage ─────────────────────────────────────────────────────────────────

    private PresentationBackstageEndpoints BuildBackstageEndpoints() => new(
        GetPresentation: () => _presentation,
        GetDisplayName: () => _fileSession.DisplayName,
        GetIsDirty: () => _fileSession.IsDirty,
        GetCurrentPath: () => _fileSession.CurrentPath,
        GetRecentEntries: () => _fileSession.RecentEntries,
        GetCurrentOptions: () => _options,
        GetDataFolder: FreePApplicationFrameDescriptor.ResolveDataFolderLabel,
        OpenOptions: OpenOptions,
        New: () => FileNew(),
        Open: () => FileOpen(),
        OpenPath: path => FileOpenPath(path),
        RecoverUnsaved: () => _autosave.RecoverUnsavedPresentations(this),
        Save: () => FileSave(),
        SaveAs: () => FileSaveAs(),
        ExportPdf: () => FileExportPdf(),
        ExportNotesPagePdf: () => FileExportNotesPagePdf(),
        ExportImages: () => FileExportImages(),
        GetPrintPlan: _fileSession.BuildPrintBackstagePlan,
        Print: request => FilePrint(request),
        ExportVideo: () => _ = _fileSession.ExportVideoAsync(),
        CanExportVideo: () => _fileSession.CanExportVideo);

    private bool FileNew() => RunFileCommand(_fileSession.NewAsync());
    private bool FileOpen() => RunFileCommand(_fileSession.OpenAsync());
    private bool FileOpenPath(string path) => RunFileCommand(_fileSession.OpenPathAsync(path));
    private bool FileSave() => RunFileCommand(_fileSession.SaveAsync());
    private bool FileSaveAs() => RunFileCommand(_fileSession.SaveAsAsync());
    private bool FileExportPdf() => RunFileCommand(_fileSession.ExportPdfAsync());
    private bool FileExportNotesPagePdf() => RunFileCommand(_fileSession.ExportNotesPagePdfAsync());
    private bool FileExportImages() => RunFileCommand(_fileSession.ExportImagesAsync());
    private bool FilePrint(PresentationPrintRequest request) =>
        RunFileCommand(_fileSession.PrintAsync(request));

    private static bool RunFileCommand(Task<PresentationFileCommandResult> command) =>
        command.GetAwaiter().GetResult().Succeeded;

    private static bool RunOptionalFileCommand(Task<PresentationFileCommandResult?> command) =>
        command.GetAwaiter().GetResult()?.Succeeded == true;

    /// <summary>
    /// Opens a recovered snapshot in its own new window. <see cref="AutosaveCoordinator"/> calls
    /// this for every accepted candidate beyond the first (the first restores into the window that
    /// is already open), so each pending snapshot from a multi-window crash gets a window instead
    /// of being silently left on disk. <c>suppressStartupRecoveryOffer: true</c> stops the new
    /// window's own Loaded handler from re-prompting for the candidates the caller's loop is still
    /// working through.
    /// </summary>
    private bool OpenNewWindowWithRecoveredSnapshot(AutosaveRecoveryCandidate candidate)
    {
        var window = new MainWindow(
            _options,
            _optionsStore,
            _messageService,
            suppressStartupRecoveryOffer: true);
        var loaded = window._fileSession.RestoreAutosaveSnapshot(
            candidate.SnapshotPath,
            candidate.Sidecar.OriginalFilePath);
        window.Show();
        window.Activate();
        return loaded;
    }

    private void OpenAdditionalStartupPresentations(IReadOnlyList<StartupFileOpenEntry> entries)
    {
        foreach (var entry in entries)
        {
            var window = new MainWindow(_options, _optionsStore, _messageService);
            window.Show();
            var startupOpenSession = new PresentationStartupOpenSession(window._fileSession);
            RunFileCommand(startupOpenSession.OpenAsync(entry));
        }
    }

    private void ShowBackstage() => ShowBackstage("Info");

    private void ShowBackstage(string paneLabel) => _backstage.Show(paneLabel);

    private void ShowPrintBackstage()
    {
        RefreshPrintBackstagePlan();
        ShowBackstage("Print");
    }

    internal bool IsBackstageOpen => _backstage.IsOpen;

    internal string? CurrentBackstagePaneLabel => _backstage.EvidencePaneLabel;

    // ── Ribbon ────────────────────────────────────────────────────────────────────

    private UIElement BuildRibbon(RibbonDefinition definition, IRibbonCommandRegistry registry, IRibbonStateStore stateStore)
    {
        FreePRibbonIcons.Install();

        var result = RibbonShellBuilder.Build(new RibbonShellBuildSpec(
            definition,
            registry,
            stateStore,
            FileTabHeader:  UiText.Get("Ribbon_Group_File_Label"),
            FileTabAccent:  FreePBrushes.AccentColor,
            FileTabHover:   FreePBrushes.AccentDarkColor,
            ShowBackstage)
        {
            EnableContextualTabs = true,
            CustomizeTabContent = (tab, content) =>
            {
                if (tab.Id == "design")
                    InjectRibbonGallery(content, "themes", () => PresentationThemeGallery.Build(registry));
                else if (tab.Id == "transitions")
                    InjectRibbonGallery(content, "transition-gallery", () => PresentationTransitionGallery.Build(registry));
                else if (tab.Id == "animations")
                    InjectRibbonGallery(content, "animation-effects", () => PresentationAnimationGallery.Build(tab, registry, stateStore));
            },
        });

        _ribbonTabs    = result.Tabs;
        _fileTab       = result.FileTab;
        _fileTabRouter = result.FileTabRouter;
        _contextualTabs = result.ContextualTabs;
        return result.Root;
    }

    // The shared renderer stamps group content with its canonical catalog id. Replace selected WPF command
    // lanes with native Office-style previews while preserving underlying commands for non-WPF hosts and
    // contextual command routing.
    private static void InjectRibbonGallery(
        DependencyObject content,
        string groupId,
        Func<FrameworkElement> createGallery)
    {
        var panel = (content as Border)?.Child as Panel;
        if (panel is null)
            return;

        var group = panel.Children.OfType<RibbonGroupHost>()
            .FirstOrDefault(host => host.GroupContent is Grid grid && RibbonMetadata.GetCatalogId(grid) == groupId);
        if (group is null)
            return;

        // The shared adaptive host creates compact presentations lazily. A native gallery must be
        // recreated for each presentation because WPF elements cannot be reparented between group grids.
        var injectedLanes = new HashSet<Panel>();
        void InjectInto(FrameworkElement presentation)
        {
            if (presentation is not Grid grid)
                return;

            var lane = grid.Children.OfType<FrameworkElement>()
                .FirstOrDefault(child => Grid.GetRow(child) == 0) as Panel;
            if (lane is null || !injectedLanes.Add(lane))
                return;

            lane.Children.Clear();
            lane.Children.Add(createGallery());
        }

        InjectInto(group.GroupContent);
        group.LayoutUpdated += (_, _) =>
        {
            if (group.Content is FrameworkElement presentation)
                InjectInto(presentation);
        };
    }

    // FreeP's format and table authoring controls belong in contextual Office tabs, not in the
    // ordinary Home ribbon. Keep the tab-strip in sync with the model selection so a blank slide
    // stays focused on the PowerPoint-equivalent Home groups, while text shapes and tables expose
    // their dedicated surfaces as soon as they are selected.
    private void RefreshContextualTabs()
    {
        if (_contextualTabs is null || Editor.CurrentSlide is not { } slide)
            return;

        var state = RibbonContextState.None;
        foreach (var shapeId in Editor.SelectedShapeIds)
        {
            var shape = SlideShapeTraversal.FindById(slide, shapeId);
            if (shape?.Table is not null)
            {
                state = state.With("table");
                continue;
            }

            if (shape?.Kind == SlideShapeKind.SmartArt && shape.SmartArt is not null)
            {
                state = state.With("smartart");
                continue;
            }

            if (shape?.TextBody is not null)
                state = state.With("text");
        }

        _contextualTabs.Apply(state);
    }

    // Opens the modal FreeP Options editor. On OK it applies the edited settings live (by mutating the
    // shared _options instance the file-command session and Program read) and persists it through the shared
    // ApplicationOptionsStore so they survive a restart. Save is best-effort — a failure surfaces a
    // message but never throws.
    private void OpenOptions()
    {
        var dialog = new OptionsDialog(this, _options);
        if (dialog.ShowDialog() != true)
            return;

        var outcome = _optionsRuntime.ApplyAndPersist(
            dialog.Result,
            options => _optionsStore.Save(options),
            () => _optionsStore.Load());
        if (!outcome.Persisted)
            DialogMessageHelper.ShowError(this, _optionsStore.LastError, OptionsDialogPlanner.Title);
    }
}
