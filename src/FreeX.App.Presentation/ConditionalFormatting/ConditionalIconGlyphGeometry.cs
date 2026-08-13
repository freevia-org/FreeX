using System;
using System.Collections.Generic;

using FreeX.App.Presentation.Charts;

namespace FreeX.App.Presentation.ConditionalFormatting;

/// <summary>
/// Which fill a single glyph primitive uses. The renderer maps these to its own brushes.
/// </summary>
public enum CfGlyphFill
{
    /// <summary>The resolved icon color (filled shape such as an arrow, circle or pie wedge).</summary>
    Icon,

    /// <summary>Opaque white (the disc behind a quarter pie, or the white dot of a sign glyph).</summary>
    White,

    /// <summary>No fill — an outline-only primitive (the quarter's bordering ring).</summary>
    None,
}

/// <summary>
/// Which stroke a single glyph primitive uses. The renderer maps these to its own pens; exact pixel
/// thicknesses live with the renderer, the gray outline being 0.75 and the white overlays 1.2/1.4.
/// </summary>
public enum CfGlyphStroke
{
    /// <summary>No stroke.</summary>
    None,

    /// <summary>The gray (96,96,96) icon outline at the standard thin thickness.</summary>
    Outline,

    /// <summary>A thin white overlay stroke (the crosses, the symbol minus, the sign stem).</summary>
    WhiteThin,

    /// <summary>A medium white overlay stroke (the check marks).</summary>
    WhiteMedium,
}

/// <summary>The geometric primitive a glyph op draws.</summary>
public enum CfGlyphPrimitiveKind
{
    /// <summary>A closed, filled polyline through <see cref="CfGlyphOp.Points"/>.</summary>
    Polygon,

    /// <summary>An open (unclosed, unfilled) polyline through <see cref="CfGlyphOp.Points"/> — a stroke.</summary>
    Polyline,

    /// <summary>An axis-aligned ellipse described by <see cref="CfGlyphOp.Center"/> and radii.</summary>
    Ellipse,

    /// <summary>A single straight segment between <see cref="CfGlyphOp.Points"/>[0] and [1].</summary>
    Line,

    /// <summary>An axis-aligned rectangle described by <see cref="CfGlyphOp.Rect"/>.</summary>
    Box,

    /// <summary>
    /// A filled pie wedge: from <see cref="CfGlyphOp.Center"/> out to <see cref="CfGlyphOp.Points"/>[0]
    /// (the 12-o'clock start point), elliptical-arc to <see cref="CfGlyphOp.Points"/>[1] (the end
    /// point) with the ellipse radii, then back to the center. <see cref="CfGlyphOp.LargeArc"/> picks
    /// the long way round; the sweep is always clockwise.
    /// </summary>
    Pie,

    /// <summary>
    /// A five-pointed star (<see cref="CfGlyphOp.Points"/> holds the 10 outer/inner vertices) with
    /// a horizontal clip so only the left <see cref="CfGlyphOp.RadiusX"/> fraction (0..1) of the
    /// star's bounding box is filled with the icon color. The remaining (right) portion of the star
    /// is drawn as an empty outline only. This produces the partial-fill star appearance used by
    /// Excel's Stars icon sets. Renderers that do not support the clip may fall back to a full
    /// (icon-colored) fill.
    /// </summary>
    StarFillFraction,
}

