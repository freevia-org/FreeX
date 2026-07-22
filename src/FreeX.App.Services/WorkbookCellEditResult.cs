using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed record WorkbookCellEditResult(
    bool Success,
    string? ErrorMessage,
    IReadOnlyList<CellAddress> AffectedCells,
    RecalcReport? RecalcReport);

/// <summary>
/// Describes a Warning/Information ("AskToContinue") data-validation alert that
/// <see cref="WorkbookSession.CommitCellText"/> needs a host decision for before it can commit (or
/// discard) an invalid entry -- see <see cref="WorkbookSession.DataValidationPromptResolver"/>.
/// </summary>
public readonly record struct DataValidationPromptRequest(string Message, string Title, DvAlertStyle AlertStyle);
