using System.Globalization;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

public sealed partial class DocumentView
{
    private void ApplyCharacterSpacingAdvances(IReadOnlyList<Cell> cells, double[] advances)
    {
        if (!cells.Any(cell => RunCharacterSpacingPlanner.ResolveAdvanceDip(cell.Fmt, PxPerPoint) != 0))
            return;
        var review = CurrentReviewDisplayPolicy;
        ApplyCharacterSpacingAdvances(
            new string(cells.Select(cell => cell.Ch).ToArray()),
            index => cells[index].Fmt,
            advances,
            index => !cells[index].IsPageBreak && !cells[index].IsColumnBreak
                && !IsBodySoftLineBreak(cells[index].Ch)
                && cells[index].EquationElement is null && !IsTextHiddenInCurrentView(cells[index].Fmt)
                && review.RevisionDecision(cells[index].Revision).IsTextVisible);
    }

    private void ApplyTableCharacterSpacing(Paragraph paragraph, List<CellWrappedItem> items)
    {
        if (!items.Any(item => RunCharacterSpacingPlanner.ResolveAdvanceDip(item.Formatting, PxPerPoint) != 0))
            return;
        var advances = items.Select(item => item.Width).ToArray();
        ApplyCharacterSpacingAdvances(
            new string(items.Select(item => item.Ch).ToArray()),
            index => items[index].Formatting,
            advances,
            index => !items[index].Hidden && !items[index].IsHardBreak && items[index].EmbeddedObject is null
                && items[index].EquationElement is null);
        for (var index = 0; index < items.Count; index++)
            items[index] = items[index] with { Width = advances[index] };
    }

    private static void ApplyCharacterSpacingAdvances(
        string text,
        Func<int, RunFormatting> formatting,
        double[] advances,
        Func<int, bool> canSpace)
    {
        var elements = StringInfo.ParseCombiningCharacters(text);
        for (var element = 0; element < elements.Length; element++)
        {
            var start = elements[element];
            var end = element + 1 < elements.Length ? elements[element + 1] : text.Length;
            if (!RunCharacterSpacingPlanner.IsSpacedElement(text[start..end]))
                continue;
            var visible = true;
            for (var index = start; index < end; index++)
                visible &= canSpace(index);
            if (!visible)
                continue;

            var spacing = RunCharacterSpacingPlanner.ResolveAdvanceDip(formatting(start), PxPerPoint);
            if (spacing >= 0)
            {
                // Put the advance after the complete grapheme, never between surrogate halves
                // or between a base letter and its combining marks.
                advances[end - 1] += spacing;
                continue;
            }

            // Condensation can overlap glyph ink, but an advance must not become negative:
            // wrapping, selection rectangles and caret positions all consume these widths.
            var remaining = -spacing;
            for (var index = end - 1; index >= start && remaining > 0; index--)
            {
                var reduction = Math.Min(Math.Max(0, advances[index]), remaining);
                advances[index] -= reduction;
                remaining -= reduction;
            }
        }
    }

    private double MeasureContentAutoFitParagraphWidth(int blockIndex, Paragraph paragraph)
    {
        var maximum = 0.0;
        var lineWidth = 0.0;
        foreach (var item in BuildCellDisplayItems(blockIndex, paragraph))
        {
            if (item.IsHardBreak)
            {
                maximum = Math.Max(maximum, lineWidth);
                lineWidth = 0;
            }
            else
                lineWidth += item.Width;
        }
        return Math.Max(maximum, lineWidth);
    }
}
