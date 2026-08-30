using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal readonly record struct CellStateSnapshot(
    uint Row,
    uint Col,
    CellCopyState State)
{
    public string? FormulaText => State.FormulaText;

    public static CellStateSnapshot Capture(CellAddress address, Cell cell) =>
        new(address.Row, address.Col, cell.CaptureCopyState());

    public CellAddress ToAddress(SheetId sheetId) => new(sheetId, Row, Col);

    public Cell ToCell() => Cell.FromCopyState(State);
}
