namespace FreeX.Core.Model;

/// <summary>
/// Service that provides the UI with a slice of the workbook for rendering.
/// This is the primary bridge between the engine and the virtualized grid.
/// </summary>
public interface IViewportService
{
    /// <summary>
    /// Returns a model containing only the data needed to render the requested viewport.
    /// </summary>
    ViewportModel GetViewport(Workbook workbook, SheetId sheetId, ViewportRequest request);

    /// <summary>
    /// Computes the last visible row and row outline groups for the given request without
    /// materializing display cells. Used to determine the correct row-header width before
    /// building the full viewport, so the viewport is never built twice due to a
    /// width mis-estimate.
    /// </summary>
    (uint LastVisibleRow, IReadOnlyList<OutlineGroupRange> RowOutlineGroups)
        ComputeRowMetricsSummary(Workbook workbook, SheetId sheetId, ViewportRequest request);

    /// <summary>
    /// Maps a pixel coordinate back to a cell address (for mouse clicks).
    /// </summary>
    CellAddress? HitTest(Workbook workbook, SheetId sheetId, double x, double y, double zoom);
}
