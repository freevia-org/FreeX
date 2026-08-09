namespace Free.Shared.Theme;

/// <summary>
/// Describes product theme selection at application startup without depending on a native UI stack.
/// The plan owns environment interpretation and guarantees that active-theme state is updated before
/// native resources are materialized.
/// </summary>
public sealed record ApplicationThemeStartupPlan<TTheme>(
    string EnvironmentVariableName,
    string AlternateThemeValue,
    TTheme DefaultTheme,
    TTheme AlternateTheme,
    string ResourceKeyPrefix)
    where TTheme : notnull
{
    public TTheme Resolve(Func<string, string?> getEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        ArgumentException.ThrowIfNullOrWhiteSpace(EnvironmentVariableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(AlternateThemeValue);

        return string.Equals(
            getEnvironmentVariable(EnvironmentVariableName),
            AlternateThemeValue,
            StringComparison.OrdinalIgnoreCase)
            ? AlternateTheme
            : DefaultTheme;
    }

    public TTheme Apply(
        Func<string, string?> getEnvironmentVariable,
        Action<TTheme> setActiveTheme,
        Action<TTheme, string> applyNativeTheme)
    {
        ArgumentNullException.ThrowIfNull(setActiveTheme);
        ArgumentNullException.ThrowIfNull(applyNativeTheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(ResourceKeyPrefix);

        var theme = Resolve(getEnvironmentVariable);
        setActiveTheme(theme);
        applyNativeTheme(theme, ResourceKeyPrefix);
        return theme;
    }
}
