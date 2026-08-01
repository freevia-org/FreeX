using System.Diagnostics.CodeAnalysis;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class AddTextBoxCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly TextBoxModel _textBox;
    private bool _added;

    public string Label => "Insert Text Box";
    public Guid TextBoxId => _textBox.Id;

    public AddTextBoxCommand(
        SheetId sheetId,
        CellAddress anchor,
        string text,
        double width = TextBoxModel.DefaultWidth,
        double height = TextBoxModel.DefaultHeight)
    {
        _sheetId = sheetId;
        _textBox = new TextBoxModel
        {
            Anchor = anchor,
            Text = text,
            Width = width,
            Height = height,
            // R91-commands-insert-object-5-1: Excel's Insert > Text Box always creates a
            // transparent, borderless box (No Fill, No Line) until the user explicitly adds one --
            // override TextBoxModel's safe (always-bordered) class defaults here, at the one
            // new-insert choke point, instead of changing them globally and affecting every
            // loaded/imported text box that never goes through this constructor.
            HasFill = false,
            OutlineHasNoFill = true
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
    private readonly bool? _flipHorizontal;
    private readonly bool? _flipVertical;
    private double _previousWidth;
    private double _previousHeight;
    private bool _previousFlipHorizontal;
    private bool _previousFlipVertical;
    private bool _applied;

    public string Label => "Resize Text Box";

    public ResizeTextBoxCommand(
        SheetId sheetId,
        Guid textBoxId,
        double width,
        double height,
        bool? flipHorizontal = null,
        bool? flipVertical = null)
    {
        _sheetId = sheetId;
        _textBoxId = textBoxId;
        _width = width;
        _height = height;
        _flipHorizontal = flipHorizontal;
        _flipVertical = flipVertical;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (TextBoxCommandGuards.RejectInvalidSize(_width, _height) is { } invalidSize)
            return invalidSize;

        var sheet = ctx.GetSheet(_sheetId);
        if (!TextBoxCommandGuards.TryFindTextBox(sheet, _textBoxId, out var textBox))
            return TextBoxCommandGuards.TextBoxNotFound();

        // R111-model-drawing-object-lock-1-1: layer in the per-text-box Locked override so an
        // author-unlocked text box stays resizable even while the sheet blocks "Edit objects".
        if (TextBoxCommandGuards.RejectIfEditObjectsBlocked(sheet, textBox) is { } protectedOutcome)
            return protectedOutcome;

        _previousWidth = textBox.Width;
        _previousHeight = textBox.Height;
        _previousFlipHorizontal = textBox.FlipHorizontal;
        _previousFlipVertical = textBox.FlipVertical;
        textBox.Width = _width;
        textBox.Height = _height;
        if (_flipHorizontal.HasValue)
            textBox.FlipHorizontal = _flipHorizontal.Value;
        if (_flipVertical.HasValue)
            textBox.FlipVertical = _flipVertical.Value;
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
        textBox.FlipHorizontal = _previousFlipHorizontal;
        textBox.FlipVertical = _previousFlipVertical;
        _applied = false;
    }
}

public sealed class SetTextBoxTextCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _textBoxId;
    private readonly string _text;
    private string _previousText = string.Empty;
    private bool _applied;

    public string Label => "Edit Text Box";

    public SetTextBoxTextCommand(SheetId sheetId, Guid textBoxId, string text)
    {
        _sheetId = sheetId;
        _textBoxId = textBoxId;
        _text = text;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (!TextBoxCommandGuards.TryFindTextBox(sheet, _textBoxId, out var textBox))
            return TextBoxCommandGuards.TextBoxNotFound();

        // R112-model-drawing-object-lock-1-1: layer in the per-text-box Locked override so an
        // author-unlocked text box stays text-editable even while the sheet blocks "Edit objects".
        if (TextBoxCommandGuards.RejectIfEditObjectsBlocked(sheet, textBox) is { } protectedOutcome)
            return protectedOutcome;

        _previousText = textBox.Text;
        textBox.Text = _text;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [textBox.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        var sheet = ctx.GetSheet(_sheetId);
        if (!TextBoxCommandGuards.TryFindTextBox(sheet, _textBoxId, out var textBox)) return;
        textBox.Text = _previousText;
        _applied = false;
    }
}

public sealed class RotateTextBoxCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _textBoxId;
    private readonly double _rotationDegrees;
    private double _previousRotationDegrees;
    private bool _previousIsSourceLoaded;
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
        if (!TextBoxCommandGuards.TryFindTextBox(sheet, _textBoxId, out var textBox))
            return TextBoxCommandGuards.TextBoxNotFound();

        // R112-model-drawing-object-lock-1-1: layer in the per-text-box Locked override so an
        // author-unlocked text box stays rotatable even while the sheet blocks "Edit objects".
        if (TextBoxCommandGuards.RejectIfEditObjectsBlocked(sheet, textBox) is { } protectedOutcome)
            return protectedOutcome;

        _previousRotationDegrees = textBox.RotationDegrees;
        _previousIsSourceLoaded = textBox.IsSourceLoaded;
        textBox.RotationDegrees = ObjectRotationNormalizer.NormalizeDegrees(_rotationDegrees);
        // R62-io-drawing-textbox-6-1: mirror DrawingShapeFormatCommands' fix for the same class of
        // bug — a loaded text box's edit must clear IsSourceLoaded so the full writer emits the
        // edited object instead of silently discarding the rotation via the preserved source XML.
        textBox.IsSourceLoaded = false;
        _applied = true;
        return new CommandOutcome(true, AffectedCells: [textBox.Anchor]);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        var sheet = ctx.GetSheet(_sheetId);
        if (!TextBoxCommandGuards.TryFindTextBox(sheet, _textBoxId, out var textBox)) return;
        textBox.RotationDegrees = _previousRotationDegrees;
        textBox.IsSourceLoaded = _previousIsSourceLoaded;
        _applied = false;
    }

}

