namespace FreeP.App.Compositor;

/// <summary>Portable geometry for the non-interactive View ruler chrome.</summary>
public readonly record struct PresentationRulerTick(double Offset, double Length, string? Label);

public static class PresentationRulerTickPlanner
{
    public const double RulerThickness = 18;
    public const double MinorIntervalDip = 24;
    public const int MinorTicksPerMajor = 4;

    public static IReadOnlyList<PresentationRulerTick> BuildHorizontal(SlideTransformCore transform) =>
        Build(transform.OffsetX, transform.SlideWidthDip, transform.Scale);

    public static IReadOnlyList<PresentationRulerTick> BuildVertical(SlideTransformCore transform) =>
        Build(transform.OffsetY, transform.SlideHeightDip, transform.Scale);

    private static IReadOnlyList<PresentationRulerTick> Build(
        double origin,
        double slideLengthDip,
        double scale)
    {
        if (!double.IsFinite(origin) || !double.IsFinite(slideLengthDip) || !double.IsFinite(scale)
            || slideLengthDip <= 0 || scale <= 0)
            return Array.Empty<PresentationRulerTick>();

        var count = (int)Math.Ceiling(slideLengthDip / MinorIntervalDip);
        var ticks = new PresentationRulerTick[count + 1];
        for (var index = 0; index <= count; index++)
        {
            var major = index % MinorTicksPerMajor == 0;
            ticks[index] = new PresentationRulerTick(
                origin + index * MinorIntervalDip * scale,
                major ? 12 : 6,
                major ? (index / MinorTicksPerMajor).ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
        }

        return ticks;
    }
}
