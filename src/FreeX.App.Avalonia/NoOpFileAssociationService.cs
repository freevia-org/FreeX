using FreeX.App.Services.FileAssociations;

namespace FreeX.App.Avalonia;

/// <summary>
/// File associations on macOS are declared statically in Info.plist (Launch Services picks
/// them up when the .app is installed), so there is nothing to register at runtime. This no-op
/// satisfies DI on non-Windows targets.
/// </summary>
public sealed class NoOpFileAssociationService : IFileAssociationService
{
    public void RegisterAll(string executablePath) { }

    public void UnregisterAll() { }

    public bool IsDefaultHandler(string extension) => false;
}
