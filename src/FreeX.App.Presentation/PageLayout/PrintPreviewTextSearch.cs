namespace FreeX.App.Presentation.PageLayout;

/// <summary>A match in a preview paint instruction. All indices, including the UTF-16 text offset, are zero-based.</summary>
public readonly record struct PrintPreviewTextMatch(int PageIndex, int InstructionIndex, int Start, int Length);

public static class PrintPreviewTextSearch
{
    /// <summary>
    /// Finds a text match starting on the displayed page and wrapping after the last page in the search direction.
    /// A valid previous match on the displayed page resumes before or after that match. If no other hit is found,
    /// wrapping returns the previous match.
    /// </summary>
    public static PrintPreviewTextMatch? Find(
        int pageCount,
        int currentPage,
        Func<int, PrintPreviewPagePainting?> buildPage,
        string query,
        PrintPreviewTextMatch? previousMatch,
        bool backward)
    {
        ArgumentNullException.ThrowIfNull(buildPage);
        if (pageCount <= 0 || string.IsNullOrEmpty(query))
            return null;

        var startPage = Math.Clamp(currentPage, 0, pageCount - 1);
        var startPainting = buildPage(startPage);
        var cursor = GetValidCursor(startPage, startPainting, query, previousMatch);
        var match = FindOnPage(startPage, startPainting, query, backward, cursor);
        if (match is not null)
            return match;

        var pageIndex = startPage;
        for (var pagesVisited = 1; pagesVisited < pageCount; pagesVisited++)
        {
            pageIndex = NextPageIndex(pageIndex, pageCount, backward);
            match = FindOnPage(pageIndex, buildPage(pageIndex), query, backward);
            if (match is not null)
                return match;
        }

        return cursor is null
            ? null
            : FindOnPage(startPage, startPainting, query, backward, cursor, wrapped: true) ?? cursor;
    }

    private static PrintPreviewTextMatch? GetValidCursor(
        int pageIndex,
        PrintPreviewPagePainting? painting,
        string query,
        PrintPreviewTextMatch? previousMatch)
    {
        if (previousMatch is not { } cursor ||
            cursor.PageIndex != pageIndex ||
            cursor.Length != query.Length ||
            cursor.Start < 0 ||
            painting is null ||
            cursor.InstructionIndex < 0 ||
            cursor.InstructionIndex >= painting.Instructions.Count)
            return null;

        var instruction = painting.Instructions[cursor.InstructionIndex];
        return instruction.Kind == PrintPreviewPaintKind.Text &&
            instruction.Text is { } text &&
            cursor.Start <= text.Length - query.Length &&
            text.AsSpan(cursor.Start, query.Length).Equals(query.AsSpan(), StringComparison.OrdinalIgnoreCase)
            ? cursor
            : null;
    }

    private static PrintPreviewTextMatch? FindOnPage(
        int pageIndex,
        PrintPreviewPagePainting? painting,
        string query,
        bool backward,
        PrintPreviewTextMatch? cursor = null,
        bool wrapped = false)
    {
        if (painting?.Instructions is not { Count: > 0 } instructions)
            return null;

        var first = backward ? instructions.Count - 1 : 0;
        var last = backward ? 0 : instructions.Count - 1;
        if (cursor is { } anchor)
        {
            if (wrapped)
                last = anchor.InstructionIndex;
            else
                first = anchor.InstructionIndex;
        }

        var step = backward ? -1 : 1;
        for (var index = first; index != last + step; index += step)
        {
            var instruction = instructions[index];
            if (instruction.Kind != PrintPreviewPaintKind.Text || string.IsNullOrEmpty(instruction.Text))
                continue;

            var text = instruction.Text.AsSpan();
            var offset = 0;
            if (cursor is { } run && run.InstructionIndex == index)
            {
                // The first pass searches one side of the previous hit. On wrap, search the other.
                if (backward == wrapped)
                {
                    offset = run.Start + run.Length;
                    text = text[offset..];
                }
                else
                {
                    text = text[..run.Start];
                }
            }

            var start = backward
                ? text.LastIndexOf(query.AsSpan(), StringComparison.OrdinalIgnoreCase)
                : text.IndexOf(query.AsSpan(), StringComparison.OrdinalIgnoreCase);
            if (start >= 0)
                return new PrintPreviewTextMatch(pageIndex, index, offset + start, query.Length);
        }

        return null;
    }

    private static int NextPageIndex(int pageIndex, int pageCount, bool backward)
    {
        if (backward)
            return pageIndex == 0 ? pageCount - 1 : pageIndex - 1;

        return pageIndex == pageCount - 1 ? 0 : pageIndex + 1;
    }
}
