using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FreeW.Core.Model;

// ── Merge rule types ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The comparison operator used in a conditional merge rule (If…Then…Else, Skip Record If, Next
/// Record If). Matches the operators Word exposes in its Rules dialog.
/// </summary>
public enum MergeConditionOperator
{
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    IsBlank,
    IsNotBlank,
    Contains
}

/// <summary>
/// A single conditional expression: <c>FieldName Operator Value</c>. For <see cref="MergeConditionOperator.IsBlank"/>
/// and <see cref="MergeConditionOperator.IsNotBlank"/> the <see cref="Value"/> is ignored. For
/// <see cref="MergeConditionOperator.Contains"/> the comparison is case-insensitive substring. All
/// other comparisons are case-insensitive string comparisons (numeric comparison is tried first when
/// both sides parse as <see cref="double"/>).
/// </summary>
public sealed class MergeCondition
{
    public string FieldName { get; init; } = string.Empty;
    public MergeConditionOperator Operator { get; init; }
    public string Value { get; init; } = string.Empty;
}

/// <summary>
/// Carries bookmark values set by <c>«Set»</c> rules and Fill-in / Ask answers collected at merge
/// time. Passed through the entire merge run so later rules can reference earlier values.
/// </summary>
public sealed class MergeState
{
    /// <summary>Named bookmark values set by <c>«Set Name Value»</c> rules.</summary>
    public Dictionary<string, string> Bookmarks { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Answers given by the user for Fill-in prompts (keyed by prompt text, so repeated identical
    /// prompts reuse the same answer without asking again).
    /// </summary>
    public Dictionary<string, string> FillInAnswers { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Answers for Ask prompts (keyed by bookmark name). Ask sets a bookmark and the answer persists
    /// for the whole merge run.
    /// </summary>
    public Dictionary<string, string> AskAnswers { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Records whose 0-based index appears in this set are skipped in Finish &amp; Merge output.</summary>
    public HashSet<int> SkippedIndices { get; } = [];

    /// <summary>The count of non-skipped records emitted so far (the merge sequence number).</summary>
    public int SequenceNumber { get; set; }

    /// <summary>
    /// True when the most recently merged record requested an additional source-row advance.
    /// This is reset at the start of each rules-aware record merge.
    /// </summary>
    public bool AdvanceRecordRequested { get; internal set; }

    /// <summary>
    /// True when the most recently merged record requested that its source row be skipped.
    /// This is reset at the start of each rules-aware record merge.
    /// </summary>
    public bool SkipRecordRequested { get; internal set; }
}

public enum MailMergeInteractivePromptKind
{
    FillIn,
    Ask
}

public sealed record MailMergeInteractivePrompt(
    MailMergeInteractivePromptKind Kind,
    string Key,
    string Prompt,
    string DefaultAnswer = "");

/// <summary>
/// Finds the interactive merge-rule prompts that a host must collect before starting a merge run.
/// Prompts retain document order and are de-duplicated by Fill-in prompt or Ask bookmark, matching
/// Word's one-answer-per-merge-run behavior.
/// </summary>
public static class MailMergeInteractivePromptPlanner
{
    public static IReadOnlyList<MailMergeInteractivePrompt> Plan(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var prompts = new List<MailMergeInteractivePrompt>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var storyText in EnumerateStoryTexts(document))
        {
            var offset = 0;
            while (offset < storyText.Length)
            {
                var open = storyText.IndexOf(MailMerge.FieldOpen, offset);
                if (open < 0)
                    break;
                var close = storyText.IndexOf(MailMerge.FieldClose, open + 1);
                if (close < 0)
                    break;

                var instruction = storyText[(open + 1)..close].Trim();
                offset = close + 1;
                if (!MergeRuleEvaluator.TryParseInteractivePrompt(instruction, out var prompt))
                    continue;

                var identity = $"{prompt.Kind}:{prompt.Key}";
                if (seen.Add(identity))
                    prompts.Add(prompt);
            }
        }

        return prompts;
    }

    private static IEnumerable<string> EnumerateStoryTexts(TextDocument document)
    {
        foreach (var block in document.Blocks)
        {
            foreach (var text in EnumerateBlockStoryTexts(block))
                yield return text;
        }

        foreach (var text in EnumerateHeadersFootersStoryTexts(document.FinalSectionHeadersFooters))
            yield return text;
    }

    private static IEnumerable<string> EnumerateBlockStoryTexts(Block block)
    {
        switch (block)
        {
            case Paragraph paragraph:
                foreach (var text in EnumerateParagraphStoryTexts(paragraph))
                    yield return text;
                if (paragraph.SectionBreak is { } section)
                    foreach (var text in EnumerateHeadersFootersStoryTexts(section.HeadersFooters))
                        yield return text;
                break;
            case Table table:
                foreach (var paragraph in table.Rows
                             .SelectMany(row => row.Cells)
                             .SelectMany(cell => cell.Paragraphs))
                    foreach (var text in EnumerateParagraphStoryTexts(paragraph))
                        yield return text;
                break;
        }
    }

    private static IEnumerable<string> EnumerateParagraphStoryTexts(Paragraph paragraph)
    {
        yield return string.Concat(paragraph.Runs.Select(run =>
            run.ComplexField is { Keyword: "FILLIN" or "ASK" } field
            && ComplexFieldEngine.HasSwitch(field.Instruction, 'o')
                ? $"{MailMerge.FieldOpen}{field.Instruction}{MailMerge.FieldClose}"
                : run.Text));
        foreach (var run in paragraph.Runs)
        {
            if (run.Shape is { } shape)
                foreach (var nested in shape.TextParagraphs.SelectMany(EnumerateParagraphStoryTexts))
                    yield return nested;
            if (run.DrawingGroup is { } group)
                foreach (var nested in EnumerateDrawingGroupStoryTexts(group))
                    yield return nested;
        }
    }

    private static IEnumerable<string> EnumerateDrawingGroupStoryTexts(DrawingGroup group)
    {
        foreach (var child in group.Children)
        {
            if (child is Shape shape)
                foreach (var text in shape.TextParagraphs.SelectMany(EnumerateParagraphStoryTexts))
                    yield return text;
            if (child is DrawingGroup nested)
                foreach (var text in EnumerateDrawingGroupStoryTexts(nested))
                    yield return text;
        }
    }

    private static IEnumerable<string> EnumerateHeadersFootersStoryTexts(SectionHeadersFooters headersFooters)
    {
        foreach (var headerFooter in new[]
                 {
                     headersFooters.Header,
                     headersFooters.Footer,
                     headersFooters.EvenHeader,
                     headersFooters.EvenFooter,
                     headersFooters.FirstHeader,
                     headersFooters.FirstFooter
                 })
        {
            if (headerFooter is null)
                continue;
            foreach (var text in headerFooter.Paragraphs.SelectMany(EnumerateParagraphStoryTexts))
                yield return text;
        }
    }
}

/// <summary>
/// The result of evaluating a merge rule field instruction against a single data row.
/// </summary>
public readonly record struct MergeRuleResult(
    /// <summary>The text to emit in place of the field instruction.</summary>
    string Text,
    /// <summary>True when this result causes the current record to be skipped.</summary>
    bool SkipRecord,
    /// <summary>True when this result causes the record index to advance by one («Next Record If»).</summary>
    bool AdvanceRecord);

/// <summary>
/// Pure, deterministic evaluator for Word's conditional merge-rule field instructions. All rule
/// placeholders follow the same guillemet convention as ordinary merge fields: <c>«instruction»</c>.
/// The evaluator is stateless per-call except for the <see cref="MergeState"/> passed in.
/// <para>
/// Supported instructions (parsed from the text between guillemets):
/// <list type="bullet">
///   <item><c>If FieldName Op Value Then TrueText Else FalseText</c> — emit one literal vs another.</item>
///   <item><c>Skip Record If FieldName Op Value</c> — mark record as skipped when condition is true.</item>
///   <item><c>Next Record If FieldName Op Value</c> — advance to next record when condition is true.</item>
///   <item><c>Merge Sequence #</c> — the 1-based sequence number of non-skipped records emitted so far.</item>
///   <item><c>Fill-in Prompt</c> — emit the answer given by the user for this prompt (UI-level: the
///     answer is pre-populated in <see cref="MergeState.FillInAnswers"/> before calling).</item>
///   <item><c>Ask BookmarkName Prompt</c> — emit the bookmark value (answer pre-populated in
///     <see cref="MergeState.AskAnswers"/>); sets the bookmark in <see cref="MergeState.Bookmarks"/>.</item>
///   <item><c>Set BookmarkName Value</c> — set a named bookmark; emits nothing.</item>
///   <item><c>Ref BookmarkName</c> — emit the current value of a named bookmark.</item>
/// </list>
/// </para>
/// </summary>
public static class MergeRuleEvaluator
{
    public static bool TryParseInteractivePrompt(
        string instruction,
        out MailMergeInteractivePrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        var span = instruction.AsSpan().Trim();
        if (TryParsePrefix(span, "Fill-in ", out var afterFillIn))
        {
            var fillInPrompt = Unquote(afterFillIn.Trim());
            prompt = new MailMergeInteractivePrompt(
                MailMergeInteractivePromptKind.FillIn,
                fillInPrompt,
                fillInPrompt);
            return true;
        }

        if (TryParsePrefix(span, "FILLIN ", out var afterNativeFillIn))
        {
            var tokens = Tokenize(afterNativeFillIn.ToString());
            if (tokens.Count >= 1)
            {
                prompt = new MailMergeInteractivePrompt(
                    MailMergeInteractivePromptKind.FillIn,
                    tokens[0],
                    tokens[0],
                    SwitchArgument(tokens, 'd'));
                return true;
            }
        }

        if (TryParsePrefix(span, "Ask ", out var afterAsk))
        {
            var tokens = Tokenize(afterAsk.ToString());
            if (tokens.Count >= 1)
            {
                prompt = new MailMergeInteractivePrompt(
                    MailMergeInteractivePromptKind.Ask,
                    tokens[0],
                    tokens.Count >= 2 ? tokens[1] : string.Empty,
                    SwitchArgument(tokens, 'd'));
                return true;
            }
        }

        prompt = null!;
        return false;
    }

    public static bool TryParseBookmarkReference(string instruction, out string bookmarkName)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        var span = instruction.AsSpan().Trim();
        if (TryParsePrefix(span, "Ref ", out var afterRef))
        {
            var tokens = Tokenize(afterRef.ToString());
            if (tokens.Count >= 1)
            {
                bookmarkName = tokens[0];
                return true;
            }
        }

        bookmarkName = string.Empty;
        return false;
    }

    /// <summary>
    /// Returns the recipient field referenced by a valid conditional rule. Rules without a recipient-field
    /// operand (Set, Ref, Fill-in, Ask, and Merge Sequence #) return false.
    /// </summary>
    public static bool TryGetReferencedFieldName(string instruction, out string fieldName)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        var span = instruction.AsSpan().Trim();
        MergeCondition? condition = null;
        if (TryParsePrefix(span, "If ", out var afterIf)
            && TryParseConditionAndBranches(afterIf, out var ifCondition, out _, out _))
        {
            condition = ifCondition;
        }
        else if (TryParsePrefix(span, "Skip Record If ", out var afterSkip)
                 && TryParseCondition(afterSkip, out var skipCondition))
        {
            condition = skipCondition;
        }
        else if (TryParsePrefix(span, "Next Record If ", out var afterNext)
                 && TryParseCondition(afterNext, out var nextCondition))
        {
            condition = nextCondition;
        }

        fieldName = condition?.FieldName ?? string.Empty;
        return fieldName.Length > 0;
    }

    /// <summary>
    /// Evaluate a single field-instruction string (the text <em>between</em> the guillemets) against
    /// <paramref name="row"/> and <paramref name="state"/>. Returns a <see cref="MergeRuleResult"/>
    /// describing the text to emit and any control effects (skip/advance). Returns
    /// <c>default(MergeRuleResult)</c> (empty text, no effects) when the instruction is not recognised
    /// as a rule (so the caller can fall back to plain merge-field substitution).
    /// </summary>
    public static MergeRuleResult? Evaluate(
        string instruction,
        IReadOnlyDictionary<string, string> row,
        MergeState state,
        int recordIndex)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(state);

        var span = instruction.AsSpan().Trim();

        // ── Merge Sequence # ─────────────────────────────────────────────────────────────────────
        if (span.Equals("Merge Sequence #", StringComparison.OrdinalIgnoreCase))
            return new MergeRuleResult(state.SequenceNumber.ToString(), false, false);

        // ── Set BookmarkName Value ────────────────────────────────────────────────────────────────
        if (TryParsePrefix(span, "Set ", out var afterSet))
        {
            var setTokens = Tokenize(afterSet.ToString());
            if (setTokens.Count >= 1)
            {
                var bookmarkName = setTokens[0];
                // The value is everything after the bookmark name, unquoted.
                var rawValue = setTokens.Count >= 2 ? setTokens[1] : string.Empty;
                // Resolve any merge-field references inside the value.
                var resolvedValue = SubstituteRow(rawValue, row);
                state.Bookmarks[bookmarkName] = resolvedValue;
                return new MergeRuleResult(string.Empty, false, false);
            }
        }

        // ── Ref BookmarkName ─────────────────────────────────────────────────────────────────────
        if (TryParseBookmarkReference(instruction, out var referencedBookmarkName))
        {
            var value = state.Bookmarks.TryGetValue(referencedBookmarkName, out var bv) ? bv : string.Empty;
            return new MergeRuleResult(value, false, false);
        }

        // ── Fill-in Prompt ───────────────────────────────────────────────────────────────────────
        if (TryParsePrefix(span, "Fill-in ", out var afterFillIn))
        {
            var prompt = Unquote(afterFillIn.Trim());
            var answer = state.FillInAnswers.TryGetValue(prompt, out var fa) ? fa : string.Empty;
            return new MergeRuleResult(answer, false, false);
        }

        // ── Ask BookmarkName "Prompt" ─────────────────────────────────────────────────────────────
        if (TryParsePrefix(span, "Ask ", out var afterAsk))
        {
            var askTokens = Tokenize(afterAsk.ToString());
            if (askTokens.Count >= 1)
            {
                var bmName = askTokens[0];
                var prompt = askTokens.Count >= 2 ? askTokens[1] : string.Empty;
                var answer = state.AskAnswers.TryGetValue(bmName, out var aa) ? aa : string.Empty;
                state.Bookmarks[bmName] = answer;
                return new MergeRuleResult(answer, false, false);
            }
        }

        // ── If FieldName Op Value Then TrueText Else FalseText ───────────────────────────────────
        if (TryParsePrefix(span, "If ", out var afterIf))
        {
            if (TryParseConditionAndBranches(afterIf, out var cond, out var trueText, out var falseText))
            {
                var fieldValue = LookupField(row, cond.FieldName);
                var condMet = EvaluateCondition(fieldValue, cond.Operator, cond.Value);
                var emit = condMet ? SubstituteRow(trueText, row) : SubstituteRow(falseText, row);
                return new MergeRuleResult(emit, false, false);
            }
        }

        // ── Skip Record If FieldName Op Value ────────────────────────────────────────────────────
        if (TryParsePrefix(span, "Skip Record If ", out var afterSkip))
        {
            if (TryParseCondition(afterSkip, out var cond))
            {
                var fieldValue = LookupField(row, cond.FieldName);
                var condMet = EvaluateCondition(fieldValue, cond.Operator, cond.Value);
                if (condMet)
                    state.SkippedIndices.Add(recordIndex);
                return new MergeRuleResult(string.Empty, condMet, false);
            }
        }

        // ── Next Record If FieldName Op Value ────────────────────────────────────────────────────
        if (TryParsePrefix(span, "Next Record If ", out var afterNextIf))
        {
            if (TryParseCondition(afterNextIf, out var cond))
            {
                var fieldValue = LookupField(row, cond.FieldName);
                var condMet = EvaluateCondition(fieldValue, cond.Operator, cond.Value);
                return new MergeRuleResult(string.Empty, false, condMet);
            }
        }

        return null; // not a recognised rule instruction
    }

