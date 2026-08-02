using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>Shared command metadata and defaults for the PowerPoint Zoom format dialog.</summary>
public static class ZoomObjectPropertiesPlanner
{
    public const string CommandId = "freep.zoom.format";
    public const string DialogTitle = "Zoom Format";

    public static ZoomObjectProperties Effective(PreservedObjectInfo? info) =>
        info?.ZoomProperties ?? new ZoomObjectProperties(true, "preview", null, true);

    public static bool IsSupportedImageType(string? imageType) =>
        string.Equals(imageType, "preview", StringComparison.OrdinalIgnoreCase)
        || string.Equals(imageType, "cover", StringComparison.OrdinalIgnoreCase);
}
