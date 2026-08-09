using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaRectangle = Avalonia.Controls.Shapes.Rectangle;
using Free.Shared.AppServices;
using Free.Shared.AppServices.Printing;
using Free.Shared.Drawing;
using Free.Shared.IO;
using Free.Shared.Pdf.Skia;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;
using Free.Shared.Ribbon.KeyTips;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;
using Free.Shared.Theme;
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
public sealed partial class MainWindow : Window
{
    // Avalonia text metrics place the action row two pixels above WPF without this compensation.
    private const double ReadingOrderActionTopCompensation = 2;

    private const string DefaultTitle = "FreeP";
    private static readonly SisterAppFileTextSpec FileText = PresentationFileTextResources.Presentation;
    private static readonly FilePickerFileType PictureFileType =
        AvaloniaFilePickerTypeAdapter.CreateFileType(
            PresentationFileTextResources.PictureFileTypeName,
            ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.svg", "*.wmf", "*.emf"],
            ["image/png", "image/jpeg", "image/gif", "image/bmp", "image/svg+xml", "image/x-wmf", "image/x-emf"]);
    private static readonly FilePickerFileType VideoFileType =
        AvaloniaFilePickerTypeAdapter.CreateFileType(
            PresentationFileTextResources.VideoFileTypeName,
            ["*.mp4", "*.mov", "*.avi", "*.wmv", "*.m4v"],
            ["video/mp4", "video/quicktime", "video/x-msvideo", "video/x-ms-wmv", "video/x-m4v"]);
    private static readonly FilePickerFileType AudioFileType =
        AvaloniaFilePickerTypeAdapter.CreateFileType(
            PresentationFileTextResources.AudioFileTypeName,
            PresentationMediaFileTypeCatalog.AudioFilePatterns,
            PresentationMediaFileTypeCatalog.AudioMimeTypes);
    private static readonly FilePickerFileType EmbeddedObjectFileType =
        AvaloniaFilePickerTypeAdapter.CreateFileType(
            OleInsertionPlanner.PickerTitle,
            ["*.xlsx", "*.xlsm", "*.xls", "*.docx", "*.doc", "*.pptx", "*.ppt"],
            [
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "application/vnd.ms-excel.sheet.macroEnabled.12",
                "application/vnd.ms-excel",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                "application/msword",
                "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                "application/vnd.ms-powerpoint",
            ]);


    // ── Presentation model ─────────────────────────────────────────────────────

    private readonly PresentationWorkareaSession _workareaSession;
    private Presentation _presentation => _workareaSession.Presentation;
    private readonly SisterAvaloniaFileCommandWorkflow _fileWorkflow;
    private readonly PresentationFileCommandSession _fileSession;
    private readonly StartupDirtyTrace? _startupDirtyTrace;
    private readonly SisterAvaloniaAsyncWindowCloseCoordinator _closeCoordinator;
    private readonly AvaloniaPresentationClipboardService _clipboardService;
    private Func<FileOpenPickerPlan, Task<string?>>? _openPickerOverrideForTests;
    private Func<FileSavePickerPlan, Task<string?>>? _savePickerOverrideForTests;
    internal Func<FileSavePickerPlan, Task<VideoPickerSelectionForTests?>>? VideoPickerOverrideForTests { get; set; }
    private int _ownerFocusRestoreCount;
    private readonly PresentationClipboardOperationQueue _clipboardOperationQueue = new();
    private readonly FreePOptions _options;
    private readonly ApplicationOptionsStore<FreePOptions> _optionsStore;
    private LinuxNativeOutputCapabilities _nativeOutputCapabilities;
    private ILinuxNativePrintHandoffAdapter _nativePrintAdapter;
    private readonly IPlatformPrintService _printService;
    private readonly Func<Window, PrinterDiscoveryResult, PrintSelection?, CancellationToken, Task<PrintSelection?>>
        _showPrintSelectionDialog;
    private readonly bool _portablePrintWorkflowEnabled;
    private PrinterDiscoveryResult? _latestPrinterDiscovery;
    private ILinuxVideoExportAdapter _videoExportAdapter;
    private PresentationNativePrintHandoffHostCapabilities _nativePrintHostCapabilities;
    private PresentationVideoExportHandoffHostCapabilities _videoExportHostCapabilities;
    private readonly Func<LinuxNativeOutputCapabilities>? _nativeOutputCapabilityDetector;
    private readonly Func<PresentationPrintRequest?, PresentationPrintOutputPackage>? _printOutputPackageFactory;
    private readonly Func<PresentationVideoExportRequest?, PresentationVideoFramePackage>? _videoFramePackageFactory;
    private bool _nativeOutputDetectionStarted;
    private CancellationTokenSource? _nativeOutputCancellation;

    // ── Editing session ────────────────────────────────────────────────────────

    internal EditingSession Editor => _workareaSession.Editor;

    // ── UI elements ────────────────────────────────────────────────────────────

    private readonly SlideCanvas _slideCanvas;
    private Border _canvasHost = null!;
    private Canvas _oleOverlay = null!;
#if FREEP_WINDOWS_CAPTURE
    private AvaloniaOleInPlaceHost? _activeOleHost;
#endif
    private readonly ListBox _slidePaneList;
    private readonly Border _slidePaneInsertionIndicator;
    private readonly Button _slidePaneNewSlideButton;
    private readonly TextBox _notesBox;
    private readonly TextBlock _statusText;
    private readonly BackstageView _backstage;
    private Task<LinuxNativePrintResult> _backstagePrintOperation =
        Task.FromResult(LinuxNativePrintResult.Failed("No Backstage print action has run."));
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
    private bool _reviewCommentsPaneRequested;
    private readonly PresentationReviewWorkflowSession _reviewWorkflowSession;
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
    private bool _altTextPaneRefreshing;
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
    private bool _mediaCaptionPaneRefreshing;
    private readonly PresentationMediaPaneSession _mediaPaneSession;
    private readonly PresentationSmartArtTextPaneSession _smartArtTextPaneSession;
    private readonly PresentationZoomAuthoringSession _zoomAuthoringSession;
    private readonly PresentationDomainContextMenuSession _domainContextMenuSession;
    private readonly PresentationNotesPaneSession _notesPaneSession;
    private readonly PresentationHyperlinkWorkflowSession _hyperlinkWorkflowSession;
    private readonly AnimationPaneSession _animationPaneSession;
    private readonly AnimationPaneControlSchemaPlan _animationPaneControlSchema;
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
    private FreePRibbonHostActionEndpoints? _ribbonHostActionEndpoints;
    private readonly RibbonStateStore _ribbonStateStore = new();
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

    // ── Smoke surface ──────────────────────────────────────────────────────────

    /// <summary>True once the ribbon has been built. Read by the launch-smoke coordinator.</summary>
    internal bool HasToolbar { get; private set; }

    /// <summary>Current slide count — read by the launch-smoke coordinator.</summary>
    internal int SlideCount => _presentation.Slides.Count;

    /// <summary>Current slide index (0-based) — read by the launch-smoke coordinator.</summary>
    internal int CurrentSlideIndex => Editor?.CurrentSlideIndex ?? -1;
    internal bool RibbonKeyTipsVisibleForTests => _ribbonKeyTipsVisible;
    internal bool RibbonKeyTipMenuOpenForTests => _ribbonKeyTipMenuItems is not null;
    internal bool RibbonKeyTipFlyoutOpenForTests => _ribbonKeyTipFlyout?.IsOpen == true;
    internal bool SlideCanvasFocusedForTests => _slideCanvas.IsFocused;
    internal IReadOnlyList<MenuItem> RibbonKeyTipRenderedMenuItemsForTests =>
        _ribbonKeyTipRenderedMenuItems ?? Array.Empty<MenuItem>();
    internal void SetRibbonKeyTipMenuScopeForTests(RibbonMenu menu, MenuFlyout flyout)
    {
        _ribbonKeyTipsVisible = true;
        _ribbonKeyTipMenuItems = menu.Items;
        _ribbonKeyTipFlyout = flyout;
        _ribbonKeyTipRenderedMenuItems = flyout.Items.OfType<MenuItem>().ToArray();
        _ribbonKeyTipSequence = string.Empty;
    }
    internal bool HandleRibbonMenuKeyTipForTests(string token) => TryHandleRibbonMenuKeyTip(token);
    internal RibbonCommandRegistry RibbonCommandRegistryForTests => _ribbonCommandRegistry!;
    internal Control? RibbonControlForTests => _ribbonControl;
    internal Border TitleBarForTests => _titleBar;
    internal IReadOnlyList<Button> QuickAccessButtonsForTests => _quickAccessButtons;
    internal string StatusTextForTests => _statusText.Text ?? string.Empty;
    internal bool HasWindowIconForTests => Icon is not null;
    internal int OwnerFocusRestoreCountForTests => _ownerFocusRestoreCount;
    internal void RaiseKeyDownForTests(KeyEventArgs args) => MainWindow_KeyDown(this, args);
    internal Task ClipboardOperationForTests => _clipboardOperationQueue.Completion;
    internal int SlidePaneSlideItemCount => _slidePaneList.Items
        .OfType<ListBoxItem>()
        .Count(item => item.Tag is int);
    internal int SlidePaneSectionHeaderCount => _slidePaneList.Items
        .OfType<ListBoxItem>()
        .Count(item => item.Tag is SlidePaneSectionHeaderTag);
    internal bool IsSlidePaneInsertionIndicatorVisible => _slidePaneInsertionIndicator.IsVisible;
    internal bool IsSlidePaneNewSlideButtonVisible => _slidePaneNewSlideButton.IsVisible;
    internal string? SlidePaneNewSlideButtonText => _slidePaneNewSlideButton.Content?.ToString();
    internal string? SlidePaneNewSlideButtonAutomationName => AutomationProperties.GetName(_slidePaneNewSlideButton);
    internal Button SlidePaneNewSlideButtonForTests => _slidePaneNewSlideButton;
    internal IReadOnlyList<string?> SelectionPaneRenameToolTipsForTests => _selectionPane.RenameToolTipsForTests;
    internal bool IsShellShortcutTargetForTests(Control? focused) => IsShellShortcutTarget(focused);
    internal ListBoxItem? SelectedSlidePaneItemForTests => GetCurrentSlidePaneItem();
    internal IReadOnlyList<int> SlidePaneSelectedSlideIndicesForTests =>
        _workareaSession.SlidePaneSession.Selection.SelectedSlideIndices;
    internal IReadOnlyList<SlidePaneThumbnailVisualPlan> SlidePaneRenderedThumbnailPlans =>
        _workareaSession.SlidePaneSession.Projection.Items
            .Select(item => item.Thumbnail)
            .OfType<SlidePaneThumbnailVisualPlan>()
            .ToArray();
    internal IReadOnlyList<SlidePaneSectionHeaderVisualPlan> SlidePaneRenderedSectionHeaderPlans =>
        _workareaSession.SlidePaneSession.Projection.Items
            .Select(item => item.SectionHeader)
            .OfType<SlidePaneSectionHeaderVisualPlan>()
            .ToArray();
    internal IReadOnlyList<string?> SlidePaneSectionHeaderAutomationNamesForTests => _slidePaneList.Items
        .OfType<ListBoxItem>()
        .Where(item => item.Tag is SlidePaneSectionHeaderTag)
        .Select(AutomationProperties.GetName)
        .ToArray();
    internal IReadOnlyList<string?> SlidePaneThumbnailAutomationNamesForTests => _slidePaneList.Items
        .OfType<ListBoxItem>()
        .Where(item => item.Tag is int)
        .Select(AutomationProperties.GetName)
        .ToArray();

    internal bool IsDirty => _fileWorkflow.IsDirty;
    internal int DirtyGeneration => _fileWorkflow.DirtyGeneration;
    internal IReadOnlyList<StartupDirtyTraceEntry> StartupDirtyTraceForTests =>
        _startupDirtyTrace?.Entries ?? Array.Empty<StartupDirtyTraceEntry>();
    internal bool IsCloseDecisionPendingForTests => _closeCoordinator.IsClosePending;
    internal PresentationViewShowState ViewShowStateForTests => _viewShowState;
    internal PresentationViewZoomState ViewZoomStateForTests => _viewZoomState;
    internal PresentationViewZoomState SlideCanvasViewZoomStateForTests => _slideCanvas.ViewZoomState;
    internal bool? GestureSnapToGridForTests => _gestureHandler?.SnapToGrid;
    internal bool? GestureSnapToShapesForTests => _gestureHandler?.SnapToShapes;

    internal string? CurrentPath => _fileWorkflow.CurrentPath;

    internal IReadOnlyList<RecentFileEntry> RecentEntries => _fileWorkflow.RecentEntries;

