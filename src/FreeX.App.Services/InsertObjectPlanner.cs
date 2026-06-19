using System.IO;

namespace FreeX.App.Services;

/// <summary>How the Insert ▸ Object dialog should place the chosen file on the sheet.</summary>
public enum InsertObjectRendering
{
    /// <summary>The file is an image; embed its bytes directly as a picture (true visual content).</summary>
    EmbedImageAsPicture,

    /// <summary>
    /// The file is not an image; place a generated icon/label placeholder picture standing in for the
    /// object. This is an honest placeholder — the original file's content is NOT embedded in the model,
    /// because the Core model has no first-class OLE-object part. The placeholder carries the file name
    /// (and, when linked, the source path) so the user can identify the object.
    /// </summary>
    IconPlaceholder
}

/// <summary>Why an Insert ▸ Object request could not be planned.</summary>
public enum InsertObjectValidationError
{
    None,
    MissingFilePath,
    FileNotFound
}

/// <summary>
/// A planned Insert ▸ Object placement. The UI shell turns this into the existing
/// <c>InsertPictureCommand</c>: for <see cref="InsertObjectRendering.EmbedImageAsPicture"/> it reads the
/// file bytes; for <see cref="InsertObjectRendering.IconPlaceholder"/> it renders a small icon bitmap
/// labelled with <see cref="DisplayName"/>. <see cref="DisplayName"/> / <see cref="LinkPath"/> are
/// suitable for the picture's title/alt-text so the placeholder is self-describing.
/// </summary>
public sealed record InsertObjectPlan(
    InsertObjectRendering Rendering,
    string FilePath,
    string DisplayName,
    string? ImageContentType,
    bool LinkToFile,
    string? LinkPath);

/// <summary>
/// Portable planner for Insert ▸ Object (create-from-file). It validates the chosen file and decides how
/// to render it, keeping all the decision logic out of the UI so macOS inherits it.
///
/// HONESTY NOTE: the FreeX Core model has no editable embedded-OLE-object part (OLE XML is only preserved
/// on XLSX round-trip, never created from the UI). True OLE embedding is therefore NOT implemented. This
/// planner delivers the realistic subset: an image file is embedded as a real picture, and any other file
/// becomes an icon/label placeholder picture (optionally "linked" — the link path is recorded on the
/// placeholder for identification, but FreeX does not auto-refresh from the source).
/// </summary>
public static class InsertObjectPlanner
{
    /// <summary>Image extensions that can be embedded directly as picture content (mirrors the picture-insert path).</summary>
    private static readonly IReadOnlyDictionary<string, string> ImageContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".gif"] = "image/gif",
            [".bmp"] = "image/bmp",
            [".webp"] = "image/webp",
            [".tif"] = "image/tiff",
            [".tiff"] = "image/tiff",
        };

    /// <summary>The image MIME content type for an extension, or <c>null</c> when the file is not a supported image.</summary>
    public static string? ImageContentTypeForPath(string path)
    {
        var extension = Path.GetExtension(path);
        return extension is { Length: > 0 } && ImageContentTypes.TryGetValue(extension, out var contentType)
            ? contentType
            : null;
    }

    /// <summary>True when the path has a supported image extension (and would embed as real picture content).</summary>
    public static bool IsEmbeddableImagePath(string path) => ImageContentTypeForPath(path) is not null;

    /// <summary>
    /// Validates a create-from-file request and produces an <see cref="InsertObjectPlan"/>.
    /// <paramref name="fileExists"/> is supplied by the caller so the planner stays free of filesystem
    /// I/O (the UI shell already has the chosen file handle); pass <c>true</c> when the file is
    /// known to exist (e.g. it came from a storage picker).
    /// </summary>
    public static bool TryPlan(
        string? filePath,
        bool fileExists,
        bool linkToFile,
        out InsertObjectPlan plan,
        out InsertObjectValidationError error)
    {
        plan = null!;

        if (string.IsNullOrWhiteSpace(filePath))
        {
            error = InsertObjectValidationError.MissingFilePath;
            return false;
        }

        if (!fileExists)
        {
            error = InsertObjectValidationError.FileNotFound;
            return false;
        }

        var trimmed = filePath.Trim();
        var displayName = DisplayNameForPath(trimmed);
        var imageContentType = ImageContentTypeForPath(trimmed);
        var rendering = imageContentType is not null
            ? InsertObjectRendering.EmbedImageAsPicture
            : InsertObjectRendering.IconPlaceholder;

        plan = new InsertObjectPlan(
            rendering,
            trimmed,
            displayName,
            imageContentType,
            linkToFile,
            linkToFile ? trimmed : null);
        error = InsertObjectValidationError.None;
        return true;
    }

    /// <summary>The file name (without directory) shown on the placeholder; falls back to the raw path.</summary>
    public static string DisplayNameForPath(string path)
    {
        try
        {
            var name = Path.GetFileName(path);
            return string.IsNullOrWhiteSpace(name) ? path : name;
        }
        catch (ArgumentException)
        {
            return path;
        }
    }
}
