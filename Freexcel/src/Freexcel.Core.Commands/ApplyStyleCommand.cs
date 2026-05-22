using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>
/// Applies a partial style override to every cell in a range.
/// Only non-null StyleDiff fields are changed; others are preserved.
/// </summary>
public sealed class ApplyStyleCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly StyleDiff _diff;
    private List<(CellAddress Address, Cell? OldCell, StyleId? OldStyleOnly)>? _snapshot;

    public string Label => "Apply Style";

    public ApplyStyleCommand(SheetId sheetId, GridRange range, StyleDiff diff)
    {
        _sheetId = sheetId;
        _range   = range;
        _diff    = diff;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;
        if (StyleDiffValidator.Validate(_diff) is { } validationOutcome)
            return validationOutcome;

        _snapshot = [];

        foreach (var addr in _range.AllCells())
        {
            var cell = sheet.GetCell(addr);

            if (cell is null)
            {
                _snapshot.Add((addr, null, sheet.GetStyleOnly(addr.Row, addr.Col)));

                var baseStyle  = ctx.Workbook.GetStyle(StyleId.Default);
                var newStyle   = _diff.ApplyTo(baseStyle);
                var newStyleId = ctx.Workbook.RegisterStyle(newStyle);
                sheet.SetStyleOnly(addr.Row, addr.Col, newStyleId);
            }
            else
            {
                _snapshot.Add((addr, cell.Clone(), null));

                var baseStyle = ctx.Workbook.GetStyle(cell.StyleId);
                var newStyle  = _diff.ApplyTo(baseStyle);
                cell.StyleId  = ctx.Workbook.RegisterStyle(newStyle);
            }
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null) return;
        var sheet = ctx.GetSheet(_sheetId);
        foreach (var (addr, oldCell, oldStyleOnly) in _snapshot)
        {
            if (oldCell is null)
            {
                if (oldStyleOnly.HasValue)
                    sheet.SetStyleOnly(addr.Row, addr.Col, oldStyleOnly.Value);
                else
                    sheet.ClearStyleOnly(addr.Row, addr.Col);
            }
            else
            {
                sheet.SetCell(addr, oldCell.Clone());
            }
        }
    }
}

internal static class StyleDiffValidator
{
    public static CommandOutcome? Validate(StyleDiff diff)
    {
        if (diff.HAlign is { } hAlign && !Enum.IsDefined(hAlign))
            return new CommandOutcome(false, "Horizontal alignment is not supported.");
        if (diff.VAlign is { } vAlign && !Enum.IsDefined(vAlign))
            return new CommandOutcome(false, "Vertical alignment is not supported.");
        if (diff.FontSize is { } fontSize && !IsSupportedFontSize(fontSize))
            return new CommandOutcome(false, "Font size is not supported.");
        if (diff.TextRotation is { } rotation && !IsSupportedTextRotation(rotation))
            return new CommandOutcome(false, "Text rotation is not supported.");
        if (diff.FillPatternStyle is { } fillPatternStyle && !Enum.IsDefined(fillPatternStyle))
            return new CommandOutcome(false, "Fill pattern style is not supported.");
        if (HasInvalidBorderStyle(diff.BorderTop) ||
            HasInvalidBorderStyle(diff.BorderRight) ||
            HasInvalidBorderStyle(diff.BorderBottom) ||
            HasInvalidBorderStyle(diff.BorderLeft))
            return new CommandOutcome(false, "Border style is not supported.");

        return null;
    }

    private static bool IsSupportedTextRotation(int rotation) =>
        rotation == 255 || rotation is >= -90 and <= 90;

    private static bool IsSupportedFontSize(double fontSize) =>
        double.IsFinite(fontSize) && fontSize is >= 1 and <= 409;

    private static bool HasInvalidBorderStyle(CellBorder? border) =>
        border is { } value && !Enum.IsDefined(value.Style);
}
