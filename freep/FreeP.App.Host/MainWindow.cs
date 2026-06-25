using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Free.Shared.Ribbon.Wpf;
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
    private TextBlock _titleText = null!;
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
        _titleText = titleBar.TitleText;
        AddQuickAccessButtons(titleBar.QatHost);

        // Ribbon.
        // Wave 4C: pass slideshow Actions into the command registry.
        // StartSlideShow is defined by Wave 4B; if that branch has not yet merged,
        // the local stub below (see "4B-MERGE PLACEHOLDER") keeps this branch green.
        var stateStore = new RibbonStateStore();
        var commands = FreePRibbonCommands.Build(
            stateStore,
            Editor,
            onStartFromStart:   () => StartSlideShow(true),
            onStartFromCurrent: () => StartSlideShow(false));
        var ribbon = BuildRibbon(FreePRibbon.Build(), commands, stateStore);

        // Body: slide pane + stage.
        var body = BuildBody();

        // Status bar.
        var status = BuildStatusBar();
        var clientFrame = SisterAppClientFrameBuilder.Build(new SisterAppClientFrameSpec(ribbon, body, status));
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
        UpdateSlideCount();
    }

    // ── Editor construction ───────────────────────────────────────────────────────

    private void RebuildEditor()
    {
        var bus = new PresentationCommandBus(_presentation);
        Editor  = new EditingSession(_presentation, bus);

        Editor.Changed           += () => { _file.MarkDirty(); RefreshCanvas(); UpdateSlideCount(); UpdateTitle(); };
        Editor.CurrentSlideChanged += (_, _) => RefreshCanvas();
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

        // Wrap canvas + overlay in a Grid so the overlay occupies the same bounds.
        var stageGrid = new Grid();
        stageGrid.Children.Add(SlideCanvas);
        stageGrid.Children.Add(_textOverlay);

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

        var splitter = new Grid();
        splitter.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        splitter.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(SlidePaneHost, 0);
        Grid.SetColumn(_canvasHost,   1);
        splitter.Children.Add(SlidePaneHost);
        splitter.Children.Add(_canvasHost);

        return splitter;
    }

    // ── Canvas refresh ────────────────────────────────────────────────────────────

    private void RefreshCanvas()
    {
        SlideCanvas.Presentation = _presentation;
        SlideCanvas.Slide        = Editor.CurrentSlide;
        SlideCanvas.Refresh();
    }

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
        _slideCountText.Text = $"Slide {Editor.CurrentSlideIndex + 1} / {_presentation.Slides.Count}   {ResolveDataFolderLabel()}";

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
        var title = WindowTitlePlanner.Compose(
            displayName:    _file.DisplayName,
            applicationName: "FreeP",
            isDirty:         _file.IsDirty,
            dirtyMarker:     " *",
            separator:       " — ");
        Title           = title;
        _titleText.Text = title;
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

    // ── Slide Show (4B SEAM) ──────────────────────────────────────────────────────

    /// <summary>
    /// Launches the slideshow.
    ///
    /// 4B-MERGE PLACEHOLDER: Wave 4B owns the real implementation (SlideShowWindow etc.).
    /// This stub exists ONLY so the Wave 4C branch compiles independently. The orchestrator
    /// must remove this stub after merging 4B, which provides the real method.
    /// </summary>
    internal void StartSlideShow(bool fromStart)
    {
        // 4B-MERGE PLACEHOLDER — remove this stub after merging Wave 4B.
        _ = fromStart; // suppress unused-parameter warning
    }
}
