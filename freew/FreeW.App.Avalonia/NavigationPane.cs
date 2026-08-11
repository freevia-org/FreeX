using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Panes;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>
/// FreeW Avalonia navigation pane: heading outline + document search. Mirrors the WPF host's
/// <c>BuildNavPane()</c>/<c>BuildNavSearch()</c> behaviour using Avalonia controls. Consumes
/// <see cref="DocumentOutline"/> (heading list) and <see cref="TextSearch"/> (find-in-pane) from the
/// portable model tier; does NOT duplicate any model logic.
///
/// Construction: pass the <see cref="DocumentView"/> once. After construction, assign
/// <see cref="ScrollerRef"/> to the <see cref="ScrollViewer"/> wrapping the editor, and wire
/// <see cref="DocumentView.DocumentChanged"/> to call <see cref="Refresh"/>. Toggle
/// <see cref="IsVisible"/> via the View ribbon; it defaults to hidden.
/// </summary>
public sealed class NavigationPane : SidePaneBase
{
    private const double IndentPerLevel = 12.0;

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly ListBox _headingList;
    private readonly TextBox _searchBox;
    private readonly Button _prevButton;
    private readonly Button _nextButton;
    private readonly TextBlock _searchStatus;
    private readonly NavigationPaneSession _session;

    // ── Public surface ────────────────────────────────────────────────────────

    /// <summary>
    /// The <see cref="ScrollViewer"/> wrapping the <see cref="DocumentView"/>. When set, heading clicks
    /// and search navigation scroll the viewer to bring the target block into view.
    /// </summary>
    public ScrollViewer? ScrollerRef { get; set; }

    // ── Construction ──────────────────────────────────────────────────────────

    public NavigationPane(DocumentView editor)
        : base(editor, NavigationPaneTextCatalog.Resolve(UiText.Get).Title, width: 240, chromeBorderThickness: new Thickness(0, 0, 1, 0), includeSeparator: false)
    {
        var text = NavigationPaneTextCatalog.Resolve(UiText.Get);
        _session = new NavigationPaneSession(
            () => editor.Document,
            new NavigationPaneMutationActions(
                editor.MoveHeading,
                editor.PromoteHeading,
                editor.DemoteHeading,
                editor.CollapseHeading,
                editor.ExpandHeading,
                editor.IsHeadingCollapsed),
            text);

        // --- Heading list ---------------------------------------------------
        _headingList = new ListBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
        };
        _headingList.SelectionChanged += OnHeadingSelected;
        _headingList.KeyDown += OnHeadingListKeyDown;
        RefreshHeadingContextMenu();

        // --- Search box -----------------------------------------------------
        _searchBox = new TextBox
        {
            PlaceholderText = text.SearchDocument,
            Margin = new Thickness(8, 4, 8, 2),
            Padding = new Thickness(4, 2),
        };
        _searchBox.TextChanged += (_, _) => RunSearch();
        _searchBox.KeyDown += (_, e) =>
        {
            // Enter = next match; Shift+Enter = previous — mirrors WPF nav search.
            if (e.Key == Key.Enter)
            {
                StepSearch(forward: (e.KeyModifiers & KeyModifiers.Shift) == 0);
                e.Handled = true;
            }
        };

        // --- Previous / Next buttons ----------------------------------------
        _prevButton = new Button
        {
            Content = "‹",
            Width = 24,
            Padding = new Thickness(0),
            IsEnabled = false,
        };
        ToolTip.SetTip(_prevButton, text.PreviousMatch);
        _prevButton.Click += (_, _) => StepSearch(forward: false);

        _nextButton = new Button
        {
            Content = "›",
            Width = 24,
            Padding = new Thickness(0),
            Margin = new Thickness(2, 0, 0, 0),
            IsEnabled = false,
        };
        ToolTip.SetTip(_nextButton, text.NextMatch);
        _nextButton.Click += (_, _) => StepSearch(forward: true);

