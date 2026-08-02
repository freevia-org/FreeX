using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Single choke point for building a <see cref="PivotCacheFieldModel"/> from LIVE worksheet source data
/// (as opposed to one read back from a previously-saved file, which already carries its own
/// <see cref="PivotCacheFieldModel.SharedItems"/> verbatim from the OOXML/JSON). Every command that
/// creates or rebuilds a cache field for an in-session pivot -- <c>AddPivotTableCommand</c> (brand-new
/// pivot), <c>PivotTableRefreshService.ReconcileCacheFields</c> (a table-backed source's column set
/// changed on refresh), and <c>ChangePivotTableSourceCommand</c>/<c>BuildRedirectedCache</c> ("Change Data
/// Source") -- must go through this one place, not re-derive the flags/shared-items themselves.
///
/// R114-commands-pivot-sharedItems: before this, every one of those call sites built a field with only the
/// header name (or header + summary flags), leaving <see cref="PivotCacheFieldModel.SharedItems"/> null.
/// Two independent consumers need a non-empty SharedItems list to do anything useful for a pivot that was
/// never round-tripped through a file: <c>SlicerItemResolver.ResolveAvailableItems</c> (a pivot-bound
/// slicer's live filter buttons) and <c>XlsxPivotSlicerCacheData.BuildPivotSlicerCacheDataElement</c> (the
/// saved slicerCacheDefinition's item list) -- both silently produced zero items for a slicer added to a
/// freshly created pivot.
///
/// R115-commands-pivot-sharedItems-refresh: <see cref="MergeFromSourceData"/> is the companion choke point
/// for a field that SURVIVES <c>ReconcileCacheFields</c>'s by-name match on an ORDINARY refresh (no header
/// change) -- it must still pick up new distinct values the live data grew since the last refresh, but
/// (unlike <see cref="BuildFromSourceData"/>) without discarding the existing SharedItems order/index, which
/// a pivot-bound slicer's <see cref="SlicerModel.CacheItems"/> stores positionally.
/// </summary>
internal static class PivotCacheFieldFactory
{
    /// <summary>
    /// Builds a cache field whose sharedItems type/range metadata (ContainsNumber/ContainsDate/
    /// ContainsString + MinValue/MaxValue/MinDate/MaxDate) AND distinct-value <see
    /// cref="PivotCacheFieldModel.SharedItems"/>/<see cref="PivotCacheFieldModel.SharedItemKinds"/> list
    /// reflect the actual source-column data at creation time. Fields created via
    /// <c>new PivotCacheFieldModel(header)</c> alone leave every optional flag at its all-false/null
    /// default (serializing to a bare <c>&lt;sharedItems/&gt;</c> Excel's schema defaults would
    /// misinterpret) AND leave SharedItems null, so neither a live pivot-bound slicer nor the saved
    /// slicerCacheDefinition part has any item to offer.
    /// </summary>
    public static PivotCacheFieldModel BuildFromSourceData(
        string header,
        Sheet sourceSheet,
        GridRange sourceRange,
        int columnIndex)
    {
        var col = sourceRange.Start.Col + (uint)columnIndex;
        var containsString = false;
        var containsNumber = false;
        var containsDate = false;
        var containsBlank = false;
        double? minValue = null;
        double? maxValue = null;
        string? minDate = null;
        string? maxDate = null;

        var sharedItems = new List<string>();
        var sharedItemKinds = new List<char>();
        var seenItems = new HashSet<string>(StringComparer.Ordinal);

        void AddSharedItem(string raw, char kind)
        {
            // Key on kind+raw (not raw alone) so a text "1" and a boolean "1" in the same
            // (mixed-type) column never collapse into a single shared item.
            if (seenItems.Add(kind + "|" + raw))
            {
                sharedItems.Add(raw);
                sharedItemKinds.Add(kind);
            }
        }

        for (var row = sourceRange.Start.Row + 1; row <= sourceRange.End.Row; row++)
        {
            switch (sourceSheet.GetValue(row, col))
            {
                case NumberValue number:
                    containsNumber = true;
                    if (minValue is null || number.Value < minValue.Value)
                        minValue = number.Value;
                    if (maxValue is null || number.Value > maxValue.Value)
                        maxValue = number.Value;
                    AddSharedItem(number.Value.ToString(CultureInfo.InvariantCulture), 'n');
                    break;
                case DateTimeValue date:
                    containsDate = true;
                    var iso = date.ToDateTime().ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
                    if (minDate is null || string.CompareOrdinal(iso, minDate) < 0)
                        minDate = iso;
                    if (maxDate is null || string.CompareOrdinal(iso, maxDate) > 0)
                        maxDate = iso;
                    AddSharedItem(iso, 'd');
                    break;
                case TextValue text when !string.IsNullOrEmpty(text.Value):
                    containsString = true;
                    AddSharedItem(text.Value, 's');
                    break;
                case BoolValue boolean:
                    containsString = true;
                    AddSharedItem(boolean.Value ? "1" : "0", 'b');
                    break;
                case ErrorValue error:
                    containsString = true;
                    AddSharedItem(error.Code, 's');
                    break;
                default:
                    containsBlank = true;
                    break;
            }
        }

        var typeCount = (containsString ? 1 : 0) + (containsNumber ? 1 : 0) + (containsDate ? 1 : 0);
        return new PivotCacheFieldModel(
            header,
            ContainsBlank: containsBlank,
            ContainsString: containsString,
            ContainsNumber: containsNumber,
            ContainsDate: containsDate,
            ContainsMixedTypes: typeCount > 1,
            MinValue: minValue,
            MaxValue: maxValue,
            MinDate: minDate,
            MaxDate: maxDate,
            SharedItems: sharedItems.Count > 0 ? sharedItems : null,
            SharedItemKinds: sharedItemKinds.Count > 0 ? sharedItemKinds : null);
    }

