using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetLayoutMetadataReader
{
    public static NativeXmlPreserveBag? ReadWorksheetDimensionMetadata(XElement? dimension)
        => ReadMetadata(
            dimension,
            "dimension",
            attribute => !string.Equals(attribute.Name.LocalName, "ref", StringComparison.Ordinal));

    public static NativeXmlPreserveBag? ReadWorksheetSheetPropertiesMetadata(XElement? sheetProperties)
        => ReadMetadata(sheetProperties, "sheetPr", IsPreservableSheetPropertiesAttribute);

    private static NativeXmlPreserveBag? ReadMetadata(
        XElement? element,
        string bagName,
        Func<XAttribute, bool> shouldPreserveAttribute,
        Func<XElement, bool>? shouldPreserveChild = null)
    {
        if (element is null)
            return null;

        var attrs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || !shouldPreserveAttribute(attribute))
                continue;

            attrs[attribute.Name.ToString()] = attribute.Value;
        }

        var children = shouldPreserveChild is null
            ? null
            : element.Elements()
                .Where(shouldPreserveChild)
                .Select(child => child.ToString(SaveOptions.DisableFormatting))
                .ToList();

        var serialized = XmlNativeBagSerializer.Serialize(attrs, children);
        if (serialized is null)
            return null;

        var bag = new NativeXmlPreserveBag();
        bag.Set(bagName, serialized);
        return bag;
    }

    private static bool IsModeledSheetPropertiesAttribute(string name) =>
        name is "codeName";

    private static bool IsPreservableSheetPropertiesAttribute(XAttribute attribute) =>
        attribute.Name.NamespaceName.Length == 0 &&
        !IsModeledSheetPropertiesAttribute(attribute.Name.LocalName) &&
        attribute.Name.LocalName is "syncHorizontal" or
            "syncVertical" or
            "syncRef" or
            "transitionEvaluation" or
            "transitionEntry" or
            "published" or
            "filterMode" or
            "enableFormatConditionsCalculation";

    public static NativeXmlPreserveBag? ReadWorksheetPrimaryViewMetadata(XElement? sheetView)
        => ReadMetadata(
            sheetView,
            "sheetView",
            attribute => !IsModeledPrimaryViewAttribute(attribute.Name.LocalName),
            element => !IsModeledPrimaryViewElement(element.Name.LocalName));

    private static bool IsModeledPrimaryViewAttribute(string name) =>
        name is "workbookViewId" or "view" or "showGridLines" or "showRowColHeaders" or "showRuler" or
            "zoomScale" or "showFormulas" or "topLeftCell";

    private static bool IsModeledPrimaryViewElement(string name) =>
        name is "pane";

    public static NativeXmlPreserveBag? ReadWorksheetHeaderFooterMetadata(XElement? headerFooter)
        => ReadMetadata(
            headerFooter,
            "headerFooter",
            attribute => !IsModeledHeaderFooterAttribute(attribute.Name.LocalName),
            element => !IsModeledHeaderFooterElement(element.Name.LocalName));

    private static bool IsModeledHeaderFooterAttribute(string name) =>
        name is "differentOddEven" or "differentFirst" or "scaleWithDoc" or "alignWithMargins";

    private static bool IsModeledHeaderFooterElement(string name) =>
        name is "oddHeader" or "oddFooter" or "evenHeader" or "evenFooter" or "firstHeader" or "firstFooter";

    public static NativeXmlPreserveBag? ReadWorksheetPageMarginsMetadata(XElement? pageMargins)
        => ReadMetadata(
            pageMargins,
            "pageMargins",
            attribute => !IsModeledPageMarginsAttribute(attribute.Name.LocalName),
            element => true);

    private static bool IsModeledPageMarginsAttribute(string name) =>
        name is "left" or "right" or "top" or "bottom" or "header" or "footer";

    public static NativeXmlPreserveBag? ReadWorksheetSheetFormatMetadata(XElement? sheetFormatProperties)
        => ReadMetadata(
            sheetFormatProperties,
            "sheetFormatPr",
            attribute => !IsModeledSheetFormatAttribute(attribute.Name.LocalName),
            element => true);

    private static bool IsModeledSheetFormatAttribute(string name) =>
        name is "defaultColWidth" or "defaultRowHeight";

    public static NativeXmlPreserveBag? ReadWorksheetPrintOptionsMetadata(XElement? printOptions)
        => ReadMetadata(
            printOptions,
            "printOptions",
            attribute => !IsModeledPrintOptionsAttribute(attribute.Name.LocalName),
            element => true);

    private static bool IsModeledPrintOptionsAttribute(string name) =>
        name is "gridLines" or "headings" or "horizontalCentered" or "verticalCentered";

    public static NativeXmlPreserveBag? ReadWorksheetPageSetupMetadata(XElement? pageSetup)
        => ReadMetadata(
            pageSetup,
            "pageSetup",
            attribute => !IsModeledPageSetupAttribute(attribute.Name.LocalName),
            element => true);

    private static bool IsModeledPageSetupAttribute(string name) =>
        name is "paperSize" or "scale" or "firstPageNumber" or "fitToWidth" or "fitToHeight" or
            "pageOrder" or "orientation" or "usePrinterDefaults" or "blackAndWhite" or "draft" or
            "cellComments" or "useFirstPageNumber" or "errors" or "horizontalDpi" or "verticalDpi" or
            "copies";

    public static NativeXmlPreserveBag? ReadWorksheetProtectionMetadata(XElement? protection)
        => ReadMetadata(protection, "sheetProtection", IsPreservableProtectionAttribute);

    private static bool IsPreservableProtectionAttribute(XAttribute attribute) =>
        attribute.Name.NamespaceName.Length == 0 &&
        attribute.Name.LocalName is not "sheet" and not "password" &&
        attribute.Name.LocalName is "algorithmName" or
            "hashValue" or
            "saltValue" or
            "spinCount" or
            "objects" or
            "scenarios" or
            "formatCells" or
            "formatColumns" or
            "formatRows" or
            "insertColumns" or
            "insertRows" or
            "insertHyperlinks" or
            "deleteColumns" or
            "deleteRows" or
            "selectLockedCells" or
            "sort" or
            "autoFilter" or
            "pivotTables" or
            "selectUnlockedCells";
}
