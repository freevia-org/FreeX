using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// R92-cmd-paste-floating-objects: carries a floating DrawingShape (rectangle/arrow/connector/
/// WordArt/etc) along with a plain Ctrl+V paste when the shape's anchor cell lies inside the copied
/// range -- generalizing <see cref="PastePicturesCommand"/>'s picture-only carry to the other
/// anchored-object kinds real Excel also duplicates when a copied cell range covers them. Mirrors
/// <see cref="PastePicturesCommand"/>'s tiling/anchor-mapping shape exactly (DrawingShapeModel has
/// the same cell-anchored <c>Anchor</c>/<c>AnchorOffsetX</c>/<c>AnchorOffsetY</c> shape as
/// PictureModel), but reuses <see cref="DuplicateSheetDrawingCloner.CloneDrawingShape"/> for the
/// ~30-property clone (already bumped internal for exactly this kind of reuse) instead of
/// hand-rolling a second property list, then overrides only the clone's Anchor to the mapped
/// destination.
/// </summary>
public sealed class PasteShapesCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _sourceRange;
    private readonly CellAddress _destination;
    private readonly GridRange? _destinationRange;
    private readonly bool _transpose;
    private readonly IReadOnlyList<DrawingShapeModel> _sourceShapes;
    private List<DrawingShapeModel>? _added;

    public string Label => "Paste Shapes";

    public PasteShapesCommand(
        SheetId sheetId,
        GridRange sourceRange,
        CellAddress destination,
        IReadOnlyList<DrawingShapeModel> sourceShapes,
        bool transpose)
    {
        _sheetId = sheetId;
        _sourceRange = sourceRange;
        _destination = destination;
        _sourceShapes = sourceShapes;
        _transpose = transpose;
    }

    /// <summary>
    /// Tiling counterpart, mirroring <see cref="PastePicturesCommand"/>'s destination-range overload.
    /// </summary>
    public PasteShapesCommand(
        SheetId sheetId,
        GridRange sourceRange,
        GridRange destinationRange,
        IReadOnlyList<DrawingShapeModel> sourceShapes,
        bool transpose)
        : this(sheetId, sourceRange, destinationRange.Start, sourceShapes, transpose)
    {
        _destinationRange = destinationRange;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var targetSheet = ctx.GetSheet(_sheetId);
        if (DrawingShapeCommandGuards.RejectIfEditObjectsBlocked(targetSheet) is { } protectedOutcome)
            return protectedOutcome;

        _added = [];
        var affected = new List<CellAddress>();
        foreach (var tileAnchor in EnumerateTileAnchors())
        {
            foreach (var shape in _sourceShapes)
            {
                var destinationAnchor = MapDestination(shape.Anchor, _sourceRange, tileAnchor, _transpose);
                var clone = DuplicateSheetDrawingCloner.CloneDrawingShape(shape, _sheetId);
                clone.Anchor = destinationAnchor;
                targetSheet.DrawingShapes.Add(clone);
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
        foreach (var shape in _added)
            sheet.DrawingShapes.Remove(shape);
        _added = null;
    }

    // Mirrors PastePicturesCommand.EnumerateTileAnchors.
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
}
