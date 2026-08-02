using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Free.Shared.AppServices;
using Free.Shared.Drawing;
using Free.Shared.Ribbon.Wpf;
using Free.Shared.Shell.Wpf;
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
public sealed partial class MainWindow : Window
{
    // Identity/palette for the shared window shell (PowerPoint-style brick title bar; "P" badge).
    private static ShellChromeOptions BuildChromeOptions() => new()
    {
        BadgeLetter = "P",
        TitleBarColor = ResolveTokenColor("FreePTitleBarBrush",   Color.FromRgb(0xB7, 0x47, 0x2A)),
        BadgeColor    = ResolveTokenColor("FreePAccentDarkBrush", Color.FromRgb(0x8F, 0x37, 0x21)),
        CaptionHeight = FreePShellVisualMetrics.TitleBarHeight,
        IconUri = "pack://application:,,,/FreeP.App.Host;component/Resources/FreeP.ico"
    };

    private static Color ResolveTokenColor(string key, Color fallback)
    {
        if (System.Windows.Application.Current?.Resources[key] is SolidColorBrush brush)
            return brush.Color;
        return fallback;
    }

    private static Brush? ResolveTokenBrush(string key)
    {
        if (System.Windows.Application.Current?.Resources[key] is Brush brush)
            return brush;
        return null;
    }

    private readonly FreePOptions _options;
    private readonly ApplicationOptionsStore<FreePOptions> _optionsStore;
    private readonly IUserMessageService? _messageService;

    // ── Wave 10B: OS-clipboard service ────────────────────────────────────────────
    // Created once; the renderer is injected so tests can replace it without real Clipboard.
    private readonly OsClipboardService _osClipboard =
        new OsClipboardService(new WpfOsClipboard(), new WpfShapeRenderer());

    // ── Model ─────────────────────────────────────────────────────────────────────

    private Presentation _presentation = Presentation.CreateEmpty();

    // ── Editing session (Wave 3A) ─────────────────────────────────────────────────

    /// <summary>
    /// The active editing session. 3B (thumbnail pane) and 3C (canvas interaction) consume this.
    /// Rebuilt on every file new/open — subscribers re-attach after LoadModel.
    /// </summary>
    internal EditingSession Editor { get; private set; } = null!;

    // ── Shell chrome ──────────────────────────────────────────────────────────────

    private FileCommands _file = null!;
    private BackstageView _backstage = null!;
    private Border _titleBar = null!;
    private SisterWpfWindowTitleBinder _titleBinder = null!;
    private TabControl _ribbonTabs = null!;
    private TabItem _fileTab = null!;
    private RibbonFileTabRouter? _fileTabRouter;

    // ── Body layout ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Left pane host seam — Wave 3B fills this with the slide-thumbnail pane.
    /// Named exactly "_slidePaneHost" so 3B can attach without restructuring.
    /// Currently rendered as an empty 180px-wide border region.
    /// <!-- 3B SEAM: add your thumbnail pane as the Child of this Border. -->
    /// </summary>
    internal Border SlidePaneHost { get; private set; } = null!;

    /// <summary>
    /// The centre-stage canvas. 3C attaches interaction (mouse/keyboard) to this.
    /// The canvas is bound to Editor.CurrentSlide.
    /// <!-- 3C SEAM: attach mouse handlers + adorner layer to _slideCanvas. -->
    /// </summary>
    internal SlideCanvas SlideCanvas { get; private set; } = null!;

    private Border _canvasHost = null!;
    private Canvas _textOverlay = null!;
    private Canvas _oleOverlay = null!;
    private WpfOleInPlaceHost? _activeOleHost;
    private TextBlock _slideCountText = null!;
    private PresentationViewShowState _viewShowState = PresentationViewShowState.Default;
    private PresentationViewZoomState _viewZoomState = PresentationViewZoomState.FitToWindow;

    // Notes pane (Wave 7B)
    private TextBox _notesBox = null!;
    private bool _notesRefreshing;   // guard against re-entrant TextChanged → SetCurrentSlideNotesText

    // Comment indicator overlay + list pane (Wave 11B)
    private Canvas  _commentOverlay = null!;  // hosts speech-bubble dots over the slide canvas
    private StackPanel _commentListPanel = null!; // shows comment text list below canvas
    private Border  _commentListHost = null!; // collapsible container for _commentListPanel
    private bool _reviewCommentsPaneRequested;
    private readonly PresentationReviewWorkflowSession _reviewWorkflowSession;
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
    private bool _smartArtTextPaneRefreshing;
    private string? _selectedSmartArtTextPaneModelId;

    internal PresentationCommentPanePlan? LastCommentPanePlan => _reviewWorkflowSession.LastCommentPanePlan;
    internal PresentationCommentNavigationPlan? LastCommentNavigationPlan => _reviewWorkflowSession.LastCommentNavigationPlan;
    internal PresentationCommentMentionPickerPlan? LastCommentMentionPickerPlan { get; private set; }
    internal PresentationCommentMentionInsertionPlan? LastCommentMentionInsertionPlan { get; private set; }
    internal PresentationAccessibilitySummaryPlan? LastAccessibilitySummaryPlan { get; private set; }
    internal PresentationAccessibilityCheckerPanePlan? LastAccessibilityCheckerPanePlan { get; private set; }
    internal PresentationSlideTitleMutationPlan? LastSlideTitleMutationPlan { get; private set; }
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
    internal PresentationDesignCommandPlan? LastLayoutRequestPlan { get; private set; }
    internal PresentationNotesPagePreviewPlan? LastNotesPagePreviewPlan { get; private set; }
    internal PresentationNotesPagePdfRenderPlan? LastNotesPagePdfRenderPlan { get; private set; }
    internal PresentationPrintOutputPackage? LastPrintOutputPackage { get; private set; }
    internal PresentationPrintBackstagePlan? LastPrintBackstagePlan { get; private set; }
    internal PresentationPrintBackstagePlan? LastFilePrintBackstagePlanForTests => _file.LastPrintBackstagePlan;
    internal PresentationVideoExportPlan? LastVideoExportPlan { get; private set; }
    internal PresentationVideoFramePackage? LastVideoFramePackage { get; private set; }
    internal PresentationVideoExportHandoffPlan? LastVideoExportHandoffPlan { get; private set; }
    internal PresentationLayoutPickerPlan? LastLayoutPickerPlan { get; private set; }
    internal PresentationLayoutChoice? LastAppliedLayoutChoice { get; private set; }
    internal TableInsertionPickerPlan? LastTablePickerPlan { get; private set; }
    internal bool IsLayoutPickerVisible => _layoutPickerHost?.Visibility == Visibility.Visible;
    internal bool IsTablePickerVisible => _tablePickerHost?.Visibility == Visibility.Visible;
    internal int TablePickerChoiceButtonCount => LastTablePickerPlan?.Choices.Count ?? 0;
    internal int TablePickerDefaultChoiceCount => LastTablePickerPlan?.Choices.Count(choice => choice.IsDefault) ?? 0;
    internal int LayoutPickerChoiceButtonCount => LastLayoutPickerPlan?.Choices.Count ?? 0;
    internal int LayoutPickerGroupHeaderCount => LastLayoutPickerPlan?.Groups.Count ?? 0;
    internal int LayoutPickerThumbnailPlaceholderCount =>
        LastLayoutPickerPlan?.Choices.Sum(choice => choice.ThumbnailPlaceholders.Count) ?? 0;
    internal int LayoutPickerCurrentChoiceCount =>
        LastLayoutPickerPlan?.Choices.Count(choice => choice.Chrome.IsCurrent) ?? 0;
    internal bool IsAltTextPaneVisible => _altTextPaneHost?.Visibility == Visibility.Visible;
    internal bool IsAltTextPaneApplyEnabled => _altTextApplyButton?.IsEnabled == true;
    internal string AltTextPaneTitleLabel => _altTextTitleLabel?.Text ?? string.Empty;
    internal string AltTextPaneTitleText => _altTextTitleBox?.Text ?? string.Empty;
    internal string AltTextPaneTitlePlaceholder => LastAltTextPanePlan?.Title.Placeholder ?? string.Empty;
    internal string AltTextPaneDescriptionLabel => _altTextDescriptionLabel?.Text ?? string.Empty;
    internal string AltTextPaneDescriptionText => _altTextDescriptionBox?.Text ?? string.Empty;
    internal string AltTextPaneDescriptionPlaceholder => LastAltTextPanePlan?.Description.Placeholder ?? string.Empty;
    internal bool IsAltTextPaneDecorativeChecked => _altTextDecorativeCheck?.IsChecked == true;
    internal string AltTextPaneMessage => _altTextPaneMessage?.Text ?? string.Empty;
    internal bool IsSmartArtTextPaneVisible => _smartArtTextPaneHost?.Visibility == Visibility.Visible;
    internal int SmartArtTextPaneRowCount => _smartArtTextPaneRowsPanel?.Children.OfType<TextBox>().Count() ?? 0;
    internal int SmartArtTextPaneSelectedRowCount =>
        _smartArtTextPaneRowsPanel?.Children.OfType<TextBox>().Count(box =>
            box.Tag is SmartArtNodeOutlineItem item &&
            StringComparer.Ordinal.Equals(item.ModelId, _selectedSmartArtTextPaneModelId)) ?? 0;
    internal int SmartArtTextPaneActionButtonCount => _smartArtTextPaneActionButtons.Count;
    internal int SmartArtTextPaneEnabledActionButtonCount =>
        _smartArtTextPaneActionButtons.Count(button => button.IsEnabled);
    internal string SmartArtTextPaneMessage => _smartArtTextPaneMessage?.Text ?? string.Empty;
    internal IReadOnlyList<string> SmartArtTextPaneRenderedRows =>
        _smartArtTextPaneRowsPanel?.Children.OfType<TextBox>()
            .Select(box => box.Tag is SmartArtNodeOutlineItem item
                ? $"{item.ModelId}|{item.Level}|{item.IsAssistant}|{box.Text}"
                : box.Text)
            .ToArray() ?? [];
    internal bool IsAccessibilityCheckerPaneVisible => _accessibilityCheckerPaneHost?.Visibility == Visibility.Visible;
    internal int AccessibilityCheckerPaneRowCount => LastAccessibilityCheckerPanePlan?.Rows.Count ?? 0;
    internal int AccessibilityCheckerPaneSelectedRowCount =>
        LastAccessibilityCheckerPanePlan?.Rows.Count(row => row.IsSelected) ?? 0;
    internal string AccessibilityCheckerPaneHeading => _accessibilityCheckerPaneHeading?.Text ?? string.Empty;
    internal string AccessibilityCheckerPaneMessage => _accessibilityCheckerPaneMessage?.Text ?? string.Empty;
    internal IReadOnlyList<string> AccessibilityCheckerTableStructureReviewRenderedLines =>
        _accessibilityCheckerTableStructureReviewRenderedLines.ToArray();
    internal bool IsDirty => _file.IsDirty;
    internal int ReviewCommentSelectedCount => LastCommentPanePlan?.Comments.Count(comment => comment.IsSelected) ?? 0;
    internal bool IsReviewCommentsPaneVisible => _commentListHost?.Visibility == Visibility.Visible;
    internal string ReviewCommentPaneSummary => LastCommentPanePlan?.DeckSummaryLabel ?? string.Empty;
    internal IReadOnlyList<string> ReviewCommentPaneFilterStates =>
        LastCommentPanePlan?.Filters.Select(filter =>
            $"{filter.Kind}|{filter.Label}|{filter.Count}|{filter.IsSelected}|{filter.HasMatches}").ToArray() ?? [];
    internal IReadOnlyList<string> ReviewCommentPaneRenderedMentionLines =>
        EnumerateCommentPaneText(_commentListPanel)
            .Where(text => text.StartsWith("Mentions:", StringComparison.Ordinal))
            .ToArray();
    internal IReadOnlyList<string> ReviewCommentPaneRenderedMentionActions =>
        EnumerateCommentPaneButtons(_commentListPanel)
            .Where(button => button.Tag is string tag &&
                tag.StartsWith("comment-mention:", StringComparison.Ordinal))
            .Select(button => $"{button.Tag}:{button.Content}:{button.IsEnabled}")
            .ToArray();
    internal bool InvokeReviewCommentPaneMentionActionForTests(string tag, string? candidateLabel = null)
    {
        var button = EnumerateCommentPaneButtons(_commentListPanel)
            .FirstOrDefault(candidate => string.Equals(candidate.Tag as string, tag, StringComparison.Ordinal));
        if (button is null)
            return false;

        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        var item = button.ContextMenu?.Items.OfType<MenuItem>()
            .FirstOrDefault(candidate => candidateLabel is null ||
                string.Equals(candidate.Header as string, candidateLabel, StringComparison.Ordinal));
        if (item is null)
            return candidateLabel is null;

        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        return true;
    }
    internal bool IsReadingOrderPaneVisible => _readingOrderPaneHost?.Visibility == Visibility.Visible;
    internal int ReadingOrderPaneItemCount => LastReadingOrderPlan?.Items.Count ?? 0;
    internal string ReadingOrderPaneHeading => _readingOrderPaneHeading?.Text ?? string.Empty;
    internal string ReadingOrderPaneMessage => _readingOrderPaneMessage?.Text ?? string.Empty;
    internal bool IsReadingOrderMoveEarlierEnabled => _readingOrderMoveEarlierButton?.IsEnabled == true;
    internal bool IsReadingOrderMoveLaterEnabled => _readingOrderMoveLaterButton?.IsEnabled == true;
    internal bool IsProofingPaneVisible => _proofingPaneHost?.Visibility == Visibility.Visible;
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
    internal bool IsMediaCaptionPaneVisible => _mediaCaptionPaneHost?.Visibility == Visibility.Visible;
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

