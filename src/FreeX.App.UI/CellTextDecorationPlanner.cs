using FreeX.Core.Model;
using System.Windows;

namespace FreeX.App.UI;

public static class CellTextDecorationPlanner
{
    public static TextDecorationCollection? Build(CellStyle? style)
    {
        if (style is null)
            return null;

        var decorations = new TextDecorationCollection();
        // Single underline: apply via TextDecorations (WPF FormattedText).
        // Double underline: do NOT add a TextDecoration here — the host's DrawCellText draws
        // two manual strokes for double-underline, so adding one here would produce 3 lines total.
        if (style.Underline && !style.DoubleUnderline)
            foreach (var decoration in TextDecorations.Underline)
                decorations.Add(decoration);
        if (style.Strikethrough)
            foreach (var decoration in TextDecorations.Strikethrough)
                decorations.Add(decoration);

        return decorations.Count == 0 ? null : decorations;
    }
}
