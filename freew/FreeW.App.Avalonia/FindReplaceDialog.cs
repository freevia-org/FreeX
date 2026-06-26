using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>
/// FreeW Avalonia Find &amp; Replace dialog: a modeless <see cref="Window"/> (non-blocking) with Find /
/// Replace fields, match-case / whole-word / wildcard checkboxes, Find Next / Replace / Replace All
/// buttons, and a Go To section (headings via <see cref="DocumentOutline"/> + document start/end).
///
/// Text matching is delegated entirely to the pure <see cref="TextSearch"/> helper in the model tier;
/// no matching logic lives here. Navigation (Find Next, Go To) uses the editor's
/// <see cref="DocumentView.FindNext"/> / <see cref="DocumentView.GetBlockTop"/> surface so the editor
/// controls the caret and scroll.
///
/// Options supported: Match Case, Whole Word, Use Wildcards (per <see cref="TextSearch.FindAll"/>).
/// "Use Wildcards" disables "Whole Word" (incompatible, matching the WPF dialog's behaviour).
///
/// The inline find bar in MainWindow continues to work; the dialog is opened via a separate
/// <c>freew.find-replace-dialog</c> ribbon command (Home → Editing group) or Ctrl+H.
/// </summary>
public sealed class FindReplaceDialog : Window
{
    // ── Editor reference ──────────────────────────────────────────────────────

    private readonly DocumentView _editor;

    // ── Controls ──────────────────────────────────────────────────────────────

    private readonly TextBox _findBox = new()
    {
        MinWidth = 220,
        PlaceholderText = "Search text…",
        Margin = new Thickness(0, 6, 0, 0),
    };

    private readonly TextBox _replaceBox = new()
    {
        MinWidth = 220,
        PlaceholderText = "Replacement text…",
        Margin = new Thickness(0, 6, 0, 0),
    };

    private readonly CheckBox _matchCase = new()
    {
        Content = "Match case",
        Margin = new Thickness(0, 8, 0, 0),
    };

    private readonly CheckBox _wholeWord = new()
    {
        Content = "Whole word",
        Margin = new Thickness(0, 4, 0, 0),
    };

    private readonly CheckBox _useWildcards = new()
    {
        Content = "Use wildcards  (* ? [ ] < >)",
        Margin = new Thickness(0, 4, 0, 0),
    };

    private readonly ComboBox _goToTarget = new()
    {
        MinWidth = 220,
        Margin = new Thickness(0, 6, 0, 0),
    };

