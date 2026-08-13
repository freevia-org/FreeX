using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>
/// FreeW Avalonia reviewing pane: tracked-changes list with Accept / Reject per-entry and Accept-All /
/// Reject-All bulk actions. Mirrors the WPF host's Reviewing Pane behaviour using Avalonia controls.
/// Consumes <see cref="RevisionList"/> (enumerate, per-entry accept/reject) and
/// <see cref="TrackChanges"/> (bulk accept/reject, has-revisions) from the portable model tier;
/// does NOT duplicate any model logic.
///
/// Construction: pass the <see cref="DocumentView"/> once. Wire
/// <see cref="DocumentView.DocumentChanged"/> to call <see cref="Refresh"/>. Toggle
/// <see cref="IsVisible"/> via the Review ribbon command (<c>freew.reviewingpane</c>); defaults to hidden.
///
/// Accept/reject are fully wired: each entry row has Accept and Reject buttons that call
/// <see cref="RevisionList.Accept"/> / <see cref="RevisionList.Reject"/> directly, then raise
/// <see cref="DocumentView.DocumentChanged"/> so the editor re-renders and the pane re-populates.
/// The Accept-All / Reject-All header buttons call <see cref="TrackChanges.AcceptAll"/> /
/// <see cref="TrackChanges.RejectAll"/> then do the same.
/// </summary>
public sealed class ReviewingPane : SidePaneBase
{
    // ── State ─────────────────────────────────────────────────────────────────

    private readonly ListBox _revisionList;
    private readonly TextBlock _countLabel;
    private readonly ComboBox _sortCombo;
    private readonly Button _acceptAllButton;
    private readonly Button _rejectAllButton;
    private IReadOnlyList<RevisionEntry> _revisions = Array.Empty<RevisionEntry>();
    private ReviewRevisionSortOrder _sortOrder = ReviewRevisionSortOrder.Sequence;

    // ── Construction ──────────────────────────────────────────────────────────