    /// <summary>
    /// Build the field instruction string (to be wrapped in guillemets) for an If…Then…Else rule.
    /// Field names containing spaces are quoted so the parser can round-trip them. For the
    /// <see cref="MergeConditionOperator.IsBlank"/> and <see cref="MergeConditionOperator.IsNotBlank"/>
    /// operators no comparison value is emitted (the value is ignored by the evaluator anyway).
    /// </summary>
    public static string BuildIfInstruction(string fieldName, MergeConditionOperator op, string value, string trueText, string falseText)
    {
        var condPart = BuildConditionPart(fieldName, op, value);
        return $"If {condPart} Then {Quote(trueText)} Else {Quote(falseText)}";
    }

    /// <summary>Build the field instruction string for a Skip Record If rule.</summary>
    public static string BuildSkipRecordIfInstruction(string fieldName, MergeConditionOperator op, string value) =>
        $"Skip Record If {BuildConditionPart(fieldName, op, value)}";

    /// <summary>Build the field instruction string for a Next Record If rule.</summary>
    public static string BuildNextRecordIfInstruction(string fieldName, MergeConditionOperator op, string value) =>
        $"Next Record If {BuildConditionPart(fieldName, op, value)}";

    // Build the "FieldName Op [Value]" portion of a condition, omitting the value for blank operators.
    private static string BuildConditionPart(string fieldName, MergeConditionOperator op, string value)
    {
        var opToken = OperatorToToken(op);
        if (op == MergeConditionOperator.IsBlank || op == MergeConditionOperator.IsNotBlank)
            return $"{QuoteIfNeeded(fieldName)} {opToken}";
        return $"{QuoteIfNeeded(fieldName)} {opToken} {Quote(value)}";
    }

    /// <summary>Build the field instruction string for a Set Bookmark rule.</summary>
    public static string BuildSetInstruction(string bookmarkName, string value) =>
        $"Set {bookmarkName} {Quote(value)}";

    /// <summary>Build the field instruction string for a Ref Bookmark rule.</summary>
    public static string BuildRefInstruction(string bookmarkName) => $"Ref {bookmarkName}";

    /// <summary>Build the field instruction string for a Fill-in rule.</summary>
    public static string BuildFillInInstruction(string prompt) => $"Fill-in {Quote(prompt)}";

    /// <summary>Build the field instruction string for an Ask rule.</summary>
    public static string BuildAskInstruction(string bookmarkName, string prompt) => $"Ask {bookmarkName} {Quote(prompt)}";

    // ── Condition evaluation ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Evaluate <paramref name="fieldValue"/> against <paramref name="op"/> and
    /// <paramref name="ruleValue"/>. Numeric comparison is attempted when both sides parse as
    /// <see cref="double"/>; otherwise lexicographic (case-insensitive) string comparison.
    /// </summary>
    public static bool EvaluateCondition(string fieldValue, MergeConditionOperator op, string ruleValue)
    {
        return op switch
        {
            MergeConditionOperator.IsBlank       => string.IsNullOrWhiteSpace(fieldValue),
            MergeConditionOperator.IsNotBlank    => !string.IsNullOrWhiteSpace(fieldValue),
            MergeConditionOperator.Contains      => fieldValue.Contains(ruleValue, StringComparison.OrdinalIgnoreCase),
            _ => CompareValues(fieldValue, ruleValue, op)
        };
    }

    private static bool CompareValues(string fieldValue, string ruleValue, MergeConditionOperator op)
    {
        // Try numeric comparison first.
        if (double.TryParse(fieldValue, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var fNum) &&
            double.TryParse(ruleValue,  System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var rNum))
        {
            return op switch
            {
                MergeConditionOperator.Equal              => fNum == rNum,
                MergeConditionOperator.NotEqual           => fNum != rNum,
                MergeConditionOperator.LessThan           => fNum  < rNum,
                MergeConditionOperator.LessThanOrEqual    => fNum <= rNum,
                MergeConditionOperator.GreaterThan        => fNum  > rNum,
                MergeConditionOperator.GreaterThanOrEqual => fNum >= rNum,
                _ => false
            };
        }

        // Fallback: case-insensitive string comparison.
        var cmp = string.Compare(fieldValue, ruleValue, StringComparison.OrdinalIgnoreCase);
        return op switch
        {
            MergeConditionOperator.Equal              => cmp == 0,
            MergeConditionOperator.NotEqual           => cmp != 0,
            MergeConditionOperator.LessThan           => cmp  < 0,
            MergeConditionOperator.LessThanOrEqual    => cmp <= 0,
            MergeConditionOperator.GreaterThan        => cmp  > 0,
            MergeConditionOperator.GreaterThanOrEqual => cmp >= 0,
            _ => false
        };
    }

    // ── Parsing helpers ──────────────────────────────────────────────────────────────────────────

    private static bool TryParseConditionAndBranches(
        ReadOnlySpan<char> span,
        out MergeCondition condition,
        out string trueText,
        out string falseText)
    {
        condition = new MergeCondition();
        trueText = falseText = string.Empty;

        // Parse: FieldName Op Value Then "TrueText" Else "FalseText"
        // The FieldName ends at the first recognized operator keyword or symbol.
        if (!TryParseConditionCore(span, out condition, out var rest))
            return false;

        // Expect "Then" keyword.
        rest = rest.TrimStart();
        if (!TryParsePrefix(rest, "Then ", out var afterThen))
            return false;
        afterThen = afterThen.TrimStart();

        // Then-text (optionally quoted), followed by optional " Else ...".
        if (TryParsePrefix(afterThen, "Else ", out _))
        {
            // Empty then-text.
            var elseStart = afterThen;
            if (!TryParsePrefix(elseStart, "Else ", out var afterElse))
                return false;
            trueText = string.Empty;
            falseText = Unquote(afterElse.Trim());
            return true;
        }

        // Extract the quoted or unquoted then-text.
        string rawThen;
        if (afterThen.Length > 0 && afterThen[0] == '"')
        {
            if (!TryReadQuoted(afterThen, out rawThen, out var afterQuote))
                return false;
            afterQuote = afterQuote.TrimStart();
            if (TryParsePrefix(afterQuote, "Else ", out var afterElse2))
                falseText = Unquote(afterElse2.Trim());
            trueText = rawThen;
        }
        else
        {
            // Unquoted: everything before " Else " (if present) is the then-text.
            var elseIdx = IndexOfKeyword(afterThen, " Else ");
            if (elseIdx >= 0)
            {
                trueText  = afterThen.Slice(0, elseIdx).Trim().ToString();
                falseText = Unquote(afterThen.Slice(elseIdx + 6).Trim());
            }
            else
            {
                trueText  = afterThen.Trim().ToString();
                falseText = string.Empty;
            }
        }

        return true;
    }

    private static bool TryParseCondition(ReadOnlySpan<char> span, out MergeCondition condition)
    {
        condition = new MergeCondition();
        if (!TryParseConditionCore(span, out condition, out _))
            return false;
        return true;
    }

    // Core parser: FieldName Op Value → condition; rest = text after the value.
    // Op may be a symbol (=, <>, <, <=, >, >=) or a keyword (is blank, is not blank, contains).
    private static bool TryParseConditionCore(ReadOnlySpan<char> span, out MergeCondition condition, out ReadOnlySpan<char> rest)
    {
        condition = new MergeCondition();
        rest = default;

        span = span.TrimStart();
        if (span.IsEmpty)
            return false;

        // Field name: up to the first space followed by operator.
        // We tokenise by splitting on whitespace sequences.
        var tokens = Tokenize(span.ToString());
        if (tokens.Count < 2)
            return false;

        // Match: FieldName (Op | "is blank" | "is not blank" | "contains") [Value]
        var fieldName = tokens[0];

        // 2-word operators: "is blank", "is not blank", "contains"
        if (tokens.Count >= 3 && tokens[1].Equals("is", StringComparison.OrdinalIgnoreCase) &&
            tokens[2].Equals("blank", StringComparison.OrdinalIgnoreCase))
        {
            condition = new MergeCondition { FieldName = fieldName, Operator = MergeConditionOperator.IsBlank, Value = string.Empty };
            rest = ConsumeTokens(span, 3);
            return true;
        }

        if (tokens.Count >= 4 && tokens[1].Equals("is", StringComparison.OrdinalIgnoreCase) &&
            tokens[2].Equals("not", StringComparison.OrdinalIgnoreCase) &&
            tokens[3].Equals("blank", StringComparison.OrdinalIgnoreCase))
        {
            condition = new MergeCondition { FieldName = fieldName, Operator = MergeConditionOperator.IsNotBlank, Value = string.Empty };
            rest = ConsumeTokens(span, 4);
            return true;
        }

        if (tokens.Count >= 3 && tokens[1].Equals("contains", StringComparison.OrdinalIgnoreCase))
        {
            var value = Unquote(tokens[2].AsSpan());
            condition = new MergeCondition { FieldName = fieldName, Operator = MergeConditionOperator.Contains, Value = value };
            rest = ConsumeTokens(span, 3);
            return true;
        }

        // Symbolic operators.
        if (tokens.Count >= 3 && TryParseOperator(tokens[1].AsSpan(), out var op))
        {
            var rawValue = tokens[2];
            // Allow the value to be the rest of the string (for multi-word unquoted values
            // in Then/Else context the caller handles the rest).
            var value = Unquote(rawValue.AsSpan());
            condition = new MergeCondition { FieldName = fieldName, Operator = op, Value = value };
            rest = ConsumeTokens(span, 3);
            return true;
        }

        return false;
    }

    private static bool TryParseOperator(ReadOnlySpan<char> token, out MergeConditionOperator op)
    {
        if (token.SequenceEqual("=".AsSpan()))           { op = MergeConditionOperator.Equal;              return true; }
        if (token.SequenceEqual("<>".AsSpan()))          { op = MergeConditionOperator.NotEqual;           return true; }
        if (token.SequenceEqual("!=".AsSpan()))          { op = MergeConditionOperator.NotEqual;           return true; }
        if (token.SequenceEqual("<=".AsSpan()))          { op = MergeConditionOperator.LessThanOrEqual;    return true; }
        if (token.SequenceEqual(">=".AsSpan()))          { op = MergeConditionOperator.GreaterThanOrEqual; return true; }
        if (token.SequenceEqual("<".AsSpan()))           { op = MergeConditionOperator.LessThan;           return true; }
        if (token.SequenceEqual(">".AsSpan()))           { op = MergeConditionOperator.GreaterThan;        return true; }
        if (token.Equals("contains".AsSpan(), StringComparison.OrdinalIgnoreCase)) { op = MergeConditionOperator.Contains; return true; }
        op = default;
        return false;
    }

