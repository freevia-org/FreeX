using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed record WorkbookCellEditResult(
    bool Success,
    string? ErrorMessage,
    IReadOnlyList<CellAddress> AffectedCells,
    RecalcReport? RecalcReport,
    // R132-clipboard-cut-move-os-invalidation: true when this successful paste consumed a Cut
    // FreeX-internal clipboard as a MOVE (WorkbookSession.PasteInternalClipboardAtActiveCell).
    // The WPF host already invalidates the real OS clipboard once such a move completes
    // (MainWindow.ClipboardCommands.InvalidateOsClipboardAfterCutMove) so a later Ctrl+V can't
    // re-paste the already-moved content a second time via its external-clipboard fallback; the
    // Avalonia shell has no such call and relies on this flag to know when to make the matching
    // IClipboard.ClearAsync() call itself. Defaults to false so every pre-existing positional
    // construction of this record (none of which cares about this signal) keeps compiling.
    bool ClipboardCutMoveCompleted = false);

/// <summary>
/// Describes a Warning/Information ("AskToContinue") data-validation alert that
/// <see cref="WorkbookSession.CommitCellText"/> needs a host decision for before it can commit (or
/// discard) an invalid entry -- see <see cref="WorkbookSession.DataValidationPromptResolver"/>.
/// </summary>
public readonly record struct DataValidationPromptRequest(string Message, string Title, DvAlertStyle AlertStyle);
