using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Free.Shared.Ribbon.Wpf;
using FreeP.App.Host.Backstage;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>
/// FreeP main window. Deliberately code-only and minimal: it exists to prove the shared tier is consumable by
/// a third sister app. The window is composed entirely from shared chrome — the <see cref="ShellChrome"/>
/// title bar, a shared <see cref="RibbonDefinition"/> ribbon rendered by the shared WPF renderer, the shared
/// <c>BackstageFrame</c> File screen, and a simple status bar — around a placeholder slide canvas (NOT a real
/// renderer). Mirrors FreeW.MainWindow's composition, swapping the Word document for the presentation stub.
/// </summary>
public sealed class MainWindow : Window
{
    // Identity/palette for the shared window shell (PowerPoint-style brick title bar; "P" badge).
    // Colors are resolved from the active theme tokens (FreePTitleBarBrush / FreePAccentDarkBrush)
    // registered by WpfThemeApplier at startup, with literal fallbacks so tests that construct
    // MainWindow without a running Application still work.
    // Values are BYTE-IDENTICAL to the previous literals when the default FreeP theme is active.
    private static ShellChromeOptions BuildChromeOptions() => new()
    {
        BadgeLetter = "P",
        TitleBarColor = ResolveTokenColor("FreePTitleBarBrush",   Color.FromRgb(0xB7, 0x47, 0x2A)),
        BadgeColor    = ResolveTokenColor("FreePAccentDarkBrush", Color.FromRgb(0x8F, 0x37, 0x21)),
        CaptionHeight = 34
    };

    /// <summary>
    /// Looks up a frozen <see cref="SolidColorBrush"/> registered by <see cref="WpfThemeApplier"/> in
    /// <see cref="Application.Current"/> and returns its <see cref="SolidColorBrush.Color"/>.
    /// Falls back to <paramref name="fallback"/> when no Application is running (e.g. unit tests) or the
    /// key is absent.
    /// </summary>
    private static Color ResolveTokenColor(string key, Color fallback)
    {
        if (System.Windows.Application.Current?.Resources[key] is SolidColorBrush brush)
            return brush.Color;
        return fallback;
    }

    /// <summary>
    /// Looks up a frozen <see cref="SolidColorBrush"/> registered by <see cref="WpfThemeApplier"/> in
    /// <see cref="Application.Current"/> and returns it, or <see langword="null"/> when absent/no Application.
    /// </summary>
    private static Brush? ResolveTokenBrush(string key)
    {
        if (System.Windows.Application.Current?.Resources[key] is Brush brush)
            return brush;
        return null;
    }

    private readonly FreePOptions _options;
    private readonly ApplicationOptionsStore<FreePOptions> _optionsStore;

    // The presentation model + its undo bus (shared command tier). The placeholder canvas re-renders from this.
    private Presentation _presentation = Presentation.CreateEmpty();
    private PresentationCommandBus _commandBus = null!;

    private FileCommands _file = null!;
    private BackstageView _backstage = null!;
    private Border _titleBar = null!;
    private TextBlock _titleText = null!;
    private TabControl _ribbonTabs = null!;
    private TabItem _fileTab = null!;
    private RibbonFileTabRouter? _fileTabRouter;
    private Border _canvasHost = null!;
    private SlideCanvas _slideCanvas = null!;
    private TextBlock _slideCountText = null!;

    public MainWindow() : this(new FreePOptions())
    {
    }

