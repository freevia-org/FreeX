using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Renderer-neutral geometry of the DrawingML preset text warps (<c>a:prstTxWarp</c>, ECMA-376
/// presetTextWarpDefinitions) at their default adjust values. A warp maps the text's own bounding box
/// onto the shape's text rectangle: <see cref="Map"/> takes a point of the unwarped text as fractions
/// (<c>u</c> across, <c>v</c> down, both 0..1) and returns where it lands as fractions of the text
/// rectangle. Envelope warps follow the preset's top and bottom guide paths — a point at height
/// <c>v</c> is interpolated between the two paths at <c>u</c>; arc warps bend the text box around the
/// inscribed ellipse. Renderers flatten the text outline and map every vertex.
/// </summary>
public static class WordArtWarpGeometry
{
    // Default adjust values of the presets (ECMA-376 presetTextWarpDefinitions, 100000 = 1).
    private const double WaveAmplitude = 0.125;     // textWave1 / textWave2 adj1 12500 (of the height, x10/3 bezier)
    private const double InflateDepth = 0.1875;     // textInflate adj 18750
    private const double DeflateDepth = 0.1875;     // textDeflate adj 18750
    private const double InflateBottomTop = 0.6;    // textInflateBottom adj 60000
    private const double ChevronDepth = 0.25;       // textChevron adj 25000 / textChevronInverted 75000
    private const double FadeDepth = 0.33333;       // textFadeRight / textFadeLeft adj 33333
    private const double SlantDepth = 0.55556;      // textSlantUp adj 55556 / textSlantDown adj 44445

    /// <summary>True for every warp this geometry can map (all presets except <see cref="WordArtWarp.None"/>).</summary>
    public static bool Supports(WordArtWarp warp) => warp != WordArtWarp.None;

    /// <summary>True for the presets that bend the text around the inscribed ellipse instead of an envelope.</summary>
    public static bool IsArc(WordArtWarp warp) =>
        warp is WordArtWarp.ArchUp or WordArtWarp.ArchDown or WordArtWarp.Circle or WordArtWarp.Button;

    /// <summary>
    /// Maps a point of the unwarped text box (<paramref name="u"/>, <paramref name="v"/> as fractions) onto
    /// the text rectangle (fractions). <paramref name="textAspect"/> is the unwarped text's height divided by
    /// its width; arc warps use it to keep the bent glyph band proportional to the text.
    /// </summary>
    public static (double X, double Y) Map(WordArtWarp warp, double u, double v, double textAspect = 0.25)
    {
        if (IsArc(warp))
            return MapArc(warp, u, v, textAspect);

        var (top, bottom) = Envelope(warp, u);
        return (u, top + (bottom - top) * v);
    }

    /// <summary>The top and bottom guide paths of an envelope warp at <paramref name="u"/> (fractions of the height).</summary>
    public static (double Top, double Bottom) Envelope(WordArtWarp warp, double u)
    {
        u = Math.Clamp(u, 0, 1);
        var centered = 1 - 2 * u;
        // The wave presets draw each guide as one cubic bezier whose control points sit 10/3 of the
        // amplitude above and below the end height: y = y0 - 10a * u(1-u)(1-2u).
        var wave = 10 * WaveAmplitude * u * (1 - u) * centered;
        return warp switch
        {
            WordArtWarp.Wave1 => (WaveAmplitude - wave, 1 - WaveAmplitude - wave),
            WordArtWarp.Wave2 => (WaveAmplitude + wave, 1 - WaveAmplitude + wave),
            WordArtWarp.Inflate => (InflateDepth * centered * centered, 1 - InflateDepth * centered * centered),
            WordArtWarp.Deflate => (DeflateDepth * (1 - centered * centered), 1 - DeflateDepth * (1 - centered * centered)),
            WordArtWarp.InflateBottom => (0, InflateBottomTop + (1 - InflateBottomTop) * (1 - centered * centered)),
            WordArtWarp.ChevronUp => (ChevronDepth * Math.Abs(centered), 1 - ChevronDepth * (1 - Math.Abs(centered))),
            WordArtWarp.ChevronDown => (ChevronDepth * (1 - Math.Abs(centered)), 1 - ChevronDepth * Math.Abs(centered)),
            WordArtWarp.FadeRight => (FadeDepth / 2 * u, 1 - FadeDepth / 2 * u),
            WordArtWarp.FadeLeft => (FadeDepth / 2 * (1 - u), 1 - FadeDepth / 2 * (1 - u)),
            WordArtWarp.SlantUp => (SlantDepth * (1 - u), 1 - SlantDepth * u),
            WordArtWarp.SlantDown => (SlantDepth * u, 1 - SlantDepth * (1 - u)),
            _ => (0, 1),
        };
    }

    private static (double X, double Y) MapArc(WordArtWarp warp, double u, double v, double textAspect)
    {
        u = Math.Clamp(u, 0, 1);
        v = Math.Clamp(v, 0, 1);
        // Text bent around an arc keeps its proportions: a band whose thickness is the text's height
        // relative to the arc length it covers, bounded so the glyphs stay legible.
        var sweep = warp == WordArtWarp.Circle ? 2 * Math.PI : Math.PI;
        var band = Math.Clamp(sweep * 0.5 * Math.Max(0, textAspect), 0.12, 0.6);
        double angle;
        double radius;
        switch (warp)
        {
            case WordArtWarp.ArchDown:
                // Along the bottom half from left to right, glyph tops toward the centre.
                angle = Math.PI - Math.PI * u;
                radius = 1 - band + band * v;
                break;
            case WordArtWarp.Circle:
                // Once around from the left edge over the top, glyph tops outward.
                angle = Math.PI + 2 * Math.PI * u;
                radius = 1 - band * v;
                break;
            default:
                // textArchUp / textButton: along the top half from left to right, glyph tops outward.
                angle = Math.PI + Math.PI * u;
                radius = 1 - band * v;
                break;
        }

        return (0.5 + 0.5 * radius * Math.Cos(angle), 0.5 + 0.5 * radius * Math.Sin(angle));
    }
}
