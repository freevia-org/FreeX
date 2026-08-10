using FreeX.App.Presentation.Ribbon;

namespace FreeX.App.Avalonia.Ribbon;

/// <summary>Compatibility facade for the Avalonia shell's historical command identifiers.</summary>
internal static class AvaloniaCommandIdAdapter
{
    public static string ToCanonical(string avaloniaId) =>
        FreeXRibbonCommandIdentityCatalog.ToCanonical(avaloniaId);

    public static string ToAvalonia(string canonicalId) =>
        FreeXRibbonCommandIdentityCatalog.ToAvalonia(canonicalId);

    public static bool IsKnownAvaloniaId(string avaloniaId) =>
        FreeXRibbonCommandIdentityCatalog.IsKnownAvaloniaId(avaloniaId);

    public static IEnumerable<string> AvaloniaIds =>
        FreeXRibbonCommandIdentityCatalog.AvaloniaIds;

    public static IReadOnlySet<string> OrphanAvaloniaIds =>
        FreeXRibbonCommandIdentityCatalog.OrphanAvaloniaIds;
}
