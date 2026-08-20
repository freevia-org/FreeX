using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Framework-neutral rich-text transaction used by in-canvas editors. UI controls own caret,
/// selection, IME, and clipboard behavior; this buffer preserves the presentation text model
/// while the edit remains local and produces one rich body for commit.
/// </summary>
public class InCanvasRichTextEditBuffer
{
    private TextBody _body;
    private Run? _typingRun;
    private int? _typingCaret;

    public InCanvasRichTextEditBuffer(TextBody? body)
    {
        _body = TextBodyModelCloner.CloneTextBody(body) ?? CreateEmptyBody();
    }

    protected void ResetBody(TextBody? body)
    {
        _body = TextBodyModelCloner.CloneTextBody(body) ?? CreateEmptyBody();
        _typingRun = null;
        _typingCaret = null;
    }

    public TextBody Body => TextBodyModelCloner.CloneTextBody(_body)!;

    public string PlainText => InCanvasTextEditPlanner.ExtractPlainText(_body);

    /// <summary>
    /// Returns the inline OLE payload at a logical text position without cloning it.
    /// Hosts use this only for activation; the edit buffer remains the owner so any
    /// bytes written back by the external application are committed with the edit.
    /// </summary>
    public bool TryGetInlineOleObjectAt(int logicalPosition, out InlineOleObjectInfo? inlineObject)
        => FindInlineOleObjectAt(_body, logicalPosition, out inlineObject);

    /// <summary>
    /// Finds an inline OLE payload in an existing text body without cloning it.
    /// Activation uses this overload so external edits update the live shape model.
    /// </summary>
    public static bool FindInlineOleObjectAt(
        TextBody? body,
        int logicalPosition,
        out InlineOleObjectInfo? inlineObject)
    {
        if (body is null)
        {
            inlineObject = null;
            return false;
        }

        int position = 0;
        foreach (var paragraph in body.Paragraphs)
        {
            foreach (var run in paragraph.Runs)
            {
                int length = run.Text?.Length ?? 0;
                if (run.InlineOleObject is not null
                    && logicalPosition >= position
                    && logicalPosition < position + Math.Max(1, length))
                {
                    inlineObject = run.InlineOleObject;
                    return true;
                }

                position += length;
            }

            position++;
        }

        inlineObject = null;
        return false;
    }

    /// <summary>
    /// Locates the logical text position of one inline OLE payload inside <paramref name="body"/>.
    /// The scan mirrors <see cref="FindInlineOleObjectAt"/> exactly, so a position reported here
    /// resolves back to the same run when looked up in a structurally identical body -- which is
    /// how an in-place host, holding only the payload it was handed, addresses the matching
    /// payload in the live shape model.
    /// </summary>
    public static bool TryFindInlineOleObjectPosition(
        TextBody? body,
        InlineOleObjectInfo? inlineObject,
        out int logicalPosition)
    {
        logicalPosition = -1;
        if (body is null || inlineObject is null)
            return false;

        int position = 0;
        foreach (var paragraph in body.Paragraphs)
        {
            foreach (var run in paragraph.Runs)
            {
                if (ReferenceEquals(run.InlineOleObject, inlineObject))
                {
                    logicalPosition = position;
                    return true;
                }

                position += run.Text?.Length ?? 0;
            }

            position++;
        }

        return false;
    }

    /// <summary>
    /// Writes bytes an OLE server produced onto the inline payload at a logical position.
    /// When <paramref name="expected"/> is supplied the write only happens if the payload found
    /// there is still the same embedded object (same file name and class), so a commit that
    /// arrives after the text around it changed can never overwrite an unrelated object.
    /// </summary>
    public static bool TryCommitInlineOlePayload(
        TextBody? body,
        int logicalPosition,
        IReadOnlyList<byte> embeddedBytes,
        InlineOleObjectInfo? expected = null)
    {
        ArgumentNullException.ThrowIfNull(embeddedBytes);
        if (embeddedBytes.Count == 0
            || !FindInlineOleObjectAt(body, logicalPosition, out var target)
            || target is null)
            return false;

        if (expected is not null
            && (!string.Equals(target.FileName, expected.FileName, StringComparison.Ordinal)
                || !string.Equals(target.ClassName, expected.ClassName, StringComparison.Ordinal)))
            return false;

        target.EmbeddedBytes = embeddedBytes.ToArray();
        return true;
    }

