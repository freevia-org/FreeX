using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private static WorksheetCellWatchesMetadataModel? ToWorksheetCellWatchesMetadata(WorksheetCellWatchesMetadataDto? dto)
    {
        if (dto is null)
            return null;

        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        var watchNativeAttributes = CleanKeyedNativeAttributes(dto.WatchNativeAttributes, CleanNativeAttributes);

        if (nativeAttributes.Count == 0 && watchNativeAttributes.Count == 0)
            return null;

        return new WorksheetCellWatchesMetadataModel
        {
            NativeAttributes = nativeAttributes,
            WatchNativeAttributes = watchNativeAttributes
        };
    }

    private static WorksheetCellWatchesMetadataDto? ToWorksheetCellWatchesMetadataDto(WorksheetCellWatchesMetadataModel? model)
    {
        if (model is null)
            return null;

        var nativeAttributes = CleanNativeAttributesForSave(model.NativeAttributes);
        var watchNativeAttributes = CleanKeyedNativeAttributes(model.WatchNativeAttributes, CleanNativeAttributesForSave);

        if (nativeAttributes.Count == 0 && watchNativeAttributes.Count == 0)
            return null;

        return new WorksheetCellWatchesMetadataDto
        {
            NativeAttributes = nativeAttributes,
            WatchNativeAttributes = watchNativeAttributes
        };
    }

    private static WorksheetIgnoredErrorsMetadataModel? ToWorksheetIgnoredErrorsMetadata(WorksheetIgnoredErrorsMetadataDto? dto)
    {
        if (dto is null)
            return null;

        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        var errorNativeAttributes = CleanKeyedNativeAttributes(dto.ErrorNativeAttributes, CleanNativeAttributes);

        if (nativeAttributes.Count == 0 && errorNativeAttributes.Count == 0)
            return null;

        return new WorksheetIgnoredErrorsMetadataModel
        {
            NativeAttributes = nativeAttributes,
            ErrorNativeAttributes = errorNativeAttributes
        };
    }

    private static WorksheetIgnoredErrorsMetadataDto? ToWorksheetIgnoredErrorsMetadataDto(WorksheetIgnoredErrorsMetadataModel? model)
    {
        if (model is null)
            return null;

        var nativeAttributes = CleanNativeAttributesForSave(model.NativeAttributes);
        var errorNativeAttributes = CleanKeyedNativeAttributes(model.ErrorNativeAttributes, CleanNativeAttributesForSave);

        if (nativeAttributes.Count == 0 && errorNativeAttributes.Count == 0)
            return null;

        return new WorksheetIgnoredErrorsMetadataDto
        {
            NativeAttributes = nativeAttributes,
            ErrorNativeAttributes = errorNativeAttributes
        };
    }

    private static Dictionary<string, Dictionary<string, string>> CleanKeyedNativeAttributes(
        IReadOnlyDictionary<string, Dictionary<string, string>>? source,
        Func<Dictionary<string, string>?, Dictionary<string, string>> cleanAttributes)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        if (source is null)
            return result;

        foreach (var pair in source)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
                continue;

            var attributes = cleanAttributes(pair.Value);
            if (attributes.Count > 0)
                result[pair.Key.Trim()] = attributes;
        }

        return result;
    }
}
