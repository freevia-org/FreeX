using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using FreeW.App.Presentation.DocumentView;
using TextEffectArgb = FreeW.Core.Model.TextEffectArgb;
using TextOutlineJoin = FreeW.Core.Model.TextOutlineJoin;

namespace FreeW.App.Host.Editing;

/// <summary>
/// The one drawing implementation of Word's outline / shadow / glow / reflection text effects, shared by
/// the on-screen overlay (<see cref="DocumentView.TextEffectsAdorner"/>, which finds the glyphs from the
/// editor's live character rectangles) and the print / Print Preview / PDF / XPS path
/// (<see cref="TextEffectsPrintPaginator"/>, which finds them in the laid-out page's glyph runs). Both hand
/// it the run's resolved <see cref="RunTextEffectVisualPlan"/> and its glyph outlines, one geometry per
/// line, and it paints in Word's order: blurred shadow and glow first, the reflection, then the glyphs
/// again in their own fill (so a shadow or glow never covers them) and the outline on top.
/// </summary>
internal static class TextEffectsPainter
{
    /// <summary>One laid-out line of a run's glyph outlines.</summary>
    internal sealed class GlyphLine(double top, double bottom, double left)
    {
        public double Top { get; } = top;
        public double Bottom { get; private set; } = bottom;
        public double Left { get; private set; } = left;
        public GeometryGroup Geometry { get; } = new() { FillRule = FillRule.Nonzero };

        public void Add(Geometry geometry, double bottom, double left)
        {
            Geometry.Children.Add(geometry);
            Bottom = Math.Max(Bottom, bottom);
            Left = Math.Min(Left, left);
        }
    }

    /// <summary>
    /// Paints one run's effects. <paramref name="glyphBrush"/> is the run's glyph fill (null when the
    /// effect fill is "no fill"); blurred layers go to <paramref name="addLayer"/> as their own visuals so
    /// each blur applies to that layer only, and the sharp parts go to <paramref name="top"/>.
    /// </summary>
    public static void Paint(
        RunTextEffectVisualPlan plan,
        IReadOnlyList<GlyphLine> lines,
        Brush? glyphBrush,
        DrawingContext top,
        Action<Visual> addLayer)
    {
        if (lines.Count == 0)
            return;

        var all = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var line in lines)
            all.Children.Add(line.Geometry);
        all.Freeze();

        if (plan.Shadow is { } shadow)
        {
            var transform = new TransformGroup();
            if (Math.Abs(shadow.Scale - 1) > 0.001)
            {
                var box = all.Bounds;
                transform.Children.Add(new ScaleTransform(shadow.Scale, shadow.Scale, box.X + box.Width / 2, box.Y + box.Height / 2));
            }

            transform.Children.Add(new TranslateTransform(shadow.OffsetXDip, shadow.OffsetYDip));
            transform.Freeze();
            var shadowBrush = Frozen(new SolidColorBrush(ToWpfColor(shadow.Color)));
            addLayer(BlurredLayer(shadow.BlurDip, context =>
            {
                context.PushTransform(transform);
                context.DrawGeometry(shadowBrush, null, all);
                context.Pop();
            }));
        }

        if (plan.Glow is { } glow)
        {
            var glowBrush = Frozen(new SolidColorBrush(ToWpfColor(glow.Color)));
            var pen = new Pen(glowBrush, glow.RadiusDip) { LineJoin = PenLineJoin.Round };
            pen.Freeze();
            addLayer(BlurredLayer(glow.RadiusDip, context => context.DrawGeometry(glowBrush, pen, all)));
        }

