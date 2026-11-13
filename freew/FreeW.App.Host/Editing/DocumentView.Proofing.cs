using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Free.Shared.Ribbon;
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

    // "Ignore Once" for a grammar issue: the rule, the paragraph text it was found in, and where. Editing
    // the paragraph changes its text, so the issue is re-evaluated then, as in Word.
    private readonly HashSet<(string RuleId, string ParagraphText, int Offset)> _ignoredGrammarOccurrences = [];

    /// <summary>
    /// Which grammar rules run in this editor. Every rule is individually switchable; the session's
    /// "Don't check for this issue" choices are applied on top.
    /// </summary>
    public GrammarCheckOptions GrammarOptions
    {
        get => _grammarOptions;
        set
        {
            _grammarOptions = value ?? GrammarCheckOptions.Default;
            RefreshGrammarSquiggles();
        }
    }

    private GrammarCheckOptions _grammarOptions = GrammarCheckOptions.Default;

    private GrammarCheckOptions EffectiveGrammarOptions =>
        _ignoredGrammarRules.Count == 0
            ? _grammarOptions
            : new GrammarCheckOptions(_grammarOptions.DisabledRuleIds.Concat(_ignoredGrammarRules));

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
        _ignoredGrammarOccurrences.Clear();
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
        ProofingDiagnosticPlanner.Build(_model, spellCheckEnabled, ProofingDictionary?.Words, EffectiveGrammarOptions)
            .Where(diagnostic => !IsIgnoredGrammarOccurrence(diagnostic))
            .ToArray();

    private bool IsIgnoredGrammarOccurrence(ProofingDiagnostic diagnostic) =>
        _ignoredGrammarOccurrences.Count > 0
        && diagnostic is { Kind: ProofingDiagnosticKind.Grammar, RuleId: { } ruleId }
        && _model.Blocks[diagnostic.BlockIndex] is ModelParagraph paragraph
        && _ignoredGrammarOccurrences.Contains((ruleId, paragraph.PlainText, diagnostic.ParagraphOffset));

    /// <summary>
    /// The grammar issues that get a blue double underline: none while as-you-type checking is off or the
    /// document hides grammatical errors, and never an issue or rule the user chose to ignore.
    /// </summary>
    internal IReadOnlyList<ProofingDiagnostic> VisibleGrammarDiagnostics() =>
        !SpellCheckEnabled || _model.HideGrammaticalErrors
            ? []
            : BuildProofingDiagnostics(spellCheckEnabled: true)
                .Where(diagnostic => diagnostic.Kind == ProofingDiagnosticKind.Grammar)
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
        if (string.IsNullOrWhiteSpace(ruleId))
            return;

        _ignoredGrammarRules.Add(ruleId);
        RefreshGrammarSquiggles();
    }

    /// <summary>
    /// Ignore Once for a grammar issue: this occurrence loses its underline and is skipped by the
    /// walk-through until its paragraph is edited. (The native speller has no per-occurrence ignore, so a
    /// spelling issue is only skipped by the walk-through itself.)
    /// </summary>
    internal void IgnoreProofingIssueOnce(ProofingReviewIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        if (issue.Kind != ProofingReviewIssueKind.Grammar
            || issue.RuleId is not { } ruleId
            || issue.BlockIndex < 0
            || issue.BlockIndex >= _model.Blocks.Count
            || _model.Blocks[issue.BlockIndex] is not ModelParagraph paragraph)
        {
            return;
        }

        _ignoredGrammarOccurrences.Add((ruleId, paragraph.PlainText, issue.Offset));
        RefreshGrammarSquiggles();
    }

    // ── Blue double underline ────────────────────────────────────────────────────────────────────

    // How long the text must be quiet before the grammar underlines are recomputed, so typing never
    // waits on a whole-document pass.
    private static readonly TimeSpan GrammarSquiggleDelay = TimeSpan.FromMilliseconds(350);

    // Live ranges of the visible grammar issues in the CURRENT FlowDocument. TextPointers follow edits,
    // so the underlines stay glued to their text between refreshes; a re-render replaces the document
    // and the ranges are rebuilt.
    private readonly List<TextRange> _grammarSquiggleRanges = [];
    private GrammarSquiggleAdorner? _grammarSquiggleAdorner;
    private DispatcherTimer? _grammarSquiggleTimer;

    /// <summary>The flagged text under each blue double underline, in document order.</summary>
    internal IReadOnlyList<string> GrammarSquiggleTexts =>
        _grammarSquiggleRanges.Select(range => range.Text).ToArray();

    // Called once from the constructor: the proofing chrome hangs off the editor's own events so the big
    // editing file needs no further hooks.
    private void InitializeProofingSurface()
    {
        Loaded += (_, _) => RefreshGrammarSquiggles();
        TextChanged += (_, _) => ScheduleGrammarSquiggleRefresh();
        _editingSession.Changed += ScheduleGrammarSquiggleRefresh;
        PreviewMouseRightButtonDown += (_, e) =>
            PrepareProofingContextMenu(GetPositionFromPoint(e.GetPosition(this), snapToText: false));
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Apps || (e.SystemKey == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift))
                PrepareProofingContextMenu(CaretPosition);
        };
    }

    private void ScheduleGrammarSquiggleRefresh()
    {
        // The old ranges belong to a document that a re-render has just replaced: drop them at once.
        if (_grammarSquiggleRanges.Count > 0
            && !_grammarSquiggleRanges[0].Start.IsInSameDocument(Document.ContentStart))
        {
            _grammarSquiggleRanges.Clear();
            _grammarSquiggleAdorner?.InvalidateVisual();
        }

        if (!IsLoaded)
            return;

        _grammarSquiggleTimer ??= CreateGrammarSquiggleTimer();
        _grammarSquiggleTimer.Stop();
        _grammarSquiggleTimer.Start();
    }

    private DispatcherTimer CreateGrammarSquiggleTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = GrammarSquiggleDelay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            RefreshGrammarSquiggles();
        };
        return timer;
    }

    /// <summary>
    /// Recomputes the blue double underlines from the shared grammar diagnostics of the model. A range
    /// whose live text no longer matches its diagnostic (the surface is ahead of the model mid-edit) is
    /// skipped rather than drawn in the wrong place.
    /// </summary>
    internal void RefreshGrammarSquiggles()
    {
        _grammarSquiggleRanges.Clear();
        foreach (var diagnostic in VisibleGrammarDiagnostics())
        {
            var start = TextPointerAtModelTextOffset(diagnostic.BlockIndex, diagnostic.ParagraphOffset);
            var end = TextPointerAtModelTextOffset(
                diagnostic.BlockIndex,
                diagnostic.ParagraphOffset + diagnostic.Length);
            if (start is null || end is null)
                continue;

            var range = new TextRange(start, end);
            if (string.Equals(range.Text, diagnostic.Word, StringComparison.Ordinal))
                _grammarSquiggleRanges.Add(range);
        }

        SyncGrammarSquiggleAdorner();
        GrammarSquigglesRefreshed?.Invoke();
    }

    /// <summary>Raised after the grammar underlines were recomputed (an edit, an ignored rule or issue, new options).</summary>
    internal event Action? GrammarSquigglesRefreshed;

    /// <summary>The position <paramref name="offset"/> characters into model block <paramref name="modelBlockIndex"/>'s text.</summary>
    internal TextPointer? PositionAtModelTextOffset(int modelBlockIndex, int offset) =>
        TextPointerAtModelTextOffset(modelBlockIndex, offset);

    private void SyncGrammarSquiggleAdorner()
    {
        if (_grammarSquiggleAdorner is null)
        {
            if (_grammarSquiggleRanges.Count == 0 || AdornerLayer.GetAdornerLayer(this) is not { } layer)
                return;

            _grammarSquiggleAdorner = new GrammarSquiggleAdorner(this, () => _grammarSquiggleRanges);
            layer.Add(_grammarSquiggleAdorner);
        }

        _grammarSquiggleAdorner.InvalidateVisual();
    }

    // ── Right-click menu ─────────────────────────────────────────────────────────────────────────

    // The grammar menu installed for the pending right-click; null leaves WPF's own editing/spelling menu.
    private ContextMenu? _grammarContextMenu;

    /// <summary>The visible grammar issue covering <paramref name="position"/>, if any.</summary>
    internal ProofingReviewIssue? GrammarIssueAt(TextPointer? position)
    {
        if (position is null || !TryGetModelTextOffset(position, out var model))
            return null;

        var diagnostic = VisibleGrammarDiagnostics().FirstOrDefault(candidate =>
            candidate.BlockIndex == model.BlockIndex
            && candidate.ParagraphOffset <= model.Offset
            && model.Offset <= candidate.ParagraphOffset + candidate.Length);
        return diagnostic is null ? null : ProofingReviewIssue.FromDiagnostic(diagnostic);
    }

    // Runs BEFORE the framework raises ContextMenuOpening. RichTextBox shows its built-in menu (with the
    // native spelling suggestions) only while ContextMenu is null, so a grammar issue under the pointer
    // swaps in the grammar menu and anything else restores the built-in one.
    private void PrepareProofingContextMenu(TextPointer? position)
    {
        if (ReferenceEquals(ContextMenu, _grammarContextMenu))
            ContextMenu = null;
        _grammarContextMenu = null;

        // A misspelling keeps the native speller's own menu even when a grammar issue overlaps it.
        if (position is null || (SpellCheck.IsEnabled && GetSpellingError(position) is not null))
            return;

        // Only text that shows a grammar underline can open the grammar menu, so an ordinary right-click
        // costs nothing; the model is synchronised only when one is actually hit.
        if (!_grammarSquiggleRanges.Any(range =>
                range.Start.IsInSameDocument(position) && range.Contains(position)))
        {
            return;
        }

        CommitToModel();
        if (GrammarIssueAt(position) is not { } issue)
            return;

        _grammarContextMenu = BuildGrammarContextMenu(issue);
        ContextMenu = _grammarContextMenu;
    }

    /// <summary>
    /// Word's right-click menu over a grammar issue: the shared grammar section (rule, suggestions, Ignore
    /// Once, Don't Check for This Issue) followed by the ordinary clipboard commands.
    /// </summary>
    internal ContextMenu BuildGrammarContextMenu(ProofingReviewIssue issue)
    {
        var menu = new ContextMenu();
        var canEdit = AllowsRestrictEditingOperation(RestrictEditingOperationKind.BodyTextEdit);
        foreach (var planned in ProofingContextMenuPlanner.BuildGrammar(issue, canEdit, UiText.Get).Items)
        {
            if (planned.Kind == RibbonMenuItemKind.Separator || planned.CommandId is not { } commandId)
            {
                menu.Items.Add(new Separator());
                continue;
            }

            var entry = new MenuItem { Header = planned.Header, IsEnabled = planned.IsEnabled, Tag = commandId.Value };
            entry.Click += (_, _) => ExecuteGrammarContextMenuCommand(issue, commandId);
            menu.Items.Add(entry);
        }

        // Framework commands supply their own localized captions and enablement.
        foreach (var command in new[] { ApplicationCommands.Cut, ApplicationCommands.Copy, ApplicationCommands.Paste })
            menu.Items.Add(new MenuItem { Command = command, CommandTarget = this });
        return menu;
    }

    internal void ExecuteGrammarContextMenuCommand(ProofingReviewIssue issue, RibbonCommandId commandId)
    {
        if (ProofingContextMenuPlanner.TryParseReplacement(commandId, out var index))
        {
            if (index < issue.Suggestions.Count)
            {
                var suggestion = issue.Suggestions[index];
                ApplyProofingReplacements(
                [
                    new ProofingReviewReplacement(
                        issue.BlockIndex,
                        suggestion.Offset,
                        suggestion.Length,
                        suggestion.Replacement),
                ]);
            }

            return;
        }

        switch (commandId.Value)
        {
            case ProofingContextMenuPlanner.GrammarIgnoreOnce:
                IgnoreProofingIssueOnce(issue);
                break;
            case ProofingContextMenuPlanner.GrammarIgnoreRule when issue.RuleId is { } ruleId:
                IgnoreGrammarRule(ruleId);
                break;
        }
    }

    private sealed class ProofingReviewHost(DocumentView editor) : IProofingReviewHost
    {
        public ProofingReviewPosition CaretPosition => editor.ProofingCaretPosition;

        public IReadOnlyList<ProofingReviewIssue> CollectIssues() => editor.CollectProofingReviewIssues();

        public void SelectIssue(ProofingReviewIssue issue) => editor.SelectProofingIssue(issue);

        public bool ApplyReplacements(IReadOnlyList<ProofingReviewReplacement> replacements) =>
            editor.ApplyProofingReplacements(replacements);

        public void IgnoreOnce(ProofingReviewIssue issue) => editor.IgnoreProofingIssueOnce(issue);

        public void IgnoreSpelling(ProofingReviewIssue issue) => editor.IgnoreProofingSpelling(issue);

        public void AddToDictionary(ProofingReviewIssue issue) => editor.AddProofingWordToDictionary(issue.Text);

        public void IgnoreGrammarRule(string ruleId) => editor.IgnoreGrammarRule(ruleId);
    }
}