    private static string OperatorToToken(MergeConditionOperator op) => op switch
    {
        MergeConditionOperator.Equal              => "=",
        MergeConditionOperator.NotEqual           => "<>",
        MergeConditionOperator.LessThan           => "<",
        MergeConditionOperator.LessThanOrEqual    => "<=",
        MergeConditionOperator.GreaterThan        => ">",
        MergeConditionOperator.GreaterThanOrEqual => ">=",
        MergeConditionOperator.IsBlank            => "is blank",
        MergeConditionOperator.IsNotBlank         => "is not blank",
        MergeConditionOperator.Contains           => "contains",
        _ => "="
    };

    // Split into whitespace-separated tokens (respecting quoted strings as single tokens).
    private static List<string> Tokenize(string s)
    {
        var tokens = new List<string>();
        var i = 0;
        while (i < s.Length)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            if (i >= s.Length) break;
            if (s[i] == '"')
            {
                var sb = new StringBuilder();
                i++; // skip opening quote
                while (i < s.Length)
                {
                    if (s[i] == '"')
                    {
                        if (i + 1 < s.Length && s[i + 1] == '"') { sb.Append('"'); i += 2; }
                        else { i++; break; }
                    }
                    else { sb.Append(s[i++]); }
                }
                tokens.Add(sb.ToString());
            }
            else
            {
                var start = i;
                while (i < s.Length && !char.IsWhiteSpace(s[i])) i++;
                tokens.Add(s.Substring(start, i - start));
            }
        }
        return tokens;
    }

    private static string SwitchArgument(IReadOnlyList<string> tokens, char switchLetter)
    {
        var switchToken = $"\\{switchLetter}";
        for (var i = 0; i + 1 < tokens.Count; i++)
        {
            if (tokens[i].Equals(switchToken, StringComparison.OrdinalIgnoreCase))
                return tokens[i + 1];
        }

        return string.Empty;
    }

    // Return the portion of span after consuming n whitespace-separated tokens.
    private static ReadOnlySpan<char> ConsumeTokens(ReadOnlySpan<char> span, int n)
    {
        var rest = span.TrimStart();
        for (var i = 0; i < n && !rest.IsEmpty; i++)
        {
            if (rest.Length > 0 && rest[0] == '"')
            {
                // Skip quoted token.
                var j = 1;
                while (j < rest.Length)
                {
                    if (rest[j] == '"') { j++; if (j < rest.Length && rest[j] == '"') j++; else break; }
                    else j++;
                }
                rest = rest.Slice(j).TrimStart();
            }
            else
            {
                var j = 0;
                while (j < rest.Length && !char.IsWhiteSpace(rest[j])) j++;
                rest = rest.Slice(j).TrimStart();
            }
        }
        return rest;
    }

    private static bool TryParsePrefix(ReadOnlySpan<char> span, string prefix, out ReadOnlySpan<char> rest)
    {
        if (span.StartsWith(prefix.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            rest = span.Slice(prefix.Length);
            return true;
        }
        rest = default;
        return false;
    }

    private static void SplitFirstToken(ReadOnlySpan<char> span, out ReadOnlySpan<char> first, out ReadOnlySpan<char> rest)
    {
        span = span.TrimStart();
        var j = 0;
        while (j < span.Length && !char.IsWhiteSpace(span[j])) j++;
        first = span.Slice(0, j);
        rest  = j < span.Length ? span.Slice(j + 1) : ReadOnlySpan<char>.Empty;
    }

    // Unquote a double-quoted string span (or return the raw string if not quoted).
    private static string Unquote(ReadOnlySpan<char> s)
    {
        s = s.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
        {
            var inner = s.Slice(1, s.Length - 2).ToString();
            return inner.Replace("\"\"", "\"");
        }
        return s.ToString();
    }

    private static bool TryReadQuoted(ReadOnlySpan<char> span, out string value, out ReadOnlySpan<char> rest)
    {
        value = string.Empty;
        rest = default;
        if (span.IsEmpty || span[0] != '"') return false;
        var sb = new StringBuilder();
        var i = 1;
        while (i < span.Length)
        {
            if (span[i] == '"')
            {
                if (i + 1 < span.Length && span[i + 1] == '"') { sb.Append('"'); i += 2; }
                else { i++; break; }
            }
            else sb.Append(span[i++]);
        }
        value = sb.ToString();
        rest = span.Slice(i);
        return true;
    }

    // Case-insensitive index-of keyword in a span.
    private static int IndexOfKeyword(ReadOnlySpan<char> span, string keyword)
    {
        var s = span.ToString();
        var idx = s.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        return idx;
    }

    // Quote a string for embedding in a field instruction (double-quote escaping).
    private static string Quote(string s) => $"\"{s.Replace("\"", "\"\"")}\"";

    // Quote only when the string contains whitespace (field names with spaces must be quoted for the
    // tokeniser to treat them as a single token; plain names without spaces round-trip without quotes).
    private static string QuoteIfNeeded(string s) => s.Any(char.IsWhiteSpace) ? Quote(s) : s;

    private static string LookupField(IReadOnlyDictionary<string, string> row, string name) =>
        row.TryGetValue(name, out var v) ? v ?? string.Empty : string.Empty;

    private static string SubstituteRow(string text, IReadOnlyDictionary<string, string> row)
    {
        if (text.IndexOf('«') < 0)
            return text;
        var sb = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '«')
            {
                var close = text.IndexOf('»', i + 1);
                if (close < 0) { sb.Append(text, i, text.Length - i); break; }
                var name = text.Substring(i + 1, close - i - 1).Trim();
                sb.Append(LookupField(row, name));
                i = close + 1;
            }
            else sb.Append(text[i++]);
        }
        return sb.ToString();
    }
}

/// <summary>
/// The semantic roles Word maps recipient-list columns to when composing an Address Block or Greeting
/// Line. Each role represents a distinct piece of contact information; the <see cref="FieldMapping"/>
/// records which data-source column name is bound to each role.
/// </summary>
public enum FieldRole
{
    Title,
    FirstName,
    MiddleName,
    LastName,
    Suffix,
    Company,
    Address1,
    Address2,
    City,
    State,
    PostalCode,
    Country
}

/// <summary>
/// Maps each <see cref="FieldRole"/> to a column name in the active data source. A null value means the
/// role is unmapped (the field is omitted from the composed block). Instances are mutable so the Match
/// Fields dialog can update individual bindings without creating a new object.
/// </summary>
public sealed class FieldMapping
{
    private readonly Dictionary<FieldRole, string?> _map = new();

    /// <summary>Get or set the column name bound to <paramref name="role"/> (null = unmapped).</summary>
    public string? this[FieldRole role]
    {
        get => _map.TryGetValue(role, out var v) ? v : null;
        set => _map[role] = value;
    }

    /// <summary>All roles explicitly stored in this mapping (mapped or null).</summary>
    public IEnumerable<FieldRole> MappedRoles => _map.Keys;
}

/// <summary>
/// A simple mail-merge data source: an ordered header of field names plus zero or more rows, each row
/// mapping a field name to its value for that record. Field names are matched case-insensitively (so a
/// template field «Name» binds to a "name" header), mirroring how Word treats merge-field names. The
/// store is pure model data with no docx part of its own; the merge engine (see <see cref="MailMerge"/>)
/// substitutes the values into ordinary text runs.
/// </summary>
public sealed class MergeData
{
    private readonly List<IReadOnlyDictionary<string, string>> _rows = [];

    /// <summary>
    /// Create a data source from a header (the field names, in order) and rows of values. Each row is a
    /// list of cell values positionally matched to the header; rows shorter than the header are padded
    /// with empty strings, and extra cells beyond the header are ignored. Header names are trimmed.
    /// </summary>
    public MergeData(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(rows);

        Header = header.Select(h => (h ?? string.Empty).Trim()).ToList();
        foreach (var cells in rows)
        {
            ArgumentNullException.ThrowIfNull(cells);
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < Header.Count; i++)
                row[Header[i]] = i < cells.Count ? cells[i] ?? string.Empty : string.Empty;
            _rows.Add(row);
        }
    }

    /// <summary>The field names, in header order (trimmed).</summary>
    public IReadOnlyList<string> Header { get; }

    /// <summary>The records, each a case-insensitive map from field name to value.</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, string>> Rows => _rows;

    /// <summary>The number of records (rows) in the data source.</summary>
    public int Count => _rows.Count;

    /// <summary>
    /// Parse a data source from CSV text. The first non-empty content forms the header line; each
    /// subsequent line is a record. Fields may be quoted with double quotes to embed commas, newlines,
    /// or doubled quotes (<c>""</c> → a literal <c>"</c>), following the usual CSV conventions. Both
    /// CRLF and LF line endings are accepted. An empty/blank input yields an empty data source (no
    /// header, no rows).
    /// </summary>
    public static MergeData FromCsv(string csv)
    {
        ArgumentNullException.ThrowIfNull(csv);

        var records = ParseCsv(csv);
        if (records.Count == 0)
            return new MergeData([], []);

        var header = records[0];
        var rows = records.Skip(1).ToList();
        return new MergeData(header, rows);
    }

    // Tokenise CSV into a list of records (each a list of field strings), honouring double-quoted fields
    // (with embedded commas/newlines and "" escapes). Fully blank lines outside quotes are skipped.
    private static List<List<string>> ParseCsv(string csv)
    {
        var records = new List<List<string>>();
        var field = new StringBuilder();
        var record = new List<string>();
        var inQuotes = false;
        var sawAny = false;

        void EndField()
        {
            record.Add(field.ToString());
            field.Clear();
        }

        void EndRecord()
        {
            EndField();
            // Skip records that are entirely empty (a single blank field) — typically a trailing newline.
            if (record.Count == 1 && record[0].Length == 0)
            {
                record = [];
                return;
            }
            records.Add(record);
            record = [];
        }

        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < csv.Length && csv[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    sawAny = true;
                    break;
                case ',':
                    sawAny = true;
                    EndField();
                    break;
                case '\r':
                    // Swallow CR; the following LF (if any) ends the record.
                    if (i + 1 < csv.Length && csv[i + 1] == '\n')
                        i++;
                    EndRecord();
                    break;
                case '\n':
                    EndRecord();
                    break;
                default:
                    sawAny = true;
                    field.Append(c);
                    break;
            }
        }

        // Flush the final record if the input did not end with a newline.
        if (field.Length > 0 || record.Count > 0 || (sawAny && records.Count == 0))
            EndRecord();

        return records;
    }
}

/// <summary>Backed FreeW subset of Word's Start Mail Merge document types.</summary>
public enum MailMergeOutputMode
{
    /// <summary>Start each merged record on a new page, matching Word's letter-style merge output.</summary>
    Letters,

    /// <summary>Append records continuously, matching Word's directory/catalog-style merge output.</summary>
    Directory
}

/// <summary>Where an e-mail merge should place the merged document content. Planning only; no mail is sent.</summary>
public enum MailMergeEmailOutputFormat
{
    MessageBody,
    Attachment
}

/// <summary>The body format requested for an e-mail merge delivery plan. Planning only; no mail is sent.</summary>
public enum MailMergeEmailBodyFormat
{
    Html,
    PlainText
}

/// <summary>The recipient records a planned e-mail merge should target.</summary>
public enum MailMergeEmailRecordScope
{
    AllRecords,
    CurrentRecord,
    SelectedRecords
}

/// <summary>
/// User intent for Word-style Send E-mail Messages. This is a delivery plan only: FreeW records the
/// chosen recipient field, subject, format and record range but does not send mail or require Outlook.
/// </summary>
public sealed record MailMergeEmailDeliveryIntent(
    string RecipientAddressField,
    string Subject,
    MailMergeEmailOutputFormat OutputFormat,
    MailMergeEmailBodyFormat BodyFormat,
    MailMergeEmailRecordScope RecordScope,
    int CurrentRecordIndex = 0,
    IReadOnlyList<int>? SelectedRecordIndexes = null);

/// <summary>
/// Validated e-mail merge plan derived from a <see cref="MailMergeEmailDeliveryIntent"/> and recipient
/// data. Errors block a useful plan; warnings describe risky but inspectable choices such as blank
/// subjects or records without an e-mail address. No message delivery happens here.
/// </summary>
public sealed record MailMergeEmailDeliveryPlan(
    MailMergeEmailDeliveryIntent Intent,
    IReadOnlyList<int> RecordIndexes,
    IReadOnlyList<int> DeliverableRecordIndexes,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public bool IsReady => Errors.Count == 0;
}

/// <summary>
/// Pure, deterministic mail-merge helpers over the FreeW document model. A merge field is the literal
/// text <c>«FieldName»</c> — the field name wrapped in guillemets (U+00AB «, U+00BB ») — carried inside
/// ordinary run text, so it round-trips through docx as plain text with no special part. The engine
/// discovers field names, substitutes a record's values into the field placeholders, and produces one
/// merged document per record.
/// <para>
/// Missing-field policy: when a placeholder names a field that the data row does not contain, the
/// placeholder is replaced with an <b>empty string</b> (the field is dropped, matching Word's behaviour
/// for an empty merge value). A field whose row value is the empty string is likewise substituted to
/// empty. The placeholder delimiters themselves are always removed for any well-formed «Field».
/// </para>
/// </summary>
public static class MailMerge
{
    /// <summary>The opening merge-field delimiter (left guillemet, U+00AB).</summary>
    public const char FieldOpen = '«';

    /// <summary>The closing merge-field delimiter (right guillemet, U+00BB).</summary>
    public const char FieldClose = '»';

