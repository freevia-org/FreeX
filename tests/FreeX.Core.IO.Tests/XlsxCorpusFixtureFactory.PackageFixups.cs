using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO.Tests;

internal static partial class XlsxCorpusFixtureFactory
{
    private static void ApplyPackageFixups(string id, ZipArchive archive)
    {
        if (string.Equals(id, "generated-form-controls-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyFormControlsFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-custom-ribbon-ui-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyCustomRibbonUiFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-office-addins-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyOfficeAddinsFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-vba-macros-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyVbaMacrosFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-data-model-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyDataModelFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-smartart-diagrams-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplySmartArtDiagramsFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-slicers-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplySlicerTimelineFloatingDrawingFixup(
                archive,
                "Slicer Region",
                "../slicers/slicer1.xml",
                "http://schemas.microsoft.com/office/2007/relationships/slicer");
            return;
        }

        if (string.Equals(id, "generated-timelines-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplySlicerTimelineFloatingDrawingFixup(
                archive,
                "Timeline Date",
                "../timelines/timeline1.xml",
                "http://schemas.microsoft.com/office/2010/relationships/Timeline");
            return;
        }

        if (string.Equals(id, "generated-printer-settings-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyPrinterSettingsReferenceFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-calc-chain-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyCalcChainReferenceFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-custom-xml-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyCustomXmlReferenceFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-custom-docprops-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyCustomDocumentPropertiesReferenceFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-header-footer-legacy-drawing-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyHeaderFooterLegacyDrawingReferenceFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-workbook-extension-list-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorkbookExtensionListFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-legacy-drawing-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetLegacyDrawingFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-workbook-properties-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorkbookPropertiesFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-workbook-calculation-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorkbookCalculationFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-workbook-file-version-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorkbookFileVersionFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-workbook-file-recovery-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorkbookFileRecoveryFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-workbook-file-sharing-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorkbookFileSharingFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-workbook-protection-native-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorkbookProtectionNativeFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-workbook-smart-tags-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorkbookSmartTagsFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-workbook-function-groups-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorkbookFunctionGroupsFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-workbook-views-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorkbookViewsFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-workbook-defined-names-native-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorkbookDefinedNamesNativeFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-stylesheet-native-metadata-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyStylesheetNativeMetadataFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-ignored-errors-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetIgnoredErrorsFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-cell-watches-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetCellWatchesFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-single-xml-cells-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetSingleXmlCellsFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-calculation-properties-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetCalculationPropertiesFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-sheet-views-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetSheetViewsFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-sheet-format-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetSheetFormatFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-page-breaks-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetPageBreaksFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-print-options-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetPrintOptionsFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-page-setup-native-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetPageSetupNativeFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-header-footer-native-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetHeaderFooterNativeFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-dimension-native-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetDimensionNativeFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-sheet-properties-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetSheetPropertiesFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-protection-native-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetProtectionNativeFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-protected-ranges-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetProtectedRangesFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-cell-structure-native-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetCellStructureNativeFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-phonetic-properties-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetPhoneticPropertiesFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-sort-state-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetSortStateFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-data-consolidation-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetDataConsolidationFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-auto-filter-metadata-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetAutoFilterMetadataFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-custom-properties-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetCustomPropertiesFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-smart-tags-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetSmartTagsFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-scenarios-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetScenariosFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-custom-sheet-views-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetCustomSheetViewsFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-worksheet-extension-list-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetExtensionListFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-threaded-comments-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyThreadedCommentsFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-unsupported-sheet-types-001", StringComparison.OrdinalIgnoreCase))
        {
            ApplyUnsupportedSheetTypesFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-table-ref-formulas-package-003", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetTableRefFormulasFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-cross-sheet-range-package-003", StringComparison.OrdinalIgnoreCase))
        {
            ApplyWorksheetCrossSheetRangeFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-named-range-count-package-003", StringComparison.OrdinalIgnoreCase))
        {
            ApplyNamedRangeCountFixup(archive);
            return;
        }

        if (string.Equals(id, "generated-chart-series-count-003", StringComparison.OrdinalIgnoreCase))
        {
            ApplyChartSeriesCountFixup(archive);
            return;
        }

        if (!string.Equals(id, "generated-external-links-001", StringComparison.OrdinalIgnoreCase))
            return;

        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace officeRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var workbookRelsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (contentTypesEntry is null || workbookEntry is null || workbookRelsEntry is null)
            return;

        XDocument contentTypes;
        using (var stream = contentTypesEntry.Open())
            contentTypes = XDocument.Load(stream);

        if (contentTypes.Root?.Elements(contentTypeNs + "Override").Any(element =>
                string.Equals(element.Attribute("PartName")?.Value, "/xl/externalLinks/externalLink1.xml", StringComparison.OrdinalIgnoreCase)) != true)
        {
            contentTypes.Root?.Add(new XElement(
                contentTypeNs + "Override",
                new XAttribute("PartName", "/xl/externalLinks/externalLink1.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.externalLink+xml")));
        }

        contentTypesEntry.Delete();
        var contentTypesReplacement = archive.CreateEntry("[Content_Types].xml");
        using (var output = contentTypesReplacement.Open())
            contentTypes.Save(output);

        XDocument workbookXml;
        using (var stream = workbookEntry.Open())
            workbookXml = XDocument.Load(stream);
        workbookXml.Root?.Element(workbookNs + "externalReferences")?.Remove();
        workbookXml.Root?.Add(new XElement(
            workbookNs + "externalReferences",
            new XElement(workbookNs + "externalReference", new XAttribute(officeRelNs + "id", "rIdFreeXExternalLink1"))));
        workbookEntry.Delete();
        var workbookReplacement = archive.CreateEntry("xl/workbook.xml");
        using (var output = workbookReplacement.Open())
            workbookXml.Save(output);

        XDocument workbookRelsXml;
        using (var stream = workbookRelsEntry.Open())
            workbookRelsXml = XDocument.Load(stream);
        workbookRelsXml.Root?.Elements(packageRelNs + "Relationship")
            .Where(element => string.Equals(element.Attribute("Id")?.Value, "rIdFreeXExternalLink1", StringComparison.OrdinalIgnoreCase))
            .Remove();
        workbookRelsXml.Root?.Add(new XElement(
            packageRelNs + "Relationship",
            new XAttribute("Id", "rIdFreeXExternalLink1"),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink"),
            new XAttribute("Target", "externalLinks/externalLink1.xml")));
        workbookRelsEntry.Delete();
        var workbookRelsReplacement = archive.CreateEntry("xl/_rels/workbook.xml.rels");
        using var relOutput = workbookRelsReplacement.Open();
        workbookRelsXml.Save(relOutput);
    }

