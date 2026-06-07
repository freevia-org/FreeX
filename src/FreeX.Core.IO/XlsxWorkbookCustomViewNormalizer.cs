using System.Globalization;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorkbookCustomViewNormalizer
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly HashSet<string> NoAttributes = [];

    private static readonly HashSet<string> CustomWorkbookViewAttributes =
    [
        "name",
        "guid",
        "autoUpdate",
        "mergeInterval",
        "changesSavedWin",
        "onlySync",
        "personalView",
        "includePrintSettings",
        "includeHiddenRowCol",
        "maximized",
        "minimized",
        "showHorizontalScroll",
        "showVerticalScroll",
        "showSheetTabs",
        "xWindow",
        "yWindow",
        "windowWidth",
        "windowHeight",
        "tabRatio",
        "activeSheetId",
        "showFormulaBar",
        "showStatusbar",
        "showComments",
        "showObjects"
    ];

    private static readonly string[] BooleanAttributes =
    [
        "autoUpdate",
        "changesSavedWin",
        "onlySync",
        "personalView",
        "includePrintSettings",
        "includeHiddenRowCol",
        "maximized",
        "minimized",
        "showHorizontalScroll",
        "showVerticalScroll",
        "showSheetTabs",
        "showFormulaBar",
        "showStatusbar"
    ];

    private static readonly string[] IntAttributes =
    [
        "xWindow",
        "yWindow"
    ];

    private static readonly string[] UnsignedIntAttributes =
    [
        "mergeInterval",
        "windowWidth",
        "windowHeight",
        "tabRatio"
    ];

    private static readonly HashSet<string> ShowCommentsValues =
    [
        "commNone",
        "commIndicator",
        "commIndAndComment"
    ];

    private static readonly HashSet<string> ShowObjectsValues =
    [
        "all",
        "placeholders",
        "none"
    ];

    public static bool NormalizeCustomWorkbookViewsElement(XElement customWorkbookViews)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(customWorkbookViews, NoAttributes);
        changed |= RemoveUnexpectedChildElements(customWorkbookViews, WorkbookNs + "customWorkbookView");

        foreach (var customWorkbookView in customWorkbookViews.Elements(WorkbookNs + "customWorkbookView").ToList())
        {
            changed |= NormalizeCustomWorkbookViewElement(customWorkbookView);
            if (!ShouldRemoveCustomWorkbookViewElement(customWorkbookView))
                continue;

            customWorkbookView.Remove();
            changed = true;
        }

        return changed;
    }

    public static bool ShouldRemoveCustomWorkbookViewsElement(XElement customWorkbookViews) =>
        !customWorkbookViews.Elements(WorkbookNs + "customWorkbookView").Any();

    public static bool NormalizeCustomWorkbookViewElement(XElement customWorkbookView)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(customWorkbookView, CustomWorkbookViewAttributes);
        changed |= RemoveUnexpectedChildElements(customWorkbookView, WorkbookNs + "extLst");
        changed |= NormalizeExtensionLists(customWorkbookView);
        changed |= NormalizeAttribute(customWorkbookView, "name", NormalizeOptionalText);
        changed |= NormalizeAttribute(customWorkbookView, "guid", NormalizeGuid);
        changed |= NormalizeAttribute(customWorkbookView, "showComments", value => NormalizeToken(value, ShowCommentsValues));
        changed |= NormalizeAttribute(customWorkbookView, "showObjects", value => NormalizeToken(value, ShowObjectsValues));
        changed |= NormalizeAttribute(customWorkbookView, "activeSheetId", value => NormalizeUnsignedIntOrDefault(value, "1"));

        foreach (var attributeName in BooleanAttributes)
            changed |= NormalizeAttribute(customWorkbookView, attributeName, NormalizeBoolean);
        foreach (var attributeName in IntAttributes)
            changed |= NormalizeAttribute(customWorkbookView, attributeName, NormalizeIntOrNull);
        foreach (var attributeName in UnsignedIntAttributes)
            changed |= NormalizeAttribute(customWorkbookView, attributeName, NormalizeUnsignedIntOrNull);

        return changed;
    }

    private static bool ShouldRemoveCustomWorkbookViewElement(XElement customWorkbookView) =>
        string.IsNullOrWhiteSpace(customWorkbookView.Attribute("name")?.Value) ||
        string.IsNullOrWhiteSpace(customWorkbookView.Attribute("guid")?.Value);

    private static bool NormalizeExtensionLists(XElement customWorkbookView)
    {
        var changed = false;
        var keptExtensionList = false;
        foreach (var extensionList in customWorkbookView.Elements(WorkbookNs + "extLst").ToList())
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

    private static string? NormalizeGuid(string? value)
    {
        var trimmed = value?.Trim();
        return Guid.TryParse(trimmed?.Trim('{', '}'), out var guid)
            ? $"{{{guid:D}}}".ToUpperInvariant()
            : null;
    }

    private static string? NormalizeIntOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return int.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static string? NormalizeOptionalText(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
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

    private static string NormalizeUnsignedIntOrDefault(string? value, string defaultValue)
    {
        var trimmed = value?.Trim();
        return uint.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : defaultValue;
    }
}
