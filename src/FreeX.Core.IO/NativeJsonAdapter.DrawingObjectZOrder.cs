using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private static List<DrawingObjectZOrderEntryDto> ToDrawingObjectZOrderDtos(Sheet sheet)
    {
        if (sheet.DrawingObjectZOrder.Count == 0)
            return [];

        return DrawingObjectZOrder.GetNormalizedOrder(sheet)
            .Select(entry => new DrawingObjectZOrderEntryDto
            {
                Kind = entry.Kind,
                Id = entry.Id
            })
            .ToList();
    }

    private static void LoadDrawingObjectZOrder(Sheet sheet, IEnumerable<DrawingObjectZOrderEntryDto?>? orderDtos)
    {
        if (orderDtos is null)
            return;

        foreach (var dto in orderDtos)
        {
            if (dto is null || dto.Id == Guid.Empty)
                continue;

            var entry = new DrawingObjectZOrderEntry(dto.Kind, dto.Id);
            if (DrawingObjectZOrder.IsSupportedKind(entry.Kind) &&
                DrawingObjectZOrder.ContainsObject(sheet, entry))
            {
                sheet.DrawingObjectZOrder.Add(entry);
            }
        }

        if (sheet.DrawingObjectZOrder.Count > 0)
            DrawingObjectZOrder.EnsureNormalizedOrder(sheet);
    }
}
