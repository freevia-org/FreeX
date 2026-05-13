namespace Freexcel.Core.Model;

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
    /// Maps a pixel coordinate back to a cell address (for mouse clicks).
    /// </summary>
    CellAddress? HitTest(Workbook workbook, SheetId sheetId, double x, double y, double zoom);
}
