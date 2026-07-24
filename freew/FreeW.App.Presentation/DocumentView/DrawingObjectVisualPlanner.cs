using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

public enum DrawingObjectVisualKind
{
    Image,
    Shape,
    Chart,
    WordArt,
    SmartArt,
    Group
}

public enum DrawingObjectGeometryKind
{
    Rectangle,
    RoundedRectangle,
    Ellipse,
    TextBox,
    Custom
}

public enum DrawingObjectFillKind
{
    None,
    Solid,
    Gradient,
    Pattern
}

public sealed record DrawingObjectGradientStopPlan(int Position, string ColorHex);

public sealed record DrawingObjectFillPlan(
    DrawingObjectFillKind Kind,
    string? ColorHex,
    int GradientAngle,
    IReadOnlyList<DrawingObjectGradientStopPlan> GradientStops,
    string? PatternPreset,
    string? PatternForegroundColorHex,
    string? PatternBackgroundColorHex)
{
    public static DrawingObjectFillPlan None { get; } = new(
        DrawingObjectFillKind.None,
        ColorHex: null,
        GradientAngle: 0,
        GradientStops: [],
        PatternPreset: null,
        PatternForegroundColorHex: null,
        PatternBackgroundColorHex: null);

    public string Summary
    {
        get
        {
            return Kind switch
            {
                DrawingObjectFillKind.Solid => "solid:" + (ColorHex ?? "none"),
                DrawingObjectFillKind.Gradient => "gradient:"
                    + GradientAngle.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + ":"
                    + string.Join("/", GradientStops.Select(stop =>
                        stop.Position.ToString(System.Globalization.CultureInfo.InvariantCulture) + "=" + stop.ColorHex)),
                DrawingObjectFillKind.Pattern => "pattern:"
                    + (PatternPreset ?? "none")
                    + ":"
                    + (PatternForegroundColorHex ?? "none")
                    + "/"
                    + (PatternBackgroundColorHex ?? "none"),
                _ => "none"
            };
        }
    }
}

public sealed record DrawingObjectOutlinePlan(
    bool IsVisible,
    string? ColorHex,
    double WidthDip,
    string? DashStyle);

public sealed record DrawingObjectTextPlan(
    string Text,
    ShapeTextDirection Direction);

public sealed record DrawingObjectEffectsPlan(
    bool HasShadow,
    string ShadowColorHex,
    double ShadowBlurDip,
    double ShadowDistanceDip,
    double ShadowDirectionDegrees,
    double ShadowOpacity,
    bool HasGlow,
    string GlowColorHex,
    double GlowRadiusDip,
    double GlowOpacity,
    bool HasSoftEdge,
    double SoftEdgeRadiusDip,
    bool HasReflection,
    bool HasBevel)
{
    public static DrawingObjectEffectsPlan None { get; } = new(
        HasShadow: false,
        ShadowColorHex: "#000000",
        ShadowBlurDip: 0,
        ShadowDistanceDip: 0,
        ShadowDirectionDegrees: 0,
        ShadowOpacity: 0,
        HasGlow: false,
        GlowColorHex: "#4472C4",
        GlowRadiusDip: 0,
        GlowOpacity: 0,
        HasSoftEdge: false,
        SoftEdgeRadiusDip: 0,
        HasReflection: false,
        HasBevel: false);

    public bool HasAny => HasShadow || HasGlow || HasSoftEdge || HasReflection || HasBevel;

    public string Summary
    {
        get
        {
            var parts = new List<string>(capacity: 5);
            if (HasShadow) parts.Add("shadow");
            if (HasGlow) parts.Add("glow");
            if (HasSoftEdge) parts.Add("soft-edge");
            if (HasReflection) parts.Add("reflection");
            if (HasBevel) parts.Add("bevel");
            return parts.Count == 0 ? "none" : string.Join(", ", parts);
        }
    }
}

