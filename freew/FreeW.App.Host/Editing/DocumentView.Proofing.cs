using System.Windows.Documents;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Editing;
using FreeW.App.Presentation.Proofing;
using FreeW.Core.Model;
using ModelParagraph = FreeW.Core.Model.Paragraph;
using WpfParagraph = System.Windows.Documents.Paragraph;

namespace FreeW.App.Host.Editing;

/// <summary>
/// WPF realization of the shared Spelling &amp; Grammar walk-through (<see cref="ProofingReviewSession"/>).
/// Issue discovery combines WPF's native speller (when it is active) with the portable
/// <see cref="ProofingDiagnosticPlanner"/>; every change goes through the portable body-edit session and
/// the document command bus, so it is undoable exactly like typing.
/// </summary>
public sealed partial class DocumentView
{
    // Upper bound on native spelling errors gathered for one walk-through pass, so a document in a
    // language the speller does not know cannot stall the UI thread enumerating every word.
    private const int MaxNativeSpellingIssues = 2000;

    private const int MaxNativeSpellingSuggestions = 10;

    // Session-scoped "Ignore All" words and "Don't check for this issue" grammar rules. They live on the
    // editor (not the pane) so the walk-through, the squiggles and the context menu always agree.
    private readonly HashSet<string> _ignoredProofingWords = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ignoredGrammarRules = new(StringComparer.Ordinal);

    /// <summary>
    /// The persistent custom dictionary shared with Review &gt; Add to Dictionary. Null in editor-only
    /// contexts, where "Add to Dictionary" degrades to a session-scoped ignore.
    /// </summary>
    internal CustomDictionaryStore? ProofingDictionary { get; set; }

    /// <summary>Grammar rules switched off for this editing session (see <see cref="GrammarRuleIds"/>).</summary>
    internal IReadOnlyCollection<string> IgnoredGrammarRules => _ignoredGrammarRules;

    // "Ignore All" and "Don't check for this issue" last for one document, as in Word.
    private void ResetProofingSessionState()
    {
        _ignoredProofingWords.Clear();
        _ignoredGrammarRules.Clear();
    }

    /// <summary>Creates the renderer port the shared walk-through session drives.</summary>
    internal IProofingReviewHost CreateProofingReviewHost() => new ProofingReviewHost(this);

    /// <summary>
    /// Keeps the walk-through's selection visible while keyboard focus sits in the proofing pane, the way
    /// Word keeps the flagged text highlighted behind its Spelling pane.
    /// </summary>
    internal void SetProofingReviewActive(bool active) => IsInactiveSelectionHighlightEnabled = active;

    /// <summary>The caret (or selection start) in model coordinates; the document start when unresolved.</summary>
    internal ProofingReviewPosition ProofingCaretPosition
    {
        get
        {
            var pointer = Selection.IsEmpty ? CaretPosition : Selection.Start;
            return pointer is not null && TryGetModelTextOffset(pointer, out var position)
                ? new ProofingReviewPosition(position.BlockIndex, position.Offset)
                : default;
        }
    }

    /// <summary>
    /// Every spelling and grammar issue of the committed document. Runs regardless of the as-you-type
    /// toggle and the document's hide-errors flags, as Word's F7 does.
    /// </summary>
    internal IReadOnlyList<ProofingReviewIssue> CollectProofingReviewIssues()
    {
        CommitToModel();

        // The native speller owns spelling whenever it is running; the portable correction catalog only
        // stands in when it is not (spell checking switched off, hidden by the document, or unavailable).
        var nativeSpelling = SpellCheck.IsEnabled;
        var issues = new List<ProofingReviewIssue>();
        foreach (var diagnostic in BuildProofingDiagnostics(spellCheckEnabled: true))
        {
            if (diagnostic.Kind == ProofingDiagnosticKind.Spelling
                && (nativeSpelling || _ignoredProofingWords.Contains(diagnostic.Word)))
            {
                continue;
            }

            issues.Add(ProofingReviewIssue.FromDiagnostic(diagnostic));
        }

        if (nativeSpelling)
            CollectNativeSpellingIssues(issues);
        return issues;
    }

    /// <summary>
    /// Shared planner diagnostics for the committed model, honouring the custom dictionary and the
    /// session's ignored grammar rules.
    /// </summary>
    private IReadOnlyList<ProofingDiagnostic> BuildProofingDiagnostics(bool spellCheckEnabled) =>
        ProofingDiagnosticPlanner.Build(_model, spellCheckEnabled, ProofingDictionary?.Words)
            .Where(diagnostic => diagnostic.RuleId is null || !_ignoredGrammarRules.Contains(diagnostic.RuleId))
            .ToArray();