    private readonly TextBlock _status = new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60)),
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 8, 0, 0),
    };

    // ── Construction ──────────────────────────────────────────────────────────

    public FindReplaceDialog(DocumentView editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));

        Title = "Find & Replace";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        // "Use Wildcards" disables "Whole Word" (incompatible — mirrors WPF dialog).
        _useWildcards.IsCheckedChanged += (_, _) =>
            _wholeWord.IsEnabled = _useWildcards.IsChecked != true;

        // --- Main grid (Find label | Find box, Replace label | Replace box) ------
        var grid = new Grid { Margin = new Thickness(14, 10, 14, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Row 0: Find:
        AddLabeledRow(grid, 0, "Find:", _findBox);
        // Row 1: Replace:
        AddLabeledRow(grid, 1, "Replace:", _replaceBox);

        // Row 2-4: checkboxes (span both columns)
        foreach (var (chk, row) in new[] { (_matchCase, 2), (_wholeWord, 3), (_useWildcards, 4) })
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(chk, row);
            Grid.SetColumn(chk, 1);
            grid.Children.Add(chk);
        }

        // --- Action buttons ---------------------------------------------------
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(14, 10, 14, 0),
        };
        btnRow.Children.Add(MakeButton("Find Previous", (_, _) => FindPrevious()));
        btnRow.Children.Add(MakeButton("Find Next", (_, _) => FindNext()));
        btnRow.Children.Add(MakeButton("Replace", (_, _) => Replace()));
        btnRow.Children.Add(MakeButton("Replace All", (_, _) => ReplaceAll()));
        btnRow.Children.Add(MakeButton("Close", (_, _) => Close()));

        // --- Go To section ---------------------------------------------------
        var goToSection = BuildGoToSection();

        // --- Status bar -------------------------------------------------------
        var statusHost = new Border
        {
            Margin = new Thickness(14, 4, 14, 10),
            Child = _status,
        };

        // --- Outer stack ------------------------------------------------------
        var outer = new StackPanel();
        outer.Children.Add(grid);
        outer.Children.Add(btnRow);
        outer.Children.Add(goToSection);
        outer.Children.Add(statusHost);

        Content = outer;

        // Keyboard: Enter = Find Next in find box, Escape = close dialog.
        _findBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { FindNext(); e.Handled = true; }
            else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        };
        _replaceBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        };
    }

    protected override void OnClosed(EventArgs e)
    {
        // Remove the find-all highlight when the dialog goes away so stale washes don't linger.
        _editor.ClearFindHighlights();
        base.OnClosed(e);
    }

    // ── Go To section ─────────────────────────────────────────────────────────

    private Panel BuildGoToSection()
    {
        var panel = new StackPanel { Margin = new Thickness(14, 6, 14, 0) };

        panel.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            Margin = new Thickness(0, 6, 0, 6),
        });

        panel.Children.Add(new TextBlock
        {
            Text = "Go to:",
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 4),
        });

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(_goToTarget, 0);
        row.Children.Add(_goToTarget);

        var goBtn = MakeButton("Go", (_, _) => GoTo());
        goBtn.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(goBtn, 1);
        row.Children.Add(goBtn);

        panel.Children.Add(row);

        // Populate initially and again each time the drop-down opens (document may have changed).
        PopulateGoToTargets();
        _goToTarget.DropDownOpened += (_, _) => PopulateGoToTargets();

        return panel;
    }

    private readonly record struct GoToItem(int BlockIndex, string Label)
    {
        public override string ToString() => Label;
    }

    private void PopulateGoToTargets()
    {
        var prevIndex = _goToTarget.SelectedIndex;
        var items = new List<GoToItem>
        {
            new(-1, "Document start"),
            new(int.MaxValue, "Document end"),
        };

        foreach (var entry in DocumentOutline.Of(_editor.Document))
        {
            var text = string.IsNullOrWhiteSpace(entry.Text) ? "(untitled heading)" : entry.Text;
            var indent = new string(' ', entry.Level * 2);
            items.Add(new GoToItem(entry.BlockIndex, $"{indent}{text}"));
        }

        _goToTarget.ItemsSource = items;
        _goToTarget.SelectedIndex = prevIndex >= 0 && prevIndex < items.Count ? prevIndex : 0;
    }

    private void GoTo()
    {
        if (_goToTarget.SelectedItem is not GoToItem item)
            return;

        int blockIndex;
        string label;
        if (item.BlockIndex == -1)
        {
            // Jump to the first block.
            blockIndex = 0;
            label = "Document start";
        }
        else if (item.BlockIndex == int.MaxValue)
        {
            // Jump to the last block.
            blockIndex = Math.Max(0, _editor.Document.Blocks.Count - 1);
            label = "Document end";
        }
        else
        {
            blockIndex = item.BlockIndex;
            label = item.Label.Trim();
        }

        ScrollEditorToBlock(blockIndex);
        _editor.Focus();
        _status.Text = $"Jumped to {label}.";
    }

    // ── Find / Replace logic ──────────────────────────────────────────────────

    private bool MatchCase => _matchCase.IsChecked == true;
    private bool WholeWord => _wholeWord.IsChecked == true;
    private bool UseWildcards => _useWildcards.IsChecked == true;

    private DocumentView.FindOptions Options => new(MatchCase, WholeWord);

    /// <summary>
    /// Re-runs FindAll for the current term/options so the editor highlights every match, and
    /// reports the total count plus the 1-based ordinal of the active match ("3 of 7"). Called after
    /// every Find Next / Find Previous and whenever options change.
    /// </summary>
    private void RefreshHighlights(string term)
    {
        if (term.Length == 0 || UseWildcards)
        {
            // Wildcard highlighting goes through the dialog's own counter (the editor highlight uses
            // plain match-case/whole-word options only); clear the editor highlight in that case.
            _editor.ClearFindHighlights();
            return;
        }

        var total = _editor.FindAll(term, Options);
        var ordinal = _editor.CurrentFindOrdinal(term, Options);
        if (total == 0)
            _status.Text = $"\"{term}\" not found.";
        else if (ordinal > 0)
            _status.Text = $"{ordinal} of {total}";
        else
            _status.Text = $"{total} match{(total == 1 ? "" : "es")}";
    }

    private void FindNext()
    {
        var term = _findBox.Text ?? string.Empty;
        if (term.Length == 0)
        {
            _status.Text = "Enter a search term.";
            return;
        }

        var found = UseWildcards
            ? FindWildcardNext(term)
            : _editor.FindNext(term, Options);

        if (found)
            RefreshHighlights(term);
        else
            _status.Text = $"\"{term}\" not found.";
    }

    private void FindPrevious()
    {
        var term = _findBox.Text ?? string.Empty;
        if (term.Length == 0)
        {
            _status.Text = "Enter a search term.";
            return;
        }

        // Wildcard previous reuses the forward path (rare); plain path supports true backwards search.
        var found = UseWildcards ? FindWildcardNext(term) : _editor.FindPrevious(term, Options);
        if (found)
            RefreshHighlights(term);
        else
            _status.Text = $"\"{term}\" not found.";
    }

    /// <summary>
    /// Wildcard Find Next: scans the document via <see cref="TextSearch.FindAll"/> (which understands
    /// the Word wildcard grammar) and selects the first match in document order. The editor's own
    /// FindNext only does literal matching, so wildcard navigation is driven from the dialog.
    /// </summary>
    private bool FindWildcardNext(string term)
    {
        var blocks = _editor.Document.Blocks;
        for (var bi = 0; bi < blocks.Count; bi++)
        {
            if (blocks[bi] is not Paragraph p)
                continue;
            var hit = TextSearch.FindAll(p.PlainText, term, MatchCase, WholeWord, useWildcards: true).FirstOrDefault();
            if (hit.Length > 0)
            {
                _editor.SelectRange(bi, hit.Start, hit.Length);
                return true;
            }
        }
        return false;
    }

    private void Replace()
    {
        var term = _findBox.Text ?? string.Empty;
        var replacement = _replaceBox.Text ?? string.Empty;
        if (term.Length == 0)
            return;

        if (!_editor.ReplaceNext(term, replacement, Options))
            _status.Text = $"\"{term}\" not found.";
        else
            RefreshHighlights(term);
    }

    private void ReplaceAll()
    {
        var term = _findBox.Text ?? string.Empty;
        var replacement = _replaceBox.Text ?? string.Empty;
        if (term.Length == 0)
            return;

        var count = _editor.ReplaceAll(term, replacement, Options);
        _status.Text = count == 0
            ? $"\"{term}\" not found."
            : $"Replaced {count} occurrence{(count == 1 ? "" : "s")}.";
    }

    // ── Scroll helper (mirrors NavigationPane.ScrollEditorToBlock) ────────────

    /// <summary>
    /// Scrolls the <see cref="ScrollViewer"/> that wraps the editor so that
    /// <paramref name="blockIndex"/> is visible near the top of the viewport. Set via
    /// <see cref="ScrollerRef"/> after construction (wired by MainWindow).
    /// </summary>
    public ScrollViewer? ScrollerRef { get; set; }

    private void ScrollEditorToBlock(int blockIndex)
    {
        if (ScrollerRef is not { } scroller)
            return;
        var y = _editor.GetBlockTop(blockIndex);
        if (y < 0)
            return;
        scroller.Offset = new Vector(scroller.Offset.X, Math.Max(0, y - 40));
    }

    // ── Layout helpers ────────────────────────────────────────────────────────

    private static void AddLabeledRow(Grid grid, int row, string label, Control field)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var lbl = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 6, 8, 0),
        };
        Grid.SetRow(lbl, row);
        Grid.SetColumn(lbl, 0);
        grid.Children.Add(lbl);

        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        grid.Children.Add(field);
    }

    private static Button MakeButton(string content, EventHandler<RoutedEventArgs> onClick)
    {
        var btn = new Button
        {
            Content = content,
            MinWidth = 84,
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(6, 3, 6, 3),
        };
        btn.Click += onClick;
        return btn;
    }

    // ── Test-support ──────────────────────────────────────────────────────────

    /// <summary>
    /// Counts all matches of <paramref name="term"/> in <paramref name="doc"/> using the given
    /// options. Exposed for headless tests so the matching logic can be verified without showing
    /// the dialog or needing an Avalonia backend.
    /// </summary>
    internal static int CountMatches(
        TextDocument doc, string term,
        bool matchCase = false, bool wholeWord = false, bool useWildcards = false)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (string.IsNullOrEmpty(term))
            return 0;

        var count = 0;
        foreach (var block in doc.Blocks)
        {
            if (block is not Paragraph p)
                continue;
            count += TextSearch.FindAll(p.PlainText, term, matchCase, wholeWord, useWildcards).Count();
        }

        return count;
    }
}
