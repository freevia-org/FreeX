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
        foreach (var (picture, destinationAnchor) in PastePlacementPolicy.EnumerateMappedItems(
                     _sourcePictures,
                     static picture => picture.Anchor,
                     _sourceRange,
                     _destination,
                     _destinationRange,
                     _transpose))
        {
            var clone = DuplicateSheetDrawingCloner.ClonePictureForPaste(picture, destinationAnchor);
            targetSheet.Pictures.Add(clone);
            _added.Add(clone);
            affected.Add(destinationAnchor);
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

}
