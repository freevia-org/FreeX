using FreeW.Core.Model;
using Free.Shared.Shell;

namespace FreeW.App.Presentation.Dialogs;

public sealed record ManualHyphenationOption(int BreakPoint, string DisplayText);

public enum ManualHyphenationDialogAction
{
    Accept,
    Skip,
    Cancel
}

public enum ManualHyphenationDialogField
{
    Choices,
    Yes,
    No,
    Cancel,
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
    public const string Title = "Manual Hyphenation";
    public const string HyphenateAtLabel = "Hyphenate at:";
    public const string YesLabel = "Yes";
    public const string NoLabel = "No";
    public const string CancelLabel = "Cancel";
    public const string YesAccessLabel = "_Yes";
    public const string NoAccessLabel = "_No";
    public const string AutomationId = "ManualHyphenationDialog";
    public const string ChoicesAutomationId = "ManualHyphenationChoices";
    public const string YesButtonAutomationId = "ManualHyphenationYesButton";
    public const string NoButtonAutomationId = "ManualHyphenationNoButton";
    public const string CancelButtonAutomationId = "ManualHyphenationCancelButton";
    public const string NoCandidatesMessage = "Manual hyphenation found no words to review.";
    public const string NoChangesMessage = "Manual hyphenation made no changes.";

    public static DialogSurfaceSpec<ManualHyphenationDialogField> HostSurface { get; } = CreateSurface(
        YesAccessLabel,
        NoAccessLabel);

    public static DialogSurfaceSpec<ManualHyphenationDialogField> AvaloniaSurface { get; } = CreateSurface(
        YesLabel,
        NoLabel);

    public static DialogFocusPlan<ManualHyphenationDialogField> FocusPlan { get; } = new(
        InitialFocusTarget: ManualHyphenationDialogField.Choices,
        ValidationFocusTarget: ManualHyphenationDialogField.Choices,
        SelectAllOnFocus: false,
        ActionButtons:
        [
            new DialogActionPlan(YesLabel, IsDefault: true),
            new DialogActionPlan(NoLabel),
            new DialogActionPlan(CancelLabel, IsCancel: true),
        ]);

    public static string FormatCandidateLabel(int candidateNumber) => $"Word {candidateNumber}";

    public static string FormatSummary(int acceptedCount) =>
        acceptedCount == 0
            ? NoChangesMessage
            : $"Manual hyphenation inserted breaks in {acceptedCount} word(s).";

    public static ManualHyphenationSession CreateSession(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var entries = new List<(string, IReadOnlyList<(int, ManualHyphenationEdit)>)>();
        foreach (var paragraph in ReviewParagraphs(document))
        {
            if (paragraph.Formatting.SuppressAutoHyphens)
                continue;
            AddParagraphCandidates(paragraph, document.Page.DoNotHyphenateCaps, entries);
        }
        return new ManualHyphenationSession(entries);
    }

    private static DialogSurfaceSpec<ManualHyphenationDialogField> CreateSurface(
        string yesLabel,
        string noLabel) => new(
            Title,
            AutomationId,
            Title,
            [
                new(ManualHyphenationDialogField.Choices, HyphenateAtLabel, ChoicesAutomationId, "Hyphenation choices"),
                new(ManualHyphenationDialogField.Yes, yesLabel, YesButtonAutomationId, "Accept hyphenation"),
                new(ManualHyphenationDialogField.No, noLabel, NoButtonAutomationId, "Skip hyphenation"),
                new(ManualHyphenationDialogField.Cancel, CancelLabel, CancelButtonAutomationId, "Cancel manual hyphenation"),
            ]);

    private static IEnumerable<Paragraph> ReviewParagraphs(TextDocument document)
    {
        var seenParagraphs = new HashSet<Paragraph>(ReferenceEqualityComparer.Instance);
        var seenGroups = new HashSet<DrawingGroup>(ReferenceEqualityComparer.Instance);
        foreach (var paragraph in BodyParagraphs(document.Blocks))
            foreach (var nested in ParagraphTree(paragraph, seenParagraphs, seenGroups))
                yield return nested;

        foreach (var section in document.Sections)
        {
            foreach (var content in HeaderFooterStories(section.HeadersFooters))
            {
                if (content is null)
                    continue;
                foreach (var paragraph in content.Paragraphs)
                    foreach (var nested in ParagraphTree(paragraph, seenParagraphs, seenGroups))
                        yield return nested;
            }
        }

        foreach (var paragraph in document.Footnotes
                     .Where(note => note.Key > 0)
                     .OrderBy(note => note.Key)
                     .SelectMany(note => note.Value.Content))
        {
            foreach (var nested in ParagraphTree(paragraph, seenParagraphs, seenGroups))
                yield return nested;
        }

        foreach (var paragraph in document.Endnotes
                     .Where(note => note.Key > 0)
                     .OrderBy(note => note.Key)
                     .SelectMany(note => note.Value.Content))
        {
            foreach (var nested in ParagraphTree(paragraph, seenParagraphs, seenGroups))
                yield return nested;
        }
    }

    private static IEnumerable<Paragraph> ParagraphTree(
        Paragraph paragraph,
        ISet<Paragraph> seenParagraphs,
        ISet<DrawingGroup> seenGroups)
    {
        if (!seenParagraphs.Add(paragraph))
            yield break;

        yield return paragraph;
        foreach (var run in paragraph.Runs)
        {
            if (run.Shape is { } shape)
                foreach (var nested in ShapeParagraphs(shape, seenParagraphs, seenGroups))
                    yield return nested;
            if (run.DrawingGroup is { } group)
                foreach (var nested in GroupParagraphs(group, seenParagraphs, seenGroups))
                    yield return nested;
        }
    }

    private static IEnumerable<Paragraph> ShapeParagraphs(
        Shape shape,
        ISet<Paragraph> seenParagraphs,
        ISet<DrawingGroup> seenGroups)
    {
        foreach (var paragraph in shape.TextParagraphs)
            foreach (var nested in ParagraphTree(paragraph, seenParagraphs, seenGroups))
                yield return nested;
    }

    private static IEnumerable<Paragraph> GroupParagraphs(
        DrawingGroup group,
        ISet<Paragraph> seenParagraphs,
        ISet<DrawingGroup> seenGroups)
    {
        if (!seenGroups.Add(group))
            yield break;

        foreach (var child in group.Children)
        {
            if (child is Shape shape)
                foreach (var nested in ShapeParagraphs(shape, seenParagraphs, seenGroups))
                    yield return nested;
            else if (child is DrawingGroup nestedGroup)
                foreach (var nested in GroupParagraphs(nestedGroup, seenParagraphs, seenGroups))
                    yield return nested;
        }
    }

    private static IEnumerable<HeaderFooter?> HeaderFooterStories(SectionHeadersFooters stories)
    {
        yield return stories.Header;
        yield return stories.Footer;
        yield return stories.EvenHeader;
        yield return stories.EvenFooter;
        yield return stories.FirstHeader;
        yield return stories.FirstFooter;
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
        // Shape owner runs mirror the text-box plain text for compatibility. Review the authoritative
        // Shape.TextParagraphs story instead, otherwise the same visible word is offered twice and an
        // accepted edit mutates only the fallback anchor text.
        var runs = paragraph.Runs.Where(run => run.Text.Length > 0 && run.Shape is null).ToList();
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
