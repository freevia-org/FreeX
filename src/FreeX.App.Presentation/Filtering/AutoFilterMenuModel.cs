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
    bool IsEnabled = true,
    bool? IsChecked = null)
{
    public AutoFilterMenuEntry(
        string header,
        AutoFilterMenuEntryKind kind,
        bool isEnabled = true,
        bool? isChecked = null)
        : this(header, kind, [], header, [], isEnabled, isChecked)
    {
    }

    public AutoFilterMenuEntry(
        string header,
        AutoFilterMenuEntryKind kind,
        IReadOnlyList<string> criteriaSuggestions,
        bool isEnabled = true,
        bool? isChecked = null)
        : this(header, kind, criteriaSuggestions, header, [], isEnabled, isChecked)
    {
    }

    public AutoFilterMenuEntry(
        string header,
        AutoFilterMenuEntryKind kind,
        IReadOnlyList<string> criteriaSuggestions,
        string value,
        bool isEnabled = true,
        bool? isChecked = null)
        : this(header, kind, criteriaSuggestions, value, [], isEnabled, isChecked)
    {
    }

    public AutoFilterMenuEntry(AutoFilterChecklistItem item)
        : this(item.DisplayText, AutoFilterMenuEntryKind.ChecklistItem, [], item.Value, [], true, item.IsChecked)
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
    FilterFamily,
    FilterFamilyCommand,
    Search,
    SelectAll,
    ChecklistItem
}
