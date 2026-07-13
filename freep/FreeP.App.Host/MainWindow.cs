using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Free.Shared.AppServices;
using Free.Shared.Ribbon.Wpf;
using Free.Shared.Shell.Wpf;
using FreeP.App.Compositor;
using FreeP.App.Host.Backstage;
using FreeP.App.Rendering.Wpf;
using FreeP.Core.Model;

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
public sealed class MainWindow : Window
{
    // Identity/palette for the shared window shell (PowerPoint-style brick title bar; "P" badge).
    private static ShellChromeOptions BuildChromeOptions() => new()
    {
        BadgeLetter = "P",
        TitleBarColor = ResolveTokenColor("FreePTitleBarBrush",   Color.FromRgb(0xB7, 0x47, 0x2A)),
        BadgeColor    = ResolveTokenColor("FreePAccentDarkBrush", Color.FromRgb(0x8F, 0x37, 0x21)),
        CaptionHeight = 34
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
    private int? _selectedCommentIndex;
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
    private PresentationProofingIgnoreState _proofingIgnoreState = PresentationProofingIgnoreState.Empty;

    internal PresentationCommentPanePlan? LastCommentPanePlan { get; private set; }
    internal PresentationCommentNavigationPlan? LastCommentNavigationPlan { get; private set; }
    internal PresentationCommentMentionPickerPlan? LastCommentMentionPickerPlan { get; private set; }
    internal PresentationCommentMentionInsertionPlan? LastCommentMentionInsertionPlan { get; private set; }
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
    internal PresentationMediaTranscriptPlan? LastMediaTranscriptPlan { get; private set; }
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
    internal bool IsAccessibilityCheckerPaneVisible => _accessibilityCheckerPaneHost?.Visibility == Visibility.Visible;
    internal int AccessibilityCheckerPaneRowCount => LastAccessibilityCheckerPanePlan?.Rows.Count ?? 0;
    internal int AccessibilityCheckerPaneSelectedRowCount =>
        LastAccessibilityCheckerPanePlan?.Rows.Count(row => row.IsSelected) ?? 0;
    internal string AccessibilityCheckerPaneHeading => _accessibilityCheckerPaneHeading?.Text ?? string.Empty;
    internal string AccessibilityCheckerPaneMessage => _accessibilityCheckerPaneMessage?.Text ?? string.Empty;
    internal bool IsDirty => _file.IsDirty;
    internal int ReviewCommentSelectedCount => LastCommentPanePlan?.Comments.Count(comment => comment.IsSelected) ?? 0;
    internal string ReviewCommentPaneSummary => LastCommentPanePlan?.DeckSummaryLabel ?? string.Empty;
    internal IReadOnlyList<string> ReviewCommentPaneFilterStates =>
        LastCommentPanePlan?.Filters.Select(filter =>
            $"{filter.Kind}|{filter.Label}|{filter.Count}|{filter.IsSelected}|{filter.HasMatches}").ToArray() ?? [];
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
    internal string ProofingPaneHeading => _proofingPaneHeading?.Text ?? string.Empty;
    internal string ProofingPaneMessage => _proofingPaneMessage?.Text ?? string.Empty;
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

    /// <summary>
    /// Test-seam: exposes the animation pane host border so tests can inspect visibility
    /// without launching the actual UI.  Internal; only visible to FreeP.App.Host.Tests.
    /// </summary>
    internal Border? AnimPaneHostForTest => _animPaneHost;
    // 16B SEAM END

    // ── Constructors ──────────────────────────────────────────────────────────────

    public MainWindow() : this(new FreePOptions()) { }

    public MainWindow(
        FreePOptions options,
        ApplicationOptionsStore<FreePOptions>? optionsStore = null,
        IUserMessageService? messageService = null)
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

        // File commands.
        _file = new FileCommands(
            this,
            () => _presentation,
            LoadModel,
            UpdateTitle,
            _options,
            messageService: _messageService,
            getImageExportRange: BuildCurrentSlideImageExportRange,
            getPrintCurrentSlideNumber: () => Editor.CurrentSlideIndex + 1);

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
            onEditChartData:    () => OpenChartDataDialog(),
            getSlideCanvas:     () => SlideCanvas,
            // Wave 10B: open custom slide-size dialog from Design tab ribbon button.
            onCustomSlideSize:  () => OpenSlideSizeDialog(),
            onLayoutPicker:     () => OpenLayoutPicker(),
            // Wave 10B: OS-clipboard service for ribbon Copy/Cut/Paste buttons.
            osClipboard:        _osClipboard,
            // Wave 11A: Insert Hyperlink dialog.
            onInsertLink:       () => OpenHyperlinkDialog(),
            // Wave 12B: Find & Replace dialogs.
            onFind:             () => OpenFindDialog(),
            onFindReplace:      () => OpenFindReplaceDialog(),
            onReviewCommentsPane: () => ShowReviewCommentsPane(),
            onReviewAccessibility: () => ShowAccessibilityCheckerPane(),
            onReviewAltText: () => ShowAltTextPane(),
            onReviewReadingOrder: () => ShowReadingOrderPane(),
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
            onTablePicker:      () => OpenTablePicker(),
            onHeaderFooter:     focus => OpenHeaderFooterDialog(focus),
            getViewShowState:   () => _viewShowState,
            applyViewShowState: ApplyPresentationViewShowState,
            getViewZoomState:   () => _viewZoomState,
            applyViewZoomState: ApplyPresentationViewZoomState,
            onCustomShows:      () => OpenCustomShowDialog());
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