public sealed record DrawingObjectWordArtPlan(
    string Text,
    WordArtStyle Style,
    WordArtWarp Warp,
    double FontSizeDip,
    DrawingObjectFillPlan Fill,
    DrawingObjectOutlinePlan Outline,
    bool Bold,
    string WarpHint)
{
    public string FillColorHex =>
        Fill.ColorHex
        ?? Fill.GradientStops.FirstOrDefault()?.ColorHex
        ?? Fill.PatternForegroundColorHex
        ?? "#1F4E79";

    public string? OutlineColorHex => Outline.IsVisible ? Outline.ColorHex : null;

    public bool HasPatternFill => Fill.Kind == DrawingObjectFillKind.Pattern;

    public string StyleSummary =>
        "style:" + Style
        + ";fill:" + Fill.Summary
        + ";outline:" + (Outline.IsVisible ? Outline.ColorHex ?? "visible" : "none")
        + ";bold:" + (Bold ? "true" : "false")
        + ";warp:" + WarpHint;
}

public sealed record DrawingObjectWordArtGlyphPlacementPlan(
    double CenterXNormalized,
    double CenterYNormalized,
    double RotationRadians);

public sealed record DrawingObjectWordArtPlacementPlan(
    WordArtWarp Warp,
    IReadOnlyList<DrawingObjectWordArtGlyphPlacementPlan> Glyphs);

public sealed record DrawingObjectInlineWordArtPlan(
    DrawingObjectWordArtPlan WordArt,
    DrawingObjectEffectsPlan Effects)
{
    public string Summary => WordArt.StyleSummary + ";effects:" + Effects.Summary;
}

public sealed record DrawingObjectImagePlan(
    ImageFormat Format,
    int ByteLength,
    bool HasCrop,
    bool HasAdjustments,
    bool HasRecolor,
    bool HasEffects,
    bool HasArtisticEffect);

public sealed record DrawingObjectGroupChildVisualPlan(
    int ChildIndex,
    double OffsetXDip,
    double OffsetYDip,
    DrawingObjectVisualPlan Visual);

public sealed record DrawingObjectVisualPlan(
    DrawingObjectVisualKind Kind,
    DocumentFloatRect Rect,
    ImageWrapping Wrapping,
    int ZOrderIndex,
    bool BehindText,
    double RotationAngle,
    bool FlipH,
    bool FlipV,
    DrawingObjectGeometryKind? GeometryKind,
    CustomGeometry? CustomGeometry,
    DrawingObjectFillPlan Fill,
    DrawingObjectOutlinePlan Outline,
    DrawingObjectTextPlan? Text,
    DrawingObjectWordArtPlan? WordArt,
    DrawingObjectEffectsPlan Effects,
    IReadOnlyList<DrawingObjectGroupChildVisualPlan> GroupChildren,
    DrawingObjectImagePlan? Image,
    ChartVisualPlan? Chart,
    SmartArtVisualPlan? SmartArt);

public static class DrawingObjectVisualPlanner
{
    private const double DipPerPoint = 96.0 / 72.0;
    private const double WordArtOutlineWidthDip = DipPerPoint * 0.75;

    public static DrawingObjectVisualPlan BuildVisualPlan(
        Shape shape,
        DocumentFloatingObjectSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(snapshot);

        return new DrawingObjectVisualPlan(
            DrawingObjectVisualKind.Shape,
            snapshot.Rect,
            snapshot.Wrapping,
            snapshot.ZOrderIndex,
            snapshot.BehindText,
            shape.RotationAngle,
            shape.FlipH,
            shape.FlipV,
            shape.HasCustomGeometry ? DrawingObjectGeometryKind.Custom : ToGeometryKind(shape.Kind),
            shape.HasCustomGeometry ? shape.CustomGeometry : null,
            BuildFillPlan(shape),
            BuildOutlinePlan(shape),
            shape.HasText ? new DrawingObjectTextPlan(shape.PlainText, shape.TextDirection) : null,
            WordArt: null,
            Effects: BuildEffectsPlan(shape.Effects),
            GroupChildren: [],
            Image: null,
            Chart: null,
            SmartArt: null);
    }

