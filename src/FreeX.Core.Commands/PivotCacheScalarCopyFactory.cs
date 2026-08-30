using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Copies the scalar state shared by replacement <see cref="PivotCacheModel"/> instances.
/// Collection policies remain at each call site because duplicating a sheet copies the existing
/// fields verbatim, while Change Data Source reconciles fields against the new source.
/// </summary>
internal static class PivotCacheScalarCopyFactory
{
    internal static PivotCacheModel Create(
        PivotCacheModel original,
        int cacheId,
        PivotCacheSourceType sourceType,
        string? sourceSheetName,
        string? sourceReference,
        string? sourceTableName,
        int? sourceTableId,
        string packagePart) =>
        new()
        {
            CacheId = cacheId,
            SourceType = sourceType,
            SourceSheetName = sourceSheetName,
            SourceReference = sourceReference,
            SourceTableName = sourceTableName,
            SourceTableId = sourceTableId,
            PackagePart = packagePart,
            ConnectionId = original.ConnectionId,
            IsOlap = original.IsOlap,
            RefreshOnLoad = original.RefreshOnLoad,
            SaveData = original.SaveData,
            EnableRefresh = original.EnableRefresh,
            PreserveSourceSortFilter = original.PreserveSourceSortFilter,
            MissingItemsLimit = original.MissingItemsLimit,
            RecordCount = original.RecordCount,
            CreatedVersion = original.CreatedVersion,
            MinRefreshableVersion = original.MinRefreshableVersion,
            RefreshedVersion = original.RefreshedVersion,
            RefreshedBy = original.RefreshedBy,
            RefreshedDateIso = original.RefreshedDateIso,
            RawRecordsXml = original.RawRecordsXml,
        };
}
