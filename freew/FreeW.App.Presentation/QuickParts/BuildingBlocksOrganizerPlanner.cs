using FreeW.Core.Model;

namespace FreeW.App.Presentation.QuickParts;

/// <summary>Shared Building Blocks Organizer display, sizing, and selection-state contract.</summary>
public static class BuildingBlocksOrganizerPlanner
{
    public const string Title = "Building Blocks Organizer";
    public const string InsertText = "Insert";
    public const string DeleteText = "Delete";
    public const string CloseText = "Close";
    public const double Width = 660;
    public const double ListMinWidth = 300;
    public const double ListMinHeight = 240;
    public const double PreviewMinWidth = 300;
    public const double PreviewMinHeight = 240;
    public const double ColumnGap = 12;
    public const string ListLabel = "Building blocks:";
    public const string PreviewLabel = "Preview:";
    public const string EmptyStatus = "No building blocks saved yet. Select some text and choose Save Selection to Quick Parts first.";
    public const string NoFilterMatchesStatus = "No building blocks match the filter.";

    public static string FormatListItem(QuickPart part)
    {
        ArgumentNullException.ThrowIfNull(part);
        return $"{part.Name}  ({part.Gallery} / {part.Category})";
    }

    public static string FormatPreview(QuickPart? part) =>
        part is null
            ? string.Empty
            : string.IsNullOrEmpty(part.Description)
                ? part.Text
                : $"{part.Description}\n\n{part.Text}";

    public static string FormatRemovedStatus(string name) =>
        $"Removed \"{name}\".";

    public static BuildingBlocksOrganizerSession CreateSession(QuickPartLibrary library) =>
        new(library);
}

/// <summary>A host-neutral list item retaining the full Quick Part metadata for selection.</summary>
public sealed record BuildingBlockListItem(QuickPart Part)
{
    public override string ToString() => BuildingBlocksOrganizerPlanner.FormatListItem(Part);
}

public enum BuildingBlocksOrganizerActionKind
{
    Insert,
}

public sealed record BuildingBlocksOrganizerAction(
    BuildingBlocksOrganizerActionKind Kind,
    string Name,
    string Text);

public sealed record BuildingBlocksOrganizerState(
    IReadOnlyList<BuildingBlockListItem> Items,
    int SelectedIndex,
    string PreviewText,
    string StatusText,
    bool CanInsert,
    bool CanDelete)
{
    public BuildingBlockListItem? SelectedItem =>
        SelectedIndex >= 0 && SelectedIndex < Items.Count
            ? Items[SelectedIndex]
            : null;
}

/// <summary>
/// Owns organizer filtering, selection, preview, command enablement, deletion, and acceptance.
/// Renderers only project <see cref="Current"/> into their native controls.
/// </summary>
public sealed class BuildingBlocksOrganizerSession
{
    private readonly QuickPartLibrary _library;
    private string _filter = string.Empty;

    public BuildingBlocksOrganizerSession(QuickPartLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        _library = library;
        Current = BuildState(selectedName: null, statusText: null);
    }

    public BuildingBlocksOrganizerState Current { get; private set; }

    public BuildingBlocksOrganizerState SetFilter(string? filter)
    {
        var selectedName = Current.SelectedItem?.Part.Name;
        _filter = filter?.Trim() ?? string.Empty;
        Current = BuildState(selectedName, statusText: null);
        return Current;
    }

    public BuildingBlocksOrganizerState SelectIndex(int selectedIndex)
    {
        var normalizedIndex = selectedIndex >= 0 && selectedIndex < Current.Items.Count
            ? selectedIndex
            : -1;
        var selected = normalizedIndex >= 0 ? Current.Items[normalizedIndex] : null;
        Current = Current with
        {
            SelectedIndex = normalizedIndex,
            PreviewText = BuildingBlocksOrganizerPlanner.FormatPreview(selected?.Part),
            CanInsert = selected is not null,
            CanDelete = selected is not null,
        };
        return Current;
    }

    public BuildingBlocksOrganizerAction? AcceptSelection()
    {
        var selected = Current.SelectedItem?.Part;
        return selected is null
            ? null
            : new BuildingBlocksOrganizerAction(
                BuildingBlocksOrganizerActionKind.Insert,
                selected.Name,
                selected.Text);
    }

    public BuildingBlocksOrganizerState DeleteSelection()
    {
        var selected = Current.SelectedItem?.Part;
        if (selected is null)
            return Current;

        _library.Remove(selected.Name);
        Current = BuildState(
            selected.Name,
            BuildingBlocksOrganizerPlanner.FormatRemovedStatus(selected.Name));
        return Current;
    }

    private BuildingBlocksOrganizerState BuildState(string? selectedName, string? statusText)
    {
        var items = _library.Snippets
            .Where(MatchesFilter)
            .Select(static part => new BuildingBlockListItem(part))
            .ToArray();
        var selectedIndex = FindSelectedIndex(items, selectedName);
        var selected = selectedIndex >= 0 ? items[selectedIndex] : null;
        var emptyStatus = _library.IsEmpty
            ? BuildingBlocksOrganizerPlanner.EmptyStatus
            : items.Length == 0
                ? BuildingBlocksOrganizerPlanner.NoFilterMatchesStatus
                : string.Empty;

        return new BuildingBlocksOrganizerState(
            items,
            selectedIndex,
            BuildingBlocksOrganizerPlanner.FormatPreview(selected?.Part),
            statusText ?? emptyStatus,
            CanInsert: selected is not null,
            CanDelete: selected is not null);
    }

    private bool MatchesFilter(QuickPart part)
    {
        if (_filter.Length == 0)
            return true;

        return Contains(part.Name) ||
            Contains(part.Gallery) ||
            Contains(part.Category) ||
            Contains(part.Description) ||
            Contains(part.Text);
    }

    private bool Contains(string value) =>
        value.Contains(_filter, StringComparison.OrdinalIgnoreCase);

    private static int FindSelectedIndex(
        IReadOnlyList<BuildingBlockListItem> items,
        string? selectedName)
    {
        if (selectedName is not null)
        {
            for (var index = 0; index < items.Count; index++)
            {
                if (string.Equals(
                        items[index].Part.Name,
                        selectedName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }
        }

        return items.Count == 0 ? -1 : 0;
    }
}
