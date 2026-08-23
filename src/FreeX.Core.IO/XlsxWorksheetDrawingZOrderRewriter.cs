using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// R118-io-drawing-zorder-1 fix: <c>BringDrawingShapeForwardCommand</c>/<c>SendDrawingShapeBackwardCommand</c>
/// (<c>DrawingShapeZOrderCommands.cs</c>) and <c>MoveSelectionPaneObjectCommand</c>
/// (<c>SelectionPaneCommands.cs</c>) only ever mutate <see cref="Sheet.DrawingObjectZOrder"/> -- they never
/// clear a moved object's <c>IsSourceLoaded</c>, the ordinary (unedited) state for most objects reloaded
/// from an .xlsx. <see cref="XlsxWorksheetDrawingObjectWriter"/> is gated per-sheet on having at least one
/// object it needs to REGENERATE (<c>!IsSourceLoaded</c> and otherwise supported) -- when a sheet has
/// nothing to regenerate (every object on it is still source-loaded, the exact scenario an ordinary
/// reorder-only edit produces), that writer skips the sheet entirely and its ENTIRE drawing part is instead
/// carried forward untouched by the generic unknown-part passthrough
/// (<see cref="XlsxPackageMetadataMerger.CopyUnknownPackageParts"/>) plus
/// <see cref="XlsxWorksheetDrawingReferencePreserver"/> restoring the worksheet's <c>&lt;xdr:drawing&gt;</c>
/// reference -- neither of which knows anything about <see cref="Sheet.DrawingObjectZOrder"/>, so the
/// reorder is silently discarded. <see cref="XlsxWorksheetDrawingPartMerger"/>'s own reorder pass (also part
/// of this fix) only covers the OTHER case, where at least one object on the sheet WAS regenerated and the
/// merge path actually runs.
/// <para>
/// This rewriter is the catch-all: it runs LAST, after every other drawing-part mechanism has settled each
/// sheet's drawing part at its final path (whether via the merge path or the verbatim passthrough), and
/// simply re-applies <see cref="XlsxWorksheetDrawingPartMerger.ReorderAnchorsByZOrder"/> to whatever drawing
/// part the sheet ended up with. It is a deliberate no-op (see that method) whenever the anchors already
/// match the sheet's z-order -- including the ordinary case of a sheet that was never reordered at all.
/// </para>
/// </summary>
internal static class XlsxWorksheetDrawingZOrderRewriter
{
    private static readonly XNamespace SpreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

    /// <summary>True when any sheet has an explicit z-order recorded at all -- cheap gate for
    /// <see cref="XlsxFileAdapter"/>'s feature plan. A sheet whose DrawingObjectZOrder is empty was never
    /// reordered through a z-order command, so this rewriter has nothing to do for it.</summary>
    public static bool HasExplicitDrawingZOrder(Sheet sheet) => sheet.DrawingObjectZOrder.Count > 0;

    public static void Save(Stream packageStream, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);

        foreach (var sheet in workbook.Sheets)
        {
            if (!HasExplicitDrawingZOrder(sheet))
                continue;

            var worksheetPath = worksheetPathMap?.SheetPathsByName.GetValueOrDefault(sheet.Name);
            if (string.IsNullOrWhiteSpace(worksheetPath))
                continue;

            var drawingPath = XlsxWorksheetDrawingPartMerger.GetWorksheetDrawingPath(
                archive,
                worksheetPath,
                XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main"),
                XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships"),
                OpcRelationships.Namespace);
            if (string.IsNullOrWhiteSpace(drawingPath))
                continue;

            var drawingEntry = archive.GetEntry(drawingPath);
            if (drawingEntry is null)
                continue;

            var drawingXml = XlsxPackageXmlEditor.LoadXml(drawingEntry);
            if (drawingXml.Root is null)
                continue;

            if (XlsxWorksheetDrawingPartMerger.ReorderAnchorsByZOrder(drawingXml.Root, sheet, SpreadsheetDrawingNs))
                XlsxPackageXmlEditor.ReplaceXml(archive, drawingPath, drawingXml);
        }
    }

}
