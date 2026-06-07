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
        changed |= RemoveUnknownAttributes(oleSize, OleSizeAttributes);
        changed |= NormalizeReference(oleSize);
        changed |= RemoveAllNodes(oleSize);
        return changed;
    }

    public static bool ShouldRemoveElement(XElement oleSize) =>
        NormalizeCellRange(oleSize.Attribute("ref")?.Value) is null;

    private static bool RemoveUnknownAttributes(XElement element, IReadOnlySet<string> allowedNames)
    {
        var changed = false;
        foreach (var attribute in element.Attributes().ToList())
        {
            if (attribute.IsNamespaceDeclaration ||
                (attribute.Name.NamespaceName.Length == 0 && allowedNames.Contains(attribute.Name.LocalName)))
            {
                continue;
            }

            attribute.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool NormalizeReference(XElement oleSize)
    {
        var attribute = oleSize.Attribute("ref");
        var normalizedReference = NormalizeCellRange(attribute?.Value);
        if (normalizedReference is null)
        {
            if (attribute is null)
                return false;

            attribute.Remove();
            return true;
        }

        if (attribute is not null && string.Equals(attribute.Value, normalizedReference, StringComparison.Ordinal))
            return false;

        oleSize.SetAttributeValue("ref", normalizedReference);
        return true;
    }

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
