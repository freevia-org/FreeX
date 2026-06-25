using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FreeW.App.Avalonia.Editing;
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
public sealed class NavigationPane : UserControl
{
    private const double IndentPerLevel = 12.0;

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly DocumentView _editor;
    private readonly ListBox _headingList;
    private readonly TextBox _searchBox;
    private readonly Button _prevButton;
    private readonly Button _nextButton;
    private readonly TextBlock _searchStatus;

    /// <summary>Block indices (document order) where the current search term matches.</summary>
    private readonly List<int> _searchHits = new();

    /// <summary>Current position within <see cref="_searchHits"/> (-1 = no active result).</summary>
    private int _searchHitIndex = -1;

    // ── Public surface ────────────────────────────────────────────────────────

    /// <summary>
    /// The <see cref="ScrollViewer"/> wrapping the <see cref="DocumentView"/>. When set, heading clicks
    /// and search navigation scroll the viewer to bring the target block into view.
    /// </summary>
    public ScrollViewer? ScrollerRef { get; set; }

    // ── Construction ──────────────────────────────────────────────────────────

    public NavigationPane(DocumentView editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));

        // --- Heading list ---------------------------------------------------
        _headingList = new ListBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
        };
        _headingList.SelectionChanged += OnHeadingSelected;

        // --- Search box -----------------------------------------------------
        _searchBox = new TextBox
        {
            PlaceholderText = "Search document",
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
        ToolTip.SetTip(_prevButton, "Previous match");
        _prevButton.Click += (_, _) => StepSearch(forward: false);

        _nextButton = new Button
        {
            Content = "›",
            Width = 24,
            Padding = new Thickness(0),
            Margin = new Thickness(2, 0, 0, 0),
            IsEnabled = false,
        };
        ToolTip.SetTip(_nextButton, "Next match");
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

        // --- Header ---------------------------------------------------------
        var header = new TextBlock
        {
            Text = "Navigation",
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            Padding = new Thickness(8, 6),
        };

        // --- Root layout ----------------------------------------------------
        //   [header]           Dock.Top
        //   [searchArea]       Dock.Top
        //   [headingList]      fill remainder
        var layout = new DockPanel { Width = 240 };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(searchArea, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(searchArea);
        layout.Children.Add(_headingList);

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = layout,
        };

        IsVisible = false; // hidden by default; toggled by View ribbon command
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Rebuild the heading list from the editor's current document. Call whenever the document
    /// changes (wire to <see cref="DocumentView.DocumentChanged"/>).
    /// </summary>
    public void Refresh()
    {
        var outline = DocumentOutline.Of(_editor.Document);
        var term = _searchBox.Text ?? string.Empty;

        IReadOnlyList<OutlineEntry> displayed = outline;
        if (!string.IsNullOrEmpty(term) && outline.Count > 0)
            displayed = FilterToMatches(outline, term);

        // Suspend SelectionChanged while rebuilding so we don't fire scroll on repopulate.
        _headingList.SelectionChanged -= OnHeadingSelected;
        _headingList.Items.Clear();
        foreach (var entry in displayed)
            _headingList.Items.Add(new OutlineItem(entry));
        _headingList.SelectionChanged += OnHeadingSelected;
    }

    // ── Heading-click handler ─────────────────────────────────────────────────

    private void OnHeadingSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_headingList.SelectedItem is OutlineItem item)
            ScrollEditorToBlock(item.Entry.BlockIndex);
    }

    // ── Search logic ──────────────────────────────────────────────────────────

    private void RunSearch()
    {
        _searchHits.Clear();
        _searchHitIndex = -1;

        var term = _searchBox.Text ?? string.Empty;
        if (!string.IsNullOrEmpty(term))
        {
            var blocks = _editor.Document.Blocks;
            for (var i = 0; i < blocks.Count; i++)
            {
                var text = blocks[i] is Paragraph p ? p.PlainText : string.Empty;
                if (BlockContainsTerm(text, term))
                    _searchHits.Add(i);
            }

            if (_searchHits.Count > 0)
            {
                _searchHitIndex = 0;
                ScrollEditorToBlock(_searchHits[0]);
            }
        }

        Refresh();         // re-filter heading list when term is active
        UpdateSearchStatus();
    }

    private void StepSearch(bool forward)
    {
        if (_searchHits.Count == 0)
            return;

        _searchHitIndex = forward
            ? (_searchHitIndex + 1) % _searchHits.Count
            : (_searchHitIndex - 1 + _searchHits.Count) % _searchHits.Count;

        ScrollEditorToBlock(_searchHits[_searchHitIndex]);
        UpdateSearchStatus();
    }

    private void UpdateSearchStatus()
    {
        var hasTerm = !string.IsNullOrEmpty(_searchBox.Text);
        var hasHits = _searchHits.Count > 0;
        _searchStatus.Text = !hasTerm
            ? string.Empty
            : hasHits ? $"{_searchHitIndex + 1} of {_searchHits.Count}" : "No matches";
        _prevButton.IsEnabled = hasHits;
        _nextButton.IsEnabled = hasHits;
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

    // ── Outline filter ────────────────────────────────────────────────────────

    /// <summary>
    /// Keeps only entries whose heading text contains the term (case-insensitive), plus ancestor
    /// headings that sit above any matching entry in the hierarchy.
    /// </summary>
    private static IReadOnlyList<OutlineEntry> FilterToMatches(IReadOnlyList<OutlineEntry> outline, string term)
    {
        var matched = new bool[outline.Count];
        for (var i = 0; i < outline.Count; i++)
            matched[i] = BlockContainsTerm(outline[i].Text, term);

        // Include shallower ancestors of each matched entry.
        for (var i = 0; i < outline.Count; i++)
        {
            if (!matched[i])
                continue;
            var depth = outline[i].Level;
            for (var j = i - 1; j >= 0 && depth > 0; j--)
            {
                if (outline[j].Level < depth)
                {
                    matched[j] = true;
                    depth = outline[j].Level;
                }
            }
        }

        var result = new List<OutlineEntry>(outline.Count);
        for (var i = 0; i < outline.Count; i++)
            if (matched[i])
                result.Add(outline[i]);

        return result;
    }

    private static bool BlockContainsTerm(string text, string term) =>
        text.Contains(term, StringComparison.OrdinalIgnoreCase);

    // ── Test-support properties ───────────────────────────────────────────────

    /// <summary>Number of heading items currently shown in the list (for headless testing).</summary>
    internal int HeadingItemCount => _headingList.Items.Count;

    /// <summary>
    /// Counts how many entries in <paramref name="doc"/>'s outline match <paramref name="term"/>
    /// (case-insensitive), including ancestor headings — the same logic as <see cref="FilterToMatches"/>.
    /// Exposed for headless tests only.
    /// </summary>
    internal int CountHeadingsMatching(string term, TextDocument doc)
    {
        var outline = DocumentOutline.Of(doc);
        if (string.IsNullOrEmpty(term) || outline.Count == 0)
            return outline.Count;
        return FilterToMatches(outline, term).Count;
    }

    // ── Item type ─────────────────────────────────────────────────────────────

    /// <summary>
    /// List item holding an <see cref="OutlineEntry"/>. The <see cref="ToString"/> provides the
    /// display label; <see cref="Indent"/> exposes the pixel indent used for hierarchy depth.
    /// </summary>
    internal sealed class OutlineItem
    {
        public OutlineItem(OutlineEntry entry) => Entry = entry;

        public OutlineEntry Entry { get; }

        /// <summary>Left indent in pixels for this heading level.</summary>
        public double Indent => Entry.Level * IndentPerLevel;

        public override string ToString() => Entry.Text;
    }
}
