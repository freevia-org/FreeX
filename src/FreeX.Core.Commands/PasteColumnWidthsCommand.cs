using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class PasteColumnWidthsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _sourceRange;
    private readonly uint _destinationStartCol;
    private readonly uint? _destinationColCount;
    private readonly IReadOnlyList<GridRange>? _sourceAreas;
    private Dictionary<uint, double>? _previousWidths;

    public string Label => "Paste Column Widths";

    public PasteColumnWidthsCommand(SheetId sheetId, GridRange sourceRange, uint destinationStartCol)
    {
        _sheetId = sheetId;
        _sourceRange = sourceRange;
        _destinationStartCol = destinationStartCol;
    }

    // R36-commands-paste-special-4-3: when the caller knows the full destination column
    // selection (not just its start column), this overload lets the paste tile the copied
    // column widths across every whole repeat of the source range's columns that fits the
    // selection -- mirroring how PasteCommandFactory.CreateInternalPasteCommand tiles
    // Values/Formulas/Formats/All onto a destination selection that is a whole multiple of the
    // copied range, instead of only ever filling the source range's own column footprint
    // anchored at the selection's start column.
    //
    // R78-commands-paste-special-5-1: `sourceAreas`, when supplied with more than one area,
    // records every individually Ctrl+clicked area of a multi-area source selection (mirroring
    // InternalClipboard.SourceAreas in MainWindow.ClipboardCommands.cs). `sourceRange` remains
    // only the BOUNDING BOX of those areas, so without this, every column in the gap between
    // disjoint areas (never part of the selection) was silently clobbered too.
    public PasteColumnWidthsCommand(
        SheetId sheetId,
        GridRange sourceRange,
        uint destinationStartCol,
        uint destinationColCount,
        IReadOnlyList<GridRange>? sourceAreas = null)
        : this(sheetId, sourceRange, destinationStartCol)
    {
        _destinationColCount = destinationColCount;
        _sourceAreas = sourceAreas is { Count: > 1 } ? sourceAreas : null;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_sourceRange.Start.Sheet != _sourceRange.End.Sheet)
            return new CommandOutcome(false, "Source range must be on one sheet.");

        var sourceSheet = ctx.GetSheet(_sourceRange.Start.Sheet);
        var targetSheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(targetSheet, SheetProtectionPermission.FormatColumns) is { } protectedOutcome)
            return protectedOutcome;

        var pasteColCount = _sourceRange.ColCount;
        var targetColCount = GetTargetColCount();
        var destinationEndCol = _destinationStartCol + targetColCount - 1;
        _previousWidths = CaptureDestinationWidths(targetSheet);

        for (uint tileOffset = 0; tileOffset + pasteColCount <= targetColCount; tileOffset += pasteColCount)
        {
            for (uint offset = 0; offset < pasteColCount; offset++)
            {
                var sourceCol = _sourceRange.Start.Col + offset;
                // R78-commands-paste-special-5-1: a multi-area (Ctrl+click) source's _sourceRange
                // is only the BOUNDING BOX of the actually-selected areas -- a column that falls in
                // the gap between disjoint areas was never part of the copy, so its destination
                // column must be left completely untouched rather than clobbered to the gap
                // column's (usually default/absent) width.
                if (_sourceAreas is { } areas && !areas.Any(area => sourceCol >= area.Start.Col && sourceCol <= area.End.Col))
                    continue;
                var destinationCol = _destinationStartCol + tileOffset + offset;
                if (sourceSheet.ColumnWidths.TryGetValue(sourceCol, out var width))
                    targetSheet.ColumnWidths[destinationCol] = width;
                else
                    targetSheet.ColumnWidths.Remove(destinationCol);
            }
        }

        return new CommandOutcome(true, IsNoOp: NothingChanged(targetSheet));
    }

    /// <summary>
    /// r260: the destination columns' widths, over exactly the range this command writes. Shared by
    /// the capture in Apply and the comparison in <see cref="NothingChanged"/> so the two cannot
    /// describe different ranges.
    /// </summary>
    private Dictionary<uint, double> CaptureDestinationWidths(Sheet sheet)
    {
        var widths = new Dictionary<uint, double>();
        var destinationEndCol = _destinationStartCol + GetTargetColCount() - 1;
        for (var col = _destinationStartCol; col <= destinationEndCol; col++)
        {
            if (sheet.ColumnWidths.TryGetValue(col, out var width))
                widths[col] = width;
        }
        return widths;
    }

    /// <summary>
    /// r260: pasting column widths onto columns that already have them writes what is already
    /// there -- pasting a copied block back over itself, or onto a column range formatted the same
    /// way. Without this the command still pushed an undo entry, and UndoRedoStack.Push clears the
    /// redo stack, destroying a real edit the user could have redone.
    ///
    /// <para>r221 grouped this with PasteDataValidation as needing "a before/after snapshot
    /// comparison, which is a change to how they work rather than a guard bolted on", because
    /// neither accumulates the affected list its eleven siblings do. That is true of the
    /// did-we-write-anything test, and this command turns out already to hold the BEFORE half:
    /// <c>_previousWidths</c> is exactly what Revert restores, so comparing it against the same
    /// range afterwards is the whole decision.</para>
    /// </summary>
    private bool NothingChanged(Sheet sheet)
    {
        if (_previousWidths is null)
            return true;

        var current = CaptureDestinationWidths(sheet);
        if (current.Count != _previousWidths.Count)
            return false;

        foreach (var (col, width) in _previousWidths)
        {
            if (!current.TryGetValue(col, out var currentWidth) || currentWidth != width)
                return false;
        }

        return true;
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousWidths is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        var destinationEndCol = _destinationStartCol + GetTargetColCount() - 1;
        for (var col = _destinationStartCol; col <= destinationEndCol; col++)
            sheet.ColumnWidths.Remove(col);
        foreach (var (col, width) in _previousWidths)
            sheet.ColumnWidths[col] = width;
    }

    // A destination column selection no wider than the copied source range pastes exactly one
    // (unclipped) copy of the source's own column footprint, anchored at the selection's start
    // column -- matching the original always-single-copy behavior when no destination selection
    // is known at all.
    private uint GetTargetColCount() =>
        _destinationColCount is { } destinationColCount && destinationColCount > _sourceRange.ColCount
            ? destinationColCount
            : _sourceRange.ColCount;
}
