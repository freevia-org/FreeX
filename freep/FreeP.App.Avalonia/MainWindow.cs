using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using AvaloniaRectangle = Avalonia.Controls.Shapes.Rectangle;
using Free.Shared.AppServices;
using Free.Shared.IO;
using Free.Shared.Pdf.Skia;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
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
///   Edit:   Undo, Redo, Find, Replace
///   Keyboard: Ctrl+N/O/S/Shift+S, Ctrl+Z/Y
///
/// Deferred to later Avalonia parity: transitions, animations, full platform dialogs,
///   clipboard (full).
/// </summary>
public sealed class MainWindow : Window
{
    private const string DefaultTitle = "FreeP";
    private const int DefaultRecentFilesCap = ApplicationOptionsNormalizer.DefaultRecentFilesCap;
    private static readonly SisterAppFileTextSpec FileText = SisterAppFileTextPlanner.Presentation;

    private static readonly FilePickerFileType PictureFileType =
        AvaloniaFilePickerTypeAdapter.CreateFileType(
            PresentationFileTextResources.PictureFileTypeName,
            ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.svg"],
            ["image/png", "image/jpeg", "image/gif", "image/bmp", "image/svg+xml"]);

    private static readonly (string CommandId, Action<EditingSession> Execute)[] ArrangeCommandRoutes =
    [
        ("freep.arrange.group", static editor => editor.GroupSelectedShapes()),
        ("freep.arrange.ungroup", static editor => editor.UngroupSelected()),
        ("freep.arrange.bring-to-front", static editor => editor.BringToFront()),
        ("freep.arrange.bring-forward", static editor => editor.BringForward()),
        ("freep.arrange.send-backward", static editor => editor.SendBackward()),
        ("freep.arrange.send-to-back", static editor => editor.SendToBack()),
        ("freep.arrange.align-left", static editor => editor.AlignLeft()),
        ("freep.arrange.align-center-h", static editor => editor.AlignCenterH()),
        ("freep.arrange.align-right", static editor => editor.AlignRight()),
        ("freep.arrange.align-top", static editor => editor.AlignTop()),
        ("freep.arrange.align-middle", static editor => editor.AlignMiddle()),
        ("freep.arrange.align-bottom", static editor => editor.AlignBottom()),
        ("freep.arrange.distribute-h", static editor => editor.DistributeHorizontally()),
        ("freep.arrange.distribute-v", static editor => editor.DistributeVertically()),
    ];

    // ── Presentation model ─────────────────────────────────────────────────────

    private Presentation _presentation = Presentation.CreateEmpty();
    private readonly SisterAvaloniaFileCommandWorkflow _fileWorkflow;

    // ── Editing session ────────────────────────────────────────────────────────

    internal EditingSession Editor { get; private set; } = null!;

    // ── UI elements ────────────────────────────────────────────────────────────

    private readonly SlideCanvas _slideCanvas;
    private readonly ListBox _slidePaneList;
    private readonly Border _slidePaneInsertionIndicator;
    private readonly Button _slidePaneNewSlideButton;
    private readonly HashSet<string> _slidePaneCollapsedSectionIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBox _notesBox;
    private readonly TextBlock _statusText;
    private Border _layoutPickerHost = null!;
    private StackPanel _layoutPickerPanel = null!;
    private Border _tablePickerHost = null!;
    private WrapPanel _tablePickerPanel = null!;
    private Border _slideSizePaneHost = null!;
    private ComboBox _slideSizePresetCombo = null!;
    private ComboBox _slideSizeUnitCombo = null!;
    private TextBox _slideSizeWidthBox = null!;
    private TextBox _slideSizeHeightBox = null!;
    private TextBlock _slideSizeWidthUnitLabel = null!;
    private TextBlock _slideSizeHeightUnitLabel = null!;
    private TextBlock _slideSizeValidationText = null!;
    private bool _slideSizePaneRefreshing;
    private SlideSizeDialogUnit _slideSizeUnit = SlideSizeDialogUnit.Inches;
    private Border _headerFooterPaneHost = null!;
    private CheckBox _headerFooterDateTimeCheck = null!;
    private CheckBox _headerFooterFooterCheck = null!;
    private TextBox _headerFooterFooterBox = null!;
    private CheckBox _headerFooterSlideNumberCheck = null!;
    private Border _reviewCommentsPaneHost = null!;
    private StackPanel _reviewCommentsPanePanel = null!;
    private int? _selectedCommentIndex;
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
    private StackPanel _accessibilityCheckerRowsPanel = null!;
    private int? _selectedAccessibilityCheckerRowIndex;
    private Border _readingOrderPaneHost = null!;
    private TextBlock _readingOrderPaneHeading = null!;
    private TextBlock _readingOrderPaneMessage = null!;
    private StackPanel _readingOrderPaneItemsPanel = null!;
    private Button _readingOrderMoveEarlierButton = null!;
    private Button _readingOrderMoveLaterButton = null!;
    private Border _proofingPaneHost = null!;
    private TextBlock _proofingPaneHeading = null!;
    private TextBlock _proofingPaneMessage = null!;
    private StackPanel _proofingPaneRowsPanel = null!;
    private int? _selectedProofingIssueRowIndex;
    private Border _animationPaneHost = null!;
    private TextBlock _animationPaneHeading = null!;
    private TextBlock _animationPaneMessage = null!;
    private StackPanel _animationPanePlaybackControlsPanel = null!;
    private StackPanel _animationPaneItemsPanel = null!;
    private Button _animationPanePreviewButton = null!;
    private int _selectedAnimationIndex = -1;
    private AnimationPanePlaybackSessionPlan? _animationPanePlaybackSessionPlan;
    private readonly List<string> _animationPaneRenderedRows = new();
    private readonly List<string> _animationPaneRenderedPlaybackControls = new();
    private int _animationPaneEffectOptionControlCount;
    private int _animationPaneTriggerControlCount;
    private int _animationPaneDurationControlCount;
    private int _animationPaneDelayControlCount;
    private Border _findReplacePaneHost = null!;
    private TextBlock _findReplacePaneHeading = null!;
    private TextBlock _findReplaceStatusText = null!;
    private TextBox _findReplaceFindBox = null!;
    private TextBlock _findReplaceReplaceLabel = null!;
    private TextBox _findReplaceReplaceBox = null!;
    private CheckBox _findReplaceMatchCaseCheck = null!;
    private CheckBox _findReplaceWholeWordCheck = null!;
    private Button _findReplaceButton = null!;
    private Button _findReplacePreviousButton = null!;
    private Button _findReplaceReplaceButton = null!;
    private Button _findReplaceReplaceAllButton = null!;
    private readonly List<TextSearchMatch> _findReplaceMatches = new();
    private int _findReplaceCurrentMatchIndex = -1;
    private bool _findReplaceShowReplace;
    private Border _printOptionsPaneHost = null!;
    private TextBlock _printOptionsPaneHeading = null!;
    private TextBlock _printOptionsPaneMessage = null!;
    private StackPanel _printOptionsPaneRowsPanel = null!;
    private readonly List<string> _printOptionsPaneRenderedOptionLines = new();
    private readonly List<string> _printOptionsPaneRenderedPreviewRows = new();
    private readonly List<string> _printOptionsPaneRenderedLayoutRows = new();
    private readonly List<string> _printOptionsPaneRenderedRangeRows = new();

    // ── Interaction layer (Theme 15) ────────────────────────────────────────────

    private SelectionAdornerLayer?       _adorner;
    private AvaloniaCanvasGestureHandler? _gestureHandler;
    private AvaloniaInCanvasTextEditor?  _textEditor;
    private PresentationViewShowState _viewShowState = PresentationViewShowState.Default;
    private PresentationViewZoomState _viewZoomState = PresentationViewZoomState.FitToWindow;

    private bool _notesRefreshing;
    private bool _slidePaneRefreshing;
    private bool _slidePaneIsDragging;
    private int _slidePaneDragSourceIndex = -1;
    private int _slidePaneDragTargetIndex = -1;
    private Point _slidePaneDragStartPoint;

    private sealed record SlidePaneSectionHeaderTag(string SectionId, int SectionIndex);

    // ── Smoke surface ──────────────────────────────────────────────────────────

    /// <summary>True once the ribbon has been built. Read by the launch-smoke coordinator.</summary>
    internal bool HasToolbar { get; private set; }

    /// <summary>Current slide count — read by the launch-smoke coordinator.</summary>
    internal int SlideCount => _presentation.Slides.Count;

    /// <summary>Current slide index (0-based) — read by the launch-smoke coordinator.</summary>
    internal int CurrentSlideIndex => Editor?.CurrentSlideIndex ?? -1;
    internal int SlidePaneSlideItemCount => _slidePaneList.Items
        .OfType<ListBoxItem>()
        .Count(item => item.Tag is int);
    internal int SlidePaneSectionHeaderCount => _slidePaneList.Items
        .OfType<ListBoxItem>()
        .Count(item => item.Tag is SlidePaneSectionHeaderTag);
    internal bool IsSlidePaneInsertionIndicatorVisible => _slidePaneInsertionIndicator.IsVisible;
    internal bool IsSlidePaneNewSlideButtonVisible => _slidePaneNewSlideButton.IsVisible;
    internal string? SlidePaneNewSlideButtonText => _slidePaneNewSlideButton.Content?.ToString();

    internal bool IsDirty => _fileWorkflow.IsDirty;
    internal PresentationViewShowState ViewShowStateForTests => _viewShowState;
    internal PresentationViewZoomState ViewZoomStateForTests => _viewZoomState;
    internal PresentationViewZoomState SlideCanvasViewZoomStateForTests => _slideCanvas.ViewZoomState;
    internal bool? GestureSnapToGridForTests => _gestureHandler?.SnapToGrid;
    internal bool? GestureSnapToShapesForTests => _gestureHandler?.SnapToShapes;

    internal string? CurrentPath => _fileWorkflow.CurrentPath;

    internal IReadOnlyList<RecentFileEntry> RecentEntries => _fileWorkflow.RecentEntries;