    internal PresentationCommentPanePlan? LastCommentPanePlan => _reviewWorkflowSession.LastCommentPanePlan;
    internal PresentationCommentNavigationPlan? LastCommentNavigationPlan => _reviewWorkflowSession.LastCommentNavigationPlan;
    internal PresentationCommentMentionPickerPlan? LastCommentMentionPickerPlan =>
        _reviewWorkflowSession.LastCommentMentionPickerPlan;
    internal PresentationCommentMentionInsertionPlan? LastCommentMentionInsertionPlan =>
        _reviewWorkflowSession.LastCommentMentionInsertionPlan;
    internal PresentationAccessibilitySummaryPlan? LastAccessibilitySummaryPlan =>
        _reviewWorkflowSession.LastAccessibilitySummaryPlan;
    internal PresentationAccessibilityCheckerPanePlan? LastAccessibilityCheckerPanePlan =>
        _reviewWorkflowSession.LastAccessibilityCheckerPanePlan;
    internal PresentationSlideTitleMutationPlan? LastSlideTitleMutationPlan =>
        _reviewWorkflowSession.LastSlideTitleMutationPlan;
    internal PresentationChartTitleMutationPlan? LastChartTitleMutationPlan =>
        _reviewWorkflowSession.LastChartTitleMutationPlan;
    internal PresentationTableHeaderRowMutationPlan? LastTableHeaderRowMutationPlan =>
        _reviewWorkflowSession.LastTableHeaderRowMutationPlan;
    internal PresentationTableStructureReviewPlan? LastTableStructureReviewPlan =>
        _reviewWorkflowSession.LastTableStructureReviewPlan;
    internal PresentationTableStructureReviewDisplayPlan? LastTableStructureReviewDisplayPlan =>
        _reviewWorkflowSession.LastTableStructureReviewDisplayPlan;
    internal PresentationAltTextRequestPlan? LastAltTextRequestPlan => _reviewWorkflowSession.LastAltTextRequestPlan;
    internal PresentationAltTextPanePlan? LastAltTextPanePlan => _reviewWorkflowSession.LastAltTextPanePlan;
    internal PresentationReadingOrderPlan? LastReadingOrderPlan => _reviewWorkflowSession.LastReadingOrderPlan;
    internal PresentationProofingRequestPlan? LastProofingRequestPlan => _reviewWorkflowSession.LastProofingRequestPlan;
    internal PresentationProofingExecutionPlan? LastProofingExecutionPlan => _reviewWorkflowSession.LastProofingExecutionPlan;
    internal PresentationProofingPanePlan? LastProofingPanePlan => _reviewWorkflowSession.LastProofingPanePlan;
    internal PresentationMediaTranscriptPlan? LastMediaTranscriptPlan =>
        _reviewWorkflowSession.LastMediaTranscriptPlan;
    internal PresentationMediaCaptionAuthoringPanePlan? LastMediaCaptionAuthoringPanePlan =>
        _mediaPaneSession.LastCaptionAuthoringPanePlan;
    internal PresentationMediaCaptionAuthoringMutationPlan? LastMediaCaptionAuthoringMutationPlan =>
        _mediaPaneSession.LastCaptionAuthoringMutationPlan;
    internal PresentationMediaCaptionTrackMutationResult? LastMediaCaptionTrackMutationResult =>
        _mediaPaneSession.LastCaptionTrackMutationResult;
    internal SmartArtTextPaneApplyResult? LastSmartArtTextPaneApplyResult =>
        _smartArtTextPaneSession.LastTextPaneApplyResult;
    internal SmartArtNodeEditResult? LastSmartArtTextPaneEditResult =>
        _smartArtTextPaneSession.LastTextPaneEditResult;
    internal SmartArtTextPaneKeyboardRoute? LastSmartArtTextPaneKeyboardRoute =>
        _smartArtTextPaneSession.LastKeyboardRoute;
    internal SmartArtColorApplyResult? LastSmartArtColorApplyResult =>
        _smartArtTextPaneSession.LastColorApplyResult;
    internal SmartArtDataPartRewriteResult? LastSmartArtDataPartRewriteResult =>
        _smartArtTextPaneSession.LastDataPartRewriteResult;
    internal SmartArtDrawingCacheRegenerationResult? LastSmartArtDrawingCacheRegenerationResult =>
        _smartArtTextPaneSession.LastDrawingCacheRegenerationResult;
    internal AnimationPaneTimelinePlan? LastAnimationPaneTimelinePlan => _animationPaneSession.Timeline;
    internal AnimationPaneWorkflowEvidencePlan? LastAnimationPaneWorkflowEvidencePlan =>
        _animationPaneSession.WorkflowEvidence;
    internal AnimationPanePlaybackSessionPlan? LastAnimationPanePlaybackSessionPlan => _animationPaneSession.Playback;
    internal AnimationPanePlaybackWorkflowEvidencePlan? LastAnimationPanePlaybackWorkflowEvidencePlan =>
        _animationPaneSession.PlaybackWorkflowEvidence;
    internal FindReplaceWorkflowPlan? LastFindReplaceWorkflowPlan { get; private set; }
    internal PresentationDesignCommandPlan? LastCustomSlideSizeRequestPlan { get; private set; }
    internal SlideSizeDialogInitialState? LastCustomSlideSizeInitialState { get; private set; }
    internal SlideSizeDialogResultPlan? LastCustomSlideSizeResultPlan { get; private set; }
    internal HeaderFooterCommandFocus? LastHeaderFooterFocus { get; private set; }
    internal HeaderFooterState? LastHeaderFooterState { get; private set; }
    internal HeaderFooterApplyPlan? LastHeaderFooterApplyPlan { get; private set; }
    internal HyperlinkDialogRequest? LastHyperlinkDialogRequest { get; private set; }
    internal HyperlinkDialogApplyPlan? LastHyperlinkDialogApplyPlan { get; private set; }
    internal Func<HyperlinkDialogRequest, Task<Hyperlink?>>? HyperlinkDialogResultProviderForTests { get; set; }
    internal Func<Task<PresentationPictureBulletPayload?>>? PictureBulletPayloadProviderForTests { get; set; }
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
    internal LinuxNativePrintResult? LastNativePrintResult { get; private set; }
    internal PrinterDiscoveryResult? LatestPrinterDiscoveryForTests => _latestPrinterDiscovery;
    internal PrintSelection? LastPrintSelectionForTests { get; private set; }
    internal LinuxVideoExportResult? LastVideoExportResult { get; private set; }
    internal bool NativeOutputDetectionStartedForTests => _nativeOutputDetectionStarted;
    internal PresentationNativePrintHandoffHostCapabilities NativePrintHostCapabilitiesForTests => _nativePrintHostCapabilities;
    internal PresentationVideoExportHandoffHostCapabilities VideoExportHostCapabilitiesForTests => _videoExportHostCapabilities;
    internal void StartNativeOutputCapabilityDetectionForTests() => StartNativeOutputCapabilityDetection();
    internal PresentationLayoutPickerPlan? LastLayoutPickerPlan { get; private set; }
    internal PresentationLayoutChoice? LastAppliedLayoutChoice { get; private set; }
    internal TableInsertionPickerPlan? LastTablePickerPlan { get; private set; }
    internal bool IsLayoutPickerVisible => _layoutPickerHost?.IsVisible == true;
    internal bool IsTablePickerVisible => _tablePickerHost?.IsVisible == true;
    internal SlideSizeDialog? ActiveSlideSizeDialog => _slideSizeDialog;
    internal HeaderFooterDialog? ActiveHeaderFooterDialog => _headerFooterDialog;
    internal SlideShowSettingsDialog? ActiveSlideShowSettingsDialog => _slideShowSettingsDialog;
    internal int TablePickerChoiceButtonCount => LastTablePickerPlan?.Choices.Count ?? 0;
    internal int TablePickerDefaultChoiceCount => LastTablePickerPlan?.Choices.Count(choice => choice.IsDefault) ?? 0;
    internal int LayoutPickerChoiceButtonCount => LastLayoutPickerPlan?.Choices.Count ?? 0;
    internal int LayoutPickerGroupHeaderCount => LastLayoutPickerPlan?.Groups.Count ?? 0;
    internal int LayoutPickerThumbnailPlaceholderCount =>
        LastLayoutPickerPlan?.Choices.Sum(choice => choice.ThumbnailPlaceholders.Count) ?? 0;
    internal int LayoutPickerCurrentChoiceCount =>
        LastLayoutPickerPlan?.Choices.Count(choice => choice.Chrome.IsCurrent) ?? 0;
    internal bool IsReviewCommentsPaneVisible => _reviewCommentsPaneHost?.IsVisible == true;
    internal int ReviewCommentsPaneCommentCount => LastCommentPanePlan?.Comments.Count ?? 0;
    internal int ReviewCommentsPaneActionButtonCount => LastCommentPanePlan?.Actions.Count ?? 0;
    internal int ReviewCommentsPaneSelectedCommentCount => LastCommentPanePlan?.Comments.Count(comment => comment.IsSelected) ?? 0;
    internal string ReviewCommentsPaneSummary => LastCommentPanePlan?.DeckSummaryLabel ?? string.Empty;
    internal IReadOnlyList<string> ReviewCommentsPaneFilterStates =>
        LastCommentPanePlan?.Filters.Select(filter =>
            $"{filter.Kind}|{filter.Label}|{filter.Count}|{filter.IsSelected}|{filter.HasMatches}").ToArray() ?? [];
    internal IReadOnlyList<string> ReviewCommentsPaneRenderedActionStates =>
        EnumerateReviewPaneButtons(_reviewCommentsPanePanel)
            .Where(button => button.Tag is string commandId &&
                commandId.StartsWith("freep.review.comments.", StringComparison.Ordinal))
            .Select(button => $"{button.Tag}|{button.Content}|{button.IsEnabled}")
            .ToArray();
    internal IReadOnlyList<string> ReviewCommentsPaneRenderedMentionLines =>
        EnumerateReviewPaneText(_reviewCommentsPanePanel)
            .Where(text => text.StartsWith("Mentions:", StringComparison.Ordinal))
            .ToArray();
    internal IReadOnlyList<string> ReviewCommentsPaneRenderedMentionActions =>
        EnumerateReviewPaneButtons(_reviewCommentsPanePanel)
            .Where(button => button.Tag is string tag &&
                tag.StartsWith("comment-mention:", StringComparison.Ordinal))
            .Select(button => $"{button.Tag}|{button.Content}|{button.IsEnabled}")
            .ToArray();
    internal bool InvokeReviewCommentPaneMentionActionForTests(string tag, string? candidateLabel = null)
    {
        var button = EnumerateReviewPaneButtons(_reviewCommentsPanePanel)
            .FirstOrDefault(candidate => string.Equals(candidate.Tag as string, tag, StringComparison.Ordinal));
        if (button is null)
            return false;

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var item = button.ContextMenu?.Items.OfType<MenuItem>()
            .FirstOrDefault(candidate => candidateLabel is null ||
                string.Equals(candidate.Header as string, candidateLabel, StringComparison.Ordinal));
        if (item is null)
            return candidateLabel is null;

        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        return true;
    }
    internal bool IsAltTextPaneVisible => _altTextPaneHost?.IsVisible == true;
    internal bool IsAltTextPaneApplyEnabled => _altTextApplyButton?.IsEnabled == true;
    internal string AltTextPaneTitleLabel => _altTextTitleLabel?.Text ?? string.Empty;
    internal string AltTextPaneTitleText => _altTextTitleBox?.Text ?? string.Empty;
    internal string AltTextPaneTitlePlaceholder => _altTextTitleBox?.PlaceholderText ?? string.Empty;
    internal string AltTextPaneDescriptionLabel => _altTextDescriptionLabel?.Text ?? string.Empty;
    internal string AltTextPaneDescriptionText => _altTextDescriptionBox?.Text ?? string.Empty;
    internal string AltTextPaneDescriptionPlaceholder => _altTextDescriptionBox?.PlaceholderText ?? string.Empty;
    internal bool IsAltTextPaneDecorativeChecked => _altTextDecorativeCheck?.IsChecked == true;
    internal string AltTextPaneMessage => _altTextPaneMessage?.Text ?? string.Empty;
    internal bool IsSmartArtTextPaneVisible => _smartArtTextPaneHost?.IsVisible == true;
    internal int SmartArtTextPaneRowCount => _smartArtTextPaneRowsPanel?.Children.OfType<TextBox>().Count() ?? 0;
    internal int SmartArtTextPaneSelectedRowCount =>
        _smartArtTextPaneRowsPanel?.Children.OfType<TextBox>().Count(box =>
            box.Tag is SmartArtNodeOutlineItem item &&
            StringComparer.Ordinal.Equals(item.ModelId, _smartArtTextPaneSession.SelectedModelId)) ?? 0;
    internal int SmartArtTextPaneActionButtonCount => _smartArtTextPaneActionButtons.Count;
    internal int SmartArtTextPaneEnabledActionButtonCount =>
        _smartArtTextPaneActionButtons.Count(button => button.IsEnabled);
    internal int SmartArtTextPaneCommandActionCount =>
        _smartArtTextPaneCommandActions?.Children.OfType<Button>().Count() ?? 0;
    internal bool SmartArtTextPaneCommandActionsWrap =>
        _smartArtTextPaneCommandActions is not null;
    internal string SmartArtTextPaneMessage => _smartArtTextPaneMessage?.Text ?? string.Empty;
    internal IReadOnlyList<string> SmartArtTextPaneRenderedRows =>
        _smartArtTextPaneRowsPanel?.Children.OfType<TextBox>()
            .Select(box => box.Tag is SmartArtNodeOutlineItem item
                ? $"{item.ModelId}|{item.Level}|{item.IsAssistant}|{box.Text}"
                : box.Text ?? string.Empty)
            .ToArray() ?? [];
    internal bool IsAccessibilityCheckerPaneVisible => _accessibilityCheckerPaneHost?.IsVisible == true;
    internal int AccessibilityCheckerPaneRowCount => LastAccessibilityCheckerPanePlan?.Rows.Count ?? 0;
    internal int AccessibilityCheckerPaneSelectedRowCount =>
        LastAccessibilityCheckerPanePlan?.Rows.Count(row => row.IsSelected) ?? 0;
    internal string AccessibilityCheckerPaneHeading => _accessibilityCheckerPaneHeading?.Text ?? string.Empty;
    internal string AccessibilityCheckerPaneMessage => _accessibilityCheckerPaneMessage?.Text ?? string.Empty;
    internal IReadOnlyList<string> AccessibilityCheckerTableStructureReviewRenderedLines =>
        _accessibilityCheckerTableStructureReviewRenderedLines.ToArray();
    internal bool IsReadingOrderPaneVisible => _readingOrderPaneHost?.IsVisible == true;
    internal int ReadingOrderPaneItemCount => LastReadingOrderPlan?.Items.Count ?? 0;
    internal string ReadingOrderPaneHeading => _readingOrderPaneHeading?.Text ?? string.Empty;
    internal string ReadingOrderPaneMessage => _readingOrderPaneMessage?.Text ?? string.Empty;
    internal bool IsReadingOrderMoveEarlierEnabled => _readingOrderMoveEarlierButton?.IsEnabled == true;
    internal bool IsReadingOrderMoveLaterEnabled => _readingOrderMoveLaterButton?.IsEnabled == true;
    internal bool IsProofingPaneVisible => _proofingPaneHost?.IsVisible == true;
    internal int ProofingPaneIssueRowCount => LastProofingPanePlan?.Rows.Count ?? 0;
    internal int ProofingPaneSelectedIssueCount => LastProofingPanePlan?.Rows.Count(row => row.IsSelected) ?? 0;
    internal bool IsProofingPaneCorrectionEnabled =>
        LastProofingPanePlan?.Actions.SingleOrDefault(action =>
            action.CommandId == PresentationReviewWorkflowPlanner.ProofingApplyCorrectionCommandId)?.IsEnabled == true;
    internal bool IsProofingPaneIgnoreEnabled =>
        LastProofingPanePlan?.Actions.SingleOrDefault(action =>
            action.CommandId == PresentationReviewWorkflowPlanner.ProofingIgnoreCommandId)?.IsEnabled == true;
    internal bool IsProofingPaneIgnoreAllEnabled =>
        LastProofingPanePlan?.Actions.SingleOrDefault(action =>
            action.CommandId == PresentationReviewWorkflowPlanner.ProofingIgnoreAllCommandId)?.IsEnabled == true;
    internal bool IsProofingPaneAddToDictionaryEnabled =>
        LastProofingPanePlan?.Actions.SingleOrDefault(action =>
            action.CommandId == PresentationReviewWorkflowPlanner.ProofingAddToDictionaryCommandId)?.IsEnabled == true;
    internal string ProofingPaneHeading => _proofingPaneHeading?.Text ?? string.Empty;
    internal string ProofingPaneMessage => _proofingPaneMessage?.Text ?? string.Empty;
    internal bool IsMediaCaptionPaneVisible => _mediaCaptionPaneHost?.IsVisible == true;
    internal string MediaCaptionPaneHeading => _mediaCaptionPaneHeading?.Text ?? string.Empty;
    internal string MediaCaptionPaneMessage => _mediaCaptionPaneMessage?.Text ?? string.Empty;
    internal int MediaCaptionPaneTrackCount => LastMediaCaptionAuthoringPanePlan?.Tracks.Count ?? 0;
    internal bool IsMediaCaptionCreateEnabled => _mediaCaptionCreateButton?.IsEnabled == true;
    internal bool IsMediaCaptionReplaceEnabled => _mediaCaptionReplaceButton?.IsEnabled == true;
    internal bool IsMediaCaptionDeleteEnabled => _mediaCaptionDeleteButton?.IsEnabled == true;
    internal string MediaCaptionPaneTranscriptText => _mediaCaptionTranscriptBox?.Text ?? string.Empty;
    internal int MediaVolumePercent => _mediaVolumeSlider is null
        ? 80
        : PresentationMediaPaneSession.NormalizeVolumePercent(_mediaVolumeSlider.Value);
    internal bool IsMediaVolumeApplyEnabled => _mediaVolumeApplyButton?.IsEnabled == true;
    internal MediaPlaybackStartMode MediaPlaybackStartMode =>
        PresentationMediaPaneSession.GetPlaybackStartMode(_mediaStartModeBox?.SelectedIndex ?? -1);
    internal bool MediaLoop => _mediaLoopCheckBox?.IsChecked == true;
    internal bool MediaShowWhenStopped => _mediaShowWhenStoppedCheckBox?.IsChecked != false;
    internal bool MediaRewindAfterPlaying => _mediaRewindAfterPlayingCheckBox?.IsChecked == true;
    internal bool MediaPlayFullScreen => _mediaPlayFullScreenCheckBox?.IsChecked == true;
    internal int MediaStopAfterSlides => int.TryParse(_mediaStopAfterSlidesBox?.Text, out var value)
        ? Math.Max(1, value)
        : 1;
    internal string? ReadingOrderMoveEarlierDisabledReason =>
        LastReadingOrderPlan?.Actions.SingleOrDefault(action =>
            action.CommandId == PresentationReviewWorkflowPlanner.ReadingOrderMoveEarlierCommandId)?.DisabledReason;
    internal string? ReadingOrderMoveLaterDisabledReason =>
        LastReadingOrderPlan?.Actions.SingleOrDefault(action =>
            action.CommandId == PresentationReviewWorkflowPlanner.ReadingOrderMoveLaterCommandId)?.DisabledReason;
    internal bool IsAnimationPaneVisible => _animationPaneHost?.IsVisible == true;
    internal bool EditPointsEnabledForTests => _slideCanvas.EditPointsEnabled;
    internal int AnimationPaneItemCount => LastAnimationPaneTimelinePlan?.Items.Count ?? 0;
    internal int AnimationPaneRenderedItemCount => _animationPaneItemsPanel?.Children.Count ?? 0;
    internal string AnimationPaneHeading => LastAnimationPaneWorkflowEvidencePlan?.View.Heading
        ?? _animationPaneHeading?.Text
        ?? string.Empty;
    internal string AnimationPaneMessage => _animationPaneMessage?.Text ?? string.Empty;
    internal bool IsAnimationPanePreviewEnabled => _animationPanePreviewButton?.IsEnabled == true;
    internal IReadOnlyList<string> AnimationPanePlaybackControls => _animationPaneRenderedPlaybackControls;
    internal IReadOnlyList<string> AnimationPaneRenderedRows => _animationPaneRenderedRows;
    internal IReadOnlyList<string> AnimationPaneWorkflowEvidenceLines =>
        LastAnimationPaneWorkflowEvidencePlan?.EvidenceLines ?? Array.Empty<string>();
    internal int AnimationPaneEffectOptionControlCount => _animationPaneEffectOptionControlCount;
    internal int AnimationPaneTriggerControlCount => _animationPaneTriggerControlCount;
    internal int AnimationPaneDurationControlCount => _animationPaneDurationControlCount;
    internal int AnimationPaneDelayControlCount => _animationPaneDelayControlCount;
    internal FindReplaceDialog? ActiveFindReplaceDialog => _findReplaceDialog;
    internal bool IsFindReplaceDialogVisible => _findReplaceDialog?.IsVisible == true;
    internal bool IsFindReplaceReplaceInputVisible => _findReplaceDialog?.ShowReplace == true;
    internal bool IsPrintOptionsPaneVisible => _printOptionsPaneHost?.IsVisible == true;
    internal IReadOnlyList<PresentationPaneAccessibilitySnapshotEntry> PaneAccessibilitySnapshotForTests =>
        _paneAccessibility.BuildSnapshot();
    internal void FocusRepresentativePanesForAccessibilityValidation()
    {
        _slidePaneList.Focus();
    }
    internal string PaneAccessibilitySnapshotSerializationForTests =>
        _paneAccessibility.SerializeSnapshot();
    internal TextBox NotesPaneForAccessibilityTests => _notesBox;
    internal ListBox SlidePaneForAccessibilityTests => _slidePaneList;
    internal Border CommentsPaneForAccessibilityTests => _reviewCommentsPaneHost;
    internal IReadOnlyList<Control> CommentsPaneItemsForAccessibilityTests =>
        _reviewCommentsPanePanel is null
            ? Array.Empty<Control>()
            : _reviewCommentsPanePanel.Children
                .OfType<Control>()
                .Where(item => AutomationProperties.GetAutomationId(item)
                    ?.StartsWith("FreePCommentsPaneItem", StringComparison.Ordinal) == true)
                .ToArray();
    internal SelectionPane SelectionPaneForAccessibilityTests => _selectionPane;
    internal Border AnimationPaneForAccessibilityTests => _animationPaneHost;
    internal IReadOnlyList<Control> SelectionPaneItemsForAccessibilityTests =>
        _selectionPane?.AccessibilityItemsForTests ?? Array.Empty<Control>();
    internal IReadOnlyList<Control> AnimationPaneItemsForAccessibilityTests =>
        _animationPaneItemsPanel?.Children.OfType<Control>().ToArray() ?? Array.Empty<Control>();
    internal IReadOnlyList<Control> SlidePaneItemsForAccessibilityTests =>
        _slidePaneList is null
            ? Array.Empty<Control>()
            : _slidePaneList.Items
                .OfType<ListBoxItem>()
                .Where(item => AutomationProperties.GetAutomationId(item)
                    ?.StartsWith("FreePSlidePaneItem", StringComparison.Ordinal) == true)
                .Cast<Control>()
                .ToArray();
    internal string PrintOptionsPaneHeading => _printOptionsPaneHeading?.Text ?? string.Empty;
    internal string PrintOptionsPaneMessage => _printOptionsPaneMessage?.Text ?? string.Empty;
    internal int PrintOptionsPaneRenderedRowCount => _printOptionsPaneRowsPanel?.Children.Count ?? 0;
    internal IReadOnlyList<string> PrintOptionsPaneRenderedOptionLines => _printOptionsPaneRenderedOptionLines;
    internal IReadOnlyList<string> PrintOptionsPaneRenderedPreviewRows => _printOptionsPaneRenderedPreviewRows;
    internal IReadOnlyList<string> PrintOptionsPaneRenderedLayoutRows => _printOptionsPaneRenderedLayoutRows;
    internal IReadOnlyList<string> PrintOptionsPaneRenderedRangeRows => _printOptionsPaneRenderedRangeRows;
    internal bool ApplyPrintCustomRangeForTests(string rangeText)
    {
        if (_printCustomRangeInput is null || _printCustomRangeApplyButton is null)
            return false;

        _printCustomRangeInput.Text = rangeText;
        _printCustomRangeApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        return true;
    }
    internal bool ApplyBackstageCustomPrintRangeForTests(string rangeText) =>
        _backstage.ApplyCustomPrintRangeForTests(rangeText);
    internal IReadOnlyList<(string AutomationId, bool IsEnabled)> BackstagePrintActionsForTests =>
        _backstage.PrintActionsForTests;
    internal bool InvokeBackstagePrintActionForTests(string automationId) =>
        _backstage.InvokePrintActionForTests(automationId);
    internal Task<LinuxNativePrintResult> BackstagePrintOperationForTests => _backstagePrintOperation;
    internal bool IsBackstageOpen => _backstage.IsOpen;
    internal string? CurrentBackstagePaneLabel => _backstage.CurrentPaneLabel;
    internal IReadOnlyList<SisterBackstageEntryPlan<Control>> BackstageEntries => _backstage.Entries;

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
        IPresentationSystemClipboard? systemClipboard = null,
        IPresentationClipboardShapeRenderer? clipboardRenderer = null,
        LinuxNativeOutputCapabilities? nativeOutputCapabilities = null,
        ILinuxNativePrintHandoffAdapter? nativePrintAdapter = null,
        ILinuxVideoExportAdapter? videoExportAdapter = null,
        Func<LinuxNativeOutputCapabilities>? nativeOutputCapabilityDetector = null,
        Func<PresentationPrintRequest?, PresentationPrintOutputPackage>? printOutputPackageFactory = null,
        Func<PresentationVideoExportRequest?, PresentationVideoFramePackage>? videoFramePackageFactory = null,
        bool enableStartupDirtyTrace = false,
        IPlatformPrintService? printService = null,
        Func<Window, PrinterDiscoveryResult, PrintSelection?, CancellationToken, Task<PrintSelection?>>?
            showPrintSelectionDialog = null,
        ApplicationOptionsStore<FreePOptions>? optionsStore = null)
    {
        _startupDirtyTrace = enableStartupDirtyTrace ? new StartupDirtyTrace() : null;
        Title = DefaultTitle;
        Width = 1280;
        Height = 760;
        MinWidth = 800;
        MinHeight = 500;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));
        ApplyWindowIcon();
        _optionsStore = optionsStore ?? ApplicationOptionsStore<FreePOptions>.Create();
        _options = options ?? new FreePOptions();
        _options.Normalize();
        _nativeOutputCapabilities = nativeOutputCapabilities ??
            LinuxNativeOutputCapabilities.Unavailable("Native output capability detection is pending.");
        _nativePrintAdapter = nativePrintAdapter ?? CreateNativePrintAdapter(_nativeOutputCapabilities.Print);
        _printService = printService ?? new CupsPrintService();
        _showPrintSelectionDialog = showPrintSelectionDialog ??
            ((owner, discovery, requested, cancellationToken) =>
                CupsPrintDialog.ShowAsync(owner, discovery, requested, cancellationToken: cancellationToken));
        _portablePrintWorkflowEnabled = printService is not null || nativePrintAdapter is null;
        _videoExportAdapter = videoExportAdapter ?? CreateVideoExportAdapter(_nativeOutputCapabilities.Video);
        _nativePrintHostCapabilities = BuildNativePrintHostCapabilities(_nativeOutputCapabilities.Print);
        _videoExportHostCapabilities = BuildVideoExportHostCapabilities(_nativeOutputCapabilities.Video);
        _nativeOutputCapabilityDetector = nativeOutputCapabilityDetector ??
            (nativeOutputCapabilities is null ? DetectNativeOutputCapabilities : null);
        _printOutputPackageFactory = printOutputPackageFactory;
        _videoFramePackageFactory = videoFramePackageFactory;
        _clipboardService = new AvaloniaPresentationClipboardService(
            systemClipboard ?? new AvaloniaPresentationSystemClipboard(
                () => TopLevel.GetTopLevel(this)?.Clipboard),
            clipboardRenderer ?? new AvaloniaClipboardShapeRenderer());

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
            PlaceholderText = "Click to add notes",
            MinHeight       = FreePShellVisualMetrics.NotesPaneHeight,
            MaxHeight       = 120,
            Padding         = new Thickness(8, 4),
            FontSize        = 12,
            Background      = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xF0)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
        };
        _notesBox.TextChanged += OnNotesTextChanged;

        _statusText = SisterAppStatusBarChrome.CreateInfoText(
            foreground: ResolveThemeBrush("FreePWhiteBrush", Brushes.White),
            margin: new Thickness(12, 0, 0, 0));
        _fileWorkflow = new SisterAvaloniaFileCommandWorkflow(
            owner: this,
            titleSpec: new SisterAvaloniaFileTitleSpec(
                ApplicationName: DefaultTitle,
                Separator: " \u2014 ",
                ApplicationPlacement: WindowTitleApplicationPlacement.DocumentThenApplication),
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
            new AvaloniaPresentationFileLifecyclePort(_fileWorkflow),
            new AvaloniaPresentationFilePickerPort(this),
            new AvaloniaPresentationFileRenderPort(),
            new AvaloniaPresentationPrintPort(this),
            new AvaloniaPresentationVideoPort(this),
            new AvaloniaPresentationFileFeedbackPort(this),
            getImageExportRange: () => PresentationExportPlanner.BuildCurrentSlideRangeRequest(Editor.CurrentSlideIndex),
            getPrintCurrentSlideNumber: () => Editor.CurrentSlideIndex + 1,
            printPackageFactory: _printOutputPackageFactory,
            videoPackageFactory: _videoFramePackageFactory);
        _startupDirtyTrace?.Record("file-workflow-created", _fileWorkflow);
        _closeCoordinator = new SisterAvaloniaAsyncWindowCloseCoordinator(
            confirmCloseAllowedAsync: () => _fileSession.ConfirmCloseAllowedAsync(),
            requestClose: Close,
            restoreOwnerFocus: RestoreOwnerFocus);

        _reviewWorkflowSession = new(
            () => Editor,
            new PresentationReviewWorkflowSessionCallbacks(
                MarkDirty: () => _fileWorkflow.MarkDirty(),
                RefreshCanvas: RefreshCanvas,
                RefreshNotesPane: RefreshNotesPane,
                RenderAccessibilityCheckerPaneIfVisible: RenderAccessibilityCheckerPaneIfVisible,
                PresentAccessibilityCheckerPane: PresentAccessibilityCheckerPane,
                OpenAltTextPane: () => ShowAltTextPane(),
                OpenHyperlinkDialog: () => OpenHyperlinkDialog(),
                OpenMediaCaptionPane: () => ShowMediaCaptionPane(),
                RenderCommentPane: ShowReviewCommentsPane,
                RenderAltTextPaneIfVisible: RenderAltTextPaneIfVisible,
                RenderReadingOrderPaneIfVisible: RenderReadingOrderPaneIfVisible,
                PresentReadingOrderPane: PresentReadingOrderPane,
                RenderProofingPaneIfVisible: RenderProofingPaneIfVisible,
                PresentProofingPane: PresentProofingPane,
                UpdateAfterCommentMutation: UpdateStatus,
                UpdateAfterCommentNavigation: UpdateStatus,
                UpdateAfterProofingCorrection: UpdateStatus));
        _mediaPaneSession = new(
            () => Editor,
            new PresentationMediaPaneSessionCallbacks(
                MarkDirty: () => _fileWorkflow.MarkDirty(),
                RefreshReviewWorkflowPlans: RefreshReviewWorkflowPlans,
                UpdateHost: UpdateStatus,
                RefreshPane: RefreshVisibleMediaCaptionPaneFromFields));
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
        _animationPaneControlSchema = AnimationPanePlanner.BuildControlSchema();
        _customShowSession = new(() => Editor);


        // ── Root layout ───────────────────────────────────────────────────────

        var ribbon = BuildRibbon();
        var statusBar = SisterAppStatusBarChrome.Build(new SisterAppStatusBarSpec(
            Background: ResolveThemeBrush(
                "FreePStatusSurfaceBrush",
                new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A))),
            LeftContent: _statusText)).Root;
        var frame = SisterAppClientFrameBuilder.Build(SisterAppClientFrameSpec.ForWorkArea(
            chrome: ribbon,
            workArea: BuildBody(),
            statusBar: statusBar));
        _backstage = new BackstageView(BuildBackstageCallbacks());
        var clientRoot = new Grid();
        clientRoot.Children.Add(frame.Root);
        clientRoot.Children.Add(_backstage);

        var windowFrame = SisterAppWindowFrameBuilder.Build(new SisterAppWindowFrameSpec(
            Window: this,
            Body: clientRoot,
            TitleBarBackground: ResolveThemeBrush(
                "FreePTitleBarBrush",
                new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A))),
            TitleBarForeground: ResolveThemeBrush("FreePWhiteBrush", Brushes.White),
            TitleBarHeight: FreePShellVisualMetrics.TitleBarHeight));
        _titleBar = windowFrame.TitleBar;
        _quickAccessButtons = SisterQuickAccessToolbarBuilder.Render(
            windowFrame.QatHost,
            new SisterQuickAccessToolbarActions(
                Save: () => _workareaSession.ExecuteCommand(FreePKeyboardCommand.SavePresentation),
                Undo: () => _workareaSession.ExecuteCommand(FreePKeyboardCommand.Undo),
                Redo: () => _workareaSession.ExecuteCommand(FreePKeyboardCommand.Redo)),
            ResolveThemeBrush("FreePWhiteBrush", Brushes.White));

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
        Closing += (_, e) => e.Cancel =
            !_allowCloseWithoutDirtyPromptForPhysicalValidation &&
            _closeCoordinator.ShouldCancelClosing();
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

        var startupPresentation = startupArguments
            .FirstOrDefault(a => IsSupportedPresentationPath(a) && File.Exists(a));
        _startupDirtyTrace?.Record(
            startupPresentation is null ? "startup-load-not-requested" : "startup-load-begin",
            _fileWorkflow);

        Exception? startupOpenError = null;
        if (startupPresentation is not null)
        {
            try
            {
                var result = PresentationFilePersistenceWorkflow.Open(startupPresentation);
                LoadPresentationAsSaved(result.Presentation, result.SavedPath, result.SuppressRecentFiles);
                _startupDirtyTrace?.Record("startup-load-saved", _fileWorkflow);
                _statusText.Text = SisterAppFileTextPlanner.FormatOpened(FileText, Path.GetFileName(startupPresentation));
            }
            catch (Exception ex)
            {
                startupOpenError = ex;
                LoadPresentationAsSaved(_presentation, path: null);
                _startupDirtyTrace?.Record("startup-load-failed-fallback-saved", _fileWorkflow);
                _statusText.Text = SisterAppFileTextPlanner.FormatCommandFailed(
                    FileText,
                    FileText.OpenCommand,
                    ex.Message);
            }
        }
        else
        {
            LoadPresentationAsSaved(_presentation, path: null);
            _startupDirtyTrace?.Record("startup-empty-saved", _fileWorkflow);
        }

        SeedPhysicalSmartArtTextPaneIfRequested();
        SeedPhysicalHyperlinkFixtureIfRequested();
        _startupDirtyTrace?.Record("startup-seeds-complete", _fileWorkflow);

        Content = windowFrame.Root;
        _startupDirtyTrace?.Record("content-assigned", _fileWorkflow);
        UpdateStatus();
        _startupDirtyTrace?.Record("constructor-complete", _fileWorkflow);
        Opened += (_, _) => _startupDirtyTrace?.Record("window-opened", _fileWorkflow);
        if (startupOpenError is not null)
        {
            var error = startupOpenError;
            Opened += async (_, _) => await _fileWorkflow.ShowFileCommandErrorAsync(
                "Could not open the presentation",
                error);
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

    private void SeedPhysicalAnimationPaneFixtureIfRequested()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("FREEP_PHYSICAL_ANIMATION_PANE_SEED"),
                "1",
                StringComparison.Ordinal) ||
            Editor.CurrentSlide is null ||
            Editor.CurrentSlide.Animations.Count > 0)
        {
            return;
        }

        var shape = Editor.InsertTextBox("Animation Pane sample");
        Editor.CurrentSlide.Animations.Add(new ShapeAnimation
        {
            ShapeId = shape.Id,
            Kind = AnimationKind.Entrance,
            Preset = AnimationPreset.Fade,
            Trigger = AnimationTrigger.OnClick,
            DurationMs = 500,
        });
        RefreshCanvas();
    }

    private void SeedPhysicalSmartArtTextPaneIfRequested()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("FREEP_PHYSICAL_SMARTART_TEXT_PANE_SEED"),
                "1",
                StringComparison.Ordinal) ||
            Editor.CurrentSlide is null)
        {
            return;
        }

        var smartArt = Editor.CurrentSlide.Shapes.FirstOrDefault(shape => shape.SmartArt is not null);
        if (smartArt is null)
        {
            return;
        }

        Editor.Select(smartArt.Id);
        ShowSmartArtTextPane();
        RefreshCanvas();
    }

    private void SeedPhysicalHyperlinkFixtureIfRequested()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("FREEP_PHYSICAL_HYPERLINK_SEED"),
                "1",
                StringComparison.Ordinal) ||
            _presentation.Slides.Count != 1)
        {
            return;
        }

        var firstSlide = Editor.CurrentSlide ?? _presentation.Slides[0];
        var shapeWidth = DrawingMlCoordinateUnits.EmuPerInch * 4;
        var shapeHeight = DrawingMlCoordinateUnits.EmuPerInch * 2;
        var linkShape = new SlideShape
        {
            Id = 9001,
            Name = "Physical internal-slide hyperlink target",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = (_presentation.SlideSizeCxEmu - shapeWidth) / 2,
            OffsetYEmu = (_presentation.SlideSizeCyEmu - shapeHeight) / 2,
            ExtentCxEmu = shapeWidth,
            ExtentCyEmu = shapeHeight,
            Fill = new ShapeFill.Solid(new SrgbColor(0x44, 0x72, 0xC3)),
            TextBody = new TextBody
            {
                Wrap = true,
                Paragraphs = { new Paragraph { Runs = { new Run { Text = "CLICK LINK TO SLIDE 2" } } } },
            },
        };
        Editor.AddShape(linkShape);
        if (linkShape.ExtentCxEmu <= 0 || linkShape.ExtentCyEmu <= 0 ||
            !firstSlide.Shapes.Any(shape => shape.Id == linkShape.Id))
        {
            throw new InvalidOperationException("Physical hyperlink fixture did not create a visible slide-1 rectangle.");
        }
        Editor.InsertSlide();
        Editor.InsertTextBox("TARGET SLIDE 2");
        Editor.SelectSlide(0);
        Editor.Select(linkShape.Id);
        RefreshCanvas();
        var fixturePostconditionPath = Environment.GetEnvironmentVariable("FREEP_PHYSICAL_HYPERLINK_FIXTURE_POSTCONDITION");
        if (!string.IsNullOrWhiteSpace(fixturePostconditionPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fixturePostconditionPath)!);
            File.WriteAllText(
                fixturePostconditionPath,
                $"slide1Id={firstSlide.Id}\nslide2Id={_presentation.Slides[1].Id}\ncurrentSlideIndex={Editor.CurrentSlideIndex}\nshapeOffsetXEmu={linkShape.OffsetXEmu}\nshapeOffsetYEmu={linkShape.OffsetYEmu}\nshapeExtentCxEmu={linkShape.ExtentCxEmu}\nshapeExtentCyEmu={linkShape.ExtentCyEmu}\nslideSizeCxEmu={_presentation.SlideSizeCxEmu}\nslideSizeCyEmu={_presentation.SlideSizeCyEmu}\n");
        }
    }

    private void ApplyWindowIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "FreeP.ico");
        if (!File.Exists(iconPath))
            return;

        try
        {
            using var stream = File.OpenRead(iconPath);
            Icon = new WindowIcon(stream);
        }
        catch
        {
            // An unsupported desktop icon must not prevent the presentation from opening.
        }
    }

    private void StartNativeOutputCapabilityDetection()
    {
        if (_nativeOutputDetectionStarted || _nativeOutputCapabilityDetector is null)
            return;

        _nativeOutputDetectionStarted = true;
        _ = Task.Run(_nativeOutputCapabilityDetector).ContinueWith(
            task =>
            {
                if (task.IsCanceled || task.IsFaulted)
                    return;

                Dispatcher.UIThread.Post(() =>
                {
                    _nativeOutputCapabilities = task.Result;
                    _nativePrintAdapter = CreateNativePrintAdapter(_nativeOutputCapabilities.Print);
                    _videoExportAdapter = CreateVideoExportAdapter(_nativeOutputCapabilities.Video);
                    _nativePrintHostCapabilities = BuildNativePrintHostCapabilities(_nativeOutputCapabilities.Print);
                    _videoExportHostCapabilities = BuildVideoExportHostCapabilities(_nativeOutputCapabilities.Video);
                    if (_printOptionsPaneHost?.IsVisible == true)
                        RenderPrintOptionsPane(RefreshPrintBackstagePlan(_printOptionsPaneRequest));
                });
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
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
        canvasStack.Children.Add(_adorner);

        _canvasHost = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)),
            ClipToBounds = true,
            Child      = canvasStack,
        };
        _layoutPickerPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
        };
        _layoutPickerHost = new Border
        {
            Background      = Brushes.White,
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
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
            Background      = Brushes.White,
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
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
                        Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
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
            Background      = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xE8)),
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
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
        _selectionPane = new SelectionPane(Editor);
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
        if (_notesBox is null || _reviewCommentsPaneHost is null)
            return;

        var commentPlan = LastCommentPanePlan;
        var accessibilityPlan = LastAccessibilityCheckerPanePlan;
        var readingOrderPlan = LastReadingOrderPlan;
        var proofingPlan = LastProofingPanePlan;
        var captionPlan = LastMediaCaptionAuthoringPanePlan;
        var smartArtItemCount = _smartArtTextPaneRowsPanel?.Children.Count ?? 0;
        var selectionPlan = _selectionPane.CurrentPlan;
        var animationPlan = _animationPaneSession.Refresh();
        var selectedSmartArtRow = _smartArtTextPaneRowsPanel?.Children
            .OfType<TextBox>()
            .FirstOrDefault(box =>
                box.Tag is SmartArtNodeOutlineItem item &&
                StringComparer.Ordinal.Equals(item.ModelId, _smartArtTextPaneSession.SelectedModelId));

        _paneAccessibility.ApplyPane(_slidePaneList, PresentationPaneAccessibilityPlanner.SlidePaneId, true,
            _presentation.Slides.Count, Editor.CurrentSlideIndex);
        _paneAccessibility.ApplyPane(_notesBox, PresentationPaneAccessibilityPlanner.NotesPaneId, true, 1);
        _paneAccessibility.ApplyPane(_reviewCommentsPaneHost, PresentationPaneAccessibilityPlanner.CommentsPaneId,
            _reviewCommentsPaneHost.IsVisible,
            commentPlan?.Comments.Count ?? 0, commentPlan?.SelectedCommentIndex ?? -1);
        _paneAccessibility.ApplyPane(_accessibilityCheckerPaneHost, PresentationPaneAccessibilityPlanner.AccessibilityPaneId,
            _accessibilityCheckerPaneHost.IsVisible,
            accessibilityPlan?.Rows.Count ?? _accessibilityCheckerRowsPanel?.Children.Count ?? 0,
            accessibilityPlan?.SelectedRowIndex ?? -1);
        _paneAccessibility.ApplyPane(_altTextPaneHost, PresentationPaneAccessibilityPlanner.AltTextPaneId,
            _altTextPaneHost.IsVisible, 3);
        _paneAccessibility.ApplyPane(_readingOrderPaneHost, PresentationPaneAccessibilityPlanner.ReadingOrderPaneId,
            _readingOrderPaneHost.IsVisible,
            readingOrderPlan?.Items.Count ?? _readingOrderPaneItemsPanel?.Children.Count ?? 0,
            readingOrderPlan?.SelectedItemIndex ?? -1);
        _paneAccessibility.ApplyPane(_proofingPaneHost, PresentationPaneAccessibilityPlanner.ProofingPaneId,
            _proofingPaneHost.IsVisible,
            proofingPlan?.Rows.Count ?? _proofingPaneRowsPanel?.Children.Count ?? 0,
            proofingPlan?.SelectedRowIndex ?? -1);
        _paneAccessibility.ApplyPane(_mediaCaptionPaneHost, PresentationPaneAccessibilityPlanner.MediaCaptionPaneId,
            _mediaCaptionPaneHost.IsVisible,
            captionPlan?.Tracks.Count ?? _mediaCaptionTrackBox?.Items.Count ?? 0,
            captionPlan?.SelectedTrackIndex ?? _mediaCaptionTrackBox?.SelectedIndex ?? -1);
        _paneAccessibility.ApplyPane(_smartArtTextPaneHost, PresentationPaneAccessibilityPlanner.SmartArtTextPaneId,
            _smartArtTextPaneHost.IsVisible,
            smartArtItemCount,
            selectedSmartArtRow is null || _smartArtTextPaneRowsPanel is null
                ? -1
                : _smartArtTextPaneRowsPanel.Children.IndexOf(selectedSmartArtRow));
        _paneAccessibility.ApplyPane(_selectionPane, PresentationPaneAccessibilityPlanner.SelectionPaneId,
            _selectionPane.IsVisible,
            selectionPlan.Items.Count,
            selectionPlan.SelectedItemIndex);
        _paneAccessibility.ApplyPane(_animationPaneHost, PresentationPaneAccessibilityPlanner.AnimationPaneId,
            _animationPaneHost.IsVisible,
            animationPlan?.Items.Count ?? _animationPaneItemsPanel?.Children.Count ?? 0,
            animationPlan?.SelectedIndex ?? -1);
    }

    private Border BuildPrintOptionsPaneHost()
    {
        _printOptionsPaneHeading = new TextBlock
        {
            Text = "Print",
            FontSize = 26,
            FontWeight = FontWeight.Light,
            Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
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
            Content = "Print",
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
            Background = Brushes.White,
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
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
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
            Content = "Close",
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
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
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
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(12, 12, 12, 4),
        };
        _mediaCaptionPaneMessage = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            Margin = new Thickness(12, 0, 12, 8),
        };
        _mediaCaptionTrackBox = new ComboBox
        {
            Margin = new Thickness(12, 0, 12, 6),
            MinHeight = 28,
        };
        _mediaCaptionTrackBox.SelectionChanged += (_, _) =>
        {
            if (_mediaCaptionPaneRefreshing || LastMediaCaptionAuthoringPanePlan is null)
                return;
            var selectedIndex = _mediaCaptionTrackBox.SelectedIndex;
            _mediaPaneSession.SelectCaptionTrack(selectedIndex >= 0
                && selectedIndex < LastMediaCaptionAuthoringPanePlan.Tracks.Count
                    ? LastMediaCaptionAuthoringPanePlan.Tracks[selectedIndex].TrackIndex
                    : null);
            RefreshVisibleMediaCaptionPaneFromFields();
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
            Minimum = 0,
            Maximum = 100,
            TickFrequency = 10,
            Margin = new Thickness(12, 0, 12, 4),
        };
        _mediaVolumeApplyButton = BuildMediaCaptionPaneButton();
        _mediaVolumeApplyButton.Content = PresentationPaneTextResources.ApplyVolume;
        _mediaVolumeApplyButton.Click += (_, _) => ApplyMediaVolumePane();
        _mediaStartModeText = BuildMediaCaptionPaneLabel();
        _mediaStartModeText.Text = PresentationPaneTextResources.PlaybackStart;
        _mediaStartModeBox = new ComboBox
        {
            Margin = new Thickness(12, 0, 12, 4),
            MinHeight = 28,
            ItemsSource = PresentationPaneTextResources.MediaPlaybackStartOptions
                .Select(option => option.Label)
                .ToArray(),
        };
        _mediaLoopCheckBox = new CheckBox
        {
            Content = PresentationPaneTextResources.LoopUntilStopped,
            Margin = new Thickness(12, 2, 12, 4),
        };
        _mediaShowWhenStoppedCheckBox = new CheckBox
        {
            Content = PresentationPaneTextResources.ShowWhenStopped,
            Margin = new Thickness(12, 2, 12, 4),
            IsChecked = true,
        };
        _mediaRewindAfterPlayingCheckBox = new CheckBox
        {
            Content = PresentationPaneTextResources.RewindAfterPlaying,
            Margin = new Thickness(12, 2, 12, 4),
        };
        _mediaPlayFullScreenCheckBox = new CheckBox
        {
            Content = PresentationPaneTextResources.PlayFullScreen,
            Margin = new Thickness(12, 2, 12, 4),
        };
        _mediaStopAfterSlidesText = BuildMediaCaptionPaneLabel();
        _mediaStopAfterSlidesText.Text = PresentationPaneTextResources.StopAfterSlides;
        _mediaStopAfterSlidesBox = BuildMediaCaptionPaneTextBox(singleLine: true);
        _mediaStopAfterSlidesBox.Text = "1";
        _mediaPlaybackApplyButton = BuildMediaCaptionPaneButton();
        _mediaPlaybackApplyButton.Content = PresentationPaneTextResources.ApplyPlayback;
        _mediaPlaybackApplyButton.Click += (_, _) => ApplyMediaPlaybackPane();
        _mediaTrimStartText = BuildMediaCaptionPaneLabel();
        _mediaTrimStartText.Text = PresentationPaneTextResources.TrimStartMilliseconds;
        _mediaTrimStartBox = BuildMediaCaptionPaneTextBox(singleLine: true);
        _mediaTrimEndText = BuildMediaCaptionPaneLabel();
        _mediaTrimEndText.Text = PresentationPaneTextResources.TrimEndMilliseconds;
        _mediaTrimEndBox = BuildMediaCaptionPaneTextBox(singleLine: true);
        _mediaFadeInText = BuildMediaCaptionPaneLabel();
        _mediaFadeInText.Text = PresentationPaneTextResources.FadeInMilliseconds;
        _mediaFadeInBox = BuildMediaCaptionPaneTextBox(singleLine: true);
        _mediaFadeOutText = BuildMediaCaptionPaneLabel();
        _mediaFadeOutText.Text = PresentationPaneTextResources.FadeOutMilliseconds;
        _mediaFadeOutBox = BuildMediaCaptionPaneTextBox(singleLine: true);
        _mediaTimingApplyButton = BuildMediaCaptionPaneButton();
        _mediaTimingApplyButton.Content = PresentationPaneTextResources.ApplyTiming;
        _mediaTimingApplyButton.Click += (_, _) => ApplyMediaTimingPane();
        _mediaBookmarkText = BuildMediaCaptionPaneLabel();
        _mediaBookmarkText.Text = PresentationPaneTextResources.MediaBookmarks;
        _mediaBookmarkBox = new ComboBox { Margin = new Thickness(12, 0, 12, 4), MinHeight = 28 };
        _mediaBookmarkBox.SelectionChanged += (_, _) =>
        {
            if (_mediaCaptionPaneRefreshing)
                return;
            _mediaPaneSession.SelectBookmark(_mediaBookmarkBox.SelectedItem is ComboBoxItem { Tag: int index }
                ? index
                : null);
            RefreshVisibleMediaCaptionPaneFromFields();
        };
        _mediaBookmarkNameText = BuildMediaCaptionPaneLabel();
        _mediaBookmarkNameText.Text = PresentationPaneTextResources.BookmarkName;
        _mediaBookmarkNameBox = BuildMediaCaptionPaneTextBox(singleLine: true);
        _mediaBookmarkTimeText = BuildMediaCaptionPaneLabel();
        _mediaBookmarkTimeText.Text = PresentationPaneTextResources.BookmarkTimeMilliseconds;
        _mediaBookmarkTimeBox = BuildMediaCaptionPaneTextBox(singleLine: true);
        _mediaBookmarkCreateButton = BuildMediaCaptionPaneButton();
        _mediaBookmarkCreateButton.Content = PresentationPaneTextResources.AddBookmark;
        _mediaBookmarkCreateButton.Click += (_, _) => ApplyMediaBookmarkCreatePane();
        _mediaBookmarkReplaceButton = BuildMediaCaptionPaneButton();
        _mediaBookmarkReplaceButton.Content = PresentationPaneTextResources.ReplaceBookmark;
        _mediaBookmarkReplaceButton.Click += (_, _) => ApplyMediaBookmarkReplacePane();
        _mediaBookmarkDeleteButton = BuildMediaCaptionPaneButton();
        _mediaBookmarkDeleteButton.Content = PresentationPaneTextResources.DeleteBookmark;
        _mediaBookmarkDeleteButton.Click += (_, _) => ApplyMediaBookmarkDeletePane();
        _mediaCaptionCreateButton = BuildMediaCaptionPaneButton();
        _mediaCaptionReplaceButton = BuildMediaCaptionPaneButton();
        _mediaCaptionDeleteButton = BuildMediaCaptionPaneButton();
        _mediaCaptionCloseButton = BuildMediaCaptionPaneButton();

        _mediaCaptionLabelBox.TextChanged += (_, _) => RefreshVisibleMediaCaptionPaneFromFields();
        _mediaCaptionLanguageBox.TextChanged += (_, _) => RefreshVisibleMediaCaptionPaneFromFields();
        _mediaCaptionSourceBox.TextChanged += (_, _) => RefreshVisibleMediaCaptionPaneFromFields();
        _mediaCaptionTranscriptBox.TextChanged += (_, _) => RefreshVisibleMediaCaptionPaneFromFields();
        _mediaCaptionCreateButton.Click += (_, _) => ApplyMediaCaptionPane(PresentationMediaCaptionAuthoringIntentKind.Create);
        _mediaCaptionReplaceButton.Click += (_, _) => ApplyMediaCaptionPane(PresentationMediaCaptionAuthoringIntentKind.Replace);
        _mediaCaptionDeleteButton.Click += (_, _) => ApplyMediaCaptionPane(PresentationMediaCaptionAuthoringIntentKind.Delete);
        _mediaCaptionCloseButton.Click += (_, _) => HideMediaCaptionPane();

        return new Border
        {
            Width = 320,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
            BorderThickness = new Thickness(1, 0, 0, 0),
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
                        Margin = new Thickness(12, 8, 12, 12),
                        Children =
                        {
                            _mediaCaptionCreateButton,
                            _mediaCaptionReplaceButton,
                            _mediaCaptionDeleteButton,
                            _mediaVolumeApplyButton,
                            _mediaPlaybackApplyButton,
                            _mediaTimingApplyButton,
                            _mediaBookmarkCreateButton,
                            _mediaBookmarkReplaceButton,
                            _mediaBookmarkDeleteButton,
                            _mediaCaptionCloseButton,
                        },
                    },
                },
            },
        };
    }

    private static TextBlock BuildMediaCaptionPaneLabel()
        => new()
        {
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(12, 6, 12, 2),
        };

    private static TextBox BuildMediaCaptionPaneTextBox(bool singleLine)
        => new()
        {
            AcceptsReturn = !singleLine,
            TextWrapping = singleLine ? TextWrapping.NoWrap : TextWrapping.Wrap,
            MinHeight = singleLine ? 28 : 128,
            MaxHeight = singleLine ? 28 : 180,
            Margin = new Thickness(12, 0, 12, 4),
            Padding = new Thickness(6, 4),
        };

    private static Button BuildMediaCaptionPaneButton()
        => new()
        {
            MinWidth = 72,
            Padding = new Thickness(10, 4),
            Margin = new Thickness(0, 0, 6, 6),
        };

    private Border BuildSmartArtTextPaneHost()
    {
        _smartArtTextPaneHeading = new TextBlock
        {
            Text = "SmartArt Text Pane",
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(12, 12, 12, 4),
        };
        _smartArtTextPaneMessage = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            Margin = new Thickness(12, 0, 12, 8),
        };
        _smartArtTextPaneRowsPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
        };
        _smartArtTextPaneAssistantButton = new Button
        {
            Content = "Toggle Assistant",
            MinWidth = 120,
            Padding = new Thickness(10, 4),
            Margin = new Thickness(0, 0, 8, 0),
        };
        _smartArtTextPanePictureButton = new Button
        {
            Content = "Replace picture",
            MinWidth = 120,
            Padding = new Thickness(10, 4),
            Margin = new Thickness(0, 0, 8, 0),
        };
        _smartArtTextPaneClearPictureButton = new Button
        {
            Content = "Remove picture",
            MinWidth = 120,
            Padding = new Thickness(10, 4),
            Margin = new Thickness(0, 0, 8, 0),
        };
        _smartArtTextPaneApplyButton = new Button
        {
            Content = "Apply",
            MinWidth = 72,
            Padding = new Thickness(10, 4),
            Margin = new Thickness(0, 0, 8, 0),
        };
        _smartArtTextPaneCloseButton = new Button
        {
            Content = "Close",
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
        AddSmartArtTextPaneActionButton(
            "Add sibling",
            "Add a sibling row after the selected SmartArt row.",
            SmartArtNodeEditKind.AddSiblingAfter);
        AddSmartArtTextPaneActionButton(
            "Add child",
            "Add a child row below the selected SmartArt row.",
            SmartArtNodeEditKind.AddChild);
        AddSmartArtTextPaneActionButton(
            "Remove",
            "Remove the selected SmartArt row.",
            SmartArtNodeEditKind.Remove);
        AddSmartArtTextPaneActionButton(
            "Move up",
            "Move the selected SmartArt row earlier.",
            SmartArtNodeEditKind.MoveUp);
        AddSmartArtTextPaneActionButton(
            "Move down",
            "Move the selected SmartArt row later.",
            SmartArtNodeEditKind.MoveDown);
        AddSmartArtTextPaneActionButton(
            "Promote",
            "Promote the selected SmartArt row.",
            SmartArtNodeEditKind.Promote);
        AddSmartArtTextPaneActionButton(
            "Demote",
            "Demote the selected SmartArt row.",
            SmartArtNodeEditKind.Demote);
        AddSmartArtTextPaneActionButton(
            "Add assistant",
            "Add an assistant below the selected hierarchy row.",
            SmartArtNodeEditKind.AddAssistant);

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
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
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
            Text = "Accessibility",
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
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
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
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
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
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
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
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
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
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
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
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = panel,
        };
    }

    private Border BuildAnimationPaneHost()
    {
        _animationPaneHeading = new TextBlock
        {
            Text = _animationPaneControlSchema.Heading,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        _animationPaneMessage = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
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
            Background = new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A)),
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
            Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
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
        if (shape.Kind != SlideShapeKind.Ole
            || shape.OleObject is null
            || Math.Abs(shape.RotationDeg) > 0.01
            || shape.FlipH
            || shape.FlipV)
            return false;

        CloseActiveOleHost();
        var bounds = SlideCanvasGeometryPlanner.EmuBoundsToScreen(
            shape.OffsetXEmu,
            shape.OffsetYEmu,
            shape.ExtentCxEmu,
            shape.ExtentCyEmu,
            _slideCanvas.CurrentTransform);
        var overlayBounds = new Rect(
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height);

        return AvaloniaOleInPlaceHost.TryShow(
            _oleOverlay,
            shape.OleObject,
            overlayBounds,
            onActivationFailed: () =>
            {
                CloseActiveOleHost();
                OleActivationService.TryActivate(shape.OleObject);
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
        foreach (var entry in plan.Entries)
        {
            if (entry.Kind == PresentationDomainContextMenuEntryKind.Separator)
                menu.Items.Add(new Separator());
            else
                menu.Items.Add(BuildDomainContextMenuItem(entry));
        }
        return menu;
    }

    private MenuItem BuildDomainContextMenuItem(PresentationDomainContextMenuEntryPlan entry)
    {
        var item = new MenuItem
        {
            Header = entry.Text,
            IsEnabled = entry.IsEnabled,
        };
        if (entry.Children is { Count: > 0 })
        {
            foreach (var child in entry.Children)
                item.Items.Add(BuildDomainContextMenuItem(child));
        }
        else if (entry.Action is { } action)
        {
            item.Click += (_, _) =>
                _domainContextMenuSession.Execute(action, TryExecuteInlineTableAction);
        }
        return item;
    }

    private bool TryExecuteInlineTableAction(PresentationDomainContextAction action)
    {
        if (_textEditor?.IsCellEditActive != true)
            return false;

        return action.Kind switch
        {
            PresentationDomainContextActionKind.InsertTableRowAbove =>
                _textEditor.TryInsertActiveTableRowAbove(),
            PresentationDomainContextActionKind.InsertTableRowBelow =>
                _textEditor.TryInsertActiveTableRowBelow(),
            PresentationDomainContextActionKind.InsertTableColumnLeft =>
                _textEditor.TryInsertActiveTableColumnLeft(),
            PresentationDomainContextActionKind.InsertTableColumnRight =>
                _textEditor.TryInsertActiveTableColumnRight(),
            PresentationDomainContextActionKind.DeleteTableRow =>
                _textEditor.TryDeleteActiveTableRow(),
            PresentationDomainContextActionKind.DeleteTableColumn =>
                _textEditor.TryDeleteActiveTableColumn(),
            PresentationDomainContextActionKind.MergeTableCell =>
                _textEditor.TryMergeActiveTableCell(),
            PresentationDomainContextActionKind.SplitTableCell =>
                _textEditor.TrySplitActiveTableCell(),
            _ => false,
        };
    }

    internal ContextMenu BuildChartContextMenuForTests(ChartSubtargetHit hit) =>
        BuildDomainContextMenu(_domainContextMenuSession.BuildChart(hit));

    internal ContextMenu? BuildTableContextMenuForTests(uint shapeId)
    {
        var plan = _domainContextMenuSession.BuildTable(shapeId);
        return plan is null ? null : BuildDomainContextMenu(plan);
    }

    internal bool ActivateTableCellEditForTests(uint shapeId, int row, int col)
    {
        _textEditor?.ActivateCellEdit(shapeId, row, col);
        return _textEditor?.IsCellEditActive == true;
    }

    internal bool IsTableCellEditActiveForTests => _textEditor?.IsCellEditActive == true;

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
        var definition = FreePRibbonAvalonia.Build();
        _ribbonDefinition = definition;
        _ribbonCommandRegistry = registry;

        _ribbonControl = AvaloniaRibbonRenderer.BuildRibbon(
            definition,
            registry,
            afterExecute: null,
            palette: RibbonVisualPalette.FromTheme(App.ActiveTheme),
            onFileTabSelected: ShowBackstage,
            stateStore: _ribbonStateStore);

        HasToolbar = true;
        return new Border
        {
            Height          = FreePShellVisualMetrics.RibbonHeight,
            Background      = Brushes.White,
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child           = _ribbonControl,
        };
    }

    internal RibbonCommandRegistry BuildCommandRegistry()
    {
        return FreePRibbonHostRegistryComposer.Build(
            Editor,
            _ribbonStateStore,
            CreateRibbonHostProfile()).Registry;
    }

    private FreePRibbonHostProfile CreateRibbonHostProfile() => new()
    {
        ActionEndpoints = GetRibbonHostActionEndpoints(),
        QueryEndpoints = new FreePRibbonHostQueryEndpoints
        {
            BeginFormatPainter = () => _gestureHandler?.BeginFormatPainter() == true,
            CanMergeTableCells = () =>
                _domainContextMenuSession.CanExecuteCurrentTableAction(
                    PresentationDomainContextActionKind.MergeTableCell),
            CanSplitTableCell = () =>
                _domainContextMenuSession.CanExecuteCurrentTableAction(
                    PresentationDomainContextActionKind.SplitTableCell),
            EditPointsEnabled = () => _slideCanvas.EditPointsEnabled,
            AnimationPaneVisible = () => IsAnimationPaneVisible,
            ViewShowState = () => _viewShowState,
            ViewZoomState = () => _viewZoomState,
        },
        TextActionEndpoints = BuildRibbonTextActionEndpoints(),
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
    };

    private FreePRibbonHostActionEndpoints GetRibbonHostActionEndpoints() =>
        _ribbonHostActionEndpoints ??= new FreePRibbonHostActionEndpoints
        {
            Copy = QueueClipboardCopy,
            Cut = QueueClipboardCut,
            Paste = QueueClipboardPaste,
            InsertPicture = () => _ = InsertPictureFromFileAsync(),
            InsertVideo = () => _ = InsertMediaFromFileAsync(isVideo: true),
            InsertAudio = () => _ = InsertMediaFromFileAsync(isVideo: false),
            OpenTablePicker = OpenTablePicker,
            MergeTableCells = () =>
            {
                _domainContextMenuSession.ExecuteCurrentTableAction(
                    PresentationDomainContextActionKind.MergeTableCell,
                    TryExecuteInlineTableAction);
            },
            SplitTableCell = () =>
            {
                _domainContextMenuSession.ExecuteCurrentTableAction(
                    PresentationDomainContextActionKind.SplitTableCell,
                    TryExecuteInlineTableAction);
            },
            PickPictureBullet = () => _ = ApplyPictureBulletFromFileAsync(),
            InsertSlideZoom = () => _ = OpenSlideZoomDialogAsync(),
            InsertSectionZoom = () => _ = OpenSectionZoomDialogAsync(),
            InsertSummaryZoom = () => _ = OpenSummaryZoomDialogAsync(),
            EditZoomTarget = () => _ = OpenZoomTargetDialogAsync(),
            EditSummaryZoomTargets = () => _ = OpenSummaryZoomTargetsDialogAsync(),
            FormatZoom = () => _ = OpenZoomObjectPropertiesDialogAsync(),
            SetZoomCoverImage = () => _ = OpenZoomCoverImagePickerAsync(),
            ResetZoomCoverImage = () => _ = RestoreZoomPreviewAsync(),
            OpenHeaderFooter = OpenHeaderFooterDialog,
            DesignRequest = OnDesignHostRequest,
            ApplySmartArtColor = preset => ApplySmartArtColorPreset(preset),
            ApplySmartArtLayout = preset => ApplySmartArtLayoutPreset(preset),
            ApplySmartArtQuickStyle = preset => ApplySmartArtQuickStylePreset(preset),
            ConvertSmartArtToShapes = () => ConvertSelectedSmartArtToShapes(),
            OpenSmartArtTextPane = () => ShowSmartArtTextPane(),
            OpenChartData = OpenChartDataDialog,
            OpenChartDisplayOptions = OpenChartDisplayOptionsDialog,
            OpenChartAxisOptions = () => OpenChartAxisOptionsDialog(),
            OpenChartSeriesOptions = () => OpenChartSeriesOptionsDialog(),
            OpenChartPointOptions = () => OpenChartPointOptionsDialog(),
            OpenChartLayoutOptions = OpenChartLayoutOptionsDialog,
            OpenChartExSeriesLayout = OpenChartExSeriesLayoutDialog,
            OpenChartDataTableOptions = OpenChartDataTableOptionsDialog,
            OpenChartBubbleOptions = OpenChartBubbleOptionsDialog,
            OpenChartPieOptions = OpenChartPieOptionsDialog,
            OpenChartPlotStyleOptions = OpenChartPlotStyleOptionsDialog,
            OpenChart3DViewOptions = OpenChart3DViewOptionsDialog,
            OpenChartTextOptions = OpenChartTextOptionsDialog,
            OpenChartAreaOptions = OpenChartAreaOptionsDialog,
            OpenChartProtectionOptions = OpenChartProtectionOptionsDialog,
            OpenHyperlink = OpenHyperlinkDialog,
            OpenRotationOptions = OpenRotationOptionsDialog,
            SetEditPointsEnabled = _slideCanvas.SetEditPointsMode,
            OpenFind = OpenFindDialog,
            OpenReplace = OpenFindReplaceDialog,
            ShowCommentsPane = () => ShowReviewCommentsPane(),
            ShowAccessibilityPane = () => ShowAccessibilityCheckerPane(),
            ShowAltTextPane = () => ShowAltTextPane(),
            ShowReadingOrderPane = () => ShowReadingOrderPane(),
            ShowSelectionPane = () => ShowSelectionPane(),
            ShowProofingPane = () => ShowProofingPane(),
            AddComment = () => AddComment(PresentationPaneTextResources.NewCommentDefault),
            EditComment = () => EditSelectedComment(GetSelectedCommentText()),
            ReplyComment = () => ReplyToSelectedComment(PresentationPaneTextResources.NewReplyDefault),
            DeleteComment = () => DeleteSelectedComment(),
            PreviousComment = () => NavigateReviewComment(PresentationReviewWorkflowIntentKind.PreviousComment),
            NextComment = () => NavigateReviewComment(PresentationReviewWorkflowIntentKind.NextComment),
            ResolveComment = () => ResolveSelectedComment(),
            ReopenComment = () => ReopenSelectedComment(),
            ApplyViewShowState = ApplyPresentationViewShowState,
            ApplyViewZoomState = ApplyPresentationViewZoomState,
            PickTransitionSound = () => _ = PickTransitionSoundAsync(),
            ToggleAnimationPane = OnAnimationPaneRequested,
            StartSlideShowFromBeginning = () => StartSlideShow(fromStart: true),
            StartSlideShowFromCurrent = () => StartSlideShow(fromStart: false),
            RehearseTimings = () => StartSlideShowWithTiming(SlideShowTimingIntent.RehearseTimings),
            RecordTimings = () => StartSlideShowWithTiming(SlideShowTimingIntent.RecordTimings),
            OpenCustomShows = OpenCustomShowDialog,
            OpenSlideShowSettings = OpenSlideShowSettingsDialog,
        };

    private FreePRibbonTextActionEndpoints BuildRibbonTextActionEndpoints() => new()
    {
        ToggleFormat = format =>
            _textEditor?.TryApplyActiveShapeTextFormat(format) == true ||
            _textEditor?.TryApplyActiveTableCellTextFormat(format) == true,
        SetParagraphAlignment = alignment =>
            _textEditor?.TryApplyActiveShapeParagraphAlignment(alignment) == true ||
            _textEditor?.TryApplyActiveTableCellParagraphAlignment(alignment) == true,
        ApplyListPreset = preset =>
            _textEditor?.TryApplyActiveShapeParagraphListPreset(preset) == true ||
            _textEditor?.TryApplyActiveTableCellParagraphListPreset(preset) == true,
        ToggleBullets = () =>
            _textEditor?.TryApplyActiveShapeParagraphBulletToggle() == true ||
            _textEditor?.TryApplyActiveTableCellParagraphBulletToggle() == true,
        ToggleNumbering = () =>
            _textEditor?.TryApplyActiveShapeParagraphNumberingToggle() == true ||
            _textEditor?.TryApplyActiveTableCellParagraphNumberingToggle() == true,
        Indent = () =>
            _textEditor?.TryApplyActiveShapeParagraphIndent() == true ||
            _textEditor?.TryApplyActiveTableCellParagraphIndent() == true,
        Outdent = () =>
            _textEditor?.TryApplyActiveShapeParagraphOutdent() == true ||
            _textEditor?.TryApplyActiveTableCellParagraphOutdent() == true,
        SetFontFamily = family =>
            _textEditor?.TryApplyActiveShapeFontFamily(family) == true ||
            _textEditor?.TryApplyActiveTableCellFontFamily(family) == true,
        SetFontSize = sizePt =>
            _textEditor?.TryApplyActiveShapeFontSize(sizePt) == true ||
            _textEditor?.TryApplyActiveTableCellFontSize(sizePt) == true,
        SetColor = color =>
            _textEditor?.TryApplyActiveShapeColor(color) == true ||
            _textEditor?.TryApplyActiveTableCellColor(color) == true,
        SetTextVerticalType = verticalType =>
            _textEditor?.TryApplyActiveTableCellTextVerticalType(verticalType) == true,
        SetTableCellFill = color =>
            _textEditor?.TryApplyActiveTableCellFill(color) == true,
        SetTableCellAnchor = anchor =>
            _textEditor?.TryApplyActiveTableCellAnchor(anchor) == true,
        SetTableCellBorder = (side, outline) =>
            _textEditor?.TryApplyActiveTableCellBorder(side, outline) == true,
        SetTableCellInset = (side, value) =>
            _textEditor?.TryApplyActiveTableCellInset(side, value) == true,
        SetTableRowHeight = height =>
            _textEditor?.TryApplyActiveTableRowHeight(height) == true,
        RemoveHyperlink = () =>
            _textEditor?.TryApplySelectedShapeRunHyperlink(null) == true,
    };


    private void OnDesignHostRequest(PresentationDesignCommandPlan plan)
    {
        switch (plan.Intent)
        {
            case PresentationDesignCommandIntentKind.RequestCustomSlideSize:
                OnCustomSlideSizeRequested(plan);
                break;
            case PresentationDesignCommandIntentKind.RequestLayoutPicker:
                OnLayoutPickerRequested(plan);
                break;
        }
    }

    private void OnCustomSlideSizeRequested(PresentationDesignCommandPlan plan)
    {
        LastCustomSlideSizeRequestPlan = plan;
        OpenSlideSizeDialog();
        _statusText.Text = "Slide Size";
    }

    private void OnLayoutPickerRequested(PresentationDesignCommandPlan plan)
    {
        LastLayoutRequestPlan = plan;
        LastLayoutPickerPlan = PresentationDesignCommandPlanner.BuildLayoutPickerPlan(
            _presentation,
            Editor.CurrentSlideIndex);
        ShowLayoutPicker(LastLayoutPickerPlan);
        _statusText.Text = $"Layout picker: {LastLayoutPickerPlan.Choices.Count} choices";
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
        _statusText.Text = $"Table picker: {LastTablePickerPlan.Choices.Count} choices";
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
                Content = choice.IsDefault ? $"{choice.Label} (default)" : choice.Label,
                Margin = new Thickness(2),
                Padding = new Thickness(6, 4),
                MinWidth = 74,
                BorderBrush = choice.IsDefault
                    ? new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A))
                    : new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
                Background = choice.IsDefault
                    ? new SolidColorBrush(Color.FromRgb(0xFE, 0xF2, 0xEC))
                    : Brushes.White,
            };
            AutomationProperties.SetAutomationId(button, $"table-{choice.Rows}x{choice.Columns}");
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

        _layoutPickerPanel.Children.Clear();
        foreach (var group in plan.Groups)
        {
            _layoutPickerPanel.Children.Add(new TextBlock
            {
                Text = group.Heading,
                Margin = new Thickness(10, 8, 10, 2),
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
            });

            var groupPanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(4, 0, 4, 4),
            };

            foreach (var choice in group.Choices)
            {
                var button = new Button
                {
                    Tag = choice.LayoutId,
                    Content = BuildLayoutChoiceTile(choice),
                    Margin = new Thickness(4),
                    Padding = new Thickness(0),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    IsEnabled = choice.Chrome.IsEnabled,
                };
                AutomationProperties.SetName(button, choice.DisplayLabel);
                AutomationProperties.SetAutomationId(button, $"layout-{choice.LayoutId}");
                button.Click += (_, _) =>
                {
                    if (button.Tag is string layoutId)
                        ApplyLayoutChoice(layoutId);
                };
                groupPanel.Children.Add(button);
            }

            _layoutPickerPanel.Children.Add(groupPanel);
        }

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
        _statusText.Text = "Header and Footer";
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
        _statusText.Text = "Set Up Slide Show";
        dialog.Closed += (_, _) => _slideShowSettingsDialog = null;
        if (IsVisible)
            _ = dialog.ShowDialog<bool?>(this);
        else
            dialog.Show();
    }

    private static Control BuildLayoutChoiceTile(PresentationLayoutChoice choice)
    {
        var (borderBrush, backgroundBrush) = BuildLayoutChoiceBrushes(choice.Chrome);
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
                Foreground = new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A)),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 3, 0, 0),
            });
        }

        return new Border
        {
            BorderBrush = borderBrush,
            Background = backgroundBrush,
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
            Background = Brushes.White,
        };

        foreach (var placeholder in choice.ThumbnailPlaceholders)
        {
            var rect = new AvaloniaRectangle
            {
                Width = placeholder.Bounds.Width,
                Height = placeholder.Bounds.Height,
                Fill = BuildLayoutPlaceholderFill(placeholder.PlaceholderType),
                Stroke = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
                StrokeThickness = 1,
                RadiusX = 1,
                RadiusY = 1,
            };
            Canvas.SetLeft(rect, placeholder.Bounds.X);
            Canvas.SetTop(rect, placeholder.Bounds.Y);
            canvas.Children.Add(rect);
        }

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xD9)),
            BorderThickness = new Thickness(1),
            Child = canvas,
        };
    }

    private static IBrush BuildLayoutPlaceholderFill(PlaceholderType type) =>
        type is PlaceholderType.Title or PlaceholderType.CenteredTitle or PlaceholderType.SubTitle
            ? new SolidColorBrush(Color.FromRgb(0xF8, 0xDD, 0xD1))
            : new SolidColorBrush(Color.FromRgb(0xEA, 0xF1, 0xF6));

    private static (IBrush Border, IBrush Background) BuildLayoutChoiceBrushes(
        PresentationLayoutChoiceChrome chrome) =>
        chrome.State switch
        {
            PresentationLayoutChoiceChromeState.Current => (
                new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A)),
                new SolidColorBrush(Color.FromRgb(0xFF, 0xF4, 0xEF))),
            PresentationLayoutChoiceChromeState.Disabled => (
                new SolidColorBrush(Color.FromRgb(0xA6, 0xA6, 0xA6)),
                new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3))),
            _ => (
                new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)),
                Brushes.White),
        };

    private async Task InsertPictureFromFileAsync()
    {
        var result = await ImportPresentationAssetAsync(PresentationAssetImportKind.Picture);
        MaterializePresentationAssetImportResult(result, showInsertedStatus: true);
    }

    private async Task InsertMediaFromFileAsync(bool isVideo)
    {
        var kind = isVideo
            ? PresentationAssetImportKind.Video
            : PresentationAssetImportKind.Audio;
        var result = await ImportPresentationAssetAsync(kind);
        MaterializePresentationAssetImportResult(result, showInsertedStatus: true);
    }

    private async Task InsertEmbeddedObjectFromFileAsync()
    {
        var result = await ImportPresentationAssetAsync(PresentationAssetImportKind.EmbeddedObject);
        MaterializePresentationAssetImportResult(result, showInsertedStatus: true);
    }

    private async Task PickTransitionSoundAsync()
    {
        var result = await ImportPresentationAssetAsync(PresentationAssetImportKind.TransitionSound);
        MaterializePresentationAssetImportResult(result, showInsertedStatus: true);
    }

    // ── File lifecycle ─────────────────────────────────────────────────────────

    internal Task ApplyPictureBulletFromFileAsyncForTests() => ApplyPictureBulletFromFileAsync();

    private async Task ApplyPictureBulletFromFileAsync()
    {
        if (PictureBulletPayloadProviderForTests is { } provider)
        {
            try
            {
                var payload = await provider();
                if (payload is not null && ApplyImportedPictureBullet(payload))
                    _statusText.Text = "Picture bullet applied.";
            }
            catch (Exception ex)
            {
                _statusText.Text = SisterAppFileTextPlanner.FormatCommandFailed(
                    FileText,
                    "Picture Bullet",
                    ex.Message);
            }
            return;
        }

        var result = await ImportPresentationAssetAsync(PresentationAssetImportKind.PictureBullet);
        MaterializePresentationAssetImportResult(result, successStatus: "Picture bullet applied.");
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
        RunGuarded(async () => await OpenHyperlinkDialogAsync(), "Hyperlink");

    internal Task<HyperlinkDialogApplyPlan> OpenHyperlinkDialogAsyncForTests() =>
        OpenHyperlinkDialogAsync();

    private async Task<HyperlinkDialogApplyPlan> OpenHyperlinkDialogAsync()
    {
        Hyperlink? selectedRunHyperlink = null;
        var editsSelectedRun = _textEditor is not null
            && _textEditor.TryGetSelectedShapeRunHyperlink(out selectedRunHyperlink);
        var request = _hyperlinkWorkflowSession.BuildRequest(
            editsSelectedRun,
            selectedRunHyperlink);
        LastHyperlinkDialogRequest = request.DialogRequest;

        var result = HyperlinkDialogResultProviderForTests is { } provider
            ? await provider(request.DialogRequest)
            : await ShowHyperlinkDialogAsync(request.DialogRequest);

        var workflowResult = _hyperlinkWorkflowSession.Apply(
            request,
            result,
            hyperlink => _textEditor?.TryApplySelectedShapeRunHyperlink(hyperlink) == true);
        LastHyperlinkDialogApplyPlan = workflowResult.ApplyPlan;
        if (workflowResult.Target == PresentationHyperlinkApplyTarget.SelectedShape)
        {
            var authoringPostconditionPath = Environment.GetEnvironmentVariable(
                "FREEP_PHYSICAL_HYPERLINK_AUTHORING_POSTCONDITION");
            if (!string.IsNullOrWhiteSpace(authoringPostconditionPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(authoringPostconditionPath)!);
                File.WriteAllText(
                    authoringPostconditionPath,
                    $"selectedShapeId={Editor.SelectedShapeIds.SingleOrDefault()}\ntargetSlideId={workflowResult.ApplyPlan.TargetSlideId}\n");
            }
        }

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

    internal void OpenSlideZoomDialog() => RunGuarded(OpenSlideZoomDialogAsync, "Slide Zoom");

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

    internal void OpenSectionZoomDialog() => RunGuarded(OpenSectionZoomDialogAsync, "Section Zoom");

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

    internal void OpenSummaryZoomDialog() => RunGuarded(OpenSummaryZoomDialogAsync, "Summary Zoom");

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

    internal void OpenZoomTargetDialog() => RunGuarded(OpenZoomTargetDialogAsync, "Zoom Target");

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

    internal void OpenSummaryZoomTargetsDialog() => RunGuarded(OpenSummaryZoomTargetsDialogAsync, "Summary Zoom Targets");

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
        MaterializePresentationAssetImportResult(result);
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

    internal FindReplaceWorkflowPlan SetFindReplaceDialogInputForTests(
        string? query,
        string? replacement = null,
        bool matchCase = false,
        bool wholeWord = false)
    {
        var dialog = _findReplaceDialog ?? throw new InvalidOperationException("Find/Replace is not open.");
        LastFindReplaceWorkflowPlan = dialog.SetInputForTests(query, replacement, matchCase, wholeWord);
        return LastFindReplaceWorkflowPlan;
    }

    internal FindReplaceWorkflowPlan NavigateFindReplaceDialogForTests(int direction)
    {
        var dialog = _findReplaceDialog ?? throw new InvalidOperationException("Find/Replace is not open.");
        LastFindReplaceWorkflowPlan = dialog.NavigateForTests(direction);
        return LastFindReplaceWorkflowPlan;
    }

    internal FindReplaceWorkflowPlan ReplaceAllFindReplaceDialogForTests()
    {
        var dialog = _findReplaceDialog ?? throw new InvalidOperationException("Find/Replace is not open.");
        LastFindReplaceWorkflowPlan = dialog.ReplaceAllForTests();
        return LastFindReplaceWorkflowPlan;
    }

    private void FileNew() => _ = FileNewAsync();

    internal Task<bool> FileNewAsyncForTests() => FileNewAsync();

    private async Task<bool> FileNewAsync() =>
        (await _fileSession.NewAsync()).Succeeded;

    private BackstageCallbacks BuildBackstageCallbacks() => new(
        GetPresentation: () => _presentation,
        GetDisplayName: () => _fileWorkflow.DisplayName,
        GetIsDirty: () => _fileWorkflow.IsDirty,
        GetCurrentPath: () => _fileWorkflow.CurrentPath,
        GetRecentEntries: () => _fileWorkflow.RecentEntries,
        GetCurrentOptions: () => _options,
        GetDataFolder: ResolveDataFolderLabel,
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
        CanExportVideo: () => _nativeOutputCapabilities.Video.CanEncodeMp4);

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
        ShowBackstage("Print");
    }

    private void HideBackstageAndRestoreFocus()
    {
        _backstage.Hide();
        var target = _backstageRestoreFocus;
        _backstageRestoreFocus = null;
        (target is { IsVisible: true, Focusable: true } ? target : _slideCanvas).Focus();
    }

    internal void ShowBackstageForTests() => ShowBackstage();

    internal bool ActivateBackstageEntryForTests(string label) => _backstage.TryActivateEntry(label);

    internal Control? CurrentBackstagePaneContentForTests => _backstage.CurrentPaneContent;

    internal bool HandleBackstageKeyForTests(Key key) => _backstage.HandleKey(key);

    private void OpenRecentPath(string path) => _ = OpenRecentPathAsync(path);

    private async Task<bool> OpenRecentPathAsync(string path) =>
        (await _fileSession.OpenRecentPathAsync(path)).Succeeded;

    private async Task<bool> FileOpenAsync() =>
        (await _fileSession.OpenAsync()).Succeeded;

    internal Task<bool> FileOpenAsyncForTests() => FileOpenAsync();
    internal Task<bool> FileSaveAsAsyncForTests() => FileSaveAsAsync();

    internal void SetFilePickerOverridesForTests(
        Func<FileOpenPickerPlan, Task<string?>>? openPicker,
        Func<FileSavePickerPlan, Task<string?>>? savePicker)
    {
        _openPickerOverrideForTests = openPicker;
        _savePickerOverrideForTests = savePicker;
    }

    private static string ResolveDataFolderLabel() =>
        AppStoragePathPlanner.GetOptionsFilePathLabelOrFallback(PlatformApplicationDataPathProvider.LocalInstance);

    // Opens the modal FreeP Options editor. On OK it applies the edited settings live (by mutating the
    // shared _options instance the Backstage and FileCommands read) and persists them through the shared
    // ApplicationOptionsStore so they survive a restart.
    internal async Task OpenOptionsAsync()
    {
        var dialog = new OptionsDialog(_options);
        await dialog.ShowDialog(this);
        if (dialog.Result is not { } edited)
            return;

        _options.RecentFilesCap = edited.RecentFilesCap;
        _options.DefaultSaveFormat = edited.DefaultSaveFormat;
        _options.UiLanguage = edited.UiLanguage;
        _options.Normalize();

        _optionsStore.Save(_options);
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
        _statusText.Text = "Print handout layout planned";
        return LastHandoutLayoutPlan;
    }

    internal PresentationNotesPagePdfRenderPlan RefreshNotesPagePdfRenderPlan(PresentationSlideRangeRequest? range = null)
    {
        LastNotesPagePdfRenderPlan = _fileSession.BuildNotesPagePdfRenderPlan(range);
        _statusText.Text = "Notes page PDF planned";
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

    internal Task<LinuxNativePrintResult> ExecutePrintForTests(
        PresentationPrintRequest? request = null,
        CancellationToken cancellationToken = default) =>
        ExecutePrintWorkflowAsync(request, cancellationToken);

    private async Task<LinuxNativePrintResult> ExecutePrintWorkflowAsync(
        PresentationPrintRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        await _fileSession.PrintAsync(request, cancellationToken).ConfigureAwait(true);
        return LastNativePrintResult ?? LinuxNativePrintResult.CanceledResult();
    }

    private async Task<LinuxNativePrintResult> ExecutePrintWorkflowCoreAsync(
        PresentationPrintRequest request,
        Func<PresentationPrintRequest, PresentationPrintOutputPackage> buildPackage,
        CancellationToken cancellationToken)
    {
        if (!_portablePrintWorkflowEnabled || OperatingSystem.IsWindows())
            return await ExecuteNativePrintHandoffCoreAsync(request, buildPackage, cancellationToken).ConfigureAwait(true);

        var requestedRequest = request;
        try
        {
            _latestPrinterDiscovery = await _printService.DiscoverAsync(cancellationToken).ConfigureAwait(true);
            var requestedSelection = new PrintSelection(
                _nativeOutputCapabilities.Print.PrinterName ?? _latestPrinterDiscovery.DefaultPrinter,
                requestedRequest.Copies,
                PrintPageRange.All,
                PrintOrientation.Document,
                requestedRequest.Collate);
            var selection = await _showPrintSelectionDialog(
                this,
                _latestPrinterDiscovery,
                requestedSelection,
                cancellationToken).ConfigureAwait(true);
            if (selection is null)
            {
                LastNativePrintResult = LinuxNativePrintResult.CanceledResult();
                _statusText.Text = LastNativePrintResult.StatusText;
                return LastNativePrintResult;
            }

            selection.Validate();
            LastPrintSelectionForTests = selection;
            var effectiveRequest = requestedRequest with
            {
                Copies = selection.Copies,
                Collate = selection.Collate,
            };
            var package = buildPackage(effectiveRequest);
            LastPrintOutputPackage = package;
            LastPrintExecutionDescriptor = _fileSession.LastPrintExecutionDescriptor;
            LastNativePrintHandoffPlan = _fileSession.LastNativePrintHandoffPlan;
            if (package is null || !package.Plan.CanBuildPackage)
            {
                LastNativePrintResult = LinuxNativePrintResult.Failed(
                    package?.Plan.DisabledReason ?? "Printable package was not built.");
                _statusText.Text = LastNativePrintResult.FailureReason ?? LastNativePrintResult.StatusText;
                return LastNativePrintResult;
            }

            var temporaryPath = Path.Combine(Path.GetTempPath(), $"freep-print-{Guid.NewGuid():N}.pdf");
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _printCancellation = linkedCancellation;
            try
            {
                await File.WriteAllBytesAsync(temporaryPath, package.Bytes, linkedCancellation.Token).ConfigureAwait(true);
                var submission = await _printService.SubmitAsync(
                    temporaryPath,
                    selection,
                    linkedCancellation.Token).ConfigureAwait(true);
                LastNativePrintResult = ToNativePrintResult(submission);
            }
            finally
            {
                if (ReferenceEquals(_printCancellation, linkedCancellation))
                    _printCancellation = null;
                TryDeletePrintFile(temporaryPath);
            }
        }
        catch (OperationCanceledException)
        {
            LastNativePrintResult = LinuxNativePrintResult.CanceledResult();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LastNativePrintResult = LinuxNativePrintResult.Failed(ex.Message);
        }

        _statusText.Text = LastNativePrintResult!.StatusText;
        if (!LastNativePrintResult.Succeeded && !LastNativePrintResult.Canceled &&
            LastNativePrintResult.FailureReason is not null)
        {
            _statusText.Text = $"{LastNativePrintResult.StatusText}: {LastNativePrintResult.FailureReason}";
        }

        return LastNativePrintResult;
    }

    private static LinuxNativePrintResult ToNativePrintResult(PrintSubmissionResult submission) =>
        submission.Status switch
        {
            PrintSubmissionStatus.Submitted => LinuxNativePrintResult.Success(null),
            PrintSubmissionStatus.Cancelled => LinuxNativePrintResult.CanceledResult(),
            _ => LinuxNativePrintResult.Failed(
                submission.Message ?? "Portable print submission failed."),
        };

    private static void TryDeletePrintFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    internal Task<LinuxNativePrintResult> ExecuteNativePrintHandoffAsync(
        PresentationPrintRequest? request = null,
        CancellationToken cancellationToken = default) =>
        ExecuteNativePrintHandoffCoreAsync(
            request ?? new PresentationPrintRequest(PresentationPrintLayoutKind.FullPageSlides),
            _fileSession.BuildPrintOutputPackage,
            cancellationToken);

    private async Task<LinuxNativePrintResult> ExecuteNativePrintHandoffCoreAsync(
        PresentationPrintRequest request,
        Func<PresentationPrintRequest, PresentationPrintOutputPackage> buildPackage,
        CancellationToken cancellationToken)
    {
        var package = buildPackage(request);
        LastPrintOutputPackage = package;
        LastPrintExecutionDescriptor = _fileSession.LastPrintExecutionDescriptor;
        LastNativePrintHandoffPlan = _fileSession.LastNativePrintHandoffPlan;
        if (package is null)
        {
            LastNativePrintResult = LinuxNativePrintResult.Failed("Printable package was not built.");
            return LastNativePrintResult;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _nativeOutputCancellation = linkedCancellation;
        try
        {
            LastNativePrintResult = await _nativePrintAdapter.PrintAsync(
                package.Bytes,
                LastNativePrintHandoffPlan?.SuggestedPrintJobName ?? "FreeP presentation",
                linkedCancellation.Token).ConfigureAwait(true);
        }
        finally
        {
            if (ReferenceEquals(_nativeOutputCancellation, linkedCancellation))
                _nativeOutputCancellation = null;
        }
        _statusText.Text = LastNativePrintResult.StatusText;
        if (!LastNativePrintResult.Succeeded && !LastNativePrintResult.Canceled &&
            LastNativePrintResult.FailureReason is not null)
        {
            _statusText.Text = $"{LastNativePrintResult.StatusText}: {LastNativePrintResult.FailureReason}";
        }

        return LastNativePrintResult;
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

        AddPrintOptionsPaneSection("Settings");
        foreach (var field in surface.Settings)
            AddPrintOptionsPaneField(field.Label, field.Value);
#if FREEP_WINDOWS_CAPTURE
        AddWindowsPrinterSelector();
#endif

        foreach (var group in surface.ChoiceGroups)
        {
            AddPrintOptionsPaneSection(PrintOptionsPaneSectionHeading(group.Heading));
            foreach (var choice in group.Choices)
            {
                var row = BuildPrintOptionsPaneChoiceSummary(
                    choice.Label,
                    choice.Description,
                    choice.IsSelected,
                    choice.IsAvailable);
                AddPrintOptionsPaneRenderedChoice(group.Heading, row);
                AddPrintOptionsPaneChoice(row, choice.IsAvailable);
            }
        }

        AddPrintOptionsPaneSection(PrintOptionsPaneSectionHeading(surface.CustomRangeHeading));
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

    private void AddPrintOptionsPaneRenderedChoice(string heading, string row)
    {
        switch (heading)
        {
            case "Output Options":
                _printOptionsPaneRenderedOptionLines.Add(row);
                break;
            case "Preview":
                _printOptionsPaneRenderedPreviewRows.Add(row);
                break;
            case "Layouts":
                _printOptionsPaneRenderedLayoutRows.Add(row);
                break;
            case "Slide Range":
                _printOptionsPaneRenderedRangeRows.Add(row);
                break;
        }
    }

    private static string PrintOptionsPaneSectionHeading(string heading) => heading switch
    {
        "Output Options" => "Output options",
        "Slide Range" => "Slide range",
        "Custom Range" => "Custom range",
        _ => heading,
    };

    private void AddPrintOptionsPaneSection(string text)
    {
        _printOptionsPaneRowsPanel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
            Margin = new Thickness(0, 16, 0, 6),
        });
    }

#if FREEP_WINDOWS_CAPTURE
    private void AddWindowsPrinterSelector()
    {
        if (!OperatingSystem.IsWindows())
            return;

        AddPrintOptionsPaneSection("Printer");
        var printers = WindowsNativePrintOutput.GetPrinters();
        if (printers.Count == 0)
        {
            AddPrintOptionsPaneField("Queue", "No Windows printer queues were detected.");
            return;
        }

        _nativePrinterPicker = new ComboBox
        {
            ItemsSource = printers,
            SelectedItem = _nativeOutputCapabilities.Print.PrinterName,
            MinWidth = 280,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 6),
        };
        AutomationProperties.SetAutomationId(_nativePrinterPicker, "FreePWindowsPrinterPicker");
        _nativePrinterPicker.SelectionChanged += (_, _) =>
        {
            if (_nativePrinterPicker.SelectedItem is string printerName)
                SelectWindowsPrinter(printerName);
        };
        _printOptionsPaneRowsPanel.Children.Add(_nativePrinterPicker);

        var nativeDialogButton = new Button
        {
            Content = "Windows printer dialog",
            MinWidth = 180,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 6),
        };
        AutomationProperties.SetAutomationId(nativeDialogButton, "FreePWindowsPrinterDialog");
        nativeDialogButton.Click += (_, _) => ShowWindowsPrinterDialog();
        _printOptionsPaneRowsPanel.Children.Add(nativeDialogButton);
    }

    private void ShowWindowsPrinterDialog()
    {
        if (!WindowsNativePrintOutput.TryShowPrinterSelectionDialog(
                _nativeOutputCapabilities.Print.PrinterName,
                out var selectedPrinter) ||
            string.IsNullOrWhiteSpace(selectedPrinter))
        {
            return;
        }

        SelectWindowsPrinter(selectedPrinter);
        if (_nativePrinterPicker is not null)
            _nativePrinterPicker.SelectedItem = selectedPrinter;
    }

    private void SelectWindowsPrinter(string printerName)
    {
        var capability = WindowsNativePrintOutput.ForPrinter(printerName);
        if (!capability.CanPrint)
        {
            _statusText.Text = capability.Reason;
            return;
        }

        _nativeOutputCapabilities = _nativeOutputCapabilities with { Print = capability };
        _nativePrintAdapter = WindowsNativePrintOutput.CreateAdapter(capability);
        _nativePrintHostCapabilities = BuildNativePrintHostCapabilities(capability);
        _statusText.Text = $"Printer selected: {capability.PrinterName}";
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
            Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
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

    private static string BuildPrintOptionsPaneChoiceSummary(
        string label,
        string description,
        bool isSelected,
        bool isAvailable = true)
    {
        var prefix = isSelected ? "Selected: " : string.Empty;
        var availability = isAvailable ? string.Empty : " (unavailable)";
        return $"{prefix}{label}{availability}\n{description}";
    }

    internal PresentationVideoExportPlan RefreshVideoExportPlan(PresentationVideoExportRequest? request = null)
    {
        LastVideoExportPlan = _fileSession.BuildVideoExportPlan(request);

        _statusText.Text = LastVideoExportPlan.DisabledReason ?? "Video export planned";
        return LastVideoExportPlan;
    }

    internal Task<bool> FileExportVideoAsyncForTests() => FileExportVideoAsync();

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
            result.Message ?? "Video export failed.",
            outputPath);
    }

    internal void CancelNativeOutputForTests()
    {
        _nativeOutputCancellation?.Cancel();
        _printCancellation?.Cancel();
    }

    internal sealed record VideoPickerSelectionForTests(string? LocalPath);

    private static PresentationNativePrintHandoffHostCapabilities BuildNativePrintHostCapabilities(
        LinuxNativePrintCapability capability) =>
        capability.CanPrint &&
        OperatingSystem.IsWindows() &&
        string.Equals(capability.ExecutablePath, "windows-shell-print", StringComparison.Ordinal)
            ? PresentationNativePrintHandoffHostCapabilities.Available("Avalonia Windows print host")
            : capability.CanPrint
            ? PresentationNativePrintHandoffHostCapabilities.NativePrinterSubmissionAvailable(
                "Avalonia Linux print host")
            : PresentationNativePrintHandoffHostCapabilities.Deferred(
                OperatingSystem.IsWindows() ? "Avalonia Windows print host" : "Avalonia Linux print host",
                capability.Reason);

    private static LinuxNativeOutputCapabilities DetectNativeOutputCapabilities()
    {
#if FREEP_WINDOWS_CAPTURE
        if (OperatingSystem.IsWindows())
            return WindowsNativePrintOutput.Detect();
#endif
        return new LinuxNativeOutputCapabilityDetector().Detect();
    }

    private static ILinuxNativePrintHandoffAdapter CreateNativePrintAdapter(
        LinuxNativePrintCapability capability)
    {
#if FREEP_WINDOWS_CAPTURE
        if (OperatingSystem.IsWindows())
            return WindowsNativePrintOutput.CreateAdapter(capability);
#endif
        return new LinuxNativePrintHandoffAdapter(capability);
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
        return new(
            isWindowsNative ? "Avalonia Windows video export host" : "Avalonia Linux video export host",
            capability.CanEncodeMp4,
            CanCaptureNarration: capability.CanCaptureNarration,
            CanCaptureCameraAndMedia: capability.CanCaptureCameraAndMedia,
            CanMuxTimedCaptions: capability.CanMuxTimedCaptions,
            UnavailableReason: capability.CanEncodeMp4
                ? isWindowsNative
                    ? capability.Reason
                    : capability.CanCaptureNarration
                        ? "ffmpeg video export, persisted narration muxing, and captured camera picture-in-picture are available."
                        : "Video-only ffmpeg export is available; narration and captured camera picture-in-picture are unavailable."
                : capability.Reason);
    }


    internal void RefreshReviewWorkflowPlans()
    {
        _reviewWorkflowSession.RefreshReviewWorkflowPlans();
        RefreshPaneAccessibilityMetadata();
    }

    private void RefreshVisibleReviewCommentsPane()
    {
        if (_reviewCommentsPaneHost is null || _reviewCommentsPanePanel is null
            || (!_reviewCommentsPaneRequested && !_reviewCommentsPaneHost.IsVisible))
        {
            return;
        }

        // The shared session refreshes the plan, while this host owns the realized
        // controls. Keep an already-open pane attached to the active slide.
        _reviewWorkflowSession.ShowReviewCommentsPane();
    }

    internal PresentationCommentPanePlan ShowReviewCommentsPane()
    {
        _reviewCommentsPaneRequested = true;
        return _reviewWorkflowSession.ShowReviewCommentsPane();
    }

    private void ShowReviewCommentsPane(PresentationCommentPanePlan plan)
    {
        if (_reviewCommentsPaneHost is null || _reviewCommentsPanePanel is null)
            return;

        _reviewCommentsPanePanel.Children.Clear();
        _reviewCommentsPanePanel.Children.Add(BuildReviewCommentsPaneHeader(plan));
        _reviewCommentsPanePanel.Children.Add(BuildAddCommentInput());
        _reviewCommentsPanePanel.Children.Add(BuildReviewCommentActions(plan.Actions));

        if (plan.Comments.Count == 0)
        {
            _reviewCommentsPanePanel.Children.Add(new TextBlock
            {
                Text       = "No comments on this slide.",
                Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                Margin     = new Thickness(12, 0, 12, 10),
            });
        }
        else
        {
            foreach (var (comment, itemIndex) in plan.Comments.Select((comment, index) => (comment, index)))
            {
                var card = BuildReviewCommentCard(comment, itemIndex);
                _reviewCommentsPanePanel.Children.Add(card);
            }
        }

        _reviewCommentsPaneHost.IsVisible = plan.Comments.Count > 0 || _reviewCommentsPaneRequested;
        RefreshPaneAccessibilityMetadata();
    }

    private Control BuildReviewCommentsPaneHeader(PresentationCommentPanePlan plan)
    {
        var summaryRow = new DockPanel
        {
            LastChildFill = true,
        };
        var close = new Button
        {
            Content  = "Close",
            MinWidth = PresentationCommentPaneVisualMetrics.CloseMinimumWidth,
            MinHeight = 0,
            Height   = PresentationCommentPaneVisualMetrics.CompactControlHeight,
            FontSize = PresentationCommentPaneVisualMetrics.CompactControlFontSize,
            Padding  = new Thickness(8, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Tag      = "comments-pane-close",
            Margin   = new Thickness(6, 0, 0, 6),
        };
        close.Click += (_, _) => HideReviewCommentsPane();
        DockPanel.SetDock(close, Dock.Right);
        summaryRow.Children.Add(close);
        summaryRow.Children.Add(new TextBlock
        {
            Text              = $"{plan.CurrentSlideSummaryLabel} | {plan.DeckSummaryLabel}",
            FontSize          = PresentationCommentPaneVisualMetrics.SummaryFontSize,
            FontWeight        = FontWeight.SemiBold,
            Foreground        = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
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
                    Text       = string.Join(" | ", plan.Filters.Select(filter => filter.Summary)),
                    FontSize   = PresentationCommentPaneVisualMetrics.FilterFontSize,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                    Margin     = new Thickness(0, 0, 0, 6),
                },
            },
        };
    }

    internal void HideReviewCommentsPane()
    {
        _reviewCommentsPaneRequested = false;
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

    private Control BuildReviewCommentCard(PresentationCommentDescriptor comment, int itemIndex)
    {
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(6, 4, 6, 0),
        };
        header.Children.Add(new Border
        {
            Background   = new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A)),
            CornerRadius = new CornerRadius(3),
            Padding      = new Thickness(4, 1, 4, 1),
            Margin       = new Thickness(0, 0, 6, 0),
            Child        = new TextBlock
            {
                Text       = comment.InitialsBadgeText,
                FontSize   = PresentationCommentPaneVisualMetrics.StatusFontSize,
                Foreground = Brushes.White,
            },
        });
        header.Children.Add(new TextBlock
        {
            Text              = comment.AuthorDisplayName,
            FontSize          = PresentationCommentPaneVisualMetrics.AuthorFontSize,
            FontWeight        = FontWeight.SemiBold,
            Foreground        = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
            VerticalAlignment = VerticalAlignment.Center,
        });
        header.Children.Add(new TextBlock
        {
            Text              = comment.ThreadStatusLabel,
            FontSize          = PresentationCommentPaneVisualMetrics.StatusFontSize,
            Foreground        = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
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
            Foreground   = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
            Margin       = new Thickness(16, 2, 6, 6),
        });
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
                "comment-mention:edit",
                () => editInput.Text,
                () => editInput.CaretIndex,
                PresentationReviewWorkflowIntentKind.EditComment);
            var editButton = new Button
            {
                Content = "Save",
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
                Text         = $"{reply.AuthorDisplayName}: {reply.TextPreview}",
                TextWrapping = TextWrapping.Wrap,
                FontSize     = PresentationCommentPaneVisualMetrics.ReplyFontSize,
                Margin       = new Thickness(26, 0, 6, 4),
                Foreground   = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            });
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
                "comment-mention:reply",
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
            Background      = new SolidColorBrush(comment.IsSelected ? Color.FromRgb(0xF4, 0xEC, 0xE8) : Color.FromRgb(0xFA, 0xFA, 0xFA)),
            BorderBrush     = new SolidColorBrush(comment.IsSelected ? Color.FromRgb(0xB7, 0x47, 0x2A) : Color.FromRgb(0xE0, 0xE0, 0xE0)),
            BorderThickness = new Thickness(comment.IsSelected ? 2 : 1),
            CornerRadius    = new CornerRadius(4),
            Margin          = new Thickness(0, 0, 0, PresentationCommentPaneVisualMetrics.CardBottomMargin),
            Child           = card,
        };
        border.Cursor = new Cursor(StandardCursorType.Hand);
        border.PointerPressed += (_, _) => SelectReviewComment(comment.CommentIndex);
        PresentationPaneAccessibilityAdapter.ApplyItem(
            border,
            PresentationPaneAccessibilityPlanner.CommentsPaneId,
            itemIndex,
            comment.TextPreview,
            comment.IsSelected ? "Selected" : "Not selected");
        return border;
    }

    private static void AddMentionDetail(StackPanel card, string mentionDetailSummary, Thickness margin)
    {
        if (string.Equals(mentionDetailSummary, "No mentions", StringComparison.Ordinal))
            return;

        card.Children.Add(new TextBlock
        {
            Text = mentionDetailSummary,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
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
        var candidate = mentionPicker.DefaultCandidate;
        var button = new Button
        {
            Content = mentionPicker.Candidates.Count == 1 ? candidate?.Label : "@",
            IsEnabled = mentionPicker.HasCandidates,
            Tag = tag,
            MinWidth = 72,
        };
        button.Click += (_, _) =>
        {
            var currentPlan = _reviewWorkflowSession.BuildCommentMentionPickerPlanForInput(
                getText(),
                getCaretIndex());
            if (currentPlan.Candidates.Count == 1)
            {
                _reviewWorkflowSession.ApplyCommentMention(
                    intent,
                    getText(),
                    getCaretIndex(),
                    currentPlan.DefaultCandidate);
                return;
            }

            if (currentPlan.HasCandidates)
            {
                var menu = BuildCommentMentionMenu(
                    tag,
                    getText,
                    getCaretIndex,
                    intent,
                    currentPlan);
                button.ContextMenu = menu;
                menu.Open(button);
            }
        };
        return button;
    }

    private ContextMenu BuildCommentMentionMenu(
        string tag,
        Func<string?> getText,
        Func<int> getCaretIndex,
        PresentationReviewWorkflowIntentKind intent,
        PresentationCommentMentionPickerPlan picker)
    {
        var menu = new ContextMenu();
        foreach (var candidate in picker.Candidates)
        {
            var item = new MenuItem
            {
                Header = candidate.Label,
                Tag = $"{tag}:{candidate.InsertToken}",
            };
            item.Click += (_, _) => _reviewWorkflowSession.ApplyCommentMention(
                intent,
                getText(),
                getCaretIndex(),
                candidate);
            menu.Items.Add(item);
        }

        return menu;
    }

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

    internal PresentationCommentPanePlan SetSelectedReviewCommentIndexForTests(int? commentIndex)
        => _reviewWorkflowSession.SetSelectedReviewCommentIndex(commentIndex);

    private void SelectReviewComment(int commentIndex)
        => _reviewWorkflowSession.SelectReviewComment(commentIndex);

    internal PresentationCommentNavigationPlan NavigateReviewComment(
        PresentationReviewWorkflowIntentKind intent)
        => _reviewWorkflowSession.NavigateReviewComment(intent);

    internal PresentationCommentMutationPlan DeleteSelectedComment()
        => _reviewWorkflowSession.DeleteSelectedComment();

    internal PresentationCommentMutationPlan AddComment(
        string? text,
        DateTime? timestamp = null,
        string? author = null,
        string? initials = null,
        long xemu = 0,
        long yemu = 0)
        => _reviewWorkflowSession.AddComment(text, timestamp, author, initials, xemu, yemu);

    internal PresentationCommentMutationPlan EditSelectedComment(
        string? text,
        string? author = null,
        string? initials = null)
        => _reviewWorkflowSession.EditSelectedComment(text, author, initials);

    internal PresentationCommentMutationPlan ResolveSelectedComment(
        DateTime? resolvedAt = null,
        string? resolvedBy = null)
        => _reviewWorkflowSession.ResolveSelectedComment(resolvedAt, resolvedBy);

    internal PresentationCommentMutationPlan ReopenSelectedComment()
        => _reviewWorkflowSession.ReopenSelectedComment();

    internal PresentationCommentMutationPlan ReplyToSelectedComment(
        string? text,
        DateTime? timestamp = null,
        string? author = null,
        string? initials = null)
        => _reviewWorkflowSession.ReplyToSelectedComment(text, timestamp, author, initials);

    internal PresentationCommentMentionPickerPlan BuildCommentMentionPickerPlanForTests(
        string? query = null,
        string? currentAuthor = null,
        string? currentInitials = null)
        => _reviewWorkflowSession.BuildCommentMentionPickerPlan(query, currentAuthor, currentInitials);

    internal PresentationCommentMentionInsertionPlan InsertCommentMentionForTests(
        string? text,
        int caretIndex,
        PresentationCommentMentionCandidate? candidate)
        => _reviewWorkflowSession.InsertCommentMention(text, caretIndex, candidate);

    internal PresentationCommentMutationPlan InsertMentionInSelectedCommentForTests(
        int caretIndex,
        PresentationCommentMentionCandidate? candidate,
        string? author = null,
        string? initials = null)
        => _reviewWorkflowSession.InsertMentionInSelectedComment(
            caretIndex,
            candidate,
            author,
            initials);

    private string? GetSelectedCommentText() => _reviewWorkflowSession.GetSelectedCommentText();

    private string? GetCommentText(int commentIndex) => _reviewWorkflowSession.GetCommentText(commentIndex);

    private void OnAnimationPaneRequested(PresentationAnimationCommandPlan plan)
    {
        SeedPhysicalAnimationPaneFixtureIfRequested();
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
        var viewPlan = (_animationPaneSession.WorkflowEvidence ??
            AnimationPanePlanner.BuildWorkflowEvidencePlan(plan, Editor.CurrentSlideIndex)).View;
        _animationPaneHeading.Text = _animationPaneControlSchema.Heading;
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
                Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
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
                PresentationPaneAccessibilityPlanner.AnimationPaneId,
                i,
                item.ShapeName,
                item.IsSelected ? "Selected" : "Not selected");
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
                Background = new SolidColorBrush(Color.FromRgb(0x8F, 0x37, 0x21)),
                Foreground = Brushes.White,
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

    internal AnimationPanePlaybackSessionPlan ExecuteAnimationPanePlaybackControlForTests(
        AnimationPanePlaybackControlKind controlKind)
    {
        var control = RefreshAnimationPaneTimelinePlan(_animationPaneSession.SelectedAnimationIndex)
            .PlaybackControls
            .First(candidate => candidate.Kind == controlKind);
        return ExecuteAnimationPanePlaybackControl(control, startPreview: false);
    }

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
        var effectOptionsControl = _animationPaneControlSchema.GetRequired(AnimationPaneControlKind.EffectOptions);
        var wheelSpokesControl = _animationPaneControlSchema.GetRequired(AnimationPaneControlKind.WheelSpokes);
        var triggerControl = _animationPaneControlSchema.GetRequired(AnimationPaneControlKind.Trigger);
        var durationControl = _animationPaneControlSchema.GetRequired(AnimationPaneControlKind.Duration);
        var delayControl = _animationPaneControlSchema.GetRequired(AnimationPaneControlKind.Delay);
        var repeatControl = _animationPaneControlSchema.GetRequired(AnimationPaneControlKind.Repeat);
        var autoReverseControl = _animationPaneControlSchema.GetRequired(AnimationPaneControlKind.AutoReverse);
        var moveEarlierControl = _animationPaneControlSchema.GetRequired(AnimationPaneControlKind.MoveEarlier);
        var moveLaterControl = _animationPaneControlSchema.GetRequired(AnimationPaneControlKind.MoveLater);
        var removeControl = _animationPaneControlSchema.GetRequired(AnimationPaneControlKind.RemoveAnimation);
        var editMotionPathControl = _animationPaneControlSchema.GetRequired(AnimationPaneControlKind.EditMotionPath);
        var effectOptionItems = item.EffectOptions.Options
            .Select(option => option.DisplayText)
            .ToArray();
        var selectedEffectOptionIndex = item.EffectOptions.Options
            .Select((option, index) => (option, index))
            .FirstOrDefault(pair => pair.option.IsSelected)
            .index;

        var effectOptionCombo = new ComboBox
        {
            ItemsSource = effectOptionItems,
            SelectedIndex = selectedEffectOptionIndex,
            Width = 104,
            Height = 24,
            FontSize = 10,
            Margin = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = item.Index,
            IsEnabled = item.EffectOptions.CanApply,
            IsVisible = item.EffectOptions.Options.Count > 0,
        };
        ToolTip.SetTip(
            effectOptionCombo,
            item.EffectOptions.CanApply
                ? effectOptionsControl.ToolTip
                : item.EffectOptions.DisabledReason);
        effectOptionCombo.SelectionChanged += (_, _) =>
        {
            if (effectOptionCombo.SelectedIndex < 0
                || effectOptionCombo.SelectedIndex >= item.EffectOptions.Options.Count)
            {
                return;
            }

            ApplyAnimationPaneEffectOptionEdit(
                item.Index,
                item.EffectOptions.Options[effectOptionCombo.SelectedIndex].Id);
        };
        if (item.EffectOptions.Options.Count > 0)
            _animationPaneEffectOptionControlCount++;

        var wheelSpokeCombo = new ComboBox
        {
            ItemsSource = item.EffectOptions.WheelSpokeOptions
                .Select(option => option.DisplayText)
                .ToArray(),
            SelectedIndex = item.EffectOptions.WheelSpokeOptions
                .Select((option, index) => (option, index))
                .FirstOrDefault(pair => pair.option.IsSelected)
                .index,
            Width = 86,
            Height = 24,
            FontSize = 10,
            Margin = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = item.Index,
            IsEnabled = item.EffectOptions.CanApply,
            IsVisible = item.EffectOptions.WheelSpokeOptions.Count > 0,
        };
        ToolTip.SetTip(wheelSpokeCombo, wheelSpokesControl.ToolTip);
        wheelSpokeCombo.SelectionChanged += (_, _) =>
        {
            if (wheelSpokeCombo.SelectedIndex < 0
                || wheelSpokeCombo.SelectedIndex >= item.EffectOptions.WheelSpokeOptions.Count)
            {
                return;
            }

            ApplyAnimationPaneEffectOptionEdit(
                item.Index,
                item.EffectOptions.WheelSpokeOptions[wheelSpokeCombo.SelectedIndex].Id);
        };
        if (item.EffectOptions.WheelSpokeOptions.Count > 0)
            _animationPaneEffectOptionControlCount++;

        var triggerCombo = new ComboBox
        {
            ItemsSource = triggerControl.OptionLabels,
            SelectedIndex = item.TriggerIndex,
            Width = 110,
            Height = 24,
            FontSize = 10,
            Margin = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = item.Index,
        };
        ToolTip.SetTip(triggerCombo, triggerControl.ToolTip);
        triggerCombo.SelectionChanged += (_, _) =>
            ApplyAnimationPaneTriggerEdit(item.Index, triggerCombo.SelectedIndex);
        _animationPaneTriggerControlCount++;

        var durationBox = new TextBox
        {
            Text = item.DurationText,
            Width = 48,
            Height = 24,
            FontSize = 10,
            Padding = new Thickness(2, 1),
            Margin = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = item.Index,
        };
        ToolTip.SetTip(durationBox, durationControl.ToolTip);
        durationBox.LostFocus += (_, _) =>
        {
            var plan = ApplyAnimationPaneDurationEdit(item.Index, durationBox.Text ?? string.Empty);
            if (!plan.ShouldApply)
                durationBox.Text = plan.DisplayText;
        };
        _animationPaneDurationControlCount++;

        var delayBox = new TextBox
        {
            Text = item.DelayText,
            Width = 48,
            Height = 24,
            FontSize = 10,
            Padding = new Thickness(2, 1),
            Margin = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = item.Index,
        };
        ToolTip.SetTip(delayBox, delayControl.ToolTip);
        delayBox.LostFocus += (_, _) =>
        {
            var plan = ApplyAnimationPaneDelayEdit(item.Index, delayBox.Text ?? string.Empty);
            if (!plan.ShouldApply)
                delayBox.Text = plan.DisplayText;
        };

        TextBox? decelerationBox = null;
        var accelerationBox = new TextBox
        {
            Text = AnimationPanePlanner.FormatEasing(item.Acceleration),
            Width = 48,
            Height = 24,
            FontSize = 10,
            Padding = new Thickness(2, 1),
            Margin = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = item.Index,
        };
        ToolTip.SetTip(accelerationBox, "Smooth start");
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
            Text = AnimationPanePlanner.FormatEasing(item.Deceleration),
            Width = 48,
            Height = 24,
            FontSize = 10,
            Padding = new Thickness(2, 1),
            Margin = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = item.Index,
        };
        ToolTip.SetTip(decelerationBox, "Smooth end");
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
            ItemsSource = repeatControl.OptionLabels,
            SelectedItem = AnimationPanePlanner.FormatRepeat(item.RepeatCount, item.RepeatIndefinitely),
            Width = 82,
            Height = 24,
            FontSize = 10,
            Margin = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = item.Index,
        };
        ToolTip.SetTip(repeatCombo, repeatControl.ToolTip);

        var autoReverseCheck = new CheckBox
        {
            IsChecked = item.AutoReverse,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2),
            Tag = item.Index,
        };
        ToolTip.SetTip(autoReverseCheck, autoReverseControl.ToolTip);

        void ApplyRepeat()
        {
            var plan = _animationPaneSession.ApplyRepeat(
                item.Index,
                repeatCombo.SelectedItem as string,
                autoReverseCheck.IsChecked == true);
            if (!plan.ShouldApply && plan.DisabledReason is not null)
            {
                repeatCombo.SelectedItem = AnimationPanePlanner.FormatRepeat(
                    plan.RepeatCount,
                    plan.RepeatIndefinitely);
                autoReverseCheck.IsChecked = plan.AutoReverse;
            }
        }

        repeatCombo.SelectionChanged += (_, _) => ApplyRepeat();
        autoReverseCheck.IsCheckedChanged += (_, _) => ApplyRepeat();

        var moveEarlierButton = BuildAnimationPaneActionButton(
            "▲",
            item.CanMoveEarlier,
            moveEarlierControl.ToolTip,
            () => MoveAnimationPaneItem(item.Index, -1));
        var moveLaterButton = BuildAnimationPaneActionButton(
            "▼",
            item.CanMoveLater,
            moveLaterControl.ToolTip,
            () => MoveAnimationPaneItem(item.Index, 1));
        var removeButton = BuildAnimationPaneActionButton(
            "×",
            true,
            removeControl.ToolTip,
            () => RemoveAnimationPaneItem(item.Index));
        removeButton.Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x20, 0x20));
        var paragraphBuildPlan = AnimationPanePlanner.BuildParagraphBuildMutationPlan(
            Editor.CurrentSlide,
            item.ShapeId);
        var paragraphBuildButton = BuildAnimationPaneActionButton(
            "¶",
            paragraphBuildPlan.ShouldApply,
            paragraphBuildPlan.DisabledReason ?? paragraphBuildPlan.DisplayText,
            () => ToggleParagraphBuild(item.ShapeId));
        var editMotionPathButton = item.Kind == AnimationKind.Motion
            ? BuildAnimationPaneActionButton(
                editMotionPathControl.Label,
                true,
                editMotionPathControl.ToolTip,
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
            Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)),
            Width = 20,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(4, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var nameLabel = new TextBlock
        {
            Text = item.ShapeName,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 80,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var effectLabel = new TextBlock
        {
            Text = item.EffectText,
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
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
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0xD6))
                : new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
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

    internal AnimationPaneParagraphBuildMutationPlan ToggleParagraphBuildForTests(uint shapeId)
    {
        var plan = _animationPaneSession.ToggleParagraphBuild(shapeId);
        RefreshVisibleAnimationPane(_animationPaneSession.SelectedAnimationIndex);
        return plan;
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
            Background = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
            BorderThickness = new Thickness(1),
            IsEnabled = isEnabled,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(button, toolTip);
        button.Click += (_, _) => action();
        return button;
    }

    internal AnimationPaneTimingMutationPlan ApplyAnimationPaneTriggerEditForTests(
        int animationIndex,
        int selectedTriggerIndex)
        => ApplyAnimationPaneTriggerEdit(animationIndex, selectedTriggerIndex);

    internal AnimationPaneTimingMutationPlan ApplyAnimationPaneDurationEditForTests(
        int animationIndex,
        string text)
        => ApplyAnimationPaneDurationEdit(animationIndex, text);

    internal AnimationPaneTimingMutationPlan ApplyAnimationPaneDelayEditForTests(
        int animationIndex,
        string text)
        => ApplyAnimationPaneDelayEdit(animationIndex, text);

    internal AnimationPaneEasingMutationPlan ApplyAnimationPaneEasingEditForTests(
        int animationIndex,
        string accelerationText,
        string decelerationText)
        => ApplyAnimationPaneEasingEdit(animationIndex, accelerationText, decelerationText);

    internal AnimationPaneEffectOptionMutationPlan ApplyAnimationPaneEffectOptionEditForTests(
        int animationIndex,
        string optionId)
        => ApplyAnimationPaneEffectOptionEdit(animationIndex, optionId);

    internal AnimationPaneReorderMutationPlan MoveAnimationPaneItemForTests(int animationIndex, int offset)
        => MoveAnimationPaneItem(animationIndex, offset);

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

    internal AnimationPaneRemoveMutationPlan RemoveAnimationPaneItemForTests(int animationIndex) =>
        RemoveAnimationPaneItem(animationIndex);

    private AnimationPaneRemoveMutationPlan RemoveAnimationPaneItem(int animationIndex)
    {
        var plan = _animationPaneSession.RemoveAnimation(animationIndex);
        if (plan.ShouldApply)
            RefreshVisibleAnimationPane(_animationPaneSession.SelectedAnimationIndex);

        return plan;
    }

    internal PresentationAccessibilityCheckerPanePlan ShowAccessibilityCheckerPane()
    {
        var plan = _reviewWorkflowSession.ShowAccessibilityCheckerPane();
        RefreshPaneAccessibilityMetadata();
        return plan;
    }

    internal PresentationAccessibilityCheckerPanePlan SelectAccessibilityCheckerRow(int rowIndex)
        => _reviewWorkflowSession.SelectAccessibilityCheckerRow(rowIndex);

    internal PresentationAccessibilityCheckerPanePlan ApplyAccessibilityCheckerRowAction(int rowIndex)
        => _reviewWorkflowSession.ApplyAccessibilityCheckerRowAction(rowIndex);

    private void RenderAccessibilityCheckerPaneIfVisible(
        PresentationAccessibilityCheckerPanePlan plan)
    {
        if (IsAccessibilityCheckerPaneVisible)
            RenderAccessibilityCheckerPane(plan);
    }

    private void PresentAccessibilityCheckerPane(PresentationAccessibilityCheckerPanePlan plan)
    {
        RenderAccessibilityCheckerPane(plan);
        _accessibilityCheckerPaneHost.IsVisible = true;
    }

    private void RenderAccessibilityCheckerPane(PresentationAccessibilityCheckerPanePlan plan)
    {
        _accessibilityCheckerPaneHeading.Text = plan.Heading;
        _accessibilityCheckerPaneMessage.Text = plan.Message;
        RenderTableStructureReviewDetails(LastTableStructureReviewDisplayPlan);

        _accessibilityCheckerRowsPanel.Children.Clear();
        if (plan.Rows.Count == 0)
        {
            _accessibilityCheckerRowsPanel.Children.Add(new TextBlock
            {
                Text = plan.EmptyStateMessage,
                Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
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
            Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)),
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
                    Text = $"{row.SlideDisplay} - {row.Title}",
                    FontFamily = AvaloniaCompactDialogChrome.WindowsUiFontFamily,
                    FontSize = 12,
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(row.ShapeName)
                        ? $"{row.Severity} - {row.Category}"
                        : $"{row.Severity} - {row.Category} - {row.ShapeName}",
                    FontFamily = AvaloniaCompactDialogChrome.WindowsUiFontFamily,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = row.Detail,
                    FontFamily = AvaloniaCompactDialogChrome.WindowsUiFontFamily,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                    TextWrapping = TextWrapping.Wrap,
                },
                action,
            }
        };

        if (row.IsSelected)
        {
            panel.Children.Insert(1, new TextBlock
            {
                Text = PresentationPaneTextResources.ProofingSelectedIssue,
                FontFamily = AvaloniaCompactDialogChrome.WindowsUiFontFamily,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A)),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }

        var border = new Border
        {
            Background = row.IsSelected
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0xF6, 0xF2))
                : new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)),
            BorderBrush = row.IsSelected
                ? new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A))
                : new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10),
            Margin = new Thickness(12, 0, 12, 10),
            Child = panel,
        };
        border.PointerPressed += (_, _) => SelectAccessibilityCheckerRow(row.RowIndex);
        PresentationPaneAccessibilityAdapter.ApplyItem(
            border,
            PresentationPaneAccessibilityPlanner.AccessibilityPaneId,
            row.RowIndex,
            row.Title,
            row.IsSelected ? "Selected" : "Not selected");
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
            Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
            TextWrapping = TextWrapping.Wrap,
        });
        _accessibilityCheckerReviewDetailsPanel.Children.Add(new TextBlock
        {
            Text = display.Guidance,
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            TextWrapping = TextWrapping.Wrap,
        });

        foreach (var detail in display.Details)
        {
            _accessibilityCheckerTableStructureReviewRenderedLines.Add(
                $"{detail.Category}: {detail.Summary} {detail.Detail}");
            _accessibilityCheckerReviewDetailsPanel.Children.Add(BuildTableStructureReviewDetail(detail));
        }
    }

    private static Control BuildTableStructureReviewDetail(PresentationTableStructureReviewDetailRowPlan detail)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF7)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE2, 0xE2)),
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
                        Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                        TextWrapping = TextWrapping.Wrap,
                    },
                }
            },
        };
    }

    private void RefreshAltTextRequestPlan()
    {
        _reviewWorkflowSession.RefreshAltTextPlans(null, null, null);
        if (IsAltTextPaneVisible && LastAltTextPanePlan is not null)
            RenderAltTextPane(LastAltTextPanePlan);
    }

    internal IReadOnlyList<SmartArtNodeOutlineItem> ShowSmartArtTextPane()
    {
        var outline = RefreshSmartArtTextPane();
        _smartArtTextPaneHost.IsVisible = true;
        RefreshPaneAccessibilityMetadata();
        return outline;
    }

    internal void HideSmartArtTextPane()
    {
        if (_smartArtTextPaneHost is not null)
            _smartArtTextPaneHost.IsVisible = false;
        RefreshPaneAccessibilityMetadata();
    }

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

    internal SmartArtNodeEditResult? ToggleSmartArtTextPaneAssistantForTests(string? modelId = null)
    {
        if (modelId is not null)
            _smartArtTextPaneSession.SelectModel(modelId);
        return ToggleSmartArtTextPaneAssistant();
    }

    internal SmartArtNodeEditResult? ApplySmartArtTextPaneEditForTests(
        SmartArtNodeEditKind kind,
        string? modelId = null)
    {
        if (modelId is not null)
            _smartArtTextPaneSession.SelectModel(modelId);
        return ApplySmartArtTextPaneAction(kind);
    }

    private SmartArtNodeEditResult? ApplySmartArtTextPaneAction(SmartArtNodeEditKind kind)
        => _smartArtTextPaneSession.ApplyAction(kind);

    private SmartArtNodeEditResult? ToggleSmartArtTextPaneAssistant()
        => _smartArtTextPaneSession.ToggleAssistant();

    internal SmartArtColorApplyResult ApplySmartArtColorPresetForTests(SmartArtColorPreset preset) =>
        ApplySmartArtColorPreset(preset);

    internal SmartArtLayoutApplyResult ApplySmartArtLayoutPresetForTests(SmartArtLayoutPreset preset) =>
        ApplySmartArtLayoutPreset(preset);

    internal SmartArtQuickStyleApplyResult ApplySmartArtQuickStylePresetForTests(SmartArtQuickStylePreset preset) =>
        ApplySmartArtQuickStylePreset(preset);

    private SmartArtLayoutApplyResult ApplySmartArtLayoutPreset(SmartArtLayoutPreset preset)
        => _smartArtTextPaneSession.ApplyLayoutPreset(preset);

    private SmartArtQuickStyleApplyResult ApplySmartArtQuickStylePreset(SmartArtQuickStylePreset preset)
        => _smartArtTextPaneSession.ApplyQuickStylePreset(preset);

    private SmartArtColorApplyResult ApplySmartArtColorPreset(SmartArtColorPreset preset)
        => _smartArtTextPaneSession.ApplyColorPreset(preset);

    internal SmartArtNodeEditResult? ApplySmartArtTextPaneKeyboardRouteForTests(
        SmartArtTextPaneShortcutKey key,
        SmartArtTextPaneShortcutModifiers modifiers,
        string? modelId = null)
    {
        if (modelId is not null)
            _smartArtTextPaneSession.SelectModel(modelId);
        return ApplySmartArtTextPaneKeyboardRoute(key, modifiers);
    }

    private IReadOnlyList<SmartArtNodeOutlineItem> RefreshSmartArtTextPane()
        => _smartArtTextPaneSession.Refresh().Rows;

    private void RenderSmartArtTextPane(PresentationSmartArtTextPanePlan plan)
    {
        _smartArtTextPaneRefreshing = true;
        try
        {
            _smartArtTextPaneRowsPanel.Children.Clear();
            _smartArtTextPaneHeading.Text = plan.Heading;
            _smartArtTextPaneMessage.Text = plan.Message;
            _smartArtTextPaneApplyButton.IsEnabled = plan.CanApply;
            _smartArtTextPaneAssistantButton.IsEnabled = plan.CanToggleAssistant;
            foreach (var button in _smartArtTextPaneActionButtons)
                button.IsEnabled = plan.CanEditSelectedRow;

            for (var index = 0; index < plan.Rows.Count; index++)
            {
                var item = plan.Rows[index];
                var row = BuildSmartArtTextPaneRow(item);
                PresentationPaneAccessibilityAdapter.ApplyItem(
                    row,
                    PresentationPaneAccessibilityPlanner.SmartArtTextPaneId,
                    index,
                    item.Text,
                    StringComparer.Ordinal.Equals(item.ModelId, plan.SelectedModelId)
                        ? "Selected"
                        : "Not selected");
                _smartArtTextPaneRowsPanel.Children.Add(row);
            }
        }
        finally
        {
            _smartArtTextPaneRefreshing = false;
        }
    }

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
                ? new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A))
                : new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
            BorderThickness = new Thickness(selected ? 2 : 1),
        };
        ToolTip.SetTip(box, item.IsAssistant
            ? "Assistant row"
            : item.Level == 0
                ? "Root row"
                : $"Level {item.Level + 1} row");
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

    internal SmartArtNodeEditResult? ApplySmartArtTextPanePictureForTests(
        byte[] imageBytes,
        string contentType = "image/png",
        string? modelId = null)
    {
        if (modelId is not null)
            _smartArtTextPaneSession.SelectModel(modelId);
        return ApplySmartArtTextPanePicture(imageBytes, contentType);
    }

    private async Task ReplaceSmartArtTextPanePictureFromFileAsync()
    {
        var result = await ImportPresentationAssetAsync(PresentationAssetImportKind.SmartArtPicture);
        MaterializePresentationAssetImportResult(result);
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

    internal void ShowAltTextPane()
    {
        RefreshAltTextPlans(proposedDescription: null, proposedTitle: null, isDecorative: null);
        if (LastAltTextPanePlan is not null)
            RenderAltTextPane(LastAltTextPanePlan);
        _altTextPaneHost.IsVisible = true;
        RefreshPaneAccessibilityMetadata();
    }

    internal void HideAltTextPane()
    {
        if (_altTextPaneHost is not null)
            _altTextPaneHost.IsVisible = false;
        RefreshPaneAccessibilityMetadata();
    }

    internal PresentationMediaCaptionAuthoringPanePlan ShowMediaCaptionPane()
    {
        RefreshMediaCaptionAuthoringPlans(null, null, null, null);
        RenderMediaCaptionPane(LastMediaCaptionAuthoringPanePlan!);
        _mediaCaptionPaneHost.IsVisible = true;
        RefreshPaneAccessibilityMetadata();
        return LastMediaCaptionAuthoringPanePlan!;
    }

    internal void HideMediaCaptionPane()
    {
        if (_mediaCaptionPaneHost is not null)
            _mediaCaptionPaneHost.IsVisible = false;
        RefreshPaneAccessibilityMetadata();
    }

    internal void SetMediaCaptionPaneInput(
        string label,
        string language,
        string source,
        string transcriptText,
        int? selectedTrackIndex = null)
    {
        if (!IsMediaCaptionPaneVisible)
            ShowMediaCaptionPane();

        _mediaCaptionPaneRefreshing = true;
        try
        {
            if (selectedTrackIndex.HasValue)
                _mediaPaneSession.SelectCaptionTrack(selectedTrackIndex);
            _mediaCaptionLabelBox.Text = label;
            _mediaCaptionLanguageBox.Text = language;
            _mediaCaptionSourceBox.Text = source;
            _mediaCaptionTranscriptBox.Text = transcriptText;
        }
        finally
        {
            _mediaCaptionPaneRefreshing = false;
        }

        RefreshVisibleMediaCaptionPaneFromFields();
    }

    internal void SetMediaVolumePaneInput(int volumePercent)
    {
        if (!IsMediaCaptionPaneVisible)
            ShowMediaCaptionPane();

        _mediaCaptionPaneRefreshing = true;
        try
        {
            _mediaVolumeSlider.Value = PresentationMediaPaneSession.NormalizeVolumePercent(volumePercent);
        }
        finally
        {
            _mediaCaptionPaneRefreshing = false;
        }
    }

    internal void SetMediaPlaybackPaneInput(
        MediaPlaybackStartMode startMode,
        bool loop,
        bool showWhenStopped = true,
        bool rewindAfterPlaying = false,
        bool playFullScreen = false,
        int stopAfterSlides = 1)
    {
        ShowMediaCaptionPane();

        _mediaCaptionPaneRefreshing = true;
        try
        {
            _mediaStartModeBox.SelectedIndex = PresentationMediaPaneSession.GetPlaybackStartModeIndex(startMode);
            _mediaLoopCheckBox.IsChecked = loop;
            _mediaShowWhenStoppedCheckBox.IsChecked = showWhenStopped;
            _mediaRewindAfterPlayingCheckBox.IsChecked = rewindAfterPlaying;
            _mediaPlayFullScreenCheckBox.IsChecked = playFullScreen;
            _mediaStopAfterSlidesBox.Text = Math.Max(1, stopAfterSlides).ToString();
        }
        finally
        {
            _mediaCaptionPaneRefreshing = false;
        }
    }

    internal PresentationMediaCaptionTrackMutationResult ApplyMediaCaptionPane(
        PresentationMediaCaptionAuthoringIntentKind intent)
        => _mediaPaneSession.ApplyCaptionAuthoring(
            intent,
            _mediaCaptionLabelBox.Text,
            _mediaCaptionLanguageBox.Text,
            _mediaCaptionSourceBox.Text,
            _mediaCaptionTranscriptBox.Text);

    internal bool ApplyMediaVolumePane() => _mediaPaneSession.ApplyVolume(MediaVolumePercent);

    internal bool ApplyMediaPlaybackPane() =>
        _mediaPaneSession.ApplyPlayback(
            MediaPlaybackStartMode,
            MediaLoop,
            MediaShowWhenStopped,
            MediaRewindAfterPlaying,
            MediaPlayFullScreen,
            MediaStopAfterSlides);

    internal double MediaTrimStartMilliseconds => PresentationMediaPaneSession.ParseTiming(_mediaTrimStartBox?.Text);
    internal double MediaTrimEndMilliseconds => PresentationMediaPaneSession.ParseTiming(_mediaTrimEndBox?.Text);
    internal double MediaFadeInMilliseconds => PresentationMediaPaneSession.ParseTiming(_mediaFadeInBox?.Text);
    internal double MediaFadeOutMilliseconds => PresentationMediaPaneSession.ParseTiming(_mediaFadeOutBox?.Text);

    internal void SetMediaTimingPaneInput(double trimStart, double trimEnd, double fadeIn, double fadeOut)
    {
        if (!IsMediaCaptionPaneVisible)
            ShowMediaCaptionPane();
        _mediaCaptionPaneRefreshing = true;
        try
        {
            var plan = PresentationMediaPaneSession.BuildTimingInputPlan(trimStart, trimEnd, fadeIn, fadeOut);
            _mediaTrimStartBox.Text = plan.TrimStartText;
            _mediaTrimEndBox.Text = plan.TrimEndText;
            _mediaFadeInBox.Text = plan.FadeInText;
            _mediaFadeOutBox.Text = plan.FadeOutText;
        }
        finally
        {
            _mediaCaptionPaneRefreshing = false;
        }
    }

    internal bool ApplyMediaTimingPane() => _mediaPaneSession.ApplyTiming(
        _mediaTrimStartBox?.Text,
        _mediaTrimEndBox?.Text,
        _mediaFadeInBox?.Text,
        _mediaFadeOutBox?.Text);

    internal int MediaBookmarkCount => _mediaPaneSession.BuildProjection().Bookmarks.Count;

    internal void SetMediaBookmarkPaneInput(string name, double timeMilliseconds)
    {
        if (!IsMediaCaptionPaneVisible)
            ShowMediaCaptionPane();
        _mediaCaptionPaneRefreshing = true;
        try
        {
            var plan = PresentationMediaPaneSession.BuildBookmarkInputPlan(name, timeMilliseconds);
            _mediaBookmarkNameBox.Text = plan.Name;
            _mediaBookmarkTimeBox.Text = plan.TimeText;
        }
        finally
        {
            _mediaCaptionPaneRefreshing = false;
        }
    }

    internal bool ApplyMediaBookmarkCreatePane() => _mediaPaneSession.ApplyBookmark(
        PresentationMediaBookmarkMutationIntentKind.Create,
        _mediaBookmarkNameBox.Text,
        _mediaBookmarkTimeBox.Text);

    internal bool ApplyMediaBookmarkReplacePane() => _mediaPaneSession.ApplyBookmark(
        PresentationMediaBookmarkMutationIntentKind.Replace,
        _mediaBookmarkNameBox.Text,
        _mediaBookmarkTimeBox.Text);

    internal bool ApplyMediaBookmarkDeletePane() => _mediaPaneSession.ApplyBookmark(
        PresentationMediaBookmarkMutationIntentKind.Delete,
        _mediaBookmarkNameBox.Text,
        _mediaBookmarkTimeBox.Text);

    internal double MediaBookmarkTimeMilliseconds =>
        PresentationMediaPaneSession.ParseTiming(_mediaBookmarkTimeBox?.Text);

    private void RenderMediaBookmarkOptions(PresentationMediaPaneProjection plan)
    {
        _mediaBookmarkBox.Items.Clear();
        foreach (var bookmark in plan.Bookmarks)
            _mediaBookmarkBox.Items.Add(new ComboBoxItem { Content = bookmark.DisplayText, Tag = bookmark.Index });

        _mediaBookmarkBox.SelectedIndex = plan.SelectedBookmarkIndex ?? -1;
        _mediaBookmarkNameBox.Text = plan.BookmarkName;
        _mediaBookmarkTimeBox.Text = plan.BookmarkTimeText;
        _mediaBookmarkBox.IsEnabled = plan.HasMedia;
        _mediaBookmarkNameBox.IsEnabled = plan.HasMedia;
        _mediaBookmarkTimeBox.IsEnabled = plan.HasMedia;
        _mediaBookmarkCreateButton.IsEnabled = plan.HasMedia;
        _mediaBookmarkReplaceButton.IsEnabled = plan.HasSelectedBookmark;
        _mediaBookmarkDeleteButton.IsEnabled = plan.HasSelectedBookmark;
    }

    private void RefreshMediaCaptionAuthoringPlans(
        string? proposedLabel,
        string? proposedLanguage,
        string? proposedSource,
        string? proposedTranscriptText)
    {
        _mediaPaneSession.RefreshCaptionAuthoringPanePlan(
            proposedLabel,
            proposedLanguage,
            proposedSource,
            proposedTranscriptText);
    }

    private void RefreshVisibleMediaCaptionPaneFromFields()
    {
        if (_mediaCaptionPaneRefreshing || !IsMediaCaptionPaneVisible)
            return;

        RefreshMediaCaptionAuthoringPlans(
            _mediaCaptionLabelBox.Text,
            _mediaCaptionLanguageBox.Text,
            _mediaCaptionSourceBox.Text,
            _mediaCaptionTranscriptBox.Text);
        RenderMediaCaptionPane(LastMediaCaptionAuthoringPanePlan!);
    }

    private void RenderMediaCaptionPane(PresentationMediaCaptionAuthoringPanePlan plan)
    {
        _mediaCaptionPaneRefreshing = true;
        try
        {
            _mediaCaptionPaneHeading.Text = plan.Heading;
            _mediaCaptionPaneMessage.Text = plan.Message;
            RenderMediaCaptionTrackOptions(plan);
            RenderMediaCaptionField(_mediaCaptionLabelText, _mediaCaptionLabelBox, plan.Label);
            RenderMediaCaptionField(_mediaCaptionLanguageText, _mediaCaptionLanguageBox, plan.Language);
            RenderMediaCaptionField(_mediaCaptionSourceText, _mediaCaptionSourceBox, plan.Source);
            RenderMediaCaptionField(_mediaCaptionTranscriptText, _mediaCaptionTranscriptBox, plan.TranscriptText);
            var mediaPlan = _mediaPaneSession.BuildProjection();
            _mediaStartModeBox.SelectedIndex = PresentationMediaPaneSession.GetPlaybackStartModeIndex(mediaPlan.PlaybackStartMode);
            _mediaLoopCheckBox.IsChecked = mediaPlan.Loop;
            _mediaShowWhenStoppedCheckBox.IsChecked = mediaPlan.ShowWhenStopped;
            _mediaRewindAfterPlayingCheckBox.IsChecked = mediaPlan.RewindAfterPlaying;
            _mediaPlayFullScreenCheckBox.IsChecked = mediaPlan.PlayFullScreen;
            _mediaStopAfterSlidesBox.Text = mediaPlan.StopAfterSlides.ToString();
            _mediaStartModeBox.IsEnabled = mediaPlan.HasMedia;
            _mediaLoopCheckBox.IsEnabled = mediaPlan.HasMedia;
            _mediaShowWhenStoppedCheckBox.IsEnabled = mediaPlan.HasMedia;
            _mediaRewindAfterPlayingCheckBox.IsEnabled = mediaPlan.HasMedia;
            _mediaPlayFullScreenCheckBox.IsEnabled = mediaPlan.CanPlayFullScreen;
            _mediaStopAfterSlidesBox.IsEnabled = mediaPlan.CanStopAfterSlides;
            _mediaPlaybackApplyButton.IsEnabled = mediaPlan.HasMedia;
            _mediaVolumeSlider.Value = mediaPlan.VolumePercent;
            _mediaVolumeSlider.IsEnabled = mediaPlan.HasMedia;
            _mediaVolumeApplyButton.IsEnabled = mediaPlan.HasMedia;
            _mediaTimingApplyButton.IsEnabled = mediaPlan.HasMedia;
            _mediaTrimStartBox.Text = mediaPlan.Timing.TrimStartText;
            _mediaTrimEndBox.Text = mediaPlan.Timing.TrimEndText;
            _mediaFadeInBox.Text = mediaPlan.Timing.FadeInText;
            _mediaFadeOutBox.Text = mediaPlan.Timing.FadeOutText;
            RenderMediaBookmarkOptions(mediaPlan);
            ApplyMediaCaptionButtonPlan(
                _mediaCaptionCreateButton,
                GetMediaCaptionPaneAction(plan, PresentationMediaTranscriptPlanner.CaptionAuthoringPaneCreateCommandId));
            ApplyMediaCaptionButtonPlan(
                _mediaCaptionReplaceButton,
                GetMediaCaptionPaneAction(plan, PresentationMediaTranscriptPlanner.CaptionAuthoringPaneReplaceCommandId));
            ApplyMediaCaptionButtonPlan(
                _mediaCaptionDeleteButton,
                GetMediaCaptionPaneAction(plan, PresentationMediaTranscriptPlanner.CaptionAuthoringPaneDeleteCommandId));
            ApplyMediaCaptionButtonPlan(
                _mediaCaptionCloseButton,
                GetMediaCaptionPaneAction(plan, PresentationMediaTranscriptPlanner.CaptionAuthoringPaneCloseCommandId));
        }
        finally
        {
            _mediaCaptionPaneRefreshing = false;
        }
    }

    private void RenderMediaCaptionTrackOptions(PresentationMediaCaptionAuthoringPanePlan plan)
    {
        _mediaCaptionTrackBox.ItemsSource = null;
        _mediaCaptionTrackBox.Items.Clear();
        foreach (var (track, itemIndex) in plan.Tracks.Select((track, index) => (track, index)))
        {
            var item = new ComboBoxItem
            {
                Content = track.DisplayText,
                Tag = track.TrackIndex,
            };
            PresentationPaneAccessibilityAdapter.ApplyItem(
                item,
                PresentationPaneAccessibilityPlanner.MediaCaptionPaneId,
                itemIndex,
                track.Label,
                track.IsSelected ? "Selected" : "Not selected");
            _mediaCaptionTrackBox.Items.Add(item);
        }
        _mediaCaptionTrackBox.IsEnabled = plan.Tracks.Count > 0;
        _mediaCaptionTrackBox.SelectedIndex = plan.SelectedTrackListIndex;
    }

    private static void RenderMediaCaptionField(
        TextBlock label,
        TextBox textBox,
        PresentationMediaCaptionAuthoringFieldPlan field)
    {
        label.Text = field.ValidationMessage is null
            ? field.Label
            : $"{field.Label} - {field.ValidationMessage}";
        textBox.PlaceholderText = field.Placeholder;
        ToolTip.SetTip(textBox, field.ValidationMessage ?? field.Placeholder);
        textBox.IsEnabled = field.IsEnabled;
        SetTextIfChanged(textBox, field.Value);
    }

    private static PresentationMediaCaptionAuthoringActionPlan GetMediaCaptionPaneAction(
        PresentationMediaCaptionAuthoringPanePlan plan,
        string commandId)
        => plan.Actions.Single(action => action.CommandId == commandId);

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

    internal PresentationSelectionPanePlan ShowSelectionPane()
    {
        var plan = _selectionPane.Refresh();
        _selectionPane.IsVisible = true;
        RefreshPaneAccessibilityMetadata();
        return plan;
    }

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
    {
        if (!IsAltTextPaneVisible)
            ShowAltTextPane();

        _altTextPaneRefreshing = true;
        try
        {
            _altTextTitleBox.Text = title;
            _altTextDescriptionBox.Text = description;
            _altTextDecorativeCheck.IsChecked = isDecorative;
        }
        finally
        {
            _altTextPaneRefreshing = false;
        }

        RefreshVisibleAltTextPaneFromFields();
    }

    internal PresentationAltTextMutationPlan ApplyAltTextPane()
    {
        var plan = ApplySelectedShapeAlternativeText(
            _altTextDescriptionBox.Text,
            _altTextTitleBox.Text,
            _altTextDecorativeCheck.IsChecked == true);
        if (LastAltTextPanePlan is not null)
            RenderAltTextPane(LastAltTextPanePlan);

        return plan;
    }

    private void RefreshAltTextPlans(
        string? proposedDescription,
        string? proposedTitle,
        bool? isDecorative)
        => _reviewWorkflowSession.RefreshAltTextPlans(proposedDescription, proposedTitle, isDecorative);

    private void RefreshVisibleAltTextPaneFromFields()
    {
        if (_altTextPaneRefreshing || !IsAltTextPaneVisible)
            return;

        RefreshAltTextPlans(
            _altTextDescriptionBox.Text,
            _altTextTitleBox.Text,
            _altTextDecorativeCheck.IsChecked == true);
        if (LastAltTextPanePlan is not null)
            RenderAltTextPane(LastAltTextPanePlan);
    }

    private void RenderAltTextPaneIfVisible(PresentationAltTextPanePlan plan)
    {
        if (IsAltTextPaneVisible)
            RenderAltTextPane(plan);
    }

    private void RenderAltTextPane(PresentationAltTextPanePlan plan)
    {
        _altTextPaneRefreshing = true;
        try
        {
            var applyAction = GetAltTextPaneAction(plan, PresentationReviewWorkflowPlanner.AltTextPaneApplyCommandId);
            var decorativeAction = GetAltTextPaneAction(plan, PresentationReviewWorkflowPlanner.AltTextPaneDecorativeCommandId);
            var closeAction = GetAltTextPaneAction(plan, PresentationReviewWorkflowPlanner.AltTextPaneCloseCommandId);

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
            _altTextDecorativeCheck.Content = decorativeAction.Label;
            _altTextDecorativeCheck.IsEnabled = decorativeAction.IsEnabled;
            _altTextDecorativeCheck.IsChecked = plan.IsDecorative;
            _altTextApplyButton.Content = applyAction.Label;
            _altTextApplyButton.IsEnabled = applyAction.IsEnabled;
            _altTextCloseButton.Content = closeAction.Label;
            _altTextCloseButton.IsEnabled = closeAction.IsEnabled;
        }
        finally
        {
            _altTextPaneRefreshing = false;
        }
    }

    private static PresentationReviewWorkflowActionPlan GetAltTextPaneAction(
        PresentationAltTextPanePlan plan,
        string commandId)
        => plan.Actions.Single(action => action.CommandId == commandId);

    private void RenderReadingOrderPane(PresentationReadingOrderPlan plan)
    {
        _readingOrderPaneHeading.Text = plan.Heading;
        _readingOrderPaneMessage.Text = plan.DisplayMessage;

        var moveEarlier = GetReadingOrderAction(
            plan,
            PresentationReviewWorkflowPlanner.ReadingOrderMoveEarlierCommandId);
        var moveLater = GetReadingOrderAction(
            plan,
            PresentationReviewWorkflowPlanner.ReadingOrderMoveLaterCommandId);
        ApplyReadingOrderButtonPlan(_readingOrderMoveEarlierButton, moveEarlier);
        ApplyReadingOrderButtonPlan(_readingOrderMoveLaterButton, moveLater);

        _readingOrderPaneItemsPanel.Children.Clear();
        if (plan.Items.Count == 0)
        {
            _readingOrderPaneItemsPanel.Children.Add(new TextBlock
            {
                Text = PresentationReviewWorkflowPlanner.EmptyReadingOrderMessage,
                Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
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
                PresentationPaneAccessibilityPlanner.ReadingOrderPaneId,
                item.ReadingOrderIndex,
                item.ShapeName,
                item.IsSelected ? "Selected" : "Not selected");
            _readingOrderPaneItemsPanel.Children.Add(card);
        }
    }

    private void RenderReadingOrderPaneIfVisible(PresentationReadingOrderPlan plan)
    {
        if (IsReadingOrderPaneVisible)
            RenderReadingOrderPane(plan);
    }

    private void PresentReadingOrderPane(PresentationReadingOrderPlan plan)
    {
        RenderReadingOrderPane(plan);
        _readingOrderPaneHost.IsVisible = true;
        RefreshPaneAccessibilityMetadata();
    }

    private static PresentationReviewWorkflowActionPlan GetReadingOrderAction(
        PresentationReadingOrderPlan plan,
        string commandId)
        => plan.Actions.Single(action => action.CommandId == commandId);

    private static void ApplyReadingOrderButtonPlan(
        Button button,
        PresentationReviewWorkflowActionPlan action)
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
                    Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = item.AccessibilitySummary,
                    FontSize = PresentationReadingOrderPaneVisualMetrics.BodyFontSize,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = item.AltTextDisplayText,
                    FontSize = PresentationReadingOrderPaneVisualMetrics.BodyFontSize,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
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
                Foreground = new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A)),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, PresentationReadingOrderPaneVisualMetrics.SelectedItemTopInset, 0, 0),
            });
        }

        var card = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = item.IsSelected
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0xF6, 0xF2))
                : new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)),
            BorderBrush = item.IsSelected
                ? new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A))
                : new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
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

    internal PresentationAltTextMutationPlan ApplySelectedShapeAlternativeText(
        string? description,
        string? title = null,
        bool isDecorative = false)
        => _reviewWorkflowSession.ApplySelectedShapeAlternativeText(description, title, isDecorative);

    internal PresentationProofingCorrectionMutationPlan ApplyProofingCorrection(
        PresentationProofingScopeDescriptor scope,
        int start,
        int length,
        string? replacement)
        => _reviewWorkflowSession.ApplyProofingCorrection(scope, start, length, replacement);

    private void RefreshProofingRequestPlan()
        => _reviewWorkflowSession.RefreshProofingRequestPlan();

    internal PresentationProofingPanePlan ShowProofingPane()
        => _reviewWorkflowSession.ShowProofingPane();

    internal PresentationProofingPanePlan SelectProofingIssueRow(int rowIndex)
        => _reviewWorkflowSession.SelectProofingIssueRow(rowIndex);

    internal PresentationProofingCorrectionMutationPlan ApplySelectedProofingCorrection()
        => _reviewWorkflowSession.ApplySelectedProofingCorrection();

    internal PresentationProofingPanePlan IgnoreSelectedProofingIssue()
        => _reviewWorkflowSession.IgnoreSelectedProofingIssue();

    internal PresentationProofingPanePlan IgnoreAllSelectedProofingIssues()
        => _reviewWorkflowSession.IgnoreAllSelectedProofingIssues();

    internal PresentationProofingPanePlan AddSelectedProofingWordToDictionary()
        => _reviewWorkflowSession.AddSelectedProofingWordToDictionary();

    private void RenderProofingPaneIfVisible(PresentationProofingPanePlan plan)
    {
        if (IsProofingPaneVisible)
            RenderProofingPane(plan);
    }

    private void PresentProofingPane(PresentationProofingPanePlan plan)
    {
        RenderProofingPane(plan);
        _proofingPaneHost.IsVisible = true;
        RefreshPaneAccessibilityMetadata();
    }

    private void RenderProofingPane(PresentationProofingPanePlan plan)
    {
        _proofingPaneHeading.Text = plan.Heading;
        _proofingPaneMessage.Text = plan.DisplayMessage;

        _proofingPaneRowsPanel.Children.Clear();
        if (plan.Rows.Count == 0)
        {
            _proofingPaneRowsPanel.Children.Add(new TextBlock
            {
                Text = plan.Message,
                Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                Margin = new Thickness(12, 0, 12, 10),
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        foreach (var row in plan.Rows)
            _proofingPaneRowsPanel.Children.Add(BuildProofingIssueRowCard(row));
    }

    private Control BuildProofingIssueRowCard(PresentationProofingIssueRowPlan row)
    {
        var action = new Button
        {
            Content = row.CorrectionAction.Label,
            Tag = row.RowIndex,
            MinWidth = 72,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
            IsEnabled = row.CorrectionAction.IsEnabled,
        };
        ToolTip.SetTip(action, row.CorrectionAction.DisabledReason);
        action.Click += (_, _) =>
        {
            SelectProofingIssueRow(row.RowIndex);
            ApplySelectedProofingCorrection();
        };

        var ignore = new Button
        {
            Content = row.IgnoreAction.Label,
            Tag = row.RowIndex,
            MinWidth = 72,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(8, 8, 0, 0),
            IsEnabled = row.IgnoreAction.IsEnabled,
        };
        ToolTip.SetTip(ignore, row.IgnoreAction.DisabledReason);
        ignore.Click += (_, _) =>
        {
            SelectProofingIssueRow(row.RowIndex);
            IgnoreSelectedProofingIssue();
        };

        var ignoreAll = new Button
        {
            Content = row.IgnoreAllAction.Label,
            Tag = row.RowIndex,
            MinWidth = 72,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(8, 8, 0, 0),
            IsEnabled = row.IgnoreAllAction.IsEnabled,
        };
        ToolTip.SetTip(ignoreAll, row.IgnoreAllAction.DisabledReason);
        ignoreAll.Click += (_, _) =>
        {
            SelectProofingIssueRow(row.RowIndex);
            IgnoreAllSelectedProofingIssues();
        };

        var addToDictionary = new Button
        {
            Content = row.AddToDictionaryAction.Label,
            Tag = row.RowIndex,
            MinWidth = 120,
            Margin = new Thickness(8, 8, 0, 0),
            IsEnabled = row.AddToDictionaryAction.IsEnabled,
        };
        ToolTip.SetTip(addToDictionary, row.AddToDictionaryAction.DisabledReason);
        addToDictionary.Click += (_, _) =>
        {
            SelectProofingIssueRow(row.RowIndex);
            AddSelectedProofingWordToDictionary();
        };

        var select = new Button
        {
            Content = "Select",
            Tag = row.RowIndex,
            MinWidth = 72,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(8, 8, 0, 0),
        };
        select.Click += (_, _) => SelectProofingIssueRow(row.RowIndex);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { action, ignore, ignoreAll, addToDictionary, select },
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 2,
            Children =
            {
                new TextBlock
                {
                    Text = $"{row.SlideDisplay} - {row.SourceName}",
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = $"{row.Text} -> {row.SuggestedReplacement}",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
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
            Background = row.IsSelected ? new SolidColorBrush(Color.FromRgb(0xE8, 0xF1, 0xFF)) : Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 8, 12, 8),
            Child = panel,
        };
        PresentationPaneAccessibilityAdapter.ApplyItem(
            border,
            PresentationPaneAccessibilityPlanner.ProofingPaneId,
            row.RowIndex,
            row.Text,
            row.IsSelected ? "Selected" : "Not selected");
        return border;
    }

    private async Task<bool> TrySavePresentationFileAsync(string path) =>
        (await _fileSession.SavePathAsync(path)).Succeeded;

    internal Task<bool> TrySavePresentationFileAsyncForTests(string path) =>
        TrySavePresentationFileAsync(path);

    private static bool IsSupportedPresentationPath(string path) =>
        PresentationFilePersistenceWorkflow.IsSupportedPresentationPath(path);

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
                    PresentationPaneAccessibilityPlanner.SlidePaneId,
                    projected.AccessibilityOrdinal,
                    plan.AccessibleName,
                    plan.IsSelected ? "Selected" : "Not selected",
                    $"Slide{plan.SlideIndex + 1}");
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
            PresentationPaneAccessibilityPlanner.SlidePaneId,
            projected.AccessibilityOrdinal,
            plan.AccessibleName,
            "Not selected",
            $"Section{plan.SectionIndex + 1}");
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

    internal ContextMenu BuildSlidePaneContextMenuForTests(int slideIndex) =>
        BuildSlidePaneContextMenu(slideIndex);

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

    internal ContextMenu BuildSlidePaneSectionContextMenuForTests(SlidePaneEntry entry) =>
        BuildSlidePaneSectionContextMenu(entry);

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
            _statusText.Text = SisterAppFileTextPlanner.FormatCommandFailed(FileText, "Slide Pane", ex.Message);
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
            promptedName = await PromptSectionNameAsync(execution.PromptTitle, execution.SuggestedName);
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

    internal bool ToggleSlidePaneSectionForTests(int sectionIndex)
    {
        if (sectionIndex < 0 || sectionIndex >= _presentation.Sections.Count)
            return false;

        ToggleSlidePaneSection(SlidePanePlanner.GetSectionIdentity(_presentation.Sections[sectionIndex], sectionIndex));
        return true;
    }

    internal bool TryApplySlideSectionActionForTests(
        SlideSectionActionKind kind,
        int slideIndex = -1,
        int sectionIndex = -1,
        string? promptedName = null)
    {
        var command = kind switch
        {
            SlideSectionActionKind.AddSection => FreePContextMenuCommand.AddSection,
            SlideSectionActionKind.RenameSection => FreePContextMenuCommand.RenameSection,
            SlideSectionActionKind.RemoveSection => FreePContextMenuCommand.RemoveSection,
            SlideSectionActionKind.RemoveAllSections => FreePContextMenuCommand.RemoveAllSections,
            _ => default,
        };
        var execution = _workareaSession.BuildSlidePaneContextCommandRoute(
                command,
                slideIndex,
                sectionIndex)
            .SectionExecution;
        return execution is not null &&
            _workareaSession.ExecuteSlidePaneSectionAction(execution, promptedName);
    }

    private async Task<string?> PromptSectionNameAsync(string title, string initialName)
    {
        var textBox = new TextBox
        {
            Text = initialName,
            MinWidth = 260,
            Margin = new Thickness(0, 0, 0, 12),
        };

        var ok = new Button
        {
            Content = "OK",
            Width = 76,
            Margin = new Thickness(0, 0, 8, 0),
        };
        var cancel = new Button
        {
            Content = "Cancel",
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
                    Text = "Section name:",
                    Margin = new Thickness(0, 0, 0, 4),
                },
                textBox,
                buttons,
            },
        };

        var dialog = new Window
        {
            Title = title,
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

    internal SlidePaneDropVisualPlan PreviewSlidePaneDragForTests(
        int sourceSlideIndex,
        double startPointerY,
        double pointerYWithinItem,
        double pointerYWithinPane)
    {
        _workareaSession.BeginSlidePaneDrag(sourceSlideIndex, startPointerY);
        var update = _workareaSession.UpdateSlidePaneDrag(
            pointerYWithinItem,
            pointerYWithinPane,
            SlidePanePlanner.DefaultSlideItemHeight);
        if (update.State.IsDragging)
            ShowSlidePaneInsertionIndicator(update.DropVisualPlan);
        else
            HideSlidePaneInsertionIndicator();

        return update.DropVisualPlan;
    }

    internal bool CompleteSlidePaneDragForTests()
    {
        var applied = _workareaSession.CompleteSlidePaneDrag(out var shouldReleaseCapture);
        HideSlidePaneInsertionIndicator();
        return shouldReleaseCapture && applied;
    }

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

    internal bool ClickSlidePaneNewSlideAffordanceForTests()
    {
        var before = _presentation.Slides.Count;
        var applied = InsertSlideFromSlidePaneAffordance();
        return applied && _presentation.Slides.Count == before + 1;
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
            Background                 = new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A)),
            Foreground                 = Brushes.White,
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
                PresentationPaneAccessibilityPlanner.SlidePaneId,
                projected.AccessibilityOrdinal,
                plan.AccessibleName,
                plan.IsActive ? "Active and selected" : plan.IsSelected ? "Selected" : "Not selected",
                $"Slide{slideIndex + 1}");
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
        _startupDirtyTrace?.Record("notes-text-changed", _fileWorkflow);
        var result = _notesPaneSession.ApplyText(_notesBox.Text);
        LastNotesPagePreviewPlan = result.Plan.Preview;
        if (!result.Changed)
            return;
        RefreshPaneAccessibilityMetadata();
    }

    // ── Event handlers ─────────────────────────────────────────────────────────

    private void SyncSlidePaneSelectionFromEditor()
    {
        _slidePaneRefreshing = true;
        try { SyncSlidePaneSelectionFromSession(); }
        finally { _slidePaneRefreshing = false; }
    }

    private void SyncRibbonCommandStates()
    {
        if (_ribbonControl is not null)
        {
            AvaloniaRibbonRenderer.SyncToggleStates(
                _ribbonControl,
                _ribbonCommandRegistry,
                RibbonVisualPalette.FromTheme(App.ActiveTheme),
                _ribbonStateStore);
        }
    }

    private void OnFileWorkflowChanged()
    {
        _startupDirtyTrace?.Record("file-workflow-changed", _fileWorkflow);
        UpdateStatus();
    }

    // ── Status ─────────────────────────────────────────────────────────────────

    private void UpdateStatus()
    {
        _statusText.Text = _workareaSession.BuildStatusPlan(ResolveDataFolderLabel()).Text;
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
        if (TryQueueActiveRichClipboard(static editor => editor.CopySelectionAsync()))
            return;

        var request = _clipboardService.PrepareWrite(Editor);
        QueueClipboardOperation(() => _clipboardService.ExecuteCopyAsync(request));
    }

    private void QueueClipboardCut()
    {
        if (TryQueueActiveRichClipboard(static editor => editor.CutSelectionAsync()))
            return;

        var request = _clipboardService.PrepareWrite(Editor);
        QueueClipboardOperation(() => _clipboardService.ExecuteCutAsync(request));
    }

    private void QueueClipboardPaste()
    {
        if (TryQueueActiveRichClipboard(static editor => editor.PasteClipboardAsync()))
            return;

        var request = _clipboardService.PreparePaste(Editor);
        QueueClipboardOperation(() => _clipboardService.ExecutePasteAsync(request));
    }

    private bool TryQueueActiveRichClipboard(
        Func<AvaloniaInCanvasTextEditor, Task<bool>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var textEditor = _textEditor;
        if (textEditor?.IsRichTextEditActive != true)
            return false;

        QueueClipboardOperation(async () =>
        {
            _ = await operation(textEditor);
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
        if (args.Key is Key.LeftAlt or Key.RightAlt ||
            args.Key == Key.F10 && args.KeyModifiers == KeyModifiers.None)
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
        if (token is null)
            return false;

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

    private static bool KeyTipEquals(string? keyTip, string sequence)
        => string.Equals(keyTip?.Trim(), sequence, StringComparison.OrdinalIgnoreCase);

    private static bool KeyTipStartsWith(string? keyTip, string sequence)
        => !string.IsNullOrWhiteSpace(keyTip) &&
           keyTip.Trim().StartsWith(sequence, StringComparison.OrdinalIgnoreCase);

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

    private static string? ToRibbonKeyTipToken(Key key)
    {
        var name = key.ToString();
        if (name.Length == 1 && char.IsAsciiLetterOrDigit(name[0]))
            return name.ToUpperInvariant();
        if (name.Length == 2 && name[0] == 'D' && char.IsAsciiDigit(name[1]))
            return name[1].ToString();
        return null;
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
    {
        if (!_customShowSession.TryBuildLaunchRoute(fromStart, animationStartIndex, out var route))
            return;

        var selectedCaption = GetSelectedCaptionPlaybackSelection();
        var slideShow = new SlideShowWindow(
            _presentation,
            route,
            Editor.SetSlideNotesText,
            selectedCaption?.SlideIndex,
            selectedCaption?.ShapeId,
            selectedCaption?.TrackIndex);
        if (timingIntent != FreeP.App.Compositor.SlideShowTimingIntent.None)
            slideShow.SetPresenterTimingIntent(timingIntent);

        // WPF leaves the editor selection unchanged while the separate slideshow window
        // plays. Avalonia must keep that same editor-side selection authority on close.
        slideShow.Closed += (_, _) =>
        {
            RestoreOwnerFocus();
        };

        if (IsVisible)
            slideShow.Show(this);
        else
            slideShow.Show();
    }

    private (int SlideIndex, uint ShapeId, int TrackIndex)? GetSelectedCaptionPlaybackSelection()
    {
        var mediaShape = PresentationMediaTranscriptPlanner.FindSelectedMediaShape(
            Editor.CurrentSlide,
            Editor.SelectedShapeIds);
        return mediaShape is not null && _mediaPaneSession.SelectedCaptionTrackIndex is int trackIndex
            ? (Editor.CurrentSlideIndex, mediaShape.Id, trackIndex)
            : null;
    }

    internal bool TryBuildCustomSlideShowRoute(
        string? customShowName,
        int startIndex,
        out SlideShowPlaybackRoute route) =>
        _customShowSession.TryBuildNamedRoute(customShowName, startIndex, out route);

    internal SlideShowLaunchPlan BuildSlideShowLaunchPlan() =>
        _customShowSession.BuildLaunchPlan();

    internal bool TryStartCustomSlideShow(string? customShowName, int startIndex = 0)
    {
        if (!TryBuildCustomSlideShowRoute(customShowName, startIndex, out var route) ||
            route.SlideCount == 0)
        {
            return false;
        }

        var selectedCaption = GetSelectedCaptionPlaybackSelection();
        var slideShow = new SlideShowWindow(
            _presentation,
            route,
            Editor.SetSlideNotesText,
            selectedCaption?.SlideIndex,
            selectedCaption?.ShapeId,
            selectedCaption?.TrackIndex);
        // A named custom show is still a separate playback window. Restore the
        // editor's focus when it closes just like the normal slideshow route.
        slideShow.Closed += (_, _) => RestoreOwnerFocus();

        if (IsVisible)
            slideShow.Show(this);
        else
            slideShow.Show();
        return true;
    }

    internal void OpenCustomShowDialog() =>
        RunGuarded(OpenCustomShowDialogAsync, "Custom Show");

    internal Task OpenCustomShowDialogAsyncForTests() =>
        OpenCustomShowDialogAsync();

    private async Task OpenCustomShowDialogAsync()
    {
        var dialog = new CustomShowDialog(
            _customShowSession,
            name => TryStartCustomSlideShow(name));
        await dialog.ShowDialog(this);
    }

}
