using Free.Shared.AppServices;
using Free.Shared.Theme;

namespace FreeX.App.Services;

/// <summary>Canonical product identity and startup theme policy shared by both FreeX hosts.</summary>
public static class FreeXApplicationStartupDescriptor
{
    public static AppProductIdentity ProductIdentity { get; } =
        new("FreeX", "FREEX_DIAGNOSTICS", "FreeX");

    public static ApplicationThemeStartupPlan<Theme> Theme { get; } = new(
        EnvironmentVariableName: "FREEX_THEME",
        AlternateThemeValue: "midnight",
        DefaultTheme: BrandThemes.FreeX,
        AlternateTheme: BrandThemes.FreeXMidnight,
        ResourceKeyPrefix: "FreeX");
}
