using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxExtensionListNormalizer
{
    private static readonly HashSet<string> NoAttributes = [];
    private static readonly HashSet<string> ExtensionAttributes = ["uri"];

    public static bool NormalizeRoot(XElement root, XNamespace mainNamespace)
    {
        var changed = false;
        var keptExtensionList = false;
        var seenUris = new HashSet<string>(StringComparer.Ordinal);
        foreach (var extensionList in root.Elements(mainNamespace + "extLst").ToList())
        {
            if (keptExtensionList)
            {
                extensionList.Remove();
                changed = true;
                continue;
            }

            changed |= NormalizeElement(extensionList, mainNamespace, seenUris);
            if (ShouldRemove(extensionList, mainNamespace))
            {
                extensionList.Remove();
                changed = true;
                continue;
            }

            keptExtensionList = true;
        }

        return changed;
    }

    public static bool NormalizeElement(XElement extensionList, XNamespace mainNamespace) =>
        NormalizeElement(extensionList, mainNamespace, new HashSet<string>(StringComparer.Ordinal));

    public static bool ShouldRemove(XElement extensionList, XNamespace mainNamespace) =>
        !extensionList.Elements(mainNamespace + "ext").Any();

    private static bool NormalizeElement(
        XElement extensionList,
        XNamespace mainNamespace,
        HashSet<string> seenUris)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(extensionList, NoAttributes);
        changed |= XlsxXmlNormalizationHelpers.RemoveChildElementsExcept(extensionList, mainNamespace + "ext");

        foreach (var extension in extensionList.Elements(mainNamespace + "ext").ToList())
        {
            changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(extension, ExtensionAttributes);
            changed |= NormalizeUri(extension);
            var uri = extension.Attribute("uri")?.Value;
            if (string.IsNullOrWhiteSpace(uri) || !seenUris.Add(uri))
            {
                extension.Remove();
                changed = true;
            }
        }

        return changed;
    }

    private static bool NormalizeUri(XElement extension)
    {
        var attribute = extension.Attribute("uri");
        var trimmed = attribute?.Value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            if (attribute is null)
                return false;

            attribute.Remove();
            return true;
        }

        if (string.Equals(attribute!.Value, trimmed, StringComparison.Ordinal))
            return false;

        extension.SetAttributeValue("uri", trimmed);
        return true;
    }
}
