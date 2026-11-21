using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Editing;

/// <summary>One grey "ghost" cell of a live Flash Fill suggestion: the value Flash Fill would write.</summary>
public readonly record struct FlashFillGhostCell(uint Row, uint Col, string Text);

/// <summary>
/// A live Flash Fill suggestion computed while the user types into a cell that sits next to data.
/// <see cref="Ghosts"/> are the cells BELOW the edited cell (the edited cell itself is covered by the
/// in-cell editor); <see cref="EditedCellValue"/> is what the edited cell receives on accept.
/// Accepting runs <see cref="CreateAcceptCommand"/>, i.e. the ordinary undoable
/// <see cref="FlashFillCommand"/> -- the same engine and the same undo step as Ctrl+E.
/// </summary>
public sealed class FlashFillSuggestion
{
    public FlashFillSuggestion(
        SheetId sheetId,
        uint fillColumn,
        uint sourceColumn,
        uint startRow,
        uint endRow,
        uint editedRow,
        string editedCellValue,
        IReadOnlyList<FlashFillGhostCell> ghosts)
    {
        SheetId = sheetId;
        FillColumn = fillColumn;
        SourceColumn = sourceColumn;
        StartRow = startRow;
        EndRow = endRow;
        EditedRow = editedRow;
        EditedCellValue = editedCellValue;
        Ghosts = ghosts;
    }

    public SheetId SheetId { get; }
    public uint FillColumn { get; }
    public uint SourceColumn { get; }
    public uint StartRow { get; }
    public uint EndRow { get; }
    public uint EditedRow { get; }
    public string EditedCellValue { get; }
    public IReadOnlyList<FlashFillGhostCell> Ghosts { get; }

    /// <summary>The fill column's cells the accept command will write (edited cell first).</summary>
    public GridRange AffectedRange => new(
        new CellAddress(SheetId, EditedRow, FillColumn),
        new CellAddress(SheetId, EndRow, FillColumn));

    public FlashFillCommand CreateAcceptCommand() =>
        new(SheetId, FillColumn, SourceColumn, StartRow, EndRow);

    public bool SameGhostsAs(FlashFillSuggestion? other) =>
        other is not null &&
        SheetId == other.SheetId &&
        EditedRow == other.EditedRow &&
        FillColumn == other.FillColumn &&
        Ghosts.SequenceEqual(other.Ghosts);
}

public enum FlashFillSuggestionKey
{
    Other,
    Enter,
    Tab,
    Escape,
}

public enum FlashFillSuggestionKeyAction
{
    /// <summary>The key keeps its normal meaning.</summary>
    None,

    /// <summary>Enter/Tab: cancel the in-cell edit and run the Flash Fill command.</summary>
    Accept,

    /// <summary>Esc: drop the ghost cells (the edit is cancelled by the host as usual).</summary>
    Dismiss,
}

/// <summary>
/// Pure planner behind Excel's "Automatically Flash Fill": decides when a suggestion is offered while
/// a value is typed next to data, computes the ghost values with the existing Flash Fill engine, and
/// resolves accept/dismiss keys. Hosts only render <see cref="FlashFillSuggestion.Ghosts"/> and call
/// <see cref="Resolve"/>.
/// </summary>
public static class FlashFillSuggestionPlanner
{
    private const uint MaxSuggestedRows = 5_000;

    /// <summary>
    /// Plans a suggestion for <paramref name="text"/> being typed into the empty cell
    /// <paramref name="address"/>. Examples are the committed values directly above in the same
    /// column; the typed text is only used to confirm that it begins the value the pattern predicts
    /// (so typing something unrelated silently dismisses the suggestion).
    /// </summary>
    public static FlashFillSuggestion? Plan(
        bool enabled,
        Workbook workbook,
        CellAddress address,
        string? text)
    {
        if (!enabled || workbook is null || string.IsNullOrEmpty(text) || text[0] == '=')
            return null;

        var sheet = workbook.GetSheet(address.Sheet);
        if (sheet is null || address.Row < 2 || !IsBlank(sheet, address.Row, address.Col))
            return null;

        var context = new PlannerContext(workbook);
        foreach (var sourceColumn in CandidateSourceColumns(address.Col))
        {
            if (!HasValue(sheet, address.Row, sourceColumn))
                continue;

            var suggestion = PlanForSource(context, sheet, address, sourceColumn, text);
            if (suggestion is not null)
                return suggestion;
        }

        return null;
    }

