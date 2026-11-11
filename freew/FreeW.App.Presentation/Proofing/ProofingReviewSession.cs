using FreeW.Core.Model;

namespace FreeW.App.Presentation.Proofing;

public enum ProofingReviewIssueKind
{
    Spelling,
    Grammar,
}

public enum ProofingReviewStatus
{
    Idle,
    Reviewing,
    Completed,
}

/// <summary>A body position in model coordinates: leaf block index plus paragraph plain-text offset.</summary>
public readonly record struct ProofingReviewPosition(int BlockIndex, int Offset)
    : IComparable<ProofingReviewPosition>
{
    public int CompareTo(ProofingReviewPosition other)
    {
        var block = BlockIndex.CompareTo(other.BlockIndex);
        return block != 0 ? block : Offset.CompareTo(other.Offset);
    }

    public static bool operator <(ProofingReviewPosition left, ProofingReviewPosition right) =>
        left.CompareTo(right) < 0;

    public static bool operator >(ProofingReviewPosition left, ProofingReviewPosition right) =>
        left.CompareTo(right) > 0;

    public static bool operator <=(ProofingReviewPosition left, ProofingReviewPosition right) =>
        left.CompareTo(right) <= 0;

    public static bool operator >=(ProofingReviewPosition left, ProofingReviewPosition right) =>
        left.CompareTo(right) >= 0;
}

/// <summary>
/// One proposed fix: replace <see cref="Length"/> characters at <see cref="Offset"/> of the issue's
/// paragraph with <see cref="Replacement"/>. An empty replacement deletes the range.
/// </summary>
public sealed record ProofingReviewSuggestion(string Replacement, int Offset, int Length);

/// <summary>One spelling or grammar issue offered by the Spelling &amp; Grammar walk-through.</summary>
public sealed record ProofingReviewIssue(
    ProofingReviewIssueKind Kind,
    int BlockIndex,
    int Offset,
    int Length,
    string Text,
    IReadOnlyList<ProofingReviewSuggestion> Suggestions,
    string? RuleId = null)
{
    public ProofingReviewPosition Start => new(BlockIndex, Offset);

    public ProofingReviewPosition End => new(BlockIndex, Offset + Length);

    /// <summary>Projects a shared planner diagnostic into a walk-through issue.</summary>
    public static ProofingReviewIssue FromDiagnostic(ProofingDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        return new ProofingReviewIssue(
            diagnostic.Kind == ProofingDiagnosticKind.Grammar
                ? ProofingReviewIssueKind.Grammar
                : ProofingReviewIssueKind.Spelling,
            diagnostic.BlockIndex,
            diagnostic.ParagraphOffset,
            diagnostic.Length,
            diagnostic.Word,
            diagnostic.SuggestionsOrEmpty
                .Select(suggestion => new ProofingReviewSuggestion(
                    suggestion.Replacement,
                    suggestion.ParagraphOffset,
                    suggestion.Length))
                .ToArray(),
            diagnostic.RuleId);
    }

    internal bool IsSameOccurrence(ProofingReviewIssue other) =>
        Kind == other.Kind
        && BlockIndex == other.BlockIndex
        && Offset == other.Offset
        && Length == other.Length
        && string.Equals(RuleId, other.RuleId, StringComparison.Ordinal)
        && string.Equals(Text, other.Text, StringComparison.Ordinal);
}

/// <summary>One text replacement the host applies through its ordinary undoable editing path.</summary>
public sealed record ProofingReviewReplacement(int BlockIndex, int Offset, int Length, string Text);

/// <summary>
/// Renderer port of the Spelling &amp; Grammar walk-through. The host owns issue discovery (its native
/// speller and the shared <see cref="ProofingDiagnosticPlanner"/>), selection, the undoable edit path,
/// and the existing custom-dictionary / ignore stores; the session owns only the walk order and state.
/// </summary>
public interface IProofingReviewHost
{
    /// <summary>The caret (or selection start) the walk-through starts from.</summary>
    ProofingReviewPosition CaretPosition { get; }

    /// <summary>All current issues of the committed document, in any order.</summary>
    IReadOnlyList<ProofingReviewIssue> CollectIssues();

    /// <summary>Selects the issue's flagged range in the document and scrolls it into view.</summary>
    void SelectIssue(ProofingReviewIssue issue);

    /// <summary>
    /// Applies every replacement as ONE undoable edit. Replacements arrive ordered from the end of the
    /// document to its start, so earlier offsets stay valid while later ones are applied. Returns false,
    /// leaving the document unchanged, when the edit is not allowed or could not be applied.
    /// </summary>
    bool ApplyReplacements(IReadOnlyList<ProofingReviewReplacement> replacements);

    /// <summary>Stops flagging this one occurrence (Ignore Once), so its indicator goes away too.</summary>
    void IgnoreOnce(ProofingReviewIssue issue);

