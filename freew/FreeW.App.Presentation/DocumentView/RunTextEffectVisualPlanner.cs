using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>One resolved gradient stop: offset in 0..1 and a concrete colour.</summary>
public sealed record TextEffectGradientStopPlan(double Offset, TextEffectArgb Color);

/// <summary>A resolved glyph or outline fill, ready for a renderer to turn into a brush.</summary>
public sealed record TextEffectFillPlan
{
    /// <summary>True for <c>w14:noFill</c>: nothing is painted.</summary>
    public bool IsNone { get; init; }

    /// <summary>The solid colour, or null for a gradient.</summary>
    public TextEffectArgb? Solid { get; init; }

    /// <summary>Gradient stops in ascending offset (empty for a solid fill).</summary>
    public IReadOnlyList<TextEffectGradientStopPlan> Stops { get; init; } = [];

    /// <summary>Linear gradient angle in degrees, clockwise, 0 = left to right.</summary>
    public double AngleDegrees { get; init; }

    /// <summary>True for a path (circle/rect/shape) gradient, drawn as a radial gradient.</summary>
    public bool IsRadial { get; init; }
}

/// <summary>A resolved glyph outline.</summary>
public sealed record TextEffectOutlinePlan(
    double WidthDip,
    TextEffectFillPlan Fill,
    IReadOnlyList<double> DashPattern,
    bool RoundCap,
    TextOutlineJoin Join);

/// <summary>A resolved outer shadow: an offset, blurred, tinted copy of the glyphs.</summary>
public sealed record TextEffectShadowPlan(double OffsetXDip, double OffsetYDip, double BlurDip, double Scale, TextEffectArgb Color);

/// <summary>A resolved glow: a blurred halo of the glyphs grown by <see cref="RadiusDip"/>.</summary>
public sealed record TextEffectGlowPlan(double RadiusDip, TextEffectArgb Color);

/// <summary>
/// A resolved reflection: the glyphs mirrored about their baseline area, offset down by
/// <see cref="DistanceDip"/>, fading from <see cref="StartOpacity"/> at <see cref="StartOffset"/> to
/// <see cref="EndOpacity"/> at <see cref="EndOffset"/> (offsets are fractions of the glyph height).
/// </summary>
public sealed record TextEffectReflectionPlan(
    double BlurDip,
    double DistanceDip,
    double StartOpacity,
    double StartOffset,
    double EndOpacity,
    double EndOffset);

/// <summary>
/// Everything a renderer needs to draw a body run's text effects, with theme colours resolved and
/// EMU converted to device-independent pixels. A null member means "that effect is absent".
/// </summary>
public sealed record RunTextEffectVisualPlan
{
    public TextEffectFillPlan? Fill { get; init; }
    public TextEffectOutlinePlan? Outline { get; init; }
    public TextEffectShadowPlan? Shadow { get; init; }
    public TextEffectGlowPlan? Glow { get; init; }
    public TextEffectReflectionPlan? Reflection { get; init; }

    /// <summary>True when something must be drawn beyond recolouring the glyphs themselves.</summary>
    public bool NeedsOverlay => Outline is not null || Shadow is not null || Glow is not null || Reflection is not null;

    public bool IsEmpty => Fill is null && !NeedsOverlay;
}

/// <summary>
/// Turns a run's stored text effects into a renderer-neutral drawing plan. Effects that are absent,
/// that the typed layer cannot interpret, or that have no drawable form (3-D scene and bevel) simply
/// produce no plan member — the run is then drawn without them and still round-trips untouched.
/// </summary>
public static class RunTextEffectVisualPlanner
{
    /// <summary>Hard cap on blur/glow extents so a hostile document cannot request a huge raster.</summary>
    public const double MaxExtentDip = 96;

    private const double DipPerPoint = 96.0 / 72.0;

    public static RunTextEffectVisualPlan? Build(RunTextEffects? effects, ThemeColorScheme scheme)
    {
        ArgumentNullException.ThrowIfNull(scheme);
        if (effects is null || !effects.HasVisualEffects())
            return null;

        var plan = new RunTextEffectVisualPlan
        {
            Fill = effects.GetFill() is { } fill ? BuildFill(fill, scheme) : null,
            Outline = BuildOutline(effects.GetOutline(), scheme),
            Shadow = BuildShadow(effects.GetShadow(), scheme),
            Glow = BuildGlow(effects.GetGlow(), scheme),
            Reflection = BuildReflection(effects.GetReflection()),
        };
        return plan.IsEmpty ? null : plan;
    }

