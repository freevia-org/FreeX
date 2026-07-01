namespace FreeX.App.Presentation.Filtering;

public static class AutoFilterMenuCatalog
{
    private static readonly AutoFilterCriteriaDescriptor[] TextFilterCriteria =
    [
        new("AutoFilter_Criteria_Equals", "text=", SuggestionPrefix: "equals:"),
        new("AutoFilter_Criteria_DoesNotEqual", "text<>"),
        new("AutoFilter_Criteria_Contains", "contains:"),
        new("AutoFilter_Criteria_DoesNotContain", "notcontains:"),
        new("AutoFilter_Criteria_BeginsWith", "begins:"),
        new("AutoFilter_Criteria_EndsWith", "ends:"),
        new("AutoFilter_Criteria_Blanks", "blank", RequiresValue: false),
        new("AutoFilter_Criteria_NonBlanks", "nonblank", RequiresValue: false)
    ];

    private static readonly AutoFilterCriteriaDescriptor[] NumberFilterCriteria =
    [
        new("AutoFilter_Criteria_Equals", "="),
        new("AutoFilter_Criteria_DoesNotEqual", "<>"),
        new("AutoFilter_Criteria_GreaterThan", ">"),
        new("AutoFilter_Criteria_GreaterThanOrEqualTo", ">="),
        new("AutoFilter_Criteria_LessThan", "<"),
        new("AutoFilter_Criteria_LessThanOrEqualTo", "<="),
        new("AutoFilter_Criteria_Between", "between:", SpecialKind: AutoFilterCriteriaSpecialKind.Between),
        new("AutoFilter_Criteria_Top10", "top:", SpecialKind: AutoFilterCriteriaSpecialKind.TopBottom),
        new("AutoFilter_Criteria_Bottom10", "bottom:", SpecialKind: AutoFilterCriteriaSpecialKind.TopBottom),
        new("AutoFilter_Criteria_Top10Percent", "toppercent:", SpecialKind: AutoFilterCriteriaSpecialKind.TopBottom),
        new("AutoFilter_Criteria_Bottom10Percent", "bottompercent:", SpecialKind: AutoFilterCriteriaSpecialKind.TopBottom),
        new("AutoFilter_Criteria_AboveAverage", "above average", RequiresValue: false),
        new("AutoFilter_Criteria_BelowAverage", "below average", RequiresValue: false),
        new("AutoFilter_Criteria_Blanks", "blank", RequiresValue: false),
        new("AutoFilter_Criteria_NonBlanks", "nonblank", RequiresValue: false)
    ];

    private static readonly AutoFilterCriteriaDescriptor[] DateFilterCriteria =
    [
        new("AutoFilter_Criteria_Equals", "date="),
        new("AutoFilter_Criteria_DoesNotEqual", "date<>"),
        new("AutoFilter_Criteria_After", "date>"),
        new("AutoFilter_Criteria_OnOrAfter", "date>="),
        new("AutoFilter_Criteria_Before", "date<"),
        new("AutoFilter_Criteria_OnOrBefore", "date<="),
        new("AutoFilter_Criteria_Between", "datebetween:", SpecialKind: AutoFilterCriteriaSpecialKind.Between),
        new("AutoFilter_Criteria_Blanks", "blank", RequiresValue: false),
        new("AutoFilter_Criteria_NonBlanks", "nonblank", RequiresValue: false)
    ];

    private static readonly AutoFilterFilterFamilyDescriptor TextFilterFamily =
        new(AutoFilterMenuFilterKind.Text, "AutoFilter_FilterFamily_Text", TextFilterCriteria);

    private static readonly AutoFilterFilterFamilyDescriptor NumberFilterFamily =
        new(AutoFilterMenuFilterKind.Number, "AutoFilter_FilterFamily_Number", NumberFilterCriteria);

    private static readonly AutoFilterFilterFamilyDescriptor DateFilterFamily =
        new(AutoFilterMenuFilterKind.Date, "AutoFilter_FilterFamily_Date", DateFilterCriteria);

