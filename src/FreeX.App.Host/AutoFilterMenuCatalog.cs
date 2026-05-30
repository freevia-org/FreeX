namespace FreeX.App.Host;

internal static class AutoFilterMenuCatalog
{
    private static readonly string[] TextFilterCriteria =
    [
        "equals:",
        "text<>",
        "contains:",
        "notcontains:",
        "begins:",
        "ends:",
        "blank",
        "nonblank"
    ];

    private static readonly string[] NumberFilterCriteria =
    [
        "=",
        "<>",
        ">",
        ">=",
        "<",
        "<=",
        "between:",
        "top:",
        "bottom:",
        "toppercent:",
        "bottompercent:",
        "above average",
        "below average",
        "blank",
        "nonblank"
    ];

    private static readonly string[] DateFilterCriteria =
    [
        "date=",
        "date<>",
        "date>",
        "date>=",
        "date<",
        "date<=",
        "datebetween:",
        "blank",
        "nonblank"
    ];

    public static AutoFilterMenuEntry CreateFilterFamilyEntry(AutoFilterMenuFilterKind filterKind) =>
        filterKind switch
        {
            AutoFilterMenuFilterKind.Number => new AutoFilterMenuEntry(UiText.Get("AutoFilter_FilterFamily_Number"), AutoFilterMenuEntryKind.FilterFamily, NumberFilterCriteria, UiText.Get("AutoFilter_FilterFamily_Number"), CreateFilterFamilyChildren(AutoFilterMenuFilterKind.Number)),
            AutoFilterMenuFilterKind.Date => new AutoFilterMenuEntry(UiText.Get("AutoFilter_FilterFamily_Date"), AutoFilterMenuEntryKind.FilterFamily, DateFilterCriteria, UiText.Get("AutoFilter_FilterFamily_Date"), CreateFilterFamilyChildren(AutoFilterMenuFilterKind.Date)),
            _ => new AutoFilterMenuEntry(UiText.Get("AutoFilter_FilterFamily_Text"), AutoFilterMenuEntryKind.FilterFamily, TextFilterCriteria, UiText.Get("AutoFilter_FilterFamily_Text"), CreateFilterFamilyChildren(AutoFilterMenuFilterKind.Text))
        };

    public static IReadOnlyList<AutoFilterMenuSection> CreateSections(IReadOnlyList<AutoFilterMenuEntry> entries)
    {
        var sortEntries = new List<AutoFilterMenuEntry>(2);
        var filterEntries = new List<AutoFilterMenuEntry>(3);
        var searchEntries = new List<AutoFilterMenuEntry>(2);
        var checklistEntries = new List<AutoFilterMenuEntry>(Math.Max(0, entries.Count - 7));

        foreach (var entry in entries)
        {
            switch (entry.Kind)
            {
                case AutoFilterMenuEntryKind.SortAscending:
                case AutoFilterMenuEntryKind.SortDescending:
                    sortEntries.Add(entry);
                    break;
                case AutoFilterMenuEntryKind.ClearFilter:
                case AutoFilterMenuEntryKind.FilterByColor:
                case AutoFilterMenuEntryKind.FilterFamily:
                    filterEntries.Add(entry);
                    break;
                case AutoFilterMenuEntryKind.Search:
                case AutoFilterMenuEntryKind.SelectAll:
                    searchEntries.Add(entry);
                    break;
                case AutoFilterMenuEntryKind.ChecklistItem:
                    checklistEntries.Add(entry);
                    break;
            }
        }

        return
        [
            new AutoFilterMenuSection(AutoFilterMenuSectionKind.Sort, UiText.Get("AutoFilter_SectionSort"), sortEntries),
            new AutoFilterMenuSection(AutoFilterMenuSectionKind.FilterCommands, UiText.Get("AutoFilter_SectionFilter"), filterEntries),
            new AutoFilterMenuSection(AutoFilterMenuSectionKind.Search, UiText.Get("AutoFilter_SectionSearch"), searchEntries),
            new AutoFilterMenuSection(AutoFilterMenuSectionKind.Checklist, UiText.Get("AutoFilter_SectionValues"), checklistEntries)
        ];
    }

    private static IReadOnlyList<AutoFilterMenuEntry> CreateFilterFamilyChildren(AutoFilterMenuFilterKind filterKind)
    {
        IReadOnlyList<(string Label, string Prefix)> options = filterKind switch
        {
            AutoFilterMenuFilterKind.Number =>
            [
                (UiText.Get("AutoFilter_Criteria_Equals"), "="),
                (UiText.Get("AutoFilter_Criteria_DoesNotEqual"), "<>"),
                (UiText.Get("AutoFilter_Criteria_GreaterThan"), ">"),
                (UiText.Get("AutoFilter_Criteria_GreaterThanOrEqualTo"), ">="),
                (UiText.Get("AutoFilter_Criteria_LessThan"), "<"),
                (UiText.Get("AutoFilter_Criteria_LessThanOrEqualTo"), "<="),
                (UiText.Get("AutoFilter_Criteria_Between"), "between:"),
                (UiText.Get("AutoFilter_Criteria_Top10"), "top:"),
                (UiText.Get("AutoFilter_Criteria_Bottom10"), "bottom:"),
                (UiText.Get("AutoFilter_Criteria_Top10Percent"), "toppercent:"),
                (UiText.Get("AutoFilter_Criteria_Bottom10Percent"), "bottompercent:"),
                (UiText.Get("AutoFilter_Criteria_AboveAverage"), "above average"),
                (UiText.Get("AutoFilter_Criteria_BelowAverage"), "below average"),
                (UiText.Get("AutoFilter_Criteria_Blanks"), "blank"),
                (UiText.Get("AutoFilter_Criteria_NonBlanks"), "nonblank")
            ],
            AutoFilterMenuFilterKind.Date =>
            [
                (UiText.Get("AutoFilter_Criteria_Equals"), "date="),
                (UiText.Get("AutoFilter_Criteria_DoesNotEqual"), "date<>"),
                (UiText.Get("AutoFilter_Criteria_After"), "date>"),
                (UiText.Get("AutoFilter_Criteria_OnOrAfter"), "date>="),
                (UiText.Get("AutoFilter_Criteria_Before"), "date<"),
                (UiText.Get("AutoFilter_Criteria_OnOrBefore"), "date<="),
                (UiText.Get("AutoFilter_Criteria_Between"), "datebetween:"),
                (UiText.Get("AutoFilter_Criteria_Blanks"), "blank"),
                (UiText.Get("AutoFilter_Criteria_NonBlanks"), "nonblank")
            ],
            _ =>
            [
                (UiText.Get("AutoFilter_Criteria_Equals"), "text="),
                (UiText.Get("AutoFilter_Criteria_DoesNotEqual"), "text<>"),
                (UiText.Get("AutoFilter_Criteria_Contains"), "contains:"),
                (UiText.Get("AutoFilter_Criteria_DoesNotContain"), "notcontains:"),
                (UiText.Get("AutoFilter_Criteria_BeginsWith"), "begins:"),
                (UiText.Get("AutoFilter_Criteria_EndsWith"), "ends:"),
                (UiText.Get("AutoFilter_Criteria_Blanks"), "blank"),
                (UiText.Get("AutoFilter_Criteria_NonBlanks"), "nonblank")
            ]
        };

        return options
            .Select(option => new AutoFilterMenuEntry(
                option.Label,
                AutoFilterMenuEntryKind.FilterFamilyCommand,
                [option.Prefix],
                option.Prefix))
            .ToList();
    }
}