    private static TextEffectFillPlan BuildFill(TextFillEffect fill, ThemeColorScheme scheme)
    {
        switch (fill.Kind)
        {
            case TextFillKind.None:
                return new TextEffectFillPlan { IsNone = true };
            case TextFillKind.Gradient when fill.Stops.Count >= 2:
                return new TextEffectFillPlan
                {
                    Stops = fill.Stops
                        .OrderBy(stop => stop.Position)
                        .Select(stop => new TextEffectGradientStopPlan(
                            Math.Clamp(stop.Position / 100000.0, 0, 1),
                            stop.Color.Resolve(scheme)))
                        .ToArray(),
                    AngleDegrees = fill.LinearAngleDegrees,
                    IsRadial = fill.GradientKind != TextGradientKind.Linear,
                };
            default:
                var color = fill.Color ?? fill.Stops.FirstOrDefault()?.Color ?? TextEffectColor.FromScheme("tx1");
                return new TextEffectFillPlan { Solid = color.Resolve(scheme) };
        }
    }

    private static TextEffectOutlinePlan? BuildOutline(TextOutlineEffect? outline, ThemeColorScheme scheme)
    {
        if (outline is null || outline.IsInvisible || outline.Fill is null)
            return null;

        var widthDip = Math.Min(outline.WidthPt * DipPerPoint, MaxExtentDip);
        return new TextEffectOutlinePlan(
            widthDip,
            BuildFill(outline.Fill, scheme),
            DashPattern(outline.Dash),
            RoundCap: outline.Cap == "rnd",
            outline.Join);
    }

    private static TextEffectShadowPlan? BuildShadow(TextShadowEffect? shadow, ThemeColorScheme scheme)
    {
        if (shadow is null)
            return null;

        var color = shadow.Color.Resolve(scheme);
        if (color.A == 0)
            return null;

        var distanceDip = Math.Min(shadow.DistancePt * DipPerPoint, MaxExtentDip);
        var radians = shadow.DirectionDegrees * Math.PI / 180.0;
        return new TextEffectShadowPlan(
            Round(distanceDip * Math.Cos(radians)),
            Round(distanceDip * Math.Sin(radians)),
            Math.Min(shadow.BlurRadiusPt * DipPerPoint, MaxExtentDip),
            Math.Clamp(shadow.ScaleX / 100000.0, 0.1, 4),
            color);
    }

    private static TextEffectGlowPlan? BuildGlow(TextGlowEffect? glow, ThemeColorScheme scheme)
    {
        if (glow is null || glow.RadiusEmu <= 0)
            return null;

        var color = glow.Color.Resolve(scheme);
        return color.A == 0
            ? null
            : new TextEffectGlowPlan(Math.Min(glow.RadiusPt * DipPerPoint, MaxExtentDip), color);
    }

    private static TextEffectReflectionPlan? BuildReflection(TextReflectionEffect? reflection)
    {
        if (reflection is null || (reflection.StartOpacity <= 0 && reflection.EndOpacity <= 0))
            return null;

        return new TextEffectReflectionPlan(
            Math.Min(reflection.BlurRadiusPt * DipPerPoint, MaxExtentDip),
            Math.Min(reflection.DistancePt * DipPerPoint, MaxExtentDip),
            reflection.StartOpacity / 100000.0,
            reflection.StartPosition / 100000.0,
            reflection.EndOpacity / 100000.0,
            Math.Max(reflection.EndPosition, reflection.StartPosition) / 100000.0);
    }

    /// <summary>Preset dash tokens as on/off lengths in multiples of the pen width (empty = solid).</summary>
    private static IReadOnlyList<double> DashPattern(string? dash) => dash switch
    {
        "sysDot" => [1, 1],
        "dot" => [1, 3],
        "sysDash" => [3, 1],
        "dash" => [4, 3],
        "dashDot" => [4, 3, 1, 3],
        "lgDash" => [8, 3],
        "lgDashDot" => [8, 3, 1, 3],
        "lgDashDotDot" => [8, 3, 1, 3, 1, 3],
        "sysDashDot" => [3, 1, 1, 1],
        "sysDashDotDot" => [3, 1, 1, 1, 1, 1],
        _ => [],
    };

    private static double Round(double value) => Math.Round(value, 4);
}