    /// <summary>
    /// R115-commands-pivot-sharedItems-refresh: re-derives <paramref name="existing"/>'s live-data
    /// flags/<see cref="PivotCacheFieldModel.SharedItems"/> from the CURRENT source column on an
    /// ordinary refresh (the field's header did not change, so it survived
    /// <c>PivotTableRefreshService.ReconcileCacheFields</c>'s by-name match), the same way
    /// <see cref="BuildFromSourceData"/> does for a brand-new field -- EXCEPT this preserves the
    /// existing SharedItems order/index for every value still present, appending only genuinely NEW
    /// distinct values at the end, instead of discarding and rebuilding the list from scratch.
    ///
    /// Order/index stability matters because a pivot-bound slicer's <see
    /// cref="SlicerModel.CacheItems"/> stores each item as an INDEX into this list (see
    /// <see cref="SlicerItemResolver"/>): a naive full rebuild-from-scratch on every refresh would
    /// silently renumber existing values whenever a new distinct value happens to be discovered
    /// earlier in row order than some older value, corrupting every existing slicer's selection state
    /// even though nothing the user did should have touched it. Appending keeps every previously
    /// assigned index meaning exactly what it always meant.
    ///
    /// A value that disappeared from the live data (e.g. the only row that had it was edited away) is
    /// deliberately NOT dropped from the merged list -- matching Excel's "retain items" behaviour,
    /// where old items linger in the shared-items cache after a refresh rather than vanishing outright.
    /// </summary>
    public static PivotCacheFieldModel MergeFromSourceData(
        PivotCacheFieldModel existing,
        Sheet sourceSheet,
        GridRange sourceRange,
        int columnIndex)
    {
        var live = BuildFromSourceData(existing.Name, sourceSheet, sourceRange, columnIndex);

        var existingItems = existing.SharedItems;
        if (existingItems is not { Count: > 0 })
        {
            // Nothing to preserve order for yet (e.g. a field that was created without live-data
            // shared items, such as one round-tripped from a source this codebase doesn't compute
            // shared items for) -- take the live-derived flags/SharedItems directly, exactly
            // equivalent to appending onto an empty list. Critically this still preserves `existing`
            // via `with`, not a bare `live` -- SharedItems is only ONE of this field's properties, and
            // NumberFormatId/Grouping/GroupStart(Date)/GroupEnd(Date)/GroupItems/Formula must survive
            // an ordinary refresh exactly like a field that DOES already have SharedItems.
            return existing with
            {
                ContainsBlank = existing.ContainsBlank || live.ContainsBlank,
                ContainsString = existing.ContainsString || live.ContainsString,
                ContainsNumber = existing.ContainsNumber || live.ContainsNumber,
                ContainsDate = existing.ContainsDate || live.ContainsDate,
                ContainsMixedTypes = existing.ContainsMixedTypes || live.ContainsMixedTypes,
                MinValue = MinOrNull(existing.MinValue, live.MinValue),
                MaxValue = MaxOrNull(existing.MaxValue, live.MaxValue),
                MinDate = MinOrNull(existing.MinDate, live.MinDate),
                MaxDate = MaxOrNull(existing.MaxDate, live.MaxDate),
                SharedItems = live.SharedItems,
                SharedItemKinds = live.SharedItemKinds,
            };
        }

        if (live.SharedItems is not { Count: > 0 })
        {
            // The live column currently has no non-blank values at all (e.g. the body was cleared).
            // Keep the existing SharedItems rather than discarding history a slicer may still
            // reference, matching Excel's stale-item retention.
            return existing;
        }

        var existingKinds = existing.SharedItemKinds;
        var mergedItems = new List<string>(existingItems);
        var mergedKinds = new List<char>(existingItems.Count);
        for (var i = 0; i < existingItems.Count; i++)
            mergedKinds.Add(existingKinds is not null && i < existingKinds.Count ? existingKinds[i] : '\0');

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < existingItems.Count; i++)
            seen.Add(mergedKinds[i] + "|" + existingItems[i]);

