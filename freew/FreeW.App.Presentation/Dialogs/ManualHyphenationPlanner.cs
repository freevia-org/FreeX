using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public sealed record ManualHyphenationOption(int BreakPoint, string DisplayText);

public enum ManualHyphenationDialogAction
{
    Accept,
    Skip,
    Cancel
}

public sealed record ManualHyphenationDialogResult(
    ManualHyphenationDialogAction Action,
    int? BreakPoint = null);

public sealed record ManualHyphenationCandidate(
    int Number,
    string Word,
    IReadOnlyList<ManualHyphenationOption> Options);

/// <summary>
/// A non-mutating manual-hyphenation review session. Hosts walk candidates in document order and record
/// Yes/No choices; accepted choices become one undoable command only after the dialog pass finishes.
/// </summary>
public sealed class ManualHyphenationSession
{
    private sealed record Entry(
        ManualHyphenationCandidate Candidate,
        IReadOnlyDictionary<int, ManualHyphenationEdit> EditsByBreakPoint);

    private readonly IReadOnlyList<Entry> _entries;
    private readonly List<ManualHyphenationEdit> _edits = [];
    private int _index;

    internal ManualHyphenationSession(
        IReadOnlyList<(string Word, IReadOnlyList<(int BreakPoint, ManualHyphenationEdit Edit)> Options)> entries)
    {
        _entries = entries.Select((entry, index) => new Entry(
            new ManualHyphenationCandidate(
                index + 1,
                entry.Word,
                entry.Options.Select(option => new ManualHyphenationOption(
                    option.BreakPoint,
                    entry.Word.Insert(option.BreakPoint, "-"))).ToList()),
            entry.Options.ToDictionary(option => option.BreakPoint, option => option.Edit)))
            .ToList();
    }

    public int CandidateCount => _entries.Count;
    public int AcceptedCount => _edits.Count;
    public bool IsComplete => _index >= _entries.Count;
    public ManualHyphenationCandidate? Current => IsComplete ? null : _entries[_index].Candidate;
    public IReadOnlyList<ManualHyphenationEdit> Edits => _edits;

    public void Accept(int breakPoint)
    {
        if (IsComplete)
            throw new InvalidOperationException("The manual hyphenation session is complete.");
        var entry = _entries[_index];
        if (!entry.EditsByBreakPoint.TryGetValue(breakPoint, out var edit))
            throw new ArgumentOutOfRangeException(nameof(breakPoint));
        _edits.Add(edit);
        _index++;
    }

    public void Skip()
    {
        if (!IsComplete)
            _index++;
    }
}

public static class ManualHyphenationPlanner
{
    public static ManualHyphenationSession CreateSession(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var entries = new List<(string, IReadOnlyList<(int, ManualHyphenationEdit)>)>();
        foreach (var paragraph in BodyParagraphs(document.Blocks))
        {
            if (paragraph.Formatting.SuppressAutoHyphens)
                continue;
            AddParagraphCandidates(paragraph, document.Page.DoNotHyphenateCaps, entries);
        }
        return new ManualHyphenationSession(entries);
    }

    private static IEnumerable<Paragraph> BodyParagraphs(IEnumerable<Block> blocks)
    {
        foreach (var block in blocks)
        {
            if (block is Paragraph paragraph)
            {
                yield return paragraph;
            }
            else if (block is Table table)
            {
                foreach (var cellParagraph in table.Rows
                    .SelectMany(row => row.Cells)
                    .SelectMany(cell => cell.Paragraphs))
                {
                    yield return cellParagraph;
                }
            }
        }
    }

    private static void AddParagraphCandidates(
        Paragraph paragraph,
        bool doNotHyphenateCaps,
        ICollection<(string, IReadOnlyList<(int, ManualHyphenationEdit)>)> entries)
    {
        var runs = paragraph.Runs.Where(run => run.Text.Length > 0).ToList();
        var text = string.Concat(runs.Select(run => run.Text));
        for (var tokenStart = 0; tokenStart < text.Length;)
        {
            while (tokenStart < text.Length && char.IsWhiteSpace(text[tokenStart]))
                tokenStart++;
            if (tokenStart >= text.Length)
                break;
            var tokenEnd = tokenStart;
            while (tokenEnd < text.Length && !char.IsWhiteSpace(text[tokenEnd]))
                tokenEnd++;

            var token = text[tokenStart..tokenEnd];
            if (!token.Contains(Hyphenator.SoftHyphen))
            {
                var coreStart = 0;
                while (coreStart < token.Length && !char.IsLetter(token[coreStart]))
                    coreStart++;
                var coreEnd = token.Length;
                while (coreEnd > coreStart && !char.IsLetter(token[coreEnd - 1]))
                    coreEnd--;
                var word = token[coreStart..coreEnd];
                var isAllCaps = word.Any(char.IsLetter) && !word.Any(char.IsLower);
                if (!(doNotHyphenateCaps && isAllCaps))
                {
                    var points = Hyphenator.BreakPoints(word);
                    if (points.Count > 0)
                    {
                        var wordStart = tokenStart + coreStart;
                        var options = points.Select(point =>
                            (point, LocateEdit(runs, wordStart + point))).ToList();
                        entries.Add((word, options));
                    }
                }
            }
            tokenStart = tokenEnd;
        }
    }

    private static ManualHyphenationEdit LocateEdit(IReadOnlyList<Run> runs, int paragraphOffset)
    {
        var runStart = 0;
        foreach (var run in runs)
        {
            var runEnd = runStart + run.Text.Length;
            if (paragraphOffset <= runEnd)
                return new ManualHyphenationEdit(run, paragraphOffset - runStart);
            runStart = runEnd;
        }
        throw new ArgumentOutOfRangeException(nameof(paragraphOffset));
    }
}
