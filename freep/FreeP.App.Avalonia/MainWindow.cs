using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaRectangle = Avalonia.Controls.Shapes.Rectangle;
using Free.Shared.AppServices;
using Free.Shared.AppServices.Printing;
#if FREEP_WINDOWS_CAPTURE
using Free.Shared.AppServices.Windows;
#endif
using Free.Shared.Drawing;
using Free.Shared.IO;
using Free.Shared.Pdf.Skia;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;
using Free.Shared.Ribbon.KeyTips;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;
using Free.Shared.Theme;
using Free.Shared.Theme.Avalonia;
using FreeP.App.Avalonia.Backstage;
using FreeP.App.Avalonia.Printing;
using FreeP.App.Compositor;
using FreeP.App.Recording;
#if FREEP_WINDOWS_CAPTURE
using FreeP.App.Recording.Windows;
#endif
using FreeP.App.Rendering.Avalonia;
using FreeP.Core.IO;
using FreeP.Core.Model;
using System.Linq;

namespace FreeP.App.Avalonia;

/// <summary>
/// FreeP cross-platform main window. Viewer + navigator + file lifecycle (Wave 14B v1).
///
/// Layout:
///   ┌──────────────────────────────────────────┐
///   │  Ribbon (Home: File / Slides / Edit)     │
///   ├──────────────────────────────────────────┤
///   │  Body                                    │
///   │  ┌──────────┬───────────────────────────┐│
///   │  │ Slide    │  Stage (SlideCanvas)       ││
///   │  │ Pane     │                           ││
///   │  │ ~180 px  ├───────────────────────────┤│
///   │  │          │  Notes pane (TextBox)      ││
///   │  └──────────┴───────────────────────────┘│
///   ├──────────────────────────────────────────┤
///   │  Status bar ("Slide N / M")              │
///   └──────────────────────────────────────────┘
///
/// Commands wired (v1):
///   File:   New, Open, Save, Save As
///   Slide:  New Slide, Duplicate, Delete
///   Insert: Text Box, Table, Chart, Link, Picture, Rectangle, Ellipse
///   Edit:   Undo, Redo, Copy, Cut, Paste, Find, Replace
///   Keyboard: Ctrl+N/O/S/Shift+S, Ctrl+Z/Y
///
/// Deferred to later Avalonia parity: transitions, animations, and full platform dialogs.
/// </summary>
public sealed partial class MainWindow : Window,
    IPresentationAltTextPaneHostView,
    IPresentationReadingOrderPaneHostView
{
    partial void InitializeConditionalHost();
    partial void RecordStartupObservation(string stage);
    partial void RegisterStartupOpenedObservation();
    partial void CoordinateAnimationPaneRequestObserver();
    partial void NotifyHyperlinkAppliedObserver();
    partial void ConfigureSlideShowObserver(SlideShowWindow window);
    partial void OverrideCloseCancellation(ref bool cancel);
    partial void ObserveNativeOutputDetectionCompleted();
    partial void ResolveOpenPickerOverride(FileOpenPickerPlan plan, ref Task<string?>? selectionTask);
    partial void ResolveSavePickerOverride(FileSavePickerPlan plan, ref Task<string?>? selectionTask);
    partial void ResolveVideoPickerOverride(
        FileSavePickerPlan plan,
        ref Task<PresentationFilePickerResult>? resultTask);
    partial void ResolvePictureBulletPayloadOverride(ref Task<PresentationPictureBulletPayload?>? payloadTask);
    partial void ResolveHyperlinkDialogOverride(
        HyperlinkDialogRequest request,
        ref Task<Hyperlink?>? resultTask);

    private static readonly ProductThemeResourceProfile ThemeResources = ProductThemeResourceProfiles.FreeP;

    // Avalonia text metrics place the action row two pixels above WPF without this compensation.
    private const double ReadingOrderActionTopCompensation = 2;

    private static readonly SisterAppFileTextSpec FileText = PresentationFileTextResources.Presentation;

    // ── Presentation model ─────────────────────────────────────────────────────

    private readonly PresentationWorkareaSession _workareaSession;
    private Presentation _presentation => _workareaSession.Presentation;
    private readonly SisterAvaloniaFileCommandWorkflow _fileWorkflow;
    private readonly PresentationFileCommandSession _fileSession;
    private readonly SisterAvaloniaAsyncWindowCloseCoordinator _closeCoordinator;
    private readonly PresentationPlatformClipboardSession _clipboardService;
    private int _ownerFocusRestoreCount;
    private readonly PresentationClipboardOperationQueue _clipboardOperationQueue = new();
    private readonly FreePOptions _options;
    private readonly FreePOptionsRuntimeSession _optionsRuntime;
    private readonly IApplicationOptionsStore<FreePOptions> _optionsStore;
    private readonly IUserMessageService? _messageService;
    private LinuxNativeOutputCapabilities _nativeOutputCapabilities;
    private readonly IPlatformPrintService _printService;
    private readonly PortablePrintSubmissionWorkflow _portablePrintWorkflow;
    private readonly Func<Window, PrinterDiscoveryResult, PrintSelection?, CancellationToken, Task<PrintSelection?>>
        _showPrintSelectionDialog;
    private PrinterDiscoveryResult? _latestPrinterDiscovery;
    private string? _selectedPrinterName;
    private ILinuxVideoExportAdapter _videoExportAdapter;
    private readonly PresentationNativePrintHandoffHostCapabilities _nativePrintHostCapabilities;
    private PresentationVideoExportHandoffHostCapabilities _videoExportHostCapabilities;
    private readonly Func<LinuxNativeOutputCapabilities>? _nativeOutputCapabilityDetector;
    private readonly Func<PresentationPrintRequest?, PresentationPrintOutputPackage>? _printOutputPackageFactory;
    private readonly Func<PresentationVideoExportRequest?, PresentationVideoFramePackageArtifact>?
        _videoFramePackageArtifactFactory;
    private bool _nativeOutputDetectionStarted;
    private readonly PresentationVideoExportSession _videoExportSession;

    // ── Editing session ────────────────────────────────────────────────────────

    internal EditingSession Editor => _workareaSession.Editor;

    // ── UI elements ────────────────────────────────────────────────────────────

    private readonly SlideCanvas _slideCanvas;
    private Border _canvasHost = null!;
    private Canvas _oleOverlay = null!;
    private Canvas _commentOverlay = null!;
#if FREEP_WINDOWS_CAPTURE
    private AvaloniaOleInPlaceHost? _activeOleHost;
#endif
    private readonly ListBox _slidePaneList;
    private readonly Border _slidePaneInsertionIndicator;
    private readonly Button _slidePaneNewSlideButton;
    private readonly TextBox _notesBox;
    private readonly TextBlock _statusText;
    private readonly BackstageView _backstage;
    private Task<PrintSubmissionResult> _backstagePrintOperation =
        Task.FromResult(new PrintSubmissionResult(
            PrintSubmissionStatus.Failed,
            null,
            Message: PresentationShellTextCatalog.Resolve(
                PresentationShellTextCatalog.BackstagePrintNotRunStatus)));
    private CancellationTokenSource? _printCancellation;
    private readonly Border _titleBar;
    private IReadOnlyList<Button> _quickAccessButtons = [];
    private Border _layoutPickerHost = null!;
    private StackPanel _layoutPickerPanel = null!;
    private Border _tablePickerHost = null!;
    private UniformGrid _tablePickerPanel = null!;
    private SlideSizeDialog? _slideSizeDialog;
    private HeaderFooterDialog? _headerFooterDialog;
    private SlideShowSettingsDialog? _slideShowSettingsDialog;
    private Border _reviewCommentsPaneHost = null!;
    private ScrollViewer _reviewCommentsPaneScrollViewer = null!;
    private StackPanel _reviewCommentsPanePanel = null!;
    private readonly PresentationReviewWorkflowSession _reviewWorkflowSession;
    private readonly PresentationMainWindowReviewPaneCoordinator _reviewPaneHostCoordinator = null!;
    private readonly PresentationProofingPaneNativeViewAdapter<Control> _proofingPaneNativeView;
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
    private readonly PresentationMediaPaneHostViewAdapter _avaloniaMediaPaneHostView;
    private readonly PresentationMediaPaneHostCoordinator _mediaPaneHostCoordinator;
    private readonly PresentationSmartArtTextPaneSession _smartArtTextPaneSession;
    private readonly PresentationSmartArtTextPaneNativeViewAdapter<TextBox> _smartArtTextPaneNativeView;
    private readonly PresentationZoomAuthoringSession _zoomAuthoringSession;
    private readonly PresentationDomainContextMenuSession _domainContextMenuSession;
    private readonly PresentationNotesPaneSession _notesPaneSession;
    private readonly PresentationHyperlinkWorkflowSession _hyperlinkWorkflowSession;
    private readonly AnimationPaneSession _animationPaneSession;
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
    private WrapPanel _smartArtTextPaneCommandActions = null!;
    private bool _smartArtTextPaneRefreshing;
    private Border _animationPaneHost = null!;
    private TextBlock _animationPaneHeading = null!;
    private TextBlock _animationPaneMessage = null!;
    private StackPanel _animationPanePlaybackControlsPanel = null!;
    private StackPanel _animationPaneItemsPanel = null!;
    private Button _animationPanePreviewButton = null!;
    private readonly PresentationPaneAccessibilityAdapter _paneAccessibility = new();
    private readonly List<string> _animationPaneRenderedRows = new();
    private readonly List<string> _animationPaneRenderedPlaybackControls = new();
    private int _animationPaneEffectOptionControlCount;
    private int _animationPaneTriggerControlCount;
    private int _animationPaneDurationControlCount;
    private int _animationPaneDelayControlCount;
    private FindReplaceDialog? _findReplaceDialog;
    private Border _printOptionsPaneHost = null!;
    private TextBlock _printOptionsPaneHeading = null!;
    private TextBlock _printOptionsPaneMessage = null!;
    private StackPanel _printOptionsPaneRowsPanel = null!;
    private Button _printOptionsPaneExecuteButton = null!;
#if FREEP_WINDOWS_CAPTURE
    private ComboBox? _nativePrinterPicker;
#endif
    private TextBox? _printCustomRangeInput;
    private Button? _printCustomRangeApplyButton;
    private PresentationPrintRequest? _printOptionsPaneRequest;
    private readonly List<string> _printOptionsPaneRenderedOptionLines = new();
    private readonly List<string> _printOptionsPaneRenderedPreviewRows = new();
    private readonly List<string> _printOptionsPaneRenderedLayoutRows = new();
    private readonly List<string> _printOptionsPaneRenderedRangeRows = new();

    // ── Interaction layer (Theme 15) ────────────────────────────────────────────

    private SelectionAdornerLayer?       _adorner;
    private AvaloniaCanvasGestureHandler? _gestureHandler;
    private AvaloniaInCanvasTextEditor?  _textEditor;
    private Control? _ribbonControl;
    private RibbonDefinition? _ribbonDefinition;
    private RibbonCommandRegistry? _ribbonCommandRegistry;
    private readonly RibbonStateStore _ribbonStateStore = new();
    private FreePRibbonBindingSession? _ribbonBindingSession;
    private bool _ribbonKeyTipsVisible;
    private string? _ribbonKeyTipTabId;
    private string? _ribbonKeyTipGroupId;
    private string _ribbonKeyTipSequence = string.Empty;
    private IReadOnlyList<RibbonMenuItem>? _ribbonKeyTipMenuItems;
    private IReadOnlyList<MenuItem>? _ribbonKeyTipRenderedMenuItems;
    private FlyoutBase? _ribbonKeyTipFlyout;
    private PresentationViewShowState _viewShowState = PresentationViewShowState.Default;
    private PresentationViewZoomState _viewZoomState = PresentationViewZoomState.FitToWindow;

    private bool _notesRefreshing;
    private bool _slidePaneRefreshing;
    private bool _restoreSlidePaneFocusAfterRefresh;
    private sealed record SlidePaneSectionHeaderTag(string SectionId, int SectionIndex);

    internal int CurrentSlideIndex => Editor?.CurrentSlideIndex ?? -1;

    internal bool IsDirty => _fileWorkflow.IsDirty;

    internal string? CurrentPath => _fileWorkflow.CurrentPath;

    internal IReadOnlyList<RecentFileEntry> RecentEntries => _fileWorkflow.RecentEntries;

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
    internal FindReplaceWorkflowPlan? LastFindReplaceWorkflowPlan { get; private set; }
    internal PresentationDesignCommandPlan? LastCustomSlideSizeRequestPlan { get; private set; }
    internal SlideSizeDialogInitialState? LastCustomSlideSizeInitialState { get; private set; }
    internal SlideSizeDialogResultPlan? LastCustomSlideSizeResultPlan { get; private set; }
    internal HeaderFooterCommandFocus? LastHeaderFooterFocus { get; private set; }
    internal HeaderFooterState? LastHeaderFooterState { get; private set; }
    internal HeaderFooterApplyPlan? LastHeaderFooterApplyPlan { get; private set; }
    internal HyperlinkDialogRequest? LastHyperlinkDialogRequest { get; private set; }
    internal HyperlinkDialogApplyPlan? LastHyperlinkDialogApplyPlan { get; private set; }
    internal PresentationDesignCommandPlan? LastLayoutRequestPlan { get; private set; }
    internal PresentationHandoutLayoutPlan? LastHandoutLayoutPlan { get; private set; }
    internal PresentationNotesPagePreviewPlan? LastNotesPagePreviewPlan { get; private set; }
    internal PresentationNotesPagePdfRenderPlan? LastNotesPagePdfRenderPlan { get; private set; }
    internal PresentationPrintOutputPackage? LastPrintOutputPackage { get; private set; }
    internal PresentationPrintBackstagePlan? LastPrintBackstagePlan { get; private set; }
    internal PresentationNativePrintHandoffPlan? LastNativePrintHandoffPlan { get; private set; }
    internal PresentationPrintOutputPackageExecutionDescriptor? LastPrintExecutionDescriptor { get; private set; }
    internal PresentationVideoExportPlan? LastVideoExportPlan { get; private set; }
    internal PresentationVideoFramePackage? LastVideoFramePackage { get; private set; }
    internal PresentationVideoExportHandoffPlan? LastVideoExportHandoffPlan { get; private set; }
    internal PresentationVideoFramePackageExecutionDescriptor? LastVideoExecutionDescriptor { get; private set; }
    internal PrintSubmissionResult? LastPrintSubmissionResult { get; private set; }
    private PrintSelection? _lastPrintSelection;
    internal LinuxVideoExportResult? LastVideoExportResult { get; private set; }
    internal PresentationLayoutPickerPlan? LastLayoutPickerPlan { get; private set; }
    internal PresentationLayoutChoice? LastAppliedLayoutChoice { get; private set; }
    internal TableInsertionPickerPlan? LastTablePickerPlan { get; private set; }
    internal bool IsSmartArtTextPaneVisible =>
        _workareaSession.Panes.IsVisible(PresentationWorkareaPane.SmartArtText);
    internal bool IsAccessibilityCheckerPaneVisible =>
        _workareaSession.Panes.IsVisible(PresentationWorkareaPane.AccessibilityChecker);
    internal bool IsProofingPaneVisible =>
        _workareaSession.Panes.IsVisible(PresentationWorkareaPane.Proofing);
    internal bool IsMediaCaptionPaneVisible =>
        _workareaSession.Panes.IsVisible(PresentationWorkareaPane.MediaCaption);
    internal bool IsAnimationPaneVisible => _animationPaneHost?.IsVisible == true;

    // ── Constructors ───────────────────────────────────────────────────────────

    public MainWindow()
        : this(Array.Empty<string>())
    {
    }

    public MainWindow(IReadOnlyList<string> startupArguments)
        : this(startupArguments, loadRecentFilesStore: null)
    {
    }

    internal MainWindow(
        IReadOnlyList<string> startupArguments,
        Func<RecentFilesStore>? loadRecentFilesStore,
        FreePOptions? options = null,
        Func<string, Task<SaveChangesPrompt>>? promptSaveChangesAsync = null,
        Func<string, Exception, Task>? showFileCommandErrorAsync = null,
        IPlatformClipboard? systemClipboard = null,
        IPresentationClipboardShapeRenderer? clipboardRenderer = null,
        LinuxNativeOutputCapabilities? nativeOutputCapabilities = null,
        ILinuxVideoExportAdapter? videoExportAdapter = null,
        Func<LinuxNativeOutputCapabilities>? nativeOutputCapabilityDetector = null,
        Func<PresentationPrintRequest?, PresentationPrintOutputPackage>? printOutputPackageFactory = null,
        Func<PresentationVideoExportRequest?, PresentationVideoFramePackageArtifact>?
            videoFramePackageArtifactFactory = null,
        IPlatformPrintService? printService = null,
        Func<Window, PrinterDiscoveryResult, PrintSelection?, CancellationToken, Task<PrintSelection?>>?
            showPrintSelectionDialog = null,
        IApplicationOptionsStore<FreePOptions>? optionsStore = null,
        IUserMessageService? messageService = null)
    {
        InitializeConditionalHost();
        Title = FreePApplicationFrameDescriptor.Title.ApplicationName;
        Width = 1280;
        Height = 760;
        MinWidth = 800;
        MinHeight = 500;
        Background = FreePBrushes.SheetSurface;
        ApplyWindowIcon();
        _options = options ?? new FreePOptions();
        _optionsRuntime = new FreePOptionsRuntimeSession(_options);
        _optionsStore = optionsStore ?? new InMemoryApplicationOptionsStore<FreePOptions>(_options);
        _messageService = messageService;
        _nativeOutputCapabilities = nativeOutputCapabilities ??
            LinuxNativeOutputCapabilities.Unavailable(PresentationShellTextCatalog.Resolve(
                PresentationShellTextCatalog.NativeOutputDetectionPendingStatus));
        _printService = printService ?? CreatePlatformPrintService();
        _portablePrintWorkflow = new PortablePrintSubmissionWorkflow(_printService);
        _showPrintSelectionDialog = showPrintSelectionDialog ??
            ShowPlatformPrintSelectionDialogAsync;
        _videoExportAdapter = videoExportAdapter ?? CreateVideoExportAdapter(_nativeOutputCapabilities.Video);
        _videoExportSession = new PresentationVideoExportSession(() => _videoExportAdapter);
        _nativePrintHostCapabilities = BuildNativePrintHostCapabilities(_printService);
        _videoExportHostCapabilities = BuildVideoExportHostCapabilities(_nativeOutputCapabilities.Video);
        _nativeOutputCapabilityDetector = nativeOutputCapabilityDetector ??
            (nativeOutputCapabilities is null ? DetectNativeOutputCapabilities : null);
        _printOutputPackageFactory = printOutputPackageFactory;
        _videoFramePackageArtifactFactory = videoFramePackageArtifactFactory;
        var resolvedClipboardRenderer = clipboardRenderer ?? new AvaloniaClipboardShapeRenderer();
        _clipboardService = new PresentationPlatformClipboardSession(
            systemClipboard ?? new AvaloniaPlatformClipboard(
                () => TopLevel.GetTopLevel(this)?.Clipboard),
            resolvedClipboardRenderer.RenderSelection,
            static content => PresentationClipboardPlatformMapper.ToPlatformContent(
                content,
                PresentationClipboardPlatformMapper.ResolveNativeScope(),
                PresentationClipboardPlatformMapper.ResolveNativeXamlPackageFormat(),
                PresentationClipboardPlatformMapper.ResolveNativeRtfFormat()),
            PresentationClipboardPlatformIdentityStrategy.ContentIdentity(
                AvaloniaClipboardShapeRenderer.NormalizePng));

        _workareaSession = new PresentationWorkareaSession(CreateWorkareaEndpoint());
        // ── Core UI elements ──────────────────────────────────────────────────

        _slideCanvas = new SlideCanvas
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Stretch,
        };

        _slidePaneList = new ListBox
        {
            Width       = FreePShellVisualMetrics.SlidePaneWidth,
            MaxHeight   = 520,
            Padding     = new Thickness(4),
            Background  = BrushFromHex(SlidePanePlanner.DefaultPaneBackgroundHex),
            SelectionMode = SelectionMode.Multiple,
        };
        _slidePaneList.SelectionChanged += OnSlidePaneSelectionChanged;

        _slidePaneInsertionIndicator = new Border
        {
            Height              = SlidePanePlanner.DefaultDropIndicatorThickness,
            Background          = new SolidColorBrush(Color.Parse(SlidePanePlanner.DefaultDropIndicatorAccentHex)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Top,
            IsHitTestVisible    = false,
            IsVisible           = false,
        };
        _slidePaneNewSlideButton = BuildSlidePaneNewSlideButton();

        _notesBox = new TextBox
        {
            AcceptsReturn   = true,
            TextWrapping    = TextWrapping.Wrap,
            PlaceholderText = PresentationPaneTextResources.NotesPlaceholder,
            MinHeight       = FreePShellVisualMetrics.NotesPaneHeight,
            MaxHeight       = 120,
            Padding         = new Thickness(8, 4),
            FontSize        = 12,
            Background      = FreePBrushes.NotesHintSurface,
            BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush     = FreePBrushes.PaneBorder,
        };
        _notesBox.TextChanged += OnNotesTextChanged;
        _notesBox.KeyDown += OnNotesKeyDown;

        _statusText = SisterAppStatusBarChrome.CreateInfoText(
            foreground: AvaloniaThemeResourceResolver.ResolveOr<IBrush>(ThemeResources.WhiteBrush, Brushes.White),
            margin: new Thickness(12, 0, 0, 0));
        _fileWorkflow = new SisterAvaloniaFileCommandWorkflow(
            owner: this,
            titleSpec: new SisterAvaloniaFileTitleSpec(
                ApplicationName: FreePApplicationFrameDescriptor.Title.ApplicationName,
                Separator: FreePApplicationFrameDescriptor.Title.Separator,
                DirtyMarker: FreePApplicationFrameDescriptor.Title.DirtyMarker,
                ApplicationPlacement: FreePApplicationFrameDescriptor.Title.ApplicationPlacement),
            maxRecentEntries: () => _options.RecentFilesCap,
            onChanged: OnFileWorkflowChanged,
            loadRecentFilesStore: loadRecentFilesStore,
            saveAsync: FileSaveAsync,
            promptSaveChangesAsync: promptSaveChangesAsync,
            showFileCommandErrorAsync: showFileCommandErrorAsync,
            restoreOwnerFocus: RestoreOwnerFocus);
        _fileSession = new PresentationFileCommandSession(
            () => _presentation,
            LoadPresentationContent,
            new PresentationFileLifecycleAdapter(
                _fileWorkflow.Workflow,
                (action, load) => _fileWorkflow.NewAsync(action, load),
                _fileWorkflow.OpenAsync,
                _fileWorkflow.ConfirmCloseAllowedAsync),
            new AvaloniaPresentationFilePickerPort(this),
            new AvaloniaPresentationFileRenderPort(),
            new AvaloniaPresentationPrintPort(this),
            new AvaloniaPresentationVideoPort(this, _videoExportSession),
            new AvaloniaPresentationFileFeedbackPort(this),
            getImageExportRange: () => PresentationExportPlanner.BuildCurrentSlideRangeRequest(Editor.CurrentSlideIndex),
            getPrintCurrentSlideNumber: () => Editor.CurrentSlideIndex + 1,
            printPackageFactory: _printOutputPackageFactory,
            videoPackageArtifactFactory: _videoFramePackageArtifactFactory);
        RecordStartupObservation("file-workflow-created");
        _closeCoordinator = new SisterAvaloniaAsyncWindowCloseCoordinator(
            confirmCloseAllowedAsync: () => _fileSession.ConfirmCloseAllowedAsync(),
            requestClose: Close,
            restoreOwnerFocus: RestoreOwnerFocus);

        _readingOrderPaneHostCoordinator = new(_workareaSession.Panes, this);
        _proofingPaneNativeView = new(
            new PresentationProofingPaneNativeViewBindings<Control>(
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
        _reviewWorkflowSession = new(
            () => Editor,
            new PresentationReviewWorkflowSessionCallbacks(
                MarkDirty: () => _fileWorkflow.MarkDirty(),
                RefreshCanvas: RefreshCanvas,
                RefreshNotesPane: RefreshNotesPane,
                RenderAccessibilityCheckerPaneIfVisible: plan =>
                    _reviewPaneHostCoordinator.RenderAccessibilityPaneIfVisible(plan),
                PresentAccessibilityCheckerPane: plan =>
                    _reviewPaneHostCoordinator.PresentAccessibilityPane(plan),
                OpenAltTextPane: () => ShowAltTextPane(),
                OpenHyperlinkDialog: () => OpenHyperlinkDialog(),
                OpenMediaCaptionPane: () => MediaPaneHost.Show(),
                RenderCommentPane: ShowReviewCommentsPane,
                RenderAltTextPaneIfVisible: RenderAltTextPaneIfVisible,
                RenderReadingOrderPaneIfVisible: plan => _readingOrderPaneHostCoordinator.RenderIfVisible(plan),
                PresentReadingOrderPane: plan => _readingOrderPaneHostCoordinator.Present(plan),
                RenderProofingPaneIfVisible: plan =>
                    _reviewPaneHostCoordinator.RenderProofingPaneIfVisible(plan),
                PresentProofingPane: plan => _reviewPaneHostCoordinator.PresentProofingPane(plan),
                UpdateAfterCommentMutation: UpdateStatus,
                UpdateAfterCommentNavigation: UpdateStatus,
                UpdateAfterProofingCorrection: UpdateStatus));
        _reviewPaneHostCoordinator = new(
            _reviewWorkflowSession,
            _workareaSession.Panes,
            new DelegatingPresentationMainWindowReviewPaneView(
                new PresentationMainWindowReviewPaneViewBindings(
                    IsAccessibilityPaneVisible: () => IsAccessibilityCheckerPaneVisible,
                    IsProofingPaneVisible: () => IsProofingPaneVisible,
                    SetAccessibilityPaneVisible: visible => _accessibilityCheckerPaneHost.IsVisible = visible,
                    SetProofingPaneVisible: visible => _proofingPaneHost.IsVisible = visible,
                    RenderAccessibilityPane: RenderAccessibilityCheckerPane,
                    RenderProofingPane: RenderProofingPane,
                    RefreshPaneAccessibilityMetadata: RefreshPaneAccessibilityMetadata)));
        _altTextPaneHostCoordinator = new(
            _reviewWorkflowSession,
            _workareaSession.Panes,
            this);
        _avaloniaMediaPaneHostView = BuildAvaloniaMediaPaneHostView();
        _mediaPaneHostCoordinator = new(
            new PresentationMediaPaneSession(
                () => Editor,
                new PresentationMediaPaneSessionCallbacks(
                    MarkDirty: () => _fileWorkflow.MarkDirty(),
                    RefreshReviewWorkflowPlans: RefreshReviewWorkflowPlans,
                    UpdateHost: UpdateStatus)),
            _workareaSession.Panes,
            _avaloniaMediaPaneHostView);
        _smartArtTextPaneSession = new(
            () => Editor,
            new PresentationSmartArtTextPaneSessionCallbacks(
                MarkDirty: () => _fileWorkflow.MarkDirty(),
                RefreshCanvas: RefreshCanvas,
                UpdateHost: UpdateStatus,
                RenderPane: RenderSmartArtTextPane));
        _zoomAuthoringSession = new(
            () => Editor,
            new PresentationZoomAuthoringSessionCallbacks(
                MarkDirty: () => _fileWorkflow.MarkDirty(),
                RefreshCanvas: RefreshCanvas,
                UpdateHost: UpdateStatus,
                RenderSlidePreview: (presentation, slideIndex, widthPx, heightPx) =>
                    SlideRenderer.RenderToBytes(
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
        _animationPaneSession = new(() => Editor);
        _customShowSession = new(() => Editor);


        // ── Root layout ───────────────────────────────────────────────────────

        var ribbon = BuildRibbon();
        var statusBar = SisterAppStatusBarChrome.Build(new SisterAppStatusBarSpec(
            Background: AvaloniaThemeResourceResolver.ResolveOr<IBrush>(
                ThemeResources.StatusSurfaceBrush,
                FreePBrushes.Accent),
            LeftContent: _statusText)).Root;
        var frame = SisterAppClientFrameBuilder.Build(SisterAppClientFrameSpec.ForWorkArea(
            chrome: ribbon,
            workArea: BuildBody(),
            statusBar: statusBar));
        BindMediaPaneEvents();
        _backstage = new BackstageView(BuildBackstageEndpoints());
        var clientRoot = new Grid();
        clientRoot.Children.Add(frame.Root);
        clientRoot.Children.Add(_backstage);

        var windowFrame = SisterAppWindowFrameBuilder.Build(new SisterAppWindowFrameSpec(
            Window: this,
            Body: clientRoot,
            TitleBarBackground: AvaloniaThemeResourceResolver.ResolveOr<IBrush>(
                ThemeResources.TitleBarBrush,
                FreePBrushes.Accent),
            TitleBarForeground: AvaloniaThemeResourceResolver.ResolveOr<IBrush>(ThemeResources.WhiteBrush, Brushes.White),
            TitleBarHeight: FreePShellVisualMetrics.TitleBarHeight));
        _titleBar = windowFrame.TitleBar;
        _quickAccessButtons = SisterQuickAccessToolbarBuilder.Render(
            windowFrame.QatHost,
            new SisterQuickAccessToolbarActions(
                Save: () => _workareaSession.ExecuteCommand(FreePKeyboardCommand.SavePresentation),
                Undo: () => _workareaSession.ExecuteCommand(FreePKeyboardCommand.Undo),
                Redo: () => _workareaSession.ExecuteCommand(FreePKeyboardCommand.Redo)),
            AvaloniaThemeResourceResolver.ResolveOr<IBrush>(ThemeResources.WhiteBrush, Brushes.White));

        // ── Keyboard shortcuts ────────────────────────────────────────────────

        // A focused ribbon or slide-pane button can consume Ctrl+Z/Ctrl+Y before
        // the shell sees it. Tunnel only those two shell commands; leave the
        // normal bubble route in place so inline text editors retain ownership
        // of Ctrl+C/V/X/Z/Y and the rest of the shortcut catalog.
        AddHandler(
            InputElement.KeyDownEvent,
            (_, e) =>
            {
                if (ShouldTunnelShellUndoRedo(e))
                    MainWindow_KeyDown(this, e);
            },
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        KeyDown += MainWindow_KeyDown;
        AddHandler(
            InputElement.KeyDownEvent,
            (_, e) =>
            {
                if (!_backstage.IsOpen || e.Key != Key.Escape)
                    return;

                HideBackstageAndRestoreFocus();
                e.Handled = true;
            },
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AddHandler(
            InputElement.PointerPressedEvent,
            (_, _) => SetRibbonKeyTipsVisible(false),
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        Deactivated += (_, _) => SetRibbonKeyTipsVisible(false);
        Closing += (_, e) =>
        {
            var cancel = _closeCoordinator.ShouldCancelClosing();
            OverrideCloseCancellation(ref cancel);
            e.Cancel = cancel;
        };
        Closed += (_, _) =>
        {
            _workareaSession.Dispose();
            CloseActiveOleHost();
            _findReplaceDialog?.Close();
            _slideSizeDialog?.Close(false);
            _headerFooterDialog?.Close(false);
            _slideShowSettingsDialog?.Close(false);
        };

        // ── Initial content ───────────────────────────────────────────────────

        var startupOpenSession = new PresentationStartupOpenSession(_fileSession);
        var startupOpenPlan = startupOpenSession.Plan(startupArguments);
        var primaryStartupEntry = startupOpenPlan.Entries.FirstOrDefault(entry => !entry.OpenInNewWindow);
        RecordStartupObservation(
            primaryStartupEntry is null && !startupOpenPlan.ShouldReportMissingPath
                ? "startup-load-not-requested"
                : "startup-load-begin");

        PresentationFileCommandResult? startupOpenResult;
        if (primaryStartupEntry is not null)
        {
            startupOpenResult = startupOpenSession
                .OpenAsync(primaryStartupEntry, reportFeedback: false)
                .GetAwaiter()
                .GetResult();
            if (startupOpenResult.Succeeded)
                RecordStartupObservation("startup-load-saved");
            else
            {
                LoadPresentationAsSaved(_presentation, path: null);
                RecordStartupObservation("startup-load-failed-fallback-saved");
            }
        }
        else
        {
            startupOpenResult = startupOpenSession
                .ReportFirstUnopenableAsync(startupOpenPlan, reportFeedback: false)
                .GetAwaiter()
                .GetResult();
            LoadPresentationAsSaved(_presentation, path: null);
            RecordStartupObservation(startupOpenResult is null
                ? "startup-empty-saved"
                : "startup-load-failed-fallback-saved");
        }

        var additionalStartupEntries = startupOpenPlan.Entries
            .Where(entry => entry.OpenInNewWindow)
            .ToArray();

        RecordStartupObservation("startup-seeds-complete");

        Content = windowFrame.Root;
        RecordStartupObservation("content-assigned");
        UpdateStatus();
        RecordStartupObservation("constructor-complete");
        RegisterStartupOpenedObservation();
        if (startupOpenResult is not null || additionalStartupEntries.Length > 0)
        {
            Opened += async (_, _) =>
            {
                if (startupOpenResult is not null)
                    await startupOpenSession.ReportFeedbackAsync(startupOpenResult);
                await OpenAdditionalStartupPresentationsAsync(additionalStartupEntries);
            };
        }

        if (_nativeOutputCapabilityDetector is not null)
            Opened += (_, _) => StartNativeOutputCapabilityDetection();
    }

    private void RestoreOwnerFocus()
    {
        _ownerFocusRestoreCount++;
        Activate();
        Focus();
    }

    private async Task OpenAdditionalStartupPresentationsAsync(
        IReadOnlyList<StartupFileOpenEntry> entries)
    {
        foreach (var entry in entries)
        {
            var window = new MainWindow(
                [],
                loadRecentFilesStore: null,
                options: _options,
                optionsStore: _optionsStore);
            window.Show();
            var startupOpenSession = new PresentationStartupOpenSession(window._fileSession);
            await startupOpenSession.OpenAsync(entry);
        }
    }

    private void ApplyWindowIcon() =>
        AvaloniaWindowIconLoader.TryApply(this, "FreeP.ico");

    private void StartNativeOutputCapabilityDetection()
    {
        if (_nativeOutputDetectionStarted || _nativeOutputCapabilityDetector is null)
            return;

        _nativeOutputDetectionStarted = true;
        _ = Task.Run(_nativeOutputCapabilityDetector).ContinueWith(
            task =>
            {
                if (task.IsCanceled || task.IsFaulted)
                {
                    Dispatcher.UIThread.Post(() => ObserveNativeOutputDetectionCompleted());
                    return;
                }

                Dispatcher.UIThread.Post(() =>
                {
                    _nativeOutputCapabilities = task.Result;
                    _videoExportAdapter = CreateVideoExportAdapter(_nativeOutputCapabilities.Video);
                    _videoExportHostCapabilities = BuildVideoExportHostCapabilities(_nativeOutputCapabilities.Video);
                    ObserveNativeOutputDetectionCompleted();
                    if (_printOptionsPaneHost?.IsVisible == true)
                        RenderPrintOptionsPane(RefreshPrintBackstagePlan(_printOptionsPaneRequest));
                });
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }

    // ── Editor construction ────────────────────────────────────────────────────

    // ── Body layout ────────────────────────────────────────────────────────────

    private Control BuildBody()
    {
        // Right: canvas (fills) + notes pane (auto height) stacked in a Grid.
        var rightGrid = new Grid();
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ── Interaction overlay stack ───────────────────────────────────────────
        // A Panel stack: SlideCanvas at the bottom, the text-edit overlay above it,
        // and the non-interactive selection adorner at the top, matching WPF's
        // AdornerDecorator z-order.
        _adorner = new SelectionAdornerLayer
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Stretch,
            IsHitTestVisible    = false,
            Margin              = new Thickness(FreePShellVisualMetrics.CanvasMargin),
        };

        // Text-overlay: a Canvas that hosts TextBox children during text editing.
        var textOverlay = new Canvas
        {
            IsVisible        = false,
            IsHitTestVisible = false,
        };

        _commentOverlay = new Canvas
        {
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        _commentOverlay.SizeChanged += (_, _) =>
            RenderCommentMarkers(LastCommentPanePlan?.Comments ?? []);

        // Match WPF's stage geometry: the canvas uses the 40-DIP canvas margin,
        // while the text editor overlay spans the full stage.
        // Text editor placements are planned in canvas coordinates, so applying the
        // margin to that overlay would shift the native WPF-equivalent viewport.
        var canvasContent = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Stretch,
            Margin              = new Thickness(FreePShellVisualMetrics.CanvasMargin),
        };

        canvasContent.Children.Add(_slideCanvas);
        _oleOverlay = new Canvas
        {
            IsHitTestVisible = false,
        };
        canvasContent.Children.Add(_oleOverlay);

        var canvasStack = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Stretch,
        };
        canvasStack.Children.Add(canvasContent);
        canvasStack.Children.Add(textOverlay);
        canvasStack.Children.Add(_commentOverlay);
        canvasStack.Children.Add(_adorner);

        _canvasHost = new Border
        {
            Background = FreePBrushes.PlaceholderSurface,
            ClipToBounds = true,
            Child      = canvasStack,
        };
        _layoutPickerPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
        };
        _layoutPickerHost = new Border
        {
            Background      = FreePBrushes.White,
            BorderBrush     = FreePBrushes.PaneBorder,
            BorderThickness = new Thickness(0, 1, 0, 0),
            MaxHeight       = 220,
            IsVisible       = false,
            Child           = new ScrollViewer
            {
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content                       = _layoutPickerPanel,
            },
        };
        _tablePickerPanel = new UniformGrid
        {
            Rows = TableInsertionPickerPlanner.DefaultMaxRows,
            Columns = TableInsertionPickerPlanner.DefaultMaxColumns,
            Margin = new Thickness(8),
        };
        _tablePickerHost = new Border
        {
            Background      = FreePBrushes.White,
            BorderBrush     = FreePBrushes.PaneBorder,
            BorderThickness = new Thickness(0, 1, 0, 0),
            IsVisible       = false,
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
                        FontWeight = FontWeight.SemiBold,
                        Foreground = FreePBrushes.PaneText,
                    },
                    _tablePickerPanel,
                },
            },
        };
        _reviewCommentsPanePanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing     = 0,
        };
        _reviewCommentsPaneHost = new Border
        {
            Background      = FreePBrushes.NotesSurface,
            BorderBrush     = FreePBrushes.DisabledBorder,
            BorderThickness = new Thickness(0, 1, 0, 0),
            MaxHeight       = 100,
            IsVisible       = false,
            Child           = _reviewCommentsPaneScrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content                       = _reviewCommentsPanePanel,
            },
        };
        _altTextPaneHost = BuildAltTextPaneHost();
        _accessibilityCheckerPaneHost = BuildAccessibilityCheckerPaneHost();
        _readingOrderPaneHost = BuildReadingOrderPaneHost();
        _selectionPane = new SelectionPane(Editor, RefreshPaneAccessibilityMetadata);
        _selectionPane.Refresh();
        _proofingPaneHost = BuildProofingPaneHost();
        _mediaCaptionPaneHost = BuildMediaCaptionPaneHost();
        _smartArtTextPaneHost = BuildSmartArtTextPaneHost();
        _animationPaneHost = BuildAnimationPaneHost();
        _printOptionsPaneHost = BuildPrintOptionsPaneHost();
        Grid.SetRow(_canvasHost, 0);
        Grid.SetRow(_layoutPickerHost, 1);
        Grid.SetRow(_tablePickerHost, 2);
        Grid.SetRow(_reviewCommentsPaneHost, 3);
        Grid.SetRow(_notesBox,  4);
        rightGrid.Children.Add(_canvasHost);
        rightGrid.Children.Add(_layoutPickerHost);
        rightGrid.Children.Add(_tablePickerHost);
        rightGrid.Children.Add(_reviewCommentsPaneHost);
        rightGrid.Children.Add(_notesBox);

        // Wire interaction after the overlay panel is built.
        WireInteraction(textOverlay);

        var slidePaneHost = new Grid
        {
            Width = FreePShellVisualMetrics.SlidePaneWidth,
            Background = BrushFromHex(SlidePanePlanner.DefaultPaneBackgroundHex),
        };
        slidePaneHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        slidePaneHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var slidePaneListHost = new Grid();
        slidePaneListHost.Children.Add(_slidePaneList);
        slidePaneListHost.Children.Add(_slidePaneInsertionIndicator);

        Grid.SetRow(slidePaneListHost, 0);
        Grid.SetRow(_slidePaneNewSlideButton, 1);
        slidePaneHost.Children.Add(slidePaneListHost);
        slidePaneHost.Children.Add(_slidePaneNewSlideButton);

        // Left (slide pane) + right split.
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(slidePaneHost, 0);
        Grid.SetColumn(rightGrid,      1);
        Grid.SetColumn(_accessibilityCheckerPaneHost, 2);
        Grid.SetColumn(_altTextPaneHost, 3);
        Grid.SetColumn(_readingOrderPaneHost, 4);
        Grid.SetColumn(_proofingPaneHost, 5);
        Grid.SetColumn(_mediaCaptionPaneHost, 6);
        Grid.SetColumn(_smartArtTextPaneHost, 7);
        Grid.SetColumn(_selectionPane, 8);
        Grid.SetColumn(_animationPaneHost, 9);
        Grid.SetColumn(_printOptionsPaneHost, 10);
        body.Children.Add(slidePaneHost);
        body.Children.Add(rightGrid);
        body.Children.Add(_accessibilityCheckerPaneHost);
        body.Children.Add(_altTextPaneHost);
        body.Children.Add(_readingOrderPaneHost);
        body.Children.Add(_selectionPane);
        body.Children.Add(_proofingPaneHost);
        body.Children.Add(_mediaCaptionPaneHost);
        body.Children.Add(_smartArtTextPaneHost);
        body.Children.Add(_animationPaneHost);
        body.Children.Add(_printOptionsPaneHost);

        RefreshPaneAccessibilityMetadata();

        return body;
    }

    private void RefreshPaneAccessibilityMetadata()
    {
        if (_slidePaneList is null || _notesBox is null || _reviewCommentsPaneHost is null
            || _accessibilityCheckerPaneHost is null || _altTextPaneHost is null
            || _readingOrderPaneHost is null || _proofingPaneHost is null
            || _mediaCaptionPaneHost is null || _smartArtTextPaneHost is null
            || _selectionPane is null || _animationPaneHost is null)
            return;

        var smartArtItemCount = _smartArtTextPaneRowsPanel?.Children.Count ?? 0;
        var selectionPlan = _selectionPane.CurrentPlan;
        var animationPlan = _animationPaneSession.Refresh();
        var selectedSmartArtRow = _smartArtTextPaneRowsPanel?.Children
            .OfType<TextBox>()
            .FirstOrDefault(box =>
                box.Tag is SmartArtNodeOutlineItem item &&
                StringComparer.Ordinal.Equals(item.ModelId, _smartArtTextPaneSession.SelectedModelId));
        var selectedSmartArtIndex = selectedSmartArtRow is null || _smartArtTextPaneRowsPanel is null
            ? -1
            : _smartArtTextPaneRowsPanel.Children.IndexOf(selectedSmartArtRow);
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
                _animationPaneHost.IsVisible,
                animationPlan?.Items.Count ?? _animationPaneItemsPanel?.Children.Count ?? 0,
                animationPlan?.SelectedIndex ?? -1));
        Control[] controls =
        [
            _slidePaneList, _notesBox, _reviewCommentsPaneHost, _accessibilityCheckerPaneHost,
            _altTextPaneHost, _readingOrderPaneHost, _proofingPaneHost, _mediaCaptionPaneHost,
            _smartArtTextPaneHost, _selectionPane, _animationPaneHost,
        ];

        for (var index = 0; index < states.Count; index++)
        {
            var state = states[index];
            _paneAccessibility.ApplyPane(
                controls[index], state.PaneId, state.IsVisible, state.ItemCount, state.SelectedIndex);
        }
    }

    private Border BuildPrintOptionsPaneHost()
    {
        _printOptionsPaneHeading = new TextBlock
        {
            Text = PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.PrintSurfacePrintHeading),
            FontSize = 26,
            FontWeight = FontWeight.Light,
            Foreground = FreePBrushes.PaneHeadingText,
            Margin = new Thickness(0, 0, 0, 18),
        };
        _printOptionsPaneMessage = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70)),
            Margin = new Thickness(0, 0, 0, 16),
        };
        _printOptionsPaneRowsPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
        };
        _printOptionsPaneExecuteButton = new Button
        {
            Content = PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.PrintSurfacePrintHeading),
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 88,
            Margin = new Thickness(0, 16, 0, 0),
            IsEnabled = false,
        };
        _printOptionsPaneExecuteButton.Click += async (_, _) =>
            await ExecutePrintWorkflowAsync(_printOptionsPaneRequest);

        var content = new StackPanel
        {
            Orientation = Orientation.Vertical,
            MaxWidth = 760,
            HorizontalAlignment = HorizontalAlignment.Left,
            Children =
            {
                _printOptionsPaneHeading,
                _printOptionsPaneMessage,
                _printOptionsPaneRowsPanel,
                _printOptionsPaneExecuteButton,
            },
        };

        return new Border
        {
            Width = 1010,
            IsVisible = false,
            Background = FreePBrushes.White,
            BorderThickness = new Thickness(0),
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = content,
            },
        };
    }

    private Border BuildAltTextPaneHost()
    {
        _altTextPaneHeading = new TextBlock
        {
            Text = PresentationPaneTextResources.AltTextHeading,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
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
            Padding = new Thickness(10, 4),
            Margin = new Thickness(0, 0, 8, 0),
        };
        _altTextCloseButton = new Button
        {
            MinWidth = 72,
            Padding = new Thickness(10, 4),
            Content = PresentationPaneTextResources.CloseCommand,
        };

        _altTextTitleBox.TextChanged += (_, _) => RefreshVisibleAltTextPaneFromFields();
        _altTextDescriptionBox.TextChanged += (_, _) => RefreshVisibleAltTextPaneFromFields();
        _altTextDecorativeCheck.PropertyChanged += (_, e) =>
        {
            if (e.Property == ToggleButton.IsCheckedProperty)
                RefreshVisibleAltTextPaneFromFields();
        };
        _altTextApplyButton.Click += (_, _) => ApplyAltTextPane();
        _altTextCloseButton.Click += (_, _) => HideAltTextPane();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12, 8, 12, 12),
            Children =
            {
                _altTextApplyButton,
                _altTextCloseButton,
            }
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Children =
            {
                _altTextPaneHeading,
                _altTextPaneMessage,
                _altTextTitleLabel,
                _altTextTitleBox,
                _altTextDescriptionLabel,
                _altTextDescriptionBox,
                _altTextDecorativeCheck,
                buttons,
            }
        };

        return new Border
        {
            Width = 292,
            IsVisible = false,
            Background = FreePBrushes.White,
            BorderBrush = FreePBrushes.PaneBorder,
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = panel,
        };
    }

    private static TextBlock BuildAltTextPaneLabel()
        => new()
        {
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
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
            Padding = new Thickness(6, 4),
        };

    private Border BuildMediaCaptionPaneHost()
    {
        _mediaCaptionPaneHeading = new TextBlock
        {
            Text = PresentationPaneTextResources.MediaCaptionsHeading,
            FontSize = PresentationMediaPaneVisualMetrics.HeadingFontSize,
            FontWeight = FontWeight.SemiBold,
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
            ItemsSource = PresentationPaneTextResources.MediaPlaybackStartOptions
                .Select(option => option.Label)
                .ToArray(),
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

        return new Border
        {
            Width = PresentationMediaPaneVisualMetrics.PaneWidth,
            Background = FreePBrushes.White,
            BorderBrush = FreePBrushes.DisabledBorder,
            BorderThickness = new Thickness(PresentationMediaPaneVisualMetrics.PaneBorderThickness, 0, 0, 0),
            IsVisible = false,
            Child = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Children =
                {
                    _mediaCaptionPaneHeading,
                    _mediaCaptionPaneMessage,
                    _mediaCaptionTrackBox,
                    _mediaCaptionLabelText,
                    _mediaCaptionLabelBox,
                    _mediaCaptionLanguageText,
                    _mediaCaptionLanguageBox,
                    _mediaCaptionSourceText,
                    _mediaCaptionSourceBox,
                    _mediaCaptionTranscriptText,
                    _mediaCaptionTranscriptBox,
                    _mediaStartModeText,
                    _mediaStartModeBox,
                    _mediaLoopCheckBox,
                    _mediaShowWhenStoppedCheckBox,
                    _mediaRewindAfterPlayingCheckBox,
                    _mediaPlayFullScreenCheckBox,
                    _mediaStopAfterSlidesText,
                    _mediaStopAfterSlidesBox,
                    _mediaVolumeText,
                    _mediaVolumeSlider,
                    _mediaTrimStartText,
                    _mediaTrimStartBox,
                    _mediaTrimEndText,
                    _mediaTrimEndBox,
                    _mediaFadeInText,
                    _mediaFadeInBox,
                    _mediaFadeOutText,
                    _mediaFadeOutBox,
                    _mediaBookmarkText,
                    _mediaBookmarkBox,
                    _mediaBookmarkNameText,
                    _mediaBookmarkNameBox,
                    _mediaBookmarkTimeText,
                    _mediaBookmarkTimeBox,
                    new WrapPanel
                    {
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = MediaPaneMargin(
                            PresentationMediaPaneVisualMetrics.ActionRowTopMargin,
                            PresentationMediaPaneVisualMetrics.ActionRowBottomMargin),
                        Children =
                        {
                            _mediaPaneButtons.InVisualOrder[0],
                            _mediaPaneButtons.InVisualOrder[1],
                            _mediaPaneButtons.InVisualOrder[2],
                            _mediaPaneButtons.InVisualOrder[3],
                            _mediaPaneButtons.InVisualOrder[4],
                            _mediaPaneButtons.InVisualOrder[5],
                            _mediaPaneButtons.InVisualOrder[6],
                            _mediaPaneButtons.InVisualOrder[7],
                            _mediaPaneButtons.InVisualOrder[8],
                            _mediaPaneButtons.InVisualOrder[9],
                        },
                    },
                },
            },
        };
    }

    private static TextBlock BuildMediaCaptionPaneLabel()
        => new()
        {
            FontSize = PresentationMediaPaneVisualMetrics.BodyFontSize,
            FontWeight = FontWeight.SemiBold,
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
                PresentationMediaPaneVisualMetrics.FieldVerticalPadding),
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
        ReadSelectedMediaCaptionTrack,
        comboBox => comboBox.SelectedItem is ComboBoxItem { Tag: int bookmarkIndex } ? bookmarkIndex : null,
        new PresentationMediaPaneFormEventRouter(_mediaPaneHostCoordinator));

    private int? ReadSelectedMediaCaptionTrack(ComboBox comboBox)
    {
        var plan = LastMediaCaptionAuthoringPanePlan;
        var index = comboBox.SelectedIndex;
        return plan is not null && index >= 0 && index < plan.Tracks.Count
            ? plan.Tracks[index].TrackIndex
            : null;
    }

    private Border BuildSmartArtTextPaneHost()
    {
        var chrome = PresentationPaneTextResources.BuildSmartArtTextPaneChrome();
        _smartArtTextPaneHeading = new TextBlock
        {
            Text = chrome.Heading,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
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
            Padding = new Thickness(10, 4),
            Margin = new Thickness(0, 0, 8, 0),
        };
        _smartArtTextPanePictureButton = new Button
        {
            Content = chrome.ReplacePicture,
            MinWidth = 120,
            Padding = new Thickness(10, 4),
            Margin = new Thickness(0, 0, 8, 0),
        };
        _smartArtTextPaneClearPictureButton = new Button
        {
            Content = chrome.RemovePicture,
            MinWidth = 120,
            Padding = new Thickness(10, 4),
            Margin = new Thickness(0, 0, 8, 0),
        };
        _smartArtTextPaneApplyButton = new Button
        {
            Content = chrome.Apply,
            MinWidth = 72,
            Padding = new Thickness(10, 4),
            Margin = new Thickness(0, 0, 8, 0),
        };
        _smartArtTextPaneCloseButton = new Button
        {
            Content = chrome.Close,
            MinWidth = 72,
            Padding = new Thickness(10, 4),
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

        // Keep the fixed-width pane usable at the same 320px width as WPF: the
        // command row must wrap instead of measuring wider than its host and
        // leaving the left-side actions unreachable.
        _smartArtTextPaneCommandActions = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(12, 8, 12, 12),
        };
        _smartArtTextPaneCommandActions.Children.Add(_smartArtTextPaneAssistantButton);
        _smartArtTextPaneCommandActions.Children.Add(_smartArtTextPanePictureButton);
        _smartArtTextPaneCommandActions.Children.Add(_smartArtTextPaneClearPictureButton);
        _smartArtTextPaneCommandActions.Children.Add(_smartArtTextPaneApplyButton);
        _smartArtTextPaneCommandActions.Children.Add(_smartArtTextPaneCloseButton);
        DockPanel.SetDock(_smartArtTextPaneCommandActions, Dock.Bottom);

        DockPanel.SetDock(_smartArtTextPaneOutlineActions, Dock.Bottom);

        var header = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Children =
            {
                _smartArtTextPaneHeading,
                _smartArtTextPaneMessage,
            }
        };
        DockPanel.SetDock(header, Dock.Top);

        return new Border
        {
            Width = 320,
            IsVisible = false,
            Background = FreePBrushes.White,
            BorderBrush = FreePBrushes.PaneBorder,
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = new DockPanel
            {
                Children =
                {
                    header,
                    _smartArtTextPaneOutlineActions,
                    _smartArtTextPaneCommandActions,
                    new ScrollViewer
                    {
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        Content = _smartArtTextPaneRowsPanel,
                    },
                }
            },
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
            MinWidth = 82,
            Padding = new Thickness(8, 3),
            Margin = new Thickness(0, 0, 6, 6),
            IsEnabled = false,
        };
        ToolTip.SetTip(button, toolTip);
        button.Click += (_, _) => ApplySmartArtTextPaneAction(kind);
        _smartArtTextPaneActionButtons.Add(button);
        _smartArtTextPaneOutlineActions.Children.Add(button);
    }

    private Border BuildAccessibilityCheckerPaneHost()
    {
        _accessibilityCheckerPaneHeading = new TextBlock
        {
            Text = PresentationPaneTextResources.AccessibilityHeading,
            FontFamily = AvaloniaCompactDialogChrome.WindowsUiFontFamily,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(12, 12, 12, 4),
        };
        _accessibilityCheckerPaneMessage = new TextBlock
        {
            FontFamily = AvaloniaCompactDialogChrome.WindowsUiFontFamily,
            FontSize = 12,
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
            Spacing = 6,
        };

        var header = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Children =
            {
                _accessibilityCheckerPaneHeading,
                _accessibilityCheckerPaneMessage,
                _accessibilityCheckerReviewDetailsPanel,
            }
        };
        DockPanel.SetDock(header, Dock.Top);

        return new Border
        {
            Width = 320,
            IsVisible = false,
            Background = FreePBrushes.White,
            BorderBrush = FreePBrushes.PaneBorder,
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = new DockPanel
            {
                Children =
                {
                    header,
                    new ScrollViewer
                    {
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Content = _accessibilityCheckerRowsPanel,
                    },
                }
            },
        };
    }

    private Border BuildProofingPaneHost()
    {
        _proofingPaneHeading = new TextBlock
        {
            Text = PresentationPaneTextResources.ProofingHeading,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
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

        var header = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Children =
            {
                _proofingPaneHeading,
                _proofingPaneMessage,
            }
        };
        DockPanel.SetDock(header, Dock.Top);

        return new Border
        {
            Width = 320,
            IsVisible = false,
            Background = FreePBrushes.White,
            BorderBrush = FreePBrushes.PaneBorder,
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = new DockPanel
            {
                Children =
                {
                    header,
                    new ScrollViewer
                    {
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Content = _proofingPaneRowsPanel,
                    },
                }
            },
        };
    }

    private Border BuildReadingOrderPaneHost()
    {
        _readingOrderPaneHeading = new TextBlock
        {
            Text = PresentationPaneTextResources.ReadingOrderHeading,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(12, 12, 12, 4),
        };
        _readingOrderPaneMessage = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = FreePBrushes.PaneSecondaryText,
            Margin = new Thickness(12, 0, 12, 8),
        };
        _readingOrderMoveEarlierButton = new Button
        {
            Width = PresentationReadingOrderPaneVisualMetrics.MoveEarlierButtonWidth,
            Height = PresentationReadingOrderPaneVisualMetrics.ActionButtonHeight,
            FontSize = PresentationReadingOrderPaneVisualMetrics.BodyFontSize,
            Padding = new Thickness(
                PresentationReadingOrderPaneVisualMetrics.ActionButtonHorizontalPadding,
                PresentationReadingOrderPaneVisualMetrics.ActionButtonVerticalPadding),
            Margin = new Thickness(0, 0, PresentationReadingOrderPaneVisualMetrics.ActionButtonGap, 0),
        };
        _readingOrderMoveLaterButton = new Button
        {
            Width = PresentationReadingOrderPaneVisualMetrics.MoveLaterButtonWidth,
            Height = PresentationReadingOrderPaneVisualMetrics.ActionButtonHeight,
            FontSize = PresentationReadingOrderPaneVisualMetrics.BodyFontSize,
            Padding = new Thickness(
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
                ReadingOrderActionTopCompensation,
                PresentationReadingOrderPaneVisualMetrics.ContentSideMargin,
                PresentationReadingOrderPaneVisualMetrics.MessageBottomMargin - ReadingOrderActionTopCompensation),
            Children =
            {
                _readingOrderMoveEarlierButton,
                _readingOrderMoveLaterButton,
            }
        };

        var header = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Children =
            {
                _readingOrderPaneHeading,
                _readingOrderPaneMessage,
                actionPanel,
            }
        };
        DockPanel.SetDock(header, Dock.Top);

        var panel = new DockPanel();
        panel.Children.Add(header);
        var itemsScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _readingOrderPaneItemsPanel,
        };
        itemsScroll.SetValue(ScrollViewer.AllowAutoHideProperty, false);
        panel.Children.Add(itemsScroll);

        return new Border
        {
            Width = PresentationReadingOrderPaneVisualMetrics.PaneWidth,
            IsVisible = false,
            Background = FreePBrushes.White,
            BorderBrush = FreePBrushes.PaneBorder,
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = panel,
        };
    }

    private Border BuildAnimationPaneHost()
    {
        _animationPaneHeading = new TextBlock
        {
            Text = _animationPaneSession.ControlSchema.Heading,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = FreePBrushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        _animationPaneMessage = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = FreePBrushes.PaneSecondaryText,
            IsVisible = false,
        };
        _animationPanePreviewButton = new Button
        {
            Content = PresentationPaneTextResources.AnimationPreview,
            Padding = new Thickness(6, 2),
            Margin = new Thickness(0, 4, 6, 4),
        };
        _animationPanePlaybackControlsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                _animationPanePreviewButton,
            },
        };

        _animationPaneItemsPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
        };

        var header = new Border
        {
            Background = FreePBrushes.Accent,
            Padding = new Thickness(0, 4, 4, 4),
            Child = new DockPanel
            {
                LastChildFill = true,
                Children =
                {
                    _animationPanePlaybackControlsPanel,
                    _animationPaneHeading,
                },
            },
        };
        DockPanel.SetDock(_animationPanePlaybackControlsPanel, Dock.Right);
        DockPanel.SetDock(header, Dock.Top);

        var panel = new DockPanel();
        panel.Children.Add(header);
        panel.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _animationPaneItemsPanel,
        });

        return new Border
        {
            Width = 240,
            IsVisible = false,
            Background = FreePBrushes.DisabledSurface,
            BorderBrush = FreePBrushes.PaneBorder,
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = panel,
        };
    }

    // ── Interaction wiring (Theme 15) ───────────────────────────────────────────

    private void WireInteraction(Canvas textOverlay)
    {
        if (_adorner is null) return;

        // Allow the canvas to receive keyboard focus for arrow/delete keys.
        _slideCanvas.Focusable = true;

        // Gesture handler drives selection, move, resize, rotate.
        _gestureHandler = new AvaloniaCanvasGestureHandler(
            _slideCanvas,
            Editor,
            _adorner,
            OnChartPointDoubleClick,
            tryOpenOleInPlace: TryOpenOleInPlace);
        _slideCanvas.AttachGestureHandler(_gestureHandler);
        ApplyPresentationViewShowState(_viewShowState);

        // Text editor: double-click a shape to edit its text.
        _textEditor = new AvaloniaInCanvasTextEditor(
            _slideCanvas,
            Editor,
            textOverlay,
#if FREEP_WINDOWS_CAPTURE
            AvaloniaOleInPlaceHost.TryCreate);
#else
            null);
