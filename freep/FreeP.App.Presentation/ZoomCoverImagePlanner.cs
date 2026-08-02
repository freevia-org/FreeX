namespace FreeP.App.Compositor;

/// <summary>Shared command metadata for importing a user-authored Zoom cover image.</summary>
public static class ZoomCoverImagePlanner
{
    public const string CommandId = "freep.zoom.cover-image";
    public const string DialogTitle = "Set Zoom Cover Image";
    public const string ResetCommandId = "freep.zoom.reset-cover-image";
    public const string ResetDialogTitle = "Restore Zoom Preview";

    public static bool IsSupportedContentType(string? contentType) =>
        contentType is not null
        && contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}