    /// <summary>Build Word's native field-code instruction for an ordinary recipient column.</summary>
    public static string BuildMergeFieldInstruction(string fieldName)
    {
        ArgumentNullException.ThrowIfNull(fieldName);
        var normalized = NormalizeMergeFieldName(fieldName);
        if (normalized.Length == 0)
            return string.Empty;

        var serializedName = normalized.Any(char.IsWhiteSpace) || normalized.Contains('"')
            ? "\"" + normalized.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : normalized;
        return $" MERGEFIELD {serializedName} \\* MERGEFORMAT ";
    }

    /// <summary>Extract the recipient column name from an imported or authored native MERGEFIELD.</summary>
    public static bool TryGetMergeFieldName(ComplexField? field, out string fieldName)
    {
        fieldName = string.Empty;
        if (field?.Keyword != "MERGEFIELD")
            return false;

        fieldName = ComplexFieldEngine.Argument(field.Instruction).Trim();
        return fieldName.Length > 0;
    }

    /// <summary>Normalize a merge-field name accepted from a dialog or existing guillemet label.</summary>
    public static string NormalizeMergeFieldName(string fieldName)
    {
        ArgumentNullException.ThrowIfNull(fieldName);
        return fieldName.Trim().Trim(FieldOpen, FieldClose).Trim();
    }

    /// <summary>
    /// The placeholder text (without guillemets) for the «Next Record» special field. During
    /// <see cref="SubstituteSpecial"/> this causes the record index to advance by one so a single
    /// template can emit multiple records (used in directory / label layouts).
    /// </summary>
    public const string NextRecordField = "Next Record";

    /// <summary>
    /// The placeholder text (without guillemets) for the «Merge Record #» special field. During
    /// <see cref="SubstituteSpecial"/> this is replaced by the 1-based record index.
    /// </summary>
    public const string MergeRecordNumberField = "Merge Record #";

    /// <summary>
    /// The placeholder text (without guillemets) for the «Merge Sequence #» special field. During
    /// <see cref="SubstituteSpecialWithRules"/> this is replaced by the 1-based count of non-skipped
    /// records emitted so far — distinct from <see cref="MergeRecordNumberField"/> which is the absolute
    /// row index. Stored as a constant so it round-trips through the document as plain text.
    /// </summary>
    public const string MergeSequenceNumberField = "Merge Sequence #";

    /// <summary>Native Word field-code instruction for <see cref="NextRecordField"/>.</summary>
    public const string NextRecordInstruction = "NEXT";

    /// <summary>Native Word field-code instruction for <see cref="MergeRecordNumberField"/>.</summary>
    public const string MergeRecordNumberInstruction = "MERGEREC";

    /// <summary>Native Word field-code instruction for <see cref="MergeSequenceNumberField"/>.</summary>
    public const string MergeSequenceNumberInstruction = "MERGESEQ";

    /// <summary>Native Word field-code instruction for the default address block.</summary>
    public const string AddressBlockInstruction = " ADDRESSBLOCK \\* MERGEFORMAT ";

    /// <summary>Native Word field-code instruction for the default formal greeting line.</summary>
    public const string GreetingLineInstruction =
        " GREETINGLINE \\f \"<<_BEFORE_ Dear >><<_TITLE0_ >><<_LAST0_>><<_AFTER_ ,>>\" "
        + "\\e \"Dear Sir or Madam,\" \\l 1033 \\* MERGEFORMAT ";

    /// <summary>Maps FreeW's visible special-field label to Word's native field instruction.</summary>
    public static bool TryGetNativeSpecialFieldInstruction(string fieldName, out string instruction)
    {
        instruction = fieldName.Trim() switch
        {
            var name when name.Equals(NextRecordField, StringComparison.OrdinalIgnoreCase) => NextRecordInstruction,
            var name when name.Equals(MergeRecordNumberField, StringComparison.OrdinalIgnoreCase) => MergeRecordNumberInstruction,
            var name when name.Equals(MergeSequenceNumberField, StringComparison.OrdinalIgnoreCase) => MergeSequenceNumberInstruction,
            _ => string.Empty
        };
        return instruction.Length > 0;
    }

    private static readonly string[] EmailFieldSynonyms =
    [
        "email",
        "emailaddress",
        "emailaddr",
        "eaddress",
        "mail",
        "recipientemail",
        "recipientaddress",
        "to",
        "toaddress"
    ];

    // ── Canonical synonyms for each role used by AutoMatchFields (case-insensitive) ────────────────
    private static readonly Dictionary<FieldRole, string[]> RoleSynonyms = new()
    {
        [FieldRole.Title]      = ["title", "salutation", "honorific"],
        [FieldRole.FirstName]  = ["firstname", "first name", "first", "givenname", "given name"],
        [FieldRole.MiddleName] = ["middlename", "middle name", "middle", "middleinitial", "middle initial"],
        [FieldRole.LastName]   = ["lastname", "last name", "last", "surname", "familyname", "family name"],
        [FieldRole.Suffix]     = ["suffix"],
        [FieldRole.Company]    = ["company", "organization", "organisation", "companyname", "company name", "org"],
        [FieldRole.Address1]   = ["address1", "address 1", "address", "street", "streetaddress", "street address", "addr1"],
        [FieldRole.Address2]   = ["address2", "address 2", "addr2"],
        [FieldRole.City]       = ["city", "town", "locality"],
        [FieldRole.State]      = ["state", "province", "region"],
        [FieldRole.PostalCode] = ["postalcode", "postal code", "zip", "zipcode", "zip code", "postcode", "post code"],
        [FieldRole.Country]    = ["country", "countryorregion", "country or region", "nation"],
    };

    /// <summary>
    /// Auto-match a list of column headers to <see cref="FieldRole"/>s using case-insensitive
    /// heuristics (synonym matching). Each role is bound to the first header that matches any of its
    /// known synonyms; unmatched roles are left null. The returned mapping seeds the Match Fields dialog.
    /// </summary>
    public static FieldMapping AutoMatchFields(IReadOnlyList<string> header)
    {
        ArgumentNullException.ThrowIfNull(header);

        var mapping = new FieldMapping();
        // Build a lookup from normalized header → original header name.
        var normalized = header
            .Select(h => (Normalized: Normalize(h), Original: h))
            .ToList();

        foreach (var (role, synonyms) in RoleSynonyms)
        {
            foreach (var (norm, orig) in normalized)
            {
                if (Array.Exists(synonyms, s => s.Equals(norm, StringComparison.OrdinalIgnoreCase)))
                {
                    mapping[role] = orig;
                    break;
                }
            }
        }

        return mapping;

        static string Normalize(string s) => s.Trim().Replace("_", " ");
    }

    /// <summary>
    /// Pick the best recipient e-mail column from a data-source header using common Word/mail-merge names.
    /// Returns <c>null</c> when no likely e-mail column exists.
    /// </summary>
    public static string? SuggestEmailAddressField(IReadOnlyList<string> header)
    {
        ArgumentNullException.ThrowIfNull(header);

        foreach (var column in header)
        {
            var normalized = NormalizeEmailHeader(column);
            if (EmailFieldSynonyms.Any(s => s.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
                return column;
        }

        return null;
    }

    /// <summary>
    /// Validate and materialize a Word-style e-mail merge delivery plan. The returned plan is an exposure
    /// artifact only: it identifies intended recipients and warnings but never sends mail.
    /// </summary>
    public static MailMergeEmailDeliveryPlan CreateEmailDeliveryPlan(
        MergeData data,
        MailMergeEmailDeliveryIntent intent)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(intent);

        var errors = new List<string>();
        var warnings = new List<string>();

        var recipientField = (intent.RecipientAddressField ?? string.Empty).Trim();
        var normalizedIntent = intent with
        {
            RecipientAddressField = recipientField,
            Subject = intent.Subject?.Trim() ?? string.Empty,
            CurrentRecordIndex = Math.Max(0, intent.CurrentRecordIndex),
            SelectedRecordIndexes = intent.SelectedRecordIndexes?.ToArray() ?? []
        };

        if (!Enum.IsDefined(typeof(MailMergeEmailOutputFormat), normalizedIntent.OutputFormat))
            errors.Add("Choose a valid e-mail output format.");
        if (!Enum.IsDefined(typeof(MailMergeEmailBodyFormat), normalizedIntent.BodyFormat))
            errors.Add("Choose a valid e-mail body format.");
        if (!Enum.IsDefined(typeof(MailMergeEmailRecordScope), normalizedIntent.RecordScope))
            errors.Add("Choose a valid recipient record range.");

        if (data.Count == 0)
            errors.Add("Recipient data source has no records.");

        var hasRecipientField = false;
        if (recipientField.Length == 0)
        {
            errors.Add("Choose a recipient e-mail address field.");
        }
        else
        {
            hasRecipientField = data.Header.Any(h => h.Equals(recipientField, StringComparison.OrdinalIgnoreCase));
            if (!hasRecipientField)
                errors.Add($"Recipient e-mail address field '{recipientField}' is not in the recipient data source.");
        }

        if (normalizedIntent.Subject.Length == 0)
            warnings.Add("Subject line is blank.");

        var recordIndexes = ResolveEmailRecordIndexes(data, normalizedIntent, warnings);
        if (recordIndexes.Count == 0)
            errors.Add("No recipient records are selected for the e-mail merge.");

        var deliverableIndexes = new List<int>();
        if (hasRecipientField)
        {
            foreach (var index in recordIndexes)
            {
                var row = data.Rows[index];
                var address = row.TryGetValue(recipientField, out var value) ? value : string.Empty;
                if (string.IsNullOrWhiteSpace(address))
                    warnings.Add($"Record {index + 1} has no e-mail address in '{recipientField}'.");
                else
                    deliverableIndexes.Add(index);
            }

            if (recordIndexes.Count > 0 && deliverableIndexes.Count == 0)
                errors.Add($"No selected records have an e-mail address in '{recipientField}'.");
        }

        return new MailMergeEmailDeliveryPlan(
            normalizedIntent,
            recordIndexes,
            deliverableIndexes,
            errors,
            warnings);
    }

    /// <summary>
    /// Compose a formatted postal address block from <paramref name="row"/> using the role bindings in
    /// <paramref name="mapping"/>. The format follows Word's default address-block layout:
    /// <code>
    ///   [Title] FirstName LastName [Suffix]
    ///   [Company]
    ///   Address1
    ///   [Address2]
    ///   City, State PostalCode
    ///   [Country]
    /// </code>
    /// Empty optional lines are omitted. Word preserves the empty leading name line when address data
    /// exists but every name role is blank. Returns an empty string when no address information is available.
    /// </summary>
    public static string ComposeAddressBlock(IReadOnlyDictionary<string, string> row, FieldMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(mapping);

        string Get(FieldRole role) => mapping[role] is { } col ? Lookup(row, col) : string.Empty;

        // Word's default ADDRESSBLOCK name line omits the middle-name role.
        var nameParts = new List<string>();
        var title  = Get(FieldRole.Title);
        var first  = Get(FieldRole.FirstName);
        var last   = Get(FieldRole.LastName);
        var suffix = Get(FieldRole.Suffix);
        if (title.Length  > 0) nameParts.Add(title);
        if (first.Length  > 0) nameParts.Add(first);
        if (last.Length   > 0) nameParts.Add(last);
        if (suffix.Length > 0) nameParts.Add(suffix);

        var company   = Get(FieldRole.Company);
        var address1  = Get(FieldRole.Address1);
        var address2  = Get(FieldRole.Address2);
        var city      = Get(FieldRole.City);
        var state     = Get(FieldRole.State);
        var postal    = Get(FieldRole.PostalCode);
        var country   = Get(FieldRole.Country);

        // City, State PostalCode line — only include non-empty parts.
        var cityStateParts = new List<string>();
        var cityState = city.Length > 0 && state.Length > 0 ? $"{city}, {state}"
                      : city.Length  > 0 ? city
                      : state.Length > 0 ? state
                      : string.Empty;
        if (cityState.Length > 0) cityStateParts.Add(cityState);
        if (postal.Length    > 0) cityStateParts.Add(postal);
        var cityStateLine = string.Join(" ", cityStateParts);

        var lines = new List<string>();
        if (nameParts.Count > 0)
            lines.Add(string.Join(" ", nameParts));
        else if (company.Length > 0 || address1.Length > 0 || address2.Length > 0
                 || cityStateLine.Length > 0 || country.Length > 0)
            lines.Add(string.Empty);
        if (company.Length  > 0)  lines.Add(company);
        if (address1.Length > 0)  lines.Add(address1);
        if (address2.Length > 0)  lines.Add(address2);
        if (cityStateLine.Length > 0) lines.Add(cityStateLine);
        if (country.Length  > 0)  lines.Add(country);

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Compose a greeting line from <paramref name="row"/> using the role bindings in
    /// <paramref name="mapping"/>. <paramref name="greetingFormat"/> is the prefix text that precedes
    /// the recipient name (e.g. <c>"Dear"</c>); the composed greeting is:
    /// <c>{greetingFormat} {Title} {LastName},</c>
    /// falling back to <c>Dear Sir or Madam,</c> when no name fields are bound/populated.
    /// Pure and deterministic.
    /// </summary>
    public static string ComposeGreetingLine(
        IReadOnlyDictionary<string, string> row,
        FieldMapping mapping,
        string greetingFormat = "Dear")
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(greetingFormat);

        string Get(FieldRole role) => mapping[role] is { } col ? Lookup(row, col) : string.Empty;

        var title = Get(FieldRole.Title);
        var first = Get(FieldRole.FirstName);
        var last  = Get(FieldRole.LastName);

        // Build the name portion: prefer "Title LastName", fall back to "FirstName LastName",
        // then just the non-empty name part, then the generic fallback.
        string namePart;
        if (title.Length > 0 && last.Length > 0)
            namePart = $"{title} {last}";
        else if (first.Length > 0 && last.Length > 0)
            namePart = $"{first} {last}";
        else if (last.Length > 0)
            namePart = last;
        else if (first.Length > 0)
            namePart = first;
        else
            namePart = string.Empty;

        var prefix = greetingFormat.TrimEnd();
        return namePart.Length > 0 ? $"{prefix} {namePart}," : $"{prefix} Sir or Madam,";
    }

    /// <summary>
    /// Replace every <c>«Field»</c> placeholder in <paramref name="text"/> with the matching value from
    /// <paramref name="row"/>, and also resolve the special placeholders <c>«Merge Record #»</c> (the
    /// 1-based <paramref name="recordIndex"/>) and <c>«Next Record»</c> (sets
    /// <paramref name="advanceRecord"/> to true so the caller can move to the next row). A standard
    /// merge-field lookup occurs for all other names.
    /// </summary>
    public static string SubstituteSpecial(
        string text,
        IReadOnlyDictionary<string, string> row,
        int recordIndex,
        out bool advanceRecord)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(row);

        advanceRecord = false;
        if (text.IndexOf(FieldOpen) < 0)
            return text;

        var sb = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == FieldOpen)
            {
                var close = text.IndexOf(FieldClose, i + 1);
                if (close < 0)
                {
                    sb.Append(text, i, text.Length - i);
                    break;
                }

                var name = text.Substring(i + 1, close - i - 1).Trim();
                if (name.Equals(NextRecordField, StringComparison.OrdinalIgnoreCase))
                {
                    advanceRecord = true;
                    // «Next Record» produces no visible output — it is a control directive only.
                }
                else if (name.Equals(MergeRecordNumberField, StringComparison.OrdinalIgnoreCase))
                {
                    sb.Append(recordIndex);
                }
                else if (name.Equals("AddressBlock", StringComparison.OrdinalIgnoreCase))
                {
                    // «AddressBlock» without a mapping is a plain substitution from a named field if present,
                    // otherwise empty. Full resolution (via FieldMapping) is done by the caller before
                    // SubstituteSpecial; if it reaches here the field just resolves via the row dictionary.
                    sb.Append(Lookup(row, name));
                }
                else if (name.Equals("GreetingLine", StringComparison.OrdinalIgnoreCase))
                {
                    sb.Append(Lookup(row, name));
                }
                else
                {
                    sb.Append(Lookup(row, name));
                }

                i = close + 1;
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// The distinct merge-field names appearing in <paramref name="text"/>, in first-appearance order.
    /// A field is <c>«Name»</c>; the returned names are trimmed and de-duplicated case-insensitively
    /// (the first spelling wins). Empty placeholders (<c>«»</c> or whitespace-only) are ignored.
    /// </summary>
    public static IReadOnlyList<string> FieldNames(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in EnumerateFields(text))
        {
            var trimmed = name.Trim();
            if (trimmed.Length == 0)
                continue;
            if (seen.Add(trimmed))
                result.Add(trimmed);
        }
        return result;
    }

