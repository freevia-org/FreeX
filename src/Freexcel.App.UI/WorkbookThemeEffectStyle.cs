using Freexcel.Core.Model;

namespace Freexcel.App.UI;

public readonly record struct WorkbookThemeEffectStyle(
    double ShadowOpacity,
    double ShadowOffsetX,
    double ShadowOffsetY)
{
    public bool HasShadow => ShadowOpacity > 0;

    public static WorkbookThemeEffectStyle FromTheme(WorkbookTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        return theme.EffectsName.Trim().ToUpperInvariant() switch
        {
            "SUBTLE" => new WorkbookThemeEffectStyle(0.18, 2, 2),
            "REFINED" => new WorkbookThemeEffectStyle(0.28, 3, 3),
            _ => default
        };
    }
}
