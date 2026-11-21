using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum ShapeGeometryKind
{
    /// <summary>The shape's own contour list.</summary>
    Contours,

    /// <summary>A rounded rectangle over <see cref="ShapeGeometrySource.Bounds"/> (imported relaxed-inset bevel face).</summary>
    RoundedRect,
}

/// <summary>Renderer-neutral description of the single geometry every step of one shape paints with or is clipped to.</summary>
public readonly record struct ShapeGeometrySource(
    ShapeGeometryKind Kind,
    LayoutRect Bounds,
    double CornerRadiusDip);

/// <summary>One flattened reflection pass: opacity-mask gradient, flip about a pivot, blur-ring offset and pass opacity.</summary>
public readonly record struct ShapeReflectionStep(
    byte MaskAlpha,
    double MaskEndPos,
    bool NeedsTerminalTransparentStop,
    double ScaleY,
    double CenterXDip,
    double PivotYDip,
    double OffsetXDip,
    double OffsetYDip,
    double Opacity);

/// <summary>A filled bevel polygon; the vertices are in drawing (perimeter) order.</summary>
public readonly record struct ShapeBevelWedge(
    LayoutPoint P1,
    LayoutPoint P2,
    LayoutPoint P3,
    LayoutPoint P4,
    SrgbColor Color,
    byte Alpha);

/// <summary>
/// Native sink for <see cref="ShapeRenderSequence"/>. Every method paints one neutral step; the host owns
/// the brushes, pens, geometry objects and the transform stack around the whole sequence.
/// </summary>
public interface IShapeRenderSink
{
    /// <summary>Always the first call: the geometry that later steps fill, stroke or clip to.</summary>
    void UseGeometry(ShapeGeometrySource geometry);

    void FillShadow(ShapeShadowPass pass);

    void StrokeGlow(ShapeGlowPass pass);

    /// <summary>Stroke the geometry with the shape fill brush; the width is already scaled by the outer-spread factor.</summary>
    void StrokeSoftEdge(ShapeSoftEdgePass pass);

    void DrawReflection(ShapeReflectionStep step);

    void FillExtrusion(ShapeMaterialRenderPlan material);

    void StrokeElbowRoute(IReadOnlyList<LayoutPoint> route, bool strokeOutline);

    /// <summary>Fill with the shape fill and, when <paramref name="strokeOutline"/>, stroke with the shape outline.</summary>
    void DrawGeometry(bool strokeOutline);

    void PaintMaterial(ShapeMaterialRenderPlan material);

    /// <summary>Fill the wedges clipped to the geometry.</summary>
    void PaintBevel(IReadOnlyList<ShapeBevelWedge> wedges);

    void StrokeContour(SrgbColor color, double widthDip);
}

/// <summary>
/// The one ordered shape paint sequence for both slide-canvas hosts (the table-cell-border pattern: portable
/// sequence plus native sink). Order: outer effects, extrusion, route/geometry, bevel and contour, then the
/// relaxed-inset/angle material. The WPF behaviour is the reference for every host.
/// </summary>
public static class ShapeRenderSequence
{
    /// <summary>The soft-edge ring stroke is only this fraction of the planned width.</summary>
    public const double SoftEdgeOuterSpreadScale = 0.20;

    private const double RelaxedInsetCornerFraction = 0.16;

    private static readonly SrgbColor HighlightColor = new(255, 255, 255);
    private const byte HighlightAlpha = 120;
    private static readonly SrgbColor ShadeColor = new(0, 0, 0);
    private const byte ShadeAlpha = 110;
    private static readonly SrgbColor DefaultContourColor = new(0x60, 0x60, 0x60);

    public static void Dispatch<TSink>(DrawOp.Shape shape, ref TSink sink)
        where TSink : struct, IShapeRenderSink
    {
        ArgumentNullException.ThrowIfNull(shape);

        var material = ShapeMaterialRenderPlanner.Plan(shape);
        sink.UseGeometry(PlanGeometry(shape, material));

        var fx = shape.Effects;
        bool hasContours = shape.Geometry.Contours.Count > 0;
        bool strokeOutline = fx?.HasSoftEdge != true;

        if (fx is not null && hasContours)
            DispatchOuterEffects(shape, ref sink);

        if (material.Kind == ImportedShapeMaterialKind.IsometricCrossDepth)
            sink.FillExtrusion(material);

        if (shape.ElbowRouteDip is { Count: >= 2 } route)
        {
            sink.StrokeElbowRoute(route, strokeOutline);
        }
        else if (hasContours)
        {
            sink.DrawGeometry(strokeOutline);
            if (material.Kind == ImportedShapeMaterialKind.Circle)
                sink.PaintMaterial(material);
        }

        if (fx is not null && hasContours)
            DispatchBevelAndContour(shape, fx, ref sink);

        if (material.Kind is ImportedShapeMaterialKind.RelaxedInset or ImportedShapeMaterialKind.Angle)
            sink.PaintMaterial(material);
    }

    public static ShapeGeometrySource PlanGeometry(DrawOp.Shape shape, ShapeMaterialRenderPlan material)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(material);

