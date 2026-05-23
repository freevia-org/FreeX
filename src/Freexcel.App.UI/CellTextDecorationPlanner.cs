using Freexcel.Core.Model;
using System.Windows;

namespace Freexcel.App.UI;

public static class CellTextDecorationPlanner
{
    public static TextDecorationCollection? Build(CellStyle? style)
    {
        if (style is null)
            return null;

        var decorations = new TextDecorationCollection();
        if (style.Underline || style.DoubleUnderline)
            foreach (var decoration in TextDecorations.Underline)
                decorations.Add(decoration);
        if (style.Strikethrough)
            foreach (var decoration in TextDecorations.Strikethrough)
                decorations.Add(decoration);

        return decorations.Count == 0 ? null : decorations;
    }
}
