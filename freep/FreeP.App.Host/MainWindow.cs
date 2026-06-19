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
    private static readonly ShellChromeOptions ChromeOptions = new()
    {
        BadgeLetter = "P",
        TitleBarColor = Color.FromRgb(0xB7, 0x47, 0x2A),
        BadgeColor = Color.FromRgb(0x8F, 0x37, 0x21),
        CaptionHeight = 34
    };

    private readonly FreePOptions _options;
    private readonly FreePOptionsStore _optionsStore;

    // The presentation model + its undo bus (shared command tier). The placeholder canvas re-renders from this.
    private Presentation _presentation = Presentation.CreateEmpty();
    private PresentationCommandBus _commandBus = null!;

    private FileCommands _file = null!;
    private BackstageView _backstage = null!;
    private Border _titleBar = null!;
    private TextBlock _titleText = null!;
    private TabControl _ribbonTabs = null!;
    private TabItem _fileTab = null!;
    private int _lastRibbonTabIndex = 1;
    private bool _suppressFileTabRevert;
    private Border _canvas = null!;
    private TextBlock _canvasLabel = null!;
    private TextBlock _slideCountText = null!;

    public MainWindow() : this(new FreePOptions())
    {
    }

    public MainWindow(FreePOptions options, FreePOptionsStore? optionsStore = null)
    {
        _options = options ?? new FreePOptions();
        // No store supplied (tests / isolation) → a transient in-memory store so editing still works without
        // touching the real profile. Mirrors FreeW.
        _optionsStore = optionsStore ?? FreePOptionsStore.ForPath(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FreeP", "settings.transient.json"));

        Title = "FreeP";
        Width = 1280;
        Height = 760;
        WindowState = WindowState.Maximized;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        // Borderless shared WindowChrome shell (custom title bar, window buttons, rounded corners).
        ShellChrome.ConfigureWindow(this, ChromeOptions);

        _commandBus = new PresentationCommandBus(_presentation);
        _commandBus.Changed += () => { _file.MarkDirty(); RefreshCanvas(); UpdateSlideCount(); UpdateTitle(); };

        // Root layout: row 0 = ribbon (the title bar lives in the OUTER grid), row 1 = body (canvas),
        // row 2 = status bar.
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                     // ribbon
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // body
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                     // status bar

        // File commands over the shared lifecycle planner + the .fxp adapter.
        _file = new FileCommands(this, () => _presentation, LoadModel, UpdateTitle, _options);

        // Title bar (shared shell): occupies its own OUTER-grid row above the Backstage overlay.
        var titleBar = ShellChrome.BuildTitleBar(this, ChromeOptions);
        _titleBar = titleBar.Root;
        _titleText = titleBar.TitleText;
        AddQuickAccessButtons(titleBar.QatHost);

        // Ribbon (shared definition + shared WPF renderer).
        var stateStore = new RibbonStateStore();
        var commands = FreePRibbonCommands.Build(stateStore, NewSlide);
        var ribbon = BuildRibbon(FreePRibbon.Build(), commands, stateStore);
        Grid.SetRow(ribbon, 0);
        root.Children.Add(ribbon);

        // Placeholder slide canvas (NOT a real renderer): a grey "stage" with a centred white slide page.
        var body = BuildCanvas();
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        // Status bar.
        var status = BuildStatusBar();
        Grid.SetRow(status, 2);
        root.Children.Add(status);

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
            CurrentOptions: () => _options,
            OnClosed: () => { },
            DataFolder: ResolveDataFolderLabel));

        // Compose: title bar in its own top row, the body+backstage stacked below (File screen covers the body
        // but leaves the title bar visible — Office behaviour).
        var belowTitle = new Grid();
        belowTitle.Children.Add(root);
        belowTitle.Children.Add(_backstage);

        var outer = new Grid();
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_titleBar, 0);
        outer.Children.Add(_titleBar);
        Grid.SetRow(belowTitle, 1);
        outer.Children.Add(belowTitle);
        Content = outer;

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

    // Placeholder slide canvas: a grey "stage" hosting a centred white 16:9 slide page labelled with the
    // current slide's title. There is NO slide rendering here — this is chrome standing in for the real
    // editor the presentation-domain session will build.
    private UIElement BuildCanvas()
    {
        _canvasLabel = new TextBlock
        {
            FontSize = 28,
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var page = new Border
        {
            Width = 720,
            Height = 405, // 16:9
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            BorderThickness = new Thickness(1),
            Child = _canvasLabel
        };

        _canvas = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)),
            Child = new Viewbox
            {
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                Margin = new Thickness(40),
                Child = page
            }
        };
        return _canvas;
    }

    private void RefreshCanvas()
    {
        var first = _presentation.Slides.Count > 0 ? _presentation.Slides[0] : null;
        _canvasLabel.Text = first is null ? "No slides" : (string.IsNullOrWhiteSpace(first.Title) ? "Slide 1" : first.Title);
    }

    private Border BuildStatusBar()
    {
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _slideCountText = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        Grid.SetColumn(_slideCountText, 0);
        grid.Children.Add(_slideCountText);

        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A)),
            MinHeight = 26,
            Child = grid
        };
    }

    private void UpdateSlideCount() =>
        _slideCountText.Text = $"Slides: {_presentation.Slides.Count}   Data folder: {ResolveDataFolderLabel()}";

    private void AddQuickAccessButtons(StackPanel host)
    {
        var items = new[]
        {
            new QuickAccessToolbarItem("Save", "Save (Ctrl+S)", RibbonCommandIconKind.Save),
            new QuickAccessToolbarItem("Undo", "Undo (Ctrl+Z)", RibbonCommandIconKind.Undo),
            new QuickAccessToolbarItem("Redo", "Redo (Ctrl+Y)", RibbonCommandIconKind.Redo)
        };

        QuickAccessToolbarRenderer.Render(host, this, items, OnQuickAccessCommand);
    }

    private void OnQuickAccessCommand(string commandId)
    {
        switch (commandId)
        {
            case "Save":
                _file.Save();
                break;
            case "Undo":
                _commandBus.Undo();
                break;
            case "Redo":
                _commandBus.Redo();
                break;
        }
    }

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
    {
        try
        {
            return AppStoragePathPlanner.GetOptionsFilePath(PlatformApplicationDataPathProvider.LocalInstance);
        }
        catch
        {
            return $"%LOCALAPPDATA%\\{AppProduct.Current.ProductDirectoryName}";
        }
    }

    // --- Ribbon: a flat File + Home/Insert tab strip over the shared RibbonDefinition, rendered by the shared
    //     WPF renderer (the same renderer FreeX and FreeW use). The File pill opens the Backstage. ---
    private UIElement BuildRibbon(RibbonDefinition definition, IRibbonCommandRegistry registry, IRibbonStateStore stateStore)
    {
        FreePRibbonIcons.Install();

        var tabs = RibbonTabControlFactory.Create();

        // File tab (the FIRST tab): an accent pill that opens the Backstage rather than swapping the body.
        _fileTab = new TabItem
        {
            Header = "File",
            Style = BuildFileTabStyle(),
            Content = null
        };
        tabs.Items.Add(_fileTab);

        foreach (var tab in definition.Tabs)
        {
            var content = RibbonWpfRenderer.BuildTabContent(tab, tabs, registry, stateStore);
            tabs.Items.Add(new TabItem { Header = tab.Header, Content = content });
        }

        if (tabs.Items.Count > 1)
        {
            tabs.SelectedIndex = 1;
            _lastRibbonTabIndex = 1;
        }

        tabs.SelectionChanged += (_, e) =>
        {
            if (!ReferenceEquals(e.OriginalSource, tabs))
                return;
            if (_suppressFileTabRevert)
                return;

            if (ReferenceEquals(tabs.SelectedItem, _fileTab))
            {
                _suppressFileTabRevert = true;
                tabs.SelectedIndex = _lastRibbonTabIndex;
                _suppressFileTabRevert = false;
                ShowBackstage();
            }
            else
            {
                _lastRibbonTabIndex = tabs.SelectedIndex;
            }
        };

        _ribbonTabs = tabs;

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = tabs
        };
    }

    private static Style BuildFileTabStyle()
    {
        var accent = Freeze(Color.FromRgb(0xB7, 0x47, 0x2A));
        var accentHover = Freeze(Color.FromRgb(0x8F, 0x37, 0x21));

        var style = new Style(typeof(TabItem));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(16, 6, 16, 6)));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 2, 0)));
        style.Setters.Add(new Setter(UIElement.FocusableProperty, true));

        var border = new FrameworkElementFactory(typeof(Border), "FileTabBorder");
        border.SetValue(Border.BackgroundProperty, accent);
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        border.SetValue(FrameworkElement.CursorProperty, Cursors.Hand);

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);

        var template = new ControlTemplate(typeof(TabItem)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, accentHover, "FileTabBorder"));
        template.Triggers.Add(hover);
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }

    private static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