    public static AutoFilterFilterFamilyDescriptor GetFilterFamilyDescriptor(AutoFilterMenuFilterKind filterKind) =>
        filterKind switch
        {
            AutoFilterMenuFilterKind.Number => NumberFilterFamily,
            AutoFilterMenuFilterKind.Date => DateFilterFamily,
            _ => TextFilterFamily
        };

    public static IReadOnlyList<AutoFilterCriteriaDescriptor> GetCriteriaDescriptors(AutoFilterMenuFilterKind filterKind) =>
        GetFilterFamilyDescriptor(filterKind).Criteria;

    public static bool IsBetweenCriteriaPrefix(string criteriaPrefix) =>
        HasSpecialKind(criteriaPrefix, AutoFilterCriteriaSpecialKind.Between);

    public static bool IsTopBottomCriteriaPrefix(string criteriaPrefix) =>
        HasSpecialKind(criteriaPrefix, AutoFilterCriteriaSpecialKind.TopBottom);

    public static AutoFilterMenuEntry CreateFilterFamilyEntry(
        AutoFilterMenuFilterKind filterKind,
        IAutoFilterMenuTextProvider textProvider)
    {
        ArgumentNullException.ThrowIfNull(textProvider);

        var descriptor = GetFilterFamilyDescriptor(filterKind);
        var label = textProvider.Get(descriptor.ResourceKey);
        return new AutoFilterMenuEntry(
            label,
            AutoFilterMenuEntryKind.FilterFamily,
            CreateCriteriaSuggestions(descriptor.Criteria),
            label,
            CreateFilterFamilyChildren(descriptor.FilterKind, textProvider));
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
        var descriptors = GetCriteriaDescriptors(filterKind);
        var entries = new List<AutoFilterMenuEntry>(descriptors.Count);
        foreach (var descriptor in descriptors)
        {
            entries.Add(new AutoFilterMenuEntry(
                textProvider.Get(descriptor.ResourceKey),
                AutoFilterMenuEntryKind.FilterFamilyCommand,
                [descriptor.CriteriaPrefix],
                descriptor.CriteriaPrefix));
        }

        return entries;
    }

    private static IReadOnlyList<string> CreateCriteriaSuggestions(IReadOnlyList<AutoFilterCriteriaDescriptor> descriptors)
    {
        var suggestions = new List<string>(descriptors.Count);
        foreach (var descriptor in descriptors)
            suggestions.Add(descriptor.SuggestionPrefix ?? descriptor.CriteriaPrefix);

        return suggestions;
    }

    private static bool HasSpecialKind(string criteriaPrefix, AutoFilterCriteriaSpecialKind specialKind) =>
        HasSpecialKind(TextFilterCriteria, criteriaPrefix, specialKind) ||
        HasSpecialKind(NumberFilterCriteria, criteriaPrefix, specialKind) ||
        HasSpecialKind(DateFilterCriteria, criteriaPrefix, specialKind);

    private static bool HasSpecialKind(
        IReadOnlyList<AutoFilterCriteriaDescriptor> descriptors,
        string criteriaPrefix,
        AutoFilterCriteriaSpecialKind specialKind)
    {
        foreach (var descriptor in descriptors)
        {
            if (string.Equals(descriptor.CriteriaPrefix, criteriaPrefix, StringComparison.OrdinalIgnoreCase))
                return descriptor.SpecialKind == specialKind;
        }

        return false;
    }
}

public sealed record AutoFilterFilterFamilyDescriptor(
    AutoFilterMenuFilterKind FilterKind,
    string ResourceKey,
    IReadOnlyList<AutoFilterCriteriaDescriptor> Criteria);

public sealed record AutoFilterCriteriaDescriptor(
    string ResourceKey,
    string CriteriaPrefix,
    bool RequiresValue = true,
    string? SuggestionPrefix = null,
    AutoFilterCriteriaSpecialKind SpecialKind = AutoFilterCriteriaSpecialKind.None);

public enum AutoFilterCriteriaSpecialKind
{
    None,
    Between,
    TopBottom
}
