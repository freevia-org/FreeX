using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Host;

/// <summary>
/// Right-margin balloon overlay for Review > Show Markup > Show Revisions in Balloons.
///
/// Architecture: a 200px-wide Canvas strip placed to the RIGHT of the DocumentView (hosted in the
/// same workspace Grid row, added as a sibling column). Each balloon is a rounded rectangle with a
/// leader line drawn to the text's horizontal midpoint in the editor. The strip is rebuild on every
/// TextChanged event when Balloons mode is active. When inactive the strip has Width=0 and no children.
///
/// Balloon sources (in document order):
///   1. Comments — from TextDocument.Comments (author, first paragraph of content).
///   2. Tracked-change revisions — from DocumentView.ListRevisions() (author, kind, text).
///
/// Layout rule: balloons are stacked top-to-bottom with 8px gaps; each balloon has a fixed height
/// (60px) and fills the strip width (180px). The leader line connects the balloon's left edge midpoint
/// to the center of the editor at a fixed horizontal offset (the strip's left edge = the editor's right
/// edge). Actual text-position Y is approximated via a per-item ordinal mapping (proportional to the
/// sorted order in the document) since WPF FlowDocument doesn't expose per-range screen coordinates
/// without a full measure pass — the vertical position tracks the relative order faithfully even if
/// not pixel-exact.
///
/// State flag: <see cref="BalloonsEnabled"/> toggles the mode; consumers call <see cref="Rebuild"/>
/// whenever the document changes or the flag is toggled.
/// </summary>
internal sealed class BalloonOverlay
{
    // ── Constants ────────────────────────────────────────────────────────────────────────────────
    private const double StripWidth      = 200;
    private const double BalloonWidth    = 176;
    private const double BalloonHeight   = 56;
    private const double BalloonGap      = 8;
    private const double BalloonX        = 12;
    private const double BalloonCorner   = 4;
    private const double LeaderThickness = 1.0;

    private static readonly Brush PaneBackgroundBrush = ToBrush(ReviewBalloonStyleCatalog.PaneBackground);
    private static readonly Brush LeaderBrush = ToBrush(ReviewBalloonStyleCatalog.Leader);
    private static readonly Brush AuthorBrush = ToBrush(ReviewBalloonStyleCatalog.AuthorText);
    private static readonly Brush TextBrush = ToBrush(ReviewBalloonStyleCatalog.BodyText);
    private static readonly Brush MetadataBrush = ToBrush(ReviewBalloonStyleCatalog.MetadataText);

    private static Brush ToBrush(ReviewBalloonColor color)
    {
        var brush = new SolidColorBrush(Color.FromRgb(color.Red, color.Green, color.Blue));
        brush.Freeze();
        return brush;
    }

    // ── Fields ───────────────────────────────────────────────────────────────────────────────────
    private readonly DocumentView _editor;
    private readonly Canvas       _canvas;

    /// <summary>When true the strip is shown and rebuilt on document changes; when false it collapses.</summary>
    public bool BalloonsEnabled { get; private set; }

    // ── Constructor ──────────────────────────────────────────────────────────────────────────────

    public BalloonOverlay(DocumentView editor)
    {
        _editor = editor;
        _canvas = new Canvas
        {
            Width = 0,               // collapsed until enabled
            Background = PaneBackgroundBrush,
            ClipToBounds = true
        };
    }

    /// <summary>The visual to place as the right-sibling column of the editor.</summary>
    public UIElement Visual => _canvas;

    // ── Toggle ───────────────────────────────────────────────────────────────────────────────────

    public void Toggle()
    {
        BalloonsEnabled = !BalloonsEnabled;
        _canvas.Width = BalloonsEnabled ? StripWidth : 0;
        Rebuild();
    }

    public void Enable()  { BalloonsEnabled = true;  _canvas.Width = StripWidth; Rebuild(); }
    public void Disable() { BalloonsEnabled = false; _canvas.Width = 0;          _canvas.Children.Clear(); }

