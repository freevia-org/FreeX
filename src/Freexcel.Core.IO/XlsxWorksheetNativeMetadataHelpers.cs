using System.Xml;
using System.Xml.Linq;

namespace Freexcel.Core.IO;

internal static class XlsxWorksheetNativeMetadataHelpers
{
    public static void ReadNativeAttributes(
        XElement element,
        Dictionary<string, string> target,
        IReadOnlyCollection<string> modeledNames)
    {
        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || modeledNames.Contains(attribute.Name.LocalName, StringComparer.Ordinal))
                continue;

            target[attribute.Name.ToString()] = attribute.Value;
        }
    }

    public static void ApplyNativeAttributes(
        XElement element,
        Dictionary<string, string> attributes,
        IReadOnlyCollection<string> modeledNames)
    {
        foreach (var attribute in attributes)
        {
            if (string.IsNullOrWhiteSpace(attribute.Key) || modeledNames.Contains(attribute.Key, StringComparer.Ordinal))
                continue;

            TrySetNativeAttribute(element, attribute.Key, attribute.Value);
        }
    }

    public static bool TrySetNativeAttribute(XElement element, string name, string value)
    {
        try
        {
            element.SetAttributeValue(XName.Get(name), value);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    public static string? ToBoolAttribute(bool? value) =>
        value is { } boolValue ? boolValue ? "1" : "0" : null;

    public static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
