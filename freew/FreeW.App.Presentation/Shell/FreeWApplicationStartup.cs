using Free.Shared.AppServices;
using Free.Shared.Theme;

namespace FreeW.App.Presentation.Shell;

/// <summary>
/// Renderer-neutral FreeW startup identity and decisions. Platform hosts still own their application
/// lifetime, dispatcher, window construction, dialogs, and activation hooks.
/// </summary>
public static class FreeWApplicationStartup
{
    public static AppProductIdentity ProductIdentity { get; } =
        new("FreeW", "FREEW_DIAGNOSTICS", "FreeW");

    public static ApplicationThemeStartupPlan<Theme> Theme { get; } = new(
        EnvironmentVariableName: "FREEW_THEME",
        AlternateThemeValue: "midnight",
        DefaultTheme: BrandThemes.FreeW,
        AlternateTheme: BrandThemes.FreeXMidnight,
        ResourceKeyPrefix: "FreeW");

    /// <summary>
    /// Opens the first existing, supported startup argument. An unreadable startup document silently
    /// falls back to the host's default document, matching the existing launch failure policy.
    /// </summary>
    public static DocumentOpenResult? TryOpenStartupDocument(
        IReadOnlyList<string> startupArguments,
        DocumentPersistenceWorkflow persistence)
    {
        ArgumentNullException.ThrowIfNull(startupArguments);
        ArgumentNullException.ThrowIfNull(persistence);

        var path = startupArguments.FirstOrDefault(argument =>
            File.Exists(argument) && persistence.CanOpenPath(argument));
        if (path is null)
            return null;

        try
        {
            return persistence.Open(path);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
