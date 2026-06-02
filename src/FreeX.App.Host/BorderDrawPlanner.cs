using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public enum BorderDrawMode
{
    None,
    DrawGrid,
    Erase
}

public static class BorderDrawPlanner
{
    public static IWorkbookCommand CreateCommand(
        SheetId sheetId,
        GridRange range,
        BorderDrawMode mode,
        BorderStyle style,
        CellColor color)
    {
        if (mode == BorderDrawMode.None)
            throw new ArgumentException("Border draw mode must be active.", nameof(mode));

        var sheetRange = GroupedSheetRangePlanner.RemapRangeToSheet(range, sheetId);
        return new BorderDrawWorkbookCommand(
            CommandTitle(mode),
            sheetId,
            sheetRange,
            CreateDiff(mode, style, color));
    }

    public static StyleDiff CreateDiff(BorderDrawMode mode, BorderStyle style, CellColor color) => mode switch
    {
        BorderDrawMode.DrawGrid => BorderShortcutService.GetAllBorderDiff(style, color),
        BorderDrawMode.Erase => BorderShortcutService.GetClearBorderDiff(),
        BorderDrawMode.None => new StyleDiff(),
        _ => ThrowInvalidMode<StyleDiff>(mode)
    };

    public static string CommandTitle(BorderDrawMode mode) => mode switch
    {
        BorderDrawMode.DrawGrid => "Draw Border Grid",
        BorderDrawMode.Erase => "Erase Border",
        BorderDrawMode.None => "Border Draw",
        _ => ThrowInvalidMode<string>(mode)
    };

    private static T ThrowInvalidMode<T>(BorderDrawMode mode) =>
        throw new ArgumentOutOfRangeException(nameof(mode), mode, null);

    private sealed class BorderDrawWorkbookCommand : IWorkbookCommand, IEstimatesMemory
    {
        private readonly ApplyStyleCommand _inner;

        public BorderDrawWorkbookCommand(string label, SheetId sheetId, GridRange range, StyleDiff diff)
        {
            Label = label;
            _inner = new ApplyStyleCommand(sheetId, range, diff);
        }

        public string Label { get; }

        public int EstimatedBytes => _inner.EstimatedBytes;

        public CommandOutcome Apply(ICommandContext ctx) => _inner.Apply(ctx);

        public void Revert(ICommandContext ctx) => _inner.Revert(ctx);
    }
}
