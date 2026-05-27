namespace Freexcel.Core.IO;

public static class XlsxPackagePath
{
    public static string NormalizeWorkbookTarget(string target)
    {
        target = target.Replace('\\', '/').TrimStart('/');
        return target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)
            ? target
            : $"xl/{target}";
    }

    public static string GetRelationshipPartPath(string sourcePath)
    {
        var normalized = sourcePath.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        if (slash < 0)
            return $"_rels/{normalized}.rels";

        return $"{normalized[..slash]}/_rels/{normalized[(slash + 1)..]}.rels";
    }

    public static string ResolveRelationshipTarget(string sourcePath, string target)
    {
        var normalizedTarget = UnescapePathSegments(target.Replace('\\', '/'));
        if (normalizedTarget.StartsWith('/'))
            return normalizedTarget.TrimStart('/');
        if (normalizedTarget.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
            return normalizedTarget;

        var sourceDirectory = sourcePath.Replace('\\', '/');
        var slash = sourceDirectory.LastIndexOf('/');
        sourceDirectory = slash >= 0 ? sourceDirectory[..slash] : "";
        return NormalizeZipPath($"{sourceDirectory}/{normalizedTarget}");
    }

    public static string GetRelationshipTarget(string sourcePath, string targetPath)
    {
        var sourceDirectory = sourcePath.Replace('\\', '/');
        var slash = sourceDirectory.LastIndexOf('/');
        sourceDirectory = slash >= 0 ? sourceDirectory[..slash] : "";

        string target;
        if (sourceDirectory.Equals("xl/worksheets", StringComparison.OrdinalIgnoreCase) &&
            targetPath.StartsWith("xl/media/", StringComparison.OrdinalIgnoreCase))
            target = $"../media/{targetPath["xl/media/".Length..]}";
        else if (sourceDirectory.Equals("xl/worksheets", StringComparison.OrdinalIgnoreCase) &&
            targetPath.StartsWith("xl/drawings/", StringComparison.OrdinalIgnoreCase))
            target = $"../drawings/{targetPath["xl/drawings/".Length..]}";
        else if (sourceDirectory.Equals("xl/worksheets", StringComparison.OrdinalIgnoreCase) &&
            targetPath.StartsWith("xl/tables/", StringComparison.OrdinalIgnoreCase))
            target = $"../tables/{targetPath["xl/tables/".Length..]}";
        else if (sourceDirectory.Equals("xl/worksheets", StringComparison.OrdinalIgnoreCase) &&
            targetPath.StartsWith("xl/pivotTables/", StringComparison.OrdinalIgnoreCase))
            target = $"../pivotTables/{targetPath["xl/pivotTables/".Length..]}";
        else if (sourceDirectory.Equals("xl/pivotTables", StringComparison.OrdinalIgnoreCase) &&
            targetPath.StartsWith("xl/pivotCache/", StringComparison.OrdinalIgnoreCase))
            target = $"../pivotCache/{targetPath["xl/pivotCache/".Length..]}";
        else if (sourceDirectory.Equals("xl/slicers", StringComparison.OrdinalIgnoreCase) &&
            targetPath.StartsWith("xl/slicerCaches/", StringComparison.OrdinalIgnoreCase))
            target = $"../slicerCaches/{targetPath["xl/slicerCaches/".Length..]}";
        else if (sourceDirectory.Equals("xl/timelines", StringComparison.OrdinalIgnoreCase) &&
            targetPath.StartsWith("xl/timelineCaches/", StringComparison.OrdinalIgnoreCase))
            target = $"../timelineCaches/{targetPath["xl/timelineCaches/".Length..]}";
        else if (sourceDirectory.Equals("xl/drawings", StringComparison.OrdinalIgnoreCase) &&
            targetPath.StartsWith("xl/charts/", StringComparison.OrdinalIgnoreCase))
            target = $"../charts/{targetPath["xl/charts/".Length..]}";
        else if (sourceDirectory.Equals("xl/drawings", StringComparison.OrdinalIgnoreCase) &&
            targetPath.StartsWith("xl/media/", StringComparison.OrdinalIgnoreCase))
            target = $"../media/{targetPath["xl/media/".Length..]}";
        else
            target = targetPath.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)
            ? targetPath["xl/".Length..]
            : targetPath;

        return EscapePathSegments(target);
    }

    public static string NormalizeZipPath(string path)
    {
        var parts = new List<string>();
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
                continue;
            if (part == "..")
            {
                if (parts.Count > 0)
                    parts.RemoveAt(parts.Count - 1);
                continue;
            }

            parts.Add(part);
        }

        return string.Join('/', parts);
    }

    public static string GetImageContentType(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".bmp" => "image/bmp",
            ".gif" => "image/gif",
            _ => "image/png"
        };
    }

    public static string GetImageExtension(string contentType) =>
        contentType.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/bmp" => ".bmp",
            "image/gif" => ".gif",
            _ => ".png"
        };

    public static string GetWorksheetBackgroundMediaFileName(string? fileName, int backgroundIndex, string extension)
    {
        var candidate = Path.GetFileName(fileName ?? "");
        if (string.IsNullOrWhiteSpace(candidate) ||
            candidate.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return $"freexcelBackground{backgroundIndex}{extension}";
        }

        return Path.HasExtension(candidate)
            ? candidate
            : $"{candidate}{extension}";
    }

    private static string UnescapePathSegments(string path) =>
        string.Join('/', path.Split('/').Select(UnescapePathSegment));

    private static string EscapePathSegments(string path) =>
        string.Join('/', path.Split('/').Select(EscapePathSegment));

    private static string UnescapePathSegment(string segment)
    {
        try
        {
            return Uri.UnescapeDataString(segment);
        }
        catch (UriFormatException)
        {
            return segment;
        }
    }

    private static string EscapePathSegment(string segment) =>
        segment is "." or ".." ? segment : Uri.EscapeDataString(segment);
}