        // --- Match count status text ----------------------------------------
        _searchStatus = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
            FontSize = 11,
        };

        // DockPanel: prev › next › status (left-aligned, fills remaining width)
        var searchControls = new DockPanel { Margin = new Thickness(8, 0, 8, 4) };
        DockPanel.SetDock(_prevButton, Dock.Left);
        DockPanel.SetDock(_nextButton, Dock.Left);
        searchControls.Children.Add(_prevButton);
        searchControls.Children.Add(_nextButton);
        searchControls.Children.Add(_searchStatus);

        var searchArea = new StackPanel();
        searchArea.Children.Add(_searchBox);
        searchArea.Children.Add(searchControls);

        // Dock search area and heading list into the shared InnerLayout.
        // InnerLayout already contains the header (Dock.Top). Add:
        //   [searchArea]   Dock.Top
        //   [headingList]  fill remainder
        DockPanel.SetDock(searchArea, Dock.Top);
        InnerLayout.Children.Add(searchArea);
        InnerLayout.Children.Add(_headingList);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Rebuild the heading list from the editor's current document. Call whenever the document
    /// changes (wire to <see cref="DocumentView.DocumentChanged"/>).
    /// </summary>
    public override void Refresh()
    {
        Render(_session.Refresh());
    }

    private void Render(NavigationPaneOutcome outcome)
    {
        var state = outcome.State;
        _headingList.SelectionChanged -= OnHeadingSelected;
        _headingList.Items.Clear();
        foreach (var heading in state.Headings)
            _headingList.Items.Add(new OutlineItem(heading));
        _headingList.SelectedIndex = state.SelectedHeadingBlockIndex is { } blockIndex
            ? state.Headings.ToList().FindIndex(heading => heading.BlockIndex == blockIndex)
            : -1;
        _headingList.SelectionChanged += OnHeadingSelected;

        _searchStatus.Text = state.SearchStatusText;
        _prevButton.IsEnabled = state.CanStepSearch;
        _nextButton.IsEnabled = state.CanStepSearch;
        RefreshHeadingContextMenu();

        if (outcome.NavigateToBlockIndex is { } target)
            ScrollEditorToBlock(target);
    }

    // ── Heading-click handler ─────────────────────────────────────────────────

    private void OnHeadingSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_headingList.SelectedItem is OutlineItem item)
            Render(_session.SelectHeading(item.Entry.BlockIndex));
        RefreshHeadingContextMenu();
    }

    private void RefreshHeadingContextMenu()
    {
        var menu = AvaloniaContextMenuRenderer.BuildContextMenu(
            _session.BuildOutlineMenu(),
            ExecuteOutlineContextCommand);
        menu.Opened += (_, _) => menu.Items.OfType<MenuItem>().FirstOrDefault(item => item.IsEnabled)?.Focus();
        _headingList.ContextMenu = menu;
    }

    private void OnHeadingListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Apps && (e.Key != Key.F10 || e.KeyModifiers != KeyModifiers.Shift))
            return;
        e.Handled = true;
        RefreshHeadingContextMenu();
        _headingList.ContextMenu?.Open(_headingList);
    }

    private void ExecuteOutlineContextCommand(RibbonCommandId commandId)
    {
        Render(_session.ExecuteOutlineCommand(commandId));
    }

    // ── Search logic ──────────────────────────────────────────────────────────

    private void RunSearch()
    {
        Render(_session.SetQuery(_searchBox.Text));
    }

    private void StepSearch(bool forward)
    {
        Render(_session.StepSearch(forward ? 1 : -1));
    }

    // ── Scroll helper ─────────────────────────────────────────────────────────

    /// <summary>
    /// Scrolls the wrapping <see cref="ScrollViewer"/> so that <paramref name="blockIndex"/> is
    /// visible near the top of the viewport. Uses <see cref="DocumentView.GetBlockTop"/> to obtain the
    /// block's layout Y coordinate without touching any model internals.
    /// </summary>
    private void ScrollEditorToBlock(int blockIndex)
    {
        if (ScrollerRef is not { } scroller)
            return;

        var y = _editor.GetBlockTop(blockIndex);
        if (y < 0)
            return;

        var target = Math.Max(0, y - 40);
        scroller.Offset = new Vector(scroller.Offset.X, target);
    }

    // ── Test-support properties ───────────────────────────────────────────────

    /// <summary>Number of heading items currently shown in the list (for headless testing).</summary>
    internal int HeadingItemCount => _headingList.Items.Count;

    /// <summary>
    /// Counts how many entries in <paramref name="doc"/>'s outline match <paramref name="term"/>
    /// through the shared navigation-pane projection. Exposed for headless tests only.
    /// </summary>
    internal int CountHeadingsMatching(string term, TextDocument doc) =>
        NavigationPaneSession.ProjectHeadings(doc, term).Count;

    // ── Item type ─────────────────────────────────────────────────────────────

    /// <summary>
    /// List item holding a shared heading projection. The <see cref="ToString"/> provides the
    /// display label; <see cref="Indent"/> exposes the pixel indent used for hierarchy depth.
    /// </summary>
    internal sealed class OutlineItem
    {
        public OutlineItem(NavigationHeadingProjection entry) => Entry = entry;

        public NavigationHeadingProjection Entry { get; }

        /// <summary>Left indent in pixels for this heading level.</summary>
        public double Indent => Entry.Level * IndentPerLevel;

        public override string ToString() => Entry.Text;
    }
}
