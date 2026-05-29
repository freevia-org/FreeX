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
            AutoFilterMenuFilterKind.Number => new AutoFilterMenuEntry("Number Filters", AutoFilterMenuEntryKind.FilterFamily, NumberFilterCriteria, "Number Filters", CreateFilterFamilyChildren(AutoFilterMenuFilterKind.Number)),
            AutoFilterMenuFilterKind.Date => new AutoFilterMenuEntry("Date Filters", AutoFilterMenuEntryKind.FilterFamily, DateFilterCriteria, "Date Filters", CreateFilterFamilyChildren(AutoFilterMenuFilterKind.Date)),
            _ => new AutoFilterMenuEntry("Text Filters", AutoFilterMenuEntryKind.FilterFamily, TextFilterCriteria, "Text Filters", CreateFilterFamilyChildren(AutoFilterMenuFilterKind.Text))
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
            new AutoFilterMenuSection(AutoFilterMenuSectionKind.Sort, "Sort", sortEntries),
            new AutoFilterMenuSection(AutoFilterMenuSectionKind.FilterCommands, "Filter", filterEntries),
            new AutoFilterMenuSection(AutoFilterMenuSectionKind.Search, "Search", searchEntries),
            new AutoFilterMenuSection(AutoFilterMenuSectionKind.Checklist, "Values", checklistEntries)
        ];
    }

    private static IReadOnlyList<AutoFilterMenuEntry> CreateFilterFamilyChildren(AutoFilterMenuFilterKind filterKind)
    {
        IReadOnlyList<(string Label, string Prefix)> options = filterKind switch
        {
            AutoFilterMenuFilterKind.Number =>
            [
                ("Equals", "="),
                ("Does Not Equal", "<>"),
                ("Greater Than", ">"),
                ("Greater Than Or Equal To", ">="),
                ("Less Than", "<"),
                ("Less Than Or Equal To", "<="),
                ("Between", "between:"),
                ("Top 10", "top:"),
                ("Bottom 10", "bottom:"),
                ("Top 10 Percent", "toppercent:"),
                ("Bottom 10 Percent", "bottompercent:"),
                ("Above Average", "above average"),
                ("Below Average", "below average"),
                ("Blanks", "blank"),
                ("Non-Blanks", "nonblank")
            ],
            AutoFilterMenuFilterKind.Date =>
            [
                ("Equals", "date="),
                ("Does Not Equal", "date<>"),
                ("After", "date>"),
                ("On Or After", "date>="),
                ("Before", "date<"),
                ("On Or Before", "date<="),
                ("Between", "datebetween:"),
                ("Blanks", "blank"),
                ("Non-Blanks", "nonblank")
            ],
            _ =>
            [
                ("Equals", "text="),
                ("Does Not Equal", "text<>"),
                ("Contains", "contains:"),
                ("Does Not Contain", "notcontains:"),
                ("Begins With", "begins:"),
                ("Ends With", "ends:"),
                ("Blanks", "blank"),
                ("Non-Blanks", "nonblank")
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
