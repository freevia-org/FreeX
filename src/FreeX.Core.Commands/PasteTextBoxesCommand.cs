using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// R92-cmd-paste-floating-objects: TextBox analogue of <see cref="PastePicturesCommand"/> -- carries
/// a floating text box along with a plain Ctrl+V paste when the text box's anchor cell lies inside
/// the copied range. See <see cref="PasteShapesCommand"/> (the DrawingShape sibling added alongside
/// this) for the shared rationale; reuses <see cref="DuplicateSheetDrawingCloner.CloneTextBox"/> for
/// the property clone and then overrides only the clone's Anchor to the mapped destination.
/// </summary>
public sealed class PasteTextBoxesCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _sourceRange;
    private readonly CellAddress _destination;
    private readonly GridRange? _destinationRange;
    private readonly bool _transpose;
    private readonly IReadOnlyList<TextBoxModel> _sourceTextBoxes;
    private List<TextBoxModel>? _added;

    public string Label => "Paste Text Boxes";

    public PasteTextBoxesCommand(
        SheetId sheetId,
        GridRange sourceRange,
        CellAddress destination,
        IReadOnlyList<TextBoxModel> sourceTextBoxes,
        bool transpose)
    {
        _sheetId = sheetId;
        _sourceRange = sourceRange;
        _destination = destination;
        _sourceTextBoxes = sourceTextBoxes;
        _transpose = transpose;
    }

    /// <summary>
    /// Tiling counterpart, mirroring <see cref="PastePicturesCommand"/>'s destination-range overload.
    /// </summary>
    public PasteTextBoxesCommand(
        SheetId sheetId,
        GridRange sourceRange,
        GridRange destinationRange,
        IReadOnlyList<TextBoxModel> sourceTextBoxes,
        bool transpose)
        : this(sheetId, sourceRange, destinationRange.Start, sourceTextBoxes, transpose)
    {
        _destinationRange = destinationRange;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var targetSheet = ctx.GetSheet(_sheetId);
        if (TextBoxCommandGuards.RejectIfEditObjectsBlocked(targetSheet) is { } protectedOutcome)
            return protectedOutcome;

        _added = [];
        var affected = new List<CellAddress>();
        foreach (var tileAnchor in EnumerateTileAnchors())
        {
            foreach (var textBox in _sourceTextBoxes)
            {
                var destinationAnchor = MapDestination(textBox.Anchor, _sourceRange, tileAnchor, _transpose);
                var clone = DuplicateSheetDrawingCloner.CloneTextBox(textBox, _sheetId);
                clone.Anchor = destinationAnchor;
                targetSheet.TextBoxes.Add(clone);
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
        foreach (var textBox in _added)
            sheet.TextBoxes.Remove(textBox);
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
