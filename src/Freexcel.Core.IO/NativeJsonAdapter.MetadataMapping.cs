using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private static WorkbookFileSharingModel? ToWorkbookFileSharing(WorkbookFileSharingDto? dto)
    {
        if (dto is null)
            return null;

        var userName = string.IsNullOrWhiteSpace(dto.UserName) ? null : dto.UserName;
        var reservationPassword = string.IsNullOrWhiteSpace(dto.ReservationPassword) ? null : dto.ReservationPassword;
        if (dto.ReadOnlyRecommended is null &&
            userName is null &&
            reservationPassword is null)
        {
            return null;
        }

        return new WorkbookFileSharingModel
        {
            ReadOnlyRecommended = dto.ReadOnlyRecommended,
            UserName = userName,
            ReservationPassword = reservationPassword
        };
    }

    private static WorkbookFileVersionModel? ToWorkbookFileVersion(WorkbookFileVersionDto? dto)
    {
        if (dto is null)
            return null;

        var nativeAttributes = (dto.NativeAttributes ?? new Dictionary<string, string>())
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var appName = string.IsNullOrWhiteSpace(dto.AppName) ? null : dto.AppName;
        var lastEdited = string.IsNullOrWhiteSpace(dto.LastEdited) ? null : dto.LastEdited;
        var lowestEdited = string.IsNullOrWhiteSpace(dto.LowestEdited) ? null : dto.LowestEdited;
        var rupBuild = string.IsNullOrWhiteSpace(dto.RupBuild) ? null : dto.RupBuild;
        var codeName = string.IsNullOrWhiteSpace(dto.CodeName) ? null : dto.CodeName;
        if (appName is null &&
            lastEdited is null &&
            lowestEdited is null &&
            rupBuild is null &&
            codeName is null &&
            nativeAttributes.Count == 0)
        {
            return null;
        }

        return new WorkbookFileVersionModel
        {
            AppName = appName,
            LastEdited = lastEdited,
            LowestEdited = lowestEdited,
            RupBuild = rupBuild,
            CodeName = codeName,
            NativeAttributes = nativeAttributes
        };
    }

    private static WorkbookFileRecoveryPropertiesModel? ToWorkbookFileRecoveryProperties(WorkbookFileRecoveryPropertiesDto? dto)
    {
        if (dto is null)
            return null;

        var nativeAttributes = (dto.NativeAttributes ?? new Dictionary<string, string>())
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (dto.AutoRecover is null &&
            dto.CrashSave is null &&
            dto.DataExtractLoad is null &&
            dto.RepairLoad is null &&
            nativeAttributes.Count == 0)
        {
            return null;
        }

        return new WorkbookFileRecoveryPropertiesModel
        {
            AutoRecover = dto.AutoRecover,
            CrashSave = dto.CrashSave,
            DataExtractLoad = dto.DataExtractLoad,
            RepairLoad = dto.RepairLoad,
            NativeAttributes = nativeAttributes
        };
    }

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

    private static WorkbookFileSharingDto? FromWorkbookFileSharing(WorkbookFileSharingModel? model)
    {
        if (model is null)
            return null;

        var userName = string.IsNullOrWhiteSpace(model.UserName) ? null : model.UserName;
        var reservationPassword = string.IsNullOrWhiteSpace(model.ReservationPassword) ? null : model.ReservationPassword;
        if (model.ReadOnlyRecommended is null &&
            userName is null &&
            reservationPassword is null)
        {
            return null;
        }

        return new WorkbookFileSharingDto
        {
            ReadOnlyRecommended = model.ReadOnlyRecommended,
            UserName = userName,
            ReservationPassword = reservationPassword
        };
    }

    private static WorkbookFileVersionDto? FromWorkbookFileVersion(WorkbookFileVersionModel? model)
    {
        if (model is null)
            return null;

        var nativeAttributes = (model.NativeAttributes ?? new Dictionary<string, string>())
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var appName = string.IsNullOrWhiteSpace(model.AppName) ? null : model.AppName;
        var lastEdited = string.IsNullOrWhiteSpace(model.LastEdited) ? null : model.LastEdited;
        var lowestEdited = string.IsNullOrWhiteSpace(model.LowestEdited) ? null : model.LowestEdited;
        var rupBuild = string.IsNullOrWhiteSpace(model.RupBuild) ? null : model.RupBuild;
        var codeName = string.IsNullOrWhiteSpace(model.CodeName) ? null : model.CodeName;
        if (appName is null &&
            lastEdited is null &&
            lowestEdited is null &&
            rupBuild is null &&
            codeName is null &&
            nativeAttributes.Count == 0)
        {
            return null;
        }

        return new WorkbookFileVersionDto
        {
            AppName = appName,
            LastEdited = lastEdited,
            LowestEdited = lowestEdited,
            RupBuild = rupBuild,
            CodeName = codeName,
            NativeAttributes = nativeAttributes
        };
    }

    private static WorkbookFileRecoveryPropertiesDto? FromWorkbookFileRecoveryProperties(WorkbookFileRecoveryPropertiesModel? model)
    {
        if (model is null)
            return null;

        var nativeAttributes = (model.NativeAttributes ?? new Dictionary<string, string>())
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (model.AutoRecover is null &&
            model.CrashSave is null &&
            model.DataExtractLoad is null &&
            model.RepairLoad is null &&
            nativeAttributes.Count == 0)
        {
            return null;
        }

        return new WorkbookFileRecoveryPropertiesDto
        {
            AutoRecover = model.AutoRecover,
            CrashSave = model.CrashSave,
            DataExtractLoad = model.DataExtractLoad,
            RepairLoad = model.RepairLoad,
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