    internal PresentationCommentPanePlan? LastCommentPanePlan { get; private set; }
    internal PresentationCommentNavigationPlan? LastCommentNavigationPlan { get; private set; }
    internal PresentationAccessibilitySummaryPlan? LastAccessibilitySummaryPlan { get; private set; }
    internal PresentationAccessibilityCheckerPanePlan? LastAccessibilityCheckerPanePlan { get; private set; }
    internal PresentationSlideTitleMutationPlan? LastSlideTitleMutationPlan { get; private set; }
    internal PresentationTableHeaderRowMutationPlan? LastTableHeaderRowMutationPlan { get; private set; }
    internal PresentationAltTextRequestPlan? LastAltTextRequestPlan { get; private set; }
    internal PresentationAltTextPanePlan? LastAltTextPanePlan { get; private set; }
    internal PresentationReadingOrderPlan? LastReadingOrderPlan { get; private set; }
    internal PresentationProofingRequestPlan? LastProofingRequestPlan { get; private set; }
    internal PresentationProofingExecutionPlan? LastProofingExecutionPlan { get; private set; }
    internal PresentationProofingPanePlan? LastProofingPanePlan { get; private set; }
    internal AnimationPaneTimelinePlan? LastAnimationPaneTimelinePlan { get; private set; }
    internal AnimationPanePlaybackSessionPlan? LastAnimationPanePlaybackSessionPlan => _animationPanePlaybackSessionPlan;
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
    internal PresentationDesignCommandPlan? LastLayoutRequestPlan { get; private set; }
    internal PresentationHandoutLayoutPlan? LastHandoutLayoutPlan { get; private set; }
    internal PresentationNotesPagePreviewPlan? LastNotesPagePreviewPlan { get; private set; }
    internal PresentationNotesPagePdfRenderPlan? LastNotesPagePdfRenderPlan { get; private set; }
    internal PresentationPrintOutputPackage? LastPrintOutputPackage { get; private set; }
    internal PresentationPrintBackstagePlan? LastPrintBackstagePlan { get; private set; }
    internal PresentationVideoExportPlan? LastVideoExportPlan { get; private set; }
    internal PresentationVideoFramePackage? LastVideoFramePackage { get; private set; }
    internal PresentationLayoutPickerPlan? LastLayoutPickerPlan { get; private set; }
    internal PresentationLayoutChoice? LastAppliedLayoutChoice { get; private set; }
    internal TableInsertionPickerPlan? LastTablePickerPlan { get; private set; }
    internal bool IsLayoutPickerVisible => _layoutPickerHost?.IsVisible == true;
    internal bool IsTablePickerVisible => _tablePickerHost?.IsVisible == true;
    internal bool IsCustomSlideSizePaneVisible => _slideSizePaneHost?.IsVisible == true;
    internal bool IsHeaderFooterPaneVisible => _headerFooterPaneHost?.IsVisible == true;
    internal string CustomSlideSizeWidthText => _slideSizeWidthBox?.Text ?? string.Empty;
    internal string CustomSlideSizeHeightText => _slideSizeHeightBox?.Text ?? string.Empty;
    internal string CustomSlideSizeValidationText => _slideSizeValidationText?.Text ?? string.Empty;
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
    internal IReadOnlyList<string> ReviewCommentsPaneRenderedActionStates =>
        EnumerateReviewPaneButtons(_reviewCommentsPanePanel)
            .Where(button => button.Tag is string commandId &&
                commandId.StartsWith("freep.review.comments.", StringComparison.Ordinal))
            .Select(button => $"{button.Tag}|{button.Content}|{button.IsEnabled}")
            .ToArray();
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
    internal bool IsAccessibilityCheckerPaneVisible => _accessibilityCheckerPaneHost?.IsVisible == true;
    internal int AccessibilityCheckerPaneRowCount => LastAccessibilityCheckerPanePlan?.Rows.Count ?? 0;
    internal int AccessibilityCheckerPaneSelectedRowCount =>
        LastAccessibilityCheckerPanePlan?.Rows.Count(row => row.IsSelected) ?? 0;
    internal string AccessibilityCheckerPaneHeading => _accessibilityCheckerPaneHeading?.Text ?? string.Empty;
    internal string AccessibilityCheckerPaneMessage => _accessibilityCheckerPaneMessage?.Text ?? string.Empty;
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
    internal string ProofingPaneHeading => _proofingPaneHeading?.Text ?? string.Empty;
    internal string ProofingPaneMessage => _proofingPaneMessage?.Text ?? string.Empty;
    internal string? ReadingOrderMoveEarlierDisabledReason =>
        LastReadingOrderPlan?.Actions.SingleOrDefault(action =>
            action.CommandId == PresentationReviewWorkflowPlanner.ReadingOrderMoveEarlierCommandId)?.DisabledReason;
    internal string? ReadingOrderMoveLaterDisabledReason =>
        LastReadingOrderPlan?.Actions.SingleOrDefault(action =>
            action.CommandId == PresentationReviewWorkflowPlanner.ReadingOrderMoveLaterCommandId)?.DisabledReason;
    internal bool IsAnimationPaneVisible => _animationPaneHost?.IsVisible == true;
    internal int AnimationPaneItemCount => LastAnimationPaneTimelinePlan?.Items.Count ?? 0;
    internal int AnimationPaneRenderedItemCount => _animationPaneItemsPanel?.Children.Count ?? 0;
    internal string AnimationPaneHeading => _animationPaneHeading?.Text ?? string.Empty;
    internal string AnimationPaneMessage => _animationPaneMessage?.Text ?? string.Empty;
    internal bool IsAnimationPanePreviewEnabled => _animationPanePreviewButton?.IsEnabled == true;
    internal IReadOnlyList<string> AnimationPanePlaybackControls => _animationPaneRenderedPlaybackControls;
    internal IReadOnlyList<string> AnimationPaneRenderedRows => _animationPaneRenderedRows;
    internal int AnimationPaneEffectOptionControlCount => _animationPaneEffectOptionControlCount;
    internal int AnimationPaneTriggerControlCount => _animationPaneTriggerControlCount;
    internal int AnimationPaneDurationControlCount => _animationPaneDurationControlCount;
    internal int AnimationPaneDelayControlCount => _animationPaneDelayControlCount;
    internal bool IsFindReplacePaneVisible => _findReplacePaneHost?.IsVisible == true;
    internal string FindReplacePaneTitle => _findReplacePaneHeading?.Text ?? string.Empty;
    internal string FindReplacePaneStatus => _findReplaceStatusText?.Text ?? string.Empty;
    internal bool IsFindReplaceReplaceInputVisible => _findReplaceReplaceBox?.IsVisible == true;
    internal bool IsPrintOptionsPaneVisible => _printOptionsPaneHost?.IsVisible == true;
    internal string PrintOptionsPaneHeading => _printOptionsPaneHeading?.Text ?? string.Empty;
    internal string PrintOptionsPaneMessage => _printOptionsPaneMessage?.Text ?? string.Empty;
    internal int PrintOptionsPaneRenderedRowCount => _printOptionsPaneRowsPanel?.Children.Count ?? 0;
    internal IReadOnlyList<string> PrintOptionsPaneRenderedOptionLines => _printOptionsPaneRenderedOptionLines;
    internal IReadOnlyList<string> PrintOptionsPaneRenderedPreviewRows => _printOptionsPaneRenderedPreviewRows;
    internal IReadOnlyList<string> PrintOptionsPaneRenderedLayoutRows => _printOptionsPaneRenderedLayoutRows;
    internal IReadOnlyList<string> PrintOptionsPaneRenderedRangeRows => _printOptionsPaneRenderedRangeRows;

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
        Func<RecentFilesStore>? loadRecentFilesStore)
    {
        Title = DefaultTitle;
        Width = 1280;
        Height = 760;
        MinWidth = 800;
        MinHeight = 500;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        // Build editing session around the initial empty presentation.
        RebuildEditor();

        // ── Core UI elements ──────────────────────────────────────────────────

        _slideCanvas = new SlideCanvas
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Stretch,
            Margin              = new Thickness(24),
        };

        _slidePaneList = new ListBox
        {
            Width       = 180,
            Padding     = new Thickness(4),
            Background  = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
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
            MinHeight       = 64,
            MaxHeight       = 120,
            Padding         = new Thickness(8, 4),
            FontSize        = 12,
            Background      = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xF0)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
        };
        _notesBox.TextChanged += OnNotesTextChanged;

        _statusText = SisterAppStatusBarChrome.CreateInfoText(foreground: Brushes.White, margin: new Thickness(8, 0));
        _fileWorkflow = new SisterAvaloniaFileCommandWorkflow(
            owner: this,
            titleSpec: new SisterAvaloniaFileTitleSpec(
                ApplicationName: DefaultTitle,
                Separator: " \u2014 "),
            maxRecentEntries: () => DefaultRecentFilesCap,
            onChanged: UpdateStatus,
            save: () => FileSaveAsync().GetAwaiter().GetResult(),
            loadRecentFilesStore: loadRecentFilesStore);

        // ── Root layout ───────────────────────────────────────────────────────

        var ribbon = BuildRibbon();
        var statusBar = SisterAppStatusBarChrome.Build(new SisterAppStatusBarSpec(
            Background: new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A)),
            LeftContent: _statusText)).Root;
        var frame = SisterAppClientFrameBuilder.Build(SisterAppClientFrameSpec.ForWorkArea(
            chrome: ribbon,
            workArea: BuildBody(),
            statusBar: statusBar));

        // ── Keyboard shortcuts ────────────────────────────────────────────────

        KeyDown += MainWindow_KeyDown;

        // ── Initial content ───────────────────────────────────────────────────

        var startupPresentation = startupArguments
            .FirstOrDefault(a => IsSupportedPresentationPath(a) && File.Exists(a));

        if (startupPresentation is not null)
            TryLoadPresentationFile(startupPresentation);
        else
            LoadPresentationAsSaved(_presentation, path: null);

        Content = frame.Root;
        UpdateStatus();
    }

    // ── Editor construction ────────────────────────────────────────────────────

    private void RebuildEditor()
    {
        var bus = new PresentationCommandBus(_presentation);
        Editor  = new EditingSession(_presentation, bus);

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
        // A Panel stack: SlideCanvas at the bottom, SelectionAdornerLayer on top (transparent to
        // pointer events), and a Canvas for the text-edit TextBox overlay on the very top.
        _adorner = new SelectionAdornerLayer
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Stretch,
            IsHitTestVisible    = false,
        };

        // Text-overlay: a Canvas that hosts TextBox children during text editing.
        var textOverlay = new Canvas
        {
            IsVisible        = false,
            IsHitTestVisible = false,
        };

        // Stack all three in a Panel (Grid with single cell).
        var canvasStack = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Stretch,
        };
        canvasStack.Children.Add(_slideCanvas);
        canvasStack.Children.Add(_adorner);
        canvasStack.Children.Add(textOverlay);

        var canvasHost = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)),
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
        _tablePickerPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
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
            Spacing     = 6,
        };
        _reviewCommentsPaneHost = new Border
        {
            Background      = Brushes.White,
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            MaxHeight       = 180,
            IsVisible       = false,
            Child           = new ScrollViewer
            {
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content                       = _reviewCommentsPanePanel,
            },
        };
        _altTextPaneHost = BuildAltTextPaneHost();
        _accessibilityCheckerPaneHost = BuildAccessibilityCheckerPaneHost();
        _readingOrderPaneHost = BuildReadingOrderPaneHost();
        _proofingPaneHost = BuildProofingPaneHost();
        _animationPaneHost = BuildAnimationPaneHost();
        _findReplacePaneHost = BuildFindReplacePaneHost();
        _printOptionsPaneHost = BuildPrintOptionsPaneHost();
        _slideSizePaneHost = BuildSlideSizePaneHost();
        _headerFooterPaneHost = BuildHeaderFooterPaneHost();
        Grid.SetRow(canvasHost, 0);
        Grid.SetRow(_layoutPickerHost, 1);
        Grid.SetRow(_tablePickerHost, 2);
        Grid.SetRow(_reviewCommentsPaneHost, 3);
        Grid.SetRow(_notesBox,  4);
        rightGrid.Children.Add(canvasHost);
        rightGrid.Children.Add(_layoutPickerHost);
        rightGrid.Children.Add(_tablePickerHost);
        rightGrid.Children.Add(_reviewCommentsPaneHost);
        rightGrid.Children.Add(_notesBox);

        // Wire interaction after the overlay panel is built.
        WireInteraction(textOverlay);

        var slidePaneHost = new Grid
        {
            Width = 180,
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
        Grid.SetColumn(_slideSizePaneHost, 2);
        Grid.SetColumn(_headerFooterPaneHost, 3);
        Grid.SetColumn(_accessibilityCheckerPaneHost, 4);
        Grid.SetColumn(_altTextPaneHost, 5);
        Grid.SetColumn(_readingOrderPaneHost, 6);
        Grid.SetColumn(_proofingPaneHost, 7);
        Grid.SetColumn(_animationPaneHost, 8);
        Grid.SetColumn(_findReplacePaneHost, 9);
        Grid.SetColumn(_printOptionsPaneHost, 10);
        body.Children.Add(slidePaneHost);
        body.Children.Add(rightGrid);
        body.Children.Add(_slideSizePaneHost);
        body.Children.Add(_headerFooterPaneHost);
        body.Children.Add(_accessibilityCheckerPaneHost);
        body.Children.Add(_altTextPaneHost);
        body.Children.Add(_readingOrderPaneHost);
        body.Children.Add(_proofingPaneHost);
        body.Children.Add(_animationPaneHost);
        body.Children.Add(_findReplacePaneHost);
        body.Children.Add(_printOptionsPaneHost);

        return body;
    }

    private Border BuildFindReplacePaneHost()
    {
        _findReplacePaneHeading = new TextBlock
        {
            Text = FindReplaceDialogPlanner.FindTitle,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(12, 12, 12, 4),
        };
        _findReplaceFindBox = new TextBox
        {
            Margin = new Thickness(12, 2, 12, 8),
            PlaceholderText = "Find what",
        };
        _findReplaceFindBox.TextChanged += (_, _) => InvalidateFindReplaceSearch();

        _findReplaceReplaceLabel = new TextBlock
        {
            Text = "Replace with",
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(12, 4, 12, 2),
        };
        _findReplaceReplaceBox = new TextBox
        {
            Margin = new Thickness(12, 2, 12, 8),
            PlaceholderText = "Replacement text",
        };
        _findReplaceReplaceBox.TextChanged += (_, _) => RefreshFindReplaceWorkflowPlan();

        _findReplaceMatchCaseCheck = new CheckBox
        {
            Content = "Match case",
            Margin = new Thickness(12, 2, 12, 2),
        };
        _findReplaceMatchCaseCheck.IsCheckedChanged += (_, _) => InvalidateFindReplaceSearch();

        _findReplaceWholeWordCheck = new CheckBox
        {
            Content = "Whole word",
            Margin = new Thickness(12, 0, 12, 8),
        };
        _findReplaceWholeWordCheck.IsCheckedChanged += (_, _) => InvalidateFindReplaceSearch();

        _findReplacePreviousButton = new Button
        {
            Content = "Previous",
            MinWidth = 80,
            Margin = new Thickness(0, 0, 6, 0),
        };
        _findReplacePreviousButton.Click += (_, _) => NavigateFindReplace(-1);

        _findReplaceButton = new Button
        {
            Content = "Find Next",
            MinWidth = 80,
            Margin = new Thickness(0, 0, 6, 0),
        };
        _findReplaceButton.Click += (_, _) => NavigateFindReplace(+1);

        _findReplaceReplaceButton = new Button
        {
            Content = "Replace",
            MinWidth = 80,
            Margin = new Thickness(0, 0, 6, 0),
        };
        _findReplaceReplaceButton.Click += (_, _) => ReplaceCurrentFindReplaceMatch();

        _findReplaceReplaceAllButton = new Button
        {
            Content = "Replace All",
            MinWidth = 96,
        };
        _findReplaceReplaceAllButton.Click += (_, _) => ReplaceAllFindReplaceMatches();

        _findReplaceStatusText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            Margin = new Thickness(12, 8, 12, 12),
        };

        return new Border
        {
            Width = 300,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
            BorderThickness = new Thickness(1, 0, 0, 0),
            IsVisible = false,
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Children =
                    {
                        _findReplacePaneHeading,
                        new TextBlock
                        {
                            Text = "Find what",
                            FontWeight = FontWeight.SemiBold,
                            Margin = new Thickness(12, 4, 12, 2),
                        },
                        _findReplaceFindBox,
                        _findReplaceReplaceLabel,
                        _findReplaceReplaceBox,
                        _findReplaceMatchCaseCheck,
                        _findReplaceWholeWordCheck,
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Margin = new Thickness(12, 4, 12, 4),
                            Children =
                            {
                                _findReplaceButton,
                                _findReplacePreviousButton,
                            },
                        },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Margin = new Thickness(12, 4, 12, 4),
                            Children =
                            {
                                _findReplaceReplaceButton,
                                _findReplaceReplaceAllButton,
                            },
                        },
                        _findReplaceStatusText,
                    },
                },
            },
        };
    }

    private Border BuildPrintOptionsPaneHost()
    {
        _printOptionsPaneHeading = new TextBlock
        {
            Text = "Print",
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(12, 12, 12, 4),
        };
        _printOptionsPaneMessage = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            Margin = new Thickness(12, 0, 12, 8),
        };
        _printOptionsPaneRowsPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
        };

        var header = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Children =
            {
                _printOptionsPaneHeading,
                _printOptionsPaneMessage,
            },
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
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        Content = _printOptionsPaneRowsPanel,
                    },
                },
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

    private Border BuildSlideSizePaneHost()
    {
        _slideSizePresetCombo = new ComboBox
        {
            Margin = new Thickness(12, 4, 12, 8),
            Items =
            {
                "Standard (4:3)",
                "Widescreen (16:9)",
                "Custom",
            },
        };
        _slideSizePresetCombo.SelectionChanged += OnSlideSizePresetChanged;

        _slideSizeUnitCombo = new ComboBox
        {
            Margin = new Thickness(12, 4, 12, 8),
            Items =
            {
                "Inches",
                "Centimeters",
            },
        };
        _slideSizeUnitCombo.SelectionChanged += OnSlideSizeUnitChanged;

        _slideSizeWidthBox = BuildSlideSizeTextBox();
        _slideSizeHeightBox = BuildSlideSizeTextBox();
        _slideSizeWidthUnitLabel = BuildSlideSizeUnitLabel();
        _slideSizeHeightUnitLabel = BuildSlideSizeUnitLabel();
        _slideSizeValidationText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9B, 0x1C, 0x1C)),
            Margin = new Thickness(12, 2, 12, 8),
        };

        var apply = new Button
        {
            Content = "Apply",
            MinWidth = 78,
            Margin = new Thickness(0, 0, 8, 0),
        };
        apply.Click += (_, _) => ApplyCustomSlideSize();

        var close = new Button
        {
            Content = "Close",
            MinWidth = 78,
        };
        close.Click += (_, _) => HideCustomSlideSizePane();

        return new Border
        {
            Width = 260,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
            BorderThickness = new Thickness(1, 0, 0, 0),
            IsVisible = false,
            Child = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Slide Size",
                        FontSize = 15,
                        FontWeight = FontWeight.SemiBold,
                        Margin = new Thickness(12, 12, 12, 4),
                    },
                    BuildSlideSizeLabel("Preset"),
                    _slideSizePresetCombo,
                    BuildSlideSizeLabel("Unit"),
                    _slideSizeUnitCombo,
                    BuildSlideSizeLabel("Width"),
                    BuildSlideSizeFieldRow(_slideSizeWidthBox, _slideSizeWidthUnitLabel),
                    BuildSlideSizeLabel("Height"),
                    BuildSlideSizeFieldRow(_slideSizeHeightBox, _slideSizeHeightUnitLabel),
                    _slideSizeValidationText,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(12, 4, 12, 12),
                        Children = { apply, close },
                    },
                },
            },
        };
    }

    private static TextBlock BuildSlideSizeLabel(string text) => new()
    {
        Text = text,
        Margin = new Thickness(12, 6, 12, 0),
        FontSize = 11,
        Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
    };

    private static TextBox BuildSlideSizeTextBox() => new()
    {
        Margin = new Thickness(12, 3, 6, 3),
        MinWidth = 120,
    };

    private static TextBlock BuildSlideSizeUnitLabel() => new()
    {
        Width = 28,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static StackPanel BuildSlideSizeFieldRow(TextBox box, TextBlock unitLabel) => new()
    {
        Orientation = Orientation.Horizontal,
        Children = { box, unitLabel },
    };

    private Border BuildHeaderFooterPaneHost()
    {
        _headerFooterDateTimeCheck = new CheckBox
        {
            Content = "Date and time",
            Margin = new Thickness(12, 4, 12, 8),
        };
        _headerFooterFooterCheck = new CheckBox
        {
            Content = "Footer",
            Margin = new Thickness(12, 4, 12, 4),
        };
        _headerFooterFooterBox = new TextBox
        {
            Margin = new Thickness(28, 0, 12, 8),
            MinWidth = 180,
        };
        _headerFooterSlideNumberCheck = new CheckBox
        {
            Content = "Slide number",
            Margin = new Thickness(12, 4, 12, 12),
        };
        _headerFooterFooterCheck.IsCheckedChanged += (_, _) =>
            _headerFooterFooterBox.IsEnabled = _headerFooterFooterCheck.IsChecked == true;

        var apply = new Button
        {
            Content = "Apply",
            MinWidth = 78,
            Margin = new Thickness(0, 0, 8, 0),
        };
        apply.Click += (_, _) => ApplyHeaderFooter(HeaderFooterApplyScope.CurrentSlide);

        var applyAll = new Button
        {
            Content = "Apply All",
            MinWidth = 78,
            Margin = new Thickness(0, 0, 8, 0),
        };
        applyAll.Click += (_, _) => ApplyHeaderFooter(HeaderFooterApplyScope.AllSlides);

        var close = new Button
        {
            Content = "Close",
            MinWidth = 78,
        };
        close.Click += (_, _) => HideHeaderFooterPane();

        return new Border
        {
            Width = 260,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
            BorderThickness = new Thickness(1, 0, 0, 0),
            IsVisible = false,
            Child = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Header and Footer",
                        FontSize = 15,
                        FontWeight = FontWeight.SemiBold,
                        Margin = new Thickness(12, 12, 12, 4),
                    },
                    _headerFooterDateTimeCheck,
                    _headerFooterFooterCheck,
                    _headerFooterFooterBox,
                    _headerFooterSlideNumberCheck,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(12, 4, 12, 12),
                        Children = { apply, applyAll, close },
                    },
                },
            },
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

    private Border BuildAccessibilityCheckerPaneHost()
    {
        _accessibilityCheckerPaneHeading = new TextBlock
        {
            Text = "Accessibility",
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(12, 12, 12, 4),
        };
        _accessibilityCheckerPaneMessage = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            Margin = new Thickness(12, 0, 12, 8),
        };
        _accessibilityCheckerRowsPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
        };

        var header = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Children =
            {
                _accessibilityCheckerPaneHeading,
                _accessibilityCheckerPaneMessage,
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
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            Margin = new Thickness(12, 0, 12, 8),
        };
        _readingOrderMoveEarlierButton = new Button
        {
            MinWidth = 94,
            Padding = new Thickness(10, 4),
            Margin = new Thickness(0, 0, 8, 0),
        };
        _readingOrderMoveLaterButton = new Button
        {
            MinWidth = 84,
            Padding = new Thickness(10, 4),
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
            Margin = new Thickness(12, 0, 12, 8),
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
        panel.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _readingOrderPaneItemsPanel,
        });

        return new Border
        {
            Width = 320,
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
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(12, 12, 12, 4),
        };
        _animationPaneMessage = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            Margin = new Thickness(12, 0, 12, 8),
        };
        _animationPanePreviewButton = new Button
        {
            Content = "Preview",
            MinWidth = 82,
            Padding = new Thickness(10, 4),
            Margin = new Thickness(0, 0, 8, 8),
        };
        _animationPanePlaybackControlsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(12, 0, 12, 0),
            Children =
            {
                _animationPanePreviewButton,
            },
        };

        _animationPaneItemsPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
        };

        var header = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Children =
            {
                _animationPaneHeading,
                _animationPaneMessage,
                _animationPanePlaybackControlsPanel,
            }
        };
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
            Width = 340,
            IsVisible = false,
            Background = Brushes.White,
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
        _gestureHandler = new AvaloniaCanvasGestureHandler(_slideCanvas, Editor, _adorner);
        ApplyPresentationViewShowState(_viewShowState);

        // Text editor: double-click a shape to edit its text.
        _textEditor = new AvaloniaInCanvasTextEditor(_slideCanvas, Editor, textOverlay);
    }

    /// <summary>
    /// Re-wires the interaction layer to the new <see cref="Editor"/> instance after a
    /// file open / new operation.
    /// </summary>
    private void RewireInteractionToEditor()
    {
        if (_adorner is null) return;
        // The gesture handler and text editor subscribe to the canvas's pointer events,
        // so we must create new instances to bind to the new EditingSession.
        // Find the textOverlay in the visual tree (it's the 3rd child of the canvasStack).
        // We can retrieve it from the existing text editor's overlay or re-find it:
        Canvas? textOverlay = null;
        if (_textEditor is not null)
        {
            // Cancel any active edit before we destroy the old editor.
            _textEditor.Cancel();
        }

        // Detach old gesture handler's pointer event subscriptions by creating a new instance.
        // The old handlers go out of scope and GC naturally; Avalonia weak event subscriptions
        // allow this. New instances re-subscribe.
        // Re-find the overlay canvas from the canvasStack structure.
        if (_slideCanvas.Parent is Grid canvasStack && canvasStack.Children.Count >= 3
            && canvasStack.Children[2] is Canvas ov)
        {
            textOverlay = ov;
        }

        if (textOverlay is not null)
        {
            _gestureHandler = new AvaloniaCanvasGestureHandler(_slideCanvas, Editor, _adorner);
            ApplyPresentationViewShowState(_viewShowState);
            _textEditor     = new AvaloniaInCanvasTextEditor(_slideCanvas, Editor, textOverlay);
        }
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

        var ribbon = AvaloniaRibbonRenderer.BuildRibbon(
            FreePRibbonAvalonia.Build(),
            registry,
            afterExecute: null);

        HasToolbar = true;
        return new Border
        {
            Background      = Brushes.White,
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child           = ribbon,
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
        r.Register(PresentationExportPlanner.PrintCommandId, new ActionRibbonCommand(() => ShowPrintOptionsPane()));
        r.Register(PresentationExportPlanner.VideoExportCommandId, new ActionRibbonCommand(() => RefreshVideoFramePackage()));

        // Slide navigation/management
        r.Register("freep.new-slide",       new ActionRibbonCommand(() => Editor.InsertSlide()));
        r.Register("freep.duplicate-slide", new ActionRibbonCommand(() => Editor.DuplicateCurrentSlide()));
        r.Register("freep.delete-slide",    new ActionRibbonCommand(() => Editor.DeleteCurrentSlide()));
        r.Register(PresentationDesignCommandPlanner.LayoutCommandId, new ActionRibbonCommand(() =>
            PresentationDesignCommandPlanner.TryApply(
                Editor,
                PresentationDesignCommandPlanner.LayoutPlan,
                OnDesignHostRequest)));

        // Clipboard
        r.Register("freep.copy", new ActionRibbonCommand(() => Editor.CopySelectedShapes()));
        r.Register("freep.cut", new ActionRibbonCommand(() => Editor.CutSelectedShapes()));
        r.Register("freep.paste", new ActionRibbonCommand(() => Editor.Paste()));
        r.Register("freep.format-painter", new ActionRibbonCommand(() =>
        {
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

        foreach (var route in ArrangeCommandRoutes)
        {
            r.Register(route.CommandId, new ActionRibbonCommand(() => route.Execute(Editor)));
        }

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

            r.Register(plan.CommandId, new ActionRibbonCommand(() =>
                SlideObjectInsertionPlanner.Apply(Editor, plan)));
        }

        r.Register(ChartDataDialogPlanner.EditDataCommandId, new ActionRibbonCommand(OpenChartDataDialog));
        r.Register("freep.insert-link", new ActionRibbonCommand(OpenHyperlinkDialog));
        r.Register("freep.remove-link", new ActionRibbonCommand(() => Editor.RemoveShapeHyperlink()));
        r.Register(HeaderFooterCommandPlanner.HeaderFooterCommandId,
            new ActionRibbonCommand(() => OpenHeaderFooterPane(HeaderFooterCommandFocus.HeaderFooter)));
        r.Register(HeaderFooterCommandPlanner.DateTimeCommandId,
            new ActionRibbonCommand(() => OpenHeaderFooterPane(HeaderFooterCommandFocus.DateTime)));
        r.Register(HeaderFooterCommandPlanner.SlideNumberCommandId,
            new ActionRibbonCommand(() => OpenHeaderFooterPane(HeaderFooterCommandFocus.SlideNumber)));

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
            r.Register(plan.CommandId, new ContextRibbonCommand(ctx =>
                PresentationTransitionCommandPlanner.TryApply(Editor, plan, ctx.SelectedValue)));
        }

        foreach (var plan in PresentationDesignCommandPlanner.BuiltInPlans)
        {
            r.Register(plan.CommandId, new ActionRibbonCommand(() =>
                PresentationDesignCommandPlanner.TryApply(Editor, plan, OnDesignHostRequest)));
        }

        foreach (var plan in PresentationAnimationCommandPlanner.BuiltInPlans)
        {
            r.Register(plan.CommandId, new ContextRibbonCommand(ctx =>
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

        return r;
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
        ShowCustomSlideSizePane(LastCustomSlideSizeInitialState);
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
            button.Click += (_, _) =>
            {
                if (button.Tag is TableInsertionPickerChoice tableChoice)
                    ApplyTablePickerChoice(tableChoice.Rows, tableChoice.Columns);
            };
            _tablePickerPanel.Children.Add(button);
        }

        HideLayoutPicker();
        HideCustomSlideSizePane();
        HideHeaderFooterPane();
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
        HideCustomSlideSizePane();
        HideHeaderFooterPane();
        _layoutPickerHost.IsVisible = true;
    }

    private void HideLayoutPicker()
    {
        if (_layoutPickerHost is not null)
            _layoutPickerHost.IsVisible = false;
    }

    private void ShowCustomSlideSizePane(SlideSizeDialogInitialState state)
    {
        if (_slideSizePaneHost is null)
            return;

        HideLayoutPicker();
        HideTablePicker();
        HideHeaderFooterPane();

        _slideSizePaneRefreshing = true;
        try
        {
            _slideSizeUnit = SlideSizeDialogUnit.Inches;
            _slideSizePresetCombo.SelectedIndex = ToSlideSizePresetIndex(state.Preset);
            _slideSizeUnitCombo.SelectedIndex = 0;
            ApplySlideSizeDisplay(state.Display);
            _slideSizeValidationText.Text = string.Empty;
        }
        finally
        {
            _slideSizePaneRefreshing = false;
        }

        _slideSizePaneHost.IsVisible = true;
    }

    private void HideCustomSlideSizePane()
    {
        if (_slideSizePaneHost is not null)
            _slideSizePaneHost.IsVisible = false;
    }

    internal void OpenHeaderFooterPane(HeaderFooterCommandFocus focus)
    {
        LastHeaderFooterFocus = focus;
        LastHeaderFooterState = HeaderFooterCommandPlanner.BuildState(Editor);
        var options = HeaderFooterCommandPlanner.BuildDefaultOptions(LastHeaderFooterState, focus);

        _headerFooterDateTimeCheck.IsChecked = options.ShowDateTime;
        _headerFooterFooterCheck.IsChecked = options.ShowFooter;
        _headerFooterFooterBox.Text = options.FooterText;
        _headerFooterFooterBox.IsEnabled = options.ShowFooter;
        _headerFooterSlideNumberCheck.IsChecked = options.ShowSlideNumber;

        HideLayoutPicker();
        HideTablePicker();
        HideCustomSlideSizePane();
        _headerFooterPaneHost.IsVisible = true;
        _statusText.Text = "Header and Footer";
    }

    private void HideHeaderFooterPane()
    {
        if (_headerFooterPaneHost is not null)
            _headerFooterPaneHost.IsVisible = false;
    }

    internal bool ApplyHeaderFooterForTests(
        bool showDateTime,
        bool showFooter,
        bool showSlideNumber,
        string footerText,
        HeaderFooterApplyScope scope)
    {
        _headerFooterDateTimeCheck.IsChecked = showDateTime;
        _headerFooterFooterCheck.IsChecked = showFooter;
        _headerFooterFooterBox.Text = footerText;
        _headerFooterSlideNumberCheck.IsChecked = showSlideNumber;
        return ApplyHeaderFooter(scope);
    }

    internal bool ApplyHeaderFooter(HeaderFooterApplyScope scope)
    {
        var options = new HeaderFooterApplyOptions(
            _headerFooterDateTimeCheck.IsChecked == true,
            _headerFooterFooterCheck.IsChecked == true,
            _headerFooterSlideNumberCheck.IsChecked == true,
            _headerFooterFooterBox.Text ?? string.Empty,
            scope);

        if (!HeaderFooterCommandPlanner.TryApply(Editor, options, out var plan))
        {
            return false;
        }

        LastHeaderFooterApplyPlan = plan;
        RefreshCanvas();
        UpdateStatus();
        HideHeaderFooterPane();
        return true;
    }

    private void OnSlideSizePresetChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_slideSizePaneRefreshing)
            return;

        var display = SlideSizeDialogPlanner.BuildPresetSelectionDisplay(
            SlideSizePresetFromIndex(_slideSizePresetCombo.SelectedIndex),
            _slideSizeUnit);
        if (display is not null)
            ApplySlideSizeDisplay(display);
    }

    private void OnSlideSizeUnitChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_slideSizePaneRefreshing)
            return;

        var newUnit = _slideSizeUnitCombo.SelectedIndex == 1
            ? SlideSizeDialogUnit.Centimeters
            : SlideSizeDialogUnit.Inches;
        if (newUnit == _slideSizeUnit)
            return;

        var display = SlideSizeDialogPlanner.BuildUnitChangeDisplay(
            _slideSizeWidthBox.Text ?? string.Empty,
            _slideSizeHeightBox.Text ?? string.Empty,
            _slideSizeUnit,
            newUnit);
        _slideSizeUnit = newUnit;
        ApplySlideSizeDisplay(display);
    }

    internal bool ApplyCustomSlideSizeForTests(
        string widthText,
        string heightText,
        SlideSizeDialogUnit unit)
    {
        _slideSizePaneRefreshing = true;
        try
        {
            _slideSizeUnit = unit;
            _slideSizeUnitCombo.SelectedIndex = unit == SlideSizeDialogUnit.Centimeters ? 1 : 0;
            _slideSizeWidthBox.Text = widthText;
            _slideSizeHeightBox.Text = heightText;
            _slideSizeWidthUnitLabel.Text = unit == SlideSizeDialogUnit.Centimeters ? "cm" : "in";
            _slideSizeHeightUnitLabel.Text = unit == SlideSizeDialogUnit.Centimeters ? "cm" : "in";
        }
        finally
        {
            _slideSizePaneRefreshing = false;
        }

        return ApplyCustomSlideSize();
    }

    internal bool ApplyCustomSlideSize()
    {
        LastCustomSlideSizeResultPlan = SlideSizeDialogPlanner.BuildOkResult(
            _slideSizeWidthBox.Text ?? string.Empty,
            _slideSizeHeightBox.Text ?? string.Empty,
            _slideSizeUnit);
        if (!SlideSizeDialogPlanner.TryApplyResult(Editor, LastCustomSlideSizeResultPlan))
        {
            _slideSizeValidationText.Text = LastCustomSlideSizeResultPlan.Validation?.Message ?? string.Empty;
            return false;
        }

        _slideSizeValidationText.Text = string.Empty;
        RefreshCanvas();
        UpdateStatus();
        HideCustomSlideSizePane();
        return true;
    }

    private void ApplySlideSizeDisplay(SlideSizeDialogDisplayState display)
    {
        _slideSizeWidthBox.Text = display.WidthText;
        _slideSizeHeightBox.Text = display.HeightText;
        _slideSizeWidthUnitLabel.Text = display.UnitLabel;
        _slideSizeHeightUnitLabel.Text = display.UnitLabel;
    }

    private static int ToSlideSizePresetIndex(SlideSizeDialogPreset preset)
        => preset switch
        {
            SlideSizeDialogPreset.Widescreen169 => 1,
            SlideSizeDialogPreset.Custom => 2,
            _ => 0,
        };

    private static SlideSizeDialogPreset SlideSizePresetFromIndex(int selectedIndex)
        => selectedIndex switch
        {
            1 => SlideSizeDialogPreset.Widescreen169,
            2 => SlideSizeDialogPreset.Custom,
            _ => SlideSizeDialogPreset.Standard43,
        };

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

    // ── File lifecycle ─────────────────────────────────────────────────────────

    internal void OpenChartDataDialog()
    {
        if (Editor.SelectedChart is null)
            return;

        var dialog = new ChartDataDialog(Editor);
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
        var request = HyperlinkDialogPlanner.BuildDialogRequest(
            Editor.Presentation.Slides,
            Editor.SelectedShapeHyperlink);
        LastHyperlinkDialogRequest = request;

        var result = HyperlinkDialogResultProviderForTests is { } provider
            ? await provider(request)
            : await ShowHyperlinkDialogAsync(request);

        var applyPlan = HyperlinkDialogPlanner.BuildApplyPlan(result);
        LastHyperlinkDialogApplyPlan = applyPlan;
        if (applyPlan.ShouldApply)
            Editor.SetShapeHyperlink(applyPlan.Url, applyPlan.TargetSlideId, applyPlan.Tooltip);

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

    internal void OpenFindDialog() =>
        OpenFindReplaceDialog(showReplace: false);

    internal void OpenFindReplaceDialog() =>
        OpenFindReplaceDialog(showReplace: true);

    private void OpenFindReplaceDialog(bool showReplace)
    {
        ShowFindReplacePane(showReplace);
    }

    internal FindReplaceWorkflowPlan SetFindReplacePaneInputForTests(
        string? query,
        string? replacement = null,
        bool matchCase = false,
        bool wholeWord = false)
    {
        _findReplaceFindBox.Text = query ?? string.Empty;
        _findReplaceReplaceBox.Text = replacement ?? string.Empty;
        _findReplaceMatchCaseCheck.IsChecked = matchCase;
        _findReplaceWholeWordCheck.IsChecked = wholeWord;
        InvalidateFindReplaceSearch();
        return LastFindReplaceWorkflowPlan!;
    }

    internal FindReplaceWorkflowPlan NavigateFindReplacePaneForTests(int direction) =>
        NavigateFindReplace(direction);

    internal FindReplaceWorkflowPlan ReplaceAllFindReplacePaneForTests() =>
        ReplaceAllFindReplaceMatches();

    private FindReplaceWorkflowPlan ShowFindReplacePane(bool showReplace)
    {
        _findReplaceShowReplace = showReplace;
        _findReplacePaneHost.IsVisible = true;
        _findReplaceReplaceLabel.IsVisible = showReplace;
        _findReplaceReplaceBox.IsVisible = showReplace;
        _findReplaceReplaceButton.IsVisible = showReplace;
        _findReplaceReplaceAllButton.IsVisible = showReplace;
        return RefreshFindReplaceWorkflowPlan();
    }

    private FindReplaceWorkflowPlan RefreshFindReplaceWorkflowPlan(
        string? statusText = null,
        FindReplacePolicyStatusKind statusKind = FindReplacePolicyStatusKind.None)
    {
        LastFindReplaceWorkflowPlan = FindReplaceDialogPlanner.BuildWorkflowPlan(
            _findReplaceShowReplace,
            _findReplaceFindBox.Text,
            _findReplaceReplaceBox.Text,
            _findReplaceMatchCaseCheck.IsChecked == true,
            _findReplaceWholeWordCheck.IsChecked == true,
            _findReplaceMatches,
            _findReplaceCurrentMatchIndex,
            statusText,
            statusKind);

        RenderFindReplaceWorkflowPlan(LastFindReplaceWorkflowPlan);
        return LastFindReplaceWorkflowPlan;
    }

    private void RenderFindReplaceWorkflowPlan(FindReplaceWorkflowPlan plan)
    {
        _findReplacePaneHeading.Text = plan.Title;
        _findReplaceStatusText.Text = plan.StatusText;
        _findReplaceStatusText.Foreground = plan.StatusKind switch
        {
            FindReplacePolicyStatusKind.NoMatches or FindReplacePolicyStatusKind.NoReplacements =>
                new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)),
            FindReplacePolicyStatusKind.Match or FindReplacePolicyStatusKind.Replacements =>
                new SolidColorBrush(Color.FromRgb(0x1B, 0x7E, 0x30)),
            _ => new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
        };
        _findReplaceButton.IsEnabled = plan.CanSearch;
        _findReplacePreviousButton.IsEnabled = plan.CanSearch;
        _findReplaceReplaceButton.IsEnabled = plan.CanReplace;
        _findReplaceReplaceAllButton.IsEnabled = plan.CanReplaceAll;
    }

    private void InvalidateFindReplaceSearch()
    {
        _findReplaceMatches.Clear();
        _findReplaceCurrentMatchIndex = -1;
        RefreshFindReplaceWorkflowPlan();
    }

    private void EnsureFindReplaceMatches()
    {
        if (_findReplaceMatches.Count > 0)
            return;

        _findReplaceMatches.AddRange(Editor.FindAll(_findReplaceFindBox.Text, BuildFindReplaceOptions()));
    }

    private FindReplaceWorkflowPlan NavigateFindReplace(int direction)
    {
        EnsureFindReplaceMatches();

        var plan = FindReplaceDialogPlanner.Navigate(
            _findReplaceCurrentMatchIndex,
            _findReplaceMatches.Count,
            direction);
        if (plan.HasMatch)
        {
            _findReplaceCurrentMatchIndex = plan.MatchIndex;
            Editor.NavigateTo(_findReplaceMatches[_findReplaceCurrentMatchIndex]);
            RefreshCanvas();
            RefreshSlidePane();
        }

        return RefreshFindReplaceWorkflowPlan(plan.StatusText, plan.StatusKind);
    }

    private FindReplaceWorkflowPlan ReplaceCurrentFindReplaceMatch()
    {
        EnsureFindReplaceMatches();
        var index = FindReplaceDialogPlanner.ReplacementTargetIndex(
            _findReplaceCurrentMatchIndex,
            _findReplaceMatches.Count);
        if (index < 0)
            return RefreshFindReplaceWorkflowPlan(
                FindReplaceDialogPolicy.NoMatchesStatus,
                FindReplacePolicyStatusKind.NoMatches);

        Editor.ReplaceOne(_findReplaceMatches[index], _findReplaceReplaceBox.Text ?? string.Empty);
        _findReplaceMatches.Clear();
        _findReplaceCurrentMatchIndex = -1;
        return NavigateFindReplace(+1);
    }

    private FindReplaceWorkflowPlan ReplaceAllFindReplaceMatches()
    {
        var query = _findReplaceFindBox.Text;
        if (!FindReplaceDialogPlanner.CanReplaceAll(query))
            return RefreshFindReplaceWorkflowPlan(
                FindReplaceDialogPolicy.SearchTermRequiredMessage,
                FindReplacePolicyStatusKind.None);

        var count = Editor.ReplaceAll(query, _findReplaceReplaceBox.Text ?? string.Empty, BuildFindReplaceOptions());
        _findReplaceMatches.Clear();
        _findReplaceCurrentMatchIndex = -1;
        var status = FindReplaceDialogPlanner.ReplacementStatus(count);
        return RefreshFindReplaceWorkflowPlan(status.StatusText, status.StatusKind);
    }

    private TextSearchOptions BuildFindReplaceOptions() => FindReplaceDialogPlanner.BuildOptions(
        _findReplaceMatchCaseCheck.IsChecked == true,
        _findReplaceWholeWordCheck.IsChecked == true);

    private void FileNew()
    {
        _fileWorkflow.New(
            FileText.NewAction,
            () => LoadPresentationContent(Presentation.CreateEmpty()));
    }

    private Task<bool> FileOpenAsync() =>
        _fileWorkflow.OpenAsync(
            FileText.OpenAction,
            PromptOpenPathAsync,
            path => Task.FromResult(TryLoadPresentationFile(path)));

    private async Task<string?> PromptOpenPathAsync()
    {
        if (!AvaloniaFilePickerService.CanOpen(StorageProvider))
        {
            _statusText.Text = SisterAppFileTextPlanner.FormatCommandUnavailable(SisterAppFileTextPlanner.OpenCommand);
            return null;
        }

        var plan = PresentationFileDialogPlanner.BuildOpenPickerPlan();
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
            path => Task.FromResult(TrySavePresentationFile(path)),
            FileSaveAsAsync);

    private async Task<bool> FileSaveAsAsync()
    {
        if (!AvaloniaFilePickerService.CanSave(StorageProvider))
        {
            _statusText.Text = SisterAppFileTextPlanner.FormatCommandUnavailable(SisterAppFileTextPlanner.SaveCommand);
            return false;
        }

        var plan = PresentationFileDialogPlanner.BuildSavePickerPlan(_fileWorkflow.CurrentFileName);

        using var file = await AvaloniaFilePickerService.PickSaveFileWithLocalPathAsync(
            StorageProvider,
            AvaloniaFilePickerSaveRequest.FromSavePlan(FileText.SavePickerTitle, plan));

        var path = file?.LocalPath;
        if (path is null)
        {
            if (file is not null)
                _statusText.Text = SisterAppFileTextPlanner.FormatSelectedFileNotLocalPath(SisterAppFileTextPlanner.SaveCommand);

            return false;
        }

        return TrySavePresentationFile(path);
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
            ExportAtomicWriter.WriteAllBytes(path, PresentationNotesPagePdfExporter.ExportToBytes(_presentation, request));
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
            _presentation.Slides.Count,
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
        LastPrintOutputPackage = PresentationPrintOutputPackageExecutor.BuildPackage(
            _presentation,
            request,
            SlideRenderer.RenderToBytes,
            SkiaRasterPdfWriter.WriteToBytes);
        _statusText.Text = LastPrintOutputPackage.Plan.DisabledReason ??
            PresentationPrintOutputPackageExecutor.NativePrinterDialogDeferredReason;
        return LastPrintOutputPackage;
    }

    internal PresentationPrintBackstagePlan RefreshPrintBackstagePlan(PresentationPrintRequest? request = null)
    {
        LastPrintBackstagePlan = PresentationPrintBackstagePlanner.Build(
            request,
            _presentation,
            Editor.CurrentSlideIndex + 1,
            request?.SlideRange?.SelectedSlideNumbers);
        _statusText.Text = LastPrintBackstagePlan.DisabledReason ??
            LastPrintBackstagePlan.NativePrinterDialogDeferredMessage;
        return LastPrintBackstagePlan;
    }

    internal PresentationPrintBackstagePlan ShowPrintOptionsPane(PresentationPrintRequest? request = null)
    {
        var plan = RefreshPrintBackstagePlan(request);
        RenderPrintOptionsPane(plan);
        _printOptionsPaneHost.IsVisible = true;
        return plan;
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
        AddPrintOptionsPaneField("Native printer dialog", plan.NativePrinterDialogDeferred ? "Deferred" : "Available");

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

        _printOptionsPaneRowsPanel.Children.Add(new TextBlock
        {
            Text = plan.DisabledReason ?? plan.NativePrinterDialogDeferredMessage,
            TextWrapping = TextWrapping.Wrap,
            FontStyle = FontStyle.Italic,
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
            Margin = new Thickness(12, 10, 12, 12),
        });
    }

    private void AddPrintOptionsPaneSection(string text)
    {
        _printOptionsPaneRowsPanel.Children.Add(new TextBlock
        {
            Text = text,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(12, 10, 12, 4),
        });
    }

    private void AddPrintOptionsPaneField(string label, string value)
    {
        _printOptionsPaneRowsPanel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(12, 3, 12, 5),
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                },
                new TextBlock
                {
                    Text = value,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                },
            },
        });
    }

    private void AddPrintOptionsPaneChoice(string row, bool isAvailable)
    {
        _printOptionsPaneRowsPanel.Children.Add(new TextBlock
        {
            Text = row,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(20, 1, 12, 7),
            Foreground = isAvailable
                ? new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33))
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
        return $"{prefix}{label}{availability}: {description}";
    }

    internal PresentationVideoExportPlan RefreshVideoExportPlan(PresentationVideoExportRequest? request = null)
    {
        LastVideoExportPlan = PresentationExportPlanner.BuildVideoExportPlan(request, _presentation);
        _statusText.Text = LastVideoExportPlan.DisabledReason ?? "Video export planned";
        return LastVideoExportPlan;
    }

    internal PresentationVideoFramePackage RefreshVideoFramePackage(PresentationVideoExportRequest? request = null)
    {
        LastVideoFramePackage = PresentationVideoFramePackageExecutor.BuildPackage(
            _presentation,
            request,
            SlideRenderer.RenderToBytes);
        LastVideoExportPlan = LastVideoFramePackage.Plan.ExportPlan;
        _statusText.Text = LastVideoFramePackage.Plan.DisabledReason ??
            PresentationVideoFramePackageExecutor.EncoderDeferredReason;
        return LastVideoFramePackage;
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
        LastCommentPanePlan = PresentationReviewWorkflowPlanner.BuildCommentPanePlan(
            _presentation.Slides,
            Editor.CurrentSlideIndex,
            _selectedCommentIndex);
        RefreshAccessibilitySummaryPlan();
        RefreshAltTextRequestPlan();
        RefreshReadingOrderPlan();
        RefreshProofingRequestPlan();
    }

    internal PresentationCommentPanePlan ShowReviewCommentsPane()
    {
        var plan = PresentationReviewWorkflowPlanner.BuildCommentPanePlan(
            _presentation.Slides,
            Editor.CurrentSlideIndex,
            _selectedCommentIndex);
        LastCommentPanePlan = plan;
        ShowReviewCommentsPane(plan);
        return plan;
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
            foreach (var comment in plan.Comments)
                _reviewCommentsPanePanel.Children.Add(BuildReviewCommentCard(comment));
        }

        _reviewCommentsPaneHost.IsVisible = true;
    }

    private static Control BuildReviewCommentsPaneHeader(PresentationCommentPanePlan plan)
        => new TextBlock
        {
            Text       = $"Comments - {plan.CurrentSlideSummaryLabel} | {plan.DeckSummaryLabel}",
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D)),
            Margin     = new Thickness(12, 10, 12, 2),
        };

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
    }

    private Control BuildAddCommentInput()
    {
        var input = new TextBox
        {
            PlaceholderText = "Comment",
            MinWidth = 180,
        };
        var button = new Button
        {
            Content = "New Comment",
            MinWidth = 96,
        };
        button.Click += (_, _) => AddComment(input.Text);

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(12, 6, 12, 8),
            Children =
            {
                input,
                button,
            }
        };
    }

    private Control BuildReviewCommentCard(PresentationCommentDescriptor comment)
    {
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing     = 6,
        };
        header.Children.Add(new Border
        {
            Background   = new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A)),
            CornerRadius = new CornerRadius(3),
            Padding      = new Thickness(5, 1, 5, 1),
            Child        = new TextBlock
            {
                Text       = comment.InitialsBadgeText,
                FontSize   = 11,
                Foreground = Brushes.White,
            },
        });
        header.Children.Add(new TextBlock
        {
            Text              = comment.AuthorDisplayName,
            FontWeight        = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        header.Children.Add(new TextBlock
        {
            Text              = comment.ThreadStatusLabel,
            FontSize          = 11,
            Foreground        = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var card = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing     = 4,
        };
        card.Children.Add(header);
        card.Children.Add(new TextBlock
        {
            Text         = comment.TextPreview,
            TextWrapping = TextWrapping.Wrap,
            Foreground   = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
        });
        if (comment.IsSelected && comment.CanEdit)
        {
            var editInput = new TextBox
            {
                Text = GetCommentText(comment.CommentIndex) ?? comment.TextPreview,
                MinWidth = 180,
            };
            var editButton = new Button
            {
                Content = "Save",
                MinWidth = 72,
            };
            editButton.Click += (_, _) => EditSelectedComment(editInput.Text);
            card.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    editInput,
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
                FontSize     = 12,
                Margin       = new Thickness(18, 0, 0, 0),
                Foreground   = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            });
        }
        if (comment.IsSelected && comment.CanReply)
        {
            var replyInput = new TextBox
            {
                PlaceholderText = "Reply",
                MinWidth        = 180,
            };
            var replyButton = new Button
            {
                Content = "Reply",
                MinWidth = 72,
            };
            replyButton.Click += (_, _) => ReplyToSelectedComment(replyInput.Text);
            card.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing     = 6,
                Children    =
                {
                    replyInput,
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
            Padding         = new Thickness(10),
            Margin          = new Thickness(12, 0, 12, 10),
            Child           = card,
        };
        border.Cursor = new Cursor(StandardCursorType.Hand);
        border.PointerPressed += (_, _) => SelectReviewComment(comment.CommentIndex);
        return border;
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
    {
        _selectedCommentIndex = commentIndex;
        return ShowReviewCommentsPane();
    }

    private void SelectReviewComment(int commentIndex)
    {
        _selectedCommentIndex = commentIndex;
        ShowReviewCommentsPane();
        RefreshReviewWorkflowPlans();
    }

    internal PresentationCommentNavigationPlan NavigateReviewComment(
        PresentationReviewWorkflowIntentKind intent)
    {
        var plan = PresentationReviewWorkflowPlanner.BuildCommentNavigationPlan(
            _presentation.Slides,
            Editor.CurrentSlideIndex,
            _selectedCommentIndex,
            intent);
        LastCommentNavigationPlan = plan;
        if (!plan.ShouldNavigate)
        {
            return plan;
        }

        if (Editor.CurrentSlideIndex != plan.TargetSlideIndex)
        {
            Editor.SelectSlide(plan.TargetSlideIndex);
        }

        _selectedCommentIndex = plan.TargetCommentIndex;
        ShowReviewCommentsPane();
        RefreshReviewWorkflowPlans();
        UpdateStatus();
        return plan;
    }

    internal PresentationCommentMutationPlan DeleteSelectedComment()
        => ApplySelectedCommentMutation(
            PresentationReviewWorkflowIntentKind.DeleteComment,
            null,
            null);

    internal PresentationCommentMutationPlan AddComment(
        string? text,
        DateTime? timestamp = null,
        string? author = null,
        string? initials = null,
        long xemu = 0,
        long yemu = 0)
        => ApplySelectedCommentMutation(
            PresentationReviewWorkflowIntentKind.AddComment,
            null,
            null,
            addText: text,
            addTimestamp: timestamp,
            addAuthor: author,
            addInitials: initials,
            addXemu: xemu,
            addYemu: yemu);

    internal PresentationCommentMutationPlan EditSelectedComment(
        string? text,
        string? author = null,
        string? initials = null)
        => ApplySelectedCommentMutation(
            PresentationReviewWorkflowIntentKind.EditComment,
            null,
            null,
            editText: text,
            editAuthor: author,
            editInitials: initials);

    internal PresentationCommentMutationPlan ResolveSelectedComment(
        DateTime? resolvedAt = null,
        string? resolvedBy = null)
        => ApplySelectedCommentMutation(
            PresentationReviewWorkflowIntentKind.ResolveComment,
            resolvedAt,
            resolvedBy);

    internal PresentationCommentMutationPlan ReopenSelectedComment()
        => ApplySelectedCommentMutation(
            PresentationReviewWorkflowIntentKind.ReopenComment,
            null,
            null);

    internal PresentationCommentMutationPlan ReplyToSelectedComment(
        string? text,
        DateTime? timestamp = null,
        string? author = null,
        string? initials = null)
        => ApplySelectedCommentMutation(
            PresentationReviewWorkflowIntentKind.ReplyComment,
            null,
            null,
            text,
            timestamp,
            author,
            initials);

    private PresentationCommentMutationPlan ApplySelectedCommentMutation(
        PresentationReviewWorkflowIntentKind intent,
        DateTime? resolvedAt,
        string? resolvedBy,
        string? replyText = null,
        DateTime? replyTimestamp = null,
        string? replyAuthor = null,
        string? replyInitials = null,
        string? addText = null,
        DateTime? addTimestamp = null,
        string? addAuthor = null,
        string? addInitials = null,
        long addXemu = 0,
        long addYemu = 0,
        string? editText = null,
        string? editAuthor = null,
        string? editInitials = null)
    {
        var selected = _selectedCommentIndex;
        var plan = intent == PresentationReviewWorkflowIntentKind.AddComment
            ? PresentationReviewWorkflowPlanner.BuildAddCommentPlan(
                _presentation.Slides,
                Editor.CurrentSlideIndex,
                addText,
                addAuthor ?? "FreeP User",
                addInitials,
                addXemu,
                addYemu,
                addTimestamp ?? DateTime.UtcNow)
            : selected is { } selectedIndex
            ? intent switch
            {
                PresentationReviewWorkflowIntentKind.EditComment =>
                    PresentationReviewWorkflowPlanner.BuildEditCommentPlan(
                        _presentation.Slides,
                        Editor.CurrentSlideIndex,
                        selectedIndex,
                        editText,
                        editAuthor,
                        editInitials),
                PresentationReviewWorkflowIntentKind.DeleteComment =>
                    PresentationReviewWorkflowPlanner.BuildDeleteCommentPlan(
                        _presentation.Slides,
                        Editor.CurrentSlideIndex,
                        selectedIndex),
                PresentationReviewWorkflowIntentKind.ResolveComment =>
                    PresentationReviewWorkflowPlanner.BuildResolveCommentPlan(
                        _presentation.Slides,
                        Editor.CurrentSlideIndex,
                        selectedIndex,
                        resolvedAt ?? DateTime.UtcNow,
                        resolvedBy ?? "FreeP User"),
                PresentationReviewWorkflowIntentKind.ReplyComment =>
                    PresentationReviewWorkflowPlanner.BuildReplyCommentPlan(
                        _presentation.Slides,
                        Editor.CurrentSlideIndex,
                        selectedIndex,
                        replyText,
                        replyAuthor ?? "FreeP User",
                        replyInitials,
                        replyTimestamp ?? DateTime.UtcNow),
                PresentationReviewWorkflowIntentKind.ReopenComment =>
                    PresentationReviewWorkflowPlanner.BuildReopenCommentPlan(
                        _presentation.Slides,
                        Editor.CurrentSlideIndex,
                        selectedIndex),
                _ => new PresentationCommentMutationPlan(
                    intent,
                    false,
                    Editor.CurrentSlideIndex,
                    selected,
                    null,
                    PresentationReviewWorkflowPlanner.MissingCommentMessage)
            }
            : new PresentationCommentMutationPlan(
                intent,
                false,
                Editor.CurrentSlideIndex,
                selected,
                null,
                PresentationReviewWorkflowPlanner.MissingCommentMessage);

        if (PresentationReviewWorkflowPlanner.TryApplyCommentMutationPlan(_presentation.Slides, plan))
        {
            _selectedCommentIndex = PresentationReviewWorkflowPlanner.NormalizeCommentSelectionAfterMutation(
                _presentation.Slides,
                plan,
                selected);
            _fileWorkflow.MarkDirty();
            ShowReviewCommentsPane();
            RefreshReviewWorkflowPlans();
            UpdateStatus();
        }

        return plan;
    }

    private string? GetSelectedCommentText()
        => _selectedCommentIndex is { } index ? GetCommentText(index) : null;

    private string? GetCommentText(int commentIndex)
    {
        var comments = Editor.CurrentSlide?.Comments;
        return comments is not null && commentIndex >= 0 && commentIndex < comments.Count
            ? comments[commentIndex].Text
            : null;
    }

    private void OnAnimationPaneRequested(PresentationAnimationCommandPlan plan)
    {
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
        return LastAnimationPaneTimelinePlan;
    }

    internal AnimationPaneTimelinePlan ShowAnimationPane(int selectedAnimationIndex = -1)
    {
        var plan = RefreshAnimationPaneTimelinePlan(selectedAnimationIndex);
        RenderAnimationPane(plan);
        _animationPaneHost.IsVisible = true;
        return plan;
    }

    internal void HideAnimationPane()
    {
        if (_animationPaneHost is not null)
            _animationPaneHost.IsVisible = false;
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
        _animationPaneHeading.Text =
            $"Animation Pane - slide {Editor.CurrentSlideIndex + 1} ({plan.Items.Count} animations)";
        _animationPaneMessage.Text = plan.SelectedItem is { } selected
            ? $"Selected: {selected.ShapeName} - {selected.EffectText}"
            : plan.HasAnimations
                ? "Select an animation row to inspect and reorder it."
                : "No animations on this slide.";
        RenderAnimationPanePlaybackControls(plan);

        _animationPaneRenderedRows.Clear();
        _animationPaneItemsPanel.Children.Clear();
        _animationPaneEffectOptionControlCount = 0;
        _animationPaneTriggerControlCount = 0;
        _animationPaneDurationControlCount = 0;
        _animationPaneDelayControlCount = 0;
        if (!plan.HasAnimations)
        {
            _animationPaneItemsPanel.Children.Add(new TextBlock
            {
                Text = "No animations on this slide.",
                Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                Margin = new Thickness(12, 0, 12, 10),
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        foreach (var item in plan.Items)
        {
            _animationPaneRenderedRows.Add(BuildAnimationPaneRowSummary(item));
            _animationPaneItemsPanel.Children.Add(BuildAnimationPaneItemCard(item));
        }
    }

    private void RenderAnimationPanePlaybackControls(AnimationPaneTimelinePlan plan)
    {
        _animationPanePlaybackControlsPanel.Children.Clear();
        _animationPaneRenderedPlaybackControls.Clear();
        foreach (var control in plan.PlaybackControls)
        {
            var button = new Button
            {
                Content = control.Label,
                IsEnabled = control.IsEnabled,
                MinWidth = control.Kind == AnimationPanePlaybackControlKind.PlayFromSelected ? 126 : 82,
                Padding = new Thickness(10, 4),
                Margin = new Thickness(0, 0, 8, 8),
                Tag = control.CommandId,
            };
            ToolTip.SetTip(button, control.DisabledReason ?? control.ToolTip);
            button.Click += (_, _) => ExecuteAnimationPanePlaybackControl(control);
            _animationPanePlaybackControlsPanel.Children.Add(button);
            _animationPaneRenderedPlaybackControls.Add(
                $"{control.Label}: {FormatAvailability(control.IsEnabled)}");

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
        RefreshVisibleAnimationPane(_selectedAnimationIndex);

        if (!control.IsEnabled)
            return _animationPanePlaybackSessionPlan;

        switch (control.Kind)
        {
            case AnimationPanePlaybackControlKind.PreviewCurrentSlide:
            case AnimationPanePlaybackControlKind.PlayFromSelected:
            case AnimationPanePlaybackControlKind.PlayCurrentSlide:
                if (startPreview)
                    StartSlideShow(fromStart: false);
                break;
        }

        return _animationPanePlaybackSessionPlan;
    }

    private Control BuildAnimationPaneItemCard(AnimationPaneTimelineItemPlan item)
    {
        var timingLine =
            $"{item.TriggerText}; duration {item.DurationText}s; delay {item.DelayText}s";
        var timelineLine =
            $"Timeline: starts {item.StartText}s, ends {AnimationPanePlanner.FormatDuration(item.EndMs)}s";
        var actionLine =
            $"Move earlier: {FormatAvailability(item.CanMoveEarlier)}; move later: {FormatAvailability(item.CanMoveLater)}";
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
            Width = 118,
            Margin = new Thickness(0, 4, 8, 0),
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

        var triggerCombo = new ComboBox
        {
            ItemsSource = AnimationPanePlanner.TriggerLabels,
            SelectedIndex = item.TriggerIndex,
            Width = 132,
            Margin = new Thickness(0, 4, 8, 0),
            Tag = item.Index,
        };
        ToolTip.SetTip(triggerCombo, "Trigger");
        triggerCombo.SelectionChanged += (_, _) =>
            ApplyAnimationPaneTriggerEdit(item.Index, triggerCombo.SelectedIndex);
        _animationPaneTriggerControlCount++;

        var durationBox = new TextBox
        {
            Text = item.DurationText,
            Width = 58,
            Margin = new Thickness(0, 4, 8, 0),
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
            Width = 58,
            Margin = new Thickness(0, 4, 8, 0),
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

        var timingControls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                effectOptionCombo,
                triggerCombo,
                durationBox,
                delayBox,
            },
        };

        var moveEarlierButton = new Button
        {
            Content = "Earlier",
            IsEnabled = item.CanMoveEarlier,
            Padding = new Thickness(8, 3),
            Margin = new Thickness(0, 6, 6, 0),
            Tag = item.Index,
        };
        moveEarlierButton.Click += (_, _) => MoveAnimationPaneItem(item.Index, -1);

        var moveLaterButton = new Button
        {
            Content = "Later",
            IsEnabled = item.CanMoveLater,
            Padding = new Thickness(8, 3),
            Margin = new Thickness(0, 6, 6, 0),
            Tag = item.Index,
        };
        moveLaterButton.Click += (_, _) => MoveAnimationPaneItem(item.Index, 1);

        var actionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                moveEarlierButton,
                moveLaterButton,
            }
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 2,
            Children =
            {
                new TextBlock
                {
                    Text = $"{item.OrderText}. {item.ShapeName}",
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = item.EffectText,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = timingLine,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                    TextWrapping = TextWrapping.Wrap,
                },
                timingControls,
                new TextBlock
                {
                    Text = timelineLine,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = actionLine,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                    TextWrapping = TextWrapping.Wrap,
                },
                actionPanel,
            }
        };

        var border = new Border
        {
            Background = item.IsSelected
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0xF6, 0xF2))
                : new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)),
            BorderBrush = item.IsSelected
                ? new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A))
                : new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10),
            Margin = new Thickness(12, 0, 12, 10),
            Child = panel,
        };
        border.Cursor = new Cursor(StandardCursorType.Hand);
        border.PointerPressed += (_, _) => SelectAnimationPaneItem(item.Index);
        return border;
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

    private void MoveAnimationPaneItem(int animationIndex, int offset)
    {
        var intent = AnimationPanePlanner.BuildReorderIntent(
            animationIndex,
            Editor.CurrentSlideAnimations.Count,
            offset);
        if (!intent.CanMove)
            return;

        _selectedAnimationIndex = intent.ToIndex;
        Editor.MoveAnimation(intent.FromIndex, intent.ToIndex);
        RefreshVisibleAnimationPane(_selectedAnimationIndex);
    }

    private static string BuildAnimationPaneRowSummary(AnimationPaneTimelineItemPlan item)
        => $"{item.OrderText}. {item.ShapeName} - {item.EffectText}{FormatEffectOptions(item.EffectOptions)} - {item.TriggerText}; "
            + $"duration {item.DurationText}s; delay {item.DelayText}s; starts {item.StartText}s; "
            + $"move earlier {FormatAvailability(item.CanMoveEarlier)}; move later {FormatAvailability(item.CanMoveLater)}";

    private static string FormatEffectOptions(AnimationPaneEffectOptionsPlan plan)
        => plan.CanApply
            ? $" ({plan.SelectedOptionText})"
            : string.Empty;

    private static string FormatAvailability(bool isAvailable)
        => isAvailable ? "available" : "unavailable";

    private void RefreshAccessibilitySummaryPlan()
    {
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
        return LastAccessibilityCheckerPanePlan!;
    }

    internal PresentationAccessibilityCheckerPanePlan SelectAccessibilityCheckerRow(int rowIndex)
    {
        RefreshAccessibilitySummaryPlan();
        var normalized = LastAccessibilityCheckerPanePlan!.Rows.Any(row => row.RowIndex == rowIndex)
            ? rowIndex
            : LastAccessibilityCheckerPanePlan.SelectedRowIndex;
        _selectedAccessibilityCheckerRowIndex = normalized >= 0 ? normalized : null;
        LastAccessibilityCheckerPanePlan =
            PresentationReviewWorkflowPlanner.BuildAccessibilityCheckerPanePlan(
                _presentation,
                LastAccessibilitySummaryPlan!,
                _selectedAccessibilityCheckerRowIndex);
        if (LastAccessibilityCheckerPanePlan.SelectedRow is { } row)
            NavigateToAccessibilityCheckerRow(row);
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
        else if (row?.CommandHint == PresentationReviewWorkflowPlanner.InsertLinkCommandId)
        {
            OpenHyperlinkDialog();
        }

        return LastAccessibilityCheckerPanePlan!;
    }

    private void NavigateToAccessibilityCheckerRow(PresentationAccessibilityCheckerRowPlan row)
    {
        if (row.ShouldNavigateToSlide)
            Editor.SelectSlide(row.SlideIndex);
        if (row.ShouldSelectShape && row.ShapeId is { } shapeId)
            Editor.Select(shapeId);
    }

    private void RenderAccessibilityCheckerPane(PresentationAccessibilityCheckerPanePlan plan)
    {
        _accessibilityCheckerPaneHeading.Text =
            $"Accessibility - {plan.IssueCount} issues";
        _accessibilityCheckerPaneMessage.Text = plan.SelectedRow is { } selected
            ? $"{selected.SlideDisplay}: {selected.Title}"
            : "No accessibility issues found.";

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
            Tag = row.RowIndex,
            MinWidth = 96,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
        };
        ToolTip.SetTip(action, row.CommandHint);
        action.Click += (_, _) => ApplyAccessibilityCheckerRowAction(row.RowIndex);

        var select = new Button
        {
            Content = "Select",
            Tag = row.RowIndex,
            MinWidth = 72,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 6, 0),
        };
        select.Click += (_, _) => SelectAccessibilityCheckerRow(row.RowIndex);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                select,
                action,
            }
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 2,
            Children =
            {
                new TextBlock
                {
                    Text = $"{row.SlideDisplay} - {row.Title}",
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(row.ShapeName)
                        ? $"{row.Severity} - {row.Category}"
                        : $"{row.Severity} - {row.Category} - {row.ShapeName}",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = row.Detail,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                    TextWrapping = TextWrapping.Wrap,
                },
                buttons,
            }
        };

        if (row.IsSelected)
        {
            panel.Children.Insert(1, new TextBlock
            {
                Text = "Selected issue",
                Foreground = new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A)),
                FontWeight = FontWeight.SemiBold,
            });
        }

        return new Border
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
    }

    private void RefreshAltTextRequestPlan()
    {
        RefreshAltTextPlans(proposedDescription: null, proposedTitle: null, isDecorative: null);
        if (IsAltTextPaneVisible && LastAltTextPanePlan is not null)
            RenderAltTextPane(LastAltTextPanePlan);
    }

    internal void ShowAltTextPane()
    {
        RefreshAltTextPlans(proposedDescription: null, proposedTitle: null, isDecorative: null);
        if (LastAltTextPanePlan is not null)
            RenderAltTextPane(LastAltTextPanePlan);
        _altTextPaneHost.IsVisible = true;
    }

    internal void HideAltTextPane()
    {
        if (_altTextPaneHost is not null)
            _altTextPaneHost.IsVisible = false;
    }

    internal PresentationReadingOrderPlan ShowReadingOrderPane()
    {
        var plan = RefreshReadingOrderPlan();
        RenderReadingOrderPane(plan);
        _readingOrderPaneHost.IsVisible = true;
        return plan;
    }

    internal PresentationReadingOrderMutationPlan ApplyReadingOrderMoveEarlier()
        => ApplyReadingOrderMove(PresentationReviewWorkflowIntentKind.MoveReadingOrderEarlier);

    internal PresentationReadingOrderMutationPlan ApplyReadingOrderMoveLater()
        => ApplyReadingOrderMove(PresentationReviewWorkflowIntentKind.MoveReadingOrderLater);

    internal PresentationReadingOrderSelectionPlan ApplyReadingOrderSelectItem(uint shapeId)
    {
        var plan = PresentationReviewWorkflowPlanner.TryApplyReadingOrderSelection(Editor, shapeId);
        RefreshReadingOrderPlan();
        if (IsReadingOrderPaneVisible && LastReadingOrderPlan is not null)
            RenderReadingOrderPane(LastReadingOrderPlan);
        return plan;
    }

    private PresentationReadingOrderMutationPlan ApplyReadingOrderMove(
        PresentationReviewWorkflowIntentKind intent)
    {
        var plan = PresentationReviewWorkflowPlanner.TryApplyReadingOrderMove(Editor, intent);
        RefreshReadingOrderPlan();
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
    {
        var selectedShapeId = GetSingleSelectedShapeId();
        LastAltTextRequestPlan = PresentationReviewWorkflowPlanner.BuildAltTextRequestPlan(
            Editor.CurrentSlide,
            selectedShapeId,
            proposedDescription,
            proposedTitle,
            isDecorative);
        LastAltTextPanePlan = PresentationReviewWorkflowPlanner.BuildAltTextPanePlan(
            Editor.CurrentSlide,
            selectedShapeId,
            proposedDescription,
            proposedTitle,
            isDecorative);
    }

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
            _readingOrderPaneItemsPanel.Children.Add(BuildReadingOrderItemCard(item));
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
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = $"{item.ShapeTypeLabel} - depth {item.NestingDepth}",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = item.AccessibilitySummary,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = BuildReadingOrderAltTextLine(item),
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
                Foreground = new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A)),
                FontWeight = FontWeight.SemiBold,
            });
        }

        var card = new Border
        {
            Background = item.IsSelected
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0xF6, 0xF2))
                : new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)),
            BorderBrush = item.IsSelected
                ? new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A))
                : new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10),
            Margin = new Thickness(12, 0, 12, 10),
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
    {
        LastReadingOrderPlan = PresentationReviewWorkflowPlanner.BuildReadingOrderPlan(
            Editor.CurrentSlide,
            Editor.CurrentSlideIndex,
            Editor.SelectedShapeIds);
        return LastReadingOrderPlan;
    }

    internal PresentationAltTextMutationPlan ApplySelectedShapeAlternativeText(
        string? description,
        string? title = null,
        bool isDecorative = false)
    {
        uint? selectedShapeId = GetSingleSelectedShapeId();
        var plan = PresentationReviewWorkflowPlanner.BuildAltTextMutationPlan(
            Editor.CurrentSlide,
            Editor.CurrentSlideIndex,
            selectedShapeId,
            description,
            title,
            isDecorative);
        if (plan.ShouldApply)
        {
            Editor.SetSelectedShapeAlternativeText(plan.Description, plan.Title, plan.IsDecorative);
            LastAltTextRequestPlan = PresentationReviewWorkflowPlanner.BuildAltTextRequestPlan(
                Editor.CurrentSlide,
                plan.ShapeId,
                plan.Description,
                plan.Title,
                plan.IsDecorative);
            LastAltTextPanePlan = PresentationReviewWorkflowPlanner.BuildAltTextPanePlan(
                Editor.CurrentSlide,
                plan.ShapeId,
                plan.Description,
                plan.Title,
                plan.IsDecorative);
            RefreshAccessibilitySummaryPlan();
        }

        return plan;
    }

    internal PresentationProofingCorrectionMutationPlan ApplyProofingCorrection(
        PresentationProofingScopeDescriptor scope,
        int start,
        int length,
        string? replacement)
    {
        var plan = PresentationReviewWorkflowPlanner.TryApplyProofingCorrection(
            _presentation,
            scope,
            start,
            length,
            replacement);
        if (plan.ShouldApply)
        {
            _fileWorkflow.MarkDirty();
            RefreshCanvas();
            RefreshNotesPane();
            RefreshReviewWorkflowPlans();
            UpdateStatus();
        }

        return plan;
    }

    private void RefreshProofingRequestPlan()
    {
        LastProofingExecutionPlan =
            PresentationReviewWorkflowPlanner.BuildProofingExecutionPlan(_presentation);
        LastProofingRequestPlan =
            PresentationReviewWorkflowPlanner.BuildProofingRequestPlan(_presentation);
        LastProofingPanePlan =
            PresentationReviewWorkflowPlanner.BuildProofingPanePlan(
                LastProofingExecutionPlan,
                _selectedProofingIssueRowIndex);
        _selectedProofingIssueRowIndex = LastProofingPanePlan.SelectedRowIndex >= 0
            ? LastProofingPanePlan.SelectedRowIndex
            : null;
        if (IsProofingPaneVisible)
            RenderProofingPane(LastProofingPanePlan);
    }

    internal PresentationProofingPanePlan ShowProofingPane()
    {
        RefreshProofingRequestPlan();
        RenderProofingPane(LastProofingPanePlan!);
        _proofingPaneHost.IsVisible = true;
        return LastProofingPanePlan!;
    }

    internal PresentationProofingPanePlan SelectProofingIssueRow(int rowIndex)
    {
        RefreshProofingRequestPlan();
        var normalized = LastProofingPanePlan!.Rows.Any(row => row.RowIndex == rowIndex)
            ? rowIndex
            : LastProofingPanePlan.SelectedRowIndex;
        _selectedProofingIssueRowIndex = normalized >= 0 ? normalized : null;
        LastProofingPanePlan = PresentationReviewWorkflowPlanner.BuildProofingPanePlan(
            LastProofingExecutionPlan!,
            _selectedProofingIssueRowIndex);
        RenderProofingPane(LastProofingPanePlan);
        _proofingPaneHost.IsVisible = true;
        return LastProofingPanePlan;
    }

    internal PresentationProofingCorrectionMutationPlan ApplySelectedProofingCorrection()
    {
        if (LastProofingPanePlan is null)
            ShowProofingPane();

        var selectedRow = LastProofingPanePlan!.SelectedRow;
        if (selectedRow is null)
        {
            return new PresentationProofingCorrectionMutationPlan(
                false,
                new PresentationProofingScopeDescriptor(
                    PresentationProofingScopeKind.SlideTitle,
                    -1,
                    null,
                    null,
                    null,
                    null,
                    null,
                    string.Empty,
                    string.Empty,
                    string.Empty),
                0,
                0,
                string.Empty,
                null,
                PresentationReviewWorkflowPlanner.ProofingMissingIssueMessage);
        }

        var previousSelection = LastProofingPanePlan.SelectedRowIndex;
        var mutation = ApplyProofingCorrection(
            selectedRow.Scope,
            selectedRow.Start,
            selectedRow.Length,
            selectedRow.SuggestedReplacement);
        if (mutation.ShouldApply)
        {
            var refreshed = PresentationReviewWorkflowPlanner.BuildProofingPanePlan(LastProofingExecutionPlan!);
            LastProofingPanePlan = PresentationReviewWorkflowPlanner.BuildProofingPanePlan(
                LastProofingExecutionPlan!,
                PresentationReviewWorkflowPlanner.NormalizeProofingSelectionAfterCorrection(
                    previousSelection,
                    refreshed));
            _selectedProofingIssueRowIndex = LastProofingPanePlan.SelectedRowIndex >= 0
                ? LastProofingPanePlan.SelectedRowIndex
                : null;
            if (IsProofingPaneVisible)
                RenderProofingPane(LastProofingPanePlan);
        }

        return mutation;
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
            Children = { action, select },
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

        return new Border
        {
            Background = row.IsSelected ? new SolidColorBrush(Color.FromRgb(0xE8, 0xF1, 0xFF)) : Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 8, 12, 8),
            Child = panel,
        };
    }

    private bool TryLoadPresentationFile(string path)
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
            return false;
        }
    }

    private bool TrySavePresentationFile(string path)
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
            return false;
        }
    }

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
        _presentation = presentation;

        RebuildEditorAndRewireInteraction();
        HideLayoutPicker();
        HideTablePicker();
        RefreshSlidePane();
        RefreshCanvas();
        RefreshNotesPane();
        RefreshReviewWorkflowPlans();
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
        _slidePaneRefreshing = true;
        try
        {
            _slidePaneList.Items.Clear();

            var entries = SlidePanePlanner.BuildEntries(
                _presentation.Slides,
                _presentation.Sections,
                _slidePaneCollapsedSectionIds);
            foreach (var entry in entries)
            {
                if (entry.Kind == SlidePaneEntryKind.SectionHeader)
                {
                    _slidePaneList.Items.Add(BuildSlidePaneSectionHeader(entry));
                    continue;
                }

                var slide = _presentation.Slides[entry.SlideIndex];
                var plan = SlidePanePlanner.BuildThumbnailVisualPlan(
                    entry,
                    slide,
                    Editor.CurrentSlideIndex);

                // Small SlideCanvas thumbnail using the shared slide pane metrics.
                var thumb = new SlideCanvas
                {
                    Presentation = _presentation,
                    Slide        = slide,
                    SlideIndex   = plan.SlideIndex,
                    Width        = plan.ThumbnailWidth,
                    Height       = plan.ThumbnailHeight,
                };

                // Slide number label beneath thumbnail.
                var label = new TextBlock
                {
                    Text                = plan.LabelText,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontSize            = 10,
                    MinHeight           = plan.LabelHeight,
                    Margin              = new Thickness(0, 2, 0, 0),
                };

                var panel = new StackPanel
                {
                    Margin   = new Thickness(plan.ItemPadding * 0.5),
                    Children = { thumb, label },
                };

                var item = new ListBoxItem
                {
                    Tag         = plan.SlideIndex,
                    Content     = panel,
                    Padding     = new Thickness(2),
                    MinHeight   = plan.ItemHeight,
                    IsSelected  = plan.IsSelected,
                    ContextMenu = BuildSlidePaneContextMenu(plan.SlideIndex),
                };
                ToolTip.SetTip(item, plan.ToolTipText);
                WireSlidePaneDragHandlers(item);
                _slidePaneList.Items.Add(item);
            }

            SelectSlidePaneItem(Editor.CurrentSlideIndex);
        }
        finally
        {
            _slidePaneRefreshing = false;
        }
    }

    private ListBoxItem BuildSlidePaneSectionHeader(SlidePaneEntry entry)
    {
        var disclosure = new TextBlock
        {
            Text              = entry.IsSectionCollapsed ? ">" : "v",
            FontSize          = 11,
            FontWeight        = FontWeight.Bold,
            Foreground        = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
            Width             = 14,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var label = new TextBlock
        {
            Text              = entry.Text,
            FontSize          = 11,
            FontWeight        = FontWeight.SemiBold,
            Foreground        = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming      = TextTrimming.CharacterEllipsis,
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { disclosure, label },
        };

        var item = new ListBoxItem
        {
            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
                Padding    = new Thickness(10, 4),
                Child      = row,
            },
            Padding     = new Thickness(0),
            Margin      = new Thickness(0, 6, 0, 2),
            Focusable   = true,
            Tag         = new SlidePaneSectionHeaderTag(entry.SectionId, entry.SectionIndex),
            Cursor      = new Cursor(StandardCursorType.Hand),
            ContextMenu = BuildSlidePaneSectionContextMenu(entry),
        };
        item.PointerPressed += (_, e) =>
        {
            var point = e.GetCurrentPoint(item);
            if (!point.Properties.IsLeftButtonPressed)
                return;

            ToggleSlidePaneSection(entry.SectionId);
            e.Handled = true;
        };
        item.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space)
            {
                ToggleSlidePaneSection(entry.SectionId);
                e.Handled = true;
            }
        };

        return item;
    }

    private ContextMenu BuildSlidePaneContextMenu(int slideIndex)
    {
        var menu = new ContextMenu();

        foreach (var action in SlideSectionPlanner.BuildSlideContextActions(
                     _presentation.Slides,
                     _presentation.Sections,
                     slideIndex))
        {
            var item = new MenuItem
            {
                Header = action.Text,
                IsEnabled = action.IsEnabled,
            };
            item.Click += async (_, _) => await ApplySlideSectionActionAsync(action);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());

        foreach (var action in SlidePanePlanner.BuildContextActions(_presentation.Slides.Count, slideIndex))
        {
            if (action.Kind == SlidePaneActionKind.DeleteSlide)
                menu.Items.Add(new Separator());

            var item = new MenuItem
            {
                Header = action.Text,
                IsEnabled = action.IsEnabled,
            };
            item.Click += (_, _) => TryApplySlidePaneContextAction(slideIndex, action.Kind);
            menu.Items.Add(item);
        }

        return menu;
    }

    internal bool TryApplySlidePaneContextAction(int slideIndex, SlidePaneActionKind kind)
    {
        var action = SlidePanePlanner.BuildContextActions(_presentation.Slides.Count, slideIndex)
            .FirstOrDefault(candidate => candidate.Kind == kind);

        return action is not null && SlidePanePlanner.TryApplyAction(Editor, action);
    }

    private ContextMenu BuildSlidePaneSectionContextMenu(SlidePaneEntry entry)
    {
        var menu = new ContextMenu();

        foreach (var action in SlideSectionPlanner.BuildSectionHeaderActions(
                     _presentation.Sections,
                     entry.SectionIndex,
                     entry.SlideIndex))
        {
            if (action.Kind == SlideSectionActionKind.RemoveSection)
                menu.Items.Add(new Separator());

            var item = new MenuItem
            {
                Header = action.Text,
                IsEnabled = action.IsEnabled,
            };
            item.Click += async (_, _) => await ApplySlideSectionActionAsync(action);
            menu.Items.Add(item);
        }

        return menu;
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

        if (!_slidePaneCollapsedSectionIds.Add(sectionId))
            _slidePaneCollapsedSectionIds.Remove(sectionId);

        RefreshSlidePane();
    }

    internal bool ToggleSlidePaneSectionForTests(int sectionIndex)
    {
        if (sectionIndex < 0 || sectionIndex >= _presentation.Sections.Count)
            return false;

        ToggleSlidePaneSection(SlidePanePlanner.GetSectionIdentity(_presentation.Sections[sectionIndex], sectionIndex));
        return true;
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

        _slidePaneDragSourceIndex = sourceSlideIndex;
        _slidePaneDragTargetIndex = sourceSlideIndex;
        _slidePaneDragStartPoint = e.GetPosition(item);
    }

    private void OnSlidePaneItemPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not ListBoxItem item || _slidePaneDragSourceIndex < 0)
            return;

        var point = e.GetCurrentPoint(item);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        var itemPosition = e.GetPosition(item);
        if (!_slidePaneIsDragging &&
            Math.Abs(itemPosition.Y - _slidePaneDragStartPoint.Y) < SlidePanePlanner.DefaultDragStartThreshold)
            return;

        if (!_slidePaneIsDragging)
        {
            _slidePaneIsDragging = true;
            e.Pointer.Capture(item);
        }

        var panePosition = e.GetPosition(_slidePaneList);
        _slidePaneDragTargetIndex = SlidePanePlanner.HitTestInsertionPoint(
            GetSlidePaneItemKinds(),
            panePosition.Y,
            SlidePanePlanner.DefaultSlideItemHeight);
        ShowSlidePaneInsertionIndicator();
        e.Handled = true;
    }

    private void OnSlidePaneItemPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_slidePaneIsDragging)
        {
            _slidePaneDragSourceIndex = -1;
            _slidePaneDragTargetIndex = -1;
            return;
        }

        var sourceSlideIndex = _slidePaneDragSourceIndex;
        var targetInsertionIndex = _slidePaneDragTargetIndex;
        _slidePaneIsDragging = false;
        _slidePaneDragSourceIndex = -1;
        _slidePaneDragTargetIndex = -1;
        e.Pointer.Capture(null);
        HideSlidePaneInsertionIndicator();

        TryApplySlidePaneMove(sourceSlideIndex, targetInsertionIndex);
        e.Handled = true;
    }

    private void OnSlidePaneItemPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _slidePaneIsDragging = false;
        _slidePaneDragSourceIndex = -1;
        _slidePaneDragTargetIndex = -1;
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

    internal bool ClickSlidePaneNewSlideAffordanceForTests()
    {
        var before = _presentation.Slides.Count;
        InsertSlideFromSlidePaneAffordance();
        return _presentation.Slides.Count == before + 1;
    }

    private Button BuildSlidePaneNewSlideButton()
    {
        var button = new Button
        {
            Content                    = SlidePanePlanner.NewSlideButtonText,
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
        };
        button.Click += (_, _) => InsertSlideFromSlidePaneAffordance();
        return button;
    }

    private void InsertSlideFromSlidePaneAffordance() =>
        Editor.InsertSlide();

    private void ShowSlidePaneInsertionIndicator()
    {
        var plan = SlidePanePlanner.BuildDropVisualPlan(
            GetSlidePaneItemKinds(),
            _slidePaneDragSourceIndex,
            _slidePaneDragTargetIndex,
            SlidePanePlanner.DefaultSlideItemHeight);

        if (!plan.IsVisible)
        {
            HideSlidePaneInsertionIndicator();
            return;
        }

        _slidePaneInsertionIndicator.Height = plan.IndicatorThickness;
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
        _slidePaneList.Items
            .OfType<ListBoxItem>()
            .Select(item => item.Tag is int)
            .ToArray();

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
            var notes = Editor.CurrentSlideNotes;
            _notesBox.Text = notes is null
                ? string.Empty
                : string.Join(
                    Environment.NewLine,
                    notes.Paragraphs.Select(p => string.Concat(p.Runs.Select(r => r.Text))));
        }
        finally
        {
            _notesRefreshing = false;
        }
    }

    private void OnNotesTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_notesRefreshing)
            return;
        Editor.SetCurrentSlideNotesText(_notesBox.Text);
        LastNotesPagePreviewPlan = PresentationNotesPagePreviewPlanner.Build(
            _presentation,
            Editor.CurrentSlideIndex);
    }

    // ── Event handlers ─────────────────────────────────────────────────────────

    private void OnEditorChanged()
    {
        _fileWorkflow.MarkDirty();
        RefreshSlidePane();
        RefreshCanvas(); // refresh canvas so shape moves/resizes are reflected immediately
        RefreshNotesPane();
        RefreshReviewWorkflowPlans();
        RefreshVisibleAnimationPane(_selectedAnimationIndex);
        UpdateStatus();
    }

    private void OnCurrentSlideChanged(object? sender, EventArgs e)
    {
        _selectedCommentIndex = null;
        _selectedAnimationIndex = -1;

        // Sync slide-pane selection without re-triggering OnSlidePaneSelectionChanged.
        _slidePaneRefreshing = true;
        try { SelectSlidePaneItem(Editor.CurrentSlideIndex); }
        finally { _slidePaneRefreshing = false; }

        RefreshCanvas();
        RefreshNotesPane();
        RefreshReviewWorkflowPlans();
        RefreshVisibleAnimationPane();
        UpdateStatus();
    }

    private void OnEditorSelectionChanged(object? sender, EventArgs e)
    {
        RefreshAltTextRequestPlan();
        RefreshReadingOrderPlan();
        if (IsAltTextPaneVisible)
            ShowAltTextPane();
        RefreshVisibleAnimationPane();
    }

    // ── Status ─────────────────────────────────────────────────────────────────

    private void UpdateStatus()
    {
        var count   = _presentation.Slides.Count;
        var current = Editor.CurrentSlideIndex;
        _statusText.Text = SisterAppStatusBarTextPlanner.FormatPresentationSlideStatus(current, count);
    }

    // ── Keyboard shortcuts ─────────────────────────────────────────────────────

    private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        var ctrl = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;

        // ── Ctrl shortcuts ──────────────────────────────────────────────────────
        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.N: FileNew(); e.Handled = true; return;
                case Key.O: _ = FileOpenAsync(); e.Handled = true; return;
                case Key.S when (e.KeyModifiers & KeyModifiers.Shift) != 0:
                    _ = FileSaveAsAsync(); e.Handled = true; return;
                case Key.S: _ = FileSaveAsync(); e.Handled = true; return;
                case Key.Z: Editor.Undo(); e.Handled = true; return;
                case Key.Y: Editor.Redo(); e.Handled = true; return;
                case Key.A: Editor.SelectAll(); e.Handled = true; return;
            }
        }

        // ── Slide show keys (no modifier) ─────────────────────────────────────
        if (!ctrl)
        {
            switch (e.Key)
            {
                case Key.F5 when (e.KeyModifiers & KeyModifiers.Shift) != 0:
                    StartSlideShow(fromStart: false);
                    e.Handled = true;
                    return;
                case Key.F5:
                    StartSlideShow(fromStart: true);
                    e.Handled = true;
                    return;
            }
        }

        // ── Arrow / Delete keys — delegate to gesture handler (Theme 15) ────────
        if (_gestureHandler is not null)
        {
            // Skip if text editor is active (keys go into the TextBox).
            if (_textEditor is { IsActive: true }) return;

            if (_gestureHandler.HandleKeyDown(e.Key, e.KeyModifiers))
            {
                e.Handled = true;
                // Refresh canvas + adorner after model change.
                _slideCanvas.Refresh();
            }
        }
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
    {
        if (_presentation.Slides.Count == 0)
            return; // nothing to show

        int startIdx = fromStart ? 0 : Math.Max(0, Editor.CurrentSlideIndex);
        var slideShow = new SlideShowWindow(_presentation, startIdx);

        // DA5: restore the editor's selected slide to wherever the slideshow ended.
        slideShow.Closed += (_, _) =>
        {
            int exitIdx = slideShow.Controller.CurrentSlideIndex;
            if (exitIdx >= 0 && exitIdx < _presentation.Slides.Count)
                Editor.SelectSlide(exitIdx);
        };

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

    internal bool TryStartCustomSlideShow(string? customShowName, int startIndex = 0)
    {
        if (!TryBuildCustomSlideShowRoute(customShowName, startIndex, out var route) ||
            route.SlideCount == 0)
        {
            return false;
        }

        var slideShow = new SlideShowWindow(_presentation, route);
        slideShow.Closed += (_, _) =>
        {
            int exitIdx = slideShow.Controller.CurrentSlideIndex;
            int sourceIdx = route.GetSourceSlideIndex(exitIdx);
            if (sourceIdx >= 0 && sourceIdx < _presentation.Slides.Count)
                Editor.SelectSlide(sourceIdx);
        };

        slideShow.Show();
        return true;
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
