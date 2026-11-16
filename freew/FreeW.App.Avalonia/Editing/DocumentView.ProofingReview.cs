using FreeW.App.Presentation.Proofing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

public sealed partial class DocumentView
{
    private readonly HashSet<string> _proofingReviewIgnoredRules = new(StringComparer.Ordinal);
    private readonly HashSet<(string RuleId, string ParagraphText, int Offset)> _proofingReviewIgnoredOccurrences = [];

    internal IProofingReviewHost CreateProofingReviewHost() => new ProofingReviewHost(this);

    internal void SetProofingReviewActive(bool active)
    {
        _proofingReviewActive = active;
        InvalidateVisual();
    }

    private bool _proofingReviewActive;

    internal void ResetProofingReviewState()
    {
        _proofingReviewIgnoredRules.Clear();
        _proofingReviewIgnoredOccurrences.Clear();
        _proofingReviewActive = false;
    }

    private ProofingReviewPosition ProofingReviewCaretPosition
    {
        get
        {
            var start = NormalizedSelection()?.Start ?? _caret;
            return new ProofingReviewPosition(start.Block, start.Offset);
        }
    }

    private IReadOnlyList<ProofingReviewIssue> CollectProofingReviewIssues()
    {
        var options = _proofingReviewIgnoredRules.Count == 0
            ? GrammarCheckOptions.Default
            : new GrammarCheckOptions(_proofingReviewIgnoredRules);
        return ProofingDiagnosticPlanner.Build(_doc, spellCheckEnabled: true, _customDictionary.Words, options)
            .Where(diagnostic => !_ignoredProofingWords.Contains(diagnostic.NormalizedWord))
            .Where(diagnostic => diagnostic.Kind != ProofingDiagnosticKind.Grammar
                || diagnostic.RuleId is not { } ruleId
                || diagnostic.BlockIndex < 0
                || diagnostic.BlockIndex >= _doc.Blocks.Count
                || _doc.Blocks[diagnostic.BlockIndex] is not Paragraph paragraph
                || !_proofingReviewIgnoredOccurrences.Contains((ruleId, paragraph.PlainText, diagnostic.ParagraphOffset)))
            .Select(ProofingReviewIssue.FromDiagnostic)
            .ToArray();
    }

    private void SelectProofingReviewIssue(ProofingReviewIssue issue)
    {
        if (issue.BlockIndex < 0 || issue.BlockIndex >= _doc.Blocks.Count
            || _doc.Blocks[issue.BlockIndex] is not Paragraph paragraph)
            return;

        var start = Math.Clamp(issue.Offset, 0, paragraph.PlainText.Length);
        _selectionAnchor = new DocPosition(issue.BlockIndex, start);
        _caret = new DocPosition(issue.BlockIndex, Math.Clamp(start + issue.Length, start, paragraph.PlainText.Length));
        InvalidateVisual();
        CaretMoved?.Invoke();
    }

    private bool ApplyProofingReviewReplacements(IReadOnlyList<ProofingReviewReplacement> replacements)
    {
        if (replacements.Count == 0 || IsEditingLocked || _bus.IsUndoGroupOpen)
            return false;
        foreach (var replacement in replacements)
        {
            if (replacement.BlockIndex < 0 || replacement.BlockIndex >= _doc.Blocks.Count
                || _doc.Blocks[replacement.BlockIndex] is not Paragraph paragraph
                || replacement.Offset < 0 || replacement.Length < 0
                || replacement.Offset + replacement.Length > paragraph.PlainText.Length)
                return false;
        }

        _bus.BeginUndoGroup();
        try
        {
            foreach (var replacement in replacements)
            {
                _selectionAnchor = new DocPosition(replacement.BlockIndex, replacement.Offset);
                _caret = new DocPosition(replacement.BlockIndex, replacement.Offset + replacement.Length);
                ReplaceSelectionWith(replacement.Text);
            }
            _bus.CommitUndoGroup(replacements.Count > 1 ? "Change All" : "Spelling and Grammar");
            return true;
        }
        catch
        {
            _bus.RollbackUndoGroup();
            throw;
        }
    }

    private void IgnoreProofingReviewOnce(ProofingReviewIssue issue)
    {
        if (issue.Kind == ProofingReviewIssueKind.Spelling)
        {
            var normalized = ProofingDiagnosticPlanner.NormalizeWord(issue.Text);
            if (normalized is not null)
                _ignoredProofingWords.Add(normalized);
            return;
        }
        if (issue.RuleId is not { } ruleId || issue.BlockIndex < 0 || issue.BlockIndex >= _doc.Blocks.Count
            || _doc.Blocks[issue.BlockIndex] is not Paragraph paragraph)
            return;
        _proofingReviewIgnoredOccurrences.Add((ruleId, paragraph.PlainText, issue.Offset));
    }

    private void IgnoreProofingReviewSpelling(ProofingReviewIssue issue)
    {
        var normalized = ProofingDiagnosticPlanner.NormalizeWord(issue.Text);
        if (normalized is not null)
            _ignoredProofingWords.Add(normalized);
    }

    private void IgnoreProofingReviewRule(string ruleId)
    {
        _proofingReviewIgnoredRules.Add(ruleId);
        _proofingReviewIgnoredOccurrences.RemoveWhere(item => item.RuleId == ruleId);
        InvalidateVisual();
    }

    private sealed class ProofingReviewHost(DocumentView editor) : IProofingReviewHost
    {
        public ProofingReviewPosition CaretPosition => editor.ProofingReviewCaretPosition;
        public IReadOnlyList<ProofingReviewIssue> CollectIssues() => editor.CollectProofingReviewIssues();
        public void SelectIssue(ProofingReviewIssue issue) => editor.SelectProofingReviewIssue(issue);
        public bool ApplyReplacements(IReadOnlyList<ProofingReviewReplacement> replacements) =>
            editor.ApplyProofingReviewReplacements(replacements);
        public void IgnoreOnce(ProofingReviewIssue issue) => editor.IgnoreProofingReviewOnce(issue);
        public void IgnoreSpelling(ProofingReviewIssue issue) => editor.IgnoreProofingReviewSpelling(issue);
        public void AddToDictionary(ProofingReviewIssue issue) => editor.AddToDictionary(issue.Text);
        public void IgnoreGrammarRule(string ruleId) => editor.IgnoreProofingReviewRule(ruleId);
    }
}
