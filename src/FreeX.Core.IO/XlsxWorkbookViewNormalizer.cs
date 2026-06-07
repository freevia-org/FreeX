using System.Globalization;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorkbookViewNormalizer
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly HashSet<string> NoAttributes = [];

    private static readonly HashSet<string> WorkbookViewAttributes =
    [
        "visibility",
        "minimized",
        "showHorizontalScroll",
        "showVerticalScroll",
        "showSheetTabs",
        "xWindow",
        "yWindow",
        "windowWidth",
        "windowHeight",
        "tabRatio",
        "firstSheet",
        "activeTab",
        "autoFilterDateGrouping"
    ];

    private static readonly HashSet<string> ValidVisibilityValues =
    [
        "visible",
        "hidden",
        "veryHidden"
    ];

    private static readonly string[] BooleanAttributes =
    [
        "minimized",
        "showHorizontalScroll",
        "showVerticalScroll",
        "showSheetTabs",
        "autoFilterDateGrouping"
    ];

    private static readonly string[] IntAttributes =
    [
        "xWindow",
        "yWindow"
    ];

    private static readonly string[] UnsignedIntAttributes =
    [
        "windowWidth",
        "windowHeight",
        "tabRatio",
        "firstSheet",
        "activeTab"
    ];

    public static bool NormalizeBookViewsElement(XElement bookViews)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(bookViews, NoAttributes);
        changed |= RemoveUnexpectedChildElements(bookViews, WorkbookNs + "workbookView");

        foreach (var workbookView in bookViews.Elements(WorkbookNs + "workbookView"))
            changed |= NormalizeWorkbookViewElement(workbookView);

        return changed;
    }

    public static bool NormalizeWorkbookViewElement(XElement workbookView)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(workbookView, WorkbookViewAttributes);
        changed |= RemoveUnexpectedChildElements(workbookView, WorkbookNs + "extLst");
        changed |= NormalizeExtensionLists(workbookView);
        changed |= NormalizeAttribute(workbookView, "visibility", value => NormalizeToken(value, ValidVisibilityValues));

        foreach (var attributeName in BooleanAttributes)
            changed |= NormalizeAttribute(workbookView, attributeName, NormalizeBoolean);
        foreach (var attributeName in IntAttributes)
            changed |= NormalizeAttribute(workbookView, attributeName, NormalizeIntOrNull);
        foreach (var attributeName in UnsignedIntAttributes)
            changed |= NormalizeAttribute(workbookView, attributeName, NormalizeUnsignedIntOrNull);

        return changed;
    }

    private static bool NormalizeExtensionLists(XElement workbookView)
    {
        var changed = false;
        var keptExtensionList = false;
        foreach (var extensionList in workbookView.Elements(WorkbookNs + "extLst").ToList())
        {
            if (keptExtensionList)
            {
                extensionList.Remove();
                changed = true;
                continue;
            }

            changed |= XlsxWorkbookExtensionListNormalizer.NormalizeExtensionListElement(extensionList);
            if (XlsxWorkbookExtensionListNormalizer.ShouldRemoveExtensionListElement(extensionList))
            {
                extensionList.Remove();
                changed = true;
                continue;
            }

            keptExtensionList = true;
        }

        return changed;
    }

    private static bool RemoveUnknownAttributes(XElement element, IReadOnlySet<string> allowedAttributes)
    {
        var changed = false;
        foreach (var attribute in element.Attributes().ToList())
        {
            if (attribute.IsNamespaceDeclaration ||
                (attribute.Name.NamespaceName.Length == 0 && allowedAttributes.Contains(attribute.Name.LocalName)))
            {
                continue;
            }

            attribute.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool RemoveUnexpectedChildElements(XElement element, XName allowedChildName)
    {
        var changed = false;
        foreach (var child in element.Elements().Where(child => child.Name != allowedChildName).ToList())
        {
            child.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool NormalizeAttribute(
        XElement element,
        string attributeName,
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

        if (attribute is not null && string.Equals(attribute.Value, normalized, StringComparison.Ordinal))
            return false;

        element.SetAttributeValue(attributeName, normalized);
        return true;
    }

    private static string? NormalizeBoolean(string? value)
    {
        var trimmed = value?.Trim();
        return trimmed switch
        {
            "0" or "1" => trimmed,
            "true" or "false" => trimmed,
            _ => null
        };
    }

    private static string? NormalizeIntOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return int.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static string? NormalizeToken(string? value, IReadOnlySet<string> allowedValues)
    {
        var trimmed = value?.Trim();
        return trimmed is not null && allowedValues.Contains(trimmed) ? trimmed : null;
    }

    private static string? NormalizeUnsignedIntOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return uint.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;
    }
}
