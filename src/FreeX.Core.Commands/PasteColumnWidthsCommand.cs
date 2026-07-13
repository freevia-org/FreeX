using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class PasteColumnWidthsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _sourceRange;
    private readonly uint _destinationStartCol;
    private readonly uint? _destinationColCount;
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
    public PasteColumnWidthsCommand(SheetId sheetId, GridRange sourceRange, uint destinationStartCol, uint destinationColCount)
        : this(sheetId, sourceRange, destinationStartCol)
    {
        _destinationColCount = destinationColCount;
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
        _previousWidths = new Dictionary<uint, double>();
        for (var col = _destinationStartCol; col <= destinationEndCol; col++)
        {
            if (targetSheet.ColumnWidths.TryGetValue(col, out var width))
                _previousWidths[col] = width;
        }

        for (uint tileOffset = 0; tileOffset + pasteColCount <= targetColCount; tileOffset += pasteColCount)
        {
            for (uint offset = 0; offset < pasteColCount; offset++)
            {
                var sourceCol = _sourceRange.Start.Col + offset;
                var destinationCol = _destinationStartCol + tileOffset + offset;
                if (sourceSheet.ColumnWidths.TryGetValue(sourceCol, out var width))
                    targetSheet.ColumnWidths[destinationCol] = width;
                else
                    targetSheet.ColumnWidths.Remove(destinationCol);
            }
        }

        return new CommandOutcome(true);
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
