using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Xml.Linq;
using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxFileAdapterPerformanceTests
{
    [BenchmarkFact]
    public void Benchmark_StructuredTableWriterTrailingNumber_AvoidsReverseIteratorAllocation()
    {
        var source = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxStructuredTableWriter.cs");
        var methodStart = source.IndexOf("private static int ExtractTrailingNumber", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private static void TrySetNativeAttributeIfMissing", methodStart, StringComparison.Ordinal);
        var method = source[methodStart..methodEnd];

        method.Should().NotContain(
            ".Reverse()",
            "structured table id fallback parsing runs during XLSX save and should avoid LINQ iterator scaffolding");
        method.Should().NotContain(
            ".ToArray()",
            "trailing-number parsing should avoid a temporary char array allocation");
    }

    [Fact]
    public void SavePostProcessing_DetectsPivotCustomNumberFormatsWithoutNestedLinq()
    {
        var source = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.SavePostProcessing.cs");

        source.Should().Contain("featurePlan.HasPivotCustomNumberFormats");
        source.Should().Contain("private static bool HasPivotCustomNumberFormats(Sheet sheet)");
        source.Should().NotContain(
            "workbook.Sheets.SelectMany(sheet => sheet.PivotTables)",
            "XLSX save post-processing should avoid nested LINQ iterator allocation while deciding whether pivot custom number formats need catalog output");
    }

    [Fact]
    public void SavePostProcessing_BatchesWorkbookFeatureDetection()
    {
        var source = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.SavePostProcessing.cs");

        source.Should().Contain("var featurePlan = XlsxPostProcessingFeaturePlan.Create(workbook);");
        source.Should().Contain("private struct XlsxPostProcessingFeaturePlan");
        source.Should().Contain("foreach (var sheet in workbook.Sheets)");
        source.Should().Contain("sheet.GetOccupiedCellMap()");
        source.Should().Contain("if (!HasSupportedCharts)");
        source.Should().Contain("if (!HasPivotCustomNumberFormats)");
        source.Should().NotContain(
            "workbook.Sheets.Any(",
            "XLSX save post-processing should batch sheet feature checks instead of rescanning every sheet for each optional writer");
        source.Should().NotContain(
            "sheet.EnumerateCells().Any",
            "ignored-error detection should avoid nested LINQ and cell-address iterator allocation");
        source.Should().NotContain(
            "HasSupportedCharts |= HasSupportedXlsxCharts(sheet);",
            "chart support checks can stop after the first supported chart is found");
        source.Should().NotContain(
            "HasPivotCustomNumberFormats |= HasPivotCustomNumberFormats(sheet);",
            "pivot custom-format scans can stop after the first custom format is found");
    }

    [Fact]
    public void PackageXmlEditor_RewritesXmlWithoutFormattingWhitespace()
    {
        var editorSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxPackageXmlEditor.cs");
        var sharedSource = TestWorkspaceFiles.ReadRepoText("shared", "Free.Shared.Opc", "OpcXml.cs");
        var replaceXmlEntry = sharedSource[
            sharedSource.IndexOf("public static void ReplaceXmlEntry", StringComparison.Ordinal)..
            sharedSource.IndexOf("public static void WriteXmlEntry", StringComparison.Ordinal)];

        editorSource.Should().Contain("OpcXml.ReplaceXmlEntry(archive, entryName, document);");
        replaceXmlEntry.Should().Contain("SaveOptions saveOptions = SaveOptions.DisableFormatting");
        replaceXmlEntry.Should().Contain("document.Save(stream, saveOptions);");
        replaceXmlEntry.Should().NotContain("document.Save(stream);");
    }

    [Fact]
    public void StylesheetMetadataPreserver_PreflightsPlainStylesheetBeforeLoadingXml()
    {
        var source = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxStylesheetMetadataPreserver.cs");

        source.Should().Contain("HasPreservableStylesheetMetadata(sourceStylesEntry)");
        source.Should().Contain("case \"colors\":");
        source.Should().Contain("case \"extLst\":");
        source.Should().Contain("case \"dxfs\":");
        source.Should().Contain("case \"tableStyles\":");
        source.Should().Contain("TableStyleMedium2");
        source.Should().Contain("PivotStyleLight16");
        source.Should().Contain("return true;");
    }

    [Fact]
    public void NumberFormatCatalogWriter_BuildsPivotCustomFormatCatalogWithoutNestedLinq()
    {
        var source = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxNumberFormatCatalogWriter.cs");

        source.Should().Contain("foreach (var sheet in workbook.Sheets)");
        source.Should().Contain("foreach (var pivot in sheet.PivotTables)");
        source.Should().Contain("foreach (var field in pivot.DataFields)");
        source.Should().NotContain(
            ".SelectMany(",
            "pivot custom number-format catalog building should walk sheets/pivots/data fields directly");
        source.Should().NotContain(
            ".Where(pair => pair.Key >= 164",
            "catalog seeding should avoid a temporary LINQ filtered dictionary projection");
    }

    [Fact]
    public void LoadCore_ReadsWorkbookMetadataInSinglePackagePass()
    {
        var adapterSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.cs");
        var metadataSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxWorkbookMetadataReader.cs");

        adapterSource.Should().Contain("workbookMetadata = packageParts.HasWorkbook");
        adapterSource.Should().Contain("XlsxWorkbookMetadataReader.LoadWorkbookMetadata(packageArchive)");
        adapterSource.Should().NotContain("XlsxWorkbookMetadataReader.LoadWorkbookMetadata(packageStream)");
        foreach (var legacyCall in new[]
        {
            "LoadUses1904DateSystem(packageStream)",
            "LoadWorkbookProperties(packageStream)",
            "LoadWorkbookViewProperties(packageStream)",
            "LoadFileSharing(packageStream)",
            "LoadFileRecoveryProperties(packageStream)",
            "LoadFileVersion(packageStream)",
            "LoadFunctionGroups(packageStream)",
            "LoadSmartTags(packageStream)",
            "LoadProtection(packageStream)",
            "LoadProtectionMetadata(packageStream)",
            "LoadCalculationProperties(packageStream)",
            "LoadCustomViews(packageStream)"
        })
        {
            adapterSource.Should().NotContain(legacyCall);
        }

        metadataSource.Should().Contain("public static XlsxWorkbookMetadataSnapshot LoadWorkbookMetadata(Stream xlsxStream)");
        metadataSource.Should().Contain("internal static XlsxWorkbookMetadataSnapshot LoadWorkbookMetadata(ZipArchive archive)");
        metadataSource.Should().Contain("var workbookEntry = archive.GetEntry(\"xl/workbook.xml\");");
        metadataSource.Should().Contain("return LoadWorkbookMetadata(workbookXml);");
    }

    [Fact]
    public void LoadCore_ReusesSingleStylesheetParseForLoadMetadata()
    {
        var adapterSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.cs");

        adapterSource.Should().Contain("stylesXml = packageParts.HasStyles");
        adapterSource.Should().Contain("XlsxStylesheetReader.Load(packageArchive)");
        adapterSource.Should().Contain("XlsxWorkbookMetadataReader.LoadNumberFormatCatalog(stylesXml)");
        adapterSource.Should().Contain("XlsxIndexedColorPaletteMapper.Load(stylesXml)");
        adapterSource.Should().Contain("XlsxPivotTableStyleMetadataReader.Load(stylesXml)");
        adapterSource.Should().Contain("XlsxStructuredTableStyleMetadataReader.Load(stylesXml, workbookTheme, loadedIndexedColors)");
        adapterSource.Should().Contain("sheetXmlLayout = LoadSheetXmlLayout(");
        adapterSource.Should().Contain("packageParts.HasStructuredTables,");
        adapterSource.Should().NotContain("XlsxStylesheetReader.Load(packageStream)");
        adapterSource.Should().NotContain("LoadNumberFormatCatalog(packageStream)");
        adapterSource.Should().NotContain("XlsxIndexedColorPaletteMapper.Load(packageStream)");
        adapterSource.Should().NotContain("XlsxPivotTableStyleMetadataReader.Load(packageStream)");
        adapterSource.Should().NotContain("XlsxStructuredTableStyleMetadataReader.Load(packageStream)");
        adapterSource.Should().NotContain("LoadSheetXmlLayout(packageStream);");
    }

    [Fact]
    public void LoadCore_UsesPackagePartSummaryToSkipOptionalMetadataReaders()
    {
        var adapterSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.cs");
        var layoutSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.SheetXmlLayout.cs");
        var structuredTableSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxStructuredTableMetadataReader.cs")
            .ReplaceLineEndings("\n");

        adapterSource.Should().Contain("packageParts = XlsxLoadPackageParts.Inspect(packageArchive);");
        adapterSource.Should().Contain("if (packageParts.HasPivotPackageParts)");
        adapterSource.Should().Contain("packageParts.HasChartExChartParts");
        adapterSource.Should().Contain("packageParts.HasDrawingPackageParts");
        adapterSource.Should().Contain("InspectChartExChartParts(archive)");
        adapterSource.Should().Contain("if (packageParts.HasSlicerTimelinePackageParts)");
        adapterSource.Should().Contain("if (packageParts.HasExternalLinks)");
        adapterSource.Should().Contain("if (packageParts.HasStructuredTables &&");
        adapterSource.Should().Contain("XlsxPivotTableReader.Load(packageArchive, numberFormatCatalog)");
        adapterSource.Should().Contain("XlsxSlicerTimelineMetadataReader.Load(packageArchive)");
        adapterSource.Should().Contain("XlsxExternalLinkMetadataReader.Load(packageArchive)");
        adapterSource.Should().Contain("XlsxStructuredTableMetadataReader.Load(");
        adapterSource.Should().Contain("packageArchive,");
        adapterSource.Should().Contain("loadedStructuredTableMetadataFromSheetLayout");
        adapterSource.Should().Contain("!loadedStructuredTableMetadataFromSheetLayout");
        layoutSource.Should().Contain("ReadTableRelationshipIds(worksheetXml, worksheetNs, relNs)");
        layoutSource.Should().Contain("TableRelationshipIds");
        layoutSource.Should().Contain("XlsxStructuredTableMetadataReader.Load(");
        structuredTableSource.Should().Contain("IReadOnlyDictionary<string, IReadOnlyList<string>>? tableRelationshipIdsBySheetName");
        structuredTableSource.Should().Contain("return Load(archive);");
        structuredTableSource.Should().Contain("catch\n        {\n            return StructuredTablePackageMetadata.Empty;\n        }");
        structuredTableSource.Should().Contain("catch\n        {\n            return Load(archive);\n        }");
        adapterSource.Should().NotContain("XlsxPivotTableReader.Load(packageStream, numberFormatCatalog)");
        adapterSource.Should().NotContain("XlsxSlicerTimelineMetadataReader.Load(packageStream)");
        adapterSource.Should().NotContain("XlsxExternalLinkMetadataReader.Load(packageStream)");
        adapterSource.Should().NotContain("XlsxStructuredTableMetadataReader.Load(packageStream)");
    }

    [Fact]
    public void LoadCore_ReusesSheetXmlLayoutFactsForClosedXmlPackagePreparation()
    {
        var adapterSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.cs");
        var layoutSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.SheetXmlLayout.cs");
        var sheetStyleOnlySource = TestWorkspaceFiles.ReadCoreModelRepoSource("Sheet.StyleOnly.cs");
        var sanitizerSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxClosedXmlLoadPackageSanitizer.cs");
        var stripperSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxClosedXmlStyleOnlyCellStripper.cs");

        adapterSource.Should().Contain("GetClosedXmlStyleOnlyWorksheetPathsToStrip(");
        adapterSource.Should().Contain("CreateClosedXmlLoadSanitizationHints(");
        adapterSource.Should().Contain("sheet.EnsureStyleOnlyCapacity(layoutWithStyleOnlyCells.ExplicitStyleOnlyCells.Count);");
        adapterSource.Should().Contain("AddStyleOnlyRun(ref explicitStyleOnlyRuns");
        adapterSource.Should().Contain("sheet.SetStyleOnlyRuns(explicitStyleOnlyRuns);");
        adapterSource.Should().Contain("layout.HasDuplicateStyleOnlyCellStyleIndexes");
        adapterSource.Should().Contain("layout.HasClosedXmlUnsupportedConditionalFormatting");
        adapterSource.Should().Contain("layout.HasWorksheetDynamicFilters");
        adapterSource.Should().Contain("layout.MergedRegions.Count");
        adapterSource.Should().Contain("if (xmlLayout is null)");
        adapterSource.Should().Contain("XlsxClosedXmlLoadPackageSanitizer.Create(");
        adapterSource.Should().Contain("styleOnlyWorksheetPathsToStrip,");
        adapterSource.Should().NotContain("XlsxClosedXmlStyleOnlyCellStripper.Create(packageStream, styleOnlyWorksheetPathsToStrip)");
        adapterSource.Should().NotContain("mutateSourcePackage: canMutateStyleOptimizedPackage");
        adapterSource.Should().Contain("sanitizationHints");

        layoutSource.Should().Contain("cellLayout.HasDuplicateStyleOnlyCellStyleIndexes");
        layoutSource.Should().Contain("HasDynamicFilter(autoFilter)");
        layoutSource.Should().Contain("allowBlankType: false");
        layoutSource.Should().Contain("IReadOnlyList<GridRange> MergedRegions");
        layoutSource.Should().Contain("ReadMergedRegions(worksheetXml, worksheetNs)");

        sheetStyleOnlySource.Should().Contain("EnsureStyleOnlyCapacity");
        sheetStyleOnlySource.Should().Contain("StyleOnlyRun");
        sheetStyleOnlySource.Should().Contain("TryGetStyleOnlyRun");
        sheetStyleOnlySource.Should().Contain("_styleOnly.EnsureCapacity(capacity)");

        sanitizerSource.Should().Contain("XlsxClosedXmlLoadSanitizationHints");
        sanitizerSource.Should().Contain("bool mutateSourcePackage = false");
        sanitizerSource.Should().Contain("IReadOnlySet<string>? styleOnlyWorksheetPathsToStrip");
        sanitizerSource.Should().Contain("CreateFusedTransientPackage(");
        sanitizerSource.Should().Contain("TryWriteFusedEntry(");
        sanitizerSource.Should().Contain("XlsxClosedXmlStyleOnlyCellStripper.StripRedundantStyleOnlyCells");
        sanitizerSource.Should().Contain("CompressionLevel.Fastest");
        sanitizerSource.Should().Contain("TryCreateSanitizationRequirementsFromHints(");
        sanitizerSource.Should().Contain("ResolveKnownOrScan(knownHints.HasPivotPackageMetadata");
        sanitizerSource.Should().Contain("ResolveKnownOrScan(knownHints.HasChartExChartParts");
        sanitizerSource.Should().Contain("ResolveKnownOrScan(knownHints.HasDrawingPackageParts");
        sanitizerSource.Should().Contain("RemoveDrawingPackageParts(archive)");
        sanitizerSource.Should().Contain("RemoveSheetDrawingReferences(archive)");
        sanitizerSource.Should().Contain("RemoveContentTypeOverrides(archive, removedParts)");
        sanitizerSource.Should().Contain("ResolveKnownOrScan(knownHints.HasUnsupportedConditionalFormattingBlocks");
        sanitizerSource.Should().Contain("ResolveKnownOrScan(knownHints.HasWorksheetDynamicFilters");
        sanitizerSource.Should().Contain("MergeCellWorksheetPathsToStrip");
        sanitizerSource.Should().Contain("RemoveWorksheetMergeCells(archive, mergeCellWorksheetPaths)");
        sanitizerSource.Should().Contain("ShouldStripMergeCells(requirements, normalizedPath)");

        stripperSource.Should().Contain("IReadOnlySet<string>? worksheetPathsToStrip");
        stripperSource.Should().Contain("internal static bool ShouldStripWorksheet");
        stripperSource.Should().Contain("internal static void StripRedundantStyleOnlyCells");
        stripperSource.Should().Contain("worksheetPathsToStrip.Contains(XlsxPackagePath.NormalizePackagePath(sourceEntry.FullName))");
        stripperSource.Should().Contain("ContainsDuplicateStyleOnlyCells(scanStream)");
    }

    [Fact]
    public void Save_UsesSaveScopedStyleCacheForStyleLookup()
    {
        var saveSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.Save.cs");

        saveSource.Should().Contain("var styleCache = new Dictionary<StyleId, CellStyle>(workbook.StyleCount);");
        saveSource.Should().Contain("GetCachedStyle(workbook, styleCache, cell.StyleId)");
        saveSource.Should().Contain("GetCachedStyle(workbook, styleCache, seed.StyleId)");
        saveSource.Should().Contain("style = workbook.GetStyle(styleId);");
        saveSource.Should().NotContain("workbook.GetStyle(cell.StyleId)");
        saveSource.Should().NotContain("workbook.GetStyle(seed.StyleId)");
    }

    [Fact]
    public void SourcePackagePatch_RewritesWorksheetXmlWithFastCompression()
    {
        var snapshotSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.SourcePackageSnapshot.cs");
        var streamingPatchStart = snapshotSource.IndexOf("public static bool TryApplySimpleExistingCellChangesStreaming", StringComparison.Ordinal);
        var streamingPatchEnd = snapshotSource.IndexOf("private static XmlWriterSettings CreatePatchXmlWriterSettings", streamingPatchStart, StringComparison.Ordinal);
        var streamingPatchSource = snapshotSource[streamingPatchStart..streamingPatchEnd];

        streamingPatchSource.Should().Contain("archive.CreateEntry(worksheetPath, CompressionLevel.Fastest)");
        streamingPatchSource.Should().NotContain("archive.CreateEntry(worksheetPath, CompressionLevel.Optimal)");
    }

    [Fact]
    public void Save_ExpandsStyleOnlyCellsInPostProcessingAfterClosedXmlStyleSeeding()
    {
        var saveSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.Save.cs");
        var postProcessingSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.SavePostProcessing.cs");
        var writerSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxStyleOnlyCellWriter.cs");

        saveSource.Should().Contain("ApplyStyleOnlySeedCells");
        saveSource.Should().Contain("XlsxStyleOnlyCellWriter.GetSeedCells(sheet)");
        saveSource.Should().NotContain("GetStyleOnlyRuns");
        postProcessingSource.Should().Contain("featurePlan.HasStyleOnlyCells");
        postProcessingSource.Should().Contain("XlsxStyleOnlyCellWriter.Save(packageStream, workbook, GetWorksheetPathMap());");
        writerSource.Should().Contain("ReadSeedStyleIndexes");
        writerSource.Should().Contain("ApplyStyleOnlyCells");
        writerSource.Should().Contain("UpdateDimension");
        writerSource.Should().Contain("new List<StyleOnlyCell>(sheet.StyleOnlyCellCount)");
        writerSource.Should().Contain("var isRowMajorOrdered = true;");
        writerSource.Should().Contain("if (!isRowMajorOrdered)");
        writerSource.Should().Contain("string.Create(");
        writerSource.Should().NotContain(
            "CellAddress.NumberToColumnName(col)",
            "style-only post-processing should create the required A1 attribute text directly instead of allocating separate column and row strings for each cell");
        writerSource.Should().NotContain(
            ".ToDictionary(",
            "style-only worksheet post-processing walks cells row-by-row and should avoid a temporary reference map per row");
    }

    [Fact]
    public void SavePostProcessing_BatchesWorkbookMetadataXmlWrites()
    {
        var adapterSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.SavePostProcessing.cs");
        var writerSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxWorkbookMetadataWriter.cs");

        adapterSource.Should().Contain("XlsxWorkbookMetadataWriter.SavePostProcessingMetadata(packageStream, workbook);");
        adapterSource.Should().Contain("if (featurePlan.HasWorkbookPostProcessingMetadata)");
        adapterSource.Should().Contain("XlsxWorkbookMetadataWriter.SaveSourcePackageReplayMetadata(packageStream, workbook);");
        adapterSource.Should().NotContain("XlsxWorkbookMetadataWriter.SaveWorkbookProperties(packageStream, workbook);");
        adapterSource.Should().NotContain("XlsxWorkbookMetadataWriter.SaveCalculationProperties(packageStream, workbook);");
        adapterSource.Should().NotContain("XlsxWorkbookAdditionalViewMapper.Save(packageStream, workbook);");

        writerSource.Should().Contain("public static bool HasPostProcessingMetadata(Workbook workbook)");
        writerSource.Should().Contain("private static bool HasCalculationProperties(Workbook workbook)");
        writerSource.Should().Contain("public static void SavePostProcessingMetadata(Stream xlsxStream, Workbook workbook)");
        writerSource.Should().Contain("public static void SaveSourcePackageReplayMetadata(Stream xlsxStream, Workbook workbook)");
        writerSource.Should().Contain("private static void SaveWorkbookXml(Stream xlsxStream, Workbook workbook");
    }

    [Fact]
    public void SavePostProcessing_BatchesWorksheetNativeMetadataXmlWrites()
    {
        var adapterSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.SavePostProcessing.cs");
        var saveSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.Save.cs");
        var batchSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxWorksheetNativeMetadataBatchWriter.cs");
        var sourceIndependentBatchSource =
            TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxWorksheetSourceIndependentMetadataBatchWriter.cs")
                .Replace("\r\n", "\n", StringComparison.Ordinal);
        var dataValidationNativeSource =
            TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxDataValidationNativeMetadataMapper.cs");
        var sessionSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxWorksheetXmlEditSession.cs");

        adapterSource.Should().Contain("XlsxWorksheetSourceIndependentMetadataBatchWriter.Save(packageStream, workbook, GetWorksheetPathMap());");
        adapterSource.Should().Contain("XlsxWorksheetSourceIndependentMetadataBatchWriter.HasMetadata");
        adapterSource.Should().NotContain("XlsxWorksheetNativeMetadataBatchWriter.Save(packageStream, workbook, GetWorksheetPathMap());");
        adapterSource.Should().NotContain("XlsxWorksheetAutoFilterMapper.Save(packageStream, workbook, GetWorksheetPathMap());");
        adapterSource.Should().NotContain("XlsxDataValidationNativeMetadataMapper.Save(packageStream, workbook);");
        adapterSource.Should().NotContain("HasSourcePackageIndependentWorksheetNativeMetadata");
        foreach (var legacyCall in new[]
        {
            "XlsxWorksheetProtectionMetadataWriter.Save(packageStream, workbook, GetWorksheetPathMap());",
            "XlsxWorksheetPrintOptionsMetadataWriter.Save(packageStream, workbook, GetWorksheetPathMap());",
            "XlsxWorksheetDimensionMetadataWriter.Save(packageStream, workbook, GetWorksheetPathMap());",
            "XlsxWorksheetSheetPropertiesMetadataWriter.Save(packageStream, workbook, GetWorksheetPathMap());",
            "XlsxWorksheetPrimaryViewMetadataWriter.Save(packageStream, workbook, GetWorksheetPathMap());",
            "XlsxWorksheetPageMarginsMetadataWriter.Save(packageStream, workbook, GetWorksheetPathMap());",
            "XlsxWorksheetPageBreaksMetadataWriter.Save(packageStream, workbook, GetWorksheetPathMap());",
            "XlsxWorksheetHeaderFooterMetadataWriter.Save(packageStream, workbook, GetWorksheetPathMap());"
        })
        {
            adapterSource.Should().NotContain(legacyCall);
        }

        batchSource.Should().Contain("new XlsxWorksheetXmlEditSession(xlsxStream, worksheetPathMap)");
        batchSource.Should().Contain("internal static void Save(XlsxWorksheetXmlEditSession session, Workbook workbook)");
        batchSource.Should().Contain("XlsxWorksheetProtectionMetadataWriter.Save(session, workbook);");
        batchSource.Should().Contain("XlsxWorksheetHeaderFooterMetadataWriter.Save(session, workbook);");
        // R89-io-autofilter-color-dxf-1-1: the autofilter save call now threads through any dxfIds
        // XlsxAutoFilterColorFilterDxfWriter allocated for colour filters in this same batch pass.
        sourceIndependentBatchSource.Should().Contain("removeMissingAutoFilters: true");
        sourceIndependentBatchSource.Should().Contain("removeMissingAutoFilters: false");
        sourceIndependentBatchSource.Should().Contain(
            "XlsxWorksheetAutoFilterMapper.Save(\n            session,\n            workbook,\n            colorFilterDxfIds,\n            removeMissingAutoFilters);");
        sourceIndependentBatchSource.Should().Contain("XlsxDataValidationNativeMetadataMapper.Save(session, workbook);");
        sourceIndependentBatchSource.Should().Contain("XlsxWorksheetNativeMetadataBatchWriter.Save(session, workbook);");
        saveSource.Should().Contain("if (!XlsxDataValidationNativeMetadataMapper.HasNativeMetadata(sheet))");
        dataValidationNativeSource.Should().Contain("TryCreateDataValidationsElement(sheet, containerSource, out var replacement)");
        dataValidationNativeSource.Should().Contain("AddDataValidationsInOrder(edit.Root, replacement);");
        dataValidationNativeSource.Should().Contain("XlsxDataValidationClosedXmlMapper.NormalizeListFormulaForSave");
        sessionSource.Should().Contain("private readonly Dictionary<string, XDocument> _documents");
        sessionSource.Should().Contain("XlsxPackageXmlEditor.ReplaceXml(_archive, path, _documents[path]);");
    }

    [Fact]
    public void DataValidationNativeMetadataSave_UsesCachedXmlNamesAndDirectAnchorScan()
    {
        var source = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxDataValidationNativeMetadataMapper.cs");

        source.Should().Contain("private static readonly XName DataValidationsName");
        source.Should().Contain("private static readonly XName DataValidationName");
        source.Should().Contain("private static bool IsElementAfterDataValidations");
        source.Should().Contain("foreach (var element in root.Elements())");
        source.Should().NotContain(
            "new XElement(WorksheetNs + \"dataValidation\")",
            "data-validation native metadata save should reuse cached XML names in its per-rule path");
        source.Should().NotContain(
            ".FirstOrDefault(element =>",
            "data-validation insertion should avoid LINQ iterator allocation while scanning worksheet children");
        source.Should().NotContain(
            "ElementsAfterDataValidations",
            "a switch keeps the fixed worksheet-order anchor set allocation-free");
    }

    [Fact]
    public void SavePostProcessing_BatchesSourcePackageReplayWorksheetMetadataXmlWrites()
    {
        var adapterSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.SavePostProcessing.cs");
        var batchSource =
            TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxWorksheetPostProcessingMetadataBatchWriter.cs");

        adapterSource.Should().Contain(
            "XlsxWorksheetPostProcessingMetadataBatchWriter.Save(packageStream, workbook, GetWorksheetPathMap());");
        adapterSource.Should().Contain(
            "XlsxWorksheetPostProcessingMetadataBatchWriter.SaveWorksheetElementMetadata(");
        adapterSource.Should().Contain("XlsxWorksheetPostProcessingMetadataBatchWriter.HasReplayMetadata");
        adapterSource.Should().Contain("XlsxWorksheetPostProcessingMetadataBatchWriter.HasWorksheetElementMetadata");
        adapterSource.Should().NotContain("XlsxWorksheetSingleXmlCellMapper.Save(packageStream, workbook, GetWorksheetPathMap());");
        batchSource.Should().Contain("new XlsxWorksheetXmlEditSession(xlsxStream, worksheetPathMap)");
        batchSource.Should().Contain("sheet.SingleXmlCells is not null");
        batchSource.Should().Contain("XlsxWorksheetSmartTagMapper.Save(session, workbook);");
        batchSource.Should().Contain("XlsxWorksheetSortStateMapper.Save(session, workbook);");
        batchSource.Should().Contain("XlsxWorksheetAdditionalViewMapper.Save(session, workbook);");
        batchSource.Should().Contain("XlsxWorksheetDataConsolidationMapper.Save(session, workbook);");
        batchSource.Should().Contain("XlsxWorksheetSingleXmlCellMapper.Save(xlsxStream, workbook, worksheetPathMap);");
        batchSource.Should().Contain("XlsxWorksheetPageSetupMetadataWriter.Save(session, workbook);");
        batchSource.Should().Contain("private static bool HasModeledPrinterAttributes(Workbook workbook)");
        batchSource.Should().Contain("foreach (var sheet in workbook.Sheets)");
        batchSource.Should().NotContain(
            "workbook.Sheets.Any(",
            "source-package replay metadata save should avoid allocating a LINQ predicate iterator while checking page-setup metadata");
    }

    [Fact]
    public void AdvancedConditionalFormatDifferentialStyles_NormalizeOrderWithoutEagerLinqSort()
    {
        var source =
            TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxAdvancedConditionalFormatWriter.DifferentialStyles.cs");

        source.Should().Contain("NormalizeDifferentialStyleChildrenOrder");
        source.Should().Contain("NormalizeDifferentialFontChildrenOrder");
        source.Should().Contain("StableSortDifferentialStyleChildren");
        source.Should().NotContain(
            ".OrderBy(",
            "generated differential styles are emitted in schema order and should not pay eager LINQ sorting costs");
        source.Should().NotContain(
            ".SequenceEqual(",
            "order normalization should use an allocation-free scan on the hot generated-style path");
        source.Should().NotContain(
            ".ToList()",
            "order normalization should only copy child elements after it detects out-of-order native payloads");
    }
}
