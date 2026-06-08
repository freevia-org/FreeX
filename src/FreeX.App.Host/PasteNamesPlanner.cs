using FreeX.Core.Model;

namespace FreeX.App.Host;

internal sealed record PasteNamesDialogItem(string Name, string RefersTo);

internal static class PasteNamesPlanner
{
    public static IReadOnlyList<PasteNamesDialogItem> BuildItems(
        Workbook workbook,
        Func<GridRange, string> formatRange)
    {
        return workbook.NamedRanges
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new PasteNamesDialogItem(pair.Key, formatRange(pair.Value)))
            .ToList();
    }

    public static bool TryBuildPasteListEdits(
        CellAddress start,
        IReadOnlyList<PasteNamesDialogItem> items,
        out IReadOnlyList<(CellAddress Address, Cell NewCell)> edits,
        out string? error)
    {
        edits = [];
        error = null;

        if (items.Count == 0)
        {
            error = UiText.Get("PasteNames_NoNamesMessage");
            return false;
        }

        if (start.Col >= CellAddress.MaxCol)
        {
            error = UiText.Get("PasteNames_NotEnoughColumnsMessage");
            return false;
        }

        var lastRow = (ulong)start.Row + (ulong)items.Count - 1;
        if (lastRow > CellAddress.MaxRow)
        {
            error = UiText.Get("PasteNames_NotEnoughRowsMessage");
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
