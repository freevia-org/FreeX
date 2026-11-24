using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

public sealed partial class DocumentView
{
    // Raw line separators are paragraph-internal line boundaries, not printable glyphs.
    // Measuring a standalone separator can also stall Avalonia's no-glyph formatter path
    // (upstream fixes: AvaloniaUI/Avalonia#21808 and #22119). Use the run's font metrics.
    private static bool IsBodySoftLineBreak(char character) =>
        character is '\r' or '\n' or '\v' or '\u2028' or '\u2029';

    private (double Width, double Height) MeasureBodyTextCell(char character, RunFormatting formatting)
    {
        var isControl = IsBodySoftLineBreak(character) || character == '\t';
        var metrics = BuildForLayout(isControl ? "Ag" : character.ToString(), formatting);
        return (isControl ? 0 : metrics.WidthIncludingTrailingWhitespace, metrics.Height);
    }

    // CRLF may straddle formatting runs; it still consumes two UTF-16 source offsets
    // while advancing one visual line. Hidden controls do not create visible lines.
    private static int BodySoftLineBreakEnd(
        IReadOnlyList<Cell> cells, IReadOnlyList<double> heights, int index) =>
        cells[index].Ch == '\r' && index + 1 < cells.Count
            && cells[index + 1].Ch == '\n' && heights[index + 1] > 0
                ? index + 2
                : index + 1;
}
