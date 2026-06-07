using System.Xml;
using System.Xml.Linq;

namespace FreeX.Core.IO;

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
        IReadOnlyDictionary<string, string>? attributes,
        IReadOnlyCollection<string> modeledNames)
    {
        if (attributes is null)
            return;

        foreach (var attribute in attributes)
        {
            if (string.IsNullOrWhiteSpace(attribute.Key) || modeledNames.Contains(attribute.Key, StringComparer.Ordinal))
                continue;

            TrySetNativeAttribute(element, attribute.Key, attribute.Value);
        }
    }

    public static bool ApplyNativeAttributesIfMissing(
        XElement element,
        IReadOnlyDictionary<string, string>? attributes)
    {
        if (attributes is null)
            return false;

        var changed = false;
        foreach (var (name, value) in attributes)
        {
            changed |= TrySetNativeAttributeIfMissing(element, name, value);
        }

        return changed;
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

    public static bool TrySetNativeAttributeIfDifferent(XElement element, string name, string value)
    {
        try
        {
            var attributeName = XName.Get(name);
            if (string.Equals(element.Attribute(attributeName)?.Value, value, StringComparison.Ordinal))
                return false;

            element.SetAttributeValue(attributeName, value);
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

    public static bool ApplyNativeAttributesIfDifferent(
        XElement element,
        IReadOnlyDictionary<string, string>? attributes,
        IReadOnlyCollection<string> modeledNames)
    {
        if (attributes is null)
            return false;

        var changed = false;
        foreach (var (name, value) in attributes)
        {
            if (string.IsNullOrWhiteSpace(name) || modeledNames.Contains(name, StringComparer.Ordinal))
                continue;

            changed |= TrySetNativeAttributeIfDifferent(element, name, value);
        }

        return changed;
    }

    public static bool TrySetNativeAttributeIfMissing(XElement element, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        try
        {
            var attributeName = XName.Get(name);
            if (element.Attribute(attributeName) is not null)
                return false;

            element.SetAttributeValue(attributeName, value);
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

    public static bool TryAddNativeChildElement(
        XElement target,
        string? childXml,
        IReadOnlyCollection<string>? excludedLocalNames = null)
    {
        if (string.IsNullOrWhiteSpace(childXml))
            return false;

        try
        {
            var child = XElement.Parse(childXml);
            if (excludedLocalNames?.Contains(child.Name.LocalName) == true)
                return false;

            target.Add(child);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool ReplaceChildrenFromNativeXml(
        XElement target,
        IReadOnlyList<string> childXmls,
        IReadOnlyCollection<string>? excludedLocalNames = null)
    {
        if (childXmls.Count == 0)
            return false;

        target.Elements().Remove();
        foreach (var childXml in childXmls)
        {
            TryAddNativeChildElement(target, childXml, excludedLocalNames);
        }

        return true;
    }

    public static XElement? TryParseNativeElement(
        string? xml,
        XName expectedName,
        Func<XElement, bool>? normalize = null)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        try
        {
            var element = XElement.Parse(xml);
            if (element.Name != expectedName)
                return null;

            _ = normalize?.Invoke(element);
            return element;
        }
        catch
        {
            return null;
        }
    }

    public static string? ToBoolAttribute(bool? value) =>
        value is { } boolValue ? boolValue ? "1" : "0" : null;

    public static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
