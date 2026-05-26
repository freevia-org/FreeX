using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private static WorksheetSortStateModel? ToWorksheetSortState(WorksheetSortStateDto? dto)
    {
        if (dto is null)
            return null;

        var reference = string.IsNullOrWhiteSpace(dto.Reference) ? null : dto.Reference;
        var sortMethod = string.IsNullOrWhiteSpace(dto.SortMethod) ? null : dto.SortMethod;
        var nativeXml = string.IsNullOrWhiteSpace(dto.NativeXml) ? null : dto.NativeXml;
        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        var conditions = (dto.Conditions ?? [])
            .Select(ToWorksheetSortCondition)
            .OfType<WorksheetSortConditionModel>()
            .ToList();
        if (reference is null && dto.ColumnSort is null && dto.CaseSensitive is null && sortMethod is null &&
            nativeXml is null && nativeAttributes.Count == 0 && conditions.Count == 0)
        {
            return null;
        }

        return new WorksheetSortStateModel
        {
            Reference = reference,
            ColumnSort = dto.ColumnSort,
            CaseSensitive = dto.CaseSensitive,
            SortMethod = sortMethod,
            NativeXml = nativeXml,
            NativeAttributes = nativeAttributes,
            Conditions = conditions
        };
    }

    private static WorksheetSortConditionModel? ToWorksheetSortCondition(WorksheetSortConditionDto? dto)
    {
        if (dto is null)
            return null;

        var reference = string.IsNullOrWhiteSpace(dto.Reference) ? null : dto.Reference;
        var sortBy = string.IsNullOrWhiteSpace(dto.SortBy) ? null : dto.SortBy;
        var customList = string.IsNullOrWhiteSpace(dto.CustomList) ? null : dto.CustomList;
        var dxfId = string.IsNullOrWhiteSpace(dto.DxfId) ? null : dto.DxfId;
        var iconSet = string.IsNullOrWhiteSpace(dto.IconSet) ? null : dto.IconSet;
        var iconId = string.IsNullOrWhiteSpace(dto.IconId) ? null : dto.IconId;
        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        if (reference is null && dto.Descending is null && sortBy is null && customList is null &&
            dxfId is null && iconSet is null && iconId is null && nativeAttributes.Count == 0)
        {
            return null;
        }

        return new WorksheetSortConditionModel
        {
            Reference = reference,
            Descending = dto.Descending,
            SortBy = sortBy,
            CustomList = customList,
            DxfId = dxfId,
            IconSet = iconSet,
            IconId = iconId,
            NativeAttributes = nativeAttributes
        };
    }

    private static WorksheetSortStateDto? ToWorksheetSortStateDto(WorksheetSortStateModel? model)
    {
        if (model is null)
            return null;

        var reference = string.IsNullOrWhiteSpace(model.Reference) ? null : model.Reference;
        var sortMethod = string.IsNullOrWhiteSpace(model.SortMethod) ? null : model.SortMethod;
        var nativeXml = string.IsNullOrWhiteSpace(model.NativeXml) ? null : model.NativeXml;
        var nativeAttributes = CleanNativeAttributes(model.NativeAttributes);
        var conditions = model.Conditions
            .Select(ToWorksheetSortConditionDto)
            .OfType<WorksheetSortConditionDto>()
            .ToList();
        if (reference is null && model.ColumnSort is null && model.CaseSensitive is null && sortMethod is null &&
            nativeXml is null && nativeAttributes.Count == 0 && conditions.Count == 0)
        {
            return null;
        }

        return new WorksheetSortStateDto
        {
            Reference = reference,
            ColumnSort = model.ColumnSort,
            CaseSensitive = model.CaseSensitive,
            SortMethod = sortMethod,
            NativeXml = nativeXml,
            NativeAttributes = nativeAttributes,
            Conditions = conditions
        };
    }

    private static WorksheetSortConditionDto? ToWorksheetSortConditionDto(WorksheetSortConditionModel? model)
    {
        if (model is null)
            return null;

        var reference = string.IsNullOrWhiteSpace(model.Reference) ? null : model.Reference;
        var sortBy = string.IsNullOrWhiteSpace(model.SortBy) ? null : model.SortBy;
        var customList = string.IsNullOrWhiteSpace(model.CustomList) ? null : model.CustomList;
        var dxfId = string.IsNullOrWhiteSpace(model.DxfId) ? null : model.DxfId;
        var iconSet = string.IsNullOrWhiteSpace(model.IconSet) ? null : model.IconSet;
        var iconId = string.IsNullOrWhiteSpace(model.IconId) ? null : model.IconId;
        var nativeAttributes = CleanNativeAttributes(model.NativeAttributes);
        if (reference is null && model.Descending is null && sortBy is null && customList is null &&
            dxfId is null && iconSet is null && iconId is null && nativeAttributes.Count == 0)
        {
            return null;
        }

        return new WorksheetSortConditionDto
        {
            Reference = reference,
            Descending = model.Descending,
            SortBy = sortBy,
            CustomList = customList,
            DxfId = dxfId,
            IconSet = iconSet,
            IconId = iconId,
            NativeAttributes = nativeAttributes
        };
    }
}
