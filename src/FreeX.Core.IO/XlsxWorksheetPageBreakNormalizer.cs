using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetPageBreakNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly HashSet<string> PageBreaksAttributes = ["count", "manualBreakCount"];
    private static readonly HashSet<string> BreakAttributes = ["id", "min", "max", "man", "pt"];

    public static bool NormalizeElement(XElement pageBreaks)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(pageBreaks, PageBreaksAttributes);
        changed |= RemoveUnexpectedChildren(pageBreaks);

        foreach (var breakElement in pageBreaks.Elements(WorksheetNs + "brk").ToList())
            changed |= NormalizeBreakElement(breakElement);

        var breakCount = pageBreaks.Elements(WorksheetNs + "brk").Count();
        var manualBreakCount = pageBreaks
            .Elements(WorksheetNs + "brk")
            .Count(element => !string.Equals(element.Attribute("man")?.Value, "0", StringComparison.Ordinal));

        changed |= XlsxXmlNormalizationHelpers.SetAttributeIfChanged(pageBreaks, "count", breakCount.ToString(CultureInfo.InvariantCulture));
        changed |= XlsxXmlNormalizationHelpers.SetAttributeIfChanged(pageBreaks, "manualBreakCount", manualBreakCount.ToString(CultureInfo.InvariantCulture));
        return changed;
    }

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot)
    {
        var changed = false;
        if (worksheetRoot.Element(WorksheetNs + "rowBreaks") is { } rowBreaks)
            changed |= NormalizeElement(rowBreaks);
        if (worksheetRoot.Element(WorksheetNs + "colBreaks") is { } columnBreaks)
            changed |= NormalizeElement(columnBreaks);
        return changed;
    }

    public static void NormalizeWorksheets(ZipArchive archive)
    {
        foreach (var worksheetEntry in archive.Entries.Where(IsWorksheetXmlEntry).ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is not null &&
                NormalizeWorksheetRoot(root))
            {
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
            }
        }
    }

    private static bool NormalizeBreakElement(XElement breakElement)
    {
        var normalizedId = NormalizeUnsignedIntOrNull(breakElement.Attribute("id")?.Value);
        if (normalizedId is null)
        {
            breakElement.Remove();
            return true;
        }

        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(breakElement, BreakAttributes);
        changed |= XlsxXmlNormalizationHelpers.SetAttributeIfChanged(breakElement, "id", normalizedId);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(breakElement, "min", NormalizeUnsignedIntOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(breakElement, "max", NormalizeUnsignedIntOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(breakElement, "man", NormalizeBoolean);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(breakElement, "pt", NormalizeBoolean);
        return changed;
    }

    private static bool RemoveUnexpectedChildren(XElement pageBreaks)
    {
        var changed = false;
        foreach (var child in pageBreaks.Elements().ToList())
        {
            if (child.Name == WorksheetNs + "brk")
                continue;

            child.Remove();
            changed = true;
        }

        return changed;
    }

    private static string? NormalizeBoolean(string? value)
    {
        var trimmed = value?.Trim();
        return trimmed switch
        {
            "0" or "1" => trimmed,
            "true" or "false" => trimmed,
            _ => null
        };
    }

    private static string? NormalizeUnsignedIntOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return uint.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static bool IsWorksheetXmlEntry(ZipArchiveEntry entry)
    {
        var path = XlsxPackagePath.NormalizeZipPath(entry.FullName.Replace('\\', '/'));
        return path.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
               path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
               !path.Contains("/_rels/", StringComparison.OrdinalIgnoreCase);
    }
}
