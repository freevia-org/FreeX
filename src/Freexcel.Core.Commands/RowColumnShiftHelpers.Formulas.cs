using Freexcel.Core.Formula;
using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

internal static partial class RowColumnShiftHelpers
{
    internal static void RewriteAllFormulas(
        Workbook workbook, RewriteOperation op, Dictionary<CellAddress, string> snapshot)
    {
        foreach (var sheet in workbook.Sheets)
        {
            foreach (var (addr, cell) in sheet.EnumerateCells())
            {
                if (cell.FormulaText is null) continue;
                var rewritten = FormulaRewriter.Rewrite(cell.FormulaText, op, sheet.Name);
                if (rewritten is null) continue;
                snapshot[addr] = cell.FormulaText;
                cell.FormulaText = rewritten;
            }
        }
    }

    internal static void RestoreFormulas(
        Workbook workbook, Dictionary<CellAddress, string> snapshot)
    {
        foreach (var (addr, original) in snapshot)
        {
            var s = workbook.GetSheet(addr.Sheet);
            var cell = s?.GetCell(addr.Row, addr.Col);
            if (cell is not null)
                cell.FormulaText = original;
        }
        snapshot.Clear();
    }
}
