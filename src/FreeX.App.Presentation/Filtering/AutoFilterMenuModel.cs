using Free.Shared.Ribbon;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Filtering;

public sealed record AutoFilterDropdownPlan(GridRange Range, uint FilterColumnOffset);

public sealed record AutoFilterChecklistItem(string DisplayText, string Value, bool IsChecked = true);

public sealed record AutoFilterMenuPlan(
    string HeaderText,
    AutoFilterMenuFilterKind FilterKind,
    IReadOnlyList<AutoFilterMenuEntry> Entries,
    IReadOnlyList<AutoFilterColorOption>? ColorOptions = null,
    IReadOnlyList<AutoFilterMenuSection>? Sections = null)
{
    public IReadOnlyList<AutoFilterMenuSection> Sections { get; init; } = Sections ?? [];
}

public sealed record AutoFilterMenuSection(
    AutoFilterMenuSectionKind Kind,
    string Label,
    IReadOnlyList<AutoFilterMenuEntry> Entries);

public enum AutoFilterMenuSectionKind
{
    Sort,
    FilterCommands,
    Search,
    Checklist
}

public enum AutoFilterColorFilterKind
{
    None,
    CellFillColor,
    NoFill,
    FontColor
}

public sealed record AutoFilterColorOption(
    string Label,
    AutoFilterColorFilterKind Kind,
    CellColor? Color);

public sealed record AutoFilterMenuEntry(
    string Header,
    AutoFilterMenuEntryKind Kind,
    IReadOnlyList<string> CriteriaSuggestions,
    string Value,
    IReadOnlyList<AutoFilterMenuEntry> Children,
    AutoFilterMenuEntryPresentation Presentation,
    bool IsEnabled = true,
    bool? IsChecked = null)
{
    public AutoFilterMenuEntry(
        string header,
        AutoFilterMenuEntryKind kind,
        bool isEnabled = true,
        bool? isChecked = null)
        : this(header, kind, [], header, [], AutoFilterMenuEntryPresentation.ForKind(kind), isEnabled, isChecked)
    {
    }

    public AutoFilterMenuEntry(
        string header,
        AutoFilterMenuEntryKind kind,
        IReadOnlyList<string> criteriaSuggestions,
        bool isEnabled = true,
        bool? isChecked = null)
        : this(header, kind, criteriaSuggestions, header, [], AutoFilterMenuEntryPresentation.ForKind(kind), isEnabled, isChecked)
    {
    }

    public AutoFilterMenuEntry(
        string header,
        AutoFilterMenuEntryKind kind,
        IReadOnlyList<string> criteriaSuggestions,
        string value,
        bool isEnabled = true,
        bool? isChecked = null)
        : this(header, kind, criteriaSuggestions, value, [], AutoFilterMenuEntryPresentation.ForKind(kind), isEnabled, isChecked)
    {
    }

    public AutoFilterMenuEntry(
        string header,
        AutoFilterMenuEntryKind kind,
        IReadOnlyList<string> criteriaSuggestions,
        string value,
        IReadOnlyList<AutoFilterMenuEntry> children,
        bool isEnabled = true,
        bool? isChecked = null)
        : this(header, kind, criteriaSuggestions, value, children, AutoFilterMenuEntryPresentation.ForKind(kind), isEnabled, isChecked)
    {
    }

    public AutoFilterMenuEntry(AutoFilterChecklistItem item)
        : this(
            item.DisplayText,
            AutoFilterMenuEntryKind.ChecklistItem,
            [],
            item.Value,
            [],
            AutoFilterMenuEntryPresentation.ForKind(AutoFilterMenuEntryKind.ChecklistItem),
            true,
            item.IsChecked)
    {
    }
}

public enum AutoFilterMenuFilterKind
{
    Text,
    Number,
    Date
}

public enum AutoFilterMenuEntryKind
{
    SortAscending,
    SortDescending,
    Separator,
    ClearFilter,
    FilterByColor,
    SortByColor,
    FilterFamily,
    FilterFamilyCommand,
    Search,
    SelectAll,
    ChecklistItem
}

public sealed record AutoFilterMenuEntryPresentation(
    RibbonCommandIconKind IconKind,
    AutoFilterMenuEntryFocusRole FocusRole,
    bool ShowsContinuation = false,
    bool ParticipatesInSearch = false)
{
    public static AutoFilterMenuEntryPresentation ForKind(AutoFilterMenuEntryKind kind) =>
        kind switch
        {
            AutoFilterMenuEntryKind.SortAscending => new(
                RibbonCommandIconKind.SortAscending,
                AutoFilterMenuEntryFocusRole.Command),
            AutoFilterMenuEntryKind.SortDescending => new(
                RibbonCommandIconKind.SortDescending,
                AutoFilterMenuEntryFocusRole.Command),
            AutoFilterMenuEntryKind.ClearFilter => new(
                RibbonCommandIconKind.Clear,
                AutoFilterMenuEntryFocusRole.Command),
            AutoFilterMenuEntryKind.FilterByColor => new(
                RibbonCommandIconKind.Color,
                AutoFilterMenuEntryFocusRole.Command,
                ShowsContinuation: true),
            AutoFilterMenuEntryKind.SortByColor => new(
                RibbonCommandIconKind.Color,
                AutoFilterMenuEntryFocusRole.Command,
                ShowsContinuation: true),
            AutoFilterMenuEntryKind.FilterFamily => new(
                RibbonCommandIconKind.Filter,
                AutoFilterMenuEntryFocusRole.Submenu,
                ShowsContinuation: true),
            AutoFilterMenuEntryKind.FilterFamilyCommand => new(
                RibbonCommandIconKind.Filter,
                AutoFilterMenuEntryFocusRole.SubmenuCommand),
            AutoFilterMenuEntryKind.Search => new(
                RibbonCommandIconKind.Search,
                AutoFilterMenuEntryFocusRole.SearchBox,
                ParticipatesInSearch: true),
            AutoFilterMenuEntryKind.SelectAll => new(
                RibbonCommandIconKind.CheckBox,
                AutoFilterMenuEntryFocusRole.TriStateSelectAll,
                ParticipatesInSearch: true),
            AutoFilterMenuEntryKind.ChecklistItem => new(
                RibbonCommandIconKind.CheckBox,
                AutoFilterMenuEntryFocusRole.ChecklistItem,
                ParticipatesInSearch: true),
            _ => new(RibbonCommandIconKind.Generic, AutoFilterMenuEntryFocusRole.None)
        };
}

public enum AutoFilterMenuEntryFocusRole
{
    None,
    Command,
    Submenu,
    SubmenuCommand,
    SearchBox,
    TriStateSelectAll,
    ChecklistItem
}