/// <summary>
/// A single primitive within a conditional-format icon glyph, expressed purely as coordinates and a
/// fill/stroke role. Absolute coordinates within the passed-in rect (in the rect's own space).
/// </summary>
public readonly record struct CfGlyphOp(
    CfGlyphPrimitiveKind Kind,
    CfGlyphFill Fill,
    CfGlyphStroke Stroke,
    IReadOnlyList<LayoutPoint> Points,
    LayoutPoint Center = default,
    double RadiusX = 0,
    double RadiusY = 0,
    LayoutRect Rect = default,
    bool LargeArc = false)
{
    private static readonly IReadOnlyList<LayoutPoint> NoPoints = Array.Empty<LayoutPoint>();

    public static CfGlyphOp Polygon(CfGlyphFill fill, CfGlyphStroke stroke, IReadOnlyList<LayoutPoint> points) =>
        new(CfGlyphPrimitiveKind.Polygon, fill, stroke, points);

    public static CfGlyphOp Polyline(CfGlyphStroke stroke, IReadOnlyList<LayoutPoint> points) =>
        new(CfGlyphPrimitiveKind.Polyline, CfGlyphFill.None, stroke, points);

    public static CfGlyphOp Ellipse(CfGlyphFill fill, CfGlyphStroke stroke, LayoutPoint center, double radiusX, double radiusY) =>
        new(CfGlyphPrimitiveKind.Ellipse, fill, stroke, NoPoints, center, radiusX, radiusY);

    public static CfGlyphOp Line(CfGlyphStroke stroke, LayoutPoint a, LayoutPoint b) =>
        new(CfGlyphPrimitiveKind.Line, CfGlyphFill.None, stroke, new[] { a, b });

    public static CfGlyphOp Box(CfGlyphFill fill, CfGlyphStroke stroke, LayoutRect rect) =>
        new(CfGlyphPrimitiveKind.Box, fill, stroke, NoPoints, Rect: rect);

    public static CfGlyphOp Pie(LayoutPoint center, double radiusX, double radiusY, LayoutPoint start, LayoutPoint end, bool largeArc) =>
        new(CfGlyphPrimitiveKind.Pie, CfGlyphFill.Icon, CfGlyphStroke.None, new[] { start, end }, center, radiusX, radiusY, LargeArc: largeArc);

    /// <summary>
    /// Emits a star-with-partial-fill op. <paramref name="points"/> are the 10 outer/inner star
    /// vertices; <paramref name="fillFraction"/> (0..1) is the left-to-right fill extent.
    /// </summary>
    public static CfGlyphOp StarFillFraction(IReadOnlyList<LayoutPoint> points, double fillFraction) =>
        new(CfGlyphPrimitiveKind.StarFillFraction, CfGlyphFill.Icon, CfGlyphStroke.Outline, points, RadiusX: fillFraction);
}

public readonly record struct CfStarFillPlan(
    IReadOnlyList<LayoutPoint> Points,
    LayoutRect ClipRect,
    bool ShouldFill,
    bool RequiresClip);

/// <summary>
/// Toolkit-neutral geometry emitter for conditional-format icon-set glyphs. Given the glyph kind
/// (resolved by <see cref="ConditionalIconGlyphResolver"/>), the bucket index/count and the target
/// rect, it returns the ordered list of primitive ops to draw. The desktop renderer and the
/// cross-platform port both translate these ops into their own path/shape primitives, so the two draw
/// identical shapes from one source of truth. Pure geometry — no UI-framework dependencies.
/// </summary>
public static class ConditionalIconGlyphGeometry
{
    public static CfStarFillPlan PlanStarFill(CfGlyphOp op)
    {
        if (op.Kind != CfGlyphPrimitiveKind.StarFillFraction)
            throw new ArgumentException("The glyph operation must be a partial-fill star.", nameof(op));
        if (op.Points.Count == 0)
            throw new ArgumentException("The partial-fill star must contain points.", nameof(op));

        var minX = op.Points[0].X;
        var maxX = minX;
        var minY = op.Points[0].Y;
        var maxY = minY;
        for (var index = 1; index < op.Points.Count; index++)
        {
            var point = op.Points[index];
            minX = Math.Min(minX, point.X);
            maxX = Math.Max(maxX, point.X);
            minY = Math.Min(minY, point.Y);
            maxY = Math.Max(maxY, point.Y);
        }

        var fillFraction = Math.Clamp(op.RadiusX, 0d, 1d);
        return new CfStarFillPlan(
            op.Points,
            new LayoutRect(minX, minY, (maxX - minX) * fillFraction, maxY - minY),
            ShouldFill: fillFraction > 0d,
            RequiresClip: fillFraction < 1d);
    }

