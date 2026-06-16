namespace Free.Shared.AppServices;

/// <summary>
/// Per-app identity used by the shared storage/diagnostics helpers so each app keeps its
/// own on-disk footprint. <see cref="ProductDirectoryName"/> is the folder under the OS
/// app-data root (e.g. %LOCALAPPDATA%\FreeX); <see cref="DiagnosticsEnvironmentVariable"/>
/// toggles local diagnostics; <see cref="ProductName"/> is the display name used in notices.
/// </summary>
public sealed record AppProductIdentity(
    string ProductDirectoryName,
    string DiagnosticsEnvironmentVariable,
    string ProductName);

/// <summary>
/// Ambient product identity for the shared storage helpers. Each application installs its
/// own identity exactly once, as the very first startup step — before any storage path is
/// resolved — so settings/recent-files/autosave/diagnostics land in that app's own folder.
/// FreeX sets it in Program.Main; FreeW will do the same. The neutral default exists only so
/// the library is never in an undefined state; production code must override it.
/// </summary>
public static class AppProduct
{
    public static AppProductIdentity Current { get; set; } =
        new AppProductIdentity("FreeApp", "FREEAPP_DIAGNOSTICS", "FreeApp");
}