    public static DrawingObjectVisualPlan BuildVisualPlan(
        WordArt wordArt,
        DocumentFloatingObjectSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(wordArt);
        ArgumentNullException.ThrowIfNull(snapshot);
        var inlinePlan = BuildInlineWordArtPlan(wordArt);

        return new DrawingObjectVisualPlan(
            DrawingObjectVisualKind.WordArt,
            snapshot.Rect,
            snapshot.Wrapping,
            snapshot.ZOrderIndex,
            snapshot.BehindText,
            RotationAngle: 0,
            FlipH: false,
            FlipV: false,
            GeometryKind: null,
            CustomGeometry: null,
            Fill: DrawingObjectFillPlan.None,
            Outline: new DrawingObjectOutlinePlan(false, null, 0, null),
            Text: null,
            WordArt: inlinePlan.WordArt,
            Effects: inlinePlan.Effects,
            GroupChildren: [],
            Image: null,
            Chart: null,
            SmartArt: null);
    }

    public static DrawingObjectVisualPlan BuildVisualPlan(
        InlineImage image,
        DocumentFloatingObjectSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(snapshot);

        return new DrawingObjectVisualPlan(
            DrawingObjectVisualKind.Image,
            snapshot.Rect,
            snapshot.Wrapping,
            snapshot.ZOrderIndex,
            snapshot.BehindText,
            image.RotationAngle,
            image.FlipH,
            image.FlipV,
            GeometryKind: null,
            CustomGeometry: null,
            Fill: DrawingObjectFillPlan.None,
            Outline: new DrawingObjectOutlinePlan(false, null, 0, null),
            Text: null,
            WordArt: null,
            Effects: DrawingObjectEffectsPlan.None,
            GroupChildren: [],
            Image: BuildImagePlan(image),
            Chart: null,
            SmartArt: null);
    }

    public static DrawingObjectVisualPlan BuildVisualPlan(
        Chart chart,
        DocumentFloatingObjectSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(snapshot);

        return new DrawingObjectVisualPlan(
            DrawingObjectVisualKind.Chart,
            snapshot.Rect,
            snapshot.Wrapping,
            snapshot.ZOrderIndex,
            snapshot.BehindText,
            RotationAngle: 0,
            FlipH: false,
            FlipV: false,
            GeometryKind: null,
            CustomGeometry: null,
            Fill: DrawingObjectFillPlan.None,
            Outline: new DrawingObjectOutlinePlan(false, null, 0, null),
            Text: null,
            WordArt: null,
            Effects: DrawingObjectEffectsPlan.None,
            GroupChildren: [],
            Image: null,
            Chart: ChartSmartArtVisualPlanner.BuildChartPlan(chart),
            SmartArt: null);
    }

    public static DrawingObjectVisualPlan BuildVisualPlan(
        SmartArt smartArt,
        DocumentFloatingObjectSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(smartArt);
        ArgumentNullException.ThrowIfNull(snapshot);

        return new DrawingObjectVisualPlan(
            DrawingObjectVisualKind.SmartArt,
            snapshot.Rect,
            snapshot.Wrapping,
            snapshot.ZOrderIndex,
            snapshot.BehindText,
            RotationAngle: 0,
            FlipH: false,
            FlipV: false,
            GeometryKind: null,
            CustomGeometry: null,
            Fill: DrawingObjectFillPlan.None,
            Outline: new DrawingObjectOutlinePlan(false, null, 0, null),
            Text: null,
            WordArt: null,
            Effects: DrawingObjectEffectsPlan.None,
            GroupChildren: [],
            Image: null,
            Chart: null,
            SmartArt: ChartSmartArtVisualPlanner.BuildSmartArtPlan(smartArt));
    }

