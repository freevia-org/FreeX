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
    int MinExcelOpenedFormulaCells = 0,
    int MinExcelOpenedStructuredTables = 0,
    int MinExcelReopenedFormulaCells = 0,
    int MinExcelReopenedStructuredTables = 0,
    int MinFreeXReopenedFormulaCells = 0,
    int MinFreeXReopenedStructuredTables = 0,
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
