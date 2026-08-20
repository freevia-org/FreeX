using System.IO.Compression;
using System.Xml.Linq;

namespace Free.Shared.Opc;

public enum OpcMediaExtensionProfile
{
    EmbeddedPlayback,
    TransitionSound,
    PackageAudioVideo,
    TemporaryPlaybackMaterialization,
    PackageTransitionSound,
    PresentationPackageMediaPart,
    PresentationZoomCoverImage,
    PresentationSmartArtImage,
    PresentationCaptionTrack,
}

public enum OpcMediaContentTypeProfile
{
    PresentationPictureInsertion,
    PresentationAudioInsertion,
    PresentationVideoInsertion,
    ExternalXamlPicture,
    PresentationListGalleryPicture,
    PresentationCaptionTrack,
    OfficeEmbeddedObjectInsertion,
    OfficeEmbeddedObjectPackageRead,
}

public static class OpcMediaTypes
{
    [Flags]
    private enum MediaExtensionProfileMask
    {
        None = 0,
        EmbeddedPlayback = 1,
        TransitionSound = 2,
        PackageAudioVideo = 4,
    }

    private sealed record MediaExtensionRule(
        string Extension,
        MediaExtensionProfileMask Profiles);

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
            ["webp"] = "image/webp",
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

    private static readonly Dictionary<string, MediaExtensionRule> MediaExtensionsByContentType =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["video/mp4"] = Rule("mp4", embedded: true, package: true),
            ["video/mpeg"] = Rule("mpg", embedded: true),
            ["video/avi"] = Rule("avi", embedded: true),
            ["video/x-msvideo"] = Rule("avi", embedded: true, package: true),
            ["video/quicktime"] = Rule("mov", embedded: true, package: true),
            ["video/x-ms-wmv"] = Rule("wmv", embedded: true, package: true),
            ["video/x-ms-asf"] = Rule("asf", embedded: true),
            ["video/webm"] = Rule("webm", embedded: true),
            ["audio/mpeg"] = Rule("mp3", embedded: true, transition: true, package: true),
            ["audio/mp3"] = Rule("mp3", embedded: true, transition: true, package: true),
            ["audio/wav"] = Rule("wav", embedded: true, transition: true, package: true),
            ["audio/x-wav"] = Rule("wav", embedded: true, transition: true),
            ["audio/ogg"] = Rule("ogg", embedded: true, transition: true, package: true),
            ["audio/x-ms-wma"] = Rule("wma", embedded: true, transition: true, package: true),
            ["audio/aac"] = Rule("aac", embedded: true, transition: true, package: true),
            ["audio/flac"] = Rule("flac", embedded: true, transition: true),
            ["audio/x-flac"] = Rule("flac", transition: true),
            ["audio/mp4"] = Rule("m4a", transition: true, package: true),
            ["audio/m4a"] = Rule("m4a", transition: true),
            ["audio/x-m4a"] = Rule("m4a", transition: true),
        };

    public static bool TryGetDefaultContentType(string extension, out string contentType) =>
        DefaultContentTypes.TryGetValue(extension.TrimStart('.'), out contentType!);

    public static string GetMediaFileExtension(
        string? contentType,
        OpcMediaExtensionProfile profile,
        bool includeDot = false,
        string? fallbackFileNameOrExtension = null)
    {
        var specializedExtension = profile switch
        {
            OpcMediaExtensionProfile.TemporaryPlaybackMaterialization =>
                GetTemporaryPlaybackExtension(contentType),
            OpcMediaExtensionProfile.PackageTransitionSound =>
                GetPackageTransitionSoundExtension(contentType),
            OpcMediaExtensionProfile.PresentationPackageMediaPart =>
                GetPresentationPackageMediaExtension(contentType),
            OpcMediaExtensionProfile.PresentationZoomCoverImage =>
                GetPresentationZoomCoverExtension(contentType),
            OpcMediaExtensionProfile.PresentationSmartArtImage =>
                GetPresentationSmartArtExtension(contentType),
            OpcMediaExtensionProfile.PresentationCaptionTrack =>
                GetPresentationCaptionTrackExtension(contentType, fallbackFileNameOrExtension),
            _ => null,
        };
        if (specializedExtension is not null)
            return includeDot ? $".{specializedExtension}" : specializedExtension;

        var normalized = profile == OpcMediaExtensionProfile.PackageAudioVideo
            ? contentType?.Trim()
            : contentType;
        var profileMask = ToMask(profile);
        var extension = normalized is not null &&
                        MediaExtensionsByContentType.TryGetValue(normalized, out var rule) &&
                        (rule.Profiles & profileMask) != 0
            ? rule.Extension
            : profile switch
            {
                OpcMediaExtensionProfile.EmbeddedPlayback => "bin",
                OpcMediaExtensionProfile.TransitionSound => "mp3",
                _ => "mp4",
            };
        return includeDot ? $".{extension}" : extension;
    }

    public static string GetContentTypeForFileNameOrExtension(
        string? fileNameOrExtension,
        OpcMediaContentTypeProfile profile)
    {
        var extension = profile == OpcMediaContentTypeProfile.PresentationCaptionTrack &&
                        fileNameOrExtension?.IndexOfAny(['?', '#']) >= 0
            ? GetSourceExtension(fileNameOrExtension)
            : NormalizeFileNameOrExtension(fileNameOrExtension);
        return profile switch
        {
            OpcMediaContentTypeProfile.PresentationPictureInsertion => extension switch
            {
                "jpg" or "jpeg" => "image/jpeg",
                "gif" => "image/gif",
                "bmp" => "image/bmp",
                "svg" => "image/svg+xml",
                "webp" => "image/webp",
                _ => "image/png",
            },
            OpcMediaContentTypeProfile.PresentationAudioInsertion => extension switch
            {
                "mp3" => "audio/mpeg",
                "m4a" => "audio/mp4",
                "wav" => "audio/wav",
                "wma" => "audio/x-ms-wma",
                _ => "audio/mpeg",
            },
            OpcMediaContentTypeProfile.PresentationVideoInsertion => extension switch
            {
                "mp4" => "video/mp4",
                "mov" => "video/quicktime",
                "avi" => "video/x-msvideo",
                "wmv" => "video/x-ms-wmv",
                "m4v" => "video/x-m4v",
                _ => "video/mp4",
            },
            OpcMediaContentTypeProfile.ExternalXamlPicture => extension switch
            {
                "jpg" or "jpeg" => "image/jpeg",
                "gif" => "image/gif",
                "bmp" => "image/bmp",
                "tif" or "tiff" => "image/tiff",
                _ => "image/png",
            },
            OpcMediaContentTypeProfile.PresentationListGalleryPicture => extension switch
            {
                "jpg" or "jpeg" => "image/jpeg",
                "gif" => "image/gif",
                "bmp" => "image/bmp",
                "svg" => "image/svg+xml",
                "wmf" => "image/x-wmf",
                "emf" => "image/x-emf",
                _ => "image/png",
            },
            OpcMediaContentTypeProfile.PresentationCaptionTrack => extension switch
            {
                "vtt" => "text/vtt",
                "ttml" or "dfxp" => "application/ttml+xml",
                "srt" => "application/x-subrip",
                _ => string.Empty,
            },
            OpcMediaContentTypeProfile.OfficeEmbeddedObjectInsertion => extension switch
            {
                "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "xlsm" => "application/vnd.ms-excel.sheet.macroEnabled.12",
                "xls" => "application/vnd.ms-excel",
                "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                "doc" => "application/msword",
                "pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                "ppt" => "application/vnd.ms-powerpoint",
                _ => "application/octet-stream",
            },
            OpcMediaContentTypeProfile.OfficeEmbeddedObjectPackageRead => extension switch
            {
                "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "xlsm" => "application/vnd.ms-excel.sheet.macroEnabled.12",
                "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                "pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                "bin" => "application/vnd.ms-office.activeX+xml",
                _ => "application/octet-stream",
            },
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null),
        };
    }

    public static string GetCaptionTrackExtension(string? contentType, string? source) =>
        GetMediaFileExtension(
            contentType,
            OpcMediaExtensionProfile.PresentationCaptionTrack,
            fallbackFileNameOrExtension: source);

    public static string GetSourceExtension(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return string.Empty;

        var end = source.AsSpan();
        var queryIndex = source.IndexOfAny(['?', '#']);
        if (queryIndex >= 0)
            end = source.AsSpan(0, queryIndex);

        var slashIndex = end.LastIndexOf('/');
        var fileName = slashIndex >= 0 ? end[(slashIndex + 1)..] : end;
        var dotIndex = fileName.LastIndexOf('.');
        return dotIndex >= 0 && dotIndex < fileName.Length - 1
            ? fileName[(dotIndex + 1)..].ToString().ToLowerInvariant()
            : string.Empty;
    }

    public static Dictionary<string, string> ReadDefaultContentTypes(ZipArchive archive)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var contentTypesXml = OpcXml.LoadXmlOrNull(archive, ContentTypesPath);
        foreach (var element in contentTypesXml?.Root?.Elements(ContentTypesNamespace + "Default") ?? [])
        {
            var extension = element.Attribute("Extension")?.Value;
            var contentType = element.Attribute("ContentType")?.Value;
            if (!string.IsNullOrEmpty(extension) && !string.IsNullOrEmpty(contentType))
                map[extension] = contentType;
        }

        return map;
    }

    public static Dictionary<string, string> ReadOverrideContentTypes(ZipArchive archive)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var contentTypesXml = OpcXml.LoadXmlOrNull(archive, ContentTypesPath);
        foreach (var element in contentTypesXml?.Root?.Elements(ContentTypesNamespace + "Override") ?? [])
        {
            var partName = element.Attribute("PartName")?.Value;
            var contentType = element.Attribute("ContentType")?.Value;
            if (!string.IsNullOrEmpty(partName) && !string.IsNullOrEmpty(contentType))
                map[partName] = contentType;
        }

        return map;
    }

    public static void MergePreservedContentTypes(
        XDocument targetContentTypes,
        XDocument sourceContentTypes,
        Func<string, bool>? skipOverridePartName = null)
    {
        if (targetContentTypes.Root is null || sourceContentTypes.Root is null)
            return;

        var targetNamespace = targetContentTypes.Root.Name.Namespace;
        var sourceNamespace = sourceContentTypes.Root.Name.Namespace;
        var existingDefaults = new HashSet<string>(
            targetContentTypes.Root.Elements(targetNamespace + "Default")
                .Select(element => element.Attribute("Extension")?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!),
            StringComparer.OrdinalIgnoreCase);
        var existingOverrides = new HashSet<string>(
            targetContentTypes.Root.Elements(targetNamespace + "Override")
                .Select(element => element.Attribute("PartName")?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!),
            StringComparer.OrdinalIgnoreCase);

        foreach (var sourceDefault in sourceContentTypes.Root.Elements(sourceNamespace + "Default"))
        {
            var extension = sourceDefault.Attribute("Extension")?.Value;
            var contentType = sourceDefault.Attribute("ContentType")?.Value;
            if (string.IsNullOrWhiteSpace(extension) ||
                string.IsNullOrWhiteSpace(contentType) ||
                !existingDefaults.Add(extension))
            {
                continue;
            }

            targetContentTypes.Root.Add(new XElement(
                targetNamespace + "Default",
                new XAttribute("Extension", extension),
                new XAttribute("ContentType", contentType)));
        }

        foreach (var sourceOverride in sourceContentTypes.Root.Elements(sourceNamespace + "Override"))
        {
            var partName = sourceOverride.Attribute("PartName")?.Value;
            var contentType = sourceOverride.Attribute("ContentType")?.Value;
            if (string.IsNullOrWhiteSpace(partName) ||
                string.IsNullOrWhiteSpace(contentType) ||
                skipOverridePartName?.Invoke(partName) == true ||
                !existingOverrides.Add(partName))
            {
                continue;
            }

            targetContentTypes.Root.Add(new XElement(
                targetNamespace + "Override",
                new XAttribute("PartName", partName),
                new XAttribute("ContentType", contentType)));
        }
    }

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

        if (extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase))
            return "image/tiff";

        if (extension.Equals(".webp", StringComparison.OrdinalIgnoreCase))
            return "image/webp";

        if (extension.Equals(".wmf", StringComparison.OrdinalIgnoreCase))
            return "image/x-wmf";

        if (extension.Equals(".emf", StringComparison.OrdinalIgnoreCase))
            return "image/x-emf";

        return "image/png";
    }

    public static string GetImageExtension(string contentType, bool includeDot = false)
    {
        var trimmed = contentType.AsSpan().Trim();
        var extension = trimmed.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.Equals("image/jpg", StringComparison.OrdinalIgnoreCase)
            ? "jpg"
            : trimmed.Equals("image/bmp", StringComparison.OrdinalIgnoreCase)
                ? "bmp"
                : trimmed.Equals("image/gif", StringComparison.OrdinalIgnoreCase)
                    ? "gif"
                    : trimmed.Equals("image/tiff", StringComparison.OrdinalIgnoreCase) ||
                      trimmed.Equals("image/tif", StringComparison.OrdinalIgnoreCase)
                        ? "tiff"
                        : trimmed.Equals("image/webp", StringComparison.OrdinalIgnoreCase)
                            ? "webp"
                            : trimmed.Equals("image/x-wmf", StringComparison.OrdinalIgnoreCase) ||
                              trimmed.Equals("image/wmf", StringComparison.OrdinalIgnoreCase)
                                ? "wmf"
                                : trimmed.Equals("image/x-emf", StringComparison.OrdinalIgnoreCase) ||
                                  trimmed.Equals("image/emf", StringComparison.OrdinalIgnoreCase)
                                    ? "emf"
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
            "image/webp" => "webp",
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
        GetMediaFileExtension(contentType, OpcMediaExtensionProfile.PackageAudioVideo);

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

    private static MediaExtensionRule Rule(
        string extension,
        bool embedded = false,
        bool transition = false,
        bool package = false)
    {
        var profiles = (embedded ? MediaExtensionProfileMask.EmbeddedPlayback : MediaExtensionProfileMask.None) |
                       (transition ? MediaExtensionProfileMask.TransitionSound : MediaExtensionProfileMask.None) |
                       (package ? MediaExtensionProfileMask.PackageAudioVideo : MediaExtensionProfileMask.None);
        return new MediaExtensionRule(extension, profiles);
    }

    private static MediaExtensionProfileMask ToMask(OpcMediaExtensionProfile profile) =>
        profile switch
        {
            OpcMediaExtensionProfile.EmbeddedPlayback => MediaExtensionProfileMask.EmbeddedPlayback,
            OpcMediaExtensionProfile.TransitionSound => MediaExtensionProfileMask.TransitionSound,
            OpcMediaExtensionProfile.PackageAudioVideo => MediaExtensionProfileMask.PackageAudioVideo,
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null),
        };

    private static string GetTemporaryPlaybackExtension(string? contentType) =>
        contentType?.Trim().ToLowerInvariant() switch
        {
            "video/mp4" => "mp4",
            "video/mpeg" => "mpg",
            "video/avi" or "video/x-msvideo" => "avi",
            "video/quicktime" => "mov",
            "video/webm" => "webm",
            "audio/mpeg" or "audio/mp3" => "mp3",
            "audio/wav" or "audio/x-wav" => "wav",
            "audio/ogg" => "ogg",
            "audio/aac" => "aac",
            "audio/flac" => "flac",
            "audio/x-ms-wma" => "wma",
            _ => "bin",
        };

    private static string GetPackageTransitionSoundExtension(string? contentType) =>
        contentType switch
        {
            "audio/mpeg" or "audio/mp3" => "mp3",
            "audio/wav" => "wav",
            "audio/ogg" => "ogg",
            "audio/aac" => "aac",
            "audio/x-ms-wma" => "wma",
            _ => "mp3",
        };

    private static string GetPresentationPackageMediaExtension(string? contentType) =>
        contentType switch
        {
            "video/mp4" => "mp4",
            "video/quicktime" => "mov",
            "video/x-msvideo" => "avi",
            "video/x-ms-wmv" => "wmv",
            "audio/mpeg" => "mp3",
            "audio/mp4" => "m4a",
            "audio/wav" => "wav",
            "audio/x-ms-wma" => "wma",
            _ => "mp4",
        };

    private static string GetPresentationZoomCoverExtension(string? contentType) =>
        contentType switch
        {
            "image/jpeg" => "jpg",
            "image/gif" => "gif",
            "image/bmp" => "bmp",
            "image/svg+xml" => "svg",
            "image/webp" => "webp",
            _ => "png",
        };

    // r157-remediation: webp belongs here for the same reason it belongs in the zoom-cover mapper
    // above. Teaching the infer side about webp without teaching this one produced something worse
    // than the original bug: PptxPackageWriter writes a SmartArt picture's [Content_Types].xml
    // Override from the stored ContentType while naming the part from this extension, so a webp
    // image inserted into a SmartArt node became a part called "picture1.png" declared as
    // image/webp -- an internally inconsistent, spec-violating package, where before it was merely
    // mislabelled as png and self-consistent.
    private static string GetPresentationSmartArtExtension(string? contentType) =>
        contentType?.ToLowerInvariant() switch
        {
            "image/jpeg" => "jpg",
            "image/gif" => "gif",
            "image/bmp" => "bmp",
            "image/svg+xml" => "svg",
            "image/webp" => "webp",
            _ => "png",
        };

    private static string GetPresentationCaptionTrackExtension(
        string? contentType,
        string? fallbackFileNameOrExtension)
    {
        var contentTypeExtension = contentType?.Trim().ToLowerInvariant() switch
        {
            "text/vtt" => "vtt",
            "application/ttml+xml" or "application/ttaf+xml" => "ttml",
            "application/x-subrip" or "text/srt" => "srt",
            _ => string.Empty,
        };
        if (contentTypeExtension.Length > 0)
            return contentTypeExtension;

        var sourceExtension = GetSourceExtension(fallbackFileNameOrExtension);
        return sourceExtension is "vtt" or "ttml" or "dfxp" or "srt"
            ? sourceExtension
            : "vtt";
    }

    private static string NormalizeFileNameOrExtension(string? fileNameOrExtension)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrExtension))
            return string.Empty;

        var extension = Path.GetExtension(fileNameOrExtension);
        if (string.IsNullOrWhiteSpace(extension))
            extension = fileNameOrExtension;
        return extension.TrimStart('.').ToLowerInvariant();
    }
}
