using FreeX.Core.Model;

namespace FreeX.App.Presentation.Rendering;

public enum CellFillBackgroundKind
{
    Transparent,
    WhiteFallback,
    Solid,
    LinearGradient,
    RadialGradient,
}

public enum CellGradientSpreadMode
{
    Pad,
}

public enum EmptyCellGradientBehavior
{
    Materialize,
    UseFallback,
}

public enum CellFillFallbackKind
{
    Transparent,
    White,
}

public readonly record struct CellGradientPoint(double X, double Y);

public sealed record CellGradientStopPlan(double Offset, CellColor Color);

public sealed record CellGradientMaterializationPlan(
    CellFillBackgroundKind Kind,
    double NormalizedDegree,
    CellGradientPoint Start,
    CellGradientPoint End,
    CellGradientPoint Center,
    CellGradientPoint Origin,
    double RadiusX,
    double RadiusY,
    CellGradientSpreadMode Spread,
    IReadOnlyList<CellGradientStopPlan> Stops);

public sealed record CellFillMaterializationProfile(
    EmptyCellGradientBehavior EmptyGradientBehavior,
    bool OverlayPatternOnGradient)
{
    public static CellFillMaterializationProfile Wpf { get; } =
        new(EmptyCellGradientBehavior.Materialize, false);

    public static CellFillMaterializationProfile Avalonia { get; } =
        new(EmptyCellGradientBehavior.UseFallback, true);

    public static CellFillMaterializationProfile PatternOverlay { get; } =
        new(EmptyCellGradientBehavior.UseFallback, true);
}

public sealed record CellFillMaterializationPlan(
    CellFillBackgroundKind BackgroundKind,
    CellColor? SolidColor,
    CellGradientMaterializationPlan? Gradient,
    CellFillPatternPlan Pattern,
    CellColor? PatternColor,
    bool HasDeclaredSurface,
    bool HasExplicitPrimaryFill);

/// <summary>
/// Resolves cell fill precedence, fallback, pattern visibility, and portable gradient geometry.
/// Native brush creation and painting remain renderer-owned.
/// </summary>
public static class CellFillMaterializationPlanner
{
    public static CellFillMaterializationPlan Plan(
        CellStyle? style,
        WorkbookTheme theme,
        CellFillMaterializationProfile profile,
        CellFillFallbackKind fallback)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(profile);

        var resolvedFill = style?.ResolveFillColor(theme);
        var declaredGradient = style?.GradientFill;
        var gradient = PlanGradient(declaredGradient, profile.EmptyGradientBehavior);
        var backgroundKind = gradient?.Kind ??
            (resolvedFill.HasValue
                ? CellFillBackgroundKind.Solid
                : fallback == CellFillFallbackKind.White
                    ? CellFillBackgroundKind.WhiteFallback
                    : CellFillBackgroundKind.Transparent);

        var pattern = style is null ||
            (declaredGradient is not null && !profile.OverlayPatternOnGradient)
                ? CellFillPatternPlanner.Plan(CellFillPatternStyle.None)
                : CellFillPatternPlanner.Plan(style.FillPatternStyle);
        CellColor? patternColor = pattern.Kind == CellFillPatternPlanKind.None
            ? null
            : style!.ResolveFillPatternColor(theme) ?? CellColor.Black;
        var hasDeclaredSurface = style is not null &&
            (resolvedFill.HasValue ||
             style.FillPatternStyle != CellFillPatternStyle.None ||
             declaredGradient is not null);

        return new CellFillMaterializationPlan(
            backgroundKind,
            backgroundKind == CellFillBackgroundKind.Solid ? resolvedFill : null,
            gradient,
            pattern,
            patternColor,
            hasDeclaredSurface,
            declaredGradient is not null || resolvedFill.HasValue);
    }

    public static CellGradientMaterializationPlan? PlanGradient(
        CellGradientFill? gradient,
        EmptyCellGradientBehavior emptyGradientBehavior)
    {
        if (gradient is null ||
            (gradient.Stops.Count == 0 && emptyGradientBehavior == EmptyCellGradientBehavior.UseFallback))
        {
            return null;
        }

        var stops = gradient.Stops
            .Select(stop => new CellGradientStopPlan(NormalizeFraction(stop.Position), stop.Color))
            .OrderBy(stop => stop.Offset)
            .ToArray();

        if (gradient.Type == CellGradientFillType.Path)
        {
            var left = NormalizeFraction(gradient.Left);
            var right = NormalizeFraction(gradient.Right);
            var top = NormalizeFraction(gradient.Top);
            var bottom = NormalizeFraction(gradient.Bottom);
            var originX = NormalizeFraction(left + (1.0 - left - right) / 2.0);
            var originY = NormalizeFraction(top + (1.0 - top - bottom) / 2.0);
            var origin = new CellGradientPoint(originX, originY);

            return new CellGradientMaterializationPlan(
                CellFillBackgroundKind.RadialGradient,
                0,
                default,
                default,
                origin,
                origin,
                Math.Max(originX, 1.0 - originX),
                Math.Max(originY, 1.0 - originY),
                CellGradientSpreadMode.Pad,
                Array.AsReadOnly(stops));
        }

        var degree = NormalizeDegree(gradient.Degree);
        var (start, end) = PlanLinearGradientAxis(degree);

        return new CellGradientMaterializationPlan(
            CellFillBackgroundKind.LinearGradient,
            degree,
            start,
            end,
            default,
            default,
            0,
            0,
            CellGradientSpreadMode.Pad,
            Array.AsReadOnly(stops));
    }

    public static (CellGradientPoint Start, CellGradientPoint End) PlanLinearGradientAxis(double degree)
    {
        var radians = NormalizeDegree(degree) * Math.PI / 180.0;
        var dx = Math.Cos(radians);
        var dy = Math.Sin(radians);
        return (
            new CellGradientPoint(0.5 - 0.5 * dx, 0.5 - 0.5 * dy),
            new CellGradientPoint(0.5 + 0.5 * dx, 0.5 + 0.5 * dy));
    }

    private static double NormalizeDegree(double degree)
    {
        if (!double.IsFinite(degree))
            return 0;

        var normalized = degree % 360.0;
        if (normalized < 0)
            normalized += 360.0;
        return normalized == 0 ? 0 : normalized;
    }

    private static double NormalizeFraction(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0.0, 1.0) : 0.0;
}