    public static DrawingObjectInlineWordArtPlan BuildInlineWordArtPlan(WordArt wordArt)
    {
        ArgumentNullException.ThrowIfNull(wordArt);

        return new DrawingObjectInlineWordArtPlan(
            BuildWordArtPlan(wordArt),
            BuildWordArtEffectsPlan(wordArt.Style));
    }

    public static DrawingObjectWordArtPlacementPlan BuildWordArtPlacementPlan(
        WordArtWarp warp,
        IReadOnlyList<double> glyphWidths,
        double boundsWidthDip,
        double boundsHeightDip)
    {
        ArgumentNullException.ThrowIfNull(glyphWidths);
        if (glyphWidths.Count == 0 || boundsWidthDip <= 0 || boundsHeightDip <= 0)
            return new DrawingObjectWordArtPlacementPlan(warp, []);

        var totalWidth = glyphWidths.Sum();
        if (totalWidth <= 0 || warp is not (WordArtWarp.ArchUp or WordArtWarp.Wave1))
            return new DrawingObjectWordArtPlacementPlan(warp, []);

        var halfSpan = totalWidth / 2;
        var normalizedHalfSpan = Math.Max(1, halfSpan);
        var normalizedTotalWidth = Math.Max(1, totalWidth);
        var currentX = boundsWidthDip / 2 - halfSpan;
        var archDepth = Math.Min(boundsHeightDip * 0.28, Math.Max(3, totalWidth * 0.12));
        var waveAmplitude = Math.Min(boundsHeightDip * 0.08, Math.Max(2, totalWidth * 0.0275));
        var placements = new List<DrawingObjectWordArtGlyphPlacementPlan>(glyphWidths.Count);

        foreach (var width in glyphWidths)
        {
            var centerX = currentX + width / 2;
            var normalizedX = (centerX - boundsWidthDip / 2) / normalizedHalfSpan;
            double centerY;
            double tangent;
            if (warp == WordArtWarp.ArchUp)
            {
                centerY = boundsHeightDip / 2 - archDepth / 2 + archDepth * normalizedX * normalizedX;
                tangent = 2 * archDepth * normalizedX / normalizedHalfSpan;
            }
            else
            {
                var progress = (centerX - (boundsWidthDip / 2 - halfSpan)) / normalizedTotalWidth;
                var phase = Math.PI * 2 * progress;
                centerY = boundsHeightDip / 2 + waveAmplitude * Math.Sin(phase);
                tangent = waveAmplitude * Math.PI * 2 * Math.Cos(phase) / normalizedTotalWidth;
            }

            placements.Add(new DrawingObjectWordArtGlyphPlacementPlan(
                centerX / boundsWidthDip,
                centerY / boundsHeightDip,
                Math.Atan(tangent)));
            currentX += width;
        }

        return new DrawingObjectWordArtPlacementPlan(warp, placements);
    }

