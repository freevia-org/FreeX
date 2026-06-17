using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Free.Shared.Ribbon.Wpf;
using FreeW.App.Host.Backstage;
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
    private Ruler _hRuler = null!;
    private Ruler _vRuler = null!;
    private TextBlock _titleText = null!;
    private TextBlock _pageText = null!;
    private TextBlock _sectionText = null!;
    private TextBlock _countsText = null!;
    private Slider _zoomSlider = null!;
    private TextBlock _zoomLabel = null!;
    private FindReplaceDialog? _findDialog;
    private RibbonStateStore _stateStore = null!;
    private BackstageView _backstage = null!;
    private Border _navPane = null!;
    private ListBox _navList = null!;
    private bool _navPaneVisible;

    // The grey "desk" the Print-Layout page floats on. Frozen so it can back the editor cheaply.
    private static readonly Brush WorkspaceBrush = CreateWorkspaceBrush();
    private Border _workspace = null!;

    private static Brush CreateWorkspaceBrush()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6));
        brush.Freeze();
        return brush;
    }

    // Read mode (distraction-free view) chrome we hide/restore, plus the saved presentation we restore.
    private Border _titleBar = null!;
    private UIElement _ribbon = null!;
    private TabControl _ribbonTabs = null!;
    private StatusBar _status = null!;
    private StatusBarItem _dataFolderItem = null!;
    private StatusBarItem _viewSwitchItem = null!;
    private StatusBarItem _zoomItem = null!;
    private bool _readMode;
    private bool _navPaneVisibleBeforeReadMode;
    private Thickness _editorMarginBeforeReadMode;
    private double _editorMaxWidthBeforeReadMode = double.PositiveInfinity;
    private HorizontalAlignment _editorAlignmentBeforeReadMode = HorizontalAlignment.Stretch;
    // Print-Layout sizing the editor applies (page-width Width + drop shadow) which read mode neutralizes
    // for its reading column and restores on exit, so the two view toggles don't fight over the surface.
    private double _editorWidthBeforeReadMode = double.NaN;
    private Effect? _editorEffectBeforeReadMode;

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
            editor, stateStore, OpenPrintPreview, ToggleNavPane, () => _navPaneVisible, ToggleReadMode, () => _readMode,
            TogglePrintLayout, () => _editor.PrintLayoutEnabled);
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

        var (ribbon, ribbonTabs) = BuildRibbon(FreeWRibbon.Build(), commands, stateStore);
        _ribbon = ribbon;
        _ribbonTabs = ribbonTabs;
        DockPanel.SetDock(ribbon, Dock.Top);
        root.Children.Add(ribbon);

        var status = new StatusBar();
        _status = status;

        // Word-style left cluster: "Page X of Y" then the live word/character counts.
        _pageText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        status.Items.Add(new StatusBarItem { Content = _pageText });
        status.Items.Add(new Separator());
        _sectionText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        status.Items.Add(new StatusBarItem { Content = _sectionText });
        status.Items.Add(new Separator());
        _countsText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        status.Items.Add(new StatusBarItem { Content = _countsText });
        status.Items.Add(new Separator());
        _dataFolderItem = new StatusBarItem { Content = $"Data folder: {ResolveDataFolderLabel()}" };
        status.Items.Add(_dataFolderItem);

        // Word-style right cluster: view-switch buttons (Read Mode / Print Layout) then the zoom control.
        _viewSwitchItem = new StatusBarItem { HorizontalAlignment = HorizontalAlignment.Right, Content = BuildViewSwitchControl() };
        status.Items.Add(_viewSwitchItem);
        _zoomItem = new StatusBarItem { HorizontalAlignment = HorizontalAlignment.Right, Content = BuildZoomControl() };
        status.Items.Add(_zoomItem);
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(status);

        var navPane = BuildNavPane();
        DockPanel.SetDock(navPane, Dock.Left);
        root.Children.Add(navPane);

        // Grey "workspace" behind the editor so the Print-Layout page reads as a white sheet floating on a
        // desk. The editor sizes/centres itself to the page width in Print-Layout mode (see
        // DocumentView.ApplyPageChrome); the grey shows on either side. In plain/continuous mode the editor
        // stretches to fill, so the grey is fully covered and the look is unchanged. Purely host chrome.
        // Word-style rulers (Print-Layout only): a horizontal tick scale above the page and a thinner
        // vertical scale down its left edge. Both are passive, read-only chrome (see Ruler) that mirror the
        // page geometry; the corner cell where they meet stays blank. The editor sits in the bottom-right
        // cell so the page floats on the grey workspace exactly as before.
        _hRuler = new Ruler(editor, Ruler.Orientation.Horizontal);
        _vRuler = new Ruler(editor, Ruler.Orientation.Vertical);

        var workspaceGrid = new Grid();
        workspaceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        workspaceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        workspaceGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        workspaceGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Grid.SetRow(_hRuler, 0);
        Grid.SetColumn(_hRuler, 1);
        workspaceGrid.Children.Add(_hRuler);

        Grid.SetRow(_vRuler, 1);
        Grid.SetColumn(_vRuler, 0);
        workspaceGrid.Children.Add(_vRuler);

        Grid.SetRow(editor, 1);
        Grid.SetColumn(editor, 1);
        workspaceGrid.Children.Add(editor);

        _workspace = new Border
        {
            Background = WorkspaceBrush,
            Child = workspaceGrid
        };
        root.Children.Add(_workspace);

        // Keep the indent/tab markers on the horizontal ruler following the caret/selection.
        editor.SelectionChanged += (_, _) => _hRuler.Refresh();

        CommandBindings.Add(new CommandBinding(ApplicationCommands.New, (_, _) => _file.New()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Open, (_, _) => _file.Open()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, (_, _) => _file.Save()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.SaveAs, (_, _) => _file.SaveAs()));

        CommandBindings.Add(new CommandBinding(ApplicationCommands.Print, (_, _) => Print()));

        var findReplace = new RoutedUICommand("Find & Replace", "FindReplace", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(findReplace, (_, _) => OpenFindReplace()));
        InputBindings.Add(new KeyBinding(findReplace, new KeyGesture(Key.F, ModifierKeys.Control)));
        InputBindings.Add(new KeyBinding(findReplace, new KeyGesture(Key.H, ModifierKeys.Control)));

        // Ctrl+Shift+V: Paste Text Only (paste-special), the Word-standard shortcut.
        var pastePlain = new RoutedUICommand("Paste Text Only", "PastePlain", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(pastePlain, (_, _) => _editor.PastePlainText()));
        InputBindings.Add(new KeyBinding(pastePlain, new KeyGesture(Key.V, ModifierKeys.Control | ModifierKeys.Shift)));

        UpdateTitle();
        UpdateCounts();
        RefreshOutline();

        // Print Layout is the default view (the Word default), so seed the View > Print Layout toggle as
        // checked to match the editor's initial PrintLayoutEnabled state.
        _stateStore.SetChecked("freew.print-layout", _editor.PrintLayoutEnabled);

        // The Word-style Backstage (File screen) is a full-window overlay above the document. It is
        // hidden by default; the File button (title bar) shows it, a back arrow / Esc hides it. It reuses
        // the host's existing File commands — no file IO is reimplemented in the backstage.
        _backstage = new BackstageView(_editor, _file, new BackstageActions(
            New: () => _file.New(),
            Open: () => _file.Open(),
            OpenPath: path => _file.OpenPath(path),
            Save: () => _file.Save(),
            SaveAs: () => _file.SaveAs(),
            Print: Print,
            EditProperties: OpenProperties,
            OnClosed: () => { },
            DataFolder: ResolveDataFolderLabel));

        var shell = new Grid();
        shell.Children.Add(root);
        shell.Children.Add(_backstage);
        Content = shell;

        // V5 KeyTips: pressing Alt overlays Word-style letter badges over the ribbon tabs, then over the
        // active tab's controls, so the ribbon is fully keyboard-navigable. The overlay walks the rendered
        // ribbon and draws its badges on the shell grid (which spans the whole client area).
        KeyTipsOverlay.Install(this, _ribbonTabs, shell);
    }

    // Show the Word-style Backstage (File screen) over the document.
    private void ShowBackstage() => _backstage.Show();

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

        // The Backstage entry point: opens the full-window Word-style File screen.
        var fileButton = new Button
        {
            Content = "File",
            Margin = new Thickness(0, 0, 12, 0),
            Padding = new Thickness(12, 2, 12, 2),
            FontWeight = FontWeights.SemiBold
        };
        fileButton.Click += (_, _) => ShowBackstage();
        bar.Children.Add(fileButton);

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
        UpdatePageStatus();

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

    // Refresh the Word-style "Page X of Y" status: an approximate page position derived from the editor's
    // single continuous flow against the page's printable height (see DocumentView.PageInfo). It tracks the
    // on-screen page-break markers, which can differ by a page from the fully paginated Print Preview.
    private void UpdatePageStatus()
    {
        var (current, total) = _editor.PageInfo();
        _pageText.Text = $"Page {current} of {total}";

        // Word-style current-section indicator next to the page count. Best-effort: which section the
        // caret's block falls in, out of TextDocument.Sections (see DocumentView.SectionInfo).
        var (section, sections) = _editor.SectionInfo();
        _sectionText.Text = $"Section {section} of {sections}";
    }

    // The Word-style view-switch cluster on the right of the status bar: a Read Mode toggle and a Print
    // Layout toggle. They reuse the existing MainWindow toggles (ToggleReadMode / TogglePrintLayout), so the
    // ribbon View tab and these buttons drive the same state. No new view state is introduced here.
    private UIElement BuildViewSwitchControl()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        Button ViewButton(string label, string tip, Action onClick)
        {
            var button = new Button { Content = label, Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(2, 0, 2, 0), ToolTip = tip };
            button.Click += (_, _) => onClick();
            return button;
        }

        panel.Children.Add(ViewButton("Read Mode", "Toggle distraction-free Read Mode", ToggleReadMode));
        panel.Children.Add(ViewButton("Print Layout", "Toggle Print Layout page view", TogglePrintLayout));
        return panel;
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

        // Right-click an outline entry to restructure it: Promote/Demote change the heading's style
        // (reversible, like the styles dropdown) and Collapse/Expand hide/show its body in the editor
        // view only. Each item maps the selected OutlineEntry's model block index onto the DocumentView.
        _navList.ContextMenu = BuildOutlineContextMenu();

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
            _editorWidthBeforeReadMode = _editor.Width;
            _editorEffectBeforeReadMode = _editor.Effect;

            _titleBar.Visibility = Visibility.Collapsed;
            _ribbon.Visibility = Visibility.Collapsed;
            _dataFolderItem.Visibility = Visibility.Collapsed;
            _viewSwitchItem.Visibility = Visibility.Collapsed;
            _zoomItem.Visibility = Visibility.Collapsed;

            // Collapse the navigation pane while reading (without disturbing its remembered state).
            _navPane.Visibility = Visibility.Collapsed;

            // A centered, comfortable reading column: cap the width and add generous breathing room.
            // Drop any Print-Layout page sizing/shadow so the reading column owns the surface width.
            _editor.HorizontalAlignment = HorizontalAlignment.Center;
            _editor.Width = double.NaN;
            _editor.Effect = null;
            _editor.MaxWidth = 760;
            _editor.Margin = new Thickness(40, 40, 40, 40);
        }
        else
        {
            _titleBar.Visibility = Visibility.Visible;
            _ribbon.Visibility = Visibility.Visible;
            _dataFolderItem.Visibility = Visibility.Visible;
            _viewSwitchItem.Visibility = Visibility.Visible;
            _zoomItem.Visibility = Visibility.Visible;

            // Restore the editor's original presentation (including any Print-Layout page sizing/shadow).
            _editor.HorizontalAlignment = _editorAlignmentBeforeReadMode;
            _editor.MaxWidth = _editorMaxWidthBeforeReadMode;
            _editor.Margin = _editorMarginBeforeReadMode;
            _editor.Width = _editorWidthBeforeReadMode;
            _editor.Effect = _editorEffectBeforeReadMode;

            // Restore the navigation pane to whatever it was before entering read mode.
            _navPane.Visibility = _navPaneVisibleBeforeReadMode ? Visibility.Visible : Visibility.Collapsed;
        }

        _stateStore.SetChecked("freew.read-mode", _readMode);
    }

    // View > Print Layout: flip the editor between the Word-style page view (white page on the grey
    // workspace, margins shown, drop shadow, page-break markers) and the plain/continuous flat view.
    // DocumentView owns the page presentation; here we only mirror the new checked-state into the shared
    // RibbonStateStore so the toggle button stays in sync, exactly like the read-mode / nav-pane toggles.
    private void TogglePrintLayout()
    {
        var enabled = _editor.TogglePrintLayout();
        _stateStore.SetChecked("freew.print-layout", enabled);
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

    // The outline-entry context menu (Promote / Demote / Collapse / Expand). Each item acts on the
    // currently selected OutlineEntry, mapping its model block index onto the editor's heading commands,
    // then refreshes the outline so promoted/demoted levels and collapse markers update.
    private ContextMenu BuildOutlineContextMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(OutlineMenuItem("Promote", entry => _editor.PromoteHeading(entry.BlockIndex)));
        menu.Items.Add(OutlineMenuItem("Demote", entry => _editor.DemoteHeading(entry.BlockIndex)));
        menu.Items.Add(new Separator());
        menu.Items.Add(OutlineMenuItem("Collapse", entry => _editor.CollapseHeading(entry.BlockIndex)));
        menu.Items.Add(OutlineMenuItem("Expand", entry => _editor.ExpandHeading(entry.BlockIndex)));
        return menu;
    }

    // Build one outline context-menu item that runs `action` against the selected outline entry. A no-op
    // when nothing is selected. The outline is refreshed afterwards so the nav list reflects the change.
    private MenuItem OutlineMenuItem(string header, Action<OutlineEntry> action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) =>
        {
            if (_navList.SelectedItem is not OutlineItem selected)
                return;
            action(selected.Entry);
            RefreshOutline();
        };
        return item;
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

    // --- Real Word-style ribbon, rendered by the shared WPF renderer ---
    //
    // BuildRibbon builds a flat tab strip (Home/Insert/Layout/Design/View/Mailings/Review) over the
    // shared RibbonDefinition model. Each selected tab's body is produced by the shared
    // Free.Shared.Ribbon.Wpf.RibbonWpfRenderer — the same renderer FreeX uses — so FreeW gets Word's
    // visual vocabulary (Large hero buttons, Medium icon+label, Small icon-only, group panels, dividers,
    // group-label borders and vector glyphs). Command behavior and live toggle state flow through the
    // FreeW command registry + IRibbonStateStore exactly as before.

    private (UIElement Ribbon, TabControl Tabs) BuildRibbon(RibbonDefinition definition, IRibbonCommandRegistry registry, IRibbonStateStore stateStore)
    {
        // Install FreeW's command-id → glyph mapping so the shared renderer draws meaningful icons for
        // freew.* ids (otherwise every button would fall back to the generic glyph).
        FreeWRibbonIcons.Install();

        var tabs = new TabControl
        {
            Background = Brushes.White,
            BorderThickness = new Thickness(0),
            MinHeight = 116
        };

        // The renderer resolves its button/group styles and surface brushes via TryFindResource on the
        // supplied resource host. Merge FreeW's ribbon styles into the TabControl so those lookups
        // resolve (the renderer falls back gracefully for any key it can't find).
        tabs.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/FreeW.App.Host;component/Ribbon/FreeWRibbonResources.xaml", UriKind.Relative)
        });

        foreach (var tab in definition.Tabs)
        {
            var content = RibbonWpfRenderer.BuildTabContent(tab, tabs, registry, stateStore);

            // V5 galleries: inject the live-preview Word-style galleries into the rendered group content.
            // The shared renderer stamps each group's grid with its catalog id (RibbonMetadata.CatalogId),
            // so we find the target group and prepend a custom gallery control into its content lane. This
            // keeps the galleries entirely app-side (custom WPF content) without a shared RibbonGallery type.
            if (tab.Id == "home")
                // Drop the placeholder Style combo (the gallery supersedes it) but keep the group's
                // New Style / Manage Styles buttons, prepending the live-preview gallery before them.
                InjectGallery(content, "styles", StylesGallery.Build(_editor), removeKind: RemoveKind.Combos);
            if (tab.Id == "design")
                // The Design > themes group's only control is the placeholder Themes combo; replace it
                // wholesale with the Themes gallery plus the theme-colours gallery.
                InjectGallery(content, "themes", ThemeGallery.BuildThemes(_editor), removeKind: RemoveKind.All,
                    extra: ThemeGallery.BuildColours(_editor));

            var item = new TabItem { Header = tab.Header, Content = content };
            tabs.Items.Add(item);
        }

        if (tabs.Items.Count > 0)
            tabs.SelectedIndex = 0;

        var border = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = tabs
        };
        return (border, tabs);
    }

    // What of a group's original rendered controls to drop before injecting a gallery.
    private enum RemoveKind { None, Combos, All }

    // Find the group grid carrying CatalogId == groupId in the freshly built tab content and prepend the
    // gallery into its content lane (row 0). `removeKind` controls which of the group's original
    // placeholder controls are removed first: All clears the lane (the gallery fully owns the group);
    // Combos drops only ComboBox columns (so a placeholder combo the gallery supersedes goes away while
    // command buttons like New Style / Manage Styles remain). An optional `extra` gallery is appended
    // after the first (e.g. the Design theme-colours strip).
    private static void InjectGallery(DependencyObject content, string groupId, FrameworkElement gallery, RemoveKind removeKind, FrameworkElement? extra = null)
    {
        var grid = FindGroupGrid(content, groupId);
        if (grid is null)
            return;

        // Row 0 of the group grid holds the content lane (a horizontal StackPanel of columns/controls).
        var lane = grid.Children.OfType<FrameworkElement>().FirstOrDefault(c => Grid.GetRow(c) == 0) as Panel;
        if (lane is null)
            return;

        if (removeKind == RemoveKind.All)
        {
            lane.Children.Clear();
        }
        else if (removeKind == RemoveKind.Combos)
        {
            // Each lane column is its own StackPanel; the renderer packs combos into combo-only columns,
            // so a column whose children are all ComboBoxes is a placeholder-combo column to drop.
            var toRemove = lane.Children.OfType<Panel>()
                .Where(col => col.Children.Count > 0 && col.Children.OfType<UIElement>().All(c => c is ComboBox))
                .ToList();
            foreach (var col in toRemove)
                lane.Children.Remove(col);
        }

        var host = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(2, 2, 2, 0) };
        host.Children.Add(gallery);
        if (extra is not null)
            host.Children.Add(extra);
        lane.Children.Insert(0, host);
    }

    // Find the group content grid stamped with the given catalog id, walking the renderer's known
    // structure: the tab content is a Border whose child is a RibbonAdaptivePanel whose children are
    // RibbonGroupHosts. Each host's Content is the group grid (which carries the catalog id). This walks
    // the logical structure the renderer built eagerly, so it works before the visual tree is realized
    // (unlike VisualTreeHelper, which would see nothing until the ribbon is measured/rendered).
    private static Grid? FindGroupGrid(DependencyObject root, string groupId)
    {
        var panel = (root as Border)?.Child as Panel;
        if (panel is null)
            return null;

        foreach (var child in panel.Children)
        {
            if (child is RibbonGroupHost host && host.Content is Grid grid
                && RibbonMetadata.GetCatalogId(grid) == groupId)
                return grid;
        }
        return null;
    }
}
