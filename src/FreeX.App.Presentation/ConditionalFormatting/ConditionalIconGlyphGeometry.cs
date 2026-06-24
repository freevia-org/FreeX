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
}

/// <summary>
/// Toolkit-neutral geometry emitter for conditional-format icon-set glyphs. Given the glyph kind
/// (resolved by <see cref="ConditionalIconGlyphResolver"/>), the bucket index/count and the target
/// rect, it returns the ordered list of primitive ops to draw. The desktop renderer and the
/// cross-platform port both translate these ops into their own path/shape primitives, so the two draw
/// identical shapes from one source of truth. Pure geometry — no UI-framework dependencies.
/// </summary>
public static class ConditionalIconGlyphGeometry
{
    /// <summary>
    /// Build the ordered primitive ops for a glyph. <paramref name="x"/>/<paramref name="y"/>/
    /// <paramref name="width"/>/<paramref name="height"/> describe the target rect; ops are emitted in
    /// that rect's coordinate space (so a renderer that draws onto a same-sized canvas at the origin
    /// passes 0,0,size,size).
    /// </summary>
    public static IReadOnlyList<CfGlyphOp> Build(
        ConditionalIconGlyphKind glyphKind,
        int iconIndex,
        int iconCount,
        double x,
        double y,
        double width,
        double height)
    {
        var r = new RectInfo(x, y, width, height);
        return glyphKind switch
        {
            ConditionalIconGlyphKind.TrafficLight => new[] { FilledEllipse(r) },
            ConditionalIconGlyphKind.Sign => SignGlyph(iconIndex, r),
            ConditionalIconGlyphKind.Symbol => SymbolGlyph(iconIndex, r),
            ConditionalIconGlyphKind.Flag => FlagGlyph(r),
            ConditionalIconGlyphKind.Rating => new[] { CfGlyphOp.Polygon(CfGlyphFill.Icon, CfGlyphStroke.Outline, StarPoints(r)) },
            ConditionalIconGlyphKind.Quarter => QuarterGlyph(iconIndex, iconCount, r),
            ConditionalIconGlyphKind.Box => new[] { BoxGlyph(iconIndex, iconCount, r) },
            _ => new[] { CfGlyphOp.Polygon(CfGlyphFill.Icon, CfGlyphStroke.Outline, ArrowPoints(r, iconIndex)) },
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

    private static CfGlyphOp[] SymbolGlyph(int iconIndex, RectInfo r)
    {
        if (iconIndex <= 0)
        {
            return new[]
            {
                CfGlyphOp.Polygon(CfGlyphFill.Icon, CfGlyphStroke.Outline, DiamondPoints(r)),
                CfGlyphOp.Line(CfGlyphStroke.WhiteThin, r.Frac(0.32, 0.32), r.Frac(0.68, 0.68)),
                CfGlyphOp.Line(CfGlyphStroke.WhiteThin, r.Frac(0.68, 0.32), r.Frac(0.32, 0.68)),
            };
        }

        if (iconIndex == 1)
        {
            return new[]
            {
                FilledEllipse(r),
                CfGlyphOp.Line(CfGlyphStroke.WhiteThin, r.Frac(0.3, 0.5), r.Frac(0.7, 0.5)),
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
        var sweepFraction = Math.Max(1, iconIndex + 1) / Math.Max(1d, iconCount);
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
    /// Each arrow is a standard 7-point chevron polygon: a rectangular shaft with a wide arrowhead.
    /// iconIndex 0 = up (best), 1 = right/neutral, 2+ = down (worst).
    /// </summary>
    private static LayoutPoint[] ArrowPoints(RectInfo r, int iconIndex)
    {
        if (iconIndex == 1)
        {
            // Sideways (right-pointing) filled chevron arrow — neutral bucket.
            // Shaft occupies left ~55% of the width; arrowhead the right ~45%.
            var shaftTop    = r.Top    + r.Height * 0.30;
            var shaftBottom = r.Bottom - r.Height * 0.30;
            var neckX       = r.Left   + r.Width  * 0.55;
            return new[]
            {
                new LayoutPoint(r.Left,  shaftTop),
                new LayoutPoint(neckX,   shaftTop),
                new LayoutPoint(neckX,   r.Top),
                new LayoutPoint(r.Right, r.Top + r.Height / 2),
                new LayoutPoint(neckX,   r.Bottom),
                new LayoutPoint(neckX,   shaftBottom),
                new LayoutPoint(r.Left,  shaftBottom),
            };
        }

        if (iconIndex == 0)
        {
            // Up arrow — best bucket.
            var shaftLeft  = r.Left + r.Width  * 0.30;
            var shaftRight = r.Left + r.Width  * 0.70;
            var neckY      = r.Top  + r.Height * 0.45;
            return new[]
            {
                new LayoutPoint(r.Left + r.Width / 2, r.Top),
                new LayoutPoint(r.Right,  neckY),
                new LayoutPoint(shaftRight, neckY),
                new LayoutPoint(shaftRight, r.Bottom),
                new LayoutPoint(shaftLeft,  r.Bottom),
                new LayoutPoint(shaftLeft,  neckY),
                new LayoutPoint(r.Left,   neckY),
            };
        }

        // Down arrow — worst bucket.
        var dShaftLeft  = r.Left + r.Width  * 0.30;
        var dShaftRight = r.Left + r.Width  * 0.70;
        var dNeckY      = r.Top  + r.Height * 0.55;
        return new[]
        {
            new LayoutPoint(r.Left + r.Width / 2, r.Bottom),
            new LayoutPoint(r.Right,  dNeckY),
            new LayoutPoint(dShaftRight, dNeckY),
            new LayoutPoint(dShaftRight, r.Top),
            new LayoutPoint(dShaftLeft,  r.Top),
            new LayoutPoint(dShaftLeft,  dNeckY),
            new LayoutPoint(r.Left,   dNeckY),
        };
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
