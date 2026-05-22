using Freexcel.Core.Commands;

namespace Freexcel.App.Host;

public static class GoToSpecialInputParser
{
    public static GoToSpecialKind Parse(string input) =>
        input.Trim().ToLowerInvariant() switch
        {
            "constant" or "constants" => GoToSpecialKind.Constants,
            "formula" or "formulas" => GoToSpecialKind.Formulas,
            "comment" or "comments" => GoToSpecialKind.Comments,
            "validation" or "data validation" => GoToSpecialKind.DataValidation,
            "visible" or "visible cells" => GoToSpecialKind.VisibleCellsOnly,
            "row differences" or "row difference" => GoToSpecialKind.RowDifferences,
            "column differences" or "column difference" => GoToSpecialKind.ColumnDifferences,
            "current region" => GoToSpecialKind.CurrentRegion,
            "last cell" => GoToSpecialKind.LastCell,
            "conditional format" or "conditional formats" => GoToSpecialKind.ConditionalFormats,
            _ => GoToSpecialKind.Blanks
        };
}
