using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Framework-neutral rich-text transaction used by in-canvas editors. UI controls own caret,
/// selection, IME, and clipboard behavior; this buffer preserves the presentation text model
/// while the edit remains local and produces one rich body for commit.
/// </summary>
public sealed class InCanvasRichTextEditBuffer
{
    private TextBody _body;
    private Run? _typingRun;
    private int? _typingCaret;

    public InCanvasRichTextEditBuffer(TextBody? body)
    {
        _body = TextBodyModelCloner.CloneTextBody(body) ?? CreateEmptyBody();
    }

    public TextBody Body => TextBodyModelCloner.CloneTextBody(_body)!;

    public string PlainText => InCanvasTextEditPlanner.ExtractPlainText(_body);

    public InCanvasTableCellRichTextEditPlan Plan(InCanvasEditorTextSelection selection)
    {
        var plan = TableCellEditPlanner.PlanRichTextEdit(_body, selection);
        if (!selection.IsCollapsed
            || _typingRun is null
            || _typingCaret != ClampCaret(selection.Start))
            return plan;

        var typingStyle = BuildStyleState(_typingRun);
        return plan with
        {
            SuggestedEditorStyle = typingStyle,
            InitialSelectionStyle = typingStyle,
        };
    }

    public void ReplacePlainText(string? editedText)
    {
        string normalized = NormalizeNewlines(editedText ?? string.Empty);
        string original = PlainText;
        if (StringComparer.Ordinal.Equals(original, normalized))
            return;

        int prefix = CommonPrefixLength(original, normalized);
        int suffix = CommonSuffixLength(original, normalized, prefix);
        int removedLength = original.Length - prefix - suffix;
        string insertedText = normalized.Substring(prefix, normalized.Length - prefix - suffix);

        bool useTypingStyle = insertedText.Length > 0
            && removedLength == 0
            && _typingRun is not null
            && _typingCaret == prefix;
        _body = RichTextBodyMutationPlanner.Replace(
            _body,
            prefix,
            removedLength,
            insertedText,
            useTypingStyle ? _typingRun : null);

        if (useTypingStyle)
        {
            _typingCaret = prefix + insertedText.Length;
        }
        else
        {
            _typingRun = null;
            _typingCaret = null;
        }
    }

    public bool ToggleTextFormat(
        TableCellTextFormatKind kind,
        InCanvasEditorTextSelection selection)
    {
        if (!TextBodyRunMutationPlanner.HasTextRuns(_body))
            return false;

        if (selection.IsCollapsed)
        {
            var typingBody = CreateTypingBody(EnsureTypingRun(selection.Start));
            typingBody = TextBodyRunMutationPlanner.ToggleTextFormat(
                typingBody,
                kind,
                selection: null,
                out _);
            _typingRun = typingBody.Paragraphs[0].Runs[0];
            return true;
        }

        ClearTypingStyle();
        _body = TextBodyRunMutationPlanner.ToggleTextFormat(
            _body,
            kind,
            NormalizeSelection(selection),
            out _);
        return true;
    }

    public bool ApplyValueFormat(
        TableCellTextValueFormatKind kind,
        object? value,
        InCanvasEditorTextSelection selection)
    {
        if (!TextBodyRunMutationPlanner.HasTextRuns(_body))
            return false;

        if (selection.IsCollapsed)
        {
            var typingBody = CreateTypingBody(EnsureTypingRun(selection.Start));
            typingBody = TextBodyRunMutationPlanner.ApplyValueFormat(
                typingBody,
                kind,
                value,
                selection: null);
            _typingRun = typingBody.Paragraphs[0].Runs[0];
            return true;
        }

        ClearTypingStyle();
        _body = TextBodyRunMutationPlanner.ApplyValueFormat(
            _body,
            kind,
            value,
            NormalizeSelection(selection));
        return true;
    }

    public bool ApplyParagraphAlignment(TextAlign alignment, InCanvasEditorTextSelection selection) =>
        ApplyParagraphMutation(body => TableCellEditPlanner.ApplyParagraphAlignmentToBody(
            body,
            alignment,
            NormalizeParagraphSelection(selection)));

    public bool ToggleParagraphBullets(InCanvasEditorTextSelection selection) =>
        ApplyParagraphMutation(body => TableCellEditPlanner.ApplyParagraphBulletToggleToBody(
            body,
            NormalizeParagraphSelection(selection)));

    public bool ToggleParagraphNumbering(InCanvasEditorTextSelection selection) =>
        ApplyParagraphMutation(body => TableCellEditPlanner.ApplyParagraphNumberingToggleToBody(
            body,
            NormalizeParagraphSelection(selection)));

    public bool ApplyParagraphListPreset(
        TableCellListPresetDescriptor preset,
        InCanvasEditorTextSelection selection)
    {
        ArgumentNullException.ThrowIfNull(preset);
        return ApplyParagraphMutation(body => TableCellEditPlanner.ApplyParagraphListPresetToBody(
            body,
            NormalizeParagraphSelection(selection),
            preset));
    }

