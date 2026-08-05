using System.Globalization;

namespace FreeW.Core.Model;

/// <summary>
/// Pure, WPF-free recomputation of a <em>complex</em> Word field's result (the <c>w:fldChar</c>/
/// <c>w:instrText</c> construct carried by <see cref="Run.ComplexField"/>). This is the model-side engine
/// behind F9 / "Update Field": given a field instruction and the current document state it returns the
/// field's fresh result text. It complements <see cref="Run.ComplexField"/> (which only round-trips the
/// raw instruction and a cached result) by actually re-evaluating the instruction.
/// <para>
/// The engine resolves the reference/numbering field families FreeW already models but previously could
/// not refresh:
/// </para>
/// <list type="bullet">
/// <item><c>REF bookmark</c> — the text of the bookmarked paragraph (Word's cross-reference "Text").</item>
/// <item><c>PAGEREF bookmark</c> — the page the bookmarked paragraph sits on, via a caller-supplied page
/// map (the model has no pagination of its own); falls back to "1" when no page is known.</item>
/// <item><c>SEQ name</c> — the running counter for that sequence name (the basis of captions like
/// "Figure 1"/"Table 2"), counting how many earlier SEQ fields of the same name precede this one, with
/// support for the <c>\c</c> (repeat current), <c>\r N</c> (reset to N) and <c>\n</c>/<c>\h</c> switches.</item>
/// <item><c>STYLEREF 1</c> / <c>STYLEREF "Heading 1"</c> — the nearest preceding body paragraph using the
/// requested heading style.</item>
/// </list>
/// <para>
/// Lives in the model project so it is fully unit-testable without any UI. Deterministic and side-effect
/// free — it never mutates the document.
/// </para>
/// </summary>
public static class ComplexFieldEngine
{
    /// <summary>
    /// True when <paramref name="field"/> is a field family this engine can recompute
    /// (<c>REF</c>, <c>PAGEREF</c>, <c>SEQ</c>, <c>CITATION</c> or <c>STYLEREF</c>). Other keywords
    /// (PAGE/DATE/AUTHOR/…) are resolved elsewhere or left to their cached value, so the caller can
    /// cheaply skip them.
    /// </summary>
    public static bool CanRecompute(ComplexField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.Keyword is "REF" or "PAGEREF" or "SEQ" or "CITATION" or "STYLEREF";
    }

    /// <summary>
    /// Recomputes the result text of the complex field carried by the run at
    /// (<paramref name="blockIndex"/>, <paramref name="runIndex"/>) in <paramref name="document"/>, against
    /// the document's current bookmarks (REF/PAGEREF) and sequence counters (SEQ). Returns the run's
    /// existing <see cref="Run.Text"/> unchanged for fields this engine does not handle, for unresolvable
    /// references/style lookups, or for an empty instruction — so an F9 pass never blanks a field it cannot
    /// evaluate.
    /// </summary>
    /// <param name="document">The document whose current state the field resolves against.</param>
    /// <param name="blockIndex">Index of the field run's paragraph in <see cref="TextDocument.Blocks"/>.</param>
    /// <param name="runIndex">Index of the field run within its paragraph's <see cref="Paragraph.Runs"/>.</param>
    /// <param name="pageOf">
    /// Optional page-number resolver mapping a target body block index to its 1-based page (for PAGEREF).
    /// Null — or a null return — falls back to "1", since the pure model has no pagination.
    /// </param>
    /// <param name="pageTextOf">
    /// Optional formatted page-text resolver. A non-empty result is authoritative over
    /// <paramref name="pageOf"/> for section restarts and non-decimal page formats.
    /// </param>
    public static string Recompute(
        TextDocument document,
        int blockIndex,
        int runIndex,
        Func<int, int?>? pageOf = null,
        Func<int, string?>? pageTextOf = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (blockIndex < 0 || blockIndex >= document.Blocks.Count)
            return string.Empty;
        if (document.Blocks[blockIndex] is not Paragraph paragraph)
            return string.Empty;
        if (runIndex < 0 || runIndex >= paragraph.Runs.Count)
            return string.Empty;

        var run = paragraph.Runs[runIndex];
        if (run.ComplexField is not { } field)
            return run.Text;

        return field.Keyword switch
        {
            "REF" => ResolveRef(document, field, run.Text),
            "PAGEREF" => ResolvePageRef(document, field, run.Text, pageOf, pageTextOf),
            "SEQ" => ResolveSeq(document, field, blockIndex, runIndex),
            "CITATION" => Citations.ResolveCitationField(document, field, run.Text),
            "STYLEREF" => ResolveStyleRef(document, field, blockIndex, run.Text),
            _ => run.Text
        };
    }

