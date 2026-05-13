using System.Text.RegularExpressions;
using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>
/// Fills a range by repeating the last cell of <paramref name="sourceRange"/>.
/// Formulas have relative cell references incremented by the fill offset.
/// </summary>
public sealed partial class AutofillCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _sourceRange;
    private readonly GridRange _fillRange;
    private List<(CellAddress Addr, Cell? OldCell)>? _snapshot;

    public string Label => "Autofill";

    public AutofillCommand(SheetId sheetId, GridRange sourceRange, GridRange fillRange)
    {
        _sheetId     = sheetId;
        _sourceRange = sourceRange;
        _fillRange   = fillRange;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);

        var sourceAddr = _sourceRange.End;
        var sourceCell = sheet.GetCell(sourceAddr);

        _snapshot = [];

        foreach (var addr in _fillRange.AllCells())
        {
            _snapshot.Add((addr, sheet.GetCell(addr)?.Clone()));

            if (sourceCell is null)
            {
                sheet.ClearCell(addr);
                continue;
            }

            int rowOffset = (int)addr.Row - (int)sourceAddr.Row;
            int colOffset = (int)addr.Col - (int)sourceAddr.Col;

            Cell newCell;
            if (sourceCell.HasFormula && sourceCell.FormulaText is not null)
            {
                var shifted = ShiftFormula(sourceCell.FormulaText, rowOffset, colOffset);
                newCell = Cell.FromFormula(shifted);
            }
            else
            {
                newCell = Cell.FromValue(sourceCell.Value);
            }

            newCell.StyleId = sourceCell.StyleId;
            sheet.SetCell(addr, newCell);
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null) return;
        var sheet = ctx.GetSheet(_sheetId);
        foreach (var (addr, oldCell) in _snapshot)
        {
            if (oldCell is null) sheet.ClearCell(addr);
            else sheet.SetCell(addr, oldCell.Clone());
        }
    }

    /// <summary>
    /// Shift relative cell references in a formula by rowOffset / colOffset.
    /// Pattern: (\$?)([A-Z]{1,3})(\$?)(\d{1,7})
    /// Group 1 = optional $ before column  (absolute col marker)
    /// Group 2 = column letters
    /// Group 3 = optional $ before row     (absolute row marker)
    /// Group 4 = row digits
    /// </summary>
    private static string ShiftFormula(string formula, int rowOffset, int colOffset)
    {
        return CellRefPattern().Replace(formula, m =>
        {
            bool absCol = m.Groups[1].Value == "$";
            bool absRow = m.Groups[3].Value == "$";
            var colLetters = m.Groups[2].Value;
            uint rowNum = uint.Parse(m.Groups[4].Value);
            uint colNum = CellAddress.ColumnNameToNumber(colLetters);

            if (!absCol && colOffset != 0)
                colNum = (uint)Math.Max(1, (int)colNum + colOffset);
            if (!absRow && rowOffset != 0)
                rowNum = (uint)Math.Max(1, (int)rowNum + rowOffset);

            return m.Groups[1].Value
                 + CellAddress.NumberToColumnName(colNum)
                 + m.Groups[3].Value
                 + rowNum;
        });
    }

    [GeneratedRegex(@"(\$?)([A-Z]{1,3})(\$?)(\d{1,7})")]
    private static partial Regex CellRefPattern();
}
