using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Freexcel.App.Host;

public static class FormulaReferenceTextOverlay
{
    public static void Apply(
        TextBlock overlay,
        string text,
        IReadOnlyList<FormulaReferenceHighlight> highlights,
        IReadOnlyList<Brush> brushes,
        Brush normalBrush,
        bool keepFormulaVisibleWithoutHighlights = false)
    {
        overlay.Inlines.Clear();

        if (!text.StartsWith("=", StringComparison.Ordinal) || highlights.Count == 0)
        {
            if (keepFormulaVisibleWithoutHighlights && text.StartsWith("=", StringComparison.Ordinal))
            {
                overlay.Inlines.Add(CreateRun(text, normalBrush));
                overlay.Visibility = Visibility.Visible;
                return;
            }

            overlay.Visibility = Visibility.Collapsed;
            return;
        }

        var index = 0;
        foreach (var highlight in highlights.OrderBy(h => h.TextStart))
        {
            if (highlight.TextStart < index ||
                highlight.TextStart >= text.Length ||
                highlight.TextLength <= 0)
            {
                continue;
            }

            var highlightEnd = Math.Min(text.Length, highlight.TextStart + highlight.TextLength);
            if (highlight.TextStart > index)
                overlay.Inlines.Add(CreateRun(text[index..highlight.TextStart], normalBrush));

            overlay.Inlines.Add(CreateRun(
                text[highlight.TextStart..highlightEnd],
                brushes[highlight.PaletteIndex % brushes.Count]));
            index = highlightEnd;
        }

        if (index < text.Length)
            overlay.Inlines.Add(CreateRun(text[index..], normalBrush));

        overlay.Visibility = Visibility.Visible;
    }

    public static void Clear(TextBlock? overlay)
    {
        if (overlay is null)
            return;

        overlay.Inlines.Clear();
        overlay.Visibility = Visibility.Collapsed;
    }

    private static Run CreateRun(string text, Brush brush) =>
        new(text) { Foreground = brush };
}
