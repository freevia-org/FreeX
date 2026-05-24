using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static partial class XlsxPivotTableWriter
{
    private static string ToPivotFieldGroupingText(PivotFieldGrouping grouping) =>
        grouping switch
        {
            PivotFieldGrouping.Year => "years",
            PivotFieldGrouping.Quarter => "quarters",
            PivotFieldGrouping.Month => "months",
            PivotFieldGrouping.Day => "days",
            PivotFieldGrouping.NumberRange => "numberRange",
            _ => "none"
        };

    private static string ToPivotReportLayoutText(PivotReportLayout layout) =>
        layout switch
        {
            PivotReportLayout.Compact => "compact",
            PivotReportLayout.Outline => "outline",
            _ => "tabular"
        };

    private static string ToPivotShowValuesAsText(PivotShowValuesAs showValuesAs) =>
        showValuesAs switch
        {
            PivotShowValuesAs.PercentOfGrandTotal => "percentOfGrandTotal",
            PivotShowValuesAs.PercentOfRowTotal => "percentOfRowTotal",
            PivotShowValuesAs.PercentOfColumnTotal => "percentOfColumnTotal",
            PivotShowValuesAs.RunningTotalIn => "runningTotalIn",
            PivotShowValuesAs.DifferenceFrom => "differenceFrom",
            PivotShowValuesAs.PercentDifferenceFrom => "percentDifferenceFrom",
            PivotShowValuesAs.RankSmallest => "rankSmallest",
            PivotShowValuesAs.RankLargest => "rankLargest",
            PivotShowValuesAs.Index => "index",
            PivotShowValuesAs.PercentOfParentRowTotal => "percentOfParentRowTotal",
            PivotShowValuesAs.PercentOfParentColumnTotal => "percentOfParentColumnTotal",
            PivotShowValuesAs.PercentOfParentTotal => "percentOfParentTotal",
            _ => "none"
        };

    private static string ToPivotValueFilterKindText(PivotValueFilterKind kind) =>
        kind switch
        {
            PivotValueFilterKind.Bottom => "bottom",
            PivotValueFilterKind.GreaterThan => "greaterThan",
            PivotValueFilterKind.GreaterThanOrEqual => "greaterThanOrEqual",
            PivotValueFilterKind.LessThan => "lessThan",
            PivotValueFilterKind.LessThanOrEqual => "lessThanOrEqual",
            PivotValueFilterKind.Equals => "equals",
            PivotValueFilterKind.DoesNotEqual => "doesNotEqual",
            PivotValueFilterKind.Between => "between",
            PivotValueFilterKind.NotBetween => "notBetween",
            PivotValueFilterKind.AboveAverage => "aboveAverage",
            PivotValueFilterKind.BelowAverage => "belowAverage",
            _ => "top"
        };

    private static string ToPivotLabelFilterKindText(PivotLabelFilterKind kind) =>
        kind switch
        {
            PivotLabelFilterKind.DoesNotEqual => "doesNotEqual",
            PivotLabelFilterKind.BeginsWith => "beginsWith",
            PivotLabelFilterKind.EndsWith => "endsWith",
            PivotLabelFilterKind.Contains => "contains",
            PivotLabelFilterKind.DoesNotContain => "doesNotContain",
            PivotLabelFilterKind.GreaterThan => "greaterThan",
            PivotLabelFilterKind.GreaterThanOrEqual => "greaterThanOrEqual",
            PivotLabelFilterKind.LessThan => "lessThan",
            PivotLabelFilterKind.LessThanOrEqual => "lessThanOrEqual",
            PivotLabelFilterKind.Between => "between",
            _ => "equals"
        };
}
