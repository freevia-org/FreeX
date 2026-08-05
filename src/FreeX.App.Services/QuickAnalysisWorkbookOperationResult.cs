using FreeX.Core.Model;

namespace FreeX.App.Services;

public enum QuickAnalysisWorkbookOperationFailure
{
    None,
    InvalidOperation,
    InvalidSparklineSelection,
    CommandFailed,
}

public sealed record QuickAnalysisWorkbookOperationResult(
    WorkbookCellEditResult EditResult,
    QuickAnalysisWorkbookOperationFailure Failure,
    int AppliedItemCount,
    GridRange SourceRange,
    CellAddress? SelectedCell,
    string CommandTitle)
{
    public bool Success => EditResult.Success;
    public bool IsNoOp => EditResult.IsNoOp;
    public string? ErrorMessage => EditResult.ErrorMessage;
}
