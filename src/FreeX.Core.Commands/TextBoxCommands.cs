using System.Diagnostics.CodeAnalysis;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class AddTextBoxCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly TextBoxModel _textBox;
    private bool _added;

    public string Label => "Insert Text Box";

    public AddTextBoxCommand(
        SheetId sheetId,
        CellAddress anchor,
        string text,
        double width = 180,
        double height = 80)
    {
        _sheetId = sheetId;
        _textBox = new TextBoxModel
        {
            Anchor = anchor,
            Text = text,
            Width = width,
            Height = height
        };
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_textBox.Anchor.Sheet != _sheetId)
            return new CommandOutcome(false, "Text box anchor must be on the target sheet.");
        if (TextBoxCommandGuards.RejectInvalidSize(_textBox.Width, _textBox.Height) is { } invalidSize)
            return invalidSize;

        var sheet = ctx.GetSheet(_sheetId);
        if (TextBoxCommandGuards.RejectIfEditObjectsBlocked(sheet) is { } protectedOutcome)
            return protectedOutcome;

        sheet.TextBoxes.Add(_textBox);
        _added = true;
        return new CommandOutcome(true, AffectedCells: [_textBox.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_added)
            return;

        ctx.GetSheet(_sheetId).TextBoxes.Remove(_textBox);
        _added = false;
    }
}

public sealed class ResizeTextBoxCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _textBoxId;
    private readonly double _width;
    private readonly double _height;
    private double _previousWidth;
    private double _previousHeight;
    private bool _applied;

    public string Label => "Resize Text Box";

    public ResizeTextBoxCommand(SheetId sheetId, Guid textBoxId, double width, double height)
    {
        _sheetId = sheetId;
        _textBoxId = textBoxId;
        _width = width;
        _height = height;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (TextBoxCommandGuards.RejectInvalidSize(_width, _height) is { } invalidSize)
            return invalidSize;

        var sheet = ctx.GetSheet(_sheetId);
        if (TextBoxCommandGuards.RejectIfEditObjectsBlocked(sheet) is { } protectedOutcome)
            return protectedOutcome;

        if (!TextBoxCommandGuards.TryFindTextBox(sheet, _textBoxId, out var textBox))
            return TextBoxCommandGuards.TextBoxNotFound();

        _previousWidth = textBox.Width;
        _previousHeight = textBox.Height;
        textBox.Width = _width;
        textBox.Height = _height;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [textBox.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        var sheet = ctx.GetSheet(_sheetId);
        if (!TextBoxCommandGuards.TryFindTextBox(sheet, _textBoxId, out var textBox)) return;
        textBox.Width = _previousWidth;
        textBox.Height = _previousHeight;
        _applied = false;
    }
}

public sealed class RotateTextBoxCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _textBoxId;
    private readonly double _rotationDegrees;
    private double _previousRotationDegrees;
    private bool _applied;

    public string Label => "Rotate Text Box";

    public RotateTextBoxCommand(SheetId sheetId, Guid textBoxId, double rotationDegrees)
    {
        _sheetId = sheetId;
        _textBoxId = textBoxId;
        _rotationDegrees = rotationDegrees;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (!double.IsFinite(_rotationDegrees))
            return new CommandOutcome(false, "Text box rotation must be a finite number.");

        var sheet = ctx.GetSheet(_sheetId);
        if (TextBoxCommandGuards.RejectIfEditObjectsBlocked(sheet) is { } protectedOutcome)
            return protectedOutcome;

        if (!TextBoxCommandGuards.TryFindTextBox(sheet, _textBoxId, out var textBox))
            return TextBoxCommandGuards.TextBoxNotFound();

        _previousRotationDegrees = textBox.RotationDegrees;
        textBox.RotationDegrees = ObjectRotationNormalizer.NormalizeDegrees(_rotationDegrees);
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [textBox.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        var sheet = ctx.GetSheet(_sheetId);
        if (!TextBoxCommandGuards.TryFindTextBox(sheet, _textBoxId, out var textBox)) return;
        textBox.RotationDegrees = _previousRotationDegrees;
        _applied = false;
    }

}

