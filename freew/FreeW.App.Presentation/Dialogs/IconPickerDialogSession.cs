namespace FreeW.App.Presentation.Dialogs;

public sealed record IconPickerDialogState(
    IReadOnlyList<IconPickerEntry> VisibleEntries,
    string StatusText,
    IconPickerEntry? SelectedEntry);

/// <summary>
/// Owns renderer-neutral filtering, selection, and acceptance state for the Insert Icon dialog.
/// </summary>
public sealed class IconPickerDialogSession
{
    private readonly IReadOnlyList<IconPickerEntry> _entries;
    private IReadOnlyList<IconPickerEntry> _visibleEntries;
    private string _statusText;
    private IconPickerEntry? _selectedEntry;

    public IconPickerDialogSession(IEnumerable<IconPickerEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        _entries = entries.ToArray();
        Categories = IconPickerDialogPlanner.Categories(_entries);
        var projection = IconPickerDialogPlanner.Project(_entries, category: null, search: null);
        _visibleEntries = projection.Entries;
        _statusText = projection.StatusText;
    }

    public IReadOnlyList<string> Categories { get; }

    public IconPickerDialogState State => BuildState();

    public IconPickerDialogState ApplyFilter(string? category, string? search)
    {
        var projection = IconPickerDialogPlanner.Project(_entries, category, search);
        _visibleEntries = projection.Entries;
        _statusText = projection.StatusText;
        _selectedEntry = null;
        return BuildState();
    }

    public IconPickerDialogState Select(IconPickerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _selectedEntry = _visibleEntries.FirstOrDefault(candidate => candidate == entry);
        return BuildState();
    }

    public IconPickerAcceptPlan PlanAccept() =>
        IconPickerDialogPlanner.PlanAccept(_selectedEntry);

    private IconPickerDialogState BuildState() =>
        new(_visibleEntries, _statusText, _selectedEntry);
}