    /// <summary>
    /// The distinct merge-field names appearing in any mergeable document story or visible drawing text,
    /// in first-appearance order and de-duplicated case-insensitively.
    /// </summary>
    public static IReadOnlyList<string> FieldNames(TextDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Scan(string text)
        {
            foreach (var name in EnumerateFields(text))
            {
                var trimmed = name.Trim();
                if (trimmed.Length == 0)
                    continue;
                if (seen.Add(trimmed))
                    result.Add(trimmed);
            }
        }

        foreach (var block in doc.Blocks)
            ScanBlock(block, Scan);
        ScanSectionHeadersFooters(doc.FinalSectionHeadersFooters, Scan);
        foreach (var footnote in doc.Footnotes.OrderBy(entry => entry.Key).Select(entry => entry.Value))
            foreach (var paragraph in footnote.Content)
                ScanParagraph(paragraph, Scan);
        foreach (var endnote in doc.Endnotes.OrderBy(entry => entry.Key).Select(entry => entry.Value))
            foreach (var paragraph in endnote.Content)
                ScanParagraph(paragraph, Scan);
        foreach (var comment in doc.Comments.OrderBy(entry => entry.Key).Select(entry => entry.Value))
            foreach (var node in comment.ThreadInOrder())
                foreach (var paragraph in node.Content)
                    ScanParagraph(paragraph, Scan);

        return result;
    }

    /// <summary>
    /// Replace every <c>«Field»</c> placeholder in <paramref name="text"/> with the matching value from
    /// <paramref name="row"/> (looked up case-insensitively when the dictionary supports it; otherwise by
    /// exact key). A placeholder whose field is absent from the row is replaced with the empty string
    /// (see the missing-field policy on <see cref="MailMerge"/>). Literal text outside placeholders is
    /// left untouched, and an unterminated <c>«</c> (no closing <c>»</c>) is treated as literal text.
    /// </summary>
    public static string Substitute(string text, IReadOnlyDictionary<string, string> row)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(row);

        if (text.IndexOf(FieldOpen) < 0)
            return text;

