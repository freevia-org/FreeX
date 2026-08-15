using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public enum WorkbookValidationCircleOutcome
{
    Circled,
    NoInvalidData,
    Cleared,
    NothingToClear,
    Pruned,
    Unchanged,
}

public sealed record WorkbookValidationCircleResult(
    WorkbookValidationCircleOutcome Outcome,
    IReadOnlyList<CellAddress> Cells,
    CellAddress? FirstCell,
    int RemovedCount = 0)
{
    public bool HasCircles => Cells.Count > 0;
}

/// <summary>
/// Owns the transient Data Validation circle state on <see cref="Sheet.ValidationCircleCells"/>.
/// Desktop hosts project this state into their native overlays; print, preview, and PDF consumers
/// read the same per-sheet source of truth directly.
/// </summary>
public static class WorkbookValidationCircleWorkflow
{
    public static WorkbookValidationCircleResult CircleInvalidData(Workbook workbook, Sheet sheet)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(sheet);

        var invalid = DataValidationCirclePlanner.FindInvalidDataCells(workbook, sheet).ToArray();
        sheet.ValidationCircleCells = invalid.Length == 0 ? null : invalid;
        return Result(
            invalid.Length == 0
                ? WorkbookValidationCircleOutcome.NoInvalidData
                : WorkbookValidationCircleOutcome.Circled,
            invalid);
    }

    public static WorkbookValidationCircleResult Clear(Sheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var removedCount = sheet.ValidationCircleCells?.Count ?? 0;
        sheet.ValidationCircleCells = null;
        return new WorkbookValidationCircleResult(
            removedCount == 0
                ? WorkbookValidationCircleOutcome.NothingToClear
                : WorkbookValidationCircleOutcome.Cleared,
            [],
            null,
            removedCount);
    }

    public static WorkbookValidationCircleResult Prune(Workbook workbook, Sheet sheet)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(sheet);

        if (sheet.ValidationCircleCells is not { Count: > 0 } current)
        {
            sheet.ValidationCircleCells = null;
            return new WorkbookValidationCircleResult(
                WorkbookValidationCircleOutcome.Unchanged,
                [],
                null);
        }

        var sheetCells = current.Where(address => address.Sheet == sheet.Id).ToArray();
        var pruned = PruneCells(workbook, sheet, sheetCells);
        if (pruned.Count == current.Count && pruned.SequenceEqual(current))
            return Result(WorkbookValidationCircleOutcome.Unchanged, current);

        sheet.ValidationCircleCells = pruned.Count == 0 ? null : pruned;
        return new WorkbookValidationCircleResult(
            WorkbookValidationCircleOutcome.Pruned,
            pruned,
            pruned.Count == 0 ? null : pruned[0],
            current.Count - pruned.Count);
    }

    public static IReadOnlyList<CellAddress> PruneCells(
        Workbook workbook,
        Sheet sheet,
        IReadOnlyList<CellAddress> circledCells)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(circledCells);

        if (circledCells.Count == 0)
            return circledCells;

        var stillInvalid = new HashSet<CellAddress>(
            DataValidationCirclePlanner.FindInvalidDataCells(workbook, sheet));
        var pruned = circledCells
            .Where(address => address.Sheet != sheet.Id || stillInvalid.Contains(address))
            .ToArray();

        return pruned.Length == circledCells.Count && pruned.SequenceEqual(circledCells)
            ? circledCells
            : pruned;
    }

    private static WorkbookValidationCircleResult Result(
        WorkbookValidationCircleOutcome outcome,
        IReadOnlyList<CellAddress> cells) =>
        new(outcome, cells, cells.Count == 0 ? null : cells[0]);
}