    public static DrawingObjectVisualPlan BuildVisualPlan(
        DrawingGroup group,
        DocumentFloatingObjectSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(snapshot);

        var children = new List<DrawingObjectGroupChildVisualPlan>();
        var childSnapshots = DocumentViewLayoutPlanner.BuildFloatingGroupChildSnapshots(group, snapshot.Rect);
        foreach (var childSnapshot in childSnapshots)
        {
            if (childSnapshot.ChildIndex < 0 || childSnapshot.ChildIndex >= group.Children.Count)
                continue;

            var child = group.Children[childSnapshot.ChildIndex];
            DrawingObjectVisualPlan? childPlan = child switch
            {
                InlineImage image => BuildVisualPlan(image, ChildSnapshot(snapshot, childSnapshot, image)),
                Shape shape => BuildVisualPlan(shape, ChildSnapshot(snapshot, childSnapshot, shape)),
                Chart chart => BuildVisualPlan(chart, ChildSnapshot(snapshot, childSnapshot, chart)),
                WordArt wordArt => BuildVisualPlan(wordArt, ChildSnapshot(snapshot, childSnapshot, wordArt)),
                SmartArt smartArt => BuildVisualPlan(smartArt, ChildSnapshot(snapshot, childSnapshot, smartArt)),
                _ => null
            };

            if (childPlan is null)
                continue;

            children.Add(new DrawingObjectGroupChildVisualPlan(
                childSnapshot.ChildIndex,
                childSnapshot.Rect.XDip - snapshot.Rect.XDip,
                childSnapshot.Rect.YDip - snapshot.Rect.YDip,
                childPlan));
        }

        return new DrawingObjectVisualPlan(
            DrawingObjectVisualKind.Group,
            snapshot.Rect,
            snapshot.Wrapping,
            snapshot.ZOrderIndex,
            snapshot.BehindText,
            RotationAngle: group.RotationAngle,
            FlipH: group.FlipH,
            FlipV: group.FlipV,
            GeometryKind: null,
            CustomGeometry: null,
            Fill: DrawingObjectFillPlan.None,
            Outline: new DrawingObjectOutlinePlan(false, null, 0, null),
            Text: null,
            WordArt: null,
            Effects: DrawingObjectEffectsPlan.None,
            GroupChildren: children,
            Image: null,
            Chart: null,
            SmartArt: null);
    }

    private static DocumentFloatingObjectSnapshot ChildSnapshot(
        DocumentFloatingObjectSnapshot groupSnapshot,
        DocumentFloatingGroupChildSnapshot childSnapshot,
        InlineImage image) =>
        new(
            DocumentFloatingObjectKind.Image,
            groupSnapshot.BlockIndex,
            groupSnapshot.RunIndex,
            childSnapshot.Rect,
            groupSnapshot.BehindText,
            groupSnapshot.ZOrderIndex,
            groupSnapshot.Wrapping,
            image.RotationAngle,
            image.FlipH,
            image.FlipV);

    private static DocumentFloatingObjectSnapshot ChildSnapshot(
        DocumentFloatingObjectSnapshot groupSnapshot,
        DocumentFloatingGroupChildSnapshot childSnapshot,
        Shape shape) =>
        new(
            DocumentFloatingObjectKind.Shape,
            groupSnapshot.BlockIndex,
            groupSnapshot.RunIndex,
            childSnapshot.Rect,
            groupSnapshot.BehindText,
            groupSnapshot.ZOrderIndex,
            groupSnapshot.Wrapping,
            shape.RotationAngle,
            shape.FlipH,
            shape.FlipV);

    private static DocumentFloatingObjectSnapshot ChildSnapshot(
        DocumentFloatingObjectSnapshot groupSnapshot,
        DocumentFloatingGroupChildSnapshot childSnapshot,
        Chart _) =>
        new(
            DocumentFloatingObjectKind.Chart,
            groupSnapshot.BlockIndex,
            groupSnapshot.RunIndex,
            childSnapshot.Rect,
            groupSnapshot.BehindText,
            groupSnapshot.ZOrderIndex,
            groupSnapshot.Wrapping);

    private static DocumentFloatingObjectSnapshot ChildSnapshot(
        DocumentFloatingObjectSnapshot groupSnapshot,
        DocumentFloatingGroupChildSnapshot childSnapshot,
        WordArt _) =>
        new(
            DocumentFloatingObjectKind.WordArt,
            groupSnapshot.BlockIndex,
            groupSnapshot.RunIndex,
            childSnapshot.Rect,
            groupSnapshot.BehindText,
            groupSnapshot.ZOrderIndex,
            groupSnapshot.Wrapping);