        var sb = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == FieldOpen)
            {
                var close = text.IndexOf(FieldClose, i + 1);
                if (close < 0)
                {
                    // No closing delimiter — emit the rest verbatim.
                    sb.Append(text, i, text.Length - i);
                    break;
                }

                var name = text.Substring(i + 1, close - i - 1).Trim();
                sb.Append(Lookup(row, name));
                i = close + 1;
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Produce a new document that is <paramref name="template"/> with every run's <c>«Field»</c>
    /// placeholder substituted for <paramref name="row"/>'s values. The template is not mutated; the
    /// returned document is a deep copy of the body (paragraphs, runs and tables) sharing the same
    /// immutable formatting records, with styles, page settings, header/footer and properties carried
    /// over so the merged record looks like the template. Deterministic.
    /// </summary>
    public static TextDocument MergeRecord(TextDocument template, IReadOnlyDictionary<string, string> row)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(row);

        var doc = new TextDocument
        {
            DefaultRun = template.DefaultRun,
            DefaultParagraph = template.DefaultParagraph,
            UseWordApplicationDefaultLineSpacing = template.UseWordApplicationDefaultLineSpacing,
            UseWordApplicationDefaultRunFormatting = template.UseWordApplicationDefaultRunFormatting,
            Protection = template.Protection,
            DoNotDisplayPageBoundaries = template.DoNotDisplayPageBoundaries,
            RemovePersonalInformation = template.RemovePersonalInformation,
            HideSpellingErrors = template.HideSpellingErrors,
            HideGrammaticalErrors = template.HideGrammaticalErrors,
            AutomaticallyUpdateStylesFromTemplate = template.AutomaticallyUpdateStylesFromTemplate,
            UpdateFieldsOnOpen = template.UpdateFieldsOnOpen,
            TrackRevisions = template.TrackRevisions,
            DoNotTrackMoves = template.DoNotTrackMoves,
            DoNotTrackFormatting = template.DoNotTrackFormatting,
            DoNotAutoCompressPictures = template.DoNotAutoCompressPictures,
            EmbedSystemFonts = template.EmbedSystemFonts,
            SaveSubsetFonts = template.SaveSubsetFonts,
            PageBordersDoNotSurroundHeader = template.PageBordersDoNotSurroundHeader,
            PageBordersDoNotSurroundFooter = template.PageBordersDoNotSurroundFooter,
            MarkedAsFinal = template.MarkedAsFinal,
            Theme = template.Theme,
            BibliographyStyle = template.BibliographyStyle
        };

        foreach (var (id, style) in template.Styles)
            doc.Styles[id] = style;

        CopyDocumentState(template, doc, block => CloneBlock(block, row));
        doc.Preserved.CopyFrom(template.Preserved);
        CopyPageSettings(template.Page, doc.Page);
        CopySectionHeadersFooters(template.FinalSectionHeadersFooters, doc.FinalSectionHeadersFooters,
            source => CloneHeaderFooter(source, row));

        foreach (var block in template.Blocks)
            doc.Blocks.Add(CloneBlock(block, row));

        return doc;
    }

    /// <summary>
    /// Produce one merged document per row in <paramref name="data"/>, in row order, each the result of
    /// <see cref="MergeRecord"/> against <paramref name="template"/>. Deterministic; an empty data source
    /// yields an empty list.
    /// </summary>
    public static IReadOnlyList<TextDocument> MergeAll(TextDocument template, MergeData data)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(data);

        var result = new List<TextDocument>(data.Count);
        foreach (var row in data.Rows)
            result.Add(MergeRecord(template, row));
        return result;
    }

    /// <summary>
    /// Produce merged documents for every non-skipped row in <paramref name="data"/> by evaluating
    /// conditional merge rules in addition to plain field substitution. Rule placeholders use the same
    /// <c>«instruction»</c> syntax but their instruction text is recognised by
    /// <see cref="MergeRuleEvaluator"/>. The <paramref name="state"/> accumulates skip decisions and
    /// bookmark values across the whole merge run; callers may pre-populate
    /// <see cref="MergeState.FillInAnswers"/> and <see cref="MergeState.AskAnswers"/> before calling
    /// (for Fill-in / Ask rules whose prompts were shown to the user at merge-start time).
    /// The returned list contains only non-skipped records (so its count may be less than
    /// <paramref name="data"/>.<see cref="MergeData.Count"/>).
    /// </summary>
    public static IReadOnlyList<TextDocument> MergeAllWithRules(
        TextDocument template,
        MergeData data,
        MergeState state)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(state);

        var result = new List<TextDocument>(data.Count);
        for (var i = 0; i < data.Count; i++)
        {
            var row = data.Rows[i];
            // Pre-increment SequenceNumber so «Merge Sequence #» in the template sees the correct
            // value for this record. If the record is subsequently skipped by a «Skip Record If»
            // rule, we roll the counter back so the sequence remains gapless.
            state.SequenceNumber++;
            // Evaluate the template against this row with full rule support. This also marks
            // state.SkippedIndices[i] when a «Skip Record If» condition fires.
            var merged = MergeRecordWithRules(template, row, state, recordIndex: i + 1);
            if (state.SkippedIndices.Contains(i))
            {
                // Roll back — this record won't appear in the output.
                state.SequenceNumber--;
            }
            else
            {
                result.Add(merged);
            }

            // NEXT / NEXTIF consumes one additional source row after the current output record.
            // The record-level evaluator only reports this cursor request; the all-record caller owns
            // advancing the data-source cursor, just as the label-sheet hosts do between cells.
            if (state.AdvanceRecordRequested && i + 1 < data.Count)
                i++;
        }
        return result;
    }

    /// <summary>
    /// Deep-clone <paramref name="template"/> with both plain merge-field substitution and conditional
    /// rule evaluation applied for the given row. Rule placeholders are resolved via
    /// <see cref="MergeRuleEvaluator.Evaluate"/>; skip/advance side-effects update
    /// <paramref name="state"/>. The <paramref name="recordIndex"/> is 1-based.
    /// </summary>
    public static TextDocument MergeRecordWithRules(
        TextDocument template,
        IReadOnlyDictionary<string, string> row,
        MergeState state,
        int recordIndex)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(state);

        state.AdvanceRecordRequested = false;
        state.SkipRecordRequested = false;

        var doc = new TextDocument
        {
            DefaultRun = template.DefaultRun,
            DefaultParagraph = template.DefaultParagraph,
            UseWordApplicationDefaultLineSpacing = template.UseWordApplicationDefaultLineSpacing,
            UseWordApplicationDefaultRunFormatting = template.UseWordApplicationDefaultRunFormatting,
            Protection = template.Protection,
            DoNotDisplayPageBoundaries = template.DoNotDisplayPageBoundaries,
            RemovePersonalInformation = template.RemovePersonalInformation,
            HideSpellingErrors = template.HideSpellingErrors,
            HideGrammaticalErrors = template.HideGrammaticalErrors,
            AutomaticallyUpdateStylesFromTemplate = template.AutomaticallyUpdateStylesFromTemplate,
            UpdateFieldsOnOpen = template.UpdateFieldsOnOpen,
            TrackRevisions = template.TrackRevisions,
            DoNotTrackMoves = template.DoNotTrackMoves,
            DoNotTrackFormatting = template.DoNotTrackFormatting,
            DoNotAutoCompressPictures = template.DoNotAutoCompressPictures,
            EmbedSystemFonts = template.EmbedSystemFonts,
            SaveSubsetFonts = template.SaveSubsetFonts,
            PageBordersDoNotSurroundHeader = template.PageBordersDoNotSurroundHeader,
            PageBordersDoNotSurroundFooter = template.PageBordersDoNotSurroundFooter,
            MarkedAsFinal = template.MarkedAsFinal,
            Theme = template.Theme,
            BibliographyStyle = template.BibliographyStyle
        };

        foreach (var (id, style) in template.Styles)
            doc.Styles[id] = style;

        CopyDocumentState(template, doc, block => CloneBlockWithRules(block, row, state, recordIndex));
        doc.Preserved.CopyFrom(template.Preserved);
        CopyPageSettings(template.Page, doc.Page);
        CopySectionHeadersFooters(template.FinalSectionHeadersFooters, doc.FinalSectionHeadersFooters,
            source => CloneHeaderFooterWithRules(source, row, state, recordIndex));

        foreach (var block in template.Blocks)
            doc.Blocks.Add(CloneBlockWithRules(block, row, state, recordIndex));

        return doc;
    }

    /// <summary>
    /// Replace every <c>«instruction»</c> placeholder in <paramref name="text"/> with the matching
    /// value from <paramref name="row"/>, evaluating conditional rules via
    /// <see cref="MergeRuleEvaluator"/> in addition to the standard special fields handled by
    /// <see cref="SubstituteSpecial"/>. The <paramref name="recordIndex"/> is 1-based.
    /// </summary>
    public static string SubstituteSpecialWithRules(
        string text,
        IReadOnlyDictionary<string, string> row,
        MergeState state,
        int recordIndex,
        out bool advanceRecord,
        out bool skipRecord)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(state);

        advanceRecord = false;
        skipRecord = false;

        if (text.IndexOf(FieldOpen) < 0)
            return text;

        var sb = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == FieldOpen)
            {
                var close = text.IndexOf(FieldClose, i + 1);
                if (close < 0)
                {
                    sb.Append(text, i, text.Length - i);
                    break;
                }

                var name = text.Substring(i + 1, close - i - 1).Trim();
                i = close + 1;

                // Try rule evaluator first.
                var ruleResult = MergeRuleEvaluator.Evaluate(name, row, state, recordIndex - 1 /* 0-based */);
                if (ruleResult.HasValue)
                {
                    sb.Append(ruleResult.Value.Text);
                    if (ruleResult.Value.SkipRecord)  skipRecord    = true;
                    if (ruleResult.Value.AdvanceRecord) advanceRecord = true;
                    continue;
                }

                // Fall through to standard special-field handling.
                if (name.Equals(NextRecordField, StringComparison.OrdinalIgnoreCase))
                {
                    advanceRecord = true;
                }
                else if (name.Equals(MergeRecordNumberField, StringComparison.OrdinalIgnoreCase))
                {
                    sb.Append(recordIndex);
                }
                else if (name.Equals(MergeSequenceNumberField, StringComparison.OrdinalIgnoreCase))
                {
                    sb.Append(state.SequenceNumber);
                }
                else
                {
                    sb.Append(Lookup(row, name));
                }
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Combine already-merged records into a single document using the selected output mode. Letters force
    /// a page break before each record after the first; Directory appends records continuously. Later records
    /// are copied through the document merge path so annotation ids and preserved package references remain valid.
    /// </summary>
    public static TextDocument CombineMergedRecords(IReadOnlyList<TextDocument> records, MailMergeOutputMode mode)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count == 0)
            return TextDocument.CreateEmpty();

        var first = records[0];
        for (var d = 1; d < records.Count; d++)
        {
            var record = records[d];
            if (mode == MailMergeOutputMode.Letters)
                StartNextLetterSection(first, record);

            DocumentMerge.Merge(first, first.Blocks.Count, record);
        }

        return first;
    }

    // Enumerate the raw (untrimmed) field-name spans found between matched «…» delimiters in order.
    private static IEnumerable<string> EnumerateFields(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            var open = text.IndexOf(FieldOpen, i);
            if (open < 0)
                yield break;
            var close = text.IndexOf(FieldClose, open + 1);
            if (close < 0)
                yield break;
            yield return text.Substring(open + 1, close - open - 1);
            i = close + 1;
        }
    }

    private static void StartNextLetterSection(TextDocument combined, TextDocument nextRecord)
    {
        var completedSection = new Section(combined.Page.Clone(), SectionBreakKind.NextPage)
        {
            HeadersFooters = CloneSectionHeadersFooters(
                combined.FinalSectionHeadersFooters,
                headerFooter => headerFooter)
        };

        if (combined.Blocks.LastOrDefault() is Paragraph { SectionBreak: null } paragraph)
            paragraph.SectionBreak = completedSection;
        else
            combined.Blocks.Add(new Paragraph { SectionBreak = completedSection });

        CopyPageSettings(nextRecord.Page, combined.Page);
        CopySectionHeadersFooters(
            nextRecord.FinalSectionHeadersFooters,
            combined.FinalSectionHeadersFooters,
            headerFooter => headerFooter);
    }

    private static string Lookup(IReadOnlyDictionary<string, string> row, string name) =>
        row.TryGetValue(name, out var value) ? value ?? string.Empty : string.Empty;

    private static IReadOnlyList<int> ResolveEmailRecordIndexes(
        MergeData data,
        MailMergeEmailDeliveryIntent intent,
        List<string> warnings)
    {
        if (data.Count == 0)
            return [];

        return intent.RecordScope switch
        {
            MailMergeEmailRecordScope.AllRecords => Enumerable.Range(0, data.Count).ToArray(),
            MailMergeEmailRecordScope.CurrentRecord => [Math.Clamp(intent.CurrentRecordIndex, 0, data.Count - 1)],
            MailMergeEmailRecordScope.SelectedRecords => ResolveSelectedEmailRecordIndexes(data, intent, warnings),
            _ => []
        };
    }

    private static IReadOnlyList<int> ResolveSelectedEmailRecordIndexes(
        MergeData data,
        MailMergeEmailDeliveryIntent intent,
        List<string> warnings)
    {
        var selected = intent.SelectedRecordIndexes ?? [];
        var valid = new List<int>();
        var seen = new HashSet<int>();

        foreach (var index in selected)
        {
            if (index < 0 || index >= data.Count)
            {
                warnings.Add($"Selected record index {index} is outside the recipient list.");
                continue;
            }

            if (seen.Add(index))
                valid.Add(index);
        }

        return valid;
    }

    private static string NormalizeEmailHeader(string value)
    {
        var chars = value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray();
        return new string(chars);
    }

    private static void ScanBlock(Block block, Action<string> scan)
    {
        switch (block)
        {
            case Paragraph p:
                ScanParagraph(p, scan);
                break;
            case Table t:
                foreach (var row in t.Rows)
                    foreach (var cell in row.Cells)
                        foreach (var p in cell.Paragraphs)
                            ScanParagraph(p, scan);
                break;
        }
    }

    private static void ScanParagraph(Paragraph paragraph, Action<string> scan)
    {
        foreach (var run in paragraph.Runs)
            ScanRun(run, scan);
        if (paragraph.SectionBreak is { } section)
            ScanSectionHeadersFooters(section.HeadersFooters, scan);
    }

    private static void ScanRun(Run run, Action<string> scan)
    {
        var isNativeMergeField = TryGetMergeFieldName(run.ComplexField, out var mergeFieldName);
        if (isNativeMergeField)
            scan($"{FieldOpen}{mergeFieldName}{FieldClose}");

        if (!isNativeMergeField && run.Ruby is { } ruby)
        {
            foreach (var fragment in ruby.BaseFragments)
                scan(fragment.Text);
            foreach (var fragment in ruby.PhoneticFragments)
                scan(fragment.Text);
        }
        else if (!isNativeMergeField)
        {
            scan(run.Text);
        }

        if (run.Shape is { } shape)
            foreach (var paragraph in shape.TextParagraphs)
                ScanParagraph(paragraph, scan);
        if (run.WordArt is { } wordArt)
            scan(wordArt.Text);
        if (run.SmartArt is { } smartArt)
            foreach (var node in smartArt.Nodes)
                ScanSmartArtNode(node, scan);
        if (run.Chart is { } chart)
            ScanChart(chart, scan);
        if (run.DrawingGroup is { } drawingGroup)
            ScanDrawingGroup(drawingGroup, scan);
    }

    private static void ScanSmartArtNode(SmartArtNode node, Action<string> scan)
    {
        scan(node.Text);
        foreach (var child in node.Children)
            ScanSmartArtNode(child, scan);
    }

    private static void ScanChart(Chart chart, Action<string> scan)
    {
        if (chart.Title is { } title)
            scan(title);
        if (chart.CategoryAxisTitle is { } categoryAxisTitle)
            scan(categoryAxisTitle);
        if (chart.ValueAxisTitle is { } valueAxisTitle)
            scan(valueAxisTitle);
        foreach (var category in chart.Categories)
            scan(category);
        foreach (var series in chart.Series)
            if (series.Name is { } name)
                scan(name);
    }

    private static void ScanDrawingGroup(DrawingGroup group, Action<string> scan)
    {
        foreach (var child in group.Children)
        {
            switch (child)
            {
                case Shape shape:
                    foreach (var paragraph in shape.TextParagraphs)
                        ScanParagraph(paragraph, scan);
                    break;
                case WordArt wordArt:
                    scan(wordArt.Text);
                    break;
                case SmartArt smartArt:
                    foreach (var node in smartArt.Nodes)
                        ScanSmartArtNode(node, scan);
                    break;
                case Chart chart:
                    ScanChart(chart, scan);
                    break;
                case DrawingGroup nested:
                    ScanDrawingGroup(nested, scan);
                    break;
            }
        }
    }

    private static void ScanSectionHeadersFooters(SectionHeadersFooters stories, Action<string> scan)
    {
        foreach (var story in new[]
                 {
                     stories.Header,
                     stories.Footer,
                     stories.EvenHeader,
                     stories.EvenFooter,
                     stories.FirstHeader,
                     stories.FirstFooter
                 })
            if (story is not null)
                foreach (var paragraph in story.Paragraphs)
                    ScanParagraph(paragraph, scan);
    }

    private static Block CloneBlock(Block block, IReadOnlyDictionary<string, string> row)
    {
        var clone = DocumentMerge.CloneBlock(block);
        TransformBlockText(clone, text => Substitute(text, row), ResolveNativeMergeField);
        return clone;

        bool ResolveNativeMergeField(Run run)
        {
            switch (run.ComplexField?.Keyword)
            {
                case "MERGEFIELD" when TryGetMergeFieldName(run.ComplexField, out var fieldName):
                    run.Text = ResolveMergeFieldResult(run, fieldName, row);
                    run.ComplexField = null;
                    return true;
                case "ADDRESSBLOCK" when IsSupportedCompositeMergeField(run.ComplexField):
                case "GREETINGLINE" when IsSupportedCompositeMergeField(run.ComplexField):
                    run.Text = ResolveCompositeMergeFieldResult(run.ComplexField.Keyword, row);
                    run.ComplexField = null;
                    return true;
                case "ADDRESSBLOCK":
                case "GREETINGLINE":
                    return true;
                default:
                    return false;
            }
        }
    }

    private static Block CloneBlockWithRules(
        Block block,
        IReadOnlyDictionary<string, string> row,
        MergeState state,
        int recordIndex)
    {
        var clone = DocumentMerge.CloneBlock(block);
        TransformBlockText(clone, Resolve, ResolveNativeSpecialField);
        return clone;

        string Resolve(string text)
        {
            var resolved = SubstituteSpecialWithRules(
                text, row, state, recordIndex, out var advanceRecord, out var skipRecord);
            state.AdvanceRecordRequested |= advanceRecord;
            state.SkipRecordRequested |= skipRecord;
            return resolved;
        }

        bool ResolveNativeSpecialField(Run run)
        {
            switch (run.ComplexField?.Keyword)
            {
                case "MERGEFIELD" when TryGetMergeFieldName(run.ComplexField, out var fieldName):
                    run.Text = ResolveMergeFieldResult(run, fieldName, row);
                    run.ComplexField = null;
                    return true;
                case "ADDRESSBLOCK" when IsSupportedCompositeMergeField(run.ComplexField):
                case "GREETINGLINE" when IsSupportedCompositeMergeField(run.ComplexField):
                    run.Text = ResolveCompositeMergeFieldResult(run.ComplexField.Keyword, row);
                    run.ComplexField = null;
                    return true;
                case "ADDRESSBLOCK":
                case "GREETINGLINE":
                    return true;
                case NextRecordInstruction:
                    run.Text = string.Empty;
                    state.AdvanceRecordRequested = true;
                    return true;
                case MergeRecordNumberInstruction:
                    run.Text = recordIndex.ToString(CultureInfo.InvariantCulture);
                    return true;
                case MergeSequenceNumberInstruction:
                    run.Text = state.SequenceNumber.ToString(CultureInfo.InvariantCulture);
                    return true;
                case "FILLIN" when ComplexFieldEngine.HasSwitch(run.ComplexField.Instruction, 'o'):
                    if (MergeRuleEvaluator.TryParseInteractivePrompt(
                            run.ComplexField.Instruction,
                            out var prompt))
                    {
                        var answer = state.FillInAnswers.TryGetValue(prompt.Key, out var suppliedAnswer)
                            ? suppliedAnswer
                            : prompt.DefaultAnswer;
                        run.Text = ApplyMergeFieldGeneralFormats(
                            answer,
                            string.Empty,
                            string.Empty,
                            run.ComplexField.Instruction);
                        run.ComplexField = null;
                    }
                    return true;
                case "ASK" when ComplexFieldEngine.HasSwitch(run.ComplexField.Instruction, 'o'):
                    if (MergeRuleEvaluator.TryParseInteractivePrompt(
                            run.ComplexField.Instruction,
                            out var askPrompt))
                    {
                        var answer = state.AskAnswers.TryGetValue(askPrompt.Key, out var suppliedAnswer)
                            ? suppliedAnswer
                            : askPrompt.DefaultAnswer;
                        state.Bookmarks[askPrompt.Key] = ApplyMergeFieldGeneralFormats(
                            answer,
                            string.Empty,
                            string.Empty,
                            run.ComplexField.Instruction);
                        run.Text = string.Empty;
                        run.ComplexField = null;
                    }
                    return true;
                case "REF" when MergeRuleEvaluator.TryParseBookmarkReference(
                        run.ComplexField.Instruction,
                        out var bookmarkName)
                    && state.Bookmarks.TryGetValue(bookmarkName, out var bookmarkValue):
                    run.Text = bookmarkValue;
                    run.ComplexField = null;
                    return true;
                default:
                    return false;
            }
        }
    }

    private static string ResolveCompositeMergeFieldResult(
        string keyword,
        IReadOnlyDictionary<string, string> row)
    {
        var syntheticName = keyword == "ADDRESSBLOCK" ? "AddressBlock" : "GreetingLine";
        foreach (var pair in row)
        {
            if (pair.Key.Equals(syntheticName, StringComparison.OrdinalIgnoreCase))
                return pair.Value ?? string.Empty;
        }

        var mapping = AutoMatchFields(row.Keys.ToArray());
        return keyword == "ADDRESSBLOCK"
            ? ComposeAddressBlock(row, mapping)
            : ComposeGreetingLine(row, mapping);
    }

    private static bool IsSupportedCompositeMergeField(ComplexField field)
    {
        if (field.Keyword == "ADDRESSBLOCK")
        {
            return Regex.IsMatch(
                field.Instruction,
                @"^\s*ADDRESSBLOCK\s*(?:\\\*\s+MERGEFORMAT\s*)?$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return field.Keyword == "GREETINGLINE"
            && Regex.IsMatch(
                field.Instruction,
                @"^\s*(?i:GREETINGLINE)\s+(?i:\\f)\s+""<<_BEFORE_ Dear >><<_TITLE0_ >><<_LAST0_>><<_AFTER_ ,>>""\s+(?i:\\e)\s+""Dear Sir or Madam,""\s+(?i:\\l)\s+1033\s*(?:(?i:\\\*\s+MERGEFORMAT)\s*)?$",
                RegexOptions.CultureInvariant);
    }

    private static string ResolveMergeFieldResult(
        Run run,
        string fieldName,
        IReadOnlyDictionary<string, string> row)
    {
        var field = run.ComplexField!;
        var value = Lookup(row, fieldName);
        if (value.Length == 0)
            return string.Empty;

        value = ApplyMergeFieldDatePicture(value, field.Instruction, MergeFieldCulture(run));
        value = ApplyMergeFieldNumericPicture(value, field.Instruction);
        var before = ComplexFieldEngine.SwitchValue(field.Instruction, 'b') ?? string.Empty;
        var after = ComplexFieldEngine.SwitchValue(field.Instruction, 'f') ?? string.Empty;
        return ApplyMergeFieldGeneralFormats(value, before, after, field.Instruction);
    }

    private static CultureInfo MergeFieldCulture(Run run)
    {
        if (run.Formatting.LanguageTag is { Length: > 0 } tag)
        {
            try
            {
                return CultureInfo.GetCultureInfo(tag);
            }
            catch (CultureNotFoundException)
            {
                // Fall through to the process culture for malformed/imported language tags.
            }
        }
        return CultureInfo.CurrentCulture;
    }

    private static string ApplyMergeFieldDatePicture(
        string value,
        string instruction,
        CultureInfo culture)
    {
        var picture = ComplexFieldEngine.SwitchValue(instruction, '@');
        if (picture is null || !TryConvertWordDatePicture(picture, out var netPicture))
            return value;

        if (!DateTime.TryParse(value, culture, DateTimeStyles.AllowWhiteSpaces, out var moment)
            && !DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out moment))
        {
            return value;
        }

        return moment.ToString(netPicture, culture);
    }

    private static bool TryConvertWordDatePicture(
        string picture,
        out string netPicture)
    {
        var builder = new StringBuilder(picture.Length + 4);
        for (var i = 0; i < picture.Length;)
        {
            if (picture.AsSpan(i).StartsWith("AM/PM", StringComparison.Ordinal))
            {
                builder.Append("tt");
                i += 5;
                continue;
            }
            if (picture.AsSpan(i).StartsWith("am/pm", StringComparison.Ordinal))
            {
                builder.Append("tt");
                i += 5;
                continue;
            }

            var ch = picture[i];
            if (ch == '\'')
            {
                var closingQuote = picture.IndexOf('\'', i + 1);
                if (closingQuote < 0)
                {
                    netPicture = string.Empty;
                    return false;
                }

                builder.Append(picture, i, closingQuote - i + 1);
                i = closingQuote + 1;
                continue;
            }
            if (!char.IsLetter(ch))
            {
                if (ch is '/' or ':')
                    builder.Append('\\');
                builder.Append(ch);
                i++;
                continue;
            }

            var end = i + 1;
            while (end < picture.Length && picture[end] == ch)
                end++;
            var length = end - i;
            var valid = ch switch
            {
                'd' or 'M' => length is >= 1 and <= 4,
                'y' => length is >= 1 and <= 4,
                'h' or 'H' or 'm' or 's' => length is >= 1 and <= 2,
                _ => false
            };
            if (!valid)
            {
                netPicture = string.Empty;
                return false;
            }

            builder.Append(ch, length);
            i = end;
        }

        netPicture = builder.Length == 1
            ? "%" + builder.ToString()
            : builder.ToString();
        return netPicture.Length > 0;
    }

    private static string ApplyMergeFieldNumericPicture(string value, string instruction)
    {
        var picture = ComplexFieldEngine.SwitchValue(instruction, '#');
        if (picture is not ("0"
                or "0.00"
                or "#,##0"
                or "#,##0.00"
                or "000000"
                or "$#,##0.00"
                or "$#,##0.00;($#,##0.00)"
                or "0.00;-0.00;ZERO"
                or "0.0%")
            || !double.TryParse(
                value,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out var number))
        {
            return value;
        }

        if (picture == "$#,##0.00" && number < 0)
            return value;

        // These common pictures are exact Word-calibrated signatures. Word's wider picture language has
        // operators (including x and conditional signs) that are not .NET-compatible, so unknown pictures
        // intentionally preserve the source value until their semantics are modeled separately. Word's
        // percent sign is a literal suffix and does not multiply the input by 100.
        var netPicture = picture.Replace("%", "\\%", StringComparison.Ordinal);
        var formatted = TableFormulaEvaluator.Format(number, netPicture);
        if (picture != "$#,##0.00")
            return formatted;

        var decimalIndex = formatted.IndexOf('.');
        var integerEnd = decimalIndex >= 0 ? decimalIndex : formatted.Length;
        var integerDigits = formatted[..integerEnd].Count(char.IsDigit);
        var padding = Math.Max(0, 4 - integerDigits);
        return "$" + new string(' ', padding) + formatted.TrimStart('$');
    }

    private const string MergeFieldNumberFormatError =
        "Error! Number cannot be represented in specified format.";

    private static string ApplyMergeFieldGeneralFormats(
        string value,
        string before,
        string after,
        string instruction)
    {
        var formats = ComplexFieldEngine.SwitchValues(instruction, '*')
            .Where(format => !format.Equals("MERGEFORMAT", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var hasConditionalText = before.Length > 0 || after.Length > 0;
        var input = before + value + after;

        // Word's lone numeric result switch owns the complete non-empty merge result and suppresses
        // conditional \b/\f text when the data is numeric. Multiple result switches instead process
        // the assembled conditional result in order; a numeric switch then leaves nonnumeric text alone.
        var conditionalTextIsPunctuationOnly = (before + after).All(character =>
            !char.IsLetterOrDigit(character));
        if (hasConditionalText
            && formats.Length > 0
            && IsMergeFieldNumericGeneralFormat(formats[0])
            && TryParseMergeFieldNumber(value, out _)
            && (formats.Length == 1 || conditionalTextIsPunctuationOnly))
        {
            input = value;
        }

        foreach (var format in formats)
        {
            input = format.ToUpperInvariant() switch
            {
                "UPPER" => input.ToUpperInvariant(),
                "LOWER" => input.ToLowerInvariant(),
                "FIRSTCAP" => CapitalizeFirstLetter(input),
                "CAPS" => CapitalizeWordInitials(input),
                _ => ApplyMergeFieldNumericGeneralFormat(input, format)
            };
        }
        return input;
    }

    private static bool IsMergeFieldNumericGeneralFormat(string format) =>
        format.ToUpperInvariant() is
            "ARABIC" or "ROMAN" or "ALPHABETIC" or "HEX" or "ORDINAL"
            or "ORDTEXT" or "CARDTEXT" or "DOLLARTEXT";

    private static string ApplyMergeFieldNumericGeneralFormat(string value, string format)
    {
        if (!IsMergeFieldNumericGeneralFormat(format)
            || !TryParseMergeFieldNumber(value, out var number))
        {
            return value;
        }

        var rounded = decimal.Round(number, 0, MidpointRounding.AwayFromZero);
        return format.ToUpperInvariant() switch
        {
            "ARABIC" => rounded.ToString("0", CultureInfo.InvariantCulture),
            "ROMAN" => FormatMergeFieldRoman(rounded, lower: format == "roman"),
            "ALPHABETIC" => FormatMergeFieldAlphabetic(rounded, lower: format == "alphabetic"),
            "HEX" => FormatMergeFieldHex(rounded),
            "ORDINAL" => FormatMergeFieldOrdinal(rounded),
            "ORDTEXT" => FormatMergeFieldNumberWords(rounded, ordinal: true),
            "CARDTEXT" => FormatMergeFieldNumberWords(rounded, ordinal: false),
            "DOLLARTEXT" => FormatMergeFieldDollarText(number),
            _ => value
        };
    }

    private static bool TryParseMergeFieldNumber(string value, out decimal number) =>
        decimal.TryParse(
            value,
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out number);

    private static string FormatMergeFieldRoman(decimal rounded, bool lower)
    {
        if (rounded == 0)
            return string.Empty;
        if (rounded is < 1 or > 32767)
            return MergeFieldNumberFormatError;

        (int Value, string Symbol)[] map =
        [
            (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
            (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
            (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")
        ];
        var remaining = decimal.ToInt32(rounded);
        var result = new System.Text.StringBuilder();
        foreach (var (amount, symbol) in map)
        {
            while (remaining >= amount)
            {
                result.Append(symbol);
                remaining -= amount;
            }
        }
        var text = result.ToString();
        return lower ? text.ToLowerInvariant() : text;
    }

    private static string FormatMergeFieldAlphabetic(decimal rounded, bool lower)
    {
        if (rounded == 0)
            return string.Empty;
        if (rounded is < 1 or > 780)
            return MergeFieldNumberFormatError;

        var value = decimal.ToInt32(rounded);
        var letter = (char)((lower ? 'a' : 'A') + (value - 1) % 26);
        var count = (value - 1) / 26 + 1;
        return new string(letter, count);
    }

    private static string FormatMergeFieldHex(decimal rounded)
    {
        if (rounded is < 0 or > 65535)
            return MergeFieldNumberFormatError;
        return decimal.ToInt32(rounded).ToString("X", CultureInfo.InvariantCulture);
    }

    private static string FormatMergeFieldOrdinal(decimal rounded)
    {
        var absolute = decimal.Abs(rounded);
        var lastTwo = decimal.ToInt32(absolute % 100);
        var last = decimal.ToInt32(absolute % 10);
        var suffix = lastTwo is >= 11 and <= 13
            ? "th"
            : last switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th"
            };
        return rounded.ToString("0", CultureInfo.InvariantCulture) + suffix;
    }

    private static string FormatMergeFieldNumberWords(decimal rounded, bool ordinal)
    {
        if (rounded is < 0 or > 999999)
            return MergeFieldNumberFormatError;
        var number = decimal.ToInt32(rounded);
        return ordinal ? NumberToOrdinalWords(number) : NumberToCardinalWords(number);
    }

    private static string FormatMergeFieldDollarText(decimal number)
    {
        if (number < 0)
            return MergeFieldNumberFormatError;

        var whole = decimal.Truncate(number);
        if (whole > 999999)
            return MergeFieldNumberFormatError;

        var cents = decimal.ToInt32(decimal.Round(
            (number - whole) * 100,
            0,
            MidpointRounding.AwayFromZero)) % 100;
        return $"{NumberToCardinalWords(decimal.ToInt32(whole))} and {cents:00}/100";
    }

    private static readonly string[] CardinalOnes =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine",
        "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen",
        "seventeen", "eighteen", "nineteen"
    ];

    private static readonly string[] CardinalTens =
    [
        "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"
    ];

    private static readonly string[] OrdinalOnes =
    [
        "zeroth", "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth",
        "ninth", "tenth", "eleventh", "twelfth", "thirteenth", "fourteenth", "fifteenth",
        "sixteenth", "seventeenth", "eighteenth", "nineteenth"
    ];

    private static readonly string[] OrdinalTens =
    [
        "", "", "twentieth", "thirtieth", "fortieth", "fiftieth", "sixtieth",
        "seventieth", "eightieth", "ninetieth"
    ];

    private static string NumberToCardinalWords(int value)
    {
        if (value < 20)
            return CardinalOnes[value];
        if (value < 100)
        {
            var remainder = value % 10;
            return CardinalTens[value / 10]
                + (remainder == 0 ? string.Empty : "-" + CardinalOnes[remainder]);
        }
        if (value < 1000)
        {
            var remainder = value % 100;
            return CardinalOnes[value / 100] + " hundred"
                + (remainder == 0 ? string.Empty : " " + NumberToCardinalWords(remainder));
        }

        var thousands = value / 1000;
        var tail = value % 1000;
        return NumberToCardinalWords(thousands) + " thousand"
            + (tail == 0 ? string.Empty : " " + NumberToCardinalWords(tail));
    }

    private static string NumberToOrdinalWords(int value)
    {
        if (value < 20)
            return OrdinalOnes[value];
        if (value < 100)
        {
            var remainder = value % 10;
            return remainder == 0
                ? OrdinalTens[value / 10]
                : CardinalTens[value / 10] + "-" + OrdinalOnes[remainder];
        }
        if (value < 1000)
        {
            var remainder = value % 100;
            return remainder == 0
                ? CardinalOnes[value / 100] + " hundredth"
                : CardinalOnes[value / 100] + " hundred " + NumberToOrdinalWords(remainder);
        }

        var thousands = value / 1000;
        var tail = value % 1000;
        return tail == 0
            ? NumberToCardinalWords(thousands) + " thousandth"
            : NumberToCardinalWords(thousands) + " thousand " + NumberToOrdinalWords(tail);
    }

    private static string CapitalizeFirstLetter(string value)
    {
        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetter(chars[i]))
                continue;
            chars[i] = char.ToUpperInvariant(chars[i]);
            break;
        }
        return new string(chars);
    }

    private static string CapitalizeWordInitials(string value)
    {
        var chars = value.ToCharArray();
        var atWordStart = true;
        for (var i = 0; i < chars.Length; i++)
        {
            if (char.IsWhiteSpace(chars[i])
                || char.IsPunctuation(chars[i]) && chars[i] is not '\'' and not '’')
            {
                atWordStart = true;
            }
            else if (char.IsLetter(chars[i]))
            {
                if (atWordStart)
                    chars[i] = char.ToUpperInvariant(chars[i]);
                atWordStart = false;
            }
        }
        return new string(chars);
    }

    private static void TransformBlockText(
        Block block,
        Func<string, string> transform,
        Func<Run, bool>? transformRun = null)
    {
        switch (block)
        {
            case Paragraph paragraph:
                TransformParagraphText(paragraph, transform, transformRun);
                break;
            case Table table:
                foreach (var row in table.Rows)
                    foreach (var cell in row.Cells)
                        foreach (var paragraph in cell.Paragraphs)
                            TransformParagraphText(paragraph, transform, transformRun);
                break;
        }
    }

    private static void TransformParagraphText(
        Paragraph paragraph,
        Func<string, string> transform,
        Func<Run, bool>? transformRun = null)
    {
        foreach (var run in paragraph.Runs)
            TransformRunText(run, transform, transformRun);

        if (paragraph.SectionBreak is { } section)
            TransformSectionHeadersFootersText(section.HeadersFooters, transform, transformRun);
    }

    private static void TransformRunText(
        Run run,
        Func<string, string> transform,
        Func<Run, bool>? transformRun = null)
    {
        var textWasMaterialized = transformRun?.Invoke(run) == true;
        if (!textWasMaterialized && run.Ruby is { } ruby)
        {
            TransformRubyFragments(ruby.BaseFragments, transform);
            TransformRubyFragments(ruby.PhoneticFragments, transform);
        }
        else if (!textWasMaterialized)
        {
            run.Text = transform(run.Text);
        }

        if (run.Shape is { } shape)
            TransformShapeText(shape, transform, transformRun);
        if (run.WordArt is { } wordArt)
            wordArt.Text = transform(wordArt.Text);
        if (run.SmartArt is { } smartArt)
            TransformSmartArtText(smartArt, transform);
        if (run.Chart is { } chart)
            TransformChartText(chart, transform);
        if (run.DrawingGroup is { } drawingGroup)
            TransformDrawingGroupText(drawingGroup, transform, transformRun);
    }

    private static void TransformRubyFragments(
        IList<RubyTextFragment> fragments,
        Func<string, string> transform)
    {
        for (var index = 0; index < fragments.Count; index++)
            fragments[index] = fragments[index] with { Text = transform(fragments[index].Text) };
    }

    private static void TransformShapeText(
        Shape shape,
        Func<string, string> transform,
        Func<Run, bool>? transformRun = null)
    {
        foreach (var paragraph in shape.TextParagraphs)
            TransformParagraphText(paragraph, transform, transformRun);
    }

    private static void TransformSmartArtText(SmartArt smartArt, Func<string, string> transform)
    {
        foreach (var node in smartArt.Nodes)
            TransformSmartArtNodeText(node, transform);
    }

    private static void TransformSmartArtNodeText(SmartArtNode node, Func<string, string> transform)
    {
        node.Text = transform(node.Text);
        foreach (var child in node.Children)
            TransformSmartArtNodeText(child, transform);
    }

    private static void TransformChartText(Chart chart, Func<string, string> transform)
    {
        if (chart.Title is not null)
            chart.Title = transform(chart.Title);
        if (chart.CategoryAxisTitle is not null)
            chart.CategoryAxisTitle = transform(chart.CategoryAxisTitle);
        if (chart.ValueAxisTitle is not null)
            chart.ValueAxisTitle = transform(chart.ValueAxisTitle);
        for (var index = 0; index < chart.Categories.Count; index++)
            chart.Categories[index] = transform(chart.Categories[index]);
        foreach (var series in chart.Series)
            if (series.Name is not null)
                series.Name = transform(series.Name);
    }

    private static void TransformDrawingGroupText(
        DrawingGroup group,
        Func<string, string> transform,
        Func<Run, bool>? transformRun = null)
    {
        foreach (var child in group.Children)
        {
            switch (child)
            {
                case Shape shape:
                    TransformShapeText(shape, transform, transformRun);
                    break;
                case WordArt wordArt:
                    wordArt.Text = transform(wordArt.Text);
                    break;
                case SmartArt smartArt:
                    TransformSmartArtText(smartArt, transform);
                    break;
                case Chart chart:
                    TransformChartText(chart, transform);
                    break;
                case DrawingGroup nested:
                    TransformDrawingGroupText(nested, transform, transformRun);
                    break;
            }
        }
    }

    private static void TransformSectionHeadersFootersText(
        SectionHeadersFooters headersFooters,
        Func<string, string> transform,
        Func<Run, bool>? transformRun = null)
    {
        foreach (var headerFooter in new[]
                 {
                     headersFooters.Header,
                     headersFooters.Footer,
                     headersFooters.EvenHeader,
                     headersFooters.EvenFooter,
                     headersFooters.FirstHeader,
                     headersFooters.FirstFooter
                 })
        {
            if (headerFooter is null)
                continue;
            foreach (var paragraph in headerFooter.Paragraphs)
                TransformParagraphText(paragraph, transform, transformRun);
        }
    }

    private static HeaderFooter? CloneHeaderFooterWithRules(HeaderFooter? source, IReadOnlyDictionary<string, string> row, MergeState state, int recordIndex)
    {
        if (source is null)
            return null;
        var clone = new HeaderFooter();
        foreach (var p in source.Paragraphs)
            clone.Paragraphs.Add((Paragraph)CloneBlockWithRules(p, row, state, recordIndex));
        return clone;
    }

    private static HeaderFooter? CloneHeaderFooter(HeaderFooter? source, IReadOnlyDictionary<string, string> row)
    {
        if (source is null)
            return null;
        var clone = new HeaderFooter();
        foreach (var p in source.Paragraphs)
            clone.Paragraphs.Add((Paragraph)CloneBlock(p, row));
        return clone;
    }

    private static void CopyDocumentState(TextDocument source, TextDocument target, Func<Block, Block> cloneBlock)
    {
        target.Properties.ApplyCoreProperties(source.Properties.ToCoreProperties());
        target.MultiLevelList.SetNumberFormats(source.MultiLevelList.NumberFormats);
        CopyNoteNumbering(source.FootnoteNumbering, target.FootnoteNumbering);
        CopyNoteNumbering(source.EndnoteNumbering, target.EndnoteNumbering);

        foreach (var (id, footnote) in source.Footnotes)
        {
            var clone = new Footnote(id)
            {
                HasAutomaticReferenceMark = footnote.HasAutomaticReferenceMark
            };
            foreach (var paragraph in footnote.Content)
                clone.Content.Add((Paragraph)cloneBlock(paragraph));
            target.Footnotes[id] = clone;
        }

        foreach (var (id, endnote) in source.Endnotes)
        {
            var clone = new Endnote(id)
            {
                HasAutomaticReferenceMark = endnote.HasAutomaticReferenceMark
            };
            foreach (var paragraph in endnote.Content)
                clone.Content.Add((Paragraph)cloneBlock(paragraph));
            target.Endnotes[id] = clone;
        }

        foreach (var (id, comment) in source.Comments)
            target.Comments[id] = CloneComment(comment, cloneBlock);

        target.Sources.AddRange(source.Sources);
        target.IndexEntries.AddRange(source.IndexEntries.Select(entry => new IndexEntry(entry.Term)));
        target.Citations.AddRange(source.Citations.Select(citation =>
            new Citation(citation.LongCitation, citation.Category, citation.ShortCitation)));
        target.EmbeddedFonts.AddRange(source.EmbeddedFonts.Select(font => new EmbeddedFont(
            font.Family,
            CloneBytes(font.Regular),
            CloneBytes(font.Bold),
            CloneBytes(font.Italic),
            CloneBytes(font.BoldItalic))));
    }

    private static Comment CloneComment(Comment source, Func<Block, Block> cloneBlock)
    {
        var clone = new Comment(source.Id)
        {
            Author = source.Author,
            Initials = source.Initials,
            DateXml = source.DateXml,
            Resolved = source.Resolved
        };
        foreach (var paragraph in source.Content)
            clone.Content.Add((Paragraph)cloneBlock(paragraph));
        foreach (var reply in source.Replies)
            clone.Replies.Add(CloneComment(reply, cloneBlock));
        return clone;
    }

    private static void CopyNoteNumbering(NoteNumberingOptions source, NoteNumberingOptions target)
    {
        target.NumberFormat = source.NumberFormat;
        target.StartAt = source.StartAt;
        target.NumberRestart = source.NumberRestart;
    }

    private static byte[]? CloneBytes(byte[]? bytes) => bytes is null ? null : (byte[])bytes.Clone();

    private static SectionHeadersFooters CloneSectionHeadersFooters(
        SectionHeadersFooters source,
        Func<HeaderFooter?, HeaderFooter?> clone)
    {
        var target = new SectionHeadersFooters();
        CopySectionHeadersFooters(source, target, clone);
        return target;
    }

    private static void CopySectionHeadersFooters(
        SectionHeadersFooters source,
        SectionHeadersFooters target,
        Func<HeaderFooter?, HeaderFooter?> clone)
    {
        target.Header = clone(source.Header);
        target.Footer = clone(source.Footer);
        target.EvenHeader = clone(source.EvenHeader);
        target.EvenFooter = clone(source.EvenFooter);
        target.FirstHeader = clone(source.FirstHeader);
        target.FirstFooter = clone(source.FirstFooter);
    }

    private static void CopyPageSettings(PageSettings from, PageSettings to)
    {
        to.WidthPt = from.WidthPt;
        to.HeightPt = from.HeightPt;
        to.MarginLeftPt = from.MarginLeftPt;
        to.MarginRightPt = from.MarginRightPt;
        to.MarginTopPt = from.MarginTopPt;
        to.MarginBottomPt = from.MarginBottomPt;
        to.Landscape = from.Landscape;
        to.GutterPt = from.GutterPt;
        to.HeaderDistancePt = from.HeaderDistancePt;
        to.FooterDistancePt = from.FooterDistancePt;
        to.MirrorMargins = from.MirrorMargins;
        to.GutterAtTop = from.GutterAtTop;
        to.ColumnCount = from.ColumnCount;
        to.ColumnSpacingPt = from.ColumnSpacingPt;
        to.ColumnsLineBetween = from.ColumnsLineBetween;
        to.ColumnWidthsPt = from.ColumnWidthsPt is null ? null : new List<double>(from.ColumnWidthsPt);
        to.PageBorder = from.PageBorder;
        to.Watermark = from.Watermark;
        to.WatermarkOptions = PageSettings.CloneWatermarkOptions(from.WatermarkOptions);
        to.LineNumberMode = from.LineNumberMode;
        to.LineNumberCountBy = from.LineNumberCountBy;
        to.LineNumberStartAt = from.LineNumberStartAt;
        to.PageNumberFormat = from.PageNumberFormat;
        to.PageNumberStartAt = from.PageNumberStartAt;
        to.PageNumberChapterStyleLevel = from.PageNumberChapterStyleLevel;
        to.PageNumberChapterSeparator = from.PageNumberChapterSeparator;
        to.AutoHyphenation = from.AutoHyphenation;
        to.HyphenationZonePt = from.HyphenationZonePt;
        to.ConsecutiveHyphenLimit = from.ConsecutiveHyphenLimit;
        to.DoNotHyphenateCaps = from.DoNotHyphenateCaps;
        to.DefaultTabStopPt = from.DefaultTabStopPt;
        to.VerticalAlignment = from.VerticalAlignment;
        to.DifferentFirstPage = from.DifferentFirstPage;
        to.DifferentOddEvenPages = from.DifferentOddEvenPages;
        to.BackgroundColorHex = from.BackgroundColorHex;
    }
}