    /// <summary>
    /// The first non-switch argument of <paramref name="instruction"/> after its leading keyword — e.g.
    /// the bookmark name of <c>REF MyMark \h</c> or the sequence name of <c>SEQ Figure \* ARABIC</c>.
    /// Honours simple double-quoting. Returns "" when the field has no argument.
    /// </summary>
    public static string Argument(string instruction)
    {
        foreach (var token in Tokenize(instruction))
        {
            if (token.StartsWith('\\'))
                break; // switches start here; the identifier (if any) comes before them
            return token;
        }
        return string.Empty;
    }

    /// <summary>
    /// Replaces the first non-switch argument after a field keyword while preserving the keyword,
    /// spacing, and all following switches. Returns the original instruction when it has no argument.
    /// </summary>
    internal static string ReplaceArgument(string instruction, string replacement)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        ArgumentNullException.ThrowIfNull(replacement);

        var cursor = 0;
        while (cursor < instruction.Length && char.IsWhiteSpace(instruction[cursor]))
            cursor++;
        while (cursor < instruction.Length && !char.IsWhiteSpace(instruction[cursor]))
            cursor++;
        while (cursor < instruction.Length && char.IsWhiteSpace(instruction[cursor]))
            cursor++;

        if (cursor >= instruction.Length || instruction[cursor] == '\\')
            return instruction;

        var argumentStart = cursor;
        if (instruction[cursor] == '"')
        {
            cursor++;
            var closed = false;
            while (cursor < instruction.Length)
            {
                if (instruction[cursor] == '\\' && cursor + 1 < instruction.Length)
                {
                    cursor += 2;
                    continue;
                }

                if (instruction[cursor++] == '"')
                {
                    closed = true;
                    break;
                }
            }

            if (!closed)
                return instruction;
        }
        else
        {
            while (cursor < instruction.Length && !char.IsWhiteSpace(instruction[cursor]))
                cursor++;
        }

