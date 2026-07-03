using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>
/// First Avalonia Review > Show Markup > Show Revisions in Balloons surface. This is a compact
/// right-side strip backed by the existing revision and comment models; it intentionally does not
/// claim Word-perfect leader-line anchoring yet.
/// </summary>
public sealed class ReviewBalloonsPane : SidePaneBase
{
    private readonly TextBlock _countLabel;
    private readonly ListBox _balloonList;

    public ReviewBalloonsPane(DocumentView editor)
        : base(editor, "Review Balloons", width: 260, chromeBorderThickness: new Thickness(1, 0, 0, 0), includeSeparator: true)
    {
        _countLabel = new TextBlock
        {
            Margin = new Thickness(8, 2, 8, 6),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
        };

        _balloonList = new ListBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
        };

        DockPanel.SetDock(_countLabel, Dock.Top);
        InnerLayout.Children.Add(_countLabel);
        InnerLayout.Children.Add(_balloonList);
    }

    public override void Refresh()
    {
        var items = EnumerateBalloons(_editor.Document, _editor.CurrentReviewDisplayPolicy);
        _countLabel.Text = items.Count == 0
            ? "No review balloons"
            : $"{items.Count} review balloon{(items.Count == 1 ? "" : "s")}";
        _balloonList.ItemsSource = items.Select(item => new BalloonItemView(item)).ToArray();
    }

    internal int BalloonItemCount => (_balloonList.ItemsSource as BalloonItemView[])?.Length ?? 0;

    internal static IReadOnlyList<ReviewBalloonItem> EnumerateBalloons(
        TextDocument document,
        ReviewDisplayPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(document);

        var revisions = RevisionList.Enumerate(document)
            .Where(entry => ShouldShowRevision(entry, policy))
            .Select(ReviewBalloonItem.FromRevision);

        var comments = policy.ShowComments
            ? CommentListPlanner.Build(document).Select(ReviewBalloonItem.FromComment)
            : Enumerable.Empty<ReviewBalloonItem>();

        return revisions.Concat(comments)
            .OrderBy(item => item.BlockIndex)
            .ThenBy(item => item.Offset)
            .ThenBy(item => item.SortKind)
            .ToList();
    }

    private static bool ShouldShowRevision(RevisionEntry entry, ReviewDisplayPolicy policy) =>
        entry.Kind switch
        {
            RevisionEntryKind.Formatting => policy.ShowFormatting,
            RevisionEntryKind.Insertion or RevisionEntryKind.Deletion => policy.ShowInsertionsAndDeletions,
            _ => true,
        };

    internal sealed record ReviewBalloonItem(
        string Kind,
        string Author,
        string Text,
        int BlockIndex,
        int Offset,
        int SortKind,
        bool Resolved = false)
    {
        public static ReviewBalloonItem FromRevision(RevisionEntry entry)
        {
            var kind = entry.Kind switch
            {
                RevisionEntryKind.Insertion => "Inserted",
                RevisionEntryKind.Deletion => "Deleted",
                RevisionEntryKind.Formatting => "Formatting",
                _ => entry.Kind.ToString(),
            };

            return new ReviewBalloonItem(
                kind,
                string.IsNullOrWhiteSpace(entry.Author) ? "Unknown" : entry.Author,
                string.IsNullOrWhiteSpace(entry.Text) ? "(formatting change)" : entry.Text,
                entry.BlockIndex,
                RevisionOffset(entry),
                SortKind: 0);
        }

        public static ReviewBalloonItem FromComment(CommentListItem item) =>
            new(
                item.Resolved ? "Resolved comment" : "Comment",
                string.IsNullOrWhiteSpace(item.Author) ? "Unknown" : item.Author,
                item.ReplyCount > 0 ? $"{item.Text} ({item.ReplyCount} repl{(item.ReplyCount == 1 ? "y" : "ies")})" : item.Text,
                item.BlockIndex,
                item.Anchor.Offset,
                SortKind: 1,
                item.Resolved);

        private static int RevisionOffset(RevisionEntry entry)
        {
            var offset = 0;
            foreach (var run in entry.Paragraph.Runs)
            {
                if (ReferenceEquals(run, entry.Run))
                    return offset;

                offset += run.Text.Length;
            }

            return 0;
        }
    }

    private sealed class BalloonItemView : UserControl
    {
        public BalloonItemView(ReviewBalloonItem item)
        {
            var kind = new TextBlock
            {
                Text = item.Kind,
                FontSize = 10,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brushes.White,
            };

            var badge = new Border
            {
                Background = item.Resolved
                    ? new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80))
                    : new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1),
                Child = kind,
            };

            var author = new TextBlock
            {
                Text = item.Author,
                FontWeight = FontWeight.SemiBold,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
            };

            var topRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 3),
            };
            topRow.Children.Add(badge);
            topRow.Children.Add(author);

            var text = new TextBlock
            {
                Text = Trim(item.Text),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
            };

            var stack = new StackPanel { Margin = new Thickness(6, 5) };
            stack.Children.Add(topRow);
            stack.Children.Add(text);

            Content = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = stack,
            };
        }

        private static string Trim(string text) =>
            text.Length <= 120 ? text : string.Concat(text.AsSpan(0, 117), "...");
    }
}
