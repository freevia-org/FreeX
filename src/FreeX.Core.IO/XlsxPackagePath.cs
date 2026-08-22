using System.IO.Compression;

namespace FreeX.Core.IO;

public static class XlsxPackagePath
{
    public static string NormalizeWorkbookTarget(string target)
    {
        target = OpcPathHelper.ToZipEntryPath(target);
        return target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)
            ? target
            : $"xl/{target}";
    }

    public static string GetRelationshipPartPath(string sourcePath)
        => OpcPathHelper.GetRelationshipPartPath(sourcePath);

    public static string ResolveRelationshipTarget(string sourcePath, string target)
    {
        var normalizedTarget = OpcPathHelper.UnescapeRelationshipPathSegments(target.Replace('\\', '/'));
        if (normalizedTarget.StartsWith('/'))
            return normalizedTarget.TrimStart('/');
        if (normalizedTarget.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
            return normalizedTarget;

        return OpcPathHelper.ResolveRelativeZipPath(
            OpcPathHelper.GetDirectoryName(sourcePath),
            normalizedTarget);
    }

    public static string GetRelationshipTarget(string sourcePath, string targetPath)
    {
        var sourceDirectory = OpcPathHelper.GetDirectoryName(sourcePath);
        var normalizedTargetPath = OpcPathHelper.ToZipEntryPath(targetPath);

        string target;
        if (UsesRelativeRelationshipTarget(sourceDirectory, normalizedTargetPath))
            target = OpcPathHelper.GetRelativeZipPath(sourceDirectory, normalizedTargetPath);
        else
            target = normalizedTargetPath.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)
            ? normalizedTargetPath["xl/".Length..]
            : normalizedTargetPath;

        return OpcPathHelper.EscapeRelationshipPathSegments(target);
    }

    public static string NormalizeZipPath(string path)
        => OpcPathHelper.NormalizeZipEntryPath(path);

    public static bool IsWorksheetXmlEntry(ZipArchiveEntry entry) =>
        IsXmlEntryInDirectory(entry, "xl/worksheets/");

    public static string NormalizeEntryPath(ZipArchiveEntry entry) =>
        OpcPathHelper.NormalizeEntryPath(entry);

    public static string NormalizePackagePath(string path) =>
        OpcPathHelper.NormalizeZipEntryPath(path);

    public static bool IsXmlEntryInDirectory(ZipArchiveEntry entry, string directory)
    {
        var path = NormalizeEntryPath(entry);
        return path.StartsWith(directory, StringComparison.OrdinalIgnoreCase) &&
               path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
               !path.Contains("/_rels/", StringComparison.OrdinalIgnoreCase);
    }

    public static string GetImageContentType(string path)
        => OpcMediaTypes.GetImageContentType(path);

    public static string GetImageExtension(string contentType)
        => OpcMediaTypes.GetImageExtension(contentType, includeDot: true);

    private static bool UsesRelativeRelationshipTarget(string sourceDirectory, string targetPath) =>
        sourceDirectory switch
        {
            var value when value.Equals("xl/worksheets", StringComparison.OrdinalIgnoreCase) =>
                IsInXlFolder(targetPath, "media") ||
                IsInXlFolder(targetPath, "drawings") ||
                IsInXlFolder(targetPath, "ctrlProps") ||
                IsInXlFolder(targetPath, "tables") ||
                IsInXlFolder(targetPath, "threadedComments") ||
                IsInXlFolder(targetPath, "pivotTables") ||
                IsInXlFolder(targetPath, "customProperty") ||
                IsInXlFolder(targetPath, "slicers") ||
                IsInXlFolder(targetPath, "timelines") ||
                targetPath.Equals("xl/webPublishItems.xml", StringComparison.OrdinalIgnoreCase),
            var value when value.Equals("xl/pivotTables", StringComparison.OrdinalIgnoreCase) =>
                IsInXlFolder(targetPath, "pivotCache"),
            var value when value.Equals("xl/pivotCache", StringComparison.OrdinalIgnoreCase) =>
                IsInXlFolder(targetPath, "pivotCache"),
            var value when value.Equals("xl/slicers", StringComparison.OrdinalIgnoreCase) =>
                IsInXlFolder(targetPath, "slicerCaches"),
            var value when value.Equals("xl/timelines", StringComparison.OrdinalIgnoreCase) =>
                IsInXlFolder(targetPath, "timelineCaches"),
            var value when value.Equals("xl/drawings", StringComparison.OrdinalIgnoreCase) =>
                IsInXlFolder(targetPath, "charts") ||
                IsInXlFolder(targetPath, "media"),
            _ => false
        };

    private static bool IsInXlFolder(string targetPath, string folder) =>
        targetPath.StartsWith($"xl/{folder}/", StringComparison.OrdinalIgnoreCase);

    public static string GetWorksheetBackgroundMediaFileName(string? fileName, int backgroundIndex, string extension)
    {
        var candidate = GetPackageFileName(fileName);
        if (!IsSafePackageMediaFileName(candidate))
        {
            return $"freexBackground{backgroundIndex}{extension}";
        }

        return HasPackageFileExtension(candidate)
            ? candidate
            : $"{candidate}{extension}";
    }

    private static string GetPackageFileName(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return "";

        var slash = fileName.LastIndexOf('/');
        var backslash = fileName.LastIndexOf('\\');
        var start = Math.Max(slash, backslash) + 1;
        return fileName[start..];
    }

    private static bool IsSafePackageMediaFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName is "." or "..")
            return false;

        foreach (var value in fileName)
        {
            if (IsUnsafePackageMediaFileNameCharacter(value))
                return false;
        }

        return true;
    }

    private static bool IsUnsafePackageMediaFileNameCharacter(char value) =>
        char.IsControl(value) ||
        value is ':' or '?' or '*' or '/' or '\\' or '"' or '<' or '>' or '|';

    private static bool HasPackageFileExtension(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        return dot >= 0 && dot < fileName.Length - 1;
    }

}
