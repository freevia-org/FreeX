using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// R91-io-clipboard-image-formats-5-2: carries a floating picture (photo/clip art/camera-linked
/// snapshot) along with a plain Ctrl+V paste when the picture's anchor cell lies inside the
/// copied range -- matching real Excel, which duplicates a picture anchored inside the selection
/// at the paste destination exactly like it duplicates the cell values/formats themselves.
/// Mirrors <see cref="PasteCommentsCommand"/>'s tiling/anchor-mapping shape, but ADDS a fresh
/// clone (new <see cref="PictureModel.Id"/>) rather than overwriting an address-keyed dictionary
/// entry -- a sheet can host multiple pictures anchored at/near the same cell, so there is no
/// destination "slot" to clear/overwrite the way a legacy note's single per-cell dictionary entry
/// has.
/// </summary>
public sealed class PastePicturesCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _sourceRange;
    private readonly CellAddress _destination;
    private readonly GridRange? _destinationRange;
    private readonly bool _transpose;
    private readonly IReadOnlyList<PictureModel> _sourcePictures;
    private List<PictureModel>? _added;

    public string Label => "Paste Pictures";

    public PastePicturesCommand(
        SheetId sheetId,
        GridRange sourceRange,
        CellAddress destination,
        IReadOnlyList<PictureModel> sourcePictures,
        bool transpose)
    {
        _sheetId = sheetId;
        _sourceRange = sourceRange;
        _destination = destination;
        _sourcePictures = sourcePictures;
        _transpose = transpose;
    }

    /// <summary>
    /// Tiling counterpart, mirroring <see cref="PasteCommentsCommand"/>'s destination-range
    /// overload: when the caller knows the full destination selection (not just its top-left
    /// anchor) and that selection is a whole multiple of the copied source range, the carried
    /// picture(s) are re-created once per repeated tile, exactly like values/formats/comments are.
    /// </summary>
    public PastePicturesCommand(
        SheetId sheetId,
        GridRange sourceRange,
        GridRange destinationRange,
        IReadOnlyList<PictureModel> sourcePictures,
        bool transpose)
        : this(sheetId, sourceRange, destinationRange.Start, sourcePictures, transpose)
    {
        _destinationRange = destinationRange;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var targetSheet = ctx.GetSheet(_sheetId);
        if (PictureCommandGuards.RejectIfEditObjectsBlocked(targetSheet) is { } protectedOutcome)
            return protectedOutcome;

        _added = [];
        var affected = new List<CellAddress>();
        foreach (var tileAnchor in EnumerateTileAnchors())
        {
            foreach (var picture in _sourcePictures)
            {
                var destinationAnchor = MapDestination(picture.Anchor, _sourceRange, tileAnchor, _transpose);
                var clone = ClonePictureAtAnchor(picture, destinationAnchor);
                targetSheet.Pictures.Add(clone);
                _added.Add(clone);
                affected.Add(destinationAnchor);
            }
        }

        return new CommandOutcome(true, AffectedCells: affected.Distinct().ToList());
    }

    public void Revert(ICommandContext ctx)
    {
        if (_added is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        foreach (var picture in _added)
            sheet.Pictures.Remove(picture);
        _added = null;
    }

    // Mirrors PasteCommentsCommand.EnumerateTileAnchors: a single anchor when no destination
    // range was supplied (or it is no larger than the copied source range), otherwise one anchor
    // per whole repeated tile of the source range that fits the destination selection. A trailing
    // partial tile is left untouched, matching every other tiled-paste content kind.
    private IEnumerable<CellAddress> EnumerateTileAnchors()
    {
        if (_destinationRange is not { } destinationRange)
        {
            yield return _destination;
            yield break;
        }

        var pasteRows = _transpose ? _sourceRange.ColCount : _sourceRange.RowCount;
        var pasteCols = _transpose ? _sourceRange.RowCount : _sourceRange.ColCount;
        var targetRows = destinationRange.RowCount;
        var targetCols = destinationRange.ColCount;

        if (targetRows <= pasteRows && targetCols <= pasteCols)
        {
            yield return destinationRange.Start;
            yield break;
        }

        for (var rowOffset = 0U; rowOffset + pasteRows <= targetRows; rowOffset += pasteRows)
        {
            for (var colOffset = 0U; colOffset + pasteCols <= targetCols; colOffset += pasteCols)
            {
                yield return new CellAddress(
                    destinationRange.Start.Sheet,
                    destinationRange.Start.Row + rowOffset,
                    destinationRange.Start.Col + colOffset);
            }
        }
    }

    private static CellAddress MapDestination(
        CellAddress source,
        GridRange sourceRange,
        CellAddress destination,
        bool transpose)
    {
        var rowOffset = source.Row - sourceRange.Start.Row;
        var colOffset = source.Col - sourceRange.Start.Col;
        return transpose
            ? new CellAddress(destination.Sheet, destination.Row + colOffset, destination.Col + rowOffset)
            : new CellAddress(destination.Sheet, destination.Row + rowOffset, destination.Col + colOffset);
    }

    /// <summary>
    /// Builds the pasted copy of <paramref name="picture"/> anchored at <paramref name="destinationAnchor"/>.
    /// Mirrors <c>DuplicateSheetDrawingCloner.ClonePicture</c>'s field-for-field copy (new Id, own
    /// defensive-copied byte buffers, <c>IsSourceLoaded = false</c> so the pasted copy round-trips
    /// through the normal authored-picture writer instead of being silently dropped for lacking a
    /// mapped source drawing part). <see cref="PictureModel.LinkedSourceRange"/> is intentionally
    /// left unshifted: a camera/"linked picture" copy keeps referencing the same source data range
    /// it always did, exactly like Excel's own Copy/Paste of a linked picture.
    /// </summary>
    private static PictureModel ClonePictureAtAnchor(PictureModel picture, CellAddress destinationAnchor)
    {
        var clone = new PictureModel
        {
            Name = picture.Name,
            Anchor = destinationAnchor,
            AnchorOffsetX = picture.AnchorOffsetX,
            AnchorOffsetY = picture.AnchorOffsetY,
            Kind = picture.Kind,
            SourceRowCount = picture.SourceRowCount,
            SourceColumnCount = picture.SourceColumnCount,
            IsLinkedToSourceRange = picture.IsLinkedToSourceRange,
            LinkedSourceRange = picture.LinkedSourceRange,
            LinkedSourceSheetName = picture.LinkedSourceSheetName,
            ImageBytes = picture.ImageBytes?.ToArray(),
            ContentType = picture.ContentType,
            SvgImageBytes = picture.SvgImageBytes?.ToArray(),
            LinkedImageTarget = picture.LinkedImageTarget,
            Title = picture.Title,
            AltText = picture.AltText,
            IsDecorative = picture.IsDecorative,
            Width = picture.Width,
            Height = picture.Height,
            LockAspectRatio = picture.LockAspectRatio,
            RotationDegrees = picture.RotationDegrees,
            FlipHorizontal = picture.FlipHorizontal,
            FlipVertical = picture.FlipVertical,
            IsVisible = picture.IsVisible,
            // R127C-clone-editas-parity: mirrors DuplicateSheetDrawingCloner.ClonePicture's
            // DrawingAnchorKind copy -- without this, a oneCellAnchor ("move but don't size") or
            // absoluteAnchor ("don't move or size") picture carried along in a cell-range
            // copy/paste (normal paste, paste-special picture carry-over, or tiled/multi-paste)
            // silently reverted to the PictureModel default of TwoCell, so the pasted copy would
            // then wrongly move AND resize on a later row/column insert or delete.
            DrawingAnchorKind = picture.DrawingAnchorKind,
            // R150-model-drawing-object-lock-paste-1-1: mirrors DuplicateSheetDrawingCloner.ClonePicture's
            // Locked copy (R111-model-drawing-object-lock-1-1 precedent) -- without this, an
            // explicitly-unlocked picture (Format Picture > Properties > Locked unchecked) silently
            // reverted to the PictureModel default of Locked = true on every copy/paste that routes
            // through this command, re-locking the pasted copy against move/resize under sheet
            // protection even though the source picture stayed unlocked.
            Locked = picture.Locked,
            CropLeft = picture.CropLeft,
            CropTop = picture.CropTop,
            CropRight = picture.CropRight,
            CropBottom = picture.CropBottom,
            // R97-model-drawing-hyperlink-2-2: carry the object-level hyperlink into the pasted copy
            // -- mirrors DuplicateSheetDrawingCloner.ClonePicture's identical Hyperlink copy.
            Hyperlink = picture.Hyperlink,
            IsSourceLoaded = false
        };

        foreach (var cell in picture.Cells)
            clone.Cells.Add(cell);

        return clone;
    }
}