    /// <summary>
    /// Build the ordered primitive ops for a glyph. <paramref name="x"/>/<paramref name="y"/>/
    /// <paramref name="width"/>/<paramref name="height"/> describe the target rect; ops are emitted in
    /// that rect's coordinate space (so a renderer that draws onto a same-sized canvas at the origin
    /// passes 0,0,size,size).
    /// </summary>
    /// <param name="isAlternateVariant">
    /// R54-render-cf-icon-databar-4-2: <c>true</c> for the alternate member of a style pair that
    /// shares a <see cref="ConditionalIconGlyphKind"/> but is visually distinct in Excel --
    /// "3 Traffic Lights (Rimmed)" (adds a bezel ring around the light) and "3 Symbols (Uncircled)"
    /// (drops the circular backdrop behind the mark). See
    /// <see cref="ConditionalIconGlyphResolver.IsAlternateGlyphVariant"/>. Ignored by every other
    /// glyph kind. Defaults to <c>false</c> (the primary/default variant of each pair), matching the
    /// pre-existing behavior for callers that don't yet distinguish the two.
    /// </param>
    public static IReadOnlyList<CfGlyphOp> Build(
        ConditionalIconGlyphKind glyphKind,
        int iconIndex,
        int iconCount,
        double x,
        double y,
        double width,
        double height,
        bool isAlternateVariant = false)
    {
        var r = new RectInfo(x, y, width, height);
        return glyphKind switch
        {
            ConditionalIconGlyphKind.TrafficLight => TrafficLightGlyph(r, isAlternateVariant),
            ConditionalIconGlyphKind.Sign => SignGlyph(iconIndex, r),
            ConditionalIconGlyphKind.Symbol => SymbolGlyph(iconIndex, r, isAlternateVariant),
            ConditionalIconGlyphKind.Flag => FlagGlyph(r),
            ConditionalIconGlyphKind.Rating => RatingBarsGlyph(iconIndex, iconCount, r),
            ConditionalIconGlyphKind.Star => StarGlyph(iconIndex, iconCount, r),
            ConditionalIconGlyphKind.Quarter => QuarterGlyph(iconIndex, iconCount, r),
            ConditionalIconGlyphKind.Box => new[] { BoxGlyph(iconIndex, iconCount, r) },
            _ => new[] { CfGlyphOp.Polygon(CfGlyphFill.Icon, CfGlyphStroke.Outline, ArrowPoints(r, iconIndex, iconCount)) },
        };
    }

    private readonly record struct RectInfo(double X, double Y, double Width, double Height)
    {
        public double Left => X;
        public double Top => Y;
        public double Right => X + Width;
        public double Bottom => Y + Height;
        public LayoutPoint Center => new(X + Width / 2, Y + Height / 2);

        /// <summary>Point at the given horizontal/vertical fractions of the rect.</summary>
        public LayoutPoint Frac(double fx, double fy) => new(X + Width * fx, Y + Height * fy);
    }

    private static CfGlyphOp FilledEllipse(RectInfo r) =>
        CfGlyphOp.Ellipse(CfGlyphFill.Icon, CfGlyphStroke.Outline, r.Center, r.Width / 2, r.Height / 2);

    /// <summary>
    /// "3 Traffic Lights" glyph. Unrimmed (the default) is a single filled circle with its own thin
    /// outline. Rimmed adds a darker bezel ring around a slightly smaller filled disc, distinguishing
    /// style "3TrafficLights2" (Rimmed) from "3TrafficLights1" (Unrimmed) -- previously both rendered
    /// pixel-identical (R54-render-cf-icon-databar-4-2).
    /// </summary>
    private static CfGlyphOp[] TrafficLightGlyph(RectInfo r, bool isRimmed)
    {
        if (!isRimmed)
            return new[] { FilledEllipse(r) };

        var bezelRadiusX = r.Width / 2;
        var bezelRadiusY = r.Height / 2;
        var discRadiusX = bezelRadiusX * 0.82;
        var discRadiusY = bezelRadiusY * 0.82;
        return new[]
        {
            CfGlyphOp.Ellipse(CfGlyphFill.None, CfGlyphStroke.Outline, r.Center, bezelRadiusX, bezelRadiusY),
            CfGlyphOp.Ellipse(CfGlyphFill.Icon, CfGlyphStroke.Outline, r.Center, discRadiusX, discRadiusY),
        };
    }

