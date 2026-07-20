namespace FreeW.App.Presentation.Dialogs;

public sealed record IconPickerEntry(
    string Name,
    string Category,
    string Keywords,
    string Path);

public sealed record IconPickerSelection(
    string Name,
    string Category,
    string Path);

public static class IconPickerDialogPlanner
{
    public const string AllCategoriesLabel = "(All)";

    public static IReadOnlyList<string> Categories(IEnumerable<IconPickerEntry> entries) =>
        entries.Select(entry => entry.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(category => category, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static IReadOnlyList<IconPickerEntry> Filter(
        IEnumerable<IconPickerEntry> entries,
        string? category,
        string? search)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var result = entries;
        if (!string.IsNullOrWhiteSpace(category)
            && !string.Equals(category, AllCategoriesLabel, StringComparison.OrdinalIgnoreCase))
        {
            result = result.Where(entry => string.Equals(entry.Category, category, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var query = search.Trim();
            result = result.Where(entry => entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || entry.Keywords.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        return result.ToArray();
    }

    public static IconPickerSelection Select(IconPickerEntry entry) =>
        new(entry.Name, entry.Category, entry.Path);
}