    private static DocumentFloatingObjectSnapshot ChildSnapshot(
        DocumentFloatingObjectSnapshot groupSnapshot,
        DocumentFloatingGroupChildSnapshot childSnapshot,
        SmartArt _) =>
        new(
            DocumentFloatingObjectKind.SmartArt,
            groupSnapshot.BlockIndex,
            groupSnapshot.RunIndex,
            childSnapshot.Rect,
            groupSnapshot.BehindText,
            groupSnapshot.ZOrderIndex,
            groupSnapshot.Wrapping);

    private static DrawingObjectGeometryKind ToGeometryKind(ShapeKind kind) =>
        kind switch
        {
            ShapeKind.Ellipse => DrawingObjectGeometryKind.Ellipse,
            ShapeKind.RoundedRectangle => DrawingObjectGeometryKind.RoundedRectangle,
            ShapeKind.TextBox => DrawingObjectGeometryKind.TextBox,
            _ => DrawingObjectGeometryKind.Rectangle
        };

    private static DrawingObjectFillPlan BuildFillPlan(Shape shape)
    {
        if (shape.ExtendedFill is { } fill)
        {
            return fill.Kind switch
            {
                ShapeFillKind.NoFill => DrawingObjectFillPlan.None,
                ShapeFillKind.Gradient => new DrawingObjectFillPlan(
                    DrawingObjectFillKind.Gradient,
                    ColorHex: null,
                    fill.GradientAngle,
                    fill.GradientStops
                        .Select(stop => new DrawingObjectGradientStopPlan(stop.Position, NormalizeHex(stop.ColorHex, "#000000")))
                        .ToList(),
                    PatternPreset: null,
                    PatternForegroundColorHex: null,
                    PatternBackgroundColorHex: null),
                ShapeFillKind.Pattern => new DrawingObjectFillPlan(
                    DrawingObjectFillKind.Pattern,
                    ColorHex: null,
                    GradientAngle: 0,
                    GradientStops: [],
                    fill.PatternPreset,
                    NormalizeHex(fill.PatternFgColorHex, "#4472C4"),
                    NormalizeHex(fill.PatternBgColorHex, "#FFFFFF")),
                _ => DrawingObjectFillPlan.None
            };
        }

        return string.IsNullOrWhiteSpace(shape.FillColorHex)
            ? DrawingObjectFillPlan.None
            : new DrawingObjectFillPlan(
                DrawingObjectFillKind.Solid,
                NormalizeHex(shape.FillColorHex, "#000000"),
                GradientAngle: 0,
                GradientStops: [],
                PatternPreset: null,
                PatternForegroundColorHex: null,
                PatternBackgroundColorHex: null);
    }

    private static DrawingObjectOutlinePlan BuildOutlinePlan(Shape shape)
    {
        if (string.IsNullOrWhiteSpace(shape.OutlineColorHex))
            return new DrawingObjectOutlinePlan(false, null, 0, shape.OutlineDash);

        return new DrawingObjectOutlinePlan(
            true,
            NormalizeHex(shape.OutlineColorHex, "#808080"),
            Math.Max(0.75, shape.OutlineWidthPt * DipPerPoint),
            shape.OutlineDash);
    }

    private static DrawingObjectEffectsPlan BuildEffectsPlan(ShapeEffectLst? effects)
    {
        if (effects is null)
            return DrawingObjectEffectsPlan.None;

        return new DrawingObjectEffectsPlan(
            effects.HasShadow,
            NormalizeHex(effects.ShadowColorHex, "#000000"),
            EmuToDip(effects.ShadowBlurRad),
            EmuToDip(effects.ShadowDist),
            effects.ShadowDir / 60000.0,
            Math.Clamp(effects.ShadowAlpha / 100000.0, 0, 1),
            effects.HasGlow,
            NormalizeHex(effects.GlowColorHex, "#4472C4"),
            EmuToDip(effects.GlowRad),
            Math.Clamp(effects.GlowAlpha / 100000.0, 0, 1),
            effects.HasSoftEdge,
            EmuToDip(effects.SoftEdgeRad),
            effects.HasReflection,
            effects.HasBevel);
    }

