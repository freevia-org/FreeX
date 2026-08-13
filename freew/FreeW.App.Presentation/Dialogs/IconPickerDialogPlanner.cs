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

public sealed record IconPickerProjection(
    IReadOnlyList<IconPickerEntry> Entries,
    string StatusText);

public sealed record IconPickerAcceptPlan(
    IconPickerSelection? Selection,
    string? WarningMessage)
{
    public bool ShouldAccept => Selection is not null;
}

public enum IconPickerFieldKind
{
    Category,
    Search,
}

public sealed record IconPickerFieldSpec(
    IconPickerFieldKind Kind,
    string Label,
    double Width,
    string AutomationId);

public sealed record IconPickerSurfaceSpec(
    string Title,
    double DialogWidth,
    double DialogHeight,
    double MinDialogHeight,
    double RootMargin,
    double FilterBottomMargin,
    double CategoryTrailingMargin,
    double TileSize,
    double IconSize,
    int TilesPerRow,
    double ActionButtonWidth,
    IReadOnlyList<IconPickerFieldSpec> Fields,
    string TilesAutomationId,
    string StatusAutomationId)
{
    public IconPickerFieldSpec Field(IconPickerFieldKind kind) =>
        Fields.First(field => field.Kind == kind);
}

public static class IconPickerDialogPlanner
{
    public const string AllCategoriesLabel = "(All)";
    public const string NoMatchesStatusText = "No icons match.";
    public const string SelectionRequiredMessage = "Select an icon first.";

    public static IconPickerSurfaceSpec Surface { get; } = new(
        Title: "Insert Icon",
        DialogWidth: 496,
        DialogHeight: 480,
        MinDialogHeight: 320,
        RootMargin: 10,
        FilterBottomMargin: 8,
        CategoryTrailingMargin: 14,
        TileSize: 54,
        IconSize: 38,
        TilesPerRow: 8,
        ActionButtonWidth: 72,
        Fields:
        [
            new(IconPickerFieldKind.Category, "Category:", 120, "IconPickerCategoryComboBox"),
            new(IconPickerFieldKind.Search, "Search:", 160, "IconPickerSearchTextBox"),
        ],
        TilesAutomationId: "IconPickerTiles",
        StatusAutomationId: "IconPickerStatus");

    public static string ToolTipFor(IconPickerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return $"{entry.Name}\n({entry.Category})";
    }

    public static string TileAutomationId(IconPickerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return $"IconPickerTile-{entry.Category}-{entry.Name}";
    }

    public static string RasterizationErrorMessage(string message) =>
        $"Could not rasterize the icon:\n{message}";

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

    public static IconPickerProjection Project(
        IEnumerable<IconPickerEntry> entries,
        string? category,
        string? search)
    {
        var filtered = Filter(entries, category, search);
        return new IconPickerProjection(filtered, StatusText(filtered.Count));
    }

    public static string StatusText(int visibleEntryCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(visibleEntryCount);
        return visibleEntryCount == 0 ? NoMatchesStatusText : $"{visibleEntryCount} icons";
    }

    public static IconPickerSelection Select(IconPickerEntry entry) =>
        new(entry.Name, entry.Category, entry.Path);

    public static IconPickerAcceptPlan PlanAccept(IconPickerEntry? entry) =>
        entry is null
            ? new IconPickerAcceptPlan(null, SelectionRequiredMessage)
            : new IconPickerAcceptPlan(Select(entry), null);
}