    /// <summary>Refreshes one local inline OLE snapshot after external activation saves it.</summary>
    public bool UpdateInlineOleObjectAt(
        int logicalPosition,
        IReadOnlyList<byte> embeddedBytes)
    {
        ArgumentNullException.ThrowIfNull(embeddedBytes);
        if (!TryGetInlineOleObjectAt(logicalPosition, out var inlineObject)
            || inlineObject is null)
            return false;

        inlineObject.EmbeddedBytes = embeddedBytes.ToArray();
        return true;
    }

    /// <summary>
    /// Commits a nested rich-text editor back to one inline table cell. The logical position
    /// identifies the owning inline table marker in the current body, so nested editors can
    /// commit their own body without replacing the parent table run.
    /// </summary>
    public bool UpdateInlineTableCellAt(
        int logicalPosition,
        int rowIndex,
        int columnIndex,
        TextBody editedBody)
    {
        ArgumentNullException.ThrowIfNull(editedBody);
        if (!TryGetInlineTableAt(logicalPosition, out var table)
            || table is null)
            return false;

        var row = table.Rows.ElementAtOrDefault(rowIndex);
        var cell = row?.Cells.ElementAtOrDefault(columnIndex);
        if (cell is null)
            return false;

        cell.TextBody = TextBodyModelCloner.CloneTextBody(editedBody);
        return true;
    }

    private bool TryGetInlineTableAt(int logicalPosition, out TableShape? table)
    {
        int position = 0;
        foreach (var paragraph in _body.Paragraphs)
        {
            foreach (var run in paragraph.Runs)
            {
                int length = Math.Max(1, run.Text?.Length ?? 0);
                if (run.InlineTable is { } inlineTable
                    && logicalPosition >= position
                    && logicalPosition < position + length)
                {
                    table = inlineTable.Table;
                    return true;
                }

                position += run.Text?.Length ?? 0;
            }

            position++;
        }

        table = null;
        return false;
    }

    public InCanvasRichClipboardPayload CreateClipboardPayload(
        InCanvasEditorTextSelection selection) =>
        InCanvasRichClipboardPlanner.Capture(_body, selection, _typingRun);

    public bool ApplyClipboardPayload(
        InCanvasRichClipboardPayload payload,
        InCanvasEditorTextSelection selection,
        out int caret)
    {
        ArgumentNullException.ThrowIfNull(payload);
        _body = InCanvasRichClipboardPlanner.Apply(_body, selection, payload, out caret);
        _typingRun = payload.TypingRun is null
            ? null
            : TextBodyModelCloner.CloneRun(payload.TypingRun);
        _typingCaret = caret;
        return payload.PlainText.Length > 0 || !selection.IsCollapsed;
    }

    public bool ReplaceSelectionWithPlainText(
        InCanvasEditorTextSelection selection,
        string? insertedText,
        out int caret)
    {
        string normalized = NormalizeNewlines(insertedText ?? string.Empty);
        int textLength = PlainText.Length;
        int start = Math.Clamp(Math.Min(selection.Start, selection.End), 0, textLength);
        int end = Math.Clamp(Math.Max(selection.Start, selection.End), 0, textLength);
        int removedLength = end - start;
        var typingRun = selection.IsCollapsed && _typingRun is not null && _typingCaret == start
            ? TextBodyModelCloner.CloneRun(_typingRun)
            : null;

        _body = RichTextBodyMutationPlanner.Replace(
            _body,
            start,
            removedLength,
            normalized,
            typingRun);
        caret = start + normalized.Length;
        _typingRun = typingRun;
        _typingCaret = typingRun is null ? null : caret;
        return removedLength > 0 || normalized.Length > 0;
    }

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

