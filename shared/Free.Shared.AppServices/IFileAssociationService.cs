namespace Free.Shared.AppServices;

/// <summary>
/// Registers/unregisters an app as a handler for its supported file types on the current OS.
/// All methods are best-effort: failures are logged by the implementation and never thrown
/// to the caller, so installation/startup is never blocked by association problems.
/// </summary>
public interface IFileAssociationService
{
    /// <summary>Register the app for all of its file-association definitions.</summary>
    void RegisterAll(string executablePath);

    /// <summary>Remove every association this app created. Used on uninstall.</summary>
    void UnregisterAll();

    /// <summary>True if the app is currently the default handler for the given extension.</summary>
    bool IsDefaultHandler(string extension);
}
