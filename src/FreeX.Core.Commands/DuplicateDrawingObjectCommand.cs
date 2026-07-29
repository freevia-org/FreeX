using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Duplicates a single selected chart or shape (GridView.SelectedObjectKind/SelectedObjectId) onto a
/// destination sheet -- the command backing Ctrl+C/Ctrl+V on a selected chart/shape
/// (R91-io-clipboard-image-formats-5-1). Real Excel's Ctrl+C/Ctrl+V on a selected object duplicates
/// it (same sheet, or a different sheet if the destination selection moved there first); before this
/// command existed, FreeX's clipboard commands only ever knew about cell RANGES, so Ctrl+C on a
/// selected chart/shape silently copied whatever cell happened to be under its anchor instead.
/// Reuses <see cref="DuplicateSheetDrawingCloner"/>'s per-object clone helpers (the exact ones
/// Duplicate Sheet already uses) so a pasted chart/shape carries every property, instead of
/// hand-rolling a second partial property list here.
/// </summary>
public sealed class DuplicateDrawingObjectCommand : IWorkbookCommand
{
    private readonly SheetId _sourceSheetId;
    private readonly SheetId _destinationSheetId;
    private readonly SelectionPaneObjectKind _kind;
    private readonly Guid _sourceObjectId;
    private readonly double _offsetX;
    private readonly double _offsetY;

    private ChartModel? _clonedChart;
    private DrawingShapeModel? _clonedShape;
    private bool _added;

    public string Label => "Paste";

    /// <summary>The Id of the newly-created duplicate, once <see cref="Apply"/> has succeeded.</summary>
    public Guid? NewObjectId => _clonedChart?.Id ?? _clonedShape?.Id;

    /// <param name="offsetX">
    /// Pixel offset applied to the duplicate's position relative to the source, matching Excel's
    /// small cascade offset on Ctrl+V of an object (avoids the paste landing exactly on top of the
    /// source and looking like nothing happened when source and destination sheet are the same).
    /// </param>
    public DuplicateDrawingObjectCommand(
        SheetId sourceSheetId,
        SheetId destinationSheetId,
        SelectionPaneObjectKind kind,
        Guid sourceObjectId,
        double offsetX = 12,
        double offsetY = 12)
    {
        _sourceSheetId = sourceSheetId;
        _destinationSheetId = destinationSheetId;
        _kind = kind;
        _sourceObjectId = sourceObjectId;
        _offsetX = offsetX;
        _offsetY = offsetY;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sourceSheet = ctx.GetSheet(_sourceSheetId);
        var destinationSheet = ctx.GetSheet(_destinationSheetId);

        switch (_kind)
        {
            case SelectionPaneObjectKind.Chart:
            {
                var sourceChart = sourceSheet.Charts.Find(c => c.Id == _sourceObjectId);
                if (sourceChart is null)
                    return new CommandOutcome(false, "The copied chart no longer exists.");
                if (ChartCommandGuards.RejectIfEditObjectsBlocked(destinationSheet) is { } protectedOutcome)
                    return protectedOutcome;

                _clonedChart = DuplicateSheetDrawingCloner.CloneChart(sourceChart, _sourceSheetId, _destinationSheetId);
                _clonedChart.Left = sourceChart.Left + _offsetX;
                _clonedChart.Top = sourceChart.Top + _offsetY;
                destinationSheet.Charts.Add(_clonedChart);
                _added = true;
                return new CommandOutcome(true);
            }

            case SelectionPaneObjectKind.Shape:
            {
                var sourceShape = sourceSheet.DrawingShapes.Find(s => s.Id == _sourceObjectId);
                if (sourceShape is null)
                    return new CommandOutcome(false, "The copied shape no longer exists.");
                if (DrawingShapeCommandGuards.RejectIfEditObjectsBlocked(destinationSheet) is { } protectedOutcome)
                    return protectedOutcome;

                _clonedShape = DuplicateSheetDrawingCloner.CloneDrawingShape(sourceShape, _destinationSheetId);
                _clonedShape.AnchorOffsetX = sourceShape.AnchorOffsetX + _offsetX;
                _clonedShape.AnchorOffsetY = sourceShape.AnchorOffsetY + _offsetY;
                destinationSheet.DrawingShapes.Add(_clonedShape);
                _added = true;
                return new CommandOutcome(true);
            }

            default:
                return new CommandOutcome(false, "Copying this object type is not supported yet.");
        }
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_added)
            return;

        var destinationSheet = ctx.GetSheet(_destinationSheetId);
        if (_clonedChart is not null)
            destinationSheet.Charts.Remove(_clonedChart);
        if (_clonedShape is not null)
            destinationSheet.DrawingShapes.Remove(_clonedShape);
        _added = false;
    }
}
