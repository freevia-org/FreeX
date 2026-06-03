internal enum WorkbookValidationWorkflow
{
    DirectExcel,
    FreeXSaveThenExcel
}

internal sealed record WorkbookSmokeInput(
    string SourcePath,
    WorkbookValidationWorkflow Workflow,
    string Description,
    bool GenerateWithExcel = false,
    CorpusManifestRow? CorpusRow = null,
    WorkbookSmokeExpectations? Expectations = null);

internal sealed record WorkbookSmokeExpectations(
    int MinFreeXPreSaveFormulaCells = 0,
    int MinFreeXPreSaveStructuredTables = 0,
    int MinFreeXPreSaveDataValidations = 0,
    int MinFreeXPreSaveConditionalFormats = 0,
    int MinFreeXPreSaveHyperlinks = 0,
    int MinFreeXPreSaveComments = 0,
    int MinFreeXPreSavePictures = 0,
    int MinFreeXPreSaveSparklines = 0,
    int MinFreeXPreSaveTextBoxes = 0,
    int MinFreeXPreSaveDrawingShapes = 0,
    int MinFreeXPreSaveProtectedSheets = 0,
    int MinFreeXPreSaveStructureProtection = 0,
    int MinExcelOpenedFormulaCells = 0,
    int MinExcelOpenedStructuredTables = 0,
    int MinExcelOpenedShapes = 0,
    int MinExcelReopenedFormulaCells = 0,
    int MinExcelReopenedStructuredTables = 0,
    int MinExcelReopenedShapes = 0,
    int MinFreeXReopenedFormulaCells = 0,
    int MinFreeXReopenedStructuredTables = 0,
    int MinFreeXReopenedDataValidations = 0,
    int MinFreeXReopenedConditionalFormats = 0,
    int MinFreeXReopenedHyperlinks = 0,
    int MinFreeXReopenedComments = 0,
    int MinFreeXReopenedPictures = 0,
    int MinFreeXReopenedSparklines = 0,
    int MinFreeXReopenedTextBoxes = 0,
    int MinFreeXReopenedDrawingShapes = 0,
    int MinFreeXReopenedProtectedSheets = 0,
    int MinFreeXReopenedStructureProtection = 0,
    int MinFreeXPreSavePivotTables = 0,
    int MinFreeXPreSavePivotCaches = 0,
    int MinExcelOpenedPivotTables = 0,
    int MinExcelReopenedPivotTables = 0,
    int MinFreeXReopenedPivotTables = 0,
    int MinFreeXReopenedPivotCaches = 0);

internal sealed record WorkbookSmokeResult(
    bool Success,
    WorkbookSmokeInput Input,
    string? StagedPath,
    string? FreeXSavedPath,
    string? ExcelSavedPath,
    ExcelWorkbookSummary? Opened,
    ExcelWorkbookSummary? Reopened,
    FreeXWorkbookSummary? FreeXPreSave,
    FreeXWorkbookSummary? FreeXReopenedExcelSave,
    string? Error)
{
    public static WorkbookSmokeResult Pass(
        WorkbookSmokeInput input,
        string stagedPath,
        string? freeXSavedPath,
        string? ExcelSavedPath,
        ExcelWorkbookSummary opened,
        ExcelWorkbookSummary? Reopened,
        FreeXWorkbookSummary? freeXPreSave,
        FreeXWorkbookSummary? FreeXReopenedExcelSave) =>
        new(
            true,
            input,
            stagedPath,
            freeXSavedPath,
            ExcelSavedPath,
            opened,
            Reopened,
            freeXPreSave,
            FreeXReopenedExcelSave,
            null);

    public static WorkbookSmokeResult Fail(WorkbookSmokeInput input, string? freeXSavedPath, string error) =>
        new(false, input, null, freeXSavedPath, null, null, null, null, null, error);
}

internal sealed record ExcelWorkbookSummary(
    int WorksheetCount,
    int ShapeCount,
    int FormulaCellCount,
    int StructuredTableCount,
    int PivotTableCount);
internal sealed record FreeXWorkbookSummary(
    int SheetCount,
    int CellCount,
    int FormulaCellCount,
    int StructuredTableCount,
    int DataValidationCount,
    int ConditionalFormatCount,
    int HyperlinkCount,
    int CommentCount,
    int PictureCount,
    int SparklineCount,
    int TextBoxCount,
    int DrawingShapeCount,
    int ProtectedSheetCount,
    int StructureProtectionCount,
    int PivotTableCount,
    int PivotCacheCount);
internal sealed record FreeXSaveResult(string SavedPath, FreeXWorkbookSummary Summary);
internal sealed record ExcelSaveReopenResult(
    string ExcelSavedPath,
    ExcelWorkbookSummary Opened,
    ExcelWorkbookSummary Reopened);
internal sealed record ExcelSmokeSummary(
    int Total,
    int Passed,
    int Failed,
    IReadOnlyList<WorkbookSmokeResult> Results);

internal sealed record CorpusManifestRow(
    string Id,
    string RelativePath,
    string SourceType,
    string SourceUrl,
    string RetrievedOn,
    string License,
    string FeatureTags,
    string ExpectedWarnings,
    string ExpectedStatus,
    string Notes);

internal sealed record CorpusManifestSkip(
    CorpusManifestRow Row,
    string Reason,
    string? FullPath);

internal sealed record CorpusManifestSelection(
    string ManifestPath,
    IReadOnlyList<WorkbookSmokeInput> Inputs,
    IReadOnlyList<CorpusManifestSkip> Skipped);
