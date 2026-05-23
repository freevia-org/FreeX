using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class DataValidationDialog
{
    public static bool TryValidateCriteriaInputs(
        string typeTag,
        string operatorTag,
        string? formula1,
        string? formula2,
        out string? error)
    {
        error = null;
        if (string.Equals(typeTag, "Any", StringComparison.Ordinal))
            return true;

        var first = formula1?.Trim() ?? "";
        var second = formula2?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(first))
        {
            error = typeTag switch
            {
                "List" => "Source is required.",
                "Custom" => "Formula is required.",
                _ => "Value is required."
            };
            return false;
        }

        if (RequiresSecondFormula(typeTag, operatorTag) && string.IsNullOrWhiteSpace(second))
        {
            error = "Maximum is required.";
            return false;
        }

        return true;
    }

    private static bool RequiresSecondFormula(string typeTag, string operatorTag) =>
        typeTag is not "Any" and not "List" and not "Custom"
        && operatorTag is "Between" or "NotBetween";

    public static DataValidationRangeSelectionRequest CreateRangeSelectionRequest(
        DataValidationRangeSelectionTarget target,
        string currentText) =>
        new(target, currentText.Trim(), CollapseDialog: true);

    private static string TypeTag(DvType type) => type switch
    {
        DvType.List => "List",
        DvType.WholeNumber => "WholeNumber",
        DvType.Decimal => "Decimal",
        DvType.Date => "Date",
        DvType.Time => "Time",
        DvType.TextLength => "TextLength",
        DvType.Custom => "Custom",
        _ => "Any"
    };

    private static string OperatorTag(DvOperator op) => op switch
    {
        DvOperator.NotBetween => "NotBetween",
        DvOperator.Equal => "Equal",
        DvOperator.NotEqual => "NotEqual",
        DvOperator.GreaterThan => "GreaterThan",
        DvOperator.LessThan => "LessThan",
        DvOperator.GreaterThanOrEqual => "GreaterThanOrEqual",
        DvOperator.LessThanOrEqual => "LessThanOrEqual",
        _ => "Between"
    };

    private static string AlertStyleTag(DvAlertStyle style) => style switch
    {
        DvAlertStyle.Warning => "Warning",
        DvAlertStyle.Information => "Information",
        _ => "Stop"
    };
}
