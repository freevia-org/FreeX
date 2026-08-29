using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetDimensionNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly IReadOnlySet<string> DimensionAttributes =
        new HashSet<string>(StringComparer.Ordinal) { "ref" };
    private static readonly Regex CellRangePattern = new(
        "^[A-Z]{1,3}[1-9][0-9]{0,6}(:[A-Z]{1,3}[1-9][0-9]{0,6})?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot)
    {
        var dimensions = worksheetRoot.Elements(WorksheetNs + "dimension").ToList();
        if (dimensions.Count == 0)
            return false;

        var changed = false;
        var keptDimension = false;
        foreach (var dimension in dimensions)
        {
            if (keptDimension)
            {
                dimension.Remove();
                changed = true;
                continue;
            }

            var normalizedReference = NormalizeCellRange(dimension.Attribute("ref")?.Value);
            if (normalizedReference is null)
            {
                dimension.Remove();
                changed = true;
                continue;
            }

            keptDimension = true;
            changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(dimension, DimensionAttributes);
            changed |= XlsxXmlNormalizationHelpers.SetAttributeIfChanged(dimension, "ref", normalizedReference);
            changed |= XlsxXmlNormalizationHelpers.RemoveChildElements(dimension);
        }

        return changed;
    }

    private static string? NormalizeCellRange(string? value)
    {
        var trimmed = value?.Trim().ToUpperInvariant();
        return trimmed is not null && CellRangePattern.IsMatch(trimmed) ? trimmed : null;
    }

}
