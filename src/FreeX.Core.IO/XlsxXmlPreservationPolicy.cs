using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxXmlPreservationPolicy
{
    public static bool IsOfficeRevisionAttribute(XAttribute attribute) =>
        !attribute.IsNamespaceDeclaration &&
        string.Equals(attribute.Name.LocalName, "uid", StringComparison.Ordinal) &&
        IsOfficeRevisionNamespace(attribute.Name.NamespaceName);

    public static void RemoveOfficeRevisionAttributes(XElement element)
    {
        foreach (var attribute in element.Attributes().Where(IsOfficeRevisionAttribute).ToList())
            attribute.Remove();

        foreach (var namespaceAttribute in element.Attributes().Where(attribute =>
                     attribute.IsNamespaceDeclaration &&
                     IsOfficeRevisionNamespace(attribute.Value) &&
                     !element.Attributes().Any(other =>
                         !other.IsNamespaceDeclaration &&
                         other.Name.NamespaceName == attribute.Value)).ToList())
        {
            namespaceAttribute.Remove();
        }
    }

    public static bool MergeMissingAttributes(
        XElement sourceElement,
        XElement targetElement,
        IReadOnlyCollection<string>? excludedLocalNames = null)
    {
        var changed = false;
        foreach (var attribute in sourceElement.Attributes())
        {
            if (attribute.IsNamespaceDeclaration ||
                IsOfficeRevisionAttribute(attribute) ||
                excludedLocalNames?.Contains(attribute.Name.LocalName, StringComparer.Ordinal) == true ||
                targetElement.Attribute(attribute.Name) is not null)
            {
                continue;
            }

            targetElement.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        return changed;
    }

    private static bool IsOfficeRevisionNamespace(string namespaceName) =>
        namespaceName.StartsWith("http://schemas.microsoft.com/office/spreadsheetml/", StringComparison.Ordinal) &&
        namespaceName.Contains("/revision", StringComparison.Ordinal);
}
