using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Duplicates or moves a single selected chart/shape/picture/text box (GridView.SelectedObjectKind/
/// SelectedObjectId) onto a destination sheet -- the command backing Ctrl+C/Ctrl+V on a selected
/// object (R91-io-clipboard-image-formats-5-1 for Chart/Shape; completed for Picture/TextBox by
/// R92-consumer-wiring-sweep-2). Real Excel's Ctrl+C/Ctrl+V on a selected object duplicates it (same
/// sheet, or a different sheet if the destination selection moved there first); before this command
/// existed, FreeX's clipboard commands only ever knew about cell RANGES, so Ctrl+C on a selected
/// drawing object silently copied whatever cell happened to be under its anchor instead.
/// Reuses <see cref="DuplicateSheetDrawingCloner"/>'s per-object clone helpers (the exact ones
/// Duplicate Sheet already uses) so a pasted object carries every property, instead of hand-rolling
/// a second partial property list here. A move adds the clone and removes the source in one command,
/// which makes object Cut transactional and undoable just like the WPF cell clipboard path.
/// </summary>
public sealed class DuplicateDrawingObjectCommand : IWorkbookCommand
{
    private readonly SheetId _sourceSheetId;
    private readonly SheetId _destinationSheetId;
    private readonly SelectionPaneObjectKind _kind;
    private readonly Guid _sourceObjectId;
    private readonly double _offsetX;
    private readonly double _offsetY;
    private readonly bool _removeSource;

    private ChartModel? _clonedChart;
    private DrawingShapeModel? _clonedShape;
    private PictureModel? _clonedPicture;
    private TextBoxModel? _clonedTextBox;
    private ChartModel? _movedChart;
    private DrawingShapeModel? _movedShape;
    private PictureModel? _movedPicture;
    private TextBoxModel? _movedTextBox;
    private int _sourceIndex;
    private bool _added;

    public string Label => _removeSource ? "Move" : "Paste";

    /// <summary>The Id of the newly-created duplicate, once <see cref="Apply"/> has succeeded.</summary>
    public Guid? NewObjectId => _clonedChart?.Id ?? _clonedShape?.Id ?? _clonedPicture?.Id ?? _clonedTextBox?.Id;

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
        double offsetY = 12,
        bool removeSource = false)
    {
        _sourceSheetId = sourceSheetId;
        _destinationSheetId = destinationSheetId;
        _kind = kind;
        _sourceObjectId = sourceObjectId;
        _offsetX = offsetX;
        _offsetY = offsetY;
        _removeSource = removeSource;
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
                if ((_removeSource
                        ? ChartCommandGuards.RejectIfEditObjectsBlocked(destinationSheet, sourceChart)
                        : ChartCommandGuards.RejectIfEditObjectsBlocked(destinationSheet)) is { } protectedOutcome)
                    return protectedOutcome;

                // Ctrl+C/Ctrl+V of a selected chart object duplicates the object itself, not the
                // data it plots -- the DataRange must keep pointing at the exact original
                // source sheet/cells regardless of the destination sheet (R94-cmd-paste-charts-
                // cross-sheet-dataRange), unlike whole-sheet Duplicate Sheet's "same-sheet
                // DataRange follows the copy" semantics.
                _clonedChart = DuplicateSheetDrawingCloner.CloneChart(
                    sourceChart, _sourceSheetId, _destinationSheetId, remapSameSheetDataRange: false);
                _clonedChart.Left = sourceChart.Left + _offsetX;
                _clonedChart.Top = sourceChart.Top + _offsetY;
                destinationSheet.Charts.Add(_clonedChart);
                if (_removeSource)
                {
                    _sourceIndex = sourceSheet.Charts.IndexOf(sourceChart);
                    sourceSheet.Charts.Remove(sourceChart);
                    _movedChart = sourceChart;
                }
                _added = true;
                return new CommandOutcome(true);
            }

            case SelectionPaneObjectKind.Shape:
            {
                var sourceShape = sourceSheet.DrawingShapes.Find(s => s.Id == _sourceObjectId);
                if (sourceShape is null)
                    return new CommandOutcome(false, "The copied shape no longer exists.");
                if ((_removeSource
                        ? DrawingShapeCommandGuards.RejectIfEditObjectsBlocked(destinationSheet, sourceShape)
                        : DrawingShapeCommandGuards.RejectIfEditObjectsBlocked(destinationSheet)) is { } protectedOutcome)
                    return protectedOutcome;

                _clonedShape = DuplicateSheetDrawingCloner.CloneDrawingShape(sourceShape, _destinationSheetId);
                _clonedShape.AnchorOffsetX = sourceShape.AnchorOffsetX + _offsetX;
                _clonedShape.AnchorOffsetY = sourceShape.AnchorOffsetY + _offsetY;
                destinationSheet.DrawingShapes.Add(_clonedShape);
                if (_removeSource)
                {
                    _sourceIndex = sourceSheet.DrawingShapes.IndexOf(sourceShape);
                    sourceSheet.DrawingShapes.Remove(sourceShape);
                    _movedShape = sourceShape;
                }
                _added = true;
                return new CommandOutcome(true);
            }

            case SelectionPaneObjectKind.Picture:
            {
                var sourcePicture = sourceSheet.Pictures.Find(p => p.Id == _sourceObjectId);
                if (sourcePicture is null)
                    return new CommandOutcome(false, "The copied picture no longer exists.");
                if ((_removeSource
                        ? PictureCommandGuards.RejectIfEditObjectsBlocked(destinationSheet, sourcePicture)
                        : PictureCommandGuards.RejectIfEditObjectsBlocked(destinationSheet)) is { } protectedOutcome)
                    return protectedOutcome;

                _clonedPicture = DuplicateSheetDrawingCloner.ClonePicture(
                    sourcePicture, _sourceSheetId, sourceSheet.Name, destinationSheet.Name, _destinationSheetId);
                _clonedPicture.AnchorOffsetX = sourcePicture.AnchorOffsetX + _offsetX;
                _clonedPicture.AnchorOffsetY = sourcePicture.AnchorOffsetY + _offsetY;
                destinationSheet.Pictures.Add(_clonedPicture);
                if (_removeSource)
                {
                    _sourceIndex = sourceSheet.Pictures.IndexOf(sourcePicture);
                    sourceSheet.Pictures.Remove(sourcePicture);
                    _movedPicture = sourcePicture;
                }
                _added = true;
                return new CommandOutcome(true);
            }

            case SelectionPaneObjectKind.TextBox:
            {
                var sourceTextBox = sourceSheet.TextBoxes.Find(t => t.Id == _sourceObjectId);
                if (sourceTextBox is null)
                    return new CommandOutcome(false, "The copied text box no longer exists.");
                if ((_removeSource
                        ? TextBoxCommandGuards.RejectIfEditObjectsBlocked(destinationSheet, sourceTextBox)
                        : TextBoxCommandGuards.RejectIfEditObjectsBlocked(destinationSheet)) is { } protectedOutcome)
                    return protectedOutcome;

                _clonedTextBox = DuplicateSheetDrawingCloner.CloneTextBox(sourceTextBox, _destinationSheetId);
                _clonedTextBox.AnchorOffsetX = sourceTextBox.AnchorOffsetX + _offsetX;
                _clonedTextBox.AnchorOffsetY = sourceTextBox.AnchorOffsetY + _offsetY;
                destinationSheet.TextBoxes.Add(_clonedTextBox);
                if (_removeSource)
                {
                    _sourceIndex = sourceSheet.TextBoxes.IndexOf(sourceTextBox);
                    sourceSheet.TextBoxes.Remove(sourceTextBox);
                    _movedTextBox = sourceTextBox;
                }
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
        if (_clonedPicture is not null)
            destinationSheet.Pictures.Remove(_clonedPicture);
        if (_clonedTextBox is not null)
            destinationSheet.TextBoxes.Remove(_clonedTextBox);

        if (_removeSource)
        {
            var sourceSheet = ctx.GetSheet(_sourceSheetId);
            if (_movedChart is not null)
                sourceSheet.Charts.Insert(Math.Min(_sourceIndex, sourceSheet.Charts.Count), _movedChart);
            if (_movedShape is not null)
                sourceSheet.DrawingShapes.Insert(Math.Min(_sourceIndex, sourceSheet.DrawingShapes.Count), _movedShape);
            if (_movedPicture is not null)
                sourceSheet.Pictures.Insert(Math.Min(_sourceIndex, sourceSheet.Pictures.Count), _movedPicture);
            if (_movedTextBox is not null)
                sourceSheet.TextBoxes.Insert(Math.Min(_sourceIndex, sourceSheet.TextBoxes.Count), _movedTextBox);
            _movedChart = null;
            _movedShape = null;
            _movedPicture = null;
            _movedTextBox = null;
        }
        _added = false;
    }
}
