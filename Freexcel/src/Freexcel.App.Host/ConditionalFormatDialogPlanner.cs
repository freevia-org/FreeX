using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class ConditionalFormatDialogPlanner
{
    public static ConditionalFormat CloneRule(ConditionalFormat source)
    {
        var clone = new ConditionalFormat
        {
            Id = source.Id,
            AppliesTo = source.AppliesTo,
            Priority = source.Priority,
            RuleType = source.RuleType,
            Operator = source.Operator,
            Value1 = source.Value1,
            Value2 = source.Value2,
            FormatIfTrue = source.FormatIfTrue?.Clone(),
            MinColor = source.MinColor,
            MidColor = source.MidColor,
            MaxColor = source.MaxColor,
            UseThreeColorScale = source.UseThreeColorScale,
            MinThresholdType = source.MinThresholdType,
            MinThresholdValue = source.MinThresholdValue,
            MidThresholdType = source.MidThresholdType,
            MidThresholdValue = source.MidThresholdValue,
            MaxThresholdType = source.MaxThresholdType,
            MaxThresholdValue = source.MaxThresholdValue,
            DataBarColor = source.DataBarColor,
            DataBarMinThresholdType = source.DataBarMinThresholdType,
            DataBarMinThresholdValue = source.DataBarMinThresholdValue,
            DataBarMaxThresholdType = source.DataBarMaxThresholdType,
            DataBarMaxThresholdValue = source.DataBarMaxThresholdValue,
            DataBarShowValue = source.DataBarShowValue,
            DataBarMinLength = source.DataBarMinLength,
            DataBarMaxLength = source.DataBarMaxLength,
            AboveAverage = source.AboveAverage,
            FormulaText = source.FormulaText,
            IconSetStyle = source.IconSetStyle,
            IconSetShowValue = source.IconSetShowValue,
            IconSetReverse = source.IconSetReverse,
            TopBottomRank = source.TopBottomRank,
            TopBottomPercent = source.TopBottomPercent,
            TextRuleText = source.TextRuleText,
            DateOccurringPeriod = source.DateOccurringPeriod,
            StopIfTrue = source.StopIfTrue
        };
        clone.IconSetThresholds.AddRange(source.IconSetThresholds);
        return clone;
    }

    public static string RuleTypeLabel(ConditionalFormat cf) => cf.RuleType switch
    {
        CfRuleType.Formula => "Formula",
        CfRuleType.DataBar => "Data Bar",
        CfRuleType.ColorScale => "Color Scale",
        CfRuleType.IconSet => "Icon Set",
        CfRuleType.ContainsText => "Text Contains",
        CfRuleType.DateOccurring => "Date Occurring",
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
            _ => "Greater Than"
        },
        _ => "Greater Than"
    };
}
