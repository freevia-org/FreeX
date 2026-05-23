using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public sealed class SetPictureAltTextCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _pictureId;
    private readonly string? _altText;
    private string? _previousAltText;
    private bool _applied;

    public string Label => "Picture Alt Text";

    public SetPictureAltTextCommand(SheetId sheetId, Guid pictureId, string? altText)
    {
        _sheetId = sheetId;
        _pictureId = pictureId;
        _altText = Normalize(altText);
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects) is { } protectedOutcome)
            return protectedOutcome;

        var picture = sheet.Pictures.FirstOrDefault(item => item.Id == _pictureId);
        if (picture is null)
            return new CommandOutcome(false, "Picture was not found.");

        _previousAltText = picture.AltText;
        picture.AltText = _altText;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [picture.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        var picture = ctx.GetSheet(_sheetId).Pictures.FirstOrDefault(item => item.Id == _pictureId);
        if (picture is null) return;
        picture.AltText = _previousAltText;
        _applied = false;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class SetDrawingShapeAltTextCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _shapeId;
    private readonly string? _altText;
    private string? _previousAltText;
    private bool _applied;

    public string Label => "Shape Alt Text";

    public SetDrawingShapeAltTextCommand(SheetId sheetId, Guid shapeId, string? altText)
    {
        _sheetId = sheetId;
        _shapeId = shapeId;
        _altText = Normalize(altText);
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects) is { } protectedOutcome)
            return protectedOutcome;

        var shape = sheet.DrawingShapes.FirstOrDefault(item => item.Id == _shapeId);
        if (shape is null)
            return new CommandOutcome(false, "Drawing shape was not found.");

        _previousAltText = shape.AltText;
        shape.AltText = _altText;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [shape.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        var shape = ctx.GetSheet(_sheetId).DrawingShapes.FirstOrDefault(item => item.Id == _shapeId);
        if (shape is null) return;
        shape.AltText = _previousAltText;
        _applied = false;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class SetTextBoxAltTextCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _textBoxId;
    private readonly string? _altText;
    private string? _previousAltText;
    private bool _applied;

    public string Label => "Text Box Alt Text";

    public SetTextBoxAltTextCommand(SheetId sheetId, Guid textBoxId, string? altText)
    {
        _sheetId = sheetId;
        _textBoxId = textBoxId;
        _altText = Normalize(altText);
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects) is { } protectedOutcome)
            return protectedOutcome;

        var textBox = sheet.TextBoxes.FirstOrDefault(item => item.Id == _textBoxId);
        if (textBox is null)
            return new CommandOutcome(false, "Text box was not found.");

        _previousAltText = textBox.AltText;
        textBox.AltText = _altText;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [textBox.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        var textBox = ctx.GetSheet(_sheetId).TextBoxes.FirstOrDefault(item => item.Id == _textBoxId);
        if (textBox is null) return;
        textBox.AltText = _previousAltText;
        _applied = false;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