    public bool ApplyParagraphPictureBullet(
        PresentationPictureBulletPayload payload,
        InCanvasEditorTextSelection selection)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (!payload.IsValid)
            return false;

        return ApplyParagraphMutation(body => TableCellEditPlanner.ApplyParagraphPictureBulletToBody(
            body,
            NormalizeParagraphSelection(selection),
            PresentationPictureBulletAuthoringPlanner.CreateImagePart(payload)));
    }

    public bool ApplyParagraphIndent(bool increase, InCanvasEditorTextSelection selection) =>
        ApplyParagraphMutation(body => TableCellEditPlanner.ApplyParagraphIndentToBody(
            body,
            increase,
            NormalizeParagraphSelection(selection)));

    private bool ApplyParagraphMutation(Func<TextBody, TextBody> mutate)
    {
        if (_body.Paragraphs.Count == 0)
            return false;

        _body = mutate(_body);
        return true;
    }

    private Run EnsureTypingRun(int caret)
    {
        int clampedCaret = ClampCaret(caret);
        if (_typingRun is not null && _typingCaret == clampedCaret)
            return _typingRun;

        _typingCaret = clampedCaret;
        _typingRun = RichTextBodyMutationPlanner.ResolveRunAtCaret(_body, clampedCaret);
        return _typingRun;
    }

    private int ClampCaret(int caret) => Math.Clamp(caret, 0, PlainText.Length);

    private void ClearTypingStyle()
    {
        _typingRun = null;
        _typingCaret = null;
    }

    private static TextBody CreateTypingBody(Run run)
    {
        var body = new TextBody();
        body.Paragraphs.Add(new Paragraph { Runs = { TextBodyModelCloner.CloneRun(run) } });
        return body;
    }

    private static InCanvasEditorTextStyleState BuildStyleState(Run run) => new(
        run.FontFamily,
        run.FontSizePt,
        run.Bold,
        run.Italic,
        run.Underline,
        run.Strikethrough,
        run.Color);

    private (int Start, int End)? NormalizeSelection(InCanvasEditorTextSelection selection)
    {
        int textLength = PlainText.Length;
        int start = Math.Clamp(Math.Min(selection.Start, selection.End), 0, textLength);
        int end = Math.Clamp(Math.Max(selection.Start, selection.End), 0, textLength);
        return end > start ? (start, end) : null;
    }

    private (int Start, int End)? NormalizeParagraphSelection(InCanvasEditorTextSelection selection)
    {
        var range = NormalizeSelection(selection);
        if (range is not null)
            return range;

        int textLength = PlainText.Length;
        if (textLength == 0)
            return null;

        int caret = Math.Clamp(selection.Start, 0, textLength);
        return caret < textLength ? (caret, caret + 1) : (textLength - 1, textLength);
    }

    private static int CommonPrefixLength(string left, string right)
    {
        int limit = Math.Min(left.Length, right.Length);
        int index = 0;
        while (index < limit && left[index] == right[index])
            index++;
        return index;
    }

    private static int CommonSuffixLength(string left, string right, int prefixLength)
    {
        int limit = Math.Min(left.Length, right.Length) - prefixLength;
        int count = 0;
        while (count < limit && left[left.Length - count - 1] == right[right.Length - count - 1])
            count++;
        return count;
    }

    private static string NormalizeNewlines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static TextBody CreateEmptyBody()
    {
        var body = new TextBody();
        body.Paragraphs.Add(new Paragraph { Runs = { new Run() } });
        return body;
    }
}

internal static class RichTextBodyMutationPlanner
{
    private sealed record Token(char? Character, Run? RunTemplate, Paragraph? NextParagraphTemplate)
    {
        public bool IsBreak => Character is null;
    }

    internal static TextBody Replace(
        TextBody source,
        int start,
        int removedLength,
        string insertedText,
        Run? insertionRunOverride = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(insertedText);

        var working = TextBodyModelCloner.CloneTextBody(source) ?? new TextBody();
        EnsureParagraph(working);

        var tokens = Flatten(working);
        int clampedStart = Math.Clamp(start, 0, tokens.Count);
        int clampedLength = Math.Clamp(removedLength, 0, tokens.Count - clampedStart);
        var insertionRun = insertionRunOverride is null
            ? ResolveInsertionRun(working, tokens, clampedStart, clampedLength)
            : TextBodyModelCloner.CloneRun(insertionRunOverride);
        var insertionParagraph = ResolveInsertionParagraph(working, tokens, clampedStart);

        tokens.RemoveRange(clampedStart, clampedLength);
        tokens.InsertRange(
            clampedStart,
            BuildInsertionTokens(insertedText, insertionRun, insertionParagraph));

        return Rebuild(working, tokens);
    }

