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
        var formatting = paragraph.Runs.Select(run => ResolveRunFmt(run, paragraph)).ToArray();
        if (!formatting.Any(format => RunCharacterSpacingPlanner.ResolveAdvanceDip(format, PxPerPoint) != 0))
            return;
        var advances = items.Select(item => item.Width).ToArray();
        ApplyCharacterSpacingAdvances(
            new string(items.Select(item => item.Ch).ToArray()),
            index => formatting[items[index].RunIndex],
            advances,
            index => !items[index].Hidden && !items[index].IsHardBreak && items[index].EmbeddedObject is null);
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

    private double MeasureContentAutoFitParagraphWidth(Paragraph paragraph)
    {
        // Keep the existing per-run font measurement, but find text elements across run
        // boundaries: a formatting boundary does not add a new combining/ZWJ character.
        var text = string.Concat(paragraph.Runs.Select(run => run.Text));
        var elements = StringInfo.ParseCombiningCharacters(text);
        var spacedElementStarts = new HashSet<int>();
        for (var index = 0; index < elements.Length; index++)
        {
            var end = index + 1 < elements.Length ? elements[index + 1] : text.Length;
            if (RunCharacterSpacingPlanner.IsSpacedElement(text[elements[index]..end]))
                spacedElementStarts.Add(elements[index]);
        }
        var paragraphOffset = 0;
        var width = 0.0;
        foreach (var run in paragraph.Runs)
        {
            if (string.IsNullOrEmpty(run.Text))
                continue;
            var formatting = ResolveRunFmt(run, paragraph);
            var spacing = RunCharacterSpacingPlanner.ResolveAdvanceDip(formatting, PxPerPoint);
            if (spacing == 0)
            {
                width += BuildForLayout(run.Text, formatting).WidthIncludingTrailingWhitespace;
                paragraphOffset += run.Text.Length;
                continue;
            }

            var lineOffset = 0;
            var runWidth = 0.0;
            foreach (var line in run.Text.Split(['\r', '\n']))
            {
                var spacedElements = 0;
                for (var index = 0; index < line.Length; index++)
                {
                    var position = paragraphOffset + lineOffset + index;
                    if (spacedElementStarts.Contains(position))
                        spacedElements++;
                }
                runWidth = Math.Max(runWidth, Math.Max(0,
                    BuildForLayout(line, formatting).WidthIncludingTrailingWhitespace + spacing * spacedElements));
                lineOffset += line.Length + 1;
            }
            width += runWidth;
            paragraphOffset += run.Text.Length;
        }
        return width;
    }
}
