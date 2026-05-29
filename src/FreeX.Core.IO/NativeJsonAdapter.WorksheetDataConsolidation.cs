using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class NativeJsonAdapter
{
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
}
