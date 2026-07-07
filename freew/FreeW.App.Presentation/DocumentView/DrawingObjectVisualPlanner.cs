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
    string FillColorHex,
    string? OutlineColorHex,
    bool Bold);

public sealed record DrawingObjectInlineWordArtPlan(
    DrawingObjectWordArtPlan WordArt,
    DrawingObjectEffectsPlan Effects);

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
        var (fill, outline, bold) = WordArtStyleToColors(wordArt.Style);
        return new DrawingObjectWordArtPlan(
            wordArt.Text,
            wordArt.Style,
            wordArt.Warp,
            Math.Max(1, wordArt.FontSizePt * DipPerPoint),
            fill,
            outline,
            bold);
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
            WordArtStyle.Shadow or WordArtStyle.ShadowOrange => new DrawingObjectEffectsPlan(
                HasShadow: true,
                ShadowColorHex: "#000000",
                ShadowBlurDip: 4,
                ShadowDistanceDip: 3,
                ShadowDirectionDegrees: 45,
                ShadowOpacity: 0.35,
                HasGlow: false,
                GlowColorHex: "#4472C4",
                GlowRadiusDip: 0,
                GlowOpacity: 0,
                HasSoftEdge: false,
                SoftEdgeRadiusDip: 0,
                HasReflection: false,
                HasBevel: false),
            WordArtStyle.GlowBlue or WordArtStyle.GlowGold => new DrawingObjectEffectsPlan(
                HasShadow: false,
                ShadowColorHex: "#000000",
                ShadowBlurDip: 0,
                ShadowDistanceDip: 0,
                ShadowDirectionDegrees: 0,
                ShadowOpacity: 0,
                HasGlow: true,
                GlowColorHex: style == WordArtStyle.GlowGold ? "#FFC000" : "#4472C4",
                GlowRadiusDip: 6,
                GlowOpacity: 0.6,
                HasSoftEdge: false,
                SoftEdgeRadiusDip: 0,
                HasReflection: false,
                HasBevel: false),
            WordArtStyle.Reflection => DrawingObjectEffectsPlan.None with { HasReflection = true },
            WordArtStyle.Bevel => DrawingObjectEffectsPlan.None with { HasBevel = true },
            _ => DrawingObjectEffectsPlan.None
        };

    private static (string FillHex, string? OutlineHex, bool Bold) WordArtStyleToColors(WordArtStyle style) =>
        style switch
        {
            WordArtStyle.FillBlue => ("#4472C4", null, true),
            WordArtStyle.GradientFill => ("#4472C4", null, true),
            WordArtStyle.GradFillMulti => ("#ED7D31", null, true),
            WordArtStyle.Outline => ("#FFFFFF", "#4472C4", true),
            WordArtStyle.ChromeOne => ("#FFFFFF", "#000000", true),
            WordArtStyle.ChromeTwo => ("#4472C4", "#FFFFFF", true),
            WordArtStyle.Shadow => ("#4472C4", null, true),
            WordArtStyle.ShadowOrange => ("#ED7D31", null, true),
            WordArtStyle.FillGold => ("#FFC000", null, true),
            WordArtStyle.FillWhite => ("#FFFFFF", "#AAAAAA", true),
            WordArtStyle.GlowBlue => ("#4472C4", null, true),
            WordArtStyle.GlowGold => ("#FFC000", null, true),
            WordArtStyle.Reflection => ("#4472C4", null, true),
            WordArtStyle.Bevel => ("#4472C4", null, true),
            WordArtStyle.PatternFill => ("#4472C4", "#4472C4", true),
            _ => ("#4472C4", null, true)
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