#endif
        WireTableContextMenu();
    }

    /// <summary>
    /// Re-wires the interaction layer to the new <see cref="Editor"/> instance after a
    /// file open / new operation.
    /// </summary>
    private void RewireInteractionToEditor()
    {
        if (_adorner is null) return;
        CloseActiveOleHost();
        // The gesture handler and text editor subscribe strongly to the canvas's routed
        // pointer events, so detach them before binding the new EditingSession.
        // Find the textOverlay in the visual tree (it's the 3rd child of the canvasStack).
        // We can retrieve it from the existing text editor's overlay or re-find it:
        Canvas? textOverlay = null;
        _textEditor?.Dispose();
        _textEditor = null;
        _gestureHandler?.Dispose();
        _gestureHandler = null;
        UnwireTableContextMenu();

        // Re-find the overlay canvas from the full-stage stack. The canvas and
        // selection adorner now live in its margined child, matching WPF.
        if (_slideCanvas.Parent is Grid canvasContent &&
            canvasContent.Parent is Grid canvasStack)
        {
            textOverlay = canvasStack.Children
                .OfType<Canvas>()
                .FirstOrDefault(candidate => !ReferenceEquals(candidate, _oleOverlay));
        }

        if (textOverlay is not null)
        {
            _gestureHandler = new AvaloniaCanvasGestureHandler(
                _slideCanvas,
                Editor,
                _adorner,
                OnChartPointDoubleClick,
                tryOpenOleInPlace: TryOpenOleInPlace);
            _slideCanvas.AttachGestureHandler(_gestureHandler);
            ApplyPresentationViewShowState(_viewShowState);
            _textEditor = new AvaloniaInCanvasTextEditor(
                _slideCanvas,
                Editor,
                textOverlay,
#if FREEP_WINDOWS_CAPTURE
                AvaloniaOleInPlaceHost.TryCreate);
#else
                null);
#endif
            WireTableContextMenu();
        }
    }

    private bool TryOpenOleInPlace(SlideShape shape)
    {
#if FREEP_WINDOWS_CAPTURE
        var plan = OleActivationCoordinator.PlanInPlaceActivation(
            shape,
            _slideCanvas.CurrentTransform);
        if (plan is null)
            return false;

        CloseActiveOleHost();
        var overlayBounds = new Rect(
            plan.Bounds.Left,
            plan.Bounds.Top,
            plan.Bounds.Width,
            plan.Bounds.Height);

        return AvaloniaOleInPlaceHost.TryShow(
            _oleOverlay,
            plan.OleObject,
            overlayBounds,
            onActivationFailed: () =>
            {
                CloseActiveOleHost();
                OleActivationService.TryActivate(plan.OleObject);
            },
            out _activeOleHost);
#else
        return false;
#endif
    }

    private void CloseActiveOleHost()
    {
#if FREEP_WINDOWS_CAPTURE
        if (_activeOleHost is null)
            return;

        _activeOleHost.Dispose();
        _oleOverlay.Children.Remove(_activeOleHost);
        _oleOverlay.IsHitTestVisible = false;
        _activeOleHost = null;
#endif
    }

    private void WireTableContextMenu()
    {
        UnwireTableContextMenu();
        _slideCanvas.PointerPressed += OnTableContextMenuPointerPressed;
    }

    private void UnwireTableContextMenu() =>
        _slideCanvas.PointerPressed -= OnTableContextMenuPointerPressed;

    private void OnTableContextMenuPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_slideCanvas).Properties.IsRightButtonPressed)
            return;

        var point = e.GetPosition(_slideCanvas);
        var slidePoint = _slideCanvas.CurrentTransform.ScreenToSlide(point.X, point.Y);
        var plan = _domainContextMenuSession.BuildAtSlidePoint(slidePoint.X, slidePoint.Y);
        if (plan is null)
            return;

        var menu = BuildDomainContextMenu(plan);
        _slideCanvas.ContextMenu = menu;
        menu.Open(_slideCanvas);
        e.Handled = true;
    }

    private ContextMenu BuildDomainContextMenu(PresentationDomainContextMenuPlan plan)
    {
        var menu = new ContextMenu
        {
            // Avalonia opens domain context menus at the right-click location.
            Placement = PlacementMode.Pointer,
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

    private bool TryExecuteInlineTableAction(PresentationDomainContextAction action)
        => _textEditor?.TryExecuteActiveTableStructureAction(action.Kind) == true;





    // ── Ribbon ─────────────────────────────────────────────────────────────────

    private void ApplyPresentationViewShowState(PresentationViewShowState state)
    {
        _viewShowState = state;
        if (_gestureHandler is null)
            return;

        _gestureHandler.SnapToGrid = state.ShowGridlines;
        _gestureHandler.SnapToShapes = state.ShowGuides;
    }

    private void ApplyPresentationViewZoomState(PresentationViewZoomState state)
    {
        _viewZoomState = state;
        _slideCanvas.ApplyViewZoomState(state);
    }

    private Control BuildRibbon()
    {
        var registry = BuildCommandRegistry();
        var definition = FreeP.Ribbon.Definitions.FreePRibbon.Build(FreeP.Ribbon.Definitions.FreePRibbonCapabilities.Avalonia);
        _ribbonDefinition = definition;
        _ribbonCommandRegistry = registry;

        _ribbonControl = AvaloniaRibbonRenderer.BuildRibbon(
            definition,
            registry,
            afterExecute: null,
            palette: RibbonVisualPalette.FromTheme(App.ActiveTheme),
            onFileTabSelected: ShowBackstage,
            stateStore: _ribbonStateStore);

        return new Border
        {
            Height          = FreePShellVisualMetrics.RibbonHeight,
            Background      = FreePBrushes.White,
            BorderBrush     = FreePBrushes.GridBorder,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child           = _ribbonControl,
        };
    }

    internal RibbonCommandRegistry BuildCommandRegistry()
    {
        _ribbonBindingSession = new FreePRibbonBindingSession(
            Editor,
            _ribbonStateStore,
            CreateRibbonHostProfile);
        return _ribbonBindingSession.Registry;
    }

    private FreePRibbonHostProfile CreateRibbonHostProfile() =>
        FreePRibbonHostProfileFactory.Create(new FreePRibbonHostPorts
        {
            ActionProfile = GetRibbonActionPortProfile(),
            QueryEndpoints = new FreePRibbonHostQueryEndpoints
            {
                BeginFormatPainter = () => _gestureHandler?.BeginFormatPainter() == true,
                EditPointsEnabled = () => _slideCanvas.EditPointsEnabled,
                AnimationPaneVisible = () => IsAnimationPaneVisible,
                ViewShowState = () => _viewShowState,
                ViewZoomState = () => _viewZoomState,
            },
            TextActionTargets = CreateRibbonTextActionTargets(),
            DesignCommands = new FreePRibbonDesignCommandEndpoints
            {
                OpenCustomSlideSize = OnCustomSlideSizeRequested,
                OpenLayoutPicker = OnLayoutPickerRequested,
            },
            FileCommands = new FreePRibbonFileCommandEndpoints
            {
                New = FileNew,
                Open = () => _ = FileOpenAsync(),
                Save = () => _ = FileSaveAsync(),
                SaveAs = () => _ = FileSaveAsAsync(),
                ExportPdf = () => _ = FileExportPdfAsync(),
                ExportNotesPagePdf = () => _ = FileExportNotesPagePdfAsync(),
                ExportImages = () => _ = FileExportImagesAsync(),
                Print = () =>
                {
                    RefreshHandoutLayoutPlan();
                    ShowPrintBackstage();
                },
                ExportVideo = () => _ = FileExportVideoAsync(),
            },
            OleCommands = new FreePRibbonOleCommandEndpoints
            {
                InsertEmbeddedObject = () => _ = InsertEmbeddedObjectFromFileAsync(),
                TryOpenInlineEmbeddedObject = () => _textEditor?.TryActivateInlineOleObject() == true,
                TryOpenSelectedEmbeddedObject = ole =>
                {
                    OleActivationService.TryActivate(ole);
                    return true;
                },
            },
        });

    private FreePRibbonTextActionTargets CreateRibbonTextActionTargets() => new()
    {
        Notes = FreePRibbonTextActionEndpointFactory.CreateFormattingTarget(
            TryApplyCurrentSlideNotesTextFormat,
            TryApplyCurrentSlideNotesValueFormat,
            TryApplyCurrentSlideNotesParagraphFormat),
        Shape = new FreePRibbonTextActionEndpoints
        {
            ToggleFormat = format => _textEditor?.TryApplyActiveShapeTextFormat(format) == true,
            SetParagraphAlignment = alignment =>
                _textEditor?.TryApplyActiveShapeParagraphAlignment(alignment) == true,
            ApplyListPreset = preset =>
                _textEditor?.TryApplyActiveShapeParagraphListPreset(preset) == true,
            ToggleBullets = () =>
                _textEditor?.TryApplyActiveShapeParagraphBulletToggle() == true,
            ToggleNumbering = () =>
                _textEditor?.TryApplyActiveShapeParagraphNumberingToggle() == true,
            Indent = () => _textEditor?.TryApplyActiveShapeParagraphIndent() == true,
            Outdent = () => _textEditor?.TryApplyActiveShapeParagraphOutdent() == true,
            SetFontFamily = family => _textEditor?.TryApplyActiveShapeFontFamily(family) == true,
            SetFontSize = sizePt => _textEditor?.TryApplyActiveShapeFontSize(sizePt) == true,
            SetColor = color => _textEditor?.TryApplyActiveShapeColor(color) == true,
            RemoveHyperlink = () =>
                _textEditor?.TryApplySelectedShapeRunHyperlink(null) == true,
        },
        Table = new FreePRibbonTextActionEndpoints
        {
            ToggleFormat = format => _textEditor?.TryApplyActiveTableCellTextFormat(format) == true,
            SetParagraphAlignment = alignment =>
                _textEditor?.TryApplyActiveTableCellParagraphAlignment(alignment) == true,
            ApplyListPreset = preset =>
                _textEditor?.TryApplyActiveTableCellParagraphListPreset(preset) == true,
            ToggleBullets = () =>
                _textEditor?.TryApplyActiveTableCellParagraphBulletToggle() == true,
            ToggleNumbering = () =>
                _textEditor?.TryApplyActiveTableCellParagraphNumberingToggle() == true,
            Indent = () => _textEditor?.TryApplyActiveTableCellParagraphIndent() == true,
            Outdent = () => _textEditor?.TryApplyActiveTableCellParagraphOutdent() == true,
            SetFontFamily = family => _textEditor?.TryApplyActiveTableCellFontFamily(family) == true,
            SetFontSize = sizePt => _textEditor?.TryApplyActiveTableCellFontSize(sizePt) == true,
            SetColor = color => _textEditor?.TryApplyActiveTableCellColor(color) == true,
            SetTextVerticalType = verticalType =>
                _textEditor?.TryApplyActiveTableCellTextVerticalType(verticalType) == true,
            SetTableCellFill = color => _textEditor?.TryApplyActiveTableCellFill(color) == true,
            SetTableCellAnchor = anchor => _textEditor?.TryApplyActiveTableCellAnchor(anchor) == true,
            SetTableCellBorder = (side, outline) =>
                _textEditor?.TryApplyActiveTableCellBorder(side, outline) == true,
            SetTableCellInset = (side, value) =>
                _textEditor?.TryApplyActiveTableCellInset(side, value) == true,
            SetTableRowHeight = height => _textEditor?.TryApplyActiveTableRowHeight(height) == true,
        },
    };

    private void OnCustomSlideSizeRequested(PresentationDesignCommandPlan plan)
    {
        LastCustomSlideSizeRequestPlan = plan;
        OpenSlideSizeDialog();
        _statusText.Text = PresentationShellTextCatalog.Resolve(
            PresentationShellTextCatalog.SlideSizeDialogStatus);
    }

    private void OnLayoutPickerRequested(PresentationDesignCommandPlan plan)
    {
        LastLayoutRequestPlan = plan;
        LastLayoutPickerPlan = PresentationDesignCommandPlanner.BuildLayoutPickerPlan(
            _presentation,
            Editor.CurrentSlideIndex);
        ShowLayoutPicker(LastLayoutPickerPlan);
        _statusText.Text = PresentationShellTextCatalog.Resolve(
            PresentationShellTextCatalog.LayoutPickerStatus(LastLayoutPickerPlan.Choices.Count));
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
            RefreshSlidePane();
            RefreshCanvas();
            UpdateStatus();
            HideLayoutPicker();
        }

        return applied;
    }

    internal void OpenTablePicker()
    {
        LastTablePickerPlan = TableInsertionPickerPlanner.BuildPlan();
        ShowTablePicker(LastTablePickerPlan);
        _statusText.Text = PresentationShellTextCatalog.Resolve(
            PresentationShellTextCatalog.TablePickerStatus(LastTablePickerPlan.Choices.Count));
    }

    internal bool ApplyTablePickerChoice(int rows, int columns)
    {
        var applied = TableInsertionPickerPlanner.TryApplyChoice(Editor, rows, columns);
        if (applied)
        {
            RefreshSlidePane();
            RefreshCanvas();
            UpdateStatus();
            HideTablePicker();
        }

        return applied;
    }

    private void ShowTablePicker(TableInsertionPickerPlan plan)
    {
        if (_tablePickerHost is null || _tablePickerPanel is null)
            return;

        _tablePickerPanel.Rows = plan.MaxRows;
        _tablePickerPanel.Columns = plan.MaxColumns;
        _tablePickerPanel.Children.Clear();
        foreach (var choice in plan.Choices)
        {
            var button = new Button
            {
                Tag = choice,
                Content = choice.DisplayLabel,
                Margin = new Thickness(2),
                Padding = new Thickness(6, 4),
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
            _tablePickerPanel.Children.Add(button);
        }

        HideLayoutPicker();
        _tablePickerHost.IsVisible = true;
    }

    private void HideTablePicker()
    {
        if (_tablePickerHost is not null)
            _tablePickerHost.IsVisible = false;
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
                    FontWeight = FontWeight.SemiBold,
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
        _layoutPickerHost.IsVisible = true;
    }

    private void HideLayoutPicker()
    {
        if (_layoutPickerHost is not null)
            _layoutPickerHost.IsVisible = false;
    }

    internal void OpenSlideSizeDialog()
    {
        if (_slideSizeDialog is not null)
        {
            _slideSizeDialog.Activate();
            return;
        }

        HideLayoutPicker();
        HideTablePicker();
        var dialog = new SlideSizeDialog(Editor);
        LastCustomSlideSizeInitialState = dialog.InitialState;
        _slideSizeDialog = dialog;
        dialog.Closed += (_, _) =>
        {
            LastCustomSlideSizeResultPlan = dialog.LastResultPlan;
            if (dialog.LastResultPlan?.ShouldApply == true)
            {
                RefreshCanvas();
                UpdateStatus();
            }
            _slideSizeDialog = null;
        };

        if (IsVisible)
            _ = dialog.ShowDialog<bool?>(this);
        else
            dialog.Show();
    }

    internal void OpenHeaderFooterDialog(HeaderFooterCommandFocus focus)
    {
        LastHeaderFooterFocus = focus;
        if (_headerFooterDialog is not null)
        {
            _headerFooterDialog.Activate();
            return;
        }

        HideLayoutPicker();
        HideTablePicker();
        var dialog = new HeaderFooterDialog(Editor, focus);
        LastHeaderFooterState = dialog.InitialState;
        _headerFooterDialog = dialog;
        _statusText.Text = PresentationShellTextCatalog.Resolve(
            PresentationShellTextCatalog.HeaderFooterDialogStatus);
        dialog.Closed += (_, _) =>
        {
            LastHeaderFooterApplyPlan = dialog.LastApplyPlan;
            if (dialog.LastApplyPlan?.ShouldApply == true)
            {
                RefreshCanvas();
                UpdateStatus();
            }
            _headerFooterDialog = null;
        };

        if (IsVisible)
            _ = dialog.ShowDialog<bool?>(this);
        else
            dialog.Show();
    }

    internal void OpenSlideShowSettingsDialog()
    {
        if (_slideShowSettingsDialog is not null)
        {
            _slideShowSettingsDialog.Activate();
            return;
        }

        var dialog = new SlideShowSettingsDialog(Editor);
        _slideShowSettingsDialog = dialog;
        _statusText.Text = PresentationShellTextCatalog.Resolve(
            PresentationShellTextCatalog.SlideShowSettingsDialogStatus);
        dialog.Closed += (_, _) => _slideShowSettingsDialog = null;
        if (IsVisible)
            _ = dialog.ShowDialog<bool?>(this);
        else
            dialog.Show();
    }

    private static Control BuildLayoutChoiceTile(PresentationLayoutChoice choice)
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
            Children =
            {
                BuildLayoutThumbnail(choice),
                label,
            },
        };

        if (!string.IsNullOrWhiteSpace(choice.Chrome.BadgeText))
        {
            stack.Children.Add(new TextBlock
            {
                Text = choice.Chrome.BadgeText,
                FontSize = 10,
                Foreground = BrushFromHex(
                    PresentationDesignCommandPlanner.LayoutPickerVisuals.BadgeForegroundBrushHex),
                FontWeight = FontWeight.SemiBold,
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

    private static Control BuildLayoutThumbnail(PresentationLayoutChoice choice)
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
            var rect = new AvaloniaRectangle
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

    private async Task InsertPictureFromFileAsync()
    {
        var result = await ImportPresentationAssetAsync(PresentationAssetImportKind.Picture);
        await MaterializePresentationAssetImportResultAsync(
            result,
            new PresentationAssetImportOutcomePolicy(ShowInsertedStatus: true));
    }

    private async Task InsertMediaFromFileAsync(bool isVideo)
    {
        var kind = isVideo
            ? PresentationAssetImportKind.Video
            : PresentationAssetImportKind.Audio;
        var result = await ImportPresentationAssetAsync(kind);
        await MaterializePresentationAssetImportResultAsync(
            result,
            new PresentationAssetImportOutcomePolicy(ShowInsertedStatus: true));
    }

    private async Task InsertEmbeddedObjectFromFileAsync()
    {
        var result = await ImportPresentationAssetAsync(PresentationAssetImportKind.EmbeddedObject);
        await MaterializePresentationAssetImportResultAsync(
            result,
            new PresentationAssetImportOutcomePolicy(ShowInsertedStatus: true));
    }

    private async Task PickTransitionSoundAsync()
    {
        var result = await ImportPresentationAssetAsync(PresentationAssetImportKind.TransitionSound);
        await MaterializePresentationAssetImportResultAsync(
            result,
            new PresentationAssetImportOutcomePolicy(ShowInsertedStatus: true));
    }

    // ── File lifecycle ─────────────────────────────────────────────────────────


    private async Task ApplyPictureBulletFromFileAsync()
    {
        Task<PresentationPictureBulletPayload?>? payloadOverride = null;
        ResolvePictureBulletPayloadOverride(ref payloadOverride);
        if (payloadOverride is not null)
        {
            try
            {
                var payload = await payloadOverride;
                if (payload is not null && ApplyImportedPictureBullet(payload))
                {
                    _statusText.Text = PresentationShellTextCatalog.Resolve(
                        PresentationShellTextCatalog.PictureBulletAppliedStatus);
                }
            }
            catch (Exception ex)
            {
                _statusText.Text = SisterAppFileTextPlanner.FormatCommandFailed(
                    FileText,
                    PresentationShellTextCatalog.Resolve(
                        PresentationShellTextCatalog.PictureBulletCommandName),
                    ex.Message);
            }
            return;
        }

        var result = await ImportPresentationAssetAsync(PresentationAssetImportKind.PictureBullet);
        await MaterializePresentationAssetImportResultAsync(
            result,
            new PresentationAssetImportOutcomePolicy(
                SuccessStatusText: PresentationShellTextCatalog.Resolve(
                    PresentationShellTextCatalog.PictureBulletAppliedStatus)));
    }

    private void ShowDomainDialog(Window dialog)
    {
        if (IsVisible)
        {
            _ = dialog.ShowDialog<bool?>(this);
            return;
        }

        dialog.Show();
    }

    internal void OpenChartDataDialog()
    {
        if (!_workareaSession.CanOpenDomainDialog(PresentationDomainDialogKind.ChartData))
            return;

        ShowDomainDialog(new ChartDataDialog(Editor));
    }

    internal void OpenChartDisplayOptionsDialog()
    {
        if (!_workareaSession.CanOpenDomainDialog(PresentationDomainDialogKind.ChartDisplayOptions))
            return;

        ShowDomainDialog(new ChartDisplayOptionsDialog(Editor));
    }

    internal void OpenChartAxisOptionsDialog() => OpenChartAxisOptionsDialog(null);

    internal void OpenChartAxisOptionsDialog(ChartAxisKind? initialAxis)
    {
        if (!_workareaSession.CanOpenDomainDialog(PresentationDomainDialogKind.ChartAxisOptions))
            return;

        ShowDomainDialog(new ChartAxisOptionsDialog(Editor, initialAxis));
    }

    internal void OpenChartSeriesOptionsDialog() => OpenChartSeriesOptionsDialog(null);

    internal void OpenChartSeriesOptionsDialog(int? initialSeriesIndex)
    {
        if (!_workareaSession.CanOpenDomainDialog(PresentationDomainDialogKind.ChartSeriesOptions))
            return;

        ShowDomainDialog(new ChartSeriesOptionsDialog(Editor, initialSeriesIndex));
    }

    private void OnChartPointDoubleClick(ChartPointHit hit)
    {
        Editor.Select(hit.ShapeId);
        OpenChartPointOptionsDialog(hit.SeriesIndex, hit.PointIndex);
    }

    internal void OpenChartPointOptionsDialog(int? seriesIndex = null, int? pointIndex = null)
    {
        if (!_workareaSession.CanOpenDomainDialog(PresentationDomainDialogKind.ChartPointOptions))
            return;

        ShowDomainDialog(new ChartPointOptionsDialog(Editor, seriesIndex, pointIndex));
    }

    internal void OpenChartLayoutOptionsDialog()
    {
        if (!_workareaSession.CanOpenDomainDialog(PresentationDomainDialogKind.ChartLayoutOptions))
            return;

        ShowDomainDialog(new ChartLayoutOptionsDialog(Editor));
    }

    internal void OpenChartExSeriesLayoutDialog()
    {
        if (!_workareaSession.CanOpenDomainDialog(PresentationDomainDialogKind.ChartExSeriesLayout))
            return;

        ShowDomainDialog(new ChartExSeriesLayoutDialog(Editor));
    }

    internal void OpenChartDataTableOptionsDialog()
    {
        if (!_workareaSession.CanOpenDomainDialog(PresentationDomainDialogKind.ChartDataTableOptions))
            return;

        ShowDomainDialog(new ChartDataTableOptionsDialog(Editor));
    }

    internal void OpenChartBubbleOptionsDialog()
    {
        if (!_workareaSession.CanOpenDomainDialog(PresentationDomainDialogKind.ChartBubbleOptions))
            return;

        ShowDomainDialog(new ChartBubbleOptionsDialog(Editor));
    }

    internal void OpenChartPieOptionsDialog()
    {
        if (!_workareaSession.CanOpenDomainDialog(PresentationDomainDialogKind.ChartPieOptions))
            return;

        ShowDomainDialog(new ChartPieOptionsDialog(Editor));
    }

    internal void OpenChartPlotStyleOptionsDialog()
    {
        if (!_workareaSession.CanOpenDomainDialog(PresentationDomainDialogKind.ChartPlotStyleOptions))
            return;

        ShowDomainDialog(new ChartPlotStyleOptionsDialog(Editor));
    }

    internal void OpenChart3DViewOptionsDialog()
    {
        if (!_workareaSession.CanOpenDomainDialog(PresentationDomainDialogKind.Chart3DViewOptions))
            return;

        ShowDomainDialog(new Chart3DViewOptionsDialog(Editor));
    }

    internal void OpenChartTextOptionsDialog() => OpenChartTextOptionsDialog(ChartTextTarget.Chart);

    internal void OpenChartTextOptionsDialog(ChartTextTarget target)
    {
        if (!_workareaSession.CanOpenDomainDialog(PresentationDomainDialogKind.ChartTextOptions))
            return;

        ShowDomainDialog(new ChartTextOptionsDialog(Editor, target));
    }

    internal void OpenChartAreaOptionsDialog() => OpenChartAreaOptionsDialog(null);

    internal void OpenChartAreaOptionsDialog(ChartAreaFormattingTarget? initialTarget)
    {
        if (!_workareaSession.CanOpenDomainDialog(PresentationDomainDialogKind.ChartAreaOptions)) return;
        var dialog = new ChartAreaOptionsDialog(Editor, initialTarget);
        dialog.ShowDialog(this);
    }

    internal void OpenChartProtectionOptionsDialog()
    {
        if (!_workareaSession.CanOpenDomainDialog(PresentationDomainDialogKind.ChartProtectionOptions))
            return;

        ShowDomainDialog(new ChartProtectionOptionsDialog(Editor));
    }

    internal void OpenRotationOptionsDialog()
    {
        if (!_workareaSession.CanOpenDomainDialog(PresentationDomainDialogKind.RotationOptions))
            return;

        ShowDomainDialog(new RotationOptionsDialog(Editor));
    }

    /// <summary>
    /// Runs an async ribbon/menu command from a void event-handler context without letting a failure
    /// escape. These command entry points are <c>async void</c>, so an exception escaping one
    /// terminates the process — a routine ribbon click against a presentation in an unexpected state
    /// (a missing slide/section lookup, a model mutation that rejects) would kill the app outright.
    /// Report it in the status bar instead, matching how the file/media commands already degrade.
    /// </summary>
    private async void RunGuarded(Func<Task> command, string commandName)
    {
        try
        {
            await command();
        }
        catch (Exception ex)
        {
            _statusText.Text = SisterAppFileTextPlanner.FormatCommandFailed(FileText, commandName, ex.Message);
        }
    }

    internal void OpenHyperlinkDialog() =>
        RunGuarded(
            async () => await OpenHyperlinkDialogAsync(),
            UiText.Get("Ribbon_Command_InsertLink_Label"));


    private async Task<HyperlinkDialogApplyPlan> OpenHyperlinkDialogAsync()
    {
        Hyperlink? selectedRunHyperlink = null;
        var editsSelectedRun = _textEditor is not null
            && _textEditor.TryGetSelectedShapeRunHyperlink(out selectedRunHyperlink);
        var request = _hyperlinkWorkflowSession.BuildRequest(
            editsSelectedRun,
            selectedRunHyperlink);
        LastHyperlinkDialogRequest = request.DialogRequest;

        Task<Hyperlink?>? resultOverride = null;
        ResolveHyperlinkDialogOverride(request.DialogRequest, ref resultOverride);
        var result = resultOverride is not null
            ? await resultOverride
            : await ShowHyperlinkDialogAsync(request.DialogRequest);

        var workflowResult = _hyperlinkWorkflowSession.Apply(
            request,
            result,
            hyperlink => _textEditor?.TryApplySelectedShapeRunHyperlink(hyperlink) == true);
        LastHyperlinkDialogApplyPlan = workflowResult.ApplyPlan;
        if (workflowResult.Target == PresentationHyperlinkApplyTarget.SelectedShape)
            NotifyHyperlinkAppliedObserver();

        return workflowResult.ApplyPlan;
    }

    private async Task<Hyperlink?> ShowHyperlinkDialogAsync(HyperlinkDialogRequest request)
    {
        var dialog = new HyperlinkDialog(request);
        if (IsVisible)
            return await dialog.ShowDialog<Hyperlink?>(this);

        dialog.Show();
        return null;
    }

    internal void OpenSlideZoomDialog() =>
        RunGuarded(OpenSlideZoomDialogAsync, UiText.Get("Shell_Command_SlideZoom"));

    internal async Task OpenSlideZoomDialogAsync()
    {
        var request = _zoomAuthoringSession.BuildSlideInsertionRequest();
        if (request is null || !IsVisible)
            return;

        var dialog = new SlideZoomDialog(
            request.Options,
            request.Title,
            request.SelectedTargetId);
        var result = await dialog.ShowDialog<bool?>(this);
        if (result == true)
            _zoomAuthoringSession.ApplySlideInsertion(dialog.SelectedTargetSlideId);
    }

    internal void OpenSectionZoomDialog() =>
        RunGuarded(OpenSectionZoomDialogAsync, UiText.Get("Shell_Command_SectionZoom"));

    internal async Task OpenSectionZoomDialogAsync()
    {
        var request = _zoomAuthoringSession.BuildSectionInsertionRequest();
        if (request is null || !IsVisible)
            return;

        var dialog = new SectionZoomDialog(
            request.Options,
            request.Title,
            request.SelectedTargetId);
        var result = await dialog.ShowDialog<bool?>(this);
        if (result == true)
            _zoomAuthoringSession.ApplySectionInsertion(dialog.SelectedTargetSectionId);
    }

    internal void OpenSummaryZoomDialog() =>
        RunGuarded(OpenSummaryZoomDialogAsync, UiText.Get("Shell_Command_SummaryZoom"));

    internal async Task OpenSummaryZoomDialogAsync()
    {
        var request = _zoomAuthoringSession.BuildSummaryInsertionRequest();
        if (request is null || !IsVisible)
            return;

        var dialog = new SummaryZoomDialog(
            request.Options,
            request.Title,
            request.SelectedTargetIds);
        var result = await dialog.ShowDialog<bool?>(this);
        if (result == true)
            _zoomAuthoringSession.ApplySummaryInsertion(dialog.SelectedTargetSectionIds);
    }

    internal void OpenZoomTargetDialog() =>
        RunGuarded(OpenZoomTargetDialogAsync, UiText.Get("Shell_Command_ZoomTarget"));

    internal async Task OpenZoomTargetDialogAsync()
    {
        var request = _zoomAuthoringSession.BuildSelectedTargetRequest();
        if (request is null || !IsVisible)
            return;

        if (request.Kind == PresentationZoomTargetKind.Slide)
        {
            var dialog = new SlideZoomDialog(
                request.Options,
                request.Title,
                request.SelectedTargetId);
            var result = await dialog.ShowDialog<bool?>(this);
            if (result == true)
                _zoomAuthoringSession.ApplySelectedTarget(request, dialog.SelectedTargetSlideId);
            return;
        }

        var sectionDialog = new SectionZoomDialog(
            request.Options,
            request.Title,
            request.SelectedTargetId);
        var sectionResult = await sectionDialog.ShowDialog<bool?>(this);
        if (sectionResult == true)
            _zoomAuthoringSession.ApplySelectedTarget(request, sectionDialog.SelectedTargetSectionId);
    }

    internal void OpenSummaryZoomTargetsDialog() =>
        RunGuarded(OpenSummaryZoomTargetsDialogAsync, UiText.Get("Shell_Command_SummaryZoomTargets"));

    internal async Task OpenSummaryZoomTargetsDialogAsync()
    {
        var request = _zoomAuthoringSession.BuildSelectedSummaryTargetsRequest();
        if (request is null || !IsVisible)
            return;

        var dialog = new SummaryZoomDialog(
            request.Options,
            request.Title,
            request.SelectedTargetIds);
        var result = await dialog.ShowDialog<bool?>(this);
        if (result == true)
            _zoomAuthoringSession.ApplySelectedSummaryTargets(
                request,
                dialog.SelectedTargetSectionIds);
    }

    internal async Task OpenZoomObjectPropertiesDialogAsync()
    {
        var request = _zoomAuthoringSession.BuildSelectedPropertiesRequest();
        if (request is null || !IsVisible)
            return;

        var dialog = new ZoomObjectPropertiesDialog(
            request.Properties,
            request.SummaryTargets,
            request.SummaryTileProperties);
        var result = await dialog.ShowDialog<bool?>(this);
        if (result == true)
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

    internal async Task OpenZoomCoverImagePickerAsync()
    {
        var request = _zoomAuthoringSession.BuildSelectedCoverTargetRequest();
        if (request is null)
            return;

        string? summarySectionId = null;
        if (request.RequiresSummaryTarget)
        {
            var targetDialog = new SummaryZoomCoverImageTargetDialog(request.SummaryTargetOptions);
            var targetResult = await targetDialog.ShowDialog<bool?>(this);
            if (targetResult != true)
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
        else if (result.Status == PresentationAssetImportStatus.Unavailable)
        {
            await MaterializePresentationAssetImportResultAsync(
                result,
                new PresentationAssetImportOutcomePolicy());
        }
    }

    internal async Task RestoreZoomPreviewAsync()
    {
        var request = _zoomAuthoringSession.BuildSelectedCoverTargetRequest();
        if (request is null)
            return;

        string? summarySectionId = null;
        if (request.RequiresSummaryTarget)
        {
            var targetDialog = new SummaryZoomCoverImageTargetDialog(request.SummaryTargetOptions);
            var targetResult = await targetDialog.ShowDialog<bool?>(this);
            if (targetResult != true)
                return;
            summarySectionId = targetDialog.SelectedTargetSectionId;
        }

        _zoomAuthoringSession.RestoreSelectedPreview(request, summarySectionId);
    }

    internal void OpenFindDialog() =>
        OpenFindReplaceDialog(showReplace: false);

    internal void OpenFindReplaceDialog() =>
        OpenFindReplaceDialog(showReplace: true);

    private void OpenFindReplaceDialog(bool showReplace)
    {
        if (_findReplaceDialog is not null)
        {
            _findReplaceDialog.ShowReplaceMode(showReplace);
            LastFindReplaceWorkflowPlan = _findReplaceDialog.LastWorkflowPlan;
            _findReplaceDialog.Activate();
            return;
        }

        var dialog = new FindReplaceDialog(
            Editor,
            showReplace,
            () =>
            {
                RefreshCanvas();
                RefreshSlidePane();
                UpdateStatus();
            });
        _findReplaceDialog = dialog;
        LastFindReplaceWorkflowPlan = dialog.LastWorkflowPlan;
        dialog.Closed += (_, _) =>
        {
            LastFindReplaceWorkflowPlan = dialog.LastWorkflowPlan;
            _findReplaceDialog = null;
        };

        if (IsVisible)
            dialog.Show(this);
        else
            dialog.Show();
    }




    private void FileNew() => _ = FileNewAsync();


    private async Task<bool> FileNewAsync() =>
        (await _fileSession.NewAsync()).Succeeded;

    private PresentationBackstageEndpoints BuildBackstageEndpoints() => new(
        GetPresentation: () => _presentation,
        GetDisplayName: () => _fileSession.DisplayName,
        GetIsDirty: () => _fileSession.IsDirty,
        GetCurrentPath: () => _fileSession.CurrentPath,
        GetRecentEntries: () => _fileSession.RecentEntries,
        GetCurrentOptions: () => _options,
        GetDataFolder: FreePApplicationFrameDescriptor.ResolveDataFolderLabel,
        OpenOptions: () => _ = OpenOptionsAsync(),
        New: FileNew,
        Open: () => _ = FileOpenAsync(),
        OpenPath: OpenRecentPath,
        Save: () => _ = FileSaveAsync(),
        SaveAs: () => _ = FileSaveAsAsync(),
        ExportPdf: () => _ = FileExportPdfAsync(),
        ExportNotesPagePdf: () => _ = FileExportNotesPagePdfAsync(),
        ExportImages: () => _ = FileExportImagesAsync(),
        GetPrintPlan: RefreshPrintBackstagePlan,
        Print: request => _backstagePrintOperation = ExecutePrintWorkflowAsync(request),
        ExportVideo: () => _ = FileExportVideoAsync(),
        CanExportVideo: () => _fileSession.CanExportVideo);

    private Control? _backstageRestoreFocus;

    private void ShowBackstage() => ShowBackstage("Info");

    private void ShowBackstage(string paneLabel)
    {
        _backstageRestoreFocus = FocusManager?.GetFocusedElement() as Control ?? _slideCanvas;
        _backstage.Show(paneLabel);
    }

    private void ShowPrintBackstage()
    {
        HidePrintOptionsPane();
        ShowBackstage(PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.PrintSurfacePrintHeading));
    }

    private void HideBackstageAndRestoreFocus()
    {
        _backstage.Hide();
        var target = _backstageRestoreFocus;
        _backstageRestoreFocus = null;
        (target is { IsVisible: true, Focusable: true } ? target : _slideCanvas).Focus();
    }

    private void OpenRecentPath(string path) => _ = OpenRecentPathAsync(path);

    private async Task<bool> OpenRecentPathAsync(string path) =>
        (await _fileSession.OpenRecentPathAsync(path)).Succeeded;

    private async Task<bool> FileOpenAsync() =>
        (await _fileSession.OpenAsync()).Succeeded;

    // Opens the modal FreeP Options editor. On OK it applies the edited settings live (by mutating the
    // shared _options instance the Backstage and file-command session read) and persists it through the shared
    // ApplicationOptionsStore so they survive a restart.
    internal async Task OpenOptionsAsync()
    {
        var dialog = new OptionsDialog(_options);
        await dialog.ShowDialog(this);
        if (dialog.Result is not { } edited)
            return;

        _optionsRuntime.ApplyAndPersist(
            edited,
            _ => _optionsStore.Save(_options));
    }

    private async Task<bool> FileSaveAsync()
    {
        var opensSaveAsPicker = _fileSession.CurrentPath is null;
        try
        {
            return (await _fileSession.SaveAsync()).Succeeded;
        }
        finally
        {
            if (opensSaveAsPicker)
                RestoreOwnerFocus();
        }
    }

    private async Task<bool> FileSaveAsAsync()
    {
        try
        {
            return (await _fileSession.SaveAsAsync()).Succeeded;
        }
        finally
        {
            RestoreOwnerFocus();
        }
    }

    private async Task<bool> FileExportPdfAsync() =>
        (await _fileSession.ExportPdfAsync()).Succeeded;

    private async Task<bool> FileExportNotesPagePdfAsync()
    {
        var range = new PresentationSlideRangeRequest(PresentationSlideRangeKind.AllSlides);
        var result = await _fileSession.ExportNotesPagePdfAsync(range);
        LastNotesPagePdfRenderPlan = _fileSession.LastNotesPagePdfRenderPlan;
        return result.Succeeded;
    }

    internal async Task<PresentationImageExportResult> FileExportImagesToFolder(
        string outputDirectory,
        PresentationSlideRangeRequest? range = null)
    {
        var result = await _fileSession.ExportImagesToFolderAsync(outputDirectory, range);
        if (!result.Succeeded || _fileSession.LastImageExportResult is null)
            throw result.Error?.Exception ?? new InvalidOperationException(result.Message);
        return _fileSession.LastImageExportResult;
    }

    private async Task<bool> FileExportImagesAsync() =>
        (await _fileSession.ExportImagesAsync()).Succeeded;

    internal PresentationHandoutLayoutPlan RefreshHandoutLayoutPlan(int? slidesPerPage = null)
    {
        LastHandoutLayoutPlan = _fileSession.BuildHandoutLayoutPlan(slidesPerPage);
        _statusText.Text = PresentationShellTextCatalog.Resolve(LastHandoutLayoutPlan.StatusText);
        return LastHandoutLayoutPlan;
    }

    internal PresentationNotesPagePdfRenderPlan RefreshNotesPagePdfRenderPlan(PresentationSlideRangeRequest? range = null)
    {
        LastNotesPagePdfRenderPlan = _fileSession.BuildNotesPagePdfRenderPlan(range);
        _statusText.Text = PresentationShellTextCatalog.Resolve(LastNotesPagePdfRenderPlan.StatusText);
        return LastNotesPagePdfRenderPlan;
    }

    internal PresentationPrintOutputPackage RefreshPrintOutputPackage(PresentationPrintRequest? request = null)
    {
        LastPrintOutputPackage = _fileSession.BuildPrintOutputPackage(request);
        LastPrintExecutionDescriptor = _fileSession.LastPrintExecutionDescriptor;
        LastNativePrintHandoffPlan = _fileSession.LastNativePrintHandoffPlan;
        _statusText.Text = LastPrintOutputPackage.Plan.DisabledReason ??
            LastNativePrintHandoffPlan!.Reason;
        return LastPrintOutputPackage;
    }

    internal PresentationNativePrintHandoffPlan RefreshNativePrintHandoffPlan(PresentationPrintRequest? request = null)
    {
        LastNativePrintHandoffPlan = _fileSession.ExecuteNativePrintHandoff(request);
        LastPrintOutputPackage = _fileSession.LastPrintOutputPackage;
        LastPrintExecutionDescriptor = _fileSession.LastPrintExecutionDescriptor;
        _statusText.Text = LastNativePrintHandoffPlan.Reason;
        return LastNativePrintHandoffPlan;
    }

    internal PresentationPrintBackstagePlan RefreshPrintBackstagePlan(PresentationPrintRequest? request = null)
    {
        LastPrintBackstagePlan = _fileSession.BuildPrintBackstagePlan(request);
        LastNativePrintHandoffPlan = _fileSession.LastNativePrintHandoffPlan;
        _statusText.Text = LastPrintBackstagePlan.DisabledReason ??
            LastPrintBackstagePlan.NativePrintHandoff.Reason;
        return LastPrintBackstagePlan;
    }

    internal PresentationPrintBackstagePlan ShowPrintOptionsPane(PresentationPrintRequest? request = null)
    {
        var plan = RefreshPrintBackstagePlan(request);
        _printOptionsPaneRequest = PresentationBackstagePrintRequestPlanner.BuildRequest(plan);
        RenderPrintOptionsPane(plan);
        _printOptionsPaneHost.IsVisible = true;
        return plan;
    }


    private async Task<PrintSubmissionResult> ExecutePrintWorkflowAsync(
        PresentationPrintRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        await _fileSession.PrintAsync(request, cancellationToken).ConfigureAwait(true);
        return LastPrintSubmissionResult ?? new PrintSubmissionResult(
            PrintSubmissionStatus.Cancelled,
            _selectedPrinterName);
    }

    private async Task<PrintSubmissionResult> ExecutePrintWorkflowCoreAsync(
        PresentationPrintRequest request,
        Func<PresentationPrintRequest, PresentationPrintOutputPackage> buildPackage,
        CancellationToken cancellationToken,
        bool promptForSelection = true)
    {
        var requestedSelection = new PrintSelection(
            _selectedPrinterName,
            request.Copies,
            PrintPageRange.All,
            PrintOrientation.Document,
            request.Collate,
            JobTitle: PresentationFileTextResources.NormalizePrintJobName(
                LastNativePrintHandoffPlan?.SuggestedPrintJobName));
        PrintSelection? selectedSelection = null;
        string? packageFailureMessage = null;
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _printCancellation = linkedCancellation;
        try
        {
            var execution = await _portablePrintWorkflow.ExecuteAsync(
                async (intent, token) =>
                {
                    selectedSelection = promptForSelection
                        ? await _showPrintSelectionDialog(
                            this,
                            intent.Discovery,
                            intent.RequestedSelection,
                            token).ConfigureAwait(true)
                        : intent.RequestedSelection;
                    return selectedSelection;
                },
                async (output, _, token) =>
                {
                    var selection = selectedSelection ?? requestedSelection;
                    _selectedPrinterName = selection.PrinterName;
                    _lastPrintSelection = selection;
                    var package = buildPackage(request with
                    {
                        Copies = selection.Copies,
                        Collate = selection.Collate,
                    });
                    LastPrintOutputPackage = package;
                    LastPrintExecutionDescriptor = _fileSession.LastPrintExecutionDescriptor;
                    LastNativePrintHandoffPlan = _fileSession.LastNativePrintHandoffPlan;
                    var validation = package is null
                        ? null
                        : PresentationPrintOutputPackageExecutor.ValidatePackage(package);
                    if (validation?.IsValid != true)
                    {
                        packageFailureMessage = validation?.FailureReason ??
                            PresentationNativeCommandOutcomePlanner.PrintPackageNotBuiltFailure;
                        throw new InvalidDataException(packageFailureMessage);
                    }

                    await output.WriteAsync(package!.Bytes, token).ConfigureAwait(true);
                },
                requestedSelection,
                linkedCancellation.Token).ConfigureAwait(true);
            _latestPrinterDiscovery = execution.Discovery ?? _latestPrinterDiscovery;
            LastPrintSubmissionResult = execution.Submission ?? new PrintSubmissionResult(
                PrintSubmissionStatus.Failed,
                selectedSelection?.PrinterName ?? requestedSelection.PrinterName,
                Message: execution.Operation.Exception?.Message);
        }
        finally
        {
            if (ReferenceEquals(_printCancellation, linkedCancellation))
                _printCancellation = null;
        }

        _statusText.Text = packageFailureMessage is null
            ? PresentationNativeCommandOutcomePlanner.BuildPrintStatusText(LastPrintSubmissionResult!)
            : PresentationNativeCommandOutcomePlanner.BuildPrintPackageFailureStatus(packageFailureMessage);

        return LastPrintSubmissionResult;
    }

    internal void HidePrintOptionsPane()
    {
        if (_printOptionsPaneHost is not null)
            _printOptionsPaneHost.IsVisible = false;
    }

    private void RenderPrintOptionsPane(PresentationPrintBackstagePlan plan)
    {
        var surface = PresentationBackstagePrintSurfacePlanner.Build(plan);
        _printOptionsPaneHeading.Text = surface.Heading;
        _printOptionsPaneMessage.Text = surface.Description;
        _printOptionsPaneRenderedOptionLines.Clear();
        _printOptionsPaneRenderedPreviewRows.Clear();
        _printOptionsPaneRenderedLayoutRows.Clear();
        _printOptionsPaneRenderedRangeRows.Clear();
        _printOptionsPaneRowsPanel.Children.Clear();

        AddPrintOptionsPaneSection(surface.SettingsHeading);
        foreach (var field in surface.Settings)
            AddPrintOptionsPaneField(field.Label, field.Value);
#if FREEP_WINDOWS_CAPTURE
        AddWindowsPrinterSelector(surface.NativePrint);
#endif

        foreach (var group in surface.ChoiceGroups)
        {
            AddPrintOptionsPaneSection(group.Heading);
            foreach (var choice in group.Choices)
            {
                var row = choice.DisplayText;
                AddPrintOptionsPaneRenderedChoice(group.Kind, row);
                AddPrintOptionsPaneChoice(row, choice.IsAvailable);
            }
        }

        AddPrintOptionsPaneSection(surface.CustomRangeHeading);
        _printOptionsPaneRowsPanel.Children.Add(new TextBlock
        {
            Text = surface.CustomRangeDescription,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70)),
            Margin = new Thickness(0, 0, 0, 4),
        });
        _printCustomRangeInput = new TextBox
        {
            Text = surface.CustomRangeText,
            PlaceholderText = surface.CustomRangePlaceholder,
            MinWidth = 240,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 6),
        };
        AutomationProperties.SetAutomationId(
            _printCustomRangeInput,
            surface.CustomRangeInputAutomationId);
        _printCustomRangeApplyButton = new Button
        {
            Content = surface.CustomRangeApplyLabel,
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 6),
        };
        AutomationProperties.SetAutomationId(
            _printCustomRangeApplyButton,
            surface.CustomRangeApplyAutomationId);
        _printCustomRangeApplyButton.Click += (_, _) =>
        {
            var currentRequest = _printOptionsPaneRequest ??
                PresentationBackstagePrintRequestPlanner.BuildRequest(plan);
            ShowPrintOptionsPane(PresentationBackstagePrintRequestPlanner.WithCustomRange(
                currentRequest,
                _printCustomRangeInput.Text));
        };
        _printOptionsPaneRowsPanel.Children.Add(_printCustomRangeInput);
        _printOptionsPaneRowsPanel.Children.Add(_printCustomRangeApplyButton);

        _printOptionsPaneRowsPanel.Children.Add(new TextBlock
        {
            Text = surface.StatusText,
            TextWrapping = TextWrapping.Wrap,
            FontStyle = FontStyle.Italic,
            Foreground = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70)),
            Margin = new Thickness(0, 8, 0, 0),
        });
        _printOptionsPaneExecuteButton.IsEnabled =
            PresentationBackstagePrintRequestPlanner.Validate(plan).CanPrint;
    }

    private void AddPrintOptionsPaneRenderedChoice(
        PresentationBackstagePrintChoiceGroupKind kind,
        string row)
    {
        switch (kind)
        {
            case PresentationBackstagePrintChoiceGroupKind.OutputOptions:
                _printOptionsPaneRenderedOptionLines.Add(row);
                break;
            case PresentationBackstagePrintChoiceGroupKind.Preview:
                _printOptionsPaneRenderedPreviewRows.Add(row);
                break;
            case PresentationBackstagePrintChoiceGroupKind.Layouts:
                _printOptionsPaneRenderedLayoutRows.Add(row);
                break;
            case PresentationBackstagePrintChoiceGroupKind.SlideRange:
                _printOptionsPaneRenderedRangeRows.Add(row);
                break;
        }
    }

    private void AddPrintOptionsPaneSection(string text)
    {
        _printOptionsPaneRowsPanel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = FreePBrushes.PaneHeadingText,
            Margin = new Thickness(0, 16, 0, 6),
        });
    }

