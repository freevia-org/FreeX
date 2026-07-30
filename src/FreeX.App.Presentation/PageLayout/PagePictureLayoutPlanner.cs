using FreeX.App.Presentation.DrawingInteraction;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

/// <summary>
/// Resolves worksheet pictures (Insert &gt; Pictures, or a raster Paste Special &gt; Picture snapshot)
/// into portable page-space blocks so print, Print Preview, and PDF export can paint them -- mirrors
/// <see cref="PageTextBoxLayoutPlanner"/>'s cell-anchored placement (anchor cell + page inclusion via
/// the same row/column position lookup) and its established print-model scope: sub-cell anchor
/// offsets (<c>PictureModel.AnchorOffsetX/Y</c>) and picture rotation are not applied here, only
/// position/size/crop (see <c>PageTextBoxLayoutPlannerTests.Build_PreservesInputOrderAndPrintParityIgnoresOffsetsAndRotation</c>
/// for the sibling contract this follows).
///
/// Only <see cref="PictureKind.Image"/> pictures with decoded raster bytes are resolved --
/// a <see cref="PictureKind.CellRangeSnapshot"/> picture (the default kind produced by a non-linked
/// Paste Special &gt; Picture of a cell range) has no raster bytes to draw; it instead stores a grid
/// of per-cell style/text snapshots (<c>PictureModel.Cells</c>) that only the interactive grid's own
/// cell-drawing pipeline (<c>GridView.RenderPicture</c>'s fallback branch) currently knows how to
/// paint. Reproducing that mini-grid render here is out of scope for this pass
/// (R92-consumer-wiring-sweep-1) and is called out explicitly rather than silently dropped.
/// </summary>
public static class PagePictureLayoutPlanner
{
    public static IReadOnlyList<PagePictureBlock> Build(
        IReadOnlyList<PictureModel> pictures,
        IReadOnlyList<uint> pageRows,
        IReadOnlyList<uint> pageColumns,
        double gridLeft,
        double gridTop,
        PrintGridMeasurement measurement)
    {
        ArgumentNullException.ThrowIfNull(pictures);
        ArgumentNullException.ThrowIfNull(pageRows);
        ArgumentNullException.ThrowIfNull(pageColumns);
        ArgumentNullException.ThrowIfNull(measurement);

        if (pictures.Count == 0 || pageRows.Count == 0 || pageColumns.Count == 0)
            return [];

        var rowIndexes = BuildPositionLookup(pageRows);
        var columnIndexes = BuildPositionLookup(pageColumns);
        var blocks = new List<PagePictureBlock>();
        foreach (var picture in pictures)
        {
            if (!picture.IsVisible ||
                picture.Kind != PictureKind.Image ||
                picture.ImageBytes is not { Length: > 0 } imageBytes ||
                string.IsNullOrEmpty(picture.ContentType) ||
                !rowIndexes.TryGetValue(picture.Anchor.Row, out var rowIndex) ||
                !columnIndexes.TryGetValue(picture.Anchor.Col, out var columnIndex))
            {
                continue;
            }

            var bounds = new LayoutRect(
                gridLeft + measurement.ColumnOffset(columnIndex),
                gridTop + measurement.RowOffset(rowIndex),
                Math.Max(1, picture.Width),
                Math.Max(1, picture.Height));

            blocks.Add(new PagePictureBlock(
                picture.Id,
                bounds,
                new PictureCropRatios(picture.CropLeft, picture.CropTop, picture.CropRight, picture.CropBottom),
                imageBytes,
                picture.ContentType));
        }

        return blocks;
    }

    private static IReadOnlyDictionary<uint, int> BuildPositionLookup(IReadOnlyList<uint> indexes)
    {
        var lookup = new Dictionary<uint, int>(indexes.Count);
        for (var i = 0; i < indexes.Count; i++)
            lookup[indexes[i]] = i;

        return lookup;
    }
}
