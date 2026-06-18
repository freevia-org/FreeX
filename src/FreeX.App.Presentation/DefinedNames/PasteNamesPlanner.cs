using FreeX.Core.Model;

namespace FreeX.App.Presentation.DefinedNames;

/// <summary>A single Paste Names row: a defined name and its sheet-qualified refers-to text.</summary>
public sealed record PasteNamesItem(string Name, string RefersTo);

/// <summary>Why a Paste List request could not be planned.</summary>
public enum PasteNamesListError
{
    None,
    NoNames,
    NotEnoughColumns,
    NotEnoughRows,
}

/// <summary>
/// Portable (no UI) backing logic for the Paste Names dialog (Formulas ▸ Use in Formula ▸ Paste Names).
/// It projects the workbook's defined names into the dialog rows (sorted by name) and, for the "Paste List"
/// option, plans the two-column block of name/refers-to cell edits anchored at a start address. Kept UI-free
/// so any shell (WPF, Avalonia) can reuse it and so it is unit-testable without a window.
/// </summary>
public static class PasteNamesPlanner
{
    /// <summary>
    /// Projects the workbook's defined names into Paste Names rows (sorted case-insensitively by name). The
    /// caller supplies <paramref name="formatRange"/> so the host controls how a range renders as refers-to
    /// text (e.g. sheet-qualified A1).
    /// </summary>
    public static IReadOnlyList<PasteNamesItem> BuildItems(
        Workbook workbook,
        Func<GridRange, string> formatRange)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(formatRange);

        return workbook.NamedRanges
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new PasteNamesItem(pair.Key, formatRange(pair.Value)))
            .ToList();
    }

    /// <summary>
    /// Plans the "Paste List" cell edits: a two-column block (name | refers-to) written downward from
    /// <paramref name="start"/>, one row per defined name. Returns false (with a populated
    /// <paramref name="error"/>) when there are no names, or the block would run past the sheet's column/row
    /// bounds.
    /// </summary>
    public static bool TryBuildPasteListEdits(
        CellAddress start,
        IReadOnlyList<PasteNamesItem> items,
        out IReadOnlyList<(CellAddress Address, Cell NewCell)> edits,
        out PasteNamesListError error)
    {
        ArgumentNullException.ThrowIfNull(items);

        edits = [];
        error = PasteNamesListError.None;

        if (items.Count == 0)
        {
            error = PasteNamesListError.NoNames;
            return false;
        }

        if (start.Col >= CellAddress.MaxCol)
        {
            error = PasteNamesListError.NotEnoughColumns;
            return false;
        }

        var lastRow = (ulong)start.Row + (ulong)items.Count - 1;
        if (lastRow > CellAddress.MaxRow)
        {
            error = PasteNamesListError.NotEnoughRows;
            return false;
        }

        var plannedEdits = new List<(CellAddress Address, Cell NewCell)>(items.Count * 2);
        for (var index = 0; index < items.Count; index++)
        {
            var row = start.Row + (uint)index;
            plannedEdits.Add((
                new CellAddress(start.Sheet, row, start.Col),
                Cell.FromValue(new TextValue(items[index].Name))));
            plannedEdits.Add((
                new CellAddress(start.Sheet, row, start.Col + 1),
                Cell.FromValue(new TextValue(items[index].RefersTo))));
        }

        edits = plannedEdits;
        return true;
    }
}
