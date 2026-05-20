using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public enum GoToSpecialKind
{
    Blanks,
    Constants,
    Formulas,
    Comments,
    DataValidation,
    VisibleCellsOnly
}

public static class GoToSpecialService
{
    public static IReadOnlyList<CellAddress> Find(Sheet sheet, GridRange range, GoToSpecialKind kind)
    {
        var result = new List<CellAddress>();
        foreach (var address in range.AllCells())
        {
            if (kind == GoToSpecialKind.VisibleCellsOnly)
            {
                if (!sheet.IsRowEffectivelyHidden(address.Row) &&
                    !sheet.IsColEffectivelyHidden(address.Col))
                {
                    result.Add(address);
                }
                continue;
            }

            var cell = sheet.GetCell(address);
            switch (kind)
            {
                case GoToSpecialKind.Blanks when cell is null || cell.Value is BlankValue:
                    result.Add(address);
                    break;
                case GoToSpecialKind.Constants when cell is { HasFormula: false } && cell.Value is not BlankValue:
                    result.Add(address);
                    break;
                case GoToSpecialKind.Formulas when cell?.HasFormula == true:
                    result.Add(address);
                    break;
                case GoToSpecialKind.Comments when sheet.Comments.ContainsKey(address) || sheet.ThreadedComments.ContainsKey(address):
                    result.Add(address);
                    break;
                case GoToSpecialKind.DataValidation when sheet.DataValidations.Any(rule => rule.AppliesTo.Contains(address)):
                    result.Add(address);
                    break;
            }
        }

        return result;
    }
}
