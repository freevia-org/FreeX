using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorkbookOleSizeNormalizer
{
    private static readonly IReadOnlySet<string> OleSizeAttributes =
        new HashSet<string>(StringComparer.Ordinal) { "ref" };
    private static readonly Regex CellRangePattern = new(
        "^[A-Z]{1,3}[1-9][0-9]{0,6}(:[A-Z]{1,3}[1-9][0-9]{0,6})?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool NormalizeWorkbookRoot(XElement workbookRoot, XNamespace workbookNs)
    {
        var changed = false;
        var keptOleSize = false;
        foreach (var oleSize in workbookRoot.Elements(workbookNs + "oleSize").ToList())
        {
            if (keptOleSize)
            {
                oleSize.Remove();
                changed = true;
                continue;
            }

            changed |= NormalizeElement(oleSize);
            if (ShouldRemoveElement(oleSize))
            {
                oleSize.Remove();
                changed = true;
                continue;
            }

            keptOleSize = true;
        }

        return changed;
    }

    public static bool NormalizeElement(XElement oleSize)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(oleSize, OleSizeAttributes);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(oleSize, "ref", NormalizeCellRange);
        changed |= RemoveAllNodes(oleSize);
        return changed;
    }

    public static bool ShouldRemoveElement(XElement oleSize) =>
        NormalizeCellRange(oleSize.Attribute("ref")?.Value) is null;

    private static bool RemoveAllNodes(XElement element)
    {
        if (!element.Nodes().Any())
            return false;

        element.RemoveNodes();
        return true;
    }

    private static string? NormalizeCellRange(string? value)
    {
        var trimmed = value?.Trim().ToUpperInvariant();
        return trimmed is not null && CellRangePattern.IsMatch(trimmed) ? trimmed : null;
    }
}
