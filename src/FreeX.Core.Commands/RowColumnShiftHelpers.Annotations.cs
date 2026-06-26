using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static partial class RowColumnShiftHelpers
{
    internal static void ShiftCommentRowsUp<TValue>(Dictionary<CellAddress, TValue> comments, uint start, uint count)
    {
        List<KeyValuePair<CellAddress, TValue>>? shifted = null;
        foreach (var pair in comments)
        {
            if (pair.Key.Row >= start)
                (shifted ??= new List<KeyValuePair<CellAddress, TValue>>(comments.Count)).Add(pair);
        }

        if (shifted is null)
            return;

        foreach (var (addr, _) in shifted)
            comments.Remove(addr);
        foreach (var (addr, comment) in shifted)
            comments[new CellAddress(addr.Sheet, addr.Row + count, addr.Col)] = comment;
    }

    internal static void ShiftCommentRowsDown<TValue>(Dictionary<CellAddress, TValue> comments, uint start, uint count)
    {
        var end = start + count - 1;
        List<CellAddress>? removed = null;
        List<KeyValuePair<CellAddress, TValue>>? shifted = null;
        foreach (var pair in comments)
        {
            if (pair.Key.Row > end)
                (shifted ??= new List<KeyValuePair<CellAddress, TValue>>(comments.Count)).Add(pair);
            else if (pair.Key.Row >= start)
                (removed ??= []).Add(pair.Key);
        }

        if (removed is not null)
        {
            foreach (var addr in removed)
                comments.Remove(addr);
        }
        if (shifted is not null)
        {
            foreach (var (addr, _) in shifted)
                comments.Remove(addr);
            foreach (var (addr, comment) in shifted)
                comments[new CellAddress(addr.Sheet, addr.Row - count, addr.Col)] = comment;
        }
    }

    internal static void ShiftCommentColumnsUp<TValue>(Dictionary<CellAddress, TValue> comments, uint start, uint count)
    {
        List<KeyValuePair<CellAddress, TValue>>? shifted = null;
        foreach (var pair in comments)
        {
            if (pair.Key.Col >= start)
                (shifted ??= new List<KeyValuePair<CellAddress, TValue>>(comments.Count)).Add(pair);
        }

        if (shifted is null)
            return;

        foreach (var (addr, _) in shifted)
            comments.Remove(addr);
        foreach (var (addr, comment) in shifted)
            comments[new CellAddress(addr.Sheet, addr.Row, addr.Col + count)] = comment;
    }

    internal static void ShiftCommentColumnsDown<TValue>(Dictionary<CellAddress, TValue> comments, uint start, uint count)
    {
        var end = start + count - 1;
        List<CellAddress>? removed = null;
        List<KeyValuePair<CellAddress, TValue>>? shifted = null;
        foreach (var pair in comments)
        {
            if (pair.Key.Col > end)
                (shifted ??= new List<KeyValuePair<CellAddress, TValue>>(comments.Count)).Add(pair);
            else if (pair.Key.Col >= start)
                (removed ??= []).Add(pair.Key);
        }

        if (removed is not null)
        {
            foreach (var addr in removed)
                comments.Remove(addr);
        }
        if (shifted is not null)
        {
            foreach (var (addr, _) in shifted)
                comments.Remove(addr);
            foreach (var (addr, comment) in shifted)
                comments[new CellAddress(addr.Sheet, addr.Row, addr.Col - count)] = comment;
        }
    }

    // ── In-document hyperlink bookmark shifting ───────────────────────────────
    // HyperlinkMetadata.Bookmark for PlaceInThisDocument links stores a cell
    // reference in 'SheetName!CellRef' form (e.g. "Sheet1!A10").  When rows or
    // columns are inserted/deleted on the target sheet the cell ref must be
    // updated — but the source-cell address key is shifted separately by
    // ShiftCommentRows*/ShiftCommentColumns*.
    //
    // Undo is handled automatically: the caller snapshots sheet.HyperlinkMetadata
    // (keys + values) via CaptureDictionary *before* any shift, so RestoreDictionary
    // on undo restores both key addresses and bookmark strings.

    /// <summary>
    /// Rewrites the <see cref="HyperlinkMetadata.Bookmark"/> field of every
    /// PlaceInThisDocument hyperlink on <paramref name="sheet"/> whose bookmark
    /// targets the affected sheet, using the given <see cref="RewriteOperation"/>.
    /// Must be called AFTER the source-cell key shift (ShiftCommentRows*/Columns*)
    /// but before the caller's snapshot is discarded.
    /// </summary>
    internal static void ShiftHyperlinkBookmarks(
        Sheet sheet, RewriteOperation op, string affectedSheetName)
    {
        if (sheet.HyperlinkMetadata.Count == 0)
            return;

        List<KeyValuePair<CellAddress, HyperlinkMetadata>>? changed = null;
        foreach (var pair in sheet.HyperlinkMetadata)
        {
            var meta = pair.Value;
            if (meta.LinkType != HyperlinkTargetKind.PlaceInThisDocument)
                continue;

            var bookmark = meta.Bookmark;
            if (string.IsNullOrEmpty(bookmark))
                continue;

            // Only rewrite bookmarks targeting the affected sheet.
            // The bookmark is in the form "SheetName!CellRef".
            // If there is no '!' the bookmark is a named range — leave it alone.
            var bangIndex = bookmark.IndexOf('!', StringComparison.Ordinal);
            if (bangIndex < 0)
                continue;

            var sheetPart = bookmark[..bangIndex].Trim('\'');
            if (!string.Equals(sheetPart, affectedSheetName, StringComparison.OrdinalIgnoreCase))
                continue;

            // Use FormulaRewriter to shift the cell reference portion.
            // The bookmark body after '!' is a cell reference expression understood
            // by the rewriter (e.g. "A10", "$A$10").
            var rewritten = FormulaRewriter.Rewrite(bookmark, op, affectedSheetName);
            if (rewritten is null || rewritten == bookmark)
                continue;

            (changed ??= new List<KeyValuePair<CellAddress, HyperlinkMetadata>>())
                .Add(new KeyValuePair<CellAddress, HyperlinkMetadata>(
                    pair.Key, meta with { Bookmark = rewritten }));
        }

        if (changed is null) return;
        foreach (var (addr, meta) in changed)
            sheet.HyperlinkMetadata[addr] = meta;
    }
}
