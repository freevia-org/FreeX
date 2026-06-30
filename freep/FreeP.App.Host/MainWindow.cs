using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
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

    // Notes pane (Wave 7B)
    private TextBox _notesBox = null!;
    private bool _notesRefreshing;   // guard against re-entrant TextChanged → SetCurrentSlideNotesText

    // Comment indicator overlay + list pane (Wave 11B)
    private Canvas  _commentOverlay = null!;  // hosts speech-bubble dots over the slide canvas
    private StackPanel _commentListPanel = null!; // shows comment text list below canvas
    private Border  _commentListHost = null!; // collapsible container for _commentListPanel

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

    public MainWindow(FreePOptions options, ApplicationOptionsStore<FreePOptions>? optionsStore = null)
    {
        _options = options ?? new FreePOptions();
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
        _file = new FileCommands(this, () => _presentation, LoadModel, UpdateTitle, _options);

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
            // Wave 10B: OS-clipboard service for ribbon Copy/Cut/Paste buttons.
            osClipboard:        _osClipboard,
            // Wave 11A: Insert Hyperlink dialog.
            onInsertLink:       () => OpenHyperlinkDialog(),
            // Wave 12B: Find & Replace dialogs.
            onFind:             () => OpenFindDialog(),
            onFindReplace:      () => OpenFindReplaceDialog(),
            // Wave 16B: Animation pane toggle.
            onAnimPane:         () => ToggleAnimationPane());
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
            CurrentOptions: () => _options,
            OnClosed: () => { },
            DataFolder: ResolveDataFolderLabel));

        var frame = SisterAppWindowFrameBuilder.Build(new SisterAppWindowFrameSpec(_titleBar, root, _backstage));
        Content = frame.Root;

        UpdateTitle();
        RefreshCanvas();
        RefreshNotesPane();
        RefreshCommentPane();
        UpdateSlideCount();
    }

    // ── Editor construction ───────────────────────────────────────────────────────

    private void RebuildEditor()
    {
        var bus = new PresentationCommandBus(_presentation);
        Editor  = new EditingSession(_presentation, bus);

        Editor.Changed           += () => { _file.MarkDirty(); RefreshCanvas(); UpdateSlideCount(); UpdateTitle(); };
        Editor.CurrentSlideChanged += (_, _) => { RefreshCanvas(); RefreshNotesPane(); RefreshCommentPane(); };
        // SelectionChanged: 3C subscribes directly to Editor.SelectionChanged.

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
    }

    // ── File load ─────────────────────────────────────────────────────────────────

    private void LoadModel(Presentation presentation)
    {
        _presentation = presentation;
        RebuildEditor(); // also calls AttachCanvasEditing()
        // 3B: re-bind slide pane to the new Editor on file open/new.
        SlidePaneHost.Child = new SlidePane(Editor);
        RefreshCanvas();
        UpdateSlideCount();
        RefreshNotesPane();
        RefreshCommentPane();
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

        // Right-side panel: canvas on top, comment strip, notes strip below.
        var rightPanel = new Grid();
        rightPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rightPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rightPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_canvasHost,       0);
        Grid.SetRow(_commentListHost,  1);
        Grid.SetRow(_notesBox,         2);
        rightPanel.Children.Add(_canvasHost);
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

        var splitter = new Grid();
        splitter.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        splitter.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        splitter.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 16B: anim pane
        Grid.SetColumn(SlidePaneHost,  0);
        Grid.SetColumn(rightPanel,     1);
        Grid.SetColumn(_animPaneHost,  2); // 16B
        splitter.Children.Add(SlidePaneHost);
        splitter.Children.Add(rightPanel);
        splitter.Children.Add(_animPaneHost); // 16B

        return splitter;
    }

    // ── Canvas refresh ────────────────────────────────────────────────────────────

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

        var slide    = Editor.CurrentSlide;
        var comments = slide?.Comments ?? new List<FreeP.Core.Model.SlideComment>();

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
                        Text       = string.IsNullOrWhiteSpace(cm.Initials) ? "?" : cm.Initials,
                        FontSize   = 10,
                        Foreground = System.Windows.Media.Brushes.White,
                    }
                };
                var authorText = new TextBlock
                {
                    Text       = string.IsNullOrWhiteSpace(cm.Author) ? "(unknown)" : cm.Author,
                    FontSize   = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                headerPanel.Children.Add(badge);
                headerPanel.Children.Add(authorText);

                // Comment body text
                var bodyText = new TextBlock
                {
                    Text         = cm.Text,
                    FontSize     = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground   = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                    Margin       = new Thickness(16, 2, 6, 6),
                };

                _commentListPanel.Children.Add(headerPanel);
                _commentListPanel.Children.Add(bodyText);
            }
            _commentListHost.Visibility = Visibility.Visible;
        }
        else
        {
            _commentListHost.Visibility = Visibility.Collapsed;
        }
    }

    private void OnCommentOverlayLoaded(object sender, RoutedEventArgs e)
    {
        _commentOverlay.Loaded -= OnCommentOverlayLoaded;
        var slide    = Editor.CurrentSlide;
        var comments = slide?.Comments ?? new List<FreeP.Core.Model.SlideComment>();
        DrawCommentDots(comments);
    }

    /// <summary>
    /// Paints speech-bubble dot markers on <see cref="_commentOverlay"/> for each comment.
    /// Positions are derived from the comment's EMU coordinates mapped into the overlay bounds,
    /// accounting for SlideCanvas's 40 px margin on each side.
    /// </summary>
    private void DrawCommentDots(IReadOnlyList<FreeP.Core.Model.SlideComment> comments)
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
                Width           = 14,
                Height          = 14,
                CornerRadius    = new CornerRadius(7),
                Background      = new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A)),
                BorderBrush     = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(1.5),
                ToolTip         = $"{cm.Author}: {cm.Text}",
            };

            Canvas.SetLeft(dot, cx - 7);
            Canvas.SetTop(dot,  cy - 7);
            _commentOverlay.Children.Add(dot);
        }
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

        int startIndex = fromStart ? 0 : Editor.CurrentSlideIndex;
        var window = new SlideShowWindow(_presentation, startIndex);
        // Owner can only be set when the main window is already shown (not during unit tests).
        if (IsVisible)
            window.Owner = this;
        window.Show();
    }

    // ── Chart data editing (Wave 9B) ──────────────────────────────────────────────

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
