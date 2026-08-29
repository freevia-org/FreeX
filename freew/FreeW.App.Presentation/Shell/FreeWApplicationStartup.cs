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
    /// Plans every existing, supported startup argument -- not just the first. shared-startup-args
    /// F1: this used to cap <see cref="StartupFileOpenPolicy.MaximumOpenableFiles"/> at exactly one
    /// candidate, so a launch with several file arguments (several documents dragged onto the
    /// dock/taskbar icon in one gesture, which the OS delivers as multiple path arguments to a
    /// single process) silently dropped every argument past the first -- despite FreeW's own
    /// packaging declaring multi-file support (Linux desktop entry
    /// <c>Exec=freew %F</c>) and every other shell in this codebase (FreeX, FreeP, and FreeW's own WPF
    /// host) opening every one, each in its own window. Uncapped, the plan already deduplicates a
    /// path repeated in argv down to one entry (see <see cref="StartupFileOpenPlanner"/>'s
    /// <c>seenPaths</c> guard), so callers get "one window per distinct file" for free. Hosts open
    /// <see cref="StartupFileOpenPlan.Entries"/>[0] (marked <c>OpenInNewWindow: false</c>) into their
    /// primary window via <see cref="TryOpenStartupDocument(StartupFileOpenEntry, DocumentPersistenceWorkflow)"/>
    /// and every remaining entry (<c>OpenInNewWindow: true</c>) each in a brand-new window.
    /// </summary>
    public static StartupFileOpenPlan PlanStartupDocuments(
        IReadOnlyList<string> startupArguments,
        DocumentPersistenceWorkflow persistence)
    {
        ArgumentNullException.ThrowIfNull(startupArguments);
        ArgumentNullException.ThrowIfNull(persistence);

        return StartupFileOpenPlanner.Plan(
            startupArguments,
            new StartupFileOpenPolicy(persistence.CanOpenPath));
    }

    /// <summary>
    /// Opens a single planned startup-file entry (see <see cref="PlanStartupDocuments"/>). An
    /// unreadable document silently returns null, matching the existing launch failure policy -- the
    /// host reports it (or not) however it already reports any other failed Open.
    /// </summary>
    public static DocumentOpenResult? TryOpenStartupDocument(
        StartupFileOpenEntry entry,
        DocumentPersistenceWorkflow persistence)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(persistence);

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
