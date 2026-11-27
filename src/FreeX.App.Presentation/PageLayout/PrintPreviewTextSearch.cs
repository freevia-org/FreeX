using System.Globalization;
using System.Text;

namespace FreeX.App.Presentation.PageLayout;

/// <summary>A match in a preview paint instruction. All indices, including the UTF-16 text offset, are zero-based.</summary>
public readonly record struct PrintPreviewTextMatch(int PageIndex, int InstructionIndex, int Start, int Length);

public static class PrintPreviewTextSearch
{
    /// <summary>
    /// Finds a text match starting on the displayed page and wrapping after the last page in the search direction.
    /// A valid previous match on the displayed page resumes before or after that match. If no other hit is found,
    /// wrapping returns the previous match. <paramref name="options"/> are the find box drop-down options; the
    /// default (all off) is the WPF DocumentViewer default: case-insensitive, diacritic/kashida/alef-hamza
    /// insensitive, matching inside words. A match's length is measured in the searched text, so it can differ
    /// from the query length when ignored marks sit inside the hit.
    /// </summary>
    public static PrintPreviewTextMatch? Find(
        int pageCount,
        int currentPage,
        Func<int, PrintPreviewPagePainting?> buildPage,
        string query,
        PrintPreviewTextMatch? previousMatch,
        bool backward,
        PrintPreviewFindOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(buildPage);
        if (pageCount <= 0 || string.IsNullOrEmpty(query))
            return null;

        var foldedQuery = Fold(query, options).Text;
        if (foldedQuery.Length == 0)
            return null;

        var startPage = Math.Clamp(currentPage, 0, pageCount - 1);
        var startPainting = buildPage(startPage);
        var cursor = GetValidCursor(startPage, startPainting, foldedQuery, previousMatch, options);
        var match = FindOnPage(startPage, startPainting, foldedQuery, backward, options, cursor);
        if (match is not null)
            return match;

        var pageIndex = startPage;
        for (var pagesVisited = 1; pagesVisited < pageCount; pagesVisited++)
        {
            pageIndex = NextPageIndex(pageIndex, pageCount, backward);
            match = FindOnPage(pageIndex, buildPage(pageIndex), foldedQuery, backward, options);
            if (match is not null)
                return match;
        }

        return cursor is null
            ? null
            : FindOnPage(startPage, startPainting, foldedQuery, backward, options, cursor, wrapped: true) ?? cursor;
    }

    private static PrintPreviewTextMatch? GetValidCursor(
        int pageIndex,
        PrintPreviewPagePainting? painting,
        string foldedQuery,
        PrintPreviewTextMatch? previousMatch,
        PrintPreviewFindOptions options)
    {
        if (previousMatch is not { } cursor ||
            cursor.PageIndex != pageIndex ||
            cursor.Length <= 0 ||
            cursor.Start < 0 ||
            painting is null ||
            cursor.InstructionIndex < 0 ||
            cursor.InstructionIndex >= painting.Instructions.Count)
            return null;

        var instruction = painting.Instructions[cursor.InstructionIndex];
        return instruction.Kind == PrintPreviewPaintKind.Text &&
            instruction.Text is { } text &&
            cursor.Start <= text.Length - cursor.Length &&
            string.Equals(Fold(text.Substring(cursor.Start, cursor.Length), options).Text, foldedQuery, StringComparison.Ordinal)
            ? cursor
            : null;
    }

    private static PrintPreviewTextMatch? FindOnPage(
        int pageIndex,
        PrintPreviewPagePainting? painting,
        string foldedQuery,
        bool backward,
        PrintPreviewFindOptions options,
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

            var text = instruction.Text;
            var lower = 0;
            var upper = text.Length;
            if (cursor is { } run && run.InstructionIndex == index)
            {
                // The first pass searches one side of the previous hit. On wrap, search the other.
                if (backward == wrapped)
                    lower = run.Start + run.Length;
                else
                    upper = run.Start;
            }

            if (FindInText(text, foldedQuery, options, backward, lower, upper) is { } hit)
                return new PrintPreviewTextMatch(pageIndex, index, hit.Start, hit.Length);
        }

        return null;
    }

    /// <summary>Finds the first (or last) match lying wholly inside [lower, upper) of <paramref name="text"/>.</summary>
    private static (int Start, int Length)? FindInText(
        string text,
        string foldedQuery,
        PrintPreviewFindOptions options,
        bool backward,
        int lower,
        int upper)
    {
        var folded = Fold(text, options);
        (int Start, int Length)? best = null;
        for (var from = 0; from <= folded.Text.Length - foldedQuery.Length;)
        {
            var at = folded.Text.IndexOf(foldedQuery, from, StringComparison.Ordinal);
            if (at < 0)
                break;

            from = at + 1;
            var end = at + foldedQuery.Length;
            if (options.MatchWholeWord && !IsWholeWord(folded.Text, at, end))
                continue;

            var start = folded.Map[at];
            var length = (end < folded.Text.Length ? folded.Map[end] : text.Length) - start;
            if (start < lower || start + length > upper)
                continue;

            best = (start, length);
            if (!backward)
                break;
        }

        return best;
    }

    // A word edge only needs a boundary where the match itself touches a letter or digit.
    private static bool IsWholeWord(string folded, int start, int end) =>
        (start == 0 || !char.IsLetterOrDigit(folded[start]) || !char.IsLetterOrDigit(folded[start - 1])) &&
        (end == folded.Length || !char.IsLetterOrDigit(folded[end - 1]) || !char.IsLetterOrDigit(folded[end]));

    private readonly record struct FoldedText(string Text, int[] Map);

    /// <summary>
    /// Reduces <paramref name="source"/> to the form compared by the search: ignored marks are dropped and
    /// <c>Map</c> keeps each surviving char's index in the source so hits map back to source offsets.
    /// </summary>
    private static FoldedText Fold(string source, PrintPreviewFindOptions options)
    {
        var builder = new StringBuilder(source.Length);
        var map = new int[source.Length];
        for (var index = 0; index < source.Length; index++)
        {
            var c = source[index];
            if (!options.MatchKashida && c == 'ـ')
                continue;

            var alefVariant = c is 'آ' or 'أ' or 'إ' or 'ٱ' or 'ٲ' or 'ٳ';
            if (alefVariant && !options.MatchAlefHamza)
                c = 'ا';

            // Hangul syllables decompose into jamo, which would make unrelated syllables compare equal. The alef
            // variants are governed by the alef-hamza option alone, so diacritic folding must not strip their hamza.
            if (!options.MatchDiacritic && !alefVariant && c >= 'À' && !char.IsSurrogate(c) && c is not (>= '가' and <= '힣'))
            {
                var category = char.GetUnicodeCategory(c);
                if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.SpacingCombiningMark)
                    continue;

                c = c.ToString().Normalize(NormalizationForm.FormD)[0];
            }

            map[builder.Length] = index;
            builder.Append(options.MatchCase ? c : char.ToUpperInvariant(c));
        }

        return new FoldedText(builder.ToString(), map);
    }

    private static int NextPageIndex(int pageIndex, int pageCount, bool backward)
    {
        if (backward)
            return pageIndex == 0 ? pageCount - 1 : pageIndex - 1;

        return pageIndex == pageCount - 1 ? 0 : pageIndex + 1;
    }
}