    private static CfGlyphOp[] SignGlyph(int iconIndex, RectInfo r)
    {
        if (iconIndex <= 0)
        {
            return new[]
            {
                FilledEllipse(r),
                CfGlyphOp.Line(CfGlyphStroke.WhiteThin, r.Frac(0.28, 0.28), r.Frac(0.72, 0.72)),
                CfGlyphOp.Line(CfGlyphStroke.WhiteThin, r.Frac(0.72, 0.28), r.Frac(0.28, 0.72)),
            };
        }

        if (iconIndex == 1)
        {
            return new[]
            {
                CfGlyphOp.Polygon(CfGlyphFill.Icon, CfGlyphStroke.Outline, TrianglePoints(r, pointUp: true)),
                CfGlyphOp.Line(CfGlyphStroke.WhiteThin, r.Frac(0.5, 0.3), r.Frac(0.5, 0.62)),
                CfGlyphOp.Ellipse(CfGlyphFill.White, CfGlyphStroke.None, r.Frac(0.5, 0.75), 0.9, 0.9),
            };
        }

        return CheckMarkCircle(r);
    }

    private static CfGlyphOp[] SymbolGlyph(int iconIndex, RectInfo r, bool isUncircled = false)
    {
        if (iconIndex <= 0)
        {
            // R54-render-cf-icon-databar-4-2: Uncircled (style "3Symbols2") drops the diamond backdrop
            // entirely and draws just the bare cross mark, unlike Circled (the default), which keeps
            // the filled diamond behind a white cross.
            if (isUncircled)
            {
                return new[]
                {
                    CfGlyphOp.Line(CfGlyphStroke.Outline, r.Frac(0.24, 0.24), r.Frac(0.76, 0.76)),
                    CfGlyphOp.Line(CfGlyphStroke.Outline, r.Frac(0.76, 0.24), r.Frac(0.24, 0.76)),
                };
            }

            return new[]
            {
                CfGlyphOp.Polygon(CfGlyphFill.Icon, CfGlyphStroke.Outline, DiamondPoints(r)),
                CfGlyphOp.Line(CfGlyphStroke.WhiteThin, r.Frac(0.32, 0.32), r.Frac(0.68, 0.68)),
                CfGlyphOp.Line(CfGlyphStroke.WhiteThin, r.Frac(0.68, 0.32), r.Frac(0.32, 0.68)),
            };
        }

        if (iconIndex == 1)
        {
            if (isUncircled)
                return new[] { CfGlyphOp.Line(CfGlyphStroke.Outline, r.Frac(0.2, 0.5), r.Frac(0.8, 0.5)) };

            return new[]
            {
                FilledEllipse(r),
                CfGlyphOp.Line(CfGlyphStroke.WhiteThin, r.Frac(0.3, 0.5), r.Frac(0.7, 0.5)),
            };
        }

        if (isUncircled)
        {
            return new[]
            {
                CfGlyphOp.Line(CfGlyphStroke.Outline, r.Frac(0.28, 0.56), r.Frac(0.44, 0.72)),
                CfGlyphOp.Line(CfGlyphStroke.Outline, r.Frac(0.44, 0.72), r.Frac(0.76, 0.3)),
            };
        }

        return CheckMarkCircle(r);
    }