public sealed class SetTextBoxColorsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _textBoxId;
    private readonly CellColor? _fillColor;
    private readonly CellColor? _outlineColor;
    private CellColor? _previousFillColor;
    private CellColor? _previousOutlineColor;
    private WorkbookThemeColorReference? _previousFillThemeColor;
    private WorkbookThemeColorReference? _previousOutlineThemeColor;
    private bool _applied;

    public string Label => "Text Box Colors";

    public SetTextBoxColorsCommand(
        SheetId sheetId,
        Guid textBoxId,
        CellColor? fillColor,
        CellColor? outlineColor)
    {
        _sheetId = sheetId;
        _textBoxId = textBoxId;
        _fillColor = fillColor;
        _outlineColor = outlineColor;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (TextBoxCommandGuards.RejectIfEditObjectsBlocked(sheet) is { } protectedOutcome)
            return protectedOutcome;

        if (!TextBoxCommandGuards.TryFindTextBox(sheet, _textBoxId, out var textBox))
            return TextBoxCommandGuards.TextBoxNotFound();

        _previousFillColor = textBox.FillColor;
        _previousOutlineColor = textBox.OutlineColor;
        _previousFillThemeColor = textBox.FillThemeColor;
        _previousOutlineThemeColor = textBox.OutlineThemeColor;
        textBox.FillColor = _fillColor;
        textBox.OutlineColor = _outlineColor;
        textBox.FillThemeColor = null;
        textBox.OutlineThemeColor = null;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [textBox.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        var sheet = ctx.GetSheet(_sheetId);
        if (!TextBoxCommandGuards.TryFindTextBox(sheet, _textBoxId, out var textBox)) return;
        textBox.FillColor = _previousFillColor;
        textBox.OutlineColor = _previousOutlineColor;
        textBox.FillThemeColor = _previousFillThemeColor;
        textBox.OutlineThemeColor = _previousOutlineThemeColor;
        _applied = false;
    }
}

public sealed class RepositionTextBoxCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _textBoxId;
    private readonly CellAddress _anchor;
    private CellAddress _previousAnchor;
    private bool _applied;

    public string Label => "Move Text Box";

    public RepositionTextBoxCommand(SheetId sheetId, Guid textBoxId, CellAddress anchor)
    {
        _sheetId = sheetId;
        _textBoxId = textBoxId;
        _anchor = anchor;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (TextBoxCommandGuards.RejectIfEditObjectsBlocked(sheet) is { } protectedOutcome)
            return protectedOutcome;
        if (!TextBoxCommandGuards.TryFindTextBox(sheet, _textBoxId, out var textBox))
            return TextBoxCommandGuards.TextBoxNotFound();
        _previousAnchor = textBox.Anchor;
        textBox.Anchor = _anchor;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [_previousAnchor, _anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        var sheet = ctx.GetSheet(_sheetId);
        if (!TextBoxCommandGuards.TryFindTextBox(sheet, _textBoxId, out var textBox)) return;
        textBox.Anchor = _previousAnchor;
        _applied = false;
    }
}

internal static class TextBoxCommandGuards
{
    private const string InvalidTextBoxSizeMessage = "Text box size must be positive.";
    private const string TextBoxNotFoundMessage = "Text box was not found.";

    public static CommandOutcome? RejectIfEditObjectsBlocked(Sheet sheet) =>
        CommandGuards.RejectIfProtectedWithoutPermission(sheet, SheetProtectionPermission.EditObjects);

    public static CommandOutcome? RejectInvalidSize(double width, double height) =>
        double.IsFinite(width) && double.IsFinite(height) && width > 0 && height > 0
            ? null
            : new CommandOutcome(false, InvalidTextBoxSizeMessage);

    public static bool TryFindTextBox(
        Sheet sheet,
        Guid textBoxId,
        [NotNullWhen(true)] out TextBoxModel? textBox)
    {
        textBox = sheet.TextBoxes.FirstOrDefault(item => item.Id == textBoxId);
        return textBox is not null;
    }

    public static CommandOutcome TextBoxNotFound() =>
        new(false, TextBoxNotFoundMessage);
}