    /// <summary>Stops flagging this spelling for the rest of the editing session (Ignore All).</summary>
    void IgnoreSpelling(ProofingReviewIssue issue);

    /// <summary>Adds the word to the persistent custom dictionary.</summary>
    void AddToDictionary(ProofingReviewIssue issue);

    /// <summary>Stops checking this grammar rule for the rest of the editing session.</summary>
    void IgnoreGrammarRule(string ruleId);
}

/// <summary>
/// Word's classic Spelling &amp; Grammar walk-through as a renderer-neutral state machine. Starting at
/// the caret it visits each issue in document order, wraps once at the end of the document, and
/// completes when it gets back to where it started. Every action re-collects the issues from the host,
/// so the walk stays correct while replacements shift the text underneath it.
/// </summary>
public sealed class ProofingReviewSession
{
    private readonly IProofingReviewHost _host;
    private readonly HashSet<string> _ignoredWords = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ignoredRules = new(StringComparer.Ordinal);
    private readonly List<ProofingReviewIssue> _ignoredOnce = [];
    private ProofingReviewPosition _anchor;
    private ProofingReviewPosition _cursor;
    private bool _wrapped;

    public ProofingReviewSession(IProofingReviewHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public ProofingReviewStatus Status { get; private set; } = ProofingReviewStatus.Idle;

    /// <summary>The issue currently selected in the document, or null when idle or complete.</summary>
    public ProofingReviewIssue? Current { get; private set; }

    /// <summary>Number of replacements applied since <see cref="Start"/>.</summary>
    public int ChangeCount { get; private set; }

    public bool IsReviewing => Status == ProofingReviewStatus.Reviewing && Current is not null;

    public bool CanChange => IsReviewing && Current!.Suggestions.Count > 0;

    public bool CanChangeAll => CanChange && Current!.Kind == ProofingReviewIssueKind.Spelling;

    public bool CanIgnoreOnce => IsReviewing;

    public bool CanIgnoreAll =>
        IsReviewing
        && (Current!.Kind == ProofingReviewIssueKind.Spelling || !string.IsNullOrEmpty(Current.RuleId));

    public bool CanAddToDictionary => IsReviewing && Current!.Kind == ProofingReviewIssueKind.Spelling;

    /// <summary>Starts (or restarts) the walk-through at the host's caret and selects the first issue.</summary>
    public void Start()
    {
        _ignoredOnce.Clear();
        ChangeCount = 0;
        _wrapped = false;
        var caret = _host.CaretPosition;

        // An issue the caret sits inside (or directly after) is the first stop, as in Word.
        var containing = ActiveIssues()
            .Where(issue => issue.Start <= caret && caret <= issue.End)
            .Select(issue => (ProofingReviewPosition?)issue.Start)
            .FirstOrDefault();
        _anchor = containing ?? caret;
        _cursor = _anchor;
        Advance();
    }

    /// <summary>Ends the walk-through without touching the document.</summary>
    public void Close()
    {
        Current = null;
        Status = ProofingReviewStatus.Idle;
    }

    /// <summary>Skips this occurrence only.</summary>
    public bool IgnoreOnce()
    {
        if (!IsReviewing)
            return false;

        _ignoredOnce.Add(Current!);
        _host.IgnoreOnce(Current!);
        Advance();
        return true;
    }

    /// <summary>
    /// Spelling: ignores every occurrence of the word. Grammar: stops checking the rule. Both last for
    /// the rest of the editing session and go through the host's existing ignore infrastructure.
    /// </summary>
    public bool IgnoreAll()
    {
        if (!CanIgnoreAll)
            return false;

        var issue = Current!;
        if (issue.Kind == ProofingReviewIssueKind.Spelling)
        {
            _ignoredWords.Add(issue.Text);
            _host.IgnoreSpelling(issue);
        }
        else
        {
            _ignoredRules.Add(issue.RuleId!);
            _host.IgnoreGrammarRule(issue.RuleId!);
        }

        Advance();
        return true;
    }

    public bool AddToDictionary()
    {
        if (!CanAddToDictionary)
            return false;

        var issue = Current!;
        _host.AddToDictionary(issue);
        _ignoredWords.Add(issue.Text);
        Advance();
        return true;
    }

    /// <summary>Applies the chosen suggestion to the current issue as one undoable edit.</summary>
    public bool Change(int suggestionIndex)
    {
        if (!TryGetSuggestion(suggestionIndex, out var issue, out var suggestion))
            return false;

        return ApplyAndAdvance(
        [
            new ProofingReviewReplacement(
                issue.BlockIndex,
                suggestion.Offset,
                suggestion.Length,
                suggestion.Replacement),
        ]);
    }

    /// <summary>
    /// Replaces the current misspelling and every other occurrence of the same misspelling with the
    /// chosen suggestion, as one undoable edit. Offered for spelling only, as in Word.
    /// </summary>
    public bool ChangeAll(int suggestionIndex)
    {
        if (!CanChangeAll || !TryGetSuggestion(suggestionIndex, out var issue, out var suggestion))
            return false;

        var replacements = ActiveIssues()
            .Where(candidate =>
                candidate.Kind == ProofingReviewIssueKind.Spelling
                && string.Equals(candidate.Text, issue.Text, StringComparison.Ordinal))
            .Select(candidate => new ProofingReviewReplacement(
                candidate.BlockIndex,
                candidate.Offset,
                candidate.Length,
                suggestion.Replacement))
            .ToList();
        if (replacements.Count == 0)
            return false;

        return ApplyAndAdvance(replacements);
    }

    private bool TryGetSuggestion(
        int suggestionIndex,
        out ProofingReviewIssue issue,
        out ProofingReviewSuggestion suggestion)
    {
        issue = null!;
        suggestion = null!;
        if (!CanChange || suggestionIndex < 0 || suggestionIndex >= Current!.Suggestions.Count)
            return false;

        // The document may have been edited behind the pane; never apply offsets of a stale issue.
        var current = Current;
        if (!ActiveIssues().Any(candidate => candidate.IsSameOccurrence(current)))
        {
            Advance();
            return false;
        }

        issue = current;
        suggestion = current.Suggestions[suggestionIndex];
        return true;
    }

    private bool ApplyAndAdvance(IReadOnlyList<ProofingReviewReplacement> replacements)
    {
        var ordered = replacements
            .OrderByDescending(replacement => new ProofingReviewPosition(replacement.BlockIndex, replacement.Offset))
            .ToArray();
        if (!_host.ApplyReplacements(ordered))
            return false;

        ChangeCount += ordered.Length;
        foreach (var replacement in ordered)
        {
            _anchor = Shift(_anchor, replacement);
            for (var index = _ignoredOnce.Count - 1; index >= 0; index--)
            {
                var ignored = _ignoredOnce[index];
                if (ignored.BlockIndex != replacement.BlockIndex)
                    continue;
                if (ignored.Offset < replacement.Offset + replacement.Length
                    && replacement.Offset < ignored.Offset + ignored.Length)
                {
                    _ignoredOnce.RemoveAt(index);
                    continue;
                }

                var shifted = Shift(ignored.Start, replacement).Offset;
                if (shifted != ignored.Offset)
                    _ignoredOnce[index] = ignored with { Offset = shifted };
            }
        }

        // Resume at the first changed position so a follow-up issue exposed by the fix is still visited.
        // (The edit range can start before the flagged range: deleting a repeated word also removes the
        // whitespace in front of it.)
        var current = Current!;
        _cursor = ordered
            .Where(replacement => replacement.BlockIndex == current.BlockIndex)
            .Select(replacement => new ProofingReviewPosition(replacement.BlockIndex, replacement.Offset))
            .Where(position => position <= current.Start)
            .DefaultIfEmpty(current.Start)
            .Max();
        Advance();
        return true;
    }

    private static ProofingReviewPosition Shift(
        ProofingReviewPosition position,
        ProofingReviewReplacement replacement)
    {
        if (position.BlockIndex != replacement.BlockIndex || position.Offset <= replacement.Offset)
            return position;
        if (position.Offset >= replacement.Offset + replacement.Length)
            return position with { Offset = position.Offset + replacement.Text.Length - replacement.Length };
        return position with { Offset = replacement.Offset };
    }

    private IReadOnlyList<ProofingReviewIssue> ActiveIssues() =>
        _host.CollectIssues()
            .Where(issue => issue.Kind == ProofingReviewIssueKind.Spelling
                ? !_ignoredWords.Contains(issue.Text)
                : issue.RuleId is null || !_ignoredRules.Contains(issue.RuleId))
            .OrderBy(issue => issue.Start)
            .ThenBy(issue => issue.Length)
            .ThenBy(issue => issue.Kind)
            .ThenBy(issue => issue.RuleId, StringComparer.Ordinal)
            .ToArray();

    private void Advance()
    {
        var candidates = ActiveIssues()
            .Where(issue => !_ignoredOnce.Any(ignored => ignored.IsSameOccurrence(issue)))
            .ToArray();

        ProofingReviewIssue? next = null;
        if (!_wrapped)
        {
            next = candidates.FirstOrDefault(issue => issue.Start >= _cursor);
            if (next is null)
            {
                _wrapped = true;
                _cursor = default;
            }
        }

        next ??= candidates.FirstOrDefault(issue => issue.Start >= _cursor && issue.Start < _anchor);
        if (next is null)
        {
            Current = null;
            Status = ProofingReviewStatus.Completed;
            return;
        }

        _cursor = next.Start;
        Current = next;
        Status = ProofingReviewStatus.Reviewing;
        _host.SelectIssue(next);
    }
}
