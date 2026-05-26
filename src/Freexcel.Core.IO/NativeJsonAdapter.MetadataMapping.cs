using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private static WorkbookFunctionGroupsModel? ToWorkbookFunctionGroups(WorkbookFunctionGroupsDto? dto)
    {
        if (dto is null)
            return null;

        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        var builtInGroupCount = string.IsNullOrWhiteSpace(dto.BuiltInGroupCount) ? null : dto.BuiltInGroupCount;
        var groups = (dto.Groups ?? [])
            .Select(ToWorkbookFunctionGroup)
            .OfType<WorkbookFunctionGroupModel>()
            .ToList();
        if (builtInGroupCount is null && nativeAttributes.Count == 0 && groups.Count == 0)
            return null;

        return new WorkbookFunctionGroupsModel
        {
            BuiltInGroupCount = builtInGroupCount,
            NativeAttributes = nativeAttributes,
            Groups = groups
        };
    }

    private static WorkbookPropertiesModel? ToWorkbookProperties(WorkbookPropertiesDto? dto)
    {
        if (dto is null)
            return null;

        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        var nativeChildXmls = (dto.NativeChildXmls ?? [])
            .Where(xml => !string.IsNullOrWhiteSpace(xml))
            .ToList();
        if (nativeAttributes.Count == 0 && nativeChildXmls.Count == 0)
            return null;

        return new WorkbookPropertiesModel
        {
            NativeAttributes = nativeAttributes,
            NativeChildXmls = nativeChildXmls
        };
    }

    private static WorkbookProtectionMetadataModel? ToWorkbookProtectionMetadata(WorkbookProtectionMetadataDto? dto)
    {
        if (dto is null)
            return null;

        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        var nativeChildXmls = (dto.NativeChildXmls ?? [])
            .Where(xml => !string.IsNullOrWhiteSpace(xml))
            .ToList();
        if (nativeAttributes.Count == 0 && nativeChildXmls.Count == 0)
            return null;

        return new WorkbookProtectionMetadataModel
        {
            NativeAttributes = nativeAttributes,
            NativeChildXmls = nativeChildXmls
        };
    }

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

    private static WorkbookFunctionGroupModel? ToWorkbookFunctionGroup(WorkbookFunctionGroupDto? dto)
    {
        if (dto is null)
            return null;

        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        var name = string.IsNullOrWhiteSpace(dto.Name) ? null : dto.Name;
        if (name is null && nativeAttributes.Count == 0)
            return null;

        return new WorkbookFunctionGroupModel
        {
            Name = name,
            NativeAttributes = nativeAttributes
        };
    }

    private static WorkbookAdditionalViewsModel? ToWorkbookAdditionalViews(WorkbookAdditionalViewsDto? dto)
    {
        if (dto is null)
            return null;

        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        var views = (dto.Views ?? [])
            .Select(ToWorkbookAdditionalView)
            .OfType<WorkbookAdditionalViewModel>()
            .ToList();
        if (nativeAttributes.Count == 0 && views.Count == 0)
            return null;

        return new WorkbookAdditionalViewsModel
        {
            NativeAttributes = nativeAttributes,
            Views = views
        };
    }

    private static WorkbookAdditionalViewModel? ToWorkbookAdditionalView(WorkbookAdditionalViewDto? dto)
    {
        if (dto is null)
            return null;

        var nativeXml = string.IsNullOrWhiteSpace(dto.NativeXml) ? null : dto.NativeXml;
        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        if (nativeXml is null && nativeAttributes.Count == 0)
            return null;

        return new WorkbookAdditionalViewModel
        {
            NativeXml = nativeXml,
            NativeAttributes = nativeAttributes
        };
    }

    private static WorkbookFunctionGroupsDto? FromWorkbookFunctionGroups(WorkbookFunctionGroupsModel? model)
    {
        if (model is null)
            return null;

        var nativeAttributes = CleanNativeAttributesForSave(model.NativeAttributes);
        var builtInGroupCount = string.IsNullOrWhiteSpace(model.BuiltInGroupCount) ? null : model.BuiltInGroupCount;
        var groups = model.Groups
            .Select(FromWorkbookFunctionGroup)
            .OfType<WorkbookFunctionGroupDto>()
            .ToList();
        if (builtInGroupCount is null && nativeAttributes.Count == 0 && groups.Count == 0)
            return null;

        return new WorkbookFunctionGroupsDto
        {
            BuiltInGroupCount = builtInGroupCount,
            NativeAttributes = nativeAttributes,
            Groups = groups
        };
    }

    private static WorkbookFunctionGroupDto? FromWorkbookFunctionGroup(WorkbookFunctionGroupModel? model)
    {
        if (model is null)
            return null;

        var nativeAttributes = CleanNativeAttributesForSave(model.NativeAttributes);
        var name = string.IsNullOrWhiteSpace(model.Name) ? null : model.Name;
        if (name is null && nativeAttributes.Count == 0)
            return null;

        return new WorkbookFunctionGroupDto
        {
            Name = name,
            NativeAttributes = nativeAttributes
        };
    }

    private static WorkbookPropertiesDto? FromWorkbookProperties(WorkbookPropertiesModel? model)
    {
        if (model is null)
            return null;

        var nativeAttributes = CleanNativeAttributesForSave(model.NativeAttributes);
        var nativeChildXmls = model.NativeChildXmls
            .Where(xml => !string.IsNullOrWhiteSpace(xml))
            .ToList();
        if (nativeAttributes.Count == 0 && nativeChildXmls.Count == 0)
            return null;

        return new WorkbookPropertiesDto
        {
            NativeAttributes = nativeAttributes,
            NativeChildXmls = nativeChildXmls
        };
    }

    private static WorkbookProtectionMetadataDto? FromWorkbookProtectionMetadata(WorkbookProtectionMetadataModel? model)
    {
        if (model is null)
            return null;

        var nativeAttributes = CleanNativeAttributesForSave(model.NativeAttributes);
        var nativeChildXmls = model.NativeChildXmls
            .Where(xml => !string.IsNullOrWhiteSpace(xml))
            .ToList();
        if (nativeAttributes.Count == 0 && nativeChildXmls.Count == 0)
            return null;

        return new WorkbookProtectionMetadataDto
        {
            NativeAttributes = nativeAttributes,
            NativeChildXmls = nativeChildXmls
        };
    }

    private static WorksheetProtectionMetadataDto? FromWorksheetProtectionMetadata(WorksheetProtectionMetadataModel? model)
    {
        if (model is null)
            return null;

        var nativeAttributes = CleanNativeAttributesForSave(model.NativeAttributes);
        var nativeChildXmls = model.NativeChildXmls
            .Where(xml => !string.IsNullOrWhiteSpace(xml))
            .ToList();
        if (nativeAttributes.Count == 0 && nativeChildXmls.Count == 0)
            return null;

        return new WorksheetProtectionMetadataDto
        {
            NativeAttributes = nativeAttributes,
            NativeChildXmls = nativeChildXmls
        };
    }

    private static WorksheetPageSetupMetadataDto? FromWorksheetPageSetupMetadata(WorksheetPageSetupMetadataModel? model)
    {
        if (model is null)
            return null;

        var nativeAttributes = CleanNativeAttributesForSave(model.NativeAttributes);
        var nativeChildXmls = model.NativeChildXmls
            .Where(xml => !string.IsNullOrWhiteSpace(xml))
            .ToList();
        if (nativeAttributes.Count == 0 && nativeChildXmls.Count == 0)
            return null;

        return new WorksheetPageSetupMetadataDto
        {
            NativeAttributes = nativeAttributes,
            NativeChildXmls = nativeChildXmls
        };
    }

    private static WorksheetPrintOptionsMetadataDto? FromWorksheetPrintOptionsMetadata(WorksheetPrintOptionsMetadataModel? model)
    {
        if (model is null)
            return null;

        var nativeAttributes = CleanNativeAttributesForSave(model.NativeAttributes);
        var nativeChildXmls = model.NativeChildXmls
            .Where(xml => !string.IsNullOrWhiteSpace(xml))
            .ToList();
        if (nativeAttributes.Count == 0 && nativeChildXmls.Count == 0)
            return null;

        return new WorksheetPrintOptionsMetadataDto
        {
            NativeAttributes = nativeAttributes,
            NativeChildXmls = nativeChildXmls
        };
    }

    private static WorksheetSheetFormatMetadataDto? FromWorksheetSheetFormatMetadata(WorksheetSheetFormatMetadataModel? model)
    {
        if (model is null)
            return null;

        var nativeAttributes = CleanNativeAttributesForSave(model.NativeAttributes);
        var nativeChildXmls = model.NativeChildXmls
            .Where(xml => !string.IsNullOrWhiteSpace(xml))
            .ToList();
        if (nativeAttributes.Count == 0 && nativeChildXmls.Count == 0)
            return null;

        return new WorksheetSheetFormatMetadataDto
        {
            NativeAttributes = nativeAttributes,
            NativeChildXmls = nativeChildXmls
        };
    }

    private static WorksheetDimensionMetadataDto? FromWorksheetDimensionMetadata(WorksheetDimensionMetadataModel? model)
    {
        if (model is null)
            return null;

        var nativeAttributes = CleanNativeAttributesForSave(model.NativeAttributes);
        if (nativeAttributes.Count == 0)
            return null;

        return new WorksheetDimensionMetadataDto
        {
            NativeAttributes = nativeAttributes
        };
    }

    private static WorksheetSheetPropertiesMetadataDto? FromWorksheetSheetPropertiesMetadata(WorksheetSheetPropertiesMetadataModel? model)
    {
        if (model is null)
            return null;

        var nativeAttributes = CleanNativeAttributesForSave(model.NativeAttributes);
        var nativeChildXmls = model.NativeChildXmls
            .Where(xml => !string.IsNullOrWhiteSpace(xml))
            .ToList();
        if (nativeAttributes.Count == 0 && nativeChildXmls.Count == 0)
            return null;

        return new WorksheetSheetPropertiesMetadataDto
        {
            NativeAttributes = nativeAttributes,
            NativeChildXmls = nativeChildXmls
        };
    }

    private static WorksheetPrimaryViewMetadataDto? FromWorksheetPrimaryViewMetadata(WorksheetPrimaryViewMetadataModel? model)
    {
        if (model is null)
            return null;

        var nativeAttributes = CleanNativeAttributesForSave(model.NativeAttributes);
        var nativeChildXmls = model.NativeChildXmls
            .Where(xml => !string.IsNullOrWhiteSpace(xml))
            .ToList();
        if (nativeAttributes.Count == 0 && nativeChildXmls.Count == 0)
            return null;

        return new WorksheetPrimaryViewMetadataDto
        {
            NativeAttributes = nativeAttributes,
            NativeChildXmls = nativeChildXmls
        };
    }

    private static WorksheetPageBreaksMetadataDto? FromWorksheetPageBreaksMetadata(WorksheetPageBreaksMetadataModel? model)
    {
        if (model is null)
            return null;

        var nativeAttributes = CleanNativeAttributesForSave(model.NativeAttributes);
        var breakNativeAttributes = new Dictionary<uint, Dictionary<string, string>>();
        foreach (var pair in model.BreakNativeAttributes)
        {
            var attributes = CleanNativeAttributesForSave(pair.Value);
            if (attributes.Count > 0)
                breakNativeAttributes[pair.Key] = attributes;
        }

        if (nativeAttributes.Count == 0 && breakNativeAttributes.Count == 0)
            return null;

        return new WorksheetPageBreaksMetadataDto
        {
            NativeAttributes = nativeAttributes,
            BreakNativeAttributes = breakNativeAttributes
        };
    }

    private static WorksheetPageMarginsMetadataDto? FromWorksheetPageMarginsMetadata(WorksheetPageMarginsMetadataModel? model)
    {
        if (model is null)
            return null;

        var nativeAttributes = CleanNativeAttributesForSave(model.NativeAttributes);
        var nativeChildXmls = model.NativeChildXmls
            .Where(xml => !string.IsNullOrWhiteSpace(xml))
            .ToList();
        if (nativeAttributes.Count == 0 && nativeChildXmls.Count == 0)
            return null;

        return new WorksheetPageMarginsMetadataDto
        {
            NativeAttributes = nativeAttributes,
            NativeChildXmls = nativeChildXmls
        };
    }

    private static WorksheetHeaderFooterMetadataDto? FromWorksheetHeaderFooterMetadata(WorksheetHeaderFooterMetadataModel? model)
    {
        if (model is null)
            return null;

        var nativeAttributes = CleanNativeAttributesForSave(model.NativeAttributes);
        var nativeChildXmls = model.NativeChildXmls
            .Where(xml => !string.IsNullOrWhiteSpace(xml))
            .ToList();
        if (nativeAttributes.Count == 0 && nativeChildXmls.Count == 0)
            return null;

        return new WorksheetHeaderFooterMetadataDto
        {
            NativeAttributes = nativeAttributes,
            NativeChildXmls = nativeChildXmls
        };
    }

    private static WorkbookAdditionalViewsDto? FromWorkbookAdditionalViews(WorkbookAdditionalViewsModel? model)
    {
        if (model is null)
            return null;

        var nativeAttributes = CleanNativeAttributesForSave(model.NativeAttributes);
        var views = model.Views
            .Select(FromWorkbookAdditionalView)
            .OfType<WorkbookAdditionalViewDto>()
            .ToList();
        if (nativeAttributes.Count == 0 && views.Count == 0)
            return null;

        return new WorkbookAdditionalViewsDto
        {
            NativeAttributes = nativeAttributes,
            Views = views
        };
    }

    private static WorkbookAdditionalViewDto? FromWorkbookAdditionalView(WorkbookAdditionalViewModel? model)
    {
        if (model is null)
            return null;

        var nativeXml = string.IsNullOrWhiteSpace(model.NativeXml) ? null : model.NativeXml;
        var nativeAttributes = CleanNativeAttributesForSave(model.NativeAttributes);
        if (nativeXml is null && nativeAttributes.Count == 0)
            return null;

        return new WorkbookAdditionalViewDto
        {
            NativeXml = nativeXml,
            NativeAttributes = nativeAttributes
        };
    }
}
