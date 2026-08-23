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
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(bookViews, NoAttributes);
        changed |= XlsxXmlNormalizationHelpers.RemoveChildElementsExcept(bookViews, WorkbookNs + "workbookView");

        foreach (var workbookView in bookViews.Elements(WorkbookNs + "workbookView"))
            changed |= NormalizeWorkbookViewElement(workbookView);

        return changed;
    }

    public static bool NormalizeWorkbookViewElement(XElement workbookView)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(workbookView, WorkbookViewAttributes);
        changed |= XlsxXmlNormalizationHelpers.RemoveChildElementsExcept(workbookView, WorkbookNs + "extLst");
        changed |= XlsxWorkbookExtensionListNormalizer.NormalizeParent(workbookView);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(workbookView, "visibility", value => XlsxXmlNormalizationHelpers.NormalizeToken(value, ValidVisibilityValues));

        foreach (var attributeName in BooleanAttributes)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(workbookView, attributeName, XlsxXmlNormalizationHelpers.NormalizeBoolean);
        foreach (var attributeName in IntAttributes)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(workbookView, attributeName, NormalizeIntOrNull);
        foreach (var attributeName in UnsignedIntAttributes)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(workbookView, attributeName, XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull);

        return changed;
    }

    private static string? NormalizeIntOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return int.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;
    }

}
