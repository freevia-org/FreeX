using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static partial class XlsxWorksheetMetadataPreserver
{
    private static readonly HashSet<string> ModeledSheetFormatAttributes = new(StringComparer.Ordinal)
    {
        "defaultRowHeight",
        "defaultColWidth",
        "customHeight",
        "dyDescent"
    };

    private static readonly HashSet<string> ModeledPageMarginsPreflightAttributes = new(StringComparer.Ordinal)
    {
        "left",
        "right",
        "top",
        "bottom",
        "header",
        "footer"
    };

    private static readonly HashSet<string> ModeledSheetViewAttributes = new(StringComparer.Ordinal)
    {
        "workbookViewId",
        "view",
        "showGridLines",
        "showRowColHeaders",
        "showRuler",
        "zoomScale",
        "zoomScaleNormal",
        "zoomScalePageLayoutView",
        "showFormulas",
        "topLeftCell",
        "tabSelected"
    };

    private static readonly HashSet<string> ModeledPaneAttributes = new(StringComparer.Ordinal)
    {
        "xSplit",
        "ySplit",
        "topLeftCell",
        "activePane",
        "state"
    };

    private static readonly HashSet<string> ModeledSelectionAttributes = new(StringComparer.Ordinal)
    {
        "pane",
        "activeCell",
        "activeCellId",
        "sqref"
    };

    private static readonly HashSet<string> ModeledSheetPropertiesAttributes = new(StringComparer.Ordinal)
    {
        "codeName"
    };

    private static readonly HashSet<string> ModeledSheetPropertiesElements = new(StringComparer.Ordinal)
    {
        "tabColor",
        "outlinePr",
        "pageSetUpPr"
    };

    private static readonly HashSet<string> ModeledRowAttributes = new(StringComparer.Ordinal)
    {
        "r",
        "spans",
        "s",
        "customFormat",
        "ht",
        "hidden",
        "outlineLevel",
        "collapsed",
        "thickTop",
        "thickBot",
        "ph",
        "customHeight",
        "dyDescent"
    };

    private static readonly HashSet<string> ModeledColumnAttributes = new(StringComparer.Ordinal)
    {
        "min",
        "max",
        "width",
        "style",
        "hidden",
        "outlineLevel",
        "collapsed",
        "bestFit",
        "customWidth",
        "phonetic"
    };

    private static bool HasPreservableSourceWorksheetMetadata(
        IReadOnlyList<XElement> sourceBlocks,
        XElement? sourceSheetProperties,
        XElement? sourceSheetFormatProperties,
        XElement? sourceDimension,
        XElement? sourcePrintOptions,
        XElement? sourcePageMargins,
        XElement? sourcePageSetup,
        XElement? sourceHeaderFooter,
        XElement? sourceMergeCells,
        XElement? sourceColumns,
        XElement? sourceSheetData,
        XElement? sourceSheetProtection,
        XElement? sourceSheetViews,
        XElement? sourceHyperlinks,
        XElement? sourceExtensionList,
        XNamespace workbookNs)
    {
        if (sourceBlocks.Count > 0 ||
            sourceMergeCells is not null ||
            sourceSheetProtection is not null ||
            sourceHyperlinks is not null ||
            sourceExtensionList is not null)
        {
            return true;
        }

        return
            HasPreservableSheetPropertiesMetadata(sourceSheetProperties) ||
            HasNativeOnlyElementMetadataByLocalName(sourceSheetFormatProperties, ModeledSheetFormatAttributes) ||
            HasNativeOnlyElementMetadata(sourceDimension, ModeledDimensionAttributes) ||
            HasNativeOnlyElementMetadata(sourcePrintOptions, ModeledPrintOptionsAttributes) ||
            HasNativeOnlyElementMetadataByLocalName(sourcePageMargins, ModeledPageMarginsPreflightAttributes) ||
            HasNativeOnlyElementMetadata(sourcePageSetup, ModeledPageSetupAttributes) ||
            HasNativeOnlyElementMetadata(sourceHeaderFooter, ModeledHeaderFooterAttributes) ||
            HasPreservableColumnMetadata(sourceColumns, workbookNs) ||
            HasPreservableSheetDataMetadata(sourceSheetData, workbookNs) ||
            HasPreservableSheetViewMetadata(sourceSheetViews, workbookNs);
    }

    private static bool HasPreservableSheetPropertiesMetadata(XElement? sourceSheetProperties)
    {
        if (sourceSheetProperties is null)
            return false;

        return sourceSheetProperties.Attributes()
                   .Any(attribute => IsNativeOnlyLocalAttribute(attribute, ModeledSheetPropertiesAttributes)) ||
               sourceSheetProperties.Elements()
                   .Any(element => !ModeledSheetPropertiesElements.Contains(element.Name.LocalName));
    }

    private static bool HasNativeOnlyElementMetadata(XElement? sourceElement, HashSet<string> modeledAttributeNames)
    {
        if (sourceElement is null)
            return false;

        return sourceElement.Attributes().Any(attribute => IsNativeOnlyWorksheetAttribute(attribute, modeledAttributeNames)) ||
            sourceElement.Elements().Any();
    }

    private static bool HasNativeOnlyElementMetadataByLocalName(XElement? sourceElement, HashSet<string> modeledAttributeNames)
    {
        if (sourceElement is null)
            return false;

        return sourceElement.Attributes().Any(attribute => IsNativeOnlyLocalAttribute(attribute, modeledAttributeNames)) ||
            sourceElement.Elements().Any();
    }

    private static bool HasPreservableColumnMetadata(XElement? sourceColumns, XNamespace workbookNs)
    {
        if (sourceColumns is null)
            return false;

        if (sourceColumns.Attributes().Any())
            return true;

        foreach (var column in sourceColumns.Elements(workbookNs + "col"))
        {
            if (column.Elements().Any())
                return true;

            if (column.Attributes().Any(attribute => IsNativeOnlyLocalAttribute(attribute, ModeledColumnAttributes)))
                return true;
        }

        return false;
    }

    private static bool HasPreservableSheetDataMetadata(XElement? sourceSheetData, XNamespace workbookNs)
    {
        if (sourceSheetData is null)
            return false;

        if (sourceSheetData.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration))
            return true;

        foreach (var row in sourceSheetData.Elements(workbookNs + "row"))
        {
            if (row.Attributes().Any(attribute => IsNativeOnlyLocalAttribute(attribute, ModeledRowAttributes)))
                return true;

            if (row.Element(workbookNs + "extLst") is not null ||
                row.Elements().Any(child => child.Name != workbookNs + "c" && child.Name != workbookNs + "extLst"))
            {
                return true;
            }

            foreach (var cell in row.Elements(workbookNs + "c"))
            {
                if (GetSourceCellNativeMetadata(cell, workbookNs).HasAny)
                    return true;
            }
        }

        return false;
    }

    private static bool HasPreservableSheetViewMetadata(XElement? sourceSheetViews, XNamespace workbookNs)
    {
        if (sourceSheetViews is null)
            return false;

        if (sourceSheetViews.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration))
            return true;

        foreach (var child in sourceSheetViews.Elements())
        {
            if (child.Name != workbookNs + "sheetView")
                return true;

            if (child.Attributes().Any(attribute => IsNativeOnlyLocalAttribute(attribute, ModeledSheetViewAttributes)))
                return true;

            foreach (var viewChild in child.Elements())
            {
                if (viewChild.Name == workbookNs + "pane")
                {
                    if (viewChild.Attributes().Any(attribute => IsNativeOnlyLocalAttribute(attribute, ModeledPaneAttributes)) ||
                        viewChild.Elements().Any())
                    {
                        return true;
                    }

                    continue;
                }

                if (viewChild.Name == workbookNs + "selection")
                {
                    if (viewChild.Attributes().Any(attribute => IsNativeOnlyLocalAttribute(attribute, ModeledSelectionAttributes)) ||
                        viewChild.Elements().Any())
                    {
                        return true;
                    }

                    continue;
                }

                return true;
            }
        }

        return false;
    }

    private static bool IsNativeOnlyLocalAttribute(XAttribute attribute, HashSet<string> modeledAttributeNames) =>
        !attribute.IsNamespaceDeclaration &&
        !modeledAttributeNames.Contains(attribute.Name.LocalName);
}
