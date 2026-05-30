using FreeX.Core.Model;

namespace FreeX.App.Host;

public static class ConditionalFormatDialogPlanner
{
    public static ConditionalFormat CloneRule(ConditionalFormat source)
        => ManageConditionalFormatsPlanner.CloneWithPriority(source, source.Priority);

    public static string RuleTypeLabel(ConditionalFormat cf) => cf.RuleType switch
    {
        CfRuleType.Formula => "Formula",
        CfRuleType.DataBar => "Data Bar",
        CfRuleType.ColorScale => "Color Scale",
        CfRuleType.IconSet => "Icon Set",
        CfRuleType.ContainsText => "Text Contains",
        CfRuleType.NotContainsText => "Text Does Not Contain",
        CfRuleType.BeginsWith => "Text Begins With",
        CfRuleType.EndsWith => "Text Ends With",
        CfRuleType.DateOccurring => "Date Occurring",
        CfRuleType.Blanks => "Blanks",
        CfRuleType.NoBlanks => "No Blanks",
        CfRuleType.Errors => "Errors",
        CfRuleType.NoErrors => "No Errors",
        CfRuleType.DuplicateValues or CfRuleType.UniqueValues => "Duplicate Values",
        CfRuleType.AboveAverage => cf.AboveAverage ? "Above Average" : "Below Average",
        CfRuleType.Top10 => cf.TopBottomPercent
            ? (cf.AboveAverage ? "Top 10%" : "Bottom 10%")
            : (cf.AboveAverage ? "Top 10 Items" : "Bottom 10 Items"),
        CfRuleType.CellValue => cf.Operator switch
        {
            CfOperator.GreaterThan => "Greater Than",
            CfOperator.LessThan => "Less Than",
            CfOperator.Equal => "Equal To",
            CfOperator.Between => "Between",
            CfOperator.NotEqual => "Not Equal To",
            CfOperator.GreaterThanOrEqual => "Greater Than Or Equal To",
            CfOperator.LessThanOrEqual => "Less Than Or Equal To",
            CfOperator.NotBetween => "Not Between",
            _ => "Greater Than"
        },
        _ => "Greater Than"
    };
}
