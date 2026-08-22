using Free.Shared.AppServices;
using Free.Shared.Theme;

namespace FreeW.App.Presentation.Shell;

/// <summary>
/// Renderer-neutral FreeW startup identity and decisions. Platform hosts still own their application
/// lifetime, dispatcher, window construction, dialogs, and activation hooks.
/// </summary>
public static class FreeWApplicationStartup
{
    private static ApplicationStartupDescriptor<Theme> Descriptor { get; } =
        ApplicationStartupDescriptor<Theme>.Create(
            productName: "FreeW",
            environmentVariablePrefix: "FREEW",
            defaultTheme: BrandThemes.FreeW,
            alternateTheme: BrandThemes.FreeWMidnight);

    public static AppProductIdentity ProductIdentity => Descriptor.ProductIdentity;

    public static ApplicationThemeStartupPlan<Theme> Theme => Descriptor.Theme;

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

        var plan = StartupFileOpenPlanner.Plan(
            startupArguments,
            new StartupFileOpenPolicy(
                persistence.CanOpenPath,
                MaximumOpenableFiles: 1));
        var entry = plan.Entries.SingleOrDefault();
        if (entry is null)
            return null;

        try
        {
            return persistence.Open(entry.Path);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
