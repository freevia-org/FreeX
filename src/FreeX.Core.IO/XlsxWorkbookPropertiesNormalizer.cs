using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorkbookPropertiesNormalizer
{
    private static readonly HashSet<string> WorkbookPropertyAttributes =
    [
        "date1904",
        "showObjects",
        "showBorderUnselectedTables",
        "filterPrivacy",
        "promptedSolutions",
        "showInkAnnotation",
        "backupFile",
        "saveExternalLinkValues",
        "updateLinks",
        "codeName",
        "hidePivotFieldList",
        "showPivotChartFilter",
        "allowRefreshQuery",
        "publishItems",
        "checkCompatibility",
        "autoCompressPictures",
        "refreshAllConnections",
        "defaultThemeVersion"
    ];

    private static readonly string[] BooleanAttributes =
    [
        "date1904",
        "showBorderUnselectedTables",
        "filterPrivacy",
        "promptedSolutions",
        "showInkAnnotation",
        "backupFile",
        "saveExternalLinkValues",
        "hidePivotFieldList",
        "showPivotChartFilter",
        "allowRefreshQuery",
        "publishItems",
        "checkCompatibility",
        "autoCompressPictures",
        "refreshAllConnections"
    ];

    private static readonly HashSet<string> ShowObjectsValues =
    [
        "all",
        "placeholders",
        "none"
    ];

    private static readonly HashSet<string> UpdateLinksValues =
    [
        "userSet",
        "never",
        "always"
    ];

    public static bool NormalizeElement(XElement workbookPr)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(workbookPr, WorkbookPropertyAttributes);
        changed |= XlsxXmlNormalizationHelpers.RemoveAllNodes(workbookPr);

        foreach (var attributeName in BooleanAttributes)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(workbookPr, attributeName, XlsxXmlNormalizationHelpers.NormalizeBoolean);

        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(workbookPr, "showObjects", value => XlsxXmlNormalizationHelpers.NormalizeToken(value, ShowObjectsValues));
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(workbookPr, "updateLinks", value => XlsxXmlNormalizationHelpers.NormalizeToken(value, UpdateLinksValues));
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(workbookPr, "defaultThemeVersion", XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(workbookPr, "codeName", NormalizeOptionalText);
        return changed;
    }

    private static string? NormalizeOptionalText(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