    internal static Run ResolveRunAtCaret(TextBody source, int caret)
    {
        ArgumentNullException.ThrowIfNull(source);
        var working = TextBodyModelCloner.CloneTextBody(source) ?? new TextBody();
        EnsureParagraph(working);
        var tokens = Flatten(working);
        int clampedCaret = Math.Clamp(caret, 0, tokens.Count);
        return TextBodyModelCloner.CloneRun(
            ResolveInsertionRun(working, tokens, clampedCaret, removedLength: 0));
    }

    private static List<Token> Flatten(TextBody body)
    {
        var tokens = new List<Token>();
        for (int paragraphIndex = 0; paragraphIndex < body.Paragraphs.Count; paragraphIndex++)
        {
            var paragraph = body.Paragraphs[paragraphIndex];
            foreach (var run in paragraph.Runs)
            {
                foreach (char character in run.Text)
                    tokens.Add(new Token(character, run, null));
            }

            if (paragraphIndex + 1 < body.Paragraphs.Count)
                tokens.Add(new Token(null, null, body.Paragraphs[paragraphIndex + 1]));
        }
        return tokens;
    }

    private static Run ResolveInsertionRun(
        TextBody body,
        IReadOnlyList<Token> tokens,
        int start,
        int removedLength)
    {
        if (removedLength > 0 && start < tokens.Count && tokens[start].RunTemplate is { } selectedRun)
            return selectedRun;

        for (int index = start - 1; index >= 0; index--)
        {
            if (tokens[index].RunTemplate is { } precedingRun)
                return precedingRun;
        }

        for (int index = start; index < tokens.Count; index++)
        {
            if (tokens[index].RunTemplate is { } followingRun)
                return followingRun;
        }

        return body.Paragraphs.SelectMany(paragraph => paragraph.Runs).FirstOrDefault() ?? new Run();
    }

    private static Paragraph ResolveInsertionParagraph(
        TextBody body,
        IReadOnlyList<Token> tokens,
        int start)
    {
        var paragraph = body.Paragraphs[0];
        for (int index = 0; index < start && index < tokens.Count; index++)
        {
            if (tokens[index].NextParagraphTemplate is { } next)
                paragraph = next;
        }
        return paragraph;
    }

    private static IEnumerable<Token> BuildInsertionTokens(
        string insertedText,
        Run runTemplate,
        Paragraph paragraphTemplate)
    {
        foreach (char character in insertedText)
        {
            yield return character == '\n'
                ? new Token(null, null, paragraphTemplate)
                : new Token(character, runTemplate, null);
        }
    }

    private static TextBody Rebuild(TextBody source, IReadOnlyList<Token> tokens)
    {
        var result = TextBodyModelCloner.CloneTextBody(source)!;
        var firstTemplate = source.Paragraphs[0];
        result.Paragraphs.Clear();

        var paragraph = CloneParagraphWithoutRuns(firstTemplate);
        Run? activeTemplate = null;
        var activeText = new System.Text.StringBuilder();

        void FlushRun()
        {
            if (activeTemplate is null || activeText.Length == 0)
                return;
            var run = TextBodyModelCloner.CloneRun(activeTemplate);
            run.Text = activeText.ToString();
            paragraph.Runs.Add(run);
            activeTemplate = null;
            activeText.Clear();
        }

        void FlushParagraph(Paragraph? nextTemplate)
        {
            FlushRun();
            EnsureRun(paragraph, activeTemplate ?? firstTemplate.Runs.FirstOrDefault());
            result.Paragraphs.Add(paragraph);
            paragraph = CloneParagraphWithoutRuns(nextTemplate ?? firstTemplate);
        }

        foreach (var token in tokens)
        {
            if (token.IsBreak)
            {
                FlushParagraph(token.NextParagraphTemplate);
                continue;
            }

            if (!ReferenceEquals(activeTemplate, token.RunTemplate))
            {
                FlushRun();
                activeTemplate = token.RunTemplate;
            }
            activeText.Append(token.Character!.Value);
        }

        FlushRun();
        EnsureRun(paragraph, activeTemplate ?? firstTemplate.Runs.FirstOrDefault());
        result.Paragraphs.Add(paragraph);

        return result;
    }

    private static Paragraph CloneParagraphWithoutRuns(Paragraph source)
    {
        var paragraph = TextBodyModelCloner.CloneParagraph(source);
        paragraph.Runs.Clear();
        return paragraph;
    }

    private static void EnsureParagraph(TextBody body)
    {
        if (body.Paragraphs.Count == 0)
            body.Paragraphs.Add(new Paragraph { Runs = { new Run() } });
    }

    private static void EnsureRun(Paragraph paragraph, Run? template)
    {
        if (paragraph.Runs.Count > 0)
            return;
        var run = template is null ? new Run() : TextBodyModelCloner.CloneRun(template);
        run.Text = string.Empty;
        paragraph.Runs.Add(run);
    }
}
