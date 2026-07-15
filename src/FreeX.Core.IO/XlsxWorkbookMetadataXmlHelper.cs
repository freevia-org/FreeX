using System.Xml;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorkbookMetadataXmlHelper
{
    public static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    public static int? ClampWorkbookViewInteger(int? value, int min, int max) =>
        value is { } intValue ? Math.Clamp(intValue, min, max) : null;

    public static bool HasRevisionProtectionMetadata(NativeXmlPreserveBag? metadata)
    {
        if (metadata is null)
            return false;
        var (attrs, _) = XmlNativeBagSerializer.Deserialize(metadata.Get("workbookProtection"));
        return attrs.ContainsKey("lockRevision") || attrs.ContainsKey("revisionsPassword");
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

    public static void ApplyNativeAttributes(
        XElement element,
        IEnumerable<KeyValuePair<string, string>> attributes,
        params string[] excludedNames)
    {
        foreach (var attribute in attributes)
        {
            if (string.IsNullOrWhiteSpace(attribute.Key) ||
                excludedNames.Contains(attribute.Key, StringComparer.Ordinal))
            {
                continue;
            }

            TrySetNativeAttribute(element, attribute.Key, attribute.Value);
        }
    }
}
