using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorkbookFileVersionNormalizer
{
    private static readonly HashSet<string> FileVersionAttributes =
    [
        "appName",
        "lastEdited",
        "lowestEdited",
        "rupBuild",
        "codeName"
    ];

    public static bool NormalizeElement(XElement fileVersion)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(fileVersion);
        changed |= RemoveAllNodes(fileVersion);
        return changed;
    }

    private static bool RemoveUnknownAttributes(XElement element)
    {
        var changed = false;
        foreach (var attribute in element.Attributes().ToList())
        {
            if (attribute.IsNamespaceDeclaration ||
                (attribute.Name.NamespaceName.Length == 0 && FileVersionAttributes.Contains(attribute.Name.LocalName)))
            {
                continue;
            }

            attribute.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool RemoveAllNodes(XElement element)
    {
        if (!element.Nodes().Any())
            return false;

        element.RemoveNodes();
        return true;
    }
}
