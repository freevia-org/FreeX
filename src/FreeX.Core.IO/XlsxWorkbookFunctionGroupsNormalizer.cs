using System.Globalization;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorkbookFunctionGroupsNormalizer
{
    private static readonly HashSet<string> FunctionGroupsAttributes =
    [
        "builtInGroupCount"
    ];

    private static readonly HashSet<string> FunctionGroupAttributes =
    [
        "name"
    ];

    public static bool NormalizeElement(XElement functionGroups)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(functionGroups, FunctionGroupsAttributes);
        changed |= NormalizeUnsignedIntAttribute(functionGroups, "builtInGroupCount");

        foreach (var child in functionGroups.Elements().ToList())
        {
            if (child.Name.LocalName != "functionGroup" ||
                child.Name.NamespaceName != functionGroups.Name.NamespaceName)
            {
                child.Remove();
                changed = true;
                continue;
            }

            changed |= NormalizeFunctionGroup(child);
            if (child.Attribute("name") is null)
            {
                child.Remove();
                changed = true;
            }
        }

        return changed;
    }

    private static bool NormalizeFunctionGroup(XElement functionGroup)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(functionGroup, FunctionGroupAttributes);
        changed |= XlsxXmlNormalizationHelpers.RemoveAllNodes(functionGroup);
        if (string.IsNullOrWhiteSpace(functionGroup.Attribute("name")?.Value))
        {
            functionGroup.Attribute("name")?.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool NormalizeUnsignedIntAttribute(XElement element, string attributeName)
    {
        var attribute = element.Attribute(attributeName);
        var trimmed = attribute?.Value.Trim();
        if (uint.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            var normalized = parsed.ToString(CultureInfo.InvariantCulture);
            if (string.Equals(attribute!.Value, normalized, StringComparison.Ordinal))
                return false;

            attribute.Value = normalized;
            return true;
        }

        if (attribute is null)
            return false;

        attribute.Remove();
        return true;
    }
}
