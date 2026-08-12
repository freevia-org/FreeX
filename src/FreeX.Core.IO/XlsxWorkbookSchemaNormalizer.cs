using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorkbookSchemaNormalizer
{
    public static void Normalize(Stream xlsxStream)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        Normalize(archive);
    }

    public static void Normalize(ZipArchive archive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XlsxWorksheetDimensionNormalizer.NormalizeWorksheets(archive);
        XlsxWorksheetCalculationPropertyNormalizer.NormalizeWorksheets(archive);
        XlsxWorksheetSheetFormatNormalizer.NormalizeWorksheets(archive);
        XlsxWorksheetSheetPropertiesNormalizer.NormalizeWorksheets(archive);
        XlsxWorksheetSheetViewNormalizer.NormalizeWorksheets(archive);
        XlsxWorksheetProtectionNormalizer.NormalizeWorksheets(archive);
        XlsxWorksheetProtectedRangeNormalizer.NormalizeWorksheets(archive);
        XlsxWorksheetScenarioNormalizer.NormalizeWorksheets(archive);
        XlsxWorksheetSmartTagNormalizer.NormalizeWorksheets(archive);
        XlsxWorksheetCustomSheetViewExtensionListNormalizer.NormalizeWorksheets(archive);
        XlsxWorksheetPhoneticPropertyNormalizer.NormalizeWorksheets(archive);
        XlsxWorksheetCellWatchesNormalizer.NormalizeWorksheets(archive);
        XlsxWorksheetCustomPropertiesNormalizer.NormalizeWorksheets(archive);
        XlsxWorksheetIgnoredErrorsNormalizer.NormalizeWorksheets(archive);
        XlsxWorksheetHyperlinkNormalizer.NormalizeWorksheets(archive);
        XlsxWorksheetConditionalFormatNormalizer.NormalizeWorksheets(archive);
        XlsxWorksheetAutoFilterNormalizer.NormalizeWorksheets(archive);
        XlsxWorksheetSortStateNormalizer.NormalizeWorksheets(archive);
        XlsxWorksheetDataConsolidationNormalizer.NormalizeWorksheets(archive);
        XlsxWorksheetDataValidationNormalizer.NormalizeWorksheets(archive);
        XlsxWorksheetExtensionListNormalizer.NormalizeWorksheets(archive);
        XlsxWorksheetWebPublishItemsNormalizer.NormalizePackage(archive);
        XlsxWorksheetOleControlNormalizer.NormalizeWorksheets(archive);
        XlsxWorksheetRelationshipMarkerNormalizer.NormalizeWorksheets(archive);
        XlsxWorksheetPageLayoutNormalizer.NormalizeWorksheets(archive);
        XlsxWorksheetPageBreakNormalizer.NormalizeWorksheets(archive);
        XlsxRichTextFontNormalizer.NormalizePackage(archive);
        XlsxDocumentThumbnailPackageGraphNormalizer.NormalizePackage(archive);
        XlsxThemeTypefaceNormalizer.NormalizePackage(archive);
        XlsxLegacyCommentFontNormalizer.NormalizePackage(archive);
        XlsxStructuredTableSchemaNormalizer.NormalizePackage(archive);
        XlsxConnectionQueryTableSchemaNormalizer.NormalizePackage(archive);
        XlsxWorksheetSingleXmlCellMapper.NormalizePackage(archive);
        NormalizeWorksheets(archive, workbookNs);

        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return;

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        if (!NormalizeWorkbook(workbookXml, workbookNs))
            return;

        XlsxPackageXmlEditor.ReplaceXml(archive, "xl/workbook.xml", workbookXml);
    }

    private static void NormalizeWorksheets(ZipArchive archive, XNamespace workbookNs)
    {
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var worksheetPaths = archive.Entries
            .Select(entry => entry.FullName)
            .Where(path => path.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                           path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                           !path.Contains("/_rels/", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var worksheetPath in worksheetPaths)
        {
            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                continue;

            var preflight = InspectWorksheetNormalization(worksheetEntry, workbookNs, relNs);
            if (!preflight.NeedsChildOrderNormalization && !preflight.HasLegacyDrawingHeaderFooter)
                continue;

            string? vmlRelationshipId = null;
            if (preflight.HasLegacyDrawingHeaderFooter)
            {
                vmlRelationshipId = GetVmlDrawingRelationshipId(archive, worksheetPath, packageRelNs);
                if (!preflight.NeedsChildOrderNormalization &&
                    (string.IsNullOrWhiteSpace(vmlRelationshipId) ||
                     string.Equals(
                         preflight.LegacyDrawingHeaderFooterRelationshipId,
                         vmlRelationshipId,
                         StringComparison.Ordinal)))
                {
                    continue;
                }
            }

            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var changed = preflight.NeedsChildOrderNormalization &&
                NormalizeWorksheet(worksheetXml, workbookNs);
            changed |= NormalizeLegacyDrawingHeaderFooterRelationship(
                archive,
                worksheetPath,
                worksheetXml,
                workbookNs,
                relNs,
                packageRelNs,
                vmlRelationshipId);
            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        }
    }

    private const string VmlDrawingRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing";

    private static WorksheetNormalizationPreflight InspectWorksheetNormalization(
        ZipArchiveEntry worksheetEntry,
        XNamespace workbookNs,
        XNamespace relNs)
    {
        try
        {
            using var stream = worksheetEntry.Open();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true
            });

            var worksheetDepth = -1;
            var previousOrder = -1;
            var needsChildOrderNormalization = false;
            var hasLegacyDrawingHeaderFooter = false;
            string? legacyDrawingHeaderFooterRelationshipId = null;

            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                    continue;

                if (worksheetDepth < 0)
                {
                    if (reader.LocalName == "worksheet" &&
                        reader.NamespaceURI == workbookNs.NamespaceName)
                    {
                        worksheetDepth = reader.Depth;
                    }

                    continue;
                }

                if (reader.Depth != worksheetDepth + 1 ||
                    reader.NamespaceURI != workbookNs.NamespaceName)
                {
                    continue;
                }

                var order = WorksheetChildOrder(reader.LocalName, reader.NamespaceURI, workbookNs);
                if (order < previousOrder)
                    needsChildOrderNormalization = true;
                else
                    previousOrder = order;

                if (reader.LocalName == "legacyDrawingHF")
                {
                    hasLegacyDrawingHeaderFooter = true;
                    legacyDrawingHeaderFooterRelationshipId = reader.GetAttribute("id", relNs.NamespaceName);
                }
            }

            return new WorksheetNormalizationPreflight(
                needsChildOrderNormalization,
                hasLegacyDrawingHeaderFooter,
                legacyDrawingHeaderFooterRelationshipId);
        }
        catch
        {
            return new WorksheetNormalizationPreflight(
                NeedsChildOrderNormalization: true,
                HasLegacyDrawingHeaderFooter: false,
                LegacyDrawingHeaderFooterRelationshipId: null);
        }
    }

    private static bool NormalizeLegacyDrawingHeaderFooterRelationship(
        ZipArchive archive,
        string worksheetPath,
        XDocument worksheetXml,
        XNamespace workbookNs,
        XNamespace relNs,
        XNamespace packageRelNs,
        string? vmlRelationshipId = null)
    {
        var legacyDrawingHeaderFooter = worksheetXml.Root?.Element(workbookNs + "legacyDrawingHF");
        if (legacyDrawingHeaderFooter is null)
            return false;

        vmlRelationshipId ??= GetVmlDrawingRelationshipId(archive, worksheetPath, packageRelNs);
        if (string.IsNullOrWhiteSpace(vmlRelationshipId) ||
            string.Equals(legacyDrawingHeaderFooter.Attribute(relNs + "id")?.Value, vmlRelationshipId, StringComparison.Ordinal))
        {
            return false;
        }

        worksheetXml.Root?.SetAttributeValue(XNamespace.Xmlns + "r", relNs.NamespaceName);
        legacyDrawingHeaderFooter.SetAttributeValue(relNs + "id", vmlRelationshipId);
        return true;
    }

    private static string? GetVmlDrawingRelationshipId(
        ZipArchive archive,
        string worksheetPath,
        XNamespace packageRelNs)
    {
        var relsEntry = archive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(worksheetPath));
        if (relsEntry is null)
            return null;

        var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);
        foreach (var relationship in relsXml.Root?.Elements(packageRelNs + "Relationship") ?? [])
        {
            if (!string.Equals(
                    relationship.Attribute("Type")?.Value,
                    VmlDrawingRelationshipType,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var id = relationship.Attribute("Id")?.Value;
            if (!string.IsNullOrWhiteSpace(id))
                return id;
        }

        return null;
    }

    internal static bool NormalizeWorkbook(XDocument workbookXml, XNamespace workbookNs)
    {
        var root = workbookXml.Root;
        if (root is null)
            return false;

        var changed = false;
        if (root.Element(workbookNs + "fileSharing") is { } fileSharing)
            changed |= XlsxWorkbookLeafElementNormalizer.Normalize(fileSharing);
        if (root.Element(workbookNs + "workbookPr") is { } workbookPr)
            changed |= XlsxWorkbookLeafElementNormalizer.Normalize(workbookPr);
        if (root.Element(workbookNs + "workbookProtection") is { } workbookProtection)
            changed |= XlsxWorkbookLeafElementNormalizer.Normalize(workbookProtection);
        if (root.Element(workbookNs + "bookViews") is { } bookViews)
            changed |= XlsxWorkbookViewNormalizer.NormalizeBookViewsElement(bookViews);
        foreach (var customWorkbookViews in root.Elements(workbookNs + "customWorkbookViews").ToList())
        {
            changed |= XlsxWorkbookCustomViewNormalizer.NormalizeCustomWorkbookViewsElement(customWorkbookViews);
            if (XlsxWorkbookCustomViewNormalizer.ShouldRemoveCustomWorkbookViewsElement(customWorkbookViews))
            {
                customWorkbookViews.Remove();
                changed = true;
            }
        }

        // Container schemas: definedNames, externalReferences, functionGroups, webPublishObjects, pivotCaches
        foreach (var containerSchema in XlsxWorkbookContainerElementSchemas.All)
            changed |= XlsxWorkbookContainerElementNormalizer.NormalizeWorkbookRoot(root, containerSchema, workbookNs);

        // oleSize via leaf schema with RequiredAttributes (remove-self-if-invalid)
        if (XlsxWorkbookLeafElementSchemas.ByLocalName.TryGetValue("oleSize", out var oleSizeSchema))
        {
            var keptOleSize = false;
            foreach (var oleSize in root.Elements(workbookNs + "oleSize").ToList())
            {
                if (keptOleSize)
                {
                    oleSize.Remove();
                    changed = true;
                    continue;
                }

                changed |= XlsxWorkbookLeafElementNormalizer.Normalize(oleSize, oleSizeSchema);
                if (XlsxWorkbookLeafElementNormalizer.ShouldRemove(oleSize, oleSizeSchema))
                {
                    oleSize.Remove();
                    changed = true;
                    continue;
                }

                keptOleSize = true;
            }
        }

        changed |= XlsxWorkbookWebPublishingNormalizer.NormalizeWorkbookRoot(root, workbookNs);
        changed |= XlsxWorkbookExtensionListNormalizer.NormalizeWorkbookRoot(root, workbookNs);

        if (root.Element(workbookNs + "fileVersion") is { } fileVersion)
            changed |= XlsxWorkbookLeafElementNormalizer.Normalize(fileVersion);
        if (root.Element(workbookNs + "calcPr") is { } calcPr)
            changed |= XlsxWorkbookLeafElementNormalizer.Normalize(calcPr);
        foreach (var fileRecoveryPr in root.Elements(workbookNs + "fileRecoveryPr"))
            changed |= XlsxWorkbookLeafElementNormalizer.Normalize(fileRecoveryPr);
        // functionGroups is now handled by XlsxWorkbookContainerElementSchemas above.
        if (root.Element(workbookNs + "smartTagPr") is { } smartTagPr)
            changed |= XlsxWorkbookSmartTagNormalizer.NormalizeSmartTagPropertiesElement(smartTagPr);
        if (root.Element(workbookNs + "smartTagTypes") is { } smartTagTypes)
        {
            changed |= XlsxWorkbookSmartTagNormalizer.NormalizeSmartTagTypesElement(smartTagTypes);
            if (XlsxWorkbookSmartTagNormalizer.ShouldRemoveSmartTagTypesElement(smartTagTypes))
            {
                smartTagTypes.Remove();
                changed = true;
            }
        }

        var orderedChildren = root.Elements()
            .Select((element, index) => new { Element = element, Index = index })
            .OrderBy(item => WorkbookChildOrder(item.Element, workbookNs))
            .ThenBy(item => item.Index)
            .Select(item => item.Element)
            .ToList();
        if (orderedChildren.Count == 0 || root.Elements().SequenceEqual(orderedChildren))
            return changed;

        root.ReplaceNodes(orderedChildren);
        return true;
    }

    private static int WorkbookChildOrder(XElement element, XNamespace workbookNs) =>
        element.Name == workbookNs + "revisionPtr" ? 0 :
        element.Name == workbookNs + "fileVersion" ? 1 :
        element.Name == workbookNs + "fileSharing" ? 2 :
        element.Name == workbookNs + "workbookPr" ? 3 :
        element.Name == workbookNs + "workbookProtection" ? 4 :
        element.Name == workbookNs + "bookViews" ? 5 :
        element.Name == workbookNs + "sheets" ? 6 :
        element.Name == workbookNs + "functionGroups" ? 7 :
        element.Name == workbookNs + "externalReferences" ? 8 :
        element.Name == workbookNs + "definedNames" ? 9 :
        element.Name == workbookNs + "calcPr" ? 10 :
        element.Name == workbookNs + "oleSize" ? 11 :
        element.Name == workbookNs + "customWorkbookViews" ? 12 :
        element.Name == workbookNs + "pivotCaches" ? 13 :
        element.Name == workbookNs + "smartTagPr" ? 14 :
        element.Name == workbookNs + "smartTagTypes" ? 15 :
        element.Name == workbookNs + "webPublishing" ? 16 :
        element.Name == workbookNs + "fileRecoveryPr" ? 17 :
        element.Name == workbookNs + "webPublishObjects" ? 18 :
        element.Name == workbookNs + "extLst" ? 100 :
        90;

    internal static bool NormalizeWorksheet(XDocument worksheetXml, XNamespace workbookNs)
    {
        var root = worksheetXml.Root;
        if (root is null)
            return false;

        var orderedChildren = root.Elements()
            .Select((element, index) => new { Element = element, Index = index })
            .OrderBy(item => WorksheetChildOrder(item.Element, workbookNs))
            .ThenBy(item => item.Index)
            .Select(item => item.Element)
            .ToList();
        if (orderedChildren.Count == 0 || root.Elements().SequenceEqual(orderedChildren))
            return false;

        root.ReplaceNodes(orderedChildren);
        return true;
    }

    private static int WorksheetChildOrder(XElement element, XNamespace workbookNs) =>
        WorksheetChildOrder(element.Name.LocalName, element.Name.NamespaceName, workbookNs);

    private static int WorksheetChildOrder(string localName, string namespaceName, XNamespace workbookNs)
    {
        if (!string.Equals(namespaceName, workbookNs.NamespaceName, StringComparison.Ordinal))
            return 90;

        return localName switch
        {
            "sheetPr" => 0,
            "dimension" => 1,
            "sheetViews" => 2,
            "sheetFormatPr" => 3,
            "cols" => 4,
            "sheetData" => 5,
            "sheetCalcPr" => 6,
            "sheetProtection" => 7,
            "protectedRanges" => 8,
            "scenarios" => 9,
            "autoFilter" => 10,
            "sortState" => 11,
            "dataConsolidate" => 12,
            "customSheetViews" => 13,
            "mergeCells" => 14,
            "phoneticPr" => 15,
            "conditionalFormatting" => 16,
            "dataValidations" => 17,
            "hyperlinks" => 18,
            "printOptions" => 19,
            "pageMargins" => 20,
            "pageSetup" => 21,
            "headerFooter" => 22,
            "rowBreaks" => 23,
            "colBreaks" => 24,
            "customProperties" => 25,
            "cellWatches" => 26,
            "ignoredErrors" => 27,
            "singleXmlCells" => 28,
            "smartTags" => 29,
            "drawing" => 30,
            "legacyDrawing" => 31,
            "legacyDrawingHF" => 32,
            "drawingHF" => 33,
            "picture" => 34,
            "oleObjects" => 35,
            "controls" => 36,
            "webPublishItems" => 37,
            "tableParts" => 38,
            "extLst" => 100,
            _ => 90
        };
    }

    private readonly record struct WorksheetNormalizationPreflight(
        bool NeedsChildOrderNormalization,
        bool HasLegacyDrawingHeaderFooter,
        string? LegacyDrawingHeaderFooterRelationshipId);
}