    private static DrawingObjectWordArtPlan BuildWordArtPlan(WordArt wordArt)
    {
        var (fill, outline, _) = BuildWordArtStylePlan(wordArt.Style);
        return new DrawingObjectWordArtPlan(
            wordArt.Text,
            wordArt.Style,
            wordArt.Warp,
            Math.Max(1, wordArt.FontSizePt * DipPerPoint),
            fill,
            outline,
            false,
            BuildWordArtWarpHint(wordArt.Warp));
    }

    private static DrawingObjectImagePlan BuildImagePlan(InlineImage image) =>
        new(
            image.Format,
            image.Bytes.Length,
            image.HasCrop,
            image.HasAdjustments,
            image.HasRecolor,
            image.HasEffects,
            image.HasArtisticEffect);

    private static DrawingObjectEffectsPlan BuildWordArtEffectsPlan(WordArtStyle style) =>
        style switch
        {
            WordArtStyle.Shadow => WordArtShadow("#2E2E2E", 50800, 38100, 0.4),
            WordArtStyle.ChromeTwo => WordArtShadow("#1F4E79", 38100, 25400, 0.3),
            WordArtStyle.ShadowOrange => WordArtShadow("#ED7D31", 50800, 38100, 0.5),
            WordArtStyle.GlowBlue or WordArtStyle.GlowGold => new DrawingObjectEffectsPlan(
                HasShadow: false,
                ShadowColorHex: "#000000",
                ShadowBlurDip: 0,
                ShadowDistanceDip: 0,
                ShadowDirectionDegrees: 0,
                ShadowOpacity: 0,
                HasGlow: true,
                GlowColorHex: style == WordArtStyle.GlowGold ? "#C09000" : "#2E75B6",
                GlowRadiusDip: EmuToDip(101600),
                GlowOpacity: 0.6,
                HasSoftEdge: false,
                SoftEdgeRadiusDip: 0,
                HasReflection: false,
                HasBevel: false),
            WordArtStyle.Reflection => DrawingObjectEffectsPlan.None with { HasReflection = true },
            WordArtStyle.Bevel => DrawingObjectEffectsPlan.None with { HasBevel = true },
            _ => DrawingObjectEffectsPlan.None
        };

    private static DrawingObjectEffectsPlan WordArtShadow(
        string colorHex,
        int blurRad,
        int dist,
        double opacity) =>
        new(
            HasShadow: true,
            ShadowColorHex: colorHex,
            ShadowBlurDip: EmuToDip(blurRad),
            ShadowDistanceDip: EmuToDip(dist),
            ShadowDirectionDegrees: 45,
            ShadowOpacity: opacity,
            HasGlow: false,
            GlowColorHex: "#4472C4",
            GlowRadiusDip: 0,
            GlowOpacity: 0,
            HasSoftEdge: false,
            SoftEdgeRadiusDip: 0,
            HasReflection: false,
            HasBevel: false);

