using System.Xml.Linq;

namespace Free.Shared.Opc;

public static class OpcMediaTypes
{
    public static readonly XNamespace ContentTypesNamespace =
        "http://schemas.openxmlformats.org/package/2006/content-types";

    public const string RelationshipsContentType =
        "application/vnd.openxmlformats-package.relationships+xml";

    public const string XmlContentType = "application/xml";

    private static readonly Dictionary<string, string> DefaultContentTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["png"] = "image/png",
            ["jpg"] = "image/jpeg",
            ["jpeg"] = "image/jpeg",
            ["gif"] = "image/gif",
            ["bmp"] = "image/bmp",
            ["tif"] = "image/tiff",
            ["tiff"] = "image/tiff",
            ["svg"] = "image/svg+xml",
            ["wmf"] = "image/x-wmf",
            ["emf"] = "image/x-emf",
            ["mp4"] = "video/mp4",
            ["mov"] = "video/quicktime",
            ["avi"] = "video/x-msvideo",
            ["wmv"] = "video/x-ms-wmv",
            ["mp3"] = "audio/mpeg",
            ["m4a"] = "audio/mp4",
            ["wav"] = "audio/wav",
            ["wma"] = "audio/x-ms-wma",
            ["ogg"] = "audio/ogg",
            ["aac"] = "audio/aac",
        };

    public static bool TryGetDefaultContentType(string extension, out string contentType) =>
        DefaultContentTypes.TryGetValue(extension.TrimStart('.'), out contentType!);

    public static string GetImageContentType(string path)
    {
        var extension = Path.GetExtension(path.AsSpan());
        if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            return "image/jpeg";

        if (extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
            return "image/bmp";

        if (extension.Equals(".gif", StringComparison.OrdinalIgnoreCase))
            return "image/gif";

        return "image/png";
    }

    public static string GetImageExtension(string contentType, bool includeDot = false)
    {
        var extension = contentType.AsSpan().Trim().Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ||
                        contentType.AsSpan().Trim().Equals("image/jpg", StringComparison.OrdinalIgnoreCase)
            ? "jpg"
            : contentType.AsSpan().Trim().Equals("image/bmp", StringComparison.OrdinalIgnoreCase)
                ? "bmp"
                : contentType.AsSpan().Trim().Equals("image/gif", StringComparison.OrdinalIgnoreCase)
                    ? "gif"
                    : "png";

        return includeDot ? $".{extension}" : extension;
    }

    public static string GetDrawingMediaExtension(string contentType) =>
        contentType.Trim().ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => "jpg",
            "image/gif" => "gif",
            "image/bmp" => "bmp",
            "image/tiff" => "tiff",
            "image/svg+xml" => "svg",
            "image/x-wmf" or "image/wmf" => "wmf",
            "image/x-emf" or "image/emf" => "emf",
            _ => "png"
        };

    public static string GetAudioVideoExtension(string contentType) =>
        contentType.Trim().ToLowerInvariant() switch
        {
            "video/mp4" => "mp4",
            "video/quicktime" => "mov",
            "video/x-msvideo" => "avi",
            "video/x-ms-wmv" => "wmv",
            "audio/mpeg" or "audio/mp3" => "mp3",
            "audio/mp4" => "m4a",
            "audio/wav" => "wav",
            "audio/x-ms-wma" => "wma",
            "audio/ogg" => "ogg",
            "audio/aac" => "aac",
            _ => "mp4"
        };
}