    /// <summary>A filled circle with a white check mark — shared by the sign and symbol "good" buckets.</summary>
    private static CfGlyphOp[] CheckMarkCircle(RectInfo r) => new[]
    {
        FilledEllipse(r),
        CfGlyphOp.Line(CfGlyphStroke.WhiteMedium, r.Frac(0.28, 0.56), r.Frac(0.44, 0.72)),
        CfGlyphOp.Line(CfGlyphStroke.WhiteMedium, r.Frac(0.44, 0.72), r.Frac(0.76, 0.3)),
    };

    private static CfGlyphOp[] QuarterGlyph(int iconIndex, int iconCount, RectInfo r)
    {
        // R54-render-cf-icon-databar-4-1: Excel's "5 Quarters" icon set fills 0/4, 1/4, 2/4, 3/4, 4/4
        // quarters for buckets 0..4 (worst bucket is a fully EMPTY circle). Mirrors the same
        // index/(count-1) mapping StarGlyph already uses two methods below, rather than
        // (index+1)/count, which is 20 points too full for every bucket and never reaches empty.
        var sweepFraction = iconCount <= 1
            ? 1d
            : Math.Clamp(iconIndex / (double)(iconCount - 1), 0d, 1d);
        return new[]
        {
            CfGlyphOp.Ellipse(CfGlyphFill.White, CfGlyphStroke.Outline, r.Center, r.Width / 2, r.Height / 2),
            PieOp(r, sweepFraction),
            CfGlyphOp.Ellipse(CfGlyphFill.None, CfGlyphStroke.Outline, r.Center, r.Width / 2, r.Height / 2),
        };
    }

    private static CfGlyphOp PieOp(RectInfo r, double sweepFraction)
    {
        var radiusX = r.Width / 2;
        var radiusY = r.Height / 2;
        var center = r.Center;
        var sweep = Math.Clamp(sweepFraction, 0d, 1d) * Math.PI * 2;
        var start = -Math.PI / 2;
        var end = start + sweep;
        var startPoint = new LayoutPoint(center.X, r.Top);
        var endPoint = new LayoutPoint(center.X + Math.Cos(end) * radiusX, center.Y + Math.Sin(end) * radiusY);
        return CfGlyphOp.Pie(center, radiusX, radiusY, startPoint, endPoint, largeArc: sweep > Math.PI);
    }

    private static CfGlyphOp BoxGlyph(int iconIndex, int iconCount, RectInfo r)
    {
        var inset = Math.Max(0, (iconCount - 1 - iconIndex) * r.Width * 0.07);
        var rect = new LayoutRect(
            r.Left + inset,
            r.Top + inset,
            Math.Max(1, r.Width - inset * 2),
            Math.Max(1, r.Height - inset * 2));
        return CfGlyphOp.Box(CfGlyphFill.Icon, CfGlyphStroke.Outline, rect);
    }

    /// <summary>
    /// Builds a chunky filled-arrow polygon that closely matches Excel's icon-set arrow weight.
    /// iconIndex 0 = worst bucket (red, DOWN); iconIndex count-1 = best bucket (green, UP).
    /// For 3-arrows: 0=DOWN, 1=RIGHT (neutral), 2=UP.
    /// For 4-arrows: 0=DOWN, 1=DOWN-DIAGONAL, 2=UP-DIAGONAL, 3=UP.
    /// For 5-arrows: 0=DOWN, 1=DOWN-DIAGONAL, 2=RIGHT (neutral), 3=UP-DIAGONAL, 4=UP.
    /// </summary>
    private static LayoutPoint[] ArrowPoints(RectInfo r, int iconIndex, int iconCount)
    {
        var direction = ResolveArrowDirection(iconIndex, iconCount);
        return direction switch
        {
            ArrowDirection.Up => UpArrowPoints(r),
            ArrowDirection.UpDiagonal => DiagonalArrowPoints(r, pointingUp: true),
            ArrowDirection.Right => RightArrowPoints(r),
            ArrowDirection.DownDiagonal => DiagonalArrowPoints(r, pointingUp: false),
            _ => DownArrowPoints(r),
        };
    }

