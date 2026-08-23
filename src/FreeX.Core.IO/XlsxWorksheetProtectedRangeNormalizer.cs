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

    private static bool NormalizeProtectedRangesElement(XElement protectedRanges)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(protectedRanges, NoAttributes);
        changed |= XlsxXmlNormalizationHelpers.RemoveChildElementsExcept(protectedRanges, WorksheetNs + "protectedRange");

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
        if (IsUnsupportedMultiAreaProtectedRange(protectedRange))
            return false;

        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(protectedRange, ProtectedRangeAttributes);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(protectedRange, "password", NormalizeLegacyPasswordHashOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(protectedRange, "sqref", NormalizeSqref);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(protectedRange, "name", NormalizeOptionalText);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(protectedRange, "hashValue", NormalizeBase64BinaryOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(protectedRange, "saltValue", NormalizeBase64BinaryOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(protectedRange, "spinCount", XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull);
        changed |= NormalizeProtectedRangeChildren(protectedRange);
        return changed;
    }

    private static bool ShouldRemoveProtectedRangeElement(XElement protectedRange) =>
        string.IsNullOrWhiteSpace(protectedRange.Attribute("sqref")?.Value) ||
        string.IsNullOrWhiteSpace(protectedRange.Attribute("name")?.Value);

    private static bool IsUnsupportedMultiAreaProtectedRange(XElement protectedRange)
    {
        var tokens = protectedRange.Attribute("sqref")?.Value
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens is { Length: > 1 };
    }

    private static bool NormalizeProtectedRangeChildren(XElement protectedRange)
    {
        var children = protectedRange.Elements().ToList();
        if (children.Count == 0)
            return false;

        if (children.Any(child => child.Name != WorksheetNs + "extLst"))
            return XlsxXmlNormalizationHelpers.RemoveChildElements(protectedRange);

        var changed = false;
        var keptExtensionList = false;
        foreach (var extensionList in protectedRange.Elements(WorksheetNs + "extLst").ToList())
            changed |= XlsxWorksheetExtensionListNormalizer.NormalizeChild(extensionList, ref keptExtensionList);

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

}
