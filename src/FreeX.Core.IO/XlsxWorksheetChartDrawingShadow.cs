namespace FreeX.Core.IO;

// drawing-zorder-share-part: XlsxWorksheetChartWriter and XlsxWorksheetDrawingObjectWriter each
// rebuild the SAME worksheet drawing part independently in SavePostProcessing (chart writer runs
// first, drawing-object writer runs second), so whichever writer runs second silently discards
// whatever the first one just wrote -- see the "delete then rewrite the whole part" pattern in both
// writers' WriteWorksheetCharts/WriteWorksheetDrawingObjects. That is squarely a bug in how those two
// writers interact, but only XlsxWorksheetChartWriter and XlsxWorksheetDrawingPartMerger are in scope
// here (XlsxWorksheetDrawingObjectWriter and the SavePostProcessing call order are owned elsewhere).
//
// Bounded fix: whenever the chart writer is about to write into a drawing part the workbook's source
// package already owns for this sheet -- the exact case where XlsxWorksheetDrawingObjectWriter is
// guaranteed to reuse (and overwrite) that same part right afterwards, see the call-site comment in
// XlsxWorksheetChartWriter.Save -- it also stashes a throwaway copy of the anchors it just wrote at
// GetShadowPath(drawingPath). XlsxWorksheetDrawingPartMerger picks the shadow back up once the
// drawing-object writer and the source-package preservation pass have both run, merges the stashed
// chart anchors into the final drawing part, and deletes the shadow -- so the sheet ends up with a
// single drawing part holding both the chart and the drawing-object anchors instead of losing one.
//
// Residual gap (needs changes outside this file set to close): a sheet with no prior source drawing
// part -- including every sheet of a brand-new, never-saved workbook -- never reaches the "reused own
// source drawing path" branch, so no shadow is written; the two writers there still allocate/point at
// different drawing{N}.xml parts and the drawing-object writer's worksheet-drawing-reference rewrite
// still silently orphans the chart writer's part. Closing that requires either writer to become aware
// of the other's allocation within the same save pass, which lives in
// XlsxWorksheetDrawingObjectWriter.cs / XlsxFileAdapter.SavePostProcessing.cs.
internal static class XlsxWorksheetChartDrawingShadow
{
    private const string ShadowMarker = ".freexChartShadow";

    /// <summary>
    /// Maps a real drawing part path (e.g. "xl/drawings/drawing1.xml") to a private, throwaway
    /// sibling part path (e.g. "xl/drawings/drawing1.freexChartShadow.xml") used purely as a same-save
    /// scratch slot. The marker can never collide with a genuine drawing{N}.xml allocation, and the
    /// shadow part is always deleted again before the package is handed back to the caller.
    /// </summary>
    public static string GetShadowPath(string drawingPath)
    {
        var slash = drawingPath.LastIndexOf('/');
        var directory = slash < 0 ? string.Empty : drawingPath[..(slash + 1)];
        var fileName = slash < 0 ? drawingPath : drawingPath[(slash + 1)..];
        var dot = fileName.LastIndexOf('.');
        return dot < 0
            ? $"{directory}{fileName}{ShadowMarker}"
            : $"{directory}{fileName[..dot]}{ShadowMarker}{fileName[dot..]}";
    }
}
