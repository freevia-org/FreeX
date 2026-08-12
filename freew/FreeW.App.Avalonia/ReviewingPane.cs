using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Panes;
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
/// Accept/reject and bulk actions delegate portable targeting and transitions to
/// <see cref="ReviewingPaneSession"/>; the renderer only invalidates and projects native controls.
/// </summary>
public sealed partial class ReviewingPane : SidePaneBase
{
    private static readonly ReviewingPanePresentationDescriptor Presentation =
        ReviewingPanePresentationPlanner.For(ReviewingPanePresentationProfile.DetailedAvalonia);

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly ListBox _revisionList;
    private readonly TextBlock _countLabel;
    private readonly ComboBox _sortCombo;
    private readonly Button _acceptAllButton;
    private readonly Button _rejectAllButton;
    private readonly ReviewingPaneSession _session;

    // ── Construction ──────────────────────────────────────────────────────────

    public ReviewingPane(DocumentView editor)
        : base(editor, Presentation.PaneTitle, width: 280, chromeBorderThickness: new Thickness(1, 0, 0, 0), includeSeparator: true)
    {
        _session = new ReviewingPaneSession(
            () => ReviewingPaneSession.Enumerate(editor.Document),
            new ReviewingPaneMutationActions(
                entry => ResolveEntry(entry, accept: true),
                entry => ResolveEntry(entry, accept: false),
                editor.AcceptAllRevisions,
                editor.RejectAllRevisions));

        // --- Accept-All / Reject-All buttons ------------------------------------
        _acceptAllButton = new Button
        {
            Content = Presentation.Actions.AcceptAll.Label,
            Padding = new Thickness(8, 3),
            Margin = new Thickness(0, 0, 4, 0),
            IsEnabled = false,
        };
        ToolTip.SetTip(_acceptAllButton, Presentation.Actions.AcceptAll.ToolTip);
        _acceptAllButton.Click += OnAcceptAll;

        _rejectAllButton = new Button
        {
            Content = Presentation.Actions.RejectAll.Label,
            Padding = new Thickness(8, 3),
            IsEnabled = false,
        };
        ToolTip.SetTip(_rejectAllButton, Presentation.Actions.RejectAll.ToolTip);
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
        foreach (var option in Presentation.SortOptions)
            _sortCombo.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Order });
        _sortCombo.SelectedIndex = 0;
        _sortCombo.SelectionChanged += (_, _) =>
        {
            if (_sortCombo.SelectedItem is ComboBoxItem { Tag: ReviewRevisionSortOrder order })
                Render(_session.SetSortOrder(order));
        };

        var sortRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8, 0, 8, 4),
        };
        sortRow.Children.Add(new TextBlock
        {
            Text = Presentation.SortLabel,
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
        _revisionList.SelectionChanged += OnRevisionSelected;

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
        Render(_session.Refresh());
    }

    private void Render(ReviewingPaneOutcome outcome)
    {
        var state = outcome.State;
        _acceptAllButton.IsEnabled = state.HasRevisions;
        _rejectAllButton.IsEnabled = state.HasRevisions;
        _countLabel.Text = ReviewingPanePresentationPlanner.BuildCountText(
            state.Entries.Count,
            ReviewingPanePresentationProfile.DetailedAvalonia);

        var items = state.Entries.Select(revision => new RevisionItemView(revision, this)).ToArray();
        _revisionList.SelectionChanged -= OnRevisionSelected;
        _revisionList.ItemsSource = items;
        _revisionList.SelectedIndex = state.SelectedIndex;
        _revisionList.SelectionChanged += OnRevisionSelected;

        if (outcome.NavigateToRevision is { } target)
            _editor.NavigateToRevision(target);
    }

    private void OnRevisionSelected(object? sender, SelectionChangedEventArgs e)
    {
        Render(_session.SelectIndex(_revisionList.SelectedIndex));
    }

    /// <summary>Steps through tracked changes using WPF's open, refresh, and wrapping semantics.</summary>
    internal bool StepRevision(int direction, bool refresh = true)
    {
        var outcome = _session.Step(direction, refresh);
        Render(outcome);
        return outcome.NavigateToRevision is not null;
    }

    // ── Accept / Reject per-entry (called from item rows) ────────────────────

    internal void AcceptEntry(RevisionEntry entry)
    {
        Render(_session.Accept(entry));
    }

    internal void RejectEntry(RevisionEntry entry)
    {
        Render(_session.Reject(entry));
    }

    // ── Bulk handlers ─────────────────────────────────────────────────────────

    private void OnAcceptAll(object? sender, RoutedEventArgs e)
    {
        Render(_session.AcceptAll());
    }

    private void OnRejectAll(object? sender, RoutedEventArgs e)
    {
        Render(_session.RejectAll());
    }

    // ── Document refresh ──────────────────────────────────────────────────────

    /// <summary>
    /// Signals the editor that the document model was mutated outside the command bus (accept/reject
    /// bypass undo/redo, matching Word's behaviour). The editor re-renders and raises
    /// <see cref="DocumentView.DocumentChanged"/>, which re-triggers <see cref="Refresh"/> via the
    /// <see cref="MainWindow"/> wiring.
    /// </summary>
    private bool ResolveEntry(RevisionEntry entry, bool accept)
    {
        var applied = accept
            ? ReviewingPaneSession.Accept(_editor.Document, entry)
            : ReviewingPaneSession.Reject(_editor.Document, entry);
        if (applied)
            _editor.InvalidateAfterExternalMutation();
        return applied;
    }

    internal RevisionEntry? SelectedRevision => _session.State.SelectedRevision;
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
            var presentation = ReviewingPanePresentationPlanner.BuildRevision(
                entry,
                ReviewingPanePresentationProfile.DetailedAvalonia);

            // Kind badge (coloured pill).
            var kindBadge = new Border
            {
                Background = KindBrush(entry.Kind),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1),
                Margin = new Thickness(0, 0, 6, 0),
                Child = new TextBlock
                {
                    Text = presentation.KindLabel,
                    FontSize = 10,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brushes.White,
                },
            };

            // Author name.
            var author = new TextBlock
            {
                Text = presentation.AuthorText,
                FontWeight = FontWeight.SemiBold,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            };

            // Top row: [badge] [author]
            var topRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
            topRow.Children.Add(kindBadge);
            topRow.Children.Add(author);

            // Snippet of affected text (truncated for readability).
            var snippetBlock = new TextBlock
            {
                Text = presentation.SnippetText,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(0, 0, 0, 2),
            };

            // Date (parsed from W3CDTF; falls back to raw string when not parseable).
            var dateBlock = new TextBlock
            {
                Text = presentation.DateText,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                Margin = new Thickness(0, 0, 0, 2),
            };

            // Per-entry Accept / Reject buttons.
            var acceptBtn = new Button
            {
                Content = Presentation.Actions.AcceptSelected.Label,
                Padding = new Thickness(6, 2),
                Margin = new Thickness(0, 0, 4, 0),
                FontSize = 10,
            };
            ToolTip.SetTip(acceptBtn, presentation.AcceptToolTip);
            acceptBtn.Click += (_, _) => pane.AcceptEntry(entry);

            var rejectBtn = new Button
            {
                Content = Presentation.Actions.RejectSelected.Label,
                Padding = new Thickness(6, 2),
                FontSize = 10,
            };
            ToolTip.SetTip(rejectBtn, presentation.RejectToolTip);
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

        private static IBrush KindBrush(RevisionEntryKind kind) => kind switch
        {
            RevisionEntryKind.Insertion  => new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)),   // green
            RevisionEntryKind.Deletion   => new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)),   // red
            RevisionEntryKind.Formatting => new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0)),   // blue
            _ => Brushes.Gray,
        };

    }
}
