namespace FreeX.App.Services.FileAssociations;

/// <summary>
/// Registers/unregisters FreeX as a handler for supported file types on the current OS.
/// All methods are best-effort: failures are logged by the implementation and never thrown
/// to the caller, so installation/startup is never blocked by association problems.
/// </summary>
public interface IFileAssociationService
{
    /// <summary>Register FreeX for all definitions in <see cref="FileAssociationDefinition.All"/>.</summary>
    void RegisterAll(string executablePath);

    /// <summary>Remove every FreeX association this app created. Used on uninstall.</summary>
    void UnregisterAll();

    /// <summary>True if FreeX is currently the default handler for the given extension.</summary>
    bool IsDefaultHandler(string extension);
}
