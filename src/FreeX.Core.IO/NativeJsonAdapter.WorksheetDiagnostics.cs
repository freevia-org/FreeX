using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private static WorksheetCellWatchesMetadataModel? ToWorksheetCellWatchesMetadata(WorksheetCellWatchesMetadataDto? dto)
    {
        if (dto is null)
            return null;

        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        var watchNativeAttributes = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in dto.WatchNativeAttributes ?? [])
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
                continue;

            var attributes = CleanNativeAttributes(pair.Value);
            if (attributes.Count > 0)
                watchNativeAttributes[pair.Key.Trim()] = attributes;
        }

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
        var watchNativeAttributes = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in model.WatchNativeAttributes)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
                continue;

            var attributes = CleanNativeAttributesForSave(pair.Value);
            if (attributes.Count > 0)
                watchNativeAttributes[pair.Key.Trim()] = attributes;
        }

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
        var errorNativeAttributes = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in dto.ErrorNativeAttributes ?? [])
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
                continue;

            var attributes = CleanNativeAttributes(pair.Value);
            if (attributes.Count > 0)
                errorNativeAttributes[pair.Key.Trim()] = attributes;
        }

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
        var errorNativeAttributes = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in model.ErrorNativeAttributes)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
                continue;

            var attributes = CleanNativeAttributesForSave(pair.Value);
            if (attributes.Count > 0)
                errorNativeAttributes[pair.Key.Trim()] = attributes;
        }

        if (nativeAttributes.Count == 0 && errorNativeAttributes.Count == 0)
            return null;

        return new WorksheetIgnoredErrorsMetadataDto
        {
            NativeAttributes = nativeAttributes,
            ErrorNativeAttributes = errorNativeAttributes
        };
    }
}
