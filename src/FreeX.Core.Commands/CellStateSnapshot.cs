using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal readonly record struct CellStateSnapshot(
    uint Row,
    uint Col,
    ScalarValue Value,
    string? FormulaText,
    object? CachedAst,
    bool IgnoreFormulaError,
    StyleId StyleId,
    FormulaArrayMode ArrayMode,
    uint LegacyArrayRows,
    uint LegacyArrayCols)
{
    public static CellStateSnapshot Capture(CellAddress address, Cell cell) =>
        new(address.Row, address.Col, cell.Value, cell.FormulaText, cell.CachedAst, cell.IgnoreFormulaError, cell.StyleId, cell.ArrayMode, cell.LegacyArrayRows, cell.LegacyArrayCols);

    public CellAddress ToAddress(SheetId sheetId) => new(sheetId, Row, Col);

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
        // Assign ArrayMode/LegacyArray* after FormulaText: the FormulaText setter resets all
        // three to their modern-dynamic-formula defaults.
        cell.ArrayMode = ArrayMode;
        cell.LegacyArrayRows = LegacyArrayRows;
        cell.LegacyArrayCols = LegacyArrayCols;
        return cell;
    }
}
