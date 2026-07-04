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

    // ── Address-keyed HashSet shifting (e.g. Sheet.ShownComments) ─────────────
    // Same shift semantics as ShiftCommentRows*/ShiftCommentColumns* above, but for a
    // HashSet<CellAddress> rather than a Dictionary<CellAddress, TValue> — used for
    // Sheet.ShownComments (which cell's legacy note box is pinned open), which must move
    // in lockstep with Sheet.Comments/Sheet.CommentAuthors on every structural edit.

    internal static void ShiftCommentSetRowsUp(HashSet<CellAddress> addresses, uint start, uint count)
    {
        List<CellAddress>? shifted = null;
        foreach (var addr in addresses)
        {
            if (addr.Row >= start)
                (shifted ??= new List<CellAddress>(addresses.Count)).Add(addr);
        }

        if (shifted is null)
            return;

        foreach (var addr in shifted)
            addresses.Remove(addr);
        foreach (var addr in shifted)
            addresses.Add(new CellAddress(addr.Sheet, addr.Row + count, addr.Col));
    }

    internal static void ShiftCommentSetRowsDown(HashSet<CellAddress> addresses, uint start, uint count)
    {
        var end = start + count - 1;
        List<CellAddress>? removed = null;
        List<CellAddress>? shifted = null;
        foreach (var addr in addresses)
        {
            if (addr.Row > end)
                (shifted ??= new List<CellAddress>(addresses.Count)).Add(addr);
            else if (addr.Row >= start)
                (removed ??= []).Add(addr);
        }

        if (removed is not null)
        {
            foreach (var addr in removed)
                addresses.Remove(addr);
        }
        if (shifted is not null)
        {
            foreach (var addr in shifted)
                addresses.Remove(addr);
            foreach (var addr in shifted)
                addresses.Add(new CellAddress(addr.Sheet, addr.Row - count, addr.Col));
        }
    }

    internal static void ShiftCommentSetColumnsUp(HashSet<CellAddress> addresses, uint start, uint count)
    {
        List<CellAddress>? shifted = null;
        foreach (var addr in addresses)
        {
            if (addr.Col >= start)
                (shifted ??= new List<CellAddress>(addresses.Count)).Add(addr);
        }

        if (shifted is null)
            return;

        foreach (var addr in shifted)
            addresses.Remove(addr);
        foreach (var addr in shifted)
            addresses.Add(new CellAddress(addr.Sheet, addr.Row, addr.Col + count));
    }

    internal static void ShiftCommentSetColumnsDown(HashSet<CellAddress> addresses, uint start, uint count)
    {
        var end = start + count - 1;
        List<CellAddress>? removed = null;
        List<CellAddress>? shifted = null;
        foreach (var addr in addresses)
        {
            if (addr.Col > end)
                (shifted ??= new List<CellAddress>(addresses.Count)).Add(addr);
            else if (addr.Col >= start)
                (removed ??= []).Add(addr);
        }

        if (removed is not null)
        {
            foreach (var addr in removed)
                addresses.Remove(addr);
        }
        if (shifted is not null)
        {
            foreach (var addr in shifted)
                addresses.Remove(addr);
            foreach (var addr in shifted)
                addresses.Add(new CellAddress(addr.Sheet, addr.Row, addr.Col - count));
        }
    }

    internal static List<CellAddress>? CaptureAddressSet(HashSet<CellAddress> source)
    {
        if (source.Count == 0)
            return null;

        var snapshot = new List<CellAddress>(source.Count);
        foreach (var addr in source)
            snapshot.Add(addr);

        return snapshot;
    }

    internal static void RestoreAddressSet(HashSet<CellAddress> target, IReadOnlyList<CellAddress>? snapshot)
    {
        if (snapshot is null)
            return;

        target.Clear();
        foreach (var addr in snapshot)
            target.Add(addr);
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
