using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class NativeJsonAdapter
{
    // ── Save ─────────────────────────────────────────────────────────────────

    private static List<SlicerDto> ToSlicerDtos(Workbook workbook)
    {
        if (workbook.Slicers.Count == 0)
            return [];

        return workbook.Slicers
            .OfType<SlicerModel>()
            .Select(slicer => new SlicerDto
            {
                Name = slicer.Name,
                Caption = slicer.Caption,
                CacheName = slicer.CacheName,
                SourcePivotTableName = slicer.SourcePivotTableName,
                ConnectedPivotTableNames = slicer.ConnectedPivotTableNames.Count == 0
                    ? null
                    : slicer.ConnectedPivotTableNames.ToList(),
                SourceFieldName = slicer.SourceFieldName,
                StyleName = slicer.StyleName,
                SelectedItems = slicer.SelectedItems.ToList(),
                DrawingAnchor = ToDrawingAnchorRangeDto(slicer.DrawingAnchor),
                DrawingShapeName = slicer.DrawingShapeName,
                ColumnCount = slicer.ColumnCount,
                ShowCaption = slicer.ShowCaption,
                SourceSheetName = slicer.SourceSheetName,
                SourceTableId = slicer.SourceTableId,
                SourceTableColumnId = slicer.SourceTableColumnId,
                CacheItems = slicer.CacheItems.Count == 0
                    ? null
                    : slicer.CacheItems
                        .Select(item => new SlicerCacheItemDto { Index = item.Index, IsSelected = item.IsSelected })
                        .ToList(),
                SelectionCaptured = slicer.SelectionCaptured
            })
            .ToList();
    }

    private static List<TimelineDto> ToTimelineDtos(Workbook workbook)
    {
        if (workbook.Timelines.Count == 0)
            return [];

        return workbook.Timelines
            .OfType<TimelineModel>()
            .Select(timeline => new TimelineDto
            {
                Name = timeline.Name,
                Caption = timeline.Caption,
                CacheName = timeline.CacheName,
                SourcePivotTableName = timeline.SourcePivotTableName,
                ConnectedPivotTableNames = timeline.ConnectedPivotTableNames.Count == 0
                    ? null
                    : timeline.ConnectedPivotTableNames.ToList(),
                SourceFieldName = timeline.SourceFieldName,
                StyleName = timeline.StyleName,
                StartDate = timeline.StartDate,
                EndDate = timeline.EndDate,
                SelectedStartDate = timeline.SelectedStartDate,
                SelectedEndDate = timeline.SelectedEndDate,
                DrawingAnchor = ToDrawingAnchorRangeDto(timeline.DrawingAnchor),
                DrawingShapeName = timeline.DrawingShapeName,
                SourceSheetName = timeline.SourceSheetName,
                Level = timeline.Level,
                SelectionLevel = timeline.SelectionLevel,
                ScrollPosition = timeline.ScrollPosition
            })
            .ToList();
    }

    // ── Load ─────────────────────────────────────────────────────────────────

    private static void LoadSlicers(Workbook workbook, IReadOnlyList<SlicerDto>? slicerDtos)
    {
        foreach (var dto in slicerDtos ?? [])
        {
            if (dto is null || string.IsNullOrWhiteSpace(dto.Name))
                continue;

            var slicer = new SlicerModel
            {
                Name = dto.Name,
                Caption = dto.Caption,
                CacheName = dto.CacheName ?? "",
                SourcePivotTableName = dto.SourcePivotTableName,
                ConnectedPivotTableNames = (dto.ConnectedPivotTableNames ?? []).ToList(),
                SourceFieldName = dto.SourceFieldName,
                StyleName = dto.StyleName,
                DrawingAnchor = ToDrawingAnchorRange(dto.DrawingAnchor),
                DrawingShapeName = dto.DrawingShapeName,
                ColumnCount = dto.ColumnCount > 0 ? dto.ColumnCount : 1,
                ShowCaption = dto.ShowCaption,
                SourceSheetName = dto.SourceSheetName,
                SourceTableId = dto.SourceTableId,
                SourceTableColumnId = dto.SourceTableColumnId,
                CacheItems = (dto.CacheItems ?? [])
                    .Select(item => new SlicerCacheItem(item.Index, item.IsSelected))
                    .ToList(),
                SelectionCaptured = dto.SelectionCaptured
            };
            foreach (var item in dto.SelectedItems ?? [])
                slicer.SelectedItems.Add(item);

            workbook.Slicers.Add(slicer);
        }
    }

    private static void LoadTimelines(Workbook workbook, IReadOnlyList<TimelineDto>? timelineDtos)
    {
        foreach (var dto in timelineDtos ?? [])
        {
            if (dto is null || string.IsNullOrWhiteSpace(dto.Name))
                continue;

            workbook.Timelines.Add(new TimelineModel
            {
                Name = dto.Name,
                Caption = dto.Caption,
                CacheName = dto.CacheName ?? "",
                SourcePivotTableName = dto.SourcePivotTableName,
                ConnectedPivotTableNames = (dto.ConnectedPivotTableNames ?? []).ToList(),
                SourceFieldName = dto.SourceFieldName,
                StyleName = dto.StyleName,
                StartDate = dto.StartDate,
                EndDate = dto.EndDate,
                SelectedStartDate = dto.SelectedStartDate,
                SelectedEndDate = dto.SelectedEndDate,
                DrawingAnchor = ToDrawingAnchorRange(dto.DrawingAnchor),
                DrawingShapeName = dto.DrawingShapeName,
                SourceSheetName = dto.SourceSheetName,
                Level = dto.Level,
                SelectionLevel = dto.SelectionLevel,
                ScrollPosition = dto.ScrollPosition
            });
        }
    }
}