#if FREEP_WINDOWS_CAPTURE
    private void AddWindowsPrinterSelector(PresentationNativePrintSurfacePlan surface)
    {
        if (!OperatingSystem.IsWindows())
            return;

        AddPrintOptionsPaneSection(PresentationShellTextCatalog.Resolve(surface.SectionHeading));
        _latestPrinterDiscovery = _printService.DiscoverAsync().GetAwaiter().GetResult();
        var printers = _latestPrinterDiscovery.Printers
            .Select(static printer => printer.Name)
            .ToArray();
        if (printers.Count == 0)
        {
            AddPrintOptionsPaneField(
                PresentationShellTextCatalog.Resolve(surface.QueueLabel),
                PresentationShellTextCatalog.Resolve(surface.NoQueuesStatus));
            return;
        }

        _nativePrinterPicker = new ComboBox
        {
            ItemsSource = printers,
            SelectedItem = _selectedPrinterName ?? _latestPrinterDiscovery.DefaultPrinter,
            MinWidth = 280,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 6),
        };
        AutomationProperties.SetAutomationId(_nativePrinterPicker, surface.PrinterPickerAutomationId);
        _nativePrinterPicker.SelectionChanged += (_, _) =>
        {
            if (_nativePrinterPicker.SelectedItem is string printerName)
                SelectWindowsPrinter(printerName, surface);
        };
        _printOptionsPaneRowsPanel.Children.Add(_nativePrinterPicker);

        var nativeDialogButton = new Button
        {
            Content = PresentationShellTextCatalog.Resolve(surface.NativeDialogLabel),
            MinWidth = 180,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 6),
        };
        AutomationProperties.SetAutomationId(nativeDialogButton, surface.NativeDialogAutomationId);
        nativeDialogButton.Click += (_, _) => ShowWindowsPrinterDialog(surface);
        _printOptionsPaneRowsPanel.Children.Add(nativeDialogButton);
    }

    private void ShowWindowsPrinterDialog(PresentationNativePrintSurfacePlan surface)
    {
        if (!WindowsNativePrintOutput.TryShowPrinterSelectionDialog(
                _selectedPrinterName ?? _latestPrinterDiscovery?.DefaultPrinter,
                out var selectedPrinter) ||
            string.IsNullOrWhiteSpace(selectedPrinter))
        {
            return;
        }

        SelectWindowsPrinter(selectedPrinter, surface);
        if (_nativePrinterPicker is not null)
            _nativePrinterPicker.SelectedItem = selectedPrinter;
    }

    private void SelectWindowsPrinter(
        string printerName,
        PresentationNativePrintSurfacePlan surface)
    {
        var normalized = printerName.Trim();
        var knownPrinter = _latestPrinterDiscovery?.Printers.FirstOrDefault(printer =>
            string.Equals(printer.Name, normalized, StringComparison.OrdinalIgnoreCase));
        if (knownPrinter is null)
        {
            _statusText.Text = PresentationNativeCommandOutcomePlanner.BuildPrintStatusText(
                new PrintSubmissionResult(
                    PrintSubmissionStatus.Failed,
                    normalized,
                    Message: PresentationShellTextCatalog.Resolve(
                        PresentationShellTextCatalog.WindowsPrinterQueueUnavailableStatus(normalized))));
            return;
        }

        _selectedPrinterName = knownPrinter.Name;
        _statusText.Text = PresentationShellTextCatalog.Resolve(
            surface.BuildPrinterSelectedStatus(knownPrinter.Name));
    }
