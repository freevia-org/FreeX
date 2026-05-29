using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private static WorksheetSmartTagsModel? ToWorksheetSmartTags(WorksheetSmartTagsDto? dto)
    {
        if (dto is null)
            return null;

        var nativeXml = string.IsNullOrWhiteSpace(dto.NativeXml) ? null : dto.NativeXml;
        var cells = (dto.Cells ?? [])
            .Select(ToWorksheetCellSmartTags)
            .OfType<WorksheetCellSmartTagsModel>()
            .ToList();
        if (nativeXml is null && cells.Count == 0)
            return null;

        return new WorksheetSmartTagsModel
        {
            NativeXml = nativeXml,
            Cells = cells
        };
    }

    private static WorksheetCellSmartTagsModel? ToWorksheetCellSmartTags(WorksheetCellSmartTagsDto? dto)
    {
        if (dto is null)
            return null;

        var reference = string.IsNullOrWhiteSpace(dto.Reference) ? null : dto.Reference;
        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        var tags = (dto.Tags ?? [])
            .Select(ToWorksheetCellSmartTag)
            .OfType<WorksheetCellSmartTagModel>()
            .ToList();
        if (reference is null && nativeAttributes.Count == 0 && tags.Count == 0)
            return null;

        return new WorksheetCellSmartTagsModel
        {
            Reference = reference,
            NativeAttributes = nativeAttributes,
            Tags = tags
        };
    }

    private static WorksheetCellSmartTagModel? ToWorksheetCellSmartTag(WorksheetCellSmartTagDto? dto)
    {
        if (dto is null)
            return null;

        var type = string.IsNullOrWhiteSpace(dto.Type) ? null : dto.Type;
        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        var properties = (dto.Properties ?? [])
            .Select(ToWorksheetCellSmartTagProperty)
            .OfType<WorksheetCellSmartTagPropertyModel>()
            .ToList();
        if (type is null && dto.Deleted is null && nativeAttributes.Count == 0 && properties.Count == 0)
            return null;

        return new WorksheetCellSmartTagModel
        {
            Type = type,
            Deleted = dto.Deleted,
            NativeAttributes = nativeAttributes,
            Properties = properties
        };
    }

    private static WorksheetCellSmartTagPropertyModel? ToWorksheetCellSmartTagProperty(WorksheetCellSmartTagPropertyDto? dto)
    {
        if (dto is null)
            return null;

        var key = string.IsNullOrWhiteSpace(dto.Key) ? null : dto.Key;
        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        if (key is null && dto.Value is null && nativeAttributes.Count == 0)
            return null;

        return new WorksheetCellSmartTagPropertyModel
        {
            Key = key,
            Value = dto.Value,
            NativeAttributes = nativeAttributes
        };
    }

    private static WorksheetSmartTagsDto? ToWorksheetSmartTagsDto(WorksheetSmartTagsModel? model)
    {
        if (model is null)
            return null;

        var nativeXml = string.IsNullOrWhiteSpace(model.NativeXml) ? null : model.NativeXml;
        var cells = model.Cells
            .Select(ToWorksheetCellSmartTagsDto)
            .OfType<WorksheetCellSmartTagsDto>()
            .ToList();
        if (nativeXml is null && cells.Count == 0)
            return null;

        return new WorksheetSmartTagsDto
        {
            NativeXml = nativeXml,
            Cells = cells
        };
    }

    private static WorksheetCellSmartTagsDto? ToWorksheetCellSmartTagsDto(WorksheetCellSmartTagsModel? model)
    {
        if (model is null)
            return null;

        var reference = string.IsNullOrWhiteSpace(model.Reference) ? null : model.Reference;
        var nativeAttributes = CleanNativeAttributes(model.NativeAttributes);
        var tags = model.Tags
            .Select(ToWorksheetCellSmartTagDto)
            .OfType<WorksheetCellSmartTagDto>()
            .ToList();
        if (reference is null && nativeAttributes.Count == 0 && tags.Count == 0)
            return null;

        return new WorksheetCellSmartTagsDto
        {
            Reference = reference,
            NativeAttributes = nativeAttributes,
            Tags = tags
        };
    }

    private static WorksheetCellSmartTagDto? ToWorksheetCellSmartTagDto(WorksheetCellSmartTagModel? model)
    {
        if (model is null)
            return null;

        var type = string.IsNullOrWhiteSpace(model.Type) ? null : model.Type;
        var nativeAttributes = CleanNativeAttributes(model.NativeAttributes);
        var properties = model.Properties
            .Select(ToWorksheetCellSmartTagPropertyDto)
            .OfType<WorksheetCellSmartTagPropertyDto>()
            .ToList();
        if (type is null && model.Deleted is null && nativeAttributes.Count == 0 && properties.Count == 0)
            return null;

        return new WorksheetCellSmartTagDto
        {
            Type = type,
            Deleted = model.Deleted,
            NativeAttributes = nativeAttributes,
            Properties = properties
        };
    }

    private static WorksheetCellSmartTagPropertyDto? ToWorksheetCellSmartTagPropertyDto(WorksheetCellSmartTagPropertyModel? model)
    {
        if (model is null)
            return null;

        var key = string.IsNullOrWhiteSpace(model.Key) ? null : model.Key;
        var nativeAttributes = CleanNativeAttributes(model.NativeAttributes);
        if (key is null && model.Value is null && nativeAttributes.Count == 0)
            return null;

        return new WorksheetCellSmartTagPropertyDto
        {
            Key = key,
            Value = model.Value,
            NativeAttributes = nativeAttributes
        };
    }
}