        var quoted = replacement.Any(char.IsWhiteSpace) || replacement.Contains('"', StringComparison.Ordinal);
        var serialized = quoted
            ? "\"" + replacement.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : replacement;
        return instruction[..argumentStart] + serialized + instruction[cursor..];
    }

    /// <summary>
    /// True when <paramref name="instruction"/> carries the switch letter <paramref name="letter"/>
    /// (e.g. <c>'c'</c> for SEQ's <c>\c</c>), case-insensitively. The leading keyword/argument are skipped.
    /// </summary>
    public static bool HasSwitch(string instruction, char letter)
    {
        var target = char.ToUpperInvariant(letter);
        foreach (var token in Tokenize(instruction))
        {
            if (token.Length == 2 && token[0] == '\\' && char.ToUpperInvariant(token[1]) == target)
                return true;
        }
        return false;
    }

    /// <summary>
    /// The value following the switch <paramref name="letter"/> (e.g. the <c>N</c> of SEQ's <c>\r N</c>),
    /// or null when the switch is absent or has no following value. Honours double-quoting.
    /// </summary>
    public static string? SwitchValue(string instruction, char letter)
    {
        var target = char.ToUpperInvariant(letter);
        var tokens = Tokenize(instruction).ToList();
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Length == 2 && tokens[i][0] == '\\' && char.ToUpperInvariant(tokens[i][1]) == target)
                return i + 1 < tokens.Count && !tokens[i + 1].StartsWith('\\') ? tokens[i + 1] : null;
        }
        return null;
    }

    // REF: the text of the paragraph that carries the referenced bookmark, trimmed of trailing blanks.
    // Unresolvable (no such bookmark) falls back to the cached text so the field never blanks.
    private static string ResolveRef(TextDocument document, ComplexField field, string cached)
    {
        var name = Argument(field.Instruction);
        if (name.Length == 0)
            return cached;
        foreach (var location in Bookmarks.List(document))
        {
            if (string.Equals(location.Name, name, StringComparison.Ordinal)
                && document.Blocks[location.BlockIndex] is Paragraph target)
            {
                var text = target.PlainText.TrimEnd();
                return text.Length > 0 ? text : cached;
            }
        }
        return cached;
    }

    // PAGEREF: the page number of the referenced bookmark's paragraph, via the page resolver; "1" when no
    // page is known (the pure model has no pagination). Unresolvable bookmark falls back to cached text.
    private static string ResolvePageRef(
        TextDocument document,
        ComplexField field,
        string cached,
        Func<int, int?>? pageOf,
        Func<int, string?>? pageTextOf)
    {
        var name = Argument(field.Instruction);
        if (name.Length == 0)
            return cached;
        foreach (var location in Bookmarks.List(document))
        {
            if (string.Equals(location.Name, name, StringComparison.Ordinal))
            {
                if (pageTextOf?.Invoke(location.BlockIndex) is { Length: > 0 } pageText)
                    return pageText;

                var page = pageOf?.Invoke(location.BlockIndex);
                return (page ?? 1).ToString(CultureInfo.InvariantCulture);
            }
        }
        return cached;
    }

    // SEQ: the running counter for this sequence name. The number is one more than the count of earlier
    // SEQ fields of the same name in document order, honouring \r N (reset the running value to N at this
    // field) and \c (repeat the current value rather than advancing). The \n/\h switches hide the result.
    private static string ResolveSeq(TextDocument document, ComplexField field, int blockIndex, int runIndex)
    {
        var name = Argument(field.Instruction);
        if (name.Length == 0)
            return string.Empty;
        // \h (hidden) and \n (no number) suppress the printed value but still advance the counter for
        // following fields; the value at this position is empty.
        var hidden = HasSwitch(field.Instruction, 'h') || HasSwitch(field.Instruction, 'n');

        var value = 0;
        var blocks = document.Blocks;
        for (var b = 0; b < blocks.Count; b++)
        {
            if (blocks[b] is not Paragraph paragraph)
                continue;
            for (var r = 0; r < paragraph.Runs.Count; r++)
            {
                if (paragraph.Runs[r].ComplexField is not { } cf
                    || cf.Keyword != "SEQ"
                    || !string.Equals(Argument(cf.Instruction), name, StringComparison.Ordinal))
                    continue;

                var resetTo = SeqReset(cf.Instruction);
                var repeat = HasSwitch(cf.Instruction, 'c');
                if (resetTo is { } reset)
                    value = reset;             // \r N restarts the running value at N for this field
                else if (!repeat)
                    value++;                   // ordinary SEQ advances; \c repeats the current value

                if (b == blockIndex && r == runIndex)
                    return hidden ? string.Empty : value.ToString(CultureInfo.InvariantCulture);
            }
        }
        // The target field was not found among the document's SEQ fields (shouldn't happen for an in-doc
        // field): fall back to a bare first ordinal.
        return hidden ? string.Empty : "1";
    }

    // The integer reset value of a SEQ \r switch (e.g. "\r 5" → 5), or null when absent/unparseable.
    private static int? SeqReset(string instruction) =>
        SwitchValue(instruction, 'r') is { } raw
        && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : (int?)null;

    // STYLEREF: nearest preceding body paragraph matching the requested style. This bounded slice covers
    // Word's common heading-reference form; page-aware/header-footer behavior and switches remain cached.
    private static string ResolveStyleRef(TextDocument document, ComplexField field, int blockIndex, string cached)
    {
        var argument = Argument(field.Instruction);
        if (argument.Length == 0)
            return cached;

        var headingStyleId = argument.Length == 1 && argument[0] is >= '1' and <= '9'
            ? "Heading" + argument
            : null;

        for (var b = Math.Min(blockIndex - 1, document.Blocks.Count - 1); b >= 0; b--)
        {
            if (document.Blocks[b] is not Paragraph paragraph
                || !StyleRefMatches(document, paragraph, argument, headingStyleId))
                continue;

            var text = paragraph.PlainText.TrimEnd();
            return text.Length > 0 ? text : cached;
        }

        return cached;
    }

    private static bool StyleRefMatches(
        TextDocument document, Paragraph paragraph, string argument, string? headingStyleId)
    {
        if (paragraph.StyleId is not { Length: > 0 } styleId)
            return false;

        if (headingStyleId is not null)
            return string.Equals(styleId, headingStyleId, StringComparison.OrdinalIgnoreCase);

        if (string.Equals(styleId, argument, StringComparison.OrdinalIgnoreCase))
            return true;

        return document.Styles.TryGetValue(styleId, out var style)
            && string.Equals(style.Name, argument, StringComparison.OrdinalIgnoreCase);
    }

    internal static string? FirstArgument(string instruction) =>
        Tokenize(instruction).FirstOrDefault(token => !token.StartsWith('\\'));

    // Splits a field instruction into whitespace-separated tokens, skipping the leading keyword, honouring
    // double-quoted spans (so a quoted argument with spaces stays one token) and splitting a "\x" switch
    // letter from a following value. The leading keyword is dropped so callers see only argument/switches.
    private static IEnumerable<string> Tokenize(string instruction)
    {
        var text = instruction.Trim();
        var i = 0;
        var first = true;
        while (i < text.Length)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i]))
                i++;
            if (i >= text.Length)
                yield break;

            string token;
            if (text[i] == '"')
            {
                var value = new System.Text.StringBuilder();
                i++;
                while (i < text.Length)
                {
                    if (text[i] == '\\' && i + 1 < text.Length)
                    {
                        value.Append(text[i + 1]);
                        i += 2;
                        continue;
                    }

                    if (text[i] == '"')
                    {
                        i++;
                        break;
                    }

                    value.Append(text[i]);
                    i++;
                }

                token = value.ToString();
            }
            else
            {
                var start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '"')
                    i++;
                token = text[start..i];
            }

            if (first)
            {
                first = false; // drop the leading keyword (REF/PAGEREF/SEQ/…)
                continue;
            }
            yield return token;
        }
    }
}
