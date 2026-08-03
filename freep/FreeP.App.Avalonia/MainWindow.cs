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
using FreeP.App.Compositor.Printing;
using FreeP.App.Recording;
#if FREEP_WINDOWS_CAPTURE
using FreeP.App.Recording.Windows;
#endif
using FreeP.App.Rendering.Avalonia;
using FreeP.Core.IO;
using FreeP.Core.Model;
using System.Globalization;
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
    private static readonly SisterAppFileTextSpec FileText = SisterAppFileTextPlanner.Presentation;
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

    private static readonly (string CommandId, Action<EditingSession> Execute)[] ArrangeCommandRoutes =
    [
        ("freep.arrange.group", static editor => editor.GroupSelectedShapes()),
        ("freep.arrange.ungroup", static editor => editor.UngroupSelected()),
        (ShapeChangePlanner.RectangleCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.Rectangle)),
        (ShapeChangePlanner.RoundedRectangleCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.RoundedRectangle)),
        (ShapeChangePlanner.EllipseCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.Ellipse)),
        (ShapeChangePlanner.TriangleCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.Triangle)),
        (ShapeChangePlanner.DiamondCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.Diamond)),
        (ShapeChangePlanner.RightArrowCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.RightArrow)),
        (ShapeChangePlanner.HexagonCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.Hexagon)),
        (ShapeChangePlanner.ParallelogramCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.Parallelogram)),
        (ShapeChangePlanner.TrapezoidCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.Trapezoid)),
        (ShapeChangePlanner.LeftArrowCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.LeftArrow)),
        (ShapeChangePlanner.Star5CommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.Star5)),
        (ShapeChangePlanner.UpArrowCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.UpArrow)),
        (ShapeChangePlanner.DownArrowCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.DownArrow)),
        (ShapeChangePlanner.CrossCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.Cross)),
        (ShapeChangePlanner.PlusSignCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.PlusSign)),
        (ShapeChangePlanner.PentagonCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.Pentagon)),
        (ShapeChangePlanner.OctagonCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.Octagon)),
        (ShapeChangePlanner.LeftRightArrowCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.LeftRightArrow)),
        (ShapeChangePlanner.UpDownArrowCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.UpDownArrow)),
        (ShapeChangePlanner.Star8CommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.Star8)),
        (ShapeChangePlanner.ChevronCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.Chevron)),
        (ShapeChangePlanner.HomePlateCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.HomePlate)),
        (ShapeChangePlanner.RightTriangleCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.RightTriangle)),
        (ShapeChangePlanner.MinusSignCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.MinusSign)),
        (ShapeChangePlanner.MultiplySignCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.MultiplySign)),
        (ShapeChangePlanner.DivideSignCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.DivideSign)),
        (ShapeChangePlanner.EqualSignCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.EqualSign)),
        (ShapeChangePlanner.NotEqualSignCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.NotEqualSign)),
        (ShapeChangePlanner.WaveCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.Wave)),
        (ShapeChangePlanner.RectangularCalloutCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.RectangularCallout)),
        (ShapeChangePlanner.RoundedRectangularCalloutCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.RoundedRectangularCallout)),
        (ShapeChangePlanner.OvalCalloutCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.OvalCallout)),
        (ShapeChangePlanner.ExplosionCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.Explosion)),
        (ShapeChangePlanner.RibbonCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.Ribbon)),
        (ShapeChangePlanner.FlowchartProcessCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.FlowchartProcess)),
        (ShapeChangePlanner.FlowchartDecisionCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.FlowchartDecision)),
        (ShapeChangePlanner.FlowchartDataCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.FlowchartData)),
        (ShapeChangePlanner.FlowchartPredefinedProcessCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.FlowchartPredefinedProcess)),
        (ShapeChangePlanner.FlowchartDocumentCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.FlowchartDocument)),
        (ShapeChangePlanner.FlowchartTerminatorCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.FlowchartTerminator)),
        (ShapeChangePlanner.LineCalloutCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.LineCallout)),
        (ShapeChangePlanner.CylinderCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.Cylinder)),
        (ShapeChangePlanner.ChordCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.Chord)),
        (ShapeChangePlanner.HeartCommandId, static editor => editor.ChangeSelectedAutoShapeKind(DrawingShapeKind.Heart)),
        ("freep.arrange.bring-to-front", static editor => editor.BringToFront()),
        ("freep.arrange.bring-forward", static editor => editor.BringForward()),
        ("freep.arrange.send-backward", static editor => editor.SendBackward()),
        ("freep.arrange.send-to-back", static editor => editor.SendToBack()),
        ("freep.arrange.flip-horizontal", static editor => editor.FlipSelectedHorizontal()),
        ("freep.arrange.flip-vertical", static editor => editor.FlipSelectedVertical()),
        ("freep.arrange.rotate-left-90", static editor => editor.RotateSelectedLeft90()),
        ("freep.arrange.rotate-right-90", static editor => editor.RotateSelectedRight90()),
        ("freep.arrange.align-left", static editor => editor.AlignLeft()),
        ("freep.arrange.align-center-h", static editor => editor.AlignCenterH()),
        ("freep.arrange.align-right", static editor => editor.AlignRight()),
        ("freep.arrange.align-top", static editor => editor.AlignTop()),
        ("freep.arrange.align-middle", static editor => editor.AlignMiddle()),
        ("freep.arrange.align-bottom", static editor => editor.AlignBottom()),
        ("freep.arrange.align-left-to-slide", static editor => editor.AlignLeftToSlide()),
        ("freep.arrange.align-center-h-to-slide", static editor => editor.AlignCenterHToSlide()),
        ("freep.arrange.align-right-to-slide", static editor => editor.AlignRightToSlide()),
        ("freep.arrange.align-top-to-slide", static editor => editor.AlignTopToSlide()),
        ("freep.arrange.align-middle-to-slide", static editor => editor.AlignMiddleToSlide()),
        ("freep.arrange.align-bottom-to-slide", static editor => editor.AlignBottomToSlide()),
        ("freep.arrange.distribute-h", static editor => editor.DistributeHorizontally()),
        ("freep.arrange.distribute-v", static editor => editor.DistributeVertically()),
    ];

    // ── Presentation model ─────────────────────────────────────────────────────

    private Presentation _presentation = Presentation.CreateEmpty();
    private readonly SisterAvaloniaFileCommandWorkflow _fileWorkflow;
    private readonly StartupDirtyTrace? _startupDirtyTrace;
    private readonly SisterAvaloniaAsyncWindowCloseCoordinator _closeCoordinator;
    private readonly AvaloniaPresentationClipboardService _clipboardService;
    private Func<FileOpenPickerPlan, Task<string?>>? _openPickerOverrideForTests;
    private Func<FileSavePickerPlan, Task<string?>>? _savePickerOverrideForTests;
    internal Func<FileSavePickerPlan, Task<VideoPickerSelectionForTests?>>? VideoPickerOverrideForTests { get; set; }
    private int _ownerFocusRestoreCount;
    private Task _clipboardOperation = Task.CompletedTask;
    private readonly FreePOptions _options;
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

    internal EditingSession Editor { get; private set; } = null!;

    // ── UI elements ────────────────────────────────────────────────────────────

    private readonly SlideCanvas _slideCanvas;
    private Border _canvasHost = null!;
    private readonly ListBox _slidePaneList;
    private readonly Border _slidePaneInsertionIndicator;
    private readonly Button _slidePaneNewSlideButton;
    private SlidePaneSessionState _slidePaneSessionState = SlidePaneSessionState.Empty;
    private SlidePaneSessionProjection? _slidePaneProjection;
    private readonly List<SlidePaneThumbnailVisualPlan> _slidePaneRenderedThumbnailPlans = new();
    private readonly List<SlidePaneSectionHeaderVisualPlan> _slidePaneRenderedSectionHeaderPlans = new();
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
    private int? _selectedAccessibilityCheckerRowIndex;
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
    private Button _mediaCaptionCreateButton = null!;
    private Button _mediaCaptionReplaceButton = null!;
    private Button _mediaCaptionDeleteButton = null!;
    private Button _mediaCaptionCloseButton = null!;
    private bool _mediaCaptionPaneRefreshing;
    private int? _selectedMediaCaptionTrackIndex;
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
    private string? _selectedSmartArtTextPaneModelId;
    private Border _animationPaneHost = null!;
    private TextBlock _animationPaneHeading = null!;
    private TextBlock _animationPaneMessage = null!;
    private StackPanel _animationPanePlaybackControlsPanel = null!;
    private StackPanel _animationPaneItemsPanel = null!;
    private Button _animationPanePreviewButton = null!;
    private readonly PresentationPaneAccessibilityAdapter _paneAccessibility = new();
    private int _selectedAnimationIndex = -1;
    private AnimationPanePlaybackSessionPlan? _animationPanePlaybackSessionPlan;
    private AnimationPanePlaybackWorkflowEvidencePlan? _animationPanePlaybackWorkflowEvidencePlan;
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
    internal Task ClipboardOperationForTests => _clipboardOperation;
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
    internal IReadOnlyList<SlidePaneThumbnailVisualPlan> SlidePaneRenderedThumbnailPlans => _slidePaneRenderedThumbnailPlans;
    internal IReadOnlyList<SlidePaneSectionHeaderVisualPlan> SlidePaneRenderedSectionHeaderPlans => _slidePaneRenderedSectionHeaderPlans;
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
    internal PresentationCommentMentionPickerPlan? LastCommentMentionPickerPlan { get; private set; }
    internal PresentationCommentMentionInsertionPlan? LastCommentMentionInsertionPlan { get; private set; }
    internal PresentationAccessibilitySummaryPlan? LastAccessibilitySummaryPlan { get; private set; }
    internal PresentationAccessibilityCheckerPanePlan? LastAccessibilityCheckerPanePlan { get; private set; }
    internal PresentationSlideTitleMutationPlan? LastSlideTitleMutationPlan { get; private set; }
    internal PresentationChartTitleMutationPlan? LastChartTitleMutationPlan { get; private set; }
    internal PresentationTableHeaderRowMutationPlan? LastTableHeaderRowMutationPlan { get; private set; }
    internal PresentationTableStructureReviewPlan? LastTableStructureReviewPlan { get; private set; }
    internal PresentationTableStructureReviewDisplayPlan? LastTableStructureReviewDisplayPlan { get; private set; }
    internal PresentationAltTextRequestPlan? LastAltTextRequestPlan => _reviewWorkflowSession.LastAltTextRequestPlan;
    internal PresentationAltTextPanePlan? LastAltTextPanePlan => _reviewWorkflowSession.LastAltTextPanePlan;
    internal PresentationReadingOrderPlan? LastReadingOrderPlan => _reviewWorkflowSession.LastReadingOrderPlan;
    internal PresentationProofingRequestPlan? LastProofingRequestPlan => _reviewWorkflowSession.LastProofingRequestPlan;
    internal PresentationProofingExecutionPlan? LastProofingExecutionPlan => _reviewWorkflowSession.LastProofingExecutionPlan;
    internal PresentationProofingPanePlan? LastProofingPanePlan => _reviewWorkflowSession.LastProofingPanePlan;
    internal PresentationMediaTranscriptPlan? LastMediaTranscriptPlan { get; private set; }
    internal PresentationMediaCaptionAuthoringPanePlan? LastMediaCaptionAuthoringPanePlan { get; private set; }
    internal PresentationMediaCaptionAuthoringMutationPlan? LastMediaCaptionAuthoringMutationPlan { get; private set; }
    internal PresentationMediaCaptionTrackMutationResult? LastMediaCaptionTrackMutationResult { get; private set; }
    internal SmartArtTextPaneApplyResult? LastSmartArtTextPaneApplyResult { get; private set; }
    internal SmartArtNodeEditResult? LastSmartArtTextPaneEditResult { get; private set; }
    internal SmartArtTextPaneKeyboardRoute? LastSmartArtTextPaneKeyboardRoute { get; private set; }
    internal SmartArtColorApplyResult? LastSmartArtColorApplyResult { get; private set; }
    internal SmartArtDataPartRewriteResult? LastSmartArtDataPartRewriteResult { get; private set; }
    internal SmartArtDrawingCacheRegenerationResult? LastSmartArtDrawingCacheRegenerationResult { get; private set; }
    internal AnimationPaneTimelinePlan? LastAnimationPaneTimelinePlan { get; private set; }
    internal AnimationPaneWorkflowEvidencePlan? LastAnimationPaneWorkflowEvidencePlan { get; private set; }
    internal AnimationPanePlaybackSessionPlan? LastAnimationPanePlaybackSessionPlan => _animationPanePlaybackSessionPlan;
    internal AnimationPanePlaybackWorkflowEvidencePlan? LastAnimationPanePlaybackWorkflowEvidencePlan =>
        _animationPanePlaybackWorkflowEvidencePlan;
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
            StringComparer.Ordinal.Equals(item.ModelId, _selectedSmartArtTextPaneModelId)) ?? 0;
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
            showPrintSelectionDialog = null)
    {
        _startupDirtyTrace = enableStartupDirtyTrace ? new StartupDirtyTrace() : null;
        Title = DefaultTitle;
        Width = 1280;
        Height = 760;
        MinWidth = 800;
        MinHeight = 500;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));
        ApplyWindowIcon();
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

        // Build editing session around the initial empty presentation.
        RebuildEditor();
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
        _startupDirtyTrace?.Record("file-workflow-created", _fileWorkflow);
        _closeCoordinator = new SisterAvaloniaAsyncWindowCloseCoordinator(
            confirmCloseAllowedAsync: () => _fileWorkflow.ConfirmCloseAllowedAsync("closing"),
            requestClose: Close,
            restoreOwnerFocus: RestoreOwnerFocus);

        _reviewWorkflowSession = new(
            () => Editor,
            new PresentationReviewWorkflowSessionCallbacks(
                MarkDirty: () => _fileWorkflow.MarkDirty(),
                RefreshCanvas: RefreshCanvas,
                RefreshNotesPane: RefreshNotesPane,
                RefreshAccessibilitySummaryPlan: RefreshAccessibilitySummaryPlan,
                RenderCommentPane: ShowReviewCommentsPane,
                RenderAltTextPaneIfVisible: RenderAltTextPaneIfVisible,
                RenderProofingPaneIfVisible: RenderProofingPaneIfVisible,
                UpdateAfterCommentMutation: UpdateStatus,
                UpdateAfterCommentNavigation: UpdateStatus,
                UpdateAfterProofingCorrection: UpdateStatus));

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
                Save: () => _ = FileSaveAsync(),
                Undo: () => Editor.Undo(),
                Redo: () => Editor.Redo()),
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
            _findReplaceDialog?.Close();
            _slideSizeDialog?.Close(false);
            _headerFooterDialog?.Close(false);
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
                _statusText.Text = SisterAppFileTextPlanner.FormatOpened(Path.GetFileName(startupPresentation));
            }
            catch (Exception ex)
            {
                startupOpenError = ex;
                LoadPresentationAsSaved(_presentation, path: null);
                _startupDirtyTrace?.Record("startup-load-failed-fallback-saved", _fileWorkflow);
                _statusText.Text = SisterAppFileTextPlanner.FormatCommandFailed(
                    SisterAppFileTextPlanner.OpenCommand,
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
                        RenderPrintOptionsPane(RefreshPrintBackstagePlan());
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

    private void RebuildEditor()
    {
        var bus = new PresentationCommandBus(_presentation);
        Editor  = new EditingSession(_presentation, bus);
        _selectionPane?.SetEditor(Editor);

        Editor.Changed             += OnEditorChanged;
        Editor.CurrentSlideChanged += OnCurrentSlideChanged;
        Editor.SelectionChanged    += OnEditorSelectionChanged;
    }

    private void RebuildEditorAndRewireInteraction()
    {
        RebuildEditor();
        // Only re-wire if the interaction layer has already been built (BuildBody sets it up).
        if (_adorner is not null)
            RewireInteractionToEditor();
    }

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
        var selectionPlan = PresentationSelectionPanePlanner.Build(
            Editor.CurrentSlide,
            Editor.CurrentSlideIndex,
            Editor.SelectedShapeIds);
        var animationPlan = AnimationPanePlanner.BuildTimelinePlan(
            Editor.CurrentSlide,
            Editor.SelectedShapeIds,
            _selectedAnimationIndex,
            isPlaybackRunning: _animationPanePlaybackSessionPlan?.IsRunning == true);
        LastAnimationPaneTimelinePlan = animationPlan;
        var selectedSmartArtRow = _smartArtTextPaneRowsPanel?.Children
            .OfType<TextBox>()
            .FirstOrDefault(box =>
                box.Tag is SmartArtNodeOutlineItem item &&
                StringComparer.Ordinal.Equals(item.ModelId, _selectedSmartArtTextPaneModelId));

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
            Array.FindIndex(selectionPlan.Items.ToArray(), item => item.IsSelected));
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
            Text = "Alt Text",
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
            Text = "Media Captions",
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
            _selectedMediaCaptionTrackIndex = selectedIndex >= 0
                && selectedIndex < LastMediaCaptionAuthoringPanePlan.Tracks.Count
                    ? LastMediaCaptionAuthoringPanePlan.Tracks[selectedIndex].TrackIndex
                    : null;
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
                    new WrapPanel
                    {
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(12, 8, 12, 12),
                        Children =
                        {
                            _mediaCaptionCreateButton,
                            _mediaCaptionReplaceButton,
                            _mediaCaptionDeleteButton,
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
            Text = "Spelling",
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
            Text = "Reading Order",
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
            Text = "Animation Pane",
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
            Content = "Preview",
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
            OnChartPointDoubleClick);
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
            textOverlay = canvasStack.Children.OfType<Canvas>().SingleOrDefault();
        }

        if (textOverlay is not null)
        {
            _gestureHandler = new AvaloniaCanvasGestureHandler(
                _slideCanvas,
                Editor,
                _adorner,
                OnChartPointDoubleClick);
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

        var slide = Editor.CurrentSlide;
        if (slide is null || Editor.Presentation is null)
            return;

        var point = e.GetPosition(_slideCanvas);
        var slidePoint = _slideCanvas.CurrentTransform.ScreenToSlide(point.X, point.Y);
        var hitId = ShapeHitTester.HitTest(slide, Editor.Presentation, slidePoint.X, slidePoint.Y);
        var shape = hitId.HasValue
            ? ShapeTreeLookup.Find(slide, hitId.Value)
            : null;
        if (shape?.Kind == SlideShapeKind.Chart && shape.Chart is not null &&
            ChartSubtargetHitTester.TryHitTest(slide, Editor.Presentation, slidePoint.X, slidePoint.Y, out var chartHit))
        {
            Editor.Select(shape.Id);
            var chartMenu = BuildChartContextMenu(chartHit);
            _slideCanvas.ContextMenu = chartMenu;
            chartMenu.Open(_slideCanvas);
            e.Handled = true;
            return;
        }

        if (shape?.Kind != SlideShapeKind.Table || shape.Table is null)
            return;

        var cellHit = TableCellHitTester.HitTest(shape, slidePoint.X, slidePoint.Y);
        if (!cellHit.HasValue)
            return;

        Editor.SetActiveTableCell(cellHit.Value.Row, cellHit.Value.Col);
        var menu = BuildTableContextMenu(shape);
        _slideCanvas.ContextMenu = menu;
        menu.Open(_slideCanvas);
        e.Handled = true;
    }

    private ContextMenu BuildChartContextMenu(ChartSubtargetHit hit)
    {
        var menu = new ContextMenu();
        void Add(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        switch (hit.Kind)
        {
            case ChartSubtargetKind.Point:
                Add("Format Data Point...", () => OpenChartPointOptionsDialog(hit.SeriesIndex, hit.PointIndex));
                Add("Format Data Series...", () => OpenChartSeriesOptionsDialog(hit.SeriesIndex));
                break;
            case ChartSubtargetKind.Series:
                Add("Format Data Series...", () => OpenChartSeriesOptionsDialog(hit.SeriesIndex));
                break;
            case ChartSubtargetKind.CategoryAxis:
                Add("Format Category Axis...", () => OpenChartAxisOptionsDialog(ChartAxisKind.Category));
                break;
            case ChartSubtargetKind.ValueAxis:
                Add("Format Value Axis...", () => OpenChartAxisOptionsDialog(ChartAxisKind.Value));
                break;
            case ChartSubtargetKind.AxisTitle:
                Add("Format Axis...", () => OpenChartAxisOptionsDialog(hit.AxisKind ?? ChartAxisKind.Value));
                break;
            case ChartSubtargetKind.Title:
            case ChartSubtargetKind.Legend:
                Add("Format Chart Text...", OpenChartTextOptionsDialog);
                break;
            case ChartSubtargetKind.PlotArea:
                Add("Format Plot Area...", () => OpenChartAreaOptionsDialog(ChartAreaFormattingTarget.PlotArea));
                break;
            default:
                Add("Format Chart Area...", OpenChartAreaOptionsDialog);
                break;
        }

        menu.Items.Add(new Separator());
        Add("Chart Options...", OpenChartDisplayOptionsDialog);
        return menu;
    }

    private ContextMenu BuildTableContextMenu(SlideShape shape)
    {
        var menu = new ContextMenu();

        void Add(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        Add("Insert Row Above", () => { Editor.Select(shape.Id); Editor.InsertRowAbove(); });
        Add("Insert Row Below", () => { Editor.Select(shape.Id); Editor.InsertRowBelow(); });
        menu.Items.Add(new Separator());
        Add("Insert Column Left", () => { Editor.Select(shape.Id); Editor.InsertColumnLeft(); });
        Add("Insert Column Right", () => { Editor.Select(shape.Id); Editor.InsertColumnRight(); });
        menu.Items.Add(new Separator());
        Add("Delete Row", () => { Editor.Select(shape.Id); Editor.DeleteRow(); });
        Add("Delete Column", () => { Editor.Select(shape.Id); Editor.DeleteColumn(); });
        menu.Items.Add(new Separator());

        var widthMenu = new MenuItem { Header = "Column Width" };
        foreach (var (label, inches) in new[]
        {
            ("0.75 in", 0.75),
            ("1.00 in", 1.00),
            ("1.25 in", 1.25),
            ("1.50 in", 1.50),
            ("2.00 in", 2.00),
        })
        {
            var widthItem = new MenuItem { Header = label };
            widthItem.Click += (_, _) =>
            {
                Editor.Select(shape.Id);
                Editor.TryApplyActiveTableColumnWidth(
                    (long)Math.Round(inches * DrawingMlCoordinateUnits.EmuPerInch));
            };
            widthMenu.Items.Add(widthItem);
        }
        menu.Items.Add(widthMenu);

        var table = shape.Table!;
        var activeCell = Editor.ActiveTableCell;
        var canMerge = activeCell.HasValue &&
            (activeCell.Value.Col + 1 < table.ColumnWidthsEmu.Count ||
             activeCell.Value.Row + 1 < table.Rows.Count);
        var canSplit = activeCell.HasValue &&
            table.Rows.Count > activeCell.Value.Row &&
            table.Rows[activeCell.Value.Row].Cells.ElementAtOrDefault(activeCell.Value.Col) is { } cell &&
            (cell.GridSpan > 1 || cell.RowSpan > 1);

        var mergeItem = new MenuItem { Header = "Merge with Right Cell", IsEnabled = canMerge };
        if (canMerge && activeCell is { } mergeCell)
        {
            var row = mergeCell.Row;
            var col = mergeCell.Col;
            var rightColumn = col + 1 < table.ColumnWidthsEmu.Count ? col + 1 : col;
            var belowRow = row + 1 < table.Rows.Count && rightColumn == col ? row + 1 : row;
            mergeItem.Click += (_, _) =>
            {
                Editor.Select(shape.Id);
                Editor.MergeTableCells(row, col, belowRow, rightColumn);
            };
        }
        menu.Items.Add(mergeItem);

        var splitItem = new MenuItem { Header = "Split Cell", IsEnabled = canSplit };
        if (canSplit && activeCell is { } splitCell)
        {
            splitItem.Click += (_, _) =>
            {
                Editor.Select(shape.Id);
                Editor.SplitTableCell(splitCell.Row, splitCell.Col);
            };
        }
        menu.Items.Add(splitItem);
        return menu;
    }

    internal ContextMenu? BuildTableContextMenuForTests(uint shapeId)
    {
        var shape = Editor.CurrentSlide is { } slide ? ShapeTreeLookup.Find(slide, shapeId) : null;
        return shape?.Kind == SlideShapeKind.Table && shape.Table is not null
            ? BuildTableContextMenu(shape)
            : null;
    }

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
            onFileTabSelected: ShowBackstage);

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
        var r = new RibbonCommandRegistry();

        // File operations
        r.Register("freep.file.new",     new ActionRibbonCommand(FileNew));
        r.Register("freep.file.open",    new ActionRibbonCommand(() => _ = FileOpenAsync()));
        r.Register("freep.file.save",    new ActionRibbonCommand(() => _ = FileSaveAsync()));
        r.Register("freep.file.save-as", new ActionRibbonCommand(() => _ = FileSaveAsAsync()));
        r.Register(PresentationExportPlanner.PdfExportCommandId, new ActionRibbonCommand(() => _ = FileExportPdfAsync()));
        r.Register(PresentationExportPlanner.NotesPagePdfExportCommandId, new ActionRibbonCommand(() => _ = FileExportNotesPagePdfAsync()));
        r.Register(PresentationExportPlanner.ImageExportCommandId, new ActionRibbonCommand(() => _ = FileExportImagesAsync()));
        r.Register(PresentationExportPlanner.PrintCommandId, new ActionRibbonCommand(() =>
        {
            RefreshHandoutLayoutPlan();
            ShowPrintBackstage();
        }));
        r.Register(PresentationExportPlanner.VideoExportCommandId, new ActionRibbonCommand(() => _ = FileExportVideoAsync()));
        

        // Slide navigation/management
        r.Register("freep.new-slide",       new ActionRibbonCommand(() => Editor.InsertSlide()));
        r.Register("freep.duplicate-slide", new ActionRibbonCommand(() => Editor.DuplicateCurrentSlide()));
        r.Register("freep.delete-slide",    new ActionRibbonCommand(() => Editor.DeleteCurrentSlide()));
        r.Register(SlideZoomInsertionPlanner.CommandId,
            new ActionRibbonCommand(() => _ = OpenSlideZoomDialogAsync()));
        r.Register(SectionZoomInsertionPlanner.CommandId,
            new ActionRibbonCommand(() => _ = OpenSectionZoomDialogAsync()));
        r.Register(SummaryZoomInsertionPlanner.CommandId,
            new ActionRibbonCommand(() => _ = OpenSummaryZoomDialogAsync()));
        r.Register(ZoomObjectPropertiesPlanner.CommandId,
            new ActionRibbonCommand(() => _ = OpenZoomObjectPropertiesDialogAsync()));
        r.Register(ZoomCoverImagePlanner.CommandId,
            new ActionRibbonCommand(() => _ = OpenZoomCoverImagePickerAsync()));
        r.Register(ZoomCoverImagePlanner.ResetCommandId,
            new ActionRibbonCommand(() => _ = RestoreZoomPreviewAsync()));
        r.Register(PresentationDesignCommandPlanner.LayoutCommandId, new ActionRibbonCommand(() =>
            PresentationDesignCommandPlanner.TryApply(
                Editor,
                PresentationDesignCommandPlanner.LayoutPlan,
                OnDesignHostRequest)));

        // Clipboard
        r.Register("freep.copy", new ActionRibbonCommand(() =>
            QueueClipboardCopy()));
        r.Register("freep.cut", new ActionRibbonCommand(() =>
            QueueClipboardCut()));
        r.Register("freep.paste", new ActionRibbonCommand(() =>
            QueueClipboardPaste()));
        r.Register("freep.format-painter", new ActionRibbonCommand(() =>
        {
            if (Editor.SelectedShapeIds.Count == 1 && _gestureHandler?.BeginFormatPainter() == true)
                return;

            // Preserve the existing one-click multi-selection behavior: the first selected
            // shape is the source and all other selected shapes are painted immediately.
            Editor.CopyFormatting();
            Editor.ApplyFormattingToSelection();
        }));

        // Font formatting
        r.Register("freep.font-family", new ContextRibbonCommand(ctx =>
        {
            if (string.IsNullOrEmpty(ctx.SelectedValue))
                return;

            if (_textEditor?.TryApplyActiveShapeFontFamily(ctx.SelectedValue) == true) return;
            if (_textEditor?.TryApplyActiveTableCellFontFamily(ctx.SelectedValue) == true) return;
            if (Editor.TryApplyActiveTableCellFontFamily(ctx.SelectedValue)) return;
            Editor.SetFontFamilyOnSelection(ctx.SelectedValue);
        }));
        r.Register("freep.font-size", new ContextRibbonCommand(ctx =>
        {
            if (!TryGetRibbonFontSize(ctx, out double sizePt))
                return;

            if (_textEditor?.TryApplyActiveShapeFontSize(sizePt) == true) return;
            if (_textEditor?.TryApplyActiveTableCellFontSize(sizePt) == true) return;
            if (Editor.TryApplyActiveTableCellFontSize(sizePt)) return;
            Editor.SetFontSizeOnSelection(sizePt);
        }));
        r.Register("freep.font-color", new ContextRibbonCommand(ctx =>
        {
            if (!TryGetRibbonFontColor(ctx, out var color))
                return;

            if (_textEditor?.TryApplyActiveShapeColor(color) == true) return;
            if (_textEditor?.TryApplyActiveTableCellColor(color) == true) return;
            if (Editor.TryApplyActiveTableCellColor(color)) return;
            Editor.SetColorOnSelection(color);
        }));
        r.Register("freep.text-autofit", new ContextRibbonCommand(ctx =>
        {
            if (!ctx.Parameters.TryGetValue(RibbonCommandContext.SelectedValueKey, out var value) ||
                value is not string selection ||
                !TextAutoFitOptionParser.TryParse(selection, out var kind))
                return;

            Editor.SetTextAutoFitOnSelection(kind);
        }));
        r.Register("freep.text-direction", new ContextRibbonCommand(ctx =>
        {
            if (!ctx.Parameters.TryGetValue(RibbonCommandContext.SelectedValueKey, out var value) ||
                value is not string selection ||
                !TextVerticalTypeOptionParser.TryParse(selection, out var verticalType))
                return;

            if (Editor.TryApplyActiveTableCellTextVerticalType(verticalType))
                return;
            Editor.SetTextVerticalTypeOnSelection(verticalType);
        }));
        r.Register("freep.text-columns", new ContextRibbonCommand(ctx =>
        {
            if (!ctx.Parameters.TryGetValue(RibbonCommandContext.SelectedValueKey, out var value) ||
                value is not string selection ||
                !TextColumnCountOptionParser.TryParse(selection, out var count))
                return;

            Editor.SetTextColumnCountOnSelection(count);
        }));
        r.Register("freep.text-column-spacing", new ContextRibbonCommand(ctx =>
        {
            if (!ctx.Parameters.TryGetValue(RibbonCommandContext.SelectedValueKey, out var value) ||
                value is not string selection ||
                !TextColumnSpacingOptionParser.TryParse(selection, out var spacingEmu))
                return;

            Editor.SetTextColumnSpacingOnSelection(spacingEmu);
        }));
        r.Register("freep.table-cell-fill", new ContextRibbonCommand(ctx =>
        {
            if (!TryGetRibbonFontColor(ctx, out var color))
                return;

            Editor.TryApplyActiveTableCellFill(color);
        }));
        r.Register("freep.table-cell-anchor", new ContextRibbonCommand(ctx =>
        {
            if (!TryGetRibbonTableCellAnchor(ctx, out var anchor))
                return;

            Editor.TryApplyActiveTableCellAnchor(anchor);
        }));
        r.Register("freep.table-cell-border", new ContextRibbonCommand(ctx =>
        {
            if (!ctx.Parameters.TryGetValue(RibbonCommandContext.SelectedValueKey, out var value) ||
                value is not string selection ||
                !TableCellBorderOptionParser.TryParse(selection, out var side, out var outline))
                return;

            Editor.TryApplyActiveTableCellBorder(side, outline);
        }));
        r.Register("freep.table-cell-inset", new ContextRibbonCommand(ctx =>
        {
            if (!ctx.Parameters.TryGetValue(RibbonCommandContext.SelectedValueKey, out var value) ||
                value is not string selection ||
                !TableCellInsetOptionParser.TryParse(selection, out var side, out var insetPt))
                return;

            Editor.TryApplyActiveTableCellInset(side, insetPt);
        }));
        r.Register("freep.table-row-height", new ContextRibbonCommand(ctx =>
        {
            if (!ctx.Parameters.TryGetValue(RibbonCommandContext.SelectedValueKey, out var value) ||
                value is not string selection ||
                !TableRowHeightOptionParser.TryParse(selection, out var heightEmu))
                return;

            Editor.TryApplyActiveTableRowHeight(heightEmu);
        }));
        r.Register(TableCellEditPlanner.MergeCellsCommandId,
            new ActionRibbonCommand(() => Editor.TryMergeActiveTableCell()));
        r.Register(TableCellEditPlanner.SplitCellCommandId,
            new ActionRibbonCommand(() => Editor.TrySplitActiveTableCell()));
        r.Register(TableCellEditPlanner.TableFirstRowCommandId,
            new ActionRibbonCommand(() => Editor.ToggleSelectedTableStyleFlag(TableStyleFlagKind.FirstRow)));
        r.Register(TableCellEditPlanner.TableLastRowCommandId,
            new ActionRibbonCommand(() => Editor.ToggleSelectedTableStyleFlag(TableStyleFlagKind.LastRow)));
        r.Register(TableCellEditPlanner.TableFirstColCommandId,
            new ActionRibbonCommand(() => Editor.ToggleSelectedTableStyleFlag(TableStyleFlagKind.FirstCol)));
        r.Register(TableCellEditPlanner.TableLastColCommandId,
            new ActionRibbonCommand(() => Editor.ToggleSelectedTableStyleFlag(TableStyleFlagKind.LastCol)));
        r.Register(TableCellEditPlanner.TableBandRowCommandId,
            new ActionRibbonCommand(() => Editor.ToggleSelectedTableStyleFlag(TableStyleFlagKind.BandRow)));
        r.Register(TableCellEditPlanner.TableBandColCommandId,
            new ActionRibbonCommand(() => Editor.ToggleSelectedTableStyleFlag(TableStyleFlagKind.BandCol)));
        r.Register("freep.bold", new ActionRibbonCommand(() =>
        {
            if (_textEditor?.TryApplyActiveShapeTextFormat(TableCellTextFormatKind.Bold) == true) return;
            if (_textEditor?.TryApplyActiveTableCellTextFormat(TableCellTextFormatKind.Bold) == true) return;
            if (Editor.ToggleBoldOnActiveTableCell()) return;
            Editor.ToggleBoldOnSelection();
        }));
        r.Register("freep.italic", new ActionRibbonCommand(() =>
        {
            if (_textEditor?.TryApplyActiveShapeTextFormat(TableCellTextFormatKind.Italic) == true) return;
            if (_textEditor?.TryApplyActiveTableCellTextFormat(TableCellTextFormatKind.Italic) == true) return;
            if (Editor.ToggleItalicOnActiveTableCell()) return;
            Editor.ToggleItalicOnSelection();
        }));
        r.Register("freep.underline", new ActionRibbonCommand(() =>
        {
            if (_textEditor?.TryApplyActiveShapeTextFormat(TableCellTextFormatKind.Underline) == true) return;
            if (_textEditor?.TryApplyActiveTableCellTextFormat(TableCellTextFormatKind.Underline) == true) return;
            if (Editor.ToggleUnderlineOnActiveTableCell()) return;
            Editor.ToggleUnderlineOnSelection();
        }));
        r.Register("freep.superscript", new ActionRibbonCommand(() =>
        {
            if (_textEditor?.TryApplyActiveShapeTextFormat(TableCellTextFormatKind.Superscript) == true) return;
            if (_textEditor?.TryApplyActiveTableCellTextFormat(TableCellTextFormatKind.Superscript) == true) return;
            if (Editor.ToggleSuperscriptOnActiveTableCell()) return;
            Editor.ToggleSuperscriptOnSelection();
        }));
        r.Register("freep.subscript", new ActionRibbonCommand(() =>
        {
            if (_textEditor?.TryApplyActiveShapeTextFormat(TableCellTextFormatKind.Subscript) == true) return;
            if (_textEditor?.TryApplyActiveTableCellTextFormat(TableCellTextFormatKind.Subscript) == true) return;
            if (Editor.ToggleSubscriptOnActiveTableCell()) return;
            Editor.ToggleSubscriptOnSelection();
        }));
        r.Register("freep.paragraph.align-left", new ActionRibbonCommand(() =>
        {
            if (_textEditor?.TryApplyActiveShapeParagraphAlignment(TextAlign.Left) == true) return;
            if (_textEditor?.TryApplyActiveTableCellParagraphAlignment(TextAlign.Left) == true) return;
            Editor.TryApplyActiveTableCellParagraphAlignment(TextAlign.Left);
        }));
        r.Register("freep.paragraph.align-center", new ActionRibbonCommand(() =>
        {
            if (_textEditor?.TryApplyActiveShapeParagraphAlignment(TextAlign.Center) == true) return;
            if (_textEditor?.TryApplyActiveTableCellParagraphAlignment(TextAlign.Center) == true) return;
            Editor.TryApplyActiveTableCellParagraphAlignment(TextAlign.Center);
        }));
        r.Register("freep.paragraph.align-right", new ActionRibbonCommand(() =>
        {
            if (_textEditor?.TryApplyActiveShapeParagraphAlignment(TextAlign.Right) == true) return;
            if (_textEditor?.TryApplyActiveTableCellParagraphAlignment(TextAlign.Right) == true) return;
            Editor.TryApplyActiveTableCellParagraphAlignment(TextAlign.Right);
        }));
        r.Register("freep.paragraph.align-justify", new ActionRibbonCommand(() =>
        {
            if (_textEditor?.TryApplyActiveShapeParagraphAlignment(TextAlign.Justify) == true) return;
            if (_textEditor?.TryApplyActiveTableCellParagraphAlignment(TextAlign.Justify) == true) return;
            Editor.TryApplyActiveTableCellParagraphAlignment(TextAlign.Justify);
        }));
        r.Register("freep.bullets", new ContextRibbonCommand(ctx =>
        {
            if (TableCellListPresetCatalog.TryGet(ctx.SelectedValue, out var bulletPreset) &&
                bulletPreset is not null)
            {
                if (_textEditor?.TryApplyActiveShapeParagraphListPreset(bulletPreset) == true) return;
                if (_textEditor?.TryApplyActiveTableCellParagraphListPreset(bulletPreset) == true) return;
                Editor.TryApplyActiveTableCellParagraphListPreset(bulletPreset);
                return;
            }

            if (_textEditor?.TryApplyActiveShapeParagraphBulletToggle() == true) return;
            if (_textEditor?.TryApplyActiveTableCellParagraphBulletToggle() == true) return;
            Editor.TryApplyActiveTableCellParagraphBulletToggle();
        }));
        r.Register("freep.numbering", new ContextRibbonCommand(ctx =>
        {
            if (TableCellListPresetCatalog.TryGet(ctx.SelectedValue, out var numberingPreset) &&
                numberingPreset is not null)
            {
                if (_textEditor?.TryApplyActiveShapeParagraphListPreset(numberingPreset) == true) return;
                if (_textEditor?.TryApplyActiveTableCellParagraphListPreset(numberingPreset) == true) return;
                Editor.TryApplyActiveTableCellParagraphListPreset(numberingPreset);
                return;
            }

            if (_textEditor?.TryApplyActiveShapeParagraphNumberingToggle() == true) return;
            if (_textEditor?.TryApplyActiveTableCellParagraphNumberingToggle() == true) return;
            Editor.TryApplyActiveTableCellParagraphNumberingToggle();
        }));
        RegisterListGalleryPresetCommands(r);
        r.Register("freep.indent-increase", new ActionRibbonCommand(() =>
        {
            if (_textEditor?.TryApplyActiveShapeParagraphIndent() == true) return;
            if (_textEditor?.TryApplyActiveTableCellParagraphIndent() == true) return;
            Editor.TryApplyActiveTableCellParagraphIndent();
        }));
        r.Register("freep.indent-decrease", new ActionRibbonCommand(() =>
        {
            if (_textEditor?.TryApplyActiveShapeParagraphOutdent() == true) return;
            if (_textEditor?.TryApplyActiveTableCellParagraphOutdent() == true) return;
            Editor.TryApplyActiveTableCellParagraphOutdent();
        }));
        r.Register("freep.increase-indent", new ActionRibbonCommand(() =>
        {
            if (_textEditor?.TryApplyActiveShapeParagraphIndent() == true) return;
            if (_textEditor?.TryApplyActiveTableCellParagraphIndent() == true) return;
            Editor.TryApplyActiveTableCellParagraphIndent();
        }));
        r.Register("freep.decrease-indent", new ActionRibbonCommand(() =>
        {
            if (_textEditor?.TryApplyActiveShapeParagraphOutdent() == true) return;
            if (_textEditor?.TryApplyActiveTableCellParagraphOutdent() == true) return;
            Editor.TryApplyActiveTableCellParagraphOutdent();
        }));

        foreach (var route in ArrangeCommandRoutes)
        {
            r.Register(route.CommandId, new ActionRibbonCommand(() => route.Execute(Editor)));
        }
        r.Register(RotationOptionsPlanner.CommandId,
            new ActionRibbonCommand(OpenRotationOptionsDialog));
        r.Register(OleActivationPlanner.OpenEmbeddedObjectCommandId,
            new ActionRibbonCommand(() =>
            {
                OleActivationPlanner.TryOpenInlineFirst(
                    () => _textEditor?.TryActivateInlineOleObject() == true,
                    () =>
                    {
                        if (Editor.SelectedOleObject is not { } ole)
                            return false;
                        OleActivationService.TryActivate(ole);
                        return true;
                    });
            }));
        r.Register(OleInsertionPlanner.InsertEmbeddedObjectCommandId,
            new ActionRibbonCommand(() => _ = InsertEmbeddedObjectFromFileAsync()));
        r.Register("freep.arrange.edit-points", new EditPointsToggleCommand(_slideCanvas));

        // Insert objects/text
        foreach (var plan in SlideObjectInsertionPlanner.BuiltInPlans)
        {
            if (plan.CommandId == SlideObjectInsertionPlanner.Table3x3CommandId)
            {
                r.Register(plan.CommandId, new ActionRibbonCommand(OpenTablePicker));
                continue;
            }

            if (plan.RequiresPicturePayload)
            {
                r.Register(plan.CommandId, new ActionRibbonCommand(() => _ = InsertPictureFromFileAsync()));
                continue;
            }

            if (plan.RequiresMediaPayload)
            {
                var isVideo = plan.CommandId == SlideObjectInsertionPlanner.VideoCommandId;
                r.Register(plan.CommandId, new ActionRibbonCommand(() => _ = InsertMediaFromFileAsync(isVideo)));
                continue;
            }

            r.Register(plan.CommandId, new ActionRibbonCommand(() =>
                SlideObjectInsertionPlanner.Apply(Editor, plan)));
        }

        r.Register(
            PictureCropAuthoringPlanner.InsetCommandId,
            new ActionRibbonCommand(() => Editor.SetSelectedPictureCrop(PictureCropAuthoringPlanner.Inset())));
        r.Register(
            PictureCropAuthoringPlanner.ResetCommandId,
            new ActionRibbonCommand(() => Editor.SetSelectedPictureCrop(PictureCropAuthoringPlanner.Reset())));
        r.Register(
            PictureColorEffectAuthoringPlanner.GrayscaleCommandId,
            new ActionRibbonCommand(() => Editor.SetSelectedPictureColorEffects(PictureColorEffectAuthoringPlanner.Grayscale())));
        r.Register(
            PictureColorEffectAuthoringPlanner.ResetCommandId,
            new ActionRibbonCommand(() => Editor.SetSelectedPictureColorEffects(PictureColorEffectAuthoringPlanner.Reset())));
        r.Register(
            ShapeEffectAuthoringPlanner.NoneCommandId,
            new ActionRibbonCommand(() => Editor.SetSelectedShapeShadow(ShapeEffectAuthoringPlanner.None())));
        r.Register(
            ShapeEffectAuthoringPlanner.SubtleCommandId,
            new ActionRibbonCommand(() => Editor.SetSelectedShapeShadow(ShapeEffectAuthoringPlanner.Subtle())));
        r.Register(
            ShapeEffectAuthoringPlanner.OffsetCommandId,
            new ActionRibbonCommand(() => Editor.SetSelectedShapeShadow(ShapeEffectAuthoringPlanner.Offset())));
        r.Register(
            ShapeEffectAuthoringPlanner.GlowNoneCommandId,
            new ActionRibbonCommand(() => Editor.SetSelectedShapeGlow(ShapeEffectAuthoringPlanner.GlowNone())));
        r.Register(
            ShapeEffectAuthoringPlanner.GlowSubtleCommandId,
            new ActionRibbonCommand(() => Editor.SetSelectedShapeGlow(ShapeEffectAuthoringPlanner.GlowSubtle())));
        r.Register(
            ShapeEffectAuthoringPlanner.GlowStrongCommandId,
            new ActionRibbonCommand(() => Editor.SetSelectedShapeGlow(ShapeEffectAuthoringPlanner.GlowStrong())));
        r.Register(
            ShapeEffectAuthoringPlanner.SoftEdgeNoneCommandId,
            new ActionRibbonCommand(() => Editor.SetSelectedShapeSoftEdge(ShapeEffectAuthoringPlanner.SoftEdgeNone())));
        r.Register(
            ShapeEffectAuthoringPlanner.SoftEdgeSubtleCommandId,
            new ActionRibbonCommand(() => Editor.SetSelectedShapeSoftEdge(ShapeEffectAuthoringPlanner.SoftEdgeSubtle())));
        r.Register(
            ShapeEffectAuthoringPlanner.SoftEdgeStrongCommandId,
            new ActionRibbonCommand(() => Editor.SetSelectedShapeSoftEdge(ShapeEffectAuthoringPlanner.SoftEdgeStrong())));
        r.Register(
            ShapeEffectAuthoringPlanner.BevelNoneCommandId,
            new ActionRibbonCommand(() => Editor.SetSelectedShapeBevel(ShapeEffectAuthoringPlanner.BevelNone())));
        r.Register(
            ShapeEffectAuthoringPlanner.BevelSubtleCommandId,
            new ActionRibbonCommand(() => Editor.SetSelectedShapeBevel(ShapeEffectAuthoringPlanner.BevelSubtle())));
        r.Register(
            ShapeEffectAuthoringPlanner.BevelStrongCommandId,
            new ActionRibbonCommand(() => Editor.SetSelectedShapeBevel(ShapeEffectAuthoringPlanner.BevelStrong())));
        r.Register(
            ShapeEffectAuthoringPlanner.Shape3dNoneCommandId,
            new ActionRibbonCommand(() => Editor.SetSelectedShape3d(ShapeEffectAuthoringPlanner.Shape3dNone())));
        r.Register(
            ShapeEffectAuthoringPlanner.Shape3dSubtleCommandId,
            new ActionRibbonCommand(() => Editor.SetSelectedShape3d(ShapeEffectAuthoringPlanner.Shape3dSubtle())));
        r.Register(
            ShapeEffectAuthoringPlanner.Shape3dStrongCommandId,
            new ActionRibbonCommand(() => Editor.SetSelectedShape3d(ShapeEffectAuthoringPlanner.Shape3dStrong())));

        r.Register(ChartDataDialogPlanner.EditDataCommandId, new ActionRibbonCommand(OpenChartDataDialog));
        r.Register(ChartDataDialogPlanner.ChangeChartTypeCommandId, new ActionRibbonCommand(OpenChartDataDialog));
        foreach (var option in ChartDataDialogPlanner.ChartTypeOptions)
        {
            var chartType = option.Value;
            r.Register(
                ChartDataDialogPlanner.ChangeChartTypeOptionCommandId(chartType),
                new ActionRibbonCommand(() => Editor.ChangeSelectedChartType(chartType)));
        }
        r.Register(ChartDisplayOptionsPlanner.CommandId, new ActionRibbonCommand(OpenChartDisplayOptionsDialog));
        r.Register(ChartAxisOptionsPlanner.CommandId, new ActionRibbonCommand(OpenChartAxisOptionsDialog));
        r.Register(ChartSeriesOptionsPlanner.CommandId, new ActionRibbonCommand(OpenChartSeriesOptionsDialog));
        r.Register(
            ChartPointOptionsPlanner.CommandId,
            new ActionRibbonCommand(() => OpenChartPointOptionsDialog()));
        r.Register(ChartLayoutOptionsPlanner.CommandId, new ActionRibbonCommand(OpenChartLayoutOptionsDialog));
        r.Register(ChartDataTableOptionsPlanner.CommandId, new ActionRibbonCommand(OpenChartDataTableOptionsDialog));
        r.Register(ChartBubbleOptionsPlanner.CommandId, new ActionRibbonCommand(OpenChartBubbleOptionsDialog));
        r.Register(ChartPieOptionsPlanner.CommandId, new ActionRibbonCommand(OpenChartPieOptionsDialog));
        r.Register(ChartPlotStyleOptionsPlanner.CommandId, new ActionRibbonCommand(OpenChartPlotStyleOptionsDialog));
        r.Register(Chart3DViewOptionsPlanner.CommandId, new ActionRibbonCommand(OpenChart3DViewOptionsDialog));
        r.Register(ChartTextOptionsPlanner.CommandId, new ActionRibbonCommand(OpenChartTextOptionsDialog));
        r.Register(ChartAreaOptionsPlanner.CommandId, new ActionRibbonCommand(OpenChartAreaOptionsDialog));
        r.Register(ChartProtectionOptionsPlanner.CommandId, new ActionRibbonCommand(OpenChartProtectionOptionsDialog));
        r.Register(ShapeTransparencyPlanner.FillCommandId, new ActionRibbonCommand(() => Editor.SetSelectedFillTransparency(0)));
        r.Register(ShapeTransparencyPlanner.OutlineCommandId, new ActionRibbonCommand(() => Editor.SetSelectedOutlineTransparency(0)));
        foreach (var option in ShapeTransparencyPlanner.Options)
        {
            r.Register(
                ShapeTransparencyPlanner.OptionCommandId(ShapeTransparencyTarget.Fill, option.Percent),
                new ActionRibbonCommand(() => Editor.SetSelectedFillTransparency(option.Percent)));
            r.Register(
                ShapeTransparencyPlanner.OptionCommandId(ShapeTransparencyTarget.Outline, option.Percent),
                new ActionRibbonCommand(() => Editor.SetSelectedOutlineTransparency(option.Percent)));
        }
        r.Register("freep.insert-link", new ActionRibbonCommand(OpenHyperlinkDialog));
        r.Register("freep.remove-link", new ActionRibbonCommand(() =>
        {
            if (_textEditor?.TryApplySelectedShapeRunHyperlink(null) == true)
                return;
            Editor.RemoveShapeHyperlink();
        }));
        r.Register(HeaderFooterCommandPlanner.HeaderFooterCommandId,
            new ActionRibbonCommand(() => OpenHeaderFooterDialog(HeaderFooterCommandFocus.HeaderFooter)));
        r.Register(HeaderFooterCommandPlanner.DateTimeCommandId,
            new ActionRibbonCommand(() => OpenHeaderFooterDialog(HeaderFooterCommandFocus.DateTime)));
        r.Register(HeaderFooterCommandPlanner.SlideNumberCommandId,
            new ActionRibbonCommand(() => OpenHeaderFooterDialog(HeaderFooterCommandFocus.SlideNumber)));
        r.Register(SmartArtAuthoringPlanner.ThemeAccentsCommandId,
            new ActionRibbonCommand(() => ApplySmartArtColorPreset(SmartArtColorPreset.ThemeAccents)));
        r.Register(SmartArtAuthoringPlanner.SingleAccentCommandId,
            new ActionRibbonCommand(() => ApplySmartArtColorPreset(SmartArtColorPreset.SingleAccent)));
        r.Register(SmartArtAuthoringPlanner.MonochromaticAccent2CommandId,
            new ActionRibbonCommand(() => ApplySmartArtColorPreset(SmartArtColorPreset.MonochromaticAccent2)));
        r.Register(SmartArtAuthoringPlanner.MonochromaticAccent3CommandId,
            new ActionRibbonCommand(() => ApplySmartArtColorPreset(SmartArtColorPreset.MonochromaticAccent3)));
        r.Register(SmartArtAuthoringPlanner.MonochromaticAccent4CommandId,
            new ActionRibbonCommand(() => ApplySmartArtColorPreset(SmartArtColorPreset.MonochromaticAccent4)));
        r.Register(SmartArtAuthoringPlanner.MonochromaticAccent5CommandId,
            new ActionRibbonCommand(() => ApplySmartArtColorPreset(SmartArtColorPreset.MonochromaticAccent5)));
        r.Register(SmartArtAuthoringPlanner.MonochromaticAccent6CommandId,
            new ActionRibbonCommand(() => ApplySmartArtColorPreset(SmartArtColorPreset.MonochromaticAccent6)));
        r.Register(SmartArtAuthoringPlanner.GrayscaleCommandId,
            new ActionRibbonCommand(() => ApplySmartArtColorPreset(SmartArtColorPreset.Grayscale)));
        foreach (var entry in SmartArtAuthoringPlanner.ColorGallery)
        {
            var preset = entry.Preset;
            r.Register(entry.CommandId,
                new ActionRibbonCommand(() => ApplySmartArtColorPreset(preset)));
        }
        r.Register(SmartArtAuthoringPlanner.BasicProcessLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.BasicProcess)));
        r.Register(SmartArtAuthoringPlanner.AccentProcessLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.AccentProcess)));
        r.Register(SmartArtAuthoringPlanner.AscendingProcessLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.AscendingProcess)));
        r.Register(SmartArtAuthoringPlanner.DescendingProcessLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.DescendingProcess)));
        r.Register(SmartArtAuthoringPlanner.BasicTimelineLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.BasicTimeline)));
        r.Register(SmartArtAuthoringPlanner.CircleAccentTimelineLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.CircleAccentTimeline)));
        r.Register(SmartArtAuthoringPlanner.PhasedProcessLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.PhasedProcess)));
        r.Register(SmartArtAuthoringPlanner.StepDownProcessLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.StepDownProcess)));
        r.Register(SmartArtAuthoringPlanner.ContinuousBlockProcessLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.ContinuousBlockProcess)));
        r.Register(SmartArtAuthoringPlanner.SegmentedProcessLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.SegmentedProcess)));
        r.Register(SmartArtAuthoringPlanner.ChevronProcessLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.ChevronProcess)));
        r.Register(SmartArtAuthoringPlanner.BasicChevronProcessLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.BasicChevronProcess)));
        r.Register(SmartArtAuthoringPlanner.ClosedChevronProcessLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.ClosedChevronProcess)));
        r.Register(SmartArtAuthoringPlanner.BendingProcessLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.BendingProcess)));
        r.Register(SmartArtAuthoringPlanner.AlternatingProcessLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.AlternatingProcess)));
        r.Register(SmartArtAuthoringPlanner.ArrowRibbonLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.ArrowRibbon)));
        r.Register(SmartArtAuthoringPlanner.CircleProcessLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.CircleProcess)));
        r.Register(SmartArtAuthoringPlanner.CircleArrowProcessLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.CircleArrowProcess)));
        r.Register(SmartArtAuthoringPlanner.IncreasingCircleProcessLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.IncreasingCircleProcess)));
        r.Register(SmartArtAuthoringPlanner.FunnelProcessLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.FunnelProcess)));
        r.Register(SmartArtAuthoringPlanner.VerticalProcessLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.VerticalProcess)));
        r.Register(SmartArtAuthoringPlanner.VerticalBoxListLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.VerticalBoxList)));
        r.Register(SmartArtAuthoringPlanner.VerticalBlockListLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.VerticalBlockList)));
        r.Register(SmartArtAuthoringPlanner.VerticalChevronListLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.VerticalChevronList)));
        r.Register(SmartArtAuthoringPlanner.VerticalArrowListLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.VerticalArrowList)));
        r.Register(SmartArtAuthoringPlanner.VerticalBulletListLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.VerticalBulletList)));
        r.Register(SmartArtAuthoringPlanner.HorizontalBulletListLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.HorizontalBulletList)));
        r.Register(SmartArtAuthoringPlanner.HorizontalBlockListLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.HorizontalBlockList)));
        r.Register(SmartArtAuthoringPlanner.TrapezoidListLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.TrapezoidList)));
        r.Register(SmartArtAuthoringPlanner.GroupedListLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.GroupedList)));
        r.Register(SmartArtAuthoringPlanner.BasicCycleLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.BasicCycle)));
        r.Register(SmartArtAuthoringPlanner.MultidirectionalCycleLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.MultidirectionalCycle)));
        r.Register(SmartArtAuthoringPlanner.Cycle2LayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.Cycle2)));
        r.Register(SmartArtAuthoringPlanner.ContinuousCycleLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.ContinuousCycle)));
        r.Register(SmartArtAuthoringPlanner.GearCycleLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.GearCycle)));
        r.Register(SmartArtAuthoringPlanner.TextCycleLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.TextCycle)));
        r.Register(SmartArtAuthoringPlanner.BlockCycleLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.BlockCycle)));
        r.Register(SmartArtAuthoringPlanner.NonDirectionalCycleLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.NonDirectionalCycle)));
        r.Register(SmartArtAuthoringPlanner.BasicBlockListLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.BasicBlockList)));
        r.Register(SmartArtAuthoringPlanner.BasicListLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.BasicList)));
        r.Register(SmartArtAuthoringPlanner.List2LayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.List2)));
        r.Register(SmartArtAuthoringPlanner.StackedListLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.StackedList)));
        r.Register(SmartArtAuthoringPlanner.DescendingBlockListLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.DescendingBlockList)));
        r.Register(SmartArtAuthoringPlanner.BasicPyramidLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.BasicPyramid)));
        r.Register(SmartArtAuthoringPlanner.PyramidListLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.PyramidList)));
        r.Register(SmartArtAuthoringPlanner.InvertedPyramidLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.InvertedPyramid)));
        r.Register(SmartArtAuthoringPlanner.RadialCycleLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.RadialCycle)));
        r.Register(SmartArtAuthoringPlanner.BasicRadialLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.BasicRadial)));
        r.Register(SmartArtAuthoringPlanner.RadialClusterLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.RadialCluster)));
        r.Register(SmartArtAuthoringPlanner.RadialListLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.RadialList)));
        r.Register(SmartArtAuthoringPlanner.BasicMatrixLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.BasicMatrix)));
        r.Register(SmartArtAuthoringPlanner.TitledMatrixLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.TitledMatrix)));
        r.Register(SmartArtAuthoringPlanner.GridMatrixLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.GridMatrix)));
        r.Register(SmartArtAuthoringPlanner.BasicRelationshipLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.BasicRelationship)));
        r.Register(SmartArtAuthoringPlanner.OpposingIdeasLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.OpposingIdeas)));
        r.Register(SmartArtAuthoringPlanner.ConvergingRadialLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.ConvergingRadial)));
        r.Register(SmartArtAuthoringPlanner.DivergingRadialLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.DivergingRadial)));
        r.Register(SmartArtAuthoringPlanner.BasicVennLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.BasicVenn)));
        r.Register(SmartArtAuthoringPlanner.RadialVennLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.RadialVenn)));
        r.Register(SmartArtAuthoringPlanner.TargetListLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.TargetList)));
        r.Register(SmartArtAuthoringPlanner.StackedVennLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.StackedVenn)));
        r.Register(SmartArtAuthoringPlanner.InterlockingRingsLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.InterlockingRings)));
        r.Register(SmartArtAuthoringPlanner.BasicHierarchyLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.BasicHierarchy)));
        r.Register(SmartArtAuthoringPlanner.Hierarchy3LayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.Hierarchy3)));
        r.Register(SmartArtAuthoringPlanner.HorizontalHierarchyLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.HorizontalHierarchy)));
        r.Register(SmartArtAuthoringPlanner.OrgChartLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.OrgChart)));
        r.Register(SmartArtAuthoringPlanner.NameAndTitleOrgChartLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.NameAndTitleOrgChart)));
        r.Register(SmartArtAuthoringPlanner.PictureCaptionListLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.PictureCaptionList)));
        r.Register(SmartArtAuthoringPlanner.PictureAccentListLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.PictureAccentList)));
        r.Register(SmartArtAuthoringPlanner.PictureStackLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.PictureStack)));
        r.Register(SmartArtAuthoringPlanner.PictureLineupLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.PictureLineup)));
        r.Register(SmartArtAuthoringPlanner.PictureStripsLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.PictureStrips)));
        r.Register(SmartArtAuthoringPlanner.ContinuousPictureListLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.ContinuousPictureList)));
        r.Register(SmartArtAuthoringPlanner.PictureGridLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.PictureGrid)));
        r.Register(SmartArtAuthoringPlanner.PictureAccentProcessLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.PictureAccentProcess)));
        r.Register(SmartArtAuthoringPlanner.LabeledHierarchyLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.LabeledHierarchy)));
        r.Register(SmartArtAuthoringPlanner.TableHierarchyLayoutCommandId,
            new ActionRibbonCommand(() => ApplySmartArtLayoutPreset(SmartArtLayoutPreset.TableHierarchy)));
        r.Register(SmartArtAuthoringPlanner.SimpleQuickStyleCommandId,
            new ActionRibbonCommand(() => ApplySmartArtQuickStylePreset(SmartArtQuickStylePreset.Simple)));
        r.Register(SmartArtAuthoringPlanner.ModerateQuickStyleCommandId,
            new ActionRibbonCommand(() => ApplySmartArtQuickStylePreset(SmartArtQuickStylePreset.Moderate)));
        r.Register(SmartArtAuthoringPlanner.IntenseQuickStyleCommandId,
            new ActionRibbonCommand(() => ApplySmartArtQuickStylePreset(SmartArtQuickStylePreset.Intense)));
        r.Register(SmartArtAuthoringPlanner.SubtleQuickStyleCommandId,
            new ActionRibbonCommand(() => ApplySmartArtQuickStylePreset(SmartArtQuickStylePreset.Subtle)));
        r.Register(SmartArtAuthoringPlanner.SoftEdgeQuickStyleCommandId,
            new ActionRibbonCommand(() => ApplySmartArtQuickStylePreset(SmartArtQuickStylePreset.SoftEdge)));
        r.Register(SmartArtAuthoringPlanner.InsertQuickStyleCommandId,
            new ActionRibbonCommand(() => ApplySmartArtQuickStylePreset(SmartArtQuickStylePreset.Insert)));
        r.Register(SmartArtAuthoringPlanner.CartoonQuickStyleCommandId,
            new ActionRibbonCommand(() => ApplySmartArtQuickStylePreset(SmartArtQuickStylePreset.Cartoon)));
        r.Register(SmartArtAuthoringPlanner.PowderQuickStyleCommandId,
            new ActionRibbonCommand(() => ApplySmartArtQuickStylePreset(SmartArtQuickStylePreset.Powder)));
        r.Register(SmartArtAuthoringPlanner.PolishedQuickStyleCommandId,
            new ActionRibbonCommand(() => ApplySmartArtQuickStylePreset(SmartArtQuickStylePreset.Polished)));
        r.Register(SmartArtAuthoringPlanner.BrickSceneQuickStyleCommandId,
            new ActionRibbonCommand(() => ApplySmartArtQuickStylePreset(SmartArtQuickStylePreset.BrickScene)));
        r.Register(SmartArtAuthoringPlanner.FlatSceneQuickStyleCommandId,
            new ActionRibbonCommand(() => ApplySmartArtQuickStylePreset(SmartArtQuickStylePreset.FlatScene)));
        r.Register(SmartArtAuthoringPlanner.MetallicSceneQuickStyleCommandId,
            new ActionRibbonCommand(() => ApplySmartArtQuickStylePreset(SmartArtQuickStylePreset.MetallicScene)));
        r.Register(SmartArtAuthoringPlanner.SunsetSceneQuickStyleCommandId,
            new ActionRibbonCommand(() => ApplySmartArtQuickStylePreset(SmartArtQuickStylePreset.SunsetScene)));
        r.Register(SmartArtAuthoringPlanner.BirdsEyeSceneQuickStyleCommandId,
            new ActionRibbonCommand(() => ApplySmartArtQuickStylePreset(SmartArtQuickStylePreset.BirdsEyeScene)));
        r.Register(SmartArtAuthoringPlanner.ConvertToShapesCommandId,
            new ActionRibbonCommand(() =>
            {
                if (Editor.SelectedShapeIds.Count == 1)
                    Editor.ConvertSmartArtToShapes(Editor.SelectedShapeIds[0]);
            }));

        // Undo / Redo
        r.Register("freep.undo", new ActionRibbonCommand(() => Editor.Undo()));
        r.Register("freep.redo", new ActionRibbonCommand(() => Editor.Redo()));
        r.Register("freep.find", new ActionRibbonCommand(OpenFindDialog));
        r.Register("freep.replace", new ActionRibbonCommand(OpenFindReplaceDialog));
        RegisterReviewWorkflowCommands(r);
        RegisterViewShowCommands(r);
        RegisterViewZoomCommands(r);

        foreach (var plan in PresentationTransitionCommandPlanner.BuiltInPlans)
        {
            r.Register(
                plan.CommandId,
                plan.Intent == PresentationTransitionCommandIntentKind.ToggleAdvanceOnClick
                    ? new TransitionAdvanceOnClickToggleCommand(Editor, plan)
                    : new ContextRibbonCommand(ctx =>
                        PresentationTransitionCommandPlanner.TryApply(
                            Editor,
                            plan,
                            ctx.SelectedValue,
                            () => _ = PickTransitionSoundAsync())));
        }

        foreach (var plan in PresentationDesignCommandPlanner.BuiltInPlans)
        {
            r.Register(plan.CommandId, new ActionRibbonCommand(() =>
                PresentationDesignCommandPlanner.TryApply(Editor, plan, OnDesignHostRequest)));
        }

        foreach (var plan in PresentationAnimationCommandPlanner.BuiltInPlans)
        {
            r.Register(
                plan.CommandId,
                plan.Intent == PresentationAnimationCommandIntentKind.TogglePane
                    ? new AnimationPaneToggleCommand(
                        Editor,
                        plan,
                        () => IsAnimationPaneVisible,
                        OnAnimationPaneRequested)
                    : new ContextRibbonCommand(ctx =>
                        PresentationAnimationCommandPlanner.TryApply(
                            Editor,
                            plan,
                            ctx.SelectedValue,
                            OnAnimationPaneRequested)));
        }

        // Slide show
        r.Register("freep.slideshow.from-beginning",
            new ActionRibbonCommand(() => StartSlideShow(fromStart: true)));
        r.Register("freep.slideshow.from-current-slide",
            new ActionRibbonCommand(() => StartSlideShow(fromStart: false)));
        r.Register("freep.slideshow.rehearse-timings",
            new ActionRibbonCommand(() => StartSlideShowWithTiming(
                FreeP.App.Compositor.SlideShowTimingIntent.RehearseTimings)));
        r.Register("freep.slideshow.record-timings",
            new ActionRibbonCommand(() => StartSlideShowWithTiming(
                FreeP.App.Compositor.SlideShowTimingIntent.RecordTimings)));
        r.Register("freep.slideshow.custom-shows",
            new ActionRibbonCommand(OpenCustomShowDialog));

        return r;
    }

    private void RegisterListGalleryPresetCommands(RibbonCommandRegistry registry)
    {
        foreach (var item in PresentationListGalleryPlanner.BuildPlans().SelectMany(plan => plan.Items))
        {
            if (!item.IsEnabled || item.ListPreset is null)
                continue;

            registry.Register(item.CommandId, new ActionRibbonCommand(() =>
            {
                if (_textEditor?.TryApplyActiveShapeParagraphListPreset(item.ListPreset) == true) return;
                if (_textEditor?.TryApplyActiveTableCellParagraphListPreset(item.ListPreset) == true) return;
                Editor.TryApplyActiveTableCellParagraphListPreset(item.ListPreset);
            }));
        }

        registry.Register(
            PresentationListGalleryPlanner.ImageBulletCommandId,
            new ActionRibbonCommand(() => _ = ApplyPictureBulletFromFileAsync()));
    }

    private static bool TryGetRibbonFontSize(RibbonCommandContext ctx, out double sizePt)
    {
        sizePt = 0;
        if (!ctx.Parameters.TryGetValue(RibbonCommandContext.SelectedValueKey, out var value))
            return false;

        switch (value)
        {
            case double d:
                sizePt = d;
                break;
            case float f:
                sizePt = f;
                break;
            case int i:
                sizePt = i;
                break;
            case decimal m:
                sizePt = (double)m;
                break;
            case string s:
                var text = s.Trim();
                if (text.EndsWith("pt", StringComparison.OrdinalIgnoreCase))
                    text = text[..^2].Trim();
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out sizePt))
                    return false;
                break;
            default:
                return false;
        }

        return sizePt > 0 && !double.IsNaN(sizePt) && !double.IsInfinity(sizePt);
    }

    private static bool TryGetRibbonFontColor(RibbonCommandContext ctx, out ThemeAwareColor? color)
    {
        color = null;
        if (!ctx.Parameters.TryGetValue(RibbonCommandContext.SelectedValueKey, out var value))
            return false;

        switch (value)
        {
            case ThemeAwareColor themeColor:
                color = themeColor;
                return true;
            case SrgbColor srgb:
                color = new ThemeAwareColor(srgb);
                return true;
            case string s:
                return TryParseRibbonFontColor(s, out color);
            default:
                return false;
        }
    }

    private static bool TryGetRibbonTableCellAnchor(RibbonCommandContext ctx, out TableCellAnchor? anchor)
    {
        anchor = null;
        if (!ctx.Parameters.TryGetValue(RibbonCommandContext.SelectedValueKey, out var value))
            return false;

        switch (value)
        {
            case TableCellAnchor cellAnchor:
                anchor = cellAnchor;
                return true;
            case string s:
                return TryParseRibbonTableCellAnchor(s, out anchor);
            default:
                return false;
        }
    }

    private static bool TryParseRibbonTableCellAnchor(string? value, out TableCellAnchor? anchor)
    {
        anchor = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        switch (value.Trim().ToLowerInvariant())
        {
            case "automatic":
            case "auto":
            case "default":
                return true;
            case "top":
                anchor = TableCellAnchor.Top;
                return true;
            case "middle":
            case "center":
            case "centre":
                anchor = TableCellAnchor.Middle;
                return true;
            case "bottom":
                anchor = TableCellAnchor.Bottom;
                return true;
            default:
                return false;
        }
    }

    private static bool TryParseRibbonFontColor(string? value, out ThemeAwareColor? color)
    {
        color = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = value.Trim();
        if (text.Equals("automatic", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("auto", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("default", StringComparison.OrdinalIgnoreCase))
            return true;

        var hex = text.StartsWith("#", StringComparison.Ordinal) ? text[1..] : text;
        if (hex.Length == 6 &&
            int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
        {
            color = new ThemeAwareColor(SrgbColor.FromRgb(rgb));
            return true;
        }

        color = text.ToLowerInvariant() switch
        {
            "black" => ThemeAwareColor.Black,
            "white" => ThemeAwareColor.White,
            "red" => new ThemeAwareColor(SrgbColor.FromRgb(0xC00000)),
            "green" => new ThemeAwareColor(SrgbColor.FromRgb(0x008000)),
            "blue" => new ThemeAwareColor(SrgbColor.FromRgb(0x0000FF)),
            "yellow" => new ThemeAwareColor(SrgbColor.FromRgb(0xFFFF00)),
            "orange" => new ThemeAwareColor(SrgbColor.FromRgb(0xF4B183)),
            "purple" => new ThemeAwareColor(SrgbColor.FromRgb(0x7030A0)),
            "dark-red" or "dark red" => new ThemeAwareColor(SrgbColor.FromRgb(0x800000)),
            "dark-blue" or "dark blue" => new ThemeAwareColor(SrgbColor.FromRgb(0x1F4E79)),
            _ => null,
        };

        return color is not null;
    }

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
        LastCustomSlideSizeInitialState = SlideSizeDialogPlanner.BuildInitialState(
            _presentation.SlideSizeCxEmu,
            _presentation.SlideSizeCyEmu,
            SlideSizeDialogUnit.Inches);
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
                AutomationProperties.SetName(button, BuildLayoutChoiceLabel(choice));
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
        LastHeaderFooterState = HeaderFooterCommandPlanner.BuildState(Editor);
        if (_headerFooterDialog is not null)
        {
            _headerFooterDialog.Activate();
            return;
        }

        HideLayoutPicker();
        HideTablePicker();
        var dialog = new HeaderFooterDialog(Editor, focus);
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

    private static string BuildLayoutChoiceLabel(PresentationLayoutChoice choice)
    {
        var currentPrefix = choice.IsCurrent ? "Current - " : string.Empty;
        var placeholders = choice.PlaceholderCount == 1 ? "1 placeholder" : $"{choice.PlaceholderCount} placeholders";
        return $"{currentPrefix}{choice.DisplayName}\n{choice.MasterDisplayName} - {placeholders}";
    }

    private static Control BuildLayoutChoiceTile(PresentationLayoutChoice choice)
    {
        var (borderBrush, backgroundBrush) = BuildLayoutChoiceBrushes(choice.Chrome);
        var label = new TextBlock
        {
            Text = BuildLayoutChoiceLabel(choice),
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
        if (!AvaloniaFilePickerService.CanOpen(StorageProvider))
        {
            _statusText.Text = SisterAppFileTextPlanner.FormatCommandUnavailable(SisterAppFileTextPlanner.InsertPictureCommand);
            return;
        }

        var file = await AvaloniaFilePickerService.PickSingleOpenFileAsync(
            StorageProvider,
            AvaloniaFilePickerOpenRequest.FromFileTypes(
                SisterAppFileTextPlanner.InsertPicturePickerTitle,
                [PictureFileType]));

        if (file is null)
            return;

        try
        {
            await using var source = await file.OpenReadAsync();
            using var memory = new MemoryStream();
            await source.CopyToAsync(memory);

            var payload = SlideObjectInsertionPlanner.CreatePicturePayload(memory.ToArray(), file.Name);
            var added = SlideObjectInsertionPlanner.ApplyCommand(
                Editor,
                SlideObjectInsertionPlanner.PictureCommandId,
                payload);

            if (added is not null)
                _statusText.Text = SisterAppFileTextPlanner.FormatInserted(file.Name);
        }
        catch (Exception ex)
        {
            _statusText.Text = SisterAppFileTextPlanner.FormatCommandFailed(SisterAppFileTextPlanner.InsertPictureCommand, ex.Message);
        }
    }

    private async Task InsertMediaFromFileAsync(bool isVideo)
    {
        var command = isVideo
            ? PresentationFileTextResources.InsertVideoCommand
            : PresentationFileTextResources.InsertAudioCommand;
        var pickerTitle = isVideo
            ? PresentationFileTextResources.InsertVideoPickerTitle
            : PresentationFileTextResources.InsertAudioPickerTitle;

        if (!AvaloniaFilePickerService.CanOpen(StorageProvider))
        {
            _statusText.Text = SisterAppFileTextPlanner.FormatCommandUnavailable(command);
            return;
        }

        var file = await AvaloniaFilePickerService.PickSingleOpenFileAsync(
            StorageProvider,
            AvaloniaFilePickerOpenRequest.FromFileTypes(
                pickerTitle,
                [isVideo ? VideoFileType : AudioFileType]));

        if (file is null)
            return;

        try
        {
            await using var source = await file.OpenReadAsync();
            using var memory = new MemoryStream();
            await source.CopyToAsync(memory);

            var payload = SlideObjectInsertionPlanner.CreateMediaPayload(memory.ToArray(), file.Name, isVideo);
            var plan = isVideo
                ? SlideObjectInsertionPlanner.VideoCommandId
                : SlideObjectInsertionPlanner.AudioCommandId;
            var added = SlideObjectInsertionPlanner.ApplyCommand(
                Editor,
                plan,
                mediaPayload: payload);

            if (added is not null)
                _statusText.Text = SisterAppFileTextPlanner.FormatInserted(file.Name);
        }
        catch (Exception ex)
        {
            _statusText.Text = SisterAppFileTextPlanner.FormatCommandFailed(command, ex.Message);
        }
    }

    private async Task InsertEmbeddedObjectFromFileAsync()
    {
        if (!AvaloniaFilePickerService.CanOpen(StorageProvider))
        {
            _statusText.Text = SisterAppFileTextPlanner.FormatCommandUnavailable(
                OleInsertionPlanner.PickerTitle);
            return;
        }

        var file = await AvaloniaFilePickerService.PickSingleOpenFileAsync(
            StorageProvider,
            AvaloniaFilePickerOpenRequest.FromFileTypes(
                OleInsertionPlanner.PickerTitle,
                [EmbeddedObjectFileType]));

        if (file is null)
            return;

        try
        {
            await using var source = await file.OpenReadAsync();
            using var memory = new MemoryStream();
            await source.CopyToAsync(memory);
            Editor.InsertEmbeddedObject(memory.ToArray(), file.Name);
            _statusText.Text = SisterAppFileTextPlanner.FormatInserted(file.Name);
        }
        catch (Exception ex)
        {
            _statusText.Text = SisterAppFileTextPlanner.FormatCommandFailed(
                OleInsertionPlanner.PickerTitle,
                ex.Message);
        }
    }

    private async Task PickTransitionSoundAsync()
    {
        if (!AvaloniaFilePickerService.CanOpen(StorageProvider))
        {
            _statusText.Text = SisterAppFileTextPlanner.FormatCommandUnavailable(
                PresentationFileTextResources.InsertAudioCommand);
            return;
        }

        var file = await AvaloniaFilePickerService.PickSingleOpenFileAsync(
            StorageProvider,
            AvaloniaFilePickerOpenRequest.FromFileTypes(
                PresentationFileTextResources.InsertAudioPickerTitle,
                [AudioFileType]));

        if (file is null)
        {
            return;
        }

        try
        {
            await using var source = await file.OpenReadAsync();
            using var memory = new MemoryStream();
            await source.CopyToAsync(memory);

            Editor.SetCurrentSlideTransitionSound(new TransitionSound
            {
                AudioBytes = memory.ToArray(),
                ContentType = SlideObjectInsertionPlanner.InferMediaContentType(file.Name, isVideo: false),
                IsBuiltIn = false,
            });
            _statusText.Text = SisterAppFileTextPlanner.FormatInserted(file.Name);
        }
        catch (Exception ex)
        {
            _statusText.Text = SisterAppFileTextPlanner.FormatCommandFailed(
                PresentationFileTextResources.InsertAudioCommand,
                ex.Message);
        }
    }

    // ── File lifecycle ─────────────────────────────────────────────────────────

    internal Task ApplyPictureBulletFromFileAsyncForTests() => ApplyPictureBulletFromFileAsync();

    private async Task ApplyPictureBulletFromFileAsync()
    {
        try
        {
            var payload = PictureBulletPayloadProviderForTests is { } provider
                ? await provider()
                : await PickPictureBulletPayloadAsync();

            if (payload is null)
                return;

            if (_textEditor?.TryApplyActiveShapeParagraphPictureBullet(payload) == true)
            {
                _statusText.Text = "Picture bullet applied.";
                return;
            }

            if (_textEditor?.TryApplyActiveTableCellParagraphPictureBullet(payload) == true)
            {
                _statusText.Text = "Picture bullet applied.";
                return;
            }

            if (Editor.TryApplyActiveTableCellParagraphPictureBullet(payload))
                _statusText.Text = "Picture bullet applied.";
        }
        catch (Exception ex)
        {
            _statusText.Text = SisterAppFileTextPlanner.FormatCommandFailed("Picture Bullet", ex.Message);
        }
    }

    private async Task<PresentationPictureBulletPayload?> PickPictureBulletPayloadAsync()
    {
        if (!AvaloniaFilePickerService.CanOpen(StorageProvider))
        {
            _statusText.Text = SisterAppFileTextPlanner.FormatCommandUnavailable("Picture Bullet");
            return null;
        }

        var file = await AvaloniaFilePickerService.PickSingleOpenFileAsync(
            StorageProvider,
            AvaloniaFilePickerOpenRequest.FromFileTypes(
                "Choose Picture Bullet",
                [PictureFileType]));

        if (file is null)
            return null;

        await using var source = await file.OpenReadAsync();
        using var memory = new MemoryStream();
        await source.CopyToAsync(memory);

        return PresentationPictureBulletAuthoringPlanner.CreatePayloadFromFileName(
            memory.ToArray(),
            file.Name);
    }

    internal void OpenChartDataDialog()
    {
        if (!Editor.CanEditSelectedChartData)
            return;

        var dialog = new ChartDataDialog(Editor);
        if (IsVisible)
        {
            _ = dialog.ShowDialog<bool?>(this);
            return;
        }

        dialog.Show();
    }

    internal void OpenChartDisplayOptionsDialog()
    {
        if (!Editor.CanEditSelectedChartFormatting)
            return;

        var dialog = new ChartDisplayOptionsDialog(Editor);
        if (IsVisible)
        {
            _ = dialog.ShowDialog<bool?>(this);
            return;
        }

        dialog.Show();
    }

    internal void OpenChartAxisOptionsDialog() => OpenChartAxisOptionsDialog(null);

    internal void OpenChartAxisOptionsDialog(ChartAxisKind? initialAxis)
    {
        if (!Editor.CanEditSelectedChartFormatting)
            return;

        var dialog = new ChartAxisOptionsDialog(Editor, initialAxis);
        if (IsVisible)
        {
            _ = dialog.ShowDialog<bool?>(this);
            return;
        }

        dialog.Show();
    }

    internal void OpenChartSeriesOptionsDialog() => OpenChartSeriesOptionsDialog(null);

    internal void OpenChartSeriesOptionsDialog(int? initialSeriesIndex)
    {
        if (!Editor.CanEditSelectedChartFormatting)
            return;

        var dialog = new ChartSeriesOptionsDialog(Editor, initialSeriesIndex);
        if (IsVisible)
        {
            _ = dialog.ShowDialog<bool?>(this);
            return;
        }

        dialog.Show();
    }

    private void OnChartPointDoubleClick(ChartPointHit hit)
    {
        Editor.Select(hit.ShapeId);
        OpenChartPointOptionsDialog(hit.SeriesIndex, hit.PointIndex);
    }

    internal void OpenChartPointOptionsDialog(int? seriesIndex = null, int? pointIndex = null)
    {
        if (!Editor.CanEditSelectedChartFormatting)
            return;

        var dialog = new ChartPointOptionsDialog(Editor, seriesIndex, pointIndex);
        if (IsVisible)
        {
            _ = dialog.ShowDialog<bool?>(this);
            return;
        }

        dialog.Show();
    }

    internal void OpenChartLayoutOptionsDialog()
    {
        if (!Editor.CanEditSelectedChartFormatting)
            return;

        var dialog = new ChartLayoutOptionsDialog(Editor);
        if (IsVisible)
        {
            _ = dialog.ShowDialog<bool?>(this);
            return;
        }

        dialog.Show();
    }

    internal void OpenChartDataTableOptionsDialog()
    {
        if (!Editor.CanEditSelectedChartFormatting)
            return;

        var dialog = new ChartDataTableOptionsDialog(Editor);
        if (IsVisible)
        {
            _ = dialog.ShowDialog<bool?>(this);
            return;
        }

        dialog.Show();
    }

    internal void OpenChartBubbleOptionsDialog()
    {
        if (!Editor.CanEditSelectedChartFormatting
            || Editor.SelectedChart is not { ChartType: ChartType.Bubble })
            return;

        var dialog = new ChartBubbleOptionsDialog(Editor);
        if (IsVisible)
        {
            _ = dialog.ShowDialog<bool?>(this);
            return;
        }

        dialog.Show();
    }

    internal void OpenChartPieOptionsDialog()
    {
        if (!Editor.CanEditSelectedChartFormatting
            || Editor.SelectedChart is not { ChartType: ChartType.Pie or ChartType.Doughnut })
            return;

        var dialog = new ChartPieOptionsDialog(Editor);
        if (IsVisible)
        {
            _ = dialog.ShowDialog<bool?>(this);
            return;
        }

        dialog.Show();
    }

    internal void OpenChartPlotStyleOptionsDialog()
    {
        if (!Editor.CanEditSelectedChartFormatting
            || Editor.SelectedChart is not { ChartType: ChartType.Scatter or ChartType.Radar })
            return;

        var dialog = new ChartPlotStyleOptionsDialog(Editor);
        if (IsVisible)
        {
            _ = dialog.ShowDialog<bool?>(this);
            return;
        }

        dialog.Show();
    }

    internal void OpenChart3DViewOptionsDialog()
    {
        if (!Editor.CanEditSelectedChartFormatting)
            return;

        var dialog = new Chart3DViewOptionsDialog(Editor);
        if (IsVisible)
        {
            _ = dialog.ShowDialog<bool?>(this);
            return;
        }

        dialog.Show();
    }

    internal void OpenChartTextOptionsDialog()
    {
        if (!Editor.CanEditSelectedChartFormatting)
            return;

        var dialog = new ChartTextOptionsDialog(Editor);
        if (IsVisible)
        {
            _ = dialog.ShowDialog<bool?>(this);
            return;
        }

        dialog.Show();
    }

    internal void OpenChartAreaOptionsDialog() => OpenChartAreaOptionsDialog(null);

    internal void OpenChartAreaOptionsDialog(ChartAreaFormattingTarget? initialTarget)
    {
        if (!Editor.CanEditSelectedChartFormatting) return;
        var dialog = new ChartAreaOptionsDialog(Editor, initialTarget);
        dialog.ShowDialog(this);
    }

    internal void OpenChartProtectionOptionsDialog()
    {
        if (Editor.SelectedChart is null)
            return;

        var dialog = new ChartProtectionOptionsDialog(Editor);
        if (IsVisible)
        {
            _ = dialog.ShowDialog<bool?>(this);
            return;
        }

        dialog.Show();
    }

    internal void OpenRotationOptionsDialog()
    {
        if (Editor.SelectedShapeIds.Count == 0)
            return;

        var dialog = new RotationOptionsDialog(Editor);
        if (IsVisible)
        {
            _ = dialog.ShowDialog<bool?>(this);
            return;
        }

        dialog.Show();
    }

    internal async void OpenHyperlinkDialog()
    {
        await OpenHyperlinkDialogAsync();
    }

    internal Task<HyperlinkDialogApplyPlan> OpenHyperlinkDialogAsyncForTests() =>
        OpenHyperlinkDialogAsync();

    private async Task<HyperlinkDialogApplyPlan> OpenHyperlinkDialogAsync()
    {
        Hyperlink? selectedRunHyperlink = null;
        var editsSelectedRun = _textEditor is not null
            && _textEditor.TryGetSelectedShapeRunHyperlink(out selectedRunHyperlink);
        var request = HyperlinkDialogPlanner.BuildDialogRequest(
            Editor.Presentation.Slides,
            editsSelectedRun ? selectedRunHyperlink : Editor.SelectedShapeHyperlink);
        LastHyperlinkDialogRequest = request;

        var result = HyperlinkDialogResultProviderForTests is { } provider
            ? await provider(request)
            : await ShowHyperlinkDialogAsync(request);

        var applyPlan = HyperlinkDialogPlanner.BuildApplyPlan(result);
        LastHyperlinkDialogApplyPlan = applyPlan;
        if (applyPlan.ShouldApply)
        {
            var hyperlink = new Hyperlink
            {
                Url = applyPlan.Url,
                TargetSlideId = applyPlan.TargetSlideId,
                Tooltip = applyPlan.Tooltip,
            };
            if (!editsSelectedRun || _textEditor?.TryApplySelectedShapeRunHyperlink(hyperlink) != true)
            {
                Editor.SetShapeHyperlink(applyPlan.Url, applyPlan.TargetSlideId, applyPlan.Tooltip);
                var authoringPostconditionPath = Environment.GetEnvironmentVariable("FREEP_PHYSICAL_HYPERLINK_AUTHORING_POSTCONDITION");
                if (!string.IsNullOrWhiteSpace(authoringPostconditionPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(authoringPostconditionPath)!);
                    File.WriteAllText(authoringPostconditionPath, $"selectedShapeId={Editor.SelectedShapeIds.SingleOrDefault()}\ntargetSlideId={applyPlan.TargetSlideId}\n");
                }
            }
        }

        return applyPlan;
    }

    private async Task<Hyperlink?> ShowHyperlinkDialogAsync(HyperlinkDialogRequest request)
    {
        var dialog = new HyperlinkDialog(request);
        if (IsVisible)
            return await dialog.ShowDialog<Hyperlink?>(this);

        dialog.Show();
        return null;
    }

    internal async void OpenSlideZoomDialog() => await OpenSlideZoomDialogAsync();

    internal async Task OpenSlideZoomDialogAsync()
    {
        var options = SlideZoomInsertionPlanner.BuildTargetOptions(
            Editor.Presentation.Slides,
            Editor.CurrentSlideIndex);
        if (options.Count == 0 || !IsVisible)
            return;

        var dialog = new SlideZoomDialog(options);
        var result = await dialog.ShowDialog<bool?>(this);
        if (result == true && dialog.SelectedTargetSlideId is { Length: > 0 } targetSlideId)
        {
            var shape = Editor.InsertSlideZoom(targetSlideId);
            var targetSlideIndex = Editor.Presentation.Slides.FindIndex(slide =>
                string.Equals(slide.Id, targetSlideId, StringComparison.OrdinalIgnoreCase));
            AttachZoomPreview(shape, targetSlideIndex);
        }
    }

    internal async void OpenSectionZoomDialog() => await OpenSectionZoomDialogAsync();

    internal async Task OpenSectionZoomDialogAsync()
    {
        var options = SectionZoomInsertionPlanner.BuildTargetOptions(
            Editor.Presentation,
            Editor.CurrentSlideIndex);
        if (options.Count == 0 || !IsVisible)
            return;

        var dialog = new SectionZoomDialog(options);
        var result = await dialog.ShowDialog<bool?>(this);
        if (result == true && dialog.SelectedTargetSectionId is { Length: > 0 } targetSectionId)
        {
            var shape = Editor.InsertSectionZoom(targetSectionId);
            if (SummaryZoomPreviewPlanner.TryResolveTargetSlideIndex(
                    Editor.Presentation, targetSectionId, out var targetSlideIndex))
                AttachZoomPreview(shape, targetSlideIndex);
        }
    }

    internal async void OpenSummaryZoomDialog() => await OpenSummaryZoomDialogAsync();

    internal async Task OpenSummaryZoomDialogAsync()
    {
        var options = SummaryZoomInsertionPlanner.BuildTargetOptions(
            Editor.Presentation,
            Editor.CurrentSlideIndex);
        if (options.Count < 2 || !IsVisible)
            return;

        var dialog = new SummaryZoomDialog(options);
        var result = await dialog.ShowDialog<bool?>(this);
        if (result == true)
        {
            var shape = Editor.InsertSummaryZoom(dialog.SelectedTargetSectionIds);
            AttachSummaryZoomPreviews(shape);
        }
    }

    private void AttachSummaryZoomPreviews(SlideShape shape)
    {
        var widthPx = SummaryZoomPreviewPlanner.DefaultPreviewWidthPx;
        var heightPx = SummaryZoomPreviewPlanner.ResolvePreviewHeightPx(Editor.Presentation, widthPx);
        SummaryZoomPreviewPlanner.AttachPreviewImages(
            Editor.Presentation,
            shape,
            slideIndex => SlideRenderer.RenderToBytes(
                Editor.Presentation, slideIndex, widthPx, heightPx));
    }

    private void AttachZoomPreview(SlideShape shape, int targetSlideIndex)
    {
        if (targetSlideIndex < 0)
            return;

        var widthPx = SummaryZoomPreviewPlanner.DefaultPreviewWidthPx;
        var heightPx = SummaryZoomPreviewPlanner.ResolvePreviewHeightPx(Editor.Presentation, widthPx);
        SummaryZoomPreviewPlanner.AttachPreviewImage(
            Editor.Presentation,
            shape,
            targetSlideIndex,
            slideIndex => SlideRenderer.RenderToBytes(
                Editor.Presentation, slideIndex, widthPx, heightPx));
    }

    internal async Task OpenZoomObjectPropertiesDialogAsync()
    {
        var current = Editor.SelectedZoomObjectProperties;
        if (current is null || !IsVisible)
            return;

        var selectedShapeId = GetSingleSelectedShapeId();
        var selectedInfo = selectedShapeId is uint shapeId && Editor.CurrentSlide is { } slide
            ? ShapeTreeLookup.Find(slide, shapeId)?.PreservedObject
            : null;
        var summaryTargets = selectedInfo?.SummaryZoomTargets
            ?? (IReadOnlyList<SummaryZoomTarget>)Array.Empty<SummaryZoomTarget>();
        var summaryTileProperties = summaryTargets.Count == 0
            ? Array.Empty<ZoomObjectProperties>()
            : summaryTargets.Select(target =>
                ZoomObjectPropertiesPlanner.EffectiveSummaryTile(selectedInfo, target.SectionId)).ToArray();
        var dialog = new ZoomObjectPropertiesDialog(current, summaryTargets, summaryTileProperties);
        var result = await dialog.ShowDialog<bool?>(this);
        if (result == true)
        {
            var changed = dialog.SummaryTileProperties is { } tileProperties
                && selectedShapeId is uint summaryPropertiesShapeId
                ? Editor.SetSummaryZoomTileProperties(
                    summaryPropertiesShapeId,
                    tileProperties.SectionId,
                    tileProperties.Properties)
                : Editor.SetSelectedZoomObjectProperties(dialog.Properties);
            if (dialog.SummaryTileLayout is { } tile && selectedShapeId is uint summaryShapeId)
                changed |= Editor.SetSummaryZoomTileLayout(
                    summaryShapeId,
                    tile.SectionId,
                    tile.OffsetFactorX,
                    tile.OffsetFactorY,
                    tile.ScaleFactorX,
                    tile.ScaleFactorY);
            if (changed)
            {
                _fileWorkflow.MarkDirty();
                RefreshCanvas();
                UpdateStatus();
            }
        }
    }

    internal async Task OpenZoomCoverImagePickerAsync()
    {
        var selectedShapeId = GetSingleSelectedShapeId();
        if (selectedShapeId is null || Editor.CurrentSlide is not { } slide)
            return;

        var shape = ShapeTreeLookup.Find(slide, selectedShapeId.Value);
        if (shape?.Kind != SlideShapeKind.Zoom || shape.PreservedObject is not { } info)
            return;

        string? summarySectionId = null;
        if (info.SummaryZoomTargets.Count > 0)
        {
            var targetOptions = info.SummaryZoomTargets
                .Select(target => (target.SectionId, string.IsNullOrWhiteSpace(target.Title)
                    ? target.SectionId
                    : target.Title))
                .ToArray();
            var targetDialog = new SummaryZoomCoverImageTargetDialog(targetOptions);
            var targetResult = await targetDialog.ShowDialog<bool?>(this);
            if (targetResult != true)
                return;
            summarySectionId = targetDialog.SelectedTargetSectionId;
        }
        if (!AvaloniaFilePickerService.CanOpen(StorageProvider))
        {
            _statusText.Text = SisterAppFileTextPlanner.FormatCommandUnavailable(
                ZoomCoverImagePlanner.DialogTitle);
            return;
        }

        var file = await AvaloniaFilePickerService.PickSingleOpenFileAsync(
            StorageProvider,
            AvaloniaFilePickerOpenRequest.FromFileTypes(
                ZoomCoverImagePlanner.DialogTitle,
                [PictureFileType]));
        if (file is null)
            return;

        try
        {
            await using var source = await file.OpenReadAsync();
            using var memory = new MemoryStream();
            await source.CopyToAsync(memory);
            var contentType = SlideObjectInsertionPlanner.InferPictureContentType(file.Name);
            var applied = summarySectionId is null
                ? Editor.SetSelectedZoomCoverImage(memory.ToArray(), contentType)
                : Editor.SetSummaryZoomTileCoverImage(
                    shape.Id, summarySectionId, memory.ToArray(), contentType);
            if (applied)
            {
                _fileWorkflow.MarkDirty();
                RefreshCanvas();
                UpdateStatus();
            }
        }
        catch (Exception ex)
        {
            _statusText.Text = SisterAppFileTextPlanner.FormatCommandFailed(
                ZoomCoverImagePlanner.DialogTitle,
                ex.Message);
        }
    }

    internal async Task RestoreZoomPreviewAsync()
    {
        var selectedShapeId = GetSingleSelectedShapeId();
        if (selectedShapeId is null || Editor.CurrentSlide is not { } slide)
            return;

        var shape = ShapeTreeLookup.Find(slide, selectedShapeId.Value);
        if (shape?.Kind != SlideShapeKind.Zoom || shape.PreservedObject is not { } info)
            return;

        string? summarySectionId = null;
        if (info.SummaryZoomTargets.Count > 0)
        {
            var targetOptions = info.SummaryZoomTargets
                .Select(target => (target.SectionId, string.IsNullOrWhiteSpace(target.Title)
                    ? target.SectionId
                    : target.Title))
                .ToArray();
            var targetDialog = new SummaryZoomCoverImageTargetDialog(targetOptions);
            var targetResult = await targetDialog.ShowDialog<bool?>(this);
            if (targetResult != true)
                return;
            summarySectionId = targetDialog.SelectedTargetSectionId;
        }

        var targetIndex = summarySectionId is not null
            ? SummaryZoomPreviewPlanner.TryResolveTargetSlideIndex(
                Editor.Presentation, summarySectionId, out var summaryIndex)
                ? summaryIndex
                : -1
            : ZoomNavigationService.TryGetTargetSlideIndex(
                Editor.Presentation, info, out var singleIndex)
                ? singleIndex
                : -1;
        if (targetIndex < 0)
            return;

        var widthPx = SummaryZoomPreviewPlanner.DefaultPreviewWidthPx;
        var heightPx = SummaryZoomPreviewPlanner.ResolvePreviewHeightPx(
            Editor.Presentation, widthPx);
        var preview = SlideRenderer.RenderToBytes(
            Editor.Presentation, targetIndex, widthPx, heightPx);
        var applied = summarySectionId is null
            ? Editor.ResetSelectedZoomCoverImage(preview, "image/png")
            : Editor.ResetSummaryZoomTileCoverImage(
                shape.Id, summarySectionId, preview, "image/png");
        if (applied)
        {
            _fileWorkflow.MarkDirty();
            RefreshCanvas();
            UpdateStatus();
        }
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

    private Task<bool> FileNewAsync() =>
        _fileWorkflow.NewAsync(
            FileText.NewAction,
            () =>
            {
                LoadPresentationContent(Presentation.CreateEmpty());
                return Task.CompletedTask;
            });

    private BackstageCallbacks BuildBackstageCallbacks() => new(
        GetPresentation: () => _presentation,
        GetDisplayName: () => _fileWorkflow.DisplayName,
        GetIsDirty: () => _fileWorkflow.IsDirty,
        GetCurrentPath: () => _fileWorkflow.CurrentPath,
        GetRecentEntries: () => _fileWorkflow.RecentEntries,
        GetCurrentOptions: () => _options,
        GetDataFolder: ResolveDataFolderLabel,
        New: FileNew,
        Open: () => _ = FileOpenAsync(),
        OpenPath: OpenRecentPath,
        Save: () => _ = FileSaveAsync(),
        SaveAs: () => _ = FileSaveAsAsync(),
        ExportPdf: () => _ = FileExportPdfAsync(),
        ExportNotesPagePdf: () => _ = FileExportNotesPagePdfAsync(),
        ExportImages: () => _ = FileExportImagesAsync(),
        GetPrintPlan: () => RefreshPrintBackstagePlan(),
        GetPrintPlanForCustomRange: rangeText => RefreshPrintBackstagePlan(
            new PresentationPrintRequest(
                PresentationPrintLayoutKind.FullPageSlides,
                new PresentationSlideRangeRequest(
                    PresentationSlideRangeKind.CustomRange,
                    CustomRangeText: rangeText))),
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

    internal bool HandleBackstageKeyForTests(Key key) => _backstage.HandleKey(key);

    private void OpenRecentPath(string path) => _ = OpenRecentPathAsync(path);

    private Task<bool> OpenRecentPathAsync(string path) =>
        _fileWorkflow.OpenAsync(
            FileText.OpenAction,
            () => Task.FromResult<string?>(path),
            TryLoadPresentationFileAsync);

    private Task<bool> FileOpenAsync() =>
        _fileWorkflow.OpenAsync(
            FileText.OpenAction,
            PromptOpenPathAsync,
            TryLoadPresentationFileAsync);

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

    private async Task<string?> PromptOpenPathAsync()
    {
        var plan = PresentationFileDialogPlanner.BuildOpenPickerPlan();
        if (_openPickerOverrideForTests is { } pickerOverride)
            return await pickerOverride(plan);

        if (!AvaloniaFilePickerService.CanOpen(StorageProvider))
        {
            _statusText.Text = SisterAppFileTextPlanner.FormatCommandUnavailable(SisterAppFileTextPlanner.OpenCommand);
            return null;
        }

        using var file = await AvaloniaFilePickerService.PickSingleOpenFileWithLocalPathAsync(
            StorageProvider,
            AvaloniaFilePickerOpenRequest.FromDescriptors(FileText.OpenPickerTitle, plan.FileTypes));

        if (file is null)
            return null;

        var path = file.LocalPath;
        if (path is null)
            _statusText.Text = SisterAppFileTextPlanner.FormatSelectedFileNotLocalPath(SisterAppFileTextPlanner.OpenCommand);

        return path;
    }

    private Task<bool> FileSaveAsync() =>
        _fileWorkflow.SaveAsync(
            TrySavePresentationFileAsync,
            FileSaveAsAsync);

    private async Task<bool> FileSaveAsAsync()
    {
        try
        {
            var plan = PresentationFileDialogPlanner.BuildSavePickerPlan(_fileWorkflow.CurrentFileName);
            if (_savePickerOverrideForTests is { } pickerOverride)
            {
                var overriddenPath = await pickerOverride(plan);
                if (overriddenPath is null)
                    return false;

                return await TrySavePickerPathAsync(overriddenPath);
            }

            if (!AvaloniaFilePickerService.CanSave(StorageProvider))
            {
                _statusText.Text = SisterAppFileTextPlanner.FormatCommandUnavailable(SisterAppFileTextPlanner.SaveCommand);
                return false;
            }

            using var file = await AvaloniaFilePickerService.PickSaveFileWithLocalPathAsync(
                StorageProvider,
                AvaloniaFilePickerSaveRequest.FromSavePlan(
                    FileText.SavePickerTitle,
                    plan,
                    showOverwritePrompt: true));

            var path = file?.LocalPath;
            if (path is null)
            {
                if (file is not null)
                    _statusText.Text = SisterAppFileTextPlanner.FormatSelectedFileNotLocalPath(SisterAppFileTextPlanner.SaveCommand);

                return false;
            }

            return await TrySavePickerPathAsync(path);
        }
        finally
        {
            RestoreOwnerFocus();
        }
    }

    private async Task<bool> TrySavePickerPathAsync(string path)
    {
        if (!PresentationFileDialogPlanner.TryResolveSavePickerPath(path, out var resolvedPath))
        {
            var error = new InvalidDataException(PresentationFileDialogPlanner.UnsupportedSavePathMessage);
            _statusText.Text = SisterAppFileTextPlanner.FormatCommandFailed(
                SisterAppFileTextPlanner.SaveCommand,
                error.Message);
            await _fileWorkflow.ShowFileCommandErrorAsync("Could not save the presentation", error);
            return false;
        }

        return await TrySavePresentationFileAsync(resolvedPath);
    }

    private async Task<bool> FileExportPdfAsync()
    {
        if (!AvaloniaFilePickerService.CanSave(StorageProvider))
        {
            _statusText.Text = SisterAppFileTextPlanner.FormatCommandUnavailable(
                FileText,
                PresentationExportPlanner.PdfExportCommandText);
            return false;
        }

        var plan = PresentationExportPlanner.BuildPdfExportPickerPlan(_fileWorkflow.CurrentFileName);

        using var file = await AvaloniaFilePickerService.PickSaveFileWithLocalPathAsync(
            StorageProvider,
            AvaloniaFilePickerSaveRequest.FromSavePlan(PresentationExportPlanner.PdfExportPickerTitle, plan));

        var path = file?.LocalPath;
        if (path is null)
        {
            if (file is not null)
            {
                _statusText.Text = SisterAppFileTextPlanner.FormatSelectedFileNotLocalPath(
                    FileText,
                    PresentationExportPlanner.PdfExportCommandText);
            }

            return false;
        }

        try
        {
            var bytes = PresentationRasterPdfExporter.ExportToBytes(
                _presentation,
                request: null,
                SlideRenderer.RenderToBytes,
                SkiaRasterPdfWriter.WriteToBytes);
            ExportAtomicWriter.WriteAllBytes(path, bytes);
            _statusText.Text = $"Exported {Path.GetFileName(path)}";
            return true;
        }
        catch (Exception ex)
        {
            _statusText.Text = SisterAppFileTextPlanner.FormatCommandFailed(
                FileText,
                PresentationExportPlanner.PdfExportCommandText,
                ex.Message);
            return false;
        }
    }

    private async Task<bool> FileExportNotesPagePdfAsync()
    {
        if (!AvaloniaFilePickerService.CanSave(StorageProvider))
        {
            _statusText.Text = SisterAppFileTextPlanner.FormatCommandUnavailable(
                FileText,
                PresentationExportPlanner.NotesPagePdfExportCommandText);
            return false;
        }

        // Notes-page PDF exports the whole deck (one notes page per slide), matching the WPF
        // host (FreeP.App.Host/FileCommands.cs ExportNotesPagePdf, range: null -> AllSlides) and
        // this shell's own slides-PDF export (FileExportPdfAsync above, which also exports the
        // full deck). Do not narrow this to the current slide only.
        var range = new PresentationSlideRangeRequest(PresentationSlideRangeKind.AllSlides);
        var exportPlan = PresentationExportPlanner.BuildNotesPagePdfExportPlan(range, _presentation.Slides.Count);
        var request = new PresentationNotesPagePdfExportRequest(new PresentationPrintRequest(
            PresentationPrintLayoutKind.NotesPages,
            range));
        LastNotesPagePdfRenderPlan = PresentationNotesPagePdfExporter.BuildRenderPlan(
            _presentation,
            request);
        if (!exportPlan.CanExecute)
        {
            _statusText.Text = exportPlan.DisabledReason ?? PresentationExportPlanner.NotesPagePdfExportCommandText;
            return false;
        }

        var plan = PresentationExportPlanner.BuildNotesPagePdfExportPickerPlan(_fileWorkflow.CurrentFileName);

        using var file = await AvaloniaFilePickerService.PickSaveFileWithLocalPathAsync(
            StorageProvider,
            AvaloniaFilePickerSaveRequest.FromSavePlan(PresentationExportPlanner.NotesPagePdfExportPickerTitle, plan));

        var path = file?.LocalPath;
        if (path is null)
        {
            if (file is not null)
            {
                _statusText.Text = SisterAppFileTextPlanner.FormatSelectedFileNotLocalPath(
                    FileText,
                    PresentationExportPlanner.NotesPagePdfExportCommandText);
            }

            return false;
        }

        try
        {
            ExportAtomicWriter.WriteAllBytes(
                path,
                PresentationNotesPagePdfExporter.ExportToBytes(
                    _presentation,
                    request,
                    SkiaPdfWriter.WriteToBytesWithPortableFallback));
            _statusText.Text = $"Exported {Path.GetFileName(path)}";
            return true;
        }
        catch (Exception ex)
        {
            _statusText.Text = SisterAppFileTextPlanner.FormatCommandFailed(
                FileText,
                PresentationExportPlanner.NotesPagePdfExportCommandText,
                ex.Message);
            return false;
        }
    }

    internal PresentationImageExportResult FileExportImagesToFolder(
        string outputDirectory,
        PresentationSlideRangeRequest? range = null) =>
        PresentationImageExportExecutor.Export(
            _presentation,
            new PresentationImageExportRequest(
                outputDirectory,
                BaseFileName: Path.GetFileNameWithoutExtension(_fileWorkflow.CurrentFileName),
                SlideRange: range),
            SlideRenderer.RenderToBytes);

    private async Task<bool> FileExportImagesAsync()
    {
        if (!StorageProvider.CanPickFolder)
        {
            _statusText.Text = SisterAppFileTextPlanner.FormatCommandUnavailable(
                FileText,
                PresentationExportPlanner.ImageExportCommandText);
            return false;
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = PresentationExportPlanner.ImageExportPickerTitle,
            AllowMultiple = false,
        });

        var folder = folders.Count == 0 ? null : folders[0];
        var path = folder?.TryGetLocalPath();
        if (path is null)
        {
            if (folder is not null)
            {
                _statusText.Text = SisterAppFileTextPlanner.FormatSelectedFileNotLocalPath(
                    FileText,
                    PresentationExportPlanner.ImageExportCommandText);
            }

            return false;
        }

        try
        {
            FileExportImagesToFolder(path, BuildCurrentSlideImageExportRange());
            return true;
        }
        catch (Exception ex)
        {
            _statusText.Text = SisterAppFileTextPlanner.FormatCommandFailed(
                FileText,
                PresentationExportPlanner.ImageExportCommandText,
                ex.Message);
            return false;
        }
    }

    private PresentationSlideRangeRequest BuildCurrentSlideImageExportRange() =>
        new(
            PresentationSlideRangeKind.CurrentSlide,
            CurrentSlideNumber: Editor.CurrentSlideIndex + 1);

    internal PresentationHandoutLayoutPlan RefreshHandoutLayoutPlan(int? slidesPerPage = null)
    {
        LastHandoutLayoutPlan = PresentationExportPlanner.BuildHandoutLayoutPlan(
            new PresentationPrintRequest(
                PresentationPrintLayoutKind.Handouts,
                HandoutSlidesPerPage: slidesPerPage),
            _presentation,
            _presentation.SlideSizeCxEmu,
            _presentation.SlideSizeCyEmu);
        _statusText.Text = "Print handout layout planned";
        return LastHandoutLayoutPlan;
    }

    internal PresentationNotesPagePdfRenderPlan RefreshNotesPagePdfRenderPlan(PresentationSlideRangeRequest? range = null)
    {
        LastNotesPagePdfRenderPlan = PresentationNotesPagePdfExporter.BuildRenderPlan(
            _presentation,
            new PresentationNotesPagePdfExportRequest(new PresentationPrintRequest(
                PresentationPrintLayoutKind.NotesPages,
                range)));
        _statusText.Text = "Notes page PDF planned";
        return LastNotesPagePdfRenderPlan;
    }

    internal PresentationPrintOutputPackage RefreshPrintOutputPackage(PresentationPrintRequest? request = null)
    {
        LastPrintOutputPackage = _printOutputPackageFactory?.Invoke(request) ??
            PresentationPrintOutputPackageExecutor.BuildPackage(
                _presentation,
                request,
                SlideRenderer.RenderToBytes,
                SkiaRasterPdfWriter.WriteToBytes,
                SkiaPdfWriter.WriteToBytesWithPortableFallback);
        LastPrintExecutionDescriptor = PresentationPrintOutputPackageExecutor.BuildExecutionDescriptor(
            LastPrintOutputPackage,
            _nativePrintHostCapabilities,
            suggestedBaseFileName: _fileWorkflow.CurrentFileName);
        LastNativePrintHandoffPlan = LastPrintExecutionDescriptor.HandoffPlan;
        _statusText.Text = LastPrintOutputPackage.Plan.DisabledReason ??
            LastNativePrintHandoffPlan.Reason;
        return LastPrintOutputPackage;
    }

    internal PresentationNativePrintHandoffPlan RefreshNativePrintHandoffPlan(PresentationPrintRequest? request = null)
    {
        RefreshPrintOutputPackage(request);
        LastNativePrintHandoffPlan = LastPrintExecutionDescriptor!.HandoffPlan;
        _statusText.Text = LastNativePrintHandoffPlan.Reason;
        return LastNativePrintHandoffPlan;
    }

    internal PresentationPrintBackstagePlan RefreshPrintBackstagePlan(PresentationPrintRequest? request = null)
    {
        LastPrintBackstagePlan = PresentationPrintBackstagePlanner.Build(
            request,
            _presentation,
            Editor.CurrentSlideIndex + 1,
            request?.SlideRange?.SelectedSlideNumbers,
            _nativePrintHostCapabilities,
            _fileWorkflow.CurrentFileName);
        LastNativePrintHandoffPlan = LastPrintBackstagePlan.NativePrintHandoff;
        _statusText.Text = LastPrintBackstagePlan.DisabledReason ??
            LastPrintBackstagePlan.NativePrintHandoff.Reason;
        return LastPrintBackstagePlan;
    }

    internal PresentationPrintBackstagePlan ShowPrintOptionsPane(PresentationPrintRequest? request = null)
    {
        var plan = RefreshPrintBackstagePlan(request);
        _printOptionsPaneRequest = request ?? new PresentationPrintRequest(
            plan.SelectedLayout.Layout.Layout,
            plan.SelectedRange.Request,
            plan.SelectedLayout.Layout.IsHandout ? plan.SelectedLayout.Layout.SlidesPerPage : null,
            plan.PrintHiddenSlides,
            plan.Options.Copies,
            plan.Options.Collate,
            plan.Options.ColorMode,
            plan.Options.FrameSlides,
            plan.Options.IncludeCommentsAndInkMarkup);
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
        if (!_portablePrintWorkflowEnabled || OperatingSystem.IsWindows())
            return await ExecuteNativePrintHandoffAsync(request, cancellationToken).ConfigureAwait(true);

        var requestedRequest = request ?? new PresentationPrintRequest(PresentationPrintLayoutKind.FullPageSlides);
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
            RefreshPrintOutputPackage(effectiveRequest);
            var package = LastPrintOutputPackage;
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

    internal async Task<LinuxNativePrintResult> ExecuteNativePrintHandoffAsync(
        PresentationPrintRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        RefreshPrintOutputPackage(request);
        var package = LastPrintOutputPackage;
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
        _printOptionsPaneHeading.Text = plan.Heading;
        _printOptionsPaneMessage.Text = plan.Description;
        _printOptionsPaneRenderedOptionLines.Clear();
        _printOptionsPaneRenderedPreviewRows.Clear();
        _printOptionsPaneRenderedLayoutRows.Clear();
        _printOptionsPaneRenderedRangeRows.Clear();
        _printOptionsPaneRowsPanel.Children.Clear();

        AddPrintOptionsPaneSection("Settings");
        AddPrintOptionsPaneField("Layout", plan.SelectedLayout.Layout.DisplayName);
        AddPrintOptionsPaneField("Slides", plan.SlideRangeSummary);
        AddPrintOptionsPaneField("Pages", plan.PageCount.ToString(CultureInfo.InvariantCulture));
        AddPrintOptionsPaneField("Preview", plan.PreviewPlan.PageCountText);
        AddPrintOptionsPaneField("Hidden slides", plan.PrintHiddenSlides ? "Included" : "Not included");
        AddPrintOptionsPaneField("Options", plan.Options.DisplaySummary);
        AddPrintOptionsPaneField("Native printer handoff", plan.NativePrintHandoff.StatusText);
#if FREEP_WINDOWS_CAPTURE
        AddWindowsPrinterSelector();
#endif

        AddPrintOptionsPaneSection("Output options");

        foreach (var choice in plan.OutputOptionChoices)
        {
            var row = BuildPrintOptionsPaneChoiceSummary(
                $"{choice.Group}: {choice.DisplayName}",
                choice.Description,
                choice.IsSelected,
                choice.IsAvailable);
            _printOptionsPaneRenderedOptionLines.Add(row);
            AddPrintOptionsPaneChoice(row, choice.IsAvailable);
        }

        AddPrintOptionsPaneSection("Preview");
        foreach (var page in plan.PreviewPlan.Pages)
        {
            var row = BuildPrintOptionsPaneChoiceSummary(
                page.ThumbnailLabel,
                page.Detail,
                page.PageNumber == 1);
            _printOptionsPaneRenderedPreviewRows.Add(row);
            AddPrintOptionsPaneChoice(row, isAvailable: true);
        }

        AddPrintOptionsPaneSection("Layouts");
        foreach (var choice in plan.LayoutChoices)
        {
            var row = BuildPrintOptionsPaneChoiceSummary(
                choice.Layout.DisplayName,
                choice.PackagePlan.LayoutSummary,
                choice.IsSelected);
            _printOptionsPaneRenderedLayoutRows.Add(row);
            AddPrintOptionsPaneChoice(row, isAvailable: true);
        }

        AddPrintOptionsPaneSection("Slide range");
        foreach (var choice in plan.RangeChoices)
        {
            var row = BuildPrintOptionsPaneChoiceSummary(
                choice.DisplayName,
                choice.Description,
                choice.Kind == plan.SelectedRange.Kind,
                choice.IsAvailable);
            _printOptionsPaneRenderedRangeRows.Add(row);
            AddPrintOptionsPaneChoice(row, choice.IsAvailable);
        }

        AddPrintOptionsPaneSection("Custom range");
        _printOptionsPaneRowsPanel.Children.Add(new TextBlock
        {
            Text = "Enter slide numbers and ranges, for example 2,4-6.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70)),
            Margin = new Thickness(0, 0, 0, 4),
        });
        _printCustomRangeInput = new TextBox
        {
            Text = plan.SelectedRange.Request?.CustomRangeText ?? string.Empty,
            PlaceholderText = "e.g. 2,4-6",
            MinWidth = 240,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 6),
        };
        AutomationProperties.SetAutomationId(_printCustomRangeInput, "FreePPrintCustomRangeInput");
        _printCustomRangeApplyButton = new Button
        {
            Content = "Apply range",
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 6),
        };
        AutomationProperties.SetAutomationId(_printCustomRangeApplyButton, "FreePPrintCustomRangeApply");
        _printCustomRangeApplyButton.Click += (_, _) =>
        {
            var text = _printCustomRangeInput.Text?.Trim() ?? string.Empty;
            ShowPrintOptionsPane(string.IsNullOrWhiteSpace(text)
                ? new PresentationPrintRequest(PresentationPrintLayoutKind.FullPageSlides)
                : new PresentationPrintRequest(
                    PresentationPrintLayoutKind.FullPageSlides,
                    new PresentationSlideRangeRequest(
                        PresentationSlideRangeKind.CustomRange,
                        CustomRangeText: text)));
        };
        _printOptionsPaneRowsPanel.Children.Add(_printCustomRangeInput);
        _printOptionsPaneRowsPanel.Children.Add(_printCustomRangeApplyButton);

        _printOptionsPaneRowsPanel.Children.Add(new TextBlock
        {
            Text = plan.DisabledReason ?? plan.NativePrintHandoff.Reason,
            TextWrapping = TextWrapping.Wrap,
            FontStyle = FontStyle.Italic,
            Foreground = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70)),
            Margin = new Thickness(0, 8, 0, 0),
        });
        _printOptionsPaneExecuteButton.IsEnabled =
            plan.NativePrintHandoff.CanOpenNativePrintDialog ||
            plan.NativePrintHandoff.CanSubmitToNativePrinter;
    }

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
        LastVideoExportPlan = PresentationExportPlanner.BuildVideoExportPlan(
            request,
            _presentation,
            _videoExportHostCapabilities);

        _statusText.Text = LastVideoExportPlan.DisabledReason ?? "Video export planned";
        return LastVideoExportPlan;
    }

    internal Task<bool> FileExportVideoAsyncForTests() => FileExportVideoAsync();

    private async Task<bool> FileExportVideoAsync()
    {
        if (!_nativeOutputCapabilities.Video.CanEncodeMp4)
        {
            _statusText.Text = _nativeOutputCapabilities.Video.Reason;
            return false;
        }

        if (VideoPickerOverrideForTests is null && !AvaloniaFilePickerService.CanSave(StorageProvider))
        {
            _statusText.Text = SisterAppFileTextPlanner.FormatCommandUnavailable(
                FileText,
                PresentationExportPlanner.VideoExportCommandText);
            return false;
        }

        var plan = PresentationExportPlanner.BuildVideoExportPickerPlan(_fileWorkflow.CurrentFileName);
        string? path;
        var wasSelected = false;
        if (VideoPickerOverrideForTests is { } pickerOverride)
        {
            var selection = await pickerOverride(plan);
            if (selection is null)
                return false;
            wasSelected = true;
            path = selection.LocalPath;
        }
        else
        {
            using var file = await AvaloniaFilePickerService.PickSaveFileWithLocalPathAsync(
                StorageProvider,
                AvaloniaFilePickerSaveRequest.FromSavePlan(
                    PresentationExportPlanner.VideoExportPickerTitle,
                    plan,
                    showOverwritePrompt: true));
            wasSelected = file is not null;
            path = file?.LocalPath;
        }
        if (path is null)
        {
            if (wasSelected)
                _statusText.Text = SisterAppFileTextPlanner.FormatSelectedFileNotLocalPath(
                    FileText,
                    PresentationExportPlanner.VideoExportCommandText);
            return false;
        }

        var result = await ExecuteVideoExportAsync(path);
        return result.Succeeded;
    }

    internal PresentationVideoFramePackage RefreshVideoFramePackage(PresentationVideoExportRequest? request = null)
    {
        LastVideoFramePackage = _videoFramePackageFactory?.Invoke(request) ??
            PresentationVideoFramePackageExecutor.BuildPackage(
                _presentation,
                request,
                SlideRenderer.RenderToBytes,
                _videoExportHostCapabilities);
        LastVideoExportPlan = LastVideoFramePackage.Plan.ExportPlan;
        LastVideoExecutionDescriptor = PresentationVideoFramePackageExecutor.BuildExecutionDescriptor(
            LastVideoFramePackage,
            _videoExportHostCapabilities,
            _fileWorkflow.CurrentFileName);
        LastVideoExportHandoffPlan = LastVideoExecutionDescriptor.HandoffPlan;
        _statusText.Text = LastVideoExportHandoffPlan.StatusText;
        return LastVideoFramePackage;
    }

    internal async Task<LinuxVideoExportResult> ExecuteVideoExportAsync(
        string outputPath,
        PresentationVideoExportRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        RefreshVideoFramePackage(request);
        var package = LastVideoFramePackage;
        if (package is null)
        {
            LastVideoExportResult = LinuxVideoExportResult.Failed("Video frame package was not built.", outputPath);
            return LastVideoExportResult;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _nativeOutputCancellation = linkedCancellation;
        try
        {
            LastVideoExportResult = await _videoExportAdapter.ExportAsync(
                package,
                outputPath,
                linkedCancellation.Token,
                _presentation.RecordingMediaArtifacts).ConfigureAwait(true);
        }
        finally
        {
            if (ReferenceEquals(_nativeOutputCancellation, linkedCancellation))
                _nativeOutputCancellation = null;
        }
        _statusText.Text = LastVideoExportResult.StatusText;
        if (!LastVideoExportResult.Succeeded && !LastVideoExportResult.Canceled &&
            LastVideoExportResult.FailureReason is not null)
        {
            _statusText.Text = $"{LastVideoExportResult.StatusText}: {LastVideoExportResult.FailureReason}";
        }

        return LastVideoExportResult;
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
            capability.CanEncodeMp4
                ? isWindowsNative
                    ? capability.Reason
                    : capability.CanCaptureNarration
                        ? "ffmpeg video export, persisted narration muxing, and captured camera picture-in-picture are available."
                        : "Video-only ffmpeg export is available; narration and captured camera picture-in-picture are unavailable."
                : capability.Reason);
    }

    private void RegisterReviewWorkflowCommands(RibbonCommandRegistry registry)
    {
        registry.Register(
            PresentationReviewWorkflowPlanner.CommentsPaneCommandId,
            new ActionRibbonCommand(() => ShowReviewCommentsPane()));
        registry.Register(
            PresentationReviewWorkflowPlanner.AccessibilityCommandId,
            new ActionRibbonCommand(() => ShowAccessibilityCheckerPane()));
        registry.Register(
            PresentationReviewWorkflowPlanner.AltTextCommandId,
            new ActionRibbonCommand(ShowAltTextPane));
        registry.Register(
            PresentationReviewWorkflowPlanner.ReadingOrderPaneCommandId,
            new ActionRibbonCommand(() => ShowReadingOrderPane()));
        registry.Register(
            PresentationSelectionPanePlanner.SelectionPaneCommandId,
            new ActionRibbonCommand(() => ShowSelectionPane()));
        registry.Register(
            PresentationReviewWorkflowPlanner.ProofingCommandId,
            new ActionRibbonCommand(() => ShowProofingPane()));
        registry.Register(
            PresentationReviewWorkflowPlanner.AddCommentCommandId,
            new ActionRibbonCommand(() => AddComment("New comment")));
        registry.Register(
            PresentationReviewWorkflowPlanner.EditCommentCommandId,
            new ActionRibbonCommand(() => EditSelectedComment(GetSelectedCommentText())));
        registry.Register(
            PresentationReviewWorkflowPlanner.ReplyCommentCommandId,
            new ActionRibbonCommand(() => ReplyToSelectedComment("New reply")));
        registry.Register(
            PresentationReviewWorkflowPlanner.DeleteCommentCommandId,
            new ActionRibbonCommand(() => DeleteSelectedComment()));
        registry.Register(
            PresentationReviewWorkflowPlanner.PreviousCommentCommandId,
            new ActionRibbonCommand(() => NavigateReviewComment(PresentationReviewWorkflowIntentKind.PreviousComment)));
        registry.Register(
            PresentationReviewWorkflowPlanner.NextCommentCommandId,
            new ActionRibbonCommand(() => NavigateReviewComment(PresentationReviewWorkflowIntentKind.NextComment)));
        registry.Register(
            PresentationReviewWorkflowPlanner.ResolveCommentCommandId,
            new ActionRibbonCommand(() => ResolveSelectedComment()));
        registry.Register(
            PresentationReviewWorkflowPlanner.ReopenCommentCommandId,
            new ActionRibbonCommand(() => ReopenSelectedComment()));
    }

    private void RegisterViewShowCommands(RibbonCommandRegistry registry)
    {
        foreach (var plan in PresentationViewShowPlanner.BuildPlans(_viewShowState))
        {
            registry.Register(
                plan.CommandId,
                new ViewShowToggleCommand(
                    plan,
                    () => _viewShowState,
                    ApplyPresentationViewShowState));
        }
    }

    private void RegisterViewZoomCommands(RibbonCommandRegistry registry)
    {
        foreach (var plan in PresentationViewZoomPlanner.BuiltInPlans)
        {
            registry.Register(
                plan.CommandId,
                new ContextRibbonCommand(ctx =>
                {
                    var result = PresentationViewZoomPlanner.Execute(
                        _viewZoomState,
                        plan,
                        ctx.SelectedValue);
                    ApplyPresentationViewZoomState(result.State);
                }));
        }
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
            Content  = "New Comment",
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
                () => ResolveCommentInputCaret(editInput.Text, editInput.CaretIndex),
                updatedText => EditSelectedComment(updatedText));
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
                PlaceholderText = "Reply",
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
                () => ResolveCommentInputCaret(replyInput.Text, replyInput.CaretIndex),
                updatedText => ReplyToSelectedComment(updatedText));
            var replyButton = new Button
            {
                Content = "Reply",
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
        Func<string, PresentationCommentMutationPlan> applyUpdatedText)
    {
        var mentionPicker = BuildCommentMentionPickerPlanForCurrentInput(getText, getCaretIndex);
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
            var currentPlan = BuildCommentMentionPickerPlanForCurrentInput(getText, getCaretIndex);
            if (currentPlan.Candidates.Count == 1)
            {
                ApplyCommentMentionCandidate(
                    getText,
                    getCaretIndex,
                    currentPlan.DefaultCandidate,
                    applyUpdatedText);
                return;
            }

            if (currentPlan.HasCandidates)
            {
                var menu = BuildCommentMentionMenu(
                    tag,
                    getText,
                    getCaretIndex,
                    applyUpdatedText,
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
        Func<string, PresentationCommentMutationPlan> applyUpdatedText,
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
            item.Click += (_, _) => ApplyCommentMentionCandidate(
                getText,
                getCaretIndex,
                candidate,
                applyUpdatedText);
            menu.Items.Add(item);
        }

        return menu;
    }

    private void ApplyCommentMentionCandidate(
        Func<string?> getText,
        Func<int> getCaretIndex,
        PresentationCommentMentionCandidate? candidate,
        Func<string, PresentationCommentMutationPlan> applyUpdatedText)
    {
        LastCommentMentionInsertionPlan = PresentationReviewWorkflowPlanner.BuildCommentMentionInsertionPlan(
            getText(),
            getCaretIndex(),
            candidate);
        if (LastCommentMentionInsertionPlan.ShouldApply)
            applyUpdatedText(LastCommentMentionInsertionPlan.UpdatedText);
    }

    private PresentationCommentMentionPickerPlan BuildCommentMentionPickerPlanForCurrentInput(
        Func<string?> getText,
        Func<int> getCaretIndex)
    {
        LastCommentMentionPickerPlan = PresentationReviewWorkflowPlanner.BuildCommentMentionPickerPlanForInsertionContext(
            _presentation.Slides,
            getText(),
            getCaretIndex());
        return LastCommentMentionPickerPlan;
    }

    private static int ResolveCommentInputCaret(string? text, int caretIndex)
    {
        var currentText = text ?? string.Empty;
        return caretIndex == 0 && currentText.Length > 0 ? currentText.Length : caretIndex;
    }

    private void ExecuteReviewCommentAction(string commandId)
    {
        if (commandId == PresentationReviewWorkflowPlanner.AddCommentCommandId)
        {
            AddComment("New comment");
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
    {
        var plan = _reviewWorkflowSession.BuildCommentMentionPickerPlanForTests(query, currentAuthor, currentInitials);
        LastCommentMentionPickerPlan = plan;
        return plan;
    }

    internal PresentationCommentMentionInsertionPlan InsertCommentMentionForTests(
        string? text,
        int caretIndex,
        PresentationCommentMentionCandidate? candidate)
    {
        var plan = _reviewWorkflowSession.InsertCommentMentionForTests(text, caretIndex, candidate);
        LastCommentMentionInsertionPlan = plan;
        return plan;
    }

    internal PresentationCommentMutationPlan InsertMentionInSelectedCommentForTests(
        int caretIndex,
        PresentationCommentMentionCandidate? candidate,
        string? author = null,
        string? initials = null)
    {
        var plan = _reviewWorkflowSession.InsertMentionInSelectedCommentForTests(
            caretIndex,
            candidate,
            author,
            initials);
        LastCommentMentionInsertionPlan = _reviewWorkflowSession.LastCommentMentionInsertionPlan;
        return plan;
    }

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
        LastAnimationPaneTimelinePlan = AnimationPanePlanner.BuildTimelinePlan(
            Editor.CurrentSlide,
            Editor.SelectedShapeIds,
            selectedAnimationIndex,
            isPlaybackRunning: _animationPanePlaybackSessionPlan?.IsRunning == true);
        RefreshPaneAccessibilityMetadata();
        return LastAnimationPaneTimelinePlan;
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
        _selectedAnimationIndex = plan.SelectedIndex;
        LastAnimationPaneWorkflowEvidencePlan =
            AnimationPanePlanner.BuildWorkflowEvidencePlan(plan, Editor.CurrentSlideIndex);
        var viewPlan = LastAnimationPaneWorkflowEvidencePlan.View;
        _animationPaneHeading.Text = "Animation Pane";
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
        var control = RefreshAnimationPaneTimelinePlan(_selectedAnimationIndex)
            .PlaybackControls
            .First(candidate => candidate.Kind == controlKind);
        return ExecuteAnimationPanePlaybackControl(control, startPreview: false);
    }

    private AnimationPanePlaybackSessionPlan ExecuteAnimationPanePlaybackControl(
        AnimationPanePlaybackControlDescriptor control,
        bool startPreview)
    {
        var timeline = LastAnimationPaneTimelinePlan ?? RefreshAnimationPaneTimelinePlan(_selectedAnimationIndex);
        _animationPanePlaybackSessionPlan = AnimationPanePlanner.BuildPlaybackSessionPlan(timeline, control.Kind);
        _animationPanePlaybackWorkflowEvidencePlan = AnimationPanePlanner.BuildPlaybackWorkflowEvidencePlan(
            timeline,
            _animationPanePlaybackSessionPlan,
            Array.Empty<SlideShowAnimationStepVisualCheckpointPlan>(),
            Editor.CurrentSlideIndex);
        RefreshVisibleAnimationPane(_selectedAnimationIndex);

        if (!control.IsEnabled)
            return _animationPanePlaybackSessionPlan;

        switch (control.Kind)
        {
            case AnimationPanePlaybackControlKind.PreviewCurrentSlide:
            case AnimationPanePlaybackControlKind.PlayFromSelected:
            case AnimationPanePlaybackControlKind.PlayCurrentSlide:
                if (startPreview)
                    StartAnimationPanePreview(_animationPanePlaybackSessionPlan);
                break;
        }

        return _animationPanePlaybackSessionPlan;
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
                ? "Effect options"
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
        ToolTip.SetTip(wheelSpokeCombo, "Wheel spokes");
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
            ItemsSource = AnimationPanePlanner.TriggerLabels,
            SelectedIndex = item.TriggerIndex,
            Width = 110,
            Height = 24,
            FontSize = 10,
            Margin = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = item.Index,
        };
        ToolTip.SetTip(triggerCombo, "Trigger");
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
        ToolTip.SetTip(durationBox, "Duration (seconds)");
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
        ToolTip.SetTip(delayBox, "Delay (seconds)");
        delayBox.LostFocus += (_, _) =>
        {
            var plan = ApplyAnimationPaneDelayEdit(item.Index, delayBox.Text ?? string.Empty);
            if (!plan.ShouldApply)
                delayBox.Text = plan.DisplayText;
        };
        _animationPaneDelayControlCount++;

        var repeatCombo = new ComboBox
        {
            ItemsSource = new[] { "1", "2", "3", "4", "Indefinitely" },
            SelectedItem = AnimationPanePlanner.FormatRepeat(item.RepeatCount, item.RepeatIndefinitely),
            Width = 82,
            Height = 24,
            FontSize = 10,
            Margin = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = item.Index,
        };
        ToolTip.SetTip(repeatCombo, "Repeat count");

        var autoReverseCheck = new CheckBox
        {
            IsChecked = item.AutoReverse,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2),
            Tag = item.Index,
        };
        ToolTip.SetTip(autoReverseCheck, "Auto-reverse between repeats");

        void ApplyRepeat()
        {
            var plan = AnimationPanePlanner.BuildRepeatMutationPlan(
                Editor.CurrentSlideAnimations,
                item.Index,
                repeatCombo.SelectedItem as string,
                autoReverseCheck.IsChecked == true);
            if (!AnimationPanePlanner.TryApplyRepeatMutation(Editor, plan)
                && plan.DisabledReason is not null)
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
            "Move earlier",
            () => MoveAnimationPaneItem(item.Index, -1));
        var moveLaterButton = BuildAnimationPaneActionButton(
            "▼",
            item.CanMoveLater,
            "Move later",
            () => MoveAnimationPaneItem(item.Index, 1));
        var removeButton = BuildAnimationPaneActionButton(
            "×",
            true,
            "Remove animation",
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
            ? BuildAnimationPaneActionButton("Edit", true, "Edit motion path geometry", () => _ = OpenMotionPathEditorAsync(item.Index))
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
                     (actionPanel, 10),
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
        var plan = AnimationPanePlanner.BuildParagraphBuildMutationPlan(
            Editor.CurrentSlide,
            shapeId);
        if (AnimationPanePlanner.TryApplyParagraphBuildMutation(Editor, plan))
            RefreshVisibleAnimationPane(_selectedAnimationIndex);
    }

    internal AnimationPaneParagraphBuildMutationPlan ToggleParagraphBuildForTests(uint shapeId)
    {
        var plan = AnimationPanePlanner.BuildParagraphBuildMutationPlan(
            Editor.CurrentSlide,
            shapeId);
        AnimationPanePlanner.TryApplyParagraphBuildMutation(Editor, plan);
        RefreshVisibleAnimationPane(_selectedAnimationIndex);
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
        var plan = AnimationPanePlanner.BuildEffectOptionMutationPlan(
            Editor.CurrentSlideAnimations,
            animationIndex,
            optionId);
        if (AnimationPanePlanner.TryApplyEffectOptionMutation(Editor, plan))
            RefreshVisibleAnimationPane(_selectedAnimationIndex);
        return plan;
    }

    private AnimationPaneTimingMutationPlan ApplyAnimationPaneTriggerEdit(
        int animationIndex,
        int selectedTriggerIndex)
    {
        var plan = AnimationPanePlanner.BuildTriggerMutationPlan(
            Editor.CurrentSlideAnimations,
            animationIndex,
            selectedTriggerIndex);
        ApplyAnimationPaneTimingMutation(plan);
        return plan;
    }

    private AnimationPaneTimingMutationPlan ApplyAnimationPaneDurationEdit(
        int animationIndex,
        string text)
    {
        var plan = AnimationPanePlanner.BuildDurationMutationPlan(
            Editor.CurrentSlideAnimations,
            animationIndex,
            text);
        ApplyAnimationPaneTimingMutation(plan);
        return plan;
    }

    private AnimationPaneTimingMutationPlan ApplyAnimationPaneDelayEdit(
        int animationIndex,
        string text)
    {
        var plan = AnimationPanePlanner.BuildDelayMutationPlan(
            Editor.CurrentSlideAnimations,
            animationIndex,
            text);
        ApplyAnimationPaneTimingMutation(plan);
        return plan;
    }

    private void ApplyAnimationPaneTimingMutation(AnimationPaneTimingMutationPlan plan)
    {
        if (AnimationPanePlanner.TryApplyTimingMutation(Editor, plan))
            RefreshVisibleAnimationPane(_selectedAnimationIndex);
    }

    private void SelectAnimationPaneItem(int animationIndex)
    {
        _selectedAnimationIndex = animationIndex;
        var animations = Editor.CurrentSlideAnimations;
        if (animationIndex >= 0 && animationIndex < animations.Count)
            Editor.Select(animations[animationIndex].ShapeId);

        RefreshVisibleAnimationPane(_selectedAnimationIndex);
    }

    private AnimationPaneReorderMutationPlan MoveAnimationPaneItem(int animationIndex, int offset)
    {
        var plan = AnimationPanePlanner.BuildReorderMutationPlan(
            Editor.CurrentSlideAnimations,
            animationIndex,
            offset);
        if (!AnimationPanePlanner.TryApplyReorderMutation(Editor, plan))
            return plan;

        _selectedAnimationIndex = plan.SelectedAnimationIndex;
        RefreshVisibleAnimationPane(_selectedAnimationIndex);
        return plan;
    }

    internal AnimationPaneRemoveMutationPlan RemoveAnimationPaneItemForTests(int animationIndex) =>
        RemoveAnimationPaneItem(animationIndex);

    private AnimationPaneRemoveMutationPlan RemoveAnimationPaneItem(int animationIndex)
    {
        var plan = AnimationPanePlanner.BuildRemoveMutationPlan(
            Editor.CurrentSlideAnimations,
            animationIndex);
        if (AnimationPanePlanner.TryApplyRemoveMutation(Editor, plan))
        {
            _selectedAnimationIndex = plan.SelectedAnimationIndex;
            RefreshVisibleAnimationPane(_selectedAnimationIndex);
        }

        return plan;
    }

    private static string FormatAvailability(bool isAvailable)
        => isAvailable ? "available" : "unavailable";

    private void RefreshAccessibilitySummaryPlan()
    {
        LastMediaTranscriptPlan = PresentationMediaTranscriptPlanner.BuildTranscriptPlan(_presentation);
        LastAccessibilitySummaryPlan =
            PresentationReviewWorkflowPlanner.BuildAccessibilitySummaryPlan(_presentation);
        LastAccessibilityCheckerPanePlan =
            PresentationReviewWorkflowPlanner.BuildAccessibilityCheckerPanePlan(
                _presentation,
                LastAccessibilitySummaryPlan,
                _selectedAccessibilityCheckerRowIndex);
        _selectedAccessibilityCheckerRowIndex = LastAccessibilityCheckerPanePlan.SelectedRowIndex >= 0
            ? LastAccessibilityCheckerPanePlan.SelectedRowIndex
            : null;
        if (IsAccessibilityCheckerPaneVisible)
            RenderAccessibilityCheckerPane(LastAccessibilityCheckerPanePlan);
    }

    internal PresentationAccessibilityCheckerPanePlan ShowAccessibilityCheckerPane()
    {
        RefreshAccessibilitySummaryPlan();
        RenderAccessibilityCheckerPane(LastAccessibilityCheckerPanePlan!);
        _accessibilityCheckerPaneHost.IsVisible = true;
        RefreshPaneAccessibilityMetadata();
        return LastAccessibilityCheckerPanePlan!;
    }

    internal PresentationAccessibilityCheckerPanePlan SelectAccessibilityCheckerRow(int rowIndex)
    {
        RefreshAccessibilitySummaryPlan();
        var normalized = PresentationReviewWorkflowPlanner.NormalizeAccessibilityCheckerRowSelection(
            LastAccessibilityCheckerPanePlan!,
            rowIndex);
        _selectedAccessibilityCheckerRowIndex = normalized >= 0 ? normalized : null;
        LastAccessibilityCheckerPanePlan =
            PresentationReviewWorkflowPlanner.BuildAccessibilityCheckerPanePlan(
                _presentation,
                LastAccessibilitySummaryPlan!,
                _selectedAccessibilityCheckerRowIndex);
        NavigateToAccessibilityCheckerRow(
            PresentationReviewWorkflowPlanner.BuildAccessibilityCheckerNavigationPlan(
                LastAccessibilityCheckerPanePlan,
                _selectedAccessibilityCheckerRowIndex));
        if (LastAccessibilityCheckerPanePlan.SelectedRow?.CommandHint != PresentationReviewWorkflowPlanner.ReviewTableStructureCommandId)
            ClearTableStructureReviewDisplay();
        RenderAccessibilityCheckerPane(LastAccessibilityCheckerPanePlan);
        _accessibilityCheckerPaneHost.IsVisible = true;
        return LastAccessibilityCheckerPanePlan;
    }

    internal PresentationAccessibilityCheckerPanePlan ApplyAccessibilityCheckerRowAction(int rowIndex)
    {
        var plan = SelectAccessibilityCheckerRow(rowIndex);
        var row = plan.SelectedRow;
        if (row?.CommandHint == PresentationReviewWorkflowPlanner.AltTextCommandId)
        {
            ShowAltTextPane();
        }
        else if (row?.CommandHint == PresentationReviewWorkflowPlanner.SetSlideTitleCommandId)
        {
            LastSlideTitleMutationPlan =
                PresentationReviewWorkflowPlanner.TryApplySlideTitleMutation(Editor, row.SlideIndex);
        }
        else if (row?.CommandHint == PresentationReviewWorkflowPlanner.SetTableHeaderRowCommandId)
        {
            LastTableHeaderRowMutationPlan =
                PresentationReviewWorkflowPlanner.TryApplyTableHeaderRowMutation(
                    Editor,
                    row.SlideIndex,
                    row.ShapeId);
            RefreshAccessibilitySummaryPlan();
        }
        else if (row?.CommandHint == PresentationReviewWorkflowPlanner.ReviewTableStructureCommandId)
        {
            LastTableStructureReviewPlan = OpenTableStructureReviewPlan(row);
        }
        else if (row?.CommandHint == PresentationReviewWorkflowPlanner.InsertLinkCommandId)
        {
            OpenHyperlinkDialog();
        }
        else if (row?.CommandHint == PresentationReviewWorkflowPlanner.ChartTitleCommandId)
        {
            LastChartTitleMutationPlan =
                PresentationReviewWorkflowPlanner.TryApplyChartTitleMutation(
                    Editor,
                    row.SlideIndex,
                    row.ShapeId);
            RefreshAccessibilitySummaryPlan();
        }
        else if (row?.CommandHint == PresentationMediaTranscriptPlanner.CaptionAuthoringPaneOpenCommandId
            || row?.Category == "Media")
        {
            ShowMediaCaptionPane();
        }

        return LastAccessibilityCheckerPanePlan!;
    }

    private PresentationTableStructureReviewPlan OpenTableStructureReviewPlan(PresentationAccessibilityCheckerRowPlan row)
    {
        var reviewPlan = PresentationReviewWorkflowPlanner.BuildTableStructureReviewPlan(
            _presentation,
            row.SlideIndex,
            row.ShapeId);
        LastTableStructureReviewDisplayPlan =
            PresentationReviewWorkflowPlanner.BuildTableStructureReviewDisplayPlan(reviewPlan);
        RefreshAccessibilitySummaryPlan();
        LastAccessibilityCheckerPanePlan =
            PresentationReviewWorkflowPlanner.BuildAccessibilityCheckerPanePlan(
                _presentation,
                LastAccessibilitySummaryPlan!,
                row.RowIndex);
        RenderAccessibilityCheckerPane(LastAccessibilityCheckerPanePlan);
        _accessibilityCheckerPaneHost.IsVisible = true;
        return reviewPlan;
    }

    private void NavigateToAccessibilityCheckerRow(PresentationAccessibilityCheckerNavigationPlan plan)
    {
        if (!plan.ShouldNavigate)
            return;

        Editor.SelectSlide(plan.TargetSlideIndex);
        if (plan.ShouldSelectShape && plan.TargetShapeId is { } shapeId)
            Editor.Select(shapeId);
    }

    private void RenderAccessibilityCheckerPane(PresentationAccessibilityCheckerPanePlan plan)
    {
        _accessibilityCheckerPaneHeading.Text =
            $"Accessibility - {plan.IssueCount} issues";
        _accessibilityCheckerPaneMessage.Text = plan.SelectedRow is { } selected
            ? $"{selected.SlideDisplay}: {selected.Title}"
            : "No accessibility issues found.";
        RenderTableStructureReviewDetails(LastTableStructureReviewDisplayPlan);

        _accessibilityCheckerRowsPanel.Children.Clear();
        if (plan.Rows.Count == 0)
        {
            _accessibilityCheckerRowsPanel.Children.Add(new TextBlock
            {
                Text = "No accessibility issues found.",
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
                Text = "Selected issue",
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

    private void ClearTableStructureReviewDisplay()
    {
        LastTableStructureReviewPlan = null;
        LastTableStructureReviewDisplayPlan = null;
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
        var smartArtShape = GetSelectedSmartArtShape();
        var rows = _smartArtTextPaneRowsPanel.Children
            .OfType<TextBox>()
            .Select(box => box.Tag is SmartArtNodeOutlineItem item
                ? new SmartArtTextPaneOutlineRow(box.Text ?? string.Empty, item.Level, item.IsAssistant, item.ModelId)
                : new SmartArtTextPaneOutlineRow(box.Text ?? string.Empty, 0))
            .ToArray();

        if (smartArtShape is null)
        {
            LastSmartArtTextPaneApplyResult = SmartArtEditingPlanner.ApplyTextPaneOutline(null, rows);
        }
        else
        {
            Editor.EditSmartArt(smartArtShape.Id, smartArt =>
            {
                LastSmartArtTextPaneApplyResult = SmartArtEditingPlanner.ApplyTextPaneOutline(
                    smartArt.Data,
                    rows);
                if (LastSmartArtTextPaneApplyResult is not { Applied: true })
                    return false;

                if (CommitSmartArtTextPaneMutation(smartArt, smartArtShape))
                    return true;

                LastSmartArtTextPaneApplyResult = LastSmartArtTextPaneApplyResult with
                {
                    Applied = false,
                    Message = "SmartArt native data or drawing cache refresh failed."
                };
                return false;
            });
        }

        if (LastSmartArtTextPaneApplyResult is { Applied: true })
        {
            _fileWorkflow.MarkDirty();
            RefreshCanvas();
            UpdateStatus();
        }

        RefreshSmartArtTextPane();
        return LastSmartArtTextPaneApplyResult!;
    }

    internal SmartArtNodeEditResult? ToggleSmartArtTextPaneAssistantForTests(string? modelId = null)
    {
        if (modelId is not null)
            _selectedSmartArtTextPaneModelId = modelId;
        return ToggleSmartArtTextPaneAssistant();
    }

    internal SmartArtNodeEditResult? ApplySmartArtTextPaneEditForTests(
        SmartArtNodeEditKind kind,
        string? modelId = null)
    {
        if (modelId is not null)
            _selectedSmartArtTextPaneModelId = modelId;
        return ApplySmartArtTextPaneAction(kind);
    }

    private SmartArtNodeEditResult? ApplySmartArtTextPaneAction(SmartArtNodeEditKind kind)
    {
        var targetId = _selectedSmartArtTextPaneModelId;
        if (string.IsNullOrWhiteSpace(targetId))
        {
            LastSmartArtTextPaneEditResult = SmartArtNodeEditResult.NotApplied(
                kind,
                targetId,
                "Select a SmartArt row first.");
            RefreshSmartArtTextPane();
            return LastSmartArtTextPaneEditResult;
        }

        var intent = kind switch
        {
            SmartArtNodeEditKind.AddSiblingAfter => SmartArtNodeEditIntent.AddSiblingAfter(
                targetId,
                SmartArtEditingPlanner.DefaultNewNodeText),
            SmartArtNodeEditKind.AddChild => SmartArtNodeEditIntent.AddChild(
                targetId,
                SmartArtEditingPlanner.DefaultNewNodeText),
            SmartArtNodeEditKind.Remove => SmartArtNodeEditIntent.Remove(targetId),
            SmartArtNodeEditKind.MoveUp => SmartArtNodeEditIntent.MoveUp(targetId),
            SmartArtNodeEditKind.MoveDown => SmartArtNodeEditIntent.MoveDown(targetId),
            SmartArtNodeEditKind.Promote => SmartArtNodeEditIntent.Promote(targetId),
            SmartArtNodeEditKind.Demote => SmartArtNodeEditIntent.Demote(targetId),
            SmartArtNodeEditKind.AddAssistant => SmartArtNodeEditIntent.AddAssistant(
                targetId,
                "Assistant"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported SmartArt text-pane action."),
        };
        return ApplySmartArtTextPaneEdit(intent);
    }

    private SmartArtNodeEditResult? ToggleSmartArtTextPaneAssistant()
    {
        var smartArtShape = GetSelectedSmartArtShape();
        var targetId = _selectedSmartArtTextPaneModelId;
        if (smartArtShape is null || string.IsNullOrWhiteSpace(targetId))
        {
            LastSmartArtTextPaneEditResult = SmartArtNodeEditResult.NotApplied(
                SmartArtNodeEditKind.ToggleAssistant,
                targetId,
                "Select a SmartArt hierarchy row first.");
        }
        else
        {
            LastSmartArtTextPaneEditResult = Editor.ToggleSmartArtAssistant(smartArtShape.Id, targetId);
        }

        if (LastSmartArtTextPaneEditResult is { Applied: true })
        {
            _fileWorkflow.MarkDirty();
            RefreshCanvas();
            UpdateStatus();
        }

        RefreshSmartArtTextPane();
        return LastSmartArtTextPaneEditResult;
    }

    internal SmartArtColorApplyResult ApplySmartArtColorPresetForTests(SmartArtColorPreset preset) =>
        ApplySmartArtColorPreset(preset);

    internal SmartArtLayoutApplyResult ApplySmartArtLayoutPresetForTests(SmartArtLayoutPreset preset) =>
        ApplySmartArtLayoutPreset(preset);

    internal SmartArtQuickStyleApplyResult ApplySmartArtQuickStylePresetForTests(SmartArtQuickStylePreset preset) =>
        ApplySmartArtQuickStylePreset(preset);

    private SmartArtLayoutApplyResult ApplySmartArtLayoutPreset(SmartArtLayoutPreset preset)
    {
        var smartArtShape = GetSelectedSmartArtShape();
        if (smartArtShape is null)
            return SmartArtAuthoringPlanner.ApplyLayoutPreset(null, preset);

        SmartArtLayoutApplyResult? result = null;
        Editor.EditSmartArt(smartArtShape.Id, smartArt =>
        {
            result = SmartArtAuthoringPlanner.ApplyLayoutPreset(smartArt, preset);
            if (result is not { Applied: true })
                return false;

            if (CommitSmartArtTextPaneMutation(smartArt, smartArtShape))
                return true;

            result = result with
            {
                Applied = false,
                Message = "SmartArt native data or drawing cache refresh failed."
            };
            return false;
        });

        if (result is { Applied: true })
        {
            _fileWorkflow.MarkDirty();
            RefreshCanvas();
            UpdateStatus();
        }

        return result ?? new SmartArtLayoutApplyResult(false, "No SmartArt layout was changed.", null, null, SmartArtFamily.Unknown);
    }

    private SmartArtQuickStyleApplyResult ApplySmartArtQuickStylePreset(SmartArtQuickStylePreset preset)
    {
        var smartArtShape = GetSelectedSmartArtShape();
        if (smartArtShape is null)
            return SmartArtAuthoringPlanner.ApplyQuickStylePreset(null, preset);

        SmartArtQuickStyleApplyResult? result = null;
        Editor.EditSmartArt(smartArtShape.Id, smartArt =>
        {
            result = SmartArtAuthoringPlanner.ApplyQuickStylePreset(smartArt, preset);
            if (result is not { Applied: true })
                return false;

            if (CommitSmartArtTextPaneMutation(smartArt, smartArtShape))
                return true;

            result = result with
            {
                Applied = false,
                Message = "SmartArt native data or drawing cache refresh failed."
            };
            return false;
        });

        if (result is { Applied: true })
        {
            _fileWorkflow.MarkDirty();
            RefreshCanvas();
            UpdateStatus();
        }

        return result ?? new SmartArtQuickStyleApplyResult(false, "No SmartArt Quick Style was changed.", null, null);
    }

    private SmartArtColorApplyResult ApplySmartArtColorPreset(SmartArtColorPreset preset)
    {
        var smartArtShape = GetSelectedSmartArtShape();
        if (smartArtShape is null)
        {
            LastSmartArtColorApplyResult = SmartArtAuthoringPlanner.ApplyColorPreset(
                null, preset, ResolveCurrentSlideTheme());
            return LastSmartArtColorApplyResult;
        }

        Editor.EditSmartArt(smartArtShape.Id, smartArt =>
        {
            LastSmartArtColorApplyResult = SmartArtAuthoringPlanner.ApplyColorPreset(
                smartArt,
                preset,
                ResolveCurrentSlideTheme(),
                Editor.CurrentSlide?.ColorMapOverride);
            if (LastSmartArtColorApplyResult is not { Applied: true })
                return false;

            if (CommitSmartArtTextPaneMutation(smartArt, smartArtShape))
                return true;

            LastSmartArtColorApplyResult = LastSmartArtColorApplyResult with
            {
                Applied = false,
                Message = "SmartArt native data or drawing cache refresh failed."
            };
            return false;
        });

        if (LastSmartArtColorApplyResult is { Applied: true })
        {
            _fileWorkflow.MarkDirty();
            RefreshCanvas();
            UpdateStatus();
        }

        return LastSmartArtColorApplyResult!;
    }

    internal SmartArtNodeEditResult? ApplySmartArtTextPaneKeyboardRouteForTests(
        SmartArtTextPaneShortcutKey key,
        SmartArtTextPaneShortcutModifiers modifiers,
        string? modelId = null)
    {
        if (modelId is not null)
            _selectedSmartArtTextPaneModelId = modelId;
        return ApplySmartArtTextPaneKeyboardRoute(key, modifiers);
    }

    private IReadOnlyList<SmartArtNodeOutlineItem> RefreshSmartArtTextPane()
    {
        var shape = GetSelectedSmartArtShape();
        var outline = SmartArtEditingPlanner.BuildOutline(shape?.SmartArt?.Data);
        if (_selectedSmartArtTextPaneModelId is null || outline.All(item =>
                !StringComparer.Ordinal.Equals(item.ModelId, _selectedSmartArtTextPaneModelId)))
        {
            _selectedSmartArtTextPaneModelId = outline.FirstOrDefault()?.ModelId;
        }

        RenderSmartArtTextPane(shape, outline);
        return outline;
    }

    private void RenderSmartArtTextPane(
        SlideShape? shape,
        IReadOnlyList<SmartArtNodeOutlineItem> outline)
    {
        _smartArtTextPaneRefreshing = true;
        try
        {
            _smartArtTextPaneRowsPanel.Children.Clear();
            _smartArtTextPaneHeading.Text = shape is null || string.IsNullOrWhiteSpace(shape.Name)
                ? "SmartArt Text Pane"
                : $"SmartArt Text Pane - {shape.Name}";
            _smartArtTextPaneMessage.Text = shape is null
                ? "Select a SmartArt graphic to edit its text outline."
                : outline.Count == 0
                    ? "The selected SmartArt graphic has no editable shared outline rows."
                    : "Rows mirror the shared SmartArt outline.";
            _smartArtTextPaneApplyButton.IsEnabled = shape is not null && outline.Count > 0;
            var selectedItem = outline.FirstOrDefault(item =>
                StringComparer.Ordinal.Equals(item.ModelId, _selectedSmartArtTextPaneModelId));
            _smartArtTextPaneAssistantButton.IsEnabled =
                shape?.SmartArt?.Data?.Family == SmartArtFamily.Hierarchy &&
                selectedItem is { Level: > 0 };
            foreach (var button in _smartArtTextPaneActionButtons)
                button.IsEnabled = shape is not null && selectedItem is not null;

            for (var index = 0; index < outline.Count; index++)
            {
                var item = outline[index];
                var row = BuildSmartArtTextPaneRow(item);
                PresentationPaneAccessibilityAdapter.ApplyItem(
                    row,
                    PresentationPaneAccessibilityPlanner.SmartArtTextPaneId,
                    index,
                    item.Text,
                    StringComparer.Ordinal.Equals(item.ModelId, _selectedSmartArtTextPaneModelId)
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
        var selected = StringComparer.Ordinal.Equals(item.ModelId, _selectedSmartArtTextPaneModelId);
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
        box.GotFocus += (_, _) => _selectedSmartArtTextPaneModelId = item.ModelId;
        box.KeyDown += (_, e) =>
        {
            if (_smartArtTextPaneRefreshing)
                return;

            if (!TryMapSmartArtTextPaneKey(e.Key, e.KeyModifiers, out var key, out var modifiers))
                return;

            _selectedSmartArtTextPaneModelId = item.ModelId;
            var result = ApplySmartArtTextPaneKeyboardRoute(key, modifiers);
            if (result is not null)
                e.Handled = true;
        };
        return box;
    }

    private SmartArtNodeEditResult? ApplySmartArtTextPaneKeyboardRoute(
        SmartArtTextPaneShortcutKey key,
        SmartArtTextPaneShortcutModifiers modifiers)
    {
        var route = SmartArtEditingPlanner.PlanTextPaneKeyboardRoute(
            key,
            modifiers,
            _selectedSmartArtTextPaneModelId);
        LastSmartArtTextPaneKeyboardRoute = route;
        if (route is null)
            return null;

        return ApplySmartArtTextPaneEdit(route.Intent);
    }

    private SmartArtNodeEditResult? ApplySmartArtTextPaneEdit(SmartArtNodeEditIntent intent)
    {

        var smartArtShape = GetSelectedSmartArtShape();
        if (smartArtShape is null)
        {
            LastSmartArtTextPaneEditResult = SmartArtEditingPlanner.Apply(null, intent);
        }
        else
        {
            Editor.EditSmartArt(smartArtShape.Id, smartArt =>
            {
                LastSmartArtTextPaneEditResult = SmartArtEditingPlanner.Apply(
                    smartArt.Data,
                    intent);
                if (LastSmartArtTextPaneEditResult is not { Applied: true })
                    return false;

                _selectedSmartArtTextPaneModelId = LastSmartArtTextPaneEditResult.SelectedModelId;
                if (CommitSmartArtTextPaneMutation(smartArt, smartArtShape))
                    return true;

                LastSmartArtTextPaneEditResult = LastSmartArtTextPaneEditResult with
                {
                    Applied = false,
                    Message = "SmartArt native data or drawing cache refresh failed."
                };
                return false;
            });
        }

        if (LastSmartArtTextPaneEditResult is { Applied: true })
        {
            _fileWorkflow.MarkDirty();
            RefreshCanvas();
            UpdateStatus();
        }

        RefreshSmartArtTextPane();
        return LastSmartArtTextPaneEditResult;
    }

    internal SmartArtNodeEditResult? ApplySmartArtTextPanePictureForTests(
        byte[] imageBytes,
        string contentType = "image/png",
        string? modelId = null)
    {
        if (modelId is not null)
            _selectedSmartArtTextPaneModelId = modelId;
        return ApplySmartArtTextPanePicture(imageBytes, contentType);
    }

    private async Task ReplaceSmartArtTextPanePictureFromFileAsync()
    {
        if (!AvaloniaFilePickerService.CanOpen(StorageProvider))
        {
            _statusText.Text = SisterAppFileTextPlanner.FormatCommandUnavailable("Replace SmartArt picture");
            return;
        }

        var file = await AvaloniaFilePickerService.PickSingleOpenFileAsync(
            StorageProvider,
            AvaloniaFilePickerOpenRequest.FromFileTypes(
                "Replace SmartArt picture",
                [PictureFileType]));
        if (file is null)
            return;

        try
        {
            await using var source = await file.OpenReadAsync();
            using var memory = new MemoryStream();
            await source.CopyToAsync(memory);
            ApplySmartArtTextPanePicture(
                memory.ToArray(),
                SlideObjectInsertionPlanner.InferPictureContentType(file.Name));
        }
        catch (Exception ex)
        {
            _statusText.Text = SisterAppFileTextPlanner.FormatCommandFailed(
                "Replace SmartArt picture",
                ex.Message);
        }
    }

    private SmartArtNodeEditResult? ApplySmartArtTextPanePicture(
        byte[] imageBytes,
        string contentType)
    {
        var smartArtShape = GetSelectedSmartArtShape();
        var targetId = _selectedSmartArtTextPaneModelId;
        if (smartArtShape is null || string.IsNullOrWhiteSpace(targetId))
        {
            LastSmartArtTextPaneEditResult = SmartArtNodeEditResult.NotApplied(
                SmartArtNodeEditKind.SetPicture,
                targetId,
                "Select a SmartArt row first.");
        }
        else
        {
            LastSmartArtTextPaneEditResult = Editor.ReplaceSmartArtNodePicture(
                smartArtShape.Id,
                targetId,
                imageBytes,
                contentType);
        }

        if (LastSmartArtTextPaneEditResult is { Applied: true })
        {
            _fileWorkflow.MarkDirty();
            RefreshCanvas();
            UpdateStatus();
        }

        RefreshSmartArtTextPane();
        return LastSmartArtTextPaneEditResult;
    }

    private void ClearSmartArtTextPanePicture()
    {
        var smartArtShape = GetSelectedSmartArtShape();
        var targetId = _selectedSmartArtTextPaneModelId;
        LastSmartArtTextPaneEditResult = smartArtShape is null || string.IsNullOrWhiteSpace(targetId)
            ? SmartArtNodeEditResult.NotApplied(
                SmartArtNodeEditKind.ClearPicture,
                targetId,
                "Select a SmartArt row first.")
            : Editor.ClearSmartArtNodePicture(smartArtShape.Id, targetId);

        if (LastSmartArtTextPaneEditResult.Applied)
        {
            _fileWorkflow.MarkDirty();
            RefreshCanvas();
            UpdateStatus();
        }
        RefreshSmartArtTextPane();
    }

    private bool CommitSmartArtTextPaneMutation(SmartArtShape smartArt, SlideShape smartArtShape)
    {
        LastSmartArtDataPartRewriteResult = SmartArtEditingPlanner.RewriteDataPart(smartArt);
        if (LastSmartArtDataPartRewriteResult is not { Applied: true })
            return false;

        LastSmartArtDrawingCacheRegenerationResult = SmartArtEditingPlanner.RegenerateDrawingCache(
            smartArt,
            smartArtShape.OffsetXEmu,
            smartArtShape.OffsetYEmu,
            smartArtShape.ExtentCxEmu,
            smartArtShape.ExtentCyEmu,
            ResolveCurrentSlideTheme(),
            Editor.CurrentSlide?.ColorMapOverride);
        return LastSmartArtDrawingCacheRegenerationResult is { Applied: true };
    }

    private SlideShape? GetSelectedSmartArtShape()
    {
        var selectedShapeId = GetSingleSelectedShapeId();
        if (selectedShapeId is null || Editor.CurrentSlide is not { } slide)
            return null;

        var shape = ShapeTreeLookup.Find(slide, selectedShapeId.Value);
        return shape?.Kind == SlideShapeKind.SmartArt && shape.SmartArt is not null
            ? shape
            : null;
    }

    private PresentationTheme ResolveCurrentSlideTheme()
    {
        var slide = Editor.CurrentSlide;
        var layout = slide is null
            ? null
            : _presentation.Layouts.FirstOrDefault(candidate =>
                StringComparer.Ordinal.Equals(candidate.Id, slide.LayoutId));
        var master = layout is null
            ? null
            : _presentation.Masters.FirstOrDefault(candidate =>
                StringComparer.Ordinal.Equals(candidate.Id, layout.MasterId));
        return master?.Theme ?? _presentation.Theme;
    }

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
                _selectedMediaCaptionTrackIndex = selectedTrackIndex;
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

    internal PresentationMediaCaptionTrackMutationResult ApplyMediaCaptionPane(
        PresentationMediaCaptionAuthoringIntentKind intent)
    {
        var media = PresentationMediaTranscriptPlanner
            .FindSelectedMediaShape(Editor.CurrentSlide, Editor.SelectedShapeIds)
            ?.Media;
        var descriptor = new PresentationMediaCaptionTrackAuthoringDescriptor(
            _mediaCaptionLabelBox.Text,
            _mediaCaptionLanguageBox.Text,
            _mediaCaptionSourceBox.Text,
            _mediaCaptionTranscriptBox.Text);
        LastMediaCaptionAuthoringMutationPlan =
            PresentationMediaTranscriptPlanner.BuildCaptionAuthoringMutationPlan(
                media,
                intent,
                _selectedMediaCaptionTrackIndex ?? -1,
                descriptor);
        LastMediaCaptionTrackMutationResult =
            Editor.ApplyMediaCaptionAuthoring(LastMediaCaptionAuthoringMutationPlan);
        if (LastMediaCaptionTrackMutationResult.Succeeded)
        {
            _selectedMediaCaptionTrackIndex = NormalizeMediaCaptionSelectionAfterMutation(
                media,
                intent,
                LastMediaCaptionTrackMutationResult.TrackIndex);
            _fileWorkflow.MarkDirty();
            RefreshReviewWorkflowPlans();
            UpdateStatus();
        }

        RefreshVisibleMediaCaptionPaneFromFields();
        return LastMediaCaptionTrackMutationResult;
    }

    private void RefreshMediaCaptionAuthoringPlans(
        string? proposedLabel,
        string? proposedLanguage,
        string? proposedSource,
        string? proposedTranscriptText)
    {
        LastMediaCaptionAuthoringPanePlan =
            PresentationMediaTranscriptPlanner.BuildCaptionAuthoringPanePlan(
                Editor.CurrentSlide,
                Editor.CurrentSlideIndex,
                Editor.SelectedShapeIds,
                _selectedMediaCaptionTrackIndex,
                proposedLabel,
                proposedLanguage,
                proposedSource,
                proposedTranscriptText);
        _selectedMediaCaptionTrackIndex = LastMediaCaptionAuthoringPanePlan.SelectedTrackIndex >= 0
            ? LastMediaCaptionAuthoringPanePlan.SelectedTrackIndex
            : null;
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
            _mediaCaptionPaneHeading.Text = string.IsNullOrWhiteSpace(plan.ShapeName)
                ? "Media Captions"
                : $"Media Captions - {plan.ShapeName}";
            _mediaCaptionPaneMessage.Text = plan.Message;
            RenderMediaCaptionTrackOptions(plan);
            RenderMediaCaptionField(_mediaCaptionLabelText, _mediaCaptionLabelBox, plan.Label);
            RenderMediaCaptionField(_mediaCaptionLanguageText, _mediaCaptionLanguageBox, plan.Language);
            RenderMediaCaptionField(_mediaCaptionSourceText, _mediaCaptionSourceBox, plan.Source);
            RenderMediaCaptionField(_mediaCaptionTranscriptText, _mediaCaptionTranscriptBox, plan.TranscriptText);
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
                Content = $"{track.TrackIndex + 1}. {track.Label} ({FormatAvailability(!track.IsExternal)})",
                Tag = track.TrackIndex,
            };
            PresentationPaneAccessibilityAdapter.ApplyItem(
                item,
                PresentationPaneAccessibilityPlanner.MediaCaptionPaneId,
                itemIndex,
                track.Label,
                track.TrackIndex == plan.SelectedTrackIndex ? "Selected" : "Not selected");
            _mediaCaptionTrackBox.Items.Add(item);
        }
        _mediaCaptionTrackBox.IsEnabled = plan.Tracks.Count > 0;
        var selectedIndex = -1;
        for (var index = 0; index < plan.Tracks.Count; index++)
        {
            if (plan.Tracks[index].TrackIndex == plan.SelectedTrackIndex)
            {
                selectedIndex = index;
                break;
            }
        }

        _mediaCaptionTrackBox.SelectedIndex = selectedIndex;
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

    private static int? NormalizeMediaCaptionSelectionAfterMutation(
        MediaInfo? media,
        PresentationMediaCaptionAuthoringIntentKind intent,
        int changedTrackIndex)
    {
        if (media is null || media.CaptionTracks.Count == 0)
            return null;

        return intent == PresentationMediaCaptionAuthoringIntentKind.Delete
            ? Math.Min(changedTrackIndex, media.CaptionTracks.Count - 1)
            : changedTrackIndex;
    }

    internal PresentationReadingOrderPlan ShowReadingOrderPane()
    {
        var plan = RefreshReadingOrderPlan();
        RenderReadingOrderPane(plan);
        _readingOrderPaneHost.IsVisible = true;
        RefreshPaneAccessibilityMetadata();
        return plan;
    }

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
    {
        var plan = _reviewWorkflowSession.SelectReadingOrderItem(shapeId);
        if (IsReadingOrderPaneVisible && LastReadingOrderPlan is not null)
            RenderReadingOrderPane(LastReadingOrderPlan);
        return plan;
    }

    private PresentationReadingOrderMutationPlan ApplyReadingOrderMove(
        PresentationReviewWorkflowIntentKind intent)
    {
        var plan = _reviewWorkflowSession.ApplyReadingOrderMove(intent);
        if (IsReadingOrderPaneVisible && LastReadingOrderPlan is not null)
            RenderReadingOrderPane(LastReadingOrderPlan);
        return plan;
    }

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

            _altTextPaneHeading.Text = string.IsNullOrWhiteSpace(plan.ShapeName)
                ? "Alt Text"
                : $"Alt Text - {plan.ShapeName}";
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
        _readingOrderPaneHeading.Text =
            $"Reading Order - slide {plan.SlideIndex + 1} ({plan.Items.Count} shapes)";
        _readingOrderPaneMessage.Text = plan.SelectedItem is { } selected
            ? $"Selected: {selected.ShapeName}"
            : plan.Items.Count == 0
                ? PresentationReviewWorkflowPlanner.EmptyReadingOrderMessage
                : PresentationReviewWorkflowPlanner.MissingReadingOrderSelectionMessage;

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
                    Text = $"{item.ReadingOrderIndex + 1}. {item.ShapeName}",
                    FontSize = PresentationReadingOrderPaneVisualMetrics.BodyFontSize,
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = $"{item.ShapeTypeLabel} - depth {item.NestingDepth}",
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
                    Text = BuildReadingOrderAltTextLine(item),
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
                Text = "Selected item",
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
        ToolTip.SetTip(button, $"Select {item.ShapeName}");
        button.Click += (_, _) => ApplyReadingOrderSelectItem(item.ShapeId);
        return button;
    }

    private static string BuildReadingOrderAltTextLine(PresentationReadingOrderItemPlan item)
    {
        if (item.IsDecorative)
            return "Decorative object";

        if (string.IsNullOrWhiteSpace(item.AlternativeTextTitle)
            && string.IsNullOrWhiteSpace(item.AlternativeTextDescription))
        {
            return "Alt text: missing";
        }

        if (string.IsNullOrWhiteSpace(item.AlternativeTextDescription))
            return $"Alt text title: {item.AlternativeTextTitle}";

        if (string.IsNullOrWhiteSpace(item.AlternativeTextTitle))
            return $"Alt text: {item.AlternativeTextDescription}";

        return $"Alt text: {item.AlternativeTextTitle} - {item.AlternativeTextDescription}";
    }

    private static void SetTextIfChanged(TextBox textBox, string value)
    {
        if (textBox.Text != value)
            textBox.Text = value;
    }

    private uint? GetSingleSelectedShapeId()
        => Editor.SelectedShapeIds.Count == 1
            ? Editor.SelectedShapeIds[0]
            : null;

    private PresentationReadingOrderPlan RefreshReadingOrderPlan()
        => _reviewWorkflowSession.RefreshReadingOrderPlan();

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
    {
        var plan = _reviewWorkflowSession.ShowProofingPane();
        RenderProofingPane(plan);
        _proofingPaneHost.IsVisible = true;
        RefreshPaneAccessibilityMetadata();
        return plan;
    }

    internal PresentationProofingPanePlan SelectProofingIssueRow(int rowIndex)
    {
        var plan = _reviewWorkflowSession.SelectProofingIssueRow(rowIndex);
        RenderProofingPane(plan);
        _proofingPaneHost.IsVisible = true;
        RefreshPaneAccessibilityMetadata();
        return plan;
    }

    internal PresentationProofingCorrectionMutationPlan ApplySelectedProofingCorrection()
    {
        if (LastProofingPanePlan is null)
            ShowProofingPane();
        return _reviewWorkflowSession.ApplySelectedProofingCorrection();
    }

    internal PresentationProofingPanePlan IgnoreSelectedProofingIssue()
    {
        if (LastProofingPanePlan is null)
            ShowProofingPane();
        return _reviewWorkflowSession.IgnoreSelectedProofingIssue();
    }

    internal PresentationProofingPanePlan IgnoreAllSelectedProofingIssues()
    {
        if (LastProofingPanePlan is null)
            ShowProofingPane();
        return _reviewWorkflowSession.IgnoreAllSelectedProofingIssues();
    }

    internal PresentationProofingPanePlan AddSelectedProofingWordToDictionary()
    {
        if (LastProofingPanePlan is null)
            ShowProofingPane();
        return _reviewWorkflowSession.AddSelectedProofingWordToDictionary();
    }

    private void RenderProofingPaneIfVisible(PresentationProofingPanePlan plan)
    {
        if (IsProofingPaneVisible)
            RenderProofingPane(plan);
    }

    private void RenderProofingPane(PresentationProofingPanePlan plan)
    {
        _proofingPaneHeading.Text = $"Spelling - {plan.IssueCount} issues";
        _proofingPaneMessage.Text = plan.SelectedRow is { } selected
            ? $"{selected.SlideDisplay}: change \"{selected.Text}\" to \"{selected.SuggestedReplacement}\""
            : plan.Message;

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

    private async Task<bool> TryLoadPresentationFileAsync(string path)
    {
        try
        {
            var result = PresentationFilePersistenceWorkflow.Open(path);
            LoadPresentationAsSaved(result.Presentation, result.SavedPath, result.SuppressRecentFiles);
            _statusText.Text = SisterAppFileTextPlanner.FormatOpened(Path.GetFileName(path));
            return true;
        }
        catch (Exception ex)
        {
            _statusText.Text = SisterAppFileTextPlanner.FormatCommandFailed(SisterAppFileTextPlanner.OpenCommand, ex.Message);
            await _fileWorkflow.ShowFileCommandErrorAsync("Could not open the presentation", ex);
            return false;
        }
    }

    private async Task<bool> TrySavePresentationFileAsync(string path)
    {
        try
        {
            var result = PresentationFilePersistenceWorkflow.Save(path, _presentation);
            _fileWorkflow.MarkSavedWithPath(result.SavedPath, result.SuppressRecentFiles);
            _statusText.Text = SisterAppFileTextPlanner.FormatSaved(Path.GetFileName(result.SavedPath));
            return true;
        }
        catch (Exception ex)
        {
            _statusText.Text = SisterAppFileTextPlanner.FormatCommandFailed(SisterAppFileTextPlanner.SaveCommand, ex.Message);
            await _fileWorkflow.ShowFileCommandErrorAsync("Could not save the presentation", ex);
            return false;
        }
    }

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
        _findReplaceDialog?.Close();
        _presentation = presentation;

        RebuildEditorAndRewireInteraction();
        // A visible pane is a projection of the active editor. Rebind it after New/Open so
        // rows and playback state cannot remain attached to the previous presentation.
        _selectedAnimationIndex = -1;
        _animationPanePlaybackSessionPlan = null;
        _animationPanePlaybackWorkflowEvidencePlan = null;
        HideLayoutPicker();
        HideTablePicker();
        RefreshSlidePane();
        RefreshCanvas();
        RefreshNotesPane();
        RefreshReviewWorkflowPlans();
        RefreshVisibleReviewCommentsPane();
        RefreshVisibleAnimationPane();
        UpdateStatus();
    }

    // ── Canvas refresh ─────────────────────────────────────────────────────────

    private void RefreshCanvas()
    {
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
            _slidePaneRenderedThumbnailPlans.Clear();
            _slidePaneRenderedSectionHeaderPlans.Clear();

            _slidePaneSessionState = SlidePanePlanner.SetSelectedSlide(
                _slidePaneSessionState,
                Editor.CurrentSlideIndex);
            _slidePaneProjection = SlidePanePlanner.BuildSessionProjection(
                _presentation.Slides,
                _presentation.Sections,
                _slidePaneSessionState);
            var accessibilityOrdinal = 0;
            foreach (var entry in _slidePaneProjection.Entries)
            {
                if (entry.Kind == SlidePaneEntryKind.SectionHeader)
                {
                    _slidePaneList.Items.Add(BuildSlidePaneSectionHeader(entry, accessibilityOrdinal++));
                    continue;
                }

                var slide = _presentation.Slides[entry.SlideIndex];
                var plan = SlidePanePlanner.BuildThumbnailVisualPlan(
                    entry,
                    slide,
                    _slidePaneProjection.SelectedSlideIndex);
                _slidePaneRenderedThumbnailPlans.Add(plan);

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
                    accessibilityOrdinal++,
                    plan.AccessibleName,
                    plan.IsSelected ? "Selected" : "Not selected",
                    $"Slide{plan.SlideIndex + 1}");
                ToolTip.SetTip(item, plan.ToolTipText);
                WireContextMenuLifecycle(item);
                item.KeyDown += OnSlidePaneItemKeyDown;
                item.PointerEntered += (_, _) =>
                {
                    if (item.Tag is int idx && idx != Editor.CurrentSlideIndex)
                        itemChrome.Background = BrushFromHex(plan.ItemHoverBackgroundHex);
                };
                item.PointerExited += (_, _) =>
                {
                    if (item.Tag is int idx)
                        itemChrome.Background = BrushFromHex(idx == Editor.CurrentSlideIndex
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
                Editor.CurrentSlideIndex);
            SelectSlidePaneItem(Editor.CurrentSlideIndex);
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
        SlidePaneEntry entry,
        int accessibilityOrdinal)
    {
        var plan = SlidePanePlanner.BuildSectionHeaderVisualPlan(entry);
        _slidePaneRenderedSectionHeaderPlans.Add(plan);
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
            accessibilityOrdinal,
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
            command => ApplyContextMenuCommandAsync(command, slideIndex, sectionIndex: -1));

        return menu;
    }

    internal ContextMenu BuildSlidePaneContextMenuForTests(int slideIndex) =>
        BuildSlidePaneContextMenu(slideIndex);

    internal bool TryApplySlidePaneContextAction(int slideIndex, SlidePaneActionKind kind)
    {
        var action = kind == SlidePaneActionKind.ToggleHiddenSlide
            ? SlidePanePlanner.BuildHiddenSlideAction(_presentation.Slides, slideIndex)
            : SlidePanePlanner.BuildContextActions(_presentation.Slides.Count, slideIndex)
                .FirstOrDefault(candidate => candidate.Kind == kind);

        return action is not null && SlidePanePlanner.TryApplyAction(Editor, action);
    }

    private ContextMenu BuildSlidePaneSectionContextMenu(SlidePaneEntry entry)
    {
        var menu = new ContextMenu();

        AddContextMenuEntries(
            menu,
            FreePContextMenuCatalog.BuildSectionHeaderMenu(
                _presentation.Sections,
                entry.SectionIndex,
                entry.SlideIndex),
            command => ApplyContextMenuCommandAsync(command, entry.SlideIndex, entry.SectionIndex));

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
            item.Click += async (_, _) => await execute(entry.Command!.Value);
            menu.Items.Add(item);
        }
    }

    private async Task ApplyContextMenuCommandAsync(
        FreePContextMenuCommand command,
        int slideIndex,
        int sectionIndex)
    {
        if (command is FreePContextMenuCommand.NewSlide or
            FreePContextMenuCommand.DuplicateSlide or
            FreePContextMenuCommand.DeleteSlide or
            FreePContextMenuCommand.ToggleHiddenSlide)
        {
            var kind = command switch
            {
                FreePContextMenuCommand.NewSlide => SlidePaneActionKind.InsertAfterSlide,
                FreePContextMenuCommand.DuplicateSlide => SlidePaneActionKind.DuplicateSlide,
                FreePContextMenuCommand.DeleteSlide => SlidePaneActionKind.DeleteSlide,
                _ => SlidePaneActionKind.ToggleHiddenSlide,
            };
            TryApplySlidePaneContextAction(slideIndex, kind);
            return;
        }

        var sectionActionKind = command switch
        {
            FreePContextMenuCommand.AddSection => SlideSectionActionKind.AddSection,
            FreePContextMenuCommand.RenameSection => SlideSectionActionKind.RenameSection,
            FreePContextMenuCommand.RemoveSection => SlideSectionActionKind.RemoveSection,
            FreePContextMenuCommand.RemoveAllSections => SlideSectionActionKind.RemoveAllSections,
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
        };
        var actions = sectionActionKind == SlideSectionActionKind.AddSection
            ? SlideSectionPlanner.BuildSlideContextActions(
                _presentation.Slides,
                _presentation.Sections,
                slideIndex)
            : SlideSectionPlanner.BuildSectionHeaderActions(
                _presentation.Sections,
                sectionIndex,
                slideIndex);
        await ApplySlideSectionActionAsync(actions.Single(candidate => candidate.Kind == sectionActionKind));
    }

    private async Task ApplySlideSectionActionAsync(SlideSectionActionPlan action)
    {
        var execution = SlideSectionPlanner.BuildExecutionPlan(action);
        if (!execution.IsEnabled)
            return;

        string? promptedName = null;
        if (execution.RequiresNamePrompt)
        {
            promptedName = await PromptSectionNameAsync(execution.PromptTitle, execution.SuggestedName);
            if (promptedName is null)
                return;
        }

        SlideSectionPlanner.TryApplyAction(Editor, execution, promptedName);
    }

    private void ToggleSlidePaneSection(string sectionId)
    {
        if (string.IsNullOrWhiteSpace(sectionId))
            return;

        _slidePaneSessionState = SlidePanePlanner.ToggleSection(_slidePaneSessionState, sectionId);

        RefreshSlidePane();
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
        var action = kind == SlideSectionActionKind.AddSection
            ? SlideSectionPlanner.BuildSlideContextActions(
                    _presentation.Slides,
                    _presentation.Sections,
                    slideIndex)
                .SingleOrDefault(candidate => candidate.Kind == kind)
            : SlideSectionPlanner.BuildSectionHeaderActions(
                    _presentation.Sections,
                    sectionIndex,
                    slideIndex)
                .SingleOrDefault(candidate => candidate.Kind == kind);

        if (action is null)
            return false;

        var execution = SlideSectionPlanner.BuildExecutionPlan(action);
        return SlideSectionPlanner.TryApplyAction(Editor, execution, promptedName);
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

        _slidePaneSessionState = _slidePaneSessionState with
        {
            DragSession = SlidePanePlanner.BeginDragSession(
                sourceSlideIndex,
                e.GetPosition(item).Y)
        };
        // Match WPF: a left-click selects the thumbnail before a possible drag
        // starts, so a click-and-hold always operates on the clicked slide.
        Editor.SelectSlide(sourceSlideIndex);
    }

    private void OnSlidePaneItemPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not ListBoxItem item || !_slidePaneSessionState.DragSession.IsTracking)
            return;

        var point = e.GetCurrentPoint(item);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        var update = SlidePanePlanner.UpdateDragSession(
            _slidePaneSessionState.DragSession,
            GetSlidePaneItemKinds(),
            e.GetPosition(item).Y,
            e.GetPosition(_slidePaneList).Y,
            SlidePanePlanner.DefaultSlideItemHeight);
        _slidePaneSessionState = _slidePaneSessionState with { DragSession = update.State };
        if (!_slidePaneSessionState.DragSession.IsDragging)
            return;

        if (update.ShouldCapturePointer)
            e.Pointer.Capture(item);

        ShowSlidePaneInsertionIndicator(update.DropVisualPlan);
        e.Handled = true;
    }

    private void OnSlidePaneItemPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var completion = SlidePanePlanner.CompleteDragSession(
            _slidePaneSessionState.DragSession,
            _presentation.Slides.Count);
        _slidePaneSessionState = _slidePaneSessionState with { DragSession = completion.State };

        if (!completion.ShouldReleaseCapture)
        {
            return;
        }

        e.Pointer.Capture(null);
        HideSlidePaneInsertionIndicator();

        SlidePanePlanner.TryApplyAction(Editor, completion.Action);
        e.Handled = true;
    }

    private void OnSlidePaneItemPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _slidePaneSessionState = _slidePaneSessionState with
        {
            DragSession = SlidePanePlanner.CancelDragSession(_slidePaneSessionState.DragSession)
        };
        HideSlidePaneInsertionIndicator();
    }

    internal bool TryApplySlidePaneMove(int sourceSlideIndex, int targetInsertionIndex)
    {
        var action = SlidePanePlanner.PlanMoveAction(
            _presentation.Slides.Count,
            sourceSlideIndex,
            targetInsertionIndex);

        return SlidePanePlanner.TryApplyAction(Editor, action);
    }

    internal SlidePaneDropVisualPlan PreviewSlidePaneDragForTests(
        int sourceSlideIndex,
        double startPointerY,
        double pointerYWithinItem,
        double pointerYWithinPane)
    {
        _slidePaneSessionState = _slidePaneSessionState with
        {
            DragSession = SlidePanePlanner.BeginDragSession(sourceSlideIndex, startPointerY)
        };
        var update = SlidePanePlanner.UpdateDragSession(
            _slidePaneSessionState.DragSession,
            GetSlidePaneItemKinds(),
            pointerYWithinItem,
            pointerYWithinPane,
            SlidePanePlanner.DefaultSlideItemHeight);
        _slidePaneSessionState = _slidePaneSessionState with { DragSession = update.State };
        if (update.State.IsDragging)
            ShowSlidePaneInsertionIndicator(update.DropVisualPlan);
        else
            HideSlidePaneInsertionIndicator();

        return update.DropVisualPlan;
    }

    internal bool CompleteSlidePaneDragForTests()
    {
        var completion = SlidePanePlanner.CompleteDragSession(
            _slidePaneSessionState.DragSession,
            _presentation.Slides.Count);
        _slidePaneSessionState = _slidePaneSessionState with { DragSession = completion.State };
        HideSlidePaneInsertionIndicator();
        return completion.ShouldReleaseCapture &&
            SlidePanePlanner.TryApplyAction(Editor, completion.Action);
    }

    internal bool TryApplySlidePaneKeyboardAction(SlidePaneKeyboardIntentKind intent)
    {
        var action = SlidePanePlanner.BuildKeyboardAction(
            _presentation.Slides.Count,
            Editor.CurrentSlideIndex,
            intent);

        return SlidePanePlanner.TryApplyAction(Editor, action);
    }

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
        var plan = SlidePanePlanner.BuildBottomNewSlideAffordance(
            _presentation.Slides.Count,
            Editor.CurrentSlideIndex);
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
        SlidePanePlanner.TryApplyBottomNewSlideAffordance(Editor);

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

    private IReadOnlyList<bool> GetSlidePaneItemKinds() =>
        _slidePaneProjection?.PaneItemIsSlide ?? Array.Empty<bool>();

    private static IBrush BrushFromHex(string hex) =>
        new SolidColorBrush(Color.Parse(hex));

    private void SelectSlidePaneItem(int slideIndex)
    {
        var itemIndex = 0;
        foreach (var item in _slidePaneList.Items)
        {
            if (item is ListBoxItem { Tag: int itemSlideIndex } && itemSlideIndex == slideIndex)
            {
                _slidePaneList.SelectedIndex = itemIndex;
                return;
            }

            itemIndex++;
        }

        _slidePaneList.SelectedIndex = -1;
    }

    private ListBoxItem? GetCurrentSlidePaneItem() =>
        _slidePaneList.Items
            .OfType<ListBoxItem>()
            .FirstOrDefault(item => item.Tag is int slideIndex &&
                slideIndex == Editor.CurrentSlideIndex);

    private void RestoreSlidePaneFocusAfterRefresh()
    {
        if (!_restoreSlidePaneFocusAfterRefresh)
            return;

        _restoreSlidePaneFocusAfterRefresh = false;
        GetCurrentSlidePaneItem()?.Focus();
    }

    private void UpdateSlidePaneItemChrome()
    {
        foreach (var item in _slidePaneList.Items.OfType<ListBoxItem>())
        {
            if (item.Tag is not int slideIndex || item.Content is not Border chrome)
                continue;

            var plan = _slidePaneRenderedThumbnailPlans.FirstOrDefault(p => p.SlideIndex == slideIndex);
            if (plan is null)
                continue;

            var selected = slideIndex == Editor.CurrentSlideIndex;
            chrome.Background = BrushFromHex(selected ? plan.ItemSelectedBackgroundHex : plan.ItemNormalBackgroundHex);
            chrome.BorderBrush = BrushFromHex(selected ? plan.ItemSelectedBorderHex : plan.ItemNormalBorderHex);
            chrome.BorderThickness = new Thickness(selected ? plan.SelectedBorderThickness : plan.NormalBorderThickness);
            AutomationProperties.SetName(item, plan.AccessibleName);
            PresentationPaneAccessibilityAdapter.ApplyItem(
                item,
                PresentationPaneAccessibilityPlanner.SlidePaneId,
                GetAccessibilityOrdinalForSlide(slideIndex),
                plan.AccessibleName,
                selected ? "Selected" : "Not selected",
                $"Slide{slideIndex + 1}");
        }
        RefreshPaneAccessibilityMetadata();
    }

    private int GetAccessibilityOrdinalForSlide(int slideIndex)
    {
        if (_slidePaneProjection is null)
            return slideIndex;

        for (var ordinal = 0; ordinal < _slidePaneProjection.Entries.Count; ordinal++)
        {
            var entry = _slidePaneProjection.Entries[ordinal];
            if (entry.Kind == SlidePaneEntryKind.Slide && entry.SlideIndex == slideIndex)
                return ordinal;
        }

        return slideIndex;
    }

    private void OnSlidePaneSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_slidePaneRefreshing)
            return;

        if (_slidePaneList.SelectedItem is not ListBoxItem { Tag: int idx })
            return;

        if (idx < 0 || idx >= _presentation.Slides.Count)
            return;

        Editor.SelectSlide(idx);
    }

    // ── Notes pane ─────────────────────────────────────────────────────────────

    private void RefreshNotesPane()
    {
        _notesRefreshing = true;
        try
        {
            LastNotesPagePreviewPlan = PresentationNotesPagePreviewPlanner.Build(
                _presentation,
                Editor.CurrentSlideIndex);
            _notesBox.Text = FormatNotesText(Editor.CurrentSlideNotes);
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
        var text = _notesBox.Text ?? string.Empty;
        if (string.Equals(text, FormatNotesText(Editor.CurrentSlideNotes), StringComparison.Ordinal))
            return;

        Editor.SetCurrentSlideNotesText(text);
        LastNotesPagePreviewPlan = PresentationNotesPagePreviewPlanner.Build(
            _presentation,
            Editor.CurrentSlideIndex);
        RefreshPaneAccessibilityMetadata();
    }

    // ── Event handlers ─────────────────────────────────────────────────────────

    private void OnEditorChanged()
    {
        _startupDirtyTrace?.Record("editor-changed-before-mark", _fileWorkflow);
        _fileWorkflow.MarkDirty();
        _startupDirtyTrace?.Record("editor-changed", _fileWorkflow);
        SyncRibbonCommandStates();
        RefreshSlidePane();
        RefreshCanvas(); // refresh canvas so shape moves/resizes are reflected immediately
        RefreshNotesPane();
        RefreshReviewWorkflowPlans();
        if (IsSmartArtTextPaneVisible)
            ShowSmartArtTextPane();
        RefreshVisibleAnimationPane(_selectedAnimationIndex);
        _selectionPane?.Refresh();
        RefreshPaneAccessibilityMetadata();
        UpdateStatus();
    }

    private void OnCurrentSlideChanged(object? sender, EventArgs e)
    {
        _startupDirtyTrace?.Record("current-slide-changed", _fileWorkflow);
        _slidePaneSessionState = SlidePanePlanner.SetSelectedSlide(
            _slidePaneSessionState,
            Editor.CurrentSlideIndex);
        _reviewWorkflowSession.SelectedCommentIndex = null;
        _selectedAnimationIndex = -1;
        _selectedMediaCaptionTrackIndex = null;
        SyncRibbonCommandStates();

        // Sync slide-pane selection without re-triggering OnSlidePaneSelectionChanged.
        _slidePaneRefreshing = true;
        try { SelectSlidePaneItem(Editor.CurrentSlideIndex); }
        finally { _slidePaneRefreshing = false; }

        UpdateSlidePaneItemChrome();
        RefreshCanvas();
        RefreshNotesPane();
        RefreshReviewWorkflowPlans();
        RefreshVisibleReviewCommentsPane();
        RefreshVisibleMediaCaptionPaneFromFields();
        RefreshVisibleAnimationPane();
        _selectionPane?.Refresh();
        RefreshPaneAccessibilityMetadata();
        UpdateStatus();
    }

    private void SyncRibbonCommandStates()
    {
        if (_ribbonControl is not null)
        {
            AvaloniaRibbonRenderer.SyncToggleStates(
                _ribbonControl,
                _ribbonCommandRegistry,
                RibbonVisualPalette.FromTheme(App.ActiveTheme));
        }
    }

    private void OnEditorSelectionChanged(object? sender, EventArgs e)
    {
        RefreshAltTextRequestPlan();
        RefreshReadingOrderPlan();
        if (IsAltTextPaneVisible)
            ShowAltTextPane();
        if (IsSmartArtTextPaneVisible)
            ShowSmartArtTextPane();
        RefreshVisibleMediaCaptionPaneFromFields();
        RefreshVisibleAnimationPane();
        _selectionPane?.Refresh();
        RefreshPaneAccessibilityMetadata();
    }

    private static string FormatNotesText(TextBody? notes) => notes is null
        ? string.Empty
        : string.Join(
            Environment.NewLine,
            notes.Paragraphs.Select(p => string.Concat(p.Runs.Select(r => r.Text))));

    private void OnFileWorkflowChanged()
    {
        _startupDirtyTrace?.Record("file-workflow-changed", _fileWorkflow);
        UpdateStatus();
    }

    // ── Status ─────────────────────────────────────────────────────────────────

    private void UpdateStatus()
    {
        var count   = _presentation.Slides.Count;
        var current = Editor.CurrentSlideIndex;
        _statusText.Text = SisterAppStatusBarTextPlanner.FormatPresentationSlideStatus(
            current,
            count,
            ResolveDataFolderLabel());
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
                ExecuteKeyboardCommand))
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

    private void ExecuteKeyboardCommand(FreePKeyboardCommand command)
    {
        switch (command)
        {
            case FreePKeyboardCommand.NewPresentation: FileNew(); break;
            case FreePKeyboardCommand.OpenPresentation: _ = FileOpenAsync(); break;
            case FreePKeyboardCommand.SavePresentation: _ = FileSaveAsync(); break;
            case FreePKeyboardCommand.SavePresentationAs: _ = FileSaveAsAsync(); break;
            case FreePKeyboardCommand.PrintPresentation: ShowPrintBackstage(); break;
            case FreePKeyboardCommand.Undo: Editor.Undo(); break;
            case FreePKeyboardCommand.Redo: Editor.Redo(); break;
            case FreePKeyboardCommand.DeleteSelectedShapes: Editor.DeleteSelected(); break;
            case FreePKeyboardCommand.DuplicateCurrentSlide: Editor.DuplicateCurrentSlide(); break;
            case FreePKeyboardCommand.StartSlideShowFromBeginning: StartSlideShow(fromStart: true); break;
            case FreePKeyboardCommand.StartSlideShowFromCurrentSlide: StartSlideShow(fromStart: false); break;
            case FreePKeyboardCommand.Copy:
                QueueClipboardCopy();
                break;
            case FreePKeyboardCommand.Cut:
                QueueClipboardCut();
                break;
            case FreePKeyboardCommand.Paste:
                QueueClipboardPaste();
                break;
            case FreePKeyboardCommand.Find: OpenFindDialog(); break;
            case FreePKeyboardCommand.Replace: OpenFindReplaceDialog(); break;
            case FreePKeyboardCommand.SelectAll: Editor.SelectAll(); break;
            default: throw new ArgumentOutOfRangeException(nameof(command), command, null);
        }
    }

    private void QueueClipboardCopy()
    {
        var request = _clipboardService.PrepareWrite(Editor);
        QueueClipboardOperation(() => _clipboardService.ExecuteCopyAsync(request));
    }

    private void QueueClipboardCut()
    {
        var request = _clipboardService.PrepareWrite(Editor);
        QueueClipboardOperation(() => _clipboardService.ExecuteCutAsync(request));
    }

    private void QueueClipboardPaste()
    {
        var request = _clipboardService.PreparePaste(Editor);
        QueueClipboardOperation(() => _clipboardService.ExecutePasteAsync(request));
    }

    private void QueueClipboardOperation(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        _clipboardOperation = RunClipboardOperationAsync(_clipboardOperation, operation);
    }

    private static async Task RunClipboardOperationAsync(Task preceding, Func<Task> operation)
    {
        try
        {
            await preceding;
        }
        catch
        {
            // A failed adapter operation must not prevent later clipboard commands.
        }

        await operation();
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
        if (_presentation.Slides.Count == 0)
            return; // nothing to show

        var choiceId = fromStart
            ? SlideShowCustomShowPlanner.FullPresentationChoiceId
            : SlideShowCustomShowPlanner.FromCurrentSlideChoiceId;
        if (!SlideShowCustomShowPlanner.TryBuildRouteForLaunchChoice(
                _presentation,
                choiceId,
                Editor.CurrentSlideIndex,
                out var route))
        {
            return;
        }

        if (animationStartIndex is int selectedAnimationIndex)
            route = route.WithAnimationStartIndex(selectedAnimationIndex);

        var slideShow = new SlideShowWindow(_presentation, route, Editor.SetSlideNotesText);
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

    internal bool TryBuildCustomSlideShowRoute(
        string? customShowName,
        int startIndex,
        out SlideShowPlaybackRoute route) =>
        SlideShowCustomShowPlanner.TryBuildNamedCustomShowRoute(
            _presentation,
            customShowName,
            startIndex,
            out route);

    internal SlideShowLaunchPlan BuildSlideShowLaunchPlan() =>
        SlideShowCustomShowPlanner.BuildLaunchPlan(_presentation, Editor.CurrentSlideIndex);

    internal SlideShowCustomShowAuthoringPlan BuildCustomShowAuthoringPlan() =>
        SlideShowCustomShowPlanner.BuildAuthoringPlan(_presentation);

    internal SlideShowCustomShowSessionPlan BuildCustomShowSessionPlan(
        SlideShowCustomShowSessionState state) =>
        SlideShowCustomShowSessionPlanner.BuildPlan(
            BuildCustomShowAuthoringPlan(),
            state);

    internal SlideShowCustomShowMutationResult CreateCustomShow(
        string? name,
        IEnumerable<string?> slideIds) =>
        Editor.ApplyCustomShowMutation(presentation =>
            SlideShowCustomShowPlanner.CreateCustomShow(presentation, name, slideIds));

    internal SlideShowCustomShowMutationResult RenameCustomShow(
        int customShowIndex,
        string? name) =>
        Editor.ApplyCustomShowMutation(presentation =>
            SlideShowCustomShowPlanner.RenameCustomShow(presentation, customShowIndex, name));

    internal SlideShowCustomShowMutationResult DeleteCustomShow(int customShowIndex) =>
        Editor.ApplyCustomShowMutation(presentation =>
            SlideShowCustomShowPlanner.DeleteCustomShow(presentation, customShowIndex));

    internal SlideShowCustomShowMutationResult UpdateCustomShowSlides(
        int customShowIndex,
        IEnumerable<string?> slideIds) =>
        Editor.ApplyCustomShowMutation(presentation =>
            SlideShowCustomShowPlanner.UpdateCustomShowSlides(presentation, customShowIndex, slideIds));

    internal SlideShowCustomShowMutationResult MoveCustomShowSlide(
        int customShowIndex,
        int sourceSlideIndex,
        string? sourceSlideId,
        int targetSlideIndex) =>
        Editor.ApplyCustomShowMutation(presentation =>
            SlideShowCustomShowPlanner.MoveCustomShowSlide(
                presentation,
                customShowIndex,
                sourceSlideIndex,
                sourceSlideId,
                targetSlideIndex));

    internal bool TryStartCustomSlideShow(string? customShowName, int startIndex = 0)
    {
        if (!TryBuildCustomSlideShowRoute(customShowName, startIndex, out var route) ||
            route.SlideCount == 0)
        {
            return false;
        }

        var slideShow = new SlideShowWindow(_presentation, route, Editor.SetSlideNotesText);
        // A named custom show is still a separate playback window. Restore the
        // editor's focus when it closes just like the normal slideshow route.
        slideShow.Closed += (_, _) => RestoreOwnerFocus();

        if (IsVisible)
            slideShow.Show(this);
        else
            slideShow.Show();
        return true;
    }

    internal async void OpenCustomShowDialog()
    {
        await OpenCustomShowDialogAsync();
    }

    internal Task OpenCustomShowDialogAsyncForTests() =>
        OpenCustomShowDialogAsync();

    private async Task OpenCustomShowDialogAsync()
    {
        var dialog = new CustomShowDialog(this);
        await dialog.ShowDialog(this);
    }

    private sealed class EditPointsToggleCommand(SlideCanvas canvas) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context) =>
            canvas.SetEditPointsMode(
                PresentationEditPointsModePlanner.BuildTogglePlan(canvas.EditPointsEnabled).NextIsEnabled);

        public RibbonCommandState GetState() => new(
            IsEnabled: true,
            IsChecked: canvas.EditPointsEnabled);
    }

    private sealed class TransitionAdvanceOnClickToggleCommand(
        EditingSession editor,
        PresentationTransitionCommandPlan plan) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context) =>
            PresentationTransitionCommandPlanner.TryApply(
                editor,
                plan,
                context.SelectedValue);

        public RibbonCommandState GetState() => new(
            IsEnabled: true,
            IsChecked: PresentationTransitionCommandPlanner.IsAdvanceOnClickChecked(
                editor.CurrentSlideTransition));
    }

    private sealed class AnimationPaneToggleCommand : IRibbonStatefulCommand
    {
        private readonly EditingSession _editor;
        private readonly PresentationAnimationCommandPlan _plan;
        private readonly Func<bool> _isPaneVisible;
        private readonly Action<PresentationAnimationCommandPlan> _togglePane;

        public AnimationPaneToggleCommand(
            EditingSession editor,
            PresentationAnimationCommandPlan plan,
            Func<bool> isPaneVisible,
            Action<PresentationAnimationCommandPlan> togglePane)
        {
            _editor = editor;
            _plan = plan;
            _isPaneVisible = isPaneVisible;
            _togglePane = togglePane;
        }

        public void Execute(RibbonCommandContext context) =>
            PresentationAnimationCommandPlanner.TryApply(
                _editor,
                _plan,
                context.SelectedValue,
                _togglePane);

        public RibbonCommandState GetState() => new(
            IsEnabled: true,
            IsChecked: _isPaneVisible());
    }

    private sealed class ViewShowToggleCommand : IRibbonStatefulCommand
    {
        private readonly PresentationViewShowCommandPlan _plan;
        private readonly Func<PresentationViewShowState> _getState;
        private readonly Action<PresentationViewShowState> _applyState;

        public ViewShowToggleCommand(
            PresentationViewShowCommandPlan plan,
            Func<PresentationViewShowState> getState,
            Action<PresentationViewShowState> applyState)
        {
            _plan = plan;
            _getState = getState;
            _applyState = applyState;
        }

        public void Execute(RibbonCommandContext context)
        {
            var result = PresentationViewShowPlanner.Toggle(_getState(), _plan);
            _applyState(result.State);
        }

        public RibbonCommandState GetState() => new(
            IsEnabled: true,
            IsChecked: PresentationViewShowPlanner.IsChecked(_getState(), _plan.Kind));
    }
}
