using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetHyperlinkNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static readonly IReadOnlySet<string> EmptyAttributes = new HashSet<string>(StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> HyperlinkAttributes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "ref",
            "location",
            "tooltip",
            "display"
        };

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot)
    {
        var hyperlinkContainers = worksheetRoot.Elements(WorksheetNs + "hyperlinks").ToList();
        if (hyperlinkContainers.Count == 0)
            return false;

        var changed = false;
        var hyperlinks = hyperlinkContainers[0];
        foreach (var duplicate in hyperlinkContainers.Skip(1))
        {
            hyperlinks.Add(duplicate.Elements(WorksheetNs + "hyperlink").Select(hyperlink => new XElement(hyperlink)));
            duplicate.Remove();
            changed = true;
        }

        changed |= NormalizeElement(hyperlinks);
        if (!hyperlinks.Elements(WorksheetNs + "hyperlink").Any())
        {
            hyperlinks.Remove();
            changed = true;
        }

        return changed;
    }

    public static bool NormalizeElement(XElement hyperlinks)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(hyperlinks, EmptyAttributes);
        changed |= XlsxXmlNormalizationHelpers.RemoveChildElementsExcept(hyperlinks, WorksheetNs + "hyperlink");

        var seenRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hyperlink in hyperlinks.Elements(WorksheetNs + "hyperlink").ToList())
        {
            var normalizedRef = NormalizeReference(hyperlink.Attribute("ref")?.Value);
            if (normalizedRef is null)
            {
                hyperlink.Remove();
                changed = true;
                continue;
            }

            changed |= RemoveUnknownHyperlinkAttributes(hyperlink);
            changed |= XlsxXmlNormalizationHelpers.SetAttributeIfChanged(hyperlink, "ref", normalizedRef);
            changed |= XlsxXmlNormalizationHelpers.NormalizeRelationshipId(hyperlink, RelationshipNs + "id");
            changed |= NormalizeLocation(hyperlink);
            if (!HasTarget(hyperlink) || !seenRefs.Add(normalizedRef))
            {
                hyperlink.Remove();
                changed = true;
                continue;
            }

            changed |= XlsxXmlNormalizationHelpers.RemoveAllNodes(hyperlink);
        }

        return changed;
    }

    public static void NormalizeWorksheets(ZipArchive archive)
    {
        foreach (var worksheetEntry in archive.Entries.Where(XlsxPackagePath.IsWorksheetXmlEntry).ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            if (NormalizeWorksheetRoot(root))
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
        }
    }

    private static bool HasTarget(XElement hyperlink) =>
        !string.IsNullOrWhiteSpace(hyperlink.Attribute(RelationshipNs + "id")?.Value) ||
        !string.IsNullOrWhiteSpace(hyperlink.Attribute("location")?.Value);

    private static bool NormalizeLocation(XElement hyperlink)
    {
        var location = hyperlink.Attribute("location");
        if (location is null)
            return false;

        var normalized = location.Value.Trim();
        if (normalized.Length == 0)
        {
            location.Remove();
            return true;
        }

        if (string.Equals(location.Value, normalized, StringComparison.Ordinal))
            return false;

        location.Value = normalized;
        return true;
    }

    private static string? NormalizeReference(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Contains(' ', StringComparison.Ordinal))
            return null;

        var parts = trimmed.Split(':');
        var sheet = SheetId.New();
        if (parts.Length == 1)
        {
            return CellAddress.TryParse(parts[0], sheet, out var address)
                ? address.ToA1()
                : null;
        }

        if (parts.Length == 2 &&
            CellAddress.TryParse(parts[0], sheet, out var start) &&
            CellAddress.TryParse(parts[1], sheet, out var end))
        {
            var range = new GridRange(start, end);
            return range.Start == range.End
                ? range.Start.ToA1()
                : $"{range.Start.ToA1()}:{range.End.ToA1()}";
        }

        // Pass through whole-column (A:A) and whole-row (3:3) references unchanged —
        // these are valid sqref forms that Excel supports but CellAddress.TryParse
        // cannot represent.  Returning the original trimmed value preserves them.
        if (parts.Length == 2 && IsWholeColumnOrRowRef(parts[0], parts[1]))
            return trimmed;

        return null;
    }

    /// <summary>
    /// True when any hyperlink under <paramref name="worksheetRoot"/> carries a whole-column (A:A)
    /// or whole-row (3:3) range ref. ClosedXML materializes such refs across the entire column/row
    /// when it loads the worksheet (~1M cells), so the load sanitizer must clamp them first.
    /// </summary>
    public static bool ContainsRangeHyperlinkRef(XElement worksheetRoot)
    {
        foreach (var hyperlinks in worksheetRoot.Elements(WorksheetNs + "hyperlinks"))
        {
            foreach (var hyperlink in hyperlinks.Elements(WorksheetNs + "hyperlink"))
            {
                if (IsRangeHyperlinkRef(hyperlink.Attribute("ref")?.Value))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Strips whole-column/row hyperlink elements from the ClosedXML-input copy of the worksheet so
    /// ClosedXML never materializes them across the entire column/row (~1M cells). The model is
    /// CellAddress-keyed and cannot represent a whole-column/row hyperlink anyway; the original refs
    /// are retained verbatim via the source-package snapshot, so a load→save round-trip still emits
    /// the unchanged whole-column/row ref. Returns true when anything changed.
    /// </summary>
    public static bool StripRangeHyperlinkRefs(XElement worksheetRoot)
    {
        var changed = false;
        foreach (var hyperlinks in worksheetRoot.Elements(WorksheetNs + "hyperlinks").ToList())
        {
            foreach (var hyperlink in hyperlinks.Elements(WorksheetNs + "hyperlink").ToList())
            {
                if (IsRangeHyperlinkRef(hyperlink.Attribute("ref")?.Value))
                {
                    hyperlink.Remove();
                    changed = true;
                }
            }

            if (!hyperlinks.Elements(WorksheetNs + "hyperlink").Any())
            {
                hyperlinks.Remove();
                changed = true;
            }
        }

        return changed;
    }

    // ClosedXML materializes a ranged hyperlink's "ref" attribute into one hyperlink entry per
    // cell in the range. A bounded range that is merely large (well beyond anything a user would
    // plausibly attach a single hyperlink to, e.g. A1:Z100000 = 2.6M cells) blows up the same way
    // whole-column/row refs do, even though it isn't recognized as whole-column/row. Any bounded
    // range above this cap gets stripped alongside the whole-column/row case below.
    private const long MaxBoundedHyperlinkRangeCellCount = 100_000;

    private static bool IsRangeHyperlinkRef(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Contains(' ', StringComparison.Ordinal))
            return false;

        var parts = trimmed.Split(':');
        if (parts.Length != 2)
            return false;

        if (IsWholeColumnOrRowRef(parts[0], parts[1]))
            return true;

        return IsOversizedBoundedRangeRef(parts[0], parts[1]);
    }

    private static bool IsOversizedBoundedRangeRef(string left, string right)
    {
        var sheet = SheetId.New();
        if (!CellAddress.TryParse(left, sheet, out var start) || !CellAddress.TryParse(right, sheet, out var end))
            return false;

        var range = new GridRange(start, end);
        return range.CellCount > MaxBoundedHyperlinkRangeCellCount;
    }

    private static bool IsWholeColumnOrRowRef(string left, string right)
    {
        // Whole-column: both sides are a single column letter sequence with no digits (e.g. A, AA).
        // Whole-row:    both sides are a positive integer with no letters (e.g. 3, 10).
        if (left.Length == 0 || right.Length == 0)
            return false;

        var leftIsLetters = left.All(char.IsAsciiLetter);
        var rightIsLetters = right.All(char.IsAsciiLetter);
        if (leftIsLetters && rightIsLetters)
            return true;

        var leftIsDigits = left.All(char.IsAsciiDigit);
        var rightIsDigits = right.All(char.IsAsciiDigit);
        return leftIsDigits && rightIsDigits;
    }

    private static bool RemoveUnknownHyperlinkAttributes(XElement element)
    {
        var changed = false;
        foreach (var attribute in element.Attributes().ToList())
        {
            if (attribute.IsNamespaceDeclaration ||
                (attribute.Name.NamespaceName.Length == 0 && HyperlinkAttributes.Contains(attribute.Name.LocalName)) ||
                attribute.Name == RelationshipNs + "id")
            {
                continue;
            }

            attribute.Remove();
            changed = true;
        }

        return changed;
    }

}