    /// <summary>
    /// Inserts a soft line break at the current selection. Unlike a normal newline,
    /// this keeps the text in the current paragraph and is persisted as the model's
    /// dedicated <c>Run.Text == "\n"</c> break run.
    /// </summary>
    public bool InsertSoftBreak(InCanvasEditorTextSelection selection)
    {
        int textLength = PlainText.Length;
        int start = Math.Clamp(Math.Min(selection.Start, selection.End), 0, textLength);
        int end = Math.Clamp(Math.Max(selection.Start, selection.End), 0, textLength);
        ClearTypingStyle();
        _body = RichTextBodyMutationPlanner.InsertSoftBreak(
            _body,
            start,
            end - start);
        return true;
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

    public Hyperlink? GetSelectedRunHyperlink(InCanvasEditorTextSelection selection) =>
        InCanvasTextEditPlanner.GetSelectedRunHyperlink(_body, NormalizeSelection(selection));

    public bool ApplyHyperlink(
        Hyperlink? hyperlink,
        InCanvasEditorTextSelection selection)
    {
        if (!TextBodyRunMutationPlanner.HasTextRuns(_body))
            return false;

        ClearTypingStyle();
        _body = InCanvasTextEditPlanner.ApplySelectedRunHyperlink(
            _body,
            hyperlink,
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
        => TableCellEditPlanner.NormalizeParagraphSelection(
            (selection.Start, selection.End),
            PlainText.Length);

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
    private sealed record Token(
        char? Character,
        Run? RunTemplate,
        Paragraph? NextParagraphTemplate,
        bool IsInsertedBreak = false,
        bool IsSoftBreak = false)
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

    internal static TextBody ReplaceWithFragment(
        TextBody source,
        int start,
        int removedLength,
        TextBody fragment)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(fragment);

        var working = TextBodyModelCloner.CloneTextBody(source) ?? new TextBody();
        EnsureParagraph(working);
        var pasted = TextBodyModelCloner.CloneTextBody(fragment) ?? new TextBody();
        EnsureParagraph(pasted);

        var tokens = Flatten(working);
        var fragmentTokens = Flatten(pasted);
        int clampedStart = Math.Clamp(start, 0, tokens.Count);
        int clampedLength = Math.Clamp(removedLength, 0, tokens.Count - clampedStart);

        tokens.RemoveRange(clampedStart, clampedLength);
        tokens.InsertRange(clampedStart, fragmentTokens);
        return Rebuild(working, tokens, pasted.Paragraphs[0]);
    }

    internal static TextBody InsertSoftBreak(
        TextBody source,
        int start,
        int removedLength)
    {
        ArgumentNullException.ThrowIfNull(source);

        var working = TextBodyModelCloner.CloneTextBody(source) ?? new TextBody();
        EnsureParagraph(working);

        var tokens = Flatten(working);
        int clampedStart = Math.Clamp(start, 0, tokens.Count);
        int clampedLength = Math.Clamp(removedLength, 0, tokens.Count - clampedStart);
        var insertionRun = ResolveInsertionRun(working, tokens, clampedStart, clampedLength);

        tokens.RemoveRange(clampedStart, clampedLength);
        tokens.Insert(
            clampedStart,
            new Token('\n', insertionRun, null, IsSoftBreak: true));

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
                {
                    tokens.Add(character == '\n'
                        ? new Token(character, run, null, IsSoftBreak: true)
                        : new Token(character, run, null));
                }
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
        {
            if (!tokens[start].IsSoftBreak)
                return selectedRun;
        }

        for (int index = start - 1; index >= 0; index--)
        {
            if (!tokens[index].IsSoftBreak
                && tokens[index].RunTemplate is { } precedingRun)
                return precedingRun;
        }

        for (int index = start; index < tokens.Count; index++)
        {
            if (!tokens[index].IsSoftBreak
                && tokens[index].RunTemplate is { } followingRun)
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
                ? new Token(null, null, paragraphTemplate, IsInsertedBreak: true)
                : new Token(character, runTemplate, null);
        }
    }

    private static TextBody Rebuild(
        TextBody source,
        IReadOnlyList<Token> tokens,
        Paragraph? initialParagraphTemplate = null)
    {
        var result = TextBodyModelCloner.CloneTextBody(source)!;
        var firstTemplate = initialParagraphTemplate ?? source.Paragraphs[0];
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

        void FlushParagraph(Paragraph? nextTemplate, bool clearAutoNumStartAtSpecified)
        {
            FlushRun();
            EnsureRun(paragraph, activeTemplate ?? firstTemplate.Runs.FirstOrDefault());
            result.Paragraphs.Add(paragraph);
            paragraph = CloneParagraphWithoutRuns(
                nextTemplate ?? firstTemplate,
                clearAutoNumStartAtSpecified);
        }

        foreach (var token in tokens)
        {
            if (token.IsBreak)
            {
                FlushParagraph(token.NextParagraphTemplate, token.IsInsertedBreak);
                continue;
            }

            if (token.IsSoftBreak)
            {
                FlushRun();
                var softBreakRun = token.RunTemplate is null
                    ? new Run()
                    : TextBodyModelCloner.CloneRun(token.RunTemplate);
                softBreakRun.Text = "\n";
                paragraph.Runs.Add(softBreakRun);
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

    private static Paragraph CloneParagraphWithoutRuns(
        Paragraph source,
        bool clearAutoNumStartAtSpecified = false)
    {
        var paragraph = TextBodyModelCloner.CloneParagraphMetadata(
            source,
            clearAutoNumStartAtSpecified);
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
