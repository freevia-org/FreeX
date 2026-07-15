namespace FreeX.App.Host;

public sealed record PivotFieldListItem(string Caption, bool IsChecked);

public sealed record PendingPivotLayoutUpdate(
    bool IsDeferred,
    string? AvailableFieldsSearchText,
    IReadOnlyList<PivotFieldListItem> Fields);

internal static class PivotUiHostHelpers
{
    public static string? GetFieldListCaption(object? item) =>
        item switch
        {
            string value when !string.IsNullOrWhiteSpace(value) => value,
            PivotFieldListItem field when !string.IsNullOrWhiteSpace(field.Caption) => field.Caption,
            _ => null
        };

    public static IReadOnlyList<PivotFieldListItem> FilterPivotFieldListItems(
        IEnumerable<PivotFieldListItem> fields,
        string? searchText) =>
        fields
            .Where(field => FreeX.App.Presentation.PivotUI.PivotUiPlanner.FieldListCaptionMatchesSearch(
                field.Caption, searchText))
            .ToList();

    public static void InsertOrAppend<T>(List<T> items, T item, int index) =>
        FreeX.App.Presentation.PivotUI.PivotUiPlanner.InsertOrAppend(items, item, index);
}
