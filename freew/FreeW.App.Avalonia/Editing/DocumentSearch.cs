using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

/// <summary>
/// Pure, UI-free document text search used by the Find bar. Scans top-level paragraphs (tables and
/// other blocks are skipped) by their plain text, case-insensitive, starting just after a position and
/// wrapping around to the start. Kept separate from DocumentView so it is unit-testable headlessly.
/// </summary>
internal static class DocumentSearch
{
    internal readonly record struct Match(int Block, int Start, int Length);

    public static Match? FindNext(TextDocument document, string query, int fromBlock, int fromOffset)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrEmpty(query))
            return null;

        var blocks = document.Blocks;
        var count = blocks.Count;
        if (count == 0)
            return null;

        // Visit every block once, starting at fromBlock, wrapping around.
        for (var step = 0; step <= count; step++)
        {
            var index = (fromBlock + step) % count;
            if (blocks[index] is not Paragraph paragraph)
                continue;

            var text = paragraph.PlainText;
            // On the very first block, search after the cursor; on the wrap-around pass over the
            // same block, search from the beginning so an earlier match is still found.
            var startAt = step == 0 ? Math.Clamp(fromOffset, 0, text.Length) : 0;
            var found = text.IndexOf(query, startAt, StringComparison.OrdinalIgnoreCase);
            if (found >= 0)
                return new Match(index, found, query.Length);

            // Wrap within the start block: if we began mid-paragraph and found nothing after the
            // cursor, allow a match before the cursor on the final wrap step.
            if (step == count && startAt > 0)
            {
                found = text.IndexOf(query, 0, StringComparison.OrdinalIgnoreCase);
                if (found >= 0 && found < fromOffset)
                    return new Match(index, found, query.Length);
            }
        }

        return null;
    }
}
