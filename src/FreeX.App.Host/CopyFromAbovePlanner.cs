using FreeX.Core.Model;

namespace FreeX.App.Host;

public static class CopyFromAbovePlanner
{
    public static (CellAddress Address, Cell NewCell)? CreateEdit(
        Sheet sheet,
        CellAddress target,
        CopyFromAboveMode mode)
    {
        if (!HasSourceRow(target))
            return null;

        var source = GetSourceCell(sheet, target);
        var newCell = CreateCopiedCell(source, mode);

        return (target, newCell);
    }

    private static bool HasSourceRow(CellAddress target) => target.Row > 1;

    private static Cell GetSourceCell(Sheet sheet, CellAddress target)
    {
        var sourceAddress = new CellAddress(target.Sheet, target.Row - 1, target.Col);
        return sheet.GetCell(sourceAddress) ?? Cell.FromValue(BlankValue.Instance);
    }

    private static Cell CreateCopiedCell(Cell source, CopyFromAboveMode mode) =>
        mode == CopyFromAboveMode.FormulaOrContent && source.FormulaText is { } formula
            ? Cell.FromFormula(formula)
            : Cell.FromValue(source.Value);
}

public enum CopyFromAboveMode
{
    FormulaOrContent,
    Value
}