    private enum ArrowDirection { Down, DownDiagonal, Right, UpDiagonal, Up }

    private static ArrowDirection ResolveArrowDirection(int iconIndex, int iconCount)
    {
        // Map bucket index 0 (worst) → Down, index count-1 (best) → Up.
        // Middle bucket distribution for 4/5 arrow sets includes diagonals.
        return iconCount switch
        {
            >= 5 => iconIndex switch
            {
                0 => ArrowDirection.Down,
                1 => ArrowDirection.DownDiagonal,
                2 => ArrowDirection.Right,
                3 => ArrowDirection.UpDiagonal,
                _ => ArrowDirection.Up,
            },
            4 => iconIndex switch
            {
                0 => ArrowDirection.Down,
                1 => ArrowDirection.DownDiagonal,
                2 => ArrowDirection.UpDiagonal,
                _ => ArrowDirection.Up,
            },
            _ => iconIndex switch
            {
                0 => ArrowDirection.Down,
                1 => ArrowDirection.Right,
                _ => ArrowDirection.Up,
            },
        };
    }

    private static LayoutPoint[] UpArrowPoints(RectInfo r)
    {
        // Up arrow — best bucket: 7-point chevron pointing up.
        var shaftLeft  = r.Left + r.Width  * 0.30;
        var shaftRight = r.Left + r.Width  * 0.70;
        var neckY      = r.Top  + r.Height * 0.45;
        return
        [
            new LayoutPoint(r.Left + r.Width / 2, r.Top),
            new LayoutPoint(r.Right,    neckY),
            new LayoutPoint(shaftRight, neckY),
            new LayoutPoint(shaftRight, r.Bottom),
            new LayoutPoint(shaftLeft,  r.Bottom),
            new LayoutPoint(shaftLeft,  neckY),
            new LayoutPoint(r.Left,     neckY),
        ];
    }

    private static LayoutPoint[] DownArrowPoints(RectInfo r)
    {
        // Down arrow — worst bucket: 7-point chevron pointing down.
        var dShaftLeft  = r.Left + r.Width  * 0.30;
        var dShaftRight = r.Left + r.Width  * 0.70;
        var dNeckY      = r.Top  + r.Height * 0.55;
        return
        [
            new LayoutPoint(r.Left + r.Width / 2, r.Bottom),
            new LayoutPoint(r.Right,    dNeckY),
            new LayoutPoint(dShaftRight, dNeckY),
            new LayoutPoint(dShaftRight, r.Top),
            new LayoutPoint(dShaftLeft,  r.Top),
            new LayoutPoint(dShaftLeft,  dNeckY),
            new LayoutPoint(r.Left,      dNeckY),
        ];
    }

    private static LayoutPoint[] RightArrowPoints(RectInfo r)
    {
        // Sideways (right-pointing) filled chevron arrow — neutral bucket.
        // Shaft occupies left ~55% of the width; arrowhead the right ~45%.
        var shaftTop    = r.Top    + r.Height * 0.30;
        var shaftBottom = r.Bottom - r.Height * 0.30;
        var neckX       = r.Left   + r.Width  * 0.55;
        return
        [
            new LayoutPoint(r.Left,  shaftTop),
            new LayoutPoint(neckX,   shaftTop),
            new LayoutPoint(neckX,   r.Top),
            new LayoutPoint(r.Right, r.Top + r.Height / 2),
            new LayoutPoint(neckX,   r.Bottom),
            new LayoutPoint(neckX,   shaftBottom),
            new LayoutPoint(r.Left,  shaftBottom),
        ];
    }

