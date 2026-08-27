using FreeX.Core.Model;

namespace FreeX.App.Presentation.GridInteraction;

public static class SelectionMoveOverwritePlanner
{
    public static bool HasOverwriteTargets(Sheet sheet, GridRange sourceRange, GridRange targetRange) =>
        FindOverwriteTargets(sheet, sourceRange, targetRange).Count > 0;

    public static IReadOnlyList<CellAddress> FindOverwriteTargets(
        Sheet sheet,
        GridRange sourceRange,
        GridRange targetRange)
    {
        // r164 remediation, dense whole-sheet enumeration: this walked targetRange.AllCells(), so
        // dragging a select-all selection asked for 17,179,869,184 iterations on the synchronous UI
        // thread. Only cells WITH content can ever be returned, so visiting the sheet's occupied
        // cells is exactly equivalent and costs the size of the document instead of the selection.
        // HasContent below can be true for a cell that has no entry in the occupied-cell map -- a
        // dynamic-array spill target, or a cell carrying only a note/comment/hyperlink -- so every
        // collection it consults is visited here, not just the value cells. Sorted so the result
        // keeps the row-major order the dense walk produced.
        var candidates = sheet.GetOccupiedCells()
            .Select(cell => new CellAddress(sheet.Id, cell.Row, cell.Col))
            .Concat(sheet.EnumerateSpillTargetCells())
            .Concat(sheet.Comments.Keys)
            .Concat(sheet.ThreadedComments.Keys)
            .Concat(sheet.Hyperlinks.Keys)
            .Concat(sheet.HyperlinkMetadata.Keys)
            .Where(address => targetRange.Contains(address) && !sourceRange.Contains(address))
            .Distinct()
            .OrderBy(address => address.Row)
            .ThenBy(address => address.Col);

        var targets = new List<CellAddress>();
        foreach (var address in candidates)
        {
            if (HasContent(sheet, address))
                targets.Add(address);
        }

        return targets;
    }

    private static bool HasContent(Sheet sheet, CellAddress address)
    {
        if (sheet.GetCell(address)?.HasFormula == true)
            return true;

        if (sheet.GetValue(address) is not BlankValue)
            return true;

        return sheet.Comments.ContainsKey(address) ||
            sheet.ThreadedComments.ContainsKey(address) ||
            sheet.Hyperlinks.ContainsKey(address) ||
            sheet.HyperlinkMetadata.ContainsKey(address);
    }
}