#endif

    private void AddPrintOptionsPaneField(string label, string value)
    {
        var row = new Grid
        {
            Margin = new Thickness(0, 2),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(120) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
        };
        var name = new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70)),
        };
        var content = new TextBlock
        {
            Text = value,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = FreePBrushes.PaneHeadingText,
        };
        Grid.SetColumn(content, 1);
        row.Children.Add(name);
        row.Children.Add(content);
        _printOptionsPaneRowsPanel.Children.Add(row);
    }

    private void AddPrintOptionsPaneChoice(string row, bool isAvailable)
    {
        _printOptionsPaneRowsPanel.Children.Add(new TextBlock
        {
            Text = row,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = isAvailable
                ? new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70))
                : new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
        });
    }

    internal PresentationVideoExportPlan RefreshVideoExportPlan(PresentationVideoExportRequest? request = null)
    {
        LastVideoExportPlan = _fileSession.BuildVideoExportPlan(request);

        _statusText.Text = LastVideoExportPlan.DisabledReason ??
            PresentationShellTextCatalog.Resolve(LastVideoExportPlan.PlannedStatusText);
        return LastVideoExportPlan;
    }

    private async Task<bool> FileExportVideoAsync() =>
        (await _fileSession.ExportVideoAsync()).Succeeded;

    internal PresentationVideoFramePackage RefreshVideoFramePackage(PresentationVideoExportRequest? request = null)
    {
        LastVideoFramePackage = _fileSession.BuildVideoFramePackage(request);
        LastVideoExportPlan = _fileSession.LastVideoExportPlan;
        LastVideoExecutionDescriptor = _fileSession.LastVideoExecutionDescriptor;
        LastVideoExportHandoffPlan = _fileSession.LastVideoExportHandoffPlan;
        _statusText.Text = LastVideoExportHandoffPlan!.StatusText;
        return LastVideoFramePackage;
    }

    internal async Task<LinuxVideoExportResult> ExecuteVideoExportAsync(
        string outputPath,
        PresentationVideoExportRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _fileSession.ExportVideoToPathAsync(outputPath, request, cancellationToken);
        LastVideoFramePackage = _fileSession.LastVideoFramePackage;
        LastVideoExportPlan = _fileSession.LastVideoExportPlan;
        LastVideoExecutionDescriptor = _fileSession.LastVideoExecutionDescriptor;
        LastVideoExportHandoffPlan = _fileSession.LastVideoExportHandoffPlan;
        return LastVideoExportResult ?? LinuxVideoExportResult.Failed(
            result.Message ?? PresentationFileTextResources.VideoExportFailed,
            outputPath);
    }

    private static PresentationNativePrintHandoffHostCapabilities BuildNativePrintHostCapabilities(
        IPlatformPrintService printService)
    {
        var hostName = OperatingSystem.IsWindows()
            ? PresentationShellTextCatalog.Resolve(
                PresentationShellTextCatalog.AvaloniaWindowsPrintHostName)
            : PresentationShellTextCatalog.Resolve(
                PresentationShellTextCatalog.AvaloniaLinuxPrintHostName);
        if (!printService.IsSupported)
            return PresentationNativePrintHandoffHostCapabilities.Deferred(
                hostName,
                PresentationShellTextCatalog.Resolve(
                    PresentationShellTextCatalog.PrintHostUnavailableStatus(hostName)));

        return OperatingSystem.IsWindows()
            ? PresentationNativePrintHandoffHostCapabilities.Available(hostName)
            : PresentationNativePrintHandoffHostCapabilities.NativePrinterSubmissionAvailable(hostName);
    }

    private static LinuxNativeOutputCapabilities DetectNativeOutputCapabilities()
    {
#if FREEP_WINDOWS_CAPTURE
        if (OperatingSystem.IsWindows())
            return WindowsNativePrintOutput.Detect();
#endif
        return new LinuxNativeOutputCapabilityDetector().Detect();
    }

    private static IPlatformPrintService CreatePlatformPrintService() =>
        PlatformPrintServiceSelector.Select(
#if FREEP_WINDOWS_CAPTURE
            windowsFactory: static () => new WindowsPrintService(
                options: new WindowsPrintServiceOptions(
                    RequirePrinterDiscoveryBeforeSubmission: false,
                    RejectNonZeroHandlerExitCode: false)),
#else
            windowsFactory: null,
#endif
            cupsFactory: static () => new CupsPrintService());

    private static Task<PrintSelection?> ShowPlatformPrintSelectionDialogAsync(
        Window owner,
        PrinterDiscoveryResult discovery,
        PrintSelection? requested,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
#if FREEP_WINDOWS_CAPTURE
        if (OperatingSystem.IsWindows())
        {
            var currentPrinter = requested?.PrinterName ?? discovery.DefaultPrinter;
            return Task.FromResult(
                WindowsNativePrintOutput.TryShowPrinterSelectionDialog(
                    currentPrinter,
                    out var selectedPrinter) &&
                !string.IsNullOrWhiteSpace(selectedPrinter)
                    ? (requested ?? new PrintSelection()) with { PrinterName = selectedPrinter }
                    : null);
        }
#endif
        return CupsPrintDialog.ShowAsync(
            owner,
            discovery,
            requested,
            cancellationToken: cancellationToken);
    }

    private static ILinuxVideoExportAdapter CreateVideoExportAdapter(
        LinuxVideoEncoderCapability capability)
    {
#if FREEP_WINDOWS_CAPTURE
        if (OperatingSystem.IsWindows())
            return WindowsNativePrintOutput.CreateVideoAdapter(capability);
#endif
        return new LinuxVideoExportAdapter(capability);
    }

    private static PresentationVideoExportHandoffHostCapabilities BuildVideoExportHostCapabilities(
        LinuxVideoEncoderCapability capability)
    {
        var isWindowsNative = string.Equals(
            capability.ExecutablePath,
            "windows-media-composition",
            StringComparison.Ordinal);
        return PresentationNativeCommandOutcomePlanner.BuildVideoExportHostCapabilities(
            isWindowsNative
                ? PresentationVideoExportHostProfile.AvaloniaWindows
                : PresentationVideoExportHostProfile.AvaloniaLinux,
            capability.CanEncodeMp4,
            capability.CanCaptureNarration,
            capability.CanCaptureCameraAndMedia,
            capability.CanMuxTimedCaptions,
            capability.Reason);
    }


    internal void RefreshReviewWorkflowPlans()
    {
        _reviewWorkflowSession.RefreshReviewWorkflowPlans();
        RefreshPaneAccessibilityMetadata();
    }

    private void RefreshVisibleReviewCommentsPane()
    {
        if (_reviewCommentsPaneHost is null || _reviewCommentsPanePanel is null
            || !_workareaSession.Panes.IsVisible(PresentationWorkareaPane.ReviewComments))
        {
            return;
        }

        // The shared session refreshes the plan, while this host owns the realized
        // controls. Keep an already-open pane attached to the active slide.
        _reviewWorkflowSession.ShowReviewCommentsPane();
    }

    internal PresentationCommentPanePlan ShowReviewCommentsPane()
    {
        _workareaSession.Panes.Show(PresentationWorkareaPane.ReviewComments);
        return _reviewWorkflowSession.ShowReviewCommentsPane();
    }

    private void ShowReviewCommentsPane(PresentationCommentPanePlan plan)
    {
        RenderCommentMarkers(plan.Comments);

        if (_reviewCommentsPaneHost is null || _reviewCommentsPanePanel is null)
            return;

        _reviewCommentsPanePanel.Children.Clear();
        _reviewCommentsPanePanel.Children.Add(BuildReviewCommentsPaneHeader(plan));
        _reviewCommentsPanePanel.Children.Add(BuildAddCommentInput());
        _reviewCommentsPanePanel.Children.Add(BuildReviewCommentActions(plan.Actions));

        if (plan.ShouldShowEmptyState)
        {
            _reviewCommentsPanePanel.Children.Add(new TextBlock
            {
                Text       = plan.EmptyStateMessage,
                Foreground = FreePBrushes.PaneMutedText,
                Margin     = new Thickness(12, 0, 12, 10),
            });
        }
        else
        {
            foreach (var (comment, itemIndex) in plan.Comments.Select((comment, index) => (comment, index)))
            {
                var card = BuildReviewCommentCard(comment, itemIndex, plan.SaveEditAction);
                _reviewCommentsPanePanel.Children.Add(card);
            }
        }

        _reviewCommentsPaneHost.IsVisible = _workareaSession.Panes.ResolveVisibility(
            PresentationWorkareaPane.ReviewComments,
            plan.Comments.Count > 0,
            PresentationWorkareaPaneVisibilityPolicy.RequestedOrContent).Current.IsVisible;
        RefreshPaneAccessibilityMetadata();
    }

    /// <summary>
    /// Materializes the shared comment-anchor marker plan as Avalonia controls.
    /// Geometry and semantic metadata remain owned by Presentation code.
    /// </summary>
    private void RenderCommentMarkers(IReadOnlyList<PresentationCommentDescriptor> comments)
    {
        if (_commentOverlay is null)
            return;

        _commentOverlay.Children.Clear();
        var markers = PresentationCommentMarkerLayoutPlanner.Build(
            comments,
            _commentOverlay.Bounds.Width,
            _commentOverlay.Bounds.Height,
            _presentation.SlideSizeCxEmu,
            _presentation.SlideSizeCyEmu);

        foreach (var marker in markers)
        {
            var dot = new Border
            {
                Width = marker.Bounds.Width,
                Height = marker.Bounds.Height,
                CornerRadius = new CornerRadius(marker.Bounds.Width / 2),
                Background = marker.IsSelected ? FreePBrushes.AccentDark : FreePBrushes.Accent,
                BorderBrush = FreePBrushes.White,
                BorderThickness = new Thickness(marker.BorderThickness),
                IsHitTestVisible = false,
            };
            AutomationProperties.SetAutomationId(dot, marker.AutomationId);
            AutomationProperties.SetName(dot, marker.ToolTip);
            ToolTip.SetTip(dot, marker.ToolTip);
            Canvas.SetLeft(dot, marker.Bounds.X);
            Canvas.SetTop(dot, marker.Bounds.Y);
            _commentOverlay.Children.Add(dot);
        }
    }

    private Control BuildReviewCommentsPaneHeader(PresentationCommentPanePlan plan)
    {
        var summaryRow = new DockPanel
        {
            LastChildFill = true,
        };
        var close = new Button
        {
            Content  = plan.CloseAction.Label,
            IsEnabled = plan.CloseAction.IsEnabled,
            MinWidth = PresentationCommentPaneVisualMetrics.CloseMinimumWidth,
            MinHeight = 0,
            Height   = PresentationCommentPaneVisualMetrics.CompactControlHeight,
            FontSize = PresentationCommentPaneVisualMetrics.CompactControlFontSize,
            Padding  = new Thickness(8, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Tag      = PresentationSemanticIdentityCatalog.CommentsPaneCloseTag,
            Margin   = new Thickness(6, 0, 0, 6),
        };
        close.Click += (_, _) => HideReviewCommentsPane();
        DockPanel.SetDock(close, Dock.Right);
        summaryRow.Children.Add(close);
        summaryRow.Children.Add(new TextBlock
        {
            Text              = plan.HeaderSummaryText,
            FontSize          = PresentationCommentPaneVisualMetrics.SummaryFontSize,
            FontWeight        = FontWeight.SemiBold,
            Foreground        = FreePBrushes.PaneText,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 0, 6),
        });

        return new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing     = 0,
            Children =
            {
                summaryRow,
                new TextBlock
                {
                    Text       = plan.FilterOptionsSummaryText,
                    FontSize   = PresentationCommentPaneVisualMetrics.FilterFontSize,
                    Foreground = FreePBrushes.PaneMutedText,
                    Margin     = new Thickness(0, 0, 0, 6),
                },
            },
        };
    }

    internal void HideReviewCommentsPane()
    {
        _workareaSession.Panes.Hide(PresentationWorkareaPane.ReviewComments);
        if (_reviewCommentsPaneHost is not null)
            _reviewCommentsPaneHost.IsVisible = false;
        RefreshPaneAccessibilityMetadata();
    }

    private Control BuildReviewCommentActions(IReadOnlyList<PresentationReviewWorkflowActionPlan> actions)
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
                Content   = action.Label,
                IsEnabled = action.IsEnabled,
                Tag       = action.CommandId,
                MinWidth  = 88,
                Margin    = new Thickness(0, 0, 6, 6),
            };
            button.Click += (_, _) => ExecuteReviewCommentAction(action.CommandId);
            panel.Children.Add(button);
        }

        return panel;
    }

    private static IEnumerable<Button> EnumerateReviewPaneButtons(Control? control)
    {
        if (control is null)
        {
            yield break;
        }

        if (control is Button button)
        {
            yield return button;
        }

        if (control is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                foreach (var descendant in EnumerateReviewPaneButtons(child))
                {
                    yield return descendant;
                }
            }
        }
        else if (control is ContentControl { Content: Control content })
        {
            foreach (var descendant in EnumerateReviewPaneButtons(content))
            {
                yield return descendant;
            }
        }
        else if (control is Decorator { Child: Control child })
        {
            foreach (var descendant in EnumerateReviewPaneButtons(child))
            {
                yield return descendant;
            }
        }
    }

    private static IEnumerable<string> EnumerateReviewPaneText(Control? control)
    {
        if (control is null)
        {
            yield break;
        }

        if (control is TextBlock textBlock)
        {
            yield return textBlock.Text ?? string.Empty;
        }

        if (control is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                foreach (var text in EnumerateReviewPaneText(child))
                {
                    yield return text;
                }
            }
        }
        else if (control is ContentControl { Content: Control content })
        {
            foreach (var text in EnumerateReviewPaneText(content))
            {
                yield return text;
            }
        }
        else if (control is Decorator { Child: Control child })
        {
            foreach (var text in EnumerateReviewPaneText(child))
            {
                yield return text;
            }
        }
    }

    private Control BuildAddCommentInput()
    {
        var input = new TextBox
        {
            MinWidth = PresentationCommentPaneVisualMetrics.AddCommentInputMinimumWidth,
            MinHeight = 0,
            Height   = PresentationCommentPaneVisualMetrics.CompactControlHeight,
            FontSize = PresentationCommentPaneVisualMetrics.CompactControlFontSize,
            Padding  = new Thickness(4, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin   = new Thickness(0, 0, 6, 0),
        };
        var button = new Button
        {
            Content  = PresentationPaneTextResources.NewCommentCommand,
            MinWidth = PresentationCommentPaneVisualMetrics.AddCommentButtonMinimumWidth,
            MinHeight = 0,
            Height   = PresentationCommentPaneVisualMetrics.CompactControlHeight,
            FontSize = PresentationCommentPaneVisualMetrics.CompactControlFontSize,
            Padding  = new Thickness(8, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        button.Click += (_, _) => AddComment(input.Text);

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 0,
            Margin = new Thickness(0, 0, 0, 8),
            Children =
            {
                input,
                button,
            }
        };
    }

    private Control BuildReviewCommentCard(
        PresentationCommentDescriptor comment,
        int itemIndex,
        PresentationReviewSurfaceActionPlan editAction)
    {
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(6, 4, 6, 0),
        };
        header.Children.Add(new Border
        {
            Background   = FreePBrushes.Accent,
            CornerRadius = new CornerRadius(3),
            Padding      = new Thickness(4, 1, 4, 1),
            Margin       = new Thickness(0, 0, 6, 0),
            Child        = new TextBlock
            {
                Text       = comment.InitialsBadgeText,
                FontSize   = PresentationCommentPaneVisualMetrics.StatusFontSize,
                Foreground = FreePBrushes.White,
            },
        });
        header.Children.Add(new TextBlock
        {
            Text              = comment.AuthorDisplayName,
            FontSize          = PresentationCommentPaneVisualMetrics.AuthorFontSize,
            FontWeight        = FontWeight.SemiBold,
            Foreground        = FreePBrushes.PaneHeadingText,
            VerticalAlignment = VerticalAlignment.Center,
        });
        header.Children.Add(new TextBlock
        {
            Text              = comment.ThreadStatusLabel,
            FontSize          = PresentationCommentPaneVisualMetrics.StatusFontSize,
            Foreground        = FreePBrushes.PaneMutedText,
            Margin            = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var card = new StackPanel
        {
            Orientation = Orientation.Vertical,
        };
        card.Children.Add(header);
        card.Children.Add(new TextBlock
        {
            Text         = comment.TextPreview,
            FontSize     = PresentationCommentPaneVisualMetrics.BodyFontSize,
            TextWrapping = TextWrapping.Wrap,
            Foreground   = FreePBrushes.PaneText,
            Margin       = new Thickness(16, 2, 6, 6),
        });
        if (comment.ShouldShowMentionDetail)
            AddMentionDetail(card, comment.MentionDetailSummary, new Thickness(0));
        if (comment.IsSelected && comment.CanEdit)
        {
            var editText = GetCommentText(comment.CommentIndex) ?? comment.TextPreview;
            var editInput = new TextBox
            {
                Text = editText,
                CaretIndex = editText.Length,
                MinWidth = PresentationCommentPaneVisualMetrics.AddCommentInputMinimumWidth,
                MinHeight = 0,
                Margin   = new Thickness(16, 0, 6, 6),
                Height   = PresentationCommentPaneVisualMetrics.CompactControlHeight,
                FontSize = PresentationCommentPaneVisualMetrics.CompactControlFontSize,
                Padding  = new Thickness(4, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            var mentionButton = BuildCommentMentionButton(
                PresentationSemanticIdentityCatalog.CommentMentionEditTag,
                () => editInput.Text,
                () => editInput.CaretIndex,
                PresentationReviewWorkflowIntentKind.EditComment);
            var editButton = new Button
            {
                Content = editAction.Label,
                IsEnabled = editAction.IsEnabled,
                MinWidth = 72,
                MinHeight = 0,
                Height   = PresentationCommentPaneVisualMetrics.CompactControlHeight,
                FontSize = PresentationCommentPaneVisualMetrics.CompactControlFontSize,
                Padding  = new Thickness(8, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin   = new Thickness(0, 0, 6, 6),
            };
            editButton.Click += (_, _) => EditSelectedComment(editInput.Text);
            card.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    editInput,
                    mentionButton,
                    editButton,
                }
            });
        }
        foreach (var reply in comment.Replies)
        {
            card.Children.Add(new TextBlock
            {
                Text         = reply.DisplayText,
                TextWrapping = TextWrapping.Wrap,
                FontSize     = PresentationCommentPaneVisualMetrics.ReplyFontSize,
                Margin       = new Thickness(26, 0, 6, 4),
                Foreground   = FreePBrushes.PaneSecondaryText,
            });
            if (reply.ShouldShowMentionDetail)
                AddMentionDetail(card, reply.MentionDetailSummary, new Thickness(26, 0, 6, 4));
        }
        if (comment.IsSelected && comment.CanReply)
        {
            var replyInput = new TextBox
            {
                PlaceholderText = PresentationPaneTextResources.ReplyCommand,
                MinWidth        = 180,
                MinHeight       = 0,
                Height          = PresentationCommentPaneVisualMetrics.CompactControlHeight,
                FontSize        = PresentationCommentPaneVisualMetrics.CompactControlFontSize,
                Padding         = new Thickness(4, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin          = new Thickness(0, 0, 6, 0),
            };
            var mentionButton = BuildCommentMentionButton(
                PresentationSemanticIdentityCatalog.CommentMentionReplyTag,
                () => replyInput.Text,
                () => replyInput.CaretIndex,
                PresentationReviewWorkflowIntentKind.ReplyComment);
            var replyButton = new Button
            {
                Content = PresentationPaneTextResources.ReplyCommand,
                MinWidth = 58,
                MinHeight = 0,
                Height   = PresentationCommentPaneVisualMetrics.CompactControlHeight,
                FontSize = PresentationCommentPaneVisualMetrics.CompactControlFontSize,
                Padding  = new Thickness(8, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            replyButton.Click += (_, _) => ReplyToSelectedComment(replyInput.Text);
            card.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin      = new Thickness(16, 0, 6, 6),
                Children    =
                {
                    replyInput,
                    mentionButton,
                    replyButton,
                }
            });
        }

        var border = new Border
        {
            Background      = comment.IsSelected ? FreePBrushes.SelectedCommentSurface : FreePBrushes.PaneSurface,
            BorderBrush     = comment.IsSelected ? FreePBrushes.Accent : FreePBrushes.CardBorder,
            BorderThickness = new Thickness(comment.IsSelected ? 2 : 1),
            CornerRadius    = new CornerRadius(4),
            Margin          = new Thickness(0, 0, 0, PresentationCommentPaneVisualMetrics.CardBottomMargin),
            Child           = card,
        };
        border.Cursor = new Cursor(StandardCursorType.Hand);
        border.PointerPressed += (_, _) => SelectReviewComment(comment.CommentIndex);
        PresentationPaneAccessibilityAdapter.ApplyItem(
            border,
            PresentationPaneAccessibilityPlanner.PlanItem(
                PresentationPaneAccessibilityPlanner.CommentsPaneId,
                itemIndex,
                comment.TextPreview,
                comment.IsSelected,
                comment.AccessibilityKey));
        return border;
    }

    private static void AddMentionDetail(StackPanel card, string mentionDetailSummary, Thickness margin)
    {
        card.Children.Add(new TextBlock
        {
            Text = mentionDetailSummary,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
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
            IsEnabled = mentionPicker.HasCandidates,
            Tag = tag,
            MinWidth = 72,
        };
        button.Click += (_, _) =>
        {
            var dispatch = _reviewWorkflowSession.DispatchCommentMentionPicker(
                intent,
                getText(),
                getCaretIndex());
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
                menu.Open(button);
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

    private void ExecuteReviewCommentAction(string commandId)
    {
        if (commandId == PresentationReviewWorkflowPlanner.AddCommentCommandId)
        {
            AddComment(PresentationPaneTextResources.NewCommentDefault);
        }
        else if (commandId == PresentationReviewWorkflowPlanner.EditCommentCommandId)
        {
            EditSelectedComment(GetSelectedCommentText());
        }
        else if (commandId == PresentationReviewWorkflowPlanner.ResolveCommentCommandId)
        {
            ResolveSelectedComment();
        }
        else if (commandId == PresentationReviewWorkflowPlanner.ReopenCommentCommandId)
        {
            ReopenSelectedComment();
        }
        else if (commandId == PresentationReviewWorkflowPlanner.DeleteCommentCommandId)
        {
            DeleteSelectedComment();
        }
        else if (commandId == PresentationReviewWorkflowPlanner.PreviousCommentCommandId)
        {
            NavigateReviewComment(PresentationReviewWorkflowIntentKind.PreviousComment);
        }
        else if (commandId == PresentationReviewWorkflowPlanner.NextCommentCommandId)
        {
            NavigateReviewComment(PresentationReviewWorkflowIntentKind.NextComment);
        }
    }


    private void SelectReviewComment(int commentIndex) => _reviewWorkflowSession.SelectReviewComment(commentIndex);
    internal PresentationCommentNavigationPlan NavigateReviewComment(PresentationReviewWorkflowIntentKind intent) => _reviewWorkflowSession.NavigateReviewComment(intent);
    internal PresentationCommentMutationPlan DeleteSelectedComment() => _reviewWorkflowSession.DeleteSelectedComment();
    internal PresentationCommentMutationPlan AddComment(string? text, DateTime? timestamp = null, string? author = null, string? initials = null, long xemu = 0, long yemu = 0) => _reviewWorkflowSession.AddComment(text, timestamp, author, initials, xemu, yemu);
    internal PresentationCommentMutationPlan EditSelectedComment(string? text, string? author = null, string? initials = null) => _reviewWorkflowSession.EditSelectedComment(text, author, initials);
    internal PresentationCommentMutationPlan ResolveSelectedComment(DateTime? resolvedAt = null, string? resolvedBy = null) => _reviewWorkflowSession.ResolveSelectedComment(resolvedAt, resolvedBy);
    internal PresentationCommentMutationPlan ReopenSelectedComment() => _reviewWorkflowSession.ReopenSelectedComment();
    internal PresentationCommentMutationPlan ReplyToSelectedComment(string? text, DateTime? timestamp = null, string? author = null, string? initials = null) => _reviewWorkflowSession.ReplyToSelectedComment(text, timestamp, author, initials);




    private string? GetSelectedCommentText() => _reviewWorkflowSession.GetSelectedCommentText();

    private string? GetCommentText(int commentIndex) => _reviewWorkflowSession.GetCommentText(commentIndex);

    private void OnAnimationPaneRequested(PresentationAnimationCommandPlan plan)
    {
        CoordinateAnimationPaneRequestObserver();
        _ = plan;
        if (IsAnimationPaneVisible)
            HideAnimationPane();
        else
            ShowAnimationPane();
    }

    internal AnimationPaneTimelinePlan RefreshAnimationPaneTimelinePlan(int selectedAnimationIndex = -1)
    {
        var plan = _animationPaneSession.Refresh(selectedAnimationIndex);
        RefreshPaneAccessibilityMetadata();
        return plan;
    }

    internal AnimationPaneTimelinePlan ShowAnimationPane(int selectedAnimationIndex = -1)
    {
        var plan = RefreshAnimationPaneTimelinePlan(selectedAnimationIndex);
        RenderAnimationPane(plan);
        _animationPaneHost.IsVisible = true;
        SyncRibbonCommandStates();
        RefreshPaneAccessibilityMetadata();
        return plan;
    }

    internal void HideAnimationPane()
    {
        if (_animationPaneHost is not null)
            _animationPaneHost.IsVisible = false;
        SyncRibbonCommandStates();
        RefreshPaneAccessibilityMetadata();
    }

    private void RefreshVisibleAnimationPane(int selectedAnimationIndex = -1)
    {
        if (!IsAnimationPaneVisible)
            return;

        var plan = RefreshAnimationPaneTimelinePlan(selectedAnimationIndex);
        RenderAnimationPane(plan);
    }

    private void RenderAnimationPane(AnimationPaneTimelinePlan plan)
    {
        var viewPlan = _animationPaneSession.WorkflowEvidence!.View;
        _animationPaneHeading.Text = _animationPaneSession.ControlSchema.Heading;
        _animationPaneMessage.Text = viewPlan.Message;
        RenderAnimationPanePlaybackControls(plan, viewPlan);

        _animationPaneRenderedRows.Clear();
        _animationPaneItemsPanel.Children.Clear();
        _animationPaneEffectOptionControlCount = 0;
        _animationPaneTriggerControlCount = 0;
        _animationPaneDurationControlCount = 0;
        _animationPaneDelayControlCount = 0;
        _paneAccessibility.ApplyPane(
            _animationPaneHost,
            PresentationPaneAccessibilityPlanner.AnimationPaneId,
            _animationPaneHost.IsVisible,
            plan.Items.Count,
            plan.SelectedIndex);
        if (!plan.HasAnimations)
        {
            _animationPaneItemsPanel.Children.Add(new TextBlock
            {
                Text = viewPlan.EmptyMessage,
                FontSize = 11,
                Foreground = FreePBrushes.PaneMutedText,
                Margin = new Thickness(10, 12, 10, 12),
                TextWrapping = TextWrapping.Wrap,
            });
            RefreshPaneAccessibilityMetadata();
            return;
        }

        for (var i = 0; i < plan.Items.Count; i++)
        {
            var item = plan.Items[i];
            _animationPaneRenderedRows.Add(viewPlan.RowSummaries[i]);
            var card = BuildAnimationPaneItemCard(item);
            PresentationPaneAccessibilityAdapter.ApplyItem(
                card,
                PresentationPaneAccessibilityPlanner.PlanItem(
                    PresentationPaneAccessibilityPlanner.AnimationPaneId,
                    i,
                    item.ShapeName,
                    item.IsSelected,
                    PresentationPaneAccessibilityPlanner.BuildAnimationKey(item.ShapeId, item.Index)));
            _animationPaneItemsPanel.Children.Add(card);
        }
        RefreshPaneAccessibilityMetadata();
    }

    private void RenderAnimationPanePlaybackControls(
        AnimationPaneTimelinePlan plan,
        AnimationPaneWorkflowViewPlan viewPlan)
    {
        _animationPanePlaybackControlsPanel.Children.Clear();
        _animationPaneRenderedPlaybackControls.Clear();
        for (var i = 0; i < plan.PlaybackControls.Count; i++)
        {
            var control = plan.PlaybackControls[i];
            var button = new Button
            {
                Content = control.Label,
                IsEnabled = control.IsEnabled,
                Padding = new Thickness(6, 2),
                Margin = new Thickness(0, 4, 6, 4),
                Background = FreePBrushes.AccentDark,
                Foreground = FreePBrushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 12,
                Tag = control.CommandId,
            };
            ToolTip.SetTip(button, control.DisabledReason ?? control.ToolTip);
            button.Click += (_, _) => ExecuteAnimationPanePlaybackControl(control);
            _animationPanePlaybackControlsPanel.Children.Add(button);
            _animationPaneRenderedPlaybackControls.Add(viewPlan.PlaybackControlSummaries[i]);

            if (control.Kind == AnimationPanePlaybackControlKind.PreviewCurrentSlide)
                _animationPanePreviewButton = button;
        }
    }

    private void ExecuteAnimationPanePlaybackControl(AnimationPanePlaybackControlDescriptor control)
        => ExecuteAnimationPanePlaybackControl(control, startPreview: true);


    private AnimationPanePlaybackSessionPlan ExecuteAnimationPanePlaybackControl(
        AnimationPanePlaybackControlDescriptor control,
        bool startPreview)
    {
        var transition = _animationPaneSession.ExecutePlayback(control.Kind);
        RefreshVisibleAnimationPane(_animationPaneSession.SelectedAnimationIndex);

        if (startPreview && transition.ShouldStartPreview)
            StartAnimationPanePreview(transition.Playback);

        return transition.Playback;
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

    private Control BuildAnimationPaneItemCard(AnimationPaneTimelineItemPlan item)
    {
        var controls = _animationPaneSession.BuildItemControlPlan(item, canEditMotionPath: true);

        var effectOptionCombo = new ComboBox
        {
            ItemsSource = controls.EffectOptions.Options.Select(option => option.Label).ToArray(),
            SelectedIndex = controls.EffectOptions.SelectedIndex,
            Width = 104,
            Height = 24,
            FontSize = 10,
            Margin = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = item.Index,
            IsEnabled = controls.EffectOptions.IsEnabled,
            IsVisible = controls.EffectOptions.IsVisible,
        };
        ToolTip.SetTip(effectOptionCombo, controls.EffectOptions.ToolTip);
        effectOptionCombo.SelectionChanged += (_, _) =>
        {
            if (controls.EffectOptions.ResolveOptionId(effectOptionCombo.SelectedIndex) is { } optionId)
                ApplyAnimationPaneEffectOptionEdit(item.Index, optionId);
        };
        if (controls.EffectOptions.IsVisible)
            _animationPaneEffectOptionControlCount++;

        var wheelSpokeCombo = new ComboBox
        {
            ItemsSource = controls.WheelSpokes.Options.Select(option => option.Label).ToArray(),
            SelectedIndex = controls.WheelSpokes.SelectedIndex,
            Width = 86,
            Height = 24,
            FontSize = 10,
            Margin = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = item.Index,
            IsEnabled = controls.WheelSpokes.IsEnabled,
            IsVisible = controls.WheelSpokes.IsVisible,
        };
        ToolTip.SetTip(wheelSpokeCombo, controls.WheelSpokes.ToolTip);
        wheelSpokeCombo.SelectionChanged += (_, _) =>
        {
            if (controls.WheelSpokes.ResolveOptionId(wheelSpokeCombo.SelectedIndex) is { } optionId)
                ApplyAnimationPaneEffectOptionEdit(item.Index, optionId);
        };
        if (controls.WheelSpokes.IsVisible)
            _animationPaneEffectOptionControlCount++;

        var triggerCombo = new ComboBox
        {
            ItemsSource = controls.Trigger.Options.Select(option => option.Label).ToArray(),
            SelectedIndex = controls.Trigger.SelectedIndex,
            Width = 110,
            Height = 24,
            FontSize = 10,
            Margin = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = item.Index,
        };
        ToolTip.SetTip(triggerCombo, controls.Trigger.ToolTip);
        triggerCombo.SelectionChanged += (_, _) =>
            ApplyAnimationPaneTriggerEdit(item.Index, triggerCombo.SelectedIndex);
        _animationPaneTriggerControlCount++;

        var durationBox = new TextBox
        {
            Text = controls.Duration.Text,
            Width = 48,
            Height = 24,
            FontSize = 10,
            Padding = new Thickness(2, 1),
            Margin = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = item.Index,
        };
        ToolTip.SetTip(durationBox, controls.Duration.Descriptor.ToolTip);
        durationBox.LostFocus += (_, _) =>
        {
            var plan = ApplyAnimationPaneDurationEdit(item.Index, durationBox.Text ?? string.Empty);
            if (!plan.ShouldApply)
                durationBox.Text = plan.DisplayText;
        };
        _animationPaneDurationControlCount++;

        var delayBox = new TextBox
        {
            Text = controls.Delay.Text,
            Width = 48,
            Height = 24,
            FontSize = 10,
            Padding = new Thickness(2, 1),
            Margin = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = item.Index,
        };
        ToolTip.SetTip(delayBox, controls.Delay.Descriptor.ToolTip);
        delayBox.LostFocus += (_, _) =>
        {
            var plan = ApplyAnimationPaneDelayEdit(item.Index, delayBox.Text ?? string.Empty);
            if (!plan.ShouldApply)
                delayBox.Text = plan.DisplayText;
        };

        TextBox? decelerationBox = null;
        var accelerationBox = new TextBox
        {
            Text = controls.SmoothStart.Text,
            Width = 48,
            Height = 24,
            FontSize = 10,
            Padding = new Thickness(2, 1),
            Margin = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = item.Index,
        };
        ToolTip.SetTip(accelerationBox, controls.SmoothStart.Descriptor.ToolTip);
        accelerationBox.LostFocus += (_, _) =>
        {
            var plan = ApplyAnimationPaneEasingEdit(
                item.Index,
                accelerationBox.Text ?? string.Empty,
                decelerationBox?.Text ?? string.Empty);
            if (!plan.ShouldApply)
                accelerationBox.Text = plan.AccelerationText;
        };

        decelerationBox = new TextBox
        {
            Text = controls.SmoothEnd.Text,
            Width = 48,
            Height = 24,
            FontSize = 10,
            Padding = new Thickness(2, 1),
            Margin = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = item.Index,
        };
        ToolTip.SetTip(decelerationBox, controls.SmoothEnd.Descriptor.ToolTip);
        decelerationBox.LostFocus += (_, _) =>
        {
            var plan = ApplyAnimationPaneEasingEdit(
                item.Index,
                accelerationBox.Text ?? string.Empty,
                decelerationBox.Text ?? string.Empty);
            if (!plan.ShouldApply)
                decelerationBox.Text = plan.DecelerationText;
        };
        _animationPaneDelayControlCount++;

        var repeatCombo = new ComboBox
        {
            ItemsSource = controls.Repeat.Options.Select(option => option.Label).ToArray(),
            SelectedIndex = controls.Repeat.SelectedIndex,
            Width = 82,
            Height = 24,
            FontSize = 10,
            Margin = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = item.Index,
        };
        ToolTip.SetTip(repeatCombo, controls.Repeat.ToolTip);

        var autoReverseCheck = new CheckBox
        {
            IsChecked = controls.AutoReverse.IsChecked,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2),
            Tag = item.Index,
        };
        ToolTip.SetTip(autoReverseCheck, controls.AutoReverse.Descriptor.ToolTip);

        void ApplyRepeat()
        {
            var plan = _animationPaneSession.ApplyRepeat(
                item.Index,
                repeatCombo.SelectedItem as string,
                autoReverseCheck.IsChecked == true);
            if (!plan.ShouldApply && plan.DisabledReason is not null)
            {
                repeatCombo.SelectedItem = plan.DisplayText;
                autoReverseCheck.IsChecked = plan.AutoReverse;
            }
        }

        repeatCombo.SelectionChanged += (_, _) => ApplyRepeat();
        autoReverseCheck.IsCheckedChanged += (_, _) => ApplyRepeat();

        var moveEarlierButton = BuildAnimationPaneActionButton(
            "▲",
            controls.MoveEarlier.IsEnabled,
            controls.MoveEarlier.ToolTip,
            () => MoveAnimationPaneItem(item.Index, -1));
        var moveLaterButton = BuildAnimationPaneActionButton(
            "▼",
            controls.MoveLater.IsEnabled,
            controls.MoveLater.ToolTip,
            () => MoveAnimationPaneItem(item.Index, 1));
        var removeButton = BuildAnimationPaneActionButton(
            "×",
            controls.Remove.IsEnabled,
            controls.Remove.ToolTip,
            () => RemoveAnimationPaneItem(item.Index));
        removeButton.Foreground = FreePBrushes.AnimationDanger;
        var paragraphBuildButton = BuildAnimationPaneActionButton(
            "¶",
            controls.ParagraphBuild.IsEnabled,
            controls.ParagraphBuild.ToolTip,
            () => ToggleParagraphBuild(item.ShapeId));
        var editMotionPathButton = controls.EditMotionPath.IsVisible
            ? BuildAnimationPaneActionButton(
                controls.EditMotionPath.Descriptor.Label,
                controls.EditMotionPath.IsEnabled,
                controls.EditMotionPath.ToolTip,
                () => _ = OpenMotionPathEditorAsync(item.Index))
            : null;
        var actionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        actionPanel.Children.Add(moveEarlierButton);
        actionPanel.Children.Add(moveLaterButton);
        actionPanel.Children.Add(paragraphBuildButton);
        if (editMotionPathButton is not null)
            actionPanel.Children.Add(editMotionPathButton);
        actionPanel.Children.Add(removeButton);

        var innerGrid = new Grid
        {
            VerticalAlignment = VerticalAlignment.Center,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(80) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };
        innerGrid.PointerPressed += (_, e) =>
        {
            SelectAnimationPaneItem(item.Index);
            e.Handled = true;
        };
        var orderLabel = new TextBlock
        {
            Text = item.OrderText,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = FreePBrushes.AnimationText,
            Width = 20,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(4, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var nameLabel = new TextBlock
        {
            Text = item.ShapeName,
            FontSize = 11,
            Foreground = FreePBrushes.AnimationText,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 80,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var effectLabel = new TextBlock
        {
            Text = item.EffectText,
            FontSize = 10,
            Foreground = FreePBrushes.PaneMutedText,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 70,
            Margin = new Thickness(4, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (var placement in new (Control Control, int Column)[]
                 {
                     (orderLabel, 0),
                     (nameLabel, 1),
                     (effectLabel, 2),
                     (effectOptionCombo, 3),
                     (wheelSpokeCombo, 4),
                     (triggerCombo, 5),
                     (durationBox, 6),
                     (delayBox, 7),
                     (repeatCombo, 8),
                     (autoReverseCheck, 9),
                     (accelerationBox, 10),
                     (decelerationBox, 11),
                     (actionPanel, 12),
                 })
        {
            Grid.SetColumn(placement.Control, placement.Column);
            innerGrid.Children.Add(placement.Control);
        }

        var border = new Border
        {
            Background = item.IsSelected
                ? FreePBrushes.AnimationSelectedSurface
                : FreePBrushes.PaneSurface,
            BorderBrush = FreePBrushes.GridBorder,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(4),
            Child = innerGrid,
        };
        border.Cursor = new Cursor(StandardCursorType.Hand);
        border.PointerPressed += (_, _) => SelectAnimationPaneItem(item.Index);
        return border;
    }

    private async Task OpenMotionPathEditorAsync(int animationIndex)
    {
        var dialog = new MotionPathEditorDialog(Editor, animationIndex);
        await dialog.ShowDialog<bool?>(this);
        RefreshVisibleAnimationPane(animationIndex);
    }

    private void ToggleParagraphBuild(uint shapeId)
    {
        var plan = _animationPaneSession.ToggleParagraphBuild(shapeId);
        if (plan.ShouldApply)
            RefreshVisibleAnimationPane(_animationPaneSession.SelectedAnimationIndex);
    }


    private static Button BuildAnimationPaneActionButton(
        string content,
        bool isEnabled,
        string toolTip,
        Action action)
    {
        var button = new Button
        {
            Content = content,
            FontSize = 9,
            Width = 18,
            Height = 18,
            Padding = new Thickness(0),
            Margin = new Thickness(1),
            Background = FreePBrushes.CardBorder,
            BorderBrush = FreePBrushes.PaneBorder,
            BorderThickness = new Thickness(1),
            IsEnabled = isEnabled,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(button, toolTip);
        button.Click += (_, _) => action();
        return button;
    }







    private AnimationPaneEffectOptionMutationPlan ApplyAnimationPaneEffectOptionEdit(
        int animationIndex,
        string optionId)
    {
        var plan = _animationPaneSession.ApplyEffectOption(animationIndex, optionId);
        if (plan.ShouldApply)
            RefreshVisibleAnimationPane(_animationPaneSession.SelectedAnimationIndex);
        return plan;
    }

    private AnimationPaneTimingMutationPlan ApplyAnimationPaneTriggerEdit(
        int animationIndex,
        int selectedTriggerIndex)
    {
        var plan = _animationPaneSession.ApplyTrigger(animationIndex, selectedTriggerIndex);
        RefreshAnimationPaneAfterTimingMutation(plan);
        return plan;
    }

    private AnimationPaneTimingMutationPlan ApplyAnimationPaneDurationEdit(
        int animationIndex,
        string text)
    {
        var plan = _animationPaneSession.ApplyDuration(animationIndex, text);
        RefreshAnimationPaneAfterTimingMutation(plan);
        return plan;
    }

    private AnimationPaneTimingMutationPlan ApplyAnimationPaneDelayEdit(
        int animationIndex,
        string text)
    {
        var plan = _animationPaneSession.ApplyDelay(animationIndex, text);
        RefreshAnimationPaneAfterTimingMutation(plan);
        return plan;
    }

    private AnimationPaneEasingMutationPlan ApplyAnimationPaneEasingEdit(
        int animationIndex,
        string accelerationText,
        string decelerationText)
    {
        var plan = _animationPaneSession.ApplyEasing(
            animationIndex,
            accelerationText,
            decelerationText);
        if (plan.ShouldApply)
            RefreshVisibleAnimationPane(_animationPaneSession.SelectedAnimationIndex);
        return plan;
    }

    private void RefreshAnimationPaneAfterTimingMutation(AnimationPaneTimingMutationPlan plan)
    {
        if (plan.ShouldApply)
            RefreshVisibleAnimationPane(_animationPaneSession.SelectedAnimationIndex);
    }

    private void SelectAnimationPaneItem(int animationIndex)
    {
        _animationPaneSession.SelectAnimation(animationIndex);
        RefreshVisibleAnimationPane(_animationPaneSession.SelectedAnimationIndex);
    }

    private AnimationPaneReorderMutationPlan MoveAnimationPaneItem(int animationIndex, int offset)
    {
        var plan = _animationPaneSession.MoveAnimation(animationIndex, offset);
        if (plan.ShouldApply)
            RefreshVisibleAnimationPane(_animationPaneSession.SelectedAnimationIndex);
        return plan;
    }


    private AnimationPaneRemoveMutationPlan RemoveAnimationPaneItem(int animationIndex)
    {
        var plan = _animationPaneSession.RemoveAnimation(animationIndex);
        if (plan.ShouldApply)
            RefreshVisibleAnimationPane(_animationPaneSession.SelectedAnimationIndex);

        return plan;
    }

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

    private Control BuildAccessibilityCheckerRowCard(PresentationAccessibilityCheckerRowPlan row)
    {
        var action = new Button
        {
            Content = row.ActionLabel,
            FontFamily = AvaloniaCompactDialogChrome.WindowsUiFontFamily,
            FontSize = 12,
            Tag = row.RowIndex,
            Height = 20,
            MinWidth = 96,
            Padding = new Thickness(8, 0),
            CornerRadius = new CornerRadius(0),
            Background = FreePBrushes.DisabledSurface,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xAD, 0xAD, 0xAD)),
            BorderThickness = new Thickness(1),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
        };
        ToolTip.SetTip(action, row.CommandHint);
        action.Click += (_, _) => ApplyAccessibilityCheckerRowAction(row.RowIndex);

        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Children =
            {
                new TextBlock
                {
                    Text = row.DisplayTitle,
                    FontFamily = AvaloniaCompactDialogChrome.WindowsUiFontFamily,
                    FontSize = 12,
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = row.DisplayMetadata,
                    FontFamily = AvaloniaCompactDialogChrome.WindowsUiFontFamily,
                    FontSize = 12,
                    Foreground = FreePBrushes.PaneSecondaryText,
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = row.Detail,
                    FontFamily = AvaloniaCompactDialogChrome.WindowsUiFontFamily,
                    FontSize = 12,
                    Foreground = FreePBrushes.PaneText,
                    TextWrapping = TextWrapping.Wrap,
                },
                action,
            }
        };

        if (row.ShouldShowSelectionIndicator)
        {
            panel.Children.Insert(1, new TextBlock
            {
                Text = PresentationPaneTextResources.ProofingSelectedIssue,
                FontFamily = AvaloniaCompactDialogChrome.WindowsUiFontFamily,
                FontSize = 12,
                Foreground = FreePBrushes.Accent,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }

        var border = new Border
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
        border.PointerPressed += (_, _) => SelectAccessibilityCheckerRow(row.RowIndex);
        PresentationPaneAccessibilityAdapter.ApplyItem(
            border,
            PresentationPaneAccessibilityPlanner.PlanItem(
                PresentationPaneAccessibilityPlanner.AccessibilityPaneId,
                row.RowIndex,
                row.Title,
                row.IsSelected,
                row.AccessibilityKey));
        return border;
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
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        _accessibilityCheckerReviewDetailsPanel.Children.Add(new TextBlock
        {
            Text = display.Summary,
            Foreground = FreePBrushes.PaneText,
            TextWrapping = TextWrapping.Wrap,
        });
        _accessibilityCheckerReviewDetailsPanel.Children.Add(new TextBlock
        {
            Text = display.Guidance,
            Foreground = FreePBrushes.PaneSecondaryText,
            TextWrapping = TextWrapping.Wrap,
        });

        foreach (var detail in display.Details)
        {
            _accessibilityCheckerTableStructureReviewRenderedLines.Add(detail.RenderedLine);
            _accessibilityCheckerReviewDetailsPanel.Children.Add(BuildTableStructureReviewDetail(detail));
        }
    }

    private static Control BuildTableStructureReviewDetail(PresentationTableStructureReviewDetailRowPlan detail)
    {
        return new Border
        {
            Background = FreePBrushes.SubtlePaneSurface,
            BorderBrush = FreePBrushes.SubtlePaneBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Child = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 2,
                Children =
                {
                    new TextBlock
                    {
                        Text = detail.Category,
                        FontWeight = FontWeight.SemiBold,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new TextBlock
                    {
                        Text = detail.Summary,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new TextBlock
                    {
                        Text = detail.Detail,
                        Foreground = FreePBrushes.PaneSecondaryText,
                        TextWrapping = TextWrapping.Wrap,
                    },
                }
            },
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
            visible => _smartArtTextPaneHost.IsVisible = visible,
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
                ? new SmartArtTextPaneOutlineRow(box.Text ?? string.Empty, item.Level, item.IsAssistant, item.ModelId)
                : new SmartArtTextPaneOutlineRow(box.Text ?? string.Empty, 0))
            .ToArray();
        return _smartArtTextPaneSession.ApplyOutline(rows);
    }



    private SmartArtNodeEditResult? ApplySmartArtTextPaneAction(SmartArtNodeEditKind kind) => _smartArtTextPaneSession.ApplyAction(kind);
    private SmartArtNodeEditResult? ToggleSmartArtTextPaneAssistant() => _smartArtTextPaneSession.ToggleAssistant();




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
            Padding = new Thickness(6, 3),
            BorderBrush = selected
                ? FreePBrushes.Accent
                : FreePBrushes.DisabledBorder,
            BorderThickness = new Thickness(selected ? 2 : 1),
        };
        ToolTip.SetTip(box, item.RoleDisplayText);
        box.GotFocus += (_, _) => _smartArtTextPaneSession.SelectModel(item.ModelId);
        box.KeyDown += (_, e) =>
        {
            if (_smartArtTextPaneRefreshing)
                return;

            if (!TryMapSmartArtTextPaneKey(e.Key, e.KeyModifiers, out var key, out var modifiers))
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

    private void ConvertSelectedSmartArtToShapes()
        => _smartArtTextPaneSession.ConvertSelectedToShapes();

    private static bool TryMapSmartArtTextPaneKey(
        Key key,
        KeyModifiers keyboardModifiers,
        out SmartArtTextPaneShortcutKey shortcutKey,
        out SmartArtTextPaneShortcutModifiers modifiers)
    {
        shortcutKey = key switch
        {
            Key.Enter => SmartArtTextPaneShortcutKey.Enter,
            Key.Tab => SmartArtTextPaneShortcutKey.Tab,
            Key.Up => SmartArtTextPaneShortcutKey.Up,
            Key.Down => SmartArtTextPaneShortcutKey.Down,
            Key.Delete => SmartArtTextPaneShortcutKey.Delete,
            _ => default
        };
        if (key is not (Key.Enter or Key.Tab or Key.Up or Key.Down or Key.Delete))
        {
            modifiers = SmartArtTextPaneShortcutModifiers.None;
            return false;
        }

        modifiers = SmartArtTextPaneShortcutModifiers.None;
        if (keyboardModifiers.HasFlag(KeyModifiers.Shift))
            modifiers |= SmartArtTextPaneShortcutModifiers.Shift;
        if (keyboardModifiers.HasFlag(KeyModifiers.Control) || keyboardModifiers.HasFlag(KeyModifiers.Meta))
            modifiers |= SmartArtTextPaneShortcutModifiers.Control;
        if (keyboardModifiers.HasFlag(KeyModifiers.Alt))
            modifiers |= SmartArtTextPaneShortcutModifiers.Alt;
        return true;
    }

    internal void ShowAltTextPane() => _altTextPaneHostCoordinator.Show();

    internal void HideAltTextPane() => _altTextPaneHostCoordinator.Hide();

    private PresentationMediaCaptionHostSnapshot CaptureMediaCaptionHostSnapshot() =>
        _avaloniaMediaPaneHostView.CaptureCaption();
    private PresentationMediaVolumeHostSnapshot CaptureMediaVolumeHostSnapshot() =>
        _avaloniaMediaPaneHostView.CaptureVolume();
    private PresentationMediaPlaybackHostSnapshot CaptureMediaPlaybackHostSnapshot() =>
        _avaloniaMediaPaneHostView.CapturePlayback();
    private PresentationMediaTimingHostSnapshot CaptureMediaTimingHostSnapshot() =>
        _avaloniaMediaPaneHostView.CaptureTiming();
    private PresentationMediaBookmarkHostSnapshot CaptureMediaBookmarkHostSnapshot() =>
        _avaloniaMediaPaneHostView.CaptureBookmark();

    private PresentationMediaPaneHostViewAdapter BuildAvaloniaMediaPaneHostView() => new(
        new DelegatingPresentationMediaPaneControlSurface(new(
            PaneVisible: new(() => IsMediaCaptionPaneVisible, value => SetAvaloniaVisible(_mediaCaptionPaneHost, value)),
            CaptionLabel: new(() => ReadAvaloniaText(_mediaCaptionLabelBox), value => WriteAvaloniaText(_mediaCaptionLabelBox, value)),
            CaptionLanguage: new(() => ReadAvaloniaText(_mediaCaptionLanguageBox), value => WriteAvaloniaText(_mediaCaptionLanguageBox, value)),
            CaptionSource: new(() => ReadAvaloniaText(_mediaCaptionSourceBox), value => WriteAvaloniaText(_mediaCaptionSourceBox, value)),
            CaptionTranscript: new(() => ReadAvaloniaText(_mediaCaptionTranscriptBox), value => WriteAvaloniaText(_mediaCaptionTranscriptBox, value)),
            VolumePercent: new(() => ReadAvaloniaValue(_mediaVolumeSlider), value => WriteAvaloniaValue(_mediaVolumeSlider, value)),
            PlaybackStartModeIndex: new(() => ReadAvaloniaIndex(_mediaStartModeBox), value => WriteAvaloniaIndex(_mediaStartModeBox, value)),
            Loop: new(() => ReadAvaloniaCheck(_mediaLoopCheckBox), value => WriteAvaloniaCheck(_mediaLoopCheckBox, value)),
            ShowWhenStopped: new(() => ReadAvaloniaCheck(_mediaShowWhenStoppedCheckBox),
                value => WriteAvaloniaCheck(_mediaShowWhenStoppedCheckBox, value)),
            RewindAfterPlaying: new(() => ReadAvaloniaCheck(_mediaRewindAfterPlayingCheckBox),
                value => WriteAvaloniaCheck(_mediaRewindAfterPlayingCheckBox, value)),
            PlayFullScreen: new(() => ReadAvaloniaCheck(_mediaPlayFullScreenCheckBox),
                value => WriteAvaloniaCheck(_mediaPlayFullScreenCheckBox, value)),
            StopAfterSlides: new(() => ReadAvaloniaText(_mediaStopAfterSlidesBox),
                value => WriteAvaloniaText(_mediaStopAfterSlidesBox, value)),
            TrimStart: new(() => ReadAvaloniaText(_mediaTrimStartBox), value => WriteAvaloniaText(_mediaTrimStartBox, value)),
            TrimEnd: new(() => ReadAvaloniaText(_mediaTrimEndBox), value => WriteAvaloniaText(_mediaTrimEndBox, value)),
            FadeIn: new(() => ReadAvaloniaText(_mediaFadeInBox), value => WriteAvaloniaText(_mediaFadeInBox, value)),
            FadeOut: new(() => ReadAvaloniaText(_mediaFadeOutBox), value => WriteAvaloniaText(_mediaFadeOutBox, value)),
            BookmarkName: new(() => ReadAvaloniaText(_mediaBookmarkNameBox), value => WriteAvaloniaText(_mediaBookmarkNameBox, value)),
            BookmarkTime: new(() => ReadAvaloniaText(_mediaBookmarkTimeBox), value => WriteAvaloniaText(_mediaBookmarkTimeBox, value)),
            SetHeading: value => WriteAvaloniaText(_mediaCaptionPaneHeading, value),
            SetMessage: value => WriteAvaloniaText(_mediaCaptionPaneMessage, value),
            SetPlaybackStartModeEnabled: value => SetAvaloniaEnabled(_mediaStartModeBox, value),
            SetLoopEnabled: value => SetAvaloniaEnabled(_mediaLoopCheckBox, value),
            SetShowWhenStoppedEnabled: value => SetAvaloniaEnabled(_mediaShowWhenStoppedCheckBox, value),
            SetRewindAfterPlayingEnabled: value => SetAvaloniaEnabled(_mediaRewindAfterPlayingCheckBox, value),
            SetPlayFullScreenEnabled: value => SetAvaloniaEnabled(_mediaPlayFullScreenCheckBox, value),
            SetStopAfterSlidesEnabled: value => SetAvaloniaEnabled(_mediaStopAfterSlidesBox, value),
            SetPlaybackApplyEnabled: value => SetAvaloniaEnabled(_mediaPlaybackApplyButton, value),
            SetVolumeEnabled: value => SetAvaloniaEnabled(_mediaVolumeSlider, value),
            SetVolumeApplyEnabled: value => SetAvaloniaEnabled(_mediaVolumeApplyButton, value),
            SetTimingApplyEnabled: value => SetAvaloniaEnabled(_mediaTimingApplyButton, value),
            RenderCaptionTracks: RenderMediaCaptionTrackOptions,
            RenderCaptionField: RenderAvaloniaMediaCaptionField,
            RenderCaptionAction: RenderAvaloniaMediaCaptionAction,
            RenderBookmarks: RenderAvaloniaMediaBookmarkOptions,
            RefreshAccessibilityMetadata: RefreshPaneAccessibilityMetadata)));

    private static string? ReadAvaloniaText(TextBox? control) => control?.Text;
    private static void WriteAvaloniaText(TextBox control, string? value) => control.Text = value ?? string.Empty;
    private static void WriteAvaloniaText(TextBlock control, string value) => control.Text = value;
    private static double? ReadAvaloniaValue(Slider? control) => control?.Value;
    private static void WriteAvaloniaValue(Slider control, double? value) =>
        control.Value = value ?? PresentationMediaPaneSession.DefaultVolumePercent;
    private static int? ReadAvaloniaIndex(ComboBox? control) => control?.SelectedIndex;
    private static void WriteAvaloniaIndex(ComboBox control, int? value) => control.SelectedIndex = value ?? -1;
    private static bool? ReadAvaloniaCheck(CheckBox? control) => control?.IsChecked;
    private static void WriteAvaloniaCheck(CheckBox control, bool? value) => control.IsChecked = value;
    private static void SetAvaloniaEnabled(Control control, bool value) => control.IsEnabled = value;
    private static void SetAvaloniaVisible(Control control, bool value) => control.IsVisible = value;

    private void RenderAvaloniaMediaCaptionField(
        PresentationMediaPaneCaptionField field,
        PresentationMediaCaptionAuthoringFieldPlan plan)
    {
        var controls = _mediaCaptionControls.Get(field);
        RenderMediaCaptionField(controls.Label, controls.Input, plan);
    }

    private void RenderAvaloniaMediaCaptionAction(
        PresentationMediaPaneCaptionAction action,
        PresentationMediaCaptionAuthoringActionPlan plan)
    {
        ApplyMediaCaptionButtonPlan(_mediaPaneButtons.Get(action), plan);
    }

    private void RenderAvaloniaMediaBookmarkOptions(PresentationMediaPaneProjection plan)
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
                Clear: () =>
                {
                    _mediaCaptionTrackBox.ItemsSource = null;
                    _mediaCaptionTrackBox.Items.Clear();
                },
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
        textBox.PlaceholderText = field.Placeholder;
        ToolTip.SetTip(textBox, field.ToolTip);
        textBox.IsEnabled = field.IsEnabled;
        SetTextIfChanged(textBox, field.Value);
    }

    private static void ApplyMediaCaptionButtonPlan(
        Button button,
        PresentationMediaCaptionAuthoringActionPlan action)
    {
        button.Content = action.Label;
        button.IsEnabled = action.IsEnabled;
        ToolTip.SetTip(button, action.DisabledReason);
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
            visible => _selectionPane.IsVisible = visible,
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

    bool IPresentationAltTextPaneHostView.IsPaneVisible => _altTextPaneHost?.IsVisible == true;

    PresentationAltTextPaneHostSnapshot IPresentationAltTextPaneHostView.CaptureInput() =>
        new(_altTextTitleBox.Text, _altTextDescriptionBox.Text, _altTextDecorativeCheck.IsChecked == true);

    void IPresentationAltTextPaneHostView.SetPaneVisible(bool visible) =>
        _altTextPaneHost.IsVisible = visible;

    void IPresentationAltTextPaneHostView.SetInput(PresentationAltTextPaneHostSnapshot input)
    {
        _altTextTitleBox.Text = input.Title;
        _altTextDescriptionBox.Text = input.Description;
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
        _altTextTitleBox.PlaceholderText = plan.Title.Placeholder;
        _altTextDescriptionBox.PlaceholderText = plan.Description.Placeholder;
        _altTextTitleBox.IsEnabled = plan.Title.IsEnabled;
        _altTextDescriptionBox.IsEnabled = plan.Description.IsEnabled;
        _altTextDecorativeCheck.Content = plan.DecorativeAction.Label;
        _altTextDecorativeCheck.IsEnabled = plan.DecorativeAction.IsEnabled;
        _altTextDecorativeCheck.IsChecked = plan.IsDecorative;
        _altTextApplyButton.Content = plan.ApplyAction.Label;
        _altTextApplyButton.IsEnabled = plan.ApplyAction.IsEnabled;
        _altTextCloseButton.Content = plan.CloseAction.Label;
        _altTextCloseButton.IsEnabled = plan.CloseAction.IsEnabled;
    }

    void IPresentationAltTextPaneHostView.RefreshAccessibilityMetadata() =>
        RefreshPaneAccessibilityMetadata();

    void IPresentationReadingOrderPaneHostView.SetPaneVisible(bool visible) =>
        _readingOrderPaneHost.IsVisible = visible;

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
        {
            var card = BuildReadingOrderItemCard(item);
            PresentationPaneAccessibilityAdapter.ApplyItem(
                card,
                PresentationPaneAccessibilityPlanner.PlanItem(
                    PresentationPaneAccessibilityPlanner.ReadingOrderPaneId,
                    item.ReadingOrderIndex,
                    item.ShapeName,
                    item.IsSelected,
                    PresentationPaneAccessibilityPlanner.BuildShapeKey(item.ShapeId)));
            _readingOrderPaneItemsPanel.Children.Add(card);
        }
    }

    void IPresentationReadingOrderPaneHostView.RefreshAccessibilityMetadata() =>
        RefreshPaneAccessibilityMetadata();

    private static void ApplyReadingOrderButtonPlan(
        Button button,
        PresentationReadingOrderPaneActionRenderPlan action)
    {
        button.Content = action.Label;
        button.IsEnabled = action.IsEnabled;
        button.Tag = action.CommandId;
        ToolTip.SetTip(button, action.DisabledReason);
    }

    private Control BuildReadingOrderItemCard(PresentationReadingOrderItemPlan item)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 2,
            Children =
            {
                new TextBlock
                {
                    Text = item.DisplayTitle,
                    FontSize = PresentationReadingOrderPaneVisualMetrics.BodyFontSize,
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = item.Metadata,
                    FontSize = PresentationReadingOrderPaneVisualMetrics.BodyFontSize,
                    Foreground = FreePBrushes.PaneSecondaryText,
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = item.AccessibilitySummary,
                    FontSize = PresentationReadingOrderPaneVisualMetrics.BodyFontSize,
                    Foreground = FreePBrushes.PaneText,
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = item.AltTextDisplayText,
                    FontSize = PresentationReadingOrderPaneVisualMetrics.BodyFontSize,
                    Foreground = FreePBrushes.PaneMutedText,
                    TextWrapping = TextWrapping.Wrap,
                },
            }
        };

        if (item.IsSelected)
        {
            panel.Children.Insert(1, new TextBlock
            {
                Text = item.SelectedLabel,
                FontSize = PresentationReadingOrderPaneVisualMetrics.BodyFontSize,
                Foreground = FreePBrushes.Accent,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, PresentationReadingOrderPaneVisualMetrics.SelectedItemTopInset, 0, 0),
            });
        }

        var card = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
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
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        ToolTip.SetTip(button, item.SelectionToolTip);
        button.Click += (_, _) => ApplyReadingOrderSelectItem(item.ShapeId);
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

    private static Control BuildProofingEmptyState(string message) =>
        new TextBlock
        {
            Text = message,
            Foreground = FreePBrushes.PaneMutedText,
            Margin = new Thickness(12, 0, 12, 10),
            TextWrapping = TextWrapping.Wrap,
        };

    private Control BuildProofingIssueRowCard(PresentationProofingIssueRowPlan row)
    {
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
        };
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
            };
            ToolTip.SetTip(button, action.DisabledReason);
            button.Click += (_, _) =>
                _reviewPaneHostCoordinator.ExecuteProofingRowAction(row.RowIndex, action.Kind);
            buttons.Children.Add(button);
        }

        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 2,
            Children =
            {
                new TextBlock
                {
                    Text = row.DisplayTitle,
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = row.ReplacementDisplayText,
                    Foreground = FreePBrushes.PaneSecondaryText,
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = row.Message,
                    TextWrapping = TextWrapping.Wrap,
                },
                buttons,
            }
        };

        var border = new Border
        {
            Background = row.IsSelected ? FreePBrushes.SelectedRowSurface : Brushes.Transparent,
            BorderBrush = FreePBrushes.GridBorder,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 8, 12, 8),
            Child = panel,
        };
        PresentationPaneAccessibilityAdapter.ApplyItem(
            border,
            PresentationPaneAccessibilityPlanner.PlanItem(
                PresentationPaneAccessibilityPlanner.ProofingPaneId,
                row.RowIndex,
                row.Text,
                row.IsSelected,
                row.AccessibilityKey));
        return border;
    }

    private async Task<bool> TrySavePresentationFileAsync(string path) =>
        (await _fileSession.SavePathAsync(path)).Succeeded;


    // ── Presentation load ──────────────────────────────────────────────────────

    private void LoadPresentationAsSaved(Presentation presentation, string? path, bool suppressRecentFiles = false)
    {
        LoadPresentationContent(presentation);

        if (path is null)
            _fileWorkflow.MarkSavedWithoutPath();
        else
            _fileWorkflow.MarkSavedWithPath(path, suppressRecentFiles);
    }

    private void LoadPresentationContent(Presentation presentation)
    {
        _workareaSession.ReplacePresentation(presentation);
    }

    // ── Canvas refresh ─────────────────────────────────────────────────────────

    private void RefreshCanvas()
    {
        CloseActiveOleHost();
        _slideCanvas.Presentation = _presentation;
        _slideCanvas.Slide        = Editor.CurrentSlide;
        _slideCanvas.SlideIndex   = Editor.CurrentSlideIndex;
        _slideCanvas.Refresh();
    }

    // ── Slide pane ─────────────────────────────────────────────────────────────

    private void RefreshSlidePane()
    {
        if (IsSlidePaneListTarget(FocusManager?.GetFocusedElement() as Control))
            _restoreSlidePaneFocusAfterRefresh = true;
        var restoreSlidePaneFocus = _restoreSlidePaneFocusAfterRefresh;
        _slidePaneRefreshing = true;
        try
        {
            _slidePaneList.Items.Clear();
            var projection = _workareaSession.SlidePaneSession.Projection;
            foreach (var projected in projection.Items)
            {
                var entry = projected.Entry;
                if (projected.SectionHeader is { } sectionHeader)
                {
                    _slidePaneList.Items.Add(BuildSlidePaneSectionHeader(
                        projected,
                        sectionHeader));
                    continue;
                }

                var slide = _presentation.Slides[entry.SlideIndex];
                var plan = projected.Thumbnail!;

                // Small SlideCanvas thumbnail using the shared slide pane metrics.
                var thumb = new SlideCanvas
                {
                    Presentation = _presentation,
                    Slide        = slide,
                    SlideIndex   = plan.SlideIndex,
                    Width        = plan.ThumbnailWidth,
                    Height       = plan.ThumbnailHeight,
                    // Slide-pane thumbnails are previews; pointer and keyboard input belongs
                    // to the surrounding item so selection, drag, and context-menu routes stay
                    // identical to the WPF host.
                    IsHitTestVisible = false,
                    IsEnabled        = false,
                };

                // Slide number label beneath thumbnail.
                var label = new TextBlock
                {
                    Text                = plan.LabelText,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontSize            = plan.LabelFontSize,
                    Height              = plan.LabelHeight,
                    MinHeight           = plan.LabelHeight,
                    VerticalAlignment   = VerticalAlignment.Center,
                    Margin              = new Thickness(0, 0, 0, plan.LabelBottomMargin),
                    Foreground          = BrushFromHex(plan.LabelForegroundHex),
                };

                var thumbnailBorder = new Border
                {
                    BorderBrush     = BrushFromHex(plan.ThumbnailBorderHex),
                    BorderThickness = new Thickness(plan.ThumbnailBorderThickness),
                    Child           = thumb,
                };

                var panel = new StackPanel
                {
                    HorizontalAlignment = plan.CenterThumbnailContent
                        ? HorizontalAlignment.Center
                        : HorizontalAlignment.Stretch,
                };
                panel.Children.Add(label);
                panel.Children.Add(thumbnailBorder);

                var itemChrome = new Border
                {
                    Background      = BrushFromHex(plan.IsSelected ? plan.ItemSelectedBackgroundHex : plan.ItemNormalBackgroundHex),
                    BorderBrush     = BrushFromHex(plan.IsSelected ? plan.ItemSelectedBorderHex : plan.ItemNormalBorderHex),
                    BorderThickness = new Thickness(plan.IsSelected ? plan.SelectedBorderThickness : plan.NormalBorderThickness),
                    CornerRadius    = new CornerRadius(plan.ItemCornerRadius),
                    Padding         = new Thickness(plan.ItemPadding),
                    Child           = panel,
                };

                var item = new ListBoxItem
                {
                    Tag         = plan.SlideIndex,
                    Content     = itemChrome,
                    Padding     = new Thickness(0),
                    Margin      = new Thickness(plan.ItemMarginHorizontal, plan.ItemMarginVertical),
                    MinHeight   = plan.ItemHeight,
                    IsSelected  = plan.IsSelected,
                    ContextMenu = BuildSlidePaneContextMenu(plan.SlideIndex),
                };
                AutomationProperties.SetName(item, plan.AccessibleName);
                PresentationPaneAccessibilityAdapter.ApplyItem(
                    item,
                    PresentationPaneAccessibilityPlanner.PlanSlideItem(
                        projected.AccessibilityOrdinal,
                        plan.SlideIndex,
                        plan.AccessibleName,
                        plan.IsSelected,
                        plan.IsActive));
                ToolTip.SetTip(item, plan.ToolTipText);
                WireContextMenuLifecycle(item);
                item.KeyDown += OnSlidePaneItemKeyDown;
                item.PointerEntered += (_, _) =>
                {
                    if (item.Tag is int idx &&
                        !_workareaSession.SlidePaneSession.Selection.IsSelected(idx))
                        itemChrome.Background = BrushFromHex(plan.ItemHoverBackgroundHex);
                };
                item.PointerExited += (_, _) =>
                {
                    if (item.Tag is int idx)
                        itemChrome.Background = BrushFromHex(
                            _workareaSession.SlidePaneSession.Selection.IsSelected(idx)
                            ? plan.ItemSelectedBackgroundHex
                            : plan.ItemNormalBackgroundHex);
                };
                WireSlidePaneDragHandlers(item);
                _slidePaneList.Items.Add(item);
            }

            _paneAccessibility.ApplyPane(
                _slidePaneList,
                PresentationPaneAccessibilityPlanner.SlidePaneId,
                true,
                _presentation.Slides.Count,
                projection.Selection.ActiveSlideIndex);
            SyncSlidePaneSelectionFromSession(scrollActiveIntoView: false);
            if (restoreSlidePaneFocus)
            {
                GetCurrentSlidePaneItem()?.Focus();
                Dispatcher.UIThread.Post(RestoreSlidePaneFocusAfterRefresh);
            }
        }
        finally
        {
            _slidePaneRefreshing = false;
        }
        RefreshPaneAccessibilityMetadata();
    }

    private ListBoxItem BuildSlidePaneSectionHeader(
        PresentationSlidePaneItemProjection projected,
        SlidePaneSectionHeaderVisualPlan plan)
    {
        var entry = projected.Entry;
        var normalBackground = BrushFromHex(plan.BackgroundHex);
        var hoverBackground = BrushFromHex(plan.HoverBackgroundHex);

        var disclosure = new TextBlock
        {
            Text              = plan.DisclosureText,
            FontSize          = plan.FontSize,
            FontWeight        = FontWeight.Bold,
            Foreground        = BrushFromHex(plan.ForegroundHex),
            Width             = plan.DisclosureWidth,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var label = new TextBlock
        {
            Text              = plan.LabelText,
            FontSize          = plan.FontSize,
            FontWeight        = FontWeight.SemiBold,
            Foreground        = BrushFromHex(plan.ForegroundHex),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming      = TextTrimming.CharacterEllipsis,
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { disclosure, label },
        };

        var headerChrome = new Border
        {
            Background   = normalBackground,
            Padding      = new Thickness(plan.HorizontalPadding, plan.VerticalPadding),
            CornerRadius = new CornerRadius(plan.CornerRadius),
            MinHeight    = plan.HeaderHeight,
            Child        = row,
        };

        var item = new ListBoxItem
        {
            Content     = headerChrome,
            Padding     = new Thickness(0),
            Margin      = new Thickness(0, plan.TopMargin, 0, plan.BottomMargin),
            MinHeight   = plan.HeaderHeight,
            Focusable   = true,
            Tag         = new SlidePaneSectionHeaderTag(plan.SectionId, plan.SectionIndex),
            Cursor      = new Cursor(StandardCursorType.Hand),
            ContextMenu = BuildSlidePaneSectionContextMenu(entry),
        };
        ToolTip.SetTip(item, plan.ToolTipText);
        AutomationProperties.SetName(item, plan.AccessibleName);
        PresentationPaneAccessibilityAdapter.ApplyItem(
            item,
            PresentationPaneAccessibilityPlanner.PlanSectionItem(
                projected.AccessibilityOrdinal,
                plan.SectionIndex,
                plan.AccessibleName));
        WireContextMenuLifecycle(item);
        item.PointerEntered += (_, _) => headerChrome.Background = hoverBackground;
        item.PointerExited += (_, _) => headerChrome.Background = normalBackground;
        item.PointerPressed += (_, e) =>
        {
            var point = e.GetCurrentPoint(item);
            if (!point.Properties.IsLeftButtonPressed)
                return;

            ToggleSlidePaneSection(plan.SectionId);
            e.Handled = true;
        };
        item.KeyDown += (_, e) =>
        {
            if (TryHandleContextMenuKeyboard(item, e))
                return;

            if (e.Key is Key.Enter or Key.Space)
            {
                ToggleSlidePaneSection(plan.SectionId);
                e.Handled = true;
            }
        };

        return item;
    }

    private ContextMenu BuildSlidePaneContextMenu(int slideIndex)
    {
        var menu = new ContextMenu();

        AddContextMenuEntries(
            menu,
            FreePContextMenuCatalog.BuildSlideMenu(
                _presentation.Slides,
                _presentation.Sections,
                slideIndex),
            command => ApplyContextMenuCommandGuardedAsync(command, slideIndex, sectionIndex: -1));

        return menu;
    }


    internal bool TryApplySlidePaneContextAction(int slideIndex, SlidePaneActionKind kind)
        => _workareaSession.ExecuteSlidePaneAction(kind, slideIndex);

    private ContextMenu BuildSlidePaneSectionContextMenu(SlidePaneEntry entry)
    {
        var menu = new ContextMenu();

        AddContextMenuEntries(
            menu,
            FreePContextMenuCatalog.BuildSectionHeaderMenu(
                _presentation.Sections,
                entry.SectionIndex,
                entry.SlideIndex),
            command => ApplyContextMenuCommandGuardedAsync(command, entry.SlideIndex, entry.SectionIndex));

        return menu;
    }


    private static void AddContextMenuEntries(
        ContextMenu menu,
        IReadOnlyList<FreePContextMenuEntryPlan> entries,
        Func<FreePContextMenuCommand, Task> execute)
    {
        foreach (var entry in entries)
        {
            if (entry.Kind == FreePContextMenuEntryKind.Separator)
            {
                menu.Items.Add(new Separator());
                continue;
            }

            var item = new MenuItem
            {
                Header = entry.Text,
                IsEnabled = entry.IsEnabled,
                IsChecked = entry.IsChecked,
                Tag = entry.Command,
            };
            if (entry.IsCheckable)
                item.ToggleType = MenuItemToggleType.CheckBox;
            // This lambda is `async void`, so `execute` must never throw — callers pass the guarded
            // ApplyContextMenuCommandGuardedAsync for exactly that reason.
            item.Click += async (_, _) => await execute(entry.Command!.Value);
            menu.Items.Add(item);
        }
    }

    /// <summary>
    /// Guarded wrapper the slide-pane context menu is wired to. The menu item's Click handler is an
    /// <c>async void</c> lambda, and the command path below genuinely throws: an
    /// <see cref="ArgumentOutOfRangeException"/> for an unmapped command, and a <c>.Single()</c> over
    /// the planner's actions that fails when the requested kind is not present exactly once. Without
    /// this, right-clicking a slide or section in an edge-case state terminated the process.
    /// </summary>
    private async Task ApplyContextMenuCommandGuardedAsync(
        FreePContextMenuCommand command,
        int slideIndex,
        int sectionIndex)
    {
        try
        {
            await ApplyContextMenuCommandAsync(command, slideIndex, sectionIndex);
        }
        catch (Exception ex)
        {
            _statusText.Text = SisterAppFileTextPlanner.FormatCommandFailed(
                FileText,
                UiText.Get("Shell_Command_SlidePane"),
                ex.Message);
        }
    }

    private async Task ApplyContextMenuCommandAsync(
        FreePContextMenuCommand command,
        int slideIndex,
        int sectionIndex)
    {
        var route = _workareaSession.BuildSlidePaneContextCommandRoute(
            command,
            slideIndex,
            sectionIndex);
        if (route.SlideAction is { } slideAction)
        {
            _workareaSession.ExecuteSlidePaneAction(
                slideAction.Kind,
                slideIndex,
                slideAction.TargetSlideIndex);
            return;
        }

        if (route.SectionExecution is { } sectionExecution)
            await ApplySlideSectionActionAsync(sectionExecution);
    }

    private async Task ApplySlideSectionActionAsync(SlideSectionActionExecutionPlan execution)
    {
        if (!execution.IsEnabled)
            return;

        string? promptedName = null;
        if (execution.RequiresNamePrompt)
        {
            promptedName = await PromptSectionNameAsync(execution);
            if (promptedName is null)
                return;
        }

        _workareaSession.ExecuteSlidePaneSectionAction(execution, promptedName);
    }

    private void ToggleSlidePaneSection(string sectionId)
    {
        if (string.IsNullOrWhiteSpace(sectionId))
            return;

        _workareaSession.ToggleSlidePaneSection(sectionId);
    }



    private async Task<string?> PromptSectionNameAsync(SlideSectionActionExecutionPlan prompt)
    {
        var textBox = new TextBox
        {
            Text = prompt.SuggestedName,
            MinWidth = 260,
            Margin = new Thickness(0, 0, 0, 12),
        };

        var ok = new Button
        {
            Content = prompt.PromptAcceptText,
            Width = 76,
            Margin = new Thickness(0, 0, 8, 0),
        };
        var cancel = new Button
        {
            Content = prompt.PromptCancelText,
            Width = 76,
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { ok, cancel },
        };

        var panel = new StackPanel
        {
            Margin = new Thickness(14),
            Children =
            {
                new TextBlock
                {
                    Text = prompt.PromptLabel,
                    Margin = new Thickness(0, 0, 0, 4),
                },
                textBox,
                buttons,
            },
        };

        var dialog = new Window
        {
            Title = prompt.PromptTitle,
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };

        ok.Click += (_, _) => dialog.Close(textBox.Text);
        cancel.Click += (_, _) => dialog.Close(null);
        dialog.Opened += (_, _) =>
        {
            textBox.Focus();
            textBox.SelectAll();
        };

        return await dialog.ShowDialog<string?>(this);
    }

    private void WireSlidePaneDragHandlers(ListBoxItem item)
    {
        item.PointerPressed += OnSlidePaneItemPointerPressed;
        item.PointerMoved += OnSlidePaneItemPointerMoved;
        item.PointerReleased += OnSlidePaneItemPointerReleased;
        item.PointerCaptureLost += OnSlidePaneItemPointerCaptureLost;
    }

    private void OnSlidePaneItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ListBoxItem { Tag: int sourceSlideIndex } item)
            return;

        var point = e.GetCurrentPoint(item);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        _workareaSession.BeginSlidePaneDrag(sourceSlideIndex, e.GetPosition(item).Y);
    }

    private void OnSlidePaneItemPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not ListBoxItem item ||
            !_workareaSession.SlidePaneSession.Projection.Layout.DragSession.IsTracking)
            return;

        var point = e.GetCurrentPoint(item);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        var update = _workareaSession.UpdateSlidePaneDrag(
            e.GetPosition(item).Y,
            e.GetPosition(_slidePaneList).Y,
            SlidePanePlanner.DefaultSlideItemHeight);
        if (!update.State.IsDragging)
            return;

        if (update.ShouldCapturePointer)
            e.Pointer.Capture(item);

        ShowSlidePaneInsertionIndicator(update.DropVisualPlan);
        e.Handled = true;
    }

    private void OnSlidePaneItemPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _workareaSession.CompleteSlidePaneDrag(out var shouldReleaseCapture);
        if (!shouldReleaseCapture)
        {
            return;
        }

        e.Pointer.Capture(null);
        HideSlidePaneInsertionIndicator();

        e.Handled = true;
    }

    private void OnSlidePaneItemPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _workareaSession.CancelSlidePaneDrag();
        HideSlidePaneInsertionIndicator();
    }

    internal bool TryApplySlidePaneMove(int sourceSlideIndex, int targetInsertionIndex)
        => _workareaSession.ExecuteSlidePaneAction(
            SlidePaneActionKind.MoveSlide,
            sourceSlideIndex,
            targetInsertionIndex);



    internal bool TryApplySlidePaneKeyboardAction(SlidePaneKeyboardIntentKind intent)
        => _workareaSession.ExecuteSlidePaneKeyboardAction(intent);

    private void OnSlidePaneItemKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is Control anchor && TryHandleContextMenuKeyboard(anchor, e))
            return;

        if (!TryMapSlidePaneKeyboardIntent(e, out var intent))
            return;

        if (TryApplySlidePaneKeyboardAction(intent))
            e.Handled = true;
    }

    private static void WireContextMenuLifecycle(Control anchor)
    {
        if (anchor.ContextMenu is not { } menu)
            return;

        menu.Opened += (_, _) => Dispatcher.UIThread.Post(() =>
            menu.Items.OfType<MenuItem>().FirstOrDefault(item => item.IsEnabled)?.Focus());
        menu.Closed += (_, _) => Dispatcher.UIThread.Post(() => anchor.Focus());
        menu.KeyDown += (_, args) =>
        {
            if (!TryMapKeyboardKey(args.Key, out var key) ||
                !FreePContextMenuCatalog.IsKeyboardDismissal(key, ToKeyboardModifiers(args.KeyModifiers)))
            {
                return;
            }

            menu.Close();
            args.Handled = true;
        };
    }

    private static bool TryHandleContextMenuKeyboard(Control anchor, KeyEventArgs args)
    {
        if (anchor.ContextMenu is not { } menu ||
            !TryMapKeyboardKey(args.Key, out var key))
        {
            return false;
        }

        var modifiers = ToKeyboardModifiers(args.KeyModifiers);
        if (FreePContextMenuCatalog.IsKeyboardInvocation(key, modifiers))
        {
            anchor.Focus();
            menu.Open(anchor);
            args.Handled = true;
            return true;
        }

        if (menu.IsOpen && FreePContextMenuCatalog.IsKeyboardDismissal(key, modifiers))
        {
            menu.Close();
            args.Handled = true;
            return true;
        }

        return false;
    }

    private static bool TryMapSlidePaneKeyboardIntent(
        KeyEventArgs e,
        out SlidePaneKeyboardIntentKind intent)
    {
        intent = e.Key switch
        {
            Key.Insert when e.KeyModifiers == KeyModifiers.None =>
                SlidePaneKeyboardIntentKind.InsertAfterCurrentSlide,
            Key.Delete when e.KeyModifiers == KeyModifiers.None =>
                SlidePaneKeyboardIntentKind.DeleteCurrentSlide,
            Key.D when e.KeyModifiers == KeyModifiers.Control =>
                SlidePaneKeyboardIntentKind.DuplicateCurrentSlide,
            Key.Up when e.KeyModifiers == KeyModifiers.Alt =>
                SlidePaneKeyboardIntentKind.MoveCurrentSlideEarlier,
            Key.Down when e.KeyModifiers == KeyModifiers.Alt =>
                SlidePaneKeyboardIntentKind.MoveCurrentSlideLater,
            _ => default,
        };

        return intent != SlidePaneKeyboardIntentKind.None;
    }


    private Button BuildSlidePaneNewSlideButton()
    {
        var plan = _workareaSession.SlidePaneSession.Projection.BottomAffordance;
        var button = new Button
        {
            Content                    = plan.Text,
            Margin                     = new Thickness(8, 6, 8, 8),
            Padding                    = new Thickness(0, 6),
            HorizontalAlignment        = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Background                 = FreePBrushes.Accent,
            Foreground                 = FreePBrushes.White,
            BorderThickness            = new Thickness(0),
            CornerRadius               = new CornerRadius(3),
            FontSize                   = 12,
            FontWeight                 = FontWeight.SemiBold,
            IsVisible                  = plan.IsVisible,
            IsEnabled                  = plan.Action.IsEnabled,
        };
        AutomationProperties.SetName(button, plan.AccessibleName);
        ToolTip.SetTip(button, plan.ToolTipText);
        button.Click += (_, _) => InsertSlideFromSlidePaneAffordance();
        return button;
    }

    private bool InsertSlideFromSlidePaneAffordance() =>
        _workareaSession.ExecuteSlidePaneAction(
            SlidePaneActionKind.InsertAfterSlide,
            _workareaSession.SlidePaneSession.Selection.ActiveSlideIndex);

    private void ShowSlidePaneInsertionIndicator(SlidePaneDropVisualPlan plan)
    {
        if (!plan.IsVisible)
        {
            HideSlidePaneInsertionIndicator();
            return;
        }

        _slidePaneInsertionIndicator.Height = plan.IndicatorThickness;
        _slidePaneInsertionIndicator.Background = BrushFromHex(plan.AccentColorHex);
        _slidePaneInsertionIndicator.Margin = new Thickness(
            plan.HorizontalInset,
            plan.IndicatorTopMargin,
            plan.HorizontalInset,
            0);
        _slidePaneInsertionIndicator.IsVisible = true;
    }

    private void HideSlidePaneInsertionIndicator() =>
        _slidePaneInsertionIndicator.IsVisible = false;

    private static IBrush BrushFromHex(string hex) =>
        new SolidColorBrush(Color.Parse(hex));

    private void SyncSlidePaneSelectionFromSession(bool scrollActiveIntoView = true)
    {
        var selection = _workareaSession.SlidePaneSession.Selection;
        _slidePaneList.SelectedItems?.Clear();
        foreach (var item in _slidePaneList.Items.OfType<ListBoxItem>())
        {
            if (item.Tag is int slideIndex && selection.IsSelected(slideIndex))
                _slidePaneList.SelectedItems?.Add(item);
        }

        if (scrollActiveIntoView && GetCurrentSlidePaneItem() is { } active)
            active.BringIntoView();
    }

    private ListBoxItem? GetCurrentSlidePaneItem() =>
        _slidePaneList.Items
            .OfType<ListBoxItem>()
            .FirstOrDefault(item => item.Tag is int slideIndex &&
                slideIndex == _workareaSession.SlidePaneSession.Selection.ActiveSlideIndex);

    private void RestoreSlidePaneFocusAfterRefresh()
    {
        if (!_restoreSlidePaneFocusAfterRefresh)
            return;

        _restoreSlidePaneFocusAfterRefresh = false;
        GetCurrentSlidePaneItem()?.Focus();
    }

    private void UpdateSlidePaneItemChrome()
    {
        var projection = _workareaSession.SlidePaneSession.Projection;
        foreach (var item in _slidePaneList.Items.OfType<ListBoxItem>())
        {
            if (item.Tag is not int slideIndex || item.Content is not Border chrome)
                continue;

            var projected = projection.Items.FirstOrDefault(candidate =>
                candidate.Entry.Kind == SlidePaneEntryKind.Slide &&
                candidate.Entry.SlideIndex == slideIndex);
            if (projected?.Thumbnail is not { } plan)
                continue;

            chrome.Background = BrushFromHex(plan.IsSelected ? plan.ItemSelectedBackgroundHex : plan.ItemNormalBackgroundHex);
            chrome.BorderBrush = BrushFromHex(plan.IsSelected ? plan.ItemSelectedBorderHex : plan.ItemNormalBorderHex);
            chrome.BorderThickness = new Thickness(plan.IsSelected ? plan.SelectedBorderThickness : plan.NormalBorderThickness);
            AutomationProperties.SetName(item, plan.AccessibleName);
            PresentationPaneAccessibilityAdapter.ApplyItem(
                item,
                PresentationPaneAccessibilityPlanner.PlanSlideItem(
                    projected.AccessibilityOrdinal,
                    slideIndex,
                    plan.AccessibleName,
                    plan.IsSelected,
                    plan.IsActive));
        }
        RefreshPaneAccessibilityMetadata();
    }

    private void OnSlidePaneSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_slidePaneRefreshing)
            return;

        var selected = _slidePaneList.SelectedItems?
            .OfType<ListBoxItem>()
            .Select(item => item.Tag)
            .OfType<int>()
            .ToArray() ?? [];
        var active = e.AddedItems
            .OfType<ListBoxItem>()
            .Select(item => item.Tag)
            .OfType<int>()
            .LastOrDefault(_workareaSession.SlidePaneSession.Selection.ActiveSlideIndex);
        if (selected.Length == 0 || active < 0)
            return;

        _workareaSession.ApplySlidePaneNativeSelection(selected, active);
    }

    // ── Notes pane ─────────────────────────────────────────────────────────────

    private void RefreshNotesPane()
    {
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

    private void OnNotesTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_notesRefreshing)
            return;
        RecordStartupObservation("notes-text-changed");
        var result = _notesPaneSession.ApplyText(_notesBox.Text);
        LastNotesPagePreviewPlan = result.Plan.Preview;
        if (!result.Changed)
            return;
        RefreshPaneAccessibilityMetadata();
    }

    // ── Event handlers ─────────────────────────────────────────────────────────

    private void OnNotesKeyDown(object? sender, KeyEventArgs e)
    {
        if (_notesRefreshing ||
            (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0)
        {
            return;
        }

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
        if (_notesRefreshing || !_notesBox.IsVisible || _notesBox.SelectionStart == _notesBox.SelectionEnd)
            return false;

        return Editor.TryApplyCurrentSlideNotesTextFormat(
            kind,
            (_notesBox.SelectionStart, _notesBox.SelectionEnd),
            _notesBox.Text);
    }

    private bool TryApplyCurrentSlideNotesValueFormat(
        TableCellTextValueFormatKind kind,
        object? value)
    {
        if (_notesRefreshing || !_notesBox.IsVisible || _notesBox.SelectionStart == _notesBox.SelectionEnd)
            return false;

        return Editor.TryApplyCurrentSlideNotesValueFormat(
            kind,
            value,
            (_notesBox.SelectionStart, _notesBox.SelectionEnd),
            _notesBox.Text);
    }

    private bool TryApplyCurrentSlideNotesParagraphFormat(
        TableCellParagraphFormatKind kind,
        object? value = null)
    {
        if (_notesRefreshing || !_notesBox.IsVisible || _notesBox.SelectionStart == _notesBox.SelectionEnd)
            return false;

        return Editor.TryApplyCurrentSlideNotesParagraphFormat(
            kind,
            value,
            (_notesBox.SelectionStart, _notesBox.SelectionEnd),
            _notesBox.Text);
    }

    private void SyncSlidePaneSelectionFromEditor()
    {
        _slidePaneRefreshing = true;
        try { SyncSlidePaneSelectionFromSession(); }
        finally { _slidePaneRefreshing = false; }
    }

    private void SyncRibbonCommandStates()
    {
        _ribbonBindingSession?.SyncCommandStates();
    }

    private void OnFileWorkflowChanged()
    {
        RecordStartupObservation("file-workflow-changed");
        UpdateStatus();
    }

    // ── Status ─────────────────────────────────────────────────────────────────

    private void UpdateStatus()
    {
        _statusText.Text = _workareaSession
            .BuildStatusPlan(FreePApplicationFrameDescriptor.ResolveDataFolderLabel())
            .Text;
    }

    // ── Keyboard shortcuts ─────────────────────────────────────────────────────

    private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (TryHandleRibbonKeyTips(e))
            return;

        if (_backstage.IsOpen && e.Key == Key.Escape)
        {
            HideBackstageAndRestoreFocus();
            e.Handled = true;
            return;
        }

        if (TryMapKeyboardKey(e.Key, out var key) &&
            FreePKeyboardShortcutCatalog.TryDispatch(
                key,
                ToKeyboardModifiers(e.KeyModifiers),
                _workareaSession.ExecuteCommand))
        {
            e.Handled = true;
            return;
        }

        // ── Arrow / Delete keys — delegate to gesture handler (Theme 15) ────────
        if (_gestureHandler is not null)
        {
            // Skip if text editor is active (keys go into the TextBox).
            if (_textEditor is { IsActive: true } || _textEditor?.IsEditorFocused == true)
                return;

            if (_gestureHandler.HandleKeyDown(e.Key, e.KeyModifiers))
            {
                e.Handled = true;
                // Refresh canvas + adorner after model change.
                _slideCanvas.Refresh();
            }
        }
    }

    private bool ShouldTunnelShellUndoRedo(KeyEventArgs e) =>
        e.Key is Key.Z or Key.Y &&
        (e.KeyModifiers & KeyModifiers.Control) != 0 &&
        IsShellShortcutTarget(FocusManager?.GetFocusedElement() as Control ?? e.Source as Control);

    private bool IsShellShortcutTarget(Control? focused)
    {
        if (ReferenceEquals(focused, _slidePaneNewSlideButton))
            return true;

        if (IsSlidePaneListTarget(focused))
            return true;

        for (var current = focused; current is not null; current = current.Parent as Control)
        {
            if (current is TextBox)
                return false;
            if (ReferenceEquals(current, _ribbonControl))
                return true;
        }

        return false;
    }

    private bool IsSlidePaneListTarget(Control? focused)
    {
        for (var current = focused; current is not null; current = current.Parent as Control)
        {
            if (current is TextBox)
                return false;
            if (ReferenceEquals(current, _slidePaneList))
                return true;
        }

        return false;
    }

    private void QueueClipboardCopy()
    {
        var command = PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.EditCopyCommand);
        if (TryQueueActiveRichClipboard(static editor => editor.CopySelectionAsync(), command))
            return;

        var request = _clipboardService.PrepareWrite(Editor);
        QueueClipboardOperation(async () =>
            ReportClipboardWriteFailureIfAny(
                await _clipboardService.ExecuteCopyAsync(request),
                command,
                _clipboardService.LastWriteFailureMessage));
    }

    private void QueueClipboardCut()
    {
        var command = PresentationShellTextCatalog.Resolve(PresentationShellTextCatalog.EditCutCommand);
        if (TryQueueActiveRichClipboard(static editor => editor.CutSelectionAsync(), command))
            return;

        var request = _clipboardService.PrepareWrite(Editor);
        QueueClipboardOperation(async () =>
            ReportClipboardWriteFailureIfAny(
                await _clipboardService.ExecuteCutAsync(request),
                command,
                _clipboardService.LastWriteFailureMessage));
    }

    private void ReportClipboardWriteFailureIfAny(bool succeeded, string command, string? errorMessage)
    {
        if (succeeded || errorMessage is not { } error)
            return;

        _statusText.Text = SisterAppFileTextPlanner.FormatCommandFailed(FileText, command, error);
    }

    private void QueueClipboardPaste()
    {
        if (TryQueueActiveRichClipboard(static editor => editor.PasteClipboardAsync()))
            return;

        var request = _clipboardService.PreparePaste(Editor);
        QueueClipboardOperation(() => _clipboardService.ExecutePasteAsync(request));
    }

    private bool TryQueueActiveRichClipboard(
        Func<AvaloniaInCanvasTextEditor, Task<bool>> operation,
        string? failureCommandName = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var textEditor = _textEditor;
        if (textEditor?.IsRichTextEditActive != true)
            return false;

        QueueClipboardOperation(async () =>
        {
            var succeeded = await operation(textEditor);
            if (failureCommandName is not null)
            {
                ReportClipboardWriteFailureIfAny(
                    succeeded,
                    failureCommandName,
                    textEditor.LastWriteFailureMessage);
            }
        });
        return true;
    }

    private void QueueClipboardOperation(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        _clipboardOperationQueue.Enqueue(operation);
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

        var token = transition.Token!;

        if (_ribbonKeyTipTabId is null)
        {
            if (_ribbonControl is null ||
                !AvaloniaRibbonRenderer.TryActivateTopLevelKeyTip(_ribbonControl, token))
                return false;

            _ribbonKeyTipTabId = GetSelectedRibbonTabId();
            _ribbonKeyTipGroupId = null;
            _ribbonKeyTipSequence = string.Empty;
            args.Handled = _ribbonKeyTipTabId is not null;
            return args.Handled;
        }

        if (TryHandleNestedRibbonKeyTip(token))
        {
            args.Handled = true;
            return true;
        }

        if (_ribbonKeyTipMenuItems is not null)
        {
            SetRibbonKeyTipsVisible(false);
            args.Handled = true;
            return true;
        }

        // WPF keeps key-tip mode active after an unmatched character, so the
        // user can recover with another key or Escape without invoking a document shortcut.
        _ribbonKeyTipSequence = string.Empty;
        args.Handled = true;
        return true;
    }

    private void SetRibbonKeyTipsVisible(bool visible)
    {
        if (!visible)
        {
            if (_ribbonKeyTipFlyout is not null)
                AvaloniaRibbonRenderer.SetMenuKeyTipsVisible(_ribbonKeyTipFlyout, false);
            _ribbonKeyTipFlyout?.Hide();
        }

        _ribbonKeyTipFlyout = null;
        _ribbonKeyTipMenuItems = null;
        _ribbonKeyTipRenderedMenuItems = null;
        _ribbonKeyTipsVisible = visible;
        _ribbonKeyTipTabId = null;
        _ribbonKeyTipGroupId = null;
        _ribbonKeyTipSequence = string.Empty;
        if (_ribbonControl is not null)
            AvaloniaRibbonRenderer.SetTopLevelKeyTipsVisible(_ribbonControl, visible);
    }

    private bool TryHandleNestedRibbonKeyTip(string token)
    {
        if (_ribbonKeyTipMenuItems is not null)
            return TryHandleRibbonMenuKeyTip(token);

        var tab = _ribbonDefinition?.FindTab(_ribbonKeyTipTabId!);
        if (tab is null)
            return false;

        _ribbonKeyTipSequence += token;

        if (_ribbonKeyTipGroupId is null)
        {
            var groupResolution = RibbonKeyTipResolutionPlanner.Resolve(
                tab.Groups,
                _ribbonKeyTipSequence,
                group => GetVisibleRibbonGroupKeyTip(tab, group));
            if (groupResolution.Kind == RibbonKeyTipResolutionKind.Exact)
            {
                var exactGroup = tab.Groups[groupResolution.ExactIndex];
                _ribbonKeyTipGroupId = exactGroup.Id;
                _ribbonKeyTipSequence = string.Empty;
                TryEnterCollapsedRibbonGroupKeyTipScope(exactGroup);
                return true;
            }

            if (groupResolution.Kind == RibbonKeyTipResolutionKind.Prefix)
                return true;

            // Some WPF ribbons expose a command directly after the tab. Support that
            // form when the key tip is unique across the active tab.
            var directControls = tab.Groups
                .SelectMany(group => group.Controls)
                .ToArray();
            var directResolution = ResolveRibbonControlKeyTip(directControls, _ribbonKeyTipSequence);
            return directResolution.Kind == RibbonKeyTipResolutionKind.Exact &&
                   TryExecuteRibbonKeyTipCommand(directControls[directResolution.ExactIndex]);
        }

        var group = tab.FindGroup(_ribbonKeyTipGroupId);
        if (group is null)
            return false;

        var resolution = ResolveRibbonControlKeyTip(group.Controls, _ribbonKeyTipSequence);
        return resolution.Kind switch
        {
            RibbonKeyTipResolutionKind.Exact =>
                TryExecuteRibbonKeyTipCommand(group.Controls[resolution.ExactIndex]),
            RibbonKeyTipResolutionKind.Prefix => true,
            _ => false,
        };
    }

    private RibbonKeyTipResolution ResolveRibbonControlKeyTip(
        IReadOnlyList<RibbonControl> controls,
        string sequence)
    {
        // Office keeps an exact leaf pending only when the longer candidate opens a
        // nested scope. A longer leaf by itself must not make a short access key
        // ambiguous; a dropdown or split button must remain reachable by its prefix.
        return RibbonKeyTipResolutionPlanner.Resolve(
            controls,
            sequence,
            control => control.KeyTip,
            control => IsRibbonCommandEnabled(control.CommandId),
            control => control is RibbonDropdown or RibbonSplitButton);
    }

    private bool TryHandleRibbonMenuKeyTip(string token)
    {
        var items = _ribbonKeyTipMenuItems;
        if (items is null)
            return false;

        _ribbonKeyTipSequence += token;
        var resolution = RibbonKeyTipResolutionPlanner.Resolve(
            items,
            _ribbonKeyTipSequence,
            item => item.KeyTip,
            item => item.IsEnabled,
            item => item.Children.Count > 0);
        if (resolution.Kind == RibbonKeyTipResolutionKind.Exact)
        {
            var exactItem = items[resolution.ExactIndex];
            if (exactItem.Children.Count > 0)
            {
                var renderedParent = FindRenderedMenuItem(exactItem, items);
                if (_ribbonKeyTipFlyout is not null && renderedParent is null)
                    return false;

                if (renderedParent is not null)
                {
                    renderedParent.IsSubMenuOpen = true;
                    AvaloniaRibbonRenderer.SetMenuKeyTipsVisible(renderedParent, true);
                    _ribbonKeyTipRenderedMenuItems = renderedParent.Items.OfType<MenuItem>().ToArray();
                }

                _ribbonKeyTipMenuItems = exactItem.Children;
                _ribbonKeyTipSequence = string.Empty;
                return true;
            }

            if (exactItem.CommandId is not { } commandId || !TryExecuteRibbonKeyTipCommand(commandId))
                return false;

            return true;
        }

        return resolution.Kind == RibbonKeyTipResolutionKind.Prefix;
    }

    private bool TryExecuteRibbonKeyTipCommand(RibbonControl control)
    {
        if (control is RibbonSeparator or RibbonRowBreak or RibbonLabel)
            return false;

        if (!IsRibbonCommandEnabled(control.CommandId))
            return false;

        if (control is RibbonComboBox)
        {
            var combo = FindRibbonComboBox(control.CommandId);
            if (combo is null)
                return false;

            combo.Focus();
            combo.IsDropDownOpen = true;
            SetRibbonKeyTipsVisible(false);
            return true;
        }

        if (control is RibbonSplitButton split && split.Menu.Items.Count > 0)
            return EnterRibbonMenuKeyTipScope(split, split.Menu);

        if (control is RibbonDropdown dropdown && dropdown.Menu.Items.Count > 0)
            return EnterRibbonMenuKeyTipScope(dropdown, dropdown.Menu);

        if (_ribbonCommandRegistry is null ||
            !_ribbonCommandRegistry.TryGet(control.CommandId, out var command) ||
            command is null)
            return false;

        command.Execute(RibbonCommandContext.Empty);
        SetRibbonKeyTipsVisible(false);
        return true;
    }

    private bool TryExecuteRibbonKeyTipCommand(RibbonCommandId commandId)
    {
        if (!IsRibbonCommandEnabled(commandId) ||
            _ribbonCommandRegistry is null ||
            !_ribbonCommandRegistry.TryGet(commandId, out var command) ||
            command is null)
            return false;

        command.Execute(RibbonCommandContext.Empty);
        SetRibbonKeyTipsVisible(false);
        return true;
    }

    private bool IsRibbonCommandEnabled(RibbonCommandId commandId)
    {
        if (_ribbonCommandRegistry is null ||
            !_ribbonCommandRegistry.TryGet(commandId, out var command) ||
            command is null)
            return false;

        return command is not IRibbonStatefulCommand stateful || stateful.GetState().IsEnabled;
    }

    private bool EnterRibbonMenuKeyTipScope(RibbonControl control, RibbonMenu menu)
    {
        var flyout = ShowRibbonFlyout(control.CommandId);
        _ribbonKeyTipMenuItems = menu.Items;
        _ribbonKeyTipRenderedMenuItems = flyout is MenuFlyout menuFlyout
            ? menuFlyout.Items.OfType<MenuItem>().ToArray()
            : Array.Empty<MenuItem>();
        _ribbonKeyTipSequence = string.Empty;
        _ribbonKeyTipFlyout = flyout;
        if (flyout is not null)
            AvaloniaRibbonRenderer.SetMenuKeyTipsVisible(flyout, true);
        return true;
    }

    private void TryEnterCollapsedRibbonGroupKeyTipScope(RibbonGroup group)
    {
        var button = FindCollapsedRibbonGroupButton(group);
        if (button?.Flyout is not MenuFlyout flyout)
            return;

        flyout.ShowAt(button);
        AvaloniaRibbonRenderer.SetMenuKeyTipsVisible(flyout, true);
        _ribbonKeyTipFlyout = flyout;
        _ribbonKeyTipMenuItems = BuildCollapsedGroupKeyTipItems(group);
        _ribbonKeyTipRenderedMenuItems = flyout.Items.OfType<MenuItem>().ToArray();
    }

    private Button? FindCollapsedRibbonGroupButton(RibbonGroup group) =>
        _ribbonControl?
            .GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(button =>
                string.Equals(button.Tag as string, $"collapsed:{group.Id}", StringComparison.Ordinal));

    private string? GetVisibleRibbonGroupKeyTip(RibbonTab tab, RibbonGroup group)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in tab.Groups)
        {
            var keyTip = RibbonCollapsedGroupPresentationPlanner.DeriveGroupKeyTip(candidate.Header, used);
            if (string.Equals(candidate.Id, group.Id, StringComparison.Ordinal))
                return FindCollapsedRibbonGroupButton(candidate) is not null
                    ? keyTip
                    : candidate.KeyTip;
        }

        return group.KeyTip;
    }

    private IReadOnlyList<RibbonMenuItem> BuildCollapsedGroupKeyTipItems(RibbonGroup group)
    {
        var items = new List<RibbonMenuItem>();
        foreach (var control in RibbonCollapsedGroupPresentationPlanner.GetOverflowControls(group, includeSeparators: true))
        {
            switch (control)
            {
                case RibbonSeparator:
                    items.Add(RibbonMenuItem.Separator());
                    break;
                case RibbonComboBox combo:
                    items.Add(new RibbonMenuItem(combo.Label) { IsEnabled = false });
                    break;
                case RibbonSplitButton split:
                    items.Add(new RibbonMenuItem(split.Label, split.CommandId, split.KeyTip));
                    foreach (var menuItem in split.Menu.Items)
                    {
                        if (menuItem.Kind != RibbonMenuItemKind.Separator &&
                            menuItem.CommandId is { } commandId &&
                            (commandId == split.CommandId ||
                             string.Equals(menuItem.Header, split.Label, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        items.Add(menuItem);
                    }
                    break;
                default:
                    var menu = GetRibbonMenu(control);
                    items.Add(new RibbonMenuItem(
                        control.Label,
                        control.CommandId,
                        control.KeyTip,
                        Children: menu?.Items));
                    break;
            }
        }

        return items;
    }

    private static RibbonMenu? GetRibbonMenu(RibbonControl control) => control switch
    {
        RibbonSplitButton split => split.Menu,
        RibbonDropdown dropdown => dropdown.Menu,
        _ => null,
    };

    private MenuItem? FindRenderedMenuItem(RibbonMenuItem logicalItem, IReadOnlyList<RibbonMenuItem> scope)
    {
        if (_ribbonKeyTipRenderedMenuItems is null)
            return null;

        var renderedIndex = 0;
        for (var index = 0; index < scope.Count; index++)
        {
            if (scope[index].Kind == RibbonMenuItemKind.Separator)
                continue;

            if (ReferenceEquals(scope[index], logicalItem))
                return renderedIndex < _ribbonKeyTipRenderedMenuItems.Count
                    ? _ribbonKeyTipRenderedMenuItems[renderedIndex]
                    : null;

            renderedIndex++;
        }

        return null;
    }

    private FlyoutBase? ShowRibbonFlyout(RibbonCommandId commandId)
    {
        if (_ribbonControl is null)
            return null;

        return ShowRibbonFlyout(_ribbonControl, commandId);
    }

    internal static FlyoutBase? ShowRibbonFlyout(Control ribbon, RibbonCommandId commandId)
    {
        var buttons = ribbon
            .GetVisualDescendants()
            .OfType<Button>()
            .Where(candidate => candidate.Flyout is not null)
            .ToArray();
        var dropdownTag = $"{commandId.Value}.Dropdown";
        var button = buttons.FirstOrDefault(candidate =>
                string.Equals(candidate.Tag as string, dropdownTag, StringComparison.Ordinal))
            ?? buttons.FirstOrDefault(candidate =>
                string.Equals(candidate.Tag as string, commandId.Value, StringComparison.Ordinal));
        if (button?.Flyout is not { } flyout)
            return null;

        flyout.ShowAt(button);
        return flyout;
    }

    private ComboBox? FindRibbonComboBox(RibbonCommandId commandId)
    {
        if (_ribbonControl is null)
            return null;

        static bool IsCommandCombo(ComboBox combo, RibbonCommandId commandId) =>
            string.Equals(combo.Tag as string, commandId.Value, StringComparison.Ordinal);

        // Key-tip execution targets the rendered control. The logical tree can omit
        // controls inside a realized TabItem template in the headless presenter.
        return _ribbonControl.GetVisualDescendants()
                   .OfType<ComboBox>()
                   .FirstOrDefault(combo => IsCommandCombo(combo, commandId))
               ?? _ribbonControl.GetLogicalDescendants()
                   .OfType<ComboBox>()
                   .FirstOrDefault(combo => IsCommandCombo(combo, commandId));
    }

    private string? GetSelectedRibbonTabId()
        => (_ribbonControl as TabControl)?.SelectedItem is TabItem { Tag: string tabId }
            ? tabId
            : null;

    private static bool KeyTipEquals(string? keyTip, string sequence) =>
        string.Equals(
            RibbonKeyTipText.Normalize(keyTip),
            RibbonKeyTipText.Normalize(sequence),
            StringComparison.Ordinal);

    private static bool KeyTipStartsWith(string? keyTip, string sequence) =>
        RibbonKeyTipText.Normalize(keyTip) is { } normalizedKeyTip &&
        RibbonKeyTipText.Normalize(sequence) is { } normalizedSequence &&
        normalizedKeyTip.StartsWith(normalizedSequence, StringComparison.Ordinal);

    private static bool TryMapKeyboardKey(Key key, out FreePKeyboardKey mapped)
    {
        mapped = key switch
        {
            Key.A => FreePKeyboardKey.A,
            Key.C => FreePKeyboardKey.C,
            Key.D => FreePKeyboardKey.D,
            Key.F => FreePKeyboardKey.F,
            Key.H => FreePKeyboardKey.H,
            Key.N => FreePKeyboardKey.N,
            Key.O => FreePKeyboardKey.O,
            Key.P => FreePKeyboardKey.P,
            Key.S => FreePKeyboardKey.S,
            Key.V => FreePKeyboardKey.V,
            Key.X => FreePKeyboardKey.X,
            Key.Y => FreePKeyboardKey.Y,
            Key.Z => FreePKeyboardKey.Z,
            Key.Delete => FreePKeyboardKey.Delete,
            Key.F5 => FreePKeyboardKey.F5,
            Key.F10 => FreePKeyboardKey.F10,
            Key.Apps => FreePKeyboardKey.Apps,
            Key.Escape => FreePKeyboardKey.Escape,
            _ => default,
        };

        return key is Key.A or Key.C or Key.D or Key.F or Key.H or Key.N or Key.O or
            Key.P or Key.S or Key.V or Key.X or Key.Y or Key.Z or Key.Delete or Key.F5 or
            Key.F10 or Key.Apps or Key.Escape;
    }

    private static FreePKeyboardModifiers ToKeyboardModifiers(KeyModifiers modifiers)
    {
        var result = FreePKeyboardModifiers.None;
        if ((modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
            result |= FreePKeyboardModifiers.Control;
        if ((modifiers & KeyModifiers.Shift) != 0)
            result |= FreePKeyboardModifiers.Shift;
        if ((modifiers & KeyModifiers.Alt) != 0)
            result |= FreePKeyboardModifiers.Alt;
        return result;
    }

    // ── Slide show launch ──────────────────────────────────────────────────────

    /// <summary>
    /// Opens the Avalonia fullscreen slide show window.
    /// </summary>
    /// <param name="fromStart">
    ///   true  = start from slide 0 (F5 / "From Beginning").
    ///   false = start from the currently selected slide (Shift+F5 / "From Current").
    /// </param>
    internal void StartSlideShow(bool fromStart)
        => StartSlideShow(fromStart, FreeP.App.Compositor.SlideShowTimingIntent.None);

    private void StartSlideShowWithTiming(FreeP.App.Compositor.SlideShowTimingIntent timingIntent)
        => StartSlideShow(fromStart: true, timingIntent: timingIntent);

    private void StartSlideShow(
        bool fromStart,
        FreeP.App.Compositor.SlideShowTimingIntent timingIntent,
        int? animationStartIndex = null)
        => SlideShowWindowLauncher.TryLaunch(fromStart, timingIntent, animationStartIndex);

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
            CreateSlideShowWindow,
            static (window, intent) => window.SetPresenterTimingIntent(intent),
            ShowSlideShowWindow);

    private SlideShowWindow CreateSlideShowWindow(SlideShowWindowLaunchPlan launchPlan)
    {
        var window = new SlideShowWindow(launchPlan);
        ConfigureSlideShowObserver(window);
        window.Closed += (_, _) => RestoreOwnerFocus();
        return window;
    }

    private void ShowSlideShowWindow(SlideShowWindow window)
    {
        if (IsVisible)
            window.Show(this);
        else
            window.Show();
    }

    internal void OpenCustomShowDialog() =>
        RunGuarded(OpenCustomShowDialogAsync, UiText.Get("Shell_Command_CustomShow"));


    private async Task OpenCustomShowDialogAsync()
    {
        var dialog = new CustomShowDialog(
            _customShowSession,
            name => TryStartCustomSlideShow(name));
        await dialog.ShowDialog(this);
    }

}
