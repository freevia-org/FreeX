using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private static WorksheetCustomPropertyMetadataModel? ToWorksheetCustomPropertyMetadata(WorksheetCustomPropertyMetadataDto? dto)
    {
        if (dto is null)
            return null;

        var nativeAttributes = CleanNativeAttributes(dto.NativeAttributes);
        var nativeChildXmls = (dto.NativeChildXmls ?? [])
            .Where(xml => !string.IsNullOrWhiteSpace(xml))
            .ToList();
        if (nativeAttributes.Count == 0 && nativeChildXmls.Count == 0)
            return null;

        return new WorksheetCustomPropertyMetadataModel
        {
            NativeAttributes = nativeAttributes,
            NativeChildXmls = nativeChildXmls
        };
    }

    private static WorksheetCustomPropertyMetadataDto? FromWorksheetCustomPropertyMetadata(WorksheetCustomPropertyMetadataModel? model)
    {
        if (model is null)
            return null;

        var nativeAttributes = CleanNativeAttributesForSave(model.NativeAttributes);
        var nativeChildXmls = (model.NativeChildXmls ?? [])
            .Where(xml => !string.IsNullOrWhiteSpace(xml))
            .ToList();
        if (nativeAttributes.Count == 0 && nativeChildXmls.Count == 0)
            return null;

        return new WorksheetCustomPropertyMetadataDto
        {
            NativeAttributes = nativeAttributes,
            NativeChildXmls = nativeChildXmls
        };
    }
}
