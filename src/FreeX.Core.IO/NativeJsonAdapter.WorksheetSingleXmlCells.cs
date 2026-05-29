using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class NativeJsonAdapter
{
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
}
