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

    /// <summary>
    /// Resolves a relationship's <c>Target</c> attribute -- a URI reference, per the OPC spec and
    /// RFC 3986, not a bare zip path -- into a zip entry path. Strips a trailing URI fragment
    /// (<c>#...</c>, meaningless for locating a package part), percent-decodes escaped path
    /// segments via <see cref="UnescapeRelationshipPathSegments"/>, then resolves relative/rooted-ness
    /// and collapses dot segments via <see cref="ResolveRelativeZipPath"/>.
    /// <para>
    /// This is additive: it does not change <see cref="ResolveRelativeZipPath"/>'s own behavior for
    /// existing callers that already unescape or otherwise pre-process their target before calling
    /// it (e.g. <c>XlsxPackagePath.ResolveRelationshipTarget</c> unescapes first for its own reasons
    /// and would double-decode if <see cref="ResolveRelativeZipPath"/> unescaped internally).
    /// </para>
    /// <para>
    /// Does NOT resolve zip-entry casing -- OPC part-name comparison is case-insensitive but zip
    /// entries are case-sensitive, so casing needs archive access; see the <paramref name="archive"/>
    /// overload below (<see cref="ResolveRelationshipTargetZipPath(ZipArchive, string, string)"/>)
    /// for that, or <see cref="FindEntry"/> directly.
    /// </para>
    /// </summary>
    public static string ResolveRelationshipTargetZipPath(string baseDirectory, string target) =>
        ResolveRelativeZipPath(baseDirectory, UnescapeRelationshipPathSegments(StripUriFragment(target)));

    /// <summary>
    /// Same resolution as <see cref="ResolveRelationshipTargetZipPath(string, string)"/>, plus
    /// case-canonicalization against the archive's actual entries via <see cref="FindEntry"/>.
    /// This matters beyond the immediate lookup: the returned path is frequently used as the base
    /// directory for resolving THAT part's own children (e.g. presentation.xml's directory when
    /// resolving its slide masters), or fed into <see cref="GetRelationshipPartPath"/> to find its
    /// own .rels sibling -- both of which are exact, case-sensitive zip-entry lookups elsewhere in
    /// the pipeline. Canonicalizing the case here, once, means every path derived from this one is
    /// already correctly cased instead of depending on every downstream lookup also having its own
    /// case-insensitive fallback. Falls back to the un-canonicalized resolved path when no entry
    /// matches (exactly or unambiguously by case) so a genuinely missing part still resolves to a
    /// path that a subsequent exact-or-fallback lookup correctly reports as missing.
    /// </summary>
    public static string ResolveRelationshipTargetZipPath(ZipArchive archive, string baseDirectory, string target)
    {
        var resolved = ResolveRelationshipTargetZipPath(baseDirectory, target);
        return FindEntry(archive, resolved)?.FullName ?? resolved;
    }

    private static string StripUriFragment(string target)
    {
        var hashIndex = target.IndexOf('#');
        return hashIndex < 0 ? target : target[..hashIndex];
    }

    /// <summary>
    /// Looks up a zip entry by OPC part-name path: exact (ordinal) match first, falling back to a
    /// case-insensitive match ONLY when it is unambiguous. OPC part-name equivalence is
    /// spec-defined as case-insensitive, but zip entries themselves are case-sensitive, so a
    /// (malformed) package could legally contain two entries differing only by case -- in that
    /// situation this returns null, exactly like an exact-match miss, rather than silently
    /// guessing which entry the caller meant.
    /// </summary>
    public static ZipArchiveEntry? FindEntry(ZipArchive archive, string path)
    {
        var exact = archive.GetEntry(path);
        if (exact is not null)
            return exact;

        ZipArchiveEntry? caseInsensitiveMatch = null;
        foreach (var entry in archive.Entries)
        {
            if (!string.Equals(entry.FullName, path, StringComparison.OrdinalIgnoreCase))
                continue;

            if (caseInsensitiveMatch is not null)
                return null; // ambiguous: more than one entry differs from `path` only by case.

            caseInsensitiveMatch = entry;
        }

        return caseInsensitiveMatch;
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
