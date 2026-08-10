namespace FreeX.App.Presentation;

public readonly record struct FormulaReferenceTextSegment(string Text, int? PaletteIndex);

public static class FormulaReferenceTextSegmentPlanner
{
    public static IReadOnlyList<FormulaReferenceTextSegment> CreateSegments(
        string text,
        IReadOnlyList<FormulaReferenceHighlight> highlights)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(highlights);

        if (!text.StartsWith("=", StringComparison.Ordinal) || highlights.Count == 0)
            return [];

        var segments = new List<FormulaReferenceTextSegment>();
        var index = 0;
        foreach (var highlight in highlights.OrderBy(static highlight => highlight.TextStart))
        {
            if (highlight.TextStart < index ||
                highlight.TextStart >= text.Length ||
                highlight.TextLength <= 0)
            {
                continue;
            }

            var highlightEnd = Math.Min(text.Length, highlight.TextStart + highlight.TextLength);
            if (highlight.TextStart > index)
                segments.Add(new FormulaReferenceTextSegment(text[index..highlight.TextStart], PaletteIndex: null));

            segments.Add(new FormulaReferenceTextSegment(
                text[highlight.TextStart..highlightEnd],
                highlight.PaletteIndex));
            index = highlightEnd;
        }

        if (index < text.Length)
            segments.Add(new FormulaReferenceTextSegment(text[index..], PaletteIndex: null));

        return segments;
    }
}