        var bounds = shape.BoundsDip;
        return material.Kind == ImportedShapeMaterialKind.RelaxedInset
            ? new ShapeGeometrySource(
                ShapeGeometryKind.RoundedRect,
                bounds,
                Math.Min(bounds.Width, bounds.Height) * RelaxedInsetCornerFraction)
            : new ShapeGeometrySource(ShapeGeometryKind.Contours, bounds, 0);
    }

    /// <summary>
    /// Highlight/shade wedges for a bevel: a trapezoid per edge in top, bottom, left, right order, followed by
    /// the inner shaded band when the preset is relaxedInset.
    /// </summary>
    public static IReadOnlyList<ShapeBevelWedge> PlanBevelWedges(
        LayoutRect bounds, ResolvedBevel bevel, double lightDirDeg)
    {
        ArgumentNullException.ThrowIfNull(bevel);

        var (highlight, shade) = BevelGeometryHelper.ComputeBevelRegions(bounds, bevel, lightDirDeg);
        var (bw, bh) = BevelGeometryHelper.GetRenderDimensions(bounds, bevel.WidthDip, bevel.HeightDip);
        double x = bounds.X, y = bounds.Y, w = bounds.Width, h = bounds.Height;
        var wedges = new List<ShapeBevelWedge>(8);

        void Add(bool isHighlight, LayoutPoint p1, LayoutPoint p2, LayoutPoint p3, LayoutPoint p4) =>
            wedges.Add(new ShapeBevelWedge(
                p1, p2, p3, p4,
                isHighlight ? HighlightColor : ShadeColor,
                isHighlight ? HighlightAlpha : ShadeAlpha));

        Add(highlight.Top,
            new(x, y), new(x + w, y), new(x + w - bw, y + bh), new(x + bw, y + bh));
        Add(highlight.Bottom,
            new(x + bw, y + h - bh), new(x + w - bw, y + h - bh), new(x + w, y + h), new(x, y + h));
        Add(highlight.Left,
            new(x, y), new(x + bw, y + bh), new(x + bw, y + h - bh), new(x, y + h));
        Add(highlight.Right,
            new(x + w - bw, y + bh), new(x + w, y), new(x + w, y + h), new(x + w - bw, y + h - bh));

        if (string.Equals(bevel.PresetName, "relaxedInset", StringComparison.OrdinalIgnoreCase))
        {
            // PowerPoint's relaxed inset has a second, shaded material band between the outer bevel
            // highlight and the front face.
            double ix = x + bw, iy = y + bh;
            double iw = Math.Max(0, w - 2 * bw), ih = Math.Max(0, h - 2 * bh);
            Add(false,
                new(ix, iy), new(ix + iw, iy), new(ix + iw - bw, iy + bh), new(ix + bw, iy + bh));
            Add(false,
                new(ix + bw, iy + ih - bh), new(ix + iw - bw, iy + ih - bh), new(ix + iw, iy + ih), new(ix, iy + ih));
            Add(false,
                new(ix, iy), new(ix + bw, iy + bh), new(ix + bw, iy + ih - bh), new(ix, iy + ih));
            Add(false,
                new(ix + iw - bw, iy + bh), new(ix + iw, iy), new(ix + iw, iy + ih), new(ix + iw - bw, iy + ih - bh));
        }

        return wedges;
    }

    private static void DispatchOuterEffects<TSink>(DrawOp.Shape shape, ref TSink sink)
        where TSink : struct, IShapeRenderSink
    {
        // A shadow cast by a shape with nothing in it is a dark blob behind transparent text; glow, soft
        // edge and reflection stay (they match the exported PDF).
        bool suppressShadow = shape.Text is not null && shape.Fill is ResolvedFill.None;
        var plan = ResolvedShapeEffectRenderPlanner.PlanOuterEffects(shape.Effects, shape.BoundsDip);

        if (!suppressShadow)
        {
            foreach (var pass in plan.ShadowPasses)
                sink.FillShadow(pass);
        }

        foreach (var pass in plan.GlowPasses)
            sink.StrokeGlow(pass);

        foreach (var pass in plan.SoftEdgePasses)
            sink.StrokeSoftEdge(pass with { StrokeWidthDip = pass.StrokeWidthDip * SoftEdgeOuterSpreadScale });

        if (plan.HasReflection)
        {
            var bounds = shape.BoundsDip;
            double centerX = bounds.X + bounds.Width / 2;
            foreach (var pass in plan.ReflectionPasses)
            {
                sink.DrawReflection(new ShapeReflectionStep(
                    plan.ReflectionAlpha,
                    plan.ReflectionEndPos,
                    plan.ReflectionNeedsTerminalTransparentStop,
                    plan.ReflectionScaleY,
                    centerX,
                    plan.ReflectionPivotYDip,
                    pass.OffsetXDip,
                    pass.OffsetYDip,
                    pass.Opacity));
            }
        }
    }

    private static void DispatchBevelAndContour<TSink>(DrawOp.Shape shape, ResolvedShapeEffects fx, ref TSink sink)
        where TSink : struct, IShapeRenderSink
    {
        // A shape may declare only a:bevelB with no a:bevelT; fall back to the bottom bevel so that case
        // still paints an overlay instead of silently rendering flat.
        var activeBevel = fx.BevelTop ?? fx.BevelBottom;
        if (activeBevel is not null)
        {
            var wedges = PlanBevelWedges(shape.BoundsDip, activeBevel, fx.LightDirDeg);
            sink.PaintBevel(wedges);
        }

        if (fx.ContourWidthDip > 0)
            sink.StrokeContour(fx.ContourColor ?? DefaultContourColor, Math.Max(0.5, fx.ContourWidthDip));
    }
}
