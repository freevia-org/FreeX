using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// r238: the shared form of the r237 comparison -- "did the writes this command made leave the sheet
/// as it found it", answered from the undo snapshots the command already keeps.
/// <para>
/// It takes the snapshot lists rather than reading the command, because those lists are the point:
/// a command's undo snapshots are by construction the complete record of what it writes, so
/// consulting all of them is exactly what makes the answer complete. Passing fewer would narrow the
/// question silently, which is why
/// <c>R237_NoOpDecisionUsesEverySnapshotContractTests</c> checks that a command's decision mentions
/// every snapshot field it declares -- a check that works equally well when the decision is a call
/// to this helper.
/// </para>
/// </summary>
internal static class CellWriteSnapshots
{
    internal static bool NothingChanged(
        Sheet sheet,
        IReadOnlyList<(CellAddress Address, Cell? OldCell, StyleId? OldStyleOnly)>? cells,
        IReadOnlyList<(CellAddress Address, bool HadTarget, string? Target, bool HadMetadata, HyperlinkMetadata? Metadata)>? hyperlinks,
        IReadOnlyList<(CellAddress Address, bool HadRuns, IReadOnlyList<CellTextRun>? Runs)>? richTextRuns,
        IReadOnlyList<(CellAddress Address, bool HadPhoneticGuide, CellPhoneticGuide? PhoneticGuide)>? phoneticGuides,
        IReadOnlyList<(CellAddress Address, bool HadComment, string? Comment, bool HadCommentAuthor, string? CommentAuthor, bool HadShown, bool HadThreadedComment, ThreadedComment? ThreadedComment)>? comments)
    {
        if (cells is not null)
        {
            foreach (var entry in cells)
            {
                if (!CellEditCompanionSnapshot.SameCellOrAbsent(sheet, entry.Address, entry.OldCell))
                    return false;

                if (entry.OldCell is null
                    && !Nullable.Equals(entry.OldStyleOnly, sheet.GetStyleOnly(entry.Address.Row, entry.Address.Col)))
                {
                    return false;
                }
            }
        }

        if (hyperlinks is not null)
        {
            foreach (var entry in hyperlinks)
            {
                if (!SameEntry(sheet.Hyperlinks, entry.Address, entry.HadTarget, entry.Target)
                    || !SameEntry(sheet.HyperlinkMetadata, entry.Address, entry.HadMetadata, entry.Metadata))
                {
                    return false;
                }
            }
        }

        if (richTextRuns is not null)
        {
            foreach (var entry in richTextRuns)
            {
                if (!SameEntry(sheet.RichTextRuns, entry.Address, entry.HadRuns, entry.Runs))
                    return false;
            }
        }

        if (phoneticGuides is not null)
        {
            foreach (var entry in phoneticGuides)
            {
                if (!SameEntry(sheet.CellPhoneticGuides, entry.Address, entry.HadPhoneticGuide, entry.PhoneticGuide))
                    return false;
            }
        }

        if (comments is null)
            return true;

        foreach (var entry in comments)
        {
            if (!SameEntry(sheet.Comments, entry.Address, entry.HadComment, entry.Comment)
                || !SameEntry(sheet.CommentAuthors, entry.Address, entry.HadCommentAuthor, entry.CommentAuthor)
                || entry.HadShown != sheet.ShownComments.Contains(entry.Address)
                || !SameEntry(sheet.ThreadedComments, entry.Address, entry.HadThreadedComment, entry.ThreadedComment))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameEntry<T>(
        IDictionary<CellAddress, T> entries,
        CellAddress address,
        bool had,
        T? captured)
    {
        var present = entries.TryGetValue(address, out var live);
        if (present != had)
            return false;

        return !present || EqualityComparer<T?>.Default.Equals(captured, live);
    }
}
