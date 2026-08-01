using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class SetPictureAltTextCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _pictureId;
    private readonly AltTextCommandChange _change;

    public string Label => "Picture Alt Text";

    public SetPictureAltTextCommand(SheetId sheetId, Guid pictureId, string? altText)
    {
        _sheetId = sheetId;
        _pictureId = pictureId;
        _change = new AltTextCommandChange(altText);
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (!PictureCommandGuards.TryFindPicture(sheet, _pictureId, out var picture))
            return PictureCommandGuards.PictureNotFound();

        // R112-model-drawing-object-lock-1-1 sibling fix: layer in the per-picture Locked override
        // so an author-unlocked picture's alt text stays editable even while the sheet blocks "Edit
        // objects".
        if (PictureCommandGuards.RejectIfEditObjectsBlocked(sheet, picture) is { } protectedOutcome)
            return protectedOutcome;

        picture.AltText = _change.Apply(picture.AltText);
        return new CommandOutcome(true, AffectedCells: [picture.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_change.Applied) return;
        if (!PictureCommandGuards.TryFindPicture(ctx.GetSheet(_sheetId), _pictureId, out var picture)) return;
        picture.AltText = _change.PreviousAltText;
        _change.MarkReverted();
    }

}

public sealed class SetDrawingShapeAltTextCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _shapeId;
    private readonly AltTextCommandChange _change;

    public string Label => "Shape Alt Text";

    public SetDrawingShapeAltTextCommand(SheetId sheetId, Guid shapeId, string? altText)
    {
        _sheetId = sheetId;
        _shapeId = shapeId;
        _change = new AltTextCommandChange(altText);
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (!DrawingShapeCommandGuards.TryFindShape(sheet, _shapeId, out var shape))
            return DrawingShapeCommandGuards.DrawingShapeNotFound();

        // R112-model-drawing-object-lock-1-1 sibling fix: layer in the per-shape Locked override so
        // an author-unlocked shape's alt text stays editable even while the sheet blocks "Edit
        // objects".
        if (DrawingShapeCommandGuards.RejectIfEditObjectsBlocked(sheet, shape) is { } protectedOutcome)
            return protectedOutcome;

        shape.AltText = _change.Apply(shape.AltText);
        return new CommandOutcome(true, AffectedCells: [shape.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_change.Applied) return;
        if (!DrawingShapeCommandGuards.TryFindShape(ctx.GetSheet(_sheetId), _shapeId, out var shape)) return;
        shape.AltText = _change.PreviousAltText;
        _change.MarkReverted();
    }

}

public sealed class SetTextBoxAltTextCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _textBoxId;
    private readonly AltTextCommandChange _change;

    public string Label => "Text Box Alt Text";

    public SetTextBoxAltTextCommand(SheetId sheetId, Guid textBoxId, string? altText)
    {
        _sheetId = sheetId;
        _textBoxId = textBoxId;
        _change = new AltTextCommandChange(altText);
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (!TextBoxCommandGuards.TryFindTextBox(sheet, _textBoxId, out var textBox))
            return TextBoxCommandGuards.TextBoxNotFound();

        // R112-model-drawing-object-lock-1-1 sibling fix: layer in the per-text-box Locked override
        // so an author-unlocked text box's alt text stays editable even while the sheet blocks "Edit
        // objects".
        if (TextBoxCommandGuards.RejectIfEditObjectsBlocked(sheet, textBox) is { } protectedOutcome)
            return protectedOutcome;

        textBox.AltText = _change.Apply(textBox.AltText);
        return new CommandOutcome(true, AffectedCells: [textBox.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_change.Applied) return;
        if (!TextBoxCommandGuards.TryFindTextBox(ctx.GetSheet(_sheetId), _textBoxId, out var textBox)) return;
        textBox.AltText = _change.PreviousAltText;
        _change.MarkReverted();
    }

}

sealed class AltTextCommandChange
{
    private readonly string? _altText;

    public AltTextCommandChange(string? altText)
    {
        _altText = AltTextCommandText.Normalize(altText);
    }

    public string? PreviousAltText { get; private set; }
    public bool Applied { get; private set; }

    public string? Apply(string? currentAltText)
    {
        PreviousAltText = currentAltText;
        Applied = true;
        return _altText;
    }

    public void MarkReverted()
    {
        Applied = false;
    }
}

file static class AltTextCommandText
{
    public static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
