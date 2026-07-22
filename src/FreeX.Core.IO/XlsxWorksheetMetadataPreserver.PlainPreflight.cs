using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static partial class XlsxWorksheetMetadataPreserver
{
    private static readonly HashSet<string> RetainedWorksheetChildLocalNames = new(StringComparer.Ordinal)
    {
        "customSheetViews",
        "scenarios",
        "ignoredErrors",
        "cellWatches",
        "sheetCalcPr",
        "phoneticPr",
        "sortState",
        "dataConsolidate",
        "legacyDrawing",
        "legacyDrawingHF",
        "picture",
        "customProperties",
        "smartTags",
        "singleXmlCells",
        "autoFilter",
        "protectedRanges",
        "rowBreaks",
        "colBreaks",
        "webPublishItems",
        "oleObjects",
        "controls"
    };

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

    // "style" and "bestFit" are intentionally NOT listed here: FreeX has no model field for either
    // (Sheet only tracks ColumnWidths/HiddenCols/ColOutlineLevels), so they must be treated as
    // native-only preservable attributes - otherwise a column whose only native attribute is style or
    // bestFit is misclassified as fully modeled, preservation is skipped for that sheet, and the
    // attribute is silently dropped on save. See MergeWorksheetColumnAttributes, which already copies
    // any unmodeled source column attribute (including style/bestFit) onto the rebuilt <col>.
    private static readonly HashSet<string> ModeledColumnAttributes = new(StringComparer.Ordinal)
    {
        "min",
        "max",
        "width",
        "hidden",
        "outlineLevel",
        "collapsed",
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

    internal static bool HasPreservableSourceWorksheetMetadata(
        ZipArchiveEntry sourceWorksheetEntry,
        XNamespace workbookNs)
    {
        try
        {
            using var stream = sourceWorksheetEntry.Open();
            using var reader = XmlReader.Create(stream, CreateWorksheetPreflightReaderSettings());

            var worksheetDepth = -1;
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                    continue;

                if (worksheetDepth < 0)
                {
                    if (reader.LocalName == "worksheet" &&
                        string.Equals(reader.NamespaceURI, workbookNs.NamespaceName, StringComparison.Ordinal))
                    {
                        worksheetDepth = reader.Depth;
                    }

                    continue;
                }

                if (reader.Depth != worksheetDepth + 1 ||
                    !string.Equals(reader.NamespaceURI, workbookNs.NamespaceName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (RetainedWorksheetChildLocalNames.Contains(reader.LocalName) ||
                    reader.LocalName is "mergeCells" or "sheetProtection" or "hyperlinks" or "extLst")
                {
                    return true;
                }

                switch (reader.LocalName)
                {
                    case "sheetPr":
                        if (HasPreservableSheetPropertiesMetadata(reader, workbookNs))
                            return true;
                        break;
                    case "sheetFormatPr":
                        if (HasNativeOnlyElementMetadataByLocalName(reader, ModeledSheetFormatAttributes))
                            return true;
                        break;
                    case "dimension":
                        if (HasNativeOnlyElementMetadata(reader, ModeledDimensionAttributes))
                            return true;
                        break;
                    case "printOptions":
                        if (HasNativeOnlyElementMetadata(reader, ModeledPrintOptionsAttributes))
                            return true;
                        break;
                    case "pageMargins":
                        if (HasNativeOnlyElementMetadataByLocalName(reader, ModeledPageMarginsPreflightAttributes))
                            return true;
                        break;
                    case "pageSetup":
                        if (HasNativeOnlyElementMetadata(reader, ModeledPageSetupAttributes))
                            return true;
                        break;
                    case "headerFooter":
                        if (HasNativeOnlyElementMetadata(reader, ModeledHeaderFooterAttributes))
                            return true;
                        break;
                    case "cols":
                        if (HasPreservableColumnMetadata(reader, workbookNs))
                            return true;
                        break;
                    case "sheetData":
                        if (HasPreservableSheetDataMetadata(reader, workbookNs))
                            return true;
                        break;
                    case "sheetViews":
                        if (HasPreservableSheetViewMetadata(reader, workbookNs))
                            return true;
                        break;
                }
            }

            return false;
        }
        catch
        {
            return true;
        }
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

    private static bool HasPreservableSheetPropertiesMetadata(XmlReader reader, XNamespace workbookNs)
    {
        if (HasNativeOnlyLocalAttributes(reader, ModeledSheetPropertiesAttributes))
            return true;

        if (reader.IsEmptyElement)
            return false;

        using var subtree = reader.ReadSubtree();
        while (subtree.Read())
        {
            if (subtree.NodeType != XmlNodeType.Element ||
                subtree.Depth == 0)
            {
                continue;
            }

            if (!string.Equals(subtree.NamespaceURI, workbookNs.NamespaceName, StringComparison.Ordinal) ||
                !ModeledSheetPropertiesElements.Contains(subtree.LocalName))
            {
                return true;
            }
        }

        return false;
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

    private static bool HasNativeOnlyElementMetadata(XmlReader reader, HashSet<string> modeledAttributeNames) =>
        HasNativeOnlyWorksheetAttributes(reader, modeledAttributeNames) ||
        HasChildElements(reader);

    private static bool HasNativeOnlyElementMetadataByLocalName(XmlReader reader, HashSet<string> modeledAttributeNames) =>
        HasNativeOnlyLocalAttributes(reader, modeledAttributeNames) ||
        HasChildElements(reader);

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

    private static bool HasPreservableColumnMetadata(XmlReader reader, XNamespace workbookNs)
    {
        if (HasAnyNonNamespaceAttribute(reader))
            return true;
        if (reader.IsEmptyElement)
            return false;

        using var subtree = reader.ReadSubtree();
        while (subtree.Read())
        {
            if (subtree.NodeType != XmlNodeType.Element ||
                subtree.Depth == 0)
            {
                continue;
            }

            if (!string.Equals(subtree.NamespaceURI, workbookNs.NamespaceName, StringComparison.Ordinal) ||
                subtree.LocalName != "col")
            {
                return true;
            }

            if (HasNativeOnlyLocalAttributes(subtree, ModeledColumnAttributes) ||
                HasChildElements(subtree))
            {
                return true;
            }
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

    private static bool HasPreservableSheetDataMetadata(XmlReader reader, XNamespace workbookNs)
    {
        if (HasAnyNonNamespaceAttribute(reader))
            return true;
        if (reader.IsEmptyElement)
            return false;

        using var subtree = reader.ReadSubtree();
        var sheetDataDepth = -1;
        var rowDepth = -1;
        var cellDepth = -1;
        var inlineStringDepth = -1;
        var currentCellIsInlineString = false;

        while (subtree.Read())
        {
            if (subtree.NodeType == XmlNodeType.EndElement)
            {
                if (subtree.Depth == inlineStringDepth)
                    inlineStringDepth = -1;
                if (subtree.Depth == cellDepth)
                {
                    cellDepth = -1;
                    currentCellIsInlineString = false;
                }
                if (subtree.Depth == rowDepth)
                    rowDepth = -1;
                continue;
            }

            if (subtree.NodeType != XmlNodeType.Element)
                continue;

            if (sheetDataDepth < 0)
            {
                sheetDataDepth = subtree.Depth;
                continue;
            }

            if (!string.Equals(subtree.NamespaceURI, workbookNs.NamespaceName, StringComparison.Ordinal))
            {
                return true;
            }

            if (subtree.Depth == sheetDataDepth + 1)
            {
                if (subtree.LocalName != "row")
                    return true;
                if (HasNativeOnlyLocalAttributes(subtree, ModeledRowAttributes))
                    return true;
                rowDepth = subtree.Depth;
                if (subtree.IsEmptyElement)
                    rowDepth = -1;
                continue;
            }

            if (rowDepth >= 0 && subtree.Depth == rowDepth + 1)
            {
                if (subtree.LocalName == "extLst")
                    return true;
                if (subtree.LocalName != "c")
                    return true;
                if (HasPreservableCellAttributes(subtree))
                    return true;

                currentCellIsInlineString = string.Equals(
                    subtree.GetAttribute("t"),
                    "inlineStr",
                    StringComparison.OrdinalIgnoreCase);
                cellDepth = subtree.Depth;
                if (subtree.IsEmptyElement)
                {
                    cellDepth = -1;
                    currentCellIsInlineString = false;
                }

                continue;
            }

            if (cellDepth >= 0 && subtree.Depth == cellDepth + 1)
            {
                if (subtree.LocalName == "extLst")
                    return true;
                if (subtree.LocalName == "f")
                {
                    if (HasAnyNonNamespaceAttribute(subtree))
                        return true;
                    continue;
                }

                if (subtree.LocalName == "is")
                {
                    inlineStringDepth = subtree.Depth;
                    if (currentCellIsInlineString && HasAnyNonNamespaceAttribute(subtree))
                        return true;
                    continue;
                }

                if (subtree.LocalName != "v")
                    return true;

                continue;
            }

            if (inlineStringDepth >= 0 && subtree.Depth == inlineStringDepth + 1)
            {
                if (currentCellIsInlineString &&
                    subtree.LocalName is "r" or "rPh" or "phoneticPr")
                {
                    return true;
                }
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

    private static bool HasPreservableSheetViewMetadata(XmlReader reader, XNamespace workbookNs)
    {
        if (HasAnyNonNamespaceAttribute(reader))
            return true;
        if (reader.IsEmptyElement)
            return false;

        using var subtree = reader.ReadSubtree();
        var sheetViewsDepth = -1;
        var sheetViewDepth = -1;
        while (subtree.Read())
        {
            if (subtree.NodeType == XmlNodeType.EndElement)
            {
                if (subtree.Depth == sheetViewDepth)
                    sheetViewDepth = -1;
                continue;
            }

            if (subtree.NodeType != XmlNodeType.Element)
                continue;

            if (sheetViewsDepth < 0)
            {
                sheetViewsDepth = subtree.Depth;
                continue;
            }

            if (!string.Equals(subtree.NamespaceURI, workbookNs.NamespaceName, StringComparison.Ordinal))
                return true;

            if (subtree.Depth == sheetViewsDepth + 1)
            {
                if (subtree.LocalName != "sheetView")
                    return true;
                if (HasNativeOnlyLocalAttributes(subtree, ModeledSheetViewAttributes))
                    return true;
                sheetViewDepth = subtree.Depth;
                if (subtree.IsEmptyElement)
                    sheetViewDepth = -1;
                continue;
            }

            if (sheetViewDepth >= 0 && subtree.Depth == sheetViewDepth + 1)
            {
                if (subtree.LocalName == "pane")
                {
                    if (HasNativeOnlyLocalAttributes(subtree, ModeledPaneAttributes) ||
                        HasChildElements(subtree))
                    {
                        return true;
                    }

                    continue;
                }

                if (subtree.LocalName == "selection")
                {
                    if (HasNativeOnlyLocalAttributes(subtree, ModeledSelectionAttributes) ||
                        HasChildElements(subtree))
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

    private static XmlReaderSettings CreateWorksheetPreflightReaderSettings() =>
        new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true
        };

    private static bool HasNativeOnlyWorksheetAttributes(XmlReader reader, HashSet<string> modeledAttributeNames)
    {
        if (!reader.HasAttributes)
            return false;

        for (var i = 0; i < reader.AttributeCount; i++)
        {
            reader.MoveToAttribute(i);
            if (!IsNamespaceDeclaration(reader) &&
                (reader.NamespaceURI.Length != 0 || !modeledAttributeNames.Contains(reader.LocalName)))
            {
                reader.MoveToElement();
                return true;
            }
        }

        reader.MoveToElement();
        return false;
    }

    private static bool HasNativeOnlyLocalAttributes(XmlReader reader, HashSet<string> modeledAttributeNames)
    {
        if (!reader.HasAttributes)
            return false;

        for (var i = 0; i < reader.AttributeCount; i++)
        {
            reader.MoveToAttribute(i);
            if (!IsNamespaceDeclaration(reader) && !modeledAttributeNames.Contains(reader.LocalName))
            {
                reader.MoveToElement();
                return true;
            }
        }

        reader.MoveToElement();
        return false;
    }

    private static bool HasAnyNonNamespaceAttribute(XmlReader reader)
    {
        if (!reader.HasAttributes)
            return false;

        for (var i = 0; i < reader.AttributeCount; i++)
        {
            reader.MoveToAttribute(i);
            if (!IsNamespaceDeclaration(reader))
            {
                reader.MoveToElement();
                return true;
            }
        }

        reader.MoveToElement();
        return false;
    }

    private static bool HasChildElements(XmlReader reader)
    {
        if (reader.IsEmptyElement)
            return false;

        using var subtree = reader.ReadSubtree();
        while (subtree.Read())
        {
            if (subtree.NodeType == XmlNodeType.Element && subtree.Depth > 0)
                return true;
        }

        return false;
    }

    private static bool HasPreservableCellAttributes(XmlReader reader)
    {
        if (!reader.HasAttributes)
            return false;

        for (var i = 0; i < reader.AttributeCount; i++)
        {
            reader.MoveToAttribute(i);
            if (!IsNamespaceDeclaration(reader) &&
                (reader.NamespaceURI.Length != 0 ||
                 reader.LocalName is not ("r" or "s" or "t")))
            {
                reader.MoveToElement();
                return true;
            }
        }

        reader.MoveToElement();
        return false;
    }

    private static bool IsNamespaceDeclaration(XmlReader reader) =>
        reader.Prefix == "xmlns" ||
        (reader.Prefix.Length == 0 && reader.LocalName == "xmlns");
}
