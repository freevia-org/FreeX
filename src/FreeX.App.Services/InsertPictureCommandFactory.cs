using System.IO;
using Free.Shared.IO;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>
/// UI-free factory that turns chosen image bytes into the Core <see cref="InsertPictureCommand"/> the shell
/// runs to place a picture on the sheet. Shells still own image picking and native-size decoding; this
/// factory owns shared content-type mapping and safe size fallback.
/// </summary>
public static class InsertPictureCommandFactory
{
    public const double DefaultWidth = 240d;
    public const double DefaultHeight = 140d;

    /// <summary>The image MIME content type for a file path's extension, or <c>null</c> when unsupported.</summary>
    public static string? ContentTypeForPath(string path)
    {
        var extension = FilePathPolicy.GetExtensionOrEmpty(path);
        return extension.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".tif" or ".tiff" => "image/tiff",
            _ => null,
        };
    }

    /// <summary>True when the path has a supported image extension.</summary>
    public static bool IsSupportedImagePath(string path) => ContentTypeForPath(path) is not null;

    /// <summary>
    /// Builds the <see cref="InsertPictureCommand"/> anchoring the image at <paramref name="anchor"/>.
    /// Non-positive widths/heights (e.g. when native decoding failed) fall back to the defaults.
    /// </summary>
    public static InsertPictureCommand Build(
        SheetId sheetId,
        CellAddress anchor,
        byte[] imageBytes,
        string contentType,
        double width,
        double height) =>
        new(sheetId,
            anchor,
            imageBytes,
            contentType,
            ClampPositive(width, DefaultWidth),
            ClampPositive(height, DefaultHeight));

    private static double ClampPositive(double value, double fallback) =>
        double.IsFinite(value) && value > 0 ? value : fallback;
}
