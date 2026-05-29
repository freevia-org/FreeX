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

    public static IReadOnlyList<AutoFilterMenuSection> CreateSections(IReadOnlyList<AutoFilterMenuEntry> entries) =>
    [
        new AutoFilterMenuSection(
            AutoFilterMenuSectionKind.Sort,
            "Sort",
            entries.Where(entry => entry.Kind is AutoFilterMenuEntryKind.SortAscending or AutoFilterMenuEntryKind.SortDescending).ToList()),
        new AutoFilterMenuSection(
            AutoFilterMenuSectionKind.FilterCommands,
            "Filter",
            entries.Where(entry => entry.Kind is AutoFilterMenuEntryKind.ClearFilter or AutoFilterMenuEntryKind.FilterByColor or AutoFilterMenuEntryKind.FilterFamily).ToList()),
        new AutoFilterMenuSection(
            AutoFilterMenuSectionKind.Search,
            "Search",
            entries.Where(entry => entry.Kind is AutoFilterMenuEntryKind.Search or AutoFilterMenuEntryKind.SelectAll).ToList()),
        new AutoFilterMenuSection(
            AutoFilterMenuSectionKind.Checklist,
            "Values",
            entries.Where(entry => entry.Kind is AutoFilterMenuEntryKind.ChecklistItem).ToList())
    ];

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
