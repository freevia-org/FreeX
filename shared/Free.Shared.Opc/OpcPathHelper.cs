using System.IO.Compression;

namespace Free.Shared.Opc;

public static class OpcPathHelper
{
    public static string ToZipEntryPath(string path) =>
        path.Replace('\\', '/').TrimStart('/');

    public static string NormalizeZipEntryPath(string path) =>
        CollapseDotSegments(ToZipEntryPath(path));

    public static string NormalizeEntryPath(ZipArchiveEntry entry) =>
        NormalizeZipEntryPath(entry.FullName);

    public static string GetDirectoryName(string path)
    {
        var normalized = ToZipEntryPath(path);
        var lastSlash = normalized.LastIndexOf('/');
        return lastSlash < 0 ? string.Empty : normalized[..lastSlash];
    }

    public static string GetFileName(string path)
    {
        var normalized = ToZipEntryPath(path);
        var lastSlash = normalized.LastIndexOf('/');
        return lastSlash < 0 ? normalized : normalized[(lastSlash + 1)..];
    }

    public static string GetRelationshipPartPath(string partPath)
    {
        var normalized = ToZipEntryPath(partPath);
        var directory = GetDirectoryName(normalized);
        var fileName = GetFileName(normalized);
        return string.IsNullOrEmpty(directory)
            ? $"_rels/{fileName}.rels"
            : $"{directory}/_rels/{fileName}.rels";
    }

    public static string ResolveRelativeZipPath(string baseDirectory, string target)
    {
        var normalizedTarget = target.Replace('\\', '/');
        if (normalizedTarget.StartsWith('/'))
            return NormalizeZipEntryPath(normalizedTarget);

        return NormalizeZipEntryPath(string.IsNullOrEmpty(baseDirectory)
            ? normalizedTarget
            : $"{ToZipEntryPath(baseDirectory)}/{normalizedTarget}");
    }

    public static string? ResolveAbsolutePartName(string baseFolder, string target)
    {
        var normalizedTarget = target.Replace('\\', '/');
        if (normalizedTarget.StartsWith('/'))
            return normalizedTarget;

        var segments = new List<string>(
            baseFolder.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries));

        foreach (var segment in normalizedTarget.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
                continue;

            if (segment == "..")
            {
                if (segments.Count == 0)
                    return null;

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return "/" + string.Join('/', segments);
    }

    public static string GetPartDirectoryName(string partName)
    {
        var normalized = partName.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        return slash <= 0 ? "/" : normalized[..slash];
    }

    public static string GetRelationshipPartName(string partName)
    {
        var normalized = partName.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        var folder = slash <= 0 ? string.Empty : normalized[..slash];
        var file = slash < 0 ? normalized : normalized[(slash + 1)..];
        return $"{folder}/_rels/{file}.rels";
    }

    private static string CollapseDotSegments(string path)
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
}
