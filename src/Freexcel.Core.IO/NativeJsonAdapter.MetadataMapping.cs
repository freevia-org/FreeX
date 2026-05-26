using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed partial class NativeJsonAdapter
{
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

}