    public ReviewingPane(DocumentView editor)
        : base(editor, "Tracked Changes", width: 280, chromeBorderThickness: new Thickness(1, 0, 0, 0), includeSeparator: true)
    {
        // --- Accept-All / Reject-All buttons ------------------------------------
        _acceptAllButton = new Button
        {
            Content = "Accept All",
            Padding = new Thickness(8, 3),
            Margin = new Thickness(0, 0, 4, 0),
            IsEnabled = false,
        };
        ToolTip.SetTip(_acceptAllButton, "Accept all tracked changes");
        _acceptAllButton.Click += OnAcceptAll;

        _rejectAllButton = new Button
        {
            Content = "Reject All",
            Padding = new Thickness(8, 3),
            IsEnabled = false,
        };
        ToolTip.SetTip(_rejectAllButton, "Reject all tracked changes");
        _rejectAllButton.Click += OnRejectAll;

        var bulkRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8, 4),
        };
        bulkRow.Children.Add(_acceptAllButton);
        bulkRow.Children.Add(_rejectAllButton);

        // --- Change count label -------------------------------------------------
        _countLabel = new TextBlock
        {
            Margin = new Thickness(8, 0, 8, 4),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
        };

        _sortCombo = new ComboBox
        {
            Width = 132,
        };
        foreach (var option in ReviewRevisionSortPlanner.Options)
            _sortCombo.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Order });
        _sortCombo.SelectedIndex = ReviewRevisionSortPlanner.IndexOf(_sortOrder);
        _sortCombo.SelectionChanged += (_, _) =>
        {
            if (_sortCombo.SelectedItem is ComboBoxItem { Tag: ReviewRevisionSortOrder order })
            {
                _sortOrder = order;
                Refresh();
            }
        };

        var sortRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8, 0, 8, 4),
        };
        sortRow.Children.Add(new TextBlock
        {
            Text = "Sort:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        });
        sortRow.Children.Add(_sortCombo);

        // --- Revision list ------------------------------------------------------
        _revisionList = new ListBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
        };
        _revisionList.SelectionChanged += (_, _) =>
        {
            if (_revisionList.SelectedItem is RevisionItemView item)
            {
                _editor.NavigateToRevision(item.Entry);
            }
        };

        // Dock bulk row and count label into InnerLayout (base added header + separator already).
        //   [bulkRow]      Dock.Top
        //   [countLabel]   Dock.Top
        //   [revisionList] fill
        DockPanel.SetDock(bulkRow, Dock.Top);
        DockPanel.SetDock(_countLabel, Dock.Top);
        DockPanel.SetDock(sortRow, Dock.Top);
        InnerLayout.Children.Add(bulkRow);
        InnerLayout.Children.Add(_countLabel);
        InnerLayout.Children.Add(sortRow);
        InnerLayout.Children.Add(_revisionList);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Rebuild the revision list from the editor's current document. Call whenever the document
    /// changes (wire to <see cref="DocumentView.DocumentChanged"/>).
    /// </summary>
    public override void Refresh()
    {
        var revisions = ReviewRevisionSortPlanner.Sort(RevisionList.Enumerate(_editor.Document), _sortOrder);
        var previousIndex = _revisionList.SelectedIndex;
        _revisions = revisions;
        var hasRevisions = revisions.Count > 0;

        _acceptAllButton.IsEnabled = hasRevisions;
        _rejectAllButton.IsEnabled = hasRevisions;
        var paneState = ReviewingPaneStatePlanner.BuildRefreshState(revisions.Count, previousIndex);
        _countLabel.Text = paneState.StatusText;

        var items = revisions.Select(r => new RevisionItemView(r, this)).ToArray();
        _revisionList.ItemsSource = items;
        _revisionList.SelectedIndex = paneState.SelectedIndex;
    }

    /// <summary>Steps through tracked changes using WPF's open, refresh, and wrapping semantics.</summary>
    internal bool StepRevision(int direction, bool refresh = true)
    {
        if (direction == 0)
            throw new ArgumentOutOfRangeException(nameof(direction));

        if (refresh)
            Refresh();
        var next = ReviewingPaneStatePlanner.ResolveStep(
            _revisions.Count,
            _revisionList.SelectedIndex,
            direction);
        if (next < 0)
            return false;
        _revisionList.SelectedIndex = next;
        return true;
    }

    // ── Accept / Reject per-entry (called from item rows) ────────────────────

    internal void AcceptEntry(RevisionEntry entry)
    {
        RevisionList.Accept(_editor.Document, entry);
        NotifyDocumentMutated();
    }

    internal void RejectEntry(RevisionEntry entry)
    {
        RevisionList.Reject(_editor.Document, entry);
        NotifyDocumentMutated();
    }

    // ── Bulk handlers ─────────────────────────────────────────────────────────

    private void OnAcceptAll(object? sender, RoutedEventArgs e)
    {
        TrackChanges.AcceptAll(_editor.Document);
        NotifyDocumentMutated();
    }

    private void OnRejectAll(object? sender, RoutedEventArgs e)
    {
        TrackChanges.RejectAll(_editor.Document);
        NotifyDocumentMutated();
    }

    // ── Document refresh ──────────────────────────────────────────────────────

    /// <summary>
    /// Signals the editor that the document model was mutated outside the command bus (accept/reject
    /// bypass undo/redo, matching Word's behaviour). The editor re-renders and raises
    /// <see cref="DocumentView.DocumentChanged"/>, which re-triggers <see cref="Refresh"/> via the
    /// <see cref="MainWindow"/> wiring.
    /// </summary>
    private void NotifyDocumentMutated()
    {
        _editor.InvalidateAfterExternalMutation();
    }

    // ── Test-support ──────────────────────────────────────────────────────────

    /// <summary>Number of revision rows currently shown in the list (for headless testing).</summary>
    internal int RevisionItemCount => (_revisionList.ItemsSource as RevisionItemView[])?.Length ?? 0;
    internal int SelectedRevisionIndexForTest => _revisionList.SelectedIndex;
    internal RevisionEntry? SelectedRevision => (_revisionList.SelectedItem as RevisionItemView)?.Entry;
    internal RevisionEntry? SelectedRevisionForTest => SelectedRevision;
    internal ReviewRevisionSortOrder SortOrderForTest => _sortOrder;

    internal void SetSortOrderForTest(ReviewRevisionSortOrder order)
    {
        _sortCombo.SelectedIndex = ReviewRevisionSortPlanner.IndexOf(order);
    }

    /// <summary>
    /// Enumerates the tracked-change entries for <paramref name="doc"/> via the model tier — the same
    /// list the pane would show. Exposed for headless tests only.
    /// </summary>
    internal static IReadOnlyList<RevisionEntry> EnumerateRevisions(TextDocument doc) =>
        RevisionList.Enumerate(doc);

    // ── Row item ──────────────────────────────────────────────────────────────

    /// <summary>
    /// A single revision row: coloured kind badge, author, text snippet, date, and
    /// per-entry Accept / Reject buttons. Stateless — rebuilt on every <see cref="Refresh"/>.
    /// </summary>
    internal sealed class RevisionItemView : UserControl
    {
        public RevisionItemView(RevisionEntry entry, ReviewingPane pane)
        {
            Entry = entry;

            // Kind badge (coloured pill).
            var kindBadge = new Border
            {
                Background = KindBrush(entry.Kind),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1),
                Margin = new Thickness(0, 0, 6, 0),
                Child = new TextBlock
                {
                    Text = KindLabel(entry.Kind),
                    FontSize = 10,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brushes.White,
                },
            };

            // Author name.
            var author = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(entry.Author) ? "(unknown)" : entry.Author,
                FontWeight = FontWeight.SemiBold,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            };

            // Top row: [badge] [author]
            var topRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
            topRow.Children.Add(kindBadge);
            topRow.Children.Add(author);

            // Snippet of affected text (truncated for readability).
            var snippet = entry.Text.Length > 60
                ? string.Concat("\"", entry.Text.AsSpan(0, 57), "…\"")
                : $"\"{entry.Text}\"";
            var snippetBlock = new TextBlock
            {
                Text = snippet,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(0, 0, 0, 2),
            };

            // Date (parsed from W3CDTF; falls back to raw string when not parseable).
            var dateText = FormatDate(entry.DateXml);
            var dateBlock = new TextBlock
            {
                Text = dateText,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                Margin = new Thickness(0, 0, 0, 2),
            };

            // Per-entry Accept / Reject buttons.
            var acceptBtn = new Button
            {
                Content = "Accept",
                Padding = new Thickness(6, 2),
                Margin = new Thickness(0, 0, 4, 0),
                FontSize = 10,
            };
            ToolTip.SetTip(acceptBtn, $"Accept this {KindLabel(entry.Kind).ToLowerInvariant()} change");
            acceptBtn.Click += (_, _) => pane.AcceptEntry(entry);

            var rejectBtn = new Button
            {
                Content = "Reject",
                Padding = new Thickness(6, 2),
                FontSize = 10,
            };
            ToolTip.SetTip(rejectBtn, $"Reject this {KindLabel(entry.Kind).ToLowerInvariant()} change");
            rejectBtn.Click += (_, _) => pane.RejectEntry(entry);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
            btnRow.Children.Add(acceptBtn);
            btnRow.Children.Add(rejectBtn);

            // Vertical card layout.
            var card = new StackPanel { Margin = new Thickness(4, 4, 4, 2) };
            card.Children.Add(topRow);
            card.Children.Add(snippetBlock);
            card.Children.Add(dateBlock);
            card.Children.Add(btnRow);

            Content = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = card,
            };
        }

        public RevisionEntry Entry { get; }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static string KindLabel(RevisionEntryKind kind) => kind switch
        {
            RevisionEntryKind.Insertion => "Insertion",
            RevisionEntryKind.Deletion  => "Deletion",
            RevisionEntryKind.Formatting => "Formatting",
            _ => kind.ToString(),
        };

        private static IBrush KindBrush(RevisionEntryKind kind) => kind switch
        {
            RevisionEntryKind.Insertion  => new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)),   // green
            RevisionEntryKind.Deletion   => new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)),   // red
            RevisionEntryKind.Formatting => new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0)),   // blue
            _ => Brushes.Gray,
        };

        private static string FormatDate(string? dateXml)
        {
            if (string.IsNullOrEmpty(dateXml))
                return string.Empty;
            // W3CDTF: "2024-03-15T10:30:00Z" — take the date part only for brevity.
            var i = dateXml.IndexOf('T');
            return i > 0 ? dateXml[..i] : dateXml;
        }
    }
}