    private void CollectNativeSpellingIssues(List<ProofingReviewIssue> issues)
    {
        var indexOf = new Dictionary<WpfParagraph, int>();
        var visibleIndex = 0;
        foreach (var block in Document.Blocks)
            NumberLeafBlocks(block, indexOf, ref visibleIndex);

        TextPointer? position = Document.ContentStart;
        TextPointer? lastStart = null;
        for (var found = 0; found < MaxNativeSpellingIssues && position is not null;)
        {
            var errorPosition = GetNextSpellingErrorPosition(position, LogicalDirection.Forward);
            if (errorPosition is null)
                break;

            var range = GetSpellingErrorRange(errorPosition);
            var error = GetSpellingError(errorPosition);
            if (range is null
                || error is null
                || (lastStart is not null && range.Start.CompareTo(lastStart) <= 0))
            {
                // Not a usable error, or the speller handed the previous one back: step past it.
                position = errorPosition.GetNextInsertionPosition(LogicalDirection.Forward);
                if (position is not null && lastStart is not null && position.CompareTo(lastStart) <= 0)
                    position = lastStart.GetNextInsertionPosition(LogicalDirection.Forward);
                continue;
            }

            found++;
            lastStart = range.Start;
            position = range.End;

            // Only ordinary body paragraphs map to model coordinates; a misspelling inside a table cell
            // or a multi-paragraph range keeps its squiggle but is not part of the walk-through.
            var paragraph = range.Start.Paragraph;
            if (paragraph is null
                || !ReferenceEquals(paragraph, range.End.Paragraph)
                || !indexOf.TryGetValue(paragraph, out var visible))
            {
                continue;
            }

            var blockIndex = ModelIndexFromVisible(visible);
            var start = OffsetInParagraph(paragraph, range.Start);
            var length = OffsetInParagraph(paragraph, range.End) - start;
            var text = range.Text;
            if (blockIndex < 0
                || length <= 0
                || string.IsNullOrWhiteSpace(text)
                || _ignoredProofingWords.Contains(text))
            {
                continue;
            }

            issues.Add(new ProofingReviewIssue(
                ProofingReviewIssueKind.Spelling,
                blockIndex,
                start,
                length,
                text,
                error.Suggestions
                    .Take(MaxNativeSpellingSuggestions)
                    .Select(suggestion => new ProofingReviewSuggestion(suggestion, start, length))
                    .ToArray()));
        }
    }

    /// <summary>Selects the issue's flagged range and scrolls it into view without taking focus.</summary>
    internal bool SelectProofingIssue(ProofingReviewIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        var start = TextPointerAtModelTextOffset(issue.BlockIndex, issue.Offset);
        var end = TextPointerAtModelTextOffset(issue.BlockIndex, issue.Offset + issue.Length);
        if (start is null || end is null)
            return false;

        Selection.Select(start, end);
        start.Paragraph?.BringIntoView();
        return true;
    }

