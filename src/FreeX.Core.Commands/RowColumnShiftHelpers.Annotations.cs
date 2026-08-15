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
    private static void ShiftHyperlinkBookmarksOnSheet(
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
            {
                // No Bookmark recorded — FreeX's own Insert Hyperlink dialog stores the
                // sheet-qualified ref straight into sheet.Hyperlinks[addr] and leaves
                // Bookmark unset (see SheetCommands.cs RenameSheetCommand for the same
                // fallback). That raw target string is what HyperlinkNavigationPlanner and
                // CreateXlsxHyperlink actually read, so it must be shifted here too.
                ShiftRawHyperlinkTarget(sheet, pair.Key, op, affectedSheetName);
                continue;
            }

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

    /// <summary>
    /// Rewrites the raw sheet-qualified target string in <see cref="Sheet.Hyperlinks"/>[<paramref name="addr"/>]
    /// for a 'Place in This Document' hyperlink whose <see cref="HyperlinkMetadata.Bookmark"/> is empty — the
    /// representation FreeX's own Insert Hyperlink dialog produces when the user types a ref directly into the
    /// address field instead of using the Bookmark picker. Mutates <see cref="Sheet.Hyperlinks"/> in place; safe
    /// to call mid-iteration of <see cref="Sheet.HyperlinkMetadata"/> since it never touches that dictionary.
    /// </summary>
    private static void ShiftRawHyperlinkTarget(
        Sheet sheet, CellAddress addr, RewriteOperation op, string affectedSheetName)
    {
        if (!sheet.Hyperlinks.TryGetValue(addr, out var target) || string.IsNullOrEmpty(target))
            return;

        // The target is in the form "SheetName!CellRef" (or 'Sheet Name'!CellRef). If there is
        // no '!' it is EITHER a named range reference (leave alone) OR a bare cell/range reference
        // implicitly relative to whichever sheet hosts the hyperlink -- FreeX's own Insert
        // Hyperlink dialog stores exactly that when the user types e.g. "B10" into the address box
        // for a same-sheet "Place in This Document" link (see SetHyperlinkCommand). That bare form
        // must still shift when the structural edit lands on the hyperlink's OWN sheet, or the
        // hover tooltip/navigation keeps pointing at the pre-shift row/column once rows or columns
        // are inserted/deleted above or before it -- previously this returned unconditionally,
        // silently leaving every unqualified same-sheet hyperlink target stale.
        var bangIndex = target.IndexOf('!', StringComparison.Ordinal);
        if (bangIndex < 0)
        {
            if (!string.Equals(sheet.Name, affectedSheetName, StringComparison.OrdinalIgnoreCase))
                return;

            var rewrittenBare = FormulaRewriter.Rewrite(target, op, affectedSheetName);
            if (rewrittenBare is null || rewrittenBare == target)
                return;

            sheet.Hyperlinks[addr] = rewrittenBare;
            return;
        }

        var sheetPart = target[..bangIndex].Trim('\'');
        if (!string.Equals(sheetPart, affectedSheetName, StringComparison.OrdinalIgnoreCase))
            return;

        var rewritten = FormulaRewriter.Rewrite(target, op, affectedSheetName);
        if (rewritten is null || rewritten == target)
            return;

        sheet.Hyperlinks[addr] = rewritten;
    }

    /// <summary>
    /// One captured "other sheet" hyperlink change from <see cref="ShiftHyperlinkBookmarks"/> —
    /// exactly one of <see cref="OldBookmark"/> / <see cref="OldTarget"/> is non-null, matching
    /// which of the two representations (Bookmark-populated vs. raw sheet.Hyperlinks target) the
    /// hyperlink at <see cref="Address"/> used before the shift.
    /// </summary>
    internal readonly record struct HyperlinkOtherSheetChange(
        SheetId Sheet, CellAddress Address, string? OldBookmark, string? OldTarget);

    /// <summary>
    /// Rewrites 'Place in This Document' hyperlink bookmarks (and, when Bookmark is empty, the
    /// raw sheet.Hyperlinks target string) that target <paramref name="affectedSheetName"/> across
    /// EVERY sheet in the workbook — a hyperlink lives on whichever sheet it was inserted on, which
    /// may differ from the sheet the structural edit (<paramref name="op"/>) happened on (e.g. a
    /// bookmark on Sheet2 can read "Sheet1!A10"). Mirrors the all-sheets sweep
    /// RenameSheetCommand performs for the same reason. <paramref name="editedSheet"/>
    /// is rewritten in place (its dictionaries are already snapshotted by the caller for
    /// undo); every OTHER sheet's changes are captured into a returned snapshot list
    /// so the caller can restore them on <c>Revert</c>.
    /// </summary>
    internal static List<HyperlinkOtherSheetChange> ShiftHyperlinkBookmarks(
        Workbook workbook, Sheet editedSheet, RewriteOperation op, string affectedSheetName)
    {
        var otherSheetSnapshot = new List<HyperlinkOtherSheetChange>();
        foreach (var sheet in workbook.Sheets)
        {
            if (ReferenceEquals(sheet, editedSheet))
            {
                ShiftHyperlinkBookmarksOnSheet(sheet, op, affectedSheetName);
                continue;
            }

            if (sheet.HyperlinkMetadata.Count == 0)
                continue;

            List<KeyValuePair<CellAddress, string>>? beforeBookmark = null;
            List<KeyValuePair<CellAddress, string>>? beforeTarget = null;
            foreach (var pair in sheet.HyperlinkMetadata)
            {
                if (pair.Value.LinkType != HyperlinkTargetKind.PlaceInThisDocument)
                    continue;

                if (!string.IsNullOrEmpty(pair.Value.Bookmark))
                {
                    (beforeBookmark ??= []).Add(new KeyValuePair<CellAddress, string>(pair.Key, pair.Value.Bookmark));
                }
                else if (sheet.Hyperlinks.TryGetValue(pair.Key, out var target) && !string.IsNullOrEmpty(target))
                {
                    (beforeTarget ??= []).Add(new KeyValuePair<CellAddress, string>(pair.Key, target));
                }
            }

            ShiftHyperlinkBookmarksOnSheet(sheet, op, affectedSheetName);

            if (beforeBookmark is not null)
            {
                foreach (var (addr, oldBookmark) in beforeBookmark)
                {
                    if (sheet.HyperlinkMetadata.TryGetValue(addr, out var meta) &&
                        !string.Equals(meta.Bookmark, oldBookmark, StringComparison.Ordinal))
                    {
                        otherSheetSnapshot.Add(new HyperlinkOtherSheetChange(sheet.Id, addr, oldBookmark, null));
                    }
                }
            }

            if (beforeTarget is not null)
            {
                foreach (var (addr, oldTarget) in beforeTarget)
                {
                    if (sheet.Hyperlinks.TryGetValue(addr, out var newTarget) &&
                        !string.Equals(newTarget, oldTarget, StringComparison.Ordinal))
                    {
                        otherSheetSnapshot.Add(new HyperlinkOtherSheetChange(sheet.Id, addr, null, oldTarget));
                    }
                }
            }
        }

        return otherSheetSnapshot;
    }

    /// <summary>
    /// Restores bookmarks/targets captured by the "other sheets" portion of
    /// <see cref="ShiftHyperlinkBookmarks(Workbook, Sheet, RewriteOperation, string)"/> on undo.
    /// </summary>
    internal static void RestoreHyperlinkBookmarks(
        Workbook workbook, List<HyperlinkOtherSheetChange>? snapshot)
    {
        if (snapshot is null || snapshot.Count == 0)
            return;

        foreach (var change in snapshot)
        {
            var sheet = workbook.Sheets.FirstOrDefault(s => s.Id == change.Sheet);
            if (sheet is null)
                continue;

            if (change.OldBookmark is not null)
            {
                if (sheet.HyperlinkMetadata.TryGetValue(change.Address, out var meta))
                    sheet.HyperlinkMetadata[change.Address] = meta with { Bookmark = change.OldBookmark };
            }
            else if (change.OldTarget is not null)
            {
                sheet.Hyperlinks[change.Address] = change.OldTarget;
            }
        }
    }
}