    // ── Rebuild ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Clears and redraws all balloons from the live document state. No-op when not enabled.
    /// Should be called whenever the document changes (TextChanged) or balloons mode is toggled.
    /// </summary>
    public void Rebuild()
    {
        _canvas.Children.Clear();
        if (!BalloonsEnabled) return;

        var canvasHeight = _canvas.ActualHeight > 0 ? _canvas.ActualHeight : 800;
        var layouts = ReviewBalloonLayoutPlanner.BuildLayout(
            _editor.Model,
            _editor.CurrentReviewDisplayPolicy,
            canvasHeight);

        foreach (var layout in layouts)
        {
            var item = new BalloonItem(
                layout.Source.Kind,
                layout.Source.KindLabel,
                layout.Source.HeaderText,
                layout.Source.MetadataText,
                ReviewBalloonLayoutPlanner.TruncatePreview(layout.Source.BodyText, 68, "\u2026"),
                layout.Source.Resolved,
                layout.Ordinal);
            DrawBalloon(item, layout.BalloonX, layout.BalloonY, layout.LeaderStartY);
        }
    }

    private sealed record BalloonItem(
        ReviewBalloonKind Kind,
        string KindLabel,
        string Author,
        string Metadata,
        string Preview,
        bool Resolved,
        int    Ordinal);

    // ── Drawing ──────────────────────────────────────────────────────────────────────────────────

    private void DrawBalloon(BalloonItem item, double x, double y, double anchorY)
    {
        var style = ReviewBalloonStyleCatalog.Resolve(item.Kind, item.Resolved);
        var fill = ToBrush(style.Fill);
        var stroke = ToBrush(style.Stroke);

        // Leader line: from left edge of balloon midpoint to the edge of the strip.
        var balloonMidY = y + BalloonHeight / 2;
        var leader = new Line
        {
            X1 = 0,           Y1 = anchorY,
            X2 = x,           Y2 = balloonMidY,
            Stroke = LeaderBrush,
            StrokeThickness = LeaderThickness,
            StrokeDashArray = new DoubleCollection { 3, 2 }
        };
        _canvas.Children.Add(leader);

        // Balloon rectangle.
        var rect = new Rectangle
        {
            Width           = BalloonWidth,
            Height          = BalloonHeight,
            Fill            = fill,
            Stroke          = stroke,
            StrokeThickness = 1.2,
            RadiusX         = BalloonCorner,
            RadiusY         = BalloonCorner
        };
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect,  y);
        _canvas.Children.Add(rect);

        // Shared kind label used by both WPF and Avalonia review cards.
        var kindLabel = item.KindLabel;

        // Author + kind header.
        var header = new TextBlock
        {
            Text            = $"{item.Author} – {kindLabel}",
            Foreground      = AuthorBrush,
            FontWeight      = FontWeights.SemiBold,
            FontSize        = 10,
            Width           = BalloonWidth - 8,
            TextTrimming    = TextTrimming.CharacterEllipsis,
            Margin          = new Thickness(0)
        };
        Canvas.SetLeft(header, x + 4);
        Canvas.SetTop(header,  y + 4);
        _canvas.Children.Add(header);

        var metadata = new TextBlock
        {
            Text            = item.Metadata,
            Foreground      = MetadataBrush,
            FontSize        = 9,
            Width           = BalloonWidth - 8,
            TextTrimming    = TextTrimming.CharacterEllipsis,
            Margin          = new Thickness(0)
        };
        Canvas.SetLeft(metadata, x + 4);
        Canvas.SetTop(metadata, y + 17);
        _canvas.Children.Add(metadata);

        // Preview text.
        if (!string.IsNullOrEmpty(item.Preview))
        {
            var preview = new TextBlock
            {
                Text         = item.Preview,
                Foreground   = TextBrush,
                FontSize     = 10,
                Width        = BalloonWidth - 8,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight    = BalloonHeight - 32,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin       = new Thickness(0)
            };
            Canvas.SetLeft(preview, x + 4);
            Canvas.SetTop(preview,  y + 29);
            _canvas.Children.Add(preview);
        }
    }
}
