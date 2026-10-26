using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class SpreadsheetXmlFileAdapter
{
    private static bool TryFormatSpreadsheetDateTime(DateTimeValue value, out string text)
    {
        text = "";
        if (!double.IsFinite(value.Value))
            return false;

        try
        {
            text = value.ToDateTime().ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string UniqueSheetName(Workbook workbook, string? rawName, int index)
    {
        var baseName = string.IsNullOrWhiteSpace(rawName) ? $"Sheet{index}" : rawName.Trim();
        baseName = SanitizeSheetName(baseName);
        var candidate = baseName;
        var suffix = 1;
        while (workbook.ValidateSheetName(candidate) is not null)
        {
            var marker = $" ({suffix++})";
            // r195: see SurrogateSafeTruncation -- this loop re-slices at a different cut point than
            // the initial truncation, so it needs the same guard.
            candidate = SurrogateSafeTruncation.LimitToTextElements(baseName, 31 - marker.Length) + marker;
        }

        return candidate;
    }

    private static string SanitizeSheetName(string value)
    {
        Span<char> invalid = [':', '\\', '/', '?', '*', '[', ']'];
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
            builder.Append(invalid.Contains(ch) ? '_' : ch);

        var sanitized = builder.ToString().Trim('\'');
        if (sanitized.Length == 0)
            return "Sheet";

        // r194: see SurrogateSafeTruncation -- a raw [..31] can leave a lone surrogate that makes
        // every later .xlsx save throw.
        return sanitized.Length <= 31
            ? sanitized
            : SurrogateSafeTruncation.LimitToTextElements(sanitized, 31);
    }

    private static HyperlinkTargetKind GetHyperlinkTargetKind(string target)
    {
        if (target.StartsWith("#", StringComparison.Ordinal))
            return HyperlinkTargetKind.PlaceInThisDocument;

        return target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            ? HyperlinkTargetKind.EmailAddress
            : HyperlinkTargetKind.ExistingFileOrWebPage;
    }

    private static string GetHyperlinkBookmark(string target) =>
        target.StartsWith("#", StringComparison.Ordinal) ? target[1..] : "";

    /// <summary>
    /// Produces the <c>ss:HRef</c> string for a cell's hyperlink, or <c>null</c> when there is nothing
    /// to emit. Internal "place in this document" targets are written with a leading <c>#</c> (the
    /// SpreadsheetML form Excel uses, e.g. <c>#Sheet1!A1</c>) so that on reload they are classified as
    /// internal again instead of being mistaken for a relative file path. Without the <c>#</c>, a
    /// subsequent xlsx save would treat the target as an external link and fail to build a URI for it
    /// (ClosedXML <c>AddHyperlinkRelationship(null)</c>). Empty/whitespace targets are dropped.
    /// </summary>
    private static string? BuildHyperlinkHref(string? target, HyperlinkMetadata? metadata)
    {
        var isInternal = metadata?.LinkType == HyperlinkTargetKind.PlaceInThisDocument;

        // Prefer the bookmark for internal links (the in-document address); fall back to the target.
        var effective = isInternal && !string.IsNullOrWhiteSpace(metadata?.Bookmark)
            ? metadata!.Bookmark
            : target;

        if (string.IsNullOrWhiteSpace(effective))
            return null;

        effective = effective.Trim();
        if (isInternal && !effective.StartsWith("#", StringComparison.Ordinal))
            return "#" + effective;

        return effective;
    }

}
