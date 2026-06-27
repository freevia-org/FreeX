namespace FreeX.App.Presentation.Filtering;

public static class AutoFilterMenuCatalog
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

    public static AutoFilterMenuEntry CreateFilterFamilyEntry(
        AutoFilterMenuFilterKind filterKind,
        IAutoFilterMenuTextProvider textProvider)
    {
        ArgumentNullException.ThrowIfNull(textProvider);

        return filterKind switch
        {
            AutoFilterMenuFilterKind.Number => new AutoFilterMenuEntry(
                textProvider.Get("AutoFilter_FilterFamily_Number"),
                AutoFilterMenuEntryKind.FilterFamily,
                NumberFilterCriteria,
                textProvider.Get("AutoFilter_FilterFamily_Number"),
                CreateFilterFamilyChildren(AutoFilterMenuFilterKind.Number, textProvider)),
            AutoFilterMenuFilterKind.Date => new AutoFilterMenuEntry(
                textProvider.Get("AutoFilter_FilterFamily_Date"),
                AutoFilterMenuEntryKind.FilterFamily,
                DateFilterCriteria,
                textProvider.Get("AutoFilter_FilterFamily_Date"),
                CreateFilterFamilyChildren(AutoFilterMenuFilterKind.Date, textProvider)),
            _ => new AutoFilterMenuEntry(
                textProvider.Get("AutoFilter_FilterFamily_Text"),
                AutoFilterMenuEntryKind.FilterFamily,
                TextFilterCriteria,
                textProvider.Get("AutoFilter_FilterFamily_Text"),
                CreateFilterFamilyChildren(AutoFilterMenuFilterKind.Text, textProvider))
        };
    }

    public static IReadOnlyList<AutoFilterMenuSection> CreateSections(
        IReadOnlyList<AutoFilterMenuEntry> entries,
        IAutoFilterMenuTextProvider textProvider)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(textProvider);

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
            new AutoFilterMenuSection(AutoFilterMenuSectionKind.Sort, textProvider.Get("AutoFilter_SectionSort"), sortEntries),
            new AutoFilterMenuSection(AutoFilterMenuSectionKind.FilterCommands, textProvider.Get("AutoFilter_SectionFilter"), filterEntries),
            new AutoFilterMenuSection(AutoFilterMenuSectionKind.Search, textProvider.Get("AutoFilter_SectionSearch"), searchEntries),
            new AutoFilterMenuSection(AutoFilterMenuSectionKind.Checklist, textProvider.Get("AutoFilter_SectionValues"), checklistEntries)
        ];
    }

    private static IReadOnlyList<AutoFilterMenuEntry> CreateFilterFamilyChildren(
        AutoFilterMenuFilterKind filterKind,
        IAutoFilterMenuTextProvider textProvider)
    {
        IReadOnlyList<(string Label, string Prefix)> options = filterKind switch
        {
            AutoFilterMenuFilterKind.Number =>
            [
                (textProvider.Get("AutoFilter_Criteria_Equals"), "="),
                (textProvider.Get("AutoFilter_Criteria_DoesNotEqual"), "<>"),
                (textProvider.Get("AutoFilter_Criteria_GreaterThan"), ">"),
                (textProvider.Get("AutoFilter_Criteria_GreaterThanOrEqualTo"), ">="),
                (textProvider.Get("AutoFilter_Criteria_LessThan"), "<"),
                (textProvider.Get("AutoFilter_Criteria_LessThanOrEqualTo"), "<="),
                (textProvider.Get("AutoFilter_Criteria_Between"), "between:"),
                (textProvider.Get("AutoFilter_Criteria_Top10"), "top:"),
                (textProvider.Get("AutoFilter_Criteria_Bottom10"), "bottom:"),
                (textProvider.Get("AutoFilter_Criteria_Top10Percent"), "toppercent:"),
                (textProvider.Get("AutoFilter_Criteria_Bottom10Percent"), "bottompercent:"),
                (textProvider.Get("AutoFilter_Criteria_AboveAverage"), "above average"),
                (textProvider.Get("AutoFilter_Criteria_BelowAverage"), "below average"),
                (textProvider.Get("AutoFilter_Criteria_Blanks"), "blank"),
                (textProvider.Get("AutoFilter_Criteria_NonBlanks"), "nonblank")
            ],
            AutoFilterMenuFilterKind.Date =>
            [
                (textProvider.Get("AutoFilter_Criteria_Equals"), "date="),
                (textProvider.Get("AutoFilter_Criteria_DoesNotEqual"), "date<>"),
                (textProvider.Get("AutoFilter_Criteria_After"), "date>"),
                (textProvider.Get("AutoFilter_Criteria_OnOrAfter"), "date>="),
                (textProvider.Get("AutoFilter_Criteria_Before"), "date<"),
                (textProvider.Get("AutoFilter_Criteria_OnOrBefore"), "date<="),
                (textProvider.Get("AutoFilter_Criteria_Between"), "datebetween:"),
                (textProvider.Get("AutoFilter_Criteria_Blanks"), "blank"),
                (textProvider.Get("AutoFilter_Criteria_NonBlanks"), "nonblank")
            ],
            _ =>
            [
                (textProvider.Get("AutoFilter_Criteria_Equals"), "text="),
                (textProvider.Get("AutoFilter_Criteria_DoesNotEqual"), "text<>"),
                (textProvider.Get("AutoFilter_Criteria_Contains"), "contains:"),
                (textProvider.Get("AutoFilter_Criteria_DoesNotContain"), "notcontains:"),
                (textProvider.Get("AutoFilter_Criteria_BeginsWith"), "begins:"),
                (textProvider.Get("AutoFilter_Criteria_EndsWith"), "ends:"),
                (textProvider.Get("AutoFilter_Criteria_Blanks"), "blank"),
                (textProvider.Get("AutoFilter_Criteria_NonBlanks"), "nonblank")
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