    public static FlashFillSuggestionKeyAction Resolve(
        FlashFillSuggestion? suggestion,
        FlashFillSuggestionKey key,
        bool hasModifiers)
    {
        if (suggestion is null || hasModifiers)
            return FlashFillSuggestionKeyAction.None;

        return key switch
        {
            FlashFillSuggestionKey.Enter or FlashFillSuggestionKey.Tab => FlashFillSuggestionKeyAction.Accept,
            FlashFillSuggestionKey.Escape => FlashFillSuggestionKeyAction.Dismiss,
            _ => FlashFillSuggestionKeyAction.None,
        };
    }

    private static IEnumerable<uint> CandidateSourceColumns(uint fillColumn)
    {
        if (fillColumn > 1)
            yield return fillColumn - 1;
        if (fillColumn < CellAddress.MaxCol)
            yield return fillColumn + 1;
    }

    private static FlashFillSuggestion? PlanForSource(
        PlannerContext context,
        Sheet sheet,
        CellAddress address,
        uint sourceColumn,
        string text)
    {
        var row = address.Row;
        var fillColumn = address.Col;

        // Need at least one committed example directly above.
        if (!HasValue(sheet, row - 1, fillColumn) || !HasValue(sheet, row - 1, sourceColumn))
            return null;

        var firstExampleRow = row - 1;
        while (firstExampleRow > 1 &&
               HasValue(sheet, firstExampleRow - 1, fillColumn) &&
               HasValue(sheet, firstExampleRow - 1, sourceColumn))
        {
            firstExampleRow--;
        }

        // Fill only the contiguous run of blank cells that have source data.
        var endRow = row;
        while (endRow < CellAddress.MaxRow &&
               endRow - row < MaxSuggestedRows &&
               HasValue(sheet, endRow + 1, sourceColumn) &&
               IsBlank(sheet, endRow + 1, fillColumn))
        {
            endRow++;
        }

        // The first contiguous row is often a header ("Name" / "First"); retry without it.
        for (var startRow = firstExampleRow; startRow < row; startRow++)
        {
            if (startRow > firstExampleRow + 1)
                break;

            var suggestion = TryPreview(context, sheet, address, sourceColumn, startRow, endRow, text);
            if (suggestion is not null)
                return suggestion;
        }

        return null;
    }

    private static FlashFillSuggestion? TryPreview(
        PlannerContext context,
        Sheet sheet,
        CellAddress address,
        uint sourceColumn,
        uint startRow,
        uint endRow,
        string text)
    {
        var command = new FlashFillCommand(sheet.Id, address.Col, sourceColumn, startRow, endRow);
        FlashFillPreview preview;
        try
        {
            preview = command.Preview(context);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IndexOutOfRangeException)
        {
            return null;
        }

        if (!preview.Success || preview.Cells.Count < 2)
            return null;

        var first = preview.Cells[0];
        if (first.Address.Row != address.Row ||
            first.Value.Length == 0 ||
            !first.Value.StartsWith(text, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var ghosts = new List<FlashFillGhostCell>(preview.Cells.Count - 1);
        for (var i = 1; i < preview.Cells.Count; i++)
        {
            var (cell, value) = preview.Cells[i];
            if (value.Length > 0)
                ghosts.Add(new FlashFillGhostCell(cell.Row, cell.Col, value));
        }

        if (ghosts.Count == 0)
            return null;

        return new FlashFillSuggestion(
            sheet.Id,
            address.Col,
            sourceColumn,
            startRow,
            endRow,
            address.Row,
            first.Value,
            ghosts);
    }

    private static bool HasValue(Sheet sheet, uint row, uint column) => !IsBlank(sheet, row, column);

    private static bool IsBlank(Sheet sheet, uint row, uint column) =>
        sheet.GetValue(row, column) is null or BlankValue or TextValue { Value: "" };

    private sealed class PlannerContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) => Workbook.GetSheet(sheetId)
            ?? throw new InvalidOperationException("Sheet not found.");
    }
}

/// <summary>
/// Holds the live suggestion for the current in-cell edit so both hosts share one lifecycle:
/// recompute on every text change, clear when the edit ends.
/// </summary>
public sealed class FlashFillSuggestionState
{
    public FlashFillSuggestion? Current { get; private set; }

    /// <summary>Recomputes the suggestion; true when what the host must render changed.</summary>
    public bool Update(bool enabled, Workbook workbook, CellAddress address, string? text)
    {
        var next = FlashFillSuggestionPlanner.Plan(enabled, workbook, address, text);
        var changed = next is null ? Current is not null : !next.SameGhostsAs(Current);
        Current = next;
        return changed;
    }

    /// <summary>Drops the suggestion; true when one was showing.</summary>
    public bool Clear()
    {
        var had = Current is not null;
        Current = null;
        return had;
    }
}
