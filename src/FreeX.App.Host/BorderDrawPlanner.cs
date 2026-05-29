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
}