        var liveKinds = live.SharedItemKinds;
        for (var i = 0; i < live.SharedItems.Count; i++)
        {
            var kind = liveKinds is not null && i < liveKinds.Count ? liveKinds[i] : '\0';
            var key = kind + "|" + live.SharedItems[i];
            if (seen.Add(key))
            {
                mergedItems.Add(live.SharedItems[i]);
                mergedKinds.Add(kind);
            }
        }

        var typeCount = ((existing.ContainsString || live.ContainsString) ? 1 : 0) +
                        ((existing.ContainsNumber || live.ContainsNumber) ? 1 : 0) +
                        ((existing.ContainsDate || live.ContainsDate) ? 1 : 0);

        return existing with
        {
            ContainsBlank = existing.ContainsBlank || live.ContainsBlank,
            ContainsString = existing.ContainsString || live.ContainsString,
            ContainsNumber = existing.ContainsNumber || live.ContainsNumber,
            ContainsDate = existing.ContainsDate || live.ContainsDate,
            ContainsMixedTypes = typeCount > 1,
            MinValue = MinOrNull(existing.MinValue, live.MinValue),
            MaxValue = MaxOrNull(existing.MaxValue, live.MaxValue),
            MinDate = MinOrNull(existing.MinDate, live.MinDate),
            MaxDate = MaxOrNull(existing.MaxDate, live.MaxDate),
            SharedItems = mergedItems,
            SharedItemKinds = mergedKinds,
        };
    }

    /// <summary>
    /// R116-commands-pivot-slicer-changesource: single choke point for reconciling an EXISTING cache's
    /// field list against a (possibly reordered/expanded/narrowed) live header set, preserving every
    /// surviving field's <see cref="PivotCacheFieldModel.SharedItems"/> order/index via
    /// <see cref="MergeFromSourceData"/> instead of discarding it. Used by both
    /// <c>PivotTableRefreshService.ReconcileCacheFields</c> (ordinary refresh / table-growth) and
    /// <c>ChangePivotTableSourceCommand</c> (explicit "Change Data Source", both the same-SourceType
    /// in-place mutation and the cross-SourceType <c>BuildRedirectedCache</c> replacement) so neither
    /// path can drift from the other and silently reintroduce a full SharedItems rebuild that renumbers
    /// a pivot-bound slicer's <see cref="SlicerModel.CacheItems"/> indices out from under it.
    /// A field whose header has no existing same-named match (a truly new column) still gets a
    /// brand-new field built from scratch via <see cref="BuildFromSourceData"/>, exactly as before.
    /// </summary>
    internal static List<PivotCacheFieldModel> ReconcileFields(
        IEnumerable<PivotCacheFieldModel> existingFields,
        IReadOnlyList<string> liveHeaders,
        Sheet sourceSheet,
        GridRange sourceRange)
    {
        var existingByName = new Dictionary<string, PivotCacheFieldModel>(StringComparer.Ordinal);
        foreach (var field in existingFields)
            existingByName.TryAdd(field.Name, field);

        var reconciled = new List<PivotCacheFieldModel>(liveHeaders.Count);
        for (var index = 0; index < liveHeaders.Count; index++)
        {
            var header = liveHeaders[index];
            reconciled.Add(existingByName.TryGetValue(header, out var existing)
                ? MergeFromSourceData(existing, sourceSheet, sourceRange, index)
                : BuildFromSourceData(header, sourceSheet, sourceRange, index));
        }

        return reconciled;
    }

    private static double? MinOrNull(double? a, double? b) =>
        a is null ? b : b is null ? a : Math.Min(a.Value, b.Value);

    private static double? MaxOrNull(double? a, double? b) =>
        a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);

    private static string? MinOrNull(string? a, string? b) =>
        a is null ? b : b is null ? a : (string.CompareOrdinal(a, b) <= 0 ? a : b);

    private static string? MaxOrNull(string? a, string? b) =>
        a is null ? b : b is null ? a : (string.CompareOrdinal(a, b) >= 0 ? a : b);
}
