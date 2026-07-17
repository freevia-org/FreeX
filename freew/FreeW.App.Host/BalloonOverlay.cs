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
/// edge). Its leader start is taken from the marked text's live FlowDocument geometry. Before WPF has
/// completed its layout pass, the planner uses a deterministic ordinal fallback so the review surface
/// remains available while the document is being constructed.
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

    private static readonly Brush CommentFill    = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xF4, 0xCE)));
    private static readonly Brush CommentStroke  = Freeze(new SolidColorBrush(Color.FromRgb(0xE5, 0xC3, 0x65)));
    private static readonly Brush InsertFill     = Freeze(new SolidColorBrush(Color.FromRgb(0xD9, 0xF0, 0xE0)));
    private static readonly Brush InsertStroke   = Freeze(new SolidColorBrush(Color.FromRgb(0x60, 0xA9, 0x70)));
    private static readonly Brush DeleteFill     = Freeze(new SolidColorBrush(Color.FromRgb(0xFD, 0xDE, 0xDE)));
    private static readonly Brush DeleteStroke   = Freeze(new SolidColorBrush(Color.FromRgb(0xC5, 0x50, 0x50)));
    private static readonly Brush FormatFill     = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xF8)));
    private static readonly Brush FormatStroke   = Freeze(new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0xC8)));
    private static readonly Brush ResolvedFill   = Freeze(new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)));
    private static readonly Brush ResolvedStroke = Freeze(new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)));
    private static readonly Brush LeaderBrush    = Freeze(new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)));
    private static readonly Brush AuthorBrush    = Freeze(new SolidColorBrush(Color.FromRgb(0x17, 0x32, 0x4D)));
    private static readonly Brush TextBrush      = Freeze(new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30)));
    private static readonly Brush MetadataBrush  = Freeze(new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)));

    private static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

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
            Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF8)),
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
        var sources = ReviewBalloonLayoutPlanner.BuildSources(
            _editor.Model,
            _editor.CurrentReviewDisplayPolicy);
        var anchorYs = BuildAnchorYs(sources);
        var layouts = ReviewBalloonLayoutPlanner.BuildLayout(sources, canvasHeight, anchorYs);

        foreach (var layout in layouts)
        {
            var item = new BalloonItem(
                OverlayKind(layout.Source.Kind),
                layout.Source.KindLabel,
                layout.Source.HeaderText,
                layout.Source.MetadataText,
                TruncatePreview(layout.Source.BodyText, 68),
                layout.Source.Resolved,
                layout.Ordinal);
            DrawBalloon(item, layout.BalloonX, layout.BalloonY, layout.LeaderStartY);
        }
    }

    private IReadOnlyList<double?> BuildAnchorYs(IReadOnlyList<ReviewBalloonSource> sources)
    {
        System.Windows.Point editorOrigin;
        try
        {
            editorOrigin = _editor.TranslatePoint(new System.Windows.Point(0, 0), _canvas);
        }
        catch (InvalidOperationException)
        {
            return new double?[sources.Count];
        }

        return sources
            .Select<ReviewBalloonSource, double?>(source => _editor.TryGetReviewAnchorY(source.BlockIndex, source.Offset) is { } y
                ? y + editorOrigin.Y
                : null)
            .ToArray();
    }

    private sealed record BalloonItem(
        string Kind,          // "comment" | "insert" | "delete" | "format"
        string KindLabel,
        string Author,
        string Metadata,
        string Preview,
        bool Resolved,
        int    Ordinal);

    private static string TruncatePreview(string text, int maxLen) =>
        text.Length <= maxLen ? text : text[..maxLen] + "…";

    private static string OverlayKind(ReviewBalloonKind kind) => kind switch
    {
        ReviewBalloonKind.Insertion => "insert",
        ReviewBalloonKind.Deletion => "delete",
        ReviewBalloonKind.Formatting => "format",
        _ => "comment"
    };

    // ── Drawing ──────────────────────────────────────────────────────────────────────────────────

    private void DrawBalloon(BalloonItem item, double x, double y, double anchorY)
    {
        // Choose colours by kind.
        var (fill, stroke) = item.Resolved
            ? (ResolvedFill, ResolvedStroke)
            : item.Kind switch
            {
                "insert" => (InsertFill, InsertStroke),
                "delete" => (DeleteFill, DeleteStroke),
                "format" => (FormatFill, FormatStroke),
                _        => (CommentFill, CommentStroke)   // "comment" default
            };

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
