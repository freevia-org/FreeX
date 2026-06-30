using System.IO.Compression;
using System.Text;

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

    public static string GetRelativeZipPath(string baseDirectory, string targetPath)
    {
        var baseSegments = NormalizeZipEntryPath(baseDirectory)
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        var targetSegments = NormalizeZipEntryPath(targetPath)
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        var common = 0;
        while (common < baseSegments.Length &&
               common < targetSegments.Length &&
               string.Equals(baseSegments[common], targetSegments[common], StringComparison.OrdinalIgnoreCase))
        {
            common++;
        }

        var segments = Enumerable
            .Repeat("..", baseSegments.Length - common)
            .Concat(targetSegments.Skip(common))
            .ToArray();
        return segments.Length == 0 ? string.Empty : string.Join('/', segments);
    }

    public static string UnescapeRelationshipPathSegments(string path)
    {
        if (!path.Contains('%', StringComparison.Ordinal))
            return path;

        return string.Join('/', path.Split('/').Select(UnescapeRelationshipPathSegment));
    }

    public static string EscapeRelationshipPathSegments(string path)
    {
        if (!RelationshipPathNeedsEscaping(path))
            return path;

        return string.Join('/', path.Split('/').Select(EscapeRelationshipPathSegment));
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

    private static bool RelationshipPathNeedsEscaping(string path)
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

    private static string UnescapeRelationshipPathSegment(string segment)
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

    private static string EscapeRelationshipPathSegment(string segment) =>
        segment is "." or ".." ? segment : Uri.EscapeDataString(segment);
}
