namespace FreeX.App.Presentation.Filtering;

public sealed class InvariantAutoFilterMenuTextProvider : IAutoFilterMenuTextProvider
{
    public const string BlankDisplayText = "(Blanks)";

    public static InvariantAutoFilterMenuTextProvider Instance { get; } = new();

    private InvariantAutoFilterMenuTextProvider()
    {
    }

    public string Get(string resourceKey) => resourceKey switch
    {
        "AutoFilter_SortAscending" => "Sort A to Z",
        "AutoFilter_SortDescending" => "Sort Z to A",
        "AutoFilter_SortAToZ" => "Sort A to Z",
        "AutoFilter_SortZToA" => "Sort Z to A",
        "AutoFilter_SortSmallestToLargest" => "Sort Smallest to Largest",
        "AutoFilter_SortLargestToSmallest" => "Sort Largest to Smallest",
        "AutoFilter_SortOldestToNewest" => "Sort Oldest to Newest",
        "AutoFilter_SortNewestToOldest" => "Sort Newest to Oldest",
        "AutoFilter_FilterByColor" => "Filter by Color",
        "AutoFilter_SortByColor" => "Sort by Color",
        "AutoFilter_Search" => "Search",
        "AutoFilter_SelectAll" => "(Select All)",
        "AutoFilter_NoFill" => "No Fill",
        "AutoFilter_FilterFamily_Text" => "Text Filters",
        "AutoFilter_FilterFamily_Number" => "Number Filters",
        "AutoFilter_FilterFamily_Date" => "Date Filters",
        "AutoFilter_SectionSort" => "Sort",
        "AutoFilter_SectionFilter" => "Filter",
        "AutoFilter_SectionSearch" => "Search",
        "AutoFilter_SectionValues" => "Values",
        "AutoFilter_Criteria_Equals" => "Equals",
        "AutoFilter_Criteria_DoesNotEqual" => "Does Not Equal",
        "AutoFilter_Criteria_GreaterThan" => "Greater Than",
        "AutoFilter_Criteria_GreaterThanOrEqualTo" => "Greater Than Or Equal To",
        "AutoFilter_Criteria_LessThan" => "Less Than",
        "AutoFilter_Criteria_LessThanOrEqualTo" => "Less Than Or Equal To",
        "AutoFilter_Criteria_Between" => "Between",
        "AutoFilter_Criteria_Top10" => "Top 10",
        "AutoFilter_Criteria_Bottom10" => "Bottom 10",
        "AutoFilter_Criteria_Top10Percent" => "Top 10 Percent",
        "AutoFilter_Criteria_Bottom10Percent" => "Bottom 10 Percent",
        "AutoFilter_Criteria_AboveAverage" => "Above Average",
        "AutoFilter_Criteria_BelowAverage" => "Below Average",
        "AutoFilter_Criteria_Blanks" => "Blanks",
        "AutoFilter_Criteria_NonBlanks" => "Non-Blanks",
        "AutoFilter_Criteria_After" => "After",
        "AutoFilter_Criteria_OnOrAfter" => "On Or After",
        "AutoFilter_Criteria_Before" => "Before",
        "AutoFilter_Criteria_OnOrBefore" => "On Or Before",
        "AutoFilter_Criteria_Contains" => "Contains",
        "AutoFilter_Criteria_DoesNotContain" => "Does Not Contain",
        "AutoFilter_Criteria_BeginsWith" => "Begins With",
        "AutoFilter_Criteria_EndsWith" => "Ends With",
        _ => resourceKey
    };

    public string Format(string resourceKey, string value) => resourceKey switch
    {
        "AutoFilter_ClearFilterFrom" => $"Clear Filter From \"{value}\"",
        "AutoFilter_ColumnHeader" => $"Column {value}",
        _ => $"{resourceKey}: {value}"
    };
}
