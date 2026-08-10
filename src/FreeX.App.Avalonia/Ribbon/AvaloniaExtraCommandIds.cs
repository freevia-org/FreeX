using FreeX.App.Presentation.Ribbon;

namespace FreeX.App.Avalonia.Ribbon;

/// <summary>Compatibility facade for raw canonical command identities wired by the Avalonia shell.</summary>
internal static class AvaloniaExtraCommandIds
{
    public static IReadOnlySet<string> RawCanonical =>
        FreeXRibbonCommandIdentityCatalog.RawCanonicalAvaloniaIds;
}
