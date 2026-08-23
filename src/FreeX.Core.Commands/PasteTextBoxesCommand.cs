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
        foreach (var (textBox, destinationAnchor) in PastePlacementPolicy.EnumerateMappedItems(
                     _sourceTextBoxes,
                     static textBox => textBox.Anchor,
                     _sourceRange,
                     _destination,
                     _destinationRange,
                     _transpose))
        {
            var clone = DuplicateSheetDrawingCloner.CloneTextBox(textBox, _sheetId);
            clone.Anchor = destinationAnchor;
            targetSheet.TextBoxes.Add(clone);
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
        foreach (var textBox in _added)
            sheet.TextBoxes.Remove(textBox);
        _added = null;
    }

}
