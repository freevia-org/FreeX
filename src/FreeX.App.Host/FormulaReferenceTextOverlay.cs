using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using FreeX.App.Presentation;

namespace FreeX.App.Host;

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
        var segments = FormulaReferenceTextSegmentPlanner.CreateSegments(text, highlights);

        if (segments.Count == 0)
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

        foreach (var segment in segments)
        {
            overlay.Inlines.Add(CreateRun(
                segment.Text,
                segment.PaletteIndex is { } paletteIndex
                    ? brushes[paletteIndex % brushes.Count]
                    : normalBrush));
        }

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
