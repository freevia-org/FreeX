using FreeX.Core.Model;

namespace FreeX.App.Host;

internal static class SelectionMoveOverwritePlanner
{
    public static bool HasOverwriteTargets(Sheet sheet, GridRange sourceRange, GridRange targetRange) =>
        FindOverwriteTargets(sheet, sourceRange, targetRange).Count > 0;

    public static IReadOnlyList<CellAddress> FindOverwriteTargets(
        Sheet sheet,
        GridRange sourceRange,
        GridRange targetRange)
    {
        var targets = new List<CellAddress>();
        foreach (var address in targetRange.AllCells())
        {
            if (sourceRange.Contains(address))
                continue;

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