    /// <summary>
    /// Diagonal arrow (up-right or down-right): a chevron rotated 45°, approximated as a simple
    /// 7-point polygon. Excel uses a small-headed diagonal chevron; we approximate it as a rotated
    /// shaft + triangular head to keep the geometry simple and framework-neutral.
    /// </summary>
    private static LayoutPoint[] DiagonalArrowPoints(RectInfo r, bool pointingUp)
    {
        // The diagonal arrow head points to the upper-right (pointingUp=true) or lower-right (false).
        // We build it as a quadrilateral shaft plus triangular head, all within the icon rect.
        var cx = r.Left + r.Width  / 2;
        var cy = r.Top  + r.Height / 2;

        if (pointingUp)
        {
            // Head points to top-right corner; tail at bottom-left area.
            var headTip = new LayoutPoint(r.Right, r.Top);
            var headLeft  = new LayoutPoint(r.Right - r.Width * 0.44, r.Top);
            var headBottom = new LayoutPoint(r.Right, r.Top + r.Height * 0.44);
            var shaftTL   = new LayoutPoint(r.Left  + r.Width * 0.16, r.Top  + r.Height * 0.36);
            var shaftBL   = new LayoutPoint(r.Left  + r.Width * 0.04, r.Top  + r.Height * 0.52);
            var shaftBR   = new LayoutPoint(r.Right - r.Width * 0.36, r.Bottom - r.Height * 0.04);
            var shaftTR   = new LayoutPoint(r.Right - r.Width * 0.16, r.Bottom - r.Height * 0.16);
            _ = cx;  // suppress unused warning (used for center-based fallback)
            _ = cy;
            return [headTip, headBottom, shaftTR, shaftBR, shaftBL, shaftTL, headLeft];
        }
        else
        {
            // Head points to bottom-right corner; tail at upper-left area.
            var headTip   = new LayoutPoint(r.Right, r.Bottom);
            var headTop   = new LayoutPoint(r.Right, r.Bottom - r.Height * 0.44);
            var headLeft  = new LayoutPoint(r.Right - r.Width * 0.44, r.Bottom);
            var shaftBL   = new LayoutPoint(r.Left  + r.Width * 0.16, r.Bottom - r.Height * 0.36);
            var shaftTL   = new LayoutPoint(r.Left  + r.Width * 0.04, r.Bottom - r.Height * 0.52);
            var shaftTR   = new LayoutPoint(r.Right - r.Width * 0.36, r.Top + r.Height * 0.04);
            var shaftBR   = new LayoutPoint(r.Right - r.Width * 0.16, r.Top + r.Height * 0.16);
            _ = cx;
            _ = cy;
            return [headTip, headLeft, shaftBL, shaftTL, shaftTR, shaftBR, headTop];
        }
    }

    // ── Star (3Stars / 5Stars) ────────────────────────────────────────────────

    /// <summary>
    /// Five-pointed star glyph with a horizontal partial fill proportional to the bucket index.
    /// Bucket 0 (worst) = empty outline only; bucket count-1 (best) = fully filled.
    /// Emits a single <see cref="CfGlyphPrimitiveKind.StarFillFraction"/> op.
    /// </summary>
    private static IReadOnlyList<CfGlyphOp> StarGlyph(int iconIndex, int iconCount, RectInfo r)
    {
        // Fill fraction: 0.0 for the empty star (index 0), 1.0 for full (index count-1).
        // Use (index / (count - 1)) so index 0 → 0 and last index → 1.
        var fraction = iconCount <= 1
            ? 1d
            : Math.Clamp(iconIndex / (double)(iconCount - 1), 0d, 1d);
        return new[] { CfGlyphOp.StarFillFraction(StarPoints(r), fraction) };
    }

    // ── Rating bars (4Rating / 5Rating) ─────────────────────────────────────

