using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

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

    // Expresses the FreeX report layout as the OOXML CT_pivotTableDefinition layout attributes. There is
    // no single 'reportLayout' attribute in the schema; Excel derives the layout from these flags.
    // Internal (not private): also called from XlsxFileAdapter.SavePostProcessing.cs's
    // RewritePivotTableLayoutState to regenerate these attributes on the hasSourcePackage (preserved-part)
    // save path, where this class's own Save() never runs (R75-io-pivottable-layout-4-1).
    internal static XAttribute[] PivotReportLayoutAttributes(PivotReportLayout layout) =>
        layout switch
        {
            PivotReportLayout.Compact =>
            [
                new XAttribute("compact", "1"),
                new XAttribute("compactData", "1"),
                new XAttribute("outline", "1"),
                new XAttribute("outlineData", "0"),
                new XAttribute("gridDropZones", "0"),
            ],
            PivotReportLayout.Outline =>
            [
                new XAttribute("compact", "0"),
                new XAttribute("compactData", "0"),
                new XAttribute("outline", "1"),
                new XAttribute("outlineData", "1"),
                new XAttribute("gridDropZones", "0"),
            ],
            // Tabular
            _ =>
            [
                new XAttribute("compact", "0"),
                new XAttribute("compactData", "0"),
                new XAttribute("outline", "0"),
                new XAttribute("outlineData", "0"),
                new XAttribute("gridDropZones", "1"),
            ],
        };

    // CT_DataField's real OOXML attribute is showDataAs, whose ST_ShowDataAs tokens (ECMA-376 18.18.72)
    // are percentOfTotal/percentOfRow/percentOfCol/runTotal/difference/percentDiff/rankAscending/
    // rankDescending/percentOfParent* -- NOT the earlier FreeX-invented percentOf*Total/runningTotalIn/
    // differenceFrom/rankSmallest/rankLargest tokens, which real Excel silently ignores (R36-io-pivot-cache-2-1).
    // Internal (not private): also called from XlsxFileAdapter.SavePostProcessing.cs's
    // RewritePivotTableDataFieldSummaries (R75-io-pivottable-layout-4-1).
    internal static string ToPivotShowValuesAsText(PivotShowValuesAs showValuesAs) =>
        showValuesAs switch
        {
            PivotShowValuesAs.PercentOfGrandTotal => "percentOfTotal",
            PivotShowValuesAs.PercentOfRowTotal => "percentOfRow",
            PivotShowValuesAs.PercentOfColumnTotal => "percentOfCol",
            PivotShowValuesAs.RunningTotalIn => "runTotal",
            PivotShowValuesAs.DifferenceFrom => "difference",
            PivotShowValuesAs.PercentDifferenceFrom => "percentDiff",
            PivotShowValuesAs.RankSmallest => "rankAscending",
            PivotShowValuesAs.RankLargest => "rankDescending",
            PivotShowValuesAs.Index => "index",
            PivotShowValuesAs.PercentOfParentRowTotal => "percentOfParentRow",
            PivotShowValuesAs.PercentOfParentColumnTotal => "percentOfParentCol",
            PivotShowValuesAs.PercentOfParentTotal => "percentOfParent",
            _ => "normal"
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

    // R82-io-pivot-layout-5-2: the REAL ST_PivotFilterType tokens (verified against the OpenXml SDK's
    // PivotFilterValues enumeration via OpenXmlValidator -- there is no "topcount"/"bottomcount"/"top"/
    // "bottom"/"aboveAverage"/"belowAverage" token in the actual schema at all). Real Excel expresses a
    // Top-N filter's direction on the nested <autoFilter><filterColumn><top10 top="0|1"/></filterColumn>,
    // NOT on the <filter>'s own "type" -- see ToPivotFilterAutoFilterFillerXml. AboveAverage/BelowAverage
    // have no real representation in this mechanism at all (Excel apparently only persists their
    // resulting per-item hidden state, not a reapplied "above/below average" criterion), so this returns
    // null for those two kinds; callers fall back to ToPivotValueFiltersXml's FreeX-authored shape purely
    // so FreeX's own round-trip doesn't lose the setting.
    internal static string? ToNativePivotValueFilterKindText(PivotValueFilterKind kind) =>
        kind switch
        {
            PivotValueFilterKind.Top or PivotValueFilterKind.Bottom => "count",
            PivotValueFilterKind.Equals => "valueEqual",
            PivotValueFilterKind.DoesNotEqual => "valueNotEqual",
            PivotValueFilterKind.GreaterThan => "valueGreaterThan",
            PivotValueFilterKind.GreaterThanOrEqual => "valueGreaterThanOrEqual",
            PivotValueFilterKind.LessThan => "valueLessThan",
            PivotValueFilterKind.LessThanOrEqual => "valueLessThanOrEqual",
            PivotValueFilterKind.Between => "valueBetween",
            PivotValueFilterKind.NotBetween => "valueNotBetween",
            _ => null
        };

    // R82-io-pivot-layout-5-2: real ST_PivotFilterType tokens for label (caption) filters -- every
    // PivotLabelFilterKind has a genuine equivalent (unlike the value-filter average kinds above).
    internal static string ToNativePivotLabelFilterKindText(PivotLabelFilterKind kind) =>
        kind switch
        {
            PivotLabelFilterKind.DoesNotEqual => "captionNotEqual",
            PivotLabelFilterKind.BeginsWith => "captionBeginsWith",
            PivotLabelFilterKind.EndsWith => "captionEndsWith",
            PivotLabelFilterKind.Contains => "captionContains",
            PivotLabelFilterKind.DoesNotContain => "captionNotContains",
            PivotLabelFilterKind.GreaterThan => "captionGreaterThan",
            PivotLabelFilterKind.GreaterThanOrEqual => "captionGreaterThanOrEqual",
            PivotLabelFilterKind.LessThan => "captionLessThan",
            PivotLabelFilterKind.LessThanOrEqual => "captionLessThanOrEqual",
            PivotLabelFilterKind.Between => "captionBetween",
            PivotLabelFilterKind.DateEqual => "dateEqual",
            PivotLabelFilterKind.DateNotEqual => "dateNotEqual",
            PivotLabelFilterKind.DateOlderThan => "dateOlderThan",
            PivotLabelFilterKind.DateOlderThanOrEqual => "dateOlderThanOrEqual",
            PivotLabelFilterKind.DateNewerThan => "dateNewerThan",
            PivotLabelFilterKind.DateNewerThanOrEqual => "dateNewerThanOrEqual",
            PivotLabelFilterKind.DateBetween => "dateBetween",
            PivotLabelFilterKind.DateNotBetween => "dateNotBetween",
            PivotLabelFilterKind.Yesterday => "yesterday",
            PivotLabelFilterKind.Today => "today",
            PivotLabelFilterKind.Tomorrow => "tomorrow",
            PivotLabelFilterKind.LastWeek => "lastWeek",
            PivotLabelFilterKind.ThisWeek => "thisWeek",
            PivotLabelFilterKind.NextWeek => "nextWeek",
            PivotLabelFilterKind.LastMonth => "lastMonth",
            PivotLabelFilterKind.ThisMonth => "thisMonth",
            PivotLabelFilterKind.NextMonth => "nextMonth",
            PivotLabelFilterKind.LastQuarter => "lastQuarter",
            PivotLabelFilterKind.ThisQuarter => "thisQuarter",
            PivotLabelFilterKind.NextQuarter => "nextQuarter",
            PivotLabelFilterKind.LastYear => "lastYear",
            PivotLabelFilterKind.ThisYear => "thisYear",
            PivotLabelFilterKind.NextYear => "nextYear",
            PivotLabelFilterKind.YearToDate => "yearToDate",
            _ => "captionEqual"
        };

    // ST_FilterOperator tokens for the filler <customFilter operator="..."/> inside a native pivot
    // filter's required <autoFilter> (see ToPivotFilterAutoFilterFillerXml).
    private static string ToPivotComparisonAutoFilterOperator(PivotValueFilterKind kind) =>
        kind switch
        {
            PivotValueFilterKind.GreaterThan => "greaterThan",
            PivotValueFilterKind.GreaterThanOrEqual => "greaterThanOrEqual",
            PivotValueFilterKind.LessThan => "lessThan",
            PivotValueFilterKind.LessThanOrEqual => "lessThanOrEqual",
            PivotValueFilterKind.DoesNotEqual => "notEqual",
            PivotValueFilterKind.Between or PivotValueFilterKind.NotBetween => "greaterThanOrEqual",
            _ => "equal"
        };
}
