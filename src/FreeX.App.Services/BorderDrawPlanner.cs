using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public enum BorderDrawMode
{
    None,
    Draw,
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
        CellColor color,
        Sheet? sheet = null)
    {
        if (mode == BorderDrawMode.None)
            throw new ArgumentException("Border draw mode must be active.", nameof(mode));

        var sheetRange = StyleSelectionRangePlanner.RemapRangeToSheet(range, sheetId);
        return mode == BorderDrawMode.Draw
            ? CreateDrawBorderCommand(sheetId, sheetRange, style, color, sheet)
            : new BorderDrawWorkbookCommand(
                CommandTitle(mode),
                new ApplyStyleCommand(sheetId, sheetRange, CreateDiff(mode, style, color)),
                sheetRange.CellCount);
    }

    public static StyleDiff CreateDiff(BorderDrawMode mode, BorderStyle style, CellColor color) => mode switch
    {
        BorderDrawMode.DrawGrid => BorderShortcutService.GetAllBorderDiff(style, color),
        BorderDrawMode.Erase => BorderShortcutService.GetClearBorderDiff(),
        BorderDrawMode.None => new StyleDiff(),
        BorderDrawMode.Draw => BorderShortcutService.GetAllBorderDiff(style, color),
        _ => ThrowInvalidMode<StyleDiff>(mode)
    };

    public static StyleDiff CreateCellDiff(
        BorderDrawMode mode,
        GridRange range,
        CellAddress address,
        BorderStyle style,
        CellColor color) => mode switch
    {
        BorderDrawMode.Draw => BorderShortcutService.GetOutlineBorderDiff(range, address, style, color),
        BorderDrawMode.DrawGrid => CreateDiff(mode, style, color),
        BorderDrawMode.Erase => CreateDiff(mode, style, color),
        BorderDrawMode.None => new StyleDiff(),
        _ => ThrowInvalidMode<StyleDiff>(mode)
    };

    public static string CommandTitle(BorderDrawMode mode) => mode switch
    {
        BorderDrawMode.Draw => "Draw Border",
        BorderDrawMode.DrawGrid => "Draw Border Grid",
        BorderDrawMode.Erase => "Erase Border",
        BorderDrawMode.None => "Border Draw",
        _ => ThrowInvalidMode<string>(mode)
    };

    private static BorderDrawWorkbookCommand CreateDrawBorderCommand(
        SheetId sheetId,
        GridRange range,
        BorderStyle style,
        CellColor color,
        Sheet? sheet)
    {
        // r164 remediation, dense whole-sheet enumeration: this builds one single-cell
        // ApplyStyleCommand per address, so an unbounded selection asked for up to 17,179,869,184
        // command objects on the synchronous UI thread. The two siblings that build per-cell border
        // commands the same way -- SelectionStyleCommandPlanner.CreateBorderCommands and
        // MainWindow.CellsCommands' CreateBorderCommands -- already clamp through
        // ApplyStyleCommand.StyleOnlyCreateZone ("this prevents creating millions of single-cell
        // commands"); this one never got it. The full range still feeds CreateCellDiff, so
        // outline-vs-inside edge decisions are unchanged.
        var iterRange = sheet is not null
            ? ApplyStyleCommand.StyleOnlyCreateZone(sheet, range) ?? range
            : range;

        var commands = iterRange
            .AllCells()
            .Select(address => (Address: address, Diff: CreateCellDiff(BorderDrawMode.Draw, range, address, style, color)))
            .Where(plan => BorderShortcutService.HasBorderChanges(plan.Diff))
            .Select(plan => (IWorkbookCommand)new ApplyStyleCommand(
                sheetId,
                new GridRange(plan.Address, plan.Address),
                plan.Diff))
            .ToList();

        return new BorderDrawWorkbookCommand(
            CommandTitle(BorderDrawMode.Draw),
            commands.Count == 1
                ? commands[0]
                : new CompositeWorkbookCommand(CommandTitle(BorderDrawMode.Draw), commands),
            commands.Count);
    }

    private static T ThrowInvalidMode<T>(BorderDrawMode mode) =>
        throw new ArgumentOutOfRangeException(nameof(mode), mode, null);

    private sealed class BorderDrawWorkbookCommand : IWorkbookCommand, IEstimatesMemory
    {
        private readonly IWorkbookCommand _inner;
        private readonly int _estimatedBytes;

        public BorderDrawWorkbookCommand(string label, IWorkbookCommand inner, long cellCount)
        {
            Label = label;
            _inner = inner;
            _estimatedBytes = (int)Math.Min(cellCount * 200, int.MaxValue);
        }

        public string Label { get; }

        public int EstimatedBytes => _estimatedBytes;

        public CommandOutcome Apply(ICommandContext ctx) => _inner.Apply(ctx);

        public void Revert(ICommandContext ctx) => _inner.Revert(ctx);
    }
}
