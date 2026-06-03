using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal readonly record struct CellStateSnapshot(
    CellAddress Address,
    ScalarValue Value,
    string? FormulaText,
    object? CachedAst,
    bool IgnoreFormulaError,
    StyleId StyleId)
{
    public static CellStateSnapshot Capture(CellAddress address, Cell cell) =>
        new(address, cell.Value, cell.FormulaText, cell.CachedAst, cell.IgnoreFormulaError, cell.StyleId);

    public Cell ToCell()
    {
        var cell = new Cell
        {
            Value = Value,
            IgnoreFormulaError = IgnoreFormulaError,
            StyleId = StyleId
        };

        cell.FormulaText = FormulaText;
        cell.CachedAst = CachedAst;
        return cell;
    }
}