    public MainWindow(FreePOptions options, ApplicationOptionsStore<FreePOptions>? optionsStore = null)
    {
        _options = options ?? new FreePOptions();
        // No store supplied (tests / isolation) → a transient in-memory store so editing still works without
        // touching the real profile. Mirrors FreeW.
        _optionsStore = optionsStore ?? ApplicationOptionsStore<FreePOptions>.ForPath(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FreeP", "settings.transient.json"));

        Title = "FreeP";
        Width = 1280;
        Height = 760;
        WindowState = WindowState.Maximized;
        Background = ResolveTokenBrush("FreePSheetSurfaceBrush")
            ?? new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        // Borderless shared WindowChrome shell (custom title bar, window buttons, rounded corners).
        var chromeOptions = BuildChromeOptions();
        ShellChrome.ConfigureWindow(this, chromeOptions);

        _commandBus = new PresentationCommandBus(_presentation);
        _commandBus.Changed += () => { _file.MarkDirty(); RefreshCanvas(); UpdateSlideCount(); UpdateTitle(); };

        // File commands over the shared lifecycle planner + the .fxp adapter.
        _file = new FileCommands(this, () => _presentation, LoadModel, UpdateTitle, _options);

        // Title bar (shared shell): occupies its own OUTER-grid row above the Backstage overlay.
        var titleBar = ShellChrome.BuildTitleBar(this, chromeOptions);
        _titleBar = titleBar.Root;
        _titleText = titleBar.TitleText;
        AddQuickAccessButtons(titleBar.QatHost);

        // Ribbon (shared definition + shared WPF renderer).
        var stateStore = new RibbonStateStore();
        var commands = FreePRibbonCommands.Build(stateStore, NewSlide);
        var ribbon = BuildRibbon(FreePRibbon.Build(), commands, stateStore);

        // Placeholder slide canvas (NOT a real renderer): a grey "stage" with a centred white slide page.
        var body = BuildCanvas();

        // Status bar.
        var status = BuildStatusBar();
        var clientFrame = SisterAppClientFrameBuilder.Build(new SisterAppClientFrameSpec(ribbon, body, status));
        var root = clientFrame.Root;

        // File commands routed to keyboard shortcuts.
        CommandBindings.Add(new CommandBinding(ApplicationCommands.New, (_, _) => _file.New()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Open, (_, _) => _file.Open()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, (_, _) => _file.Save()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.SaveAs, (_, _) => _file.SaveAs()));

        Closing += (_, e) =>
        {
            if (!_file.ConfirmCloseAllowed())
                e.Cancel = true;
        };

        // Backstage (shared BackstageFrame), wired to the host's File commands.
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

        // Compose: title bar in its own top row, the body+backstage stacked below (File screen covers the body
        // but leaves the title bar visible — Office behaviour).
        var frame = SisterAppWindowFrameBuilder.Build(new SisterAppWindowFrameSpec(_titleBar, root, _backstage));
        Content = frame.Root;

        UpdateTitle();
        RefreshCanvas();
        UpdateSlideCount();
    }

    // Load a freshly opened/new model and rebind the undo bus to it.
    private void LoadModel(Presentation presentation)
    {
        _presentation = presentation;
        _commandBus = new PresentationCommandBus(_presentation);
        _commandBus.Changed += () => { _file.MarkDirty(); RefreshCanvas(); UpdateSlideCount(); UpdateTitle(); };
        RefreshCanvas();
        UpdateSlideCount();
    }

    // The one real edit in the scaffold: append a blank slide through the shared command bus (undoable).
    private void NewSlide()
    {
        var slide = new Slide { Title = $"Slide {_presentation.Slides.Count + 1}" };
        _commandBus.Execute(new AddSlideCommand(slide));
    }

    // Real slide canvas: a grey "stage" hosting the SlideCanvas renderer, which uses
    // SlideCompositor to convert the presentation model into WPF draw calls.
    private UIElement BuildCanvas()
    {
        _slideCanvas = new SlideCanvas
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        // Slide canvas hosted inside a viewbox so it scales uniformly with the window.
        var viewbox = new Viewbox
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.Both,
            Margin = new Thickness(40),
            Child = _slideCanvas
        };

        _canvasHost = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)),
            Child = viewbox
        };

        return _canvasHost;
    }

    private void RefreshCanvas()
    {
        var first = _presentation.Slides.Count > 0 ? _presentation.Slides[0] : null;
        _slideCanvas.Presentation = _presentation;
        _slideCanvas.Slide = first;
        _slideCanvas.Refresh();
    }

    private Border BuildStatusBar()
    {
        _slideCountText = SisterAppStatusBarChrome.CreateInfoText();

        return SisterAppStatusBarChrome.Build(new SisterAppStatusBarSpec(
            // Status bar surface routed through FreePStatusSurfaceBrush token (#B7472A default).
            ResolveTokenBrush("FreePStatusSurfaceBrush")
                ?? new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A)),
            _slideCountText,
            LeftMargin: new Thickness(12, 0, 0, 0))).Root;
    }

    private void UpdateSlideCount() =>
        _slideCountText.Text = $"Slides: {_presentation.Slides.Count}   Data folder: {ResolveDataFolderLabel()}";

    private void AddQuickAccessButtons(StackPanel host) =>
        SisterQuickAccessToolbarBuilder.Render(
            host,
            this,
            new SisterQuickAccessToolbarActions(
                Save: () => _file.Save(),
                Undo: () => _commandBus.Undo(),
                Redo: () => _commandBus.Redo()));

    private void UpdateTitle()
    {
        var title = WindowTitlePlanner.Compose(
            displayName: _file.DisplayName,
            applicationName: "FreeP",
            isDirty: _file.IsDirty,
            dirtyMarker: " *",
            separator: " — ");
        Title = title;
        _titleText.Text = title;
    }

    private void ShowBackstage() => _backstage.Show();

    private static string ResolveDataFolderLabel()
        => AppStoragePathPlanner.GetOptionsFilePathLabelOrFallback(PlatformApplicationDataPathProvider.LocalInstance);

    // --- Ribbon: a flat File + Home/Insert tab strip over the shared RibbonDefinition, rendered by the shared
    //     WPF renderer (the same renderer FreeX and FreeW use). The File pill opens the Backstage. ---
    private UIElement BuildRibbon(RibbonDefinition definition, IRibbonCommandRegistry registry, IRibbonStateStore stateStore)
    {
        FreePRibbonIcons.Install();

        var result = RibbonShellBuilder.Build(new RibbonShellBuildSpec(
            definition,
            registry,
            stateStore,
            FileTabHeader: "File",
            FileTabAccent: Color.FromRgb(0xB7, 0x47, 0x2A),
            FileTabHover: Color.FromRgb(0x8F, 0x37, 0x21),
            ShowBackstage));

        _ribbonTabs = result.Tabs;
        _fileTab = result.FileTab;
        _fileTabRouter = result.FileTabRouter;
        return result.Root;
    }

}