        // File keyboard shortcuts.
        CommandBindings.Add(new CommandBinding(ApplicationCommands.New,    (_, _) => _file.New()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Open,   (_, _) => _file.Open()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save,   (_, _) => _file.Save()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.SaveAs, (_, _) => _file.SaveAs()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Print,  (_, _) => RefreshPrintBackstagePlan()));

        // Editing keyboard shortcuts (Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z / Delete / Ctrl+D).
        AddEditingKeyBindings();

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
            PlanPrint: () => RefreshPrintBackstagePlan(),
            ExportVideo: () => RefreshVideoFramePackage(),
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

        Editor.Changed           += () => { _file.MarkDirty(); RefreshCanvas(); RefreshNotesPane(); UpdateSlideCount(); UpdateTitle(); RefreshReviewWorkflowPlans(); };
        Editor.CurrentSlideChanged += (_, _) => { _selectedCommentIndex = null; RefreshCanvas(); RefreshNotesPane(); RefreshCommentPane(); RefreshReviewWorkflowPlans(); };
        Editor.SelectionChanged += (_, _) =>
        {
            RefreshAltTextRequestPlan();
            RefreshReadingOrderPlan();
            if (IsAltTextPaneVisible)
                ShowAltTextPane();
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
        SlideCanvas.AttachEditing(Editor, _textOverlay);
        SlideCanvas.ApplyViewShowState(_viewShowState);
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
            Width      = 180,
            Background = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
        };
        // 3B SEAM: attach the slide-thumbnail pane.
        SlidePaneHost.Child = new SlidePane(Editor);

