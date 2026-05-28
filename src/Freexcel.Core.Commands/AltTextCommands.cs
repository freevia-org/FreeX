using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

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
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects) is { } protectedOutcome)
            return protectedOutcome;

        var picture = sheet.Pictures.FirstOrDefault(item => item.Id == _pictureId);
        if (picture is null)
            return new CommandOutcome(false, "Picture was not found.");

        picture.AltText = _change.Apply(picture.AltText);
        return new CommandOutcome(true, AffectedCells: [picture.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_change.Applied) return;
        var picture = ctx.GetSheet(_sheetId).Pictures.FirstOrDefault(item => item.Id == _pictureId);
        if (picture is null) return;
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
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects) is { } protectedOutcome)
            return protectedOutcome;

        var shape = sheet.DrawingShapes.FirstOrDefault(item => item.Id == _shapeId);
        if (shape is null)
            return new CommandOutcome(false, "Drawing shape was not found.");

        shape.AltText = _change.Apply(shape.AltText);
        return new CommandOutcome(true, AffectedCells: [shape.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_change.Applied) return;
        var shape = ctx.GetSheet(_sheetId).DrawingShapes.FirstOrDefault(item => item.Id == _shapeId);
        if (shape is null) return;
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
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects) is { } protectedOutcome)
            return protectedOutcome;

        var textBox = sheet.TextBoxes.FirstOrDefault(item => item.Id == _textBoxId);
        if (textBox is null)
            return new CommandOutcome(false, "Text box was not found.");

        textBox.AltText = _change.Apply(textBox.AltText);
        return new CommandOutcome(true, AffectedCells: [textBox.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_change.Applied) return;
        var textBox = ctx.GetSheet(_sheetId).TextBoxes.FirstOrDefault(item => item.Id == _textBoxId);
        if (textBox is null) return;
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
