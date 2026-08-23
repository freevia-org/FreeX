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
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(customWorkbookViews, NoAttributes);
        changed |= XlsxXmlNormalizationHelpers.RemoveChildElementsExcept(customWorkbookViews, WorkbookNs + "customWorkbookView");

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
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(customWorkbookView, CustomWorkbookViewAttributes);
        changed |= XlsxXmlNormalizationHelpers.RemoveChildElementsExcept(customWorkbookView, WorkbookNs + "extLst");
        changed |= XlsxWorkbookExtensionListNormalizer.NormalizeParent(customWorkbookView);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(customWorkbookView, "name", XlsxXmlNormalizationHelpers.NormalizeOptionalText);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(customWorkbookView, "guid", NormalizeGuid);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(customWorkbookView, "showComments", value => XlsxXmlNormalizationHelpers.NormalizeToken(value, ShowCommentsValues));
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(customWorkbookView, "showObjects", value => XlsxXmlNormalizationHelpers.NormalizeToken(value, ShowObjectsValues));
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(customWorkbookView, "activeSheetId", value => NormalizeUnsignedIntOrDefault(value, "1"));

        foreach (var attributeName in BooleanAttributes)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(customWorkbookView, attributeName, XlsxXmlNormalizationHelpers.NormalizeBoolean);
        foreach (var attributeName in IntAttributes)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(customWorkbookView, attributeName, NormalizeIntOrNull);
        foreach (var attributeName in UnsignedIntAttributes)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(customWorkbookView, attributeName, XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull);

        return changed;
    }

    private static bool ShouldRemoveCustomWorkbookViewElement(XElement customWorkbookView) =>
        string.IsNullOrWhiteSpace(customWorkbookView.Attribute("name")?.Value) ||
        string.IsNullOrWhiteSpace(customWorkbookView.Attribute("guid")?.Value);

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

    private static string NormalizeUnsignedIntOrDefault(string? value, string defaultValue)
    {
        var trimmed = value?.Trim();
        return uint.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : defaultValue;
    }
}
