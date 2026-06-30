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

    /// <summary>Maps a DrawingShapeKind back to a canonical OOXML preset name.</summary>
    public static string ToPreset(DrawingShapeKind kind) =>
        DrawingMlPresetGeometryMap.GetPreset(kind);
}
