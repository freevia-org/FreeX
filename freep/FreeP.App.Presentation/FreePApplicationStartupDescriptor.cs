using Free.Shared.AppServices;
using Free.Shared.Theme;

namespace FreeP.App.Presentation;

/// <summary>Canonical product identity and startup theme policy shared by both FreeP hosts.</summary>
public static class FreePApplicationStartupDescriptor
{
    public static AppProductIdentity ProductIdentity { get; } =
        new("FreeP", "FREEP_DIAGNOSTICS", "FreeP");

    public static ApplicationThemeStartupPlan<Theme> Theme { get; } = new(
        EnvironmentVariableName: "FREEP_THEME",
        AlternateThemeValue: "midnight",
        DefaultTheme: BrandThemes.FreeP,
        AlternateTheme: BrandThemes.FreeXMidnight,
        ResourceKeyPrefix: "FreeP");
}