    /// <summary>
    /// Applies proofing replacements (ordered from the end of the document to its start) through the
    /// portable body-edit session as ONE undoable edit. Honours Restrict Editing and Track Changes
    /// exactly like typing over a selection. Returns false, leaving the document unchanged, when the
    /// edit is not allowed, a range no longer fits the text, or the session declines a replacement.
    /// </summary>
    internal bool ApplyProofingReplacements(IReadOnlyList<ProofingReviewReplacement> replacements)
    {
        ArgumentNullException.ThrowIfNull(replacements);
        if (replacements.Count == 0
            || _commands.IsUndoGroupOpen
            || !AllowsRestrictEditingOperation(RestrictEditingOperationKind.BodyTextEdit))
        {
            return false;
        }

        CommitToModel();
        foreach (var replacement in replacements)
        {
            if (replacement.BlockIndex < 0
                || replacement.BlockIndex >= _model.Blocks.Count
                || _model.Blocks[replacement.BlockIndex] is not ModelParagraph paragraph
                || replacement.Offset < 0
                || replacement.Length < 0
                || replacement.Offset + replacement.Length > paragraph.PlainText.Length
                || (replacement.Length == 0 && replacement.Text.Length == 0))
            {
                return false;
            }
        }

        // One group even for a single replacement: the model edits below never re-read the rendered
        // surface, so the view refreshes once, when the group commits.
        DocumentTextPosition caret = default;
        _commands.BeginUndoGroup();
        try
        {
            foreach (var replacement in replacements)
            {
                var range = new DocumentTextRange(
                    new DocumentTextPosition(replacement.BlockIndex, replacement.Offset),
                    new DocumentTextPosition(replacement.BlockIndex, replacement.Offset + replacement.Length));
                DocumentBodyEditorActionResult result;
                var applied = replacement.Text.Length == 0
                    ? _editingSession.Body.TryApplyDeletion(
                        range,
                        DocumentBodyDeleteDirection.Forward,
                        TrackChangesEnabled,
                        out result)
                    : _editingSession.Body.TryApplyTextInput(
                        range,
                        new DocumentBodyTextInput(
                            replacement.Text,
                            TrackChangesEnabled,
                            ProofingReplacementFormatting(replacement)),
                        out result);
                if (!applied)
                {
                    _commands.RollbackUndoGroup();
                    return false;
                }

                caret = result.Caret;
            }

            PruneOrphanedNoteAndCommentAnchorsAfterPortableEdit();
            _commands.CommitUndoGroup(replacements.Count > 1 ? "Change All" : "Spelling and Grammar");
        }
        catch
        {
            if (_commands.IsUndoGroupOpen)
                _commands.RollbackUndoGroup();
            throw;
        }

        // Leave the caret at the (last applied, i.e. earliest) change without stealing focus from the pane.
        if (TextPointerAtModelTextOffset(caret.BlockIndex, caret.Offset) is { } pointer)
            CaretPosition = pointer;
        return true;
    }

    // A correction keeps the character formatting of the text it replaces (the run under the first
    // replaced character), not that of the text in front of it.
    private RunFormatting? ProofingReplacementFormatting(ProofingReviewReplacement replacement)
    {
        if (replacement.Length == 0 || _model.Blocks[replacement.BlockIndex] is not ModelParagraph paragraph)
            return null;

        var offset = 0;
        foreach (var run in paragraph.Runs)
        {
            var end = offset + run.Text.Length;
            if (replacement.Offset < end)
                return run.Formatting;
            offset = end;
        }

        return null;
    }

    /// <summary>Ignore All: stops flagging this spelling for the rest of the editing session.</summary>
    internal void IgnoreProofingSpelling(ProofingReviewIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        _ignoredProofingWords.Add(issue.Text);
        if (!SpellCheck.IsEnabled)
            return;

        try
        {
            // Let the native speller drop its squiggles for the word too.
            var inside = TextPointerAtModelTextOffset(issue.BlockIndex, issue.Offset + Math.Min(1, issue.Length));
            if (inside is not null)
                GetSpellingError(inside)?.IgnoreAll();
        }
        catch (InvalidOperationException)
        {
            // Best-effort: the session-scoped ignore list above already hides the word from the walk.
        }
    }

    /// <summary>
    /// Adds the word to the shared persistent custom dictionary (the one Review &gt; Add to Dictionary
    /// uses) and reloads it into the native speller. Returns true when the word was newly stored.
    /// </summary>
    internal bool AddProofingWordToDictionary(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return false;

        if (ProofingDictionary is not { } dictionary)
        {
            _ignoredProofingWords.Add(word);
            return false;
        }

        if (!dictionary.Add(word))
            return false;

        RefreshCustomDictionary();
        return true;
    }

    /// <summary>Stops checking one grammar rule for the rest of the editing session.</summary>
    internal void IgnoreGrammarRule(string ruleId)
    {
        if (!string.IsNullOrWhiteSpace(ruleId))
            _ignoredGrammarRules.Add(ruleId);
    }

    private sealed class ProofingReviewHost(DocumentView editor) : IProofingReviewHost
    {
        public ProofingReviewPosition CaretPosition => editor.ProofingCaretPosition;

        public IReadOnlyList<ProofingReviewIssue> CollectIssues() => editor.CollectProofingReviewIssues();

        public void SelectIssue(ProofingReviewIssue issue) => editor.SelectProofingIssue(issue);

        public bool ApplyReplacements(IReadOnlyList<ProofingReviewReplacement> replacements) =>
            editor.ApplyProofingReplacements(replacements);

        public void IgnoreSpelling(ProofingReviewIssue issue) => editor.IgnoreProofingSpelling(issue);

        public void AddToDictionary(ProofingReviewIssue issue) => editor.AddProofingWordToDictionary(issue.Text);

        public void IgnoreGrammarRule(string ruleId) => editor.IgnoreGrammarRule(ruleId);
    }
}
