using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// R121-model-drawing-delete-1: the FIRST way to delete a picture, text box, drawing shape, or chart
/// from a sheet -- before this command, every existing <c>sheet.Pictures.Remove(...)</c>-style call
/// site (PictureCommands.cs, TextBoxCommands.cs, DrawingShapeCommands.cs, ChartCommands.Create.cs,
/// PasteXxxCommand.cs, DuplicateDrawingObjectCommand.cs) is the <see cref="IWorkbookCommand.Revert"/>
/// of a same-session Insert/Paste/Duplicate, never a user-facing delete of an EXISTING object. Excel
/// lets a user select any drawing object and press Delete; FreeX had no equivalent.
/// <para>
/// LINKED FIX (R121, round 111 backlog): deleting an object that was ever loaded from the opened
/// .xlsx must not silently resurrect it on the next save. <see cref="Apply"/> always records the
/// deleted object's <c>cNvPr@name</c> onto <see cref="Sheet.DeletedSourceDrawingObjectNames"/> --
/// regardless of whether the object was still <c>IsSourceLoaded</c> at the moment of deletion, because
/// an EDITED-then-deleted loaded object's stale original anchor is exactly as dangerous as a
/// never-edited one's (see the doc comment on that list, and on
/// <c>XlsxWorksheetDrawingObjectWriter.GetRewrittenSourceObjectNames</c>, which now unions it in for
/// <c>XlsxWorksheetDrawingPartMerger</c>). Tombstoning a freshly authored object's name (one that was
/// never in the source package at all) is harmless: the merger's supersede check only ever matches a
/// name against an anchor that is ACTUALLY present in the source drawing part, so a name with no source
/// anchor at all is simply never matched.
/// </para>
/// </summary>
public sealed class DeleteDrawingObjectCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly SelectionPaneObjectKind _kind;
    private readonly Guid _objectId;

    private PictureModel? _removedPicture;
    private TextBoxModel? _removedTextBox;
    private DrawingShapeModel? _removedShape;
    private ChartModel? _removedChart;
    private int _removedIndex = -1;
    private string? _tombstonedName;
    private bool _applied;

    public string Label => "Delete";

    public DeleteDrawingObjectCommand(SheetId sheetId, SelectionPaneObjectKind kind, Guid objectId)
    {
        _sheetId = sheetId;
        _kind = kind;
        _objectId = objectId;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        return _kind switch
        {
            SelectionPaneObjectKind.Picture => ApplyPicture(sheet),
            SelectionPaneObjectKind.TextBox => ApplyTextBox(sheet),
            SelectionPaneObjectKind.Shape => ApplyShape(sheet),
            SelectionPaneObjectKind.Chart => ApplyChart(sheet),
            _ => new CommandOutcome(false, "Drawing object kind is not supported.")
        };
    }

    private CommandOutcome ApplyPicture(Sheet sheet)
    {
        var index = sheet.Pictures.FindIndex(item => item.Id == _objectId);
        if (index < 0)
            return PictureCommandGuards.PictureNotFound();

        var picture = sheet.Pictures[index];
        if (PictureCommandGuards.RejectIfEditObjectsBlocked(sheet, picture) is { } protectedOutcome)
            return protectedOutcome;

        sheet.Pictures.RemoveAt(index);
        _removedPicture = picture;
        _removedIndex = index;
        TombstoneName(sheet, picture.Name);
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [picture.Anchor]);
    }

    private CommandOutcome ApplyTextBox(Sheet sheet)
    {
        var index = sheet.TextBoxes.FindIndex(item => item.Id == _objectId);
        if (index < 0)
            return TextBoxCommandGuards.TextBoxNotFound();

        var textBox = sheet.TextBoxes[index];
        if (TextBoxCommandGuards.RejectIfEditObjectsBlocked(sheet, textBox) is { } protectedOutcome)
            return protectedOutcome;

        sheet.TextBoxes.RemoveAt(index);
        _removedTextBox = textBox;
        _removedIndex = index;
        TombstoneName(sheet, textBox.Name);
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [textBox.Anchor]);
    }

    private CommandOutcome ApplyShape(Sheet sheet)
    {
        var index = sheet.DrawingShapes.FindIndex(item => item.Id == _objectId);
        if (index < 0)
            return DrawingShapeCommandGuards.DrawingShapeNotFound();

        var shape = sheet.DrawingShapes[index];
        if (DrawingShapeCommandGuards.RejectIfEditObjectsBlocked(sheet, shape) is { } protectedOutcome)
            return protectedOutcome;

        sheet.DrawingShapes.RemoveAt(index);
        _removedShape = shape;
        _removedIndex = index;
        TombstoneName(sheet, shape.Name);
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [shape.Anchor]);
    }

    private CommandOutcome ApplyChart(Sheet sheet)
    {
        var index = sheet.Charts.FindIndex(item => item.Id == _objectId);
        if (index < 0)
            return ChartCommandGuards.ChartNotFound();

        var chart = sheet.Charts[index];
        if (ChartCommandGuards.RejectIfEditObjectsBlocked(sheet, chart) is { } protectedOutcome)
            return protectedOutcome;

        sheet.Charts.RemoveAt(index);
        _removedChart = chart;
        _removedIndex = index;
        TombstoneName(sheet, chart.Name);
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [chart.DataRange.Start]);
    }

    private void TombstoneName(Sheet sheet, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        sheet.DeletedSourceDrawingObjectNames.Add(name);
        _tombstonedName = name;
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        var index = Math.Clamp(_removedIndex, 0, int.MaxValue);

        if (_removedPicture is { } picture)
            InsertAt(sheet.Pictures, picture, index);
        else if (_removedTextBox is { } textBox)
            InsertAt(sheet.TextBoxes, textBox, index);
        else if (_removedShape is { } shape)
            InsertAt(sheet.DrawingShapes, shape, index);
        else if (_removedChart is { } chart)
            InsertAt(sheet.Charts, chart, index);

        if (_tombstonedName is not null)
            sheet.DeletedSourceDrawingObjectNames.Remove(_tombstonedName);

        _removedPicture = null;
        _removedTextBox = null;
        _removedShape = null;
        _removedChart = null;
        _removedIndex = -1;
        _tombstonedName = null;
        _applied = false;
    }

    private static void InsertAt<T>(List<T> list, T item, int index)
    {
        if (index >= 0 && index <= list.Count)
            list.Insert(index, item);
        else
            list.Add(item);
    }
}
