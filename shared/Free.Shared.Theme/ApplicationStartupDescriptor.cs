using Free.Shared.AppServices;

namespace Free.Shared.Theme;

/// <summary>
/// Product identity and theme-selection conventions shared by application composition roots.
/// Product wrappers supply only identity and palette values.
/// </summary>
public sealed record ApplicationStartupDescriptor<TTheme>(
    AppProductIdentity ProductIdentity,
    ApplicationThemeStartupPlan<TTheme> Theme)
    where TTheme : notnull
{
    public static ApplicationStartupDescriptor<TTheme> Create(
        string productName,
        string environmentVariablePrefix,
        TTheme defaultTheme,
        TTheme alternateTheme,
        string alternateThemeValue = "midnight")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentVariablePrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(alternateThemeValue);

        return new ApplicationStartupDescriptor<TTheme>(
            new AppProductIdentity(
                productName,
                $"{environmentVariablePrefix}_DIAGNOSTICS",
                productName),
            new ApplicationThemeStartupPlan<TTheme>(
                $"{environmentVariablePrefix}_THEME",
                alternateThemeValue,
                defaultTheme,
                alternateTheme,
                productName));
    }
}
