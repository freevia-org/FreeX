using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetMergeCellsNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly IReadOnlySet<string> MergeCellsAttributes =
        new HashSet<string>(StringComparer.Ordinal) { "count" };
    private static readonly IReadOnlySet<string> MergeCellAttributes =
        new HashSet<string>(StringComparer.Ordinal) { "ref" };
    private static readonly Regex CellRangePattern = new(
        "^[A-Z]{1,3}[1-9][0-9]{0,6}(:[A-Z]{1,3}[1-9][0-9]{0,6})?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot)
    {
        var mergeCells = worksheetRoot.Element(WorksheetNs + "mergeCells");
        return mergeCells is not null && NormalizeElement(mergeCells);
    }

    public static bool NormalizeElement(XElement mergeCells)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(mergeCells, MergeCellsAttributes);
        changed |= XlsxXmlNormalizationHelpers.RemoveChildElementsExcept(mergeCells, WorksheetNs + "mergeCell");

        foreach (var mergeCell in mergeCells.Elements(WorksheetNs + "mergeCell").ToList())
            changed |= NormalizeMergeCellElement(mergeCell);

        var count = mergeCells.Elements(WorksheetNs + "mergeCell").Count();
        if (count == 0)
        {
            mergeCells.Remove();
            return true;
        }

        changed |= XlsxXmlNormalizationHelpers.SetAttributeIfChanged(
            mergeCells,
            "count",
            count.ToString(CultureInfo.InvariantCulture));
        return changed;
    }

    private static bool NormalizeMergeCellElement(XElement mergeCell)
    {
        var normalizedReference = NormalizeCellRange(mergeCell.Attribute("ref")?.Value);
        if (normalizedReference is null)
        {
            mergeCell.Remove();
            return true;
        }

        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(mergeCell, MergeCellAttributes);
        changed |= XlsxXmlNormalizationHelpers.SetAttributeIfChanged(mergeCell, "ref", normalizedReference);
        changed |= XlsxXmlNormalizationHelpers.RemoveChildElements(mergeCell);
        return changed;
    }

    private static string? NormalizeCellRange(string? value)
    {
        var trimmed = value?.Trim().ToUpperInvariant();
        return trimmed is not null && CellRangePattern.IsMatch(trimmed) ? trimmed : null;
    }

}
