using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private static WorksheetAutoFilterModel? ToWorksheetAutoFilter(WorksheetAutoFilterDto? dto) =>
        dto is null || (string.IsNullOrWhiteSpace(dto.Reference) && string.IsNullOrWhiteSpace(dto.NativeXml))
            ? null
            : new WorksheetAutoFilterModel(dto.Reference, dto.NativeXml);

    private static WorksheetAutoFilterDto? ToWorksheetAutoFilterDto(WorksheetAutoFilterModel? autoFilter) =>
        autoFilter is null
            ? null
            : new WorksheetAutoFilterDto
            {
                Reference = autoFilter.Reference,
                NativeXml = autoFilter.NativeXml
            };

    private static WorksheetProtectionMetadataModel? ToWorksheetProtectionMetadata(WorksheetProtectionMetadataDto? dto)
    {
        if (dto is null)
            return null;

        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        var nativeChildXmls = (dto.NativeChildXmls ?? [])
            .Where(xml => !string.IsNullOrWhiteSpace(xml))
            .ToList();
        if (nativeAttributes.Count == 0 && nativeChildXmls.Count == 0)
            return null;

        return new WorksheetProtectionMetadataModel
        {
            NativeAttributes = nativeAttributes,
            NativeChildXmls = nativeChildXmls
        };
    }

    private static WorksheetPageSetupMetadataModel? ToWorksheetPageSetupMetadata(WorksheetPageSetupMetadataDto? dto)
    {
        if (dto is null)
            return null;

        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        var nativeChildXmls = (dto.NativeChildXmls ?? [])
            .Where(xml => !string.IsNullOrWhiteSpace(xml))
            .ToList();
        if (nativeAttributes.Count == 0 && nativeChildXmls.Count == 0)
            return null;

        return new WorksheetPageSetupMetadataModel
        {
            NativeAttributes = nativeAttributes,
            NativeChildXmls = nativeChildXmls
        };
    }

    private static WorksheetPrintOptionsMetadataModel? ToWorksheetPrintOptionsMetadata(WorksheetPrintOptionsMetadataDto? dto)
    {
        if (dto is null)
            return null;

        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        var nativeChildXmls = (dto.NativeChildXmls ?? [])
            .Where(xml => !string.IsNullOrWhiteSpace(xml))
            .ToList();
        if (nativeAttributes.Count == 0 && nativeChildXmls.Count == 0)
            return null;

        return new WorksheetPrintOptionsMetadataModel
        {
            NativeAttributes = nativeAttributes,
            NativeChildXmls = nativeChildXmls
        };
    }

    private static WorksheetSheetFormatMetadataModel? ToWorksheetSheetFormatMetadata(WorksheetSheetFormatMetadataDto? dto)
    {
        if (dto is null)
            return null;

        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        var nativeChildXmls = (dto.NativeChildXmls ?? [])
            .Where(xml => !string.IsNullOrWhiteSpace(xml))
            .ToList();
        if (nativeAttributes.Count == 0 && nativeChildXmls.Count == 0)
            return null;

        return new WorksheetSheetFormatMetadataModel
        {
            NativeAttributes = nativeAttributes,
            NativeChildXmls = nativeChildXmls
        };
    }

    private static WorksheetDimensionMetadataModel? ToWorksheetDimensionMetadata(WorksheetDimensionMetadataDto? dto)
    {
        if (dto is null)
            return null;

        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        if (nativeAttributes.Count == 0)
            return null;

        return new WorksheetDimensionMetadataModel
        {
            NativeAttributes = nativeAttributes
        };
    }

    private static WorksheetSheetPropertiesMetadataModel? ToWorksheetSheetPropertiesMetadata(WorksheetSheetPropertiesMetadataDto? dto)
    {
        if (dto is null)
            return null;

        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        var nativeChildXmls = (dto.NativeChildXmls ?? [])
            .Where(xml => !string.IsNullOrWhiteSpace(xml))
            .ToList();
        if (nativeAttributes.Count == 0 && nativeChildXmls.Count == 0)
            return null;

        return new WorksheetSheetPropertiesMetadataModel
        {
            NativeAttributes = nativeAttributes,
            NativeChildXmls = nativeChildXmls
        };
    }

    private static WorksheetPrimaryViewMetadataModel? ToWorksheetPrimaryViewMetadata(WorksheetPrimaryViewMetadataDto? dto)
    {
        if (dto is null)
            return null;

        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        var nativeChildXmls = (dto.NativeChildXmls ?? [])
            .Where(xml => !string.IsNullOrWhiteSpace(xml))
            .ToList();
        if (nativeAttributes.Count == 0 && nativeChildXmls.Count == 0)
            return null;

        return new WorksheetPrimaryViewMetadataModel
        {
            NativeAttributes = nativeAttributes,
            NativeChildXmls = nativeChildXmls
        };
    }

    private static WorksheetPageBreaksMetadataModel? ToWorksheetPageBreaksMetadata(WorksheetPageBreaksMetadataDto? dto)
    {
        if (dto is null)
            return null;

        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        var breakNativeAttributes = new Dictionary<uint, Dictionary<string, string>>();
        foreach (var pair in dto.BreakNativeAttributes ?? [])
        {
            var attributes = CleanNativeAttributes(pair.Value);
            if (attributes.Count > 0)
                breakNativeAttributes[pair.Key] = attributes;
        }

        if (nativeAttributes.Count == 0 && breakNativeAttributes.Count == 0)
            return null;

        return new WorksheetPageBreaksMetadataModel
        {
            NativeAttributes = nativeAttributes,
            BreakNativeAttributes = breakNativeAttributes
        };
    }

    private static WorksheetPageMarginsMetadataModel? ToWorksheetPageMarginsMetadata(WorksheetPageMarginsMetadataDto? dto)
    {
        if (dto is null)
            return null;

        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        var nativeChildXmls = (dto.NativeChildXmls ?? [])
            .Where(xml => !string.IsNullOrWhiteSpace(xml))
            .ToList();
        if (nativeAttributes.Count == 0 && nativeChildXmls.Count == 0)
            return null;

        return new WorksheetPageMarginsMetadataModel
        {
            NativeAttributes = nativeAttributes,
            NativeChildXmls = nativeChildXmls
        };
    }

    private static WorksheetHeaderFooterMetadataModel? ToWorksheetHeaderFooterMetadata(WorksheetHeaderFooterMetadataDto? dto)
    {
        if (dto is null)
            return null;

        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        var nativeChildXmls = (dto.NativeChildXmls ?? [])
            .Where(xml => !string.IsNullOrWhiteSpace(xml))
            .ToList();
        if (nativeAttributes.Count == 0 && nativeChildXmls.Count == 0)
            return null;

        return new WorksheetHeaderFooterMetadataModel
        {
            NativeAttributes = nativeAttributes,
            NativeChildXmls = nativeChildXmls
        };
    }

    private static WorksheetSingleXmlCellsModel? ToWorksheetSingleXmlCells(WorksheetSingleXmlCellsDto? dto)
    {
        if (dto is null)
            return null;

        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        var cells = (dto.Cells ?? [])
            .Select(ToWorksheetSingleXmlCell)
            .OfType<WorksheetSingleXmlCellModel>()
            .ToList();
        if (nativeAttributes.Count == 0 && cells.Count == 0)
            return null;

        return new WorksheetSingleXmlCellsModel
        {
            NativeAttributes = nativeAttributes,
            Cells = cells
        };
    }

    private static WorksheetSingleXmlCellModel? ToWorksheetSingleXmlCell(WorksheetSingleXmlCellDto? dto)
    {
        if (dto is null)
            return null;

        var reference = string.IsNullOrWhiteSpace(dto.Reference) ? null : dto.Reference;
        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        if (dto.Id is null && reference is null && dto.XmlCellPropertyId is null && nativeAttributes.Count == 0)
            return null;

        return new WorksheetSingleXmlCellModel
        {
            Id = dto.Id,
            Reference = reference,
            XmlCellPropertyId = dto.XmlCellPropertyId,
            NativeAttributes = nativeAttributes
        };
    }

    private static WorksheetSingleXmlCellsDto? ToWorksheetSingleXmlCellsDto(WorksheetSingleXmlCellsModel? model)
    {
        if (model is null)
            return null;

        var nativeAttributes = CleanNativeAttributesForSave(model.NativeAttributes);
        var cells = model.Cells
            .Select(ToWorksheetSingleXmlCellDto)
            .OfType<WorksheetSingleXmlCellDto>()
            .ToList();
        if (nativeAttributes.Count == 0 && cells.Count == 0)
            return null;

        return new WorksheetSingleXmlCellsDto
        {
            NativeAttributes = nativeAttributes,
            Cells = cells
        };
    }

    private static WorksheetSingleXmlCellDto? ToWorksheetSingleXmlCellDto(WorksheetSingleXmlCellModel? model)
    {
        if (model is null)
            return null;

        var reference = string.IsNullOrWhiteSpace(model.Reference) ? null : model.Reference;
        var nativeAttributes = CleanNativeAttributesForSave(model.NativeAttributes);
        if (model.Id is null && reference is null && model.XmlCellPropertyId is null && nativeAttributes.Count == 0)
            return null;

        return new WorksheetSingleXmlCellDto
        {
            Id = model.Id,
            Reference = reference,
            XmlCellPropertyId = model.XmlCellPropertyId,
            NativeAttributes = nativeAttributes
        };
    }

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

    private static WorksheetDataConsolidationModel? ToWorksheetDataConsolidation(WorksheetDataConsolidationDto? dto)
    {
        if (dto is null)
            return null;

        var function = string.IsNullOrWhiteSpace(dto.Function) ? null : dto.Function;
        var nativeXml = string.IsNullOrWhiteSpace(dto.NativeXml) ? null : dto.NativeXml;
        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        var references = (dto.References ?? [])
            .Select(ToWorksheetDataConsolidationReference)
            .OfType<WorksheetDataConsolidationReferenceModel>()
            .ToList();
        if (function is null && dto.LeftLabels is null && dto.TopLabels is null && dto.Link is null &&
            nativeXml is null && nativeAttributes.Count == 0 && references.Count == 0)
        {
            return null;
        }

        return new WorksheetDataConsolidationModel
        {
            Function = function,
            LeftLabels = dto.LeftLabels,
            TopLabels = dto.TopLabels,
            Link = dto.Link,
            NativeXml = nativeXml,
            NativeAttributes = nativeAttributes,
            References = references
        };
    }

    private static WorksheetDataConsolidationReferenceModel? ToWorksheetDataConsolidationReference(
        WorksheetDataConsolidationReferenceDto? dto)
    {
        if (dto is null)
            return null;

        var reference = string.IsNullOrWhiteSpace(dto.Reference) ? null : dto.Reference;
        var sheet = string.IsNullOrWhiteSpace(dto.Sheet) ? null : dto.Sheet;
        var name = string.IsNullOrWhiteSpace(dto.Name) ? null : dto.Name;
        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        if (reference is null && sheet is null && name is null && nativeAttributes.Count == 0)
            return null;

        return new WorksheetDataConsolidationReferenceModel
        {
            Reference = reference,
            Sheet = sheet,
            Name = name,
            NativeAttributes = nativeAttributes
        };
    }

    private static WorksheetDataConsolidationDto? ToWorksheetDataConsolidationDto(
        WorksheetDataConsolidationModel? model)
    {
        if (model is null)
            return null;

        var function = string.IsNullOrWhiteSpace(model.Function) ? null : model.Function;
        var nativeXml = string.IsNullOrWhiteSpace(model.NativeXml) ? null : model.NativeXml;
        var nativeAttributes = CleanNativeAttributes(model.NativeAttributes);
        var references = model.References
            .Select(ToWorksheetDataConsolidationReferenceDto)
            .OfType<WorksheetDataConsolidationReferenceDto>()
            .ToList();
        if (function is null && model.LeftLabels is null && model.TopLabels is null && model.Link is null &&
            nativeXml is null && nativeAttributes.Count == 0 && references.Count == 0)
        {
            return null;
        }

        return new WorksheetDataConsolidationDto
        {
            Function = function,
            LeftLabels = model.LeftLabels,
            TopLabels = model.TopLabels,
            Link = model.Link,
            NativeXml = nativeXml,
            NativeAttributes = nativeAttributes,
            References = references
        };
    }

    private static WorksheetDataConsolidationReferenceDto? ToWorksheetDataConsolidationReferenceDto(
        WorksheetDataConsolidationReferenceModel? model)
    {
        if (model is null)
            return null;

        var reference = string.IsNullOrWhiteSpace(model.Reference) ? null : model.Reference;
        var sheet = string.IsNullOrWhiteSpace(model.Sheet) ? null : model.Sheet;
        var name = string.IsNullOrWhiteSpace(model.Name) ? null : model.Name;
        var nativeAttributes = CleanNativeAttributes(model.NativeAttributes);
        if (reference is null && sheet is null && name is null && nativeAttributes.Count == 0)
            return null;

        return new WorksheetDataConsolidationReferenceDto
        {
            Reference = reference,
            Sheet = sheet,
            Name = name,
            NativeAttributes = nativeAttributes
        };
    }

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

    private static WorksheetAdditionalViewsModel? ToWorksheetAdditionalViews(WorksheetAdditionalViewsDto? dto)
    {
        if (dto is null)
            return null;

        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        var views = (dto.Views ?? [])
            .Select(ToWorksheetAdditionalView)
            .OfType<WorksheetAdditionalViewModel>()
            .ToList();
        if (nativeAttributes.Count == 0 && views.Count == 0)
            return null;

        return new WorksheetAdditionalViewsModel
        {
            NativeAttributes = nativeAttributes,
            Views = views
        };
    }

    private static WorksheetAdditionalViewModel? ToWorksheetAdditionalView(WorksheetAdditionalViewDto? dto)
    {
        if (dto is null)
            return null;

        var workbookViewId = string.IsNullOrWhiteSpace(dto.WorkbookViewId) ? null : dto.WorkbookViewId;
        var nativeXml = string.IsNullOrWhiteSpace(dto.NativeXml) ? null : dto.NativeXml;
        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        if (workbookViewId is null && nativeXml is null && nativeAttributes.Count == 0)
            return null;

        return new WorksheetAdditionalViewModel
        {
            WorkbookViewId = workbookViewId,
            NativeXml = nativeXml,
            NativeAttributes = nativeAttributes
        };
    }

    private static WorksheetAdditionalViewsDto? ToWorksheetAdditionalViewsDto(WorksheetAdditionalViewsModel? model)
    {
        if (model is null)
            return null;

        var nativeAttributes = CleanNativeAttributes(model.NativeAttributes);
        var views = model.Views
            .Select(ToWorksheetAdditionalViewDto)
            .OfType<WorksheetAdditionalViewDto>()
            .ToList();
        if (nativeAttributes.Count == 0 && views.Count == 0)
            return null;

        return new WorksheetAdditionalViewsDto
        {
            NativeAttributes = nativeAttributes,
            Views = views
        };
    }

    private static WorksheetAdditionalViewDto? ToWorksheetAdditionalViewDto(WorksheetAdditionalViewModel? model)
    {
        if (model is null)
            return null;

        var workbookViewId = string.IsNullOrWhiteSpace(model.WorkbookViewId) ? null : model.WorkbookViewId;
        var nativeXml = string.IsNullOrWhiteSpace(model.NativeXml) ? null : model.NativeXml;
        var nativeAttributes = CleanNativeAttributes(model.NativeAttributes);
        if (workbookViewId is null && nativeXml is null && nativeAttributes.Count == 0)
            return null;

        return new WorksheetAdditionalViewDto
        {
            WorkbookViewId = workbookViewId,
            NativeXml = nativeXml,
            NativeAttributes = nativeAttributes
        };
    }

    private static WorksheetCustomViewState ToWorksheetCustomViewState(CustomViewSheetDto sheetDto)
    {
        var frozenRows = NativeJsonValueSanitizer.ValidFrozenRowsOrZero(sheetDto.FrozenRows);
        var frozenCols = NativeJsonValueSanitizer.ValidFrozenColumnsOrZero(sheetDto.FrozenCols);
        var hasFrozenPanes = frozenRows > 0 || frozenCols > 0;
        return new WorksheetCustomViewState(
            sheetDto.SheetName,
            Enum.IsDefined(sheetDto.ViewMode) ? sheetDto.ViewMode : WorksheetViewMode.Normal,
            frozenRows,
            frozenCols,
            hasFrozenPanes ? null : NativeJsonValueSanitizer.ValidRowPaneOrNull(sheetDto.SplitRow),
            hasFrozenPanes ? null : NativeJsonValueSanitizer.ValidColumnPaneOrNull(sheetDto.SplitColumn),
            sheetDto.ShowGridlines ?? true,
            sheetDto.ShowHeadings ?? true,
            sheetDto.ShowRulers ?? true,
            NativeJsonValueSanitizer.ValidZoomPercentOrDefault(sheetDto.ZoomPercent),
            sheetDto.ShowFormulas ?? false);
    }

    private static CustomViewSheetDto ToCustomViewSheetDto(WorksheetCustomViewState state)
    {
        var frozenRows = NativeJsonValueSanitizer.ValidFrozenRowsOrZero(state.FrozenRows);
        var frozenCols = NativeJsonValueSanitizer.ValidFrozenColumnsOrZero(state.FrozenCols);
        var hasFrozenPanes = frozenRows > 0 || frozenCols > 0;
        return new CustomViewSheetDto
        {
            SheetName = state.SheetName,
            ViewMode = NativeJsonValueSanitizer.ValidEnumOrDefault(state.ViewMode, WorksheetViewMode.Normal),
            FrozenRows = frozenRows,
            FrozenCols = frozenCols,
            SplitRow = hasFrozenPanes ? null : NativeJsonValueSanitizer.ValidRowPaneOrNull(state.SplitRow),
            SplitColumn = hasFrozenPanes ? null : NativeJsonValueSanitizer.ValidColumnPaneOrNull(state.SplitColumn),
            ShowGridlines = state.ShowGridlines,
            ShowHeadings = state.ShowHeadings,
            ShowRulers = state.ShowRulers,
            ZoomPercent = NativeJsonValueSanitizer.ValidZoomPercentOrDefault(state.ZoomPercent),
            ShowFormulas = state.ShowFormulas
        };
    }

    private static WorksheetPhoneticProperties? ToWorksheetPhoneticProperties(WorksheetPhoneticPropertiesDto? dto)
    {
        if (dto is null)
            return null;

        var fontId = string.IsNullOrWhiteSpace(dto.FontId) ? null : dto.FontId;
        var type = string.IsNullOrWhiteSpace(dto.Type) ? null : dto.Type;
        var alignment = string.IsNullOrWhiteSpace(dto.Alignment) ? null : dto.Alignment;
        return fontId is null && type is null && alignment is null
            ? null
            : new WorksheetPhoneticProperties(fontId, type, alignment);
    }

    private static WorksheetPhoneticPropertiesDto? ToWorksheetPhoneticPropertiesDto(WorksheetPhoneticProperties? properties)
    {
        if (properties is null)
            return null;

        var fontId = string.IsNullOrWhiteSpace(properties.FontId) ? null : properties.FontId;
        var type = string.IsNullOrWhiteSpace(properties.Type) ? null : properties.Type;
        var alignment = string.IsNullOrWhiteSpace(properties.Alignment) ? null : properties.Alignment;
        return fontId is null && type is null && alignment is null
            ? null
            : new WorksheetPhoneticPropertiesDto
            {
                FontId = fontId,
                Type = type,
                Alignment = alignment
            };
    }

    private static WorksheetRepeatRange? ToRepeatRange(RepeatRangeDto? dto) =>
        dto is null ? null : new WorksheetRepeatRange(dto.Start, dto.End);

    private static RepeatRangeDto? FromRepeatRange(WorksheetRepeatRange? range) =>
        range is null ? null : new RepeatRangeDto { Start = range.Value.Start, End = range.Value.End };

    private static RepeatRangeDto? FromValidRepeatRange(WorksheetRepeatRange? range, uint max) =>
        range is { } value && value.Start >= 1 && value.End >= value.Start && value.End <= max
            ? new RepeatRangeDto { Start = value.Start, End = value.End }
            : null;

    private static PageMarginsDto FromPageMargins(WorksheetPageMargins margins) =>
        new()
        {
            Left = margins.Left,
            Right = margins.Right,
            Top = margins.Top,
            Bottom = margins.Bottom
        };

    private static WorksheetHeaderFooter ToHeaderFooter(HeaderFooterDto? dto) =>
        dto is null
            ? new WorksheetHeaderFooter("", "", "")
            : new WorksheetHeaderFooter(dto.Left ?? "", dto.Center ?? "", dto.Right ?? "");

    private static HeaderFooterDto FromHeaderFooter(WorksheetHeaderFooter value) =>
        new() { Left = value.Left, Center = value.Center, Right = value.Right };

    private static WorksheetHeaderFooterPictureSet ToHeaderFooterPictures(HeaderFooterPictureSetDto? dto) =>
        dto is null
            ? WorksheetHeaderFooterPictureSet.Empty
            : new WorksheetHeaderFooterPictureSet(
                ToHeaderFooterPicture(dto.Left),
                ToHeaderFooterPicture(dto.Center),
                ToHeaderFooterPicture(dto.Right));

    private static HeaderFooterPictureSetDto? FromHeaderFooterPictures(WorksheetHeaderFooterPictureSet value) =>
        value.Left is null && value.Center is null && value.Right is null
            ? null
            : new HeaderFooterPictureSetDto
            {
                Left = FromHeaderFooterPicture(value.Left),
                Center = FromHeaderFooterPicture(value.Center),
                Right = FromHeaderFooterPicture(value.Right)
            };

    private static WorksheetHeaderFooterPicture? ToHeaderFooterPicture(HeaderFooterPictureDto? dto)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.ImageBase64))
            return null;

        try
        {
            var bytes = Convert.FromBase64String(dto.ImageBase64);
            return new WorksheetHeaderFooterPicture(
                bytes,
                string.IsNullOrWhiteSpace(dto.ContentType) ? "image/png" : dto.ContentType,
                dto.FileName,
                NativeJsonValueSanitizer.PositiveFiniteOrDefault(dto.Width, 96),
                NativeJsonValueSanitizer.PositiveFiniteOrDefault(dto.Height, 48));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static HeaderFooterPictureDto? FromHeaderFooterPicture(WorksheetHeaderFooterPicture? value) =>
        value is null
            ? null
            : new HeaderFooterPictureDto
            {
                ImageBase64 = Convert.ToBase64String(value.ImageBytes),
                ContentType = value.ContentType,
                FileName = value.FileName,
                Width = NativeJsonValueSanitizer.PositiveFiniteOrDefault(value.Width, 96),
                Height = NativeJsonValueSanitizer.PositiveFiniteOrDefault(value.Height, 48)
            };
}