    // ── Wave 16B: Animation pane (right-side collapsible panel) ──────────────────
    // 16B SEAM START — do not restructure this region (16A/16C may conflict nearby).
    private AnimationPane? _animPane;
    private Border         _animPaneHost = null!;  // collapsible right-side dock (~240px)
    private readonly PresentationPaneAccessibilityAdapter _paneAccessibility = new();

    /// <summary>
    /// Test-seam: exposes the animation pane host border so tests can inspect visibility
    /// without launching the actual UI.  Internal; only visible to FreeP.App.Host.Tests.
    /// </summary>
    internal Border? AnimPaneHostForTest => _animPaneHost;
    internal IReadOnlyList<PresentationPaneAccessibilitySnapshotEntry> PaneAccessibilitySnapshotForTests =>
        _paneAccessibility.BuildSnapshot();
    internal string PaneAccessibilitySnapshotSerializationForTests =>
        _paneAccessibility.SerializeSnapshot();
    internal TextBox NotesPaneForAccessibilityTests => _notesBox;
    internal Border CommentsPaneForAccessibilityTests => _commentListHost;
    internal IReadOnlyList<FrameworkElement> CommentsPaneItemsForAccessibilityTests =>
        _commentListPanel is null
            ? Array.Empty<FrameworkElement>()
            : _commentListPanel.Children
                .OfType<FrameworkElement>()
                .Where(item => AutomationProperties.GetAutomationId(item)
                    .StartsWith("FreePCommentsPaneItem", StringComparison.Ordinal))
                .ToArray();
    internal SelectionPane SelectionPaneForAccessibilityTests => _selectionPane;
    internal IReadOnlyList<FrameworkElement> SelectionPaneItemsForAccessibilityTests =>
        _selectionPane?.AccessibilityItemsForTests ?? Array.Empty<FrameworkElement>();
    internal AnimationPane? AnimationPaneForAccessibilityTests => _animPane;
    internal IReadOnlyList<FrameworkElement> AnimationPaneItemsForAccessibilityTests =>
        _animPane?.AccessibilityItemsForTests ?? Array.Empty<FrameworkElement>();
    internal IReadOnlyList<FrameworkElement> SlidePaneItemsForAccessibilityTests =>
        (SlidePaneHost.Child as SlidePane)?.AccessibilityItemsForTests
        ?? Array.Empty<FrameworkElement>();
    // 16B SEAM END

    // ── Constructors ──────────────────────────────────────────────────────────────

    public MainWindow() : this(new FreePOptions()) { }

    public MainWindow(
        FreePOptions options,
        ApplicationOptionsStore<FreePOptions>? optionsStore = null,
        IUserMessageService? messageService = null,
        WpfNativePrintCapability? nativePrintCapability = null)
    {
        _options = options ?? new FreePOptions();
        _messageService = messageService;
        _optionsStore = optionsStore ?? ApplicationOptionsStore<FreePOptions>.ForPath(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FreeP", "settings.transient.json"));

        Title = "FreeP";
        Width = 1280;
        Height = 760;
        WindowState = WindowState.Maximized;
        Background = ResolveTokenBrush("FreePSheetSurfaceBrush")
            ?? new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        var chromeOptions = BuildChromeOptions();
        ShellChrome.ConfigureWindow(this, chromeOptions);

        // Initialise the editing session (and command bus inside it).
        RebuildEditor();
        _reviewWorkflowSession = new(
            () => Editor,
            new PresentationReviewWorkflowSessionCallbacks(
                MarkDirty: () => _file.MarkDirty(),
                RefreshCanvas: RefreshCanvas,
                RefreshNotesPane: RefreshNotesPane,
                RefreshAccessibilitySummaryPlan: RefreshAccessibilitySummaryPlan,
                RenderCommentPane: RenderCommentPane,
                RenderAltTextPaneIfVisible: RenderAltTextPaneIfVisible,
                RenderProofingPaneIfVisible: RenderProofingPaneIfVisible,
                UpdateAfterCommentMutation: UpdateTitle,
                UpdateAfterCommentNavigation: UpdateSlideCount,
                UpdateAfterProofingCorrection: UpdateTitle));

        // File commands.
        _file = new FileCommands(
            this,
            () => _presentation,
            LoadModel,
            UpdateTitle,
            _options,
            messageService: _messageService,
            getImageExportRange: BuildCurrentSlideImageExportRange,
            getPrintCurrentSlideNumber: () => Editor.CurrentSlideIndex + 1,
            nativePrintCapability: nativePrintCapability);

        // Title bar.
        var titleBar = ShellChrome.BuildTitleBar(this, chromeOptions);
        _titleBar = titleBar.Root;
        _titleBinder = new SisterWpfWindowTitleBinder(this, titleBar.TitleText);
        AddQuickAccessButtons(titleBar.QatHost);

        // Ribbon. Wave 4C passes the slideshow launch Actions into the command registry;
        // StartSlideShow (Wave 4B) opens the fullscreen SlideShowWindow.
        var stateStore = new RibbonStateStore();
        // Wave 10A: pass a lazy canvas getter so the ribbon format commands can route to the
        // active RichTextBox editor when it is open.  SlideCanvas is created inside BuildBody
        // (called below) and stored in the SlideCanvas field; the lambda captures the field
        // reference via `this`, so it always resolves at call-time after body construction.
        var commands = FreePRibbonCommands.Build(
            stateStore,
            Editor,
            onStartFromStart:   () => StartSlideShow(true),
            onStartFromCurrent: () => StartSlideShow(false),
            onRehearseTimings:  () => StartSlideShowWithTiming(FreeP.App.Compositor.SlideShowTimingIntent.RehearseTimings),
            onRecordTimings:    () => StartSlideShowWithTiming(FreeP.App.Compositor.SlideShowTimingIntent.RecordTimings),
            onEditChartData:    () => OpenChartDataDialog(),
            onEditChartOptions: () => OpenChartDisplayOptionsDialog(),
            onEditChartAxisOptions: () => OpenChartAxisOptionsDialog(),
            onEditChartSeriesOptions: () => OpenChartSeriesOptionsDialog(),
            onEditChartPointOptions: () => OpenChartPointOptionsDialog(),
            onEditChartLayoutOptions: () => OpenChartLayoutOptionsDialog(),
            onEditChartDataTableOptions: () => OpenChartDataTableOptionsDialog(),
            onEditChartBubbleOptions: () => OpenChartBubbleOptionsDialog(),
            onEditChartPieOptions: () => OpenChartPieOptionsDialog(),
            onEditChartPlotStyleOptions: () => OpenChartPlotStyleOptionsDialog(),
            onEditChart3DViewOptions: () => OpenChart3DViewOptionsDialog(),
            onEditChartTextOptions: () => OpenChartTextOptionsDialog(),
            onEditChartAreaOptions: () => OpenChartAreaOptionsDialog(),
            onEditChartProtectionOptions: () => OpenChartProtectionOptionsDialog(),
            onEditRotationOptions: () => OpenRotationOptionsDialog(),
            onInsertEmbeddedObject: () => InsertEmbeddedObjectFromFile(),
            tryOpenInlineEmbeddedObject: () => SlideCanvas.TextEditor?.TryActivateInlineOleObject() == true,
            getSlideCanvas:     () => SlideCanvas,
            onEditPoints:       () => SlideCanvas.SetEditPointsMode(!SlideCanvas.EditPointsEnabled),
            // Wave 10B: open custom slide-size dialog from Design tab ribbon button.
            onCustomSlideSize:  () => OpenSlideSizeDialog(),
            onLayoutPicker:     () => OpenLayoutPicker(),
            // Wave 10B: OS-clipboard service for ribbon Copy/Cut/Paste buttons.
            osClipboard:        _osClipboard,
            // Wave 11A: Insert Hyperlink dialog.
            onInsertLink:       () => OpenHyperlinkDialog(),
            onInsertSlideZoom:  () => OpenSlideZoomDialog(),
            onInsertSectionZoom: () => OpenSectionZoomDialog(),
            onInsertSummaryZoom: () => OpenSummaryZoomDialog(),
            // Wave 12B: Find & Replace dialogs.
            onFind:             () => OpenFindDialog(),
            onFindReplace:      () => OpenFindReplaceDialog(),
            onReviewCommentsPane: () => ShowReviewCommentsPane(),
            onReviewAccessibility: () => ShowAccessibilityCheckerPane(),
            onReviewAltText: () => ShowAltTextPane(),
            onReviewReadingOrder: () => ShowReadingOrderPane(),
            onSelectionPane: () => ShowSelectionPane(),
            onReviewProofing: () => ShowProofingPane(),
            onAddComment: () => AddComment("New comment"),
            onEditComment: () => EditSelectedComment(GetSelectedCommentText()),
            onReplyComment: () => ReplyToSelectedComment("New reply"),
            onDeleteComment: () => DeleteSelectedComment(),
            onPreviousComment: () => NavigateReviewComment(PresentationReviewWorkflowIntentKind.PreviousComment),
            onNextComment: () => NavigateReviewComment(PresentationReviewWorkflowIntentKind.NextComment),
            onResolveComment: () => ResolveSelectedComment(),
            onReopenComment: () => ReopenSelectedComment(),
            // Wave 16B: Animation pane toggle.
            onAnimPane:         () => ToggleAnimationPane(),
            onTransitionSound:  () => PickTransitionSound(),
            getEditPointsEnabled: () => SlideCanvas?.EditPointsEnabled ?? true,
            setEditPointsEnabled: enabled => SlideCanvas?.SetEditPointsMode(enabled),
            onTablePicker:      () => OpenTablePicker(),
            onHeaderFooter:     focus => OpenHeaderFooterDialog(focus),
            getViewShowState:   () => _viewShowState,
            applyViewShowState: ApplyPresentationViewShowState,
            getViewZoomState:   () => _viewZoomState,
            applyViewZoomState: ApplyPresentationViewZoomState,
            onCustomShows:      () => OpenCustomShowDialog(),
            onSmartArtColorPreset: preset => ApplySmartArtColorPreset(preset),
            onSmartArtLayoutPreset: preset => ApplySmartArtLayoutPreset(preset),
            onSmartArtQuickStylePreset: preset => ApplySmartArtQuickStylePreset(preset));
        var ribbon = BuildRibbon(FreePRibbon.Build(), commands, stateStore);

        // Body: slide pane + stage.
        var body = BuildBody();

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
            if (!_file.ConfirmCloseAllowed())
                e.Cancel = true;
        };

        // Backstage.
        _backstage = new BackstageView(() => _presentation, _file, new BackstageActions(
            New: () => _file.New(),
            Open: () => _file.Open(),
            OpenPath: path => _file.OpenPath(path),
            Save: () => _file.Save(),
            SaveAs: () => _file.SaveAs(),
            ExportPdf: () => _file.ExportPdf(),
            ExportNotesPagePdf: () => _file.ExportNotesPagePdf(),
            ExportImages: () => _file.ExportImages(),
            Print: request => _file.Print(request),
            PlanPrint: () => RefreshPrintBackstagePlan(),
            ExportVideo: () => _ = _file.ExportVideoAsync(),
            CanExportVideo: () => _file.CanExportVideo,
            CurrentOptions: () => _options,
            OnClosed: () => { },
            DataFolder: ResolveDataFolderLabel));

        var frame = SisterAppWindowFrameBuilder.Build(new SisterAppWindowFrameSpec(_titleBar, root, _backstage));
        Content = frame.Root;

        UpdateTitle();
        RefreshCanvas();
        RefreshNotesPane();
        RefreshCommentPane();
        RefreshReviewWorkflowPlans();
        UpdateSlideCount();
    }

