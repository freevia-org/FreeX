using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class XlsxFileAdapter
{
    // Worksheet XML layout metadata loading and XLSX XML helper methods.
    private sealed record SheetXmlLayout(
        HashSet<uint> HiddenRows,
        HashSet<uint> HiddenCols,
        bool IsProtected,
        string? ProtectionPasswordHash,
        NativeXmlPreserveBag? ProtectionMetadata,
        IReadOnlyList<SheetProtectionPermission> ProtectionPermissions,
        IReadOnlyList<GridRange> AllowEditRanges,
        Dictionary<GridRange, string?> AllowEditRangePasswords,
        IReadOnlyList<GridRange> MergedRegions,
        WorksheetViewMode ViewMode,
        bool ShowGridlines,
        bool ShowHeadings,
        bool ShowRulers,
        int ZoomPercent,
        bool ShowFormulas,
        bool ShowZeros,
        bool IsRightToLeft,
        double? DefaultColumnWidth,
        double? DefaultRowHeight,
        NativeXmlPreserveBag? SheetFormatMetadata,
        NativeXmlPreserveBag? DimensionMetadata,
        NativeXmlPreserveBag? SheetPropertiesMetadata,
        bool FullCalculationOnLoad,
        WorksheetPhoneticProperties? PhoneticProperties,
        string? PaneState,
        uint? PaneRowSplit,
        uint? PaneColumnSplit,
        uint? ViewTopRow,
        uint? ViewLeftCol,
        uint? ActiveRow,
        uint? ActiveCol,
        NativeXmlPreserveBag? PrintOptionsMetadata,
        WorksheetAutoFilterModel? AutoFilter,
        bool? UsePrinterDefaults,
        int? PrintCopies,
        NativeXmlPreserveBag? PageMarginsMetadata,
        bool? FitToPage,
        bool? AutoPageBreaks,
        int? PrintQualityDpi,
        int? PrintQualityVerticalDpi,
        NativeXmlPreserveBag? PageSetupMetadata,
        NativeXmlPreserveBag? HeaderFooterMetadata,
        WorksheetBackgroundImage? BackgroundImage,
        XlsxHeaderFooterPictureSets HeaderFooterPictures,
        Dictionary<uint, int> RowOutlineLevels,
        Dictionary<uint, int> ColOutlineLevels,
        bool? OutlineSummaryBelow,
        bool? OutlineSummaryRight,
        bool? ShowOutlineSymbols,
        bool? ApplyOutlineStyles,
        HashSet<uint> GroupHiddenRows,
        HashSet<uint> GroupHiddenCols,
        HashSet<uint> CollapsedAnchorRows,
        HashSet<uint> CollapsedAnchorCols,
        Dictionary<uint, double> RowHeights,
        Dictionary<uint, double> ColumnWidths,
        HashSet<uint> StyledRows,
        HashSet<uint> StyledColumns,
        IReadOnlyList<(uint Row, uint Col, string Text, string Author)> Comments,
        IReadOnlyList<(uint Row, uint Col)> ShownCommentAddresses,
        IReadOnlyList<(uint Row, uint Col, ThreadedComment Comment)> ThreadedComments,
        IReadOnlyList<XlsxChartPackagePart> ChartParts,
        IReadOnlyList<XlsxPicturePackagePart> PictureParts,
        IReadOnlyList<XlsxTextBoxPackagePart> TextBoxParts,
        IReadOnlyList<XlsxShapePackagePart> ShapeParts,
        IReadOnlyList<XlsxSparklineLayout> Sparklines,
        IReadOnlyList<FormControlModel> FormControls,
        IReadOnlyList<ConditionalFormat> AdvancedConditionalFormats,
        IReadOnlyList<int> ClassicConditionalFormatPriorities,
        IReadOnlyList<IReadOnlyDictionary<string, string>?> ClassicConditionalFormatContainerAttributes,
        IReadOnlyList<DataValidationNativeMetadata> DataValidationNativeMetadata,
        IReadOnlyList<X14DataValidationMetadata> X14DataValidations,
        IgnoredErrorLayout IgnoredErrors,
        WorksheetIgnoredErrorsMetadataModel? IgnoredErrorsMetadata,
        IReadOnlyList<CellAddress> CellWatches,
        WorksheetCellWatchesMetadataModel? CellWatchesMetadata,
        IReadOnlyList<WorkbookScenario> Scenarios,
        IReadOnlyList<XlsxWorksheetCustomViewState> CustomViews,
        IReadOnlyList<WorksheetCustomProperty> CustomProperties,
        WorksheetSmartTagsModel? SmartTags,
        WorksheetDataConsolidationModel? DataConsolidation,
        WorksheetSortStateModel? SortState,
        WorksheetSingleXmlCellsModel? SingleXmlCells,
        WorksheetAdditionalViewsModel? AdditionalViews,
        NativeXmlPreserveBag? PrimaryViewMetadata,
        WorksheetPageBreaksMetadataModel? RowPageBreaksMetadata,
        WorksheetPageBreaksMetadataModel? ColumnPageBreaksMetadata,
        Dictionary<(uint Row, uint Col), ErrorValue> CachedFormulaErrors,
        int PopulatedCellCount,
        bool HasStyleOnlyCells,
        IReadOnlyList<(uint Row, uint Col, int StyleIndex)> ExplicitPopulatedCellStyles,
        IReadOnlyList<(uint Row, uint Col, int StyleIndex)> ExplicitStyleOnlyCells,
        bool HasDuplicateStyleOnlyCellStyleIndexes,
        IReadOnlyList<(uint Row, uint Col)> SharedStringValueCells,
        string WorksheetPath,
        bool HasConditionalFormattingBlocks,
        bool HasPreservableSourceWorksheetMetadata,
        bool HasClosedXmlUnsupportedConditionalFormatting,
        bool HasUnsupportedConditionalFormatting,
        bool HasWorksheetDynamicFilters,
        bool HasWorksheetRelationshipMarkerSchemaIssues,
        bool HasWorksheetPageLayoutSchemaIssues,
        bool HasWorksheetPageBreakSchemaIssues,
        bool HasWorksheetAutoFilterSchemaIssues,
        bool HasWorksheetSheetViewSchemaIssues,
        bool HasWorksheetNativeMetadataSchemaIssues,
        IReadOnlyList<string> TableRelationshipIds,
        string? CodeName);

    private static Dictionary<string, SheetXmlLayout> LoadSheetXmlLayout(
        Stream xlsxStream,
        XDocument? stylesXml,
        WorkbookTheme workbookTheme,
        WorkbookIndexedColorPalette indexedColors,
        bool loadStructuredTableMetadata,
        out StructuredTablePackageMetadata structuredTableMetadata,
        List<string>? warnings = null)
    {
        var result = new Dictionary<string, SheetXmlLayout>(StringComparer.OrdinalIgnoreCase);
        structuredTableMetadata = StructuredTablePackageMetadata.Empty;

        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
            if (workbookEntry is null || relsEntry is null)
                return result;

            var workbookXml = LoadXml(workbookEntry);
            var relsXml = LoadXml(relsEntry);

            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

            var relTargets = XlsxRelationshipReader.ReadTargets(
                relsXml,
                packageRelNs,
                XlsxPackagePath.NormalizeWorkbookTarget);

            var differentialStyles = XlsxDifferentialStyleReader.ReadAll(stylesXml, workbookNs, workbookTheme, indexedColors);
            Dictionary<string, string>? worksheetPathsBySheetName = loadStructuredTableMetadata
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : null;
            Dictionary<string, IReadOnlyList<string>>? tableRelationshipIdsBySheetName = loadStructuredTableMetadata
                ? new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                : null;

            foreach (var sheetElement in workbookXml.Root?.Element(workbookNs + "sheets")?.Elements(workbookNs + "sheet") ?? [])
            {
                var name = sheetElement.Attribute("name")?.Value;
                var relId = sheetElement.Attribute(relNs + "id")?.Value;
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(relId))
                    continue;
                if (!relTargets.TryGetValue(relId, out var worksheetPath))
                    continue;

                var worksheetEntry = archive.GetEntry(worksheetPath);
                if (worksheetEntry is null)
                    continue;

                // R106-io-sheet-xml-layout-isolation-1: isolate each sheet's layout read so a
                // failure parsing ONE sheet's metadata (background image / header-footer pictures /
                // drawing parts / sparklines / form controls / structured tables / advanced CF /
                // data-validation native metadata / x14 DV / ignored errors / etc.) only skips that
                // one sheet's entry, instead of aborting the whole dictionary build and silently
                // leaving every sheet AFTER it in document order with no entry at all -- which
                // downstream disables both XlsxDataValidationNativeMetadataMapper.Apply (multi-area
                // rule dedup) and XlsxX14DataValidationReader.Apply (cross-sheet/long List source
                // merge) for every one of those later sheets.
                try
                {
                    var layout = ReadHiddenSheetLayout(archive, worksheetPath, worksheetEntry, stylesXml, differentialStyles, workbookTheme, indexedColors);
                    result[name] = layout;
                    if (loadStructuredTableMetadata)
                    {
                        worksheetPathsBySheetName![name] = worksheetPath;
                        if (layout.TableRelationshipIds.Count > 0)
                            tableRelationshipIdsBySheetName![name] = layout.TableRelationshipIds;
                    }
                }
                catch (Exception ex)
                {
                    warnings?.Add($"[worksheet-xml-metadata] Sheet '{name}': {ex.Message}");
                }
            }

            if (loadStructuredTableMetadata)
            {
                structuredTableMetadata = XlsxStructuredTableMetadataReader.Load(
                    archive,
                    worksheetPathsBySheetName,
                    tableRelationshipIdsBySheetName);
            }
        }
        catch (Exception ex)
        {
            warnings?.Add($"[worksheet-xml-metadata]: {ex.Message}");
        }

        return result;
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        return XlsxPackageXmlEditor.LoadXml(entry);
    }

    private static void ReplacePackageXml(ZipArchive archive, string entryName, XDocument document)
        => XlsxPackageXmlEditor.ReplaceXml(archive, entryName, document);

    private static Dictionary<string, string> LoadRelationshipTargets(
        ZipArchive archive,
        string relsPath,
        string sourcePart,
        XNamespace packageRelNs) =>
        XlsxRelationshipReader.LoadTargets(archive, relsPath, sourcePart, packageRelNs);

    private static WorksheetAutoFilterModel? ReadWorksheetAutoFilter(
        XElement? autoFilter,
        IReadOnlyList<CellStyle>? differentialStyles) =>
        XlsxWorksheetAutoFilterMapper.Read(autoFilter, differentialStyles);

    private static CfThresholdType FromCfvoType(string? type) =>
        XlsxAdvancedConditionalFormatMetadata.FromCfvoType(type);

    private static void EnsureContentType(ZipArchive archive, string extension, string contentType)
        => XlsxPackageXmlEditor.EnsureDefaultContentType(archive, extension, contentType);

    private static void EnsureSpecificContentType(ZipArchive archive, string partName, string contentType)
        => XlsxPackageXmlEditor.EnsureSpecificContentType(archive, partName, contentType);

    private static SheetXmlLayout ReadHiddenSheetLayout(
        ZipArchive archive,
        string worksheetPath,
        ZipArchiveEntry worksheetEntry,
        XDocument? stylesXml,
        IReadOnlyList<CellStyle> differentialStyles,
        WorkbookTheme workbookTheme,
        WorkbookIndexedColorPalette indexedColors)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XDocument worksheetXml;
        XlsxWorksheetSheetDataLayout sheetDataLayout;
        if (TryLoadWorksheetXmlWithoutSheetData(
                worksheetEntry,
                worksheetNs,
                out var prunedWorksheetXml,
                out var streamedSheetDataLayout))
        {
            worksheetXml = prunedWorksheetXml;
            sheetDataLayout = MergeColumnLayout(worksheetXml, worksheetNs, streamedSheetDataLayout);
        }
        else
        {
            worksheetXml = LoadXml(worksheetEntry);
            sheetDataLayout = XlsxWorksheetRowColumnLayoutReader.ReadSheetDataLayout(worksheetXml, worksheetNs);
        }

        var rowColumnLayout = sheetDataLayout.RowColumnLayout;
        var cellLayout = sheetDataLayout.CellLayout;

        var protection = worksheetXml.Root?.Element(worksheetNs + "sheetProtection");
        var isProtected = IsTruthy(protection?.Attribute("sheet")?.Value);
        var passwordHash = ReadSheetProtectionPasswordHash(protection);
        var protectionMetadata = XlsxWorksheetLayoutMetadataReader.ReadWorksheetProtectionMetadata(protection);
        var protectionPermissions = XlsxSheetProtectionPermissionMapper.Read(protection);
        var allowEditRanges = XlsxAllowEditRangeMapper.Read(worksheetXml, worksheetNs, out var allowEditRangePasswords);
        var mergedRegions = ReadMergedRegions(worksheetXml, worksheetNs);

        var sheetView = FindPrimarySheetView(worksheetXml, worksheetNs);
        var sheetCalcPr = worksheetXml.Root?.Element(worksheetNs + "sheetCalcPr");
        var dimension = worksheetXml.Root?.Element(worksheetNs + "dimension");
        var sheetFormatPr = worksheetXml.Root?.Element(worksheetNs + "sheetFormatPr");
        var sheetPr = worksheetXml.Root?.Element(worksheetNs + "sheetPr");
        var pageSetUpPr = sheetPr?.Element(worksheetNs + "pageSetUpPr");
        var outlinePr = sheetPr?.Element(worksheetNs + "outlinePr");
        XlsxWorksheetRowColumnLayoutReader.ClassifyCollapsedOutlineHidden(
            rowColumnLayout,
            ParseOptionalBool(outlinePr?.Attribute("summaryBelow")?.Value) ?? true,
            ParseOptionalBool(outlinePr?.Attribute("summaryRight")?.Value) ?? true);
        var pageSetup = worksheetXml.Root?.Element(worksheetNs + "pageSetup");
        var headerFooter = worksheetXml.Root?.Element(worksheetNs + "headerFooter");
        var pageMargins = worksheetXml.Root?.Element(worksheetNs + "pageMargins");
        var printOptions = worksheetXml.Root?.Element(worksheetNs + "printOptions");
        var rowBreaks = worksheetXml.Root?.Element(worksheetNs + "rowBreaks");
        var colBreaks = worksheetXml.Root?.Element(worksheetNs + "colBreaks");
        var phoneticPr = worksheetXml.Root?.Element(worksheetNs + "phoneticPr");
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var pane = sheetView?.Element(worksheetNs + "pane");
        var viewTopLeft = ParseOptionalCellReference(sheetView?.Attribute("topLeftCell")?.Value);
        var activeCell = ReadActiveSelectionCell(sheetView, pane, worksheetNs);
        var background = XlsxWorksheetBackgroundReaderWriter.Read(archive, worksheetPath, worksheetXml);
        var headerFooterPictures = XlsxHeaderFooterPictureReaderWriter.Read(archive, worksheetPath, worksheetXml);
        var drawingParts = XlsxWorksheetDrawingPartReader.ReadParts(archive, worksheetPath, worksheetXml);
        var sparklines = XlsxSparklineMapper.Read(worksheetXml, workbookTheme, indexedColors);
        var formControls = XlsxFormControlMapper.ReadWorksheet(archive, worksheetPath, worksheetXml);
        var advancedConditionalFormats = ReadAdvancedConditionalFormats(
            worksheetXml, worksheetNs, differentialStyles, workbookTheme, indexedColors,
            out var classicConditionalFormatPriorities, out var classicConditionalFormatContainerAttributes);
        var dataValidationNativeMetadata = XlsxDataValidationNativeMetadataMapper.Read(worksheetXml, worksheetNs);
        var x14DataValidations = XlsxX14DataValidationReader.Read(worksheetXml);
        var ignoredErrors = XlsxWorksheetDiagnosticsMapper.ReadIgnoredErrors(worksheetXml, worksheetNs);
        var ignoredErrorsMetadata = XlsxWorksheetDiagnosticsMapper.ReadIgnoredErrorsMetadata(worksheetXml, worksheetNs);
        var cellWatches = XlsxWorksheetDiagnosticsMapper.ReadCellWatches(worksheetXml, worksheetNs);
        var cellWatchesMetadata = XlsxWorksheetDiagnosticsMapper.ReadCellWatchesMetadata(worksheetXml, worksheetNs);
        var scenarios = XlsxWorksheetScenarioMapper.Read(worksheetXml, worksheetNs);
        var customViews = XlsxCustomViewMapper.ReadWorksheetViews(worksheetXml, worksheetNs);
        var customProperties = XlsxWorksheetCustomPropertyMapper.Read(worksheetXml, worksheetNs, archive, worksheetPath);
        var smartTags = XlsxWorksheetSmartTagMapper.Read(worksheetXml.Root?.Element(worksheetNs + "smartTags"));
        var dataConsolidation = XlsxWorksheetDataConsolidationMapper.Read(worksheetXml.Root?.Element(worksheetNs + "dataConsolidate"));
        var sortState = XlsxWorksheetSortStateMapper.Read(worksheetXml.Root?.Element(worksheetNs + "sortState"));
        var singleXmlCells = XlsxWorksheetSingleXmlCellMapper.Read(
            archive,
            worksheetPath,
            worksheetXml.Root?.Element(worksheetNs + "singleXmlCells"));
        var additionalViews = XlsxWorksheetAdditionalViewMapper.Read(worksheetXml.Root?.Element(worksheetNs + "sheetViews"));
        var autoFilter = ReadWorksheetAutoFilter(worksheetXml.Root?.Element(worksheetNs + "autoFilter"), differentialStyles);
        var hasWorksheetDynamicFilters = HasDynamicFilter(autoFilter);
        var comments = XlsxWorksheetCommentReader.Read(archive, worksheetPath);
        var shownCommentAddresses = XlsxWorksheetCommentVisibilityReader.Read(archive, worksheetPath, worksheetXml, worksheetNs);
        var threadedComments = XlsxWorksheetThreadedCommentMapper.Read(archive, worksheetPath);
        var codeName = sheetPr?.Attribute("codeName")?.Value;
        var hasPreservableSourceWorksheetMetadata = HasRetainedWorksheetMetadataElement(worksheetXml.Root, worksheetNs) ||
            XlsxWorksheetMetadataPreserver.HasPreservableSourceWorksheetMetadata(worksheetXml, worksheetNs) ||
            sheetDataLayout.HasPreservableSourceSheetDataMetadata;
        var hasConditionalFormattingBlocks =
            worksheetXml.Root?.Elements(worksheetNs + "conditionalFormatting").Any() == true;
        var hasClosedXmlUnsupportedConditionalFormatting =
            XlsxConditionalFormatRuleSupport.HasUnsupportedRule(worksheetXml, worksheetNs, allowBlankType: false);
        var hasUnsupportedConditionalFormatting =
            XlsxConditionalFormatRuleSupport.HasUnsupportedRule(worksheetXml, worksheetNs, allowBlankType: true);
        var hasWorksheetRelationshipMarkerSchemaIssues =
            worksheetXml.Root is { } worksheetRoot &&
            XlsxWorksheetRelationshipMarkerNormalizer.NormalizeWorksheetRoot(new XElement(worksheetRoot));
        // Compute worksheet-scoped schema hints using the pruned root (sheetData children stripped).
        // Each check is run on a clone so the normalizer mutations don't affect the layout's own root.
        // These normalizers only inspect structural elements outside sheetData, so the pruned root
        // gives the same result as the full root for these checks.
        var hasWorksheetPageLayoutSchemaIssues =
            worksheetXml.Root is { } pageLayoutRoot &&
            XlsxWorksheetPageLayoutNormalizer.NormalizeWorksheetRoot(new XElement(pageLayoutRoot));
        var hasWorksheetPageBreakSchemaIssues =
            worksheetXml.Root is { } pageBreakRoot &&
            XlsxWorksheetPageBreakNormalizer.NormalizeWorksheetRoot(new XElement(pageBreakRoot));
        var hasWorksheetAutoFilterSchemaIssues =
            worksheetXml.Root?.Element(worksheetNs + "autoFilter") is { } autoFilterElement &&
            XlsxWorksheetAutoFilterNormalizer.NormalizeElement(new XElement(autoFilterElement));
        var hasWorksheetSheetViewSchemaIssues =
            worksheetXml.Root?.Element(worksheetNs + "sheetViews") is { } sheetViewsElement &&
            XlsxWorksheetSheetViewNormalizer.NormalizeSheetViewsElement(new XElement(sheetViewsElement));
        var hasWorksheetNativeMetadataSchemaIssues =
            worksheetXml.Root is { } nativeMetadataRoot &&
            XlsxClosedXmlLoadPackageSanitizer.NormalizeWorksheetNativeMetadataRoot(new XElement(nativeMetadataRoot));
        var tableRelationshipIds = ReadTableRelationshipIds(worksheetXml, worksheetNs, relNs);

        return new SheetXmlLayout(
            rowColumnLayout.HiddenRows,
            rowColumnLayout.HiddenCols,
            isProtected,
            passwordHash,
            protectionMetadata,
            protectionPermissions,
            allowEditRanges,
            allowEditRangePasswords,
            mergedRegions,
            ParseWorksheetViewMode(sheetView?.Attribute("view")?.Value),
            !IsFalse(sheetView?.Attribute("showGridLines")?.Value),
            !IsFalse(sheetView?.Attribute("showRowColHeaders")?.Value),
            !IsFalse(sheetView?.Attribute("showRuler")?.Value),
            ParseZoomPercent(sheetView?.Attribute("zoomScale")?.Value),
            IsTruthy(sheetView?.Attribute("showFormulas")?.Value),
            !IsFalse(sheetView?.Attribute("showZeros")?.Value),
            IsTruthy(sheetView?.Attribute("rightToLeft")?.Value),
            XlsxWorksheetXmlValueParser.ParsePositiveFiniteDouble(
                sheetFormatPr?.Attribute("defaultColWidth")?.Value),
            ReadDefaultRowHeight(sheetFormatPr, stylesXml),
            XlsxWorksheetLayoutMetadataReader.ReadWorksheetSheetFormatMetadata(sheetFormatPr),
            XlsxWorksheetLayoutMetadataReader.ReadWorksheetDimensionMetadata(dimension),
            XlsxWorksheetLayoutMetadataReader.ReadWorksheetSheetPropertiesMetadata(sheetPr),
            XlsxWorksheetCalculationPropertyMapper.ReadFullCalculationOnLoad(sheetCalcPr),
            XlsxWorksheetPhoneticPropertyMapper.Read(phoneticPr),
            pane?.Attribute("state")?.Value,
            ParsePaneSplit(pane?.Attribute("ySplit")?.Value),
            ParsePaneSplit(pane?.Attribute("xSplit")?.Value),
            viewTopLeft?.Row,
            viewTopLeft?.Col,
            activeCell?.Row,
            activeCell?.Col,
            XlsxWorksheetLayoutMetadataReader.ReadWorksheetPrintOptionsMetadata(printOptions),
            autoFilter,
            ParseOptionalBool(pageSetup?.Attribute("usePrinterDefaults")?.Value),
            ParseOptionalPositiveInt(pageSetup?.Attribute("copies")?.Value),
            XlsxWorksheetLayoutMetadataReader.ReadWorksheetPageMarginsMetadata(pageMargins),
            ParseOptionalBool(pageSetUpPr?.Attribute("fitToPage")?.Value),
            ParseOptionalBool(pageSetUpPr?.Attribute("autoPageBreaks")?.Value),
            ParseOptionalPositiveInt(pageSetup?.Attribute("horizontalDpi")?.Value),
            ParseOptionalPositiveInt(pageSetup?.Attribute("verticalDpi")?.Value),
            XlsxWorksheetLayoutMetadataReader.ReadWorksheetPageSetupMetadata(pageSetup),
            XlsxWorksheetLayoutMetadataReader.ReadWorksheetHeaderFooterMetadata(headerFooter),
            background,
            headerFooterPictures,
            rowColumnLayout.RowOutlineLevels,
            rowColumnLayout.ColOutlineLevels,
            ParseOptionalBool(outlinePr?.Attribute("summaryBelow")?.Value),
            ParseOptionalBool(outlinePr?.Attribute("summaryRight")?.Value),
            ParseOptionalBool(outlinePr?.Attribute("showOutlineSymbols")?.Value),
            ParseOptionalBool(outlinePr?.Attribute("applyStyles")?.Value),
            rowColumnLayout.GroupHiddenRows,
            rowColumnLayout.GroupHiddenCols,
            rowColumnLayout.CollapsedAnchorRows ?? [],
            rowColumnLayout.CollapsedAnchorCols ?? [],
            rowColumnLayout.RowHeights,
            rowColumnLayout.ColumnWidths,
            rowColumnLayout.StyledRows ?? [],
            rowColumnLayout.StyledColumns ?? [],
            comments,
            shownCommentAddresses,
            threadedComments,
            drawingParts.ChartParts,
            drawingParts.PictureParts,
            drawingParts.TextBoxParts,
            drawingParts.ShapeParts,
            sparklines,
            formControls,
            advancedConditionalFormats,
            classicConditionalFormatPriorities,
            classicConditionalFormatContainerAttributes,
            dataValidationNativeMetadata,
            x14DataValidations,
            ignoredErrors,
            ignoredErrorsMetadata,
            cellWatches,
            cellWatchesMetadata,
            scenarios,
            customViews,
            customProperties,
            smartTags,
            dataConsolidation,
            sortState,
            singleXmlCells,
            additionalViews,
            XlsxWorksheetLayoutMetadataReader.ReadWorksheetPrimaryViewMetadata(sheetView),
            XlsxWorksheetPageBreaksMetadataReader.Read(rowBreaks, CellAddress.MaxRow),
            XlsxWorksheetPageBreaksMetadataReader.Read(colBreaks, CellAddress.MaxCol),
            cellLayout.CachedFormulaErrors,
            cellLayout.PopulatedCellCount,
            cellLayout.HasStyleOnlyCells,
            cellLayout.ExplicitPopulatedCellStyles,
            cellLayout.ExplicitStyleOnlyCells,
            cellLayout.HasDuplicateStyleOnlyCellStyleIndexes,
            cellLayout.SharedStringValueCells,
            worksheetPath,
            hasConditionalFormattingBlocks,
            hasPreservableSourceWorksheetMetadata,
            hasClosedXmlUnsupportedConditionalFormatting,
            hasUnsupportedConditionalFormatting,
            hasWorksheetDynamicFilters,
            hasWorksheetRelationshipMarkerSchemaIssues,
            hasWorksheetPageLayoutSchemaIssues,
            hasWorksheetPageBreakSchemaIssues,
            hasWorksheetAutoFilterSchemaIssues,
            hasWorksheetSheetViewSchemaIssues,
            hasWorksheetNativeMetadataSchemaIssues,
            tableRelationshipIds,
            codeName);
    }

    private static IReadOnlyList<string> ReadTableRelationshipIds(
        XDocument worksheetXml,
        XNamespace worksheetNs,
        XNamespace relNs)
    {
        var tableParts = worksheetXml.Root?.Element(worksheetNs + "tableParts");
        if (tableParts is null)
            return [];

        List<string>? result = null;
        foreach (var tablePart in tableParts.Elements(worksheetNs + "tablePart"))
        {
            var relId = tablePart.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(relId))
                continue;

            result ??= [];
            result.Add(relId);
        }

        return result ?? [];
    }

    private static IReadOnlyList<GridRange> ReadMergedRegions(
        XDocument worksheetXml,
        XNamespace worksheetNs)
    {
        var mergeCells = worksheetXml.Root?.Element(worksheetNs + "mergeCells");
        if (mergeCells is null)
            return [];

        var tempSheet = SheetId.New();
        List<GridRange>? result = null;
        foreach (var mergeCell in mergeCells.Elements(worksheetNs + "mergeCell"))
        {
            var reference = mergeCell.Attribute("ref")?.Value;
            if (string.IsNullOrWhiteSpace(reference) ||
                !TryParseSqrefToken(reference, tempSheet, out var range) ||
                range.CellCount <= 1)
            {
                continue;
            }

            result ??= [];
            result.Add(range);
        }

        return result ?? [];
    }

    private static bool TryLoadWorksheetXmlWithoutSheetData(
        ZipArchiveEntry worksheetEntry,
        XNamespace worksheetNs,
        out XDocument worksheetXml,
        out XlsxWorksheetSheetDataLayout sheetDataLayout)
    {
        worksheetXml = new XDocument();
        sheetDataLayout = CreateEmptySheetDataLayout();

        try
        {
            using var stream = worksheetEntry.Open();
            using var reader = XmlReader.Create(stream, SecureXmlReaderSettings.Create());
            reader.MoveToContent();
            if (reader.NodeType != XmlNodeType.Element ||
                reader.LocalName != "worksheet" ||
                !string.Equals(reader.NamespaceURI, worksheetNs.NamespaceName, StringComparison.Ordinal))
            {
                return false;
            }

            var root = XmlReaderElementMaterializer.CreateShallowElement(reader);
            worksheetXml.Add(root);
            if (reader.IsEmptyElement)
                return true;

            var worksheetDepth = reader.Depth;
            var readNext = true;
            while (true)
            {
                if (readNext && !reader.Read())
                    break;
                readNext = true;

                if (reader.NodeType == XmlNodeType.EndElement &&
                    reader.Depth == worksheetDepth)
                {
                    break;
                }

                if (reader.NodeType != XmlNodeType.Element ||
                    reader.Depth != worksheetDepth + 1)
                {
                    continue;
                }

                if (reader.LocalName == "sheetData" &&
                    string.Equals(reader.NamespaceURI, worksheetNs.NamespaceName, StringComparison.Ordinal))
                {
                    root.Add(XmlReaderElementMaterializer.CreateShallowElement(reader));
                    sheetDataLayout = XlsxWorksheetRowColumnLayoutReader.ReadSheetDataLayout(
                        reader,
                        worksheetNs,
                        detectPreservableSourceSheetDataMetadata: true);
                    continue;
                }

                if (XNode.ReadFrom(reader) is XElement child)
                {
                    root.Add(child);
                    readNext = false;
                }
            }

            return true;
        }
        catch
        {
            worksheetXml = new XDocument();
            sheetDataLayout = CreateEmptySheetDataLayout();
            return false;
        }
    }

    private static XlsxWorksheetSheetDataLayout MergeColumnLayout(
        XDocument worksheetXml,
        XNamespace worksheetNs,
        XlsxWorksheetSheetDataLayout sheetDataLayout)
    {
        var columnLayout = XlsxWorksheetRowColumnLayoutReader.Read(worksheetXml, worksheetNs);
        return sheetDataLayout with
        {
            RowColumnLayout = sheetDataLayout.RowColumnLayout with
            {
                HiddenCols = columnLayout.HiddenCols,
                ColOutlineLevels = columnLayout.ColOutlineLevels,
                GroupHiddenCols = columnLayout.GroupHiddenCols,
                CollapsedAnchorCols = columnLayout.CollapsedAnchorCols,
                ColumnWidths = columnLayout.ColumnWidths,
                StyledColumns = columnLayout.StyledColumns
            }
        };
    }

    private static XlsxWorksheetSheetDataLayout CreateEmptySheetDataLayout() =>
        new(
            new XlsxWorksheetRowColumnLayout([], [], [], [], [], [], [], []),
            new XlsxWorksheetCellLayout([], [], [], false, false, 0, []));

    private static bool HasDynamicFilter(WorksheetAutoFilterModel? autoFilter)
    {
        if (autoFilter is null)
            return false;

        foreach (var filterColumn in autoFilter.FilterColumns)
            if (filterColumn.DynamicFilter is not null)
                return true;

        return false;
    }

    private static bool HasRetainedWorksheetMetadataElement(XElement? root, XNamespace worksheetNs) =>
        root is not null &&
        (root.Element(worksheetNs + "customSheetViews") is not null ||
         root.Element(worksheetNs + "scenarios") is not null ||
         root.Element(worksheetNs + "ignoredErrors") is not null ||
         root.Element(worksheetNs + "cellWatches") is not null ||
         root.Element(worksheetNs + "sheetCalcPr") is not null ||
         root.Element(worksheetNs + "phoneticPr") is not null ||
         root.Element(worksheetNs + "sortState") is not null ||
         root.Element(worksheetNs + "dataConsolidate") is not null ||
         root.Element(worksheetNs + "legacyDrawing") is not null ||
         root.Element(worksheetNs + "legacyDrawingHF") is not null ||
         root.Element(worksheetNs + "picture") is not null ||
         root.Element(worksheetNs + "customProperties") is not null ||
         root.Element(worksheetNs + "smartTags") is not null ||
         root.Element(worksheetNs + "singleXmlCells") is not null ||
         root.Element(worksheetNs + "autoFilter") is not null ||
         root.Element(worksheetNs + "protectedRanges") is not null ||
         root.Element(worksheetNs + "rowBreaks") is not null ||
         root.Element(worksheetNs + "colBreaks") is not null ||
         root.Element(worksheetNs + "webPublishItems") is not null ||
         root.Element(worksheetNs + "oleObjects") is not null ||
         root.Element(worksheetNs + "controls") is not null ||
         root.Element(worksheetNs + "mergeCells") is not null ||
         root.Element(worksheetNs + "sheetProtection") is not null ||
         root.Element(worksheetNs + "hyperlinks") is not null ||
         root.Element(worksheetNs + "extLst") is not null);

    private static bool TryParseSqrefToken(string token, SheetId sheet, out GridRange range)
    {
        range = default;
        var parts = token.Split(':');
        if (parts.Length == 1)
        {
            if (!CellAddress.TryParse(parts[0], sheet, out var address))
                return false;

            range = new GridRange(address, address);
            return true;
        }

        if (parts.Length == 2 &&
            CellAddress.TryParse(parts[0], sheet, out var start) &&
            CellAddress.TryParse(parts[1], sheet, out var end))
        {
            range = new GridRange(start, end);
            return true;
        }

        return false;
    }

    private static uint? ParsePaneSplit(string? value)
        => XlsxWorksheetXmlValueParser.ParsePaneSplit(value);

    private static CellAddress? ParseOptionalCellReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return null;

        return CellAddress.TryParse(reference.Split(':')[0], SheetId.New(), out var address)
            ? address
            : null;
    }

    /// <summary>
    /// Reads the legacy 4-hex <c>password</c> attribute when present, otherwise falls back to the
    /// modern ISO 29500 salted/iterated hash (<c>algorithmName</c>/<c>hashValue</c>/<c>saltValue</c>/
    /// <c>spinCount</c>) Excel writes by default since Excel 2013 — encoded so
    /// <see cref="ProtectionPasswordHelper.VerifyStoredPassword"/> can verify against it. Returns null
    /// when neither scheme is present (protected with no password at all).
    /// </summary>
    private static string? ReadSheetProtectionPasswordHash(XElement? protection)
    {
        var legacyPassword = protection?.Attribute("password")?.Value;
        if (!string.IsNullOrEmpty(legacyPassword))
            return legacyPassword;

        var hashValue = protection?.Attribute("hashValue")?.Value;
        if (string.IsNullOrEmpty(hashValue))
            return null;

        return ProtectionPasswordHelper.EncodeIso29500Hash(
            protection?.Attribute("algorithmName")?.Value,
            protection?.Attribute("spinCount")?.Value,
            protection?.Attribute("saltValue")?.Value,
            hashValue);
    }

    private static bool IsTruthy(string? value) =>
        XlsxWorksheetXmlValueParser.IsTruthy(value);

    private static bool IsFalse(string? value) =>
        XlsxWorksheetXmlValueParser.IsFalse(value);

    private static bool? ParseOptionalBool(string? value)
    {
        if (IsTruthy(value))
            return true;
        if (IsFalse(value))
            return false;
        return null;
    }

    private static int? ParseOptionalPositiveInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : null;

    private static int ParseZoomPercent(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var zoom) && zoom is >= 10 and <= 400 ? zoom : 100;

    private static double? ReadDefaultRowHeight(XElement? sheetFormatPr, XDocument? stylesXml)
    {
        if (XlsxWorksheetXmlValueParser.ParsePositiveFiniteDouble(
            sheetFormatPr?.Attribute("defaultRowHeight")?.Value) is not { } rowHeightPoints)
        {
            return null;
        }

        if (!IsTruthy(sheetFormatPr?.Attribute("customHeight")?.Value) &&
            IsAptosNarrowNormalStyle(stylesXml) &&
            Math.Abs(rowHeightPoints - 15.0) < 0.001)
        {
            rowHeightPoints = 14.5;
        }

        return Math.Round(rowHeightPoints * (96.0 / 72.0), MidpointRounding.AwayFromZero);
    }

    private static bool IsAptosNarrowNormalStyle(XDocument? stylesXml)
    {
        var (fontName, fontSize) = ReadNormalStyleFont(stylesXml);
        return string.Equals(fontName, "Aptos Narrow", StringComparison.OrdinalIgnoreCase) &&
            (!fontSize.HasValue || Math.Abs(fontSize.Value - 11.0) < 0.001);
    }

    private static (string? FontName, double? FontSize) ReadNormalStyleFont(XDocument? stylesXml)
    {
        var root = stylesXml?.Root;
        if (root is null)
            return (null, null);

        var ns = root.Name.Namespace;
        var fontId = ReadNormalStyleFontId(root, ns) ?? 0;
        var font = root.Element(ns + "fonts")?
            .Elements(ns + "font")
            .ElementAtOrDefault(fontId);
        if (font is null)
            return (null, null);

        var fontName = font.Element(ns + "name")?.Attribute("val")?.Value;
        var fontSize = XlsxWorksheetXmlValueParser.ParsePositiveFiniteDouble(
            font.Element(ns + "sz")?.Attribute("val")?.Value);
        return (fontName, fontSize);
    }

    private static int? ReadNormalStyleFontId(XElement stylesRoot, XNamespace ns)
    {
        var normalStyle = FindNormalCellStyle(stylesRoot, ns);

        if (ParseOptionalNonNegativeInt(normalStyle?.Attribute("xfId")?.Value) is { } normalXfId)
        {
            var styleXf = stylesRoot.Element(ns + "cellStyleXfs")?
                .Elements(ns + "xf")
                .ElementAtOrDefault(normalXfId);
            if (ParseOptionalNonNegativeInt(styleXf?.Attribute("fontId")?.Value) is { } styleFontId)
                return styleFontId;
        }

        var defaultCellXf = FindFirstDefaultCellXf(stylesRoot, ns);
        return ParseOptionalNonNegativeInt(defaultCellXf?.Attribute("fontId")?.Value);
    }

    private static XElement? FindPrimarySheetView(XDocument worksheetXml, XNamespace worksheetNs)
    {
        var sheetViews = worksheetXml.Root?.Element(worksheetNs + "sheetViews");
        if (sheetViews is null)
            return null;

        foreach (var sheetView in sheetViews.Elements(worksheetNs + "sheetView"))
        {
            if (IsPrimarySheetView(sheetView))
                return sheetView;
        }

        return null;
    }

    private static CellAddress? ReadActiveSelectionCell(XElement? sheetView, XElement? pane, XNamespace worksheetNs)
    {
        if (sheetView is null)
            return null;

        // When the view is frozen/split into panes, Excel writes one <selection> per pane and
        // marks the pane holding the true cursor via pane/@activePane (defaulting to "topLeft"
        // when no pane element is present). A <selection> with no @pane attribute implicitly
        // belongs to "topLeft". Picking the first <selection> in document order (rather than the
        // one matching the active pane) silently reports the wrong active cell whenever the user's
        // cursor was left in any pane other than the first one Excel happened to write.
        var activePaneName = pane?.Attribute("activePane")?.Value;
        if (string.IsNullOrWhiteSpace(activePaneName))
            activePaneName = "topLeft";

        XElement? fallbackSelection = null;
        foreach (var selection in sheetView.Elements(worksheetNs + "selection"))
        {
            var activeCell = selection.Attribute("activeCell")?.Value;
            if (string.IsNullOrWhiteSpace(activeCell))
                continue;

            fallbackSelection ??= selection;

            var selectionPaneName = selection.Attribute("pane")?.Value;
            if (string.IsNullOrWhiteSpace(selectionPaneName))
                selectionPaneName = "topLeft";

            if (string.Equals(selectionPaneName, activePaneName, StringComparison.Ordinal))
                return ParseOptionalCellReference(activeCell);
        }

        var fallbackActiveCell = fallbackSelection?.Attribute("activeCell")?.Value;
        return string.IsNullOrWhiteSpace(fallbackActiveCell) ? null : ParseOptionalCellReference(fallbackActiveCell);
    }

    private static XElement? FindNormalCellStyle(XElement stylesRoot, XNamespace ns)
    {
        var cellStyles = stylesRoot.Element(ns + "cellStyles");
        if (cellStyles is null)
            return null;

        foreach (var cellStyle in cellStyles.Elements(ns + "cellStyle"))
        {
            if (IsNormalCellStyle(cellStyle))
                return cellStyle;
        }

        return null;
    }

    private static bool IsNormalCellStyle(XElement style) =>
        string.Equals(style.Attribute("builtinId")?.Value, "0", StringComparison.Ordinal) ||
        string.Equals(style.Attribute("name")?.Value, "Normal", StringComparison.OrdinalIgnoreCase);

    private static XElement? FindFirstDefaultCellXf(XElement stylesRoot, XNamespace ns)
    {
        var cellXfs = stylesRoot.Element(ns + "cellXfs");
        if (cellXfs is null)
            return null;

        foreach (var cellXf in cellXfs.Elements(ns + "xf"))
            return cellXf;

        return null;
    }

    private static int? ParseOptionalNonNegativeInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
            ? parsed
            : null;

    private static WorksheetViewMode ParseWorksheetViewMode(string? value) =>
        XlsxWorksheetXmlValueParser.ParseWorksheetViewMode(value);

    private static bool IsPrimarySheetView(XElement element) =>
        string.Equals(element.Attribute("workbookViewId")?.Value ?? "0", "0", StringComparison.Ordinal);

    private static bool IsValidWorksheetRow(uint row) =>
        row is >= 1 and <= CellAddress.MaxRow;

    private static bool IsValidWorksheetColumn(uint column) =>
        column is >= 1 and <= CellAddress.MaxCol;

    private static bool IsValidRepeatRange(WorksheetRepeatRange range, uint max) =>
        range.Start >= 1 && range.End >= range.Start && range.End <= max;

    private static bool IsSupportedTextRotation(int rotation) =>
        rotation == 255 || rotation is >= -90 and <= 90;

    private static uint ValidFrozenRowsOrZero(uint row) =>
        XlsxWorksheetXmlValueParser.ValidFrozenRowsOrZero(row);

    private static uint ValidFrozenColumnsOrZero(uint column) =>
        XlsxWorksheetXmlValueParser.ValidFrozenColumnsOrZero(column);

    private static bool IsSupportedFontSize(double fontSize) =>
        double.IsFinite(fontSize) && fontSize is >= 1 and <= 409;

}

