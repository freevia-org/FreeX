using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed record WorkbookDataValidationMutationResult(
    bool Success,
    string? ErrorMessage,
    bool Mutated,
    IReadOnlyList<CellAddress> AffectedCells,
    RecalcReport? RecalcReport)
{
    public static WorkbookDataValidationMutationResult NoMutation() =>
        new(true, null, Mutated: false, [], RecalcReport: null);

    public static WorkbookDataValidationMutationResult FromEditResult(
        WorkbookCellEditResult result,
        bool mutated) =>
        new(result.Success, result.ErrorMessage, result.Success && mutated, result.AffectedCells, result.RecalcReport);
}
