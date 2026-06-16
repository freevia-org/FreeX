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
    private TextBlock _countsText = null!;
    private Slider _zoomSlider = null!;
    private TextBlock _zoomLabel = null!;
    private FindReplaceDialog? _findDialog;
    private RibbonStateStore _stateStore = null!;
    private Border _navPane = null!;
    private ListBox _navList = null!;
    private bool _navPaneVisible;

    // Read mode (distraction-free view) chrome we hide/restore, plus the saved presentation we restore.
    private Border _titleBar = null!;
    private UIElement _ribbon = null!;
    private StatusBar _status = null!;
    private StatusBarItem _dataFolderItem = null!;
    private StatusBarItem _zoomItem = null!;
    private bool _readMode;
    private bool _navPaneVisibleBeforeReadMode;
    private Thickness _editorMarginBeforeReadMode;
    private double _editorMaxWidthBeforeReadMode = double.PositiveInfinity;
    private HorizontalAlignment _editorAlignmentBeforeReadMode = HorizontalAlignment.Stretch;

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
        _stateStore = stateStore;
        var commands = FreeWRibbonCommands.Build(
            editor, stateStore, OpenPrintPreview, ToggleNavPane, () => _navPaneVisible, ToggleReadMode, () => _readMode);
        _file = new FileCommands(this, editor, UpdateTitle);
        editor.TextChanged += (_, _) => { _file.MarkDirty(); UpdateCounts(); RefreshOutline(); };
        // Live selection stats: when the caret/selection moves, refresh the status-bar counts so a
        // non-empty selection shows its own word/character totals (and reverts when nothing is selected).
        editor.SelectionChanged += (_, _) => UpdateCounts();
        _autosave = new AutosaveCoordinator(editor, _file);
        Loaded += (_, _) => { _autosave.OfferRecovery(this); _autosave.Start(); };
        Closing += (_, _) => _autosave.Stop();

        var titleBar = BuildTitleBar();
        _titleBar = titleBar;
        DockPanel.SetDock(titleBar, Dock.Top);
        root.Children.Add(titleBar);

        var ribbon = BuildRibbon(FreeWRibbon.Build(), commands, stateStore);
        _ribbon = ribbon;
        DockPanel.SetDock(ribbon, Dock.Top);
        root.Children.Add(ribbon);

        var status = new StatusBar();
        _status = status;
        _countsText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        status.Items.Add(new StatusBarItem { Content = _countsText });
        status.Items.Add(new Separator());
        _dataFolderItem = new StatusBarItem { Content = $"Data folder: {ResolveDataFolderLabel()}" };
        status.Items.Add(_dataFolderItem);
        _zoomItem = new StatusBarItem { HorizontalAlignment = HorizontalAlignment.Right, Content = BuildZoomControl() };
        status.Items.Add(_zoomItem);
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(status);

        var navPane = BuildNavPane();
        DockPanel.SetDock(navPane, Dock.Left);
        root.Children.Add(navPane);

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
        UpdateCounts();
        RefreshOutline();

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

        var propertiesButton = new Button { Content = "Properties", Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(10, 2, 10, 2) };
        propertiesButton.Click += (_, _) => OpenProperties();
        bar.Children.Add(propertiesButton);

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

    // Recompute the live status-bar counts. When there is a non-empty selection, show that selection's
    // word + character totals (via the pure WordCount helpers over Selection.Text); otherwise fall back
    // to the whole-document word/character/paragraph counts. Cheap enough to run on every edit
    // (TextChanged), on selection change, and on document load.
    private void UpdateCounts()
    {
        var selectionText = _editor.Selection.Text;
        if (!string.IsNullOrEmpty(selectionText))
        {
            var words = WordCount.Words(selectionText);
            var characters = WordCount.Characters(selectionText, includeSpaces: true);
            _countsText.Text = $"Selection: {words} words, {characters} characters";
            return;
        }

        _editor.CommitToModel();
        var stats = WordCount.Of(_editor.Model);
        _countsText.Text = $"Words: {stats.Words}   Characters: {stats.CharactersWithSpaces}   Paragraphs: {stats.Paragraphs}";
    }

    // The left navigation pane: a header plus a ListBox of heading outline entries (indented by level).
    // Collapsed by default; ToggleNavPane shows/hides it. Selecting an entry scrolls that heading into
    // view in the editor and moves the caret there (see RefreshOutline / OnOutlineSelected).
    private UIElement BuildNavPane()
    {
        _navList = new ListBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent
        };
        _navList.SelectionChanged += OnOutlineSelected;

        var header = new TextBlock
        {
            Text = "Navigation",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(10, 8, 10, 6)
        };

        var layout = new DockPanel { Width = 240 };
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(_navList);

        _navPane = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Visibility = Visibility.Collapsed,
            Child = layout
        };
        return _navPane;
    }

    // Show/hide the navigation pane and push the new checked-state into the ribbon state store so the
    // View > Navigation Pane toggle button stays in sync. Refreshes the outline when the pane appears.
    private void ToggleNavPane()
    {
        _navPaneVisible = !_navPaneVisible;
        _navPane.Visibility = _navPaneVisible ? Visibility.Visible : Visibility.Collapsed;
        _stateStore.SetChecked("freew.nav-pane", _navPaneVisible);
        if (_navPaneVisible)
            RefreshOutline();
    }

    // Read mode (distraction-free view): hide the ribbon, title bar, navigation pane, and the status
    // bar's non-essential extras (data folder + zoom), then constrain the editor to a centered, roomy
    // reading column. Toggling off restores every hidden element to exactly what it was before (including
    // whether the nav pane had been open) and returns the editor to its original full-width presentation.
    // The toggle state is mirrored into the shared RibbonStateStore so the View > Read Mode button stays
    // in sync, exactly like the navigation-pane toggle.
    private void ToggleReadMode()
    {
        _readMode = !_readMode;
        if (_readMode)
        {
            // Remember the normal layout so we can put it back verbatim when read mode is switched off.
            _navPaneVisibleBeforeReadMode = _navPaneVisible;
            _editorMarginBeforeReadMode = _editor.Margin;
            _editorMaxWidthBeforeReadMode = _editor.MaxWidth;
            _editorAlignmentBeforeReadMode = _editor.HorizontalAlignment;

            _titleBar.Visibility = Visibility.Collapsed;
            _ribbon.Visibility = Visibility.Collapsed;
            _dataFolderItem.Visibility = Visibility.Collapsed;
            _zoomItem.Visibility = Visibility.Collapsed;

            // Collapse the navigation pane while reading (without disturbing its remembered state).
            _navPane.Visibility = Visibility.Collapsed;

            // A centered, comfortable reading column: cap the width and add generous breathing room.
            _editor.HorizontalAlignment = HorizontalAlignment.Center;
            _editor.MaxWidth = 760;
            _editor.Margin = new Thickness(40, 40, 40, 40);
        }
        else
        {
            _titleBar.Visibility = Visibility.Visible;
            _ribbon.Visibility = Visibility.Visible;
            _dataFolderItem.Visibility = Visibility.Visible;
            _zoomItem.Visibility = Visibility.Visible;

            // Restore the editor's original full-width presentation.
            _editor.HorizontalAlignment = _editorAlignmentBeforeReadMode;
            _editor.MaxWidth = _editorMaxWidthBeforeReadMode;
            _editor.Margin = _editorMarginBeforeReadMode;

            // Restore the navigation pane to whatever it was before entering read mode.
            _navPane.Visibility = _navPaneVisibleBeforeReadMode ? Visibility.Visible : Visibility.Collapsed;
        }

        _stateStore.SetChecked("freew.read-mode", _readMode);
    }

    // Recompute the heading outline from the editor's committed model and repopulate the nav list.
    // Cheap, and skipped entirely while the pane is hidden. Each list item carries its OutlineEntry so
    // a selection can map straight back to the model block index.
    private void RefreshOutline()
    {
        if (_navList is null || !_navPaneVisible)
            return;

        _editor.CommitToModel();
        var outline = DocumentOutline.Of(_editor.Model);

        // Repopulate without triggering a navigation jump from the resulting selection reset.
        _navList.SelectionChanged -= OnOutlineSelected;
        _navList.Items.Clear();
        foreach (var entry in outline)
            _navList.Items.Add(new OutlineItem(entry));
        _navList.SelectionChanged += OnOutlineSelected;
    }

    // Clicking an outline entry scrolls the matching heading into view and moves the caret there by
    // mapping the entry's model block index onto the editor's FlowDocument (DocumentView.BringBlockIntoView).
    private void OnOutlineSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_navList.SelectedItem is OutlineItem item)
            _editor.BringBlockIntoView(item.Entry.BlockIndex);
    }

    // A nav-list row: indents the heading text by its outline level and remembers the source entry
    // (so a click can map back to the model block index). ToString drives the default ListBox display.
    private sealed class OutlineItem(OutlineEntry entry)
    {
        public OutlineEntry Entry { get; } = entry;

        public override string ToString()
        {
            var text = Entry.Text.Length > 0 ? Entry.Text : "(untitled)";
            return new string(' ', Entry.Level * 4) + text;
        }
    }

    // A status-bar zoom control: a [-] button, a 50%..200% slider, a [+] button, and a live percentage
    // label. All three drive DocumentView.ZoomLevel; ZoomChanged feeds the slider/label back so the
    // control stays in sync with other zoom sources (e.g. Ctrl+MouseWheel in the editor).
    private UIElement BuildZoomControl()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        Button ZoomButton(string label, Action onClick)
        {
            var button = new Button { Content = label, Width = 22, Padding = new Thickness(0), Margin = new Thickness(2, 0, 2, 0) };
            button.Click += (_, _) => onClick();
            return button;
        }

        _zoomSlider = new Slider
        {
            Minimum = ZoomLevels.Min,
            Maximum = ZoomLevels.Max,
            Value = _editor.ZoomLevel,
            Width = 120,
            VerticalAlignment = VerticalAlignment.Center,
            TickFrequency = ZoomLevels.Step,
            SmallChange = ZoomLevels.Step,
            LargeChange = ZoomLevels.Step,
            ToolTip = "Zoom"
        };
        _zoomSlider.ValueChanged += (_, e) => _editor.ZoomLevel = e.NewValue;

        _zoomLabel = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 2, 0),
            MinWidth = 38,
            TextAlignment = System.Windows.TextAlignment.Right,
            Text = $"{ZoomLevels.ToPercent(_editor.ZoomLevel)}%"
        };

        // Keep the slider + label in sync no matter how zoom changes (buttons, wheel, or the slider itself).
        _editor.ZoomChanged += (_, factor) =>
        {
            _zoomSlider.Value = factor;
            _zoomLabel.Text = $"{ZoomLevels.ToPercent(factor)}%";
        };

        panel.Children.Add(ZoomButton("−", () => _editor.ZoomLevel = ZoomLevels.StepDown(_editor.ZoomLevel)));
        panel.Children.Add(_zoomSlider);
        panel.Children.Add(ZoomButton("+", () => _editor.ZoomLevel = ZoomLevels.StepUp(_editor.ZoomLevel)));
        panel.Children.Add(_zoomLabel);
        return panel;
    }

    private void Print()
    {
        var dialog = new PrintDialog();

        // Print at the model's page size (points -> DIP), not just the printer's printable area, so
        // margins and page breaks match what the user sees in Print Preview.
        var page = _editor.Model.Page;
        var (pageWidth, pageHeight) = PageLayout.PageSizeDip(page);
        dialog.PrintTicket.PageMediaSize = new System.Printing.PageMediaSize(pageWidth, pageHeight);

        if (dialog.ShowDialog() != true)
            return;

        // Build a fresh, page-settings-aware paginator (display-only clone of the editor content),
        // breaking the flow into pages at the model's geometry and overlaying any header/footer.
        var paginator = PrintLayout.BuildPaginator(_editor);
        dialog.PrintDocument(paginator, "FreeW Document");
    }

    private void OpenPrintPreview()
    {
        var preview = new PrintPreviewWindow(_editor) { Owner = this };
        preview.Show();
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

    private void OpenProperties()
    {
        var dialog = new PropertiesDialog(this, _editor.Model.Properties);
        if (dialog.ShowDialog() == true)
            _file.MarkDirty();
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
        doc.Blocks.Clear();

        doc.Blocks.Add(new Paragraph("Welcome to FreeW") { StyleId = "Title" });
        doc.Blocks.Add(new Paragraph("A free word processor") { StyleId = "Heading1" });

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
        doc.Blocks.Add(intro);

        doc.Blocks.Add(new Paragraph("Centered paragraph.")
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
