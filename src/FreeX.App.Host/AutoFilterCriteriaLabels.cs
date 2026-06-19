using FreeX.App.Presentation.Filtering;

namespace FreeX.App.Host;

/// <summary>
/// Host-side, localized companion to the portable <see cref="AutoFilterDialogCriteriaPlanner"/>: produces the
/// typed-filter family header and the per-kind custom-criteria operator catalog with their resource-localized
/// labels. The criteria <em>prefixes</em> here are the same opaque tokens the portable planner composes into
/// criteria text and the filter command parses, so only the user-facing labels are localized.
/// </summary>
internal static class AutoFilterCriteriaLabels
{
    public static string GetFilterFamilyHeader(AutoFilterMenuFilterKind filterKind) =>
        filterKind switch
        {
            AutoFilterMenuFilterKind.Number => UiText.Get("AutoFilter_FilterFamily_Number"),
            AutoFilterMenuFilterKind.Date => UiText.Get("AutoFilter_FilterFamily_Date"),
            _ => UiText.Get("AutoFilter_FilterFamily_Text")
        };

    public static IReadOnlyList<AutoFilterCriteriaOption> GetCriteriaOptions(AutoFilterMenuFilterKind filterKind) =>
        filterKind switch
        {
            AutoFilterMenuFilterKind.Number =>
            [
                new(UiText.Get("AutoFilter_Criteria_Equals"), "="),
                new(UiText.Get("AutoFilter_Criteria_DoesNotEqual"), "<>"),
                new(UiText.Get("AutoFilter_Criteria_GreaterThan"), ">"),
                new(UiText.Get("AutoFilter_Criteria_GreaterThanOrEqualTo"), ">="),
                new(UiText.Get("AutoFilter_Criteria_LessThan"), "<"),
                new(UiText.Get("AutoFilter_Criteria_LessThanOrEqualTo"), "<="),
                new(UiText.Get("AutoFilter_Criteria_Between"), "between:"),
                new(UiText.Get("AutoFilter_Criteria_Top10"), "top:"),
                new(UiText.Get("AutoFilter_Criteria_Bottom10"), "bottom:"),
                new(UiText.Get("AutoFilter_Criteria_Top10Percent"), "toppercent:"),
                new(UiText.Get("AutoFilter_Criteria_Bottom10Percent"), "bottompercent:"),
                new(UiText.Get("AutoFilter_Criteria_AboveAverage"), "above average", RequiresValue: false),
                new(UiText.Get("AutoFilter_Criteria_BelowAverage"), "below average", RequiresValue: false),
                new(UiText.Get("AutoFilter_Criteria_Blanks"), "blank", RequiresValue: false),
                new(UiText.Get("AutoFilter_Criteria_NonBlanks"), "nonblank", RequiresValue: false)
            ],
            AutoFilterMenuFilterKind.Date =>
            [
                new(UiText.Get("AutoFilter_Criteria_Equals"), "date="),
                new(UiText.Get("AutoFilter_Criteria_DoesNotEqual"), "date<>"),
                new(UiText.Get("AutoFilter_Criteria_After"), "date>"),
                new(UiText.Get("AutoFilter_Criteria_OnOrAfter"), "date>="),
                new(UiText.Get("AutoFilter_Criteria_Before"), "date<"),
                new(UiText.Get("AutoFilter_Criteria_OnOrBefore"), "date<="),
                new(UiText.Get("AutoFilter_Criteria_Between"), "datebetween:"),
                new(UiText.Get("AutoFilter_Criteria_Blanks"), "blank", RequiresValue: false),
                new(UiText.Get("AutoFilter_Criteria_NonBlanks"), "nonblank", RequiresValue: false)
            ],
            _ =>
            [
                new(UiText.Get("AutoFilter_Criteria_Equals"), "text="),
                new(UiText.Get("AutoFilter_Criteria_DoesNotEqual"), "text<>"),
                new(UiText.Get("AutoFilter_Criteria_Contains"), "contains:"),
                new(UiText.Get("AutoFilter_Criteria_DoesNotContain"), "notcontains:"),
                new(UiText.Get("AutoFilter_Criteria_BeginsWith"), "begins:"),
                new(UiText.Get("AutoFilter_Criteria_EndsWith"), "ends:"),
                new(UiText.Get("AutoFilter_Criteria_Blanks"), "blank", RequiresValue: false),
                new(UiText.Get("AutoFilter_Criteria_NonBlanks"), "nonblank", RequiresValue: false)
            ]
        };
}
