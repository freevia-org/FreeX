using FreeX.Core.Model;

namespace FreeX.App.UI;

public readonly record struct WorkbookThemeEffectStyle(
    double ShadowOpacity,
    double ShadowOffsetX,
    double ShadowOffsetY,
    double GlowOpacity = 0,
    double GlowRadius = 0,
    CellColor? GlowColor = null)
{
    public bool HasShadow => ShadowOpacity > 0;
    public bool HasGlow => GlowOpacity > 0 && GlowRadius > 0;

    public static WorkbookThemeEffectStyle FromTheme(WorkbookTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        if (theme.EffectDefaults is { HasAnyEffect: true } effectDefaults)
        {
            return new WorkbookThemeEffectStyle(
                effectDefaults.ShadowOpacity,
                effectDefaults.ShadowOffsetX,
                effectDefaults.ShadowOffsetY,
                effectDefaults.GlowOpacity,
                effectDefaults.GlowRadius,
                effectDefaults.GlowColor);
        }

        return theme.EffectsName.Trim().ToUpperInvariant() switch
        {
            "SUBTLE" => new WorkbookThemeEffectStyle(0.18, 2, 2),
            "REFINED" => new WorkbookThemeEffectStyle(0.28, 3, 3),
            _ => default
        };
    }
}
