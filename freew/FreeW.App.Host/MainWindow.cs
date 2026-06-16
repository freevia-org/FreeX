using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// FreeW main window. Deliberately code-only and minimal: it exists to prove the shared tier is
/// consumable by a second app. The ribbon is built from the shared <see cref="RibbonDefinition"/>
/// model and rendered by a small local renderer; the status bar shows that the shared storage
/// helpers resolve FreeW's own data folder (because Program.Main set AppProduct = "FreeW").
/// </summary>
public sealed class MainWindow : Window
{
    private FileCommands _file = null!;
    private AutosaveCoordinator _autosave = null!;
    private DocumentView _editor = null!;
    private TextBlock _titleText = null!;
    private FindReplaceDialog? _findDialog;

    public MainWindow()
    {
        Title = "FreeW";
        Width = 1040;
        Height = 720;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        var root = new DockPanel();

        var editor = new DocumentView { Margin = new Thickness(40, 24, 40, 24) };
        _editor = editor;
        editor.LoadModel(CreateSampleDocument());
        var stateStore = new RibbonStateStore();
        var commands = FreeWRibbonCommands.Build(editor, stateStore);
        _file = new FileCommands(this, editor, UpdateTitle);
        editor.TextChanged += (_, _) => _file.MarkDirty();
        _autosave = new AutosaveCoordinator(editor, _file);
        Loaded += (_, _) => { _autosave.OfferRecovery(this); _autosave.Start(); };
        Closing += (_, _) => _autosave.Stop();

        var titleBar = BuildTitleBar();
        DockPanel.SetDock(titleBar, Dock.Top);
        root.Children.Add(titleBar);

        var ribbon = BuildRibbon(FreeWRibbon.Build(), commands, stateStore);
        DockPanel.SetDock(ribbon, Dock.Top);
        root.Children.Add(ribbon);

        var status = new StatusBar();
        status.Items.Add(new StatusBarItem { Content = $"Data folder: {ResolveDataFolderLabel()}" });
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(status);

        root.Children.Add(editor);

        CommandBindings.Add(new CommandBinding(ApplicationCommands.New, (_, _) => _file.New()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Open, (_, _) => _file.Open()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, (_, _) => _file.Save()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.SaveAs, (_, _) => _file.SaveAs()));

        CommandBindings.Add(new CommandBinding(ApplicationCommands.Print, (_, _) => Print()));

        var findReplace = new RoutedUICommand("Find & Replace", "FindReplace", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(findReplace, (_, _) => OpenFindReplace()));
        InputBindings.Add(new KeyBinding(findReplace, new KeyGesture(Key.F, ModifierKeys.Control)));
        InputBindings.Add(new KeyBinding(findReplace, new KeyGesture(Key.H, ModifierKeys.Control)));

        UpdateTitle();

        Content = root;
    }

