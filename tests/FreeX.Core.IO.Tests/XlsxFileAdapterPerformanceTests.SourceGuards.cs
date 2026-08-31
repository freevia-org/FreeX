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
    public void SavePostProcessing_RewritesPreservedPivotDefinitionsInOnePackagePass()
    {
        var source = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.SavePostProcessing.cs");
        var methodStart = source.IndexOf(
            "private static void RewritePreservedPivotTableDefinitions",
            StringComparison.Ordinal);
        var methodEnd = source.IndexOf(
            "private static List<int> ReadPreservedPivotFieldCollectionIndexes",
            methodStart,
            StringComparison.Ordinal);
        var method = source[methodStart..methodEnd];

        source.Should().Contain(
            "RewritePreservedPivotTableDefinitions(packageStream, workbook, numberFormatIdMap);");
        source.Should().NotContain("private static void RewritePivotTableFieldAxes(");
        source.Should().NotContain("private static void RewritePivotTableFilterState(");
        source.Should().NotContain("private static void RewritePivotTableLayoutState(");
        method.Should().Contain("using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);");
        method.Should().Contain("var pivotXml = XlsxPackageXmlEditor.LoadXml(entry);");
        method.Should().Contain("changed |= RewritePreservedPivotPageFieldSelections");
        method.Should().Contain("changed |= RewritePreservedPivotDataFieldSummaries");
        method.Should().Contain("var fieldMetadata = PreservedPivotFieldMetadata.Create(pivot);");
        method.Should().Contain("RewritePreservedPivotFieldAxes(root, pivot, cache, fieldMetadata, workbookNs)");
        method.Should().Contain("RewritePreservedPivotFieldItemFilters(root, cache, fieldMetadata, workbookNs)");
        method.Should().Contain("RewritePreservedPivotPageFieldSelections(root, cache, fieldMetadata, workbookNs)");
        method.Should().Contain("if (changed)");
        source.Should().Contain("private sealed class PreservedPivotFieldMetadata");
        source.Should().Contain("axisBySourceFieldIndex.TryAdd(field.SourceFieldIndex, \"axisRow\")");
        source.Should().Contain("axisBySourceFieldIndex.TryAdd(field.SourceFieldIndex, \"axisCol\")");
        source.Should().Contain("axisBySourceFieldIndex.TryAdd(field.SourceFieldIndex, \"axisPage\")");
        source.Should().NotContain("desiredRowIndexes.Contains(index)",
            "preserved pivot-field axis rewrites must use the per-pivot index instead of linear list scans per native field");
        source.Should().NotContain("FindPreservedPivotField(",
            "preserved item-filter rewrites must use the precomputed last-match model lookup");
    }

    [Fact]
    public void SavePostProcessing_UsesOneFeatureGatedSourceDrawingMetadataSnapshot()
    {
        var source = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.SavePostProcessing.cs");
        var readerStart = source.IndexOf(
            "private static SourceDrawingSaveMetadata ReadSourceDrawingSaveMetadata",
            StringComparison.Ordinal);
        var readerEnd = source.IndexOf(
            "private static IReadOnlyDictionary<string, string> ReadSourceDrawingPathsBySheet",
            readerStart,
            StringComparison.Ordinal);
        var reader = source[readerStart..readerEnd];
        var mediaStart = source.IndexOf("private static void ReadSourceMediaMetadata", StringComparison.Ordinal);
        var mediaEnd = source.IndexOf("private sealed record SourceDrawingSaveMetadata", mediaStart, StringComparison.Ordinal);
        var mediaReader = source[mediaStart..mediaEnd];

        source.Should().Contain("sourceDrawingSaveMetadata ??= ReadSourceDrawingSaveMetadata(workbook, sourceDrawingMetadataFields)");
        source.Should().Contain("SourceDrawingMetadataFields.MediaEntryNames");
        source.Should().Contain("SourceDrawingMetadataFields.ChartHyperlinks");
        source.Should().Contain("SourceDrawingMetadataFields.ObjectHyperlinks");
        source.Should().Contain("SourceDrawingMetadataFields.MaxPictureIndex");
        reader.Split("sourcePackage.OpenRead()")
            .Should().HaveCount(2, "the source package should be opened once for the complete snapshot");
        reader.Split("new ZipArchive(sourceStream, ZipArchiveMode.Read)")
            .Should().HaveCount(2, "the complete snapshot should share one source archive");
        mediaReader.Split("foreach (var entry in sourceArchive.Entries)")
            .Should().HaveCount(2, "media names and the maximum authored-picture index should share one entry scan");
        source.Should().NotContain("GetSourceDrawingPathsBySheet(");
        source.Should().NotContain("GetSourceChartHyperlinksBySheet(");
        source.Should().NotContain("GetSourceDrawingObjectHyperlinksBySheet(");
        source.Should().NotContain("GetSourceMaxPictureIndex(");
        source.Should().NotContain("GetSourceMediaEntryNames(");
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
    public void NumberFormatCatalogWriter_IndexesExistingFormatsOnce()
    {
        var source = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxNumberFormatCatalogWriter.cs");

        source.Should().Contain("var formatIndex = NumberFormatIndex.Create(numFmts, workbookNs);");
        source.Should().Contain("_formatCodesById.TryAdd(id, formatCode);");
        source.Should().Contain("_customIdsByCode.TryAdd(formatCode, id);");
        source.Should().Contain("numFmts.SetAttributeValue(\"count\", formatIndex.Count");
        source.Should().NotContain("FindNumberFormatById");
        source.Should().NotContain("FindEquivalentNumberFormat");
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
    public void DataValidationClosedXmlLoad_IndexesAcceptedRulesByExactRange()
    {
        var source = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxDataValidationClosedXmlMapper.cs");
        var loadEnd = source.IndexOf("    public static void Save(", StringComparison.Ordinal);
        var load = source[..loadEnd];

        load.Should().Contain("var existingRulesByRange = BuildValidationRangeIndex(sheet.DataValidations);")
            .And.Contain("IsDuplicateCoveredValidation(existingRulesByRange, dv)")
            .And.Contain("IndexValidationRanges(existingRulesByRange, dv)")
            .And.NotContain("IsDuplicateCoveredValidation(sheet.DataValidations, dv)",
                "each incoming data validation should only inspect rules registered for its exact range");
        source.Should().Contain("private static Dictionary<GridRange, List<DataValidation>> BuildValidationRangeIndex")
            .And.Contain("private static bool IsRangeCovered(")
            .And.NotContain("existingRules.Any(existing => CoversRange(existing, range, candidate))",
                "data-validation deduplication must avoid rescanning every accepted rule for each incoming rule");
    }

    [Fact]
    public void ChartExSeriesTitles_IndexVerbatimAndEmbeddedEntriesWithoutLinqRescans()
    {
        var source = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxChartXmlWriter.ChartEx.cs");
        var buildStart = source.IndexOf("    internal static IEnumerable<XElement> BuildChartExSeries(", StringComparison.Ordinal);
        var titleStart = source.IndexOf("    private static XElement? ToChartExSeriesTitleXml(", StringComparison.Ordinal);
        var lookupStart = source.IndexOf("    private sealed class ChartExSeriesTitleLookup", StringComparison.Ordinal);
        var valueStripStart = source.IndexOf("    private static uint GetChartExSeriesValueStrip", StringComparison.Ordinal);
        var build = source[buildStart..titleStart];
        var title = source[titleStart..lookupStart];
        var lookup = source[lookupStart..valueStripStart];

        build.Should().Contain("ChartExSeriesTitleLookup.Create(chart)");
        title.Should().Contain("titleLookup.TryGetVerbatim(seriesIndex, out var verbatim)")
            .And.NotContain(".FirstOrDefault(",
                "each chartEx series title should use the precomputed lookup instead of rescanning formula entries");
        lookup.Should().Contain("if (chart.VerbatimSeriesFormulas is not { Count: > 0 } verbatimSeries)")
            .And.Contain("return null;",
                "the common chartEx path with no verbatim formulas must not allocate title lookup dictionaries")
            .And.Contain("verbatimBySeriesIndex.TryAdd(verbatim.SeriesIndex, verbatim)")
            .And.Contain("embeddedBySeriesIndex.TryAdd(embedded.SeriesIndex, embedded)",
                "first duplicate series entries must retain the prior FirstOrDefault semantics")
            .And.NotContain(".FirstOrDefault(",
                "lookup construction should scan each source collection exactly once");
    }

    [Fact]
    public void ClassicChartSeries_IndexVerbatimFormulasWithoutPerSeriesLinqRescans()
    {
        var source = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxChartXmlWriter.Series.cs");
        var lookupStart = source.IndexOf("    private sealed class ChartSeriesVerbatimFormulaLookup", StringComparison.Ordinal);
        var lookupEnd = source.IndexOf("    /// <summary>", lookupStart, StringComparison.Ordinal);
        var builders = source[..lookupStart] + source[lookupEnd..];
        var lookup = source[lookupStart..lookupEnd];

        builders.Should().Contain("var verbatimLookup = ChartSeriesVerbatimFormulaLookup.Create(chart);")
            .And.NotContain("chart.VerbatimSeriesFormulas?.FirstOrDefault(",
                "classic chart series writers should not linearly rescan verbatim formulas for every series");
        lookup.Should().Contain("if (chart.VerbatimSeriesFormulas is not { Count: > 0 } formulas)")
            .And.Contain("return null;",
                "charts without verbatim formulas must stay allocation-free")
            .And.Contain("formulasBySeriesIndex.TryAdd(formula.SeriesIndex, formula)",
                "first duplicate formula entries must retain the prior FirstOrDefault behavior");
    }

    [Fact]
    public void ClassicChartSeries_IndexLastSeriesFormatsWithoutPerSeriesLinqRescans()
    {
        var source = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxChartXmlWriter.Series.cs");
        var lookupStart = source.IndexOf("    private sealed class ChartSeriesFormatLookup", StringComparison.Ordinal);
        var lookupEnd = source.IndexOf("    /// <summary>", lookupStart, StringComparison.Ordinal);
        var builders = source[..lookupStart] + source[lookupEnd..];
        var lookup = source[lookupStart..lookupEnd];

        builders.Should().Contain("var formatLookup = ChartSeriesFormatLookup.Create(chart);")
            .And.NotContain("chart.SeriesFormats.LastOrDefault(",
                "classic chart series formatting must not linearly rescan the full format list for every helper");
        lookup.Should().Contain("if (chart.SeriesFormats.Count == 0)")
            .And.Contain("return null;",
                "charts without per-series formats must remain allocation-free")
            .And.Contain("formatsBySeriesIndex[format.SeriesIndex] = format",
                "later duplicate format entries must retain the prior LastOrDefault precedence")
            .And.Contain("Normalize(format)",
                "enum sanitization must remain part of every indexed format lookup");
    }

    [Fact]
    public void SourcePackage_RenumberedQueryTableReplayIndexesExistingTargets()
    {
        var source = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.SourcePackage.cs");
        var methodStart = source.IndexOf(
            "    private static void PreserveRenumberedWorksheetQueryTableRelationships(",
            StringComparison.Ordinal);
        var methodEnd = source.IndexOf("    private static void CloneQueryTablesForDuplicatedSheets(", methodStart, StringComparison.Ordinal);
        var method = source[methodStart..methodEnd];

        method.Should().Contain("var existingQueryTableTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);")
            .And.Contain("if (!existingQueryTableTargets.Add(target))")
            .And.NotContain(".Any(existing =>",
                "replaying many query-table relationships onto a renumbered worksheet must not rescan " +
                "the generated relationship XML for every source relationship");
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

    [Fact]
    public void DeferredChartExReader_IndexesFirstOrdinalDataEntryOncePerChart()
    {
        var source = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxChartPartReader.Deferred.cs");
        var readerStart = source.IndexOf("    private static bool TryReadDeferredAdvancedChart(", StringComparison.Ordinal);
        var orientationStart = source.IndexOf("    private static bool DetectDeferredSeriesInRows(", StringComparison.Ordinal);
        var lookupStart = source.IndexOf("    private sealed class ChartExDataLookup", StringComparison.Ordinal);
        var firstChildStart = source.IndexOf("    private static XElement? FirstChildElementByLocalName", StringComparison.Ordinal);
        var reader = source[readerStart..orientationStart];
        var lookup = source[lookupStart..firstChildStart];

        reader.Should().Contain("var chartExDataLookup = chartExSeries.Length > 0 ? ChartExDataLookup.Create(chartXml) : null;")
            .And.NotContain("FindChartExData(",
                "each chartEx series must reuse the per-chart data index rather than rescan the XML document");
        lookup.Should().Contain("new Dictionary<string, XElement>(StringComparer.Ordinal)")
            .And.Contain("dataById.TryAdd(dataId, element)",
                "first duplicate IDs must preserve the former document-order lookup result")
            .And.NotContain("FindChartExData(",
                "the old per-dataId full-document scan should be removed after indexing");
    }

    [Fact]
    public void ChartSeriesWriter_IndexesDataPointFormatsWithLastDuplicatePrecedence()
    {
        var source = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxChartXmlWriter.Series.cs");
        var buildStart = source.IndexOf("    private static IEnumerable<XElement> BuildChartSeries(", StringComparison.Ordinal);
        var lookupStart = source.IndexOf("    private sealed class ChartDataPointFormatLookup", StringComparison.Ordinal);
        var dataPointsStart = source.IndexOf("    private static IEnumerable<XElement> ToDataPointsXml(", StringComparison.Ordinal);
        var markerStart = source.IndexOf("    private static XElement? ToPointMarkerXml(", StringComparison.Ordinal);
        var builds = source[buildStart..lookupStart];
        var lookup = source[lookupStart..dataPointsStart];
        var dataPoints = source[dataPointsStart..markerStart];

        builds.Should().Contain("var dataPointFormatLookup = ChartDataPointFormatLookup.Create(chart);")
            .And.Contain("ToDataPointsXml(chart, dataPointFormatLookup, seriesIndex, chartNs, drawingNs)");
        lookup.Should().Contain("if (chart.PointFillColors.Count == 0 && chart.PointMarkerFormats.Count == 0)")
            .And.Contain("return null;",
                "charts without per-point overrides must not allocate lookup dictionaries")
            .And.Contain("formats[pointIndex] = format;",
                "later duplicate formats must retain the old LastOrDefault precedence");
        dataPoints.Should().Contain("dataPointFormatLookup.GetPointIndexes(seriesIndex, explodedPoints.Keys)")
            .And.NotContain("chart.PointFillColors.LastOrDefault(")
            .And.NotContain("chart.PointMarkerFormats.LastOrDefault(",
                "each emitted data point must resolve indexed formats instead of rescanning both lists");
    }

    [Fact]
    public void ChartSeriesWriter_IndexesLastMetadataEntriesOncePerBuilder()
    {
        var source = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxChartXmlWriter.Series.cs");
        var buildStart = source.IndexOf("    private static IEnumerable<XElement> BuildChartSeries(", StringComparison.Ordinal);
        var lookupStart = source.IndexOf("    private sealed class ChartSeriesMetadataLookup", StringComparison.Ordinal);
        var dataPointLookupStart = source.IndexOf("    private sealed class ChartDataPointFormatLookup", StringComparison.Ordinal);
        var builds = source[buildStart..lookupStart];
        var lookup = source[lookupStart..dataPointLookupStart];

        builds.Should().Contain("var metadataLookup = ChartSeriesMetadataLookup.Create(chart);")
            .And.Contain("GetSeriesOrder(metadataLookup, seriesIndex)")
            .And.Contain("ToRangeDataLabelsExtXml(metadataLookup, seriesIndex, chartNs)");
        lookup.Should().Contain("if (chart.SeriesNameOverrides.Count == 0")
            .And.Contain("chart.SeriesRangeDataLabels?.Count is not > 0)")
            .And.Contain("return null;",
                "the common chart path with no captured metadata must not allocate lookup dictionaries")
            .And.Contain("bySeriesIndex[getSeriesIndex(entry)] = entry;",
                "the final duplicate must retain the prior LastOrDefault precedence");
        source.Should().NotContain("SeriesNameOverrides.LastOrDefault(")
            .And.NotContain("SeriesOrderOverrides.LastOrDefault(")
            .And.NotContain("MultiLevelCategoryXml\n            .LastOrDefault(")
            .And.NotContain("SeriesRangeDataLabels?.LastOrDefault(",
                "every series writer must resolve captured metadata through its per-builder index");
    }
}
