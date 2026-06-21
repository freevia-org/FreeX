using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;
using Free.Shared.Ribbon.Wpf;
using FreeW.App.Host.Backstage;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using TextSearch = FreeW.Core.Model.TextSearch;

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

    // Reveal Formatting pane (Word's Shift+F1): a read-only side pane, docked on the right (Word's side),
    // that mirrors the effective FONT / PARAGRAPH / SECTION formatting of the current selection. It updates
    // on SelectionChanged from the pure FreeW.Core.Model.RevealFormatting describer, so the pane never
    // touches the model and cannot interfere with editing. Mirrors the navigation pane's dock/toggle shape.
    private Border _revealPane = null!;
    private StackPanel _revealContent = null!;
    private bool _revealPaneVisible;

    // Reviewing Pane (Word's Review > Reviewing Pane): a dockable list of every tracked change in the
    // document — author, date, type, and the affected text — with click-to-navigate (jumps the editor to
    // the change) plus Accept / Reject of the SELECTED single revision and Previous/Next navigation. It is
    // rebuilt from the pure FreeW.Core.Model.RevisionList whenever the document changes or the pane opens,
    // so the surface never owns revision logic. Mirrors the navigation/reveal panes' dock + toggle shape.
    private Border _reviewPane = null!;
    private ListBox _reviewList = null!;
    private TextBlock _reviewStatus = null!;
    private bool _reviewPaneVisible;
    private bool _reviewPaneVisibleBeforeReadMode;
    // The revisions currently shown in the pane (the live snapshot the list items index into).
    private System.Collections.Generic.IReadOnlyList<RevisionEntry> _reviewEntries = System.Array.Empty<RevisionEntry>();

    // Navigation-pane search (the box at the top of the pane). Typing finds every occurrence of the term
    // in the document body; the result label shows the count and Next/Prev step through the matches,
    // jumping each into view in the editor. The heading outline below is filtered to entries that either
    // match the term themselves or own a matching block in their subtree. All matching reuses the pure
    // FreeW.Core.Model.TextSearch helper (the same one Find & Replace uses) — no bespoke search here.
    private TextBox _navSearch = null!;
    private TextBlock _navSearchStatus = null!;
    private Button _navSearchPrev = null!;
    private Button _navSearchNext = null!;
    private readonly List<int> _navSearchHits = new(); // model block indices with a match, in order
    private int _navSearchHitIndex = -1;                // current position within _navSearchHits

    // Identity/palette for the shared window shell (FreeX navy title bar; the real FreeW app icon as the
    // title-bar badge + window/taskbar icon).
    private static readonly ShellChromeOptions ChromeOptions = new()
    {
        BadgeLetter = "W",
        TitleBarColor = Color.FromRgb(0x17, 0x32, 0x4D),
        BadgeColor = Color.FromRgb(0x0F, 0x6D, 0x8C),
        CaptionHeight = 34,
        IconUri = "pack://application:,,,/Resources/FreeW.ico"
    };

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

    // Manages the Word-style contextual "Tools" tabs (Picture Format / Table Design / Table Layout):
    // the shared controller shows them only while their selection context is active.
    private RibbonContextualTabController _contextualTabs = null!;

    // File ribbon tab (Word-style Backstage entry): selecting it opens Backstage and the shared router
    // restores the previously-active content tab.
    private TabItem _fileTab = null!;
    private RibbonFileTabRouter? _fileTabRouter;
    private Border _status = null!;
    private Border _markedAsFinalBanner = null!;
    private TextBlock _dataFolderText = null!;
    private FrameworkElement _dataFolderItem = null!;
    private FrameworkElement _viewSwitchItem = null!;
    private FrameworkElement _zoomItem = null!;
    private bool _readMode;

    // Status-bar view-switch toggle buttons for the three mutually-exclusive print-family view modes
    // (Print Layout / Web Layout / Draft). They mirror the same state as the View ribbon's Views group;
    // RefreshViewModeChecks keeps exactly one of them checked to match _editor.ViewMode.
    private ToggleButton _printLayoutSwitch = null!;
    private ToggleButton _webLayoutSwitch = null!;
    private ToggleButton _draftSwitch = null!;

    // Outline view (View > Outline). The outline surface overlays the normal editing surface; entering the
    // view hides the workspace (and its rulers) and shows the outline, exiting restores them verbatim —
    // the same save/restore shape as Read Mode. The model is never mutated by switching views.
    private OutlineView _outlineView = null!;
    private bool _outlineMode;
    private Visibility _hRulerVisibilityBeforeOutline;
    private Visibility _vRulerVisibilityBeforeOutline;
    private bool _navPaneVisibleBeforeReadMode;
    private bool _revealPaneVisibleBeforeReadMode;
    private Thickness _editorMarginBeforeReadMode;
    private double _editorMaxWidthBeforeReadMode = double.PositiveInfinity;
    private HorizontalAlignment _editorAlignmentBeforeReadMode = HorizontalAlignment.Stretch;
    // Print-Layout sizing the editor applies (page-width Width + drop shadow) which read mode neutralizes
    // for its reading column and restores on exit, so the two view toggles don't fight over the surface.
    private double _editorWidthBeforeReadMode = double.NaN;
    private Effect? _editorEffectBeforeReadMode;

    // FreeW's persisted settings (shared JsonSettingsStore). Defaults are used when none are supplied,
    // so the window stays constructible in isolation; Program.Main passes the loaded options + the store
    // that persists edits made from the backstage Options dialog. The options instance is mutated in place
    // so settings read live by FileCommands (e.g. the recent-files cap) take effect without a restart.
    private readonly FreeWOptions _options;
    private readonly ApplicationOptionsStore<FreeWOptions> _optionsStore;

    public MainWindow() : this(new FreeWOptions())
    {
    }

    public MainWindow(FreeWOptions options, ApplicationOptionsStore<FreeWOptions>? optionsStore = null)
    {
        _options = options ?? new FreeWOptions();
        // No store supplied (e.g. constructed in isolation / tests) → a no-op in-memory store so editing
        // still round-trips through the dialog and applies live, just without touching the real profile.
        _optionsStore = optionsStore ?? ApplicationOptionsStore<FreeWOptions>.ForPath(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FreeW", "settings.transient.json"));
        Title = "FreeW";
        Width = 1280;
        Height = 760;
        // Open maximized like FreeX, so the ribbon shows its groups in full rather than collapsing the
        // dense tabs to overflow dropdowns at a small default size.
        WindowState = WindowState.Maximized;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        // Build the borderless WindowChrome shell — custom integrated title bar with embedded window
        // buttons, Win11 rounded corners, the maximized inset, and the shared chrome styles — from the
        // shared tier, so FreeW assembles its window from shared parts instead of re-coding the chrome.
        // App-specific ribbon brushes/styles still come from FreeWRibbonResources (merged at the ribbon).
        ShellChrome.ConfigureWindow(this, ChromeOptions);

        // Root layout is an explicit 3-row grid so the footer (#3) is unambiguously a full-width row BELOW
        // the body. Row 0 = window chrome (title bar + ribbon, stacked), row 1 = body (nav pane + workspace,
        // where the vertical ruler lives), row 2 = status bar. The body's ruler therefore cannot draw over
        // the footer: they occupy separate grid rows.
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // chrome (title + ribbon)
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // body
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // status bar

        var chromeStack = new StackPanel { Orientation = Orientation.Vertical };
        Grid.SetRow(chromeStack, 0);
        root.Children.Add(chromeStack);

        var body = new DockPanel { LastChildFill = true };
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var editor = new DocumentView { Margin = new Thickness(40, 24, 40, 24) };
        _editor = editor;
        // Push the persisted AutoCorrect / AutoFormat-As-You-Type settings so the editor's as-you-type
        // rules honour the user's toggles from the first keystroke (re-applied when Options is saved).
        ApplyAutoFormatOptions();
        editor.LoadModel(CreateSampleDocument());
        var stateStore = new RibbonStateStore();
        _stateStore = stateStore;
        var commands = FreeWRibbonCommands.Build(
            editor, stateStore, OpenPrintPreview, ToggleNavPane, () => _navPaneVisible, ToggleReadMode, () => _readMode,
            () => SetViewMode(DocumentViewMode.PrintLayout), () => _editor.ViewMode == DocumentViewMode.PrintLayout,
            ToggleOutlineView, () => _outlineMode, OpenZoomDialog,
            onWebLayout: () => SetViewMode(DocumentViewMode.WebLayout),
            isWebLayoutActive: () => !_outlineMode && _editor.ViewMode == DocumentViewMode.WebLayout,
            onDraftView: () => SetViewMode(DocumentViewMode.Draft),
            isDraftViewActive: () => !_outlineMode && _editor.ViewMode == DocumentViewMode.Draft,
            onToggleRevealFormatting: ToggleRevealFormatting,
            isRevealFormattingVisible: () => _revealPaneVisible,
            onToggleReviewingPane: ToggleReviewPane,
            isReviewingPaneVisible: () => _reviewPaneVisible,
            onAcceptThisChange: AcceptSelectedRevision,
            onRejectThisChange: RejectSelectedRevision,
            onPreviousChange: () => StepRevision(-1),
            onNextChange: () => StepRevision(+1),
            onFindReplace: OpenFindReplace);
        _file = new FileCommands(this, editor, UpdateTitle, _options);
        editor.TextChanged += (_, _) => { _file.MarkDirty(); UpdateCounts(); RefreshOutline(); RefreshContextualTabs(); RefreshReviewPane(); };
        // Live selection stats: when the caret/selection moves, refresh the status-bar counts so a
        // non-empty selection shows its own word/character totals (and reverts when nothing is selected).
        // Also re-evaluate which contextual "Tools" tabs apply to the new selection.
        editor.SelectionChanged += (_, _) => { UpdateCounts(); RefreshContextualTabs(); };
        _autosave = new AutosaveCoordinator(editor, _file);
        Loaded += (_, _) => { _autosave.OfferRecovery(this); _autosave.Start(); };
        Closing += (_, e) =>
        {
            // Save-before-close gate (shared FileLifecyclePlanner). Cancel the close if the user
            // backs out; only stop autosave (which deletes the recovery snapshot) once we commit to
            // closing. Previously FreeW closed without prompting and silently lost unsaved work.
            if (!_file.ConfirmCloseAllowed())
            {
                e.Cancel = true;
                return;
            }
            _autosave.Stop();
        };

        // The title bar comes from the shared shell; the host fills its QAT slot and keeps the title text.
        // It is composed into the OUTER grid (below), in its own top row ABOVE the Backstage overlay, so
        // opening the File screen never hides the caption / QAT / window buttons. Only the ribbon goes into
        // the chrome stack here.
        var titleBar = ShellChrome.BuildTitleBar(this, ChromeOptions);
        _titleBar = titleBar.Root;
        _titleText = titleBar.TitleText;
        AddQuickAccessButtons(titleBar.QatHost);

        var (ribbon, ribbonTabs) = BuildRibbon(FreeWRibbon.Build(), commands, stateStore);
        _ribbon = ribbon;
        _ribbonTabs = ribbonTabs;
        chromeStack.Children.Add(ribbon);

        var status = BuildStatusBar();
        Grid.SetRow(status, 2);
        root.Children.Add(status);

        var navPane = BuildNavPane();
        DockPanel.SetDock(navPane, Dock.Left);
        body.Children.Add(navPane);

        // Reveal Formatting pane docks on the RIGHT (Word's side for the Shift+F1 pane), opposite the
        // left navigation pane. Added before the fill child so the DockPanel reserves its edge first.
        var revealPane = BuildRevealPane();
        DockPanel.SetDock(revealPane, Dock.Right);
        body.Children.Add(revealPane);

        // Reviewing Pane also docks on the RIGHT (Word's side for the revisions list). Added before the
        // fill child so the DockPanel reserves its edge; only one of reveal/review is typically open.
        var reviewPane = BuildReviewPane();
        DockPanel.SetDock(reviewPane, Dock.Right);
        body.Children.Add(reviewPane);

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

        // Outline view (View > Outline): an indented heading/body outline with the Outlining mini-toolbar.
        // It overlays the normal editing surface and is collapsed until the view is switched on; both share
        // one host grid so toggling between Print Layout and Outline just flips which child is visible —
        // the editor model is never disturbed (mirrors the Read-Mode enter/exit pattern).
        _outlineView = new OutlineView(_editor) { Visibility = Visibility.Collapsed };
        var contentHost = new Grid();
        contentHost.Children.Add(_workspace);
        contentHost.Children.Add(_outlineView);

        // "Marked as Final" banner (Word's advisory read-only bar): a subtle amber strip docked above the
        // editing surface, with an "Edit Anyway" button that clears the flag. Collapsed until the document
        // is marked final; kept in sync via the editor's ProtectionStateChanged event.
        _markedAsFinalBanner = BuildMarkedAsFinalBanner();
        DockPanel.SetDock(_markedAsFinalBanner, Dock.Top);
        body.Children.Add(_markedAsFinalBanner);

        body.Children.Add(contentHost);

        // Keep the banner and the Protect-group ribbon toggles in sync with the editor's protection /
        // Mark-as-Final state, however it changes (ribbon command, load, or "Edit Anyway").
        editor.ProtectionStateChanged += (_, _) =>
        {
            RefreshMarkedAsFinalBanner();
            _stateStore.SetChecked("freew.mark-as-final", _editor.IsMarkedAsFinal);
            _stateStore.SetChecked("freew.restrict-editing", _editor.IsProtected);
        };

        // Keep the indent/tab markers on the horizontal ruler following the caret/selection.
        editor.SelectionChanged += (_, _) => _hRuler.Refresh();

        // Keep the Reveal Formatting pane (when shown) reflecting the caret's current formatting.
        editor.SelectionChanged += (_, _) => RefreshRevealFormatting();

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

        // Shift+F1: toggle the Reveal Formatting pane (Word's keyboard shortcut for the Shift+F1 pane).
        var revealFormatting = new RoutedUICommand("Reveal Formatting", "RevealFormatting", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(revealFormatting, (_, _) => ToggleRevealFormatting()));
        InputBindings.Add(new KeyBinding(revealFormatting, new KeyGesture(Key.F1, ModifierKeys.Shift)));

        // Alt+F9: toggle field codes vs results across the document (Word's field-code toggle).
        var toggleFieldCodes = new RoutedUICommand("Toggle Field Codes", "ToggleFieldCodes", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(toggleFieldCodes, (_, _) => _editor.ToggleFieldCodes()));
        InputBindings.Add(new KeyBinding(toggleFieldCodes, new KeyGesture(Key.F9, ModifierKeys.Alt)));

        // F9: update (recompute) every field's result (Word's Update Field shortcut).
        var updateFields = new RoutedUICommand("Update Fields", "UpdateFields", typeof(MainWindow));
        CommandBindings.Add(new CommandBinding(updateFields, (_, _) => _editor.UpdateFields()));
        InputBindings.Add(new KeyBinding(updateFields, new KeyGesture(Key.F9)));

        UpdateTitle();
        UpdateCounts();
        RefreshOutline();

        // Print Layout is the default view (the Word default), so seed the View > Views toggles (Print
        // Layout / Web Layout / Draft) to reflect the editor's initial view mode — exactly one is checked.
        RefreshViewModeChecks();

        // The Word-style Backstage (File screen) is a full-window overlay above the document. It is
        // hidden by default; the File button (title bar) shows it, a back arrow / Esc hides it. It reuses
        // the host's existing File commands — no file IO is reimplemented in the backstage.
        _backstage = new BackstageView(_editor, _file, new BackstageActions(
            New: () => _file.New(),
            Open: () => _file.Open(),
            OpenPath: path => _file.OpenPath(path),
            Save: () => _file.Save(),
            SaveAs: () => _file.SaveAs(),
            SaveCopy: () => _file.SaveCopy(),
            Print: Print,
            ExportPdf: ExportToPdf,
            ExportXps: ExportToXps,
            EditProperties: OpenProperties,
            EditOptions: OpenOptions,
            CurrentOptions: () => _options,
            OnClosed: () => SetEditorAdornersVisible(true),
            DataFolder: ResolveDataFolderLabel));

        // Compose the window. The title bar occupies its own top row of the OUTER grid, always above the
        // Backstage; `belowTitle` stacks the Backstage overlay over the 3-row body (ribbon + document +
        // status), so the File screen covers those but leaves the title bar visible (Word behaviour).
        var frame = SisterAppWindowFrameBuilder.Build(new SisterAppWindowFrameSpec(_titleBar, root, _backstage));
        Content = frame.Root;

        // V5 KeyTips: pressing Alt overlays Word-style letter badges over the ribbon tabs, then over the
        // active tab's controls, so the ribbon is fully keyboard-navigable. The overlay walks the rendered
        // ribbon and draws its badges on the outer grid (which spans the whole client area).
        KeyTipsOverlay.Install(this, _ribbonTabs, frame.Root);
    }

    // Show the Word-style Backstage (File screen) over the document.
    private void ShowBackstage()
    {
        // The backstage is an opaque overlay, but the editor's page-break markers live in the window
        // AdornerLayer, which draws ABOVE sibling content — so they bleed through the File screen unless the
        // layer is hidden. Collapse it here and restore it in the backstage OnClosed callback.
        SetEditorAdornersVisible(false);
        _backstage.Show();
    }

    // Toggle the editor's AdornerLayer (page-break markers, etc.) so they don't draw over the backstage.
    private void SetEditorAdornersVisible(bool visible)
    {
        if (System.Windows.Documents.AdornerLayer.GetAdornerLayer(_editor) is { } layer)
            layer.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    // Recompute which contextual "Tools" tabs apply to the current selection and let the shared controller
    // show/hide them: Picture Format when an image is selected, table tabs when the caret is in a table.
    // The activation keys ("picture"/"table") match the RibbonTabContext keys declared in FreeWRibbon.
    private void RefreshContextualTabs()
    {
        if (_contextualTabs is null)
            return;

        var state = RibbonContextState.None;
        if (_editor.SelectedImage() is not null)
            state = state.With("picture");
        if (_editor.IsCaretInTable())
            state = state.With("table");

        _contextualTabs.Apply(state);
    }

    // Fill the shared title bar's Quick Access Toolbar slot with Save / Undo / Redo via the shared QAT
    // renderer (Free.Shared.Ribbon.Wpf): neutral descriptors (command id + tooltip + ribbon glyph) rendered
    // as small flat icon buttons (white vector glyphs on the navy caption, shared ChromeFlatButtonStyle,
    // hit-test-visible so clicks land while WindowChrome owns the caption). The click callback routes by
    // command id: Save → the file command; Undo/Redo → the editor's built-in (RichTextBox) history, the same
    // inline history the keyboard drives.
    private void AddQuickAccessButtons(StackPanel host) =>
        SisterQuickAccessToolbarBuilder.Render(
            host,
            this,
            new SisterQuickAccessToolbarActions(
                Save: () => _file.Save(),
                Undo,
                Redo));

    private void UpdateTitle()
    {
        var title = WindowTitlePlanner.Compose(
            displayName: _file.DisplayName,
            applicationName: "FreeW",
            isDirty: _file.IsDirty,
            dirtyMarker: " *",
            separator: " — ");
        Title = title;
        _titleText.Text = title;
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

    // Build the footer (status bar) as a single full-width row that sits BELOW the ruler + document region
    // (#3): it is docked to the bottom of the outer DockPanel, so the workspace (which contains the vertical
    // ruler and the page) fills only the space above it and can never draw over it.
    //
    // The bar is responsive (#4): a 3-column grid whose middle column ("*") holds the left info group, which
    // is allowed to condense/ellipsize as the window narrows, while the right-side view + zoom controls are
    // pinned in Auto columns so they stay fully visible. The whole strip uses the same #2B579A surface as the
    // title bar with the shared clean (flat, hover-only) footer button styles.
    // Word's "Marked as Final" advisory bar: a subtle amber strip above the editing surface with the
    // information text and an "Edit Anyway" button that clears the flag (re-enabling editing). Collapsed
    // until the document is marked final; see RefreshMarkedAsFinalBanner.
    private Border BuildMarkedAsFinalBanner()
    {
        var text = new TextBlock
        {
            Text = "Marked as Final  An author has marked this document as final to discourage editing.",
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x4D, 0x00)),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 12, 0)
        };

        var editAnyway = new Button
        {
            Content = "Edit Anyway",
            MinWidth = 96,
            Padding = new Thickness(8, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center
        };
        editAnyway.Click += (_, _) => _editor.SetMarkedAsFinal(false);

        var row = new DockPanel { LastChildFill = true, Margin = new Thickness(12, 4, 12, 4) };
        DockPanel.SetDock(editAnyway, Dock.Right);
        row.Children.Add(editAnyway);
        row.Children.Add(text);

        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xFD, 0xF3, 0xD0)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE3, 0xC8, 0x70)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Visibility = Visibility.Collapsed,
            Child = row
        };
    }

    // Show the "Marked as Final" banner only while the document carries Word's advisory read-only flag.
    private void RefreshMarkedAsFinalBanner() =>
        _markedAsFinalBanner.Visibility = _editor.IsMarkedAsFinal ? Visibility.Visible : Visibility.Collapsed;

    private Border BuildStatusBar()
    {
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // left info (condenses)
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // view toggles (pinned)
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // zoom (pinned)

        // ── Left info group: Page / Section / Words-Chars-Paragraphs / Data folder. Hosted in a clipping
        //    StackPanel so when space runs short the rightmost item (data folder) is clipped/ellipsized
        //    first while the pinned right-side controls keep their full width. ──
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };

        TextBlock InfoText() => new()
        {
            Foreground = Brushes.White,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        UIElement InfoSep() => new Rectangle
        {
            Width = 1,
            Margin = new Thickness(8, 3, 8, 3),
            Fill = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Stretch
        };

        _pageText = InfoText();
        left.Children.Add(_pageText);
        left.Children.Add(InfoSep());
        _sectionText = InfoText();
        left.Children.Add(_sectionText);
        left.Children.Add(InfoSep());
        _countsText = InfoText();
        left.Children.Add(_countsText);

        // The data folder is the lowest-priority item; wrap it so it ellipsizes and is the first to be
        // clipped when the window narrows (its separator + text live in one panel that can shrink away).
        var dataFolderPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        dataFolderPanel.Children.Add(InfoSep());
        _dataFolderText = InfoText();
        _dataFolderText.Text = $"Data folder: {ResolveDataFolderLabel()}";
        _dataFolderText.ToolTip = _dataFolderText.Text;
        dataFolderPanel.Children.Add(_dataFolderText);
        _dataFolderItem = dataFolderPanel;
        left.Children.Add(dataFolderPanel);

        var leftHost = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 0), ClipToBounds = true };
        leftHost.Children.Add(left);
        Grid.SetColumn(leftHost, 0);
        grid.Children.Add(leftHost);

        // ── Pinned right-side controls. ──
        _viewSwitchItem = (FrameworkElement)BuildViewSwitchControl();
        Grid.SetColumn(_viewSwitchItem, 1);
        grid.Children.Add(_viewSwitchItem);

        _zoomItem = (FrameworkElement)BuildZoomControl();
        _zoomItem.Margin = new Thickness(6, 0, 10, 0);
        Grid.SetColumn(_zoomItem, 2);
        grid.Children.Add(_zoomItem);

        _status = new Border
        {
            // FreeX status-bar surface (#17324D), matching the title bar (FreeXStatusSurfaceBrush).
            Background = new SolidColorBrush(Color.FromRgb(0x17, 0x32, 0x4D)),
            MinHeight = 26,
            Child = grid
        };
        return _status;
    }

    // The Word-style view-switch cluster on the right of the status bar: a Read Mode button plus the three
    // mutually-exclusive print-family view toggles (Print Layout / Web Layout / Draft). They reuse the same
    // MainWindow state the View ribbon drives (ToggleReadMode / SetViewMode), so the ribbon Views group and
    // these buttons stay in lock-step. The print-family buttons are ChromeStatusToggleButtons so the active
    // view reads as pressed; RefreshViewModeChecks keeps exactly one checked.
    private UIElement BuildViewSwitchControl()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        Button ViewButton(string label, string tip, Action onClick)
        {
            var button = new Button
            {
                Content = label,
                Style = (Style)FindResource("ChromeStatusButtonStyle"),
                Padding = new Thickness(8, 1, 8, 1),
                Margin = new Thickness(2, 3, 2, 3),
                ToolTip = tip
            };
            button.Click += (_, _) => onClick();
            return button;
        }

        ToggleButton ViewToggle(string label, string tip, DocumentViewMode mode)
        {
            var toggle = new ToggleButton
            {
                Content = label,
                Style = (Style)FindResource("ChromeStatusToggleButtonStyle"),
                Margin = new Thickness(2, 3, 2, 3),
                ToolTip = tip
            };
            // Clicking always lands on this mode (re-checking the active one is a no-op); never let the
            // toggle uncheck itself, since exactly one print-family view is always active.
            toggle.Click += (_, _) => SetViewMode(mode);
            return toggle;
        }

        _printLayoutSwitch = ViewToggle("Print Layout", "Print Layout page view", DocumentViewMode.PrintLayout);
        _webLayoutSwitch = ViewToggle("Web Layout", "Web Layout: continuous, full-width view (no page chrome)", DocumentViewMode.WebLayout);
        _draftSwitch = ViewToggle("Draft", "Draft: simplified continuous view for fast editing", DocumentViewMode.Draft);

        panel.Children.Add(ViewButton("Read Mode", "Toggle distraction-free Read Mode", ToggleReadMode));
        panel.Children.Add(_printLayoutSwitch);
        panel.Children.Add(_webLayoutSwitch);
        panel.Children.Add(_draftSwitch);
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

        var searchArea = BuildNavSearch();

        var layout = new DockPanel { Width = 240 };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(searchArea, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(searchArea);
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

    // The "Search document" area at the top of the navigation pane: a text box plus a Prev/Next/count
    // row. Typing recomputes the document matches (TextSearch over every body paragraph) and jumps to
    // the first; Prev/Next step through them. Built once and reused; wired to the live editor model.
    private UIElement BuildNavSearch()
    {
        _navSearch = new TextBox
        {
            Margin = new Thickness(10, 0, 10, 4),
            Padding = new Thickness(2, 1, 2, 1),
            ToolTip = "Search document"
        };
        _navSearch.TextChanged += (_, _) => RunNavSearch();
        _navSearch.KeyDown += (_, e) =>
        {
            // Enter advances to the next match (Shift+Enter to the previous), mirroring Word's nav search.
            if (e.Key == Key.Enter)
            {
                StepNavSearch(forward: (Keyboard.Modifiers & ModifierKeys.Shift) == 0);
                e.Handled = true;
            }
        };

        _navSearchPrev = new Button { Content = "‹", Width = 22, Padding = new Thickness(0), ToolTip = "Previous match" };
        _navSearchNext = new Button { Content = "›", Width = 22, Padding = new Thickness(0), ToolTip = "Next match" };
        _navSearchPrev.Click += (_, _) => StepNavSearch(forward: false);
        _navSearchNext.Click += (_, _) => StepNavSearch(forward: true);

        _navSearchStatus = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60))
        };

        var controls = new DockPanel { Margin = new Thickness(10, 0, 10, 6) };
        DockPanel.SetDock(_navSearchPrev, Dock.Left);
        DockPanel.SetDock(_navSearchNext, Dock.Left);
        controls.Children.Add(_navSearchPrev);
        controls.Children.Add(_navSearchNext);
        controls.Children.Add(_navSearchStatus);

        var area = new StackPanel();
        area.Children.Add(_navSearch);
        area.Children.Add(controls);
        UpdateNavSearchStatus();
        return area;
    }

    // Recompute the set of body blocks that contain the search term (reusing the pure TextSearch helper),
    // jump to the first hit, filter the outline to relevant headings, and refresh the Prev/Next/count row.
    // An empty term clears the search and shows the full outline again.
    private void RunNavSearch()
    {
        _navSearchHits.Clear();
        _navSearchHitIndex = -1;

        var term = _navSearch?.Text ?? string.Empty;
        if (!string.IsNullOrEmpty(term))
        {
            _editor.CommitToModel();
            var blocks = _editor.Model.Blocks;
            for (var i = 0; i < blocks.Count; i++)
            {
                if (BlockMatches(blocks[i], term))
                    _navSearchHits.Add(i);
            }

            if (_navSearchHits.Count > 0)
            {
                _navSearchHitIndex = 0;
                _editor.BringBlockIntoView(_navSearchHits[0]);
            }
        }

        RefreshOutline();
        UpdateNavSearchStatus();
    }

    // Move to the next/previous document match (wrapping at the ends) and bring it into view. No-op when
    // there are no matches.
    private void StepNavSearch(bool forward)
    {
        if (_navSearchHits.Count == 0)
            return;
        _navSearchHitIndex = forward
            ? (_navSearchHitIndex + 1) % _navSearchHits.Count
            : (_navSearchHitIndex - 1 + _navSearchHits.Count) % _navSearchHits.Count;
        _editor.BringBlockIntoView(_navSearchHits[_navSearchHitIndex]);
        UpdateNavSearchStatus();
    }

    // Update the "n of m" / "No matches" status label and enable the Prev/Next buttons accordingly.
    private void UpdateNavSearchStatus()
    {
        if (_navSearchStatus is null)
            return;

        var hasTerm = !string.IsNullOrEmpty(_navSearch?.Text);
        var hasHits = _navSearchHits.Count > 0;
        _navSearchStatus.Text = !hasTerm
            ? string.Empty
            : hasHits
                ? $"{_navSearchHitIndex + 1} of {_navSearchHits.Count}"
                : "No matches";
        _navSearchPrev.IsEnabled = hasHits;
        _navSearchNext.IsEnabled = hasHits;
    }

    // Whether a model block's plain text contains at least one match for the term (case-insensitive,
    // whole-word off — the live "search as you type" behaviour), via the shared TextSearch helper.
    private static bool BlockMatches(Block block, string term)
    {
        var text = block switch
        {
            Paragraph paragraph => paragraph.PlainText,
            Table table => TableText(table),
            _ => string.Empty
        };
        return TextSearch.FindAll(text, term, matchCase: false, wholeWord: false).Any();
    }

    // Flatten a table's cell text so a search term inside a table cell still registers as a hit.
    private static string TableText(Table table) =>
        string.Join(" ", table.Rows.SelectMany(row => row.Cells).Select(cell => cell.PlainText));

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

    // The Reveal Formatting pane: a header plus a scrollable, read-only list of the FONT / PARAGRAPH /
    // SECTION formatting in effect at the caret. Collapsed by default; ToggleRevealFormatting shows/hides
    // it. The content is rebuilt on every selection change from the pure RevealFormatting describer (see
    // RefreshRevealFormatting), so the pane never touches the model. Mirrors BuildNavPane's dock/chrome.
    private UIElement BuildRevealPane()
    {
        var header = new TextBlock
        {
            Text = "Reveal Formatting",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(10, 8, 10, 6)
        };

        _revealContent = new StackPanel { Margin = new Thickness(10, 0, 10, 8) };
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _revealContent
        };

        var layout = new DockPanel { Width = 240 };
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(scroll);

        _revealPane = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Visibility = Visibility.Collapsed,
            Child = layout
        };
        return _revealPane;
    }

    // Show/hide the Reveal Formatting pane and keep the View > Reveal Formatting toggle button in sync.
    // Rebuilds the pane's content when it appears so it shows the current selection immediately.
    private void ToggleRevealFormatting()
    {
        _revealPaneVisible = !_revealPaneVisible;
        _revealPane.Visibility = _revealPaneVisible ? Visibility.Visible : Visibility.Collapsed;
        _stateStore.SetChecked("freew.reveal-formatting", _revealPaneVisible);
        if (_revealPaneVisible)
            RefreshRevealFormatting();
    }

    // Rebuild the Reveal Formatting pane from the effective formatting at the caret (run + paragraph +
    // section), via the pure RevealFormatting describer. No-op when the pane is hidden, so selection
    // churn while the pane is closed costs nothing. Read-only: never commits or mutates the model.
    private void RefreshRevealFormatting()
    {
        if (_revealContent is null || !_revealPaneVisible)
            return;

        var sections = RevealFormatting.Describe(
            _editor.CurrentRunFormatting, _editor.CurrentParagraphFormatting, _editor.Model.Page);

        _revealContent.Children.Clear();
        foreach (var section in sections)
        {
            _revealContent.Children.Add(new TextBlock
            {
                Text = section.Heading,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x17, 0x32, 0x4D)),
                Margin = new Thickness(0, 10, 0, 4)
            });

            foreach (var item in section.Items)
            {
                _revealContent.Children.Add(new TextBlock
                {
                    Text = item.Label,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60)),
                    Margin = new Thickness(0, 4, 0, 0)
                });
                _revealContent.Children.Add(new TextBlock
                {
                    Text = item.Value,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(8, 0, 0, 2)
                });
            }
        }
    }

    // The Reviewing Pane: a header, an Accept/Reject/Prev/Next toolbar, a status line ("N changes"), and a
    // scrollable list of every tracked change (author • type, plus the affected text). Collapsed by default;
    // ToggleReviewPane shows/hides it. Selecting an entry jumps the editor to that change (click-to-navigate)
    // and the toolbar acts on the SELECTED single revision. Content is rebuilt from the pure RevisionList
    // (see RefreshReviewPane), so the pane never owns revision logic. Mirrors BuildRevealPane's dock/chrome.
    private UIElement BuildReviewPane()
    {
        var header = new TextBlock
        {
            Text = "Revisions",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(10, 8, 10, 6)
        };

        Button MakeButton(string text, string tip, System.Action onClick)
        {
            var button = new Button
            {
                Content = text,
                ToolTip = tip,
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 0, 4, 0),
                MinWidth = 28
            };
            button.Click += (_, _) => onClick();
            return button;
        }

        var toolbar = new WrapPanel { Margin = new Thickness(10, 0, 10, 6) };
        toolbar.Children.Add(MakeButton("Accept", "Accept the selected change", AcceptSelectedRevision));
        toolbar.Children.Add(MakeButton("Reject", "Reject the selected change", RejectSelectedRevision));
        toolbar.Children.Add(MakeButton("▲", "Previous change (jump up)", () => StepRevision(-1)));
        toolbar.Children.Add(MakeButton("▼", "Next change (jump down)", () => StepRevision(+1)));

        _reviewStatus = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60)),
            Margin = new Thickness(10, 0, 10, 6)
        };

        _reviewList = new ListBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Margin = new Thickness(4, 0, 4, 8)
        };
        // Selecting an entry navigates the editor to that change (click-to-navigate).
        _reviewList.SelectionChanged += (_, _) =>
        {
            var index = _reviewList.SelectedIndex;
            if (index >= 0 && index < _reviewEntries.Count)
                _editor.NavigateToRevision(_reviewEntries[index]);
        };

        var layout = new DockPanel { Width = 260 };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(_reviewStatus, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(toolbar);
        layout.Children.Add(_reviewStatus);
        layout.Children.Add(_reviewList);

        _reviewPane = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Visibility = Visibility.Collapsed,
            Child = layout
        };
        return _reviewPane;
    }

    // Show/hide the Reviewing Pane and keep the Review > Reviewing Pane toggle button in sync. Rebuilds the
    // list when it appears so it reflects the current document immediately.
    private void ToggleReviewPane()
    {
        _reviewPaneVisible = !_reviewPaneVisible;
        _reviewPane.Visibility = _reviewPaneVisible ? Visibility.Visible : Visibility.Collapsed;
        _stateStore.SetChecked("freew.reviewing-pane", _reviewPaneVisible);
        if (_reviewPaneVisible)
            RefreshReviewPane();
    }

    // Rebuild the Reviewing Pane's list from the document's tracked changes (the pure RevisionList via
    // DocumentView.ListRevisions). No-op when the pane is hidden, so editing churn while it is closed costs
    // nothing. Tries to preserve the selected position so Accept/Reject keeps focus on the next change.
    private void RefreshReviewPane()
    {
        if (_reviewList is null || !_reviewPaneVisible)
            return;

        var previousIndex = _reviewList.SelectedIndex;
        _reviewEntries = _editor.ListRevisions();

        _reviewList.Items.Clear();
        foreach (var entry in _reviewEntries)
            _reviewList.Items.Add(BuildRevisionItem(entry));

        _reviewStatus.Text = _reviewEntries.Count switch
        {
            0 => "No tracked changes",
            1 => "1 change",
            var n => $"{n} changes"
        };

        if (_reviewEntries.Count == 0)
            return;
        // Keep the cursor near where it was (the change that slid into the resolved slot, or the last one).
        var next = previousIndex < 0 ? 0 : System.Math.Min(previousIndex, _reviewEntries.Count - 1);
        _reviewList.SelectedIndex = next;
    }

    // One reviewing-pane row: a bold "Author • Type" caption over the affected text (wrapped, dimmed).
    private static UIElement BuildRevisionItem(RevisionEntry entry)
    {
        var verb = entry.Kind switch
        {
            RevisionEntryKind.Insertion => "Inserted",
            RevisionEntryKind.Deletion => "Deleted",
            _ => "Formatted"
        };
        var author = string.IsNullOrWhiteSpace(entry.Author) ? "Unknown" : entry.Author;

        var panel = new StackPanel { Margin = new Thickness(6, 4, 6, 4) };
        panel.Children.Add(new TextBlock
        {
            Text = $"{author} • {verb}",
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x17, 0x32, 0x4D))
        });
        var preview = entry.Text.Replace("\r", " ").Replace("\n", " ").Trim();
        if (preview.Length > 0)
            panel.Children.Add(new TextBlock
            {
                Text = preview,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50))
            });
        return panel;
    }

    // Accept the single revision selected in the Reviewing Pane, then rebuild the list (which slides the
    // selection onto the next pending change). No-op when nothing is selected.
    private void AcceptSelectedRevision()
    {
        var index = _reviewList.SelectedIndex;
        if (index < 0 || index >= _reviewEntries.Count)
            return;
        if (_editor.AcceptRevision(_reviewEntries[index]))
        {
            _file.MarkDirty();
            UpdateCounts();
        }
        RefreshReviewPane();
    }

    // Reject the single revision selected in the Reviewing Pane, then rebuild the list.
    private void RejectSelectedRevision()
    {
        var index = _reviewList.SelectedIndex;
        if (index < 0 || index >= _reviewEntries.Count)
            return;
        if (_editor.RejectRevision(_reviewEntries[index]))
        {
            _file.MarkDirty();
            UpdateCounts();
        }
        RefreshReviewPane();
    }

    // Previous/Next change: step the selection through the list (and so navigate the editor, via the list's
    // SelectionChanged handler). Opens the pane first if it is closed. Wraps at the ends.
    private void StepRevision(int direction)
    {
        if (!_reviewPaneVisible)
            ToggleReviewPane();
        else
            RefreshReviewPane();
        if (_reviewEntries.Count == 0)
            return;

        var current = _reviewList.SelectedIndex;
        var next = current < 0
            ? (direction > 0 ? 0 : _reviewEntries.Count - 1)
            : (current + direction + _reviewEntries.Count) % _reviewEntries.Count;
        _reviewList.SelectedIndex = next;
        _reviewList.ScrollIntoView(_reviewList.SelectedItem);
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

            // Likewise hide the Reveal Formatting pane while reading (its remembered state is untouched).
            _revealPaneVisibleBeforeReadMode = _revealPaneVisible;
            _revealPane.Visibility = Visibility.Collapsed;

            // And hide the Reviewing Pane while reading (its remembered state is untouched).
            _reviewPaneVisibleBeforeReadMode = _reviewPaneVisible;
            _reviewPane.Visibility = Visibility.Collapsed;

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

            // Restore the Reveal Formatting pane to whatever it was before entering read mode.
            _revealPane.Visibility = _revealPaneVisibleBeforeReadMode ? Visibility.Visible : Visibility.Collapsed;

            // Restore the Reviewing Pane to whatever it was before entering read mode.
            _reviewPane.Visibility = _reviewPaneVisibleBeforeReadMode ? Visibility.Visible : Visibility.Collapsed;
        }

        _stateStore.SetChecked("freew.read-mode", _readMode);
    }

    // View > Views: switch the editing surface to one of the three mutually-exclusive print-family view
    // modes — Print Layout (the Word page sheet), Web Layout (a continuous, full-width view with no page
    // chrome, text wrapping to the window like a web page) or Draft (a simplified continuous view for fast
    // editing). DocumentView owns the page presentation; here we drive that, leave any Outline overlay
    // (which replaces the surface) so the chosen view is actually visible, and refresh the ribbon + status
    // bar so exactly one mode reads as active. Switching never mutates the model.
    private void SetViewMode(DocumentViewMode mode)
    {
        // Outline view swaps the whole surface out, so it would hide whichever print-family view is chosen.
        // Picking Print Layout / Web Layout / Draft therefore leaves Outline first (Word's views are all
        // mutually exclusive), mirroring how the ribbon's Views group behaves.
        if (_outlineMode)
            ToggleOutlineView();

        _editor.SetViewMode(mode);
        RefreshViewModeChecks();
    }

    // Push the active print-family view mode into the shared RibbonStateStore (so the View ribbon's Print
    // Layout / Web Layout / Draft toggle buttons reflect it) and the status-bar toggle buttons. Exactly one
    // is checked — unless Outline view is active, in which case none of the three is (Outline owns the
    // surface). Mirrors how the read-mode / nav-pane toggles keep their buttons in sync.
    private void RefreshViewModeChecks()
    {
        var mode = _editor.ViewMode;
        var printLayout = !_outlineMode && mode == DocumentViewMode.PrintLayout;
        var webLayout = !_outlineMode && mode == DocumentViewMode.WebLayout;
        var draft = !_outlineMode && mode == DocumentViewMode.Draft;

        _stateStore.SetChecked("freew.print-layout", printLayout);
        _stateStore.SetChecked("freew.web-layout", webLayout);
        _stateStore.SetChecked("freew.draft-view", draft);

        if (_printLayoutSwitch is not null) _printLayoutSwitch.IsChecked = printLayout;
        if (_webLayoutSwitch is not null) _webLayoutSwitch.IsChecked = webLayout;
        if (_draftSwitch is not null) _draftSwitch.IsChecked = draft;
    }

    // View > Outline: swap the normal editing surface for the heading-structured outline view (and its
    // Outlining mini-toolbar), or back again. Entering hides the workspace + rulers and shows the outline,
    // populated from the live model; exiting restores everything verbatim — the same save/restore shape as
    // Read Mode. Switching views never mutates the model, so toggling back lands on an untouched document.
    // The checked-state is mirrored into the shared RibbonStateStore so the View > Outline button stays in
    // sync, exactly like the Print Layout / Read Mode toggles.
    private void ToggleOutlineView()
    {
        _outlineMode = !_outlineMode;
        if (_outlineMode)
        {
            _hRulerVisibilityBeforeOutline = _hRuler.Visibility;
            _vRulerVisibilityBeforeOutline = _vRuler.Visibility;

            _workspace.Visibility = Visibility.Collapsed;
            _hRuler.Visibility = Visibility.Collapsed;
            _vRuler.Visibility = Visibility.Collapsed;

            _outlineView.Visibility = Visibility.Visible;
            _outlineView.Refresh();
        }
        else
        {
            _outlineView.Visibility = Visibility.Collapsed;
            _workspace.Visibility = Visibility.Visible;
            _hRuler.Visibility = _hRulerVisibilityBeforeOutline;
            _vRuler.Visibility = _vRulerVisibilityBeforeOutline;
        }

        _stateStore.SetChecked("freew.outline-view", _outlineMode);

        // Outline and the print-family views are mutually exclusive: entering Outline clears the Print
        // Layout / Web Layout / Draft checks, and leaving it re-checks whichever the editor is still in.
        RefreshViewModeChecks();
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

        // When a search term is active, narrow the outline to headings that are themselves a match or
        // that own a matching block in their subtree, so the list doubles as a "results in this document"
        // view (Word's navigation-pane search behaviour). With no term the full outline is shown.
        var term = _navSearch?.Text ?? string.Empty;
        if (!string.IsNullOrEmpty(term) && outline.Count > 0)
            outline = FilterOutlineToMatches(outline, term);

        // Repopulate without triggering a navigation jump from the resulting selection reset.
        _navList.SelectionChanged -= OnOutlineSelected;
        _navList.Items.Clear();
        foreach (var entry in outline)
            _navList.Items.Add(new OutlineItem(entry));
        _navList.SelectionChanged += OnOutlineSelected;
    }

    // Keep only the outline entries relevant to the active search: a heading whose own text matches, or
    // one that owns a matching block anywhere in its subtree (OutlineTools.SubtreeRange). Reuses the same
    // TextSearch matching as the document scan so the filtered headings and the Next/Prev hits agree.
    private IReadOnlyList<OutlineEntry> FilterOutlineToMatches(IReadOnlyList<OutlineEntry> outline, string term)
    {
        var blocks = _editor.Model.Blocks;
        var kept = new List<OutlineEntry>(outline.Count);
        foreach (var entry in outline)
        {
            var (start, end) = OutlineTools.SubtreeRange(blocks, entry.BlockIndex);
            var matched = false;
            for (var i = start; i < end && !matched; i++)
                matched = BlockMatches(blocks[i], term);
            if (matched)
                kept.Add(entry);
        }
        return kept;
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
        menu.Items.Add(MoveHeadingMenuItem("Move Up", moveUp: true));
        menu.Items.Add(MoveHeadingMenuItem("Move Down", moveUp: false));
        menu.Items.Add(new Separator());
        menu.Items.Add(OutlineMenuItem("Promote", entry => _editor.PromoteHeading(entry.BlockIndex)));
        menu.Items.Add(OutlineMenuItem("Demote", entry => _editor.DemoteHeading(entry.BlockIndex)));
        menu.Items.Add(new Separator());
        menu.Items.Add(OutlineMenuItem("Collapse", entry => _editor.CollapseHeading(entry.BlockIndex)));
        menu.Items.Add(OutlineMenuItem("Expand", entry => _editor.ExpandHeading(entry.BlockIndex)));
        return menu;
    }

    // A "Move Up / Move Down" context item: relocates the selected heading and its whole subtree by one
    // sibling position via the editor's reversible MoveHeading (OutlineTools.MoveSubtree on the undo/redo
    // bus), then refreshes the outline and re-selects the heading at its new index so it stays highlighted.
    private MenuItem MoveHeadingMenuItem(string header, bool moveUp)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) =>
        {
            if (_navList.SelectedItem is not OutlineItem selected)
                return;
            var newIndex = _editor.MoveHeading(selected.Entry.BlockIndex, moveUp);
            RefreshOutline();
            SelectOutlineEntry(newIndex);
        };
        return item;
    }

    // Select the nav-list row whose entry maps to model block index `blockIndex` (no jump beyond the one
    // the selection already triggers). A no-op when no row matches (e.g. it was filtered out by a search).
    private void SelectOutlineEntry(int blockIndex)
    {
        foreach (var listItem in _navList.Items)
        {
            if (listItem is OutlineItem outlineItem && outlineItem.Entry.BlockIndex == blockIndex)
            {
                _navList.SelectedItem = listItem;
                return;
            }
        }
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
            var button = new Button
            {
                Content = label,
                Style = (Style)FindResource("ChromeStatusButtonStyle"),
                Width = 24,
                Padding = new Thickness(0),
                Margin = new Thickness(2, 3, 2, 3)
            };
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
            Foreground = Brushes.White,
            FontSize = 12,
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
        // The percentage is clickable (Word does this): clicking it opens the Zoom dialog.
        var zoomButton = new Button
        {
            Content = _zoomLabel,
            Style = (Style)FindResource("ChromeStatusButtonStyle"),
            Padding = new Thickness(2, 0, 2, 0),
            ToolTip = "Zoom"
        };
        zoomButton.Click += (_, _) => OpenZoomDialog();
        panel.Children.Add(zoomButton);
        return panel;
    }

    // View > Zoom (and the clickable status-bar percentage): open Word's Zoom dialog. The page-relative fit
    // factors (Page width / Text width / Whole page) are computed from the live workspace viewport and the
    // model page geometry via the pure ZoomFit helper, so "Page width"/"Whole page" honour the real page
    // size + margins. The chosen factor drives DocumentView.ZoomLevel (clamped, shared with the slider).
    private void OpenZoomDialog()
    {
        _editor.CommitToModel();
        var page = _editor.Model.Page;
        var (pageWidthDip, pageHeightDip) = PageLayout.PageSizeDip(page);
        var (contentWidthDip, _) = PageLayout.ContentAreaDip(page);

        // The viewport the page floats in: the grey workspace, minus the editor's own breathing-room margin.
        var margin = _editor.Margin;
        var viewportWidth = Math.Max(0, _workspace.ActualWidth - margin.Left - margin.Right);
        var viewportHeight = Math.Max(0, _workspace.ActualHeight - margin.Top - margin.Bottom);

        var pageWidthFactor = ZoomFit.PageWidth(pageWidthDip, viewportWidth);
        var textWidthFactor = ZoomFit.TextWidth(contentWidthDip, viewportWidth);
        var wholePageFactor = ZoomFit.WholePage(pageWidthDip, pageHeightDip, viewportWidth, viewportHeight);

        var chosen = ZoomDialog.Prompt(this, _editor.ZoomLevel, pageWidthFactor, textWidthFactor, wholePageFactor);
        if (chosen is { } factor)
            _editor.ZoomLevel = factor;
    }

    // QAT Undo / Redo: focus the editing surface and run its built-in (RichTextBox) undo/redo, which is
    // the same inline history Ctrl+Z / Ctrl+Y drive. Guarded by CanUndo/CanRedo so a no-op stays a no-op.
    private void Undo()
    {
        _editor.Focus();
        if (_editor.CanUndo)
            _editor.Undo();
    }

    private void Redo()
    {
        _editor.Focus();
        if (_editor.CanRedo)
            _editor.Redo();
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

    /// <summary>
    /// File &gt; Export: writes the document to a real PDF. Reuses the print pipeline
    /// (<see cref="PrintLayout.BuildPaginator"/>) so the exported pages match Print / Print Preview
    /// exactly (page geometry, header/footer, watermark, border, footnotes), renders them to PDF via
    /// <see cref="PdfExport"/>, and flushes atomically through the shared
    /// <see cref="Free.Shared.Shell.ExportAtomicWriter"/>.
    /// </summary>
    private void ExportToPdf()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export to PDF",
            Filter = "PDF document (*.pdf)|*.pdf",
            DefaultExt = ".pdf",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = _file.DisplayName + ".pdf"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var path = dialog.FileName;
        try
        {
            // Render on the UI thread (walks the WPF visual tree), then write atomically.
            var paginator = PrintLayout.BuildPaginator(_editor);
            var bytes = PdfExport.RenderToBytes(paginator, _file.DisplayName);
            Free.Shared.Shell.ExportAtomicWriter.WriteAllBytes(path, bytes);

            MessageBox.Show(
                this,
                $"Exported to PDF:\n{path}",
                "Export to PDF",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                "The document could not be exported to PDF.\n\n" + ex.Message,
                "Export to PDF",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// File &gt; Export: writes the document to a real XPS package. Reuses the same print pipeline
    /// (<see cref="PrintLayout.BuildPaginator"/>) as Print / Export to PDF so the exported pages match
    /// exactly (page geometry, header/footer, watermark, border, footnotes), serialises them as vector
    /// glyph runs via <see cref="XpsExport"/>, and flushes atomically through the shared
    /// <see cref="Free.Shared.Shell.ExportAtomicWriter"/>.
    /// </summary>
    private void ExportToXps()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export to XPS",
            Filter = "XPS document (*.xps)|*.xps",
            DefaultExt = ".xps",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = _file.DisplayName + ".xps"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var path = dialog.FileName;
        try
        {
            // Render on the UI thread (walks the WPF visual tree), then write atomically.
            var paginator = PrintLayout.BuildPaginator(_editor);
            var bytes = XpsExport.RenderToBytes(paginator);
            Free.Shared.Shell.ExportAtomicWriter.WriteAllBytes(path, bytes);

            MessageBox.Show(
                this,
                $"Exported to XPS:\n{path}",
                "Export to XPS",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                "The document could not be exported to XPS.\n\n" + ex.Message,
                "Export to XPS",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
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

    private void OpenProperties()
    {
        var dialog = new PropertiesDialog(this, _editor.Model.Properties);
        if (dialog.ShowDialog() == true)
            _file.MarkDirty();
    }

    // Opens the modal FreeW Options editor. On OK it applies the edited settings live (by mutating the
    // shared _options instance FileCommands reads) and persists them through the shared JsonSettingsStore
    // so they survive a restart. Save is best-effort — a failure surfaces a message but never throws.
    private void OpenOptions()
    {
        var dialog = new OptionsDialog(this, _options);
        if (dialog.ShowDialog() != true)
            return;

        var edited = dialog.Result;
        _options.RecentFilesCap = edited.RecentFilesCap;
        _options.DefaultSaveFormat = edited.DefaultSaveFormat;
        _options.UiLanguage = edited.UiLanguage;
        _options.AutoCorrectEnabled = edited.AutoCorrectEnabled;
        _options.AutoFormat = edited.AutoFormat;
        _options.AutoCorrect = edited.AutoCorrect;
        _options.Normalize();
        ApplyAutoFormatOptions();

        if (!_optionsStore.Save(_options))
            DialogMessageHelper.ShowError(this, _optionsStore.LastError, "FreeW Options");
    }

    // Push the persisted AutoCorrect master switch + per-rule AutoFormat toggles onto the live editor so the
    // as-you-type rules honour the user's settings immediately (called at construction and after Options OK).
    private void ApplyAutoFormatOptions()
    {
        _editor.AutoCorrectEnabled = _options.AutoCorrectEnabled;
        _editor.AutoFormatOptions = _options.AutoFormat ?? AutoFormatOptions.Default;
        _editor.AutoCorrectOptions = _options.AutoCorrect ?? AutoCorrectOptions.Default;
    }

    // Shows that AppProduct = "FreeW" routes the shared storage helpers to FreeW's own folder.
    private static string ResolveDataFolderLabel()
        => AppStoragePathPlanner.GetOptionsFilePathLabelOrFallback(PlatformApplicationDataPathProvider.LocalInstance);

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

        // Flat Word/FreeX ribbon tabs come from the shared library so any app on the shared ribbon gets
        // the look automatically (no border, transparent headers, hover wash, selected tab filled white
        // with a colored accent underline). See RibbonTabControlFactory.
        var result = RibbonShellBuilder.Build(new RibbonShellBuildSpec(
            definition,
            registry,
            stateStore,
            FileTabHeader: "File",
            FileTabAccent: Color.FromRgb(0x0F, 0x6D, 0x8C),
            FileTabHover: Color.FromRgb(0x0B, 0x55, 0x6E),
            ShowBackstage)
        {
            EnableContextualTabs = true,
            ResourceDictionaries =
            [
                new ResourceDictionary
                {
                    Source = new Uri("/FreeW.App.Host;component/Ribbon/FreeWRibbonResources.xaml", UriKind.Relative)
                }
            ],
            CustomizeTabContent = (tab, content) =>
            {

        // The renderer resolves its button/group styles and surface brushes via TryFindResource on the
        // supplied resource host. Merge FreeW's ribbon styles into the TabControl so those lookups
        // resolve (the renderer falls back gracefully for any key it can't find).
        // ── File tab (Word-style): the FIRST ribbon tab, rendered as an accent-coloured pill. Selecting it
        //    opens the Backstage overlay rather than swapping the ribbon body to an empty tab. Like FreeX,
        //    the File tab never *stays* selected: the SelectionChanged handler shows the Backstage and
        //    immediately reverts the selection to the previously-active content tab (index 1 = Home).
        // Contextual "Tools" tabs (Picture Format / table tabs) are declared in the ribbon model and
        // managed by the shared controller — hidden until their selection context is active. Default revert
        // tab is Home (index 1; index 0 is the File pill).
            // V5 galleries: inject the live-preview Word-style galleries into the rendered group content.
            // The shared renderer stamps each group's grid with its catalog id (RibbonMetadata.CatalogId),
            // so we find the target group and prepend a custom gallery control into its content lane. This
            // keeps the galleries entirely app-side (custom WPF content) without a shared RibbonGallery type.
            if (tab.Id == "home")
                // Drop the placeholder Style combo (the gallery supersedes it) but keep the group's
                // New Style / Manage Styles buttons, prepending the live-preview gallery before them.
                InjectGallery(content, "styles", StylesGallery.Build(_editor), removeKind: RemoveKind.Combos);
            if (tab.Id == "design")
                // Replace the placeholder Themes combo with gallery previews, but keep the backed
                // Colors dropdown beside them so the group matches Word's Document Formatting shape.
                InjectGallery(content, "themes", ThemeGallery.BuildThemes(_editor), removeKind: RemoveKind.Combos,
                    extra: ThemeGallery.BuildColours(_editor));

            }
        });

        // Start on Home (index 1; index 0 is the File tab). Remember it as the last "real" tab so File
        // selection can revert to it.
        _fileTab = result.FileTab;
        _fileTabRouter = result.FileTabRouter;
        _contextualTabs = result.ContextualTabs!;
        return (result.Root, result.Tabs);
    }

    // The accent-coloured File tab style (Word's blue File button look): a solid accent fill with white
    // text, a darker hover/press, comfortable padding. Distinct from the flat content-tab headers so it
    // reads as the Backstage entry point. Authored in code to keep parity with the code-only shell.
    // What of a group's original rendered controls to drop before injecting a gallery.
    private enum RemoveKind { None, Combos, All }

    // Find the group grid carrying CatalogId == groupId in the freshly built tab content and prepend the
    // gallery into its content lane (row 0). `removeKind` controls which of the group's original
    // placeholder controls are removed first: All clears the lane (the gallery fully owns the group);
    // Combos drops only ComboBox columns (so a placeholder combo the gallery supersedes goes away while
    // command buttons like New Style / Manage Styles remain). An optional `extra` gallery is appended
    // after the first (e.g. the Design Colors strip).
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
