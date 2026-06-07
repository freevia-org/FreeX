using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetProtectedRangeNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly HashSet<string> NoAttributes = [];

    private static readonly HashSet<string> ProtectedRangeAttributes =
    [
        "password",
        "algorithmName",
        "hashValue",
        "saltValue",
        "spinCount",
        "sqref",
        "name",
        "securityDescriptor"
    ];

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot)
    {
        var changed = false;
        var keptProtectedRanges = false;
        foreach (var protectedRanges in worksheetRoot.Elements(WorksheetNs + "protectedRanges").ToList())
        {
            if (keptProtectedRanges)
            {
                protectedRanges.Remove();
                changed = true;
                continue;
            }

            changed |= NormalizeProtectedRangesElement(protectedRanges);
            if (ShouldRemoveProtectedRangesElement(protectedRanges))
            {
                protectedRanges.Remove();
                changed = true;
                continue;
            }

            keptProtectedRanges = true;
        }

        return changed;
    }

    public static void NormalizeWorksheets(ZipArchive archive)
    {
        foreach (var worksheetEntry in archive.Entries.Where(IsWorksheetXmlEntry).ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            if (NormalizeWorksheetRoot(root))
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
        }
    }

    private static bool NormalizeProtectedRangesElement(XElement protectedRanges)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(protectedRanges, NoAttributes);
        changed |= RemoveUnexpectedChildElements(protectedRanges, WorksheetNs + "protectedRange");

        foreach (var protectedRange in protectedRanges.Elements(WorksheetNs + "protectedRange").ToList())
        {
            changed |= NormalizeProtectedRangeElement(protectedRange);
            if (!ShouldRemoveProtectedRangeElement(protectedRange))
                continue;

            protectedRange.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool ShouldRemoveProtectedRangesElement(XElement protectedRanges) =>
        !protectedRanges.Elements(WorksheetNs + "protectedRange").Any();

    private static bool NormalizeProtectedRangeElement(XElement protectedRange)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(protectedRange, ProtectedRangeAttributes);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(protectedRange, "password", NormalizeLegacyPasswordHashOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(protectedRange, "sqref", NormalizeSqref);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(protectedRange, "name", NormalizeOptionalText);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(protectedRange, "hashValue", NormalizeBase64BinaryOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(protectedRange, "saltValue", NormalizeBase64BinaryOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(protectedRange, "spinCount", XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull);
        changed |= XlsxXmlNormalizationHelpers.RemoveChildElements(protectedRange);
        return changed;
    }

    private static bool ShouldRemoveProtectedRangeElement(XElement protectedRange) =>
        string.IsNullOrWhiteSpace(protectedRange.Attribute("sqref")?.Value) ||
        string.IsNullOrWhiteSpace(protectedRange.Attribute("name")?.Value);

    private static bool RemoveUnexpectedChildElements(XElement element, XName allowedChildName)
    {
        var changed = false;
        foreach (var child in element.Elements().Where(child => child.Name != allowedChildName).ToList())
        {
            child.Remove();
            changed = true;
        }

        return changed;
    }

    private static string? NormalizeOptionalText(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string? NormalizeSqref(string? value)
    {
        var tokens = value?
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens is { Length: > 0 }
            ? string.Join(' ', tokens)
            : null;
    }

    private static string? NormalizeBase64BinaryOrNull(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        try
        {
            _ = Convert.FromBase64String(trimmed);
            return trimmed;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string? NormalizeLegacyPasswordHashOrNull(string? value)
    {
        var trimmed = value?.Trim();
        if (trimmed is not { Length: 4 } ||
            !trimmed.All(static c => char.IsAsciiHexDigit(c)))
        {
            return null;
        }

        return trimmed.ToUpperInvariant();
    }

    private static bool IsWorksheetXmlEntry(ZipArchiveEntry entry) =>
        entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
        entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
        !entry.FullName.Contains("/_rels/", StringComparison.OrdinalIgnoreCase);
}
