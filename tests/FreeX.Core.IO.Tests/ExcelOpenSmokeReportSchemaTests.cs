using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed class ExcelOpenSmokeReportSchemaTests
{
    [Fact]
    public void MachineReadableReport_IncludesExcelAuthoredSourceFlag()
    {
        var modelsSource = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ExcelOpenSmoke", "SmokeModels.cs");
        var programSource = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ExcelOpenSmoke", "Program.cs");

        modelsSource.Should().Contain("bool GenerateWithExcel = false");
        programSource.Should().Contain("GenerateWithExcel: true");
        programSource.Should().Contain("generatedWithExcel = result.Input.GenerateWithExcel");
        programSource.Should().Contain("sourceAuthorship = result.Input.GenerateWithExcel ? \"excel-authored\" : \"external-or-freex-authored\"");
    }

    [Fact]
    public void ExcelOpenSmoke_ExposesExcelAuthoredPivotCorpusGeneration()
    {
        var optionsSource = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ExcelOpenSmoke", "SmokeOptions.cs");
        var usageSource = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ExcelOpenSmoke", "SmokeUsage.cs");
        var fixturesSource = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ExcelOpenSmoke", "ExcelSmokeFixtures.cs");
        var programSource = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ExcelOpenSmoke", "Program.cs");

        optionsSource.Should().Contain("GenerateExcelPivotCorpusFixtures");
        optionsSource.Should().Contain("--generate-excel-pivot-corpus-fixtures");
        usageSource.Should().Contain("--generate-excel-pivot-corpus-fixtures");
        fixturesSource.Should().Contain("GetExcelPivotCorpusFixturePaths");
        fixturesSource.Should().Contain("Excel_native_pivot_multiple_pivots_one_cache_001.xlsx");
        fixturesSource.Should().Contain("Excel_native_pivot_report_filters_001.xlsx");
        fixturesSource.Should().Contain("Excel_native_pivot_slicer_timeline_001.xlsx");
        fixturesSource.Should().Contain("Excel_native_pivot_filters_sorts_002.xlsx");
        fixturesSource.Should().Contain("Excel_native_pivot_layout_options_002.xlsx");
        fixturesSource.Should().Contain("Excel_native_pivot_date_grouping_003.xlsx");
        fixturesSource.Should().Contain("Excel_native_pivot_calculated_field_item_003.xlsx");
        fixturesSource.Should().Contain("Excel_native_pivot_show_items_no_data_004.xlsx");
        fixturesSource.Should().Contain("Excel_native_pivot_layout_matrix_004.xlsx");
        fixturesSource.Should().Contain("Excel_native_pivot_subtotal_grand_totals_004.xlsx");
        fixturesSource.Should().Contain("Excel_native_pivot_named_range_source_004.xlsx");
        fixturesSource.Should().Contain("Excel_native_pivot_show_values_as_variants_004.xlsx");
        fixturesSource.Should().Contain("Excel_native_pivot_chrome_style_flags_004.xlsx");
        fixturesSource.Should().Contain("AddNativePivotReportFilters");
        fixturesSource.Should().Contain("AddNativePivotShowItemsWithNoData");
        fixturesSource.Should().Contain("AddNativePivotLayoutMatrix");
        fixturesSource.Should().Contain("AddNativePivotSubtotalGrandTotals");
        fixturesSource.Should().Contain("AddNativePivotNamedRangeSource");
        fixturesSource.Should().Contain("AddNativePivotShowValuesAsVariants");
        fixturesSource.Should().Contain("AddNativePivotChromeStyleFlags");
        fixturesSource.Should().Contain("XlPageField");
        fixturesSource.Should().Contain("XlTimeline");
        fixturesSource.Should().Contain("SlicerCaches");
        fixturesSource.Should().Contain("PageFieldOrder");
        fixturesSource.Should().Contain("PageFieldWrapCount");
        fixturesSource.Should().Contain("ShowAllItems");
        fixturesSource.Should().Contain("XlCompactRow");
        fixturesSource.Should().Contain("LayoutSubtotalLocation");
        fixturesSource.Should().Contain("NativeSalesRange");
        fixturesSource.Should().Contain("XlPercentOfColumn");
        fixturesSource.Should().Contain("XlRunningTotal");
        fixturesSource.Should().Contain("ShowDrillIndicators");
        programSource.Should().Contain("ExcelNativePivotCorpusExpectations");
        programSource.Should().Contain("TimelineRelationshipType2011");
        programSource.Should().Contain("IsExpectedSlicerTimelineRelationshipType");
    }

    [Fact]
    public void ExcelOpenSmoke_CellStyleGradientCorpusUsesExcelLinearGradientPatternValue()
    {
        var fixturesSource = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ExcelOpenSmoke", "ExcelSmokeFixtures.cs");

        fixturesSource.Should().Contain("private const int XlPatternLinearGradient = 4000");
        fixturesSource.Should().Contain("((dynamic)interior).Pattern = XlPatternLinearGradient");
        fixturesSource.Should().Contain("((dynamic)gradStops).Clear()");
        fixturesSource.Should().NotContain("Pattern = 2; // xlPatternLinearGradient");
    }

    [Fact]
    public void SheetGridImageCompare_ExposesPivotRangeVisualComparisonMode()
    {
        var source = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.SheetGridImageCompare", "Program.cs");

        source.Should().Contain("--pivot-ranges");
        source.Should().Contain("--pivot-sheet-ranges");
        source.Should().Contain("--export-excel-pngs");
        source.Should().Contain("EnumeratePivotVisualRanges");
        source.Should().Contain("EnumeratePivotSheetVisualRanges");
        source.Should().Contain("SheetUsedRangeWithNativeVisualFilters");
        source.Should().Contain("ResolvePivotVisualRange");
        source.Should().Contain("InferPivotVisualRangeFromCells");
        source.Should().Contain("ResolveExcelPivotVisualRanges");
        source.Should().Contain("TableRange2");
        source.Should().Contain("ExportExcelReferencePngs");
        source.Should().Contain("LoadExcelReferenceDimensions");
        source.Should().Contain("targetPixelDimensions");
        source.Should().Contain("render FreeX to the same pixel canvas");
        source.Should().Contain("var safetyPadding = (captureRange is null && targetPixelDimensions is null) ? 20.0 : 0.0");
        source.Should().Contain("viewW = Math.Max(viewW, captureRange is null ? 200 : 1)");
        source.Should().Contain("viewH = Math.Max(viewH, captureRange is null ? 100 : 1)");
        source.Should().Contain("new RenderTargetBitmap(pixelW, pixelH, 96.0, 96.0, PixelFormats.Pbgra32)");
        source.Should().Contain("ctx.PushTransform(new ScaleTransform(scaleX, scaleY))");
        source.Should().Contain("PivotGridAdornmentPlanner.BuildHeaderTargets");
        source.Should().Contain("PivotGridAdornmentPlanner.BuildRowLabelAdornments");
        source.Should().Contain("IsLikelyBlankReferencePng");
        source.Should().Contain("opaqueRatio");
        source.Should().Contain("TrySaveClipboardImageToPng");
        source.Should().Contain("CopyEnhMetaFile");
        source.Should().Contain("selected bitmap");
        source.Should().Contain("Excel range PNG export produced a blank-looking image");
        source.Should().Contain("CopyPicture");
        source.Should().Contain("ExportExcelRangeToPngThroughPdf");
        source.Should().Contain("Windows.Data.Pdf");
        source.Should().Contain("PrintGridlines = true");
        source.Should().Contain("Copy(Type.Missing, Type.Missing)");
        source.Should().Contain("CropPdfRangePngToLogicalSurface");
        source.IndexOf("CopyPicture", StringComparison.Ordinal)
            .Should().BeLessThan(source.IndexOf("ExportExcelRangeToPngThroughPdf", StringComparison.Ordinal),
                "the PDF route is only a fallback after every CopyPicture attempt");
        source.Should().Contain("GetPngDimensions");
        source.Should().Contain("--fail-on-dimension-mismatch");
        source.Should().Contain("Dimension gate: native Excel and FreeX PNG dimensions must match exactly.");
        source.Should().Contain("Dimension check: native Excel and FreeX PNG dimensions are reported");
        source.Should().Contain("Dimension mismatch: Excel");
        source.Should().Contain("Dimension mismatch warning: Excel");
        source.Should().Contain("800x600 compatibility resize fallback");
        source.Should().Contain("ComputeExactPixelDiff");
        source.Should().Contain("Exact same-size pixel metrics");
        source.Should().Contain("Exact pixels: mean=");
        source.Should().Contain("--pixel-tolerance");
        source.Should().Contain("--strict-pixel-threshold");
        source.Should().Contain("Strict pixel gate");
        source.Should().Contain("PixelDiffMetrics");
        source.Should().Contain("ComparisonFailed");
        source.Should().Contain("WriteMetricsJson");
        source.Should().Contain("\"metrics.json\"");
        source.Should().Contain("\"effectiveStatus\"");
        source.Should().Contain("\"exactPixelMetrics\"");
        source.Should().Contain("\"changedPixelPercent\"");
        source.Should().Contain("\"pivotDropdowns\"");
        source.Should().Contain("Metrics JSON:");
    }

    [Fact]
    public void SaveReopenValidation_CoversCorePackageHealthOnBothSavedPaths()
    {
        var programSource = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ExcelOpenSmoke", "Program.cs");

        AssertCoreValidationCalls(programSource, "freeXSave.SavedPath", "FreeX-saved workbook", "input.SourcePath");
        AssertCoreValidationCalls(programSource, "excelSavedPath", "Excel-saved workbook", "stagedPath");
    }

    [Fact]
    public void FreeXToolingSavePaths_SurfaceSaveWarnings()
    {
        var modelsSource = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ExcelOpenSmoke", "SmokeModels.cs");
        var smokeSource = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ExcelOpenSmoke", "Program.cs");
        var fidelitySource = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.SheetFidelity", "Program.cs");

        modelsSource.Should().Contain("IReadOnlyList<string> SaveWarnings");
        smokeSource.Should().Contain("adapter.SaveWithWarnings(workbook, output)");
        smokeSource.Should().Contain("AssertFreeXSaveWarnings(input, \"FreeX source save\", freeXSave.SaveWarnings)");
        smokeSource.Should().Contain("CombineFreeXWarnings(freeXSave.LoadWarnings, freeXSave.SaveWarnings)");
        fidelitySource.Should().Contain("new XlsxFileAdapter().SaveWithWarnings(workbook, outStream).Warnings");
        fidelitySource.Should().Contain("Save warnings");
    }

    [Fact]
    public void SaveReopenValidation_AllowsExcelWorkbookViewRevisionUid()
    {
        var programSource = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ExcelOpenSmoke", "Program.cs");

        programSource.Should().Contain("SpreadsheetRevision2Ns");
        programSource.Should().Contain("IsKnownNamespacedWorkbookViewAttribute(attribute.Name)");
        programSource.Should().Contain("name == SpreadsheetRevision2Ns + \"uid\"");
    }

    [Fact]
    public void PublicCorpusWarningTolerance_DoesNotAllowSupportedThreadedCommentsWarnings()
    {
        var programSource = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ExcelOpenSmoke", "Program.cs");
        var manifest = TestWorkspaceFiles.ReadWorkspaceText("test-corpus", "manifest.csv");

        manifest.Should().Contain("generated-threaded-comments-001,generated/threaded-comments-001.xlsx,generated,local,2026-06-08,FreeX-generated,threaded-comments,,supported-metadata-pass");
        programSource.Should().NotContain("tags.Contains(\"threaded-comments\")");
        programSource.Should().Contain("tags.Contains(\"unsupported-sheet-types\")");
    }

    [Fact]
    public void MetadataPassHeaderFooterLegacyDrawing_RowRequiresPositiveSmokeCounter()
    {
        var programSource = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ExcelOpenSmoke", "Program.cs");
        var manifest = TestWorkspaceFiles.ReadWorkspaceText("test-corpus", "manifest.csv");
        var expectationBlock = ExtractExpectationBlock(programSource, "generated-header-footer-legacy-drawing-001");

        manifest.Should().Contain("generated-header-footer-legacy-drawing-001,generated/header-footer-legacy-drawing-001.xlsx,generated,local,2026-05-26,FreeX-generated,header-footer legacy-drawing vml-drawing,,supported-metadata-pass");
        expectationBlock.Should().Contain("RequiredFreeXSavedPackageParts");
        expectationBlock.Should().Contain("RequiredExcelSavedPackageRelationships");
        expectationBlock.Should().Contain("MinExcelOpenedHeaderFooterSheets = 1");
        expectationBlock.Should().Contain("MinExcelReopenedHeaderFooterSheets = reopen");
    }

    [Fact]
    public void MetadataPassExpectations_CoverThreadedCommentSummaryCounters()
    {
        var programSource = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ExcelOpenSmoke", "Program.cs");
        var manifest = TestWorkspaceFiles.ReadWorkspaceText("test-corpus", "manifest.csv");
        var expectationBlock = ExtractExpectationBlock(programSource, "generated-threaded-comments-001");

        manifest.Should().Contain("generated-threaded-comments-001,generated/threaded-comments-001.xlsx,generated,local,2026-06-08,FreeX-generated,threaded-comments,,supported-metadata-pass");
        expectationBlock.Should().Contain("RequiredFreeXSavedPackageParts");
        expectationBlock.Should().Contain("RequiredFreeXSavedPackageRelationships");
        expectationBlock.Should().Contain("MinFreeXPreSaveComments = 1");
    }

    [Fact]
    public void SupportedMetadataExpectations_EnforceDataValidationCountPackageReopenedCounter()
    {
        var programSource = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ExcelOpenSmoke", "Program.cs");
        var expectationBlock = ExtractExpectationBlock(programSource, "generated-dv-count-package-003");

        expectationBlock.Should().Contain("MinFreeXReopenedDataValidations = saveReopen ? 10 : 0");
    }

    [Fact]
    public void GeneratedFeatureFixtures_AssertBidirectionalGridFormulaStyleAndLayoutCounters()
    {
        var programSource = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ExcelOpenSmoke", "Program.cs");
        var expectationBlock = ExtractMethodBlock(programSource, "private static WorkbookSmokeExpectations FormulaExpectations");

        expectationBlock.Should().Contain("MinFreeXPreSaveFrozenSheets: expectFreeXPreSave ? 1 : 0");
        expectationBlock.Should().Contain("MinFreeXPreSaveCustomColumnWidths: expectFreeXPreSave ? 4 : 0");
        expectationBlock.Should().Contain("MinFreeXPreSaveStyledCells: expectFreeXPreSave ? 6 : 0");
        expectationBlock.Should().Contain("MinFreeXPreSaveNumberFormatCells: expectFreeXPreSave ? 4 : 0");
        expectationBlock.Should().Contain("MinExcelOpenedFreezePaneSheets: 1");
        expectationBlock.Should().Contain("MinExcelOpenedCustomColumnWidths: 4");
        expectationBlock.Should().Contain("MinExcelOpenedStyledCells: 6");
        expectationBlock.Should().Contain("MinExcelReopenedFreezePaneSheets: saveReopen ? 1 : 0");
        expectationBlock.Should().Contain("MinFreeXReopenedFrozenSheets: saveReopen ? 1 : 0");
        expectationBlock.Should().Contain("MinFreeXReopenedStyledCells: saveReopen ? 6 : 0");
    }

    [Fact]
    public void GeneratedFeatureFixtures_AssertTableAutofilterAndPackagePartsBothDirections()
    {
        var programSource = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ExcelOpenSmoke", "Program.cs");
        var expectationBlock = ExtractMethodBlock(programSource, "private static WorkbookSmokeExpectations StructuredTableExpectations");

        expectationBlock.Should().Contain("MinFreeXPreSaveAutoFilterSheets: expectFreeXPreSave ? minStructuredTables : 0");
        expectationBlock.Should().Contain("MinExcelOpenedAutoFilterSheets: minStructuredTables");
        expectationBlock.Should().Contain("MinExcelReopenedAutoFilterSheets: saveReopen ? minStructuredTables : 0");
        expectationBlock.Should().Contain("MinFreeXReopenedAutoFilterSheets: saveReopen ? minStructuredTables : 0");
        expectationBlock.Should().Contain("RequiredFreeXSavedPackageParts:");
        expectationBlock.Should().Contain("\"xl/tables/table1.xml\"");
        expectationBlock.Should().Contain("RequiredExcelSavedPackageParts:");
    }

    [Fact]
    public void GeneratedFeatureFixtures_AssertProtectionFreezePaneAndPivotPackagePartsBothDirections()
    {
        var programSource = TestWorkspaceFiles.ReadRepoText("tools", "FreeX.ExcelOpenSmoke", "Program.cs");
        var protectionBlock = ExtractMethodBlock(programSource, "private static WorkbookSmokeExpectations ProtectionPageExpectations");
        var pivotBlock = ExtractMethodBlock(programSource, "private static WorkbookSmokeExpectations PivotTableExpectations");

        protectionBlock.Should().Contain("MinFreeXPreSaveFrozenSheets: expectFreeXPreSave ? 1 : 0");
        protectionBlock.Should().Contain("MinExcelOpenedFreezePaneSheets: 1");
        protectionBlock.Should().Contain("MinExcelReopenedFreezePaneSheets: saveReopen ? 1 : 0");
        protectionBlock.Should().Contain("MinFreeXReopenedFrozenSheets: saveReopen ? 1 : 0");

        pivotBlock.Should().Contain("RequiredFreeXSavedPackageParts:");
        pivotBlock.Should().Contain("\"xl/pivotTables/pivotTable1.xml\"");
        pivotBlock.Should().Contain("\"xl/pivotCache/pivotCacheDefinition1.xml\"");
        pivotBlock.Should().Contain("RequiredExcelSavedPackageParts:");
    }

    private static void AssertCoreValidationCalls(
        string source,
        string pathExpression,
        string label,
        string sourcePathExpression)
    {
        source.Should().Contain($"AssertPackageHealth({pathExpression}, \"{label}\", {sourcePathExpression});");
        source.Should().Contain($"AssertNoExcelRecoveryLog({pathExpression}, \"{label}\", {sourcePathExpression});");
        source.Should().Contain($"AssertOpenXmlValid({pathExpression}, \"{label}\");");
        source.Should().Contain($"AssertWorkbookPackageRoot({pathExpression}, \"{label}\", {sourcePathExpression});");
    }

    private static string ExtractExpectationBlock(string source, string rowId)
    {
        var start = source.IndexOf($"row.Id, \"{rowId}\"", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the smoke tool must special-case corpus row {rowId}");

        var nextElse = source.IndexOf("else if (string.Equals(row.Id", start + rowId.Length, StringComparison.Ordinal);
        nextElse.Should().BeGreaterThan(start, $"the smoke expectation block for {rowId} should be bounded by another row block");
        return source[start..nextElse];
    }

    private static string ExtractMethodBlock(string source, string methodStartText)
    {
        var start = source.IndexOf(methodStartText, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the smoke tool must contain {methodStartText}");

        var nextMethod = source.IndexOf("\r\n    private static", start + methodStartText.Length, StringComparison.Ordinal);
        if (nextMethod < 0)
            nextMethod = source.IndexOf("\n    private static", start + methodStartText.Length, StringComparison.Ordinal);

        nextMethod.Should().BeGreaterThan(start, $"the smoke expectation method {methodStartText} should be bounded by another private static member");
        return source[start..nextMethod];
    }
}