    // ── Editor construction ───────────────────────────────────────────────────────

    private void RebuildEditor()
    {
        var bus = new PresentationCommandBus(_presentation);
        Editor  = new EditingSession(_presentation, bus);
        _selectionPane?.SetEditor(Editor);

        Editor.Changed           += () =>
        {
            _file.MarkDirty();
            RefreshCanvas();
            RefreshNotesPane();
            UpdateSlideCount();
            UpdateTitle();
            RefreshReviewWorkflowPlans();
            if (IsSmartArtTextPaneVisible)
                ShowSmartArtTextPane();
            _selectionPane?.Refresh();
            RefreshPaneAccessibilityMetadata();
        };
        Editor.CurrentSlideChanged += (_, _) => { _reviewWorkflowSession.SelectedCommentIndex = null; _selectedMediaCaptionTrackIndex = null; RefreshCanvas(); RefreshNotesPane(); RefreshCommentPane(); RefreshReviewWorkflowPlans(); RefreshVisibleMediaCaptionPaneFromFields(); _selectionPane?.Refresh(); RefreshPaneAccessibilityMetadata(); };
        Editor.SelectionChanged += (_, _) =>
        {
            RefreshAltTextRequestPlan();
            RefreshReadingOrderPlan();
            if (IsAltTextPaneVisible)
                ShowAltTextPane();
            if (IsSmartArtTextPaneVisible)
                ShowSmartArtTextPane();
            RefreshVisibleMediaCaptionPaneFromFields();
            _selectionPane?.Refresh();
            RefreshPaneAccessibilityMetadata();
        };

        // Re-attach editing layer whenever the editor is rebuilt (file open/new).
        // Guard: SlideCanvas is null during initial construction; BuildBody calls
        // AttachCanvasEditing() itself after creating the canvas.
        if (SlideCanvas is not null)
            AttachCanvasEditing();
    }

    // ── 3C SEAM: canvas editing attachment ───────────────────────────────────────

