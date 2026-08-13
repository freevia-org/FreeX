using Free.Shared.AppServices;
using Free.Shared.Theme;

namespace FreeX.App.Services;

/// <summary>Canonical product identity and startup theme policy shared by both FreeX hosts.</summary>
public static class FreeXApplicationStartupDescriptor
{
    private static ApplicationStartupDescriptor<Theme> Descriptor { get; } =
        ApplicationStartupDescriptor<Theme>.Create(
            productName: "FreeX",
            environmentVariablePrefix: "FREEX",
            defaultTheme: BrandThemes.FreeX,
            alternateTheme: BrandThemes.FreeXMidnight);

    public static AppProductIdentity ProductIdentity => Descriptor.ProductIdentity;

    public static ApplicationThemeStartupPlan<Theme> Theme => Descriptor.Theme;
}
