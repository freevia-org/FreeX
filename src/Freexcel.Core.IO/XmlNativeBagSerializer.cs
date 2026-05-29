using System.Xml;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

/// <summary>
/// Serializes and deserializes <see cref="NativeXmlPreserveBag"/> entries to/from XML strings.
///
/// Each bag entry stores residual XML (attributes and child elements not yet modelled by Freexcel)
/// as a serialized XML fragment wrapped in a lightweight &lt;e /&gt; element.
/// Example: &lt;e customAttr="foo"&gt;&lt;child /&gt;&lt;/e&gt;
/// </summary>
internal static class XmlNativeBagSerializer
{
    private const string WrapperTag = "e";

    /// <summary>
    /// Serializes residual attributes and child XML strings into a single string value
    /// suitable for storing in a <see cref="NativeXmlPreserveBag"/>.
    /// Returns null when there is nothing to preserve (no extra attributes or children).
    /// </summary>
    public static string? Serialize(
        IReadOnlyDictionary<string, string> nativeAttributes,
        IReadOnlyList<string>? nativeChildXmls = null)
    {
        if (nativeAttributes.Count == 0 && (nativeChildXmls is null || nativeChildXmls.Count == 0))
            return null;

        var wrapper = new XElement(WrapperTag);

        foreach (var (name, value) in nativeAttributes)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;
            try
            {
                wrapper.SetAttributeValue(XName.Get(name), value);
            }
            catch (ArgumentException) { }
            catch (XmlException) { }
        }

        if (nativeChildXmls is not null)
        {
            foreach (var childXml in nativeChildXmls)
            {
                if (string.IsNullOrWhiteSpace(childXml))
                    continue;
                try
                {
                    wrapper.Add(XElement.Parse(childXml, LoadOptions.PreserveWhitespace));
                }
                catch { }
            }
        }

        return wrapper.Attributes().Any() || wrapper.HasElements
            ? wrapper.ToString(SaveOptions.DisableFormatting)
            : null;
    }

    /// <summary>
    /// Deserializes a bag entry string back into attributes and child XML strings.
    /// Returns empty collections when the value is null or unparseable.
    /// </summary>
    public static (Dictionary<string, string> NativeAttributes, List<string> NativeChildXmls) Deserialize(string? value)
    {
        var attrs = new Dictionary<string, string>(StringComparer.Ordinal);
        var children = new List<string>();

        if (string.IsNullOrWhiteSpace(value))
            return (attrs, children);

        try
        {
            var element = XElement.Parse(value, LoadOptions.PreserveWhitespace);
            foreach (var attr in element.Attributes())
            {
                if (!attr.IsNamespaceDeclaration)
                    attrs[attr.Name.ToString()] = attr.Value;
            }
            foreach (var child in element.Elements())
                children.Add(child.ToString(SaveOptions.DisableFormatting));
        }
        catch { }

        return (attrs, children);
    }

    /// <summary>
    /// Applies a bag entry to an existing XML element by setting extra attributes and replacing child elements.
    /// Returns true if any change was made.
    /// </summary>
    public static bool ApplyToElement(XElement target, string? bagValue, IReadOnlyCollection<string> modeledAttributes)
    {
        if (string.IsNullOrWhiteSpace(bagValue))
            return false;

        var (attrs, children) = Deserialize(bagValue);
        var changed = false;

        foreach (var (name, value) in attrs)
        {
            if (string.IsNullOrWhiteSpace(name) || modeledAttributes.Contains(name, StringComparer.Ordinal))
                continue;
            changed |= TrySetAttributeIfDifferent(target, name, value);
        }

        if (children.Count > 0)
        {
            var targetChildNodesAreElements = target.Nodes().All(node => node is XElement);
            if (targetChildNodesAreElements
                && target.Elements()
                    .Select(child => child.ToString(SaveOptions.DisableFormatting))
                    .SequenceEqual(children, StringComparer.Ordinal))
                return changed;

            target.RemoveNodes();
            changed = true;
            foreach (var childXml in children)
            {
                if (string.IsNullOrWhiteSpace(childXml))
                    continue;
                try
                {
                    target.Add(XElement.Parse(childXml, LoadOptions.PreserveWhitespace));
                }
                catch { }
            }
        }

        return changed;
    }

    private static bool TrySetAttributeIfDifferent(XElement element, string name, string value)
    {
        try
        {
            var xname = XName.Get(name);
            if (string.Equals(element.Attribute(xname)?.Value, value, StringComparison.Ordinal))
                return false;
            element.SetAttributeValue(xname, value);
            return true;
        }
        catch (ArgumentException) { return false; }
        catch (XmlException) { return false; }
    }
}