    /// <summary>
    /// Wires the gesture handler and in-canvas text editor to the current Editor.
    /// Called once from BuildBody (initial) and then on every file new/open from RebuildEditor.
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
        SlideCanvas.AttachEditing(Editor, _textOverlay, TryOpenOleInPlace);
        SlideCanvas.ApplyViewShowState(_viewShowState);
    }

    private bool TryOpenOleInPlace(SlideShape shape)
    {
        if (shape.Kind != SlideShapeKind.Ole
            || shape.OleObject is null
            || _oleOverlay is null
            || Math.Abs(shape.RotationDeg) > 0.01
            || shape.FlipH
            || shape.FlipV)
            return false;

        CloseActiveOleHost();
        var transform = SlideCanvas.CurrentTransform;
        var margin = SlideCanvas.Margin;
        var topLeft = transform.SlideToScreen(
            SlideTransform.EmuToDip(shape.OffsetXEmu),
            SlideTransform.EmuToDip(shape.OffsetYEmu));
        var bounds = new Rect(
            margin.Left + topLeft.X,
            margin.Top + topLeft.Y,
            transform.ScaleDipToScreen(SlideTransform.EmuToDip(shape.ExtentCxEmu)),
            transform.ScaleDipToScreen(SlideTransform.EmuToDip(shape.ExtentCyEmu)));

        return WpfOleInPlaceHost.TryShow(
            _oleOverlay,
            shape.OleObject,
            bounds,
            out _activeOleHost);
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
        var slide = Editor.CurrentSlide;
        if (slide is null || _presentation is null)
            return;

        var screenPoint = e.GetPosition(SlideCanvas);
        var slidePoint = SlideCanvas.CurrentTransform.ScreenToSlide(screenPoint.X, screenPoint.Y);
        var hitId = FreeP.App.Compositor.ShapeHitTester.HitTest(slide, _presentation, slidePoint.X, slidePoint.Y);
        var shape = hitId.HasValue
            ? ShapeTreeLookup.Find(slide, hitId.Value)
            : null;
        if (shape?.Kind != SlideShapeKind.Table || shape.Table is null)
            return;

        var cellHit = TableCellHitTester.HitTest(shape, slidePoint.X, slidePoint.Y);
        if (!cellHit.HasValue)
            return;

        Editor.SetActiveTableCell(cellHit.Value.Row, cellHit.Value.Col);
        var menu = BuildTableContextMenu(shape);
        menu.PlacementTarget = SlideCanvas;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
        e.Handled = true;
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
        var shape = Editor.CurrentSlide is { } slide
            ? ShapeTreeLookup.Find(slide, shapeId)
            : null;
        return shape?.Kind == SlideShapeKind.Table && shape.Table is not null
            ? BuildTableContextMenu(shape)
            : null;
    }

    // ── File load ─────────────────────────────────────────────────────────────────

    private void ApplyPresentationViewShowState(PresentationViewShowState state)
    {
        _viewShowState = state;
        if (SlideCanvas is not null)
            SlideCanvas.ApplyViewShowState(state);
    }

    private void ApplyPresentationViewZoomState(PresentationViewZoomState state)
    {
        _viewZoomState = state;
        if (SlideCanvas is not null)
            SlideCanvas.ApplyViewZoomState(state);
    }

    private void LoadModel(Presentation presentation)
    {
        _findReplaceDialog?.Close();
        _presentation = presentation;
        RebuildEditor(); // also calls AttachCanvasEditing()
        // 3B: re-bind slide pane to the new Editor on file open/new.
        SlidePaneHost.Child = new SlidePane(Editor);
        HideLayoutPicker();
        HideTablePicker();
        RefreshCanvas();
        UpdateSlideCount();
        RefreshNotesPane();
        RefreshCommentPane();
        RefreshReviewWorkflowPlans();
        // 16B: rebuild animation pane for new editor (only if the pane is currently shown).
        RebuildAnimationPaneIfVisible();
    }

    // ── Body layout ───────────────────────────────────────────────────────────────

    private UIElement BuildBody()
    {
        // LEFT pane host — Wave 3B fills this container with the thumbnail/sorter pane.
        // <!-- 3B SEAM: set SlidePaneHost.Child = your thumbnail panel here. -->
        SlidePaneHost = new Border
        {
            Width      = FreePShellVisualMetrics.SlidePaneWidth,
            Background = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
        };
        // 3B SEAM: attach the slide-thumbnail pane.
        SlidePaneHost.Child = new SlidePane(Editor);

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
            Background = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)),
            ClipToBounds = true,
            Child      = adornerDecorator
        };

        // 3C SEAM: attach gesture handler and text editor.
        // Called here (after canvas is created) and again in RebuildEditor/LoadModel.
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
            Background          = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xF0)),
            BorderThickness     = new Thickness(0, 1, 0, 0),
            BorderBrush         = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
            VerticalAlignment   = VerticalAlignment.Stretch,
        };
        _notesBox.TextChanged += (_, _) =>
        {
            if (_notesRefreshing) return;
            Editor.SetCurrentSlideNotesText(_notesBox.Text);
            LastNotesPagePreviewPlan = PresentationNotesPagePreviewPlanner.Build(
                _presentation,
                Editor.CurrentSlideIndex);
        };

        // Wave 11B: comment list pane — a collapsible strip above the notes pane.
        // It is hidden when the current slide has no comments.
        _commentListPanel = new StackPanel { Orientation = Orientation.Vertical };
        _commentListHost = new Border
        {
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Background      = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xE8)),
            MaxHeight       = 100,
            Child           = new ScrollViewer
            {
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content                       = _commentListPanel,
            },
            Visibility      = Visibility.Collapsed,
        };

        _layoutPickerPanel = new StackPanel { Orientation = Orientation.Vertical };
        _layoutPickerHost = new Border
        {
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Background      = Brushes.White,
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
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Background      = Brushes.White,
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
                        Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
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
            Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)),
        };
        // AnimationPane itself is created lazily on first show (ToggleAnimationPane).
        // END 16B SEAM

        _readingOrderPaneHost = BuildReadingOrderPaneHost();
        _selectionPane = new SelectionPane(Editor, RefreshPaneAccessibilityMetadata);
        _selectionPane.Refresh();
        _proofingPaneHost = BuildProofingPaneHost();
        _mediaCaptionPaneHost = BuildMediaCaptionPaneHost();
        _smartArtTextPaneHost = BuildSmartArtTextPaneHost();

        var splitter = new Grid();
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
            || _selectionPane is null || _animPaneHost is null)
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
        var animationPlan = _animPane?.CurrentTimelinePlanForTest;

        _paneAccessibility.ApplyPane(SlidePaneHost, PresentationPaneAccessibilityPlanner.SlidePaneId, true,
            _presentation.Slides.Count, Editor.CurrentSlideIndex);
        _paneAccessibility.ApplyPane(_notesBox, PresentationPaneAccessibilityPlanner.NotesPaneId, true, 1);
        _paneAccessibility.ApplyPane(_commentListHost, PresentationPaneAccessibilityPlanner.CommentsPaneId,
            _commentListHost.Visibility == Visibility.Visible,
            commentPlan?.Comments.Count ?? 0, commentPlan?.SelectedCommentIndex ?? -1);
        _paneAccessibility.ApplyPane(_accessibilityCheckerPaneHost, PresentationPaneAccessibilityPlanner.AccessibilityPaneId,
            _accessibilityCheckerPaneHost.Visibility == Visibility.Visible,
            accessibilityPlan?.Rows.Count ?? _accessibilityCheckerRowsPanel?.Children.Count ?? 0,
            accessibilityPlan?.SelectedRowIndex ?? -1);
        _paneAccessibility.ApplyPane(_altTextPaneHost, PresentationPaneAccessibilityPlanner.AltTextPaneId,
            _altTextPaneHost.Visibility == Visibility.Visible, 3);
        _paneAccessibility.ApplyPane(_readingOrderPaneHost, PresentationPaneAccessibilityPlanner.ReadingOrderPaneId,
            _readingOrderPaneHost.Visibility == Visibility.Visible,
            readingOrderPlan?.Items.Count ?? _readingOrderPaneItemsPanel?.Children.Count ?? 0,
            readingOrderPlan?.SelectedItemIndex ?? -1);
        _paneAccessibility.ApplyPane(_proofingPaneHost, PresentationPaneAccessibilityPlanner.ProofingPaneId,
            _proofingPaneHost.Visibility == Visibility.Visible,
            proofingPlan?.Rows.Count ?? _proofingPaneRowsPanel?.Children.Count ?? 0,
            proofingPlan?.SelectedRowIndex ?? -1);
        _paneAccessibility.ApplyPane(_mediaCaptionPaneHost, PresentationPaneAccessibilityPlanner.MediaCaptionPaneId,
            _mediaCaptionPaneHost.Visibility == Visibility.Visible,
            captionPlan?.Tracks.Count ?? _mediaCaptionTrackBox?.Items.Count ?? 0,
            captionPlan?.SelectedTrackIndex ?? _mediaCaptionTrackBox?.SelectedIndex ?? -1);
        _paneAccessibility.ApplyPane(_smartArtTextPaneHost, PresentationPaneAccessibilityPlanner.SmartArtTextPaneId,
            _smartArtTextPaneHost.Visibility == Visibility.Visible, smartArtItemCount,
            _smartArtTextPaneRowsPanel?.Children.IndexOf(
                _smartArtTextPaneRowsPanel.Children.OfType<TextBox>().FirstOrDefault(box =>
                    box.Tag is SmartArtNodeOutlineItem item &&
                    StringComparer.Ordinal.Equals(item.ModelId, _selectedSmartArtTextPaneModelId))) ?? -1);
        _paneAccessibility.ApplyPane(_selectionPane, PresentationPaneAccessibilityPlanner.SelectionPaneId,
            _selectionPane.Visibility == Visibility.Visible, selectionPlan.Items.Count,
            Array.FindIndex(selectionPlan.Items.ToArray(), item => item.IsSelected));
        _paneAccessibility.ApplyPane(_animPaneHost, PresentationPaneAccessibilityPlanner.AnimationPaneId,
            _animPaneHost.Visibility == Visibility.Visible,
            animationPlan?.Items.Count ?? 0, animationPlan?.SelectedIndex ?? -1);
    }

    private Border BuildMediaCaptionPaneHost()
    {
        _mediaCaptionPaneHeading = new TextBlock
        {
            Text = "Media Captions",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
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
            if (_mediaCaptionPaneRefreshing)
                return;
            _selectedMediaCaptionTrackIndex = _mediaCaptionTrackBox.SelectedItem is ComboBoxItem { Tag: int trackIndex }
                ? trackIndex
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

        var buttons = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12, 8, 12, 12),
        };
        buttons.Children.Add(_mediaCaptionCreateButton);
        buttons.Children.Add(_mediaCaptionReplaceButton);
        buttons.Children.Add(_mediaCaptionDeleteButton);
        buttons.Children.Add(_mediaCaptionCloseButton);

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
        panel.Children.Add(buttons);

        return new Border
        {
            Width = 320,
            Visibility = Visibility.Collapsed,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = panel,
        };
    }

    private static TextBlock BuildMediaCaptionPaneLabel()
        => new()
        {
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
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
            Padding = new Thickness(6, 4, 6, 4),
            VerticalScrollBarVisibility = singleLine ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto,
        };

    private static Button BuildMediaCaptionPaneButton()
        => new()
        {
            MinWidth = 72,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 6),
        };

    private Border BuildSmartArtTextPaneHost()
    {
        _smartArtTextPaneHeading = new TextBlock
        {
            Text = "SmartArt Text Pane",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
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
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 8, 0),
        };
        _smartArtTextPanePictureButton = new Button
        {
            Content = "Replace picture",
            MinWidth = 120,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 8, 0),
        };
        _smartArtTextPaneClearPictureButton = new Button
        {
            Content = "Remove picture",
            MinWidth = 120,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 8, 0),
        };
        _smartArtTextPaneApplyButton = new Button
        {
            Content = "Apply",
            MinWidth = 72,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 8, 0),
        };
        _smartArtTextPaneCloseButton = new Button
        {
            Content = "Close",
            MinWidth = 72,
            Padding = new Thickness(10, 4, 10, 4),
        };
        _smartArtTextPaneAssistantButton.Click += (_, _) => ToggleSmartArtTextPaneAssistant();
        _smartArtTextPanePictureButton.Click += (_, _) => ReplaceSmartArtTextPanePictureFromFile();
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
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
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

    private static string FormatAvailability(bool isAvailable)
        => isAvailable ? "available" : "unavailable";

    private Border BuildAltTextPaneHost()
    {
        _altTextPaneHeading = new TextBlock
        {
            Text = "Alt Text",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
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
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = panel,
        };
        _altTextCloseButton.Content = "Close";
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
            Text = "Accessibility",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
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
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = panel,
        };
    }

    private Border BuildReadingOrderPaneHost()
    {
        _readingOrderPaneHeading = new TextBlock
        {
            Text = "Reading Order",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
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
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 8, 0),
        };
        _readingOrderMoveLaterButton = new Button
        {
            MinWidth = 84,
            Padding = new Thickness(10, 4, 10, 4),
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
            Width = 320,
            Visibility = Visibility.Collapsed,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = panel,
        };
    }

    // ── Canvas refresh ────────────────────────────────────────────────────────────

    private Border BuildProofingPaneHost()
    {
        _proofingPaneHeading = new TextBlock
        {
            Text = "Spelling",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
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
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
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
            LastNotesPagePreviewPlan = PresentationNotesPagePreviewPlanner.Build(
                _presentation,
                Editor.CurrentSlideIndex);
            var notes = Editor.CurrentSlideNotes;
            if (notes is null)
            {
                _notesBox.Text = string.Empty;
            }
            else
            {
                _notesBox.Text = string.Join(
                    Environment.NewLine,
                    notes.Paragraphs.Select(p => string.Concat(p.Runs.Select(r => r.Text))));
            }
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
        if (comments.Count > 0)
        {
            // The overlay is stretched over the same area as SlideCanvas.
            // SlideCanvas.Margin = 40 on all sides; the slide itself is rendered inside that margin.
            // We approximate the slide area as the canvas actual size minus the 40px margins.
            // At runtime the canvas layout has been measured; we use actual dimensions.
            // Since RefreshCommentPane is called after layout pass via events, ActualWidth is valid
            // except on the very first call (before Loaded).  We add a safe fallback of 0.
            var presW = _presentation.SlideSizeCxEmu;
            var presH = _presentation.SlideSizeCyEmu;
            if (presW <= 0) presW = 12192000;
            if (presH <= 0) presH = 6858000;

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
        if (comments.Count > 0)
        {
            foreach (var (cm, itemIndex) in comments.Select((comment, index) => (comment, index)))
            {
                // Header: initials badge + author name + timestamp
                var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 4, 6, 0) };
                var badge = new Border
                {
                    Background      = new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A)),
                    CornerRadius    = new CornerRadius(3),
                    Padding         = new Thickness(4, 1, 4, 1),
                    Margin          = new Thickness(0, 0, 6, 0),
                    Child           = new TextBlock
                    {
                        Text       = cm.InitialsBadgeText,
                        FontSize   = 10,
                        Foreground = System.Windows.Media.Brushes.White,
                    }
                };
                var authorText = new TextBlock
                {
                    Text       = cm.AuthorDisplayName,
                    FontSize   = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                headerPanel.Children.Add(badge);
                headerPanel.Children.Add(authorText);
                headerPanel.Children.Add(new TextBlock
                {
                    Text       = cm.ThreadStatusLabel,
                    FontSize   = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                    Margin     = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                });

                // Comment body text
                var bodyText = new TextBlock
                {
                    Text         = cm.TextPreview,
                    FontSize     = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground   = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                    Margin       = new Thickness(16, 2, 6, 6),
                };

                var card = new StackPanel();
                card.Children.Add(headerPanel);
                card.Children.Add(bodyText);
                AddMentionDetail(card, cm.MentionDetailSummary, new Thickness(16, 0, 6, 6));
                AddEditCommentInput(card, cm);
                AddReplyRows(card, cm);
                AddReplyInput(card, cm);

                var cardHost = new Border
                {
                    Background      = new SolidColorBrush(cm.IsSelected ? Color.FromRgb(0xF4, 0xEC, 0xE8) : Color.FromRgb(0xFA, 0xFA, 0xFA)),
                    BorderBrush     = new SolidColorBrush(cm.IsSelected ? Color.FromRgb(0xB7, 0x47, 0x2A) : Color.FromRgb(0xE0, 0xE0, 0xE0)),
                    BorderThickness = new Thickness(cm.IsSelected ? 2 : 1),
                    CornerRadius    = new CornerRadius(4),
                    Margin          = new Thickness(0, 0, 0, 6),
                    Cursor          = Cursors.Hand,
                    Child           = card,
                };
                PresentationPaneAccessibilityAdapter.ApplyItem(
                    cardHost,
                    PresentationPaneAccessibilityPlanner.CommentsPaneId,
                    itemIndex,
                    cm.TextPreview,
                    cm.IsSelected ? "Selected" : "Not selected");
                cardHost.MouseLeftButtonDown += (_, _) => SelectReviewComment(cm.CommentIndex);
                _commentListPanel.Children.Add(cardHost);
            }
        }

        _commentListHost.Visibility = comments.Count > 0 || _reviewCommentsPaneRequested
            ? Visibility.Visible
            : Visibility.Collapsed;
        PresentationPaneAccessibilityAdapter.ApplyPaneMetadata(
            _commentListHost,
            PresentationPaneAccessibilityPlanner.CommentsPaneId,
            _commentListHost.Visibility == Visibility.Visible,
            comments.Count,
            plan.SelectedCommentIndex);
        RefreshPaneAccessibilityMetadata();
    }

    private void AddCommentPaneSummary(Panel host, PresentationCommentPanePlan plan)
    {
        var summaryRow = new DockPanel();
        var close = new Button
        {
            Content = "Close",
            MinWidth = 64,
            Margin = new Thickness(6, 0, 0, 6),
            Tag = "comments-pane-close",
        };
        close.Click += (_, _) => HideReviewCommentsPane();
        DockPanel.SetDock(close, Dock.Right);
        summaryRow.Children.Add(close);
        summaryRow.Children.Add(new TextBlock
        {
            Text = $"{plan.CurrentSlideSummaryLabel} | {plan.DeckSummaryLabel}",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
            Margin = new Thickness(0, 0, 0, 6),
        });
        host.Children.Add(summaryRow);
        host.Children.Add(new TextBlock
        {
            Text = string.Join(" | ", plan.Filters.Select(filter => filter.Summary)),
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
            Margin = new Thickness(0, 0, 0, 6),
        });
    }

    private void AddCommentInput(Panel host)
    {
        var input = new TextBox
        {
            MinWidth = 220,
            Margin = new Thickness(0, 0, 6, 0)
        };
        var button = new Button
        {
            Content = "New Comment",
            MinWidth = 96
        };
        button.Click += (_, _) => AddComment(input.Text);

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };
        row.Children.Add(input);
        row.Children.Add(button);
        host.Children.Add(row);
    }

    private void AddEditCommentInput(StackPanel card, PresentationCommentDescriptor cm)
    {
        if (!cm.IsSelected || !cm.CanEdit)
            return;

        var editText = GetCommentText(cm.CommentIndex) ?? cm.TextPreview;
        var input = new TextBox
        {
            Text = editText,
            CaretIndex = editText.Length,
            MinWidth = 220,
            Margin = new Thickness(16, 0, 6, 6)
        };
        var mentionButton = BuildCommentMentionButton(
            "comment-mention:edit",
            () => input.Text,
            () => ResolveCommentInputCaret(input.Text, input.CaretIndex),
            updatedText => EditSelectedComment(updatedText));
        var button = new Button
        {
            Content = "Save",
            MinWidth = 72,
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
                Text = $"{reply.AuthorDisplayName}: {reply.TextPreview}",
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                Margin = new Thickness(26, 0, 6, 4),
            };
            card.Children.Add(row);
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
            MinWidth = 180,
            Margin = new Thickness(0, 0, 6, 0),
        };
        var mentionButton = BuildCommentMentionButton(
            "comment-mention:reply",
            () => input.Text,
            () => ResolveCommentInputCaret(input.Text, input.CaretIndex),
            updatedText => ReplyToSelectedComment(updatedText));
        var button = new System.Windows.Controls.Button
        {
            Content = "Reply",
            MinWidth = 58,
        };
        button.Click += (_, _) => ReplyToSelectedComment(input.Text);
        row.Children.Add(input);
        row.Children.Add(mentionButton);
        row.Children.Add(button);
        card.Children.Add(row);
    }

    private static void AddMentionDetail(StackPanel card, string mentionDetailSummary, Thickness margin)
    {
        if (string.Equals(mentionDetailSummary, "No mentions", StringComparison.Ordinal))
            return;

        card.Children.Add(new TextBlock
        {
            Text = mentionDetailSummary,
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
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
            MinWidth = 72,
            Margin = new Thickness(0, 0, 6, 6),
            IsEnabled = mentionPicker.HasCandidates,
            Tag = tag,
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
                menu.IsOpen = true;
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
    /// Positions are derived from the comment's EMU coordinates mapped into the overlay bounds,
    /// accounting for SlideCanvas's 40 px margin on each side.
    /// </summary>
    private void DrawCommentDots(IReadOnlyList<PresentationCommentDescriptor> comments)
    {
        _commentOverlay.Children.Clear();
        if (comments.Count == 0) return;

        const double CanvasMargin = 40.0;
        double w = _commentOverlay.ActualWidth;
        double h = _commentOverlay.ActualHeight;
        if (w <= 0 || h <= 0) return;

        double slideW = w - 2 * CanvasMargin;
        double slideH = h - 2 * CanvasMargin;
        if (slideW <= 0 || slideH <= 0) return;

        long presW = _presentation.SlideSizeCxEmu > 0 ? _presentation.SlideSizeCxEmu : 12192000;
        long presH = _presentation.SlideSizeCyEmu > 0 ? _presentation.SlideSizeCyEmu : 6858000;

        // Scale so the slide fits within the available area (same as SlideCanvas renderer).
        double scaleX = slideW / presW;
        double scaleY = slideH / presH;
        double scale  = Math.Min(scaleX, scaleY);

        double rendW = presW * scale;
        double rendH = presH * scale;

        // Centre the rendered slide within the available area.
        double offX = CanvasMargin + (slideW - rendW) / 2.0;
        double offY = CanvasMargin + (slideH - rendH) / 2.0;

        foreach (var cm in comments)
        {
            double cx = offX + cm.Xemu * scale;
            double cy = offY + cm.Yemu * scale;

            // Speech-bubble: a small orange circle with a tooltip showing author+text.
            var dot = new Border
            {
                Width           = cm.IsSelected ? 18 : 14,
                Height          = cm.IsSelected ? 18 : 14,
                CornerRadius    = new CornerRadius(cm.IsSelected ? 9 : 7),
                Background      = new SolidColorBrush(cm.IsSelected ? Color.FromRgb(0x8F, 0x37, 0x21) : Color.FromRgb(0xB7, 0x47, 0x2A)),
                BorderBrush     = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(cm.IsSelected ? 2.0 : 1.5),
                ToolTip         = $"{cm.Author}: {cm.TextPreview}",
            };

            Canvas.SetLeft(dot, cx - (cm.IsSelected ? 9 : 7));
            Canvas.SetTop(dot,  cy - (cm.IsSelected ? 9 : 7));
            _commentOverlay.Children.Add(dot);
        }
    }

    internal PresentationVideoExportPlan RefreshVideoExportPlan(PresentationVideoExportRequest? request = null)
    {
        LastVideoExportPlan = _file.BuildVideoExportPlan(request);
        return LastVideoExportPlan;
    }

    internal PresentationVideoFramePackage RefreshVideoFramePackage(PresentationVideoExportRequest? request = null)
    {
        LastVideoFramePackage = _file.BuildVideoFramePackage(request);
        LastVideoExportPlan = _file.BuildVideoExportPlan(request);
        LastVideoExportHandoffPlan = _file.LastVideoExportHandoffPlan;
        return LastVideoFramePackage;
    }

    internal PresentationNotesPagePdfRenderPlan RefreshNotesPagePdfRenderPlan(PresentationSlideRangeRequest? range = null)
    {
        LastNotesPagePdfRenderPlan = _file.BuildNotesPagePdfRenderPlan(range);
        return LastNotesPagePdfRenderPlan;
    }

    internal PresentationPrintOutputPackage RefreshPrintOutputPackage(PresentationPrintRequest? request = null)
    {
        LastPrintOutputPackage = _file.BuildPrintOutputPackage(request);
        return LastPrintOutputPackage;
    }

    internal PresentationPrintBackstagePlan RefreshPrintBackstagePlan(PresentationPrintRequest? request = null)
    {
        LastPrintBackstagePlan = _file.BuildPrintBackstagePlan(request);
        return LastPrintBackstagePlan;
    }

    internal void RefreshReviewWorkflowPlans()
    {
        _reviewWorkflowSession.RefreshReviewWorkflowPlans();
        RefreshPaneAccessibilityMetadata();
    }

    internal PresentationCommentPanePlan ShowReviewCommentsPane()
    {
        _reviewCommentsPaneRequested = true;
        return _reviewWorkflowSession.ShowReviewCommentsPane();
    }

    internal void HideReviewCommentsPane()
    {
        _reviewCommentsPaneRequested = false;
        if (_commentListHost is not null)
            _commentListHost.Visibility = Visibility.Collapsed;
        RefreshPaneAccessibilityMetadata();
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
        _accessibilityCheckerPaneHost.Visibility = Visibility.Visible;
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
        _accessibilityCheckerPaneHost.Visibility = Visibility.Visible;
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
            OpenChartDisplayOptionsDialog();
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
        _accessibilityCheckerPaneHost.Visibility = Visibility.Visible;
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

    private UIElement BuildAccessibilityCheckerRowCard(PresentationAccessibilityCheckerRowPlan row)
    {
        var title = new TextBlock
        {
            Text = $"{row.SlideDisplay} - {row.Title}",
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        var metadata = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(row.ShapeName)
                ? $"{row.Severity} - {row.Category}"
                : $"{row.Severity} - {row.Category} - {row.ShapeName}",
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            TextWrapping = TextWrapping.Wrap,
        };
        var detail = new TextBlock
        {
            Text = row.Detail,
            Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
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
        if (row.IsSelected)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Selected issue",
                Foreground = new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A)),
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
        card.MouseLeftButtonUp += (_, _) => SelectAccessibilityCheckerRow(row.RowIndex);
        PresentationPaneAccessibilityAdapter.ApplyItem(
            card,
            PresentationPaneAccessibilityPlanner.AccessibilityPaneId,
            row.RowIndex,
            row.Title,
            row.IsSelected ? "Selected" : "Not selected");
        return card;
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
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        _accessibilityCheckerReviewDetailsPanel.Children.Add(new TextBlock
        {
            Text = display.Summary,
            Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 4),
        });
        _accessibilityCheckerReviewDetailsPanel.Children.Add(new TextBlock
        {
            Text = display.Guidance,
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        });

        foreach (var detail in display.Details)
        {
            _accessibilityCheckerTableStructureReviewRenderedLines.Add(
                $"{detail.Category}: {detail.Summary} {detail.Detail}");
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
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            TextWrapping = TextWrapping.Wrap,
        });

        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF7)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE2, 0xE2)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 6),
            Child = panel,
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
        _smartArtTextPaneHost.Visibility = Visibility.Visible;
        RefreshPaneAccessibilityMetadata();
        return outline;
    }

    internal void HideSmartArtTextPane()
    {
        if (_smartArtTextPaneHost is not null)
            _smartArtTextPaneHost.Visibility = Visibility.Collapsed;
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
                ? new SmartArtTextPaneOutlineRow(box.Text, item.Level, item.IsAssistant, item.ModelId)
                : new SmartArtTextPaneOutlineRow(box.Text, 0))
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

                CommitSmartArtTextPaneMutation(smartArt, smartArtShape);
                return true;
            });
        }

        if (LastSmartArtTextPaneApplyResult is { Applied: true })
        {
            _file.MarkDirty();
            RefreshCanvas();
            UpdateTitle();
        }

        RefreshSmartArtTextPane();
        return LastSmartArtTextPaneApplyResult!;
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

    private void ReplaceSmartArtTextPanePictureFromFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Replace SmartArt picture",
            Filter = "Picture files|*.png;*.jpg;*.jpeg;*.gif;*.svg;*.bmp|All files|*.*",
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            ApplySmartArtTextPanePicture(
                System.IO.File.ReadAllBytes(dialog.FileName),
                SlideObjectInsertionPlanner.InferPictureContentType(dialog.FileName));
        }
        catch (Exception ex)
        {
            _smartArtTextPaneMessage.Text = $"Could not replace SmartArt picture: {ex.Message}";
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
            _file.MarkDirty();
            RefreshCanvas();
            UpdateTitle();
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
            _file.MarkDirty();
            RefreshCanvas();
            UpdateTitle();
        }
        RefreshSmartArtTextPane();
    }

    internal SmartArtNodeEditResult? ToggleSmartArtTextPaneAssistantForTests(string? modelId = null)
    {
        if (modelId is not null)
            _selectedSmartArtTextPaneModelId = modelId;
        return ToggleSmartArtTextPaneAssistant();
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
            _file.MarkDirty();
            RefreshCanvas();
            UpdateTitle();
        }

        RefreshSmartArtTextPane();
        return LastSmartArtTextPaneEditResult;
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

            CommitSmartArtTextPaneMutation(smartArt, smartArtShape);
            return true;
        });

        if (result is { Applied: true })
        {
            _file.MarkDirty();
            RefreshCanvas();
            UpdateTitle();
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

            CommitSmartArtTextPaneMutation(smartArt, smartArtShape);
            return true;
        });

        if (result is { Applied: true })
        {
            _file.MarkDirty();
            RefreshCanvas();
            UpdateTitle();
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

            CommitSmartArtTextPaneMutation(smartArt, smartArtShape);
            return true;
        });

        if (LastSmartArtColorApplyResult is { Applied: true })
        {
            _file.MarkDirty();
            RefreshCanvas();
            UpdateTitle();
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
            Padding = new Thickness(6, 3, 6, 3),
            BorderBrush = selected
                ? new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A))
                : new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
            BorderThickness = new Thickness(selected ? 2 : 1),
            ToolTip = item.IsAssistant
                ? "Assistant row"
                : item.Level == 0
                    ? "Root row"
                    : $"Level {item.Level + 1} row",
        };
        box.GotKeyboardFocus += (_, _) => _selectedSmartArtTextPaneModelId = item.ModelId;
        box.KeyDown += (_, e) =>
        {
            if (_smartArtTextPaneRefreshing)
                return;

            if (!TryMapSmartArtTextPaneKey(e.Key, Keyboard.Modifiers, out var key, out var modifiers))
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
                CommitSmartArtTextPaneMutation(smartArt, smartArtShape);
                return true;
            });
        }

        if (LastSmartArtTextPaneEditResult is { Applied: true })
        {
            _file.MarkDirty();
            RefreshCanvas();
            UpdateTitle();
        }

        RefreshSmartArtTextPane();
        return LastSmartArtTextPaneEditResult;
    }

    private void CommitSmartArtTextPaneMutation(SmartArtShape smartArt, SlideShape smartArtShape)
    {
        LastSmartArtDataPartRewriteResult = SmartArtEditingPlanner.RewriteDataPart(smartArt);
        LastSmartArtDrawingCacheRegenerationResult = SmartArtEditingPlanner.RegenerateDrawingCache(
            smartArt,
            smartArtShape.OffsetXEmu,
            smartArtShape.OffsetYEmu,
            smartArtShape.ExtentCxEmu,
            smartArtShape.ExtentCyEmu,
            ResolveCurrentSlideTheme(),
            Editor.CurrentSlide?.ColorMapOverride);
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

    internal void ShowAltTextPane()
    {
        RefreshAltTextPlans(proposedDescription: null, proposedTitle: null, isDecorative: null);
        if (LastAltTextPanePlan is not null)
            RenderAltTextPane(LastAltTextPanePlan);
        _altTextPaneHost.Visibility = Visibility.Visible;
        RefreshPaneAccessibilityMetadata();
    }

    internal void HideAltTextPane()
    {
        if (_altTextPaneHost is not null)
            _altTextPaneHost.Visibility = Visibility.Collapsed;
        RefreshPaneAccessibilityMetadata();
    }

    internal PresentationMediaCaptionAuthoringPanePlan ShowMediaCaptionPane()
    {
        RefreshMediaCaptionAuthoringPlans(null, null, null, null);
        RenderMediaCaptionPane(LastMediaCaptionAuthoringPanePlan!);
        _mediaCaptionPaneHost.Visibility = Visibility.Visible;
        RefreshPaneAccessibilityMetadata();
        return LastMediaCaptionAuthoringPanePlan!;
    }

    internal void HideMediaCaptionPane()
    {
        if (_mediaCaptionPaneHost is not null)
            _mediaCaptionPaneHost.Visibility = Visibility.Collapsed;
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
            PresentationMediaTranscriptPlanner.ApplyCaptionAuthoringMutation(
                media,
                LastMediaCaptionAuthoringMutationPlan);
        if (LastMediaCaptionTrackMutationResult.Succeeded)
        {
            _selectedMediaCaptionTrackIndex = NormalizeMediaCaptionSelectionAfterMutation(
                media,
                intent,
                LastMediaCaptionTrackMutationResult.TrackIndex);
            _file.MarkDirty();
            RefreshReviewWorkflowPlans();
            UpdateTitle();
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
        for (var index = 0; index < _mediaCaptionTrackBox.Items.Count; index++)
        {
            if (_mediaCaptionTrackBox.Items[index] is ComboBoxItem { Tag: int trackIndex }
                && trackIndex == plan.SelectedTrackIndex)
            {
                _mediaCaptionTrackBox.SelectedIndex = index;
                return;
            }
        }

        _mediaCaptionTrackBox.SelectedIndex = -1;
    }

    private static void RenderMediaCaptionField(
        TextBlock label,
        TextBox textBox,
        PresentationMediaCaptionAuthoringFieldPlan field)
    {
        label.Text = field.ValidationMessage is null
            ? field.Label
            : $"{field.Label} - {field.ValidationMessage}";
        textBox.ToolTip = field.ValidationMessage ?? field.Placeholder;
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
        button.ToolTip = action.DisabledReason;
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
        _readingOrderPaneHost.Visibility = Visibility.Visible;
        RefreshPaneAccessibilityMetadata();
        return plan;
    }

    internal PresentationSelectionPanePlan ShowSelectionPane()
    {
        var plan = _selectionPane.Refresh();
        _selectionPane.Visibility = Visibility.Visible;
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
            _altTextTitleBox.ToolTip = plan.Title.Placeholder;
            _altTextDescriptionBox.ToolTip = plan.Description.ValidationMessage ?? plan.Description.Placeholder;
            _altTextTitleBox.IsEnabled = plan.Title.IsEnabled;
            _altTextDescriptionBox.IsEnabled = plan.Description.IsEnabled;
            _altTextDecorativeCheck.Content = decorativeAction.Label;
            _altTextDecorativeCheck.IsEnabled = decorativeAction.IsEnabled;
            _altTextDecorativeCheck.IsChecked = plan.IsDecorative;
            _altTextApplyButton.Content = applyAction.Label;
            _altTextApplyButton.IsEnabled = applyAction.IsEnabled;
            _altTextApplyButton.ToolTip = applyAction.DisabledReason;
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
        button.ToolTip = action.DisabledReason;
        button.Tag = action.CommandId;
    }

    private UIElement BuildReadingOrderItemCard(PresentationReadingOrderItemPlan item)
    {
        var title = new TextBlock
        {
            Text = $"{item.ReadingOrderIndex + 1}. {item.ShapeName}",
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        var metadata = new TextBlock
        {
            Text = $"{item.ShapeTypeLabel} - depth {item.NestingDepth}",
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            TextWrapping = TextWrapping.Wrap,
        };
        var accessibility = new TextBlock
        {
            Text = item.AccessibilitySummary,
            Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
            TextWrapping = TextWrapping.Wrap,
        };
        var altText = new TextBlock
        {
            Text = BuildReadingOrderAltTextLine(item),
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
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
                Text = "Selected item",
                Foreground = new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A)),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 2, 0, 0),
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
            ToolTip = $"Select {item.ShapeName}",
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        button.Click += (_, _) => ApplyReadingOrderSelectItem(item.ShapeId);
        PresentationPaneAccessibilityAdapter.ApplyItem(
            button,
            PresentationPaneAccessibilityPlanner.ReadingOrderPaneId,
            item.ReadingOrderIndex,
            item.ShapeName,
            item.IsSelected ? "Selected" : "Not selected");
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
        _proofingPaneHost.Visibility = Visibility.Visible;
        RefreshPaneAccessibilityMetadata();
        return plan;
    }

    internal PresentationProofingPanePlan SelectProofingIssueRow(int rowIndex)
    {
        var plan = _reviewWorkflowSession.SelectProofingIssueRow(rowIndex);
        RenderProofingPane(plan);
        _proofingPaneHost.Visibility = Visibility.Visible;
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

    private UIElement BuildProofingIssueRowCard(PresentationProofingIssueRowPlan row)
    {
        var action = new Button
        {
            Content = row.CorrectionAction.Label,
            Tag = row.RowIndex,
            MinWidth = 72,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
            IsEnabled = row.CorrectionAction.IsEnabled,
            ToolTip = row.CorrectionAction.DisabledReason,
        };
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
            ToolTip = row.IgnoreAction.DisabledReason,
        };
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
            ToolTip = row.IgnoreAllAction.DisabledReason,
        };
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
            ToolTip = row.AddToDictionaryAction.DisabledReason,
        };
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

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(action);
        buttons.Children.Add(ignore);
        buttons.Children.Add(ignoreAll);
        buttons.Children.Add(addToDictionary);
        buttons.Children.Add(select);

        var panel = new StackPanel { Orientation = Orientation.Vertical };
        panel.Children.Add(new TextBlock
        {
            Text = $"{row.SlideDisplay} - {row.SourceName}",
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"{row.Text} -> {row.SuggestedReplacement}",
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
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
            Background = row.IsSelected ? new SolidColorBrush(Color.FromRgb(0xE8, 0xF1, 0xFF)) : Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 8, 12, 8),
            Child = panel,
        };
        PresentationPaneAccessibilityAdapter.ApplyItem(
            card,
            PresentationPaneAccessibilityPlanner.ProofingPaneId,
            row.RowIndex,
            row.Text,
            row.IsSelected ? "Selected" : "Not selected");
        return card;
    }

    // ── Wave 16B: Animation pane show/hide ───────────────────────────────────────
    //
    // ToggleAnimationPane is called by FreePRibbonCommands when the freep.anim.pane
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
                    Editor,
                    onPreview: StartAnimationPanePreview,
                    onAccessibilityChanged: RefreshPaneAccessibilityMetadata,
                    onEditMotionPath: OpenMotionPathEditor);
                _animPaneHost.Child = _animPane;
            }
            _animPaneHost.Visibility = Visibility.Visible;
            RefreshPaneAccessibilityMetadata();
        }
    }

    /// <summary>
    /// Replaces the AnimationPane with one bound to the current (rebuilt) Editor.
    /// Called from LoadModel after the editor is rebuilt; no-op when pane is hidden.
    /// </summary>
    private void RebuildAnimationPaneIfVisible()
    {
        if (_animPaneHost is null || _animPaneHost.Visibility != Visibility.Visible) return;
        _animPane = new AnimationPane(
            Editor,
            onPreview: StartAnimationPanePreview,
            onAccessibilityChanged: RefreshPaneAccessibilityMetadata,
            onEditMotionPath: OpenMotionPathEditor);
        _animPaneHost.Child = _animPane;
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
            ResolveTokenBrush("FreePStatusSurfaceBrush")
                ?? new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A)),
            _slideCountText,
            LeftMargin: new Thickness(12, 0, 0, 0))).Root;
    }

    private void UpdateSlideCount() =>
        _slideCountText.Text = SisterAppStatusBarTextPlanner.FormatPresentationSlideStatus(
            Editor.CurrentSlideIndex,
            _presentation.Slides.Count,
            ResolveDataFolderLabel());

    // ── Quick-access + title ──────────────────────────────────────────────────────

    private void AddQuickAccessButtons(StackPanel host) =>
        SisterQuickAccessToolbarBuilder.Render(
            host,
            this,
            new SisterQuickAccessToolbarActions(
                Save: () => _file.Save(),
                Undo: () => Editor.Undo(),
                Redo: () => Editor.Redo()));

    private void UpdateTitle()
    {
        _titleBinder.Update(new SisterWpfWindowTitleSpec(
            DisplayName: _file.DisplayName,
            ApplicationName: "FreeP",
            IsDirty: _file.IsDirty,
            DirtyMarker: " *",
            Separator: " \u2014 "));
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
                (_, _) => ExecuteKeyboardCommand(command)));
        }

        foreach (var shortcut in FreePKeyboardShortcutCatalog.All)
        {
            InputBindings.Add(new KeyBinding(
                commands[shortcut.Command],
                new KeyGesture(ToWpfKey(shortcut.Key), ToWpfModifiers(shortcut.Modifiers))));
        }
    }

    private void ExecuteKeyboardCommand(FreePKeyboardCommand command)
    {
        switch (command)
        {
            case FreePKeyboardCommand.NewPresentation: _file.New(); break;
            case FreePKeyboardCommand.OpenPresentation: _file.Open(); break;
            case FreePKeyboardCommand.SavePresentation: _file.Save(); break;
            case FreePKeyboardCommand.SavePresentationAs: _file.SaveAs(); break;
            case FreePKeyboardCommand.PrintPresentation: ShowPrintBackstage(); break;
            case FreePKeyboardCommand.Undo: Editor.Undo(); break;
            case FreePKeyboardCommand.Redo: Editor.Redo(); break;
            case FreePKeyboardCommand.DeleteSelectedShapes: Editor.DeleteSelected(); break;
            case FreePKeyboardCommand.DuplicateCurrentSlide: Editor.DuplicateCurrentSlide(); break;
            case FreePKeyboardCommand.StartSlideShowFromBeginning: StartSlideShow(fromStart: true); break;
            case FreePKeyboardCommand.StartSlideShowFromCurrentSlide: StartSlideShow(fromStart: false); break;
            case FreePKeyboardCommand.Copy:
                WpfClipboardCommands.Copy(Editor, _osClipboard);
                break;
            case FreePKeyboardCommand.Cut:
                WpfClipboardCommands.Cut(Editor, _osClipboard);
                break;
            case FreePKeyboardCommand.Paste:
                _osClipboard.Paste(Editor, preferOsClipboard: true);
                break;
            case FreePKeyboardCommand.Find: OpenFindDialog(); break;
            case FreePKeyboardCommand.Replace: OpenFindReplaceDialog(); break;
            case FreePKeyboardCommand.SelectAll: Editor.SelectAll(); break;
            default: throw new ArgumentOutOfRangeException(nameof(command), command, null);
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

    private void StartSlideShowWithTiming(FreeP.App.Compositor.SlideShowTimingIntent timingIntent)
        => StartSlideShow(fromStart: true, timingIntent: timingIntent);

    private void StartSlideShow(
        bool fromStart,
        FreeP.App.Compositor.SlideShowTimingIntent timingIntent,
        int? animationStartIndex = null)
    {
        if (_presentation.Slides.Count == 0) return;

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

        var window = new SlideShowWindow(_presentation, route, Editor.SetSlideNotesText);
        if (timingIntent != FreeP.App.Compositor.SlideShowTimingIntent.None)
            window.SetPresenterTimingIntent(timingIntent);
        // Owner can only be set when the main window is already shown (not during unit tests).
        if (IsVisible)
            window.Owner = this;
        window.Show();
    }

    // ── Chart data editing (Wave 9B) ──────────────────────────────────────────────

    /// <summary>
    /// Builds the playback route for a stored custom show without opening a window.
    /// </summary>
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
        SlideShowCustomShowPlanner.CreateCustomShow(_presentation, name, slideIds);

    internal SlideShowCustomShowMutationResult RenameCustomShow(
        int customShowIndex,
        string? name) =>
        SlideShowCustomShowPlanner.RenameCustomShow(_presentation, customShowIndex, name);

    internal SlideShowCustomShowMutationResult DeleteCustomShow(int customShowIndex) =>
        SlideShowCustomShowPlanner.DeleteCustomShow(_presentation, customShowIndex);

    internal SlideShowCustomShowMutationResult UpdateCustomShowSlides(
        int customShowIndex,
        IEnumerable<string?> slideIds) =>
        SlideShowCustomShowPlanner.UpdateCustomShowSlides(_presentation, customShowIndex, slideIds);

    internal SlideShowCustomShowMutationResult MoveCustomShowSlide(
        int customShowIndex,
        int sourceSlideIndex,
        string? sourceSlideId,
        int targetSlideIndex) =>
        SlideShowCustomShowPlanner.MoveCustomShowSlide(
            _presentation,
            customShowIndex,
            sourceSlideIndex,
            sourceSlideId,
            targetSlideIndex);

    internal bool TryStartCustomSlideShow(string? customShowName, int startIndex = 0)
    {
        if (!TryBuildCustomSlideShowRoute(customShowName, startIndex, out var route) ||
            route.SlideCount == 0)
        {
            return false;
        }

        var window = new SlideShowWindow(_presentation, route, Editor.SetSlideNotesText);
        if (IsVisible)
            window.Owner = this;
        window.Show();
        return true;
    }

    internal void OpenCustomShowDialog()
    {
        var dialog = new CustomShowDialog(this);
        if (IsVisible)
            dialog.Owner = this;
        dialog.ShowDialog();
    }

    /// <summary>
    /// Opens the <see cref="ChartDataDialog"/> for the currently selected chart.
    /// If the selection is empty or the selected shape is not a chart, does nothing.
    /// </summary>
    internal void OpenChartDataDialog()
    {
        if (!Editor.CanEditSelectedChartData) return;

        var dialog = new ChartDataDialog(Editor);
        if (IsVisible)
            dialog.Owner = this;
        // dialog.ShowDialog() returns true when OK was clicked; the command is already
        // applied inside ChartDataDialog.OnOk() via EditingSession.ReplaceChartData().
        dialog.ShowDialog();
    }

    internal void OpenChartDisplayOptionsDialog()
    {
        if (!Editor.CanEditSelectedChartFormatting) return;

        var dialog = new ChartDisplayOptionsDialog(Editor);
        if (IsVisible)
            dialog.Owner = this;
        dialog.ShowDialog();
    }

    internal void OpenChartAxisOptionsDialog()
    {
        if (!Editor.CanEditSelectedChartFormatting) return;

        var dialog = new ChartAxisOptionsDialog(Editor);
        if (IsVisible)
            dialog.Owner = this;
        dialog.ShowDialog();
    }

    internal void OpenChartSeriesOptionsDialog()
    {
        if (!Editor.CanEditSelectedChartFormatting) return;

        var dialog = new ChartSeriesOptionsDialog(Editor);
        if (IsVisible)
            dialog.Owner = this;
        dialog.ShowDialog();
    }

    internal void OpenChartPointOptionsDialog()
    {
        if (!Editor.CanEditSelectedChartFormatting) return;

        var dialog = new ChartPointOptionsDialog(Editor);
        if (IsVisible)
            dialog.Owner = this;
        dialog.ShowDialog();
    }

    internal void OpenChartLayoutOptionsDialog()
    {
        if (!Editor.CanEditSelectedChartFormatting) return;

        var dialog = new ChartLayoutOptionsDialog(Editor);
        if (IsVisible)
            dialog.Owner = this;
        dialog.ShowDialog();
    }

    internal void OpenChartDataTableOptionsDialog()
    {
        if (!Editor.CanEditSelectedChartFormatting) return;

        var dialog = new ChartDataTableOptionsDialog(Editor);
        if (IsVisible)
            dialog.Owner = this;
        dialog.ShowDialog();
    }

    internal void OpenChartBubbleOptionsDialog()
    {
        if (!Editor.CanEditSelectedChartFormatting
            || Editor.SelectedChart is not { ChartType: ChartType.Bubble }) return;

        var dialog = new ChartBubbleOptionsDialog(Editor);
        if (IsVisible)
            dialog.Owner = this;
        dialog.ShowDialog();
    }

    internal void OpenChartPieOptionsDialog()
    {
        if (!Editor.CanEditSelectedChartFormatting
            || Editor.SelectedChart is not { ChartType: ChartType.Pie or ChartType.Doughnut }) return;

        var dialog = new ChartPieOptionsDialog(Editor);
        if (IsVisible)
            dialog.Owner = this;
        dialog.ShowDialog();
    }

    internal void OpenChartPlotStyleOptionsDialog()
    {
        if (!Editor.CanEditSelectedChartFormatting
            || Editor.SelectedChart is not { ChartType: ChartType.Scatter or ChartType.Radar }) return;

        var dialog = new ChartPlotStyleOptionsDialog(Editor);
        if (IsVisible)
            dialog.Owner = this;
        dialog.ShowDialog();
    }

    internal void OpenChart3DViewOptionsDialog()
    {
        if (!Editor.CanEditSelectedChartFormatting) return;

        var dialog = new Chart3DViewOptionsDialog(Editor);
        if (IsVisible)
            dialog.Owner = this;
        dialog.ShowDialog();
    }

    internal void OpenChartTextOptionsDialog()
    {
        if (!Editor.CanEditSelectedChartFormatting) return;

        var dialog = new ChartTextOptionsDialog(Editor);
        if (IsVisible)
            dialog.Owner = this;
        dialog.ShowDialog();
    }

    internal void OpenChartAreaOptionsDialog()
    {
        if (!Editor.CanEditSelectedChartFormatting) return;
        var dialog = new ChartAreaOptionsDialog(Editor) { Owner = this };
        dialog.ShowDialog();
    }

    internal void OpenChartProtectionOptionsDialog()
    {
        if (Editor.SelectedChart is null) return;
        var dialog = new ChartProtectionOptionsDialog(Editor);
        if (IsVisible)
            dialog.Owner = this;
        dialog.ShowDialog();
    }

    internal void OpenRotationOptionsDialog()
    {
        if (Editor.SelectedShapeIds.Count == 0)
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
        var dialog = new SlideSizeDialog(Editor);
        if (IsVisible)
            dialog.Owner = this;
        dialog.ShowDialog();
    }

    internal void OpenHeaderFooterDialog(HeaderFooterCommandFocus focus)
    {
        var dialog = new HeaderFooterDialog(Editor, focus);
        if (IsVisible)
            dialog.Owner = this;
        dialog.ShowDialog();
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
        var result = WpfFileDialogService.ShowOpenDialog(
            owner: this,
            filter: PresentationMediaFileTypeCatalog.BuildWpfAudioFilter(),
            title: PresentationFileTextResources.InsertAudioPickerTitle);

        if (!result.Chosen || string.IsNullOrWhiteSpace(result.FileName))
        {
            return;
        }

        try
        {
            Editor.SetCurrentSlideTransitionSound(new TransitionSound
            {
                AudioBytes = System.IO.File.ReadAllBytes(result.FileName),
                ContentType = SlideObjectInsertionPlanner.InferMediaContentType(result.FileName, isVideo: false),
                IsBuiltIn = false,
            });
        }
        catch
        {
            // Match the existing ribbon media-pick behavior: a cancelled or unreadable file is a no-op.
        }
    }

    private void InsertEmbeddedObjectFromFile()
    {
        var result = WpfFileDialogService.ShowOpenDialog(
            owner: this,
            filter: "Office files|*.xlsx;*.xlsm;*.xls;*.docx;*.doc;*.pptx;*.ppt|All files|*.*",
            title: OleInsertionPlanner.PickerTitle);

        if (!result.Chosen || string.IsNullOrWhiteSpace(result.FileName))
            return;

        try
        {
            Editor.InsertEmbeddedObject(
                File.ReadAllBytes(result.FileName),
                result.FileName);
            RefreshCanvas();
            UpdateSlideCount();
        }
        catch
        {
            // Cancelled or unreadable files are a no-op, matching the other insert pickers.
        }
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
                Content = choice.IsDefault ? $"{choice.Label} (default)" : choice.Label,
                Margin = new Thickness(2),
                Padding = new Thickness(6, 4, 6, 4),
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

        _layoutPickerPanel.Children.Clear();
        foreach (var group in plan.Groups)
        {
            _layoutPickerPanel.Children.Add(new TextBlock
            {
                Text = group.Heading,
                Margin = new Thickness(10, 8, 10, 2),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
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
        _layoutPickerHost.Visibility = Visibility.Visible;
    }

    private void HideLayoutPicker()
    {
        if (_layoutPickerHost is not null)
            _layoutPickerHost.Visibility = Visibility.Collapsed;
    }

    private static string BuildLayoutChoiceLabel(PresentationLayoutChoice choice)
    {
        var currentPrefix = choice.IsCurrent ? "Current - " : string.Empty;
        var placeholders = choice.PlaceholderCount == 1 ? "1 placeholder" : $"{choice.PlaceholderCount} placeholders";
        return $"{currentPrefix}{choice.DisplayName}\n{choice.MasterDisplayName} - {placeholders}";
    }

    private static UIElement BuildLayoutChoiceTile(PresentationLayoutChoice choice)
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
        };
        stack.Children.Add(BuildLayoutThumbnail(choice));
        stack.Children.Add(label);

        if (!string.IsNullOrWhiteSpace(choice.Chrome.BadgeText))
        {
            stack.Children.Add(new TextBlock
            {
                Text = choice.Chrome.BadgeText,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A)),
                FontWeight = FontWeights.SemiBold,
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

    private static UIElement BuildLayoutThumbnail(PresentationLayoutChoice choice)
    {
        var canvas = new Canvas
        {
            Width = PresentationDesignCommandPlanner.LayoutThumbnailWidthDip,
            Height = PresentationDesignCommandPlanner.LayoutThumbnailHeightDip,
            Background = Brushes.White,
        };

        foreach (var placeholder in choice.ThumbnailPlaceholders)
        {
            var rect = new System.Windows.Shapes.Rectangle
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

    private static Brush BuildLayoutPlaceholderFill(PlaceholderType type) =>
        type is PlaceholderType.Title or PlaceholderType.CenteredTitle or PlaceholderType.SubTitle
            ? new SolidColorBrush(Color.FromRgb(0xF8, 0xDD, 0xD1))
            : new SolidColorBrush(Color.FromRgb(0xEA, 0xF1, 0xF6));

    private static (Brush Border, Brush Background) BuildLayoutChoiceBrushes(
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
        var request = HyperlinkDialogPlanner.BuildDialogRequest(
            Editor.Presentation.Slides,
            editsSelectedRun ? selectedRunHyperlink : Editor.SelectedShapeHyperlink);
        var dialog = new HyperlinkDialog(request);
        if (IsVisible) dialog.Owner = this;
        var applyPlan = dialog.ShowDialog() == true
            ? HyperlinkDialogPlanner.BuildApplyPlan(dialog.Result)
            : HyperlinkDialogPlanner.BuildApplyPlan(null);
        if (!applyPlan.ShouldApply)
            return;

        var hyperlink = new ModelHyperlink
        {
            Url = applyPlan.Url,
            TargetSlideId = applyPlan.TargetSlideId,
            Tooltip = applyPlan.Tooltip,
        };
        if (editsSelectedRun && textEditor?.TryApplySelectedShapeRunHyperlink(hyperlink) == true)
            return;

        Editor.SetShapeHyperlink(applyPlan.Url, applyPlan.TargetSlideId, applyPlan.Tooltip);
    }

    internal void OpenSlideZoomDialog()
    {
        var options = SlideZoomInsertionPlanner.BuildTargetOptions(
            Editor.Presentation.Slides,
            Editor.CurrentSlideIndex);
        if (options.Count == 0)
            return;

        var dialog = new SlideZoomDialog(options);
        if (IsVisible)
            dialog.Owner = this;
        if (dialog.ShowDialog() == true && dialog.SelectedTargetSlideId is { Length: > 0 } targetSlideId)
            Editor.InsertSlideZoom(targetSlideId);
    }

    internal void OpenSectionZoomDialog()
    {
        var options = SectionZoomInsertionPlanner.BuildTargetOptions(
            Editor.Presentation,
            Editor.CurrentSlideIndex);
        if (options.Count == 0)
            return;

        var dialog = new SectionZoomDialog(options);
        if (IsVisible)
            dialog.Owner = this;
        if (dialog.ShowDialog() == true && dialog.SelectedTargetSectionId is { Length: > 0 } targetSectionId)
            Editor.InsertSectionZoom(targetSectionId);
    }

    internal void OpenSummaryZoomDialog()
    {
        var options = SummaryZoomInsertionPlanner.BuildTargetOptions(
            Editor.Presentation,
            Editor.CurrentSlideIndex);
        if (options.Count < 2)
            return;

        var dialog = new SummaryZoomDialog(options);
        if (IsVisible)
            dialog.Owner = this;
        if (dialog.ShowDialog() == true)
            Editor.InsertSummaryZoom(dialog.SelectedTargetSectionIds);
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

    private void ShowBackstage() => ShowBackstage("Info");

    private void ShowBackstage(string paneLabel) => _backstage.Show(paneLabel);

    private void ShowPrintBackstage()
    {
        RefreshPrintBackstagePlan();
        ShowBackstage("Print");
    }

    internal void ShowBackstageForTests() => ShowBackstage();

    internal bool IsBackstageOpen => _backstage.IsOpen;

    internal string? CurrentBackstagePaneLabel => _backstage.EvidencePaneLabel;

    internal bool ActivateBackstageEntryForTests(string label)
    {
        _backstage.Show(label);
        return _backstage.CurrentPaneContent is not null;
    }

    internal bool ApplyBackstagePrintCustomRangeForTests(string rangeText) =>
        _backstage.ApplyCustomPrintRangeForTests(rangeText);

    private PresentationSlideRangeRequest BuildCurrentSlideImageExportRange() =>
        new(
            PresentationSlideRangeKind.CurrentSlide,
            CurrentSlideNumber: Editor.CurrentSlideIndex + 1);

    // ── Ribbon ────────────────────────────────────────────────────────────────────

    private UIElement BuildRibbon(RibbonDefinition definition, IRibbonCommandRegistry registry, IRibbonStateStore stateStore)
    {
        FreePRibbonIcons.Install();

        var result = RibbonShellBuilder.Build(new RibbonShellBuildSpec(
            definition,
            registry,
            stateStore,
            FileTabHeader:  "File",
            FileTabAccent:  Color.FromRgb(0xB7, 0x47, 0x2A),
            FileTabHover:   Color.FromRgb(0x8F, 0x37, 0x21),
            ShowBackstage));

        _ribbonTabs    = result.Tabs;
        _fileTab       = result.FileTab;
        _fileTabRouter = result.FileTabRouter;
        return result.Root;
    }

    private static string ResolveDataFolderLabel()
        => AppStoragePathPlanner.GetOptionsFilePathLabelOrFallback(PlatformApplicationDataPathProvider.LocalInstance);
}