public sealed class SetTextBoxColorsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly Guid _textBoxId;
    private readonly CellColor? _fillColor;
    private readonly CellColor? _outlineColor;
    private readonly bool _updateFill;
    private readonly bool _updateOutline;
    private readonly bool? _hasFill;
    private CellColor? _previousFillColor;
    private CellColor? _previousOutlineColor;
    private WorkbookThemeColorReference? _previousFillThemeColor;
    private WorkbookThemeColorReference? _previousOutlineThemeColor;
    private bool _previousHasFill;
    private bool _previousIsSourceLoaded;
    private bool _applied;

    public string Label => "Text Box Colors";

    public SetTextBoxColorsCommand(
        SheetId sheetId,
        Guid textBoxId,
        CellColor? fillColor,
        CellColor? outlineColor,
        bool updateFill = true,
        bool updateOutline = true,
        bool? hasFill = null)
    {
        _sheetId = sheetId;
        _textBoxId = textBoxId;
        _fillColor = fillColor;
        _outlineColor = outlineColor;
        _updateFill = updateFill;
        _updateOutline = updateOutline;
        _hasFill = hasFill;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (!TextBoxCommandGuards.TryFindTextBox(sheet, _textBoxId, out var textBox))
            return TextBoxCommandGuards.TextBoxNotFound();

        // R112-model-drawing-object-lock-1-1: layer in the per-text-box Locked override so an
        // author-unlocked text box's colors stay editable even while the sheet blocks "Edit objects".
        if (TextBoxCommandGuards.RejectIfEditObjectsBlocked(sheet, textBox) is { } protectedOutcome)
            return protectedOutcome;

        _previousFillColor = textBox.FillColor;
        _previousOutlineColor = textBox.OutlineColor;
        _previousFillThemeColor = textBox.FillThemeColor;
        _previousOutlineThemeColor = textBox.OutlineThemeColor;
        _previousHasFill = textBox.HasFill;
        _previousIsSourceLoaded = textBox.IsSourceLoaded;
        if (_updateFill)
        {
            textBox.HasFill = _hasFill ?? (_fillColor is not null);
            textBox.FillColor = _fillColor;
            textBox.FillThemeColor = null;
        }

        if (_updateOutline)
        {
            textBox.OutlineColor = _outlineColor;
            textBox.OutlineThemeColor = null;
        }

        // R62-io-drawing-textbox-6-1: mirror DrawingShapeFormatCommands' fix — clear IsSourceLoaded
        // so the full writer emits the edited fill/outline instead of silently discarding it via the
        // preserved source XML passthrough.
        textBox.IsSourceLoaded = false;
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
        textBox.HasFill = _previousHasFill;
        textBox.IsSourceLoaded = _previousIsSourceLoaded;
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
        if (!TextBoxCommandGuards.TryFindTextBox(sheet, _textBoxId, out var textBox))
            return TextBoxCommandGuards.TextBoxNotFound();

        // R111-model-drawing-object-lock-1-1: layer in the per-text-box Locked override so an
        // author-unlocked text box stays movable even while the sheet blocks "Edit objects".
        if (TextBoxCommandGuards.RejectIfEditObjectsBlocked(sheet, textBox) is { } protectedOutcome)
            return protectedOutcome;
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

    /// <summary>
    /// R111-model-drawing-object-lock-1-1: same sheet-level "Edit objects" protection check as
    /// <see cref="RejectIfEditObjectsBlocked(Sheet)"/>, but layers in the per-text-box
    /// <see cref="TextBoxModel.Locked"/> flag -- mirrors
    /// <see cref="DrawingShapeCommandGuards.RejectIfEditObjectsBlocked(Sheet, DrawingShapeModel)"/>: an
    /// author-unlocked text box (<c>Locked == false</c>) stays movable/resizable even while the sheet
    /// is protected with "Edit objects" blocked, matching Excel's per-object Locked checkbox. A locked
    /// text box (the default) is rejected exactly like the sheet-only overload.
    /// </summary>
    public static CommandOutcome? RejectIfEditObjectsBlocked(Sheet sheet, TextBoxModel textBox) =>
        textBox.Locked ? RejectIfEditObjectsBlocked(sheet) : null;

    public static CommandOutcome? RejectInvalidSize(double width, double height) =>
        double.IsFinite(width) && double.IsFinite(height) && width > 0 && height > 0
            ? null
            : new CommandOutcome(false, InvalidTextBoxSizeMessage);

    public static bool TryFindTextBox(
        Sheet sheet,
        Guid textBoxId,
        [NotNullWhen(true)] out TextBoxModel? textBox)
    {
        textBox = TextBoxModel.FindById(sheet.TextBoxes, textBoxId);
        return textBox is not null;
    }

    public static CommandOutcome TextBoxNotFound() =>
        new(false, TextBoxNotFoundMessage);
}
