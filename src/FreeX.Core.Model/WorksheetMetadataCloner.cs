namespace FreeX.Core.Model;

/// <summary>
/// Owns deep copies of worksheet metadata graphs whose nested native-attribute dictionaries must
/// remain isolated across sheet duplication and structural-edit snapshots.
/// </summary>
internal static class WorksheetMetadataCloner
{
    public static WorksheetPageBreaksMetadataModel? ClonePageBreaks(
        WorksheetPageBreaksMetadataModel? metadata) =>
        metadata is null
            ? null
            : new WorksheetPageBreaksMetadataModel
            {
                NativeAttributes = CloneAttributes(metadata.NativeAttributes),
                BreakNativeAttributes = CloneNestedAttributes(metadata.BreakNativeAttributes)
            };

    public static WorksheetCellWatchesMetadataModel? CloneCellWatches(
        WorksheetCellWatchesMetadataModel? metadata) =>
        metadata is null
            ? null
            : new WorksheetCellWatchesMetadataModel
            {
                NativeAttributes = CloneAttributes(metadata.NativeAttributes),
                WatchNativeAttributes = CloneNestedAttributes(
                    metadata.WatchNativeAttributes,
                    StringComparer.OrdinalIgnoreCase)
            };

    public static WorksheetIgnoredErrorsMetadataModel? CloneIgnoredErrors(
        WorksheetIgnoredErrorsMetadataModel? metadata) =>
        metadata is null
            ? null
            : new WorksheetIgnoredErrorsMetadataModel
            {
                NativeAttributes = CloneAttributes(metadata.NativeAttributes),
                ErrorNativeAttributes = CloneNestedAttributes(
                    metadata.ErrorNativeAttributes,
                    StringComparer.OrdinalIgnoreCase)
            };

    private static Dictionary<string, string> CloneAttributes(
        IReadOnlyDictionary<string, string> attributes) =>
        new(attributes, StringComparer.Ordinal);

    private static Dictionary<TKey, Dictionary<string, string>> CloneNestedAttributes<TKey>(
        IReadOnlyDictionary<TKey, Dictionary<string, string>> source,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
    {
        var clone = new Dictionary<TKey, Dictionary<string, string>>(source.Count, comparer);
        foreach (var (key, attributes) in source)
            clone.Add(key, CloneAttributes(attributes));
        return clone;
    }
}
