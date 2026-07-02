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
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
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
    private const double SlidePaneAvaloniaSlideItemHeight = 108.0;
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
    private readonly TextBox _notesBox;
    private readonly TextBlock _statusText;
    private Border _layoutPickerHost = null!;
    private StackPanel _layoutPickerPanel = null!;
    private Border _tablePickerHost = null!;
    private WrapPanel _tablePickerPanel = null!;
    private Border _reviewCommentsPaneHost = null!;
    private StackPanel _reviewCommentsPanePanel = null!;
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
    private Border _readingOrderPaneHost = null!;
    private TextBlock _readingOrderPaneHeading = null!;
    private TextBlock _readingOrderPaneMessage = null!;
    private StackPanel _readingOrderPaneItemsPanel = null!;
    private Button _readingOrderMoveEarlierButton = null!;
    private Button _readingOrderMoveLaterButton = null!;

    // ── Interaction layer (Theme 15) ────────────────────────────────────────────

    private SelectionAdornerLayer?       _adorner;
    private AvaloniaCanvasGestureHandler? _gestureHandler;
    private AvaloniaInCanvasTextEditor?  _textEditor;

    private bool _notesRefreshing;
    private bool _slidePaneRefreshing;
    private bool _slidePaneIsDragging;
    private int _slidePaneDragSourceIndex = -1;
    private int _slidePaneDragTargetIndex = -1;
    private Point _slidePaneDragStartPoint;

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
    internal bool IsSlidePaneInsertionIndicatorVisible => _slidePaneInsertionIndicator.IsVisible;
    internal bool IsSlidePaneNewSlideButtonVisible => _slidePaneNewSlideButton.IsVisible;
    internal string? SlidePaneNewSlideButtonText => _slidePaneNewSlideButton.Content?.ToString();

    internal bool IsDirty => _fileWorkflow.IsDirty;

    internal string? CurrentPath => _fileWorkflow.CurrentPath;

    internal IReadOnlyList<RecentFileEntry> RecentEntries => _fileWorkflow.RecentEntries;

    internal PresentationCommentPanePlan? LastCommentPanePlan { get; private set; }
    internal PresentationAccessibilitySummaryPlan? LastAccessibilitySummaryPlan { get; private set; }
    internal PresentationAltTextRequestPlan? LastAltTextRequestPlan { get; private set; }
    internal PresentationAltTextPanePlan? LastAltTextPanePlan { get; private set; }
    internal PresentationReadingOrderPlan? LastReadingOrderPlan { get; private set; }
    internal PresentationProofingRequestPlan? LastProofingRequestPlan { get; private set; }
    internal AnimationPaneTimelinePlan? LastAnimationPaneTimelinePlan { get; private set; }
    internal PresentationDesignCommandPlan? LastLayoutRequestPlan { get; private set; }
    internal PresentationHandoutLayoutPlan? LastHandoutLayoutPlan { get; private set; }
    internal PresentationNotesPagePreviewPlan? LastNotesPagePreviewPlan { get; private set; }
    internal PresentationLayoutPickerPlan? LastLayoutPickerPlan { get; private set; }
    internal PresentationLayoutChoice? LastAppliedLayoutChoice { get; private set; }
    internal TableInsertionPickerPlan? LastTablePickerPlan { get; private set; }
    internal bool IsLayoutPickerVisible => _layoutPickerHost?.IsVisible == true;
    internal bool IsTablePickerVisible => _tablePickerHost?.IsVisible == true;
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
    internal bool IsReadingOrderPaneVisible => _readingOrderPaneHost?.IsVisible == true;
    internal int ReadingOrderPaneItemCount => LastReadingOrderPlan?.Items.Count ?? 0;
    internal string ReadingOrderPaneHeading => _readingOrderPaneHeading?.Text ?? string.Empty;
    internal string ReadingOrderPaneMessage => _readingOrderPaneMessage?.Text ?? string.Empty;
    internal bool IsReadingOrderMoveEarlierEnabled => _readingOrderMoveEarlierButton?.IsEnabled == true;
    internal bool IsReadingOrderMoveLaterEnabled => _readingOrderMoveLaterButton?.IsEnabled == true;
    internal string? ReadingOrderMoveEarlierDisabledReason =>
        LastReadingOrderPlan?.Actions.SingleOrDefault(action =>
            action.CommandId == PresentationReviewWorkflowPlanner.ReadingOrderMoveEarlierCommandId)?.DisabledReason;
    internal string? ReadingOrderMoveLaterDisabledReason =>
        LastReadingOrderPlan?.Actions.SingleOrDefault(action =>
            action.CommandId == PresentationReviewWorkflowPlanner.ReadingOrderMoveLaterCommandId)?.DisabledReason;

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
            Height              = 2,
            Background          = new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A)),
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
        _readingOrderPaneHost = BuildReadingOrderPaneHost();
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
        Grid.SetColumn(slidePaneHost, 0);
        Grid.SetColumn(rightGrid,      1);
        Grid.SetColumn(_altTextPaneHost, 2);
        Grid.SetColumn(_readingOrderPaneHost, 3);
        body.Children.Add(slidePaneHost);
        body.Children.Add(rightGrid);
        body.Children.Add(_altTextPaneHost);
        body.Children.Add(_readingOrderPaneHost);

        return body;
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

    // ── Interaction wiring (Theme 15) ───────────────────────────────────────────

    private void WireInteraction(Canvas textOverlay)
    {
        if (_adorner is null) return;

        // Allow the canvas to receive keyboard focus for arrow/delete keys.
        _slideCanvas.Focusable = true;

        // Gesture handler drives selection, move, resize, rotate.
        _gestureHandler = new AvaloniaCanvasGestureHandler(_slideCanvas, Editor, _adorner);

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
            _textEditor     = new AvaloniaInCanvasTextEditor(_slideCanvas, Editor, textOverlay);
        }
    }

    // ── Ribbon ─────────────────────────────────────────────────────────────────

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
        r.Register(PresentationExportPlanner.ImageExportCommandId, new ActionRibbonCommand(() => _ = FileExportImagesAsync()));
        r.Register(PresentationExportPlanner.PrintCommandId, new ActionRibbonCommand(() => RefreshHandoutLayoutPlan()));

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

            Editor.SetFontFamilyOnSelection(ctx.SelectedValue);
        }));
        r.Register("freep.bold", new ActionRibbonCommand(() => Editor.ToggleBoldOnSelection()));
        r.Register("freep.italic", new ActionRibbonCommand(() => Editor.ToggleItalicOnSelection()));
        r.Register("freep.underline", new ActionRibbonCommand(() => Editor.ToggleUnderlineOnSelection()));

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

        // Undo / Redo
        r.Register("freep.undo", new ActionRibbonCommand(() => Editor.Undo()));
        r.Register("freep.redo", new ActionRibbonCommand(() => Editor.Redo()));
        r.Register("freep.find", new ActionRibbonCommand(OpenFindDialog));
        r.Register("freep.replace", new ActionRibbonCommand(OpenFindReplaceDialog));
        RegisterReviewWorkflowCommands(r);

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
        _ = plan;
        _ = SlideSizeDialogPlanner.BuildInitialState(
            _presentation.SlideSizeCxEmu,
            _presentation.SlideSizeCyEmu,
            SlideSizeDialogUnit.Inches);
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
        _layoutPickerHost.IsVisible = true;
    }

    private void HideLayoutPicker()
    {
        if (_layoutPickerHost is not null)
            _layoutPickerHost.IsVisible = false;
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

    internal void OpenHyperlinkDialog()
    {
        _ = HyperlinkDialogPlanner.BuildDialogRequest(
            Editor.Presentation.Slides,
            Editor.SelectedShapeHyperlink);
    }

    internal void OpenFindDialog() =>
        OpenFindReplaceDialog(showReplace: false);

    internal void OpenFindReplaceDialog() =>
        OpenFindReplaceDialog(showReplace: true);

    private void OpenFindReplaceDialog(bool showReplace)
    {
        _ = FindReplaceDialogPlanner.TitleForMode(showReplace);
    }

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
            ExportAtomicWriter.WriteAllBytes(path, PresentationPdfExporter.ExportToBytes(_presentation));
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

    private void RegisterReviewWorkflowCommands(RibbonCommandRegistry registry)
    {
        registry.Register(
            PresentationReviewWorkflowPlanner.CommentsPaneCommandId,
            new ActionRibbonCommand(ShowReviewCommentsPane));
        registry.Register(
            PresentationReviewWorkflowPlanner.AccessibilityCommandId,
            new ActionRibbonCommand(RefreshAccessibilitySummaryPlan));
        registry.Register(
            PresentationReviewWorkflowPlanner.AltTextCommandId,
            new ActionRibbonCommand(ShowAltTextPane));
        registry.Register(
            PresentationReviewWorkflowPlanner.ReadingOrderPaneCommandId,
            new ActionRibbonCommand(() => ShowReadingOrderPane()));
        registry.Register(
            PresentationReviewWorkflowPlanner.ProofingCommandId,
            new ActionRibbonCommand(RefreshProofingRequestPlan));
        registry.Register(PresentationReviewWorkflowPlanner.AddCommentCommandId, EmptyRibbonCommand.Instance);
        registry.Register(PresentationReviewWorkflowPlanner.EditCommentCommandId, EmptyRibbonCommand.Instance);
        registry.Register(PresentationReviewWorkflowPlanner.DeleteCommentCommandId, EmptyRibbonCommand.Instance);
        registry.Register(PresentationReviewWorkflowPlanner.PreviousCommentCommandId, EmptyRibbonCommand.Instance);
        registry.Register(PresentationReviewWorkflowPlanner.NextCommentCommandId, EmptyRibbonCommand.Instance);
        registry.Register(PresentationReviewWorkflowPlanner.ResolveCommentCommandId, EmptyRibbonCommand.Instance);
    }

    internal void RefreshReviewWorkflowPlans()
    {
        LastCommentPanePlan = PresentationReviewWorkflowPlanner.BuildCommentPanePlan(
            _presentation.Slides,
            Editor.CurrentSlideIndex);
        RefreshAccessibilitySummaryPlan();
        RefreshAltTextRequestPlan();
        RefreshReadingOrderPlan();
        RefreshProofingRequestPlan();
    }

    private void ShowReviewCommentsPane()
    {
        var plan = PresentationReviewWorkflowPlanner.BuildCommentPanePlan(
            _presentation.Slides,
            Editor.CurrentSlideIndex);
        LastCommentPanePlan = plan;
        ShowReviewCommentsPane(plan);
    }

    private void ShowReviewCommentsPane(PresentationCommentPanePlan plan)
    {
        if (_reviewCommentsPaneHost is null || _reviewCommentsPanePanel is null)
            return;

        _reviewCommentsPanePanel.Children.Clear();
        _reviewCommentsPanePanel.Children.Add(BuildReviewCommentsPaneHeader(plan));
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
            Text       = $"Comments - slide {plan.SlideIndex + 1} of {plan.SlideCount} ({plan.TotalCommentCount} total)",
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D)),
            Margin     = new Thickness(12, 10, 12, 2),
        };

    private static Control BuildReviewCommentActions(IReadOnlyList<PresentationReviewWorkflowActionPlan> actions)
    {
        var panel = new WrapPanel
        {
            Margin = new Thickness(12, 0, 12, 2),
        };

        foreach (var action in actions)
        {
            panel.Children.Add(new Button
            {
                Content   = action.Label,
                IsEnabled = action.IsEnabled,
                Tag       = action.CommandId,
                MinWidth  = 88,
                Margin    = new Thickness(0, 0, 6, 6),
            });
        }

        return panel;
    }

    private static Control BuildReviewCommentCard(PresentationCommentDescriptor comment)
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
                Text       = string.IsNullOrWhiteSpace(comment.Initials) ? "?" : comment.Initials,
                FontSize   = 11,
                Foreground = Brushes.White,
            },
        });
        header.Children.Add(new TextBlock
        {
            Text              = string.IsNullOrWhiteSpace(comment.Author) ? "Unknown reviewer" : comment.Author,
            FontWeight        = FontWeight.SemiBold,
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

        return new Border
        {
            Background      = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)),
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(4),
            Padding         = new Thickness(10),
            Margin          = new Thickness(12, 0, 12, 10),
            Child           = card,
        };
    }

    private void OnAnimationPaneRequested(PresentationAnimationCommandPlan plan)
    {
        _ = plan;
        RefreshAnimationPaneTimelinePlan();
    }

    internal AnimationPaneTimelinePlan RefreshAnimationPaneTimelinePlan(int selectedAnimationIndex = -1)
    {
        LastAnimationPaneTimelinePlan = AnimationPanePlanner.BuildTimelinePlan(
            Editor.CurrentSlide,
            Editor.SelectedShapeIds,
            selectedAnimationIndex);
        return LastAnimationPaneTimelinePlan;
    }

    private void RefreshAccessibilitySummaryPlan()
    {
        LastAccessibilitySummaryPlan =
            PresentationReviewWorkflowPlanner.BuildAccessibilitySummaryPlan(_presentation);
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

    private static Control BuildReadingOrderItemCard(PresentationReadingOrderItemPlan item)
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

        return new Border
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

    private void RefreshProofingRequestPlan()
    {
        LastProofingRequestPlan =
            PresentationReviewWorkflowPlanner.BuildProofingRequestPlan(_presentation);
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

            var entries = SlidePanePlanner.BuildEntries(_presentation.Slides, _presentation.Sections);
            foreach (var entry in entries)
            {
                if (entry.Kind == SlidePaneEntryKind.SectionHeader)
                {
                    _slidePaneList.Items.Add(BuildSlidePaneSectionHeader(entry));
                    continue;
                }

                var slideIdx = entry.SlideIndex;
                var slide    = _presentation.Slides[entry.SlideIndex];

                // Small SlideCanvas thumbnail using the shared slide pane metrics.
                var thumb = new SlideCanvas
                {
                    Presentation = _presentation,
                    Slide        = slide,
                    SlideIndex   = slideIdx,
                    Width        = SlidePanePlanner.DefaultThumbnailWidth,
                    Height       = SlidePanePlanner.DefaultThumbnailHeight,
                };

                // Slide number label beneath thumbnail.
                var label = new TextBlock
                {
                    Text                = entry.Text,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontSize            = 10,
                    Margin              = new Thickness(0, 2, 0, 0),
                };

                var panel = new StackPanel
                {
                    Margin   = new Thickness(4),
                    Children = { thumb, label },
                };

                var item = new ListBoxItem
                {
                    Tag         = entry.SlideIndex,
                    Content     = panel,
                    Padding     = new Thickness(2),
                    ContextMenu = BuildSlidePaneContextMenu(entry.SlideIndex),
                };
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

    private static ListBoxItem BuildSlidePaneSectionHeader(SlidePaneEntry entry)
    {
        var label = new TextBlock
        {
            Text              = entry.Text,
            FontSize          = 11,
            FontWeight        = FontWeight.SemiBold,
            Foreground        = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming      = TextTrimming.CharacterEllipsis,
        };

        return new ListBoxItem
        {
            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
                Padding    = new Thickness(10, 4),
                Child      = label,
            },
            Padding   = new Thickness(0),
            Margin    = new Thickness(0, 6, 0, 2),
            Focusable = false,
            IsEnabled = false,
        };
    }

    private ContextMenu BuildSlidePaneContextMenu(int slideIndex)
    {
        var menu = new ContextMenu();

        foreach (var action in SlidePanePlanner.BuildContextActions(_presentation.Slides.Count, slideIndex))
        {
            if (action.Kind == SlidePaneActionKind.DeleteSlide)
                menu.Items.Add(new Separator());

            var item = new MenuItem
            {
                Header = action.Text,
                IsEnabled = action.IsEnabled,
            };
            item.Click += (_, _) => SlidePanePlanner.TryApplyAction(Editor, action);
            menu.Items.Add(item);
        }

        return menu;
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
        if (!_slidePaneIsDragging && Math.Abs(itemPosition.Y - _slidePaneDragStartPoint.Y) < 5)
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
            SlidePaneAvaloniaSlideItemHeight);
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
        var indicatorY = SlidePanePlanner.ComputeInsertionIndicatorOffset(
            GetSlidePaneItemKinds(),
            _slidePaneDragTargetIndex,
            SlidePaneAvaloniaSlideItemHeight);

        _slidePaneInsertionIndicator.Margin = new Thickness(0, indicatorY - 1, 0, 0);
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
        UpdateStatus();
    }

    private void OnCurrentSlideChanged(object? sender, EventArgs e)
    {
        // Sync slide-pane selection without re-triggering OnSlidePaneSelectionChanged.
        _slidePaneRefreshing = true;
        try { SelectSlidePaneItem(Editor.CurrentSlideIndex); }
        finally { _slidePaneRefreshing = false; }

        RefreshCanvas();
        RefreshNotesPane();
        RefreshReviewWorkflowPlans();
        UpdateStatus();
    }

    private void OnEditorSelectionChanged(object? sender, EventArgs e)
    {
        RefreshAltTextRequestPlan();
        RefreshReadingOrderPlan();
        if (IsAltTextPaneVisible)
            ShowAltTextPane();
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
}
