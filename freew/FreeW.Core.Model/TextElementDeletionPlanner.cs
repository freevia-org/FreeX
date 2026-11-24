using System.Globalization;

namespace FreeW.Core.Model;

public readonly record struct TextElementDeletionRange(int Start, int Length)
{
    public int End => Start + Length;
}

/// <summary>Deletes complete Unicode text elements while preserving unrelated document run metadata.</summary>
public static class TextElementDeletionPlanner
{
    public static TextElementDeletionRange? Plan(string text, int caretOffset, bool backward)
    {
        var offset = Math.Clamp(caretOffset, 0, text.Length);
        if (text.Length == 0 || backward && offset == 0 || !backward && offset == text.Length)
            return null;
        var stops = StringInfo.ParseCombiningCharacters(text);
        var index = Array.BinarySearch(stops, offset);
        int start;
        int end;
        if (index >= 0)
        {
            start = backward ? stops[index - 1] : offset;
            end = backward ? offset : index + 1 < stops.Length ? stops[index + 1] : text.Length;
        }
        else
        {
            var next = ~index;
            start = stops[next - 1];
            end = next < stops.Length ? stops[next] : text.Length;
        }
        return new(start, end - start);
    }

    public static RevisionEditPlanner.DeleteResult Apply(
        Paragraph paragraph,
        TextElementDeletionRange range,
        bool trackChanges,
        string author,
        string? dateXml)
    {
        if (trackChanges)
        {
            var controls = paragraph.Runs.Where(run => run.Control is not null)
                .DistinctBy(run => run.Control, ReferenceEqualityComparer.Instance).ToArray();
            var deletion = RevisionEditPlanner.DeleteRangeAsRevision(paragraph, range.Start, range.End, author, dateXml);
            var boundaries = BookmarkBoundaryMapper.Capture(paragraph);
            foreach (var source in controls.Reverse())
            {
                if (paragraph.Runs.Any(run => ReferenceEquals(run.Control, source.Control)))
                    continue;
                // Taking back an author's own insertion can remove every text run in a
                // control. Keep the empty field, just as an ordinary form-text edit does.
                var index = BookmarkBoundaryMapper.EnsureRunBoundaryAtTextOffset(paragraph, deletion.CaretOffset);
                paragraph.Runs.Insert(index, new Run(string.Empty, source.Formatting) { Control = source.Control });
            }
            BookmarkBoundaryMapper.Restore(paragraph, boundaries);
            return deletion;
        }

        var bookmarks = BookmarkBoundaryMapper.Capture(paragraph);
        var replacement = new List<Run>();
        var position = 0;
        foreach (var run in paragraph.Runs)
        {
            var end = position + run.Text.Length;
            var from = Math.Clamp(range.Start - position, 0, run.Text.Length);
            var to = Math.Clamp(range.End - position, from, run.Text.Length);
            var text = run.Text.Remove(from, to - from);
            if (text.Length > 0 || run.Text.Length == 0 || run.Control is not null)
                replacement.Add(RevisionEditPlanner.CloneRunWithText(run, text));
            position = end;
        }
        paragraph.Runs.Clear();
        paragraph.Runs.AddRange(replacement);
        BookmarkBoundaryMapper.Restore(paragraph, bookmarks,
            offset => offset <= range.Start ? offset : offset < range.End ? range.Start : offset - range.Length);
        return new(range.Start, KeptDeletedText: false);
    }
}