        if (plan.Reflection is { } reflection)
        {
            var reflectionBrush = glyphBrush ?? Brushes.Black;
            foreach (var line in lines)
            {
                var height = Math.Max(1, line.Bottom - line.Top);
                var mask = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new(Color.FromArgb(ToAlpha(reflection.StartOpacity), 0, 0, 0), reflection.StartOffset),
                        new(Color.FromArgb(ToAlpha(reflection.EndOpacity), 0, 0, 0), Math.Max(reflection.StartOffset, reflection.EndOffset)),
                        new(Color.FromArgb(0, 0, 0, 0), Math.Min(1, Math.Max(reflection.StartOffset, reflection.EndOffset) + 0.0001)),
                    },
                    new Point(0, 0),
                    new Point(0, 1));
                mask.Freeze();
                var mirrorAxis = line.Bottom + reflection.DistanceDip / 2;
                var geometry = line.Geometry;
                addLayer(BlurredLayer(reflection.BlurDip, context =>
                {
                    context.PushOpacityMask(MaskBelow(mask, line, mirrorAxis, height));
                    context.PushTransform(new ScaleTransform(1, -1, 0, mirrorAxis));
                    context.DrawGeometry(reflectionBrush, null, geometry);
                    context.Pop();
                    context.Pop();
                }));
            }
        }

        // The glyphs again over their own blurred effects, then the outline (Word strokes on top).
        if (glyphBrush is not null && (plan.Shadow is not null || plan.Glow is not null))
            top.DrawGeometry(glyphBrush, null, all);

        if (plan.Outline is { } outline)
        {
            var pen = new Pen(CreateFillBrush(outline.Fill), Math.Max(0.25, outline.WidthDip))
            {
                LineJoin = outline.Join switch
                {
                    TextOutlineJoin.Miter => PenLineJoin.Miter,
                    TextOutlineJoin.Bevel => PenLineJoin.Bevel,
                    _ => PenLineJoin.Round,
                },
                StartLineCap = outline.RoundCap ? PenLineCap.Round : PenLineCap.Flat,
                EndLineCap = outline.RoundCap ? PenLineCap.Round : PenLineCap.Flat,
                DashCap = outline.RoundCap ? PenLineCap.Round : PenLineCap.Flat,
            };
            if (outline.DashPattern.Count > 0)
                pen.DashStyle = new DashStyle(outline.DashPattern, 0);
            pen.Freeze();
            top.DrawGeometry(null, pen, all);
        }
    }

    /// <summary>A text-effect fill as a frozen WPF brush (solid, linear or radial gradient, or no fill).</summary>
    public static Brush CreateFillBrush(TextEffectFillPlan fill)
    {
        if (fill.IsNone)
            return Brushes.Transparent;

        Brush brush;
        if (fill.Solid is not null || fill.Stops.Count < 2)
        {
            var color = fill.Solid ?? fill.Stops.FirstOrDefault()?.Color ?? new TextEffectArgb(255, 0, 0, 0);
            brush = new SolidColorBrush(ToWpfColor(color));
        }
        else
        {
            var stops = new GradientStopCollection(fill.Stops.Select(stop => new System.Windows.Media.GradientStop(ToWpfColor(stop.Color), stop.Offset)));
            if (fill.IsRadial)
            {
                brush = new RadialGradientBrush(stops);
            }
            else
            {
                var radians = fill.AngleDegrees * Math.PI / 180.0;
                var dx = Math.Cos(radians) / 2;
                var dy = Math.Sin(radians) / 2;
                brush = new LinearGradientBrush(stops, new Point(0.5 - dx, 0.5 - dy), new Point(0.5 + dx, 0.5 + dy));
            }
        }

        brush.Freeze();
        return brush;
    }

    public static Color ToWpfColor(TextEffectArgb color) => Color.FromArgb(color.A, color.R, color.G, color.B);

    private static DrawingVisual BlurredLayer(double blurDip, Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
            draw(context);
        if (blurDip > 0.5)
            visual.Effect = new BlurEffect { Radius = blurDip, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
        return visual;
    }

    // The reflection fades with distance below its line: the mask spans one line height under the axis.
    private static Brush MaskBelow(LinearGradientBrush mask, GlyphLine line, double axis, double height)
    {
        var brush = mask.CloneCurrentValue();
        brush.MappingMode = BrushMappingMode.Absolute;
        brush.StartPoint = new Point(line.Left, axis);
        brush.EndPoint = new Point(line.Left, axis + height);
        brush.Freeze();
        return brush;
    }

    private static byte ToAlpha(double opacity) => (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    /// <summary>
    /// The glyph lines of every tagged effect run drawn on a laid-out page: walks the page visual's
    /// drawings (accumulating each visual's offset/transform and each drawing group's transform), keeps the
    /// glyph runs whose foreground brush is one of <paramref name="plans"/>' keys, and groups their outlines
    /// by brush and baseline. Brush identity is what ties a printed glyph run back to its model run.
    /// </summary>
    public static IReadOnlyList<(Brush Brush, RunTextEffectVisualPlan Plan, List<GlyphLine> Lines)> CollectPageGlyphLines(
        Visual pageVisual,
        IReadOnlyDictionary<Brush, RunTextEffectVisualPlan> plans)
    {
        var byBrush = new Dictionary<Brush, (RunTextEffectVisualPlan Plan, List<GlyphLine> Lines)>(ReferenceEqualityComparer.Instance);
        if (plans.Count > 0)
            WalkVisual(pageVisual, Matrix.Identity, plans, byBrush);
        return byBrush.Select(item => (item.Key, item.Value.Plan, item.Value.Lines)).ToList();
    }

    private static void WalkVisual(
        Visual visual,
        Matrix transform,
        IReadOnlyDictionary<Brush, RunTextEffectVisualPlan> plans,
        Dictionary<Brush, (RunTextEffectVisualPlan Plan, List<GlyphLine> Lines)> found)
    {
        if (VisualTreeHelper.GetDrawing(visual) is { } drawing)
            WalkDrawing(drawing, transform, plans, found);

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(visual); index++)
        {
            if (VisualTreeHelper.GetChild(visual, index) is not Visual child)
                continue;

            var childTransform = Matrix.Identity;
            if (VisualTreeHelper.GetTransform(child) is { } local)
                childTransform = local.Value;
            var offset = VisualTreeHelper.GetOffset(child);
            childTransform.Translate(offset.X, offset.Y);
            WalkVisual(child, childTransform * transform, plans, found);
        }
    }

    private static void WalkDrawing(
        Drawing drawing,
        Matrix transform,
        IReadOnlyDictionary<Brush, RunTextEffectVisualPlan> plans,
        Dictionary<Brush, (RunTextEffectVisualPlan Plan, List<GlyphLine> Lines)> found)
    {
        switch (drawing)
        {
            case DrawingGroup group:
                var groupTransform = group.Transform is { } local ? local.Value * transform : transform;
                foreach (var child in group.Children)
                    WalkDrawing(child, groupTransform, plans, found);
                break;

            case GlyphRunDrawing { GlyphRun: { } glyphRun, ForegroundBrush: { } brush }
                when plans.TryGetValue(brush, out var plan):
                var geometry = glyphRun.BuildGeometry().CloneCurrentValue();
                var matrix = new MatrixTransform(transform);
                matrix.Freeze();
                geometry.Transform = matrix;
                geometry.Freeze();

                var box = glyphRun.ComputeAlignmentBox();
                box.Offset(glyphRun.BaselineOrigin.X, glyphRun.BaselineOrigin.Y);
                box.Transform(transform);
                if (!found.TryGetValue(brush, out var entry))
                {
                    entry = (plan, new List<GlyphLine>());
                    found[brush] = entry;
                }

                var line = entry.Lines.FirstOrDefault(candidate => Math.Abs(candidate.Top - box.Top) < 0.5);
                if (line is null)
                {
                    line = new GlyphLine(box.Top, box.Bottom, box.Left);
                    entry.Lines.Add(line);
                }

                line.Add(geometry, box.Bottom, box.Left);
                break;
        }
    }
}
