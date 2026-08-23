using System.Globalization;
using System.Xml.Linq;

namespace Free.Shared.Opc;

internal static class XlsxXmlNormalizationHelpers
{
    public static bool RemoveUnknownAttributes(XElement element, IReadOnlySet<string> allowedNames)
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

    public static bool RemoveUnknownAttributes(XElement element, params XName[] allowedNames)
    {
        var changed = false;
        foreach (var attribute in element.Attributes().ToList())
        {
            if (attribute.IsNamespaceDeclaration || allowedNames.Contains(attribute.Name))
                continue;

            attribute.Remove();
            changed = true;
        }

        return changed;
    }

    public static bool RemoveUnknownAttributes(
        XElement element,
        IReadOnlySet<string> allowedLocalNames,
        params XName[] allowedNames)
    {
        var changed = false;
        foreach (var attribute in element.Attributes().ToList())
        {
            if (attribute.IsNamespaceDeclaration ||
                allowedNames.Contains(attribute.Name) ||
                (attribute.Name.NamespaceName.Length == 0 && allowedLocalNames.Contains(attribute.Name.LocalName)))
            {
                continue;
            }

            attribute.Remove();
            changed = true;
        }

        return changed;
    }

    public static bool RemoveAllNodes(XElement element)
    {
        if (!element.Nodes().Any())
            return false;

        element.RemoveNodes();
        return true;
    }

    public static bool RemoveChildElements(XElement element)
    {
        if (!element.HasElements)
            return false;

        element.Elements().Remove();
        return true;
    }

    public static bool RemoveChildElementsExcept(XElement element, XName allowedChildName)
    {
        var changed = false;
        foreach (var child in element.Elements().Where(child => child.Name != allowedChildName).ToList())
        {
            child.Remove();
            changed = true;
        }

        return changed;
    }

    public static bool RemoveChildElementsExcept(
        XElement element,
        XNamespace allowedNamespace,
        IReadOnlySet<string> allowedLocalNames)
    {
        var changed = false;
        foreach (var child in element.Elements().ToList())
        {
            if (child.Name.Namespace == allowedNamespace && allowedLocalNames.Contains(child.Name.LocalName))
                continue;

            child.Remove();
            changed = true;
        }

        return changed;
    }

    public static bool NormalizeChildOrder(XElement element, Func<XElement, int> orderSelector)
    {
        var children = element.Elements()
            .Select((child, index) => new { Child = child, Index = index })
            .OrderBy(item => orderSelector(item.Child))
            .ThenBy(item => item.Index)
            .Select(item => item.Child)
            .ToList();
        if (children.Count == 0 || element.Elements().SequenceEqual(children))
            return false;

        element.ReplaceNodes(children);
        return true;
    }

    public static bool NormalizeAttribute(
        XElement element,
        string attributeName,
        Func<string?, string?> normalize)
    {
        return NormalizeAttribute(element, XName.Get(attributeName), normalize);
    }

    public static bool NormalizeAttribute(
        XElement element,
        XName attributeName,
        Func<string?, string?> normalize)
    {
        var attribute = element.Attribute(attributeName);
        var normalized = normalize(attribute?.Value);
        if (normalized is null)
        {
            if (attribute is null)
                return false;

            attribute.Remove();
            return true;
        }

        return SetAttributeIfChanged(element, attributeName, normalized);
    }

    public static bool SetAttributeIfChanged(XElement element, string attributeName, string value) =>
        SetAttributeIfChanged(element, XName.Get(attributeName), value);

    public static bool SetAttributeIfChanged(XElement element, XName attributeName, string value)
    {
        var attribute = element.Attribute(attributeName);
        if (attribute is not null && string.Equals(attribute.Value, value, StringComparison.Ordinal))
            return false;

        element.SetAttributeValue(attributeName, value);
        return true;
    }

    /// <summary>
    /// Sets the attribute to <paramref name="value"/> if different, or removes it when <paramref name="value"/> is null.
    /// Returns true if the element was modified.
    /// </summary>
    public static bool SetOrRemoveAttributeIfChanged(XElement element, XName attributeName, string? value)
    {
        if (value is null)
            return RemoveAttributeIfPresent(element, attributeName);

        return SetAttributeIfChanged(element, attributeName, value);
    }

    /// <summary>
    /// Removes the attribute if present. Returns true if the element was modified.
    /// </summary>
    public static bool RemoveAttributeIfPresent(XElement element, XName attributeName)
    {
        if (element.Attribute(attributeName) is null)
            return false;

        element.SetAttributeValue(attributeName, null);
        return true;
    }

    /// <summary>
    /// Removes the attribute (no-namespace) if present. Returns true if the element was modified.
    /// </summary>
    public static bool RemoveAttributeIfPresent(XElement element, string attributeName) =>
        RemoveAttributeIfPresent(element, XName.Get(attributeName));

    public static string? NormalizeBoolean(string? value)
    {
        var trimmed = value?.Trim();
        return trimmed switch
        {
            "0" or "1" => trimmed,
            "true" or "false" => trimmed,
            _ => null
        };
    }

    public static string? NormalizeBooleanAsNumeric(string? value)
    {
        var trimmed = value?.Trim();
        if (string.Equals(trimmed, "1", StringComparison.Ordinal) ||
            string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase))
        {
            return "1";
        }

        if (string.Equals(trimmed, "0", StringComparison.Ordinal) ||
            string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase))
        {
            return "0";
        }

        return null;
    }

    public static string? NormalizeRequiredUnsignedInt(string? value) =>
        NormalizeUnsignedIntOrNull(value) ?? "0";

    public static string? NormalizeUnsignedIntOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return uint.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    public static string? NormalizeToken(string? value, IReadOnlySet<string> allowedValues)
    {
        var trimmed = value?.Trim();
        return trimmed is not null && allowedValues.Contains(trimmed) ? trimmed : null;
    }

    public static string? NormalizeOptionalText(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    public static string? NormalizeBase64BinaryOrNull(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        try
        {
            _ = Convert.FromBase64String(trimmed);
            return trimmed;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static string? NormalizeLegacyPasswordHashOrNull(string? value)
    {
        var trimmed = value?.Trim();
        if (trimmed is not { Length: 4 } ||
            !trimmed.All(static c => char.IsAsciiHexDigit(c)))
        {
            return null;
        }

        return trimmed.ToUpperInvariant();
    }
}