    /// <summary>
    /// Graduated bar-chart icon: N filled bar columns of equal width, bottom-aligned, where N is the
    /// number of bars filled. Each bucket index fills progressively more bars (0 = 0 bars filled = all
    /// empty; count-1 = all filled). The bars are drawn as a row of boxes with gray outline, matching
    /// Excel's 4Rating / 5Rating icon-set appearance.
    /// </summary>
    private static IReadOnlyList<CfGlyphOp> RatingBarsGlyph(int iconIndex, int iconCount, RectInfo r)
    {
        // Clamp to ensure sensible values.
        var count = Math.Max(1, iconCount);
        var filled = Math.Clamp(iconIndex, 0, count - 1);  // number of bars filled (0 = none)
        // Each bar takes an equal share of the width with a small gap between bars.
        const double gapFraction = 0.06;
        var totalGap = gapFraction * (count - 1);
        var barWidth = (r.Width * (1 - totalGap)) / count;
        var ops = new List<CfGlyphOp>(count);

        for (var i = 0; i < count; i++)
        {
            // Bar i: left edge at x + i * (barWidth + gap)
            var barLeft = r.Left + i * (barWidth + r.Width * gapFraction);
            // Height scales with bar number: bar 0 (leftmost) is shortest (lowest rank),
            // bar count-1 is tallest (full height), regardless of bucket fill level.
            var heightFraction = (i + 1.0) / count;
            var barHeight = r.Height * heightFraction;
            var barTop    = r.Bottom - barHeight;
            var rect = new LayoutRect(barLeft, barTop, Math.Max(1, barWidth), Math.Max(1, barHeight));

            // Bars up to and including the fill index use icon color; remaining bars use outline only.
            var isFilled = i <= filled;
            ops.Add(CfGlyphOp.Box(
                isFilled ? CfGlyphFill.Icon : CfGlyphFill.None,
                CfGlyphStroke.Outline,
                rect));
        }

        return ops;
    }

    private static LayoutPoint[] TrianglePoints(RectInfo r, bool pointUp) => pointUp
        ? new[]
        {
            new LayoutPoint(r.Left + r.Width / 2, r.Top),
            new LayoutPoint(r.Right, r.Bottom),
            new LayoutPoint(r.Left, r.Bottom),
        }
        : new[]
        {
            new LayoutPoint(r.Left, r.Top),
            new LayoutPoint(r.Right, r.Top),
            new LayoutPoint(r.Left + r.Width / 2, r.Bottom),
        };

    private static LayoutPoint[] DiamondPoints(RectInfo r) => new[]
    {
        new LayoutPoint(r.Left + r.Width / 2, r.Top),
        new LayoutPoint(r.Right, r.Top + r.Height / 2),
        new LayoutPoint(r.Left + r.Width / 2, r.Bottom),
        new LayoutPoint(r.Left, r.Top + r.Height / 2),
    };

    private static CfGlyphOp[] FlagGlyph(RectInfo r)
    {
        var poleX = r.Left + r.Width * 0.25;
        // The pole is an open outline stroke; the banner is a filled quad. (The desktop renderer
        // always drew the pole; the earlier port dropped it — this single source restores it.)
        var pole = new[]
        {
            new LayoutPoint(poleX, r.Bottom),
            new LayoutPoint(poleX, r.Top),
        };
        var banner = new[]
        {
            new LayoutPoint(poleX, r.Top + r.Height * 0.08),
            new LayoutPoint(r.Right, r.Top + r.Height * 0.18),
            new LayoutPoint(r.Right - r.Width * 0.18, r.Top + r.Height * 0.46),
            new LayoutPoint(poleX, r.Top + r.Height * 0.38),
        };
        return new[]
        {
            CfGlyphOp.Polyline(CfGlyphStroke.Outline, pole),
            CfGlyphOp.Polygon(CfGlyphFill.Icon, CfGlyphStroke.Outline, banner),
        };
    }

    private static LayoutPoint[] StarPoints(RectInfo r)
    {
        var center = r.Center;
        var outer = Math.Min(r.Width, r.Height) / 2;
        var inner = outer * 0.45;
        var points = new LayoutPoint[10];
        for (var i = 0; i < 10; i++)
        {
            var radius = i % 2 == 0 ? outer : inner;
            var angle = -Math.PI / 2 + i * Math.PI / 5;
            points[i] = new LayoutPoint(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius);
        }

        return points;
    }
}