    private static void ApplyFormControlsFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace officeRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is not null)
        {
            var worksheetXml = LoadPackageXml(worksheetEntry);
            var controls = worksheetXml.Root?.Element(worksheetNs + "controls");
            if (controls is null && worksheetXml.Root is not null)
            {
                controls = new XElement(worksheetNs + "controls");
                worksheetXml.Root.Add(controls);
            }

            if (controls is not null && !controls.Elements(worksheetNs + "control").Any(control =>
                    string.Equals(control.Attribute(officeRelNs + "id")?.Value, "rIdFreeXControl1", StringComparison.OrdinalIgnoreCase)))
            {
                controls.Add(new XElement(
                    worksheetNs + "control",
                    new XAttribute("shapeId", "1026"),
                    new XAttribute("name", "FreeX Button"),
                    new XAttribute(officeRelNs + "id", "rIdFreeXControl1")));
            }

            ReplacePackageXml(archive, worksheetPath, worksheetXml);
        }

        var worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
        var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
            ? LoadPackageXml(worksheetRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        EnsureRelationship(
            worksheetRelsXml,
            "rIdFreeXControl1",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/ctrlProp",
            "../ctrlProps/ctrlProp1.xml");
        ReplacePackageXml(archive, worksheetRelsPath, worksheetRelsXml);

        var activeXRelsPath = "xl/activeX/_rels/activeX1.xml.rels";
        var activeXRelsXml = archive.GetEntry(activeXRelsPath) is { } activeXRelsEntry
            ? LoadPackageXml(activeXRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        EnsureRelationship(
            activeXRelsXml,
            "rIdFreeXActiveXBinary1",
            "http://schemas.microsoft.com/office/2006/relationships/activeXControlBinary",
            "activeX1.bin");
        ReplacePackageXml(archive, activeXRelsPath, activeXRelsXml);
    }

    private static void ApplyCustomRibbonUiFixup(ZipArchive archive)
    {
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var packageRelsPath = "_rels/.rels";
        var packageRelsXml = archive.GetEntry(packageRelsPath) is { } packageRelsEntry
            ? LoadPackageXml(packageRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        EnsureRelationship(
            packageRelsXml,
            "rIdFreeXCustomUi1",
            "http://schemas.microsoft.com/office/2006/relationships/ui/extensibility",
            "customUI/customUI.xml");
        ReplacePackageXml(archive, packageRelsPath, packageRelsXml);
    }

    private static void ApplyOfficeAddinsFixup(ZipArchive archive)
    {
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var packageRelsPath = "_rels/.rels";
        var packageRelsXml = archive.GetEntry(packageRelsPath) is { } packageRelsEntry
            ? LoadPackageXml(packageRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        EnsureRelationship(
            packageRelsXml,
            "rIdFreeXOfficeAddinTaskpanes1",
            "http://schemas.microsoft.com/office/2011/relationships/webextensiontaskpanes",
            "xl/webextensions/taskpanes.xml");
        ReplacePackageXml(archive, packageRelsPath, packageRelsXml);

        var taskpanesRelsPath = "xl/webextensions/_rels/taskpanes.xml.rels";
        var taskpanesRelsXml = archive.GetEntry(taskpanesRelsPath) is { } taskpanesRelsEntry
            ? LoadPackageXml(taskpanesRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        EnsureRelationship(
            taskpanesRelsXml,
            "rIdFreeXWebextension1",
            "http://schemas.microsoft.com/office/2011/relationships/webextension",
            "webextension1.xml");
        ReplacePackageXml(archive, taskpanesRelsPath, taskpanesRelsXml);
    }

    private static void ApplyVbaMacrosFixup(ZipArchive archive)
    {
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var workbookRelsPath = "xl/_rels/workbook.xml.rels";
        var workbookRelsXml = archive.GetEntry(workbookRelsPath) is { } workbookRelsEntry
            ? LoadPackageXml(workbookRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        EnsureRelationship(
            workbookRelsXml,
            "rIdFreeXVbaProject1",
            "http://schemas.microsoft.com/office/2006/relationships/vbaProject",
            "vbaProject.bin");
        ReplacePackageXml(archive, workbookRelsPath, workbookRelsXml);
    }

    private static void ApplyDataModelFixup(ZipArchive archive)
    {
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var workbookRelsPath = "xl/_rels/workbook.xml.rels";
        var workbookRelsXml = archive.GetEntry(workbookRelsPath) is { } workbookRelsEntry
            ? LoadPackageXml(workbookRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        EnsureRelationship(
            workbookRelsXml,
            "rIdFreeXDataModel1",
            "http://schemas.microsoft.com/office/2011/relationships/model",
            "model/item.data");
        ReplacePackageXml(archive, workbookRelsPath, workbookRelsXml);
    }

    private static void ApplySmartArtDiagramsFixup(ZipArchive archive)
    {
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace officeRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is not null)
        {
            var contentTypes = LoadPackageXml(contentTypesEntry);
            EnsureContentTypeOverride(
                contentTypes,
                "/xl/drawings/drawing1.xml",
                "application/vnd.openxmlformats-officedocument.drawing+xml");
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypes);
        }

        var drawingRelId = "rIdFreeXSmartArtDrawing1";
        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is not null)
        {
            var worksheetXml = LoadPackageXml(worksheetEntry);
            if (worksheetXml.Root?.Element(worksheetNs + "drawing") is null)
            {
                worksheetXml.Root?.Add(new XElement(worksheetNs + "drawing", new XAttribute(officeRelNs + "id", drawingRelId)));
                ReplacePackageXml(archive, worksheetPath, worksheetXml);
            }
        }

        var worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
        var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
            ? LoadPackageXml(worksheetRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        EnsureRelationship(
            worksheetRelsXml,
            drawingRelId,
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing",
            "../drawings/drawing1.xml");
        ReplacePackageXml(archive, worksheetRelsPath, worksheetRelsXml);

        ReplacePackageXml(archive, "xl/drawings/drawing1.xml", new XDocument(
            new XElement(
                spreadsheetDrawingNs + "wsDr",
                new XAttribute(XNamespace.Xmlns + "xdr", spreadsheetDrawingNs),
                new XAttribute(XNamespace.Xmlns + "a", drawingNs),
                new XElement(
                    spreadsheetDrawingNs + "absoluteAnchor",
                    new XElement(spreadsheetDrawingNs + "pos", new XAttribute("x", "0"), new XAttribute("y", "0")),
                    new XElement(spreadsheetDrawingNs + "ext", new XAttribute("cx", "1828800"), new XAttribute("cy", "914400")),
                    new XElement(
                        spreadsheetDrawingNs + "graphicFrame",
                        new XElement(
                            spreadsheetDrawingNs + "nvGraphicFramePr",
                            new XElement(
                                spreadsheetDrawingNs + "cNvPr",
                                new XAttribute("id", "2"),
                                new XAttribute("name", "FreeX SmartArt")),
                            new XElement(spreadsheetDrawingNs + "cNvGraphicFramePr")),
                        new XElement(spreadsheetDrawingNs + "xfrm"),
                        new XElement(
                            drawingNs + "graphic",
                            new XElement(
                                drawingNs + "graphicData",
                                new XAttribute("uri", "http://schemas.openxmlformats.org/drawingml/2006/diagram")))),
                    new XElement(spreadsheetDrawingNs + "clientData")))));

        var drawingRelsPath = "xl/drawings/_rels/drawing1.xml.rels";
        var drawingRelsXml = archive.GetEntry(drawingRelsPath) is { } drawingRelsEntry
            ? LoadPackageXml(drawingRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        EnsureRelationship(
            drawingRelsXml,
            "rIdFreeXDiagramData1",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramData",
            "../diagrams/data1.xml");
        EnsureRelationship(
            drawingRelsXml,
            "rIdFreeXDiagramLayout1",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramLayout",
            "../diagrams/layout1.xml");
        EnsureRelationship(
            drawingRelsXml,
            "rIdFreeXDiagramQuickStyle1",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramQuickStyle",
            "../diagrams/quickStyle1.xml");
        ReplacePackageXml(archive, drawingRelsPath, drawingRelsXml);
    }

    private static void ApplyThreadedCommentsFixup(ZipArchive archive)
    {
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
        var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
            ? LoadPackageXml(worksheetRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        EnsureRelationship(
            worksheetRelsXml,
            "rIdFreeXThreadedComments1",
            "http://schemas.microsoft.com/office/2017/10/relationships/threadedComment",
            "../threadedComments/threadedComment1.xml");
        ReplacePackageXml(archive, worksheetRelsPath, worksheetRelsXml);

        var workbookRelsPath = "xl/_rels/workbook.xml.rels";
        var workbookRelsXml = archive.GetEntry(workbookRelsPath) is { } workbookRelsEntry
            ? LoadPackageXml(workbookRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        EnsureRelationship(
            workbookRelsXml,
            "rIdFreeXPersons1",
            "http://schemas.microsoft.com/office/2017/10/relationships/person",
            "persons/person.xml");
        ReplacePackageXml(archive, workbookRelsPath, workbookRelsXml);
    }

    private static void ApplyUnsupportedSheetTypesFixup(ZipArchive archive)
    {
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace officeRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is not null)
        {
            var contentTypes = LoadPackageXml(contentTypesEntry);
            EnsureContentTypeOverride(
                contentTypes,
                "/xl/chartsheets/sheet1.xml",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.chartsheet+xml");
            EnsureContentTypeOverride(
                contentTypes,
                "/xl/dialogSheets/sheet2.xml",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.dialogsheet+xml");
            EnsureContentTypeOverride(
                contentTypes,
                "/xl/macroSheets/sheet3.xml",
                "application/vnd.ms-excel.macrosheet+xml");
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypes);
        }

        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is not null)
        {
            var workbookXml = LoadPackageXml(workbookEntry);
            var sheets = workbookXml.Root?.Element(workbookNs + "sheets");
            if (sheets is not null)
            {
                EnsureWorkbookSheet(
                    sheets,
                    workbookNs,
                    officeRelNs,
                    "FreeX Chart Sheet",
                    9001,
                    "rIdFreeXChartSheet1");
                EnsureWorkbookSheet(
                    sheets,
                    workbookNs,
                    officeRelNs,
                    "FreeX Dialog Sheet",
                    9002,
                    "rIdFreeXDialogSheet1");
                EnsureWorkbookSheet(
                    sheets,
                    workbookNs,
                    officeRelNs,
                    "FreeX Macro Sheet",
                    9003,
                    "rIdFreeXMacroSheet1");
                ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
            }
        }

        var workbookRelsPath = "xl/_rels/workbook.xml.rels";
        var workbookRelsXml = archive.GetEntry(workbookRelsPath) is { } workbookRelsEntry
            ? LoadPackageXml(workbookRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        EnsureRelationship(
            workbookRelsXml,
            "rIdFreeXChartSheet1",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chartsheet",
            "chartsheets/sheet1.xml");
        EnsureRelationship(
            workbookRelsXml,
            "rIdFreeXDialogSheet1",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/dialogsheet",
            "dialogSheets/sheet2.xml");
        EnsureRelationship(
            workbookRelsXml,
            "rIdFreeXMacroSheet1",
            "http://schemas.microsoft.com/office/2006/relationships/xlMacrosheet",
            "macroSheets/sheet3.xml");
        ReplacePackageXml(archive, workbookRelsPath, workbookRelsXml);
    }

    private static void EnsureWorkbookSheet(
        XElement sheets,
        XNamespace workbookNs,
        XNamespace officeRelNs,
        string name,
        int sheetId,
        string relationshipId)
    {
        sheets.Elements(workbookNs + "sheet")
            .Where(sheet => string.Equals(sheet.Attribute("name")?.Value, name, StringComparison.OrdinalIgnoreCase))
            .Remove();
        sheets.Add(new XElement(
            workbookNs + "sheet",
            new XAttribute("name", name),
            new XAttribute("sheetId", sheetId),
            new XAttribute(officeRelNs + "id", relationshipId)));
    }

    private static void ApplySlicerTimelineFloatingDrawingFixup(
        ZipArchive archive,
        string objectName,
        string nativePartTarget,
        string nativeRelationshipType)
    {
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace officeRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace markupCompatNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";
        XNamespace slicerNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
        XNamespace timelineNs = "http://schemas.microsoft.com/office/spreadsheetml/2010/11/main";

        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        var worksheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
        if (contentTypesEntry is null || worksheetEntry is null)
            return;

        var isSlicer = nativePartTarget.Contains("/slicers/", StringComparison.OrdinalIgnoreCase);
        var nativePartName = isSlicer ? "/xl/slicers/slicer1.xml" : "/xl/timelines/timeline1.xml";
        var cachePartName = isSlicer ? "/xl/slicerCaches/slicerCache1.xml" : "/xl/timelineCaches/timelineCache1.xml";
        var cacheRelationshipId = isSlicer ? "rIdFreeXSlicerCache1" : "rIdFreeXTimelineCache1";
        var nativeRelationshipId = isSlicer ? "rIdFreeXSlicerView1" : "rIdFreeXTimelineView1";
        var cacheRelationshipType = isSlicer
            ? "http://schemas.microsoft.com/office/2007/relationships/slicerCache"
            : "http://schemas.microsoft.com/office/2010/relationships/TimelineCache";
        var cacheRelationshipTarget = isSlicer ? "slicerCaches/slicerCache1.xml" : "timelineCaches/timelineCache1.xml";
        var nativeContentType = isSlicer ? "application/vnd.ms-excel.slicer+xml" : "application/vnd.ms-excel.Timeline+xml";
        var cacheContentType = isSlicer ? "application/vnd.ms-excel.slicerCache+xml" : "application/vnd.ms-excel.TimelineCache+xml";
        var extensionNs = isSlicer ? slicerNs : timelineNs;
        var extensionPrefix = isSlicer ? "x14" : "x15";

        XDocument contentTypes;
        using (var stream = contentTypesEntry.Open())
            contentTypes = XDocument.Load(stream);
        if (contentTypes.Root?.Elements(contentTypeNs + "Override").Any(element =>
                string.Equals(element.Attribute("PartName")?.Value, "/xl/drawings/drawing1.xml", StringComparison.OrdinalIgnoreCase)) != true)
        {
            contentTypes.Root?.Add(new XElement(
                contentTypeNs + "Override",
                new XAttribute("PartName", "/xl/drawings/drawing1.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.drawing+xml")));
        }
        EnsureContentTypeOverride(contentTypes, nativePartName, nativeContentType);
        EnsureContentTypeOverride(contentTypes, cachePartName, cacheContentType);
        ReplacePackageXml(archive, "[Content_Types].xml", contentTypes);

        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is not null)
        {
            var workbookXml = LoadPackageXml(workbookEntry);
            EnsureSlicerTimelineWorkbookExtensionRef(
                workbookXml,
                extensionNs,
                extensionPrefix,
                isSlicer ? "{BBE1A952-AA13-448E-AADC-164F8A28A991}" : "{D0CA8CA8-9F24-4464-BF8E-62219DCF47F9}",
                isSlicer ? "slicerCaches" : "timelineCacheRefs",
                isSlicer ? "slicerCache" : "timelineCacheRef",
                cacheRelationshipId,
                officeRelNs,
                markupCompatNs,
                workbookNs);
            ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
        }

        var workbookRelsPath = "xl/_rels/workbook.xml.rels";
        var workbookRelsXml = archive.GetEntry(workbookRelsPath) is { } workbookRelsEntry
            ? LoadPackageXml(workbookRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        EnsureRelationship(workbookRelsXml, cacheRelationshipId, cacheRelationshipType, cacheRelationshipTarget);
        ReplacePackageXml(archive, workbookRelsPath, workbookRelsXml);

        var drawingRelId = "rIdFreeXFloatingDrawing1";
        XDocument worksheetXml;
        using (var stream = worksheetEntry.Open())
            worksheetXml = XDocument.Load(stream);
        var root = worksheetXml.Root;
        if (root is not null && root.Element(worksheetNs + "drawing") is null)
            root.Add(new XElement(worksheetNs + "drawing", new XAttribute(officeRelNs + "id", drawingRelId)));
        EnsureSlicerTimelineWorksheetExtensionRef(
            worksheetXml,
            extensionNs,
            extensionPrefix,
            isSlicer ? "{A8765BA9-456A-4DAB-B4F3-ACF838C121DE}" : "{7E03D99C-DC04-49D9-9315-930204A7B6E9}",
            isSlicer ? "slicerList" : "timelineRefs",
            isSlicer ? "slicer" : "timelineRef",
            nativeRelationshipId,
            officeRelNs,
            markupCompatNs,
            worksheetNs);
        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

        var worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
        var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
            ? LoadPackageXml(worksheetRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        EnsureRelationship(
            worksheetRelsXml,
            drawingRelId,
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing",
            "../drawings/drawing1.xml");
        EnsureRelationship(worksheetRelsXml, nativeRelationshipId, nativeRelationshipType, nativePartTarget);
        ReplacePackageXml(archive, worksheetRelsPath, worksheetRelsXml);

        ReplacePackageXml(archive, "xl/drawings/drawing1.xml", new XDocument(
            new XElement(
                spreadsheetDrawingNs + "wsDr",
                new XAttribute(XNamespace.Xmlns + "xdr", spreadsheetDrawingNs),
                new XAttribute(XNamespace.Xmlns + "a", drawingNs),
                new XElement(
                    spreadsheetDrawingNs + "twoCellAnchor",
                    new XElement(
                        spreadsheetDrawingNs + "from",
                        new XElement(spreadsheetDrawingNs + "col", "4"),
                        new XElement(spreadsheetDrawingNs + "colOff", "0"),
                        new XElement(spreadsheetDrawingNs + "row", "2"),
                        new XElement(spreadsheetDrawingNs + "rowOff", "0")),
                    new XElement(
                        spreadsheetDrawingNs + "to",
                        new XElement(spreadsheetDrawingNs + "col", "8"),
                        new XElement(spreadsheetDrawingNs + "colOff", "0"),
                        new XElement(spreadsheetDrawingNs + "row", "10"),
                        new XElement(spreadsheetDrawingNs + "rowOff", "0")),
                    new XElement(
                        spreadsheetDrawingNs + "sp",
                        new XElement(
                            spreadsheetDrawingNs + "nvSpPr",
                            new XElement(
                                spreadsheetDrawingNs + "cNvPr",
                                new XAttribute("id", "2"),
                                new XAttribute("name", objectName)),
                            new XElement(spreadsheetDrawingNs + "cNvSpPr")),
                        new XElement(
                            spreadsheetDrawingNs + "spPr",
                            new XElement(drawingNs + "prstGeom",
                                new XAttribute("prst", "rect"),
                                new XElement(drawingNs + "avLst")))),
                    new XElement(spreadsheetDrawingNs + "clientData")))));

        var drawingRelsXml = new XDocument(new XElement(packageRelNs + "Relationships"));
        if (!isSlicer)
            EnsureRelationship(drawingRelsXml, "rIdFreeXNativeControl1", nativeRelationshipType, nativePartTarget);
        ReplacePackageXml(archive, "xl/drawings/_rels/drawing1.xml.rels", drawingRelsXml);
    }

    private static void EnsureSlicerTimelineWorkbookExtensionRef(
        XDocument workbookXml,
        XNamespace extensionNs,
        string prefix,
        string extensionUri,
        string containerName,
        string childName,
        string relationshipId,
        XNamespace officeRelNs,
        XNamespace markupCompatNs,
        XNamespace workbookNs)
    {
        EnsureSlicerTimelineExtensionRef(
            workbookXml,
            extensionNs,
            prefix,
            extensionUri,
            containerName,
            childName,
            relationshipId,
            officeRelNs,
            markupCompatNs,
            workbookNs);
    }

    private static void EnsureSlicerTimelineWorksheetExtensionRef(
        XDocument worksheetXml,
        XNamespace extensionNs,
        string prefix,
        string extensionUri,
        string containerName,
        string childName,
        string relationshipId,
        XNamespace officeRelNs,
        XNamespace markupCompatNs,
        XNamespace worksheetNs)
    {
        EnsureSlicerTimelineExtensionRef(
            worksheetXml,
            extensionNs,
            prefix,
            extensionUri,
            containerName,
            childName,
            relationshipId,
            officeRelNs,
            markupCompatNs,
            worksheetNs);
    }

    private static void EnsureSlicerTimelineExtensionRef(
        XDocument document,
        XNamespace extensionNs,
        string prefix,
        string extensionUri,
        string containerName,
        string childName,
        string relationshipId,
        XNamespace officeRelNs,
        XNamespace markupCompatNs,
        XNamespace mainNs)
    {
        var root = document.Root;
        if (root is null)
            return;

        root.SetAttributeValue(XNamespace.Xmlns + "r", officeRelNs.NamespaceName);
        root.SetAttributeValue(XNamespace.Xmlns + prefix, extensionNs.NamespaceName);
        root.SetAttributeValue(XNamespace.Xmlns + "mc", markupCompatNs.NamespaceName);
        var ignorable = root.Attribute(markupCompatNs + "Ignorable")?.Value ?? "";
        var prefixes = ignorable.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (!prefixes.Any(value => string.Equals(value, prefix, StringComparison.OrdinalIgnoreCase)))
            prefixes.Add(prefix);
        root.SetAttributeValue(markupCompatNs + "Ignorable", string.Join(" ", prefixes));

        var extensionList = root.Element(mainNs + "extLst");
        if (extensionList is null)
        {
            extensionList = new XElement(mainNs + "extLst");
            root.Add(extensionList);
        }

        var extension = extensionList.Elements(mainNs + "ext")
            .FirstOrDefault(element => string.Equals(element.Attribute("uri")?.Value, extensionUri, StringComparison.OrdinalIgnoreCase));
        if (extension is null)
        {
            extension = new XElement(
                mainNs + "ext",
                new XAttribute("uri", extensionUri),
                new XAttribute(XNamespace.Xmlns + prefix, extensionNs.NamespaceName));
            extensionList.Add(extension);
        }
        else
        {
            extension.SetAttributeValue(XNamespace.Xmlns + prefix, extensionNs.NamespaceName);
        }

        var container = extension.Element(extensionNs + containerName);
        if (container is null)
        {
            container = new XElement(extensionNs + containerName);
            extension.Add(container);
        }

        container.Elements(extensionNs + childName)
            .Where(element => string.Equals(element.Attribute(officeRelNs + "id")?.Value, relationshipId, StringComparison.OrdinalIgnoreCase))
            .Remove();
        container.Add(new XElement(extensionNs + childName, new XAttribute(officeRelNs + "id", relationshipId)));
    }

    private static void ApplyPrinterSettingsReferenceFixup(ZipArchive archive)
    {
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace officeRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        var worksheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
        if (contentTypesEntry is null || worksheetEntry is null)
            return;

        var contentTypes = LoadPackageXml(contentTypesEntry);
        EnsureContentTypeOverride(
            contentTypes,
            "/xl/printerSettings/printerSettings1.bin",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.printerSettings");
        ReplacePackageXml(archive, "[Content_Types].xml", contentTypes);

        var worksheetXml = LoadPackageXml(worksheetEntry);
        var pageSetup = worksheetXml.Root?.Element(worksheetNs + "pageSetup");
        if (pageSetup is null)
        {
            pageSetup = new XElement(worksheetNs + "pageSetup",
                new XAttribute("paperSize", "1"),
                new XAttribute("orientation", "portrait"));
            worksheetXml.Root?.Add(pageSetup);
        }

        pageSetup.SetAttributeValue(officeRelNs + "id", "rIdPrinterSettings1");
        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

        var worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
        var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
            ? LoadPackageXml(worksheetRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        EnsureRelationship(
            worksheetRelsXml,
            "rIdPrinterSettings1",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/printerSettings",
            "../printerSettings/printerSettings1.bin");
        ReplacePackageXml(archive, worksheetRelsPath, worksheetRelsXml);
    }

    private static void ApplyCalcChainReferenceFixup(ZipArchive archive)
    {
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is not null)
        {
            var contentTypes = LoadPackageXml(contentTypesEntry);
            EnsureContentTypeOverride(
                contentTypes,
                "/xl/calcChain.xml",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.calcChain+xml");
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypes);
        }

        var workbookRelsPath = "xl/_rels/workbook.xml.rels";
        var workbookRelsXml = archive.GetEntry(workbookRelsPath) is { } workbookRelsEntry
            ? LoadPackageXml(workbookRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        EnsureRelationship(
            workbookRelsXml,
            "rIdFreeXCalcChain1",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/calcChain",
            "/xl/calcChain.xml");
        ReplacePackageXml(archive, workbookRelsPath, workbookRelsXml);
    }

    private static void ApplyCustomXmlReferenceFixup(ZipArchive archive)
    {
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is not null)
        {
            var contentTypes = LoadPackageXml(contentTypesEntry);
            EnsureContentTypeOverride(contentTypes, "/customXml/item1.xml", "application/xml");
            EnsureContentTypeOverride(
                contentTypes,
                "/customXml/itemProps1.xml",
                "application/vnd.openxmlformats-officedocument.customXmlProperties+xml");
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypes);
        }

        var workbookRelsPath = "xl/_rels/workbook.xml.rels";
        var workbookRelsXml = archive.GetEntry(workbookRelsPath) is { } workbookRelsEntry
            ? LoadPackageXml(workbookRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        EnsureRelationship(
            workbookRelsXml,
            "rIdFreeXCustomXml1",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml",
            "../customXml/item1.xml");
        ReplacePackageXml(archive, workbookRelsPath, workbookRelsXml);
    }

    private static void ApplyCustomDocumentPropertiesReferenceFixup(ZipArchive archive)
    {
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is not null)
        {
            var contentTypes = LoadPackageXml(contentTypesEntry);
            EnsureContentTypeOverride(
                contentTypes,
                "/docProps/custom.xml",
                "application/vnd.openxmlformats-officedocument.custom-properties+xml");
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypes);
        }

        var packageRelsPath = "_rels/.rels";
        var packageRelsXml = archive.GetEntry(packageRelsPath) is { } packageRelsEntry
            ? LoadPackageXml(packageRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        EnsureRelationship(
            packageRelsXml,
            "rIdFreeXCustomDocumentProperties1",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties",
            "docProps/custom.xml");
        ReplacePackageXml(archive, packageRelsPath, packageRelsXml);
    }

    private static void ApplyHeaderFooterLegacyDrawingReferenceFixup(ZipArchive archive)
    {
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace officeRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is not null)
        {
            var contentTypes = LoadPackageXml(contentTypesEntry);
            EnsureContentTypeOverride(
                contentTypes,
                "/xl/drawings/vmlDrawing1.vml",
                "application/vnd.openxmlformats-officedocument.vmlDrawing");
            EnsureContentTypeOverride(contentTypes, "/xl/media/headerFooterImage1.png", "image/png");
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypes);
        }

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is not null)
        {
            var worksheetXml = LoadPackageXml(worksheetEntry);
            worksheetXml.Root?.Elements(worksheetNs + "legacyDrawingHF").Remove();
            worksheetXml.Root?.Add(new XElement(
                worksheetNs + "legacyDrawingHF",
                new XAttribute(officeRelNs + "id", "rIdHeaderFooterDrawing1")));
            ReplacePackageXml(archive, worksheetPath, worksheetXml);
        }

        var worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
        var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
            ? LoadPackageXml(worksheetRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        EnsureRelationship(
            worksheetRelsXml,
            "rIdHeaderFooterDrawing1",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing",
            "../drawings/vmlDrawing1.vml");
        ReplacePackageXml(archive, worksheetRelsPath, worksheetRelsXml);
    }

    private static void ApplyWorkbookExtensionListFixup(ZipArchive archive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace x15Ns = "http://schemas.microsoft.com/office/spreadsheetml/2010/11/main";

        var workbookPath = "xl/workbook.xml";
        var workbookEntry = archive.GetEntry(workbookPath);
        if (workbookEntry is null)
            return;

        var workbookXml = LoadPackageXml(workbookEntry);
        workbookXml.Root?.Elements(workbookNs + "extLst").Remove();
        workbookXml.Root?.Add(new XElement(
            workbookNs + "extLst",
            new XElement(
                workbookNs + "ext",
                new XAttribute("uri", "{00112233-4455-6677-8899-AABBCCDDEEFF}"),
                new XElement(
                    x15Ns + "futureMetadata",
                    new XAttribute(XNamespace.Xmlns + "x15", x15Ns),
                    new XAttribute("name", "FreeXUnknownWorkbookExtension")))));
        ReplacePackageXml(archive, workbookPath, workbookXml);
    }

    private static void ApplyWorksheetLegacyDrawingFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace officeRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is not null)
        {
            var worksheetXml = LoadPackageXml(worksheetEntry);
            worksheetXml.Root?.Elements(worksheetNs + "legacyDrawing").Remove();
            worksheetXml.Root?.Add(new XElement(
                worksheetNs + "legacyDrawing",
                new XAttribute(officeRelNs + "id", "rIdFreeXLegacyDrawing")));
            ReplacePackageXml(archive, worksheetPath, worksheetXml);
        }

        var worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
        var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
            ? LoadPackageXml(worksheetRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        EnsureRelationship(
            worksheetRelsXml,
            "rIdFreeXLegacyDrawing",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing",
            "../drawings/vmlDrawing1.vml");
        ReplacePackageXml(archive, worksheetRelsPath, worksheetRelsXml);
    }

    private static void ApplyWorkbookFileVersionFixup(ZipArchive archive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var workbookPath = "xl/workbook.xml";
        var workbookEntry = archive.GetEntry(workbookPath);
        if (workbookEntry is null)
            return;

        var workbookXml = LoadPackageXml(workbookEntry);
        workbookXml.Root?.Elements(workbookNs + "fileVersion").Remove();
        workbookXml.Root?.AddFirst(new XElement(
            workbookNs + "fileVersion",
            new XAttribute("appName", "xl"),
            new XAttribute("lastEdited", "7"),
            new XAttribute("lowestEdited", "7"),
            new XAttribute("rupBuild", "28129")));
        ReplacePackageXml(archive, workbookPath, workbookXml);
    }

    private static void ApplyWorkbookPropertiesFixup(ZipArchive archive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbookPath = "xl/workbook.xml";
        var workbookEntry = archive.GetEntry(workbookPath);
        if (workbookEntry is null)
            return;

        var workbookXml = LoadPackageXml(workbookEntry);
        workbookXml.Root?.Elements(workbookNs + "workbookPr").Remove();
        workbookXml.Root?.AddFirst(new XElement(
            workbookNs + "workbookPr",
            new XAttribute("date1904", "1"),
            new XAttribute("defaultThemeVersion", "166925")));
        ReplacePackageXml(archive, workbookPath, workbookXml);
    }

    private static void ApplyWorkbookCalculationFixup(ZipArchive archive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var workbookPath = "xl/workbook.xml";
        var workbookEntry = archive.GetEntry(workbookPath);
        if (workbookEntry is null)
            return;

        var workbookXml = LoadPackageXml(workbookEntry);
        workbookXml.Root?.Elements(workbookNs + "calcPr").Remove();
        workbookXml.Root?.Add(new XElement(
            workbookNs + "calcPr",
            new XAttribute("calcMode", "manual"),
            new XAttribute("iterate", "1"),
            new XAttribute("iterateCount", "50"),
            new XAttribute("calcId", "191029"),
            new XAttribute("refMode", "A1"),
            new XAttribute("fullPrecision", "0"),
            new XAttribute("concurrentCalc", "1")));
        ReplacePackageXml(archive, workbookPath, workbookXml);
    }

    private static void ApplyWorkbookFileRecoveryFixup(ZipArchive archive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var workbookPath = "xl/workbook.xml";
        var workbookEntry = archive.GetEntry(workbookPath);
        if (workbookEntry is null)
            return;

        var workbookXml = LoadPackageXml(workbookEntry);
        workbookXml.Root?.Elements(workbookNs + "fileRecoveryPr").Remove();
        workbookXml.Root?.Add(
            new XElement(
                workbookNs + "fileRecoveryPr",
                new XAttribute("autoRecover", "1"),
                new XAttribute("crashSave", "1"),
                new XAttribute("repairLoad", "0")),
            new XElement(
                workbookNs + "fileRecoveryPr",
                new XAttribute("dataExtractLoad", "1"),
                new XAttribute("repairLoad", "1")));
        ReplacePackageXml(archive, workbookPath, workbookXml);
    }

    private static void ApplyWorkbookFileSharingFixup(ZipArchive archive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var workbookPath = "xl/workbook.xml";
        var workbookEntry = archive.GetEntry(workbookPath);
        if (workbookEntry is null)
            return;

        var workbookXml = LoadPackageXml(workbookEntry);
        workbookXml.Root?.Elements(workbookNs + "fileSharing").Remove();
        workbookXml.Root?.AddFirst(new XElement(
            workbookNs + "fileSharing",
            new XAttribute("readOnlyRecommended", "1"),
            new XAttribute("userName", "FreeXTest")));
        ReplacePackageXml(archive, workbookPath, workbookXml);
    }

    private static void ApplyWorkbookProtectionNativeFixup(ZipArchive archive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbookPath = "xl/workbook.xml";
        var workbookEntry = archive.GetEntry(workbookPath);
        if (workbookEntry is null)
            return;

        var workbookXml = LoadPackageXml(workbookEntry);
        workbookXml.Root?.Elements(workbookNs + "workbookProtection").Remove();
        workbookXml.Root?.AddFirst(new XElement(
            workbookNs + "workbookProtection",
            new XAttribute("lockStructure", "1"),
            new XAttribute("lockWindows", "1"),
            new XAttribute("workbookPassword", "83AF")));
        ReplacePackageXml(archive, workbookPath, workbookXml);
    }

    private static void ApplyWorkbookSmartTagsFixup(ZipArchive archive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var workbookPath = "xl/workbook.xml";
        var workbookEntry = archive.GetEntry(workbookPath);
        if (workbookEntry is null)
            return;

        var workbookXml = LoadPackageXml(workbookEntry);
        workbookXml.Root?.Elements(workbookNs + "smartTagPr").Remove();
        workbookXml.Root?.Elements(workbookNs + "smartTagTypes").Remove();
        workbookXml.Root?.Add(
            new XElement(
                workbookNs + "smartTagPr",
                new XAttribute("embed", "1"),
                new XAttribute("show", "all")),
            new XElement(
                workbookNs + "smartTagTypes",
                new XElement(
                    workbookNs + "smartTagType",
                    new XAttribute("namespaceUri", "urn:schemas-microsoft-com:office:smarttags"),
                    new XAttribute("name", "place"))));
        ReplacePackageXml(archive, workbookPath, workbookXml);
    }

    private static void ApplyWorkbookFunctionGroupsFixup(ZipArchive archive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var workbookPath = "xl/workbook.xml";
        var workbookEntry = archive.GetEntry(workbookPath);
        if (workbookEntry is null)
            return;

        var workbookXml = LoadPackageXml(workbookEntry);
        workbookXml.Root?.Elements(workbookNs + "functionGroups").Remove();
        workbookXml.Root?.Add(new XElement(
            workbookNs + "functionGroups",
            new XAttribute("builtInGroupCount", "16"),
            new XElement(
                workbookNs + "functionGroup",
                new XAttribute("name", "FreeXNativeFunctions"))));
        ReplacePackageXml(archive, workbookPath, workbookXml);
    }

    private static void ApplyWorkbookViewsFixup(ZipArchive archive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var workbookPath = "xl/workbook.xml";
        var workbookEntry = archive.GetEntry(workbookPath);
        if (workbookEntry is null)
            return;

        var workbookXml = LoadPackageXml(workbookEntry);
        workbookXml.Root?.Elements(workbookNs + "bookViews").Remove();
        workbookXml.Root?.Elements(workbookNs + "customWorkbookViews").Remove();
        workbookXml.Root?.AddFirst(new XElement(
            workbookNs + "bookViews",
            new XElement(
                workbookNs + "workbookView",
                new XAttribute("visibility", "visible"),
                new XAttribute("showSheetTabs", "0"),
                new XAttribute("tabRatio", "700"),
                new XAttribute("firstSheet", "0"),
                new XAttribute("activeTab", "0")),
            new XElement(
                workbookNs + "workbookView",
                new XAttribute("visibility", "hidden"),
                new XAttribute("minimized", "1"),
                new XAttribute("showHorizontalScroll", "0"),
                new XAttribute("showVerticalScroll", "0"),
                new XAttribute("showSheetTabs", "0"),
                new XAttribute("tabRatio", "700"),
                new XAttribute("firstSheet", "0"),
                new XAttribute("activeTab", "0"))));
        workbookXml.Root?.Add(new XElement(
            workbookNs + "customWorkbookViews",
            new XElement(
                workbookNs + "customWorkbookView",
                new XAttribute("name", "FreeXView"),
                new XAttribute("guid", "{22222222-2222-2222-2222-222222222222}"),
                new XAttribute("autoUpdate", "0"),
                new XAttribute("mergeInterval", "0"),
                new XAttribute("personalView", "0"),
                new XAttribute("includePrintSettings", "1"),
                new XAttribute("includeHiddenRowCol", "1"))));
        ReplacePackageXml(archive, workbookPath, workbookXml);
    }

    private static void ApplyWorkbookDefinedNamesNativeFixup(ZipArchive archive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var workbookPath = "xl/workbook.xml";
        var workbookEntry = archive.GetEntry(workbookPath);
        if (workbookEntry is null)
            return;

        var workbookXml = LoadPackageXml(workbookEntry);
        workbookXml.Root?.Elements(workbookNs + "definedNames").Remove();
        workbookXml.Root?.Add(new XElement(
            workbookNs + "definedNames",
            new XElement(
                workbookNs + "definedName",
                new XAttribute("name", "DynamicSalesRange"),
                new XAttribute("hidden", "1"),
                "1+1")));
        ReplacePackageXml(archive, workbookPath, workbookXml);
    }

    private static void ApplyStylesheetNativeMetadataFixup(ZipArchive archive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var stylesPath = "xl/styles.xml";
        var stylesEntry = archive.GetEntry(stylesPath);
        if (stylesEntry is null)
            return;

        var stylesXml = LoadPackageXml(stylesEntry);
        stylesXml.Root?.Elements(workbookNs + "colors").Remove();
        stylesXml.Root?.Elements(workbookNs + "dxfs").Remove();
        stylesXml.Root?.Elements(workbookNs + "tableStyles").Remove();
        stylesXml.Root?.Elements(workbookNs + "extLst").Remove();
        stylesXml.Root?.Add(
            new XElement(
                workbookNs + "colors",
                new XElement(
                    workbookNs + "indexedColors",
                    new XElement(workbookNs + "rgbColor", new XAttribute("rgb", "FF010203")))),
            new XElement(
                workbookNs + "dxfs",
                new XAttribute("count", "1"),
                new XElement(
                    workbookNs + "dxf",
                    new XElement(
                        workbookNs + "fill",
                        new XElement(
                            workbookNs + "patternFill",
                            new XAttribute("patternType", "solid"),
                            new XElement(workbookNs + "fgColor", new XAttribute("rgb", "FFABCDEF")))))),
            new XElement(
                workbookNs + "tableStyles",
                new XAttribute("defaultPivotStyle", "PivotStyleMedium9"),
                new XElement(
                    workbookNs + "tableStyle",
                    new XAttribute("name", "FreeXNativeTableStyle"),
                    new XAttribute("pivot", "0"),
                    new XAttribute("table", "1"),
                    new XAttribute("count", "1"),
                    new XElement(
                        workbookNs + "tableStyleElement",
                        new XAttribute("type", "wholeTable"),
                        new XAttribute("dxfId", "0"))),
                new XElement(
                    workbookNs + "tableStyle",
                    new XAttribute("name", "FreeXNativePivotStyle"),
                    new XAttribute("pivot", "1"),
                    new XAttribute("table", "0"),
                    new XAttribute("count", "1"),
                    new XElement(
                        workbookNs + "tableStyleElement",
                        new XAttribute("type", "wholeTable"),
                        new XAttribute("dxfId", "0")))),
            new XElement(
                workbookNs + "extLst",
                new XElement(
                    workbookNs + "ext",
                    new XAttribute("uri", "{FFEEDDCC-7788-6655-4433-22110099AABB}"),
                    new XElement(workbookNs + "FreeXNativeStylesExtension"))));
        ReplacePackageXml(archive, stylesPath, stylesXml);
    }

    private static void ApplyWorksheetIgnoredErrorsFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadPackageXml(worksheetEntry);
        worksheetXml.Root?.Elements(worksheetNs + "ignoredErrors").Remove();
        worksheetXml.Root?.Add(new XElement(
            worksheetNs + "ignoredErrors",
            new XElement(
                worksheetNs + "ignoredError",
                new XAttribute("sqref", "A1"),
                new XAttribute("numberStoredAsText", "1"),
                new XAttribute("twoDigitTextYear", "1"))));
        ReplacePackageXml(archive, worksheetPath, worksheetXml);
    }

    private static void ApplyWorksheetCellWatchesFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadPackageXml(worksheetEntry);
        worksheetXml.Root?.Elements(worksheetNs + "cellWatches").Remove();
        worksheetXml.Root?.Add(new XElement(
            worksheetNs + "cellWatches",
            new XElement(
                worksheetNs + "cellWatch",
                new XAttribute("r", "A1"))));
        ReplacePackageXml(archive, worksheetPath, worksheetXml);
    }

    private static void ApplyWorksheetSingleXmlCellsFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadPackageXml(worksheetEntry);
        worksheetXml.Root?.Elements(worksheetNs + "singleXmlCells").Remove();
        worksheetXml.Root?.Add(new XElement(
            worksheetNs + "singleXmlCells",
            new XAttribute("nativeSingleXmlCellsAttr", "kept"),
            new XElement(
                worksheetNs + "singleXmlCell",
                new XAttribute("id", "1"),
                new XAttribute("r", "A1"),
                new XAttribute("xmlCellPrId", "1"),
                new XAttribute("nativeSingleXmlCellAttr", "cell-kept"))));
        ReplacePackageXml(archive, worksheetPath, worksheetXml);
    }

    private static void ApplyWorksheetCalculationPropertiesFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadPackageXml(worksheetEntry);
        worksheetXml.Root?.Elements(worksheetNs + "sheetCalcPr").Remove();
        worksheetXml.Root?.Add(new XElement(
            worksheetNs + "sheetCalcPr",
            new XAttribute("fullCalcOnLoad", "1")));
        ReplacePackageXml(archive, worksheetPath, worksheetXml);
    }

    private static void ApplyWorksheetSheetViewsFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadPackageXml(worksheetEntry);
        worksheetXml.Root?.Elements(worksheetNs + "sheetViews").Remove();
        worksheetXml.Root?.AddFirst(new XElement(
            worksheetNs + "sheetViews",
            new XElement(
                worksheetNs + "sheetView",
                new XAttribute("workbookViewId", "0"),
                new XAttribute("showZeros", "0"),
                new XAttribute("rightToLeft", "1"))));
        ReplacePackageXml(archive, worksheetPath, worksheetXml);
    }

    private static void ApplyWorksheetSheetFormatFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadPackageXml(worksheetEntry);
        worksheetXml.Root?.Elements(worksheetNs + "sheetFormatPr").Remove();
        worksheetXml.Root?.AddFirst(new XElement(
            worksheetNs + "sheetFormatPr",
            new XAttribute("baseColWidth", "12"),
            new XAttribute("zeroHeight", "1"),
            new XAttribute("thickTop", "1"),
            new XAttribute("outlineLevelRow", "3")));
        ReplacePackageXml(archive, worksheetPath, worksheetXml);
    }

    private static void ApplyWorksheetPageBreaksFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadPackageXml(worksheetEntry);
        worksheetXml.Root?.Elements(worksheetNs + "rowBreaks").Remove();
        worksheetXml.Root?.Elements(worksheetNs + "colBreaks").Remove();
        worksheetXml.Root?.Add(
            new XElement(
                worksheetNs + "rowBreaks",
                new XAttribute("count", "1"),
                new XAttribute("manualBreakCount", "1"),
                new XElement(
                    worksheetNs + "brk",
                    new XAttribute("id", "20"),
                    new XAttribute("max", "16383"),
                    new XAttribute("man", "1"),
                    new XAttribute("pt", "1"))),
            new XElement(
                worksheetNs + "colBreaks",
                new XAttribute("count", "1"),
                new XAttribute("manualBreakCount", "1"),
                new XElement(
                    worksheetNs + "brk",
                    new XAttribute("id", "5"),
                    new XAttribute("max", "1048575"),
                    new XAttribute("man", "1"),
                    new XAttribute("pt", "1"))));
        ReplacePackageXml(archive, worksheetPath, worksheetXml);
    }

    private static void ApplyWorksheetPrintOptionsFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadPackageXml(worksheetEntry);
        worksheetXml.Root?.Elements(worksheetNs + "printOptions").Remove();
        worksheetXml.Root?.Add(new XElement(
            worksheetNs + "printOptions",
            new XAttribute("gridLinesSet", "1")));
        ReplacePackageXml(archive, worksheetPath, worksheetXml);
    }

    private static void ApplyWorksheetPageSetupNativeFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadPackageXml(worksheetEntry);
        worksheetXml.Root?.Elements(worksheetNs + "pageSetup").Remove();
        worksheetXml.Root?.Add(new XElement(
            worksheetNs + "pageSetup",
            new XAttribute("usePrinterDefaults", "1"),
            new XAttribute("copies", "3")));
        ReplacePackageXml(archive, worksheetPath, worksheetXml);
    }

    private static void ApplyWorksheetHeaderFooterNativeFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadPackageXml(worksheetEntry);
        worksheetXml.Root?.Elements(worksheetNs + "headerFooter").Remove();
        worksheetXml.Root?.Add(new XElement(
            worksheetNs + "headerFooter",
            new XElement(worksheetNs + "oddHeader", "&LLeft&CCenter&RRight")));
        ReplacePackageXml(archive, worksheetPath, worksheetXml);
    }

    private static void ApplyWorksheetDimensionNativeFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadPackageXml(worksheetEntry);
        worksheetXml.Root?.Elements(worksheetNs + "dimension").Remove();
        worksheetXml.Root?.AddFirst(new XElement(
            worksheetNs + "dimension",
            new XAttribute("ref", "A1")));
        ReplacePackageXml(archive, worksheetPath, worksheetXml);
    }

    private static void ApplyWorksheetSheetPropertiesFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadPackageXml(worksheetEntry);
        worksheetXml.Root?.Elements(worksheetNs + "sheetPr").Remove();
        worksheetXml.Root?.AddFirst(new XElement(
            worksheetNs + "sheetPr",
            new XAttribute("filterMode", "1"),
            new XElement(
                worksheetNs + "pageSetUpPr",
                new XAttribute("fitToPage", "1"),
                new XAttribute("autoPageBreaks", "0"))));
        ReplacePackageXml(archive, worksheetPath, worksheetXml);
    }

    private static void ApplyWorksheetProtectionNativeFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadPackageXml(worksheetEntry);
        worksheetXml.Root?.Elements(worksheetNs + "sheetProtection").Remove();
        worksheetXml.Root?.Add(new XElement(
            worksheetNs + "sheetProtection",
            new XAttribute("sheet", "1"),
            new XAttribute("algorithmName", "SHA-512"),
            new XAttribute("hashValue", "AQIDBA=="),
            new XAttribute("saltValue", "BQYHCA=="),
            new XAttribute("spinCount", "100000"),
            new XAttribute("objects", "1"),
            new XAttribute("scenarios", "1")));
        ReplacePackageXml(archive, worksheetPath, worksheetXml);
    }

    private static void ApplyWorksheetProtectedRangesFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadPackageXml(worksheetEntry);
        worksheetXml.Root?.Elements(worksheetNs + "protectedRanges").Remove();
        worksheetXml.Root?.Add(new XElement(
            worksheetNs + "protectedRanges",
            new XElement(
                worksheetNs + "protectedRange",
                new XAttribute("name", "NativeEditableRange"),
                new XAttribute("sqref", "B2:C3"),
                new XAttribute("password", "ABCD"),
                new XAttribute("securityDescriptor", "D:PAI")),
            new XElement(
                worksheetNs + "protectedRange",
                new XAttribute("name", "NativeMultiAreaRange"),
                new XAttribute("sqref", "B2 C3"),
                new XAttribute("password", "1234"))));
        ReplacePackageXml(archive, worksheetPath, worksheetXml);
    }

    private static void ApplyWorksheetCellStructureNativeFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace freexNs = "urn:freex:test";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadPackageXml(worksheetEntry);
        worksheetXml.Root?.Elements(worksheetNs + "cols").Remove();
        worksheetXml.Root?.Elements(worksheetNs + "sheetData").Remove();
        worksheetXml.Root?.Elements(worksheetNs + "mergeCells").Remove();
        worksheetXml.Root?.AddFirst(new XElement(
            worksheetNs + "cols",
            new XElement(
                worksheetNs + "col",
                new XAttribute("min", "2"),
                new XAttribute("max", "2"),
                new XAttribute("width", "14"),
                new XAttribute("customWidth", "1"),
                new XAttribute("bestFit", "1"),
                new XAttribute("phonetic", "1"))));

        worksheetXml.Root?.Add(new XElement(
            worksheetNs + "sheetData",
            new XElement(
                worksheetNs + "row",
                new XAttribute("r", "1"),
                new XElement(
                    worksheetNs + "c",
                    new XAttribute("r", "A1"),
                    new XElement(worksheetNs + "v", "3.14"))),
            new XElement(
                worksheetNs + "row",
                new XAttribute("r", "2"),
                new XAttribute("thickTop", "1"),
                new XAttribute("ph", "1"),
                new XElement(
                    worksheetNs + "c",
                    new XAttribute("r", "A2"),
                    new XAttribute("ph", "1"),
                    new XElement(
                        worksheetNs + "f",
                        new XAttribute("t", "array"),
                        new XAttribute("ref", "A2:A2"),
                        new XAttribute("ca", "1"),
                        "A1*2"),
                    new XElement(worksheetNs + "v", "6.28"),
                    new XElement(
                        worksheetNs + "extLst",
                        new XElement(
                            worksheetNs + "ext",
                            new XAttribute("uri", "{FREEX-CELL-EXT}"),
                            new XElement(freexNs + "cellExt", new XAttribute("value", "cell-extension"))))),
                new XElement(
                    worksheetNs + "extLst",
                    new XElement(
                        worksheetNs + "ext",
                        new XAttribute("uri", "{FREEX-ROW-EXT}"),
                        new XElement(freexNs + "rowExt", new XAttribute("value", "row-extension"))))),
            new XElement(
                worksheetNs + "row",
                new XAttribute("r", "4"),
                new XElement(
                    worksheetNs + "c",
                    new XAttribute("r", "A4"),
                    new XAttribute("t", "str"),
                    new XElement(worksheetNs + "v", "merged")))));
        worksheetXml.Root?.Add(new XElement(
            worksheetNs + "mergeCells",
            new XAttribute("count", "1"),
            new XElement(
                worksheetNs + "mergeCell",
                new XAttribute("ref", "A4:B5"))));

        ReplacePackageXml(archive, worksheetPath, worksheetXml);
    }

    private static void ApplyWorksheetPhoneticPropertiesFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadPackageXml(worksheetEntry);
        worksheetXml.Root?.Elements(worksheetNs + "phoneticPr").Remove();
        worksheetXml.Root?.Add(new XElement(
            worksheetNs + "phoneticPr",
            new XAttribute("fontId", "1"),
            new XAttribute("type", "fullwidthKatakana"),
            new XAttribute("alignment", "center")));
        ReplacePackageXml(archive, worksheetPath, worksheetXml);
    }

    private static void ApplyWorksheetSortStateFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadPackageXml(worksheetEntry);
        worksheetXml.Root?.Elements(worksheetNs + "autoFilter").Remove();
        worksheetXml.Root?.Elements(worksheetNs + "sortState").Remove();
        worksheetXml.Root?.Add(
            new XElement(
                worksheetNs + "autoFilter",
                new XAttribute("ref", "A1:B3"),
                new XElement(
                    worksheetNs + "filterColumn",
                    new XAttribute("colId", "0"),
                    new XElement(
                        worksheetNs + "filters",
                        new XElement(worksheetNs + "filter", new XAttribute("val", "A"))))),
            new XElement(
                worksheetNs + "sortState",
                new XAttribute("ref", "A1:A3"),
                new XAttribute("caseSensitive", "1"),
                new XAttribute("sortMethod", "stroke"),
                new XElement(
                    worksheetNs + "sortCondition",
                    new XAttribute("ref", "A2:A3"),
                    new XAttribute("descending", "1"),
                    new XAttribute("sortBy", "cellColor"))));
        ReplacePackageXml(archive, worksheetPath, worksheetXml);
    }

    private static void ApplyWorksheetDataConsolidationFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadPackageXml(worksheetEntry);
        worksheetXml.Root?.Elements(worksheetNs + "dataConsolidate").Remove();
        worksheetXml.Root?.Add(new XElement(
            worksheetNs + "dataConsolidate",
            new XAttribute("function", "sum"),
            new XAttribute("leftLabels", "1"),
            new XAttribute("topLabels", "1"),
            new XAttribute("link", "1"),
            new XElement(
                worksheetNs + "dataRefs",
                new XAttribute("count", "1"),
                new XElement(
                    worksheetNs + "dataRef",
                    new XAttribute("ref", "A1:B2"),
                    new XAttribute("sheet", "Data")))));
        ReplacePackageXml(archive, worksheetPath, worksheetXml);
    }

    private static void ApplyWorksheetAutoFilterMetadataFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadPackageXml(worksheetEntry);
        worksheetXml.Root?.Elements(worksheetNs + "sheetData").Remove();
        worksheetXml.Root?.Elements(worksheetNs + "autoFilter").Remove();
        worksheetXml.Root?.Add(
            new XElement(
                worksheetNs + "sheetData",
                new XElement(
                    worksheetNs + "row",
                    new XAttribute("r", "1"),
                    new XElement(worksheetNs + "c", new XAttribute("r", "A1"), new XAttribute("t", "str"), new XElement(worksheetNs + "v", "Category")),
                    new XElement(worksheetNs + "c", new XAttribute("r", "B1"), new XAttribute("t", "str"), new XElement(worksheetNs + "v", "Amount"))),
                new XElement(
                    worksheetNs + "row",
                    new XAttribute("r", "2"),
                    new XElement(worksheetNs + "c", new XAttribute("r", "A2"), new XAttribute("t", "str"), new XElement(worksheetNs + "v", "A")),
                    new XElement(worksheetNs + "c", new XAttribute("r", "B2"), new XElement(worksheetNs + "v", "10"))),
                new XElement(
                    worksheetNs + "row",
                    new XAttribute("r", "3"),
                    new XElement(worksheetNs + "c", new XAttribute("r", "A3"), new XAttribute("t", "str"), new XElement(worksheetNs + "v", "B")),
                    new XElement(worksheetNs + "c", new XAttribute("r", "B3"), new XElement(worksheetNs + "v", "20")))),
            new XElement(
                worksheetNs + "autoFilter",
                new XAttribute("ref", "A1:B3"),
                new XElement(
                    worksheetNs + "filterColumn",
                    new XAttribute("colId", "0"),
                    new XElement(
                        worksheetNs + "filters",
                        new XAttribute("blank", "1"),
                        new XElement(worksheetNs + "filter", new XAttribute("val", "A"))))));
        ReplacePackageXml(archive, worksheetPath, worksheetXml);
    }

    private static void ApplyWorksheetCustomPropertiesFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadPackageXml(worksheetEntry);
        worksheetXml.Root?.Elements(worksheetNs + "customProperties").Remove();
        worksheetXml.Root?.Add(new XElement(
            worksheetNs + "customProperties",
            new XElement(
                worksheetNs + "customPr",
                new XAttribute("name", "FreeXNativeProperty"),
                new XAttribute("id", "1"))));
        ReplacePackageXml(archive, worksheetPath, worksheetXml);
    }

    private static void ApplyWorksheetSmartTagsFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadPackageXml(worksheetEntry);
        worksheetXml.Root?.Elements(worksheetNs + "smartTags").Remove();
        worksheetXml.Root?.Add(new XElement(
            worksheetNs + "smartTags",
            new XElement(
                worksheetNs + "cellSmartTags",
                new XAttribute("r", "A1"),
                new XElement(
                    worksheetNs + "cellSmartTag",
                    new XAttribute("type", "0"),
                    new XAttribute("deleted", "0"),
                    new XElement(
                        worksheetNs + "cellSmartTagPr",
                        new XAttribute("key", "place"),
                        new XAttribute("val", "Seattle"),
                        new XAttribute("customSmartTagPropertyFlag", "keep"))))));
        ReplacePackageXml(archive, worksheetPath, worksheetXml);
    }

    private static void ApplyWorksheetScenariosFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadPackageXml(worksheetEntry);
        worksheetXml.Root?.Elements(worksheetNs + "scenarios").Remove();
        worksheetXml.Root?.Add(new XElement(
            worksheetNs + "scenarios",
            new XAttribute("current", "0"),
            new XAttribute("show", "0"),
            new XElement(
                worksheetNs + "scenario",
                new XAttribute("name", "BestCase"),
                new XAttribute("comment", "Scenario comment"),
                new XAttribute("hidden", "1"),
                new XAttribute("locked", "1"),
                new XAttribute("count", "1"),
                new XAttribute("user", "FreeXTest"),
                new XElement(
                    worksheetNs + "inputCells",
                    new XAttribute("r", "A1"),
                    new XAttribute("val", "42")))));
        ReplacePackageXml(archive, worksheetPath, worksheetXml);
    }

    private static void ApplyWorksheetCustomSheetViewsFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadPackageXml(worksheetEntry);
        worksheetXml.Root?.Elements(worksheetNs + "customSheetViews").Remove();
        worksheetXml.Root?.Add(new XElement(
            worksheetNs + "customSheetViews",
            new XElement(
                worksheetNs + "customSheetView",
                new XAttribute("guid", "{11111111-1111-1111-1111-111111111111}"),
                new XAttribute("scale", "120"),
                new XAttribute("showGridLines", "0"),
                new XAttribute("showRowCol", "0"),
                new XAttribute("state", "visible"),
                new XElement(
                    worksheetNs + "pane",
                    new XAttribute("xSplit", "1"),
                    new XAttribute("ySplit", "1"),
                    new XAttribute("topLeftCell", "B2"),
                    new XAttribute("activePane", "bottomRight")))));
        ReplacePackageXml(archive, worksheetPath, worksheetXml);
    }

    private static void ApplyWorksheetExtensionListFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace x14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
        XNamespace x15Ns = "http://schemas.microsoft.com/office/spreadsheetml/2010/11/main";
        XNamespace xmNs = "http://schemas.microsoft.com/office/excel/2006/main";

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return;

        var worksheetXml = LoadPackageXml(worksheetEntry);
        worksheetXml.Root?.Elements(worksheetNs + "sheetData").Remove();
        worksheetXml.Root?.Elements(worksheetNs + "extLst").Remove();
        worksheetXml.Root?.Add(
            new XElement(
                worksheetNs + "sheetData",
                new XElement(
                    worksheetNs + "row",
                    new XAttribute("r", "1"),
                    new XElement(worksheetNs + "c", new XAttribute("r", "A1"), new XElement(worksheetNs + "v", "1")),
                    new XElement(worksheetNs + "c", new XAttribute("r", "B1"), new XElement(worksheetNs + "v", "2")),
                    new XElement(worksheetNs + "c", new XAttribute("r", "C1"), new XElement(worksheetNs + "v", "3")))),
            new XElement(
                worksheetNs + "extLst",
                new XElement(
                    worksheetNs + "ext",
                    new XAttribute("uri", "{05C60535-1F16-4fd2-B633-F4F36F0B64E0}"),
                    new XElement(
                        x14Ns + "sparklineGroups",
                        new XAttribute(XNamespace.Xmlns + "x14", x14Ns),
                        new XAttribute(XNamespace.Xmlns + "xm", xmNs),
                        new XElement(
                            x14Ns + "sparklineGroup",
                            new XAttribute("type", "column"),
                            new XElement(
                                x14Ns + "sparklines",
                                new XElement(
                                    x14Ns + "sparkline",
                                    new XElement(xmNs + "f", "Sheet1!A1:C1"),
                                    new XElement(xmNs + "sqref", "D1")))))),
                new XElement(
                    worksheetNs + "ext",
                    new XAttribute("uri", "{FFEEDDCC-BBAA-9988-7766-554433221100}"),
                    new XElement(
                        x15Ns + "futureMetadata",
                        new XAttribute(XNamespace.Xmlns + "x15", x15Ns),
                        new XAttribute("name", "FreeXUnknownWorksheetExtension")))));
        ReplacePackageXml(archive, worksheetPath, worksheetXml);
    }

    private static void ApplyWorksheetTableRefFormulasFixup(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace officeRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is not null)
        {
            var contentTypes = LoadPackageXml(contentTypesEntry);
            EnsureContentTypeOverride(
                contentTypes,
                "/xl/tables/table1.xml",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.table+xml");
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypes);
        }

        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is not null)
        {
            var worksheetXml = LoadPackageXml(worksheetEntry);
            var worksheetRoot = worksheetXml.Root;
            if (worksheetRoot is not null)
            {
                worksheetRoot.SetAttributeValue(XNamespace.Xmlns + "r", officeRelNs.NamespaceName);
                var sheetData = CreateTableRefFormulaSheetData(worksheetNs);
                var existingSheetData = worksheetRoot.Element(worksheetNs + "sheetData");
                if (existingSheetData is null)
                    worksheetRoot.Add(sheetData);
                else
                    existingSheetData.ReplaceWith(sheetData);

                worksheetRoot.Elements(worksheetNs + "tableParts").Remove();
                InsertWorksheetTerminalElementInOrder(
                    worksheetRoot,
                    worksheetNs,
                    new XElement(
                        worksheetNs + "tableParts",
                        new XAttribute("count", "1"),
                        new XElement(
                            worksheetNs + "tablePart",
                            new XAttribute(officeRelNs + "id", "rIdFreeXSalesTable1"))));
                ReplacePackageXml(archive, worksheetPath, worksheetXml);
            }
        }

        var worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
        var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
            ? LoadPackageXml(worksheetRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        EnsureRelationship(
            worksheetRelsXml,
            "rIdFreeXSalesTable1",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/table",
            "../tables/table1.xml");
        ReplacePackageXml(archive, worksheetRelsPath, worksheetRelsXml);

        ReplacePackageXml(
            archive,
            "xl/tables/table1.xml",
            new XDocument(
                new XElement(
                    worksheetNs + "table",
                    new XAttribute("id", "1"),
                    new XAttribute("name", "SalesTable"),
                    new XAttribute("displayName", "SalesTable"),
                    new XAttribute("ref", "A1:D3"),
                    new XAttribute("totalsRowShown", "0"),
                    new XElement(worksheetNs + "autoFilter", new XAttribute("ref", "A1:D3")),
                    new XElement(
                        worksheetNs + "tableColumns",
                        new XAttribute("count", "4"),
                        new XElement(worksheetNs + "tableColumn", new XAttribute("id", "1"), new XAttribute("name", "Item")),
                        new XElement(worksheetNs + "tableColumn", new XAttribute("id", "2"), new XAttribute("name", "Price")),
                        new XElement(worksheetNs + "tableColumn", new XAttribute("id", "3"), new XAttribute("name", "Qty")),
                        new XElement(worksheetNs + "tableColumn", new XAttribute("id", "4"), new XAttribute("name", "Total"))),
                    new XElement(
                        worksheetNs + "tableStyleInfo",
                        new XAttribute("name", "TableStyleMedium2"),
                        new XAttribute("showFirstColumn", "0"),
                        new XAttribute("showLastColumn", "0"),
                        new XAttribute("showRowStripes", "1"),
                        new XAttribute("showColumnStripes", "0")))));
    }

    private static void ApplyWorksheetCrossSheetRangeFixup(ZipArchive archive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace officeRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is not null)
        {
            var contentTypes = LoadPackageXml(contentTypesEntry);
            EnsureContentTypeOverride(
                contentTypes,
                "/xl/worksheets/sheet2.xml",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypes);
        }

        var workbookPath = "xl/workbook.xml";
        var workbookEntry = archive.GetEntry(workbookPath);
        if (workbookEntry is not null)
        {
            var workbookXml = LoadPackageXml(workbookEntry);
            var workbookRoot = workbookXml.Root;
            var sheets = workbookRoot?.Element(workbookNs + "sheets");
            if (workbookRoot is not null && sheets is not null)
            {
                workbookRoot.SetAttributeValue(XNamespace.Xmlns + "r", officeRelNs.NamespaceName);
                sheets.Elements(workbookNs + "sheet")
                    .Where(sheet => string.Equals(sheet.Attribute("name")?.Value, "Lookup", StringComparison.OrdinalIgnoreCase))
                    .Remove();
                sheets.Add(new XElement(
                    workbookNs + "sheet",
                    new XAttribute("name", "Lookup"),
                    new XAttribute("sheetId", "2"),
                    new XAttribute(officeRelNs + "id", "rIdFreeXCrossSheetLookup")));
                ReplacePackageXml(archive, workbookPath, workbookXml);
            }
        }

        var workbookRelsPath = "xl/_rels/workbook.xml.rels";
        var workbookRelsXml = archive.GetEntry(workbookRelsPath) is { } workbookRelsEntry
            ? LoadPackageXml(workbookRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        EnsureRelationship(
            workbookRelsXml,
            "rIdFreeXCrossSheetLookup",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet",
            "/xl/worksheets/sheet2.xml");
        ReplacePackageXml(archive, workbookRelsPath, workbookRelsXml);

        ReplacePackageXml(
            archive,
            "xl/worksheets/sheet1.xml",
            new XDocument(
                new XElement(
                    workbookNs + "worksheet",
                    CreateCrossSheetSummarySheetData(workbookNs))));
        ReplacePackageXml(
            archive,
            "xl/worksheets/sheet2.xml",
            new XDocument(
                new XElement(
                    workbookNs + "worksheet",
                    CreateCrossSheetLookupSheetData(workbookNs))));
    }

    private static void ApplyNamedRangeCountFixup(ZipArchive archive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null) return;
        var workbookXml = LoadPackageXml(workbookEntry);
        var workbookRoot = workbookXml.Root;
        if (workbookRoot is null)
            return;

        workbookRoot.Elements(workbookNs + "definedNames").Remove();
        var definedNamesElement = new XElement(
            workbookNs + "definedNames",
            new XElement(workbookNs + "definedName", new XAttribute("name", "Range01"), "'Sheet1'!$A$1:$A$3"),
            new XElement(workbookNs + "definedName", new XAttribute("name", "Range02"), "'Sheet1'!$B$1:$B$3"),
            new XElement(workbookNs + "definedName", new XAttribute("name", "Range03"), "'Sheet1'!$C$1:$C$3"),
            new XElement(workbookNs + "definedName", new XAttribute("name", "Range04"), "'Sheet1'!$D$1:$D$3"),
            new XElement(workbookNs + "definedName", new XAttribute("name", "Range05"), "'Sheet1'!$E$1:$E$3"),
            new XElement(workbookNs + "definedName", new XAttribute("name", "Range06"), "'Sheet1'!$F$1:$F$3"),
            new XElement(workbookNs + "definedName", new XAttribute("name", "Range07"), "'Sheet1'!$G$1:$G$3"),
            new XElement(workbookNs + "definedName", new XAttribute("name", "Range08"), "'Sheet1'!$H$1:$H$3"),
            new XElement(workbookNs + "definedName", new XAttribute("name", "Range09"), "'Sheet1'!$I$1:$I$3"),
            new XElement(workbookNs + "definedName", new XAttribute("name", "Range10"), "'Sheet1'!$J$1:$J$3"),
            new XElement(workbookNs + "definedName", new XAttribute("name", "Range11"), "'Sheet1'!$K$1:$K$3"),
            new XElement(workbookNs + "definedName", new XAttribute("name", "Range12"), "'Sheet1'!$L$1:$L$3"));

        var sheets = workbookRoot.Element(workbookNs + "sheets");
        if (sheets is null)
            workbookRoot.AddFirst(definedNamesElement);
        else
            sheets.AddAfterSelf(definedNamesElement);
        ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
    }

    private static XElement CreateTableRefFormulaSheetData(XNamespace worksheetNs) =>
        new(
            worksheetNs + "sheetData",
            new XElement(
                worksheetNs + "row",
                new XAttribute("r", "1"),
                new XElement(worksheetNs + "c", new XAttribute("r", "A1"), new XAttribute("t", "str"), new XElement(worksheetNs + "v", "Item")),
                new XElement(worksheetNs + "c", new XAttribute("r", "B1"), new XAttribute("t", "str"), new XElement(worksheetNs + "v", "Price")),
                new XElement(worksheetNs + "c", new XAttribute("r", "C1"), new XAttribute("t", "str"), new XElement(worksheetNs + "v", "Qty")),
                new XElement(worksheetNs + "c", new XAttribute("r", "D1"), new XAttribute("t", "str"), new XElement(worksheetNs + "v", "Total"))),
            new XElement(
                worksheetNs + "row",
                new XAttribute("r", "2"),
                new XElement(worksheetNs + "c", new XAttribute("r", "A2"), new XAttribute("t", "str"), new XElement(worksheetNs + "v", "Alpha")),
                new XElement(worksheetNs + "c", new XAttribute("r", "B2"), new XElement(worksheetNs + "v", "10")),
                new XElement(worksheetNs + "c", new XAttribute("r", "C2"), new XElement(worksheetNs + "v", "5")),
                new XElement(
                    worksheetNs + "c",
                    new XAttribute("r", "D2"),
                    new XElement(worksheetNs + "f", "[@Price]*[@Qty]"),
                    new XElement(worksheetNs + "v", "50"))),
            new XElement(
                worksheetNs + "row",
                new XAttribute("r", "3"),
                new XElement(worksheetNs + "c", new XAttribute("r", "A3"), new XAttribute("t", "str"), new XElement(worksheetNs + "v", "Beta")),
                new XElement(worksheetNs + "c", new XAttribute("r", "B3"), new XElement(worksheetNs + "v", "20")),
                new XElement(worksheetNs + "c", new XAttribute("r", "C3"), new XElement(worksheetNs + "v", "3")),
                new XElement(
                    worksheetNs + "c",
                    new XAttribute("r", "D3"),
                    new XElement(worksheetNs + "f", "[@Price]*[@Qty]"),
                    new XElement(worksheetNs + "v", "60"))));

    private static XElement CreateCrossSheetSummarySheetData(XNamespace worksheetNs) =>
        new(
            worksheetNs + "sheetData",
            new XElement(
                worksheetNs + "row",
                new XAttribute("r", "1"),
                new XElement(worksheetNs + "c", new XAttribute("r", "A1"), new XAttribute("t", "str"), new XElement(worksheetNs + "v", "Region")),
                new XElement(worksheetNs + "c", new XAttribute("r", "B1"), new XAttribute("t", "str"), new XElement(worksheetNs + "v", "Total"))),
            new XElement(
                worksheetNs + "row",
                new XAttribute("r", "2"),
                new XElement(worksheetNs + "c", new XAttribute("r", "A2"), new XAttribute("t", "str"), new XElement(worksheetNs + "v", "North")),
                new XElement(
                    worksheetNs + "c",
                    new XAttribute("r", "B2"),
                    new XElement(worksheetNs + "f", "SUMIF(Lookup!A2:A3,\"North\",Lookup!B2:B3)"),
                    new XElement(worksheetNs + "v", "100"))),
            new XElement(
                worksheetNs + "row",
                new XAttribute("r", "3"),
                new XElement(worksheetNs + "c", new XAttribute("r", "A3"), new XAttribute("t", "str"), new XElement(worksheetNs + "v", "South")),
                new XElement(
                    worksheetNs + "c",
                    new XAttribute("r", "B3"),
                    new XElement(worksheetNs + "f", "SUMIF(Lookup!A2:A3,\"South\",Lookup!B2:B3)"),
                    new XElement(worksheetNs + "v", "125"))));

    private static XElement CreateCrossSheetLookupSheetData(XNamespace worksheetNs) =>
        new(
            worksheetNs + "sheetData",
            new XElement(
                worksheetNs + "row",
                new XAttribute("r", "1"),
                new XElement(worksheetNs + "c", new XAttribute("r", "A1"), new XAttribute("t", "str"), new XElement(worksheetNs + "v", "Region")),
                new XElement(worksheetNs + "c", new XAttribute("r", "B1"), new XAttribute("t", "str"), new XElement(worksheetNs + "v", "Sales"))),
            new XElement(
                worksheetNs + "row",
                new XAttribute("r", "2"),
                new XElement(worksheetNs + "c", new XAttribute("r", "A2"), new XAttribute("t", "str"), new XElement(worksheetNs + "v", "North")),
                new XElement(worksheetNs + "c", new XAttribute("r", "B2"), new XElement(worksheetNs + "v", "100"))),
            new XElement(
                worksheetNs + "row",
                new XAttribute("r", "3"),
                new XElement(worksheetNs + "c", new XAttribute("r", "A3"), new XAttribute("t", "str"), new XElement(worksheetNs + "v", "South")),
                new XElement(worksheetNs + "c", new XAttribute("r", "B3"), new XElement(worksheetNs + "v", "125"))));

    private static void InsertWorksheetTerminalElementInOrder(
        XElement worksheetRoot,
        XNamespace worksheetNs,
        XElement element)
    {
        var extLst = worksheetRoot.Elements(worksheetNs + "extLst").FirstOrDefault();
        if (extLst is null)
            worksheetRoot.Add(element);
        else
            extLst.AddBeforeSelf(element);
    }

    private static void ApplyChartSeriesCountFixup(ZipArchive archive)
    {
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace officeRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is not null)
        {
            var contentTypes = LoadPackageXml(contentTypesEntry);
            EnsureContentTypeOverride(contentTypes, "/xl/charts/chart1.xml",
                "application/vnd.openxmlformats-officedocument.drawingml.chart+xml");
            EnsureContentTypeOverride(contentTypes, "/xl/drawings/drawing1.xml",
                "application/vnd.openxmlformats-officedocument.drawing+xml");
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypes);
        }

        var drawingRelId = "rIdFreeXChartSeriesCountDrawing1";
        var worksheetPath = "xl/worksheets/sheet1.xml";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is not null)
        {
            var worksheetXml = LoadPackageXml(worksheetEntry);
            if (worksheetXml.Root?.Element(worksheetNs + "drawing") is null)
            {
                worksheetXml.Root?.Add(new XElement(worksheetNs + "drawing",
                    new XAttribute(officeRelNs + "id", drawingRelId)));
                ReplacePackageXml(archive, worksheetPath, worksheetXml);
            }
        }

        var worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
        var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
            ? LoadPackageXml(worksheetRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        EnsureRelationship(worksheetRelsXml, drawingRelId,
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing",
            "../drawings/drawing1.xml");
        ReplacePackageXml(archive, worksheetRelsPath, worksheetRelsXml);

        var drawingRelsPath = "xl/drawings/_rels/drawing1.xml.rels";
        var drawingRelsXml = archive.GetEntry(drawingRelsPath) is { } drawingRelsEntry
            ? LoadPackageXml(drawingRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        EnsureRelationship(drawingRelsXml, "rIdFreeXChart1",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart",
            "../charts/chart1.xml");
        ReplacePackageXml(archive, drawingRelsPath, drawingRelsXml);

        var drawingPath = "xl/drawings/drawing1.xml";
        if (archive.GetEntry(drawingPath) is null)
        {
            XNamespace xdrNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
            XNamespace aDrawNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
            XNamespace r2Ns = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            var drawingXml = new XDocument(
                new XElement(xdrNs + "wsDr",
                    new XAttribute(XNamespace.Xmlns + "xdr", xdrNs.NamespaceName),
                    new XAttribute(XNamespace.Xmlns + "a", aDrawNs.NamespaceName),
                    new XAttribute(XNamespace.Xmlns + "r", r2Ns.NamespaceName),
                    new XElement(xdrNs + "twoCellAnchor",
                        new XElement(xdrNs + "from",
                            new XElement(xdrNs + "col", "0"), new XElement(xdrNs + "colOff", "0"),
                            new XElement(xdrNs + "row", "1"), new XElement(xdrNs + "rowOff", "0")),
                        new XElement(xdrNs + "to",
                            new XElement(xdrNs + "col", "7"), new XElement(xdrNs + "colOff", "0"),
                            new XElement(xdrNs + "row", "15"), new XElement(xdrNs + "rowOff", "0")),
                        new XElement(xdrNs + "graphicFrame",
                            new XElement(xdrNs + "nvGraphicFramePr",
                                new XElement(xdrNs + "cNvPr", new XAttribute("id", "2"), new XAttribute("name", "Chart 1")),
                                new XElement(xdrNs + "cNvGraphicFramePr")),
                            new XElement(xdrNs + "xfrm"),
                            new XElement(aDrawNs + "graphic",
                                new XElement(aDrawNs + "graphicData",
                                    new XAttribute("uri", "http://schemas.openxmlformats.org/drawingml/2006/chart"),
                                    new XElement(XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/chart") + "chart",
                                        new XAttribute(r2Ns + "id", "rIdFreeXChart1"))))),
                        new XElement(xdrNs + "clientData"))));
            ReplacePackageXml(archive, drawingPath, drawingXml);
        }
    }

}
