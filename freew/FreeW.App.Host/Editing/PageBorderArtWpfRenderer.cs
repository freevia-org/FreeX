using System.Windows;
using System.Windows.Media;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Host.Editing;

public static class PageBorderArtWpfRenderer
{
    private static readonly Brush AppleFill = FrozenBrush(PageBorderArtVisualPlanner.AppleFillRed, 0, 0);
    private static readonly Pen AppleStem = FrozenPen(PageBorderArtVisualPlanner.AppleStemRed, 0, 0, 1.35);
    private static readonly Pen AppleHighlight = FrozenPen(
        PageBorderArtVisualPlanner.AppleHighlightRed,
        PageBorderArtVisualPlanner.AppleHighlightGreen,
        PageBorderArtVisualPlanner.AppleHighlightBlue,
        2.0);
    private static readonly Brush ShadowedSquareFill = FrozenBrush(
        0,
        0,
        PageBorderArtVisualPlanner.ShadowedSquareBlue);
    private static readonly Pen ShorebirdTrackPen = FrozenPen(
        0,
        0,
        0,
        PageBorderArtVisualPlanner.ShorebirdTrackStrokeWidthDip,
        roundCaps: false);

    public static bool TryDraw(
        DrawingContext context,
        PageBorder border,
        Rect frame,
        double edgeInsetDip)
    {
        if (PageBorderArtVisualPlanner.TryBuildApplesFrame(
                border.ArtId,
                border.WidthPt,
                frame.Width,
                frame.Height,
                edgeInsetDip,
                out var appleMotifs))
        {
            foreach (var motif in appleMotifs)
                DrawApple(context, motif with { Xdip = frame.X + motif.Xdip, Ydip = frame.Y + motif.Ydip });
            return true;
        }

        if (PageBorderArtVisualPlanner.TryBuildShadowedSquaresFrame(
                border.ArtId,
                border.WidthPt,
                frame.Width,
                frame.Height,
                edgeInsetDip,
                out var squareMotifs))
        {
            foreach (var motif in squareMotifs)
                DrawShadowedSquare(context, motif with { Xdip = frame.X + motif.Xdip, Ydip = frame.Y + motif.Ydip });
            return true;
        }

        if (PageBorderArtVisualPlanner.TryBuildShorebirdTracksFrame(
                border.ArtId,
                border.WidthPt,
                frame.Width,
                frame.Height,
                edgeInsetDip,
                out var trackMotifs))
        {
            foreach (var motif in trackMotifs)
            {
                var placed = motif with
                {
                    CenterXDip = frame.X + motif.CenterXDip,
                    CenterYDip = frame.Y + motif.CenterYDip,
                };
                foreach (var segment in PageBorderArtVisualPlanner.BuildShorebirdTrackSegments(placed))
                {
                    context.DrawLine(
                        ShorebirdTrackPen,
                        new Point(segment.X1Dip, segment.Y1Dip),
                        new Point(segment.X2Dip, segment.Y2Dip));
                }
            }
            return true;
        }

        return false;
    }

    private static void DrawShadowedSquare(DrawingContext context, PageBorderShadowedSquareMotif motif)
    {
        var shadowSize = Math.Max(0, motif.SizeDip - 4.0);
        context.DrawRectangle(
            ShadowedSquareFill,
            null,
            new Rect(motif.Xdip, motif.Ydip, shadowSize, shadowSize));

        var faceInset = PageBorderArtVisualPlanner.ShadowedSquareFaceInsetDip;
        var faceSize = Math.Max(0, motif.SizeDip - 6.0);
        var faceX = motif.Xdip + faceInset;
        var faceY = motif.Ydip + faceInset;
        context.DrawRectangle(Brushes.White, null, new Rect(faceX, faceY, faceSize, faceSize));
        var outlineInset = PageBorderArtVisualPlanner.ShadowedSquareOutlineInsetDip;
        var outlineSize = Math.Max(0, motif.SizeDip - 4.0);
        var outlineX = motif.Xdip + outlineInset;
        var outlineY = motif.Ydip + outlineInset;
        context.DrawRectangle(ShadowedSquareFill, null, new Rect(outlineX, outlineY, outlineSize, 1));
        context.DrawRectangle(ShadowedSquareFill, null, new Rect(outlineX, outlineY + outlineSize - 1, outlineSize, 1));
        context.DrawRectangle(ShadowedSquareFill, null, new Rect(outlineX, outlineY, 1, outlineSize));
        context.DrawRectangle(ShadowedSquareFill, null, new Rect(outlineX + outlineSize - 1, outlineY, 1, outlineSize));
    }

    private static void DrawApple(DrawingContext context, PageBorderAppleMotif motif)
    {
        var x = motif.Xdip;
        var y = motif.Ydip;
        var size = motif.SizeDip;
        var body = new StreamGeometry();
        using (var path = body.Open())
        {
            path.BeginFigure(Point(x, y, size, 0.50, 0.22), true, true);
            path.BezierTo(
                Point(x, y, size, 0.35, 0.04),
                Point(x, y, size, 0.04, 0.10),
                Point(x, y, size, 0.03, 0.51),
                true,
                true);
            path.BezierTo(
                Point(x, y, size, 0.02, 0.82),
                Point(x, y, size, 0.24, 1.00),
                Point(x, y, size, 0.50, 0.91),
                true,
                true);
            path.BezierTo(
                Point(x, y, size, 0.76, 1.00),
                Point(x, y, size, 0.98, 0.82),
                Point(x, y, size, 0.97, 0.51),
                true,
                true);
            path.BezierTo(
                Point(x, y, size, 0.96, 0.10),
                Point(x, y, size, 0.65, 0.04),
                Point(x, y, size, 0.50, 0.22),
                true,
                true);
        }
        body.Freeze();
        context.DrawGeometry(AppleFill, null, body);

        var stem = new StreamGeometry();
        using (var path = stem.Open())
        {
            path.BeginFigure(Point(x, y, size, 0.50, 0.30), false, false);
            path.BezierTo(
                Point(x, y, size, 0.56, 0.24),
                Point(x, y, size, 0.61, 0.10),
                Point(x, y, size, 0.62, 0.03),
                true,
                true);
        }
        stem.Freeze();
        context.DrawGeometry(null, ScaledPen(AppleStem, size / 32.0), stem);

        var highlight = new StreamGeometry();
        using (var path = highlight.Open())
        {
            path.BeginFigure(Point(x, y, size, 0.25, 0.34), false, false);
            path.BezierTo(
                Point(x, y, size, 0.15, 0.47),
                Point(x, y, size, 0.15, 0.70),
                Point(x, y, size, 0.22, 0.78),
                true,
                true);
        }
        highlight.Freeze();
        context.DrawGeometry(null, ScaledPen(AppleHighlight, size / 32.0), highlight);
    }

    private static Point Point(double x, double y, double size, double nx, double ny) =>
        new(x + size * nx, y + size * ny);

    private static Brush FrozenBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(
        byte red,
        byte green,
        byte blue,
        double thickness,
        bool roundCaps = true)
    {
        var pen = new Pen(FrozenBrush(red, green, blue), thickness);
        if (roundCaps)
        {
            pen.StartLineCap = PenLineCap.Round;
            pen.EndLineCap = PenLineCap.Round;
        }
        pen.Freeze();
        return pen;
    }

    private static Pen ScaledPen(Pen source, double scale)
    {
        if (Math.Abs(scale - 1.0) < 0.001)
            return source;

        var pen = source.Clone();
        pen.Thickness *= scale;
        pen.Freeze();
        return pen;
    }
}
