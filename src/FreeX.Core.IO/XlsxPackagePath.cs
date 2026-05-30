using System.Text;

namespace FreeX.Core.IO;

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

    public static string GetImageExtension(string contentType)
    {
        if (string.Equals(contentType, "image/jpeg", StringComparison.OrdinalIgnoreCase))
            return ".jpg";

        if (string.Equals(contentType, "image/bmp", StringComparison.OrdinalIgnoreCase))
            return ".bmp";

        if (string.Equals(contentType, "image/gif", StringComparison.OrdinalIgnoreCase))
            return ".gif";

        return ".png";
    }

    public static string GetWorksheetBackgroundMediaFileName(string? fileName, int backgroundIndex, string extension)
    {
        var candidate = Path.GetFileName(fileName ?? "");
        if (string.IsNullOrWhiteSpace(candidate) ||
            candidate.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return $"freexBackground{backgroundIndex}{extension}";
        }

        return Path.HasExtension(candidate)
            ? candidate
            : $"{candidate}{extension}";
    }

    private static string UnescapePathSegments(string path)
    {
        if (!path.Contains('%', StringComparison.Ordinal))
            return path;

        return string.Join('/', path.Split('/').Select(UnescapePathSegment));
    }

    private static string EscapePathSegments(string path)
    {
        if (!PathNeedsEscaping(path))
            return path;

        return string.Join('/', path.Split('/').Select(EscapePathSegment));
    }

    private static bool PathNeedsEscaping(string path)
    {
        for (var i = 0; i < path.Length; i++)
        {
            var value = path[i];
            if (value == '/' || IsSafeRelationshipPathCharacter(value))
                continue;

            return true;
        }

        return false;
    }

    private static bool IsSafeRelationshipPathCharacter(char value) =>
        value is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '.'
            or '-'
            or '_'
            or '~';

    private static string UnescapePathSegment(string segment)
    {
        try
        {
            if (IsEncodedDotControlSegment(segment))
                return segment;

            var separatorEscapeIndex = IndexOfEncodedPathSeparator(segment, 0);
            if (separatorEscapeIndex >= 0)
                return UnescapePathSegmentPreservingEncodedSeparators(segment, separatorEscapeIndex);

            return Uri.UnescapeDataString(segment);
        }
        catch (UriFormatException)
        {
            return segment;
        }
    }

    private static bool IsEncodedDotControlSegment(string segment)
    {
        if (!segment.Contains('%', StringComparison.Ordinal))
            return false;

        var unescaped = Uri.UnescapeDataString(segment);
        return unescaped is "." or ".." && segment.All(IsDotControlSegmentCharacter);
    }

    private static bool IsDotControlSegmentCharacter(char value) =>
        value is '.' or '%' or '2' or 'E' or 'e';

    private static string UnescapePathSegmentPreservingEncodedSeparators(string segment, int firstSeparatorEscapeIndex)
    {
        var builder = new StringBuilder(segment.Length);
        var segmentStart = 0;
        var separatorEscapeIndex = firstSeparatorEscapeIndex;
        while (separatorEscapeIndex >= 0)
        {
            builder.Append(Uri.UnescapeDataString(segment[segmentStart..separatorEscapeIndex]));
            builder.Append(segment, separatorEscapeIndex, 3);
            segmentStart = separatorEscapeIndex + 3;
            separatorEscapeIndex = IndexOfEncodedPathSeparator(segment, segmentStart);
        }

        builder.Append(Uri.UnescapeDataString(segment[segmentStart..]));
        return builder.ToString();
    }

    private static int IndexOfEncodedPathSeparator(string segment, int startIndex)
    {
        for (var i = startIndex; i <= segment.Length - 3; i++)
        {
            if (segment[i] != '%')
                continue;

            if (IsHexDigit(segment[i + 1], '2') && IsHexDigit(segment[i + 2], 'F') ||
                IsHexDigit(segment[i + 1], '5') && IsHexDigit(segment[i + 2], 'C'))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsHexDigit(char value, char expected) =>
        char.ToUpperInvariant(value) == expected;

    private static string EscapePathSegment(string segment) =>
        segment is "." or ".." ? segment : Uri.EscapeDataString(segment);
}
