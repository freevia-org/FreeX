using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed record WorkbookCellEditResult(
    bool Success,
    string? ErrorMessage,
    IReadOnlyList<CellAddress> AffectedCells,
    RecalcReport? RecalcReport,
    WorkbookCellEditFailure? Failure = null,
    bool IsNoOp = false,
    DrawingObjectSelectionHint? DrawingObjectSelection = null,
    // True when a successful paste consumed an internal Cut clipboard as a move. Renderers use this
    // signal to invalidate the matching OS clipboard payload so it cannot be pasted a second time.
    bool ClipboardCutMoveCompleted = false);

public enum WorkbookCellEditFailureKind
{
    InvalidEntrySyntax,
    DataValidationBlocked,
    DataValidationDeclined
}

/// <summary>
/// Structured edit failure details that let each renderer present native validation/formula UI
/// without reimplementing portable entry parsing and validation policy.
/// </summary>
public sealed record WorkbookCellEditFailure(
    WorkbookCellEditFailureKind Kind,
    string? Title = null,
    DvAlertStyle? AlertStyle = null,
    UserMessageResult? PromptDecision = null);

/// <summary>
/// Describes a Warning/Information ("AskToContinue") data-validation alert that
/// <see cref="WorkbookSession.CommitCellText"/> needs a host decision for before it can commit (or
/// discard) an invalid entry -- see <see cref="WorkbookSession.DataValidationPromptResolver"/>.
/// </summary>
public readonly record struct DataValidationPromptRequest(string Message, string Title, DvAlertStyle AlertStyle);

/// <summary>
/// Describes Excel's "Sort Warning" -- the selection being sorted is a proper subset of a larger
/// contiguous data block, so sorting it as-is would reorder some columns/rows of a record without
/// their related cells. <see cref="SelectedRange"/> is what the user actually selected;
/// <see cref="ExpandedRange"/> is the surrounding current-region block FreeX would sort instead if
/// the host resolves <see cref="UserMessageResult.Yes"/> ("Expand the selection"). Any other
/// result ("Continue with the current selection") sorts <see cref="SelectedRange"/> unchanged --
/// see <see cref="WorkbookSession.SortAdjacentDataPromptResolver"/>.
/// </summary>
public readonly record struct SortAdjacentDataPromptRequest(GridRange SelectedRange, GridRange ExpandedRange);
