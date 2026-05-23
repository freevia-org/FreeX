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
        var normalizedTarget = target.Replace('\\', '/');
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

        if (sourceDirectory.Equals("xl/worksheets", StringComparison.OrdinalIgnoreCase) &&
            targetPath.StartsWith("xl/media/", StringComparison.OrdinalIgnoreCase))
            return $"../media/{targetPath["xl/media/".Length..]}";

        if (sourceDirectory.Equals("xl/worksheets", StringComparison.OrdinalIgnoreCase) &&
            targetPath.StartsWith("xl/drawings/", StringComparison.OrdinalIgnoreCase))
            return $"../drawings/{targetPath["xl/drawings/".Length..]}";

        if (sourceDirectory.Equals("xl/worksheets", StringComparison.OrdinalIgnoreCase) &&
            targetPath.StartsWith("xl/tables/", StringComparison.OrdinalIgnoreCase))
            return $"../tables/{targetPath["xl/tables/".Length..]}";

        if (sourceDirectory.Equals("xl/worksheets", StringComparison.OrdinalIgnoreCase) &&
            targetPath.StartsWith("xl/pivotTables/", StringComparison.OrdinalIgnoreCase))
            return $"../pivotTables/{targetPath["xl/pivotTables/".Length..]}";

        if (sourceDirectory.Equals("xl/pivotTables", StringComparison.OrdinalIgnoreCase) &&
            targetPath.StartsWith("xl/pivotCache/", StringComparison.OrdinalIgnoreCase))
            return $"../pivotCache/{targetPath["xl/pivotCache/".Length..]}";

        if (sourceDirectory.Equals("xl/slicers", StringComparison.OrdinalIgnoreCase) &&
            targetPath.StartsWith("xl/slicerCaches/", StringComparison.OrdinalIgnoreCase))
            return $"../slicerCaches/{targetPath["xl/slicerCaches/".Length..]}";

        if (sourceDirectory.Equals("xl/timelines", StringComparison.OrdinalIgnoreCase) &&
            targetPath.StartsWith("xl/timelineCaches/", StringComparison.OrdinalIgnoreCase))
            return $"../timelineCaches/{targetPath["xl/timelineCaches/".Length..]}";

        if (sourceDirectory.Equals("xl/drawings", StringComparison.OrdinalIgnoreCase) &&
            targetPath.StartsWith("xl/charts/", StringComparison.OrdinalIgnoreCase))
            return $"../charts/{targetPath["xl/charts/".Length..]}";

        if (sourceDirectory.Equals("xl/drawings", StringComparison.OrdinalIgnoreCase) &&
            targetPath.StartsWith("xl/media/", StringComparison.OrdinalIgnoreCase))
            return $"../media/{targetPath["xl/media/".Length..]}";

        return targetPath.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)
            ? targetPath["xl/".Length..]
            : targetPath;
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
}
