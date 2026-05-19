using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class CopyFromAbovePlanner
{
    public static (CellAddress Address, Cell NewCell)? CreateEdit(
        Sheet sheet,
        CellAddress target,
        CopyFromAboveMode mode)
    {
        if (target.Row <= 1)
            return null;

        var sourceAddress = new CellAddress(target.Sheet, target.Row - 1, target.Col);
        var source = sheet.GetCell(sourceAddress) ?? Cell.FromValue(BlankValue.Instance);
        var newCell = mode == CopyFromAboveMode.FormulaOrContent && source.FormulaText is { } formula
            ? Cell.FromFormula(formula)
            : Cell.FromValue(source.Value);

        return (target, newCell);
    }
}

public enum CopyFromAboveMode
{
    FormulaOrContent,
    Value
}