    private static (DrawingObjectFillPlan Fill, DrawingObjectOutlinePlan Outline, bool Bold) BuildWordArtStylePlan(WordArtStyle style) =>
        style switch
        {
            WordArtStyle.GradientFill => (
                GradientFill(5400000, (0, "#4472C4"), (100000, "#ED7D31")),
                NoWordArtOutline(),
                true),
            WordArtStyle.GradFillMulti => (
                GradientFill(5400000, (0, "#FF6000"), (50000, "#C00000"), (100000, "#7030A0")),
                NoWordArtOutline(),
                true),
            WordArtStyle.FillGold => (
                GradientFill(5400000, (0, "#C09000"), (100000, "#8B6200")),
                NoWordArtOutline(),
                true),
            WordArtStyle.ChromeOne => (
                GradientFill(5400000, (0, "#C0C0C0"), (35000, "#FFFFFF"), (65000, "#A0A8B0"), (100000, "#E8E8E8")),
                WordArtOutline("#242424", WordArtOutlineWidthDip * 2),
                true),
            WordArtStyle.PatternFill => (
                new DrawingObjectFillPlan(
                    DrawingObjectFillKind.Pattern,
                    ColorHex: null,
                    GradientAngle: 0,
                    GradientStops: [],
                    PatternPreset: "diagCross",
                    PatternForegroundColorHex: "#1F4E79",
                    PatternBackgroundColorHex: "#FFFFFF"),
                WordArtOutline("#1F4E79"),
                true),
            WordArtStyle.Outline => (SolidFill("#1F4E79"), WordArtOutline("#2E2E2E"), true),
            WordArtStyle.ChromeTwo => (SolidFill("#FFFFFF"), WordArtOutline("#1F4E79", DipPerPoint), true),
            WordArtStyle.FillWhite => (SolidFill("#FFFFFF"), WordArtOutline("#242424"), true),
            WordArtStyle.ShadowOrange or WordArtStyle.Bevel => (SolidFill("#ED7D31"), NoWordArtOutline(), true),
            WordArtStyle.GlowBlue or WordArtStyle.GlowGold => (SolidFill("#242424"), NoWordArtOutline(), true),
            WordArtStyle.FillBlue or WordArtStyle.Shadow or WordArtStyle.Reflection => (SolidFill("#1F4E79"), NoWordArtOutline(), true),
            _ => (SolidFill("#1F4E79"), NoWordArtOutline(), true)
        };

    private static DrawingObjectFillPlan SolidFill(string colorHex) =>
        new(
            DrawingObjectFillKind.Solid,
            colorHex,
            GradientAngle: 0,
            GradientStops: [],
            PatternPreset: null,
            PatternForegroundColorHex: null,
            PatternBackgroundColorHex: null);

    private static DrawingObjectFillPlan GradientFill(int angle, params (int Position, string ColorHex)[] stops) =>
        new(
            DrawingObjectFillKind.Gradient,
            ColorHex: null,
            angle,
            stops.Select(stop => new DrawingObjectGradientStopPlan(stop.Position, stop.ColorHex)).ToList(),
            PatternPreset: null,
            PatternForegroundColorHex: null,
            PatternBackgroundColorHex: null);

    private static DrawingObjectOutlinePlan NoWordArtOutline() =>
        new(false, null, 0, null);

    private static DrawingObjectOutlinePlan WordArtOutline(string colorHex, double widthDip = WordArtOutlineWidthDip) =>
        new(true, colorHex, widthDip, null);

    private static string BuildWordArtWarpHint(WordArtWarp warp) =>
        warp switch
        {
            WordArtWarp.None => "none",
            WordArtWarp.ArchUp or WordArtWarp.ArchDown or WordArtWarp.Circle or WordArtWarp.Button => "arc",
            WordArtWarp.Wave1 or WordArtWarp.Wave2 => "wave",
            WordArtWarp.Inflate or WordArtWarp.Deflate or WordArtWarp.InflateBottom => "inflate",
            WordArtWarp.ChevronUp or WordArtWarp.ChevronDown => "chevron",
            WordArtWarp.FadeRight or WordArtWarp.FadeLeft => "fade",
            WordArtWarp.SlantUp or WordArtWarp.SlantDown => "slant",
            _ => "custom"
        };

    private static double EmuToDip(int emu) =>
        emu / 12700.0 * DipPerPoint;

    private static string NormalizeHex(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var hex = value.Trim();
        if (hex.StartsWith('#'))
            hex = hex[1..];
        if (hex.Length == 8)
            hex = hex[2..];
        if (hex.Length != 6)
            return fallback;

        return int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out _)
            ? "#" + hex.ToUpperInvariant()
            : fallback;
    }
}
