using Free.Shared.Drawing;

namespace FreeP.Core.IO;

/// <summary>
/// Maps between OOXML <c>a:prstGeom prst="..."</c> strings and <see cref="DrawingShapeKind"/>.
/// Unknown presets fall back to <see cref="DrawingShapeKind.Rectangle"/>.
/// </summary>
internal static class PptxShapeKindMap
{
    /// <summary>Maps a PresentationML preset geometry name to a DrawingShapeKind.</summary>
    public static DrawingShapeKind FromPreset(string? prst) =>
        DrawingMlPresetGeometryMap.GetShapeKindOrDefault(prst, DrawingShapeKind.Rectangle);

    /// <summary>
    /// Maps a PresentationML preset geometry name to a DrawingShapeKind, also reporting the raw
    /// preset text via <paramref name="unmodeledPreset"/> when it falls back to Rectangle
    /// because FreeP does not model that preset. Callers should stash the returned
    /// unmodeledPreset on <c>SlideShape.UnmodeledPresetGeometry</c> so the original geometry can
    /// be preserved on save instead of being silently replaced by a plain rectangle.
    /// </summary>
    public static DrawingShapeKind FromPreset(string? prst, out string? unmodeledPreset)
    {
        if (DrawingMlPresetGeometryMap.TryGetShapeKind(prst, out var kind))
        {
            unmodeledPreset = null;
            return kind;
        }

        unmodeledPreset = string.IsNullOrWhiteSpace(prst) ? null : prst;
        return DrawingShapeKind.Rectangle;
    }

    /// <summary>Maps a DrawingShapeKind back to a canonical OOXML preset name.</summary>
    public static string ToPreset(DrawingShapeKind kind) =>
        DrawingMlPresetGeometryMap.GetPreset(kind);
}
