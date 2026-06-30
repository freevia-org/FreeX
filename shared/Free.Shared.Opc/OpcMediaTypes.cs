using System.IO.Compression;
using System.Xml.Linq;

namespace Free.Shared.Opc;

public static class OpcMediaTypes
{
    public static readonly XNamespace ContentTypesNamespace =
        "http://schemas.openxmlformats.org/package/2006/content-types";

    public const string ContentTypesPath = "[Content_Types].xml";

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

    public static bool EnsureDefaultContentType(
        ZipArchive archive,
        string extension,
        string contentType)
    {
        var contentTypesXml = OpcXml.LoadXmlOrNull(archive, ContentTypesPath);
        var root = contentTypesXml?.Root;
        if (root is null)
            return false;

        var normalizedExtension = extension.TrimStart('.');
        var hasDefault = root
            .Elements(ContentTypesNamespace + "Default")
            .Any(element => string.Equals(
                element.Attribute("Extension")?.Value,
                normalizedExtension,
                StringComparison.OrdinalIgnoreCase));
        if (hasDefault)
            return false;

        root.Add(new XElement(
            ContentTypesNamespace + "Default",
            new XAttribute("Extension", normalizedExtension),
            new XAttribute("ContentType", contentType)));
        OpcXml.ReplaceXmlEntry(archive, ContentTypesPath, contentTypesXml!);
        return true;
    }

    public static bool EnsureOverrideContentType(
        ZipArchive archive,
        string partName,
        string contentType)
    {
        var contentTypesXml = OpcXml.LoadXmlOrNull(archive, ContentTypesPath);
        var root = contentTypesXml?.Root;
        if (root is null)
            return false;

        var normalizedPartName = NormalizePartName(partName);
        var matches = FindOverrideContentTypes(root, normalizedPartName).ToList();
        if (matches.Count == 1 &&
            string.Equals(matches[0].Attribute("ContentType")?.Value, contentType, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var match in matches)
            match.Remove();

        root.Add(new XElement(
            ContentTypesNamespace + "Override",
            new XAttribute("PartName", normalizedPartName),
            new XAttribute("ContentType", contentType)));
        OpcXml.ReplaceXmlEntry(archive, ContentTypesPath, contentTypesXml!);
        return true;
    }

    public static bool RemoveOverrideContentTypes(ZipArchive archive, IEnumerable<string> partNames)
    {
        var normalizedPartNames = partNames
            .Select(NormalizePartName)
            .Where(partName => !string.IsNullOrWhiteSpace(partName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (normalizedPartNames.Count == 0)
            return false;

        var contentTypesXml = OpcXml.LoadXmlOrNull(archive, ContentTypesPath);
        var root = contentTypesXml?.Root;
        if (root is null)
            return false;

        var overrides = root
            .Elements(ContentTypesNamespace + "Override")
            .Where(element => normalizedPartNames.Contains(NormalizePartName(element.Attribute("PartName")?.Value)))
            .ToList();
        if (overrides.Count == 0)
            return false;

        foreach (var element in overrides)
            element.Remove();
        OpcXml.ReplaceXmlEntry(archive, ContentTypesPath, contentTypesXml!);
        return true;
    }

    public static bool PruneMissingOverrideContentTypes(ZipArchive archive)
    {
        var contentTypesXml = OpcXml.LoadXmlOrNull(archive, ContentTypesPath);
        var root = contentTypesXml?.Root;
        if (root is null)
            return false;

        var changed = false;
        foreach (var overrideElement in root.Elements(ContentTypesNamespace + "Override").ToList())
        {
            var zipPath = NormalizePartName(overrideElement.Attribute("PartName")?.Value).TrimStart('/');
            if (!string.IsNullOrWhiteSpace(zipPath) && archive.GetEntry(zipPath) is null)
            {
                overrideElement.Remove();
                changed = true;
            }
        }

        if (!changed)
            return false;

        OpcXml.ReplaceXmlEntry(archive, ContentTypesPath, contentTypesXml!);
        return true;
    }

    public static string NormalizePartName(string? partName)
    {
        if (string.IsNullOrWhiteSpace(partName))
            return string.Empty;

        return "/" + OpcPathHelper.NormalizeZipEntryPath(partName.Trim());
    }

    public static IEnumerable<XElement> FindOverrideContentTypes(XElement contentTypesRoot, string partName)
    {
        var normalizedPartName = NormalizePartName(partName);
        return contentTypesRoot
            .Elements(ContentTypesNamespace + "Override")
            .Where(overrideElement => string.Equals(
                NormalizePartName(overrideElement.Attribute("PartName")?.Value),
                normalizedPartName,
                StringComparison.OrdinalIgnoreCase));
    }

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

    public static string GetDrawingMediaContentType(string path)
    {
        var extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        if (extension is "jpg" or "jpeg" or "gif" or "bmp" or "tif" or "tiff" or "svg" or "wmf" or "emf" &&
            TryGetDefaultContentType(extension, out var contentType))
        {
            return contentType;
        }

        return "image/png";
    }

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

    public static string GetAudioVideoContentType(string path)
    {
        var extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        if (extension == "m4v")
            return "video/mp4";

        if (extension is "mp4" or "mov" or "avi" or "wmv" or "mp3" or "m4a" or "wav" or "wma" &&
            TryGetDefaultContentType(extension, out var contentType))
        {
            return contentType;
        }

        return "video/mp4";
    }
}
