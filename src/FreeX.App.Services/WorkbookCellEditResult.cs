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
