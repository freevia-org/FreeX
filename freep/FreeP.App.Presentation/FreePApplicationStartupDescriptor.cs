using Free.Shared.AppServices;
using Free.Shared.Theme;

namespace FreeP.App.Compositor;

/// <summary>Canonical product identity and startup theme policy shared by both FreeP hosts.</summary>
public static class FreePApplicationStartupDescriptor
{
    private static ApplicationStartupDescriptor<Theme> Descriptor { get; } =
        ApplicationStartupDescriptor<Theme>.Create(
            productName: "FreeP",
            environmentVariablePrefix: "FREEP",
            defaultTheme: BrandThemes.FreeP,
            alternateTheme: BrandThemes.FreeXMidnight);

    public static AppProductIdentity ProductIdentity => Descriptor.ProductIdentity;

    public static ApplicationThemeStartupPlan<Theme> Theme => Descriptor.Theme;
}