    private Border BuildTitleBar()
    {
        static Button FileButton(string label, System.Windows.Input.RoutedUICommand command) => new()
        {
            Content = label,
            Margin = new Thickness(0, 0, 6, 0),
            Padding = new Thickness(10, 2, 10, 2),
            Command = command
        };

        var bar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        bar.Children.Add(FileButton("New", ApplicationCommands.New));
        bar.Children.Add(FileButton("Open", ApplicationCommands.Open));
        bar.Children.Add(FileButton("Save", ApplicationCommands.Save));

        var recentButton = new Button { Content = "Recent ▾", Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(10, 2, 10, 2) };
        recentButton.Click += (_, _) => ShowRecentMenu(recentButton);
        bar.Children.Add(recentButton);

        _titleText = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        bar.Children.Add(_titleText);

        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x57, 0x9A)),
            Padding = new Thickness(12, 6, 12, 6),
            Child = bar
        };
    }

    private void UpdateTitle()
    {
        var name = _file.DisplayName + (_file.IsDirty ? " *" : "");
        Title = $"{name} — FreeW";
        _titleText.Text = $"{name} — FreeW";
    }

    private void Print()
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true)
            return;

        var doc = _editor.Document;
        var saved = (doc.PageWidth, doc.PageHeight, doc.PagePadding, doc.ColumnWidth);
        try
        {
            _editor.CommitToModel();
            doc.PageWidth = dialog.PrintableAreaWidth;
            doc.PageHeight = dialog.PrintableAreaHeight;
            doc.PagePadding = new Thickness(60);
            doc.ColumnWidth = double.PositiveInfinity;
            var paginator = ((System.Windows.Documents.IDocumentPaginatorSource)doc).DocumentPaginator;
            dialog.PrintDocument(paginator, "FreeW Document");
        }
        finally
        {
            (doc.PageWidth, doc.PageHeight, doc.PagePadding, doc.ColumnWidth) = saved;
        }
    }

    private void OpenFindReplace()
    {
        if (_findDialog is null)
        {
            _findDialog = new FindReplaceDialog(this, _editor);
            _findDialog.Closed += (_, _) => _findDialog = null;
        }
        _findDialog.Show();
        _findDialog.Activate();
    }

    private void ShowRecentMenu(Button anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        var entries = _file.RecentEntries;
        if (entries.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "(no recent files)", IsEnabled = false });
        }
        else
        {
            foreach (var entry in entries.Take(15))
            {
                var path = entry.Path;
                var item = new MenuItem { Header = System.IO.Path.GetFileName(path), ToolTip = path };
                item.Click += (_, _) => _file.OpenPath(path);
                menu.Items.Add(item);
            }
        }
        menu.IsOpen = true;
    }

    // Shows that AppProduct = "FreeW" routes the shared storage helpers to FreeW's own folder.
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

    // A sample document that exercises the model's styles + run/paragraph formatting.
    private static TextDocument CreateSampleDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Paragraphs.Clear();

        doc.Paragraphs.Add(new Paragraph("Welcome to FreeW") { StyleId = "Title" });
        doc.Paragraphs.Add(new Paragraph("A free word processor") { StyleId = "Heading1" });

        var intro = new Paragraph();
        intro.Runs.Add(new Run("This document is rendered from the FreeW model. Formatting like "));
        intro.Runs.Add(new Run("bold", new RunFormatting { Bold = true }));
        intro.Runs.Add(new Run(", "));
        intro.Runs.Add(new Run("italic", new RunFormatting { Italic = true }));
        intro.Runs.Add(new Run(", "));
        intro.Runs.Add(new Run("underline", new RunFormatting { Underline = true }));
        intro.Runs.Add(new Run(" and "));
        intro.Runs.Add(new Run("colour", new RunFormatting { ColorHex = "#C0504D", Bold = true }));
        intro.Runs.Add(new Run(" resolves through styles and document defaults. Edit freely — the surface is a live RichTextBox; CommitToModel() maps your edits back."));
        doc.Paragraphs.Add(intro);

        doc.Paragraphs.Add(new Paragraph("Centered paragraph.")
        {
            Formatting = ParagraphFormatting.Default with { Alignment = FreeW.Core.Model.TextAlignment.Center }
        });

        return doc;
    }

    // --- Minimal ribbon renderer over the shared RibbonDefinition model ---

    private static UIElement BuildRibbon(RibbonDefinition definition, IRibbonCommandRegistry registry, IRibbonStateStore stateStore)
    {
        var tabs = new TabControl
        {
            Background = Brushes.White,
            BorderThickness = new Thickness(0),
            MinHeight = 116
        };

        foreach (var tab in definition.Tabs)
            tabs.Items.Add(new TabItem { Header = tab.Header, Content = BuildTab(tab, registry, stateStore) });

        if (tabs.Items.Count > 0)
            tabs.SelectedIndex = 0;

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = tabs
        };
    }

    private static UIElement BuildTab(RibbonTab tab, IRibbonCommandRegistry registry, IRibbonStateStore stateStore)
    {
        var lane = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(6, 6, 6, 4)
        };

        foreach (var group in tab.Groups)
            lane.Children.Add(BuildGroup(group, registry, stateStore));

        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = lane
        };
    }

    private static UIElement BuildGroup(RibbonGroup group, IRibbonCommandRegistry registry, IRibbonStateStore stateStore)
    {
        var controls = new WrapPanel { MaxWidth = 220, Margin = new Thickness(4, 2, 4, 2) };
        foreach (var control in group.Controls)
        {
            var element = BuildControl(control, registry, stateStore);
            if (element is not null)
                controls.Children.Add(element);
        }

        var header = new TextBlock
        {
            Text = group.Header,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0)
        };

        var stack = new StackPanel();
        stack.Children.Add(controls);
        stack.Children.Add(header);

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE2, 0xE2)),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(6, 4, 6, 2),
            Child = stack
        };
    }

    private static UIElement? BuildControl(RibbonControl control, IRibbonCommandRegistry registry, IRibbonStateStore stateStore)
    {
        if (control is RibbonSeparator or RibbonRowBreak)
            return null;

        var thickness = new Thickness(2);
        var padding = new Thickness(8, 4, 8, 4);
        registry.TryGet(control.CommandId, out var command);

        void Execute() => command?.Execute(RibbonCommandContext.Empty);

        if (control is RibbonComboBox combo)
        {
            var box = new ComboBox
            {
                IsEditable = true,
                MinWidth = combo.Width ?? 100,
                Margin = thickness,
                IsEnabled = command is not null
            };
            foreach (var item in combo.Items)
                box.Items.Add(item);

            void Apply(string? value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    command?.Execute(new RibbonCommandContext(new System.Collections.Generic.Dictionary<string, object?> { ["value"] = value }));
            }
            box.SelectionChanged += (_, _) => Apply(box.SelectedItem as string);
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Apply(box.Text); };
            return box;
        }

        if (control is RibbonToggleButton)
        {
            var id = control.CommandId;
            var toggle = new ToggleButton { Content = control.Label, Margin = thickness, Padding = padding, MinWidth = 60 };
            if (command is IRibbonStatefulCommand stateful)
                toggle.IsChecked = stateful.GetState().IsChecked;
            // Observe the shared state store so the toggle reflects the current selection live.
            stateStore.StateChanged += (_, e) =>
            {
                if (e.Id == id)
                    toggle.IsChecked = e.State.IsChecked;
            };
            toggle.Click += (_, _) => Execute();
            toggle.IsEnabled = command is not null;
            return toggle;
        }

        var button = new Button { Content = control.Label, Margin = thickness, Padding = padding, MinWidth = 60 };
        button.Click += (_, _) => Execute();
        button.IsEnabled = command is not null;
        return button;
    }
}
