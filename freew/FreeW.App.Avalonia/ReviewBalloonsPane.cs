using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>
/// Avalonia Review > Show Markup > Show Revisions in Balloons surface. The pane shares the same
/// source enumeration and right-margin leader-line layout contract as the WPF balloon overlay.
/// </summary>
public sealed class ReviewBalloonsPane : SidePaneBase
{
    private static readonly ReviewBalloonLayoutOptions LayoutOptions = new(
        StripWidth: 260,
        BalloonWidth: 218,
        BalloonX: 22);

    private static readonly IBrush PaneBackground = ToBrush(ReviewBalloonStyleCatalog.PaneBackground);
    private static readonly IBrush LeaderBrush = ToBrush(ReviewBalloonStyleCatalog.Leader);
    private static readonly IBrush AuthorBrush = ToBrush(ReviewBalloonStyleCatalog.AuthorText);
    private static readonly IBrush TextBrush = ToBrush(ReviewBalloonStyleCatalog.BodyText);
    private static readonly IBrush MetadataBrush = ToBrush(ReviewBalloonStyleCatalog.MetadataText);

    private static IBrush ToBrush(ReviewBalloonColor color) =>
        new SolidColorBrush(Color.FromRgb(color.Red, color.Green, color.Blue));

    private readonly TextBlock _countLabel;
    private readonly Canvas _balloonCanvas;
    private IReadOnlyList<ReviewBalloonLayout> _layouts = [];

    public ReviewBalloonsPane(DocumentView editor)
        : base(editor, "Review Balloons", width: 260, chromeBorderThickness: new Thickness(1, 0, 0, 0), includeSeparator: true)
    {
        _countLabel = new TextBlock
        {
            Margin = new Thickness(8, 2, 8, 6),
            FontSize = 11,
            Foreground = MetadataBrush,
        };

        _balloonCanvas = new Canvas
        {
            Width = LayoutOptions.StripWidth,
            Background = PaneBackground,
            ClipToBounds = true,
        };

        DockPanel.SetDock(_countLabel, Dock.Top);
        InnerLayout.Children.Add(_countLabel);
        InnerLayout.Children.Add(_balloonCanvas);
    }

    public override void Refresh()
    {
        var viewportHeight = ResolveViewportHeight();
        _layouts = ReviewBalloonLayoutPlanner.BuildLayout(
            _editor.Document,
            _editor.CurrentReviewDisplayPolicy,
            viewportHeight,
            LayoutOptions);

        _countLabel.Text = _layouts.Count == 0
            ? "No review balloons"
            : $"{_layouts.Count} review balloon{(_layouts.Count == 1 ? "" : "s")}";

        _balloonCanvas.Height = Math.Max(
            viewportHeight,
            _layouts.Count == 0
                ? 0
                : _layouts[^1].BalloonY + _layouts[^1].BalloonHeight + LayoutOptions.BalloonGap);
        _balloonCanvas.Children.Clear();

        foreach (var layout in _layouts)
            DrawBalloon(layout);
    }

    private double ResolveViewportHeight()
    {
        if (Bounds.Height > 0)
            return Bounds.Height;

        return Height > 0 ? Height : 800;
    }

    internal int BalloonItemCount => _layouts.Count;
    internal IReadOnlyList<ReviewBalloonLayout> LayoutsForTest => _layouts;
    internal int VisualChildCountForTest => _balloonCanvas.Children.Count;

    internal static IReadOnlyList<ReviewBalloonSource> EnumerateBalloons(
        TextDocument document,
        ReviewDisplayPolicy policy) =>
        ReviewBalloonLayoutPlanner.BuildSources(document, policy);

    private void DrawBalloon(ReviewBalloonLayout layout)
    {
        var item = layout.Source;
        var style = ReviewBalloonStyleCatalog.Resolve(item.Kind, item.Resolved);
        var leader = new Line
        {
            StartPoint = new Point(layout.LeaderStartX, layout.LeaderStartY),
            EndPoint = new Point(layout.LeaderEndX, layout.LeaderEndY),
            Stroke = LeaderBrush,
            StrokeThickness = LayoutOptions.LeaderThickness,
            StrokeDashArray = new AvaloniaList<double> { 3, 2 },
        };
        _balloonCanvas.Children.Add(leader);

        var balloon = new Border
        {
            Width = layout.BalloonWidth,
            Height = layout.BalloonHeight,
            Background = ToBrush(style.Fill),
            BorderBrush = ToBrush(style.Stroke),
            BorderThickness = new Thickness(1.2),
            CornerRadius = new CornerRadius(LayoutOptions.BalloonCornerRadius),
            Child = new BalloonItemView(item),
        };
        Canvas.SetLeft(balloon, layout.BalloonX);
        Canvas.SetTop(balloon, layout.BalloonY);
        _balloonCanvas.Children.Add(balloon);
    }

    private sealed class BalloonItemView : UserControl
    {
        public BalloonItemView(ReviewBalloonSource item)
        {
            var kind = new TextBlock
            {
                Text = item.KindLabel,
                FontSize = 10,
                FontWeight = FontWeight.SemiBold,
                Foreground = ToBrush(ReviewBalloonStyleCatalog.BadgeText),
            };

            var badge = new Border
            {
                Background = ToBrush(ReviewBalloonStyleCatalog.ResolveBadge(item.Resolved)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1),
                Child = kind,
            };

            var author = new TextBlock
            {
                Text = item.HeaderText,
                FontWeight = FontWeight.SemiBold,
                FontSize = 11,
                Foreground = AuthorBrush,
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

            var metadata = new TextBlock
            {
                Text = item.MetadataText,
                FontSize = 10,
                Foreground = MetadataBrush,
                Margin = new Thickness(0, 0, 0, 2),
            };

            var text = new TextBlock
            {
                Text = ReviewBalloonLayoutPlanner.TruncatePreview(item.BodyText, 120),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = TextBrush,
            };

            var stack = new StackPanel { Margin = new Thickness(6, 5) };
            stack.Children.Add(topRow);
            stack.Children.Add(metadata);
            stack.Children.Add(text);

            Content = stack;
        }
    }
}