        // CENTRE stage — the canvas proper.
        SlideCanvas = new SlideCanvas
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Stretch,
            Margin              = new Thickness(40)
        };

        // 3C SEAM: text-edit overlay Canvas (sits on top of the canvas, same coordinate space).
        _textOverlay = new Canvas
        {
            IsHitTestVisible    = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Stretch
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

        // AdornerDecorator ensures the adorner layer sits directly above SlideCanvas,
        // so SelectionAdorner handles are positioned correctly regardless of zoom.
        var adornerDecorator = new AdornerDecorator { Child = stageGrid };

        _canvasHost = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)),
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
            MinHeight           = 60,
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
        _proofingPaneHost = BuildProofingPaneHost();

        var splitter = new Grid();
        splitter.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        splitter.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        splitter.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        splitter.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        splitter.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        splitter.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        splitter.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 16B: anim pane
        Grid.SetColumn(SlidePaneHost,  0);
        Grid.SetColumn(rightPanel,     1);
        Grid.SetColumn(_accessibilityCheckerPaneHost, 2);
        Grid.SetColumn(_altTextPaneHost, 3);
        Grid.SetColumn(_readingOrderPaneHost, 4);
        Grid.SetColumn(_proofingPaneHost, 5);
        Grid.SetColumn(_animPaneHost,  6); // 16B
        splitter.Children.Add(SlidePaneHost);
        splitter.Children.Add(rightPanel);
        splitter.Children.Add(_accessibilityCheckerPaneHost);
        splitter.Children.Add(_altTextPaneHost);
        splitter.Children.Add(_readingOrderPaneHost);
        splitter.Children.Add(_proofingPaneHost);
        splitter.Children.Add(_animPaneHost); // 16B

        return splitter;
    }

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

        var panel = new DockPanel();
        var header = new StackPanel { Orientation = Orientation.Vertical };
        header.Children.Add(_accessibilityCheckerPaneHeading);
        header.Children.Add(_accessibilityCheckerPaneMessage);
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
    }

    // ── Comment pane + overlay refresh (Wave 11B) ────────────────────────────────

    /// <summary>
    /// Refreshes the comment indicator overlay dots (on the stage canvas) and the
    /// comment list strip below the canvas for the current slide.
    /// Guards null fields so it is safe to call before BuildBody completes.
    /// </summary>
    private void RefreshCommentPane()
    {
        if (_commentOverlay is null || _commentListHost is null || _commentListPanel is null) return;

        var plan = PresentationReviewWorkflowPlanner.BuildCommentPanePlan(
            _presentation.Slides,
            Editor.CurrentSlideIndex,
            _selectedCommentIndex);
        LastCommentPanePlan = plan;
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
            foreach (var cm in comments)
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
                cardHost.MouseLeftButtonDown += (_, _) => SelectReviewComment(cm.CommentIndex);
                _commentListPanel.Children.Add(cardHost);
            }
            _commentListHost.Visibility = Visibility.Visible;
        }
        else
        {
            _commentListHost.Visibility = Visibility.Collapsed;
        }
    }

    private static void AddCommentPaneSummary(Panel host, PresentationCommentPanePlan plan)
    {
        host.Children.Add(new TextBlock
        {
            Text = $"{plan.CurrentSlideSummaryLabel} | {plan.DeckSummaryLabel}",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
            Margin = new Thickness(0, 0, 0, 6),
        });
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

        var input = new TextBox
        {
            Text = GetCommentText(cm.CommentIndex) ?? cm.TextPreview,
            MinWidth = 220,
            Margin = new Thickness(16, 0, 6, 6)
        };
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
        var button = new System.Windows.Controls.Button
        {
            Content = "Reply",
            MinWidth = 58,
        };
        button.Click += (_, _) => ReplyToSelectedComment(input.Text);
        row.Children.Add(input);
        row.Children.Add(button);
        card.Children.Add(row);
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
        LastVideoExportPlan = LastVideoFramePackage.Plan.ExportPlan;
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
        LastCommentPanePlan = PresentationReviewWorkflowPlanner.BuildCommentPanePlan(
            _presentation.Slides,
            Editor.CurrentSlideIndex,
            _selectedCommentIndex);
        RefreshAccessibilitySummaryPlan();
        RefreshAltTextRequestPlan();
        RefreshReadingOrderPlan();
        RefreshProofingRequestPlan();
    }

    private void ShowReviewCommentsPane()
    {
        RefreshCommentPane();
    }

    internal PresentationCommentPanePlan SetSelectedReviewCommentIndexForTests(int? commentIndex)
    {
        _selectedCommentIndex = commentIndex;
        RefreshCommentPane();
        return LastCommentPanePlan!;
    }

    private void SelectReviewComment(int commentIndex)
    {
        _selectedCommentIndex = commentIndex;
        RefreshCommentPane();
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
        RefreshCommentPane();
        RefreshReviewWorkflowPlans();
        UpdateSlideCount();
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

    internal PresentationCommentMentionPickerPlan BuildCommentMentionPickerPlanForTests(
        string? query = null,
        string? currentAuthor = null,
        string? currentInitials = null)
    {
        LastCommentMentionPickerPlan = PresentationReviewWorkflowPlanner.BuildCommentMentionPickerPlan(
            _presentation.Slides,
            query,
            currentAuthor,
            currentInitials);
        return LastCommentMentionPickerPlan;
    }

    internal PresentationCommentMentionInsertionPlan InsertCommentMentionForTests(
        string? text,
        int caretIndex,
        PresentationCommentMentionCandidate? candidate)
    {
        LastCommentMentionInsertionPlan = PresentationReviewWorkflowPlanner.BuildCommentMentionInsertionPlan(
            text,
            caretIndex,
            candidate);
        return LastCommentMentionInsertionPlan;
    }

    internal PresentationCommentMutationPlan InsertMentionInSelectedCommentForTests(
        int caretIndex,
        PresentationCommentMentionCandidate? candidate,
        string? author = null,
        string? initials = null)
    {
        LastCommentMentionInsertionPlan = PresentationReviewWorkflowPlanner.BuildCommentMentionInsertionPlan(
            GetSelectedCommentText(),
            caretIndex,
            candidate);
        if (!LastCommentMentionInsertionPlan.ShouldApply)
        {
            return new PresentationCommentMutationPlan(
                PresentationReviewWorkflowIntentKind.EditComment,
                false,
                Editor.CurrentSlideIndex,
                _selectedCommentIndex,
                null,
                LastCommentMentionInsertionPlan.ValidationMessage);
        }

        return EditSelectedComment(LastCommentMentionInsertionPlan.UpdatedText, author, initials);
    }

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
            _file.MarkDirty();
            RefreshCommentPane();
            RefreshReviewWorkflowPlans();
            UpdateTitle();
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
        return card;
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
        _altTextPaneHost.Visibility = Visibility.Visible;
    }

    internal void HideAltTextPane()
    {
        if (_altTextPaneHost is not null)
            _altTextPaneHost.Visibility = Visibility.Collapsed;
    }

    internal PresentationReadingOrderPlan ShowReadingOrderPane()
    {
        var plan = RefreshReadingOrderPlan();
        RenderReadingOrderPane(plan);
        _readingOrderPaneHost.Visibility = Visibility.Visible;
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
            _file.MarkDirty();
            RefreshCanvas();
            RefreshNotesPane();
            RefreshReviewWorkflowPlans();
            UpdateTitle();
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
                _selectedProofingIssueRowIndex,
                _proofingIgnoreState);
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
        _proofingPaneHost.Visibility = Visibility.Visible;
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
            _selectedProofingIssueRowIndex,
            _proofingIgnoreState);
        RenderProofingPane(LastProofingPanePlan);
        _proofingPaneHost.Visibility = Visibility.Visible;
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
            var refreshed = PresentationReviewWorkflowPlanner.BuildProofingPanePlan(
                LastProofingExecutionPlan!,
                ignoreState: _proofingIgnoreState);
            LastProofingPanePlan = PresentationReviewWorkflowPlanner.BuildProofingPanePlan(
                LastProofingExecutionPlan!,
                PresentationReviewWorkflowPlanner.NormalizeProofingSelectionAfterCorrection(
                    previousSelection,
                    refreshed),
                _proofingIgnoreState);
            _selectedProofingIssueRowIndex = LastProofingPanePlan.SelectedRowIndex >= 0
                ? LastProofingPanePlan.SelectedRowIndex
                : null;
            if (IsProofingPaneVisible)
                RenderProofingPane(LastProofingPanePlan);
        }

        return mutation;
    }

    internal PresentationProofingPanePlan IgnoreSelectedProofingIssue()
    {
        if (LastProofingPanePlan is null)
            ShowProofingPane();

        var previousSelection = LastProofingPanePlan!.SelectedRowIndex;
        _proofingIgnoreState = PresentationReviewWorkflowPlanner.AddProofingIgnoredIssue(
            _proofingIgnoreState,
            LastProofingPanePlan.SelectedRow);
        return RefreshProofingPaneAfterIgnore(previousSelection);
    }

    internal PresentationProofingPanePlan IgnoreAllSelectedProofingIssues()
    {
        if (LastProofingPanePlan is null)
            ShowProofingPane();

        var previousSelection = LastProofingPanePlan!.SelectedRowIndex;
        _proofingIgnoreState = PresentationReviewWorkflowPlanner.AddProofingIgnoredIssueGroup(
            _proofingIgnoreState,
            LastProofingPanePlan.SelectedRow);
        return RefreshProofingPaneAfterIgnore(previousSelection);
    }

    private PresentationProofingPanePlan RefreshProofingPaneAfterIgnore(int previousSelection)
    {
        var refreshed = PresentationReviewWorkflowPlanner.BuildProofingPanePlan(
            LastProofingExecutionPlan!,
            ignoreState: _proofingIgnoreState);
        LastProofingPanePlan = PresentationReviewWorkflowPlanner.BuildProofingPanePlan(
            LastProofingExecutionPlan!,
            PresentationReviewWorkflowPlanner.NormalizeProofingSelectionAfterIgnore(previousSelection, refreshed),
            _proofingIgnoreState);
        _selectedProofingIssueRowIndex = LastProofingPanePlan.SelectedRowIndex >= 0
            ? LastProofingPanePlan.SelectedRowIndex
            : null;
        if (IsProofingPaneVisible)
            RenderProofingPane(LastProofingPanePlan);

        return LastProofingPanePlan;
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

        return new Border
        {
            Background = row.IsSelected ? new SolidColorBrush(Color.FromRgb(0xE8, 0xF1, 0xFF)) : Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 8, 12, 8),
            Child = panel,
        };
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
        }
        else
        {
            // Lazy construction: create the pane against the current Editor.
            if (_animPane is null || _animPaneHost.Child is null)
            {
                _animPane = new AnimationPane(Editor, onPreview: () => StartSlideShow(fromStart: false));
                _animPaneHost.Child = _animPane;
            }
            _animPaneHost.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// Replaces the AnimationPane with one bound to the current (rebuilt) Editor.
    /// Called from LoadModel after the editor is rebuilt; no-op when pane is hidden.
    /// </summary>
    private void RebuildAnimationPaneIfVisible()
    {
        if (_animPaneHost is null || _animPaneHost.Visibility != Visibility.Visible) return;
        _animPane = new AnimationPane(Editor, onPreview: () => StartSlideShow(fromStart: false));
        _animPaneHost.Child = _animPane;
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

    private void AddEditingKeyBindings()
    {
        // Undo: Ctrl+Z
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Undo, (_, _) => Editor.Undo()));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Undo,
            new KeyGesture(Key.Z, ModifierKeys.Control)));

        // Redo: Ctrl+Y
        var redoCommand = new RoutedCommand("Redo", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(redoCommand, (_, _) => Editor.Redo()));
        InputBindings.Add(new KeyBinding(redoCommand, new KeyGesture(Key.Y, ModifierKeys.Control)));
        InputBindings.Add(new KeyBinding(redoCommand, new KeyGesture(Key.Z, ModifierKeys.Control | ModifierKeys.Shift)));

        // Delete: delete selected shapes (only when canvas-region has focus — 3C refines).
        var deleteCommand = new RoutedCommand("DeleteSelected", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(deleteCommand, (_, _) => Editor.DeleteSelected()));
        InputBindings.Add(new KeyBinding(deleteCommand, new KeyGesture(Key.Delete)));

        // Ctrl+D: duplicate current slide.
        var dupSlideCommand = new RoutedCommand("DuplicateSlide", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(dupSlideCommand, (_, _) => Editor.DuplicateCurrentSlide()));
        InputBindings.Add(new KeyBinding(dupSlideCommand, new KeyGesture(Key.D, ModifierKeys.Control)));

        // F5: Start slide show from the beginning.
        var slideShowFromStart = new RoutedCommand("SlideShowFromStart", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(slideShowFromStart, (_, _) => StartSlideShow(fromStart: true)));
        InputBindings.Add(new KeyBinding(slideShowFromStart, new KeyGesture(Key.F5)));

        // Shift+F5: Start slide show from the current slide.
        var slideShowFromCurrent = new RoutedCommand("SlideShowFromCurrent", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(slideShowFromCurrent, (_, _) => StartSlideShow(fromStart: false)));
        InputBindings.Add(new KeyBinding(slideShowFromCurrent, new KeyGesture(Key.F5, ModifierKeys.Shift)));

        // Wave 5B / 10B: Clipboard keyboard shortcuts (Ctrl+C / Ctrl+X / Ctrl+V).
        // Copy and Cut update both the internal clipboard (EditingSession) AND the OS clipboard
        // (OsClipboardService) so shapes can be pasted into other apps.
        // Paste checks OS clipboard first (image → picture, text → textbox) then internal.
        var copyCommand = new RoutedCommand("CopyShapes", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(copyCommand, (_, _) =>
        {
            Editor.CopySelectedShapes();
            _osClipboard.PlaceSelectionOnOsClipboard(Editor);
        }));
        InputBindings.Add(new KeyBinding(copyCommand, new KeyGesture(Key.C, ModifierKeys.Control)));

        var cutCommand = new RoutedCommand("CutShapes", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(cutCommand, (_, _) =>
        {
            // Y7: capture the selection on the OS clipboard BEFORE CutSelectedShapes()
            // calls DeleteSelected() → ClearSelection(), which would leave an empty
            // selection and cause PlaceSelectionOnOsClipboard to silently no-op.
            // Order: (1) deep-clone to internal clipboard, (2) render+push to OS clipboard,
            // (3) delete the originals.  Both clipboards end up populated.
            Editor.CopySelectedShapes();
            _osClipboard.PlaceSelectionOnOsClipboard(Editor);
            Editor.DeleteSelected();
        }));
        InputBindings.Add(new KeyBinding(cutCommand, new KeyGesture(Key.X, ModifierKeys.Control)));

        var pasteCommand = new RoutedCommand("PasteShapes", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(pasteCommand, (_, _) =>
            _osClipboard.Paste(Editor, preferOsClipboard: true)));
        InputBindings.Add(new KeyBinding(pasteCommand, new KeyGesture(Key.V, ModifierKeys.Control)));

        // Wave 12B: Ctrl+F — Find, Ctrl+H — Find & Replace.
        var findCommand = new RoutedCommand("FindText", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(findCommand, (_, _) => OpenFindDialog()));
        InputBindings.Add(new KeyBinding(findCommand, new KeyGesture(Key.F, ModifierKeys.Control)));

        var replaceCommand = new RoutedCommand("ReplaceText", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(replaceCommand, (_, _) => OpenFindReplaceDialog()));
        InputBindings.Add(new KeyBinding(replaceCommand, new KeyGesture(Key.H, ModifierKeys.Control)));
    }

    // ── Slide show (Wave 4B) ──────────────────────────────────────────────────────

    /// <summary>
    /// Launches the fullscreen slide show playback.
    /// Called by F5 (fromStart=true) and Shift+F5 (fromStart=false).
    /// Wave 4C adds ribbon buttons that call this method; keep internal/public + discoverable.
    /// </summary>
    internal void StartSlideShow(bool fromStart)
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

        var window = new SlideShowWindow(_presentation, route);
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

        var window = new SlideShowWindow(_presentation, route);
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
        if (Editor.SelectedChart is null) return;

        var dialog = new ChartDataDialog(Editor);
        if (IsVisible)
            dialog.Owner = this;
        // dialog.ShowDialog() returns true when OK was clicked; the command is already
        // applied inside ChartDataDialog.OnOk() via EditingSession.ReplaceChartData().
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
        var request = HyperlinkDialogPlanner.BuildDialogRequest(
            Editor.Presentation.Slides,
            Editor.SelectedShapeHyperlink);
        var dialog = new HyperlinkDialog(request);
        if (IsVisible) dialog.Owner = this;
        var applyPlan = dialog.ShowDialog() == true
            ? HyperlinkDialogPlanner.BuildApplyPlan(dialog.Result)
            : HyperlinkDialogPlanner.BuildApplyPlan(null);
        if (applyPlan.ShouldApply)
            Editor.SetShapeHyperlink(applyPlan.Url, applyPlan.TargetSlideId, applyPlan.Tooltip);
    }

    // ── Find & Replace dialog (Wave 12B) ──────────────────────────────────────────

    /// <summary>The live Find/Replace dialog instance (modeless).  Null when closed.</summary>
    private FindReplaceDialog? _findReplaceDialog;

    /// <summary>
    /// Opens (or focuses) the Find dialog in Find-only mode (Ctrl+F).
    /// </summary>
    internal void OpenFindDialog()
    {
        if (_findReplaceDialog is null || !_findReplaceDialog.IsVisible)
        {
            _findReplaceDialog = new FindReplaceDialog(Editor, showReplace: false);
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
            _findReplaceDialog = new FindReplaceDialog(Editor, showReplace: true);
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

    private void ShowBackstage() => _backstage.Show();

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
